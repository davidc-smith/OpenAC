using System.Collections.Immutable;
using System.Numerics;
using AcDream.Runtime.Maps;

namespace AcDream.Runtime.Tests.Maps;

/// <summary>
/// The floorplan builder over <see cref="TwoRoomLandblock"/>: floors are the
/// up-facing polygons, walls the vertical ones with the doorway left out,
/// and a wall authored in two pieces comes out as one line.
/// </summary>
public sealed class DungeonFloorplanBuilderTests
{
    private const uint Landblock = TwoRoomLandblock.Landblock;

    [Fact]
    public void TwoRoomsBecomeTwoLayersOfFloorsAndWalls()
    {
        var dats = new TwoRoomLandblock();
        var builder = new DungeonFloorplanBuilder(dats, dats.Lock);

        DungeonFloorplan? plan = builder.TryBuild(TwoRoomLandblock.TurnedRoom);

        Assert.NotNull(plan);
        Assert.Equal(Landblock, plan.LandblockId);
        Assert.Equal([0f, 6f], plan.Layers.Select(static layer => layer.Z).ToArray());

        DungeonFloorplanLayer ground = plan.Layers[0];
        Assert.Single(ground.Floors);
        Assert.Equal(4, ground.Floors[0].Length);
        // Six faces: the ceiling faces down and the east wall is the doorway.
        Assert.Equal(3, ground.Walls.Length);
        Assert.Equal(
            [8f, 10f, 10f],
            ground.Walls.Select(static wall => wall.Length).Order().ToArray(),
            new ApproximateFloatComparer());
        // The south wall, local y = 0 from x 0 to 10, turned a quarter turn
        // about (20, 30): local x runs along world +y.
        Assert.Contains(ground.Walls, wall =>
            Matches(wall, new Vector2(20f, 30f), new Vector2(20f, 40f)));

        DungeonFloorplanLayer upstairs = plan.Layers[1];
        // Five wall polygons, four wall lines: the split north wall merged.
        Assert.Equal(4, upstairs.Walls.Length);
        Assert.Equal(
            [8f, 8f, 10f, 10f],
            upstairs.Walls.Select(static wall => wall.Length).Order().ToArray(),
            new ApproximateFloatComparer());
        Assert.Contains(upstairs.Walls, wall =>
            Matches(wall, new Vector2(40f, 38f), new Vector2(50f, 38f)));

        Assert.Equal(
            [TwoRoomLandblock.TurnedRoom, TwoRoomLandblock.UpstairsRoom],
            plan.Cells.Select(static cell => cell.CellId).ToArray());
        Assert.Equal(0f, plan.Cells[0].LayerZ);
        Assert.Equal(6f, plan.Cells[1].LayerZ);
        AssertVector(new Vector3(16f, 35f, 2f), plan.Cells[0].Center);

        AssertVector(new Vector3(12f, 30f, 0.5f), plan.Bounds.Min);
        AssertVector(new Vector3(50f, 40f, 10.2f), plan.Bounds.Max);

        Assert.Equal(2, plan.Counts.Floors);
        Assert.Equal(2, plan.Counts.Ceilings);
        Assert.Equal(1, plan.Counts.Portals);
        Assert.Equal(8, plan.Counts.Walls);
        Assert.Equal(0, plan.Counts.WallsAboveFloor);
        Assert.Equal(7, plan.Counts.WallsAfterMerge);
    }

    [Fact]
    public void ThePlanAndTheGeometryAreBuiltOnceAndReadUnderTheLock()
    {
        var dats = new TwoRoomLandblock();
        var builder = new DungeonFloorplanBuilder(dats, dats.Lock);

        DungeonFloorplan? first = builder.TryBuild(Landblock);
        int readsForOne = dats.Reads;
        DungeonFloorplan? again = builder.TryBuild(Landblock | 0xFFFFu);

        Assert.NotNull(first);
        Assert.Same(first, again);
        Assert.Equal(readsForOne, dats.Reads);
        Assert.Equal(2, builder.CachedGeometryCount);
        Assert.Equal(0, dats.ReadsOutsideTheLock);
    }

    [Fact]
    public void ALandblockWithoutCellsHasNoPlan()
    {
        var dats = new TwoRoomLandblock();
        var builder = new DungeonFloorplanBuilder(dats, dats.Lock);

        Assert.Null(builder.TryBuild(0x45670000u));
        Assert.Null(builder.TryBuild(TwoRoomLandblock.EmptyLandblock));
    }

    [Fact]
    public void ATallRoomsUpperCellFoldsIntoTheBandOfTheCellBelow()
    {
        var dats = new StackedRoomLandblock();
        var builder = new DungeonFloorplanBuilder(dats, dats.Lock);

        DungeonFloorplan? plan = builder.TryBuild(StackedRoomLandblock.Landblock);

        Assert.NotNull(plan);
        // The lower cell's origin is at z = 3.5, a storey up, but its floor
        // is at z = 2.5; the upper cell's origin is at 8.5 and it has no
        // floor; the lonely cell is at 15 and has no floor and no doorway.
        Assert.Equal([0f, 18f], plan.Layers.Select(static layer => layer.Z).ToArray());

        DungeonFloorplanLayer ground = plan.Layers[0];
        Assert.Single(ground.Floors);
        // Eight wall polygons, four per cell, one on top of the other: four lines.
        Assert.Equal(4, ground.Walls.Length);
        Assert.Contains(ground.Walls, wall =>
            Matches(wall, new Vector2(20f, 30f), new Vector2(30f, 30f)));

        DungeonFloorplanLayer lonely = plan.Layers[1];
        Assert.Empty(lonely.Floors);
        Assert.Equal(4, lonely.Walls.Length);

        Assert.Equal(
            [0f, 0f, 18f],
            plan.Cells.Select(static cell => cell.LayerZ).ToArray());
        Assert.Equal(1, plan.Counts.Floors);
        Assert.Equal(2, plan.Counts.Ceilings);
        Assert.Equal(1, plan.Counts.Portals);
        Assert.Equal(12, plan.Counts.Walls);
        // The upper cell's walls start six metres over the floor it folds onto.
        Assert.Equal(4, plan.Counts.WallsAboveFloor);
        Assert.Equal(8, plan.Counts.WallsAfterMerge);
    }

    /// <summary>
    /// Folding goes round until nothing more folds: a floorless cell whose
    /// only way down is another floorless cell is folded once that one has
    /// been. The fixture numbers its cells so a single pass meets the top
    /// before the middle, which a single pass would leave in its own band.
    /// </summary>
    [Fact]
    public void AFloorlessCellOverAnotherFloorlessCellFoldsAllTheWayDown()
    {
        var dats = new ChainedShaftLandblock();
        var builder = new DungeonFloorplanBuilder(dats, dats.Lock);

        DungeonFloorplan? plan = builder.TryBuild(ChainedShaftLandblock.Landblock);

        Assert.NotNull(plan);
        DungeonFloorplanLayer ground = Assert.Single(plan.Layers);
        Assert.Equal(0f, ground.Z);
        Assert.Single(ground.Floors);
        // Twelve wall polygons, four per cell, one on top of another: four lines.
        Assert.Equal(4, ground.Walls.Length);
        Assert.Equal(
            [ChainedShaftLandblock.BottomCell, ChainedShaftLandblock.MiddleCell, ChainedShaftLandblock.TopCell],
            plan.Cells.Select(static cell => cell.CellId).ToArray());
        Assert.Equal([0f, 0f, 0f], plan.Cells.Select(static cell => cell.LayerZ).ToArray());
        Assert.Equal(1, plan.Counts.Floors);
        Assert.Equal(1, plan.Counts.Ceilings);
        Assert.Equal(2, plan.Counts.Portals);
        Assert.Equal(12, plan.Counts.Walls);
        Assert.Equal(8, plan.Counts.WallsAboveFloor);
        Assert.Equal(4, plan.Counts.WallsAfterMerge);
    }

    [Fact]
    public void ARampsFloorKeepsItsRealHeights()
    {
        var dats = new DelveLandblock();
        DungeonFloorplan plan = new DungeonFloorplanBuilder(dats, dats.Lock).TryBuild(DelveLandblock.Landblock)!;

        DungeonFloorplanCell ramp = plan.Cells.Single(static cell => cell.CellId == DelveLandblock.Ramp);
        ImmutableArray<Vector3> floor = Assert.Single(ramp.Floors);
        Assert.Equal(
            [new(100f, 100f, 0f), new(110f, 100f, 0f), new(110f, 108f, 6f), new(100f, 108f, 6f)],
            floor.ToArray());
    }

    [Fact]
    public void ALintelOverADoorwayIsNotAWallAndTheDoorwayIsAGap()
    {
        var dats = new DelveLandblock();
        DungeonFloorplan plan = new DungeonFloorplanBuilder(dats, dats.Lock).TryBuild(DelveLandblock.Landblock)!;

        DungeonFloorplanLayer band = plan.Layers.Single(static layer => layer.Z == 6f);
        DungeonFloorplanWall[] south = band.Walls
            .Where(static wall => MathF.Abs(wall.Start.Y - 20f) < 0.01f && MathF.Abs(wall.End.Y - 20f) < 0.01f)
            .ToArray();
        Assert.Equal(2, south.Length);
        Assert.Contains(south, wall => Matches(wall, new Vector2(20f, 20f), new Vector2(24f, 20f)));
        Assert.Contains(south, wall => Matches(wall, new Vector2(26f, 20f), new Vector2(30f, 20f)));
    }

    [Fact]
    public void ATallRoomsUpperCellFoldsDownAndItsWallsAreDropped()
    {
        var dats = new DelveLandblock();
        DungeonFloorplan plan = new DungeonFloorplanBuilder(dats, dats.Lock).TryBuild(DelveLandblock.Landblock)!;

        DungeonFloorplanCell top = plan.Cells.Single(static cell => cell.CellId == DelveLandblock.ShaftTop);
        Assert.Equal(0f, top.LayerZ);
        Assert.Empty(top.Floors);
        Assert.DoesNotContain(plan.Layers, static layer => layer.Z == 6f && layer.Walls.Any(static wall => wall.Start.X >= 59f));
        // The shaft's four walls outline it once, from the bottom cell; the
        // top cell's four, six metres up, and the corridor's lintel are dropped.
        DungeonFloorplanLayer ground = plan.Layers.Single(static layer => layer.Z == 0f);
        Assert.Equal(4, ground.Walls.Count(static wall => wall.Start.X >= 59f));
        Assert.Equal(5, plan.Counts.WallsAboveFloor);
    }

    [Fact]
    public void AFlatCellFoldsOntoTheCellItsOpeningLeadsDownTo()
    {
        var dats = new DelveLandblock();
        DungeonFloorplan plan = new DungeonFloorplanBuilder(dats, dats.Lock).TryBuild(DelveLandblock.Landblock)!;

        Assert.Equal(0f, plan.Cells.Single(static cell => cell.CellId == DelveLandblock.Cap).LayerZ);
    }

    [Fact]
    public void AFloorlessCellBesideALowerCellDoesNotFoldSideways()
    {
        var dats = new DelveLandblock();
        DungeonFloorplan plan = new DungeonFloorplanBuilder(dats, dats.Lock).TryBuild(DelveLandblock.Landblock)!;

        DungeonFloorplanCell beside = plan.Cells.Single(static cell => cell.CellId == DelveLandblock.Beside);
        Assert.Equal(12f, beside.LayerZ);
        // With no way down it keeps every wall, so it does not vanish.
        DungeonFloorplanLayer band = plan.Layers.Single(static layer => layer.Z == 12f);
        Assert.Equal(3, band.Walls.Length);
    }

    [Fact]
    public void ABridgesDeckIsItsCellsObjectFloorAndBandsIt()
    {
        var dats = new DelveLandblock();
        DungeonFloorplan plan = new DungeonFloorplanBuilder(dats, dats.Lock).TryBuild(DelveLandblock.Landblock)!;

        DungeonFloorplanCell bridge = plan.Cells.Single(static cell => cell.CellId == DelveLandblock.Bridge);
        Assert.Empty(bridge.Floors);
        // The deck, turned a quarter turn about (200, 200, 12), and the
        // small shelf; not the underside, which faces down, nor the railing.
        Assert.Equal(2, bridge.ObjectFloors.Length);
        ImmutableArray<Vector3> deck = bridge.ObjectFloors[0];
        Assert.Equal(4, deck.Length);
        AssertVector(new Vector3(200f, 200f, 12f), deck[0]);
        AssertVector(new Vector3(200f, 204f, 12f), deck[1]);
        AssertVector(new Vector3(198f, 204f, 12f), deck[2]);
        AssertVector(new Vector3(198f, 200f, 12f), deck[3]);
        Assert.All(bridge.ObjectFloors[1], static corner => Assert.Equal(23f, corner.Z, 3));

        // Banded by the deck, not folded, and not lifted by the shelf.
        Assert.Equal(12f, bridge.LayerZ);
        Assert.Contains(
            plan.Layers.Single(static layer => layer.Z == 12f).Floors,
            static floor => floor.Length == 4 && floor.All(static corner => corner.X is >= 197.9f and <= 200.1f));
        Assert.Contains(
            plan.Layers.Single(static layer => layer.Z == 24f).Floors,
            static floor => floor.All(static corner => corner.X is >= 198f and <= 199.1f));
    }

    [Fact]
    public void ASetupsPartsAreEachPlacedByTheirOwnTransform()
    {
        var dats = new DelveLandblock();
        DungeonFloorplan plan = new DungeonFloorplanBuilder(dats, dats.Lock).TryBuild(DelveLandblock.Landblock)!;

        DungeonFloorplanCell platform = plan.Cells.Single(static cell => cell.CellId == DelveLandblock.Platform);
        Assert.Equal(2, platform.ObjectFloors.Length);
        Assert.Equal(
            [new(300f, 300f, 18f), new(301f, 300f, 18f), new(301f, 301f, 18f), new(300f, 301f, 18f)],
            platform.ObjectFloors[0].ToArray());
        Assert.Equal(
            [new(302f, 300f, 19f), new(303f, 300f, 19f), new(303f, 301f, 19f), new(302f, 301f, 19f)],
            platform.ObjectFloors[1].ToArray());
        Assert.Equal(18f, platform.LayerZ);
    }

    [Fact]
    public void AnObjectNeverMovesACellWithAFloorOfItsOwn()
    {
        var dats = new DelveLandblock();
        DungeonFloorplan plan = new DungeonFloorplanBuilder(dats, dats.Lock).TryBuild(DelveLandblock.Landblock)!;

        DungeonFloorplanCell room = plan.Cells.Single(static cell => cell.CellId == DelveLandblock.TableRoom);
        Assert.Equal(0f, room.LayerZ);
        Assert.Single(room.Floors);
        ImmutableArray<Vector3> top = Assert.Single(room.ObjectFloors);
        Assert.All(top, static corner => Assert.Equal(1f, corner.Z));
        Assert.Equal(new Vector3(402f, 402f, 1f), top[0]);
    }

    [Fact]
    public void EveryObjectFloorIsCounted()
    {
        var dats = new DelveLandblock();
        var builder = new DungeonFloorplanBuilder(dats, dats.Lock);

        DungeonFloorplan? plan = builder.TryBuild(DelveLandblock.Landblock);

        Assert.NotNull(plan);
        Assert.Equal(5, plan.Counts.ObjectFloors);
    }

    private static bool Matches(DungeonFloorplanWall wall, Vector2 a, Vector2 b) =>
        (Near(wall.Start, a) && Near(wall.End, b))
        || (Near(wall.Start, b) && Near(wall.End, a));

    private static bool Near(Vector2 actual, Vector2 expected) =>
        Vector2.Distance(actual, expected) < 0.01f;

    private static void AssertVector(Vector3 expected, Vector3 actual) =>
        Assert.True(
            Vector3.Distance(expected, actual) < 0.01f,
            $"expected {expected}, got {actual}");

    private sealed class ApproximateFloatComparer : IEqualityComparer<float>
    {
        public bool Equals(float x, float y) => MathF.Abs(x - y) < 0.01f;
        public int GetHashCode(float value) => 0;
    }
}
