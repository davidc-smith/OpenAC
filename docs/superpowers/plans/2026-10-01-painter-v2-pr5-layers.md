# Painter v2 — PR 5: Layering Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers-extended-cc:subagent-driven-development (recommended) or superpowers-extended-cc:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** A plugin can put a canvas over every window as well as under them, and order its own canvases, while windows, pre-game screens, dialogs and tooltips each stay in a defined band of the interface's stacking order instead of relying on integer overflow and unstable ties.

**Architecture:** The root's children get bands: windows (0 up to 1,000,000,000), a single z-order for the layer of canvases drawn above windows, screens, then dialogs and tooltips, with `int.MaxValue` pinned. `BringToFront(e)` raises within the band `e` is already in; `BringToFront(e, band)` moves it into one. Dialogs and tooltips raise into their band, the pre-game screens into theirs. Plugin canvases live in two stacks: the existing world layer under the overlay host, and a new click-through layer on the root at the canvas z-order. Each stack holds one group per plugin, in the order their first canvas mounted; a group ranks its canvases by `(ZOrder, registration)` every tick and sets each element's z-order to its rank, so no two are ever equal. The UI renderer gains a third draw layer: from the canvases over windows upwards, everything is drawn after every window, text and rectangles included. No canvas answers the hit-test while a drag is in progress. No shader changes.

**Tech Stack:** C# / .NET 10, xUnit, the repository's `RecordingGpuDevice`.

**Spec:** `docs/superpowers/specs/2026-09-30-plugin-painter-v2-design.md` (section 8, and the PR 5 entries under "Plan-time corrections", which supersede section 8 where they differ).

**Checked:** this plan's code was applied to upstream `main` (`bbc83275`) in a throwaway worktree before hand-off, one commit per task. Every "replace this" anchor was checked to exist verbatim in `bbc83275` or in the previous task's result. It built with 0 warnings, and every new test passed, along with the full portable suite (apart from the baseline failures listed under Global Constraints, which fail identically on clean `bbc83275`). Each fix was also removed once to see its new tests fail (the band callers, the drag pass-through, the drain order). The result was then merged into fork `main` (`450dd2b9`) in a scratch worktree, with the resolutions given in Task 8; the merge built with 0 warnings and passed the full portable suite and the `Lane=Vulkan` canvas tests.

## Global Constraints

- Branch `painter-v2/layers`, created from upstream `main` at `bbc83275` (`upstream/main`; fork `main` contains it). PR 5 depends on none of PRs 0–4, so the branch can go upstream as one topic. Nothing under `docs/superpowers/` goes on a code branch.
- `AcDream.Plugin.Abstractions` stays additive: a new `PluginCanvasLayer` enum, two `init` properties on `PluginCanvasDescriptor` (`Layer`, default `World`; `ZOrder`, default 0), a default member `int ZOrder { get => 0; set { } }` on `IPluginCanvas`, and `ZOrder` on `NoOpPluginCanvas`. Every public member has XML docs; an undocumented member fails the build.
- Exact values (in `UiBands`): `WindowsCeiling = 1_000_000_000` (exclusive), `CanvasesAboveWindows = WindowsCeiling`, `ScreensFloor = CanvasesAboveWindows + 1`, `DialogsFloor = 1_500_000_000`, `Pinned = int.MaxValue`, `UpperRenderLayerFloor = CanvasesAboveWindows`. The window band's entry floor is `UiOverlayZOrder.WindowFloor` (0); anything below it is still in the window band and keeps its value until raised.
- Order, bottom to top: world and world labels → `World` canvases (overlay band, z −9000) → windows → `AboveWindows` canvases → screens → dialogs and tooltips → menus and drag ghost (overlay pass) → pinned children.
- Within a stack, plugin groups take z-order 0, 1, 2, … in the order each plugin's first canvas mounted there; inside a group each canvas's z-order is its rank by `(IPluginCanvas.ZOrder, registration id)`.
- With no `AboveWindows` canvas mounted, everything draws exactly as before, except that dialogs, tooltips, screens and pinned children now draw in the upper layer, so a window's rectangles and bitmap-font text no longer show through them.
- No shader source or SPIR-V changes.
- Commit subjects: `plugin api: …`, `plugin canvas: …`, `ui: …`, `docs: …`. Every commit message ends with `Claude-Session: https://claude.ai/code/session_01EtugJHhFMqGXY2V6Lz2ZFB`.
- Environment for every command: `export DOTNET_ROOT=$HOME/.dotnet PATH=$HOME/.dotnet:$PATH`. `Lane=Vulkan` commands also need `DYLD_FALLBACK_LIBRARY_PATH=/opt/homebrew/lib`.
- Builds on this Mac rewrite `src/AcDream.Launcher/packages.neutral.lock.json`, and an unlocked restore touches `packages.osx-arm64.lock.json` files. Never stage a `packages.*.lock.json`. Stage files by explicit path, and run `git checkout -- '*.lock.json'` before pushing.
- The first solution build in a fresh worktree can fail once with `NETSDK1047 … AcDream.Bake/obj/project.assets.json doesn't have a target for 'net10.0/osx-arm64'`, a restore race. Build again; the second build is clean.
- Portable gate filter: `Lane!=InstalledDat&Lane!=PreparedPackage&Lane!=Live&Lane!=Manual&Lane!=Timing&Lane!=Windows&Lane!=Linux&Lane!=MacOS&Lane!=Unix&Lane!=Vulkan&Lane!=SystemFont&Purpose!=Diagnostic&Status!=KnownFailure`.
- Baseline failures on this Mac, which fail identically on clean `bbc83275` and are not this PR's: run the full portable suite with `TMPDIR=/tmp/` (the default temp path makes Unix-socket paths longer than 104 characters). Even then these fail: `AcDream.App.Tests` `GraphicalPluginSessionTests.ReloadCommandLoadsAFreshCopyAndTheOldOneLeavesMemory`, `GameWindowRenderLeafCompositionTests.PaperdollComposition_SkipsEitherMissingOptionalUiSurface` and `LiveEntityNetworkUpdateControllerForcePositionWiringTests.CommittedOrDeferredCellReturnsBeforeReachingTheGenericTail`; three `AcDream.RenderPackValidator.Tests` `RenderPackValidatorCommandTests.External*`; `AcDream.Headless.Tests` `HeadlessSessionIsolationTests.ThirtySessionMixedWorkloadMaintainsIsolationAndConverges`; and 2–6 `AcDream.HostParity.Tests` `Peer*ParityTests`, which vary run to run. Record the baseline set in Task 0 and compare against it; do not try to fix them here.
- The unit tests must run outside the command sandbox, or the peer and pipe tests fail on socket permissions too.

**User decisions (already made):**
- "One spec, a PR series"; this is PR 5 (layers), discussed and agreed 2026-09-30.
- Base: "Upstream main (Recommended)" (2026-10-01): branch from upstream `main` so PR 5 can go upstream as one topic; "afterwards it's merged into fork main with a merge commit, as PR 3 was", with the conflict resolutions checked by a scratch merge (Task 8).
- Pre-game screens: "Screens band (Recommended)" (2026-10-01): "Order: windows → AboveWindows canvases → screens → dialogs/tooltips → menus/ghost → int.MaxValue", so canvases stay hidden behind pre-game screens while dialogs and tooltips show above them.
- Branches are pushed to `origin`. Do not open a pull request without asking which repository it goes to.

---

## File Structure

| File | Change | Responsibility |
|---|---|---|
| `src/AcDream.Plugin.Abstractions/PluginCanvas.cs` | Modify | `PluginCanvasLayer`; descriptor `Layer`, `ZOrder`; `IPluginCanvas.ZOrder`; `NoOpPluginCanvas.ZOrder`. |
| `src/AcDream.App/Plugins/PluginCanvasRegistration.cs` | Modify | Keeps `ZOrder` (volatile) and exposes `Layer`. |
| `src/AcDream.Core/Plugins/ScopedPluginHost.cs` | Modify | `IndividualCanvas` forwards `ZOrder`. |
| `tests/AcDream.Plugin.Tests/PluginCanvasLayerContractTests.cs` | Create | Defaults, inert canvas, fake host, older host. |
| `tests/AcDream.Core.Tests/Plugins/ScopedUiRegistryCanvasTests.cs` | Modify | Forwarder test. |
| `tests/AcDream.App.Tests/Plugins/BufferedUiRegistryCanvasTests.cs` | Modify | Registration keeps layer and starts at the descriptor's order. |
| `src/AcDream.App/UI/UiBand.cs` | Create | `UiBand` and the `UiBands` values. |
| `src/AcDream.App/UI/RetailWindowManager.cs` | Modify | Banded `BringToFront`, compaction. |
| `src/AcDream.App/UI/UiRoot.cs` | Modify | `BringToFront(e, band)`; starts the upper draw layer. |
| `tests/AcDream.App.Tests/UI/UiBandTests.cs` | Create | Banding rules. |
| `src/AcDream.App/UI/Layout/RetailDialogFactory.cs`, `RetailTooltipPresenter.cs` | Modify | Raise into the dialogs-and-tooltips band. |
| `src/AcDream.App/UI/Layout/ConnectionUiController.cs`, `CharacterManagementUiController.cs`, `CharacterCreationUiController.cs`, `CreditsUiController.cs` | Modify | Raise into the screens band. |
| six test files under `tests/AcDream.App.Tests/UI/Layout/` | Modify | One band test per caller. |
| `src/AcDream.App/Rendering/TextRenderer.cs` | Modify | `UiDrawLayer`; three layer buffers. |
| `src/AcDream.App/UI/UiRenderContext.cs` | Modify | `BeginUpperLayer`. |
| `src/AcDream.App/UI/UiElement.cs` | Modify | `OnDrawingChild` hook. |
| `tests/AcDream.App.Tests/Rendering/TextRendererDrawLayerTests.cs` | Create | Layer order. |
| `src/AcDream.App/UI/Layout/PluginCanvasStack.cs` | Create | `PluginCanvasStack` and `PluginCanvasGroup`. |
| `src/AcDream.App/UI/Layout/UiOverlayHost.cs` | Modify | `AddLayerAboveWindows`. |
| `src/AcDream.App/UI/Layout/PluginCanvasElement.cs` | Modify | No hit while dragging. |
| `src/AcDream.App/Plugins/BufferedUiRegistry.cs` | Modify | Drain in registration order. |
| `src/AcDream.App/UI/RetailUiRuntime.cs` | Modify | Mount into the descriptor's stack. |
| `tests/AcDream.App.Tests/UI/Layout/PluginCanvasStackTests.cs` | Create | Stacking, input, modal, drag. |
| `docs/plugin-api.md`, `docs/plugin-ui-markup.md` | Modify | Layers and order; test list. |

The existing canvas test harnesses (`PluginCanvasElementTests`, `PluginCanvasPointerTests`, `PluginCanvasTeardownOrderTests`) mount elements straight onto a layer and are left as they are: an element works under any parent the size of the viewport.

---

### Task 0: Create the branch and record the baseline

**Goal:** `painter-v2/layers` from upstream `main`, building cleanly, with the baseline test failures written down.

**Files:** none

**Acceptance Criteria:**
- [ ] `git branch --show-current` prints `painter-v2/layers`
- [ ] `git rev-parse HEAD` is `bbc83275…` (upstream `main`)
- [ ] `docs/superpowers` does not exist in the working tree
- [ ] `dotnet build AcDream.slnx -c Release` reports `0 Warning(s)` and `0 Error(s)`
- [ ] The names of the portable-suite failures (run with `TMPDIR=/tmp/`) are saved to `/tmp/openac-layers/baseline-failures.txt`

**Verify:** `git branch --show-current && git merge-base --is-ancestor HEAD upstream/main && test ! -e docs/superpowers && echo ok` → `painter-v2/layers`, `ok`

**Steps:**

- [ ] **Step 1: Branch**

```bash
cd /Users/davidsmith/code/OpenAC/OpenAC
git fetch upstream && git fetch origin
git rev-parse --short upstream/main   # expect bbc83275; if upstream moved, stop and ask
git worktree add -b painter-v2/layers .worktrees/layers upstream/main
cd .worktrees/layers
git merge-base --is-ancestor HEAD upstream/main && test ! -e docs/superpowers && echo ok
```

Expected: `bbc83275`, then `ok`. Every later command runs in `/Users/davidsmith/code/OpenAC/OpenAC/.worktrees/layers` (ignored by git). The main checkout stays on `docs/painter-v2-spec`, where this plan lives.

- [ ] **Step 2: Baseline build and tests**

```bash
export DOTNET_ROOT=$HOME/.dotnet PATH=$HOME/.dotnet:$PATH
mkdir -p /tmp/openac-layers
dotnet build AcDream.slnx -c Release 2>&1 | tail -3
TMPDIR=/tmp/ dotnet test AcDream.slnx -c Release --no-build --filter "Lane!=InstalledDat&Lane!=PreparedPackage&Lane!=Live&Lane!=Manual&Lane!=Timing&Lane!=Windows&Lane!=Linux&Lane!=MacOS&Lane!=Unix&Lane!=Vulkan&Lane!=SystemFont&Purpose!=Diagnostic&Status!=KnownFailure" 2>&1 \
  | grep -E "^\s+Failed " | sed -E 's/ \[[^]]*\]$//' | sort -u > /tmp/openac-layers/baseline-failures.txt
cat /tmp/openac-layers/baseline-failures.txt
git checkout -- '*.lock.json'
```

Expected: `0 Warning(s)`, `0 Error(s)`. The failures listed are the baseline set under Global Constraints (the HostParity peer tests vary).

```json:metadata
{"files": [], "verifyCommand": "git branch --show-current && git merge-base --is-ancestor HEAD upstream/main && test ! -e docs/superpowers && echo ok", "acceptanceCriteria": ["on painter-v2/layers", "HEAD is upstream main bbc83275", "no docs/superpowers", "0 warnings", "baseline failures recorded"], "modelTier": "mechanical"}
```

---

### Task 1: Layer and ZOrder in the contract

**Goal:** A plugin can name a canvas's layer and its order among the plugin's canvases; the graphical host's registration and the scoped forwarder keep both, and every host without layers answers inertly.

**Files:**
- Modify: `src/AcDream.Plugin.Abstractions/PluginCanvas.cs`
- Modify: `src/AcDream.App/Plugins/PluginCanvasRegistration.cs`
- Modify: `src/AcDream.Core/Plugins/ScopedPluginHost.cs`
- Create: `tests/AcDream.Plugin.Tests/PluginCanvasLayerContractTests.cs`
- Modify: `tests/AcDream.Core.Tests/Plugins/ScopedUiRegistryCanvasTests.cs`
- Modify: `tests/AcDream.App.Tests/Plugins/BufferedUiRegistryCanvasTests.cs`

**Acceptance Criteria:**
- [ ] `new PluginCanvasDescriptor("map", 64, 64)` has `Layer == PluginCanvasLayer.World` and `ZOrder == 0`; `World` is 0 and `AboveWindows` is 1
- [ ] `NoOpPluginCanvas` starts at the descriptor's `ZOrder` and keeps what is set
- [ ] An `IPluginCanvas` that does not implement `ZOrder` answers 0 and ignores the set
- [ ] A `PluginCanvasRegistration` exposes the descriptor's `Layer` and starts at its `ZOrder`; a set is kept
- [ ] Setting `ZOrder` on a scoped plugin's handle reaches the host's canvas and reads back from it
- [ ] The Abstractions project builds with no missing-XML-doc warning

**Verify:** `dotnet test tests/AcDream.Plugin.Tests --filter "FullyQualifiedName~PluginCanvasLayerContractTests|FullyQualifiedName~PluginCanvasContractTests"` → `Passed!  - Failed:     0, Passed:    12`

**Steps:**

- [ ] **Step 1: Write the failing tests**

Create `tests/AcDream.Plugin.Tests/PluginCanvasLayerContractTests.cs`:

```csharp
// Copyright (c) OpenAC contributors.
// Distributed under the terms of the MIT license.

using AcDream.Plugin.Abstractions;
using AcDream.Plugin.Tests.Fixtures;

namespace AcDream.Plugin.Tests;

/// <summary>
/// Layers and stacking order are additive: a descriptor defaults to the
/// world layer at zero, the inert canvas keeps the order the plugin sets,
/// and a canvas from a host that predates layers answers zero.
/// </summary>
public sealed class PluginCanvasLayerContractTests
{
    /// <summary>A canvas written before layers existed: none of the new members.</summary>
    private sealed class OlderHostCanvas : IPluginCanvas
    {
        public string CanvasId => "old";
        public int Width => 1;
        public int Height => 1;
        public bool IsVisible { get; set; }
        public PluginCanvasAnchor Anchor { get; set; }
        public PluginPoint Offset { get; set; }
        public void Invalidate() { }
        public void Dispose() { }
    }

    [Fact]
    public void ADescriptorDefaultsToTheWorldLayerAtZero()
    {
        var descriptor = new PluginCanvasDescriptor("map", 64, 64);

        Assert.Equal(PluginCanvasLayer.World, descriptor.Layer);
        Assert.Equal(0, descriptor.ZOrder);
        Assert.Equal(0, (int)PluginCanvasLayer.World);
        Assert.Equal(1, (int)PluginCanvasLayer.AboveWindows);
    }

    [Fact]
    public void TheInertCanvasStartsAtTheDescriptorsZOrderAndKeepsWhatThePluginSets()
    {
        IPluginCanvas canvas = new NoOpPluginCanvas(
            new PluginCanvasDescriptor("hud", 8, 8) { Layer = PluginCanvasLayer.AboveWindows, ZOrder = 5 });

        Assert.Equal(5, canvas.ZOrder);
        canvas.ZOrder = -1;
        Assert.Equal(-1, canvas.ZOrder);
    }

    [Fact]
    public void TheFakeHostKeepsTheZOrder()
    {
        var host = new FakePluginHost();

        IPluginCanvas canvas = host.Ui.RegisterCanvas(
            new PluginCanvasDescriptor("map", 8, 8) { ZOrder = 2 }, _ => { });
        canvas.ZOrder = 6;

        Assert.Equal(6, canvas.ZOrder);
        Assert.False(canvas.IsAvailable);
    }

    [Fact]
    public void ACanvasFromAnOlderHostAnswersZeroAndIgnoresTheSet()
    {
        IPluginCanvas canvas = new OlderHostCanvas();

        canvas.ZOrder = 3;

        Assert.Equal(0, canvas.ZOrder);
    }
}
```

In `tests/AcDream.Core.Tests/Plugins/ScopedUiRegistryCanvasTests.cs`, give the fake canvas a `ZOrder`. Replace:

```csharp
        public Action<PluginPointerEvent>? PointerHandler { get; set; }

        public void Invalidate() => Invalidations++;
```

with:

```csharp
        public Action<PluginPointerEvent>? PointerHandler { get; set; }
        public int ZOrder { get; set; } = descriptor.ZOrder;

        public void Invalidate() => Invalidations++;
```

and insert this test directly before `public void DisposingThePluginDisposesEveryCanvasItStillHolds()`'s `[Fact]`:

```csharp
    [Fact]
    public void TheZOrderReachesTheHostsCanvasBothWays()
    {
        var inner = new FakeScopedUiRegistry();
        var scoped = new ScopedPluginHost(new StubHost(inner), "example.plugin", "Example");
        IPluginCanvas canvas = scoped.Ui.RegisterCanvas(Hud with { ZOrder = 4 }, _ => { });
        FakeCanvas hosts = Assert.Single(inner.Canvases);

        Assert.Equal(4, canvas.ZOrder);
        canvas.ZOrder = 9;
        Assert.Equal(9, hosts.ZOrder);
        hosts.ZOrder = -2;
        Assert.Equal(-2, canvas.ZOrder);
        scoped.Dispose();
    }

```

At the end of `tests/AcDream.App.Tests/Plugins/BufferedUiRegistryCanvasTests.cs`, before the class's closing brace, add:

```csharp
    [Fact]
    public void ARegistrationKeepsItsLayerAndStartsAtItsDescriptorsZOrder()
    {
        var registry = new BufferedUiRegistry();
        var registration = (PluginCanvasRegistration)registry.RegisterCanvas(
            A, Canvas("hud") with { Layer = PluginCanvasLayer.AboveWindows, ZOrder = 3 }, Paint);
        var plain = (PluginCanvasRegistration)registry.RegisterCanvas(A, Canvas("map"), Paint);

        Assert.Equal(PluginCanvasLayer.AboveWindows, registration.Layer);
        Assert.Equal(3, registration.ZOrder);
        Assert.Equal(PluginCanvasLayer.World, plain.Layer);
        Assert.Equal(0, plain.ZOrder);

        registration.ZOrder = -7;
        Assert.Equal(-7, registration.ZOrder);
        Assert.Equal(PluginCanvasLayer.AboveWindows, registration.Layer);
    }
```

- [ ] **Step 2: Run them to see them fail**

Run: `dotnet build tests/AcDream.Plugin.Tests 2>&1 | grep -c "error CS"`
Expected: a non-zero count (`PluginCanvasLayer` and `ZOrder` do not exist yet).

- [ ] **Step 3: The contract**

In `src/AcDream.Plugin.Abstractions/PluginCanvas.cs`, insert before `/// <summary>A width and a height, in pixels.</summary>`:

```csharp
/// <summary>
/// Where a canvas sits in the interface's stacking order. The layer is
/// chosen when the canvas is registered and does not change.
/// </summary>
public enum PluginCanvasLayer
{
    /// <summary>
    /// Over the world and its labels, under every window: a HUD that never
    /// hides the interface. The default.
    /// </summary>
    World = 0,

    /// <summary>
    /// Over every window, under dialogs, tooltips, menus and the item being
    /// dragged. A canvas here that takes input takes the pointer from the
    /// windows beneath its rectangle.
    /// </summary>
    AboveWindows = 1,
}

```

After the descriptor's `public bool AcceptsPointerInput { get; init; }` (the last member of `PluginCanvasDescriptor`), add:

```csharp

    /// <summary>
    /// Which layer the canvas is drawn in: <see cref="PluginCanvasLayer.World"/>,
    /// the default, under every window, or
    /// <see cref="PluginCanvasLayer.AboveWindows"/>. Fixed at registration.
    /// A host that predates layers draws every canvas in the world layer.
    /// </summary>
    public PluginCanvasLayer Layer { get; init; } = PluginCanvasLayer.World;

    /// <summary>
    /// The canvas's starting place among this plugin's canvases in the same
    /// layer: higher is drawn on top, and canvases with equal values keep the
    /// order they were registered in. It orders a plugin's own canvases
    /// only; it never lifts one plugin's canvas over another plugin's. Change
    /// it later through <see cref="IPluginCanvas.ZOrder"/>.
    /// </summary>
    public int ZOrder { get; init; }
```

Replace the start of the `IPluginCanvas` summary:

```csharp
/// <summary>
/// A rectangle the plugin paints, shown over the world and under every
/// window, taking no input unless it opted in through
/// <see cref="PluginCanvasDescriptor.AcceptsPointerInput"/>.
```

with:

```csharp
/// <summary>
/// A rectangle the plugin paints, shown over the world and under every
/// window (or, in <see cref="PluginCanvasLayer.AboveWindows"/>, over every
/// window), taking no input unless it opted in through
/// <see cref="PluginCanvasDescriptor.AcceptsPointerInput"/>.
```

At the end of `IPluginCanvas`, after `void ReleasePointer() { }`, add:

```csharp

    /// <summary>
    /// The canvas's place among this plugin's canvases in the same layer:
    /// higher is drawn on top, and equal values keep registration order.
    /// Starts at <see cref="PluginCanvasDescriptor.ZOrder"/>; set it to
    /// restack, from the next frame. A host that predates layers answers 0
    /// and ignores the set.
    /// </summary>
    int ZOrder
    {
        get => 0;
        set { }
    }
```

In `NoOpPluginCanvas`, add `ZOrder = descriptor.ZOrder;` as the constructor's last line (after `Offset = descriptor.Offset;`), and after its `ReleasePointer()` method add:

```csharp

    /// <summary>Kept so the plugin's own logic runs unchanged; nothing is stacked.</summary>
    public int ZOrder { get; set; }
```

- [ ] **Step 4: The registration and the forwarder**

In `src/AcDream.App/Plugins/PluginCanvasRegistration.cs`:
- after `private volatile bool _invalidated = true;` add `private volatile int _zOrder;`
- add `_zOrder = descriptor.ZOrder;` as the constructor's last line (after `Offset = descriptor.Offset;`)
- before `/// <summary>Whether the plugin asked for pointer input when it registered the canvas.</summary>` insert:

```csharp
    /// <summary>The layer the plugin chose when it registered the canvas.</summary>
    internal PluginCanvasLayer Layer => Descriptor.Layer;

```

- after `public void ReleasePointer() => _releasePointer?.Invoke();` add:

```csharp

    /// <summary>
    /// Read by the canvas's group in the interface every tick, which restacks
    /// the plugin's canvases when it changes.
    /// </summary>
    public int ZOrder
    {
        get => _zOrder;
        set => _zOrder = value;
    }
```

In `src/AcDream.Core/Plugins/ScopedPluginHost.cs`, in `IndividualCanvas`, after `public void ReleasePointer() => inner.ReleasePointer();` add:

```csharp

            public int ZOrder
            {
                get => inner.ZOrder;
                set => inner.ZOrder = value;
            }
```

- [ ] **Step 5: Run the tests**

```bash
dotnet test tests/AcDream.Plugin.Tests --filter "FullyQualifiedName~PluginCanvasLayerContractTests|FullyQualifiedName~PluginCanvasContractTests" 2>&1 | grep -E "Passed!|Failed!"
dotnet test tests/AcDream.Core.Tests --filter "FullyQualifiedName~ScopedUiRegistryCanvasTests" 2>&1 | grep -E "Passed!|Failed!"
dotnet test tests/AcDream.App.Tests --filter "FullyQualifiedName~BufferedUiRegistryCanvasTests" 2>&1 | grep -E "Passed!|Failed!"
```

Expected: `Passed: 12`, `Passed: 6`, `Passed: 12`, all with `Failed: 0`.

- [ ] **Step 6: Commit**

```bash
git add src/AcDream.Plugin.Abstractions/PluginCanvas.cs src/AcDream.App/Plugins/PluginCanvasRegistration.cs src/AcDream.Core/Plugins/ScopedPluginHost.cs tests/AcDream.Plugin.Tests/PluginCanvasLayerContractTests.cs tests/AcDream.Core.Tests/Plugins/ScopedUiRegistryCanvasTests.cs tests/AcDream.App.Tests/Plugins/BufferedUiRegistryCanvasTests.cs
git commit -m "plugin api: canvas layer and order among a plugin's canvases

Claude-Session: https://claude.ai/code/session_01EtugJHhFMqGXY2V6Lz2ZFB"
```

```json:metadata
{"files": ["src/AcDream.Plugin.Abstractions/PluginCanvas.cs", "src/AcDream.App/Plugins/PluginCanvasRegistration.cs", "src/AcDream.Core/Plugins/ScopedPluginHost.cs", "tests/AcDream.Plugin.Tests/PluginCanvasLayerContractTests.cs", "tests/AcDream.Core.Tests/Plugins/ScopedUiRegistryCanvasTests.cs", "tests/AcDream.App.Tests/Plugins/BufferedUiRegistryCanvasTests.cs"], "verifyCommand": "dotnet test tests/AcDream.Plugin.Tests --filter \"FullyQualifiedName~PluginCanvasLayerContractTests|FullyQualifiedName~PluginCanvasContractTests\"", "acceptanceCriteria": ["descriptor defaults World/0", "NoOp keeps ZOrder", "older host answers 0", "registration keeps Layer and ZOrder", "forwarder forwards ZOrder", "no missing XML docs"], "modelTier": "mechanical"}
```

---

### Task 2: Banded z-order for the root's children

**Goal:** `BringToFront` raises an element within its band (windows, screens, dialogs and tooltips), never over the canvas layer or a pinned child, and renumbers a band that fills up.

**Files:**
- Create: `src/AcDream.App/UI/UiBand.cs`
- Modify: `src/AcDream.App/UI/RetailWindowManager.cs:229-236`
- Modify: `src/AcDream.App/UI/UiRoot.cs` (`BringToFront`, around line 974)
- Create: `tests/AcDream.App.Tests/UI/UiBandTests.cs`

**Acceptance Criteria:**
- [ ] A window raised over windows at 5 and 9 gets 10 and stays below `UiBands.CanvasesAboveWindows`
- [ ] `BringToFront(e, UiBand.DialogsAndTooltips)` puts `e` at `DialogsFloor` or above; a later plain `BringToFront(e)` keeps it in that band
- [ ] `BringToFront(e, UiBand.Screens)` puts `e` over the canvas layer and under every dialog
- [ ] A pinned (`int.MaxValue`) child is never raised, never counted, and a window raised beside it does not wrap
- [ ] The canvas layer at `CanvasesAboveWindows` is not moved by a plain `BringToFront`
- [ ] A raise that would reach a band's ceiling renumbers the band from its floor in order; window-band members below 0 keep their values
- [ ] Every existing `UiRootInputTests`, `UiOverlayHostTests` and `RetailWindowManagerTests` test still passes

**Verify:** `dotnet test tests/AcDream.App.Tests --filter "FullyQualifiedName~UiBandTests|FullyQualifiedName~UiRootInputTests|FullyQualifiedName~UiOverlayHostTests|FullyQualifiedName~RetailWindowManagerTests"` → `Passed!  - Failed:     0, Passed:   103`

**Steps:**

- [ ] **Step 1: Write the failing tests**

Create `tests/AcDream.App.Tests/UI/UiBandTests.cs`:

```csharp
using AcDream.App.UI;
using AcDream.App.UI.Layout;

namespace AcDream.App.Tests.UI;

/// <summary>
/// Raising keeps an element inside its band: a window brought to the front
/// stays under the layer of canvases drawn above windows, screens sit over
/// that layer, dialogs and tooltips over screens, and pinned children are
/// never raised, counted or reached.
/// </summary>
public sealed class UiBandTests
{
    private static (UiRoot Root, UiPanel CanvasLayer) RootWithCanvasLayer()
    {
        var root = new UiRoot { Width = 800, Height = 600 };
        var canvasLayer = new UiPanel { ZOrder = UiBands.CanvasesAboveWindows };
        root.AddChild(canvasLayer);
        return (root, canvasLayer);
    }

    private static UiPanel Child(UiRoot root, int zOrder = 0)
    {
        var child = new UiPanel { Width = 100, Height = 100, ZOrder = zOrder };
        root.AddChild(child);
        return child;
    }

    [Fact]
    public void TheBandsAreInOrderAndTheirBoundsAreWhereTheirNamesSay()
    {
        Assert.True(UiOverlayZOrder.BandCeiling < UiOverlayZOrder.WindowFloor);
        Assert.True(UiOverlayZOrder.WindowFloor < UiBands.WindowsCeiling);
        Assert.Equal(UiBands.WindowsCeiling, UiBands.CanvasesAboveWindows);
        Assert.True(UiBands.CanvasesAboveWindows < UiBands.ScreensFloor);
        Assert.True(UiBands.ScreensFloor < UiBands.DialogsFloor);
        Assert.True(UiBands.DialogsFloor < UiBands.Pinned);
        Assert.Equal(UiBands.CanvasesAboveWindows, UiBands.UpperRenderLayerFloor);

        Assert.Equal(UiBand.Windows, UiBands.Of(UiOverlayZOrder.SharedHostRoot));
        Assert.Equal(UiBand.Windows, UiBands.Of(UiBands.WindowsCeiling - 1));
        Assert.Null(UiBands.Of(UiBands.CanvasesAboveWindows));
        Assert.Equal(UiBand.Screens, UiBands.Of(UiBands.ScreensFloor));
        Assert.Equal(UiBand.Screens, UiBands.Of(UiBands.DialogsFloor - 1));
        Assert.Equal(UiBand.DialogsAndTooltips, UiBands.Of(UiBands.DialogsFloor));
        Assert.Equal(UiBand.DialogsAndTooltips, UiBands.Of(UiBands.Pinned - 1));
        Assert.Null(UiBands.Of(UiBands.Pinned));
    }

    [Fact]
    public void AWindowBroughtToTheFrontStaysUnderTheCanvasLayerAndEveryDialog()
    {
        (UiRoot root, UiPanel canvasLayer) = RootWithCanvasLayer();
        UiPanel a = Child(root, zOrder: 5);
        UiPanel b = Child(root, zOrder: 9);
        UiPanel dialog = Child(root);
        root.BringToFront(dialog, UiBand.DialogsAndTooltips);

        root.BringToFront(a);

        Assert.Equal(10, a.ZOrder);
        Assert.Equal(UiBands.CanvasesAboveWindows, canvasLayer.ZOrder);
        Assert.Equal(UiBands.DialogsFloor, dialog.ZOrder);
        Assert.True(b.ZOrder < a.ZOrder);
    }

    [Fact]
    public void DialogsAndTooltipsGoOverTheCanvasLayerAndStayInTheirBandWhenRaisedAgain()
    {
        (UiRoot root, UiPanel canvasLayer) = RootWithCanvasLayer();
        UiPanel window = Child(root, zOrder: 3);
        UiPanel first = Child(root);
        UiPanel tooltip = Child(root);

        root.BringToFront(first, UiBand.DialogsAndTooltips);
        root.BringToFront(tooltip, UiBand.DialogsAndTooltips);
        Assert.Equal(UiBands.DialogsFloor, first.ZOrder);
        Assert.Equal(UiBands.DialogsFloor + 1, tooltip.ZOrder);

        // The root raises a clicked dialog without naming a band.
        root.BringToFront(first);
        Assert.Equal(UiBands.DialogsFloor + 2, first.ZOrder);
        Assert.True(canvasLayer.ZOrder < tooltip.ZOrder);
        Assert.Equal(3, window.ZOrder);
    }

    [Fact]
    public void AScreenGoesOverTheCanvasLayerAndUnderDialogs()
    {
        (UiRoot root, UiPanel canvasLayer) = RootWithCanvasLayer();
        UiPanel window = Child(root, zOrder: 40);
        UiPanel screen = Child(root);
        UiPanel dialog = Child(root);
        root.BringToFront(dialog, UiBand.DialogsAndTooltips);

        root.BringToFront(screen, UiBand.Screens);
        root.BringToFront(window);

        Assert.Equal(UiBands.ScreensFloor, screen.ZOrder);
        Assert.True(canvasLayer.ZOrder < screen.ZOrder);
        Assert.True(screen.ZOrder < dialog.ZOrder);
        Assert.Equal(40, window.ZOrder);
    }

    [Fact]
    public void PinnedChildrenAreNeverRaisedCountedOrReached()
    {
        var root = new UiRoot { Width = 800, Height = 600 };
        UiPanel spew = Child(root, zOrder: UiBands.Pinned);
        UiPanel window = Child(root, zOrder: 9);
        UiPanel dialog = Child(root);

        root.BringToFront(window);
        root.BringToFront(dialog, UiBand.DialogsAndTooltips);
        root.BringToFront(spew);
        root.BringToFront(spew, UiBand.Windows);

        Assert.Equal(9, window.ZOrder);
        Assert.Equal(UiBands.DialogsFloor, dialog.ZOrder);
        Assert.Equal(UiBands.Pinned, spew.ZOrder);
    }

    [Fact]
    public void TheCanvasLayerIsNeverRaisedByAPlainRaise()
    {
        (UiRoot root, UiPanel canvasLayer) = RootWithCanvasLayer();
        Child(root, zOrder: 7);

        root.BringToFront(canvasLayer);

        Assert.Equal(UiBands.CanvasesAboveWindows, canvasLayer.ZOrder);
    }

    [Fact]
    public void AFullDialogBandIsRenumberedFromItsFloorInOrder()
    {
        var root = new UiRoot { Width = 800, Height = 600 };
        UiPanel spew = Child(root, zOrder: UiBands.Pinned);
        UiPanel back = Child(root, zOrder: UiBands.DialogsFloor + 5);
        UiPanel front = Child(root, zOrder: UiBands.Pinned - 1);

        root.BringToFront(back);

        Assert.Equal(UiBands.DialogsFloor, front.ZOrder);
        Assert.Equal(UiBands.DialogsFloor + 1, back.ZOrder);
        Assert.Equal(UiBands.Pinned, spew.ZOrder);
    }

    [Fact]
    public void AFullWindowBandIsRenumberedAndWhatSitsBelowTheFloorKeepsItsPlace()
    {
        var root = new UiRoot { Width = 800, Height = 600 };
        UiOverlayHost host = UiOverlayHost.Mount(root);
        UiPanel back = Child(root, zOrder: 3);
        UiPanel front = Child(root, zOrder: UiBands.WindowsCeiling - 1);

        root.BringToFront(back);

        Assert.Equal(0, front.ZOrder);
        Assert.Equal(1, back.ZOrder);
        Assert.Equal(UiOverlayZOrder.SharedHostRoot, host.Root.ZOrder);
    }

    [Fact]
    public void ClickingADialogKeepsItInItsBand()
    {
        var root = new UiRoot { Width = 800, Height = 600 };
        var dialog = new UiPanel { Width = 100, Height = 100, Draggable = true };
        root.AddChild(dialog);
        root.BringToFront(dialog, UiBand.DialogsAndTooltips);

        root.OnMouseDown(UiMouseButton.Left, 50, 50);
        root.OnMouseUp(UiMouseButton.Left, 50, 50);

        Assert.Equal(UiBand.DialogsAndTooltips, UiBands.Of(dialog.ZOrder));
    }
}
```

- [ ] **Step 2: Run them to see them fail**

Run: `dotnet build tests/AcDream.App.Tests 2>&1 | grep -c "error CS"`
Expected: non-zero (`UiBands` and `UiBand` do not exist).

- [ ] **Step 3: The bands**

Create `src/AcDream.App/UI/UiBand.cs`:

```csharp
namespace AcDream.App.UI;

/// <summary>
/// The stacking bands of the interface root's children, back to front.
/// Raising an element to the front keeps it inside its band, so a window
/// clicked to the front never climbs over the plugin canvases drawn above
/// windows, and a dialog raised every tick never sinks beneath them.
/// </summary>
public enum UiBand
{
    /// <summary>Real windows, and anything else the root raises without naming a band.</summary>
    Windows,

    /// <summary>
    /// The full-screen pre-game screens (connecting, character select and
    /// creation, credits): over every window and every plugin canvas, under
    /// dialogs and tooltips.
    /// </summary>
    Screens,

    /// <summary>
    /// Confirmation dialogs and tooltips: over everything except what the
    /// overlay pass draws (menus, the drag ghost) and pinned children.
    /// </summary>
    DialogsAndTooltips,
}

/// <summary>
/// Where each <see cref="UiBand"/> lies in the root's z-order, and the two
/// values that belong to no band.
///
/// <para>Bottom to top: the overlay band and everything else below
/// <see cref="Layout.UiOverlayZOrder.WindowFloor"/>; windows from the floor
/// up to <see cref="WindowsCeiling"/>; the layer of plugin canvases drawn
/// above windows at <see cref="CanvasesAboveWindows"/>; screens; dialogs and
/// tooltips; and <see cref="Pinned"/> children (the SpewBox, the credits'
/// click surface), which nothing is ever raised over.</para>
/// </summary>
internal static class UiBands
{
    /// <summary>The front of the window band, exclusive.</summary>
    public const int WindowsCeiling = 1_000_000_000;

    /// <summary>
    /// Where the click-through layer of plugin canvases drawn above windows
    /// sits. It is in no band: nothing is raised into it or past it by a
    /// raise in another band.
    /// </summary>
    public const int CanvasesAboveWindows = WindowsCeiling;

    /// <summary>The back of the screens band.</summary>
    public const int ScreensFloor = CanvasesAboveWindows + 1;

    /// <summary>The back of the dialogs-and-tooltips band, and the front of the screens band, exclusive.</summary>
    public const int DialogsFloor = 1_500_000_000;

    /// <summary>
    /// Children here are pinned on top: never raised, never counted when
    /// another child is raised, and never reached by a raise.
    /// </summary>
    public const int Pinned = int.MaxValue;

    /// <summary>
    /// Where the interface's upper render layer starts: everything at or
    /// above the canvases drawn over windows is drawn after every window,
    /// text included (see <see cref="Rendering.TextRenderer"/>).
    /// </summary>
    public const int UpperRenderLayerFloor = CanvasesAboveWindows;

    /// <summary>The band a z-order lies in, or null for the canvas layer and pinned children.</summary>
    public static UiBand? Of(int zOrder) => zOrder switch
    {
        Pinned => null,
        >= DialogsFloor => UiBand.DialogsAndTooltips,
        >= ScreensFloor => UiBand.Screens,
        >= WindowsCeiling => null,
        _ => UiBand.Windows,
    };

    /// <summary>
    /// Where an element moved into the band starts, and the band's front,
    /// exclusive. A window band member below the floor (an overlay, an
    /// imported layout root one level back) is in the band but keeps its
    /// own value until it is raised.
    /// </summary>
    public static (int Floor, int Ceiling) Range(UiBand band) => band switch
    {
        UiBand.Windows => (Layout.UiOverlayZOrder.WindowFloor, WindowsCeiling),
        UiBand.Screens => (ScreensFloor, DialogsFloor),
        UiBand.DialogsAndTooltips => (DialogsFloor, Pinned),
        _ => throw new ArgumentOutOfRangeException(nameof(band), band, null),
    };
}
```

- [ ] **Step 4: Banded raise**

In `src/AcDream.App/UI/RetailWindowManager.cs`, replace:

```csharp
    public void BringToFront(UiElement window)
    {
        int top = window.ZOrder;
        foreach (var child in _root.Children)
            if (!ReferenceEquals(child, window))
                top = Math.Max(top, child.ZOrder + 1);
        window.ZOrder = top;
    }
```

with:

```csharp
    /// <summary>
    /// Raises <paramref name="window"/> over every other root child in the
    /// band it is already in (see <see cref="UiBands"/>). An element in no
    /// band -- pinned, or the layer of canvases drawn above windows -- stays
    /// where it is.
    /// </summary>
    public void BringToFront(UiElement window)
    {
        if (UiBands.Of(window.ZOrder) is { } band)
            Raise(window, band);
    }

    /// <summary>
    /// Moves <paramref name="window"/> into <paramref name="band"/>, over
    /// every other root child there. A pinned element stays pinned.
    /// </summary>
    public void BringToFront(UiElement window, UiBand band)
    {
        if (window.ZOrder != UiBands.Pinned)
            Raise(window, band);
    }

    private void Raise(UiElement window, UiBand band)
    {
        (int floor, int ceiling) = UiBands.Range(band);
        int top = UiBands.Of(window.ZOrder) == band ? window.ZOrder : floor;
        foreach (var child in _root.Children)
        {
            // A band member is below the band's ceiling, so the + 1 cannot overflow.
            if (!ReferenceEquals(child, window) && UiBands.Of(child.ZOrder) == band)
                top = Math.Max(top, child.ZOrder + 1);
        }
        if (top >= ceiling)
            top = Compact(window, band, floor);
        window.ZOrder = top;
    }

    /// <summary>
    /// Renumbers the band's other members from its floor, in their current
    /// order, and answers the value above them. Reached only after about a
    /// billion raises in one band; members below the floor keep their values.
    /// </summary>
    private int Compact(UiElement window, UiBand band, int floor)
    {
        UiElement[] members = _root.Children
            .Where(child => !ReferenceEquals(child, window)
                && UiBands.Of(child.ZOrder) == band
                && child.ZOrder >= floor)
            .OrderBy(child => child.ZOrder)
            .ToArray();
        for (int index = 0; index < members.Length; index++)
            members[index].ZOrder = floor + index;
        return floor + members.Length;
    }
```

(`System.Linq` is already imported.) In `src/AcDream.App/UI/UiRoot.cs`, replace:

```csharp
    public void BringToFront(UiElement window)
        => WindowManager.BringToFront(window);
```

with:

```csharp
    public void BringToFront(UiElement window)
        => WindowManager.BringToFront(window);

    public void BringToFront(UiElement window, UiBand band)
        => WindowManager.BringToFront(window, band);
```

- [ ] **Step 5: Run the tests**

Run: `dotnet test tests/AcDream.App.Tests --filter "FullyQualifiedName~UiBandTests|FullyQualifiedName~UiRootInputTests|FullyQualifiedName~UiOverlayHostTests|FullyQualifiedName~RetailWindowManagerTests" 2>&1 | grep -E "Passed!|Failed!"`
Expected: `Passed!  - Failed:     0, Passed:   103`.

- [ ] **Step 6: Commit**

```bash
git add src/AcDream.App/UI/UiBand.cs src/AcDream.App/UI/RetailWindowManager.cs src/AcDream.App/UI/UiRoot.cs tests/AcDream.App.Tests/UI/UiBandTests.cs
git commit -m "ui: bands for the root's z-order instead of overflow at int.MaxValue

Claude-Session: https://claude.ai/code/session_01EtugJHhFMqGXY2V6Lz2ZFB"
```

```json:metadata
{"files": ["src/AcDream.App/UI/UiBand.cs", "src/AcDream.App/UI/RetailWindowManager.cs", "src/AcDream.App/UI/UiRoot.cs", "tests/AcDream.App.Tests/UI/UiBandTests.cs"], "verifyCommand": "dotnet test tests/AcDream.App.Tests --filter \"FullyQualifiedName~UiBandTests|FullyQualifiedName~UiRootInputTests|FullyQualifiedName~UiOverlayHostTests|FullyQualifiedName~RetailWindowManagerTests\"", "acceptanceCriteria": ["window raise stays below canvas layer", "dialog band entry and plain raise keep band", "screens between canvas layer and dialogs", "pinned untouched, no wrap", "canvas layer not moved", "compaction from floor in order", "existing raise tests pass"], "modelTier": "standard"}
```

---

### Task 3: Dialogs, tooltips and pre-game screens in their bands

**Goal:** Confirmation dialogs and tooltips raise into the dialogs-and-tooltips band, and the connecting, character select, character creation and credits screens into the screens band, so they all sit over any canvas drawn above windows.

**Files:**
- Modify: `src/AcDream.App/UI/Layout/RetailDialogFactory.cs:277, 398`
- Modify: `src/AcDream.App/UI/Layout/RetailTooltipPresenter.cs:103, 332`
- Modify: `src/AcDream.App/UI/Layout/ConnectionUiController.cs:139`
- Modify: `src/AcDream.App/UI/Layout/CharacterManagementUiController.cs:315`
- Modify: `src/AcDream.App/UI/Layout/CharacterCreationUiController.cs:364`
- Modify: `src/AcDream.App/UI/Layout/CreditsUiController.cs:162-164`
- Test: `tests/AcDream.App.Tests/UI/Layout/RetailDialogFactoryTests.cs`, `RetailTooltipPresenterTests.cs`, `ConnectionUiControllerTests.cs`, `CreditsUiControllerTests.cs`, `CharacterCreationUiControllerTests.cs`, `CharacterManagementUiControllerTests.cs`

**Acceptance Criteria:**
- [ ] A confirmation dialog's root is in `UiBand.DialogsAndTooltips` after it opens and after `factory.Tick()`, above a canvas layer at `CanvasesAboveWindows`
- [ ] A tooltip popup is in `UiBand.DialogsAndTooltips` when shown and after `presenter.Tick()`
- [ ] The connection, character management and character creation roots, and the credits' picture and text roots, are in `UiBand.Screens` when shown; the credits' pictures stay under its text
- [ ] The six new tests fail with the six call sites reverted (checked in the prototype)

**Verify:** `dotnet test tests/AcDream.App.Tests --filter "FullyQualifiedName~RetailDialogFactoryTests|FullyQualifiedName~RetailTooltipPresenterTests|FullyQualifiedName~ConnectionUiControllerTests|FullyQualifiedName~CreditsUiControllerTests|FullyQualifiedName~CharacterCreationUiControllerTests|FullyQualifiedName~CharacterManagementUiControllerTests|FullyQualifiedName~CharacterScreensFixedCanvasArbiterTests"` → `Passed!  - Failed:     0, Passed:   211`

**Steps:**

- [ ] **Step 1: Write the failing tests**

In `tests/AcDream.App.Tests/UI/Layout/RetailDialogFactoryTests.cs`, insert before the `[Fact]` of `ConfirmationCreatesFreshCenteredRootAndReturnsPropertyResult`:

```csharp
    [Fact]
    public void ADialogIsRaisedIntoTheDialogBandOverTheCanvasLayerAndKeptThere()
    {
        var root = new UiRoot { Width = 1024f, Height = 768f };
        var canvasLayer = new UiPanel { ZOrder = UiBands.CanvasesAboveWindows };
        root.AddChild(canvasLayer);
        var layouts = new List<ImportedLayout>();
        var factory = CreateFactory(root, layouts);

        factory.MakeConfirmation("Quit?", _ => { });
        UiElement dialog = Assert.Single(layouts).Root;
        Assert.Equal(UiBand.DialogsAndTooltips, UiBands.Of(dialog.ZOrder));
        Assert.True(canvasLayer.ZOrder < dialog.ZOrder);

        factory.Tick();
        Assert.Equal(UiBand.DialogsAndTooltips, UiBands.Of(dialog.ZOrder));
    }

```

In `RetailTooltipPresenterTests.cs`, insert before the `[Fact]` of `GlobalEnableGateOff_SuppressesPresentation_ButTheDwellTimerStillFires`:

```csharp
    [Fact]
    public void ATooltipIsRaisedIntoTheDialogBandAndKeptThere()
    {
        var (root, presenter, _) = CreateHarness();
        AddFullyAuthoredTarget(root);
        var canvasLayer = new UiPanel { ZOrder = UiBands.CanvasesAboveWindows };
        root.AddChild(canvasLayer);
        var before = root.Children.ToHashSet();

        root.OnMouseMove(110, 110);
        root.Tick(0.016, 0);
        root.Tick(0.016, root.TooltipDelayMs);
        UiElement popup = Assert.Single(root.Children, child => !before.Contains(child));
        Assert.Equal(UiBand.DialogsAndTooltips, UiBands.Of(popup.ZOrder));

        presenter.Tick();
        Assert.Equal(UiBand.DialogsAndTooltips, UiBands.Of(popup.ZOrder));
    }

```

In `ConnectionUiControllerTests.cs`, insert before the `[Fact]` of `BackdropCoversTheCanvasBehindAllAuthoredArtwork`:

```csharp
    [Fact]
    public void TheScreenIsRaisedIntoTheScreensBand()
    {
        var host = new UiRoot();
        ImportedLayout layout = FixtureLoader.LoadConnectionScreen();
        var view = new View { Snapshot = new(RuntimeConnectionStatus.Connecting, 0f, 0f) };
        using var controller = Bind(host, layout, view);

        controller.Tick();

        Assert.True(controller.Root.Visible);
        Assert.Equal(UiBand.Screens, UiBands.Of(controller.Root.ZOrder));
    }

```

In `CreditsUiControllerTests.cs`, insert before the `[Fact]` of `AnyKey_ShowsWaitForAFrame_ThenReturnsToCharacterManagement`:

```csharp
    [Fact]
    public void ActivateRaisesThePicturesAndTheTextIntoTheScreensBand()
    {
        using var environment = new EnvironmentHarness();
        CreditsUiController controller = environment.Controller;

        controller.Activate();

        Assert.Equal(UiBand.Screens, UiBands.Of(controller.PictureRoot.ZOrder));
        Assert.Equal(UiBand.Screens, UiBands.Of(controller.TextRoot.ZOrder));
        Assert.True(controller.PictureRoot.ZOrder < controller.TextRoot.ZOrder);
    }

```

In `CharacterCreationUiControllerTests.cs`, insert before the `[Fact]` of `TabClick_SwitchesToTheClickedPage_FreeOfValidation`:

```csharp
    [Fact]
    public void AnOpenScreenIsRaisedIntoTheScreensBand()
    {
        using var environment = new EnvironmentHarness();

        environment.Controller.Open();
        environment.Controller.Tick();

        Assert.Equal(UiBand.Screens, UiBands.Of(environment.Controller.Root.ZOrder));
    }

```

In `CharacterManagementUiControllerTests.cs`, insert before the `[Fact]` of `AuthoredChildContract_PreservesRuntimeOrderGreyTailHighlightAndButtonMatrix`:

```csharp
    [Fact]
    public void AnActiveScreenIsRaisedIntoTheScreensBand()
    {
        using var environment = new EnvironmentHarness();

        Assert.Equal(UiBand.Screens, UiBands.Of(environment.Controller.Root.ZOrder));
    }

```

- [ ] **Step 2: Run them to see them fail**

Run: `dotnet test tests/AcDream.App.Tests --filter "FullyQualifiedName~ADialogIsRaisedIntoTheDialogBand|FullyQualifiedName~ATooltipIsRaisedIntoTheDialogBand|FullyQualifiedName~TheScreenIsRaisedIntoTheScreensBand|FullyQualifiedName~ActivateRaisesThePicturesAndTheText|FullyQualifiedName~AnOpenScreenIsRaisedIntoTheScreensBand|FullyQualifiedName~AnActiveScreenIsRaisedIntoTheScreensBand" 2>&1 | grep -E "Passed!|Failed!"`
Expected: `Failed!  - Failed:     6, Passed:     0`.

- [ ] **Step 3: Raise into the bands**

- `RetailDialogFactory.cs`: both `_host.BringToFront(view.Root);` → `_host.BringToFront(view.Root, UiBand.DialogsAndTooltips);`
- `RetailTooltipPresenter.cs`: `_host.BringToFront(root);` → `_host.BringToFront(root, UiBand.DialogsAndTooltips);` and `_host.BringToFront(_popupRoot);` → `_host.BringToFront(_popupRoot, UiBand.DialogsAndTooltips);`
- `ConnectionUiController.cs`, `CharacterManagementUiController.cs`, `CharacterCreationUiController.cs`: `_host.BringToFront(Root);` → `_host.BringToFront(Root, UiBand.Screens);`
- `CreditsUiController.cs`: replace

```csharp
        _host.BringToFront(PictureRoot);
        _host.BringToFront(TextRoot);
        _host.BringToFront(_actionSurface);
```

with

```csharp
        _host.BringToFront(PictureRoot, UiBand.Screens);
        _host.BringToFront(TextRoot, UiBand.Screens);
        // Pinned: stays over everything, as before.
        _host.BringToFront(_actionSurface, UiBand.Screens);
```

All six files are in `AcDream.App.UI.Layout`, which sees `UiBand` without a `using`.

- [ ] **Step 4: Run the tests**

Run the **Verify** command. Expected: `Passed!  - Failed:     0, Passed:   211`.

- [ ] **Step 5: Commit**

```bash
git add src/AcDream.App/UI/Layout/RetailDialogFactory.cs src/AcDream.App/UI/Layout/RetailTooltipPresenter.cs src/AcDream.App/UI/Layout/ConnectionUiController.cs src/AcDream.App/UI/Layout/CharacterManagementUiController.cs src/AcDream.App/UI/Layout/CharacterCreationUiController.cs src/AcDream.App/UI/Layout/CreditsUiController.cs tests/AcDream.App.Tests/UI/Layout/RetailDialogFactoryTests.cs tests/AcDream.App.Tests/UI/Layout/RetailTooltipPresenterTests.cs tests/AcDream.App.Tests/UI/Layout/ConnectionUiControllerTests.cs tests/AcDream.App.Tests/UI/Layout/CreditsUiControllerTests.cs tests/AcDream.App.Tests/UI/Layout/CharacterCreationUiControllerTests.cs tests/AcDream.App.Tests/UI/Layout/CharacterManagementUiControllerTests.cs
git commit -m "ui: dialogs and tooltips raise into their band, pre-game screens into theirs

Claude-Session: https://claude.ai/code/session_01EtugJHhFMqGXY2V6Lz2ZFB"
```

```json:metadata
{"files": ["src/AcDream.App/UI/Layout/RetailDialogFactory.cs", "src/AcDream.App/UI/Layout/RetailTooltipPresenter.cs", "src/AcDream.App/UI/Layout/ConnectionUiController.cs", "src/AcDream.App/UI/Layout/CharacterManagementUiController.cs", "src/AcDream.App/UI/Layout/CharacterCreationUiController.cs", "src/AcDream.App/UI/Layout/CreditsUiController.cs"], "verifyCommand": "dotnet test tests/AcDream.App.Tests --filter \"FullyQualifiedName~RetailDialogFactoryTests|FullyQualifiedName~RetailTooltipPresenterTests|FullyQualifiedName~ConnectionUiControllerTests|FullyQualifiedName~CreditsUiControllerTests|FullyQualifiedName~CharacterCreationUiControllerTests|FullyQualifiedName~CharacterManagementUiControllerTests|FullyQualifiedName~CharacterScreensFixedCanvasArbiterTests\"", "acceptanceCriteria": ["dialog in dialogs band after open and tick", "tooltip in dialogs band after show and tick", "four screens in screens band, pictures under text", "new tests fail with call sites reverted"], "modelTier": "mechanical"}
```

---

### Task 4: An upper draw layer from the canvases over windows upwards

**Goal:** Everything the root draws from `UiBands.UpperRenderLayerFloor` upwards lands in a second draw layer that the frame draws after the first, so a window's rectangles and bitmap-font text (which draw after every sprite of their layer) never show through a canvas, screen, dialog or tooltip above them.

**Files:**
- Modify: `src/AcDream.App/Rendering/TextRenderer.cs`
- Modify: `src/AcDream.App/UI/UiRenderContext.cs:141-142`
- Modify: `src/AcDream.App/UI/UiElement.cs` (`DrawSelfAndChildren`, the virtual overrides)
- Modify: `src/AcDream.App/UI/UiRoot.cs` (`DrawCore`, usings)
- Create: `tests/AcDream.App.Tests/Rendering/TextRendererDrawLayerTests.cs`

**Acceptance Criteria:**
- [ ] A rectangle drawn in the main layer is submitted before a sprite drawn in the upper layer
- [ ] `Begin` puts draws back in the main layer; `OverlayMode = true/false` selects the overlay/main layer
- [ ] Under a `UiRoot`, a child at `UiBands.CanvasesAboveWindows` draws after a window's rectangle; with every child below the floor, the order is the single-layer order as before
- [ ] `DebugSpriteSegments`, `DebugSpriteSegmentVerts`, `DebugTextBuffer` and `DebugRectVertexCount` report the main and upper layers together, so every existing renderer test passes unchanged

**Verify:** `dotnet test tests/AcDream.App.Tests --filter "FullyQualifiedName~TextRendererDrawLayerTests|FullyQualifiedName~UiRectOutlinePainterOrderTests|FullyQualifiedName~UiRenderContextAlphaTests|FullyQualifiedName~TextRenderer|FullyQualifiedName~UiRoot|FullyQualifiedName~CharacterManagementUiControllerTests|FullyQualifiedName~ConnectionMeterAnimationTests"` → `Passed!  - Failed:     0, Passed:   130`

**Steps:**

- [ ] **Step 1: Write the failing tests**

Create `tests/AcDream.App.Tests/Rendering/TextRendererDrawLayerTests.cs`:

```csharp
using System.Numerics;
using AcDream.App.Rendering;
using AcDream.App.Rendering.Gpu;
using AcDream.App.Tests.Rendering.Gpu;
using AcDream.App.UI;

namespace AcDream.App.Tests.Rendering;

/// <summary>
/// A frame is drawn as three layers -- main, upper, overlay -- each with its
/// sprite runs before its rectangles and text. Everything the root draws
/// from the plugin canvases over windows upwards is in the upper layer, so a
/// window's rectangles and text, which draw after every sprite of their
/// layer, can no longer show through a canvas or a dialog above them.
/// </summary>
public sealed class TextRendererDrawLayerTests
{
    private const uint UpperTexture = 77u;

    private sealed class FrameSource : ICurrentGpuFrameSource
    {
        public IGpuFrame? CurrentFrame { get; set; }
    }

    /// <summary>A window that draws through the renderer's rectangle bucket, which flushes after its layer's sprites.</summary>
    private sealed class RectWindow : UiElement
    {
        protected override void OnDraw(UiRenderContext ctx) =>
            ctx.TextRenderer.DrawRect(0f, 0f, Width, Height, Vector4.One);
    }

    /// <summary>Stands in for a canvas drawn over windows: one textured sprite.</summary>
    private sealed class SpriteElement : UiElement
    {
        protected override void OnDraw(UiRenderContext ctx) =>
            ctx.DrawSprite(UpperTexture, 0f, 0f, Width, Height, 0f, 0f, 1f, 1f, Vector4.One);
    }

    /// <summary>The texture slot of every draw, in submission order.</summary>
    private static List<uint> DrawnTextureSlots(RecordingGpuDevice device)
    {
        var slots = new List<uint>();
        uint current = 0;
        foreach (GpuRecordedCall call in device.Calls)
        {
            if (call is GpuRecordedPushConstants constants)
                current = constants.Constants.TextureIndexA;
            else if (call is GpuRecordedDraw)
                slots.Add(current);
        }
        return slots;
    }

    private static List<uint> Flush(RecordingGpuDevice device, FrameSource frames, TextRenderer renderer)
    {
        device.Clear();
        using IGpuFrame frame = device.BeginFrame();
        frames.CurrentFrame = frame;
        try
        {
            renderer.Flush(null);
        }
        finally
        {
            frames.CurrentFrame = null;
        }
        return DrawnTextureSlots(device);
    }

    private static uint Slot(uint handle) => UiTextureTableHandle.ToSlot(handle).Index;

    [Fact]
    public void TheUpperLayerDrawsAfterTheMainLayersRectangles()
    {
        var device = new RecordingGpuDevice();
        var frames = new FrameSource();
        var renderer = new TextRenderer(device, frames, "unused");
        renderer.Begin(new Vector2(100f, 100f));

        renderer.DrawRect(0f, 0f, 10f, 10f, Vector4.One);
        renderer.Layer = UiDrawLayer.Upper;
        renderer.DrawSprite(UpperTexture, 0f, 0f, 10f, 10f, 0f, 0f, 1f, 1f, Vector4.One);

        Assert.Equal([Slot(UiTextureTableHandle.None), Slot(UpperTexture)], Flush(device, frames, renderer));
    }

    [Fact]
    public void BeginPutsDrawsBackInTheMainLayerAndOverlayModeStillNamesTheOverlay()
    {
        var renderer = new TextRenderer(new RecordingGpuDevice(), new FrameSource(), "unused");
        renderer.Layer = UiDrawLayer.Upper;

        renderer.Begin(new Vector2(10f, 10f));
        Assert.Equal(UiDrawLayer.Main, renderer.Layer);

        renderer.OverlayMode = true;
        Assert.Equal(UiDrawLayer.Overlay, renderer.Layer);
        renderer.OverlayMode = false;
        Assert.Equal(UiDrawLayer.Main, renderer.Layer);
    }

    [Fact]
    public void TheRootDrawsFromTheCanvasesOverWindowsUpwardsInTheUpperLayer()
    {
        var device = new RecordingGpuDevice();
        var frames = new FrameSource();
        var renderer = new TextRenderer(device, frames, "unused");
        var root = new UiRoot { Width = 100f, Height = 100f };
        root.AddChild(new RectWindow { Width = 50f, Height = 50f, ZOrder = 4 });
        root.AddChild(new SpriteElement { Width = 50f, Height = 50f, ZOrder = UiBands.CanvasesAboveWindows });

        renderer.Begin(new Vector2(100f, 100f));
        root.Draw(new UiRenderContext(renderer, new Vector2(100f, 100f)));

        // Without the upper layer the window's rectangle would draw last, over the canvas.
        Assert.Equal([Slot(UiTextureTableHandle.None), Slot(UpperTexture)], Flush(device, frames, renderer));
        Assert.Equal(UiDrawLayer.Main, renderer.Layer);
    }

    [Fact]
    public void ARootWithNothingOverWindowsDrawsEverythingInTheMainLayer()
    {
        var device = new RecordingGpuDevice();
        var frames = new FrameSource();
        var renderer = new TextRenderer(device, frames, "unused");
        var root = new UiRoot { Width = 100f, Height = 100f };
        root.AddChild(new RectWindow { Width = 50f, Height = 50f, ZOrder = 4 });
        root.AddChild(new SpriteElement { Width = 50f, Height = 50f, ZOrder = 9 });

        renderer.Begin(new Vector2(100f, 100f));
        root.Draw(new UiRenderContext(renderer, new Vector2(100f, 100f)));

        // One layer: its sprite runs, then its rectangles, as before.
        Assert.Equal([Slot(UpperTexture), Slot(UiTextureTableHandle.None)], Flush(device, frames, renderer));
    }
}
```

- [ ] **Step 2: Run them to see them fail**

Run: `dotnet build tests/AcDream.App.Tests 2>&1 | grep -c "error CS"`
Expected: non-zero (`UiDrawLayer` does not exist).

- [ ] **Step 3: Three layer buffers in the renderer**

In `src/AcDream.App/Rendering/TextRenderer.cs`:

Replace

```csharp
namespace AcDream.App.Rendering;

public sealed class TextRenderer : IDisposable
```

with

```csharp
namespace AcDream.App.Rendering;

/// <summary>
/// Which of a frame's three draw layers a draw lands in. The layers are
/// drawn in this order, and each one draws its sprite runs, then its
/// rectangles, then its bitmap-font text, so text never shows through
/// anything in a later layer.
/// </summary>
internal enum UiDrawLayer
{
    /// <summary>The world overlays, plugin canvases under windows, and every window.</summary>
    Main,

    /// <summary>
    /// Everything from the plugin canvases drawn over windows upwards:
    /// those canvases, pre-game screens, dialogs and tooltips.
    /// </summary>
    Upper,

    /// <summary>The overlay pass: menus, popups and the drag ghost.</summary>
    Overlay,
}

public sealed class TextRenderer : IDisposable
```

Replace the main buffers

```csharp
    private sealed class SpriteSeg { public uint Texture; public readonly List<float> Verts = new(256); }

    private readonly List<float> _textBuf = new(8192);
    private readonly List<float> _rectBuf = new(1024);
    private readonly List<SpriteSeg> _spriteSegs = new();
    private int _segUsed;
    private int _textVerts;
    private int _rectVerts;
    private Vector2 _screenSize;
```

with

```csharp
    private sealed class SpriteSeg { public uint Texture; public readonly List<float> Verts = new(256); }

    /// <summary>What one <see cref="UiDrawLayer"/> collected this frame.</summary>
    private sealed class DrawLayerBuffers(int textCapacity, int rectCapacity)
    {
        public readonly List<float> Text = new(textCapacity);
        public readonly List<float> Rects = new(rectCapacity);
        public readonly List<SpriteSeg> SpriteSegs = new();
        public int SegUsed;
        public int TextVerts;
        public int RectVerts;

        public bool HasAnything => SegUsed > 0 || TextVerts > 0 || RectVerts > 0;

        public void Clear()
        {
            Text.Clear();
            Rects.Clear();
            SegUsed = 0; // pool the SpriteSeg objects across frames
            TextVerts = 0;
            RectVerts = 0;
        }
    }

    // Indexed by UiDrawLayer, and drawn in that order.
    private readonly DrawLayerBuffers[] _layers =
    [
        new(textCapacity: 8192, rectCapacity: 1024),
        new(textCapacity: 1024, rectCapacity: 256),
        new(textCapacity: 1024, rectCapacity: 256),
    ];
    private Vector2 _screenSize;

    private DrawLayerBuffers Current => _layers[(int)Layer];

    /// <summary>The sprite runs below the overlay pass, main layer first, as the frame draws them.</summary>
    private IEnumerable<SpriteSeg> DebugSegs()
    {
        for (int layer = (int)UiDrawLayer.Main; layer <= (int)UiDrawLayer.Upper; layer++)
        {
            DrawLayerBuffers buffers = _layers[layer];
            for (int i = 0; i < buffers.SegUsed; i++)
                yield return buffers.SpriteSegs[i];
        }
    }
```

In `DebugSpriteSegments`, replace

```csharp
            var result = new List<(uint, int, float)>(_segUsed);
            for (int i = 0; i < _segUsed; i++)
            {
                SpriteSeg seg = _spriteSegs[i];
                float alpha = seg.Verts.Count > 0 ? seg.Verts[7] : 0f;
                result.Add((seg.Texture, seg.Verts.Count / FloatsPerVertex, alpha));
            }
            return result;
```

with

```csharp
            var result = new List<(uint, int, float)>();
            foreach (SpriteSeg seg in DebugSegs())
            {
                float alpha = seg.Verts.Count > 0 ? seg.Verts[7] : 0f;
                result.Add((seg.Texture, seg.Verts.Count / FloatsPerVertex, alpha));
            }
            return result;
```

In `DebugSpriteSegmentVerts`, replace

```csharp
            var result = new List<(uint, IReadOnlyList<float>)>(_segUsed);
            for (int i = 0; i < _segUsed; i++)
            {
                SpriteSeg seg = _spriteSegs[i];
                result.Add((seg.Texture, seg.Verts.ToArray()));
            }
            return result;
```

with

```csharp
            var result = new List<(uint, IReadOnlyList<float>)>();
            foreach (SpriteSeg seg in DebugSegs())
                result.Add((seg.Texture, seg.Verts.ToArray()));
            return result;
```

Replace the text and rect debug accessors, the overlay buffers and `OverlayMode`

```csharp
    internal (int VertexCount, float Alpha) DebugTextBuffer
        => (_textVerts, _textBuf.Count > 0 ? _textBuf[7] : 0f);

    internal int DebugRectVertexCount => _rectVerts;

    private readonly List<float> _overlayTextBuf = new(1024);
    private readonly List<float> _overlayRectBuf = new(256);
    private readonly List<SpriteSeg> _overlaySpriteSegs = new();
    private int _overlaySegUsed;
    private int _overlayTextVerts;
    private int _overlayRectVerts;

    public bool OverlayMode { get; set; }
```

with

```csharp
    internal (int VertexCount, float Alpha) DebugTextBuffer
    {
        get
        {
            DrawLayerBuffers main = _layers[(int)UiDrawLayer.Main];
            DrawLayerBuffers upper = _layers[(int)UiDrawLayer.Upper];
            List<float> first = main.Text.Count > 0 ? main.Text : upper.Text;
            return (main.TextVerts + upper.TextVerts, first.Count > 0 ? first[7] : 0f);
        }
    }

    internal int DebugRectVertexCount =>
        _layers[(int)UiDrawLayer.Main].RectVerts + _layers[(int)UiDrawLayer.Upper].RectVerts;

    /// <summary>Where draws land; back to <see cref="UiDrawLayer.Main"/> at every <see cref="Begin"/>.</summary>
    internal UiDrawLayer Layer { get; set; }

    /// <summary>Whether draws land in the overlay layer; false puts them back in the main layer.</summary>
    public bool OverlayMode
    {
        get => Layer == UiDrawLayer.Overlay;
        set => Layer = value ? UiDrawLayer.Overlay : UiDrawLayer.Main;
    }
```

In `Begin`, replace

```csharp
        _screenSize = screenSize;
        _textBuf.Clear();
        _rectBuf.Clear();
        _segUsed = 0; // pool the SpriteSeg objects across frames
        _textVerts = 0;
        _rectVerts = 0;
        _overlayTextBuf.Clear();
        _overlayRectBuf.Clear();
        _overlaySegUsed = 0;
        _overlayTextVerts = 0;
        _overlayRectVerts = 0;
        OverlayMode = false;
```

with

```csharp
        _screenSize = screenSize;
        foreach (DrawLayerBuffers layer in _layers)
            layer.Clear();
        Layer = UiDrawLayer.Main;
```

In `DrawRect`, replace

```csharp
        if (OverlayMode) { AppendQuad(_overlayRectBuf, x, y, w, h, 0, 0, 0, 0, color); _overlayRectVerts += 6; }
        else             { AppendQuad(_rectBuf,        x, y, w, h, 0, 0, 0, 0, color); _rectVerts += 6; }
```

with

```csharp
        DrawLayerBuffers layer = Current;
        AppendQuad(layer.Rects, x, y, w, h, 0, 0, 0, 0, color);
        layer.RectVerts += 6;
```

In `DrawStringCore`, replace

```csharp
                if (OverlayMode) { AppendQuad(_overlayTextBuf, gx, gy, gw, gh, u0, v0, u1, v1, color); _overlayTextVerts += 6; }
                else             { AppendQuad(_textBuf,        gx, gy, gw, gh, u0, v0, u1, v1, color); _textVerts += 6; }
```

with

```csharp
                DrawLayerBuffers layer = Current;
                AppendQuad(layer.Text, gx, gy, gw, gh, u0, v0, u1, v1, color);
                layer.TextVerts += 6;
```

In `DrawSprite` and in `DrawConvexPolygon` (two places), replace

```csharp
        SpriteSeg seg = OverlayMode
            ? NextSpriteSeg(_overlaySpriteSegs, ref _overlaySegUsed, texture)
            : NextSpriteSeg(_spriteSegs,        ref _segUsed,        texture);
```

with

```csharp
        DrawLayerBuffers layer = Current;
        SpriteSeg seg = NextSpriteSeg(layer.SpriteSegs, ref layer.SegUsed, texture);
```

Replace `HasAnythingToDraw`

```csharp
    private bool HasAnythingToDraw =>
        _segUsed > 0 || _textVerts > 0 || _rectVerts > 0
        || _overlaySegUsed > 0 || _overlayTextVerts > 0 || _overlayRectVerts > 0;
```

with

```csharp
    private bool HasAnythingToDraw => Array.Exists(_layers, static layer => layer.HasAnything);
```

and in `FlushInto` replace

```csharp
        DrawLayer(_spriteSegs, _segUsed, _rectBuf, _rectVerts, _textBuf, _textVerts, font, frame, encoder);
        DrawLayer(_overlaySpriteSegs, _overlaySegUsed, _overlayRectBuf, _overlayRectVerts, _overlayTextBuf, _overlayTextVerts, font, frame, encoder);
```

with

```csharp
        foreach (DrawLayerBuffers layer in _layers)
            DrawLayer(layer.SpriteSegs, layer.SegUsed, layer.Rects, layer.RectVerts, layer.Text, layer.TextVerts, font, frame, encoder);
```

`NextSpriteSeg` and `DrawLayer` are unchanged. `grep -n "_overlay\|_segUsed\|_spriteSegs\|_textBuf\|_rectBuf\|_textVerts\|_rectVerts" src/AcDream.App/Rendering/TextRenderer.cs` must print nothing.

- [ ] **Step 4: The context, the hook and the root**

In `src/AcDream.App/UI/UiRenderContext.cs`, after `public void EndOverlayLayer() => TextRenderer.OverlayMode = false;` add:

```csharp

    /// <summary>
    /// Sends what is drawn next to the upper layer, which the frame draws
    /// after everything before it, text included. The root calls it once,
    /// at the plugin canvases drawn over windows.
    /// </summary>
    internal void BeginUpperLayer() => TextRenderer.Layer = UiDrawLayer.Upper;
```

In `src/AcDream.App/UI/UiElement.cs`, in `DrawSelfAndChildren`, replace

```csharp
                    UiElement[] ordered = ChildrenBackToFrontSnapshot();
                    for (int i = 0; i < ordered.Length; i++)
                        ordered[i].DrawSelfAndChildren(ctx);
                }

                OnDrawAfterChildren(ctx);
```

with

```csharp
                    UiElement[] ordered = ChildrenBackToFrontSnapshot();
                    for (int i = 0; i < ordered.Length; i++)
                    {
                        OnDrawingChild(ctx, ordered[i]);
                        ordered[i].DrawSelfAndChildren(ctx);
                    }
                }

                OnDrawAfterChildren(ctx);
```

and after `protected virtual void OnDrawAfterChildren(UiRenderContext ctx) { }` add:

```csharp

    /// <summary>Called just before each child is drawn, back to front.</summary>
    private protected virtual void OnDrawingChild(UiRenderContext ctx, UiElement child) { }
```

In `src/AcDream.App/UI/UiRoot.cs`, add `using AcDream.App.Rendering;` after `using System.Numerics;`, and replace

```csharp
    private void DrawCore(UiRenderContext ctx)
    {
        DrawSelfAndChildren(ctx);
```

with

```csharp
    /// <summary>
    /// From the plugin canvases drawn over windows upwards, everything is
    /// drawn in the upper layer, so no window's text shows through them.
    /// </summary>
    private protected override void OnDrawingChild(UiRenderContext ctx, UiElement child)
    {
        if (child.ZOrder >= UiBands.UpperRenderLayerFloor && ctx.TextRenderer.Layer == UiDrawLayer.Main)
            ctx.BeginUpperLayer();
    }

    private void DrawCore(UiRenderContext ctx)
    {
        DrawSelfAndChildren(ctx);
```

- [ ] **Step 5: Run the tests**

Run the **Verify** command. Expected: `Passed!  - Failed:     0, Passed:   130`.

- [ ] **Step 6: Commit**

```bash
git add src/AcDream.App/Rendering/TextRenderer.cs src/AcDream.App/UI/UiRenderContext.cs src/AcDream.App/UI/UiElement.cs src/AcDream.App/UI/UiRoot.cs tests/AcDream.App.Tests/Rendering/TextRendererDrawLayerTests.cs
git commit -m "ui: an upper draw layer, so window text never shows through what is above windows

Claude-Session: https://claude.ai/code/session_01EtugJHhFMqGXY2V6Lz2ZFB"
```

```json:metadata
{"files": ["src/AcDream.App/Rendering/TextRenderer.cs", "src/AcDream.App/UI/UiRenderContext.cs", "src/AcDream.App/UI/UiElement.cs", "src/AcDream.App/UI/UiRoot.cs", "tests/AcDream.App.Tests/Rendering/TextRendererDrawLayerTests.cs"], "verifyCommand": "dotnet test tests/AcDream.App.Tests --filter \"FullyQualifiedName~TextRendererDrawLayerTests|FullyQualifiedName~UiRectOutlinePainterOrderTests|FullyQualifiedName~UiRenderContextAlphaTests|FullyQualifiedName~TextRenderer|FullyQualifiedName~UiRoot|FullyQualifiedName~CharacterManagementUiControllerTests|FullyQualifiedName~ConnectionMeterAnimationTests\"", "acceptanceCriteria": ["upper sprite after main rect", "Begin resets to Main; OverlayMode maps to overlay/main", "root switches at the floor; single-layer order otherwise", "debug accessors cover main+upper; existing tests unchanged"], "modelTier": "standard"}
```

---

### Task 5: Canvas stacks, the layer over windows, and drag pass-through

**Goal:** Each canvas mounts into its layer's stack, grouped per plugin and ranked by `(ZOrder, registration)`, with `AboveWindows` canvases in a new click-through layer over every window; no canvas answers the pointer during a drag.

**Files:**
- Create: `src/AcDream.App/UI/Layout/PluginCanvasStack.cs`
- Modify: `src/AcDream.App/UI/Layout/UiOverlayHost.cs`
- Modify: `src/AcDream.App/UI/Layout/PluginCanvasElement.cs:139-140`
- Modify: `src/AcDream.App/Plugins/BufferedUiRegistry.cs` (`DrainCanvases`)
- Modify: `src/AcDream.App/UI/RetailUiRuntime.cs:391, 4175-4222`
- Create: `tests/AcDream.App.Tests/UI/Layout/PluginCanvasStackTests.cs`

**Acceptance Criteria:**
- [ ] The world stack's layer hangs from the overlay host root (z −9000); the above-windows layer hangs from the root at `UiBands.CanvasesAboveWindows`, is visible, and follows `SetViewport`
- [ ] Within a plugin: higher `ZOrder` on top, equal values in registration order; element z-orders are 0…n−1
- [ ] A plugin whose first canvas mounted later is drawn above an earlier plugin whatever their `ZOrder`s
- [ ] Setting `ZOrder` restacks on the next tick; a disposed canvas leaves its group and the ranks close up
- [ ] Every `World` canvas draws before every window, every `AboveWindows` canvas after
- [ ] An `AboveWindows` input canvas is picked over a window and gets Down/Up; outside it the window answers; a `World` input canvas under a window is not picked there
- [ ] Under a modal dialog an `AboveWindows` input canvas is not picked and gets no events
- [ ] A drag's drop over an `AboveWindows` input canvas reaches the window beneath, and the canvas gets no events
- [ ] `DrainCanvases` returns registrations in registration order after a removal
- [ ] The drag and drain tests fail with their fixes removed (checked in the prototype)

**Verify:** `dotnet test tests/AcDream.App.Tests --filter "FullyQualifiedName~PluginCanvas|FullyQualifiedName~UiOverlayHost|FullyQualifiedName~BufferedUiRegistry|FullyQualifiedName~ProjectileDebugOverlay|FullyQualifiedName~WorldLabel"` → `Passed!  - Failed:     0, Passed:   117`

**Steps:**

- [ ] **Step 1: Write the failing tests**

Create `tests/AcDream.App.Tests/UI/Layout/PluginCanvasStackTests.cs`:

```csharp
using System.Collections.Generic;
using System.Linq;
using AcDream.App.Plugins;
using AcDream.App.Rendering;
using AcDream.App.Rendering.Gpu;
using AcDream.App.Tests.Rendering.Gpu;
using AcDream.App.UI;
using AcDream.App.UI.Layout;
using AcDream.Plugin.Abstractions;

namespace AcDream.App.Tests.UI.Layout;

/// <summary>
/// Canvases are stacked in two layers, under and over every window. Each
/// plugin has a group per layer, stacked in the order its first canvas
/// mounted; inside a group its canvases are ranked by ZOrder, then by
/// registration, and restacked when the plugin changes a ZOrder. A canvas
/// over windows takes the pointer from the windows beneath it, gets
/// nothing under a modal dialog, and lets a drag's drop through.
/// </summary>
public sealed class PluginCanvasStackTests
{
    private sealed class FrameSource : ICurrentGpuFrameSource
    {
        public IGpuFrame? CurrentFrame { get; set; }
    }

    /// <summary>A window that can start a drag and records what is dropped on it.</summary>
    private sealed class DragWindow : UiPanel
    {
        public List<object?> Drops { get; } = [];
        public bool StartsDrags { get; init; }

        public override bool IsDragSource => StartsDrags;

        public override object? GetDragPayload() => StartsDrags ? "item" : null;

        public override bool OnEvent(in UiEvent e)
        {
            if (e.Type == UiEventType.DropReleased && ReferenceEquals(e.Target, this))
            {
                Drops.Add(e.Payload);
                return true;
            }
            return base.OnEvent(in e);
        }
    }

    private sealed class Harness
    {
        private readonly PluginCanvasSurface _surface;
        private long _now;

        public Harness()
        {
            Host = UiOverlayHost.Mount(Root);
            World = new PluginCanvasStack(Host.AddLayer("PluginCanvases"));
            AboveWindows = new PluginCanvasStack(Host.AddLayerAboveWindows("PluginCanvasesAboveWindows"));
            var services = new PluginCanvasHostServices(new RecordingGpuDevice(), new FrameSource(), "unused", null);
            _surface = new PluginCanvasSurface(services, font: null);
        }

        public BufferedUiRegistry Registry { get; } = new();
        public UiRoot Root { get; } = new() { Width = 800f, Height = 600f };
        public UiOverlayHost Host { get; }
        public PluginCanvasStack World { get; }
        public PluginCanvasStack AboveWindows { get; }
        public List<PluginPointerEvent> Events { get; } = [];

        /// <summary>Mounts as the runtime does: into the stack of the descriptor's layer.</summary>
        public (PluginCanvasRegistration Registration, PluginCanvasElement Element) Mount(
            PluginUiOwner owner, PluginCanvasDescriptor descriptor)
        {
            var registration = (PluginCanvasRegistration)Registry.RegisterCanvas(owner, descriptor, _ => { });
            ((IPluginCanvas)registration).PointerHandler = Events.Add;
            Assert.Single(Registry.DrainCanvases());
            var element = new PluginCanvasElement(registration, _surface, () => Registry.FindImages(owner));
            PluginCanvasStack stack = descriptor.Layer == PluginCanvasLayer.AboveWindows ? AboveWindows : World;
            stack.Add(element);
            Registry.CompleteCanvasMount(registration, () =>
            {
                PluginCanvasStack.Remove(element);
                element.ReleaseTargets();
            });
            Tick();
            return (registration, element);
        }

        public void Tick()
        {
            _now += 16;
            Root.Tick(0.016, _now);
        }

        /// <summary>Every canvas element under the root, back to front, as the root draws them.</summary>
        public List<PluginCanvasElement> DrawOrder()
        {
            var order = new List<PluginCanvasElement>();
            Collect(Root, order);
            return order;
        }

        private static void Collect(UiElement element, List<PluginCanvasElement> order)
        {
            if (element is PluginCanvasElement canvas)
                order.Add(canvas);
            foreach (UiElement child in element.ChildrenBackToFrontSnapshot())
                Collect(child, order);
        }
    }

    private static readonly PluginUiOwner A = new("a.plugin", "A");
    private static readonly PluginUiOwner B = new("b.plugin", "B");

    private static PluginCanvasDescriptor Canvas(
        string id,
        int zOrder = 0,
        PluginCanvasLayer layer = PluginCanvasLayer.World,
        bool input = false) =>
        new(id, 100, 100) { ZOrder = zOrder, Layer = layer, AcceptsPointerInput = input };

    private static DragWindow Window(UiRoot root, bool startsDrags = false, float left = 0f)
    {
        var window = new DragWindow { Left = left, Width = 200f, Height = 200f, StartsDrags = startsDrags };
        root.AddChild(window);
        root.BringToFront(window);
        return window;
    }

    [Fact]
    public void TheTwoLayersSitUnderAndOverTheWindowBand()
    {
        var harness = new Harness();

        Assert.Same(harness.Host.Root, harness.World.Layer.Parent);
        Assert.Equal(UiOverlayZOrder.SharedHostRoot, harness.Host.Root.ZOrder);
        Assert.Same(harness.Root, harness.AboveWindows.Layer.Parent);
        Assert.Equal(UiBands.CanvasesAboveWindows, harness.AboveWindows.Layer.ZOrder);
        Assert.True(harness.World.Layer.Visible);
        Assert.True(harness.AboveWindows.Layer.Visible);
        Assert.Equal((800f, 600f), (harness.AboveWindows.Layer.Width, harness.AboveWindows.Layer.Height));

        harness.Host.SetViewport(new System.Numerics.Vector2(1024f, 768f));
        Assert.Equal((1024f, 768f), (harness.AboveWindows.Layer.Width, harness.AboveWindows.Layer.Height));
    }

    [Fact]
    public void WithinAPluginHigherZOrderIsOnTopAndEqualValuesKeepRegistrationOrder()
    {
        var harness = new Harness();
        (_, PluginCanvasElement top) = harness.Mount(A, Canvas("top", zOrder: 5));
        (_, PluginCanvasElement first) = harness.Mount(A, Canvas("first"));
        (_, PluginCanvasElement second) = harness.Mount(A, Canvas("second"));
        (_, PluginCanvasElement bottom) = harness.Mount(A, Canvas("bottom", zOrder: -3));

        Assert.Equal([bottom, first, second, top], harness.DrawOrder());
        Assert.Equal([0, 1, 2, 3], harness.DrawOrder().Select(element => element.ZOrder));
    }

    [Fact]
    public void APluginsZOrderNeverLiftsItOverAPluginThatMountedFirst()
    {
        var harness = new Harness();
        (_, PluginCanvasElement b) = harness.Mount(B, Canvas("hud"));
        (_, PluginCanvasElement a) = harness.Mount(A, Canvas("hud", zOrder: 1_000));
        (_, PluginCanvasElement bLater) = harness.Mount(B, Canvas("map", zOrder: -1_000));

        Assert.Equal([bLater, b, a], harness.DrawOrder());
        Assert.NotSame(b.Parent, a.Parent);
        Assert.Same(b.Parent, bLater.Parent);
    }

    [Fact]
    public void ChangingZOrderRestacksOnTheNextTick()
    {
        var harness = new Harness();
        (PluginCanvasRegistration back, PluginCanvasElement backElement) = harness.Mount(A, Canvas("back"));
        (_, PluginCanvasElement frontElement) = harness.Mount(A, Canvas("front"));
        Assert.Equal([backElement, frontElement], harness.DrawOrder());

        ((IPluginCanvas)back).ZOrder = 1;
        harness.Tick();

        Assert.Equal([frontElement, backElement], harness.DrawOrder());
    }

    [Fact]
    public void ADisposedCanvasLeavesItsGroupAndTheRestCloseUp()
    {
        var harness = new Harness();
        (PluginCanvasRegistration first, PluginCanvasElement firstElement) = harness.Mount(A, Canvas("first"));
        (_, PluginCanvasElement second) = harness.Mount(A, Canvas("second"));
        (_, PluginCanvasElement third) = harness.Mount(A, Canvas("third"));

        first.Dispose();

        Assert.Null(firstElement.Parent);
        Assert.Equal([second, third], harness.DrawOrder());
        Assert.Equal([0, 1], harness.DrawOrder().Select(element => element.ZOrder));
    }

    [Fact]
    public void EveryWorldCanvasDrawsBeforeEveryWindowAndEveryCanvasOverWindowsAfter()
    {
        var harness = new Harness();
        DragWindow window = Window(harness.Root);
        (_, PluginCanvasElement under) = harness.Mount(A, Canvas("under", zOrder: 9));
        (_, PluginCanvasElement over) = harness.Mount(A, Canvas("over", layer: PluginCanvasLayer.AboveWindows));

        UiElement[] rootOrder = harness.Root.ChildrenBackToFrontSnapshot();
        int Index(UiElement element)
        {
            while (!ReferenceEquals(element.Parent, harness.Root))
                element = element.Parent!;
            return Array.IndexOf(rootOrder, element);
        }

        Assert.True(Index(under) < Index(window));
        Assert.True(Index(window) < Index(over));
    }

    [Fact]
    public void AnInputCanvasOverWindowsTakesThePointerFromTheWindowBeneath()
    {
        var harness = new Harness();
        DragWindow window = Window(harness.Root);
        (_, PluginCanvasElement over) = harness.Mount(A, Canvas("over", layer: PluginCanvasLayer.AboveWindows, input: true));
        (_, PluginCanvasElement under) = harness.Mount(B, Canvas("under", input: true));

        Assert.Same(over, harness.Root.Pick(50, 50));
        Assert.NotSame(under, harness.Root.Pick(50, 50));
        harness.Root.OnMouseDown(UiMouseButton.Left, 50, 50);
        harness.Root.OnMouseUp(UiMouseButton.Left, 50, 50);
        Assert.Equal([PluginPointerEventKind.Down, PluginPointerEventKind.Up], harness.Events.Select(e => e.Kind));

        // Outside the canvas the window beneath still answers.
        Assert.NotSame(over, harness.Root.Pick(150, 150));
        Assert.NotNull(harness.Root.Pick(150, 150));
    }

    [Fact]
    public void AWorldInputCanvasUnderAWindowGetsNothingWhereTheWindowIs()
    {
        var harness = new Harness();
        (_, PluginCanvasElement under) = harness.Mount(A, Canvas("under", input: true));
        Window(harness.Root);

        Assert.NotSame(under, harness.Root.Pick(50, 50));
    }

    [Fact]
    public void UnderAModalDialogACanvasOverWindowsGetsNoInput()
    {
        var harness = new Harness();
        harness.Mount(A, Canvas("over", layer: PluginCanvasLayer.AboveWindows, input: true));
        var dialog = new UiPanel { Left = 400f, Top = 400f, Width = 100f, Height = 100f };
        harness.Root.AddChild(dialog);
        harness.Root.BringToFront(dialog, UiBand.DialogsAndTooltips);
        harness.Root.Modal = dialog;

        Assert.Null(harness.Root.Pick(50, 50));
        harness.Root.OnMouseDown(UiMouseButton.Left, 50, 50);
        harness.Root.OnMouseUp(UiMouseButton.Left, 50, 50);

        Assert.Empty(harness.Events);
    }

    [Fact]
    public void ADragsDropGoesThroughACanvasToTheWindowBeneath()
    {
        var harness = new Harness();
        DragWindow source = Window(harness.Root, startsDrags: true, left: 400f);
        DragWindow target = Window(harness.Root);
        harness.Mount(A, Canvas("over", layer: PluginCanvasLayer.AboveWindows, input: true));

        harness.Root.OnMouseDown(UiMouseButton.Left, 450, 50);
        harness.Root.OnMouseMove(300, 50);
        harness.Root.OnMouseMove(50, 50);
        Assert.Same(source, harness.Root.DragSource);
        Assert.Same(target, harness.Root.Pick(50, 50));
        harness.Root.OnMouseUp(UiMouseButton.Left, 50, 50);

        Assert.Equal(["item"], target.Drops);
        Assert.Empty(harness.Events);
        Assert.Null(harness.Root.DragSource);
    }

    [Fact]
    public void DrainingHandsCanvasesOutInRegistrationOrderEvenAfterRemovals()
    {
        var registry = new BufferedUiRegistry();
        IPluginCanvas gone = registry.RegisterCanvas(A, Canvas("gone"), _ => { });
        registry.RegisterCanvas(A, Canvas("first"), _ => { });
        gone.Dispose();
        registry.RegisterCanvas(B, Canvas("second"), _ => { });
        registry.RegisterCanvas(A, Canvas("third"), _ => { });

        Assert.Equal(
            ["first", "second", "third"],
            registry.DrainCanvases().Select(registration => registration.CanvasId));
    }
}
```

- [ ] **Step 2: Run them to see them fail**

Run: `dotnet build tests/AcDream.App.Tests 2>&1 | grep -c "error CS"`
Expected: non-zero (`PluginCanvasStack` and `AddLayerAboveWindows` do not exist).

- [ ] **Step 3: The stack and its groups**

Create `src/AcDream.App/UI/Layout/PluginCanvasStack.cs`:

```csharp
using System.Collections.Generic;
using AcDream.Plugin.Abstractions;

namespace AcDream.App.UI.Layout;

/// <summary>
/// One of the two layers plugin canvases are drawn in (see
/// <see cref="PluginCanvasLayer"/>), holding one group per plugin. Groups
/// are stacked in the order their plugins first mounted a canvas here,
/// first lowest, so no plugin can lift its canvases over another's. Inside
/// a group the canvases are stacked by the plugin's
/// <see cref="IPluginCanvas.ZOrder"/>, then by registration.
/// </summary>
internal sealed class PluginCanvasStack
{
    private readonly UiOverlayLayer _layer;
    private readonly Dictionary<string, PluginCanvasGroup> _groups = new(StringComparer.Ordinal);

    /// <summary>Takes over <paramref name="layer"/>, which the overlay host keeps equal to the viewport, and shows it.</summary>
    internal PluginCanvasStack(UiOverlayLayer layer)
    {
        _layer = layer ?? throw new ArgumentNullException(nameof(layer));
        _layer.Visible = true;
    }

    internal UiOverlayLayer Layer => _layer;

    /// <summary>Adds <paramref name="element"/> to its plugin's group, making the group on the plugin's first canvas here.</summary>
    internal void Add(PluginCanvasElement element)
    {
        ArgumentNullException.ThrowIfNull(element);
        string ownerId = element.Registration.Owner.Id;
        if (!_groups.TryGetValue(ownerId, out PluginCanvasGroup? group))
        {
            // Groups are never removed, so the count is a unique, rising rank.
            group = new PluginCanvasGroup { Name = $"PluginCanvases:{ownerId}", ZOrder = _groups.Count };
            _groups.Add(ownerId, group);
            _layer.AddChild(group);
            group.FollowParent();
        }
        group.Add(element);
    }

    /// <summary>Takes <paramref name="element"/> out of its group; does nothing if it is not in one.</summary>
    internal static void Remove(PluginCanvasElement element)
    {
        ArgumentNullException.ThrowIfNull(element);
        element.Parent?.RemoveChild(element);
    }
}

/// <summary>
/// One plugin's canvases in one layer: a click-through panel the size of
/// the layer, whose children are ranked every tick by
/// <c>(ZOrder, registration)</c>. Each canvas's interface z-order is its
/// rank, so no two are ever equal and the order never depends on how the
/// sort treats ties.
/// </summary>
internal sealed class PluginCanvasGroup : UiOverlayLayer
{
    private readonly List<PluginCanvasElement> _canvases = [];

    internal void Add(PluginCanvasElement element)
    {
        AddChild(element, takesInput: element.Registration.AcceptsPointerInput);
        _canvases.Add(element);
        Rank();
    }

    public override bool RemoveChild(UiElement child)
    {
        if (!base.RemoveChild(child)) return false;
        if (child is PluginCanvasElement canvas)
        {
            _canvases.Remove(canvas);
            Rank();
        }
        return true;
    }

    /// <summary>Before the canvases tick: follow the layer's size, then pick up any change of a plugin's order.</summary>
    protected override void OnTick(double deltaSeconds)
    {
        FollowParent();
        Rank();
    }

    internal void FollowParent()
    {
        Left = 0f;
        Top = 0f;
        Width = Parent?.Width ?? 0f;
        Height = Parent?.Height ?? 0f;
    }

    private void Rank()
    {
        _canvases.Sort(static (a, b) =>
        {
            int byOrder = a.Registration.ZOrder.CompareTo(b.Registration.ZOrder);
            return byOrder != 0 ? byOrder : a.Registration.Id.CompareTo(b.Registration.Id);
        });
        for (int rank = 0; rank < _canvases.Count; rank++)
            _canvases[rank].ZOrder = rank;
    }
}
```

- [ ] **Step 4: The layer over windows**

In `src/AcDream.App/UI/Layout/UiOverlayHost.cs`, replace

```csharp
internal sealed class UiOverlayHost
{
    private readonly UiOverlayLayer _root;
    private readonly List<UiOverlayLayer> _layers = [];
    private Vector2 _viewport;

    private UiOverlayHost(UiOverlayLayer root, Vector2 viewport)
    {
        _root = root;
        _viewport = viewport;
    }
```

with

```csharp
internal sealed class UiOverlayHost
{
    private readonly UiRoot _host;
    private readonly UiOverlayLayer _root;
    private readonly List<UiOverlayLayer> _layers = [];
    private Vector2 _viewport;

    private UiOverlayHost(UiRoot host, UiOverlayLayer root, Vector2 viewport)
    {
        _host = host;
        _root = root;
        _viewport = viewport;
    }
```

replace `var overlayHost = new UiOverlayHost(root, new Vector2(host.Width, host.Height));` with `var overlayHost = new UiOverlayHost(host, root, new Vector2(host.Width, host.Height));`, and insert before the `/// <summary>` of `SetViewport` (the one starting `/// Points the host at the rectangle the world is being drawn into.`):

```csharp
    /// <summary>
    /// A layer over every window, for the plugin canvases drawn there. It
    /// is the one layer not under the shared root: it hangs from the
    /// interface root itself at <see cref="UiBands.CanvasesAboveWindows"/>,
    /// above the window band and under screens, dialogs and tooltips (see
    /// <see cref="UiBand"/>). Like every layer it starts hidden, covers the
    /// viewport and is click-through except where a child takes input.
    /// </summary>
    internal UiOverlayLayer AddLayerAboveWindows(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        var layer = new UiOverlayLayer
        {
            Name = name,
            Visible = false,
            ZOrder = UiBands.CanvasesAboveWindows,
        };
        _layers.Add(layer);
        _host.AddChild(layer);
        ApplyViewport(layer);
        return layer;
    }
```

- [ ] **Step 5: No canvas under a drag; drain in order**

In `src/AcDream.App/UI/Layout/PluginCanvasElement.cs`, replace

```csharp
    protected override bool OnHitTest(float localX, float localY) =>
        TakesInput && base.OnHitTest(localX, localY);
```

with

```csharp
    // While something is being dragged no canvas answers, so the drop
    // reaches the window or the world beneath.
    protected override bool OnHitTest(float localX, float localY) =>
        TakesInput && FindRoot()?.DragSource is null && base.OnHitTest(localX, localY);
```

In `src/AcDream.App/Plugins/BufferedUiRegistry.cs`, in `DrainCanvases`, replace

```csharp
                registration.Drained = true;
                pending.Add(registration);
            }
            return pending;
```

with

```csharp
                registration.Drained = true;
                pending.Add(registration);
            }
            // Registration order, which the dictionary does not keep once
            // entries have been removed: plugins' canvas groups stack in the
            // order their first canvas mounts.
            pending.Sort(static (a, b) => a.Id.CompareTo(b.Id));
            return pending;
```

- [ ] **Step 6: Mount into the stacks**

In `src/AcDream.App/UI/RetailUiRuntime.cs`, replace the field

```csharp
    private Layout.UiOverlayLayer? _pluginCanvasLayer;
```

with

```csharp
    private Layout.PluginCanvasStack? _worldCanvases;
    private Layout.PluginCanvasStack? _canvasesAboveWindows;
```

Replace the start of `MountPluginCanvases`'s summary

```csharp
    /// <summary>
    /// Mounts every canvas registered since the last drain on the shared
    /// overlay layer.
```

with

```csharp
    /// <summary>
    /// The stack a canvas in <paramref name="layer"/> joins, made on first
    /// use. Anything but <see cref="PluginCanvasLayer.AboveWindows"/> is the
    /// world layer under every window.
    /// </summary>
    private Layout.PluginCanvasStack CanvasStack(PluginCanvasLayer layer) =>
        layer == PluginCanvasLayer.AboveWindows
            ? _canvasesAboveWindows ??= new Layout.PluginCanvasStack(
                OverlayHost.AddLayerAboveWindows("PluginCanvasesAboveWindows"))
            : _worldCanvases ??= new Layout.PluginCanvasStack(OverlayHost.AddLayer("PluginCanvases"));

    /// <summary>
    /// Mounts every canvas registered since the last drain in its layer's
    /// stack.
```

(the rest of that summary, from `The same shape as the window mount:`, stays). In the method body replace

```csharp
                if (_pluginCanvasLayer is null)
                {
                    _pluginCanvasLayer = OverlayHost.AddLayer("PluginCanvases");
                    _pluginCanvasLayer.Visible = true;
                }
                Layout.UiOverlayLayer layer = _pluginCanvasLayer;
                PluginUiOwner owner = canvas.Owner;
```

with

```csharp
                Layout.PluginCanvasStack stack = CanvasStack(canvas.Layer);
                PluginUiOwner owner = canvas.Owner;
```

and

```csharp
                layer.AddChild(element, takesInput: canvas.AcceptsPointerInput);
                plugins.CompleteCanvasMount(canvas, () =>
                {
                    layer.RemoveChild(element);
                    element.ReleaseTargets();
                });
```

with

```csharp
                stack.Add(element);
                plugins.CompleteCanvasMount(canvas, () =>
                {
                    Layout.PluginCanvasStack.Remove(element);
                    element.ReleaseTargets();
                });
```

`grep -n "_pluginCanvasLayer" src/AcDream.App/UI/RetailUiRuntime.cs` must print nothing.

- [ ] **Step 7: Run the tests**

Run the **Verify** command. Expected: `Passed!  - Failed:     0, Passed:   117`.

- [ ] **Step 8: Commit**

```bash
git add src/AcDream.App/UI/Layout/PluginCanvasStack.cs src/AcDream.App/UI/Layout/UiOverlayHost.cs src/AcDream.App/UI/Layout/PluginCanvasElement.cs src/AcDream.App/Plugins/BufferedUiRegistry.cs src/AcDream.App/UI/RetailUiRuntime.cs tests/AcDream.App.Tests/UI/Layout/PluginCanvasStackTests.cs
git commit -m "plugin canvas: a layer over windows, and canvases stacked per plugin by ZOrder

Claude-Session: https://claude.ai/code/session_01EtugJHhFMqGXY2V6Lz2ZFB"
```

```json:metadata
{"files": ["src/AcDream.App/UI/Layout/PluginCanvasStack.cs", "src/AcDream.App/UI/Layout/UiOverlayHost.cs", "src/AcDream.App/UI/Layout/PluginCanvasElement.cs", "src/AcDream.App/Plugins/BufferedUiRegistry.cs", "src/AcDream.App/UI/RetailUiRuntime.cs", "tests/AcDream.App.Tests/UI/Layout/PluginCanvasStackTests.cs"], "verifyCommand": "dotnet test tests/AcDream.App.Tests --filter \"FullyQualifiedName~PluginCanvas|FullyQualifiedName~UiOverlayHost|FullyQualifiedName~BufferedUiRegistry|FullyQualifiedName~ProjectileDebugOverlay|FullyQualifiedName~WorldLabel\"", "acceptanceCriteria": ["two layers at their z-orders, viewport followed", "rank by ZOrder then registration", "plugin groups by first mount", "restack on tick; dispose closes up", "world canvases under windows, above-windows over", "input over windows; world input canvas covered", "modal blocks", "drag drop passes through", "drain in registration order", "drag and drain tests fail without fixes"], "modelTier": "standard"}
```

---

### Task 6: Documentation

**Goal:** The plugin API guide describes the two layers, the stacking order and `ZOrder`, and the markup guide's test list names the new suites.

**Files:**
- Modify: `docs/plugin-api.md` (section "Canvases")
- Modify: `docs/plugin-ui-markup.md` (Tests paragraph, around line 401)

**Acceptance Criteria:**
- [ ] The Canvases intro no longer says canvases are only "under every window", and links to `#layers-and-order`
- [ ] A `### Layers and order` subsection, before `### Pointer input`, gives both layers, the full bottom-to-top order, the per-plugin grouping, `ZOrder` semantics with an example, and the windowless and older-host answers
- [ ] Pointer input says an `AboveWindows` input canvas takes the pointer from windows beneath, a `World` one gets nothing under a window, and that modals and drags give canvases no input
- [ ] `plugin-ui-markup.md` names `PluginCanvasStackTests`, `UiBandTests`, `TextRendererDrawLayerTests`, `ScopedUiRegistryCanvasTests` and `PluginCanvasLayerContractTests`

**Verify:** `grep -c "Layers and order\|PluginCanvasLayer.AboveWindows" docs/plugin-api.md && grep -c "PluginCanvasStackTests" docs/plugin-ui-markup.md` → at least `4` and `1`

**Steps:**

- [ ] **Step 1: The guide**

In `docs/plugin-api.md`, replace

```markdown
A canvas is a rectangle the plugin paints, shown over the world and under
every window, taking no input unless it asks for it (see
[Pointer input](#pointer-input) below).
```

with

```markdown
A canvas is a rectangle the plugin paints, shown over the world and either
under every window or over them (see [Layers and order](#layers-and-order)
below), taking no input unless it asks for it (see
[Pointer input](#pointer-input) below).
```

replace

```markdown
`RegisterCanvas` throws past either. `IsVisible`, `Anchor` and `Offset`
can be set at any time;
```

with

```markdown
`RegisterCanvas` throws past either. `IsVisible`, `Anchor`, `Offset` and
`ZOrder` can be set at any time;
```

insert before `### Pointer input`:

````markdown
### Layers and order

A canvas is drawn in one of two layers, chosen with `Layer` on the
descriptor and fixed from then on:

- `PluginCanvasLayer.World`, the default: over the world and its labels,
  under every window. A HUD here never hides the interface.
- `PluginCanvasLayer.AboveWindows`: over every window, under dialogs,
  tooltips, menus and the item being dragged. It suits something the
  player must see over their windows, such as a remote-control widget; it
  also covers whatever window is beneath it, so keep it small or let the
  player hide it.

From the bottom up the interface stacks the world and world labels,
`World` canvases, windows, `AboveWindows` canvases, the pre-game screens
(connecting, character select and creation, credits), dialogs and
tooltips, and last menus and the drag ghost. No canvas is ever seen over
a pre-game screen.

Within a layer each plugin's canvases are kept together, and plugins are
stacked in the order their first canvas there was mounted, so the numbers
one plugin picks never lift its canvases over another plugin's. Among one
plugin's canvases in a layer `ZOrder` decides: higher is drawn on top, and
equal values keep the order the canvases were registered in. It starts at
the descriptor's `ZOrder` and can be set on the canvas at any time; the
canvases restack on the next frame.

```csharp
IPluginCanvas remote = host.Ui.RegisterCanvas(
    new PluginCanvasDescriptor("remote", 160, 48)
    {
        Layer = PluginCanvasLayer.AboveWindows,
        Anchor = PluginCanvasAnchor.TopCenter,
        AcceptsPointerInput = true,
    },
    painter => DrawRemote(painter));
IPluginCanvas help = host.Ui.RegisterCanvas(
    new PluginCanvasDescriptor("help", 160, 120)
    {
        Layer = PluginCanvasLayer.AboveWindows,
        Anchor = PluginCanvasAnchor.TopCenter,
        Offset = new PluginPoint(0, 40),
        ZOrder = -1,
    },
    painter => DrawHelp(painter));

// while the player hovers the remote's help button, show the help over it
help.ZOrder = 1;
```

Without a window the layer and `ZOrder` are kept and nothing is stacked.
A host that predates layers draws every canvas in the world layer and
answers `ZOrder` with 0.
````

and in Pointer input replace

```markdown
beneath gets no mouse there. Outside the rectangle nothing changes.
Without a handler an opted-in canvas stays click-through, since nobody is
listening.
```

with

```markdown
beneath gets no mouse there. Outside the rectangle nothing changes.
Without a handler an opted-in canvas stays click-through, since nobody is
listening. An `AboveWindows` canvas with input takes the pointer from the
windows beneath its rectangle in the same way; a `World` canvas gets
nothing where a window covers it. While a modal dialog is open no canvas
gets input, and while the player drags an item no canvas answers, so the
drop reaches the window or the world beneath.
```

- [ ] **Step 2: The test list**

In `docs/plugin-ui-markup.md`, after the line `and headless suites for the inert answers a host without a window gives.` add:

```markdown
Canvas layers and stacking are covered by `UI/Layout/PluginCanvasStackTests`,
`UI/UiBandTests` and `Rendering/TextRendererDrawLayerTests` under
`tests/AcDream.App.Tests/`, by `ScopedUiRegistryCanvasTests` for the scoped
forwarder, and by `PluginCanvasLayerContractTests` for the inert answers of
a host that predates layers.
```

- [ ] **Step 3: Commit**

```bash
git add docs/plugin-api.md docs/plugin-ui-markup.md
git commit -m "docs: canvas layers and stacking order

Claude-Session: https://claude.ai/code/session_01EtugJHhFMqGXY2V6Lz2ZFB"
```

```json:metadata
{"files": ["docs/plugin-api.md", "docs/plugin-ui-markup.md"], "verifyCommand": "grep -c \"Layers and order\\|PluginCanvasLayer.AboveWindows\" docs/plugin-api.md && grep -c \"PluginCanvasStackTests\" docs/plugin-ui-markup.md", "acceptanceCriteria": ["intro updated with link", "Layers and order subsection", "pointer input paragraph", "test list updated"], "modelTier": "mechanical"}
```

---

### Task 7: Full verification and push

**Goal:** The branch builds with 0 warnings, the full portable suite shows no failures beyond Task 0's baseline, and the branch is pushed to `origin`.

**Files:** none

**Acceptance Criteria:**
- [ ] `dotnet build AcDream.slnx -c Release` → `0 Warning(s)`, `0 Error(s)`
- [ ] The portable suite with `TMPDIR=/tmp/` fails only tests listed in `/tmp/openac-layers/baseline-failures.txt` (HostParity peer tests may vary)
- [ ] No staged or committed `packages.*.lock.json` changes, and nothing under `docs/superpowers`
- [ ] `git log --oneline upstream/main..HEAD` shows the six commits of Tasks 1–6
- [ ] `origin/painter-v2/layers` equals `HEAD`

**Verify:** `git fetch origin && test "$(git rev-parse HEAD)" = "$(git rev-parse origin/painter-v2/layers)" && echo pushed` → `pushed`

**Steps:**

- [ ] **Step 1: Build and test everything**

```bash
dotnet build AcDream.slnx -c Release 2>&1 | tail -3
TMPDIR=/tmp/ dotnet test AcDream.slnx -c Release --no-build --filter "Lane!=InstalledDat&Lane!=PreparedPackage&Lane!=Live&Lane!=Manual&Lane!=Timing&Lane!=Windows&Lane!=Linux&Lane!=MacOS&Lane!=Unix&Lane!=Vulkan&Lane!=SystemFont&Purpose!=Diagnostic&Status!=KnownFailure" 2>&1 \
  | grep -E "^\s+Failed " | sed -E 's/ \[[^]]*\]$//' | sort -u > /tmp/openac-layers/final-failures.txt
comm -23 /tmp/openac-layers/final-failures.txt /tmp/openac-layers/baseline-failures.txt | grep -v "HostParity.Tests.Peer" || echo "no new failures"
```

Expected: `0 Warning(s)`, `0 Error(s)`; `no new failures`. If a non-peer test fails, investigate with superpowers-extended-cc:systematic-debugging before going on.

- [ ] **Step 2: Check what the branch carries**

```bash
git checkout -- '*.lock.json'
git status --short
git log --oneline upstream/main..HEAD
git diff --name-only upstream/main..HEAD | grep -E "lock.json|docs/superpowers" || echo clean
```

Expected: a clean status, six commits, `clean`.

- [ ] **Step 3: Push**

```bash
git push -u origin painter-v2/layers
git fetch origin && test "$(git rev-parse HEAD)" = "$(git rev-parse origin/painter-v2/layers)" && echo pushed
```

Expected: `pushed`. Do not open a pull request: ask the user which repository it goes to.

```json:metadata
{"files": [], "verifyCommand": "git fetch origin && test \"$(git rev-parse HEAD)\" = \"$(git rev-parse origin/painter-v2/layers)\" && echo pushed", "acceptanceCriteria": ["0 warnings", "no portable failures beyond baseline", "no lock files or docs/superpowers", "six commits", "pushed to origin"], "modelTier": "mechanical"}
```

---

### Task 8: Merge into fork main

**Goal:** Fork `main` takes `painter-v2/layers` with a merge commit, as it took PR 3 and PR 4, with the conflicts against PRs 0, 1, 3 and 4 resolved, builds with 0 warnings, passes the portable suite and the `Lane=Vulkan` canvas tests, and is pushed; the feature branch itself is unchanged.

**Files:** (merge resolutions on `main` only)
- `src/AcDream.App/Rendering/TextRenderer.cs`
- `src/AcDream.App/UI/RetailUiRuntime.cs`
- `docs/plugin-api.md`
- `docs/plugin-ui-markup.md`

**Acceptance Criteria:**
- [ ] `git log -1 --format=%P main` lists two parents: the previous `origin/main` and `painter-v2/layers`
- [ ] `grep -n "_overlay\|_segUsed\|_spriteSegs\|_textBuf\|_rectBuf\|_textVerts\|_rectVerts" src/AcDream.App/Rendering/TextRenderer.cs` prints nothing on `main`
- [ ] `docs/plugin-api.md` on `main` has, in order, `### Shapes`, `### High-density displays`, `### Layers and order`, `### Pointer input` under Canvases
- [ ] `dotnet build AcDream.slnx -c Release` on `main` → `0 Warning(s)`, `0 Error(s)`
- [ ] The portable suite on `main` fails only baseline tests; `Lane=Vulkan` tests matching `PluginCanvas` pass
- [ ] `origin/main` equals local `main`; `origin/painter-v2/layers` is unchanged

**Verify:** `git fetch origin && test "$(git rev-parse main)" = "$(git rev-parse origin/main)" && git log -1 --format=%P main | wc -w` → `2`

**Steps:**

- [ ] **Step 1: Merge**

```bash
# There is no local main yet, and both origin and upstream have one, so name the remote.
git show-ref --verify --quiet refs/heads/main && git switch main || git switch -c main --track origin/main
git pull --ff-only origin main
git merge --no-ff painter-v2/layers -m "Merge painter-v2/layers: canvas layers and banded z-order (PR 5)

Claude-Session: https://claude.ai/code/session_01EtugJHhFMqGXY2V6Lz2ZFB"
```

Expected: conflicts in exactly `docs/plugin-api.md`, `docs/plugin-ui-markup.md`, `src/AcDream.App/Rendering/TextRenderer.cs` and `src/AcDream.App/UI/RetailUiRuntime.cs` (one hunk each).

- [ ] **Step 2: Resolve the four hunks**

- `docs/plugin-api.md`: keep both sides, `main`'s first (`### Shapes` and `### High-density displays`), then a blank line, then the branch's `### Layers and order` subsection.
- `docs/plugin-ui-markup.md`: keep `main`'s side whole (it ends with the CanvasDemo sentence), then append only the branch's five lines starting `Canvas layers and stacking are covered by`; drop the branch's two lines that repeat `ScopedUiRegistryCanvasTests` for the scoped forwarder, and by the contract … gives.`
- `src/AcDream.App/UI/RetailUiRuntime.cs`: the element keeps PR 1's fonts and joins the stack:

```csharp
                    modifiers: HeldPointerModifiers,
                    fonts: () => plugins.FindFonts(owner));
                stack.Add(element);
```

- `src/AcDream.App/Rendering/TextRenderer.cs`: the layer loop keeps PR 0's pipeline tracking:

```csharp
        foreach (DrawLayerBuffers layer in _layers)
            DrawLayer(layer.SpriteSegs, layer.SegUsed, layer.Rects, layer.RectVerts, layer.Text, layer.TextVerts, font, frame, encoder, ref bound);
```

- [ ] **Step 3: Move PR 0, 1 and 3's draw paths onto the layers**

These merged without a textual conflict but still name the removed fields. In `src/AcDream.App/Rendering/TextRenderer.cs` on `main`, replace in `DebugSpriteSegmentCoverage`

```csharp
            var result = new List<uint>(_segUsed);
            for (int i = 0; i < _segUsed; i++)
                result.Add(_spriteSegs[i].Coverage);
```

with

```csharp
            var result = new List<uint>();
            foreach (SpriteSeg seg in DebugSegs())
                result.Add(seg.Coverage);
```

in `DebugSpriteSegmentPremultiplied`

```csharp
            var result = new List<bool>(_segUsed);
            for (int i = 0; i < _segUsed; i++)
                result.Add(_spriteSegs[i].Premultiplied);
```

with

```csharp
            var result = new List<bool>();
            foreach (SpriteSeg seg in DebugSegs())
                result.Add(seg.Premultiplied);
```

in `DrawPremultipliedSprite`

```csharp
        SpriteSeg seg = OverlayMode
            ? NextSpriteSeg(_overlaySpriteSegs, ref _overlaySegUsed, texture, premultiplied: true)
            : NextSpriteSeg(_spriteSegs,        ref _segUsed,        texture, premultiplied: true);
```

with

```csharp
        DrawLayerBuffers layer = Current;
        SpriteSeg seg = NextSpriteSeg(layer.SpriteSegs, ref layer.SegUsed, texture, premultiplied: true);
```

in `DrawCoverageSprite`

```csharp
        SpriteSeg seg = OverlayMode
            ? NextSpriteSeg(_overlaySpriteSegs, ref _overlaySegUsed, UiTextureTableHandle.None, coverage: coverageTexture)
            : NextSpriteSeg(_spriteSegs,        ref _segUsed,        UiTextureTableHandle.None, coverage: coverageTexture);
```

with

```csharp
        DrawLayerBuffers layer = Current;
        SpriteSeg seg = NextSpriteSeg(layer.SpriteSegs, ref layer.SegUsed, UiTextureTableHandle.None, coverage: coverageTexture);
```

and in `DrawTriangles`

```csharp
        SpriteSeg seg = OverlayMode
            ? NextSpriteSeg(_overlaySpriteSegs, ref _overlaySegUsed, UiTextureTableHandle.None)
            : NextSpriteSeg(_spriteSegs,        ref _segUsed,        UiTextureTableHandle.None);
```

with

```csharp
        DrawLayerBuffers layer = Current;
        SpriteSeg seg = NextSpriteSeg(layer.SpriteSegs, ref layer.SegUsed, UiTextureTableHandle.None);
```

`PluginCanvasStackTests` constructs `PluginCanvasHostServices(…, "unused", null)`; on `main` the last parameter is PR 4's `FramebufferPerPoint` and `null` still compiles, so the test needs no change.

- [ ] **Step 4: Build, test, commit the merge**

```bash
grep -n "_overlay\|_segUsed\|_spriteSegs\|_textBuf\|_rectBuf\|_textVerts\|_rectVerts\|<<<<<<<\|>>>>>>>" src/AcDream.App/Rendering/TextRenderer.cs src/AcDream.App/UI/RetailUiRuntime.cs docs/plugin-api.md docs/plugin-ui-markup.md || echo resolved
dotnet build AcDream.slnx -c Release 2>&1 | tail -3
TMPDIR=/tmp/ dotnet test AcDream.slnx -c Release --no-build --filter "Lane!=InstalledDat&Lane!=PreparedPackage&Lane!=Live&Lane!=Manual&Lane!=Timing&Lane!=Windows&Lane!=Linux&Lane!=MacOS&Lane!=Unix&Lane!=Vulkan&Lane!=SystemFont&Purpose!=Diagnostic&Status!=KnownFailure" 2>&1 \
  | grep -E "^\s+Failed " | sed -E 's/ \[[^]]*\]$//' | sort -u > /tmp/openac-layers/merge-failures.txt
comm -23 /tmp/openac-layers/merge-failures.txt /tmp/openac-layers/baseline-failures.txt | grep -v "HostParity.Tests.Peer" || echo "no new failures"
DYLD_FALLBACK_LIBRARY_PATH=/opt/homebrew/lib dotnet test tests/AcDream.App.Tests -c Release --no-build --filter "Lane=Vulkan&FullyQualifiedName~PluginCanvas" 2>&1 | grep -E "Passed!|Failed!"
git add src/AcDream.App/Rendering/TextRenderer.cs src/AcDream.App/UI/RetailUiRuntime.cs docs/plugin-api.md docs/plugin-ui-markup.md
git checkout -- '*.lock.json'
git commit --no-edit
```

Expected: `resolved`; `0 Warning(s)`, `0 Error(s)`; `no new failures`; the Vulkan line `Failed:     0`. The baseline was recorded on upstream `main`; a failure that is new here but also fails on `origin/main` before the merge is not this PR's (check it there before going on).

- [ ] **Step 5: Push**

```bash
git push origin main
git fetch origin && test "$(git rev-parse main)" = "$(git rev-parse origin/main)" && git log -1 --format=%P main | wc -w
git switch painter-v2/layers
```

Expected: `2`.

```json:metadata
{"files": ["src/AcDream.App/Rendering/TextRenderer.cs", "src/AcDream.App/UI/RetailUiRuntime.cs", "docs/plugin-api.md", "docs/plugin-ui-markup.md"], "verifyCommand": "git fetch origin && test \"$(git rev-parse main)\" = \"$(git rev-parse origin/main)\" && git log -1 --format=%P main | wc -w", "acceptanceCriteria": ["merge commit with two parents", "no stale TextRenderer fields", "docs subsections in order", "0 warnings", "no new portable failures; Vulkan canvas tests pass", "pushed; feature branch unchanged"], "modelTier": "standard"}
```
