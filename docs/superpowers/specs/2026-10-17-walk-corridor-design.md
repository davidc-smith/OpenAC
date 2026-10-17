# Walk corridors: coarse way-round planning for outdoor walks

Date: 2026-10-17 · Request: Aethrix api-gaps #72 ("A walk can't go round a detour that leaves its planning square") · Fork read at `7b6641cb`

## Problem

An outdoor `GoTo` plans over one square around the character and the goal: 96–320 m a side, with a 24 m margin (`TryChooseRegion`). When the only way round a cliff leaves that square, the fine search runs out of nodes and `NavRouter.Find` returns `NoPath`, with the reason "no route reaches the floor the goal stands on". The failure handler retries only for stuck spots and moved objects, so the walk ends `NoRoute` without moving. Goals beyond 320 m are walked in stages, and each stage aims straight at the goal with `FindToward`, so a far walk with a cliff across its line stalls at the cliff's foot.

## Spike findings (offline, installed dats, client rules, Debug build)

| Walk | Grid | Result | Build | Search |
|---|---|---|---|---|
| Stonehold (`0x5ED7000A [31.0 30.9 79.7]` → 69.60N 25.56W) | 304 m @ 0.25 m (today) | NoPath, reproduces live | 0.6 s | 0.5 s |
| Stonehold | 960 m @ 0.5 m | Routed, 737 m, walking only | 1.7 s, 233 MB | 0.9 s |
| Stonehold | 960 m @ 0.25 m | Routed, 738 m | 6.7 s, 851 MB | 3.8 s |
| Stonehold | 1,344 m @ 1 m | Routed, 814 m, same corridor | 1.0 s, 116 MB | 0.4 s |
| Plateau (`0x48B8001F [92.1 151.9 105.2]` → 45.04N 43.07W) | 272 m @ 0.25 m (today) | NoPath, reproduces live | 0.5 s | 0.5 s |
| Plateau | up to 1,344 m @ 0.5–1 m, walking only | NoPath: the goal's plateau is large but never meets the start's ground | — | — |
| Plateau | 1,344 m @ 1 m, leaps (skill 443) | Routed, 1,545 m, 7 leaps | — | 202 s |

Conclusions:

- Stonehold is a real defect. A wider search fixes it, and a 1 m coarse grid over the loaded area finds the same corridor cheaply.
- The plateau has no walking way up under `FloorZ`. The Aethrix scanner's 492 m path used a looser slope rule. The expected result is `NoRoute`, with the searched area named.
- Coarse searches must be walking only. Leap planning over large grids is far too slow.
- One large square at 0.25 m is too heavy to be the fix.

## Decision

Hierarchical planning for every outdoor walk too large or too twisty for one fine square:

1. A **corridor** is a walking-only route over a coarse grid covering the loaded outdoor area. It answers "which way round?".
2. **Fine stages** at 0.25 m, with leaps allowed, follow the corridor. They answer "where exactly do the feet go?".

A baked or shipped navmesh was considered and rejected for now. It would need a second collision model kept in step with the client's rules, plus a bake pipeline. It would still not know about doors, creatures or server-placed objects. The on-demand corridor costs about 1 s per walk that needs one. Revisit if walks need detours wider than the loaded area, if plugins need world-scale reachability, or if corridor builds show up as a real cost live. The next step would then be a lazily computed, disk-cached per-landblock coarse layer built with the client's own rules, not a separately baked mesh.

## Policy

| Situation | Behaviour |
|---|---|
| Sealed dungeon | Unchanged: one grid over the dungeon. |
| Follow | Unchanged. |
| Outdoor goal fits one square (≤ 320 m) | Fine search first, unchanged. If it fails with `NoPath` (`NoStart`/`NoGoal` excluded), plan a corridor and walk it in stages. |
| Outdoor goal does not fit one square | Corridor first, then fine stages along it. This replaces straight-aim staging. |
| Corridor finds nothing | End `NoRoute` before moving, naming the area searched. Never walk to the foot of the cliff. |

Applies to `GoTo` place, `GoTo` object and `StandOn`. The final stage uses that request's normal arrival: goal floor, object side and sight, or onto.

## Components

### Core: `AcDream.Core.Navigation/NavCorridor.cs` (new)

Plain C#, no client state, testable from shapes built in code.

- `NavCorridor.Plan(NavGrid coarse, Vector3 from, Vector3 goal, float arrival, bool arriveOnGoalFloor, IReadOnlyList<NavAvoidance>? avoid)` returns a `NavCorridor` result:
  - `Outcome`: `Reaches` (the goal, or its floor for places), `TowardEdge` (the goal lies beyond the coarse grid; the corridor ends at the edge nearest it), `NoPath`, `NoStart`.
  - `Waypoints`: a simplified polyline, start to end.
  - `Length`, `Reason`, `Milliseconds`, plus the grid's `OriginX/OriginY/Size/CellSize` for narration and reasons.
- It reuses `NavRouter.Find`, or `FindToward` when the goal is outside the grid, with `leaps: null` and no crowd. No new search algorithm.
- Simplification: drop path nodes while the straight line between kept points stays walkable on the coarse grid (`CanWalkStraight`), with a cap of about 40 m per segment, so stage targets stay close to real ground.
- `NavCorridor.TargetAhead(Vector3 position, float lookahead)` returns the point on the polyline `lookahead` metres beyond the point nearest `position`, measured along the polyline. It also reports the remaining length from that nearest point to the end.

### Runtime: `NavigationWalkController` (changed; corridor logic in a new partial, `NavigationWalkController.Corridor.cs`)

**Corridor region.** A square over the resident outdoor landblocks (`PhysicsEngine.LandblockIds`):
- It's centred between the character and the goal, clamped to the resident extent, capped at 1,536 m (`MaximumCorridorRegion`).
- Cells are 1 m, or 0.5 m when the square is ≤ 768 m, which keeps the column count at about 2.4 M or less.
- It's captured on the update thread with `NavGeometry.Capture(..., _goals.StandsStill)`, then built and searched on one worker task. The coarse grid is not kept: only the `NavCorridor` result is stored on the request.

**Request state.** A walk on a corridor stores:
- `Request.Corridor` (the `NavCorridor`),
- `Request.CorridorPlans` (count),
- `Request.CorridorKeepOut` (avoidance spots from failed stretches).

`Staged` keeps its meaning: "the route being walked is one stage".

**Stage targets.**
- With a corridor, each stage aims at `TargetAhead(position, StageLookahead = 120 m)`.
- The stage square is `TryChooseSquare(position, target)`, which is always ≤ 320 m.
- The fine search is `NavRouter.Find(grid, from, target, StageArrival = 4 m, ..., arriveOnGoalFloor: false)` with leaps. Coarse waypoints can sit on spots the fine grid treats as unclear, which is why the arrival tolerance is generous.
- If the goal itself lies inside the stage square with margin (`grid.Contains(goal, CoverMargin)`), the stage is instead the request's normal final search.

**Rolling corridors.** A `TowardEdge` corridor is planned again from the character's position when its remaining length drops below `StageLookahead`. Landblocks further on have streamed in by then.

**Stage failure.**
- A fine stage that cannot reach its corridor target, or that makes less than `MinimumStageProgress` along the corridor, means the coarse grid lied (for example, a gap that looks open at 1 m).
- The walk adds a keep-out spot over that corridor stretch (centre = target, radius 12 m) and plans the corridor again. At most `MaximumCorridorPlans = 3` plans in all.
- After that it ends `NoRoute`: "no way along the planned way round was found near {point}; searched N m around the start and goal".
- Stuck recovery inside a stage (the replan ladder) is unchanged.

**Stage budget.** `MaximumStages = 12` is replaced by `ceil(corridor.Length / (StageLookahead × 0.75)) + 4`, recomputed per corridor plan. The fixed limit remains only as a floor.

**Escalation from one square.**
- In the route-collection path, the order is: stuck-spot clearing, then the `ObjectsMoved` rebuild, then this check.
- A non-staged, non-follow, outdoor request whose route is `NoPath` and has no corridor yet goes back to `Planning` ("planning a way round over N m") and starts a corridor plan, instead of ending.
- The door/object `Blocked` endings and the `Replans > 0` blocked ending keep priority. Those walks already moved and met something.

**Remaining distance.** With a corridor, `RemainingMeters` = the stage route left + the corridor length from the stage target to its end, plus, for `TowardEdge`, a straight line to the goal.

**Reasons and states.**
- While the corridor plans, the walk is in `Planning` with the reason "planning a way round over {size:0} m around the start and goal".
- Corridor `NoPath`: `NoRoute` with "{fine reason} within {size:0} m around the start and goal", for example "no route reaches the floor the goal stands on within 1,344 m around the start and goal".
- Corridor `NoStart`: today's no-start reason.

### Preview: `NavigationWalkController.Preview.cs`

- Same policy. One square first; on `NoPath`, or a goal beyond one square, plan a corridor, then plan each fine stage in turn on the worker, each stage starting from the previous stage's last leg point.
- The legs are stitched into one `PluginNavigationPlan` path, with `LengthMeters` = the sum of the stages.
- A goal beyond the loaded area returns `NoRoute`: "the destination is beyond the loaded area, {d:0} m away".
- A failed corridor or stage returns `NoRoute` with the same reasons as a walk.
- Planned spots to keep out of (portals, obstacles) are gathered per stage square, as today per square.
- Cost: about 0.5–1 s per stage (Debug), so a few seconds for Stonehold.
- No contract change.

### Narration and overlay

- `/nav debug` adds corridor lines:
  - region, cell size, build and search ms, outcome, length, up to 24 waypoints;
  - "stage N toward {point}, {d:0} m along the way round, {left:0} m left";
  - each keep-out-and-replan.
- The graphical client's route overlay draws `Corridor.Waypoints` as a fainter line under the fine route, while a corridor walk is active or after `/nav route`. Headless draws nothing, as now.

## Constraints

- **Upstream-portable.** The work touches only Core navigation, Runtime navigation and the App overlay, with no fork-only APIs, so the branch can be offered upstream later.
- **No plugin contract change.** The fork contract stays `0.1.25-fork.7`.
- **Threads.** Capture happens on the update thread; build and search on workers. At most one build or search task runs at a time, as today (`_building`/`_routing`). The corridor uses its own task field, `_corridoring`, and `Advance` waits on it like the others.
- **Interruptions.** The player's movement keys, portal space, `StopGoTo` and a newer request end a walk during corridor planning as they do during any planning.

## Testing

**Core, `tests/AcDream.Core.Tests/Navigation/NavCorridorTests.cs` (synthetic geometry):**
1. A walled plateau whose only ramp starts outside the first `TryChooseRegion` square: `Reaches`, with the waypoints passing the ramp.
2. The same plateau without a ramp: `NoPath`.
3. A goal beyond the coarse grid: `TowardEdge`, ending at the edge nearest the goal.
4. Simplification: no segment crosses a wall; segments are ≤ 40 m.
5. `TargetAhead` measures along the polyline and clamps at the end.

**Runtime, `tests/AcDream.App.Tests/Navigation/NavigationWalkControllerTests.cs`:**
1. A near goal whose fine search fails with `NoPath` escalates to a corridor and arrives through stages.
2. A far goal plans a corridor first; `RemainingMeters` follows the corridor, not the straight line.
3. A stage that cannot reach its target leads to a keep-out and a corridor replan, then `NoRoute` with the area named after `MaximumCorridorPlans`.
4. Regression: a goal fitting one square builds no corridor and plans as before.
5. A corridor with no way: `NoRoute` before any movement, with the reason naming the area.
6. Preview: the stitched path for a corridor walk; `NoRoute` beyond the loaded area.

**Installed dats, `Lane=InstalledDat`, `tests/AcDream.App.Tests/Navigation/WalkCorridorInstalledDatTests.cs`:**
1. Stonehold (7×7 landblocks around `0x5ED7`): the corridor plus stitched fine stages arrive on the goal floor.
2. Plateau (7×7 around `0x48B8`): `NoPath` within the area, with the reason naming it.
3. `NavigationWalkCorpusTests` stay green.

**Live (dev server 10.10.20.20):**
1. `/teleloc` both start points and walk to both goals. Stonehold arrives; the plateau ends `NoRoute`, naming the area.
2. Aethrix `docs/superpowers/live-checks/2026-10-17-walk-probe.md`: towns, trees, rocks, long walks (Arwic → Eastham, now without a resend), water. All results the same as before or better.
3. One Release-build timing of a corridor build and search, recorded in `docs/navigation.md`.

## Docs

`docs/navigation.md`:
- the "Far goals" row,
- a new "Corridors" subsection under "The grid and the router",
- the preview limits text (320 m becomes the loaded area),
- Known limits: a way up that needs jumps isn't found by a corridor, as on the plateau; the corridor covers only the loaded area,
- the `NavCorridor` row in the planning types table.

Aethrix api-gaps #72 is marked resolved, with the plateau finding.

## Out of scope

- Corridors that plan leaps.
- Corridors in sealed dungeons and for follows.
- Baked or cached navmeshes.
- Routes beyond the loaded area other than by rolling the corridor forward as the walk moves.
