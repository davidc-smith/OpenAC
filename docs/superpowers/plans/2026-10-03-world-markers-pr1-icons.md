# World markers PR 1 (icons) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers-extended-cc:subagent-driven-development (recommended) or superpowers-extended-cc:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Let a plugin hang images in the world, over objects (a row of debuff icons over a creature) or pinned to a position (a waypoint), through `host.Ui.WorldMarkers`.

**Architecture:** A new contract file in `AcDream.Plugin.Abstractions` (anchors, icons, a layer surface). An App-side `PluginWorldMarkerStore` owned by `BufferedUiRegistry` holds each plugin's layers, validates and caps them, and clears on `Logoff`. A `WorldIconOverlayController` on the interface overlay band (next to the world-label overlay) projects each icon every frame, lays out an object's icons in rows above its labels, and draws them, resolving each image through the owning plugin's own `PluginImages`.

**Tech Stack:** C# / .NET 10, xUnit, the retained UI (`UiElement`, `UiRenderContext`, `TextRenderer`), `ScreenProjection`.

**Spec:** `docs/superpowers/specs/2026-10-03-world-markers-design.md` (fork-only docs branch `docs/world-markers-spec`). This plan covers the spec's PR 1 only; ground shapes are PR 2 and get their own plan.

## Global Constraints

- Work in a worktree `.worktrees/world-markers-icons` on a new branch `world-markers/icons` from fork `main` (e57192bb or later). Never commit `docs/superpowers/**` on that branch.
- Build and test with the pinned SDK: every shell starts with `export DOTNET_ROOT=$HOME/.dotnet PATH=$HOME/.dotnet:$PATH`. Building rewrites some `*.lock.json` files with local RID churn; revert with `git checkout -- '*.lock.json'` before each commit, never commit that churn (a brand-new sample's own `packages.neutral.lock.json` is the exception and is committed).
- Additive plugin API only: new types, and default-implemented interface members. A plugin built against the current contract compiles and loads unchanged.
- Every public type and member in `AcDream.Plugin.Abstractions` carries XML docs (the project treats warnings as errors).
- Never throw on bad plugin data: invalid entries are dropped; an oversized set is refused with `false`. `ArgumentNullException` for a null list and `ObjectDisposedException` after disposal are the only throws, as world-line layers do.
- Icon sizes are in interface points (the overlay's unit, the same unit labels are laid out in). The renderer already draws interface points at the display's density; the only use of `UiRenderContext.PixelScale` here is snapping positions to whole device pixels.
- Limits: `IPluginWorldMarkers.MaximumIcons = 256` per plugin across all its layers; `PluginWorldIcon.MinimumSize = 8`, `MaximumSize = 128` (finite sizes are clamped into that range, non-finite ones dropped); default size 24; default range 60 m; rows of 8 icons, 2-point gaps; a 1-point border outside the image.
- Commit messages end with the line `Claude-Session: https://claude.ai/code/session_01EtugJHhFMqGXY2V6Lz2ZFB`.

**User decisions (already made):**
- Scope of the spec: icons over objects, icons at a spot, ground shapes (PR 2). Line upgrades deferred.
- Icons are always on top (interface overlay band), not hidden behind walls.
- The host lays out an object's icons in rows; plugins don't place slots.
- Icon extras: tint with opacity, and a border colour. No badges, no timer sweeps.
- One new surface on `host.Ui` with layers (approach A).
- Branches start from fork main and may use painter v2 code (`PixelScale`) directly.
- How a plugin knows a creature's debuffs is the plugin's business; the client only draws.

## Spec corrections made while planning

These were found while reading the code and are folded into the spec in the same docs commit as this plan:

1. **No `HeightOffset` on icons.** An object's icons share one row origin, and per-icon offsets would tear a row apart. Icons sit above the object's labels automatically; a plugin wanting them higher can add an empty-looking label line. YAGNI.
2. **Size:** finite sizes are clamped into 8..128; only non-finite sizes are dropped (the spec listed both). Sizes are interface points, not "times `PixelScale`".
3. **"Another plugin's image" is not detectable.** Image handles are per-plugin sequential ids (`PluginImageTable`), so a foreign handle is indistinguishable from an own one. Every icon's image is looked up in its own plugin's images only, so a plugin can never reach another plugin's image; a handle that doesn't resolve there draws nothing.
4. **Clearing on leaving the world uses `IEvents.Logoff`.** World labels do not in fact clear on logoff today: `RuntimeAutomationSurface.Unbind()` has no production caller. Markers subscribe to the shared `WorldEvents.Logoff`, which fires once per stay on every exit path (logout, lost connection, reconnect, stop).
5. **The refuse rule counts the entries given** (before drops), plus the plugin's other layers, so the outcome doesn't depend on which entries happen to be invalid.

## File Structure

| File | Responsibility |
|---|---|
| Create `src/AcDream.Plugin.Abstractions/WorldMarkers.cs` | Public contract: `PluginMarkerAnchorKind`, `PluginMarkerAnchor`, `PluginWorldIcon`, `IPluginWorldMarkers`, `IPluginWorldMarkerLayer`, `NoOpPluginWorldMarkers`. |
| Modify `src/AcDream.Plugin.Abstractions/IUiRegistry.cs` | `IUiRegistry.WorldMarkers` and `IScopedUiRegistry.WorldMarkersFor(owner)`, default inert. |
| Create `src/AcDream.App/Plugins/WorldMarkerRules.cs` | Pure validation of anchors and icons (shared with PR 2's shapes). |
| Create `src/AcDream.App/Plugins/PluginWorldMarkerStore.cs` | Per-plugin surfaces and layers, caps, merged snapshot for the overlay, clear and clear-on-logoff. |
| Modify `src/AcDream.App/Plugins/BufferedUiRegistry.cs` | Owns the store; implements `WorldMarkersFor` and `WorldMarkers`. |
| Modify `src/AcDream.Core/Plugins/ScopedPluginHost.cs` | `ScopedUiRegistry.WorldMarkers`, asked once per plugin, disposed with the plugin. |
| Modify `src/AcDream.App/Rendering/GameWindow.cs` | Clear the store on `Logoff`; pass the world origin to the UI composition. |
| Create `src/AcDream.App/World/PluginNavigationProjection.cs` | Navigation position → world metres (moved out of `PluginWorldLineRenderer`). |
| Modify `src/AcDream.App/Rendering/PluginWorldLineRenderer.cs` | Use `PluginNavigationProjection`. |
| Create `src/AcDream.App/UI/Layout/WorldIconOverlayController.cs` | `WorldIconLayout` (pure placement), `WorldIconLayerElement` (drawing), `WorldIconOverlayController` (per-frame driver). |
| Modify `src/AcDream.App/UI/RetailUiRuntime.cs` | Binding for world positions; mount and tick the icon overlay before the label overlay. |
| Modify `src/AcDream.App/Composition/InteractionRetainedUiComposition.cs` | `WorldOrigin` dependency; compose the `WorldPosition` binding. |
| Modify `docs/plugin-api.md` | "World markers" section. |
| Create `samples/AcDream.Plugins.WorldMarkersDemo/*` + modify `AcDream.slnx` | Demo plugin for the real-client gate. |
| Tests | `tests/AcDream.Plugin.Tests/PluginWorldMarkersContractTests.cs`, `tests/AcDream.App.Tests/Plugins/PluginWorldMarkerStoreTests.cs`, `tests/AcDream.App.Tests/Plugins/BufferedUiRegistryWorldMarkersTests.cs`, `tests/AcDream.Core.Tests/Plugins/ScopedUiRegistryWorldMarkersTests.cs`, `tests/AcDream.Headless.Tests/HeadlessPluginApiSurfaceTests.cs` (one test added), `tests/AcDream.App.Tests/World/PluginNavigationProjectionTests.cs`, `tests/AcDream.App.Tests/UI/Layout/WorldIconOverlayLayoutTests.cs`, `tests/AcDream.App.Tests/UI/Layout/WorldIconOverlayDrawTests.cs`. |

---

### Task 1: Plugin contract for world icons

**Goal:** Plugins can name anchors and icons and ask for a layer; every host is inert until the App implements it.

**Files:**
- Create: `src/AcDream.Plugin.Abstractions/WorldMarkers.cs`
- Modify: `src/AcDream.Plugin.Abstractions/IUiRegistry.cs` (after `IPluginFonts Fonts` at ~line 197; after `FontsFor` at ~line 314)
- Test: `tests/AcDream.Plugin.Tests/PluginWorldMarkersContractTests.cs`

**Acceptance Criteria:**
- [ ] `PluginMarkerAnchor.Object(id)` and `PluginMarkerAnchor.At(position)` set `Kind`, `ObjectId`, `Position`; `default(PluginMarkerAnchor).Kind` is `None`.
- [ ] `new PluginWorldIcon(anchor, image)` defaults to size 24, white tint, no border, range 60.
- [ ] `NoOpPluginWorldMarkers.Instance.CreateLayer()`, `NoOpUiRegistry.Instance` (as `IUiRegistry`) `.WorldMarkers.CreateLayer()` and the default `IScopedUiRegistry.WorldMarkersFor` all answer null / the inert surface.
- [ ] `dotnet build src/AcDream.Plugin.Abstractions` has 0 warnings.

**Verify:** `dotnet test tests/AcDream.Plugin.Tests/AcDream.Plugin.Tests.csproj --filter "FullyQualifiedName~PluginWorldMarkersContractTests"` → all pass.

**Steps:**

- [ ] **Step 0: Create the worktree**

```bash
cd /Users/davidsmith/code/OpenAC/OpenAC
git worktree add -b world-markers/icons .worktrees/world-markers-icons main
cd .worktrees/world-markers-icons
export DOTNET_ROOT=$HOME/.dotnet PATH=$HOME/.dotnet:$PATH
```

- [ ] **Step 1: Write the failing test** — `tests/AcDream.Plugin.Tests/PluginWorldMarkersContractTests.cs`

```csharp
using AcDream.Plugin.Abstractions;

namespace AcDream.Plugin.Tests;

/// <summary>
/// The world-marker contract a plugin compiles against: how anchors are
/// made, what an icon looks like when the plugin says nothing more, and
/// that a host which draws nothing hands out a surface with no layers.
/// </summary>
public sealed class PluginWorldMarkersContractTests
{
    private static readonly PluginNavigationPosition Spot =
        new(0x7D640013u, 12.5, -40.25, 0.5, 90f, IsOutdoor: true);

    [Fact]
    public void AnObjectAnchorNamesTheObjectAndNoPosition()
    {
        PluginMarkerAnchor anchor = PluginMarkerAnchor.Object(0x80001234u);

        Assert.Equal(PluginMarkerAnchorKind.Object, anchor.Kind);
        Assert.Equal(0x80001234u, anchor.ObjectId);
        Assert.Equal(default, anchor.Position);
    }

    [Fact]
    public void APositionAnchorNamesThePositionAndNoObject()
    {
        PluginMarkerAnchor anchor = PluginMarkerAnchor.At(Spot);

        Assert.Equal(PluginMarkerAnchorKind.Position, anchor.Kind);
        Assert.Equal(0u, anchor.ObjectId);
        Assert.Equal(Spot, anchor.Position);
    }

    [Fact]
    public void TheDefaultAnchorPinsNothing()
    {
        Assert.Equal(PluginMarkerAnchorKind.None, default(PluginMarkerAnchor).Kind);
    }

    [Fact]
    public void AnIconDefaultsToAMediumWhiteUnframedIconSeenFromSixtyMetres()
    {
        var icon = new PluginWorldIcon(PluginMarkerAnchor.Object(1u), new PluginImage(3, 32, 32));

        Assert.Equal(24f, icon.SizePixels);
        Assert.Equal(PluginColor.White, icon.Tint);
        Assert.Null(icon.Border);
        Assert.Equal(60f, icon.MaxRange);
        Assert.Equal(8f, PluginWorldIcon.MinimumSize);
        Assert.Equal(128f, PluginWorldIcon.MaximumSize);
        Assert.Equal(256, IPluginWorldMarkers.MaximumIcons);
    }

    [Fact]
    public void AHostThatDrawsNothingHandsOutNoLayers()
    {
        IUiRegistry ui = NoOpUiRegistry.Instance;
        IScopedUiRegistry scoped = NoOpUiRegistry.Instance;

        Assert.Null(NoOpPluginWorldMarkers.Instance.CreateLayer());
        Assert.Null(ui.WorldMarkers.CreateLayer());
        Assert.Same(
            NoOpPluginWorldMarkers.Instance,
            scoped.WorldMarkersFor(new PluginUiOwner("example.plugin", "Example")));
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/AcDream.Plugin.Tests/AcDream.Plugin.Tests.csproj --filter "FullyQualifiedName~PluginWorldMarkersContractTests"`
Expected: build FAIL, `PluginMarkerAnchor` / `PluginWorldIcon` / `IPluginWorldMarkers` not found.

- [ ] **Step 3: Write the contract** — `src/AcDream.Plugin.Abstractions/WorldMarkers.cs`

```csharp
namespace AcDream.Plugin.Abstractions;

/// <summary>What a world marker is pinned to.</summary>
public enum PluginMarkerAnchorKind
{
    /// <summary>
    /// Nothing: the default anchor. A marker pinned to nothing is dropped.
    /// </summary>
    None = 0,

    /// <summary>An object the client holds, by the id the server gave it.</summary>
    Object = 1,

    /// <summary>A fixed position in the world.</summary>
    Position = 2,
}

/// <summary>
/// Where a world marker is: over an object, following it wherever it goes,
/// or at a fixed position. Made with <see cref="Object"/> or
/// <see cref="At"/>; the default value pins nothing.
/// </summary>
public readonly record struct PluginMarkerAnchor
{
    private PluginMarkerAnchor(
        PluginMarkerAnchorKind kind,
        uint objectId,
        PluginNavigationPosition position)
    {
        Kind = kind;
        ObjectId = objectId;
        Position = position;
    }

    /// <summary>What the marker is pinned to.</summary>
    public PluginMarkerAnchorKind Kind { get; }

    /// <summary>
    /// The object followed, when <see cref="Kind"/> is
    /// <see cref="PluginMarkerAnchorKind.Object"/>; zero otherwise.
    /// </summary>
    public uint ObjectId { get; }

    /// <summary>
    /// The position marked, when <see cref="Kind"/> is
    /// <see cref="PluginMarkerAnchorKind.Position"/>; the default otherwise.
    /// </summary>
    public PluginNavigationPosition Position { get; }

    /// <summary>
    /// Pins a marker over an object. While the client does not hold the
    /// object the marker is simply not drawn; it comes back when the object
    /// does. An id of zero pins nothing, and the marker is dropped.
    /// </summary>
    /// <param name="objectId">The object's id, as the server gave it.</param>
    public static PluginMarkerAnchor Object(uint objectId) =>
        new(PluginMarkerAnchorKind.Object, objectId, default);

    /// <summary>
    /// Pins a marker to a fixed position, the same kind of position world
    /// lines and navigation use. A position with a coordinate that is not a
    /// finite number pins nothing, and the marker is dropped.
    /// </summary>
    /// <param name="position">Where the marker is.</param>
    public static PluginMarkerAnchor At(PluginNavigationPosition position) =>
        new(PluginMarkerAnchorKind.Position, 0u, position);
}

/// <summary>
/// One image hung in the world, drawn at a constant screen size over the
/// interface's view of the world. Icons are not hidden by walls or hills:
/// like labels, they are drawn after the world.
/// </summary>
/// <param name="Anchor">
/// Where the icon is. Over an object, the client lays the object's icons out
/// in rows above its head and above any labels on it, in the order given; at
/// a position, the icon is centred on the position.
/// </param>
/// <param name="Image">
/// The image, from this plugin's own <see cref="IUiRegistry.Images"/>. It is
/// looked up in this plugin's images only. An icon whose image is not valid
/// is dropped; one whose image is released, or dropped when the interface
/// is torn down, is not drawn until the plugin sets its icons again.
/// </param>
public readonly record struct PluginWorldIcon(PluginMarkerAnchor Anchor, PluginImage Image)
{
    /// <summary>The smallest size an icon is drawn at, in interface points.</summary>
    public const float MinimumSize = 8f;

    /// <summary>The largest size an icon is drawn at, in interface points.</summary>
    public const float MaximumSize = 128f;

    /// <summary>
    /// The side of the square the image is fitted into, keeping its shape, in
    /// interface points. Clamped to between <see cref="MinimumSize"/> and
    /// <see cref="MaximumSize"/>; an icon whose size is not a finite number is
    /// dropped.
    /// </summary>
    public float SizePixels { get; init; } = 24f;

    /// <summary>
    /// Multiplied into the image: white leaves it unchanged, and the alpha is
    /// the icon's opacity.
    /// </summary>
    public PluginColor Tint { get; init; } = PluginColor.White;

    /// <summary>A thin frame drawn around the image, or null for none.</summary>
    public PluginColor? Border { get; init; }

    /// <summary>
    /// How far from the camera, in metres, the icon is still drawn. It fades
    /// out over the last fifth of that distance. Must be a positive finite
    /// number, or the icon is dropped.
    /// </summary>
    public float MaxRange { get; init; } = 60f;
}

/// <summary>
/// A set of world markers one plugin owns. Setting a kind of marker replaces
/// that kind's set on this layer; disposing the layer removes its markers
/// from the world.
/// </summary>
public interface IPluginWorldMarkerLayer : IDisposable
{
    /// <summary>
    /// Replaces this layer's icons with <paramref name="icons"/>. The list is
    /// copied and may be reused. Icons pinned to nothing, with an image that
    /// is not valid, or with a size or range that is not a finite number (or
    /// a range that is not positive) are dropped from the set. An empty list
    /// clears the layer's icons.
    /// </summary>
    /// <param name="icons">The icons to show from now on.</param>
    /// <returns>
    /// True when the set was taken. False when the entries given, added to
    /// the icons on this plugin's other layers, come to more than
    /// <see cref="IPluginWorldMarkers.MaximumIcons"/>; the layer then keeps
    /// what it had.
    /// </returns>
    bool SetIcons(IReadOnlyList<PluginWorldIcon> icons);
}

/// <summary>
/// Markers a plugin draws in the world -- icons over objects or at fixed
/// positions -- in layers it owns. The layers go with the plugin when it is
/// unloaded, and every layer's markers are cleared when the character leaves
/// the world (the layers stay usable).
/// </summary>
public interface IPluginWorldMarkers
{
    /// <summary>
    /// The most icons one plugin may have set at once, across all its layers.
    /// </summary>
    const int MaximumIcons = 256;

    /// <summary>
    /// Creates a layer to draw into, or null when this host draws nothing (a
    /// process with no window).
    /// </summary>
    IPluginWorldMarkerLayer? CreateLayer();
}

/// <summary>The world markers of a host that draws nothing.</summary>
public sealed class NoOpPluginWorldMarkers : IPluginWorldMarkers
{
    /// <summary>The one shared instance.</summary>
    public static NoOpPluginWorldMarkers Instance { get; } = new();

    private NoOpPluginWorldMarkers()
    {
    }

    /// <inheritdoc />
    public IPluginWorldMarkerLayer? CreateLayer() => null;
}
```

- [ ] **Step 4: Add the registry members** — `src/AcDream.Plugin.Abstractions/IUiRegistry.cs`

After `IPluginFonts Fonts => NoOpPluginFonts.Instance;` in `IUiRegistry`:

```csharp

    /// <summary>
    /// The markers this plugin hangs in the world -- icons over objects or at
    /// fixed positions -- in layers it owns. Inert on a host that draws
    /// nothing, where <see cref="IPluginWorldMarkers.CreateLayer"/> answers
    /// null.
    /// </summary>
    IPluginWorldMarkers WorldMarkers => NoOpPluginWorldMarkers.Instance;
```

After `IPluginFonts FontsFor(PluginUiOwner owner) => NoOpPluginFonts.Instance;` in `IScopedUiRegistry`:

```csharp

    /// <summary>
    /// One plugin's world-marker surface, the same object on every call for
    /// the same owner until it is disposed. A host that draws nothing hands
    /// out the inert surface; a host that draws hands out a surface that also
    /// implements <see cref="IDisposable"/>, and disposing it takes down every
    /// layer the plugin made.
    /// </summary>
    IPluginWorldMarkers WorldMarkersFor(PluginUiOwner owner) => NoOpPluginWorldMarkers.Instance;
```

- [ ] **Step 5: Run test to verify it passes**

Run: `dotnet test tests/AcDream.Plugin.Tests/AcDream.Plugin.Tests.csproj --filter "FullyQualifiedName~PluginWorldMarkersContractTests"`
Expected: 5 passed. Then `dotnet build src/AcDream.Plugin.Abstractions/AcDream.Plugin.Abstractions.csproj -c Release 2>&1 | grep -E "arning\(s\)|rror\(s\)"` → `0 Warning(s)`, `0 Error(s)`.

- [ ] **Step 6: Commit**

```bash
git checkout -- '*.lock.json'
git add src/AcDream.Plugin.Abstractions/WorldMarkers.cs src/AcDream.Plugin.Abstractions/IUiRegistry.cs \
  tests/AcDream.Plugin.Tests/PluginWorldMarkersContractTests.cs
git commit -m "plugin api: world marker contract -- anchors, icons, layers

Claude-Session: https://claude.ai/code/session_01EtugJHhFMqGXY2V6Lz2ZFB"
```

```json:metadata
{"files": ["src/AcDream.Plugin.Abstractions/WorldMarkers.cs", "src/AcDream.Plugin.Abstractions/IUiRegistry.cs", "tests/AcDream.Plugin.Tests/PluginWorldMarkersContractTests.cs"], "verifyCommand": "dotnet test tests/AcDream.Plugin.Tests/AcDream.Plugin.Tests.csproj --filter \"FullyQualifiedName~PluginWorldMarkersContractTests\"", "acceptanceCriteria": ["anchor factories set Kind/ObjectId/Position; default Kind is None", "icon defaults 24 / white / null border / 60", "inert hosts answer null or the NoOp surface", "Abstractions builds with 0 warnings"], "modelTier": "standard"}
```

---

### Task 2: The App-side marker store

**Goal:** One store holds every plugin's marker layers, validates and caps what they set, hands the overlay a merged snapshot, and clears on logoff.

**Files:**
- Create: `src/AcDream.App/Plugins/WorldMarkerRules.cs`
- Create: `src/AcDream.App/Plugins/PluginWorldMarkerStore.cs`
- Test: `tests/AcDream.App.Tests/Plugins/PluginWorldMarkerStoreTests.cs`

**Acceptance Criteria:**
- [ ] Icons with a `None` anchor, object id 0, a non-finite position coordinate, an invalid image, a non-finite size, or a non-finite / non-positive range are dropped; sizes 4 and 500 become 8 and 128.
- [ ] A set whose given count plus the plugin's other layers exceeds 256 returns false and leaves the layer unchanged; exactly 256 is accepted.
- [ ] `CaptureIcons()` lists icons by owner id (ordinal), then layer creation order, then given order, and returns the same instance until something changes.
- [ ] Disposing a layer removes its icons; disposing a plugin's surface removes all its layers and `For` then hands out a fresh surface; both throw `ObjectDisposedException` when used afterwards.
- [ ] `Clear()` empties every layer and the layers stay usable; `ClearOn(events)` clears on `FireLogoff()` until the returned subscription is disposed.

**Verify:** `dotnet test tests/AcDream.App.Tests/AcDream.App.Tests.csproj --filter "FullyQualifiedName~PluginWorldMarkerStoreTests"` → all pass.

**Steps:**

- [ ] **Step 1: Write the failing test** — `tests/AcDream.App.Tests/Plugins/PluginWorldMarkerStoreTests.cs`

```csharp
using AcDream.App.Plugins;
using AcDream.Core.Plugins;
using AcDream.Plugin.Abstractions;

namespace AcDream.App.Tests.Plugins;

/// <summary>
/// What the store takes from a plugin and what it hands the overlay: the
/// drop rules, the cap across a plugin's layers, the order the overlay sees,
/// and how layers, surfaces and the whole set go away.
/// </summary>
public sealed class PluginWorldMarkerStoreTests
{
    private static readonly PluginImage Image = new(1, 32, 32);

    private static PluginWorldIcon Over(uint objectId, PluginImage? image = null) =>
        new(PluginMarkerAnchor.Object(objectId), image ?? Image);

    private static IPluginWorldMarkerLayer Layer(PluginWorldMarkerStore store, string owner = "a.plugin") =>
        store.For(owner).CreateLayer()!;

    [Fact]
    public void TheSetIsCopiedSoTheListMayBeReused()
    {
        var store = new PluginWorldMarkerStore();
        var list = new List<PluginWorldIcon> { Over(1u) };

        Assert.True(Layer(store).SetIcons(list));
        list.Add(Over(2u));

        Assert.Single(store.CaptureIcons());
    }

    [Fact]
    public void InvalidIconsAreDroppedAndTheRestShown()
    {
        var store = new PluginWorldMarkerStore();
        var badPosition = new PluginNavigationPosition(1u, double.NaN, 0, 0, 0f, true);

        Assert.True(Layer(store).SetIcons(
        [
            new PluginWorldIcon(default, Image),
            Over(0u),
            new PluginWorldIcon(PluginMarkerAnchor.At(badPosition), Image),
            Over(1u, PluginImage.None),
            Over(1u) with { SizePixels = float.NaN },
            Over(1u) with { MaxRange = 0f },
            Over(1u) with { MaxRange = -5f },
            Over(1u) with { MaxRange = float.PositiveInfinity },
            Over(7u),
        ]));

        WorldIconEntry only = Assert.Single(store.CaptureIcons());
        Assert.Equal(7u, only.Icon.Anchor.ObjectId);
    }

    [Fact]
    public void SizesAreClampedIntoTheDrawableRange()
    {
        var store = new PluginWorldMarkerStore();

        Layer(store).SetIcons([Over(1u) with { SizePixels = 4f }, Over(2u) with { SizePixels = 500f }]);

        IReadOnlyList<WorldIconEntry> icons = store.CaptureIcons();
        Assert.Equal(PluginWorldIcon.MinimumSize, icons[0].Icon.SizePixels);
        Assert.Equal(PluginWorldIcon.MaximumSize, icons[1].Icon.SizePixels);
    }

    [Fact]
    public void TheCapCountsEveryLayerOfThePluginAndRefusesTheWholeSet()
    {
        var store = new PluginWorldMarkerStore();
        IPluginWorldMarkerLayer first = Layer(store);
        IPluginWorldMarkerLayer second = Layer(store);
        Assert.True(first.SetIcons(Enumerable.Repeat(Over(1u), 200).ToArray()));
        Assert.True(second.SetIcons([Over(2u)]));

        Assert.False(second.SetIcons(Enumerable.Repeat(Over(3u), 57).ToArray()));
        Assert.Equal(201, store.CaptureIcons().Count);
        Assert.Equal(2u, store.CaptureIcons()[200].Icon.Anchor.ObjectId);

        Assert.True(second.SetIcons(Enumerable.Repeat(Over(3u), 56).ToArray()));
        Assert.Equal(IPluginWorldMarkers.MaximumIcons, store.CaptureIcons().Count);
    }

    [Fact]
    public void AnotherPluginsIconsDoNotCountAgainstTheCap()
    {
        var store = new PluginWorldMarkerStore();
        Assert.True(Layer(store, "a.plugin").SetIcons(Enumerable.Repeat(Over(1u), 256).ToArray()));

        Assert.True(Layer(store, "b.plugin").SetIcons(Enumerable.Repeat(Over(2u), 256).ToArray()));
    }

    [Fact]
    public void SettingIconsReplacesTheLayersSet()
    {
        var store = new PluginWorldMarkerStore();
        IPluginWorldMarkerLayer layer = Layer(store);

        layer.SetIcons([Over(1u), Over(2u)]);
        layer.SetIcons([Over(3u)]);

        Assert.Equal(3u, Assert.Single(store.CaptureIcons()).Icon.Anchor.ObjectId);
    }

    [Fact]
    public void TheOverlaySeesIconsByPluginThenLayerThenTheOrderGiven()
    {
        var store = new PluginWorldMarkerStore();
        IPluginWorldMarkerLayer bFirst = Layer(store, "b.plugin");
        IPluginWorldMarkerLayer aFirst = Layer(store, "a.plugin");
        IPluginWorldMarkerLayer aSecond = Layer(store, "a.plugin");
        bFirst.SetIcons([Over(10u)]);
        aSecond.SetIcons([Over(30u), Over(31u)]);
        aFirst.SetIcons([Over(20u)]);

        IReadOnlyList<WorldIconEntry> icons = store.CaptureIcons();

        Assert.Equal(new[] { "a.plugin", "a.plugin", "a.plugin", "b.plugin" }, icons.Select(e => e.OwnerId));
        Assert.Equal(new[] { 20u, 30u, 31u, 10u }, icons.Select(e => e.Icon.Anchor.ObjectId));
    }

    [Fact]
    public void TheSnapshotIsReusedUntilSomethingChanges()
    {
        var store = new PluginWorldMarkerStore();
        IPluginWorldMarkerLayer layer = Layer(store);
        layer.SetIcons([Over(1u)]);

        IReadOnlyList<WorldIconEntry> first = store.CaptureIcons();
        Assert.Same(first, store.CaptureIcons());

        layer.SetIcons([Over(2u)]);
        Assert.NotSame(first, store.CaptureIcons());
    }

    [Fact]
    public void DisposingALayerTakesItsIconsAndRefusesFurtherUse()
    {
        var store = new PluginWorldMarkerStore();
        IPluginWorldMarkerLayer layer = Layer(store);
        layer.SetIcons([Over(1u)]);

        layer.Dispose();
        layer.Dispose();

        Assert.Empty(store.CaptureIcons());
        Assert.Throws<ObjectDisposedException>(() => layer.SetIcons([Over(2u)]));
    }

    [Fact]
    public void DisposingAPluginsSurfaceTakesEveryLayerAndTheNextAskIsFresh()
    {
        var store = new PluginWorldMarkerStore();
        IPluginWorldMarkers markers = store.For("a.plugin");
        IPluginWorldMarkerLayer layer = markers.CreateLayer()!;
        layer.SetIcons([Over(1u)]);

        ((IDisposable)markers).Dispose();

        Assert.Empty(store.CaptureIcons());
        Assert.Throws<ObjectDisposedException>(() => markers.CreateLayer());
        Assert.Throws<ObjectDisposedException>(() => layer.SetIcons([Over(2u)]));
        Assert.NotSame(markers, store.For("a.plugin"));
    }

    [Fact]
    public void TheSameOwnerGetsTheSameSurface()
    {
        var store = new PluginWorldMarkerStore();

        Assert.Same(store.For("a.plugin"), store.For("a.plugin"));
        Assert.NotSame(store.For("a.plugin"), store.For("b.plugin"));
    }

    [Fact]
    public void ClearingEmptiesEveryLayerButTheLayersStayUsable()
    {
        var store = new PluginWorldMarkerStore();
        IPluginWorldMarkerLayer layer = Layer(store);
        layer.SetIcons([Over(1u)]);
        Layer(store, "b.plugin").SetIcons([Over(2u)]);

        store.Clear();

        Assert.Empty(store.CaptureIcons());
        Assert.True(layer.SetIcons([Over(3u)]));
        Assert.Single(store.CaptureIcons());
    }

    [Fact]
    public void LeavingTheWorldClearsTheStoreUntilTheSubscriptionIsDisposed()
    {
        var store = new PluginWorldMarkerStore();
        var events = new WorldEvents();
        IPluginWorldMarkerLayer layer = Layer(store);
        layer.SetIcons([Over(1u)]);
        IDisposable subscription = store.ClearOn(events);

        events.FireLogoff();
        Assert.Empty(store.CaptureIcons());

        layer.SetIcons([Over(2u)]);
        subscription.Dispose();
        events.FireLogoff();
        Assert.Single(store.CaptureIcons());
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/AcDream.App.Tests/AcDream.App.Tests.csproj --filter "FullyQualifiedName~PluginWorldMarkerStoreTests"`
Expected: build FAIL, `PluginWorldMarkerStore` / `WorldIconEntry` not found.

- [ ] **Step 3: Write the rules** — `src/AcDream.App/Plugins/WorldMarkerRules.cs`

```csharp
using AcDream.Plugin.Abstractions;

namespace AcDream.App.Plugins;

/// <summary>
/// Which world markers a plugin's set keeps, and in what form. Pure: the
/// store applies it to every entry it is given.
/// </summary>
internal static class WorldMarkerRules
{
    /// <summary>
    /// True when the anchor pins something: an object with a non-zero id, or
    /// a position whose coordinates are all finite numbers.
    /// </summary>
    internal static bool IsValidAnchor(PluginMarkerAnchor anchor) => anchor.Kind switch
    {
        PluginMarkerAnchorKind.Object => anchor.ObjectId != 0u,
        PluginMarkerAnchorKind.Position =>
            double.IsFinite(anchor.Position.EastWest)
            && double.IsFinite(anchor.Position.NorthSouth)
            && double.IsFinite(anchor.Position.Elevation),
        _ => false,
    };

    /// <summary>
    /// The icon as the overlay will draw it, with its size clamped into
    /// range, or false when it is dropped.
    /// </summary>
    internal static bool TryNormalizeIcon(PluginWorldIcon icon, out PluginWorldIcon normalized)
    {
        normalized = default;
        if (!IsValidAnchor(icon.Anchor)
            || !icon.Image.IsValid
            || !float.IsFinite(icon.SizePixels)
            || !float.IsFinite(icon.MaxRange)
            || icon.MaxRange <= 0f)
        {
            return false;
        }

        normalized = icon with
        {
            SizePixels = Math.Clamp(icon.SizePixels, PluginWorldIcon.MinimumSize, PluginWorldIcon.MaximumSize),
        };
        return true;
    }
}
```

- [ ] **Step 4: Write the store** — `src/AcDream.App/Plugins/PluginWorldMarkerStore.cs`

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
/// ordered snapshot. Everything is guarded by one lock: plugins set markers
/// on the tick thread, but a logoff can clear them from whichever thread
/// ends the session.
/// </summary>
public sealed class PluginWorldMarkerStore
{
    private readonly object _gate = new();
    private readonly SortedDictionary<string, Markers> _owners = new(StringComparer.Ordinal);
    private WorldIconEntry[]? _merged = [];

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
                    layer.Icons = [];
            }
            _merged = null;
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
                store._merged = null;
            }
        }
    }

    private sealed class Layer(PluginWorldMarkerStore store, Markers owner) : IPluginWorldMarkerLayer
    {
        private bool _disposed;

        internal PluginWorldIcon[] Icons { get; set; } = [];

        public bool SetIcons(IReadOnlyList<PluginWorldIcon> icons)
        {
            ArgumentNullException.ThrowIfNull(icons);
            lock (store._gate)
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                if (icons.Count + owner.IconCountExcept(this) > IPluginWorldMarkers.MaximumIcons)
                    return false;
                var accepted = new List<PluginWorldIcon>(icons.Count);
                for (int i = 0; i < icons.Count; i++)
                {
                    if (WorldMarkerRules.TryNormalizeIcon(icons[i], out PluginWorldIcon icon))
                        accepted.Add(icon);
                }
                Icons = accepted.ToArray();
                store._merged = null;
                return true;
            }
        }

        /// <summary>Marks the layer gone and empties it; the caller removes it from its owner.</summary>
        internal void Detach()
        {
            _disposed = true;
            Icons = [];
        }

        public void Dispose()
        {
            lock (store._gate)
            {
                if (_disposed)
                    return;
                Detach();
                owner.Layers.Remove(this);
                store._merged = null;
            }
        }
    }
}
```

- [ ] **Step 5: Run test to verify it passes**

Run: `dotnet test tests/AcDream.App.Tests/AcDream.App.Tests.csproj --filter "FullyQualifiedName~PluginWorldMarkerStoreTests"`
Expected: 13 passed.

- [ ] **Step 6: Commit**

```bash
git checkout -- '*.lock.json'
git add src/AcDream.App/Plugins/WorldMarkerRules.cs src/AcDream.App/Plugins/PluginWorldMarkerStore.cs \
  tests/AcDream.App.Tests/Plugins/PluginWorldMarkerStoreTests.cs
git commit -m "app: world marker store -- per-plugin layers, drop rules, cap, logoff clear

Claude-Session: https://claude.ai/code/session_01EtugJHhFMqGXY2V6Lz2ZFB"
```

```json:metadata
{"files": ["src/AcDream.App/Plugins/WorldMarkerRules.cs", "src/AcDream.App/Plugins/PluginWorldMarkerStore.cs", "tests/AcDream.App.Tests/Plugins/PluginWorldMarkerStoreTests.cs"], "verifyCommand": "dotnet test tests/AcDream.App.Tests/AcDream.App.Tests.csproj --filter \"FullyQualifiedName~PluginWorldMarkerStoreTests\"", "acceptanceCriteria": ["invalid icons dropped, sizes clamped 8..128", "cap of 256 across a plugin's layers refuses the whole set", "snapshot ordered owner/layer/given and cached until change", "layer and surface disposal remove icons and throw ODE after", "Clear and ClearOn(Logoff) empty every layer, layers stay usable"], "modelTier": "standard"}
```

---

### Task 3: Hand each plugin its surface, and clear on logoff

**Goal:** `host.Ui.WorldMarkers` reaches the store under the plugin's own id on the graphical host, goes with the plugin when it unloads, stays inert headless, and the store clears when the character leaves the world.

**Files:**
- Modify: `src/AcDream.App/Plugins/BufferedUiRegistry.cs` (beside `ImagesFor` ~line 130 and `Images` ~line 536)
- Modify: `src/AcDream.Core/Plugins/ScopedPluginHost.cs` (`ScopedUiRegistry`, after the `Fonts` property ~line 2095)
- Modify: `src/AcDream.App/Rendering/GameWindow.cs` (after `_uiRegistry = uiRegistry;` ~line 607)
- Test: `tests/AcDream.App.Tests/Plugins/BufferedUiRegistryWorldMarkersTests.cs`
- Test: `tests/AcDream.Core.Tests/Plugins/ScopedUiRegistryWorldMarkersTests.cs`
- Test: `tests/AcDream.Headless.Tests/HeadlessPluginApiSurfaceTests.cs` (add one test)

**Acceptance Criteria:**
- [ ] `BufferedUiRegistry.WorldMarkersFor(owner)` returns the store's surface for `owner.Id`; icons set through it appear in `WorldMarkerStore.CaptureIcons()` under that owner id; `WorldMarkers` (unscoped) uses owner id `unscoped`.
- [ ] `ScopedPluginHost.Ui.WorldMarkers` asks the inner registry once under the plugin's owner, returns the same object after, and disposing the scoped host disposes it once.
- [ ] On the headless host `pluginHost.Ui.WorldMarkers.CreateLayer()` is null.
- [ ] `GameWindow` subscribes the store's `ClearOn` to its `WorldEvents`.

**Verify:** `dotnet test tests/AcDream.App.Tests/AcDream.App.Tests.csproj --filter "FullyQualifiedName~BufferedUiRegistryWorldMarkersTests"`, `dotnet test tests/AcDream.Core.Tests/AcDream.Core.Tests.csproj --filter "FullyQualifiedName~ScopedUiRegistryWorldMarkersTests"`, `dotnet test tests/AcDream.Headless.Tests/AcDream.Headless.Tests.csproj --filter "FullyQualifiedName~WithoutAWindowWorldMarkersHaveNoLayers"` → all pass.

**Steps:**

- [ ] **Step 1: Write the failing tests**

`tests/AcDream.App.Tests/Plugins/BufferedUiRegistryWorldMarkersTests.cs`:

```csharp
using AcDream.App.Plugins;
using AcDream.Plugin.Abstractions;

namespace AcDream.App.Tests.Plugins;

/// <summary>
/// The graphical host hands each plugin the store's surface under the
/// plugin's own id, so the overlay knows whose images an icon's image is.
/// </summary>
public sealed class BufferedUiRegistryWorldMarkersTests
{
    private static readonly PluginWorldIcon Icon =
        new(PluginMarkerAnchor.Object(1u), new PluginImage(1, 32, 32));

    [Fact]
    public void EachPluginGetsItsOwnSurfaceAndItsIconsCarryItsId()
    {
        var registry = new BufferedUiRegistry();
        IPluginWorldMarkers a = registry.WorldMarkersFor(new PluginUiOwner("a.plugin", "A"));

        Assert.Same(a, registry.WorldMarkersFor(new PluginUiOwner("a.plugin", "A")));
        Assert.NotSame(a, registry.WorldMarkersFor(new PluginUiOwner("b.plugin", "B")));

        Assert.True(a.CreateLayer()!.SetIcons([Icon]));
        Assert.Equal("a.plugin", Assert.Single(registry.WorldMarkerStore.CaptureIcons()).OwnerId);
    }

    [Fact]
    public void TheUnscopedSurfaceBelongsToTheUnscopedOwner()
    {
        var registry = new BufferedUiRegistry();

        Assert.True(registry.WorldMarkers.CreateLayer()!.SetIcons([Icon]));

        Assert.Equal("unscoped", Assert.Single(registry.WorldMarkerStore.CaptureIcons()).OwnerId);
    }
}
```

`tests/AcDream.Core.Tests/Plugins/ScopedUiRegistryWorldMarkersTests.cs`:

```csharp
using AcDream.Core.Plugins;
using AcDream.Core.Selection;
using AcDream.Plugin.Abstractions;

namespace AcDream.Core.Tests.Plugins;

/// <summary>
/// A plugin's world-marker surface is asked of the host once, under the
/// plugin's own owner, and goes with the plugin through the scoped
/// registry's existing rollback.
/// </summary>
public sealed class ScopedUiRegistryWorldMarkersTests
{
    private sealed class FakeMarkers : IPluginWorldMarkers, IDisposable
    {
        public int Disposals { get; private set; }

        public IPluginWorldMarkerLayer? CreateLayer() => null;

        public void Dispose() => Disposals++;
    }

    private sealed class FakeScopedUiRegistry : IScopedUiRegistry
    {
        public List<PluginUiOwner> Asked { get; } = [];
        public FakeMarkers Markers { get; } = new();

        public void AddMarkupPanel(string markupPath, object binding)
        {
        }

        public IDisposable RegisterMarkupPanel(string markupPath, object binding) =>
            NoOpUiRegistration.Instance;

        public IPluginWorldMarkers WorldMarkersFor(PluginUiOwner owner)
        {
            Asked.Add(owner);
            return Markers;
        }
    }

    private sealed class StubHost(IScopedUiRegistry ui) : IPluginHost
    {
        public bool HasUi => true;
        public IPluginLogger Log { get; } = new SilentLogger();
        public IGameState State { get; } = new EmptyGameState();
        public IEvents Events { get; } = new WorldEvents();
        public ISelectionService Selection { get; } = new SelectionState();
        public IUiRegistry Ui { get; } = ui;
        public IAutomationSurface Automation { get; } = NoOpAutomationSurface.Instance;

        private sealed class SilentLogger : IPluginLogger
        {
            public void Info(string message) { }
            public void Warn(string message) { }
            public void Error(string message, Exception? error = null) { }
        }

        private sealed class EmptyGameState : IGameState
        {
            public IReadOnlyList<WorldEntitySnapshot> Entities { get; } = [];
        }
    }

    [Fact]
    public void TheSurfaceIsAskedOnceUnderThePluginsOwnerAndKept()
    {
        var inner = new FakeScopedUiRegistry();
        var scoped = new ScopedPluginHost(new StubHost(inner), "example.plugin", "Example");

        IPluginWorldMarkers first = scoped.Ui.WorldMarkers;
        IPluginWorldMarkers second = scoped.Ui.WorldMarkers;

        Assert.Same(inner.Markers, first);
        Assert.Same(first, second);
        Assert.Equal(new PluginUiOwner("example.plugin", "Example"), Assert.Single(inner.Asked));
        scoped.Dispose();
    }

    [Fact]
    public void TheSurfaceGoesWithThePlugin()
    {
        var inner = new FakeScopedUiRegistry();
        var scoped = new ScopedPluginHost(new StubHost(inner), "example.plugin", "Example");
        _ = scoped.Ui.WorldMarkers;

        scoped.Dispose();

        Assert.Equal(1, inner.Markers.Disposals);
    }
}
```

Add to `tests/AcDream.Headless.Tests/HeadlessPluginApiSurfaceTests.cs`, right after `WithoutAWindowTheImageSurfaceAnswersInertly`:

```csharp

    /// <summary>
    /// World markers are asked of the same surface on both hosts. Without a
    /// window there is no world to hang them in, so no layer is made.
    /// </summary>
    [Fact]
    public void WithoutAWindowWorldMarkersHaveNoLayers()
    {
        using GameRuntime runtime = NewRuntime();
        using var host = NewHost(runtime);
        IPluginHost pluginHost = host;

        Assert.Null(pluginHost.Ui.WorldMarkers.CreateLayer());
    }
```

- [ ] **Step 2: Run tests to verify they fail**

Run the three verify commands.
Expected: the App test build FAILS (`WorldMarkerStore` and `WorldMarkersFor` don't exist on `BufferedUiRegistry`); the Core tests FAIL at `Assert.Same` / `Assert.Single(inner.Asked)` because the scoped registry still falls back to the interface's inert default; the headless test already passes, since the default member is inert, and stays as a guard.

- [ ] **Step 3: Implement the registry** — `src/AcDream.App/Plugins/BufferedUiRegistry.cs`

After the `FindImages` method (~line 201), add:

```csharp

    /// <summary>Every plugin's world markers, read by the interface's icon overlay.</summary>
    internal PluginWorldMarkerStore WorldMarkerStore { get; } = new();

    public IPluginWorldMarkers WorldMarkersFor(PluginUiOwner owner)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(owner.Id);
        return WorldMarkerStore.For(owner.Id);
    }
```

After `public IPluginFonts Fonts => ...;` (~line 539), add:

```csharp

    public IPluginWorldMarkers WorldMarkers =>
        WorldMarkersFor(new PluginUiOwner("unscoped", "Plugin"));
```

- [ ] **Step 4: Implement scoping** — `src/AcDream.Core/Plugins/ScopedPluginHost.cs`, in `ScopedUiRegistry`, after the `Fonts` property:

```csharp

        // The world-marker surface is asked for once and kept, like the image
        // surface; a disposable one is tracked so the plugin's layers go with
        // the plugin.
        private IPluginWorldMarkers? _worldMarkers;

        public IPluginWorldMarkers WorldMarkers
        {
            get
            {
                lock (_gate)
                {
                    if (_disposed)
                        throw new ObjectDisposedException(nameof(ScopedUiRegistry));
                    if (_worldMarkers is null)
                    {
                        _worldMarkers = _inner.WorldMarkersFor(_owner);
                        if (_worldMarkers is IDisposable disposable)
                            _registrations.Add(disposable);
                    }
                    return _worldMarkers;
                }
            }
        }
```

- [ ] **Step 5: Clear on logoff** — `src/AcDream.App/Rendering/GameWindow.cs`, right after `_uiRegistry = uiRegistry;`:

```csharp
        // A marker hung over an object belongs to the stay that object lives
        // in: leaving the world, by any way out, takes every plugin's markers
        // down. The layers stay, so plugins set them again on the next stay.
        _ = _uiRegistry?.WorldMarkerStore.ClearOn(_worldEvents);
```

`_worldEvents` is assigned earlier in the same constructor (~line 571); confirm that before relying on it, and move this line after that assignment if not.

- [ ] **Step 6: Run tests to verify they pass**

Run the three verify commands. Expected: 2 + 2 + 1 passed. Then `dotnet build src/AcDream.App/AcDream.App.csproj -c Release 2>&1 | grep -E "arning\(s\)|rror\(s\)"` → `0 Warning(s)`, `0 Error(s)`.

- [ ] **Step 7: Commit**

```bash
git checkout -- '*.lock.json'
git add src/AcDream.App/Plugins/BufferedUiRegistry.cs src/AcDream.Core/Plugins/ScopedPluginHost.cs \
  src/AcDream.App/Rendering/GameWindow.cs tests/AcDream.App.Tests/Plugins/BufferedUiRegistryWorldMarkersTests.cs \
  tests/AcDream.Core.Tests/Plugins/ScopedUiRegistryWorldMarkersTests.cs tests/AcDream.Headless.Tests/HeadlessPluginApiSurfaceTests.cs
git commit -m "plugins: each plugin's world markers, scoped and cleared on logoff

Claude-Session: https://claude.ai/code/session_01EtugJHhFMqGXY2V6Lz2ZFB"
```

```json:metadata
{"files": ["src/AcDream.App/Plugins/BufferedUiRegistry.cs", "src/AcDream.Core/Plugins/ScopedPluginHost.cs", "src/AcDream.App/Rendering/GameWindow.cs", "tests/AcDream.App.Tests/Plugins/BufferedUiRegistryWorldMarkersTests.cs", "tests/AcDream.Core.Tests/Plugins/ScopedUiRegistryWorldMarkersTests.cs", "tests/AcDream.Headless.Tests/HeadlessPluginApiSurfaceTests.cs"], "verifyCommand": "dotnet test tests/AcDream.App.Tests/AcDream.App.Tests.csproj --filter \"FullyQualifiedName~BufferedUiRegistryWorldMarkersTests\" && dotnet test tests/AcDream.Core.Tests/AcDream.Core.Tests.csproj --filter \"FullyQualifiedName~ScopedUiRegistryWorldMarkersTests\" && dotnet test tests/AcDream.Headless.Tests/AcDream.Headless.Tests.csproj --filter \"FullyQualifiedName~WithoutAWindowWorldMarkersHaveNoLayers\"", "acceptanceCriteria": ["WorldMarkersFor hands out the store surface by owner id; unscoped uses 'unscoped'", "scoped host asks once, keeps it, disposes it with the plugin", "headless CreateLayer is null", "GameWindow subscribes ClearOn to WorldEvents"], "modelTier": "standard"}
```

---

### Task 4: Where icons land on the screen

**Goal:** A pure layout turns icons, object anchors, positions and the camera into screen placements: an object's icons in centred rows of 8 above its labels, position icons centred on their point, cut by range and screen, faded, far to near.

**Files:**
- Create: `src/AcDream.App/World/PluginNavigationProjection.cs`
- Modify: `src/AcDream.App/Rendering/PluginWorldLineRenderer.cs:205-208`
- Create: `src/AcDream.App/UI/Layout/WorldIconOverlayController.cs` (layout part only in this task)
- Test: `tests/AcDream.App.Tests/World/PluginNavigationProjectionTests.cs`
- Test: `tests/AcDream.App.Tests/UI/Layout/WorldIconOverlayLayoutTests.cs`

**Acceptance Criteria:**
- [ ] `PluginNavigationProjection.ToWorld` gives the same metres `PluginWorldLineRenderer` computed (EW/NS ×240 plus landblock offset +84, elevation ×240), and the line renderer uses it.
- [ ] One 24-point icon over an object whose head projects to (400, 240) with no labels lands at (388, 214); with 2 label lines of 16 points at (388, 182).
- [ ] Three icons are centred with 2-point gaps (x = 362, 388, 414); a ninth icon starts a second row 26 points higher; mixed sizes are bottom-aligned in a row.
- [ ] Icons past their range are cut and the row closes up; the last fifth fades; a missing object, an object behind the camera, and an icon off screen are not placed.
- [ ] A position icon is centred on its projected point; placements are ordered far to near, then by icon index.

**Verify:** `dotnet test tests/AcDream.App.Tests/AcDream.App.Tests.csproj --filter "FullyQualifiedName~WorldIconOverlayLayoutTests|FullyQualifiedName~PluginNavigationProjectionTests|FullyQualifiedName~PluginWorldLine"` → all pass.

**Steps:**

- [ ] **Step 1: Write the failing tests**

`tests/AcDream.App.Tests/World/PluginNavigationProjectionTests.cs`:

```csharp
using System.Numerics;
using AcDream.App.World;
using AcDream.Plugin.Abstractions;

namespace AcDream.App.Tests.World;

/// <summary>
/// A plugin's navigation position in the client's world metres: map units of
/// 240 m, measured from the landblock the world is centred on.
/// </summary>
public sealed class PluginNavigationProjectionTests
{
    private static PluginNavigationPosition At(double eastWest, double northSouth, double elevation) =>
        new(0u, eastWest, northSouth, elevation, 0f, IsOutdoor: true);

    [Fact]
    public void TheOriginOfTheMapSitsInTheMiddleOfTheCentreLandblock()
    {
        Assert.Equal(new Vector3(84f, 84f, 0f), PluginNavigationProjection.ToWorld(At(0, 0, 0), 127, 127));
    }

    [Fact]
    public void OneMapUnitIsTwoHundredFortyMetresOnEveryAxis()
    {
        Assert.Equal(new Vector3(324f, -156f, 240f), PluginNavigationProjection.ToWorld(At(1, -1, 1), 127, 127));
    }

    [Fact]
    public void MovingTheCentreOneLandblockWestMovesEverythingOneLandblockEast()
    {
        Assert.Equal(new Vector3(276f, 84f, 0f), PluginNavigationProjection.ToWorld(At(0, 0, 0), 126, 127));
    }
}
```

`tests/AcDream.App.Tests/UI/Layout/WorldIconOverlayLayoutTests.cs`:

```csharp
using System.Numerics;
using AcDream.App.Interaction;
using AcDream.App.Plugins;
using AcDream.App.UI.Layout;
using AcDream.Plugin.Abstractions;

namespace AcDream.App.Tests.UI.Layout;

/// <summary>
/// Where icons land on the screen: an object's icons in centred rows of
/// eight above its head and its labels, a position's icon centred on it, the
/// range and screen cuts, the fade, and the far-to-near order.
/// </summary>
public sealed class WorldIconOverlayLayoutTests
{
    private const float LineHeight = 16f;
    private static readonly Vector2 Viewport = new(800f, 600f);

    // The camera stands at the origin looking along +Y with +Z up, so an
    // object at (0, d, 0) is d metres away, straight ahead.
    private static readonly Matrix4x4 View =
        Matrix4x4.CreateLookAt(Vector3.Zero, Vector3.UnitY, Vector3.UnitZ);

    // A 90 degree vertical field of view: a head 2 m up at 10 m away
    // projects to (400, 240) on an 800x600 screen.
    private static readonly Matrix4x4 Projection =
        Matrix4x4.CreatePerspectiveFieldOfView(MathF.PI / 2f, 4f / 3f, 0.1f, 100f);

    private static readonly PluginImage Image = new(1, 32, 32);

    private static WorldIconEntry Over(uint id, float size = 24f, float range = 60f) =>
        new("a.plugin", new PluginWorldIcon(PluginMarkerAnchor.Object(id), Image)
        {
            SizePixels = size,
            MaxRange = range,
        });

    private static WorldIconEntry AtSpot(float range = 60f) =>
        new("a.plugin", new PluginWorldIcon(
            PluginMarkerAnchor.At(new PluginNavigationPosition(1u, 0, 0, 0, 0f, true)), Image)
        {
            MaxRange = range,
        });

    private static WorldLabelAnchor? StandingAt(float distance, float x = 0f) =>
        new(new Vector3(x, distance, 0f), 2f, WorldLabelAnchorSource.PhysicsCylinder);

    private static List<WorldIconPlacement> Place(
        IReadOnlyList<WorldIconEntry> icons,
        Func<uint, WorldLabelAnchor?> anchor,
        Func<uint, int>? labelLines = null,
        Func<PluginNavigationPosition, Vector3?>? position = null)
    {
        var output = new List<WorldIconPlacement>();
        new WorldIconLayout().Place(
            icons,
            anchor,
            position ?? (_ => null),
            labelLines ?? (_ => 0),
            LineHeight,
            View,
            Projection,
            Viewport,
            output);
        return output;
    }

    [Fact]
    public void AnIconSitsCentredOverTheHeadWithItsBottomJustAboveIt()
    {
        WorldIconPlacement only = Assert.Single(Place([Over(1u)], _ => StandingAt(10f)));

        // Head at (400, 240); the row's bottom is a 2-point gap above it.
        Assert.Equal(388f, only.X, 3);
        Assert.Equal(214f, only.Y, 3);
        Assert.Equal(24f, only.Size);
        Assert.Equal(10f, only.Depth, 3);
        Assert.Equal(1f, only.Alpha);
    }

    [Fact]
    public void IconsSitAboveTheObjectsLabels()
    {
        WorldIconPlacement only = Assert.Single(Place(
            [Over(1u)], _ => StandingAt(10f), labelLines: id => id == 1u ? 2 : 0));

        Assert.Equal(240f - 2 * LineHeight - 2f - 24f, only.Y, 3);
    }

    [Fact]
    public void AnObjectsIconsAreCentredInARowWithGaps()
    {
        List<WorldIconPlacement> placed = Place([Over(1u), Over(1u), Over(1u)], _ => StandingAt(10f));

        Assert.Equal(new[] { 362f, 388f, 414f }, placed.OrderBy(p => p.IconIndex).Select(p => MathF.Round(p.X, 3)));
        Assert.All(placed, p => Assert.Equal(214f, p.Y, 3));
    }

    [Fact]
    public void TheNinthIconStartsANewRowAbove()
    {
        List<WorldIconPlacement> placed = Place(
            Enumerable.Range(0, 10).Select(_ => Over(1u)).ToArray(), _ => StandingAt(10f));

        var byIndex = placed.OrderBy(p => p.IconIndex).ToArray();
        Assert.All(byIndex.Take(8), p => Assert.Equal(214f, p.Y, 3));
        Assert.Equal(188f, byIndex[8].Y, 3);
        Assert.Equal(375f, byIndex[8].X, 3);
        Assert.Equal(401f, byIndex[9].X, 3);
    }

    [Fact]
    public void IconsOfDifferentSizesShareTheRowsBottom()
    {
        List<WorldIconPlacement> placed = Place([Over(1u, size: 16f), Over(1u, size: 32f)], _ => StandingAt(10f));

        var byIndex = placed.OrderBy(p => p.IconIndex).ToArray();
        Assert.Equal(375f, byIndex[0].X, 3);
        Assert.Equal(222f, byIndex[0].Y, 3);
        Assert.Equal(393f, byIndex[1].X, 3);
        Assert.Equal(206f, byIndex[1].Y, 3);
    }

    [Fact]
    public void AnObjectsIconsKeepTheOrderGivenEvenWhenOtherIconsComeBetween()
    {
        List<WorldIconPlacement> placed = Place(
            [Over(1u), Over(2u), Over(1u)],
            id => id == 1u ? StandingAt(10f) : StandingAt(20f));

        WorldIconPlacement first = placed.Single(p => p.IconIndex == 0);
        WorldIconPlacement third = placed.Single(p => p.IconIndex == 2);
        Assert.True(first.X < third.X);
    }

    [Fact]
    public void AnIconPastItsRangeIsCutAndTheRowClosesUp()
    {
        List<WorldIconPlacement> placed = Place([Over(1u, range: 5f), Over(1u)], _ => StandingAt(10f));

        WorldIconPlacement only = Assert.Single(placed);
        Assert.Equal(1, only.IconIndex);
        Assert.Equal(388f, only.X, 3);
    }

    [Fact]
    public void TheLastFifthOfTheRangeFades()
    {
        WorldIconPlacement only = Assert.Single(Place([Over(1u, range: 11f)], _ => StandingAt(10f)));

        // Fade starts at 8.8 m and ends at 11 m: at 10 m it is 1/2.2 solid.
        Assert.Equal(1f / 2.2f, only.Alpha, 3);
    }

    [Fact]
    public void AMissingObjectAnObjectBehindTheCameraAndAnIconOffScreenAreNotPlaced()
    {
        Assert.Empty(Place([Over(1u)], _ => null));
        Assert.Empty(Place([Over(1u)], _ => StandingAt(-10f)));
        Assert.Empty(Place([Over(1u)], _ => StandingAt(10f, x: 50f)));
    }

    [Fact]
    public void APositionsIconIsCentredOnIt()
    {
        WorldIconPlacement only = Assert.Single(Place(
            [AtSpot()], _ => null, position: _ => new Vector3(0f, 10f, 2f)));

        Assert.Equal(388f, only.X, 3);
        Assert.Equal(228f, only.Y, 3);
        Assert.Equal(10f, only.Depth, 3);
    }

    [Fact]
    public void APositionTheClientCannotPlaceIsNotPlaced()
    {
        Assert.Empty(Place([AtSpot()], _ => null, position: _ => null));
        Assert.Empty(Place([AtSpot(range: 9f)], _ => null, position: _ => new Vector3(0f, 10f, 2f)));
    }

    [Fact]
    public void PlacementsRunFarToNearThenByIndex()
    {
        List<WorldIconPlacement> placed = Place(
            [Over(1u), Over(2u), Over(2u)],
            id => id == 1u ? StandingAt(10f) : StandingAt(20f));

        Assert.Equal(new[] { 1, 2, 0 }, placed.Select(p => p.IconIndex));
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/AcDream.App.Tests/AcDream.App.Tests.csproj --filter "FullyQualifiedName~WorldIconOverlayLayoutTests|FullyQualifiedName~PluginNavigationProjectionTests"`
Expected: build FAIL, `PluginNavigationProjection` / `WorldIconLayout` / `WorldIconPlacement` not found.

- [ ] **Step 3: Move the projection** — create `src/AcDream.App/World/PluginNavigationProjection.cs`

```csharp
using System.Numerics;
using AcDream.Plugin.Abstractions;

namespace AcDream.App.World;

/// <summary>
/// A plugin's navigation position in the client's world metres. Map units
/// are 240 m; the world is measured from the landblock it is centred on, and
/// a landblock is 192 m with the map's origin 84 m into it.
/// </summary>
internal static class PluginNavigationProjection
{
    internal static Vector3 ToWorld(PluginNavigationPosition position, int centerX, int centerY) => new(
        (float)(position.EastWest * 240d + (127 - centerX) * 192d + 84d),
        (float)(position.NorthSouth * 240d + (127 - centerY) * 192d + 84d),
        (float)(position.Elevation * 240d));
}
```

In `src/AcDream.App/Rendering/PluginWorldLineRenderer.cs`, replace

```csharp
    private Vector3 Project(PluginNavigationPosition p) => new(
        (float)(p.EastWest * 240d + (127 - origin.CenterX) * 192d + 84d),
        (float)(p.NorthSouth * 240d + (127 - origin.CenterY) * 192d + 84d),
        (float)(p.Elevation * 240d));
```

with

```csharp
    private Vector3 Project(PluginNavigationPosition p) =>
        PluginNavigationProjection.ToWorld(p, origin.CenterX, origin.CenterY);
```

(`AcDream.App.World` is already imported there.)

- [ ] **Step 4: Write the layout** — create `src/AcDream.App/UI/Layout/WorldIconOverlayController.cs` with this content (Task 5 appends the element and controller to the same file):

```csharp
using System.Collections.Generic;
using System.Numerics;
using AcDream.App.Interaction;
using AcDream.App.Plugins;
using AcDream.Core.Selection;
using AcDream.Plugin.Abstractions;

namespace AcDream.App.UI.Layout;

/// <summary>
/// One icon placed on the screen: which icon it is, the square it is fitted
/// into, how far its anchor is from the camera in metres, and how faded it is.
/// </summary>
internal readonly record struct WorldIconPlacement(
    int IconIndex,
    float X,
    float Y,
    float Size,
    float Depth,
    float Alpha);

/// <summary>
/// Where each icon goes on the screen this frame. The same icons, anchors and
/// camera give the same placements, which is what the tests hold it to; the
/// instance only keeps scratch collections so a frame allocates nothing.
/// </summary>
internal sealed class WorldIconLayout
{
    /// <summary>The most icons in one row over an object.</summary>
    internal const int IconsPerRow = 8;

    /// <summary>The gap between icons, between rows, and above the labels, in interface points.</summary>
    internal const float Gap = 2f;

    private static readonly Comparison<WorldIconPlacement> FarToNear = static (left, right) =>
    {
        int byDepth = right.Depth.CompareTo(left.Depth);
        return byDepth != 0 ? byDepth : left.IconIndex.CompareTo(right.IconIndex);
    };

    private readonly HashSet<uint> _placedObjects = [];
    private readonly List<int> _row = [];

    /// <summary>
    /// Fills <paramref name="output"/> with this frame's placements, farthest
    /// first, so that where icons overlap the nearer anchor's are drawn last.
    /// </summary>
    /// <param name="icons">Every icon of every plugin, in the store's order.</param>
    /// <param name="anchor">An object's base and height by server id, or null when the client does not hold it.</param>
    /// <param name="position">A navigation position in world metres, or null when the client cannot place it yet.</param>
    /// <param name="labelLines">How many label lines hang over an object, by server id.</param>
    /// <param name="lineHeight">The label font's line height in interface points.</param>
    /// <param name="view">The camera's view matrix.</param>
    /// <param name="projection">The camera's projection matrix.</param>
    /// <param name="viewport">The size of the screen the world is drawn into.</param>
    /// <param name="output">Cleared, then filled; never reallocated.</param>
    internal void Place(
        IReadOnlyList<WorldIconEntry> icons,
        Func<uint, WorldLabelAnchor?> anchor,
        Func<PluginNavigationPosition, Vector3?> position,
        Func<uint, int> labelLines,
        float lineHeight,
        Matrix4x4 view,
        Matrix4x4 projection,
        Vector2 viewport,
        List<WorldIconPlacement> output)
    {
        output.Clear();
        _placedObjects.Clear();
        for (int index = 0; index < icons.Count; index++)
        {
            PluginMarkerAnchor at = icons[index].Icon.Anchor;
            if (at.Kind == PluginMarkerAnchorKind.Position)
            {
                PlaceAtPosition(icons, index, position, view, projection, viewport, output);
            }
            else if (at.Kind == PluginMarkerAnchorKind.Object && _placedObjects.Add(at.ObjectId))
            {
                PlaceOverObject(icons, index, anchor, labelLines, lineHeight, view, projection, viewport, output);
            }
        }

        output.Sort(FarToNear);
    }

    private static void PlaceAtPosition(
        IReadOnlyList<WorldIconEntry> icons,
        int index,
        Func<PluginNavigationPosition, Vector3?> position,
        Matrix4x4 view,
        Matrix4x4 projection,
        Vector2 viewport,
        List<WorldIconPlacement> output)
    {
        PluginWorldIcon icon = icons[index].Icon;
        if (position(icon.Anchor.Position) is not { } point
            || !TryProject(point, view, projection, viewport, out Vector2 centre, out float depth)
            || depth > icon.MaxRange)
        {
            return;
        }

        float size = icon.SizePixels;
        float x = centre.X - size * 0.5f;
        float y = centre.Y - size * 0.5f;
        if (!IsOnScreen(x, y, size, viewport))
            return;
        output.Add(new WorldIconPlacement(index, x, y, size, depth, WorldLabelLayout.Fade(depth, icon.MaxRange)));
    }

    private void PlaceOverObject(
        IReadOnlyList<WorldIconEntry> icons,
        int first,
        Func<uint, WorldLabelAnchor?> anchor,
        Func<uint, int> labelLines,
        float lineHeight,
        Matrix4x4 view,
        Matrix4x4 projection,
        Vector2 viewport,
        List<WorldIconPlacement> output)
    {
        uint objectId = icons[first].Icon.Anchor.ObjectId;
        if (anchor(objectId) is not { } at)
            return;
        Vector3 head = at.BasePosition + new Vector3(0f, 0f, at.Height);
        if (!TryProject(head, view, projection, viewport, out Vector2 centre, out float depth))
            return;

        // Every icon over this object in the order given, less the ones out
        // of range, so a row closes up rather than keeping a hole.
        _row.Clear();
        for (int index = first; index < icons.Count; index++)
        {
            PluginWorldIcon icon = icons[index].Icon;
            if (icon.Anchor.Kind == PluginMarkerAnchorKind.Object
                && icon.Anchor.ObjectId == objectId
                && depth <= icon.MaxRange)
            {
                _row.Add(index);
            }
        }

        // The first row's bottom sits a gap above the object's labels; each
        // further row sits a gap above the tallest icon of the row below.
        float rowBottom = centre.Y - Math.Max(0, labelLines(objectId)) * lineHeight - Gap;
        for (int start = 0; start < _row.Count; start += IconsPerRow)
        {
            int end = Math.Min(start + IconsPerRow, _row.Count);
            float width = Gap * (end - start - 1);
            float height = 0f;
            for (int k = start; k < end; k++)
            {
                float size = icons[_row[k]].Icon.SizePixels;
                width += size;
                height = MathF.Max(height, size);
            }

            float x = centre.X - width * 0.5f;
            for (int k = start; k < end; k++)
            {
                int index = _row[k];
                PluginWorldIcon icon = icons[index].Icon;
                float size = icon.SizePixels;
                float y = rowBottom - size;
                if (IsOnScreen(x, y, size, viewport))
                {
                    output.Add(new WorldIconPlacement(
                        index, x, y, size, depth, WorldLabelLayout.Fade(depth, icon.MaxRange)));
                }
                x += size + Gap;
            }

            rowBottom -= height + Gap;
        }
    }

    private static bool TryProject(
        Vector3 point,
        Matrix4x4 view,
        Matrix4x4 projection,
        Vector2 viewport,
        out Vector2 centre,
        out float depth)
    {
        if (!ScreenProjection.TryProjectSphereToScreenRect(
                point, 0f, view, projection, viewport,
                out Vector2 minimum, out Vector2 maximum, out depth, minSidePixels: 0f))
        {
            centre = default;
            return false;
        }

        centre = (minimum + maximum) * 0.5f;
        return true;
    }

    private static bool IsOnScreen(float x, float y, float size, Vector2 viewport) =>
        x + size >= 0f && x <= viewport.X && y + size >= 0f && y <= viewport.Y;
}
```

- [ ] **Step 5: Run tests to verify they pass**

Run the verify command. Expected: 3 projection tests, 12 layout tests and every existing `PluginWorldLine*` test pass.

- [ ] **Step 6: Commit**

```bash
git checkout -- '*.lock.json'
git add src/AcDream.App/World/PluginNavigationProjection.cs src/AcDream.App/Rendering/PluginWorldLineRenderer.cs \
  src/AcDream.App/UI/Layout/WorldIconOverlayController.cs tests/AcDream.App.Tests/World/PluginNavigationProjectionTests.cs \
  tests/AcDream.App.Tests/UI/Layout/WorldIconOverlayLayoutTests.cs
git commit -m "ui: world icon layout -- rows over objects, centred at positions

Claude-Session: https://claude.ai/code/session_01EtugJHhFMqGXY2V6Lz2ZFB"
```

```json:metadata
{"files": ["src/AcDream.App/World/PluginNavigationProjection.cs", "src/AcDream.App/Rendering/PluginWorldLineRenderer.cs", "src/AcDream.App/UI/Layout/WorldIconOverlayController.cs", "tests/AcDream.App.Tests/World/PluginNavigationProjectionTests.cs", "tests/AcDream.App.Tests/UI/Layout/WorldIconOverlayLayoutTests.cs"], "verifyCommand": "dotnet test tests/AcDream.App.Tests/AcDream.App.Tests.csproj --filter \"FullyQualifiedName~WorldIconOverlayLayoutTests|FullyQualifiedName~PluginNavigationProjectionTests|FullyQualifiedName~PluginWorldLine\"", "acceptanceCriteria": ["ToWorld matches the old line-renderer maths and the renderer uses it", "single icon at (388,214); above 2 label lines at (388,182)", "rows of 8 centred with 2pt gaps, mixed sizes bottom-aligned", "range cut closes the row, last fifth fades, missing/behind/off-screen not placed", "position icon centred; far-to-near then index order"], "modelTier": "standard"}
```

---

### Task 5: Draw the icons

**Goal:** One element draws every placement — all borders first, then all images — fitting each image into its square keeping its shape, snapped to device pixels, resolving images through the owning plugin; a controller drives it each frame on its own overlay layer.

**Files:**
- Modify: `src/AcDream.App/UI/Layout/WorldIconOverlayController.cs` (append)
- Test: `tests/AcDream.App.Tests/UI/Layout/WorldIconOverlayDrawTests.cs`

**Acceptance Criteria:**
- [ ] N bordered icons sharing one texture cost 2 sprite runs (borders on texture 0, then the image); unbordered cost 1.
- [ ] Icons whose images differ cost one run per change of texture in draw order.
- [ ] An icon whose image does not resolve is placed but draws nothing.
- [ ] `WorldIconLayerElement.Fit(24, 32, 16)` is (0, 6, 24, 12); `Fit(24, 16, 32)` is (6, 0, 12, 24); a square image fills the square.
- [ ] The controller counts label lines per object from the label set (highest `Line` + 1) and hides its layer when nothing is placed.

**Verify:** `dotnet test tests/AcDream.App.Tests/AcDream.App.Tests.csproj --filter "FullyQualifiedName~WorldIconOverlayDrawTests"` → all pass.

**Steps:**

- [ ] **Step 1: Write the failing test** — `tests/AcDream.App.Tests/UI/Layout/WorldIconOverlayDrawTests.cs`

```csharp
using System.Numerics;
using AcDream.App.Interaction;
using AcDream.App.Plugins;
using AcDream.App.Rendering;
using AcDream.App.Rendering.Gpu;
using AcDream.App.Tests.Rendering.Gpu;
using AcDream.App.UI;
using AcDream.App.UI.Layout;
using AcDream.Plugin.Abstractions;

namespace AcDream.App.Tests.UI.Layout;

/// <summary>
/// The batching the icon overlay relies on. Sprite quads join a run only
/// when their texture matches the quad drawn just before, so drawing every
/// border (all on the fill texture) and then every image keeps a set of
/// icons with one image to two runs however many there are.
/// </summary>
public sealed class WorldIconOverlayDrawTests
{
    private const uint FillTex = 0u;
    private const uint IconTex = 7u;
    private const uint OtherTex = 8u;
    private const int VerticesPerQuad = 6;

    private sealed class NullGpuFrameSource : ICurrentGpuFrameSource
    {
        public IGpuFrame? CurrentFrame => null;
    }

    private static readonly Matrix4x4 View =
        Matrix4x4.CreateLookAt(Vector3.Zero, Vector3.UnitY, Vector3.UnitZ);

    private static readonly Matrix4x4 Projection =
        Matrix4x4.CreatePerspectiveFieldOfView(MathF.PI / 2f, 4f / 3f, 0.1f, 100f);

    private static readonly Vector2 Viewport = new(800f, 600f);

    // Objects spread out in front of the camera, ids from 1, each at its own
    // distance so the far-to-near order is fixed.
    private static WorldLabelAnchor? Anchor(uint id) => id == 0u
        ? null
        : new WorldLabelAnchor(new Vector3((id % 8) - 4f, 8f + id, 0f), 1.5f, WorldLabelAnchorSource.PhysicsCylinder);

    private static WorldIconTexture? Resolve(string ownerId, PluginImage image) => image.Handle switch
    {
        1 => new WorldIconTexture(IconTex, 32, 32),
        2 => new WorldIconTexture(OtherTex, 32, 32),
        _ => null,
    };

    private static WorldIconEntry Icon(uint objectId, int imageHandle = 1, bool border = false) =>
        new("a.plugin", new PluginWorldIcon(PluginMarkerAnchor.Object(objectId), new PluginImage(imageHandle, 32, 32))
        {
            Border = border ? new PluginColor(255, 0, 0) : null,
        });

    private static IReadOnlyList<(uint Texture, int VertexCount, float Alpha)> DrawOnce(
        IReadOnlyList<WorldIconEntry> icons,
        out WorldIconOverlayController controller,
        IReadOnlyList<PluginWorldLabel>? labels = null)
    {
        var device = new RecordingGpuDevice();
        var renderer = new TextRenderer(device, new NullGpuFrameSource(), "unused");
        renderer.Begin(Viewport);
        var ctx = new UiRenderContext(renderer, Viewport);
        var root = new UiRoot { Width = Viewport.X, Height = Viewport.Y };
        UiOverlayHost host = UiOverlayHost.Mount(root);
        controller = WorldIconOverlayController.Mount(
            host,
            () => icons,
            Resolve,
            Anchor,
            _ => null,
            labels is null ? null : () => labels,
            16f,
            () => (View, Projection, Viewport));

        controller.Tick();
        root.Draw(ctx);
        return renderer.DebugSpriteSegments;
    }

    [Theory]
    [InlineData(1)]
    [InlineData(10)]
    [InlineData(40)] // object 40 stands 48 m away, inside the default 60 m range
    public void BorderedIconsWithOneImageCostTwoRunsHoweverManyThereAre(int count)
    {
        var icons = Enumerable.Range(1, count).Select(i => Icon((uint)i, border: true)).ToArray();

        var segments = DrawOnce(icons, out WorldIconOverlayController controller);

        Assert.Equal(count, controller.Element.PlacementCount);
        Assert.Equal(2, segments.Count);
        Assert.Equal(FillTex, segments[0].Texture);
        Assert.Equal(count * 4 * VerticesPerQuad, segments[0].VertexCount);
        Assert.Equal(IconTex, segments[1].Texture);
        Assert.Equal(count * VerticesPerQuad, segments[1].VertexCount);
    }

    [Fact]
    public void UnborderedIconsWithOneImageCostOneRun()
    {
        var segments = DrawOnce([Icon(1u), Icon(2u), Icon(3u)], out _);

        Assert.Equal(IconTex, Assert.Single(segments).Texture);
    }

    [Fact]
    public void EachChangeOfImageInDrawOrderStartsARun()
    {
        // Far to near is object 3, 2, 1: images 1, 2, 1 -> three runs.
        var segments = DrawOnce([Icon(1u, 1), Icon(2u, 2), Icon(3u, 1)], out _);

        Assert.Equal(new[] { IconTex, OtherTex, IconTex }, segments.Select(s => s.Texture));
    }

    [Fact]
    public void AnIconWhoseImageDoesNotResolveIsPlacedButDrawsNothing()
    {
        var segments = DrawOnce([Icon(1u, imageHandle: 99, border: true)], out WorldIconOverlayController controller);

        Assert.Equal(1, controller.Element.PlacementCount);
        Assert.Empty(segments);
    }

    [Fact]
    public void LabelLinesOverAnObjectLiftItsIcons()
    {
        DrawOnce([Icon(1u)], out WorldIconOverlayController bare);
        float bareY = bare.Placements.Single().Y;

        DrawOnce(
            [Icon(1u)],
            out WorldIconOverlayController lifted,
            labels: [new PluginWorldLabel(1u, "a", Vector4.One, Line: 0), new PluginWorldLabel(1u, "b", Vector4.One, Line: 2)]);

        Assert.Equal(bareY - 3 * 16f, lifted.Placements.Single().Y, 3);
    }

    [Fact]
    public void NothingPlacedHidesTheLayer()
    {
        DrawOnce([], out WorldIconOverlayController controller);

        Assert.False(controller.LayerVisible);
    }

    [Theory]
    [InlineData(32, 16, 0f, 6f, 24f, 12f)]
    [InlineData(16, 32, 6f, 0f, 12f, 24f)]
    [InlineData(32, 32, 0f, 0f, 24f, 24f)]
    public void AnImageIsFittedIntoItsSquareKeepingItsShape(
        int width, int height, float x, float y, float w, float h)
    {
        Assert.Equal((x, y, w, h), WorldIconLayerElement.Fit(24f, width, height));
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/AcDream.App.Tests/AcDream.App.Tests.csproj --filter "FullyQualifiedName~WorldIconOverlayDrawTests"`
Expected: build FAIL, `WorldIconOverlayController` / `WorldIconTexture` / `WorldIconLayerElement` not found.

- [ ] **Step 3: Append the element and controller** to `src/AcDream.App/UI/Layout/WorldIconOverlayController.cs`

```csharp

/// <summary>The interface texture behind an icon's image, and the image's size in texels.</summary>
internal readonly record struct WorldIconTexture(uint Texture, int Width, int Height);

/// <summary>
/// The one element that draws every icon. Sprite quads are batched into runs
/// by texture in emission order, so it draws every border first -- borders
/// are fills, all on one texture -- and then every image, in the far-to-near
/// order of the placements. A border can then sit under a nearer icon's
/// neighbour; that is the price of not paying a run per bordered icon.
/// </summary>
internal sealed class WorldIconLayerElement : UiElement
{
    private readonly Func<string, PluginImage, WorldIconTexture?> _resolve;
    private IReadOnlyList<WorldIconEntry> _icons = Array.Empty<WorldIconEntry>();
    private IReadOnlyList<WorldIconPlacement> _placements = Array.Empty<WorldIconPlacement>();

    internal WorldIconLayerElement(Func<string, PluginImage, WorldIconTexture?> resolve)
    {
        ArgumentNullException.ThrowIfNull(resolve);
        _resolve = resolve;
        ClickThrough = true;
        Anchors = AnchorEdges.None;
    }

    /// <summary>
    /// What to draw from now on. The placements list is read until the next
    /// call and never written by this element.
    /// </summary>
    internal void Present(IReadOnlyList<WorldIconEntry> icons, IReadOnlyList<WorldIconPlacement> placements)
    {
        ArgumentNullException.ThrowIfNull(icons);
        ArgumentNullException.ThrowIfNull(placements);
        _icons = icons;
        _placements = placements;
    }

    /// <summary>How many placements the element is currently drawing.</summary>
    internal int PlacementCount => _placements.Count;

    /// <summary>The placements the element is currently drawing, for tests.</summary>
    internal IReadOnlyList<WorldIconPlacement> Placements => _placements;

    /// <summary>
    /// Where an image of <paramref name="width"/> by <paramref name="height"/>
    /// texels sits inside a square of side <paramref name="size"/>: as large as
    /// fits, keeping its shape, centred.
    /// </summary>
    internal static (float X, float Y, float Width, float Height) Fit(float size, int width, int height)
    {
        float w = size, h = size;
        if (width > height)
            h = size * height / width;
        else if (height > width)
            w = size * width / height;
        return ((size - w) * 0.5f, (size - h) * 0.5f, w, h);
    }

    protected override void OnDraw(UiRenderContext ctx)
    {
        IReadOnlyList<WorldIconEntry> icons = _icons;
        IReadOnlyList<WorldIconPlacement> placements = _placements;
        float scale = ctx.PixelScale;

        // Pass 1: every border, one run on the fill texture.
        for (int i = 0; i < placements.Count; i++)
        {
            WorldIconPlacement placement = placements[i];
            WorldIconEntry entry = icons[placement.IconIndex];
            if (entry.Icon.Border is not { } border
                || !TryFit(entry, placement, scale, out _, out float x, out float y, out float w, out float h))
            {
                continue;
            }
            ctx.PushAlpha(placement.Alpha);
            ctx.DrawRectOutline(x - 1f, y - 1f, w + 2f, h + 2f, ToVector(border), 1f);
            ctx.PopAlpha();
        }

        // Pass 2: every image; consecutive icons with one image share a run.
        for (int i = 0; i < placements.Count; i++)
        {
            WorldIconPlacement placement = placements[i];
            WorldIconEntry entry = icons[placement.IconIndex];
            if (!TryFit(entry, placement, scale, out uint texture, out float x, out float y, out float w, out float h))
                continue;
            ctx.PushAlpha(placement.Alpha);
            ctx.DrawSprite(texture, x, y, w, h, 0f, 0f, 1f, 1f, ToVector(entry.Icon.Tint));
            ctx.PopAlpha();
        }
    }

    private bool TryFit(
        WorldIconEntry entry,
        WorldIconPlacement placement,
        float scale,
        out uint texture,
        out float x,
        out float y,
        out float w,
        out float h)
    {
        if (_resolve(entry.OwnerId, entry.Icon.Image) is not { Width: > 0, Height: > 0 } resolved)
        {
            texture = 0u;
            x = y = w = h = 0f;
            return false;
        }

        (float dx, float dy, w, h) = Fit(placement.Size, resolved.Width, resolved.Height);
        // Whole device pixels, so an icon does not shimmer as its anchor drifts.
        x = MathF.Round((placement.X + dx) * scale) / scale;
        y = MathF.Round((placement.Y + dy) * scale) / scale;
        texture = resolved.Texture;
        return true;
    }

    private static Vector4 ToVector(PluginColor color) =>
        new(color.R / 255f, color.G / 255f, color.B / 255f, color.A / 255f);
}

/// <summary>
/// Hangs plugin icons in the world on the shared overlay band, on a layer of
/// its own mounted before the label layer, so label text reads over icons.
///
/// <para>Each frame it counts the label lines over each object, places every
/// icon through <see cref="WorldIconLayout"/>, and hands the result to one
/// drawing element. Two placement lists alternate, as the label overlay's do,
/// so the list being drawn is never the list being written. Icons are not
/// occluded: the interface is drawn after the world.</para>
/// </summary>
internal sealed class WorldIconOverlayController
{
    private readonly UiOverlayHost _host;
    private readonly UiOverlayLayer _layer;
    private readonly WorldIconLayerElement _element;
    private readonly WorldIconLayout _layout = new();
    private readonly Func<IReadOnlyList<WorldIconEntry>> _icons;
    private readonly Func<uint, WorldLabelAnchor?> _anchor;
    private readonly Func<PluginNavigationPosition, Vector3?> _position;
    private readonly Func<IReadOnlyList<PluginWorldLabel>>? _labels;
    private readonly float _labelLineHeight;
    private readonly Func<(Matrix4x4 View, Matrix4x4 Projection, Vector2 Viewport)> _camera;
    private readonly Dictionary<uint, int> _labelLines = [];
    private readonly Func<uint, int> _labelLinesOf;
    private List<WorldIconPlacement> _front = [];
    private List<WorldIconPlacement> _back = [];

    private WorldIconOverlayController(
        UiOverlayHost host,
        UiOverlayLayer layer,
        WorldIconLayerElement element,
        Func<IReadOnlyList<WorldIconEntry>> icons,
        Func<uint, WorldLabelAnchor?> anchor,
        Func<PluginNavigationPosition, Vector3?> position,
        Func<IReadOnlyList<PluginWorldLabel>>? labels,
        float labelLineHeight,
        Func<(Matrix4x4 View, Matrix4x4 Projection, Vector2 Viewport)> camera)
    {
        _host = host;
        _layer = layer;
        _element = element;
        _icons = icons;
        _anchor = anchor;
        _position = position;
        _labels = labels;
        _labelLineHeight = labelLineHeight;
        _camera = camera;
        _labelLinesOf = id => _labelLines.GetValueOrDefault(id);
    }

    /// <summary>The element doing the drawing, for the tests that count its runs.</summary>
    internal WorldIconLayerElement Element => _element;

    /// <summary>This frame's placements, for tests.</summary>
    internal IReadOnlyList<WorldIconPlacement> Placements => _element.Placements;

    /// <summary>Whether the layer is showing, for tests.</summary>
    internal bool LayerVisible => _layer.Visible;

    internal static WorldIconOverlayController Mount(
        UiOverlayHost host,
        Func<IReadOnlyList<WorldIconEntry>> icons,
        Func<string, PluginImage, WorldIconTexture?> resolve,
        Func<uint, WorldLabelAnchor?> anchor,
        Func<PluginNavigationPosition, Vector3?> position,
        Func<IReadOnlyList<PluginWorldLabel>>? labels,
        float labelLineHeight,
        Func<(Matrix4x4 View, Matrix4x4 Projection, Vector2 Viewport)> camera)
    {
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(icons);
        ArgumentNullException.ThrowIfNull(resolve);
        ArgumentNullException.ThrowIfNull(anchor);
        ArgumentNullException.ThrowIfNull(position);
        ArgumentNullException.ThrowIfNull(camera);
        UiOverlayLayer layer = host.AddLayer("PluginWorldIconOverlay");
        var element = new WorldIconLayerElement(resolve)
        {
            Name = "PluginWorldIcons",
            Left = 0f,
            Top = 0f,
            Width = layer.Width,
            Height = layer.Height,
        };
        layer.AddChild(element);
        return new WorldIconOverlayController(
            host, layer, element, icons, anchor, position, labels, labelLineHeight, camera);
    }

    internal void Tick()
    {
        IReadOnlyList<WorldIconEntry> icons = _icons();
        var camera = _camera();
        if (icons.Count == 0 || camera.Viewport.X <= 0f || camera.Viewport.Y <= 0f)
        {
            _back.Clear();
            Present(icons);
            return;
        }

        _host.SetViewport(camera.Viewport);
        _element.Width = camera.Viewport.X;
        _element.Height = camera.Viewport.Y;
        CountLabelLines();
        _layout.Place(
            icons,
            _anchor,
            _position,
            _labelLinesOf,
            _labelLineHeight,
            camera.View,
            camera.Projection,
            camera.Viewport,
            _back);
        Present(icons);
    }

    /// <summary>How many lines of labels hang over each object: its highest line plus one.</summary>
    private void CountLabelLines()
    {
        _labelLines.Clear();
        if (_labels is null)
            return;
        IReadOnlyList<PluginWorldLabel> labels = _labels();
        for (int i = 0; i < labels.Count; i++)
        {
            PluginWorldLabel label = labels[i];
            if (label.ObjectId == 0u)
                continue;
            int lines = Math.Max(0, label.Line) + 1;
            if (lines > _labelLines.GetValueOrDefault(label.ObjectId))
                _labelLines[label.ObjectId] = lines;
        }
    }

    private void Present(IReadOnlyList<WorldIconEntry> icons)
    {
        (_front, _back) = (_back, _front);
        _element.Present(icons, _front);
        _layer.Visible = _front.Count > 0;
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test tests/AcDream.App.Tests/AcDream.App.Tests.csproj --filter "FullyQualifiedName~WorldIconOverlay"`
Expected: 10 draw tests (counting theory rows) and the 12 layout tests pass. If `UiRenderContext.DrawRectOutline` emits a different number of quads than 4 per border, correct the `count * 4` in the first test to what it emits and say so in the commit message.

- [ ] **Step 5: Commit**

```bash
git checkout -- '*.lock.json'
git add src/AcDream.App/UI/Layout/WorldIconOverlayController.cs tests/AcDream.App.Tests/UI/Layout/WorldIconOverlayDrawTests.cs
git commit -m "ui: world icon overlay -- borders then images, fitted and pixel-snapped

Claude-Session: https://claude.ai/code/session_01EtugJHhFMqGXY2V6Lz2ZFB"
```

```json:metadata
{"files": ["src/AcDream.App/UI/Layout/WorldIconOverlayController.cs", "tests/AcDream.App.Tests/UI/Layout/WorldIconOverlayDrawTests.cs"], "verifyCommand": "dotnet test tests/AcDream.App.Tests/AcDream.App.Tests.csproj --filter \"FullyQualifiedName~WorldIconOverlay\"", "acceptanceCriteria": ["bordered same-image icons cost 2 runs, unbordered 1", "each image change in draw order starts a run", "unresolved image placed but draws nothing", "Fit keeps aspect and centres", "label lines lift icons; empty hides the layer"], "modelTier": "standard"}
```

---

### Task 6: Mount the overlay in the client

**Goal:** The running client draws plugin icons: the retail UI runtime mounts the icon overlay before the label overlay, ticks it each frame, resolves images through the owning plugin's images, and places position icons through the world origin.

**Files:**
- Modify: `src/AcDream.App/UI/RetailUiRuntime.cs` (bindings record ~line 342; field ~line 390; mount list ~line 473; `Tick` ~line 734; new `MountWorldIconOverlay` beside `MountWorldLabelOverlay` ~line 1429)
- Modify: `src/AcDream.App/Composition/InteractionRetainedUiComposition.cs` (dependencies record ~line 81; bindings ~line 1031)
- Modify: `src/AcDream.App/Rendering/GameWindow.cs` (`new InteractionRetainedUiDependencies(` ~line 1349)

**Acceptance Criteria:**
- [ ] `RetailUiRuntimeBindings` has `Func<PluginNavigationPosition, Vector3?>? WorldPosition = null`; the composition fills it from `LiveWorldOriginState` (null until the origin is known).
- [ ] `RetailUiRuntime` mounts the icon overlay before the label overlay when `Plugins` and `WorldLabelAnchor` are bound, logs `[PluginUI] world icon overlay mounted.`, and ticks it before the label overlay.
- [ ] `dotnet build AcDream.slnx -c Release` has 0 warnings; the full `AcDream.App.Tests` suite passes (allowing the known-flaky classes listed in the local env notes, rerun alone).

**Verify:** `dotnet build AcDream.slnx -c Release 2>&1 | grep -E "arning\(s\)|rror\(s\)"` → `0 Warning(s)`, `0 Error(s)`; `dotnet test tests/AcDream.App.Tests/AcDream.App.Tests.csproj` → passed.

**Steps:**

- [ ] **Step 1: Add the binding** — in `RetailUiRuntimeBindings` (`src/AcDream.App/UI/RetailUiRuntime.cs`), change the last parameter line

```csharp
    PluginCanvasHostServices? PluginCanvases = null);
```

to

```csharp
    PluginCanvasHostServices? PluginCanvases = null,
    Func<PluginNavigationPosition, System.Numerics.Vector3?>? WorldPosition = null);
```

- [ ] **Step 2: Mount and tick** — in `RetailUiRuntime`:

Next to `private WorldLabelOverlayController? _worldLabelOverlay;` add:

```csharp
    private WorldIconOverlayController? _worldIconOverlay;
```

In the mount list, put `MountWorldIconOverlay();` on the line before `MountWorldLabelOverlay();`.

In `Tick`, put `_worldIconOverlay?.Tick();` on the line before `_worldLabelOverlay?.Tick();`.

Before `private void MountWorldLabelOverlay()` add:

```csharp
    private void MountWorldIconOverlay()
    {
        if (_bindings.Plugins is not { } plugins
            || _bindings.WorldLabelAnchor is not { } anchor)
        {
            return;
        }
        _worldIconOverlay = WorldIconOverlayController.Mount(
            OverlayHost,
            plugins.WorldMarkerStore.CaptureIcons,
            // An icon's image is looked up in its own plugin's images only.
            (ownerId, image) =>
                plugins.FindImages(new PluginUiOwner(ownerId, ownerId)) is { } images
                && images.TryResolve(image, out uint texture, out int width, out int height)
                    ? new WorldIconTexture(texture, width, height)
                    : null,
            anchor,
            _bindings.WorldPosition ?? (static _ => null),
            _bindings.WorldLabels,
            _bindings.Assets.DefaultFont?.LineHeight ?? 0f,
            _bindings.VividTarget.Camera);
        Console.WriteLine("[PluginUI] world icon overlay mounted.");
    }
```

- [ ] **Step 3: Compose the binding** — in `src/AcDream.App/Composition/InteractionRetainedUiComposition.cs`:

In `InteractionRetainedUiDependencies`, change

```csharp
    string? JournalDirectory = null)
```

to

```csharp
    string? JournalDirectory = null,
    AcDream.App.World.LiveWorldOriginState? WorldOrigin = null)
```

In the `RetailUiRuntimeBindings` construction, after the `WorldLabelAnchor: ...` argument (it ends `: null,` around line 1031), add:

```csharp
                WorldPosition: d.WorldOrigin is not { } worldOrigin
                    ? null
                    : position => worldOrigin.IsKnown
                        ? AcDream.App.World.PluginNavigationProjection.ToWorld(
                            position, worldOrigin.CenterX, worldOrigin.CenterY)
                        : null,
```

- [ ] **Step 4: Pass the origin** — in `src/AcDream.App/Rendering/GameWindow.cs`, in `new InteractionRetainedUiDependencies(`, change

```csharp
                    JournalDirectory: _applicationPaths.JournalDirectory),
```

to

```csharp
                    JournalDirectory: _applicationPaths.JournalDirectory,
                    WorldOrigin: _liveWorldOrigin),
```

- [ ] **Step 5: Build and run the App tests**

Run the verify commands. Expected: 0 warnings, 0 errors; App tests pass. A failure in `GraphicalPluginSessionTests.ReloadCommand…`, `LiveEntityNetworkBranchRoutingTests…GenericTail` or `GameWindowRenderLeafCompositionTests.Paperdoll…` is a known local flake: rerun that class alone before treating it as a regression.

- [ ] **Step 6: Commit**

```bash
git checkout -- '*.lock.json'
git add src/AcDream.App/UI/RetailUiRuntime.cs src/AcDream.App/Composition/InteractionRetainedUiComposition.cs \
  src/AcDream.App/Rendering/GameWindow.cs
git commit -m "ui: mount the world icon overlay under the labels

Claude-Session: https://claude.ai/code/session_01EtugJHhFMqGXY2V6Lz2ZFB"
```

```json:metadata
{"files": ["src/AcDream.App/UI/RetailUiRuntime.cs", "src/AcDream.App/Composition/InteractionRetainedUiComposition.cs", "src/AcDream.App/Rendering/GameWindow.cs"], "verifyCommand": "dotnet build AcDream.slnx -c Release 2>&1 | grep -E \"arning\\(s\\)|rror\\(s\\)\" && dotnet test tests/AcDream.App.Tests/AcDream.App.Tests.csproj", "acceptanceCriteria": ["WorldPosition binding composed from LiveWorldOriginState", "icon overlay mounted before and ticked before the label overlay, with the mount log line", "solution builds with 0 warnings; App tests pass"], "modelTier": "standard"}
```

---

### Task 7: Document it and ship a demo plugin

**Goal:** Plugin authors can read how world markers work, and a sample plugin exercises every icon feature for the real-client check.

**Files:**
- Modify: `docs/plugin-api.md` (new `## World markers` section right after `## World labels`, before `## Images`)
- Create: `samples/AcDream.Plugins.WorldMarkersDemo/AcDream.Plugins.WorldMarkersDemo.csproj`
- Create: `samples/AcDream.Plugins.WorldMarkersDemo/plugin.json`
- Create: `samples/AcDream.Plugins.WorldMarkersDemo/WorldMarkersDemoPlugin.cs`
- Create (generated by the build): `samples/AcDream.Plugins.WorldMarkersDemo/packages.neutral.lock.json`
- Modify: `AcDream.slnx` (`/samples/` folder)

**Acceptance Criteria:**
- [ ] `docs/plugin-api.md` has a "World markers" section covering the example, anchors, icon layout over objects, sizes, images, limits and drops, occlusion, and lifecycle (unload, logoff, interface teardown).
- [ ] The sample builds with 0 warnings and its output holds the dll, deps.json and plugin.json.
- [ ] An offline client run with only the demo in a scratch root logs `plugin loaded: sample.world-markers-demo` and `[PluginUI] world icon overlay mounted.`

**Verify:** `grep -cE "plugin loaded: sample.world-markers-demo|world icon overlay mounted" /tmp/openac-markers/run-smoke.log` → `2`.

**Steps:**

- [ ] **Step 1: Write the docs section** — in `docs/plugin-api.md`, insert before `## Images`:

````markdown
## World markers

```csharp
IPluginWorldMarkerLayer? layer = host.Ui.WorldMarkers.CreateLayer(); // null without a window

layer?.SetIcons(
[
    new PluginWorldIcon(PluginMarkerAnchor.Object(creatureId), images.FromSpellIcon(vulnerabilityId))
        { Border = new PluginColor(220, 60, 60) },
    new PluginWorldIcon(PluginMarkerAnchor.At(waypoint), waypointImage)
        { SizePixels = 32, MaxRange = 200f },
]);
```

`host.Ui.WorldMarkers` hangs images in the world: over an object, following
it wherever it goes, or pinned to a position. A plugin makes layers and sets
each layer's icons; a call replaces that layer's set, and the list is copied,
so it can be reused. Disposing a layer takes its icons away. How a plugin
decides what to show -- which creatures carry its debuffs, where its route
goes -- is its own business; the client only draws.

Over an object, the client lays the object's icons out itself: centred in
rows of up to eight, two points apart, above the object's head and above any
[world labels](#world-labels) on it, in the order given, with every plugin's
icons over one object sharing the rows. An icon pinned to a position is
centred on it; `PluginMarkerAnchor.At` takes the same position world lines
and navigation use. A marker over an object the client does not hold is not
drawn until the object appears.

Icons are drawn at a constant size on the screen. `SizePixels` is the side
of the square the image is fitted into, keeping its shape, in interface
points (24 by default, clamped to 8–128), so an icon is as sharp on a
high-density display as the rest of the interface. `Tint` is multiplied into
the image and its alpha is the icon's opacity; `Border` draws a thin frame.
`MaxRange` is the distance from the camera, in metres, past which the icon is
not drawn; it fades over the last fifth. The image comes from the plugin's
own [images](#images) -- a spell's icon is `images.FromSpellIcon(spellId)` --
and is looked up in that plugin's images only.

Each plugin may have at most `IPluginWorldMarkers.MaximumIcons` (256) icons
set across all its layers. A set whose entries, added to the plugin's other
layers, come to more is refused as a whole -- `SetIcons` returns false and
the layer keeps what it had. Inside an accepted set, an icon pinned to
nothing (object id zero, or a position with a coordinate that is not a
finite number), with an image that is not valid, or with a size or range
that is not a finite number (or a range that is not positive) is dropped and
the rest are shown.

Like labels, icons are not occluded: they show through walls and hills,
because the interface is drawn after the world.

Layers belong to the plugin: unloading it takes them down. When the
character leaves the world -- logging out, losing the connection,
reconnecting -- every layer's icons are cleared, because object ids mean
nothing in the next session; the layers stay usable, so the plugin sets its
icons again on the next stay. Images are dropped when the interface is torn
down (for example on a reconnect); an icon whose image is gone is not drawn
until the plugin asks for its images again and sets its icons again. Call
all of this from the tick thread, as with every other UI call.

````

- [ ] **Step 2: Create the sample project**

`samples/AcDream.Plugins.WorldMarkersDemo/AcDream.Plugins.WorldMarkersDemo.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <LangVersion>latest</LangVersion>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
  </PropertyGroup>
  <ItemGroup>
    <!-- The only acdream dependency available to an external plugin. -->
    <ProjectReference Include="..\..\src\AcDream.Plugin.Abstractions\AcDream.Plugin.Abstractions.csproj">
      <Private>false</Private>
      <ExcludeAssets>runtime</ExcludeAssets>
    </ProjectReference>
  </ItemGroup>
  <ItemGroup>
    <None Include="plugin.json" CopyToOutputDirectory="PreserveNewest" />
  </ItemGroup>
</Project>
```

`samples/AcDream.Plugins.WorldMarkersDemo/plugin.json`:

```json
{
  "id": "sample.world-markers-demo",
  "displayName": "World Markers Demo Sample",
  "version": "1.0.0",
  "entryDll": "AcDream.Plugins.WorldMarkersDemo.dll",
  "apiVersion": 1,
  "kinds": ["gameplay"],
  "hosts": ["graphical"]
}
```

`samples/AcDream.Plugins.WorldMarkersDemo/WorldMarkersDemoPlugin.cs`:

```csharp
using AcDream.Plugin.Abstractions;

namespace AcDream.Plugins.WorldMarkersDemo;

/// <summary>
/// Hangs a row of spell icons over the selected object -- or over the player
/// when nothing is selected -- and pins a larger, half-transparent icon a few
/// metres east of where the player stood when the demo first saw them. It
/// exists to be looked at, for example to check the icon overlay on a
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
    private PluginNavigationPosition? _spot;
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
    // next stay sets the icons again.
    private void OnLogoff()
    {
        _shownOver = 0u;
        _spot = null;
    }

    /// <summary>Images can only be asked for once the interface is up, and the icons follow the selection.</summary>
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
        if (_spot is null)
        {
            PluginNavigationPosition here = navigation.Position;
            _spot = here with
            {
                EastWest = here.EastWest + 5d / MetresPerMapUnit,
                Elevation = here.Elevation + 1.5d / MetresPerMapUnit,
            };
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
        icons.Add(new PluginWorldIcon(PluginMarkerAnchor.At(_spot.Value), _spotImage)
        {
            SizePixels = 40f,
            Tint = new PluginColor(255, 255, 255, 180),
            MaxRange = 120f,
        });
        layer.SetIcons(icons);
    }
}
```

- [ ] **Step 3: List it in the solution** — in `AcDream.slnx`, inside `<Folder Name="/samples/">`, after the CanvasDemo line, add:

```xml
    <Project Path="samples/AcDream.Plugins.WorldMarkersDemo/AcDream.Plugins.WorldMarkersDemo.csproj" />
```

- [ ] **Step 4: Build**

Run: `dotnet build samples/AcDream.Plugins.WorldMarkersDemo/AcDream.Plugins.WorldMarkersDemo.csproj -c Release 2>&1 | grep -E "rror\(s\)|arning\(s\)"`
Expected: `0 Warning(s)`, `0 Error(s)`; a new `samples/AcDream.Plugins.WorldMarkersDemo/packages.neutral.lock.json` exists; `ls samples/AcDream.Plugins.WorldMarkersDemo/bin/Release/net10.0/` shows the dll, `.deps.json` and `plugin.json`.

- [ ] **Step 5: Smoke run** (offline, about 15 s; a window opens and closes itself)

```bash
mkdir -p /tmp/openac-markers/root-smoke/plugins/sample.world-markers-demo
cp samples/AcDream.Plugins.WorldMarkersDemo/bin/Release/net10.0/* /tmp/openac-markers/root-smoke/plugins/sample.world-markers-demo/
dotnet build src/AcDream.App/AcDream.App.csproj -c Release 2>&1 | grep -E "rror\(s\)"
printf 'sleep 8000\nclose-client\n' > /tmp/openac-markers/smoke.probe
( export DYLD_LIBRARY_PATH=/opt/homebrew/lib VK_DRIVER_FILES=/opt/homebrew/etc/vulkan/icd.d/MoltenVK_icd.json \
    ACDREAM_ROOT_DIR=/tmp/openac-markers/root-smoke ACDREAM_DAT_DIR=$HOME/AsheronsCall \
    ACDREAM_PAK_PATH="$HOME/Library/Application Support/OpenAC/data/pak/acdream.pak" ACDREAM_NO_AUDIO=1 \
    ACDREAM_UI_PROBE_SCRIPT=/tmp/openac-markers/smoke.probe
  APP=$PWD/src/AcDream.App/bin/Release/net10.0/AcDream.App.dll
  cd /tmp/openac-markers   # the client writes a keymap folder relative to its working directory
  dotnet $APP > /tmp/openac-markers/run-smoke.log 2>&1 )
grep -E "plugin loaded: sample.world-markers-demo|world icon overlay mounted" /tmp/openac-markers/run-smoke.log
```

Expected: `plugin loaded: sample.world-markers-demo (World Markers Demo Sample)` and `[PluginUI] world icon overlay mounted.` If the mount line does not appear in an offline run because the gameplay UI never mounts before login, record that, drop it from this step's expectation, and leave the check to Task 8.

- [ ] **Step 6: Commit**

```bash
git checkout -- '*.lock.json'
git status --short   # the sample's own packages.neutral.lock.json is new, so the checkout leaves it; add it
git add docs/plugin-api.md AcDream.slnx samples/AcDream.Plugins.WorldMarkersDemo/AcDream.Plugins.WorldMarkersDemo.csproj \
  samples/AcDream.Plugins.WorldMarkersDemo/plugin.json samples/AcDream.Plugins.WorldMarkersDemo/WorldMarkersDemoPlugin.cs \
  samples/AcDream.Plugins.WorldMarkersDemo/packages.neutral.lock.json
git commit -m "docs, samples: world markers section and a demo plugin

Claude-Session: https://claude.ai/code/session_01EtugJHhFMqGXY2V6Lz2ZFB"
```

```json:metadata
{"files": ["docs/plugin-api.md", "AcDream.slnx", "samples/AcDream.Plugins.WorldMarkersDemo/AcDream.Plugins.WorldMarkersDemo.csproj", "samples/AcDream.Plugins.WorldMarkersDemo/plugin.json", "samples/AcDream.Plugins.WorldMarkersDemo/WorldMarkersDemoPlugin.cs", "samples/AcDream.Plugins.WorldMarkersDemo/packages.neutral.lock.json"], "verifyCommand": "grep -cE \"plugin loaded: sample.world-markers-demo|world icon overlay mounted\" /tmp/openac-markers/run-smoke.log", "acceptanceCriteria": ["plugin-api.md has a World markers section", "sample builds with 0 warnings, output has dll/deps.json/plugin.json", "offline run logs the plugin loaded and the overlay mounted"], "modelTier": "standard"}
```

---

### Task 8: See it in the real client

**Goal:** A screenshot from the live client, in the world, shows the demo's row of five spell icons (two framed in red, one faded) above the selected object's head and the larger faded icon pinned beside the player; the user confirms it looks right.

> **USER-ORDERED GATE — NON-SKIPPABLE.** This task was requested by the user in the current conversation. It MUST NOT be closed by walking around it, by declaring it "verified inline", or by substituting a cheaper check. Close only after every item in `acceptanceCriteria` has been re-validated independently, with output captured.

**Files:**
- None committed. Scratch only: `/tmp/openac-markers/`.

**Acceptance Criteria:**
- [ ] The client run's log shows `plugin loaded: sample.world-markers-demo` and `[PluginUI] world icon overlay mounted.`
- [ ] The saved screenshot `/tmp/openac-markers/art-world/…/world-icons*.png` shows five icons in a row above the selected object (or the player), the first two with red frames and the third faded, and a larger faded icon about 5 m east of where the player first stood.
- [ ] The user, shown the screenshot, says it looks right.

**Verify:** `ls /tmp/openac-markers/art-world/` shows the screenshot; the screenshot is shown to the user (Read it, and send it with SendUserFile).

**Steps:**

- [ ] **Step 1: Prepare a scratch root with only the demo**

```bash
mkdir -p /tmp/openac-markers/root-world/plugins/sample.world-markers-demo
cp samples/AcDream.Plugins.WorldMarkersDemo/bin/Release/net10.0/* /tmp/openac-markers/root-world/plugins/sample.world-markers-demo/
printf 'sleep 150000\nscreenshot world-icons\nsleep 1000\nclose-client\n' > /tmp/openac-markers/world.probe
```

- [ ] **Step 2: Ask the user to log in during the wait**

Tell the user: a client window will open; within 2½ minutes log in to a server, enter the world, and select a creature (or select nothing, to see the icons over your own character); stand so the selected creature and the spot a few metres east of where you entered are in view; the client takes its own screenshot at 2½ minutes and closes. Wait for the user to say they're ready before Step 3.

- [ ] **Step 3: Run**

```bash
( export DYLD_LIBRARY_PATH=/opt/homebrew/lib VK_DRIVER_FILES=/opt/homebrew/etc/vulkan/icd.d/MoltenVK_icd.json \
    ACDREAM_ROOT_DIR=/tmp/openac-markers/root-world ACDREAM_DAT_DIR=$HOME/AsheronsCall \
    ACDREAM_PAK_PATH="$HOME/Library/Application Support/OpenAC/data/pak/acdream.pak" ACDREAM_NO_AUDIO=1 \
    ACDREAM_UI_PROBE_SCRIPT=/tmp/openac-markers/world.probe ACDREAM_AUTOMATION_ARTIFACT_DIR=/tmp/openac-markers/art-world
  APP=$PWD/src/AcDream.App/bin/Release/net10.0/AcDream.App.dll
  cd /tmp/openac-markers
  dotnet $APP > /tmp/openac-markers/run-world.log 2>&1 )
grep -E "plugin loaded: sample.world-markers-demo|world icon overlay mounted" /tmp/openac-markers/run-world.log
find /tmp/openac-markers/art-world -name 'world-icons*'
```

The user's server and account are theirs: if logging in needs anything beyond the client's own login screen, ask the user rather than guessing.

- [ ] **Step 4: Look and confirm**

Read the screenshot, describe what is and isn't there against the acceptance criteria, send it to the user with SendUserFile, and ask whether it looks right. If it doesn't, debug with superpowers-extended-cc:systematic-debugging before changing code; any fix goes in as its own commit with a test.

```json:metadata
{"files": [], "verifyCommand": "find /tmp/openac-markers/art-world -name 'world-icons*'", "acceptanceCriteria": ["run log shows plugin loaded and overlay mounted", "screenshot shows the 5-icon row (2 red frames, 1 faded) over the selected object and the larger faded spot icon", "user confirms the screenshot looks right"], "modelTier": "standard", "userGate": true, "tags": ["user-gate"], "gateScope": "task", "failurePolicy": "stop-and-ask"}
```

---

## After the plan

- Whole-branch review (superpowers-extended-cc:requesting-code-review) against the spec and this plan.
- Merge `world-markers/icons` into fork `main` with a merge commit and push both, only after the user confirms (as painter v2 did).
- Then write the PR 2 (shapes) plan, stacked on `world-markers/icons`.
