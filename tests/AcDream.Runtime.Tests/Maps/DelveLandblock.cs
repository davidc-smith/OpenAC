using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using AcDream.Core.Content;
using DatReaderWriter.DBObjs;
using DatReaderWriter.Enums;
using DatReaderWriter.Types;
using DatEnvironment = DatReaderWriter.DBObjs.Environment;

namespace AcDream.Runtime.Tests.Maps;

/// <summary>
/// Game files holding the pieces a Delve route trips over, each as small as
/// it can be: a ramp whose floor climbs six metres; a corridor with a
/// doorway in its south wall and a lintel over it wider than the doorway; a
/// floorless cell beside the corridor, a band up, joined to it only by a
/// standing doorway; and a shaft, a floored room under a floorless one,
/// joined through the room's ceiling and the upper cell's floor, with a
/// flat cap on top that is nothing but a level opening down into it.
/// </summary>
internal sealed class DelveLandblock : IDatObjectSource
{
    public const uint Landblock = 0x04560000u;
    public const uint Ramp = Landblock | 0x0100u;
    public const uint Corridor = Landblock | 0x0101u;
    public const uint Beside = Landblock | 0x0102u;
    public const uint ShaftBottom = Landblock | 0x0103u;
    public const uint ShaftTop = Landblock | 0x0104u;
    public const uint Cap = Landblock | 0x0105u;

    private const ushort EnvironmentId = 0x0456;
    private const ushort RampPiece = 1;
    private const ushort CorridorPiece = 2;
    private const ushort BesidePiece = 3;
    private const ushort ShaftBottomPiece = 4;
    private const ushort ShaftTopPiece = 5;
    private const ushort CapPiece = 6;

    public const ushort FloorPolygon = 0;
    public const ushort CeilingPolygon = 1;
    public const ushort DoorwayPolygon = 10;

    private readonly Dictionary<uint, DBObj> _objects = new();

    public object Lock { get; } = new();

    public DelveLandblock()
    {
        Add(new LandBlockInfo { Id = Landblock | 0xFFFEu, NumCells = 6 });
        var environment = new DatEnvironment { Id = 0x0D000000u | EnvironmentId };
        environment.Cells[RampPiece] = RampStructure();
        environment.Cells[CorridorPiece] = CorridorStructure();
        environment.Cells[BesidePiece] = BesideStructure();
        environment.Cells[ShaftBottomPiece] = ShaftStructure(floor: true);
        environment.Cells[ShaftTopPiece] = ShaftStructure(floor: false);
        environment.Cells[CapPiece] = CapStructure();
        Add(environment);

        Add(Cell(Ramp, RampPiece, new Vector3(100f, 100f, 0f)));
        Add(Cell(Corridor, CorridorPiece, new Vector3(20f, 20f, 6f), (DoorwayPolygon, 0x0102)));
        Add(Cell(Beside, BesidePiece, new Vector3(20f, 12f, 12f), (DoorwayPolygon, 0x0101)));
        Add(Cell(ShaftBottom, ShaftBottomPiece, new Vector3(60f, 60f, 0f), (CeilingPolygon, 0x0104)));
        Add(Cell(ShaftTop, ShaftTopPiece, new Vector3(60f, 60f, 6f), (FloorPolygon, 0x0103)));
        Add(Cell(Cap, CapPiece, new Vector3(60f, 60f, 12f), (FloorPolygon, 0x0104)));
    }

    private void Add(DBObj value) => _objects[value.Id] = value;

    [return: MaybeNull]
    public T Get<T>(uint fileId) where T : DatReaderWriter.Lib.IO.IDBObj
    {
        if (!Monitor.IsEntered(Lock))
            throw new InvalidOperationException("read outside the lock");
        return _objects.TryGetValue(fileId, out DBObj? value) && value is T typed
            ? typed
            : default;
    }

    public bool TryGet<T>(uint fileId, [MaybeNullWhen(false)] out T value)
        where T : DatReaderWriter.Lib.IO.IDBObj
    {
        value = Get<T>(fileId);
        return value is not null;
    }

    private static EnvCell Cell(uint id, ushort structure, Vector3 origin, params (ushort Polygon, ushort OtherCell)[] portals)
    {
        var cell = new EnvCell
        {
            Id = id,
            EnvironmentId = EnvironmentId,
            CellStructure = structure,
            Position = new Frame { Origin = origin, Orientation = Quaternion.Identity },
        };
        foreach ((ushort polygon, ushort other) in portals)
            cell.CellPortals.Add(new CellPortal { PolygonId = polygon, OtherCellId = other, OtherPortalId = 0 });
        return cell;
    }

    /// <summary>A 10 by 8 metre floor climbing from z = 0 on the south edge to z = 6 on the north.</summary>
    private static CellStruct RampStructure()
    {
        var piece = new Piece();
        piece.Add(FloorPolygon, Vector3.UnitZ, new(0f, 0f, 0f), new(10f, 0f, 0f), new(10f, 8f, 6f), new(0f, 8f, 6f));
        return piece.Structure;
    }

    /// <summary>
    /// A 10 by 8 by 3 metre corridor. Its south wall stands either side of a
    /// two-metre doorway from x = 4 to 6, and a lintel from x = 3 to 7 sits
    /// on top of the doorway from z = 3 to 3.85.
    /// </summary>
    private static CellStruct CorridorStructure()
    {
        var piece = new Piece();
        piece.Add(FloorPolygon, Vector3.UnitZ, new(0f, 0f, 0f), new(10f, 0f, 0f), new(10f, 8f, 0f), new(0f, 8f, 0f));
        piece.Add(2, -Vector3.UnitY, new(0f, 0f, 0f), new(0f, 0f, 3f), new(4f, 0f, 3f), new(4f, 0f, 0f));
        piece.Add(3, -Vector3.UnitY, new(6f, 0f, 0f), new(6f, 0f, 3f), new(10f, 0f, 3f), new(10f, 0f, 0f));
        piece.Add(4, -Vector3.UnitY, new(3f, 0f, 3f), new(3f, 0f, 3.85f), new(7f, 0f, 3.85f), new(7f, 0f, 3f));
        piece.Add(DoorwayPolygon, -Vector3.UnitY, new(4f, 0f, 0f), new(4f, 0f, 3f), new(6f, 0f, 3f), new(6f, 0f, 0f));
        piece.Add(5, Vector3.UnitY, new(0f, 8f, 0f), new(10f, 8f, 0f), new(10f, 8f, 3f), new(0f, 8f, 3f));
        piece.Add(6, Vector3.UnitX, new(0f, 0f, 0f), new(0f, 8f, 0f), new(0f, 8f, 3f), new(0f, 0f, 3f));
        piece.Add(7, -Vector3.UnitX, new(10f, 0f, 0f), new(10f, 0f, 3f), new(10f, 8f, 3f), new(10f, 8f, 0f));
        return piece.Structure;
    }

    /// <summary>A floorless, roofed 10 by 8 by 3 metre box whose north wall holds a standing doorway.</summary>
    private static CellStruct BesideStructure()
    {
        var piece = new Piece();
        piece.Add(CeilingPolygon, -Vector3.UnitZ, new(0f, 8f, 3f), new(10f, 8f, 3f), new(10f, 0f, 3f), new(0f, 0f, 3f));
        piece.Add(2, Vector3.UnitY, new(0f, 0f, 0f), new(10f, 0f, 0f), new(10f, 0f, 3f), new(0f, 0f, 3f));
        piece.Add(DoorwayPolygon, -Vector3.UnitY, new(4f, 8f, 0f), new(4f, 8f, 3f), new(6f, 8f, 3f), new(6f, 8f, 0f));
        piece.Add(6, Vector3.UnitX, new(0f, 0f, 0f), new(0f, 8f, 0f), new(0f, 8f, 3f), new(0f, 0f, 3f));
        piece.Add(7, -Vector3.UnitX, new(10f, 0f, 0f), new(10f, 0f, 3f), new(10f, 8f, 3f), new(10f, 8f, 0f));
        return piece.Structure;
    }

    /// <summary>
    /// A 6 by 6 by 6 metre shaft piece. The bottom one has a floor and opens
    /// upward through its ceiling; the top one opens downward through its
    /// floor and is roofed.
    /// </summary>
    private static CellStruct ShaftStructure(bool floor)
    {
        var piece = new Piece();
        piece.Add(FloorPolygon, Vector3.UnitZ, new(0f, 0f, 0f), new(6f, 0f, 0f), new(6f, 6f, 0f), new(0f, 6f, 0f));
        piece.Add(CeilingPolygon, -Vector3.UnitZ, new(0f, 6f, 6f), new(6f, 6f, 6f), new(6f, 0f, 6f), new(0f, 0f, 6f));
        piece.Structure.Portals.Add(floor ? CeilingPolygon : FloorPolygon);
        piece.Add(2, Vector3.UnitY, new(0f, 0f, 0f), new(6f, 0f, 0f), new(6f, 0f, 6f), new(0f, 0f, 6f));
        piece.Add(3, -Vector3.UnitY, new(0f, 6f, 0f), new(0f, 6f, 6f), new(6f, 6f, 6f), new(6f, 6f, 0f));
        piece.Add(6, Vector3.UnitX, new(0f, 0f, 0f), new(0f, 6f, 0f), new(0f, 6f, 6f), new(0f, 0f, 6f));
        piece.Add(7, -Vector3.UnitX, new(6f, 0f, 0f), new(6f, 0f, 6f), new(6f, 6f, 6f), new(6f, 6f, 0f));
        return piece.Structure;
    }

    /// <summary>
    /// A cell with no thickness: one level 6 by 6 metre opening and nothing
    /// else, so the opening lies at the cell's own middle.
    /// </summary>
    private static CellStruct CapStructure()
    {
        var piece = new Piece();
        piece.Add(FloorPolygon, -Vector3.UnitZ, new(0f, 6f, 0f), new(6f, 6f, 0f), new(6f, 0f, 0f), new(0f, 0f, 0f));
        piece.Structure.Portals.Add(FloorPolygon);
        return piece.Structure;
    }

    /// <summary>A structure built one polygon at a time, each with corners of its own.</summary>
    private sealed class Piece
    {
        private ushort _nextVertex;

        public CellStruct Structure { get; } = new() { VertexArray = new VertexArray() };

        public void Add(ushort polygonId, Vector3 normal, params Vector3[] corners)
        {
            var polygon = new Polygon { SidesType = CullMode.Clockwise };
            foreach (Vector3 corner in corners)
            {
                ushort id = _nextVertex++;
                Structure.VertexArray.Vertices[id] = new SWVertex { Origin = corner, Normal = normal };
                polygon.VertexIds.Add((short)id);
            }
            Structure.Polygons[polygonId] = polygon;
        }
    }
}
