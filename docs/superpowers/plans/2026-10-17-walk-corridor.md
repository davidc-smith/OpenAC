# Walk Corridor Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers-extended-cc:subagent-driven-development (recommended) or superpowers-extended-cc:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Outdoor walks that are too long or too twisty for one 320 m planning square first plan a walking-only "way round" (corridor) over a coarse grid of the loaded area, then walk it in ordinary fine stages. This fixes Aethrix api-gaps #72.

**Architecture:**
- Core gains `NavCorridor`: a coarse corridor search that reuses `NavRouter.Find`/`FindToward` on a 0.5–1 m `NavGrid`, together with stage helpers (`NearestIndex`, `IndexAhead`, `TrySquareOver`, `Stitch`).
- `NavGeometry` gains `Crop` and `TryMeasureResident`.
- `NavigationWalkController`:
  - plans a corridor on a worker task, either when a one-square place/object walk ends `NoPath` or up front for a goal beyond one square;
  - walks the corridor in stages aimed about 120 m ahead along it;
  - on a missed stage, keeps out of that stretch and plans again (up to 3 corridor plans).
- Preview stitches the same stages on its worker.

**Tech Stack:** C# / .NET 10, xUnit. `~/.dotnet` SDK (`export PATH=~/.dotnet:$PATH`). Installed-dat lane: `ACDREAM_DAT_DIR=~/AsheronsCall`.

**Spec:** `docs/superpowers/specs/2026-10-17-walk-corridor-design.md` (branch `docs/walk-corridor-spec`)

## Global Constraints

- **Corridor region:**
  - a square over the resident landblocks' terrain extent, capped at **1,536 m** (`MaximumCorridorRegion`);
  - cell size **0.5 m when size ≤ 768 m**, else **1 m**;
  - the search is walking only (`leaps: null`, no crowd);
  - only the `NavCorridor` result is kept; the coarse grid is discarded.
- **Stage constants** (on `NavCorridor`):

  | Constant | Value |
  |---|---|
  | `StageLookahead` | 120 m |
  | `StageArrival` | 4 m |
  | `StageMiss` | 8 m |
  | `FinalStretch` | 144 m |
  | `StageMargin` | 24 m |
  | `MinimumStageSquare` | 96 m |
  | `MaximumStageSquare` | 320 m |
  | `EdgeBand` | 8 m |
  | `NearestWindow` | 200 m |

- **Controller constants:**

  | Constant | Value |
  |---|---|
  | `MaximumCorridorRegion` | 1536 |
  | `FineCorridorRegion` | 768 |
  | `MaximumCorridorPlans` | 3 |
  | `CorridorKeepOutRadius` | 12 m |
  | `MaximumStages` | stays 12, now only a floor under the corridor's own budget |

- **Reason texts (exact):**
  - Planning: `planning a way round over {size:0} m around the start and goal`
  - Corridor `NoPath` (place): `no route reaches the floor the goal stands on within {size:0} m around the start and goal`. Generally `{router reason} within {size:0} m around the start and goal`.
  - Missed stage, out of plans: `no way along the planned way round was found near {Point}; searched {size:0} m around the start and goal`
  - Missed stage, replanning: `no way along the planned way round was found near {Point}; planning another way round`
  - Rolling with no progress: `no way on toward the goal was found; it is {away:0} m away` (kept from today)
  - Preview beyond the loaded area: `the destination is beyond the loaded area, {d:0} m away`
- **Unchanged behaviour:**
  - Sealed dungeons are unchanged.
  - Follows never escalate on `NoPath`. A follow beyond one square uses a corridor, since straight-aim staging is removed.
  - A goal that fits one square plans exactly as before and builds no corridor.
  - No plugin contract change: the fork contract stays `0.1.25-fork.7`.
- **Upstream-portable.** Touch only `AcDream.Core/Navigation`, `AcDream.Runtime/Navigation`, `AcDream.App/Rendering/NavMeshDebugOverlay.cs`, `docs/navigation.md` and their tests. No fork-only APIs.
- **Match the code around it.** Navigation code uses plain-English XML doc comments that say what a thing does in the game ("A walk that …"), `Inv(...)` for invariant-culture interpolation, `Point(...)` for map-coordinate formatting, and `Say`/`Detail` for narration.

**User decisions (already made):**
- "option B": a coarse corridor, then fine stages.
- "lets just build option 3 then and get it right from the beginning": the corridor is used for every outdoor walk beyond one square, plus escalation on `NoPath`.
- Keep the fine-first try for goals within 320 m ("its fine, proceed").
- Stitched fine preview; no contract change ("yes").
- "build it so we can send it upstream if we decide too": a fork branch, upstream-portable, with the upstream PR decided later.
- No baked navmesh for now (RynthSuite comparison discussed).

---

## Setup (before Task 1)

```bash
cd /Users/davidsmith/code/OpenAC/OpenAC
git worktree add -b feat/walk-corridor .worktrees/walk-corridor main
cd .worktrees/walk-corridor
export PATH=~/.dotnet:$PATH
dotnet build tests/AcDream.App.Tests -v q 2>&1 | tail -3   # expect: Build succeeded
```

All later paths are relative to `.worktrees/walk-corridor`. The throwaway spike `tests/AcDream.App.Tests/Navigation/DetourSpikeInstalledDatTests.cs` lives untracked in the **main checkout**, not in this worktree. Task 8 deletes it.

---

### Task 1: Core `NavCorridor` search and `NavGeometry` helpers

**Goal:** A pure Core corridor search over a coarse grid, plus geometry crop and resident-extent helpers.

**Files:**
- Create: `src/AcDream.Core/Navigation/NavCorridor.cs`
- Modify: `src/AcDream.Core/Navigation/NavGeometry.cs` (add `Crop`, `TryMeasureResident` after `OverlappingLandblocks`, ~line 408)
- Modify: `src/AcDream.Core/Navigation/NavRoute.cs:137`: `private const string ReachedEdgeReason` becomes `internal const string ReachedEdgeReason`
- Test: `tests/AcDream.Core.Tests/Navigation/NavCorridorTests.cs`

**Acceptance Criteria:**
- [ ] A ground floor split by a cliff wall with a ramp only at the far east end: `Plan` returns `Reaches`, and some point of `Points` lies beside the ramp (x > 360).
- [ ] The same world without the ramp: `NoPath`, and `Reason` ends with `within 400 m around the start and goal`.
- [ ] A goal outside the coarse grid: `TowardEdge`, ending within 8 m of the grid's edge toward the goal.
- [ ] `NearestIndex` never goes back behind `fromIndex`, and on an out-and-back corridor does not jump to the parallel return leg. `IndexAhead` measures along the polyline and clamps at the end.
- [ ] `TrySquareOver` holds every point with the margin, snaps to 0.25 m, and returns false above `largest`.
- [ ] `Crop` keeps only triangles and terrains overlapping the square; a grid built from it plans the same route inside it.

**Verify:** `dotnet test tests/AcDream.Core.Tests --filter "FullyQualifiedName~NavCorridorTests"` → all pass

**Steps:**

- [ ] **Step 1: Write the failing tests** in `tests/AcDream.Core.Tests/Navigation/NavCorridorTests.cs`:

```csharp
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

    private static NavTriangle[] Floor(float x0, float y0, float x1, float y1, float z) =>
        Quad(new Vector3(x0, y0, z), new Vector3(x1, y0, z), new Vector3(x1, y1, z), new Vector3(x0, y1, z));

    private static NavTriangle[] Ramp(float x0, float y0, float x1, float y1, float zAtY0, float zAtY1) =>
        Quad(new Vector3(x0, y0, zAtY0), new Vector3(x1, y0, zAtY0), new Vector3(x1, y1, zAtY1), new Vector3(x0, y1, zAtY1));

    private static NavTriangle[] Wall(float x0, float y0, float x1, float y1, float bottom, float top) =>
        Quad(new Vector3(x0, y0, bottom), new Vector3(x1, y1, bottom), new Vector3(x1, y1, top), new Vector3(x0, y0, top));

    private static NavTriangle[] Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d) => [new(a, b, c), new(a, c, d)];
}
```

(`Quad` mirrors `NavGridTests.Quad`. Check that file's helper and copy its winding exactly if it differs.)

The ramp rises 6 m over 40 m (≈ 8.5°), which is walkable under `PhysicsGlobals.FloorZ`. Its west side is walled so it's entered only from the south. If the 1 m grid shows the plateau edge as unclear (step heights at 1 m columns), widen the plateau floor test spans; never weaken the assertions.

- [ ] **Step 2: Run the tests to check they fail**

Run: `dotnet test tests/AcDream.Core.Tests --filter "FullyQualifiedName~NavCorridorTests"`
Expected: build error. `NavCorridor`, `NavCorridorOutcome` and `NavGeometry.Crop` are not defined.

- [ ] **Step 3: Make `ReachedEdgeReason` internal** (`NavRoute.cs:137`):

```csharp
    internal const string ReachedEdgeReason = "the goal lies beyond this grid, so the route ends at its edge toward the goal";
```

- [ ] **Step 4: Add `Crop` and `TryMeasureResident` to `NavGeometry`** (after `OverlappingLandblocks`):

```csharp
    /// <summary>
    /// The part of this geometry over a smaller square: the terrains, triangles and
    /// cylinders that overlap it. A walk planned in stages along a way round builds each
    /// stage's grid from the geometry captured once for the whole way.
    /// </summary>
    public NavGeometry Crop(float originX, float originY, float size)
    {
        float right = originX + size;
        float top = originY + size;
        bool Overlaps(NavTriangle t) =>
            MathF.Min(t.A.X, MathF.Min(t.B.X, t.C.X)) < right
            && MathF.Max(t.A.X, MathF.Max(t.B.X, t.C.X)) > originX
            && MathF.Min(t.A.Y, MathF.Min(t.B.Y, t.C.Y)) < top
            && MathF.Max(t.A.Y, MathF.Max(t.B.Y, t.C.Y)) > originY;
        return new NavGeometry(
            originX,
            originY,
            size,
            [.. Terrains.Where(t => t.OriginX < right && t.OriginX + LandblockSize > originX
                && t.OriginY < top && t.OriginY + LandblockSize > originY)],
            [.. CellTriangles.Where(Overlaps)],
            [.. ObjectTriangles.Where(Overlaps)],
            [.. Cylinders.Where(c => c.Base.X + c.Radius > originX && c.Base.X - c.Radius < right
                && c.Base.Y + c.Radius > originY && c.Base.Y - c.Radius < top)],
            [.. ShellTriangles.Where(Overlaps)])
        {
            LandblockIds = LandblockIds,
            ObjectFingerprint = ObjectFingerprint,
            ObjectIds = ObjectIds,
        };
    }

    /// <summary>
    /// The square the terrain of every resident landblock spans together, or false
    /// when none is resident: the most a way round can be planned over.
    /// </summary>
    public static bool TryMeasureResident(PhysicsEngine engine, out Vector2 minimum, out Vector2 maximum)
    {
        ArgumentNullException.ThrowIfNull(engine);
        minimum = new Vector2(float.PositiveInfinity);
        maximum = new Vector2(float.NegativeInfinity);
        foreach (uint landblockId in engine.LandblockIds)
        {
            if (!engine.TryGetLandblockCollision(landblockId, out _, out _, out Vector3 offset))
                continue;
            minimum = Vector2.Min(minimum, new Vector2(offset.X, offset.Y));
            maximum = Vector2.Max(maximum, new Vector2(offset.X + LandblockSize, offset.Y + LandblockSize));
        }
        return minimum.X <= maximum.X;
    }
```

- [ ] **Step 5: Create `src/AcDream.Core/Navigation/NavCorridor.cs`:**

```csharp
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
    public float AlongAt(int index) => _along[Math.Clamp(index, 0, _along.Length - 1)];

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

    private static float Snap(float coordinate) =>
        MathF.Floor(coordinate / NavGrid.DefaultCellSize) * NavGrid.DefaultCellSize;

    private static Vector2 Flat(Vector3 point) => new(point.X, point.Y);
}
```

(`NavGrid.Contains(Vector3 point, float margin = 0f)` already exists.)

- [ ] **Step 6: Run the tests to check they pass**

Run: `dotnet test tests/AcDream.Core.Tests --filter "FullyQualifiedName~NavCorridorTests|FullyQualifiedName~NavGridTests"`
Expected: PASS, with `NavGridTests` still green.

- [ ] **Step 7: Commit**

```bash
git add src/AcDream.Core/Navigation/NavCorridor.cs src/AcDream.Core/Navigation/NavGeometry.cs src/AcDream.Core/Navigation/NavRoute.cs tests/AcDream.Core.Tests/Navigation/NavCorridorTests.cs
git commit -m "feat(nav): plan a walking way round over a coarse grid of the loaded area

Claude-Session: https://claude.ai/code/session_01A2zB2pRyNmj41iT6MFGZ2Z"
```

---

### Task 2: Core `NavCorridor.Stitch`, fine stages along a corridor

**Goal:** One Core loop that plans fine stages along a corridor over captured geometry and joins them into one route. Preview (Task 6) and the installed-dat tests (Task 8) use it.

**Files:**
- Modify: `src/AcDream.Core/Navigation/NavCorridor.cs` (add the `NavStageSearch` delegate and `Stitch`)
- Test: `tests/AcDream.Core.Tests/Navigation/NavCorridorTests.cs`

**Acceptance Criteria:**
- [ ] On the ramp plateau, `Stitch` returns `Routed` with legs starting at the start and ending within 2.5 m of the goal at z ≈ 6, and `Length` > 500 m.
- [ ] A stage whose search ends more than `StageMiss` from its target returns `NoPath`, with a reason starting `no way along the way round reaches (`.
- [ ] It takes no more stages than `maximumStages`, else returns `NoPath` "the goal was still … after N stages".

**Verify:** `dotnet test tests/AcDream.Core.Tests --filter "FullyQualifiedName~NavCorridorTests"` → all pass

**Steps:**

- [ ] **Step 1: Write the failing tests** (append to `NavCorridorTests`):

```csharp
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
```

- [ ] **Step 2: Run the tests to check they fail.** Expected: build error, `Stitch` not defined.

- [ ] **Step 3: Implement `Stitch`.** Add the delegate above the class and the method inside it:

```csharp
/// <summary>
/// Searches one stage's fine grid from a start to a point: the stage's point along the way
/// round, or, for the <paramref name="final"/> stage, the goal with the walk's own arrival.
/// </summary>
public delegate NavRoute NavStageSearch(NavGrid grid, Vector3 from, Vector3 to, bool final);
```

```csharp
    /// <summary>
    /// Plans the fine stages along a way round that reaches its goal, each over its own
    /// square cropped from <paramref name="geometry"/>, and joins their legs into one route.
    /// A stage that ends farther than <see cref="StageMiss"/> from its point misses it. The
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
        bool endsInSight = true;
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
            if (!final && Flat(route.Legs[^1] - target).Length() > StageMiss)
                return Failed($"no way along the way round reaches {Format(target)}");
            for (int leg = 1; leg < route.Legs.Count; leg++)
                legs.Add(route.Legs[leg]);
            length += route.Length;
            at = route.Legs[^1];
            if (final)
            {
                endsInSight = route.EndsInSight;
                return new NavRoute(NavRouteOutcome.Routed, route.Reason, [], legs, length, expansions, clock.Elapsed.TotalMilliseconds)
                {
                    EndsInSight = endsInSight,
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
```

- [ ] **Step 4: Run the tests to check they pass.** Run: `dotnet test tests/AcDream.Core.Tests --filter "FullyQualifiedName~NavCorridorTests"`. Expected: PASS.

- [ ] **Step 5: Commit** (`feat(nav): plan fine stages along a way round and join them`, with the Claude-Session trailer)

---

### Task 3: Controller plans a corridor when one square finds no route

**Goal:**
- A non-follow outdoor walk whose one-square search ends `NoPath` stays `Planning` and plans a corridor on a worker.
- A corridor that finds nothing ends the walk `NoRoute` before it moves, with the area named.
- Corridor collection and region choice are plumbed in. Walking along a found corridor comes in Task 4; until then a found corridor ends `NoRoute` "following a way round is not supported yet". Task 4 removes that interim line.

**Files:**
- Create: `src/AcDream.Runtime/Navigation/NavigationWalkController.Corridor.cs` (partial class: constants, `_corridoring`, `StartCorridor`, `CollectCorridor`, `TryChooseCorridorRegion`, `CorridorCellSize`)
- Modify: `src/AcDream.Runtime/Navigation/NavigationWalkController.cs`:
  - `Tick` (~line 749): call `CollectCorridor()` after `CollectRoute()`
  - `IsSearching`/`SearchFinished` (~line 665): include `_corridoring`
  - `Advance` (~line 927): wait on `_corridoring`; handle `WantsCorridor`
  - `Begin` (~line 900): reset `Corridor = null`
  - `CollectRoute` (~line 2156): escalate
  - `Request` class (~line 3040): new members
- Test: `tests/AcDream.App.Tests/Navigation/NavigationWalkControllerTests.cs`

**Acceptance Criteria:**
- [ ] `WallWorld(gapFromX: null)` (wall across the whole world): `WalkToPlace` ends `NoRoute` with a reason ending `within 384 m around the start and goal`, and `body.MovesBegun == 0`.
- [ ] A near walk on `FlatWorld()` arrives with `walk.Corridor == null`: no corridor is planned.
- [ ] `TryChooseCorridorRegion` holds the character with `RegionMargin` to spare, stays inside the resident extent when that is wider than the square, and caps at 1,536 m.
- [ ] All existing `NavigationWalkControllerTests` still pass.

**Verify:** `dotnet test tests/AcDream.App.Tests --filter "FullyQualifiedName~NavigationWalkControllerTests"` → all pass

**Steps:**

- [ ] **Step 1: Add the test world and the failing tests** to `NavigationWalkControllerTests.cs`. Put the world builder beside `FlatWorld`:

```csharp
    /// <summary>
    /// Four flat landblocks, 384 m square, crossed east-west at y = 200 by a wall 5 m high
    /// from x = 0 to <paramref name="gapFromX"/>, or the whole way when null.
    /// </summary>
    private static PhysicsEngine WallWorld(float? gapFromX)
    {
        float end = gapFromX ?? 384f;
        var wall = new Dictionary<ushort, Vector3>
        {
            [0] = new(0f, 200f, 0f),
            [1] = new(end, 200f, 0f),
            [2] = new(end, 200f, 5f),
            [3] = new(0f, 200f, 5f),
        };
        var physics = new PhysicsEngine();
        physics.AddLandblock(0xA9B4FFFFu, new TerrainSurface(new byte[81], new float[256]),
            [new CellSurface(0xA9B40100u, wall, [[0, 1, 2, 3]])], [], 0f, 0f);
        physics.AddLandblock(0xAAB4FFFFu, new TerrainSurface(new byte[81], new float[256]), [], [], 192f, 0f);
        physics.AddLandblock(0xA9B5FFFFu, new TerrainSurface(new byte[81], new float[256]), [], [], 0f, 192f);
        physics.AddLandblock(0xAAB5FFFFu, new TerrainSurface(new byte[81], new float[256]), [], [], 192f, 192f);
        return physics;
    }
```

(Cell triangles are world coordinates; a landblock's cells are captured whenever their extent overlaps the region, so one wall polygon in `0xA9B4` covers both northern landblocks.)

```csharp
    [Fact]
    public void AWalkWithNoWayRoundAnywhereLoadedEndsBeforeMovingAndSaysWhereItLooked()
    {
        var body = new SimulatedBody(new Vector3(60f, 150f, 0f));
        var walk = new NavigationWalkController(WallWorld(gapFromX: null), body, new Goals());

        walk.WalkToPlace(0xA9B50001u, new Vector3(60f, 260f, 0f));
        NavigationWalkReport report = RunUntilSettled(walk, body);

        Assert.Equal(NavigationWalkState.NoRoute, report.State);
        Assert.EndsWith("within 384 m around the start and goal", report.Reason);
        Assert.Equal(0, body.MovesBegun);
    }

    [Fact]
    public void AWalkThatFitsOneSquarePlansNoWayRound()
    {
        var body = new SimulatedBody(new Vector3(40f, 40f, 0f));
        var walk = new NavigationWalkController(FlatWorld(), body, new Goals { [Target] = new Vector3(60f, 75f, 0f) });

        walk.WalkTo(Target);

        Assert.Equal(NavigationWalkState.Arrived, RunUntilSettled(walk, body).State);
        Assert.Null(walk.Corridor);
    }

    [Fact]
    public void AWayRoundIsPlannedOverWhatIsLoadedAndHoldsTheCharacter()
    {
        var low = new Vector2(0f, 0f);
        var high = new Vector2(384f, 384f);

        Assert.True(NavigationWalkController.TryChooseCorridorRegion(new Vector3(60f, 150f, 0f), new Vector3(60f, 260f, 0f), low, high,
            out float x, out float y, out float size));
        Assert.Equal((0f, 0f, 384f), (x, y, size));

        var wide = new Vector2(4000f, 4000f);
        Assert.True(NavigationWalkController.TryChooseCorridorRegion(new Vector3(100f, 100f, 0f), new Vector3(3900f, 3900f, 0f), low, wide,
            out x, out y, out size));
        Assert.Equal(NavigationWalkController.MaximumCorridorRegion, size);
        Assert.InRange(100f - x, NavigationWalkController.RegionMargin, size - NavigationWalkController.RegionMargin);
        Assert.InRange(100f - y, NavigationWalkController.RegionMargin, size - NavigationWalkController.RegionMargin);
        Assert.True(x >= 0f && y >= 0f);
    }
```

- [ ] **Step 2: Run the tests to check they fail.** Expected: build error (`Corridor` and `TryChooseCorridorRegion` not defined).

- [ ] **Step 3: Create `NavigationWalkController.Corridor.cs`:**

```csharp
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
```

- [ ] **Step 4: Wire it into `NavigationWalkController.cs`:**

1. `Request` (nested class near the end of the file) gains:
```csharp
        /// <summary>Whether the walk plans a way round before it plans again.</summary>
        public bool WantsCorridor { get; set; }

        /// <summary>The way round the walk follows, and how many it has planned.</summary>
        public NavCorridor? Corridor { get; set; }
        public int CorridorPlans { get; set; }

        /// <summary>Stretches of earlier ways round that led nowhere, which the next one keeps out of.</summary>
        public List<NavAvoidance> CorridorKeepOut { get; } = [];
```
2. `IsSearching` becomes `_routing is not null || _building is not null || _corridoring is not null`. `SearchFinished` adds `|| _corridoring is { IsCompleted: true }`.
3. In `Tick`, after `CollectRoute();`, add `CollectCorridor();`.
4. In `Begin`, beside `Route = null;`, add `Corridor = null;`.
5. In `Advance`, change `if (_building is not null || _routing is not null)` to also check `|| _corridoring is not null`. Then, right after the `sample.Airborne` block and before `bool inDungeon = ...`, add:
```csharp
        if (active.WantsCorridor)
        {
            active.WantsCorridor = false;
            StartCorridor(active, sample);
            return;
        }
```
6. In `CollectRoute`, directly after the `ObjectsMoved` rebuild block (the one ending `Say(... again)` + `return;`) and before `if (route.Outcome != NavRouteOutcome.Routed) { if (requester.AvoidedDoor ...`, insert:
```csharp
        if (route.Outcome == NavRouteOutcome.NoPath && WantsCorridorAfter(requester))
        {
            // One square around the character and the goal holds no way there, but a way
            // round may leave the square: plan one over everything loaded before giving up.
            requester.WantsCorridor = true;
            requester.Builds = 0;
            string round = "no route within one square; planning a way round";
            Publish(requester, NavigationWalkState.Planning, round, float.NaN);
            Say($"Walk to {Label(requester)}: {round}");
            return;
        }
```
and add the helper beside `BlockedReason`:
```csharp
    /// <summary>
    /// Whether a walk that found no route in its one square plans a way round: a walk or
    /// route to a place or object outdoors, not yet on a way round, that has not met a door,
    /// an object or a stop that a way round would not get it past.
    /// </summary>
    private bool WantsCorridorAfter(Request request) =>
        !request.Follow
        && !request.Staged
        && request.Corridor is null
        && request.CorridorPlans == 0
        && request.AvoidedDoor is null
        && request.AvoidedObject is null
        && request.Replans == 0
        && _gridDungeon == 0u;
```

- [ ] **Step 5: Run the tests.** Run: `dotnet test tests/AcDream.App.Tests --filter "FullyQualifiedName~NavigationWalkControllerTests"`. Expected: PASS. If any existing test that expected a one-square `NoRoute` now sees the interim "not supported yet" ending, record its name. Task 4 must make it pass for real; do not change its assertions.

- [ ] **Step 6: Commit** (`feat(nav): plan a way round when one square holds no route`, with the trailer)

---

### Task 4: Walk the corridor in stages; corridor-first for far goals

**Goal:**
- A walk with a corridor walks it in fine stages aimed `StageLookahead` ahead, with a final normal search once `FinalStretch` or less remains.
- A goal beyond one square plans a corridor up front, replacing straight-aim staging.
- `TowardEdge` corridors roll forward.
- The stage budget comes from the corridor.
- `RemainingMeters` follows the corridor.

**Files:**
- Modify: `src/AcDream.Runtime/Navigation/NavigationWalkController.Corridor.cs` (add `AdvanceAlongCorridor`, `CorridorBeyond`; replace the interim line in `CollectCorridor`)
- Modify: `src/AcDream.Runtime/Navigation/NavigationWalkController.cs`:
  - `Advance` (~lines 957–995)
  - `SearchRoute` (~line 1003)
  - `StartDetour` (~line 1100)
  - `NextStage` (~line 1665)
  - `CollectRoute`'s `if (requester.Staged)` block (~line 2227)
  - `Remaining` (~line 2819)
  - `Request`
  - remove `ChooseStageRegion` (~line 808) and `ReachesToward` (~line 1789)
- Test: `tests/AcDream.App.Tests/Navigation/NavigationWalkControllerTests.cs` (remove `AStageRegionHoldsTheCharacterAndReachesTowardTheGoal`, which tested the removed `ChooseStageRegion`)

**Acceptance Criteria:**
- [ ] `WallWorld(gapFromX: 330f)`: `WalkToPlace` to (60, 260) arrives within 2.55 m of the goal, the body passed x > 320, and `walk.Corridor.Outcome == Reaches`.
- [ ] Existing `AGoalTooFarAwayForOneRegionIsWalkedToInStages` passes with `Replans == 0`.
- [ ] Existing `AWalkTowardAGoalBeyondTheLoadedWorldEndsWhereItCanGetNoNearer` passes: `NoRoute`, "no way on toward the goal", body Y > 150.
- [ ] In the gap world, the first `Walking` report has `RemainingMeters` within 15% of `walk.Corridor.Length`, and more than 1.5 × the straight distance (110 m).
- [ ] All `NavigationWalkControllerTests` and `NavigationWalkCorpusTests` pass.

**Verify:** `dotnet test tests/AcDream.App.Tests --filter "FullyQualifiedName~NavigationWalkControllerTests|FullyQualifiedName~NavigationWalkCorpusTests"` → all pass

**Steps:**

- [ ] **Step 1: Write the failing tests:**

```csharp
    [Fact]
    public void AWalkWhoseWayRoundLeavesItsSquareGoesRoundTheWallAndArrives()
    {
        var body = new SimulatedBody(new Vector3(60f, 150f, 0f));
        var walk = new NavigationWalkController(WallWorld(gapFromX: 330f), body, new Goals());

        walk.WalkToPlace(0xA9B50001u, new Vector3(60f, 260f, 0f));
        NavigationWalkReport first = RunUntil(walk, body, report => report.State == NavigationWalkState.Walking, seconds: 30f);
        NavCorridor corridor = Assert.IsType<NavCorridor>(walk.Corridor);
        NavigationWalkReport report = RunUntilSettled(walk, body, seconds: 400f);

        Assert.Equal(NavCorridorOutcome.Reaches, corridor.Outcome);
        Assert.InRange(first.RemainingMeters, corridor.Length * 0.85f, corridor.Length * 1.15f);
        Assert.True(first.RemainingMeters > 165f);
        Assert.Equal(NavigationWalkState.Arrived, report.State);
        Assert.InRange(Vector2.Distance(body.Flat, new Vector2(60f, 260f)), 0f, NavigationWalkController.DefaultArrivalMeters + 0.05f);
        Assert.Contains(body.Path, point => point.X > 320f);
    }
```

(`body.Path` is the `SimulatedBody`'s list of flat positions, already filled by `Integrate`.)

- [ ] **Step 2: Run it to check it fails.** Expected: `NoRoute` "following a way round is not supported yet".

- [ ] **Step 3: Add the `Request` members:**
```csharp
        /// <summary>The point of the way round nearest the character, and the one the stage under way aims at.</summary>
        public int CorridorIndex { get; set; }
        public int StageTargetIndex { get; set; }
        public Vector3? StageTarget { get; set; }

        /// <summary>How many stages the walk may take in all.</summary>
        public int StageBudget { get; set; } = MaximumStages;
```

- [ ] **Step 4: Replace the interim line in `CollectCorridor`** (the `// Task 4 replaces…` line and the `End` after it). First change the failure branch above it, so that a rolled way round starting at the loaded edge (where the coarse grid may find no start) reads as today's far-walk ending:
```csharp
        if (corridor.Outcome is not (NavCorridorOutcome.Reaches or NavCorridorOutcome.TowardEdge))
        {
            bool rolled = requester.Stages > 0 && _body.TrySample(out NavigationWalkBodySample rolledAt);
            End(requester, NavigationWalkState.NoRoute, rolled
                ? Inv($"no way on toward the goal was found; it is {HorizontalDistance(rolledAt.Position, requester.Goal):0} m away")
                : corridor.Reason);
            return;
        }
```
Then the replacement for the interim line:
```csharp
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
```

- [ ] **Step 5: Add `AdvanceAlongCorridor` and `CorridorBeyond` to the partial:**
```csharp
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
        if (usable is null || !usable.Contains(sample.Position, CoverMargin) || !usable.Contains(target, CoverMargin))
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

    /// <summary>What is left of the way round beyond the stage under way, and on to the goal past its end.</summary>
    private static float CorridorBeyond(Request request) =>
        request.Corridor is not { } corridor
            ? 0f
            : corridor.RemainingFrom(request.StageTargetIndex)
                + (corridor.Outcome == NavCorridorOutcome.TowardEdge ? HorizontalDistance(corridor.Points[^1], request.Goal) : 0f);
```

- [ ] **Step 6: Change `Advance`.** Replace everything from `bool staged = false;` to `SearchRoute(active, usable, sample, staged, detour: false);` with:
```csharp
        if (!inDungeon && active.Corridor is { } corridor)
        {
            AdvanceAlongCorridor(active, corridor, usable, sample);
            return;
        }
        if (usable is null || !usable.Contains(active.Goal, CoverMargin))
        {
            if (inDungeon)
            {
                // (keep the existing sealed-dungeon block here unchanged, including its return)
            }
            if (TryChooseRegion(sample.Position, active.Goal, out float originX, out float originY, out float size))
            {
                BuildGrid(active, sample, originX, originY, size, dungeon: 0u);
                return;
            }
            // Too far for one square: find the way round first, then walk it in stages.
            StartCorridor(active, sample);
            return;
        }

        SearchRoute(active, usable, sample, stageTarget: null, detour: false);
```
Move the existing dungeon block body verbatim into the `if (inDungeon)` branch.

- [ ] **Step 7: Change `SearchRoute`.** Its signature becomes `private void SearchRoute(Request active, NavGrid grid, in NavigationWalkBodySample sample, Vector3? stageTarget, bool detour)`. At the top: `bool staged = stageTarget is not null;` and `Vector3 to = stageTarget ?? active.Goal;`. After `active.Staged = staged;`, add `active.StageTarget = stageTarget;`. Replace the staged branch of the `_routing =` chain:
```csharp
        _routing = staged
            ? Task.Run(() => AroundObstacles(to, avoid, obstacles, spots => NavRouter.Find(grid, from, to, NavCorridor.StageArrival, spots, leaps, crowd)))
            : onto is not null
            // … the rest unchanged
```
In `StartDetour`, the call becomes `SearchRoute(active, grid, sample, active.Staged ? active.StageTarget : null, detour: true);`. Its guard `(!active.Staged && !grid.Contains(active.Goal, CoverMargin))` becomes `!grid.Contains(active.StageTarget ?? active.Goal, CoverMargin)`.

- [ ] **Step 8: Change the staged block in `CollectRoute`** (`if (requester.Staged) { float left = …`). Replace the whole block with:
```csharp
        if (requester.Staged && requester.StageTarget is { } aimed)
        {
            if (HorizontalDistance(route.Legs[^1], aimed) > NavCorridor.StageMiss)
            {
                End(requester, NavigationWalkState.NoRoute, $"no way along the planned way round was found near {Point(aimed)}");
                return;
            }
            if (!requester.Walk)
            {
                End(
                    requester,
                    NavigationWalkState.Planned,
                    Inv($"a route was found for the first {route.Length:0} m; the rest is planned on the way"));
                return;
            }
            _driver = Drive(requester, new RuntimeRouteDriver(route.Legs, LeapsOf(route), canCutAlong: CornerCuts(), aimLeapFrom: AimLeapFrom, aimOnward: AimOnward, sameFloor: SameFloor));
            Publish(requester, NavigationWalkState.Walking, "walking", route.Length + CorridorBeyond(requester));
            return;
        }
```
For a **final** stage (non-staged) walked on a corridor, the existing non-staged code drives it; nothing changes there.

- [ ] **Step 9: Change `NextStage`.** Replace `if (active.Stages >= MaximumStages)` with `if (active.Stages >= active.StageBudget)`. After `active.Staged = false;`, add `active.StageTarget = null;`. Before the `string next = …` line, add the roll-forward:
```csharp
        if (active.Corridor is { Outcome: NavCorridorOutcome.TowardEdge } edge && active.StageTargetIndex >= edge.Points.Count - 1)
        {
            // At the end of a way round toward a goal beyond what was loaded: plan the next
            // one from here, over what has loaded since.
            active.Corridor = null;
            active.CorridorPlans = 0;
            active.CorridorKeepOut.Clear();
        }
```

- [ ] **Step 10: Change `Remaining`.** Its return becomes:
```csharp
        return request.Staged
            ? remaining + (request.Corridor is not null
                ? CorridorBeyond(request)
                : Vector2.Distance(at, new Vector2(request.Goal.X, request.Goal.Y)))
            : remaining;
```

- [ ] **Step 11: Delete** `ChooseStageRegion` and `ReachesToward` from the controller, and the test `AStageRegionHoldsTheCharacterAndReachesTowardTheGoal`. Then `grep -n "ChooseStageRegion\|ReachesToward" -r src tests` should find nothing.

- [ ] **Step 12: Run** `dotnet test tests/AcDream.App.Tests --filter "FullyQualifiedName~NavigationWalkControllerTests|FullyQualifiedName~NavigationWalkCorpusTests"`. Expected: PASS. If `AWalkTowardAGoalBeyondTheLoadedWorldEndsWhereItCanGetNoNearer` ends with a different reason, check that the roll-forward's second corridor hits the `MinimumStageProgress` check in `CollectCorridor`. The test's assertions stay as they are.

- [ ] **Step 13: Commit** (`feat(nav): walk a way round in stages and plan one first for far goals`, with the trailer)

---

### Task 5: A missed stage keeps out of that stretch and plans another way round

**Goal:** When a stage (including the final one) of a corridor walk finds no route to its point, or ends more than `StageMiss` short of it, the walk keeps out of that stretch and plans the corridor again, up to `MaximumCorridorPlans`. After that it ends `NoRoute`, naming the spot and the area.

**Files:**
- Modify: `src/AcDream.Runtime/Navigation/NavigationWalkController.Corridor.cs` (add `StageMissed` and the `CorridorPlanned` test hook; apply the hook in `CollectCorridor`)
- Modify: `src/AcDream.Runtime/Navigation/NavigationWalkController.cs` (`CollectRoute`: route the `StageMiss` end and corridor `NoPath` through `StageMissed`)
- Test: `tests/AcDream.App.Tests/Navigation/NavigationWalkControllerTests.cs`

**Acceptance Criteria:**
- [ ] With a hook that swaps only the first corridor for a straight line through the wall, the walk in `WallWorld(330f)` arrives, and a chat line contains `planning another way round`.
- [ ] With a hook that always swaps in the straight line, the walk ends `NoRoute`, with a reason starting `no way along the planned way round was found near` and containing `searched 384 m around the start and goal`, after exactly `MaximumCorridorPlans` corridor plans.

**Verify:** `dotnet test tests/AcDream.App.Tests --filter "FullyQualifiedName~NavigationWalkControllerTests"` → all pass

**Steps:**

- [ ] **Step 1: Write the failing tests:**
```csharp
    /// <summary>A way round straight from the start to the goal, as a coarse grid that missed a wall would plan it.</summary>
    private static NavCorridor StraightThroughTheWall(NavCorridor planned)
    {
        var points = new List<Vector3>();
        for (float y = 150f; y <= 260f; y += 1f)
            points.Add(new Vector3(60f, y, 0f));
        return new NavCorridor(NavCorridorOutcome.Reaches, "routed", points, planned.OriginX, planned.OriginY, planned.Size, planned.CellSize, 0d);
    }

    [Fact]
    public void AWayRoundThatLeadsNowhereIsKeptOutOfAndAnotherIsPlanned()
    {
        var said = new List<string>();
        var body = new SimulatedBody(new Vector3(60f, 150f, 0f));
        var walk = new NavigationWalkController(WallWorld(gapFromX: 330f), body, new Goals(), said.Add);
        int plans = 0;
        walk.CorridorPlanned = planned => plans++ == 0 ? StraightThroughTheWall(planned) : planned;

        walk.WalkToPlace(0xA9B50001u, new Vector3(60f, 260f, 0f));
        NavigationWalkReport report = RunUntilSettled(walk, body, seconds: 400f);

        Assert.Equal(NavigationWalkState.Arrived, report.State);
        Assert.Contains(said, line => line.Contains("planning another way round"));
        Assert.Equal(2, plans);
    }

    [Fact]
    public void AWalkGivesUpAfterEveryWayRoundLeadsNowhere()
    {
        var body = new SimulatedBody(new Vector3(60f, 150f, 0f));
        var walk = new NavigationWalkController(WallWorld(gapFromX: 330f), body, new Goals());
        int plans = 0;
        walk.CorridorPlanned = planned => { plans++; return StraightThroughTheWall(planned); };

        walk.WalkToPlace(0xA9B50001u, new Vector3(60f, 260f, 0f));
        NavigationWalkReport report = RunUntilSettled(walk, body, seconds: 400f);

        Assert.Equal(NavigationWalkState.NoRoute, report.State);
        Assert.StartsWith("no way along the planned way round was found near", report.Reason);
        Assert.Contains("searched 384 m around the start and goal", report.Reason);
        Assert.Equal(NavigationWalkController.MaximumCorridorPlans, plans);
    }
```
(Check which constructor parameter takes the `said` callback by looking at an existing test that passes `said.Add`, e.g. ~line 297, and match it.)

- [ ] **Step 2: Run them to check they fail.** Expected: build error (`CorridorPlanned` not defined).

- [ ] **Step 3: Add the hook and `StageMissed` to the partial:**
```csharp
    /// <summary>Lets a test stand in for the way round a search found. Never set outside tests.</summary>
    internal Func<NavCorridor, NavCorridor>? CorridorPlanned { get; set; }

    /// <summary>
    /// A stage of the way round found no route to its point, or ended short of it: the coarse
    /// grid saw a way the fine one does not, such as a gap narrower than a body. Keeps the next
    /// way round out of the middle of that stretch and plans one, or, after
    /// <see cref="MaximumCorridorPlans"/>, gives up.
    /// </summary>
    private void StageMissed(Request active, in NavigationWalkBodySample sample)
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
            End(active, NavigationWalkState.NoRoute,
                Inv($"no way along the planned way round was found near {Point(near)}; searched {corridor.Size:0} m around the start and goal"));
            return;
        }
        active.CorridorKeepOut.Add(new NavAvoidance(near, radius));
        active.Corridor = null;
        active.WantsCorridor = true;
        string again = $"no way along the planned way round was found near {Point(near)}; planning another way round";
        Publish(active, NavigationWalkState.Planning, again, float.NaN);
        Say($"Walk to {Label(active)}: {again}");
    }
```
In `CollectCorridor`, change `NavCorridor corridor = planning.Result;` to `NavCorridor corridor = CorridorPlanned?.Invoke(planning.Result) ?? planning.Result;`.

- [ ] **Step 4: Route misses through it in `CollectRoute`:**
1. In the staged block from Task 4, replace the `End(… "no way along the planned way round was found near …")` with:
```csharp
                if (_body.TrySample(out NavigationWalkBodySample missedAt))
                    StageMissed(requester, missedAt);
                return;
```
2. Directly after the escalation block from Task 3 (`WantsCorridorAfter`), insert:
```csharp
        if (route.Outcome != NavRouteOutcome.Routed
            && requester.Corridor is not null
            && requester.AvoidedDoor is null
            && requester.AvoidedObject is null
            && requester.Replans == 0
            && _body.TrySample(out NavigationWalkBodySample stuckAt))
        {
            StageMissed(requester, stuckAt);
            return;
        }
```

- [ ] **Step 5: Run** `dotnet test tests/AcDream.App.Tests --filter "FullyQualifiedName~NavigationWalkControllerTests"`. Expected: PASS.

- [ ] **Step 6: Commit** (`feat(nav): keep out of a way round that leads nowhere and plan another`, with the trailer)

---

### Task 6: Preview stitches the stages along a corridor

**Goal:**
- An outdoor `PreviewPathAsync` whose one square fails with `NoPath`, or whose goal is beyond one square, plans a corridor and stitches fine stages on its worker.
- A goal beyond the loaded area returns `NoRoute` "the destination is beyond the loaded area, N m away".
- Dungeon previews are unchanged.

**Files:**
- Modify: `src/AcDream.Runtime/Navigation/NavigationWalkController.Preview.cs:25-105`
- Test: `tests/AcDream.App.Tests/Navigation/NavigationWalkControllerTests.cs`

**Acceptance Criteria:**
- [ ] Preview in `WallWorld(330f)` to (60, 260): `Routed`, last point within 2.5 m of the goal, and `LengthMeters` > 500.
- [ ] Preview in `WallWorld(null)`: `NoRoute`, with a reason ending `within 384 m around the start and goal`.
- [ ] Preview in `FlatWorld()` to (40, 900): `NoRoute`, with a reason starting `the destination is beyond the loaded area`.
- [ ] The existing preview tests (`PreviewInsideDungeon…`, `PreviewOfUnreachableFloor…`, `PreviewDoesNotReplace…`) pass.

**Verify:** `dotnet test tests/AcDream.App.Tests --filter "FullyQualifiedName~NavigationWalkControllerTests.Preview"` → all pass

**Steps:**

- [ ] **Step 1: Write the failing tests:**
```csharp
    [Fact]
    public async Task PreviewOfAWayRoundStitchesItsStages()
    {
        var body = new SimulatedBody(new Vector3(60f, 150f, 0f));
        var walk = new NavigationWalkController(WallWorld(gapFromX: 330f), body, new Goals());

        PluginNavigationPlan plan = await walk.PreviewPathAsync(0xA9B50001u, new Vector3(60f, 260f, 0f), 2.5f);

        Assert.Equal(PluginNavigationPlanStatus.Routed, plan.Status);
        Assert.True(plan.LengthMeters > 500f, $"length {plan.LengthMeters}");
        Assert.Equal(0, walk.Report.Sequence);
    }

    [Fact]
    public async Task PreviewWithNoWayRoundSaysWhereItLooked()
    {
        var body = new SimulatedBody(new Vector3(60f, 150f, 0f));
        var walk = new NavigationWalkController(WallWorld(gapFromX: null), body, new Goals());

        PluginNavigationPlan plan = await walk.PreviewPathAsync(0xA9B50001u, new Vector3(60f, 260f, 0f), 2.5f);

        Assert.Equal(PluginNavigationPlanStatus.NoRoute, plan.Status);
        Assert.EndsWith("within 384 m around the start and goal", plan.Reason);
    }

    [Fact]
    public async Task PreviewBeyondTheLoadedAreaSaysSo()
    {
        var body = new SimulatedBody(new Vector3(40f, 40f, 0f));
        var walk = new NavigationWalkController(FlatWorld(), body, new Goals());

        PluginNavigationPlan plan = await walk.PreviewPathAsync(0xA9B40001u, new Vector3(40f, 900f, 0f), 2.5f);

        Assert.Equal(PluginNavigationPlanStatus.NoRoute, plan.Status);
        Assert.StartsWith("the destination is beyond the loaded area", plan.Reason);
    }
```
(The place-preview overload `PreviewPathAsync(uint cellId, Vector3 local, float arrival)` is the one `PreviewOfUnreachableFloorReturnsNoRoute` uses.)

- [ ] **Step 2: Run them to check they fail.** Expected: the first and third return `NoRoute` "the destination is outside one planning region"; the second returns the plain router reason.

- [ ] **Step 3: Restructure the outdoor path of `PreviewPathAsync`.** Keep the dungeon branch exactly as it is. For the outdoor branch (`!dungeon`):
  1. Compute `bool square = TryChooseRegion(sample.Position, goal, out originX, out originY, out size);`.
  2. Measure the resident extent and choose the corridor region on the calling thread:
```csharp
        bool haveRound = NavGeometry.TryMeasureResident(_physics, out Vector2 residentLow, out Vector2 residentHigh)
            && TryChooseCorridorRegion(sample.Position, goal, residentLow, residentHigh, out roundX, out roundY, out roundSize);
        NavGeometry? round = haveRound ? NavGeometry.Capture(_physics, roundX, roundY, roundSize, _goals.StandsStill) : null;
```
  3. If `!square && round is null`, return `NoRoute` "no collision is loaded around the character".
  4. Gather `portals` and `obstacles` over the **corridor** square when `round` is set (centre and reach from `roundX/roundY/roundSize`), else over the one square, using the same `FindPortals`/`FindObstacles` calls as today. For `FindObstacles`, pass `round?.ObjectIds ?? geometry.ObjectIds`.
  5. Capture the one-square `geometry` only when `square`. Keep the `cells` list built from the union of both geometries' `LandblockIds`.
  6. The worker body becomes:
```csharp
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
                    NavCorridor corridor = NavCorridor.Plan(NavGrid.Build(round, body, CorridorCellSize(roundSize)), start, goal,
                        PlanningRadius(arrivalMeters), onGoalFloor);
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
```
  Split the method so the dungeon path keeps its current body: extract `PreviewDungeonAsync(...)`, holding today's code with `dungeon == true`, and `PreviewOutdoorAsync(...)`. `PreviewPathAsync` dispatches on `TryMeasureDungeon`. `Inv` and `HorizontalDistance` are existing static helpers on the controller.

- [ ] **Step 4: Run** `dotnet test tests/AcDream.App.Tests --filter "FullyQualifiedName~NavigationWalkControllerTests"`. Expected: PASS.

- [ ] **Step 5: Commit** (`feat(nav): preview a way round as one path of stitched stages`, with the trailer)

---

### Task 7: Draw the corridor in the debug overlay

**Goal:** `/nav route` and the walk overlay draw the corridor as a fainter line under the fine route. (The narration lines were added in Tasks 3–4.)

**Files:**
- Modify: `src/AcDream.App/Rendering/NavMeshDebugOverlay.cs` (colour constants ~line 38; `DrawRoute` ~line 116)

**Acceptance Criteria:**
- [ ] With `_walk.Corridor` set, `DrawRoute` adds the corridor lines (every 4th point plus the last) in `CorridorColour`, before the route legs, and still draws them when `_walk.Route` is null.
- [ ] `dotnet build src/AcDream.App` succeeds with no new warnings.

**Verify:** `dotnet build src/AcDream.App -v q 2>&1 | grep -E "error|Warn|Build succeeded"` → `Build succeeded`

**Steps:**

- [ ] **Step 1: Add the constants** beside `BeyondColour`:
```csharp
    private static readonly Vector3 CorridorColour = new(0.55f, 0.35f, 0.75f);
    private static readonly Vector3 CorridorLift = new(0f, 0f, 0.3f);
    private const int CorridorStride = 4;
```

- [ ] **Step 2: Draw the corridor at the start of `DrawRoute`**, after the goal cylinder and before the `_walk.Route` early return:
```csharp
        if (_walk.Corridor is { Points.Count: > 1 } corridor)
        {
            Vector3 previous = corridor.Points[0];
            for (int index = CorridorStride; index < corridor.Points.Count + CorridorStride - 1; index += CorridorStride)
            {
                Vector3 point = corridor.Points[Math.Min(index, corridor.Points.Count - 1)];
                lines.AddLine(previous + CorridorLift, point + CorridorLift, CorridorColour, hiddenByScene: true);
                previous = point;
            }
        }
```

- [ ] **Step 3: Build.** Run: `dotnet build src/AcDream.App -v q`. Expected: Build succeeded.

- [ ] **Step 4: Commit** (`feat(nav): draw the way round under the route`, with the trailer)

---

### Task 8: Installed-dat tests for the two live walks; delete the spike

**Goal:** Lock the spike's findings into `Lane=InstalledDat` tests: Stonehold arrives along a corridor of stitched stages; the plateau has no walking corridor and names the area.

**Files:**
- Create: `tests/AcDream.App.Tests/Navigation/WalkCorridorInstalledDatTests.cs`
- Delete (main checkout, untracked): `/Users/davidsmith/code/OpenAC/OpenAC/tests/AcDream.App.Tests/Navigation/DetourSpikeInstalledDatTests.cs`

**Acceptance Criteria:**
- [ ] Stonehold (7×7 landblocks `0x5B..0x61` × `0xD4..0xDA`, base `0x5BD4`): the corridor at 1 m over 1,344 m is `Reaches`; `Stitch` returns `Routed`, ending within 2.5 m of the goal and on its floor (Z within 1.2 m of the goal's terrain height).
- [ ] Plateau (7×7 `0x45..0x4B` × `0xB4..0xBA`, base `0x45B4`): the corridor is `NoPath`, with a reason ending `within 1344 m around the start and goal`.
- [ ] The spike file is gone from the main checkout.

**Verify:** `ACDREAM_DAT_DIR=~/AsheronsCall dotnet test tests/AcDream.App.Tests --filter "FullyQualifiedName~WalkCorridorInstalledDatTests"` → 2 passed

**Steps:**

- [ ] **Step 1: Write the tests** (they should pass immediately: Tasks 1–2 already exist. Run them once with a deliberately wrong goal to see them fail, then restore):
```csharp
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

        NavCorridor corridor = NavCorridor.Plan(NavGrid.Build(geometry, world.Body, 1f), from, goal, 2f, arriveOnGoalFloor: true);
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

        NavCorridor corridor = NavCorridor.Plan(NavGrid.Build(geometry, world.Body, 1f), from, goal, 2f, arriveOnGoalFloor: true);

        Assert.Equal(NavCorridorOutcome.NoPath, corridor.Outcome);
        Assert.EndsWith("within 1344 m around the start and goal", corridor.Reason);
    }
}
```

- [ ] **Step 2: Run** `ACDREAM_DAT_DIR=~/AsheronsCall dotnet test tests/AcDream.App.Tests --filter "FullyQualifiedName~WalkCorridorInstalledDatTests" --logger "console;verbosity=detailed"`. Expected: 2 passed. Record the Stonehold corridor and stitch timings from the output for Task 9's docs.

- [ ] **Step 3: Delete the spike** from the main checkout: `rm /Users/davidsmith/code/OpenAC/OpenAC/tests/AcDream.App.Tests/Navigation/DetourSpikeInstalledDatTests.cs`. It was never committed.

- [ ] **Step 4: Commit** (`test(nav): the api-gaps #72 walks over the installed game files`, with the trailer)

---

### Task 9: Documentation and Aethrix api-gaps #72

**Goal:** `docs/navigation.md` describes corridors as built, and Aethrix's api-gaps #72 is marked resolved with the plateau finding.

**Files:**
- Modify: `docs/navigation.md`:
  - "What a walk does" table, "Far goals" row
  - "Path previews" paragraph (the 320 m sentence)
  - "The grid and the router" table (add a `NavCorridor` row) plus a new "### Corridors" subsection after that table
  - "Known limits"
- Modify: `/Users/davidsmith/code/OpenAC/OpenAC-Plugins-Aethrix/docs/api-gaps.md` (entry #72)

**Acceptance Criteria:**
- [ ] The "Far goals" row describes the corridor policy: a way round over the loaded area at 0.5–1 m, stages about 120 m along it, rolling for goals beyond the loaded area, up to 3 ways round, and a stage budget from the way round's length.
- [ ] The preview text no longer says beyond 320 m returns `NoRoute`. It says a preview covers the loaded area and stitches stages.
- [ ] Known limits gains two bullets: ways round are walked only, so a way up that needs jumps is not found (the plateau at 45.0N 43.1W); and a way round covers only what is loaded.
- [ ] The Corridors subsection records the Release-build timing measured in Task 8 or Task 10.
- [ ] api-gaps #72 is marked resolved: fork branch `feat/walk-corridor`, what changed, and that the plateau has no walking way up under `FloorZ`, so their scanner's slope rule is looser than the client's.

**Verify:** `grep -n "way round" docs/navigation.md | head` → shows the new rows; `grep -n "#72" /Users/davidsmith/code/OpenAC/OpenAC-Plugins-Aethrix/docs/api-gaps.md` → resolved entry

**Steps:**

- [ ] **Step 1: Edit `docs/navigation.md`** in the doc's own voice: plain sentences, tables. The "Far goals" row becomes:
```markdown
| Far goals | A goal beyond one square, or one whose square holds no way there, is walked along a way round: a walked route over a coarse grid of everything loaded (0.5 m cells up to 768 m a side, 1 m up to 1,536 m), planned off the update thread while the walk shows `Planning`. Each stage is planned over its own fine square toward the point about 120 m further along it, and the last straight to the goal once no more than 144 m is left. A goal beyond what is loaded gets a way round to the loaded edge, planned again from there. A stage that cannot follow its way round keeps the next way round out of that stretch, up to 3 ways round; a walk with none ends `NoRoute`, naming how far round it looked. |
```
Add the `NavCorridor` row to the planning types table:
```markdown
| `NavCorridor` | A walked way round over a coarse grid: `Plan` finds one onto the goal's floor, or to the grid's edge toward a goal beyond it; `Stitch` plans the fine stages along it and joins them, for previews. |
```
Add a "### Corridors" subsection, 3–5 sentences: why (one square can't hold a way round), the coarse/fine split, walking only, the measured timings, and the RynthSuite-style baked-mesh alternative with when to revisit it (from the spec's Decision section).

- [ ] **Step 2: Edit the Aethrix api-gaps #72 entry.** Read the file first and follow the format of entries already marked resolved (e.g. #62 or #52).

- [ ] **Step 3: Commit both repos.**
```bash
git add docs/navigation.md && git commit -m "docs(nav): ways round for walks beyond one square

Claude-Session: https://claude.ai/code/session_01A2zB2pRyNmj41iT6MFGZ2Z"
git -C /Users/davidsmith/code/OpenAC/OpenAC-Plugins-Aethrix add docs/api-gaps.md
git -C /Users/davidsmith/code/OpenAC/OpenAC-Plugins-Aethrix commit -m "docs: api-gaps #72 resolved by the client's ways round

Claude-Session: https://claude.ai/code/session_01A2zB2pRyNmj41iT6MFGZ2Z"
```
(Aethrix may have unrelated uncommitted edits. Stage only `docs/api-gaps.md`, and check `git -C … diff --cached --stat` shows that one file.)

---

### Task 10: Full gate and live walk check

**Goal:** All test lanes are green, and both api-gaps walks plus Aethrix's walk-probe checks behave on the live dev server.

> **USER-ORDERED GATE — NON-SKIPPABLE.** This task was requested by the user in the current conversation. It MUST NOT be closed by walking around it, by declaring it "verified inline", or by substituting a cheaper check. Close only after every item in `acceptanceCriteria` has been re-validated independently, with output captured.

**Files:** none (verification only)

**Acceptance Criteria:**
- [ ] `dotnet test tests/AcDream.Core.Tests` and `dotnet test tests/AcDream.App.Tests` (default lanes) pass, apart from failures already present on `main` at 7b6641cb. List any by name, with proof they fail on main too.
- [ ] `ACDREAM_DAT_DIR=~/AsheronsCall dotnet test tests/AcDream.App.Tests --filter "Lane=InstalledDat"` passes (same exception rule).
- [ ] `dotnet test tests/AcDream.Runtime.Tests tests/AcDream.Headless.Tests` pass (same exception rule).
- [ ] Live on 10.10.20.20 (dev client via `~/OpenAC-dev/dev-client.env`, never Dreamweave), with `/nav debug file` on:
  - [ ] `@teleloc 0x5ED7000A [31.0 30.9 79.7] 1 0 0 0`, then a walk to 69.60N 25.56W ends `Arrived` on the plateau.
  - [ ] `@teleloc 0x48B8001F [92.1 151.9 105.2] 1 0 0 0`, then a walk to 45.04N 43.07W ends `NoRoute`, with the reason containing `around the start and goal`.
  - [ ] Aethrix `docs/superpowers/live-checks/2026-10-17-walk-probe.md` checks 1–5 give the same or better results than the guide's recorded ones. Arwic → Eastham arrives without a resend.
  - [ ] One Release-build corridor timing (from the `Way round: … in N ms` narration line) is recorded in `docs/navigation.md`.

**Verify:** test commands above, then the live log `logs/aethrix/GolemTest/_RabidDee/<date>.log` lines for each walk

```json:metadata
{"userGate": true, "tags": ["user-gate"], "gateScope": "live walks on the dev server", "failurePolicy": "stop and report"}
```

**Steps:**

- [ ] **Step 1:** Run the four test commands and capture their summaries. For any failure, check out `main` in a scratch worktree and run that test there to show it's pre-existing.
- [ ] **Step 2:** Build the dev client in Release, start it against 10.10.20.20 per memory `local-ace-test-server`, and run the walks with `/nav debug file ~/walk-corridor-live.log`. The user may need to drive the client: ask them to run the `@teleloc` and `/nav go to` commands if the session can't send chat.
- [ ] **Step 3:** Record the results (state, reason, time per walk) in the task's close note. Add the Release timing to `docs/navigation.md` and commit.
- [ ] **Step 4:** Use superpowers-extended-cc:finishing-a-development-branch: merge `feat/walk-corridor` into fork `main` with a merge commit. Don't push until the user says so. Ask about an upstream PR then.
