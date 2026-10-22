# Markup controls, plan 15: live-check fixes Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers-extended-cc:subagent-driven-development (recommended) or superpowers-extended-cc:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Fix the four defects the plan 14 live check found:
1. A focused `<number>` ignores Shift+mouse wheel on macOS.
2. The `<hotkey>` box shows Shift as "LSHIFT".
3. The Classic list header is hard to read on the chain art.
4. A focused single-select tree can't be driven by keyboard when nothing is selected.

**Architecture:** One commit per defect on `feat/markup-controls`, each test-first, then a gate and a short live re-check. Root causes, already established:
1. **Number Shift+wheel.** The diagnostic live run logged Shift+wheel ticks as `x=±1.3 y=0`; macOS turns Shift+wheel into a sideways scroll. `SilkRetainedMouseSurface.OnScroll` (`src/AcDream.App/UI/RetainedUiInputBinding.cs:122-127`) reads only `scroll.Y`, so those ticks are dropped. Unshifted wheel ticks and trackpad ticks with Shift (`y≠0`, `shift=True`) already step correctly.
2. **Hotkey LSHIFT.** `RetailUiRuntime` hands hotkey boxes `RetailKeyNames.Describe`. That function maps the generic `ModifierMask.Shift` to `Key.ShiftLeft` (`RetailKeyNames.cs:105-111`), so the name comes out as "LSHIFT". The recorder itself never keeps left or right.
3. **Classic header.** `UiMarkupList.DrawHeader` tiles the scrollbar-track sprite `RetailScrollbarChrome.Track` (0x06004C5F, gold chain) under the captions (`UiMarkupList.Header.cs:30-33`).
4. **Tree keys.**
   - `HandleTreeKey` (`UiMarkupList.Tree.cs:185`) acts on the selected row and returns at once when nothing is selected. The sample tree starts with `-1`.
   - A single-select list takes no Up/Down (`UiMarkupList.Selection.cs` returns `false` when `!MultiSelect`), so after Tab there's no keyboard way to pick a row.
   - The focus path from field to Tab to tree works (UiRoot moves focus before offering Tab to the field).

**Tech Stack:** C# / .NET 10, xUnit, headless `UiRoot` + `MarkupDrawProbe`, Silk.NET input.

**Spec:** `docs/superpowers/specs/2026-10-21-markup-controls-design.md` (branch `docs/markup-controls-spec`): §2 number, §8 list header, §10 tree, §12 hotkey, §16 live check. Evidence: plan 14 Status line (live check findings) on the same branch.

## Global Constraints

- **Workspace:** `/Users/davidsmith/code/OpenAC/OpenAC/.worktrees/markup-controls`, branch `feat/markup-controls`, HEAD `f399562b`.
  - Never merge fork `main` in. Never add `docs/superpowers/` or `.superpowers/` files to this branch.
  - Push only when the controller says so.
- **Toolchain:** `export DOTNET_ROOT=$HOME/.dotnet PATH=$HOME/.dotnet:$PATH`.
- **Upstream-portable:** no fork-only code (no `ScrollHorizontal`, flex or fonts).
- **Narrow changes:**
  - `RetailKeyNames` (the shared table, used by other client UI) does not change.
  - Other lists' Classic chrome (the scrollbar art) does not change.
  - Multi-select keyboard behaviour does not change.
- **Commits:** one commit per task. Each builds with 0 warnings, passes its own tests, and passes `FullyQualifiedName~AcDream.App.Tests.UI.Markup` and `FullyQualifiedName~ControlsDemoSample`. Each message ends with `Claude-Session: https://claude.ai/code/session_014SAojwkxRKxrwp1RMDGPVC`.
- **Docs:** each user-visible change updates its sentence in `docs/plugin-ui-markup.md`, in the same commit. Each task gives the exact text.

**User decisions (already made):**
- "Do the same as before: push now and then start on plan 15 after, like we did for the previous plans." (2026-10-22).
- Live re-check of these four items with the user before the fork merge (plan 14 Task 8 pattern; ask before launching).
- Planner rulings, to be reported to the user:
  - The Classic header uses the list's own dark background plus the existing gold underline and dividers. This reuses `UiMarkupList.BackgroundColor`, the same as the list body and the Classic context menu.
  - A focused single-select tree takes Up/Down. This reverses the series 9 test that let them fall through to the game.

---

### Task 1: Shift+mouse wheel steps a number field on macOS

**Goal:** A sideways wheel tick (`x≠0, y=0`) that arrives while Shift is held counts as a vertical wheel step. A focused `<number>` then steps 10× on Shift+mouse wheel. Sideways ticks without Shift still scroll nothing, and ticks with any vertical part are read exactly as before.

**Files:**
- Modify: `src/AcDream.App/UI/RetainedUiInputBinding.cs`. Changes: the `IRetainedMouseSurface` scroll callback becomes `Action<int, int>` (dx, dy); `SilkRetainedMouseSurface.OnScroll` reports both signs; `RetainedMouseInputBinding.OnScroll` resolves them with a new `RetainedWheel.Vertical`.
- Modify: `tests/AcDream.App.Tests/UI/RetainedUiInputBindingTests.cs` (the `MouseSurface` fake's scroll signature, plus new tests).
- Modify: `docs/plugin-ui-markup.md` (Number field section).

**Acceptance Criteria:**
- [ ] `RetainedWheel.Vertical(dx, dy, shift)` returns `Math.Sign(dy)` when `dy≠0`, `Math.Sign(dx)` when `dy=0` and `shift`, and 0 otherwise. A theory test covers each row in Step 1.
- [ ] `SilkRetainedMouseSurface` treats NaN as 0, reports `(Sign(x), Sign(y))`, and reports nothing when both are 0.
- [ ] The binding reads Shift from `UiRoot.Keyboard` (ShiftLeft or ShiftRight) and calls `UiRoot.OnScroll(step)` only for a non-zero step.
- [ ] The Number field docs gain the macOS sentence from Step 5.

**Verify:** `dotnet test tests/AcDream.App.Tests/AcDream.App.Tests.csproj -c Release --filter "FullyQualifiedName~RetainedUiInputBinding|FullyQualifiedName~MarkupNumber"` → `Passed!`.

**Steps:**

- [ ] **Step 1: Failing test.** In `RetainedUiInputBindingTests.cs`, add:

```csharp
    [Theory]
    [InlineData(0, 1, false, 1)]
    [InlineData(0, -1, true, -1)]
    [InlineData(1, 0, true, 1)]     // macOS sends Shift+wheel as a sideways tick
    [InlineData(-1, 0, true, -1)]
    [InlineData(1, 0, false, 0)]    // a sideways swipe without Shift scrolls nothing
    [InlineData(1, -1, true, -1)]   // any vertical part wins
    [InlineData(0, 0, true, 0)]
    public void Wheel_reads_a_sideways_tick_as_the_wheel_only_while_Shift_is_held(int dx, int dy, bool shift, int expected)
        => Assert.Equal(expected, RetainedWheel.Vertical(dx, dy, shift));
```

Run: `dotnet test tests/AcDream.App.Tests/AcDream.App.Tests.csproj -c Release --filter FullyQualifiedName~RetainedUiInputBinding`. Expected: build error, `RetainedWheel` does not exist.

- [ ] **Step 2: The helper.** At the end of `src/AcDream.App/UI/RetainedUiInputBinding.cs`:

```csharp
/// <summary>
/// Turns one wheel event into a vertical step. macOS delivers Shift+wheel
/// from a mouse as a sideways scroll, so a purely sideways tick counts as
/// the wheel while Shift is held; any vertical part is read as is.
/// </summary>
internal static class RetainedWheel
{
    public static int Vertical(int dx, int dy, bool shiftHeld) =>
        dy != 0 ? Math.Sign(dy) : shiftHeld ? Math.Sign(dx) : 0;
}
```

- [ ] **Step 3: Report both axes.**
  - In `IRetainedMouseSurface`, change `AddScroll(Action<int> callback)` and `RemoveScroll(Action<int> callback)` to take `Action<int, int>`.
  - In `SilkRetainedMouseSurface`, change the field to `private Action<int, int>? _scrollCallback;`, make `AddScroll`/`RemoveScroll` match, and replace `OnScroll` with:

```csharp
    private void OnScroll(IMouse _, ScrollWheel scroll)
    {
        // Each wheel event is a directional action, including small trackpad deltas.
        int dx = float.IsNaN(scroll.X) ? 0 : Math.Sign(scroll.X);
        int dy = float.IsNaN(scroll.Y) ? 0 : Math.Sign(scroll.Y);
        if (dx != 0 || dy != 0)
            _scrollCallback?.Invoke(dx, dy);
    }
```

  - In `RetainedMouseInputBinding`, change `private readonly Action<int> _scroll;` to `Action<int, int>` and replace its `OnScroll` with:

```csharp
    private void OnScroll(int dx, int dy) => Invoke(() =>
    {
        int step = RetainedWheel.Vertical(dx, dy, ShiftHeld());
        if (step != 0)
            _root.OnScroll(step);
    });

    private bool ShiftHeld() => _root.Keyboard is { } keyboard
        && (keyboard.IsKeyPressed(Key.ShiftLeft) || keyboard.IsKeyPressed(Key.ShiftRight));
```

  - In the test fake `MouseSurface`, change `_scroll`, `AddScroll` and `RemoveScroll` to `Action<int, int>`.
  - Fix any other implementer of `IRetainedMouseSurface` that the compiler names.

- [ ] **Step 4: Run.** Run the Verify command. Expected: `Passed!`.

- [ ] **Step 5: Docs.** In `docs/plugin-ui-markup.md`, "Number field", the sentence `Hold Shift for ten steps.` becomes:

```markdown
Hold Shift for ten steps. (macOS sends Shift with a mouse wheel as
sideways scrolling; the client reads a purely sideways tick while Shift is
held as the wheel, so it steps ten too.)
```

- [ ] **Step 6: Build and commit.**

```bash
dotnet build AcDream.slnx -c Release 2>&1 | grep -E 'Warning\(s\)|Error\(s\)'
git add src/AcDream.App/UI/RetainedUiInputBinding.cs tests/AcDream.App.Tests/UI/RetainedUiInputBindingTests.cs docs/plugin-ui-markup.md
git commit -m "fix(ui): Shift+mouse wheel steps a number field on macOS

Claude-Session: https://claude.ai/code/session_014SAojwkxRKxrwp1RMDGPVC"
```

---

### Task 2: Hotkey box names modifiers Ctrl, Alt and Shift

**Goal:** The `<hotkey>` box shows modifiers as `Ctrl+`, `Alt+` and `Shift+`, and only the key itself by the game's names. "Shift+F" no longer reads "LSHIFT+…". `RetailKeyNames` is unchanged.

**Files:**
- Modify: `src/AcDream.App/UI/Markup/MarkupHotkeyText.cs` (new `Describe`).
- Modify: `src/AcDream.App/UI/RetailUiRuntime.cs:4116-4120` (the `describeKey` lambda).
- Modify: `tests/AcDream.App.Tests/UI/MarkupHotkeyTextTests.cs`.
- Modify: `docs/plugin-ui-markup.md` (Hotkey section).

**Acceptance Criteria:**
- [ ] `MarkupHotkeyText.Describe(new KeyChord(Key.F, ModifierMask.Ctrl | ModifierMask.Shift), keyName)` returns `"Ctrl+Shift+" + keyName(new KeyChord(Key.F, ModifierMask.None))`. Modifiers come in `Format`'s order (Ctrl, Alt, Shift). `keyName` is called with no modifiers.
- [ ] A chord with no modifiers returns `keyName(chord)` unchanged.
- [ ] `RetailUiRuntime`'s hotkey `describeKey` uses `MarkupHotkeyText.Describe`, with `keyNames.Describe` (under the dat lock) as the key-name function.
- [ ] The Hotkey docs sentence reads as in Step 4.

**Verify:** `dotnet test tests/AcDream.App.Tests/AcDream.App.Tests.csproj -c Release --filter "FullyQualifiedName~MarkupHotkey|FullyQualifiedName~UiMarkupHotkey"` → `Passed!`.

**Steps:**

- [ ] **Step 1: Failing tests.** Add to `MarkupHotkeyTextTests.cs`. Add `using AcDream.UI.Abstractions.Input;` and `using Silk.NET.Input;` if the file lacks them.

```csharp
    [Fact]
    public void Describe_names_modifiers_plainly_and_only_the_key_by_the_game()
    {
        var asked = new List<KeyChord>();
        string text = MarkupHotkeyText.Describe(
            new KeyChord(Key.F, ModifierMask.Shift | ModifierMask.Ctrl | ModifierMask.Alt),
            chord => { asked.Add(chord); return "F-KEY"; });

        Assert.Equal("Ctrl+Alt+Shift+F-KEY", text);
        Assert.Equal([new KeyChord(Key.F, ModifierMask.None)], asked);
    }

    [Fact]
    public void Describe_without_modifiers_is_the_game_name()
    {
        Assert.Equal("NUMPAD 5", MarkupHotkeyText.Describe(new KeyChord(Key.Keypad5, ModifierMask.None), _ => "NUMPAD 5"));
    }
```

Run the Verify command. Expected: build error, `Describe` does not exist.

- [ ] **Step 2: Implement.** In `MarkupHotkeyText`, after `Format`:

```csharp
    /// <summary>
    /// What a hotkey box shows: the modifiers as Ctrl, Alt and Shift (the
    /// string never says which side), then the key by the given name, which
    /// is asked for the key alone.
    /// </summary>
    public static string Describe(KeyChord chord, Func<KeyChord, string> keyName)
    {
        var text = new StringBuilder();
        if ((chord.Modifiers & ModifierMask.Ctrl) != 0) text.Append("Ctrl+");
        if ((chord.Modifiers & ModifierMask.Alt) != 0) text.Append("Alt+");
        if ((chord.Modifiers & ModifierMask.Shift) != 0) text.Append("Shift+");
        return text.Append(keyName(chord with { Modifiers = ModifierMask.None })).ToString();
    }
```

In `RetailUiRuntime.cs`, replace the `describeKey` lambda with:

```csharp
        Func<KeyChord, string> describeKey = chord => Markup.MarkupHotkeyText.Describe(chord, key =>
        {
            lock (_bindings.Assets.DatLock)
                return keyNames.Describe(key);
        });
```

Use the namespace qualifier the file already uses for `AcDream.App.UI.Markup` types (check its `using`s), so the reference compiles.

- [ ] **Step 3: Run.** Run the Verify command. Expected: `Passed!`.

- [ ] **Step 4: Docs.** In `docs/plugin-ui-markup.md`, "Hotkey", replace `The box shows the chord by the game's key names.` with:

```markdown
The box shows the modifiers as Ctrl, Alt and Shift (never left or right,
since the string doesn't record a side) and the key by the game's own key
names.
```

- [ ] **Step 5: Build and commit.**

```bash
dotnet build AcDream.slnx -c Release 2>&1 | grep -E 'Warning\(s\)|Error\(s\)'
git add src/AcDream.App/UI/Markup/MarkupHotkeyText.cs src/AcDream.App/UI/RetailUiRuntime.cs tests/AcDream.App.Tests/UI/MarkupHotkeyTextTests.cs docs/plugin-ui-markup.md
git commit -m "fix(ui): hotkey box names modifiers Ctrl, Alt and Shift

Claude-Session: https://claude.ai/code/session_014SAojwkxRKxrwp1RMDGPVC"
```

---

### Task 3: Classic list header on the list's dark background

**Goal:** In Classic, a list header draws `UiMarkupList.BackgroundColor` under its captions instead of the scrollbar-track chain art. The gold underline and cell dividers stay. The themed header and the list's scrollbar art are unchanged.

**Files:**
- Modify: `src/AcDream.App/UI/UiMarkupList.Header.cs:23-33`.
- Modify: `tests/AcDream.App.Tests/UI/MarkupListHeaderTests.cs` (the Classic header test).
- Modify: `docs/plugin-ui-markup.md` ("Header, sorting and column widths").

**Acceptance Criteria:**
- [ ] The Classic header draws a `BackgroundColor` quad at `(0, 0, Width, 18)` and no `RetailScrollbarChrome.Track` sprite quad above y 18.
- [ ] The underline `(0, 17, 300, 1)` and dividers `(x, 2, 1, 14)` stay in `BorderColor`.
- [ ] The scrollbar test (track at `(284, 18, 300, 108)`) and the themed header test pass unchanged.

**Verify:** `dotnet test tests/AcDream.App.Tests/AcDream.App.Tests.csproj -c Release --filter FullyQualifiedName~MarkupListHeader` → `Passed!`.

**Steps:**

- [ ] **Step 1: Failing test.** Replace `Header_ClassicFaceIsTheTrackArtWithAnUnderlineAndSeparators` with:

```csharp
    [Fact]
    public void Header_ClassicFaceIsTheListBackgroundWithAnUnderlineAndSeparators()
    {
        var (_, list) = Mount("", new Binding());
        var renderer = MarkupDrawProbe.Draw(list);
        Assert.Contains((0f, 0f, 300f, 18f), MarkupDrawProbe.QuadBounds(renderer, list.BackgroundColor));
        Assert.DoesNotContain(SpriteQuads(renderer, RetailScrollbarChrome.Track), q => q.Item2 < 18f);
        var lines = MarkupDrawProbe.QuadBounds(renderer, list.BorderColor);
        Assert.Contains((0f, 17f, 300f, 1f), lines);
        foreach (float x in new[] { 119f, 199f, 239f })
            Assert.Contains((x, 2f, 1f, 14f), lines);
    }
```

Run the Verify command. Expected: FAIL (the header still draws the track sprite).

- [ ] **Step 2: Implement.** In `UiMarkupList.Header.cs`, change the summary and the fill:

```csharp
    /// <summary>
    /// The header: Classic draws the list's own dark background under the
    /// captions, the plugin theme its field colour; both draw a
    /// border-coloured underline and a line between cells.
    /// </summary>
    private void DrawHeader(UiRenderContext context, IReadOnlyList<UiMarkupListColumn> columns)
    {
        context.DrawFill(0f, 0f, Width, HeaderHeight, ThemePalette is { } palette ? palette.Field : BackgroundColor);
        context.DrawFill(0f, HeaderHeight - 1f, Width, 1f, BorderColor);
```

Leave the rest of the method as it is. If `RetailScrollbarChrome` or `SpriteResolve` become unused in this file only, that's fine: other partial files use them.

- [ ] **Step 3: Run.** Run the Verify command. Expected: `Passed!`.

- [ ] **Step 4: Docs.** In `docs/plugin-ui-markup.md`, replace `Classic draws the header on the game's scrollbar track art; a themed window uses its field colour.` with:

```markdown
Classic draws the header on the list's own dark background; a themed window
uses its field colour.
```

- [ ] **Step 5: Build and commit.**

```bash
dotnet build AcDream.slnx -c Release 2>&1 | grep -E 'Warning\(s\)|Error\(s\)'
git add src/AcDream.App/UI/UiMarkupList.Header.cs tests/AcDream.App.Tests/UI/MarkupListHeaderTests.cs docs/plugin-ui-markup.md
git commit -m "fix(ui): Classic list header on the list's dark background

Claude-Session: https://claude.ai/code/session_014SAojwkxRKxrwp1RMDGPVC"
```

---

### Task 4: Keyboard for a focused single-select tree

**Goal:** A focused single-select tree takes Up/Down to move the selection to the row above or below; with nothing selected, Up/Down select the first shown row. With nothing selected, Left/Right select the first shown row. Multi-select keys and unfocused lists are unchanged. The ControlsDemo path (click Note, Tab, Right) works end to end.

**Files:**
- Modify: `src/AcDream.App/UI/UiMarkupList.Selection.cs` (single-select Up/Down) and `src/AcDream.App/UI/UiMarkupList.Tree.cs:185-191` (no selection).
- Modify: `tests/AcDream.App.Tests/UI/MarkupListTreeTests.cs`. Replace `SingleMode_UpDownFallThrough_LeftRightDoNot` and add tests.
- Modify: `tests/AcDream.App.Tests/UI/ControlsDemoSampleTests.cs` (one end-to-end test).
- Modify: `docs/plugin-ui-markup.md` ("Trees").

**Acceptance Criteria:**
- [ ] On a focused single-select tree with `Selected = 0`:
  - Down calls `onchange(1)`; Up from 1 calls `onchange(0)`.
  - Neither key reaches `WorldKeyFallThrough`.
  - Up on the first row and Down on the last row are taken without calling `onchange`.
- [ ] With `Selected = -1`:
  - Right calls `onchange(0)`. A second Right, with row 0 an expanded parent, moves to its first child, `onchange(1)`.
  - Left also calls `onchange(0)`.
- [ ] Multi-select tree tests are unchanged and pass.
- [ ] `ControlsDemoSampleTests`: click the Tree window's Note field, press Tab, then Right → `TreeBinding.Selected == 0` and `Status == "Looting selected"`.
- [ ] The Trees docs gain the sentence from Step 5.

**Verify:** `dotnet test tests/AcDream.App.Tests/AcDream.App.Tests.csproj -c Release --filter "FullyQualifiedName~MarkupListTree|FullyQualifiedName~ControlsDemoSample|FullyQualifiedName~MarkupListMulti"` → `Passed!`.

**Steps:**

- [ ] **Step 1: Failing tests.** In `MarkupListTreeTests.cs`, replace `SingleMode_UpDownFallThrough_LeftRightDoNot` with these three tests. The default binding shows A, A1, A2, A2a, B, B1, all expanded, as view rows 0–5.

```csharp
    [Fact]
    public void SingleMode_UpDownMoveTheSelectionAndStayOutOfTheGame()
    {
        var b = new Binding { Selected = 0 };
        var m = Mount(b);
        var fell = new List<int>();
        m.Root.WorldKeyFallThrough += (vk, _) => fell.Add(vk);
        m.Root.SetKeyboardFocus(m.List);

        Press(m, Key.Down);
        Press(m, Key.Up);
        Press(m, Key.Up);       // already on the first row: taken, no change

        Assert.Equal([1, 0], b.Picks);
        Assert.Empty(fell);
    }

    [Fact]
    public void SingleMode_UpDownWithNothingSelectedPickTheFirstRow()
    {
        var b = new Binding();
        var m = Mount(b);
        m.Root.SetKeyboardFocus(m.List);
        Press(m, Key.Down);
        Assert.Equal([0], b.Picks);
    }

    [Fact]
    public void SingleMode_LeftRightWithNothingSelectedPickTheFirstRow()
    {
        var b = new Binding();
        var m = Mount(b);
        m.Root.SetKeyboardFocus(m.List);

        Press(m, Key.Right);    // nothing selected: the first row
        Press(m, Key.Right);    // A is an expanded parent: its first child
        Assert.Equal([0, 1], b.Picks);

        b.Selected = -1;
        Press(m, Key.Left);
        Assert.Equal([0, 1, 0], b.Picks);
    }
```

In `ControlsDemoSampleTests.cs`, add the following. The Tree window's Note field is the first `UiField`-like child; read its position from the panel rather than hard-coding it.

```csharp
    [Fact]
    public void Tree_keys_work_after_tabbing_from_the_note_field()
    {
        var binding = new TreeBinding();
        var (root, panel) = Mount("tree.xml", binding);
        UiElement note = panel.Children.First(c => c.TabStop);   // the Note field comes first in tab order
        Click(root, panel, note.Left + 4f, note.Top + note.Height / 2f);

        root.OnKeyDown((int)Silk.NET.Input.Key.Tab);
        Assert.IsType<UiMarkupList>(root.KeyboardFocus);
        root.OnKeyDown((int)Silk.NET.Input.Key.Right);

        Assert.Equal(0, binding.Selected);
        Assert.Equal("Looting selected", binding.Status);
    }
```

Run the Verify command. Expected:
- the three tree tests FAIL (Up/Down fall through; nothing is picked);
- the ControlsDemo test FAILs on `binding.Selected`.

If the ControlsDemo test fails earlier, on focus (`KeyboardFocus` not a list), stop and report it. That would be a different root cause from the one this plan fixes.

- [ ] **Step 2: Up/Down for a single-select tree.** In `UiMarkupList.Selection.cs` `HandleSelectionEvent`, replace:

```csharp
        if (!MultiSelect)
            return false;
```

with:

```csharp
        if (!MultiSelect)
            return IsTree && HandleSingleStep(key);
```

Add this method to the same partial class:

```csharp
    /// <summary>
    /// Up and Down in a focused single-select tree: the row above or below,
    /// as shown, or the first row when none is selected. Taken even at the
    /// ends, so the game doesn't act on them.
    /// </summary>
    private bool HandleSingleStep(Key key)
    {
        int delta = key switch { Key.Up => -1, Key.Down => 1, _ => 0 };
        if (delta == 0)
            return false;
        RefreshSelectionRows();
        int current = SelectedIndexSource();
        int next = UiMarkupListSelection.Step(current, delta, _view);
        if (next >= 0 && next != current)
            SelectionChanged?.Invoke(next);
        return true;
    }
```

Update the `HandleSelectionEvent` summary: "Up and Down are taken only in multi-select" becomes "Up and Down are taken in multi-select and in a single-select tree".

- [ ] **Step 3: Left/Right with nothing selected.** In `UiMarkupList.Tree.cs` `HandleTreeKey`, replace:

```csharp
        int row = MultiSelect ? _leadRow : SelectedIndexSource();
        int view = _view.ViewRow(row);
        if (view < 0)
            return;
```

with:

```csharp
        int row = MultiSelect ? _leadRow : SelectedIndexSource();
        if (!MultiSelect && row < 0)
        {
            // Nothing selected yet: start at the first shown row.
            if (_view.ViewCount > 0)
                SelectionChanged?.Invoke(_view.PluginRow(0));
            return;
        }
        int view = _view.ViewRow(row);
        if (view < 0)
            return;
```

Also update the method's summary: "with nothing selected in a single-select tree, either key selects the first shown row".

- [ ] **Step 4: Run.** Run the Verify command. Expected: `Passed!`. Also run `--filter FullyQualifiedName~AcDream.App.Tests.UI.Markup` and confirm no other list test broke.

- [ ] **Step 5: Docs.** In `docs/plugin-ui-markup.md`, "Trees", after the sentence ending `and Left collapses it or moves to its parent.`, insert:

```markdown
In a single-select tree, Up and Down move the selection to the row above or
below (the first row when none is selected), and Left or Right with no row
selected selects the first row; none of these keys reach the game while the
tree has focus.
```

- [ ] **Step 6: Build and commit.**

```bash
dotnet build AcDream.slnx -c Release 2>&1 | grep -E 'Warning\(s\)|Error\(s\)'
git add src/AcDream.App/UI/UiMarkupList.Selection.cs src/AcDream.App/UI/UiMarkupList.Tree.cs \
  tests/AcDream.App.Tests/UI/MarkupListTreeTests.cs tests/AcDream.App.Tests/UI/ControlsDemoSampleTests.cs docs/plugin-ui-markup.md
git commit -m "fix(ui): keyboard for a focused single-select tree

Claude-Session: https://claude.ai/code/session_014SAojwkxRKxrwp1RMDGPVC"
```

---

### Task 5: Series gate

**Goal:** A clean Release build, no new test failures by name, and each commit `f399562b..HEAD` builds alone.

**Files:** none changed.

**Acceptance Criteria:**
- [ ] `dotnet build AcDream.slnx -c Release` → 0 warnings, 0 errors.
- [ ] App+Plugin failures ⊆ `mc-base.txt` (162 names). UI.Abstractions failures ⊆ `mc-uiabs-base.txt` (empty).
- [ ] Every commit in `f399562b..HEAD` builds with 0 warnings and passes `FullyQualifiedName~AcDream.App.Tests.UI.Markup|FullyQualifiedName~ControlsDemoSample|FullyQualifiedName~RetainedUiInputBinding`.
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
done | norm > $T/mc15-after.txt
dotnet test tests/AcDream.UI.Abstractions.Tests/AcDream.UI.Abstractions.Tests.csproj -c Release 2>&1 | norm > $T/mc15-uiabs-after.txt
echo "new App/Plugin:"; comm -13 $T/mc-base.txt $T/mc15-after.txt
echo "new UI.Abstractions:"; comm -13 $T/mc-uiabs-base.txt $T/mc15-uiabs-after.txt
git status --short
```

Expected: `0 Warning(s)`, `0 Error(s)`, nothing after either "new" line, and a clean status (revert restore-only lock changes).

- [ ] **Step 2: Each commit builds alone**

```bash
cd /Users/davidsmith/code/OpenAC/OpenAC
for c in $(git rev-list --reverse f399562b..feat/markup-controls); do
  git worktree add -q --detach .worktrees/mc-commit-check $c
  (cd .worktrees/mc-commit-check \
    && dotnet build AcDream.slnx -c Release 2>&1 | grep -E ': error|: warning|Warning\(s\)|Error\(s\)' | sort -u | sed "s/^/$(git rev-parse --short $c) build /" \
    && dotnet test tests/AcDream.App.Tests/AcDream.App.Tests.csproj -c Release --no-build \
         --filter "FullyQualifiedName~AcDream.App.Tests.UI.Markup|FullyQualifiedName~ControlsDemoSample|FullyQualifiedName~RetainedUiInputBinding" 2>&1 \
       | grep -E 'Passed!|Failed!|No test matches' | sed "s/^/$(git rev-parse --short $c) test /")
  git worktree remove --force .worktrees/mc-commit-check
done
```

Expected: every commit `0 Warning(s)`/`0 Error(s)` and `Passed!`.

- [ ] **Step 3: Branch contents**

```bash
cd /Users/davidsmith/code/OpenAC/OpenAC/.worktrees/markup-controls
git log --oneline f399562b..HEAD
git diff --stat 391e709a..HEAD | grep superpowers || echo "no superpowers docs"
```

No commit in this task.

---

### Task 6: Live re-check of the four fixes

**Goal:** The user confirms, in the real client on the local ACE server at 2x, that each of the four plan 14 defects is gone:
- Shift+mouse wheel steps a focused number field 10×;
- the hotkey box shows "Shift+…";
- the Classic table header captions read clearly;
- click Note, Tab, Right/Left/Up/Down drive the tree.

> **USER-ORDERED GATE — NON-SKIPPABLE.** This task was requested by the user in the current conversation. It MUST NOT be closed by walking around it, by declaring it "verified inline", or by substituting a cheaper check. Close only after every item in `acceptanceCriteria` has been re-validated independently, with output captured.

**Files:** none changed.

**Acceptance Criteria:**
- [ ] The user said they are available before the client was launched.
- [ ] The client log shows `sample.controls-demo` loaded, with no exception lines during the run.
- [ ] The user confirms each item:
  - Inputs: Shift+mouse wheel on a focused "Heal at %" steps by 50 (10 × 5); without Shift, by 5.
  - Keys and colours: recording Shift+key with either Shift shows "Shift+…", with no "LSHIFT".
  - Table: in Classic, the header captions are clearly readable on a dark band with a gold line under it.
  - Tree: after clicking Note and pressing Tab, Right selects "Looting". Up/Down move the selection and the character doesn't move. Left/Right expand, collapse and move.

**Verify:** `grep -E 'sample.controls-demo|Exception' <root>/client-run.log`, plus the user's per-item confirmation in the conversation.

**Steps:**

- [ ] **Step 1:** Ask the user whether they are available. Launch nothing before they say yes.
- [ ] **Step 2: Build and install** into the plan 14 scratch root. The sample didn't change, but reinstall it anyway.

```bash
export DOTNET_ROOT=$HOME/.dotnet PATH=$HOME/.dotnet:$PATH
R=/private/tmp/claude-501/-Users-davidsmith-code-OpenAC-OpenAC/mc14-live-root
cd /Users/davidsmith/code/OpenAC/OpenAC/.worktrees/markup-controls
dotnet build src/AcDream.App -c Release --nologo --artifacts-path $R/build/openac 2>&1 | grep -E 'Warning\(s\)|Error\(s\)'
dotnet build samples/AcDream.Plugins.ControlsDemo -c Release --nologo 2>&1 | grep -E 'Warning\(s\)|Error\(s\)'
cp samples/AcDream.Plugins.ControlsDemo/bin/Release/net10.0/{AcDream.Plugins.ControlsDemo.dll,AcDream.Plugins.ControlsDemo.deps.json,plugin.json,*.xml} $R/plugins/sample.controls-demo/
```

- [ ] **Step 3: Launch** in the background, with output to `$R/client-run.log`:

```bash
OPENAC_ROOT=/Users/davidsmith/code/OpenAC/OpenAC/.worktrees/markup-controls OPENAC_DEV_ROOT=$R \
  ~/code/OpenAC/OpenAC-Plugins-Aethrix/tools/run-dev-client.sh --no-build --no-aethrix --no-rynth > $R/client-run.log 2>&1
```

- [ ] **Step 4:** Walk the four items with the user (Classic for the header). Record pass or fail per item in this plan's Status line. A failure becomes a new fix task; nothing is fixed inside this task.
