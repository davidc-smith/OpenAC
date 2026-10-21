# Markup controls, plan 8: `<list selectmode="multi">` Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers-extended-cc:subagent-driven-development (recommended) or superpowers-extended-cc:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** A `<list>` can let the player select any number of rows: click, Ctrl/Cmd-click, Shift-click ranges in view order, Ctrl+A, and Up/Down with Shift to extend. The plugin owns the selection.

**Architecture:**
- **Pure rules.** `UiMarkupListSelection` is a set of static functions over plugin rows and the plan 7 view map: `Click`, `MovesAnchor`, `Range`, `All`, `Step`. Ranges run in view order. Every result is plugin rows, ascending, without duplicates.
- **Widget.** A new partial, `UiMarkupList.Selection.cs`, adds the multi-mode properties, the anchor and lead rows, the selected-row mask for drawing, held-modifier reading and the selection keys. Both list forms route a row pick through one `PickRow`.
  - The single-column form uses an identity view map (`_view.SetIdentity`) in multi mode, so the same rules apply.
- **Builder.** `Markup/MarkupListSelect.cs` binds `selectmode`, `selected`, `onchange` and `selectionband` for both modes. Single mode keeps today's bindings and error messages exactly.
- **No host selection.** The list keeps only the anchor and the lead, never a selection. It draws whatever `selected` returns and calls `onchange` on each user action.

**Tech Stack:** C# / .NET 10, Silk.NET input keys, xUnit, headless `UiRoot` + `RecordingGpuDevice` tests.

**Spec:** `docs/superpowers/specs/2026-10-21-markup-controls-design.md` (branch `docs/markup-controls-spec`), "Shared rules" and section 9. It builds on plan 7 (`2026-10-21-markup-controls-7-list-header.md`): the view map, `PluginRow`/`ViewRow`, and the reveal and press-cancel rules.

## Global Constraints

- **Workspace.** Worktree `/Users/davidsmith/code/OpenAC/OpenAC/.worktrees/markup-controls`, branch `feat/markup-controls` (HEAD fd03cfb0, from upstream 391e709a).
  - Never merge fork `main` in, and never add `docs/superpowers/` files there.
  - Push only when the controller says so.
- **Build hygiene.** Every shell runs `export DOTNET_ROOT=$HOME/.dotnet PATH=$HOME/.dotnet:$PATH` first. After building, run `git checkout -- '*.lock.json'`; never commit lock files.
- **Commits.** Every commit must build and pass its own tests. Commit messages end with `Claude-Session: https://claude.ai/code/session_014SAojwkxRKxrwp1RMDGPVC`.
- **Helper conventions.**
  - `BindRequired*` helpers throw when the attribute is absent or mistyped.
  - Other `Bind*` helpers return null when absent and throw when mistyped.
  - `context` is `"<element> <attribute>"`.
  - The list case still adds exactly one element.
- **Plugin rows.** Every callback, every bound `selected`, and every row index a plugin sees is a plugin row. Selection follows the row, not the position.
- **`<list>` selection contract.**
  - `selectmode="single"` (default) behaves exactly as today: `selected` is `int`, `onchange` is `Action<int>`, there is no band unless `selectionband="true"`, and the list is not a tab stop.
  - `selectmode="multi"`: `selected` is any `IEnumerable<int>`, `onchange` is `Action<IReadOnlyList<int>>` (optional; without it the list only displays), and the band is implied. `selectionband="false"` throws.
  - Any other `selectmode` throws.
  - Multi mode works in both forms (`items` and `<column>`).
- **Click rules** (multi mode). A click picks a row through the same cells that select in single mode: any row in the single-column form; in the column form, a text column without `onclick`. Check, icon and button cells keep their own actions.
  - Plain click: only that row.
  - Ctrl or Cmd (Super) click: toggles that row.
  - Shift-click: the view range from the anchor to this row.
  - Ctrl+Shift-click: adds that range to the current selection.
  - Shift with no valid anchor acts as the click without Shift.
  - The anchor moves on every click except a Shift-click that used it. The lead moves on every click.
- **Keys** (multi mode, list focused and enabled).
  - Up/Down: a single selection one view row from the lead, clamped at the ends. With no valid lead, the first view row.
  - Shift+Up/Down: the range from the anchor to the new lead.
  - Ctrl/Cmd+A: every row.
  - Up and Down are always handled while the list has focus. Other keys fall through.
  - `onchange` fires on every handled key, even when the selection doesn't change.
- **Focus.** A multi list is `AcceptsFocus`, `TabStop` and `FocusOnMouseClick = false`, so clicking never takes keyboard focus from the game. It draws the gold focus outline (`UiMarkupExpanderHeader.FocusColor`) while focused.
- **Reveal.** In multi mode the list reveals the lead row when the lead or the sort changes, never on data reordering. Single mode is unchanged.

**User decisions (already made):**
- One branch, one commit series per control, upstream-portable.
- The table is an extension of `<list>` (header, sort, multi-select, resizable columns), not a new element.
- Controls other than text editors don't take keyboard focus on click (spec "Keyboard" shared rule).
- The host never holds a selection across frames; the plugin owns it (spec section 9).

---

## File Structure

| File | Responsibility |
|---|---|
| `src/AcDream.App/UI/Markup/MarkupBindings.cs` | `BindRequiredIntListReader`, `BindIntListAction` |
| `src/AcDream.App/UI/UiMarkupListSelection.cs` (new) | Pure multi-select rules over plugin rows and the view map |
| `src/AcDream.App/UI/UiMarkupList.Selection.cs` (new) | Multi-mode properties, anchor/lead, mask, modifiers, `PickRow`, keys, focus |
| `src/AcDream.App/UI/UiMarkupList.cs` | Both forms pick through `PickRow`; band per selected row; lead reveal; focus outline |
| `src/AcDream.App/UI/Markup/MarkupListSelect.cs` (new) | Builder: `selectmode`, `selected`, `onchange`, `selectionband` |
| `src/AcDream.App/UI/MarkupDocument.cs` | The list case calls `MarkupListSelect.Apply`; `BindIntAction` becomes `internal` |
| `tests/AcDream.App.Tests/Plugins/FakeKeyboard.cs` | `Held` keys for `IsKeyPressed` |
| `tests/AcDream.App.Tests/UI/MarkupListSelectBindingsTests.cs`, `UiMarkupListSelectionTests.cs`, `MarkupListMultiSelectTests.cs` (new) | Tests |
| `docs/plugin-ui-markup.md` | Multi-select docs |

---

### Task 1: Binding helpers for an int-list selection

**Goal:** Builders can bind a required `IEnumerable<int>` reader and an optional `Action<IReadOnlyList<int>>`.

**Files:**
- Modify: `src/AcDream.App/UI/Markup/MarkupBindings.cs`
- Test: `tests/AcDream.App.Tests/UI/MarkupListSelectBindingsTests.cs` (new)

**Acceptance Criteria:**
- [ ] `BindRequiredIntListReader` reads an `IEnumerable<int>` property on every call, as an `IReadOnlyList<int>`. It returns the plugin's list itself when that is already an `IReadOnlyList<int>`, and a copy otherwise. A null property value reads as empty.
- [ ] `BindRequiredIntListReader` throws `FormatException` for an absent attribute, a literal, a missing property, or a wrong type (`int`, `IReadOnlyList<float>`). The message names the context.
- [ ] `BindIntListAction` returns null when absent and invokes a bound `Action<IReadOnlyList<int>>`. It throws naming the context and `Action<IReadOnlyList<int>>` for a literal, a missing property or an `Action<int>`.

**Verify:** `dotnet test tests/AcDream.App.Tests/AcDream.App.Tests.csproj --filter "FullyQualifiedName~MarkupListSelectBindingsTests"` → all pass.

**Steps:**

- [ ] **Step 1: Write the failing tests** — create `tests/AcDream.App.Tests/UI/MarkupListSelectBindingsTests.cs`:

```csharp
using System;
using System.Collections.Generic;
using AcDream.App.UI.Markup;

namespace AcDream.App.Tests.UI;

public sealed class MarkupListSelectBindingsTests
{
    private sealed class Binding
    {
        public List<int> Rows { get; set; } = new() { 2, 0 };
        public HashSet<int> RowSet { get; } = new() { 5 };
        public List<int>? NullRows { get; set; }
        public int One { get; } = 1;
        public IReadOnlyList<float> Floats { get; } = new[] { 1f };
        public IReadOnlyList<int>? LastRows { get; private set; }
        public Action<IReadOnlyList<int>> SetRows => r => LastRows = r;
        public Action<int> WrongAction => _ => { };
    }

    [Fact]
    public void IntListReader_ReadsLiveListWithoutCopying()
    {
        var b = new Binding();
        var read = MarkupBindings.BindRequiredIntListReader("{Rows}", b, "list selected");
        Assert.Same(b.Rows, read());
        b.Rows = new List<int> { 3 };
        Assert.Equal(new[] { 3 }, read());
    }

    [Fact]
    public void IntListReader_CopiesOtherEnumerablesAndReadsNullAsEmpty()
    {
        var b = new Binding();
        Assert.Equal(new[] { 5 }, MarkupBindings.BindRequiredIntListReader("{RowSet}", b, "list selected")());
        Assert.Empty(MarkupBindings.BindRequiredIntListReader("{NullRows}", b, "list selected")());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("3")]
    [InlineData("{Missing}")]
    [InlineData("{One}")]
    [InlineData("{Floats}")]
    public void IntListReader_ThrowsNamingContext(string? expression)
    {
        var ex = Assert.Throws<FormatException>(() =>
            MarkupBindings.BindRequiredIntListReader(expression, new Binding(), "list selected"));
        Assert.Contains("list selected", ex.Message);
    }

    [Fact]
    public void IntListAction_MissingIsNullAndBoundInvokes()
    {
        var b = new Binding();
        Assert.Null(MarkupBindings.BindIntListAction(null, b, "list onchange"));
        MarkupBindings.BindIntListAction("{SetRows}", b, "list onchange")!(new[] { 4, 1 });
        Assert.Equal(new[] { 4, 1 }, b.LastRows);
    }

    [Theory]
    [InlineData("SetRows")]
    [InlineData("{Missing}")]
    [InlineData("{WrongAction}")]
    public void IntListAction_ThrowsNamingContextAndType(string expression)
    {
        var ex = Assert.Throws<FormatException>(() =>
            MarkupBindings.BindIntListAction(expression, new Binding(), "list onchange"));
        Assert.Contains("list onchange", ex.Message);
        Assert.Contains("Action<IReadOnlyList<int>>", ex.Message);
    }
}
```

- [ ] **Step 2: Run, expect a compile failure** (`BindRequiredIntListReader` not defined).

- [ ] **Step 3: Implement** — in `MarkupBindings.cs`, after `BindFloatListAction`, add:

```csharp
    /// <summary>
    /// A required list of ints (any <c>IEnumerable&lt;int&gt;</c>), read on each
    /// call: the plugin's list itself when it is an <c>IReadOnlyList&lt;int&gt;</c>
    /// (read every frame, so not copied), otherwise a copy. Null reads as empty.
    /// </summary>
    public static Func<IReadOnlyList<int>> BindRequiredIntListReader(
        string? expression, object binding, string context)
    {
        if (string.IsNullOrWhiteSpace(expression) || !MarkupDocument.IsBinding(expression))
            throw new FormatException($"{context} must be an int-list binding");
        PropertyInfo? property = binding.GetType().GetProperty(expression[1..^1]);
        if (property is null || !typeof(IEnumerable<int>).IsAssignableFrom(property.PropertyType))
        {
            throw new FormatException(
                $"{expression} did not resolve to an IEnumerable<int> property on "
                + binding.GetType().Name + $" ({context})");
        }
        return () => property.GetValue(binding) switch
        {
            IReadOnlyList<int> list => list,
            IEnumerable<int> values => values.ToArray(),
            _ => Array.Empty<int>(),
        };
    }

    /// <summary>An optional <c>Action&lt;IReadOnlyList&lt;int&gt;&gt;</c>; null when the attribute is absent.</summary>
    public static Action<IReadOnlyList<int>>? BindIntListAction(string? attribute, object binding, string context) =>
        BindOptionalAction<IReadOnlyList<int>>(attribute, binding, context, "IReadOnlyList<int>");
```

- [ ] **Step 4: Run the Verify command** → all pass.

- [ ] **Step 5: Commit**

```bash
git add src/AcDream.App/UI/Markup/MarkupBindings.cs tests/AcDream.App.Tests/UI/MarkupListSelectBindingsTests.cs
git commit -m "feat(ui): int-list binding helpers for list multi-select

Claude-Session: https://claude.ai/code/session_014SAojwkxRKxrwp1RMDGPVC"
```

---

### Task 2: Pure multi-select rules

**Goal:** `UiMarkupListSelection` computes the selection after a click, a range, select-all and a keyboard step, over plugin rows and a view map.

**Files:**
- Create: `src/AcDream.App/UI/UiMarkupListSelection.cs`
- Test: `tests/AcDream.App.Tests/UI/UiMarkupListSelectionTests.cs` (new)

**Acceptance Criteria:**
- [ ] `Click` handles a plain click (that row only), `add` (toggle), `range` with a valid anchor (the view range, both directions), `add` + `range` (union with current), and `range` without a valid anchor (falls back to the click without Shift). Every result is ascending plugin rows, deduplicated, with rows outside `0..view.Count-1` dropped (including stale rows in `current`).
- [ ] `MovesAnchor` is false only for a range click with a valid anchor.
- [ ] `Range` under a sorted map returns the plugin rows of the view span, not the plugin-row span. With an invalid anchor it returns just the lead; with an invalid lead it returns empty.
- [ ] `All(n)` is `0..n-1`; `All(0)` is empty.
- [ ] `Step` moves one view row and clamps at both ends. With an invalid lead it goes to the first view row. With no rows it returns −1.

**Verify:** `dotnet test tests/AcDream.App.Tests/AcDream.App.Tests.csproj --filter "FullyQualifiedName~UiMarkupListSelectionTests"` → all pass.

**Steps:**

- [ ] **Step 1: Write the failing tests** — create `tests/AcDream.App.Tests/UI/UiMarkupListSelectionTests.cs`:

```csharp
using System;
using AcDream.App.UI;

namespace AcDream.App.Tests.UI;

public sealed class UiMarkupListSelectionTests
{
    private static UiMarkupListViewMap Identity(int count)
    {
        var map = new UiMarkupListViewMap();
        map.SetIdentity(count);
        return map;
    }

    // Keys 30,10,20,40 ascending: view order is plugin rows 1, 2, 0, 3.
    private static UiMarkupListViewMap Sorted()
    {
        var map = new UiMarkupListViewMap();
        map.SortByNumber(new[] { 30.0, 10.0, 20.0, 40.0 }, 4, descending: false);
        return map;
    }

    [Fact]
    public void Click_Plain_SelectsOnlyThatRow() =>
        Assert.Equal(new[] { 2 }, UiMarkupListSelection.Click(new[] { 0, 3 }, 2, false, false, -1, Identity(4)));

    [Fact]
    public void Click_Add_TogglesTheRow()
    {
        var map = Identity(4);
        Assert.Equal(new[] { 0, 2, 3 }, UiMarkupListSelection.Click(new[] { 3, 0 }, 2, true, false, -1, map));
        Assert.Equal(new[] { 0 }, UiMarkupListSelection.Click(new[] { 3, 0 }, 3, true, false, -1, map));
    }

    [Fact]
    public void Click_Range_RunsInViewOrderBothWays()
    {
        var map = Sorted();
        // Anchor plugin 1 (view 0) to plugin 0 (view 2): views 0..2 = plugins 1, 2, 0.
        Assert.Equal(new[] { 0, 1, 2 }, UiMarkupListSelection.Click(new[] { 3 }, 0, false, true, 1, map));
        // Anchor plugin 3 (view 3) up to plugin 0 (view 2): plugins 0, 3, not 0..3.
        Assert.Equal(new[] { 0, 3 }, UiMarkupListSelection.Click(Array.Empty<int>(), 0, false, true, 3, map));
    }

    [Fact]
    public void Click_AddRange_UnionsWithCurrent() =>
        Assert.Equal(new[] { 0, 2, 3 },
            UiMarkupListSelection.Click(new[] { 3 }, 0, true, true, 2, Sorted()));

    [Theory]
    [InlineData(-1)]
    [InlineData(9)]
    public void Click_RangeWithoutAnchor_ActsAsClickWithoutShift(int anchor)
    {
        var map = Identity(4);
        Assert.Equal(new[] { 1 }, UiMarkupListSelection.Click(new[] { 3 }, 1, false, true, anchor, map));
        Assert.Equal(new[] { 1, 3 }, UiMarkupListSelection.Click(new[] { 3 }, 1, true, true, anchor, map));
    }

    [Fact]
    public void Click_DropsStaleAndDuplicateRows() =>
        Assert.Equal(new[] { 1, 2 },
            UiMarkupListSelection.Click(new[] { 2, 2, 7, -1 }, 1, true, false, -1, Identity(4)));

    [Fact]
    public void MovesAnchor_OnlyARangeClickWithAValidAnchorKeepsIt()
    {
        Assert.False(UiMarkupListSelection.MovesAnchor(range: true, anchor: 1, count: 4));
        Assert.True(UiMarkupListSelection.MovesAnchor(range: true, anchor: -1, count: 4));
        Assert.True(UiMarkupListSelection.MovesAnchor(range: true, anchor: 4, count: 4));
        Assert.True(UiMarkupListSelection.MovesAnchor(range: false, anchor: 1, count: 4));
    }

    [Fact]
    public void Range_InvalidEnds()
    {
        var map = Sorted();
        Assert.Equal(new[] { 2 }, UiMarkupListSelection.Range(-1, 2, map));
        Assert.Empty(UiMarkupListSelection.Range(1, 9, map));
    }

    [Fact]
    public void All_IsEveryRow()
    {
        Assert.Equal(new[] { 0, 1, 2 }, UiMarkupListSelection.All(3));
        Assert.Empty(UiMarkupListSelection.All(0));
    }

    [Fact]
    public void Step_MovesInViewOrderAndClamps()
    {
        var map = Sorted();                                      // views: 1, 2, 0, 3
        Assert.Equal(0, UiMarkupListSelection.Step(2, 1, map));   // view 1 → view 2
        Assert.Equal(1, UiMarkupListSelection.Step(1, -1, map));  // top stays
        Assert.Equal(3, UiMarkupListSelection.Step(3, 1, map));   // bottom stays
        Assert.Equal(1, UiMarkupListSelection.Step(-1, 1, map));  // no lead → first view row
        Assert.Equal(1, UiMarkupListSelection.Step(8, -1, map));
        Assert.Equal(-1, UiMarkupListSelection.Step(0, 1, Identity(0)));
    }
}
```

- [ ] **Step 2: Run, expect a compile failure** (`UiMarkupListSelection` not defined).

- [ ] **Step 3: Implement** — create `src/AcDream.App/UI/UiMarkupListSelection.cs`:

```csharp
namespace AcDream.App.UI;

/// <summary>
/// The multi-select rules of a markup list, as pure functions over plugin
/// rows. Ranges run in view order (what the player sees). Every result is
/// plugin rows in ascending order, without duplicates or rows outside
/// 0..view.Count-1.
/// </summary>
internal static class UiMarkupListSelection
{
    /// <summary>
    /// The selection after a click on <paramref name="row"/>: that row alone;
    /// toggled in or out with <paramref name="add"/>; the view range from
    /// <paramref name="anchor"/> with <paramref name="range"/>, added to the
    /// current selection when <paramref name="add"/> is held too. A range with
    /// no valid anchor acts as the same click without Shift.
    /// </summary>
    public static int[] Click(
        IReadOnlyList<int> current, int row, bool add, bool range, int anchor, UiMarkupListViewMap view)
    {
        int count = view.Count;
        if (range && IsRow(anchor, count))
        {
            int[] span = Range(anchor, row, view);
            return add ? Normalize(current.Concat(span), count) : span;
        }
        if (add)
        {
            return Normalize(
                current.Contains(row) ? current.Where(r => r != row) : current.Append(row), count);
        }
        return Normalize([row], count);
    }

    /// <summary>Whether a click moves the anchor to the clicked row: every click but a range that used it.</summary>
    public static bool MovesAnchor(bool range, int anchor, int count) => !(range && IsRow(anchor, count));

    /// <summary>The plugin rows shown from <paramref name="anchor"/>'s view row to <paramref name="lead"/>'s.</summary>
    public static int[] Range(int anchor, int lead, UiMarkupListViewMap view)
    {
        int from = view.ViewRow(anchor), to = view.ViewRow(lead);
        if (to < 0)
            return [];
        if (from < 0)
            return [lead];
        (int low, int high) = from <= to ? (from, to) : (to, from);
        var rows = new int[high - low + 1];
        for (int v = low; v <= high; v++)
            rows[v - low] = view.PluginRow(v);
        Array.Sort(rows);
        return rows;
    }

    /// <summary>Every row.</summary>
    public static int[] All(int count) => Enumerable.Range(0, Math.Max(0, count)).ToArray();

    /// <summary>
    /// The plugin row one view row from <paramref name="lead"/>, clamped to the
    /// rows; the first view row when the lead is not a row; −1 with no rows.
    /// </summary>
    public static int Step(int lead, int delta, UiMarkupListViewMap view)
    {
        if (view.Count == 0)
            return -1;
        int at = view.ViewRow(lead);
        return view.PluginRow(at < 0 ? 0 : Math.Clamp(at + delta, 0, view.Count - 1));
    }

    private static bool IsRow(int row, int count) => (uint)row < (uint)count;

    private static int[] Normalize(IEnumerable<int> rows, int count) =>
        rows.Where(r => IsRow(r, count)).Distinct().Order().ToArray();
}
```

- [ ] **Step 4: Run the Verify command** → all pass.

- [ ] **Step 5: Commit**

```bash
git add src/AcDream.App/UI/UiMarkupListSelection.cs tests/AcDream.App.Tests/UI/UiMarkupListSelectionTests.cs
git commit -m "feat(ui): multi-select rules for markup lists

Claude-Session: https://claude.ai/code/session_014SAojwkxRKxrwp1RMDGPVC"
```

---

### Task 3: `selectmode="multi"` builder, pointer picks and band

**Goal:** `<list selectmode="multi">` builds, binds its `selected` list and `onchange`, picks rows by mouse under the click rules in both list forms, and draws the band on every selected row.

**Files:**
- Create: `src/AcDream.App/UI/UiMarkupList.Selection.cs`
- Create: `src/AcDream.App/UI/Markup/MarkupListSelect.cs`
- Modify: `src/AcDream.App/UI/UiMarkupList.cs` (single-column draw/event, `DrawColumns` band, `OnEventColumns` text pick)
- Modify: `src/AcDream.App/UI/MarkupDocument.cs` (list case; `BindIntAction` → `internal`)
- Modify: `tests/AcDream.App.Tests/Plugins/FakeKeyboard.cs` (`Held`)
- Test: `tests/AcDream.App.Tests/UI/MarkupListMultiSelectTests.cs` (new)

**Acceptance Criteria:**
- [ ] Single mode is unchanged. All existing `MarkupListColumnsTests`, `MarkupListHeaderTests` and `ListButtonColumn` tests pass untouched. The single-mode `onchange` and `selected` error messages are the same text as before.
- [ ] Build errors (`FormatException`):
  - `selectmode="many"`, naming `selectmode`;
  - multi with `selected` absent or bound to an `int`, naming `list selected`;
  - multi with `onchange` bound to an `Action<int>`, naming `list onchange`;
  - multi with `selectionband="false"`.
- [ ] With the Name column sorted ascending (view order plugin 3, 1, 0, 2), every click rule in Global Constraints holds. This includes the range in view order, Ctrl+Shift union, Shift without an anchor, and the anchor following its row across a re-sort.
- [ ] The single-column form applies the same rules with view = plugin order.
- [ ] A disabled list fires nothing. A list without `onchange` clicks without throwing. Check, button and text-`onclick` cells don't select.
- [ ] One band quad of `SelectedColor` per selected visible row, at that row's view position. Out-of-range and duplicate entries draw nothing extra. A `theme="plugin"` multi list draws without throwing.
- [ ] UiRoot-mounted, with the default modifier source: holding `ControlLeft` or `SuperLeft` on `root.Keyboard` toggles, and holding `ShiftLeft` ranges. `root.KeyboardFocus` stays null after the press and release.

**Verify:** `dotnet test tests/AcDream.App.Tests/AcDream.App.Tests.csproj --filter "FullyQualifiedName~MarkupListMultiSelectTests|FullyQualifiedName~MarkupListColumnsTests|FullyQualifiedName~MarkupListHeaderTests|FullyQualifiedName~ListButtonColumn|FullyQualifiedName~MarkupDocument"` → all pass.

**Steps:**

- [ ] **Step 1: Give the fake keyboard held keys** — in `tests/AcDream.App.Tests/Plugins/FakeKeyboard.cs`, replace

```csharp
    public bool IsKeyPressed(Silk.NET.Input.Key key) => false;
```

with

```csharp
    /// <summary>Keys reported as held; empty by default.</summary>
    public HashSet<Silk.NET.Input.Key> Held { get; } = new();

    public bool IsKeyPressed(Silk.NET.Input.Key key) => Held.Contains(key);
```

- [ ] **Step 2: Write the failing tests** — create `tests/AcDream.App.Tests/UI/MarkupListMultiSelectTests.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using AcDream.App.Tests.Plugins;
using AcDream.App.UI;
using AcDream.UI.Abstractions.Panels.Settings;
using Silk.NET.Input;

namespace AcDream.App.Tests.UI;

public sealed class MarkupListMultiSelectTests
{
    private sealed class Binding
    {
        public List<string> Names { get; set; } = new() { "mosswart", "Drudge", "rat", "Banderling" };
        public List<bool> On { get; set; } = new() { false, false, false, false };
        public List<string> Acts { get; set; } = new() { "Go", "Go", "Go", "Go" };
        public List<int> Selection { get; set; } = new();
        public List<IReadOnlyList<int>> Changes { get; } = new();
        public Action<IReadOnlyList<int>> SetSelection => rows => { Selection = rows.ToList(); Changes.Add(rows); };
        public Action<int> Pick => _ => { };
        public Action<int> Toggle => _ => { };
        public Action<int> Act => _ => { };
        public int Single { get; set; } = -1;
        public int SortCol { get; set; } = 0;
        public bool SortDesc { get; set; }
    }

    // Name 0-120 (sorted by sortcolumn), checkbox 120-160, button 160-300.
    // Header y 0-18; view row v spans y 18+18v .. 36+18v.
    // Ascending view order: Banderling(3), Drudge(1), mosswart(0), rat(2).
    private const string ColumnsXml = """
          <column type="text" header="Name" width="120" items="{Names}" sort="text"/>
          <column type="checkbox" header="On" width="40" values="{On}" onchange="{Toggle}"/>
          <column type="button" items="{Acts}" onclick="{Act}"/>
        """;

    private const int NameX = 60, CheckX = 140, ButtonX = 230;

    private static int RowY(int view) => 27 + 18 * view;

    private static UiPanel BuildPanel(string listXml, Binding binding, PluginUiThemeSettings? themes = null)
    {
        string theme = themes is null ? "" : "theme=\"plugin\"";
        return MarkupDocument.Build(
            $"""
            <panel x="0" y="0" w="400" h="300" {theme}>
            {listXml}
            </panel>
            """,
            binding, id => (id, 16, 16), themes: themes);
    }

    private static string ColumnList(string attributes = "selectmode=\"multi\" selected=\"{Selection}\" onchange=\"{SetSelection}\"") => $"""
          <list name="mobs" x="0" y="0" w="300" h="108" header="true" sortcolumn="{"{SortCol}"}" sortdescending="{"{SortDesc}"}" {attributes}>
        {ColumnsXml}
          </list>
        """;

    private sealed class Mounted
    {
        public required UiRoot Root { get; init; }
        public required UiMarkupList List { get; init; }
        public UiSelectModifiers Keys { get; set; }
    }

    private static Mounted Mount(Binding binding, string? listXml = null, PluginUiThemeSettings? themes = null,
        bool fakeModifiers = true)
    {
        var panel = BuildPanel(listXml ?? ColumnList(), binding, themes);
        var root = new UiRoot { Width = 800, Height = 600 };
        root.AddChild(panel);
        var list = panel.Children.OfType<UiMarkupList>().Single();
        var mounted = new Mounted { Root = root, List = list };
        if (fakeModifiers)
            list.ModifiersSource = () => mounted.Keys;
        Redraw(list);                                   // lists read rows and lay out at draw
        return mounted;
    }

    private static void Redraw(UiMarkupList list) => MarkupDrawProbe.Draw(list.Parent!, 800f, 600f);

    private static void Click(Mounted m, int x, int y, UiSelectModifiers keys = default)
    {
        m.Keys = keys;
        m.Root.OnMouseDown(UiMouseButton.Left, x, y);
        m.Root.OnMouseUp(UiMouseButton.Left, x, y);
        Redraw(m.List);
    }

    private static readonly UiSelectModifiers Add = new(Add: true, Range: false);
    private static readonly UiSelectModifiers Range = new(Add: false, Range: true);
    private static readonly UiSelectModifiers AddRange = new(Add: true, Range: true);

    [Fact]
    public void Click_SelectsOnlyThatRow()
    {
        var b = new Binding { Selection = new() { 0, 2 } };
        var m = Mount(b);
        Click(m, NameX, RowY(1));
        Assert.Equal(new[] { 1 }, Assert.Single(b.Changes));
    }

    [Fact]
    public void CtrlClick_TogglesTheRow()
    {
        var b = new Binding { Selection = new() { 1 } };
        var m = Mount(b);
        Click(m, NameX, RowY(0), Add);
        Assert.Equal(new[] { 1, 3 }, b.Selection);
        Click(m, NameX, RowY(0), Add);
        Assert.Equal(new[] { 1 }, b.Selection);
    }

    [Fact]
    public void ShiftClick_SelectsTheRangeInViewOrder()
    {
        var b = new Binding();
        var m = Mount(b);
        Click(m, NameX, RowY(0));          // plugin 3, the anchor
        Click(m, NameX, RowY(2), Range);   // views 0..2 = plugins 3, 1, 0
        Assert.Equal(new[] { 0, 1, 3 }, b.Selection);
        Click(m, NameX, RowY(1), Range);   // same anchor: views 0..1
        Assert.Equal(new[] { 1, 3 }, b.Selection);
    }

    [Fact]
    public void CtrlShiftClick_AddsTheRange()
    {
        var b = new Binding();
        var m = Mount(b);
        Click(m, NameX, RowY(3));             // [2]
        Click(m, NameX, RowY(0), Add);        // [2, 3], anchor plugin 3
        Click(m, NameX, RowY(1), AddRange);   // + views 0..1 = plugins 3, 1
        Assert.Equal(new[] { 1, 2, 3 }, b.Selection);
    }

    [Fact]
    public void ShiftClick_WithoutAnAnchor_SelectsOnlyThatRow()
    {
        var b = new Binding();
        var m = Mount(b);
        Click(m, NameX, RowY(1), Range);
        Assert.Equal(new[] { 1 }, b.Selection);
    }

    [Fact]
    public void Anchor_FollowsItsRowAcrossAResort()
    {
        var b = new Binding();
        var m = Mount(b);
        Click(m, NameX, RowY(0));          // anchor plugin 3 (Banderling)
        b.SortDesc = true;                 // views: rat(2), mosswart(0), Drudge(1), Banderling(3)
        Redraw(m.List);
        Click(m, NameX, RowY(1), Range);   // views 1..3 = plugins 0, 1, 3
        Assert.Equal(new[] { 0, 1, 3 }, b.Selection);
    }

    [Fact]
    public void CheckButtonAndTextOnclickCells_DoNotSelect()
    {
        var b = new Binding();
        var m = Mount(b);
        Click(m, CheckX, RowY(0));
        Click(m, ButtonX, RowY(0));
        Assert.Empty(b.Changes);

        var b2 = new Binding();
        var m2 = Mount(b2, """
              <list x="0" y="0" w="300" h="108" selectmode="multi" selected="{Selection}" onchange="{SetSelection}">
                <column type="text" items="{Names}" onclick="{Pick}"/>
              </list>
            """);
        Click(m2, NameX, 9);
        Assert.Empty(b2.Changes);
    }

    [Fact]
    public void SingleColumnForm_UsesPluginOrder()
    {
        var b = new Binding();
        var m = Mount(b, """
              <list x="0" y="0" w="300" h="108" items="{Names}" selectmode="multi" selected="{Selection}" onchange="{SetSelection}"/>
            """);
        Click(m, 60, 9);                  // row 0
        Click(m, 60, 9 + 36, Range);      // rows 0..2
        Assert.Equal(new[] { 0, 1, 2 }, b.Selection);
        Click(m, 60, 9 + 18, Add);
        Assert.Equal(new[] { 0, 2 }, b.Selection);
    }

    [Fact]
    public void Disabled_FiresNothing()
    {
        var b = new Binding();
        var m = Mount(b, ColumnList("selectmode=\"multi\" selected=\"{Selection}\" onchange=\"{SetSelection}\" enabled=\"false\""));
        Click(m, NameX, RowY(0));
        Assert.Empty(b.Changes);
    }

    [Fact]
    public void WithoutOnchange_ClickIsDisplayOnly()
    {
        var b = new Binding { Selection = new() { 2 } };
        var m = Mount(b, ColumnList("selectmode=\"multi\" selected=\"{Selection}\""));
        Click(m, NameX, RowY(0));
        Assert.Equal(new[] { 2 }, b.Selection);
    }

    [Fact]
    public void Band_DrawsOnEverySelectedRow()
    {
        var b = new Binding { Selection = new() { 0, 3 } };
        var m = Mount(b);
        var bands = MarkupDrawProbe.QuadBounds(MarkupDrawProbe.Draw(m.List), m.List.SelectedColor)
            .Select(q => q.Y).Order().ToArray();
        Assert.Equal(new[] { 18f + 1f, 18f + 36f + 1f }, bands);   // plugin 3 at view 0, plugin 0 at view 2
    }

    [Fact]
    public void Band_IgnoresDuplicatesAndStaleRows()
    {
        var b = new Binding { Selection = new() { 0, 0, 9, -1 } };
        var m = Mount(b);
        Assert.Single(MarkupDrawProbe.QuadBounds(MarkupDrawProbe.Draw(m.List), m.List.SelectedColor));
    }

    [Fact]
    public void PluginTheme_Draws()
    {
        var b = new Binding { Selection = new() { 1 } };
        var m = Mount(b, themes: new PluginUiThemeSettings { Theme = PluginUiTheme.Moss });
        Assert.Single(MarkupDrawProbe.QuadBounds(MarkupDrawProbe.Draw(m.List), m.List.SelectedColor));
    }

    [Theory]
    [InlineData(ControlKey.ControlLeft, new[] { 1, 3 })]
    [InlineData(ControlKey.SuperLeft, new[] { 1, 3 })]
    [InlineData(ControlKey.ShiftLeft, new[] { 1, 3 })]
    public void DefaultModifiers_ReadTheRootKeyboard(ControlKey held, int[] expected)
    {
        var b = new Binding();
        var m = Mount(b, fakeModifiers: false);
        var keyboard = new FakeKeyboard();
        m.Root.Keyboard = keyboard;
        Click(m, NameX, RowY(0));                    // [3], anchor 3
        keyboard.Held.Add((Key)held);
        Click(m, NameX, RowY(1));                    // toggle 1 in, or range views 0..1
        Assert.Equal(expected, b.Selection);
        Assert.Null(m.Root.KeyboardFocus);
    }

    // Silk's Key values, so InlineData can name them.
    public enum ControlKey
    {
        ControlLeft = (int)Key.ControlLeft,
        SuperLeft = (int)Key.SuperLeft,
        ShiftLeft = (int)Key.ShiftLeft,
    }

    [Fact]
    public void BuildErrors()
    {
        var b = new Binding();
        Assert.Contains("selectmode", Assert.Throws<FormatException>(() =>
            BuildPanel(ColumnList("selectmode=\"many\" selected=\"{Selection}\""), b)).Message);
        Assert.Contains("list selected", Assert.Throws<FormatException>(() =>
            BuildPanel(ColumnList("selectmode=\"multi\""), b)).Message);
        Assert.Contains("list selected", Assert.Throws<FormatException>(() =>
            BuildPanel(ColumnList("selectmode=\"multi\" selected=\"{Single}\""), b)).Message);
        Assert.Contains("list onchange", Assert.Throws<FormatException>(() =>
            BuildPanel(ColumnList("selectmode=\"multi\" selected=\"{Selection}\" onchange=\"{Pick}\""), b)).Message);
        Assert.Contains("selectionband", Assert.Throws<FormatException>(() =>
            BuildPanel(ColumnList("selectmode=\"multi\" selected=\"{Selection}\" selectionband=\"false\""), b)).Message);
    }

    [Fact]
    public void SingleMode_IsUnchanged()
    {
        var b = new Binding();
        var panel = BuildPanel(ColumnList("selected=\"{Single}\" onchange=\"{Pick}\""), b);
        var list = panel.Children.OfType<UiMarkupList>().Single();
        Assert.False(list.MultiSelect);
        Assert.False(list.SelectionBandEnabled);
        Assert.Contains("did not resolve to an Action<int> property", Assert.Throws<FormatException>(() =>
            BuildPanel(ColumnList("selected=\"{Single}\" onchange=\"{SetSelection}\""), b)).Message);
    }
}
```

- [ ] **Step 3: Run, expect a compile failure** (`UiSelectModifiers`, `ModifiersSource`, `MultiSelect` not defined).

- [ ] **Step 4: Widget partial** — create `src/AcDream.App/UI/UiMarkupList.Selection.cs`:

```csharp
using Silk.NET.Input;

namespace AcDream.App.UI;

/// <summary>The selection keys held during a pick: Add is Ctrl or Cmd (Super), Range is Shift.</summary>
internal readonly record struct UiSelectModifiers(bool Add, bool Range);

public sealed partial class UiMarkupList
{
    /// <summary><c>selectmode="multi"</c>: any number of rows, read from <see cref="SelectedRowsSource"/>.</summary>
    public bool MultiSelect { get; set; }

    /// <summary>The selected plugin rows in multi mode. The plugin owns them; the list keeps no selection.</summary>
    public Func<IReadOnlyList<int>> SelectedRowsSource { get; set; } = static () => Array.Empty<int>();

    /// <summary>Called in multi mode with the selection a user action asks for, plugin rows ascending.</summary>
    public Action<IReadOnlyList<int>>? SelectedRowsChanged { get; set; }

    /// <summary>The held selection keys; null reads the root's keyboard.</summary>
    internal Func<UiSelectModifiers>? ModifiersSource { get; set; }

    // The row a Shift range runs from (the last plain or Ctrl pick) and the
    // row keyboard steps move from (the last row picked or stepped to), as
    // plugin rows so they follow their rows across a sort; −1 for none.
    // Neither is a selection: what is selected is whatever the plugin returns.
    private int _anchorRow = -1, _leadRow = -1;

    // SelectedRowsSource as a per-plugin-row mask, read once per draw.
    private bool[] _selectedMask = Array.Empty<bool>();

    private UiSelectModifiers HeldModifiers()
    {
        if (ModifiersSource is { } source)
            return source();
        if (FindRoot()?.Keyboard is not { } keyboard)
            return default;
        return new UiSelectModifiers(
            Add: keyboard.IsKeyPressed(Key.ControlLeft) || keyboard.IsKeyPressed(Key.ControlRight)
                || keyboard.IsKeyPressed(Key.SuperLeft) || keyboard.IsKeyPressed(Key.SuperRight),
            Range: keyboard.IsKeyPressed(Key.ShiftLeft) || keyboard.IsKeyPressed(Key.ShiftRight));
    }

    /// <summary>A pointer pick of a plugin row: single mode reports the row, multi mode applies the click rules.</summary>
    private void PickRow(int pluginRow)
    {
        if (!MultiSelect)
        {
            SelectionChanged?.Invoke(pluginRow);
            return;
        }
        UiSelectModifiers keys = HeldModifiers();
        int[] next = UiMarkupListSelection.Click(
            SelectedRowsSource(), pluginRow, keys.Add, keys.Range, _anchorRow, _view);
        if (UiMarkupListSelection.MovesAnchor(keys.Range, _anchorRow, _view.Count))
            _anchorRow = pluginRow;
        _leadRow = pluginRow;
        SelectedRowsChanged?.Invoke(next);
    }

    /// <summary>Reads the multi-mode selection into the mask the band is drawn from.</summary>
    private void RefreshSelectedMask(int rowCount)
    {
        if (_selectedMask.Length < rowCount)
            _selectedMask = new bool[rowCount];
        else
            Array.Clear(_selectedMask);
        foreach (int row in SelectedRowsSource())
        {
            if ((uint)row < (uint)rowCount)
                _selectedMask[row] = true;
        }
    }

    /// <summary>Whether a plugin row draws the selection band this frame.</summary>
    private bool RowBanded(int pluginRow, int selected) => MultiSelect
        ? (uint)pluginRow < (uint)_selectedMask.Length && _selectedMask[pluginRow]
        : pluginRow == selected && SelectionBandEnabled;
}
```

- [ ] **Step 5: Route `UiMarkupList.cs` through it.**
  - *Single-column `OnDraw`:* after `ClampTop(items.Count, visibleRows);` add

    ```csharp
        if (MultiSelect)
        {
            _view.SetIdentity(items.Count);
            RefreshSelectedMask(items.Count);
        }
    ```

    and change the band test `if (index == selected && SelectionBandEnabled)` to `if (RowBanded(index, selected))`.
  - *Single-column `OnEvent`:* replace

    ```csharp
        if (row >= 0 && row < visibleRows && index >= 0 && index < items.Count)
            SelectionChanged?.Invoke(index);
    ```

    with

    ```csharp
        if (row >= 0 && row < visibleRows && index >= 0 && index < items.Count)
        {
            if (MultiSelect)
                _view.SetIdentity(items.Count);
            PickRow(index);
        }
    ```

  - *`DrawColumns`:* after `ClampTop(rowCount, visibleRows);` add `if (MultiSelect) RefreshSelectedMask(rowCount);`. Change the band test `if (view == selectedView && SelectionBandEnabled)` to `if (RowBanded(row, selected))`. `row` is the plugin row already computed at the top of the loop. `selectedView` is still used by the reveal block above, so leave it there.
  - *`OnEventColumns`:* in the `Text` case, replace `SelectionChanged?.Invoke(pluginRow);` with `PickRow(pluginRow);`.

- [ ] **Step 6: Builder** — create `src/AcDream.App/UI/Markup/MarkupListSelect.cs`:

```csharp
using System.Xml.Linq;

namespace AcDream.App.UI.Markup;

/// <summary>A list's selection: <c>selectmode</c>, <c>selected</c>, <c>onchange</c> and <c>selectionband</c>.</summary>
internal static class MarkupListSelect
{
    public static void Apply(XElement el, UiMarkupList list, object binding)
    {
        string? mode = (string?)el.Attribute("selectmode");
        string? selected = (string?)el.Attribute("selected");
        string? onChange = (string?)el.Attribute("onchange");
        switch (mode ?? "single")
        {
            case "single":
                Action<int>? changed = MarkupDocument.BindIntAction(onChange, binding);
                if (onChange is not null && changed is null)
                {
                    throw new FormatException(
                        $"<list onchange=\"{onChange}\"> did not resolve to an "
                        + $"Action<int> property on {binding.GetType().Name}");
                }
                list.SelectedIndexSource = MarkupDocument.BindRequiredIntReader(selected, binding, "list selected");
                list.SelectionChanged = changed;
                list.SelectionBandEnabled = MarkupDocument.B(el, "selectionband", false);
                break;
            case "multi":
                if (!MarkupDocument.B(el, "selectionband", true))
                    throw new FormatException("<list selectmode=\"multi\"> always draws the selection band; remove selectionband=\"false\"");
                list.MultiSelect = true;
                list.SelectedRowsSource = MarkupBindings.BindRequiredIntListReader(selected, binding, "list selected");
                list.SelectedRowsChanged = MarkupBindings.BindIntListAction(onChange, binding, "list onchange");
                list.SelectionBandEnabled = true;
                break;
            default:
                throw new FormatException($"<list selectmode=\"{mode}\"> must be single or multi");
        }
    }
}
```

- [ ] **Step 7: Call it from the list case** — in `MarkupDocument.cs`:
  - Change `private static Action<int>? BindIntAction(string? attribute, object binding)` to `internal static`.
  - In `case "list":`, delete the `listChangeName` / `listChanged` lines and their `throw` block at the top of the case.
  - In the `new UiMarkupList { … }` initializer, delete the three lines `SelectedIndexSource = BindRequiredIntReader(…),`, `SelectionChanged = listChanged,` and `SelectionBandEnabled = B(el, "selectionband", false),`.
  - Directly after the initializer's closing `};`, add `MarkupListSelect.Apply(el, list, binding);`.

- [ ] **Step 8: Run the Verify command** → all pass. If a single-mode test fails, fix the routing; don't change the test.

- [ ] **Step 9: Commit**

```bash
git add src/AcDream.App/UI/UiMarkupList.Selection.cs src/AcDream.App/UI/UiMarkupList.cs \
        src/AcDream.App/UI/Markup/MarkupListSelect.cs src/AcDream.App/UI/MarkupDocument.cs \
        tests/AcDream.App.Tests/Plugins/FakeKeyboard.cs tests/AcDream.App.Tests/UI/MarkupListMultiSelectTests.cs
git commit -m "feat(ui): list selectmode=\"multi\" with click, Ctrl and Shift picks

Claude-Session: https://claude.ai/code/session_014SAojwkxRKxrwp1RMDGPVC"
```

---

### Task 4: Multi-select keyboard, focus and lead reveal

**Goal:** A focused multi list moves, extends and selects all with the keyboard, shows the focus outline, and scrolls the lead row into view.

**Files:**
- Modify: `src/AcDream.App/UI/UiMarkupList.Selection.cs`
- Modify: `src/AcDream.App/UI/UiMarkupList.cs` (`OnEvent`, `OnEventColumns`, both draw paths)
- Modify: `src/AcDream.App/UI/Markup/MarkupListSelect.cs` (focus flags)
- Test: `tests/AcDream.App.Tests/UI/MarkupListMultiSelectTests.cs`

**Acceptance Criteria:**
- [ ] A multi list is `AcceptsFocus`, `TabStop` and not `FocusOnMouseClick`. A single list is none of these, as before.
- [ ] Focused, in sorted view order (3, 1, 0, 2):
  - Down with no lead selects `[3]`; Down again gives `[1]`; Up gives `[3]`; Up at the top gives `[3]` and still fires.
  - Shift+Down extends from the anchor: `[1,3]`, then `[0,1,3]`; Shift+Up shrinks to `[1,3]`.
  - Ctrl+A and Cmd(Super)+A select `[0,1,2,3]`.
  - Keys continue from the last clicked row. The click clears keyboard focus (UiRoot rule), so the test re-focuses.
- [ ] Unfocused, keys select nothing and reach `WorldKeyFallThrough`. Focused, `W` falls through and `Down` does not. Disabled and focused, nothing fires.
- [ ] The single-column form steps in plugin order.
- [ ] Stepping the lead below the visible rows scrolls it into view. A sort change reveals it again. A live data reorder that moves the lead's row does not scroll.
- [ ] The focus outline (4 quads of `UiMarkupExpanderHeader.FocusColor`) draws only while focused.

**Verify:** `dotnet test tests/AcDream.App.Tests/AcDream.App.Tests.csproj --filter "FullyQualifiedName~MarkupListMultiSelectTests|FullyQualifiedName~MarkupListColumnsTests|FullyQualifiedName~MarkupListHeaderTests|FullyQualifiedName~ListButtonColumn"` → all pass.

**Steps:**

- [ ] **Step 1: Write the failing tests** — append to `MarkupListMultiSelectTests`:

```csharp
    private static void Press(Mounted m, Key key, UiSelectModifiers keys = default)
    {
        m.Keys = keys;
        m.Root.OnKeyDown((int)key);
        Redraw(m.List);
    }

    private static Mounted MountFocused(Binding b, string? listXml = null)
    {
        var m = Mount(b, listXml);
        m.Root.SetKeyboardFocus(m.List);
        return m;
    }

    [Fact]
    public void Focus_FlagsOnlyInMultiMode()
    {
        var multi = Mount(new Binding()).List;
        Assert.True(multi.AcceptsFocus && multi.TabStop);
        Assert.False(multi.FocusOnMouseClick);
        var single = BuildPanel(ColumnList("selected=\"{Single}\""), new Binding())
            .Children.OfType<UiMarkupList>().Single();
        Assert.False(single.TabStop);
    }

    [Fact]
    public void UpDown_MoveASingleSelectionInViewOrder()
    {
        var b = new Binding();
        var m = MountFocused(b);
        Press(m, Key.Down); Assert.Equal(new[] { 3 }, b.Selection);
        Press(m, Key.Down); Assert.Equal(new[] { 1 }, b.Selection);
        Press(m, Key.Up);   Assert.Equal(new[] { 3 }, b.Selection);
        b.Changes.Clear();
        Press(m, Key.Up);
        Assert.Equal(new[] { 3 }, Assert.Single(b.Changes));
    }

    [Fact]
    public void ShiftUpDown_ExtendFromTheAnchor()
    {
        var b = new Binding();
        var m = MountFocused(b);
        Press(m, Key.Down);          // [3], anchor 3
        Press(m, Key.Down, Range);   Assert.Equal(new[] { 1, 3 }, b.Selection);
        Press(m, Key.Down, Range);   Assert.Equal(new[] { 0, 1, 3 }, b.Selection);
        Press(m, Key.Up, Range);     Assert.Equal(new[] { 1, 3 }, b.Selection);
    }

    [Fact]
    public void CtrlA_SelectsAll()
    {
        var b = new Binding();
        var m = MountFocused(b);
        Press(m, Key.A, Add);
        Assert.Equal(new[] { 0, 1, 2, 3 }, b.Selection);
    }

    [Fact]
    public void Keys_ContinueFromTheClickedRow()
    {
        var b = new Binding();
        var m = MountFocused(b);
        Click(m, NameX, RowY(2));                 // plugin 0
        Assert.Null(m.Root.KeyboardFocus);        // a click on a non-text control clears focus (UiRoot rule)
        m.Root.SetKeyboardFocus(m.List);
        Press(m, Key.Down);                       // view 3 = plugin 2
        Assert.Equal(new[] { 2 }, b.Selection);
    }

    [Fact]
    public void Keys_FallThroughWhenUnfocusedOrUnhandled()
    {
        var b = new Binding();
        var m = Mount(b);
        var fell = new List<int>();
        m.Root.WorldKeyFallThrough += (vk, _) => fell.Add(vk);
        Press(m, Key.Down);
        Assert.Empty(b.Changes);
        m.Root.SetKeyboardFocus(m.List);
        Press(m, Key.W);
        Press(m, Key.Down);
        Assert.Equal(new[] { (int)Key.Down, (int)Key.W }, fell);
        Assert.Single(b.Changes);
    }

    [Fact]
    public void Keys_DisabledFireNothing()
    {
        var b = new Binding();
        var m = MountFocused(b, ColumnList("selectmode=\"multi\" selected=\"{Selection}\" onchange=\"{SetSelection}\" enabled=\"false\""));
        Press(m, Key.Down);
        Press(m, Key.A, Add);
        Assert.Empty(b.Changes);
    }

    [Fact]
    public void Keys_SingleColumnFormStepsInPluginOrder()
    {
        var b = new Binding();
        var m = MountFocused(b, """
              <list x="0" y="0" w="300" h="108" items="{Names}" selectmode="multi" selected="{Selection}" onchange="{SetSelection}"/>
            """);
        Press(m, Key.Down);
        Press(m, Key.Down);
        Assert.Equal(new[] { 1 }, b.Selection);
    }

    [Fact]
    public void Lead_IsRevealedButReordersDoNotScroll()
    {
        var b = new Binding
        {
            Names = Enumerable.Range(0, 12).Select(i => $"mob {i:00}").ToList(),
            On = Enumerable.Repeat(false, 12).ToList(),
            Acts = Enumerable.Repeat("Go", 12).ToList(),
        };
        var m = MountFocused(b);                  // 108 px: header + 5 rows (top row y 19, last y 91)
        for (int i = 0; i < 8; i++)
            Press(m, Key.Down);                   // lead plugin 7 at view 7: scrolls to top row 3
        var band = Assert.Single(MarkupDrawProbe.QuadBounds(MarkupDrawProbe.Draw(m.List), m.List.SelectedColor));
        Assert.Equal(18f + 4 * 18f + 1f, band.Y);

        b.SortDesc = true;                        // descending: plugin 7 at view 4, still in rows 3..7
        Redraw(m.List);
        band = Assert.Single(MarkupDrawProbe.QuadBounds(MarkupDrawProbe.Draw(m.List), m.List.SelectedColor));
        Assert.Equal(18f + 1 * 18f + 1f, band.Y);

        // Live data moves the lead's row to the end (view 11): no reveal, so it scrolls out of sight.
        b.Names = b.Names.Select((n, i) => i == 7 ? "a" : n).ToList();
        Redraw(m.List);
        Assert.Empty(MarkupDrawProbe.QuadBounds(MarkupDrawProbe.Draw(m.List), m.List.SelectedColor));
    }

    [Fact]
    public void FocusOutline_OnlyWhileFocused()
    {
        var m = Mount(new Binding());
        Assert.Equal(0, MarkupDrawProbe.CountQuads(MarkupDrawProbe.Draw(m.List), UiMarkupExpanderHeader.FocusColor));
        m.Root.SetKeyboardFocus(m.List);
        Assert.Equal(4, MarkupDrawProbe.CountQuads(MarkupDrawProbe.Draw(m.List), UiMarkupExpanderHeader.FocusColor));
        m.Root.SetKeyboardFocus(null);
        Assert.Equal(0, MarkupDrawProbe.CountQuads(MarkupDrawProbe.Draw(m.List), UiMarkupExpanderHeader.FocusColor));
    }
```

`UiRoot.OnMouseDown` sets focus to `target.AcceptsFocus && target.FocusOnMouseClick ? target : null`, so a click on the list clears keyboard focus; that is the existing rule for non-text controls and `Keys_ContinueFromTheClickedRow` asserts it. Don't change `UiRoot`.

- [ ] **Step 2: Run, expect failures** (no focus flags, no key handling).

- [ ] **Step 3: Focus flags** — in `MarkupListSelect.Apply`, in the `"multi"` case after `list.MultiSelect = true;`, add:

```csharp
                list.AcceptsFocus = true;
                list.FocusOnMouseClick = false;
                list.TabStop = true;
```

- [ ] **Step 4: Keys and focus** — append to `UiMarkupList.Selection.cs` (inside the class):

```csharp
    private bool _selectionFocused;

    /// <summary>
    /// Multi-mode focus and selection keys: Up/Down step the lead (Shift
    /// extends from the anchor), Ctrl/Cmd+A selects every row. Up and Down are
    /// always taken while focused; other keys fall through.
    /// </summary>
    private bool HandleSelectionEvent(in UiEvent e)
    {
        switch (e.Type)
        {
            case UiEventType.FocusGained when TabStop:
                _selectionFocused = true;
                return true;
            case UiEventType.FocusLost when TabStop:
                _selectionFocused = false;
                return true;
            case UiEventType.KeyDown when _selectionFocused && Enabled:
                break;
            default:
                return false;
        }

        UiSelectModifiers keys = HeldModifiers();
        var key = (Key)e.Data0;
        if (key == Key.A && keys.Add)
        {
            SelectedRowsChanged?.Invoke(UiMarkupListSelection.All(RefreshSelectionRows()));
            return true;
        }
        int delta = key switch { Key.Up => -1, Key.Down => 1, _ => 0 };
        if (delta == 0)
            return false;

        RefreshSelectionRows();
        int lead = UiMarkupListSelection.Step(_leadRow, delta, _view);
        if (lead < 0)
            return true;
        int[] next;
        if (keys.Range && (uint)_anchorRow < (uint)_view.Count)
        {
            next = UiMarkupListSelection.Range(_anchorRow, lead, _view);
        }
        else
        {
            next = [lead];
            _anchorRow = lead;
        }
        _leadRow = lead;
        SelectedRowsChanged?.Invoke(next);
        return true;
    }

    /// <summary>Reads the rows now, so keys act on the current data and order; returns the row count.</summary>
    private int RefreshSelectionRows()
    {
        if (Columns is { Count: > 0 } columns)
            return RefreshRows(columns);
        int count = ItemsSource().Count;
        _view.SetIdentity(count);
        return count;
    }

    /// <summary>The gold outline a focused multi list draws over its border.</summary>
    private void DrawSelectionFocus(UiRenderContext context)
    {
        if (_selectionFocused)
            context.DrawRectOutline(0f, 0f, Width, Height, UiMarkupExpanderHeader.FocusColor, 1f);
    }
```

- [ ] **Step 5: Wire it into `UiMarkupList.cs`.**
  - *`OnEvent`:* make the first statement
    ```csharp
        if (MultiSelect && HandleSelectionEvent(e))
            return true;
    ```
    It goes before the `Columns` branch, so it serves both forms.
  - *Reveal, single-column `OnDraw`:* change

    ```csharp
        int selected = SelectedIndexSource();
        if (selected != _lastRevealedSelected)
        {
            _lastRevealedSelected = selected;
            if (selected >= 0 && selected < items.Count)
            {
                if (selected < _topRow)
                    _topRow = selected;
                else if (selected >= _topRow + visibleRows)
                    _topRow = selected - visibleRows + 1;
            }
        }
    ```

    to reveal `MultiSelect ? _leadRow : selected`:

    ```csharp
        int selected = SelectedIndexSource();
        int reveal = MultiSelect ? _leadRow : selected;
        if (reveal != _lastRevealedSelected)
        {
            _lastRevealedSelected = reveal;
            if (reveal >= 0 && reveal < items.Count)
            {
                if (reveal < _topRow)
                    _topRow = reveal;
                else if (reveal >= _topRow + visibleRows)
                    _topRow = reveal - visibleRows + 1;
            }
        }
    ```

  - *Reveal, `DrawColumns`:* change

    ```csharp
        int selected = SelectedIndexSource();
        int selectedView = _view.ViewRow(selected);
        (int Column, bool Descending) sort = CurrentSort;
        if (selected != _lastRevealedSelected || sort != _lastRevealedSort)
        {
            _lastRevealedSelected = selected;
    ```

    to

    ```csharp
        int selected = SelectedIndexSource();
        int reveal = MultiSelect ? _leadRow : selected;
        int selectedView = _view.ViewRow(reveal);
        (int Column, bool Descending) sort = CurrentSort;
        if (reveal != _lastRevealedSelected || sort != _lastRevealedSort)
        {
            _lastRevealedSelected = reveal;
    ```

    The rest of the block is unchanged. It uses `selectedView`, which is now the revealed row's view. The band line from Task 3 uses `RowBanded(row, selected)`, not `selectedView`, so it is unaffected.
  - *Focus outline:* call `DrawSelectionFocus(context);` as the last statement of `DrawColumns`, after the scrollbar. Call it in the single-column `OnDraw` after its `if (showScrollbar) DrawScrollbar(…)`.

- [ ] **Step 6: Run the Verify command** → all pass.

- [ ] **Step 7: Commit**

```bash
git add src/AcDream.App/UI/UiMarkupList.Selection.cs src/AcDream.App/UI/UiMarkupList.cs \
        src/AcDream.App/UI/Markup/MarkupListSelect.cs tests/AcDream.App.Tests/UI/MarkupListMultiSelectTests.cs
git commit -m "feat(ui): keyboard selection, focus outline and lead reveal for multi-select lists

Claude-Session: https://claude.ai/code/session_014SAojwkxRKxrwp1RMDGPVC"
```

---

### Task 5: Docs

**Goal:** `docs/plugin-ui-markup.md` documents multi-select.

**Files:**
- Modify: `docs/plugin-ui-markup.md`

**Acceptance Criteria:**
- [ ] In the Elements table, the `list` row's attributes gain `selectmode`.
- [ ] The Bindings table's `list selected` row reads `int`; with `selectmode="multi"`, `IEnumerable<int>` of plugin rows. The `list onchange` row reads `Action<int>`; with `selectmode="multi"`, `Action<IReadOnlyList<int>>`.
- [ ] A `### Multi-select` subsection, after `### Header, sorting and column widths` and before `## Slider range`, covers:
  - the example;
  - the plugin owning the selection, and plugin rows ascending;
  - which cells pick;
  - the click rules (Ctrl or Cmd, Shift ranges in view order, Ctrl+Shift, no anchor);
  - keyboard focus by Tab only, Up/Down, Shift, Ctrl/Cmd+A, and the outline;
  - the implied band;
  - `onchange` firing on every action, and display-only without it;
  - build errors;
  - the older-host line.
- [ ] The older-host line is checked against base 391e709a: `git show 391e709a:src/AcDream.App/UI/MarkupDocument.cs`. There the list binds `selected` with `BindRequiredIntReader`, so a multi list's list binding fails that host's build. Say so.

**Verify:** `grep -c 'selectmode' docs/plugin-ui-markup.md` → at least 4.

**Steps:**

- [ ] **Step 1: Tables** — edit the two tables as listed, keeping their wording style.

- [ ] **Step 2: Section** — insert before `## Slider range`:

````markdown
### Multi-select

```xml
<list name="loot" x="8" y="24" w="320" h="200" header="true" selectmode="multi"
      selected="{Picked}" onchange="{SetPicked}">
  <column type="text" header="Item" items="{Names}" sort="text"/>
  <column type="text" header="Value" width="60" items="{Values}" sort="number"/>
</list>
```

With `selectmode="multi"` the player can select any number of rows.
`selected` binds the selected rows (any `IEnumerable<int>`) and `onchange`
is an `Action<IReadOnlyList<int>>` that gets the new selection, as the
plugin's own row indices in ascending order. The plugin owns the selection:
the list draws whatever `selected` returns each frame and only asks for a
change, so set the property in `onchange`. Without `onchange` the list only
displays. `onchange` fires on every click or key the list handles, even if
the selection comes out the same.

Rows are picked where a single-select list picks them: anywhere in the
single-column form, and on a text column without `onclick` in the column
form. Check, icon and button cells keep their own actions.

- Click selects only that row.
- Ctrl-click (Cmd-click on macOS) adds or removes that row.
- Shift-click selects every row from the last plain or Ctrl click to this
  one, as they are shown, so a sorted list selects what the player sees
  between the two. Ctrl+Shift-click adds that range to the selection.
  Shift-click with no earlier click selects just that row.

The list can take keyboard focus with Tab; clicking it never takes focus
from the game. While focused it draws the gold focus outline. Up and Down
select the row above or below the last one picked (the first row if none),
Shift+Up and Shift+Down extend the range instead, and Ctrl+A (Cmd+A)
selects every row. The list keeps the last-picked row in view. Other keys
go on to the game.

Every selected row draws the selection band; `selectionband` is implied,
and `selectionband="false"` fails the build. Other build errors:
`selectmode` other than `single` or `multi`, `selected` that isn't bound to
a list of ints, and `onchange` that isn't an `Action<IReadOnlyList<int>>`.
On a host older than multi-select, `selected` must be an `int`, so a
multi-select list fails the build of its window there.
````

Re-check every sentence against `UiMarkupList.Selection.cs`, `UiMarkupListSelection.cs` and `MarkupListSelect.cs`. Also confirm the older-host claim with `git show 391e709a:src/AcDream.App/UI/MarkupDocument.cs | grep -n 'list selected'`.

- [ ] **Step 3: Commit**

```bash
git add docs/plugin-ui-markup.md
git commit -m "docs(ui): list multi-select

Claude-Session: https://claude.ai/code/session_014SAojwkxRKxrwp1RMDGPVC"
```

---

### Task 6: Series gate

**Goal:** The Release build is clean, App, Plugin and UI.Abstractions tests have no new failures, and every commit builds.

**Files:** none changed.

**Acceptance Criteria:**
- [ ] `dotnet build AcDream.slnx -c Release` → 0 errors, 0 warnings.
- [ ] App + Plugin failures ⊆ `mc-base.txt` (162 names); UI.Abstractions failures ⊆ `mc-uiabs-base.txt` (empty).
- [ ] Each of the series' commits builds and passes the list and markup test filters on its own.
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
done | norm > $T/mc8-after.txt
dotnet test tests/AcDream.UI.Abstractions.Tests/AcDream.UI.Abstractions.Tests.csproj -c Release 2>&1 | norm > $T/mc8-uiabs-after.txt
echo "new App/Plugin:"; comm -13 $T/mc-base.txt $T/mc8-after.txt
echo "new UI.Abstractions:"; comm -13 $T/mc-uiabs-base.txt $T/mc8-uiabs-after.txt
git checkout -- '*.lock.json' 2>/dev/null; git status --short
```
Expected: `0 Warning(s)`, `0 Error(s)`, nothing after either "new" line.

- [ ] **Step 2: Each commit builds alone**

```bash
cd /Users/davidsmith/code/OpenAC/OpenAC
for c in $(git rev-list --reverse fd03cfb0..feat/markup-controls); do
  git worktree add -q --detach .worktrees/mc-commit-check $c
  (cd .worktrees/mc-commit-check && dotnet test tests/AcDream.App.Tests/AcDream.App.Tests.csproj -c Release \
     --filter "FullyQualifiedName~AcDream.App.Tests.UI.Markup|FullyQualifiedName~ListButtonColumn|FullyQualifiedName~UiMarkupList|FullyQualifiedName~RememberedValueTests" 2>&1 \
     | grep -E ': error|: warning|Passed!|Failed!' | sort -u | sed "s/^/$(git rev-parse --short $c) /")
  git worktree remove --force .worktrees/mc-commit-check
done
```
Expected: one `Passed!` per commit, no `error`/`warning`.

- [ ] **Step 3: Branch contents**

```bash
cd /Users/davidsmith/code/OpenAC/OpenAC/.worktrees/markup-controls
git log --oneline fd03cfb0..HEAD
git diff --stat 391e709a..HEAD | grep superpowers || echo "no superpowers docs"
```
Expected: the 5 commits of Tasks 1-5 (plus any review-fix commits); "no superpowers docs".

No commit.
