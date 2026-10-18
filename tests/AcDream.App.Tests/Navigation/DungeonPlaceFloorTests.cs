using System.Numerics;
using AcDream.Core.Physics;
using AcDream.Plugin.Abstractions;
using AcDream.Runtime.Navigation;
using static AcDream.App.Tests.Navigation.NavigationWalkControllerTests;

namespace AcDream.App.Tests.Navigation;

/// <summary>
/// A place inside a dungeon cell is often given by the middle of the cell's bounds, which in a
/// tall room is metres above its floor. Such a place stands on the floor under it.
/// </summary>
public sealed class DungeonPlaceFloorTests
{
    private const uint LowRoom = 0xA9B40100u;
    private const uint TallRoom = 0xA9B40101u;
    private const uint StackedLower = 0xA9B40102u;
    private const uint StackedUpper = 0xA9B40103u;
    private const float Floor = -30f;

    /// <summary>The middle of the 5 m room's bounds, 2.5 m over its floor, as a dungeon map gives it.</summary>
    private static readonly Vector3 TallMiddle = new(30f, 10f, -27.5f);

    /// <summary>The middle of the floorless upper cell's bounds, 7.5 m over the floor of the cell below.</summary>
    private static readonly Vector3 UpperMiddle = new(50f, 10f, -22.5f);

    /// <summary>
    /// Why the fix is needed: a goal left in mid-air, higher over the floor than a walk can arrive
    /// at, has no route at all, though the room is open to walk into.
    /// </summary>
    [Fact]
    public async Task APlaceLeftInMidAirOverATallRoomsFloorHasNoRoute()
    {
        PluginNavigationPlan plan = await Preview(new Goals(), TallRoom, TallMiddle);

        Assert.Equal(PluginNavigationPlanStatus.NoRoute, plan.Status);
    }

    [Fact]
    public async Task APlaceAtTheMiddleOfATallRoomRoutesToItsFloor()
    {
        PluginNavigationPlan plan = await Preview(new Goals { Floors = DungeonWithTallRooms() }, TallRoom, TallMiddle);

        Assert.Equal(PluginNavigationPlanStatus.Routed, plan.Status);
        Assert.InRange(plan.Path[^1].Elevation * 240d, Floor - 0.1d, Floor + 0.1d);
    }

    [Fact]
    public async Task APlaceInAFloorlessUpperCellRoutesToTheFloorOfTheCellBelow()
    {
        PhysicsEngine world = DungeonWithTallRooms();

        Vector3 placed = RuntimeNavigationGoalSource.OnTheCellFloor(world, StackedUpper, UpperMiddle);
        PluginNavigationPlan plan = await Preview(new Goals { Floors = world }, StackedUpper, UpperMiddle);

        Assert.Equal(new Vector3(50f, 10f, Floor), placed);
        Assert.Equal(PluginNavigationPlanStatus.Routed, plan.Status);
        Assert.Equal(StackedLower, plan.Path[^1].CellId);
    }

    [Fact]
    public async Task APlaceInALowRoomIsUnchanged()
    {
        PhysicsEngine world = DungeonWithTallRooms();
        var onTheFloor = new Vector3(15f, 12f, Floor);
        var middle = new Vector3(15f, 12f, -28.5f);

        PluginNavigationPlan plan = await Preview(new Goals { Floors = world }, LowRoom, middle);

        Assert.Equal(onTheFloor, RuntimeNavigationGoalSource.OnTheCellFloor(world, LowRoom, onTheFloor));
        Assert.Equal(onTheFloor, RuntimeNavigationGoalSource.OnTheCellFloor(world, LowRoom, middle));
        Assert.Equal(PluginNavigationPlanStatus.Routed, plan.Status);
    }

    [Fact]
    public void APlaceGivenALittleUnderItsFloorStaysOnThatFloor()
    {
        PhysicsEngine world = DungeonWithTallRooms();

        Vector3 placed = RuntimeNavigationGoalSource.OnTheCellFloor(world, TallRoom, new Vector3(30f, 10f, Floor - 0.2f));

        Assert.Equal(Floor, placed.Z);
    }

    [Theory]
    [InlineData(0xA9B40001u)]
    [InlineData(0xA9B4FFFFu)]
    [InlineData(0u)]
    public void AnOutdoorPlaceIsUnchanged(uint cellId)
    {
        PhysicsEngine world = DungeonWithTallRooms();
        var place = new Vector3(30f, 10f, -27.5f);

        Assert.Equal(place, RuntimeNavigationGoalSource.OnTheCellFloor(world, cellId, place));
    }

    [Fact]
    public void APlaceWithNoFloorUnderItIsUnchanged()
    {
        PhysicsEngine world = DungeonWithTallRooms();
        var outside = new Vector3(100f, 100f, -27.5f);
        var belowEveryFloor = new Vector3(30f, 10f, -40f);

        Assert.Equal(outside, RuntimeNavigationGoalSource.OnTheCellFloor(world, TallRoom, outside));
        Assert.Equal(belowEveryFloor, RuntimeNavigationGoalSource.OnTheCellFloor(world, TallRoom, belowEveryFloor));
    }

    /// <summary>The walk to a place stands its goal where the preview does.</summary>
    [Fact]
    public void AWalkToAPlaceAtTheMiddleOfATallRoomArrives()
    {
        PhysicsEngine world = DungeonWithTallRooms();
        var body = new SimulatedBody(new Vector3(10f, 10f, Floor)) { CellId = LowRoom };
        var walk = new NavigationWalkController(world, body, new Goals { Floors = world }, isSealedDungeon: _ => true);

        walk.WalkToPlace(TallRoom, TallMiddle, 2.5f);
        NavigationWalkReport report = RunUntilSettled(walk, body);

        Assert.Equal(NavigationWalkState.Arrived, report.State);
        Assert.InRange(Vector2.Distance(new Vector2(body.Position.X, body.Position.Y), new Vector2(30f, 10f)), 0f, 2.6f);
    }

    private static Task<PluginNavigationPlan> Preview(Goals goals, uint cellId, Vector3 local)
    {
        var body = new SimulatedBody(new Vector3(10f, 10f, Floor)) { CellId = LowRoom };
        var walk = new NavigationWalkController(goals.Floors ?? DungeonWithTallRooms(), body, goals, isSealedDungeon: _ => true);
        return walk.PreviewPathAsync(cellId, local, 2.5f);
    }

    /// <summary>
    /// Three rooms in a row 30 m down: a low room 3 m high, a room 5 m high, and a room 10 m
    /// high authored as a cell on top of a cell, the upper one with walls and a ceiling but
    /// no floor.
    /// </summary>
    internal static PhysicsEngine DungeonWithTallRooms()
    {
        static CellSurface Cell(uint cellId, float x0, float x1, float? floor, float low, float top, bool roofed = true)
        {
            var vertices = new Dictionary<ushort, Vector3>();
            var polygons = new List<List<short>>();
            void Add(params Vector3[] corners)
            {
                var ids = new List<short>();
                foreach (Vector3 corner in corners)
                {
                    ids.Add((short)vertices.Count);
                    vertices[(ushort)vertices.Count] = corner;
                }
                polygons.Add(ids);
            }
            if (floor is { } z)
                Add(new(x0, 0f, z), new(x1, 0f, z), new(x1, 20f, z), new(x0, 20f, z));
            if (roofed)
                Add(new(x0, 0f, top), new(x0, 20f, top), new(x1, 20f, top), new(x1, 0f, top));
            Add(new(x0, 0f, low), new(x1, 0f, low), new(x1, 0f, top), new(x0, 0f, top));
            Add(new(x0, 20f, low), new(x1, 20f, low), new(x1, 20f, top), new(x0, 20f, top));
            return new CellSurface(cellId, vertices, polygons);
        }

        var physics = new PhysicsEngine();
        physics.AddLandblock(
            0xA9B4FFFFu,
            new TerrainSurface(new byte[81], new float[256]),
            [
                Cell(LowRoom, 0f, 20f, Floor, Floor, -27f),
                Cell(TallRoom, 20f, 40f, Floor, Floor, -25f),
                Cell(StackedLower, 40f, 60f, Floor, Floor, -25f, roofed: false),
                Cell(StackedUpper, 40f, 60f, null, -25f, -20f),
            ],
            [],
            0f,
            0f);
        return physics;
    }
}
