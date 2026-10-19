using System.Numerics;
using AcDream.Core.Navigation;

namespace AcDream.Core.Tests.Navigation;

public sealed class NavCorridorTests
{
    private static readonly NavBody Body = new(0.48f, 1.835f, 0.4f, 0.4f);
    private const float Size = 400f;

    /// <summary>
    /// Low ground south of y = 200 and a plateau 6 m up north of it, the cliff between them a
    /// wall, and, with <paramref name="ramp"/>, a ramp up the cliff only at the far east end.
    /// </summary>
    private static NavGeometry Plateau(bool ramp)
    {
        var triangles = new List<NavTriangle>();
        triangles.AddRange(Floor(0f, 0f, Size, 190f, 0f));
        triangles.AddRange(Floor(0f, 200f, Size, Size, 6f));
        triangles.AddRange(Wall(0f, 200f, ramp ? 370f : Size, 200f, 0f, 6f));
        if (ramp)
        {
            triangles.AddRange(Floor(0f, 190f, 370f, 200f, 0f));
            triangles.AddRange(Ramp(370f, 160f, Size, 200f, 0f, 6f));
            triangles.AddRange(Wall(370f, 160f, 370f, 200f, 0f, 6f));
        }
        else
        {
            triangles.AddRange(Floor(0f, 190f, Size, 200f, 0f));
        }
        return new NavGeometry(0f, 0f, Size, [], triangles, [], []);
    }

    [Fact]
    public void AWayRoundUpARampOutsideTheStraightLineReachesThePlateau()
    {
        NavGrid coarse = NavGrid.Build(Plateau(ramp: true), Body, 1f);

        NavCorridor corridor = NavCorridor.Plan(coarse, new Vector3(60f, 150f, 0f), new Vector3(60f, 260f, 6f), 2f, arriveOnGoalFloor: true);

        Assert.Equal(NavCorridorOutcome.Reaches, corridor.Outcome);
        Assert.Contains(corridor.Points, point => point.X > 360f);
        Assert.InRange(corridor.Points[^1].Z, 5.5f, 6.5f);
        Assert.True(corridor.Length > 500f, $"length {corridor.Length}");
    }

    [Fact]
    public void APlateauWithNoWayUpHasNoCorridorAndSaysWhereItLooked()
    {
        NavGrid coarse = NavGrid.Build(Plateau(ramp: false), Body, 1f);

        NavCorridor corridor = NavCorridor.Plan(coarse, new Vector3(60f, 150f, 0f), new Vector3(60f, 260f, 6f), 2f, arriveOnGoalFloor: true);

        Assert.Equal(NavCorridorOutcome.NoPath, corridor.Outcome);
        Assert.Empty(corridor.Points);
        Assert.EndsWith("within 400 m around the start and goal", corridor.Reason);
    }

    /// <summary>
    /// A ramp at 38 degrees, rising 0.78 m a metre, climbs the cliff at the far east end: walkable
    /// in the game, though each metre of it rises more than the body's own step.
    /// </summary>
    private static NavGeometry SteepRampPlateau()
    {
        const float rise = 0.78f;
        const float foot = 200f - (6f / rise);
        var triangles = new List<NavTriangle>();
        triangles.AddRange(Floor(0f, 0f, Size, 190f, 0f));
        triangles.AddRange(Floor(0f, 200f, Size, Size, 6f));
        triangles.AddRange(Floor(0f, 190f, 370f, 200f, 0f));
        triangles.AddRange(Floor(370f, 190f, Size, foot, 0f));
        triangles.AddRange(Wall(0f, 200f, 370f, 200f, 0f, 6f));
        triangles.AddRange(Ramp(370f, foot, Size, 200f, 0f, 6f));
        triangles.AddRange(Wall(370f, foot, 370f, 200f, 0f, 6f));
        return new NavGeometry(0f, 0f, Size, [], triangles, [], []);
    }

    [Fact]
    public void AWayRoundUpARampSteeperThanAStepPerCellStillReachesThePlateau()
    {
        NavGrid coarse = NavGrid.Build(SteepRampPlateau(), NavCorridor.CoarseBody(Body, 1f), 1f);

        NavCorridor corridor = NavCorridor.Plan(coarse, new Vector3(60f, 150f, 0f), new Vector3(60f, 260f, 6f), 2f, arriveOnGoalFloor: true);

        Assert.Equal(NavCorridorOutcome.Reaches, corridor.Outcome);
        Assert.Contains(corridor.Points, point => point.X > 370f);
        Assert.InRange(corridor.Points[^1].Z, 5.5f, 6.5f);
    }

    [Fact]
    public void TheCoarseBodyStepsAsFarAsTheSteepestFloorRisesOverACell()
    {
        NavBody coarse = NavCorridor.CoarseBody(Body, 1f);

        Assert.Equal(1.12f, coarse.StepUpHeight, 3);
        Assert.Equal(1.12f, coarse.StepDownHeight, 3);
        Assert.Equal(Body.Radius, coarse.Radius);
        Assert.Equal(Body.Height, coarse.Height);
        Assert.Equal(Body, NavCorridor.CoarseBody(Body, 0.25f));
    }

    [Fact]
    public void AGoalBeyondTheCoarseGridGetsACorridorToItsEdge()
    {
        NavGrid coarse = NavGrid.Build(new NavGeometry(0f, 0f, 200f, [], Floor(0f, 0f, 200f, 200f, 0f), [], []), Body, 1f);

        NavCorridor corridor = NavCorridor.Plan(coarse, new Vector3(50f, 50f, 0f), new Vector3(50f, 900f, 0f), 2f, arriveOnGoalFloor: true);

        Assert.Equal(NavCorridorOutcome.TowardEdge, corridor.Outcome);
        Assert.InRange(corridor.Points[^1].Y, 200f - NavCorridor.EdgeBand - 2f, 200f);
    }

    [Fact]
    public void TheNearestPointNeverStepsBackOrAcrossToTheWayBack()
    {
        // Out east along y = 0, then back west along y = 10.
        var points = new List<Vector3>();
        for (int x = 0; x <= 300; x++) points.Add(new Vector3(x, 0f, 0f));
        for (int y = 1; y <= 10; y++) points.Add(new Vector3(300f, y, 0f));
        for (int x = 299; x >= 0; x--) points.Add(new Vector3(x, 10f, 0f));
        var corridor = new NavCorridor(NavCorridorOutcome.Reaches, "routed", points, 0f, 0f, 400f, 1f, 0d);

        int nearest = corridor.NearestIndex(new Vector3(50f, 6f, 0f), fromIndex: 40);

        Assert.InRange(nearest, 45, 55);
        Assert.Equal(40, corridor.NearestIndex(new Vector3(0f, 0f, 0f), fromIndex: 40));
        Assert.Equal(160, corridor.IndexAhead(40, 120f));
        Assert.Equal(points.Count - 1, corridor.IndexAhead(500, 5000f));
        Assert.Equal(corridor.Length - corridor.AlongAt(40), corridor.RemainingFrom(40), 3);
    }

    [Fact]
    public void ACorridorWithNoPointsMeasuresZeroWithoutThrowing()
    {
        var corridor = new NavCorridor(NavCorridorOutcome.NoPath, "none", [], 0f, 0f, 400f, 1f, 0d);

        Assert.Equal(0f, corridor.AlongAt(3));
        Assert.Equal(0f, corridor.RemainingFrom(3));
        Assert.Equal(0, corridor.NearestIndex(new Vector3(1f, 1f, 0f), fromIndex: 5));
        Assert.Equal(0, corridor.IndexAhead(5, 100f));
    }

    [Fact]
    public void ASquareOverPointsHoldsThemWithRoomAndSnaps()
    {
        Vector3[] points = [new(10.1f, 20.3f, 0f), new(130.7f, 60.2f, 0f)];

        Assert.True(NavCorridor.TrySquareOver(points, 24f, 96f, 320f, out float x, out float y, out float size));
        Assert.Equal(176f, size);
        Assert.Equal(0f, x % 0.25f, 4);
        Assert.Equal(0f, y % 0.25f, 4);
        Assert.All(points, p =>
        {
            Assert.InRange(p.X, x + 24f - 0.25f, x + size - 24f + 0.25f);
            Assert.InRange(p.Y, y + 24f - 0.25f, y + size - 24f + 0.25f);
        });
        Assert.False(NavCorridor.TrySquareOver([new(0f, 0f, 0f), new(400f, 0f, 0f)], 24f, 96f, 320f, out _, out _, out _));
    }

    [Fact]
    public void ACroppedGeometryPlansTheSameInsideIt()
    {
        NavGeometry whole = Plateau(ramp: true);
        NavGeometry cropped = whole.Crop(0f, 0f, 160f);

        Assert.Equal(160f, cropped.Size);
        Assert.True(cropped.CellTriangles.Count < whole.CellTriangles.Count);
        NavRoute route = NavRouter.Find(NavGrid.Build(cropped, Body), new Vector3(20f, 20f, 0f), new Vector3(120f, 120f, 0f), 1f);
        Assert.Equal(NavRouteOutcome.Routed, route.Outcome);
    }

    [Fact]
    public void StagesStitchedAlongTheWayRoundArriveOnThePlateau()
    {
        NavGeometry geometry = Plateau(ramp: true);
        var from = new Vector3(60f, 150f, 0f);
        var goal = new Vector3(60f, 260f, 6f);
        NavCorridor corridor = NavCorridor.Plan(NavGrid.Build(geometry, Body, 1f), from, goal, 2f, arriveOnGoalFloor: true);

        NavRoute route = NavCorridor.Stitch(geometry, Body, corridor, from, goal,
            (grid, start, to, final) => final
                ? NavRouter.Find(grid, start, to, 2f, arriveOnGoalFloor: true)
                : NavRouter.Find(grid, start, to, NavCorridor.StageArrival),
            corridor.StageBudget);

        Assert.Equal(NavRouteOutcome.Routed, route.Outcome);
        Assert.Equal(from.X, route.Legs[0].X, 1);
        Assert.InRange(Vector2.Distance(new(route.Legs[^1].X, route.Legs[^1].Y), new(goal.X, goal.Y)), 0f, 2.5f);
        Assert.InRange(route.Legs[^1].Z, 5.5f, 6.5f);
        Assert.True(route.Length > 500f, $"length {route.Length}");
    }

    [Fact]
    public void AStageThatEndsShortOfItsPointMissesIt()
    {
        NavGeometry geometry = Plateau(ramp: true);
        var from = new Vector3(60f, 150f, 0f);
        var goal = new Vector3(60f, 260f, 6f);
        NavCorridor corridor = NavCorridor.Plan(NavGrid.Build(geometry, Body, 1f), from, goal, 2f, arriveOnGoalFloor: true);

        NavRoute route = NavCorridor.Stitch(geometry, Body, corridor, from, goal,
            (grid, start, to, final) => NavRouter.Find(grid, start, start + new Vector3(1f, 0f, 0f), 0.5f),
            corridor.StageBudget);

        Assert.Equal(NavRouteOutcome.NoPath, route.Outcome);
        Assert.StartsWith("no way along the way round reaches (", route.Reason);
    }

    [Fact]
    public void AStageThatEndsOutOfSightOfItsPointMissesIt()
    {
        NavGeometry geometry = Plateau(ramp: true);
        var from = new Vector3(60f, 150f, 0f);
        var goal = new Vector3(60f, 260f, 6f);
        NavCorridor corridor = NavCorridor.Plan(NavGrid.Build(geometry, NavCorridor.CoarseBody(Body, 1f), 1f), from, goal, 2f, arriveOnGoalFloor: true);

        NavRoute route = NavCorridor.Stitch(geometry, Body, corridor, from, goal,
            (grid, start, to, final) => final
                ? NavRouter.Find(grid, start, to, 2f, arriveOnGoalFloor: true)
                : NavRouter.Find(grid, start, to, NavCorridor.StageArrival) with { EndsInSight = false },
            corridor.StageBudget);

        Assert.Equal(NavRouteOutcome.NoPath, route.Outcome);
        Assert.StartsWith("no way along the way round reaches (", route.Reason);
    }

    private static NavTriangle[] Floor(float x0, float y0, float x1, float y1, float z) =>
        Quad(new Vector3(x0, y0, z), new Vector3(x1, y0, z), new Vector3(x1, y1, z), new Vector3(x0, y1, z));

    private static NavTriangle[] Ramp(float x0, float y0, float x1, float y1, float zAtY0, float zAtY1) =>
        Quad(new Vector3(x0, y0, zAtY0), new Vector3(x1, y0, zAtY0), new Vector3(x1, y1, zAtY1), new Vector3(x0, y1, zAtY1));

    private static NavTriangle[] Wall(float x0, float y0, float x1, float y1, float bottom, float top) =>
        Quad(new Vector3(x0, y0, bottom), new Vector3(x1, y1, bottom), new Vector3(x1, y1, top), new Vector3(x0, y0, top));

    private static NavTriangle[] Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d) => [new(a, b, c), new(a, c, d)];
}
