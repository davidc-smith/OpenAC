# Command Deck Resize Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers-extended-cc:subagent-driven-development (recommended) or superpowers-extended-cc:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make the Modern HUD Command Deck resizable: the right edge stretches it wider (1×–2×), the right-hand corners scale it evenly (75%–150%, 5% steps, locks onto 100%).

**Architecture:** The static `CommandDeckLayout` becomes an immutable `DeckGeometry(Scale, Stretch)` that every draw, click detection and tooltip call reads from the deck's current instance. Two new no-op virtual hooks on `UiElement` (`FilterResizeEdges`, `ConstrainResize`) let `UiRoot`'s existing resize drag ask the window which edges may resize and what size to use. Fonts are baked per scale step. Saved width/height (already persisted) decide scale and stretch on restore.

**Tech Stack:** C# / .NET (`~/.dotnet` SDK), xUnit, the retained UI in `src/AcDream.App/UI`.

**Spec:** `docs/superpowers/specs/2026-10-26-command-deck-resize-design.md` (branch `docs/command-deck-resize-spec`, worktree `~/code/OpenAC/OpenAC-deck-resize-spec`).

## Global Constraints

- At scale 1.0 and stretch 1.0 every deck rectangle must equal today's `CommandDeckLayout` value exactly (window 420 × 150).
- Scale range 0.75–1.5, steps of 0.05, locks onto 1.00 within ±0.03. Stretch range 1.0–2.0, continuous.
- Window size is always `(420 × Stretch × Scale, 150 × Scale)`.
- Only the right edge and the top-right / bottom-right corners resize the deck. Left edge, left corners, top and bottom edges never resize it.
- The new `UiElement` hooks default to doing nothing, and every existing window's resize behaviour stays the same.
- Nothing in the geometry depends on combat mode.
- Deck tooltips stay at their normal size; only their anchor rectangle follows the geometry.
- HUD text uses the bundled sans (`DeckFonts`), never `ctx.DefaultFont` when fonts exist.
- Fork-only: no upstream PR.
- Tests run only through `~/code/OpenAC/test-baseline/run-mac-tests.sh` (it sets `ACDREAM_DAT_DIR` and skips tests that can't pass on this Mac). Narrow with `MAC_TEST_FILTER`, never a second `--filter`.
- Never launch a game client for the live check until the user says they're free. Live tests use the local ACE server (10.10.20.20 via `~/OpenAC-dev/dev-client.env`), never Dreamweave.
- Commit messages end with `Claude-Session: https://claude.ai/code/session_01WFS4EHSNafq2P1qfUuh258`.

**User decisions (already made):**
- "a" — corners scale the deck, side edges change the width only, top/bottom do nothing.
- "that works" — orb, studs, labels, H/M/L, recall studs and recast stay the same size; bars, jump strip, power track and casting text get longer; width 420 → 840 pt at 100%.
- "yes" — scale 75–150%, 5% snapping, locks onto 100%, a corner drag keeps the stretch.
- "a" — no new reset gesture; the existing "reset layout" is enough.
- "a" — grips on the right side only (right edge, top-right and bottom-right corners).

---

## Setup (before Task 1)

Make the implementation worktree from fork `main`:

```bash
cd ~/code/OpenAC/OpenAC
git worktree add ../OpenAC-deck-resize -b feat/command-deck-resize main
```

All paths below are relative to `~/code/OpenAC/OpenAC-deck-resize`. Test command used throughout (`<Filter>` varies per task):

```bash
MAC_TEST_FILTER='FullyQualifiedName~<Filter>' ~/code/OpenAC/test-baseline/run-mac-tests.sh ~/code/OpenAC/OpenAC-deck-resize
```

## File Structure

| File | Responsibility |
|------|----------------|
| `src/AcDream.App/UI/UiElement.cs` | + `FilterResizeEdges`, `ConstrainResize` no-op virtual hooks |
| `src/AcDream.App/UI/UiRoot.cs` | Calls the hooks during resize; + `ResizeEdgesFor(window)` for grip highlights |
| `src/AcDream.App/UI/Hud/CommandDeckLayout.cs` → `DeckGeometry.cs` | Geometry for one (scale, stretch), size rules, zone lookup |
| `src/AcDream.App/UI/Hud/DeckFonts.cs` | Bundled sans faces per scale step (`DeckFontSet`) |
| `src/AcDream.App/UI/Hud/UiCommandDeck.cs` | Holds the current geometry, resize hooks, size sync, grip hit band |
| `src/AcDream.App/UI/Hud/UiCommandDeck.Draw.cs` | Draws at the geometry's scale; grip highlight |
| `src/AcDream.App/UI/RetailUiRuntime.cs` | Mounts the deck resizable with the full size range |
| `tests/AcDream.App.Tests/UI/UiRootResizeHookTests.cs` | New: hook behaviour in `UiRoot` |
| `tests/AcDream.App.Tests/UI/Hud/CommandDeckLayoutTests.cs` → `DeckGeometryTests.cs` | Geometry + size rule tests |
| `tests/AcDream.App.Tests/UI/Hud/DeckFontsTests.cs` | New: per-scale font baking |
| `tests/AcDream.App.Tests/UI/Hud/UiCommandDeckResizeTests.cs` | New: the deck as a resizable window |
| `tests/AcDream.App.Tests/UI/Hud/UiCommandDeck*.cs` | Existing tests moved to `DeckGeometry.Default` |

---

### Task 1: Resize hooks on `UiElement` / `UiRoot`

**Goal:** Windows can refuse resize edges and adjust the size a resize drag uses, and `UiRoot` reports which grip edges apply to a given window; every existing window is unchanged.

**Files:**
- Modify: `src/AcDream.App/UI/UiElement.cs` (next to `ConstrainWindowDrag`, ~line 255)
- Modify: `src/AcDream.App/UI/UiRoot.cs` (`EffectiveGripEdges` ~line 157, `OnMouseMove` resize branch ~line 485–509, `HitEdges` ~line 1515)
- Test: `tests/AcDream.App.Tests/UI/UiRootResizeHookTests.cs` (create)

**Acceptance Criteria:**
- [ ] A window whose `FilterResizeEdges` returns `None` for an edge shows `HoverResizeEdges == None` there, and a press there does not resize it.
- [ ] A window whose `ConstrainResize` changes the size gets that size; for a drag that includes `Top`, the bottom edge stays fixed, and for `Left`, the right edge stays fixed.
- [ ] `ResizeEdgesFor(window)` returns the active edges while that window is resizing, the hover edges while the pointer is over its grip, and `None` for any other window.
- [ ] Existing `UiRootInputTests`, `UiCollapsibleFrameTests` and `MarkupResizableAnchorTests` stay green.

**Verify:** `MAC_TEST_FILTER='FullyQualifiedName~UiRootResizeHookTests|FullyQualifiedName~UiRootInputTests|FullyQualifiedName~UiCollapsibleFrameTests|FullyQualifiedName~MarkupResizableAnchorTests' ~/code/OpenAC/test-baseline/run-mac-tests.sh ~/code/OpenAC/OpenAC-deck-resize` → all pass, 0 failed.

**Steps:**

- [ ] **Step 1: Write the failing tests**

Create `tests/AcDream.App.Tests/UI/UiRootResizeHookTests.cs`:

```csharp
using AcDream.App.UI;

namespace AcDream.App.Tests.UI;

public sealed class UiRootResizeHookTests
{
    /// <summary>Allows only the right side; snaps height to multiples of 50 on a corner.</summary>
    private sealed class RightOnlyPanel : UiPanel
    {
        internal override ResizeEdges FilterResizeEdges(ResizeEdges hit) => hit switch
        {
            ResizeEdges.Right
                or (ResizeEdges.Right | ResizeEdges.Top)
                or (ResizeEdges.Right | ResizeEdges.Bottom) => hit,
            _ => ResizeEdges.None,
        };

        internal override void ConstrainResize(ResizeEdges edges, ref float width, ref float height)
        {
            if ((edges & (ResizeEdges.Top | ResizeEdges.Bottom)) != 0)
                height = MathF.Round(height / 50f) * 50f;
        }
    }

    private static (UiRoot Root, RightOnlyPanel Window) Make()
    {
        var root = new UiRoot { Width = 800, Height = 600 };
        var window = new RightOnlyPanel
        {
            Left = 100, Top = 100, Width = 200, Height = 100,
            Draggable = true, Resizable = true, MinWidth = 40, MinHeight = 40,
        };
        root.AddChild(window);
        return (root, window);
    }

    [Fact]
    public void FilteredLeftEdge_ShowsNoResizeAndDoesNotResize()
    {
        var (root, window) = Make();
        root.OnMouseMove(101, 150);
        Assert.Equal(ResizeEdges.None, root.HoverResizeEdges);

        root.OnMouseDown(UiMouseButton.Left, 101, 150);
        root.OnMouseMove(60, 150);
        root.OnMouseUp(UiMouseButton.Left, 60, 150);
        Assert.Equal(200f, window.Width);
    }

    [Fact]
    public void RightEdge_StillResizes()
    {
        var (root, window) = Make();
        root.OnMouseMove(298, 150);
        Assert.Equal(ResizeEdges.Right, root.HoverResizeEdges);

        root.OnMouseDown(UiMouseButton.Left, 298, 150);
        root.OnMouseMove(338, 150);
        root.OnMouseUp(UiMouseButton.Left, 338, 150);
        Assert.Equal(240f, window.Width);
        Assert.Equal(100f, window.Left);
    }

    [Fact]
    public void ConstrainedTopRightDrag_KeepsTheBottomFixed()
    {
        var (root, window) = Make();
        // Top-right corner is (300, 100). Drag up 30: proposed height 130 snaps to 150.
        root.OnMouseDown(UiMouseButton.Left, 299, 101);
        root.OnMouseMove(299, 71);
        Assert.Equal(150f, window.Height);
        Assert.Equal(200f, window.Top + window.Height);
        Assert.Equal(100f, window.Left);
        root.OnMouseUp(UiMouseButton.Left, 299, 71);
    }

    [Fact]
    public void ResizeEdgesFor_FollowsHoverAndActiveResize()
    {
        var (root, window) = Make();
        var other = new UiPanel { Left = 500, Top = 100, Width = 100, Height = 100, Resizable = true };
        root.AddChild(other);

        root.OnMouseMove(298, 150);
        Assert.Equal(ResizeEdges.Right, root.ResizeEdgesFor(window));
        Assert.Equal(ResizeEdges.None, root.ResizeEdgesFor(other));

        root.OnMouseDown(UiMouseButton.Left, 298, 150);
        root.OnMouseMove(400, 300);   // pointer leaves the edge; the resize is still active
        Assert.Equal(ResizeEdges.Right, root.ResizeEdgesFor(window));
        root.OnMouseUp(UiMouseButton.Left, 400, 300);

        root.OnMouseMove(200, 150);
        Assert.Equal(ResizeEdges.None, root.ResizeEdgesFor(window));
    }

    [Fact]
    public void DefaultHooks_ChangeNothing()
    {
        var root = new UiRoot { Width = 800, Height = 600 };
        var window = new UiPanel { Left = 100, Top = 100, Width = 200, Height = 100,
            Draggable = true, Resizable = true, MinWidth = 40, MinHeight = 40 };
        root.AddChild(window);

        root.OnMouseDown(UiMouseButton.Left, 101, 101);   // top-left corner
        root.OnMouseMove(81, 71);
        root.OnMouseUp(UiMouseButton.Left, 81, 71);
        Assert.Equal((80f, 70f, 220f, 130f), (window.Left, window.Top, window.Width, window.Height));
    }
}
```

- [ ] **Step 2: Run the tests to check they fail**

Run: `MAC_TEST_FILTER='FullyQualifiedName~UiRootResizeHookTests' ~/code/OpenAC/test-baseline/run-mac-tests.sh ~/code/OpenAC/OpenAC-deck-resize`
Expected: build FAILS — `FilterResizeEdges` / `ConstrainResize` / `ResizeEdgesFor` not found.

- [ ] **Step 3: Add the hooks to `UiElement`**

In `src/AcDream.App/UI/UiElement.cs`, directly after `ConstrainWindowDrag`:

```csharp
    /// <summary>
    /// The edges a resize may start from, given the edges the pointer is on
    /// (border or authored grip, already filtered by <see cref="ResizeX"/>,
    /// <see cref="ResizeY"/> and <see cref="ResizableEdges"/>). A window that
    /// only resizes from some corners returns <see cref="ResizeEdges.None"/>
    /// for the rest. Most windows allow them all.
    /// </summary>
    internal virtual ResizeEdges FilterResizeEdges(ResizeEdges hit) => hit;

    /// <summary>
    /// Called on each pointer move while this window is being resized, with
    /// the dragged edges and the size the drag would give it (already within
    /// Min/Max and the parent). A window with its own size rules changes
    /// <paramref name="width"/> and <paramref name="height"/>; the edges not
    /// being dragged stay where they are. Most windows leave them alone.
    /// </summary>
    internal virtual void ConstrainResize(ResizeEdges edges, ref float width, ref float height) { }
```

- [ ] **Step 4: Call them from `UiRoot`**

In `EffectiveGripEdges`, replace the final `return edges;` with:

```csharp
        return window.FilterResizeEdges(edges);
```

In `HitEdges`, replace the final `return e;` with:

```csharp
        return e == ResizeEdges.None ? e : w.FilterResizeEdges(e);
```

In `OnMouseMove`'s resize branch, replace

```csharp
            _resizeTarget.Left = nx; _resizeTarget.Top = ny;
            _resizeTarget.Width = nw; _resizeTarget.Height = nh;
```

with

```csharp
            float cw = nw, ch = nh;
            _resizeTarget.ConstrainResize(_resizeEdges, ref cw, ref ch);
            // Keep the edges that are not being dragged where they were.
            if ((_resizeEdges & ResizeEdges.Left) != 0) nx = _resizeStartX + _resizeStartW - cw;
            if ((_resizeEdges & ResizeEdges.Top) != 0) ny = _resizeStartY + _resizeStartH - ch;
            _resizeTarget.Left = nx; _resizeTarget.Top = ny;
            _resizeTarget.Width = cw; _resizeTarget.Height = ch;
```

(`ResizeRect` already computes `nx`/`ny` the same way for unconstrained sizes, so windows with the default hook get identical values.)

After the `HoverResizeEdges` property, add:

```csharp
    /// <summary>The resize edges that apply to <paramref name="window"/> now:
    /// the dragged edges while it is being resized, else the edges under the
    /// pointer while it hovers that window's grip. Windows use it to light
    /// their own grips.</summary>
    internal ResizeEdges ResizeEdgesFor(UiElement window)
    {
        if (_resizeTarget is not null)
            return ReferenceEquals(_resizeTarget, window) ? _resizeEdges : ResizeEdges.None;
        return ReferenceEquals(FindWindow(Pick(MouseX, MouseY)), window) ? HoverResizeEdges : ResizeEdges.None;
    }
```

- [ ] **Step 5: Run the tests to check they pass**

Run the **Verify** command. Expected: all pass, 0 failed.

- [ ] **Step 6: Commit**

```bash
git add src/AcDream.App/UI/UiElement.cs src/AcDream.App/UI/UiRoot.cs tests/AcDream.App.Tests/UI/UiRootResizeHookTests.cs
git commit -m "UI: window hooks to filter resize edges and constrain resize size

Claude-Session: https://claude.ai/code/session_01WFS4EHSNafq2P1qfUuh258"
```

---

### Task 2: `DeckGeometry` replaces `CommandDeckLayout`

**Goal:** An immutable `DeckGeometry(Scale, Stretch)` with size rules replaces the static layout. The deck reads its own `_geometry` instance (still always `Default`), with no visible change.

**Files:**
- Rename: `src/AcDream.App/UI/Hud/CommandDeckLayout.cs` → `src/AcDream.App/UI/Hud/DeckGeometry.cs` (`git mv`)
- Modify: `src/AcDream.App/UI/Hud/UiCommandDeck.cs`, `src/AcDream.App/UI/Hud/UiCommandDeck.Draw.cs`, `src/AcDream.App/UI/RetailUiRuntime.cs:3398-3399`
- Rename: `tests/AcDream.App.Tests/UI/Hud/CommandDeckLayoutTests.cs` → `tests/AcDream.App.Tests/UI/Hud/DeckGeometryTests.cs` (`git mv`)
- Modify: `tests/AcDream.App.Tests/UI/Hud/UiCommandDeckTests.cs`, `UiCommandDeckDrawTests.cs`, `UiCommandDeckDragTests.cs`

**Acceptance Criteria:**
- [ ] `DeckGeometry.Default` rectangles equal today's values exactly (listed in the test below).
- [ ] At every scale in {0.75, 1, 1.5} × stretch in {1, 1.5, 2}: every rectangle sits inside `(0, 0, Width, Height)`, H/M/L and Recast end at the content right edge, the clickable rectangles do not overlap, and the bars, jump track, power track and casting text grow with stretch.
- [ ] `SnapScale`: 0.75/1.5 limits, 0.05 steps, 0.97–1.03 → 1.0, NaN → 1.0. `FromSize` limits invalid sizes and lowers the scale when the width is too narrow. `WithWidth` keeps the scale; `WithCornerSize` keeps the stretch.
- [ ] No `CommandDeckLayout` reference remains in `src` or `tests`.
- [ ] Every existing deck test passes unchanged in meaning.

**Verify:** `MAC_TEST_FILTER='FullyQualifiedName~AcDream.App.Tests.UI.Hud' ~/code/OpenAC/test-baseline/run-mac-tests.sh ~/code/OpenAC/OpenAC-deck-resize` → all pass, 0 failed; `grep -rn CommandDeckLayout src tests` → no output.

**Steps:**

- [ ] **Step 1: Rename the files**

```bash
git mv src/AcDream.App/UI/Hud/CommandDeckLayout.cs src/AcDream.App/UI/Hud/DeckGeometry.cs
git mv tests/AcDream.App.Tests/UI/Hud/CommandDeckLayoutTests.cs tests/AcDream.App.Tests/UI/Hud/DeckGeometryTests.cs
```

- [ ] **Step 2: Write the failing tests**

Replace the contents of `tests/AcDream.App.Tests/UI/Hud/DeckGeometryTests.cs` with the following. It keeps every existing test (now on `DeckGeometry.Default`) and adds the geometry and size-rule tests.

```csharp
using AcDream.App.UI.Hud;
using AcDream.Core.Combat;

namespace AcDream.App.Tests.UI.Hud;

public sealed class DeckGeometryTests
{
    private static readonly DeckGeometry G = DeckGeometry.Default;

    private static CommandDeckView View(CombatMode mode) =>
        new CommandDeckModel().Update(default(CommandDeckInput) with { Mode = mode, SessionKey = 1 }, 0);

    private static (float X, float Y) Centre(DeckRect r) => (r.X + r.W / 2f, r.Y + r.H / 2f);

    public static TheoryData<float, float> Sizes()
    {
        var data = new TheoryData<float, float>();
        foreach (float scale in new[] { 0.75f, 1f, 1.5f })
            foreach (float stretch in new[] { 1f, 1.5f, 2f })
                data.Add(scale, stretch);
        return data;
    }

    [Fact]
    public void DefaultMatchesTheOriginalLayout()
    {
        Assert.Equal((420f, 150f), (G.Width, G.Height));
        Assert.Equal(new DeckRect(70f, 4f, 350f, 142f), G.Frame);
        Assert.Equal(new DeckRect(32f, 29f, 92f, 92f), G.Orb);
        Assert.Equal(new DeckRect(134f, 16f, 274f, 20f), G.HealthBar);
        Assert.Equal(new DeckRect(134f, 41f, 274f, 20f), G.StaminaBar);
        Assert.Equal(new DeckRect(134f, 66f, 274f, 20f), G.ManaBar);
        Assert.Equal(new DeckRect(134f, 92f, 274f, 22f), G.ContextRow);
        Assert.Equal(new DeckRect(134f, 92f, 60f, 22f), G.RowLabel);
        Assert.Equal(new DeckRect(200f, 98f, 118f, 10f), G.PowerTrack);
        Assert.Equal(new DeckRect(326f, 92f, 26f, 22f), G.HeightButton(0));
        Assert.Equal(new DeckRect(382f, 92f, 26f, 22f), G.HeightButton(2));
        Assert.Equal(new DeckRect(382f, 92f, 26f, 22f), G.Recast);
        Assert.Equal(new DeckRect(200f, 92f, 176f, 22f), G.CastingText);
        Assert.Equal(new DeckRect(260f, 92f, 26f, 22f), G.RecallStud(2));
        Assert.Equal(new DeckRect(134f, 122f, 60f, 14f), G.JumpLabel);
        Assert.Equal(new DeckRect(200f, 125f, 208f, 8f), G.JumpTrack);
        Assert.Equal(new DeckRect(2f, 70f, 26f, 22f), G.Stud(1));
    }

    [Theory]
    [MemberData(nameof(Sizes))]
    public void EveryRectInside(float scale, float stretch)
    {
        DeckGeometry g = DeckGeometry.Create(scale, stretch);
        foreach (DeckRect r in g.AllRects())
        {
            Assert.True(r.X >= 0 && r.Y >= 0, r.ToString());
            Assert.True(r.X + r.W <= g.Width + 0.01f, r.ToString());
            Assert.True(r.Y + r.H <= g.Height + 0.01f, r.ToString());
        }
    }

    [Theory]
    [MemberData(nameof(Sizes))]
    public void RightHandControlsStayAgainstTheRightEdge(float scale, float stretch)
    {
        DeckGeometry g = DeckGeometry.Create(scale, stretch);
        float right = g.HealthBar.X + g.HealthBar.W;
        Assert.Equal(right, g.HeightButton(2).X + g.HeightButton(2).W, 2);
        Assert.Equal(right, g.Recast.X + g.Recast.W, 2);
        Assert.Equal(right, g.JumpTrack.X + g.JumpTrack.W, 2);
        Assert.Equal(g.Width - 12f * g.Scale, right, 2);
        Assert.True(g.PowerTrack.X + g.PowerTrack.W < g.HeightButton(0).X);
        Assert.True(g.CastingText.X + g.CastingText.W < g.Recast.X);
    }

    [Theory]
    [MemberData(nameof(Sizes))]
    public void ControlsDoNotOverlap(float scale, float stretch)
    {
        DeckGeometry g = DeckGeometry.Create(scale, stretch);
        DeckRect[] power = [g.PowerTrack, g.HeightButton(0), g.HeightButton(1), g.HeightButton(2)];
        DeckRect[] recall = [g.RecallStud(0), g.RecallStud(1), g.RecallStud(2), g.RecallStud(3), g.RecallStud(4)];
        DeckRect[] studs = [g.Stud(0), g.Stud(1), g.Stud(2)];
        foreach (DeckRect[] group in new[] { power, recall, studs, [g.CastingText, g.Recast] })
            for (int i = 0; i < group.Length; i++)
                for (int j = i + 1; j < group.Length; j++)
                    Assert.False(Overlaps(group[i], group[j]), $"{group[i]} / {group[j]}");
    }

    private static bool Overlaps(DeckRect a, DeckRect b) =>
        a.X < b.X + b.W && b.X < a.X + a.W && a.Y < b.Y + b.H && b.Y < a.Y + a.H;

    [Fact]
    public void StretchLengthensTheBarsTracksAndCastingLine_NotTheFixedParts()
    {
        DeckGeometry wide = DeckGeometry.Create(1f, 2f);
        Assert.Equal(840f, wide.Width);
        Assert.Equal(150f, wide.Height);
        Assert.Equal(G.HealthBar.W + 420f, wide.HealthBar.W);
        Assert.Equal(G.JumpTrack.W + 420f, wide.JumpTrack.W);
        Assert.Equal(G.PowerTrack.W + 420f, wide.PowerTrack.W);
        Assert.Equal(G.CastingText.W + 420f, wide.CastingText.W);
        Assert.Equal(G.Orb, wide.Orb);
        Assert.Equal(G.RecallStud(4), wide.RecallStud(4));
        Assert.Equal(G.Stud(0), wide.Stud(0));
        Assert.Equal(G.RowLabel, wide.RowLabel);
        Assert.Equal(G.Recast.W, wide.Recast.W);
    }

    [Fact]
    public void ScaleMultipliesEverything()
    {
        DeckGeometry big = DeckGeometry.Create(1.5f, 1f);
        Assert.Equal((630f, 225f), (big.Width, big.Height));
        Assert.Equal(new DeckRect(48f, 43.5f, 138f, 138f), big.Orb);
        Assert.Equal(G.PowerTrack.W * 1.5f, big.PowerTrack.W, 3);
    }

    [Theory]
    [InlineData(0.5f, 0.75f)]
    [InlineData(0.77f, 0.75f)]
    [InlineData(0.83f, 0.85f)]
    [InlineData(0.97f, 1f)]
    [InlineData(1.03f, 1f)]
    [InlineData(1.04f, 1.05f)]
    [InlineData(1.26f, 1.25f)]
    [InlineData(2f, 1.5f)]
    [InlineData(float.NaN, 1f)]
    public void SnapScale(float raw, float expected) =>
        Assert.Equal(expected, DeckGeometry.SnapScale(raw), 3);

    [Fact]
    public void FromSize_ReadsScaleFromHeightAndStretchFromWidth()
    {
        DeckGeometry g = DeckGeometry.FromSize(630f, 187.5f);
        Assert.Equal(1.25f, g.Scale, 3);
        Assert.Equal(1.2f, g.Stretch, 3);
        Assert.Equal((630f, 187.5f), (g.Width, g.Height));
    }

    [Theory]
    [InlineData(5000f, 150f, 1f, 2f)]        // too wide: stretch limited
    [InlineData(100f, 150f, 0.75f, 1f)]      // too narrow even at the smallest scale
    [InlineData(399f, 150f, 0.95f, 1f)]      // narrow width lowers the scale
    [InlineData(420f, 9999f, 1f, 1f)]        // too tall: 1.5 does not fit 420 wide, so 1.0
    [InlineData(float.NaN, float.NaN, 1f, 1f)]
    public void FromSize_LimitsInvalidSizes(float width, float height, float scale, float stretch)
    {
        DeckGeometry g = DeckGeometry.FromSize(width, height);
        Assert.Equal(scale, g.Scale, 3);
        Assert.Equal(stretch, g.Stretch, 3);
    }

    [Fact]
    public void WithWidth_KeepsTheScale()
    {
        DeckGeometry g = DeckGeometry.Create(1.25f, 1f).WithWidth(700f);
        Assert.Equal(1.25f, g.Scale, 3);
        Assert.Equal(700f, g.Width, 2);
        Assert.Equal(187.5f, g.Height, 2);
    }

    [Fact]
    public void WithCornerSize_KeepsTheStretch_AndFollowsTheAxisThatMovedMost()
    {
        DeckGeometry start = DeckGeometry.Create(1f, 1.5f);   // 630 x 150
        DeckGeometry byHeight = start.WithCornerSize(632f, 195f);
        Assert.Equal(1.3f, byHeight.Scale, 3);
        Assert.Equal(1.5f, byHeight.Stretch, 3);

        DeckGeometry byWidth = start.WithCornerSize(787.5f, 151f);
        Assert.Equal(1.25f, byWidth.Scale, 3);
        Assert.Equal(1.5f, byWidth.Stretch, 3);
    }

    [Theory]
    [InlineData(CombatMode.NonCombat)]
    [InlineData(CombatMode.Melee)]
    [InlineData(CombatMode.Magic)]
    public void OrbInEveryMode(CombatMode mode)
    {
        var (x, y) = Centre(G.Orb);
        Assert.Equal(DeckZone.Orb, G.ZoneAt(View(mode), x, y));
    }

    [Theory]
    [MemberData(nameof(Sizes))]
    public void RowZonesFollowTheRow(float scale, float stretch)
    {
        DeckGeometry g = DeckGeometry.Create(scale, stretch);
        var (hx, hy) = Centre(g.HeightButton(0));
        var (rx, ry) = Centre(g.Recast);
        var (lx, ly) = Centre(g.RecallStud(2));

        Assert.Equal(DeckZone.High, g.ZoneAt(View(CombatMode.Melee), hx, hy));
        Assert.Equal(DeckZone.Recast, g.ZoneAt(View(CombatMode.Magic), rx, ry));
        Assert.Equal(DeckZone.Recall2, g.ZoneAt(View(CombatMode.NonCombat), lx, ly));

        Assert.NotEqual(DeckZone.High, g.ZoneAt(View(CombatMode.Magic), hx, hy));
        Assert.NotEqual(DeckZone.Recast, g.ZoneAt(View(CombatMode.NonCombat), rx, ry));
        Assert.NotEqual(DeckZone.Recall2, g.ZoneAt(View(CombatMode.Melee), lx, ly));
    }

    [Fact]
    public void StudsOnlyInCombat()
    {
        var (x, y) = Centre(G.Stud(0));
        Assert.Equal(DeckZone.StudRepeat, G.ZoneAt(View(CombatMode.Melee), x, y));
        Assert.Equal(DeckZone.None, G.ZoneAt(View(CombatMode.NonCombat), x, y));
    }

    [Fact]
    public void FrameEdgeIsNone()
    {
        DeckRect f = G.Frame;
        Assert.Equal(DeckZone.None, G.ZoneAt(View(CombatMode.Melee), f.X + f.W - 4, f.Y + 4));
    }

    [Theory]
    [MemberData(nameof(Sizes))]
    public void PowerAtMapsTheTrack(float scale, float stretch)
    {
        DeckGeometry g = DeckGeometry.Create(scale, stretch);
        DeckRect t = g.PowerTrack;
        Assert.Equal(0f, g.PowerAt(t.X - 10));
        Assert.Equal(1f, g.PowerAt(t.X + t.W + 10));
        Assert.Equal(0.5f, g.PowerAt(t.X + t.W / 2f), 3);
    }

    [Fact]
    public void RecastIsAnIconButtonAndTheCastingLineEndsBeforeIt()
    {
        DeckRect recast = G.Recast, text = G.CastingText;
        Assert.Equal(G.RecallStud(0).W, recast.W);
        Assert.True(text.X + text.W < recast.X);
        Assert.True(text.W >= 170f);
    }
}
```

These are all the old tests: `FixedSize_AndEveryRectInside` is replaced by `DefaultMatchesTheOriginalLayout` + `EveryRectInside`, and the rest are carried over on `G`.

- [ ] **Step 3: Run the tests to check they fail**

Run: `MAC_TEST_FILTER='FullyQualifiedName~DeckGeometryTests' ~/code/OpenAC/test-baseline/run-mac-tests.sh ~/code/OpenAC/OpenAC-deck-resize`
Expected: build FAILS — `DeckGeometry` not found.

- [ ] **Step 4: Write `DeckGeometry`**

Replace the static class in `src/AcDream.App/UI/Hud/DeckGeometry.cs` (keep `DeckRect` and `DeckZone` above it unchanged) with:

```csharp
/// <summary>
/// The deck's geometry in points for one size. <see cref="Scale"/> multiplies
/// everything; <see cref="Stretch"/> widens the content right of the orb: the
/// bars, the tracks and the casting line get longer while the orb, studs,
/// labels and buttons keep their size. Nothing here depends on the combat
/// mode, so a mode change never changes the window's size.
/// </summary>
public sealed record DeckGeometry
{
    public const float BaseWidth = 420f;
    public const float BaseHeight = 150f;
    public const float MinScale = 0.75f;
    public const float MaxScale = 1.5f;
    public const float ScaleStep = 0.05f;
    /// <summary>A scale this close to 1 snaps to exactly 1.</summary>
    public const float LockToOne = 0.03f;
    public const float MinStretch = 1f;
    public const float MaxStretch = 2f;

    public static DeckGeometry Default { get; } = new(1f, 1f);

    public float Scale { get; }
    public float Stretch { get; }

    private DeckGeometry(float scale, float stretch)
    {
        Scale = scale;
        Stretch = stretch;
    }

    public float Width => BaseWidth * Stretch * Scale;
    public float Height => BaseHeight * Scale;

    /// <summary>A geometry with the scale snapped and the stretch limited.</summary>
    public static DeckGeometry Create(float scale, float stretch) =>
        new(SnapScale(scale), Math.Clamp(float.IsFinite(stretch) ? stretch : MinStretch, MinStretch, MaxStretch));

    /// <summary>A scale limited to the range, locked onto 1 when close, else on a 0.05 step.</summary>
    public static float SnapScale(float raw)
    {
        if (!float.IsFinite(raw)) return 1f;
        float clamped = Math.Clamp(raw, MinScale, MaxScale);
        if (MathF.Abs(clamped - 1f) <= LockToOne) return 1f;
        return MathF.Round(MathF.Round(clamped / ScaleStep) * ScaleStep, 2);
    }

    /// <summary>The geometry for a window size: the scale from the height, the
    /// stretch from the width. A width too narrow for that scale lowers the
    /// scale to the largest step that fits.</summary>
    public static DeckGeometry FromSize(float width, float height)
    {
        if (!float.IsFinite(width) || !float.IsFinite(height)) return Default;
        float scale = SnapScale(height / BaseHeight);
        if (BaseWidth * scale > width + 0.5f)
            scale = Math.Max(MinScale, MathF.Floor(width / BaseWidth / ScaleStep + 1e-4f) * ScaleStep);
        return Create(scale, width / (BaseWidth * scale));
    }

    /// <summary>A side-edge resize: the same scale, the width decides the stretch.</summary>
    public DeckGeometry WithWidth(float width) => Create(Scale, width / (BaseWidth * Scale));

    /// <summary>A corner resize: whichever axis moved further from the current
    /// size decides the scale; the stretch is kept.</summary>
    public DeckGeometry WithCornerSize(float width, float height)
    {
        float fromWidth = width / (BaseWidth * Stretch);
        float fromHeight = height / BaseHeight;
        float raw = MathF.Abs(fromWidth - Scale) > MathF.Abs(fromHeight - Scale) ? fromWidth : fromHeight;
        return Create(raw, Stretch);
    }

    // Layout in unscaled points; every public rectangle goes through S().
    private const float ContentLeft = 134f;
    private const float LabelWidth = 60f;
    private const float RowValueLeft = ContentLeft + LabelWidth + 6f;   // 200
    private float Extra => BaseWidth * (Stretch - 1f);
    private float ContentRight => 408f + Extra;
    private float HeightButtonX(int index) => ContentRight - 82f + index * 28f;

    private DeckRect S(float x, float y, float w, float h) => new(x * Scale, y * Scale, w * Scale, h * Scale);

    public DeckRect Frame => S(70f, 4f, 350f + Extra, 142f);
    public DeckRect Orb => S(32f, 29f, 92f, 92f);

    public DeckRect HealthBar => S(ContentLeft, 16f, ContentRight - ContentLeft, 20f);
    public DeckRect StaminaBar => S(ContentLeft, 41f, ContentRight - ContentLeft, 20f);
    public DeckRect ManaBar => S(ContentLeft, 66f, ContentRight - ContentLeft, 20f);

    public DeckRect ContextRow => S(ContentLeft, 92f, ContentRight - ContentLeft, 22f);
    public DeckRect RowLabel => S(ContentLeft, 92f, LabelWidth, 22f);

    public DeckRect PowerTrack => S(RowValueLeft, 98f, HeightButtonX(0) - 8f - RowValueLeft, 10f);
    public DeckRect HeightButton(int index) => S(HeightButtonX(index), 92f, 26f, 22f);

    public DeckRect Recast => S(ContentRight - 26f, 92f, 26f, 22f);
    public DeckRect CastingText => S(RowValueLeft, 92f, ContentRight - 26f - 6f - RowValueLeft, 22f);

    public DeckRect RecallStud(int index) => S(RowValueLeft + index * 30f, 92f, 26f, 22f);

    public DeckRect JumpLabel => S(ContentLeft, 122f, LabelWidth, 14f);
    public DeckRect JumpTrack => S(RowValueLeft, 125f, ContentRight - RowValueLeft, 8f);

    public DeckRect Stud(int index) => S(2f, 44f + index * 26f, 26f, 22f);
```

Then move the remaining members of the old static class into this record as **instance** members, unchanged except for removing `static`:
`AllRects()`, `RectFor(DeckZone)` (stays `internal`), `PowerAt(float)`, `ZoneAt(CommandDeckView, float, float)`, `InOrb(float, float)`. `InCircle(DeckRect, float, float)` can stay `private static`. Close the record with `}`.

- [ ] **Step 5: Move the deck onto its geometry instance**

In `src/AcDream.App/UI/Hud/UiCommandDeck.cs`:

1. Add a field after `_fonts`:
   ```csharp
    private DeckGeometry _geometry = DeckGeometry.Default;
   ```
2. Add after `internal CommandDeckView View => _view;`:
   ```csharp
    internal DeckGeometry Geometry => _geometry;
   ```
3. In the constructor replace `CommandDeckLayout.Width` / `.Height` with `_geometry.Width` / `_geometry.Height`.
4. Replace every remaining `CommandDeckLayout.` with `_geometry.` (`RectFor`, `ZoneAt`, `Frame`, `InOrb`, `PowerAt`).

In `src/AcDream.App/UI/Hud/UiCommandDeck.Draw.cs`:

1. `DrawFrame` becomes an instance method (remove `static`).
2. Replace every `CommandDeckLayout.` with `_geometry.`.
3. `FitCastingText` takes the width, since it is static:
   ```csharp
    /// <summary>The spell name, shortened with "..." so it stops short of the RECAST button;
    /// the button's tooltip carries the full name.</summary>
    internal static string FitCastingText(string text, Func<string, float> measure, float width) =>
        PluginWindowChrome.Ellipsize(text, measure, width);
   ```
   and its caller in `DrawCastingRow` passes `text.W` (the local `DeckRect text = _geometry.CastingText;`).

In `src/AcDream.App/UI/RetailUiRuntime.cs` (`MountCommandDeck`), replace `Hud.CommandDeckLayout.Width` / `.Height` with `Hud.DeckGeometry.Default.Width` / `.Height`.

- [ ] **Step 6: Move the existing deck tests**

In `UiCommandDeckTests.cs`, `UiCommandDeckDrawTests.cs` and `UiCommandDeckDragTests.cs`, replace `CommandDeckLayout.` with `DeckGeometry.Default.`. In `UiCommandDeckDrawTests.LongCastingTextIsShortenedToStopBeforeRecast`, pass the width:

```csharp
        float room = DeckGeometry.Default.Recast.X - DeckGeometry.Default.CastingText.X - 6f;
        float width = DeckGeometry.Default.CastingText.W;

        string fitted = UiCommandDeck.FitCastingText("Incantation of Coordination", Measure, width);

        Assert.EndsWith("...", fitted);
        Assert.True(Measure(fitted) <= room);
        Assert.Equal("Flame Bolt", UiCommandDeck.FitCastingText("Flame Bolt", Measure, width));
```

Then check that nothing still refers to the old name:

```bash
grep -rn CommandDeckLayout src tests
```
Expected: no output.

- [ ] **Step 7: Run the tests to check they pass**

Run the **Verify** command. Expected: all pass, 0 failed. If `FromSize_LimitsInvalidSizes` fails on a float step (for example 0.95 coming out as 0.9), check the `1e-4f` epsilon in `FromSize` before changing the test.

- [ ] **Step 8: Commit**

```bash
git add -A src/AcDream.App/UI/Hud src/AcDream.App/UI/RetailUiRuntime.cs tests/AcDream.App.Tests/UI/Hud
git commit -m "Command Deck: DeckGeometry (scale + stretch) replaces the static layout

Claude-Session: https://claude.ai/code/session_01WFS4EHSNafq2P1qfUuh258"
```

---

### Task 3: Draw at the geometry's scale; fonts per scale step

**Goal:** Every drawn detail (corner radii, offsets, icon sizes, line weights, text) follows `_geometry.Scale`, and the deck's text is baked at the scaled pixel size.

**Files:**
- Modify: `src/AcDream.App/UI/Hud/DeckFonts.cs`
- Modify: `src/AcDream.App/UI/Hud/UiCommandDeck.Draw.cs`
- Modify: `src/AcDream.App/UI/Hud/UiCommandDeck.cs` (test-only geometry setter)
- Test: `tests/AcDream.App.Tests/UI/Hud/DeckFontsTests.cs` (create), `tests/AcDream.App.Tests/UI/Hud/UiCommandDeckDrawTests.cs`

**Acceptance Criteria:**
- [ ] `DeckFonts.At(scale)` bakes each face once per scale step, at `round(14 × scale)` / `round(12 × scale)` px (SemiBold at the 14 px size), and only when a face is first used.
- [ ] `DeckFonts.Text` / `Small` / `Bold` still return the scale-1 faces (tooltips unchanged).
- [ ] At scale 1.5 / stretch 2 every mode draws only inside the deck's bounds (same check as the existing `DrawsEveryModeInsideTheDeck`).
- [ ] At scale 1 / stretch 1 the drawn vertices are identical to before this task.

**Verify:** `MAC_TEST_FILTER='FullyQualifiedName~AcDream.App.Tests.UI.Hud' ~/code/OpenAC/test-baseline/run-mac-tests.sh ~/code/OpenAC/OpenAC-deck-resize` → all pass, 0 failed.

**Steps:**

- [ ] **Step 1: Capture the scale-1 baseline (for the "identical" criterion)**

Before editing, add this test to `UiCommandDeckDrawTests` and run it once. It writes the current vertices to a file in the test output:

```csharp
    [Theory]
    [InlineData(CombatMode.NonCombat)]
    [InlineData(CombatMode.Melee)]
    [InlineData(CombatMode.Magic)]
    public void ScaleOneDrawsTheSameAsBefore(CombatMode mode)
    {
        var input = default(CommandDeckInput) with
        {
            Mode = mode, DesiredPower = 0.5f, PowerBarLevel = 0.3f, SessionKey = 1,
            Health = new(0.2f, 20, 100), Stamina = new(0.6f, 60, 100), Mana = new(1f, 100, 100),
            LastSpellName = "Flame Bolt", RepeatAttacks = true,
        };
        var deck = new UiCommandDeck(() => input, new CommandDeckModel(), new NoActions(), icons: null, clock: () => 1.0);
        deck.Refresh();
        var all = new List<float>();
        foreach (var s in MarkupDrawProbe.Draw(deck, deck.Width, deck.Height).DebugSpriteSegmentVerts)
            all.AddRange(s.Verts);

        string file = Path.Combine(AppContext.BaseDirectory, $"deck-scale1-{mode}.txt");
        string text = string.Join(",", all.Select(v => v.ToString("R", CultureInfo.InvariantCulture)));
        if (!File.Exists(file)) { File.WriteAllText(file, text); return; }
        Assert.Equal(File.ReadAllText(file), text);
    }
```

Run: `MAC_TEST_FILTER='FullyQualifiedName~ScaleOneDrawsTheSameAsBefore' ~/code/OpenAC/test-baseline/run-mac-tests.sh ~/code/OpenAC/OpenAC-deck-resize`
Expected: PASS (the baseline files get written). Add `using System.Globalization;` if the file doesn't have it. **Do not rebuild clean between this step and Step 6**, or the baseline is lost. Remove this test in Step 7 once it has passed against the new code.

- [ ] **Step 2: Write the failing font test**

Create `tests/AcDream.App.Tests/UI/Hud/DeckFontsTests.cs`:

```csharp
using AcDream.App.UI;
using AcDream.App.UI.Hud;

namespace AcDream.App.Tests.UI.Hud;

public sealed class DeckFontsTests
{
    [Theory]
    [InlineData(14f, 1f, 14f)]
    [InlineData(14f, 0.75f, 11f)]   // 10.5 rounds away from zero
    [InlineData(12f, 0.75f, 9f)]
    [InlineData(14f, 1.5f, 21f)]
    [InlineData(12f, 1.25f, 15f)]
    public void PixelHeightRoundsTheScaledSize(float basePx, float scale, float expected) =>
        Assert.Equal(expected, DeckFonts.PixelHeight(basePx, scale));

    [Fact]
    public void BakesEachFaceOncePerScale_OnlyWhenUsed()
    {
        var baked = new List<(float Px, BundledUiFontWeight Weight)>();
        var fonts = new DeckFonts((px, weight) => { baked.Add((px, weight)); return null!; });

        DeckFontSet big = fonts.At(1.5f);
        Assert.Empty(baked);

        _ = big.Text; _ = big.Text; _ = big.Small;
        Assert.Equal(new[] { (21f, BundledUiFontWeight.Regular), (18f, BundledUiFontWeight.Regular) }, baked);

        Assert.Same(big, fonts.At(1.5f));
        _ = fonts.Bold;
        Assert.Equal((14f, BundledUiFontWeight.SemiBold), baked[^1]);
    }
}
```

- [ ] **Step 3: Run the test to check it fails**

Run: `MAC_TEST_FILTER='FullyQualifiedName~DeckFontsTests' ~/code/OpenAC/test-baseline/run-mac-tests.sh ~/code/OpenAC/OpenAC-deck-resize`
Expected: build FAILS — `DeckFonts.PixelHeight`, `At`, `DeckFontSet` not found.

- [ ] **Step 4: Rewrite `DeckFonts`**

Replace the `DeckFonts` class in `src/AcDream.App/UI/Hud/DeckFonts.cs` (keep `DeckFace` below it unchanged) with:

```csharp
/// <summary>
/// The Modern HUD's fonts: the bundled sans, baked on first use at each deck
/// scale it is drawn at. Without them (tests, or before textures exist) the
/// context's default font stands in.
/// </summary>
internal sealed class DeckFonts(Func<float, BundledUiFontWeight, UiDatFont> bake)
{
    /// <summary>Values: vital numbers, tooltip body.</summary>
    public const float TextPixelHeight = 14f;
    /// <summary>Labels and the casting line, a size below the values.</summary>
    public const float SmallPixelHeight = 12f;

    private readonly Dictionary<float, DeckFontSet> _sets = [];

    public static DeckFonts Load(TextureCache textures) =>
        new((pixelHeight, weight) => BundledUiFont.Load(textures, pixelHeight, weight));

    /// <summary>The faces at scale 1, used by the tooltips.</summary>
    public UiDatFont Text => At(1f).Text;
    public UiDatFont Small => At(1f).Small;
    public UiDatFont Bold => At(1f).Bold;

    /// <summary>The faces for a deck drawn at <paramref name="scale"/>; each bakes when first used.</summary>
    public DeckFontSet At(float scale)
    {
        if (!_sets.TryGetValue(scale, out DeckFontSet? set))
        {
            float text = PixelHeight(TextPixelHeight, scale), small = PixelHeight(SmallPixelHeight, scale);
            _sets[scale] = set = new DeckFontSet(
                new(() => bake(text, BundledUiFontWeight.Regular)),
                new(() => bake(small, BundledUiFontWeight.Regular)),
                new(() => bake(text, BundledUiFontWeight.SemiBold)));
        }
        return set;
    }

    /// <summary>A base pixel height at a deck scale, in whole pixels.</summary>
    internal static float PixelHeight(float basePixels, float scale) =>
        MathF.Round(basePixels * scale, MidpointRounding.AwayFromZero);
}

/// <summary>The deck's three faces at one scale.</summary>
internal sealed class DeckFontSet(Lazy<UiDatFont> text, Lazy<UiDatFont> small, Lazy<UiDatFont> bold)
{
    public UiDatFont Text => text.Value;
    public UiDatFont Small => small.Value;
    public UiDatFont Bold => bold.Value;
}
```

(The scale keys are always snapped 0.05 steps from `DeckGeometry`, so at most 16 sets ever exist.)

- [ ] **Step 5: Scale the drawing**

In `src/AcDream.App/UI/Hud/UiCommandDeck.Draw.cs`, add after the colour constants:

```csharp
    /// <summary>A length in base points at the deck's scale.</summary>
    private float P(float points) => points * _geometry.Scale;

    /// <summary>A line weight at the deck's scale, in whole points, never under one.</summary>
    private float Line(float points) => MathF.Max(1f, MathF.Round(points * _geometry.Scale));
```

Replace the drawing methods from `DrawFrame` through `DrawIcon` with the following (same shapes and colours; every literal length goes through `P` or `Line`):

```csharp
    private void DrawFrame(UiRenderContext ctx)
    {
        DeckRect f = _geometry.Frame;
        ctx.FillRoundedRect(f.X - P(2), f.Y - P(2), f.W + P(4), f.H + P(4), P(10), FrameEdge);
        ctx.FillVerticalGradient(f.X, f.Y, f.W, f.H, new CanvasCornerRadii(P(8), P(8), P(8), P(8)), FrameTop, FrameBottom);
        ctx.StrokeRoundedRect(f.X + P(3), f.Y + P(3), f.W - P(6), f.H - P(6), P(5), Bronze, Line(1));
    }

    private void DrawBar(UiRenderContext ctx, DeckRect r, string label, DeckVitalView vital,
        (Vector4 Top, Vector4 Bottom) ink, float pulse)
    {
        ctx.FillRoundedRect(r.X, r.Y, r.W, r.H, P(4), Track);
        if (vital.Known)
        {
            if (vital.Trail > vital.Fill)
                ctx.FillRoundedRect(r.X, r.Y, r.W * vital.Trail, r.H, P(3), TrailInk);
            if (vital.Fill > 0f)
                ctx.FillVerticalGradient(r.X, r.Y, r.W * vital.Fill, r.H,
                    new CanvasCornerRadii(P(3), P(3), P(3), P(3)), ink.Top, ink.Bottom);
            if (pulse > 0f)
                ctx.FillRoundedRect(r.X, r.Y, r.W * vital.Fill, r.H, P(3), new Vector4(1f, 1f, 1f, pulse * 0.4f));
        }
        DrawText(ctx, label, r.X + P(8), r, LabelInk, small: true);
        DrawTextRight(ctx, vital.Text, r.X + r.W - P(8), r, TextInk);
    }

    private void DrawPowerRow(UiRenderContext ctx, CommandDeckView v)
    {
        DeckRect label = _geometry.RowLabel;
        DrawText(ctx, v.PowerLabel, label.X, label, LabelInk, small: true);

        DeckRect t = _geometry.PowerTrack;
        ctx.FillRoundedRect(t.X, t.Y, t.W, t.H, P(5), Track);
        for (int i = 1; i < 4; i++)
            ctx.DrawFill(t.X + t.W * i / 4f, t.Y, Line(1), t.H, Rgb(0x4A4752));
        if (v.PowerFill > 0f)
            ctx.FillRoundedRect(t.X, t.Y, t.W * v.PowerFill, t.H, P(4), PowerInk);
        float hx = t.X + t.W * v.DesiredPower;
        ctx.FillRoundedRect(hx - P(5), t.Y - P(5), P(10), t.H + P(10), P(3), Handle);

        for (int i = 0; i < 3; i++)
            DrawStud(ctx, _geometry.HeightButton(i), on: v.Height == HeightValues[i], icon: null, letter: HeightLetters[i]);
    }

    private void DrawCastingRow(UiRenderContext ctx, CommandDeckView v)
    {
        DeckRect label = _geometry.RowLabel;
        DrawText(ctx, "CASTING", label.X, label, LabelInk, small: true);
        DeckRect text = _geometry.CastingText;
        DrawText(ctx, FitCastingText(v.CastingText, t => Face(small: true).Measure(ctx, t), text.W), text.X, text,
            v.CastingLit ? CastLit : StudOffInk, small: true);
        DeckRect recast = _geometry.Recast;
        DrawStud(ctx, recast, on: false, icon: HudIcons.Recast, letter: null, dim: !v.RecastEnabled);
    }

    /// <summary>The spell name, shortened with "..." so it stops short of the RECAST button;
    /// the button's tooltip carries the full name.</summary>
    internal static string FitCastingText(string text, Func<string, float> measure, float width) =>
        PluginWindowChrome.Ellipsize(text, measure, width);

    private void DrawRecallRow(UiRenderContext ctx)
    {
        DeckRect label = _geometry.RowLabel;
        DrawText(ctx, "RECALL", label.X, label, LabelInk, small: true);
        for (int i = 0; i < 5; i++)
            DrawStud(ctx, _geometry.RecallStud(i), on: false, icon: RecallIcons[i], letter: null);
    }

    private void DrawJump(UiRenderContext ctx, CommandDeckView v)
    {
        DeckRect label = _geometry.JumpLabel;
        DrawText(ctx, "JUMP", label.X, label, v.JumpCharging ? JumpInk : LabelInk with { W = 0.5f }, small: true);
        DeckRect t = _geometry.JumpTrack;
        ctx.FillRoundedRect(t.X, t.Y, t.W, t.H, P(3), Track);
        if (v.JumpFill > 0f)
            ctx.FillRoundedRect(t.X, t.Y, t.W * v.JumpFill, t.H, P(3), JumpInk);
    }

    private void DrawOrb(UiRenderContext ctx, CommandDeckView v)
    {
        DeckRect o = _geometry.Orb;
        bool combat = v.Orb != DeckOrbMode.Peace;
        ctx.FillEllipse(o.X - P(6), o.Y - P(6), o.W + P(12), o.H + P(12), combat ? CombatGlow : PeaceGlow);
        ctx.FillEllipse(o.X - P(2), o.Y - P(2), o.W + P(4), o.H + P(4), FrameEdge);
        ctx.FillEllipse(o.X, o.Y, o.W, o.H, Rgb(0x6B5A3A));
        ctx.FillEllipse(o.X + P(3), o.Y + P(3), o.W - P(6), o.H - P(6), Rgb(0x0B0C12));
        ctx.FillEllipse(o.X + P(8), o.Y + P(6), o.W - P(24), o.H - P(24), Rgb(0x2A2D3C, 0.9f));
        ctx.FillEllipse(o.X + P(22), o.Y + P(12), P(22), P(12), new Vector4(1f, 1f, 1f, 0.12f));

        string icon = v.Orb switch
        {
            DeckOrbMode.Melee => HudIcons.Melee,
            DeckOrbMode.Missile => HudIcons.Missile,
            DeckOrbMode.Magic => HudIcons.Magic,
            _ => HudIcons.Peace,
        };
        DrawIcon(ctx, icon, o.X + o.W / 2 - P(16), o.Y + o.H / 2 - P(22), P(32), TextInk);
        string label = v.Orb switch
        {
            DeckOrbMode.Melee => "MELEE",
            DeckOrbMode.Missile => "MISSILE",
            DeckOrbMode.Magic => "MAGIC",
            _ => "PEACE",
        };
        DrawTextCentered(ctx, label, o.X + o.W / 2, new DeckRect(o.X, o.Y + o.H / 2 + P(12), o.W, P(16)), LabelInk, small: true);
    }

    private void DrawStuds(UiRenderContext ctx, CommandDeckView v)
    {
        DrawStud(ctx, _geometry.Stud(0), v.RepeatAttacks, HudIcons.Repeat, null);
        DrawStud(ctx, _geometry.Stud(1), v.AutoTarget, HudIcons.AutoTarget, null);
        DrawStud(ctx, _geometry.Stud(2), v.KeepInView, HudIcons.KeepInView, null);
    }

    private void DrawStud(UiRenderContext ctx, DeckRect r, bool on, string? icon, string? letter, bool dim = false)
    {
        ctx.FillRoundedRect(r.X - P(1), r.Y - P(1), r.W + P(2), r.H + P(2), P(6), FrameEdge);
        ctx.FillRoundedRect(r.X, r.Y, r.W, r.H, P(5), on ? StudOn : StudOff);
        Vector4 ink = on ? TextInk : StudOffInk;
        if (dim) ink = ink with { W = 0.4f };
        if (icon is not null)
            DrawIcon(ctx, icon, r.X + (r.W - P(14)) / 2, r.Y + (r.H - P(14)) / 2, P(14), ink);
        else if (letter is not null)
            DrawTextCentered(ctx, letter, r.X + r.W / 2, r, ink, small: true);
    }
```

Keep `DrawIcon` as it is (it already takes the size). Replace `Face` with:

```csharp
    private DeckFace Face(bool small)
    {
        if (_fonts is null) return new(null);
        DeckFontSet set = _fonts.At(_geometry.Scale);
        return new(small ? set.Small : set.Text);
    }
```

In `src/AcDream.App/UI/Hud/UiCommandDeck.cs`, add a test-only way to set the geometry. Task 4 replaces it with real resizing; keep it `internal`:

```csharp
    /// <summary>Sets the deck to <paramref name="geometry"/>'s size and geometry.</summary>
    internal void ApplyGeometry(DeckGeometry geometry)
    {
        _geometry = geometry;
        Width = geometry.Width;
        Height = geometry.Height;
    }
```

- [ ] **Step 6: Add the scaled draw test**

In `UiCommandDeckDrawTests.cs`, add a scaled version of `DrawsEveryModeInsideTheDeck`. Copy that test's body and change two lines: after `deck.Refresh();` add `deck.ApplyGeometry(DeckGeometry.Create(1.5f, 2f));`, and draw with `MarkupDrawProbe.Draw(deck, deck.Width, deck.Height)` and bound the vertices by `deck.Width` / `deck.Height`. Name it `DrawsEveryModeInsideTheDeck_Scaled` and give it the same `[InlineData]` rows.

- [ ] **Step 7: Run the tests to check they pass**

Run the **Verify** command. Expected: all pass, including `ScaleOneDrawsTheSameAsBefore` against the Step 1 baseline. Then delete `ScaleOneDrawsTheSameAsBefore` and the `deck-scale1-*.txt` files (they are build output, not checked in), and run the **Verify** command again.

- [ ] **Step 8: Commit**

```bash
git add src/AcDream.App/UI/Hud tests/AcDream.App.Tests/UI/Hud
git commit -m "Command Deck: draw and bake fonts at the geometry's scale

Claude-Session: https://claude.ai/code/session_01WFS4EHSNafq2P1qfUuh258"
```

---

### Task 4: The deck as a resizable window

**Goal:** The mounted deck resizes from its right edge (width) and right corners (scale), keeps its geometry in step with whatever size it is given (drag, restore, reset), and lights the grip being used.

**Files:**
- Modify: `src/AcDream.App/UI/Hud/UiCommandDeck.cs`
- Modify: `src/AcDream.App/UI/Hud/UiCommandDeck.Draw.cs`
- Modify: `src/AcDream.App/UI/RetailUiRuntime.cs` (`MountCommandDeck`, ~line 3390–3405)
- Test: `tests/AcDream.App.Tests/UI/Hud/UiCommandDeckResizeTests.cs` (create)

**Acceptance Criteria:**
- [ ] Dragging the right edge changes only the width (stretch), within 420–840 × scale; the height stays the same.
- [ ] Dragging the top-right or bottom-right corner changes the scale in 0.05 steps (locking onto 1.0), keeps the stretch, and keeps the opposite edges fixed.
- [ ] The left edge, left corners, top edge and bottom edge show no resize cursor and don't resize.
- [ ] Pressing near the right edge inside the frame's 1 px top/bottom band still starts a corner resize (the grip band hit-tests).
- [ ] A deck given any size (restore or reset) moves to `DeckGeometry.FromSize` of that size on the next `Refresh`; 420 × 150 gives `Default`.
- [ ] The mounted deck's window has `Resizable`, `ResizeX`, `ResizeY` = true, `ResizableEdges = Right`, and min/max of 315 × 112.5 / 1260 × 225.
- [ ] While a grip is hovered or dragged, the frame draws extra highlight geometry; with no grip, it draws nothing extra.

**Verify:** `MAC_TEST_FILTER='FullyQualifiedName~AcDream.App.Tests.UI' ~/code/OpenAC/test-baseline/run-mac-tests.sh ~/code/OpenAC/OpenAC-deck-resize` → all pass, 0 failed.

**Steps:**

- [ ] **Step 1: Write the failing tests**

Create `tests/AcDream.App.Tests/UI/Hud/UiCommandDeckResizeTests.cs`:

```csharp
using AcDream.App.UI;
using AcDream.App.UI.Hud;
using AcDream.App.UI.Layout;
using AcDream.Core.Combat;
using AcDream.Core.Net.Messages;
using AcDream.Runtime;

namespace AcDream.App.Tests.UI.Hud;

public sealed class UiCommandDeckResizeTests
{
    private sealed class NoActions : ICommandDeckActions
    {
        public void ToggleCombat() { }
        public void PressAttack(AttackHeight height) { }
        public void ReleaseAttack() { }
        public void SetDesiredPower(float power) { }
        public void SetOption(CharacterOptionId option, bool value) { }
        public void Recall(RuntimePortalCommand command) { }
        public void Recast() { }
    }

    private const float DeckLeft = 100f, DeckTop = 200f;

    /// <summary>A deck mounted the way <c>RetailUiRuntime.MountCommandDeck</c> mounts it.</summary>
    private static (UiRoot Root, UiCommandDeck Deck) Mount()
    {
        var root = new UiRoot { Width = 1600, Height = 900 };
        var input = default(CommandDeckInput) with { Mode = CombatMode.Melee, SessionKey = 1 };
        var deck = new UiCommandDeck(() => input, new CommandDeckModel(), new NoActions(), icons: null, clock: () => 0.0);
        RetailWindowFrame.Mount(root, deck, _ => (0u, 0, 0), UiCommandDeck.MountOptions(DeckLeft, DeckTop) with { Visible = true });
        deck.Refresh();
        return (root, deck);
    }

    private static int X(float local) => (int)(DeckLeft + local);
    private static int Y(float local) => (int)(DeckTop + local);

    [Fact]
    public void MountedDeckIsResizableOverTheFullRange()
    {
        var (_, deck) = Mount();
        Assert.True(deck.Resizable && deck.ResizeX && deck.ResizeY);
        Assert.Equal(ResizeEdges.Right, deck.ResizableEdges);
        Assert.Equal((315f, 112.5f), (deck.MinWidth, deck.MinHeight));
        Assert.Equal((1260f, 225f), (deck.MaxWidth, deck.MaxHeight));
        Assert.Equal((420f, 150f), (deck.Width, deck.Height));
    }

    [Fact]
    public void RightEdgeStretchesTheWidthOnly()
    {
        var (root, deck) = Mount();
        root.OnMouseDown(UiMouseButton.Left, X(418), Y(75));
        root.OnMouseMove(X(418) + 200, Y(75) + 40);
        root.OnMouseUp(UiMouseButton.Left, X(418) + 200, Y(75) + 40);

        Assert.Equal(620f, deck.Width, 1);
        Assert.Equal(150f, deck.Height);
        Assert.Equal(1f, deck.Geometry.Scale);
        Assert.Equal(620f / 420f, deck.Geometry.Stretch, 3);
    }

    [Fact]
    public void RightEdgeStopsAtTwiceTheWidth()
    {
        var (root, deck) = Mount();
        root.OnMouseDown(UiMouseButton.Left, X(418), Y(75));
        root.OnMouseMove(X(418) + 900, Y(75));
        root.OnMouseUp(UiMouseButton.Left, X(418) + 900, Y(75));
        Assert.Equal(840f, deck.Width, 1);
    }

    [Fact]
    public void BottomRightCornerScales_KeepsStretch_AndTopLeftStays()
    {
        var (root, deck) = Mount();
        deck.ApplyGeometry(DeckGeometry.Create(1f, 1.5f));   // 630 x 150
        root.OnMouseDown(UiMouseButton.Left, X(628), Y(148));
        root.OnMouseMove(X(628), Y(148) + 39);               // proposed height 189 -> scale 1.25 (1.26 snaps)
        root.OnMouseUp(UiMouseButton.Left, X(628), Y(148) + 39);

        Assert.Equal(1.25f, deck.Geometry.Scale, 3);
        Assert.Equal(1.5f, deck.Geometry.Stretch, 3);
        Assert.Equal((787.5f, 187.5f), (deck.Width, deck.Height));
        Assert.Equal((DeckLeft, DeckTop), (deck.Left, deck.Top));
    }

    [Fact]
    public void TopRightCornerScales_AndTheBottomStays()
    {
        var (root, deck) = Mount();
        root.OnMouseDown(UiMouseButton.Left, X(418), Y(1));
        root.OnMouseMove(X(418), Y(1) - 60);                  // proposed height 210 -> scale 1.4
        root.OnMouseUp(UiMouseButton.Left, X(418), Y(1) - 60);

        Assert.Equal(1.4f, deck.Geometry.Scale, 3);
        Assert.Equal(DeckTop + 150f, deck.Top + deck.Height, 1);
        Assert.Equal(DeckLeft, deck.Left);
    }

    [Fact]
    public void CornerLocksOntoOneHundredPercent()
    {
        var (root, deck) = Mount();
        deck.ApplyGeometry(DeckGeometry.Create(1.25f, 1f));
        root.OnMouseDown(UiMouseButton.Left, X(deck.Width - 2), Y(deck.Height - 2));
        root.OnMouseMove(X(deck.Width - 2), Y(deck.Height - 2) - 34);   // 153.5 / 150 = 1.023
        root.OnMouseUp(UiMouseButton.Left, X(deck.Width - 2), Y(deck.Height - 2) - 34);
        Assert.Equal(1f, deck.Geometry.Scale);
        Assert.Equal((420f, 150f), (deck.Width, deck.Height));
    }

    [Theory]
    [InlineData(1f, 75f)]     // left edge
    [InlineData(1f, 1f)]      // top-left corner
    [InlineData(1f, 149f)]    // bottom-left corner
    [InlineData(250f, 1f)]    // top edge
    [InlineData(250f, 149f)]  // bottom edge
    public void OtherEdgesDoNotResize(float localX, float localY)
    {
        var (root, deck) = Mount();
        root.OnMouseMove(X(localX), Y(localY));
        Assert.Equal(ResizeEdges.None, root.HoverResizeEdges);

        root.OnMouseDown(UiMouseButton.Left, X(localX), Y(localY));
        root.OnMouseMove(X(localX) - 40, Y(localY) - 40);
        root.OnMouseUp(UiMouseButton.Left, X(localX) - 40, Y(localY) - 40);
        Assert.Equal((420f, 150f), (deck.Width, deck.Height));
    }

    [Theory]
    [InlineData(418f, 1f, ResizeEdges.Right | ResizeEdges.Top)]
    [InlineData(418f, 149f, ResizeEdges.Right | ResizeEdges.Bottom)]
    [InlineData(418f, 75f, ResizeEdges.Right)]
    public void RightGripsHitTestOutsideTheFrame(float localX, float localY, ResizeEdges expected)
    {
        var (root, _) = Mount();
        root.OnMouseMove(X(localX), Y(localY));
        Assert.Equal(expected, root.HoverResizeEdges);
    }

    [Theory]
    [InlineData(630f, 187.5f, 1.25f, 1.2f)]
    [InlineData(420f, 150f, 1f, 1f)]
    [InlineData(5000f, 140f, 0.95f, 2f)]
    public void AnySizeIsCorrectedToItsGeometryOnRefresh(float width, float height, float scale, float stretch)
    {
        var (_, deck) = Mount();
        deck.Width = width;
        deck.Height = height;
        deck.Refresh();
        Assert.Equal(scale, deck.Geometry.Scale, 3);
        Assert.Equal(stretch, deck.Geometry.Stretch, 3);
        Assert.Equal((deck.Geometry.Width, deck.Geometry.Height), (deck.Width, deck.Height));
    }

    [Fact]
    public void GripHighlightDrawsOnlyWhileAGripIsInUse()
    {
        var (root, deck) = Mount();
        static int Count(UiCommandDeck d)
        {
            int n = 0;
            foreach (var s in MarkupDrawProbe.Draw(d, d.Width, d.Height).DebugSpriteSegmentVerts) n += s.Verts.Count;
            return n;
        }

        root.OnMouseMove(X(250), Y(75));
        int idle = Count(deck);
        root.OnMouseMove(X(418), Y(75));
        Assert.True(Count(deck) > idle);
    }
}
```

(`MarkupDrawProbe.Draw` draws the element where it is, still attached to the root, so `FindRoot()` works in the grip highlight test.)

- [ ] **Step 2: Run the tests to check they fail**

Run: `MAC_TEST_FILTER='FullyQualifiedName~UiCommandDeckResizeTests' ~/code/OpenAC/test-baseline/run-mac-tests.sh ~/code/OpenAC/OpenAC-deck-resize`
Expected: build FAILS — `UiCommandDeck.MountOptions` not found.

- [ ] **Step 3: Make the deck resizable**

In `src/AcDream.App/UI/Hud/UiCommandDeck.cs`:

1. Replace the constructor's size lines

   ```csharp
        Width = CommandDeckLayout.Width;   // (now _geometry.Width after Task 2)
        Height = ...;
        MinWidth = MaxWidth = Width;
        MinHeight = MaxHeight = Height;
        Resizable = false;
   ```

   with

   ```csharp
        Width = _geometry.Width;
        Height = _geometry.Height;
        MinWidth = MinWindowWidth;
        MaxWidth = MaxWindowWidth;
        MinHeight = MinWindowHeight;
        MaxHeight = MaxWindowHeight;
        Resizable = true;
        ResizableEdges = ResizeEdges.Right;
   ```

2. Add the size limits and the mount options (below the constructor):

   ```csharp
    // The window's full size range. Scale and stretch together are enforced
    // by ConstrainResize and SyncGeometry, not by these.
    private const float MinWindowWidth = DeckGeometry.BaseWidth * DeckGeometry.MinStretch * DeckGeometry.MinScale;
    private const float MaxWindowWidth = DeckGeometry.BaseWidth * DeckGeometry.MaxStretch * DeckGeometry.MaxScale;
    private const float MinWindowHeight = DeckGeometry.BaseHeight * DeckGeometry.MinScale;
    private const float MaxWindowHeight = DeckGeometry.BaseHeight * DeckGeometry.MaxScale;

    /// <summary>A pointer within this many points of the right edge hits the
    /// deck even outside the frame, so the right-hand grips are easy to grab.</summary>
    private const float GripBand = 6f;

    /// <summary>How the runtime mounts the deck: imported chrome, resizable from the right only.</summary>
    internal static RetailWindowFrame.Options MountOptions(float left, float top) => new()
    {
        WindowName = WindowNames.CommandDeck,
        Chrome = RetailWindowChrome.Imported,
        Left = left,
        Top = top,
        Resizable = true,
        ResizeX = true,
        ResizeY = true,
        ResizableEdges = ResizeEdges.Right,
        MinWidth = MinWindowWidth,
        MaxWidth = MaxWindowWidth,
        MinHeight = MinWindowHeight,
        MaxHeight = MaxWindowHeight,
        Visible = false,
        ContentClickThrough = false,
    };
   ```

   Add `using AcDream.App.UI.Layout;` at the top if `RetailWindowFrame` / `RetailWindowChrome` live there.

3. Add the resize hooks and the size sync:

   ```csharp
    internal override ResizeEdges FilterResizeEdges(ResizeEdges hit) => hit switch
    {
        ResizeEdges.Right
            or (ResizeEdges.Right | ResizeEdges.Top)
            or (ResizeEdges.Right | ResizeEdges.Bottom) => hit,
        _ => ResizeEdges.None,
    };

    internal override void ConstrainResize(ResizeEdges edges, ref float width, ref float height)
    {
        bool corner = (edges & (ResizeEdges.Top | ResizeEdges.Bottom)) != 0;
        _geometry = corner ? _geometry.WithCornerSize(width, height) : _geometry.WithWidth(width);
        width = _geometry.Width;
        height = _geometry.Height;
    }

    /// <summary>Moves the geometry to the deck's current size (after a restore,
    /// a reset or a screen limit), and the size to the nearest valid one.</summary>
    private void SyncGeometry()
    {
        if (Width == _geometry.Width && Height == _geometry.Height) return;
        _geometry = DeckGeometry.FromSize(Width, Height);
        Width = _geometry.Width;
        Height = _geometry.Height;
    }
   ```

4. Call `SyncGeometry();` as the first line of `Refresh()`.

5. Keep `ApplyGeometry` from Task 3 unchanged (tests use it).

6. In `OnHitTest`, add the grip band to the return:

   ```csharp
        return _hitZone != DeckZone.None
            || _geometry.Frame.Contains(localX, localY)
            || _geometry.InOrb(localX, localY)
            || (localX >= Width - GripBand && localY >= 0 && localY < Height);
   ```

- [ ] **Step 4: Mount with the new options**

In `src/AcDream.App/UI/RetailUiRuntime.cs` `MountCommandDeck`, replace the whole `new RetailWindowFrame.Options { ... }` block with:

```csharp
            Hud.UiCommandDeck.MountOptions(
                Math.Max(0f, (Host.Root.Width - Hud.DeckGeometry.Default.Width) * 0.5f),
                Math.Max(0f, Host.Root.Height - Hud.DeckGeometry.Default.Height - 20f)));
```

- [ ] **Step 5: Draw the grip highlight**

In `src/AcDream.App/UI/Hud/UiCommandDeck.Draw.cs`, add a colour after `CastLit`:

```csharp
    private static readonly Vector4 GripInk = Rgb(0xE2C27A, 0.55f);
```

Add `DrawResizeGrip(ctx);` right after `DrawFrame(ctx);` in `OnDraw`, and the method:

```csharp
    /// <summary>A faint bronze stroke on the right edge, or an L at the right
    /// corner, while that grip is hovered or being dragged.</summary>
    private void DrawResizeGrip(UiRenderContext ctx)
    {
        ResizeEdges edges = FindRoot()?.ResizeEdgesFor(this) ?? ResizeEdges.None;
        if ((edges & ResizeEdges.Right) == 0) return;
        DeckRect f = _geometry.Frame;
        float w = Line(2), right = f.X + f.W - w, arm = P(24);
        if ((edges & ResizeEdges.Top) != 0)
        {
            ctx.DrawFill(right, f.Y, w, arm, GripInk);
            ctx.DrawFill(right - arm + w, f.Y, arm, w, GripInk);
        }
        else if ((edges & ResizeEdges.Bottom) != 0)
        {
            ctx.DrawFill(right, f.Y + f.H - arm, w, arm, GripInk);
            ctx.DrawFill(right - arm + w, f.Y + f.H - w, arm, w, GripInk);
        }
        else
        {
            ctx.DrawFill(right, f.Y + P(10), w, f.H - P(20), GripInk);
        }
    }
```

- [ ] **Step 6: Run the tests to check they pass**

Run the **Verify** command. Expected: all pass, 0 failed. Common failures:
- `RightGripsHitTestOutsideTheFrame` returns `None` → check the `GripBand` hit test and that `HitEdges` reaches `FilterResizeEdges`.
- A corner test is off by one step → check `WithCornerSize` is reading the axis you expect; the tests move only one axis on purpose.

Then run the full Mac suite once:

```bash
~/code/OpenAC/test-baseline/run-mac-tests.sh ~/code/OpenAC/OpenAC-deck-resize
```
Expected: 0 failed.

- [ ] **Step 7: Commit**

```bash
git add src/AcDream.App/UI/Hud src/AcDream.App/UI/RetailUiRuntime.cs tests/AcDream.App.Tests/UI/Hud
git commit -m "Command Deck: resize from the right edge (width) and right corners (scale)

Claude-Session: https://claude.ai/code/session_01WFS4EHSNafq2P1qfUuh258"
```

---

### Task 5: Live check on the local ACE server

**Goal:** The user confirms that resizing works in the real client on the local ACE server, and anything they find is fixed.

> **USER-ORDERED GATE — NON-SKIPPABLE.** This task was requested by the user in the current conversation. It MUST NOT be closed by walking around it, by declaring it "verified inline", or by substituting a cheaper check. Close only after every item in `acceptanceCriteria` has been re-validated independently, with output captured.

**Files:**
- None planned (fixes from the check get their own commits on `feat/command-deck-resize`).

**Acceptance Criteria:**
- [ ] The user said they are free before the client was launched.
- [ ] The client runs from the `feat/command-deck-resize` build against 10.10.20.20 (`~/OpenAC-dev/dev-client.env`), with Modern HUD on.
- [ ] The user confirms each of these: right-edge stretch to 2× and back; corner scale to 150% and 75%; snapping to 100%; the left side, top and bottom don't resize; power drag and H/M/L work at 150% and 75%; the size survives relog and a client restart; "reset layout" gives 420 × 150; Modern HUD off and on keeps the size; text is sharp on the Retina screen and on the external screen.
- [ ] Every issue found is fixed and re-checked by the user, or explicitly parked by the user.

**Verify:** The user's own confirmation in chat for each checklist item, plus the client log showing `[UI] Command Deck mounted (Modern HUD).` and no exceptions.

**Steps:**

- [ ] **Step 1:** Ask the user whether they're free for a live check. Don't launch anything until they say yes.
- [ ] **Step 2:** Build the worktree and launch the dev client the usual way for this repo (see the `live-testing-rules` memory), pointed at 10.10.20.20.
- [ ] **Step 3:** Go through the checklist with the user one item at a time and write down each answer.
- [ ] **Step 4:** Fix each issue on the branch (a test first, where one can show it), commit, and ask the user to check it again.

```json:metadata
{"userGate": true, "tags": ["user-gate"], "files": [], "verifyCommand": "user confirmation in chat + client log", "acceptanceCriteria": ["User said they are free before launch", "Client from feat/command-deck-resize against 10.10.20.20 with Modern HUD on", "User confirms every checklist item", "Every issue fixed and re-checked or parked by the user"], "modelTier": "standard"}
```

---

## After the plan

When Task 5 passes, use `superpowers-extended-cc:finishing-a-development-branch`: merge `feat/command-deck-resize` into fork `main`, then `main` into `rynth`, then push only after the user OKs it. Archive the spec branch as a tag, as before (`archive/specs/command-deck-resize-spec`). No upstream PR.
