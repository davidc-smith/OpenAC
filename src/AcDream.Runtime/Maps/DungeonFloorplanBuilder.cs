using System.Collections.Immutable;
using System.Numerics;
using AcDream.Content;
using AcDream.Core.Content;
using AcDream.Core.Meshing;
using AcDream.Core.World;
using AcDream.Plugin.Abstractions;
using DatReaderWriter.DBObjs;
using DatReaderWriter.Types;

namespace AcDream.Runtime.Maps;

/// <summary>
/// Builds a <see cref="DungeonFloorplan"/> for a landblock from the cell
/// geometry the game data carries: each indoor cell's structure, placed by
/// the cell's own position and turn, with every polygon flattened onto the
/// ground plane. A level polygon that faces up is floor; a standing polygon
/// that reaches down to the floor it stands on is a wall, seen edge-on as a
/// line; the doorways between cells, which the data lists as portal
/// polygons, are left open. The objects a cell places -- a bridge, a stair,
/// a platform -- are floor too where their physics faces up.
///
/// <para>The game files are read with the caller's lock held and only for as
/// long as the read takes; the flattening runs afterwards, off the lock. What
/// a structure flattens to is kept once per structure, since hundreds of
/// cells share a few dozen, and a finished plan is kept per landblock, since
/// the data never changes underneath it.</para>
/// </summary>
internal sealed class DungeonFloorplanBuilder
{
    /// <summary>
    /// How far a polygon's normal must lean upward, as the cosine of its
    /// angle from straight up, to count as level: 0.7 is about 45 degrees,
    /// so a stair ramp is floor and a steep chute is not.
    /// </summary>
    internal const float LevelMinimumUp = 0.7f;

    /// <summary>
    /// The most a polygon's normal may lean up or down, as a cosine, to
    /// count as standing: 0.3 is about 17 degrees off vertical.
    /// </summary>
    private const float StandingMaximumUp = 0.3f;

    /// <summary>Cells are bucketed into bands this tall, in metres.</summary>
    private const float LayerHeight = 6f;

    /// <summary>
    /// Bands are shifted down by this much so a cell whose origin sits a
    /// little below a whole band still lands in that band.
    /// </summary>
    private const float LayerOffset = 3f;

    /// <summary>Two wall lines within this angle, in degrees, may be one line.</summary>
    private const float MergeAngleDegrees = 1f;

    /// <summary>Two wall lines this far apart sideways, in metres, may be one line.</summary>
    private const float MergeOffsetMetres = 0.05f;

    /// <summary>Two collinear spans with a gap this small, in metres, are joined.</summary>
    private const float MergeGapMetres = 0.05f;

    /// <summary>A wall shorter than this, in metres, is a sliver and dropped.</summary>
    private const float MinimumWallMetres = 0.01f;

    /// <summary>
    /// How far above the floor it stands on, in metres, a standing face may
    /// start and still be a wall: a real wall starts at the floor or within a
    /// ramp's step of it, a door's lintel three metres or more up.
    /// </summary>
    private const float WallReachMetres = 1f;

    /// <summary>
    /// How far across, in metres, a floor corner may be from a wall's foot
    /// and still be the floor the wall stands on.
    /// </summary>
    private const float FloorCornerReachMetres = 1f;

    /// <summary>
    /// A level opening this close to its cell's middle height, in metres, is
    /// in a flat cell: neither above nor below the middle.
    /// </summary>
    private const float FlatCellMetres = 0.05f;

    /// <summary>
    /// An object's level face smaller than this, in square metres, is still
    /// floor but does not band a cell: a candle shelf must not lift a room.
    /// </summary>
    private const float ObjectBandMinimumArea = 0.25f;

    private readonly IDatObjectSource _dats;
    private readonly object _datLock;
    private readonly object _cacheGate = new();
    private readonly Dictionary<uint, CellGeometry?> _geometryByStructure = new();
    private readonly Dictionary<uint, Vector3[][]> _facesByObject = new();
    private readonly Dictionary<uint, DungeonFloorplan?> _plansByLandblock = new();

    /// <param name="dats">The game files.</param>
    /// <param name="datLock">The lock every read of <paramref name="dats"/> is made under.</param>
    public DungeonFloorplanBuilder(IDatObjectSource dats, object datLock)
    {
        _dats = dats ?? throw new ArgumentNullException(nameof(dats));
        _datLock = datLock ?? throw new ArgumentNullException(nameof(datLock));
    }

    /// <summary>How many cell structures have been flattened and kept.</summary>
    public int CachedGeometryCount
    {
        get { lock (_cacheGate) return _geometryByStructure.Count; }
    }

    /// <summary>
    /// The plan for the landblock of <paramref name="landblockId"/> (any cell
    /// id in it will do), or null when the landblock is not in the data or
    /// has no indoor cells. The same plan comes back on every later call.
    /// </summary>
    public DungeonFloorplan? TryBuild(uint landblockId)
    {
        uint landblock = landblockId & 0xFFFF0000u;
        lock (_cacheGate)
        {
            if (_plansByLandblock.TryGetValue(landblock, out DungeonFloorplan? cached))
                return cached;
        }

        DungeonFloorplan? plan = Build(landblock);
        lock (_cacheGate)
        {
            // Two callers may race to the same landblock; both plans are
            // the same data, so the first one kept wins and the other is let go.
            if (_plansByLandblock.TryGetValue(landblock, out DungeonFloorplan? kept))
                return kept;
            _plansByLandblock[landblock] = plan;
            return plan;
        }
    }

    private DungeonFloorplan? Build(uint landblock)
    {
        List<PlacedCell> placed = ReadCells(landblock);
        if (placed.Count == 0)
            return null;

        var counts = new DungeonFloorplanCounts();
        var bounds = DungeonFloorplanBounds.Empty;
        var layers = new SortedDictionary<float, LayerBuilder>();
        LayerBuilder Layer(float z)
        {
            if (!layers.TryGetValue(z, out LayerBuilder? layer))
                layers[z] = layer = new LayerBuilder();
            return layer;
        }

        // Every cell is flattened first: its floor goes straight into the
        // band of its own height, since a floor is at the height it is at
        // whatever the cell's origin says; its walls wait until the cell's
        // band, and the floor they stand on, are known.
        var flattened = new List<FlattenedCell>(placed.Count);
        foreach (PlacedCell cell in placed)
        {
            Matrix4x4 transform = Matrix4x4.CreateFromQuaternion(cell.Orientation)
                * Matrix4x4.CreateTranslation(cell.Origin);
            var cellBounds = DungeonFloorplanBounds.Empty;
            var walls = new List<WallCandidate>();
            var floors = ImmutableArray.CreateBuilder<ImmutableArray<Vector3>>();
            var levelOpenings = new List<(ushort PolygonId, float Height)>();
            float floorHeight = 0f;
            int floorVertices = 0;

            foreach (PolygonGeometry polygon in cell.Geometry.Polygons)
            {
                counts.Polygons++;
                var world = new Vector3[polygon.Vertices.Length];
                for (int index = 0; index < world.Length; index++)
                {
                    world[index] = Vector3.Transform(polygon.Vertices[index], transform);
                    cellBounds = cellBounds.Including(world[index]);
                }

                Vector3 facing = Vector3.Transform(polygon.WindingNormal, cell.Orientation);
                float up = MathF.Abs(facing.Z);
                if (polygon.IsPortal)
                {
                    counts.Portals++;
                    if (up >= LevelMinimumUp)
                        levelOpenings.Add((polygon.PolygonId, MiddleHeight(world)));
                    continue;
                }

                Vector3 shading = Vector3.Transform(polygon.MeanVertexNormal, cell.Orientation);
                if (up >= LevelMinimumUp)
                {
                    // The authored vertex normals say which way a level
                    // face is lit, which is which way the room is; the
                    // winding is the fallback when they say nothing.
                    float facesUp = MathF.Abs(shading.Z) > 0.001f ? shading.Z : facing.Z;
                    if (facesUp > 0f)
                    {
                        counts.Floors++;
                        float height = 0f;
                        foreach (Vector3 vertex in world)
                            height += vertex.Z;
                        height /= world.Length;
                        Layer(LayerKey(height)).Floors.Add(ProjectFloor(world));
                        floors.Add(ImmutableArray.Create(world));
                        floorHeight += height * world.Length;
                        floorVertices += world.Length;
                    }
                    else
                    {
                        counts.Ceilings++;
                    }
                }
                else if (up <= StandingMaximumUp)
                {
                    if (TryProjectWall(world, facing, out WallSpan span))
                    {
                        counts.Walls++;
                        walls.Add(new WallCandidate(span, LowestCorner(world)));
                    }
                }
                else
                {
                    counts.Slopes++;
                }
            }

            if (cellBounds.IsEmpty)
                continue;
            bounds = bounds.Including(cellBounds.Min).Including(cellBounds.Max);
            Vector3 center = (cellBounds.Min + cellBounds.Max) * 0.5f;

            // An object's floor is at its own height, as a cell's is; only
            // the faces big enough to stand a room on say where the cell is.
            float objectHeight = 0f;
            int objectVertices = 0;
            foreach (ImmutableArray<Vector3> face in cell.ObjectFloors)
            {
                counts.ObjectFloors++;
                float height = 0f;
                foreach (Vector3 vertex in face)
                    height += vertex.Z;
                Layer(LayerKey(height / face.Length)).Floors.Add(ProjectFloor(face.AsSpan()));
                if (Area(face.AsSpan()) >= ObjectBandMinimumArea)
                {
                    objectHeight += height;
                    objectVertices += face.Length;
                }
            }

            flattened.Add(new FlattenedCell(
                cell.CellId,
                cell.Neighbours,
                levelOpenings,
                center,
                floorVertices == 0 ? null : LayerKey(floorHeight / floorVertices),
                objectVertices == 0 ? null : LayerKey(objectHeight / objectVertices),
                LayerKey(cell.Origin.Z),
                floors.ToImmutable(),
                cell.ObjectFloors,
                walls));
        }

        // A tall room is authored as a cell on top of a cell: the upper one
        // has walls and a ceiling but no floor, and opens through a level
        // doorway in its own floor onto the cell below. Its walls belong in
        // that cell's band, or the band above would show an empty outline of
        // the room below. A cell with a floor is drawn in its floor's band; a
        // cell with no floor of its own but an object to stand on, as the
        // air a bridge spans is, is drawn in the band of the object's floor
        // and stands on it; a cell with neither and no way down -- a doorway
        // beside it does not count -- stays in the band its origin is in. A
        // folded cell stands on the floor of the cell it folds onto.
        var middles = flattened.ToDictionary(static cell => cell.CellId, static cell => cell.Center.Z);
        var bands = new Dictionary<uint, float>(flattened.Count);
        var standsOn = new Dictionary<uint, ImmutableArray<ImmutableArray<Vector3>>>(flattened.Count);
        foreach (FlattenedCell cell in flattened)
        {
            if (cell.FloorBand is { } floorBand)
            {
                bands[cell.CellId] = floorBand;
                standsOn[cell.CellId] = cell.Floors;
            }
            else if (cell.ObjectBand is { } objectBand)
            {
                bands[cell.CellId] = objectBand;
                standsOn[cell.CellId] = cell.ObjectFloors;
            }
        }
        var unresolved = flattened
            .Where(static cell => cell.FloorBand is null && cell.ObjectBand is null)
            .ToList();
        // A cell may open onto another floorless cell, so folding goes
        // round until nothing more folds.
        for (bool folded = true; folded && unresolved.Count != 0;)
        {
            folded = false;
            for (int index = unresolved.Count - 1; index >= 0; index--)
            {
                FlattenedCell cell = unresolved[index];
                float? below = null;
                uint onto = 0u;
                foreach (uint neighbour in WaysDown(cell, landblock, middles))
                {
                    if (bands.TryGetValue(landblock | neighbour, out float band)
                        && (below is null || band > below))
                    {
                        below = band;
                        onto = landblock | neighbour;
                    }
                }
                if (below is { } found)
                {
                    bands[cell.CellId] = found;
                    standsOn[cell.CellId] = standsOn[onto];
                    unresolved.RemoveAt(index);
                    folded = true;
                }
            }
        }
        foreach (FlattenedCell cell in unresolved)
            bands[cell.CellId] = cell.OriginBand;

        var cells = ImmutableArray.CreateBuilder<DungeonFloorplanCell>(flattened.Count);
        foreach (FlattenedCell cell in flattened)
        {
            float band = bands[cell.CellId];
            LayerBuilder layer = Layer(band);
            // A standing face is a wall only where it reaches down to the
            // floor it stands on: a lintel over a doorway, or the walls of a
            // tall room's upper cell, would otherwise close every doorway on
            // the plan. A floorless cell with no way down keeps every wall,
            // or it would vanish from the map.
            if (standsOn.TryGetValue(cell.CellId, out ImmutableArray<ImmutableArray<Vector3>> floor))
            {
                foreach (WallCandidate wall in cell.Walls)
                {
                    if (wall.Lowest.Z - FloorHeightAt(floor, wall.Lowest) <= WallReachMetres)
                    {
                        layer.Walls.Add(wall.Span);
                    }
                    else
                    {
                        counts.WallsAboveFloor++;
                        counts.WallsAboveFloorByBand[band] =
                            counts.WallsAboveFloorByBand.GetValueOrDefault(band) + 1;
                    }
                }
            }
            else
            {
                foreach (WallCandidate wall in cell.Walls)
                    layer.Walls.Add(wall.Span);
            }
            cells.Add(new DungeonFloorplanCell(cell.CellId, cell.Center, band, cell.Floors, cell.ObjectFloors));
        }

        var built = ImmutableArray.CreateBuilder<DungeonFloorplanLayer>(layers.Count);
        foreach ((float z, LayerBuilder layer) in layers)
        {
            ImmutableArray<DungeonFloorplanWall> walls = MergeWalls(layer.Walls);
            counts.WallsAfterMerge += walls.Length;
            built.Add(new DungeonFloorplanLayer(z, layer.Floors.ToImmutable(), walls));
        }

        return new DungeonFloorplan(
            landblock,
            built.MoveToImmutable(),
            cells.ToImmutable(),
            bounds,
            counts);
    }

    /// <summary>The band a cell at height <paramref name="z"/> is drawn in.</summary>
    internal static float LayerKey(float z) =>
        MathF.Floor((z + LayerOffset) / LayerHeight) * LayerHeight;

    /// <summary>
    /// The cells a cell opens onto through a level doorway in its floor: the
    /// same test a plugin's doorway kind makes.
    /// </summary>
    private static IEnumerable<uint> WaysDown(FlattenedCell cell, uint landblock, Dictionary<uint, float> middles)
    {
        foreach ((uint other, ushort polygonId) in cell.Neighbours)
        {
            foreach ((ushort openingId, float height) in cell.LevelOpenings)
            {
                if (openingId != polygonId)
                    continue;
                float? otherMiddle = middles.TryGetValue(landblock | other, out float found) ? found : null;
                if (LevelOpeningKind(height, cell.Center.Z, otherMiddle) == PluginDoorwayKind.Floor)
                    yield return other;
                break;
            }
        }
    }

    /// <summary>
    /// The height of the floor a point stands on: the plane of the floor
    /// polygon under it, the one nearest the point's height where floors
    /// overlap. A wall stands on a floor's edge, so its corner is often just
    /// outside every polygon; then the lowest floor corner within
    /// <see cref="FloorCornerReachMetres"/> across, and failing that the
    /// lowest floor height there is.
    /// </summary>
    private static float FloorHeightAt(ImmutableArray<ImmutableArray<Vector3>> floors, Vector3 point)
    {
        var flat = new Vector2(point.X, point.Y);
        float? under = null;
        foreach (ImmutableArray<Vector3> floor in floors)
        {
            if (!Covers(floor, flat))
                continue;
            float height = PlaneHeight(floor, flat);
            if (under is null || MathF.Abs(height - point.Z) < MathF.Abs(under.Value - point.Z))
                under = height;
        }
        if (under is { } found)
            return found;

        float? nearby = null;
        float lowest = float.MaxValue;
        foreach (ImmutableArray<Vector3> floor in floors)
        {
            foreach (Vector3 corner in floor)
            {
                lowest = MathF.Min(lowest, corner.Z);
                if (Vector2.Distance(new Vector2(corner.X, corner.Y), flat) <= FloorCornerReachMetres
                    && (nearby is null || corner.Z < nearby))
                {
                    nearby = corner.Z;
                }
            }
        }
        return nearby ?? lowest;
    }

    /// <summary>
    /// Whether a floor polygon, seen from above, covers a point, counting
    /// a point within <see cref="MergeOffsetMetres"/> of its edge.
    /// </summary>
    private static bool Covers(ImmutableArray<Vector3> floor, Vector2 point)
    {
        bool inside = false;
        for (int index = 0, previous = floor.Length - 1; index < floor.Length; previous = index++)
        {
            var a = new Vector2(floor[index].X, floor[index].Y);
            var b = new Vector2(floor[previous].X, floor[previous].Y);
            if (DistanceToSegment(point, a, b) <= MergeOffsetMetres)
                return true;
            if ((a.Y > point.Y) != (b.Y > point.Y)
                && point.X < (b.X - a.X) * (point.Y - a.Y) / (b.Y - a.Y) + a.X)
            {
                inside = !inside;
            }
        }
        return inside;
    }

    private static float DistanceToSegment(Vector2 point, Vector2 a, Vector2 b)
    {
        Vector2 along = b - a;
        float lengthSquared = along.LengthSquared();
        float t = lengthSquared < 1e-12f ? 0f : Math.Clamp(Vector2.Dot(point - a, along) / lengthSquared, 0f, 1f);
        return Vector2.Distance(point, a + along * t);
    }

    /// <summary>The height of a level polygon's plane over a point.</summary>
    private static float PlaneHeight(ImmutableArray<Vector3> floor, Vector2 point)
    {
        Vector3 normal = WindingNormal(floor.AsSpan());
        Vector3 corner = floor[0];
        if (MathF.Abs(normal.Z) < 1e-6f)
            return corner.Z;
        return corner.Z - ((normal.X * (point.X - corner.X)) + (normal.Y * (point.Y - corner.Y))) / normal.Z;
    }

    private static Vector3 LowestCorner(Vector3[] world)
    {
        Vector3 lowest = world[0];
        foreach (Vector3 vertex in world)
        {
            if (vertex.Z < lowest.Z)
                lowest = vertex;
        }
        return lowest;
    }

    /// <summary>
    /// Whether a level opening is in a cell's floor or its ceiling: below the
    /// middle of the cell's geometry it is floor, above it ceiling. A flat
    /// cell, nothing but the opening, has it at its middle; then the cell
    /// beyond decides, floor when its middle is lower, and ceiling when it
    /// is not or is not known.
    /// </summary>
    /// <param name="openingHeight">The middle height of the opening's corners.</param>
    /// <param name="cellMiddle">The middle height of the cell's geometry.</param>
    /// <param name="otherMiddle">The middle height of the cell beyond, when known.</param>
    internal static PluginDoorwayKind LevelOpeningKind(float openingHeight, float cellMiddle, float? otherMiddle)
    {
        if (openingHeight < cellMiddle - FlatCellMetres)
            return PluginDoorwayKind.Floor;
        if (openingHeight > cellMiddle + FlatCellMetres)
            return PluginDoorwayKind.Ceiling;
        return otherMiddle < cellMiddle ? PluginDoorwayKind.Floor : PluginDoorwayKind.Ceiling;
    }

    /// <summary>A flat polygon's area in square metres, whichever way it faces.</summary>
    private static float Area(ReadOnlySpan<Vector3> points)
    {
        var doubled = Vector3.Zero;
        for (int index = 0; index < points.Length; index++)
            doubled += Vector3.Cross(points[index], points[(index + 1) % points.Length]);
        return doubled.Length() * 0.5f;
    }

    /// <summary>The middle of the box around a polygon's corners, up the vertical.</summary>
    private static float MiddleHeight(Vector3[] world)
    {
        float minimum = float.MaxValue;
        float maximum = float.MinValue;
        foreach (Vector3 vertex in world)
        {
            minimum = MathF.Min(minimum, vertex.Z);
            maximum = MathF.Max(maximum, vertex.Z);
        }
        return (minimum + maximum) * 0.5f;
    }

    /// <summary>
    /// A polygon's normal from its vertex order, summed over every edge so a
    /// bent or many-sided polygon still gives its overall facing; zero for a
    /// polygon with no area.
    /// </summary>
    internal static Vector3 WindingNormal(ReadOnlySpan<Vector3> points)
    {
        var normal = Vector3.Zero;
        for (int index = 0; index < points.Length; index++)
        {
            Vector3 current = points[index];
            Vector3 next = points[(index + 1) % points.Length];
            normal.X += (current.Y - next.Y) * (current.Z + next.Z);
            normal.Y += (current.Z - next.Z) * (current.X + next.X);
            normal.Z += (current.X - next.X) * (current.Y + next.Y);
        }
        float length = normal.Length();
        return length < 1e-9f ? Vector3.Zero : normal / length;
    }

    /// <summary>
    /// Reads the landblock's cells, and the objects they place, with the
    /// lock held, and nothing more: every flattening happens after the lock
    /// is released. A structure or object seen before is not read again.
    /// </summary>
    private List<PlacedCell> ReadCells(uint landblock)
    {
        var placed = new List<PlacedCell>();
        var pendingStructures = new List<(uint Key, CellStruct Structure)>();
        var pendingCells = new List<(uint CellId, EnvCell Cell, uint Key)>();
        var pendingObjects = new Dictionary<uint, List<(GfxObj Part, Matrix4x4 Transform)>>();

        lock (_datLock)
        {
            LandBlockInfo? info = _dats.Get<LandBlockInfo>(landblock | 0xFFFEu);
            if (info is null || info.NumCells == 0)
                return placed;

            uint firstCell = landblock | 0x0100u;
            for (uint offset = 0; offset < info.NumCells; offset++)
            {
                uint cellId = firstCell + offset;
                EnvCell? envCell = _dats.Get<EnvCell>(cellId);
                if (envCell is null || envCell.EnvironmentId == 0 || envCell.Position is null)
                    continue;

                uint key = StructureKey(envCell.EnvironmentId, envCell.CellStructure);
                bool known;
                lock (_cacheGate)
                    known = _geometryByStructure.ContainsKey(key)
                        || pendingStructures.Exists(pending => pending.Key == key);
                if (!known)
                {
                    var environment = _dats.Get<DatReaderWriter.DBObjs.Environment>(
                        0x0D000000u | envCell.EnvironmentId);
                    if (environment is not null
                        && environment.Cells.TryGetValue(envCell.CellStructure, out CellStruct? structure))
                    {
                        pendingStructures.Add((key, structure));
                    }
                    else
                    {
                        pendingStructures.Add((key, null!));
                    }
                }
                pendingCells.Add((cellId, envCell, key));

                foreach (Stab stab in envCell.StaticObjects)
                {
                    bool objectKnown;
                    lock (_cacheGate)
                        objectKnown = _facesByObject.ContainsKey(stab.Id);
                    if (!objectKnown && !pendingObjects.ContainsKey(stab.Id))
                        pendingObjects[stab.Id] = ReadObjectParts(stab.Id);
                }
            }
        }

        foreach ((uint key, CellStruct structure) in pendingStructures)
        {
            CellGeometry? geometry = structure is null ? null : CellGeometry.From(structure);
            lock (_cacheGate)
                _geometryByStructure.TryAdd(key, geometry);
        }

        foreach ((uint objectId, List<(GfxObj Part, Matrix4x4 Transform)> parts) in pendingObjects)
        {
            Vector3[][] faces = ObjectFaces(parts);
            lock (_cacheGate)
                _facesByObject.TryAdd(objectId, faces);
        }

        foreach ((uint cellId, EnvCell envCell, uint key) in pendingCells)
        {
            CellGeometry? geometry;
            lock (_cacheGate)
                _geometryByStructure.TryGetValue(key, out geometry);
            if (geometry is null)
                continue;

            // A doorway is a polygon the structure lists as a portal, or one
            // this cell's own portal list points at; both are openings.
            var openings = new HashSet<ushort>(geometry.PortalPolygonIds);
            foreach (CellPortal portal in envCell.CellPortals)
                openings.Add(portal.PolygonId);
            CellGeometry withOpenings = openings.SetEquals(geometry.PortalPolygonIds)
                ? geometry
                : geometry.WithOpenings(openings);

            var neighbours = new List<(uint OtherCellId, ushort PolygonId)>(envCell.CellPortals.Count);
            foreach (CellPortal portal in envCell.CellPortals)
                neighbours.Add((portal.OtherCellId, portal.PolygonId));

            placed.Add(new PlacedCell(
                cellId,
                envCell.Position.Origin,
                envCell.Position.Orientation,
                withOpenings,
                neighbours,
                PlaceObjectFloors(envCell.StaticObjects)));
        }
        return placed;
    }

    /// <summary>
    /// The parts of a placed object that the player collides with, each
    /// with its transform in the object's frame: a GfxObj is its own one
    /// part, a Setup its parts at rest. Editor markers are left out unless
    /// they carry collision, as the physics leaves them. Read under the lock.
    /// </summary>
    private List<(GfxObj Part, Matrix4x4 Transform)> ReadObjectParts(uint objectId)
    {
        var parts = new List<(GfxObj Part, Matrix4x4 Transform)>();
        if ((objectId & 0xFF000000u) == 0x01000000u)
        {
            if (LandblockPhysicsContentBuilder.KeepsInteriorPart(_dats, objectId)
                && _dats.Get<GfxObj>(objectId) is { } gfx)
            {
                parts.Add((gfx, Matrix4x4.Identity));
            }
        }
        else if ((objectId & 0xFF000000u) == 0x02000000u && _dats.Get<Setup>(objectId) is { } setup)
        {
            foreach (MeshRef part in SetupMesh.Flatten(setup))
            {
                if (LandblockPhysicsContentBuilder.KeepsInteriorPart(_dats, part.GfxObjId)
                    && _dats.Get<GfxObj>(part.GfxObjId) is { } gfx)
                {
                    parts.Add((gfx, part.PartTransform));
                }
            }
        }
        return parts;
    }

    /// <summary>Every physics polygon of an object's parts, in the object's own frame.</summary>
    private static Vector3[][] ObjectFaces(List<(GfxObj Part, Matrix4x4 Transform)> parts)
    {
        var faces = new List<Vector3[]>();
        foreach ((GfxObj part, Matrix4x4 transform) in parts)
        {
            if (part.VertexArray?.Vertices is not { } vertices)
                continue;
            foreach (Polygon polygon in part.PhysicsPolygons.Values)
            {
                if (polygon.VertexIds.Count < 3)
                    continue;
                var corners = new Vector3[polygon.VertexIds.Count];
                bool complete = true;
                for (int index = 0; index < corners.Length; index++)
                {
                    if (!vertices.TryGetValue((ushort)polygon.VertexIds[index], out SWVertex? vertex))
                    {
                        complete = false;
                        break;
                    }
                    corners[index] = Vector3.Transform(vertex.Origin, transform);
                }
                if (complete)
                    faces.Add(corners);
            }
        }
        return faces.ToArray();
    }

    /// <summary>
    /// The level, up-facing physics faces of a cell's objects, placed by
    /// each object's frame, which is already landblock-local.
    /// </summary>
    private ImmutableArray<ImmutableArray<Vector3>> PlaceObjectFloors(List<Stab> stabs)
    {
        if (stabs.Count == 0)
            return ImmutableArray<ImmutableArray<Vector3>>.Empty;
        var floors = ImmutableArray.CreateBuilder<ImmutableArray<Vector3>>();
        foreach (Stab stab in stabs)
        {
            Vector3[][]? faces;
            lock (_cacheGate)
                _facesByObject.TryGetValue(stab.Id, out faces);
            if (faces is null || faces.Length == 0)
                continue;
            Matrix4x4 place = Matrix4x4.CreateFromQuaternion(stab.Frame.Orientation)
                * Matrix4x4.CreateTranslation(stab.Frame.Origin);
            foreach (Vector3[] face in faces)
            {
                var corners = new Vector3[face.Length];
                for (int index = 0; index < corners.Length; index++)
                    corners[index] = Vector3.Transform(face[index], place);
                if (WindingNormal(corners).Z >= LevelMinimumUp)
                    floors.Add(ImmutableArray.Create(corners));
            }
        }
        return floors.ToImmutable();
    }

    private static uint StructureKey(ushort environmentId, ushort cellStructure) =>
        ((uint)environmentId << 16) | cellStructure;

    private static ImmutableArray<Vector2> ProjectFloor(ReadOnlySpan<Vector3> world)
    {
        var flat = ImmutableArray.CreateBuilder<Vector2>(world.Length);
        foreach (Vector3 vertex in world)
            flat.Add(new Vector2(vertex.X, vertex.Y));
        return flat.MoveToImmutable();
    }

    /// <summary>
    /// A standing polygon seen from above is a line: the polygon's plane
    /// meets the ground along the direction across its normal, and the
    /// vertices reach some way along it either side of their middle.
    /// </summary>
    private static bool TryProjectWall(Vector3[] world, Vector3 facing, out WallSpan span)
    {
        span = default;
        var across = new Vector2(facing.X, facing.Y);
        float acrossLength = across.Length();
        if (acrossLength < 1e-6f)
            return false;
        across /= acrossLength;
        Vector2 along = Canonical(new Vector2(-across.Y, across.X));
        // With the direction made canonical the sideways normal is fixed
        // too, so two walls facing opposite ways along one line share a key.
        Vector2 sideways = new(-along.Y, along.X);

        float offset = 0f;
        float minimum = float.MaxValue;
        float maximum = float.MinValue;
        foreach (Vector3 vertex in world)
        {
            var flat = new Vector2(vertex.X, vertex.Y);
            offset += Vector2.Dot(flat, sideways);
            float t = Vector2.Dot(flat, along);
            minimum = MathF.Min(minimum, t);
            maximum = MathF.Max(maximum, t);
        }
        offset /= world.Length;
        if (maximum - minimum < MinimumWallMetres)
            return false;

        span = new WallSpan(along, offset, minimum, maximum);
        return true;
    }

    /// <summary>
    /// A direction and its opposite are the same line; pick the one pointing
    /// right, or up when it points straight up or down.
    /// </summary>
    private static Vector2 Canonical(Vector2 direction) =>
        direction.X > 1e-6f || (MathF.Abs(direction.X) <= 1e-6f && direction.Y > 0f)
            ? direction
            : -direction;

    /// <summary>
    /// Joins spans that lie on the same line and touch or overlap. Every
    /// polygon a wall is authored in becomes one line per straight run.
    /// </summary>
    private static ImmutableArray<DungeonFloorplanWall> MergeWalls(List<WallSpan> spans)
    {
        if (spans.Count == 0)
            return ImmutableArray<DungeonFloorplanWall>.Empty;

        var groups = new Dictionary<(int Angle, int Offset), List<WallSpan>>();
        foreach (WallSpan span in spans)
        {
            float degrees = MathF.Atan2(span.Along.Y, span.Along.X) * (180f / MathF.PI);
            if (degrees < 0f)
                degrees += 180f;
            int angleKey = (int)MathF.Round(degrees / MergeAngleDegrees) % (int)(180f / MergeAngleDegrees);
            int offsetKey = (int)MathF.Round(span.Offset / MergeOffsetMetres);
            if (!groups.TryGetValue((angleKey, offsetKey), out List<WallSpan>? group))
                groups[(angleKey, offsetKey)] = group = new List<WallSpan>();
            group.Add(span);
        }

        var walls = ImmutableArray.CreateBuilder<DungeonFloorplanWall>();
        foreach (List<WallSpan> group in groups.Values)
        {
            group.Sort(static (a, b) => a.Minimum.CompareTo(b.Minimum));
            Vector2 along = group[0].Along;
            float offset = 0f;
            foreach (WallSpan span in group)
                offset += span.Offset;
            offset /= group.Count;
            Vector2 sideways = new(-along.Y, along.X);
            Vector2 origin = sideways * offset;

            float runStart = group[0].Minimum;
            float runEnd = group[0].Maximum;
            for (int index = 1; index < group.Count; index++)
            {
                WallSpan span = group[index];
                if (span.Minimum <= runEnd + MergeGapMetres)
                {
                    runEnd = MathF.Max(runEnd, span.Maximum);
                    continue;
                }
                walls.Add(new DungeonFloorplanWall(
                    origin + along * runStart, origin + along * runEnd));
                runStart = span.Minimum;
                runEnd = span.Maximum;
            }
            walls.Add(new DungeonFloorplanWall(
                origin + along * runStart, origin + along * runEnd));
        }
        return walls.ToImmutable();
    }

    private sealed class LayerBuilder
    {
        public ImmutableArray<ImmutableArray<Vector2>>.Builder Floors { get; } =
            ImmutableArray.CreateBuilder<ImmutableArray<Vector2>>();

        public List<WallSpan> Walls { get; } = new();
    }

    /// <summary>A wall line before merging: its direction, its sideways offset from the origin, and its extent along the direction.</summary>
    private readonly record struct WallSpan(Vector2 Along, float Offset, float Minimum, float Maximum);

    /// <param name="Neighbours">
    /// The low halves of the cells this cell's doorways open onto, each with
    /// the polygon of the doorway it opens through.
    /// </param>
    /// <param name="ObjectFloors">The level faces of the objects the cell places, placed.</param>
    private readonly record struct PlacedCell(
        uint CellId,
        Vector3 Origin,
        Quaternion Orientation,
        CellGeometry Geometry,
        List<(uint OtherCellId, ushort PolygonId)> Neighbours,
        ImmutableArray<ImmutableArray<Vector3>> ObjectFloors);

    /// <summary>A standing face that may be a wall: its line, and its lowest corner, placed.</summary>
    private readonly record struct WallCandidate(WallSpan Span, Vector3 Lowest);

    /// <summary>
    /// A cell after flattening, before its band is settled: its floor is
    /// already in the band of its own height, its walls are still waiting.
    /// </summary>
    /// <param name="Neighbours">The low halves of the cells its doorways open onto, each with the doorway's polygon.</param>
    /// <param name="LevelOpenings">Its level doorway polygons, each with the middle height of its corners.</param>
    /// <param name="FloorBand">The band of the cell's floor, weighted by vertex; null when the cell has no floor.</param>
    /// <param name="ObjectBand">
    /// The band of its objects' floors big enough to stand on, weighted by
    /// vertex; null when it places none.
    /// </param>
    /// <param name="OriginBand">The band the cell's origin is in.</param>
    /// <param name="Floors">The cell's own floor polygons, placed.</param>
    /// <param name="ObjectFloors">The level faces of the objects it places, placed.</param>
    private readonly record struct FlattenedCell(
        uint CellId,
        List<(uint OtherCellId, ushort PolygonId)> Neighbours,
        List<(ushort PolygonId, float Height)> LevelOpenings,
        Vector3 Center,
        float? FloorBand,
        float? ObjectBand,
        float OriginBand,
        ImmutableArray<ImmutableArray<Vector3>> Floors,
        ImmutableArray<ImmutableArray<Vector3>> ObjectFloors,
        List<WallCandidate> Walls);

    /// <summary>One polygon of a structure, in the structure's own frame.</summary>
    private readonly record struct PolygonGeometry(
        ushort PolygonId,
        Vector3[] Vertices,
        Vector3 WindingNormal,
        Vector3 MeanVertexNormal,
        bool IsPortal);

    /// <summary>A cell structure's polygons, resolved to points and normals once.</summary>
    private sealed class CellGeometry
    {
        private CellGeometry(PolygonGeometry[] polygons, ImmutableHashSet<ushort> portalPolygonIds)
        {
            Polygons = polygons;
            PortalPolygonIds = portalPolygonIds;
        }

        public PolygonGeometry[] Polygons { get; }

        public ImmutableHashSet<ushort> PortalPolygonIds { get; }

        public static CellGeometry From(CellStruct structure)
        {
            var portals = ImmutableHashSet.CreateRange(structure.Portals);
            var polygons = new List<PolygonGeometry>(structure.Polygons.Count);
            Dictionary<ushort, SWVertex> vertices = structure.VertexArray.Vertices;
            foreach ((ushort polygonId, Polygon polygon) in structure.Polygons)
            {
                if (polygon.VertexIds.Count < 3)
                    continue;
                var points = new Vector3[polygon.VertexIds.Count];
                var shading = Vector3.Zero;
                bool complete = true;
                for (int index = 0; index < points.Length; index++)
                {
                    if (!vertices.TryGetValue((ushort)polygon.VertexIds[index], out SWVertex? vertex))
                    {
                        complete = false;
                        break;
                    }
                    points[index] = vertex.Origin;
                    shading += vertex.Normal;
                }
                if (!complete)
                    continue;

                Vector3 winding = WindingNormal(points);
                if (winding == Vector3.Zero)
                    continue;
                float shadingLength = shading.Length();
                polygons.Add(new PolygonGeometry(
                    polygonId,
                    points,
                    winding,
                    shadingLength > 1e-6f ? shading / shadingLength : Vector3.Zero,
                    portals.Contains(polygonId)));
            }
            return new CellGeometry(polygons.ToArray(), portals);
        }

        public CellGeometry WithOpenings(HashSet<ushort> openings)
        {
            var polygons = new PolygonGeometry[Polygons.Length];
            for (int index = 0; index < polygons.Length; index++)
            {
                PolygonGeometry polygon = Polygons[index];
                polygons[index] = polygon with { IsPortal = openings.Contains(polygon.PolygonId) };
            }
            return new CellGeometry(polygons, openings.ToImmutableHashSet());
        }
    }
}
