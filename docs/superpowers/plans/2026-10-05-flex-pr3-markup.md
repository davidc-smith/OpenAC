# Flex layout PR 3 (markup) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers-extended-cc:subagent-driven-development (recommended) or superpowers-extended-cc:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Plugin markup gets flex layout: `layout="row|column"` on a `<group>` or the root `<panel>` places children by content size, grow/shrink and wrapping instead of coordinates. A flex window sizes itself, gets the title bar by default, and keeps its minimum on what its content needs.

**Architecture:** A flex container (`UiFlexGroup`, or the window's `UiPluginContentHost` when the root has `layout`) owns a `UiFlexBox`: its `FlexNode` and one `UiFlexItem` per child. Nested flex groups are inner nodes of their flex root's one tree (height-for-width only works across inner nodes). Controls and absolute groups are leaves measured by `MarkupContentSize`. A flex root lays out in a new `UiElement.LayoutChildren` hook, the spot where anchors are applied today, and only when its size, a child's visibility, or a leaf's measured inputs (caption, font, theme) changed. A window's minimum is `FlexFit.Minimum`: the measured minimum, grown until the content laid out at it fits. It is set at build time (the eager first measurement). When it changes later, it is applied on the next tick through the new `RetailWindowManager.EnforceMinimumSize`. Markup without `layout` builds and draws exactly as today.

**Tech Stack:** C# / .NET 10, xUnit.

**Spec:** `docs/superpowers/specs/2026-10-05-plugin-flex-layout-design.md` (fork-only docs branch `docs/flex-layout-spec`): sections 1, 3.2–3.4, 5, 6, 7 (`MarkupFlexTests`, invalidation, window minimum, scaling) and 9 (PR 3). This plan covers PR 3 only; read its "Spec corrections made while planning" first: they change parts of 3.2 and 3.3.

## Global Constraints

- Work in a worktree `.worktrees/flex-markup` on a new branch `flex/markup` from fork `main` (c4767d26 or later). Never commit `docs/superpowers/**` on that branch.
- Build and test with the pinned SDK: every shell starts with `export DOTNET_ROOT=$HOME/.dotnet PATH=$HOME/.dotnet:$PATH`. Building rewrites some `*.lock.json` files with local RID churn; revert with `git checkout -- '*.lock.json'` before each commit, never commit that churn. The one new lock file, `samples/AcDream.Plugins.FlexDemo/packages.neutral.lock.json`, is committed (Task 7).
- The repository builds with `TreatWarningsAsErrors`; the build must stay at 0 warnings.
- **No contract change.** `src/AcDream.Plugin.Abstractions/**` is untouched (no fork contract version bump). The new attributes are a documented behaviour contract in `docs/plugin-ui-markup.md`.
- **Existing markup is unchanged.** A root and groups without `layout` build the same tree, sizes and revision and draw the same vertices as today. `PluginThemeClassicIdentityTests`, `PluginThemeWindowTests`, `MarkupDocumentTests`, `MarkupPanelClickTests`, `MarkupTitleBarTests`, `MarkupWindowMountTests`, `MarkupResizableAnchorTests`, `RetailWindowManagerTests` and `RetailWindowLayoutPersistenceTests` must pass unchanged.
- Content-size constants (one place, `MarkupContentSize`): headless fallback 7 points a character and 14-point lines; control height `max(24, ceil(lineHeight + 8))`; button caption padding 12 a side, an icon adds a square column of the control height; toggle `max(20, ceil(lineHeight + 4))` tall with its caption at 17 (Classic lamp) or `1 + SwitchWidth + SwitchCaptionGap` = 35 (themed switch); icon 32×32; field and menu 120 (min 40) × control height; slider 120 (min 40) × 16; meter 120 (min 40) × 12; list and log 160×80 (min 60×40).
- Chrome: a root with a content area (`layout` or the bar) is inset 5 on the left, right and bottom, and on top 24 with the bar or 5 without (`PluginWindowChrome.TopInset`). The chrome is 10 wide, and 29 tall with the bar or 10 without. `PluginWindowChrome.Version` stays 1 (no existing inset changes).
- A window's content minimum is `FlexFit.Minimum` (whole points); `minw`/`minh` on a layout root, when given, win and are content-area sizes. Frame minimum = content minimum + chrome. Starting content size = `max(w ?? preferred, minimum)` per axis.
- Commit messages end with the line `Claude-Session: https://claude.ai/code/session_019dvJ5gakBkq3uJyr1PAC3u`.

**User decisions (already made):**
- Scope: markup windows only; canvases out of scope. Practical flexbox subset (no `order`, `align-content`, baseline).
- Item sizing: content sizes with `w`/`h`/`basis` overrides. Overflow: the content minimum drives the window minimum (scrolling groups are PR 4).
- Syntax: attributes on `<group>` and the root `<panel>`; no new element names. Engine: the in-house `FlexLayout` from PR 1.
- Title bar: on by default for windows that use `layout`, `titlebar="false"` turns it off (spec 2.2).
- On a window with a title bar the bar's X replaces the dock's "–" (PR 2); layout windows inherit this through the default bar.
- Delivery as four single-topic PRs from fork main, merged into fork main after review; spec and plans stay on the fork-only docs branch.

## Spec corrections made while planning

Found while prototyping PR 3 (a scratch branch with every task below implemented; the full App suite failed exactly main's 166 environment tests by name and passed 103 new ones). Folded into the spec under "Planning corrections (PR 3)" in the same docs commit as this plan:

1. **Changes are detected by comparison at layout time, not by setters.** The spec's `InvalidateMeasure()` walk and the theme-change action are replaced by one check per drawn flex root per frame. The check compares each item's visibility with its node's `Hidden`, and each leaf's caption, font and theme with what it was measured from (`UiFlexItem.InputsChanged`). Nothing can be missed: `SetControlLabel`, a bound caption, a theme switch and a sharper font bake all change what is compared. There is no public invalidation API. Hidden subtrees are not checked and lay out when shown. A layout with nothing changed measures nothing; a steady-state relayout allocates nothing (both tested).
2. **Content sizes are measured centrally** in `MarkupContentSize` (a switch on control type, constants in the same class) instead of an `IUiContentSize` interface on each control. No control class changes except `UiLabel.SizedByLayout`.
3. **The window minimum is fitted, not estimated** (settles the "known gaps" of spec 3.1). `FlexFit.Minimum` lays the tree out at the measured minimum and grows each non-scrolling axis until the content no longer reaches past the box (at most 4 layouts). This covers lines breaking on a `basis` above the item minimum, nested wrapping, and a preferred size below the minimum. It runs only when content changes, never on a plain resize.
4. **`wrap="true"` on a column is a build error in v1** (settles the one-dependence-direction limitation of spec 3.1: only rows wrap). It can be allowed later without breaking markup.
5. **A changed minimum is applied on the tick after the layout that found it.** The content host raises `ContentMinimumChanged` from its tick, so the window is resized outside the draw. `MarkupWindow` applies it: through `EnforceMinimumSize` once registered, by growing the frame directly before.
6. **An authored `w`/`h` below the content minimum opens at the minimum** (CSS: min wins over size). An authored `minw`/`minh` still wins over the content minimum.
7. **A label in flex centres its line vertically** in the rect it is given (it would otherwise sit at the top of a stretched row next to centred button captions).
8. **A layout root with `titlebar="false"`** has a content area inset 5 on every side (from the PR 2 review); its frame is content + 10 × content + 10.
9. **Root `w`, `h`, `minw`, `minh` on a layout root are parsed strictly** (finite, non-negative), like the item numbers; roots without `layout` keep lenient parsing.
10. **No theme case for `UiFlexGroup`** (spec 6): `PluginMarkupTheme.Register` has no case for groups at all (their colours are literal or bound), and `UiFlexGroup` is a `UiPanel`, so it is themed exactly as a group already is.
11. **FlexDemo windows** are a "Flex Finder" (spec window 1) and "Flex Settings" (spec window 3, without scrolling). Window 2 (the scrolling icon grid) and scrolling in window 3 come with PR 4.

## File Structure

| File | Responsibility |
|---|---|
| Create `src/AcDream.App/UI/MarkupFlexAttributes.cs` | Reads container and item attributes onto `FlexNode`s; every section-5 build error. |
| Create `src/AcDream.App/UI/Layout/Flex/FlexFit.cs` | The smallest box a flex tree really fits in (pure, engine side). |
| Create `src/AcDream.App/UI/MarkupContentSize.cs` | Content sizes of every markup control and the inputs they depend on. |
| Create `src/AcDream.App/UI/UiFlexBox.cs` | `UiFlexBox` (a container's node and items; layout on change; placing elements) and `UiFlexItem` (one child and its measured inputs). |
| Create `src/AcDream.App/UI/UiFlexGroup.cs` | `<group layout>`: a `UiPanel` that lays out through its box. |
| Modify `src/AcDream.App/UI/UiElement.cs` | `LayoutChildren()` hook around today's anchor loop. |
| Modify `src/AcDream.App/UI/UiPanel.cs` | `UiLabel.SizedByLayout`. |
| Modify `src/AcDream.App/UI/MarkupDocument.cs` | Flex groups, item registration, item-attribute rejection (Task 4); layout roots, title-bar default, eager measurement (Task 6). |
| Modify `src/AcDream.App/UI/RetailWindowManager.cs` | `EnforceMinimumSize`. |
| Modify `src/AcDream.App/UI/PluginWindowChrome.cs` | `TopInset`, `VerticalInsetsFor`. |
| Modify `src/AcDream.App/UI/PluginTitleBar.cs` | `SetFrameWidth` (a content-sized window learns its width after its children). |
| Modify `src/AcDream.App/UI/UiPluginContentHost.cs` | Optional flex box; minimum changes reported from the tick. |
| Modify `src/AcDream.App/UI/MarkupWindow.cs` | `TrackContentMinimum`; remembers its manager and name on `Register`. |
| Modify `docs/plugin-ui-markup.md` | "Flex layout" section; element table and title-bar default. |
| Create `samples/AcDream.Plugins.FlexDemo/*` | The FlexDemo sample (finder and settings windows); added to `AcDream.slnx`. |
| Create `tests/AcDream.App.Tests/UI/MarkupFlexAttributesTests.cs` | Parsing and errors. |
| Create `tests/AcDream.App.Tests/UI/Layout/Flex/FlexFitTests.cs` | Fitted minimums. |
| Create `tests/AcDream.App.Tests/UI/MarkupContentSizeTests.cs` | Content sizes. |
| Create `tests/AcDream.App.Tests/UI/MarkupFlexTests.cs` | Flex groups through build and draw: placement, nesting, reflow, labels, cost. |
| Create `tests/AcDream.App.Tests/UI/RetailWindowManagerMinimumTests.cs` | `EnforceMinimumSize`. |
| Create `tests/AcDream.App.Tests/UI/MarkupFlexWindowTests.cs` | Layout roots: bar default, insets, eager size, minimums, revision, growth. |
| Create `tests/AcDream.App.Tests/UI/FlexDemoSampleTests.cs` | The sample's markup builds and fits. |

Task order and dependencies: 1, 2, 3 and 5 are independent; 4 needs 1 and 3; 6 needs 2, 4 and 5; 7 needs 6; 8 needs all.

---

### Task 1: Flex attributes in markup

**Goal:** `MarkupFlexAttributes` reads `layout`, the container attributes and the item attributes onto `FlexNode`s and rejects every misuse of spec section 5 with a `FormatException` naming the element, attribute and value.

**Files:**
- Create: `src/AcDream.App/UI/MarkupFlexAttributes.cs`
- Test: `tests/AcDream.App.Tests/UI/MarkupFlexAttributesTests.cs`

**Acceptance Criteria:**
- [ ] Container defaults are column/row as given, gap 0, padding 0, justify start, align stretch, wrap false; item defaults grow 0, shrink 1, basis/w/h/limits/alignself null.
- [ ] `padding` takes 1, 2 or 4 numbers (spaces or commas) in CSS order.
- [ ] Unknown `layout`/`justify`/`align`/`alignself`/`wrap` values, negative or non-finite (`NaN`, `Infinity`) numbers, a bad `padding`, `wrap="true"` on a column, container attributes without `layout`, item attributes under an absolute container, `x`/`y`/`anchor` on a flex item, and `minw > maxw` / `minh > maxh` are all build errors whose message contains the element identity and the `attr="value"` text.

**Verify:** `dotnet test tests/AcDream.App.Tests --filter "FullyQualifiedName~MarkupFlexAttributesTests"` → 55 passed.

**Steps:**

- [ ] **Step 1: Create the worktree and branch**

```bash
cd /Users/davidsmith/code/OpenAC/OpenAC
git worktree add .worktrees/flex-markup -b flex/markup main
cd .worktrees/flex-markup
export DOTNET_ROOT=$HOME/.dotnet PATH=$HOME/.dotnet:$PATH
```

- [ ] **Step 2: Write the failing tests**

`tests/AcDream.App.Tests/UI/MarkupFlexAttributesTests.cs`:

```csharp
using System.Xml.Linq;
using AcDream.App.UI;
using AcDream.App.UI.Layout.Flex;

namespace AcDream.App.Tests.UI;

/// <summary>
/// Reading flex attributes from markup, and every build error of spec
/// section 5: unknown keywords, container attributes without layout, item
/// attributes under an absolute container, placement on a flex item,
/// negative and non-finite numbers, and inverted limits.
/// </summary>
public sealed class MarkupFlexAttributesTests
{
    private static XElement El(string xml) => XElement.Parse(xml);

    private static FlexNode Container(string attrs)
    {
        var node = new FlexNode();
        MarkupFlexAttributes.ReadContainer(El($"<group {attrs} />"), node);
        return node;
    }

    private static FlexNode Item(string attrs)
    {
        var node = new FlexNode();
        MarkupFlexAttributes.ReadItem(El($"<label {attrs} />"), node);
        return node;
    }

    [Fact]
    public void Container_defaults_are_css_defaults()
    {
        FlexNode node = Container("layout=\"column\"");

        Assert.Equal(FlexDirection.Column, node.Direction);
        Assert.Equal(0f, node.Gap);
        Assert.Equal(default, node.Padding);
        Assert.Equal(FlexJustify.Start, node.Justify);
        Assert.Equal(FlexAlign.Stretch, node.Align);
        Assert.False(node.Wrap);
    }

    [Fact]
    public void Container_attributes_are_read()
    {
        FlexNode node = Container("layout=\"row\" gap=\"6\" justify=\"space-between\" align=\"center\" wrap=\"true\"");

        Assert.Equal(FlexDirection.Row, node.Direction);
        Assert.Equal(6f, node.Gap);
        Assert.Equal(FlexJustify.SpaceBetween, node.Justify);
        Assert.Equal(FlexAlign.Center, node.Align);
        Assert.True(node.Wrap);
    }

    [Theory]
    [InlineData("8", 8f, 8f, 8f, 8f)]
    [InlineData("4 10", 4f, 10f, 4f, 10f)]
    [InlineData("1 2 3 4", 1f, 2f, 3f, 4f)]
    [InlineData("1,2,3,4", 1f, 2f, 3f, 4f)]
    public void Padding_takes_one_two_or_four_numbers_in_css_order(
        string padding, float top, float right, float bottom, float left)
    {
        Assert.Equal(new FlexEdges(top, right, bottom, left), Container($"layout=\"row\" padding=\"{padding}\"").Padding);
    }

    [Fact]
    public void Item_defaults_are_css_defaults()
    {
        FlexNode node = Item("");

        Assert.Equal(0f, node.Grow);
        Assert.Equal(1f, node.Shrink);
        Assert.Null(node.Basis);
        Assert.Null(node.Width);
        Assert.Null(node.Height);
        Assert.Null(node.MinWidth);
        Assert.Null(node.MaxWidth);
        Assert.Null(node.MinHeight);
        Assert.Null(node.MaxHeight);
        Assert.Null(node.AlignSelf);
    }

    [Fact]
    public void Item_attributes_are_read()
    {
        FlexNode node = Item(
            "grow=\"2\" shrink=\"0\" basis=\"40\" w=\"50\" h=\"20\" minw=\"10\" maxw=\"90\" minh=\"5\" maxh=\"30\" alignself=\"end\"");

        Assert.Equal(2f, node.Grow);
        Assert.Equal(0f, node.Shrink);
        Assert.Equal(40f, node.Basis);
        Assert.Equal(50f, node.Width);
        Assert.Equal(20f, node.Height);
        Assert.Equal(10f, node.MinWidth);
        Assert.Equal(90f, node.MaxWidth);
        Assert.Equal(5f, node.MinHeight);
        Assert.Equal(30f, node.MaxHeight);
        Assert.Equal(FlexAlign.End, node.AlignSelf);
    }

    [Fact]
    public void Basis_auto_is_the_default()
    {
        Assert.Null(Item("basis=\"auto\"").Basis);
    }

    [Theory]
    [InlineData("<group layout=\"grid\" />", "layout=\"grid\"")]
    [InlineData("<group layout=\"row\" justify=\"around\" />", "justify=\"around\"")]
    [InlineData("<group layout=\"row\" align=\"baseline\" />", "align=\"baseline\"")]
    [InlineData("<group layout=\"row\" wrap=\"yes\" />", "wrap=\"yes\"")]
    [InlineData("<group layout=\"column\" wrap=\"true\" />", "wrap=\"true\"")]
    [InlineData("<group layout=\"row\" gap=\"-1\" />", "gap=\"-1\"")]
    [InlineData("<group layout=\"row\" gap=\"NaN\" />", "gap=\"NaN\"")]
    [InlineData("<group layout=\"row\" gap=\"Infinity\" />", "gap=\"Infinity\"")]
    [InlineData("<group layout=\"row\" gap=\"wide\" />", "gap=\"wide\"")]
    [InlineData("<group layout=\"row\" padding=\"1 2 3\" />", "padding=\"1 2 3\"")]
    [InlineData("<group layout=\"row\" padding=\"1 x\" />", "padding=\"1 x\"")]
    [InlineData("<group layout=\"row\" padding=\"-2\" />", "padding=\"-2\"")]
    [InlineData("<group layout=\"row\" padding=\"NaN\" />", "padding=\"NaN\"")]
    public void Bad_container_values_name_the_attribute_and_value(string xml, string expected)
    {
        XElement el = El(xml);
        var ex = Assert.Throws<FormatException>(() =>
        {
            if (MarkupFlexAttributes.IsContainer(el)) MarkupFlexAttributes.ReadContainer(el, new FlexNode());
        });
        Assert.Contains("<group>", ex.Message);
        Assert.Contains(expected, ex.Message);
    }

    [Theory]
    [InlineData("gap=\"4\"")]
    [InlineData("padding=\"4\"")]
    [InlineData("justify=\"end\"")]
    [InlineData("align=\"center\"")]
    [InlineData("wrap=\"true\"")]
    public void Container_attributes_without_layout_are_errors(string attr)
    {
        var ex = Assert.Throws<FormatException>(() => MarkupFlexAttributes.RejectContainer(El($"<group name=\"g\" {attr} />")));
        Assert.Contains("<group name=\"g\">", ex.Message);
        Assert.Contains(attr, ex.Message);
        Assert.Contains("layout", ex.Message);
    }

    [Theory]
    [InlineData("grow=\"1\"")]
    [InlineData("shrink=\"0\"")]
    [InlineData("basis=\"20\"")]
    [InlineData("alignself=\"end\"")]
    [InlineData("minw=\"5\"")]
    [InlineData("maxw=\"5\"")]
    [InlineData("minh=\"5\"")]
    [InlineData("maxh=\"5\"")]
    public void Item_attributes_under_an_absolute_container_are_errors(string attr)
    {
        var ex = Assert.Throws<FormatException>(() => MarkupFlexAttributes.RejectItem(El($"<label {attr} />")));
        Assert.Contains(attr, ex.Message);
    }

    [Theory]
    [InlineData("x=\"4\"")]
    [InlineData("y=\"4\"")]
    [InlineData("anchor=\"right\"")]
    public void Placement_on_a_flex_item_is_an_error(string attr)
    {
        var ex = Assert.Throws<FormatException>(() => Item(attr));
        Assert.Contains(attr, ex.Message);
        Assert.Contains("flex item", ex.Message);
    }

    [Theory]
    [InlineData("grow=\"-1\"")]
    [InlineData("shrink=\"-0.5\"")]
    [InlineData("basis=\"-3\"")]
    [InlineData("w=\"-3\"")]
    [InlineData("grow=\"NaN\"")]
    [InlineData("shrink=\"Infinity\"")]
    [InlineData("basis=\"-Infinity\"")]
    [InlineData("w=\"NaN\"")]
    [InlineData("h=\"Infinity\"")]
    [InlineData("minw=\"NaN\"")]
    [InlineData("maxw=\"Infinity\"")]
    [InlineData("minh=\"NaN\"")]
    [InlineData("maxh=\"NaN\"")]
    [InlineData("alignself=\"middle\"")]
    [InlineData("basis=\"big\"")]
    public void Bad_item_values_name_the_attribute_and_value(string attr)
    {
        var ex = Assert.Throws<FormatException>(() => Item(attr));
        Assert.Contains("<label>", ex.Message);
        Assert.Contains(attr, ex.Message);
    }

    [Theory]
    [InlineData("minw=\"50\" maxw=\"40\"", "minw=\"50\" is greater than maxw=\"40\"")]
    [InlineData("minh=\"9\" maxh=\"8.5\"", "minh=\"9\" is greater than maxh=\"8.5\"")]
    public void A_minimum_above_its_maximum_is_an_error(string attrs, string expected)
    {
        var ex = Assert.Throws<FormatException>(() => Item(attrs));
        Assert.Contains(expected, ex.Message);
    }
}
```

- [ ] **Step 3: Run them to see them fail**

Run: `dotnet test tests/AcDream.App.Tests --filter "FullyQualifiedName~MarkupFlexAttributesTests"`
Expected: build error, `MarkupFlexAttributes` does not exist.

- [ ] **Step 4: Implement**

`src/AcDream.App/UI/MarkupFlexAttributes.cs`:

```csharp
using System.Globalization;
using System.Xml.Linq;
using AcDream.App.UI.Layout.Flex;

namespace AcDream.App.UI;

/// <summary>
/// Reads the flex attributes of plugin markup onto <see cref="FlexNode"/>s and
/// rejects every misuse at build time: container attributes on an element
/// without <c>layout</c>, item attributes under an absolute container,
/// placement attributes on a flex item, unknown keywords, negative or
/// non-finite numbers and inverted limits.
/// </summary>
internal static class MarkupFlexAttributes
{
    private static readonly string[] ContainerAttributes = ["gap", "padding", "justify", "align", "wrap"];
    private static readonly string[] ItemOnlyAttributes = ["grow", "shrink", "basis", "alignself"];
    private static readonly string[] LimitAttributes = ["minw", "maxw", "minh", "maxh"];
    private static readonly string[] PlacementAttributes = ["x", "y", "anchor"];

    /// <summary>Whether <paramref name="el"/> is a flex container; throws on an unknown <c>layout</c>.</summary>
    internal static bool IsContainer(XElement el) => (string?)el.Attribute("layout") switch
    {
        null => false,
        "row" or "column" => true,
        string other => throw Error(el, "layout", other, "must be row or column"),
    };

    /// <summary>Rejects container attributes on <paramref name="el"/>, which has no <c>layout</c>.</summary>
    internal static void RejectContainer(XElement el)
    {
        foreach (string name in ContainerAttributes)
            if (el.Attribute(name) is { } a)
                throw Error(el, name, a.Value, "needs layout=\"row\" or layout=\"column\" on the same element");
    }

    /// <summary>Reads the container attributes of <paramref name="el"/>, which has <c>layout</c>, onto <paramref name="node"/>.</summary>
    internal static void ReadContainer(XElement el, FlexNode node)
    {
        node.Direction = (string?)el.Attribute("layout") == "row" ? FlexDirection.Row : FlexDirection.Column;
        node.Gap = NonNegative(el, "gap") ?? 0f;
        node.Padding = Padding(el);
        node.Justify = (string?)el.Attribute("justify") switch
        {
            null or "start" => FlexJustify.Start,
            "center" => FlexJustify.Center,
            "end" => FlexJustify.End,
            "space-between" => FlexJustify.SpaceBetween,
            string other => throw Error(el, "justify", other, "must be start, center, end or space-between"),
        };
        node.Align = Align(el, "align") ?? FlexAlign.Stretch;
        node.Wrap = (string?)el.Attribute("wrap") switch
        {
            null or "false" => false,
            "true" => true,
            string other => throw Error(el, "wrap", other, "must be true or false"),
        };
        if (node.Wrap && node.Direction == FlexDirection.Column)
            throw Error(el, "wrap", "true", "is not supported on a column (only rows wrap in this version)");
    }

    /// <summary>
    /// Reads the item attributes of <paramref name="el"/>, a child of a flex
    /// container, onto <paramref name="node"/>. <c>x</c>, <c>y</c> and
    /// <c>anchor</c> are errors: the container places its items.
    /// </summary>
    internal static void ReadItem(XElement el, FlexNode node)
    {
        foreach (string name in PlacementAttributes)
            if (el.Attribute(name) is { } a)
                throw Error(el, name, a.Value, "cannot be used on a flex item; its container places it");

        node.Grow = NonNegative(el, "grow") ?? 0f;
        node.Shrink = NonNegative(el, "shrink") ?? 1f;
        node.Basis = (string?)el.Attribute("basis") is "auto" ? null : NonNegative(el, "basis");
        node.Width = NonNegative(el, "w");
        node.Height = NonNegative(el, "h");
        node.MinWidth = NonNegative(el, "minw");
        node.MaxWidth = NonNegative(el, "maxw");
        node.MinHeight = NonNegative(el, "minh");
        node.MaxHeight = NonNegative(el, "maxh");
        node.AlignSelf = Align(el, "alignself");
        CheckLimits(el, "minw", node.MinWidth, "maxw", node.MaxWidth);
        CheckLimits(el, "minh", node.MinHeight, "maxh", node.MaxHeight);
    }

    /// <summary>Rejects item attributes on <paramref name="el"/>, a child of an absolute container.</summary>
    internal static void RejectItem(XElement el)
    {
        foreach (string name in ItemOnlyAttributes.Concat(LimitAttributes))
            if (el.Attribute(name) is { } a)
                throw Error(el, name, a.Value, "only applies to a child of a layout=\"row\" or layout=\"column\" container");
    }

    /// <summary>
    /// A root panel's authored content-area size or limit, checked like a
    /// flex number: null when absent.
    /// </summary>
    internal static float? RootSize(XElement root, string name) => NonNegative(root, name);

    private static FlexAlign? Align(XElement el, string name) => (string?)el.Attribute(name) switch
    {
        null => null,
        "start" => FlexAlign.Start,
        "center" => FlexAlign.Center,
        "end" => FlexAlign.End,
        "stretch" => FlexAlign.Stretch,
        string other => throw Error(el, name, other, "must be start, center, end or stretch"),
    };

    private static FlexEdges Padding(XElement el)
    {
        if ((string?)el.Attribute("padding") is not { } raw) return default;
        string[] parts = raw.Split([' ', ',', '\t'], StringSplitOptions.RemoveEmptyEntries);
        var values = new float[parts.Length];
        for (int i = 0; i < parts.Length; i++)
            values[i] = Number(el, "padding", parts[i], raw);
        foreach (float v in values)
            if (v < 0f) throw Error(el, "padding", raw, "must not be negative");
        return values.Length switch
        {
            1 => FlexEdges.All(values[0]),
            2 => new FlexEdges(values[0], values[1], values[0], values[1]),
            4 => new FlexEdges(values[0], values[1], values[2], values[3]),
            _ => throw Error(el, "padding", raw, "must be 1, 2 or 4 numbers (all; vertical horizontal; top right bottom left)"),
        };
    }

    private static float? NonNegative(XElement el, string name)
    {
        if ((string?)el.Attribute(name) is not { } raw) return null;
        float value = Number(el, name, raw, raw);
        if (value < 0f) throw Error(el, name, raw, "must not be negative");
        return value;
    }

    private static float Number(XElement el, string name, string text, string raw)
    {
        if (!float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out float value))
            throw Error(el, name, raw, "is not a number");
        if (!float.IsFinite(value))
            throw Error(el, name, raw, "must be a finite number");
        return value;
    }

    private static void CheckLimits(XElement el, string minName, float? min, string maxName, float? max)
    {
        if (min is { } lo && max is { } hi && lo > hi)
            throw new FormatException(
                $"{Identity(el)} {minName}=\"{Format(lo)}\" is greater than {maxName}=\"{Format(hi)}\"");
    }

    private static FormatException Error(XElement el, string name, string value, string problem) =>
        new($"{Identity(el)} {name}=\"{value}\" {problem}");

    private static string Format(float value) => value.ToString(CultureInfo.InvariantCulture);

    private static string Identity(XElement el)
    {
        string? name = (string?)el.Attribute("name") ?? (string?)el.Attribute("id");
        return name is null ? $"<{el.Name.LocalName}>" : $"<{el.Name.LocalName} name=\"{name}\">";
    }
}
```

- [ ] **Step 5: Run the tests**

Run: `dotnet test tests/AcDream.App.Tests --filter "FullyQualifiedName~MarkupFlexAttributesTests"`
Expected: 55 passed.

- [ ] **Step 6: Commit**

```bash
git checkout -- '*.lock.json'
git add src/AcDream.App/UI/MarkupFlexAttributes.cs tests/AcDream.App.Tests/UI/MarkupFlexAttributesTests.cs
git commit -m "feat(ui): read and validate markup flex attributes

Claude-Session: https://claude.ai/code/session_019dvJ5gakBkq3uJyr1PAC3u"
```

---

### Task 2: Fitted minimum (`FlexFit`)

**Goal:** `FlexFit.Minimum(root)` returns the smallest whole-point size, starting from the measured minimum, at which the tree's laid-out content does not reach past the box on any non-scrolling axis.

**Files:**
- Create: `src/AcDream.App/UI/Layout/Flex/FlexFit.cs`
- Test: `tests/AcDream.App.Tests/UI/Layout/Flex/FlexFitTests.cs`

**Acceptance Criteria:**
- [ ] A plain padded column's fitted minimum equals `FlexLayout.Measure(root).Minimum` (60 × 64 in the test).
- [ ] A wrapping row whose items start at a `basis` above their minimum measures 60 × 20 but fits only at 60 × 30, and `FlexFit.Minimum` returns 60 × 30.
- [ ] A scrolling axis is never grown (a `ScrollY` column stays at the 40-point viewport, width 50 + 16).
- [ ] Fractional minimums round up (10.25 + 10.25 wide, 7.5 tall → 21 × 8).

**Verify:** `dotnet test tests/AcDream.App.Tests --filter "FullyQualifiedName~FlexFitTests|FullyQualifiedName~Layout.Flex"` → all pass (the PR 1 engine suites unchanged).

**Steps:**

- [ ] **Step 1: Write the failing tests**

`tests/AcDream.App.Tests/UI/Layout/Flex/FlexFitTests.cs`:

```csharp
using AcDream.App.UI.Layout.Flex;
using static AcDream.App.Tests.UI.Layout.Flex.FlexTestNodes;

namespace AcDream.App.Tests.UI.Layout.Flex;

/// <summary>
/// The fitted minimum: the measured minimum, grown until the content laid out
/// at it no longer reaches past it. Exact where the measurement is; larger
/// where wrapped lines break on preferred sizes.
/// </summary>
public sealed class FlexFitTests
{
    [Fact]
    public void A_plain_column_fits_its_measured_minimum()
    {
        FlexNode root = Column(Leaf(80f, 20f, minWidth: 30f), Leaf(50f, 30f));
        root.Gap = 4f;
        root.Padding = FlexEdges.All(5f);

        Assert.Equal(FlexLayout.Measure(root).Minimum, FlexFit.Minimum(root));
        Assert.Equal(new FlexSize(60f, 64f), FlexFit.Minimum(root));
    }

    [Fact]
    public void Wrapped_lines_that_break_on_a_basis_grow_the_minimum()
    {
        // At its narrowest (60, the widest item) the measured minimum breaks
        // lines on the items' minimums: A and B (10 each) share a line, C
        // takes one, 20 tall. Laid out, A and B start at their basis (50) and
        // cannot share 60, so there are three lines: 30 tall.
        FlexNode a = Leaf(10f, 10f), b = Leaf(10f, 10f), c = Leaf(60f, 10f);
        a.Basis = 50f;
        b.Basis = 50f;
        FlexNode root = Row(a, b, c);
        root.Wrap = true;

        Assert.Equal(new FlexSize(60f, 20f), FlexLayout.Measure(root).Minimum);
        Assert.Equal(new FlexSize(60f, 30f), FlexFit.Minimum(root));
    }

    [Fact]
    public void A_scrolling_axis_is_never_grown()
    {
        FlexNode root = Column(Leaf(50f, 100f), Leaf(50f, 100f));
        root.ScrollY = true;

        FlexSize fitted = FlexFit.Minimum(root);

        Assert.Equal(FlexLayout.MinimumScrollViewport, fitted.Height);
        Assert.Equal(50f + FlexLayout.ScrollbarThickness, fitted.Width);
    }

    [Fact]
    public void Fractional_minimums_round_up_to_whole_points()
    {
        FlexNode root = Row(Leaf(10.25f, 7.5f), Leaf(10.25f, 7.5f));

        Assert.Equal(new FlexSize(21f, 8f), FlexFit.Minimum(root));
    }
}
```

- [ ] **Step 2: Run them to see them fail**

Run: `dotnet test tests/AcDream.App.Tests --filter "FullyQualifiedName~FlexFitTests"`
Expected: build error, `FlexFit` does not exist.

- [ ] **Step 3: Implement**

`src/AcDream.App/UI/Layout/Flex/FlexFit.cs`:

```csharp
using System;

namespace AcDream.App.UI.Layout.Flex;

/// <summary>
/// The smallest box a flex tree really fits in. <see cref="FlexLayout.Measure"/>
/// gives a minimum built from the items' own minimums, which is exact for
/// plain rows and columns but only an estimate once lines wrap (lines break
/// on the items' preferred sizes, and greedy breaking is not monotonic).
/// This lays the tree out at that estimate and grows each non-scrolling axis
/// until the content no longer reaches past the box, so a window held at its
/// minimum never clips its own content.
/// </summary>
public static class FlexFit
{
    /// <summary>Layouts tried before giving up on growing further; each settles one dependent axis.</summary>
    private const int MaxPasses = 4;

    /// <summary>
    /// The smallest whole-point size, starting from the measured minimum, at
    /// which <paramref name="root"/>'s content fits. The tree is left arranged
    /// at the last size tried; arrange it again before reading its rects.
    /// </summary>
    public static FlexSize Minimum(FlexNode root)
    {
        FlexSize measured = FlexLayout.Measure(root).Minimum;
        float width = MathF.Ceiling(measured.Width), height = MathF.Ceiling(measured.Height);
        for (int pass = 0; pass < MaxPasses; pass++)
        {
            FlexLayout.Arrange(root, new FlexRect(0f, 0f, width, height));
            float needWidth = root.ScrollX ? width : MathF.Ceiling(root.ContentSize.Width);
            float needHeight = root.ScrollY ? height : MathF.Ceiling(root.ContentSize.Height);
            if (needWidth <= width && needHeight <= height) break;
            width = MathF.Max(width, needWidth);
            height = MathF.Max(height, needHeight);
        }
        return new FlexSize(width, height);
    }
}
```

- [ ] **Step 4: Run the tests**

Run: `dotnet test tests/AcDream.App.Tests --filter "FullyQualifiedName~Layout.Flex"`
Expected: all pass, including the 4 new ones.

- [ ] **Step 5: Commit**

```bash
git checkout -- '*.lock.json'
git add src/AcDream.App/UI/Layout/Flex/FlexFit.cs tests/AcDream.App.Tests/UI/Layout/Flex/FlexFitTests.cs
git commit -m "feat(ui): fitted flex minimum that never clips its content

Claude-Session: https://claude.ai/code/session_019dvJ5gakBkq3uJyr1PAC3u"
```

---

### Task 3: Content sizes

**Goal:** `MarkupContentSize` gives every markup control's preferred and minimum size (spec 3.2, with the constants in Global Constraints) and the caption, font and theme flag its size depends on.

**Files:**
- Create: `src/AcDream.App/UI/MarkupContentSize.cs`
- Test: `tests/AcDream.App.Tests/UI/MarkupContentSizeTests.cs`

**Acceptance Criteria:**
- [ ] Label "Hello" with no font is 35 × 14; "ABC" in a 16-point font with 6-point glyphs is 18 × 16; a bound label measures its current text.
- [ ] Button and tab "OK": 38 × 24; with an icon 62 × 24; icon only 24 × 24.
- [ ] Control height is 24 for a 16-point line and 28 for a 20-point line.
- [ ] Toggle "Auto": 45 × 20 in Classic, 63 × 20 themed.
- [ ] Icon 32 × 32; field and menu 120 × 24 (min 40); slider 120 × 16; meter 120 × 12 (min 40); list and log 160 × 80 (min 60 × 40); a plain `UiPanel` returns null.

**Verify:** `dotnet test tests/AcDream.App.Tests --filter "FullyQualifiedName~MarkupContentSizeTests"` → 8 passed.

**Steps:**

- [ ] **Step 1: Write the failing tests**

`tests/AcDream.App.Tests/UI/MarkupContentSizeTests.cs`:

```csharp
using AcDream.App.UI;
using AcDream.App.UI.Layout.Flex;
using DatReaderWriter.Types;

namespace AcDream.App.Tests.UI;

/// <summary>
/// Content sizes of spec section 3.2, with the headless fallback metrics
/// (7 points a character, 14-point lines) and with a dat font.
/// </summary>
public sealed class MarkupContentSizeTests
{
    private static UiDatFont Font(float lineHeight)
    {
        var glyphs = new Dictionary<char, FontCharDesc>();
        foreach (char c in "ABCDEFGH")
            glyphs[c] = new FontCharDesc { Unicode = c, Width = 6, Height = 8 };
        return new UiDatFont(
            fgTex: 1, fgW: 32, fgH: 32, bgTex: 0, bgW: 0, bgH: 0,
            lineHeight: lineHeight, baselineOffset: 12f, glyphs);
    }

    private static FlexMeasurement Measure(UiElement element) =>
        MarkupContentSize.Measure(element, MarkupContentSize.Text(element))!.Value;

    private static FlexMeasurement Size(float w, float h, float minW, float minH) =>
        new(new FlexSize(w, h), new FlexSize(minW, minH));

    [Fact]
    public void A_label_is_its_text_by_its_line()
    {
        Assert.Equal(Size(35f, 14f, 35f, 14f), Measure(new UiLabel { Text = "Hello" }));
        Assert.Equal(Size(18f, 16f, 18f, 16f), Measure(new UiLabel { Text = "ABC", DatFont = Font(16f) }));
    }

    [Fact]
    public void A_bound_label_measures_what_it_shows_now()
    {
        string shown = "AB";
        var label = new UiLabel { Text = "ignored", TextSource = () => shown };

        Assert.Equal(14f, Measure(label).Preferred.Width);
        shown = "ABCD";
        Assert.Equal(28f, Measure(label).Preferred.Width);
    }

    [Fact]
    public void A_button_is_its_caption_padded_at_control_height()
    {
        Assert.Equal(Size(38f, 24f, 38f, 24f), Measure(new UiSimpleButton { Text = "OK" }));
        Assert.Equal(Size(38f, 24f, 38f, 24f), Measure(new UiMarkupTabButton { Text = "OK" }));
    }

    [Fact]
    public void A_button_icon_adds_a_square_column()
    {
        var button = new UiSimpleButton { Text = "OK", IconSource = () => (0u, 0, 0) };
        Assert.Equal(Size(62f, 24f, 62f, 24f), Measure(button));

        var iconOnly = new UiSimpleButton { IconSource = () => (0u, 0, 0) };
        Assert.Equal(Size(24f, 24f, 24f, 24f), Measure(iconOnly));
    }

    [Fact]
    public void A_tall_font_makes_controls_taller()
    {
        Assert.Equal(24f, MarkupContentSize.ControlHeightFor(Font(16f)));
        Assert.Equal(28f, MarkupContentSize.ControlHeightFor(Font(20f)));
        Assert.Equal(28f, Measure(new UiField { DatFont = Font(20f) }).Preferred.Height);
    }

    [Fact]
    public void A_toggle_starts_its_caption_after_its_lamp_or_switch()
    {
        Assert.Equal(Size(45f, 20f, 45f, 20f), Measure(new UiMarkupToggle { Text = "Auto" }));
        var themed = new UiMarkupToggle { Text = "Auto", ThemePalette = PluginUiPalette.Moss };
        Assert.Equal(Size(63f, 20f, 63f, 20f), Measure(themed));
    }

    [Fact]
    public void Fixed_and_stretchy_controls_use_the_defaults()
    {
        Assert.Equal(Size(32f, 32f, 32f, 32f), Measure(new UiMarkupIcon()));
        Assert.Equal(Size(120f, 24f, 40f, 24f), Measure(new UiField()));
        Assert.Equal(Size(120f, 24f, 40f, 24f), Measure(new UiMenu()));
        Assert.Equal(Size(120f, 16f, 40f, 16f), Measure(new UiScrollbar()));
        Assert.Equal(Size(120f, 12f, 40f, 12f), Measure(new UiMeter()));
        Assert.Equal(Size(160f, 80f, 60f, 40f), Measure(new UiMarkupList()));
        Assert.Equal(Size(160f, 80f, 60f, 40f), Measure(new UiMarkupLog(_ => (0u, 0, 0))));
    }

    [Fact]
    public void Groups_are_not_sized_here()
    {
        Assert.Null(MarkupContentSize.Measure(new UiPanel(), string.Empty));
    }
}
```

- [ ] **Step 2: Run them to see them fail**

Run: `dotnet test tests/AcDream.App.Tests --filter "FullyQualifiedName~MarkupContentSizeTests"`
Expected: build error, `MarkupContentSize` does not exist.

- [ ] **Step 3: Implement**

`src/AcDream.App/UI/MarkupContentSize.cs`:

```csharp
using AcDream.App.UI.Layout.Flex;

namespace AcDream.App.UI;

/// <summary>
/// How big each markup control would like to be inside a flex container,
/// and how small it can go: the content sizes of spec section 3.2. Every
/// default lives here so the look can be tuned in one place. Sizes are in
/// points and come from the control's current font, so a theme switch that
/// changes the font changes them.
/// </summary>
internal static class MarkupContentSize
{
    /// <summary>Text width per character when a control has no dat font (headless only).</summary>
    internal const float FallbackCharWidth = 7f;

    /// <summary>Line height when a control has no dat font (headless only).</summary>
    internal const float FallbackLineHeight = 14f;

    /// <summary>The smallest height of a button, tab, field or menu.</summary>
    internal const float ControlHeight = 24f;

    /// <summary>Room above and below a control's text, together.</summary>
    internal const float ControlTextPadding = 8f;

    /// <summary>Room left and right of a button's caption, each side.</summary>
    internal const float ButtonPaddingX = 12f;

    /// <summary>The smallest height of a toggle.</summary>
    internal const float ToggleHeight = 20f;

    /// <summary>Where a Classic toggle's caption starts: lamp and gap.</summary>
    internal const float ClassicToggleCaptionX = 17f;

    /// <summary>Where a themed toggle's caption starts: switch and gap.</summary>
    internal const float ThemedToggleCaptionX = 1f + PluginUiStyle.SwitchWidth + PluginUiStyle.SwitchCaptionGap;

    internal const float IconSize = 32f;

    internal const float FieldWidth = 120f;
    internal const float FieldMinWidth = 40f;

    internal const float SliderHeight = 16f;
    internal const float MeterHeight = 12f;

    internal const float ListWidth = 160f;
    internal const float ListHeight = 80f;
    internal const float ListMinWidth = 60f;
    internal const float ListMinHeight = 40f;

    /// <summary>The width of <paramref name="text"/> in <paramref name="font"/>.</summary>
    internal static float TextWidth(UiDatFont? font, string text) =>
        font?.MeasureWidth(text) ?? text.Length * FallbackCharWidth;

    internal static float LineHeight(UiDatFont? font) => font?.LineHeight ?? FallbackLineHeight;

    /// <summary>The height of a button, tab, field or menu set in <paramref name="font"/>.</summary>
    internal static float ControlHeightFor(UiDatFont? font) =>
        MathF.Max(ControlHeight, MathF.Ceiling(LineHeight(font) + ControlTextPadding));

    /// <summary>
    /// The content size of a markup control, or null for an element this
    /// class does not size (groups are sized by their own layout or their
    /// authored size). <paramref name="text"/> is the caption the control
    /// shows now, already resolved.
    /// </summary>
    internal static FlexMeasurement? Measure(UiElement element, string text)
    {
        switch (element)
        {
            case UiLabel label:
                return Fixed(MathF.Ceiling(TextWidth(label.DatFont, text)), LineHeight(label.DatFont));
            case UiSimpleButton button:
            {
                float height = ControlHeightFor(button.DatFont);
                // A button with an icon keeps a square column for it on the left
                // (UiSimpleButton.OnDraw), and centres its caption in the rest.
                float icon = button.IconSource is not null ? height : 0f;
                float caption = text.Length == 0 ? 0f : MathF.Ceiling(TextWidth(button.DatFont, text)) + 2f * ButtonPaddingX;
                return Fixed(MathF.Max(icon + caption, height), height);
            }
            case UiMarkupToggle toggle:
            {
                float captionX = toggle.ThemePalette is null ? ClassicToggleCaptionX : ThemedToggleCaptionX;
                float height = MathF.Max(ToggleHeight, MathF.Ceiling(LineHeight(toggle.DatFont) + 4f));
                return Fixed(captionX + MathF.Ceiling(TextWidth(toggle.DatFont, text)), height);
            }
            case UiMarkupIcon:
                return Fixed(IconSize, IconSize);
            case UiField field:
                return Range(FieldWidth, FieldMinWidth, ControlHeightFor(field.DatFont));
            case UiMenu menu:
                return Range(FieldWidth, FieldMinWidth, ControlHeightFor(menu.ButtonDatFont ?? menu.DatFont));
            case UiScrollbar:
                return Range(FieldWidth, FieldMinWidth, SliderHeight);
            case UiMeter:
                return Range(FieldWidth, FieldMinWidth, MeterHeight);
            case UiMarkupList or UiMarkupLog:
                return new FlexMeasurement(new FlexSize(ListWidth, ListHeight), new FlexSize(ListMinWidth, ListMinHeight));
            default:
                return null;
        }
    }

    /// <summary>The caption a control shows now: what its size depends on besides its font.</summary>
    internal static string Text(UiElement element) => element switch
    {
        UiLabel label => label.TextSource?.Invoke() ?? label.Text,
        UiSimpleButton button => button.TextSource?.Invoke() ?? button.Text,
        UiMarkupToggle toggle => toggle.TextSource?.Invoke() ?? toggle.Text,
        _ => string.Empty,
    };

    /// <summary>The font a control's size is measured in, or null when its size does not depend on one.</summary>
    internal static UiDatFont? Font(UiElement element) => element switch
    {
        UiLabel label => label.DatFont,
        UiSimpleButton button => button.DatFont,
        UiMarkupToggle toggle => toggle.DatFont,
        UiField field => field.DatFont,
        UiMenu menu => menu.ButtonDatFont ?? menu.DatFont,
        _ => null,
    };

    /// <summary>Whether a control's size depends on the theme beyond its font.</summary>
    internal static bool Themed(UiElement element) => element is UiMarkupToggle { ThemePalette: not null };

    private static FlexMeasurement Fixed(float width, float height) =>
        new(new FlexSize(width, height), new FlexSize(width, height));

    private static FlexMeasurement Range(float width, float minWidth, float height) =>
        new(new FlexSize(width, height), new FlexSize(minWidth, height));
}
```

- [ ] **Step 4: Run the tests**

Run: `dotnet test tests/AcDream.App.Tests --filter "FullyQualifiedName~MarkupContentSizeTests"`
Expected: 8 passed.

- [ ] **Step 5: Commit**

```bash
git checkout -- '*.lock.json'
git add src/AcDream.App/UI/MarkupContentSize.cs tests/AcDream.App.Tests/UI/MarkupContentSizeTests.cs
git commit -m "feat(ui): content sizes for markup controls in flex layout

Claude-Session: https://claude.ai/code/session_019dvJ5gakBkq3uJyr1PAC3u"
```

---

### Task 4: Flex groups

**Goal:** `<group layout="row|column">` lays out its children through one flex tree per flex root, in a new `UiElement.LayoutChildren` hook. It reflows when its size, a child's visibility, or a leaf's caption, font or theme changes. Item attributes under absolute containers fail the build.

**Files:**
- Create: `src/AcDream.App/UI/UiFlexBox.cs`
- Create: `src/AcDream.App/UI/UiFlexGroup.cs`
- Modify: `src/AcDream.App/UI/UiElement.cs` (`DrawSelfAndChildren`, ~line 439; new method before `HitTest`)
- Modify: `src/AcDream.App/UI/UiPanel.cs` (`UiLabel`, ~lines 62–85)
- Modify: `src/AcDream.App/UI/MarkupDocument.cs` (`AddElement` signature, `group` case, end of `AddElement`, new `AddFlexItem`)
- Test: `tests/AcDream.App.Tests/UI/MarkupFlexTests.cs`

**Acceptance Criteria:**
- [ ] A row group with `gap="4" padding="5"` puts a label at (5, 5, 14, 30) and a button at (23, 5, 38, 30).
- [ ] Toolbar/list/footer column: `grow` fills (search 258 wide, list 152 tall) and `justify="end"` packs OK and Cancel to (192, 0) and (234, 0).
- [ ] A nested flex group is an inner node (`IsRoot` false) laid out by its root with height-for-width (a wrapping grid capped at 100 wide is 68 tall).
- [ ] An absolute group in flex is its authored size, resized by flex, its children following their anchors; a flex group in an absolute group is placed by its coordinates.
- [ ] Hidden items take no space and showing one reflows; a longer bound caption reflows; a theme switch (Classic → Moss) reflows a toggle (45 → 63).
- [ ] A label in flex keeps its given rect (`SizedByLayout`) and draws its line centred (7 points down in a 30-point row with a 16-point line); a label outside flex still sizes itself.
- [ ] Item attributes under an absolute group, and `x` on a flex item, fail the build.
- [ ] Two draws with nothing changed measure nothing; a wrapping grid of 50, 100 and 200 icons measures exactly that many leaves per layout; a steady-state relayout allocates 0 bytes.
- [ ] Markup without `layout` is unchanged: the suites in Global Constraints pass.

**Verify:** `dotnet test tests/AcDream.App.Tests --filter "FullyQualifiedName~MarkupFlexTests|FullyQualifiedName~MarkupDocumentTests|FullyQualifiedName~PluginThemeClassicIdentityTests|FullyQualifiedName~MarkupResizableAnchorTests|FullyQualifiedName~MarkupTitleBarTests"` → all pass (17 in `MarkupFlexTests`).

**Steps:**

- [ ] **Step 1: Write the failing tests**

`tests/AcDream.App.Tests/UI/MarkupFlexTests.cs`:

```csharp
using AcDream.App.UI;
using AcDream.App.UI.Layout.Flex;
using AcDream.App.Rendering;
using DatReaderWriter.Types;

namespace AcDream.App.Tests.UI;

/// <summary>
/// <c>&lt;group layout&gt;</c> through the markup builder and the draw path:
/// placement, nesting both ways, labels in and out of flex, reflow when an
/// item's visibility, caption or theme changes, and what a cached layout costs.
/// Headless metrics: 7 points a character, 14-point lines, 24-point controls.
/// </summary>
public sealed class MarkupFlexTests
{
    private sealed class Binding
    {
        public bool Shown { get; set; } = true;
        public string Caption { get; set; } = "AB";
        public IReadOnlyList<string> Lines { get; } = ["one", "two"];
        public int Selected => -1;
    }

    private static (UiRoot Root, UiNineSlicePanel Frame) Mount(
        string body, object? binding = null, PluginUiThemeSettings? themes = null, string panel = "w=\"300\" h=\"200\"")
    {
        UiNineSlicePanel frame = MarkupDocument.Build(
            $"<panel x=\"0\" y=\"0\" {panel}>{body}</panel>", binding ?? new object(), _ => (0u, 0, 0), themes: themes);
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

    private static UiElement Find(UiElement root, string name)
    {
        if (root.Name == name) return root;
        foreach (UiElement child in root.Children)
            if (TryFind(child, name) is { } found) return found;
        throw new InvalidOperationException($"no element named {name}");
    }

    private static UiElement? TryFind(UiElement element, string name)
    {
        if (element.Name == name) return element;
        foreach (UiElement child in element.Children)
            if (TryFind(child, name) is { } found) return found;
        return null;
    }

    private static FlexRect Rect(UiElement e) => new(e.Left, e.Top, e.Width, e.Height);

    [Fact]
    public void A_row_places_its_items_after_padding_with_gaps()
    {
        var (_, frame) = Mount("""
            <group name="g" x="10" y="10" w="200" h="40" layout="row" gap="4" padding="5">
              <label name="a" text="AB" />
              <button name="b" text="OK" />
            </group>
            """);

        Assert.IsType<UiFlexGroup>(Find(frame, "g"));
        Assert.Equal(new FlexRect(5f, 5f, 14f, 30f), Rect(Find(frame, "a")));
        Assert.Equal(new FlexRect(23f, 5f, 38f, 30f), Rect(Find(frame, "b")));
    }

    [Fact]
    public void Grow_takes_the_free_space_and_justify_end_packs_a_footer()
    {
        var (_, frame) = Mount("""
            <group x="0" y="0" w="300" h="200" layout="column">
              <group name="bar" layout="row" gap="4">
                <field name="search" grow="1" />
                <button name="go" text="Go" />
              </group>
              <list name="list" grow="1" items="{Lines}" selected="{Selected}" />
              <group name="footer" layout="row" justify="end" gap="4">
                <button name="ok" text="OK" />
                <button name="cancel" text="Cancel" />
              </group>
            </group>
            """, new Binding());

        Assert.Equal(new FlexRect(0f, 0f, 300f, 24f), Rect(Find(frame, "bar")));
        Assert.Equal(new FlexRect(0f, 0f, 258f, 24f), Rect(Find(frame, "search")));
        Assert.Equal(new FlexRect(262f, 0f, 38f, 24f), Rect(Find(frame, "go")));
        Assert.Equal(new FlexRect(0f, 24f, 300f, 152f), Rect(Find(frame, "list")));
        Assert.Equal(new FlexRect(0f, 176f, 300f, 24f), Rect(Find(frame, "footer")));
        Assert.Equal(new FlexRect(192f, 0f, 38f, 24f), Rect(Find(frame, "ok")));
        Assert.Equal(new FlexRect(234f, 0f, 66f, 24f), Rect(Find(frame, "cancel")));
    }

    [Fact]
    public void A_nested_flex_group_is_laid_out_by_its_root()
    {
        var (_, frame) = Mount("""
            <group name="root" x="0" y="0" w="100" h="200" layout="column" align="start">
              <group name="grid" layout="row" wrap="true" gap="4">
                <icon name="i0" did="1" /><icon name="i1" did="1" /><icon name="i2" did="1" />
              </group>
              <label name="after" text="A" />
            </group>
            """);

        var grid = (UiFlexGroup)Find(frame, "grid");
        Assert.False(grid.Flex.IsRoot);
        Assert.True(((UiFlexGroup)Find(frame, "root")).Flex.IsRoot);
        // Height-for-width across inner nodes: the one-line grid (104) is
        // capped at the column's 100, where two icons fit and the third wraps.
        Assert.Equal(new FlexRect(0f, 0f, 100f, 68f), Rect(grid));
        Assert.Equal(new FlexRect(0f, 36f, 32f, 32f), Rect(Find(frame, "i2")));
        Assert.Equal(new FlexRect(0f, 68f, 7f, 14f), Rect(Find(frame, "after")));
    }

    [Fact]
    public void An_absolute_group_in_flex_is_its_authored_size_and_its_children_follow_their_anchors()
    {
        var (_, frame) = Mount("""
            <group x="0" y="0" w="300" h="50" layout="row">
              <group name="abs" w="100" h="40" grow="1">
                <button name="pin" x="70" y="0" w="20" h="20" anchor="right top" />
              </group>
              <label text="AB" />
            </group>
            """);

        Assert.Equal(286f, Find(frame, "abs").Width);
        // Authored 10 points from the right edge of a 100-wide group; kept at 286.
        Assert.Equal(256f, Find(frame, "pin").Left);
    }

    [Fact]
    public void A_flex_group_in_an_absolute_container_is_placed_by_its_coordinates()
    {
        var (_, frame) = Mount("""
            <group name="abs" x="20" y="30" w="200" h="100">
              <group name="flex" x="10" y="10" w="80" h="50" anchor="left top right" layout="column">
                <button name="b" text="OK" />
              </group>
            </group>
            """);

        Assert.Equal(new FlexRect(10f, 10f, 80f, 50f), Rect(Find(frame, "flex")));
        Assert.Equal(new FlexRect(0f, 0f, 80f, 24f), Rect(Find(frame, "b")));
    }

    [Fact]
    public void Item_attributes_under_an_absolute_group_fail_the_build()
    {
        var ex = Assert.Throws<FormatException>(() => Mount("<group x=\"0\" y=\"0\" w=\"10\" h=\"10\"><label text=\"a\" grow=\"1\" /></group>"));
        Assert.Contains("grow", ex.Message);
    }

    [Fact]
    public void Coordinates_on_a_flex_item_fail_the_build()
    {
        var ex = Assert.Throws<FormatException>(() => Mount("<group x=\"0\" y=\"0\" w=\"10\" h=\"10\" layout=\"row\"><label x=\"3\" text=\"a\" /></group>"));
        Assert.Contains("x=\"3\"", ex.Message);
    }

    [Fact]
    public void A_hidden_item_takes_no_space_and_showing_it_reflows()
    {
        var binding = new Binding { Shown = false };
        var (root, frame) = Mount("""
            <group x="0" y="0" w="300" h="30" layout="row">
              <button name="first" text="OK" visible="{Shown}" />
              <button name="second" text="OK" />
            </group>
            """, binding);

        Assert.Equal(0f, Find(frame, "second").Left);

        binding.Shown = true;
        Frame(root);

        Assert.Equal(38f, Find(frame, "second").Left);
    }

    [Fact]
    public void A_longer_caption_reflows_its_row()
    {
        var binding = new Binding();
        var (root, frame) = Mount("""
            <group x="0" y="0" w="300" h="30" layout="row">
              <label name="caption" text="{Caption}" />
              <button name="after" text="OK" />
            </group>
            """, binding);

        Assert.Equal(14f, Find(frame, "after").Left);

        binding.Caption = "ABCDEF";
        Frame(root);

        Assert.Equal(42f, Find(frame, "caption").Width);
        Assert.Equal(42f, Find(frame, "after").Left);
    }

    [Fact]
    public void A_label_in_flex_keeps_the_size_it_is_given()
    {
        var (_, frame) = Mount("""
            <group x="0" y="0" w="300" h="30" layout="row">
              <label name="wide" text="AB" grow="1" />
            </group>
            <label name="loose" x="0" y="40" text="AB" />
            """);

        var wide = (UiLabel)Find(frame, "wide");
        Assert.True(wide.SizedByLayout);
        Assert.Equal(new FlexRect(0f, 0f, 300f, 30f), Rect(wide));
        // Outside flex a label still sizes itself to its text when drawn.
        Assert.False(((UiLabel)Find(frame, "loose")).SizedByLayout);
        Assert.Equal(14f, Find(frame, "loose").Width);
    }

    [Fact]
    public void A_label_in_flex_centres_its_line_in_the_height_it_is_given()
    {
        var glyphs = new Dictionary<char, FontCharDesc>
        {
            ['A'] = new FontCharDesc { Unicode = 'A', Width = 6, Height = 8 },
        };
        var font = new UiDatFont(
            fgTex: 7, fgW: 32, fgH: 32, bgTex: 0, bgW: 0, bgH: 0,
            lineHeight: 16f, baselineOffset: 12f, glyphs);

        float TextTop(string body)
        {
            UiNineSlicePanel frame = MarkupDocument.Build(
                $"<panel x=\"0\" y=\"0\" w=\"100\" h=\"100\">{body}</panel>", new object(), _ => (0u, 0, 0), datFont: font);
            var root = new UiRoot { Width = 200, Height = 200 };
            root.AddChild(frame);
            root.Tick(0, 0);
            var (renderer, ctx) = ThemeDrawCapture.Context(200, 200);
            root.Draw(ctx);
            return renderer.DebugSpriteSegmentVerts
                .Where(segment => segment.Texture == 7u)
                .SelectMany(segment => Enumerable.Range(0, segment.Verts.Count / TextRenderer.FloatsPerVertex)
                    .Select(i => segment.Verts[i * TextRenderer.FloatsPerVertex + 1]))
                .Min();
        }

        // Away from the frame's edge, so the outline drawn a point above the text is not clipped.
        float loose = TextTop("<label x=\"10\" y=\"40\" text=\"A\" />");
        float inRow = TextTop("<group x=\"10\" y=\"40\" w=\"80\" h=\"30\" layout=\"row\"><label text=\"A\" /></group>");

        Assert.Equal(7f, inRow - loose);   // (30 - 16) / 2
    }

    [Fact]
    public void A_theme_switch_reflows_items_whose_size_depends_on_it()
    {
        var themes = new PluginUiThemeSettings();
        var (root, frame) = Mount("""
            <group x="0" y="0" w="300" h="30" layout="row">
              <toggle name="t" text="Auto" checked="{Shown}" />
              <button name="after" text="OK" />
            </group>
            """, new Binding(), themes, panel: "w=\"300\" h=\"200\" theme=\"plugin\"");

        Assert.Equal(45f, Find(frame, "after").Left);

        themes.Theme = PluginUiTheme.Moss;
        Frame(root);

        Assert.Equal(63f, Find(frame, "after").Left);
    }

    [Fact]
    public void A_layout_with_nothing_changed_measures_nothing()
    {
        var (root, frame) = Mount("""
            <group name="g" x="0" y="0" w="300" h="30" layout="row">
              <label text="AB" /><button text="OK" /><field grow="1" />
            </group>
            """);
        var flex = ((UiFlexGroup)Find(frame, "g")).Flex;
        int measured = flex.MeasureCount;

        Frame(root);
        Frame(root);

        Assert.Equal(measured, flex.MeasureCount);
    }

    [Theory]
    [InlineData(50)]
    [InlineData(100)]
    [InlineData(200)]
    public void A_wrapping_grid_measures_each_item_once_per_layout(int count)
    {
        string icons = string.Concat(Enumerable.Repeat("<icon did=\"1\" />", count));
        var (_, frame) = Mount($"<group name=\"g\" x=\"0\" y=\"0\" w=\"300\" h=\"200\" layout=\"row\" wrap=\"true\">{icons}</group>");
        var group = (UiFlexGroup)Find(frame, "g");
        int before = group.Flex.MeasureCount;

        Assert.True(group.Flex.EnsureLayout(250f, 200f));

        Assert.Equal(count, group.Flex.MeasureCount - before);
    }

    [Fact]
    public void A_steady_state_relayout_allocates_nothing()
    {
        var (_, frame) = Mount("""
            <group name="g" x="0" y="0" w="300" h="60" layout="row" wrap="true" gap="2">
              <label text="AB" /><button text="OK" /><field grow="1" />
              <group layout="column"><toggle text="ABC" checked="{Shown}" /><meter /></group>
            </group>
            """, new Binding());
        UiFlexBox flex = ((UiFlexGroup)Find(frame, "g")).Flex;
        float width = 300f;

        long allocated = ZeroAllocationProbe.MeasureWarmed(() =>
        {
            width = width == 300f ? 280f : 300f;
            flex.EnsureLayout(width, 60f);
        });

        Assert.Equal(0, allocated);
    }
}
```

- [ ] **Step 2: Run them to see them fail**

Run: `dotnet test tests/AcDream.App.Tests --filter "FullyQualifiedName~MarkupFlexTests"`
Expected: build error, `UiFlexGroup` / `UiFlexBox` / `SizedByLayout` do not exist.

- [ ] **Step 3: The layout hook in `UiElement`**

In `DrawSelfAndChildren`, replace

```csharp
                OnDraw(ctx);

                for (int i = 0; i < _children.Count; i++)
                    _children[i].ApplyAnchor(Width, Height);
```

with

```csharp
                OnDraw(ctx);

                LayoutChildren();
```

and add, directly before `internal UiElement? HitTest(float localX, float localY)`:

```csharp
    /// <summary>
    /// Puts the children where they belong for this element's current size,
    /// just before they are drawn: by default each child's anchors. A flex
    /// container lays its items out here instead.
    /// </summary>
    private protected virtual void LayoutChildren()
    {
        for (int i = 0; i < _children.Count; i++)
            _children[i].ApplyAnchor(Width, Height);
    }
```

- [ ] **Step 4: Labels sized by layout**

In `UiPanel.cs`, class `UiLabel`, add after `public bool Outline { get; set; } = true;`:

```csharp
    /// <summary>
    /// Set for a label inside a flex container: its size comes from the
    /// layout (<see cref="MarkupContentSize"/>), so it no longer sizes itself
    /// to its text when drawn, and its line is centred in the height it is given.
    /// </summary>
    internal bool SizedByLayout { get; set; }
```

and replace the body of `UiLabel.OnDraw` with:

```csharp
        string text = TextSource?.Invoke() ?? Text;
        Vector4 textColor = TextColorSource?.Invoke() ?? TextColor;
        float h = DatFont?.LineHeight
            ?? ctx.DefaultFont?.LineHeight ?? 14f;
        float y = 0f;
        if (SizedByLayout)
        {
            y = MathF.Floor((Height - h) / 2f + 0.5f);
        }
        else
        {
            float w = DatFont is { } df
                ? df.MeasureWidth(text)
                : (ctx.DefaultFont?.MeasureWidth(text) ?? text.Length * 7f);
            if (w != Width) Width = w;
            if (h != Height) Height = h;
        }
        if (DatFont is { } dat)
            ctx.DrawStringDat(dat, text, 0, y, textColor, Outline);
        else
            ctx.DrawString(text, 0, y, textColor);
```

(Outside flex the label measures, sizes itself and draws at `y = 0` exactly as before.)

- [ ] **Step 5: The flex box, items and group**

`src/AcDream.App/UI/UiFlexBox.cs`:

```csharp
using AcDream.App.UI.Layout.Flex;

namespace AcDream.App.UI;

/// <summary>
/// The flex side of a markup container (a <c>&lt;group layout&gt;</c>, or a
/// root panel's content area with <c>layout</c>): its <see cref="FlexNode"/>
/// and one <see cref="UiFlexItem"/> per child.
///
/// <para>One tree per flex root. A container whose parent is not a flex
/// container is a <see cref="IsRoot">root</see>; it owns the layout of every
/// flex container nested inside it, whose nodes are inner nodes of its tree
/// (height-for-width only works across inner nodes). A root lays its tree
/// out when it is drawn, and only when something changed: its size, a child's
/// visibility, or the inputs a child was measured from (caption, font,
/// theme). Nothing calls into it to invalidate; it compares at draw.</para>
/// </summary>
internal sealed class UiFlexBox
{
    private float _laidWidth = float.NaN, _laidHeight = float.NaN;
    private bool _laidOut;

    public FlexNode Node { get; } = new();

    public List<UiFlexItem> Items { get; } = new();

    /// <summary>Whether this container lays out its tree; false for a container nested in another.</summary>
    public bool IsRoot { get; set; } = true;

    /// <summary>
    /// Set for a window's content area: the smallest size its content fits
    /// in is recomputed whenever the content changes, and reported through
    /// <see cref="MinimumChanged"/>.
    /// </summary>
    public bool TracksMinimum { get; set; }

    /// <summary>The smallest size the content fits in, after the last layout that measured it.</summary>
    public FlexSize Minimum { get; private set; }

    /// <summary>Raised from <see cref="EnsureLayout"/> when <see cref="Minimum"/> changes.</summary>
    public event Action<FlexSize>? MinimumChanged;

    /// <summary>Leaf measurements made, for the cost tests.</summary>
    internal int MeasureCount { get; set; }

    /// <summary>Adds a control or an absolute group as a measured leaf.</summary>
    public UiFlexItem AddLeaf(UiElement element, FlexNode node, FlexSize? authoredSize = null)
    {
        var item = new UiFlexItem(this, element, node, inner: null, authoredSize);
        node.Measure = item.Measure;
        Add(item);
        return item;
    }

    /// <summary>Adds a nested flex container as an inner node of this tree.</summary>
    public UiFlexItem AddContainer(UiElement element, UiFlexBox inner)
    {
        inner.IsRoot = false;
        var item = new UiFlexItem(this, element, inner.Node, inner, authoredSize: null);
        Add(item);
        return item;
    }

    private void Add(UiFlexItem item)
    {
        Node.Children.Add(item.Node);
        Items.Add(item);
    }

    /// <summary>
    /// Measures the tree now, outside a draw: the window's first measurement
    /// at build time. Returns the preferred size and the fitted minimum.
    /// </summary>
    public FlexMeasurement MeasureNow()
    {
        SyncHidden(this);
        FlexSize minimum = FlexFit.Minimum(Node);
        Minimum = minimum;
        FlexSize preferred = FlexLayout.Measure(Node).Preferred;
        // A size-dependent root can prefer less than it needs on its dependent axis.
        return new FlexMeasurement(
            new FlexSize(MathF.Max(preferred.Width, minimum.Width), MathF.Max(preferred.Height, minimum.Height)),
            minimum);
    }

    /// <summary>
    /// Lays the tree out in <paramref name="width"/> x <paramref name="height"/>
    /// and places every element in it, when anything changed since the last
    /// layout. Returns whether it laid out. Only a root lays out.
    /// </summary>
    public bool EnsureLayout(float width, float height)
    {
        if (!IsRoot) return false;
        bool contentChanged = !_laidOut || Changed(this);
        if (!contentChanged && width == _laidWidth && height == _laidHeight) return false;

        SyncHidden(this);
        if (contentChanged && TracksMinimum)
        {
            FlexSize minimum = FlexFit.Minimum(Node);
            if (minimum != Minimum)
            {
                Minimum = minimum;
                MinimumChanged?.Invoke(minimum);
            }
        }
        FlexLayout.Arrange(Node, new FlexRect(0f, 0f, width, height));
        Place(this);
        _laidOut = true;
        _laidWidth = width;
        _laidHeight = height;
        return true;
    }

    /// <summary>Whether any shown item's visibility or measured inputs changed, at any depth.</summary>
    private static bool Changed(UiFlexBox box)
    {
        foreach (UiFlexItem item in box.Items)
        {
            bool hidden = !item.Element.Visible;
            if (hidden != item.Node.Hidden) return true;
            if (hidden) continue;
            if (item.Inner is { } inner ? Changed(inner) : item.InputsChanged()) return true;
        }
        return false;
    }

    private static void SyncHidden(UiFlexBox box)
    {
        foreach (UiFlexItem item in box.Items)
        {
            item.Node.Hidden = !item.Element.Visible;
            if (item.Inner is { } inner) SyncHidden(inner);
        }
    }

    private static void Place(UiFlexBox box)
    {
        foreach (UiFlexItem item in box.Items)
        {
            if (item.Node.Hidden) continue;
            FlexRect r = item.Node.Rect;
            UiElement e = item.Element;
            e.Left = r.X;
            e.Top = r.Y;
            e.Width = r.Width;
            e.Height = r.Height;
            if (item.Inner is { } inner) Place(inner);
        }
    }
}

/// <summary>
/// One child of a flex container: the element, its node, and the inputs its
/// size was last measured from, so a change of caption, font or theme is
/// noticed at the next draw.
/// </summary>
internal sealed class UiFlexItem
{
    private readonly UiFlexBox _owner;
    private readonly FlexSize? _authoredSize;
    private string _text = string.Empty;
    private UiDatFont? _font;
    private bool _themed;

    internal UiFlexItem(UiFlexBox owner, UiElement element, FlexNode node, UiFlexBox? inner, FlexSize? authoredSize)
    {
        _owner = owner;
        Element = element;
        Node = node;
        Inner = inner;
        _authoredSize = authoredSize;
    }

    public UiElement Element { get; }

    public FlexNode Node { get; }

    /// <summary>The nested container's box when this item is a flex group; null for a leaf.</summary>
    public UiFlexBox? Inner { get; }

    /// <summary>The leaf's measure callback: its content size, recording what it was measured from.</summary>
    internal FlexMeasurement Measure(float? availableWidth)
    {
        _owner.MeasureCount++;
        _text = MarkupContentSize.Text(Element);
        _font = MarkupContentSize.Font(Element);
        _themed = MarkupContentSize.Themed(Element);
        if (_authoredSize is { } authored)
            return new FlexMeasurement(authored, authored);
        return MarkupContentSize.Measure(Element, _text) ?? default;
    }

    /// <summary>Whether the caption, font or theme the leaf was measured from has changed.</summary>
    internal bool InputsChanged() =>
        !ReferenceEquals(_font, MarkupContentSize.Font(Element))
        || _themed != MarkupContentSize.Themed(Element)
        || !string.Equals(_text, MarkupContentSize.Text(Element), StringComparison.Ordinal);
}
```

`src/AcDream.App/UI/UiFlexGroup.cs`:

```csharp
namespace AcDream.App.UI;

/// <summary>
/// A markup <c>&lt;group layout="row|column"&gt;</c>: a group that places its
/// children with <see cref="UiFlexBox"/> instead of their coordinates. It
/// draws and themes like any group. As a root (its parent is not a flex
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

- [ ] **Step 6: Flex groups and items in `MarkupDocument`**

Add `using AcDream.App.UI.Layout.Flex;` after `using System.Xml.Linq;`.

Change the end of `AddElement`'s parameter list and its first line from

```csharp
        IMarkupIconResolver? icons, UiPluginMarkupPanel? themedPanel, PluginUiThemeSettings? themes)
    {
        int firstChild = parent.Children.Count;
```

to

```csharp
        IMarkupIconResolver? icons, UiPluginMarkupPanel? themedPanel, PluginUiThemeSettings? themes,
        UiFlexBox? flex = null)
    {
        int firstChild = parent.Children.Count;
        if (flex is null) MarkupFlexAttributes.RejectItem(el);
```

Replace the start of the `group` case, from `case "group":` through the closing `};` of the `new UiPanel { ... }` initializer, with:

```csharp
            case "group":
                UiFlexBox? groupFlex = null;
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
                group.Left = F(el, "x");
                group.Top = F(el, "y");
                group.Width = F(el, "w");
                group.Height = F(el, "h");
                group.BackgroundColor = el.Attribute("background") is null
                    ? Vector4.Zero
                    : Color((string?)el.Attribute("background"));
                group.BorderColor = el.Attribute("border") is null
                    ? Vector4.Zero
                    : Color((string?)el.Attribute("border"));
                group.BorderThickness = el.Attribute("border") is null ? 0f : 1f;
                group.ClickThrough = true;
```

and in the same case pass the box to the children:

```csharp
                foreach (XElement child in el.Elements())
                    AddElement(group, child, binding, resolve, datFont, icons, themedPanel, themes, groupFlex);
```

At the end of `AddElement`, replace

```csharp
        if (themedPanel is not null && parent.Children.Count > firstChild)
            PluginMarkupTheme.Register(themedPanel, parent.Children[firstChild], el);
    }
```

with

```csharp
        if (themedPanel is not null && parent.Children.Count > firstChild)
            PluginMarkupTheme.Register(themedPanel, parent.Children[firstChild], el);
        if (flex is not null && parent.Children.Count > firstChild)
            AddFlexItem(flex, parent.Children[firstChild], el);
    }

    /// <summary>
    /// Enters <paramref name="element"/>, just built from <paramref name="el"/>
    /// under a flex container, into that container's layout: a flex group as an
    /// inner node, an absolute group as a leaf of its authored size, any other
    /// control as a leaf measured by <see cref="MarkupContentSize"/>. The
    /// container places it, so it keeps no anchors.
    /// </summary>
    private static void AddFlexItem(UiFlexBox flex, UiElement element, XElement el)
    {
        UiFlexItem item = element is UiFlexGroup group
            ? flex.AddContainer(group, group.Flex)
            : flex.AddLeaf(element, new FlexNode(),
                el.Name.LocalName == "group" ? new FlexSize(element.Width, element.Height) : null);
        MarkupFlexAttributes.ReadItem(el, item.Node);
        element.Anchors = AnchorEdges.None;
        if (element is UiLabel label) label.SizedByLayout = true;
    }
```

(The theme registration runs first, so a themed control's font is already the theme's when it is first measured.)

- [ ] **Step 7: Run the tests**

Run: `dotnet test tests/AcDream.App.Tests --filter "FullyQualifiedName~MarkupFlexTests|FullyQualifiedName~MarkupDocumentTests|FullyQualifiedName~PluginThemeClassicIdentityTests|FullyQualifiedName~MarkupResizableAnchorTests|FullyQualifiedName~MarkupTitleBarTests"`
Expected: all pass.

- [ ] **Step 8: Commit**

```bash
git checkout -- '*.lock.json'
git add src/AcDream.App/UI/UiFlexBox.cs src/AcDream.App/UI/UiFlexGroup.cs src/AcDream.App/UI/UiElement.cs \
  src/AcDream.App/UI/UiPanel.cs src/AcDream.App/UI/MarkupDocument.cs tests/AcDream.App.Tests/UI/MarkupFlexTests.cs
git commit -m "feat(ui): flex groups in plugin markup

Claude-Session: https://claude.ai/code/session_019dvJ5gakBkq3uJyr1PAC3u"
```

---

### Task 5: `EnforceMinimumSize`

**Goal:** `RetailWindowManager.EnforceMinimumSize(name)` brings a window up to its `MinWidth`/`MinHeight` whether or not it is resizable, keeps it on screen, and announces the resize (and move) like `ResizeTo`.

**Files:**
- Modify: `src/AcDream.App/UI/RetailWindowManager.cs` (new members before `SetOpacity`, ~line 225)
- Test: `tests/AcDream.App.Tests/UI/RetailWindowManagerMinimumTests.cs`

**Acceptance Criteria:**
- [ ] A non-resizable 300 × 200 window at (10, 10) with minimum 400 × 250 becomes 400 × 250 in place; `Resized` fires once, `Moved` not.
- [ ] The same window at (450, 380) on an 800 × 600 root moves to (400, 350); `Resized` then `Moved`.
- [ ] A minimum height of 900 on a 600-tall root gives height 600 at top 0.
- [ ] A window already at its minimum is untouched and raises nothing; an unknown name returns false.

**Verify:** `dotnet test tests/AcDream.App.Tests --filter "FullyQualifiedName~RetailWindowManager"` → all pass (4 new).

**Steps:**

- [ ] **Step 1: Write the failing tests**

`tests/AcDream.App.Tests/UI/RetailWindowManagerMinimumTests.cs`:

```csharp
using AcDream.App.UI;

namespace AcDream.App.Tests.UI;

/// <summary>
/// <see cref="RetailWindowManager.EnforceMinimumSize"/>: a window whose
/// minimum grew is brought up to it whether or not it is resizable, and kept
/// on screen.
/// </summary>
public sealed class RetailWindowManagerMinimumTests
{
    private static (UiRoot Root, UiNineSlicePanel Frame, RetailWindowHandle Handle, List<string> Events) Window(
        float x, float y)
    {
        var frame = MarkupDocument.Build(
            $"<panel x=\"{x}\" y=\"{y}\" w=\"300\" h=\"200\"></panel>", new object(), _ => (1u, 32, 32));
        var root = new UiRoot { Width = 800, Height = 600 };
        root.AddChild(frame);
        RetailWindowHandle handle = root.RegisterWindow("plugin-min", frame);
        var events = new List<string>();
        handle.Resized += _ => events.Add("resized");
        handle.Moved += _ => events.Add("moved");
        return (root, frame, handle, events);
    }

    [Fact]
    public void A_window_that_cannot_be_resized_still_grows_to_its_minimum()
    {
        var (root, frame, _, events) = Window(10f, 10f);
        Assert.False(frame.ResizeX);
        frame.MinWidth = 400f;
        frame.MinHeight = 250f;

        Assert.True(root.WindowManager.EnforceMinimumSize("plugin-min"));

        Assert.Equal((10f, 10f, 400f, 250f), (frame.Left, frame.Top, frame.Width, frame.Height));
        Assert.Equal(["resized"], events);
    }

    [Fact]
    public void A_window_grown_past_the_screen_edge_moves_back_onto_it()
    {
        var (root, frame, _, events) = Window(450f, 380f);
        frame.MinWidth = 400f;
        frame.MinHeight = 250f;

        root.WindowManager.EnforceMinimumSize("plugin-min");

        Assert.Equal((400f, 350f, 400f, 250f), (frame.Left, frame.Top, frame.Width, frame.Height));
        Assert.Equal(["resized", "moved"], events);
    }

    [Fact]
    public void A_minimum_larger_than_the_screen_takes_the_screen_at_its_origin()
    {
        var (root, frame, _, _) = Window(100f, 100f);
        frame.MinHeight = 900f;

        root.WindowManager.EnforceMinimumSize("plugin-min");

        Assert.Equal((100f, 0f, 300f, 600f), (frame.Left, frame.Top, frame.Width, frame.Height));
    }

    [Fact]
    public void A_window_already_at_its_minimum_is_left_alone()
    {
        var (root, frame, _, events) = Window(10f, 10f);
        frame.MinWidth = 100f;

        Assert.True(root.WindowManager.EnforceMinimumSize("plugin-min"));
        Assert.False(root.WindowManager.EnforceMinimumSize("nobody"));

        Assert.Equal((300f, 200f), (frame.Width, frame.Height));
        Assert.Empty(events);
    }
}
```

- [ ] **Step 2: Run them to see them fail**

Run: `dotnet test tests/AcDream.App.Tests --filter "FullyQualifiedName~RetailWindowManagerMinimumTests"`
Expected: build error, `EnforceMinimumSize` does not exist.

- [ ] **Step 3: Implement**

In `RetailWindowManager.cs`, directly before `public bool SetOpacity(string name, float opacity)`:

```csharp
    /// <summary>
    /// Brings a window up to its <see cref="UiElement.MinWidth"/> and
    /// <see cref="UiElement.MinHeight"/> after its minimum grew (a plugin
    /// window whose content needs more room). Unlike <see cref="ResizeTo"/> it
    /// grows an axis even when the window cannot be resized along it, and it
    /// keeps the window on screen: a frame that would reach past the parent's
    /// right or bottom edge moves left or up, no further than the parent's
    /// origin, and an axis whose minimum is larger than the parent takes the
    /// parent's size at its origin. The new geometry is announced (and so
    /// persisted) like a resize, and a move when the frame moved.
    /// </summary>
    public bool EnforceMinimumSize(string name)
    {
        if (!_byName.TryGetValue(name, out var handle)) return false;
        var frame = handle.OuterFrame;
        float width = MathF.Max(frame.Width, frame.MinWidth);
        float height = MathF.Max(frame.Height, frame.MinHeight);
        float left = frame.Left, top = frame.Top;
        if (frame.Parent is { } parent)
        {
            (left, width) = FitAxis(left, width, parent.Width);
            (top, height) = FitAxis(top, height, parent.Height);
        }

        bool resized = width != frame.Width || height != frame.Height;
        bool moved = left != frame.Left || top != frame.Top;
        if (!resized && !moved) return true;
        frame.Width = width;
        frame.Height = height;
        frame.Left = left;
        frame.Top = top;
        frame.ResetAnchorCapture();
        if (resized) _root.NotifyWindowResized(frame);
        if (moved) _root.NotifyWindowMoved(frame);
        return true;
    }

    /// <summary>Keeps one axis of a window inside a parent <paramref name="space"/> long.</summary>
    private static (float Start, float Size) FitAxis(float start, float size, float space)
    {
        if (size > space) return (0f, MathF.Max(0f, space));
        if (start + size > space) start = MathF.Max(0f, space - size);
        return (start, size);
    }
```

- [ ] **Step 4: Run the tests**

Run: `dotnet test tests/AcDream.App.Tests --filter "FullyQualifiedName~RetailWindowManager"`
Expected: all pass.

- [ ] **Step 5: Commit**

```bash
git checkout -- '*.lock.json'
git add src/AcDream.App/UI/RetailWindowManager.cs tests/AcDream.App.Tests/UI/RetailWindowManagerMinimumTests.cs
git commit -m "feat(ui): grow a window to a raised minimum and keep it on screen

Claude-Session: https://claude.ai/code/session_019dvJ5gakBkq3uJyr1PAC3u"
```

---

### Task 6: Flex windows

**Goal:** `layout` on the root `<panel>` makes the window's content area the flex root. The window gets the title bar by default, is measured once at build time (an unsized root opens at its content size; the minimum is the fitted content minimum plus chrome), and grows when its content needs more room later.

**Files:**
- Modify: `src/AcDream.App/UI/PluginWindowChrome.cs` (after `VerticalInsets`)
- Modify: `src/AcDream.App/UI/PluginTitleBar.cs` (constructor; new `SetFrameWidth`)
- Modify: `src/AcDream.App/UI/UiPluginContentHost.cs` (whole file)
- Modify: `src/AcDream.App/UI/MarkupWindow.cs` (whole file)
- Modify: `src/AcDream.App/UI/MarkupDocument.cs` (`BuildWindow` from the content-area comment to the end of `TitleBar`)
- Test: `tests/AcDream.App.Tests/UI/MarkupFlexWindowTests.cs`

**Acceptance Criteria:**
- [ ] `layout` alone and `layout` + `titlebar="true"` have the bar and a content host at (5, 24); `titlebar="false"` has no bar and the host at (5, 5); in every case `ContentRoot` is the content host and its `Flex` is set.
- [ ] `<panel layout="column" gap="4" padding="8">` with a button "OK" and a label "ABCD" opens with content 54 × 58, frame 64 × 87, frame minimum 64 × 87; the bar is 64 wide with its X at 40. Without the bar the frame is 64 × 68.
- [ ] `layout="row" w="300" h="100" padding="8"`: frame 310 × 129; the first child is at (8, 8, 38, 84) in the content area, screen position (113, 82).
- [ ] `w="20" h="10"` (below the content's 66 × 24) opens at 76 × 53 with that minimum; `minw="30" minh="200"` gives frame minimum 40 × 229 and height 229.
- [ ] `w="NaN"` on a layout root, and `gap` on a root without `layout`, fail the build.
- [ ] The revision is `ComputeAuthoredGeometryRevision(AuthoredInputs(root))`: a bound caption's length never changes it; `layout` and `w` do.
- [ ] A registered `layout="row" w="50" h="40"` window whose bound caption grows to 70 points has frame minimum and width 80 after one draw and one more tick; `Resized` fires once, and the next draw lays the content out 70 wide.
- [ ] The title bar is a child of the frame, never a flex item.
- [ ] `MarkupTitleBarTests`, `MarkupWindowMountTests` and `PluginTitleBarTests` pass unchanged.

**Verify:** `dotnet test tests/AcDream.App.Tests --filter "FullyQualifiedName~MarkupFlexWindowTests|FullyQualifiedName~MarkupTitleBarTests|FullyQualifiedName~MarkupWindowMountTests|FullyQualifiedName~PluginTitleBarTests|FullyQualifiedName~PluginWindowChromeTests"` → all pass (13 in `MarkupFlexWindowTests`).

**Steps:**

- [ ] **Step 1: Write the failing tests**

`tests/AcDream.App.Tests/UI/MarkupFlexWindowTests.cs`:

```csharp
using AcDream.App.UI;
using AcDream.App.UI.Layout.Flex;

namespace AcDream.App.Tests.UI;

/// <summary>
/// A root panel with <c>layout</c>: the title bar by default, the content
/// area as the flex root, the first measurement at build time (size and
/// minimum), the authored-inputs revision, and the minimum following the
/// content afterwards. Headless metrics: 7 points a character, 14-point
/// lines, 24-point controls; chrome 5 points a side and a 24-point bar.
/// </summary>
public sealed class MarkupFlexWindowTests
{
    private sealed class Binding
    {
        public string Caption { get; set; } = "AB";
    }

    private const string Body = "<button text=\"OK\" /><label text=\"ABCD\" />";

    private static MarkupWindow Window(string attrs, string body = Body, object? binding = null) =>
        MarkupDocument.BuildWindow(
            $"<panel x=\"100\" y=\"50\" {attrs}>{body}</panel>", binding ?? new object(), _ => (0u, 0, 0),
            fallbackTitle: "Demo");

    private static UiRoot Mount(MarkupWindow window, string? name = null)
    {
        var root = new UiRoot { Width = 1280, Height = 720 };
        root.AddChild(window.Frame);
        if (name is not null)
            window.Register(root.WindowManager, name, new PluginWindowVisibilityController(null, startVisible: true));
        Frame(root);
        return root;
    }

    private static void Frame(UiRoot root)
    {
        root.Tick(0, 0);
        ThemeDrawCapture.Draw(root);
    }

    [Theory]
    [InlineData("layout=\"column\"", true)]
    [InlineData("layout=\"column\" titlebar=\"true\"", true)]
    [InlineData("layout=\"column\" titlebar=\"false\"", false)]
    public void A_layout_root_has_the_title_bar_unless_it_says_otherwise(string attrs, bool bar)
    {
        MarkupWindow window = Window(attrs);

        Assert.Equal(bar, window.TitleBar is not null);
        var content = Assert.IsType<UiPluginContentHost>(window.ContentRoot);
        Assert.NotNull(content.Flex);
        Assert.Equal(PluginWindowChrome.Border, content.Left);
        Assert.Equal(bar ? PluginWindowChrome.TitleBarHeight : PluginWindowChrome.Border, content.Top);
    }

    [Fact]
    public void A_root_without_a_size_opens_at_its_content_size_plus_chrome()
    {
        MarkupWindow window = Window("layout=\"column\" gap=\"4\" padding=\"8\"");

        // Content: max(38, 28) + 16 wide; 24 + 4 + 14 + 16 tall.
        var content = (UiPluginContentHost)window.ContentRoot;
        Assert.Equal((54f, 58f), (content.Width, content.Height));
        Assert.Equal((64f, 87f), (window.Frame.Width, window.Frame.Height));
        Assert.Equal((64f, 87f), (window.Frame.MinWidth, window.Frame.MinHeight));
        Assert.Equal(64f, window.TitleBar!.Width);
        Assert.Equal(64f - PluginWindowChrome.TitleBarHeight, window.TitleBar.Close.Left);
    }

    [Fact]
    public void Without_the_bar_the_chrome_is_the_border_alone()
    {
        MarkupWindow window = Window("layout=\"column\" titlebar=\"false\" gap=\"4\" padding=\"8\"");

        Assert.Equal((64f, 68f), (window.Frame.Width, window.Frame.Height));
    }

    [Fact]
    public void An_authored_size_is_kept_and_children_are_laid_out_in_the_content_area()
    {
        MarkupWindow window = Window("layout=\"row\" w=\"300\" h=\"100\" padding=\"8\"");
        Mount(window);

        Assert.Equal((310f, 129f), (window.Frame.Width, window.Frame.Height));
        UiElement ok = window.ContentRoot.Children[0];
        Assert.Equal((8f, 8f, 38f, 84f), (ok.Left, ok.Top, ok.Width, ok.Height));
        Assert.Equal(new System.Numerics.Vector2(100f + 5f + 8f, 50f + 24f + 8f), ok.ScreenPosition);
    }

    [Fact]
    public void An_authored_size_below_the_content_minimum_opens_at_the_minimum()
    {
        MarkupWindow window = Window("layout=\"row\" w=\"20\" h=\"10\"");

        // Content minimum: 38 + 28 wide, 24 tall.
        Assert.Equal((76f, 53f), (window.Frame.Width, window.Frame.Height));
        Assert.Equal((76f, 53f), (window.Frame.MinWidth, window.Frame.MinHeight));
    }

    [Fact]
    public void Authored_minimums_win_over_the_content_minimum()
    {
        MarkupWindow window = Window("layout=\"row\" w=\"300\" h=\"100\" minw=\"30\" minh=\"200\"");

        Assert.Equal((40f, 229f), (window.Frame.MinWidth, window.Frame.MinHeight));
        Assert.Equal((310f, 229f), (window.Frame.Width, window.Frame.Height));
    }

    [Fact]
    public void A_bad_root_size_fails_the_build()
    {
        var ex = Assert.Throws<FormatException>(() => Window("layout=\"row\" w=\"NaN\""));
        Assert.Contains("w=\"NaN\"", ex.Message);
    }

    [Fact]
    public void Container_attributes_on_a_root_without_layout_fail_the_build()
    {
        var ex = Assert.Throws<FormatException>(() => Window("w=\"10\" h=\"10\" gap=\"4\"", body: ""));
        Assert.Contains("gap=\"4\"", ex.Message);
    }

    [Fact]
    public void The_revision_comes_from_the_markup_not_the_content()
    {
        int Revision(string attrs, string caption) =>
            Window(attrs, "<label text=\"{Caption}\" />", new Binding { Caption = caption }).AuthoredGeometryRevision;

        Assert.Equal(Revision("layout=\"row\"", "AB"), Revision("layout=\"row\"", "ABCDEFGH"));
        Assert.NotEqual(Revision("layout=\"row\"", "AB"), Revision("layout=\"column\"", "AB"));
        Assert.NotEqual(Revision("layout=\"row\"", "AB"), Revision("layout=\"row\" w=\"200\"", "AB"));
        Assert.Equal(
            RetailWindowManager.ComputeAuthoredGeometryRevision(
                PluginWindowChrome.AuthoredInputs(System.Xml.Linq.XElement.Parse("<panel layout=\"row\" />"))),
            Revision("layout=\"row\"", "AB"));
    }

    [Fact]
    public void A_longer_caption_raises_the_minimum_and_grows_a_registered_window()
    {
        var binding = new Binding();
        MarkupWindow window = Window("layout=\"row\" w=\"50\" h=\"40\"", "<label text=\"{Caption}\" />", binding);
        UiRoot root = Mount(window, "plugin:demo:main");
        var resized = new List<string>();
        Assert.True(root.WindowManager.TryGet("plugin:demo:main", out RetailWindowHandle handle));
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

    [Fact]
    public void Chrome_is_outside_the_flex_tree()
    {
        MarkupWindow window = Window("layout=\"column\"");
        var content = (UiPluginContentHost)window.ContentRoot;

        Assert.Equal(2, content.Flex!.Items.Count);
        Assert.All(content.Flex.Items, item => Assert.Same(content, item.Element.Parent));
        Assert.Same(window.Frame, window.TitleBar!.Parent);
    }
}
```

- [ ] **Step 2: Run them to see them fail**

Run: `dotnet test tests/AcDream.App.Tests --filter "FullyQualifiedName~MarkupFlexWindowTests"`
Expected: build error (`UiPluginContentHost.Flex` does not exist).

- [ ] **Step 3: Chrome insets without a bar**

In `PluginWindowChrome.cs`, after `internal const float VerticalInsets = TitleBarHeight + Border;`:

```csharp

    /// <summary>The top inset of a content area: the title bar, or the border alone without one.</summary>
    internal static float TopInset(bool titleBar) => titleBar ? TitleBarHeight : Border;

    /// <summary>The chrome above and below a content area, with or without a title bar.</summary>
    internal static float VerticalInsetsFor(bool titleBar) => titleBar ? VerticalInsets : 2f * Border;
```

- [ ] **Step 4: A title bar that learns its width late**

In `PluginTitleBar`'s constructor remove `Width = frameWidth;` and the close button's `Left = frameWidth - PluginWindowChrome.TitleBarHeight,`, and end the constructor with `SetFrameWidth(frameWidth);`. The constructor becomes:

```csharp
    public PluginTitleBar(Func<uint, (uint, int, int)> resolve, float frameWidth)
    {
        ClickThrough = true;
        Height = PluginWindowChrome.TitleBarHeight;
        Anchors = AnchorEdges.Left | AnchorEdges.Top | AnchorEdges.Right;
        Close = new PluginCloseButton(resolve)
        {
            Top = PluginWindowChrome.Border,
            Width = PluginWindowChrome.CloseButtonSize,
            Height = PluginWindowChrome.CloseButtonSize,
        };
        Close.Click += () => CloseRequested?.Invoke();
        AddChild(Close);
        SetFrameWidth(frameWidth);
    }

    /// <summary>
    /// Spans the bar across a frame <paramref name="frameWidth"/> wide, with
    /// the close button at its right inset. Used before the window's anchors
    /// are captured: once by the constructor, and again when a window sized by
    /// its content learns its width.
    /// </summary>
    internal void SetFrameWidth(float frameWidth)
    {
        Width = frameWidth;
        Close.Left = frameWidth - PluginWindowChrome.TitleBarHeight;
    }
```

- [ ] **Step 5: The content host as a flex root**

Replace `src/AcDream.App/UI/UiPluginContentHost.cs` with:

```csharp
using AcDream.App.UI.Layout.Flex;

namespace AcDream.App.UI;

/// <summary>
/// The content area of a plugin window with chrome: owns every authored
/// child, sits inside the frame's border and title bar, and stretches with
/// the frame. It draws nothing and lets presses through to the frame, so the
/// window still drags from any empty point. When the root panel has
/// <c>layout</c> it is the root of the window's flex tree.
/// </summary>
internal sealed class UiPluginContentHost : UiElement
{
    private FlexSize? _pendingMinimum;

    public UiPluginContentHost()
    {
        ClickThrough = true;
        Anchors = AnchorEdges.Left | AnchorEdges.Top | AnchorEdges.Right | AnchorEdges.Bottom;
    }

    /// <summary>The content area's flex layout, or null when its children are placed by coordinates.</summary>
    public UiFlexBox? Flex { get; private set; }

    /// <summary>
    /// Raised on the tick after a layout found that the smallest size the
    /// content fits in has changed (a longer caption), with that size. Raised
    /// from the tick rather than the draw so the window can be resized safely.
    /// </summary>
    internal event Action<FlexSize>? ContentMinimumChanged;

    internal bool ClipsChildrenForTest => ClipsChildren;

    /// <summary>Makes the content area a flex container; its children must then be added through <see cref="Flex"/>.</summary>
    internal UiFlexBox UseFlex()
    {
        Flex = new UiFlexBox { TracksMinimum = true };
        Flex.MinimumChanged += minimum => _pendingMinimum = minimum;
        return Flex;
    }

    protected override void OnTick(double deltaSeconds)
    {
        if (_pendingMinimum is not { } minimum) return;
        _pendingMinimum = null;
        ContentMinimumChanged?.Invoke(minimum);
    }

    private protected override void LayoutChildren()
    {
        Flex?.EnsureLayout(Width, Height);
        base.LayoutChildren();
    }
}
```

- [ ] **Step 6: The window follows its content's minimum**

Replace `src/AcDream.App/UI/MarkupWindow.cs` with:

```csharp
namespace AcDream.App.UI;

/// <summary>
/// A built markup window and what mounting it needs: the frame to add to
/// the root, the element that holds its authored content (the frame itself
/// unless the window has chrome), its title bar if it has one, and the
/// revision its saved size is kept under.
/// </summary>
internal sealed class MarkupWindow(
    UiNineSlicePanel frame, UiElement contentRoot, PluginTitleBar? titleBar, int authoredGeometryRevision)
{
    private RetailWindowManager? _manager;
    private string? _name;

    public UiNineSlicePanel Frame { get; } = frame;
    public UiElement ContentRoot { get; } = contentRoot;
    public PluginTitleBar? TitleBar { get; } = titleBar;
    public int AuthoredGeometryRevision { get; } = authoredGeometryRevision;

    /// <summary>
    /// Registers the mounted frame under <paramref name="name"/>, with the
    /// content root and the authored revision, and connects the close button
    /// to <see cref="RetailWindowManager.Close"/>: the path a dock slot takes,
    /// so the player's request is cleared and Hidden/Closed fire as usual.
    /// The frame must already be a direct child of the manager's root.
    /// </summary>
    internal RetailWindowHandle Register(
        RetailWindowManager manager, string name, IRetainedPanelController controller)
    {
        RetailWindowHandle handle = manager.Register(
            name, Frame, ContentRoot, controller, authoredGeometryRevision: AuthoredGeometryRevision);
        if (TitleBar is { } bar)
            bar.CloseRequested += () => manager.Close(name);
        _manager = manager;
        _name = name;
        return handle;
    }

    /// <summary>
    /// Keeps the frame's minimum on the content's: when a flex content area
    /// finds its content needs more room (a longer caption), the frame's
    /// minimum becomes that plus the chrome, unless the markup states its own
    /// <c>minw</c>/<c>minh</c>, and a registered window is grown to it through
    /// <see cref="RetailWindowManager.EnforceMinimumSize"/>.
    /// </summary>
    internal void TrackContentMinimum(
        UiPluginContentHost content, float chromeW, float chromeH, float? authoredMinW, float? authoredMinH)
    {
        content.ContentMinimumChanged += minimum =>
        {
            Frame.MinWidth = (authoredMinW ?? minimum.Width) + chromeW;
            Frame.MinHeight = (authoredMinH ?? minimum.Height) + chromeH;
            if (_manager is { } manager && _name is { } name)
            {
                manager.EnforceMinimumSize(name);
                return;
            }
            Frame.Width = MathF.Max(Frame.Width, Frame.MinWidth);
            Frame.Height = MathF.Max(Frame.Height, Frame.MinHeight);
        };
    }
}
```

- [ ] **Step 7: Layout roots in `BuildWindow`**

In `MarkupDocument.BuildWindow`, replace everything from the comment `// A window with chrome has a content area: its authored sizes are the` down to and including the `TitleBar` method (the line before `private static void AddElement(`) with:

```csharp
        // A window with chrome or a flex layout has a content area: its authored
        // sizes are the area inside the border (and title bar), and the frame
        // adds the chrome. A root with layout gets the title bar by default.
        bool hasLayout = MarkupFlexAttributes.IsContainer(root);
        bool hasTitleBar = TitleBar(root, hasLayout);
        bool hasContentArea = hasLayout || hasTitleBar;
        float chromeW = hasContentArea ? PluginWindowChrome.HorizontalInsets : 0f;
        float chromeH = hasContentArea ? PluginWindowChrome.VerticalInsetsFor(hasTitleBar) : 0f;
        if (!hasLayout) MarkupFlexAttributes.RejectContainer(root);

        // A layout root may leave its size to its content; it is measured once
        // its children exist (below), so these are provisional for it.
        float? authoredW = hasLayout ? MarkupFlexAttributes.RootSize(root, "w") : F(root, "w");
        float? authoredH = hasLayout ? MarkupFlexAttributes.RootSize(root, "h") : F(root, "h");
        float? authoredMinW = hasLayout ? MarkupFlexAttributes.RootSize(root, "minw") : null;
        float? authoredMinH = hasLayout ? MarkupFlexAttributes.RootSize(root, "minh") : null;
        float contentW = authoredW ?? 0f, contentH = authoredH ?? 0f;
        panel.Left = F(root, "x"); panel.Top = F(root, "y");
        panel.Width = contentW + chromeW; panel.Height = contentH + chromeH;

        bool resizable = B(root, "resizable", false);
        panel.Resizable = resizable;
        panel.MinWidth = FOr(root, "minw", contentW) + chromeW;
        panel.MinHeight = FOr(root, "minh", contentH) + chromeH;
        panel.ResizeX = resizable;
        panel.ResizeY = resizable;

        string? resize = (string?)root.Attribute("resize");
        if (resize is not null)
        {
            panel.ResizeX = resize is "x" or "both";
            panel.ResizeY = resize is "y" or "both";
        }

        string? visible = (string?)root.Attribute("visible");
        if (visible is not null && IsBinding(visible))
        {
            PropertyInfo? flag = binding.GetType().GetProperty(visible[1..^1]);
            if (flag is null || flag.PropertyType != typeof(bool))
            {
                throw new FormatException(
                    $"<panel visible=\"{visible}\"> did not resolve to a bool property "
                    + $"on {binding.GetType().Name}");
            }
            panel.VisibleSource = () => flag.GetValue(binding) is true;
        }

        string? title = (string?)root.Attribute("title");
        Vector4 titleColor = style is not null && style.TryColor("title", "color", out var c) ? c : Vector4.One;
        UiElement contentParent = panel;
        UiPluginContentHost? content = null;
        PluginTitleBar? titleBar = null;
        if (hasTitleBar)
        {
            titleBar = new PluginTitleBar(resolve, panel.Width)
            {
                Title = string.IsNullOrEmpty(title) ? fallbackTitle ?? string.Empty : title,
                DatFont = datFont,
                TextColor = titleColor,
            };
            titleBar.Close.AuthoredTooltipRootElementId = RuntimeTooltipRootElementId;
            titleBar.Close.AuthoredTooltipLayoutDid = RuntimeTooltipLayoutDid;
            titleBar.Close.AuthoredTooltipEnabled = true;
            panel.AddChild(titleBar);
            if (panel is UiPluginMarkupPanel barPanel)
            {
                barPanel.HasTitle = true;
                PluginMarkupTheme.RegisterTitleBar(barPanel, titleBar);
            }
        }
        else if (!string.IsNullOrEmpty(title))
        {
            panel.AddChild(new UiLabel
            {
                Text = title, Left = 8, Top = 4, TextColor = titleColor, DatFont = datFont,
            });
            if (panel is UiPluginMarkupPanel titlePanel && panel.Children[0] is UiLabel titleLabel)
            {
                titlePanel.HasTitle = true;
                PluginMarkupTheme.Register(titlePanel, titleLabel, new XElement("label"));
                PluginMarkupTheme.RegisterTitle(titlePanel, titleLabel);
            }
        }
        if (hasContentArea)
        {
            content = new UiPluginContentHost
            {
                Left = PluginWindowChrome.Border,
                Top = PluginWindowChrome.TopInset(hasTitleBar),
                Width = contentW,
                Height = contentH,
            };
            panel.AddChild(content);
            contentParent = content;
        }

        UiFlexBox? rootFlex = null;
        if (hasLayout)
        {
            rootFlex = content!.UseFlex();
            MarkupFlexAttributes.ReadContainer(root, rootFlex.Node);
        }

        foreach (var el in root.Elements())
            AddElement(contentParent, el, binding, resolve, datFont, icons, panel as UiPluginMarkupPanel, themes, rootFlex);

        if (rootFlex is not null)
        {
            // The window's first measurement, with the fonts and theme known
            // now: it sizes a root that states no w/h, and sets the minimum, so
            // mounting and registration see real geometry. An authored size
            // below the content's minimum opens at the minimum.
            FlexMeasurement measured = rootFlex.MeasureNow();
            float minW = authoredMinW ?? measured.Minimum.Width;
            float minH = authoredMinH ?? measured.Minimum.Height;
            contentW = MathF.Max(authoredW ?? measured.Preferred.Width, minW);
            contentH = MathF.Max(authoredH ?? measured.Preferred.Height, minH);
            panel.Width = contentW + chromeW; panel.Height = contentH + chromeH;
            panel.MinWidth = minW + chromeW; panel.MinHeight = minH + chromeH;
            content!.Width = contentW; content.Height = contentH;
            titleBar?.SetFrameWidth(panel.Width);
        }

        // The whole document now sits at its authored sizes, so this is the one
        // moment every anchor margin can be read off the layout its author wrote.
        // Pin them here rather than on first draw: a group that is hidden when the
        // window is resized is never reached by the draw-time anchor pass, and
        // would otherwise measure its children against the resized parent the first
        // time it is opened.
        panel.CaptureAuthoredAnchorBaselines();

        int revision = hasContentArea
            ? RetailWindowManager.ComputeAuthoredGeometryRevision(PluginWindowChrome.AuthoredInputs(root))
            : RetailWindowManager.ComputeAuthoredGeometryRevision(
                panel.Width, panel.Height, panel.MinWidth, panel.MinHeight, panel.Resizable);
        var window = new MarkupWindow(panel, contentParent, titleBar, revision);
        if (rootFlex is not null)
            window.TrackContentMinimum(content!, chromeW, chromeH, authoredMinW, authoredMinH);
        return window;
    }

    /// <summary>
    /// Whether the root asks for the host title bar: <c>titlebar</c> when it
    /// is given, else on exactly when the root uses <c>layout</c>.
    /// </summary>
    private static bool TitleBar(XElement root, bool hasLayout) => (string?)root.Attribute("titlebar") switch
    {
        null => hasLayout,
        "false" => false,
        "true" => true,
        string other => throw new FormatException(
            $"<panel titlebar=\"{other}\"> must be true or false"),
    };
```

Notes for the reviewer: the content host is now created after the title bar's theme registration (child order is unchanged: bar, then host). It is also created for a layout root without a bar. Without `layout` the code computes exactly today's numbers: `authoredW` is `F(root, "w")`, the minimums come from `FOr`, and the revision branch is the same.

- [ ] **Step 8: Run the tests**

Run: `dotnet test tests/AcDream.App.Tests --filter "FullyQualifiedName~MarkupFlexWindowTests|FullyQualifiedName~MarkupTitleBarTests|FullyQualifiedName~MarkupWindowMountTests|FullyQualifiedName~PluginTitleBarTests|FullyQualifiedName~PluginWindowChromeTests|FullyQualifiedName~MarkupFlexTests|FullyQualifiedName~PluginThemeClassicIdentityTests"`
Expected: all pass.

- [ ] **Step 9: Commit**

```bash
git checkout -- '*.lock.json'
git add src/AcDream.App/UI/PluginWindowChrome.cs src/AcDream.App/UI/PluginTitleBar.cs src/AcDream.App/UI/UiPluginContentHost.cs \
  src/AcDream.App/UI/MarkupWindow.cs src/AcDream.App/UI/MarkupDocument.cs tests/AcDream.App.Tests/UI/MarkupFlexWindowTests.cs
git commit -m "feat(ui): flex plugin windows size themselves and keep their content minimum

Claude-Session: https://claude.ai/code/session_019dvJ5gakBkq3uJyr1PAC3u"
```

---

### Task 7: Docs and the FlexDemo sample

**Goal:** `docs/plugin-ui-markup.md` documents flex layout, and `samples/AcDream.Plugins.FlexDemo` shows a finder and a settings window built with it; a test builds the sample's markup.

**Files:**
- Modify: `docs/plugin-ui-markup.md` (element table, the paragraph after it, the title-bar opening line, new "Flex layout" section before "Icon ids")
- Create: `samples/AcDream.Plugins.FlexDemo/AcDream.Plugins.FlexDemo.csproj`, `plugin.json`, `packages.neutral.lock.json`, `FlexDemoPlugin.cs`, `finder.xml`, `settings.xml`
- Modify: `AcDream.slnx` (samples folder)
- Test: `tests/AcDream.App.Tests/UI/FlexDemoSampleTests.cs`

**Acceptance Criteria:**
- [ ] The docs list every container and item attribute with its default, the content sizes, the build errors, mixing absolute and flex, and the flex-window rules (bar default, unsized roots, minimum, saved-size reset).
- [ ] `dotnet build samples/AcDream.Plugins.FlexDemo -c Release` → 0 warnings; the output folder holds `plugin.json`, `finder.xml`, `settings.xml`.
- [ ] The finder opens at 330 × 289 with a title bar, every item inside its container, and the list taller than 150; the settings window grows when its summary gets longer and still fits.

**Verify:** `dotnet test tests/AcDream.App.Tests --filter "FullyQualifiedName~FlexDemoSampleTests"` → 2 passed.

**Steps:**

- [ ] **Step 1: Write the failing test**

`tests/AcDream.App.Tests/UI/FlexDemoSampleTests.cs`:

```csharp
using AcDream.App.UI;

namespace AcDream.App.Tests.UI;

/// <summary>
/// The FlexDemo sample's markup builds, lays out inside its windows, and the
/// settings window grows when its summary gets longer. The bindings here
/// mirror the sample's property names (the sample is not referenced by the
/// test project).
/// </summary>
public sealed class FlexDemoSampleTests
{
    private sealed class Finder
    {
        public string Search => string.Empty;
        public Action<string> SetSearch => _ => { };
        public IReadOnlyList<string> Results { get; } = ["Asheron", "Holtburg"];
        public int Selected => -1;
        public Action<int> Select => _ => { };
        public string Status => "2 found";
        public Action Find => () => { };
        public Action<string> Submit => _ => { };
        public Action Clear => () => { };
        public Action Done => () => { };
    }

    private sealed class Settings
    {
        public IReadOnlyList<string> Profiles { get; } = ["Mage", "Melee"];
        public string Profile => "Mage";
        public Action<string> SetProfile => _ => { };
        public string Target => "Asheron";
        public Action<string> SetTarget => _ => { };
        public bool AutoRebuff => true;
        public Action ToggleAutoRebuff => () => { };
        public bool Announce => false;
        public Action ToggleAnnounce => () => { };
        public string Summary { get; set; } = "Mage";
        public string DetailCaption => "Show details";
        public Action ToggleDetail => () => { };
        public Action Apply => () => { };
    }

    private static string Markup(string file)
    {
        string? directory = AppContext.BaseDirectory;
        while (directory is not null && !File.Exists(Path.Combine(directory, "AcDream.slnx")))
            directory = Directory.GetParent(directory)?.FullName;
        Assert.NotNull(directory);
        return File.ReadAllText(Path.Combine(directory, "samples", "AcDream.Plugins.FlexDemo", file));
    }

    private static (UiRoot Root, MarkupWindow Window) Mount(string file, object binding)
    {
        MarkupWindow window = MarkupDocument.BuildWindow(Markup(file), binding, _ => (0u, 0, 0), themes: new PluginUiThemeSettings());
        var root = new UiRoot { Width = 1280, Height = 720 };
        root.AddChild(window.Frame);
        window.Register(root.WindowManager, file, new PluginWindowVisibilityController(null, startVisible: true));
        root.Tick(0, 0);
        ThemeDrawCapture.Draw(root);
        return (root, window);
    }

    private static void AssertInside(UiElement container)
    {
        foreach (UiElement child in container.Children)
        {
            Assert.True(child.Left >= 0f && child.Top >= 0f, $"{child.GetType().Name} starts outside its parent");
            Assert.True(child.Left + child.Width <= container.Width + 0.01f, $"{child.GetType().Name} overflows the width");
            Assert.True(child.Top + child.Height <= container.Height + 0.01f, $"{child.GetType().Name} overflows the height");
            if (child is UiFlexGroup) AssertInside(child);
        }
    }

    [Fact]
    public void The_finder_lays_out_inside_its_window()
    {
        var (_, window) = Mount("finder.xml", new Finder());

        Assert.NotNull(window.TitleBar);
        Assert.Equal((330f, 289f), (window.Frame.Width, window.Frame.Height));
        AssertInside(window.ContentRoot);
        UiElement list = window.ContentRoot.Children[1];
        Assert.IsType<UiMarkupList>(list);
        Assert.True(list.Height > 150f);
    }

    [Fact]
    public void The_settings_window_sizes_itself_and_grows_with_its_summary()
    {
        var settings = new Settings();
        var (root, window) = Mount("settings.xml", settings);
        float width = window.Frame.Width;
        AssertInside(window.ContentRoot);

        settings.Summary = "Mage on Asheron: rebuff on, announcements off, and a much longer line besides";
        for (int i = 0; i < 3; i++) { root.Tick(0, 0); ThemeDrawCapture.Draw(root); }

        Assert.True(window.Frame.Width > width);
        AssertInside(window.ContentRoot);
    }
}
```

Run: `dotnet test tests/AcDream.App.Tests --filter "FullyQualifiedName~FlexDemoSampleTests"`
Expected: FAIL, `finder.xml` not found.

- [ ] **Step 2: The sample**

`samples/AcDream.Plugins.FlexDemo/AcDream.Plugins.FlexDemo.csproj`:

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
    <None Include="finder.xml" CopyToOutputDirectory="PreserveNewest" />
    <None Include="settings.xml" CopyToOutputDirectory="PreserveNewest" />
  </ItemGroup>
</Project>
```

`samples/AcDream.Plugins.FlexDemo/plugin.json`:

```json
{
  "id": "sample.flex-demo",
  "displayName": "Flex Layout Sample",
  "version": "1.0.0",
  "entryDll": "AcDream.Plugins.FlexDemo.dll",
  "apiVersion": 1,
  "kinds": ["gameplay"],
  "hosts": ["graphical"]
}
```

`samples/AcDream.Plugins.FlexDemo/packages.neutral.lock.json` (the same as the other samples':
the only dependency is the abstractions project):

```json
{
  "version": 2,
  "dependencies": {
    "net10.0": {
      "acdream.plugin.abstractions": {
        "type": "Project"
      }
    }
  }
}
```

`samples/AcDream.Plugins.FlexDemo/finder.xml`:

```xml
<panel x="120" y="140" title="Flex Finder" theme="plugin" layout="column" padding="8" gap="6" resizable="true" w="320" h="260">
  <group layout="row" gap="4">
    <field grow="1" text="{Search}" onchange="{SetSearch}" onsubmit="{Submit}" />
    <button text="Find" onclick="{Find}" />
  </group>
  <list grow="1" items="{Results}" selected="{Selected}" onchange="{Select}" />
  <group layout="row" gap="4" align="center">
    <label grow="1" text="{Status}" color="theme:muted|#FF9AA99E" />
    <button text="Clear" onclick="{Clear}" />
    <button text="Close" onclick="{Done}" />
  </group>
</panel>
```

`samples/AcDream.Plugins.FlexDemo/settings.xml`:

```xml
<panel x="470" y="140" title="Flex Settings" theme="plugin" layout="column" padding="10" gap="8">
  <group layout="row" gap="8" align="center">
    <label text="Profile" minw="60" color="theme:muted|#FF9AA99E" />
    <menu grow="1" items="{Profiles}" selected="{Profile}" onchange="{SetProfile}" />
  </group>
  <group layout="row" gap="8" align="center">
    <label text="Target" minw="60" color="theme:muted|#FF9AA99E" />
    <field grow="1" text="{Target}" onchange="{SetTarget}" />
  </group>
  <toggle text="Rebuff automatically" checked="{AutoRebuff}" onclick="{ToggleAutoRebuff}" />
  <toggle text="Announce in chat" checked="{Announce}" onclick="{ToggleAnnounce}" />
  <label text="{Summary}" />
  <group layout="row" justify="end" gap="4">
    <button text="{DetailCaption}" onclick="{ToggleDetail}" />
    <button text="Apply" onclick="{Apply}" />
  </group>
</panel>
```

`samples/AcDream.Plugins.FlexDemo/FlexDemoPlugin.cs`:

```csharp
using AcDream.Plugin.Abstractions;

namespace AcDream.Plugins.FlexDemo;

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
    private readonly SettingsBinding _settingsBinding = new();

    public void Initialize(IPluginHost host) => _host = host;

    public void Enable()
    {
        if (_host is not { } host) return;
        // The host loads plugin assemblies from memory, so Assembly.Location is
        // empty; a relative markup path is read from the install folder.
        string Markup(string file) => host.PluginDirectory is { } dir ? Path.Combine(dir, file) : file;
        _finder = host.Ui.RegisterPanel(
            new PluginPanelDescriptor("finder", "Flex Finder") { IconText = "FF", StartVisible = true },
            Markup("finder.xml"), _finderBinding);
        _settings = host.Ui.RegisterPanel(
            new PluginPanelDescriptor("settings", "Flex Settings") { IconText = "FS", StartVisible = true },
            Markup("settings.xml"), _settingsBinding);
    }

    public void Disable()
    {
        _finder?.Dispose();
        _finder = null;
        _settings?.Dispose();
        _settings = null;
    }

    /// <summary>A search box over the names it finds.</summary>
    public sealed class FinderBinding
    {
        private static readonly string[] Names =
        [
            "Asheron", "Bael'Zharon", "Ispar", "Holtburg", "Shoushi", "Yaraq",
            "Rithwic", "Arwic", "Cragstone", "Eastham", "Glenden Wood", "Lytelthorpe",
        ];

        public string Search { get; private set; } = string.Empty;
        public Action<string> SetSearch => value => Search = value;
        public IReadOnlyList<string> Results { get; private set; } = Names;
        public int Selected { get; private set; } = -1;
        public Action<int> Select => index => Selected = index;
        public string Status => Selected >= 0 && Selected < Results.Count
            ? $"Selected {Results[Selected]}"
            : $"{Results.Count} found";

        public Action Find => Run;
        public Action<string> Submit => value => { Search = value; Run(); };
        public Action Clear => () => { Search = string.Empty; Run(); };
        public Action Done => () => Selected = -1;

        private void Run()
        {
            Results = Names.Where(n => n.Contains(Search, StringComparison.OrdinalIgnoreCase)).ToArray();
            Selected = -1;
        }
    }

    /// <summary>A short form whose summary and detail button change length on demand.</summary>
    public sealed class SettingsBinding
    {
        private bool _detail;

        public IReadOnlyList<string> Profiles { get; } = ["Mage", "Melee", "Archer", "Lifestone runner"];
        public string Profile { get; private set; } = "Mage";
        public Action<string> SetProfile => value => Profile = value;
        public string Target { get; private set; } = "Asheron";
        public Action<string> SetTarget => value => Target = value;
        public bool AutoRebuff { get; private set; } = true;
        public Action ToggleAutoRebuff => () => AutoRebuff = !AutoRebuff;
        public bool Announce { get; private set; }
        public Action ToggleAnnounce => () => Announce = !Announce;

        public string Summary => _detail
            ? $"{Profile} on {Target}: rebuff {(AutoRebuff ? "on" : "off")}, announcements {(Announce ? "on" : "off")}"
            : Profile;
        public string DetailCaption => _detail ? "Hide details" : "Show details";
        public Action ToggleDetail => () => _detail = !_detail;
        public Action Apply => () => { };
    }
}
```

In `AcDream.slnx`, add to the `/samples/` folder, before the ThemeGallery line:

```xml
    <Project Path="samples/AcDream.Plugins.FlexDemo/AcDream.Plugins.FlexDemo.csproj" />
```

- [ ] **Step 3: The docs**

In `docs/plugin-ui-markup.md`:

Replace the `panel` and `group` rows of the Elements table with:

```markdown
| `panel` (root) | The window | `x y w h title visible resizable minw minh resize titlebar`, and the flex container attributes |
| `group` | Layout container | `x y w h background border`, and the flex container attributes |
```

Replace `a binding.` at the end of the paragraph after the table with:

```markdown
a binding. Inside a flex container elements take the flex item attributes
instead of `x`, `y` and `anchor` (see [Flex layout](#flex-layout)).
```

Replace the first line of "Title bar", `` `titlebar="true"` on the root `<panel>` gives the window a host title bar: ``, with:

```markdown
`titlebar="true"` on the root `<panel>` gives the window a host title bar
(a root with `layout` has it unless it says `titlebar="false"`; see
[Flex layout](#flex-layout)):
```

Insert before `## Icon ids`:

````markdown
## Flex layout

`layout="row"` or `layout="column"` on a `<group>` or on the root `<panel>`
makes it a **flex container**: it places its children itself, one after
another along its main axis, sized to their content and sharing out the
space that is left. A container without `layout` keeps absolute placement.
The rules are a practical subset of CSS flexbox.

```xml
<panel title="Buff Bot" layout="column" padding="8" gap="6" resizable="true">
  <group layout="row" gap="4">
    <field grow="1" text="{Search}" onchange="{SetSearch}" />
    <button text="Find" onclick="{Find}" />
  </group>
  <list grow="1" items="{Results}" selected="{Selected}" onchange="{Select}" />
  <group layout="row" justify="end" gap="4">
    <button text="OK" onclick="{Ok}" />
    <button text="Cancel" onclick="{Cancel}" />
  </group>
</panel>
```

### Container attributes

| Attribute | Values | Default |
|---|---|---|
| `layout` | `row`, `column` | absent: absolute placement |
| `gap` | points between items, and between wrapped lines | `0` |
| `padding` | 1, 2 or 4 numbers in CSS order: all; vertical horizontal; top right bottom left | `0` |
| `justify` | `start`, `center`, `end`, `space-between` | `start` |
| `align` | `start`, `center`, `end`, `stretch` (across the line) | `stretch` |
| `wrap` | `true`, `false` (rows only) | `false` |

`gap`, `padding`, `justify`, `align` and `wrap` need `layout` on the same
element. Only rows wrap in this version: `wrap="true"` on a column fails the
build.

### Item attributes

Every child of a flex container may carry:

| Attribute | Meaning | Default |
|---|---|---|
| `grow` | share of the space left over on the main axis | `0` |
| `shrink` | share of the space missing, weighted by the item's basis | `1` |
| `basis` | `auto` or points: the item's starting size on the main axis | `auto`: its `w`/`h` on that axis, else its content size |
| `w`, `h` | preferred size, in place of the content size | the content size |
| `minw`, `maxw`, `minh`, `maxh` | limits | min: the content minimum; max: none |
| `alignself` | `start`, `center`, `end`, `stretch` | the container's `align` |

An item never shrinks below its content (a button below its caption) unless
`minw`/`minh` allow it. `x`, `y` and `anchor` on a flex item fail the build,
and so do `grow`, `shrink`, `basis`, `alignself` and the limits on a child
of a container without `layout`.

### Content sizes

| Element | Size |
|---|---|
| `label` | its text, one line tall |
| `button`, `tab` | its caption with 12 points either side (and a square for its icon), 24 points tall or the font's line plus 8 |
| `toggle` | lamp or switch, then its caption; 20 points tall |
| `icon` | 32 × 32 |
| `field`, `menu` | 120 wide (40 at least), control height |
| `slider` / `meter` | 120 wide (40 at least), 16 / 12 tall |
| `list`, `log` | 160 × 80 (60 × 40 at least) |
| `group` with `layout` | what its own items need |
| `group` without `layout` | its `w` × `h` |

Sizes follow what is shown: a bound caption that gets longer, a hidden item
(which takes no space), or a switch of the shared appearance lays the
container out again on the next frame. A label inside a flex container
draws in the box it is given, its line centred top to bottom.

### Mixing absolute and flex

The two nest freely. A group without `layout` inside a flex container is an
item of its authored `w` × `h`; when the container gives it another size, its
own children follow their `anchor`s exactly as in a resized window. A
`<group layout>` inside an absolute container is placed by its `x`, `y`,
`w`, `h` and `anchor` like any element, and lays out its children in
whatever size that gives it.

### Flex windows

`layout` on the root `<panel>` lays out the window's content area:

- the window gets the [title bar](#title-bar) unless the root says
  `titlebar="false"` (without it the content area is inset by the 5-point
  border on every side);
- `w` and `h` may be left out: the window then opens at the size its
  content needs. A size below what the content needs opens at the latter;
- `minw` and `minh` default to what the content needs, so a window never
  shrinks (by dragging, or by a restored layout) to a size that cuts its
  content off. When the content needs more later (a longer caption), the
  window grows to fit, even when it is not resizable, and stays on screen;
- as with the title bar, the window's saved size is reset once when its
  markup's `w`, `h`, `minw`, `minh`, `resizable`, `resize`, `layout` or
  `titlebar` change. Captions and appearance never reset it.

A root that wraps should state its `w`: without it the window opens one
line wide.
````

- [ ] **Step 4: Build and test**

```bash
dotnet build samples/AcDream.Plugins.FlexDemo -c Release 2>&1 | grep -E "Warning\(s\)|Error\(s\)"
dotnet test tests/AcDream.App.Tests --filter "FullyQualifiedName~FlexDemoSampleTests"
```

Expected: `0 Warning(s)`, `0 Error(s)`; 2 passed.

- [ ] **Step 5: Commit**

```bash
git checkout -- '*.lock.json'
git add docs/plugin-ui-markup.md samples/AcDream.Plugins.FlexDemo AcDream.slnx tests/AcDream.App.Tests/UI/FlexDemoSampleTests.cs
git status --short   # bin/ and obj/ must not be staged
git commit -m "docs(ui): flex layout in plugin markup; FlexDemo sample

Claude-Session: https://claude.ai/code/session_019dvJ5gakBkq3uJyr1PAC3u"
```

---

### Task 8: Full verification, review, live gate and merge

**Goal:** The branch builds clean, fails exactly main's tests, passes a code review and the live gate, and is merged into fork main with the user's go-ahead.

**Files:**
- No planned source changes (review and live-gate fixes are committed on `flex/markup` with their own messages).

**Acceptance Criteria:**
- [ ] `dotnet build AcDream.slnx -c Release` → `0 Warning(s)`, `0 Error(s)`.
- [ ] `AcDream.App.Tests` on `flex/markup` fails exactly the tests main fails (166 environment failures at c4767d26), compared by name.
- [ ] A code review of `main..flex/markup` reports no unresolved Critical/High issue.
- [ ] Live, on the local ACE server (10.10.20.20 via `~/OpenAC-dev/dev-client.env`, player account), both FlexDemo windows show a title bar and content that does not overlap or clip, in Classic, Moss and Brass (2× captures shown to the user).
- [ ] Live: resizing the finder grows the list, and the finder stops shrinking where its content would clip. "Show details" in the settings window grows it to fit the long summary, and "Hide details" leaves it at that size. A theme switch reflows both windows.
- [ ] The user has confirmed merging into fork main (`--no-ff`); nothing is pushed without their word.

**Verify:** `git log --oneline main..flex/markup` lists the Task 1–7 commits; the Release build line above; the failure comparison prints nothing.

**Steps:**

- [ ] **Step 1: Release build**

```bash
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
cd .worktrees/flex-markup
```

Expected: `comm` prints nothing. A test from `GraphicalPluginSessionTests`, `LiveEntityNetworkBranchRoutingTests` or `GameWindowRenderLeafCompositionTests` that differs is rerun alone on both before counting.

- [ ] **Step 3: Code review**

Invoke superpowers-extended-cc:requesting-code-review for `main..flex/markup` against this plan and the spec (sections 1, 3.2–3.4, 5 and the PR 3 planning corrections). Fix Critical/High findings on the branch, re-run the affected tasks' verify commands, commit each fix.

- [ ] **Step 4: Live gate**

```bash
dotnet build samples/AcDream.Plugins.FlexDemo -c Release
mkdir -p ~/OpenAC-dev/plugins/sample.flex-demo
cp -R samples/AcDream.Plugins.FlexDemo/bin/Release/net10.0/. ~/OpenAC-dev/plugins/sample.flex-demo/
git checkout -- '*.lock.json'
OPENAC_ROOT=/Users/davidsmith/code/OpenAC/OpenAC/.worktrees/flex-markup \
  ~/code/OpenAC/OpenAC-Plugins-Golem/tools/run-dev-client.sh    # run in the background; player account by default
```

In game check each live criterion above. Switch themes from the dock's gear (Plugin appearance). Capture the backbuffer at 2× (retina capture workflow: the user drags the window to the built-in display) in Classic, Moss and Brass, and show the captures to the user. Ask the user to tune anything that looks off (control heights, paddings, label centring). A constant change goes back through Task 3's tests and the expectations in Tasks 4, 6 and 7. Remove `~/OpenAC-dev/plugins/sample.flex-demo` afterwards if the user asks.

- [ ] **Step 5: Finish**

Use superpowers-extended-cc:finishing-a-development-branch. Delivery as for PRs 1 and 2: merge `flex/markup` into local main with `--no-ff` after review and the user's confirmation, keep the branch locally, push nothing without the user's word.

## Execution notes (2026-10-05)

Branch `flex/markup`, head c76c878f (11 commits), merged into local main as
4f16abe3 (`--no-ff`, on top of the `feat/field-onblur` merge 053cedf7; no
conflicts). Tasks 1–7 reviewed per task. Task 2 needed a fix round: `FlexFit`
now always arranges at the size it returns (9f7bf5e0). The final review's fix
wave (7896260d) added:

- no legacy title label on a layout root with `titlebar="false"`;
- a zero-size-parent guard in `EnforceMinimumSize`;
- the sample's "Done" caption;
- exact line-height terms and three behaviour notes in the docs;
- a test pinning restore against the minimum.

The live gate on 10.10.20.20 (player account) passed. It found one bug: a
flex `<menu>` opened a popup only 20 points wide, because the popup took its
width from an authored `w`. Fixed in c76c878f, where the popup follows the
laid-out width on each open. Release build has 0 warnings. The App suite
fails the same 166 environment tests as main, by name, before and after the
merge.

Backlog for PR 4:

- the frame minimum ignores the title bar, so a very narrow window's X
  covers its title;
- the dock's "–" sits over the content corner of a layout window without
  a bar;
- the remaining minor items, which the final review triaged as "leave".
