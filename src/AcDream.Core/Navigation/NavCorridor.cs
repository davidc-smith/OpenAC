using System.Diagnostics;
using System.Globalization;
using System.Numerics;

namespace AcDream.Core.Navigation;

/// <summary>What the search for a way round came to.</summary>
public enum NavCorridorOutcome
{
    /// <summary>The way round reaches the goal, on the floor the goal stands on for a place.</summary>
    Reaches,

    /// <summary>The goal lies beyond the coarse grid; the way round ends at the grid's edge nearest it.</summary>
    TowardEdge,

    /// <summary>No walked way reaches the goal, or the edge toward it, inside the coarse grid.</summary>
    NoPath,

    /// <summary>The start stands nowhere on the coarse grid.</summary>
    NoStart,
}

/// <summary>
/// Searches one stage's fine grid from a start to a point: the stage's point along the way
/// round, or, for the <paramref name="final"/> stage, the goal with the walk's own arrival.
/// </summary>
public delegate NavRoute NavStageSearch(NavGrid grid, Vector3 from, Vector3 to, bool final);

/// <summary>
/// A way round planned over a coarse grid of everything loaded, walking only, that a
/// walk too long or too twisty for one planning square follows in fine stages. The
/// coarse grid answers which way round; each stage's own fine grid answers where the
/// feet go. <see cref="Points"/> are the coarse grid's standing points, start to end.
/// </summary>
public sealed class NavCorridor
{
    /// <summary>A stage aims this far along the way round from the point nearest the character.</summary>
    public const float StageLookahead = 120f;

    /// <summary>A stage's fine route ends within this of the point it aims at, which may stand on a spot the fine grid finds too tight.</summary>
    public const float StageArrival = 4f;

    /// <summary>A stage whose route ends farther than this from the point it aimed at missed it.</summary>
    public const float StageMiss = 8f;

    /// <summary>Once no more than this of the way round is left, the last stage plans to the goal itself.</summary>
    public const float FinalStretch = 144f;

    /// <summary>Room kept around a stage's points inside its square, and the least and most a stage square may be.</summary>
    public const float StageMargin = 24f;
    public const float MinimumStageSquare = 96f;
    public const float MaximumStageSquare = 320f;

    /// <summary>A way round to a goal beyond the coarse grid ends within this of the edge nearest the goal.</summary>
    public const float EdgeBand = 8f;

    /// <summary>
    /// How far along the way round, from the last point reached, the nearest point to the
    /// character is looked for, so a way that goes out and comes back alongside itself is
    /// never followed from its return leg.
    /// </summary>
    public const float NearestWindow = 200f;

    private readonly float[] _along;

    public NavCorridor(
        NavCorridorOutcome outcome,
        string reason,
        IReadOnlyList<Vector3> points,
        float originX,
        float originY,
        float size,
        float cellSize,
        double milliseconds)
    {
        ArgumentNullException.ThrowIfNull(reason);
        ArgumentNullException.ThrowIfNull(points);
        Outcome = outcome;
        Reason = reason;
        Points = points;
        OriginX = originX;
        OriginY = originY;
        Size = size;
        CellSize = cellSize;
        Milliseconds = milliseconds;
        _along = new float[points.Count];
        for (int index = 1; index < points.Count; index++)
            _along[index] = _along[index - 1] + Flat(points[index] - points[index - 1]).Length();
    }

    public NavCorridorOutcome Outcome { get; }

    public string Reason { get; }

    public IReadOnlyList<Vector3> Points { get; }

    /// <summary>The coarse grid's square and cell size, for what a walk says it searched.</summary>
    public float OriginX { get; }

    public float OriginY { get; }

    public float Size { get; }

    public float CellSize { get; }

    public double Milliseconds { get; }

    /// <summary>The way round's horizontal length in meters.</summary>
    public float Length => _along.Length == 0 ? 0f : _along[^1];

    /// <summary>How many stages a walk along this way round may take before it gives up.</summary>
    public int StageBudget => (int)MathF.Ceiling(Length / (StageLookahead * 0.75f)) + 4;

    /// <summary>How far along the way round a point lies.</summary>
    public float AlongAt(int index) => _along.Length == 0 ? 0f : _along[Math.Clamp(index, 0, _along.Length - 1)];

    /// <summary>How much of the way round is left from a point on it.</summary>
    public float RemainingFrom(int index) => _along.Length == 0 ? 0f : Length - AlongAt(index);

    /// <summary>
    /// The point of the way round nearest a position, at or after <paramref name="fromIndex"/>
    /// and no more than <see cref="NearestWindow"/> along from it.
    /// </summary>
    public int NearestIndex(Vector3 position, int fromIndex)
    {
        if (Points.Count == 0)
            return 0;
        int first = Math.Clamp(fromIndex, 0, Points.Count - 1);
        float limit = _along[first] + NearestWindow;
        Vector2 at = Flat(position);
        int best = first;
        float bestDistance = float.PositiveInfinity;
        for (int index = first; index < Points.Count && _along[index] <= limit; index++)
        {
            float distance = Vector2.DistanceSquared(Flat(Points[index]), at);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = index;
            }
        }
        return best;
    }

    /// <summary>The first point at least <paramref name="meters"/> along the way round from a point on it, or its end.</summary>
    public int IndexAhead(int fromIndex, float meters)
    {
        if (Points.Count == 0)
            return 0;
        int index = Math.Clamp(fromIndex, 0, Points.Count - 1);
        float wanted = _along[index] + meters;
        while (index < Points.Count - 1 && _along[index] < wanted)
            index++;
        return index;
    }

    /// <summary>The points of the way round from one index to another, both included.</summary>
    public IEnumerable<Vector3> Span(int fromIndex, int toIndex)
    {
        int last = Math.Min(toIndex, Points.Count - 1);
        for (int index = Math.Max(0, fromIndex); index <= last; index++)
            yield return Points[index];
    }

    /// <summary>
    /// The body a coarse grid is built for: the walker's own, but stepping up and down as far
    /// as a floor as steep as the game lets anyone walk rises over one coarse cell. A grid
    /// joins neighbouring cells only where the rise between them is a step, so at a metre a
    /// cell the walker's own step would turn back every slope steeper than about 30 degrees,
    /// where the game walks up to nearly 50. The coarse grid must err open: a way round it
    /// shows that the fine stages cannot walk is found out and planned again, but one it
    /// wrongly shuts is never tried.
    /// </summary>
    public static NavBody CoarseBody(NavBody body, float cellSize) => body with
    {
        StepUpHeight = MathF.Max(body.StepUpHeight, cellSize * CoarseRisePerMeter),
        StepDownHeight = MathF.Max(body.StepDownHeight, cellSize * CoarseRisePerMeter),
    };

    /// <summary>How far a floor as steep as the game lets anyone walk rises over a metre, a little over 48 degrees.</summary>
    public const float CoarseRisePerMeter = 1.12f;

    /// <summary>
    /// Searches a coarse grid for a walked way from <paramref name="from"/> to the goal: onto
    /// the floor the goal stands on for a place, or near it for an object. A goal outside the
    /// grid gets a way to the grid's edge nearest it. Nothing is leapt and no creatures are
    /// passed: those are the fine stages' business.
    /// </summary>
    public static NavCorridor Plan(
        NavGrid coarse,
        Vector3 from,
        Vector3 goal,
        float arrival,
        bool arriveOnGoalFloor,
        IReadOnlyList<NavAvoidance>? avoid = null)
    {
        ArgumentNullException.ThrowIfNull(coarse);
        var clock = Stopwatch.StartNew();
        string area = string.Create(CultureInfo.InvariantCulture, $"within {coarse.Size:0} m around the start and goal");
        bool beyond = !coarse.Contains(goal, coarse.CellSize * 2f);
        NavRoute route = beyond
            ? NavRouter.FindToward(coarse, from, goal, EdgeBand, avoid)
            : NavRouter.Find(coarse, from, goal, MathF.Max(arrival, coarse.CellSize * 2f), avoid, leaps: null, crowd: null, arriveOnGoalFloor);
        NavCorridorOutcome outcome = route.Outcome switch
        {
            NavRouteOutcome.Routed when beyond && route.Reason.StartsWith(NavRouter.ReachedEdgeReason, StringComparison.Ordinal)
                => NavCorridorOutcome.TowardEdge,
            NavRouteOutcome.Routed when !beyond && (route.Reason == "routed" || !arriveOnGoalFloor)
                => NavCorridorOutcome.Reaches,
            NavRouteOutcome.NoStart => NavCorridorOutcome.NoStart,
            _ => NavCorridorOutcome.NoPath,
        };
        string reason = outcome switch
        {
            NavCorridorOutcome.Reaches or NavCorridorOutcome.TowardEdge => route.Reason,
            NavCorridorOutcome.NoStart => route.Reason,
            _ when beyond => $"no way on toward the goal was found {area}",
            _ => $"{route.Reason} {area}",
        };
        IReadOnlyList<Vector3> points = outcome is NavCorridorOutcome.Reaches or NavCorridorOutcome.TowardEdge ? route.Path : [];
        return new NavCorridor(outcome, reason, points, coarse.OriginX, coarse.OriginY, coarse.Size, coarse.CellSize, clock.Elapsed.TotalMilliseconds);
    }

    /// <summary>
    /// A square holding every point with <paramref name="margin"/> to spare, at least
    /// <paramref name="minimum"/> a side, rounded up to 16 m and snapped to the fine grid's
    /// cells; false when it would be larger than <paramref name="largest"/>.
    /// </summary>
    public static bool TrySquareOver(
        IEnumerable<Vector3> points,
        float margin,
        float minimum,
        float largest,
        out float originX,
        out float originY,
        out float size)
    {
        var low = new Vector2(float.PositiveInfinity);
        var high = new Vector2(float.NegativeInfinity);
        foreach (Vector3 point in points)
        {
            low = Vector2.Min(low, Flat(point));
            high = Vector2.Max(high, Flat(point));
        }
        originX = 0f;
        originY = 0f;
        size = 0f;
        if (!(low.X <= high.X))
            return false;
        float span = MathF.Max(high.X - low.X, high.Y - low.Y) + (2f * margin);
        size = MathF.Ceiling(MathF.Max(minimum, span) / 16f) * 16f;
        if (size > largest)
            return false;
        originX = Snap(((low.X + high.X) * 0.5f) - (size * 0.5f));
        originY = Snap(((low.Y + high.Y) * 0.5f) - (size * 0.5f));
        return true;
    }

    /// <summary>
    /// Plans the fine stages along a way round that reaches its goal, each over its own
    /// square cropped from <paramref name="geometry"/>, and joins their legs into one route.
    /// A stage that ends farther than <see cref="StageMiss"/> from its point, or near it but out
    /// of sight of it, such as across a wall, misses it, as it does on a walk. The
    /// joined route carries no leaps: it is for previews, which only show the way.
    /// </summary>
    public static NavRoute Stitch(
        NavGeometry geometry,
        NavBody body,
        NavCorridor corridor,
        Vector3 from,
        Vector3 goal,
        NavStageSearch search,
        int maximumStages)
    {
        ArgumentNullException.ThrowIfNull(geometry);
        ArgumentNullException.ThrowIfNull(corridor);
        ArgumentNullException.ThrowIfNull(search);
        var clock = Stopwatch.StartNew();
        var legs = new List<Vector3> { from };
        float length = 0f;
        int expansions = 0;
        Vector3 at = from;
        int index = corridor.NearestIndex(from, 0);
        for (int stage = 0; stage < maximumStages; stage++)
        {
            bool final = corridor.Outcome == NavCorridorOutcome.Reaches && corridor.RemainingFrom(index) <= FinalStretch;
            int targetIndex = final ? corridor.Points.Count - 1 : corridor.IndexAhead(index, StageLookahead);
            Vector3 target = final ? goal : corridor.Points[targetIndex];
            if (!TrySquareOver([at, target, .. corridor.Span(index, targetIndex)], StageMargin, MinimumStageSquare, MaximumStageSquare,
                    out float originX, out float originY, out float size))
            {
                return Failed($"the way round bends too far for one stage near {Format(target)}");
            }
            NavRoute route = search(NavGrid.Build(geometry.Crop(originX, originY, size), body), at, target, final);
            expansions += route.Expansions;
            if (route.Outcome != NavRouteOutcome.Routed)
                return Failed(final ? route.Reason : $"no way along the way round reaches {Format(target)}");
            if (!final && (Flat(route.Legs[^1] - target).Length() > StageMiss || !route.EndsInSight))
                return Failed($"no way along the way round reaches {Format(target)}");
            for (int leg = 1; leg < route.Legs.Count; leg++)
                legs.Add(route.Legs[leg]);
            length += route.Length;
            at = route.Legs[^1];
            if (final)
            {
                return new NavRoute(NavRouteOutcome.Routed, route.Reason, [], legs, length, expansions, clock.Elapsed.TotalMilliseconds)
                {
                    EndsInSight = route.EndsInSight,
                };
            }
            index = corridor.NearestIndex(at, index);
        }
        return Failed(string.Create(CultureInfo.InvariantCulture,
            $"the goal was still {Flat(goal - at).Length():0} m away after {maximumStages} stages"));

        NavRoute Failed(string reason) =>
            new(NavRouteOutcome.NoPath, reason, [], [], 0f, expansions, clock.Elapsed.TotalMilliseconds);
    }

    private static string Format(Vector3 point) =>
        string.Create(CultureInfo.InvariantCulture, $"({point.X:0}, {point.Y:0})");

    private static float Snap(float coordinate) =>
        MathF.Floor(coordinate / NavGrid.DefaultCellSize) * NavGrid.DefaultCellSize;

    private static Vector2 Flat(Vector3 point) => new(point.X, point.Y);
}
