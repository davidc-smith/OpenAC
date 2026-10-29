# Gear Deck Plan 3: Figure View Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers-extended-cc:subagent-driven-development (recommended) or superpowers-extended-cc:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add the Gear Deck's Figure view: the live 3D character in the Equipment section with a ring of every slot round it. Dropping on the body wears the item, the body can be lifted from and hovered like the slots, it turns when dragged and zooms with the wheel, and hovering any slot or body part highlights the pack items that fit it.

**Architecture:** The deck shares the retail paper doll's one renderer. `PaperdollFramePresenter` already builds the doll and renders it into whichever view asks for it. A new composite `PaperdollFrameViews` hands the frame to the first visible view: the retail doll viewport, or the Gear Deck's figure. That view's `PaperdollPose` (turn and zoom) is applied to the doll before it is drawn. `GearDeckGeometry` gains the figure layout: the figure rectangle, the retail click map's rectangle inside it, and a 4-column ring for the 24 `GearSlotCatalog` slots. Every existing slot code path (`EquipSlot(i)`, drawing, drops, tooltips, double-click) works unchanged on the ring. The deck turns a point on the figure into a body region through the retail `PaperdollClickMap`, which it carries as the Figure hit's `Index`.

**Tech Stack:** C# / .NET 10, xUnit, `System.Numerics`, the retained UI (`UiElement`, `UiRoot` capture), `DeckChrome`, `DeckFonts`, the private entity viewport renderer.

**Spec:** `docs/superpowers/specs/2026-10-28-gear-deck-design.md` (Delivery item 3: "3D model in the Equipment section, the slot ring, drops on the body, and hover-highlighting matching pack items"). Plans 1 and 2 are `docs/superpowers/plans/2026-10-28-gear-deck-core.md` and `docs/superpowers/plans/2026-10-29-gear-deck-query.md`. Their SDD ledgers are in the main checkout under `.superpowers/sdd/`.

**Worktree:** implement in `~/code/OpenAC/OpenAC-gear-deck` on branch `feat/gear-deck-figure`, made from `docs/gear-deck-spec`. That branch is main `170de1b7` plus this plan. The Verify commands use that path.

## Global Constraints

- **Width stays 300 points × the deck's own scale.** Every new size is a constant at scale 1, multiplied by `Scale` in `GearDeckGeometry` (the plan-2 `S(...)` helper) and by `P(...)` in the deck's drawing.
- **The retail click map is sampled 1:1 at scale 1.** The probe of the installed dats (2026-10-29) found the retail doll viewport `0x100001D5` 224×214, the drag mask `0x100001D6` 100×214 at x+51 inside it, and the click map 100×214. The map's left edge is therefore 61 points left of the viewport's centre (112 − 51). The deck's figure is narrower (140), so the body is drawn at the same pixel size and the map keeps the same offset from the figure's centre. At scale s every distance is × s, and a map pixel is `(local − mapOrigin) / s`.
- **One renderer, one visible view.** The retail viewport and the deck figure share `PaperdollViewportRenderer`. With Modern HUD on, the retail inventory is hidden, and with it off the deck is hidden, so the composite never needs both in one frame. The retail viewport is asked first.
- **Retail stays untouched.** The retail paper doll behaves exactly as before: its view always reports `PaperdollPose.Default`. The new interface members have default implementations, so the existing test doubles compile unchanged.
- The look is `DeckChrome` palette + `DeckFonts` only, never the plugin theme. Text drawn in the deck stays ASCII apart from `·`.
- **No new network messages.** Wearing goes through `GearDropDispatcher` (`DropOnSlot`, `TakeOff`) and `IGearItemActions.WieldFromPaperdoll`.
- Tests run through `~/code/OpenAC/test-baseline/run-mac-tests.sh`, narrowed with `MAC_TEST_FILTER` and never with a second `--filter`. Never run tests that are known to fail.
- Commit messages end with `Claude-Session: https://claude.ai/code/session_017hCEwifLBBM9NGyiUdMFVs`.
- Fork-only: no upstream PR, no plugin contract change.

**User decisions (already made):**
- Equipment views are Figure (3D model + slot ring, drag to turn, scroll to zoom) or Slots (the plan-1 6-column grid). Folded is one row of worn items, still drop targets.
- A drop on the figure picks the slot through `PaperdollClickMap`. Hovering a slot highlights the pack items that fit it. Double-click on a worn item takes it off.
- The deck is vertical, retail width (300 × scale), never wide.
- If the figure renderer fails to start, the Figure stud is disabled and Slots view is used, with one log line.
- Modern HUD windows look like the decks, never the plugin theme.
- Each deck scales independently (the Gear Deck's side edges set its scale).

**Plan-3 scope notes (decided while planning; flag at review if wrong):**
- **Figure is the default view**, as the retail paper doll's doll view is. A saved setting without the new `equip` key loads as Figure, so plan-1/2 users see the figure on first open after this lands. "slots" is saved once they pick Slots.
- **The ring holds all 24 slots** in 4 columns of 6 rows of 30-point cells, two columns each side of a 140 × 214 figure: armour (outer left), clothes (inner left), jewellery (inner right), then Aetheria and weapons (outer right). The order is in `GearDeckGeometry.RingCells`.
- **The click map only holds at rest.** While the figure is turned or zoomed, a point on it names no body region. Lift, hover, tooltip and double-click then do nothing there, and a drop wears the item retail's way (all its valid locations). Double-clicking the figure where nothing is worn resets it. Folding, switching to Slots and hiding the deck reset it too, so each open starts at rest.
- **Drag to turn only where nothing is worn.** A press on a worn body part lifts that item, as retail's doll does. A press anywhere else on the figure turns it.
- **A drop on the body:** if the region under the pointer fits the item, it goes through `DropOnSlot` with that region's mask. Otherwise, as retail's `HandleDollDrop` does, an auto-wear item is worn at all its valid locations, and anything else is refused.
- **Hover highlight** outlines, in green, the pack items in `Shown` that fit the hovered slot (ring, Slots view) or body region (figure). It uses the same rule as the drop highlight. Folded worn cells highlight nothing, because they are already worn items.
- **The figure is rendered at its point size**, as the retail viewport is (`(int)Width × (int)Height`). Rendering at Retina pixel density is left out, as it is in retail.
- **Retail figure-part flashing on selection** (`DollFigureLighting`) is not part of the deck.

---

## File Structure

| File | Responsibility |
|---|---|
| `src/AcDream.App/Rendering/PaperdollPose.cs` (create) | `PaperdollPose`: turn and zoom, rotation and field-of-view maths |
| `src/AcDream.App/Rendering/PaperdollFrameViews.cs` (create) | `PaperdollFrameViews` (first visible view wins) and `GearDeckPaperdollFrameView` (the deck as an `IPaperdollFrameView`) |
| `src/AcDream.App/Rendering/PaperdollFramePresenter.cs` (modify) | `IPaperdollFrameView.Pose` and `IPaperdollDollRenderer.SetPose` (default members); `Render` applies the view's pose |
| `src/AcDream.App/Rendering/PaperdollViewportRenderer.cs` (modify) | `SetPose`: the doll's rotation and the camera's field of view |
| `src/AcDream.App/UI/Hud/GearDeckSettings.cs` (modify) | `GearEquipView` + `Equip` (saved as `"equip"`) |
| `src/AcDream.App/UI/Hud/GearDeckGeometry.cs` (modify) | `Figure` input, figure + click-map rectangles, the slot ring, `GearZone.Figure`, figure height |
| `src/AcDream.App/UI/Hud/UiGearDeck.cs` (modify) | `ClickMap` binding, effective view in `LayoutInput`, pose reset on hide |
| `src/AcDream.App/UI/Hud/UiGearDeck.Figure.cs` (create) | Figure attach/detach, texture, visible size, pose, body hit, turn and zoom, fit highlight set |
| `src/AcDream.App/UI/Hud/UiGearDeck.Draw.cs` (modify) | Figure backdrop + texture, stud states, fit outlines in grid and list, `RectFor(Figure)` |
| `src/AcDream.App/UI/Hud/UiGearDeck.Input.cs` (modify) | Studs switch views; all hit tests through the deck's `HitAt`; figure lift, double-click, tooltip, turn, zoom, drop |
| `src/AcDream.App/UI/Hud/GearDropDispatcher.cs` (modify) | `DropOnFigure` and `FigureAccepts` |
| `src/AcDream.App/UI/RetailUiRuntime.cs` (modify) | Keep the loaded click map, pass it to the deck, expose `GearDeck` |
| `src/AcDream.App/Composition/LivePresentationComposition.cs` (modify) | Attach the deck's figure to the paper doll presenter through the composite view |
| `tests/AcDream.App.Tests/Rendering/PaperdollPoseTests.cs` (create) | Pose maths |
| `tests/AcDream.App.Tests/Rendering/PaperdollFrameViewsTests.cs` (create) | Composite choice, texture routing, pose to renderer |
| `tests/AcDream.App.Tests/UI/Hud/GearDeckSessionTests.cs` (modify) | `equip` round trip and default |
| `tests/AcDream.App.Tests/UI/Hud/GearDeckGeometryTests.cs` (modify) | Figure layout at scales 1 and 1.5 |
| `tests/AcDream.App.Tests/UI/Hud/UiGearDeckFigureTests.cs` (create) | Studs, body hit, lift, turn, zoom, reset, drops, highlight |
| `tests/AcDream.App.Tests/UI/Hud/GearDropDispatcherTests.cs` (modify) | `DropOnFigure` rules |
| `tests/AcDream.App.Tests/UI/Hud/UiGearDeckInputTests.cs` (modify) | The plan-1 "Figure does nothing" test now runs with no figure attached |

---

### Task 1: Equipment view setting

**Goal:** `GearDeckSettings` remembers Figure or Slots per character, with Figure as the default.

**Files:**
- Modify: `src/AcDream.App/UI/Hud/GearDeckSettings.cs`
- Test: `tests/AcDream.App.Tests/UI/Hud/GearDeckSessionTests.cs`

**Acceptance Criteria:**
- [ ] `GearEquipView { Figure, Slots }` exists; `GearDeckSettings.Equip` defaults to `Figure` (`Default.Equip == Figure`).
- [ ] `ToJson` writes `"equip": "figure"` or `"slots"`; `FromJson` reads it; a missing or unknown value reads as Figure, and `"equip"` is removed from the kept unknown keys.
- [ ] `Equals` and `GetHashCode` include `Equip`.

**Verify:** `MAC_TEST_FILTER='FullyQualifiedName~GearDeckSession' ~/code/OpenAC/test-baseline/run-mac-tests.sh ~/code/OpenAC/OpenAC-gear-deck` → all pass

**Steps:**

- [ ] **Step 1: Write the failing tests** (append to `GearDeckSessionTests`):

```csharp
[Fact]
public void Equip_DefaultsToFigure_AndRoundTrips()
{
    Assert.Equal(GearEquipView.Figure, GearDeckSettings.Default.Equip);
    Assert.Equal(GearEquipView.Figure, GearDeckSettings.FromJson("""{"version":1,"folded":false}""").Equip);
    Assert.Equal(GearEquipView.Figure, GearDeckSettings.FromJson("""{"equip":"hologram"}""").Equip);

    GearDeckSettings slots = GearDeckSettings.Default with { Equip = GearEquipView.Slots };
    string json = slots.ToJson();
    Assert.Contains("\"equip\":\"slots\"", json);
    Assert.Equal(GearEquipView.Slots, GearDeckSettings.FromJson(json).Equip);
    Assert.Contains("\"equip\":\"figure\"", GearDeckSettings.Default.ToJson());
}

[Fact]
public void Equip_TakesPartInEquality()
{
    GearDeckSettings slots = GearDeckSettings.Default with { Equip = GearEquipView.Slots };
    Assert.NotEqual(GearDeckSettings.Default, slots);
    Assert.Equal(slots, GearDeckSettings.FromJson(slots.ToJson()));
}

[Fact]
public void Equip_IsNotKeptAsAnUnknownKey()
{
    GearDeckSettings s = GearDeckSettings.FromJson("""{"equip":"slots","future":3}""");
    string json = (s with { Equip = GearEquipView.Figure }).ToJson();
    Assert.Contains("\"equip\":\"figure\"", json);
    Assert.DoesNotContain("\"equip\":\"slots\"", json);
    Assert.Contains("\"future\":3", json);
}
```

- [ ] **Step 2: Run them to see them fail** (compile error: `GearEquipView` does not exist).

- [ ] **Step 3: Implement.** In `GearDeckSettings.cs`, next to `GearPackView`:

```csharp
/// <summary>The expanded Equipment section: the 3D figure with the slot ring, or the 6-column slot grid.</summary>
internal enum GearEquipView { Figure, Slots }
```

On the record, beside `SavedFilters`:

```csharp
    /// <summary>Figure or Slots when unfolded. Figure is the default, as the retail paper doll's doll view is.</summary>
    public GearEquipView Equip { get; init; } = GearEquipView.Figure;
```

In `FromJson`, after `uint pack = ...`:

```csharp
            GearEquipView equip = (string?)o["equip"] == "slots" ? GearEquipView.Slots : GearEquipView.Figure;
```

Add `"equip"` to the removed keys (`new[] { "version", "folded", "view", "selectedPack", "equip" }`), and set `Equip = equip,` in the returned initializer. In `ToJson`, after `selectedPack`:

```csharp
        o["equip"] = Equip == GearEquipView.Slots ? "slots" : "figure";
```

`Equals` gains `&& Equip == other.Equip`; `GetHashCode` becomes `HashCode.Combine(Folded, View, SelectedPack, Equip, SavedFilters.Count)`.

A `(string?)o["equip"]` cast on a non-string node throws. The whole parse is already inside `try { } catch (Exception) { return Default; }`, so a corrupt value falls back like every other key.

- [ ] **Step 4: Run the tests** → pass.

- [ ] **Step 5: Commit**

```bash
git add src/AcDream.App/UI/Hud/GearDeckSettings.cs tests/AcDream.App.Tests/UI/Hud/GearDeckSessionTests.cs
git commit -m "Gear Deck: remember the Figure or Slots equipment view

Claude-Session: https://claude.ai/code/session_017hCEwifLBBM9NGyiUdMFVs"
```

---

### Task 2: Doll pose and the shared frame views

**Goal:** The paper doll presenter renders into whichever view is visible (retail viewport or a second view) and applies that view's turn and zoom to the doll.

**Files:**
- Create: `src/AcDream.App/Rendering/PaperdollPose.cs`
- Create: `src/AcDream.App/Rendering/PaperdollFrameViews.cs` (the composite only; Task 4 adds the deck adapter here)
- Modify: `src/AcDream.App/Rendering/PaperdollFramePresenter.cs` (interfaces + `Render`)
- Modify: `src/AcDream.App/Rendering/PaperdollViewportRenderer.cs`
- Test: `tests/AcDream.App.Tests/Rendering/PaperdollPoseTests.cs`, `tests/AcDream.App.Tests/Rendering/PaperdollFrameViewsTests.cs`

**Acceptance Criteria:**
- [ ] `PaperdollPose.Default` is (Yaw 0, Zoom 1) and `IsDefault`; `Turned` wraps the yaw into (−π, π]; `Zoomed(n)` multiplies by 1.12ⁿ and clamps to [1, 2.5], snapping within 0.001 of 1 back to exactly 1.
- [ ] `Apply(base)` returns `base` unchanged at yaw 0 and otherwise rotates it about +Z by the yaw. `Fov(base)` returns `base` at zoom 1 and otherwise `2·atan(tan(base/2)/zoom)`.
- [ ] `IPaperdollFrameView` has `PaperdollPose Pose => PaperdollPose.Default;` and `IPaperdollDollRenderer` has `void SetPose(PaperdollPose pose) { }`, both default members. The existing test doubles compile untouched.
- [ ] `PaperdollFramePresenter.Render` calls `_renderer.SetPose(_view.Pose)` after a visible size is found and before `Render(width, height)`.
- [ ] `PaperdollFrameViews` uses the first view whose `TryGetVisibleSize` is true. It publishes the texture to that view only, reports that view's `Pose` (Default when none is visible), and clears every view on `ClearTextureHandle`.
- [ ] `PaperdollViewportRenderer.SetPose` sets the doll's `Rotation` to `pose.Apply(baseRotation)`, where the base is the rotation the doll had when `SetDoll` received it, and sets the camera's `FovRadians` to `pose.Fov(π/4)`. A new doll keeps the current pose.

**Verify:** `MAC_TEST_FILTER='FullyQualifiedName~PaperdollPose|FullyQualifiedName~PaperdollFrameViews|FullyQualifiedName~PaperdollFramePresenter' ~/code/OpenAC/test-baseline/run-mac-tests.sh ~/code/OpenAC/OpenAC-gear-deck` → all pass

**Steps:**

- [ ] **Step 1: Write the failing tests.**

`tests/AcDream.App.Tests/Rendering/PaperdollPoseTests.cs`:

```csharp
using System.Numerics;
using AcDream.App.Rendering;

namespace AcDream.App.Tests.Rendering;

public sealed class PaperdollPoseTests
{
    [Fact]
    public void Default_IsAtRest()
    {
        Assert.True(PaperdollPose.Default.IsDefault);
        Assert.Equal(0f, PaperdollPose.Default.Yaw);
        Assert.Equal(1f, PaperdollPose.Default.Zoom);
        Assert.False(PaperdollPose.Default.Turned(0.2f).IsDefault);
        Assert.False(PaperdollPose.Default.Zoomed(1).IsDefault);
    }

    [Fact]
    public void Turned_WrapsIntoOneTurn()
    {
        PaperdollPose p = PaperdollPose.Default.Turned(MathF.PI * 3f);
        Assert.InRange(p.Yaw, -MathF.PI, MathF.PI + 1e-5f);
        Assert.Equal(MathF.PI, MathF.Abs(p.Yaw), 3);
    }

    [Fact]
    public void Zoomed_StepsAndClamps_AndSnapsBackToOne()
    {
        Assert.Equal(1.12f, PaperdollPose.Default.Zoomed(1).Zoom, 4);
        Assert.Equal(PaperdollPose.MaxZoom, PaperdollPose.Default.Zoomed(40).Zoom);
        Assert.Equal(1f, PaperdollPose.Default.Zoomed(-3).Zoom);
        Assert.Equal(1f, PaperdollPose.Default.Zoomed(3).Zoomed(-3).Zoom);
        Assert.True(PaperdollPose.Default.Zoomed(3).Zoomed(-3).IsDefault);
    }

    [Fact]
    public void Apply_RotatesAboutUp()
    {
        Quaternion baseRotation = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, 0.5f);
        Assert.Equal(baseRotation, PaperdollPose.Default.Apply(baseRotation));
        Quaternion turned = PaperdollPose.Default.Turned(0.25f).Apply(baseRotation);
        Vector3 x = Vector3.Transform(Vector3.UnitX, turned);
        Assert.Equal(MathF.Cos(0.75f), x.X, 4);
        Assert.Equal(MathF.Sin(0.75f), x.Y, 4);
        Assert.Equal(0f, x.Z, 4);
    }

    [Fact]
    public void Fov_NarrowsWithZoom()
    {
        float baseFov = MathF.PI / 4f;
        Assert.Equal(baseFov, PaperdollPose.Default.Fov(baseFov));
        PaperdollPose twice = new(0f, 2f);
        Assert.Equal(MathF.Tan(baseFov / 2f) / 2f, MathF.Tan(twice.Fov(baseFov) / 2f), 5);
    }
}
```

`tests/AcDream.App.Tests/Rendering/PaperdollFrameViewsTests.cs`:

```csharp
using AcDream.App.Rendering;
using AcDream.Core.World;

namespace AcDream.App.Tests.Rendering;

public sealed class PaperdollFrameViewsTests
{
    private sealed class View : IPaperdollFrameView
    {
        public bool Visible;
        public int W = 10, H = 20;
        public PaperdollPose Pose { get; set; } = PaperdollPose.Default;
        public readonly List<uint> Published = [];
        public int Cleared;
        public bool TryGetVisibleSize(out int width, out int height) { width = W; height = H; return Visible; }
        public void SetTextureHandle(uint textureHandle) => Published.Add(textureHandle);
        public void ClearTextureHandle() => Cleared++;
    }

    private sealed class Renderer : IPaperdollDollRenderer
    {
        public readonly List<string> Calls = [];
        public void SetHeritage(uint heritageId) { }
        public void SetDoll(WorldEntity? doll) { }
        public void Prepare() { }
        public void SetPose(PaperdollPose pose) => Calls.Add($"pose {pose.Yaw:0.00} {pose.Zoom:0.00}");
        public uint Render(int width, int height) { Calls.Add($"render {width}x{height}"); return 7u; }
    }

    private sealed class Factory : IPaperdollDollFactory
    {
        public bool TryBuild(uint heritageId, out WorldEntity? doll) { doll = null; return false; }
    }

    private sealed class Heritage : IPaperdollHeritageSource
    {
        public uint HeritageGroup => 1u;
    }

    [Fact]
    public void FirstVisibleView_GetsTheFrame()
    {
        var retail = new View { Visible = false };
        var deck = new View { Visible = true, W = 140, H = 214 };
        var views = new PaperdollFrameViews(retail, deck);

        Assert.True(views.TryGetVisibleSize(out int w, out int h));
        Assert.Equal((140, 214), (w, h));
        views.SetTextureHandle(9u);
        Assert.Empty(retail.Published);
        Assert.Equal([9u], deck.Published);

        retail.Visible = true;
        Assert.True(views.TryGetVisibleSize(out w, out _));
        Assert.Equal(10, w);
        views.SetTextureHandle(4u);
        Assert.Equal([4u], retail.Published);
    }

    [Fact]
    public void NoVisibleView_PublishesNothing_AndPoseIsDefault()
    {
        var a = new View { Pose = new PaperdollPose(1f, 2f) };
        var views = new PaperdollFrameViews(a);
        Assert.False(views.TryGetVisibleSize(out _, out _));
        views.SetTextureHandle(3u);
        Assert.Empty(a.Published);
        Assert.Equal(PaperdollPose.Default, views.Pose);
    }

    [Fact]
    public void Pose_FollowsTheChosenView_AndClearReachesEveryView()
    {
        var a = new View { Visible = false };
        var b = new View { Visible = true, Pose = new PaperdollPose(0.5f, 1.5f) };
        var views = new PaperdollFrameViews(a, b);
        views.TryGetVisibleSize(out _, out _);
        Assert.Equal(b.Pose, views.Pose);
        views.ClearTextureHandle();
        Assert.Equal((1, 1), (a.Cleared, b.Cleared));
    }

    [Fact]
    public void Presenter_AppliesTheViewPose_BeforeRendering()
    {
        var renderer = new Renderer();
        var view = new View { Visible = true, W = 140, H = 214, Pose = new PaperdollPose(0.5f, 1.5f) };
        var presenter = new PaperdollFramePresenter(renderer, view, new Factory(), new Heritage());
        presenter.Render();
        Assert.Equal(["pose 0.50 1.50", "render 140x214"], renderer.Calls);
    }
}
```

- [ ] **Step 2: Run them to see them fail** (compile errors).

- [ ] **Step 3: Implement `PaperdollPose`** (`src/AcDream.App/Rendering/PaperdollPose.cs`):

```csharp
using System.Numerics;

namespace AcDream.App.Rendering;

/// <summary>
/// How a doll view is turned and zoomed: a yaw about the doll's up axis
/// (radians, added to the pose the doll was built with) and a zoom that
/// narrows the camera's field of view. The retail paper doll is always at
/// <see cref="Default"/>; the Gear Deck's figure turns when dragged and zooms
/// with the wheel.
/// </summary>
internal readonly record struct PaperdollPose(float Yaw, float Zoom)
{
    public const float MinZoom = 1f, MaxZoom = 2.5f, ZoomStep = 1.12f;

    public static PaperdollPose Default => new(0f, 1f);

    /// <summary>Unturned and unzoomed: the only pose the retail click map matches.</summary>
    public bool IsDefault => Yaw == 0f && Zoom == 1f;

    /// <summary>Turned by <paramref name="radians"/> more, kept within one turn.</summary>
    public PaperdollPose Turned(float radians)
    {
        float yaw = (Yaw + radians) % MathF.Tau;
        if (yaw > MathF.PI) yaw -= MathF.Tau;
        else if (yaw <= -MathF.PI) yaw += MathF.Tau;
        return this with { Yaw = yaw };
    }

    /// <summary>Zoomed by <paramref name="notches"/> wheel steps (in is positive), within
    /// [<see cref="MinZoom"/>, <see cref="MaxZoom"/>]; a zoom back to within 0.001 of 1 is 1.</summary>
    public PaperdollPose Zoomed(int notches)
    {
        float zoom = Math.Clamp(Zoom * MathF.Pow(ZoomStep, notches), MinZoom, MaxZoom);
        if (MathF.Abs(zoom - 1f) < 1e-3f) zoom = 1f;
        return this with { Zoom = zoom };
    }

    /// <summary>The doll's rotation: <paramref name="baseRotation"/> turned about +Z by the yaw.</summary>
    public Quaternion Apply(Quaternion baseRotation) => Yaw == 0f
        ? baseRotation
        : Quaternion.Concatenate(baseRotation, Quaternion.CreateFromAxisAngle(Vector3.UnitZ, Yaw));

    /// <summary>The vertical field of view that makes the doll <see cref="Zoom"/> times as tall.</summary>
    public float Fov(float baseFov) => Zoom == 1f
        ? baseFov
        : 2f * MathF.Atan(MathF.Tan(baseFov / 2f) / Zoom);
}
```

- [ ] **Step 4: Add the default interface members** in `PaperdollFramePresenter.cs`:

```csharp
internal interface IPaperdollDollRenderer
{
    void SetHeritage(uint heritageId);

    void SetDoll(WorldEntity? doll);

    void Prepare();

    /// <summary>Turns and zooms the doll for the view about to be drawn.</summary>
    void SetPose(PaperdollPose pose) { }

    uint Render(int width, int height);
}

internal interface IPaperdollFrameView
{
    bool TryGetVisibleSize(out int width, out int height);

    void SetTextureHandle(uint textureHandle);

    void ClearTextureHandle();

    /// <summary>How this view wants the doll turned and zoomed; the retail viewport never moves it.</summary>
    PaperdollPose Pose => PaperdollPose.Default;
}
```

In `PaperdollFramePresenter.Render`, after the `TryGetVisibleSize` early return:

```csharp
        _renderer.SetPose(_view.Pose);
```

- [ ] **Step 5: Implement the composite** (`src/AcDream.App/Rendering/PaperdollFrameViews.cs`):

```csharp
namespace AcDream.App.Rendering;

/// <summary>
/// Several places the one paper doll can be shown (the retail inventory's
/// viewport, the Gear Deck's figure). Each frame the first visible one gets
/// the render, at its size and pose; the others keep whatever they last
/// showed, which nobody sees while they are hidden.
/// </summary>
internal sealed class PaperdollFrameViews : IPaperdollFrameView
{
    private readonly IPaperdollFrameView[] _views;
    private IPaperdollFrameView? _active;

    public PaperdollFrameViews(params IPaperdollFrameView[] views)
    {
        ArgumentNullException.ThrowIfNull(views);
        _views = views;
    }

    public PaperdollPose Pose => _active?.Pose ?? PaperdollPose.Default;

    public bool TryGetVisibleSize(out int width, out int height)
    {
        foreach (IPaperdollFrameView view in _views)
        {
            if (view.TryGetVisibleSize(out width, out height))
            {
                _active = view;
                return true;
            }
        }
        _active = null;
        width = 0;
        height = 0;
        return false;
    }

    public void SetTextureHandle(uint textureHandle) => _active?.SetTextureHandle(textureHandle);

    public void ClearTextureHandle()
    {
        foreach (IPaperdollFrameView view in _views) view.ClearTextureHandle();
    }
}
```

- [ ] **Step 6: Implement `SetPose` in `PaperdollViewportRenderer`.** Add fields and replace `SetDoll`:

```csharp
    /// <summary>The camera's field of view at zoom 1 (<see cref="DollCamera"/>'s default).</summary>
    private const float BaseFov = MathF.PI / 4f;

    private WorldEntity? _doll;
    private System.Numerics.Quaternion _dollBaseRotation = System.Numerics.Quaternion.Identity;
    private PaperdollPose _pose = PaperdollPose.Default;

    public void SetDoll(WorldEntity? doll)
    {
        _doll = doll;
        if (doll is not null) _dollBaseRotation = doll.Rotation;
        ApplyPose();
        _renderer.SetEntity(doll);
    }

    public void SetPose(PaperdollPose pose)
    {
        if (pose == _pose) return;
        _pose = pose;
        ApplyPose();
    }

    /// <summary>The doll's rotation is read every draw (the private renderer's snapshot keys on meshes only),
    /// so turning the entity in place is enough.</summary>
    private void ApplyPose()
    {
        if (_doll is not null) _doll.Rotation = _pose.Apply(_dollBaseRotation);
        _camera.FovRadians = _pose.Fov(BaseFov);
    }
```

Check that claim before relying on it. `PrivateEntityViewportRenderer.EntitySlot.MeshSnapshot.Matches` compares mesh ids only, and the draw path builds each entity's world transform from `WorldEntity.Rotation` when it draws (`WorldEntity.cs` around line 103). If the transform turns out to be cached, say so in the report and re-`SetEntity` the doll after a pose change instead.

- [ ] **Step 7: Run the tests** → pass, including the existing `PaperdollFramePresenterTests`.

- [ ] **Step 8: Commit**

```bash
git add src/AcDream.App/Rendering/PaperdollPose.cs src/AcDream.App/Rendering/PaperdollFrameViews.cs \
  src/AcDream.App/Rendering/PaperdollFramePresenter.cs src/AcDream.App/Rendering/PaperdollViewportRenderer.cs \
  tests/AcDream.App.Tests/Rendering/PaperdollPoseTests.cs tests/AcDream.App.Tests/Rendering/PaperdollFrameViewsTests.cs
git commit -m "Paper doll: a pose per view, and one doll shared by several views

Claude-Session: https://claude.ai/code/session_017hCEwifLBBM9NGyiUdMFVs"
```

---

### Task 3: Figure layout in GearDeckGeometry

**Goal:** `GearDeckGeometry` lays out the Figure view: a 140 × 214 figure centred in the Equipment section, the retail click map's rectangle inside it, and the 24 slots in a 4 × 6 ring round it, all at the deck's scale.

**Files:**
- Modify: `src/AcDream.App/UI/Hud/GearDeckGeometry.cs`
- Test: `tests/AcDream.App.Tests/UI/Hud/GearDeckGeometryTests.cs`

**Acceptance Criteria:**
- [ ] `GearDeckLayoutInput` gains `bool Figure = false` as its last parameter. Existing callers compile unchanged.
- [ ] The constants are `FigureW = 140`, `FigureH = 214`, `RingCell = 30`, `RingGap = 4`, `RingSideGap = 6`, `MapW = 100`, `MapH = 214`, `MapLeftOfCentre = 61`, and `RingRows = 6`.
- [ ] Unfolded with `Figure`, `Equipment.H == FigureH × scale`. `MinHeightFor` uses that height, so it is exactly 50 × scale more than Slots view (214 − 164), unless the 360 minimum governs.
- [ ] `Figure` is `FigureW × FigureH` (× scale), centred horizontally in `Equipment` at its top. With no figure shown (Slots view or folded) it is a zero-width rectangle at `Equipment`'s origin.
- [ ] `ClickMap` is `MapW × MapH` (× scale), at `Figure.X + Figure.W / 2 − MapLeftOfCentre × scale`, `Figure.Y`.
- [ ] In Figure view `EquipSlot(i)` returns slot i's ring cell from `RingCells`. The ring cells are 30 × scale squares, none overlapping each other or `Figure`, all inside `Equipment`, with the 6 rows centred vertically.
- [ ] `HitAt` returns `EquipSlot(i)` for a ring cell (i = the `GearSlotCatalog` index) and `GearZone.Figure` (Index −1; the deck fills in the region) inside `Figure`.
- [ ] Slots view and folded geometry are unchanged; the plan-1/2 geometry tests still pass.

**Verify:** `MAC_TEST_FILTER='FullyQualifiedName~GearDeckGeometry' ~/code/OpenAC/test-baseline/run-mac-tests.sh ~/code/OpenAC/OpenAC-gear-deck` → all pass

**Steps:**

- [ ] **Step 1: Write the failing tests** (append to `GearDeckGeometryTests`; adjust the `using`s if the file lacks `AcDream.Core.Items.Gear`):

```csharp
private static GearDeckLayoutInput FigureInput(bool folded = false) =>
    new(folded, List: false, WornCount: 2, ItemCount: 30, SidePackSlots: 7, ScrollRow: 0, Figure: true);

[Theory]
[InlineData(1f)]
[InlineData(1.5f)]
public void Figure_IsCentred_AndTheRingSurroundsIt(float scale)
{
    var g = GearDeckGeometry.Create(GearDeckGeometry.DefaultHeightAt(scale) + 100f * scale, FigureInput(), null, scale);
    Assert.Equal(GearDeckGeometry.FigureH * scale, g.Equipment.H, 3);
    Assert.Equal(GearDeckGeometry.FigureW * scale, g.Figure.W, 3);
    Assert.Equal(GearDeckGeometry.FigureH * scale, g.Figure.H, 3);
    Assert.Equal(g.Equipment.X + (g.Equipment.W - g.Figure.W) / 2f, g.Figure.X, 3);
    Assert.Equal(g.Equipment.Y, g.Figure.Y, 3);

    Assert.Equal(g.Figure.X + g.Figure.W / 2f - GearDeckGeometry.MapLeftOfCentre * scale, g.ClickMap.X, 3);
    Assert.Equal(g.Figure.Y, g.ClickMap.Y, 3);
    Assert.Equal(GearDeckGeometry.MapW * scale, g.ClickMap.W, 3);
    Assert.Equal(GearDeckGeometry.MapH * scale, g.ClickMap.H, 3);

    var cells = Enumerable.Range(0, GearSlotCatalog.Slots.Count).Select(g.EquipSlot).ToList();
    foreach (DeckRect c in cells)
    {
        Assert.Equal(GearDeckGeometry.RingCell * scale, c.W, 3);
        Assert.True(c.X >= g.Equipment.X - 0.01f && c.X + c.W <= g.Equipment.X + g.Equipment.W + 0.01f);
        Assert.True(c.Y >= g.Equipment.Y - 0.01f && c.Y + c.H <= g.Equipment.Y + g.Equipment.H + 0.01f);
        Assert.False(Overlaps(c, g.Figure));
    }
    for (int i = 0; i < cells.Count; i++)
        for (int j = i + 1; j < cells.Count; j++)
            Assert.False(Overlaps(cells[i], cells[j]), $"ring cells {i} and {j} overlap");

    float top = cells.Min(c => c.Y), bottom = cells.Max(c => c.Y + c.H);
    Assert.Equal(g.Equipment.Y + g.Equipment.H - bottom, top - g.Equipment.Y, 3);
}

private static bool Overlaps(DeckRect a, DeckRect b) =>
    a.X < b.X + b.W - 0.01f && b.X < a.X + a.W - 0.01f && a.Y < b.Y + b.H - 0.01f && b.Y < a.Y + a.H - 0.01f;

[Fact]
public void Figure_HitsRingSlotsByCatalogIndex_AndTheFigure()
{
    var g = GearDeckGeometry.Create(800f, FigureInput());
    for (int i = 0; i < GearSlotCatalog.Slots.Count; i++)
    {
        DeckRect c = g.EquipSlot(i);
        Assert.Equal(new GearDeckHit(GearZone.EquipSlot, i), g.HitAt(c.X + c.W / 2f, c.Y + c.H / 2f));
    }
    Assert.Equal(new GearDeckHit(GearZone.Figure), g.HitAt(g.Figure.X + g.Figure.W / 2f, g.Figure.Y + 20f));
}

[Fact]
public void Figure_TheHeadSitsTopLeft_AndTheWeaponOuterRight()
{
    var g = GearDeckGeometry.Create(800f, FigureInput());
    int head = Index(EquipMask.HeadWear), weapon = Index(GearSlotCatalog.Weapon);
    Assert.Equal(g.Equipment.X, g.EquipSlot(head).X, 3);
    Assert.True(g.EquipSlot(weapon).X > g.Figure.X + g.Figure.W);
    Assert.True(g.EquipSlot(head).Y < g.EquipSlot(Index(EquipMask.FootWear)).Y);
}

private static int Index(EquipMask mask)
{
    for (int i = 0; i < GearSlotCatalog.Slots.Count; i++)
        if (GearSlotCatalog.Slots[i].Mask == mask) return i;
    throw new InvalidOperationException();
}

[Fact]
public void Figure_AddsFiftyPointsToTheMinimum_AndFoldedOrSlotsHaveNoFigure()
{
    var slots = new GearDeckLayoutInput(false, false, 2, 30, 7, 0);
    float baseMin = GearDeckGeometry.MinHeightFor(slots);
    Assert.Equal(baseMin + 50f, GearDeckGeometry.MinHeightFor(slots with { Figure = true }), 3);

    Assert.Equal(0f, GearDeckGeometry.Create(800f, slots).Figure.W);
    var folded = GearDeckGeometry.Create(800f, FigureInput(folded: true));
    Assert.Equal(0f, folded.Figure.W);
    Assert.Equal(GearDeckGeometry.WornCellSize, folded.Equipment.H, 3);
}
```

The 7 side-pack slots make the side column, not the 360 floor, decide the minimum in both cases, so the difference is exactly the 50 points. If the existing tests use a different literal pattern for the input, keep this one self-contained as written.

- [ ] **Step 2: Run them to see them fail.**

- [ ] **Step 3: Implement.** In `GearDeckGeometry.cs`:

1. `GearDeckLayoutInput` gains `bool Figure = false` after `SalvageButton`.
2. `GearZone` gains `Figure` at the end.
3. Constants (beside the slot constants):

```csharp
    public const float FigureW = 140f, FigureH = 214f, RingCell = 30f, RingGap = 4f, RingSideGap = 6f;
    /// <summary>The retail click map (the doll mask 0x100001D6): 100 × 214, its left edge 61 points left of
    /// the doll viewport's centre (the 224-wide viewport 0x100001D5 holds it at x+51).</summary>
    public const float MapW = 100f, MapH = 214f, MapLeftOfCentre = 61f;
    public const int RingRows = 6;

    /// <summary>Each <see cref="AcDream.Core.Items.Gear.GearSlotCatalog.Slots"/> entry's ring cell in Figure view
    /// (column 0-3 left to right, row 0-5 top down): armour outer left, clothes inner left, jewellery inner
    /// right, Aetheria and weapons outer right; each column runs head to foot.</summary>
    internal static readonly (int Col, int Row)[] RingCells =
    [
        (0, 0), (2, 0), (1, 0),            // Head, Neck, Cloak
        (3, 0), (3, 1), (3, 2),            // Blue, Yellow, Red
        (0, 1), (0, 2), (0, 3), (0, 4),    // Chest, Abdomen, Upper arm, Lower arm
        (1, 1), (1, 2), (1, 3), (1, 4),    // Shirt, Pants, Upper leg, Lower leg
        (0, 5), (1, 5),                    // Hands, Feet
        (2, 1), (2, 2), (2, 3), (2, 4),    // L wrist, R wrist, L ring, R ring
        (3, 3), (3, 4), (3, 5), (2, 5),    // Weapon, Shield, Ammo, Trinket
    ];
```

Add a static check beside it in the test file, so a reorder of `GearSlotCatalog` is caught: assert `RingCells.Length == GearSlotCatalog.Slots.Count` and that the 24 (col, row) pairs are distinct.

4. Properties: `public required DeckRect Figure { get; init; }` and `public required DeckRect ClickMap { get; init; }`.
5. `EquipmentHeight`:

```csharp
        if (!input.Folded) return input.Figure
            ? FigureH * scale
            : SlotRows * SlotCell * scale + (SlotRows - 1) * SlotGap * scale;
```

6. In `Create`, after `equipment` is made:

```csharp
        bool figureShown = input.Figure && !input.Folded;
        var figure = figureShown
            ? new DeckRect(equipment.X + (equipment.W - S(FigureW)) / 2f, equipment.Y, S(FigureW), S(FigureH))
            : new DeckRect(equipment.X, equipment.Y, 0f, 0f);
        var clickMap = new DeckRect(figure.X + figure.W / 2f - S(MapLeftOfCentre), figure.Y, S(MapW), S(MapH));
```

and set `Figure = figure, ClickMap = clickMap,` in the initializer.

7. `EquipSlot`:

```csharp
    public DeckRect EquipSlot(int index)
    {
        if (Input.Figure && !Input.Folded) return RingSlot(index);
        ...the plan-1 body unchanged...
    }

    /// <summary>A slot's ring cell: two columns each side of <see cref="Figure"/>, six rows centred on it.</summary>
    private DeckRect RingSlot(int index)
    {
        (int col, int row) = RingCells[index];
        float cell = S(RingCell), gap = S(RingGap), side = S(RingSideGap);
        float x = col switch
        {
            0 => Figure.X - side - cell - gap - cell,
            1 => Figure.X - side - cell,
            2 => Figure.X + Figure.W + side,
            _ => Figure.X + Figure.W + side + cell + gap,
        };
        float ringH = RingRows * cell + (RingRows - 1) * gap;
        float y = Equipment.Y + (Equipment.H - ringH) / 2f + row * (cell + gap);
        return new DeckRect(x, y, cell, cell);
    }
```

At scale 1 the left edge of column 0 is 80 − 6 − 64 = 10 = `Pad`, which is `Equipment.X`, so the ring spans the full 280.

8. `HitAt`, in the unfolded branch after the slot loop:

```csharp
                if (Input.Figure && Figure.Contains(x, y)) return new GearDeckHit(GearZone.Figure);
```

The slot loop already runs `Columns * SlotRows` = 24 indices, which is `GearSlotCatalog.Slots.Count`, so the ring is hit-tested by the same loop.

- [ ] **Step 4: Run the tests** → pass (old and new).

- [ ] **Step 5: Commit**

```bash
git add src/AcDream.App/UI/Hud/GearDeckGeometry.cs tests/AcDream.App.Tests/UI/Hud/GearDeckGeometryTests.cs
git commit -m "Gear Deck: figure layout with the slot ring and the click map's place

Claude-Session: https://claude.ai/code/session_017hCEwifLBBM9NGyiUdMFVs"
```

---

### Task 4: The deck hosts the figure

**Goal:** The deck can have a figure attached. It shows Figure or Slots from its setting and the studs, draws the doll texture it is handed, and reports its figure size to the shared paper doll through a frame-view adapter. Without an attached figure it stays in Slots view with one log line.

**Files:**
- Create: `src/AcDream.App/UI/Hud/UiGearDeck.Figure.cs`
- Modify: `src/AcDream.App/UI/Hud/UiGearDeck.cs` (`ClickMap` binding, `LayoutInput`, `OnShown` / `OnHidden`)
- Modify: `src/AcDream.App/UI/Hud/UiGearDeck.Draw.cs` (studs, figure, `RectFor`)
- Modify: `src/AcDream.App/UI/Hud/UiGearDeck.Input.cs` (`FigureStud`, `SlotsStud`, `FoldStud` clicks)
- Modify: `src/AcDream.App/Rendering/PaperdollFrameViews.cs` (add `GearDeckPaperdollFrameView`)
- Create: `tests/AcDream.App.Tests/UI/Hud/UiGearDeckFigureTests.cs`
- Modify: `tests/AcDream.App.Tests/UI/Hud/UiGearDeckInputTests.cs` (`TheViewStuds_SetTheirSettings_AndFigureDoesNothing`)

**Acceptance Criteria:**
- [ ] `GearDeckBindings` gains `PaperdollClickMap? ClickMap = null` as its last parameter.
- [ ] `UiGearDeck.AttachFigure()` / `DetachFigure()` set `FigureAttached`. Detaching clears the texture and resets the pose.
- [ ] `LayoutInput().Figure` is `Settings.Equip == Figure && FigureAttached`. That is the effective view: geometry, drawing and hits all follow it.
- [ ] The Figure stud is lit in Figure view, dim and inert when no figure is attached, and otherwise unlit. Clicking it sets `Equip = Figure` and `Folded = false`. Clicking Slots sets `Equip = Slots` and `Folded = false`. Both keep the scroll in range (`ScrollTo(_scrollRow)`), as plan 1's fold does.
- [ ] `TryGetFigureSize(out w, out h)` is true only when the deck and every ancestor are visible, the player is known (`_model.Packs.Count > 0`), and the figure is shown (unfolded, effective Figure). The size is `(int)MathF.Round` of `Figure.W` / `Figure.H`.
- [ ] `SetFigureTexture(GpuTextureSlot)` stores the slot (`FigureTexture`). The figure draws a `SlotFill` rounded backdrop, then the texture over `Figure` when the slot is assigned and resolves to a handle.
- [ ] `GearDeckPaperdollFrameView(deck)` maps `TryGetVisibleSize` → `TryGetFigureSize`, `SetTextureHandle(h)` → `SetFigureTexture(UiTextureTableHandle.ToSlot(h))`, `ClearTextureHandle` → `SetFigureTexture(GpuTextureSlot.Unassigned)`, and `Pose` → `FigurePose`.
- [ ] `OnShown` logs `[gear-deck] figure view unavailable; showing Slots` once per deck when `Equip == Figure` and no figure is attached.
- [ ] `OnHidden`, the fold stud and the Slots stud reset `FigurePose` to `PaperdollPose.Default`.
- [ ] `RectFor(GearZone.Figure)` is `_g.Figure`. Task 6's drop highlight and Task 5's tooltip anchor use it.

**Verify:** `MAC_TEST_FILTER='FullyQualifiedName~UiGearDeck|FullyQualifiedName~PaperdollFrameViews' ~/code/OpenAC/test-baseline/run-mac-tests.sh ~/code/OpenAC/OpenAC-gear-deck` → all pass

**Steps:**

- [ ] **Step 1: Write the failing tests.** Create `tests/AcDream.App.Tests/UI/Hud/UiGearDeckFigureTests.cs`. Its rig copies `UiGearDeckInputTests`' rig (same `Fake`, `World`, `Place`, `Wear`, `Send`, `Click`, `DoubleClick` helpers; copy them, because the Input test's are private), and adds a click map and an attach switch:

```csharp
using AcDream.App.Rendering;
using AcDream.App.Rendering.Gpu;
using AcDream.App.UI;
using AcDream.App.UI.Hud;
using AcDream.App.UI.Layout;
using AcDream.Core.Items;
using AcDream.Core.Items.Gear;
using Xunit;

namespace AcDream.App.Tests.UI.Hud;

public sealed class UiGearDeckFigureTests
{
    // Player, Bag, Helm (HeadWear), Sword (MeleeWeapon) and loose items as in UiGearDeckInputTests,
    // plus a loose cap (HeadWear) and a loose shirt (ChestWear) for the fit and drop tests.
    private const uint Player = 0x50000001, Bag = 0xB1, Helm = 0xC1, Sword = 0xC2, Cap = 0xD1, Shirt = 0xD2, First = 0xA00;

    /// <summary>A 100 × 214 map: rows 0-39 head (blue), rows 60-119 chest (green), the rest unmapped.</summary>
    internal static PaperdollClickMap TestMap()
    {
        var rgba = new byte[100 * 214 * 4];
        for (int y = 0; y < 214; y++)
            for (int x = 0; x < 100; x++)
            {
                int p = (y * 100 + x) * 4;
                (byte r, byte g, byte b) = y < 40 ? ((byte)0, (byte)0, (byte)0xFF)
                    : y >= 60 && y < 120 ? ((byte)0, (byte)0xFF, (byte)0) : ((byte)0xFF, (byte)0xFF, (byte)0xFF);
                (rgba[p], rgba[p + 1], rgba[p + 2], rgba[p + 3]) = (r, g, b, 0xFF);
            }
        return new PaperdollClickMap(rgba, 100, 214);
    }

    private sealed class Fake : IGearItemActions
    {
        public readonly List<string> Calls = [];
        public bool Ready() => true;
        public void Report(string text) => Calls.Add($"report {text}");
        public bool PutInContainer(uint item, uint container, int placement, bool fromGround) { Calls.Add($"put {item:X} {container:X} {placement}"); return true; }
        public bool Merge(uint from, uint to) { Calls.Add($"merge {from:X} {to:X}"); return true; }
        public bool WieldFromPaperdoll(uint item, EquipMask mask) { Calls.Add($"wield {item:X} {mask}"); return true; }
        public bool WieldSecondary(uint item) { Calls.Add($"secondary {item:X}"); return true; }
        public bool Activate(uint item) { Calls.Add($"use {item:X}"); return true; }
        public bool OpenPack(uint pack) { Calls.Add($"open {pack:X}"); return true; }
    }

    private static void Place(ClientObjectTable t, ClientObject o, uint container, int slot)
    {
        t.AddOrUpdate(o);
        Assert.True(t.MoveItem(o.ObjectId, container, slot));
    }

    private static void Wear(ClientObjectTable t, ClientObject o, EquipMask mask)
    {
        t.AddOrUpdate(o);
        Assert.True(t.MoveItem(o.ObjectId, Player, -1, mask));
    }

    /// <summary>A player with one bag, a cap and a shirt (both auto-wear), four junk items, a worn helm and sword.</summary>
    private static ClientObjectTable World()
    {
        var t = new ClientObjectTable();
        t.AddOrUpdate(new ClientObject { ObjectId = Player, Type = ItemType.Creature, ItemsCapacity = 102, ContainersCapacity = 7 });
        Place(t, new ClientObject { ObjectId = Bag, Type = ItemType.Container, ItemsCapacity = 24, Name = "Pack" }, Player, 0);
        Place(t, new ClientObject { ObjectId = Cap, Name = "Cap", Type = ItemType.Clothing, ValidLocations = EquipMask.HeadWear }, Player, 0);
        Place(t, new ClientObject { ObjectId = Shirt, Name = "Shirt", Type = ItemType.Clothing, ValidLocations = EquipMask.ChestWear }, Player, 1);
        for (int i = 0; i < 4; i++)
            Place(t, new ClientObject { ObjectId = First + (uint)i, Name = $"Item {i}", Type = ItemType.Misc }, Player, 2 + i);
        Wear(t, new ClientObject { ObjectId = Helm, WielderId = Player, Name = "Helm", ValidLocations = EquipMask.HeadWear }, EquipMask.HeadWear);
        Wear(t, new ClientObject { ObjectId = Sword, WielderId = Player, Name = "Sword", ValidLocations = EquipMask.MeleeWeapon }, EquipMask.MeleeWeapon);
        return t;
    }

    private sealed class Rig
    {
        public required UiGearDeck Deck;
        public required Fake Actions;
        public required GearDeckSession Session;
        public double Now;
    }

    private static Rig Make(bool attach = true, Func<GearDeckSettings, GearDeckSettings>? settings = null,
        float scale = 1f, List<string>? log = null)
    {
        ClientObjectTable table = World();
        var session = new GearDeckSession(null, () => "", () => false);
        if (settings is not null) session.Update(settings);
        var actions = new Fake();
        Rig rig = null!;
        var b = new GearDeckBindings(
            Objects: () => table,
            PlayerGuid: () => Player,
            Strength: () => 100,
            ResolveIcon: (_, _, _, _, _) => 0u,
            ResolveDragIcon: (_, _, _, _, _) => 77u,
            DisplayName: o => o.Name,
            Session: session,
            Actions: actions,
            IsPending: _ => false,
            Close: () => { },
            Log: log is null ? null : log.Add,
            ClickMap: TestMap());
        var deck = new UiGearDeck(b, null, null, () => rig.Now);
        rig = new Rig { Deck = deck, Actions = actions, Session = session };
        if (attach) deck.AttachFigure();
        if (scale != 1f) deck.SetScale(scale);
        deck.Height = 900f * scale;
        deck.Refresh();
        return rig;
    }

    private static (float X, float Y) Centre(DeckRect r) => (r.X + r.W / 2f, r.Y + r.H / 2f);

    private static void Send(UiGearDeck deck, int type, DeckRect at, object? payload = null, int data0 = 0)
    {
        var (x, y) = Centre(at);
        deck.HitTestLocal(x, y);
        deck.OnEvent(new UiEvent(0, deck, type, Data0: data0, Data1: (int)x, Data2: (int)y, Payload: payload));
    }

    private static void Click(UiGearDeck deck, DeckRect at)
    {
        Send(deck, UiEventType.MouseDown, at);
        Send(deck, UiEventType.Click, at);
    }

    /// <summary>A point on the figure, in deck coordinates, at click-map pixel (mx, my).</summary>
    private static (float X, float Y) MapPoint(UiGearDeck deck, int mx, int my)
    {
        DeckRect m = deck.Geometry.ClickMap;
        float s = deck.Scale;
        return (m.X + (mx + 0.5f) * s, m.Y + (my + 0.5f) * s);
    }

    [Fact]
    public void Attached_DefaultsToFigure_AndTheStudsSwitch()
    {
        Rig r = Make();
        Assert.True(r.Deck.Geometry.Input.Figure);
        Click(r.Deck, r.Deck.Geometry.SlotsStud);
        Assert.Equal(GearEquipView.Slots, r.Session.Settings.Equip);
        Assert.False(r.Deck.Geometry.Input.Figure);
        Click(r.Deck, r.Deck.Geometry.FigureStud);
        Assert.Equal(GearEquipView.Figure, r.Session.Settings.Equip);
        Assert.True(r.Deck.Geometry.Input.Figure);
    }

    [Fact]
    public void TheFigureStud_Unfolds()
    {
        Rig r = Make(settings: s => s with { Folded = true, Equip = GearEquipView.Slots });
        Click(r.Deck, r.Deck.Geometry.FigureStud);
        Assert.False(r.Session.Settings.Folded);
        Assert.True(r.Deck.Geometry.Input.Figure);
    }

    [Fact]
    public void Detached_ShowsSlots_KeepsTheSetting_AndLogsOnce()
    {
        var log = new List<string>();
        Rig r = Make(attach: false, log: log);
        Assert.False(r.Deck.Geometry.Input.Figure);
        Click(r.Deck, r.Deck.Geometry.FigureStud);
        Assert.Equal(GearEquipView.Figure, r.Session.Settings.Equip);
        r.Deck.OnShown();
        r.Deck.OnShown();
        Assert.Single(log, l => l.Contains("figure view unavailable"));
    }

    [Fact]
    public void FigureSize_OnlyWhileShown()
    {
        var parent = new UiElement { Width = 2000, Height = 2000 };
        Rig r = Make();
        parent.AddChild(r.Deck);
        Assert.True(r.Deck.TryGetFigureSize(out int w, out int h));
        Assert.Equal((140, 214), (w, h));
        parent.Visible = false;
        Assert.False(r.Deck.TryGetFigureSize(out _, out _));
        parent.Visible = true;
        Click(r.Deck, r.Deck.Geometry.FoldStud);
        Assert.False(r.Deck.TryGetFigureSize(out _, out _));
    }

    [Fact]
    public void FigureSize_FollowsTheScale()
    {
        Rig r = Make(scale: 1.5f);
        Assert.True(r.Deck.TryGetFigureSize(out int w, out int h));
        Assert.Equal((210, 321), (w, h));
    }

    [Fact]
    public void TheFrameView_RoutesTextureAndPose()
    {
        Rig r = Make();
        var view = new GearDeckPaperdollFrameView(r.Deck);
        view.SetTextureHandle(5u);
        Assert.True(r.Deck.FigureTexture.IsAssigned);
        view.ClearTextureHandle();
        Assert.False(r.Deck.FigureTexture.IsAssigned);
        Assert.Equal(r.Deck.FigurePose, view.Pose);
    }

    [Fact]
    public void Detach_ClearsTheTexture_AndFallsBackToSlots()
    {
        Rig r = Make();
        r.Deck.SetFigureTexture(new GpuTextureSlot(3));
        r.Deck.DetachFigure();
        r.Deck.Refresh();
        Assert.False(r.Deck.FigureTexture.IsAssigned);
        Assert.False(r.Deck.Geometry.Input.Figure);
    }
}
```

The helpers repeat `UiGearDeckInputTests`' private ones, with a cap and a shirt added to the world. If `ItemType.Clothing` is not the enum's name, use the clothing member `ItemType` has; the equip rules read only `ValidLocations`. If `UiElement` cannot be constructed directly (it is abstract), build the parent as `GearDeckScaleTests` does.

In `UiGearDeckInputTests.TheViewStuds_SetTheirSettings_AndFigureDoesNothing`, the deck has no figure attached, so the Figure click is still inert. Keep the test. Rename it `..._AndFigureDoesNothingWithoutAFigure` and add `Assert.Equal(GearEquipView.Figure, r.Session.Settings.Equip)` before the `FigureStud` click. A detached Figure click leaves the settings equal.

- [ ] **Step 2: Run them to see them fail.**

- [ ] **Step 3: Binding and layout input** (`UiGearDeck.cs`). Add `PaperdollClickMap? ClickMap = null` as the last `GearDeckBindings` parameter (`AcDream.App.UI.Layout` is already imported). In `LayoutInput()` add `Figure: ShowsFigureView`. Change `OnShown` / `OnHidden`:

```csharp
    /// <summary>The window is on screen: nothing to restore, the scroll is kept.</summary>
    public void OnShown()
    {
        ClearHover();
        NoteFigureUnavailable();   // UiGearDeck.Figure.cs
    }

    /// <summary>The window left the screen: forget the hover and any drop highlight, keep the scroll,
    /// and stand the figure back up so the next open starts at rest.</summary>
    public void OnHidden()
    {
        ClearHover();
        ResetFigurePose();
    }
```

- [ ] **Step 4: Create `UiGearDeck.Figure.cs`**:

```csharp
using AcDream.App.Rendering;
using AcDream.App.Rendering.Gpu;

namespace AcDream.App.UI.Hud;

internal sealed partial class UiGearDeck
{
    private bool _figureAttached;
    private bool _figureUnavailableLogged;
    private GpuTextureSlot _figureTexture = GpuTextureSlot.Unassigned;

    /// <summary>Whether the shared paper doll draws into this deck; without it Figure view falls back to Slots.</summary>
    internal bool FigureAttached => _figureAttached;

    /// <summary>The doll texture the presenter last published here.</summary>
    internal GpuTextureSlot FigureTexture => _figureTexture;

    /// <summary>How the figure is turned and zoomed; <see cref="PaperdollPose.Default"/> at rest.</summary>
    internal PaperdollPose FigurePose { get; private set; } = PaperdollPose.Default;

    /// <summary>The effective equipment view: Figure only while a figure is attached.</summary>
    private bool ShowsFigureView => _figureAttached && _b.Session.Settings.Equip == GearEquipView.Figure;

    /// <summary>The composition calls this once the paper doll renderer is built.</summary>
    internal void AttachFigure()
    {
        _figureAttached = true;
        SyncGeometry();
    }

    /// <summary>The composition calls this when the paper doll renderer is released.</summary>
    internal void DetachFigure()
    {
        _figureAttached = false;
        _figureTexture = GpuTextureSlot.Unassigned;
        ResetFigurePose();
        SyncGeometry();
    }

    internal void SetFigureTexture(GpuTextureSlot slot) => _figureTexture = slot;

    /// <summary>
    /// The figure's size in points while it is on screen: the deck and every
    /// ancestor visible, a player known, unfolded in Figure view. The doll is
    /// rendered at this size, as the retail viewport renders at its own.
    /// </summary>
    internal bool TryGetFigureSize(out int width, out int height)
    {
        width = 0;
        height = 0;
        if (!_g.Input.Figure || _g.Input.Folded || _model.Packs.Count == 0) return false;
        for (UiElement? e = this; e is not null; e = e.Parent)
            if (!e.Visible) return false;
        width = (int)MathF.Round(_g.Figure.W);
        height = (int)MathF.Round(_g.Figure.H);
        return width > 0 && height > 0;
    }

    private void ResetFigurePose() => FigurePose = PaperdollPose.Default;

    /// <summary>One log line the first time the deck opens wanting the figure without one.</summary>
    private void NoteFigureUnavailable()
    {
        if (_figureAttached || _figureUnavailableLogged || _b.Session.Settings.Equip != GearEquipView.Figure) return;
        _figureUnavailableLogged = true;
        _b.Log?.Invoke("[gear-deck] figure view unavailable; showing Slots");
    }
}
```

- [ ] **Step 5: Studs** (`UiGearDeck.Input.cs` `Click`). Replace the `SlotsStud` and `FigureStud` cases and extend `FoldStud`:

```csharp
            case GearZone.FoldStud:
                _b.Session.Update(s => s with { Folded = !s.Folded });
                ResetFigurePose();
                ScrollTo(_scrollRow);
                return true;
            case GearZone.SlotsStud:
                _b.Session.Update(s => s with { Folded = false, Equip = GearEquipView.Slots });
                ResetFigurePose();
                ScrollTo(_scrollRow);
                return true;
            case GearZone.FigureStud:
                if (!_figureAttached) return true;   // dim without a figure
                _b.Session.Update(s => s with { Folded = false, Equip = GearEquipView.Figure });
                ScrollTo(_scrollRow);
                return true;
```

`ScrollTo` calls `SyncGeometry`, which reads `LayoutInput()`, so the new view is in `Geometry` when the click returns.

- [ ] **Step 6: Drawing** (`UiGearDeck.Draw.cs`). In `DrawEquipHeader`:

```csharp
        bool figure = g.Input.Figure && !g.Input.Folded;
        DrawLabelledStud(ctx, g.FigureStud, "Figure", on: figure, dim: !_figureAttached);
        DrawLabelledStud(ctx, g.SlotsStud, "Slots", on: !figure && !g.Input.Folded);
```

Update its summary to "the Figure (dim without a figure) and Slots studs". In `OnDraw`, replace `else DrawSlots(ctx, objects);` with:

```csharp
        else
        {
            if (g.Input.Figure) DrawFigure(ctx);
            DrawSlots(ctx, objects);   // the ring in Figure view, the grid in Slots view
        }
```

Add to the equipment region:

```csharp
    /// <summary>The figure: a dark backdrop and the doll the paper doll presenter rendered for it.</summary>
    private void DrawFigure(UiRenderContext ctx)
    {
        DeckRect f = _g.Figure;
        ctx.FillRoundedRect(f.X, f.Y, f.W, f.H, P(6f), SlotFill);
        if (!_figureTexture.IsAssigned) return;
        uint handle = AcDream.App.Rendering.TextRenderer.ResolveExternalTextureSlot(_figureTexture);
        if (handle == 0u) return;
        // The private viewport renders top-down (PrivateEntityViewportRenderer.TextureIsBottomUp is false).
        ctx.DrawSprite(handle, f.X, f.Y, f.W, f.H, 0f, 0f, 1f, 1f, Vector4.One);
    }
```

`DrawSlots` already draws each slot at `_g.EquipSlot(i)`, so it draws the ring in Figure view with no change. Its labels are re-fitted, because `_slotLabels` is cleared whenever the geometry is rebuilt.

`RectFor` gains `GearZone.Figure => _g.Figure,`.

- [ ] **Step 7: The frame-view adapter.** Append to `src/AcDream.App/Rendering/PaperdollFrameViews.cs`:

```csharp
/// <summary>The Gear Deck's figure as a place the paper doll is shown.</summary>
internal sealed class GearDeckPaperdollFrameView(AcDream.App.UI.Hud.UiGearDeck deck) : IPaperdollFrameView
{
    public PaperdollPose Pose => deck.FigurePose;

    public bool TryGetVisibleSize(out int width, out int height) => deck.TryGetFigureSize(out width, out height);

    public void SetTextureHandle(uint textureHandle) => deck.SetFigureTexture(UiTextureTableHandle.ToSlot(textureHandle));

    public void ClearTextureHandle() => deck.SetFigureTexture(Gpu.GpuTextureSlot.Unassigned);
}
```

- [ ] **Step 8: Run the tests** → pass, including every existing `UiGearDeck*` test.

Existing tests build decks without a figure attached, so `Input.Figure` stays false and their geometry is unchanged.

- [ ] **Step 9: Commit**

```bash
git add src/AcDream.App/UI/Hud/UiGearDeck.Figure.cs src/AcDream.App/UI/Hud/UiGearDeck.cs src/AcDream.App/UI/Hud/UiGearDeck.Draw.cs \
  src/AcDream.App/UI/Hud/UiGearDeck.Input.cs src/AcDream.App/Rendering/PaperdollFrameViews.cs \
  tests/AcDream.App.Tests/UI/Hud/UiGearDeckFigureTests.cs tests/AcDream.App.Tests/UI/Hud/UiGearDeckInputTests.cs
git commit -m "Gear Deck: host the paper doll figure and switch Figure and Slots

Claude-Session: https://claude.ai/code/session_017hCEwifLBBM9NGyiUdMFVs"
```

---

### Task 5: Figure interaction: body hit, lift, take off, turn, zoom

**Goal:** A point on the figure names a body region through the retail click map. The worn item there can be hovered (tooltip), lifted and double-clicked off. A press where nothing is worn turns the figure, the wheel zooms it, and a double-click where nothing is worn stands it back up.

**Files:**
- Modify: `src/AcDream.App/UI/Hud/UiGearDeck.Figure.cs`
- Modify: `src/AcDream.App/UI/Hud/UiGearDeck.Input.cs`
- Test: `tests/AcDream.App.Tests/UI/Hud/UiGearDeckFigureTests.cs`

**Acceptance Criteria:**
- [ ] Every hit test in the deck goes through one method, `UiGearDeck.HitAt(x, y)`. It calls `_g.HitAt`, and for a `Figure` hit sets `Index = (int)FigureRegionAt(x, y)`. No `_g.HitAt(` call is left in `UiGearDeck.Input.cs` or `UiGearDeck.Query.cs` apart from the one inside `HitAt`.
- [ ] `FigureRegionAt` returns `EquipMask.None` when there is no click map, the pose is not the default, or the point is outside `ClickMap`. Otherwise it returns `ClickMap.GetBodyLocation((int)((x − map.X) / scale), (int)((y − map.Y) / scale))`.
- [ ] `WornAt` handles `Figure`: the region's `PaperdollSelectionPolicy.GetUpperInventoryObject`, or null when the region is None or the answer is the player.
- [ ] Hovering a worn body part shows that item's caption (`TooltipFor` → `Caption`). Hover changes when the pointer moves to another region, because the hit's Index differs.
- [ ] A press on a worn body part lifts the item: `IsDragSource` is true and the payload is `ItemDragSource.Equipment` with `SourceIndex` −1.
- [ ] A press on the figure where nothing is worn starts a turn. While the button is held, each `MouseMove` turns `FigurePose` by `(dx / scale) × 0.012` radians. `MouseUp` and `CaptureChanged` end the turn. A turn's moves do not change `Hover`.
- [ ] A `Scroll` over the figure zooms `FigurePose` by `Data0` notches and is consumed. A scroll over the contents still scrolls the list.
- [ ] A double-click on a worn body part takes it off (`Dispatcher.TakeOff`). On the figure where nothing is worn, it resets `FigurePose`.
- [ ] While `FigurePose` is not the default, the body names no region: the tooltip, lift and take-off do nothing there.

**Verify:** `MAC_TEST_FILTER='FullyQualifiedName~UiGearDeck' ~/code/OpenAC/test-baseline/run-mac-tests.sh ~/code/OpenAC/OpenAC-gear-deck` → all pass

**Steps:**

- [ ] **Step 1: Write the failing tests** (append to `UiGearDeckFigureTests`; `SendAt` is `Send` with a point instead of a rect):

```csharp
private static void SendAt(UiGearDeck deck, int type, (float X, float Y) at, int data0 = 0, object? payload = null)
{
    deck.HitTestLocal(at.X, at.Y);
    deck.OnEvent(new UiEvent(0, deck, type, Data0: data0, Data1: (int)at.X, Data2: (int)at.Y, Payload: payload));
}

[Theory]
[InlineData(1f)]
[InlineData(1.5f)]
public void TheHead_IsTheHelm_AtAnyScale(float scale)
{
    Rig r = Make(scale: scale);
    var head = MapPoint(r.Deck, 50, 10);
    SendAt(r.Deck, UiEventType.MouseMove, head);
    Assert.Equal(GearZone.Figure, r.Deck.Hover.Zone);
    Assert.Equal((int)EquipMask.HeadWear, r.Deck.Hover.Index);
    r.Now = 10;
    Assert.Equal("Helm", r.Deck.VisibleTooltip?.Title);
}
```

```csharp
[Fact]
public void ABareRegion_AndUnmappedSpace_NameNothingWorn()
{
    Rig r = Make();
    var chest = MapPoint(r.Deck, 50, 80);     // mapped, nothing worn there
    SendAt(r.Deck, UiEventType.MouseMove, chest);
    Assert.Equal((int)(EquipMask.ChestWear | EquipMask.ChestArmor), r.Deck.Hover.Index);
    r.Now = 10;
    Assert.Null(r.Deck.VisibleTooltip);
    var gap = MapPoint(r.Deck, 50, 50);       // white: unmapped
    SendAt(r.Deck, UiEventType.MouseMove, gap);
    Assert.Equal(0, r.Deck.Hover.Index);
}

[Fact]
public void APressOnTheHelm_LiftsIt()
{
    Rig r = Make();
    var head = MapPoint(r.Deck, 50, 10);
    Assert.True(r.Deck.HitTestLocal(head.X, head.Y));
    Assert.True(r.Deck.IsDragSource);
    SendAt(r.Deck, UiEventType.MouseDown, head);
    var p = Assert.IsType<ItemDragPayload>(r.Deck.GetDragPayload());
    Assert.Equal((Helm, ItemDragSource.Equipment, -1), (p.ObjId, p.SourceKind, p.SourceIndex));
}

[Fact]
public void DoubleClickTheHelm_TakesItOff()
{
    Rig r = Make();
    var head = MapPoint(r.Deck, 50, 10);
    SendAt(r.Deck, UiEventType.MouseDown, head);
    SendAt(r.Deck, UiEventType.Click, head);
    SendAt(r.Deck, UiEventType.MouseDown, head);
    SendAt(r.Deck, UiEventType.Click, head);
    SendAt(r.Deck, UiEventType.DoubleClick, head);
    Assert.Contains(r.Actions.Calls, c => c.StartsWith($"put {Helm:X} "));
}

[Fact]
public void DragWhereNothingIsWorn_Turns_AndTheMapGoesQuiet()
{
    Rig r = Make();
    var chest = MapPoint(r.Deck, 50, 80);
    Assert.True(r.Deck.HitTestLocal(chest.X, chest.Y));
    Assert.False(r.Deck.IsDragSource);
    SendAt(r.Deck, UiEventType.MouseDown, chest);
    SendAt(r.Deck, UiEventType.MouseMove, (chest.X + 50f, chest.Y));
    Assert.Equal(0.6f, r.Deck.FigurePose.Yaw, 3);
    SendAt(r.Deck, UiEventType.MouseUp, (chest.X + 50f, chest.Y));
    SendAt(r.Deck, UiEventType.MouseMove, (chest.X + 90f, chest.Y));
    Assert.Equal(0.6f, r.Deck.FigurePose.Yaw, 3);   // released: no more turning

    var head = MapPoint(r.Deck, 50, 10);
    SendAt(r.Deck, UiEventType.MouseMove, head);
    Assert.Equal(0, r.Deck.Hover.Index);            // turned: no region
    Assert.True(r.Deck.HitTestLocal(head.X, head.Y));
    Assert.False(r.Deck.IsDragSource);
}

[Fact]
public void TheWheel_ZoomsTheFigure_AndDoubleClickResets()
{
    Rig r = Make();
    var chest = MapPoint(r.Deck, 50, 80);
    SendAt(r.Deck, UiEventType.Scroll, chest, data0: 2);
    Assert.Equal(1.12f * 1.12f, r.Deck.FigurePose.Zoom, 3);
    SendAt(r.Deck, UiEventType.MouseDown, chest);
    SendAt(r.Deck, UiEventType.Click, chest);
    SendAt(r.Deck, UiEventType.MouseDown, chest);
    SendAt(r.Deck, UiEventType.Click, chest);
    SendAt(r.Deck, UiEventType.DoubleClick, chest);
    Assert.True(r.Deck.FigurePose.IsDefault);
}

[Fact]
public void HidingOrFolding_StandsTheFigureUp()
{
    Rig r = Make();
    var chest = MapPoint(r.Deck, 50, 80);
    SendAt(r.Deck, UiEventType.Scroll, chest, data0: 1);
    r.Deck.OnHidden();
    Assert.True(r.Deck.FigurePose.IsDefault);
    SendAt(r.Deck, UiEventType.Scroll, chest, data0: 1);
    Click(r.Deck, r.Deck.Geometry.FoldStud);
    Assert.True(r.Deck.FigurePose.IsDefault);
}
```

- [ ] **Step 2: Run them to see them fail.**

- [ ] **Step 3: The deck's hit test** (`UiGearDeck.Figure.cs`):

```csharp
    /// <summary>Radians the figure turns per point dragged, at scale 1.</summary>
    internal const float TurnPerPoint = 0.012f;

    /// <summary>The pointer x the last turn step was measured from, while a press on the figure turns it.</summary>
    private float? _turnFromX;

    /// <summary>
    /// The geometry's hit with the body filled in: a Figure hit's Index is the
    /// body region under the point as its <see cref="EquipMask"/> bits (0 for
    /// none), so a move to another region is a new hover, and a press keeps
    /// the region it landed on.
    /// </summary>
    internal GearDeckHit HitAt(float x, float y)
    {
        GearDeckHit hit = _g.HitAt(x, y);
        return hit.Zone == GearZone.Figure ? hit with { Index = (int)FigureRegionAt(x, y) } : hit;
    }

    /// <summary>The body region at a deck point through the retail click map, sampled 1:1 at scale 1;
    /// none without a map, off the map, or while the figure is turned or zoomed.</summary>
    private EquipMask FigureRegionAt(float x, float y)
    {
        if (_b.ClickMap is not { } map || !FigurePose.IsDefault) return EquipMask.None;
        DeckRect m = _g.ClickMap;
        if (!m.Contains(x, y)) return EquipMask.None;
        return map.GetBodyLocation((int)((x - m.X) / _scale), (int)((y - m.Y) / _scale));
    }

    /// <summary>The item worn outermost on a body region, as the retail doll picks it; null for none or bare skin.</summary>
    private uint? WornOnBody(EquipMask region)
    {
        if (region == EquipMask.None) return null;
        uint player = _b.PlayerGuid();
        uint id = PaperdollSelectionPolicy.GetUpperInventoryObject(_b.Objects(), player, region);
        return id == 0u || id == player ? null : id;
    }

    /// <summary>Turns the figure as a held press moves; true while a turn is under way.</summary>
    private bool TurnTo(float x)
    {
        if (_turnFromX is not float from) return false;
        FigurePose = FigurePose.Turned((x - from) / _scale * TurnPerPoint);
        _turnFromX = x;
        return true;
    }
```

Add `using AcDream.Core.Items;` (for `EquipMask`) and `using AcDream.Runtime.Gameplay;` if `PaperdollSelectionPolicy` lives there; check with `grep -rn "class PaperdollSelectionPolicy" src`.

- [ ] **Step 4: Route every hit test through `HitAt`.** In `UiGearDeck.Input.cs` (and `UiGearDeck.Query.cs`, if it hit-tests), replace each `_g.HitAt(` with `HitAt(`: `OnHitTest`, `MouseDown`, `ReleasedOnPress`, `MouseMove`, `Scroll` and `DropTargetAt`. Then check: `grep -n "_g.HitAt(" src/AcDream.App/UI/Hud/UiGearDeck*.cs` should show only the line in `HitAt`.

- [ ] **Step 5: Wire the figure into the input.**

`WornAt` gains, before `return null;`:

```csharp
        if (hit.Zone == GearZone.Figure) return WornOnBody((EquipMask)(uint)hit.Index);
```

Its first line, `if (hit.Index < 0) return null;`, stays. A Figure index is never negative, because the click-map masks are low bits.

`OnEvent`:

```csharp
            case UiEventType.MouseDown:
                ...existing lines...
                // A press on the figure where nothing is worn turns it; a worn part lifts instead.
                _turnFromX = _press.Zone == GearZone.Figure && WornAt(_press) is null ? e.Data1 : null;
                return false;
            case UiEventType.MouseUp:
            case UiEventType.CaptureChanged:
                _turnFromX = null;
                return false;
            case UiEventType.MouseMove:
                if (TurnTo(e.Data1)) return true;
                SetHover(HitAt(e.Data1, e.Data2));
                return Hover.Zone != GearZone.None;
            case UiEventType.Scroll:
                if (_g.Input.Figure && _g.Figure.Contains(e.Data1, e.Data2))
                {
                    FigurePose = FigurePose.Zoomed(e.Data0);   // +1 per notch up: in
                    SetHover(HitAt(e.Data1, e.Data2));
                    return true;
                }
                if (!_g.Contents.Contains(e.Data1, e.Data2)) return false;
                ...existing scroll lines...
```

`UiEventType.MouseUp` (0x202) and `UiEventType.CaptureChanged` (0x215) both exist, and `UiMarkupColorPopup` handles them the same way. Make sure the new `case` labels do not duplicate one already in the switch.

`DoubleClick` gains:

```csharp
            case GearZone.Figure:
                if (WornAt(hit) is uint onBody) Dispatcher.TakeOff(onBody);
                else FigurePose = PaperdollPose.Default;   // nothing worn here: stand the figure back up
                return true;
```

`TooltipFor` gains `case GearZone.Figure when WornAt(hit) is uint body: return Caption(body);` before `default`.

`GetDragPayload`: a figure lift reports no slot index:

```csharp
    public override object? GetDragPayload() =>
        DragItem(_press) is { } lifted
            ? new ItemDragPayload(lifted.Id, lifted.Kind, _press.Zone == GearZone.Figure ? -1 : _press.Index, null)
            : null;
```

`DragItem` already falls through to `WornAt`, so a press on a worn body part lifts as equipment. `CapturesPointerDrag` stays `IsControl && DragItem is null`, so a press on bare figure keeps the window still and the root delivers its moves to the deck.

`ClearHover()` also sets `_turnFromX = null`.

- [ ] **Step 6: Run the tests** → pass, including every existing `UiGearDeck*` test.

- [ ] **Step 7: Commit**

```bash
git add src/AcDream.App/UI/Hud/UiGearDeck.Figure.cs src/AcDream.App/UI/Hud/UiGearDeck.Input.cs \
  src/AcDream.App/UI/Hud/UiGearDeck.Query.cs tests/AcDream.App.Tests/UI/Hud/UiGearDeckFigureTests.cs
git commit -m "Gear Deck: figure body hit, lift, take off, turn and zoom

Claude-Session: https://claude.ai/code/session_017hCEwifLBBM9NGyiUdMFVs"
```

(Leave `UiGearDeck.Query.cs` out of `git add` if it did not change.)

---

### Task 6: Drops on the figure

**Goal:** Dropping an item on the figure wears it: at the body region under the pointer when that region fits it, otherwise as retail's doll does (an auto-wear item at all its valid locations). The drop highlight shows whether the drop will be taken.

**Files:**
- Modify: `src/AcDream.App/UI/Hud/GearDropDispatcher.cs`
- Modify: `src/AcDream.App/UI/Hud/UiGearDeck.Input.cs` (`DropTargetAt`, `DropReleased`, `SetDropHighlight`)
- Test: `tests/AcDream.App.Tests/UI/Hud/GearDropDispatcherTests.cs`, `tests/AcDream.App.Tests/UI/Hud/UiGearDeckFigureTests.cs`

**Acceptance Criteria:**
- [ ] `GearDropDispatcher.FigureAccepts(ClientObject item, EquipMask region)` is true when the region is not None and (`IsOffHandWeaponDrop(item, region)` or `ResolvePaperdollDropWieldMask(item, region) != None`), or else when `ItemEquipRules.IsAutoWearItem(item)`.
- [ ] `DropOnFigure(item, region)` does nothing for an unknown item. When the region fits, it calls `DropOnSlot(item, region)`. Else, for an auto-wear item, it calls `Ready()` and then `WieldFromPaperdoll(item, item.ValidLocations)`. Anything else sends nothing.
- [ ] `DropTargetAt` returns a `Figure` hit (with its region Index) whenever the figure is shown.
- [ ] `DropReleased` on a Figure hit calls `DropOnFigure(p.ObjId, (EquipMask)(uint)hit.Index)` and logs `-> DropOnFigure ...` like the other drops.
- [ ] The drop highlight for a Figure hit outlines `_g.Figure`. It accepts per `FigureAccepts`.

**Verify:** `MAC_TEST_FILTER='FullyQualifiedName~GearDropDispatcher|FullyQualifiedName~UiGearDeckFigure' ~/code/OpenAC/test-baseline/run-mac-tests.sh ~/code/OpenAC/OpenAC-gear-deck` → all pass

**Steps:**

- [ ] **Step 1: Write the failing tests.** In `GearDropDispatcherTests` (its `World()` gives a player, `Bag` and a loose `Sword` with `ValidLocations = MeleeWeapon`). `ItemEquipRules.IsAutoWearItem` is "ValidLocations meets `AutoWearMask`" (head, chest and the other clothing and armour bits, plus Cloak), and `ResolvePaperdollDropWieldMask` returns an auto-wear item's whole `ValidLocations`, so a shirt is worn at `ChestWear` whichever way it arrives:

```csharp
private const uint Shirt = 0xD2, Junk = 0xD3;
private const EquipMask ChestRegion = EquipMask.ChestWear | EquipMask.ChestArmor;

private static (ClientObjectTable t, Fake a, GearDropDispatcher d) WithShirt()
{
    var w = World();
    Place(w.t, new ClientObject { ObjectId = Shirt, Type = ItemType.Clothing, ValidLocations = EquipMask.ChestWear }, Player, 1);
    Place(w.t, new ClientObject { ObjectId = Junk, Type = ItemType.Misc }, Player, 2);
    return w;
}

[Fact]
public void DropOnFigure_FittingRegion_WearsThere()
{
    var (t, a, d) = WithShirt();
    Assert.True(GearDropDispatcher.FigureAccepts(t.Get(Shirt)!, ChestRegion));
    d.DropOnFigure(Shirt, ChestRegion);
    Assert.Equal(["wield D2 ChestWear"], a.Calls);
}

[Fact]
public void DropOnFigure_OffRegionOrNoRegion_WearsAnAutoWearItemAnyway()
{
    var (t, a, d) = WithShirt();
    Assert.True(GearDropDispatcher.FigureAccepts(t.Get(Shirt)!, EquipMask.HeadWear));
    d.DropOnFigure(Shirt, EquipMask.HeadWear);
    d.DropOnFigure(Shirt, EquipMask.None);
    Assert.Equal(["wield D2 ChestWear", "wield D2 ChestWear"], a.Calls);
}

[Fact]
public void DropOnFigure_RefusesWhatTheFigureCannotWear()
{
    var (t, a, d) = WithShirt();
    Assert.False(GearDropDispatcher.FigureAccepts(t.Get(Junk)!, ChestRegion));
    Assert.False(GearDropDispatcher.FigureAccepts(t.Get(Sword)!, EquipMask.None));   // a weapon is not auto-wear
    d.DropOnFigure(Junk, ChestRegion);
    d.DropOnFigure(Sword, EquipMask.None);
    d.DropOnFigure(0xDEAD, ChestRegion);   // unknown item
    Assert.Empty(a.Calls);
}

[Fact]
public void DropOnFigure_NotReady_SendsNothing()
{
    var (_, a, d) = WithShirt();
    a.IsReady = false;
    d.DropOnFigure(Shirt, EquipMask.None);
    Assert.Empty(a.Calls);
}
```

If `ItemType.Clothing` is not the enum's name, use the clothing member `ItemType` has (`grep -n "Clothing\|Armor" src/AcDream.Core/Items/ClientObject.cs`). The rule only reads `ValidLocations`.

In `UiGearDeckFigureTests`:

```csharp
[Fact]
public void DroppingTheCapOnTheHead_WearsIt_AndTheHighlightAccepts()
{
    Rig r = Make();
    var head = MapPoint(r.Deck, 50, 10);
    var payload = new ItemDragPayload(Cap, ItemDragSource.Inventory, 0, null);
    SendAt(r.Deck, UiEventType.DragEnter, head, payload: payload);
    var drop = Assert.NotNull(r.Deck.DropHighlight);
    Assert.Equal(GearZone.Figure, drop.Hit.Zone);
    Assert.True(drop.Accept);
    SendAt(r.Deck, UiEventType.DropReleased, head, payload: payload);
    Assert.Contains(r.Actions.Calls, c => c.StartsWith($"wield {Cap:X} "));
}

[Fact]
public void ALooseMiscItem_IsRefusedByTheFigure()
{
    Rig r = Make();
    var chest = MapPoint(r.Deck, 50, 80);
    var payload = new ItemDragPayload(First, ItemDragSource.Inventory, 0, null);
    SendAt(r.Deck, UiEventType.DragEnter, chest, payload: payload);
    Assert.False(r.Deck.DropHighlight?.Accept);
    SendAt(r.Deck, UiEventType.DropReleased, chest, payload: payload);
    Assert.DoesNotContain(r.Actions.Calls, c => c.StartsWith("wield"));
}
```

- [ ] **Step 2: Run them to see them fail.**

- [ ] **Step 3: Implement the dispatcher** (`GearDropDispatcher.cs`, after `DropOnSlot`):

```csharp
    /// <summary>Whether a drop on the figure would wear <paramref name="item"/>: at <paramref name="region"/>
    /// when it fits there, else anywhere for an item retail's doll wears whole (IsAutoWearItem).</summary>
    public static bool FigureAccepts(ClientObject item, EquipMask region) =>
        RegionFits(item, region) || ItemEquipRules.IsAutoWearItem(item);

    private static bool RegionFits(ClientObject item, EquipMask region) =>
        region != EquipMask.None
        && (ItemEquipRules.IsOffHandWeaponDrop(item, region)
            || ItemEquipRules.ResolvePaperdollDropWieldMask(item, region) != EquipMask.None);

    /// <summary>A drop on the figure: the body region under the pointer when it fits the item (as a drop on that
    /// slot), else retail's doll drop (PaperdollController.HandleDollDrop): an auto-wear item at all its valid
    /// locations, and nothing for anything else.</summary>
    public void DropOnFigure(uint item, EquipMask region)
    {
        if (objects.Get(item) is not { } obj) return;
        if (RegionFits(obj, region))
        {
            DropOnSlot(item, region);
            return;
        }
        if (!ItemEquipRules.IsAutoWearItem(obj)) return;
        if (!actions.Ready()) return;
        actions.WieldFromPaperdoll(item, obj.ValidLocations);
    }
```

- [ ] **Step 4: Wire the deck.** `DropTargetAt` gains `GearZone.Figure when _g.Input.Figure => hit,`. `DropReleased`'s switch gains:

```csharp
            case GearZone.Figure:
                action = nameof(GearDropDispatcher.DropOnFigure);
                Dispatcher.DropOnFigure(p.ObjId, (EquipMask)(uint)hit.Index);
                break;
```

`SetDropHighlight`'s `accept` becomes:

```csharp
        bool accept = hit.Zone switch
        {
            GearZone.EquipSlot or GearZone.WornCell => _b.Objects().Get(p.ObjId) is { } item
                && (ItemEquipRules.IsOffHandWeaponDrop(item, SlotMaskAt(hit))
                    || ItemEquipRules.ResolvePaperdollDropWieldMask(item, SlotMaskAt(hit)) != EquipMask.None),
            GearZone.Figure => _b.Objects().Get(p.ObjId) is { } worn
                && GearDropDispatcher.FigureAccepts(worn, (EquipMask)(uint)hit.Index),
            _ => true,   // a pack or cell always takes the drop
        };
```

Keep the comment that sits above it, adding "the figure as its drop would".

- [ ] **Step 5: Run the tests** → pass.

- [ ] **Step 6: Commit**

```bash
git add src/AcDream.App/UI/Hud/GearDropDispatcher.cs src/AcDream.App/UI/Hud/UiGearDeck.Input.cs \
  tests/AcDream.App.Tests/UI/Hud/GearDropDispatcherTests.cs tests/AcDream.App.Tests/UI/Hud/UiGearDeckFigureTests.cs
git commit -m "Gear Deck: drop on the figure to wear

Claude-Session: https://claude.ai/code/session_017hCEwifLBBM9NGyiUdMFVs"
```

---

### Task 7: Hover highlight of fitting pack items

**Goal:** While the pointer is over an equipment slot (ring or Slots view) or a body region on the figure, the items in the pack view or results that could be worn there are outlined in green.

**Files:**
- Modify: `src/AcDream.App/UI/Hud/UiGearDeck.Figure.cs` (fit set)
- Modify: `src/AcDream.App/UI/Hud/UiGearDeck.Draw.cs` (grid and list outlines)
- Test: `tests/AcDream.App.Tests/UI/Hud/UiGearDeckFigureTests.cs`

**Acceptance Criteria:**
- [ ] `HoverFitMask` is the hovered `EquipSlot`'s catalog mask, or the hovered `Figure` region (`(EquipMask)(uint)Index`). Otherwise it is None, including folded worn cells, items and no hover.
- [ ] `HoverFits(uint id)` is true for a row of `Shown` that is not worn and not a pack, whose object fits the mask by the drop rule (`IsOffHandWeaponDrop` or `ResolvePaperdollDropWieldMask != None`).
- [ ] The fit set is rebuilt only when the mask, the model build, the query version or the selected pack changes (cache key), not every frame.
- [ ] In the grid, a fitting cell that is neither selected nor hovered gets `StrokeInside(cell, 5f, DropAccept with { W = 0.8f })`. In the list, a fitting row gets the same stroke with radius 3 and `shrinkH: 1f`. Selection and hover strokes take precedence.
- [ ] Works in Slots view too, with no figure attached.

**Verify:** `MAC_TEST_FILTER='FullyQualifiedName~UiGearDeck' ~/code/OpenAC/test-baseline/run-mac-tests.sh ~/code/OpenAC/OpenAC-gear-deck` → all pass

**Steps:**

- [ ] **Step 1: Write the failing tests:**

```csharp
private static int SlotIndex(EquipMask mask)
{
    for (int i = 0; i < GearSlotCatalog.Slots.Count; i++)
        if (GearSlotCatalog.Slots[i].Mask == mask) return i;
    throw new InvalidOperationException();
}

[Fact]
public void HoveringTheHeadSlot_HighlightsTheCap_NotTheShirt()
{
    Rig r = Make();
    DeckRect head = r.Deck.Geometry.EquipSlot(SlotIndex(EquipMask.HeadWear));
    Send(r.Deck, UiEventType.MouseMove, head);
    Assert.Equal(EquipMask.HeadWear, r.Deck.HoverFitMask);
    Assert.True(r.Deck.HoverFits(Cap));
    Assert.False(r.Deck.HoverFits(Shirt));
    Assert.False(r.Deck.HoverFits(Helm));   // worn, not a pack item
}

[Fact]
public void HoveringTheChest_OnTheFigure_HighlightsTheShirt()
{
    Rig r = Make();
    SendAt(r.Deck, UiEventType.MouseMove, MapPoint(r.Deck, 50, 80));
    Assert.True(r.Deck.HoverFits(Shirt));
    Assert.False(r.Deck.HoverFits(Cap));
}

[Fact]
public void SlotsView_HighlightsToo_AndLeavingClears()
{
    Rig r = Make(attach: false);
    DeckRect head = r.Deck.Geometry.EquipSlot(SlotIndex(EquipMask.HeadWear));
    Send(r.Deck, UiEventType.MouseMove, head);
    Assert.True(r.Deck.HoverFits(Cap));
    r.Deck.OnEvent(new UiEvent(0, r.Deck, UiEventType.HoverLeave));
    Assert.Equal(EquipMask.None, r.Deck.HoverFitMask);
    Assert.False(r.Deck.HoverFits(Cap));
}
```

Task 4's `World` puts `Cap` and `Shirt` in the main pack as loose items, so they are in `Shown`.

- [ ] **Step 2: Run them to see them fail.**

- [ ] **Step 3: Implement** (`UiGearDeck.Figure.cs`):

```csharp
    private readonly HashSet<uint> _fits = [];
    private (EquipMask Mask, ulong Build, int QueryVersion, uint Pack)? _fitsFor;

    /// <summary>The slot or body region the pointer is over, whose fitting pack items are outlined; None elsewhere.</summary>
    internal EquipMask HoverFitMask => Hover.Zone switch
    {
        GearZone.EquipSlot when Hover.Index >= 0 && Hover.Index < GearSlotCatalog.Slots.Count
            => GearSlotCatalog.Slots[Hover.Index].Mask,
        GearZone.Figure => (EquipMask)(uint)Hover.Index,
        _ => EquipMask.None,
    };

    /// <summary>Whether a shown row could be worn where the pointer is (the drop highlight's rule);
    /// built once per mask, model, query and pack.</summary>
    internal bool HoverFits(uint id)
    {
        EquipMask mask = HoverFitMask;
        if (mask == EquipMask.None) return false;
        IReadOnlyList<GearItemView> rows = Shown;
        uint pack = SelectedPackId;
        if (_fitsFor != (mask, _modelBuild, _queryVersion, pack))
        {
            _fits.Clear();
            ClientObjectTable objects = _b.Objects();
            foreach (GearItemView row in rows)
            {
                if (row.Worn || row.IsPack || objects.Get(row.Id) is not { } o) continue;
                if (ItemEquipRules.IsOffHandWeaponDrop(o, mask)
                    || ItemEquipRules.ResolvePaperdollDropWieldMask(o, mask) != EquipMask.None)
                    _fits.Add(row.Id);
            }
            _fitsFor = (mask, _modelBuild, _queryVersion, pack);
        }
        return _fits.Contains(id);
    }
```

Add `using AcDream.Core.Items.Gear;` if it is missing.

- [ ] **Step 4: Draw the outlines.** In `DrawGrid`, change the stroke chain:

```csharp
                if (picked) StrokeInside(ctx, r, 5f, Handle);
                else if (Hover.Zone == GearZone.Item && Hover.Index == i) StrokeInside(ctx, r, 5f, Handle with { W = 0.6f });
                else if (HoverFits(row.Id)) StrokeInside(ctx, r, 5f, DropAccept with { W = 0.8f });
```

In `DrawList`, replace the hover stroke:

```csharp
                if (Hover.Zone == GearZone.Item && Hover.Index == i)
                    StrokeInside(ctx, r, 3f, Handle with { W = 0.6f }, shrinkH: 1f);
                else if (!_selection.Contains(row.Id) && HoverFits(row.Id))
                    StrokeInside(ctx, r, 3f, DropAccept with { W = 0.8f }, shrinkH: 1f);
```

`HoverFits` returns false at once with no mask, so a frame without a slot hover does no work.

- [ ] **Step 5: Run the tests** → pass.

- [ ] **Step 6: Commit**

```bash
git add src/AcDream.App/UI/Hud/UiGearDeck.Figure.cs src/AcDream.App/UI/Hud/UiGearDeck.Draw.cs \
  tests/AcDream.App.Tests/UI/Hud/UiGearDeckFigureTests.cs
git commit -m "Gear Deck: hovering a slot or body part outlines the pack items that fit

Claude-Session: https://claude.ai/code/session_017hCEwifLBBM9NGyiUdMFVs"
```

---

### Task 8: Wire the figure in the client

**Goal:** In the live client, the Gear Deck gets the retail click map and the shared paper doll renderer. Its figure is drawn through the composite frame view, and it detaches cleanly when the presentation is released.

**Files:**
- Modify: `src/AcDream.App/UI/RetailUiRuntime.cs`
- Modify: `src/AcDream.App/Composition/LivePresentationComposition.cs`
- Test: `tests/AcDream.App.Tests/UI/Hud/GearDeckWiringTests.cs`

**Acceptance Criteria:**
- [ ] `MountInventory` keeps the loaded click map in a field `_paperdollClickMap`. `MountGearDeck` (called after it) passes `ClickMap: _paperdollClickMap` in `GearDeckBindings`.
- [ ] `RetailUiRuntime` exposes `internal Hud.UiGearDeck? GearDeck => _gearDeck;`.
- [ ] In `LivePresentationComposition`, when the paper doll presenter is built and `Runtime.GearDeck` is not null, its view is `new PaperdollFrameViews(retailView, new GearDeckPaperdollFrameView(deck))` and `deck.AttachFigure()` is called. The existing "paperdoll viewport target" release also calls `deck.DetachFigure()`. Without a deck, the view is the plain `RetailPaperdollFrameView`, exactly as before.
- [ ] When the presenter is not built (no dispatcher, retail viewport or inventory frame), the deck stays detached, so it shows Slots and logs the unavailable line on its first open.
- [ ] `GearDeckWiringTests` asserts the click map reaches the deck's bindings. Follow the file's existing mount-test pattern; if mounting needs dats it cannot get, assert on `GearDeckBindings` construction through the same seam the file already uses.

**Verify:** `MAC_TEST_FILTER='FullyQualifiedName~GearDeckWiring|FullyQualifiedName~Paperdoll' ~/code/OpenAC/test-baseline/run-mac-tests.sh ~/code/OpenAC/OpenAC-gear-deck` → all pass; then the full suite `~/code/OpenAC/test-baseline/run-mac-tests.sh ~/code/OpenAC/OpenAC-gear-deck` → 0 failures

**Steps:**

- [ ] **Step 1: Read `tests/AcDream.App.Tests/UI/Hud/GearDeckWiringTests.cs`** and add a failing test in its style: the mounted deck's `Bindings.ClickMap` is the map `MountInventory` loaded (non-null with the installed dats), and `Runtime.GearDeck` is that deck.

- [ ] **Step 2: Runtime.** In `RetailUiRuntime.cs`:
  - Field beside `_gearDeck`: `private PaperdollClickMap? _paperdollClickMap;`
  - In `MountInventory`, after the dat lock block: `_paperdollClickMap = paperdollClickMap;`
  - In `MountGearDeck`'s `GearDeckBindings(...)`, add `ClickMap: _paperdollClickMap,` after `Text: HudTextSize`. That is the last argument now, so move the comma accordingly.
  - Beside `PaperdollViewportWidget`: `internal Hud.UiGearDeck? GearDeck => _gearDeck;`

- [ ] **Step 3: Composition.** In `LivePresentationComposition.cs`, in the paper doll block (around line 905):

```csharp
            IPaperdollFrameView paperdollView = new RetailPaperdollFrameView(
                viewport,
                new PaperdollInventoryVisibility(inventoryFrame));
            // The Gear Deck shows the same doll in its Figure view; whichever of the two is on screen gets the frame.
            AcDream.App.UI.Hud.UiGearDeck? gearDeck = interaction.RetainedUi.Runtime.GearDeck;
            if (gearDeck is not null)
            {
                paperdollView = new PaperdollFrameViews(paperdollView, new GearDeckPaperdollFrameView(gearDeck));
                gearDeck.AttachFigure();
            }
```

Pass `paperdollView` to `new PaperdollFramePresenter(...)` in place of the inline `new RetailPaperdollFrameView(...)`. In the existing `AdoptRelease("paperdoll viewport target", ...)` lambda, add `gearDeck?.DetachFigure();`. That release is registered before `gearDeck` is declared, so move the `gearDeck` lookup above the `AdoptRelease` call, or register a second `AdoptRelease("gear deck figure", () => gearDeck.DetachFigure())` right after `AttachFigure()`. Either is fine; the second keeps the diff small.

- [ ] **Step 4: Build and run the tests** (Verify line, including the full suite).

- [ ] **Step 5: Commit**

```bash
git add src/AcDream.App/UI/RetailUiRuntime.cs src/AcDream.App/Composition/LivePresentationComposition.cs \
  tests/AcDream.App.Tests/UI/Hud/GearDeckWiringTests.cs
git commit -m "Gear Deck: wire the figure to the paper doll renderer and click map

Claude-Session: https://claude.ai/code/session_017hCEwifLBBM9NGyiUdMFVs"
```

---

### Task 9: Live check (user)

**Goal:** The user confirms the Figure view works in the real client against the local ACE server.

> **USER-ORDERED GATE — NON-SKIPPABLE.** This task was requested by the user in the current conversation. It MUST NOT be closed by walking around it, by declaring it "verified inline", or by substituting a cheaper check. Close only after every item in `acceptanceCriteria` has been re-validated independently, with output captured.

**Files:** none (fixes found here get their own commits)

**Acceptance Criteria:**
- [ ] Ask the user before launching. Use the local ACE server (10.10.20.20 through `~/OpenAC-dev/dev-client.env`), never Dreamweave.
- [ ] With Modern HUD on, opening the inventory shows the Gear Deck in Figure view: the character, posed as in the retail paper doll, with the 24-slot ring round it. Worn items show in their ring cells.
- [ ] The figure stays sharp and correctly framed at deck scale 1 and at a larger scale (side-edge drag).
- [ ] Hovering the head, chest, legs and so on shows the worn item's name. Hovering a ring slot or a body part outlines the fitting pack items in green.
- [ ] Dragging a piece of armour or clothing from the pack onto the body wears it. Dragging a worn item off the body or the ring into a pack takes it off. Double-clicking a worn body part takes it off.
- [ ] Dragging bare figure space turns it, the wheel zooms, and a double-click on bare figure stands it back up. Closing and reopening the deck starts at rest.
- [ ] Slots and Figure studs switch views, the choice survives a relog, and folding still works. Modern HUD off shows the retail paper doll unchanged, still rendering.
- [ ] The user says it passes.

**Verify:** the user's confirmation in the conversation, plus client log lines with `[gear-deck]`.

---

## Self-review notes

- Spec coverage for plan 3 (Delivery item 3): the 3D model in the Equipment section (T2, T4, T8), the slot ring (T3, T4), drops on the body (T6), hover-highlighting matching pack items (T7). Plus the Decisions row "Figure (3D model + slot ring, drag to turn, scroll to zoom)" (T5), "Drop on the figure picks the slot (`PaperdollClickMap`)" (T5, T6), "double-click … a worn item takes it off" on the body (T5), and Error handling "If the figure renderer fails to start, the Figure stud is disabled and Slots view is used, with one log line" (T4, T8).
- Types used across tasks: `GearEquipView` and `Equip` (T1 → T4, T8); `PaperdollPose` (T2 → T4, T5); `PaperdollFrameViews` and `GearDeckPaperdollFrameView` (T2, T4 → T8); `GearDeckLayoutInput.Figure`, `GearZone.Figure`, `Figure`, `ClickMap`, `RingCells` (T3 → T4-T7); `AttachFigure`, `DetachFigure`, `TryGetFigureSize`, `SetFigureTexture`, `FigureTexture`, `FigurePose` (T4 → T5, T8); `HitAt`, `FigureRegionAt`, `WornOnBody` (T5 → T6, T7); `DropOnFigure`, `FigureAccepts` (T6); `HoverFitMask`, `HoverFits` (T7).
