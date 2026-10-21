# Markup controls, plan 1: builder plumbing + `<checkbox>` Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers-extended-cc:subagent-driven-development (recommended) or superpowers-extended-cc:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add the shared builder plumbing every new markup control uses, then ship `<checkbox>` (bool / tri-state) and the `<column type="checkbox">` list column.

**Architecture:** New element builders live in their own files under `src/AcDream.App/UI/Markup/` and receive a `MarkupBuildContext`; `MarkupDocument.AddElement` only gains a `case` per element. The checkbox is a `UiMarkupCheckbox` widget drawing a `UiCheckBoxGlyph` (square box, pixel tick, dash for indeterminate) in Classic colours or the plugin palette. The list column reuses the existing `Check` column kind with a `Boxed` flag.

**Tech Stack:** C# / .NET 10, xUnit, headless `UiRoot` + `RecordingGpuDevice` tests.

**Spec:** `docs/superpowers/specs/2026-10-21-markup-controls-design.md` (branch `docs/markup-controls-spec`), sections "Delivery", "Shared rules", 1 and 2. This is plan 1 of a series; later series get their own plans.

## Global Constraints

- Work in the worktree `/Users/davidsmith/code/OpenAC/OpenAC/.worktrees/markup-controls`, branch `feat/markup-controls`, created from `upstream/main` 391e709a (v0.1.26). Never merge fork `main` into it; never add files under `docs/superpowers/` to it.
- Toolchain: every shell runs `export DOTNET_ROOT=$HOME/.dotnet PATH=$HOME/.dotnet:$PATH` first (global.json pins SDK 10.0.300). After a build, revert lock-file churn with `git checkout -- '*.lock.json'`; never commit lock files.
- Namespaces: widgets in `AcDream.App.UI`; builders and bind helpers in `AcDream.App.UI.Markup`.
- `<toggle>` and `<column type="check">` must not change behaviour or look.
- Checkbox semantics: `checked` binds `bool` or `bool?` (null = indeterminate); `onchange` is `Action<bool>` receiving the NEW value: unchecked → true, checked → false, indeterminate → true.
- Glyph: 11×11 px (same as `UiCheckLamp.LampSize`); caption at x = 17, as `<toggle>`.
- Commit messages end with the line `Claude-Session: https://claude.ai/code/session_01UzLnYkJ6ifzKGaeNwgsp8W`.

**User decisions (already made):**
- One branch `feat/markup-controls` from upstream, one commit series per control, upstream-portable.
- `<checkbox>` is distinct from `<toggle>`; both stay. `type="check"` keeps the lamp; a new `type="checkbox"` column draws the tick box.
- Plugin dev chooses between toggle and checkbox.

---

## File Structure

| File | Responsibility |
|---|---|
| `src/AcDream.App/UI/Markup/MarkupBuildContext.cs` (new) | What a builder needs from `AddElement`: sprite resolver, dat font, icon resolver, theme-aware colour binder |
| `src/AcDream.App/UI/Markup/MarkupBindings.cs` (new) | New binding helpers (`BindRequiredCheckState`, `BindBoolAction`); later plans add more here |
| `src/AcDream.App/UI/Markup/MarkupCheckbox.cs` (new) | `<checkbox>` builder |
| `src/AcDream.App/UI/UiCheckBoxGlyph.cs` (new) | Draws the 11px tick box in Classic or palette colours |
| `src/AcDream.App/UI/UiMarkupCheckbox.cs` (new) | The checkbox widget |
| `src/AcDream.App/UI/MarkupDocument.cs` | Helpers `private` → `internal`; `case "checkbox"`; `case "checkbox"` column |
| `src/AcDream.App/UI/PluginMarkupTheme.cs` | Theme + font registration for the checkbox |
| `src/AcDream.App/UI/UiMarkupListColumn.cs` | `Boxed` flag on check columns |
| `src/AcDream.App/UI/UiMarkupList.cs` | Draw boxed check cells with the glyph |
| `tests/AcDream.App.Tests/UI/MarkupDrawProbe.cs` (new) | Test helper: render context + count solid quads of a colour |
| `tests/AcDream.App.Tests/UI/MarkupCheckboxTests.cs` (new) | All checkbox and checkbox-column tests |
| `docs/plugin-ui-markup.md` | User docs |

---

### Task 0: Test baseline

**Goal:** Record which tests already fail on the untouched branch, so the final gate compares by name.

**Files:**
- Create: `/private/tmp/claude-501/-Users-davidsmith-code-OpenAC-OpenAC/markup-controls-baseline.txt` (outside the repo)

**Acceptance Criteria:**
- [ ] Baseline file lists every failing test name of `AcDream.App.Tests` and `AcDream.Plugin.Tests` on 391e709a, sorted, one per line (may be empty).

**Verify:** `wc -l /private/tmp/claude-501/-Users-davidsmith-code-OpenAC-OpenAC/markup-controls-baseline.txt` → a line count (any).

**Steps:**

- [ ] **Step 1: Confirm the branch is clean upstream**

```bash
cd /Users/davidsmith/code/OpenAC/OpenAC/.worktrees/markup-controls
git status --short && git log --oneline -1
```
Expected: no changes; `391e709a Merge pull request #235 from eriknihlen/release/0.1.26`.

- [ ] **Step 2: Run both test projects and keep the failures**

```bash
export DOTNET_ROOT=$HOME/.dotnet PATH=$HOME/.dotnet:$PATH
cd /Users/davidsmith/code/OpenAC/OpenAC/.worktrees/markup-controls
for p in tests/AcDream.App.Tests/AcDream.App.Tests.csproj tests/AcDream.Plugin.Tests/AcDream.Plugin.Tests.csproj; do
  dotnet test "$p" -c Release 2>&1 | grep -E '^\s+Failed [A-Za-z]' | sed -E 's/^\s+Failed ([^ ]+).*/\1/'
done | sort -u > /private/tmp/claude-501/-Users-davidsmith-code-OpenAC-OpenAC/markup-controls-baseline.txt
git checkout -- '*.lock.json' 2>/dev/null; git status --short
```
Expected: the file exists; `git status` shows nothing. (Memory notes ~70 Mac failures on 0.1.26 and three flaky App classes; that's the baseline, not a problem.)

No commit.

---

### Task 1: Builder plumbing

**Goal:** New element builders can live in their own files: a `MarkupBuildContext`, the `internal` helpers they call, and a `MarkupBindings` home for new bind helpers.

**Files:**
- Create: `src/AcDream.App/UI/Markup/MarkupBuildContext.cs`
- Create: `src/AcDream.App/UI/Markup/MarkupBindings.cs`
- Modify: `src/AcDream.App/UI/MarkupDocument.cs` (helper visibility, `using`)
- Test: `tests/AcDream.App.Tests/UI/MarkupCheckboxTests.cs` (binding-helper tests only, for now)

**Acceptance Criteria:**
- [ ] `MarkupDocument.F`, `FOr`, `B`, `Color`, `BindString`, `ApplyCommon`, `IsBinding` are `internal static`; nothing else in `MarkupDocument` changes visibility.
- [ ] `MarkupBindings.BindRequiredCheckState` accepts `bool` and `bool?` properties, returns `null` for a null `bool?`, and throws `FormatException` for a missing, literal, or other-typed attribute.
- [ ] `MarkupBindings.BindBoolAction` returns null for a missing attribute and throws for a literal or a non-`Action<bool>` property.
- [ ] Existing markup tests still pass.

**Verify:** `dotnet test tests/AcDream.App.Tests/AcDream.App.Tests.csproj --filter "FullyQualifiedName~AcDream.App.Tests.UI.Markup"` → all pass.

**Steps:**

- [ ] **Step 1: Write the failing tests**

Create `tests/AcDream.App.Tests/UI/MarkupCheckboxTests.cs`:

```csharp
using System.Numerics;
using AcDream.App.UI;
using AcDream.App.UI.Markup;

namespace AcDream.App.Tests.UI;

public class MarkupCheckboxTests
{
    private sealed class Binding
    {
        public bool Loot { get; set; }
        public bool? Mixed { get; set; }
        public int NotBool { get; set; }
        public int Calls { get; private set; }
        public bool? LastChange { get; private set; }
        public Action<bool> SetLoot => v => { Loot = v; LastChange = v; Calls++; };
        public Action<bool> SetMixed => v => { Mixed = v; LastChange = v; Calls++; };
        public Action<int> WrongAction => _ => { };
        public IReadOnlyList<bool> Flags { get; set; } = [true, false];
        public IReadOnlyList<string> Names { get; set; } = ["Drudge", "Mite"];
        public int Toggled { get; private set; } = -1;
        public Action<int> ToggleFlag => row => Toggled = row;
    }

    [Fact]
    public void CheckState_ReadsBoolAndNullableBool()
    {
        var binding = new Binding { Loot = true, Mixed = null };
        Assert.True(MarkupBindings.BindRequiredCheckState("{Loot}", binding, "ctx")());
        Assert.Null(MarkupBindings.BindRequiredCheckState("{Mixed}", binding, "ctx")());
        binding.Mixed = false;
        Assert.False(MarkupBindings.BindRequiredCheckState("{Mixed}", binding, "ctx")());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("true")]
    [InlineData("{NotBool}")]
    [InlineData("{Missing}")]
    public void CheckState_RejectsAnythingButABoolBinding(string? expression)
    {
        var ex = Assert.Throws<FormatException>(
            () => MarkupBindings.BindRequiredCheckState(expression, new Binding(), "checkbox checked"));
        Assert.Contains("checkbox checked", ex.Message);
    }

    [Fact]
    public void BoolAction_MissingIsNullAndBoundInvokes()
    {
        var binding = new Binding();
        Assert.Null(MarkupBindings.BindBoolAction(null, binding, "ctx"));
        MarkupBindings.BindBoolAction("{SetLoot}", binding, "ctx")!(true);
        Assert.True(binding.Loot);
    }

    [Theory]
    [InlineData("SetLoot")]
    [InlineData("{WrongAction}")]
    [InlineData("{Missing}")]
    public void BoolAction_RejectsLiteralsAndWrongTypes(string expression)
    {
        var ex = Assert.Throws<FormatException>(
            () => MarkupBindings.BindBoolAction(expression, new Binding(), "checkbox onchange"));
        Assert.Contains("checkbox onchange", ex.Message);
    }
}
```

- [ ] **Step 2: Run them and see them fail**

Run: `dotnet test tests/AcDream.App.Tests/AcDream.App.Tests.csproj --filter "FullyQualifiedName~MarkupCheckboxTests"`
Expected: build FAILS: `The type or namespace name 'Markup' does not exist in the namespace 'AcDream.App.UI'`.

- [ ] **Step 3: Create `MarkupBuildContext.cs`**

```csharp
using System.Numerics;

namespace AcDream.App.UI.Markup;

/// <summary>
/// Binds a colour attribute the way <c>MarkupDocument.AddElement</c> does,
/// including <c>theme:</c> tokens, against the element's binding object.
/// </summary>
internal delegate void MarkupColorBinder(
    string? expression, Action<Vector4> setValue, Action<Func<Vector4>> setSource);

/// <summary>
/// What an element builder outside <see cref="MarkupDocument"/> gets from the
/// document build: the same resolvers and font every built-in element uses.
/// </summary>
internal sealed record MarkupBuildContext(
    Func<uint, (uint, int, int)> Resolve,
    UiDatFont? DatFont,
    IMarkupIconResolver? Icons,
    MarkupColorBinder BindColor);
```

- [ ] **Step 4: Create `MarkupBindings.cs`**

```csharp
using System.Reflection;

namespace AcDream.App.UI.Markup;

/// <summary>Binding readers and actions the newer markup elements share.</summary>
internal static class MarkupBindings
{
    /// <summary>
    /// A required tri-state binding: a <c>bool</c> property, or a <c>bool?</c>
    /// one whose null means indeterminate.
    /// </summary>
    public static Func<bool?> BindRequiredCheckState(string? expression, object binding, string context)
    {
        if (string.IsNullOrWhiteSpace(expression) || !MarkupDocument.IsBinding(expression))
            throw new FormatException($"{context} must be a bool or bool? binding");
        PropertyInfo? property = binding.GetType().GetProperty(expression[1..^1]);
        if (property is null
            || (property.PropertyType != typeof(bool) && property.PropertyType != typeof(bool?)))
        {
            throw new FormatException(
                $"{expression} did not resolve to a bool or bool? property on "
                + binding.GetType().Name + $" ({context})");
        }
        return () => property.GetValue(binding) as bool?;
    }

    /// <summary>An optional <c>Action&lt;bool&gt;</c>; null when the attribute is absent.</summary>
    public static Action<bool>? BindBoolAction(string? attribute, object binding, string context)
    {
        if (attribute is null)
            return null;
        if (!MarkupDocument.IsBinding(attribute))
            throw new FormatException($"{context} must be an Action<bool> binding");
        PropertyInfo? property = binding.GetType().GetProperty(attribute[1..^1]);
        if (property is null || !typeof(Action<bool>).IsAssignableFrom(property.PropertyType))
        {
            throw new FormatException(
                $"{attribute} did not resolve to an Action<bool> property on "
                + binding.GetType().Name + $" ({context})");
        }
        return value => (property.GetValue(binding) as Action<bool>)?.Invoke(value);
    }
}
```

- [ ] **Step 5: Open up the helpers in `MarkupDocument.cs`**

Change exactly these declarations from `private static` to `internal static` (bodies unchanged):

```csharp
    internal static Func<string?> BindString(string? attribute, object binding)
    internal static bool IsBinding(string value) =>
    internal static void ApplyCommon(
    internal static float F(XElement e, string attr)
    internal static float FOr(XElement e, string attr, float fallback)
    internal static bool B(XElement e, string attr, bool fallback)
    internal static Vector4 Color(string? hex)
```

Add `using AcDream.App.UI.Markup;` after `using AcDream.Plugin.Abstractions;` at the top.


- [ ] **Step 6: Run the tests**

Run: `dotnet test tests/AcDream.App.Tests/AcDream.App.Tests.csproj --filter "FullyQualifiedName~AcDream.App.Tests.UI.Markup"`
Expected: PASS, including the 4 new test methods (13 cases).

- [ ] **Step 7: Commit**

```bash
git checkout -- '*.lock.json' 2>/dev/null
git add src/AcDream.App/UI/Markup src/AcDream.App/UI/MarkupDocument.cs tests/AcDream.App.Tests/UI/MarkupCheckboxTests.cs
git commit -m "refactor(ui): let markup element builders live in their own files

MarkupBuildContext carries what AddElement passes to its elements, and
MarkupBindings holds bind helpers for the new elements; the shared
MarkupDocument helpers they need become internal.

Claude-Session: https://claude.ai/code/session_01UzLnYkJ6ifzKGaeNwgsp8W"
```

---

### Task 2: `<checkbox>` element

**Goal:** A `<checkbox>` markup element with tri-state binding, the new-value `onchange` contract, keyboard activation, disabled fading, and both looks.

**Files:**
- Create: `src/AcDream.App/UI/UiCheckBoxGlyph.cs`
- Create: `src/AcDream.App/UI/UiMarkupCheckbox.cs`
- Create: `src/AcDream.App/UI/Markup/MarkupCheckbox.cs`
- Create: `tests/AcDream.App.Tests/UI/MarkupDrawProbe.cs`
- Modify: `src/AcDream.App/UI/MarkupDocument.cs` (`Context()` local function and `case "checkbox"` in `AddElement`)
- Modify: `src/AcDream.App/UI/PluginMarkupTheme.cs`
- Test: `tests/AcDream.App.Tests/UI/MarkupCheckboxTests.cs`

**Acceptance Criteria:**
- [ ] `<checkbox checked="{bool}" onchange="{Action<bool>}">` builds a `UiMarkupCheckbox`; clicking reports `true` then `false` as the bound value flips.
- [ ] With `checked` bound to a null `bool?`, a click reports `true`.
- [ ] Missing/mistyped `checked`, or mistyped `onchange`, throws `FormatException` at build; no `onchange` means clicks do nothing and don't throw.
- [ ] `enabled="false"` blocks click and keyboard activation; a focused checkbox activates on Space/Enter.
- [ ] Classic draw: 0 tick-colour quads unchecked, 7 checked, 1 indeterminate. Under `theme="plugin"` (Moss) the box fills with the palette `Field` colour.

**Verify:** `dotnet test tests/AcDream.App.Tests/AcDream.App.Tests.csproj --filter "FullyQualifiedName~MarkupCheckboxTests"` → all pass.

**Steps:**

- [ ] **Step 1: Add the draw probe helper**

Create `tests/AcDream.App.Tests/UI/MarkupDrawProbe.cs`:

```csharp
using System.Numerics;
using AcDream.App.Rendering;
using AcDream.App.Rendering.Gpu;
using AcDream.App.Tests.Rendering.Gpu;
using AcDream.App.UI;

namespace AcDream.App.Tests.UI;

/// <summary>Draws a widget headlessly and counts the solid quads it emitted.</summary>
internal static class MarkupDrawProbe
{
    public static TextRenderer Draw(UiElement element, float w = 400f, float h = 300f)
    {
        var renderer = new TextRenderer(new RecordingGpuDevice(), new NullFrameSource(), "unused");
        renderer.Begin(new Vector2(w, h));
        element.DrawSelfAndChildren(new UiRenderContext(renderer, new Vector2(w, h)));
        return renderer;
    }

    /// <summary>Untextured quads (6 vertices × 8 floats) whose colour matches within 0.01.</summary>
    public static int CountQuads(TextRenderer renderer, Vector4 color)
    {
        int count = 0;
        foreach (var s in renderer.DebugSpriteSegmentVerts)
        {
            if (s.Texture != 0u)
                continue;
            for (int q = 0; q + 48 <= s.Verts.Count; q += 48)
            {
                if (MathF.Abs(s.Verts[q + 4] - color.X) < 0.01f
                    && MathF.Abs(s.Verts[q + 5] - color.Y) < 0.01f
                    && MathF.Abs(s.Verts[q + 6] - color.Z) < 0.01f
                    && MathF.Abs(s.Verts[q + 7] - color.W) < 0.01f)
                {
                    count++;
                }
            }
        }
        return count;
    }

    private sealed class NullFrameSource : ICurrentGpuFrameSource
    {
        public IGpuFrame? CurrentFrame => null;
    }
}
```

(Check `MarkupListColumnsTests.MakeContext` for the exact `using`s if the build complains; the helper mirrors it.)

- [ ] **Step 2: Write the failing element tests**

Append inside `MarkupCheckboxTests`:

```csharp
    private static UiMarkupCheckbox BuildOne(string attributes, Binding binding)
    {
        var panel = MarkupDocument.Build(
            $"<panel x=\"0\" y=\"0\" w=\"200\" h=\"100\"><checkbox x=\"4\" y=\"4\" w=\"120\" h=\"16\" text=\"Loot rares\" {attributes}/></panel>",
            binding, _ => (1u, 16, 16));
        return Assert.IsType<UiMarkupCheckbox>(panel.Children[0]);
    }

    private static void Click(UiMarkupCheckbox box) =>
        box.OnEvent(new UiEvent { Type = UiEventType.Click });

    [Fact]
    public void Checkbox_ReportsTheNewValue()
    {
        var binding = new Binding();
        var box = BuildOne("checked=\"{Loot}\" onchange=\"{SetLoot}\"", binding);
        Assert.False(box.State);
        Click(box);
        Assert.Equal(true, binding.LastChange);
        Assert.True(box.State);
        Click(box);
        Assert.Equal(false, binding.LastChange);
        Assert.Equal(2, binding.Calls);
    }

    [Fact]
    public void Checkbox_IndeterminateClicksToTrue()
    {
        var binding = new Binding { Mixed = null };
        var box = BuildOne("checked=\"{Mixed}\" onchange=\"{SetMixed}\"", binding);
        Assert.Null(box.State);
        Click(box);
        Assert.Equal(true, binding.LastChange);
    }

    [Theory]
    [InlineData("")]
    [InlineData("checked=\"{NotBool}\"")]
    [InlineData("checked=\"{Loot}\" onchange=\"{WrongAction}\"")]
    public void Checkbox_BadBindingsThrowAtBuild(string attributes)
    {
        Assert.Throws<FormatException>(() => BuildOne(attributes, new Binding()));
    }

    [Fact]
    public void Checkbox_WithoutOnchangeIsDisplayOnly()
    {
        var binding = new Binding();
        var box = BuildOne("checked=\"{Loot}\"", binding);
        Click(box);
        Assert.False(binding.Loot);
    }

    [Fact]
    public void Checkbox_DisabledFiresNothing()
    {
        var binding = new Binding();
        var box = BuildOne("checked=\"{Loot}\" onchange=\"{SetLoot}\" enabled=\"false\"", binding);
        Click(box);
        box.OnEvent(new UiEvent { Type = UiEventType.FocusGained });
        box.OnEvent(new UiEvent { Type = UiEventType.KeyDown, Data0 = (int)Silk.NET.Input.Key.Space });
        Assert.Equal(0, binding.Calls);
    }

    [Fact]
    public void Checkbox_FocusedSpaceAndEnterActivate()
    {
        var binding = new Binding();
        var box = BuildOne("checked=\"{Loot}\" onchange=\"{SetLoot}\"", binding);
        Assert.True(box.TabStop);
        box.OnEvent(new UiEvent { Type = UiEventType.FocusGained });
        box.OnEvent(new UiEvent { Type = UiEventType.KeyDown, Data0 = (int)Silk.NET.Input.Key.Space });
        Assert.True(binding.Loot);
        box.OnEvent(new UiEvent { Type = UiEventType.KeyDown, Data0 = (int)Silk.NET.Input.Key.Enter });
        Assert.False(binding.Loot);
    }

    [Theory]
    [InlineData(false, 0)]
    [InlineData(true, 7)]
    [InlineData(null, 1)]
    public void Glyph_ClassicDrawsTickDashOrNothing(bool? state, int markQuads)
    {
        var box = new UiMarkupCheckbox { Width = 120f, Height = 16f, CheckedSource = () => state };
        var renderer = MarkupDrawProbe.Draw(box);
        Assert.Equal(markQuads, MarkupDrawProbe.CountQuads(renderer, UiCheckBoxGlyph.ClassicMark));
    }

    [Fact]
    public void Checkbox_PluginThemeUsesThePalette()
    {
        var themes = new PluginUiThemeSettings { Theme = PluginUiTheme.Moss };
        var panel = MarkupDocument.Build(
            "<panel theme=\"plugin\" x=\"0\" y=\"0\" w=\"200\" h=\"100\">"
            + "<checkbox x=\"4\" y=\"4\" w=\"120\" h=\"16\" text=\"Loot\" checked=\"{Loot}\"/></panel>",
            new Binding(), _ => (1u, 16, 16), themes: themes);
        var box = Assert.IsType<UiMarkupCheckbox>(panel.Children[^1]);
        Assert.Same(PluginUiPalette.Moss, box.ThemePalette);
        var renderer = MarkupDrawProbe.Draw(box);
        Assert.True(MarkupDrawProbe.CountQuads(renderer, PluginUiPalette.Moss.Field) >= 1);
        Assert.Equal(0, MarkupDrawProbe.CountQuads(renderer, UiCheckBoxGlyph.ClassicFill));
    }
```

- [ ] **Step 3: Run and see them fail**

Run: `dotnet test tests/AcDream.App.Tests/AcDream.App.Tests.csproj --filter "FullyQualifiedName~MarkupCheckboxTests"`
Expected: build FAILS: `UiMarkupCheckbox` / `UiCheckBoxGlyph` not found.

- [ ] **Step 4: Create `UiCheckBoxGlyph.cs`**

```csharp
using System.Numerics;

namespace AcDream.App.UI;

/// <summary>
/// The markup checkbox's 11px square: an empty box, a pixel tick, or a dash
/// for indeterminate. The retail client draws its checkboxes as lamps
/// (<see cref="UiCheckLamp"/>), so Classic borrows the lamp's colours for a
/// square box rather than a dat sprite.
/// </summary>
internal static class UiCheckBoxGlyph
{
    public const float Size = 11f;

    public static readonly Vector4 ClassicBorder = UiCheckLamp.UncheckedInner;
    public static readonly Vector4 ClassicFill = new(0.10f, 0.09f, 0.06f, 1f);
    public static readonly Vector4 ClassicMark = UiCheckLamp.CheckedInner;

    /// <summary>The tick's 2×2 steps, left arm down then right arm up, in box pixels.</summary>
    private static readonly (float X, float Y)[] Tick =
        [(2f, 5f), (3f, 6f), (4f, 7f), (5f, 6f), (6f, 5f), (7f, 4f), (8f, 3f)];

    public static void Draw(UiRenderContext ctx, float x, float y, bool? state, PluginUiPalette? palette)
    {
        Vector4 fill = palette?.Field ?? ClassicFill;
        Vector4 border = palette is { } p ? (state == false ? p.Border : p.Accent) : ClassicBorder;
        Vector4 mark = palette?.Accent ?? ClassicMark;

        ctx.DrawFill(x, y, Size, Size, fill);
        ctx.DrawRectOutline(x, y, Size, Size, border, 1f);
        if (state is null)
        {
            ctx.DrawFill(x + 3f, y + 4f, 5f, 3f, mark);
        }
        else if (state == true)
        {
            foreach (var (dx, dy) in Tick)
                ctx.DrawFill(x + dx, y + dy, 2f, 2f, mark);
        }
    }
}
```

- [ ] **Step 5: Create `UiMarkupCheckbox.cs`**

```csharp
using System.Numerics;

namespace AcDream.App.UI;

/// <summary>
/// A markup checkbox: a tick box and caption. Unlike <see cref="UiMarkupToggle"/>
/// it can be indeterminate, and it reports the value a click asks for rather
/// than leaving the plugin to flip its own.
/// </summary>
public sealed class UiMarkupCheckbox : UiElement
{
    public PluginUiPalette? ThemePalette { get; set; }

    public string Text { get; set; } = string.Empty;
    public Func<string?>? TextSource { get; set; }

    /// <summary>The bound state; null is indeterminate.</summary>
    public Func<bool?>? CheckedSource { get; set; }
    public UiDatFont? DatFont { get; set; }
    public Vector4 TextColor { get; set; } = new(0.86f, 0.84f, 0.74f, 1f);

    /// <summary>Re-read every frame in place of <see cref="TextColor"/>.</summary>
    public Func<Vector4>? TextColorSource { get; set; }

    /// <summary>Called with the new value a click or key press asks for.</summary>
    public Action<bool>? Changed { get; set; }

    public bool? State => CheckedSource is null ? false : CheckedSource();

    /// <summary>Unchecked and indeterminate become checked; checked becomes unchecked.</summary>
    public static bool NextValue(bool? state) => state != true;

    public override bool HandlesClick => true;

    private readonly UiKeyboardActivation _keyboardActivation = new();

    private void Activate()
    {
        if (Enabled)
            Changed?.Invoke(NextValue(State));
    }

    public override bool OnEvent(in UiEvent e)
    {
        if (_keyboardActivation.HandleEvent(in e, TabStop, Enabled, Activate))
            return true;
        if (e.Type != UiEventType.Click || !Enabled)
            return false;
        Activate();
        return true;
    }

    protected override void OnDraw(UiRenderContext ctx)
    {
        if (_keyboardActivation.Focused)
            ctx.DrawRectOutline(0f, 0f, Width, Height, new Vector4(1f, 0.82f, 0.25f, 1f), 1f);

        float boxY = MathF.Max(1f, (Height - UiCheckBoxGlyph.Size) * 0.5f);
        if (!Enabled) ctx.PushAlpha(UiMarkupToggle.DisabledAlpha);
        try
        {
            UiCheckBoxGlyph.Draw(ctx, 1f, boxY, State, ThemePalette);
        }
        finally
        {
            if (!Enabled) ctx.PopAlpha();
        }

        string caption = TextSource?.Invoke() ?? Text;
        Vector4 textColor = TextColorSource?.Invoke() ?? TextColor;
        Vector4 color = Enabled ? textColor : textColor with { W = UiMarkupToggle.DisabledAlpha };
        float y = DatFont is { } font ? (Height - font.LineHeight) * 0.5f : 1f;
        if (DatFont is { } dat)
            ctx.DrawStringDat(dat, caption, 17f, y, color, outline: ThemePalette is null);
        else
            ctx.DrawString(caption, 17f, y, color);
    }
}
```

- [ ] **Step 6: Create the builder `Markup/MarkupCheckbox.cs`**

```csharp
using System.Xml.Linq;

namespace AcDream.App.UI.Markup;

internal static class MarkupCheckbox
{
    public static UiMarkupCheckbox Build(XElement el, object binding, MarkupBuildContext context)
    {
        string? caption = (string?)el.Attribute("text");
        var box = new UiMarkupCheckbox
        {
            AcceptsFocus = true,
            FocusOnMouseClick = false,
            TabStop = true,
            Left = MarkupDocument.F(el, "x"),
            Top = MarkupDocument.F(el, "y"),
            Width = MarkupDocument.F(el, "w"),
            Height = MarkupDocument.F(el, "h"),
            Text = caption ?? string.Empty,
            TextSource = MarkupDocument.BindString(caption, binding),
            CheckedSource = MarkupBindings.BindRequiredCheckState(
                (string?)el.Attribute("checked"), binding, "checkbox checked"),
            Changed = MarkupBindings.BindBoolAction(
                (string?)el.Attribute("onchange"), binding, "checkbox onchange"),
            DatFont = context.DatFont,
        };
        string? color = (string?)el.Attribute("color");
        if (color is not null)
            box.TextColor = MarkupDocument.Color(color);
        context.BindColor(color, value => box.TextColor = value, source => box.TextColorSource = source);
        MarkupDocument.ApplyCommon(box, el, binding);
        return box;
    }
}
```

- [ ] **Step 7: Wire the element into `MarkupDocument.AddElement`**

In `AddElement`, directly after the closing brace of the local function `BindColorSource` and before `switch (el.Name.LocalName)`, add:

```csharp
        MarkupBuildContext Context() => new(
            resolve, datFont, icons,
            (expression, setValue, setSource) => BindColorSource(expression, binding, setValue, setSource));
```

Then, directly before `case "slider":`, add:

```csharp
            case "checkbox":
                parent.AddChild(MarkupCheckbox.Build(el, binding, Context()));
                break;
```

- [ ] **Step 8: Register the theme in `PluginMarkupTheme.Register`**

In the font `switch` (the one inside `if (panel.ModernFont is { } modern)`), after the `UiMarkupToggle` line add:

```csharp
                case UiMarkupCheckbox checkbox: Bind(() => checkbox.DatFont, f => checkbox.DatFont = f); break;
```

In the second `switch`, after the `case UiMarkupToggle toggle:` section add:

```csharp
            case UiMarkupCheckbox checkbox:
                var checkboxText = checkbox.TextColor;
                panel.AddThemeAction(p => { checkbox.ThemePalette = p;
                    if (Default("color")) checkbox.TextColor = p?.Text ?? checkboxText; });
                break;
```

- [ ] **Step 9: Run the tests**

Run: `dotnet test tests/AcDream.App.Tests/AcDream.App.Tests.csproj --filter "FullyQualifiedName~MarkupCheckboxTests"`
Expected: PASS. If `Checkbox_PluginThemeUsesThePalette` can't find the box at `Children[^1]` (a themed root may add chrome children), find it with `panel.Children.OfType<UiMarkupCheckbox>().Single()` instead.

- [ ] **Step 10: Commit**

```bash
git checkout -- '*.lock.json' 2>/dev/null
git add src/AcDream.App/UI tests/AcDream.App.Tests/UI
git commit -m "feat(ui): <checkbox> markup element

A tick box with an optional indeterminate state (bool? null). onchange
gets the new value: unchecked and indeterminate become checked. Draws in
Classic colours or the plugin palette; <toggle> is unchanged.

Claude-Session: https://claude.ai/code/session_01UzLnYkJ6ifzKGaeNwgsp8W"
```

---

### Task 3: `<column type="checkbox">`

**Goal:** List columns can draw tick boxes instead of lamps, with the same bindings and click behaviour as `type="check"`.

**Files:**
- Modify: `src/AcDream.App/UI/UiMarkupListColumn.cs` (`Boxed`, `Check(..., boxed)`)
- Modify: `src/AcDream.App/UI/UiMarkupList.cs` (`DrawCheckCell` and its call site)
- Modify: `src/AcDream.App/UI/MarkupDocument.cs` (`BuildListColumn`)
- Test: `tests/AcDream.App.Tests/UI/MarkupCheckboxTests.cs`

**Acceptance Criteria:**
- [ ] `type="checkbox"` builds a `Check`-kind column with `Boxed == true`; `type="check"` gives `Boxed == false`.
- [ ] Missing `values` / `onchange` on a checkbox column throws with a message naming `type="checkbox"`.
- [ ] Classic draw of a checkbox column with one ticked row emits 7 `ClassicMark` quads; the same data in a `check` column emits 1 (the lamp's inner fill).
- [ ] Clicking a checkbox cell calls the column's `onchange` with the row index.
- [ ] The unknown-type error lists `text, check, checkbox, icon, or button`.

**Verify:** `dotnet test tests/AcDream.App.Tests/AcDream.App.Tests.csproj --filter "FullyQualifiedName~AcDream.App.Tests.UI.Markup"` → all pass.

**Steps:**

- [ ] **Step 1: Write the failing tests**

Append inside `MarkupCheckboxTests`:

```csharp
    private static UiMarkupList BuildList(string columnType, Binding binding, string extra = "")
    {
        var panel = MarkupDocument.Build(
            "<panel x=\"0\" y=\"0\" w=\"200\" h=\"100\">"
            + "<list x=\"0\" y=\"0\" w=\"120\" h=\"60\" rowheight=\"18\" selected=\"{NotBool}\">"
            + $"<column type=\"{columnType}\" width=\"20\" {extra}/>"
            + "<column type=\"text\" items=\"{Names}\"/>"
            + "</list></panel>",
            binding, _ => (1u, 16, 16));
        return Assert.IsType<UiMarkupList>(panel.Children[0]);
    }

    [Theory]
    [InlineData("checkbox", true)]
    [InlineData("check", false)]
    public void Column_CheckboxIsABoxedCheckColumn(string type, bool boxed)
    {
        var list = BuildList(type, new Binding(), "values=\"{Flags}\" onchange=\"{ToggleFlag}\"");
        var column = list.Columns![0];
        Assert.Equal(UiMarkupListColumnKind.Check, column.Kind);
        Assert.Equal(boxed, column.Boxed);
    }

    [Theory]
    [InlineData("onchange=\"{ToggleFlag}\"")]
    [InlineData("values=\"{Flags}\"")]
    public void Column_CheckboxRequiresValuesAndOnchange(string extra)
    {
        var ex = Assert.Throws<FormatException>(() => BuildList("checkbox", new Binding(), extra));
        Assert.Contains("type=\"checkbox\"", ex.Message);
    }

    [Theory]
    [InlineData("checkbox", 7)]
    [InlineData("check", 1)]
    public void Column_DrawsTickBoxesOrLamps(string type, int markQuads)
    {
        var list = BuildList(type, new Binding(), "values=\"{Flags}\" onchange=\"{ToggleFlag}\"");
        var renderer = MarkupDrawProbe.Draw(list);
        Assert.Equal(markQuads, MarkupDrawProbe.CountQuads(renderer, UiCheckBoxGlyph.ClassicMark));
    }

    [Fact]
    public void Column_CheckboxClickReportsTheRow()
    {
        var binding = new Binding();
        var panel = MarkupDocument.Build(
            "<panel x=\"0\" y=\"0\" w=\"200\" h=\"100\">"
            + "<list x=\"0\" y=\"0\" w=\"120\" h=\"60\" rowheight=\"18\" selected=\"{NotBool}\">"
            + "<column type=\"checkbox\" width=\"20\" values=\"{Flags}\" onchange=\"{ToggleFlag}\"/>"
            + "<column type=\"text\" items=\"{Names}\"/>"
            + "</list></panel>",
            binding, _ => (1u, 16, 16));
        var root = new UiRoot { Width = 800, Height = 600 };
        root.AddChild(panel);
        root.OnMouseDown(UiMouseButton.Left, 8, 18 + 6);
        root.OnMouseUp(UiMouseButton.Left, 8, 18 + 6);
        Assert.Equal(1, binding.Toggled);
    }

    [Fact]
    public void Column_UnknownTypeListsCheckbox()
    {
        var ex = Assert.Throws<FormatException>(() => BuildList("lamp", new Binding()));
        Assert.Contains("text, check, checkbox, icon, or button", ex.Message);
    }
```

`selected="{NotBool}"` binds the `int` property the list requires. If `Column_CheckboxClickReportsTheRow` misses, the panel's content origin isn't (0,0); read how `MarkupListColumnsTests` clicks a check cell and use the same coordinates.

- [ ] **Step 2: Run and see them fail**

Run: `dotnet test tests/AcDream.App.Tests/AcDream.App.Tests.csproj --filter "FullyQualifiedName~MarkupCheckboxTests"`
Expected: build FAILS: `'UiMarkupListColumn' does not contain a definition for 'Boxed'`.

- [ ] **Step 3: Add `Boxed` to `UiMarkupListColumn`**

After `public Action<int>? CheckChanged { get; internal init; }` add:

```csharp
    /// <summary>A check column drawn as tick boxes (<c>type="checkbox"</c>) instead of lamps.</summary>
    public bool Boxed { get; internal init; }
```

Replace the `Check` factory with:

```csharp
    public static UiMarkupListColumn Check(
        float width,
        Func<IReadOnlyList<bool>> checkSource,
        Action<int> onChange,
        bool isAutoWidth = false,
        bool boxed = false) => new()
    {
        Kind = UiMarkupListColumnKind.Check,
        Width = width,
        IsAutoWidth = isAutoWidth,
        CheckSource = checkSource,
        CheckChanged = onChange,
        Boxed = boxed,
    };
```

- [ ] **Step 4: Draw boxed cells in `UiMarkupList`**

Change the draw call in the column switch to:

```csharp
                        case UiMarkupListColumnKind.Check:
                            DrawCheckCell(context, _cachedCheckRows[c], index, cellX, cellW, y, columns[c].Boxed);
                            break;
```

Replace `DrawCheckCell` with:

```csharp
    private void DrawCheckCell(
        UiRenderContext context, IReadOnlyList<bool>? flags, int index, float cellX, float cellW, float y,
        bool boxed)
    {
        bool isChecked = flags is not null && index < flags.Count && flags[index];
        float extent = MathF.Max(0f, cellW - 2f);
        float lampX = cellX + 1f + MathF.Max(0f, extent - UiCheckLamp.LampSize) * 0.5f;
        float lampY = y + MathF.Max(1f, (RowHeight - UiCheckLamp.LampSize) * 0.5f);
        if (boxed) UiCheckBoxGlyph.Draw(context, lampX, lampY, isChecked, ThemePalette);
        else if (ThemePalette is { } p) p.DrawCheck(context, lampX, lampY, isChecked);
        else UiCheckLamp.Draw(context, lampX, lampY, isChecked);
    }
```

- [ ] **Step 5: Build the column in `MarkupDocument.BuildListColumn`**

Replace the `case "check":` block with:

```csharp
            case "check":
            case "checkbox":
            {
                var checkSource = BindBoolList(
                    (string?)columnEl.Attribute("values"), binding, ColumnContext(index, type, "values"));
                var onChange = BindRequiredIntAction(
                    (string?)columnEl.Attribute("onchange"), binding, ColumnContext(index, type, "onchange"));
                return UiMarkupListColumn.Check(width, checkSource, onChange, isAutoWidth, boxed: type == "checkbox");
            }
```

And the `default:` message:

```csharp
                throw new FormatException(
                    $"column[{index}] has unknown type=\"{type}\" (expected text, check, checkbox, icon, or button)");
```

Then check nothing asserted the old message: `grep -rn "expected text, check, icon" tests src` → no hits (none existed on 391e709a).

- [ ] **Step 6: Run all markup tests**

Run: `dotnet test tests/AcDream.App.Tests/AcDream.App.Tests.csproj --filter "FullyQualifiedName~AcDream.App.Tests.UI.Markup"`
Expected: PASS (new tests and every existing `MarkupListColumnsTests` case).

- [ ] **Step 7: Commit**

```bash
git checkout -- '*.lock.json' 2>/dev/null
git add src/AcDream.App/UI tests/AcDream.App.Tests/UI
git commit -m "feat(ui): checkbox list column

<column type=\"checkbox\"> takes the same values/onchange as type=\"check\"
and draws tick boxes instead of lamps.

Claude-Session: https://claude.ai/code/session_01UzLnYkJ6ifzKGaeNwgsp8W"
```

---

### Task 4: Docs

**Goal:** `docs/plugin-ui-markup.md` documents `<checkbox>` and the checkbox column.

**Files:**
- Modify: `docs/plugin-ui-markup.md`

**Acceptance Criteria:**
- [ ] Elements table has a `checkbox` row; the `toggle` row says "On/off lamp" instead of "Checkbox".
- [ ] Bindings table covers `checkbox checked` (`bool` or `bool?`, throws) and `checkbox onchange` (`Action<bool>`, throws).
- [ ] A `## Checkbox` section explains the tri-state and new-value rules and the toggle/checkbox choice.
- [ ] Columns section lists `checkbox` as a type with the same bindings as `check`.

**Verify:** `grep -c 'checkbox' docs/plugin-ui-markup.md` → at least 8.

**Steps:**

- [ ] **Step 1: Edit the tables**

In the Bindings table, change the `toggle checked` row's attribute cell to keep `toggle checked` there, and add after the `list onchange` row:

```markdown
| `checkbox checked` | `bool`, or `bool?` where null is indeterminate | throws |
| `checkbox onchange` | `Action<bool>`, the new value | throws |
```

In the Elements table, change the `toggle` row's purpose to `On/off lamp` and add after it:

```markdown
| `checkbox` | Tick box, optionally indeterminate | `x y w h text checked onchange color` |
```

In the Columns attribute table, change the `type` row's binding cell to ``text`, `check`, `checkbox`, `icon`, or `button`` and the `values`/`onchange` rows for `check` to say `check`, `checkbox`.

- [ ] **Step 2: Add the section**

After the paragraph that starts "`menu style` and `slider style`" add:

````markdown
## Checkbox

```xml
<checkbox x="8" y="8" w="140" h="16" text="Loot rares"
          checked="{LootRares}" onchange="{SetLootRares}"/>
```

A `<checkbox>` is a tick box; a `<toggle>` is an on/off lamp. Both stay
available: use whichever reads right for the setting.

- `checked` binds a `bool`, or a `bool?` whose `null` draws the box
  indeterminate (a dash), e.g. a "select all" over a mix of rows.
- `onchange` receives the value the click asks for, not a bare "clicked":
  unchecked and indeterminate become `true`, checked becomes `false`. The
  plugin stores it; the box shows whatever `checked` returns next frame.
  Without `onchange` the box only displays.
- Tab focuses it; Space or Enter activates it, as for `<toggle>`.

In a list, `<column type="checkbox">` takes the same `values` and
`onchange` as `type="check"` and draws tick boxes instead of lamps.
````

In the Columns section's closing paragraph, change "A `check` cell draws the same lamp glyph as `<toggle>`." to "A `check` cell draws the same lamp glyph as `<toggle>`; a `checkbox` cell draws the `<checkbox>` tick box."

- [ ] **Step 3: Commit**

```bash
git add docs/plugin-ui-markup.md
git commit -m "docs(ui): document <checkbox> and the checkbox column

Claude-Session: https://claude.ai/code/session_01UzLnYkJ6ifzKGaeNwgsp8W"
```

---

### Task 5: Series gate

**Goal:** The branch builds clean in Release and introduces no new test failures versus the Task 0 baseline.

**Files:** none changed.

**Acceptance Criteria:**
- [ ] `dotnet build AcDream.slnx -c Release` → 0 errors, 0 warnings.
- [ ] Failing test names in `AcDream.App.Tests` + `AcDream.Plugin.Tests` are a subset of the baseline (`comm -13` prints nothing), after re-running any of the three known-flaky classes alone.
- [ ] `git log --oneline 391e709a..HEAD` shows the 4 commits of Tasks 1-4 and nothing under `docs/superpowers/`.

**Verify:** `comm -13 baseline.txt after.txt` → empty.

**Steps:**

- [ ] **Step 1: Release build**

```bash
export DOTNET_ROOT=$HOME/.dotnet PATH=$HOME/.dotnet:$PATH
cd /Users/davidsmith/code/OpenAC/OpenAC/.worktrees/markup-controls
dotnet build AcDream.slnx -c Release 2>&1 | tail -3
```
Expected: `0 Warning(s)` and `0 Error(s)`.

- [ ] **Step 2: Tests versus baseline**

```bash
for p in tests/AcDream.App.Tests/AcDream.App.Tests.csproj tests/AcDream.Plugin.Tests/AcDream.Plugin.Tests.csproj; do
  dotnet test "$p" -c Release 2>&1 | grep -E '^\s+Failed [A-Za-z]' | sed -E 's/^\s+Failed ([^ ]+).*/\1/'
done | sort -u > /private/tmp/claude-501/-Users-davidsmith-code-OpenAC-OpenAC/markup-controls-after.txt
cd /private/tmp/claude-501/-Users-davidsmith-code-OpenAC-OpenAC && comm -13 markup-controls-baseline.txt markup-controls-after.txt
```
Expected: no output. Anything printed is a new failure: if it's in `GraphicalPluginSessionTests`, `LiveEntityNetworkBranchRoutingTests` or `GameWindowRenderLeafCompositionTests`, re-run that class alone with `--filter "FullyQualifiedName~<Class>"`; otherwise fix it.

- [ ] **Step 3: Branch contents**

```bash
cd /Users/davidsmith/code/OpenAC/OpenAC/.worktrees/markup-controls
git checkout -- '*.lock.json' 2>/dev/null; git status --short
git log --oneline 391e709a..HEAD
git diff --stat 391e709a..HEAD | grep superpowers || echo "no superpowers docs"
```
Expected: clean tree, 4 commits, "no superpowers docs".

No commit. Do not push without the user's go-ahead.
