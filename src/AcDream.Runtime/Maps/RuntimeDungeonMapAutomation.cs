using System.Numerics;
using AcDream.Core.Content;
using AcDream.Plugin.Abstractions;
using AcDream.Runtime.Gameplay;
using AcDream.Runtime.Navigation;
using DatReaderWriter.DBObjs;
using DatReaderWriter.Enums;
using DatReaderWriter.Types;

namespace AcDream.Runtime.Maps;

/// <summary>
/// The plugin dungeon-map API over one game runtime, handed out by every
/// host: which landblock the character stands in, whether a cell is a sealed
/// dungeon's, and a landblock's floorplan. A host binds the runtime and the
/// game files as they come to exist; until each is bound the members that
/// need it answer the inert defaults. The plans are data that never changes,
/// so each is projected to plugin records once and handed out unchanged.
/// </summary>
internal sealed class RuntimeDungeonMapAutomation : IDungeonMapAutomation
{
    private readonly object _gate = new();
    private readonly Dictionary<uint, PluginDungeonFloorplan> _projected = new();
    /// <summary>
    /// How many landblocks' indoor cells are kept; the landblock read
    /// longest ago goes first once it is full.
    /// </summary>
    internal const int MaximumCachedIndoorLandblocks = 64;

    private readonly Dictionary<uint, IReadOnlyList<PluginIndoorCell>> _indoorCells = new();
    private readonly Queue<uint> _indoorCellOrder = new();
    private GameRuntime? _runtime;
    private IDatObjectSource? _content;
    private object? _contentLock;
    private DungeonFloorplanBuilder? _builder;

    public void Bind(GameRuntime runtime)
    {
        ArgumentNullException.ThrowIfNull(runtime);
        lock (_gate)
            _runtime = runtime;
    }

    public void BindContent(IDatObjectSource content, object contentLock)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(contentLock);
        lock (_gate)
        {
            if (ReferenceEquals(_content, content) && ReferenceEquals(_contentLock, contentLock))
                return;
            _content = content;
            _contentLock = contentLock;
            _builder = new DungeonFloorplanBuilder(content, contentLock);
            _projected.Clear();
            _indoorCells.Clear();
            _indoorCellOrder.Clear();
        }
    }

    public bool IsSealedDungeon(uint cellId)
    {
        IDatObjectSource? content;
        object? contentLock;
        lock (_gate)
        {
            content = _content;
            contentLock = _contentLock;
        }
        return content is not null
            && contentLock is not null
            && SealedDungeonCells.IsSealedDungeon(content, contentLock, cellId);
    }

    /// <summary>
    /// The landblock of the character's body, which every client carries
    /// between the server's updates; zero until there is a body.
    /// </summary>
    public uint CurrentLandblockId
    {
        get
        {
            GameRuntime? runtime;
            lock (_gate)
                runtime = _runtime;
            if (runtime is null)
                return 0u;
            RuntimeMovementSnapshot movement = runtime.Movement.Snapshot;
            return movement.HasController
                ? movement.Position.ObjCellId & 0xFFFF0000u
                : 0u;
        }
    }

    public IReadOnlyList<PluginIndoorCell> CaptureIndoorCells(uint landblockId)
    {
        uint landblock = landblockId & 0xFFFF0000u;
        IDatObjectSource? content;
        object? contentLock;
        lock (_gate)
        {
            if (_indoorCells.TryGetValue(landblock, out IReadOnlyList<PluginIndoorCell>? kept))
                return kept;
            content = _content;
            contentLock = _contentLock;
        }
        if (content is null || contentLock is null)
            return Array.Empty<PluginIndoorCell>();

        // Read-only, because every plugin is handed the same list.
        IReadOnlyList<PluginIndoorCell> cells =
            Array.AsReadOnly(ReadIndoorCells(content, contentLock, landblock));
        lock (_gate)
        {
            // Content replaced while this was read: hand back what was read,
            // and keep nothing from the files that are gone.
            if (!ReferenceEquals(_content, content))
                return cells;
            if (_indoorCells.TryGetValue(landblock, out IReadOnlyList<PluginIndoorCell>? kept))
                return kept;
            if (_indoorCells.Count >= MaximumCachedIndoorLandblocks)
                _indoorCells.Remove(_indoorCellOrder.Dequeue());
            _indoorCells[landblock] = cells;
            _indoorCellOrder.Enqueue(landblock);
            return cells;
        }
    }

    /// <summary>
    /// The landblock's cell count from its information file, then each cell
    /// the count covers, as stored. A cell the files do not have is left out.
    /// </summary>
    private static PluginIndoorCell[] ReadIndoorCells(
        IDatObjectSource content,
        object contentLock,
        uint landblock)
    {
        LandBlockInfo? info;
        lock (contentLock)
            info = content.Get<LandBlockInfo>(landblock | 0xFFFEu);
        if (info is null || info.NumCells == 0)
            return [];
        uint firstCell = landblock | 0x0100u;
        // Hundreds of cells share a few dozen pieces; each piece is read once.
        var structures = new Dictionary<uint, CellStruct?>();
        var read = new List<(uint CellId, EnvCell Cell, CellStruct? Structure)>();
        // Every cell's middle first: a level opening in a flat cell is told
        // floor from ceiling by the middle of the cell beyond it.
        var middles = new Dictionary<uint, float>();
        for (uint offset = 0; offset < info.NumCells; offset++)
        {
            uint cellId = firstCell + offset;
            // One cell at a time, so a large dungeon does not hold the shared
            // files away from everything else while it is read.
            EnvCell? envCell;
            lock (contentLock)
                envCell = content.Get<EnvCell>(cellId);
            if (envCell is null)
                continue;
            CellStruct? structure = ReadStructure(content, contentLock, envCell, structures);
            read.Add((cellId, envCell, structure));
            if (structure is not null)
            {
                middles[cellId] = PlacedMiddleHeight(
                    structure,
                    envCell.Position?.Origin ?? Vector3.Zero,
                    envCell.Position?.Orientation ?? Quaternion.Identity);
            }
        }

        var cells = new PluginIndoorCell[read.Count];
        for (int index = 0; index < cells.Length; index++)
        {
            (uint cellId, EnvCell envCell, CellStruct? structure) = read[index];
            Vector3 origin = envCell.Position?.Origin ?? Vector3.Zero;
            Quaternion orientation = envCell.Position?.Orientation ?? Quaternion.Identity;
            cells[index] = new PluginIndoorCell(
                cellId,
                envCell.EnvironmentId,
                envCell.CellStructure,
                origin,
                orientation,
                envCell.Flags.HasFlag(EnvCellFlags.SeenOutside))
            {
                Doorways = ReadDoorways(landblock, cellId, envCell, structure, origin, orientation, middles),
            };
        }
        return cells;
    }

    /// <summary>The structure a cell is built from, read once per piece; null when the files lack it.</summary>
    private static CellStruct? ReadStructure(
        IDatObjectSource content,
        object contentLock,
        EnvCell envCell,
        Dictionary<uint, CellStruct?> structures)
    {
        uint key = ((uint)envCell.EnvironmentId << 16) | envCell.CellStructure;
        if (structures.TryGetValue(key, out CellStruct? structure))
            return structure;
        DatReaderWriter.DBObjs.Environment? environment;
        lock (contentLock)
            environment = content.Get<DatReaderWriter.DBObjs.Environment>(
                0x0D000000u | envCell.EnvironmentId);
        structure = environment is not null
            && environment.Cells.TryGetValue(envCell.CellStructure, out CellStruct? found)
                ? found
                : null;
        structures[key] = structure;
        return structure;
    }

    /// <summary>
    /// The cell's portals as doorways: the cell each leads to, the middle of
    /// its polygon placed as the cell is, which way it faces and its lowest
    /// corner.
    /// </summary>
    private static PluginCellDoorway[] ReadDoorways(
        uint landblock,
        uint cellId,
        EnvCell envCell,
        CellStruct? structure,
        Vector3 origin,
        Quaternion orientation,
        Dictionary<uint, float> middles)
    {
        if (envCell.CellPortals.Count == 0)
            return [];

        // The floorplan's cell middle, and the same test its folding makes.
        float cellMiddle = middles.GetValueOrDefault(cellId, origin.Z);
        var doorways = new PluginCellDoorway[envCell.CellPortals.Count];
        for (int index = 0; index < doorways.Length; index++)
        {
            CellPortal portal = envCell.CellPortals[index];
            uint other = portal.OtherCellId == 0xFFFF ? 0u : landblock | portal.OtherCellId;
            Vector3 center = origin;
            PluginDoorwayKind kind = PluginDoorwayKind.Unknown;
            float sill = origin.Z;
            if (structure is not null
                && structure.Polygons.TryGetValue(portal.PolygonId, out Polygon? polygon)
                && TryPlace(structure, polygon, origin, orientation, out Vector3[] corners))
            {
                center = BoxMiddle(corners);
                sill = corners.Min(static corner => corner.Z);
                Vector3 normal = DungeonFloorplanBuilder.WindingNormal(corners);
                if (normal != Vector3.Zero)
                {
                    kind = MathF.Abs(normal.Z) < DungeonFloorplanBuilder.LevelMinimumUp
                        ? PluginDoorwayKind.Upright
                        : DungeonFloorplanBuilder.LevelOpeningKind(
                            center.Z,
                            cellMiddle,
                            middles.TryGetValue(other, out float otherMiddle) ? otherMiddle : null);
                }
            }
            doorways[index] = new PluginCellDoorway(other, center) { Kind = kind, Sill = sill };
        }
        return doorways;
    }

    /// <summary>
    /// The middle height of the box around every polygon corner of the
    /// structure once placed, as the floorplan's cell middle is.
    /// </summary>
    private static float PlacedMiddleHeight(CellStruct structure, Vector3 origin, Quaternion orientation)
    {
        float minimum = float.MaxValue;
        float maximum = float.MinValue;
        foreach (Polygon polygon in structure.Polygons.Values)
        {
            if (!TryPlace(structure, polygon, origin, orientation, out Vector3[] corners))
                continue;
            foreach (Vector3 corner in corners)
            {
                minimum = MathF.Min(minimum, corner.Z);
                maximum = MathF.Max(maximum, corner.Z);
            }
        }
        return minimum > maximum ? origin.Z : (minimum + maximum) * 0.5f;
    }

    /// <summary>The polygon's corners placed as the cell is; false when it has none or one is missing.</summary>
    private static bool TryPlace(
        CellStruct structure,
        Polygon polygon,
        Vector3 origin,
        Quaternion orientation,
        out Vector3[] corners)
    {
        corners = [];
        if (polygon.VertexIds.Count == 0)
            return false;
        var placed = new Vector3[polygon.VertexIds.Count];
        for (int index = 0; index < placed.Length; index++)
        {
            if (!structure.VertexArray.Vertices.TryGetValue((ushort)polygon.VertexIds[index], out SWVertex? vertex))
                return false;
            placed[index] = Vector3.Transform(vertex.Origin, orientation) + origin;
        }
        corners = placed;
        return true;
    }

    /// <summary>
    /// The middle of the box around the corners. Not their average: one side
    /// of an opening is often a single quad and the other the two triangles
    /// it splits into, and only the box gives both sides the same middle.
    /// </summary>
    private static Vector3 BoxMiddle(Vector3[] corners)
    {
        var min = new Vector3(float.MaxValue);
        var max = new Vector3(float.MinValue);
        foreach (Vector3 corner in corners)
        {
            min = Vector3.Min(min, corner);
            max = Vector3.Max(max, corner);
        }
        return (min + max) * 0.5f;
    }

    public PluginDungeonFloorplan CaptureFloorplan(uint landblockId)
    {
        uint landblock = landblockId & 0xFFFF0000u;
        DungeonFloorplanBuilder? builder;
        lock (_gate)
        {
            if (_projected.TryGetValue(landblock, out PluginDungeonFloorplan? kept))
                return kept;
            builder = _builder;
        }
        if (builder is null)
            return PluginDungeonFloorplan.Empty;

        // The builder keeps its own plan per landblock and reads the files
        // under the lock it was given; the projection to plugin records is
        // made off every lock and kept here so a plugin that asks each frame
        // gets the same object back.
        DungeonFloorplan? plan = builder.TryBuild(landblock);
        PluginDungeonFloorplan projected = plan is null
            ? PluginDungeonFloorplan.Empty
            : Project(plan);
        lock (_gate)
        {
            if (!ReferenceEquals(_builder, builder))
                return projected;
            if (_projected.TryGetValue(landblock, out PluginDungeonFloorplan? kept))
                return kept;
            _projected[landblock] = projected;
            return projected;
        }
    }

    private static PluginDungeonFloorplan Project(DungeonFloorplan plan)
    {
        var layers = new PluginDungeonLayer[plan.Layers.Length];
        for (int index = 0; index < layers.Length; index++)
        {
            DungeonFloorplanLayer layer = plan.Layers[index];
            var floors = new IReadOnlyList<Vector2>[layer.Floors.Length];
            for (int floor = 0; floor < floors.Length; floor++)
                floors[floor] = layer.Floors[floor];
            var walls = new PluginDungeonWall[layer.Walls.Length];
            for (int wall = 0; wall < walls.Length; wall++)
                walls[wall] = new PluginDungeonWall(layer.Walls[wall].Start, layer.Walls[wall].End);
            layers[index] = new PluginDungeonLayer(layer.Z, floors, walls);
        }

        var cells = new PluginDungeonCell[plan.Cells.Length];
        for (int index = 0; index < cells.Length; index++)
        {
            DungeonFloorplanCell cell = plan.Cells[index];
            var cellFloors = new IReadOnlyList<Vector3>[cell.Floors.Length];
            for (int floor = 0; floor < cellFloors.Length; floor++)
                cellFloors[floor] = cell.Floors[floor];
            cells[index] = new PluginDungeonCell(cell.CellId, cell.Center, cell.LayerZ) { Floors = cellFloors };
        }

        return new PluginDungeonFloorplan(
            plan.LandblockId,
            layers,
            cells,
            plan.Bounds.IsEmpty ? Vector3.Zero : plan.Bounds.Min,
            plan.Bounds.IsEmpty ? Vector3.Zero : plan.Bounds.Max);
    }
}
