using System.Numerics;
using AcDream.Core.Navigation;

namespace AcDream.Runtime.Navigation;

/// <summary>
/// Ways round. A walk whose goal lies beyond one planning square, or whose one square
/// holds no route to it, first plans a walked way round over a coarse grid of everything
/// loaded, then walks it in stages, each planned over its own fine square toward a point
/// further along the way.
/// </summary>
internal sealed partial class NavigationWalkController
{
    /// <summary>The most a way round is planned over, and the largest square planned over at the finer of its two cell sizes.</summary>
    internal const float MaximumCorridorRegion = 1536f;
    internal const float FineCorridorRegion = 768f;

    /// <summary>How many ways round a walk plans, each keeping out of where the last one led nowhere, before it gives up.</summary>
    internal const int MaximumCorridorPlans = 3;

    /// <summary>How far around the stretch of a way round that led nowhere the next one keeps out.</summary>
    internal const float CorridorKeepOutRadius = 12f;

    private Task<NavCorridor>? _corridoring;
    private Request? _corridorFor;

    /// <summary>The way round the latest walk follows, or planned last, for drawing.</summary>
    public NavCorridor? Corridor { get; private set; }

    /// <summary>The cell size a way round over a square this large is planned at.</summary>
    internal static float CorridorCellSize(float size) => size <= FineCorridorRegion ? 0.5f : 1f;

    /// <summary>
    /// The square a way round is planned over: as much of what is loaded as fits in
    /// <see cref="MaximumCorridorRegion"/>, centred between the character and the goal where
    /// that stays inside it, and always holding the character with room around it.
    /// </summary>
    internal static bool TryChooseCorridorRegion(
        Vector3 from,
        Vector3 goal,
        Vector2 residentMinimum,
        Vector2 residentMaximum,
        out float originX,
        out float originY,
        out float size)
    {
        Vector2 extent = residentMaximum - residentMinimum;
        size = MathF.Min(MaximumCorridorRegion, MathF.Ceiling(MathF.Max(extent.X, extent.Y) / 16f) * 16f);
        originX = 0f;
        originY = 0f;
        if (!(size >= MinimumRegion))
            return false;
        Vector2 centre = (Flat(from) + Flat(goal)) * 0.5f;
        originX = Snap(Place(centre.X - (size * 0.5f), residentMinimum.X, residentMaximum.X, from.X, size));
        originY = Snap(Place(centre.Y - (size * 0.5f), residentMinimum.Y, residentMaximum.Y, from.Y, size));
        return true;

        static float Place(float origin, float low, float high, float keep, float size)
        {
            origin = high - low >= size ? Math.Clamp(origin, low, high - size) : low;
            return Math.Clamp(origin, keep - size + RegionMargin, keep - RegionMargin);
        }
    }

    /// <summary>
    /// Captures what is loaded around the character on this thread and plans a way round
    /// over it on a worker. The walk stays planning meanwhile.
    /// </summary>
    private void StartCorridor(Request active, in NavigationWalkBodySample sample)
    {
        if (!NavGeometry.TryMeasureResident(_physics, out Vector2 low, out Vector2 high)
            || !TryChooseCorridorRegion(sample.Position, active.Goal, low, high, out float originX, out float originY, out float size))
        {
            End(active, NavigationWalkState.NoRoute, "no collision is loaded around the character");
            return;
        }
        NavGeometry? geometry = NavGeometry.Capture(_physics, originX, originY, size, _goals.StandsStill);
        if (geometry is null)
        {
            End(active, NavigationWalkState.NoRoute, "no collision is loaded around the character");
            return;
        }
        active.CorridorPlans++;
        NavBody body = sample.Body;
        Vector3 from = sample.Position;
        Vector3 goal = active.Goal;
        float arrival = PlanningRadius(active.ArrivalMeters);
        bool onGoalFloor = active.Place is not null;
        NavAvoidance[] keepOut = [.. active.CorridorKeepOut];
        float cell = CorridorCellSize(size);
        _corridorFor = active;
        _corridoring = Task.Run(() => NavCorridor.Plan(NavGrid.Build(geometry, body, cell), from, goal, arrival, onGoalFloor, keepOut));
        string planning = Inv($"planning a way round over {size:0} m around the start and goal");
        Publish(active, NavigationWalkState.Planning, planning, float.NaN);
        Say($"{(active.Follow ? "Follow" : "Walk to")} {Label(active)}: {planning}");
    }

    /// <summary>Takes up a way round planned off the update thread.</summary>
    private void CollectCorridor()
    {
        if (_corridoring is not { IsCompleted: true } planning)
            return;
        _corridoring = null;
        Request? requester = _corridorFor;
        _corridorFor = null;
        if (requester is null || !ReferenceEquals(requester, _active))
            return;
        if (!planning.IsCompletedSuccessfully)
        {
            End(requester, NavigationWalkState.NoRoute,
                $"the way round could not be planned: {planning.Exception?.GetBaseException().Message ?? "it was cancelled"}");
            return;
        }
        NavCorridor corridor = planning.Result;
        Corridor = corridor;
        Say(Inv(
            $"Way round: {corridor.Outcome} over {corridor.Size:0} m at {corridor.CellSize:0.##} m cells, {corridor.Length:0} m, in {corridor.Milliseconds:0} ms"));
        if (corridor.Outcome is not (NavCorridorOutcome.Reaches or NavCorridorOutcome.TowardEdge))
        {
            End(requester, NavigationWalkState.NoRoute, corridor.Reason);
            return;
        }
        // Task 4 replaces this line with walking the way round.
        End(requester, NavigationWalkState.NoRoute, "following a way round is not supported yet");
    }
}
