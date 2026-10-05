# Flex layout PR 4 (scroll) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers-extended-cc:subagent-driven-development (recommended) or superpowers-extended-cc:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** `scroll="x|y|both"` on a markup `<group>` (flex or absolute) or on the root `<panel>` makes that area scroll when its content does not fit: 16-point bars that appear only on overflow, the wheel (including sideways swipes, tilt wheels and Shift+wheel), dragging the thumb, and focus scrolling into view. The PR also clears the PR 3 backlog: a minimum width for windows with a title bar, and no dock "–" over a bar-less layout window.

**Architecture:** The flex engine settles a scrolling container's bars itself: with the new opt-in `FlexNode.ScrollbarSize`, it lays the container's items out in the viewport left beside the bars and adds bars until they stop changing. It also sizes a scrolling wrap grid inside a column by its lines (spec 4.3a). `UiElement` gains a content viewport and offset that move and clip every child except scroll chrome, in drawing, hit-testing and `ScreenPosition`, so popups and focus geometry follow. `UiScrollArea` owns the two scroll models and two `UiScrollbar` children of one scrolling element. That element is one of three hosts: `UiScrollPanel` (absolute group), `UiFlexGroup`, or `UiPluginContentHost` (the window root). A flex host reads the settled bars from its node; an absolute host settles them by applying anchors against the viewport. The input path carries a horizontal step and Shift. `UiRoot` routes horizontal steps as a new `ScrollHorizontal` event that only horizontally scrolling groups consume. Markup without `scroll` builds, draws and routes exactly as before.

**Tech Stack:** C# / .NET 10, xUnit.

**Spec:** `docs/superpowers/specs/2026-10-05-plugin-flex-layout-design.md` (fork-only docs branch `docs/flex-layout-spec`): sections 4 (all), 4.3a, 7 (`UiScrollGroupTests`, input, scaling) and 9 (PR 4). This plan covers PR 4 only. Read its "Spec corrections made while planning" first: they settle how 4.3 is built.

## Global Constraints

- Work in a worktree `.worktrees/flex-scroll` on a new branch `flex/scroll` from fork `main` (d54f047a or later). Never commit `docs/superpowers/**` on that branch.
- Build and test with the pinned SDK: every shell starts with `export DOTNET_ROOT=$HOME/.dotnet PATH=$HOME/.dotnet:$PATH`. Building rewrites some `*.lock.json` files with local RID churn; revert with `git checkout -- '*.lock.json'` before each commit, never commit that churn.
- The repository builds with `TreatWarningsAsErrors`; the build must stay at 0 warnings.
- **No contract change.** `src/AcDream.Plugin.Abstractions/**` is untouched (no fork contract version bump). `scroll` is a documented behaviour contract in `docs/plugin-ui-markup.md`.
- **Markup without `scroll` is unchanged**: same tree, sizes, revision, draw calls and input routing. `PluginThemeClassicIdentityTests`, `PluginThemeWindowTests`, `MarkupDocumentTests`, `MarkupPanelClickTests`, `MarkupTitleBarTests`, `MarkupResizableAnchorTests`, `MarkupFlexTests`, `MarkupListColumnsTests`, `RetailWindowManagerTests`, `RetailWindowLayoutPersistenceTests` and the `UI.Layout.Flex` suites pass unchanged. Exactly these existing tests change, in the tasks named:
  - `SilkRetainedMouseScrollTests` and the fake surface in `RetainedUiInputBindingTests`: the surface's scroll callback takes `(dx, dy)` (Task 3);
  - five `MarkupFlexWindowTests` expectations: the 96-point title-bar floor (Task 7);
  - `FlexDemoSampleTests.AssertInside`: it skips scrolling groups (Task 8).
- Constants: a bar is `UiScrollArea.BarSize` = `FlexLayout.ScrollbarThickness` = 16 points; the smallest scroll view is `FlexLayout.MinimumScrollViewport` = 40; an arrow button moves `UiScrollArea.LineStep` = 16 points and a wheel step `WheelLines` = 3 lines (48 points); the title-bar frame floor is `PluginWindowChrome.MinimumBarFrameWidth` = 96; `UiEventType.ScrollHorizontal` = `0x0B`, `Data0` = +1 toward the start (left), like `Scroll`'s +1 = up.
- Commit messages end with the line `Claude-Session: https://claude.ai/code/session_019dvJ5gakBkq3uJyr1PAC3u`.

**User decisions (already made):**
- Scope: markup windows only; canvases out of scope. Overflow: the content minimum drives the window minimum, plus scrolling groups (`scroll="y|x|both"`).
- Horizontal wheel input is in scope for PR 4 (spec 4.3, kept after the Codex review): native horizontal steps and Shift+wheel.
- On a window with a title bar the bar's X replaces the dock's "–" (PR 2).
- Delivery as four single-topic PRs from fork main, merged into fork main after review; spec and plans stay on the fork-only docs branch.
- The PR 3 backlog items (frame minimum ignoring the title bar; the dock "–" over a bar-less layout window) are fixed in PR 4.

## Spec corrections made while planning

Found while prototyping PR 4: a scratch branch with every task below implemented, then replayed task by task onto main d54f047a. Each task built at 0 warnings and passed its own tests; the full App suite failed exactly main's 166 environment tests by name and passed 71 new ones. Folded into the spec under "Planning corrections (PR 4)" in the same docs commit as this plan. Decisions marked **(veto?)** were taken in planning and are flagged for the user.

1. **Bars settle inside the engine, opt-in.** `FlexNode.ScrollbarSize` (0 by default; markup sets 16) makes a scrolling container lay its items out in the viewport beside its bars and settle them per spec 4.3 (at most three layouts of its own items, then one of its subtree). Results: `ScrollbarX`, `ScrollbarY`, `Viewport`. Nested scrolling groups settle in the same pass as their flex root (spec 4.3a's second follow-up). With `ScrollbarSize` 0 nothing changes, so PR 1's scroll tests stand.
2. **A scrolling grid laid across a column is sized by its lines** (spec 4.3a's first follow-up): a `scroll="y"` wrapping row in a column prefers the extent of its lines at the width it gets, and may shrink to its 40-point viewport. The window's *preferred* size still counts such a grid as one line; a window that holds one should state its `h`.
3. **The shared bar is the existing `UiScrollbar` element**, which `log` already uses, not a helper extracted from `UiMarkupList`. The list keeps its own bar code, so Classic lists stay byte-identical. The themed horizontal bar following a scroll model now draws a slim thumb like the vertical one; it used to draw the slider look, which no markup reached.
4. **Content offset.** `UiElement` gains `ContentViewport` (null: not scrolling), `ContentOffset` and the child flag `ScrollChrome` (the bars). While a viewport is set, every non-chrome child is moved by the offset and clipped to the viewport in drawing, overlays, hit-testing and `ScreenPosition`.
5. **Scrolling groups are hit-testable** (not `ClickThrough`), so the wheel reaches them over empty space. A press there still drags the window: the group neither handles clicks nor captures drags.
6. **Wheel details.** A group consumes a wheel step only while it overflows on that axis, so an outer group scrolls when an inner one cannot. One step is 3 × 16 points. The surface reports `(dx, dy)` and the binding adds Shift from `UiRoot.ShiftHeld`, the keyboard query Tab already used. A Shift step over no interface at all still reaches the world as the vertical step it was, so Shift+wheel world zoom keeps working **(veto?)**. A native horizontal step never reaches the world.
7. **A scrolling root needs a content area**: `scroll` on a root `<panel>` with neither `layout` nor `titlebar="true"` fails the build **(veto?)**. Otherwise `scroll` would silently change where an absolute window's children sit.
8. **`scroll` enters the saved-size revision only when present**, so no existing window's saved size is reset.
9. **Items do not grow along a scrolling axis**: the axis is unbounded (spec 4.2), so `grow` there has nothing to share. Documented.
10. **An absolute scrolling group in a flex container** may shrink to the 40-point view on each axis it scrolls (plus 16 for the other bar), like a flex one.
11. **Focus reveal settles first**: a group scrolled wholly out of its parent's view has not been drawn, so it settles its bars before revealing a descendant. Ancestors reveal innermost first.
12. **PR 3 backlog, title bar**: a window with a title bar has a frame minimum of 96 points (close button, insets, about eight title characters), and a narrower one opens at 96 **(veto?)**.
13. **PR 3 backlog, dock**: a layout window without a title bar gets no dock "–", since its content reaches the corner where the button would sit. The player closes it from its dock slot, the plugin with `HidePanel` **(veto?)**.
14. **FlexDemo window 2** is a "Flex Gallery": 30 spell icons (`spell="1"` to `"30"`) in a scrolling wrap grid, over a "Recent" strip of 12 that scrolls sideways (so the live gate can try horizontal input). The settings window becomes `resizable` with `scroll="y"`.

## File Structure

| File | Responsibility |
|---|---|
| Modify `src/AcDream.App/UI/Layout/Flex/FlexNode.cs` | `ScrollbarSize`; results `ScrollbarX`, `ScrollbarY`, `Viewport`; `ScrollsDependentContent`. |
| Modify `src/AcDream.App/UI/Layout/Flex/FlexLayout.cs` | Bar settling (`SettleScrollbars`/`ArrangeContent`); a scrolling grid laid across its parent. |
| Modify `src/AcDream.App/UI/Layout/Flex/FlexFit.cs` | A shown bar counts toward the room the content needs. |
| Modify `src/AcDream.App/UI/UiElement.cs` | `ScrollChrome`, `ContentViewport`, `ContentOffset` in draw, overlays, hit-test, `ScreenPosition`. |
| Modify `src/AcDream.App/UI/UiEvent.cs` | `UiEventType.ScrollHorizontal`. |
| Modify `src/AcDream.App/UI/UiRoot.cs` | `OnScroll(dx, dy, shift)`, `ShiftHeld`, horizontal routing (Task 3); focus reveal (Task 4). |
| Modify `src/AcDream.App/UI/RetainedUiInputBinding.cs` | The mouse surface reports `(dx, dy)`; the binding adds Shift. |
| Create `src/AcDream.App/UI/UiScrollArea.cs` | `IUiScrollHost`; `UiScrollArea`: models, bars, settling (flex and absolute), wheel, reveal, theme. |
| Create `src/AcDream.App/UI/UiScrollPanel.cs` | `<group scroll>` without `layout`. |
| Modify `src/AcDream.App/UI/UiFlexGroup.cs` | `UseScroll`; reads the settled bars after layout; wheel. |
| Modify `src/AcDream.App/UI/UiFlexBox.cs` | A leaf's authored size is a full measurement (preferred and minimum). |
| Modify `src/AcDream.App/UI/MarkupFlexAttributes.cs` | `Scroll(el)`. |
| Modify `src/AcDream.App/UI/MarkupDocument.cs` | Scrolling groups (Task 4); bar theme (Task 5); root `scroll` (Task 6); title-bar floor (Task 7). |
| Modify `src/AcDream.App/UI/UiScrollbar.cs`, `PluginUiStyle.cs` | The themed horizontal scroll thumb. |
| Modify `src/AcDream.App/UI/UiPluginContentHost.cs` | `UseScroll` for a scrolling content area, flex or absolute. |
| Modify `src/AcDream.App/UI/PluginWindowChrome.cs` | `scroll` in the authored inputs (Task 6); `MinimumBarFrameWidth`, `FrameMinimumWidth` (Task 7). |
| Modify `src/AcDream.App/UI/MarkupWindow.cs` | The content minimum keeps the title-bar floor. |
| Modify `src/AcDream.App/UI/PluginSidePanel.cs` | No "–" on a bar-less layout window. |
| Modify `docs/plugin-ui-markup.md` | "Scrolling groups" section; element table; title bar floor; dock "–" and revision notes. |
| Create/modify `samples/AcDream.Plugins.FlexDemo/*` | `gallery.xml` (window 2); `settings.xml` scrolls; plugin registers the gallery. |
| Tests under `tests/AcDream.App.Tests/UI/` | `Layout/Flex/FlexLayoutScrollbarTests`, `UiContentOffsetTests`, `UiRootWheelRoutingTests`, `UiScrollGroupTests`, `MarkupScrollWindowTests`, plus the edits listed under Global Constraints. |

Task order and dependencies: 1, 2 and 3 are independent; 4 needs 1, 2 and 3; 5 needs 4; 6 needs 4 and 5; 7 needs 6 (it edits the lines of `MarkupDocument` that Task 6 changed); 8 needs 1–7; 9 needs all. Run them in order: several tasks edit the same files, and each edit's text assumes the earlier tasks are in.

---
### Task 1: Engine: scrollbars and scrolled-across grids

**Goal:** A scrolling `FlexNode` with a `ScrollbarSize` settles its bars and lays its items out in the viewport beside them; a scrolling wrap grid laid across a column is sized by its lines with its 40-point view as its minimum; `FlexFit` counts a shown bar.

**Files:**
- Create: `tests/AcDream.App.Tests/UI/Layout/Flex/FlexLayoutScrollbarTests.cs`
- Modify: `src/AcDream.App/UI/Layout/Flex/FlexNode.cs`
- Modify: `src/AcDream.App/UI/Layout/Flex/FlexLayout.cs`
- Modify: `src/AcDream.App/UI/Layout/Flex/FlexFit.cs`

**Acceptance Criteria:**
- [ ] A 100 × 100 `scroll=y` column of two 80-tall leaves shows a vertical bar, `Viewport` 84 × 100, and its items are 84 wide.
- [ ] A vertical bar that makes 90-wide content too wide brings the horizontal bar (viewport 84 × 84), and the reverse for a horizontal bar and 95-tall content.
- [ ] Without `ScrollbarSize` nothing is reserved (`Viewport` is the whole rect, no bars): PR 1's scroll tests pass unchanged.
- [ ] A scrolling wrap grid of six 30 × 30 items in a 90-wide column is 60 tall without a bar when the column has room, 50 tall with a bar in a 70-tall column, and never below 40.
- [ ] `FlexFit.Minimum` of a `scroll=y` column of 50-wide leaves is 66 × 40, and a repeated settle allocates nothing.

**Verify:** `dotnet test tests/AcDream.App.Tests --filter "FullyQualifiedName~UI.Layout.Flex" → Passed: 94 (13 new), Failed: 0`

**Steps:**

- [ ] **Step 1: Write the failing tests**

These tests drive the engine directly with `FlexTestNodes`; `Scrolling` sets `ScrollbarSize` 16.

Create `tests/AcDream.App.Tests/UI/Layout/Flex/FlexLayoutScrollbarTests.cs`:

```csharp
using AcDream.App.UI.Layout.Flex;
using static AcDream.App.Tests.UI.Layout.Flex.FlexTestNodes;

namespace AcDream.App.Tests.UI.Layout.Flex;

/// <summary>
/// Scrolling containers that reserve room for their bars
/// (<see cref="FlexNode.ScrollbarSize"/>): which bars settle, the viewport
/// their items lay out in, and a scrolling grid laid across a column.
/// </summary>
public sealed class FlexLayoutScrollbarTests
{
    private static FlexNode[] Items(int count, float width, float height)
    {
        var items = new FlexNode[count];
        for (int i = 0; i < count; i++) items[i] = Leaf(width, height);
        return items;
    }

    private static FlexNode Scrolling(FlexNode node, bool x, bool y)
    {
        node.ScrollX = x;
        node.ScrollY = y;
        node.ScrollbarSize = 16f;
        return node;
    }

    [Fact]
    public void Content_that_fits_shows_no_bars_and_uses_the_whole_rect()
    {
        FlexNode a = Leaf(50f, 30f);
        FlexNode column = Scrolling(Column(a), x: false, y: true);

        Arrange(column, 100f, 100f);

        Assert.False(column.ScrollbarY);
        Assert.False(column.ScrollbarX);
        Assert.Equal(new FlexSize(100f, 100f), column.Viewport);
        Assert.Equal(new FlexRect(0f, 0f, 100f, 30f), a.Rect);
    }

    [Fact]
    public void A_vertical_bar_narrows_the_viewport_the_items_stretch_across()
    {
        FlexNode a = Leaf(50f, 80f), b = Leaf(50f, 80f);
        FlexNode column = Scrolling(Column(a, b), x: false, y: true);

        Arrange(column, 100f, 100f);

        Assert.True(column.ScrollbarY);
        Assert.False(column.ScrollbarX);
        Assert.Equal(new FlexSize(84f, 100f), column.Viewport);
        Assert.Equal(new FlexRect(0f, 80f, 84f, 80f), b.Rect);
        Assert.Equal(new FlexSize(84f, 160f), column.ContentSize);
    }

    [Fact]
    public void A_wrapping_grid_rebreaks_its_lines_beside_the_vertical_bar()
    {
        FlexNode[] items = Items(7, 30f, 30f);
        FlexNode row = Scrolling(Row(items), x: false, y: true);
        row.Wrap = true;
        row.Gap = 5f;

        Arrange(row, 100f, 50f);

        // At 100 three fit a line (3 lines, 100 tall: overflows); at 84 two do.
        Assert.True(row.ScrollbarY);
        Assert.Equal(new FlexRect(0f, 105f, 30f, 30f), items[6].Rect);
        Assert.Equal(new FlexSize(65f, 135f), row.ContentSize);
    }

    [Fact]
    public void A_vertical_bar_that_makes_the_content_too_wide_brings_the_horizontal_bar()
    {
        // 90 wide fits 100, but not the 84 left beside the vertical bar.
        FlexNode a = Leaf(90f, 80f), b = Leaf(90f, 80f);
        FlexNode column = Scrolling(Column(a, b), x: true, y: true);

        Arrange(column, 100f, 100f);

        Assert.True(column.ScrollbarY);
        Assert.True(column.ScrollbarX);
        Assert.Equal(new FlexSize(84f, 84f), column.Viewport);
    }

    [Fact]
    public void A_horizontal_bar_that_makes_the_content_too_tall_brings_the_vertical_bar()
    {
        // 95 tall fits 100, but not the 84 left above the horizontal bar.
        FlexNode a = Leaf(80f, 95f), b = Leaf(80f, 95f);
        FlexNode row = Scrolling(Row(a, b), x: true, y: true);
        row.Align = FlexAlign.Start;

        Arrange(row, 100f, 100f);

        Assert.True(row.ScrollbarX);
        Assert.True(row.ScrollbarY);
        Assert.Equal(new FlexSize(84f, 84f), row.Viewport);
    }

    [Fact]
    public void A_horizontal_scroller_whose_content_fits_beside_no_bar_shows_none()
    {
        FlexNode a = Leaf(40f, 20f), b = Leaf(40f, 20f);
        FlexNode row = Scrolling(Row(a, b), x: true, y: false);

        Arrange(row, 100f, 50f);

        Assert.False(row.ScrollbarX);
        Assert.Equal(new FlexSize(100f, 50f), row.Viewport);
        Assert.Equal(new FlexRect(40f, 0f, 40f, 50f), b.Rect);
    }

    [Fact]
    public void Without_a_scrollbar_size_nothing_is_reserved()
    {
        FlexNode a = Leaf(50f, 80f), b = Leaf(50f, 80f);
        FlexNode column = Column(a, b);
        column.ScrollY = true;

        Arrange(column, 100f, 100f);

        Assert.False(column.ScrollbarY);
        Assert.Equal(new FlexSize(100f, 100f), column.Viewport);
        Assert.Equal(100f, b.Rect.Width);
    }

    [Fact]
    public void A_nested_scroller_settles_its_own_bars_in_the_rect_its_parent_gives_it()
    {
        FlexNode header = Leaf(50f, 20f);
        FlexNode[] rows = Items(5, 50f, 30f);
        FlexNode list = Scrolling(Column(rows), x: false, y: true);
        list.Grow = 1f;
        FlexNode root = Column(header, list);

        Arrange(root, 120f, 100f);

        Assert.Equal(new FlexRect(0f, 20f, 120f, 80f), list.Rect);
        Assert.True(list.ScrollbarY);
        Assert.Equal(new FlexSize(104f, 80f), list.Viewport);
        Assert.Equal(104f, rows[0].Rect.Width);
    }

    [Fact]
    public void A_scrolling_grid_in_a_column_shows_all_its_lines_when_there_is_room()
    {
        FlexNode[] items = Items(6, 30f, 30f);
        FlexNode grid = Scrolling(Row(items), x: false, y: true);
        grid.Wrap = true;
        FlexNode footer = Leaf(50f, 20f);
        FlexNode root = Column(grid, footer);
        root.Align = FlexAlign.Stretch;

        Arrange(root, 90f, 200f);

        // Three a line at 90: two lines, 60 tall, without a bar.
        Assert.Equal(new FlexRect(0f, 0f, 90f, 60f), grid.Rect);
        Assert.False(grid.ScrollbarY);
        Assert.Equal(new FlexRect(0f, 60f, 90f, 20f), footer.Rect);
    }

    [Fact]
    public void A_scrolling_grid_in_a_column_shrinks_to_scroll_when_there_is_not()
    {
        FlexNode[] items = Items(6, 30f, 30f);
        FlexNode grid = Scrolling(Row(items), x: false, y: true);
        grid.Wrap = true;
        FlexNode footer = Leaf(50f, 20f);
        FlexNode root = Column(grid, footer);

        Arrange(root, 90f, 70f);

        Assert.Equal(new FlexRect(0f, 0f, 90f, 50f), grid.Rect);
        Assert.True(grid.ScrollbarY);
        Assert.Equal(new FlexRect(0f, 50f, 90f, 20f), footer.Rect);
    }

    [Fact]
    public void A_scrolling_grid_in_a_column_never_shrinks_below_its_viewport_minimum()
    {
        FlexNode[] items = Items(6, 30f, 30f);
        FlexNode grid = Scrolling(Row(items), x: false, y: true);
        grid.Wrap = true;
        FlexNode footer = Leaf(50f, 20f);
        FlexNode root = Column(grid, footer);

        Arrange(root, 90f, 30f);

        Assert.Equal(FlexLayout.MinimumScrollViewport, grid.Rect.Height);
    }

    [Fact]
    public void The_fitted_minimum_of_a_vertical_scroller_leaves_room_for_its_bar()
    {
        FlexNode a = Leaf(50f, 80f), b = Leaf(50f, 80f);
        FlexNode root = Scrolling(Column(a, b), x: false, y: true);

        FlexSize fitted = FlexFit.Minimum(root);

        Assert.Equal(new FlexSize(66f, FlexLayout.MinimumScrollViewport), fitted);
        Assert.True(root.ScrollbarY);
        Assert.Equal(50f, a.Rect.Width);
    }

    [Fact]
    public void Settling_the_bars_allocates_nothing_once_warm()
    {
        FlexNode[] items = Items(60, 30f, 30f);
        FlexNode grid = Scrolling(Row(items), x: false, y: true);
        grid.Wrap = true;
        grid.Gap = 4f;
        for (int width = 100; width < 200; width++)
            FlexLayout.Arrange(grid, new FlexRect(0f, 0f, width, 120f));

        // The least of a few rounds: the runtime's own tiering work can land in
        // one, but an allocation by the layout would show in every round.
        long least = long.MaxValue;
        for (int round = 0; round < 5; round++)
        {
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int width = 100; width < 200; width++)
                FlexLayout.Arrange(grid, new FlexRect(0f, 0f, width, 120f));
            least = Math.Min(least, GC.GetAllocatedBytesForCurrentThread() - before);
        }

        Assert.True(grid.ScrollbarY);
        Assert.Equal(0L, least);
    }
}
```

- [ ] **Step 2: Run them to see them fail**

```bash
export DOTNET_ROOT=$HOME/.dotnet PATH=$HOME/.dotnet:$PATH
dotnet test tests/AcDream.App.Tests --filter "FullyQualifiedName~UI.Layout.Flex"
```

Expected: the build fails: `FlexNode` has no `ScrollbarSize`, `ScrollbarX`, `ScrollbarY` or `Viewport`.

- [ ] **Step 3: Implement**

Bar settling wraps the old arrange (renamed `ArrangeContent`) and runs only for a scrolling container with a `ScrollbarSize` and a known size. A dry-run extent (a null size) never settles. `IsLaidAcross` covers both kinds of child sized by their content at the cross size they get, in `ResolveBases` and `PlaceLine`.

In `src/AcDream.App/UI/Layout/Flex/FlexNode.cs`, replace:

```csharp

    public List<FlexNode> Children { get; } = new();
```

with:

```csharp

    /// <summary>
    /// Thickness of each scrollbar this scrolling container shows, taken from
    /// its viewport while that bar is shown, so its items lay out beside the
    /// bar rather than under it. 0, the default, reserves nothing: the
    /// container lays out at its full size and shows no bars.
    /// </summary>
    public float ScrollbarSize { get; set; }

    public List<FlexNode> Children { get; } = new();
```

In `src/AcDream.App/UI/Layout/Flex/FlexNode.cs`, replace:

```csharp

    // Measure-pass results the arrange pass reads: preferred size with the
    // Width/Height overrides applied but not clamped, and resolved limits.
```

with:

```csharp

    /// <summary>
    /// After an arrange of a scrolling container with a
    /// <see cref="ScrollbarSize"/>: whether it shows a horizontal bar (its
    /// content is wider than its viewport and it scrolls horizontally).
    /// </summary>
    public bool ScrollbarX { get; internal set; }

    /// <summary>As <see cref="ScrollbarX"/>, for the vertical bar.</summary>
    public bool ScrollbarY { get; internal set; }

    /// <summary>
    /// After an arrange, the part of <see cref="Rect"/> the container's items
    /// are laid out in and seen through: its size less any bars shown. The
    /// whole rect when no bars are shown.
    /// </summary>
    public FlexSize Viewport { get; internal set; }

    // Measure-pass results the arrange pass reads: preferred size with the
    // Width/Height overrides applied but not clamped, and resolved limits.
```

In `src/AcDream.App/UI/Layout/Flex/FlexNode.cs`, replace:

```csharp

    // Arrange scratch, reused so a steady-state layout allocates nothing.
    internal readonly List<int> LineEnds = new();
```

with:

```csharp

    // Measure-pass result for a scrolling container: its content would be
    // size-dependent (along HeightForWidth) if it did not scroll. Such a
    // container scrolling along its parent's main axis is sized there by the
    // extent of that content, like a size-dependent node, but may shrink to
    // its scroll viewport minimum.
    internal bool ScrollsDependentContent;

    // Arrange scratch, reused so a steady-state layout allocates nothing.
    internal readonly List<int> LineEnds = new();
```

In `src/AcDream.App/UI/Layout/Flex/FlexLayout.cs`, replace:

```csharp
        node.HeightForWidth = false;
        if (node.Measure is not null || node.ScrollX || node.ScrollY) return;
        if (node.Wrap)
        {
            node.SizeDependent = true;
            node.HeightForWidth = node.Direction == FlexDirection.Row;
            return;
        }
        for (int i = 0; i < node.Children.Count; i++)
        {
            FlexNode child = node.Children[i];
            if (child.Hidden || !child.SizeDependent) continue;
            node.SizeDependent = true;
            node.HeightForWidth |= child.HeightForWidth;
        }
    }
```

with:

```csharp
        node.HeightForWidth = false;
        node.ScrollsDependentContent = false;
        if (node.Measure is not null) return;
        bool dependent = false, heightForWidth = false;
        if (node.Wrap)
        {
            dependent = true;
            heightForWidth = node.Direction == FlexDirection.Row;
        }
        else
        {
            for (int i = 0; i < node.Children.Count; i++)
            {
                FlexNode child = node.Children[i];
                if (child.Hidden || !child.SizeDependent) continue;
                dependent = true;
                heightForWidth |= child.HeightForWidth;
            }
        }
        node.HeightForWidth = heightForWidth;
        if (node.ScrollX || node.ScrollY) node.ScrollsDependentContent = dependent;
        else node.SizeDependent = dependent;
    }
```

In `src/AcDream.App/UI/Layout/Flex/FlexLayout.cs`, replace:

```csharp
    {
        if (node.Measure is not null)
        {
```

with:

```csharp
    {
        node.ScrollbarX = false;
        node.ScrollbarY = false;
        if (node.Measure is null && (node.ScrollX || node.ScrollY) && Positive(node.ScrollbarSize) > 0f
            && width is { } w && height is { } h)
        {
            SettleScrollbars(node, w, h, descend);
            return;
        }
        node.Viewport = new FlexSize(width ?? 0f, height ?? 0f);
        ArrangeContent(node, width, height, descend);
    }

    /// <summary>
    /// Lays a scrolling container out with the bars its content needs. It
    /// starts with none, lays its items out in the viewport, adds every bar
    /// whose axis now overflows, and repeats in the smaller viewport until
    /// the bars stop changing. A vertical bar narrows the viewport, which can
    /// make the content overflow horizontally, and a horizontal bar does the
    /// reverse; bars are only ever added, so this ends after at most three
    /// layouts of the container's own items. Its subtree is then laid out
    /// once, in the settled viewport.
    /// </summary>
    private static void SettleScrollbars(FlexNode node, float width, float height, bool descend)
    {
        float bar = Positive(node.ScrollbarSize);
        bool barX = false, barY = false;
        float viewW = Positive(width), viewH = Positive(height);
        for (int pass = 0; pass < 3; pass++)
        {
            ArrangeContent(node, viewW, viewH, descend: false);
            bool needX = barX || (node.ScrollX && node.ContentSize.Width > viewW);
            bool needY = barY || (node.ScrollY && node.ContentSize.Height > viewH);
            if (needX == barX && needY == barY) break;
            barX = needX;
            barY = needY;
            viewW = Positive(width - (barY ? bar : 0f));
            viewH = Positive(height - (barX ? bar : 0f));
        }
        ArrangeContent(node, viewW, viewH, descend);
        node.ScrollbarX = barX;
        node.ScrollbarY = barY;
        node.Viewport = new FlexSize(viewW, viewH);
    }

    /// <summary>
    /// <see cref="ArrangeChildren"/> without the scrollbars: lays the items
    /// out in exactly <paramref name="width"/> x <paramref name="height"/>.
    /// </summary>
    private static void ArrangeContent(FlexNode node, float? width, float? height, bool descend)
    {
        if (node.Measure is not null)
        {
```

In `src/AcDream.App/UI/Layout/Flex/FlexLayout.cs`, replace:

```csharp

            if (!IsSizedAcross(node, child))
            {
```

with:

```csharp

            if (!IsLaidAcross(node, child))
            {
```

In `src/AcDream.App/UI/Layout/Flex/FlexLayout.cs`, replace:

```csharp
            float maxMain = MainOf(child.MaximumSize, row);
            float autoMin = explicitMain is { } e ? MathF.Min(Positive(e), extent) : extent;
            s[BaseSlot * count + i] = child.Basis is { } basis ? Positive(basis) : explicitMain is { } em ? Positive(em) : extent;
```

with:

```csharp
            float maxMain = MainOf(child.MaximumSize, row);
            // A scrolling child prefers its whole content but may shrink to its viewport minimum.
            float contentMin = child.SizeDependent ? extent : MainOf(child.MinimumSize, row);
            float autoMin = explicitMain is { } e ? MathF.Min(Positive(e), contentMin) : contentMin;
            s[BaseSlot * count + i] = child.Basis is { } basis ? Positive(basis) : explicitMain is { } em ? Positive(em) : extent;
```

In `src/AcDream.App/UI/Layout/Flex/FlexLayout.cs`, replace:

```csharp
    /// <summary>
    /// Whether <paramref name="child"/> is size-dependent along
    /// <paramref name="parent"/>'s cross axis (a wrapping row, or a column
```

with:

```csharp
    /// <summary>
    /// Whether <paramref name="child"/> scrolls along <paramref name="parent"/>'s
    /// main axis (and not across it) over content that is size-dependent along
    /// that axis: a <c>scroll="y"</c> wrapping row in a column. It is sized like
    /// a child sized across, by the extent of its content at the cross size it
    /// gets, so it shows all of it when there is room; its minimum stays its
    /// scroll viewport, so it scrolls when there is not.
    /// </summary>
    private static bool IsScrolledAcross(FlexNode parent, FlexNode child)
    {
        if (!child.ScrollsDependentContent) return false;
        bool column = parent.Direction == FlexDirection.Column;
        if (child.HeightForWidth != column) return false;
        return column ? child.ScrollY && !child.ScrollX : child.ScrollX && !child.ScrollY;
    }

    /// <summary>Whether the child's main size is found by laying its content out at the cross size it gets.</summary>
    private static bool IsLaidAcross(FlexNode parent, FlexNode child) =>
        IsSizedAcross(parent, child) || IsScrolledAcross(parent, child);

    /// <summary>
    /// Whether <paramref name="child"/> is size-dependent along
    /// <paramref name="parent"/>'s cross axis (a wrapping row, or a column
```

In `src/AcDream.App/UI/Layout/Flex/FlexLayout.cs`, replace:

```csharp
            float minCross = s[CrossFloorSlot * count + i], maxCross = CrossOf(child.MaximumSize, row);
            float cross = !IsSizedAcross(node, child) && align == FlexAlign.Stretch && explicitCross is null
                ? Math.Clamp(lineCross, minCross, MathF.Max(minCross, maxCross))
```

with:

```csharp
            float minCross = s[CrossFloorSlot * count + i], maxCross = CrossOf(child.MaximumSize, row);
            float cross = !IsLaidAcross(node, child) && align == FlexAlign.Stretch && explicitCross is null
                ? Math.Clamp(lineCross, minCross, MathF.Max(minCross, maxCross))
```

In `src/AcDream.App/UI/Layout/Flex/FlexFit.cs`, replace:

```csharp
            FlexLayout.Arrange(root, new FlexRect(0f, 0f, width, height));
            float needWidth = root.ScrollX ? width : MathF.Ceiling(root.ContentSize.Width);
            float needHeight = root.ScrollY ? height : MathF.Ceiling(root.ContentSize.Height);
            if (needWidth <= width && needHeight <= height) break;
```

with:

```csharp
            FlexLayout.Arrange(root, new FlexRect(0f, 0f, width, height));
            // A bar the root shows takes room beside its content on the other axis.
            float barW = root.ScrollbarY ? root.ScrollbarSize : 0f, barH = root.ScrollbarX ? root.ScrollbarSize : 0f;
            float needWidth = root.ScrollX ? width : MathF.Ceiling(root.ContentSize.Width + barW);
            float needHeight = root.ScrollY ? height : MathF.Ceiling(root.ContentSize.Height + barH);
            if (needWidth <= width && needHeight <= height) break;
```

- [ ] **Step 4: Run them to see them pass**

```bash
dotnet test tests/AcDream.App.Tests --filter "FullyQualifiedName~UI.Layout.Flex"
```

Expected: Passed: 94, Failed: 0 (13 new in `FlexLayoutScrollbarTests`). The build reports 0 warnings.

- [ ] **Step 5: Commit**

```bash
git checkout -- '*.lock.json'
git add src/AcDream.App/UI/Layout/Flex/FlexFit.cs src/AcDream.App/UI/Layout/Flex/FlexLayout.cs src/AcDream.App/UI/Layout/Flex/FlexNode.cs tests/AcDream.App.Tests/UI/Layout/Flex/FlexLayoutScrollbarTests.cs
git commit -m "feat(ui): flex engine settles scrollbars and sizes scrolling grids by their lines" -m "Claude-Session: https://claude.ai/code/session_019dvJ5gakBkq3uJyr1PAC3u"
```

---
### Task 2: Content offset in UiElement

**Goal:** While an element has a `ContentViewport`, every child that is not `ScrollChrome` is moved by `ContentOffset` and clipped to the viewport in drawing, overlays, hit-testing and `ScreenPosition`; without one nothing changes.

**Files:**
- Create: `tests/AcDream.App.Tests/UI/UiContentOffsetTests.cs`
- Modify: `src/AcDream.App/UI/UiElement.cs`

**Acceptance Criteria:**
- [ ] Without a viewport, setting `ContentOffset` moves nothing.
- [ ] With viewport 84 × 50 and offset (0, 30), a child at (0, 60) in a host at (10, 20) has `ScreenPosition` (10, 50) and is picked at (15, 65); a `ScrollChrome` bar keeps (94, 20).
- [ ] Content outside the viewport is not hit; drawn content is offset and cut at the viewport's edge while the bar is not.

**Verify:** `dotnet test tests/AcDream.App.Tests --filter "FullyQualifiedName~UiContentOffsetTests" → Passed: 5, Failed: 0`

**Steps:**

- [ ] **Step 1: Write the failing tests**

A plain `UiPanel` host with a red content child and a blue `ScrollChrome` bar, checked without markup.

Create `tests/AcDream.App.Tests/UI/UiContentOffsetTests.cs`:

```csharp
using System.Numerics;
using AcDream.App.UI;

namespace AcDream.App.Tests.UI;

/// <summary>
/// <see cref="UiElement.ContentViewport"/> and <see cref="UiElement.ContentOffset"/>:
/// while a viewport is set, every child but a <see cref="UiElement.ScrollChrome"/>
/// one is moved by the offset and clipped to the viewport, in drawing,
/// hit-testing and screen positions alike. Without one nothing changes.
/// </summary>
public sealed class UiContentOffsetTests
{
    private static readonly Vector4 Red = new(1f, 0f, 0f, 1f);
    private static readonly Vector4 Blue = new(0f, 0f, 1f, 1f);

    private static (UiRoot Root, UiPanel Host, UiPanel Content, UiPanel Bar) Tree()
    {
        var root = new UiRoot { Width = 400, Height = 300 };
        var host = new UiPanel { Left = 10, Top = 20, Width = 100, Height = 50, BackgroundColor = Vector4.Zero, BorderColor = Vector4.Zero };
        var content = new UiPanel { Left = 0, Top = 60, Width = 40, Height = 30, BackgroundColor = Red, BorderColor = Vector4.Zero };
        var bar = new UiPanel { Left = 84, Top = 0, Width = 16, Height = 50, BackgroundColor = Blue, BorderColor = Vector4.Zero, ScrollChrome = true };
        host.AddChild(content);
        host.AddChild(bar);
        root.AddChild(host);
        return (root, host, content, bar);
    }

    [Fact]
    public void Without_a_viewport_the_offset_moves_nothing()
    {
        var (_, host, content, _) = Tree();
        host.ContentOffset = new Vector2(0f, 30f);

        Assert.Equal(new Vector2(10f, 80f), content.ScreenPosition);
    }

    [Fact]
    public void A_viewport_moves_content_but_not_chrome_in_screen_positions()
    {
        var (_, host, content, bar) = Tree();
        host.ContentViewport = new Vector2(84f, 50f);
        host.ContentOffset = new Vector2(0f, 30f);

        Assert.Equal(new Vector2(10f, 50f), content.ScreenPosition);
        Assert.Equal(new Vector2(94f, 20f), bar.ScreenPosition);
    }

    [Fact]
    public void Hit_testing_follows_the_offset_and_stops_at_the_viewport()
    {
        var (root, host, content, bar) = Tree();
        host.ContentViewport = new Vector2(84f, 50f);
        host.ContentOffset = new Vector2(0f, 30f);

        Assert.Same(content, root.Pick(15, 65));
        Assert.Same(bar, root.Pick(95, 30));
        // Content is on screen at x 10..50 only; x 88 is in the bar's column.
        Assert.NotSame(content, root.Pick(98, 65));
    }

    [Fact]
    public void Content_scrolled_out_of_the_viewport_is_not_hit()
    {
        var (root, host, content, _) = Tree();
        host.ContentViewport = new Vector2(84f, 50f);

        // Unscrolled, the content sits at y 80..110, below the 50-point viewport.
        Assert.NotSame(content, root.Pick(15, 85));
    }

    [Fact]
    public void Drawing_offsets_and_clips_content_but_not_chrome()
    {
        var (root, host, _, _) = Tree();
        host.ContentViewport = new Vector2(84f, 50f);
        host.ContentOffset = new Vector2(0f, 30f);

        (var renderer, var context) = ThemeDrawCapture.Context(root.Width, root.Height);
        root.Draw(context);
        var vertices = ThemeDrawCapture.Vertices(renderer);
        var red = vertices.Where(v => v.Color == Red).Select(v => v.Position.Y).ToList();
        var blue = vertices.Where(v => v.Color == Blue).Select(v => v.Position.Y).ToList();

        // Content drawn from y 50 (80 - 30), cut at the viewport's bottom (70).
        Assert.Equal(50f, red.Min(), 2);
        Assert.Equal(70f, red.Max(), 2);
        Assert.Equal(20f, blue.Min(), 2);
        Assert.Equal(70f, blue.Max(), 2);
    }
}
```

- [ ] **Step 2: Run them to see them fail**

```bash
export DOTNET_ROOT=$HOME/.dotnet PATH=$HOME/.dotnet:$PATH
dotnet test tests/AcDream.App.Tests --filter "FullyQualifiedName~UiContentOffsetTests"
```

Expected: the build fails: `UiElement` has no `ContentViewport`, `ContentOffset` or `ScrollChrome`.

- [ ] **Step 3: Implement**

The draw loop pays nothing for elements without a viewport: no extra push or pop, so Classic draws stay byte-identical.

In `src/AcDream.App/UI/UiElement.cs`, replace:

```csharp
            var p = new Vector2(Left, Top);
            var parent = Parent;
            while (parent is not null)
            {
                p += new Vector2(parent.Left, parent.Top);
                parent = parent.Parent;
            }
            return p;
        }
    }
```

with:

```csharp
            var p = new Vector2(Left, Top);
            UiElement child = this;
            var parent = Parent;
            while (parent is not null)
            {
                p += new Vector2(parent.Left, parent.Top) - parent.OffsetFor(child);
                child = parent;
                parent = parent.Parent;
            }
            return p;
        }
    }

    // ── Scrolling ───────────────────────────────────────────────────────

    /// <summary>
    /// Set on a child that stays put while its parent scrolls (a scrollbar):
    /// it is neither offset nor clipped to the parent's viewport.
    /// </summary>
    internal bool ScrollChrome { get; set; }

    /// <summary>
    /// The size of the part of this element its scrolled children are seen
    /// through, from its top-left corner; null (the default) for an element
    /// that does not scroll. While set, every child except
    /// <see cref="ScrollChrome"/> ones is clipped to it and moved by
    /// <see cref="ContentOffset"/>, in drawing, hit-testing and
    /// <see cref="ScreenPosition"/> alike.
    /// </summary>
    internal Vector2? ContentViewport { get; set; }

    /// <summary>How far this element's content is scrolled, in points; used only while <see cref="ContentViewport"/> is set.</summary>
    internal Vector2 ContentOffset { get; set; }

    /// <summary>How far <paramref name="child"/> is moved by this element's scrolling.</summary>
    private Vector2 OffsetFor(UiElement child) =>
        ContentViewport is null || child.ScrollChrome ? Vector2.Zero : ContentOffset;
```

In `src/AcDream.App/UI/UiElement.cs`, replace:

```csharp
                        OnDrawingChild(ctx, ordered[i]);
                        ordered[i].DrawSelfAndChildren(ctx);
                    }
```

with:

```csharp
                        OnDrawingChild(ctx, ordered[i]);
                        if (ContentViewport is { } viewport && !ordered[i].ScrollChrome)
                            DrawScrolled(ctx, ordered[i], viewport);
                        else
                            ordered[i].DrawSelfAndChildren(ctx);
                    }
```

In `src/AcDream.App/UI/UiElement.cs`, replace:

```csharp

    internal void DrawOverlays(UiRenderContext ctx)
    {
```

with:

```csharp

    /// <summary>Draws a scrolled child: clipped to the viewport, moved by the content offset.</summary>
    private void DrawScrolled(UiRenderContext ctx, UiElement child, Vector2 viewport)
    {
        ctx.PushClip(0f, 0f, viewport.X, viewport.Y);
        ctx.PushTransform(-ContentOffset.X, -ContentOffset.Y);
        try
        {
            child.DrawSelfAndChildren(ctx);
        }
        finally
        {
            ctx.PopTransform();
            ctx.PopClip();
        }
    }

    internal void DrawOverlays(UiRenderContext ctx)
    {
```

In `src/AcDream.App/UI/UiElement.cs`, replace:

```csharp
                    for (int i = 0; i < ordered.Length; i++)
                        ordered[i].DrawOverlays(ctx);
                }
```

with:

```csharp
                    for (int i = 0; i < ordered.Length; i++)
                    {
                        if (ContentViewport is null || ordered[i].ScrollChrome)
                        {
                            ordered[i].DrawOverlays(ctx);
                            continue;
                        }
                        ctx.PushTransform(-ContentOffset.X, -ContentOffset.Y);
                        try { ordered[i].DrawOverlays(ctx); }
                        finally { ctx.PopTransform(); }
                    }
                }
```

In `src/AcDream.App/UI/UiElement.cs`, replace:

```csharp
                var c = ordered[i];
                var childHit = c.HitTest(localX - c.Left, localY - c.Top);
                if (childHit is not null) return childHit;
```

with:

```csharp
                var c = ordered[i];
                float x = localX, y = localY;
                if (ContentViewport is { } viewport && !c.ScrollChrome)
                {
                    // Scrolled children are only reachable through the viewport.
                    if (x < 0f || y < 0f || x >= viewport.X || y >= viewport.Y) continue;
                    x += ContentOffset.X;
                    y += ContentOffset.Y;
                }
                var childHit = c.HitTest(x - c.Left, y - c.Top);
                if (childHit is not null) return childHit;
```

- [ ] **Step 4: Run them to see them pass**

```bash
dotnet test tests/AcDream.App.Tests --filter "FullyQualifiedName~UiContentOffsetTests"
```

Expected: Passed: 5, Failed: 0. The build reports 0 warnings.

- [ ] **Step 5: Commit**

```bash
git checkout -- '*.lock.json'
git add src/AcDream.App/UI/UiElement.cs tests/AcDream.App.Tests/UI/UiContentOffsetTests.cs
git commit -m "feat(ui): elements can scroll their content through a viewport" -m "Claude-Session: https://claude.ai/code/session_019dvJ5gakBkq3uJyr1PAC3u"
```

---
### Task 3: Horizontal wheel and Shift in the input path

**Goal:** The mouse surface reports `(dx, dy)` steps, the binding adds Shift, and `UiRoot.OnScroll(dx, dy, shift)` routes horizontal steps (and Shift-turned vertical ones) as `ScrollHorizontal` while vertical steps keep today's routing.

**Files:**
- Create: `tests/AcDream.App.Tests/UI/UiRootWheelRoutingTests.cs`
- Test: `tests/AcDream.App.Tests/UI/SilkRetainedMouseScrollTests.cs`
- Test: `tests/AcDream.App.Tests/UI/RetainedUiInputBindingTests.cs`
- Modify: `src/AcDream.App/UI/UiEvent.cs`
- Modify: `src/AcDream.App/UI/UiRoot.cs`
- Modify: `src/AcDream.App/UI/RetainedUiInputBinding.cs`

**Acceptance Criteria:**
- [ ] A Silk wheel event reports one `(sign X, sign Y)` step; a diagonal one both at once.
- [ ] A vertical step reaches the element under the pointer exactly as `OnScroll(dy)` did; `OnScroll(dy)` forwards with `dx` 0.
- [ ] A horizontal step bubbles as `ScrollHorizontal` past an element that does not take it; Shift turns a vertical step into one.
- [ ] Over the world only a Shift step falls through, as vertical; a native horizontal step goes nowhere.
- [ ] The binding passes both steps through and reads Shift from the root (false with no keyboard).

**Verify:** `dotnet test tests/AcDream.App.Tests --filter "FullyQualifiedName~UiRootWheelRoutingTests|FullyQualifiedName~SilkRetainedMouseScrollTests|FullyQualifiedName~RetainedUiInputBindingTests" → Passed: 35, Failed: 0`

**Steps:**

- [ ] **Step 1: Write the failing tests**

A recording element stands in for scrollable controls. The two existing input tests move to the `(dx, dy)` surface signature, and the binding gets a pass-through test.

Create `tests/AcDream.App.Tests/UI/UiRootWheelRoutingTests.cs`:

```csharp
using AcDream.App.UI;

namespace AcDream.App.Tests.UI;

/// <summary>
/// <see cref="UiRoot.OnScroll(int, int, bool)"/>: vertical steps keep their
/// routing, horizontal steps travel as <see cref="UiEventType.ScrollHorizontal"/>
/// to whatever consumes them, Shift turns a vertical step horizontal, and only
/// a Shift step reaches the world (as the vertical step it was).
/// </summary>
public sealed class UiRootWheelRoutingTests
{
    /// <summary>A box that records the wheel events it is offered and consumes the kinds it is told to.</summary>
    private sealed class Wheel : UiElement
    {
        public bool TakesVertical { get; init; }
        public bool TakesHorizontal { get; init; }
        public List<(int Type, int Step)> Seen { get; } = [];

        public override bool OnEvent(in UiEvent e)
        {
            if (e.Type is not (UiEventType.Scroll or UiEventType.ScrollHorizontal)) return false;
            Seen.Add((e.Type, e.Data0));
            return e.Type == UiEventType.Scroll ? TakesVertical : TakesHorizontal;
        }
    }

    private static (UiRoot Root, Wheel Outer, Wheel Inner, List<int> World) Tree(bool outerH, bool innerV)
    {
        var root = new UiRoot { Width = 400, Height = 300 };
        var outer = new Wheel { Width = 200, Height = 200, TakesHorizontal = outerH };
        var inner = new Wheel { Left = 10, Top = 10, Width = 50, Height = 50, TakesVertical = innerV };
        outer.AddChild(inner);
        root.AddChild(outer);
        var world = new List<int>();
        root.WorldScrollFallThrough += world.Add;
        root.OnMouseMove(20, 20);
        return (root, outer, inner, world);
    }

    [Fact]
    public void A_vertical_step_goes_to_the_element_under_the_pointer()
    {
        var (root, outer, inner, world) = Tree(outerH: true, innerV: true);

        root.OnScroll(0, -1, shift: false);

        Assert.Equal([(UiEventType.Scroll, -1)], inner.Seen);
        Assert.Empty(outer.Seen);
        Assert.Empty(world);
    }

    [Fact]
    public void The_one_argument_overload_is_a_vertical_step()
    {
        var (root, _, inner, _) = Tree(outerH: true, innerV: true);

        root.OnScroll(1);

        Assert.Equal([(UiEventType.Scroll, 1)], inner.Seen);
    }

    [Fact]
    public void A_horizontal_step_bubbles_past_an_element_that_only_scrolls_vertically()
    {
        var (root, outer, inner, _) = Tree(outerH: true, innerV: true);

        root.OnScroll(-1, 0, shift: false);

        Assert.Equal([(UiEventType.ScrollHorizontal, -1)], inner.Seen);
        Assert.Equal([(UiEventType.ScrollHorizontal, -1)], outer.Seen);
    }

    [Fact]
    public void Shift_turns_a_vertical_step_horizontal()
    {
        var (root, outer, inner, world) = Tree(outerH: true, innerV: true);

        root.OnScroll(0, 1, shift: true);

        Assert.Equal([(UiEventType.ScrollHorizontal, 1)], inner.Seen);
        Assert.Equal([(UiEventType.ScrollHorizontal, 1)], outer.Seen);
        Assert.Empty(world);
    }

    [Fact]
    public void A_shift_step_nothing_takes_over_the_interface_is_dropped()
    {
        var (root, _, _, world) = Tree(outerH: false, innerV: true);

        root.OnScroll(0, 1, shift: true);

        Assert.Empty(world);
    }

    [Fact]
    public void Over_the_world_only_a_shift_step_falls_through_as_vertical()
    {
        var (root, _, _, world) = Tree(outerH: true, innerV: true);
        root.OnMouseMove(350, 250);

        root.OnScroll(1, 0, shift: false);
        root.OnScroll(0, -1, shift: true);
        root.OnScroll(0, 1, shift: false);

        Assert.Equal([-1, 1], world);
    }

    [Fact]
    public void A_diagonal_step_routes_both_ways()
    {
        var (root, outer, inner, _) = Tree(outerH: true, innerV: true);

        root.OnScroll(1, -1, shift: false);

        Assert.Contains((UiEventType.ScrollHorizontal, 1), outer.Seen);
        Assert.Contains((UiEventType.Scroll, -1), inner.Seen);
    }
}
```

In `tests/AcDream.App.Tests/UI/SilkRetainedMouseScrollTests.cs`, replace:

```csharp
        var surface = new SilkRetainedMouseSurface(mouse);
        Action<int> onScroll = root.OnScroll;
        surface.AddScroll(onScroll);
```

with:

```csharp
        var surface = new SilkRetainedMouseSurface(mouse);
        Action<int, int> onScroll = (dx, dy) => root.OnScroll(dx, dy, shift: false);
        surface.AddScroll(onScroll);
```

In `tests/AcDream.App.Tests/UI/SilkRetainedMouseScrollTests.cs`, replace:

```csharp
        var steps = new List<int>();
        surface.AddScroll(steps.Add);
        ((WheelMouseProxy)mouse).Raise(mouse, delta);
```

with:

```csharp
        var steps = new List<int>();
        surface.AddScroll((_, dy) => steps.Add(dy));
        ((WheelMouseProxy)mouse).Raise(mouse, delta);
```

In `tests/AcDream.App.Tests/UI/SilkRetainedMouseScrollTests.cs`, replace:

```csharp
        var steps = new List<int>();
        surface.AddScroll(steps.Add);
        ((WheelMouseProxy)mouse).Raise(mouse, 0f);
        Assert.Empty(steps);
    }
```

with:

```csharp
        var steps = new List<int>();
        surface.AddScroll((_, dy) => steps.Add(dy));
        ((WheelMouseProxy)mouse).Raise(mouse, 0f);
        Assert.Empty(steps);
    }

    [Theory]
    [InlineData(0.01f, 1)]
    [InlineData(2f, 1)]
    [InlineData(-0.5f, -1)]
    public void HorizontalWheelEvent_emitsOneHorizontalStep(float delta, int expected)
    {
        IMouse mouse = DispatchProxy.Create<IMouse, WheelMouseProxy>();
        var surface = new SilkRetainedMouseSurface(mouse);
        var steps = new List<(int, int)>();
        surface.AddScroll((dx, dy) => steps.Add((dx, dy)));
        ((WheelMouseProxy)mouse).RaiseHorizontal(mouse, delta);
        Assert.Equal(new[] { (expected, 0) }, steps);
    }

    [Fact]
    public void DiagonalWheelEvent_emitsBothStepsTogether()
    {
        IMouse mouse = DispatchProxy.Create<IMouse, WheelMouseProxy>();
        var surface = new SilkRetainedMouseSurface(mouse);
        var steps = new List<(int, int)>();
        surface.AddScroll((dx, dy) => steps.Add((dx, dy)));
        ((WheelMouseProxy)mouse).Raise(mouse, new ScrollWheel(-0.2f, 0.4f));
        Assert.Equal(new[] { (-1, 1) }, steps);
    }
```

In `tests/AcDream.App.Tests/UI/SilkRetainedMouseScrollTests.cs`, replace:

```csharp
        public void Raise(IMouse mouse, float delta) => _scroll?.Invoke(mouse, new ScrollWheel(0, delta));
    }
}
```

with:

```csharp
        public void Raise(IMouse mouse, float delta) => _scroll?.Invoke(mouse, new ScrollWheel(0, delta));

        public void RaiseHorizontal(IMouse mouse, float delta) => _scroll?.Invoke(mouse, new ScrollWheel(delta, 0));

        public void Raise(IMouse mouse, ScrollWheel wheel) => _scroll?.Invoke(mouse, wheel);
    }
}
```

In `tests/AcDream.App.Tests/UI/RetainedUiInputBindingTests.cs`, replace:

```csharp

    private sealed class MouseSurface : IRetainedMouseSurface
    {
```

with:

```csharp

    [Fact]
    public void MouseScrollPassesBothStepsAndTheRootsShiftState()
    {
        var surface = new MouseSurface();
        var root = new UiRoot { Width = 800, Height = 600 };
        var world = new List<int>();
        root.WorldScrollFallThrough += world.Add;
        using var binding = new RetainedMouseInputBinding(surface, root, new HostQuiescenceGate());
        binding.Attach();

        surface.Scroll(0, -1);   // vertical over the world: falls through
        surface.Scroll(1, 0);    // horizontal over the world: goes nowhere

        Assert.Equal(new[] { -1 }, world);
        Assert.False(root.ShiftHeld);   // no keyboard attached
    }

    private sealed class MouseSurface : IRetainedMouseSurface
    {
```

In `tests/AcDream.App.Tests/UI/RetainedUiInputBindingTests.cs`, replace:

```csharp
        private Action<int, int>? _move;
        private Action<int>? _scroll;

        public void AddMouseDown(Action<MouseButton, int, int> callback) => Add(() => Down = callback);
        public void AddMouseUp(Action<MouseButton, int, int> callback) => Add(() => _up = callback);
        public void AddMouseMove(Action<int, int> callback) => Add(() => _move = callback);
        public void AddScroll(Action<int> callback) => Add(() => _scroll = callback);
        public void RemoveMouseDown(Action<MouseButton, int, int> callback) => Remove(() => Down = null);
```

with:

```csharp
        private Action<int, int>? _move;
        private Action<int, int>? _scroll;

        public void AddMouseDown(Action<MouseButton, int, int> callback) => Add(() => Down = callback);
        public void AddMouseUp(Action<MouseButton, int, int> callback) => Add(() => _up = callback);
        public void AddMouseMove(Action<int, int> callback) => Add(() => _move = callback);
        public void AddScroll(Action<int, int> callback) => Add(() => _scroll = callback);
        public void Scroll(int dx, int dy) => _scroll?.Invoke(dx, dy);
        public void RemoveMouseDown(Action<MouseButton, int, int> callback) => Remove(() => Down = null);
```

In `tests/AcDream.App.Tests/UI/RetainedUiInputBindingTests.cs`, replace:

```csharp
        }
        public void RemoveScroll(Action<int> callback) => Remove(() => _scroll = null);
```

with:

```csharp
        }
        public void RemoveScroll(Action<int, int> callback) => Remove(() => _scroll = null);
```

- [ ] **Step 2: Run them to see them fail**

```bash
export DOTNET_ROOT=$HOME/.dotnet PATH=$HOME/.dotnet:$PATH
dotnet test tests/AcDream.App.Tests --filter "FullyQualifiedName~UiRootWheelRoutingTests|FullyQualifiedName~SilkRetainedMouseScrollTests|FullyQualifiedName~RetainedUiInputBindingTests"
```

Expected: the build fails: `UiRoot.OnScroll` takes one argument, `UiEventType.ScrollHorizontal` and `UiRoot.ShiftHeld` do not exist, and `AddScroll` takes `Action<int>`.

- [ ] **Step 3: Implement**

`OnScroll(int dy)` keeps its signature and forwards. The old body becomes `RouteVertical`, unchanged. Tab's Shift check reuses `ShiftHeld`.

In `src/AcDream.App/UI/UiEvent.cs`, replace:

```csharp
    public const int Scroll       = 0x0A;
    public const int RightClick   = 0x0E;
    public const int DragBegin    = 0x15;
```

with:

```csharp
    public const int Scroll       = 0x0A;
    /// <summary>A horizontal wheel step (a sideways swipe, a tilt wheel, or Shift with the wheel); Data0 is +1 toward the start (left), -1 toward the end.</summary>
    public const int ScrollHorizontal = 0x0B;
    public const int RightClick   = 0x0E;
    public const int DragBegin    = 0x15;
```

In `src/AcDream.App/UI/UiRoot.cs`, replace:

```csharp

    public void OnScroll(int dy)
    {
```

with:

```csharp

    /// <summary>A vertical wheel step: +1 up, -1 down.</summary>
    public void OnScroll(int dy) => OnScroll(0, dy, shift: false);

    /// <summary>
    /// A wheel event with its horizontal and vertical steps (each -1, 0 or +1;
    /// +1 is left or up) and whether Shift was held. Shift turns a vertical
    /// step into a horizontal one before routing, as most applications do. A
    /// vertical step is routed as it always has been: to the element under the
    /// pointer and up through its parents until one consumes it, else to the
    /// world. A horizontal step goes the same way as a
    /// <see cref="UiEventType.ScrollHorizontal"/> event, which only a group
    /// scrolling horizontally consumes; nothing passes it to the world, except
    /// that a Shift step over no interface at all still reaches the world as
    /// the vertical step it was.
    /// </summary>
    public void OnScroll(int dx, int dy, bool shift)
    {
        if (shift && dy != 0 && dx == 0)
        {
            if (!RouteHorizontal(dy) && PopupHit(MouseX, MouseY) is null && HitTestTopDown(MouseX, MouseY).element is null)
                WorldScrollFallThrough?.Invoke(dy);
            return;
        }
        if (dx != 0) RouteHorizontal(dx);
        if (dy != 0) RouteVertical(dy);
    }

    /// <summary>Whether shift is held on the attached keyboard.</summary>
    internal bool ShiftHeld =>
        Keyboard?.IsKeyPressed(Silk.NET.Input.Key.ShiftLeft) == true
        || Keyboard?.IsKeyPressed(Silk.NET.Input.Key.ShiftRight) == true;

    private bool RouteHorizontal(int dx)
    {
        UiElement? target = PopupHit(MouseX, MouseY) ?? HitTestTopDown(MouseX, MouseY).element;
        if (target is null) return false;
        var sp = target.ScreenPosition;
        var e = new UiEvent(target.EventId, target, UiEventType.ScrollHorizontal, Data0: Math.Sign(dx),
                            Data1: (int)(MouseX - sp.X), Data2: (int)(MouseY - sp.Y));
        return BubbleEvent(target, in e);
    }

    private void RouteVertical(int dy)
    {
```

In `src/AcDream.App/UI/UiRoot.cs`, replace:

```csharp

        if (vk == (int)Silk.NET.Input.Key.Tab && MoveMarkupFocus(
                Keyboard?.IsKeyPressed(Silk.NET.Input.Key.ShiftLeft) == true
                || Keyboard?.IsKeyPressed(Silk.NET.Input.Key.ShiftRight) == true))
            return;
```

with:

```csharp

        if (vk == (int)Silk.NET.Input.Key.Tab && MoveMarkupFocus(ShiftHeld))
            return;
```

In `src/AcDream.App/UI/RetainedUiInputBinding.cs`, replace:

```csharp
    void RemoveMouseMove(Action<int, int> callback);
    void AddScroll(Action<int> callback);
    void RemoveScroll(Action<int> callback);
}
```

with:

```csharp
    void RemoveMouseMove(Action<int, int> callback);
    /// <summary>Wheel steps as (dx, dy), each -1, 0 or +1: +1 is left or up.</summary>
    void AddScroll(Action<int, int> callback);
    void RemoveScroll(Action<int, int> callback);
}
```

In `src/AcDream.App/UI/RetainedUiInputBinding.cs`, replace:

```csharp
    private Action<int, int>? _moveCallback;
    private Action<int>? _scrollCallback;
    private readonly Action<IMouse, MouseButton> _down;
```

with:

```csharp
    private Action<int, int>? _moveCallback;
    private Action<int, int>? _scrollCallback;
    private readonly Action<IMouse, MouseButton> _down;
```

In `src/AcDream.App/UI/RetainedUiInputBinding.cs`, replace:

```csharp

    public void AddScroll(Action<int> callback)
    {
        _scrollCallback = callback ?? throw new ArgumentNullException(nameof(callback));
        _mouse.Scroll += _scroll;
    }

    public void RemoveScroll(Action<int> callback)
    {
```

with:

```csharp

    public void AddScroll(Action<int, int> callback)
    {
        _scrollCallback = callback ?? throw new ArgumentNullException(nameof(callback));
        _mouse.Scroll += _scroll;
    }

    public void RemoveScroll(Action<int, int> callback)
    {
```

In `src/AcDream.App/UI/RetainedUiInputBinding.cs`, replace:

```csharp
    {
        // Each wheel event is a directional action, including small trackpad deltas.
        if (scroll.Y > 0f) _scrollCallback?.Invoke(1);
        else if (scroll.Y < 0f) _scrollCallback?.Invoke(-1);
    }
```

with:

```csharp
    {
        // Each wheel event is a directional action on each axis, including small trackpad deltas.
        int dx = Math.Sign(scroll.X), dy = Math.Sign(scroll.Y);
        if (dx != 0 || dy != 0) _scrollCallback?.Invoke(dx, dy);
    }
```

In `src/AcDream.App/UI/RetainedUiInputBinding.cs`, replace:

```csharp
    private readonly Action<int, int> _move;
    private readonly Action<int> _scroll;
    private readonly bool[] _attached = new bool[4];
```

with:

```csharp
    private readonly Action<int, int> _move;
    private readonly Action<int, int> _scroll;
    private readonly bool[] _attached = new bool[4];
```

In `src/AcDream.App/UI/RetainedUiInputBinding.cs`, replace:

```csharp
    private void OnMove(int x, int y) => Invoke(() => _root.OnMouseMove(x, y));
    private void OnScroll(int amount) => Invoke(() => _root.OnScroll(amount));
```

with:

```csharp
    private void OnMove(int x, int y) => Invoke(() => _root.OnMouseMove(x, y));
    private void OnScroll(int dx, int dy) => Invoke(() => _root.OnScroll(dx, dy, _root.ShiftHeld));
```

- [ ] **Step 4: Run them to see them pass**

```bash
dotnet test tests/AcDream.App.Tests --filter "FullyQualifiedName~UiRootWheelRoutingTests|FullyQualifiedName~SilkRetainedMouseScrollTests|FullyQualifiedName~RetainedUiInputBindingTests"
```

Expected: Passed: 35, Failed: 0. The build reports 0 warnings.

- [ ] **Step 5: Commit**

```bash
git checkout -- '*.lock.json'
git add src/AcDream.App/UI/RetainedUiInputBinding.cs src/AcDream.App/UI/UiEvent.cs src/AcDream.App/UI/UiRoot.cs tests/AcDream.App.Tests/UI/RetainedUiInputBindingTests.cs tests/AcDream.App.Tests/UI/SilkRetainedMouseScrollTests.cs tests/AcDream.App.Tests/UI/UiRootWheelRoutingTests.cs
git commit -m "feat(ui): horizontal wheel steps and Shift+wheel reach the retained interface" -m "Claude-Session: https://claude.ai/code/session_019dvJ5gakBkq3uJyr1PAC3u"
```

---
### Task 4: Scrolling groups

**Goal:** `scroll="x|y|both"` on a markup `<group>` (absolute or flex) scrolls its content: bars only on overflow, wheel and bar input, the offset in screen positions and hit-testing, focus scrolled into view, and flex sizing of scrolling groups.

**Files:**
- Create: `tests/AcDream.App.Tests/UI/UiScrollGroupTests.cs`
- Create: `src/AcDream.App/UI/UiScrollArea.cs`
- Create: `src/AcDream.App/UI/UiScrollPanel.cs`
- Modify: `src/AcDream.App/UI/UiFlexGroup.cs`
- Modify: `src/AcDream.App/UI/UiFlexBox.cs`
- Modify: `src/AcDream.App/UI/MarkupFlexAttributes.cs`
- Modify: `src/AcDream.App/UI/MarkupDocument.cs`
- Modify: `src/AcDream.App/UI/UiRoot.cs`

**Acceptance Criteria:**
- [ ] `scroll` builds a `UiScrollPanel` (absolute) or a scrolling `UiFlexGroup` (flex node `ScrollX`/`ScrollY`, `ScrollbarSize` 16); an unknown value fails the build; a group without `scroll` is the same `UiPanel`, still `ClickThrough`.
- [ ] Absolute extent: a child at y 150 in a 100-tall group gives `ContentHeight` 174, a bar at (84, 0, 16, 100) and viewport 84 × 100; negative coordinates are unreachable; a right-anchored child moves in beside the bar.
- [ ] Bar settling: a 90-wide child plus a far one in `scroll=both` shows both bars (viewport 84 × 84, corner empty).
- [ ] The wheel scrolls a group 48 points over empty space; a group with nothing to scroll lets it through; a list in a group scrolls itself; a horizontal step and Shift+wheel scroll an `x` group and pass a `y` group and a list.
- [ ] Focus moves a field 200 points down into view (scroll 124) and back (0), through nested groups (inner 64, outer 110).
- [ ] In a flex column an absolute `scroll=y` group shrinks to 76 and a scrolling grid of five 60 × 60 items shows both its lines without a bar.

**Verify:** `dotnet test tests/AcDream.App.Tests --filter "FullyQualifiedName~UiScrollGroupTests|FullyQualifiedName~MarkupFlex|FullyQualifiedName~FlexDemoSampleTests" → Passed: 120, Failed: 0`

**Steps:**

- [ ] **Step 1: Write the failing tests**

Markup-level tests through build, draw and the root's input routing. The theme test comes in Task 5.

Create `tests/AcDream.App.Tests/UI/UiScrollGroupTests.cs`:

```csharp
using System.Numerics;
using AcDream.App.UI;
using AcDream.App.UI.Layout.Flex;

namespace AcDream.App.Tests.UI;

/// <summary>
/// <c>scroll="x|y|both"</c> on groups and on a window's content area, through
/// the markup builder, the draw path and the root's input routing: extent,
/// bar settling, the content offset in drawing, hit-testing and screen
/// positions, wheel routing (nested lists, horizontal steps, Shift), focus
/// scrolling into view, clamping, and the window minimum. Headless metrics:
/// 7 points a character, 14-point lines, 24-point controls; bars are 16.
/// </summary>
public sealed class UiScrollGroupTests
{
    private sealed class Binding
    {
        public bool Shown { get; set; } = true;
        public IReadOnlyList<string> Lines { get; } = ["a", "b", "c", "d", "e", "f", "g", "h", "i", "j"];
        public int Selected => -1;
        public IReadOnlyList<string> Choices { get; } = ["one", "two"];
        public string Choice => "one";
    }

    private static (UiRoot Root, UiNineSlicePanel Frame) Mount(
        string body, object? binding = null, PluginUiThemeSettings? themes = null,
        string panel = "w=\"300\" h=\"200\"")
    {
        UiNineSlicePanel frame = MarkupDocument.Build(
            $"<panel x=\"0\" y=\"0\" {panel}>{body}</panel>", binding ?? new Binding(), _ => (0u, 0, 0), themes: themes);
        var root = new UiRoot { Width = 1280, Height = 720 };
        root.AddChild(frame);
        Frame(root);
        return (root, frame);
    }

    private static void Frame(UiRoot root)
    {
        root.Tick(0, 0);
        ThemeDrawCapture.Draw(root);
    }

    private static UiElement Find(UiElement root, string name) =>
        TryFind(root, name) ?? throw new InvalidOperationException($"no element named {name}");

    private static UiElement? TryFind(UiElement element, string name)
    {
        if (element.Name == name) return element;
        foreach (UiElement child in element.Children)
            if (TryFind(child, name) is { } found) return found;
        return null;
    }

    private static UiScrollArea Area(UiElement element) =>
        ((IUiScrollHost)element).ScrollArea ?? throw new InvalidOperationException("not scrolling");

    private static void Wheel(UiRoot root, int x, int y, int dx = 0, int dy = 0, bool shift = false)
    {
        root.OnMouseMove(x, y);
        root.OnScroll(dx, dy, shift);
        Frame(root);
    }

    // ── Markup ──────────────────────────────────────────────────────────

    [Theory]
    [InlineData("x", true, false)]
    [InlineData("y", false, true)]
    [InlineData("both", true, true)]
    public void Scroll_names_the_axes_of_an_absolute_or_flex_group(string value, bool x, bool y)
    {
        var (_, frame) = Mount($"""
            <group name="a" x="0" y="0" w="100" h="100" scroll="{value}" />
            <group name="f" x="100" y="0" w="100" h="100" layout="column" scroll="{value}" />
            """);

        Assert.IsType<UiScrollPanel>(Find(frame, "a"));
        Assert.Equal((x, y), (Area(Find(frame, "a")).ScrollsX, Area(Find(frame, "a")).ScrollsY));
        var flex = Assert.IsType<UiFlexGroup>(Find(frame, "f"));
        Assert.Equal((x, y), (flex.Flex.Node.ScrollX, flex.Flex.Node.ScrollY));
        Assert.Equal(UiScrollArea.BarSize, flex.Flex.Node.ScrollbarSize);
    }

    [Fact]
    public void An_unknown_scroll_value_is_a_build_error()
    {
        var error = Assert.Throws<FormatException>(() => Mount("<group name=\"g\" w=\"10\" h=\"10\" scroll=\"down\" />"));
        Assert.Contains("<group name=\"g\"> scroll=\"down\" must be x, y or both", error.Message);
    }

    [Fact]
    public void A_group_without_scroll_is_unchanged()
    {
        var (_, frame) = Mount("<group name=\"g\" x=\"0\" y=\"0\" w=\"100\" h=\"100\" />");

        UiElement group = Find(frame, "g");
        Assert.Equal(typeof(UiPanel), group.GetType());
        Assert.True(group.ClickThrough);
        Assert.Null(group.ContentViewport);
    }

    // ── Extent and bars ─────────────────────────────────────────────────

    [Fact]
    public void Content_that_fits_shows_no_bar()
    {
        var (_, frame) = Mount("""
            <group name="g" x="0" y="0" w="100" h="100" scroll="y">
              <button name="b" x="0" y="0" w="80" h="24" text="OK" />
            </group>
            """);

        UiScrollArea area = Area(Find(frame, "g"));
        Assert.False(area.Vertical.Visible);
        Assert.Equal(new Vector2(100f, 100f), Find(frame, "g").ContentViewport);
    }

    [Fact]
    public void Absolute_content_past_the_bottom_shows_the_vertical_bar_beside_the_viewport()
    {
        var (_, frame) = Mount("""
            <group name="g" x="0" y="0" w="100" h="100" scroll="y">
              <button name="b" x="0" y="150" w="50" h="24" text="OK" />
            </group>
            """);

        UiScrollArea area = Area(Find(frame, "g"));
        Assert.True(area.Vertical.Visible);
        Assert.Equal(new Vector4(84f, 0f, 16f, 100f),
            new Vector4(area.Vertical.Left, area.Vertical.Top, area.Vertical.Width, area.Vertical.Height));
        Assert.Equal(174, area.Y.ContentHeight);
        Assert.Equal(100, area.Y.ViewHeight);
        Assert.Equal(new Vector2(84f, 100f), Find(frame, "g").ContentViewport);
    }

    [Fact]
    public void Children_at_negative_coordinates_are_not_reachable()
    {
        var (_, frame) = Mount("""
            <group name="g" x="0" y="0" w="100" h="100" scroll="y">
              <button x="0" y="-60" w="50" h="24" text="Up" />
              <button x="0" y="60" w="50" h="24" text="Down" />
            </group>
            """);

        UiScrollArea area = Area(Find(frame, "g"));
        Assert.Equal(84, area.Y.ContentHeight);
        Assert.False(area.Vertical.Visible);
    }

    [Fact]
    public void A_right_anchored_child_moves_in_beside_the_bar()
    {
        var (_, frame) = Mount("""
            <group name="g" x="0" y="0" w="100" h="100" scroll="y">
              <button name="r" x="70" y="0" w="30" h="24" anchor="top right" text="R" />
              <button x="0" y="200" w="30" h="24" text="Far" />
            </group>
            """);

        Assert.Equal(54f, Find(frame, "r").Left);
    }

    [Fact]
    public void A_vertical_bar_that_makes_the_content_too_wide_brings_the_horizontal_bar()
    {
        var (_, frame) = Mount("""
            <group name="g" x="0" y="0" w="100" h="100" scroll="both">
              <button x="0" y="0" w="90" h="24" text="Wide" />
              <button x="0" y="150" w="30" h="24" text="Far" />
            </group>
            """);

        UiScrollArea area = Area(Find(frame, "g"));
        Assert.True(area.Vertical.Visible);
        Assert.True(area.Horizontal.Visible);
        Assert.Equal(new Vector2(84f, 84f), Find(frame, "g").ContentViewport);
        // The corner square is left empty.
        Assert.Equal(84f, area.Vertical.Height);
        Assert.Equal(84f, area.Horizontal.Width);
    }

    [Fact]
    public void A_scrolling_flex_column_keeps_its_items_at_their_size_beside_the_bar()
    {
        var (_, frame) = Mount("""
            <group name="g" x="0" y="0" w="120" h="60" layout="column" scroll="y">
              <button name="a" text="One" />
              <button name="b" text="Two" />
              <button name="c" text="Three" />
            </group>
            """);

        UiScrollArea area = Area(Find(frame, "g"));
        Assert.True(area.Vertical.Visible);
        Assert.Equal(new FlexRect(0f, 48f, 104f, 24f), Rect(Find(frame, "c")));
        Assert.Equal(72, area.Y.ContentHeight);
    }

    [Fact]
    public void A_nested_flex_group_scrolls_inside_its_flex_root()
    {
        var (_, frame) = Mount("""
            <group x="0" y="0" w="120" h="100" layout="column">
              <label text="Head" />
              <group name="g" grow="1" layout="column" scroll="y">
                <button text="One" />
                <button text="Two" />
                <button text="Three" />
                <button text="Four" />
              </group>
            </group>
            """);

        UiElement g = Find(frame, "g");
        Assert.Equal(new FlexRect(0f, 14f, 120f, 86f), Rect(g));
        Assert.True(Area(g).Vertical.Visible);
        Assert.Equal(96, Area(g).Y.ContentHeight);
    }

    private static FlexRect Rect(UiElement e) => new(e.Left, e.Top, e.Width, e.Height);

    // ── Offset ──────────────────────────────────────────────────────────

    [Fact]
    public void Scrolling_moves_the_content_in_screen_positions_and_hit_tests_but_not_the_bar()
    {
        var (root, frame) = Mount("""
            <group name="g" x="10" y="10" w="100" h="100" scroll="y">
              <button name="b" x="0" y="150" w="50" h="24" text="OK" />
            </group>
            """);
        UiElement group = Find(frame, "g"), button = Find(frame, "b");

        Area(group).Y.SetScrollY(70);

        Assert.Equal(new Vector2(10f, 90f), button.ScreenPosition);
        Assert.Same(button, root.Pick(20, 100));
        Assert.Equal(new Vector2(94f, 10f), Area(group).Vertical.ScreenPosition);
    }

    [Fact]
    public void Scrolled_content_outside_the_viewport_is_not_hit()
    {
        var (root, frame) = Mount("""
            <group name="g" x="0" y="0" w="100" h="100" scroll="y">
              <button name="b" x="0" y="90" w="50" h="40" text="OK" />
              <button x="0" y="200" w="50" h="24" text="Far" />
            </group>
            """);

        // The button's lower part, at y 100..130, is below the viewport.
        Assert.NotSame(Find(frame, "b"), root.Pick(20, 110));
    }

    [Fact]
    public void Scrolled_content_draws_offset_and_clipped_to_the_viewport()
    {
        var (root, frame) = Mount("""
            <group name="g" x="0" y="0" w="100" h="50" scroll="y">
              <group x="0" y="40" w="60" h="40" background="#FFFF0000" />
            </group>
            """);
        Area(Find(frame, "g")).Y.SetScrollY(30);

        (var renderer, var context) = ThemeDrawCapture.Context(root.Width, root.Height);
        root.Draw(context);
        var red = ThemeDrawCapture.Vertices(renderer)
            .Where(v => v.Color.X > 0.9f && v.Color.Y < 0.1f && v.Color.Z < 0.1f)
            .Select(v => v.Position.Y)
            .ToList();

        Assert.NotEmpty(red);
        Assert.Equal(10f, red.Min(), 2);
        Assert.Equal(50f, red.Max(), 2);
    }

    [Fact]
    public void A_menu_in_a_scrolled_group_opens_its_popup_where_the_menu_is()
    {
        var (_, frame) = Mount("""
            <group name="g" x="0" y="0" w="200" h="60" scroll="y">
              <menu name="m" x="0" y="80" w="100" h="24" items="{Choices}" selected="{Choice}" />
              <button x="0" y="200" w="50" h="24" text="Far" />
            </group>
            """);

        Area(Find(frame, "g")).Y.SetScrollY(50);

        Assert.Equal(new Vector2(0f, 30f), Find(frame, "m").ScreenPosition);
    }

    // ── Wheel ───────────────────────────────────────────────────────────

    [Fact]
    public void The_wheel_scrolls_a_group_by_three_lines_over_empty_space()
    {
        var (root, frame) = Mount("""
            <group name="g" x="0" y="0" w="100" h="100" scroll="y">
              <button x="0" y="250" w="50" h="24" text="Far" />
            </group>
            """);

        Wheel(root, 50, 50, dy: -1);

        Assert.Equal(3 * UiScrollArea.LineStep, Area(Find(frame, "g")).Y.ScrollY);
    }

    [Fact]
    public void A_group_with_nothing_to_scroll_lets_the_wheel_through()
    {
        var (root, frame) = Mount("""
            <group name="outer" x="0" y="0" w="100" h="100" scroll="y">
              <group name="inner" x="0" y="0" w="100" h="50" scroll="y" />
              <button x="0" y="250" w="50" h="24" text="Far" />
            </group>
            """);

        Wheel(root, 50, 20, dy: -1);

        Assert.Equal(48, Area(Find(frame, "outer")).Y.ScrollY);
    }

    [Fact]
    public void A_list_inside_a_scrolling_group_scrolls_itself()
    {
        var (root, frame) = Mount("""
            <group name="g" x="0" y="0" w="200" h="100" scroll="y">
              <list name="l" x="0" y="0" w="150" h="40" items="{Lines}" selected="{Selected}" />
              <button x="0" y="250" w="50" h="24" text="Far" />
            </group>
            """);

        Wheel(root, 20, 20, dy: -1);

        Assert.Equal(0, Area(Find(frame, "g")).Y.ScrollY);
    }

    [Fact]
    public void A_horizontal_step_scrolls_a_horizontal_group_and_passes_a_vertical_one()
    {
        var (root, frame) = Mount("""
            <group name="outer" x="0" y="0" w="100" h="100" scroll="x">
              <group name="inner" x="0" y="0" w="100" h="80" scroll="y">
                <button x="0" y="200" w="50" h="24" text="Down" />
              </group>
              <button x="250" y="0" w="50" h="24" text="Right" />
            </group>
            """);

        Wheel(root, 50, 40, dx: -1);

        Assert.Equal(48, Area(Find(frame, "outer")).X.ScrollY);
        Assert.Equal(0, Area(Find(frame, "inner")).Y.ScrollY);
    }

    [Fact]
    public void Shift_turns_the_wheel_horizontal_and_a_list_does_not_see_it()
    {
        var (root, frame) = Mount("""
            <group name="g" x="0" y="0" w="200" h="100" scroll="x">
              <list name="l" x="0" y="0" w="150" h="40" items="{Lines}" selected="{Selected}" />
              <button x="400" y="0" w="50" h="24" text="Right" />
            </group>
            """);

        Wheel(root, 20, 20, dy: -1, shift: true);

        Assert.Equal(48, Area(Find(frame, "g")).X.ScrollY);
    }

    [Fact]
    public void A_horizontal_step_over_the_world_goes_nowhere_but_a_shift_step_reaches_it_as_vertical()
    {
        var root = new UiRoot { Width = 1280, Height = 720 };
        var world = new List<int>();
        root.WorldScrollFallThrough += world.Add;
        root.OnMouseMove(600, 600);

        root.OnScroll(1, 0, shift: false);
        root.OnScroll(0, -1, shift: true);

        Assert.Equal(new[] { -1 }, world);
    }

    [Fact]
    public void The_old_vertical_entry_point_still_routes_vertically()
    {
        var (root, frame) = Mount("""
            <group name="g" x="0" y="0" w="100" h="100" scroll="y">
              <button x="0" y="250" w="50" h="24" text="Far" />
            </group>
            """);

        root.OnMouseMove(50, 50);
        root.OnScroll(-1);

        Assert.Equal(48, Area(Find(frame, "g")).Y.ScrollY);
    }

    // ── Bars ────────────────────────────────────────────────────────────

    [Fact]
    public void Pressing_below_the_thumb_pages_down()
    {
        var (root, frame) = Mount("""
            <group name="g" x="0" y="0" w="100" h="100" scroll="y">
              <button x="0" y="350" w="50" h="24" text="Far" />
            </group>
            """);

        root.OnMouseMove(92, 80);
        root.OnMouseDown(UiMouseButton.Left, 92, 80);
        root.OnMouseUp(UiMouseButton.Left, 92, 80);

        Assert.Equal(100, Area(Find(frame, "g")).Y.ScrollY);
    }

    // ── Position ────────────────────────────────────────────────────────

    [Fact]
    public void The_position_clamps_when_the_group_grows()
    {
        var (root, frame) = Mount("""
            <group name="g" x="0" y="0" w="100" h="100" scroll="y" anchor="top left bottom">
              <button x="0" y="150" w="50" h="24" text="Far" />
            </group>
            """, panel: "w=\"300\" h=\"200\" resizable=\"true\"");
        UiScrollArea area = Area(Find(frame, "g"));
        area.Y.ScrollToEnd();
        Assert.Equal(74, area.Y.ScrollY);

        frame.Height = 300f;
        Frame(root);

        Assert.Equal(0, area.Y.ScrollY);
        Assert.False(area.Vertical.Visible);
    }

    [Fact]
    public void The_position_survives_hide_and_show()
    {
        var binding = new Binding();
        var (root, frame) = Mount("""
            <group name="g" x="0" y="0" w="100" h="100" scroll="y" visible="{Shown}">
              <button x="0" y="250" w="50" h="24" text="Far" />
            </group>
            """, binding);
        Area(Find(frame, "g")).Y.SetScrollY(60);

        binding.Shown = false;
        Frame(root);
        binding.Shown = true;
        Frame(root);

        Assert.Equal(60, Area(Find(frame, "g")).Y.ScrollY);
    }

    [Fact]
    public void Focus_scrolls_a_field_below_the_viewport_into_view()
    {
        var (root, frame) = Mount("""
            <group name="g" x="0" y="0" w="200" h="100" scroll="y">
              <field name="f1" x="0" y="0" w="100" h="24" />
              <field name="f2" x="0" y="200" w="100" h="24" />
            </group>
            """);

        root.SetKeyboardFocus(Find(frame, "f2"));

        Assert.Equal(124, Area(Find(frame, "g")).Y.ScrollY);

        root.SetKeyboardFocus(Find(frame, "f1"));

        Assert.Equal(0, Area(Find(frame, "g")).Y.ScrollY);
    }

    [Fact]
    public void Focus_inside_nested_scrolling_groups_scrolls_each_of_them()
    {
        var (root, frame) = Mount("""
            <group name="outer" x="0" y="0" w="200" h="100" scroll="y">
              <group name="inner" x="0" y="150" w="200" h="60" scroll="y">
                <field name="f" x="0" y="100" w="100" h="24" />
              </group>
            </group>
            """);

        root.SetKeyboardFocus(Find(frame, "f"));
        Frame(root);

        Assert.Equal(64, Area(Find(frame, "inner")).Y.ScrollY);
        Assert.Equal(110, Area(Find(frame, "outer")).Y.ScrollY);
        Assert.Equal(new Vector2(0f, 76f), Find(frame, "f").ScreenPosition);
    }

    // ── Flex sizing ─────────────────────────────────────────────────────

    [Fact]
    public void An_absolute_scrolling_group_in_flex_may_shrink_to_its_viewport_minimum()
    {
        var (_, frame) = Mount("""
            <group x="0" y="0" w="200" h="100" layout="column">
              <group name="g" w="200" h="300" scroll="y" />
              <button name="b" text="OK" />
            </group>
            """);

        Assert.Equal(76f, Find(frame, "g").Height);
        Assert.Equal(76f, Find(frame, "b").Top);
    }

    [Fact]
    public void A_scrolling_grid_shows_every_line_when_its_window_has_room()
    {
        var (_, frame) = Mount("""
            <group x="0" y="0" w="200" h="300" layout="column">
              <group name="grid" layout="row" wrap="true" scroll="y">
                <group w="60" h="60" /><group w="60" h="60" /><group w="60" h="60" />
                <group w="60" h="60" /><group w="60" h="60" />
              </group>
              <button name="b" text="OK" />
            </group>
            """);

        Assert.Equal(120f, Find(frame, "grid").Height);
        Assert.False(Area(Find(frame, "grid")).Vertical.Visible);
    }
}
```

- [ ] **Step 2: Run them to see them fail**

```bash
export DOTNET_ROOT=$HOME/.dotnet PATH=$HOME/.dotnet:$PATH
dotnet test tests/AcDream.App.Tests --filter "FullyQualifiedName~UiScrollGroupTests|FullyQualifiedName~MarkupFlex|FullyQualifiedName~FlexDemoSampleTests"
```

Expected: the build fails: `UiScrollPanel`, `UiScrollArea`, `IUiScrollHost` and `UiFlexGroup.ScrollArea` do not exist.

- [ ] **Step 3: Implement**

`UiScrollArea` owns the bars as `ScrollChrome` children of the host at `ZOrder` `int.MaxValue`. The `settle` delegate is the host's own per-frame step, which `Reveal` runs first. The group case sets `ClickThrough = !scrolls` *after* building the host, because the area's constructor makes the host hit-testable and the old code set every group `ClickThrough`.

Create `src/AcDream.App/UI/UiScrollArea.cs`:

```csharp
using System.Numerics;
using AcDream.App.UI.Layout.Flex;

namespace AcDream.App.UI;

/// <summary>An element whose content can scroll: a markup <c>&lt;group scroll&gt;</c> or a scrolling content area.</summary>
internal interface IUiScrollHost
{
    /// <summary>The element's scrolling, or null when it does not scroll.</summary>
    UiScrollArea? ScrollArea { get; }
}

/// <summary>
/// The scrolling of one markup container (a group, or a window's content
/// area, with <c>scroll</c>): a scroll model and a 16-point bar per axis it
/// scrolls on, and the host's <see cref="UiElement.ContentViewport"/> and
/// <see cref="UiElement.ContentOffset"/>. A bar is shown only while the
/// content overflows on its axis, and is placed beside the viewport rather
/// than over it; when both show, the corner square stays empty.
///
/// <para>Each frame, before the host's children are drawn, the host settles
/// the bars and passes the content's extent here: a flex host reads what the
/// engine settled (<see cref="ApplyFlex"/>); an absolute host applies its
/// children's anchors against the viewport and measures them
/// (<see cref="SettleAbsolute"/>). The scroll position is kept across
/// hide/show and theme changes, clamped when the extent or viewport shrinks,
/// and never saved.</para>
/// </summary>
internal sealed class UiScrollArea
{
    /// <summary>A bar's thickness, which the engine reserves too.</summary>
    internal const float BarSize = FlexLayout.ScrollbarThickness;

    /// <summary>Points moved by a bar's arrow button.</summary>
    internal const int LineStep = 16;

    /// <summary>Lines moved by one wheel step.</summary>
    internal const int WheelLines = 3;

    private readonly UiElement _host;
    private readonly Action<UiScrollArea> _settle;

    /// <param name="settle">
    /// Settles the bars and extent from the host's current geometry (the
    /// host's own per-frame step); run before revealing a descendant, as a
    /// group scrolled out of its parent's view has not been drawn.
    /// </param>
    public UiScrollArea(
        UiElement host, bool scrollsX, bool scrollsY, Func<uint, (uint, int, int)> resolve, Action<UiScrollArea> settle)
    {
        _host = host;
        _settle = settle;
        ScrollsX = scrollsX;
        ScrollsY = scrollsY;
        // Hit-testable, so the wheel reaches it over empty space; a press there
        // still drags the window, as the host neither handles clicks nor captures.
        host.ClickThrough = false;

        Vertical = new UiScrollbar
        {
            Model = Y, Width = BarSize, SpriteResolve = resolve, Anchors = AnchorEdges.None,
            ScrollChrome = true, ZOrder = int.MaxValue, Visible = false,
        };
        RetailScrollbarChrome.ApplyVertical(Vertical);
        Horizontal = new UiScrollbar
        {
            Model = X, Horizontal = true, Height = BarSize, SpriteResolve = resolve, Anchors = AnchorEdges.None,
            ScrollChrome = true, ZOrder = int.MaxValue, Visible = false,
        };
        RetailScrollbarChrome.ApplyHorizontal(Horizontal);
        if (scrollsY) host.AddChild(Vertical);
        if (scrollsX) host.AddChild(Horizontal);

        X.PositionChanged += SyncOffset;
        Y.PositionChanged += SyncOffset;
    }

    public bool ScrollsX { get; }
    public bool ScrollsY { get; }

    /// <summary>The horizontal position: its <see cref="UiScrollable.ScrollY"/> is the offset along x.</summary>
    public UiScrollable X { get; } = new() { LineHeight = LineStep };

    public UiScrollable Y { get; } = new() { LineHeight = LineStep };

    internal UiScrollbar Vertical { get; }
    internal UiScrollbar Horizontal { get; }

    /// <summary>Takes the bars, viewport and extent a flex arrange settled for the host's node.</summary>
    internal void ApplyFlex(FlexNode node) =>
        Apply(node.ContentSize.Width, node.ContentSize.Height, node.Viewport.Width, node.Viewport.Height,
            node.ScrollbarX, node.ScrollbarY);

    /// <summary>
    /// Places an absolute host's children for its viewport and settles its
    /// bars the way the engine does: start without bars, apply the children's
    /// anchors against the viewport and take the extent, add every bar whose
    /// axis overflows, and repeat in the smaller viewport until the bars stop
    /// changing. The extent is the right and bottom edges of the visible
    /// children, from the origin: a child at negative coordinates is not
    /// reachable by scrolling.
    /// </summary>
    internal void SettleAbsolute()
    {
        float width = _host.Width, height = _host.Height;
        bool barX = false, barY = false;
        float viewW = MathF.Max(0f, width), viewH = MathF.Max(0f, height);
        for (int pass = 0; pass < 3; pass++)
        {
            (float right, float bottom) = PlaceAbsolute(viewW, viewH);
            bool needX = barX || (ScrollsX && right > viewW);
            bool needY = barY || (ScrollsY && bottom > viewH);
            if (needX == barX && needY == barY) break;
            barX = needX;
            barY = needY;
            viewW = MathF.Max(0f, width - (barY ? BarSize : 0f));
            viewH = MathF.Max(0f, height - (barX ? BarSize : 0f));
        }
        (float extentW, float extentH) = PlaceAbsolute(viewW, viewH);
        Apply(extentW, extentH, viewW, viewH, barX, barY);
    }

    private (float Right, float Bottom) PlaceAbsolute(float viewW, float viewH)
    {
        float right = 0f, bottom = 0f;
        IReadOnlyList<UiElement> children = _host.Children;
        for (int i = 0; i < children.Count; i++)
        {
            UiElement child = children[i];
            if (child.ScrollChrome) continue;
            child.ApplyAnchor(viewW, viewH);
            if (!child.Visible) continue;
            right = MathF.Max(right, child.Left + child.Width);
            bottom = MathF.Max(bottom, child.Top + child.Height);
        }
        return (right, bottom);
    }

    private void Apply(float extentW, float extentH, float viewW, float viewH, bool barX, bool barY)
    {
        _host.ContentViewport = new Vector2(viewW, viewH);
        X.SetExtents(ScrollsX ? (int)MathF.Ceiling(extentW) : 0, (int)MathF.Floor(viewW));
        Y.SetExtents(ScrollsY ? (int)MathF.Ceiling(extentH) : 0, (int)MathF.Floor(viewH));

        // A bar sits along the host's far edge, hidden or not, so it never reaches past the host.
        Vertical.Visible = barY;
        Vertical.Left = MathF.Max(0f, _host.Width - BarSize);
        Vertical.Top = 0f;
        Vertical.Height = viewH;
        Horizontal.Visible = barX;
        Horizontal.Left = 0f;
        Horizontal.Top = MathF.Max(0f, _host.Height - BarSize);
        Horizontal.Width = viewW;
        SyncOffset();
    }

    private void SyncOffset() =>
        _host.ContentOffset = new Vector2(ScrollsX ? X.ScrollY : 0f, ScrollsY ? Y.ScrollY : 0f);

    /// <summary>
    /// Scrolls with the wheel: a vertical step on an area that scrolls
    /// vertically, a horizontal step on one that scrolls horizontally, and
    /// only while there is something to scroll; anything else is left to
    /// bubble on to an outer area.
    /// </summary>
    internal bool OnEvent(in UiEvent e)
    {
        if (e.Type == UiEventType.Scroll && ScrollsY && Y.HasOverflow)
        {
            Y.ScrollByLines(-Math.Sign(e.Data0) * WheelLines);
            return true;
        }
        if (e.Type == UiEventType.ScrollHorizontal && ScrollsX && X.HasOverflow)
        {
            X.ScrollByLines(-Math.Sign(e.Data0) * WheelLines);
            return true;
        }
        return false;
    }

    /// <summary>
    /// Scrolls the least distance that brings <paramref name="target"/>, a
    /// descendant of the host, into view: its start when it is larger than
    /// the viewport. Uses the geometry of the last frame.
    /// </summary>
    internal void Reveal(UiElement target)
    {
        _settle(this);
        if (_host.ContentViewport is not { } viewport) return;
        Vector2 start = target.ScreenPosition - _host.ScreenPosition + _host.ContentOffset;
        if (ScrollsY) Y.SetScrollY(Into(Y.ScrollY, start.Y, target.Height, viewport.Y));
        if (ScrollsX) X.SetScrollY(Into(X.ScrollY, start.X, target.Width, viewport.X));
    }

    private static int Into(int offset, float start, float size, float view)
    {
        if (start < offset) return (int)MathF.Floor(start);
        if (start + size > offset + view) return (int)MathF.Ceiling(MathF.Min(start, start + size - view));
        return offset;
    }
}
```

Create `src/AcDream.App/UI/UiScrollPanel.cs`:

```csharp
namespace AcDream.App.UI;

/// <summary>
/// A markup <c>&lt;group scroll&gt;</c> without <c>layout</c>: an absolute
/// group whose children, placed by their coordinates and anchors against
/// its viewport, scroll when they reach past it.
/// </summary>
internal sealed class UiScrollPanel : UiPanel, IUiScrollHost
{
    public UiScrollPanel(bool scrollsX, bool scrollsY, Func<uint, (uint, int, int)> resolve) =>
        ScrollArea = new UiScrollArea(this, scrollsX, scrollsY, resolve, static area => area.SettleAbsolute());

    public UiScrollArea ScrollArea { get; }

    UiScrollArea? IUiScrollHost.ScrollArea => ScrollArea;

    private protected override void LayoutChildren() => ScrollArea.SettleAbsolute();

    public override bool OnEvent(in UiEvent e) => ScrollArea.OnEvent(e) || base.OnEvent(e);
}
```

In `src/AcDream.App/UI/UiFlexGroup.cs`, replace:

```csharp
/// container) it lays out its whole tree when drawn; nested in another flex
/// container its children are placed by that container's root.
/// </summary>
internal sealed class UiFlexGroup : UiPanel
{
    public UiFlexBox Flex { get; } = new();

    private protected override void LayoutChildren()
    {
        Flex.EnsureLayout(Width, Height);
        // Flex items carry no anchors, so this only reaches non-flex children (none in markup).
        base.LayoutChildren();
    }
}
```

with:

```csharp
/// container) it lays out its whole tree when drawn; nested in another flex
/// container its children are placed by that container's root. With
/// <c>scroll</c> its items lay out unbounded along the scrolling axes and are
/// seen through a viewport beside its bars.
/// </summary>
internal sealed class UiFlexGroup : UiPanel, IUiScrollHost
{
    public UiFlexBox Flex { get; } = new();

    public UiScrollArea? ScrollArea { get; private set; }

    /// <summary>Makes the group scroll on the given axes.</summary>
    internal void UseScroll(bool scrollsX, bool scrollsY, Func<uint, (uint, int, int)> resolve)
    {
        ScrollArea = new UiScrollArea(this, scrollsX, scrollsY, resolve, area => area.ApplyFlex(Flex.Node));
        Flex.Node.ScrollX = scrollsX;
        Flex.Node.ScrollY = scrollsY;
        Flex.Node.ScrollbarSize = UiScrollArea.BarSize;
    }

    private protected override void LayoutChildren()
    {
        Flex.EnsureLayout(Width, Height);
        // A nested scrolling group's node was arranged by its flex root.
        ScrollArea?.ApplyFlex(Flex.Node);
        // Flex items carry no anchors, so this only reaches non-flex children (none in markup).
        base.LayoutChildren();
    }

    public override bool OnEvent(in UiEvent e) => ScrollArea?.OnEvent(e) == true || base.OnEvent(e);
}
```

In `src/AcDream.App/UI/UiFlexBox.cs`, replace:

```csharp
    /// <summary>Adds a control or an absolute group as a measured leaf.</summary>
    public UiFlexItem AddLeaf(UiElement element, FlexNode node, FlexSize? authoredSize = null)
    {
```

with:

```csharp
    /// <summary>Adds a control or an absolute group as a measured leaf.</summary>
    public UiFlexItem AddLeaf(UiElement element, FlexNode node, FlexMeasurement? authoredSize = null)
    {
```

In `src/AcDream.App/UI/UiFlexBox.cs`, replace:

```csharp
    private readonly UiFlexBox _owner;
    private readonly FlexSize? _authoredSize;
    private string _text = string.Empty;
    private UiDatFont? _font;
    private bool _themed;

    internal UiFlexItem(UiFlexBox owner, UiElement element, FlexNode node, UiFlexBox? inner, FlexSize? authoredSize)
    {
```

with:

```csharp
    private readonly UiFlexBox _owner;
    private readonly FlexMeasurement? _authoredSize;
    private string _text = string.Empty;
    private UiDatFont? _font;
    private bool _themed;

    internal UiFlexItem(UiFlexBox owner, UiElement element, FlexNode node, UiFlexBox? inner, FlexMeasurement? authoredSize)
    {
```

In `src/AcDream.App/UI/UiFlexBox.cs`, replace:

```csharp
        if (_authoredSize is { } authored)
            return new FlexMeasurement(authored, authored);
        return MarkupContentSize.Measure(Element, _text) ?? default;
```

with:

```csharp
        if (_authoredSize is { } authored)
            return authored;
        return MarkupContentSize.Measure(Element, _text) ?? default;
```

In `src/AcDream.App/UI/MarkupFlexAttributes.cs`, replace:

```csharp
    /// <summary>
    /// A root panel's authored content-area size or limit, checked like a
    /// flex number: null when absent.
```

with:

```csharp
    /// <summary>
    /// The axes <c>scroll</c> asks <paramref name="el"/> (a group or the root
    /// panel) to scroll on; neither when it is absent.
    /// </summary>
    internal static (bool X, bool Y) Scroll(XElement el) => (string?)el.Attribute("scroll") switch
    {
        null => (false, false),
        "x" => (true, false),
        "y" => (false, true),
        "both" => (true, true),
        string other => throw Error(el, "scroll", other, "must be x, y or both"),
    };

    /// <summary>
    /// A root panel's authored content-area size or limit, checked like a
    /// flex number: null when absent.
```

In `src/AcDream.App/UI/MarkupDocument.cs`, replace:

```csharp
                UiPanel group;
                if (MarkupFlexAttributes.IsContainer(el))
                {
                    var flexGroup = new UiFlexGroup();
                    MarkupFlexAttributes.ReadContainer(el, flexGroup.Flex.Node);
                    groupFlex = flexGroup.Flex;
                    group = flexGroup;
                }
                else
                {
                    MarkupFlexAttributes.RejectContainer(el);
                    group = new UiPanel();
                }
```

with:

```csharp
                UiPanel group;
                var (scrollX, scrollY) = MarkupFlexAttributes.Scroll(el);
                bool scrolls = scrollX || scrollY;
                if (MarkupFlexAttributes.IsContainer(el))
                {
                    var flexGroup = new UiFlexGroup();
                    MarkupFlexAttributes.ReadContainer(el, flexGroup.Flex.Node);
                    if (scrolls) flexGroup.UseScroll(scrollX, scrollY, resolve);
                    groupFlex = flexGroup.Flex;
                    group = flexGroup;
                }
                else
                {
                    MarkupFlexAttributes.RejectContainer(el);
                    group = scrolls ? new UiScrollPanel(scrollX, scrollY, resolve) : new UiPanel();
                }
```

In `src/AcDream.App/UI/MarkupDocument.cs`, replace:

```csharp
                group.BorderThickness = el.Attribute("border") is null ? 0f : 1f;
                group.ClickThrough = true;
                BindColorSource(
```

with:

```csharp
                group.BorderThickness = el.Attribute("border") is null ? 0f : 1f;
                // A scrolling group is hit-testable so the wheel reaches it over empty space.
                group.ClickThrough = !scrolls;
                BindColorSource(
```

In `src/AcDream.App/UI/MarkupDocument.cs`, replace:

```csharp
            : flex.AddLeaf(element, new FlexNode(),
                el.Name.LocalName == "group" ? new FlexSize(element.Width, element.Height) : null);
        MarkupFlexAttributes.ReadItem(el, item.Node);
```

with:

```csharp
            : flex.AddLeaf(element, new FlexNode(),
                el.Name.LocalName == "group" ? AbsoluteGroupSize(element) : null);
        MarkupFlexAttributes.ReadItem(el, item.Node);
```

In `src/AcDream.App/UI/MarkupDocument.cs`, replace:

```csharp

    private static string ValidateIconKind(string? iconKind, string context = "iconkind") =>
        (iconKind ?? "did") switch
```

with:

```csharp

    /// <summary>
    /// An absolute group's content size in a flex container: its authored
    /// size, which is also its minimum, except along an axis it scrolls, where
    /// it can shrink to a 40-point viewport (plus the other axis's bar).
    /// </summary>
    private static FlexMeasurement AbsoluteGroupSize(UiElement group)
    {
        var size = new FlexSize(group.Width, group.Height);
        if (group is not UiScrollPanel { ScrollArea: var area }) return new FlexMeasurement(size, size);
        float minW = area.ScrollsX ? FlexLayout.MinimumScrollViewport + (area.ScrollsY ? UiScrollArea.BarSize : 0f) : size.Width;
        float minH = area.ScrollsY ? FlexLayout.MinimumScrollViewport + (area.ScrollsX ? UiScrollArea.BarSize : 0f) : size.Height;
        return new FlexMeasurement(size, new FlexSize(MathF.Min(minW, size.Width), MathF.Min(minH, size.Height)));
    }

    private static string ValidateIconKind(string? iconKind, string context = "iconkind") =>
        (iconKind ?? "did") switch
```

In `src/AcDream.App/UI/UiRoot.cs`, replace:

```csharp
            e.OnEvent(in gained);
        }
        KeyboardFocusChanged?.Invoke(previous, e);
    }
```

with:

```csharp
            e.OnEvent(in gained);
            RevealInScrollingAncestors(e);
        }
        KeyboardFocusChanged?.Invoke(previous, e);
    }

    /// <summary>Scrolls each scrolling ancestor of <paramref name="e"/>, innermost first, just far enough to show it.</summary>
    private static void RevealInScrollingAncestors(UiElement e)
    {
        for (UiElement? p = e.Parent; p is not null; p = p.Parent)
            if (p is IUiScrollHost { ScrollArea: { } area })
                area.Reveal(e);
    }
```

- [ ] **Step 4: Run them to see them pass**

```bash
dotnet test tests/AcDream.App.Tests --filter "FullyQualifiedName~UiScrollGroupTests|FullyQualifiedName~MarkupFlex|FullyQualifiedName~FlexDemoSampleTests"
```

Expected: Passed: 120, Failed: 0 (30 in `UiScrollGroupTests`). The build reports 0 warnings.

- [ ] **Step 5: Commit**

```bash
git checkout -- '*.lock.json'
git add src/AcDream.App/UI/MarkupDocument.cs src/AcDream.App/UI/MarkupFlexAttributes.cs src/AcDream.App/UI/UiFlexBox.cs src/AcDream.App/UI/UiFlexGroup.cs src/AcDream.App/UI/UiRoot.cs src/AcDream.App/UI/UiScrollArea.cs src/AcDream.App/UI/UiScrollPanel.cs tests/AcDream.App.Tests/UI/UiScrollGroupTests.cs
git commit -m "feat(ui): scrolling markup groups" -m "Claude-Session: https://claude.ai/code/session_019dvJ5gakBkq3uJyr1PAC3u"
```

---
### Task 5: Themed scrollbars

**Goal:** A scrolling group's bars follow the window's shared appearance (slim themed thumbs, Classic art under Classic), and a themed horizontal model bar draws a slim thumb along its track instead of the slider look.

**Files:**
- Test: `tests/AcDream.App.Tests/UI/UiScrollGroupTests.cs`
- Test: `tests/AcDream.App.Tests/UI/PluginThemeControlTests.cs`
- Modify: `src/AcDream.App/UI/UiScrollArea.cs`
- Modify: `src/AcDream.App/UI/MarkupDocument.cs`
- Modify: `src/AcDream.App/UI/PluginUiStyle.cs`
- Modify: `src/AcDream.App/UI/UiScrollbar.cs`

**Acceptance Criteria:**
- [ ] Switching a `theme="plugin"` window to Moss gives both bars the palette and turns retail art off; back to Classic restores the art.
- [ ] A themed horizontal bar over a 400/100 model draws a 4-point thumb centred in its 16-point lane, about a quarter of its 68-point track long.

**Verify:** `dotnet test tests/AcDream.App.Tests --filter "FullyQualifiedName~UiScrollGroupTests|FullyQualifiedName~PluginTheme" → Passed: 48, Failed: 0`

**Steps:**

- [ ] **Step 1: Write the failing tests**

Insert the theme test into `UiScrollGroupTests` before the "Position" heading, and add the thumb test to the end of `PluginThemeControlTests`.

In `tests/AcDream.App.Tests/UI/UiScrollGroupTests.cs`, replace:

```csharp
    // ── Position ──
```

with:

```csharp
    [Fact]
    public void Bars_take_the_window_theme_and_go_back_to_classic_art()
    {
        var themes = new PluginUiThemeSettings();
        var (root, frame) = Mount("""
            <group name="g" x="0" y="0" w="100" h="100" scroll="both" />
            """, themes: themes, panel: "w=\"300\" h=\"200\" theme=\"plugin\"");
        UiScrollArea area = Area(Find(frame, "g"));

        themes.Theme = PluginUiTheme.Moss;
        Frame(root);
        Assert.NotNull(area.Vertical.ThemePalette);
        Assert.NotNull(area.Horizontal.ThemePalette);
        Assert.False(area.Vertical.RetailArt);

        themes.Theme = PluginUiTheme.Classic;
        Frame(root);
        Assert.Null(area.Vertical.ThemePalette);
        Assert.True(area.Vertical.RetailArt);
    }

    // ── Position ──
```

In `tests/AcDream.App.Tests/UI/PluginThemeControlTests.cs`, replace:

```csharp
    }
}
```

with:

```csharp
    }

    [Fact]
    public void A_themed_horizontal_scroll_bar_draws_a_slim_thumb_along_its_track()
    {
        var model = new UiScrollable();
        model.SetExtents(400, 100);
        var bar = new UiScrollbar
        {
            Model = model, Horizontal = true, RetailArt = false, ThemePalette = P,
            Width = 100f, Height = 16f,
        };
        var root = new UiRoot { Width = 200, Height = 100 };
        root.AddChild(bar);

        (var renderer, var context) = ThemeDrawCapture.Context(200f, 100f);
        root.Draw(context);
        var thumb = ThemeDrawCapture.Vertices(renderer)
            .Where(v => v.Color.W > 0.05f && MathF.Abs(v.Color.X - P.Muted.X) < 0.004f && MathF.Abs(v.Color.Y - P.Muted.Y) < 0.004f)
            .ToList();

        // A 4-point thumb centred in the 16-point lane, a quarter of the 68-point track long.
        Assert.NotEmpty(thumb);
        Assert.InRange(thumb.Min(v => v.Position.Y), 5.5f, 6.5f);
        Assert.InRange(thumb.Max(v => v.Position.Y), 9.5f, 10.5f);
        Assert.InRange(thumb.Max(v => v.Position.X) - thumb.Min(v => v.Position.X), 16f, 18f);
    }
}
```

- [ ] **Step 2: Run them to see them fail**

```bash
export DOTNET_ROOT=$HOME/.dotnet PATH=$HOME/.dotnet:$PATH
dotnet test tests/AcDream.App.Tests --filter "FullyQualifiedName~UiScrollGroupTests|FullyQualifiedName~PluginTheme"
```

Expected: the two new tests fail: the bars' `ThemePalette` stays null, and the horizontal bar draws no muted thumb.

- [ ] **Step 3: Implement**

The bars go through the existing `UiScrollbar` case of `PluginMarkupTheme.Register`, so they are themed exactly as a log's bar is.

In `src/AcDream.App/UI/UiScrollArea.cs`, replace:

```csharp
    }
}
```

with:

```csharp
    }

    /// <summary>Gives both bars the window's theme (null: Classic art).</summary>
    internal void RegisterTheme(UiPluginMarkupPanel panel)
    {
        PluginMarkupTheme.Register(panel, Vertical, new System.Xml.Linq.XElement("scroll"));
        PluginMarkupTheme.Register(panel, Horizontal, new System.Xml.Linq.XElement("scroll"));
    }
}
```

In `src/AcDream.App/UI/MarkupDocument.cs`, replace:

```csharp
                    group = scrolls ? new UiScrollPanel(scrollX, scrollY, resolve) : new UiPanel();
                }
```

with:

```csharp
                    group = scrolls ? new UiScrollPanel(scrollX, scrollY, resolve) : new UiPanel();
                }
                if (themedPanel is not null && group is IUiScrollHost { ScrollArea: { } groupScroll })
                    groupScroll.RegisterTheme(themedPanel);
```

In `src/AcDream.App/UI/PluginUiStyle.cs`, replace:

```csharp

    /// <summary>A row highlight inset from the container's sides.</summary>
    internal static void Row(UiRenderContext ctx, float x, float y, float w, float h, Vector4 color) =>
```

with:

```csharp

    /// <summary>A slim rounded scroll thumb <paramref name="w"/> long, centred in a horizontal lane <paramref name="laneHeight"/> tall.</summary>
    internal static void ScrollThumbHorizontal(
        UiRenderContext ctx, PluginUiPalette p, float x, float y, float w, float laneHeight, bool active)
    {
        float h = active ? ScrollThumbActiveWidth : ScrollThumbWidth;
        ctx.FillRoundedRect(x, y + (laneHeight - h) / 2f, w, h, h / 2f, active ? p.Text with { W = 0.7f } : p.Muted);
    }

    /// <summary>A row highlight inset from the container's sides.</summary>
    internal static void Row(UiRenderContext ctx, float x, float y, float w, float h, Vector4 color) =>
```

In `src/AcDream.App/UI/UiScrollbar.cs`, replace:

```csharp
        bool active = _draggingThumb || _hoveredThumb;
        if (Horizontal)
        {
```

with:

```csharp
        bool active = _draggingThumb || _hoveredThumb;
        if (Horizontal && Model is { } horizontalModel)
        {
            float trackLeft = AxisExtent(DecrementButtonExtent, Width);
            float trackLength = MathF.Max(0f,
                Width - AxisExtent(DecrementButtonExtent, Width) - AxisExtent(IncrementButtonExtent, Width));
            var (tx, tw) = ModelThumbRect(horizontalModel, trackLeft, trackLength);
            PluginUiStyle.ScrollThumbHorizontal(ctx, palette, tx, 0f, tw, Height, active);
            return;
        }
        if (Horizontal)
        {
```

- [ ] **Step 4: Run them to see them pass**

```bash
dotnet test tests/AcDream.App.Tests --filter "FullyQualifiedName~UiScrollGroupTests|FullyQualifiedName~PluginTheme"
```

Expected: Passed: 48, Failed: 0. The build reports 0 warnings.

- [ ] **Step 5: Commit**

```bash
git checkout -- '*.lock.json'
git add src/AcDream.App/UI/MarkupDocument.cs src/AcDream.App/UI/PluginUiStyle.cs src/AcDream.App/UI/UiScrollArea.cs src/AcDream.App/UI/UiScrollbar.cs tests/AcDream.App.Tests/UI/PluginThemeControlTests.cs tests/AcDream.App.Tests/UI/UiScrollGroupTests.cs
git commit -m "feat(ui): scrollbars of markup groups follow the plugin appearance" -m "Claude-Session: https://claude.ai/code/session_019dvJ5gakBkq3uJyr1PAC3u"
```

---
### Task 6: Scrolling window content area

**Goal:** `scroll` on the root `<panel>` scrolls the window's content area, for a flex root and for an absolute window with the title bar; a root without a content area rejects it; `scroll` enters the saved-size revision only when present.

**Files:**
- Create: `tests/AcDream.App.Tests/UI/MarkupScrollWindowTests.cs`
- Modify: `src/AcDream.App/UI/UiPluginContentHost.cs`
- Modify: `src/AcDream.App/UI/MarkupDocument.cs`
- Modify: `src/AcDream.App/UI/PluginWindowChrome.cs`

**Acceptance Criteria:**
- [ ] A 200 × 60 `layout=column scroll=y` window of four buttons shows a bar, lays its buttons 184 wide, and one wheel step scrolls it 36 (clamped).
- [ ] A `layout=column scroll=y` window's minimum height is the 40-point view plus 29 of chrome.
- [ ] A `titlebar=true scroll=y` absolute window scrolls its content area over a child at y 180 (`ContentHeight` 204).
- [ ] `scroll` on a root with neither `layout` nor `titlebar="true"` fails the build naming both.
- [ ] `AuthoredInputs` of a root without `scroll` is unchanged (9 entries); with it, `scroll=y` is appended and the revision differs.

**Verify:** `dotnet test tests/AcDream.App.Tests --filter "FullyQualifiedName~MarkupScrollWindowTests|FullyQualifiedName~PluginWindowChromeTests|FullyQualifiedName~MarkupTitleBarTests" → Passed: 43, Failed: 0`

**Steps:**

- [ ] **Step 1: Write the failing tests**

Window-level tests for a scrolling root.

Create `tests/AcDream.App.Tests/UI/MarkupScrollWindowTests.cs`:

```csharp
using System.Xml.Linq;
using AcDream.App.UI;
using AcDream.App.UI.Layout.Flex;

namespace AcDream.App.Tests.UI;

/// <summary>
/// <c>scroll</c> on the root panel: the window's content area scrolls, for a
/// flex root and for an absolute window with the title bar; the window's
/// minimum along a scrolling axis is a small viewport; the attribute needs a
/// content area; and it enters the saved-size revision only when present.
/// Headless metrics: 24-point controls; chrome 5 points a side and a 24-point bar.
/// </summary>
public sealed class MarkupScrollWindowTests
{
    private const string Buttons =
        "<button text=\"One\" /><button text=\"Two\" /><button text=\"Three\" /><button text=\"Four\" />";

    private static MarkupWindow Window(string attrs, string body = Buttons) =>
        MarkupDocument.BuildWindow(
            $"<panel x=\"100\" y=\"50\" {attrs}>{body}</panel>", new object(), _ => (0u, 0, 0), fallbackTitle: "Demo");

    private static UiRoot Mount(MarkupWindow window)
    {
        var root = new UiRoot { Width = 1280, Height = 720 };
        root.AddChild(window.Frame);
        window.Register(root.WindowManager, "plugin:demo:main", new PluginWindowVisibilityController(null, startVisible: true));
        root.Tick(0, 0);
        ThemeDrawCapture.Draw(root);
        return root;
    }

    [Fact]
    public void A_scrolling_flex_root_scrolls_its_content_area()
    {
        MarkupWindow window = Window("layout=\"column\" w=\"200\" h=\"60\" scroll=\"y\"");
        UiRoot root = Mount(window);

        var content = (UiPluginContentHost)window.ContentRoot;
        UiScrollArea area = content.ScrollArea!;
        Assert.True(content.Flex!.Node.ScrollY);
        Assert.True(area.Vertical.Visible);
        Assert.Equal(96, area.Y.ContentHeight);
        Assert.Equal(184f, content.Flex.Items[0].Element.Width);

        root.OnMouseMove(150, 100);
        root.OnScroll(0, -1, shift: false);

        Assert.Equal(36, area.Y.ScrollY);   // 48 asked, clamped to 96 - 60
    }

    [Fact]
    public void A_scrolling_flex_root_can_shrink_to_a_small_viewport()
    {
        MarkupWindow window = Window("layout=\"column\" scroll=\"y\"");

        // Content minimum: a 40-point viewport tall, the widest button plus the bar wide.
        Assert.Equal(FlexLayout.MinimumScrollViewport + 29f, window.Frame.MinHeight);
    }

    [Fact]
    public void A_titled_absolute_window_scrolls_its_content_area()
    {
        MarkupWindow window = Window(
            "w=\"200\" h=\"100\" titlebar=\"true\" scroll=\"y\"",
            "<button x=\"0\" y=\"0\" w=\"50\" h=\"24\" text=\"Top\" /><button x=\"0\" y=\"180\" w=\"50\" h=\"24\" text=\"Far\" />");
        Mount(window);

        var content = (UiPluginContentHost)window.ContentRoot;
        Assert.Null(content.Flex);
        Assert.True(content.ScrollArea!.Vertical.Visible);
        Assert.Equal(204, content.ScrollArea.Y.ContentHeight);
    }

    [Fact]
    public void Scroll_on_a_root_without_a_content_area_is_a_build_error()
    {
        var error = Assert.Throws<FormatException>(() => Window("w=\"200\" h=\"100\" scroll=\"y\"", ""));
        Assert.Contains("needs layout=\"row|column\" or titlebar=\"true\"", error.Message);
    }

    [Fact]
    public void Scroll_enters_the_revision_only_when_present()
    {
        string?[] plain = PluginWindowChrome.AuthoredInputs(XElement.Parse("<panel layout=\"row\" />"));
        string?[] scrolling = PluginWindowChrome.AuthoredInputs(XElement.Parse("<panel layout=\"row\" scroll=\"y\" />"));

        Assert.Equal(9, plain.Length);
        Assert.Equal(plain, scrolling[..^1]);
        Assert.Equal("scroll=y", scrolling[^1]);
        Assert.NotEqual(
            Window("layout=\"column\"").AuthoredGeometryRevision,
            Window("layout=\"column\" scroll=\"y\"").AuthoredGeometryRevision);
    }
}
```

- [ ] **Step 2: Run them to see them fail**

```bash
export DOTNET_ROOT=$HOME/.dotnet PATH=$HOME/.dotnet:$PATH
dotnet test tests/AcDream.App.Tests --filter "FullyQualifiedName~MarkupScrollWindowTests|FullyQualifiedName~PluginWindowChromeTests|FullyQualifiedName~MarkupTitleBarTests"
```

Expected: the build fails: `UiPluginContentHost` has no `ScrollArea`.

- [ ] **Step 3: Implement**

`UseScroll` is called after `UseFlex`, so a flex root's node gets the scroll settings. An absolute content area settles by anchors, like `UiScrollPanel`.

In `src/AcDream.App/UI/UiPluginContentHost.cs`, replace:

```csharp
/// </summary>
internal sealed class UiPluginContentHost : UiElement
{
```

with:

```csharp
/// </summary>
internal sealed class UiPluginContentHost : UiElement, IUiScrollHost
{
```

In `src/AcDream.App/UI/UiPluginContentHost.cs`, replace:

```csharp

    /// <summary>
    /// Raised on the tick after a layout found that the smallest size the
```

with:

```csharp

    /// <summary>The content area's scrolling (root <c>scroll</c>), or null.</summary>
    public UiScrollArea? ScrollArea { get; private set; }

    /// <summary>
    /// Raised on the tick after a layout found that the smallest size the
```

In `src/AcDream.App/UI/UiPluginContentHost.cs`, replace:

```csharp

    protected override void OnTick(double deltaSeconds)
    {
```

with:

```csharp

    /// <summary>Makes the content area scroll on the given axes; call after <see cref="UseFlex"/> for a flex root.</summary>
    internal void UseScroll(bool scrollsX, bool scrollsY, Func<uint, (uint, int, int)> resolve)
    {
        ScrollArea = new UiScrollArea(this, scrollsX, scrollsY, resolve, Settle);
        if (Flex is not { } flex) return;
        flex.Node.ScrollX = scrollsX;
        flex.Node.ScrollY = scrollsY;
        flex.Node.ScrollbarSize = UiScrollArea.BarSize;
    }

    private void Settle(UiScrollArea area)
    {
        if (Flex is { } flex) area.ApplyFlex(flex.Node);
        else area.SettleAbsolute();
    }

    public override bool OnEvent(in UiEvent e) => ScrollArea?.OnEvent(e) == true || base.OnEvent(e);

    protected override void OnTick(double deltaSeconds)
    {
```

In `src/AcDream.App/UI/UiPluginContentHost.cs`, replace:

```csharp
    {
        Flex?.EnsureLayout(Width, Height);
        base.LayoutChildren();
    }
```

with:

```csharp
    {
        if (Flex is { } flex)
        {
            flex.EnsureLayout(Width, Height);
            ScrollArea?.ApplyFlex(flex.Node);
            base.LayoutChildren();
            return;
        }
        if (ScrollArea is { } area) Settle(area);
        else base.LayoutChildren();
    }
```

In `src/AcDream.App/UI/MarkupDocument.cs`, replace:

```csharp
        bool hasContentArea = hasLayout || hasTitleBar;
        float chromeW = hasContentArea ? PluginWindowChrome.HorizontalInsets : 0f;
        float chromeH = hasContentArea ? PluginWindowChrome.VerticalInsetsFor(hasTitleBar) : 0f;
```

with:

```csharp
        bool hasContentArea = hasLayout || hasTitleBar;
        var (rootScrollX, rootScrollY) = MarkupFlexAttributes.Scroll(root);
        if ((rootScrollX || rootScrollY) && !hasContentArea)
            throw new FormatException(
                $"<panel scroll=\"{(string?)root.Attribute("scroll")}\"> scrolls the window's content area, "
                + "which needs layout=\"row|column\" or titlebar=\"true\" on the panel");
        float chromeW = hasContentArea ? PluginWindowChrome.HorizontalInsets : 0f;
        float chromeH = hasContentArea ? PluginWindowChrome.VerticalInsetsFor(hasTitleBar) : 0f;
```

In `src/AcDream.App/UI/MarkupDocument.cs`, replace:

```csharp
        }

        foreach (var el in root.Elements())
```

with:

```csharp
        }
        if (rootScrollX || rootScrollY)
        {
            content!.UseScroll(rootScrollX, rootScrollY, resolve);
            if (panel is UiPluginMarkupPanel scrollPanel) content.ScrollArea!.RegisterTheme(scrollPanel);
        }

        foreach (var el in root.Elements())
```

In `src/AcDream.App/UI/PluginWindowChrome.cs`, replace:

```csharp
    {
        var inputs = new string?[GeometryAttributes.Length + 1];
        for (int i = 0; i < GeometryAttributes.Length; i++)
            inputs[i] = (string?)root.Attribute(GeometryAttributes[i]);
        inputs[^1] = Version.ToString(CultureInfo.InvariantCulture);
        return inputs;
```

with:

```csharp
    {
        // scroll changes the window's minimum; it is added only when present,
        // so the revision of every window without it is what it was.
        string? scroll = (string?)root.Attribute("scroll");
        var inputs = new string?[GeometryAttributes.Length + (scroll is null ? 1 : 2)];
        for (int i = 0; i < GeometryAttributes.Length; i++)
            inputs[i] = (string?)root.Attribute(GeometryAttributes[i]);
        inputs[GeometryAttributes.Length] = Version.ToString(CultureInfo.InvariantCulture);
        if (scroll is not null) inputs[^1] = "scroll=" + scroll;
        return inputs;
```

- [ ] **Step 4: Run them to see them pass**

```bash
dotnet test tests/AcDream.App.Tests --filter "FullyQualifiedName~MarkupScrollWindowTests|FullyQualifiedName~PluginWindowChromeTests|FullyQualifiedName~MarkupTitleBarTests"
```

Expected: Passed: 43, Failed: 0 (5 in `MarkupScrollWindowTests`). The build reports 0 warnings.

- [ ] **Step 5: Commit**

```bash
git checkout -- '*.lock.json'
git add src/AcDream.App/UI/MarkupDocument.cs src/AcDream.App/UI/PluginWindowChrome.cs src/AcDream.App/UI/UiPluginContentHost.cs tests/AcDream.App.Tests/UI/MarkupScrollWindowTests.cs
git commit -m "feat(ui): scroll on the root panel scrolls the window's content area" -m "Claude-Session: https://claude.ai/code/session_019dvJ5gakBkq3uJyr1PAC3u"
```

---
### Task 7: PR 3 backlog: title-bar width floor and the dock minus

**Goal:** A window with a title bar is never narrower than 96 points (opening, minimum, and when its content minimum changes), and a layout window without a bar gets no dock "–".

**Files:**
- Test: `tests/AcDream.App.Tests/UI/MarkupFlexWindowTests.cs`
- Test: `tests/AcDream.App.Tests/UI/MarkupWindowMountTests.cs`
- Modify: `src/AcDream.App/UI/PluginWindowChrome.cs`
- Modify: `src/AcDream.App/UI/MarkupDocument.cs`
- Modify: `src/AcDream.App/UI/MarkupWindow.cs`
- Modify: `src/AcDream.App/UI/PluginSidePanel.cs`

**Acceptance Criteria:**
- [ ] An absolute `titlebar="true"` window authored 20 wide opens 96 wide with minimum 96, content 86, close button at 72; a bar-less layout window is still content + 10.
- [ ] Layout windows narrower than 96 open and stop at 96 (the five updated `MarkupFlexWindowTests`); a caption that needs 112 points still grows the window to 122.
- [ ] Docking a `layout` window with `titlebar="false"` adds no "–" to its frame; absolute windows without a bar keep it.

**Verify:** `dotnet test tests/AcDream.App.Tests --filter "FullyQualifiedName~MarkupFlexWindowTests|FullyQualifiedName~MarkupWindowMountTests|FullyQualifiedName~PluginSidePanel|FullyQualifiedName~MarkupTitleBarTests" → Passed: 83, Failed: 0`

**Steps:**

- [ ] **Step 1: Write the failing tests**

Update the five `MarkupFlexWindowTests` expectations the 96-point floor changes (the caption test needs a longer caption so its minimum still rises above 96), and add the floor and dock tests.

In `tests/AcDream.App.Tests/UI/MarkupFlexWindowTests.cs`, replace:

```csharp

        // Content: max(38, 28) + 16 wide; 24 + 4 + 14 + 16 tall.
        var content = (UiPluginContentHost)window.ContentRoot;
        Assert.Equal((54f, 58f), (content.Width, content.Height));
        Assert.Equal((64f, 87f), (window.Frame.Width, window.Frame.Height));
        Assert.Equal((64f, 87f), (window.Frame.MinWidth, window.Frame.MinHeight));
        Assert.Equal(64f, window.TitleBar!.Width);
        Assert.Equal(64f - PluginWindowChrome.TitleBarHeight, window.TitleBar.Close.Left);
    }
```

with:

```csharp

        // Content: 24 + 4 + 14 + 16 tall; max(38, 28) + 16 = 54 wide, widened
        // to fill the title bar's 96-point frame minimum.
        var content = (UiPluginContentHost)window.ContentRoot;
        Assert.Equal((86f, 58f), (content.Width, content.Height));
        Assert.Equal((96f, 87f), (window.Frame.Width, window.Frame.Height));
        Assert.Equal((96f, 87f), (window.Frame.MinWidth, window.Frame.MinHeight));
        Assert.Equal(96f, window.TitleBar!.Width);
        Assert.Equal(96f - PluginWindowChrome.TitleBarHeight, window.TitleBar.Close.Left);
    }
```

In `tests/AcDream.App.Tests/UI/MarkupFlexWindowTests.cs`, replace:

```csharp

        // Content minimum: 38 + 28 wide, 24 tall.
        Assert.Equal((76f, 53f), (window.Frame.Width, window.Frame.Height));
        Assert.Equal((76f, 53f), (window.Frame.MinWidth, window.Frame.MinHeight));
    }
```

with:

```csharp

        // Content minimum: 24 tall; 38 + 28 wide, under the bar's 96-point frame floor.
        Assert.Equal((96f, 53f), (window.Frame.Width, window.Frame.Height));
        Assert.Equal((96f, 53f), (window.Frame.MinWidth, window.Frame.MinHeight));
    }
```

In `tests/AcDream.App.Tests/UI/MarkupFlexWindowTests.cs`, replace:

```csharp

        Assert.Equal((40f, 229f), (window.Frame.MinWidth, window.Frame.MinHeight));
        Assert.Equal((310f, 229f), (window.Frame.Width, window.Frame.Height));
```

with:

```csharp

        // minw 30 + 10 is under the bar's floor, which still applies.
        Assert.Equal((96f, 229f), (window.Frame.MinWidth, window.Frame.MinHeight));
        Assert.Equal((310f, 229f), (window.Frame.Width, window.Frame.Height));
```

In `tests/AcDream.App.Tests/UI/MarkupFlexWindowTests.cs`, replace:

```csharp
        handle.Resized += _ => resized.Add("resized");
        Assert.Equal(60f, window.Frame.Width);

        binding.Caption = "ABCDEFGHIJ";   // 70 points
        Frame(root);                       // lays out, finds the new minimum
        Frame(root);                       // the tick reports it; the window grows

        Assert.Equal(80f, window.Frame.MinWidth);
        Assert.Equal(80f, window.Frame.Width);
        Assert.Equal(["resized"], resized);
        Frame(root);
        Assert.Equal(70f, window.ContentRoot.Width);
    }
```

with:

```csharp
        handle.Resized += _ => resized.Add("resized");
        Assert.Equal(96f, window.Frame.Width);   // the bar's floor

        binding.Caption = "ABCDEFGHIJKLMNOP";   // 112 points
        Frame(root);                             // lays out, finds the new minimum
        Frame(root);                             // the tick reports it; the window grows

        Assert.Equal(122f, window.Frame.MinWidth);
        Assert.Equal(122f, window.Frame.Width);
        Assert.Equal(["resized"], resized);
        Frame(root);
        Assert.Equal(112f, window.ContentRoot.Width);
    }
```

In `tests/AcDream.App.Tests/UI/MarkupFlexWindowTests.cs`, replace:

```csharp

        // Row: 38 (OK button) + 28 (ABCD) wide, 24 tall; chrome 10 wide, 29 tall.
        Assert.Equal((76f, 53f), (window.Frame.MinWidth, window.Frame.MinHeight));

        Assert.True(handle.ResizeTo(1, 1));

        Assert.Equal((76f, 53f), (window.Frame.Width, window.Frame.Height));
    }
```

with:

```csharp

        // Row: 24 tall plus 29 of chrome; 38 (OK button) + 28 (ABCD) + 10 wide, under the bar's 96 floor.
        Assert.Equal((96f, 53f), (window.Frame.MinWidth, window.Frame.MinHeight));

        Assert.True(handle.ResizeTo(1, 1));

        Assert.Equal((96f, 53f), (window.Frame.Width, window.Frame.Height));
    }

    [Fact]
    public void A_window_with_a_bar_is_never_narrower_than_the_bar_needs()
    {
        MarkupWindow absolute = Window("w=\"20\" h=\"20\" titlebar=\"true\"", body: "");
        MarkupWindow bare = Window("layout=\"row\" titlebar=\"false\"", body: "<label text=\"A\" />");

        Assert.Equal((96f, 96f), (absolute.Frame.Width, absolute.Frame.MinWidth));
        Assert.Equal(86f, absolute.ContentRoot.Width);
        Assert.Equal(96f - PluginWindowChrome.TitleBarHeight, absolute.TitleBar!.Close.Left);
        Assert.Equal(17f, bare.Frame.Width);   // 7 + 10: no bar, no floor
    }
```

In `tests/AcDream.App.Tests/UI/MarkupWindowMountTests.cs`, replace:

```csharp
    [Fact]
    public void ASavedSizeSurvivesTheSameBarMarkup()
    {
```

with:

```csharp
    [Fact]
    public void TheDockLeavesItsMinimizeOffALayoutWindowWithoutABar()
    {
        var root = new UiRoot { Width = 1280, Height = 720 };
        using var shelf = new PluginSidePanel(root.WindowManager, _ => (0u, 0, 0), null, new PluginUiThemeSettings());
        root.AddChild(shelf);

        MarkupWindow bare = MarkupDocument.BuildWindow(
            "<panel x=\"100\" y=\"100\" layout=\"column\" titlebar=\"false\"><button text=\"OK\" /></panel>",
            new object(), _ => (0u, 0, 0));
        root.AddChild(bare.Frame);
        RetailWindowHandle handle = bare.Register(root.WindowManager, "plugin:c:main",
            new PluginWindowVisibilityController(null, true));
        shelf.Add(new PluginUiOwner("c", "C"), new PluginPanelDescriptor("main", "C"), handle);

        Assert.DoesNotContain(bare.Frame.Children, c => c is UiSimpleButton { Text: "–" });

        root.WindowManager.Unregister("plugin:c:main");
    }

    [Fact]
    public void ASavedSizeSurvivesTheSameBarMarkup()
    {
```

- [ ] **Step 2: Run them to see them fail**

```bash
export DOTNET_ROOT=$HOME/.dotnet PATH=$HOME/.dotnet:$PATH
dotnet test tests/AcDream.App.Tests --filter "FullyQualifiedName~MarkupFlexWindowTests|FullyQualifiedName~MarkupWindowMountTests|FullyQualifiedName~PluginSidePanel|FullyQualifiedName~MarkupTitleBarTests"
```

Expected: seven tests fail: the five updated `MarkupFlexWindowTests` expectations (widths 54–76 where 96 is expected), the new floor test, and the dock test (a "–" is found).

- [ ] **Step 3: Implement**

`FrameMinimumWidth` is the one place that adds the side insets and applies the floor: at build, after the eager measurement, and in `TrackContentMinimum`, which no longer needs `chromeW`.

In `src/AcDream.App/UI/PluginWindowChrome.cs`, replace:

```csharp

    internal const string CloseTooltip = "Close";
    internal const string DockedCloseTooltip = "Close (reopen from the dock)";
```

with:

```csharp

    /// <summary>
    /// The narrowest frame a window with a title bar may be: the close button
    /// and its inset, the title's inset and gap, and room for about eight
    /// characters of title, so the button never covers the title.
    /// </summary>
    internal const float MinimumBarFrameWidth = 96f;

    /// <summary>A frame's minimum width from its content's: the content plus the side insets, floored for a title bar.</summary>
    internal static float FrameMinimumWidth(float contentMinimum, bool titleBar) =>
        MathF.Max(contentMinimum + HorizontalInsets, titleBar ? MinimumBarFrameWidth : 0f);

    internal const string CloseTooltip = "Close";
    internal const string DockedCloseTooltip = "Close (reopen from the dock)";
```

In `src/AcDream.App/UI/MarkupDocument.cs`, replace:

```csharp
        panel.Resizable = resizable;
        panel.MinWidth = FOr(root, "minw", contentW) + chromeW;
        panel.MinHeight = FOr(root, "minh", contentH) + chromeH;
```

with:

```csharp
        panel.Resizable = resizable;
        panel.MinWidth = hasContentArea
            ? PluginWindowChrome.FrameMinimumWidth(FOr(root, "minw", contentW), hasTitleBar)
            : FOr(root, "minw", contentW);
        panel.MinHeight = FOr(root, "minh", contentH) + chromeH;
```

In `src/AcDream.App/UI/MarkupDocument.cs`, replace:

```csharp
            panel.Width = contentW + chromeW; panel.Height = contentH + chromeH;
            panel.MinWidth = minW + chromeW; panel.MinHeight = minH + chromeH;
            content!.Width = contentW; content.Height = contentH;
            titleBar?.SetFrameWidth(panel.Width);
        }
```

with:

```csharp
            panel.Width = contentW + chromeW; panel.Height = contentH + chromeH;
            panel.MinWidth = PluginWindowChrome.FrameMinimumWidth(minW, hasTitleBar); panel.MinHeight = minH + chromeH;
            content!.Width = contentW; content.Height = contentH;
        }
        if (hasTitleBar && panel.Width < PluginWindowChrome.MinimumBarFrameWidth)
        {
            // A window narrower than its title bar needs opens at the bar's minimum.
            panel.Width = panel.MinWidth;
            content!.Width = panel.Width - chromeW;
        }
        titleBar?.SetFrameWidth(panel.Width);
```

In `src/AcDream.App/UI/MarkupDocument.cs`, replace:

```csharp
        if (rootFlex is not null)
            window.TrackContentMinimum(content!, chromeW, chromeH, authoredMinW, authoredMinH);
        return window;
```

with:

```csharp
        if (rootFlex is not null)
            window.TrackContentMinimum(content!, chromeH, authoredMinW, authoredMinH);
        return window;
```

In `src/AcDream.App/UI/MarkupWindow.cs`, replace:

```csharp
    internal void TrackContentMinimum(
        UiPluginContentHost content, float chromeW, float chromeH, float? authoredMinW, float? authoredMinH)
    {
        content.ContentMinimumChanged += minimum =>
        {
            Frame.MinWidth = (authoredMinW ?? minimum.Width) + chromeW;
            Frame.MinHeight = (authoredMinH ?? minimum.Height) + chromeH;
```

with:

```csharp
    internal void TrackContentMinimum(
        UiPluginContentHost content, float chromeH, float? authoredMinW, float? authoredMinH)
    {
        content.ContentMinimumChanged += minimum =>
        {
            Frame.MinWidth = PluginWindowChrome.FrameMinimumWidth(authoredMinW ?? minimum.Width, TitleBar is not null);
            Frame.MinHeight = (authoredMinH ?? minimum.Height) + chromeH;
```

In `src/AcDream.App/UI/PluginSidePanel.cs`, replace:

```csharp
                titleBar.Close.Tooltip = PluginWindowChrome.DockedCloseTooltip;
            }
            else
            {
                minimize = new PluginMinimizeButton(handle, _font, _themes)
```

with:

```csharp
                titleBar.Close.Tooltip = PluginWindowChrome.DockedCloseTooltip;
            }
            else if (handle.OuterFrame.Children.OfType<UiPluginContentHost>().Any())
            {
                // A layout window without a bar has content right up to its
                // 5-point border, where "-" would sit over it; it is closed
                // from its dock slot (or HidePanel), as the author chose no chrome.
            }
            else
            {
                minimize = new PluginMinimizeButton(handle, _font, _themes)
```

- [ ] **Step 4: Run them to see them pass**

```bash
dotnet test tests/AcDream.App.Tests --filter "FullyQualifiedName~MarkupFlexWindowTests|FullyQualifiedName~MarkupWindowMountTests|FullyQualifiedName~PluginSidePanel|FullyQualifiedName~MarkupTitleBarTests"
```

Expected: Passed: 83, Failed: 0. The build reports 0 warnings.

- [ ] **Step 5: Commit**

```bash
git checkout -- '*.lock.json'
git add src/AcDream.App/UI/MarkupDocument.cs src/AcDream.App/UI/MarkupWindow.cs src/AcDream.App/UI/PluginSidePanel.cs src/AcDream.App/UI/PluginWindowChrome.cs tests/AcDream.App.Tests/UI/MarkupFlexWindowTests.cs tests/AcDream.App.Tests/UI/MarkupWindowMountTests.cs
git commit -m "fix(ui): title-bar windows keep room for their title; no dock minus on bar-less layout windows" -m "Claude-Session: https://claude.ai/code/session_019dvJ5gakBkq3uJyr1PAC3u"
```

---
### Task 8: Docs and the FlexDemo gallery

**Goal:** `docs/plugin-ui-markup.md` documents scrolling groups and the PR 4 behaviour changes, and FlexDemo gains the "Flex Gallery" window (scrolling grid and sideways strip) and a scrolling settings window, with tests building them.

**Files:**
- Test: `tests/AcDream.App.Tests/UI/FlexDemoSampleTests.cs`
- Create: `samples/AcDream.Plugins.FlexDemo/gallery.xml`
- Modify: `samples/AcDream.Plugins.FlexDemo/settings.xml`
- Modify: `samples/AcDream.Plugins.FlexDemo/FlexDemoPlugin.cs`
- Modify: `samples/AcDream.Plugins.FlexDemo/AcDream.Plugins.FlexDemo.csproj`
- Modify: `docs/plugin-ui-markup.md`

**Acceptance Criteria:**
- [ ] The docs have a "Scrolling groups" section (markup, flex and absolute extent, bars, flex sizing, the content-area rule, wheel, Shift, focus, position) and list `scroll` on `panel` and `group`; the title bar, flex window, dock and show/hide notes match Tasks 6–7.
- [ ] `gallery.xml` builds: its grid shows a vertical bar over 212 points of content and scrolls 48 by wheel; its strip is 48 tall, shows a horizontal bar and scrolls 48 with Shift+wheel.
- [ ] The settings window shows a bar when resized to 100 tall.
- [ ] `samples/AcDream.Plugins.FlexDemo` builds Release at 0 warnings and copies `gallery.xml` to its output.

**Verify:** `dotnet test tests/AcDream.App.Tests --filter "FullyQualifiedName~FlexDemoSampleTests" → Passed: 4, Failed: 0; dotnet build samples/AcDream.Plugins.FlexDemo -c Release → 0 Warning(s)`

**Steps:**

- [ ] **Step 1: Write the failing tests**

The sample tests gain the scrolling settings window and the gallery. `AssertInside` skips scrolling groups, whose content reaches past them by design.

In `tests/AcDream.App.Tests/UI/FlexDemoSampleTests.cs`, replace:

```csharp
            Assert.True(child.Top + child.Height <= container.Height + 0.01f, $"{child.GetType().Name} overflows the height");
            if (child is UiFlexGroup) AssertInside(child);
        }
```

with:

```csharp
            Assert.True(child.Top + child.Height <= container.Height + 0.01f, $"{child.GetType().Name} overflows the height");
            // A scrolling group's content reaches past it by design.
            if (child is UiFlexGroup { ScrollArea: null }) AssertInside(child);
        }
```

In `tests/AcDream.App.Tests/UI/FlexDemoSampleTests.cs`, replace:

```csharp
    }
}
```

with:

```csharp
    }

    [Fact]
    public void The_settings_window_scrolls_when_it_is_made_short()
    {
        var (root, window) = Mount("settings.xml", new Settings());
        Assert.True(root.WindowManager.TryGet("settings.xml", out RetailWindowHandle handle));
        var content = (UiPluginContentHost)window.ContentRoot;
        Assert.False(content.ScrollArea!.Vertical.Visible);

        handle.ResizeTo(window.Frame.Width, 100f);
        root.Tick(0, 0);
        ThemeDrawCapture.Draw(root);

        Assert.Equal(100f, window.Frame.Height);
        Assert.True(content.ScrollArea.Vertical.Visible);
    }

    [Fact]
    public void The_gallery_wraps_its_icons_and_scrolls_them()
    {
        var (root, window) = Mount("gallery.xml", new object());
        AssertInside(window.ContentRoot);

        var grid = Assert.IsType<UiFlexGroup>(window.ContentRoot.Children[1]);
        Assert.True(grid.ScrollArea!.Vertical.Visible);
        // 30 icons of 32 at a 4-point gap, five a line beside the bar: six lines.
        Assert.Equal(212, grid.ScrollArea.Y.ContentHeight);

        var at = grid.ScreenPosition + new System.Numerics.Vector2(20f, 20f);
        root.OnMouseMove((int)at.X, (int)at.Y);
        root.OnScroll(0, -1, shift: false);

        Assert.Equal(48, grid.ScrollArea.Y.ScrollY);

        // The recent strip: twelve icons in a row that scrolls sideways, with Shift and the wheel.
        var strip = Assert.IsType<UiFlexGroup>(window.ContentRoot.Children[3]);
        Assert.True(strip.ScrollArea!.Horizontal.Visible);
        Assert.Equal(48f, strip.Height);
        at = strip.ScreenPosition + new System.Numerics.Vector2(20f, 10f);
        root.OnMouseMove((int)at.X, (int)at.Y);
        root.OnScroll(0, -1, shift: true);

        Assert.Equal(48, strip.ScrollArea.X.ScrollY);
    }
}
```

- [ ] **Step 2: Run them to see them fail**

```bash
export DOTNET_ROOT=$HOME/.dotnet PATH=$HOME/.dotnet:$PATH
dotnet test tests/AcDream.App.Tests --filter "FullyQualifiedName~FlexDemoSampleTests"
```

Expected: the two new sample tests fail: `gallery.xml` does not exist, and the settings window has no `ScrollArea`.

- [ ] **Step 3: Implement**

The gallery uses literal spell ids 1–42 (the icon resolver shows what each one is live). `GalleryBinding` binds nothing.

Create `samples/AcDream.Plugins.FlexDemo/gallery.xml`:

```xml
<panel x="760" y="140" w="236" h="240" title="Flex Gallery" theme="plugin" layout="column" padding="8" gap="6" resizable="true">
  <label text="Spells" color="theme:muted|#FF9AA99E" />
  <group layout="row" wrap="true" gap="4" scroll="y" grow="1">
    <icon spell="1" tooltip="Spell 1" />
    <icon spell="2" tooltip="Spell 2" />
    <icon spell="3" tooltip="Spell 3" />
    <icon spell="4" tooltip="Spell 4" />
    <icon spell="5" tooltip="Spell 5" />
    <icon spell="6" tooltip="Spell 6" />
    <icon spell="7" tooltip="Spell 7" />
    <icon spell="8" tooltip="Spell 8" />
    <icon spell="9" tooltip="Spell 9" />
    <icon spell="10" tooltip="Spell 10" />
    <icon spell="11" tooltip="Spell 11" />
    <icon spell="12" tooltip="Spell 12" />
    <icon spell="13" tooltip="Spell 13" />
    <icon spell="14" tooltip="Spell 14" />
    <icon spell="15" tooltip="Spell 15" />
    <icon spell="16" tooltip="Spell 16" />
    <icon spell="17" tooltip="Spell 17" />
    <icon spell="18" tooltip="Spell 18" />
    <icon spell="19" tooltip="Spell 19" />
    <icon spell="20" tooltip="Spell 20" />
    <icon spell="21" tooltip="Spell 21" />
    <icon spell="22" tooltip="Spell 22" />
    <icon spell="23" tooltip="Spell 23" />
    <icon spell="24" tooltip="Spell 24" />
    <icon spell="25" tooltip="Spell 25" />
    <icon spell="26" tooltip="Spell 26" />
    <icon spell="27" tooltip="Spell 27" />
    <icon spell="28" tooltip="Spell 28" />
    <icon spell="29" tooltip="Spell 29" />
    <icon spell="30" tooltip="Spell 30" />
  </group>
  <label text="Recent" color="theme:muted|#FF9AA99E" />
  <group layout="row" gap="4" scroll="x">
    <icon spell="31" tooltip="Spell 31" />
    <icon spell="32" tooltip="Spell 32" />
    <icon spell="33" tooltip="Spell 33" />
    <icon spell="34" tooltip="Spell 34" />
    <icon spell="35" tooltip="Spell 35" />
    <icon spell="36" tooltip="Spell 36" />
    <icon spell="37" tooltip="Spell 37" />
    <icon spell="38" tooltip="Spell 38" />
    <icon spell="39" tooltip="Spell 39" />
    <icon spell="40" tooltip="Spell 40" />
    <icon spell="41" tooltip="Spell 41" />
    <icon spell="42" tooltip="Spell 42" />
  </group>
</panel>
```

In `samples/AcDream.Plugins.FlexDemo/settings.xml`, replace:

```xml
<panel x="470" y="140" title="Flex Settings" theme="plugin" layout="column" padding="10" gap="8">
  <group layout="row" gap="8" align="center">
```

with:

```xml
<panel x="470" y="140" title="Flex Settings" theme="plugin" layout="column" padding="10" gap="8" resizable="true" scroll="y">
  <group layout="row" gap="8" align="center">
```

In `samples/AcDream.Plugins.FlexDemo/FlexDemoPlugin.cs`, replace:

```csharp
/// <summary>
/// Two windows laid out with markup flex instead of coordinates: a finder
/// (a toolbar over a list that takes the spare height, over a footer) and a
/// settings form whose summary line and button caption change length at
/// runtime, so the window grows to fit them. Resize the finder, switch the
/// shared appearance from the dock's gear, and press "Show details" to watch
/// both reflow.
/// </summary>
public sealed class FlexDemoPlugin : IAcDreamPlugin
{
    private IPluginHost? _host;
    private IDisposable? _finder;
    private IDisposable? _settings;
    private readonly FinderBinding _finderBinding = new();
```

with:

```csharp
/// <summary>
/// Three windows laid out with markup flex instead of coordinates: a finder
/// (a toolbar over a list that takes the spare height, over a footer), a
/// settings form whose summary line and button caption change length at
/// runtime, so the window grows to fit them, and a gallery of spell icons
/// that wraps and scrolls, over a strip of recent spells that scrolls
/// sideways (swipe, tilt the wheel, or hold Shift). Resize the finder and the
/// gallery, make the settings window short to scroll it, switch the shared
/// appearance from the dock's gear, and press "Show details" to watch them
/// reflow.
/// </summary>
public sealed class FlexDemoPlugin : IAcDreamPlugin
{
    private IPluginHost? _host;
    private IDisposable? _finder;
    private IDisposable? _settings;
    private IDisposable? _gallery;
    private readonly FinderBinding _finderBinding = new();
```

In `samples/AcDream.Plugins.FlexDemo/FlexDemoPlugin.cs`, replace:

```csharp
            Markup("settings.xml"), _settingsBinding);
    }
```

with:

```csharp
            Markup("settings.xml"), _settingsBinding);
        _gallery = host.Ui.RegisterPanel(
            new PluginPanelDescriptor("gallery", "Flex Gallery") { IconText = "FG", StartVisible = true },
            Markup("gallery.xml"), new GalleryBinding());
    }
```

In `samples/AcDream.Plugins.FlexDemo/FlexDemoPlugin.cs`, replace:

```csharp
        _settings = null;
    }
```

with:

```csharp
        _settings = null;
        _gallery?.Dispose();
        _gallery = null;
    }

    /// <summary>The gallery's icons are literal spell ids; it binds nothing.</summary>
    public sealed class GalleryBinding;
```

In `samples/AcDream.Plugins.FlexDemo/AcDream.Plugins.FlexDemo.csproj`, replace:

```xml
    <None Include="settings.xml" CopyToOutputDirectory="PreserveNewest" />
  </ItemGroup>
</Project>
```

with:

```xml
    <None Include="settings.xml" CopyToOutputDirectory="PreserveNewest" />
    <None Include="gallery.xml" CopyToOutputDirectory="PreserveNewest" />
  </ItemGroup>
</Project>
```

In `docs/plugin-ui-markup.md`, replace:

```markdown
|---|---|---|
| `panel` (root) | The window | `x y w h title visible resizable minw minh resize titlebar`, and the flex container attributes |
| `group` | Layout container | `x y w h background border`, and the flex container attributes |
| `label` | Text | `x y text color` |
```

with:

```markdown
|---|---|---|
| `panel` (root) | The window | `x y w h title visible resizable minw minh resize titlebar scroll`, and the flex container attributes |
| `group` | Layout container | `x y w h background border scroll`, and the flex container attributes |
| `label` | Text | `x y text color` |
```

In `docs/plugin-ui-markup.md`, replace:

```markdown
  are placed from the content area's corner;
- dragging the bar moves the window.
```

with:

```markdown
  are placed from the content area's corner;
- dragging the bar moves the window;
- the frame is never narrower than 96 points, so the close button always
  leaves room for the start of the title; a narrower window opens at 96.
```

In `docs/plugin-ui-markup.md`, replace:

```markdown
- as with the title bar, the window's saved size is reset once when its
  markup's `w`, `h`, `minw`, `minh`, `resizable`, `resize`, `layout` or
  `titlebar` change. Captions and appearance never reset it.

Windows only grow to fit: when the content later needs less room, the window
keeps its size. A wrapping root's minimum height is the height of its
narrowest layout, so a wide wrapping window cannot be made shorter than that.

A layout root with `titlebar="false"` shows no title text in the window (its
`title` still names it in the dock). A docked window without a bar keeps the
dock's "–" button at its top-right corner, over the corner of the content
area, so leave room there.

A root that wraps should state its `w`: without it the window opens one
line wide.
```

with:

````markdown
- as with the title bar, the window's saved size is reset once when its
  markup's `w`, `h`, `minw`, `minh`, `resizable`, `resize`, `layout`,
  `titlebar` or `scroll` change. Captions and appearance never reset it.

Windows only grow to fit: when the content later needs less room, the window
keeps its size. A wrapping root's minimum height is the height of its
narrowest layout, so a wide wrapping window cannot be made shorter than that;
give it `scroll="y"` (see [Scrolling groups](#scrolling-groups)) to let it.

A layout root with `titlebar="false"` shows no title text in the window (its
`title` still names it in the dock), and gets no dock "–" button either, since
its content reaches the corner where the button would sit: the player closes
it from its dock slot, the plugin with `HidePanel`.

A root that wraps should state its `w`: without it the window opens one
line wide.

## Scrolling groups

`scroll="y"`, `scroll="x"` or `scroll="both"` on a `<group>` makes it scroll
along those axes when its content does not fit; on the root `<panel>` it
scrolls the window's content area. Without `scroll` content that does not fit
is cut off, as before; any other value fails the build.

```xml
<panel title="Spells" layout="column" padding="8" gap="6" w="240" h="200" resizable="true">
  <label text="Known spells" />
  <group layout="row" wrap="true" gap="4" scroll="y" grow="1">
    <icon spell="{Spell1}" />
    <icon spell="{Spell2}" />
    <!-- ... -->
  </group>
</panel>
```

- **Flex groups** lay their items out without a limit along the scrolling
  axis: a scrolling column keeps every item at its size instead of shrinking
  it, and a wrapping row with `scroll="y"` keeps its width and grows downward.
  Items do not grow to fill a scrolling axis either; `grow` there has no space
  to share.
- **Groups without `layout`** scroll over their children as placed: the
  scrolled area reaches the right and bottom edges of the furthest visible
  child, measured from the group's top-left corner. A child at negative
  coordinates is not reachable; leave your own margin after the last child if
  you want one.
- A **bar** (16 points) appears on each scrolling axis only while the content
  overflows it, beside the content rather than over it: the content is laid
  out (or its anchors applied) in the room left, so a right-anchored child
  moves in beside the bar. When both bars show, the corner square stays empty.
  Classic uses the game's scrollbar art and the shared themes a slim thumb.
- **In a flex container** a scrolling group may shrink to a 40-point view
  along each axis it scrolls (plus the other bar's 16 points), so it never
  holds its window open. A scrolling wrapping row in a column shows all its
  lines when the window has room and scrolls when it does not. On the root,
  the window's minimum along a scrolling axis is that small view too.
- The root needs a content area to scroll: `scroll` on a root `<panel>`
  without `layout` and without `titlebar="true"` fails the build.

**The wheel** scrolls the innermost thing under the pointer that can scroll:
a `list` or `log` inside a scrolling group scrolls itself, and the group
scrolls everywhere else. A group with nothing to scroll lets the wheel pass to
a group around it. A sideways swipe, a tilt wheel, or Shift with the wheel
scrolls horizontally, and only `scroll="x"` and `scroll="both"` groups take
that; lists and logs ignore it. One wheel step moves 48 points (three 16-point
lines); the bar's arrows move 16 and a click in its track a page.

**Focus** moves into view: when Tab (or a click) gives a control keyboard
focus, each scrolling group around it scrolls just far enough to show it.

The scroll position survives hiding and showing the group and a change of
appearance, is pulled back when the group grows or its content shrinks, and
is not saved between sessions. Plugins cannot read or set it in this version.
````

In `docs/plugin-ui-markup.md`, replace:

```markdown
binding. The player closes a window with its title bar's X (see "Title
bar"), or, without a bar, with the dock's "–" on its corner or its dock
slot; each clears the player's request, so setting the binding back to true
does not reopen it. `ShowPanel` and `HidePanel` make that request from the
```

with:

```markdown
binding. The player closes a window with its title bar's X (see "Title
bar"), or, without a bar, with the dock's "–" on its corner (absolute windows
only) or its dock slot; each clears the player's request, so setting the binding back to true
does not reopen it. `ShowPanel` and `HidePanel` make that request from the
```

- [ ] **Step 4: Run them to see them pass**

```bash
dotnet test tests/AcDream.App.Tests --filter "FullyQualifiedName~FlexDemoSampleTests"
```

Expected: Passed: 4, Failed: 0. The build reports 0 warnings.

- [ ] **Step 5: Commit**

```bash
git checkout -- '*.lock.json'
git add docs/plugin-ui-markup.md samples/AcDream.Plugins.FlexDemo/AcDream.Plugins.FlexDemo.csproj samples/AcDream.Plugins.FlexDemo/FlexDemoPlugin.cs samples/AcDream.Plugins.FlexDemo/gallery.xml samples/AcDream.Plugins.FlexDemo/settings.xml tests/AcDream.App.Tests/UI/FlexDemoSampleTests.cs
git commit -m "docs(ui): scrolling groups; FlexDemo gallery and scrolling settings" -m "Claude-Session: https://claude.ai/code/session_019dvJ5gakBkq3uJyr1PAC3u"
```

---
### Task 9: Full verification, review, live gate and merge

**Goal:** The branch builds clean, fails exactly main's tests, passes a code review and the live gate, and is merged into fork main with the user's go-ahead.

**Files:**
- No planned source changes (review and live-gate fixes are committed on `flex/scroll` with their own messages).

**Acceptance Criteria:**
- [ ] `dotnet build AcDream.slnx -c Release` → `0 Warning(s)`, `0 Error(s)`.
- [ ] `AcDream.App.Tests` on `flex/scroll` fails exactly the tests main fails (166 environment failures at d54f047a), compared by name.
- [ ] A code review of `main..flex/scroll` reports no unresolved Critical/High issue.
- [ ] Live, on the local ACE server (10.10.20.20 via `~/OpenAC-dev/dev-client.env`, player account), in Classic, Moss and Brass (2× captures shown to the user):
  - the Flex Gallery's grid scrolls with the wheel, the trackpad and by dragging its thumb, and rewraps when resized;
  - its Recent strip scrolls with a sideways swipe or tilt wheel and with Shift+wheel, in the expected direction;
  - the settings window made short scrolls, and Tab to a hidden control scrolls it into view;
  - a menu opened in a scrolled area pops up at the menu;
  - the finder and the settings window look as they did in PR 3, apart from the settings window's bar when short.
- [ ] Live: Shift+wheel over the world still does what it did before (world zoom), and the wheel over a list in a scrolling area scrolls the list.
- [ ] The user has confirmed merging into fork main (`--no-ff`); nothing is pushed without their word.

**Verify:** `git log --oneline main..flex/scroll` lists the Task 1–8 commits; the Release build line above; the failure comparison prints nothing.

**Steps:**

- [ ] **Step 1: Release build**

```bash
export DOTNET_ROOT=$HOME/.dotnet PATH=$HOME/.dotnet:$PATH
dotnet build AcDream.slnx -c Release 2>&1 | grep -E "Warning\(s\)|Error\(s\)"
git checkout -- '*.lock.json'
```

- [ ] **Step 2: Suite against the main baseline**

```bash
S="${TMPDIR:-/tmp}"
dotnet test tests/AcDream.App.Tests/AcDream.App.Tests.csproj 2>&1 | grep -E "^\s+Failed " | sed -E 's/ \[[^]]*\]$//' | sort -u > "$S/flex-branch-failures.txt"
cd /Users/davidsmith/code/OpenAC/OpenAC
git worktree add --detach .worktrees/baseline-main main
(cd .worktrees/baseline-main && dotnet test tests/AcDream.App.Tests/AcDream.App.Tests.csproj 2>&1 | grep -E "^\s+Failed " | sed -E 's/ \[[^]]*\]$//' | sort -u > "$S/flex-main-failures.txt")
git worktree remove --force .worktrees/baseline-main
comm -3 "$S/flex-branch-failures.txt" "$S/flex-main-failures.txt"
cd .worktrees/flex-scroll
```

Expected: `comm` prints nothing. A test that differs is rerun alone on both before counting. The allocation tests (`FlexLayoutScalingTests`, `FlexLayoutScrollbarTests`) measure the test thread; rerun one alone if it differs.

- [ ] **Step 3: Code review**

Invoke superpowers-extended-cc:requesting-code-review for `main..flex/scroll` against this plan and the spec (sections 4, 4.3a and the PR 4 planning corrections). Fix Critical/High findings on the branch, re-run the affected tasks' verify commands, commit each fix.

- [ ] **Step 4: Live gate**

```bash
dotnet build samples/AcDream.Plugins.FlexDemo -c Release
mkdir -p ~/OpenAC-dev/plugins/sample.flex-demo
cp -R samples/AcDream.Plugins.FlexDemo/bin/Release/net10.0/. ~/OpenAC-dev/plugins/sample.flex-demo/
git checkout -- '*.lock.json'
OPENAC_ROOT=/Users/davidsmith/code/OpenAC/OpenAC/.worktrees/flex-scroll \
  ~/code/OpenAC/OpenAC-Plugins-Golem/tools/run-dev-client.sh    # run in the background; player account by default
```

In game check each live criterion above, with the user driving the trackpad and wheel. Switch themes from the dock's gear (Plugin appearance). Capture the backbuffer at 2× (retina capture workflow: the user drags the window to the built-in display) in Classic, Moss and Brass, and show the captures to the user. If a sideways swipe scrolls the wrong way, flip the sign in `SilkRetainedMouseSurface.OnScroll` for `dx` only, with a test. The vertical direction is today's and must not change. Ask the user about the wheel step (48 points) and anything else that looks off.

- [ ] **Step 5: Finish**

Use superpowers-extended-cc:finishing-a-development-branch. Delivery as for PRs 1–3: merge `flex/scroll` into local main with `--no-ff` after review and the user's confirmation, keep the branch locally, push nothing without the user's word.

## Execution notes (2026-10-05)

Branch `flex/scroll` from main d54f047a, 9 commits (Tasks 1–8, then the
final-review fix wave ce549264). Each task was reviewed on its own, and every
review came back clean (minors deferred). The final whole-branch review said
"with fixes", and its fix wave (ce549264) changed four things:

- **Shift+wheel falls back to vertical.** A Shift step that no horizontal
  scroller takes is now routed as the vertical step it was. As planned, it
  would have been dropped over every list, log and chat, and Shift is also the
  walk-mode key.
- **Trackpad steps keep only their dominant axis**, and a NaN component counts
  as zero.
- **A press reports its position from before focus scrolled the target into
  view.**
- **Tests and cleanups:** tests that a press on empty scrolling space drags the
  window, plus small cleanups.

The scoped re-review found every finding addressed. Release build: 0 warnings.
The App suite fails the same 166 environment tests as main, by name.

The live gate on 10.10.20.20 (player account) passed. The user reported
"it all works well"; no 2× captures were taken. Merged into local main as
4aa1492d (`--no-ff`); the merged suite matches the branch. The branch is kept
locally and nothing is pushed.

Parked for a follow-up:

- a short list takes the wheel even when it has nothing to scroll;
- an open menu popup is not dismissed when its group scrolls;
- hover goes stale after a wheel scroll;
- PR 1's `A_repeated_arrange_allocates_nothing` can flake in a filtered run.
