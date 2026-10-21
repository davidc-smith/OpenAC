using System.Numerics;

namespace AcDream.Plugin.Abstractions;

/// <summary>
/// A wall seen from above: one straight line in landblock-local metres. See
/// <see cref="PluginDungeonFloorplan"/> for the frame.
/// </summary>
/// <param name="Start">One end of the wall.</param>
/// <param name="End">The other end.</param>
public readonly record struct PluginDungeonWall(Vector2 Start, Vector2 End);

/// <summary>
/// One cell of a floorplan: where it is and which layer it was drawn in.
/// </summary>
/// <param name="CellId">The full cell id, landblock in the high half.</param>
/// <param name="Center">
/// The middle of the cell's geometry, in landblock-local metres. In a tall room it is well
/// above the floor; a walk or preview to it stands it on the floor under it.
/// </param>
/// <param name="LayerZ">The <see cref="PluginDungeonLayer.Z"/> of the layer the cell is drawn in.</param>
public readonly record struct PluginDungeonCell(uint CellId, Vector3 Center, float LayerZ)
{
    private readonly IReadOnlyList<IReadOnlyList<Vector3>>? _floors;

    /// <summary>
    /// The cell's own floor: every polygon of its geometry that faces up (the
    /// same test the floorplan uses for floor), corners in landblock-local
    /// metres with their real heights, in the order the data gives them. A
    /// ramp's polygons slope; a flat floor's corners share one height. The
    /// height under a point is the plane of the polygon under it. Empty for
    /// a cell with no floor (the upper cell of a tall room). Two cells are
    /// equal only when their floors are equal corner by corner. Needs
    /// contract <c>0.1.26-fork.3</c> or later; an older host leaves it empty.
    /// </summary>
    public IReadOnlyList<IReadOnlyList<Vector3>> Floors
    {
        get => _floors ?? Array.Empty<IReadOnlyList<Vector3>>();
        init => _floors = value;
    }

    /// <summary>Compares every member, the floors corner by corner.</summary>
    public bool Equals(PluginDungeonCell other)
    {
        if (CellId != other.CellId || Center != other.Center || !LayerZ.Equals(other.LayerZ))
            return false;
        IReadOnlyList<IReadOnlyList<Vector3>> floors = Floors;
        IReadOnlyList<IReadOnlyList<Vector3>> others = other.Floors;
        if (floors.Count != others.Count)
            return false;
        for (int index = 0; index < floors.Count; index++)
        {
            if (!floors[index].SequenceEqual(others[index]))
                return false;
        }
        return true;
    }

    /// <inheritdoc/>
    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(CellId);
        hash.Add(Center);
        hash.Add(LayerZ);
        foreach (IReadOnlyList<Vector3> floor in Floors)
        {
            foreach (Vector3 corner in floor)
                hash.Add(corner);
        }
        return hash.ToHashCode();
    }
}

/// <summary>Which way an opening of a cell faces.</summary>
public enum PluginDoorwayKind
{
    /// <summary>
    /// Not known: the data lacks the opening's polygon, or the host is older
    /// than contract <c>0.1.26-fork.3</c>.
    /// </summary>
    Unknown = 0,

    /// <summary>
    /// A standing opening walked through: a door, an arch or a corridor's
    /// end, a stair's sloped opening included.
    /// </summary>
    Upright,

    /// <summary>A level opening in this cell's floor: the cell beyond is below.</summary>
    Floor,

    /// <summary>A level opening in this cell's ceiling: the cell beyond is above.</summary>
    Ceiling,
}

/// <summary>
/// One opening of an indoor cell: a doorway, stairwell or other gap the data
/// authors as a cell portal, through which the cell opens onto its
/// neighbour. Following a cell's doorways from cell to cell walks the
/// dungeon the way the rooms join, which straight-line distance does not.
/// A door is an object standing in a doorway, not part of it: a doorway is
/// listed whether its door is open, shut or locked, so a plugin that wants
/// to know matches the door objects it sees against <see cref="Center"/>.
/// </summary>
/// <param name="OtherCellId">
/// The full id of the cell on the other side, landblock in the high half;
/// zero for an opening onto the landscape, as a building's front door is.
/// </param>
/// <param name="Center">
/// The middle of the doorway, in landblock-local metres (the frame of
/// <see cref="PluginIndoorCell.Origin"/>): the middle of the box around its
/// corners, placed as the cell is, so both sides of one doorway give the
/// same point. The cell's own origin when the data lacks the doorway's
/// polygon.
/// </param>
public readonly record struct PluginCellDoorway(uint OtherCellId, Vector3 Center)
{
    /// <summary>True when the doorway opens onto the landscape rather than another cell.</summary>
    public bool LeadsOutside => OtherCellId == 0u;

    /// <summary>
    /// Which way the opening faces: <see cref="PluginDoorwayKind.Upright"/>
    /// for one walked through, <see cref="PluginDoorwayKind.Floor"/> or
    /// <see cref="PluginDoorwayKind.Ceiling"/> for a level one (a tall room
    /// or a shaft), told apart by whether it is below or above the middle of
    /// the cell's geometry (<see cref="PluginDungeonCell.Center"/>), or, in a
    /// flat cell that is nothing but the opening, by whether the cell beyond
    /// is lower. Needs contract <c>0.1.26-fork.3</c> or later; an
    /// older host, or a doorway whose polygon the data lacks, gives
    /// <see cref="PluginDoorwayKind.Unknown"/>.
    /// </summary>
    public PluginDoorwayKind Kind { get; init; }

    /// <summary>
    /// The height of the opening's lowest corner, in landblock-local metres:
    /// where a walker crosses an upright doorway. About
    /// <see cref="Center"/>'s Z for a level opening. The cell's origin
    /// height when the data lacks the doorway's polygon. Needs contract
    /// <c>0.1.26-fork.3</c> or later; an older host leaves it 0.
    /// </summary>
    public float Sill { get; init; }
}

/// <summary>
/// One indoor cell exactly as the game data authors it: which environment
/// piece it is built from and how that piece is placed in the landblock. It
/// is the raw placement, not a drawing: a plugin that draws dungeons from
/// its own tiles, one per environment piece, places each tile with this.
/// </summary>
/// <param name="CellId">The full cell id, landblock in the high half.</param>
/// <param name="EnvironmentId">
/// The environment piece the cell is built from, as the cell stores it: the
/// low sixteen bits of the environment file's id. Zero for a cell that names
/// none.
/// </param>
/// <param name="CellStructure">
/// Which of that environment piece's structures the cell uses.
/// </param>
/// <param name="Origin">
/// Where the piece's own origin sits, in landblock-local metres (x east and
/// y north from the landblock's south-west corner, z up), exactly as stored.
/// </param>
/// <param name="Orientation">
/// How the piece is turned, exactly as stored. The data stores the four
/// components in the order W, X, Y, Z; a piece turned only about the
/// vertical axis by an angle a has W = cos(a/2) and Z = sin(a/2), with a
/// positive a turning east towards north. Identity for a cell that stores no
/// placement.
/// </param>
/// <param name="SeesOutside">
/// True for a cell that sees the landscape, as a building's rooms do; false
/// for a sealed dungeon's cell, the same answer
/// <see cref="IDungeonMapAutomation.IsSealedDungeon"/> gives in reverse.
/// </param>
public readonly record struct PluginIndoorCell(
    uint CellId,
    uint EnvironmentId,
    uint CellStructure,
    Vector3 Origin,
    Quaternion Orientation,
    bool SeesOutside)
{
    private readonly IReadOnlyList<PluginCellDoorway>? _doorways;

    /// <summary>
    /// The cell's openings onto its neighbours, in the order the data lists
    /// them; empty for a cell with none. Every doorway between two cells is
    /// listed from both sides, almost always at the same
    /// <see cref="PluginCellDoorway.Center"/>. The exception is one opening
    /// shared by several cells, such as a big room's ceiling under smaller
    /// cells: the room lists the whole opening once for each of them, while
    /// each of them lists only its own piece, sometimes in several parts, so
    /// one neighbour can appear more than once and the two sides' centres
    /// differ. Two cells are equal only when their
    /// doorways are equal in the same order. Needs contract
    /// <c>0.1.25-fork.6</c> or later; an older host leaves it empty.
    /// </summary>
    public IReadOnlyList<PluginCellDoorway> Doorways
    {
        get => _doorways ?? Array.Empty<PluginCellDoorway>();
        init => _doorways = value;
    }

    /// <summary>Compares every member, the doorways item by item.</summary>
    public bool Equals(PluginIndoorCell other) =>
        CellId == other.CellId
        && EnvironmentId == other.EnvironmentId
        && CellStructure == other.CellStructure
        && Origin == other.Origin
        && Orientation == other.Orientation
        && SeesOutside == other.SeesOutside
        && Doorways.SequenceEqual(other.Doorways);

    /// <inheritdoc/>
    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(CellId);
        hash.Add(EnvironmentId);
        hash.Add(CellStructure);
        hash.Add(Origin);
        hash.Add(Orientation);
        hash.Add(SeesOutside);
        foreach (PluginCellDoorway doorway in Doorways)
            hash.Add(doorway);
        return hash.ToHashCode();
    }

    /// <summary>
    /// The turn about the vertical axis <see cref="Orientation"/> describes,
    /// in degrees from 0 up to 360, counted from east towards north. For the
    /// quarter turns dungeon pieces are laid at this is 0, 90, 180 or 270 to
    /// within rounding; a piece also tilted about another axis reads only
    /// its turn about the vertical.
    /// </summary>
    public float YawDegrees
    {
        get
        {
            Quaternion q = Orientation;
            double yaw = Math.Atan2(
                2d * ((q.W * q.Z) + (q.X * q.Y)),
                1d - (2d * ((q.Y * q.Y) + (q.Z * q.Z))));
            double degrees = yaw * (180d / Math.PI);
            if (degrees < 0d)
                degrees += 360d;
            return degrees >= 360d ? 0f : (float)degrees;
        }
    }
}

/// <summary>
/// One storey of a floorplan: the cells whose floor lies in one six-metre
/// band of height, flattened together. A cell with no floor of its own, such
/// as the upper cell of a tall room, is drawn in the band of the cell it
/// opens onto below through a level opening in its floor, however many
/// floorless cells sit between; a cell with no floor and no way down -- a
/// doorway beside it does not count -- is drawn in the band its origin is in.
/// </summary>
/// <param name="Z">
/// The lowest height the band covers, in metres; bands are six metres tall
/// and shifted down three, so a floor at height z is in the band
/// floor((z + 3) / 6) * 6.
/// </param>
/// <param name="Floors">
/// The floor area as closed polygons, each vertex in landblock-local metres,
/// in the order the data gives them. The polygons overlap wherever two cells
/// meet; fill each one and the room comes out whole.
/// </param>
/// <param name="Walls">
/// The walls as lines, collinear runs already joined. Only the standing faces
/// that reach down to the floor they stand on are walls: a door's lintel, and
/// the walls of a tall room's upper cells, are left out, so every doorway is
/// a gap in its wall line. Before contract <c>0.1.26-fork.3</c> every
/// standing face was a wall and a lintel closed its doorway.
/// </param>
public sealed record PluginDungeonLayer(
    float Z,
    IReadOnlyList<IReadOnlyList<Vector2>> Floors,
    IReadOnlyList<PluginDungeonWall> Walls);

/// <summary>
/// One landblock's indoor cells seen from above, worked out from the cell
/// geometry the game data carries: level faces are floor, standing faces
/// are walls, and the doorways between cells are left open. Everything is
/// in the landblock's own frame and in metres: x grows east and y grows
/// north from the landblock's south-west corner, the frame the game's own
/// cell positions use. The plan is built once and never changed, so it is
/// safe to keep and to read from any thread.
/// </summary>
/// <param name="LandblockId">The landblock the plan is of, with a zero low half.</param>
/// <param name="Layers">The storeys, lowest first.</param>
/// <param name="Cells">Every cell with geometry, in cell id order.</param>
/// <param name="BoundsMin">The lowest corner of the box around every cell's geometry, in landblock-local metres.</param>
/// <param name="BoundsMax">The highest corner of that box.</param>
public sealed record PluginDungeonFloorplan(
    uint LandblockId,
    IReadOnlyList<PluginDungeonLayer> Layers,
    IReadOnlyList<PluginDungeonCell> Cells,
    Vector3 BoundsMin,
    Vector3 BoundsMax)
{
    /// <summary>
    /// The plan of nothing: what comes back for a landblock the host does
    /// not know, a landblock with no indoor cells, or a host that cannot
    /// read the game data.
    /// </summary>
    public static PluginDungeonFloorplan Empty { get; } =
        new(0u, Array.Empty<PluginDungeonLayer>(), Array.Empty<PluginDungeonCell>(), Vector3.Zero, Vector3.Zero);

    /// <summary>True when there is nothing to draw.</summary>
    public bool IsEmpty => Layers.Count == 0;

    /// <summary>
    /// A position in the plan's frame: metres east and north of the
    /// south-west corner of the position's own landblock, and metres up.
    /// Compare its landblock (<see cref="PluginNavigationPosition.CellId"/>
    /// with a zero low half) with <see cref="LandblockId"/> before drawing
    /// it on this plan.
    /// </summary>
    /// <param name="position">A position as the navigation surface reports it.</param>
    public static Vector3 ToLandblockLocal(in PluginNavigationPosition position)
    {
        uint blockX = (position.CellId >> 24) & 0xFFu;
        uint blockY = (position.CellId >> 16) & 0xFFu;
        return new Vector3(
            (float)((position.EastWest * 240d) + 84d - (((double)blockX - 127d) * 192d)),
            (float)((position.NorthSouth * 240d) + 84d - (((double)blockY - 127d) * 192d)),
            (float)(position.Elevation * 240d));
    }
}

/// <summary>
/// The shape of the dungeon the character is in, for a plugin drawing a map
/// of it. The plan is data, not a picture: the plugin draws it however it
/// likes, on whatever it has to draw with.
/// </summary>
public interface IDungeonMapAutomation
{
    /// <summary>
    /// Whether <paramref name="cellId"/> is an indoor cell that sees nothing
    /// outside, as a dungeon's cells are; false for outdoor cells, for the
    /// interiors of buildings that open onto the landscape, for cells the
    /// data lacks, and on a host that cannot read the game data, which is
    /// what the default implementation always reports.
    /// </summary>
    /// <param name="cellId">The full cell id, landblock in the high half.</param>
    bool IsSealedDungeon(uint cellId) => false;

    /// <summary>
    /// The landblock the character stands in, with a zero low half, or zero
    /// when the character has no position yet, which is also what the
    /// default implementation always reports.
    /// </summary>
    uint CurrentLandblockId => 0u;

    /// <summary>
    /// The floorplan of a landblock's indoor cells, built from the game data
    /// the first time it is asked for and the same plan every time after.
    /// Any cell id in the landblock will do. Reading the data can take a few
    /// milliseconds on a large dungeon the first time; the plan is worth
    /// keeping. <see cref="PluginDungeonFloorplan.Empty"/> comes back for a
    /// landblock the data does not have, for one with no indoor cells, and
    /// on a host that cannot read the game data, which is what the default
    /// implementation always returns.
    /// </summary>
    /// <param name="landblockId">The landblock, or any cell in it.</param>
    PluginDungeonFloorplan CaptureFloorplan(uint landblockId) => PluginDungeonFloorplan.Empty;

    /// <summary>
    /// Every indoor cell of a landblock as the game data authors it -- the
    /// environment piece each is built from and how it is placed -- in cell
    /// id order, read from the game data the first time it is asked for and
    /// the same list every time after. Unlike the floorplan this is the raw
    /// placement of every cell the landblock lists, geometry or not, with
    /// the doorways each one opens through (<see cref="PluginIndoorCell.Doorways"/>),
    /// which between them are the landblock's cell graph. Empty
    /// for a landblock the data does not have, one with no indoor cells, and
    /// on a host that cannot read the game data, which is what the default
    /// implementation always returns.
    /// </summary>
    /// <param name="landblockId">The landblock, or any cell in it.</param>
    IReadOnlyList<PluginIndoorCell> CaptureIndoorCells(uint landblockId) =>
        Array.Empty<PluginIndoorCell>();
}
