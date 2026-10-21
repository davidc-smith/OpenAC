# Markup controls, plan 9: `<list tree="true">` Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers-extended-cc:subagent-driven-development (recommended) or superpowers-extended-cc:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** A column `<list>` can show its rows as a tree. Each row has a depth and a stable key, and the first text column draws the indent and a ▸/▾ arrow. Parents collapse and expand by click or Left/Right. Expansion is plugin-owned when bound and remembered per character when not. Sorting sorts siblings, and multi-select works on visible rows.

**Architecture:**
- **View map split first.** `UiMarkupListViewMap` gets `ViewCount` (rows shown) and `PluginCount` (the plugin's rows). They differ once collapsed rows are left out. All row positions (scrolling, hit tests, drawing) use `ViewCount`. Row validity uses `PluginCount`. This lands as a pure refactor.
- **Tree shape.** A pure `UiMarkupListTreeShape` loads the levels each frame and clamps them. It knows each row's parent and children, and holds a per-row collapsed flag.
  - The view map's `SetIdentity`, `SortByText` and `SortByNumber` take an optional shape.
  - With a shape, the view map arranges rows in pre-order. It sorts each set of siblings with the same stable comparer, and leaves out the descendants of collapsed rows.
- **Widget.** A new partial, `UiMarkupList.Tree.cs`, does the following:
  - reads levels, keys and expansion each frame (`LoadTree`);
  - draws the indent and arrow in the tree column;
  - toggles expansion on an arrow click or Left/Right;
  - drops hidden rows from a multi selection.
- **Builder.** `Markup/MarkupListTree.cs` binds `tree levels keys expanded onexpand collapsed`.
- **State.**
  - Bound: `expanded` is a set of keys, read every frame; `onexpand(key, state)` asks for a change.
  - Unbound: the host remembers the keys whose state differs from the default (expanded, or collapsed with `collapsed="true"`). It stores them as a JSON array of strings under kind `"expanded"`.

**Status:** DONE 2026-10-22 — feat 502f7bd5..9b4b5523 (9 commits incl. docs fix + final polish; each code commit builds; gate = baseline 162). Plan bug fixed in Task 4: LoadTree's unbound test must be `toggled.Contains(key) == StartCollapsed`. Keys bind with preserveSnapshot. A user collapse request trims a multi selection even in bound mode (documented).

**Tech Stack:** C# / .NET 10, System.Text.Json, xUnit, headless `UiRoot` + `RecordingGpuDevice` tests.

**Spec:** `docs/superpowers/specs/2026-10-21-markup-controls-design.md` (branch `docs/markup-controls-spec`), "Shared rules" and section 10. It builds on plan 7 (view map, sort) and plan 8 (multi-select, `UiMarkupListSelection`, focus/keys in `UiMarkupList.Selection.cs`).

## Global Constraints

- **Workspace.** Worktree `/Users/davidsmith/code/OpenAC/OpenAC/.worktrees/markup-controls`, branch `feat/markup-controls` (HEAD 502f7bd5, from upstream 391e709a).
  - Never merge fork `main` in, and never add `docs/superpowers/` or `.superpowers/` files.
  - Push only when the controller says so.
- **Build hygiene.** Every shell runs `export DOTNET_ROOT=$HOME/.dotnet PATH=$HOME/.dotnet:$PATH` first. After building, run `git checkout -- '*.lock.json'`; never commit lock files.
- **Commits.** Every commit must build and pass its own tests. Commit messages end with `Claude-Session: https://claude.ai/code/session_014SAojwkxRKxrwp1RMDGPVC`.
- **Helper conventions.**
  - `BindRequired*` helpers throw when the attribute is absent or mistyped.
  - Other `Bind*` helpers return null when absent and throw when mistyped.
  - `context` is `"<element> <attribute>"`.
  - The list case still adds exactly one element.
- **Plugin rows.** Every callback, every bound `selected`, and every row index a plugin sees is a plugin row. Collapsed rows' descendants are left out of the view, never renumbered.
- **Remembered-state contract** (`RememberedValue`):
  - call `TryRefresh` before building a value to remember;
  - write only on a user action (an arrow click or Left/Right);
  - unnamed lists and bound expansion never touch the store;
  - remove the entry when no key differs from the default;
  - an undecodable stored value counts as absent.
- **`<list tree="true">` contract.**
  - Needs `<column>` children and at least one `text` column. The tree draws in the first `text` column.
  - `levels`: any `IEnumerable<int>`, required. 0 = top level.
    - A negative level, or a row past the end of `levels`, is level 0.
    - A row more than one deeper than the row before it is one deeper.
  - `keys`: any `IEnumerable<string>`, required. A row past the end gets the key `"#<row>"` (invariant digits).
  - `expanded`: `IReadOnlySet<string>` (read as is) or any `IEnumerable<string>` (copied into an ordinal set each frame), optional. When bound, the plugin owns expansion.
  - `onexpand`: `Action<string, bool>`, the key and the asked-for state, optional.
  - `collapsed="true|false"` (default false): only when `expanded` is not bound; with `expanded` bound it throws.
  - `levels`, `keys`, `expanded`, `onexpand` and `collapsed` without `tree="true"` throw.
- **Layout and input.**
  - The indent is 12 px per level (`UiMarkupList.TreeIndent`).
  - In the tree column, a row's 12 px arrow slot starts at `cellX + Padding + level × 12`, and the text starts at the slot's end. Leaf rows keep the slot empty.
  - A click inside the slot of a row with children toggles it and does not select.
  - Left/Right act on the selected row (single mode) or the lead (multi mode), when that row is shown:
    - Right on a collapsed parent expands it;
    - Right on an expanded parent moves to its first child (the next view row);
    - Left on an expanded parent collapses it;
    - Left on a collapsed parent or a leaf moves to its parent (if any).
  - "Moves to" means `onchange(row)` in single mode, and `onchange([row])` with anchor = lead = row in multi mode.
  - A tree list is `AcceptsFocus`, `TabStop` and `FocusOnMouseClick = false` in both modes. Left and Right are always handled while it has focus.
- **Sorting** sorts each set of siblings with the column's keys (stable: ties keep plugin order; missing keys last) and keeps children under their parent.
- **Multi-select** works on shown rows: Shift ranges and Ctrl/Cmd+A cover only shown rows. A user collapse removes the collapsed row's descendants from the selection and calls `onchange` if anything was removed. An anchor or lead inside them moves to the collapsed row. Single mode keeps its selection on collapse.

**User decisions (already made):**
- One branch, one commit series per control, upstream-portable.
- The tree is `<list tree="true">` with levels + keys, not a node-object API (spec "Out of scope").
- Remembered state is "both": unbound is host-remembered per character/window/control name; bound is the plugin's.
- Non-text controls don't take keyboard focus on click; focus comes by Tab from another focused control (user, 2026-10-22: leave as is for now).

---

## File Structure

| File | Responsibility |
|---|---|
| `src/AcDream.App/UI/UiMarkupListViewMap.cs` | `ViewCount`/`PluginCount`; tree arrangement |
| `src/AcDream.App/UI/UiMarkupListTreeShape.cs` (new) | Levels (clamped), parents, children, collapsed flags |
| `src/AcDream.App/UI/UiMarkupListSelection.cs` | Uses `PluginCount`/`ViewCount`; `All(view)` = shown rows |
| `src/AcDream.App/UI/UiMarkupList.cs` | Positions use `_view.ViewCount`; tree cell drawing; arrow click |
| `src/AcDream.App/UI/UiMarkupList.Selection.cs` | Counts split; Left/Right routed to the tree |
| `src/AcDream.App/UI/UiMarkupList.Sort.cs` | `SortView` passes the tree shape |
| `src/AcDream.App/UI/UiMarkupList.Tree.cs` (new) | Tree properties, `LoadTree`, expansion state + codec, arrow, toggle, keys, deselect |
| `src/AcDream.App/UI/Markup/MarkupBindings.cs` | `BindRequiredStringSetReader`, `BindStringBoolAction` |
| `src/AcDream.App/UI/Markup/MarkupListTree.cs` (new) | Builder for the tree attributes |
| `src/AcDream.App/UI/MarkupDocument.cs` | The list case calls `MarkupListTree.Apply` |
| `tests/AcDream.App.Tests/UI/UiMarkupListViewMapTests.cs`, `UiMarkupListSelectionTests.cs` | Count split |
| `tests/AcDream.App.Tests/UI/UiMarkupListTreeShapeTests.cs`, `MarkupTreeBindingsTests.cs`, `MarkupListTreeTests.cs` (new) | Tests |
| `docs/plugin-ui-markup.md` | Tree docs |

---

### Task 1: Split the view map's view count from its plugin count

**Goal:** `UiMarkupListViewMap` exposes `ViewCount` and `PluginCount` instead of `Count`, and every caller uses the right one. Behaviour is unchanged, because both counts are equal until the tree arrives.

**Files:**
- Modify: `src/AcDream.App/UI/UiMarkupListViewMap.cs`
- Modify: `src/AcDream.App/UI/UiMarkupListSelection.cs`
- Modify: `src/AcDream.App/UI/UiMarkupList.Selection.cs`
- Modify: `src/AcDream.App/UI/UiMarkupList.cs` (`DrawColumns`, `OnEventColumns`)
- Test: `tests/AcDream.App.Tests/UI/UiMarkupListViewMapTests.cs`, `tests/AcDream.App.Tests/UI/UiMarkupListSelectionTests.cs`

**Acceptance Criteria:**
- [ ] `Count` is gone. `PluginRow(view)` is bounded by `ViewCount`, and `ViewRow(plugin)` by `PluginCount`. `SetIdentity` and both sorts set the two counts equal.
- [ ] `UiMarkupListSelection.Click` validates rows against `PluginCount`, and `Step` clamps by `ViewCount`. `All` becomes `All(UiMarkupListViewMap view)`, which returns the shown plugin rows in ascending order.
- [ ] The list's row positions use `_view.ViewCount`: scrollbar shown, `ClampTop`, the drawn range, `DrawScrollbar`, and `OnEventColumns`'s row count. The selected-row mask and the select-all header still use the plugin row count.
- [ ] All existing list, multi-select and view map tests pass. The view map tests gain one asserting `ViewCount == PluginCount` after identity and after a sort.

**Verify:** `dotnet test tests/AcDream.App.Tests/AcDream.App.Tests.csproj --filter "FullyQualifiedName~UiMarkupList|FullyQualifiedName~MarkupList|FullyQualifiedName~ListButtonColumn"` → all pass.

**Steps:**

- [ ] **Step 1: View map** — in `UiMarkupListViewMap.cs` replace `public int Count { get; private set; }` with:

```csharp
    /// <summary>The rows shown, top to bottom.</summary>
    public int ViewCount { get; private set; }

    /// <summary>The plugin's rows, shown or not.</summary>
    public int PluginCount { get; private set; }
```

  Then:
  - make `PluginRow` read `(uint)view >= (uint)ViewCount ? -1 : …`;
  - make `ViewRow` read `(uint)plugin >= (uint)PluginCount ? -1 : …`;
  - in `SetIdentity` and at the end of `Apply`, replace `Count = count;` with `ViewCount = PluginCount = count;`.

- [ ] **Step 2: Selection rules** — in `UiMarkupListSelection.cs`:
  - In `Click`, use `int count = view.PluginCount;`.
  - In `Step`, replace both `view.Count` with `view.ViewCount`.
  - Replace `All` with:

```csharp
    /// <summary>Every row shown.</summary>
    public static int[] All(UiMarkupListViewMap view)
    {
        var rows = new int[view.ViewCount];
        for (int v = 0; v < rows.Length; v++)
            rows[v] = view.PluginRow(v);
        Array.Sort(rows);
        return rows;
    }
```

  Update the class summary's "0..view.Count-1" to "0..view.PluginCount-1".

- [ ] **Step 3: Widget**
  - `UiMarkupList.Selection.cs`:
    - In `PickRow`, the `MovesAnchor` call takes `_view.PluginCount`.
    - In the key handler, the anchor test becomes `(uint)_anchorRow < (uint)_view.PluginCount`.
    - Ctrl+A becomes:

    ```csharp
        if (key == Key.A && keys.Add)
        {
            RefreshSelectionRows();
            SelectedRowsChanged?.Invoke(UiMarkupListSelection.All(_view));
            return true;
        }
    ```

  - `UiMarkupList.cs`, `DrawColumns`: directly after `int rowCount = RefreshRows(columns);` add `int viewCount = _view.ViewCount;`. Use `viewCount` instead of `rowCount` in four places:
    - `bool showScrollbar = …`;
    - `ClampTop(…)`;
    - `int end = Math.Min(…)`;
    - `DrawScrollbar(…)`.

    Keep `RefreshSelectedMask(rowCount)`.
  - `OnEventColumns`: replace `int rowCount = _cachedRowCount;` with `int rowCount = _view.ViewCount; // rows shown`.

- [ ] **Step 4: Tests**
  - In `UiMarkupListViewMapTests.cs`, replace `map.Count` with `map.ViewCount` and add:

```csharp
    [Fact]
    public void Counts_AreEqualWithoutATree()
    {
        var map = new UiMarkupListViewMap();
        map.SetIdentity(3);
        Assert.Equal((3, 3), (map.ViewCount, map.PluginCount));
        map.SortByNumber(new double[] { 2, 1 }, 2, descending: true);
        Assert.Equal((2, 2), (map.ViewCount, map.PluginCount));
    }
```

  - In `UiMarkupListSelectionTests.cs`, replace `All_IsEveryRow` with:

```csharp
    [Fact]
    public void All_IsEveryShownRowAscending()
    {
        Assert.Equal(new[] { 0, 1, 2 }, UiMarkupListSelection.All(Identity(3)));
        Assert.Empty(UiMarkupListSelection.All(Identity(0)));
        Assert.Equal(new[] { 0, 1, 2, 3 }, UiMarkupListSelection.All(Sorted()));
    }
```

- [ ] **Step 5: Run the Verify command** → all pass. Also run `grep -rn "_view.Count\|map.Count\|view.Count" src/AcDream.App/UI tests/AcDream.App.Tests/UI`; it should print nothing.

- [ ] **Step 6: Commit**

```bash
git add src/AcDream.App/UI/UiMarkupListViewMap.cs src/AcDream.App/UI/UiMarkupListSelection.cs \
        src/AcDream.App/UI/UiMarkupList.Selection.cs src/AcDream.App/UI/UiMarkupList.cs \
        tests/AcDream.App.Tests/UI/UiMarkupListViewMapTests.cs tests/AcDream.App.Tests/UI/UiMarkupListSelectionTests.cs
git commit -m "refactor(ui): list view map counts shown rows apart from plugin rows

Claude-Session: https://claude.ai/code/session_014SAojwkxRKxrwp1RMDGPVC"
```

---

### Task 2: Tree shape and tree arrangement

**Goal:** `UiMarkupListTreeShape` loads clamped levels, and the view map arranges a tree: pre-order, siblings sorted, collapsed descendants left out.

**Files:**
- Create: `src/AcDream.App/UI/UiMarkupListTreeShape.cs`
- Modify: `src/AcDream.App/UI/UiMarkupListViewMap.cs`
- Test: `tests/AcDream.App.Tests/UI/UiMarkupListTreeShapeTests.cs` (new)

**Acceptance Criteria:**
- [ ] `Load` clamps levels: negative → 0, past the end → 0, deeper than the previous row + 1 → previous + 1. `Parent`, `HasChildren` and `SubtreeEnd` follow from the clamped levels. `Load` clears all collapsed flags.
- [ ] `SetIdentity(count, tree)` shows the tree in plugin order, leaving out descendants of collapsed rows. Hidden rows have `ViewRow == -1`; `ViewCount` is the shown count and `PluginCount` is `count`.
- [ ] `SortByText` and `SortByNumber` with a tree sort each set of siblings (stable, missing keys last, both directions) and keep children directly under their parent.
- [ ] Without a tree, the existing behaviour is byte-for-byte unchanged (existing view map tests pass).
- [ ] A 2,000-deep chain arranges without recursion (no stack overflow) and shows every row in order.

**Verify:** `dotnet test tests/AcDream.App.Tests/AcDream.App.Tests.csproj --filter "FullyQualifiedName~UiMarkupListTreeShapeTests|FullyQualifiedName~UiMarkupListViewMapTests"` → all pass.

**Steps:**

- [ ] **Step 1: Write the failing tests** — create `tests/AcDream.App.Tests/UI/UiMarkupListTreeShapeTests.cs`:

```csharp
using System;
using System.Linq;
using AcDream.App.UI;

namespace AcDream.App.Tests.UI;

public sealed class UiMarkupListTreeShapeTests
{
    // A, A1, A2, A2a, B, B1
    private static readonly int[] Levels = { 0, 1, 1, 2, 0, 1 };

    private static UiMarkupListTreeShape Shape(int[]? levels = null, int count = 6)
    {
        var shape = new UiMarkupListTreeShape();
        shape.Load(levels ?? Levels, count);
        return shape;
    }

    private static int[] Order(UiMarkupListViewMap map) =>
        Enumerable.Range(0, map.ViewCount).Select(map.PluginRow).ToArray();

    [Fact]
    public void Load_ClampsLevels()
    {
        var shape = Shape(new[] { 2, 3, 1, -4, 9 }, 6);
        Assert.Equal(new[] { 0, 1, 1, 0, 1, 0 }, Enumerable.Range(0, 6).Select(shape.Level).ToArray());
    }

    [Fact]
    public void Load_DerivesParentsChildrenAndSubtrees()
    {
        var shape = Shape();
        Assert.Equal(new[] { -1, 0, 0, 2, -1, 4 }, Enumerable.Range(0, 6).Select(shape.Parent).ToArray());
        Assert.Equal(new[] { true, false, true, false, true, false },
            Enumerable.Range(0, 6).Select(shape.HasChildren).ToArray());
        Assert.Equal(4, shape.SubtreeEnd(0));
        Assert.Equal(4, shape.SubtreeEnd(2));
        Assert.Equal(6, shape.SubtreeEnd(4));
        Assert.Equal(2, shape.SubtreeEnd(1));
    }

    [Fact]
    public void Load_ClearsCollapsedFlags()
    {
        var shape = Shape();
        shape.SetCollapsed(0, true);
        shape.Load(Levels, 6);
        Assert.False(shape.IsCollapsed(0));
    }

    [Fact]
    public void Identity_LeavesOutCollapsedDescendants()
    {
        var shape = Shape();
        var map = new UiMarkupListViewMap();
        map.SetIdentity(6, shape);
        Assert.Equal(new[] { 0, 1, 2, 3, 4, 5 }, Order(map));

        shape.SetCollapsed(0, true);
        map.SetIdentity(6, shape);
        Assert.Equal(new[] { 0, 4, 5 }, Order(map));
        Assert.Equal((3, 6), (map.ViewCount, map.PluginCount));
        Assert.Equal(-1, map.ViewRow(1));
        Assert.Equal(-1, map.ViewRow(3));
        Assert.Equal(1, map.ViewRow(4));

        shape.SetCollapsed(0, false);
        shape.SetCollapsed(2, true);
        map.SetIdentity(6, shape);
        Assert.Equal(new[] { 0, 1, 2, 4, 5 }, Order(map));
    }

    [Fact]
    public void Sort_SortsSiblingsAndKeepsChildrenUnderTheirParent()
    {
        var map = new UiMarkupListViewMap();
        string?[] names = { "A", "A1", "A2", "A2a", "B", "B1" };
        map.SortByText(names, 6, descending: true, Shape());
        Assert.Equal(new[] { 4, 5, 0, 2, 3, 1 }, Order(map));
        map.SortByText(names, 6, descending: false, Shape());
        Assert.Equal(new[] { 0, 1, 2, 3, 4, 5 }, Order(map));
    }

    [Fact]
    public void Sort_IsStableAndPutsMissingKeysLastWithinSiblings()
    {
        var map = new UiMarkupListViewMap();
        // Children of A (rows 1, 2, 3 at level 1): keys NaN, 5, 5.
        int[] levels = { 0, 1, 1, 1 };
        map.SortByNumber(new[] { 0.0, double.NaN, 5.0, 5.0 }, 4, descending: true, Shape(levels, 4));
        Assert.Equal(new[] { 0, 2, 3, 1 }, Order(map));
    }

    [Fact]
    public void Sort_RespectsCollapsedRows()
    {
        var shape = Shape();
        shape.SetCollapsed(4, true);
        var map = new UiMarkupListViewMap();
        map.SortByText(new string?[] { "A", "A1", "A2", "A2a", "B", "B1" }, 6, descending: true, shape);
        Assert.Equal(new[] { 4, 0, 2, 3, 1 }, Order(map));
        Assert.Equal(-1, map.ViewRow(5));
    }

    [Fact]
    public void DeepChain_ArrangesWithoutRecursion()
    {
        int[] levels = Enumerable.Range(0, 2000).ToArray();
        var map = new UiMarkupListViewMap();
        map.SetIdentity(2000, Shape(levels, 2000));
        Assert.Equal(Enumerable.Range(0, 2000).ToArray(), Order(map));
    }
}
```

- [ ] **Step 2: Run, expect a compile failure** (`UiMarkupListTreeShape` not defined).

- [ ] **Step 3: Tree shape** — create `src/AcDream.App/UI/UiMarkupListTreeShape.cs`:

```csharp
namespace AcDream.App.UI;

/// <summary>
/// A tree list's shape for one frame. The plugin's rows are the tree
/// flattened in pre-order, each with a depth; a row's descendants are the
/// rows straight after it that are deeper. Levels are clamped as they load: a
/// negative level, or a row past the end of the levels, is level 0, and a row
/// more than one deeper than the row before it is one deeper.
/// </summary>
internal sealed class UiMarkupListTreeShape
{
    private int[] _levels = Array.Empty<int>();
    private int[] _parents = Array.Empty<int>();
    private bool[] _collapsed = Array.Empty<bool>();
    private int[] _path = Array.Empty<int>();

    public int Count { get; private set; }

    /// <summary>Loads <paramref name="count"/> rows' levels; every row starts expanded.</summary>
    public void Load(IReadOnlyList<int> levels, int count)
    {
        if (_levels.Length < count)
        {
            _levels = new int[count];
            _parents = new int[count];
            _collapsed = new bool[count];
            _path = new int[count + 1];
        }
        else
        {
            Array.Clear(_collapsed, 0, count);
        }
        int previous = -1;
        for (int row = 0; row < count; row++)
        {
            int level = row < levels.Count ? Math.Max(0, levels[row]) : 0;
            level = Math.Min(level, previous + 1);
            _levels[row] = level;
            _parents[row] = level == 0 ? -1 : _path[level - 1];
            _path[level] = row;
            previous = level;
        }
        Count = count;
    }

    public int Level(int row) => (uint)row < (uint)Count ? _levels[row] : 0;

    /// <summary>The row's parent; −1 for a top-level row.</summary>
    public int Parent(int row) => (uint)row < (uint)Count ? _parents[row] : -1;

    public bool HasChildren(int row) =>
        (uint)row < (uint)Count && row + 1 < Count && _levels[row + 1] > _levels[row];

    /// <summary>The first row after <paramref name="row"/>'s descendants.</summary>
    public int SubtreeEnd(int row)
    {
        int end = row + 1;
        while (end < Count && _levels[end] > _levels[row])
            end++;
        return end;
    }

    public bool IsCollapsed(int row) => (uint)row < (uint)Count && _collapsed[row];

    public void SetCollapsed(int row, bool collapsed)
    {
        if ((uint)row < (uint)Count)
            _collapsed[row] = collapsed;
    }
}
```

- [ ] **Step 4: Tree arrangement** — in `UiMarkupListViewMap.cs`:
  - Update the class summary to add: "With a tree shape it shows the rows in pre-order, sorts each set of siblings, and leaves out the descendants of collapsed rows."
  - Change the three public methods and `Apply`:

```csharp
    public void SetIdentity(int count, UiMarkupListTreeShape? tree = null)
    {
        if (tree is not null)
        {
            ArrangeTree(tree, order: null);
            return;
        }
        _identity = true;
        ViewCount = PluginCount = count;
    }

    /// <summary>Sorts by <paramref name="keys"/>[plugin row]; a null key sorts last. A tree sorts each set of siblings.</summary>
    public void SortByText(string?[] keys, int count, bool descending, UiMarkupListTreeShape? tree = null)
    {
        _comparer.Texts = keys;
        _comparer.Numbers = null;
        _comparer.Descending = descending;
        Apply(count, tree);
    }

    /// <summary>Sorts by <paramref name="keys"/>[plugin row]; a NaN key sorts last. A tree sorts each set of siblings.</summary>
    public void SortByNumber(double[] keys, int count, bool descending, UiMarkupListTreeShape? tree = null)
    {
        _comparer.Texts = null;
        _comparer.Numbers = keys;
        _comparer.Descending = descending;
        Apply(count, tree);
    }

    private void Apply(int count, UiMarkupListTreeShape? tree)
    {
        if (tree is not null)
        {
            ArrangeTree(tree, _comparer);
            _comparer.Texts = null;
            _comparer.Numbers = null;
            return;
        }
        // … the existing body of Apply, unchanged, ending with ViewCount = PluginCount = count; …
    }
```

  Add the fields and `ArrangeTree`:

```csharp
    // Tree arrangement scratch: each parent's children grouped by a counting
    // sort (group g = parent + 1, so the top level is group 0), and an
    // explicit depth-first stack so a deep tree can't overflow the call stack.
    private int[] _groupEnd = Array.Empty<int>();
    private int[] _children = Array.Empty<int>();
    private int[] _stackNext = Array.Empty<int>();
    private int[] _stackEnd = Array.Empty<int>();

    private int GroupStart(int group) => group == 0 ? 0 : _groupEnd[group - 1];

    private void ArrangeTree(UiMarkupListTreeShape tree, IComparer<int>? order)
    {
        int count = tree.Count;
        if (_viewToPlugin.Length < count)
            _viewToPlugin = new int[count];
        if (_pluginToView.Length < count)
            _pluginToView = new int[count];
        if (_children.Length < count)
            _children = new int[count];
        if (_groupEnd.Length < count + 2)
            _groupEnd = new int[count + 2];
        if (_stackNext.Length < count + 1)
        {
            _stackNext = new int[count + 1];
            _stackEnd = new int[count + 1];
        }

        // Count each group's children at [group + 1], prefix-sum to starts,
        // then place rows in plugin order; each start ends as its group's end.
        Array.Clear(_groupEnd, 0, count + 2);
        for (int row = 0; row < count; row++)
            _groupEnd[tree.Parent(row) + 2]++;
        for (int g = 1; g < count + 2; g++)
            _groupEnd[g] += _groupEnd[g - 1];
        for (int row = 0; row < count; row++)
            _children[_groupEnd[tree.Parent(row) + 1]++] = row;

        if (order is not null)
        {
            for (int g = 0; g <= count; g++)
            {
                int start = GroupStart(g), length = _groupEnd[g] - start;
                if (length > 1)
                    Array.Sort(_children, start, length, order);
            }
        }

        Array.Fill(_pluginToView, -1, 0, count);
        int shown = 0, depth = 0;
        _stackNext[0] = 0;
        _stackEnd[0] = _groupEnd[0];
        while (depth >= 0)
        {
            if (_stackNext[depth] == _stackEnd[depth])
            {
                depth--;
                continue;
            }
            int row = _children[_stackNext[depth]++];
            _viewToPlugin[shown] = row;
            _pluginToView[row] = shown++;
            int group = row + 1;
            if (!tree.IsCollapsed(row) && GroupStart(group) < _groupEnd[group])
            {
                depth++;
                _stackNext[depth] = GroupStart(group);
                _stackEnd[depth] = _groupEnd[group];
            }
        }
        _identity = false;
        ViewCount = shown;
        PluginCount = count;
    }
```

  (`KeyComparer` already breaks ties by plugin row, so sibling sorts are stable.)

- [ ] **Step 5: Run the Verify command** → all pass.

- [ ] **Step 6: Commit**

```bash
git add src/AcDream.App/UI/UiMarkupListTreeShape.cs src/AcDream.App/UI/UiMarkupListViewMap.cs \
        tests/AcDream.App.Tests/UI/UiMarkupListTreeShapeTests.cs
git commit -m "feat(ui): tree shape and pre-order tree arrangement for markup lists

Claude-Session: https://claude.ai/code/session_014SAojwkxRKxrwp1RMDGPVC"
```

---

### Task 3: Binding helpers for expansion

**Goal:** Builders can bind a required string-set reader and an optional `Action<string, bool>`.

**Files:**
- Modify: `src/AcDream.App/UI/Markup/MarkupBindings.cs`
- Test: `tests/AcDream.App.Tests/UI/MarkupTreeBindingsTests.cs` (new)

**Acceptance Criteria:**
- [ ] `BindRequiredStringSetReader` returns an `IReadOnlySet<string>` property's set itself. It copies any other `IEnumerable<string>` into an ordinal `HashSet<string>` on each read, and reads null as empty. It throws `FormatException` naming the context for absent, literal, missing or wrongly typed (`int`, `IReadOnlyList<int>`) bindings.
- [ ] `BindStringBoolAction` returns null when absent and invokes a bound `Action<string, bool>`. It throws naming the context and `Action<string, bool>` for a literal, a missing property or an `Action<int>`.

**Verify:** `dotnet test tests/AcDream.App.Tests/AcDream.App.Tests.csproj --filter "FullyQualifiedName~MarkupTreeBindingsTests"` → all pass.

**Steps:**

- [ ] **Step 1: Write the failing tests** — create `tests/AcDream.App.Tests/UI/MarkupTreeBindingsTests.cs`:

```csharp
using System;
using System.Collections.Generic;
using AcDream.App.UI.Markup;

namespace AcDream.App.Tests.UI;

public sealed class MarkupTreeBindingsTests
{
    private sealed class Binding
    {
        public HashSet<string> Set { get; set; } = new() { "a" };
        public List<string> List { get; } = new() { "b", "b" };
        public List<string>? Nothing { get; set; }
        public int One { get; } = 1;
        public IReadOnlyList<int> Ints { get; } = new[] { 1 };
        public (string, bool)? Last { get; private set; }
        public Action<string, bool> SetExpand => (k, v) => Last = (k, v);
        public Action<int> Wrong => _ => { };
    }

    [Fact]
    public void StringSetReader_ReadsALiveSetWithoutCopying()
    {
        var b = new Binding();
        var read = MarkupBindings.BindRequiredStringSetReader("{Set}", b, "list expanded");
        Assert.Same(b.Set, read());
        b.Set = new HashSet<string> { "z" };
        Assert.Contains("z", read());
    }

    [Fact]
    public void StringSetReader_CopiesOtherEnumerablesAndReadsNullAsEmpty()
    {
        var b = new Binding();
        var set = MarkupBindings.BindRequiredStringSetReader("{List}", b, "list expanded")();
        Assert.Single(set);
        Assert.Contains("b", set);
        Assert.False(set.Contains("B"));   // ordinal
        Assert.Empty(MarkupBindings.BindRequiredStringSetReader("{Nothing}", b, "list expanded")());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("a")]
    [InlineData("{Missing}")]
    [InlineData("{One}")]
    [InlineData("{Ints}")]
    public void StringSetReader_ThrowsNamingContext(string? expression)
    {
        var ex = Assert.Throws<FormatException>(() =>
            MarkupBindings.BindRequiredStringSetReader(expression, new Binding(), "list expanded"));
        Assert.Contains("list expanded", ex.Message);
    }

    [Fact]
    public void StringBoolAction_MissingIsNullAndBoundInvokes()
    {
        var b = new Binding();
        Assert.Null(MarkupBindings.BindStringBoolAction(null, b, "list onexpand"));
        MarkupBindings.BindStringBoolAction("{SetExpand}", b, "list onexpand")!("k", true);
        Assert.Equal(("k", true), b.Last);
    }

    [Theory]
    [InlineData("SetExpand")]
    [InlineData("{Missing}")]
    [InlineData("{Wrong}")]
    public void StringBoolAction_ThrowsNamingContextAndType(string expression)
    {
        var ex = Assert.Throws<FormatException>(() =>
            MarkupBindings.BindStringBoolAction(expression, new Binding(), "list onexpand"));
        Assert.Contains("list onexpand", ex.Message);
        Assert.Contains("Action<string, bool>", ex.Message);
    }
}
```

- [ ] **Step 2: Run, expect a compile failure.**

- [ ] **Step 3: Implement** — in `MarkupBindings.cs`, after `BindIntListAction`, add:

```csharp
    private static readonly IReadOnlySet<string> NoStrings = new HashSet<string>(StringComparer.Ordinal);

    /// <summary>
    /// A required set of strings, read on each call: the plugin's set itself
    /// when it is an <c>IReadOnlySet&lt;string&gt;</c>, otherwise any
    /// <c>IEnumerable&lt;string&gt;</c> copied into an ordinal set. Null reads as empty.
    /// </summary>
    public static Func<IReadOnlySet<string>> BindRequiredStringSetReader(
        string? expression, object binding, string context)
    {
        if (string.IsNullOrWhiteSpace(expression) || !MarkupDocument.IsBinding(expression))
            throw new FormatException($"{context} must be a string-set binding");
        PropertyInfo? property = binding.GetType().GetProperty(expression[1..^1]);
        if (property is null || !typeof(IEnumerable<string>).IsAssignableFrom(property.PropertyType))
        {
            throw new FormatException(
                $"{expression} did not resolve to an IEnumerable<string> property on "
                + binding.GetType().Name + $" ({context})");
        }
        return () => property.GetValue(binding) switch
        {
            IReadOnlySet<string> set => set,
            IEnumerable<string> values => new HashSet<string>(values, StringComparer.Ordinal),
            _ => NoStrings,
        };
    }

    /// <summary>An optional <c>Action&lt;string, bool&gt;</c>; null when the attribute is absent.</summary>
    public static Action<string, bool>? BindStringBoolAction(string? attribute, object binding, string context)
    {
        if (attribute is null)
            return null;
        if (!MarkupDocument.IsBinding(attribute))
            throw new FormatException($"{context} must be an Action<string, bool> binding");
        PropertyInfo? property = binding.GetType().GetProperty(attribute[1..^1]);
        if (property is null || !typeof(Action<string, bool>).IsAssignableFrom(property.PropertyType))
        {
            throw new FormatException(
                $"{attribute} did not resolve to an Action<string, bool> property on "
                + binding.GetType().Name + $" ({context})");
        }
        return (key, value) => (property.GetValue(binding) as Action<string, bool>)?.Invoke(key, value);
    }
```

- [ ] **Step 4: Run the Verify command** → all pass.

- [ ] **Step 5: Commit**

```bash
git add src/AcDream.App/UI/Markup/MarkupBindings.cs tests/AcDream.App.Tests/UI/MarkupTreeBindingsTests.cs
git commit -m "feat(ui): string-set and string/bool binding helpers for list trees

Claude-Session: https://claude.ai/code/session_014SAojwkxRKxrwp1RMDGPVC"
```

---

### Task 4: `tree="true"` builder, expansion state and drawing

**Goal:** `<list tree="true">` builds with its bindings. Each frame it reads levels, keys and expansion (bound, or unbound with remembered keys), shows only expanded rows in tree order (sorted by siblings), and draws the indent and arrows.

**Files:**
- Create: `src/AcDream.App/UI/UiMarkupList.Tree.cs`
- Create: `src/AcDream.App/UI/Markup/MarkupListTree.cs`
- Modify: `src/AcDream.App/UI/UiMarkupList.Sort.cs` (`SortView`)
- Modify: `src/AcDream.App/UI/UiMarkupList.cs` (`DrawColumns` text case)
- Modify: `src/AcDream.App/UI/MarkupDocument.cs` (list case)
- Test: `tests/AcDream.App.Tests/UI/MarkupListTreeTests.cs` (new)

**Acceptance Criteria:**
- [ ] Build errors (`FormatException`):
  - a tree with the single-column form, naming `<column>`;
  - no text column, naming `text column`;
  - `levels` absent or a string list, naming `list levels`;
  - `keys` absent, naming `list keys`;
  - `expanded` mistyped, naming `list expanded`;
  - `onexpand` mistyped, naming `list onexpand`;
  - `expanded` together with `collapsed`, naming `collapsed`;
  - any tree attribute without `tree="true"`, naming `tree`.
- [ ] Expansion:
  - by default every row shows, in pre-order;
  - `collapsed="true"` shows only top-level rows;
  - bound `expanded` is read every frame;
  - rows past `keys` use `"#<row>"`, and rows past `levels` are top level.
- [ ] Remembered (unbound, named):
  - a stored JSON array of keys flips those keys from the default, in both default modes;
  - a malformed stored value counts as absent;
  - an unnamed tree never touches the state.
- [ ] Sorting with a header sorts siblings and keeps children under their parent: descending Name gives view order 4, 5, 0, 2, 3, 1.
- [ ] Clicks report plugin rows under collapse. Scrolling and the visible range use the shown count: with 2 rows shown in 2 visible rows, the wheel doesn't scroll.
- [ ] Drawing: each parent row draws a 4-quad arrow in `TextColor`.
  - Expanded (▾): 1 px tall rows, the first at (slot + 2, row top + 6).
  - Collapsed (▸): 1 px wide columns, the first at (slot + 3, row top + 5).
  - The slot is `cellX + Padding + level × 12`.
  - Leaf rows draw no arrow.

**Verify:** `dotnet test tests/AcDream.App.Tests/AcDream.App.Tests.csproj --filter "FullyQualifiedName~MarkupListTreeTests|FullyQualifiedName~MarkupList|FullyQualifiedName~ListButtonColumn|FullyQualifiedName~UiMarkupList"` → all pass.

**Steps:**

- [ ] **Step 1: Write the failing tests** — create `tests/AcDream.App.Tests/UI/MarkupListTreeTests.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using AcDream.App.UI;
using AcDream.App.UI.Markup;
using Silk.NET.Input;

namespace AcDream.App.Tests.UI;

public sealed class MarkupListTreeTests
{
    private sealed class Binding
    {
        public List<string> Names { get; set; } = new() { "A", "A1", "A2", "A2a", "B", "B1" };
        public List<int> Levels { get; set; } = new() { 0, 1, 1, 2, 0, 1 };
        public List<string> Ids { get; set; } = new() { "a", "a1", "a2", "a2a", "b", "b1" };
        public List<bool> On { get; set; } = new() { false, false, false, false, false, false };
        public HashSet<string> Expanded { get; set; } = new();
        public List<(string Key, bool Expanded)> Expands { get; } = new();
        public Action<string, bool> NotifyExpand => (k, v) => Expands.Add((k, v));
        public Action<int> WrongExpand => _ => { };
        public int Selected { get; set; } = -1;
        public List<int> Picks { get; } = new();
        public Action<int> Pick => row => { Selected = row; Picks.Add(row); };
        public Action<int> Toggle => _ => { };
        public List<int> Selection { get; set; } = new();
        public List<IReadOnlyList<int>> Changes { get; } = new();
        public Action<IReadOnlyList<int>> SetSelection => rows => { Selection = rows.ToList(); Changes.Add(rows); };
        public int SortCol { get; set; } = -1;
        public bool SortDesc { get; set; }
    }

    // Checkbox 0-20, tree column 20-300 (the first text column, not the first column).
    // No header: view row v spans y 18v .. 18v+18. Arrow slot for level L: x 23+12L .. 35+12L.
    private const string ColumnsXml = """
          <column type="checkbox" width="20" values="{On}" onchange="{Toggle}"/>
          <column type="text" items="{Names}"/>
        """;

    private const string SortColumnsXml = """
          <column type="checkbox" header="" width="20" values="{On}" onchange="{Toggle}"/>
          <column type="text" header="Name" items="{Names}" sort="text"/>
        """;

    private const string Tree = "tree=\"true\" levels=\"{Levels}\" keys=\"{Ids}\" selected=\"{Selected}\" onchange=\"{Pick}\"";
    private const string MultiTree = "tree=\"true\" levels=\"{Levels}\" keys=\"{Ids}\" selectmode=\"multi\" selected=\"{Selection}\" onchange=\"{SetSelection}\"";

    private const int TextX = 150;

    private static int RowY(int view, bool header = false) => (header ? 18 : 0) + 9 + 18 * view;

    private static int ArrowX(int level) => 29 + 12 * level;

    private static string ListXml(string attributes, string columns = ColumnsXml, string? name = "rules", int height = 200)
    {
        string named = name is null ? "" : $"name=\"{name}\"";
        return $"""
              <list {named} x="0" y="0" w="300" h="{height}" {attributes}>
            {columns}
              </list>
            """;
    }

    private sealed class Mounted
    {
        public required UiRoot Root { get; init; }
        public required UiMarkupList List { get; init; }
        public UiSelectModifiers Keys { get; set; }
    }

    private static UiPanel BuildPanel(string listXml, Binding b, IMarkupControlState? state = null) =>
        MarkupDocument.Build($"""
            <panel x="0" y="0" w="400" h="300">
            {listXml}
            </panel>
            """, b, id => (id, 16, 16), controlState: state);

    private static Mounted Mount(Binding b, string attributes = Tree, string? listXml = null, IMarkupControlState? state = null)
    {
        var panel = BuildPanel(listXml ?? ListXml(attributes), b, state);
        var root = new UiRoot { Width = 800, Height = 600 };
        root.AddChild(panel);
        var list = panel.Children.OfType<UiMarkupList>().Single();
        var m = new Mounted { Root = root, List = list };
        list.ModifiersSource = () => m.Keys;
        Redraw(list);                                   // lists read rows and lay out at draw
        return m;
    }

    private static void Redraw(UiMarkupList list) => MarkupDrawProbe.Draw(list.Parent!, 800f, 600f);

    private static void Click(Mounted m, int x, int y, UiSelectModifiers keys = default)
    {
        m.Keys = keys;
        m.Root.OnMouseDown(UiMouseButton.Left, x, y);
        m.Root.OnMouseUp(UiMouseButton.Left, x, y);
        Redraw(m.List);
    }

    /// <summary>The plugin row shown at each view row, by clicking each row's text until a click picks nothing.</summary>
    private static int[] ViewOrder(Mounted m, Binding b, bool header = false)
    {
        var order = new List<int>();
        for (int view = 0; view < 10; view++)
        {
            b.Picks.Clear();
            Click(m, TextX, RowY(view, header));
            if (b.Picks.Count == 0)
                break;
            order.Add(Assert.Single(b.Picks));
        }
        return order.ToArray();
    }

    private static IMarkupControlState NewState() =>
        new MarkupControlStateStore(null, () => "alice").ForWindow("win");

    /// <summary>Records every call, so a test can prove the state was never touched.</summary>
    private sealed class RecordingState : IMarkupControlState
    {
        public int Calls { get; private set; }
        public int Generation { get { Calls++; return 1; } }
        public string? Get(string control, string kind) { Calls++; return null; }
        public void Set(string control, string kind, string value) => Calls++;
        public void Remove(string control, string kind) => Calls++;
    }

    [Fact]
    public void Default_ShowsEveryRowExpanded()
    {
        var b = new Binding();
        Assert.Equal(new[] { 0, 1, 2, 3, 4, 5 }, ViewOrder(Mount(b), b));
    }

    [Fact]
    public void CollapsedAttribute_StartsWithOnlyTopLevelRows()
    {
        var b = new Binding();
        Assert.Equal(new[] { 0, 4 }, ViewOrder(Mount(b, Tree + " collapsed=\"true\""), b));
    }

    [Fact]
    public void BoundExpanded_IsReadEveryFrame()
    {
        var b = new Binding { Expanded = new() { "a" } };
        var m = Mount(b, Tree + " expanded=\"{Expanded}\"");
        Assert.Equal(new[] { 0, 1, 2, 4 }, ViewOrder(m, b));
        b.Expanded = new() { "a", "a2" };
        Redraw(m.List);
        Assert.Equal(new[] { 0, 1, 2, 3, 4 }, ViewOrder(m, b));
    }

    [Fact]
    public void RowsPastTheKeys_UseHashRowKeys()
    {
        var b = new Binding { Ids = new() { "a" }, Expanded = new() { "a", "#2" } };
        Assert.Equal(new[] { 0, 1, 2, 3, 4 }, ViewOrder(Mount(b, Tree + " expanded=\"{Expanded}\""), b));
    }

    [Fact]
    public void RowsPastTheLevels_AreTopLevel()
    {
        var b = new Binding { Levels = new() { 0, 1 } };   // rows 2..5 are level 0; row 0 has child 1
        Assert.Equal(new[] { 0, 2, 3, 4, 5 }, ViewOrder(Mount(b, Tree + " collapsed=\"true\""), b));
    }

    [Fact]
    public void Remembered_KeysFlipTheDefault()
    {
        var state = NewState();
        state.Set("rules", "expanded", "[\"a\"]");
        var b = new Binding();
        Assert.Equal(new[] { 0, 4, 5 }, ViewOrder(Mount(b, state: state), b));

        var b2 = new Binding();
        Assert.Equal(new[] { 0, 1, 2, 4 }, ViewOrder(Mount(b2, Tree + " collapsed=\"true\"", state: state), b2));
    }

    [Fact]
    public void Remembered_MalformedValueIsIgnored()
    {
        var state = NewState();
        state.Set("rules", "expanded", "{");
        var b = new Binding();
        Assert.Equal(new[] { 0, 1, 2, 3, 4, 5 }, ViewOrder(Mount(b, state: state), b));
    }

    [Fact]
    public void UnnamedTree_NeverTouchesTheState()
    {
        var state = new RecordingState();
        var b = new Binding();
        var m = Mount(b, listXml: ListXml(Tree, name: null), state: state);
        ViewOrder(m, b);
        Assert.Equal(0, state.Calls);
    }

    [Fact]
    public void Sorting_SortsSiblingsAndKeepsChildrenUnderTheirParent()
    {
        var b = new Binding { SortCol = 1, SortDesc = true };
        var m = Mount(b, listXml: ListXml(
            Tree + " header=\"true\" sortcolumn=\"{SortCol}\" sortdescending=\"{SortDesc}\"", SortColumnsXml));
        Assert.Equal(new[] { 4, 5, 0, 2, 3, 1 }, ViewOrder(m, b, header: true));
    }

    [Fact]
    public void Clicks_ReportPluginRowsUnderCollapse()
    {
        var b = new Binding { Expanded = new() { "b" } };
        var m = Mount(b, Tree + " expanded=\"{Expanded}\"");   // shown: 0, 4, 5
        Click(m, TextX, RowY(1));
        Assert.Equal(4, b.Selected);
    }

    [Fact]
    public void Scrolling_UsesTheRowsShown()
    {
        var b = new Binding();
        var m = Mount(b, listXml: ListXml(Tree + " collapsed=\"true\"", height: 36));   // 2 visible rows, 2 shown
        m.Root.OnMouseMove(TextX, 9);
        m.Root.OnScroll(-1);
        Redraw(m.List);
        Click(m, TextX, RowY(0));
        Assert.Equal(0, b.Selected);
    }

    [Fact]
    public void Draw_ExpandedArrowsOnParentsOnly()
    {
        var b = new Binding();
        var m = Mount(b);
        m.List.TextColor = new Vector4(0.1f, 0.9f, 0.2f, 1f);   // unmistakable
        var quads = MarkupDrawProbe.QuadBounds(MarkupDrawProbe.Draw(m.List), m.List.TextColor);
        Assert.Equal(12, quads.Count);                          // ▾ on rows 0, 2, 4
        Assert.All(quads, q => Assert.Equal(1f, q.H));
        Assert.Contains(quads, q => q.X == 25f && q.Y == 6f);   // row 0, level 0
        Assert.Contains(quads, q => q.X == 37f && q.Y == 42f);  // row 2, level 1, view 2
        Assert.Contains(quads, q => q.X == 25f && q.Y == 78f);  // row 4, view 4
    }

    [Fact]
    public void Draw_CollapsedArrows()
    {
        var b = new Binding();
        var m = Mount(b, Tree + " collapsed=\"true\"");
        m.List.TextColor = new Vector4(0.1f, 0.9f, 0.2f, 1f);
        var quads = MarkupDrawProbe.QuadBounds(MarkupDrawProbe.Draw(m.List), m.List.TextColor);
        Assert.Equal(8, quads.Count);                           // ▸ on rows 0 and 4
        Assert.All(quads, q => Assert.Equal(1f, q.W));
        Assert.Contains(quads, q => q.X == 26f && q.Y == 5f);
        Assert.Contains(quads, q => q.X == 26f && q.Y == 23f);
    }

    [Fact]
    public void BuildErrors()
    {
        var b = new Binding();
        void Throws(string listXml, string expected) =>
            Assert.Contains(expected, Assert.Throws<FormatException>(() => BuildPanel(listXml, b)).Message);

        Throws("""<list x="0" y="0" w="300" h="200" items="{Names}" selected="{Selected}" tree="true" levels="{Levels}" keys="{Ids}"/>""",
            "<column>");
        Throws(ListXml(Tree, """<column type="checkbox" values="{On}" onchange="{Toggle}"/>"""), "text column");
        Throws(ListXml("tree=\"true\" keys=\"{Ids}\" selected=\"{Selected}\""), "list levels");
        Throws(ListXml("tree=\"true\" levels=\"{Names}\" keys=\"{Ids}\" selected=\"{Selected}\""), "list levels");
        Throws(ListXml("tree=\"true\" levels=\"{Levels}\" selected=\"{Selected}\""), "list keys");
        Throws(ListXml(Tree + " expanded=\"{Levels}\""), "list expanded");
        Throws(ListXml(Tree + " onexpand=\"{WrongExpand}\""), "list onexpand");
        Throws(ListXml(Tree + " expanded=\"{Expanded}\" collapsed=\"true\""), "collapsed");
        Throws(ListXml("levels=\"{Levels}\" selected=\"{Selected}\""), "tree");
    }
}
```

`MultiTree`, `NotifyExpand`, `Selection` and `Changes` are used by Task 5's tests in this file.

- [ ] **Step 2: Run, expect a compile failure.**

- [ ] **Step 3: Widget partial** — create `src/AcDream.App/UI/UiMarkupList.Tree.cs`:

```csharp
using System.Globalization;
using System.Text.Json;
using AcDream.App.UI.Markup;

namespace AcDream.App.UI;

public sealed partial class UiMarkupList
{
    /// <summary>Indent per tree level, and the width of a row's arrow slot.</summary>
    public const float TreeIndent = 12f;

    /// <summary>The column the tree draws in (its first text column); −1 for a flat list.</summary>
    public int TreeColumn { get; set; } = -1;

    public bool IsTree => TreeColumn >= 0;

    /// <summary>Each plugin row's depth, 0 = top level.</summary>
    public Func<IReadOnlyList<int>>? TreeLevelsSource { get; set; }

    /// <summary>Each plugin row's stable key; a row past the end is "#&lt;row&gt;".</summary>
    public Func<IReadOnlyList<string>>? TreeKeysSource { get; set; }

    /// <summary>Bound expansion: the keys of expanded rows. When set, the plugin owns expansion.</summary>
    public Func<IReadOnlySet<string>>? ExpandedSource { get; set; }

    /// <summary>Called with a row's key and the state a user action asks for.</summary>
    public Action<string, bool>? ExpansionChanged { get; set; }

    /// <summary>Unbound: every row starts collapsed instead of expanded (<c>collapsed="true"</c>).</summary>
    public bool StartCollapsed { get; set; }

    internal RememberedValue? RememberedExpansion { get; set; }

    private readonly UiMarkupListTreeShape _tree = new();
    private IReadOnlyList<string> _treeKeys = Array.Empty<string>();

    // Unbound: the keys whose state differs from the default, as remembered.
    private HashSet<string>? _hostToggled;

    private string TreeKey(int row) =>
        row < _treeKeys.Count ? _treeKeys[row] : "#" + row.ToString(CultureInfo.InvariantCulture);

    private HashSet<string> HostToggled
    {
        get
        {
            if (RememberedExpansion is { } remembered && remembered.TryRefresh(out string? text))
                _hostToggled = DecodeKeys(text);
            return _hostToggled ??= new HashSet<string>(StringComparer.Ordinal);
        }
    }

    /// <summary>Reads the tree's levels, keys and expansion for this frame; null for a flat list.</summary>
    private UiMarkupListTreeShape? LoadTree(int rowCount)
    {
        if (!IsTree)
            return null;
        _tree.Load(TreeLevelsSource!(), rowCount);
        _treeKeys = TreeKeysSource!();
        IReadOnlySet<string>? bound = ExpandedSource?.Invoke();
        HashSet<string>? toggled = bound is null ? HostToggled : null;
        for (int row = 0; row < rowCount; row++)
        {
            if (!_tree.HasChildren(row))
                continue;
            string key = TreeKey(row);
            bool expanded = bound?.Contains(key) ?? toggled!.Contains(key) != StartCollapsed;
            _tree.SetCollapsed(row, !expanded);
        }
        return _tree;
    }

    private static HashSet<string> DecodeKeys(string? text)
    {
        var keys = new HashSet<string>(StringComparer.Ordinal);
        if (text is null)
            return keys;
        try
        {
            if (JsonSerializer.Deserialize<string?[]>(text) is { } values)
            {
                foreach (string? value in values)
                {
                    if (value is not null)
                        keys.Add(value);
                }
            }
        }
        catch (JsonException)
        {
        }
        return keys;
    }

    private static string EncodeKeys(HashSet<string> keys) =>
        JsonSerializer.Serialize(keys.Order(StringComparer.Ordinal).ToArray());

    /// <summary>The tree column's cell: indent, ▸/▾ for a parent, then the text after the arrow slot.</summary>
    private void DrawTreeCell(
        UiRenderContext context, IReadOnlyList<string>? texts, IReadOnlyList<uint>? colors,
        int row, float cellX, float y)
    {
        float slotX = TreeSlotX(row, cellX);
        if (_tree.HasChildren(row))
            DrawTreeArrow(context, slotX, y, expanded: !_tree.IsCollapsed(row));
        DrawTextCell(context, texts, colors, row, slotX + TreeIndent - Padding, y);
    }

    private float TreeSlotX(int row, float cellX) => cellX + Padding + _tree.Level(row) * TreeIndent;

    /// <summary>▾ (expanded: four rows narrowing) or ▸ (collapsed: four columns narrowing) in the arrow slot.</summary>
    private void DrawTreeArrow(UiRenderContext context, float slotX, float rowY, bool expanded)
    {
        float top = rowY + MathF.Floor((RowHeight - 7f) * 0.5f);
        for (int i = 0; i < 4; i++)
        {
            float length = 7f - 2f * i;
            if (expanded)
                context.DrawFill(slotX + 2f + i, top + 1f + i, length, 1f, TextColor);
            else
                context.DrawFill(slotX + 3f + i, top + i, 1f, length, TextColor);
        }
    }
}
```

  `EncodeKeys` is used by Task 5's toggle. If the compiler reports it unused (IDE0051 or similar shown as a warning), leave it; Task 5 calls it. If it breaks the Release build's 0-warnings rule, add Task 5's `ToggleExpanded` stub instead, and say so in the report.

- [ ] **Step 4: Arrange with the tree** — in `UiMarkupList.Sort.cs` `SortView`:
  - Add `UiMarkupListTreeShape? tree = LoadTree(rowCount);` as the first statement.
  - Pass `tree` to every view map call: `_view.SetIdentity(rowCount, tree)`, `_view.SortByNumber(_numberKeys, rowCount, descending, tree)` (three places) and `_view.SortByText(_textKeys, rowCount, descending, tree)`.

- [ ] **Step 5: Draw the tree column** — in `UiMarkupList.cs` `DrawColumns`, change the text case to:

```csharp
                        case UiMarkupListColumnKind.Text:
                            if (c == TreeColumn)
                                DrawTreeCell(context, _cachedTextRows[c], _cachedColorRows[c], row, cellX, y);
                            else
                                DrawTextCell(context, _cachedTextRows[c], _cachedColorRows[c], row, cellX, y);
                            break;
```

- [ ] **Step 6: Builder** — create `src/AcDream.App/UI/Markup/MarkupListTree.cs`:

```csharp
using System.Xml.Linq;

namespace AcDream.App.UI.Markup;

/// <summary>A tree list's attributes: <c>tree levels keys expanded onexpand collapsed</c>.</summary>
internal static class MarkupListTree
{
    private static readonly string[] TreeAttributes = ["levels", "keys", "expanded", "onexpand", "collapsed"];

    public static void Apply(XElement el, UiMarkupList list, object binding, MarkupBuildContext context)
    {
        if (!MarkupDocument.B(el, "tree", false))
        {
            foreach (string name in TreeAttributes)
            {
                if (el.Attribute(name) is not null)
                    throw new FormatException($"<list {name}> needs tree=\"true\"");
            }
            return;
        }
        if (list.Columns is not { Count: > 0 } columns)
            throw new FormatException("<list tree=\"true\"> needs <column> children");
        int treeColumn = -1;
        for (int i = 0; i < columns.Count; i++)
        {
            if (columns[i].Kind == UiMarkupListColumnKind.Text)
            {
                treeColumn = i;
                break;
            }
        }
        if (treeColumn < 0)
            throw new FormatException("<list tree=\"true\"> needs a text column to draw the tree in");

        list.TreeColumn = treeColumn;
        list.TreeLevelsSource = MarkupBindings.BindRequiredIntListReader(
            (string?)el.Attribute("levels"), binding, "list levels");
        list.TreeKeysSource = MarkupDocument.BindStringList((string?)el.Attribute("keys"), binding, "list keys");
        string? expanded = (string?)el.Attribute("expanded");
        if (expanded is not null)
        {
            if (el.Attribute("collapsed") is not null)
                throw new FormatException("<list collapsed> only applies when expanded is not bound");
            list.ExpandedSource = MarkupBindings.BindRequiredStringSetReader(expanded, binding, "list expanded");
        }
        else
        {
            list.StartCollapsed = MarkupDocument.B(el, "collapsed", false);
            list.RememberedExpansion = new RememberedValue(context.ControlState, list.Name, "expanded");
        }
        list.ExpansionChanged = MarkupBindings.BindStringBoolAction(
            (string?)el.Attribute("onexpand"), binding, "list onexpand");
    }
}
```

  In `MarkupDocument.cs` `case "list":`, directly after the `if (listUsesColumns) MarkupListHeader.Apply(…); else MarkupListHeader.RejectWithoutColumns(el);` block and before `parent.AddChild(list);`, add:

```csharp
                MarkupListTree.Apply(el, list, binding, Context());
```

  (`ApplyCommon` has already set `list.Name` by then, which `RememberedValue` needs.)

- [ ] **Step 7: Run the Verify command** → all pass. If a drawing test's pixel expectation is off, find out why from the layout code. Fix the test only if the plan's arithmetic is wrong, and keep the assertion as strong. Report it.

- [ ] **Step 8: Commit**

```bash
git add src/AcDream.App/UI/UiMarkupList.Tree.cs src/AcDream.App/UI/Markup/MarkupListTree.cs \
        src/AcDream.App/UI/UiMarkupList.Sort.cs src/AcDream.App/UI/UiMarkupList.cs \
        src/AcDream.App/UI/MarkupDocument.cs tests/AcDream.App.Tests/UI/MarkupListTreeTests.cs
git commit -m "feat(ui): list tree=\"true\" with levels, keys, expansion state and arrows

Claude-Session: https://claude.ai/code/session_014SAojwkxRKxrwp1RMDGPVC"
```

---

### Task 5: Tree input: arrow clicks, Left/Right, collapse deselects

**Goal:** Players collapse and expand by clicking an arrow or pressing Left/Right on a focused tree list. Unbound expansion is remembered, and bound expansion only calls `onexpand`. A collapse drops hidden rows from a multi selection.

**Files:**
- Modify: `src/AcDream.App/UI/UiMarkupList.Tree.cs`
- Modify: `src/AcDream.App/UI/UiMarkupList.Selection.cs` (key handler)
- Modify: `src/AcDream.App/UI/UiMarkupList.cs` (`OnEvent` gate, `OnEventColumns` text case)
- Modify: `src/AcDream.App/UI/Markup/MarkupListTree.cs` (focus flags)
- Test: `tests/AcDream.App.Tests/UI/MarkupListTreeTests.cs`

**Acceptance Criteria:**
- [ ] Clicking a parent's arrow slot toggles it without selecting. Clicking a leaf's slot selects as usual.
- [ ] Unbound and named:
  - a collapse with the default expanded stores `["a"]`, and toggling back removes the entry;
  - with `collapsed="true"`, expanding `b` stores `["b"]`;
  - `onexpand` is told each change.
- [ ] Bound: an arrow click only calls `onexpand(key, state)`; nothing shows differently until the plugin changes `expanded`.
- [ ] A tree list is `AcceptsFocus`, `TabStop` and not `FocusOnMouseClick`.
- [ ] Focused, single mode, on selected row A2 (expanded): Right → selects A2a; Left → selects A2; Left → collapses A2 (view 0, 1, 2, 4, 5) without changing the selection; Right → expands it again.
- [ ] Focused, multi mode: Right and Left act on the lead and move with `onchange([row])`.
- [ ] Multi mode, selection {1, 3, 4} with the lead on row 3: collapsing A calls `onchange([4])`. The lead moves to A, so a following Down selects row 4.
- [ ] Ctrl/Cmd+A selects only the rows shown.
- [ ] Single mode: a collapse never calls `onchange`. A disabled tree ignores arrow clicks and keys.

**Verify:** `dotnet test tests/AcDream.App.Tests/AcDream.App.Tests.csproj --filter "FullyQualifiedName~MarkupListTreeTests|FullyQualifiedName~MarkupListMultiSelectTests|FullyQualifiedName~MarkupList|FullyQualifiedName~ListButtonColumn"` → all pass.

**Steps:**

- [ ] **Step 1: Write the failing tests** — append to `MarkupListTreeTests`:

```csharp
    private static readonly UiSelectModifiers Add = new(Add: true, Range: false);

    private static void Press(Mounted m, Key key, UiSelectModifiers keys = default)
    {
        m.Keys = keys;
        m.Root.OnKeyDown((int)key);
        Redraw(m.List);
    }

    [Fact]
    public void ArrowClick_TogglesAParentWithoutSelecting()
    {
        var b = new Binding();
        var m = Mount(b);
        Click(m, ArrowX(0), RowY(0));
        Assert.Empty(b.Picks);
        Assert.Equal(new[] { 0, 4, 5 }, ViewOrder(m, b));
        Click(m, ArrowX(0), RowY(0));
        Assert.Equal(new[] { 0, 1, 2, 3, 4, 5 }, ViewOrder(m, b));
    }

    [Fact]
    public void ArrowSlotOfALeaf_Selects()
    {
        var b = new Binding();
        var m = Mount(b);
        Click(m, ArrowX(1), RowY(1));   // A1 is a leaf
        Assert.Equal(new[] { 1 }, b.Picks);
    }

    [Fact]
    public void Unbound_RemembersOnlyKeysThatDifferFromTheDefault()
    {
        var state = NewState();
        var b = new Binding();
        var m = Mount(b, Tree + " onexpand=\"{NotifyExpand}\"", state: state);
        Click(m, ArrowX(0), RowY(0));
        Assert.Equal("[\"a\"]", state.Get("rules", "expanded"));
        Assert.Equal(("a", false), Assert.Single(b.Expands));
        Click(m, ArrowX(0), RowY(0));
        Assert.Null(state.Get("rules", "expanded"));
        Assert.Equal(("a", true), b.Expands[1]);
    }

    [Fact]
    public void Unbound_CollapsedDefaultRemembersExpandedKeys()
    {
        var state = NewState();
        var b = new Binding();
        var m = Mount(b, Tree + " collapsed=\"true\"", state: state);
        Click(m, ArrowX(0), RowY(1));   // B at view 1
        Assert.Equal("[\"b\"]", state.Get("rules", "expanded"));
        Assert.Equal(new[] { 0, 4, 5 }, ViewOrder(m, b));
    }

    [Fact]
    public void Bound_ArrowClickOnlyAsksThePlugin()
    {
        var b = new Binding { Expanded = new() { "a", "a2", "b" } };
        var m = Mount(b, Tree + " expanded=\"{Expanded}\" onexpand=\"{NotifyExpand}\"");
        Click(m, ArrowX(0), RowY(0));
        Assert.Equal(("a", false), Assert.Single(b.Expands));
        Assert.Equal(new[] { 0, 1, 2, 3, 4, 5 }, ViewOrder(m, b));   // the plugin didn't apply it
    }

    [Fact]
    public void TreeList_IsATabStopThatClicksDoNotFocus()
    {
        var list = Mount(new Binding()).List;
        Assert.True(list.AcceptsFocus && list.TabStop);
        Assert.False(list.FocusOnMouseClick);
    }

    [Fact]
    public void LeftRight_SingleSelect()
    {
        var b = new Binding { Selected = 2 };   // A2, expanded, child A2a
        var m = Mount(b);
        m.Root.SetKeyboardFocus(m.List);
        Press(m, Key.Right);
        Assert.Equal(3, b.Selected);            // to the first child
        Press(m, Key.Left);
        Assert.Equal(2, b.Selected);            // leaf: to the parent
        b.Picks.Clear();
        Press(m, Key.Left);                     // expanded parent: collapse
        Assert.Empty(b.Picks);
        Assert.Equal(new[] { 0, 1, 2, 4, 5 }, ViewOrder(m, b));
        b.Selected = 2;
        Redraw(m.List);
        m.Root.SetKeyboardFocus(m.List);        // ViewOrder's clicks cleared focus
        Press(m, Key.Right);                    // collapsed parent: expand
        Assert.Equal(new[] { 0, 1, 2, 3, 4, 5 }, ViewOrder(m, b));
    }

    [Fact]
    public void LeftRight_ActOnTheLeadInMultiMode()
    {
        var b = new Binding();
        var m = Mount(b, MultiTree);
        Click(m, TextX, RowY(0));               // lead A
        m.Root.SetKeyboardFocus(m.List);
        Press(m, Key.Right);
        Assert.Equal(new[] { 1 }, b.Selection); // to A1
        Press(m, Key.Left);
        Assert.Equal(new[] { 0 }, b.Selection); // back to A
    }

    [Fact]
    public void Collapse_DeselectsHiddenRowsAndMovesTheLeadUp()
    {
        var b = new Binding();
        var m = Mount(b, MultiTree);
        Click(m, TextX, RowY(4));               // [4]
        Click(m, TextX, RowY(1), Add);          // [1, 4]
        Click(m, TextX, RowY(3), Add);          // [1, 3, 4], lead 3
        b.Changes.Clear();
        Click(m, ArrowX(0), RowY(0));           // collapse A
        Assert.Equal(new[] { 4 }, Assert.Single(b.Changes));
        m.Root.SetKeyboardFocus(m.List);
        Press(m, Key.Down);                     // lead moved to A (view 0): Down → view 1 = B
        Assert.Equal(new[] { 4 }, b.Selection);
    }

    [Fact]
    public void CtrlA_SelectsOnlyTheRowsShown()
    {
        var b = new Binding();
        var m = Mount(b, MultiTree + " collapsed=\"true\"");
        m.Root.SetKeyboardFocus(m.List);
        Press(m, Key.A, Add);
        Assert.Equal(new[] { 0, 4 }, b.Selection);
    }

    [Fact]
    public void SingleMode_CollapseKeepsTheSelection()
    {
        var b = new Binding { Selected = 3 };
        var m = Mount(b);
        Click(m, ArrowX(0), RowY(0));
        Assert.Empty(b.Picks);
        Assert.Equal(3, b.Selected);
    }

    [Fact]
    public void Disabled_IgnoresArrowsAndKeys()
    {
        var b = new Binding { Selected = 0 };
        var m = Mount(b, Tree + " enabled=\"false\"");
        Click(m, ArrowX(0), RowY(0));
        m.Root.SetKeyboardFocus(m.List);
        Press(m, Key.Left);
        b.Selected = -1;
        Redraw(m.List);
        // Still every row shown: nothing collapsed. (Rows can't be clicked while disabled, so count arrows.)
        m.List.TextColor = new Vector4(0.1f, 0.9f, 0.2f, 1f);
        Assert.Equal(12, MarkupDrawProbe.QuadBounds(MarkupDrawProbe.Draw(m.List), m.List.TextColor).Count);
    }
```

- [ ] **Step 2: Run, expect failures.**

- [ ] **Step 3: Toggle, keys and deselect** — append to `UiMarkupList.Tree.cs` (inside the class):

```csharp
    /// <summary>Whether a press at <paramref name="localX"/> lands in a parent row's arrow slot.</summary>
    private bool OnTreeArrow(int row, float cellX, float localX)
    {
        if (!_tree.HasChildren(row))
            return false;
        float slotX = TreeSlotX(row, cellX);
        return localX >= slotX && localX < slotX + TreeIndent;
    }

    /// <summary>
    /// A user collapse or expand of a parent row. Unbound, the host applies and
    /// remembers it; bound, the plugin is asked. A collapse drops the row's
    /// descendants from a multi selection.
    /// </summary>
    private void ToggleExpanded(int row)
    {
        if (!_tree.HasChildren(row))
            return;
        string key = TreeKey(row);
        bool expand = _tree.IsCollapsed(row);
        if (ExpandedSource is null)
        {
            HashSet<string> toggled = HostToggled;
            if (!toggled.Remove(key))
                toggled.Add(key);
            RememberedExpansion?.Remember(toggled.Count == 0 ? null : EncodeKeys(toggled));
            _tree.SetCollapsed(row, !expand);
        }
        ExpansionChanged?.Invoke(key, expand);
        if (!expand)
            DeselectHidden(row);
    }

    /// <summary>A collapse hides the row's descendants: a multi selection drops them, and an anchor or lead among them moves up to the row.</summary>
    private void DeselectHidden(int row)
    {
        if (!MultiSelect)
            return;
        int end = _tree.SubtreeEnd(row);
        if (_anchorRow > row && _anchorRow < end)
            _anchorRow = row;
        if (_leadRow > row && _leadRow < end)
            _leadRow = row;
        IReadOnlyList<int> current = SelectedRowsSource();
        var kept = new List<int>(current.Count);
        for (int i = 0; i < current.Count; i++)
        {
            int selected = current[i];
            if (selected <= row || selected >= end)
                kept.Add(selected);
        }
        if (kept.Count != current.Count)
            SelectedRowsChanged?.Invoke(kept.Distinct().Order().ToArray());
    }

    /// <summary>
    /// Right expands a collapsed parent or moves to an expanded one's first
    /// child; Left collapses an expanded parent or moves to the row's parent.
    /// Acts on the selected row (single) or the lead (multi) when it is shown.
    /// </summary>
    private void HandleTreeKey(bool right)
    {
        int row = MultiSelect ? _leadRow : SelectedIndexSource();
        int view = _view.ViewRow(row);
        if (view < 0)
            return;
        bool parent = _tree.HasChildren(row);
        if (right)
        {
            if (!parent)
                return;
            if (_tree.IsCollapsed(row))
                ToggleExpanded(row);
            else
                MoveTreeSelection(_view.PluginRow(view + 1));
            return;
        }
        if (parent && !_tree.IsCollapsed(row))
            ToggleExpanded(row);
        else if (_tree.Parent(row) >= 0)
            MoveTreeSelection(_tree.Parent(row));
    }

    private void MoveTreeSelection(int row)
    {
        if (row < 0)
            return;
        if (MultiSelect)
        {
            _anchorRow = _leadRow = row;
            SelectedRowsChanged?.Invoke([row]);
        }
        else
        {
            SelectionChanged?.Invoke(row);
        }
    }
```

- [ ] **Step 4: Route keys** — in `UiMarkupList.Selection.cs`:
  - Update the key handler's summary to mention that a tree list also takes Left/Right.
  - Directly after `var key = (Key)e.Data0;`, insert:

```csharp
        if (IsTree && key is Key.Left or Key.Right)
        {
            RefreshSelectionRows();
            HandleTreeKey(key == Key.Right);
            return true;
        }
        if (!MultiSelect)
            return false;
```

  Note on precedence: `IsTree && key is Key.Left or Key.Right` parses as `IsTree && (key is Key.Left or Key.Right)`, because `is` patterns bind tighter than `&&`.

  In `UiMarkupList.cs` `OnEvent`, change the first statement's gate from `if (MultiSelect && HandleSelectionEvent(e))` to `if ((MultiSelect || IsTree) && HandleSelectionEvent(e))`.

- [ ] **Step 5: Arrow clicks** — in `OnEventColumns`, the `Text` case becomes:

```csharp
                case UiMarkupListColumnKind.Text:
                    if (c == TreeColumn && OnTreeArrow(pluginRow, cellX, localX))
                        ToggleExpanded(pluginRow);
                    else if (columns[c].TextClicked is { } onTextClick)
                    {
                        if (pluginRow < (_cachedTextRows[c]?.Count ?? 0))
                            onTextClick(pluginRow);
                    }
                    else
                    {
                        PickRow(pluginRow);
                    }
                    break;
```

  Match the file's brace style; wrap the first branch in braces if the surrounding code always uses them.

- [ ] **Step 6: Focus flags** — in `MarkupListTree.Apply`, after `list.TreeColumn = treeColumn;` add:

```csharp
        list.AcceptsFocus = true;
        list.FocusOnMouseClick = false;
        list.TabStop = true;
```

- [ ] **Step 7: Run the Verify command** → all pass. Also run `dotnet build AcDream.slnx -c Release 2>&1 | grep -E ': warning|: error|Warning\(s\)|Error\(s\)' | sort -u` → 0 warnings, 0 errors.

- [ ] **Step 8: Commit**

```bash
git add src/AcDream.App/UI/UiMarkupList.Tree.cs src/AcDream.App/UI/UiMarkupList.Selection.cs \
        src/AcDream.App/UI/UiMarkupList.cs src/AcDream.App/UI/Markup/MarkupListTree.cs \
        tests/AcDream.App.Tests/UI/MarkupListTreeTests.cs
git commit -m "feat(ui): tree arrows, Left/Right and collapse deselect for list trees

Claude-Session: https://claude.ai/code/session_014SAojwkxRKxrwp1RMDGPVC"
```

---

### Task 6: Docs

**Goal:** `docs/plugin-ui-markup.md` documents tree lists.

**Files:**
- Modify: `docs/plugin-ui-markup.md`

**Acceptance Criteria:**
- [ ] The Elements table `list` row gains `tree levels keys expanded onexpand collapsed` (column form only).
- [ ] The Bindings table gains rows:
  - `list levels` (`IEnumerable<int>`, 0 = top level);
  - `list keys` (`IEnumerable<string>`);
  - `list expanded` (`IReadOnlySet<string>`, or any `IEnumerable<string>` copied each frame; when bound, the plugin owns it);
  - `list onexpand` (`Action<string, bool>`, the key and the state asked for).
- [ ] A `### Trees` subsection after `### Multi-select` and before `## Slider range` covers:
  - the example;
  - pre-order rows and level clamping;
  - keys and `"#<row>"`;
  - the indent, arrow and slot click;
  - Left/Right (with focus by Tab from another focused control, as the multi-select section says);
  - bound versus remembered expansion and `collapsed`;
  - sorting by siblings;
  - plugin rows unchanged;
  - multi-select on shown rows, and a collapse dropping hidden rows (single mode keeps its selection);
  - build errors;
  - the older-host line.
- [ ] The older-host line is checked: on base 391e709a an unknown `<list>` attribute is ignored. A tree there draws as a flat list with every row, no indent. Confirm with `git show 391e709a:src/AcDream.App/UI/MarkupDocument.cs` (the list case reads only the attributes it knows).

**Verify:** `grep -c 'onexpand\|tree="true"' docs/plugin-ui-markup.md` → at least 4.

**Steps:**

- [ ] **Step 1: Tables** — edit the two tables as listed, keeping their style.

- [ ] **Step 2: Section** — insert before `## Slider range`:

````markdown
### Trees

```xml
<list name="rules" x="8" y="24" w="320" h="200" tree="true" levels="{Depths}" keys="{Ids}"
      header="true" selected="{Sel}" onchange="{Pick}">
  <column type="checkbox" width="20" values="{On}" onchange="{ToggleRule}"/>
  <column type="text" header="Rule" items="{Names}" sort="text"/>
  <column type="text" header="Value" width="80" items="{Values}"/>
</list>
```

With `tree="true"` a column list shows its rows as a tree. The rows are the
tree flattened parent-first (each row followed by its children), and
`levels` gives each row's depth, 0 for the top level. A row more than one
level deeper than the row before it is treated as one deeper; a row past the
end of `levels` is top level. `keys` gives each row a stable id that the
expanded state is kept by; a row past the end of `keys` gets the key
`#<row>`, for example `#7`.

The first `text` column draws the tree: each level indents 12 pixels, and a
row with children shows ▸ when collapsed or ▾ when expanded. Clicking the
arrow collapses or expands the row without selecting it. With keyboard
focus (Tab from another focused control in the window), Right expands the
selected row or, if it is already expanded, moves to its first child, and
Left collapses it or moves to its parent. In a multi-select list the keys
act on the last row picked.

- Without `expanded` the client keeps the expanded state: every row starts
  expanded, or collapsed with `collapsed="true"`, and a list with a `name`
  remembers the rows the player changed, per character. `onexpand` is told
  each change.
- With `expanded="{Keys}"` (an `IReadOnlySet<string>`, or any
  `IEnumerable<string>`, which is copied each frame) the plugin keeps it:
  a row is expanded when its key is in the set. Clicking an arrow or Left/Right
  only calls `onexpand` with the key and the state asked for.

Sorting a tree sorts each row's children among themselves and keeps them
under their parent. Collapsed rows' children are left out of what is drawn,
but every row index the plugin sees is still its own index. A multi-select
list selects only rows that are shown (Shift ranges and Ctrl+A), and
collapsing a row removes its hidden children from the selection through
`onchange`. A single-select list keeps its selection when the selected row
is hidden.

`tree` needs `<column>` children with at least one `text` column. A tree
without `levels` or `keys`, `expanded` that isn't a set or list of strings,
`onexpand` that isn't an `Action<string, bool>`, `collapsed` together with
`expanded`, and `levels`, `keys`, `expanded`, `onexpand` or `collapsed`
without `tree="true"` fail the build. A host older than trees ignores these
attributes and draws every row as a flat list.
````

Re-check every sentence against `UiMarkupList.Tree.cs`, `UiMarkupListTreeShape.cs` and `MarkupListTree.cs`, and the older-host claim against base 391e709a. If the base host rejects unknown attributes, say what happens instead.

- [ ] **Step 3: Commit**

```bash
git add docs/plugin-ui-markup.md
git commit -m "docs(ui): list trees

Claude-Session: https://claude.ai/code/session_014SAojwkxRKxrwp1RMDGPVC"
```

---

### Task 7: Series gate

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
done | norm > $T/mc9-after.txt
dotnet test tests/AcDream.UI.Abstractions.Tests/AcDream.UI.Abstractions.Tests.csproj -c Release 2>&1 | norm > $T/mc9-uiabs-after.txt
echo "new App/Plugin:"; comm -13 $T/mc-base.txt $T/mc9-after.txt
echo "new UI.Abstractions:"; comm -13 $T/mc-uiabs-base.txt $T/mc9-uiabs-after.txt
git checkout -- '*.lock.json' 2>/dev/null; git status --short
```
Expected: `0 Warning(s)`, `0 Error(s)`, nothing after either "new" line.

- [ ] **Step 2: Each commit builds alone**

```bash
cd /Users/davidsmith/code/OpenAC/OpenAC
for c in $(git rev-list --reverse 502f7bd5..feat/markup-controls); do
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
git log --oneline 502f7bd5..HEAD
git diff --stat 391e709a..HEAD | grep superpowers || echo "no superpowers docs"
```
Expected: the 6 commits of Tasks 1-6 (plus any review-fix commits); "no superpowers docs".

No commit.
