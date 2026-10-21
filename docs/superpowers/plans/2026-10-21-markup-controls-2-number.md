# Markup controls, plan 2: `<number>` Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers-extended-cc:subagent-driven-development (recommended) or superpowers-extended-cc:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Ship the `<number>` markup element: a one-line field that only reports valid, clamped, rounded numbers, with spin buttons, Up/Down keys, focused-only mouse wheel and Shift×10.

**Architecture:** `UiMarkupNumber` is a container that owns a `UiField` child (all text editing, focus, Tab, Enter) and draws two spin buttons in its right 14px. Values travel as `decimal` inside the widget; `MarkupBindings` converts to and from the bound `int`/`long`/`float`/`double`/`decimal`. `UiField` gains one hook, `OnEscape`, so the number can tell "Escape: restore" from "focus lost: commit". The plugin-theme code for `UiField` moves into a helper so the number's inner field is themed the same way.

**Tech Stack:** C# / .NET 10, xUnit, headless `UiRoot` + `RecordingGpuDevice` tests.

**Spec:** `docs/superpowers/specs/2026-10-21-markup-controls-design.md` (branch `docs/markup-controls-spec`), "Shared rules" and section 3. Plan 1 (`2026-10-21-markup-controls-1-checkbox.md`) built the plumbing this uses.

## Global Constraints

- Worktree `/Users/davidsmith/code/OpenAC/OpenAC/.worktrees/markup-controls`, branch `feat/markup-controls` (HEAD a7db16ed, from upstream 391e709a). Never merge fork `main` in; never add `docs/superpowers/` files there. Push only when the controller says so.
- Every shell: `export DOTNET_ROOT=$HOME/.dotnet PATH=$HOME/.dotnet:$PATH`. After building, `git checkout -- '*.lock.json'`; never commit lock files.
- Every commit must build and pass its own tests (the branch goes upstream; reviewers bisect).
- Commit messages end with `Claude-Session: https://claude.ai/code/session_01UzLnYkJ6ifzKGaeNwgsp8W`.
- Binding helper convention (MarkupBindings `<summary>`): `BindRequired*` throw when the attribute is absent or mistyped; other `Bind*` return null when absent and throw when present but mistyped; `context` is `"<element> <attribute>"`.
- Builders return exactly one element; `AddElement` adds it (theme registration depends on that).
- `<number>` contract: `value` binds `int`, `long`, `float`, `double` or `decimal` (required); `onchange` is `Action<T>` of the same `T` (required); `min`/`max` default to the type's range (`float`/`double` limited to `decimal`'s ±7.9×10^28); `step` default 1, must be > 0; `decimals` default 0 for `int`/`long` (any other value throws) and 2 otherwise, range 0-10; `max < min` throws.
- Behaviour: commit on Enter or focus loss (parse invariant culture, round half away from zero to `decimals`, then clamp, report only if the value changed, then show the bound value). Escape restores the bound value. Unparseable text on commit restores. Spin buttons repeat after 0.40 s every 0.06 s while held. Up/Down step while focused; the wheel steps only while focused; Shift steps ten times. Text that doesn't parse or is out of range draws in `UiMarkupNumber.ErrorColor`. Clicking a spin button doesn't take keyboard focus.
- Spin area is the right `UiMarkupNumber.SpinWidth` = 14 px; top half steps up, bottom half down.

**User decisions (already made):**
- One branch, one commit series per control, upstream-portable (plan 1 header).
- `<number>` design approved in brainstorming: `value min max step decimals onchange`; typing filtered; commit on Enter/blur with clamp; Escape restores; ▲▼ auto-repeat; Up/Down; Shift×10; wheel only while focused.

---

## File Structure

| File | Responsibility |
|---|---|
| `src/AcDream.App/UI/UiField.cs` | New `OnEscape` hook |
| `src/AcDream.App/UI/Markup/MarkupBindings.cs` | Number reader/action, type range helpers |
| `src/AcDream.App/UI/UiMarkupNumber.cs` (new) | The number widget: field child, commit/step logic, spin buttons |
| `src/AcDream.App/UI/Markup/MarkupNumber.cs` (new) | `<number>` builder and attribute validation |
| `src/AcDream.App/UI/MarkupDocument.cs` | `case "number"` |
| `src/AcDream.App/UI/PluginMarkupTheme.cs` | `ThemeField` helper; number font + theme cases |
| `tests/AcDream.App.Tests/UI/UiFieldTests.cs` | `OnEscape` test |
| `tests/AcDream.App.Tests/UI/MarkupNumberTests.cs` (new) | Binding, behaviour, draw and theme tests |
| `docs/plugin-ui-markup.md` | User docs |

---

### Task 1: `UiField.OnEscape`

**Goal:** A `UiField` can be told that Escape, rather than another focus change, is ending the edit.

**Files:**
- Modify: `src/AcDream.App/UI/UiField.cs`
- Test: `tests/AcDream.App.Tests/UI/UiFieldTests.cs`

**Acceptance Criteria:**
- [ ] `UiField.OnEscape` (`Action?`) runs when Escape is pressed in the focused field, before `OnFocusLost`, and focus still ends.
- [ ] With `OnEscape` unset, Escape behaves exactly as before.

**Verify:** `dotnet test tests/AcDream.App.Tests/AcDream.App.Tests.csproj --filter "FullyQualifiedName~AcDream.App.Tests.UI.UiFieldTests"` → all pass.

**Steps:**

- [ ] **Step 1: Write the failing test** — append inside `public class UiFieldTests`:

```csharp
    [Fact]
    public void Escape_RunsOnEscapeBeforeFocusIsLost()
    {
        var order = new List<string>();
        var field = new UiField
        {
            Width = 100, Height = 20,
            OnEscape = () => order.Add("escape"),
            OnFocusLost = _ => order.Add("lost"),
        };
        var root = new UiRoot { Width = 400, Height = 300 };
        root.AddChild(field);
        root.SetKeyboardFocus(field);

        root.OnKeyDown((int)Silk.NET.Input.Key.Escape);

        Assert.Equal(new[] { "escape", "lost" }, order);
        Assert.Null(root.KeyboardFocus);
    }
```

- [ ] **Step 2: Run it and see it fail**

Run: `dotnet test tests/AcDream.App.Tests/AcDream.App.Tests.csproj --filter "FullyQualifiedName~UiFieldTests.Escape_RunsOnEscapeBeforeFocusIsLost"`
Expected: build FAILS: `'UiField' does not contain a definition for 'OnEscape'`.

- [ ] **Step 3: Add the hook**

After `public Action<string>? OnFocusLost { get; set; }` add:

```csharp
    /// <summary>
    /// Runs when Escape ends the edit, before focus is released and
    /// <see cref="OnFocusLost"/> fires, so an owner can treat Escape as
    /// "cancel" rather than as an ordinary focus loss.
    /// </summary>
    public Action? OnEscape { get; set; }
```

In `OnEvent`, change the Escape case to:

```csharp
                    case Silk.NET.Input.Key.Escape:
                        OnEscape?.Invoke();
                        FindRoot()?.SetKeyboardFocus(null);
                        return true;
```

- [ ] **Step 4: Run the field tests**

Run: `dotnet test tests/AcDream.App.Tests/AcDream.App.Tests.csproj --filter "FullyQualifiedName~AcDream.App.Tests.UI.UiFieldTests"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git checkout -- '*.lock.json' 2>/dev/null
git add src/AcDream.App/UI/UiField.cs tests/AcDream.App.Tests/UI/UiFieldTests.cs
git commit -m "feat(ui): UiField.OnEscape hook

Lets an owner tell Escape from any other focus loss, which a number
field needs to restore its value instead of committing the text.

Claude-Session: https://claude.ai/code/session_01UzLnYkJ6ifzKGaeNwgsp8W"
```

---

### Task 2: Number bindings

**Goal:** `MarkupBindings` reads a bound `int`/`long`/`float`/`double`/`decimal` as `decimal` and writes a `decimal` back through the matching `Action<T>`.

**Files:**
- Modify: `src/AcDream.App/UI/Markup/MarkupBindings.cs`
- Test: `tests/AcDream.App.Tests/UI/MarkupNumberTests.cs` (create)

**Acceptance Criteria:**
- [ ] `BindRequiredNumberReader` returns a reader and the property's type for each of the five types; anything else, a literal or a missing attribute throws `FormatException` naming the context.
- [ ] A `double` of NaN reads as 0; ±infinity and values beyond `decimal`'s range read as `decimal.MaxValue`/`MinValue`.
- [ ] `BindRequiredNumberAction` requires `Action<T>` for exactly the value's `T` and converts the `decimal` to `T`.
- [ ] `NumberRange` gives `int`/`long` their own range and `decimal`'s range for the others; `IsIntegralNumber` is true only for `int` and `long`.

**Verify:** `dotnet test tests/AcDream.App.Tests/AcDream.App.Tests.csproj --filter "FullyQualifiedName~MarkupNumberTests"` → all pass.

**Steps:**

- [ ] **Step 1: Write the failing tests** — create `tests/AcDream.App.Tests/UI/MarkupNumberTests.cs`:

```csharp
using System.Linq;
using AcDream.App.UI;
using AcDream.App.UI.Markup;
using Silk.NET.Input;

namespace AcDream.App.Tests.UI;

public class MarkupNumberTests
{
    private sealed class Binding
    {
        public int Count { get; set; } = 10;
        public int Calls { get; private set; }
        public Action<int> SetCount => v => { Count = v; Calls++; };
        public double Ratio { get; set; } = 0.5;
        public Action<double> SetRatio => v => Ratio = v;
        public long Big { get; set; } = 5;
        public Action<long> SetBig => v => Big = v;
        public decimal Money { get; set; } = decimal.MaxValue - 0.5m;
        public Action<decimal> SetMoney => v => Money = v;
        public float Scale { get; set; } = 1.5f;
        public Action<float> SetScale => v => Scale = v;
        public string NotNumber { get; set; } = "";
    }

    [Fact]
    public void NumberReader_ReadsEveryNumericTypeAsDecimal()
    {
        var b = new Binding();
        var (count, countType) = MarkupBindings.BindRequiredNumberReader("{Count}", b, "ctx");
        Assert.Equal(10m, count());
        Assert.Equal(typeof(int), countType);
        Assert.Equal(0.5m, MarkupBindings.BindRequiredNumberReader("{Ratio}", b, "ctx").Read());
        Assert.Equal(5m, MarkupBindings.BindRequiredNumberReader("{Big}", b, "ctx").Read());
        Assert.Equal(1.5m, MarkupBindings.BindRequiredNumberReader("{Scale}", b, "ctx").Read());
        Assert.Equal(b.Money, MarkupBindings.BindRequiredNumberReader("{Money}", b, "ctx").Read());
    }

    [Theory]
    [InlineData(double.NaN, 0)]
    [InlineData(double.PositiveInfinity, 1)]
    [InlineData(double.NegativeInfinity, -1)]
    [InlineData(1e30, 1)]
    public void NumberReader_ClampsUnrepresentableDoubles(double value, int expected)
    {
        var b = new Binding { Ratio = value };
        decimal read = MarkupBindings.BindRequiredNumberReader("{Ratio}", b, "ctx").Read();
        Assert.Equal(expected switch { 1 => decimal.MaxValue, -1 => decimal.MinValue, _ => 0m }, read);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("10")]
    [InlineData("{NotNumber}")]
    [InlineData("{Missing}")]
    public void NumberReader_RejectsNonNumbers(string? expression)
    {
        var ex = Assert.Throws<FormatException>(
            () => MarkupBindings.BindRequiredNumberReader(expression, new Binding(), "number value"));
        Assert.Contains("number value", ex.Message);
    }

    [Fact]
    public void NumberAction_ConvertsToTheBoundType()
    {
        var b = new Binding();
        MarkupBindings.BindRequiredNumberAction("{SetCount}", b, typeof(int), "ctx")(42m);
        MarkupBindings.BindRequiredNumberAction("{SetRatio}", b, typeof(double), "ctx")(0.25m);
        MarkupBindings.BindRequiredNumberAction("{SetBig}", b, typeof(long), "ctx")(9_000_000_000m);
        MarkupBindings.BindRequiredNumberAction("{SetScale}", b, typeof(float), "ctx")(2.5m);
        MarkupBindings.BindRequiredNumberAction("{SetMoney}", b, typeof(decimal), "ctx")(1.25m);
        Assert.Equal(42, b.Count);
        Assert.Equal(0.25, b.Ratio);
        Assert.Equal(9_000_000_000L, b.Big);
        Assert.Equal(2.5f, b.Scale);
        Assert.Equal(1.25m, b.Money);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("SetCount")]
    [InlineData("{SetBig}")]
    [InlineData("{Missing}")]
    public void NumberAction_RequiresActionOfTheValueType(string? attribute)
    {
        var ex = Assert.Throws<FormatException>(
            () => MarkupBindings.BindRequiredNumberAction(attribute, new Binding(), typeof(int), "number onchange"));
        Assert.Contains("number onchange", ex.Message);
    }

    [Fact]
    public void NumberRange_AndIntegral()
    {
        Assert.Equal(((decimal)int.MinValue, (decimal)int.MaxValue), MarkupBindings.NumberRange(typeof(int)));
        Assert.Equal(((decimal)long.MinValue, (decimal)long.MaxValue), MarkupBindings.NumberRange(typeof(long)));
        Assert.Equal((decimal.MinValue, decimal.MaxValue), MarkupBindings.NumberRange(typeof(double)));
        Assert.True(MarkupBindings.IsIntegralNumber(typeof(int)));
        Assert.True(MarkupBindings.IsIntegralNumber(typeof(long)));
        Assert.False(MarkupBindings.IsIntegralNumber(typeof(float)));
    }
}
```

- [ ] **Step 2: Run and see it fail**

Run: `dotnet test tests/AcDream.App.Tests/AcDream.App.Tests.csproj --filter "FullyQualifiedName~MarkupNumberTests"`
Expected: build FAILS: `'MarkupBindings' does not contain a definition for 'BindRequiredNumberReader'`.

- [ ] **Step 3: Add the helpers** — append inside `internal static class MarkupBindings` (after `BindBoolAction`):

```csharp
    private static readonly Type[] NumberTypes =
        [typeof(int), typeof(long), typeof(float), typeof(double), typeof(decimal)];

    /// <summary>
    /// A required number binding of any of the five numeric types, read as
    /// <see cref="decimal"/>. Also returns the property's type, which the
    /// matching <c>onchange</c> must take.
    /// </summary>
    public static (Func<decimal> Read, Type ValueType) BindRequiredNumberReader(
        string? expression, object binding, string context)
    {
        if (string.IsNullOrWhiteSpace(expression) || !MarkupDocument.IsBinding(expression))
            throw new FormatException($"{context} must be a number binding");
        PropertyInfo? property = binding.GetType().GetProperty(expression[1..^1]);
        if (property is null || Array.IndexOf(NumberTypes, property.PropertyType) < 0)
        {
            throw new FormatException(
                $"{expression} did not resolve to an int, long, float, double or decimal property on "
                + binding.GetType().Name + $" ({context})");
        }
        return (() => ToDecimal(property.GetValue(binding)), property.PropertyType);
    }

    /// <summary>
    /// A required <c>Action&lt;T&gt;</c> for exactly <paramref name="valueType"/>;
    /// the returned action converts a <see cref="decimal"/> already inside
    /// <c>T</c>'s range.
    /// </summary>
    public static Action<decimal> BindRequiredNumberAction(
        string? attribute, object binding, Type valueType, string context)
    {
        string expected = $"Action<{valueType.Name}>";
        if (string.IsNullOrWhiteSpace(attribute) || !MarkupDocument.IsBinding(attribute))
            throw new FormatException($"{context} must be an {expected} binding");
        Type actionType = typeof(Action<>).MakeGenericType(valueType);
        PropertyInfo? property = binding.GetType().GetProperty(attribute[1..^1]);
        if (property is null || !actionType.IsAssignableFrom(property.PropertyType))
        {
            throw new FormatException(
                $"{attribute} did not resolve to an {expected} property on "
                + binding.GetType().Name + $" ({context})");
        }
        object Target() => property.GetValue(binding)!;
        return valueType == typeof(int) ? value => (Target() as Action<int>)?.Invoke((int)value)
            : valueType == typeof(long) ? value => (Target() as Action<long>)?.Invoke((long)value)
            : valueType == typeof(float) ? value => (Target() as Action<float>)?.Invoke((float)value)
            : valueType == typeof(double) ? value => (Target() as Action<double>)?.Invoke((double)value)
            : value => (Target() as Action<decimal>)?.Invoke(value);
    }

    /// <summary>
    /// The range a number of <paramref name="valueType"/> can hold, as decimal:
    /// <c>float</c> and <c>double</c> are limited to <see cref="decimal"/>'s.
    /// </summary>
    public static (decimal Min, decimal Max) NumberRange(Type valueType) =>
        valueType == typeof(int) ? (int.MinValue, int.MaxValue)
        : valueType == typeof(long) ? (long.MinValue, long.MaxValue)
        : (decimal.MinValue, decimal.MaxValue);

    public static bool IsIntegralNumber(Type valueType) =>
        valueType == typeof(int) || valueType == typeof(long);

    private static decimal ToDecimal(object? value) => value switch
    {
        int i => i,
        long l => l,
        decimal d => d,
        float f => FromBinary(f),
        double d => FromBinary(d),
        _ => 0m,
    };

    /// <summary>NaN reads as 0; anything beyond decimal's range reads as its limit.</summary>
    private static decimal FromBinary(double value) =>
        double.IsNaN(value) ? 0m
        : value >= (double)decimal.MaxValue ? decimal.MaxValue
        : value <= (double)decimal.MinValue ? decimal.MinValue
        : (decimal)value;
```

- [ ] **Step 4: Run the tests**

Run: `dotnet test tests/AcDream.App.Tests/AcDream.App.Tests.csproj --filter "FullyQualifiedName~MarkupNumberTests"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git checkout -- '*.lock.json' 2>/dev/null
git add src/AcDream.App/UI/Markup/MarkupBindings.cs tests/AcDream.App.Tests/UI/MarkupNumberTests.cs
git commit -m "feat(ui): number bindings for markup elements

Reads int, long, float, double or decimal as decimal and writes back
through the matching Action<T>.

Claude-Session: https://claude.ai/code/session_01UzLnYkJ6ifzKGaeNwgsp8W"
```

---

### Task 3: `<number>` widget and builder

**Goal:** A `<number>` element that commits valid, clamped, rounded values and steps with spin buttons, keys and the focused wheel.

**Files:**
- Create: `src/AcDream.App/UI/UiMarkupNumber.cs`
- Create: `src/AcDream.App/UI/Markup/MarkupNumber.cs`
- Modify: `src/AcDream.App/UI/MarkupDocument.cs` (`case "number"`)
- Test: `tests/AcDream.App.Tests/UI/MarkupNumberTests.cs`

**Acceptance Criteria:**
- [ ] Builds a `UiMarkupNumber` whose field shows the bound value formatted to `decimals`.
- [ ] Build throws for: missing/non-number `value`; missing or wrong-typed `onchange`; `max < min`; `step <= 0`; `decimals` outside 0-10 or non-zero on an integral type; `min`/`max` outside the type's range; non-numeric `min`/`max`/`step`/`decimals`.
- [ ] Focus loss and Enter commit the typed text, rounded then clamped; out-of-range clamps; unparseable restores; Escape restores without reporting.
- [ ] A value equal to the current one is not reported.
- [ ] Up/Down step while focused; Shift steps 10×; the wheel steps only while focused.
- [ ] Spin top/bottom halves step up/down, repeat after 0.40 s every 0.06 s, stop on release, clamp at the limits, and don't take keyboard focus.
- [ ] Stepping near `decimal`'s limits clamps instead of throwing.
- [ ] The field follows a bound value changed while not editing; the character filter allows `-` only when `min < 0` and `.` only when `decimals > 0`.
- [ ] Out-of-range or unparseable text draws in `ErrorColor`; `enabled="false"` makes the field read-only and the spins inert.

**Verify:** `dotnet test tests/AcDream.App.Tests/AcDream.App.Tests.csproj --filter "FullyQualifiedName~MarkupNumberTests"` → all pass.

**Steps:**

- [ ] **Step 1: Write the failing tests** — append inside `MarkupNumberTests`:

```csharp
    private const string CountAttrs =
        "value=\"{Count}\" min=\"0\" max=\"100\" step=\"5\" onchange=\"{SetCount}\"";

    // The number sits at (8, 8), 80×20: spin buttons span x 74-88; up is y 8-18, down y 18-28.
    private const int SpinX = 81, UpY = 11, DownY = 24;

    private static (UiRoot Root, UiMarkupNumber Number) Mount(string attributes, Binding binding)
    {
        var panel = MarkupDocument.Build(
            $"<panel x=\"0\" y=\"0\" w=\"300\" h=\"100\"><number x=\"8\" y=\"8\" w=\"80\" h=\"20\" {attributes}/></panel>",
            binding, _ => (1u, 16, 16));
        var root = new UiRoot { Width = 800, Height = 600 };
        root.AddChild(panel);
        return (root, Assert.IsType<UiMarkupNumber>(panel.Children[0]));
    }

    private static void TypeText(UiRoot root, UiMarkupNumber number, string text)
    {
        root.SetKeyboardFocus(number.Field);
        number.Field.SetText(text);
    }

    private static void Press(UiRoot root, int x, int y) => root.OnMouseDown(UiMouseButton.Left, x, y);
    private static void Release(UiRoot root, int x, int y) => root.OnMouseUp(UiMouseButton.Left, x, y);

    [Fact]
    public void Number_ShowsTheBoundValueFormatted()
    {
        var (_, count) = Mount(CountAttrs, new Binding());
        Assert.Equal("10", count.Field.Text);
        var (_, ratio) = Mount("value=\"{Ratio}\" onchange=\"{SetRatio}\"", new Binding());
        Assert.Equal("0.50", ratio.Field.Text);
    }

    [Theory]
    [InlineData("onchange=\"{SetCount}\"")]
    [InlineData("value=\"{NotNumber}\" onchange=\"{SetCount}\"")]
    [InlineData("value=\"{Count}\"")]
    [InlineData("value=\"{Count}\" onchange=\"{SetBig}\"")]
    [InlineData("value=\"{Count}\" onchange=\"{SetCount}\" min=\"10\" max=\"5\"")]
    [InlineData("value=\"{Count}\" onchange=\"{SetCount}\" step=\"0\"")]
    [InlineData("value=\"{Count}\" onchange=\"{SetCount}\" decimals=\"2\"")]
    [InlineData("value=\"{Ratio}\" onchange=\"{SetRatio}\" decimals=\"11\"")]
    [InlineData("value=\"{Count}\" onchange=\"{SetCount}\" max=\"3000000000\"")]
    [InlineData("value=\"{Count}\" onchange=\"{SetCount}\" min=\"low\"")]
    [InlineData("value=\"{Ratio}\" onchange=\"{SetRatio}\" decimals=\"two\"")]
    public void Number_BadAttributesThrowAtBuild(string attributes)
    {
        Assert.Throws<FormatException>(() => Mount(attributes, new Binding()));
    }

    [Fact]
    public void Number_FocusLossCommitsTheTypedValue()
    {
        var b = new Binding();
        var (root, number) = Mount(CountAttrs, b);
        TypeText(root, number, "42");
        root.SetKeyboardFocus(null);
        Assert.Equal(42, b.Count);
        Assert.Equal("42", number.Field.Text);
    }

    [Fact]
    public void Number_EnterCommits()
    {
        var b = new Binding();
        var (root, number) = Mount(CountAttrs, b);
        TypeText(root, number, "42");
        root.OnKeyDown((int)Key.Enter);
        Assert.Equal(42, b.Count);
        Assert.Null(root.KeyboardFocus);
    }

    [Fact]
    public void Number_CommitClampsAndRounds()
    {
        var b = new Binding();
        var (root, count) = Mount(CountAttrs, b);
        TypeText(root, count, "250");
        root.SetKeyboardFocus(null);
        Assert.Equal(100, b.Count);

        var (root2, ratio) = Mount("value=\"{Ratio}\" min=\"-1\" max=\"1\" decimals=\"1\" onchange=\"{SetRatio}\"", b);
        TypeText(root2, ratio, "0.25");
        root2.SetKeyboardFocus(null);
        Assert.Equal(0.3, b.Ratio, 10);
        Assert.Equal("0.3", ratio.Field.Text);
    }

    [Theory]
    [InlineData("-")]
    [InlineData("")]
    [InlineData("1-2")]
    public void Number_UnparseableTextRestores(string text)
    {
        var b = new Binding();
        var (root, number) = Mount(CountAttrs, b);
        TypeText(root, number, text);
        root.SetKeyboardFocus(null);
        Assert.Equal(0, b.Calls);
        Assert.Equal("10", number.Field.Text);
    }

    [Fact]
    public void Number_EscapeRestoresWithoutReporting()
    {
        var b = new Binding();
        var (root, number) = Mount(CountAttrs, b);
        TypeText(root, number, "77");
        root.OnKeyDown((int)Key.Escape);
        Assert.Equal(0, b.Calls);
        Assert.Equal(10, b.Count);
        Assert.Equal("10", number.Field.Text);
        Assert.Null(root.KeyboardFocus);
    }

    [Fact]
    public void Number_SameValueIsNotReported()
    {
        var b = new Binding();
        var (root, number) = Mount(CountAttrs, b);
        TypeText(root, number, "10");
        root.SetKeyboardFocus(null);
        Assert.Equal(0, b.Calls);
    }

    [Fact]
    public void Number_UpDownStepAndShiftStepsTen()
    {
        var b = new Binding();
        var (root, number) = Mount(CountAttrs, b);
        root.SetKeyboardFocus(number.Field);
        root.OnKeyDown((int)Key.Up);
        Assert.Equal(15, b.Count);
        root.OnKeyDown((int)Key.Down);
        Assert.Equal(10, b.Count);
        number.ShiftHeld = () => true;
        root.OnKeyDown((int)Key.Up);
        Assert.Equal(60, b.Count);
        Assert.Equal("60", number.Field.Text);
    }

    [Fact]
    public void Number_WheelStepsOnlyWhileFocused()
    {
        var b = new Binding();
        var (root, number) = Mount(CountAttrs, b);
        root.OnMouseMove(30, 15);
        root.OnScroll(1);
        Assert.Equal(10, b.Count);
        root.SetKeyboardFocus(number.Field);
        root.OnScroll(1);
        Assert.Equal(15, b.Count);
        root.OnScroll(-1);
        Assert.Equal(10, b.Count);
    }

    [Fact]
    public void Number_SpinButtonsStepAndRepeatWhileHeld()
    {
        var b = new Binding();
        var (root, _) = Mount(CountAttrs, b);
        Press(root, SpinX, UpY);
        Assert.Equal(15, b.Count);
        Assert.Null(root.KeyboardFocus);
        root.Tick(0.39, 0);
        Assert.Equal(15, b.Count);
        root.Tick(0.02, 0);
        Assert.Equal(20, b.Count);
        root.Tick(0.06, 0);
        Assert.Equal(25, b.Count);
        Release(root, SpinX, UpY);
        root.Tick(1.0, 0);
        Assert.Equal(25, b.Count);

        Press(root, SpinX, DownY);
        Release(root, SpinX, DownY);
        Assert.Equal(20, b.Count);
    }

    [Fact]
    public void Number_SpinClampsAtTheLimitWithoutReporting()
    {
        var b = new Binding { Count = 100 };
        var (root, _) = Mount(CountAttrs, b);
        Press(root, SpinX, UpY);
        Release(root, SpinX, UpY);
        Assert.Equal(100, b.Count);
        Assert.Equal(0, b.Calls);
    }

    [Fact]
    public void Number_SteppingPastDecimalLimitsClamps()
    {
        var b = new Binding();
        var (root, number) = Mount("value=\"{Money}\" decimals=\"1\" onchange=\"{SetMoney}\"", b);
        root.SetKeyboardFocus(number.Field);
        number.ShiftHeld = () => true;
        root.OnKeyDown((int)Key.Up);
        Assert.Equal(decimal.MaxValue, b.Money);
    }

    [Fact]
    public void Number_FollowsTheBoundValueWhileNotEditing()
    {
        var b = new Binding();
        var (root, number) = Mount(CountAttrs, b);
        b.Count = 30;
        root.Tick(0.016, 0);
        Assert.Equal("30", number.Field.Text);
    }

    [Fact]
    public void Number_CharacterFilterFollowsRangeAndDecimals()
    {
        var (_, count) = Mount(CountAttrs, new Binding());
        Assert.True(count.Field.CharacterFilter!('5'));
        Assert.False(count.Field.CharacterFilter!('a'));
        Assert.False(count.Field.CharacterFilter!('-'));
        Assert.False(count.Field.CharacterFilter!('.'));
        var (_, ratio) = Mount("value=\"{Ratio}\" min=\"-1\" max=\"1\" decimals=\"1\" onchange=\"{SetRatio}\"", new Binding());
        Assert.True(ratio.Field.CharacterFilter!('-'));
        Assert.True(ratio.Field.CharacterFilter!('.'));
    }

    [Fact]
    public void Number_InvalidTextDrawsInTheErrorColour()
    {
        var (root, number) = Mount(CountAttrs, new Binding());
        TypeText(root, number, "250");
        Assert.Equal(UiMarkupNumber.ErrorColor, number.Field.TextColorSource!());
        number.Field.SetText("50");
        Assert.NotEqual(UiMarkupNumber.ErrorColor, number.Field.TextColorSource!());
    }

    [Fact]
    public void Number_DisabledIsReadOnlyAndSpinsDoNothing()
    {
        var b = new Binding();
        var (root, number) = Mount(CountAttrs + " enabled=\"false\"", b);
        root.Tick(0.016, 0);
        Assert.False(number.Field.Editable);
        Press(root, SpinX, UpY);
        Release(root, SpinX, UpY);
        Assert.Equal(0, b.Calls);
    }

    [Fact]
    public void Number_LongAndFloatStepAsTheirTypes()
    {
        var b = new Binding();
        var (rootLong, _) = Mount("value=\"{Big}\" step=\"2\" onchange=\"{SetBig}\"", b);
        Press(rootLong, SpinX, UpY);
        Assert.Equal(7L, b.Big);
        var (rootFloat, _) = Mount("value=\"{Scale}\" step=\"0.25\" onchange=\"{SetScale}\"", b);
        Press(rootFloat, SpinX, UpY);
        Assert.Equal(1.75f, b.Scale);
    }
```

- [ ] **Step 2: Run and see them fail**

Run: `dotnet test tests/AcDream.App.Tests/AcDream.App.Tests.csproj --filter "FullyQualifiedName~MarkupNumberTests"`
Expected: build FAILS: `UiMarkupNumber` not found.

- [ ] **Step 3: Create `src/AcDream.App/UI/UiMarkupNumber.cs`**

```csharp
using System.Globalization;
using System.Numerics;

namespace AcDream.App.UI;

/// <summary>
/// A markup number field: a one-line <see cref="UiField"/> that only ever
/// reports a valid value, clamped to <see cref="Min"/>..<see cref="Max"/> and
/// rounded to <see cref="Decimals"/>, with spin buttons at its right edge.
/// Values travel as <see cref="decimal"/>; the builder converts to and from
/// the bound type.
/// </summary>
public sealed class UiMarkupNumber : UiElement
{
    /// <summary>The spin buttons' width at the right edge.</summary>
    public const float SpinWidth = 14f;

    internal const double RepeatDelay = 0.40;
    internal const double RepeatRate = 0.06;

    public static readonly Vector4 ErrorColor = new(0.95f, 0.38f, 0.32f, 1f);
    public static readonly Vector4 ClassicSpinFace = new(0.16f, 0.14f, 0.10f, 1f);
    public static readonly Vector4 ClassicSpinPressed = new(0.26f, 0.23f, 0.16f, 1f);
    public static readonly Vector4 ClassicSpinBorder = UiCheckLamp.UncheckedInner;
    public static readonly Vector4 ClassicSpinArrow = new(0.80f, 0.74f, 0.55f, 1f);

    /// <summary>The editing field; it fills the control left of the spin buttons.</summary>
    public UiField Field { get; }

    public PluginUiPalette? ThemePalette { get; set; }
    public Func<decimal>? ValueSource { get; set; }
    public Action<decimal>? Changed { get; set; }
    public decimal Min { get; set; } = decimal.MinValue;
    public decimal Max { get; set; } = decimal.MaxValue;
    public decimal Step { get; set; } = 1m;
    public int Decimals { get; set; }

    /// <summary>Whether Shift is held, so a step counts ten. Defaults to the root's keyboard.</summary>
    public Func<bool>? ShiftHeld { get; set; }

    /// <summary>A bound caption colour; re-read every frame in place of the field's own.</summary>
    public Func<Vector4>? TextColorSource { get; set; }

    private bool _cancelling;
    private int _spinDirection;
    private double _spinTimer;

    public UiMarkupNumber()
    {
        CapturesPointerDrag = true;
        Field = new UiField
        {
            OneLine = true,
            Selectable = true,
            TabStop = true,
            ClearOnSubmit = false,
            RecordHistory = false,
            SelectAllOnFocus = true,
            MaxCharacters = 32,
        };
        Field.CharacterFilter = AcceptsCharacter;
        Field.OnUp = () => StepBy(1);
        Field.OnDown = () => StepBy(-1);
        Field.OnEscape = () => _cancelling = true;
        Field.OnFocusLost = text =>
        {
            if (_cancelling)
            {
                _cancelling = false;
                ShowValue();
            }
            else
            {
                Commit(text);
            }
        };
        Field.BoundText = () => Format(Current);
        Field.TextColorSource = () => Fade(IsValidText(Field.Text)
            ? TextColorSource?.Invoke() ?? Field.TextColor
            : ErrorColor);
        AddChild(Field);
    }

    public override bool HandlesClick => true;

    /// <summary>The bound value.</summary>
    public decimal Current => ValueSource?.Invoke() ?? 0m;

    public string Format(decimal value) =>
        value.ToString("F" + Decimals.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);

    /// <summary>Digits always; a minus sign only when negatives are allowed; a point only with decimals.</summary>
    public bool AcceptsCharacter(char c) =>
        char.IsAsciiDigit(c) || (c == '-' && Min < 0m) || (c == '.' && Decimals > 0);

    public static bool TryParse(string text, out decimal value) =>
        decimal.TryParse(text, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint,
            CultureInfo.InvariantCulture, out value);

    public bool IsValidText(string text) => TryParse(text, out decimal value) && value >= Min && value <= Max;

    /// <summary>Applies typed text; text that isn't a number puts the bound value back.</summary>
    public void Commit(string text)
    {
        if (TryParse(text, out decimal typed))
            Apply(typed);
        else
            ShowValue();
    }

    /// <summary>Steps once up (+1) or down (-1); ten steps while Shift is held.</summary>
    public void StepBy(int direction)
    {
        if (!Enabled)
            return;
        bool shift = ShiftHeld?.Invoke() ?? RootShiftHeld();
        decimal requested;
        try
        {
            requested = Current + direction * Step * (shift ? 10m : 1m);
        }
        catch (OverflowException)
        {
            requested = direction > 0 ? Max : Min;
        }
        Apply(requested);
    }

    private void Apply(decimal requested)
    {
        decimal value = Math.Clamp(
            Math.Round(requested, Decimals, MidpointRounding.AwayFromZero), Min, Max);
        if (value != Current)
            Changed?.Invoke(value);
        ShowValue();
    }

    private void ShowValue() => Field.SetText(Format(Current));

    private bool RootShiftHeld() => FindRoot()?.Keyboard is { } keyboard
        && (keyboard.IsKeyPressed(Silk.NET.Input.Key.ShiftLeft)
            || keyboard.IsKeyPressed(Silk.NET.Input.Key.ShiftRight));

    private Vector4 Fade(Vector4 color) =>
        Enabled ? color : color with { W = color.W * UiMarkupToggle.DisabledAlpha };

    /// <summary>Keeps the field beside the spin buttons and read-only while disabled.</summary>
    internal void SyncField()
    {
        Field.Left = 0f;
        Field.Top = 0f;
        Field.Width = MathF.Max(0f, Width - SpinWidth);
        Field.Height = Height;
        if (Field.Editable != Enabled)
            Field.Editable = Enabled;
    }

    public override bool OnEvent(in UiEvent e)
    {
        switch (e.Type)
        {
            case UiEventType.MouseDown:
                if (!Enabled || e.Data1 < Width - SpinWidth)
                    return true;
                _spinDirection = e.Data2 < Height * 0.5f ? 1 : -1;
                _spinTimer = RepeatDelay;
                StepBy(_spinDirection);
                return true;
            case UiEventType.MouseUp:
            case UiEventType.CaptureChanged:
                _spinDirection = 0;
                return true;
            case UiEventType.Scroll:
                if (!Enabled || !Field.IsFocused || e.Data0 == 0)
                    return false;
                StepBy(Math.Sign(e.Data0));
                return true;
        }
        return false;
    }

    protected override void OnTick(double deltaSeconds)
    {
        SyncField();
        if (_spinDirection == 0)
            return;
        if (!Enabled)
        {
            _spinDirection = 0;
            return;
        }
        _spinTimer -= deltaSeconds;
        while (_spinTimer <= 0)
        {
            StepBy(_spinDirection);
            _spinTimer += RepeatRate;
        }
    }

    protected override void OnDraw(UiRenderContext ctx)
    {
        SyncField();
        float x = Width - SpinWidth;
        float half = MathF.Floor(Height * 0.5f);
        Vector4 face = Fade(ThemePalette?.Field ?? ClassicSpinFace);
        Vector4 pressed = Fade(ThemePalette?.Selected ?? ClassicSpinPressed);
        Vector4 border = Fade(ThemePalette?.Border ?? ClassicSpinBorder);
        Vector4 arrow = Fade(ThemePalette?.Text ?? ClassicSpinArrow);

        ctx.DrawFill(x, 0f, SpinWidth, half, _spinDirection > 0 ? pressed : face);
        ctx.DrawFill(x, half, SpinWidth, Height - half, _spinDirection < 0 ? pressed : face);
        ctx.DrawRectOutline(x, 0f, SpinWidth, Height, border, 1f);
        ctx.DrawFill(x, half, SpinWidth, 1f, border);
        float centre = x + SpinWidth * 0.5f;
        DrawArrow(ctx, centre, half * 0.5f, up: true, arrow);
        DrawArrow(ctx, centre, half + (Height - half) * 0.5f, up: false, arrow);
    }

    /// <summary>A pixel triangle three rows tall (1, 3 and 5 px wide), centred on (cx, cy).</summary>
    private static void DrawArrow(UiRenderContext ctx, float cx, float cy, bool up, Vector4 color)
    {
        float top = MathF.Floor(cy - 1.5f);
        for (int row = 0; row < 3; row++)
        {
            int width = up ? 1 + row * 2 : 5 - row * 2;
            ctx.DrawFill(MathF.Floor(cx - width * 0.5f), top + row, width, 1f, color);
        }
    }
}
```

- [ ] **Step 4: Create `src/AcDream.App/UI/Markup/MarkupNumber.cs`**

```csharp
using System.Globalization;
using System.Numerics;
using System.Xml.Linq;

namespace AcDream.App.UI.Markup;

internal static class MarkupNumber
{
    public static UiMarkupNumber Build(XElement el, object binding, MarkupBuildContext context)
    {
        var (read, valueType) = MarkupBindings.BindRequiredNumberReader(
            (string?)el.Attribute("value"), binding, "number value");
        Action<decimal> changed = MarkupBindings.BindRequiredNumberAction(
            (string?)el.Attribute("onchange"), binding, valueType, "number onchange");

        bool integral = MarkupBindings.IsIntegralNumber(valueType);
        (decimal typeMin, decimal typeMax) = MarkupBindings.NumberRange(valueType);
        decimal min = Decimal(el, "min", typeMin);
        decimal max = Decimal(el, "max", typeMax);
        decimal step = Decimal(el, "step", 1m);
        int decimals = Decimals(el, integral ? 0 : 2);

        if (max < min)
            throw new FormatException($"<number min=\"{min}\" max=\"{max}\"> must have max >= min");
        if (min < typeMin || max > typeMax)
            throw new FormatException(
                $"<number min=\"{min}\" max=\"{max}\"> must fit a {valueType.Name} value");
        if (step <= 0m)
            throw new FormatException($"<number step=\"{step}\"> must be greater than 0");
        if (decimals is < 0 or > 10)
            throw new FormatException($"<number decimals=\"{decimals}\"> must be from 0 to 10");
        if (integral && decimals != 0)
            throw new FormatException(
                $"<number decimals=\"{decimals}\"> must be 0 for a {valueType.Name} value");

        var number = new UiMarkupNumber
        {
            Left = MarkupDocument.F(el, "x"),
            Top = MarkupDocument.F(el, "y"),
            Width = MarkupDocument.F(el, "w"),
            Height = MarkupDocument.F(el, "h"),
            Min = min,
            Max = max,
            Step = step,
            Decimals = decimals,
            ValueSource = read,
            Changed = changed,
        };
        UiField field = number.Field;
        field.DatFont = context.DatFont;
        string? background = (string?)el.Attribute("background");
        string? color = (string?)el.Attribute("color");
        field.BackgroundColor = background is null
            ? new Vector4(0f, 0f, 0f, 0.9f)
            : MarkupDocument.Color(background);
        field.TextColor = color is null
            ? new Vector4(0.91f, 0.87f, 0.76f, 1f)
            : MarkupDocument.Color(color);
        context.BindColor(background, value => field.BackgroundColor = value,
            source => field.BackgroundColorSource = source);
        context.BindColor(color, value => field.TextColor = value,
            source => number.TextColorSource = source);

        number.SyncField();
        field.SetText(number.Format(read()));
        MarkupDocument.ApplyCommon(number, el, binding);
        return number;
    }

    private static decimal Decimal(XElement el, string attribute, decimal fallback)
    {
        string? raw = (string?)el.Attribute(attribute);
        if (raw is null)
            return fallback;
        if (!decimal.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out decimal value))
            throw new FormatException($"<number {attribute}=\"{raw}\"> is not a number");
        return value;
    }

    private static int Decimals(XElement el, int fallback)
    {
        string? raw = (string?)el.Attribute("decimals");
        if (raw is null)
            return fallback;
        if (!int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value))
            throw new FormatException($"<number decimals=\"{raw}\"> is not a whole number");
        return value;
    }
}
```

- [ ] **Step 5: Wire it into `MarkupDocument.AddElement`**

After the `case "checkbox": … break;` block, separated by a blank line, add:

```csharp
            case "number":
                parent.AddChild(MarkupNumber.Build(el, binding, Context()));
                break;
```

- [ ] **Step 6: Run the tests**

Run: `dotnet test tests/AcDream.App.Tests/AcDream.App.Tests.csproj --filter "FullyQualifiedName~MarkupNumberTests"`
Expected: PASS. If `Number_WheelStepsOnlyWhileFocused` fails because `OnScroll` doesn't reach the number, check `UiRoot.OnScroll` hit-tests the field at (30, 15) and that `UiField` returns false for a one-line Scroll (it does on 391e709a), then report what you found rather than changing the expectation.

- [ ] **Step 7: Run all markup tests**

Run: `dotnet test tests/AcDream.App.Tests/AcDream.App.Tests.csproj --filter "FullyQualifiedName~AcDream.App.Tests.UI.Markup|FullyQualifiedName~AcDream.App.Tests.UI.UiFieldTests"`
Expected: PASS.

- [ ] **Step 8: Commit**

```bash
git checkout -- '*.lock.json' 2>/dev/null
git add src/AcDream.App/UI tests/AcDream.App.Tests/UI
git commit -m "feat(ui): <number> markup element

A one-line field that reports only valid values, rounded to decimals
and clamped to min..max, with spin buttons that repeat while held,
Up/Down keys, the wheel while focused, and Shift for ten steps.
Escape restores the bound value.

Claude-Session: https://claude.ai/code/session_01UzLnYkJ6ifzKGaeNwgsp8W"
```

---

### Task 4: Theme and drawing

**Goal:** The number field takes the plugin theme exactly as `<field>` does, and its spin buttons draw in both looks, pressed and disabled.

**Files:**
- Modify: `src/AcDream.App/UI/PluginMarkupTheme.cs`
- Test: `tests/AcDream.App.Tests/UI/MarkupNumberTests.cs`

**Acceptance Criteria:**
- [ ] The `UiField` theme block lives in one `ThemeField` helper used by both `UiField` and `UiMarkupNumber`; `<field>` theming is unchanged.
- [ ] Under `theme="plugin"` (Moss): `number.ThemePalette` is the palette, the inner field's background is `Moss.Field`, and the modern font (when set) reaches the inner field.
- [ ] Classic draw: 6 `ClassicSpinArrow` quads; the pressed half draws 1 `ClassicSpinPressed` quad; disabled draws the 6 arrows at `DisabledAlpha`.
- [ ] Themed draw: no Classic arrow quads, at least 6 `Moss.Text` quads.

**Verify:** `dotnet test tests/AcDream.App.Tests/AcDream.App.Tests.csproj --filter "FullyQualifiedName~MarkupNumberTests|FullyQualifiedName~MarkupColorBindingTests|FullyQualifiedName~PluginTheme"` → all pass.

**Steps:**

- [ ] **Step 1: Write the failing tests** — append inside `MarkupNumberTests`:

```csharp
    private static UiMarkupNumber MountThemed(Binding binding)
    {
        var themes = new PluginUiThemeSettings { Theme = PluginUiTheme.Moss };
        var panel = MarkupDocument.Build(
            "<panel theme=\"plugin\" x=\"0\" y=\"0\" w=\"300\" h=\"100\">"
            + $"<number x=\"8\" y=\"8\" w=\"80\" h=\"20\" {CountAttrs}/></panel>",
            binding, _ => (1u, 16, 16), themes: themes);
        return panel.Children.OfType<UiMarkupNumber>().Single();
    }

    [Fact]
    public void Number_PluginThemeReachesTheInnerField()
    {
        var number = MountThemed(new Binding());
        Assert.Same(PluginUiPalette.Moss, number.ThemePalette);
        Assert.Equal(PluginUiPalette.Moss.Field, number.Field.BackgroundColor);
    }

    [Fact]
    public void Number_ClassicDrawsTwoArrows()
    {
        var (_, number) = Mount(CountAttrs, new Binding());
        var renderer = MarkupDrawProbe.Draw(number);
        Assert.Equal(6, MarkupDrawProbe.CountQuads(renderer, UiMarkupNumber.ClassicSpinArrow));
        Assert.Equal(0, MarkupDrawProbe.CountQuads(renderer, UiMarkupNumber.ClassicSpinPressed));
    }

    [Fact]
    public void Number_HeldSpinDrawsPressed()
    {
        var (root, number) = Mount(CountAttrs, new Binding());
        Press(root, SpinX, UpY);
        var renderer = MarkupDrawProbe.Draw(number);
        Assert.Equal(1, MarkupDrawProbe.CountQuads(renderer, UiMarkupNumber.ClassicSpinPressed));
    }

    [Fact]
    public void Number_DisabledDrawsFaded()
    {
        var (_, number) = Mount(CountAttrs + " enabled=\"false\"", new Binding());
        var renderer = MarkupDrawProbe.Draw(number);
        var faded = UiMarkupNumber.ClassicSpinArrow with { W = UiMarkupToggle.DisabledAlpha };
        Assert.Equal(6, MarkupDrawProbe.CountQuads(renderer, faded));
        Assert.Equal(0, MarkupDrawProbe.CountQuads(renderer, UiMarkupNumber.ClassicSpinArrow));
    }

    [Fact]
    public void Number_ThemedDrawUsesThePalette()
    {
        var number = MountThemed(new Binding());
        var renderer = MarkupDrawProbe.Draw(number);
        Assert.Equal(0, MarkupDrawProbe.CountQuads(renderer, UiMarkupNumber.ClassicSpinArrow));
        Assert.True(MarkupDrawProbe.CountQuads(renderer, PluginUiPalette.Moss.Text) >= 6);
    }
```

- [ ] **Step 2: Run and see them fail**

Run: `dotnet test tests/AcDream.App.Tests/AcDream.App.Tests.csproj --filter "FullyQualifiedName~MarkupNumberTests"`
Expected: `Number_PluginThemeReachesTheInnerField` and `Number_ThemedDrawUsesThePalette` FAIL (no theme registration yet); the Classic draw tests already pass from Task 3.

- [ ] **Step 3: Extract `ThemeField` and register the number in `PluginMarkupTheme`**

Replace the whole `case UiField field:` section of the second `switch` with:

```csharp
            case UiField field:
                ThemeField(panel, field, Default);
                break;
            case UiMarkupNumber number:
                ThemeField(panel, number.Field, Default);
                panel.AddThemeAction(p => number.ThemePalette = p);
                break;
```

Add this method to the class, below `Register`, with the body moved verbatim from the old case:

```csharp
    /// <summary>A field's look under the theme: palette fills, no dat sprites, no outline.</summary>
    private static void ThemeField(UiPluginMarkupPanel panel, UiField field, Func<string, bool> isDefault)
    {
        var fieldBg = field.BackgroundColor; var fieldText = field.TextColor;
        var fieldOutline = field.Outline; var sprite = field.BackgroundSprite;
        var focus = field.FocusFieldSprite; var left = field.FocusRailLeftSprite;
        var right = field.FocusRailRightSprite; var selection = field.SelectionColor;
        panel.AddThemeAction(p => {
            field.Outline = p is null && fieldOutline;
            field.BackgroundSprite = p is null ? sprite : 0;
            field.FocusFieldSprite = p is null ? focus : 0;
            field.FocusRailLeftSprite = p is null ? left : 0;
            field.FocusRailRightSprite = p is null ? right : 0;
            field.SelectionColor = p?.Selected ?? selection;
            if (isDefault("background")) field.BackgroundColor = p?.Field ?? fieldBg;
            if (isDefault("color")) field.TextColor = p?.Text ?? fieldText;
        });
    }
```

In the font `switch`, after the `UiField` line add:

```csharp
                case UiMarkupNumber number: Bind(() => number.Field.DatFont, f => number.Field.DatFont = f); break;
```

- [ ] **Step 4: Run the tests**

Run: `dotnet test tests/AcDream.App.Tests/AcDream.App.Tests.csproj --filter "FullyQualifiedName~MarkupNumberTests|FullyQualifiedName~MarkupColorBindingTests|FullyQualifiedName~PluginTheme"`
Expected: PASS. If `Number_ThemedDrawUsesThePalette` finds fewer than 6 `Moss.Text` quads, check whether `Fade` or the draw order changed the arrow colour; don't lower the bound.

- [ ] **Step 5: Run all markup tests, then commit**

Run: `dotnet test tests/AcDream.App.Tests/AcDream.App.Tests.csproj --filter "FullyQualifiedName~AcDream.App.Tests.UI.Markup"` → PASS.

```bash
git checkout -- '*.lock.json' 2>/dev/null
git add src/AcDream.App/UI/PluginMarkupTheme.cs tests/AcDream.App.Tests/UI/MarkupNumberTests.cs
git commit -m "feat(ui): plugin theme for <number>

The field theme moves into one helper so the number's inner field
looks like any <field>; the spin buttons take the palette too.

Claude-Session: https://claude.ai/code/session_01UzLnYkJ6ifzKGaeNwgsp8W"
```

---

### Task 5: Docs

**Goal:** `docs/plugin-ui-markup.md` documents `<number>`.

**Files:**
- Modify: `docs/plugin-ui-markup.md`

**Acceptance Criteria:**
- [ ] Elements table has a `number` row after `checkbox`.
- [ ] Bindings table has `number value` and `number onchange` rows after the checkbox rows.
- [ ] A `## Number field` section after `## Checkbox` covers attributes, defaults, commit/Escape, stepping, wheel, error colour, and the older-host line.
- [ ] The "Clicking a … without taking keyboard focus" sentence is unchanged (a number's field takes focus like `<field>`; its spin buttons don't).

**Verify:** `grep -c '<number\|`number' docs/plugin-ui-markup.md` → at least 6.

**Steps:**

- [ ] **Step 1: Tables**

Elements table, after the `checkbox` row:

```markdown
| `number` | Number field with spin buttons | `x y w h value onchange min max step decimals color background` |
```

Bindings table, after the `checkbox onchange` row:

```markdown
| `number value` | `int`, `long`, `float`, `double` or `decimal` | throws |
| `number onchange` | `Action<T>` of the same type as `value` | throws |
```

- [ ] **Step 2: Section** — after the `## Checkbox` section (before `## Title bar`), add:

````markdown
## Number field

```xml
<number x="8" y="8" w="80" h="20" value="{HealAt}" min="0" max="100" step="5"
        onchange="{SetHealAt}"/>
```

A `<number>` is a one-line field that only ever hands back a valid number,
with spin buttons at its right edge.

- `value` binds an `int`, `long`, `float`, `double` or `decimal`; `onchange`
  is an `Action<T>` of the same type. Both are required.
- `min` and `max` default to the type's range (for `float` and `double`,
  ±7.9×10^28). `step` defaults to 1 and must be above 0. `decimals` defaults
  to 0 for `int` and `long` (and must stay 0) and to 2 otherwise, at most 10.
- Typing accepts digits, a minus sign when `min` is below 0, and a point
  when `decimals` is above 0. Text that isn't a number, or is out of range,
  shows in red.
- Enter, or leaving the field, commits: the value is rounded to `decimals`,
  clamped to `min`..`max`, and `onchange` is called if it changed. Text that
  isn't a number puts the bound value back. Escape puts it back too.
- The spin buttons step once per click and repeat while held. Up and Down
  step while the field has focus, and so does the mouse wheel; the wheel
  never steps an unfocused field, so scrolling a list past it is safe.
  Hold Shift for ten steps.
- While nobody is typing in it, the field follows `value`.

On a host older than number fields, a `<number>` fails the build of its
window as unknown.
````

- [ ] **Step 3: Commit**

```bash
git add docs/plugin-ui-markup.md
git commit -m "docs(ui): document <number>

Claude-Session: https://claude.ai/code/session_01UzLnYkJ6ifzKGaeNwgsp8W"
```

---

### Task 6: Series gate

**Goal:** Release build clean; no new test failures versus the upstream baseline; every commit builds.

**Files:** none changed.

**Acceptance Criteria:**
- [ ] `dotnet build AcDream.slnx -c Release` → 0 errors, 0 warnings.
- [ ] Failing test names (timings stripped) equal the baseline's set, or are a subset of it.
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
done | sed -E 's/^[[:space:]]+Failed //; s/ \[[^]]*\]$//' | sort -u > $T/mc2-after.txt
sed -E 's/^[[:space:]]+Failed //; s/ \[[^]]*\]$//' $T/markup-controls-baseline.txt | sort -u > $T/mc-base.txt
echo "new:"; comm -13 $T/mc-base.txt $T/mc2-after.txt
git checkout -- '*.lock.json' 2>/dev/null; git status --short
```
Expected: `0 Warning(s)`, `0 Error(s)`, nothing after `new:`.

- [ ] **Step 2: Each commit builds alone**

```bash
cd /Users/davidsmith/code/OpenAC/OpenAC
for c in $(git rev-list --reverse a7db16ed..feat/markup-controls); do
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
git log --oneline a7db16ed..HEAD
git diff --stat 391e709a..HEAD | grep superpowers || echo "no superpowers docs"
```
Expected: the 5 commits of Tasks 1-5; "no superpowers docs".

No commit.
