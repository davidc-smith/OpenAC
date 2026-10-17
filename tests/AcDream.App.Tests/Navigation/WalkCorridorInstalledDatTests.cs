using System.Numerics;
using AcDream.App.Tests.Rendering;
using AcDream.Core.Navigation;
using Xunit.Abstractions;

namespace AcDream.App.Tests.Navigation;

/// <summary>
/// The two walks of Aethrix api-gaps #72, each a climb onto a plateau 290 m away whose one
/// planning square holds no way up. At Stonehold a walked way round of about 740 m runs
/// outside that square; at the other plateau no walked way up exists anywhere near, only a
/// chain of jumps, so the walk is right to find no route and says where it looked.
/// </summary>
[Trait("Lane", "InstalledDat")]
public sealed class WalkCorridorInstalledDatTests
{
    private readonly ITestOutputHelper _output;

    public WalkCorridorInstalledDatTests(ITestOutputHelper output) => _output = output;

    private static uint[] Blocks(int x0, int x1, int y0, int y1)
    {
        var blocks = new List<uint>();
        for (int x = x0; x <= x1; x++)
            for (int y = y0; y <= y1; y++)
                blocks.Add(((uint)x << 24) | ((uint)y << 16) | 0xFFFFu);
        return [.. blocks];
    }

    private static PublishedLandblock Load(uint[] blocks)
    {
        string? datDirectory = InstalledDatTestPath.Resolve();
        Assert.True(datDirectory is not null, "Lane=InstalledDat requires an installed retail DAT directory; see docs/release-gate.md.");
        return PublishedLandblock.Load(datDirectory!, blocks, []);
    }

    [Fact]
    public void StoneholdIsReachedAlongAWayRoundOutsideItsSquare()
    {
        // Base 0x5BD4. Start 0x5ED7000A [31.0 30.9 79.7]; goal 69.60N 25.56W.
        PublishedLandblock world = Load(Blocks(0x5B, 0x61, 0xD4, 0xDA));
        var from = new Vector3((3 * 192) + 31.0f, (3 * 192) + 30.9f, 79.7f);
        var flatGoal = new Vector2((4 * 192) + 93.6f, (2 * 192) + 84.0f);
        var goal = new Vector3(flatGoal, world.Engine.SampleTerrainZ(flatGoal.X, flatGoal.Y) ?? float.NaN);
        NavGeometry geometry = Assert.IsType<NavGeometry>(NavGeometry.Capture(world.Engine, 0f, 0f, 1344f));

        NavCorridor corridor = NavCorridor.Plan(NavGrid.Build(geometry, NavCorridor.CoarseBody(world.Body, 1f), 1f), from, goal, 2f, arriveOnGoalFloor: true);
        _output.WriteLine($"{corridor.Outcome}: {corridor.Length:0} m in {corridor.Milliseconds:0} ms");
        Assert.Equal(NavCorridorOutcome.Reaches, corridor.Outcome);

        NavRoute route = NavCorridor.Stitch(geometry, world.Body, corridor, from, goal,
            (grid, start, to, final) => final
                ? NavRouter.Find(grid, start, to, 2f, arriveOnGoalFloor: true)
                : NavRouter.Find(grid, start, to, NavCorridor.StageArrival),
            corridor.StageBudget);
        _output.WriteLine($"{route.Outcome}: {route.Reason}; {route.Length:0} m in {route.Milliseconds:0} ms");

        Assert.Equal(NavRouteOutcome.Routed, route.Outcome);
        Assert.InRange(Vector2.Distance(new(route.Legs[^1].X, route.Legs[^1].Y), flatGoal), 0f, 2.5f);
        Assert.InRange(route.Legs[^1].Z, goal.Z - 1.2f, goal.Z + 1.2f);
    }

    [Fact]
    public void ThePlateauHasNoWalkedWayUpAndTheWalkSaysWhereItLooked()
    {
        // Base 0x45B4. Start 0x48B8001F [92.1 151.9 105.2]; goal 45.04N 43.07W.
        PublishedLandblock world = Load(Blocks(0x45, 0x4B, 0xB4, 0xBA));
        var from = new Vector3((3 * 192) + 92.1f, (4 * 192) + 151.9f, 105.2f);
        var flatGoal = new Vector2((4 * 192) + 115.2f, (3 * 192) + 141.6f);
        var goal = new Vector3(flatGoal, world.Engine.SampleTerrainZ(flatGoal.X, flatGoal.Y) ?? float.NaN);
        NavGeometry geometry = Assert.IsType<NavGeometry>(NavGeometry.Capture(world.Engine, 0f, 0f, 1344f));

        NavCorridor corridor = NavCorridor.Plan(NavGrid.Build(geometry, NavCorridor.CoarseBody(world.Body, 1f), 1f), from, goal, 2f, arriveOnGoalFloor: true);

        Assert.Equal(NavCorridorOutcome.NoPath, corridor.Outcome);
        Assert.EndsWith("within 1344 m around the start and goal", corridor.Reason);
    }
}
