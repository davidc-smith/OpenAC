# Markup controls, plan 3: `<radio>` Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers-extended-cc:subagent-driven-development (recommended) or superpowers-extended-cc:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Ship `<radio>`: one choice out of several, as round buttons with captions or as a joined segmented bar, with one tab stop and arrow-key selection. Share caption drawing between `<toggle>`, `<checkbox>` and `<radio>`.

**Architecture:** `UiCaption` draws the one-line captions these controls share (left-aligned or centred). `UiMarkupRadio` is one element for the whole group: it splits its box equally into one cell per option along its orientation, hit-tests press and release by cell, and draws either `UiRadioGlyph` dots with captions or segmented cells. Selection is plugin-owned: the widget reports the picked index through `Action<int>` and draws what `selected` returns.

**Tech Stack:** C# / .NET 10, xUnit, headless `UiRoot` + `RecordingGpuDevice` tests.

**Spec:** `docs/superpowers/specs/2026-10-21-markup-controls-design.md` (branch `docs/markup-controls-spec`), "Shared rules" and section 4. Plans 1-2 built the plumbing this uses.

## Global Constraints

- Worktree `/Users/davidsmith/code/OpenAC/OpenAC/.worktrees/markup-controls`, branch `feat/markup-controls` (HEAD 345009da, from upstream 391e709a). Never merge fork `main` in; never add `docs/superpowers/` files there. Push only when the controller says so.
- Every shell: `export DOTNET_ROOT=$HOME/.dotnet PATH=$HOME/.dotnet:$PATH`. After building, `git checkout -- '*.lock.json'`; never commit lock files.
- Every commit must build and pass its own tests.
- Commit messages end with `Claude-Session: https://claude.ai/code/session_01UzLnYkJ6ifzKGaeNwgsp8W`.
- Binding helper convention (MarkupBindings `<summary>`): `BindRequired*` throw when absent or mistyped; other `Bind*` return null when absent and throw when present but mistyped; `context` is `"<element> <attribute>"`.
- Builders return exactly one element; `AddElement` adds it.
- Never two `onchange` calls for one user action; never report the value that is already selected.
- `<radio>` contract: `items` is required, a literal list split on `|` or a bound `IEnumerable<string>`; `selected` is a required `int` binding (−1 or out of range = none); `onchange` is an optional `Action<int>` with the picked index (without it the group only displays); `orientation` is `vertical` (default) or `horizontal`; `style` is `radio` (default) or `segmented`; anything else throws at build.
- Behaviour: cells split the box equally along the orientation; a click fires when the left button goes up over the same option it went down on; clicking does not take keyboard focus (as `<toggle>`); one tab stop for the group; while focused, Up/Left select the previous option and Down/Right the next, wrapping, and from no selection Down/Right pick the first and Up/Left the last; disabled fires nothing and draws faded.
- Glyph: 11×11 px like the checkbox and lamp; captions at x = 17 within each radio cell; segmented captions centred in their cell.
- `UiCaption` refactor of `<toggle>` and `<checkbox>` must not change what they draw.

**User decisions (already made):**
- One branch, one commit series per control, upstream-portable.
- `<radio>` design approved: one element for the group, literal or bound `items`, `int` `selected`/`onchange`, `orientation`, `style="radio|segmented"`, arrow keys move and wrap.
- Plan 1 final review: extract a caption helper when the third control copies the toggle's caption drawing (this series).

---

## File Structure

| File | Responsibility |
|---|---|
| `src/AcDream.App/UI/UiCaption.cs` (new) | Shared one-line caption drawing (left or centred) |
| `src/AcDream.App/UI/UiMarkupToggle.cs`, `UiMarkupCheckbox.cs` | Use `UiCaption.Draw` |
| `src/AcDream.App/UI/Markup/MarkupBindings.cs` | `BindIntAction`; `BindBoolAction` shares one generic body |
| `src/AcDream.App/UI/MarkupDocument.cs` | `BindStringList`, `BindRequiredIntReader` → `internal`; `case "radio"` |
| `src/AcDream.App/UI/UiRadioGlyph.cs` (new) | The 11 px round button |
| `src/AcDream.App/UI/UiMarkupRadio.cs` (new) | The radio group widget |
| `src/AcDream.App/UI/Markup/MarkupRadio.cs` (new) | `<radio>` builder |
| `src/AcDream.App/UI/PluginMarkupTheme.cs` | Radio font + palette registration |
| `tests/AcDream.App.Tests/UI/MarkupRadioTests.cs` (new) | Binding, behaviour, draw and theme tests |
| `docs/plugin-ui-markup.md` | User docs |

---

### Task 1: Shared caption drawing

**Goal:** `<toggle>` and `<checkbox>` draw their captions through one `UiCaption` helper, which also offers centred captions for `<radio style="segmented">`.

**Files:**
- Create: `src/AcDream.App/UI/UiCaption.cs`
- Modify: `src/AcDream.App/UI/UiMarkupToggle.cs`, `src/AcDream.App/UI/UiMarkupCheckbox.cs`

**Acceptance Criteria:**
- [ ] `UiCaption.Draw` reproduces the old caption code exactly: dat font centred on the box height and outlined when asked; bitmap fallback one pixel below the top.
- [ ] `UiCaption.DrawCentred` centres text horizontally and vertically, measuring with the dat font, else `ctx.DefaultFont`, else 7 px per character (as `UiSimpleButton`'s fallback).
- [ ] Toggle and checkbox call `UiCaption.Draw(ctx, DatFont, caption, 17f, 0f, Height, color, outline: ThemePalette is null)`; nothing else in them changes.
- [ ] All existing toggle and checkbox tests pass unchanged.

**Verify:** `dotnet test tests/AcDream.App.Tests/AcDream.App.Tests.csproj --filter "FullyQualifiedName~AcDream.App.Tests.UI.Markup"` → all pass (334 before this series).

**Steps:**

- [ ] **Step 1: Create `src/AcDream.App/UI/UiCaption.cs`**

```csharp
using System.Numerics;

namespace AcDream.App.UI;

/// <summary>
/// The one-line caption markup toggles, checkboxes and radio buttons share.
/// A dat font is centred on the row's height (outlined in Classic); the
/// bitmap fallback sits one pixel below the row's top.
/// </summary>
internal static class UiCaption
{
    public static void Draw(
        UiRenderContext ctx, UiDatFont? font, string text, float x, float top, float height,
        Vector4 color, bool outline)
    {
        if (font is { } dat)
            ctx.DrawStringDat(dat, text, x, top + (height - dat.LineHeight) * 0.5f, color, outline: outline);
        else
            ctx.DrawString(text, x, top + 1f, color);
    }

    /// <summary>The caption centred in a box, measured the way a markup button measures its own.</summary>
    public static void DrawCentred(
        UiRenderContext ctx, UiDatFont? font, string text, float x, float top, float width, float height,
        Vector4 color, bool outline)
    {
        float textWidth = font?.MeasureWidth(text) ?? ctx.DefaultFont?.MeasureWidth(text) ?? text.Length * 7f;
        float lineHeight = font?.LineHeight ?? ctx.DefaultFont?.LineHeight ?? 14f;
        float tx = x + (width - textWidth) * 0.5f;
        float ty = top + (height - lineHeight) * 0.5f;
        if (font is { } dat)
            ctx.DrawStringDat(dat, text, tx, ty, color, outline: outline);
        else
            ctx.DrawString(text, tx, ty, color);
    }
}
```

If `ctx.DefaultFont?.LineHeight` is an `int`, the `??` chain still compiles to `float`; if the compiler complains, cast with `(float?)`.

- [ ] **Step 2: Use it in `UiMarkupToggle.OnDraw`** — replace the caption block

```csharp
        float y = DatFont is { } font
            ? (Height - font.LineHeight) * 0.5f
            : 1f;
        if (DatFont is { } dat)
            ctx.DrawStringDat(dat, caption, 17f, y, color, outline: ThemePalette is null);
        else
            ctx.DrawString(caption, 17f, y, color);
```

with

```csharp
        UiCaption.Draw(ctx, DatFont, caption, 17f, 0f, Height, color, outline: ThemePalette is null);
```

- [ ] **Step 3: Same in `UiMarkupCheckbox.OnDraw`** — replace

```csharp
        float y = DatFont is { } font ? (Height - font.LineHeight) * 0.5f : 1f;
        if (DatFont is { } dat)
            ctx.DrawStringDat(dat, caption, 17f, y, color, outline: ThemePalette is null);
        else
            ctx.DrawString(caption, 17f, y, color);
```

with

```csharp
        UiCaption.Draw(ctx, DatFont, caption, 17f, 0f, Height, color, outline: ThemePalette is null);
```

(Match the exact text in each file; the checkbox's version may differ in line breaks.)

- [ ] **Step 4: Run the markup tests**

Run: `dotnet test tests/AcDream.App.Tests/AcDream.App.Tests.csproj --filter "FullyQualifiedName~AcDream.App.Tests.UI.Markup"`
Expected: PASS, same count as before.

- [ ] **Step 5: Commit**

```bash
git checkout -- '*.lock.json' 2>/dev/null
git add src/AcDream.App/UI/UiCaption.cs src/AcDream.App/UI/UiMarkupToggle.cs src/AcDream.App/UI/UiMarkupCheckbox.cs
git commit -m "refactor(ui): one caption helper for markup toggles and checkboxes

Radio buttons will draw the same captions; segmented ones centre them.

Claude-Session: https://claude.ai/code/session_01UzLnYkJ6ifzKGaeNwgsp8W"
```

---

### Task 2: Radio bindings

**Goal:** The radio builder can bind an optional `Action<int>`, a string list and a required `int`.

**Files:**
- Modify: `src/AcDream.App/UI/Markup/MarkupBindings.cs`
- Modify: `src/AcDream.App/UI/MarkupDocument.cs` (visibility only)
- Test: `tests/AcDream.App.Tests/UI/MarkupRadioTests.cs` (create)

**Acceptance Criteria:**
- [ ] `MarkupBindings.BindIntAction` returns null when absent, invokes the bound `Action<int>`, and throws `FormatException` naming the context for a literal, a missing property or another type.
- [ ] `BindBoolAction` and `BindIntAction` share one private generic body; `BindBoolAction`'s messages are unchanged (`Action<bool>`).
- [ ] `MarkupDocument.BindStringList` and `MarkupDocument.BindRequiredIntReader` are `internal static`; their bodies are unchanged.

**Verify:** `dotnet test tests/AcDream.App.Tests/AcDream.App.Tests.csproj --filter "FullyQualifiedName~MarkupRadioTests|FullyQualifiedName~MarkupCheckboxTests"` → all pass.

**Steps:**

- [ ] **Step 1: Write the failing tests** — create `tests/AcDream.App.Tests/UI/MarkupRadioTests.cs`:

```csharp
using System.Linq;
using System.Numerics;
using AcDream.App.UI;
using AcDream.App.UI.Markup;
using Silk.NET.Input;

namespace AcDream.App.Tests.UI;

public class MarkupRadioTests
{
    private sealed class Binding
    {
        public int Mode { get; set; }
        public int Calls { get; private set; }
        public int LastChange { get; private set; } = -1;
        public Action<int> SetMode => v => { Mode = v; LastChange = v; Calls++; };
        public IReadOnlyList<string> Modes { get; set; } = ["Melee", "Missile", "Magic"];
        public bool NotInt { get; set; }
        public Action<string> WrongAction => _ => { };
    }

    [Fact]
    public void IntAction_MissingIsNullAndBoundInvokes()
    {
        var b = new Binding();
        Assert.Null(MarkupBindings.BindIntAction(null, b, "ctx"));
        MarkupBindings.BindIntAction("{SetMode}", b, "ctx")!(2);
        Assert.Equal(2, b.Mode);
    }

    [Theory]
    [InlineData("SetMode")]
    [InlineData("{WrongAction}")]
    [InlineData("{Missing}")]
    public void IntAction_RejectsLiteralsAndWrongTypes(string attribute)
    {
        var ex = Assert.Throws<FormatException>(
            () => MarkupBindings.BindIntAction(attribute, new Binding(), "radio onchange"));
        Assert.Contains("radio onchange", ex.Message);
        Assert.Contains("Action<int>", ex.Message);
    }
}
```

- [ ] **Step 2: Run and see it fail**

Run: `dotnet test tests/AcDream.App.Tests/AcDream.App.Tests.csproj --filter "FullyQualifiedName~MarkupRadioTests"`
Expected: build FAILS: `'MarkupBindings' does not contain a definition for 'BindIntAction'`.

- [ ] **Step 3: Share the optional-action body** — in `MarkupBindings`, replace the whole `BindBoolAction` method with:

```csharp
    /// <summary>An optional <c>Action&lt;bool&gt;</c>; null when the attribute is absent.</summary>
    public static Action<bool>? BindBoolAction(string? attribute, object binding, string context) =>
        BindOptionalAction<bool>(attribute, binding, context, "bool");

    /// <summary>An optional <c>Action&lt;int&gt;</c>; null when the attribute is absent.</summary>
    public static Action<int>? BindIntAction(string? attribute, object binding, string context) =>
        BindOptionalAction<int>(attribute, binding, context, "int");

    private static Action<T>? BindOptionalAction<T>(
        string? attribute, object binding, string context, string typeName)
    {
        if (attribute is null)
            return null;
        if (!MarkupDocument.IsBinding(attribute))
            throw new FormatException($"{context} must be an Action<{typeName}> binding");
        PropertyInfo? property = binding.GetType().GetProperty(attribute[1..^1]);
        if (property is null || !typeof(Action<T>).IsAssignableFrom(property.PropertyType))
        {
            throw new FormatException(
                $"{attribute} did not resolve to an Action<{typeName}> property on "
                + binding.GetType().Name + $" ({context})");
        }
        return value => (property.GetValue(binding) as Action<T>)?.Invoke(value);
    }
```

(Check the old `BindBoolAction` messages read exactly `"{context} must be an Action<bool> binding"` and `"... did not resolve to an Action<bool> property on ..."`; the new body must produce the same text for `bool`.)

- [ ] **Step 4: Open two helpers in `MarkupDocument`** — change `private static` to `internal static` on:

```csharp
    internal static Func<IReadOnlyList<string>> BindStringList(
    internal static Func<int> BindRequiredIntReader(
```

- [ ] **Step 5: Run the tests**

Run: `dotnet test tests/AcDream.App.Tests/AcDream.App.Tests.csproj --filter "FullyQualifiedName~MarkupRadioTests|FullyQualifiedName~MarkupCheckboxTests"`
Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git checkout -- '*.lock.json' 2>/dev/null
git add src/AcDream.App/UI/Markup/MarkupBindings.cs src/AcDream.App/UI/MarkupDocument.cs tests/AcDream.App.Tests/UI/MarkupRadioTests.cs
git commit -m "feat(ui): optional Action<int> binding for markup elements

BindBoolAction and BindIntAction share one body; the string-list and
required-int readers open up for the radio builder.

Claude-Session: https://claude.ai/code/session_01UzLnYkJ6ifzKGaeNwgsp8W"
```

---

### Task 3: `<radio>` widget and builder

**Goal:** A `<radio>` element that reports the option a click or arrow key picks, in both orientations and both styles.

**Files:**
- Create: `src/AcDream.App/UI/UiRadioGlyph.cs`
- Create: `src/AcDream.App/UI/UiMarkupRadio.cs`
- Create: `src/AcDream.App/UI/Markup/MarkupRadio.cs`
- Modify: `src/AcDream.App/UI/MarkupDocument.cs` (`case "radio"`)
- Test: `tests/AcDream.App.Tests/UI/MarkupRadioTests.cs`

**Acceptance Criteria:**
- [ ] Literal `items="a|b|c"` and bound `items="{List}"` both build; `Items` reflects a bound list's current contents.
- [ ] Build throws for: missing or empty `items`; `items` bound to a non-`IEnumerable<string>`; missing or non-`int` `selected`; mistyped `onchange`; `orientation` other than vertical/horizontal; `style` other than radio/segmented.
- [ ] Clicking another option reports its index once; clicking the selected option, pressing on one option and releasing on another, or clicking with no `onchange` reports nothing. A click leaves `root.KeyboardFocus` null.
- [ ] Horizontal cells split the width; vertical cells split the height.
- [ ] While focused, Up/Left and Down/Right move with wrap-around; from no selection (−1 or out of range) Down/Right pick 0 and Up/Left pick the last; arrows without focus do nothing.
- [ ] Disabled reports nothing for clicks or keys. Empty bound items ignore input and draw without throwing.
- [ ] The group is one tab stop (`TabStop`, `AcceptsFocus`, `FocusOnMouseClick == false`).

**Verify:** `dotnet test tests/AcDream.App.Tests/AcDream.App.Tests.csproj --filter "FullyQualifiedName~MarkupRadioTests"` → all pass.

**Steps:**

- [ ] **Step 1: Write the failing tests** — append inside `MarkupRadioTests`:

```csharp
    private const string Literal = "items=\"Melee|Missile|Magic\" selected=\"{Mode}\" onchange=\"{SetMode}\"";

    // The radio sits at (8, 8). Vertical 200×54: rows 18 px tall (y 8, 26, 44).
    // Horizontal 210×20: cells 70 px wide (x 8, 78, 148).
    private static (UiRoot Root, UiMarkupRadio Radio) Mount(
        string attributes, Binding binding, string size = "w=\"200\" h=\"54\"")
    {
        var panel = MarkupDocument.Build(
            $"<panel x=\"0\" y=\"0\" w=\"300\" h=\"100\"><radio x=\"8\" y=\"8\" {size} {attributes}/></panel>",
            binding, _ => (1u, 16, 16));
        var root = new UiRoot { Width = 800, Height = 600 };
        root.AddChild(panel);
        return (root, Assert.IsType<UiMarkupRadio>(panel.Children[0]));
    }

    private static void Click(UiRoot root, int x, int y)
    {
        root.OnMouseDown(UiMouseButton.Left, x, y);
        root.OnMouseUp(UiMouseButton.Left, x, y);
    }

    private static void Focus(UiMarkupRadio radio) =>
        radio.OnEvent(new UiEvent { Type = UiEventType.FocusGained });

    private static void Press(UiMarkupRadio radio, Key key) =>
        radio.OnEvent(new UiEvent { Type = UiEventType.KeyDown, Data0 = (int)key });

    [Fact]
    public void Radio_LiteralItemsAndBoundSelection()
    {
        var (_, radio) = Mount(Literal, new Binding { Mode = 2 });
        Assert.Equal(new[] { "Melee", "Missile", "Magic" }, radio.Items);
        Assert.Equal(2, radio.Selected);
    }

    [Fact]
    public void Radio_BoundItemsFollowTheList()
    {
        var b = new Binding();
        var (_, radio) = Mount("items=\"{Modes}\" selected=\"{Mode}\" onchange=\"{SetMode}\"", b);
        Assert.Equal(3, radio.Items.Count);
        b.Modes = ["A", "B"];
        Assert.Equal(new[] { "A", "B" }, radio.Items);
    }

    [Theory]
    [InlineData("selected=\"{Mode}\"")]
    [InlineData("items=\"\" selected=\"{Mode}\"")]
    [InlineData("items=\"{NotInt}\" selected=\"{Mode}\"")]
    [InlineData("items=\"a|b\"")]
    [InlineData("items=\"a|b\" selected=\"{NotInt}\"")]
    [InlineData("items=\"a|b\" selected=\"{Mode}\" onchange=\"{WrongAction}\"")]
    [InlineData("items=\"a|b\" selected=\"{Mode}\" orientation=\"diagonal\"")]
    [InlineData("items=\"a|b\" selected=\"{Mode}\" style=\"round\"")]
    public void Radio_BadAttributesThrowAtBuild(string attributes)
    {
        Assert.Throws<FormatException>(() => Mount(attributes, new Binding()));
    }

    [Fact]
    public void Radio_ClickReportsTheOptionWithoutTakingFocus()
    {
        var b = new Binding();
        var (root, _) = Mount(Literal, b);
        Click(root, 20, 26 + 5);
        Assert.Equal(1, b.LastChange);
        Assert.Equal(1, b.Calls);
        Assert.Null(root.KeyboardFocus);
    }

    [Fact]
    public void Radio_ClickingTheSelectedOptionReportsNothing()
    {
        var b = new Binding();
        var (root, _) = Mount(Literal, b);
        Click(root, 20, 8 + 5);
        Assert.Equal(0, b.Calls);
    }

    [Fact]
    public void Radio_HorizontalCellsSplitTheWidth()
    {
        var b = new Binding();
        var (root, _) = Mount(Literal + " orientation=\"horizontal\"", b, "w=\"210\" h=\"20\"");
        Click(root, 78 + 10, 15);
        Assert.Equal(1, b.LastChange);
        Click(root, 148 + 10, 15);
        Assert.Equal(2, b.LastChange);
    }

    [Fact]
    public void Radio_PressAndReleaseMustBeOnTheSameOption()
    {
        var b = new Binding();
        var (root, _) = Mount(Literal, b);
        root.OnMouseDown(UiMouseButton.Left, 20, 26 + 5);
        root.OnMouseUp(UiMouseButton.Left, 20, 44 + 5);
        Assert.Equal(0, b.Calls);
    }

    [Fact]
    public void Radio_WithoutOnchangeOnlyDisplays()
    {
        var b = new Binding();
        var (root, _) = Mount("items=\"a|b|c\" selected=\"{Mode}\"", b);
        Click(root, 20, 26 + 5);
        Assert.Equal(0, b.Mode);
    }

    [Fact]
    public void Radio_DisabledReportsNothing()
    {
        var b = new Binding();
        var (root, radio) = Mount(Literal + " enabled=\"false\"", b);
        Click(root, 20, 26 + 5);
        Focus(radio);
        Press(radio, Key.Down);
        Assert.Equal(0, b.Calls);
    }

    [Fact]
    public void Radio_ArrowKeysMoveAndWrap()
    {
        var b = new Binding();
        var (_, radio) = Mount(Literal, b);
        Focus(radio);
        Press(radio, Key.Down);
        Assert.Equal(1, b.Mode);
        Press(radio, Key.Right);
        Assert.Equal(2, b.Mode);
        Press(radio, Key.Down);
        Assert.Equal(0, b.Mode);
        Press(radio, Key.Up);
        Assert.Equal(2, b.Mode);
        Press(radio, Key.Left);
        Assert.Equal(1, b.Mode);
        Assert.Equal(5, b.Calls);
    }

    [Theory]
    [InlineData(-1, Key.Down, 0)]
    [InlineData(-1, Key.Up, 2)]
    [InlineData(5, Key.Right, 0)]
    [InlineData(5, Key.Left, 2)]
    public void Radio_ArrowsFromNoSelectionPickAnEnd(int start, Key key, int expected)
    {
        var b = new Binding { Mode = start };
        var (_, radio) = Mount(Literal, b);
        Focus(radio);
        Press(radio, key);
        Assert.Equal(expected, b.Mode);
    }

    [Fact]
    public void Radio_ArrowsNeedFocus()
    {
        var b = new Binding();
        var (_, radio) = Mount(Literal, b);
        Press(radio, Key.Down);
        Assert.Equal(0, b.Calls);
    }

    [Fact]
    public void Radio_IsOneTabStopThatClicksDoNotFocus()
    {
        var (_, radio) = Mount(Literal, new Binding());
        Assert.True(radio.TabStop);
        Assert.True(radio.AcceptsFocus);
        Assert.False(radio.FocusOnMouseClick);
    }

    [Fact]
    public void Radio_EmptyBoundItemsIgnoreInput()
    {
        var b = new Binding { Modes = [] };
        var (root, radio) = Mount("items=\"{Modes}\" selected=\"{Mode}\" onchange=\"{SetMode}\"", b);
        Click(root, 20, 13);
        Focus(radio);
        Press(radio, Key.Down);
        Assert.Equal(0, b.Calls);
        MarkupDrawProbe.Draw(radio);
    }
```

- [ ] **Step 2: Run and see them fail**

Run: `dotnet test tests/AcDream.App.Tests/AcDream.App.Tests.csproj --filter "FullyQualifiedName~MarkupRadioTests"`
Expected: build FAILS: `UiMarkupRadio` not found.

- [ ] **Step 3: Create `src/AcDream.App/UI/UiRadioGlyph.cs`**

```csharp
using System.Numerics;

namespace AcDream.App.UI;

/// <summary>
/// The markup radio button's 11 px round button: the lamp's octagon as the
/// ring, a dark well inside it, and a 3 px mark when chosen. Classic borrows
/// the lamp's colours, as the checkbox does.
/// </summary>
internal static class UiRadioGlyph
{
    public const float Size = 11f;

    public static readonly Vector4 ClassicRing = UiCheckLamp.UncheckedInner;
    public static readonly Vector4 ClassicWell = UiCheckBoxGlyph.ClassicFill;
    public static readonly Vector4 ClassicMark = UiCheckLamp.CheckedInner;

    public static void Draw(
        UiRenderContext ctx, float x, float y, bool selected, PluginUiPalette? palette, float alpha = 1f)
    {
        Vector4 ring = Alpha(palette is { } p ? (selected ? p.Accent : p.Border) : ClassicRing, alpha);
        Vector4 well = Alpha(palette?.Field ?? ClassicWell, alpha);
        Vector4 mark = Alpha(palette?.Accent ?? ClassicMark, alpha);

        ctx.DrawFill(x + 3f, y, 5f, 1f, ring);
        ctx.DrawFill(x + 1f, y + 1f, 9f, 2f, ring);
        ctx.DrawFill(x, y + 3f, 11f, 5f, ring);
        ctx.DrawFill(x + 1f, y + 8f, 9f, 2f, ring);
        ctx.DrawFill(x + 3f, y + 10f, 5f, 1f, ring);
        ctx.DrawFill(x + 3f, y + 2f, 5f, 7f, well);
        ctx.DrawFill(x + 2f, y + 3f, 7f, 5f, well);
        if (selected)
            ctx.DrawFill(x + 4f, y + 4f, 3f, 3f, mark);
    }

    private static Vector4 Alpha(Vector4 color, float alpha) => color with { W = color.W * alpha };
}
```

- [ ] **Step 4: Create `src/AcDream.App/UI/UiMarkupRadio.cs`**

```csharp
using System.Numerics;

namespace AcDream.App.UI;

/// <summary>
/// A markup radio group: one choice out of several, drawn as round buttons
/// with captions or as a joined segmented bar. Its box is split equally into
/// one cell per option along its orientation. It reports the option a click
/// or arrow key picks; the plugin stores it and the group draws whatever
/// <see cref="SelectedSource"/> returns.
/// </summary>
public sealed class UiMarkupRadio : UiElement
{
    public static readonly Vector4 ClassicSegmentFace = new(0.16f, 0.14f, 0.10f, 1f);
    public static readonly Vector4 ClassicSegmentSelected = new(0.32f, 0.27f, 0.15f, 1f);
    public static readonly Vector4 ClassicSegmentBorder = UiCheckLamp.UncheckedInner;
    public static readonly Vector4 FocusColor = new(1f, 0.82f, 0.25f, 1f);

    public PluginUiPalette? ThemePalette { get; set; }
    public Func<IReadOnlyList<string>>? ItemsSource { get; set; }

    /// <summary>The chosen option; −1 or out of range is none.</summary>
    public Func<int>? SelectedSource { get; set; }

    /// <summary>Called with the option a click or arrow key picks.</summary>
    public Action<int>? Changed { get; set; }

    public bool Horizontal { get; set; }
    public bool Segmented { get; set; }
    public UiDatFont? DatFont { get; set; }
    public Vector4 TextColor { get; set; } = new(0.86f, 0.84f, 0.74f, 1f);

    /// <summary>Re-read every frame in place of <see cref="TextColor"/>.</summary>
    public Func<Vector4>? TextColorSource { get; set; }

    public IReadOnlyList<string> Items => ItemsSource?.Invoke() ?? Array.Empty<string>();
    public int Selected => SelectedSource?.Invoke() ?? -1;

    public override bool HandlesClick => true;

    private readonly UiKeyboardActivation _keyboardActivation = new();
    private int _pressed = -1;

    /// <summary>The option under a point in local coordinates, or −1.</summary>
    public int IndexAt(float x, float y)
    {
        int count = Items.Count;
        if (count == 0 || x < 0f || y < 0f || x >= Width || y >= Height)
            return -1;
        float along = Horizontal ? x : y;
        float cell = (Horizontal ? Width : Height) / count;
        return Math.Min(count - 1, (int)(along / cell));
    }

    private void Select(int index)
    {
        if (!Enabled || index < 0 || index >= Items.Count || index == Selected)
            return;
        Changed?.Invoke(index);
    }

    /// <summary>One option along, wrapping; from no selection, the first or last.</summary>
    private void Move(int delta)
    {
        int count = Items.Count;
        if (count == 0)
            return;
        int current = Selected;
        int next = current >= 0 && current < count
            ? (current + delta + count) % count
            : delta > 0 ? 0 : count - 1;
        Select(next);
    }

    public override bool OnEvent(in UiEvent e)
    {
        if (_keyboardActivation.HandleEvent(in e, TabStop, Enabled, activate: null))
            return true;
        switch (e.Type)
        {
            case UiEventType.MouseDown:
                _pressed = Enabled ? IndexAt(e.Data1, e.Data2) : -1;
                return true;
            case UiEventType.MouseUp:
                if (_pressed >= 0 && IndexAt(e.Data1, e.Data2) == _pressed)
                    Select(_pressed);
                _pressed = -1;
                return true;
            case UiEventType.CaptureChanged:
                _pressed = -1;
                return true;
            case UiEventType.KeyDown when _keyboardActivation.Focused && Enabled:
                switch ((Silk.NET.Input.Key)e.Data0)
                {
                    case Silk.NET.Input.Key.Up or Silk.NET.Input.Key.Left:
                        Move(-1);
                        return true;
                    case Silk.NET.Input.Key.Down or Silk.NET.Input.Key.Right:
                        Move(1);
                        return true;
                }
                return false;
        }
        return false;
    }

    private float Alpha => Enabled ? 1f : UiMarkupToggle.DisabledAlpha;

    private Vector4 Fade(Vector4 color) => color with { W = color.W * Alpha };

    private (float X, float Y, float W, float H) Cell(int index, int count)
    {
        if (Horizontal)
        {
            float w = Width / count;
            return (index * w, 0f, w, Height);
        }
        float h = Height / count;
        return (0f, index * h, Width, h);
    }

    protected override void OnDraw(UiRenderContext ctx)
    {
        IReadOnlyList<string> items = Items;
        int selected = Selected;
        Vector4 text = Fade(TextColorSource?.Invoke() ?? TextColor);
        bool outline = ThemePalette is null;
        if (Segmented)
            DrawSegments(ctx, items, selected, text, outline);
        else
            DrawButtons(ctx, items, selected, text, outline);
        if (_keyboardActivation.Focused)
            ctx.DrawRectOutline(0f, 0f, Width, Height, FocusColor, 1f);
    }

    private void DrawButtons(
        UiRenderContext ctx, IReadOnlyList<string> items, int selected, Vector4 text, bool outline)
    {
        for (int i = 0; i < items.Count; i++)
        {
            var (x, y, w, h) = Cell(i, items.Count);
            ctx.PushClip(x, y, w, h);
            try
            {
                float glyphY = y + MathF.Max(1f, (h - UiRadioGlyph.Size) * 0.5f);
                UiRadioGlyph.Draw(ctx, x + 1f, glyphY, i == selected, ThemePalette, Alpha);
                UiCaption.Draw(ctx, DatFont, items[i], x + 17f, y, h, text, outline);
            }
            finally
            {
                ctx.PopClip();
            }
        }
    }

    private void DrawSegments(
        UiRenderContext ctx, IReadOnlyList<string> items, int selected, Vector4 text, bool outline)
    {
        Vector4 face = Fade(ThemePalette?.Field ?? ClassicSegmentFace);
        Vector4 chosen = Fade(ThemePalette?.Selected ?? ClassicSegmentSelected);
        Vector4 border = Fade(ThemePalette?.Border ?? ClassicSegmentBorder);
        for (int i = 0; i < items.Count; i++)
        {
            var (x, y, w, h) = Cell(i, items.Count);
            ctx.DrawFill(x, y, w, h, i == selected ? chosen : face);
            if (i > 0)
            {
                if (Horizontal) ctx.DrawFill(x, y, 1f, h, border);
                else ctx.DrawFill(x, y, w, 1f, border);
            }
            ctx.PushClip(x, y, w, h);
            try
            {
                UiCaption.DrawCentred(ctx, DatFont, items[i], x, y, w, h, text, outline);
            }
            finally
            {
                ctx.PopClip();
            }
        }
        ctx.DrawRectOutline(0f, 0f, Width, Height, border, 1f);
    }
}
```

- [ ] **Step 5: Create `src/AcDream.App/UI/Markup/MarkupRadio.cs`**

```csharp
using System.Xml.Linq;

namespace AcDream.App.UI.Markup;

internal static class MarkupRadio
{
    public static UiMarkupRadio Build(XElement el, object binding, MarkupBuildContext context)
    {
        var radio = new UiMarkupRadio
        {
            AcceptsFocus = true,
            FocusOnMouseClick = false,
            TabStop = true,
            Left = MarkupDocument.F(el, "x"),
            Top = MarkupDocument.F(el, "y"),
            Width = MarkupDocument.F(el, "w"),
            Height = MarkupDocument.F(el, "h"),
            ItemsSource = Items((string?)el.Attribute("items"), binding),
            SelectedSource = MarkupDocument.BindRequiredIntReader(
                (string?)el.Attribute("selected"), binding, "radio selected"),
            Changed = MarkupBindings.BindIntAction(
                (string?)el.Attribute("onchange"), binding, "radio onchange"),
            Horizontal = Horizontal(el),
            Segmented = Segmented(el),
            DatFont = context.DatFont,
        };
        string? color = (string?)el.Attribute("color");
        if (color is not null)
            radio.TextColor = MarkupDocument.Color(color);
        context.BindColor(color, value => radio.TextColor = value, source => radio.TextColorSource = source);
        MarkupDocument.ApplyCommon(radio, el, binding);
        return radio;
    }

    /// <summary>A bound string list, or a literal list separated by <c>|</c>.</summary>
    private static Func<IReadOnlyList<string>> Items(string? expression, object binding)
    {
        if (string.IsNullOrEmpty(expression))
            throw new FormatException("radio items must list at least one option");
        if (MarkupDocument.IsBinding(expression))
            return MarkupDocument.BindStringList(expression, binding, "radio items");
        string[] options = expression.Split('|');
        return () => options;
    }

    private static bool Horizontal(XElement el) => (string?)el.Attribute("orientation") switch
    {
        null or "vertical" => false,
        "horizontal" => true,
        var other => throw new FormatException(
            $"<radio orientation=\"{other}\"> must be vertical or horizontal"),
    };

    private static bool Segmented(XElement el) => (string?)el.Attribute("style") switch
    {
        null or "radio" => false,
        "segmented" => true,
        var other => throw new FormatException($"<radio style=\"{other}\"> must be radio or segmented"),
    };
}
```

- [ ] **Step 6: Wire it into `MarkupDocument.AddElement`** — after the `case "number": … break;` block, separated by a blank line:

```csharp
            case "radio":
                parent.AddChild(MarkupRadio.Build(el, binding, Context()));
                break;
```

- [ ] **Step 7: Run the tests**

Run: `dotnet test tests/AcDream.App.Tests/AcDream.App.Tests.csproj --filter "FullyQualifiedName~MarkupRadioTests"`
Expected: PASS. If a click test misses, check how `UiRoot` delivers `MouseUp` coordinates to the captured element (`UiRoot.OnMouseUp`, around line 700) before changing anything, and report what you found.

- [ ] **Step 8: Run all markup tests and commit**

Run: `dotnet test tests/AcDream.App.Tests/AcDream.App.Tests.csproj --filter "FullyQualifiedName~AcDream.App.Tests.UI.Markup"` → PASS.

```bash
git checkout -- '*.lock.json' 2>/dev/null
git add src/AcDream.App/UI tests/AcDream.App.Tests/UI
git commit -m "feat(ui): <radio> markup element

One choice of several as round buttons or a segmented bar, vertical or
horizontal. A click fires on release over the option it pressed; arrow
keys move the choice while the group has focus, wrapping.

Claude-Session: https://claude.ai/code/session_01UzLnYkJ6ifzKGaeNwgsp8W"
```

---

### Task 4: Theme and drawing

**Goal:** `<radio>` takes the plugin theme, and its draw output is pinned for both styles, both looks, focus and disabled.

**Files:**
- Modify: `src/AcDream.App/UI/PluginMarkupTheme.cs`
- Test: `tests/AcDream.App.Tests/UI/MarkupRadioTests.cs`

**Acceptance Criteria:**
- [ ] Under `theme="plugin"` (Moss) the radio's `ThemePalette` is the palette, its caption colour is `Moss.Text` unless `color` is set, and the modern font (when set) reaches it.
- [ ] Classic radio style: 1 `UiRadioGlyph.ClassicMark` quad with an option selected, 0 with none.
- [ ] Classic segmented: 1 `ClassicSegmentSelected` quad with an option selected, 0 with none.
- [ ] Disabled: the mark draws at `DisabledAlpha`, never at full alpha.
- [ ] Focused: 4 `FocusColor` quads (the outline).
- [ ] Themed radio style with option 0 selected: exactly 6 `Moss.Accent` quads (that ring's 5 + the mark), no Classic mark; themed segmented: at least 1 `Moss.Selected` quad and no `ClassicSegmentSelected`.

**Verify:** `dotnet test tests/AcDream.App.Tests/AcDream.App.Tests.csproj --filter "FullyQualifiedName~MarkupRadioTests"` → all pass.

**Steps:**

- [ ] **Step 1: Write the tests** — append inside `MarkupRadioTests`:

```csharp
    private static UiMarkupRadio MountThemed(string attributes, Binding binding)
    {
        var themes = new PluginUiThemeSettings { Theme = PluginUiTheme.Moss };
        var panel = MarkupDocument.Build(
            "<panel theme=\"plugin\" x=\"0\" y=\"0\" w=\"300\" h=\"100\">"
            + $"<radio x=\"8\" y=\"8\" w=\"200\" h=\"54\" {attributes}/></panel>",
            binding, _ => (1u, 16, 16), themes: themes);
        return panel.Children.OfType<UiMarkupRadio>().Single();
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(-1, 0)]
    public void Radio_ClassicDrawsOneMarkForTheSelection(int selected, int marks)
    {
        var (_, radio) = Mount(Literal, new Binding { Mode = selected });
        var renderer = MarkupDrawProbe.Draw(radio);
        Assert.Equal(marks, MarkupDrawProbe.CountQuads(renderer, UiRadioGlyph.ClassicMark));
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(-1, 0)]
    public void Radio_SegmentedFillsTheSelectedSegment(int selected, int fills)
    {
        var (_, radio) = Mount(Literal + " style=\"segmented\"", new Binding { Mode = selected });
        var renderer = MarkupDrawProbe.Draw(radio);
        Assert.Equal(fills, MarkupDrawProbe.CountQuads(renderer, UiMarkupRadio.ClassicSegmentSelected));
    }

    [Fact]
    public void Radio_DisabledDrawsFaded()
    {
        var (_, radio) = Mount(Literal + " enabled=\"false\"", new Binding { Mode = 1 });
        var renderer = MarkupDrawProbe.Draw(radio);
        var faded = UiRadioGlyph.ClassicMark with { W = UiMarkupToggle.DisabledAlpha };
        Assert.Equal(1, MarkupDrawProbe.CountQuads(renderer, faded));
        Assert.Equal(0, MarkupDrawProbe.CountQuads(renderer, UiRadioGlyph.ClassicMark));
    }

    [Fact]
    public void Radio_FocusDrawsAnOutline()
    {
        var (_, radio) = Mount(Literal, new Binding());
        Focus(radio);
        var renderer = MarkupDrawProbe.Draw(radio);
        Assert.Equal(4, MarkupDrawProbe.CountQuads(renderer, UiMarkupRadio.FocusColor));
    }

    [Fact]
    public void Radio_PluginThemeUsesThePalette()
    {
        var radio = MountThemed(Literal, new Binding { Mode = 0 });
        Assert.Same(PluginUiPalette.Moss, radio.ThemePalette);
        Assert.Equal(PluginUiPalette.Moss.Text, radio.TextColor);
        var renderer = MarkupDrawProbe.Draw(radio);
        Assert.Equal(6, MarkupDrawProbe.CountQuads(renderer, PluginUiPalette.Moss.Accent));
        Assert.Equal(0, MarkupDrawProbe.CountQuads(renderer, UiRadioGlyph.ClassicMark));
    }

    [Fact]
    public void Radio_PluginThemeSegmented()
    {
        var radio = MountThemed(Literal + " style=\"segmented\"", new Binding { Mode = 1 });
        var renderer = MarkupDrawProbe.Draw(radio);
        Assert.True(MarkupDrawProbe.CountQuads(renderer, PluginUiPalette.Moss.Selected) >= 1);
        Assert.Equal(0, MarkupDrawProbe.CountQuads(renderer, UiMarkupRadio.ClassicSegmentSelected));
    }

    [Fact]
    public void Radio_ExplicitColorSurvivesTheTheme()
    {
        var radio = MountThemed(Literal + " color=\"#FFFF0000\"", new Binding());
        Assert.Equal(new Vector4(1f, 0f, 0f, 1f), radio.TextColor);
    }
```

- [ ] **Step 2: Run and see the theme tests fail**

Run: `dotnet test tests/AcDream.App.Tests/AcDream.App.Tests.csproj --filter "FullyQualifiedName~MarkupRadioTests"`
Expected: `Radio_PluginThemeUsesThePalette` and `Radio_PluginThemeSegmented` FAIL (no theme registration); the Classic draw tests pass already.

- [ ] **Step 3: Register the radio in `PluginMarkupTheme.Register`**

Font switch, after the `UiMarkupCheckbox` line:

```csharp
                case UiMarkupRadio radio: Bind(() => radio.DatFont, f => radio.DatFont = f); break;
```

Second switch, after the `case UiMarkupCheckbox checkbox:` section:

```csharp
            case UiMarkupRadio radio:
                var radioText = radio.TextColor;
                panel.AddThemeAction(p => { radio.ThemePalette = p;
                    if (Default("color")) radio.TextColor = p?.Text ?? radioText; });
                break;
```

- [ ] **Step 4: Run the tests**

Run: `dotnet test tests/AcDream.App.Tests/AcDream.App.Tests.csproj --filter "FullyQualifiedName~MarkupRadioTests"`
Expected: PASS. If the themed Accent count isn't 6, count what `UiRadioGlyph.Draw` emits for one selected and two unselected options under Moss before changing anything, and report it.

- [ ] **Step 5: Run all markup tests, then commit**

Run: `dotnet test tests/AcDream.App.Tests/AcDream.App.Tests.csproj --filter "FullyQualifiedName~AcDream.App.Tests.UI.Markup"` → PASS.

```bash
git checkout -- '*.lock.json' 2>/dev/null
git add src/AcDream.App/UI/PluginMarkupTheme.cs tests/AcDream.App.Tests/UI/MarkupRadioTests.cs
git commit -m "feat(ui): plugin theme for <radio>

Claude-Session: https://claude.ai/code/session_01UzLnYkJ6ifzKGaeNwgsp8W"
```

---

### Task 5: Docs

**Goal:** `docs/plugin-ui-markup.md` documents `<radio>`.

**Files:**
- Modify: `docs/plugin-ui-markup.md`

**Acceptance Criteria:**
- [ ] Elements table has a `radio` row after `number`.
- [ ] Bindings table has `radio items`, `radio selected` and `radio onchange` rows after the number rows.
- [ ] The "Clicking a `button`, `tab`, `toggle`, or `checkbox` … without taking keyboard focus" sentence also names `radio`.
- [ ] A `## Radio group` section after `## Number field` covers items, selection, styles, orientation, the click rule, keyboard, and the older-host line.

**Verify:** `grep -c 'radio' docs/plugin-ui-markup.md` → at least 10.

**Steps:**

- [ ] **Step 1: Tables**

Elements table, after the `number` row:

```markdown
| `radio` | One choice of several, as round buttons or a segmented bar | `x y w h items selected onchange orientation style color` |
```

Bindings table, after the `number onchange` row:

```markdown
| `radio items` | `IEnumerable<string>`, or a literal list `a\|b\|c` | throws |
| `radio selected` | `int`, the chosen option (−1 for none) | throws |
| `radio onchange` | `Action<int>`, the option picked | throws |
```

(The `\|` keeps the literal's bars from splitting the table cell.)

- [ ] **Step 2: Focus sentence** — change "Clicking a `button`, `tab`, `toggle`, or `checkbox`" to "Clicking a `button`, `tab`, `toggle`, `checkbox`, or `radio`".

- [ ] **Step 3: Section** — after the `## Number field` section, add:

````markdown
## Radio group

```xml
<radio x="8" y="8" w="200" h="54" items="Melee|Missile|Magic"
       selected="{Mode}" onchange="{SetMode}"/>
<radio x="8" y="70" w="210" h="20" items="{Ranges}" selected="{Range}"
       onchange="{SetRange}" orientation="horizontal" style="segmented"/>
```

One `<radio>` is the whole group: the player picks one of its options.

- `items` is a literal list separated by `|`, or a bound
  `IEnumerable<string>` that the group re-reads every frame.
- `selected` binds an `int`, the chosen option; −1, or any number past the
  last option, draws none chosen.
- `onchange` receives the option the player picks. It isn't called for the
  option already chosen. The plugin stores the choice; the group shows
  whatever `selected` returns next frame. Without `onchange` the group
  only displays.
- `orientation` is `vertical` (the default, options stacked) or
  `horizontal` (side by side). The box is shared equally between options.
- `style` is `radio` (the default: a round button and caption per option)
  or `segmented` (a joined bar of buttons, the chosen one lit, captions
  centred).
- A click picks an option when the button comes up over the option it went
  down on.
- The group is one Tab stop. While it has focus, Up or Left picks the
  previous option and Down or Right the next, wrapping round; with nothing
  chosen, Down or Right picks the first and Up or Left the last.

On a host older than radio groups, a `<radio>` fails the build of its
window as unknown.
````

- [ ] **Step 4: Commit**

```bash
git add docs/plugin-ui-markup.md
git commit -m "docs(ui): document <radio>

Claude-Session: https://claude.ai/code/session_01UzLnYkJ6ifzKGaeNwgsp8W"
```

---

### Task 6: Series gate

**Goal:** Release build clean; no new test failures versus the upstream baseline; every commit builds.

**Files:** none changed.

**Acceptance Criteria:**
- [ ] `dotnet build AcDream.slnx -c Release` → 0 errors, 0 warnings.
- [ ] Failing test names (timings stripped) are a subset of the baseline.
- [ ] Each of the series' commits builds and passes `UI.Markup|UiFieldTests` on its own.
- [ ] Nothing under `docs/superpowers/` on the branch.

**Verify:** `comm -13` of normalised baseline vs after → empty.

**Steps:**

- [ ] **Step 1: Release build and tests**

```bash
export DOTNET_ROOT=$HOME/.dotnet PATH=$HOME/.dotnet:$PATH
cd /Users/davidsmith/code/OpenAC/OpenAC/.worktrees/markup-controls
T=/private/tmp/claude-501/-Users-davidsmith-code-OpenAC-OpenAC
dotnet build AcDream.slnx -c Release 2>&1 | grep -E ': error|: warning|Warning\(s\)|Error\(s\)' | sort -u
for p in tests/AcDream.App.Tests/AcDream.App.Tests.csproj tests/AcDream.Plugin.Tests/AcDream.Plugin.Tests.csproj; do
  dotnet test "$p" -c Release 2>&1 | grep -E '^[[:space:]]+Failed [A-Za-z]'
done | sed -E 's/^[[:space:]]+Failed //; s/ \[[^]]*\]$//' | sort -u > $T/mc3-after.txt
echo "new:"; comm -13 $T/mc-base.txt $T/mc3-after.txt
git checkout -- '*.lock.json' 2>/dev/null; git status --short
```
Expected: `0 Warning(s)`, `0 Error(s)`, nothing after `new:`. (`mc-base.txt` is the normalised upstream baseline from plan 2's gate; if it's missing, rebuild it with `sed -E 's/^[[:space:]]+Failed //; s/ \[[^]]*\]$//' $T/markup-controls-baseline.txt | sort -u > $T/mc-base.txt`.)

- [ ] **Step 2: Each commit builds alone**

```bash
cd /Users/davidsmith/code/OpenAC/OpenAC
for c in $(git rev-list --reverse 345009da..feat/markup-controls); do
  git worktree add -q --detach .worktrees/mc-commit-check $c
  (cd .worktrees/mc-commit-check && dotnet test tests/AcDream.App.Tests/AcDream.App.Tests.csproj -c Release \
     --filter "FullyQualifiedName~AcDream.App.Tests.UI.Markup|FullyQualifiedName~AcDream.App.Tests.UI.UiFieldTests" 2>&1 \
     | grep -E ': error|: warning|Passed!|Failed!' | sort -u | sed "s/^/$(git rev-parse --short $c) /")
  git worktree remove --force .worktrees/mc-commit-check
done
```
Expected: one `Passed!` line per commit, no `error`/`warning` lines.

- [ ] **Step 3: Branch contents**

```bash
cd /Users/davidsmith/code/OpenAC/OpenAC/.worktrees/markup-controls
git log --oneline 345009da..HEAD
git diff --stat 391e709a..HEAD | grep superpowers || echo "no superpowers docs"
```
Expected: the 5 commits of Tasks 1-5; "no superpowers docs".

No commit.
