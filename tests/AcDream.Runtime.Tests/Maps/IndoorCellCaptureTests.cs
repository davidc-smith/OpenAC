using System.Numerics;
using AcDream.Plugin.Abstractions;
using AcDream.Runtime.Maps;

namespace AcDream.Runtime.Tests.Maps;

/// <summary>
/// A plugin that draws dungeons from its own tiles needs each cell's
/// environment piece and placement exactly as the data stores them, for
/// every cell the landblock lists -- including a cell with no geometry the
/// floorplan leaves out.
///
/// Mutation check (2026-09-25): reading the orientation as identity turned
/// <see cref="EveryListedCellComesBackWithItsPieceAndPlacement"/> red.
/// </summary>
public sealed class IndoorCellCaptureTests
{
    [Fact]
    public void EveryListedCellComesBackWithItsPieceAndPlacement()
    {
        var content = new TwoRoomLandblock();
        var map = new RuntimeDungeonMapAutomation();
        map.BindContent(content, content.Lock);

        IReadOnlyList<PluginIndoorCell> cells =
            map.CaptureIndoorCells(TwoRoomLandblock.TurnedRoom);

        Assert.Equal(
            [TwoRoomLandblock.TurnedRoom, TwoRoomLandblock.UpstairsRoom, TwoRoomLandblock.CellSeeingOutside],
            cells.Select(static cell => cell.CellId));

        PluginIndoorCell turned = cells[0];
        Assert.Equal(0x0123u, turned.EnvironmentId);
        Assert.Equal(1u, turned.CellStructure);
        Assert.Equal(new Vector3(20f, 30f, 0.5f), turned.Origin);
        Assert.Equal(Quaternion.CreateFromAxisAngle(Vector3.UnitZ, MathF.PI / 2f), turned.Orientation);
        Assert.Equal(90f, turned.YawDegrees, 3);
        Assert.False(turned.SeesOutside);

        PluginIndoorCell upstairs = cells[1];
        Assert.Equal(2u, upstairs.CellStructure);
        Assert.Equal(new Vector3(40f, 30f, 7.2f), upstairs.Origin);
        Assert.Equal(0f, upstairs.YawDegrees, 3);

        PluginIndoorCell outside = cells[2];
        Assert.Equal(0u, outside.EnvironmentId);
        Assert.True(outside.SeesOutside);

        Assert.Equal(0, content.ReadsOutsideTheLock);
        int reads = content.Reads;
        Assert.Same(cells, map.CaptureIndoorCells(TwoRoomLandblock.Landblock));
        Assert.Equal(reads, content.Reads);
    }

    [Fact]
    public void EachCellListsTheDoorwaysItOpensThrough()
    {
        var content = new TwoRoomLandblock();
        var map = new RuntimeDungeonMapAutomation();
        map.BindContent(content, content.Lock);

        IReadOnlyList<PluginIndoorCell> cells =
            map.CaptureIndoorCells(TwoRoomLandblock.Landblock);

        // The turned room's east wall, the middle of it at (10, 4, 1.5) in
        // the piece's frame, turned a quarter towards north and moved to the
        // cell's origin.
        PluginCellDoorway east = Assert.Single(cells[0].Doorways);
        Assert.Equal(TwoRoomLandblock.UpstairsRoom, east.OtherCellId);
        Assert.Equal(16f, east.Center.X, 3);
        Assert.Equal(40f, east.Center.Y, 3);
        Assert.Equal(2f, east.Center.Z, 3);

        // A way out to the landscape names no cell; a doorway whose polygon
        // the piece lacks stands at the cell's origin.
        PluginCellDoorway outside = Assert.Single(cells[1].Doorways);
        Assert.Equal(0u, outside.OtherCellId);
        Assert.True(outside.LeadsOutside);
        Assert.Equal(new Vector3(40f, 30f, 7.2f), outside.Center);

        Assert.Empty(cells[2].Doorways);
        Assert.Equal(0, content.ReadsOutsideTheLock);
    }

    [Fact]
    public void EachDoorwaySaysWhichWayItFacesAndWhereItsSillIs()
    {
        var content = new DelveLandblock();
        var map = new RuntimeDungeonMapAutomation();
        map.BindContent(content, content.Lock);

        IReadOnlyList<PluginIndoorCell> cells = map.CaptureIndoorCells(DelveLandblock.Landblock);
        PluginIndoorCell Cell(uint id) => cells.Single(cell => cell.CellId == id);

        PluginCellDoorway door = Assert.Single(Cell(DelveLandblock.Corridor).Doorways);
        Assert.Equal(PluginDoorwayKind.Upright, door.Kind);
        Assert.Equal(6f, door.Sill, 3);

        PluginCellDoorway up = Assert.Single(Cell(DelveLandblock.ShaftBottom).Doorways);
        Assert.Equal(PluginDoorwayKind.Ceiling, up.Kind);
        Assert.Equal(6f, up.Sill, 3);

        PluginCellDoorway down = Assert.Single(Cell(DelveLandblock.ShaftTop).Doorways);
        Assert.Equal(PluginDoorwayKind.Floor, down.Kind);
        Assert.Equal(6f, down.Sill, 3);

        // A flat cell's opening is at its own middle; the cell beyond is lower.
        PluginCellDoorway cap = Assert.Single(Cell(DelveLandblock.Cap).Doorways);
        Assert.Equal(PluginDoorwayKind.Floor, cap.Kind);
    }

    [Fact]
    public void ADoorwayWhosePolygonIsMissingIsUnknownAtTheOriginsHeight()
    {
        var content = new TwoRoomLandblock();
        var map = new RuntimeDungeonMapAutomation();
        map.BindContent(content, content.Lock);

        PluginCellDoorway outside = Assert.Single(map.CaptureIndoorCells(TwoRoomLandblock.Landblock)[1].Doorways);

        Assert.Equal(PluginDoorwayKind.Unknown, outside.Kind);
        Assert.Equal(7.2f, outside.Sill, 3);
    }

    [Fact]
    public void ACellMadeWithoutDoorwaysHasAnEmptyList()
    {
        Assert.Empty(default(PluginIndoorCell).Doorways);
        Assert.Empty(new PluginIndoorCell(
            0x01230100u, 1u, 0u, Vector3.Zero, Quaternion.Identity, false).Doorways);
    }

    [Fact]
    public void CellsWithTheSameDoorwaysAreEqual()
    {
        var first = new PluginIndoorCell(
            0x01230100u, 1u, 0u, Vector3.Zero, Quaternion.Identity, false)
        {
            Doorways = [new PluginCellDoorway(0x01230101u, Vector3.One)],
        };
        var second = first with
        {
            Doorways = [new PluginCellDoorway(0x01230101u, Vector3.One)],
        };
        var other = first with
        {
            Doorways = [new PluginCellDoorway(0x01230102u, Vector3.One)],
        };

        Assert.Equal(first, second);
        Assert.Equal(first.GetHashCode(), second.GetHashCode());
        Assert.NotEqual(first, other);
    }

    [Fact]
    public void ALandblockWithoutCellsOrWithoutFilesHasNone()
    {
        var content = new TwoRoomLandblock();
        var map = new RuntimeDungeonMapAutomation();
        Assert.Empty(map.CaptureIndoorCells(TwoRoomLandblock.Landblock));

        map.BindContent(content, content.Lock);
        Assert.Empty(map.CaptureIndoorCells(TwoRoomLandblock.EmptyLandblock));
        Assert.Empty(map.CaptureIndoorCells(0x7F7F0000u));
    }

    [Fact]
    public void TheSharedListCannotBeChangedByAPlugin()
    {
        var content = new TwoRoomLandblock();
        var map = new RuntimeDungeonMapAutomation();
        map.BindContent(content, content.Lock);

        IReadOnlyList<PluginIndoorCell> cells =
            map.CaptureIndoorCells(TwoRoomLandblock.Landblock);

        Assert.IsNotType<PluginIndoorCell[]>(cells);
        var writable = Assert.IsAssignableFrom<IList<PluginIndoorCell>>(cells);
        Assert.Throws<NotSupportedException>(() => writable[0] = default);
    }

    [Fact]
    public void TheLandblockReadLongestAgoIsDroppedOnceTheCacheIsFull()
    {
        var content = new TwoRoomLandblock();
        var map = new RuntimeDungeonMapAutomation();
        map.BindContent(content, content.Lock);
        IReadOnlyList<PluginIndoorCell> first =
            map.CaptureIndoorCells(TwoRoomLandblock.Landblock);

        for (uint index = 1; index < RuntimeDungeonMapAutomation.MaximumCachedIndoorLandblocks; index++)
            _ = map.CaptureIndoorCells(0x70000000u + (index << 16));
        Assert.Same(first, map.CaptureIndoorCells(TwoRoomLandblock.Landblock));

        _ = map.CaptureIndoorCells(0x6F000000u);
        IReadOnlyList<PluginIndoorCell> again =
            map.CaptureIndoorCells(TwoRoomLandblock.Landblock);
        Assert.NotSame(first, again);
        Assert.Equal(first, again);
    }

    [Theory]
    [InlineData(0f, 0f)]
    [InlineData(90f, 90f)]
    [InlineData(180f, 180f)]
    [InlineData(270f, 270f)]
    [InlineData(-90f, 270f)]
    public void YawIsCountedFromEastTowardsNorth(float turnDegrees, float expected)
    {
        var cell = new PluginIndoorCell(
            0x01230100u, 1u, 0u, Vector3.Zero,
            Quaternion.CreateFromAxisAngle(Vector3.UnitZ, turnDegrees * MathF.PI / 180f),
            false);
        Assert.Equal(expected, cell.YawDegrees, 2);
    }
}
