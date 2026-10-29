using System.Numerics;
using AcDream.Core.Navigation;
using AcDream.Core.Physics;
using AcDream.Plugin.Abstractions;

namespace AcDream.Runtime.Navigation;

internal sealed partial class NavigationWalkController
{
    internal Task<PluginNavigationPlan> PreviewPathAsync(uint objectId, float arrivalMeters)
    {
        if (!_goals.TryLocate(objectId, out Vector3 goal))
            return PreviewResult(PluginNavigationPlanStatus.NoRoute, "the client has no position for the object");
        float radius = _goals.TryGetSurfaces(objectId, out NavSurfaces surfaces)
            ? RadiusOf(surfaces, goal) : 0f;
        return PreviewPathAsync(goal, objectId, arrivalMeters, onGoalFloor: false, radius);
    }

    internal Task<PluginNavigationPlan> PreviewPathAsync(uint cellId, Vector3 local, float arrivalMeters)
    {
        if (!_goals.TryLocatePlace(cellId, local, out Vector3 goal))
            return PreviewResult(PluginNavigationPlanStatus.NoRoute, "the client cannot place the destination");
        return PreviewPathAsync(goal, 0u, arrivalMeters, onGoalFloor: true, 0f);
    }

    private Task<PluginNavigationPlan> PreviewPathAsync(
        Vector3 goal, uint objectId, float arrivalMeters, bool onGoalFloor, float goalRadius)
    {
        if (!_body.TrySample(out NavigationWalkBodySample sample) || sample.InPortalSpace || sample.Airborne
            || !_goals.TryGlobalOf(sample.Position, out Vector3 globalStart))
            return PreviewResult(PluginNavigationPlanStatus.Unavailable, "the character is not standing in the world");

        return TryMeasureDungeon(sample.CellId, out uint dungeonId, out Vector2 minimum, out Vector2 maximum)
            ? PreviewDungeonAsync(sample, globalStart, goal, objectId, arrivalMeters, onGoalFloor, goalRadius, dungeonId, minimum, maximum)
            : PreviewOutdoorAsync(sample, globalStart, goal, objectId, arrivalMeters, onGoalFloor, goalRadius);
    }

    /// <summary>
    /// A preview inside a sealed dungeon: one square over the whole dungeon, searched on a worker.
    /// </summary>
    private Task<PluginNavigationPlan> PreviewDungeonAsync(
        NavigationWalkBodySample sample, Vector3 globalStart, Vector3 goal, uint objectId, float arrivalMeters,
        bool onGoalFloor, float goalRadius, uint dungeonId, Vector2 minimum, Vector2 maximum)
    {
        bool region = TryChooseSquare(
            Vector2.Min(minimum, Vector2.Min(Flat(sample.Position), Flat(goal))),
            Vector2.Max(maximum, Vector2.Max(Flat(sample.Position), Flat(goal))),
            MaximumDungeonRegion, out float originX, out float originY, out float size);
        if (!region)
            return PreviewResult(PluginNavigationPlanStatus.NoRoute, "the destination is outside one planning region");

        NavGeometry? geometry = NavGeometry.CaptureDungeon(_physics, dungeonId, originX, originY, size, _goals.StandsStill);
        if (geometry is null)
            return PreviewResult(PluginNavigationPlanStatus.NoRoute, "no collision is loaded around the character");

        // Cell surfaces are immutable once loaded. Keep their identifiers with the
        // captured geometry so the worker can label indoor path points by floor.
        var cells = new List<CellSurface>();
        foreach (uint landblockId in geometry.LandblockIds)
        {
            if (_physics.TryGetLandblockCollision(landblockId, out _, out IReadOnlyList<CellSurface> found, out _))
                cells.AddRange(found);
        }
        NavAvoidance[] crowd = Crowd(sample, objectId);
        NavBody body = sample.Body;
        Vector3 start = sample.Position;
        NavLeapAbility? leaps = sample.Leaps;
        float half = size * 0.5f;
        var centre = new Vector3(originX + half, originY + half, 0f);
        float reach = half * MathF.Sqrt(2f);
        NavAvoidance[] portals = _goals.FindPortals(centre, reach, objectId, goal)
            .Select(spot => KeptOutOfPortal(spot, body)).ToArray();
        NavAvoidance[] obstacles = _goals.FindObstacles(centre, reach, objectId, geometry.ObjectIds)
            .Select(spot => spot with { Radius = spot.Radius + body.Radius }).ToArray();
        return Task.Run(() =>
        {
            try
            {
                NavGrid grid = NavGrid.Build(geometry, body);
                NavRoute route = AroundObstacles(goal, portals, obstacles,
                    spots => NavRouter.Find(grid, start, goal, PlanningRadius(arrivalMeters),
                        spots, leaps, crowd, onGoalFloor, goalRadius));
                if (route.Outcome != NavRouteOutcome.Routed || !route.EndsInSight)
                    return new PluginNavigationPlan(PluginNavigationPlanStatus.NoRoute, [], route.Reason);
                PluginNavigationPosition[] path = route.Legs
                    .Select(point => PreviewPosition(point, start, globalStart, sample.CellId, cells, true))
                    .ToArray();
                return new PluginNavigationPlan(PluginNavigationPlanStatus.Routed, path, route.Reason)
                {
                    LengthMeters = route.Length,
                };
            }
            catch (Exception error)
            {
                return new PluginNavigationPlan(PluginNavigationPlanStatus.Failed, [],
                    $"the search failed: {error.Message}");
            }
        });
    }

    /// <summary>
    /// A preview outdoors. A goal within one square is searched over that square first, as a
    /// walk would; one beyond it, or one the square holds no route to, is planned as a way
    /// round over everything loaded and searched in fine stages along it, joined into one path.
    /// All the searching happens on a worker.
    /// </summary>
    private Task<PluginNavigationPlan> PreviewOutdoorAsync(
        NavigationWalkBodySample sample, Vector3 globalStart, Vector3 goal, uint objectId, float arrivalMeters,
        bool onGoalFloor, float goalRadius)
    {
        bool square = TryChooseRegion(sample.Position, goal, out float originX, out float originY, out float size);
        float roundX = 0f, roundY = 0f, roundSize = 0f;
        bool haveRound = NavGeometry.TryMeasureResident(_physics, out Vector2 residentLow, out Vector2 residentHigh)
            && TryChooseCorridorRegion(sample.Position, goal, residentLow, residentHigh, out roundX, out roundY, out roundSize);
        NavGeometry? round = haveRound ? NavGeometry.Capture(_physics, roundX, roundY, roundSize, _goals.StandsStill) : null;
        if (!square && round is null)
            return PreviewResult(PluginNavigationPlanStatus.NoRoute, "no collision is loaded around the character");

        NavGeometry? geometry = square
            ? NavGeometry.Capture(_physics, originX, originY, size, _goals.StandsStill)
            : null;
        if (geometry is null && round is null)
            return PreviewResult(PluginNavigationPlanStatus.NoRoute, "no collision is loaded around the character");

        // Cell surfaces are immutable once loaded. Keep their identifiers with the
        // captured geometry so the worker can label indoor path points by floor.
        var cells = new List<CellSurface>();
        foreach (uint landblockId in (geometry?.LandblockIds ?? []).Union(round?.LandblockIds ?? []))
        {
            if (_physics.TryGetLandblockCollision(landblockId, out _, out IReadOnlyList<CellSurface> found, out _))
                cells.AddRange(found);
        }
        NavAvoidance[] crowd = Crowd(sample, objectId);
        NavBody body = sample.Body;
        Vector3 start = sample.Position;
        NavLeapAbility? leaps = sample.Leaps;
        // Portals and obstacles are gathered over the widest square searched, so every
        // stage of a way round keeps clear of them too.
        float half = (round is not null ? roundSize : size) * 0.5f;
        var centre = round is not null
            ? new Vector3(roundX + half, roundY + half, 0f)
            : new Vector3(originX + half, originY + half, 0f);
        float reach = half * MathF.Sqrt(2f);
        NavAvoidance[] portals = _goals.FindPortals(centre, reach, objectId, goal)
            .Select(spot => KeptOutOfPortal(spot, body)).ToArray();
        NavAvoidance[] obstacles = _goals.FindObstacles(centre, reach, objectId, round?.ObjectIds ?? geometry!.ObjectIds)
            .Select(spot => spot with { Radius = spot.Radius + body.Radius }).ToArray();
        return Task.Run(() =>
        {
            try
            {
                NavRoute Final(NavGrid grid, Vector3 from) =>
                    AroundObstacles(goal, portals, obstacles,
                        spots => NavRouter.Find(grid, from, goal, PlanningRadius(arrivalMeters), spots, leaps, crowd, onGoalFloor, goalRadius));
                NavRoute? route = square && geometry is not null ? Final(NavGrid.Build(geometry, body), start) : null;
                if ((route is null || route.Outcome == NavRouteOutcome.NoPath) && round is not null)
                {
                    float cell = CorridorCellSize(roundSize);
                    NavCorridor corridor = NavCorridor.PlanOver(round, body, cell, start, goal,
                        PlanningRadius(arrivalMeters), onGoalFloor, leaps: leaps);
                    if (corridor.Outcome == NavCorridorOutcome.TowardEdge)
                        return new PluginNavigationPlan(PluginNavigationPlanStatus.NoRoute, [],
                            Inv($"the destination is beyond the loaded area, {HorizontalDistance(start, goal):0} m away"));
                    if (corridor.Outcome != NavCorridorOutcome.Reaches)
                        return new PluginNavigationPlan(PluginNavigationPlanStatus.NoRoute, [], corridor.Reason);
                    route = NavCorridor.Stitch(round, body, corridor, start, goal,
                        (grid, from, to, final) => final
                            ? Final(grid, from)
                            : AroundObstacles(to, portals, obstacles,
                                spots => NavRouter.Find(grid, from, to, NavCorridor.StageArrival, spots, leaps, crowd)),
                        Math.Max(MaximumStages, corridor.StageBudget));
                }
                if (route is null || route.Outcome != NavRouteOutcome.Routed || !route.EndsInSight)
                    return new PluginNavigationPlan(PluginNavigationPlanStatus.NoRoute, [], route?.Reason ?? "no route was found");
                PluginNavigationPosition[] path = route.Legs
                    .Select(point => PreviewPosition(point, start, globalStart, sample.CellId, cells, false))
                    .ToArray();
                return new PluginNavigationPlan(PluginNavigationPlanStatus.Routed, path, route.Reason) { LengthMeters = route.Length };
            }
            catch (Exception error)
            {
                return new PluginNavigationPlan(PluginNavigationPlanStatus.Failed, [], $"the search failed: {error.Message}");
            }
        });
    }

    private static PluginNavigationPosition PreviewPosition(
        Vector3 point, Vector3 start, Vector3 globalStart, uint startCell,
        IReadOnlyList<CellSurface> cells, bool sealedDungeon)
    {
        Vector3 global = globalStart + point - start;
        uint cellId = 0u;
        float nearest = float.PositiveInfinity;
        foreach (CellSurface cell in cells)
        {
            float? floor = cell.SampleFloorZ(point.X, point.Y);
            if (floor is not { } z || MathF.Abs(z - point.Z) >= nearest)
                continue;
            nearest = MathF.Abs(z - point.Z);
            cellId = cell.CellId;
        }
        if (cellId == 0u && sealedDungeon)
            cellId = startCell;
        if (cellId == 0u)
        {
            int bx = Math.Clamp((int)MathF.Floor(global.X / 192f), 0, 255);
            int by = Math.Clamp((int)MathF.Floor(global.Y / 192f), 0, 255);
            cellId = TerrainSurface.ComputeOutdoorCellId((uint)((bx << 24) | (by << 16)),
                global.X - bx * 192f, global.Y - by * 192f);
        }
        return RuntimeNavigationProjection.GlobalPosition(cellId, global);
    }

    private static Task<PluginNavigationPlan> PreviewResult(PluginNavigationPlanStatus status, string reason) =>
        Task.FromResult(new PluginNavigationPlan(status, [], reason));
}
