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
        active.CorridorToward = active.Goal;
        NavBody body = sample.Body;
        Vector3 from = sample.Position;
        Vector3 goal = active.Goal;
        float arrival = PlanningRadius(active.ArrivalMeters);
        bool onGoalFloor = active.Place is not null;
        NavAvoidance[] keepOut = [.. active.CorridorKeepOut];
        float cell = CorridorCellSize(size);
        _corridorFor = active;
        _corridoring = Task.Run(() => NavCorridor.Plan(NavGrid.Build(geometry, NavCorridor.CoarseBody(body, cell), cell), from, goal, arrival, onGoalFloor, keepOut));
        string planning = Inv($"planning a way round over {size:0} m around the start and goal");
        Publish(active, NavigationWalkState.Planning, planning, float.NaN);
        Say($"{Doing(active)} {Label(active)}: {planning}");
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
        NavCorridor corridor = CorridorPlanned?.Invoke(planning.Result) ?? planning.Result;
        Corridor = corridor;
        Say(Inv(
            $"Way round: {corridor.Outcome} over {corridor.Size:0} m at {corridor.CellSize:0.##} m cells, {corridor.Length:0} m, in {corridor.Milliseconds:0} ms"));
        if (corridor.Outcome is not (NavCorridorOutcome.Reaches or NavCorridorOutcome.TowardEdge))
        {
            if (Narration is not null && requester.SquareNoRoute is { } square)
                NarrateNoRoute(requester, square);
            // A way round planned again from the loaded edge, where the coarse grid may find
            // no start, ends as a far walk that can get no nearer; one planned again around
            // where the last led nowhere says where that was and how far it searched.
            NavigationWalkBodySample rolledAt = default;
            string reason = requester.MissedNear is { } near
                ? Inv($"no way along the planned way round was found near {Point(near)}; searched {corridor.Size:0} m around the start and goal")
                : requester.RolledFromEdge && _body.TrySample(out rolledAt)
                    ? Inv($"no way on toward the goal was found; it is {HorizontalDistance(rolledAt.Position, requester.Goal):0} m away")
                    : corridor.Reason;
            End(requester, NavigationWalkState.NoRoute, reason);
            return;
        }
        if (corridor.Outcome == NavCorridorOutcome.TowardEdge && _body.TrySample(out NavigationWalkBodySample here))
        {
            float away = HorizontalDistance(here.Position, requester.Goal);
            if (away - HorizontalDistance(corridor.Points[^1], requester.Goal) < MinimumStageProgress)
            {
                End(requester, NavigationWalkState.NoRoute, Inv($"no way on toward the goal was found; it is {away:0} m away"));
                return;
            }
        }
        requester.Corridor = corridor;
        requester.CorridorIndex = 0;
        requester.Builds = 0;
        requester.StageBudget = Math.Max(MaximumStages, requester.Stages + corridor.StageBudget);
        if (Narration is not null)
        {
            int step = Math.Max(1, corridor.Points.Count / 24);
            Detail("Way round points: " + string.Join(" -> ",
                corridor.Points.Where((_, index) => index % step == 0 || index == corridor.Points.Count - 1).Select(Point)));
        }
        Publish(requester, NavigationWalkState.Planning, Inv($"following a way round of {corridor.Length:0} m"), float.NaN);
    }

    /// <summary>
    /// Plans the next stage along the way round: toward the point <see cref="NavCorridor.StageLookahead"/>
    /// further along it, or, once no more than <see cref="NavCorridor.FinalStretch"/> of it is left,
    /// to the goal itself, arriving as the walk asks.
    /// </summary>
    private void AdvanceAlongCorridor(Request active, NavCorridor corridor, NavGrid? usable, in NavigationWalkBodySample sample)
    {
        active.CorridorIndex = corridor.NearestIndex(sample.Position, active.CorridorIndex);
        bool final = corridor.Outcome == NavCorridorOutcome.Reaches
            && corridor.RemainingFrom(active.CorridorIndex) <= NavCorridor.FinalStretch;
        int targetIndex = final ? corridor.Points.Count - 1 : corridor.IndexAhead(active.CorridorIndex, NavCorridor.StageLookahead);
        Vector3 target = final ? active.Goal : corridor.Points[targetIndex];
        // The square the stage is planned over holds the whole stretch of the way round it
        // follows, not just its ends: a way round that bends out past a square holding both
        // ends goes round something that square cannot show the way round.
        if (usable is null
            || !usable.Contains(sample.Position, CoverMargin)
            || !usable.Contains(target, CoverMargin)
            || !corridor.Span(active.CorridorIndex, targetIndex).All(point => usable.Contains(point, CoverMargin)))
        {
            if (!NavCorridor.TrySquareOver(
                    [sample.Position, target, .. corridor.Span(active.CorridorIndex, targetIndex)],
                    RegionMargin, MinimumRegion, MaximumRegion, out float originX, out float originY, out float size))
            {
                End(active, NavigationWalkState.NoRoute, $"the way round bends too far for one stage near {Point(target)}");
                return;
            }
            BuildGrid(active, sample, originX, originY, size, dungeon: 0u);
            return;
        }
        active.StageTargetIndex = targetIndex;
        if (Narration is not null)
        {
            Detail(Inv(
                $"{(final ? "Last stage" : $"Stage {active.Stages + 1}")} toward {Point(target)}, {corridor.AlongAt(targetIndex):0} m along the way round, {corridor.RemainingFrom(targetIndex):0} m left"));
        }
        SearchRoute(active, usable, sample, final ? null : target, detour: false);
    }

    /// <summary>Lets a test stand in for the way round a search found. Never set outside tests.</summary>
    internal Func<NavCorridor, NavCorridor>? CorridorPlanned { get; set; }

    /// <summary>
    /// A stage of the way round found no route to its point, or ended short of it: the coarse
    /// grid saw a way the fine one does not, such as a gap narrower than a body. Keeps the next
    /// way round out of the middle of that stretch and plans one, or, after
    /// <see cref="MaximumCorridorPlans"/>, gives up. The keep-out is centred midway along the
    /// missed stretch, so a block at the far end of a long stage may take more than one plan
    /// to get round.
    /// </summary>
    private void StageMissed(Request active, in NavigationWalkBodySample sample, NavRoute? failed = null)
    {
        NavCorridor corridor = active.Corridor!;
        int middle = (active.CorridorIndex + Math.Max(active.CorridorIndex, active.StageTargetIndex)) / 2;
        Vector3 near = corridor.Points.Count > 0 ? corridor.Points[Math.Min(middle, corridor.Points.Count - 1)] : active.Goal;
        _driver = null;
        active.Staged = false;
        active.StageTarget = null;
        active.Builds = 0;
        float radius = MathF.Min(CorridorKeepOutRadius, HorizontalDistance(near, sample.Position) - 2f);
        if (active.CorridorPlans >= MaximumCorridorPlans || radius < 2f)
        {
            if (Narration is not null && failed is not null)
                NarrateNoRoute(active, failed);
            End(active, NavigationWalkState.NoRoute,
                Inv($"no way along the planned way round was found near {Point(near)}; searched {corridor.Size:0} m around the start and goal"));
            return;
        }
        active.CorridorKeepOut.Add(new NavAvoidance(near, radius));
        active.Corridor = null;
        active.WantsCorridor = true;
        active.MissedNear = near;
        active.RolledFromEdge = false;
        string again = $"no way along the planned way round was found near {Point(near)}; planning another way round";
        Publish(active, NavigationWalkState.Planning, again, float.NaN);
        Say($"{Doing(active)} {Label(active)}: {again}");
    }

    /// <summary>
    /// Lets go of a follow's way round, and of one still being planned for it, when it plans
    /// toward the player afresh: the player has moved since it was planned, perhaps back within
    /// one square, and a way round toward where they stood leads on past where they are now.
    /// </summary>
    private void ForgetCorridor(Request request)
    {
        if (ReferenceEquals(_corridorFor, request))
            _corridorFor = null;
        if (ReferenceEquals(_active, request))
            Corridor = null;
        request.Corridor = null;
        request.CorridorPlans = 0;
        request.CorridorKeepOut.Clear();
        request.WantsCorridor = false;
        request.SquareNoRoute = null;
        request.RolledFromEdge = false;
        request.MissedNear = null;
        request.CorridorIndex = 0;
        request.StageTargetIndex = 0;
        request.StageTarget = null;
        request.Staged = false;
        request.StageBudget = MaximumStages;
    }

    /// <summary>
    /// How far a player being followed may move from where the way round leads before the
    /// follow lets it go and plans another: one stage's reach. Closer than that, the stage
    /// under way still leads toward them, and the last stage plans to wherever they then stand.
    /// </summary>
    internal const float FollowCorridorDrift = NavCorridor.StageLookahead;

    /// <summary>
    /// Whether a follow's way round still leads to the player, now standing at
    /// <paramref name="leader"/>: they are still beyond one square of the character, and no
    /// more than <see cref="FollowCorridorDrift"/> from where the way round leads. A way round
    /// that reaches its goal leads to its last point. One that ends at the edge of what was
    /// loaded, with the player beyond it, leads toward where the player stood when it was
    /// planned; its last point lies far short of them by design, so it is the player's drift
    /// from where they stood that tells whether it still leads their way.
    /// </summary>
    private static bool CorridorStillLeadsTo(Request request, NavCorridor corridor, Vector3 from, Vector3 leader)
    {
        if (TryChooseRegion(from, leader, out _, out _, out _))
            return false;
        Vector3 leadsTo = corridor.Outcome == NavCorridorOutcome.Reaches && corridor.Points.Count > 0
            ? corridor.Points[^1]
            : request.CorridorToward;
        return HorizontalDistance(leadsTo, leader) <= FollowCorridorDrift;
    }

    /// <summary>What a walk's narration calls it: a follow, a walk, or a route asked for alone.</summary>
    private static string Doing(Request request) =>
        request.Follow ? "Follow" : request.Walk ? "Walk to" : "Route to";

    /// <summary>What is left of the way round beyond the stage under way, and on to the goal past its end.</summary>
    private static float CorridorBeyond(Request request) =>
        request.Corridor is not { } corridor
            ? 0f
            : corridor.RemainingFrom(request.StageTargetIndex)
                + (corridor.Outcome == NavCorridorOutcome.TowardEdge ? HorizontalDistance(corridor.Points[^1], request.Goal) : 0f);
}
