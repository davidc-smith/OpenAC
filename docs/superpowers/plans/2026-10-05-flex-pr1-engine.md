# Flex layout PR 1 (engine) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers-extended-cc:subagent-driven-development (recommended) or superpowers-extended-cc:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** A pure, fully tested flexbox layout engine (`FlexLayout`, `FlexNode`) that later PRs plug into plugin markup windows.

**Architecture:** Three files under `src/AcDream.App/UI/Layout/Flex/` with no dependency on `UiElement`: value types, the node (settings in, rects out), and a static engine that measures the whole tree once bottom-up and then arranges it top-down. Scratch buffers live on each node so a steady-state relayout allocates nothing. No markup, rendering or plugin-contract change in this PR.

**Tech Stack:** C# / .NET 10, xUnit.

**Spec:** `docs/superpowers/specs/2026-10-05-plugin-flex-layout-design.md` (fork-only docs branch `docs/flex-layout-spec`), sections 3.1 and 7. This plan covers the spec's PR 1 only; PRs 2–4 (title bar, markup, scroll) get their own plans, written when each starts.

## Global Constraints

- Work in a worktree `.worktrees/flex-engine` on a new branch `flex/engine` from fork `main` (cf5a5d79 or later). Never commit `docs/superpowers/**` on that branch.
- Build and test with the pinned SDK: every shell starts with `export DOTNET_ROOT=$HOME/.dotnet PATH=$HOME/.dotnet:$PATH`. Building rewrites some `*.lock.json` files with local RID churn; revert with `git checkout -- '*.lock.json'` before each commit, never commit that churn.
- The repository builds with `TreatWarningsAsErrors`; the build must stay at 0 warnings.
- Namespace `AcDream.App.UI.Layout.Flex`; tests in `AcDream.App.Tests.UI.Layout.Flex` under `tests/AcDream.App.Tests/UI/Layout/Flex/`.
- Units are points. "No limit" is `null` (nullable sizes), never infinity. Computed non-finite or negative values clamp to 0; layout never throws.
- Constants (spec 4.2): `FlexLayout.ScrollbarThickness = 16`, `FlexLayout.MinimumScrollViewport = 40`.
- Snapping: whole points by cumulative edges — positions accumulate unsnapped, each edge is rounded with `MathF.Floor(x + 0.5f)`, sizes are differences of rounded edges.
- Commit messages end with the line `Claude-Session: https://claude.ai/code/session_019dvJ5gakBkq3uJyr1PAC3u`.

**User decisions (already made):**
- Scope: markup windows only; canvases out of scope.
- Practical flexbox subset: direction, gap, padding, justify (start, center, end, space-between), align (start, center, end, stretch), wrap, grow, shrink, basis, min/max, align-self. No `order`, `align-content`, baseline.
- Content sizes with explicit `w`/`h`/`basis` as overrides.
- Content minimum drives the window minimum, plus scrolling groups.
- In-house pure C# engine (not Yoga, not `UiLayoutPolicy`).
- Delivery as four single-topic PRs from fork main, merged into fork main after review; spec and plans stay on the fork-only docs branch.

## Spec corrections made while planning

Found while prototyping the engine (every code block below was compiled and its tests run in a throwaway worktree); folded into the spec in the same docs commit as this plan:

1. **Height-for-width for wrapping containers laid across their parent.** With one bottom-up measurement, a wrapping toolbar inside a column always reserved the height of its *narrowest* layout (four lines at 60 wide, even when the window was 300 wide). The engine now sizes such a child (a wrapping container whose main axis is its parent's cross axis, not scrolling) from the lines it makes at the cross size it is actually given; its minimum along the parent's main axis is that same extent. Its measured `Minimum` (narrowest lines) is unchanged and still drives the window minimum, but it no longer lifts the container's preferred cross size, which stays one line.
2. **Wrapped lines share spare cross space**, as CSS's default `align-content: normal` does; a single line takes the container's whole cross size. Spec 3.1 step 4 said a line's cross size is its largest item, which is only the starting point.
3. **Stretch never squashes an item below its content minimum on the cross axis.** CSS treats `min-height: auto` as 0 across the line; we keep the content minimum (a button never gets shorter than its text) — the same "content minimum" rule as the main axis.
4. **The flex types are `public`, not internal.** xUnit theories take enum parameters and public test methods cannot expose internal types; `UiElement` and the other UI classes are public too. The measure-pass and scratch fields on `FlexNode` stay `internal`.

## Execution notes (2026-10-05)

The code blocks below are the plan as written. Execution changed the engine
beyond them, after reviews found nested wrapping layouts mis-sized; the
branch `flex/engine` is the source of truth for the final code:

- Task 2 fix round (0548b87b): one cross size per across child shared by both
  passes, along children sized from their resolved main size, extents at the
  floor size, CSS factor-sum rule; scratch grew to 6 slots; +10 tests.
- Final-review fix wave (e358693f..17c77905): recursive size-dependent
  extents by a dry-run layout with the dependent axis unbounded (any depth),
  extents at floor and ceiling sizes, measured preferred sizes clamped to item
  limits, empty-container content size includes padding, one line-breaking
  routine, a seeded fuzz test; 77 flex tests in total.
- Rulings and limitations are recorded in the spec (3.1 "Recursive",
  "Limitation (v1): one dependence direction", 4.3a).

## File Structure

| File | Responsibility |
|---|---|
| Create `src/AcDream.App/UI/Layout/Flex/FlexTypes.cs` | `FlexDirection`, `FlexJustify`, `FlexAlign`, `FlexSize`, `FlexRect`, `FlexEdges`, `FlexMeasurement`, `FlexMeasure` delegate. |
| Create `src/AcDream.App/UI/Layout/Flex/FlexNode.cs` | One box: container settings, item settings, leaf measure callback, results (`Rect`, `Measured`, `ContentSize`), internal measure results and scratch. |
| Create `src/AcDream.App/UI/Layout/Flex/FlexLayout.cs` | `Measure(root)` and `Arrange(root, bounds)`: measuring, height-for-width bases, line breaking, flexible lengths, placement, snapping. |
| Create `tests/AcDream.App.Tests/UI/Layout/Flex/FlexTestNodes.cs` | Test builders: `Leaf`, `Row`, `Column`, `Arrange`. |
| Create `tests/AcDream.App.Tests/UI/Layout/Flex/FlexLayoutMeasureTests.cs` | Measuring leaves and containers. |
| Create `tests/AcDream.App.Tests/UI/Layout/Flex/FlexLayoutArrangeTests.cs` | One line: grow, shrink, freezing, basis, justify, align, padding, gap, hidden, snapping. |
| Create `tests/AcDream.App.Tests/UI/Layout/Flex/FlexLayoutWrapScrollTests.cs` | Wrapping and unbounded (scrolling) axes. |
| Create `tests/AcDream.App.Tests/UI/Layout/Flex/FlexLayoutNestingTests.cs` | Nested containers, height-for-width, leaves with children, bad numbers, content size. |
| Create `tests/AcDream.App.Tests/UI/Layout/Flex/FlexLayoutScalingTests.cs` | Measure calls linear in leaf count; zero allocation on relayout. |

---

### Task 1: Flex types, nodes and measuring

**Goal:** A flex tree can be built and measured: each node reports a preferred and a minimum size the way CSS would (overrides, min-width:auto, limits, gaps, padding, wrapping, scrolling axes).

**Files:**
- Create: `src/AcDream.App/UI/Layout/Flex/FlexTypes.cs`
- Create: `src/AcDream.App/UI/Layout/Flex/FlexNode.cs`
- Create: `src/AcDream.App/UI/Layout/Flex/FlexLayout.cs`
- Create: `tests/AcDream.App.Tests/UI/Layout/Flex/FlexTestNodes.cs`
- Test: `tests/AcDream.App.Tests/UI/Layout/Flex/FlexLayoutMeasureTests.cs`

**Acceptance Criteria:**
- [ ] A leaf reports its callback's measurement; `Width`/`Height` override preferred and cap the automatic minimum; `MinWidth` lowers the floor; `MaxWidth` clamps preferred and minimum.
- [ ] A row/column adds visible items' bases, gaps and padding (preferred) and items' minimums (minimum); hidden items are not counted.
- [ ] A wrapping row's minimum is its widest item and the lines that makes; its preferred cross size stays one line.
- [ ] A scrolling axis has a 40-point minimum and adds the 16-point bar to the other axis; preferred is never below minimum.
- [ ] `FlexLayoutMeasureTests`: 10 passed; build 0 warnings.

**Verify:** `export DOTNET_ROOT=$HOME/.dotnet PATH=$HOME/.dotnet:$PATH && dotnet test tests/AcDream.App.Tests/AcDream.App.Tests.csproj --filter "FullyQualifiedName~UI.Layout.Flex.FlexLayoutMeasureTests"` → `Passed: 10, Failed: 0`

**Steps:**

- [ ] **Step 1: Create the worktree**

```bash
cd /Users/davidsmith/code/OpenAC/OpenAC
git worktree add -b flex/engine .worktrees/flex-engine main
cd .worktrees/flex-engine
```

- [ ] **Step 2: Write the test builders and the failing measure tests**

`tests/AcDream.App.Tests/UI/Layout/Flex/FlexTestNodes.cs`:

```csharp
using AcDream.App.UI.Layout.Flex;

namespace AcDream.App.Tests.UI.Layout.Flex;

/// <summary>Builders for flex trees in tests: leaves with a fixed measurement, and containers.</summary>
internal static class FlexTestNodes
{
    public static FlexNode Leaf(float width, float height, float? minWidth = null, float? minHeight = null)
    {
        var measurement = new FlexMeasurement(
            new FlexSize(width, height),
            new FlexSize(minWidth ?? width, minHeight ?? height));
        return new FlexNode { Measure = _ => measurement };
    }

    public static FlexNode Row(params FlexNode[] children) => Container(FlexDirection.Row, children);

    public static FlexNode Column(params FlexNode[] children) => Container(FlexDirection.Column, children);

    private static FlexNode Container(FlexDirection direction, FlexNode[] children)
    {
        var node = new FlexNode { Direction = direction };
        node.Children.AddRange(children);
        return node;
    }
}
```

`tests/AcDream.App.Tests/UI/Layout/Flex/FlexLayoutMeasureTests.cs`:

```csharp
using AcDream.App.UI.Layout.Flex;
using static AcDream.App.Tests.UI.Layout.Flex.FlexTestNodes;

namespace AcDream.App.Tests.UI.Layout.Flex;

/// <summary>
/// Measuring: a leaf reports its content, overrides and limits adjust it the
/// way CSS does (min-width:auto never exceeds an explicit size or the
/// maximum), and a container adds up its visible items, gaps and padding.
/// </summary>
public sealed class FlexLayoutMeasureTests
{
    [Fact]
    public void Leaf_reports_its_content_measurement()
    {
        FlexMeasurement m = FlexLayout.Measure(Leaf(120f, 20f, minWidth: 40f));

        Assert.Equal(new FlexSize(120f, 20f), m.Preferred);
        Assert.Equal(new FlexSize(40f, 20f), m.Minimum);
    }

    [Fact]
    public void Explicit_size_overrides_preferred_and_caps_the_automatic_minimum()
    {
        FlexNode leaf = Leaf(120f, 20f, minWidth: 80f);
        leaf.Width = 50f;

        FlexMeasurement m = FlexLayout.Measure(leaf);

        Assert.Equal(new FlexSize(50f, 20f), m.Preferred);
        Assert.Equal(50f, m.Minimum.Width);
    }

    [Fact]
    public void Explicit_minimum_lowers_the_floor_and_maximum_clamps_both()
    {
        FlexNode lowered = Leaf(120f, 20f, minWidth: 80f);
        lowered.MinWidth = 10f;
        FlexNode capped = Leaf(120f, 20f, minWidth: 80f);
        capped.MaxWidth = 60f;

        Assert.Equal(10f, FlexLayout.Measure(lowered).Minimum.Width);
        FlexMeasurement m = FlexLayout.Measure(capped);
        Assert.Equal(60f, m.Preferred.Width);
        Assert.Equal(60f, m.Minimum.Width);
    }

    [Fact]
    public void Row_adds_items_gaps_and_padding()
    {
        FlexNode row = Row(Leaf(50f, 10f, minWidth: 20f), Leaf(30f, 24f), Leaf(40f, 12f, minWidth: 10f));
        row.Gap = 4f;
        row.Padding = new FlexEdges(1f, 2f, 3f, 4f);

        FlexMeasurement m = FlexLayout.Measure(row);

        Assert.Equal(new FlexSize(50f + 30f + 40f + 8f + 6f, 24f + 4f), m.Preferred);
        Assert.Equal(new FlexSize(20f + 30f + 10f + 8f + 6f, 24f + 4f), m.Minimum);
    }

    [Fact]
    public void Column_swaps_the_axes()
    {
        FlexNode column = Column(Leaf(50f, 10f), Leaf(30f, 24f, minWidth: 5f, minHeight: 6f));
        column.Gap = 2f;

        FlexMeasurement m = FlexLayout.Measure(column);

        Assert.Equal(new FlexSize(50f, 36f), m.Preferred);
        Assert.Equal(new FlexSize(50f, 18f), m.Minimum);
    }

    [Fact]
    public void Hidden_items_are_not_counted()
    {
        FlexNode hidden = Leaf(500f, 500f);
        hidden.Hidden = true;
        FlexNode row = Row(Leaf(50f, 10f), hidden, Leaf(30f, 10f));
        row.Gap = 4f;

        Assert.Equal(new FlexSize(84f, 10f), FlexLayout.Measure(row).Preferred);
    }

    [Fact]
    public void Wrapping_minimum_is_the_widest_item_and_the_lines_it_makes()
    {
        // At 40 wide: [40], [20 + 4 + 10], [30] -> three lines of 10, 12 and 8 high.
        FlexNode row = Row(Leaf(40f, 10f), Leaf(20f, 12f), Leaf(10f, 6f), Leaf(30f, 8f));
        row.Wrap = true;
        row.Gap = 4f;

        FlexMeasurement m = FlexLayout.Measure(row);

        Assert.Equal(new FlexSize(40f + 20f + 10f + 30f + 12f, 12f), m.Preferred);
        Assert.Equal(new FlexSize(40f, 10f + 12f + 8f + 8f), m.Minimum);
    }

    [Theory]
    [InlineData(false, true, 316f, 40f, 316f, 400f)]
    [InlineData(true, false, 40f, 416f, 300f, 416f)]
    [InlineData(true, true, 56f, 56f, 300f, 400f)]
    public void Scrolling_axis_needs_only_a_small_viewport_and_its_bar_takes_the_other_axis(
        bool scrollX, bool scrollY, float minWidth, float minHeight, float preferredWidth, float preferredHeight)
    {
        // Content is 300 x 400 (two 300 x 200 items stacked). A preferred size is never below the minimum.
        FlexNode column = Column(Leaf(300f, 200f), Leaf(300f, 200f));
        column.ScrollX = scrollX;
        column.ScrollY = scrollY;

        FlexMeasurement m = FlexLayout.Measure(column);

        Assert.Equal(new FlexSize(minWidth, minHeight), m.Minimum);
        Assert.Equal(new FlexSize(preferredWidth, preferredHeight), m.Preferred);
    }
}
```

- [ ] **Step 3: Run the tests to verify they fail**

Run: `export DOTNET_ROOT=$HOME/.dotnet PATH=$HOME/.dotnet:$PATH && dotnet test tests/AcDream.App.Tests/AcDream.App.Tests.csproj --filter "FullyQualifiedName~UI.Layout.Flex.FlexLayoutMeasureTests"`
Expected: build FAILS with `CS0246: The type or namespace name 'FlexNode' could not be found` (and the other flex types).

- [ ] **Step 4: Write the types**

`src/AcDream.App/UI/Layout/Flex/FlexTypes.cs`:

```csharp
namespace AcDream.App.UI.Layout.Flex;

/// <summary>The main axis of a flex container.</summary>
public enum FlexDirection { Row, Column }

/// <summary>Where a line's items sit along the main axis when they leave space over.</summary>
public enum FlexJustify { Start, Center, End, SpaceBetween }

/// <summary>Where an item sits across its line.</summary>
public enum FlexAlign { Start, Center, End, Stretch }

/// <summary>A size in points.</summary>
public readonly record struct FlexSize(float Width, float Height);

/// <summary>A rectangle in points, relative to the parent node's top-left corner.</summary>
public readonly record struct FlexRect(float X, float Y, float Width, float Height);

/// <summary>Space between a container's edge and its items, in points.</summary>
public readonly record struct FlexEdges(float Top, float Right, float Bottom, float Left)
{
    public static FlexEdges All(float value) => new(value, value, value, value);

    public float Horizontal => Left + Right;

    public float Vertical => Top + Bottom;
}

/// <summary>
/// What a node needs: the size it would like, and the smallest size it can
/// be given without its content breaking. Both include a container's padding.
/// </summary>
public readonly record struct FlexMeasurement(FlexSize Preferred, FlexSize Minimum);

/// <summary>
/// Measures a leaf's content. <paramref name="availableWidth"/> is the width
/// the leaf may wrap to, or null when it is unconstrained; v1 controls do not
/// wrap and are always asked with null.
/// </summary>
public delegate FlexMeasurement FlexMeasure(float? availableWidth);
```

- [ ] **Step 5: Write the node**

`src/AcDream.App/UI/Layout/Flex/FlexNode.cs`:

```csharp
using System.Collections.Generic;

namespace AcDream.App.UI.Layout.Flex;

/// <summary>
/// One box in a flex layout: a container that lays out <see cref="Children"/>,
/// a leaf measured by <see cref="Measure"/>, or both settings at once (a
/// container is also an item of its own parent). A node with a
/// <see cref="Measure"/> callback is a leaf: its children, if any, are not
/// laid out by the engine.
///
/// <para>Unbounded is always null, never infinity: a null maximum has no
/// limit, and a scrolling axis lays out with no available size.</para>
/// </summary>
public sealed class FlexNode
{
    // ── Container settings ──────────────────────────────────────────────
    public FlexDirection Direction { get; set; }

    /// <summary>Points between items on a line, and between wrapped lines.</summary>
    public float Gap { get; set; }

    public FlexEdges Padding { get; set; }

    public FlexJustify Justify { get; set; }

    public FlexAlign Align { get; set; } = FlexAlign.Stretch;

    public bool Wrap { get; set; }

    /// <summary>The container scrolls horizontally: its items are laid out with no width limit.</summary>
    public bool ScrollX { get; set; }

    /// <summary>The container scrolls vertically: its items are laid out with no height limit.</summary>
    public bool ScrollY { get; set; }

    public List<FlexNode> Children { get; } = new();

    // ── Item settings ───────────────────────────────────────────────────
    public float Grow { get; set; }

    public float Shrink { get; set; } = 1f;

    /// <summary>The main-axis starting size; null is <c>auto</c>.</summary>
    public float? Basis { get; set; }

    /// <summary>Preferred width, overriding the measured one.</summary>
    public float? Width { get; set; }

    /// <summary>Preferred height, overriding the measured one.</summary>
    public float? Height { get; set; }

    /// <summary>Null: the content minimum.</summary>
    public float? MinWidth { get; set; }

    /// <summary>Null: no limit.</summary>
    public float? MaxWidth { get; set; }

    public float? MinHeight { get; set; }

    public float? MaxHeight { get; set; }

    /// <summary>Null: the parent's <see cref="Align"/>.</summary>
    public FlexAlign? AlignSelf { get; set; }

    /// <summary>A hidden node takes no space and is not laid out.</summary>
    public bool Hidden { get; set; }

    /// <summary>Set for a leaf; null for a container measured from its children.</summary>
    public FlexMeasure? Measure { get; set; }

    // ── Results ─────────────────────────────────────────────────────────
    /// <summary>Where the last arrange put this node, relative to its parent's top-left corner.</summary>
    public FlexRect Rect { get; internal set; }

    /// <summary>The last measurement: preferred size clamped to the limits, and minimum size.</summary>
    public FlexMeasurement Measured { get; internal set; }

    /// <summary>
    /// After an arrange, how far this container's items reach: the furthest
    /// item edge plus the trailing padding, on each axis. A scrolling
    /// container scrolls over this extent.
    /// </summary>
    public FlexSize ContentSize { get; internal set; }

    // Measure-pass results the arrange pass reads: preferred size with the
    // Width/Height overrides applied but not clamped, and resolved limits.
    internal FlexSize PreferredUnclamped;
    internal FlexSize MinimumSize;
    internal FlexSize MaximumSize;

    // Arrange scratch, reused so a steady-state layout allocates nothing.
    internal readonly List<int> LineEnds = new();
    internal float[] Scratch = System.Array.Empty<float>();
}
```

- [ ] **Step 6: Write the engine's measuring half**

`src/AcDream.App/UI/Layout/Flex/FlexLayout.cs` (Task 2 adds `Arrange` and the arrange pass to this file):

```csharp
using System;

namespace AcDream.App.UI.Layout.Flex;

/// <summary>
/// A reduced CSS flexbox: direction, gap, padding, justify, align, wrap,
/// grow, shrink, basis, min/max and align-self, plus unbounded scrolling
/// axes. Pure: it reads and writes <see cref="FlexNode"/>s and knows nothing
/// of the UI tree.
///
/// <para>One <see cref="Arrange"/> measures every node once, bottom-up, then
/// places every node top-down. Each leaf's measure callback runs exactly once
/// per arrange, and a repeated arrange of the same tree allocates nothing
/// once its scratch buffers have grown to fit.</para>
///
/// <para>A wrapping container placed across its parent's main axis (a
/// wrapping row inside a column) is sized height-for-width: its extent along
/// the parent's main axis is the lines it makes at the width it is given,
/// not the lines it would make at its narrowest.</para>
/// </summary>
public static class FlexLayout
{
    /// <summary>Thickness of a scrollbar, reserved in a scrolling container's minimum size.</summary>
    public const float ScrollbarThickness = 16f;

    /// <summary>The smallest viewport a scrolling container shrinks to along an axis it scrolls.</summary>
    public const float MinimumScrollViewport = 40f;

    /// <summary>Measures <paramref name="root"/> and every node under it, and returns the root's measurement.</summary>
    public static FlexMeasurement Measure(FlexNode root)
    {
        MeasureNode(root);
        return root.Measured;
    }

    // ── Measure ─────────────────────────────────────────────────────────

    private static void MeasureNode(FlexNode node)
    {
        FlexMeasurement content = node.Measure is { } measure
            ? measure(null)
            : MeasureChildren(node);

        float prefW = Positive(node.Width ?? content.Preferred.Width);
        float prefH = Positive(node.Height ?? content.Preferred.Height);

        // CSS min-width:auto: the content minimum, but never more than an
        // explicit preferred size, and never more than the maximum.
        float autoMinW = node.Width is { } w ? MathF.Min(Positive(w), Positive(content.Minimum.Width)) : Positive(content.Minimum.Width);
        float autoMinH = node.Height is { } h ? MathF.Min(Positive(h), Positive(content.Minimum.Height)) : Positive(content.Minimum.Height);
        float maxW = node.MaxWidth is { } mw ? Positive(mw) : float.MaxValue;
        float maxH = node.MaxHeight is { } mh ? Positive(mh) : float.MaxValue;
        float minW = MathF.Min(node.MinWidth is { } nw ? Positive(nw) : autoMinW, maxW);
        float minH = MathF.Min(node.MinHeight is { } nh ? Positive(nh) : autoMinH, maxH);

        // A wrapping container's cross minimum is the lines it makes at its
        // narrowest; that must not lift its preferred cross size, which is one line.
        float floorW = minW, floorH = minH;
        if (node.Measure is null && node.Wrap)
        {
            if (node.Direction == FlexDirection.Row) floorH = MathF.Min(node.MinHeight is { } eh ? Positive(eh) : 0f, maxH);
            else floorW = MathF.Min(node.MinWidth is { } ew ? Positive(ew) : 0f, maxW);
        }

        node.PreferredUnclamped = new FlexSize(prefW, prefH);
        node.MinimumSize = new FlexSize(minW, minH);
        node.MaximumSize = new FlexSize(maxW, maxH);
        node.Measured = new FlexMeasurement(
            new FlexSize(Math.Clamp(prefW, floorW, maxW), Math.Clamp(prefH, floorH, maxH)),
            new FlexSize(minW, minH));
    }

    private static FlexMeasurement MeasureChildren(FlexNode node)
    {
        bool row = node.Direction == FlexDirection.Row;
        float gap = Positive(node.Gap);
        float prefMain = 0f, prefCross = 0f, minMainSum = 0f, minMainMax = 0f, minCross = 0f;
        int visible = 0;

        for (int i = 0; i < node.Children.Count; i++)
        {
            FlexNode child = node.Children[i];
            if (child.Hidden) continue;
            MeasureNode(child);
            prefMain += MeasuredBase(child, row);
            prefCross = MathF.Max(prefCross, CrossOf(child.Measured.Preferred, row));
            float childMinMain = MainOf(child.MinimumSize, row);
            minMainSum += childMinMain;
            minMainMax = MathF.Max(minMainMax, childMinMain);
            minCross = MathF.Max(minCross, CrossOf(child.MinimumSize, row));
            visible++;
        }

        float gaps = visible > 1 ? gap * (visible - 1) : 0f;
        prefMain += gaps;

        float minMain;
        if (node.Wrap && visible > 0)
        {
            // At its minimum main size a wrapping container is as narrow as its
            // widest item; its minimum cross size is then the lines that makes.
            minMain = minMainMax;
            minCross = WrappedMinimumCross(node, row, minMain, gap);
        }
        else
        {
            minMain = minMainSum + gaps;
        }

        FlexEdges p = node.Padding;
        float padMain = row ? p.Horizontal : p.Vertical;
        float padCross = row ? p.Vertical : p.Horizontal;
        FlexSize preferred = Size(prefMain + padMain, prefCross + padCross, row);
        FlexSize minimum = Size(minMain + padMain, minCross + padCross, row);

        // A scrolling axis only needs a small viewport; the bar for that axis
        // takes room on the other axis.
        float minW = minimum.Width, minH = minimum.Height;
        if (node.ScrollX) minW = MinimumScrollViewport;
        if (node.ScrollY) minH = MinimumScrollViewport;
        if (node.ScrollY) minW += ScrollbarThickness;
        if (node.ScrollX) minH += ScrollbarThickness;
        return new FlexMeasurement(preferred, new FlexSize(minW, minH));
    }

    private static float WrappedMinimumCross(FlexNode node, bool row, float available, float gap)
    {
        float total = 0f, lineMain = 0f, lineCross = 0f;
        int lines = 0, inLine = 0;
        for (int i = 0; i < node.Children.Count; i++)
        {
            FlexNode child = node.Children[i];
            if (child.Hidden) continue;
            float main = MainOf(child.MinimumSize, row);
            if (inLine > 0 && lineMain + gap + main > available)
            {
                total += lineCross;
                lines++;
                lineMain = 0f; lineCross = 0f; inLine = 0;
            }
            lineMain += (inLine > 0 ? gap : 0f) + main;
            lineCross = MathF.Max(lineCross, CrossOf(child.MinimumSize, row));
            inLine++;
        }
        if (inLine > 0) { total += lineCross; lines++; }
        return total + (lines > 1 ? gap * (lines - 1) : 0f);
    }

    // ── Helpers ─────────────────────────────────────────────────────────

    /// <summary>The flex base size from the measurement: basis, else the main-axis preferred size (overrides applied, unclamped).</summary>
    private static float MeasuredBase(FlexNode child, bool row) =>
        child.Basis is { } basis ? Positive(basis) : MainOf(child.PreferredUnclamped, row);

    private static float MainOf(FlexSize size, bool row) => row ? size.Width : size.Height;

    private static float CrossOf(FlexSize size, bool row) => row ? size.Height : size.Width;

    private static FlexSize Size(float main, float cross, bool row) =>
        row ? new FlexSize(main, cross) : new FlexSize(cross, main);

    /// <summary>Non-finite or negative computed values are defects; they clamp to 0 so layout never throws.</summary>
    private static float Positive(float value) => float.IsFinite(value) && value > 0f ? value : 0f;
}
```

- [ ] **Step 7: Run the tests to verify they pass**

Run: `export DOTNET_ROOT=$HOME/.dotnet PATH=$HOME/.dotnet:$PATH && dotnet test tests/AcDream.App.Tests/AcDream.App.Tests.csproj --filter "FullyQualifiedName~UI.Layout.Flex.FlexLayoutMeasureTests"`
Expected: `Passed: 10, Failed: 0`, and the build prints no warnings.

- [ ] **Step 8: Commit**

```bash
git checkout -- '*.lock.json'
git add src/AcDream.App/UI/Layout/Flex tests/AcDream.App.Tests/UI/Layout/Flex
git commit -m "feat(ui): flex layout types, nodes and measuring

Claude-Session: https://claude.ai/code/session_019dvJ5gakBkq3uJyr1PAC3u"
```

---

### Task 2: Arranging: lines, flexible lengths, placement and snapping

**Goal:** `FlexLayout.Arrange` places every node: grow/shrink with freezing, basis, justify, align and align-self, gap and padding, hidden items, wrapping with shared cross space, unbounded scrolling axes, nesting with height-for-width wrapping children, cumulative-edge snapping, and `ContentSize`.

**Files:**
- Modify: `src/AcDream.App/UI/Layout/Flex/FlexLayout.cs` (insert `Arrange`, the arrange section, and two helpers)
- Modify: `tests/AcDream.App.Tests/UI/Layout/Flex/FlexTestNodes.cs` (add the `Arrange` helper)
- Test: `tests/AcDream.App.Tests/UI/Layout/Flex/FlexLayoutArrangeTests.cs`
- Test: `tests/AcDream.App.Tests/UI/Layout/Flex/FlexLayoutWrapScrollTests.cs`
- Test: `tests/AcDream.App.Tests/UI/Layout/Flex/FlexLayoutNestingTests.cs`

**Acceptance Criteria:**
- [ ] The root keeps the bounds it is given; every descendant rect is relative to its parent.
- [ ] Grow shares free space by factor (factors below 1 take only that share); shrink is weighted by basis; items hitting min/max freeze and the rest re-flex.
- [ ] Justify start/center/end/space-between; overflowing lines are not moved by justify; align start/center/end/stretch, align-self overrides; stretch keeps explicit cross sizes and respects max.
- [ ] Hidden items take no space and get a zero rect.
- [ ] Wrap breaks greedily by clamped size; lines share spare cross space; an oversized item gets its own line; no wrapping along a scrolling axis; scrolling axes never shrink items; `ContentSize` is the furthest edge plus trailing padding.
- [ ] A wrapping row inside a column is as tall as the lines it makes at its width (1, 2 and 4 lines at 300, 140 and 60 wide).
- [ ] Snapping by cumulative edges: 3 × 33.33 → 33/34/33 ending at 100; fractional gaps keep sizes; space-between with remainder ends exactly at the edge.
- [ ] NaN/negative inputs never throw and clamp to 0; a leaf's children are untouched.
- [ ] All flex tests: 51 passed; build 0 warnings.

**Verify:** `export DOTNET_ROOT=$HOME/.dotnet PATH=$HOME/.dotnet:$PATH && dotnet test tests/AcDream.App.Tests/AcDream.App.Tests.csproj --filter "FullyQualifiedName~UI.Layout.Flex"` → `Passed: 51, Failed: 0`

**Steps:**

- [ ] **Step 1: Add the `Arrange` helper to the test builders**

In `tests/AcDream.App.Tests/UI/Layout/Flex/FlexTestNodes.cs`, insert before the class's closing brace (after the `Container` method):

```csharp
    public static void Arrange(FlexNode root, float width, float height) =>
        FlexLayout.Arrange(root, new FlexRect(0f, 0f, width, height));
```

- [ ] **Step 2: Write the failing arrange tests**

`tests/AcDream.App.Tests/UI/Layout/Flex/FlexLayoutArrangeTests.cs`:

```csharp
using AcDream.App.UI.Layout.Flex;
using static AcDream.App.Tests.UI.Layout.Flex.FlexTestNodes;

namespace AcDream.App.Tests.UI.Layout.Flex;

/// <summary>
/// One line: free space is shared by grow, overflow taken back by shrink
/// weighted by basis, limits freeze items and the rest re-flex, justify and
/// align place the items, and edges snap to whole points without drifting.
/// Every expected value is worked by hand from the CSS flexbox algorithm.
/// </summary>
public sealed class FlexLayoutArrangeTests
{
    [Fact]
    public void Root_keeps_the_bounds_it_is_given()
    {
        FlexNode row = Row(Leaf(10f, 10f));

        FlexLayout.Arrange(row, new FlexRect(5.5f, 7f, 100f, 30f));

        Assert.Equal(new FlexRect(5.5f, 7f, 100f, 30f), row.Rect);
    }

    [Fact]
    public void Grow_shares_free_space_by_factor()
    {
        FlexNode a = Leaf(50f, 10f), b = Leaf(50f, 10f);
        a.Grow = 1f;
        b.Grow = 2f;

        Arrange(Row(a, b), 300f, 10f);

        // 200 free: a gets 66.67, b 133.33; the shared edge 116.67 rounds to 117.
        Assert.Equal(new FlexRect(0f, 0f, 117f, 10f), a.Rect);
        Assert.Equal(new FlexRect(117f, 0f, 183f, 10f), b.Rect);
    }

    [Fact]
    public void No_grow_leaves_items_at_their_basis()
    {
        FlexNode a = Leaf(50f, 10f), b = Leaf(30f, 10f);

        Arrange(Row(a, b), 300f, 10f);

        Assert.Equal(new FlexRect(0f, 0f, 50f, 10f), a.Rect);
        Assert.Equal(new FlexRect(50f, 0f, 30f, 10f), b.Rect);
    }

    [Fact]
    public void Shrink_is_weighted_by_basis()
    {
        FlexNode a = Leaf(100f, 10f, minWidth: 0f), b = Leaf(50f, 10f, minWidth: 0f);

        Arrange(Row(a, b), 100f, 10f);

        // 50 over: a gives back 50 * 100/150, b 50 * 50/150.
        Assert.Equal(new FlexRect(0f, 0f, 67f, 10f), a.Rect);
        Assert.Equal(new FlexRect(67f, 0f, 33f, 10f), b.Rect);
    }

    [Fact]
    public void An_item_shrunk_to_its_minimum_freezes_and_the_rest_take_the_remainder()
    {
        FlexNode a = Leaf(100f, 10f, minWidth: 80f), b = Leaf(100f, 10f, minWidth: 0f);

        Arrange(Row(a, b), 100f, 10f);

        // First pass: both 50, a clamps to 80. Second pass: b alone gives back 80.
        Assert.Equal(80f, a.Rect.Width);
        Assert.Equal(new FlexRect(80f, 0f, 20f, 10f), b.Rect);
    }

    [Fact]
    public void An_item_grown_to_its_maximum_freezes_and_the_rest_take_the_remainder()
    {
        FlexNode a = Leaf(50f, 10f), b = Leaf(50f, 10f);
        a.Grow = 1f;
        a.MaxWidth = 100f;
        b.Grow = 1f;

        Arrange(Row(a, b), 300f, 10f);

        Assert.Equal(100f, a.Rect.Width);
        Assert.Equal(new FlexRect(100f, 0f, 200f, 10f), b.Rect);
    }

    [Fact]
    public void Grow_factors_summing_below_one_take_only_that_share()
    {
        FlexNode a = Leaf(100f, 10f);
        a.Grow = 0.5f;

        Arrange(Row(a), 200f, 10f);

        Assert.Equal(150f, a.Rect.Width);
    }

    [Fact]
    public void Basis_replaces_the_measured_size_as_the_starting_point()
    {
        FlexNode a = Leaf(10f, 10f, minWidth: 0f), b = Leaf(10f, 10f, minWidth: 0f);
        a.Basis = 0f;
        a.Grow = 1f;
        b.Basis = 0f;
        b.Grow = 1f;

        Arrange(Row(a, b), 100f, 10f);

        Assert.Equal(50f, a.Rect.Width);
        Assert.Equal(50f, b.Rect.Width);
    }

    [Theory]
    [InlineData(FlexJustify.Start, 0f, 50f, 100f)]
    [InlineData(FlexJustify.Center, 75f, 125f, 175f)]
    [InlineData(FlexJustify.End, 150f, 200f, 250f)]
    [InlineData(FlexJustify.SpaceBetween, 0f, 125f, 250f)]
    public void Justify_places_items_along_the_main_axis(FlexJustify justify, float x0, float x1, float x2)
    {
        FlexNode a = Leaf(50f, 10f), b = Leaf(50f, 10f), c = Leaf(50f, 10f);
        FlexNode row = Row(a, b, c);
        row.Justify = justify;

        Arrange(row, 300f, 10f);

        Assert.Equal(new[] { x0, x1, x2 }, new[] { a.Rect.X, b.Rect.X, c.Rect.X });
    }

    [Fact]
    public void Justify_does_not_move_items_that_overflow()
    {
        FlexNode a = Leaf(40f, 10f), b = Leaf(40f, 10f);
        FlexNode row = Row(a, b);
        row.Justify = FlexJustify.Center;

        Arrange(row, 50f, 10f);

        Assert.Equal(new FlexRect(0f, 0f, 40f, 10f), a.Rect);
        Assert.Equal(new FlexRect(40f, 0f, 40f, 10f), b.Rect);
        Assert.Equal(new FlexSize(80f, 10f), row.ContentSize);
    }

    [Fact]
    public void Padding_and_gap_offset_the_items()
    {
        FlexNode a = Leaf(20f, 10f), b = Leaf(20f, 10f);
        FlexNode row = Row(a, b);
        row.Padding = FlexEdges.All(10f);
        row.Gap = 5f;

        Arrange(row, 200f, 50f);

        Assert.Equal(new FlexRect(10f, 10f, 20f, 30f), a.Rect);
        Assert.Equal(new FlexRect(35f, 10f, 20f, 30f), b.Rect);
    }

    [Theory]
    [InlineData(FlexAlign.Start, 0f, 10f)]
    [InlineData(FlexAlign.Center, 20f, 10f)]
    [InlineData(FlexAlign.End, 40f, 10f)]
    [InlineData(FlexAlign.Stretch, 0f, 50f)]
    public void Align_places_items_across_the_line(FlexAlign align, float y, float height)
    {
        FlexNode a = Leaf(20f, 10f);
        FlexNode row = Row(a);
        row.Align = align;

        Arrange(row, 100f, 50f);

        Assert.Equal(new FlexRect(0f, y, 20f, height), a.Rect);
    }

    [Fact]
    public void AlignSelf_overrides_the_container()
    {
        FlexNode a = Leaf(20f, 10f), b = Leaf(20f, 10f);
        b.AlignSelf = FlexAlign.End;

        Arrange(Row(a, b), 100f, 50f);

        Assert.Equal(50f, a.Rect.Height);
        Assert.Equal(new FlexRect(20f, 40f, 20f, 10f), b.Rect);
    }

    [Fact]
    public void Stretch_keeps_an_explicit_cross_size_and_respects_the_maximum()
    {
        FlexNode fixedHeight = Leaf(20f, 10f), capped = Leaf(20f, 10f);
        fixedHeight.Height = 12f;
        capped.MaxHeight = 30f;

        Arrange(Row(fixedHeight, capped), 100f, 50f);

        Assert.Equal(12f, fixedHeight.Rect.Height);
        Assert.Equal(30f, capped.Rect.Height);
    }

    [Fact]
    public void Hidden_items_take_no_space()
    {
        FlexNode a = Leaf(20f, 10f), hidden = Leaf(500f, 10f), b = Leaf(20f, 10f);
        hidden.Hidden = true;
        FlexNode row = Row(a, hidden, b);
        row.Gap = 4f;

        Arrange(row, 100f, 10f);

        Assert.Equal(24f, b.Rect.X);
        Assert.Equal(default, hidden.Rect);
    }

    [Fact]
    public void Column_lays_out_vertically()
    {
        FlexNode a = Leaf(20f, 50f), b = Leaf(30f, 50f);
        b.Grow = 1f;
        FlexNode column = Column(a, b);
        column.Align = FlexAlign.Start;

        Arrange(column, 100f, 300f);

        Assert.Equal(new FlexRect(0f, 0f, 20f, 50f), a.Rect);
        Assert.Equal(new FlexRect(0f, 50f, 30f, 250f), b.Rect);
    }

    [Fact]
    public void Snapping_spreads_rounding_across_the_line()
    {
        FlexNode a = Leaf(0f, 10f), b = Leaf(0f, 10f), c = Leaf(0f, 10f);
        a.Grow = b.Grow = c.Grow = 1f;

        Arrange(Row(a, b, c), 100f, 10f);

        // Edges 0, 33.33, 66.67, 100 round to 0, 33, 67, 100.
        Assert.Equal(new[] { 33f, 34f, 33f }, new[] { a.Rect.Width, b.Rect.Width, c.Rect.Width });
        Assert.Equal(100f, c.Rect.X + c.Rect.Width);
    }

    [Fact]
    public void Snapping_a_fractional_gap_keeps_item_sizes()
    {
        FlexNode a = Leaf(10f, 10f), b = Leaf(10f, 10f), c = Leaf(10f, 10f);
        FlexNode row = Row(a, b, c);
        row.Gap = 2.5f;

        Arrange(row, 100f, 10f);

        Assert.Equal(new[] { 0f, 13f, 25f }, new[] { a.Rect.X, b.Rect.X, c.Rect.X });
        Assert.Equal(new[] { 10f, 10f, 10f }, new[] { a.Rect.Width, b.Rect.Width, c.Rect.Width });
    }

    [Fact]
    public void Space_between_with_a_remainder_ends_exactly_at_the_edge()
    {
        FlexNode a = Leaf(10f, 10f), b = Leaf(10f, 10f), c = Leaf(10f, 10f), d = Leaf(10f, 10f);
        FlexNode row = Row(a, b, c, d);
        row.Justify = FlexJustify.SpaceBetween;

        Arrange(row, 101f, 10f);

        // Starts 0, 30.33, 60.67, 91 round to 0, 30, 61, 91.
        Assert.Equal(new[] { 0f, 30f, 61f, 91f }, new[] { a.Rect.X, b.Rect.X, c.Rect.X, d.Rect.X });
        Assert.Equal(101f, d.Rect.X + d.Rect.Width);
    }
}
```

`tests/AcDream.App.Tests/UI/Layout/Flex/FlexLayoutWrapScrollTests.cs`:

```csharp
using AcDream.App.UI.Layout.Flex;
using static AcDream.App.Tests.UI.Layout.Flex.FlexTestNodes;

namespace AcDream.App.Tests.UI.Layout.Flex;

/// <summary>
/// Several lines and unbounded axes: wrapping breaks greedily by each item's
/// clamped size, spare cross space is shared between lines (CSS's default
/// align-content), and an axis that scrolls lays its items out with no limit
/// and reports how far they reach.
/// </summary>
public sealed class FlexLayoutWrapScrollTests
{
    private static FlexNode[] Items(int count, float width, float height)
    {
        var items = new FlexNode[count];
        for (int i = 0; i < count; i++) items[i] = Leaf(width, height);
        return items;
    }

    [Fact]
    public void Wrap_breaks_items_into_lines_that_fit()
    {
        FlexNode[] items = Items(5, 30f, 10f);
        FlexNode row = Row(items);
        row.Wrap = true;
        row.Gap = 5f;
        row.Align = FlexAlign.Start;

        Arrange(row, 100f, 100f);

        // 30 + 5 + 30 + 5 + 30 = 100 fits; lines take (100 - 20 - 5) / 2 = 37.5
        // extra each, so the second line starts at 47.5 + 5 = 52.5, rounded 53.
        Assert.Equal(new FlexRect(0f, 0f, 30f, 10f), items[0].Rect);
        Assert.Equal(new FlexRect(70f, 0f, 30f, 10f), items[2].Rect);
        Assert.Equal(new FlexRect(0f, 53f, 30f, 10f), items[3].Rect);
        Assert.Equal(new FlexRect(35f, 53f, 30f, 10f), items[4].Rect);
    }

    [Fact]
    public void Wrapped_lines_stretch_their_items_over_the_shared_space()
    {
        FlexNode[] items = Items(5, 30f, 10f);
        FlexNode row = Row(items);
        row.Wrap = true;
        row.Gap = 5f;

        Arrange(row, 100f, 100f);

        // Lines are 47.5 high: edges 0..47.5 and 52.5..100 round to 0..48 and 53..100.
        Assert.Equal(48f, items[0].Rect.Height);
        Assert.Equal(new FlexRect(0f, 53f, 30f, 47f), items[3].Rect);
    }

    [Fact]
    public void An_item_wider_than_the_line_gets_a_line_of_its_own()
    {
        FlexNode small = Leaf(30f, 10f), wide = Leaf(150f, 10f), after = Leaf(30f, 10f);
        FlexNode row = Row(small, wide, after);
        row.Wrap = true;
        row.Align = FlexAlign.Start;
        row.ScrollY = true;

        Arrange(row, 100f, 100f);

        Assert.Equal(new[] { 0f, 10f, 20f }, new[] { small.Rect.Y, wide.Rect.Y, after.Rect.Y });
        Assert.Equal(150f, wide.Rect.Width);
    }

    [Fact]
    public void Hidden_items_do_not_take_a_place_in_a_line()
    {
        FlexNode a = Leaf(50f, 10f), hidden = Leaf(50f, 10f), b = Leaf(50f, 10f);
        hidden.Hidden = true;
        FlexNode row = Row(a, hidden, b);
        row.Wrap = true;
        row.ScrollY = true;

        Arrange(row, 100f, 100f);

        Assert.Equal(new FlexRect(50f, 0f, 50f, 10f), b.Rect);
    }

    [Fact]
    public void A_scrolling_column_does_not_shrink_its_items()
    {
        FlexNode a = Leaf(50f, 80f, minHeight: 10f), b = Leaf(50f, 80f, minHeight: 10f);
        FlexNode column = Column(a, b);
        column.ScrollY = true;
        column.Gap = 4f;
        column.Padding = FlexEdges.All(2f);

        Arrange(column, 100f, 100f);

        Assert.Equal(new FlexRect(2f, 86f, 96f, 80f), b.Rect);
        Assert.Equal(new FlexSize(100f, 168f), column.ContentSize);
    }

    [Fact]
    public void A_wrapping_row_that_scrolls_vertically_keeps_its_width_and_grows_down()
    {
        FlexNode[] items = Items(7, 30f, 30f);
        FlexNode row = Row(items);
        row.Wrap = true;
        row.Gap = 5f;
        row.ScrollY = true;

        Arrange(row, 100f, 50f);

        // Three per line: lines at 0, 35 and 70; nothing stretches the lines.
        Assert.Equal(new FlexRect(0f, 70f, 30f, 30f), items[6].Rect);
        Assert.Equal(new FlexSize(100f, 100f), row.ContentSize);
    }

    [Fact]
    public void Wrap_does_not_break_along_an_axis_that_scrolls()
    {
        FlexNode[] items = Items(5, 30f, 10f);
        FlexNode row = Row(items);
        row.Wrap = true;
        row.ScrollX = true;

        Arrange(row, 100f, 10f);

        Assert.Equal(new FlexRect(120f, 0f, 30f, 10f), items[4].Rect);
        Assert.Equal(new FlexSize(150f, 10f), row.ContentSize);
    }
}
```

`tests/AcDream.App.Tests/UI/Layout/Flex/FlexLayoutNestingTests.cs`:

```csharp
using AcDream.App.UI.Layout.Flex;
using static AcDream.App.Tests.UI.Layout.Flex.FlexTestNodes;

namespace AcDream.App.Tests.UI.Layout.Flex;

/// <summary>
/// Trees: a container is measured from its items and then laid out inside
/// the rect its own parent gave it; a leaf's children are its own business;
/// and bad numbers never make the layout throw.
/// </summary>
public sealed class FlexLayoutNestingTests
{
    /// <summary>The spec's example window: a toolbar over a growing list over an OK/Cancel footer.</summary>
    private static (FlexNode root, FlexNode header, FlexNode field, FlexNode refresh, FlexNode list,
        FlexNode footer, FlexNode cancel, FlexNode ok) ToolbarListFooter()
    {
        FlexNode field = Leaf(120f, 20f, minWidth: 40f);
        field.Grow = 1f;
        FlexNode refresh = Leaf(70f, 24f);
        FlexNode header = Row(field, refresh);
        header.Gap = 4f;

        FlexNode list = Leaf(160f, 80f, minWidth: 60f, minHeight: 40f);
        list.Grow = 1f;

        FlexNode cancel = Leaf(70f, 24f), ok = Leaf(70f, 24f);
        FlexNode footer = Row(cancel, ok);
        footer.Gap = 4f;
        footer.Justify = FlexJustify.End;

        FlexNode root = Column(header, list, footer);
        root.Padding = FlexEdges.All(8f);
        root.Gap = 6f;
        return (root, header, field, refresh, list, footer, cancel, ok);
    }

    [Fact]
    public void Nested_containers_lay_out_inside_the_rect_their_parent_gave_them()
    {
        var w = ToolbarListFooter();

        Arrange(w.root, 360f, 260f);

        // Column content 344 x 244; 24 + 80 + 24 + 12 = 140 used, the list takes the other 104.
        Assert.Equal(new FlexRect(8f, 8f, 344f, 24f), w.header.Rect);
        Assert.Equal(new FlexRect(8f, 38f, 344f, 184f), w.list.Rect);
        Assert.Equal(new FlexRect(8f, 228f, 344f, 24f), w.footer.Rect);
        // Inside the header: the field grows by 344 - 120 - 70 - 4 = 150 and stretches to 24 high.
        Assert.Equal(new FlexRect(0f, 0f, 270f, 24f), w.field.Rect);
        Assert.Equal(new FlexRect(274f, 0f, 70f, 24f), w.refresh.Rect);
        // Inside the footer: justify end.
        Assert.Equal(new FlexRect(200f, 0f, 70f, 24f), w.cancel.Rect);
        Assert.Equal(new FlexRect(274f, 0f, 70f, 24f), w.ok.Rect);
    }

    [Fact]
    public void Nested_minimums_add_up_to_the_window_minimum()
    {
        var w = ToolbarListFooter();

        FlexMeasurement m = FlexLayout.Measure(w.root);

        // Widest minimum is the footer, 70 + 4 + 70; heights 24 + 40 + 24 + two gaps.
        Assert.Equal(new FlexSize(144f + 16f, 100f + 16f), m.Minimum);
    }

    private static (FlexNode root, FlexNode toolbar, FlexNode list, FlexNode lastButton) WrappingToolbarOverList()
    {
        FlexNode toolbar = Row(Leaf(60f, 20f), Leaf(60f, 20f), Leaf(60f, 20f), Leaf(60f, 20f));
        toolbar.Wrap = true;
        toolbar.Gap = 4f;
        FlexNode list = Leaf(160f, 80f, minWidth: 60f, minHeight: 40f);
        list.Grow = 1f;
        FlexNode root = Column(toolbar, list);
        return (root, toolbar, list, toolbar.Children[3]);
    }

    [Theory]
    [InlineData(300f, 20f, 0f)]   // 4 x 60 + 3 x 4 = 252 fits on one line
    [InlineData(140f, 44f, 24f)]  // two per line: two lines and a gap
    [InlineData(60f, 92f, 72f)]   // one per line
    public void A_wrapping_row_in_a_column_is_as_tall_as_the_lines_it_makes_at_its_width(
        float width, float toolbarHeight, float lastButtonY)
    {
        var w = WrappingToolbarOverList();

        Arrange(w.root, width, 400f);

        Assert.Equal(new FlexRect(0f, 0f, width, toolbarHeight), w.toolbar.Rect);
        Assert.Equal(new FlexRect(0f, toolbarHeight, width, 400f - toolbarHeight), w.list.Rect);
        Assert.Equal(lastButtonY, w.lastButton.Rect.Y);
    }

    [Fact]
    public void A_wrapping_row_reports_its_narrowest_lines_as_its_minimum()
    {
        var w = WrappingToolbarOverList();

        FlexMeasurement m = FlexLayout.Measure(w.root);

        // At 60 wide the toolbar makes four lines: 4 x 20 + 3 x 4 = 92, over a 40-high list.
        Assert.Equal(new FlexSize(60f, 92f + 40f), m.Minimum);
        Assert.Equal(20f, w.toolbar.Measured.Preferred.Height);
    }

    [Fact]
    public void A_leaf_with_children_is_not_laid_out_by_the_engine()
    {
        FlexNode inner = Leaf(10f, 10f);
        FlexNode absoluteGroup = Leaf(100f, 50f);
        absoluteGroup.Children.Add(inner);
        inner.Rect = new FlexRect(3f, 4f, 5f, 6f);

        Arrange(Row(absoluteGroup), 300f, 50f);

        Assert.Equal(new FlexRect(0f, 0f, 100f, 50f), absoluteGroup.Rect);
        Assert.Equal(new FlexRect(3f, 4f, 5f, 6f), inner.Rect);
    }

    [Fact]
    public void Non_finite_and_negative_numbers_clamp_to_zero_instead_of_throwing()
    {
        FlexNode nanLeaf = new()
        {
            Measure = _ => new FlexMeasurement(new FlexSize(float.NaN, -5f), new FlexSize(float.PositiveInfinity, float.NaN)),
        };
        FlexNode badFactors = Leaf(20f, 10f);
        badFactors.Grow = float.NaN;
        badFactors.Shrink = -1f;
        FlexNode row = Row(nanLeaf, badFactors);
        row.Gap = float.NaN;

        Arrange(row, 100f, float.NaN);

        Assert.Equal(new FlexRect(0f, 0f, 0f, 0f), nanLeaf.Rect);
        Assert.Equal(new FlexRect(0f, 0f, 20f, 10f), badFactors.Rect); // never squashed below its content height
    }

    [Fact]
    public void Content_size_reaches_the_furthest_item_plus_trailing_padding()
    {
        FlexNode a = Leaf(30f, 10f), b = Leaf(40f, 20f);
        FlexNode row = Row(a, b);
        row.Padding = new FlexEdges(1f, 2f, 3f, 4f);
        row.Align = FlexAlign.Start;

        Arrange(row, 200f, 100f);

        Assert.Equal(new FlexSize(4f + 70f + 2f, 1f + 20f + 3f), row.ContentSize);
    }
}
```

- [ ] **Step 3: Run the tests to verify they fail**

Run: `export DOTNET_ROOT=$HOME/.dotnet PATH=$HOME/.dotnet:$PATH && dotnet test tests/AcDream.App.Tests/AcDream.App.Tests.csproj --filter "FullyQualifiedName~UI.Layout.Flex"`
Expected: build FAILS with `CS0117: 'FlexLayout' does not contain a definition for 'Arrange'`.

- [ ] **Step 4: Add `Arrange` to the engine**

In `src/AcDream.App/UI/Layout/Flex/FlexLayout.cs`, insert immediately before the line `    // ── Measure ─────────────────────────────────────────────────────────`:

```csharp
    /// <summary>
    /// Measures the tree, then lays it out with <paramref name="root"/>
    /// occupying <paramref name="bounds"/> (stored unchanged as the root's
    /// <see cref="FlexNode.Rect"/>). Every descendant's rect is relative to
    /// its own parent.
    /// </summary>
    public static void Arrange(FlexNode root, FlexRect bounds)
    {
        MeasureNode(root);
        root.Rect = bounds;
        ArrangeChildren(root, Positive(bounds.Width), Positive(bounds.Height));
    }
```

- [ ] **Step 5: Add the arrange pass**

In the same file, insert immediately before the line `    // ── Helpers ─────────────────────────────────────────────────────────`:

```csharp
    // ── Arrange ─────────────────────────────────────────────────────────

    // Per-container scratch, in quarters of FlexNode.Scratch for `count` children:
    // [0, c) resolved main size, [c, 2c) frozen flag, [2c, 3c) flex base size,
    // [3c, 4c) minimum main size.
    private const int TargetSlot = 0, FrozenSlot = 1, BaseSlot = 2, MinSlot = 3;

    private static void ArrangeChildren(FlexNode node, float width, float height)
    {
        if (node.Measure is not null)
        {
            node.ContentSize = new FlexSize(width, height);
            return;
        }

        bool row = node.Direction == FlexDirection.Row;
        FlexEdges p = node.Padding;
        float gap = Positive(node.Gap);
        bool mainScrolls = row ? node.ScrollX : node.ScrollY;
        bool crossScrolls = row ? node.ScrollY : node.ScrollX;
        float contentMain = Positive((row ? width : height) - (row ? p.Horizontal : p.Vertical));
        float contentCross = Positive((row ? height : width) - (row ? p.Vertical : p.Horizontal));
        float? availableMain = mainScrolls ? null : contentMain;
        float? availableCross = crossScrolls ? null : contentCross;
        float originMain = row ? p.Left : p.Top;
        float originCross = row ? p.Top : p.Left;
        bool singleLine = !node.Wrap || availableMain is null;

        int count = node.Children.Count;
        if (node.Scratch.Length < count * 4) node.Scratch = new float[count * 4];
        ResolveBases(node, row, count, availableCross, contentCross, singleLine);

        BreakLines(node, row, count, availableMain, gap);

        int lineCount = node.LineEnds.Count;
        float linesCross = 0f;
        int lineStart = 0;
        for (int line = 0; line < lineCount; line++)
        {
            int lineEnd = node.LineEnds[line];
            ResolveFlexibleLengths(node, row, count, lineStart, lineEnd, availableMain, gap);
            linesCross += LineCross(node, row, lineStart, lineEnd);
            lineStart = lineEnd;
        }

        // Spare cross space goes to the lines, as CSS's default align-content
        // does; a single line simply takes the whole cross size.
        float lineGaps = lineCount > 1 ? gap * (lineCount - 1) : 0f;
        float extraPerLine = 0f;
        if (availableCross is { } crossSpace && lineCount > 0)
        {
            extraPerLine = singleLine
                ? crossSpace - linesCross
                : MathF.Max(0f, (crossSpace - linesCross - lineGaps) / lineCount);
        }

        float maxMainEdge = 0f, maxCrossEdge = 0f;
        float crossCursor = originCross;
        lineStart = 0;
        for (int line = 0; line < lineCount; line++)
        {
            int lineEnd = node.LineEnds[line];
            float lineCross = MathF.Max(0f, LineCross(node, row, lineStart, lineEnd) + extraPerLine);
            PlaceLine(node, row, count, lineStart, lineEnd, availableMain, gap, originMain, crossCursor, lineCross);
            for (int i = lineStart; i < lineEnd; i++)
            {
                FlexNode child = node.Children[i];
                if (child.Hidden) continue;
                FlexRect r = child.Rect;
                maxMainEdge = MathF.Max(maxMainEdge, row ? r.X + r.Width : r.Y + r.Height);
                maxCrossEdge = MathF.Max(maxCrossEdge, row ? r.Y + r.Height : r.X + r.Width);
            }
            crossCursor += lineCross + gap;
            lineStart = lineEnd;
        }

        for (int i = 0; i < count; i++)
        {
            FlexNode child = node.Children[i];
            if (child.Hidden) { child.Rect = default; continue; }
            ArrangeChildren(child, child.Rect.Width, child.Rect.Height);
        }

        float trailMain = row ? p.Right : p.Bottom;
        float trailCross = row ? p.Bottom : p.Right;
        node.ContentSize = Size(maxMainEdge + trailMain, maxCrossEdge + trailCross, row);
    }

    /// <summary>
    /// Fills each visible child's flex base size and minimum main size. Most
    /// children use their measurement; a wrapping child laid across this
    /// container's main axis is measured at the cross size it will be given.
    /// </summary>
    private static void ResolveBases(
        FlexNode node, bool row, int count, float? availableCross, float contentCross, bool singleLine)
    {
        float[] s = node.Scratch;
        for (int i = 0; i < count; i++)
        {
            FlexNode child = node.Children[i];
            if (child.Hidden) continue;

            if (!IsSizedAcross(node, child))
            {
                s[BaseSlot * count + i] = MeasuredBase(child, row);
                s[MinSlot * count + i] = MainOf(child.MinimumSize, row);
                continue;
            }

            // The child's own main axis is our cross axis: find the cross size it
            // gets (stretched over a single line, else its preferred size within
            // the space), then the extent its lines make along our main axis.
            float? explicitCross = row ? child.Height : child.Width;
            float minCross = CrossOf(child.MinimumSize, row), maxCross = CrossOf(child.MaximumSize, row);
            FlexAlign align = child.AlignSelf ?? node.Align;
            bool stretched = singleLine && availableCross is not null && align == FlexAlign.Stretch && explicitCross is null;
            float cross = stretched ? contentCross : CrossOf(child.Measured.Preferred, row);
            if (!stretched && availableCross is { } limit) cross = MathF.Min(cross, limit);
            cross = Math.Clamp(cross, minCross, MathF.Max(minCross, maxCross));

            float extent = WrappedExtent(child, cross);
            float? explicitMain = row ? child.Width : child.Height;
            float? explicitMin = row ? child.MinWidth : child.MinHeight;
            float maxMain = MainOf(child.MaximumSize, row);
            float autoMin = explicitMain is { } e ? MathF.Min(Positive(e), extent) : extent;
            s[BaseSlot * count + i] = child.Basis is { } basis ? Positive(basis) : explicitMain is { } em ? Positive(em) : extent;
            s[MinSlot * count + i] = MathF.Min(explicitMin is { } m ? Positive(m) : autoMin, maxMain);
        }
    }

    /// <summary>
    /// Whether <paramref name="child"/> is a wrapping container whose own main
    /// axis is <paramref name="parent"/>'s cross axis, so its size along the
    /// parent's main axis depends on the cross size it gets. A child that
    /// scrolls along either axis keeps its measured size instead.
    /// </summary>
    private static bool IsSizedAcross(FlexNode parent, FlexNode child) =>
        child.Measure is null && child.Wrap && child.Direction != parent.Direction
        && !child.ScrollX && !child.ScrollY;

    /// <summary>
    /// The extent of <paramref name="node"/>'s wrapped lines across its own
    /// main axis when its main size is <paramref name="mainSize"/>, padding
    /// included. Uses the same line breaking as the arrange pass.
    /// </summary>
    private static float WrappedExtent(FlexNode node, float mainSize)
    {
        bool row = node.Direction == FlexDirection.Row;
        FlexEdges p = node.Padding;
        float gap = Positive(node.Gap);
        float available = Positive(mainSize - (row ? p.Horizontal : p.Vertical));
        float total = 0f, lineMain = 0f, lineCross = 0f;
        int lines = 0, inLine = 0;
        for (int i = 0; i < node.Children.Count; i++)
        {
            FlexNode child = node.Children[i];
            if (child.Hidden) continue;
            float min = MainOf(child.MinimumSize, row);
            float main = Math.Clamp(MeasuredBase(child, row), min, MathF.Max(min, MainOf(child.MaximumSize, row)));
            if (inLine > 0 && lineMain + gap + main > available)
            {
                total += lineCross;
                lines++;
                lineMain = 0f; lineCross = 0f; inLine = 0;
            }
            lineMain += (inLine > 0 ? gap : 0f) + main;
            lineCross = MathF.Max(lineCross, CrossOf(child.Measured.Preferred, row));
            inLine++;
        }
        if (inLine > 0) { total += lineCross; lines++; }
        return total + (lines > 1 ? gap * (lines - 1) : 0f) + (row ? p.Vertical : p.Horizontal);
    }

    /// <summary>Fills <see cref="FlexNode.LineEnds"/> with the exclusive end index of each line.</summary>
    private static void BreakLines(FlexNode node, bool row, int count, float? availableMain, float gap)
    {
        node.LineEnds.Clear();
        if (!node.Wrap || availableMain is not { } available)
        {
            node.LineEnds.Add(count);
            return;
        }

        float lineMain = 0f;
        int inLine = 0;
        for (int i = 0; i < count; i++)
        {
            FlexNode child = node.Children[i];
            if (child.Hidden) continue;
            float main = Hypothetical(node, child, row, count, i);
            if (inLine > 0 && lineMain + gap + main > available)
            {
                node.LineEnds.Add(i);
                lineMain = 0f;
                inLine = 0;
            }
            lineMain += (inLine > 0 ? gap : 0f) + main;
            inLine++;
        }
        node.LineEnds.Add(count);
    }

    /// <summary>
    /// CSS "resolve flexible lengths" for one line: writes each visible item's
    /// main size into the target slot. Items whose clamped size differs from
    /// their flexed size are frozen and the rest re-flexed until nothing changes.
    /// </summary>
    private static void ResolveFlexibleLengths(
        FlexNode node, bool row, int count, int start, int end, float? availableMain, float gap)
    {
        float[] s = node.Scratch;
        int target = TargetSlot * count, frozen = FrozenSlot * count, bases = BaseSlot * count, mins = MinSlot * count;

        int visible = 0;
        float hypotheticalSum = 0f;
        for (int i = start; i < end; i++)
        {
            FlexNode child = node.Children[i];
            if (child.Hidden) continue;
            s[target + i] = Hypothetical(node, child, row, count, i);
            hypotheticalSum += s[target + i];
            visible++;
        }
        if (availableMain is not { } available || visible == 0) return;

        float gaps = visible > 1 ? gap * (visible - 1) : 0f;
        bool growing = available - gaps - hypotheticalSum > 0f;
        for (int i = start; i < end; i++)
        {
            FlexNode child = node.Children[i];
            if (child.Hidden) continue;
            float factor = growing ? Positive(child.Grow) : Positive(child.Shrink);
            float baseSize = s[bases + i];
            bool isFrozen = factor == 0f
                || (growing && baseSize > s[target + i])
                || (!growing && baseSize < s[target + i]);
            s[frozen + i] = isFrozen ? 1f : 0f;
        }

        for (int pass = 0; pass <= visible; pass++)
        {
            float used = gaps, factorSum = 0f, scaledShrinkSum = 0f;
            for (int i = start; i < end; i++)
            {
                FlexNode child = node.Children[i];
                if (child.Hidden) continue;
                if (s[frozen + i] != 0f) { used += s[target + i]; continue; }
                used += s[bases + i];
                factorSum += growing ? Positive(child.Grow) : Positive(child.Shrink);
                scaledShrinkSum += Positive(child.Shrink) * s[bases + i];
            }
            if (factorSum == 0f) break;

            float free = available - used;
            if (factorSum < 1f) free *= factorSum; // CSS: factors summing below 1 take only that share

            float violation = 0f;
            for (int i = start; i < end; i++)
            {
                FlexNode child = node.Children[i];
                if (child.Hidden || s[frozen + i] != 0f) continue;
                float baseSize = s[bases + i];
                float flexed;
                if (growing)
                    flexed = baseSize + free * Positive(child.Grow) / factorSum;
                else
                    flexed = scaledShrinkSum > 0f
                        ? baseSize + free * Positive(child.Shrink) * baseSize / scaledShrinkSum
                        : baseSize;
                float clamped = Math.Clamp(flexed, s[mins + i], MathF.Max(s[mins + i], MainOf(child.MaximumSize, row)));
                violation += clamped - flexed;
                s[target + i] = clamped;
            }

            if (violation == 0f) break;
            // Freeze the items that hit the side the total violation points to.
            for (int i = start; i < end; i++)
            {
                FlexNode child = node.Children[i];
                if (child.Hidden || s[frozen + i] != 0f) continue;
                bool atMin = s[target + i] <= s[mins + i];
                bool atMax = s[target + i] >= MainOf(child.MaximumSize, row);
                if ((violation > 0f && atMin) || (violation < 0f && atMax))
                    s[frozen + i] = 1f;
            }
        }
    }

    private static float LineCross(FlexNode node, bool row, int start, int end)
    {
        float cross = 0f;
        for (int i = start; i < end; i++)
        {
            FlexNode child = node.Children[i];
            if (child.Hidden) continue;
            cross = MathF.Max(cross, CrossOf(child.Measured.Preferred, row));
        }
        return cross;
    }

    private static void PlaceLine(
        FlexNode node, bool row, int count, int start, int end, float? availableMain, float gap,
        float originMain, float lineCrossStart, float lineCross)
    {
        float[] s = node.Scratch;
        int visible = 0;
        float used = 0f;
        for (int i = start; i < end; i++)
        {
            if (node.Children[i].Hidden) continue;
            used += s[TargetSlot * count + i];
            visible++;
        }
        if (visible == 0) return;
        used += gap * (visible - 1);

        float free = availableMain is { } available ? MathF.Max(0f, available - used) : 0f;
        float offset = 0f, between = gap;
        switch (node.Justify)
        {
            case FlexJustify.Center: offset = free / 2f; break;
            case FlexJustify.End: offset = free; break;
            case FlexJustify.SpaceBetween when visible > 1: between = gap + free / (visible - 1); break;
        }

        // Positions accumulate unsnapped; each edge is rounded on its own, so
        // rounding error never piles up at the end of the line.
        float cursor = originMain + offset;
        for (int i = start; i < end; i++)
        {
            FlexNode child = node.Children[i];
            if (child.Hidden) continue;
            float mainStart = cursor, mainEnd = cursor + s[TargetSlot * count + i];
            cursor = mainEnd + between;

            FlexAlign align = child.AlignSelf ?? node.Align;
            float? explicitCross = row ? child.Height : child.Width;
            float minCross = CrossOf(child.MinimumSize, row), maxCross = CrossOf(child.MaximumSize, row);
            float cross = align == FlexAlign.Stretch && explicitCross is null
                ? Math.Clamp(lineCross, minCross, MathF.Max(minCross, maxCross))
                : CrossOf(child.Measured.Preferred, row);
            float crossOffset = align switch
            {
                FlexAlign.Center => (lineCross - cross) / 2f,
                FlexAlign.End => lineCross - cross,
                _ => 0f,
            };
            float crossStart = lineCrossStart + crossOffset, crossEnd = crossStart + cross;

            float m0 = Snap(mainStart), m1 = Snap(mainEnd), c0 = Snap(crossStart), c1 = Snap(crossEnd);
            child.Rect = row
                ? new FlexRect(m0, c0, Positive(m1 - m0), Positive(c1 - c0))
                : new FlexRect(c0, m0, Positive(c1 - c0), Positive(m1 - m0));
        }
    }
```

- [ ] **Step 6: Add the two arrange helpers**

In the same file's Helpers section, insert immediately before `    private static float MainOf(FlexSize size, bool row)`:

```csharp
    private static float Hypothetical(FlexNode node, FlexNode child, bool row, int count, int index)
    {
        float min = node.Scratch[MinSlot * count + index];
        return Math.Clamp(node.Scratch[BaseSlot * count + index], min, MathF.Max(min, MainOf(child.MaximumSize, row)));
    }
```

and immediately before the `/// <summary>Non-finite or negative computed values are defects` comment of `Positive`:

```csharp
    private static float Snap(float value) => float.IsFinite(value) ? MathF.Floor(value + 0.5f) : 0f;
```

- [ ] **Step 7: Run the tests to verify they pass**

Run: `export DOTNET_ROOT=$HOME/.dotnet PATH=$HOME/.dotnet:$PATH && dotnet test tests/AcDream.App.Tests/AcDream.App.Tests.csproj --filter "FullyQualifiedName~UI.Layout.Flex"`
Expected: `Passed: 51, Failed: 0`, no build warnings.

- [ ] **Step 8: Commit**

```bash
git checkout -- '*.lock.json'
git add src/AcDream.App/UI/Layout/Flex tests/AcDream.App.Tests/UI/Layout/Flex
git commit -m "feat(ui): flex layout arrange pass

Lines, flexible lengths with freezing, justify/align, wrapping with
shared cross space, unbounded scrolling axes, height-for-width wrapping
children and cumulative-edge snapping.

Claude-Session: https://claude.ai/code/session_019dvJ5gakBkq3uJyr1PAC3u"
```

---

### Task 3: Cost tests — linear measuring, zero-allocation relayout

**Goal:** Prove by counting (not timing) that one arrange measures each leaf exactly once regardless of depth, and that relaying out the same tree allocates nothing.

**Files:**
- Test: `tests/AcDream.App.Tests/UI/Layout/Flex/FlexLayoutScalingTests.cs`

**Acceptance Criteria:**
- [ ] For 50, 100 and 200 leaves nested two containers deep, the measure callback runs exactly `count` times per arrange.
- [ ] After warming up over widths 200–299, a second pass over the same widths allocates 0 bytes (`GC.GetAllocatedBytesForCurrentThread`).
- [ ] The allocation test fails when the scratch buffer is reallocated on every arrange (mutation check), proving it can catch a regression.
- [ ] All flex tests: 55 passed.

**Verify:** `export DOTNET_ROOT=$HOME/.dotnet PATH=$HOME/.dotnet:$PATH && dotnet test tests/AcDream.App.Tests/AcDream.App.Tests.csproj --filter "FullyQualifiedName~UI.Layout.Flex"` → `Passed: 55, Failed: 0`

**Steps:**

- [ ] **Step 1: Write the cost tests**

`tests/AcDream.App.Tests/UI/Layout/Flex/FlexLayoutScalingTests.cs`:

```csharp
using System;
using AcDream.App.UI.Layout.Flex;

namespace AcDream.App.Tests.UI.Layout.Flex;

/// <summary>
/// Cost, counted rather than timed: one arrange measures each leaf exactly
/// once however deep the tree, and a repeated arrange of the same tree
/// allocates nothing, so relaying out every frame during a resize is cheap.
/// </summary>
public sealed class FlexLayoutScalingTests
{
    private static readonly FlexMeasurement Icon = new(new FlexSize(32f, 32f), new FlexSize(32f, 32f));

    private static FlexMeasurement MeasureIcon(float? availableWidth) => Icon;

    private static FlexNode Grid(int count, FlexMeasure measure)
    {
        var grid = new FlexNode { Direction = FlexDirection.Row, Wrap = true, Gap = 4f, ScrollY = true };
        for (int i = 0; i < count; i++)
            grid.Children.Add(new FlexNode { Measure = measure });
        return grid;
    }

    [Theory]
    [InlineData(50)]
    [InlineData(100)]
    [InlineData(200)]
    public void Each_leaf_is_measured_once_per_arrange(int count)
    {
        int calls = 0;
        FlexNode grid = Grid(count, _ => { calls++; return Icon; });
        // Nest the grid two containers deep: depth must not multiply the calls.
        var inner = new FlexNode { Direction = FlexDirection.Column };
        inner.Children.Add(grid);
        var root = new FlexNode { Direction = FlexDirection.Row };
        root.Children.Add(inner);

        FlexLayout.Arrange(root, new FlexRect(0f, 0f, 300f, 400f));

        Assert.Equal(count, calls);
    }

    [Fact]
    public void A_repeated_arrange_allocates_nothing()
    {
        FlexNode grid = Grid(200, MeasureIcon);
        // Warm up over the same widths: the narrowest makes the most lines, so the
        // line buffer reaches its final capacity here.
        for (int width = 200; width < 300; width++)
            FlexLayout.Arrange(grid, new FlexRect(0f, 0f, width, 400f));

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int width = 200; width < 300; width++)
            FlexLayout.Arrange(grid, new FlexRect(0f, 0f, width, 400f));
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.Equal(0L, allocated);
    }
}
```

- [ ] **Step 2: Run them**

Run: `export DOTNET_ROOT=$HOME/.dotnet PATH=$HOME/.dotnet:$PATH && dotnet test tests/AcDream.App.Tests/AcDream.App.Tests.csproj --filter "FullyQualifiedName~UI.Layout.Flex.FlexLayoutScalingTests"`
Expected: `Passed: 4, Failed: 0` (the engine from Task 2 already meets both properties; these tests lock them in).

- [ ] **Step 3: Mutation check — the allocation test must be able to fail**

In `src/AcDream.App/UI/Layout/Flex/FlexLayout.cs`, temporarily change

```csharp
        if (node.Scratch.Length < count * 4) node.Scratch = new float[count * 4];
```

to

```csharp
        node.Scratch = new float[count * 4];
```

Run: `export DOTNET_ROOT=$HOME/.dotnet PATH=$HOME/.dotnet:$PATH && dotnet test tests/AcDream.App.Tests/AcDream.App.Tests.csproj --filter "FullyQualifiedName~UI.Layout.Flex.FlexLayoutScalingTests.A_repeated_arrange_allocates_nothing"`
Expected: FAIL (`Assert.Equal() Failure`, a non-zero byte count).

Restore the file and rebuild from a fresh timestamp (a restored file can keep an old mtime and skip the rebuild):

```bash
git checkout -- src/AcDream.App/UI/Layout/Flex/FlexLayout.cs
touch src/AcDream.App/UI/Layout/Flex/FlexLayout.cs
```

- [ ] **Step 4: Run all flex tests**

Run: `export DOTNET_ROOT=$HOME/.dotnet PATH=$HOME/.dotnet:$PATH && dotnet test tests/AcDream.App.Tests/AcDream.App.Tests.csproj --filter "FullyQualifiedName~UI.Layout.Flex"`
Expected: `Passed: 55, Failed: 0`.

- [ ] **Step 5: Commit**

```bash
git checkout -- '*.lock.json'
git add tests/AcDream.App.Tests/UI/Layout/Flex/FlexLayoutScalingTests.cs
git commit -m "test(ui): flex layout measures each leaf once and relayouts without allocating

Claude-Session: https://claude.ai/code/session_019dvJ5gakBkq3uJyr1PAC3u"
```

---

### Task 4: Full verification, review and merge into fork main

**Goal:** The branch builds clean, introduces no test regressions against fork main, passes a code review, and is merged into fork main with the user's go-ahead.

**Files:**
- No source changes (review fixes, if any, are committed on `flex/engine` with their own messages).

**Acceptance Criteria:**
- [ ] `dotnet build AcDream.slnx -c Release` prints `0 Warning(s)` and `0 Error(s)`.
- [ ] `AcDream.App.Tests` on `flex/engine` has no failing test that does not also fail on `main` (compare failing-test name lists; known flaky classes from the local-env notes are rerun in isolation before counting).
- [ ] A code review of `main..flex/engine` (superpowers-extended-cc:requesting-code-review) reports no unresolved Critical/High issue.
- [ ] The user has confirmed pushing `flex/engine` to origin and merging it into fork main (`--no-ff`); main is pushed only after that confirmation.

**Verify:** `git log --oneline main..flex/engine` lists the Task 1–3 commits (plus any review fixes), and `export DOTNET_ROOT=$HOME/.dotnet PATH=$HOME/.dotnet:$PATH && dotnet build AcDream.slnx -c Release 2>&1 | grep -E "Warning\(s\)|Error\(s\)"` → `0 Warning(s)` / `0 Error(s)`.

**Steps:**

- [ ] **Step 1: Release build**

```bash
export DOTNET_ROOT=$HOME/.dotnet PATH=$HOME/.dotnet:$PATH
dotnet build AcDream.slnx -c Release 2>&1 | grep -E "Warning\(s\)|Error\(s\)"
git checkout -- '*.lock.json'
```

Expected: `0 Warning(s)`, `0 Error(s)`.

- [ ] **Step 2: Baseline comparison**

```bash
export DOTNET_ROOT=$HOME/.dotnet PATH=$HOME/.dotnet:$PATH
dotnet test tests/AcDream.App.Tests/AcDream.App.Tests.csproj 2>&1 | grep -E "^\s+Failed " | sort > "${TMPDIR:-/tmp}"/flex-branch-failures.txt
cd /Users/davidsmith/code/OpenAC/OpenAC
dotnet test tests/AcDream.App.Tests/AcDream.App.Tests.csproj 2>&1 | grep -E "^\s+Failed " | sort > "${TMPDIR:-/tmp}"/flex-main-failures.txt
comm -23 "${TMPDIR:-/tmp}"/flex-branch-failures.txt "${TMPDIR:-/tmp}"/flex-main-failures.txt
cd .worktrees/flex-engine
```

Do not revert lock files in the main checkout: it may hold the user's own uncommitted changes.

Expected: `comm` prints nothing (no branch-only failures). If it prints a test from `GraphicalPluginSessionTests`, `LiveEntityNetworkBranchRoutingTests` or `GameWindowRenderLeafCompositionTests`, rerun that class alone on both checkouts before counting it.

- [ ] **Step 3: Code review**

Invoke superpowers-extended-cc:requesting-code-review for `main..flex/engine` against this plan and spec section 3.1. Fix Critical/High findings on the branch, re-run Task 2's and Task 3's verify commands, commit each fix.

- [ ] **Step 4: Push and merge (ask first)**

Ask the user to confirm, then:

```bash
git push -u origin flex/engine
cd /Users/davidsmith/code/OpenAC/OpenAC
git merge --no-ff flex/engine -m "Merge branch 'flex/engine': flex layout engine (PR 1)

Claude-Session: https://claude.ai/code/session_019dvJ5gakBkq3uJyr1PAC3u"
git push origin main
```
