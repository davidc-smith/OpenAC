# RynthAi Navigation and Nav3D (RynthAi sub-project #4) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers-extended-cc:subagent-driven-development (recommended) or superpowers-extended-cc:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** RynthAi's navigation walks its routes in OpenAC (autorun, heading servo, held motions, stop, jumps, door state) and its Nav3D rings, lines and labels draw in the world, depth-tested, with walls that fade toward the top like RynthCore's.

**Architecture:**
- Task 1 adds per-corner colours to the fork's `PluginWorldTriangle` (contract `0.1.25-fork.8`) so faded walls are possible.
- Task 2 adds three small runtime seams: `SetAutoRun`, `SnapHeading` on the player's movement state, and a public cell-local → `PluginNavigationPosition` helper.
- Task 3 binds the movement and door slots: `IRynthGameView` grows members, `RuntimeRynthGameView` implements them over `GameRuntime` / `IAutomationSurface`, a host-side `RynthMotionHolds` keeps RynthCore's held-key model and turns it into a `PluginMovementIntent`, and a new `RynthApiTable.Movement.cs` partial only marshals and catches.
- Task 4 adds `RynthNav3D` (per-tick buffer, RynthCore's caps, the landblock-anchored commit, ring/line tessellation) behind a `RynthApiTable.Nav3D.cs` partial, drawing into one `IPluginWorldTriangleLayer` reached through a new `ClientExtensionContext.WorldTriangles`.
- Task 5 is the live check, run with the user.

**Tech Stack:** .NET 10 (`~/.dotnet/dotnet`), xunit v3 (VSTest runner: `--filter "FullyQualifiedName~X"`), RynthCore/RynthSuite submodules (read only).

**Spec:** `docs/superpowers/specs/2026-10-18-rynth-ai-nav-design.md` (approved 2026-10-18). RynthAi split: #1 dashboard (done), #2 state + actions (done), #3 combat + buffs (done), **#4 this plan**, #5 loot/salvage/vendor/trade, #6 dat raycast + pathfinder.

## Global Constraints

- Repo `~/code/OpenAC/OpenAC` (the fork, `davidc-smith/OpenAC`). Work on branch `feat/rynth-ai-nav` (already created from `main`, holds the spec commit). Do not push.
- Use `~/.dotnet/dotnet` (the system `dotnet` lacks SDK 10.0.300).
- **Never edit, copy or move anything inside `external/RynthCore` or `external/RynthSuite`.** `git status --porcelain external` must stay empty.
- `AcDream.Core`, `AcDream.Runtime`, `AcDream.App`, `AcDream.ClientExtensions`, `AcDream.Plugin.Abstractions` and `AcDream.Rynth.Host` build with warnings as errors and analyzers on. New code must build clean.
- **Table rule:** every `[UnmanagedCallersOnly]` table function null-checks its pointers, catches every exception, logs through `Fail`, and returns RynthCore's "not available" value (0, or nothing for a void slot). An exception crossing that boundary ends the process.
- `RynthApiTable.ApiVersion` stays **86**.
- **Plugin contract:** only Task 1 changes `AcDream.Plugin.Abstractions` (additive: `ColorB`/`ColorC`). `ForkContractVersion` goes from `0.1.25-fork.7` to `0.1.25-fork.8` in that task, nowhere else.
- **Slots left unset after this plan:** `NativeAttackFn`, `StopMovementFn`, `DoMovementFn`, `SetAutonomyLevelFn`, `WorldToScreenFn`, `GetViewportSizeFn`, `GetRadarRectFn`, `SetRadarSuppressedFn`, `GetPluginInterfaceFn`, `IsPlayerReadyFn`, `GetScreenModeFn`, `MoveItemExternalFn`, `GetObjectPalettesFn`, `GetNumContainedItemsFn`, `GetNumContainedContainersFn`.
- All table functions run on the game update thread (`GameWindow.OnUpdate` → `ClientExtensionHost.Tick` → `RynthHost.Tick`). No locking, no queues.
- Rynth host tests run without parallelism (assembly attribute in `tests/AcDream.Rynth.Host.Tests/FakeGameView.cs`). App tests that touch Rynth use `[Collection("Rynth")]`. Only one `RynthApiTable` can be live at a time, so each test disposes its table or host.
- Known unrelated failures exist in `AcDream.App.Tests` (installed dats, offscreen Vulkan). Run App tests with each task's filter. `AcDream.Rynth.Host.Tests` must be fully green.
- Commit messages end with the line `Claude-Session: https://claude.ai/code/session_01X3ryhqAVUCdtME4PCQLPJi`.
- Never launch a client without asking the user first. Only the local ACE server 10.10.20.20, never Dreamweave. Never print passwords (`~/OpenAC-dev/dev-client.env`).

**User decisions (already made):**
- Scope A: "movement slots (autorun, turn, SetMotion holds, StopCompletely, JumpNonAutonomous and the jump trio), door state (GetObjectState and GetObjectMotionOn), and Nav3D drawn by OpenAC. Leave out WorldToScreen, viewport and radar." Detours / GetPluginInterface → #6.
- TurnToHeading: "snap, like RynthCore".
- Nav3D: "a small depth-tested pass", reusing the fork's tested world-triangle work ("Could we reuse our tested work there?").
- Ring/line wall fade: "Option 1" — per-corner colours in the plugin API (contract fork.8), not stacked bands.
- Design sections 1 and 2 approved ("Yes"); spec approved ("Proceed").
- Standing rule: "we cant make changes to their code, and we need to be able to pull in their latest daily changes".

## Reference: RynthCore facts used below

- Motion ids: WalkForward `0x45000005`, WalkBackward `0x45000006`, RunForward `0x41000003`, TurnRight `0x6500000D`, TurnLeft `0x6500000E`, SidestepRight `0x6500000F`, SidestepLeft `0x65000010`.
- Slot signatures (`external/RynthCore/src/RynthCore.Engine/Plugins/PluginContract.cs`):
  `int SetAutoRun(int)`, `int TurnToHeading(float)`, `int SetMotion(uint, int)`, `int StopCompletely()`, `int JumpNonAutonomous(float)`, `int TapJump()`, `int CommenceJump()`, `int DoJump(int)`, `int LaunchJumpWithMotion(int shift, int w, int x, int z, int c)`, `int GetObjectState(uint, uint*)`, `int GetObjectMotionOn(uint, int*)`, `void Nav3DClear()`, `void Nav3DAddRing(float wx, float wy, float wz, float radius, float thickness, uint argb)`, `void Nav3DAddRingEx(float wx, float wy, float wz, float radius, float thickness, float height, uint argb)`, `void Nav3DAddLine(float x1, float y1, float z1, float x2, float y2, float z2, float thickness, uint argb)`, `void Nav3DAddTriangle(float x1..z3 (9 floats), uint argb)`.
- SDK wrappers used by tests (`external/RynthCore/src/RynthCore.PluginSdk/RynthCoreHost.cs`): `SetAutoRun(bool)`, `TurnToHeading(float)`, `SetMotion(uint, bool)`, `StopCompletely()`, `JumpNonAutonomous(float)`, `TapJump()`, `CommenceJump()`, `DoJump(bool)`, `LaunchJumpWithMotion(bool shift, bool w, bool x, bool z, bool c)`, `TryGetObjectState(uint, out uint)`, `TryGetObjectMotionOn(uint, out bool)`, `Nav3DClear()`, `Nav3DAddRing(...)`, `Nav3DAddRingEx(...)`, `Nav3DAddLine(...)`, `Nav3DAddTriangle(...)`, and `Has*` for each.
- Nav3D (`D3D9/Nav3DRenderer.cs`, `ImGui/DX9Backend.cs`): caps 256 rings / 512 lines / 8192 triangles; `AddRing` wall height 0.5; ring = 32 segments, wall height `max(height, 0.1)`, rim annulus inner radius `max(0.01, radius - thickness)`; line = ground quad `±thickness` perpendicular in XZ (skipped when XZ length < 0.001) plus a 0.15 m wall; top colour `(c & 0x00FFFFFF) | 0x10000000`. `ClearFrame` records the player landblock (`cell >> 16`); `CommitFrame` drops the tick if the player's landblock is now a different non-zero one. Frame: X east m, Y up, Z north m, relative to the player's landblock.

---

### Task 1: Per-corner colours for world triangles (contract fork.8)

**Goal:** A `PluginWorldTriangle` can give corners B and C their own colours; the renderer passes a colour per vertex, and an unset corner uses `Color` exactly as today.

**Files:**
- Modify: `src/AcDream.Plugin.Abstractions/WorldTriangles.cs` (the `PluginWorldTriangle` record, lines 1-18)
- Modify: `src/AcDream.App/Rendering/PluginWorldTriangleRenderer.cs:57-66,101-103,160`
- Modify: `src/AcDream.App/Rendering/GroundShapeBatch.cs:88-96`
- Modify: `src/AcDream.Plugin.Abstractions/AcDream.Plugin.Abstractions.csproj:39`
- Modify: `docs/plugin-api.md` (the WorldTriangles section, ~line 1480)
- Test: `tests/AcDream.App.Tests/Rendering/PluginWorldTriangleRendererTests.cs`

**Acceptance Criteria:**
- [ ] A triangle with `ColorB = Green, ColorC = Blue` uploads vertex 0 red, vertex 1 green, vertex 2 blue (0..1 floats).
- [ ] A triangle with neither set uploads all three vertices in `Color` (existing `ATriangleIsDrawnBetweenItsCornersInItsColour` stays green).
- [ ] `PluginWorldTriangle`'s positional constructor is unchanged.
- [ ] `ForkContractVersion` is `0.1.25-fork.8`; `docs/plugin-api.md` documents `ColorB`/`ColorC` and that an older host draws `Color` only.

**Verify:** `~/.dotnet/dotnet test tests/AcDream.App.Tests --filter "FullyQualifiedName~PluginWorldTriangle"` → all pass

**Steps:**

- [ ] **Step 1: Write the failing test** (add to `PluginWorldTriangleRendererTests`, next to `ATriangleIsDrawnBetweenItsCornersInItsColour`; reuse its `Scene` and `Triangle` helpers)

```csharp
[Fact]
public void CornerColoursAreUploadedPerVertex()
{
    using var scene = new Scene(camera: new Vector3(0f, 0f, 10f));
    var green = new PluginColor(0, 255, 0, 255);
    var blue = new PluginColor(0, 0, 255, 16);

    scene.Draw(Triangle(new Vector3(10f, 0f, 0.1f), new Vector3(34f, 0f, 0.1f), new Vector3(10f, 24f, 2.1f), Red)
        with { ColorB = green, ColorC = blue });

    float[][] vertices = scene.UploadedVertices();
    Assert.Equal(3, vertices.Length);
    Assert.Equal(new Vector4(1f, 0f, 0f, 96f / 255f), Colour(vertices[0]));
    Assert.Equal(new Vector4(0f, 1f, 0f, 1f), Colour(vertices[1]));
    Assert.Equal(new Vector4(0f, 0f, 1f, 16f / 255f), Colour(vertices[2]));
}

private static Vector4 Colour(float[] vertex) => new(vertex[3], vertex[4], vertex[5], vertex[6]);
```

- [ ] **Step 2: Run it to see it fail**

Run: `~/.dotnet/dotnet test tests/AcDream.App.Tests --filter "FullyQualifiedName~CornerColoursAreUploadedPerVertex"`
Expected: build error, `ColorB` does not exist.

- [ ] **Step 3: Add the properties** (`WorldTriangles.cs`; keep the existing XML doc on the record and add to it)

```csharp
public readonly record struct PluginWorldTriangle(
    PluginNavigationPosition A,
    PluginNavigationPosition B,
    PluginNavigationPosition C,
    PluginColor Color)
{
    /// <summary>
    /// Corner <see cref="B"/>'s colour, blended across the face toward the
    /// other corners; null uses <see cref="Color"/>. A host before contract
    /// <c>0.1.25-fork.8</c> ignores it and fills the triangle in <see cref="Color"/>.
    /// </summary>
    public PluginColor? ColorB { get; init; }

    /// <summary>Corner <see cref="C"/>'s colour, as <see cref="ColorB"/>.</summary>
    public PluginColor? ColorC { get; init; }
}
```

Also extend the record's `<summary>`: "Its corners can each have a colour (`Color` for A, `ColorB`, `ColorC`), blended across the face."

- [ ] **Step 4: Pass three colours through the renderer**

`GroundShapeBatch.cs` — replace the single-colour `AddTriangle` with a per-corner one and keep the old one as a forwarder:

```csharp
/// <summary>Adds a triangle of one colour, or nothing once the budget is spent.</summary>
internal void AddTriangle(Vector3 a, Vector3 b, Vector3 c, Vector4 color) => AddTriangle(a, b, c, color, color, color);

/// <summary>Adds a triangle with a colour at each corner, or nothing once the budget is spent.</summary>
internal void AddTriangle(Vector3 a, Vector3 b, Vector3 c, Vector4 colorA, Vector4 colorB, Vector4 colorC)
{
    if (RemainingVertexBudget < 3)
        return;
    AddVertex(a, colorA);
    AddVertex(b, colorB);
    AddVertex(c, colorC);
}
```

`PluginWorldTriangleRenderer.cs`:
- `PlannedTriangle` becomes `internal readonly record struct PlannedTriangle(Vector3 A, Vector3 B, Vector3 C, Vector4 ColorA, Vector4 ColorB, Vector4 ColorC, float DistanceSquared);` and its doc says "its corners' colours as 0 to 1".
- In `Plan`, replace the colour lines with:

```csharp
Vector4 colorA = Unit(triangle.Color);
planned.Add(new PlannedTriangle(
    a, b, c,
    colorA,
    triangle.ColorB is { } colorB ? Unit(colorB) : colorA,
    triangle.ColorC is { } colorC ? Unit(colorC) : colorA,
    distanceSquared));
```

  and add `private static Vector4 Unit(PluginColor color) => new Vector4(color.R, color.G, color.B, color.A) / 255f;`
- In `Render`, the `batch.AddTriangle(...)` call passes `planned.ColorA, planned.ColorB, planned.ColorC`.

- [ ] **Step 5: Bump the contract and document it**

`AcDream.Plugin.Abstractions.csproj:39`: `<ForkContractVersion>0.1.25-fork.8</ForkContractVersion>`.

`docs/plugin-api.md`, in the WorldTriangles section after the paragraph that starts "`WorldTriangles` fills areas of the world", add:

```markdown
A triangle can also colour its corners separately: `ColorB` and `ColorC`
(init-only, `PluginColor?`) set corners B and C, `Color` sets A, and the
fill blends between them across the face — a wall fading to clear at its
top edge, say. An unset corner takes `Color`. They need contract
`0.1.25-fork.8`; an older host fills the whole triangle in `Color`.

```csharp
new PluginWorldTriangle(bottomLeft, bottomRight, topRight, solid) { ColorC = clear }
```
```

- [ ] **Step 6: Run the triangle tests**

Run: `~/.dotnet/dotnet test tests/AcDream.App.Tests --filter "FullyQualifiedName~PluginWorldTriangle"`
Expected: all pass. Also build the Golem/Aethrix-facing abstractions: `~/.dotnet/dotnet build src/AcDream.Plugin.Abstractions` → 0 warnings.

- [ ] **Step 7: Commit**

```bash
git add src/AcDream.Plugin.Abstractions src/AcDream.App/Rendering/GroundShapeBatch.cs src/AcDream.App/Rendering/PluginWorldTriangleRenderer.cs docs/plugin-api.md tests/AcDream.App.Tests/Rendering/PluginWorldTriangleRendererTests.cs
git commit -m "feat(plugins): world triangles can colour each corner (contract 0.1.25-fork.8)

Claude-Session: https://claude.ai/code/session_01X3ryhqAVUCdtME4PCQLPJi"
```

---

### Task 2: Runtime seams — SetAutoRun, SnapHeading, cell-local positions

**Goal:** `RuntimeLocalPlayerMovementState.SetAutoRun(bool)` and `SnapHeading(float)` exist, and `RuntimeNavigationPositions.FromCellLocal(cellId, origin)` turns a cell-local point into a `PluginNavigationPosition` from outside `AcDream.Runtime`.

**Files:**
- Modify: `src/AcDream.Runtime/Gameplay/RuntimeLocalPlayerMovementState.cs` (next to `CancelAutoRun`, ~line 363)
- Create: `src/AcDream.Runtime/Navigation/RuntimeNavigationPositions.cs`
- Test: `tests/AcDream.Runtime.Tests/Gameplay/RuntimeLocalPlayerMovementStateTests.cs`
- Test: `tests/AcDream.Runtime.Tests/Navigation/RuntimeNavigationPositionsTests.cs` (create)

**Acceptance Criteria:**
- [ ] `SetAutoRun(true)` turns autorun on and bumps `Revision`; a second `SetAutoRun(true)` leaves `Revision` unchanged; `SetAutoRun(false)` turns it off. All return true.
- [ ] `SnapHeading(h)` for h in {0, 90, 200, 350, -10} sets the body so `MoveToMath.GetHeading(View.Snapshot.Position.Frame.Orientation)` is h normalised to [0, 360) within 0.01°, and RynthAi's formula `(-2·atan2(qz,qw)·180/π + 720) % 360` on that quaternion gives the same value.
- [ ] `SnapHeading` returns false with no controller and for a non-finite heading.
- [ ] `FromCellLocal(0xA9B40001, (12, 34, 5))` equals `RuntimeNavigationProjection.Position(new Position(0xA9B40001, new CellFrame((12,34,5), Quaternion.Identity)))` field for field.

**Verify:** `~/.dotnet/dotnet test tests/AcDream.Runtime.Tests --filter "FullyQualifiedName~RuntimeLocalPlayerMovementStateTests|FullyQualifiedName~RuntimeNavigationPositionsTests"` → all pass

**Steps:**

- [ ] **Step 1: Write the failing tests** (in `RuntimeLocalPlayerMovementStateTests`, using the same controller setup as `ViewProjectsTheExactCanonicalControllerAndAutorunOwner`)

```csharp
[Fact]
public void SetAutoRunSetsTheLatchAndARepeatChangesNothing()
{
    using var movement = new RuntimeLocalPlayerMovementState();

    Assert.True(movement.SetAutoRun(true));
    Assert.True(movement.AutoRunActive);
    long revision = movement.Revision;
    Assert.True(movement.SetAutoRun(true));
    Assert.Equal(revision, movement.Revision);
    Assert.True(movement.SetAutoRun(false));
    Assert.False(movement.AutoRunActive);
}

[Theory]
[InlineData(0f, 0f)]
[InlineData(90f, 90f)]
[InlineData(200f, 200f)]
[InlineData(350f, 350f)]
[InlineData(-10f, 350f)]
public void SnapHeadingTurnsTheBodyAtOnceAsRynthAiReadsIt(float asked, float expected)
{
    var controller = new PlayerMovementController(new PhysicsEngine()) { LocalEntityId = 0x50000001u };
    controller.SeedPlacementForTest(new Vector3(11f, 12f, 13f), 0xA9B40001u, new Vector3(11f, 12f, 13f));
    using var movement = new RuntimeLocalPlayerMovementState { Controller = controller };

    Assert.True(movement.SnapHeading(asked));

    Quaternion q = movement.View.Snapshot.Position.Frame.Orientation;
    Assert.Equal(expected, AcDream.Core.Physics.Motion.MoveToMath.GetHeading(q), precision: 2);
    float rynth = (float)((-2.0 * Math.Atan2(q.Z, q.W) * 180.0 / Math.PI + 720.0) % 360.0);
    Assert.Equal(expected, rynth, precision: 2);
}

[Fact]
public void SnapHeadingNeedsALiveBodyAndAFiniteHeading()
{
    using var idle = new RuntimeLocalPlayerMovementState();
    Assert.False(idle.SnapHeading(90f));

    var controller = new PlayerMovementController(new PhysicsEngine()) { LocalEntityId = 0x50000001u };
    controller.SeedPlacementForTest(new Vector3(11f, 12f, 13f), 0xA9B40001u, new Vector3(11f, 12f, 13f));
    using var movement = new RuntimeLocalPlayerMovementState { Controller = controller };
    Assert.False(movement.SnapHeading(float.NaN));
}
```

Precision note: `GetHeading` of exactly 0° may come back as 360 - ε. If the 0° case fails only for that reason, compare `(value + 360) % 360` modulo wrap (assert `MathF.Min(d, 360 - d) < 0.01` with `d = MathF.Abs(actual - expected)`) for both asserts instead of `Assert.Equal`.

New file `tests/AcDream.Runtime.Tests/Navigation/RuntimeNavigationPositionsTests.cs`:

```csharp
using System.Numerics;
using AcDream.Core.Physics;
using AcDream.Plugin.Abstractions;
using AcDream.Runtime.Navigation;

namespace AcDream.Runtime.Tests.Navigation;

public sealed class RuntimeNavigationPositionsTests
{
    [Theory]
    [InlineData(0xA9B40001u)] // outdoor cell
    [InlineData(0x01D90105u)] // dungeon cell
    public void FromCellLocalIsTheNavigationProjectionOfThatPoint(uint cellId)
    {
        var origin = new Vector3(12f, 34f, 5f);

        PluginNavigationPosition actual = RuntimeNavigationPositions.FromCellLocal(cellId, origin);
        PluginNavigationPosition expected = RuntimeNavigationProjection.Position(
            new Position(cellId, new CellFrame(origin, Quaternion.Identity)));

        Assert.Equal(expected, actual);
    }
}
```

(`CellFrame` is the frame type `Position` takes; `NavigationChatCommands.cs:470` builds one the same way.)

- [ ] **Step 2: Run them to see them fail**

Run the Verify command. Expected: build errors (`SetAutoRun`, `SnapHeading`, `RuntimeNavigationPositions` missing).

- [ ] **Step 3: Implement**

`RuntimeLocalPlayerMovementState.cs`, after `CancelAutoRun`:

```csharp
/// <summary>
/// Turns autorun on or off, as RynthCore's SetAutoRun does; a repeat of the
/// current state changes nothing. Always true.
/// </summary>
public bool SetAutoRun(bool enabled)
{
    ObjectDisposedException.ThrowIf(_disposed, this);
    if (_autoRunActive == enabled)
        return true;
    _autoRunActive = enabled;
    Interlocked.Increment(ref _revision);
    return true;
}

/// <summary>
/// Sets the body's heading at once (compass degrees: 0 north, clockwise), as
/// RynthCore's TurnToHeading writes the player's frame. Nothing is cancelled;
/// the next position update tells the server. False with no live body.
/// </summary>
public bool SnapHeading(float headingDegrees)
{
    ObjectDisposedException.ThrowIf(_disposed, this);
    if (!float.IsFinite(headingDegrees)
        || _controller is not { } controller
        || !controller.CanExecuteLiveMovement)
        return false;
    float normalized = headingDegrees % 360f;
    if (normalized < 0f)
        normalized += 360f;
    controller.Yaw = AcDream.Core.Physics.Motion.MoveToMath.YawFromHeading(normalized);
    Interlocked.Increment(ref _revision);
    return true;
}
```

If `CanExecuteLiveMovement` is false for a controller built with `new PlayerMovementController(new PhysicsEngine())` + `SeedPlacementForTest` (the SnapHeading theory then fails on the first assert), look at how `tests/AcDream.Runtime.Tests/Gameplay/PlayerMovementControllerTests.cs:98` gets a controller whose `Yaw` setter works, and build the test controller that way. Do not drop the guard: `CaptureScriptedInput` uses the same one before writing `Yaw`.

New file `src/AcDream.Runtime/Navigation/RuntimeNavigationPositions.cs`:

```csharp
using System.Numerics;
using AcDream.Core.Physics;
using AcDream.Plugin.Abstractions;

namespace AcDream.Runtime.Navigation;

/// <summary>Positions in the client's cell frames as plugins' navigation positions.</summary>
public static class RuntimeNavigationPositions
{
    /// <summary>
    /// A point in <paramref name="cellId"/>'s landblock frame (metres, Z up) as a
    /// navigation position; a point past the landblock's edge lands in the
    /// neighbouring one.
    /// </summary>
    public static PluginNavigationPosition FromCellLocal(uint cellId, Vector3 origin) =>
        RuntimeNavigationProjection.Position(new Position(cellId, new CellFrame(origin, Quaternion.Identity)));
}
```

- [ ] **Step 4: Run the Verify command.** Expected: all pass.

- [ ] **Step 5: Commit**

```bash
git add src/AcDream.Runtime/Gameplay/RuntimeLocalPlayerMovementState.cs src/AcDream.Runtime/Navigation/RuntimeNavigationPositions.cs tests/AcDream.Runtime.Tests/Gameplay/RuntimeLocalPlayerMovementStateTests.cs tests/AcDream.Runtime.Tests/Navigation/RuntimeNavigationPositionsTests.cs
git commit -m "feat(runtime): set autorun, snap the player's heading, cell-local navigation positions

Claude-Session: https://claude.ai/code/session_01X3ryhqAVUCdtME4PCQLPJi"
```

---

### Task 3: Movement and door slots

**Goal:** RynthAi's `SetAutoRun`, `TurnToHeading`, `SetMotion`, `StopCompletely`, `JumpNonAutonomous`, `TapJump`, `CommenceJump`, `DoJump`, `LaunchJumpWithMotion`, `GetObjectState` and `GetObjectMotionOn` slots are bound and drive OpenAC's player through `IRynthGameView`.

**Files:**
- Modify: `src/AcDream.Rynth.Host/IRynthGameView.cs` (append members)
- Modify: `src/AcDream.Rynth.Host/RuntimeRynthGameView.cs` (append members)
- Create: `src/AcDream.Rynth.Host/RynthMotionHolds.cs`
- Create: `src/AcDream.Rynth.Host/RynthApiTable.Movement.cs`
- Modify: `src/AcDream.Rynth.Host/RynthApiTable.cs` (constructor: `BindMovementSlots();`, a `Holds` property)
- Modify: `src/AcDream.Rynth.Host/RynthHost.cs` (`Tick`: `_table.Holds.Sync()` before plugins tick)
- Modify: `tests/AcDream.Rynth.Host.Tests/FakeGameView.cs`
- Modify: `tests/AcDream.App.Tests/Extensions/FakeRynthAutomation.cs` (`FakeNavigation` records intents)
- Test: `tests/AcDream.Rynth.Host.Tests/RynthMotionHoldsTests.cs` (create)
- Test: `tests/AcDream.Rynth.Host.Tests/RynthApiTableTests.cs` (new movement test; update `DeferredSlotsStayUnset`)
- Test: `tests/AcDream.App.Tests/Extensions/RuntimeRynthGameViewTests.cs`

**Acceptance Criteria:**
- [ ] Holds: `SetMotion(RunForward, on)` → intent Forward + Run; `WalkForward` → Forward, Run false; TurnLeft/TurnRight/SidestepLeft/SidestepRight/WalkBackward map to TurnLeft/TurnRight/StrafeLeft/StrafeRight/Backward; with autorun on, any non-empty intent also has Forward + Run; releasing the last motion clears the intent; releasing an unheld motion sends nothing; an unknown id returns false and changes nothing.
- [ ] Holds: CommenceJump makes the intent hold Jump; DoJump releases it (intent cleared when nothing else is held); LaunchJumpWithMotion(shift=false, w=true) releases Jump with Forward + Run for one sync, then the next `Sync()` restores the held set.
- [ ] `StopCompletely` clears holds, clears the intent and calls the view's stop.
- [ ] Table: every slot above is non-zero; each forwards to the view; a throwing view logs "`<Slot>` threw" and returns 0.
- [ ] `GetObjectState` returns the view's raw state; `GetObjectMotionOn` returns 1 + isOn for a known On/Off, 0 for unknown.
- [ ] `RuntimeRynthGameView`: `TryGetObjectState` reads `RawPhysicsState` of an active entity; `TryGetObjectMotionOn` reads `Snapshot.MotionState.ForwardCommand` 0x000B/0x000C; `SetMovementIntent` goes through `IAutomationSurface.Navigation`; `SetAutoRun`/`SnapHeading`/`StopCompletely`/`BeginJump` are false when the surface is not available.
- [ ] `DeferredSlotsStayUnset` asserts only the slots in Global Constraints' "left unset" list.

**Verify:** `~/.dotnet/dotnet test tests/AcDream.Rynth.Host.Tests` → all pass; `~/.dotnet/dotnet test tests/AcDream.App.Tests --filter "FullyQualifiedName~RuntimeRynthGameView|FullyQualifiedName~RynthClientExtension"` → all pass

**Steps:**

- [ ] **Step 1: Add the view members** (append to `IRynthGameView`)

```csharp
/// <summary>True while the player's autorun is on.</summary>
bool AutoRunActive { get; }

/// <summary>Turns autorun on or off. True when taken.</summary>
bool SetAutoRun(bool enabled);

/// <summary>Turns the player to a compass heading at once (0 north, clockwise). True when taken.</summary>
bool SnapHeading(float headingDegrees);

/// <summary>Holds movement keys for the player, replacing the keyboard while held. True when taken.</summary>
bool SetMovementIntent(in AcDream.Plugin.Abstractions.PluginMovementIntent intent);

/// <summary>Lets go of every held movement key. True when taken.</summary>
bool ClearMovementIntent();

/// <summary>Stops the player completely, autorun included. True when taken.</summary>
bool StopCompletely();

/// <summary>Charges a jump to a power of 0..1 over the frames that takes, then releases it. True when taken.</summary>
bool BeginJump(float power);

/// <summary>An object's raw physics-state bits (0x4 ethereal: an open door); false when the object is unknown.</summary>
bool TryGetObjectState(uint objectId, out uint state);

/// <summary>Whether an object's last motion was On (true) or Off (false); false when neither has been seen.</summary>
bool TryGetObjectMotionOn(uint objectId, out bool isOn);
```

- [ ] **Step 2: Write the holds tests** (create `tests/AcDream.Rynth.Host.Tests/RynthMotionHoldsTests.cs`)

The fake view (Step 4) records `SetMovementIntent` / `ClearMovementIntent` calls in `Intents` (a `List<PluginMovementIntent?>`, null for a clear) and has a settable `AutoRun`.

```csharp
using AcDream.Plugin.Abstractions;
using AcDream.Rynth.Host;

namespace AcDream.Rynth.Host.Tests;

public sealed class RynthMotionHoldsTests
{
    private const uint WalkForward = 0x45000005, WalkBackward = 0x45000006, RunForward = 0x41000003,
        TurnRight = 0x6500000D, TurnLeft = 0x6500000E, SidestepRight = 0x6500000F, SidestepLeft = 0x65000010;

    private readonly FakeGameView _view = new();

    [Fact]
    public void EachMotionHoldsItsKey()
    {
        var holds = new RynthMotionHolds(_view);

        Assert.True(holds.SetMotion(RunForward, true));
        Assert.Equal(new PluginMovementIntent(Forward: true, Run: true), _view.Intents[^1]);
        Assert.True(holds.SetMotion(RunForward, false));
        Assert.True(holds.SetMotion(WalkForward, true));
        Assert.Equal(new PluginMovementIntent(Forward: true, Run: false), _view.Intents[^1]);
        holds.SetMotion(WalkForward, false);

        holds.SetMotion(WalkBackward, true);
        holds.SetMotion(TurnLeft, true);
        holds.SetMotion(TurnRight, true);
        holds.SetMotion(SidestepLeft, true);
        holds.SetMotion(SidestepRight, true);
        Assert.Equal(
            new PluginMovementIntent(Backward: true, TurnLeft: true, TurnRight: true, StrafeLeft: true, StrafeRight: true, Run: false),
            _view.Intents[^1]);
    }

    [Fact]
    public void ReleasingTheLastKeyClearsAndReleasingAnUnheldKeySendsNothing()
    {
        var holds = new RynthMotionHolds(_view);

        Assert.True(holds.SetMotion(TurnLeft, false));
        Assert.Empty(_view.Intents);

        holds.SetMotion(TurnLeft, true);
        holds.SetMotion(TurnLeft, false);
        Assert.Null(_view.Intents[^1]);
    }

    [Fact]
    public void AnUnknownMotionIsRefused()
    {
        var holds = new RynthMotionHolds(_view);

        Assert.False(holds.SetMotion(0x41000012, true));
        Assert.Empty(_view.Intents);
    }

    [Fact]
    public void WithAutorunOnAHeldTurnStillRuns()
    {
        _view.AutoRun = true;
        var holds = new RynthMotionHolds(_view);

        holds.SetMotion(TurnRight, true);

        Assert.Equal(new PluginMovementIntent(Forward: true, TurnRight: true, Run: true), _view.Intents[^1]);
    }

    [Fact]
    public void SyncFollowsAutorunAndSendsNothingWhenUnchanged()
    {
        var holds = new RynthMotionHolds(_view);
        holds.SetMotion(TurnRight, true);
        int sent = _view.Intents.Count;

        holds.Sync();
        Assert.Equal(sent, _view.Intents.Count);

        _view.AutoRun = true;
        holds.Sync();
        Assert.Equal(new PluginMovementIntent(Forward: true, TurnRight: true, Run: true), _view.Intents[^1]);
    }

    [Fact]
    public void CommenceHoldsJumpAndDoJumpReleasesIt()
    {
        var holds = new RynthMotionHolds(_view);

        Assert.True(holds.CommenceJump());
        Assert.Equal(new PluginMovementIntent(Run: false, Jump: true), _view.Intents[^1]);
        Assert.True(holds.DoJump());
        Assert.Null(_view.Intents[^1]);
    }

    [Fact]
    public void LaunchWithMotionReleasesWithItsKeysForOneSync()
    {
        var holds = new RynthMotionHolds(_view);
        holds.SetMotion(TurnLeft, true);
        holds.CommenceJump();

        Assert.True(holds.LaunchJumpWithMotion(shift: false, forward: true, backward: false, strafeLeft: false, strafeRight: true));
        Assert.Equal(new PluginMovementIntent(Forward: true, StrafeRight: true, TurnLeft: true, Run: true), _view.Intents[^1]);

        holds.Sync();
        Assert.Equal(new PluginMovementIntent(TurnLeft: true, Run: false), _view.Intents[^1]);
    }

    [Fact]
    public void LaunchWithShiftWalks()
    {
        var holds = new RynthMotionHolds(_view);
        holds.CommenceJump();

        holds.LaunchJumpWithMotion(shift: true, forward: true, backward: false, strafeLeft: false, strafeRight: false);

        Assert.Equal(new PluginMovementIntent(Forward: true, Run: false), _view.Intents[^1]);
    }

    [Fact]
    public void StopCompletelyLetsGoOfEverything()
    {
        var holds = new RynthMotionHolds(_view);
        holds.SetMotion(RunForward, true);
        holds.CommenceJump();

        Assert.True(holds.StopCompletely());

        Assert.Null(_view.Intents[^1]);
        Assert.Contains("stop", _view.Actions);
        holds.Sync();
        Assert.Null(_view.Intents[^1]);
    }
}
```

- [ ] **Step 3: Implement `RynthMotionHolds`** (create `src/AcDream.Rynth.Host/RynthMotionHolds.cs`)

```csharp
using AcDream.Plugin.Abstractions;

namespace AcDream.Rynth.Host;

/// <summary>
/// RynthCore's held movement keys (SetMotion on/off, the jump key) kept as a
/// set and handed to the client as one movement intent. The intent replaces
/// the keyboard while anything is held, so it carries autorun too.
/// </summary>
internal sealed class RynthMotionHolds(IRynthGameView view)
{
    private const uint WalkForward = 0x45000005, WalkBackward = 0x45000006, RunForward = 0x41000003,
        TurnRight = 0x6500000D, TurnLeft = 0x6500000E, SidestepRight = 0x6500000F, SidestepLeft = 0x65000010;

    private readonly HashSet<uint> _held = [];
    private bool _jump;
    private PluginMovementIntent? _launch;
    private PluginMovementIntent? _sent;

    /// <summary>Holds or lets go of a motion key. False for a motion RynthCore's SetMotion does not hold.</summary>
    public bool SetMotion(uint motion, bool enabled)
    {
        if (motion is not (WalkForward or WalkBackward or RunForward or TurnRight or TurnLeft or SidestepRight or SidestepLeft))
            return false;
        bool changed = enabled ? _held.Add(motion) : _held.Remove(motion);
        return !changed || Apply();
    }

    /// <summary>Presses the jump key: the client charges as it does for a held space bar.</summary>
    public bool CommenceJump()
    {
        _jump = true;
        return Apply();
    }

    /// <summary>Lets go of the jump key: the client jumps at the power charged.</summary>
    public bool DoJump()
    {
        _jump = false;
        return Apply();
    }

    /// <summary>Lets go of the jump key with these keys down for that frame, so the jump carries their motion.</summary>
    public bool LaunchJumpWithMotion(bool shift, bool forward, bool backward, bool strafeLeft, bool strafeRight)
    {
        _jump = false;
        PluginMovementIntent held = Held();
        _launch = held with
        {
            Forward = held.Forward || forward,
            Backward = held.Backward || backward,
            StrafeLeft = held.StrafeLeft || strafeLeft,
            StrafeRight = held.StrafeRight || strafeRight,
            Run = held.Run || (!shift && (forward || backward || strafeLeft || strafeRight)),
        };
        return Apply();
    }

    /// <summary>Lets go of every key and stops the player.</summary>
    public bool StopCompletely()
    {
        _held.Clear();
        _jump = false;
        _launch = null;
        Apply();
        return view.StopCompletely();
    }

    /// <summary>Once a tick, before plugins tick: ends a launch frame and follows autorun.</summary>
    public void Sync()
    {
        _launch = null;
        Apply();
    }

    /// <summary>Re-sends the held keys after autorun changed, leaving a launch frame alone.</summary>
    public bool Refresh() => Apply();

    private bool Apply()
    {
        PluginMovementIntent? wanted = _launch ?? (_held.Count == 0 && !_jump ? null : Held());
        if (wanted == _sent)
            return true;
        bool taken = wanted is { } intent ? view.SetMovementIntent(intent) : view.ClearMovementIntent();
        if (taken)
            _sent = wanted;
        return taken;
    }

    private PluginMovementIntent Held()
    {
        bool autoRun = view.AutoRunActive;
        return new PluginMovementIntent(
            Forward: autoRun || _held.Contains(RunForward) || _held.Contains(WalkForward),
            Backward: _held.Contains(WalkBackward),
            StrafeLeft: _held.Contains(SidestepLeft),
            StrafeRight: _held.Contains(SidestepRight),
            TurnLeft: _held.Contains(TurnLeft),
            TurnRight: _held.Contains(TurnRight),
            Run: autoRun || _held.Contains(RunForward),
            Jump: _jump);
    }
}
```

Check against the tests: `LaunchWithShiftWalks` expects `Run: false` with shift; `LaunchWithMotionReleasesWithItsKeysForOneSync` expects `Run: true` without shift. The `Run` expression above gives exactly that (held.Run is false in both tests). The empty-intent case after `StopCompletely` sends a clear, then `Sync` sends nothing more (`_sent` is already null), and the test reads the same last entry.

- [ ] **Step 4: Fake view members** (`tests/AcDream.Rynth.Host.Tests/FakeGameView.cs`)

```csharp
public bool AutoRun { get; set; }
public List<AcDream.Plugin.Abstractions.PluginMovementIntent?> Intents { get; } = new();
public Dictionary<uint, uint> ObjectStates { get; } = new();
public Dictionary<uint, bool> MotionOn { get; } = new();

public bool AutoRunActive => Read(AutoRun);
public bool SetAutoRun(bool enabled) => Act($"autorun {(enabled ? 1 : 0)}");
public bool SnapHeading(float headingDegrees) => Act($"heading {headingDegrees:0.##}");
public bool SetMovementIntent(in AcDream.Plugin.Abstractions.PluginMovementIntent intent)
{
    if (Throw) throw new InvalidOperationException("view failed");
    Intents.Add(intent);
    return ActionAnswer;
}
public bool ClearMovementIntent()
{
    if (Throw) throw new InvalidOperationException("view failed");
    Intents.Add(null);
    return ActionAnswer;
}
public bool StopCompletely() => Act("stop");
public bool BeginJump(float power) => Act($"jump {power:0.###}");
public bool TryGetObjectState(uint objectId, out uint state)
{
    if (Throw) throw new InvalidOperationException("view failed");
    return ObjectStates.TryGetValue(objectId, out state);
}
public bool TryGetObjectMotionOn(uint objectId, out bool isOn)
{
    if (Throw) throw new InvalidOperationException("view failed");
    return MotionOn.TryGetValue(objectId, out isOn);
}
```

Run: `~/.dotnet/dotnet test tests/AcDream.Rynth.Host.Tests --filter "FullyQualifiedName~RynthMotionHoldsTests"` → all pass (the table does not use the holds yet).

- [ ] **Step 5: Write the table test** (add to `RynthApiTableTests`)

```csharp
[Fact]
public void MovementSlotsDriveTheView()
{
    var view = new FakeGameView();
    view.ObjectStates[0x7A9B4001] = 0x4;
    view.MotionOn[0x7A9B4001] = true;
    using var table = new RynthApiTable(view, _log.Add);
    var host = new RynthCoreHost(*table.Native);

    Assert.True(host.HasSetAutoRun && host.HasTurnToHeading && host.HasSetMotion && host.HasStopCompletely);
    Assert.True(host.HasJumpNonAutonomous && host.HasTapJump && host.HasCommenceJump && host.HasDoJump && host.HasLaunchJumpWithMotion);
    Assert.True(host.HasGetObjectState && host.HasGetObjectMotionOn);

    Assert.True(host.SetAutoRun(true));
    Assert.True(host.TurnToHeading(123.5f));
    Assert.True(host.SetMotion(0x41000003, true));
    Assert.Equal(new AcDream.Plugin.Abstractions.PluginMovementIntent(Forward: true, Run: true), view.Intents[^1]);
    Assert.False(host.SetMotion(0x41000012, true));
    Assert.True(host.JumpNonAutonomous(0.5f));
    Assert.True(host.TapJump());
    Assert.True(host.CommenceJump());
    Assert.True(host.DoJump(true));
    Assert.True(host.CommenceJump());
    Assert.True(host.LaunchJumpWithMotion(false, true, false, false, false));
    Assert.True(host.StopCompletely());
    Assert.Equal(
        new[] { "autorun 1", "heading 123.5", "jump 0.5", $"jump {RynthApiTable.TapJumpPower:0.###}", "stop" },
        view.Actions);

    Assert.True(host.TryGetObjectState(0x7A9B4001, out uint state));
    Assert.Equal(0x4u, state);
    Assert.False(host.TryGetObjectState(0x7A9B4002, out _));
    Assert.True(host.TryGetObjectMotionOn(0x7A9B4001, out bool on));
    Assert.True(on);
    Assert.False(host.TryGetObjectMotionOn(0x7A9B4002, out _));

    view.Throw = true;
    Assert.False(host.SetAutoRun(false));
    Assert.False(host.TryGetObjectState(0x7A9B4001, out _));
    Assert.Contains(_log, line => line.Contains("SetAutoRun threw", StringComparison.Ordinal));
    Assert.Contains(_log, line => line.Contains("GetObjectState threw", StringComparison.Ordinal));
}
```

(These `Has*` names are checked against `RynthCoreHost.cs:50-127,1208,1226`.)

Update `DeferredSlotsStayUnset` to:

```csharp
[Fact]
public void DeferredSlotsStayUnset()
{
    using var table = new RynthApiTable(new FakeGameView(), _log.Add);
    Assert.Equal(IntPtr.Zero, table.Native->NativeAttackFn);
    Assert.Equal(IntPtr.Zero, table.Native->StopMovementFn);
    Assert.Equal(IntPtr.Zero, table.Native->DoMovementFn);
    Assert.Equal(IntPtr.Zero, table.Native->SetAutonomyLevelFn);
    Assert.Equal(IntPtr.Zero, table.Native->WorldToScreenFn);
    Assert.Equal(IntPtr.Zero, table.Native->GetViewportSizeFn);
    Assert.Equal(IntPtr.Zero, table.Native->GetRadarRectFn);
    Assert.Equal(IntPtr.Zero, table.Native->SetRadarSuppressedFn);
    Assert.Equal(IntPtr.Zero, table.Native->GetPluginInterfaceFn);
    Assert.Equal(IntPtr.Zero, table.Native->MoveItemExternalFn);
    Assert.Equal(IntPtr.Zero, table.Native->GetObjectPalettesFn);
    Assert.Equal(IntPtr.Zero, table.Native->GetNumContainedItemsFn);
}
```

- [ ] **Step 6: Implement the table partial** (create `src/AcDream.Rynth.Host/RynthApiTable.Movement.cs`)

In `RynthApiTable.cs`: add the field and property `internal RynthMotionHolds Holds { get; }`, set `Holds = new RynthMotionHolds(view);` in the constructor before the `Bind*` calls, and call `BindMovementSlots();` after `BindCombatSlots();`.

```csharp
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace AcDream.Rynth.Host;

// The table's movement and door slots (RynthAi sub-project #4). Same rules as every
// table function: catch everything, log, return RynthCore's "not available" value.
public sealed unsafe partial class RynthApiTable
{
    /// <summary>TapJump's charge: RynthCore presses and releases at once, the least a jump can be.</summary>
    internal const float TapJumpPower = 1f / 30f;

    private void BindMovementSlots()
    {
        Native->SetAutoRunFn = (IntPtr)(delegate* unmanaged[Cdecl]<int, int>)&SetAutoRun;
        Native->TurnToHeadingFn = (IntPtr)(delegate* unmanaged[Cdecl]<float, int>)&TurnToHeading;
        Native->SetMotionFn = (IntPtr)(delegate* unmanaged[Cdecl]<uint, int, int>)&SetMotion;
        Native->StopCompletelyFn = (IntPtr)(delegate* unmanaged[Cdecl]<int>)&StopCompletely;
        Native->JumpNonAutonomousFn = (IntPtr)(delegate* unmanaged[Cdecl]<float, int>)&JumpNonAutonomous;
        Native->TapJumpFn = (IntPtr)(delegate* unmanaged[Cdecl]<int>)&TapJump;
        Native->CommenceJumpFn = (IntPtr)(delegate* unmanaged[Cdecl]<int>)&CommenceJump;
        Native->DoJumpFn = (IntPtr)(delegate* unmanaged[Cdecl]<int, int>)&DoJump;
        Native->LaunchJumpWithMotionFn = (IntPtr)(delegate* unmanaged[Cdecl]<int, int, int, int, int, int>)&LaunchJumpWithMotion;
        Native->GetObjectStateFn = (IntPtr)(delegate* unmanaged[Cdecl]<uint, uint*, int>)&GetObjectState;
        Native->GetObjectMotionOnFn = (IntPtr)(delegate* unmanaged[Cdecl]<uint, int*, int>)&GetObjectMotionOn;
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static int SetAutoRun(int enabled)
    {
        RynthApiTable? table = _current;
        if (table is null)
            return 0;
        try
        {
            if (!table._view.SetAutoRun(enabled != 0))
                return 0;
            table.Holds.Refresh();
            return 1;
        }
        catch (Exception error)
        {
            table.Fail(nameof(SetAutoRun), error);
            return 0;
        }
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static int TurnToHeading(float headingDegrees)
    {
        RynthApiTable? table = _current;
        if (table is null)
            return 0;
        try { return table._view.SnapHeading(headingDegrees) ? 1 : 0; }
        catch (Exception error)
        {
            table.Fail(nameof(TurnToHeading), error);
            return 0;
        }
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static int SetMotion(uint motion, int enabled)
    {
        RynthApiTable? table = _current;
        if (table is null)
            return 0;
        try { return table.Holds.SetMotion(motion, enabled != 0) ? 1 : 0; }
        catch (Exception error)
        {
            table.Fail(nameof(SetMotion), error);
            return 0;
        }
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static int StopCompletely()
    {
        RynthApiTable? table = _current;
        if (table is null)
            return 0;
        try { return table.Holds.StopCompletely() ? 1 : 0; }
        catch (Exception error)
        {
            table.Fail(nameof(StopCompletely), error);
            return 0;
        }
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static int JumpNonAutonomous(float extent)
    {
        RynthApiTable? table = _current;
        if (table is null || !float.IsFinite(extent))
            return 0;
        try { return table._view.BeginJump(Math.Clamp(extent, TapJumpPower, 1f)) ? 1 : 0; }
        catch (Exception error)
        {
            table.Fail(nameof(JumpNonAutonomous), error);
            return 0;
        }
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static int TapJump()
    {
        RynthApiTable? table = _current;
        if (table is null)
            return 0;
        try { return table._view.BeginJump(TapJumpPower) ? 1 : 0; }
        catch (Exception error)
        {
            table.Fail(nameof(TapJump), error);
            return 0;
        }
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static int CommenceJump()
    {
        RynthApiTable? table = _current;
        if (table is null)
            return 0;
        try { return table.Holds.CommenceJump() ? 1 : 0; }
        catch (Exception error)
        {
            table.Fail(nameof(CommenceJump), error);
            return 0;
        }
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static int DoJump(int autonomous)
    {
        RynthApiTable? table = _current;
        if (table is null)
            return 0;
        try { return table.Holds.DoJump() ? 1 : 0; }
        catch (Exception error)
        {
            table.Fail(nameof(DoJump), error);
            return 0;
        }
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static int LaunchJumpWithMotion(int shift, int w, int x, int z, int c)
    {
        RynthApiTable? table = _current;
        if (table is null)
            return 0;
        try { return table.Holds.LaunchJumpWithMotion(shift != 0, w != 0, x != 0, z != 0, c != 0) ? 1 : 0; }
        catch (Exception error)
        {
            table.Fail(nameof(LaunchJumpWithMotion), error);
            return 0;
        }
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static int GetObjectState(uint objectId, uint* state)
    {
        RynthApiTable? table = _current;
        if (table is null || state is null)
            return 0;
        try
        {
            if (!table._view.TryGetObjectState(objectId, out uint value))
                return 0;
            *state = value;
            return 1;
        }
        catch (Exception error)
        {
            table.Fail(nameof(GetObjectState), error);
            return 0;
        }
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static int GetObjectMotionOn(uint objectId, int* isOn)
    {
        RynthApiTable? table = _current;
        if (table is null || isOn is null)
            return 0;
        try
        {
            if (!table._view.TryGetObjectMotionOn(objectId, out bool on))
                return 0;
            *isOn = on ? 1 : 0;
            return 1;
        }
        catch (Exception error)
        {
            table.Fail(nameof(GetObjectMotionOn), error);
            return 0;
        }
    }
}
```

`RynthHost.Tick` becomes:

```csharp
/// <summary>Delivers the frame's queued events, then ticks each plugin, as RynthCore's frame does.</summary>
public void Tick()
{
    Bridge.DeliverQueued();
    _table.Holds.Sync();
    foreach (RynthPlugin plugin in _plugins)
        plugin.Tick();
}
```

Note on the expected `Actions` in the test: `SetAutoRun` logs "autorun 1"; the SetMotion/Commence/DoJump/Launch calls only add `Intents`; `StopCompletely` logs "stop". If `Fail`'s log text is not "`<name>` threw", read `RynthApiTable.cs:121` and match the existing assertions' wording.

Run: `~/.dotnet/dotnet test tests/AcDream.Rynth.Host.Tests` → all pass.

- [ ] **Step 7: Runtime view members + tests**

In `tests/AcDream.App.Tests/Extensions/FakeRynthAutomation.cs`, make `FakeNavigation` record intents:

```csharp
public List<PluginMovementIntent?> Intents { get; } = new();
public PluginNavigationCommandStatus Answer { get; set; } = PluginNavigationCommandStatus.Accepted;
public PluginNavigationCommandStatus SetMovementIntent(in PluginMovementIntent intent)
{
    Intents.Add(intent);
    return Answer;
}
public PluginNavigationCommandStatus ClearMovementIntent()
{
    Intents.Add(null);
    return Answer;
}
```

(Replace the two existing members that return `Unavailable`. If another App test relied on `Unavailable`, set `Answer = PluginNavigationCommandStatus.Unavailable` in that test.)

Add to `RuntimeRynthGameViewTests` (reuse the file's `Spawn` helper):

```csharp
[Fact]
public void MovementGoesThroughTheSurfaceAndTheMovementOwner()
{
    var runtime = GameRuntimeTestFactory.Create();
    var automation = new FakeRynthAutomation();
    var view = new RuntimeRynthGameView(runtime, automation: automation);

    Assert.True(view.SetMovementIntent(new PluginMovementIntent(Forward: true)));
    Assert.True(view.ClearMovementIntent());
    Assert.Equal(new PluginMovementIntent?[] { new PluginMovementIntent(Forward: true), null }, automation.NavigationArea.Intents);

    Assert.True(view.SetAutoRun(true));
    Assert.True(view.AutoRunActive);
    Assert.True(view.StopCompletely());
    Assert.False(view.AutoRunActive);

    automation.Available = false;
    Assert.False(view.SetAutoRun(true));
    Assert.False(view.SnapHeading(90f));
    Assert.False(view.StopCompletely());
    Assert.False(view.BeginJump(0.5f));
}

[Fact]
public void DoorStateComesFromTheEntityRecord()
{
    var runtime = GameRuntimeTestFactory.Create();
    var view = new RuntimeRynthGameView(runtime);
    Assert.False(view.TryGetObjectState(0x7A9B4001, out _));
    Assert.False(view.TryGetObjectMotionOn(0x7A9B4001, out _));

    RuntimeEntityRecord record = runtime.EntityObjects.Entities.AddActive(Spawn(0x7A9B4001));
    Assert.True(view.TryGetObjectState(0x7A9B4001, out uint state));
    Assert.Equal(record.RawPhysicsState, state);
    Assert.False(view.TryGetObjectMotionOn(0x7A9B4001, out _));

    record.Snapshot = record.Snapshot with { MotionState = new CreateObject.ServerMotionState(Stance: 0x3D, ForwardCommand: 0x000B) };
    Assert.True(view.TryGetObjectMotionOn(0x7A9B4001, out bool open));
    Assert.True(open);
    record.Snapshot = record.Snapshot with { MotionState = new CreateObject.ServerMotionState(Stance: 0x3D, ForwardCommand: 0x000C) };
    Assert.True(view.TryGetObjectMotionOn(0x7A9B4001, out bool closed));
    Assert.False(closed);
}
```

`RuntimeEntityRecord.Snapshot` has an `internal set`, which App tests can reach (`InternalsVisibleTo AcDream.App.Tests`). If `EntitySpawn`'s motion field is not named `MotionState`, use the name `grep -n "MotionState" src/AcDream.Core.Net/WorldSession*.cs` shows.

Append to `RuntimeRynthGameView`:

```csharp
public bool AutoRunActive => _runtime.MovementOwner.AutoRunActive;

public bool SetAutoRun(bool enabled) => _automation.IsAvailable && _runtime.MovementOwner.SetAutoRun(enabled);

public bool SnapHeading(float headingDegrees) => _automation.IsAvailable && _runtime.MovementOwner.SnapHeading(headingDegrees);

public bool SetMovementIntent(in PluginMovementIntent intent) =>
    _automation.Navigation.SetMovementIntent(intent) == PluginNavigationCommandStatus.Accepted;

public bool ClearMovementIntent() =>
    _automation.Navigation.ClearMovementIntent() == PluginNavigationCommandStatus.Accepted;

public bool StopCompletely() =>
    _automation.IsAvailable && _runtime.MovementOwner.Execute(RuntimeMovementCommand.StopCompletely);

public bool BeginJump(float power) => _automation.IsAvailable && _runtime.MovementOwner.BeginJump(power);

public bool TryGetObjectState(uint objectId, out uint state)
{
    if (_runtime.EntityObjects.Entities.TryGetActive(objectId, out RuntimeEntityRecord record))
    {
        state = record.RawPhysicsState;
        return true;
    }

    state = 0;
    return false;
}

// Doors play On (0x4000000B) to open and Off (0x4000000C) to close; the record keeps the low 16 bits.
public bool TryGetObjectMotionOn(uint objectId, out bool isOn)
{
    isOn = false;
    if (!_runtime.EntityObjects.Entities.TryGetActive(objectId, out RuntimeEntityRecord record)
        || record.Snapshot.MotionState?.ForwardCommand is not { } command)
        return false;
    if (command is not (0x000B or 0x000C))
        return false;
    isOn = command == 0x000B;
    return true;
}
```

If `RuntimeMovementCommand` needs `using AcDream.Runtime;`, it is already imported at the top of the file.

Run the Verify commands → all pass.

- [ ] **Step 8: Commit**

```bash
git add src/AcDream.Rynth.Host tests/AcDream.Rynth.Host.Tests tests/AcDream.App.Tests/Extensions
git commit -m "feat(rynth): RynthAi movement and door slots (autorun, heading snap, held motions, stop, jumps, door state)

Claude-Session: https://claude.ai/code/session_01X3ryhqAVUCdtME4PCQLPJi"
```

---

### Task 4: Nav3D slots drawn through the world-triangle layer

**Goal:** RynthAi's Nav3D rings, lines and triangles are buffered per host tick with RynthCore's caps and landblock-anchored commit, turned into faded-wall triangles, and drawn in the world through one `IPluginWorldTriangleLayer`.

**Files:**
- Create: `src/AcDream.Rynth.Host/RynthNav3D.cs`
- Create: `src/AcDream.Rynth.Host/RynthApiTable.Nav3D.cs`
- Modify: `src/AcDream.Rynth.Host/RynthApiTable.cs` (constructor takes `IPluginWorldTriangleLayer? nav3DLayer = null`; `Nav3D` property; `BindNav3DSlots();`; dispose the layer)
- Modify: `src/AcDream.Rynth.Host/RynthHost.cs` (constructor parameter `IPluginWorldTriangleLayer? nav3DLayer = null` passed to the table; `Tick` begins/commits)
- Modify: `src/AcDream.ClientExtensions/ClientExtensionContext.cs` (add `WorldTriangles`)
- Modify: `src/AcDream.App/Extensions/ClientExtensionHost.cs:20-41` (parameter + context)
- Modify: `src/AcDream.App/Rendering/GameWindow.cs:540` (pass `worldTriangles: WorldTriangles`)
- Modify: `src/AcDream.Rynth.Host/RynthClientExtension.cs` (create the layer from the context, pass it to `RynthHost`)
- Test: `tests/AcDream.Rynth.Host.Tests/RynthNav3DTests.cs` (create)
- Test: `tests/AcDream.Rynth.Host.Tests/RynthApiTableTests.cs` (Nav3D slot test)

**Acceptance Criteria:**
- [ ] Caps: the 257th ring, 513th line and 8193rd triangle in one tick are ignored.
- [ ] One ring → 128 triangles (64 wall, 64 rim); its wall's bottom corners are the ring colour and its top corners `(c & 0x00FFFFFF) | 0x10000000`; `AddRing` walls are 0.5 m high, `AddRingEx` with height 0.05 makes a 0.1 m wall.
- [ ] One line → 4 triangles (2 ground, 2 wall); a line shorter than 0.001 m in XZ → 0 triangles.
- [ ] A Nav3D triangle passes through as one triangle in its colour.
- [ ] Corner (x, y, z) in the anchor cell C becomes `RuntimeNavigationPositions.FromCellLocal(C, (x, z, y))`.
- [ ] A tick whose player landblock changed between `BeginTick` and `CommitTick` is dropped (the layer keeps the previous set); with no player pose the layer is set empty once.
- [ ] Two identical ticks call `SetTriangles` once.
- [ ] Table: the five Nav3D slots are non-zero and fill the buffer; a throwing view logs and does nothing.
- [ ] `ClientExtensionContext.WorldTriangles` reaches `RynthClientExtension`; a null `CreateLayer()` (no window) is fine.

**Verify:** `~/.dotnet/dotnet test tests/AcDream.Rynth.Host.Tests` → all pass; `~/.dotnet/dotnet test tests/AcDream.App.Tests --filter "FullyQualifiedName~Rynth|FullyQualifiedName~ClientExtension"` → all pass; `~/.dotnet/dotnet build src/AcDream.App` → 0 warnings

**Steps:**

- [ ] **Step 1: Write the failing Nav3D tests** (create `tests/AcDream.Rynth.Host.Tests/RynthNav3DTests.cs`)

```csharp
using System.Numerics;
using AcDream.Plugin.Abstractions;
using AcDream.Runtime.Navigation;
using AcDream.Rynth.Host;

namespace AcDream.Rynth.Host.Tests;

public sealed class RynthNav3DTests
{
    private const uint Cell = 0xA9B40001;
    private const uint Red = 0xC0FF0000;
    private readonly FakeGameView _view = new() { Pose = (Cell, new Vector3(50, 60, 10), Quaternion.Identity) };
    private readonly FakeTriangleLayer _layer = new();

    private RynthNav3D Nav() => new(_view, _layer);

    [Fact]
    public void ARingIsAFadingWallAndAGroundBand()
    {
        RynthNav3D nav = Nav();
        nav.BeginTick();
        nav.AddRing(10, 2, 20, radius: 3, thickness: 0.5f, Red);
        nav.CommitTick();

        PluginWorldTriangle[] set = Assert.Single(_layer.Sets);
        Assert.Equal(128, set.Length);
        var top = new PluginColor(255, 0, 0, 0x10);
        var solid = new PluginColor(255, 0, 0, 0xC0);
        Assert.Equal(64, set.Count(t => t.Color == solid && (t.ColorB == top || t.ColorC == top)));
        Assert.Equal(64, set.Count(t => t.Color == solid && t.ColorB is null && t.ColorC is null));
        double maxElevation = set.SelectMany(t => new[] { t.A, t.B, t.C }).Max(p => p.Elevation);
        Assert.Equal(RynthNav3DTests.Elevation(2 + RynthNav3D.DefaultRingHeight), maxElevation, precision: 6);
    }

    [Fact]
    public void ARingExKeepsATenthOfAMetreWallAtLeast()
    {
        RynthNav3D nav = Nav();
        nav.BeginTick();
        nav.AddRingEx(10, 2, 20, radius: 3, thickness: 0.5f, height: 0.05f, Red);
        nav.CommitTick();

        double maxElevation = Assert.Single(_layer.Sets).SelectMany(t => new[] { t.A, t.B, t.C }).Max(p => p.Elevation);
        Assert.Equal(Elevation(2.1f), maxElevation, precision: 6);
    }

    [Fact]
    public void ALineIsAGroundStripAndALowWall()
    {
        RynthNav3D nav = Nav();
        nav.BeginTick();
        nav.AddLine(0, 1, 0, 10, 1, 0, thickness: 0.2f, Red);
        nav.AddLine(5, 1, 5, 5.0001f, 1, 5, thickness: 0.2f, Red);
        nav.CommitTick();

        Assert.Equal(4, Assert.Single(_layer.Sets).Length);
    }

    [Fact]
    public void ATrianglePassesThroughInTheAnchorCellsFrame()
    {
        RynthNav3D nav = Nav();
        nav.BeginTick();
        nav.AddTriangle(1, 2, 3, 4, 5, 6, 7, 8, 9, Red);
        nav.CommitTick();

        PluginWorldTriangle triangle = Assert.Single(Assert.Single(_layer.Sets));
        Assert.Equal(RuntimeNavigationPositions.FromCellLocal(Cell, new Vector3(1, 3, 2)), triangle.A);
        Assert.Equal(RuntimeNavigationPositions.FromCellLocal(Cell, new Vector3(4, 6, 5)), triangle.B);
        Assert.Equal(RuntimeNavigationPositions.FromCellLocal(Cell, new Vector3(7, 9, 8)), triangle.C);
        Assert.Equal(new PluginColor(255, 0, 0, 0xC0), triangle.Color);
    }

    [Fact]
    public void TheCapsAreRynthCoresAndATickStartsEmpty()
    {
        RynthNav3D nav = Nav();
        nav.BeginTick();
        for (int i = 0; i < RynthNav3D.MaxTriangles + 5; i++)
            nav.AddTriangle(0, 0, 0, 1, 0, 0, 0, 0, i, Red);
        for (int i = 0; i < RynthNav3D.MaxLines + 5; i++)
            nav.AddLine(0, 0, i, 1, 0, i, 0.1f, Red);
        for (int i = 0; i < RynthNav3D.MaxRings + 5; i++)
            nav.AddRing(i, 0, 0, 1, 0.1f, Red);
        nav.CommitTick();
        Assert.Equal(RynthNav3D.MaxTriangles + RynthNav3D.MaxLines * 4 + RynthNav3D.MaxRings * 128, _layer.Sets[^1].Length);

        nav.BeginTick();
        nav.CommitTick();
        Assert.Empty(_layer.Sets[^1]);
    }

    [Fact]
    public void ATickThatCrossedALandblockIsDropped()
    {
        RynthNav3D nav = Nav();
        nav.BeginTick();
        nav.AddTriangle(1, 2, 3, 4, 5, 6, 7, 8, 9, Red);
        nav.CommitTick();

        nav.BeginTick();
        nav.AddTriangle(1, 2, 3, 4, 5, 6, 7, 8, 10, Red);
        _view.Pose = (0xA9B50001, new Vector3(50, 1, 10), Quaternion.Identity);
        nav.CommitTick();

        Assert.Single(_layer.Sets);
    }

    [Fact]
    public void AnUnchangedTickIsNotSentAgainAndNoPoseEmptiesTheLayerOnce()
    {
        RynthNav3D nav = Nav();
        for (int tick = 0; tick < 2; tick++)
        {
            nav.BeginTick();
            nav.AddTriangle(1, 2, 3, 4, 5, 6, 7, 8, 9, Red);
            nav.CommitTick();
        }
        Assert.Single(_layer.Sets);

        _view.Pose = null;
        for (int tick = 0; tick < 2; tick++)
        {
            nav.BeginTick();
            nav.AddTriangle(1, 2, 3, 4, 5, 6, 7, 8, 9, Red);
            nav.CommitTick();
        }
        Assert.Equal(2, _layer.Sets.Count);
        Assert.Empty(_layer.Sets[^1]);
    }

    [Fact]
    public void WithoutALayerNothingIsDrawnAndNothingFails()
    {
        var nav = new RynthNav3D(_view, null);
        nav.BeginTick();
        nav.AddRing(1, 1, 1, 1, 0.1f, Red);
        nav.CommitTick();
        nav.Dispose();
    }

    [Fact]
    public void DisposingDisposesTheLayer()
    {
        RynthNav3D nav = Nav();
        nav.Dispose();
        Assert.True(_layer.Disposed);
    }

    private static double Elevation(float metres) =>
        RuntimeNavigationPositions.FromCellLocal(Cell, new Vector3(0, 0, metres)).Elevation;

    private sealed class FakeTriangleLayer : IPluginWorldTriangleLayer
    {
        public List<PluginWorldTriangle[]> Sets { get; } = new();
        public bool Disposed { get; private set; }
        public void SetTriangles(IReadOnlyList<PluginWorldTriangle> triangles) => Sets.Add([.. triangles]);
        public void Dispose() => Disposed = true;
    }
}
```

`FakeGameView.Pose` already exists (`(uint Cell, Vector3 Origin, Quaternion Orientation)?`) and its `TryGetPlayerPose` returns false when it is null (`FakeGameView.cs:94-99`).

Run: `~/.dotnet/dotnet test tests/AcDream.Rynth.Host.Tests --filter "FullyQualifiedName~RynthNav3DTests"` → build error (`RynthNav3D` missing).

- [ ] **Step 2: Implement `RynthNav3D`** (create `src/AcDream.Rynth.Host/RynthNav3D.cs`)

```csharp
using System.Numerics;
using AcDream.Plugin.Abstractions;
using AcDream.Runtime.Navigation;

namespace AcDream.Rynth.Host;

/// <summary>
/// RynthCore's Nav3D buffer: rings, lines and triangles a plugin adds during a
/// host tick, in metres from the player's landblock (X east, Y up, Z north),
/// shaped as RynthCore's DX9 backend shapes them and drawn as world triangles.
/// A tick in which the player crossed into another landblock is dropped, as
/// RynthCore's CommitFrame drops it.
/// </summary>
internal sealed class RynthNav3D(IRynthGameView view, IPluginWorldTriangleLayer? layer) : IDisposable
{
    internal const int MaxRings = 256, MaxLines = 512, MaxTriangles = 8192;
    internal const int RingSegments = 32;
    internal const float DefaultRingHeight = 0.5f;
    private const float MinimumRingHeight = 0.1f, MinimumInnerRadius = 0.01f, LineWallHeight = 0.15f;

    private readonly record struct Ring(Vector3 Centre, float Radius, float Thickness, float Height, uint Argb);
    private readonly record struct Line(Vector3 From, Vector3 To, float Thickness, uint Argb);
    private readonly record struct Face(Vector3 A, Vector3 B, Vector3 C, uint Argb);

    private readonly List<Ring> _rings = [];
    private readonly List<Line> _lines = [];
    private readonly List<Face> _faces = [];
    private readonly List<PluginWorldTriangle> _built = [];
    private PluginWorldTriangle[] _shown = [];
    private bool _everShown;
    private uint _anchorCell;

    /// <summary>Starts a tick: empties the buffer and anchors it to the player's cell.</summary>
    public void BeginTick()
    {
        Clear();
        _anchorCell = view.TryGetPlayerPose(out uint cell, out _, out _) ? cell : 0;
    }

    /// <summary>RynthCore's Nav3DClear: empties what this tick has added.</summary>
    public void Clear()
    {
        _rings.Clear();
        _lines.Clear();
        _faces.Clear();
    }

    public void AddRing(float x, float y, float z, float radius, float thickness, uint argb) =>
        AddRingEx(x, y, z, radius, thickness, DefaultRingHeight, argb);

    public void AddRingEx(float x, float y, float z, float radius, float thickness, float height, uint argb)
    {
        if (_rings.Count < MaxRings)
            _rings.Add(new Ring(new Vector3(x, y, z), radius, thickness, height, argb));
    }

    public void AddLine(float x1, float y1, float z1, float x2, float y2, float z2, float thickness, uint argb)
    {
        if (_lines.Count < MaxLines)
            _lines.Add(new Line(new Vector3(x1, y1, z1), new Vector3(x2, y2, z2), thickness, argb));
    }

    public void AddTriangle(float x1, float y1, float z1, float x2, float y2, float z2, float x3, float y3, float z3, uint argb)
    {
        if (_faces.Count < MaxTriangles)
            _faces.Add(new Face(new Vector3(x1, y1, z1), new Vector3(x2, y2, z2), new Vector3(x3, y3, z3), argb));
    }

    /// <summary>Ends a tick: shows what it added, unless the player changed landblock during it.</summary>
    public void CommitTick()
    {
        if (!view.TryGetPlayerPose(out uint cell, out _, out _) || _anchorCell == 0)
        {
            Show([]);
            return;
        }

        if (cell >> 16 != _anchorCell >> 16)
            return;

        _built.Clear();
        foreach (Face face in _faces)
            _built.Add(Triangle(face.A, face.B, face.C, face.Argb));
        foreach (Line line in _lines)
            BuildLine(line);
        foreach (Ring ring in _rings)
            BuildRing(ring);
        Show(_built);
    }

    public void Dispose() => layer?.Dispose();

    private void Show(List<PluginWorldTriangle> triangles)
    {
        if (_everShown && triangles.Count == _shown.Length && triangles.SequenceEqual(_shown))
            return;
        _shown = [.. triangles];
        _everShown = true;
        layer?.SetTriangles(_shown);
    }

    private void BuildRing(Ring ring)
    {
        uint top = Faded(ring.Argb);
        float height = MathF.Max(ring.Height, MinimumRingHeight);
        float inner = MathF.Max(MinimumInnerRadius, ring.Radius - ring.Thickness);
        for (int i = 0; i < RingSegments; i++)
        {
            float a0 = 2f * MathF.PI * i / RingSegments, a1 = 2f * MathF.PI * (i + 1) / RingSegments;
            Vector3 o0 = ring.Centre + new Vector3(ring.Radius * MathF.Cos(a0), 0f, ring.Radius * MathF.Sin(a0));
            Vector3 o1 = ring.Centre + new Vector3(ring.Radius * MathF.Cos(a1), 0f, ring.Radius * MathF.Sin(a1));
            Vector3 up = new(0f, height, 0f);
            _built.Add(Triangle(o0, o1, o1 + up, ring.Argb, ring.Argb, top));
            _built.Add(Triangle(o0, o1 + up, o0 + up, ring.Argb, top, top));
            Vector3 i0 = ring.Centre + new Vector3(inner * MathF.Cos(a0), 0f, inner * MathF.Sin(a0));
            Vector3 i1 = ring.Centre + new Vector3(inner * MathF.Cos(a1), 0f, inner * MathF.Sin(a1));
            _built.Add(Triangle(o0, o1, i1, ring.Argb));
            _built.Add(Triangle(o0, i1, i0, ring.Argb));
        }
    }

    private void BuildLine(Line line)
    {
        float dx = line.To.X - line.From.X, dz = line.To.Z - line.From.Z;
        float length = MathF.Sqrt(dx * dx + dz * dz);
        if (length < 0.001f)
            return;
        var side = new Vector3(-dz / length * line.Thickness, 0f, dx / length * line.Thickness);
        _built.Add(Triangle(line.From - side, line.From + side, line.To + side, line.Argb));
        _built.Add(Triangle(line.From - side, line.To + side, line.To - side, line.Argb));
        uint top = Faded(line.Argb);
        Vector3 up = new(0f, LineWallHeight, 0f);
        _built.Add(Triangle(line.From, line.To, line.To + up, line.Argb, line.Argb, top));
        _built.Add(Triangle(line.From, line.To + up, line.From + up, line.Argb, top, top));
    }

    private static uint Faded(uint argb) => (argb & 0x00FFFFFFu) | 0x10000000u;

    private PluginWorldTriangle Triangle(Vector3 a, Vector3 b, Vector3 c, uint argb) =>
        new(Position(a), Position(b), Position(c), Colour(argb));

    private PluginWorldTriangle Triangle(Vector3 a, Vector3 b, Vector3 c, uint argbA, uint argbB, uint argbC) =>
        new PluginWorldTriangle(Position(a), Position(b), Position(c), Colour(argbA))
        {
            ColorB = argbB == argbA ? null : Colour(argbB),
            ColorC = argbC == argbA ? null : Colour(argbC),
        };

    // RynthCore's frame is Y up; the client's cell frame is Z up.
    private PluginNavigationPosition Position(Vector3 rynth) =>
        RuntimeNavigationPositions.FromCellLocal(_anchorCell, new Vector3(rynth.X, rynth.Z, rynth.Y));

    private static PluginColor Colour(uint argb) =>
        new((byte)(argb >> 16), (byte)(argb >> 8), (byte)argb, (byte)(argb >> 24));
}
```

Check against `ARingIsAFadingWallAndAGroundBand`: each wall triangle has bottom-left corner A in the solid colour and at least one top corner in the faded colour; the first wall triangle has `ColorB = null` (same as A) and `ColorC = top`, the second `ColorB = top`, `ColorC = top`; rim triangles have both null. That gives 64 + 64.

Run: `~/.dotnet/dotnet test tests/AcDream.Rynth.Host.Tests --filter "FullyQualifiedName~RynthNav3DTests"` → all pass.

- [ ] **Step 3: Table slots + host tick** 

Test (add to `RynthApiTableTests`):

```csharp
[Fact]
public void Nav3DSlotsFillTheBuffer()
{
    var view = new FakeGameView { Pose = (0xA9B40001, new Vector3(50, 60, 10), Quaternion.Identity) };
    using var table = new RynthApiTable(view, _log.Add);
    var host = new RynthCoreHost(*table.Native);
    Assert.True(host.HasNav3D && host.HasNav3DTriangle && host.HasNav3DRingHeight);

    table.Nav3D.BeginTick();
    host.Nav3DAddRing(1, 2, 3, 1, 0.1f, 0xFFFF0000);
    host.Nav3DAddRingEx(1, 2, 3, 1, 0.1f, 2f, 0xFFFF0000);
    host.Nav3DAddLine(0, 0, 0, 5, 0, 0, 0.1f, 0xFFFF0000);
    host.Nav3DAddTriangle(0, 0, 0, 1, 0, 0, 0, 0, 1, 0xFFFF0000);
    Assert.Equal((2, 1, 1), table.Nav3D.Counts);
    host.Nav3DClear();
    Assert.Equal((0, 0, 0), table.Nav3D.Counts);
}
```

Add to `RynthNav3D`: `internal (int Rings, int Lines, int Triangles) Counts => (_rings.Count, _lines.Count, _faces.Count);`. (The SDK's names are `HasNav3D` (clear + ring + line), `HasNav3DTriangle` and `HasNav3DRingHeight`.)

Create `src/AcDream.Rynth.Host/RynthApiTable.Nav3D.cs`:

```csharp
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace AcDream.Rynth.Host;

// The table's Nav3D slots (RynthAi sub-project #4): they only fill the tick's buffer.
public sealed unsafe partial class RynthApiTable
{
    private void BindNav3DSlots()
    {
        Native->Nav3DClearFn = (IntPtr)(delegate* unmanaged[Cdecl]<void>)&Nav3DClear;
        Native->Nav3DAddRingFn = (IntPtr)(delegate* unmanaged[Cdecl]<float, float, float, float, float, uint, void>)&Nav3DAddRing;
        Native->Nav3DAddRingExFn = (IntPtr)(delegate* unmanaged[Cdecl]<float, float, float, float, float, float, uint, void>)&Nav3DAddRingEx;
        Native->Nav3DAddLineFn = (IntPtr)(delegate* unmanaged[Cdecl]<float, float, float, float, float, float, float, uint, void>)&Nav3DAddLine;
        Native->Nav3DAddTriangleFn = (IntPtr)(delegate* unmanaged[Cdecl]<float, float, float, float, float, float, float, float, float, uint, void>)&Nav3DAddTriangle;
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static void Nav3DClear()
    {
        RynthApiTable? table = _current;
        if (table is null)
            return;
        try { table.Nav3D.Clear(); }
        catch (Exception error) { table.Fail(nameof(Nav3DClear), error); }
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static void Nav3DAddRing(float x, float y, float z, float radius, float thickness, uint argb)
    {
        RynthApiTable? table = _current;
        if (table is null)
            return;
        try { table.Nav3D.AddRing(x, y, z, radius, thickness, argb); }
        catch (Exception error) { table.Fail(nameof(Nav3DAddRing), error); }
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static void Nav3DAddRingEx(float x, float y, float z, float radius, float thickness, float height, uint argb)
    {
        RynthApiTable? table = _current;
        if (table is null)
            return;
        try { table.Nav3D.AddRingEx(x, y, z, radius, thickness, height, argb); }
        catch (Exception error) { table.Fail(nameof(Nav3DAddRingEx), error); }
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static void Nav3DAddLine(float x1, float y1, float z1, float x2, float y2, float z2, float thickness, uint argb)
    {
        RynthApiTable? table = _current;
        if (table is null)
            return;
        try { table.Nav3D.AddLine(x1, y1, z1, x2, y2, z2, thickness, argb); }
        catch (Exception error) { table.Fail(nameof(Nav3DAddLine), error); }
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static void Nav3DAddTriangle(float x1, float y1, float z1, float x2, float y2, float z2, float x3, float y3, float z3, uint argb)
    {
        RynthApiTable? table = _current;
        if (table is null)
            return;
        try { table.Nav3D.AddTriangle(x1, y1, z1, x2, y2, z2, x3, y3, z3, argb); }
        catch (Exception error) { table.Fail(nameof(Nav3DAddTriangle), error); }
    }
}
```

`RynthApiTable` constructor: add a last parameter `AcDream.Plugin.Abstractions.IPluginWorldTriangleLayer? nav3DLayer = null`, set `Nav3D = new RynthNav3D(view, nav3DLayer);` before the `Bind*` calls, add `internal RynthNav3D Nav3D { get; }`, call `BindNav3DSlots();` after `BindMovementSlots();`, and call `Nav3D.Dispose();` in `Dispose` before the native memory is freed.

`RynthHost`: constructor gains `AcDream.Plugin.Abstractions.IPluginWorldTriangleLayer? nav3DLayer = null` (last parameter) passed to `new RynthApiTable(view, _log, scriptWindows, nav3DLayer)`. `Tick`:

```csharp
/// <summary>Delivers the frame's queued events, then ticks each plugin inside one Nav3D frame, as RynthCore's frame does.</summary>
public void Tick()
{
    Bridge.DeliverQueued();
    _table.Holds.Sync();
    _table.Nav3D.BeginTick();
    foreach (RynthPlugin plugin in _plugins)
        plugin.Tick();
    _table.Nav3D.CommitTick();
}
```

Run: `~/.dotnet/dotnet test tests/AcDream.Rynth.Host.Tests` → all pass.

- [ ] **Step 4: Wire the layer from the client**

`ClientExtensionContext.cs` — add after `Automation`:

```csharp
/// <summary>The client's world-triangle layers; null in a host that draws no world.</summary>
public AcDream.Plugin.Abstractions.IPluginWorldTriangles? WorldTriangles { get; init; }
```

`ClientExtensionHost.Attach` — add parameter `IPluginWorldTriangles? worldTriangles = null` (last) and `WorldTriangles = worldTriangles,` in the context initialiser.

`GameWindow.cs:540` — append `, worldTriangles: WorldTriangles` to the `_extensions.Attach(...)` call.

`RynthClientExtension.Attach` — where `_host = new RynthHost(view, context.Log, [...], ...)` is built, pass `nav3DLayer: context.WorldTriangles?.CreateLayer()` as the new last argument. The host owns the layer from then on (the table disposes it).

Add an App test in `tests/AcDream.App.Tests/Extensions/RynthClientExtensionTests.cs` following that file's existing attach test: attach with a context whose `WorldTriangles` is a `PluginWorldTriangleStore`, tick once with a logged-out runtime, dispose, and assert `store.Layers` is empty after dispose (the layer was created and let go). Read the file's first attach test and copy its context setup exactly, adding `WorldTriangles = store`.

Run the Verify commands → all pass, and `~/.dotnet/dotnet build src/AcDream.App` → 0 warnings.

- [ ] **Step 5: Commit**

```bash
git add src/AcDream.Rynth.Host src/AcDream.ClientExtensions src/AcDream.App/Extensions/ClientExtensionHost.cs src/AcDream.App/Rendering/GameWindow.cs tests/AcDream.Rynth.Host.Tests tests/AcDream.App.Tests/Extensions
git commit -m "feat(rynth): Nav3D rings, lines and triangles drawn as world triangles

Claude-Session: https://claude.ai/code/session_01X3ryhqAVUCdtME4PCQLPJi"
```

---

### Task 5: Live check on local ACE (user-run)

**Goal:** The user confirms on the local ACE server (10.10.20.20) that RynthAi navigation walks a route and its Nav3D markers draw, alongside the still-pending #2/#3 live checks.

**Files:**
- Modify: `docs/superpowers/plans/2026-10-18-rynth-ai-nav.md` (record results under a Status line at the top)

**Acceptance Criteria:**
- [ ] The user has said they are available before any client is launched.
- [ ] With a short outdoor Circular route loaded in RynthAi: nav turns toward the first point, autoruns, arrives within reach and moves to the next point (user observes, and the log has no "threw" lines).
- [ ] Nav3D rings and route legs show at the route points, fade toward the top, and hills in front hide them.
- [ ] Turning nav off stops the character (autorun off).
- [ ] A door on a route opens through RynthAi (door state read).
- [ ] A Recall point (e.g. Lifestone Recall) portals and nav resumes after arrival.
- [ ] Results (pass/fail per item, any log lines) are written into this plan's Status line.

**Verify:** user's observations recorded in the plan Status line; `grep -c "threw" <client log>` → 0 for the movement and Nav3D slot names

**Steps:**

- [ ] **Step 1:** Ask the user whether they are available for a live check. Do not launch anything until they say yes.
- [ ] **Step 2:** Build Release: `~/.dotnet/dotnet build src/AcDream.App -c Release`.
- [ ] **Step 3:** Launch per the live-testing memory notes (source `~/OpenAC-dev/dev-client.env` inside a script without echoing secrets, `ACDREAM_LIVE=1`, scratch `ACDREAM_ROOT_DIR`, `ACDREAM_DAT_DIR=/Users/davidsmith/AsheronsCall`, `ACDREAM_PAK_PATH`, `DYLD_FALLBACK_LIBRARY_PATH=/opt/homebrew/lib`), then `~/.dotnet/dotnet src/AcDream.App/bin/Release/net10.0/AcDream.App.dll /Users/davidsmith/AsheronsCall`.
- [ ] **Step 4:** Walk the user through the acceptance items, plus the #2/#3 items from `docs/superpowers/plans/2026-10-18-rynth-ai-combat.md` Task 5 if they want to do them in the same session.
- [ ] **Step 5:** Record results in the Status line at the top of this plan and commit:

```bash
git add docs/superpowers/plans/2026-10-18-rynth-ai-nav.md
git commit -m "docs(rynth): #4 live check results

Claude-Session: https://claude.ai/code/session_01X3ryhqAVUCdtME4PCQLPJi"
```
