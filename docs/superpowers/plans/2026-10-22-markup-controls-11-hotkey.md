# Markup controls, plan 11: `<hotkey>` Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers-extended-cc:subagent-driven-development (recommended) or superpowers-extended-cc:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** A `<hotkey>` markup element shows a bound key chord and, when clicked (or Enter/Space while focused), records the next chord the player presses and hands the plugin its canonical string (`Ctrl+Alt+Shift+Key`).

**Architecture:**
- **Codec.** `Markup/MarkupHotkeyText` formats a `KeyChord` as the canonical string and parses one back (modifiers in any order and case; anything unreadable is "unbound").
- **Widget.** `UiMarkupHotkey` is a box that takes keyboard focus on click (the spec's exception for `<hotkey>`). While it is capturing it swallows every key it is sent through `UiRoot`'s focus routing, so the game never sees them: `UiRoot.WantsKeyboard` is true while it has focus, and `InputDispatcher` already suppresses all bindings then. It reads modifiers from the root's keyboard (injectable for tests).
- **Not `InputDispatcher.BeginCapture`.** The spec names it, but it records mouse buttons and a lone modifier released on its own, both of which the spec forbids, and it is not reachable from a markup build. Capturing through `UiRoot` gives exactly the spec's rules and is headless-testable. Flag in the final review.
- **Key names.** `MarkupDocument.Build` gains an optional `Func<KeyChord, string>? describeKey`; `RetailUiRuntime.MountPlugins` passes `RetailKeyNames.Describe` (under the dat lock). Null (tests, older callers) shows the canonical string. The widget only re-describes when the bound string changes.

**Status:** DONE 2026-10-22 — feat 930a2f71..8e627c4c (5 commits: 4 tasks + final-review fix; each task commit builds; gate = baseline 162, 0 warnings). Final review fix: a recording box now blocks plugin hotkeys (UiElement.CapturesAllKeys → IHotkeyFocusSource.IsCapturingKeys → AppHotkeyRegistry early return; IHotkeyFocusSource gained a member), lost end-key KeyUp releases focus on the next tick, caption clipped inside the border, DescribeKey setter invalidates the cache, docs say left-click. Ruling (user to confirm before upstream): the string keeps Silk.NET Key names per spec §12 and the docs say it is not a PluginKeyChord (names differ: Keypad5/Numpad5; PluginKey covers fewer keys); alternatives are a public PluginKeyChord.TryParse bridge or restricting to PluginKey. Deferred: lone tab stop swallows Tab without cancelling; InputDispatcher.OnKeyUp fires Release bindings while UI has focus (pre-existing); disable-cancel of an Enter/Space capture keeps focus; ancestor disable doesn't stop capture (shared parked item); test gaps (Space/KeypadEnter start, modified Backspace, themed Accent, click while capturing, muted colour, hide/Tab via real window, nested-group describeKey, caption clip).

**Tech Stack:** C# / .NET 10, xUnit, headless `UiRoot` + `RecordingGpuDevice` tests, Silk.NET `Key`, `AcDream.UI.Abstractions.Input.KeyChord`/`ModifierMask`.

**Spec:** `docs/superpowers/specs/2026-10-21-markup-controls-design.md` (branch `docs/markup-controls-spec`), "Shared rules" and section 12.

## Global Constraints

- **Workspace.** Worktree `/Users/davidsmith/code/OpenAC/OpenAC/.worktrees/markup-controls`, branch `feat/markup-controls` (HEAD 930a2f71, from upstream 391e709a).
  - Never merge fork `main` in, and never add `docs/superpowers/` or `.superpowers/` files.
  - Push only when the controller says so.
- **Build hygiene.** Every shell runs `export DOTNET_ROOT=$HOME/.dotnet PATH=$HOME/.dotnet:$PATH` first. After building, run `git checkout -- '*.lock.json'`; never commit lock files.
- **Commits.** Every commit must build and pass its own tests. Commit messages end with `Claude-Session: https://claude.ai/code/session_014SAojwkxRKxrwp1RMDGPVC`.
- **Helper conventions.** `BindRequired*` helpers throw `FormatException` when the attribute is absent or mistyped; `context` is `"hotkey value"` / `"hotkey onchange"`. The `<hotkey>` case adds exactly one element to its parent (so the theme `Register` still fires).
- **Canonical string.** `Ctrl+`, `Alt+`, `Shift+` in that order, then `Key.ToString()` (Silk.NET `Key`). Empty = unbound. The Win/Super modifier is never written. Parsing accepts modifiers in any order and any case, the key name in any case; it rejects: empty parts, unknown or repeated modifiers, a key name that is not all ASCII letters/digits or does not start with a letter, `Key.Unknown`, and modifier keys as the key.
- **`<hotkey>` contract.**
  - `value`: required `string` binding. Null reads as empty. Shown via `DescribeKey` (retail key names) or the canonical string; empty or unreadable shows `None` in the muted colour.
  - `onchange`: required `Action<string>`. Called only when the new string differs (ordinal) from the bound string.
  - Common attributes (`name id visible enabled tooltip anchor`) and `x y w h`. No others.
- **Capture rules.**
  - Start: a left click (also takes focus) or Enter/KeypadEnter/Space while focused and not capturing. A disabled control never starts. A click while capturing does nothing.
  - While capturing the box shows `Press a key...` (ASCII dots: dat fonts may lack `…`) and handles every KeyDown/KeyUp it gets.
  - Escape: stop, report nothing. Backspace or Delete (with or without modifiers): stop, report `""`.
  - A modifier key (Shift/Ctrl/Alt/Super, left or right) keeps waiting. Any key while Win/Super is held keeps waiting.
  - Any other key: stop and report `Format(new KeyChord(key, mods))`, `mods` = Ctrl/Alt/Shift held at that moment.
  - The Enter/Space that started a keyboard capture is ignored (repeats) until its KeyUp.
  - Tab is never captured: `UiRoot` moves focus on Tab before the widget sees it, which cancels.
  - Losing focus (click elsewhere, Tab, the window hiding) cancels with no report. Becoming disabled cancels on the next tick.
  - **Focus after capture.** A capture started by click gives focus back to the game when the key that ended it goes up (Escape/Backspace/Delete/chord key), so the game doesn't see that key's KeyDown with focus gone; a cancel by disable releases at once. A capture started by Enter/Space keeps focus (the player is navigating by keyboard).
  - Mouse buttons are never captured.
- **Looks** (`UiMarkupHotkey` constants). Fill + 1 px outline + text at x = 4, vertically centred via `UiCaption.Draw`.
  - Classic: background (0, 0, 0, 0.9); border `UiCheckLamp.UncheckedInner`; text (0.91, 0.87, 0.76, 1); muted (0.5, 0.5, 0.5, 1); focused/capturing border and capturing text the gold focus colour (1, 0.82, 0.25, 1); text outlined.
  - `theme="plugin"`: `Field` background, `Border` border, `Text`, `Muted`, `Accent` for focus/capture; the panel's modern font; no outline.
  - Disabled: every colour's alpha × `UiMarkupToggle.DisabledAlpha`.

**User decisions (already made):**
- One branch, one commit series per control, upstream-portable.
- Non-text controls don't take focus on click, except text-editing controls and `<hotkey>` (spec "Keyboard").
- Keyboard input only, no mouse buttons (spec section 12).

**Plan choices (details the spec leaves open; flag in the final review if wrong):**
- Capture goes through `UiRoot` focus routing, not `InputDispatcher.BeginCapture` (see Architecture).
- Unbound shows `None`; the prompt is `Press a key...` with ASCII dots.
- Backspace/Delete clear even with modifiers held; Win+anything is ignored; Tab cannot be captured.
- `onchange` only fires on a real change.
- Focus is handed back after a click capture, on the ending key's release.

---

## File Structure

| File | Responsibility |
|---|---|
| `src/AcDream.App/UI/Markup/MarkupHotkeyText.cs` (new) | Canonical format/parse, key classification |
| `src/AcDream.App/UI/Markup/MarkupBindings.cs` | `BindRequiredStringReader`, `BindRequiredStringAction` |
| `src/AcDream.App/UI/UiMarkupHotkey.cs` (new) | The widget: capture, focus, display cache, draw |
| `src/AcDream.App/UI/Markup/MarkupHotkey.cs` (new) | Builder |
| `src/AcDream.App/UI/Markup/MarkupBuildContext.cs` | `DescribeKey` |
| `src/AcDream.App/UI/MarkupDocument.cs` | `describeKey` parameter threaded through `Build`/`AddElement`; `case "hotkey"` |
| `src/AcDream.App/UI/PluginMarkupTheme.cs` | Font + palette |
| `src/AcDream.App/UI/RetailUiRuntime.cs` | Retail key names for plugin windows |
| `tests/AcDream.App.Tests/UI/MarkupHotkeyTextTests.cs`, `UiMarkupHotkeyTests.cs`, `MarkupHotkeyTests.cs` (new) | Tests |
| `docs/plugin-ui-markup.md` | Hotkey docs |

---

### Task 1: Canonical hotkey text and string bind helpers

**Goal:** `MarkupHotkeyText.Format`/`Parse` round-trip the canonical string, and `MarkupBindings` gains required string reader/action helpers.

**Files:**
- Create: `src/AcDream.App/UI/Markup/MarkupHotkeyText.cs`
- Modify: `src/AcDream.App/UI/Markup/MarkupBindings.cs` (beside `BindRequiredFloatReader`)
- Test: `tests/AcDream.App.Tests/UI/MarkupHotkeyTextTests.cs`

**Acceptance Criteria:**
- [ ] `Format(new KeyChord(Key.F, Shift|Ctrl|Alt))` == `"Ctrl+Alt+Shift+F"`; Win is dropped; a mouse chord (Device 1), `Key.Unknown` or a modifier key formats as `""`.
- [ ] `Parse` accepts `"Ctrl+Alt+Shift+F"`, `"shift+ctrl+f"`, `"F12"`, `"Keypad5"`; returns null for null, `""`, `"Ctrl+"`, `"+F"`, `"Ctrl+Ctrl+F"`, `"Meta+F"`, `"5"`, `"A,B"`, `"Unknown"`, `"ShiftLeft"`, `"Ctrl+Nope"`.
- [ ] Every chord `Parse` returns formats back to the canonical string.
- [ ] `BindRequiredStringReader` reads a `string` property (null → `""`) and throws naming the context for absent, literal, missing or non-string; `BindRequiredStringAction` invokes an `Action<string>` and throws likewise.

**Verify:** `dotnet test tests/AcDream.App.Tests/AcDream.App.Tests.csproj --filter "FullyQualifiedName~MarkupHotkeyTextTests"` → all pass.

**Steps:**

- [ ] **Step 1: Write the failing tests** — create `tests/AcDream.App.Tests/UI/MarkupHotkeyTextTests.cs`:

```csharp
using AcDream.App.UI.Markup;
using AcDream.UI.Abstractions.Input;
using Silk.NET.Input;

namespace AcDream.App.Tests.UI;

public class MarkupHotkeyTextTests
{
    private sealed class Binding
    {
        public string? Key { get; set; } = "Ctrl+F";
        public int NotString { get; set; }
        public string? Last { get; private set; }
        public Action<string> SetKey => v => Last = v;
        public Action<int> WrongAction => _ => { };
    }

    [Fact]
    public void Format_WritesModifiersInCanonicalOrder()
    {
        var mods = ModifierMask.Shift | ModifierMask.Ctrl | ModifierMask.Alt | ModifierMask.Win;
        Assert.Equal("Ctrl+Alt+Shift+F", MarkupHotkeyText.Format(new KeyChord(Key.F, mods)));
        Assert.Equal("F5", MarkupHotkeyText.Format(new KeyChord(Key.F5, ModifierMask.None)));
    }

    [Fact]
    public void Format_RefusesWhatNoHotkeyCanHold()
    {
        Assert.Equal("", MarkupHotkeyText.Format(new KeyChord(Key.F, ModifierMask.None, Device: 1)));
        Assert.Equal("", MarkupHotkeyText.Format(new KeyChord(Key.Unknown, ModifierMask.Ctrl)));
        Assert.Equal("", MarkupHotkeyText.Format(new KeyChord(Key.ShiftLeft, ModifierMask.Shift)));
        Assert.Equal("", MarkupHotkeyText.Format(default));
    }

    [Theory]
    [InlineData("Ctrl+Alt+Shift+F", "Ctrl+Alt+Shift+F")]
    [InlineData("shift+ctrl+f", "Ctrl+Shift+F")]
    [InlineData("F12", "F12")]
    [InlineData("Keypad5", "Keypad5")]
    [InlineData("Alt+Number1", "Alt+Number1")]
    public void Parse_ReadsAndRoundTripsToCanonical(string text, string canonical)
    {
        KeyChord? chord = MarkupHotkeyText.Parse(text);
        Assert.NotNull(chord);
        Assert.Equal(canonical, MarkupHotkeyText.Format(chord.Value));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Ctrl+")]
    [InlineData("+F")]
    [InlineData("Ctrl+Ctrl+F")]
    [InlineData("Meta+F")]
    [InlineData("5")]
    [InlineData("A,B")]
    [InlineData("Unknown")]
    [InlineData("ShiftLeft")]
    [InlineData("Ctrl+Nope")]
    public void Parse_UnreadableIsUnbound(string? text) =>
        Assert.Null(MarkupHotkeyText.Parse(text));

    [Fact]
    public void StringReader_ReadsEachCallAndNullIsEmpty()
    {
        var binding = new Binding();
        Func<string> read = MarkupBindings.BindRequiredStringReader("{Key}", binding, "hotkey value");
        Assert.Equal("Ctrl+F", read());
        binding.Key = null;
        Assert.Equal("", read());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("Ctrl+F")]
    [InlineData("{NotString}")]
    [InlineData("{Missing}")]
    public void StringReader_RejectsAnythingButAStringBinding(string? expression)
    {
        var ex = Assert.Throws<FormatException>(
            () => MarkupBindings.BindRequiredStringReader(expression, new Binding(), "hotkey value"));
        Assert.Contains("hotkey value", ex.Message);
    }

    [Fact]
    public void StringAction_Invokes()
    {
        var binding = new Binding();
        MarkupBindings.BindRequiredStringAction("{SetKey}", binding, "hotkey onchange")("Alt+G");
        Assert.Equal("Alt+G", binding.Last);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("SetKey")]
    [InlineData("{WrongAction}")]
    [InlineData("{Missing}")]
    public void StringAction_RejectsAbsentLiteralAndWrongTypes(string? expression)
    {
        var ex = Assert.Throws<FormatException>(
            () => MarkupBindings.BindRequiredStringAction(expression, new Binding(), "hotkey onchange"));
        Assert.Contains("hotkey onchange", ex.Message);
    }
}
```

- [ ] **Step 2: Run to verify they fail** — `dotnet test tests/AcDream.App.Tests/AcDream.App.Tests.csproj --filter "FullyQualifiedName~MarkupHotkeyTextTests"` → build error (types missing).

- [ ] **Step 3: Implement** — create `src/AcDream.App/UI/Markup/MarkupHotkeyText.cs`:

```csharp
using System.Text;
using AcDream.UI.Abstractions.Input;
using Silk.NET.Input;

namespace AcDream.App.UI.Markup;

/// <summary>
/// The hotkey string a markup <c>&lt;hotkey&gt;</c> hands its plugin:
/// <c>Ctrl+Alt+Shift+Key</c>, modifiers in that order and the key spelled
/// as its <see cref="Key"/> name. Empty is unbound.
/// </summary>
internal static class MarkupHotkeyText
{
    /// <summary>The canonical string for a keyboard chord; empty for one no hotkey can hold.</summary>
    public static string Format(KeyChord chord)
    {
        if (chord.Device != 0 || !IsChordKey(chord.Key))
            return string.Empty;
        var text = new StringBuilder();
        if ((chord.Modifiers & ModifierMask.Ctrl) != 0) text.Append("Ctrl+");
        if ((chord.Modifiers & ModifierMask.Alt) != 0) text.Append("Alt+");
        if ((chord.Modifiers & ModifierMask.Shift) != 0) text.Append("Shift+");
        return text.Append(chord.Key.ToString()).ToString();
    }

    /// <summary>
    /// Reads a hotkey string: modifiers in any order and any case, then the
    /// key. Null for empty or unreadable text, which shows as unbound.
    /// </summary>
    public static KeyChord? Parse(string? text)
    {
        if (string.IsNullOrEmpty(text))
            return null;
        string[] parts = text.Split('+');
        ModifierMask mods = ModifierMask.None;
        for (int i = 0; i < parts.Length - 1; i++)
        {
            ModifierMask flag = ModifierNamed(parts[i]);
            if (flag == ModifierMask.None || (mods & flag) != 0)
                return null;
            mods |= flag;
        }
        string name = parts[^1];
        if (name.Length == 0 || !char.IsAsciiLetter(name[0]) || !name.All(char.IsAsciiLetterOrDigit)
            || !Enum.TryParse(name, ignoreCase: true, out Key key) || !IsChordKey(key))
            return null;
        return new KeyChord(key, mods);
    }

    /// <summary>A key a hotkey can end on: a named key that isn't a modifier.</summary>
    public static bool IsChordKey(Key key) => key != Key.Unknown && Enum.IsDefined(key) && !IsModifier(key);

    /// <summary>Shift, Ctrl, Alt or Super, left or right.</summary>
    public static bool IsModifier(Key key) => key is Key.ShiftLeft or Key.ShiftRight
        or Key.ControlLeft or Key.ControlRight or Key.AltLeft or Key.AltRight
        or Key.SuperLeft or Key.SuperRight;

    private static ModifierMask ModifierNamed(string name) =>
        string.Equals(name, "Ctrl", StringComparison.OrdinalIgnoreCase) ? ModifierMask.Ctrl
        : string.Equals(name, "Alt", StringComparison.OrdinalIgnoreCase) ? ModifierMask.Alt
        : string.Equals(name, "Shift", StringComparison.OrdinalIgnoreCase) ? ModifierMask.Shift
        : ModifierMask.None;
}
```

  In `MarkupBindings.cs`, after `BindRequiredFloatReader`, add:

```csharp
    /// <summary>A required <c>string</c> property, read on each call; null reads as empty.</summary>
    public static Func<string> BindRequiredStringReader(string? expression, object binding, string context)
    {
        if (string.IsNullOrWhiteSpace(expression) || !MarkupDocument.IsBinding(expression))
            throw new FormatException($"{context} must be a string binding");
        PropertyInfo? property = binding.GetType().GetProperty(expression[1..^1]);
        if (property is null || property.PropertyType != typeof(string))
        {
            throw new FormatException(
                $"{expression} did not resolve to a string property on "
                + binding.GetType().Name + $" ({context})");
        }
        return () => property.GetValue(binding) as string ?? string.Empty;
    }

    /// <summary>A required <c>Action&lt;string&gt;</c>.</summary>
    public static Action<string> BindRequiredStringAction(string? attribute, object binding, string context) =>
        BindOptionalAction<string>(attribute, binding, context, "string")
        ?? throw new FormatException($"{context} must be an Action<string> binding");
```

- [ ] **Step 4: Run to verify they pass** — same filter → all pass.

- [ ] **Step 5: Commit**

```bash
git add src/AcDream.App/UI/Markup/MarkupHotkeyText.cs src/AcDream.App/UI/Markup/MarkupBindings.cs tests/AcDream.App.Tests/UI/MarkupHotkeyTextTests.cs
git commit -m "feat(ui): canonical hotkey text and string bind helpers

Claude-Session: https://claude.ai/code/session_014SAojwkxRKxrwp1RMDGPVC"
```

---

### Task 2: `UiMarkupHotkey` widget

**Goal:** A widget that shows the bound chord, captures the next chord per the Global Constraints' capture rules, hands focus back after a click capture, and draws Classic and themed looks.

**Files:**
- Create: `src/AcDream.App/UI/UiMarkupHotkey.cs`
- Test: `tests/AcDream.App.Tests/UI/UiMarkupHotkeyTests.cs`

**Acceptance Criteria:**
- [ ] Mounted in a `UiRoot`: a left press+release on the box focuses it and starts capture (`DisplayText` == `"Press a key..."`); `Ctrl` down (modifier) keeps waiting; `F` down with Ctrl held reports `"Ctrl+F"` once; focus is still the box until `F` goes up, then `root.KeyboardFocus` is null.
- [ ] Escape stops with no report; Backspace and Delete report `""` (and nothing when the value is already empty); a chord equal to the bound string reports nothing.
- [ ] A key while Win is held keeps waiting.
- [ ] Focused by `root.SetKeyboardFocus`, Enter starts capture; a repeated Enter KeyDown is ignored; after Enter's KeyUp, Enter is captured as `"Enter"`; focus stays on the box.
- [ ] Losing focus while capturing stops with no report; becoming disabled stops on the next `root.Tick(dt, nowMs)`; a disabled box ignores click and Enter.
- [ ] Display: unbound/unreadable → `"None"`; bound with no `DescribeKey` → canonical string; `DescribeKey` is called once per distinct value, not every read.
- [ ] Draw: Classic unfocused → 1 background quad, 4 `ClassicBorder` quads; capturing → at least 4 `FocusColor` quads and no `ClassicBorder` quads; `theme` Moss → 1 `Field`, 4 `Border` quads; disabled Classic → 4 border quads at alpha × `DisabledAlpha`.

**Verify:** `dotnet test tests/AcDream.App.Tests/AcDream.App.Tests.csproj --filter "FullyQualifiedName~UiMarkupHotkeyTests"` → all pass.

**Steps:**

- [ ] **Step 1: Write the failing tests** — create `tests/AcDream.App.Tests/UI/UiMarkupHotkeyTests.cs`:

```csharp
using System.Numerics;
using AcDream.App.UI;
using AcDream.UI.Abstractions.Input;
using Silk.NET.Input;

namespace AcDream.App.Tests.UI;

public class UiMarkupHotkeyTests
{
    private sealed class Model
    {
        public string Value = "";
        public List<string> Reports { get; } = new();
        public ModifierMask Mods = ModifierMask.None;
    }

    private static (UiRoot Root, UiMarkupHotkey Box, Model Model) Mount(string value = "")
    {
        var model = new Model { Value = value };
        var box = new UiMarkupHotkey
        {
            Left = 10, Top = 10, Width = 140, Height = 20,
            ValueSource = () => model.Value,
            Changed = v => { model.Reports.Add(v); model.Value = v; },
            ModifiersSource = () => model.Mods,
        };
        var root = new UiRoot { Width = 800, Height = 600 };
        root.AddChild(box);
        return (root, box, model);
    }

    private static void Click(UiRoot root)
    {
        root.OnMouseDown(UiMouseButton.Left, 20, 15);
        root.OnMouseUp(UiMouseButton.Left, 20, 15);
    }

    private static void Press(UiRoot root, Key key)
    {
        root.OnKeyDown((int)key);
        root.OnKeyUp((int)key);
    }

    [Fact]
    public void Click_CapturesAChordAndGivesFocusBackOnRelease()
    {
        var (root, box, model) = Mount();
        Click(root);
        Assert.Same(box, root.KeyboardFocus);
        Assert.True(box.IsCapturing);
        Assert.Equal(UiMarkupHotkey.PromptText, box.DisplayText);

        model.Mods = ModifierMask.Ctrl;
        root.OnKeyDown((int)Key.ControlLeft);
        Assert.True(box.IsCapturing);
        root.OnKeyDown((int)Key.F);
        Assert.False(box.IsCapturing);
        Assert.Equal(["Ctrl+F"], model.Reports);
        Assert.Same(box, root.KeyboardFocus);   // still swallowing F until it goes up
        root.OnKeyUp((int)Key.F);
        Assert.Null(root.KeyboardFocus);
    }

    [Fact]
    public void Escape_CancelsWithoutReporting()
    {
        var (root, box, model) = Mount("Ctrl+F");
        Click(root);
        Press(root, Key.Escape);
        Assert.False(box.IsCapturing);
        Assert.Empty(model.Reports);
        Assert.Null(root.KeyboardFocus);
    }

    [Theory]
    [InlineData(Key.Backspace)]
    [InlineData(Key.Delete)]
    public void BackspaceOrDelete_ReportsUnbound(Key key)
    {
        var (root, _, model) = Mount("Ctrl+F");
        Click(root);
        Press(root, key);
        Assert.Equal([""], model.Reports);
    }

    [Fact]
    public void ClearingAnUnboundKey_ReportsNothing()
    {
        var (root, _, model) = Mount("");
        Click(root);
        Press(root, Key.Backspace);
        Assert.Empty(model.Reports);
    }

    [Fact]
    public void TheSameChord_ReportsNothing()
    {
        var (root, _, model) = Mount("F5");
        Click(root);
        Press(root, Key.F5);
        Assert.Empty(model.Reports);
    }

    [Fact]
    public void AKeyWithWinHeld_KeepsWaiting()
    {
        var (root, box, model) = Mount();
        Click(root);
        model.Mods = ModifierMask.Win;
        root.OnKeyDown((int)Key.E);
        Assert.True(box.IsCapturing);
        Assert.Empty(model.Reports);
    }

    [Fact]
    public void Enter_StartsAKeyboardCaptureThatKeepsFocus()
    {
        var (root, box, model) = Mount();
        root.SetKeyboardFocus(box);
        root.OnKeyDown((int)Key.Enter);
        Assert.True(box.IsCapturing);
        root.OnKeyDown((int)Key.Enter);   // the starting key repeating
        Assert.True(box.IsCapturing);
        root.OnKeyUp((int)Key.Enter);
        Press(root, Key.Enter);
        Assert.Equal(["Enter"], model.Reports);
        Assert.Same(box, root.KeyboardFocus);
    }

    [Fact]
    public void LosingFocus_CancelsWithoutReporting()
    {
        var (root, box, model) = Mount();
        Click(root);
        root.SetKeyboardFocus(null);
        Assert.False(box.IsCapturing);
        Press(root, Key.G);
        Assert.Empty(model.Reports);
    }

    [Fact]
    public void Disabling_CancelsOnTheNextTick()
    {
        var (root, box, model) = Mount();
        Click(root);
        box.Enabled = false;
        root.Tick(0.016, 16);
        Assert.False(box.IsCapturing);
        Assert.Null(root.KeyboardFocus);
        Assert.Empty(model.Reports);
    }

    [Fact]
    public void Disabled_IgnoresClickAndEnter()
    {
        var (root, box, _) = Mount();
        box.Enabled = false;
        box.OnEvent(new UiEvent { Type = UiEventType.Click });
        box.OnEvent(new UiEvent { Type = UiEventType.KeyDown, Data0 = (int)Key.Enter });
        Assert.False(box.IsCapturing);
    }

    [Theory]
    [InlineData("", UiMarkupHotkey.UnboundText)]
    [InlineData("Ctrl+Nope", UiMarkupHotkey.UnboundText)]
    [InlineData("shift+ctrl+f", "Ctrl+Shift+F")]
    public void DisplayText_WithoutKeyNamesShowsTheCanonicalString(string value, string shown)
    {
        var (_, box, _) = Mount(value);
        Assert.Equal(shown, box.DisplayText);
    }

    [Fact]
    public void DescribeKey_RunsOncePerDistinctValue()
    {
        var (_, box, model) = Mount("Ctrl+F");
        int calls = 0;
        box.DescribeKey = chord => { calls++; return "described " + chord.Key; };
        Assert.Equal("described F", box.DisplayText);
        Assert.Equal("described F", box.DisplayText);
        Assert.Equal(1, calls);
        model.Value = "G";
        Assert.Equal("described G", box.DisplayText);
        Assert.Equal(2, calls);
    }

    [Fact]
    public void Classic_DrawsFillAndBorder()
    {
        var (_, box, _) = Mount("F5");
        var renderer = MarkupDrawProbe.Draw(box);
        Assert.Equal(1, MarkupDrawProbe.CountQuads(renderer, UiMarkupHotkey.ClassicBackground));
        Assert.Equal(4, MarkupDrawProbe.CountQuads(renderer, UiMarkupHotkey.ClassicBorder));
    }

    [Fact]
    public void Capturing_DrawsTheFocusBorder()
    {
        var (root, box, _) = Mount("F5");
        Click(root);
        var renderer = MarkupDrawProbe.Draw(box);
        // The outline's 4 quads; the prompt text is gold too, but glyphs are textured.
        Assert.True(MarkupDrawProbe.CountQuads(renderer, UiMarkupHotkey.FocusColor) >= 4);
        Assert.Equal(0, MarkupDrawProbe.CountQuads(renderer, UiMarkupHotkey.ClassicBorder));
    }

    [Fact]
    public void PluginTheme_DrawsPaletteFillAndBorder()
    {
        var (_, box, _) = Mount("F5");
        box.ThemePalette = PluginUiPalette.Moss;
        var renderer = MarkupDrawProbe.Draw(box);
        Assert.Equal(1, MarkupDrawProbe.CountQuads(renderer, PluginUiPalette.Moss.Field));
        Assert.Equal(4, MarkupDrawProbe.CountQuads(renderer, PluginUiPalette.Moss.Border));
    }

    [Fact]
    public void Disabled_DrawsFaded()
    {
        var (_, box, _) = Mount("F5");
        box.Enabled = false;
        var renderer = MarkupDrawProbe.Draw(box);
        Vector4 faded = UiMarkupHotkey.ClassicBorder with
        {
            W = UiMarkupHotkey.ClassicBorder.W * UiMarkupToggle.DisabledAlpha,
        };
        Assert.Equal(4, MarkupDrawProbe.CountQuads(renderer, faded));
    }
}
```

  If `UiMouseButton`, `root.Tick(dt, nowMs)` or `UiEvent { … }` initialisers differ from what the existing tests use, copy the form from `MarkupCheckboxTests.cs` and `MarkupNumberTests.cs` rather than changing the widget. If `MarkupDrawProbe.Draw` needs the box at the origin for quad counts, that's fine; only counts are checked.

- [ ] **Step 2: Run to verify they fail** — `--filter "FullyQualifiedName~UiMarkupHotkeyTests"` → build error.

- [ ] **Step 3: Implement** — create `src/AcDream.App/UI/UiMarkupHotkey.cs`:

```csharp
using System.Numerics;
using AcDream.App.UI.Markup;
using AcDream.UI.Abstractions.Input;
using Silk.NET.Input;

namespace AcDream.App.UI;

/// <summary>
/// A markup hotkey box: shows the bound key chord and, once clicked (or
/// Enter/Space while focused), records the next chord the player presses.
/// It reports the canonical string (<see cref="MarkupHotkeyText"/>); the
/// plugin stores it and the box shows whatever <see cref="ValueSource"/>
/// returns next frame. It holds keyboard focus while recording, so the game
/// never sees the keys.
/// </summary>
public sealed class UiMarkupHotkey : UiElement
{
    public const string PromptText = "Press a key...";
    public const string UnboundText = "None";
    public const float TextInset = 4f;

    public static readonly Vector4 ClassicBackground = new(0f, 0f, 0f, 0.9f);
    public static readonly Vector4 ClassicBorder = UiCheckLamp.UncheckedInner;
    public static readonly Vector4 ClassicText = new(0.91f, 0.87f, 0.76f, 1f);
    public static readonly Vector4 ClassicMuted = new(0.5f, 0.5f, 0.5f, 1f);
    public static readonly Vector4 FocusColor = new(1f, 0.82f, 0.25f, 1f);

    public PluginUiPalette? ThemePalette { get; set; }
    public UiDatFont? DatFont { get; set; }

    /// <summary>The bound hotkey string.</summary>
    public Func<string?>? ValueSource { get; set; }

    /// <summary>Called with the new canonical string ("" to unbind) when it differs from the bound one.</summary>
    public Action<string>? Changed { get; set; }

    /// <summary>How a chord is shown (the retail key names); the canonical string when null.</summary>
    public Func<KeyChord, string>? DescribeKey { get; set; }

    /// <summary>The modifiers held now. Defaults to the root's keyboard.</summary>
    public Func<ModifierMask>? ModifiersSource { get; set; }

    public bool IsCapturing { get; private set; }
    public bool IsFocused { get; private set; }

    private bool _releaseFocus;   // a click began the capture: hand the keys back to the game after
    private Key? _startKey;       // the Enter/Space that began a keyboard capture, until it goes up
    private Key? _endKey;         // the key that ended a click capture; focus goes on its release
    private bool _shownValid;
    private string? _shownValue;
    private string _shownText = UnboundText;
    private bool _shownBound;

    public UiMarkupHotkey()
    {
        AcceptsFocus = true;
        FocusOnMouseClick = true;
        TabStop = true;
    }

    public override bool HandlesClick => true;

    /// <summary>The prompt while capturing, else the bound chord's name or <see cref="UnboundText"/>.</summary>
    public string DisplayText
    {
        get
        {
            if (IsCapturing)
                return PromptText;
            RefreshShown();
            return _shownText;
        }
    }

    private void RefreshShown()
    {
        string? value = ValueSource?.Invoke();
        if (_shownValid && string.Equals(value, _shownValue, StringComparison.Ordinal))
            return;
        _shownValid = true;
        _shownValue = value;
        KeyChord? chord = MarkupHotkeyText.Parse(value);
        _shownBound = chord is not null;
        _shownText = chord is { } c ? DescribeKey?.Invoke(c) ?? MarkupHotkeyText.Format(c) : UnboundText;
    }

    private void Begin(bool releaseFocus, Key? startKey)
    {
        if (!Enabled || IsCapturing)
            return;
        IsCapturing = true;
        _releaseFocus = releaseFocus;
        _startKey = startKey;
        _endKey = null;
    }

    /// <summary>Stops recording; a click capture gives focus back when <paramref name="endKey"/> goes up (at once without one).</summary>
    private void End(Key? endKey)
    {
        IsCapturing = false;
        _startKey = null;
        if (!_releaseFocus)
            return;
        if (endKey is null)
            ReleaseFocus();
        else
            _endKey = endKey;
    }

    private void ReleaseFocus()
    {
        _releaseFocus = false;
        _endKey = null;
        if (FindRoot() is { } root && ReferenceEquals(root.KeyboardFocus, this))
            root.SetKeyboardFocus(null);
    }

    private void Report(string value)
    {
        if (!string.Equals(value, ValueSource?.Invoke() ?? string.Empty, StringComparison.Ordinal))
            Changed?.Invoke(value);
    }

    public override bool OnEvent(in UiEvent e)
    {
        switch (e.Type)
        {
            case UiEventType.FocusGained:
                IsFocused = true;
                return true;
            case UiEventType.FocusLost:
                IsFocused = false;
                IsCapturing = false;
                _startKey = null;
                _endKey = null;
                _releaseFocus = false;
                return true;
            case UiEventType.Click:
                Begin(releaseFocus: true, startKey: null);
                return true;
            case UiEventType.KeyDown:
                return KeyDown((Key)e.Data0);
            case UiEventType.KeyUp:
                return KeyUp((Key)e.Data0);
        }
        return false;
    }

    private bool KeyDown(Key key)
    {
        if (!IsCapturing)
        {
            if (!Enabled || key is not (Key.Enter or Key.KeypadEnter or Key.Space))
                return false;
            Begin(releaseFocus: false, startKey: key);
            return true;
        }
        if (key == _startKey)
            return true;
        if (key == Key.Escape)
        {
            End(key);
            return true;
        }
        if (key is Key.Backspace or Key.Delete)
        {
            End(key);
            Report(string.Empty);
            return true;
        }
        ModifierMask mods = Modifiers();
        if (!MarkupHotkeyText.IsChordKey(key) || (mods & ModifierMask.Win) != 0)
            return true;   // a modifier alone (or Win held) is not a chord yet
        End(key);
        Report(MarkupHotkeyText.Format(new KeyChord(key, mods)));
        return true;
    }

    private bool KeyUp(Key key)
    {
        if (key == _startKey)
        {
            _startKey = null;
            return true;
        }
        if (key == _endKey)
        {
            ReleaseFocus();
            return true;
        }
        return IsCapturing;
    }

    private ModifierMask Modifiers()
    {
        if (ModifiersSource is { } source)
            return source();
        if (FindRoot()?.Keyboard is not { } keyboard)
            return ModifierMask.None;
        bool Held(Key left, Key right) => keyboard.IsKeyPressed(left) || keyboard.IsKeyPressed(right);
        ModifierMask mods = ModifierMask.None;
        if (Held(Key.ControlLeft, Key.ControlRight)) mods |= ModifierMask.Ctrl;
        if (Held(Key.AltLeft, Key.AltRight)) mods |= ModifierMask.Alt;
        if (Held(Key.ShiftLeft, Key.ShiftRight)) mods |= ModifierMask.Shift;
        if (Held(Key.SuperLeft, Key.SuperRight)) mods |= ModifierMask.Win;
        return mods;
    }

    protected override void OnTick(double deltaSeconds)
    {
        if (IsCapturing && !Enabled)
            End(endKey: null);
    }

    protected override void OnDraw(UiRenderContext ctx)
    {
        PluginUiPalette? p = ThemePalette;
        Vector4 focus = p?.Accent ?? FocusColor;
        ctx.DrawFill(0f, 0f, Width, Height, Fade(p?.Field ?? ClassicBackground));
        ctx.DrawRectOutline(0f, 0f, Width, Height,
            Fade(IsFocused || IsCapturing ? focus : p?.Border ?? ClassicBorder), 1f);

        string text = DisplayText;
        Vector4 color = IsCapturing ? focus
            : _shownBound ? p?.Text ?? ClassicText
            : p?.Muted ?? ClassicMuted;
        UiCaption.Draw(ctx, DatFont, text, TextInset, 0f, Height, Fade(color), outline: p is null);
    }

    private Vector4 Fade(Vector4 color) =>
        Enabled ? color : color with { W = color.W * UiMarkupToggle.DisabledAlpha };
}
```

  `UiCaption` is `internal`; the widget is `public` but only uses it inside a method, which is fine. If `FindRoot()` is not accessible from a subclass (it is used by `UiMarkupNumber`, so it is), keep the call as written.

- [ ] **Step 4: Run to verify they pass** — same filter → all pass. Also run `--filter "FullyQualifiedName~UiRoot"` → no new failures.

- [ ] **Step 5: Commit**

```bash
git add src/AcDream.App/UI/UiMarkupHotkey.cs tests/AcDream.App.Tests/UI/UiMarkupHotkeyTests.cs
git commit -m "feat(ui): markup hotkey capture widget

Claude-Session: https://claude.ai/code/session_014SAojwkxRKxrwp1RMDGPVC"
```

---

### Task 3: `<hotkey>` builder, document wiring, theme and retail key names

**Goal:** `<hotkey>` builds from markup, binds `value`/`onchange`, follows the plugin theme, and plugin windows in the client show retail key names.

**Files:**
- Create: `src/AcDream.App/UI/Markup/MarkupHotkey.cs`
- Modify: `src/AcDream.App/UI/Markup/MarkupBuildContext.cs` (`DescribeKey`)
- Modify: `src/AcDream.App/UI/MarkupDocument.cs` (`Build` + `AddElement` parameter, `Context()`, the two recursive `AddElement` calls, `case "hotkey"` after `case "number"`)
- Modify: `src/AcDream.App/UI/PluginMarkupTheme.cs` (font `case` and palette `case`)
- Modify: `src/AcDream.App/UI/RetailUiRuntime.cs` (`MountPlugins`)
- Test: `tests/AcDream.App.Tests/UI/MarkupHotkeyTests.cs`

**Acceptance Criteria:**
- [ ] `<hotkey x y w h value onchange/>` builds one `UiMarkupHotkey` with the position, `ValueSource`, `Changed` and the document's `DatFont`; `name`, `enabled` and `tooltip` apply through `ApplyCommon`.
- [ ] Missing/literal/mistyped `value` or `onchange` fails the build with a message containing `hotkey value` / `hotkey onchange`.
- [ ] `MarkupDocument.Build(..., describeKey: f)` reaches the widget's `DescribeKey`; without it `DescribeKey` is null.
- [ ] Under `theme="plugin"` with Moss, the widget's `ThemePalette` is `PluginUiPalette.Moss`.
- [ ] A press+release through `UiRoot` on a built `<hotkey>` focuses it and starts capture (the one-UiRoot-mounted-test rule; the hotkey takes focus by design).
- [ ] `RetailUiRuntime.MountPlugins` passes a `describeKey` built from `Layout.RetailKeyNames` over a `DatStringResolver`, constructed and called under `_bindings.Assets.DatLock`.
- [ ] `dotnet build AcDream.slnx -c Release` → 0 warnings, 0 errors.

**Verify:** `dotnet test tests/AcDream.App.Tests/AcDream.App.Tests.csproj --filter "FullyQualifiedName~Hotkey|FullyQualifiedName~AcDream.App.Tests.UI.Markup"` → all pass.

**Steps:**

- [ ] **Step 1: Write the failing tests** — create `tests/AcDream.App.Tests/UI/MarkupHotkeyTests.cs`:

```csharp
using AcDream.App.UI;
using AcDream.UI.Abstractions.Input;
using Silk.NET.Input;

namespace AcDream.App.Tests.UI;

public class MarkupHotkeyTests
{
    private sealed class Binding
    {
        public string NavKey { get; set; } = "Ctrl+N";
        public int NotString { get; set; }
        public string? Last { get; private set; }
        public Action<string> SetNavKey => v => { NavKey = v; Last = v; };
        public Action<int> WrongAction => _ => { };
    }

    private const string Attrs = "value=\"{NavKey}\" onchange=\"{SetNavKey}\"";

    private static UiMarkupHotkey BuildOne(string attributes, Binding binding,
        Func<KeyChord, string>? describeKey = null)
    {
        var panel = MarkupDocument.Build(
            $"<panel x=\"0\" y=\"0\" w=\"300\" h=\"100\"><hotkey x=\"8\" y=\"8\" w=\"140\" h=\"20\" {attributes}/></panel>",
            binding, _ => (1u, 16, 16), describeKey: describeKey);
        return Assert.IsType<UiMarkupHotkey>(Assert.Single(panel.Children));
    }

    [Fact]
    public void Hotkey_BuildsWithPositionAndBindings()
    {
        var binding = new Binding();
        var hotkey = BuildOne(Attrs + " name=\"nav\" enabled=\"false\"", binding);
        Assert.Equal((8f, 8f, 140f, 20f), (hotkey.Left, hotkey.Top, hotkey.Width, hotkey.Height));
        Assert.Equal("Ctrl+N", hotkey.ValueSource!());
        hotkey.Changed!("Alt+G");
        Assert.Equal("Alt+G", binding.Last);
        Assert.False(hotkey.Enabled);
        Assert.Null(hotkey.DescribeKey);
    }

    [Theory]
    [InlineData("onchange=\"{SetNavKey}\"", "hotkey value")]
    [InlineData("value=\"Ctrl+N\" onchange=\"{SetNavKey}\"", "hotkey value")]
    [InlineData("value=\"{NotString}\" onchange=\"{SetNavKey}\"", "hotkey value")]
    [InlineData("value=\"{NavKey}\"", "hotkey onchange")]
    [InlineData("value=\"{NavKey}\" onchange=\"{WrongAction}\"", "hotkey onchange")]
    [InlineData("value=\"{NavKey}\" onchange=\"{Missing}\"", "hotkey onchange")]
    public void Hotkey_BadBindingsFailTheBuild(string attributes, string context)
    {
        var ex = Assert.Throws<FormatException>(() => BuildOne(attributes, new Binding()));
        Assert.Contains(context, ex.Message);
    }

    [Fact]
    public void Hotkey_GetsTheDocumentsKeyNames()
    {
        var hotkey = BuildOne(Attrs, new Binding(), chord => "named " + chord.Key);
        Assert.Equal("named N", hotkey.DisplayText);
    }

    [Fact]
    public void Hotkey_FollowsThePluginTheme()
    {
        var themes = new PluginUiThemeSettings { Theme = PluginUiTheme.Moss };
        var panel = MarkupDocument.Build(
            "<panel theme=\"plugin\" x=\"0\" y=\"0\" w=\"300\" h=\"100\">"
            + $"<hotkey x=\"8\" y=\"8\" w=\"140\" h=\"20\" {Attrs}/></panel>",
            new Binding(), _ => (1u, 16, 16), themes: themes);
        Assert.Same(PluginUiPalette.Moss, panel.Children.OfType<UiMarkupHotkey>().Single().ThemePalette);
    }

    [Fact]
    public void Hotkey_ClickThroughUiRootTakesFocusAndCaptures()
    {
        var binding = new Binding();
        var panel = MarkupDocument.Build(
            $"<panel x=\"0\" y=\"0\" w=\"300\" h=\"100\"><hotkey x=\"8\" y=\"8\" w=\"140\" h=\"20\" {Attrs}/></panel>",
            binding, _ => (1u, 16, 16));
        var hotkey = (UiMarkupHotkey)panel.Children[0];
        hotkey.ModifiersSource = () => ModifierMask.Alt;
        var root = new UiRoot { Width = 800, Height = 600 };
        root.AddChild(panel);
        root.OnMouseDown(UiMouseButton.Left, 20, 15);
        root.OnMouseUp(UiMouseButton.Left, 20, 15);
        Assert.Same(hotkey, root.KeyboardFocus);
        root.OnKeyDown((int)Key.G);
        root.OnKeyUp((int)Key.G);
        Assert.Equal("Alt+G", binding.Last);
        Assert.Null(root.KeyboardFocus);
    }
}
```

- [ ] **Step 2: Run to verify they fail** — `--filter "FullyQualifiedName~MarkupHotkeyTests"` → build error (`describeKey` parameter missing).

- [ ] **Step 3: Context** — in `MarkupBuildContext.cs` add `using AcDream.UI.Abstractions.Input;`, a sentence to the summary ("…, the host's key names for hotkeys (null shows the canonical string), …"), and a last record parameter:

```csharp
    Action<UiElement, XElement>? AddChild = null,
    Func<KeyChord, string>? DescribeKey = null);
```

- [ ] **Step 4: Document wiring** — in `MarkupDocument.cs` (add `using AcDream.UI.Abstractions.Input;` if absent):
  - `Build(...)` gains a last parameter `Func<KeyChord, string>? describeKey = null` after `controlState`, passed to the top-level `AddElement` call.
  - `AddElement(...)` gains a last parameter `Func<KeyChord, string>? describeKey`. Pass it on in the `Context()` `AddChild` lambda, in the `group` child loop, and as `DescribeKey: describeKey` in `Context()` (named, after `controlState` and the `AddChild` lambda).
  - After `case "number":` add:

```csharp
            case "hotkey":
                parent.AddChild(MarkupHotkey.Build(el, binding, Context()));
                break;
```

  `grep -n "AddElement(" src/AcDream.App/UI/MarkupDocument.cs src/AcDream.App/UI/Markup/*.cs` must show every call passing `describeKey`.

- [ ] **Step 5: Builder** — create `src/AcDream.App/UI/Markup/MarkupHotkey.cs`:

```csharp
using System.Xml.Linq;

namespace AcDream.App.UI.Markup;

internal static class MarkupHotkey
{
    public static UiMarkupHotkey Build(XElement el, object binding, MarkupBuildContext context)
    {
        var hotkey = new UiMarkupHotkey
        {
            Left = MarkupDocument.F(el, "x"),
            Top = MarkupDocument.F(el, "y"),
            Width = MarkupDocument.F(el, "w"),
            Height = MarkupDocument.F(el, "h"),
            ValueSource = MarkupBindings.BindRequiredStringReader(
                (string?)el.Attribute("value"), binding, "hotkey value"),
            Changed = MarkupBindings.BindRequiredStringAction(
                (string?)el.Attribute("onchange"), binding, "hotkey onchange"),
            DescribeKey = context.DescribeKey,
            DatFont = context.DatFont,
        };
        MarkupDocument.ApplyCommon(hotkey, el, binding);
        return hotkey;
    }
}
```

- [ ] **Step 6: Theme** — in `PluginMarkupTheme.Register`:
  - font switch, after the `UiMarkupNumber` line: `case UiMarkupHotkey hotkey: Bind(() => hotkey.DatFont, f => hotkey.DatFont = f); break;`
  - palette switch, after the `UiMarkupNumber` case:

```csharp
            case UiMarkupHotkey hotkey:
                panel.AddThemeAction(p => hotkey.ThemePalette = p);
                break;
```

- [ ] **Step 7: Retail key names** — in `RetailUiRuntime.MountPlugins`, after `iconResolver` is created:

```csharp
        // Hotkey boxes show keys by the game's own names. The table reads go
        // through the dat lock like every other plugin-thread dat read.
        Layout.RetailKeyNames keyNames;
        lock (_bindings.Assets.DatLock)
        {
            var keyStrings = new DatStringResolver(_bindings.Assets.Dats);
            keyNames = new Layout.RetailKeyNames((tableId, stringId) => keyStrings.Resolve(tableId, stringId));
        }
        Func<KeyChord, string> describeKey = chord =>
        {
            lock (_bindings.Assets.DatLock)
                return keyNames.Describe(chord);
        };
```

  and pass `describeKey: describeKey` to the `MarkupDocument.Build` call there. Add `using AcDream.UI.Abstractions.Input;` if `KeyChord` is not already in scope (the file already uses `KeyBindings`/`Binding`, so it probably is). Leave the `_pluginAppearance` build (line ~4186) alone: it has no `<hotkey>`.

- [ ] **Step 8: Run** — Verify filter → all pass. `dotnet build AcDream.slnx -c Release 2>&1 | grep -E 'Warning\(s\)|Error\(s\)'` → 0 and 0. `git checkout -- '*.lock.json'`.

- [ ] **Step 9: Commit**

```bash
git add src/AcDream.App/UI/Markup/MarkupHotkey.cs src/AcDream.App/UI/Markup/MarkupBuildContext.cs src/AcDream.App/UI/MarkupDocument.cs src/AcDream.App/UI/PluginMarkupTheme.cs src/AcDream.App/UI/RetailUiRuntime.cs tests/AcDream.App.Tests/UI/MarkupHotkeyTests.cs
git commit -m "feat(ui): <hotkey> markup element

Claude-Session: https://claude.ai/code/session_014SAojwkxRKxrwp1RMDGPVC"
```

---

### Task 4: Docs

**Goal:** `docs/plugin-ui-markup.md` documents `<hotkey>`.

**Files:**
- Modify: `docs/plugin-ui-markup.md`

**Acceptance Criteria:**
- [ ] The Elements table gains, after `number`: `| \`hotkey\` | A box that records a key chord | \`x y w h value onchange\` |`.
- [ ] The Bindings table gains `hotkey value` (`string`, `Ctrl+Alt+Shift+Key`, empty = unbound) and `hotkey onchange` (`Action<string>`).
- [ ] The paragraph "Clicking a `button`, `tab`, … without taking keyboard focus" gains a sentence that a `hotkey` does take focus on click, while it records.
- [ ] A `## Hotkey` section after `## Number field` and before `## Radio group` covers the example, the canonical string and parsing leniency, display (key names, `None`), starting (click, Enter/Space), what each key does (modifiers wait, Escape, Backspace/Delete, Tab, Win), focus afterwards, cancel cases, report-only-on-change, no mouse buttons, looks, build errors and the older-host line.
- [ ] Older-host line checked against base 391e709a: `git show 391e709a:src/AcDream.App/UI/MarkupDocument.cs | grep -n "unknown element"` shows unknown elements throw.

**Verify:** `grep -c 'hotkey' docs/plugin-ui-markup.md` → at least 6.

**Steps:**

- [ ] **Step 1: Tables and focus paragraph** — edit as listed, keeping the tables' style.

- [ ] **Step 2: Section** — insert before `## Radio group`:

````markdown
## Hotkey

```xml
<hotkey x="8" y="8" w="140" h="20" value="{ToggleNavKey}" onchange="{SetToggleNavKey}"/>
```

A `<hotkey>` shows a key chord and lets the player record a new one. The
plugin decides what the chord does; the box only edits the string.

- `value` binds a `string` and `onchange` an `Action<string>`; both are
  required. The string is `Ctrl+Alt+Shift+Key`: the modifiers that are held,
  in that order, then the key's name as Silk.NET's `Key` spells it (`F`,
  `F12`, `Number1`, `Keypad5`, `Enter`). Empty means unbound. When reading
  `value` the box also accepts modifiers in any order and any case
  (`shift+ctrl+f`), but `onchange` always hands back the exact form.
- The box shows the chord by the game's key names. An empty value, or one it
  can't read, shows `None`.
- Clicking the box, or Enter or Space while it has focus, starts recording:
  the box shows `Press a key...` and holds the keyboard, so the game sees
  none of the keys. Holding Shift, Ctrl or Alt alone keeps it waiting; the
  first other key ends it and `onchange` gets the chord. Escape stops
  without changing anything, and Backspace or Delete unbinds (`onchange`
  gets `""`). Keys pressed while the Windows/Command key is held are
  ignored. Tab can't be recorded: it moves the focus, which stops recording.
- `onchange` is called only when the new string differs from `value`.
- After a recording started by a click, the box gives the keyboard back to
  the game as soon as the last key is released. One started with Enter or
  Space keeps the focus, so Tab carries on to the next control.
- Clicking elsewhere, the window hiding, or the box being disabled stops a
  recording without changing anything.
- Mouse buttons are never recorded.

In Classic the box is a dark field with a thin border that turns gold while
it has focus; under `theme="plugin"` it takes the palette's field, border,
text, muted and accent colours and the panel's font.

A `<hotkey>` without `value` or `onchange`, or with either bound to the wrong
type, fails the build. On a host older than hotkey boxes, a `<hotkey>` fails
the build of its window as unknown.
````

  Re-check every sentence against `UiMarkupHotkey.cs` and `MarkupHotkey.cs`; fix the docs, not the code, if they differ.

- [ ] **Step 3: Commit**

```bash
git add docs/plugin-ui-markup.md
git commit -m "docs(ui): hotkey boxes

Claude-Session: https://claude.ai/code/session_014SAojwkxRKxrwp1RMDGPVC"
```

---

### Task 5: Series gate

**Goal:** The Release build is clean, App, Plugin and UI.Abstractions tests have no new failures, and every commit builds.

**Files:** none changed.

**Acceptance Criteria:**
- [ ] `dotnet build AcDream.slnx -c Release` → 0 errors, 0 warnings.
- [ ] App + Plugin failures ⊆ `mc-base.txt` (162 names); UI.Abstractions failures ⊆ `mc-uiabs-base.txt` (empty).
- [ ] Each of the series' commits builds and passes the markup/hotkey test filters on its own.
- [ ] Nothing under `docs/superpowers/` on the branch.

**Verify:** both `comm -13` comparisons print nothing.

**Steps:**

- [ ] **Step 1: Release build and tests**

```bash
export DOTNET_ROOT=$HOME/.dotnet PATH=$HOME/.dotnet:$PATH
cd /Users/davidsmith/code/OpenAC/OpenAC/.worktrees/markup-controls
T=/private/tmp/claude-501/-Users-davidsmith-code-OpenAC-OpenAC
norm() { grep -E '^[[:space:]]+Failed [A-Za-z]' | sed -E 's/^[[:space:]]+Failed //; s/ \[[^]]*\]$//' | sort -u; }
dotnet build AcDream.slnx -c Release 2>&1 | grep -E ': error|: warning|Warning\(s\)|Error\(s\)' | sort -u
for p in tests/AcDream.App.Tests/AcDream.App.Tests.csproj tests/AcDream.Plugin.Tests/AcDream.Plugin.Tests.csproj; do
  dotnet test "$p" -c Release 2>&1
done | norm > $T/mc11-after.txt
dotnet test tests/AcDream.UI.Abstractions.Tests/AcDream.UI.Abstractions.Tests.csproj -c Release 2>&1 | norm > $T/mc11-uiabs-after.txt
echo "new App/Plugin:"; comm -13 $T/mc-base.txt $T/mc11-after.txt
echo "new UI.Abstractions:"; comm -13 $T/mc-uiabs-base.txt $T/mc11-uiabs-after.txt
git checkout -- '*.lock.json' 2>/dev/null; git status --short
```
Expected: `0 Warning(s)`, `0 Error(s)`, nothing after either "new" line.

- [ ] **Step 2: Each commit builds alone**

```bash
cd /Users/davidsmith/code/OpenAC/OpenAC
for c in $(git rev-list --reverse 930a2f71..feat/markup-controls); do
  git worktree add -q --detach .worktrees/mc-commit-check $c
  (cd .worktrees/mc-commit-check && dotnet test tests/AcDream.App.Tests/AcDream.App.Tests.csproj -c Release \
     --filter "FullyQualifiedName~AcDream.App.Tests.UI.Markup|FullyQualifiedName~Hotkey|FullyQualifiedName~UiMarkupList|FullyQualifiedName~UiMenu|FullyQualifiedName~ContextMenu" 2>&1 \
     | grep -E ': error|: warning|Passed!|Failed!' | sort -u | sed "s/^/$(git rev-parse --short $c) /")
  git worktree remove --force .worktrees/mc-commit-check
done
```
Expected: one `Passed!` per commit, no `error`/`warning`.

- [ ] **Step 3: Branch contents**

```bash
cd /Users/davidsmith/code/OpenAC/OpenAC/.worktrees/markup-controls
git log --oneline 930a2f71..HEAD
git diff --stat 391e709a..HEAD | grep superpowers || echo "no superpowers docs"
```
Expected: the 4 commits of Tasks 1-4 (plus any review-fix commits); "no superpowers docs".

No commit.
