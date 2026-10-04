# World markers PR 2 (ground shapes) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers-extended-cc:subagent-driven-development (recommended) or superpowers-extended-cc:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Let a plugin lay rings, discs and wedges on the ground, around an object or at a fixed spot, through `layer.SetShapes(...)` on the world-marker layers PR 1 added.

**Architecture:** The contract gains `PluginGroundShape` and `SetShapes`. The PR 1 `PluginWorldMarkerStore` keeps each layer's shapes beside its icons (own cap, own merged snapshot). A pure `GroundShapeTessellator` cuts a shape into triangles lying on the ground; a `PluginGroundShapeRenderer` places each shape (object feet or spot, terrain-following or flat, heading for facing wedges), bounds the frame (250 m, nearest first under budget) and feeds a new `GroundShapeBatch`, a translucent depth-tested, non-depth-writing triangle pipeline with its own `ground_shape` shader. The renderer runs in the Vulkan world phase right after the plugin world lines.

**Tech Stack:** C# / .NET 10, xUnit, the RHI (`IGpuDevice`, `IGpuPassEncoder`, `RecordingGpuDevice` in tests), GLSL compiled to SPIR-V by `tools/compile-shaders.ps1` (pwsh + Silk.NET.Shaderc; verified working on this Mac on 2026-10-03).

**Spec:** `docs/superpowers/specs/2026-10-03-world-markers-design.md` (fork-only docs branch `docs/world-markers-spec`). This plan covers the spec's PR 2 only. PR 1 (icons) is done: branch `world-markers/icons` (HEAD 11be68a0), merged into local fork `main` as a7535b82.

## Global Constraints

- Work in a worktree `.worktrees/world-markers-shapes` on a new branch `world-markers/shapes` from `world-markers/icons` (11be68a0) — stacked on PR 1, as the spec's Delivery says. Never commit `docs/superpowers/**` on that branch.
- Build and test with the pinned SDK: every shell starts with `export DOTNET_ROOT=$HOME/.dotnet PATH=$HOME/.dotnet:$PATH`. Building rewrites some `*.lock.json` files with local RID churn; revert with `git checkout -- '*.lock.json'` before each commit, never commit that churn. Compiling shaders creates an untracked `tools/ShaderCompiler/packages.osx-arm64.lock.json`; delete it, never commit it.
- Additive plugin API only. `SetShapes` is a new member on `IPluginWorldMarkerLayer`, which only the host implements (the PR 1 contract is not yet published upstream), so plugins built against PR 1 still compile and load.
- Every public type and member in `AcDream.Plugin.Abstractions` carries XML docs (the project treats warnings as errors).
- Never throw on bad plugin data: invalid shapes are dropped; an oversized set is refused with `false`. `ArgumentNullException` for a null list and `ObjectDisposedException` after disposal are the only throws, as for icons.
- Limits: `IPluginWorldMarkers.MaximumShapes = 256` per plugin across all its layers, counted apart from icons. Radius 0.1..100 m (out of range is dropped, not clamped). Band width (ring, unfilled arc) must be > 0 and <= radius. Arc sweep must be > 0 and <= 360. Default arc width 0.15 m.
- Geometry: compass bearings, 0 = north (+Y), 90 = east (+X), clockwise; a bearing `b` is the ground direction `(sin b, cos b)`. Edges are cut into ~0.5 m segments, 16..128 per full turn (an arc takes its share, at least 1). Filled shapes are cut into concentric rings ~4 m deep, at most 8. Shapes lie 0.05 m above the ground (or above the anchor's height when flat). Every vertex is pulled 0.2 % of the way toward the camera (depth nudge). Draw range 250 m (`PluginWorldLineRenderer.DrawRangeMeters`), measured from the camera to the shape's centre. An object is "on the land" when its base is within 1 m of the sampled terrain.
- `GroundShapeBatch`: vertex = position `Float3` + colour `Float4` (7 floats, 28 bytes); budget 2 MiB of vertices (`2 * 1024 * 1024 / 28` = 74 898); pipeline `"ground-shape"`, shader set `"ground_shape"`, triangle list, straight alpha, `GpuDepthState.TranslucentDefault`, no culling, the world pass's sample count; `SetDepthWrite(false)` at flush.
- Test baseline on `main`: App 52 failures and Headless 5, all environmental (manual probes, Linux-only, installed-DAT, offscreen Vulkan, live-server). A suite run is green when its failures are exactly those, by name.
- Commit messages end with the line `Claude-Session: https://claude.ai/code/session_01EtugJHhFMqGXY2V6Lz2ZFB`.

**User decisions (already made):**
- Scope of the spec: icons over objects, icons at a spot, ground shapes (ring, filled disc, arc or wedge). No polygons; line upgrades deferred.
- One new surface on `host.Ui` with layers (approach A): shapes go on the same `IPluginWorldMarkerLayer` as icons.
- Shapes are drawn in the world pass: hidden by walls and hills, never hiding each other.
- Branches come from fork work (PR 2 stacked on `world-markers/icons`), merged into fork main with a merge commit, as painter v2 and PR 1 were.
- How a plugin decides what to mark is its business; the client only draws.
- Live checks run against the user's local ACE server via `~/OpenAC-dev/dev-client.env`, never the launcher's Dreamweave profile; on a Retina-only session the user's own ⌘⇧3 screenshot is the evidence (the client's capture has a known 2× size bug).

## Spec corrections made while planning

Found while reading the code; folded into the spec in the same docs commit as this plan:

1. **Depth bias is a nudge along the line of sight, not a pipeline bias.** The RHI has no depth-bias state (`VulkanGpuPipeline` hard-codes `DepthBiasEnable = false`). Each vertex is moved 0.2 % of the way toward the camera instead: it stays at the same place on screen, only its depth comes forward (2 cm at 10 m, 50 cm at 250 m), on top of the 5 cm lift.
2. **Filled shapes are cut into rings as well as segments.** A fan from the centre would cut straight through a hill under a large disc. Discs and wedges are cut into concentric rings about 4 m deep (at most 8), so every ~4 m of radius has vertices sampled on the land.
3. **The heading comes from `WorldLabelAnchor.HeadingDegrees`**, a new init-only property set by `WorldSelectionQuery.TryResolveWorldLabelAnchor` from the entity's rotation (`MoveToMath.GetHeading`, the compass bearing the rest of the client uses), or from the published child pose for a wielded object.
4. **`PluginGroundShapeKind` has `None = 0`**, like `PluginMarkerAnchorKind`, so a default shape is of no kind and is dropped. Rings and discs are normalised to a full turn from north (their start, sweep and `FacesObject` are ignored); discs and filled arcs drop their width.
5. **The spec's example `PluginGroundShape.Arc(...) { FacesObject = true }` is not valid C#**; it is `PluginGroundShape.Arc(...) with { FacesObject = true }`.
6. **Budget:** the shape batch has its own 2 MiB budget (an eighth of the frame's upload ring), beside the line renderer's 4 MiB.
7. **Order in the world phase:** shapes are drawn right after the plugin world lines, so they are tested against the lines' depth too.

## File Structure

| File | Responsibility |
|---|---|
| Modify `src/AcDream.Plugin.Abstractions/WorldMarkers.cs` | `PluginGroundShapeKind`, `PluginGroundShape`, `IPluginWorldMarkerLayer.SetShapes`, `IPluginWorldMarkers.MaximumShapes`. |
| Modify `src/AcDream.App/Plugins/WorldMarkerRules.cs` | `TryNormalizeShape`: drop and normalise rules. |
| Modify `src/AcDream.App/Plugins/PluginWorldMarkerStore.cs` | Per-layer shapes, shape cap, `CaptureShapes()` snapshot; clear and disposal cover shapes. |
| Modify `src/AcDream.App/Interaction/WorldSelectionQuery.cs` | `WorldLabelAnchor.HeadingDegrees`, set by `TryResolveWorldLabelAnchor`. |
| Create `src/AcDream.App/Rendering/GroundShapeTessellator.cs` | Pure: a normalised shape → triangles on the ground. |
| Create `src/AcDream.App/Rendering/Shaders/ground_shape.vert`, `ground_shape.frag` (+ compiled `spv/ground_shape.*.spv`, manifest) | Position + RGBA colour shader. |
| Create `src/AcDream.App/Rendering/GroundShapeBatch.cs` | Translucent triangle batch with its own pipeline and budget. |
| Create `src/AcDream.App/Rendering/PluginGroundShapeRenderer.cs` | Per-frame placement, range, nearest-first budget, depth nudge, colour. |
| Modify `src/AcDream.App/Rendering/Gpu/Vk/VulkanCompositionFramePhases.cs` | Draw ground shapes after world lines at both world-phase call sites. |
| Modify `src/AcDream.App/Composition/FrameRootComposition.cs` | `WorldMarkers` dependency; build the batch (owned by the frame-root bindings) and renderer. |
| Modify `src/AcDream.App/Rendering/GameWindow.cs` | Pass the UI registry's marker store to the frame root. |
| Modify `docs/plugin-api.md` | Ground shapes in the "World markers" section. |
| Modify `samples/AcDream.Plugins.WorldMarkersDemo/WorldMarkersDemoPlugin.cs` | Ring, facing wedge, disc and band for the live gate. |
| Tests | `tests/AcDream.Plugin.Tests/PluginWorldMarkersContractTests.cs` (added tests), `tests/AcDream.App.Tests/Plugins/PluginWorldMarkerStoreShapeTests.cs`, `tests/AcDream.App.Tests/Interaction/WorldSelectionQueryTests.cs` (added tests), `tests/AcDream.App.Tests/Rendering/GroundShapeTessellatorTests.cs`, `tests/AcDream.App.Tests/Rendering/GroundShapeBatchTests.cs`, `tests/AcDream.App.Tests/Rendering/RhiVertexLayoutStrideTests.cs` (added test + layout), `tests/AcDream.App.Tests/Rendering/PluginGroundShapeRendererTests.cs`. |

---

### Task 1: Ground shapes in the contract and the store

**Goal:** A plugin can build rings, discs and arcs and set them on a layer; the store drops bad shapes, normalises the rest, caps them per plugin apart from icons, and hands the renderer one ordered snapshot.

**Files:**
- Modify: `src/AcDream.Plugin.Abstractions/WorldMarkers.cs`
- Modify: `src/AcDream.App/Plugins/WorldMarkerRules.cs`
- Modify (full replacement): `src/AcDream.App/Plugins/PluginWorldMarkerStore.cs`
- Test: `tests/AcDream.Plugin.Tests/PluginWorldMarkersContractTests.cs` (add tests)
- Test: `tests/AcDream.App.Tests/Plugins/PluginWorldMarkerStoreShapeTests.cs` (create)

**Acceptance Criteria:**
- [ ] `PluginGroundShape.Ring/Disc/Arc` set kind, radius, width, start, sweep, filled and colour as the contract tests state; `Arc` defaults to width 0.15; `default(PluginGroundShape).Kind` is `None`; `IPluginWorldMarkers.MaximumShapes` is 256.
- [ ] `SetShapes` drops every invalid shape listed in `InvalidShapesAreDroppedAndTheRestShown` and keeps the boundary values in `TheLimitsThemselvesAreAccepted`.
- [ ] Rings and discs come out of the store as full turns from north; discs and filled arcs have width 0.
- [ ] The shape cap is per plugin across layers, refuses the whole set, is independent of the icon cap, and a list with an absurd `Count` is refused without being read.
- [ ] `CaptureShapes()` orders by plugin id, then layer creation, then given order, and returns the same array until something changes; layer disposal, surface disposal and `Clear()` empty shapes as they do icons.
- [ ] All existing `PluginWorldMarkerStoreTests` and `PluginWorldMarkersContractTests` still pass; `dotnet build src/AcDream.Plugin.Abstractions` has 0 warnings.

**Verify:** `dotnet test tests/AcDream.App.Tests/AcDream.App.Tests.csproj --filter "FullyQualifiedName~PluginWorldMarkerStore"` and `dotnet test tests/AcDream.Plugin.Tests/AcDream.Plugin.Tests.csproj --filter "FullyQualifiedName~PluginWorldMarkersContractTests"` → all pass.

**Steps:**

- [ ] **Step 0: Create the worktree**

```bash
cd /Users/davidsmith/code/OpenAC/OpenAC
git worktree add -b world-markers/shapes .worktrees/world-markers-shapes world-markers/icons
cd .worktrees/world-markers-shapes
export DOTNET_ROOT=$HOME/.dotnet PATH=$HOME/.dotnet:$PATH
```

- [ ] **Step 1: Write the failing contract tests** — add to `tests/AcDream.Plugin.Tests/PluginWorldMarkersContractTests.cs`, inside the class, after the existing tests:

```csharp
    private static readonly PluginColor Paint = new(10, 20, 30, 40);

    [Fact]
    public void ARingIsAFullBandOfTheGivenWidth()
    {
        PluginGroundShape ring = PluginGroundShape.Ring(PluginMarkerAnchor.Object(1u), radius: 2f, width: 0.2f, Paint);

        Assert.Equal(PluginGroundShapeKind.Ring, ring.Kind);
        Assert.Equal(PluginMarkerAnchor.Object(1u), ring.Anchor);
        Assert.Equal(2f, ring.Radius);
        Assert.Equal(0.2f, ring.Width);
        Assert.Equal(360f, ring.SweepDegrees);
        Assert.False(ring.Filled);
        Assert.Equal(Paint, ring.Color);
    }

    [Fact]
    public void ADiscIsAFilledFullCircle()
    {
        PluginGroundShape disc = PluginGroundShape.Disc(PluginMarkerAnchor.At(Spot), radius: 6f, Paint);

        Assert.Equal(PluginGroundShapeKind.Disc, disc.Kind);
        Assert.Equal(6f, disc.Radius);
        Assert.Equal(0f, disc.Width);
        Assert.Equal(360f, disc.SweepDegrees);
        Assert.True(disc.Filled);
    }

    [Fact]
    public void AnArcIsAThinBandWhenNoWidthIsGiven()
    {
        PluginGroundShape arc = PluginGroundShape.Arc(
            PluginMarkerAnchor.Object(1u), radius: 5f, startDegrees: -45f, sweepDegrees: 90f, filled: false, Paint);

        Assert.Equal(PluginGroundShapeKind.Arc, arc.Kind);
        Assert.Equal(PluginGroundShape.DefaultWidth, arc.Width);
        Assert.Equal(0.15f, PluginGroundShape.DefaultWidth);
        Assert.Equal(-45f, arc.StartDegrees);
        Assert.Equal(90f, arc.SweepDegrees);
        Assert.False(arc.Filled);
        Assert.False(arc.FacesObject);
    }

    [Fact]
    public void AWedgeCanBeMadeToFaceItsObject()
    {
        PluginGroundShape wedge = PluginGroundShape.Arc(
            PluginMarkerAnchor.Object(1u), 5f, -30f, 60f, filled: true, Paint) with { FacesObject = true };

        Assert.True(wedge.Filled);
        Assert.True(wedge.FacesObject);
    }

    [Fact]
    public void TheDefaultShapeIsOfNoKind()
    {
        Assert.Equal(PluginGroundShapeKind.None, default(PluginGroundShape).Kind);
    }

    [Fact]
    public void APluginMayHaveTwoHundredAndFiftySixShapesSet()
    {
        Assert.Equal(256, IPluginWorldMarkers.MaximumShapes);
    }
```

(`Spot` is the `PluginNavigationPosition` field the file already declares.)

- [ ] **Step 2: Write the failing store tests** — create `tests/AcDream.App.Tests/Plugins/PluginWorldMarkerStoreShapeTests.cs`:

```csharp
using AcDream.App.Plugins;
using AcDream.Core.Plugins;
using AcDream.Plugin.Abstractions;

namespace AcDream.App.Tests.Plugins;

/// <summary>
/// What the store takes from a plugin's ground shapes and what it hands the
/// renderer: the drop and normalise rules, the cap kept apart from icons, the
/// order the renderer sees, and how shapes go away with layers, surfaces and
/// the end of a stay in the world.
/// </summary>
public sealed class PluginWorldMarkerStoreShapeTests
{
    private static readonly PluginColor Red = new(220, 60, 60, 120);
    private static readonly PluginMarkerAnchor One = PluginMarkerAnchor.Object(1u);

    private static PluginGroundShape RingUnder(uint objectId) =>
        PluginGroundShape.Ring(PluginMarkerAnchor.Object(objectId), 2f, 0.2f, Red);

    private static PluginWorldIcon IconOver(uint objectId) =>
        new(PluginMarkerAnchor.Object(objectId), new PluginImage(1, 32, 32));

    private static IPluginWorldMarkerLayer Layer(PluginWorldMarkerStore store, string owner = "a.plugin") =>
        store.For(owner).CreateLayer()!;

    [Fact]
    public void TheSetIsCopiedSoTheListMayBeReused()
    {
        var store = new PluginWorldMarkerStore();
        var list = new List<PluginGroundShape> { RingUnder(1u) };

        Assert.True(Layer(store).SetShapes(list));
        list.Add(RingUnder(2u));

        Assert.Single(store.CaptureShapes());
    }

    [Fact]
    public void InvalidShapesAreDroppedAndTheRestShown()
    {
        var store = new PluginWorldMarkerStore();
        var badPosition = new PluginNavigationPosition(1u, double.NaN, 0, 0, 0f, true);

        Assert.True(Layer(store).SetShapes(
        [
            RingUnder(1u) with { Anchor = default },
            RingUnder(0u),
            PluginGroundShape.Disc(PluginMarkerAnchor.At(badPosition), 2f, Red),
            RingUnder(1u) with { Kind = PluginGroundShapeKind.None },
            RingUnder(1u) with { Kind = (PluginGroundShapeKind)99 },
            PluginGroundShape.Disc(One, 0.05f, Red),
            PluginGroundShape.Disc(One, 100.5f, Red),
            PluginGroundShape.Disc(One, float.NaN, Red),
            PluginGroundShape.Ring(One, 2f, 0f, Red),
            PluginGroundShape.Ring(One, 2f, -0.1f, Red),
            PluginGroundShape.Ring(One, 2f, 2.5f, Red),
            PluginGroundShape.Ring(One, 2f, float.PositiveInfinity, Red),
            PluginGroundShape.Arc(One, 2f, 0f, 0f, filled: true, Red),
            PluginGroundShape.Arc(One, 2f, 0f, -90f, filled: true, Red),
            PluginGroundShape.Arc(One, 2f, 0f, 361f, filled: true, Red),
            PluginGroundShape.Arc(One, 2f, float.NaN, 90f, filled: true, Red),
            PluginGroundShape.Arc(One, 2f, 0f, float.NaN, filled: true, Red),
            PluginGroundShape.Arc(One, 2f, 0f, 90f, filled: false, Red, width: 3f),
            RingUnder(7u),
        ]));

        PluginGroundShape only = Assert.Single(store.CaptureShapes());
        Assert.Equal(7u, only.Anchor.ObjectId);
    }

    [Fact]
    public void TheLimitsThemselvesAreAccepted()
    {
        var store = new PluginWorldMarkerStore();

        Assert.True(Layer(store).SetShapes(
        [
            PluginGroundShape.Disc(One, PluginGroundShape.MinimumRadius, Red),
            PluginGroundShape.Disc(One, PluginGroundShape.MaximumRadius, Red),
            PluginGroundShape.Ring(One, 2f, 2f, Red),
            PluginGroundShape.Arc(One, 2f, 0f, 360f, filled: true, Red),
        ]));

        Assert.Equal(4, store.CaptureShapes().Count);
    }

    [Fact]
    public void RingsAndDiscsBecomeFullTurnsFromNorthAndFilledShapesLoseTheirWidth()
    {
        var store = new PluginWorldMarkerStore();

        Layer(store).SetShapes(
        [
            RingUnder(1u) with { StartDegrees = 30f, SweepDegrees = 10f, Filled = true, FacesObject = true },
            PluginGroundShape.Disc(One, 3f, Red) with { Width = 1f, StartDegrees = 30f, SweepDegrees = 10f, Filled = false },
            PluginGroundShape.Arc(One, 5f, 30f, 90f, filled: true, Red, width: 1f),
            PluginGroundShape.Arc(One, 5f, 30f, 90f, filled: false, Red, width: 1f),
        ]);

        IReadOnlyList<PluginGroundShape> shapes = store.CaptureShapes();
        Assert.Equal((0f, 360f, false, false, 0.2f),
            (shapes[0].StartDegrees, shapes[0].SweepDegrees, shapes[0].Filled, shapes[0].FacesObject, shapes[0].Width));
        Assert.Equal((0f, 360f, true, 0f),
            (shapes[1].StartDegrees, shapes[1].SweepDegrees, shapes[1].Filled, shapes[1].Width));
        Assert.Equal((30f, 90f, true, 0f),
            (shapes[2].StartDegrees, shapes[2].SweepDegrees, shapes[2].Filled, shapes[2].Width));
        Assert.Equal((30f, 90f, false, 1f),
            (shapes[3].StartDegrees, shapes[3].SweepDegrees, shapes[3].Filled, shapes[3].Width));
    }

    [Fact]
    public void AListClaimingAbsurdlyManyShapesIsRefusedWithoutBeingCopied()
    {
        var store = new PluginWorldMarkerStore();
        IPluginWorldMarkerLayer layer = Layer(store);
        Assert.True(layer.SetShapes([RingUnder(1u)]));

        // Refused on its Count alone: the fake throws if anything reads it.
        Assert.False(layer.SetShapes(new HugeList()));

        Assert.Single(store.CaptureShapes());
    }

    private sealed class HugeList : IReadOnlyList<PluginGroundShape>
    {
        public int Count => int.MaxValue;
        public PluginGroundShape this[int index] => throw new InvalidOperationException();
        public IEnumerator<PluginGroundShape> GetEnumerator() => throw new InvalidOperationException();
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => throw new InvalidOperationException();
    }

    [Fact]
    public void TheCapCountsEveryLayerOfThePluginAndRefusesTheWholeSet()
    {
        var store = new PluginWorldMarkerStore();
        IPluginWorldMarkerLayer first = Layer(store);
        IPluginWorldMarkerLayer second = Layer(store);
        Assert.True(first.SetShapes(Enumerable.Repeat(RingUnder(1u), 200).ToArray()));
        Assert.True(second.SetShapes([RingUnder(2u)]));

        Assert.False(second.SetShapes(Enumerable.Repeat(RingUnder(3u), 57).ToArray()));
        Assert.Equal(201, store.CaptureShapes().Count);
        Assert.Equal(2u, store.CaptureShapes()[200].Anchor.ObjectId);

        Assert.True(second.SetShapes(Enumerable.Repeat(RingUnder(3u), 56).ToArray()));
        Assert.Equal(IPluginWorldMarkers.MaximumShapes, store.CaptureShapes().Count);
    }

    [Fact]
    public void IconsAndShapesAreCappedApart()
    {
        var store = new PluginWorldMarkerStore();
        IPluginWorldMarkerLayer layer = Layer(store);

        Assert.True(layer.SetIcons(Enumerable.Repeat(IconOver(1u), IPluginWorldMarkers.MaximumIcons).ToArray()));
        Assert.True(layer.SetShapes(Enumerable.Repeat(RingUnder(1u), IPluginWorldMarkers.MaximumShapes).ToArray()));
    }

    [Fact]
    public void SettingShapesLeavesTheLayersIconsAloneAndTheOtherWayRound()
    {
        var store = new PluginWorldMarkerStore();
        IPluginWorldMarkerLayer layer = Layer(store);
        layer.SetIcons([IconOver(1u)]);
        layer.SetShapes([RingUnder(2u)]);

        layer.SetShapes([RingUnder(3u)]);
        Assert.Single(store.CaptureIcons());

        layer.SetIcons([]);
        Assert.Equal(3u, Assert.Single(store.CaptureShapes()).Anchor.ObjectId);
    }

    [Fact]
    public void TheRendererSeesShapesByPluginThenLayerThenTheOrderGiven()
    {
        var store = new PluginWorldMarkerStore();
        IPluginWorldMarkerLayer bFirst = Layer(store, "b.plugin");
        IPluginWorldMarkerLayer aFirst = Layer(store, "a.plugin");
        IPluginWorldMarkerLayer aSecond = Layer(store, "a.plugin");
        bFirst.SetShapes([RingUnder(10u)]);
        aSecond.SetShapes([RingUnder(30u), RingUnder(31u)]);
        aFirst.SetShapes([RingUnder(20u)]);

        Assert.Equal(new[] { 20u, 30u, 31u, 10u }, store.CaptureShapes().Select(s => s.Anchor.ObjectId));
    }

    [Fact]
    public void TheShapeSnapshotIsReusedUntilSomethingChanges()
    {
        var store = new PluginWorldMarkerStore();
        IPluginWorldMarkerLayer layer = Layer(store);
        layer.SetShapes([RingUnder(1u)]);

        IReadOnlyList<PluginGroundShape> first = store.CaptureShapes();
        Assert.Same(first, store.CaptureShapes());

        layer.SetShapes([RingUnder(2u)]);
        Assert.NotSame(first, store.CaptureShapes());
    }

    [Fact]
    public void DisposingALayerTakesItsShapesAndRefusesFurtherUse()
    {
        var store = new PluginWorldMarkerStore();
        IPluginWorldMarkerLayer layer = Layer(store);
        layer.SetShapes([RingUnder(1u)]);

        layer.Dispose();

        Assert.Empty(store.CaptureShapes());
        Assert.Throws<ObjectDisposedException>(() => layer.SetShapes([RingUnder(2u)]));
    }

    [Fact]
    public void DisposingAPluginsSurfaceTakesItsShapes()
    {
        var store = new PluginWorldMarkerStore();
        IPluginWorldMarkers markers = store.For("a.plugin");
        markers.CreateLayer()!.SetShapes([RingUnder(1u)]);

        ((IDisposable)markers).Dispose();

        Assert.Empty(store.CaptureShapes());
    }

    [Fact]
    public void ClearingEmptiesShapesButTheLayersStayUsable()
    {
        var store = new PluginWorldMarkerStore();
        IPluginWorldMarkerLayer layer = Layer(store);
        layer.SetShapes([RingUnder(1u)]);

        store.Clear();

        Assert.Empty(store.CaptureShapes());
        Assert.True(layer.SetShapes([RingUnder(2u)]));
        Assert.Single(store.CaptureShapes());
    }

    [Fact]
    public void LeavingTheWorldClearsShapes()
    {
        var store = new PluginWorldMarkerStore();
        var events = new WorldEvents();
        Layer(store).SetShapes([RingUnder(1u)]);
        using IDisposable subscription = store.ClearOn(events);

        events.FireLogoff();

        Assert.Empty(store.CaptureShapes());
    }
}
```

- [ ] **Step 3: Run the tests to verify they fail**

Run: `dotnet build tests/AcDream.App.Tests/AcDream.App.Tests.csproj 2>&1 | grep -E "error" | head`
Expected: compile errors naming `PluginGroundShape`, `SetShapes`, `CaptureShapes`, `MaximumShapes`.

- [ ] **Step 4: Add the contract** — in `src/AcDream.Plugin.Abstractions/WorldMarkers.cs`:

(a) Insert after the closing `}` of `PluginWorldIcon` (before the `IPluginWorldMarkerLayer` doc comment):

```csharp
/// <summary>The form a ground shape takes.</summary>
public enum PluginGroundShapeKind
{
    /// <summary>Nothing: the default. A shape of no kind is dropped.</summary>
    None = 0,

    /// <summary>
    /// A band all the way round the anchor, from
    /// <see cref="PluginGroundShape.Radius"/> less
    /// <see cref="PluginGroundShape.Width"/> out to the radius.
    /// </summary>
    Ring = 1,

    /// <summary>A circle around the anchor, filled from its centre.</summary>
    Disc = 2,

    /// <summary>
    /// Part of a circle, over <see cref="PluginGroundShape.SweepDegrees"/>
    /// from <see cref="PluginGroundShape.StartDegrees"/>: a wedge filled from
    /// the centre, or a band like a ring's.
    /// </summary>
    Arc = 3,
}

/// <summary>
/// A shape lying on the ground around an anchor: a ring under a target, a
/// disc over an area, a wedge in front of a creature. Shapes are drawn in the
/// world, so walls and hills hide them, and outdoors they follow the slope of
/// the land. Made with <see cref="Ring"/>, <see cref="Disc"/> or
/// <see cref="Arc"/>; the default value is of no kind and is dropped.
/// </summary>
public readonly record struct PluginGroundShape
{
    /// <summary>The smallest radius a shape may have, in metres.</summary>
    public const float MinimumRadius = 0.1f;

    /// <summary>The largest radius a shape may have, in metres.</summary>
    public const float MaximumRadius = 100f;

    /// <summary>The width of an arc's band when none is given, in metres.</summary>
    public const float DefaultWidth = 0.15f;

    /// <summary>
    /// Where the shape is: around an object's feet, following it, or around a
    /// fixed position.
    /// </summary>
    public PluginMarkerAnchor Anchor { get; init; }

    /// <summary>What form the shape takes.</summary>
    public PluginGroundShapeKind Kind { get; init; }

    /// <summary>
    /// The shape's outer radius in metres, from <see cref="MinimumRadius"/>
    /// to <see cref="MaximumRadius"/>; a shape outside that is dropped.
    /// </summary>
    public float Radius { get; init; }

    /// <summary>
    /// How wide a ring's or unfilled arc's band is, in metres, inward from
    /// <see cref="Radius"/>. More than zero and at most the radius, or the
    /// shape is dropped. Not used by discs and filled arcs.
    /// </summary>
    public float Width { get; init; }

    /// <summary>
    /// Where an arc starts, as a compass bearing in degrees: 0 is north, 90
    /// is east. With <see cref="FacesObject"/>, measured from the way the
    /// object faces instead. Ignored by rings and discs.
    /// </summary>
    public float StartDegrees { get; init; }

    /// <summary>
    /// How far round an arc goes, clockwise from its start, in degrees: more
    /// than 0 and at most 360, or the arc is dropped. Ignored by rings and
    /// discs.
    /// </summary>
    public float SweepDegrees { get; init; }

    /// <summary>
    /// True for an arc filled from the centre (a wedge); false for a band
    /// like a ring's. Ignored by rings and discs.
    /// </summary>
    public bool Filled { get; init; }

    /// <summary>
    /// True for an arc over an object to turn with it: its start is measured
    /// from the way the object faces. Ignored for rings, discs and arcs at a
    /// fixed position.
    /// </summary>
    public bool FacesObject { get; init; }

    /// <summary>The shape's colour; its alpha is the opacity, 255 solid.</summary>
    public PluginColor Color { get; init; }

    /// <summary>A band all the way round the anchor.</summary>
    /// <param name="anchor">Where the ring is.</param>
    /// <param name="radius">The ring's outer radius, in metres.</param>
    /// <param name="width">How wide the band is, inward from the radius, in metres.</param>
    /// <param name="color">The ring's colour; its alpha is the opacity.</param>
    public static PluginGroundShape Ring(PluginMarkerAnchor anchor, float radius, float width, PluginColor color) => new()
    {
        Anchor = anchor,
        Kind = PluginGroundShapeKind.Ring,
        Radius = radius,
        Width = width,
        SweepDegrees = 360f,
        Color = color,
    };

    /// <summary>A filled circle around the anchor.</summary>
    /// <param name="anchor">Where the disc is.</param>
    /// <param name="radius">The disc's radius, in metres.</param>
    /// <param name="color">The disc's colour; its alpha is the opacity.</param>
    public static PluginGroundShape Disc(PluginMarkerAnchor anchor, float radius, PluginColor color) => new()
    {
        Anchor = anchor,
        Kind = PluginGroundShapeKind.Disc,
        Radius = radius,
        SweepDegrees = 360f,
        Filled = true,
        Color = color,
    };

    /// <summary>
    /// Part of a circle: a wedge filled from the anchor, or a band. Set
    /// <see cref="FacesObject"/> with a <c>with</c> expression for an arc that
    /// turns with its object.
    /// </summary>
    /// <param name="anchor">Where the arc is centred.</param>
    /// <param name="radius">The arc's outer radius, in metres.</param>
    /// <param name="startDegrees">Where the arc starts, as a compass bearing: 0 is north, 90 east.</param>
    /// <param name="sweepDegrees">How far round it goes, clockwise, in degrees.</param>
    /// <param name="filled">True for a wedge filled from the centre; false for a band.</param>
    /// <param name="color">The arc's colour; its alpha is the opacity.</param>
    /// <param name="width">How wide the band is when not filled, in metres.</param>
    public static PluginGroundShape Arc(
        PluginMarkerAnchor anchor,
        float radius,
        float startDegrees,
        float sweepDegrees,
        bool filled,
        PluginColor color,
        float width = DefaultWidth) => new()
    {
        Anchor = anchor,
        Kind = PluginGroundShapeKind.Arc,
        Radius = radius,
        Width = width,
        StartDegrees = startDegrees,
        SweepDegrees = sweepDegrees,
        Filled = filled,
        Color = color,
    };
}
```

(b) In `IPluginWorldMarkerLayer`, after `SetIcons`:

```csharp

    /// <summary>
    /// Replaces this layer's ground shapes with <paramref name="shapes"/>.
    /// The list is copied and may be reused. Shapes pinned to nothing, of no
    /// kind, or with a radius, width, start or sweep out of range (see
    /// <see cref="PluginGroundShape"/>) are dropped from the set. An empty
    /// list clears the layer's shapes; its icons are left alone.
    /// </summary>
    /// <param name="shapes">The shapes to show from now on.</param>
    /// <returns>
    /// True when the set was taken. False when the entries given, added to
    /// the shapes on this plugin's other layers, come to more than
    /// <see cref="IPluginWorldMarkers.MaximumShapes"/>; the layer then keeps
    /// what it had.
    /// </returns>
    bool SetShapes(IReadOnlyList<PluginGroundShape> shapes);
```

(c) In `IPluginWorldMarkers`: change the summary's first sentence to `Markers a plugin draws in the world -- icons over objects or at fixed positions, and shapes on the ground -- in layers it owns.` (keep the rest), and after `MaximumIcons` add:

```csharp

    /// <summary>
    /// The most ground shapes one plugin may have set at once, across all its
    /// layers. Counted apart from its icons.
    /// </summary>
    const int MaximumShapes = 256;
```

- [ ] **Step 5: Add the shape rules** — in `src/AcDream.App/Plugins/WorldMarkerRules.cs`, after `TryNormalizeIcon`:

```csharp

    /// <summary>
    /// The shape as the renderer will draw it, or false when it is dropped.
    /// A ring or disc becomes a full turn from north (its start, sweep and
    /// facing are ignored); a disc or filled arc has no width.
    /// </summary>
    internal static bool TryNormalizeShape(PluginGroundShape shape, out PluginGroundShape normalized)
    {
        normalized = default;
        if (!IsValidAnchor(shape.Anchor)
            || !float.IsFinite(shape.Radius)
            || shape.Radius < PluginGroundShape.MinimumRadius
            || shape.Radius > PluginGroundShape.MaximumRadius)
        {
            return false;
        }

        switch (shape.Kind)
        {
            case PluginGroundShapeKind.Ring:
                if (!IsValidBand(shape.Width, shape.Radius))
                    return false;
                normalized = shape with { StartDegrees = 0f, SweepDegrees = 360f, Filled = false, FacesObject = false };
                return true;
            case PluginGroundShapeKind.Disc:
                normalized = shape with { Width = 0f, StartDegrees = 0f, SweepDegrees = 360f, Filled = true, FacesObject = false };
                return true;
            case PluginGroundShapeKind.Arc:
                if (!float.IsFinite(shape.StartDegrees)
                    || !float.IsFinite(shape.SweepDegrees)
                    || shape.SweepDegrees <= 0f
                    || shape.SweepDegrees > 360f)
                {
                    return false;
                }
                if (shape.Filled)
                {
                    normalized = shape with { Width = 0f };
                    return true;
                }
                if (!IsValidBand(shape.Width, shape.Radius))
                    return false;
                normalized = shape;
                return true;
            default:
                return false;
        }
    }

    // A band runs inward from the radius: it must have some width and cannot
    // run past the centre.
    private static bool IsValidBand(float width, float radius) =>
        float.IsFinite(width) && width > 0f && width <= radius;
```

- [ ] **Step 6: Keep shapes in the store** — replace the whole of `src/AcDream.App/Plugins/PluginWorldMarkerStore.cs` with:

```csharp
using AcDream.Plugin.Abstractions;

namespace AcDream.App.Plugins;

/// <summary>One icon as the overlay draws it, with the plugin it belongs to.</summary>
/// <param name="OwnerId">The plugin's id; its images are the only ones the icon's image is looked up in.</param>
/// <param name="Icon">The icon, already checked and with its size in range.</param>
internal readonly record struct WorldIconEntry(string OwnerId, PluginWorldIcon Icon);

/// <summary>
/// Every plugin's world markers. Each plugin gets one surface, the same until
/// it is disposed, and makes layers on it; the overlay reads one merged,
/// ordered snapshot of icons and the world pass one of ground shapes.
/// Everything is guarded by one lock: plugins set markers on the tick thread,
/// the shapes are read on the render thread, and a logoff can clear them from
/// whichever thread ends the session.
/// </summary>
public sealed class PluginWorldMarkerStore
{
    private readonly object _gate = new();
    private readonly SortedDictionary<string, Markers> _owners = new(StringComparer.Ordinal);
    private WorldIconEntry[]? _merged = [];
    private PluginGroundShape[]? _mergedShapes = [];

    /// <summary>
    /// One plugin's surface, the same object on every call until it is
    /// disposed; disposing it takes down every layer the plugin made.
    /// </summary>
    internal IPluginWorldMarkers For(string ownerId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ownerId);
        lock (_gate)
        {
            if (_owners.TryGetValue(ownerId, out Markers? existing))
                return existing;
            var markers = new Markers(this, ownerId);
            _owners.Add(ownerId, markers);
            return markers;
        }
    }

    /// <summary>
    /// Every icon of every plugin, by plugin id, then layer in the order the
    /// layers were made, then the order given. The same array until something
    /// changes, so a caller reading it every frame allocates nothing; the
    /// caller never writes to it.
    /// </summary>
    internal IReadOnlyList<WorldIconEntry> CaptureIcons()
    {
        lock (_gate)
        {
            if (_merged is { } merged)
                return merged;
            var icons = new List<WorldIconEntry>();
            foreach (Markers owner in _owners.Values)
            {
                foreach (Layer layer in owner.Layers)
                {
                    foreach (PluginWorldIcon icon in layer.Icons)
                        icons.Add(new WorldIconEntry(owner.OwnerId, icon));
                }
            }
            return _merged = icons.ToArray();
        }
    }

    /// <summary>
    /// Every ground shape of every plugin, already checked and normalised, in
    /// the same order as <see cref="CaptureIcons"/>. The same array until
    /// something changes; the caller never writes to it.
    /// </summary>
    internal IReadOnlyList<PluginGroundShape> CaptureShapes()
    {
        lock (_gate)
        {
            if (_mergedShapes is { } merged)
                return merged;
            var shapes = new List<PluginGroundShape>();
            foreach (Markers owner in _owners.Values)
            {
                foreach (Layer layer in owner.Layers)
                    shapes.AddRange(layer.Shapes);
            }
            return _mergedShapes = shapes.ToArray();
        }
    }

    /// <summary>
    /// Empties every layer of every plugin. The layers stay, so a plugin sets
    /// its markers again on its next stay in the world without asking for a
    /// new layer.
    /// </summary>
    public void Clear()
    {
        lock (_gate)
        {
            foreach (Markers owner in _owners.Values)
            {
                foreach (Layer layer in owner.Layers)
                {
                    layer.Icons = [];
                    layer.Shapes = [];
                }
            }
            Changed();
        }
    }

    /// <summary>
    /// Clears the store whenever <paramref name="events"/> raises
    /// <see cref="IEvents.Logoff"/> -- once per stay, on every way out of the
    /// world -- until the returned subscription is disposed. Markers over
    /// objects mean nothing in the next session.
    /// </summary>
    public IDisposable ClearOn(IEvents events)
    {
        ArgumentNullException.ThrowIfNull(events);
        Action clear = Clear;
        events.Logoff += clear;
        return new Subscription(events, clear);
    }

    // The caller holds the gate. Both snapshots are rebuilt on next read.
    private void Changed()
    {
        _merged = null;
        _mergedShapes = null;
    }

    private sealed class Subscription(IEvents events, Action clear) : IDisposable
    {
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
                events.Logoff -= clear;
        }
    }

    private sealed class Markers(PluginWorldMarkerStore store, string ownerId)
        : IPluginWorldMarkers, IDisposable
    {
        private bool _disposed;

        internal string OwnerId => ownerId;

        internal List<Layer> Layers { get; } = [];

        public IPluginWorldMarkerLayer? CreateLayer()
        {
            lock (store._gate)
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                var layer = new Layer(store, this);
                Layers.Add(layer);
                return layer;
            }
        }

        internal int IconCountExcept(Layer except)
        {
            int count = 0;
            foreach (Layer layer in Layers)
            {
                if (!ReferenceEquals(layer, except))
                    count += layer.Icons.Length;
            }
            return count;
        }

        internal int ShapeCountExcept(Layer except)
        {
            int count = 0;
            foreach (Layer layer in Layers)
            {
                if (!ReferenceEquals(layer, except))
                    count += layer.Shapes.Length;
            }
            return count;
        }

        public void Dispose()
        {
            lock (store._gate)
            {
                if (_disposed)
                    return;
                _disposed = true;
                foreach (Layer layer in Layers)
                    layer.Detach();
                Layers.Clear();
                if (store._owners.TryGetValue(ownerId, out Markers? current)
                    && ReferenceEquals(current, this))
                {
                    store._owners.Remove(ownerId);
                }
                store.Changed();
            }
        }
    }

    private sealed class Layer(PluginWorldMarkerStore store, Markers owner) : IPluginWorldMarkerLayer
    {
        private bool _disposed;

        internal PluginWorldIcon[] Icons { get; set; } = [];

        internal PluginGroundShape[] Shapes { get; set; } = [];

        public bool SetIcons(IReadOnlyList<PluginWorldIcon> icons)
        {
            ArgumentNullException.ThrowIfNull(icons);
            if (!TryCopy(icons, IPluginWorldMarkers.MaximumIcons, out PluginWorldIcon[] given))
                return false;
            lock (store._gate)
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                if (given.Length + owner.IconCountExcept(this) > IPluginWorldMarkers.MaximumIcons)
                    return false;
                var accepted = new List<PluginWorldIcon>(given.Length);
                for (int i = 0; i < given.Length; i++)
                {
                    if (WorldMarkerRules.TryNormalizeIcon(given[i], out PluginWorldIcon icon))
                        accepted.Add(icon);
                }
                Icons = accepted.ToArray();
                store.Changed();
                return true;
            }
        }

        public bool SetShapes(IReadOnlyList<PluginGroundShape> shapes)
        {
            ArgumentNullException.ThrowIfNull(shapes);
            if (!TryCopy(shapes, IPluginWorldMarkers.MaximumShapes, out PluginGroundShape[] given))
                return false;
            lock (store._gate)
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                if (given.Length + owner.ShapeCountExcept(this) > IPluginWorldMarkers.MaximumShapes)
                    return false;
                var accepted = new List<PluginGroundShape>(given.Length);
                for (int i = 0; i < given.Length; i++)
                {
                    if (WorldMarkerRules.TryNormalizeShape(given[i], out PluginGroundShape shape))
                        accepted.Add(shape);
                }
                Shapes = accepted.ToArray();
                store.Changed();
                return true;
            }
        }

        // The plugin's list is read once, outside the lock, so a list that
        // throws or crawls cannot do so while other callers wait on it.
        // Count is read once: a set over the cap on its own can never be
        // accepted, so it is refused before anything is allocated.
        private static bool TryCopy<T>(IReadOnlyList<T> entries, int maximum, out T[] copy)
        {
            int count = entries.Count;
            if (count > maximum)
            {
                copy = [];
                return false;
            }
            copy = new T[count];
            for (int i = 0; i < copy.Length; i++)
                copy[i] = entries[i];
            return true;
        }

        /// <summary>Marks the layer gone and empties it; the caller removes it from its owner.</summary>
        internal void Detach()
        {
            _disposed = true;
            Icons = [];
            Shapes = [];
        }

        public void Dispose()
        {
            lock (store._gate)
            {
                if (_disposed)
                    return;
                Detach();
                owner.Layers.Remove(this);
                store.Changed();
            }
        }
    }
}
```

- [ ] **Step 7: Run the tests to verify they pass**

Run: `dotnet build src/AcDream.Plugin.Abstractions/AcDream.Plugin.Abstractions.csproj 2>&1 | grep -E "Warn|Error" | tail -2` → `0 Warning(s)`, `0 Error(s)`.
Run: `dotnet test tests/AcDream.Plugin.Tests/AcDream.Plugin.Tests.csproj --filter "FullyQualifiedName~PluginWorldMarkersContractTests"` → all pass.
Run: `dotnet test tests/AcDream.App.Tests/AcDream.App.Tests.csproj --filter "FullyQualifiedName~PluginWorldMarkerStore"` → all pass (the PR 1 store tests and the new shape tests).

- [ ] **Step 8: Commit**

```bash
git checkout -- '*.lock.json'
git add src/AcDream.Plugin.Abstractions/WorldMarkers.cs src/AcDream.App/Plugins/WorldMarkerRules.cs \
  src/AcDream.App/Plugins/PluginWorldMarkerStore.cs tests/AcDream.Plugin.Tests/PluginWorldMarkersContractTests.cs \
  tests/AcDream.App.Tests/Plugins/PluginWorldMarkerStoreShapeTests.cs
git commit -m "plugins: ground shapes on world marker layers

Rings, discs and arcs a plugin sets with SetShapes. The store drops bad
shapes, normalises rings and discs to full turns, caps shapes per plugin
apart from icons, and hands the renderer one ordered snapshot.

Claude-Session: https://claude.ai/code/session_01EtugJHhFMqGXY2V6Lz2ZFB"
```

```json:metadata
{"files": ["src/AcDream.Plugin.Abstractions/WorldMarkers.cs", "src/AcDream.App/Plugins/WorldMarkerRules.cs", "src/AcDream.App/Plugins/PluginWorldMarkerStore.cs", "tests/AcDream.Plugin.Tests/PluginWorldMarkersContractTests.cs", "tests/AcDream.App.Tests/Plugins/PluginWorldMarkerStoreShapeTests.cs"], "verifyCommand": "dotnet test tests/AcDream.App.Tests/AcDream.App.Tests.csproj --filter \"FullyQualifiedName~PluginWorldMarkerStore\" && dotnet test tests/AcDream.Plugin.Tests/AcDream.Plugin.Tests.csproj --filter \"FullyQualifiedName~PluginWorldMarkersContractTests\"", "acceptanceCriteria": ["Ring/Disc/Arc factories and defaults as the contract tests state; MaximumShapes 256; default kind None", "invalid shapes dropped, boundary values kept", "rings/discs normalised to full turns; filled shapes width 0", "shape cap per plugin across layers, whole-set refusal, apart from icons, absurd Count refused unread", "CaptureShapes ordering and snapshot reuse; disposal and Clear empty shapes", "existing PR 1 store and contract tests still pass; Abstractions builds with 0 warnings"], "modelTier": "standard"}
```

---

### Task 2: The way an object faces, on its label anchor

**Goal:** `WorldLabelAnchor` carries the object's compass heading, so a wedge can turn with the creature it is under.

**Files:**
- Modify: `src/AcDream.App/Interaction/WorldSelectionQuery.cs` (`WorldLabelAnchor` at ~line 110; `TryResolveWorldLabelAnchor` at ~line 588)
- Test: `tests/AcDream.App.Tests/Interaction/WorldSelectionQueryTests.cs` (add tests after `LabelAnchorIsRefusedForAnObjectTheClientDoesNotHold`)

**Acceptance Criteria:**
- [ ] An object rotated a quarter turn clockwise (seen from above) resolves with `HeadingDegrees` 90; an unrotated object with 0.
- [ ] A wielded object takes its heading from its published child pose (a quarter turn anticlockwise → 270).
- [ ] Every return path of `TryResolveWorldLabelAnchor` (cylinder, sphere, bounds, fallback) sets the heading; the existing label anchor tests still pass.

**Verify:** `dotnet test tests/AcDream.App.Tests/AcDream.App.Tests.csproj --filter "FullyQualifiedName~WorldSelectionQueryTests"` → all pass.

**Steps:**

- [ ] **Step 1: Write the failing tests** — add to `WorldSelectionQueryTests`, after `LabelAnchorIsRefusedForAnObjectTheClientDoesNotHold`:

```csharp
    [Fact]
    public void LabelAnchorCarriesTheWayTheObjectFaces()
    {
        var h = new Harness();
        // A quarter turn clockwise seen from above: facing east.
        h.Add(Target, Vector3.Zero, ItemType.Creature,
            rotation: Quaternion.CreateFromAxisAngle(Vector3.UnitZ, -MathF.PI / 2f));

        Assert.True(h.Query.TryResolveWorldLabelAnchor(Target, out WorldLabelAnchor anchor));
        Assert.Equal(90f, anchor.HeadingDegrees, 3);
    }

    [Fact]
    public void LabelAnchorOfAnUnturnedObjectFacesNorthOnEveryHeightRung()
    {
        var h = new Harness { Cylinder = (0f, 0f), Sphere = null, ModelHeight = null };
        h.Add(Target, Vector3.Zero, ItemType.Creature);

        Assert.True(h.Query.TryResolveWorldLabelAnchor(Target, out WorldLabelAnchor anchor));
        Assert.Equal(WorldLabelAnchorSource.Fallback, anchor.Source);
        Assert.Equal(0f, anchor.HeadingDegrees, 3);
    }

    [Fact]
    public void AWieldedObjectFacesTheWayItsPublishedPoseDoes()
    {
        var h = new Harness();
        WorldEntity wielder = h.Add(Wielder, new Vector3(0f, 0f, -10f), ItemType.Creature);
        h.AddAttached(
            RemoteWeapon,
            wielder.Position,
            ItemType.MeleeWeapon,
            wielderId: Wielder,
            // A quarter turn anticlockwise seen from above: facing west.
            childRoot: Matrix4x4.CreateRotationZ(MathF.PI / 2f) * Matrix4x4.CreateTranslation(1f, 2f, -9f));

        Assert.True(h.Query.TryResolveWorldLabelAnchor(RemoteWeapon, out WorldLabelAnchor anchor));
        Assert.Equal(new Vector3(1f, 2f, -9f), anchor.BasePosition);
        Assert.Equal(270f, anchor.HeadingDegrees, 3);
    }
```

(`Wielder` and `RemoteWeapon` are constants the test class already declares; the attached setup mirrors `PickResolvesTheEquippedChildRatherThanItsWielder`.)

- [ ] **Step 2: Run to verify they fail**

Run: `dotnet build tests/AcDream.App.Tests/AcDream.App.Tests.csproj 2>&1 | grep -E " error " | head -3`
Expected: `'WorldLabelAnchor' does not contain a definition for 'HeadingDegrees'`.

- [ ] **Step 3: Add the heading** — in `src/AcDream.App/Interaction/WorldSelectionQuery.cs`:

(a) Replace the `WorldLabelAnchor` declaration with:

```csharp
public readonly record struct WorldLabelAnchor(
    Vector3 BasePosition,
    float Height,
    WorldLabelAnchorSource Source)
{
    /// <summary>
    /// The way the object faces, as a compass bearing in degrees: 0 is north,
    /// 90 east, clockwise. Ground shapes that face with the object turn by it.
    /// </summary>
    public float HeadingDegrees { get; init; }
}
```

(b) In `TryResolveWorldLabelAnchor`, replace

```csharp
        Vector3 basePosition = entity.Position;
        if (attached)
        {
            if (_childRootPose(entity.Id) is not { } published)
                return false;
            basePosition = published.Translation;
        }
```

with

```csharp
        Vector3 basePosition = entity.Position;
        Quaternion rotation = entity.Rotation;
        if (attached)
        {
            if (_childRootPose(entity.Id) is not { } published)
                return false;
            basePosition = published.Translation;
            // A wielded object turns with the pose it was published at, not
            // with the rotation its entity last had of its own.
            if (Matrix4x4.Decompose(published, out _, out Quaternion childRotation, out _))
                rotation = childRotation;
        }
        float heading = AcDream.Core.Physics.Motion.MoveToMath.GetHeading(rotation);
```

(c) Add `{ HeadingDegrees = heading }` to each of the four `new WorldLabelAnchor(...)` constructions in the method, e.g.:

```csharp
            anchor = new WorldLabelAnchor(
                basePosition, bodyHeight, WorldLabelAnchorSource.PhysicsCylinder)
            {
                HeadingDegrees = heading,
            };
```

(the same for `SelectionSphere`, `ModelBounds` and `Fallback`).

- [ ] **Step 4: Run to verify they pass**

Run: `dotnet test tests/AcDream.App.Tests/AcDream.App.Tests.csproj --filter "FullyQualifiedName~WorldSelectionQueryTests"` → all pass.
Run: `dotnet test tests/AcDream.App.Tests/AcDream.App.Tests.csproj --filter "FullyQualifiedName~WorldIconOverlay|FullyQualifiedName~WorldLabelOverlay|FullyQualifiedName~InteractionUiRuntimeSourcesTests"` → all pass (other users of `WorldLabelAnchor`).

- [ ] **Step 5: Commit**

```bash
git checkout -- '*.lock.json'
git add src/AcDream.App/Interaction/WorldSelectionQuery.cs tests/AcDream.App.Tests/Interaction/WorldSelectionQueryTests.cs
git commit -m "interaction: an object's heading on its label anchor

Ground wedges that face with a creature need the way it faces. The
anchor lookup already has the entity, or a wielded object's published
pose; it now passes on the compass heading of either.

Claude-Session: https://claude.ai/code/session_01EtugJHhFMqGXY2V6Lz2ZFB"
```

```json:metadata
{"files": ["src/AcDream.App/Interaction/WorldSelectionQuery.cs", "tests/AcDream.App.Tests/Interaction/WorldSelectionQueryTests.cs"], "verifyCommand": "dotnet test tests/AcDream.App.Tests/AcDream.App.Tests.csproj --filter \"FullyQualifiedName~WorldSelectionQueryTests\"", "acceptanceCriteria": ["east-facing object resolves HeadingDegrees 90; unturned 0", "wielded object heading from published child pose (270 for a quarter turn anticlockwise)", "all four height rungs set the heading; existing label anchor tests pass"], "modelTier": "mechanical"}
```

---

### Task 3: Cut a shape into triangles on the ground

**Goal:** A pure tessellator turns a normalised shape, its centre and its start bearing into triangles that lie just above the land (or flat at the anchor's height), with a vertex count known in advance.

**Files:**
- Create: `src/AcDream.App/Rendering/GroundShapeTessellator.cs`
- Test: `tests/AcDream.App.Tests/Rendering/GroundShapeTessellatorTests.cs`

**Acceptance Criteria:**
- [ ] `SegmentsFor` gives 16, 63, 128, 16 and 1 for (0.5 m, 360°), (5 m, 360°), (20 m, 360°), (5 m, 90°), (0.5 m, 10°).
- [ ] `FillRingsFor` gives 1, 1, 2, 8 for radii 1, 4, 6, 100 m.
- [ ] `Tessellate` emits exactly `VertexCount(shape)` vertices for every shape tested.
- [ ] A ring's points lie between its inner and outer radius; a disc includes its centre and its ring boundaries; an arc from 90° sweeping 90° lies east and south of the centre and reaches due east and due south; a start bearing given overrides the shape's own; an unfilled arc is a band over its sweep only.
- [ ] Following the land, every point is `LiftMeters` above the terrain under it, or above the centre's height where the terrain is unknown; not following, every point is `LiftMeters` above the centre's height.

**Verify:** `dotnet test tests/AcDream.App.Tests/AcDream.App.Tests.csproj --filter "FullyQualifiedName~GroundShapeTessellatorTests"` → all pass.

**Steps:**

- [ ] **Step 1: Write the failing tests** — create `tests/AcDream.App.Tests/Rendering/GroundShapeTessellatorTests.cs`:

```csharp
using System.Numerics;
using AcDream.App.Plugins;
using AcDream.App.Rendering;
using AcDream.Plugin.Abstractions;

namespace AcDream.App.Tests.Rendering;

/// <summary>
/// How a ground shape is cut into triangles: how many segments and rings,
/// which way an arc runs, and how high each point sits over the land.
/// </summary>
public sealed class GroundShapeTessellatorTests
{
    private const float Tolerance = 1e-3f;
    private static readonly PluginColor Paint = new(255, 0, 0, 128);
    private static readonly PluginMarkerAnchor Anchor = PluginMarkerAnchor.Object(1u);
    private static readonly Func<float, float, float?> NoLand = static (_, _) => null;
    private static readonly Func<float, float, float?> Slope = static (x, _) => x * 0.5f;

    private static List<Vector3> Cut(
        PluginGroundShape shape,
        Vector3 centre = default,
        float? start = null,
        bool follow = false,
        Func<float, float, float?>? terrain = null)
    {
        Assert.True(WorldMarkerRules.TryNormalizeShape(shape, out PluginGroundShape normalized));
        var triangles = new List<Vector3>();
        GroundShapeTessellator.Tessellate(
            normalized, centre, start ?? normalized.StartDegrees, follow, terrain ?? NoLand, triangles);
        Assert.Equal(GroundShapeTessellator.VertexCount(normalized), triangles.Count);
        return triangles;
    }

    private static float Across(Vector3 point, Vector3 centre) =>
        Vector2.Distance(new Vector2(point.X, point.Y), new Vector2(centre.X, centre.Y));

    [Theory]
    [InlineData(0.5f, 360f, 16)]  // a small ring still gets the minimum
    [InlineData(5f, 360f, 63)]    // 2π·5 m / 0.5 m = 62.8
    [InlineData(20f, 360f, 128)]  // a large ring is capped
    [InlineData(5f, 90f, 16)]     // a quarter takes a quarter of 63, rounded up
    [InlineData(0.5f, 10f, 1)]    // a sliver is at least one segment
    public void EdgesAreCutIntoHalfMetreSegmentsWithinBounds(float radius, float sweep, int expected)
    {
        Assert.Equal(expected, GroundShapeTessellator.SegmentsFor(radius, sweep));
    }

    [Theory]
    [InlineData(1f, 1)]
    [InlineData(4f, 1)]
    [InlineData(6f, 2)]
    [InlineData(100f, 8)]
    public void FilledShapesAreCutIntoRingsAboutFourMetresDeep(float radius, int expected)
    {
        Assert.Equal(expected, GroundShapeTessellator.FillRingsFor(radius));
    }

    [Fact]
    public void ARingIsABandFromItsInnerToItsOuterRadius()
    {
        var centre = new Vector3(10f, 20f, 3f);

        List<Vector3> points = Cut(PluginGroundShape.Ring(Anchor, 2f, 0.5f, Paint), centre);

        Assert.All(points, p => Assert.InRange(Across(p, centre), 1.5f - Tolerance, 2f + Tolerance));
        Assert.Contains(points, p => MathF.Abs(Across(p, centre) - 1.5f) < Tolerance);
        Assert.Contains(points, p => MathF.Abs(Across(p, centre) - 2f) < Tolerance);
    }

    [Fact]
    public void ADiscIsFilledFromItsCentreThroughItsRings()
    {
        var centre = new Vector3(10f, 20f, 3f);

        List<Vector3> points = Cut(PluginGroundShape.Disc(Anchor, 6f, Paint), centre);

        Assert.Contains(points, p => Across(p, centre) < Tolerance);
        // Two rings: the boundary between them is half way out.
        Assert.Contains(points, p => MathF.Abs(Across(p, centre) - 3f) < Tolerance);
        Assert.Contains(points, p => MathF.Abs(Across(p, centre) - 6f) < Tolerance);
        Assert.All(points, p => Assert.True(Across(p, centre) <= 6f + Tolerance));
    }

    [Fact]
    public void AnArcSweepsClockwiseFromItsStartBearing()
    {
        // From east (90°) a quarter turn clockwise ends at south.
        List<Vector3> points = Cut(PluginGroundShape.Arc(Anchor, 5f, 90f, 90f, filled: true, Paint));

        Assert.All(points, p =>
        {
            Assert.True(p.X >= -Tolerance);
            Assert.True(p.Y <= Tolerance);
        });
        Assert.Contains(points, p => MathF.Abs(p.X - 5f) < Tolerance && MathF.Abs(p.Y) < Tolerance);
        Assert.Contains(points, p => MathF.Abs(p.X) < Tolerance && MathF.Abs(p.Y + 5f) < Tolerance);
    }

    [Fact]
    public void TheStartGivenOverridesTheShapesOwnStart()
    {
        // A thin wedge pointing north, started from 85° instead (its object faces east): it points east.
        List<Vector3> points = Cut(PluginGroundShape.Arc(Anchor, 5f, -5f, 10f, filled: true, Paint), start: 85f);

        Assert.All(points, p => Assert.True(p.X >= -Tolerance));
        Assert.Contains(points, p => p.X > 4.9f);
    }

    [Fact]
    public void AnUnfilledArcIsABandOverItsSweepOnly()
    {
        // North round through east to south: the eastern half.
        List<Vector3> points = Cut(PluginGroundShape.Arc(Anchor, 5f, 0f, 180f, filled: false, Paint, width: 1f));

        Assert.All(points, p =>
        {
            Assert.InRange(Across(p, default), 4f - Tolerance, 5f + Tolerance);
            Assert.True(p.X >= -Tolerance);
        });
    }

    [Fact]
    public void OnTheLandEveryPointSitsJustAboveTheGroundUnderIt()
    {
        List<Vector3> points = Cut(
            PluginGroundShape.Disc(Anchor, 6f, Paint), new Vector3(0f, 0f, 100f), follow: true, terrain: Slope);

        Assert.All(points, p => Assert.Equal((p.X * 0.5f) + GroundShapeTessellator.LiftMeters, p.Z, 3));
    }

    [Fact]
    public void WhereTheLandIsUnknownAPointSitsAtTheCentresHeight()
    {
        List<Vector3> points = Cut(
            PluginGroundShape.Ring(Anchor, 2f, 0.2f, Paint), new Vector3(0f, 0f, 7f), follow: true, terrain: NoLand);

        Assert.All(points, p => Assert.Equal(7f + GroundShapeTessellator.LiftMeters, p.Z, 3));
    }

    [Fact]
    public void OffTheLandTheShapeIsFlatAtTheAnchorsHeight()
    {
        List<Vector3> points = Cut(
            PluginGroundShape.Disc(Anchor, 6f, Paint), new Vector3(0f, 0f, 100f), follow: false, terrain: Slope);

        Assert.All(points, p => Assert.Equal(100f + GroundShapeTessellator.LiftMeters, p.Z, 3));
    }
}
```

- [ ] **Step 2: Run to verify they fail**

Run: `dotnet build tests/AcDream.App.Tests/AcDream.App.Tests.csproj 2>&1 | grep -E " error " | head -3`
Expected: `The name 'GroundShapeTessellator' does not exist`.

- [ ] **Step 3: Write the tessellator** — create `src/AcDream.App/Rendering/GroundShapeTessellator.cs`:

```csharp
using System.Numerics;
using AcDream.Plugin.Abstractions;

namespace AcDream.App.Rendering;

/// <summary>
/// Cuts a plugin's ground shape into triangles lying on the ground. Pure:
/// the renderer hands it the shape's centre, the bearing its arc starts at
/// and whether (and how) to follow the land, and gets back three points per
/// triangle. Bearings are compass bearings: 0 is north (+Y), 90 east (+X),
/// increasing clockwise.
/// </summary>
internal static class GroundShapeTessellator
{
    /// <summary>About how long each piece of a shape's edge is, in metres.</summary>
    internal const float SegmentMeters = 0.5f;

    /// <summary>The fewest pieces a full turn is cut into.</summary>
    internal const int MinimumSegments = 16;

    /// <summary>The most pieces a full turn is cut into.</summary>
    internal const int MaximumSegments = 128;

    /// <summary>
    /// About how deep each ring of a filled shape is, in metres, so a large
    /// disc still has points on the land every few metres.
    /// </summary>
    internal const float FillRingMeters = 4f;

    /// <summary>The most rings a filled shape is cut into.</summary>
    internal const int MaximumFillRings = 8;

    /// <summary>How far above the land, or the anchor's height, a shape lies.</summary>
    internal const float LiftMeters = 0.05f;

    /// <summary>
    /// How many pieces an edge of <paramref name="radius"/> metres is cut into
    /// over <paramref name="sweepDegrees"/>: about every half metre, between
    /// <see cref="MinimumSegments"/> and <see cref="MaximumSegments"/> for a
    /// full turn, an arc taking its share and at least one.
    /// </summary>
    internal static int SegmentsFor(float radius, float sweepDegrees)
    {
        double full = Math.Clamp(
            Math.Ceiling(2d * Math.PI * radius / SegmentMeters), MinimumSegments, MaximumSegments);
        return (int)Math.Max(1d, Math.Ceiling(full * Math.Clamp(sweepDegrees, 0f, 360f) / 360d));
    }

    /// <summary>How many rings a filled shape of <paramref name="radius"/> metres is cut into.</summary>
    internal static int FillRingsFor(float radius) =>
        (int)Math.Clamp(Math.Ceiling(radius / FillRingMeters), 1d, MaximumFillRings);

    /// <summary>How many points <see cref="Tessellate"/> emits for this normalised shape.</summary>
    internal static int VertexCount(in PluginGroundShape shape)
    {
        int segments = SegmentsFor(shape.Radius, shape.SweepDegrees);
        return shape.Filled
            ? 3 * segments * ((2 * FillRingsFor(shape.Radius)) - 1)
            : 6 * segments;
    }

    /// <summary>
    /// Appends the triangles of <paramref name="shape"/> to
    /// <paramref name="triangles"/>, three points each. A band runs from
    /// <c>Radius - Width</c> to <c>Radius</c>; a filled shape is a fan round
    /// the centre and then bands out to the radius. Following the land, each
    /// point lies <see cref="LiftMeters"/> above <paramref name="terrain"/>'s
    /// height under it (or the centre's height where it has none); otherwise
    /// every point lies that far above the centre's height.
    /// </summary>
    /// <param name="shape">A shape the store has normalised.</param>
    /// <param name="centre">Where the shape is centred, in world metres.</param>
    /// <param name="startDegrees">The bearing the arc starts at, the object's heading already added.</param>
    /// <param name="followTerrain">Whether the shape follows the land.</param>
    /// <param name="terrain">The land's height at a world (x, y), or null where it is not known.</param>
    /// <param name="triangles">Receives three points per triangle.</param>
    internal static void Tessellate(
        in PluginGroundShape shape,
        Vector3 centre,
        float startDegrees,
        bool followTerrain,
        Func<float, float, float?> terrain,
        List<Vector3> triangles)
    {
        int segments = SegmentsFor(shape.Radius, shape.SweepDegrees);
        bool closed = shape.SweepDegrees >= 360f;
        Span<Vector2> directions = stackalloc Vector2[segments + 1];
        for (int i = 0; i <= segments; i++)
        {
            if (closed && i == segments)
            {
                // A full turn ends exactly where it began, so it closes without a seam.
                directions[i] = directions[0];
                break;
            }
            float radians = (startDegrees + (shape.SweepDegrees * i / segments)) * (MathF.PI / 180f);
            directions[i] = new Vector2(MathF.Sin(radians), MathF.Cos(radians));
        }

        Span<Vector3> inner = stackalloc Vector3[segments + 1];
        Span<Vector3> outer = stackalloc Vector3[segments + 1];
        if (!shape.Filled)
        {
            Row(shape.Radius - shape.Width, directions, centre, followTerrain, terrain, inner);
            Row(shape.Radius, directions, centre, followTerrain, terrain, outer);
            Band(inner, outer, triangles);
            return;
        }

        int rings = FillRingsFor(shape.Radius);
        Vector3 middle = Lift(centre.X, centre.Y, centre, followTerrain, terrain);
        Row(shape.Radius / rings, directions, centre, followTerrain, terrain, outer);
        for (int i = 0; i < segments; i++)
        {
            triangles.Add(middle);
            triangles.Add(outer[i]);
            triangles.Add(outer[i + 1]);
        }
        for (int ring = 2; ring <= rings; ring++)
        {
            outer.CopyTo(inner);
            Row(shape.Radius * ring / rings, directions, centre, followTerrain, terrain, outer);
            Band(inner, outer, triangles);
        }
    }

    private static void Row(
        float radius,
        ReadOnlySpan<Vector2> directions,
        Vector3 centre,
        bool followTerrain,
        Func<float, float, float?> terrain,
        Span<Vector3> row)
    {
        for (int i = 0; i < directions.Length; i++)
        {
            row[i] = Lift(
                centre.X + (directions[i].X * radius),
                centre.Y + (directions[i].Y * radius),
                centre,
                followTerrain,
                terrain);
        }
    }

    private static Vector3 Lift(float x, float y, Vector3 centre, bool followTerrain, Func<float, float, float?> terrain)
    {
        float ground = followTerrain && terrain(x, y) is { } z ? z : centre.Z;
        return new Vector3(x, y, ground + LiftMeters);
    }

    private static void Band(ReadOnlySpan<Vector3> inner, ReadOnlySpan<Vector3> outer, List<Vector3> triangles)
    {
        for (int i = 0; i + 1 < inner.Length; i++)
        {
            triangles.Add(inner[i]);
            triangles.Add(outer[i]);
            triangles.Add(outer[i + 1]);
            triangles.Add(inner[i]);
            triangles.Add(outer[i + 1]);
            triangles.Add(inner[i + 1]);
        }
    }
}
```

- [ ] **Step 4: Run to verify they pass**

Run: `dotnet test tests/AcDream.App.Tests/AcDream.App.Tests.csproj --filter "FullyQualifiedName~GroundShapeTessellatorTests"` → all pass.

- [ ] **Step 5: Commit**

```bash
git checkout -- '*.lock.json'
git add src/AcDream.App/Rendering/GroundShapeTessellator.cs tests/AcDream.App.Tests/Rendering/GroundShapeTessellatorTests.cs
git commit -m "rendering: cut ground shapes into triangles on the land

Edges in half-metre pieces (16 to 128 a turn), filled shapes in rings
about 4 m deep so a large disc still follows a hill, each point 5 cm over
the land outdoors or flat at the anchor's height elsewhere.

Claude-Session: https://claude.ai/code/session_01EtugJHhFMqGXY2V6Lz2ZFB"
```

```json:metadata
{"files": ["src/AcDream.App/Rendering/GroundShapeTessellator.cs", "tests/AcDream.App.Tests/Rendering/GroundShapeTessellatorTests.cs"], "verifyCommand": "dotnet test tests/AcDream.App.Tests/AcDream.App.Tests.csproj --filter \"FullyQualifiedName~GroundShapeTessellatorTests\"", "acceptanceCriteria": ["SegmentsFor gives 16, 63, 128, 16, 1 for the tested cases", "FillRingsFor gives 1, 1, 2, 8 for radii 1, 4, 6, 100", "Tessellate emits exactly VertexCount vertices", "ring band radii, disc centre and ring boundaries, arc clockwise from start, start override, unfilled arc band only over its sweep", "terrain-following, unknown-land and flat heights each LiftMeters above the right height"], "modelTier": "standard"}
```

---

### Task 4: A see-through triangle batch for the world pass

**Goal:** A `GroundShapeBatch` with its own `ground_shape` shader draws coloured, translucent triangles into the open world pass, tested against but never written to its depth, within its own vertex budget.

**Files:**
- Create: `src/AcDream.App/Rendering/Shaders/ground_shape.vert`
- Create: `src/AcDream.App/Rendering/Shaders/ground_shape.frag`
- Generated (commit): `src/AcDream.App/Rendering/Shaders/spv/ground_shape.vert.spv`, `spv/ground_shape.frag.spv`, `spv/shaders.manifest.json`
- Create: `src/AcDream.App/Rendering/GroundShapeBatch.cs`
- Test: `tests/AcDream.App.Tests/Rendering/GroundShapeBatchTests.cs`
- Modify: `tests/AcDream.App.Tests/Rendering/RhiVertexLayoutStrideTests.cs` (one test, one layout in `EveryRhiVertexLayout`)

**Acceptance Criteria:**
- [ ] The batch creates one pipeline named `ground-shape` with shader set `ground_shape`, triangle list, straight alpha, `TranslucentDefault` depth, no culling and the world pass's sample count.
- [ ] A flush binds that pipeline, turns depth writes off, uploads 7 floats per vertex (position then RGBA) and draws every vertex added.
- [ ] Triangles past `VertexBudget` are left out; nothing is drawn without a depth attachment or with nothing added.
- [ ] The stride test and `EveryRhiVertexLayout` include the ground shape layout; `VulkanShaderManifestTests` pass with the new shader compiled; no existing `.spv` changed.

**Verify:** `dotnet test tests/AcDream.App.Tests/AcDream.App.Tests.csproj --filter "FullyQualifiedName~GroundShapeBatchTests|FullyQualifiedName~RhiVertexLayoutStrideTests|FullyQualifiedName~VulkanShaderManifestTests"` → all pass.

**Steps:**

- [ ] **Step 1: Write the shader pair** — `src/AcDream.App/Rendering/Shaders/ground_shape.vert`:

```glsl
#version 430 core
layout(location = 0) in vec3 aPos;
layout(location = 1) in vec4 aColor;

uniform mat4 uViewProjection;

out vec4 vColor;

void main() {
    vColor = aColor;
    gl_Position = uViewProjection * vec4(aPos, 1.0);
}
```

`src/AcDream.App/Rendering/Shaders/ground_shape.frag`:

```glsl
#version 430 core
in vec4 vColor;
out vec4 FragColor;

void main() {
    FragColor = vColor;
}
```

- [ ] **Step 2: Compile it**

```bash
pwsh tools/compile-shaders.ps1 2>&1 | grep -E "ground_shape|compiled|error"
rm -f tools/ShaderCompiler/packages.osx-arm64.lock.json
git status --short src/AcDream.App/Rendering/Shaders tools
```

Expected: `[shaders] ground_shape: ok` and `24/24 pair(s) compiled`; `git status` shows only the two new `.vert`/`.frag`, the two new `spv/ground_shape.*.spv` and a modified `spv/shaders.manifest.json` — no other `.spv` modified. If any other `.spv` changed, stop and report it (the retail oracle hashes in `VulkanShaderManifestTests` must not move).

- [ ] **Step 3: Write the failing tests** — create `tests/AcDream.App.Tests/Rendering/GroundShapeBatchTests.cs`:

```csharp
using System.Numerics;
using System.Runtime.InteropServices;
using AcDream.App.Rendering;
using AcDream.App.Rendering.Gpu;
using AcDream.App.Tests.Rendering.Gpu;

namespace AcDream.App.Tests.Rendering;

/// <summary>
/// The batch plugins' ground shapes are drawn with: see-through triangles in
/// the world pass, hidden by the world but never hiding anything, and never
/// more than its budget.
/// </summary>
public sealed class GroundShapeBatchTests
{
    private static readonly Vector4 HalfOrange = new(1f, 0.5f, 0f, 0.25f);

    [Fact]
    public void ShapesAreSeeThroughTrianglesTestedAgainstTheWorldsDepth()
    {
        var device = new RecordingGpuDevice();
        IGpuFrame frame = device.BeginFrame();

        using var batch = new GroundShapeBatch(device, new FixedFrame(frame), new FourSampleWorldPass());

        GpuPipelineDescription pipeline = Assert.Single(device.CreatedPipelines).Description;
        Assert.Equal("ground-shape", pipeline.Name);
        Assert.Equal("ground_shape", pipeline.Shaders.Name);
        Assert.Equal(GpuPrimitiveTopology.TriangleList, pipeline.Topology);
        Assert.Equal(GpuBlendMode.StraightAlpha, pipeline.Blend);
        Assert.Equal(GpuDepthState.TranslucentDefault, pipeline.Depth);
        Assert.Equal(GpuCullMode.None, pipeline.Cull);
        Assert.Equal(4, pipeline.SampleCount);
        Assert.Same(GroundShapeBatch.VertexLayout, pipeline.VertexLayout);
    }

    [Fact]
    public void AFlushDrawsEveryTriangleWithDepthWritesOff()
    {
        var device = new RecordingGpuDevice(ringCapacityBytes: 1024 * 1024);
        IGpuFrame frame = device.BeginFrame();
        using var batch = new GroundShapeBatch(device, new FixedFrame(frame), new FourSampleWorldPass());

        batch.Begin();
        batch.AddTriangle(new Vector3(1f, 2f, 3f), new Vector3(4f, 5f, 6f), new Vector3(7f, 8f, 9f), HalfOrange);
        using (IGpuPassEncoder encoder = frame.BeginPass(WorldPass(withDepth: true)))
            batch.Flush(encoder, Matrix4x4.Identity, 1280, 720);

        Assert.Contains(new GpuRecordedPipelineBind("ground-shape"), device.Calls);
        Assert.Contains(new GpuRecordedDepthWrite(false), device.Calls);
        Assert.Equal(3u, Assert.Single(device.Calls.OfType<GpuRecordedDraw>()).VertexCount);
        GpuRecordedRingAllocation upload = Assert.Single(device.Calls.OfType<GpuRecordedRingAllocation>());
        float[] floats = MemoryMarshal.Cast<byte, float>(
            device.RingBytes.Slice((int)upload.OffsetBytes, upload.ByteCount)).ToArray();
        Assert.Equal(3 * GroundShapeBatch.FloatsPerVertex, floats.Length);
        Assert.Equal(new[] { 1f, 2f, 3f, 1f, 0.5f, 0f, 0.25f }, floats[..7]);
    }

    [Fact]
    public void TrianglesPastTheBudgetAreLeftOut()
    {
        var device = new RecordingGpuDevice(ringCapacityBytes: 64 * 1024 * 1024);
        IGpuFrame frame = device.BeginFrame();
        using var batch = new GroundShapeBatch(device, new FixedFrame(frame), new FourSampleWorldPass());

        batch.Begin();
        for (int i = 0; i < (GroundShapeBatch.VertexBudget / 3) + 100; i++)
            batch.AddTriangle(Vector3.Zero, Vector3.UnitX, Vector3.UnitY, HalfOrange);
        Assert.InRange(batch.RemainingVertexBudget, 0, 2);
        using (IGpuPassEncoder encoder = frame.BeginPass(WorldPass(withDepth: true)))
            batch.Flush(encoder, Matrix4x4.Identity, 1280, 720);

        uint drawn = Assert.Single(device.Calls.OfType<GpuRecordedDraw>()).VertexCount;
        Assert.InRange((int)drawn, 1, GroundShapeBatch.VertexBudget);
    }

    [Fact]
    public void NothingIsDrawnWithoutDepthOrWithNothingAdded()
    {
        var device = new RecordingGpuDevice(ringCapacityBytes: 1024 * 1024);
        IGpuFrame frame = device.BeginFrame();
        using var batch = new GroundShapeBatch(device, new FixedFrame(frame), new FourSampleWorldPass());

        batch.Begin();
        using (IGpuPassEncoder encoder = frame.BeginPass(WorldPass(withDepth: true)))
            batch.Flush(encoder, Matrix4x4.Identity, 1280, 720);
        batch.AddTriangle(Vector3.Zero, Vector3.UnitX, Vector3.UnitY, HalfOrange);
        using (IGpuPassEncoder encoder = frame.BeginPass(WorldPass(withDepth: false)))
            batch.Flush(encoder, Matrix4x4.Identity, 1280, 720);

        Assert.Empty(device.Calls.OfType<GpuRecordedDraw>());
    }

    private static GpuPassDescription WorldPass(bool withDepth) => new()
    {
        Name = "vk-world",
        Color = new GpuColorAttachment(
            Target: null,
            Load: GpuLoadOp.Clear,
            Store: GpuStoreOp.Store,
            ClearColor: default),
        Depth = withDepth
            ? new GpuDepthAttachment(
                Load: GpuLoadOp.Clear,
                Store: GpuStoreOp.DontCare,
                ClearDepth: 1f,
                ClearStencil: 0)
            : null,
        SampleCount = 4,
    };

    private sealed class FixedFrame(IGpuFrame frame) : ICurrentGpuFrameSource
    {
        public IGpuFrame? CurrentFrame => frame;
    }

    private sealed class FourSampleWorldPass : IWorldPassScope
    {
        public int SampleCount => 4;

        public IGpuPassEncoder? CurrentEncoder => null;

        public int AttachmentWidth => 1280;

        public int AttachmentHeight => 720;

        public WorldFrameSections Sections { get; } = new();

        public IGpuPassEncoder RequireEncoder() =>
            throw new InvalidOperationException("No world pass is open.");

        public void ClearInteriorDepth()
        {
        }

        public IDisposable Publish(IGpuPassEncoder encoder) =>
            throw new NotSupportedException();
    }
}
```

In `tests/AcDream.App.Tests/Rendering/RhiVertexLayoutStrideTests.cs`, after `DebugLineStrideMatchesTheFloatsTheProducerAppends` add:

```csharp
    [Fact]
    public void GroundShapeStrideMatchesTheFloatsTheProducerAppends()
    {
        Assert.Equal(
            (uint)(GroundShapeBatch.FloatsPerVertex * sizeof(float)),
            GroundShapeBatch.VertexLayout.StrideBytes);
    }
```

and in `EveryRhiVertexLayout()`, after the `"debug line"` entry:

```csharp
        yield return ("ground shape", GroundShapeBatch.VertexLayout);
```

- [ ] **Step 4: Run to verify they fail**

Run: `dotnet build tests/AcDream.App.Tests/AcDream.App.Tests.csproj 2>&1 | grep -E " error " | head -3`
Expected: `The type or namespace name 'GroundShapeBatch' could not be found`.

- [ ] **Step 5: Write the batch** — create `src/AcDream.App/Rendering/GroundShapeBatch.cs`:

```csharp
using System.Numerics;
using System.Runtime.InteropServices;
using AcDream.App.Rendering.Gpu;

namespace AcDream.App.Rendering;

/// <summary>
/// See-through coloured triangles drawn into the open world pass with its
/// depth: tested against the world, so walls and hills hide them, but never
/// written to it, so they never hide each other or what is drawn after them.
/// Plugins' ground shapes are drawn with it.
/// </summary>
internal sealed class GroundShapeBatch : IDisposable
{
    internal const int FloatsPerVertex = 7;
    private const int VertexStrideBytes = FloatsPerVertex * sizeof(float);

    /// <summary>
    /// The most vertices one batch holds: 2 MiB of them, an eighth of the
    /// frame's upload ring, which the rest of the frame shares and which
    /// cannot grow mid-frame. Past the budget, further triangles are left out.
    /// </summary>
    internal const int VertexBudget = 2 * 1024 * 1024 / VertexStrideBytes;

    internal static readonly GpuVertexLayout VertexLayout = GpuVertexLayout.Interleaved(
        strideBytes: VertexStrideBytes,
        [
            new GpuVertexAttribute(0, GpuVertexFormat.Float3, 0),
            new GpuVertexAttribute(1, GpuVertexFormat.Float4, 12),
        ]);

    private readonly ICurrentGpuFrameSource _frameSource;
    private readonly IGpuPipeline _pipeline;
    private readonly List<float> _buffer = new(4096);
    private int _vertexCount;

    internal GroundShapeBatch(IGpuDevice device, ICurrentGpuFrameSource frameSource, IWorldPassScope worldPass)
    {
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(worldPass);
        _frameSource = frameSource ?? throw new ArgumentNullException(nameof(frameSource));
        _pipeline = device.CreatePipeline(new GpuPipelineDescription
        {
            Name = "ground-shape",
            Shaders = new GpuShaderSet("ground_shape"),
            VertexLayout = VertexLayout,
            Topology = GpuPrimitiveTopology.TriangleList,
            Blend = GpuBlendMode.StraightAlpha,
            Depth = GpuDepthState.TranslucentDefault,
            Cull = GpuCullMode.None,
            AlphaToCoverage = false,
            ColorWrite = true,
            SampleCount = worldPass.SampleCount,
        });
    }

    /// <summary>How many more vertices this batch takes before it is full.</summary>
    internal int RemainingVertexBudget => VertexBudget - _vertexCount;

    internal void Begin()
    {
        _buffer.Clear();
        _vertexCount = 0;
    }

    /// <summary>Adds a triangle of one colour, or nothing once the budget is spent.</summary>
    internal void AddTriangle(Vector3 a, Vector3 b, Vector3 c, Vector4 color)
    {
        if (RemainingVertexBudget < 3)
            return;
        AddVertex(a, color);
        AddVertex(b, color);
        AddVertex(c, color);
    }

    private void AddVertex(Vector3 position, Vector4 color)
    {
        _buffer.Add(position.X); _buffer.Add(position.Y); _buffer.Add(position.Z);
        _buffer.Add(color.X); _buffer.Add(color.Y); _buffer.Add(color.Z); _buffer.Add(color.W);
        _vertexCount++;
    }

    /// <summary>
    /// Draws the triangles added since <see cref="Begin"/> into
    /// <paramref name="encoder"/>'s pass, which must have depth.
    /// </summary>
    internal void Flush(IGpuPassEncoder encoder, Matrix4x4 viewProjection, int width, int height)
    {
        if (_vertexCount == 0 || encoder.Pass.Depth is null)
            return;
        IGpuFrame frame = _frameSource.CurrentFrame
            ?? throw new InvalidOperationException("Ground shapes require an active frame.");
        encoder.BindPipeline(_pipeline);
        encoder.SetViewport(0, 0, width, height);
        encoder.SetScissor(0, 0, width, height);
        encoder.SetDepthWrite(false);
        encoder.SetStencil(GpuStencilState.Default);
        GpuPushConstants constants = GpuPushConstants.Default;
        constants.ViewProjection = viewProjection;
        encoder.SetPushConstants(constants);
        GpuRingAllocation allocation = frame.AllocateRing(_buffer.Count * sizeof(float), GpuRingUsage.Vertex);
        CollectionsMarshal.AsSpan(_buffer).CopyTo(allocation.AsSpan<float>());
        encoder.BindVertexBuffer(0, allocation.Buffer, allocation.OffsetBytes);
        encoder.Draw((uint)_vertexCount, 1, 0, 0);
    }

    public void Dispose() => _pipeline.Dispose();
}
```

- [ ] **Step 6: Run to verify they pass**

Run: `dotnet test tests/AcDream.App.Tests/AcDream.App.Tests.csproj --filter "FullyQualifiedName~GroundShapeBatchTests|FullyQualifiedName~RhiVertexLayoutStrideTests|FullyQualifiedName~VulkanShaderManifestTests"` → all pass.

- [ ] **Step 7: Commit**

```bash
git checkout -- '*.lock.json'
rm -f tools/ShaderCompiler/packages.osx-arm64.lock.json
git add src/AcDream.App/Rendering/Shaders/ground_shape.vert src/AcDream.App/Rendering/Shaders/ground_shape.frag \
  src/AcDream.App/Rendering/Shaders/spv/ground_shape.vert.spv src/AcDream.App/Rendering/Shaders/spv/ground_shape.frag.spv \
  src/AcDream.App/Rendering/Shaders/spv/shaders.manifest.json src/AcDream.App/Rendering/GroundShapeBatch.cs \
  tests/AcDream.App.Tests/Rendering/GroundShapeBatchTests.cs tests/AcDream.App.Tests/Rendering/RhiVertexLayoutStrideTests.cs
git commit -m "rendering: a see-through triangle batch for ground shapes

Its own position-plus-RGBA shader and pipeline: straight alpha, depth
tested but not written, so shapes are hidden by the world and never hide
each other. Its own 2 MiB budget beside the line renderer's.

Claude-Session: https://claude.ai/code/session_01EtugJHhFMqGXY2V6Lz2ZFB"
```

```json:metadata
{"files": ["src/AcDream.App/Rendering/Shaders/ground_shape.vert", "src/AcDream.App/Rendering/Shaders/ground_shape.frag", "src/AcDream.App/Rendering/Shaders/spv/ground_shape.vert.spv", "src/AcDream.App/Rendering/Shaders/spv/ground_shape.frag.spv", "src/AcDream.App/Rendering/Shaders/spv/shaders.manifest.json", "src/AcDream.App/Rendering/GroundShapeBatch.cs", "tests/AcDream.App.Tests/Rendering/GroundShapeBatchTests.cs", "tests/AcDream.App.Tests/Rendering/RhiVertexLayoutStrideTests.cs"], "verifyCommand": "dotnet test tests/AcDream.App.Tests/AcDream.App.Tests.csproj --filter \"FullyQualifiedName~GroundShapeBatchTests|FullyQualifiedName~RhiVertexLayoutStrideTests|FullyQualifiedName~VulkanShaderManifestTests\"", "acceptanceCriteria": ["one pipeline ground-shape / ground_shape, triangle list, straight alpha, TranslucentDefault, no cull, world sample count", "flush binds it, depth write off, 7 floats per vertex, draws all added", "budget overflow left out; nothing drawn without depth or with nothing added", "stride test and layout list include ground shape; shader manifest tests pass; no existing spv changed"], "modelTier": "standard"}
```

---

### Task 5: Place and draw each plugin's shapes

**Goal:** A `PluginGroundShapeRenderer` places every shape (an object's feet with its heading, or a spot), decides whether it follows the land, skips shapes beyond 250 m, draws the nearest first when the budget runs out, and feeds the batch colour and nudged vertices.

**Files:**
- Create: `src/AcDream.App/Rendering/PluginGroundShapeRenderer.cs`
- Test: `tests/AcDream.App.Tests/Rendering/PluginGroundShapeRendererTests.cs`

**Acceptance Criteria:**
- [ ] Nothing is drawn before the world origin is known, and nothing for a shape over an object the client does not hold.
- [ ] A shape whose centre is more than 250 m from the camera is not drawn; one inside is.
- [ ] A ring over an object lies around the object's base; a wedge with `FacesObject` points the way the object faces, one without points by its own bearing.
- [ ] `TryPlace`: an object within 1 m of the land follows it, one further off (or with no land under it) is flat; a spot follows the land only when `IsOutdoor`.
- [ ] Colour and opacity reach the batch as `color / 255`; `TowardCamera` moves a point 0.2 % of the way to the camera.
- [ ] Under budget pressure the vertices drawn stay within `GroundShapeBatch.VertexBudget` and the shape nearest the camera is among those drawn.

**Verify:** `dotnet test tests/AcDream.App.Tests/AcDream.App.Tests.csproj --filter "FullyQualifiedName~PluginGroundShapeRendererTests"` → all pass.

**Steps:**

- [ ] **Step 1: Write the failing tests** — create `tests/AcDream.App.Tests/Rendering/PluginGroundShapeRendererTests.cs`:

```csharp
using System.Numerics;
using System.Runtime.InteropServices;
using AcDream.App.Interaction;
using AcDream.App.Plugins;
using AcDream.App.Rendering;
using AcDream.App.Rendering.Gpu;
using AcDream.App.Tests.Rendering.Gpu;
using AcDream.App.World;
using AcDream.Plugin.Abstractions;

namespace AcDream.App.Tests.Rendering;

/// <summary>
/// Where each plugin's ground shapes land in the world, which are drawn, and
/// what reaches the batch.
/// </summary>
public sealed class PluginGroundShapeRendererTests
{
    private const uint Creature = 0x8000_0001u;
    private static readonly PluginColor Orange = new(255, 128, 0, 64);
    private static readonly Func<float, float, float?> FlatLand = static (_, _) => 0f;
    private static readonly Func<float, float, float?> NoLand = static (_, _) => null;

    [Fact]
    public void NothingIsDrawnBeforeTheWorldsOriginIsKnown()
    {
        using var scene = new Scene(camera: Vector3.Zero, originKnown: false);

        scene.Draw(PluginGroundShape.Disc(At(new Vector3(5f, 0f, 0f)), 2f, Orange));

        Assert.Equal(0, scene.DrawnVertexCount);
    }

    [Fact]
    public void AShapeOverAnObjectTheClientDoesNotHoldIsNotDrawn()
    {
        using var scene = new Scene(camera: Vector3.Zero);

        scene.Draw(PluginGroundShape.Ring(PluginMarkerAnchor.Object(Creature), 2f, 0.2f, Orange));

        Assert.Equal(0, scene.DrawnVertexCount);
    }

    [Fact]
    public void AShapeWhoseCentreIsBeyondTheDrawRangeIsNotDrawn()
    {
        using var scene = new Scene(camera: Vector3.Zero);

        scene.Draw(
            PluginGroundShape.Disc(At(new Vector3(260f, 0f, 0f)), 2f, Orange),
            PluginGroundShape.Disc(At(new Vector3(240f, 0f, 0f)), 2f, Orange));

        Vector3[] points = scene.UploadedPositions();
        Assert.NotEmpty(points);
        Assert.All(points, p => Assert.True(p.X < 250f));
    }

    [Fact]
    public void AShapeOverAnObjectLiesAroundItsFeet()
    {
        using var scene = new Scene(camera: Vector3.Zero);
        scene.Objects[Creature] = new WorldLabelAnchor(new Vector3(10f, 20f, 0f), 2f, WorldLabelAnchorSource.PhysicsCylinder);

        scene.Draw(PluginGroundShape.Ring(PluginMarkerAnchor.Object(Creature), 2f, 0.2f, Orange));

        Vector3[] points = scene.UploadedPositions();
        Assert.NotEmpty(points);
        // The depth nudge moves points a few centimetres toward the camera.
        Assert.All(points, p => Assert.InRange(
            Vector2.Distance(new Vector2(p.X, p.Y), new Vector2(10f, 20f)), 1.8f - 0.1f, 2f + 0.1f));
    }

    [Fact]
    public void AWedgeThatFacesItsObjectTurnsWithIt()
    {
        var facingEast = new WorldLabelAnchor(Vector3.Zero, 2f, WorldLabelAnchorSource.PhysicsCylinder) { HeadingDegrees = 90f };
        PluginGroundShape wedge = PluginGroundShape.Arc(
            PluginMarkerAnchor.Object(Creature), 5f, -10f, 20f, filled: true, Orange);

        using var turning = new Scene(camera: new Vector3(0f, 0f, 50f));
        turning.Objects[Creature] = facingEast;
        turning.Draw(wedge with { FacesObject = true });
        using var fixedNorth = new Scene(camera: new Vector3(0f, 0f, 50f));
        fixedNorth.Objects[Creature] = facingEast;
        fixedNorth.Draw(wedge);

        Assert.Contains(turning.UploadedPositions(), p => p.X > 4.5f);
        Assert.All(turning.UploadedPositions(), p => Assert.True(p.X >= -0.01f));
        Assert.Contains(fixedNorth.UploadedPositions(), p => p.Y > 4.5f);
    }

    [Theory]
    [InlineData(0.3f, true)]   // standing on the land
    [InlineData(-0.9f, true)]  // a little sunk into it
    [InlineData(6f, false)]    // up on a roof or a bridge
    public void AnObjectFollowsTheLandOnlyWhenItStandsOnIt(float baseHeight, bool follows)
    {
        PluginGroundShape ring = PluginGroundShape.Ring(PluginMarkerAnchor.Object(Creature), 2f, 0.2f, Orange);
        var anchor = new WorldLabelAnchor(new Vector3(0f, 0f, baseHeight), 2f, WorldLabelAnchorSource.PhysicsCylinder);

        Assert.True(PluginGroundShapeRenderer.TryPlace(ring, _ => anchor, FlatLand, 127, 127, out GroundShapePlacement placement));

        Assert.Equal(follows, placement.FollowTerrain);
        Assert.Equal(new Vector3(0f, 0f, baseHeight), placement.Centre);
    }

    [Fact]
    public void AnObjectWithNoLandUnderItIsFlat()
    {
        PluginGroundShape ring = PluginGroundShape.Ring(PluginMarkerAnchor.Object(Creature), 2f, 0.2f, Orange);
        var anchor = new WorldLabelAnchor(Vector3.Zero, 2f, WorldLabelAnchorSource.PhysicsCylinder);

        Assert.True(PluginGroundShapeRenderer.TryPlace(ring, _ => anchor, NoLand, 127, 127, out GroundShapePlacement placement));

        Assert.False(placement.FollowTerrain);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ASpotFollowsTheLandOnlyOutdoors(bool outdoor)
    {
        PluginGroundShape disc = PluginGroundShape.Disc(
            PluginMarkerAnchor.At(At(new Vector3(5f, 6f, 7f)).Position with { IsOutdoor = outdoor }), 2f, Orange);

        Assert.True(PluginGroundShapeRenderer.TryPlace(disc, _ => null, FlatLand, 127, 127, out GroundShapePlacement placement));

        Assert.Equal(outdoor, placement.FollowTerrain);
        Assert.Equal(5f, placement.Centre.X, 3);
        Assert.Equal(6f, placement.Centre.Y, 3);
        Assert.Equal(7f, placement.Centre.Z, 3);
    }

    [Fact]
    public void TheShapesColourAndOpacityReachTheBatch()
    {
        using var scene = new Scene(camera: Vector3.Zero);

        scene.Draw(PluginGroundShape.Disc(At(new Vector3(5f, 0f, 0f)), 2f, Orange));

        float[] first = scene.UploadedVertices()[0];
        Assert.Equal(new[] { 1f, 128f / 255f, 0f, 64f / 255f }, first[3..7]);
    }

    [Fact]
    public void EveryPointIsPulledSlightlyTowardTheCamera()
    {
        Vector3 nudged = PluginGroundShapeRenderer.TowardCamera(new Vector3(100f, 0f, 0f), Vector3.Zero);

        Assert.Equal(99.8f, nudged.X, 3);
        Assert.Equal(0f, nudged.Y);
        Assert.Equal(0f, nudged.Z);
    }

    [Fact]
    public void WhenTheBudgetRunsOutTheShapesNearestTheCameraAreTheOnesDrawn()
    {
        using var scene = new Scene(camera: Vector3.Zero);
        var shapes = new List<PluginGroundShape>();
        for (int i = 0; i < 100; i++)
        {
            // Large discs on a ring 200 m out, sent first: far more than the budget.
            float angle = i * MathF.Tau / 100f;
            shapes.Add(PluginGroundShape.Disc(
                At(new Vector3(MathF.Cos(angle) * 200f, MathF.Sin(angle) * 200f, 0f)), 100f, Orange));
        }
        // The one small ring right beside the camera is sent last.
        shapes.Add(PluginGroundShape.Ring(At(new Vector3(5f, 0f, 0f)), 1f, 0.2f, Orange));

        scene.Draw([.. shapes]);

        Assert.InRange(scene.DrawnVertexCount, 1, GroundShapeBatch.VertexBudget);
        Assert.Contains(scene.UploadedPositions(), p => MathF.Abs(p.X - 5f) <= 1.2f && MathF.Abs(p.Y) <= 1.2f);
    }

    // With the world's origin at the centre landblock, a local position is
    // the map position times 240 plus 84 m on each ground axis.
    private static PluginMarkerAnchor At(Vector3 local) => PluginMarkerAnchor.At(new PluginNavigationPosition(
        CellId: 0u,
        EastWest: (local.X - 84d) / 240d,
        NorthSouth: (local.Y - 84d) / 240d,
        Elevation: local.Z / 240d,
        HeadingDegrees: 0f,
        IsOutdoor: true));

    private sealed class Scene : IDisposable
    {
        private readonly RecordingGpuDevice _device = new(ringCapacityBytes: 64 * 1024 * 1024);
        private readonly IGpuFrame _frame;
        private readonly GroundShapeBatch _batch;
        private readonly PluginWorldMarkerStore _store = new();
        private readonly PluginGroundShapeRenderer _renderer;

        public Scene(Vector3 camera, bool originKnown = true)
        {
            _frame = _device.BeginFrame();
            _batch = new GroundShapeBatch(_device, new FixedFrame(_frame), new FourSampleWorldPass());
            var origin = new LiveWorldOriginState();
            if (originKnown)
                origin.TryInitialize(127, 127);
            _renderer = new PluginGroundShapeRenderer(
                _store.CaptureShapes,
                _batch,
                new FixedCamera(camera),
                origin,
                id => Objects.TryGetValue(id, out WorldLabelAnchor anchor) ? anchor : null,
                FlatLand);
        }

        public Dictionary<uint, WorldLabelAnchor> Objects { get; } = [];

        public long DrawnVertexCount => _device.Calls.OfType<GpuRecordedDraw>().Sum(draw => (long)draw.VertexCount);

        public void Draw(params PluginGroundShape[] shapes)
        {
            Assert.True(_store.For("a.plugin").CreateLayer()!.SetShapes(shapes));
            using IGpuPassEncoder encoder = _frame.BeginPass(WorldPass());
            _renderer.Render(encoder, 1280, 720);
        }

        public float[][] UploadedVertices()
        {
            var result = new List<float[]>();
            foreach (GpuRecordedRingAllocation allocation in _device.Calls.OfType<GpuRecordedRingAllocation>())
            {
                ReadOnlySpan<float> floats = MemoryMarshal.Cast<byte, float>(
                    _device.RingBytes.Slice((int)allocation.OffsetBytes, allocation.ByteCount));
                for (int i = 0; i + GroundShapeBatch.FloatsPerVertex <= floats.Length; i += GroundShapeBatch.FloatsPerVertex)
                    result.Add(floats.Slice(i, GroundShapeBatch.FloatsPerVertex).ToArray());
            }
            return [.. result];
        }

        public Vector3[] UploadedPositions() =>
            [.. UploadedVertices().Select(v => new Vector3(v[0], v[1], v[2]))];

        public void Dispose() => _batch.Dispose();
    }

    private static GpuPassDescription WorldPass() => new()
    {
        Name = "vk-world",
        Color = new GpuColorAttachment(
            Target: null,
            Load: GpuLoadOp.Clear,
            Store: GpuStoreOp.Store,
            ClearColor: default),
        Depth = new GpuDepthAttachment(
            Load: GpuLoadOp.Clear,
            Store: GpuStoreOp.DontCare,
            ClearDepth: 1f,
            ClearStencil: 0),
        SampleCount = 4,
    };

    private sealed class FixedCamera(Vector3 position) : IWorldFrameCameraSource
    {
        public WorldCameraFrame Resolve() => new(
            Camera: null!,
            Projection: Matrix4x4.Identity,
            ViewProjection: Matrix4x4.Identity,
            Frustum: default,
            InverseView: Matrix4x4.Identity,
            Position: position);
    }

    private sealed class FixedFrame(IGpuFrame frame) : ICurrentGpuFrameSource
    {
        public IGpuFrame? CurrentFrame => frame;
    }

    private sealed class FourSampleWorldPass : IWorldPassScope
    {
        public int SampleCount => 4;

        public IGpuPassEncoder? CurrentEncoder => null;

        public int AttachmentWidth => 1280;

        public int AttachmentHeight => 720;

        public WorldFrameSections Sections { get; } = new();

        public IGpuPassEncoder RequireEncoder() =>
            throw new InvalidOperationException("No world pass is open.");

        public void ClearInteriorDepth()
        {
        }

        public IDisposable Publish(IGpuPassEncoder encoder) =>
            throw new NotSupportedException();
    }
}
```

- [ ] **Step 2: Run to verify they fail**

Run: `dotnet build tests/AcDream.App.Tests/AcDream.App.Tests.csproj 2>&1 | grep -E " error " | head -3`
Expected: `The type or namespace name 'PluginGroundShapeRenderer' could not be found`.

- [ ] **Step 3: Write the renderer** — create `src/AcDream.App/Rendering/PluginGroundShapeRenderer.cs`:

```csharp
using System.Numerics;
using System.Runtime.InteropServices;
using AcDream.App.Interaction;
using AcDream.App.Rendering.Gpu;
using AcDream.App.World;
using AcDream.Plugin.Abstractions;

namespace AcDream.App.Rendering;

/// <summary>
/// Draws the shapes plugins lay on the ground -- rings, discs and wedges --
/// as see-through triangles in the world pass, after the world, so walls
/// and hills hide them.
/// </summary>
/// <remarks>
/// Bounded here, not by the plugin: a shape whose centre is more than
/// <see cref="DrawRangeMeters"/> from the camera is skipped, and the
/// triangles of one frame never exceed the batch's budget. When the shapes in
/// range would, the nearest are drawn and the farthest are left out.
/// </remarks>
internal sealed class PluginGroundShapeRenderer(
    Func<IReadOnlyList<PluginGroundShape>> shapes,
    GroundShapeBatch batch,
    IWorldFrameCameraSource camera,
    LiveWorldOriginState origin,
    Func<uint, WorldLabelAnchor?> objects,
    Func<float, float, float?> terrain)
{
    // The same reach as plugins' world lines.
    internal const float DrawRangeMeters = PluginWorldLineRenderer.DrawRangeMeters;

    // An object whose base is this close to the land under it is standing on
    // the land, and its shapes follow it; anything further off (a floor, a
    // roof, a bridge, a dungeon) gets a flat shape at its base.
    internal const float OnTheLandMeters = 1f;

    // Each point moves this fraction of the way toward the camera. Along the
    // line of sight it stays at the same place on the screen; only its depth
    // comes forward, so the land it lies on doesn't flicker through it.
    internal const float DepthNudge = 0.002f;

    private readonly List<PlannedShape> _planned = [];
    private readonly List<Vector3> _triangles = [];

    public void Render(IGpuPassEncoder encoder, int width, int height)
    {
        if (!origin.IsKnown || width <= 0 || height <= 0)
            return;
        IReadOnlyList<PluginGroundShape> all = shapes();
        if (all.Count == 0)
            return;
        WorldCameraFrame frame = camera.Resolve();
        _planned.Clear();
        long vertices = 0;
        for (int i = 0; i < all.Count; i++)
        {
            PluginGroundShape shape = all[i];
            if (!TryPlace(shape, objects, terrain, origin.CenterX, origin.CenterY, out GroundShapePlacement placement))
                continue;
            float distanceSquared = Vector3.DistanceSquared(placement.Centre, frame.Position);
            if (!(distanceSquared <= DrawRangeMeters * DrawRangeMeters))
                continue;
            int count = GroundShapeTessellator.VertexCount(shape);
            _planned.Add(new PlannedShape(shape, placement, distanceSquared, count));
            vertices += count;
        }
        if (_planned.Count == 0)
            return;

        batch.Begin();
        // Only when the shapes do not all fit is the order worth a sort.
        if (vertices > batch.RemainingVertexBudget)
            _planned.Sort(static (a, b) => a.DistanceSquared.CompareTo(b.DistanceSquared));
        foreach (ref readonly PlannedShape planned in CollectionsMarshal.AsSpan(_planned))
        {
            if (planned.VertexCount > batch.RemainingVertexBudget)
                break;
            Draw(in planned, frame.Position);
        }
        _planned.Clear();
        batch.Flush(encoder, frame.ViewProjection, width, height);
    }

    /// <summary>
    /// Where a shape lies this frame: around an object's base, its start
    /// turned by the object's heading when it faces with the object, or
    /// around a fixed position. False when the anchor is not in the world.
    /// </summary>
    internal static bool TryPlace(
        in PluginGroundShape shape,
        Func<uint, WorldLabelAnchor?> objects,
        Func<float, float, float?> terrain,
        int centerX,
        int centerY,
        out GroundShapePlacement placement)
    {
        placement = default;
        switch (shape.Anchor.Kind)
        {
            case PluginMarkerAnchorKind.Object:
            {
                if (objects(shape.Anchor.ObjectId) is not { } anchor)
                    return false;
                Vector3 feet = anchor.BasePosition;
                if (!float.IsFinite(feet.X + feet.Y + feet.Z))
                    return false;
                bool onTheLand = terrain(feet.X, feet.Y) is { } ground
                    && MathF.Abs(feet.Z - ground) <= OnTheLandMeters;
                float start = shape.FacesObject
                    ? shape.StartDegrees + anchor.HeadingDegrees
                    : shape.StartDegrees;
                placement = new GroundShapePlacement(feet, start, onTheLand);
                return true;
            }
            case PluginMarkerAnchorKind.Position:
            {
                Vector3 spot = PluginNavigationProjection.ToWorld(shape.Anchor.Position, centerX, centerY);
                if (!float.IsFinite(spot.X + spot.Y + spot.Z))
                    return false;
                placement = new GroundShapePlacement(spot, shape.StartDegrees, shape.Anchor.Position.IsOutdoor);
                return true;
            }
            default:
                return false;
        }
    }

    /// <summary><paramref name="point"/> moved <see cref="DepthNudge"/> of the way toward <paramref name="camera"/>.</summary>
    internal static Vector3 TowardCamera(Vector3 point, Vector3 camera) =>
        point + ((camera - point) * DepthNudge);

    private void Draw(in PlannedShape planned, Vector3 cameraPosition)
    {
        _triangles.Clear();
        GroundShapeTessellator.Tessellate(
            planned.Shape,
            planned.Placement.Centre,
            planned.Placement.StartDegrees,
            planned.Placement.FollowTerrain,
            terrain,
            _triangles);
        PluginColor c = planned.Shape.Color;
        var color = new Vector4(c.R, c.G, c.B, c.A) / 255f;
        ReadOnlySpan<Vector3> points = CollectionsMarshal.AsSpan(_triangles);
        for (int i = 0; i + 2 < points.Length; i += 3)
        {
            batch.AddTriangle(
                TowardCamera(points[i], cameraPosition),
                TowardCamera(points[i + 1], cameraPosition),
                TowardCamera(points[i + 2], cameraPosition),
                color);
        }
    }

    private readonly record struct PlannedShape(
        PluginGroundShape Shape,
        GroundShapePlacement Placement,
        float DistanceSquared,
        int VertexCount);
}

/// <summary>
/// Where one ground shape lies this frame: its centre in world metres, the
/// bearing its arc starts at (the object's heading already added), and
/// whether it follows the land or lies flat at the centre's height.
/// </summary>
internal readonly record struct GroundShapePlacement(Vector3 Centre, float StartDegrees, bool FollowTerrain);
```

- [ ] **Step 4: Run to verify they pass**

Run: `dotnet test tests/AcDream.App.Tests/AcDream.App.Tests.csproj --filter "FullyQualifiedName~PluginGroundShapeRendererTests"` → all pass.

- [ ] **Step 5: Commit**

```bash
git checkout -- '*.lock.json'
git add src/AcDream.App/Rendering/PluginGroundShapeRenderer.cs tests/AcDream.App.Tests/Rendering/PluginGroundShapeRendererTests.cs
git commit -m "rendering: place and draw plugins' ground shapes

At an object's feet (turned by its heading when the shape faces with it)
or at a spot; following the land when the object stands on it or the spot
is outdoors, flat otherwise. Shapes past 250 m are skipped and the nearest
drawn first when the budget runs out; points are nudged toward the camera
so the land doesn't flicker through them.

Claude-Session: https://claude.ai/code/session_01EtugJHhFMqGXY2V6Lz2ZFB"
```

```json:metadata
{"files": ["src/AcDream.App/Rendering/PluginGroundShapeRenderer.cs", "tests/AcDream.App.Tests/Rendering/PluginGroundShapeRendererTests.cs"], "verifyCommand": "dotnet test tests/AcDream.App.Tests/AcDream.App.Tests.csproj --filter \"FullyQualifiedName~PluginGroundShapeRendererTests\"", "acceptanceCriteria": ["nothing drawn before origin known or for an unheld object", "centre beyond 250 m not drawn, inside drawn", "ring around object base; FacesObject wedge turns with heading", "TryPlace on-the-land rule (1 m), no-land flat, spot follows only when IsOutdoor", "colour/255 reaches batch; TowardCamera moves 0.2%", "budget respected and nearest shape drawn"], "modelTier": "standard"}
```

---

### Task 6: Draw the shapes in the client's world pass

**Goal:** The live client draws every plugin's ground shapes in the Vulkan world phase, right after the plugin world lines, with the batch created and torn down alongside the frame root.

**Files:**
- Modify: `src/AcDream.App/Rendering/Gpu/Vk/VulkanCompositionFramePhases.cs` (`VulkanWorldScenePhase`: field and constructor at ~lines 77-106; render calls at ~lines 286-287 and ~389-390)
- Modify: `src/AcDream.App/Composition/FrameRootComposition.cs` (`FrameRootDependencies` at ~line 61; the `VulkanWorldScenePhase` construction at ~lines 520-537)
- Modify: `src/AcDream.App/Rendering/GameWindow.cs` (the `FrameRootDependencies` construction, `WorldLines),` at ~line 1581)

**Acceptance Criteria:**
- [ ] `VulkanWorldScenePhase` takes an optional `PluginGroundShapeRenderer? groundShapes` and calls `Render` right after `_worldLines?.Render(...)` at both call sites, only when the normal world was drawn.
- [ ] `FrameRootDependencies` has `PluginWorldMarkerStore? WorldMarkers = null`; `GameWindow` passes `_uiRegistry?.WorldMarkerStore`.
- [ ] When the store is present, the frame root builds a `GroundShapeBatch` (adopted by the frame-root bindings, so it is disposed before the GPU device) and a `PluginGroundShapeRenderer` reading `CaptureShapes`, the selection's label anchors and the physics terrain.
- [ ] `dotnet build src/AcDream.App -c Release` succeeds; the full App and Plugin suites have no failures beyond the baseline.

**Verify:** `dotnet build src/AcDream.App/AcDream.App.csproj -c Release 2>&1 | grep -E "Warn|Error" | tail -2` → `0 Error(s)`; then the App suite (Step 4) → failures identical to the baseline.

**Steps:**

- [ ] **Step 1: Draw after the lines** — in `src/AcDream.App/Rendering/Gpu/Vk/VulkanCompositionFramePhases.cs`, class `VulkanWorldScenePhase`:

(a) After `private readonly PluginWorldLineRenderer? _worldLines;` add `private readonly PluginGroundShapeRenderer? _groundShapes;`.

(b) Constructor: change the last parameter line `PluginWorldLineRenderer? worldLines = null)` to

```csharp
        PluginWorldLineRenderer? worldLines = null,
        PluginGroundShapeRenderer? groundShapes = null)
```

and after `_worldLines = worldLines;` add `_groundShapes = groundShapes;`.

(c) At both call sites replace

```csharp
                if (outcome.NormalWorldDrawn)
                    _worldLines?.Render(encoder, input.ViewportWidth, input.ViewportHeight);
```

(indentation differs between the two sites; keep each site's own) with

```csharp
                if (outcome.NormalWorldDrawn)
                {
                    _worldLines?.Render(encoder, input.ViewportWidth, input.ViewportHeight);
                    // After the lines: the shapes are see-through, and the
                    // lines' depth hides them too.
                    _groundShapes?.Render(encoder, input.ViewportWidth, input.ViewportHeight);
                }
```

- [ ] **Step 2: Build the renderer at the frame root** — in `src/AcDream.App/Composition/FrameRootComposition.cs`:

(a) In `FrameRootDependencies`, change the last parameter `AcDream.App.Plugins.PluginWorldLineStore? WorldLines = null)` to

```csharp
    AcDream.App.Plugins.PluginWorldLineStore? WorldLines = null,
    AcDream.App.Plugins.PluginWorldMarkerStore? WorldMarkers = null)
```

(b) Immediately before `worldSceneRenderer =` / `new AcDream.App.Rendering.Gpu.Vk.VulkanWorldScenePhase(` (~line 518), add:

```csharp
            PluginGroundShapeRenderer? groundShapes = null;
            if (d.WorldMarkers is { } worldMarkers)
            {
                // worldPassScope was checked non-null when the world pass surface was built above.
                var groundShapeBatch = new GroundShapeBatch(host.GpuDevice, host.GpuFrameLifetime, worldPassScope!);
                bindings.Adopt("plugin ground shape batch", groundShapeBatch);
                groundShapes = new PluginGroundShapeRenderer(
                    worldMarkers.CaptureShapes,
                    groundShapeBatch,
                    new RuntimeWorldFrameCameraSource(host.CameraController, session.LocalTeleport.ApplyViewPlane),
                    d.WorldOrigin,
                    guid => interaction.LateBindings.Selection.TryResolveWorldLabelAnchor(
                        guid, out AcDream.App.Interaction.WorldLabelAnchor anchor)
                        ? anchor
                        : null,
                    d.PhysicsEngine.SampleTerrainZ);
            }
```

(c) In the `VulkanWorldScenePhase` construction, after the `d.WorldLines is null ? null : new PluginWorldLineRenderer(...)` argument (which ends `d.WorldOrigin, d.PhysicsEngine));`), change that closing `));` to `),` and add the argument `groundShapes);` so the call ends:

```csharp
                    d.WorldLines is null ? null : new PluginWorldLineRenderer(
                        d.WorldLines, foundation.DebugLines,
                        new RuntimeWorldFrameCameraSource(host.CameraController, session.LocalTeleport.ApplyViewPlane),
                        d.WorldOrigin, d.PhysicsEngine),
                    groundShapes);
```

- [ ] **Step 3: Hand over the store** — in `src/AcDream.App/Rendering/GameWindow.cs`, in the `new FrameRootDependencies(...)` call (~line 1581) change `WorldLines),` to

```csharp
                        WorldLines,
                        _uiRegistry?.WorldMarkerStore),
```

- [ ] **Step 4: Build and run the suites**

```bash
dotnet build src/AcDream.App/AcDream.App.csproj -c Release 2>&1 | grep -E "Warn|Error" | tail -2
export ACDREAM_DAT_DIR=$HOME/AsheronsCall
mkdir -p /tmp/openac-shapes/suite
for p in AcDream.Plugin.Tests AcDream.Core.Tests AcDream.App.Tests; do
  dotnet test tests/$p/$p.csproj -c Release --logger "trx;LogFileName=/tmp/openac-shapes/suite/$p.trx" > /tmp/openac-shapes/suite/$p.log 2>&1
  echo "$p: $(grep -E 'Passed!|Failed!' /tmp/openac-shapes/suite/$p.log | tail -1)"
done
```

Expected: build `0 Error(s)`; Plugin and Core `Passed!`; App `Failed: 52` (the baseline). If App reports any other count, list the failing names (`grep -o 'testName="[^"]*"[^>]*outcome="Failed"' /tmp/openac-shapes/suite/AcDream.App.Tests.trx`) and check each new name against the environmental classes in the Global Constraints; a new failure that isn't environmental is this task's to fix.

- [ ] **Step 5: Commit**

```bash
git checkout -- '*.lock.json'
git add src/AcDream.App/Rendering/Gpu/Vk/VulkanCompositionFramePhases.cs src/AcDream.App/Composition/FrameRootComposition.cs \
  src/AcDream.App/Rendering/GameWindow.cs
git commit -m "rendering: draw plugins' ground shapes in the world pass

Right after the plugin world lines, at both world-phase call sites, only
when the normal world was drawn (so never in portal space). The batch is
owned by the frame-root bindings and goes before the GPU device.

Claude-Session: https://claude.ai/code/session_01EtugJHhFMqGXY2V6Lz2ZFB"
```

```json:metadata
{"files": ["src/AcDream.App/Rendering/Gpu/Vk/VulkanCompositionFramePhases.cs", "src/AcDream.App/Composition/FrameRootComposition.cs", "src/AcDream.App/Rendering/GameWindow.cs"], "verifyCommand": "dotnet build src/AcDream.App/AcDream.App.csproj -c Release", "acceptanceCriteria": ["VulkanWorldScenePhase renders ground shapes after world lines at both call sites, only when NormalWorldDrawn", "FrameRootDependencies.WorldMarkers; GameWindow passes _uiRegistry?.WorldMarkerStore", "batch adopted by frame-root bindings; renderer reads CaptureShapes, selection label anchors, physics terrain", "Release build succeeds; Plugin/Core pass; App failures identical to the 52 baseline"], "modelTier": "standard"}
```

---

### Task 7: Document ground shapes and show them in the demo

**Goal:** `docs/plugin-api.md` explains ground shapes, and the demo plugin lays a ring and a facing wedge under the selected object and a disc and a half band at its pinned spot.

**Files:**
- Modify: `docs/plugin-api.md` ("World markers" section, ~lines 1270-1330)
- Modify (full replacement): `samples/AcDream.Plugins.WorldMarkersDemo/WorldMarkersDemoPlugin.cs`

**Acceptance Criteria:**
- [ ] The "World markers" section's example sets shapes, and the section describes ring/disc/arc, bearings and `FacesObject`, occlusion and terrain following, the 250 m range, the `MaximumShapes` cap and the drop rules, and that leaving the world clears shapes too.
- [ ] The demo builds and sets, over the selected object (or the player), a ring and a wedge that faces with it, and at the pinned spot (on the ground, below the spot icon) a translucent disc and a half band.
- [ ] `dotnet build samples/AcDream.Plugins.WorldMarkersDemo -c Release` has 0 errors.

**Verify:** `dotnet build samples/AcDream.Plugins.WorldMarkersDemo/AcDream.Plugins.WorldMarkersDemo.csproj -c Release 2>&1 | grep -E "Warn|Error" | tail -2` → `0 Error(s)`.

**Steps:**

- [ ] **Step 1: Docs** — in `docs/plugin-api.md`, section `## World markers`:

(a) In the opening code block, after the `layer?.SetIcons(...);` statement add:

```csharp

layer?.SetShapes(
[
    PluginGroundShape.Ring(PluginMarkerAnchor.Object(targetId), radius: 2f, width: 0.15f, new PluginColor(220, 60, 60)),
    PluginGroundShape.Disc(PluginMarkerAnchor.At(blastCentre), radius: 6f, new PluginColor(255, 80, 0, 90)),
    PluginGroundShape.Arc(PluginMarkerAnchor.Object(creatureId), radius: 5f, startDegrees: -45f, sweepDegrees: 90f,
        filled: true, new PluginColor(255, 200, 40, 90)) with { FacesObject = true },
]);
```

(b) Replace the first prose paragraph's opening `` `host.Ui.WorldMarkers` hangs images in the world: over an object, following
it wherever it goes, or pinned to a position. A plugin makes layers and sets
each layer's icons; a call replaces that layer's set, and the list is copied,
so it can be reused. Disposing a layer takes its icons away.`` with:

```markdown
`host.Ui.WorldMarkers` hangs images in the world -- over an object, following
it wherever it goes, or pinned to a position -- and lays shapes on the ground
around them. A plugin makes layers and sets each layer's icons and shapes; a
call replaces that layer's set of that kind, and the list is copied, so it
can be reused. Disposing a layer takes its markers away.
```

(c) Immediately before the paragraph that starts `Like labels, icons are not occluded`, insert:

```markdown
`SetShapes` lays shapes on the ground around an anchor. `Ring` is a band from
`radius - width` out to `radius`; `Disc` is filled; `Arc` is part of a
circle -- a wedge filled from the centre, or, unfilled, a band like a ring's.
Sizes are metres, and a radius may be 0.1 to 100. An arc starts at
`startDegrees`, a compass bearing (0 is north, 90 east), and runs clockwise
through `sweepDegrees` (more than 0, up to 360). With `FacesObject` an arc
over an object measures its start from the way the object faces, so a wedge
in front of a creature turns as it turns. The colour's alpha is the shape's
opacity.

Shapes are drawn in the world, not over it: walls and hills hide them, and
they never hide each other. Outdoors -- an object standing on the land, or a
position whose `IsOutdoor` is true -- a shape follows the slope of the land
under it; anywhere else (a dungeon, a floor, a roof) it lies flat at the
object's feet or at the position's height. A shape whose centre is more than
250 m from the camera is not drawn, and when a frame's shapes come to more
than the client draws at once, the nearest are drawn.

Each plugin may have at most `IPluginWorldMarkers.MaximumShapes` (256)
shapes set across all its layers, counted apart from its icons, and refused
the same way. Inside an accepted set a shape is dropped when it is pinned to
nothing, of no kind, or has a radius out of range, a ring's or unfilled arc's
width that is not more than zero and at most the radius, or an arc's start
or sweep that is not a finite number or a sweep out of range. A ring's or
disc's start, sweep and `FacesObject` are ignored.
```

(d) In the lifecycle paragraph, change `every layer's icons are cleared` to `every layer's icons and shapes are cleared`, and `so the plugin sets its
icons again on the next stay` to `so the plugin sets its
markers again on the next stay`.

- [ ] **Step 2: Demo** — replace the whole of `samples/AcDream.Plugins.WorldMarkersDemo/WorldMarkersDemoPlugin.cs` with:

```csharp
using AcDream.Plugin.Abstractions;

namespace AcDream.Plugins.WorldMarkersDemo;

/// <summary>
/// Hangs a row of spell icons over the selected object -- or over the player
/// when nothing is selected -- with a ring at its feet and a wedge in front
/// of it that turns as it turns, and marks a spot a few metres east of where
/// the player stood when the demo first saw them with a larger,
/// half-transparent icon over a disc and a half band on the ground. It
/// exists to be looked at, for example to check the markers on a
/// high-density display, and does nothing else.
/// </summary>
public sealed class WorldMarkersDemoPlugin : IAcDreamPlugin
{
    // The first spells in the client's spell table; any spell with an icon would do.
    private static readonly uint[] RowSpellIds = [1u, 2u, 3u, 4u, 5u];
    private const uint SpotSpellId = 6u;
    private const double MetresPerMapUnit = 240d;
    private static readonly PluginColor Hostile = new(220, 60, 60);

    private readonly List<PluginImage> _row = [];
    private IPluginHost? _host;
    private IPluginWorldMarkerLayer? _layer;
    private PluginImage _spotImage = PluginImage.None;
    private PluginNavigationPosition? _ground;
    private uint _shownOver;
    private bool _dirty;

    public void Initialize(IPluginHost host) => _host = host;

    public void Enable()
    {
        if (_host is not { } host) return;
        _layer = host.Ui.WorldMarkers.CreateLayer();
        host.Events.Tick += OnTick;
        host.Events.Logoff += OnLogoff;
    }

    public void Disable()
    {
        if (_host is { } host)
        {
            host.Events.Tick -= OnTick;
            host.Events.Logoff -= OnLogoff;
        }
        _layer?.Dispose();
        _layer = null;
    }

    // The client clears every layer on logoff; forget what was shown so the
    // next stay sets the markers again.
    private void OnLogoff()
    {
        _shownOver = 0u;
        _ground = null;
    }

    /// <summary>Images can only be asked for once the interface is up, and the markers follow the selection.</summary>
    private void OnTick(double deltaSeconds)
    {
        if (_host is not { } host || _layer is not { } layer) return;
        IPluginImages images = host.Ui.Images;
        if (!images.IsAvailable)
        {
            // The interface went away (a reconnect drops images): ask again when it is back.
            _row.Clear();
            _spotImage = PluginImage.None;
            return;
        }
        if (_row.Count == 0)
        {
            foreach (uint spellId in RowSpellIds)
                _row.Add(images.FromSpellIcon(spellId));
            _spotImage = images.FromSpellIcon(SpotSpellId);
            _dirty = true;
        }

        PluginNavigationSnapshot navigation = host.Automation.Navigation.Snapshot;
        if (!navigation.IsAvailable) return;
        if (_ground is null)
        {
            PluginNavigationPosition here = navigation.Position;
            _ground = here with { EastWest = here.EastWest + 5d / MetresPerMapUnit };
            _dirty = true;
        }

        uint over = host.Selection.SelectedObjectId ?? navigation.LocalObjectId;
        if (over != _shownOver)
        {
            _shownOver = over;
            _dirty = true;
        }
        if (!_dirty || over == 0u) return;
        _dirty = false;

        PluginNavigationPosition ground = _ground.Value;
        PluginNavigationPosition spot = ground with { Elevation = ground.Elevation + 1.5d / MetresPerMapUnit };
        var icons = new List<PluginWorldIcon>(_row.Count + 1);
        for (int i = 0; i < _row.Count; i++)
        {
            icons.Add(new PluginWorldIcon(PluginMarkerAnchor.Object(over), _row[i])
            {
                // Two framed, one faded, the rest plain: one of each look.
                Border = i < 2 ? Hostile : null,
                Tint = i == 2 ? new PluginColor(255, 255, 255, 110) : PluginColor.White,
            });
        }
        icons.Add(new PluginWorldIcon(PluginMarkerAnchor.At(spot), _spotImage)
        {
            SizePixels = 40f,
            Tint = new PluginColor(255, 255, 255, 180),
            MaxRange = 120f,
        });
        layer.SetIcons(icons);

        layer.SetShapes(
        [
            // A ring at the feet of whatever the icons hang over...
            PluginGroundShape.Ring(PluginMarkerAnchor.Object(over), radius: 1.5f, width: 0.15f, Hostile),
            // ...and a wedge in front of it that turns as it turns.
            PluginGroundShape.Arc(PluginMarkerAnchor.Object(over), radius: 4f, startDegrees: -30f, sweepDegrees: 60f,
                filled: true, new PluginColor(255, 200, 40, 90)) with { FacesObject = true },
            // A see-through disc under the spot, and a band round its eastern half.
            PluginGroundShape.Disc(PluginMarkerAnchor.At(ground), radius: 3f, new PluginColor(255, 80, 0, 90)),
            PluginGroundShape.Arc(PluginMarkerAnchor.At(ground), radius: 4.5f, startDegrees: 0f, sweepDegrees: 180f,
                filled: false, new PluginColor(80, 160, 255, 200), width: 0.3f),
        ]);
    }
}
```

- [ ] **Step 3: Build**

Run: `dotnet build samples/AcDream.Plugins.WorldMarkersDemo/AcDream.Plugins.WorldMarkersDemo.csproj -c Release 2>&1 | grep -E "Warn|Error" | tail -2` → `0 Error(s)`.

- [ ] **Step 4: Commit**

```bash
git checkout -- '*.lock.json'
git add docs/plugin-api.md samples/AcDream.Plugins.WorldMarkersDemo/WorldMarkersDemoPlugin.cs
git commit -m "docs, sample: ground shapes in the world markers section and demo

Claude-Session: https://claude.ai/code/session_01EtugJHhFMqGXY2V6Lz2ZFB"
```

```json:metadata
{"files": ["docs/plugin-api.md", "samples/AcDream.Plugins.WorldMarkersDemo/WorldMarkersDemoPlugin.cs"], "verifyCommand": "dotnet build samples/AcDream.Plugins.WorldMarkersDemo/AcDream.Plugins.WorldMarkersDemo.csproj -c Release", "acceptanceCriteria": ["World markers docs: SetShapes example; ring/disc/arc, bearings, FacesObject, occlusion, terrain following, 250 m range, MaximumShapes cap, drop rules, logoff clears shapes", "demo sets ring + facing wedge over the selected object/player and disc + half band at the spot on the ground", "demo builds with 0 errors"], "modelTier": "mechanical"}
```

---

### Task 8: See the shapes in the real client

**Goal:** In the live client on the user's local ACE server, the demo's ring and facing wedge lie on the ground under the selected creature (the wedge turning as it turns) and the disc and half band lie at the pinned spot; the user confirms from their own screenshot that it looks right.

> **USER-ORDERED GATE — NON-SKIPPABLE.** This task was requested by the user in the current conversation. It MUST NOT be closed by walking around it, by declaring it "verified inline", or by substituting a cheaper check. Close only after every item in `acceptanceCriteria` has been re-validated independently, with output captured.

**Files:**
- None committed. Scratch only: `/tmp/openac-shapes/`.

**Acceptance Criteria:**
- [ ] The client run's log shows `plugin loaded: sample.world-markers-demo` and a connection to the host from `~/OpenAC-dev/dev-client.env` (10.10.20.20), not Dreamweave.
- [ ] A screenshot from the user (⌘⇧3; the client's own capture fails on a Retina-only screen) shows, under the selected creature or the player, a red ring and a yellow see-through wedge, and at the spot ~5 m east of where the player entered, an orange see-through disc with a blue band round its eastern half, all lying on the ground.
- [ ] The user confirms the wedge turns when the creature (or they) turn, that a wall or hill in front hides part of a shape, and that it looks right.

**Verify:** the user's screenshot is read and described against the criteria, and the user's confirmation is quoted.

**Steps:**

- [ ] **Step 1: Build and prepare a scratch root with only the demo**

```bash
dotnet build src/AcDream.App/AcDream.App.csproj -c Release 2>&1 | grep -E "Error" | tail -1
dotnet build samples/AcDream.Plugins.WorldMarkersDemo/AcDream.Plugins.WorldMarkersDemo.csproj -c Release 2>&1 | grep -E "Error" | tail -1
git checkout -- '*.lock.json'
rm -rf /tmp/openac-shapes/root && mkdir -p /tmp/openac-shapes/root/plugins/sample.world-markers-demo
cp samples/AcDream.Plugins.WorldMarkersDemo/bin/Release/net10.0/* /tmp/openac-shapes/root/plugins/sample.world-markers-demo/
```

- [ ] **Step 2: Launch against the local server** (in the background, with the sandbox off for the network; never print the password)

```bash
( . ~/OpenAC-dev/dev-client.env
  export DOTNET_ROOT=$HOME/.dotnet PATH=$HOME/.dotnet:$PATH DYLD_LIBRARY_PATH=/opt/homebrew/lib \
    VK_DRIVER_FILES=/opt/homebrew/etc/vulkan/icd.d/MoltenVK_icd.json ACDREAM_LIVE=1 \
    ACDREAM_ROOT_DIR=/tmp/openac-shapes/root ACDREAM_DAT_DIR=$HOME/AsheronsCall \
    ACDREAM_PAK_PATH="$HOME/Library/Application Support/OpenAC/data/pak/acdream.pak" ACDREAM_NO_AUDIO=1
  APP=$PWD/src/AcDream.App/bin/Release/net10.0/AcDream.App.dll
  cd /tmp/openac-shapes && dotnet "$APP" > /tmp/openac-shapes/run.log 2>&1 )
```

Then check: `grep -E "plugin loaded: sample.world-markers-demo|live: connecting|Exception" /tmp/openac-shapes/run.log | grep -vi pass` → the plugin line and `connecting to 10.10.20.20:9000`.

- [ ] **Step 3: Hand over to the user**

Tell the user: the client is at character select on their local server. Enter the world (+RabidDee), select a creature (or nothing, for the markers to sit under their own character), turn the camera so the creature and the spot ~5 m east of where they came in are both in view, and take a ⌘⇧3 screenshot. Also: watch the wedge as the creature (or they) turn, and stand so a wall or hill is between the camera and part of a shape. Ask for the screenshot's file name, then to close the client.

- [ ] **Step 4: Look and confirm**

Read the screenshot, describe what is and isn't there against the acceptance criteria, send it to the user with SendUserFile, and ask whether it looks right and whether the wedge turned and the wall/hill hid the shape. If something is wrong, debug with superpowers-extended-cc:systematic-debugging before changing code; any fix goes in as its own commit with a test.

```json:metadata
{"files": [], "verifyCommand": "grep -E \"plugin loaded: sample.world-markers-demo|live: connecting\" /tmp/openac-shapes/run.log | grep -vi pass", "acceptanceCriteria": ["run log shows the demo plugin loaded and a connection to 10.10.20.20 (dev-client.env), not Dreamweave", "user screenshot shows red ring + yellow see-through wedge under the selected creature/player and orange see-through disc + blue eastern half band at the spot, lying on the ground", "user confirms the wedge turns with its object, a wall or hill hides part of a shape, and it looks right"], "modelTier": "standard", "userGate": true, "tags": ["user-gate"], "gateScope": "task", "failurePolicy": "stop-and-ask"}
```

---

## After the plan

- Whole-branch review (superpowers-extended-cc:requesting-code-review) against the spec and this plan.
- Merge `world-markers/shapes` into fork `main` with a merge commit, only after the user confirms (as PR 1 was). Pushing anything waits for the user to ask.
- Update the world-markers memory: PR 2 state, branch, merge commit.

## Execution record (2026-10-04)

Executed with subagent-driven development. Branch `world-markers/shapes`
(worktree `.worktrees/world-markers-shapes`), 9 commits on 11be68a0, HEAD
6f764202. Not merged, not pushed.

| Task | Commits | Review |
|---|---|---|
| 1 Contract and store | 7c7deb83 | clean |
| 2 Heading on the label anchor | 3b344ac7 | clean |
| 3 Tessellator | 991a34ec | clean |
| 4 Batch and `ground_shape` shader | df252041 | clean; no existing `.spv` changed |
| 5 Renderer | c08eb4bd | clean |
| 6 World-phase wiring | 5d8f8b45 | clean |
| 7 Docs and demo | c17dba5b | clean |
| Final whole-branch review fixes | 112c8dcf, 6f764202 | re-review: all addressed |
| 8 Live gate | none | **pending**; the user deferred it to 2026-10-05 |

Verification after the final fixes: Release build 0 warnings; Plugin
102/102; Core 5794/5794; App 52 failures, the same environmental classes
as the baseline (probes, installed-DAT, offscreen Vulkan, Linux-only, live).

**Rulings made during execution**

1. The commit trailers carry the executing session's URL
   (`session_019dvJ5gakBkq3uJyr1PAC3u`), not the planning session's.
2. Scratch output went to the session scratchpad, not `/tmp/openac-shapes`.
3. **One land-following rule for both anchor kinds** (from the final review;
   the spec is updated in 4e6203b6). A shape follows the land only when its
   anchor is in an outdoor cell and no more than 1 m above the sampled land.
   At or below the land counts. Anywhere else it lies flat at the anchor's
   height + 5 cm.
   - Previously a spot followed the land whenever `IsOutdoor` was set, and
     `IsOutdoor` includes roofs and bridges.
   - `WorldLabelAnchor` gains `IsOutdoor`, taken from the entity's cell
     (`RenderingDiagnostics.IsEnvCellId`). A null cell counts as outdoor.
   - To revert to the spec's original two rules, revert 112c8dcf.
4. Parked as cosmetic: a doubled blank line before the `SetShapes` doc
   comment in `WorldMarkers.cs`. Fix it the next time the file is touched.

**Deferred minors** (the final reviewer triaged all of these as fine to
leave):

- Tests:
  - The tessellator lacks a filled 360° arc test and a sliver arc test.
  - The batch budget test's bound is loose.
  - The renderer tests lack a two-frame reuse test and `FacesObject` at a
    Position anchor.
- Behaviour:
  - Drawing stops at the first shape that does not fit the budget, so
    smaller, farther shapes are left out. World lines do the same.
  - The range check for a spot uses the plugin's Elevation.
  - `Changed()` invalidates both snapshots.
  - The demo re-sends the unchanged spot shapes when the selection changes.

**Follow-ups suggested by the final review** (not in this PR):

- Frustum-cull shapes, and resolve the landblock once per shape instead of
  sampling every vertex through `PhysicsEngine.SampleTerrainZ`'s landblock
  scan.
- Large filled shapes can be clipped slightly on sharp ridges, because the
  fill rings are 4 m apart.
- An object jumping more than 1 m makes its shapes switch to flat.
- On the atmospheric path, colours are blended before tonemapping, as
  world lines' are.

**To run the live gate (Task 8):**

1. Rebuild `src/AcDream.App` and the demo in Release from the shapes
   worktree.
2. Copy the demo's `bin/Release/net10.0/*` into a fresh scratch
   `ACDREAM_ROOT_DIR/plugins/sample.world-markers-demo/`.
3. Launch as in Step 2, with `. ~/OpenAC-dev/dev-client.env` and the
   sandbox off for the network.
4. Confirm that the log shows `plugin loaded: sample.world-markers-demo` and
   `live: connecting to 10.10.20.20:9000`.
5. The user takes a ⌘⇧3 screenshot and confirms:
   - a red ring and a yellow see-through wedge under the creature (or the
     player);
   - an orange see-through disc with a blue band round its eastern half at
     the spot;
   - the wedge turns with its creature;
   - a wall or hill hides part of a shape;
   - a shape inside a building, or on a roof or bridge, lies flat.
6. Optionally, read the frame profiler's `[gpu-mem] ring-peak` in a dense
   town.

Then merge `world-markers/shapes` into fork main with a merge commit.
