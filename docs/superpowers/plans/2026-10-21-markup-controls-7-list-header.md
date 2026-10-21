# Markup controls, plan 7: `<list>` header, sorting and resizable columns Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers-extended-cc:subagent-driven-development (recommended) or superpowers-extended-cc:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** A column `<list>` can show a header row, sort by a column when its header is clicked, and let the player drag column widths. Sort and widths are plugin-owned when bound and remembered per character when not.

**Architecture:**
- **View map.** A pure `UiMarkupListViewMap` maps view rows (what is drawn, top to bottom) to plugin rows (the plugin's indices) and back. It is identity until a sort is applied. A sort is stable (ties keep plugin order), and rows without a key go last in both directions. `Version` moves whenever the order changes.
- **List refactor first.** `UiMarkupList` does all of its row positions in view rows. It converts to plugin rows at every data read and every callback. Then sorting only has to change the map. The refactor lands with an identity map and no behaviour change.
- **Header.** `header="true"` draws an 18 px header row. Rows, row hit-testing and the scrollbar start below it. The new code lives in `partial` files beside `UiMarkupList.cs`: `.Header.cs`, `.Sort.cs` and `.Widths.cs`. The builder additions live in `Markup/MarkupListHeader.cs`.
- **State.** The sort is one value, `(column, descending)`:
  - bound as the pair `sortcolumn` / `sortdescending`, with `onsort`;
  - otherwise remembered as `"<col>:asc|desc"` (kind `"sort"`).
- **Widths** are one value per column, where 0 means automatic:
  - bound as `widths`, with `onresize`;
  - otherwise remembered as a comma list of whole pixels (kind `"widths"`).
- **Commit rule.** Writes to the store happen only on commit: a header click, a drag end, or a double-click reset.

**Tech Stack:** C# / .NET 10, xUnit, headless `UiRoot` + `RecordingGpuDevice` tests.

**Spec:** `docs/superpowers/specs/2026-10-21-markup-controls-design.md` (branch `docs/markup-controls-spec`), "Shared rules" and section 8. Series 8 (multi-select) and 9 (tree) build on this plan's view map.

## Global Constraints

- **Workspace.** Worktree `/Users/davidsmith/code/OpenAC/OpenAC/.worktrees/markup-controls`, branch `feat/markup-controls` (HEAD 5561afcb, from upstream 391e709a).
  - Never merge fork `main` in, and never add `docs/superpowers/` files there.
  - Push only when the controller says so.
- **Build hygiene.** Every shell runs `export DOTNET_ROOT=$HOME/.dotnet PATH=$HOME/.dotnet:$PATH` first. After building, `git checkout -- '*.lock.json'`; never commit lock files.
- **Commits.** Every commit must build and pass its own tests. Commit messages end with `Claude-Session: https://claude.ai/code/session_01UzLnYkJ6ifzKGaeNwgsp8W`.
- **Helper conventions.**
  - `BindRequired*` helpers throw when the attribute is absent or mistyped.
  - Other `Bind*` helpers return null when absent and throw when mistyped.
  - `context` is `"<element> <attribute>"`.
  - The list case still adds exactly one element.
- **Remembered-state contract** (enforced by `RememberedValue`):
  - re-read when `Generation` moves;
  - build new values from a fresh read;
  - write only on a user commit;
  - unnamed lists and bound state never touch the store;
  - remove the entry when the encoded value equals the encoded markup default (no sort; the markup widths);
  - an undecodable stored value counts as absent.
- **Plugin rows.** Every callback, every bound `selected`, and every row index a plugin sees is a plugin row. Selection follows the row, not the position.
- **`<list>` contract (columns form only).**
  - `header="true|false"`; a column's `header` is a literal or a bound string.
  - A column's `sort="text|number|bool|none"`:
    - `text` and `number` need a text or button column, or `sortkeys`;
    - `bool` needs a check or checkbox column.
  - `sortkeys` is an `IReadOnlyList<double>` or an `IReadOnlyList<string>`, and only goes with `text` or `number`.
  - `sortcolumn` (`int`, −1 none) and `sortdescending` (`bool`) are bound as a pair or not at all; `onsort` is `Action<int, bool>`.
  - `resizable="true"` needs `header="true"`. `widths` is a float list with one entry per column (0 = automatic; the last entry is ignored) and needs `resizable`. `onresize` is `Action<IReadOnlyList<float>>` and needs `resizable`.
  - `header`, `resizable`, `sortcolumn`, `sortdescending`, `onsort`, `widths` and `onresize` on a single-column list (no `<column>`) throw.
- **Sizes and keys.**
  - The header is 18 px (`UiMarkupList.HeaderHeight`).
  - A dragged column is at least 16 px (`UiMarkupList.MinColumnWidth`). The boundary grip is ±3 px.
  - Text keys compare with `StringComparison.OrdinalIgnoreCase`. Numbers are parsed invariant-culture with `NumberStyles.Float`; unparseable values go last.
  - The bool key is `false` < `true`.
  - Each header click moves asc → desc → none.

**User decisions (already made):**
- One branch, one commit series per control, upstream-portable.
- Remembered state is "both": unbound state is host-remembered per character/window/control; bound state is the plugin's.
- The table is an extension of `<list>` (header, sort, multi-select, resizable columns), not a new element; the tree is `<list tree="true">` in a later series.

---

## File Structure

| File | Responsibility |
|---|---|
| `src/AcDream.App/UI/Markup/MarkupBindings.cs` | `BindIntBoolAction`, `BindRequiredFloatListReader`, `BindFloatListAction`, `BindSortKeys` |
| `src/AcDream.App/UI/UiMarkupListViewMap.cs` (new) | View row ↔ plugin row, stable sort, `Version` |
| `src/AcDream.App/UI/UiMarkupList.cs` | `partial`; rows through the view map; rows/scrollbar under the header; layout from the width state |
| `src/AcDream.App/UI/UiMarkupList.Header.cs` (new) | Header drawing and header pointer input |
| `src/AcDream.App/UI/UiMarkupList.Sort.cs` (new) | Sort state (bound/host/remembered), sort keys, header-click cycle, glyph |
| `src/AcDream.App/UI/UiMarkupList.Widths.cs` (new) | Width state, boundary drag, double-click reset, resize cursor |
| `src/AcDream.App/UI/UiMarkupListColumn.cs` | `HeaderSource`, `Sort`, `SortNumberKeys`, `SortTextKeys`; `UiMarkupListSortKind` |
| `src/AcDream.App/UI/Markup/MarkupListHeader.cs` (new) | Builder: list and column header/sort/width attributes |
| `src/AcDream.App/UI/MarkupDocument.cs` | The list case calls `MarkupListHeader` |
| `tests/AcDream.App.Tests/UI/MarkupListBindingsTests.cs`, `UiMarkupListViewMapTests.cs`, `MarkupListHeaderTests.cs` (new) | Tests |
| `docs/plugin-ui-markup.md` | Header, sorting, widths docs |

---

### Task 1: Binding helpers for sort and widths

**Goal:** Builders can bind `Action<int, bool>`, a required float list, an optional `Action<IReadOnlyList<float>>`, and sort keys that are either a double list or a string list.

**Files:**
- Modify: `src/AcDream.App/UI/Markup/MarkupBindings.cs`
- Test: `tests/AcDream.App.Tests/UI/MarkupListBindingsTests.cs` (new)

**Acceptance Criteria:**
- [ ] `BindIntBoolAction` returns null when the attribute is absent and invokes a bound `Action<int, bool>`. It throws `FormatException` naming the context and `Action<int, bool>` for a literal, a missing property or a wrong type.
- [ ] `BindRequiredFloatListReader` reads any `IEnumerable<float>` property as a fresh array on each call. It throws for an absent attribute, a literal, a missing property or a wrong type (e.g. `IReadOnlyList<int>`), naming the context.
- [ ] `BindFloatListAction` returns null when absent, invokes a bound `Action<IReadOnlyList<float>>`, and throws naming `Action<IReadOnlyList<float>>` when mistyped.
- [ ] `BindSortKeys` returns `(Numbers, null)` for an `IEnumerable<double>` property and `(null, Texts)` for an `IEnumerable<string>` property. It throws for anything else, naming the context.

**Verify:** `dotnet test tests/AcDream.App.Tests/AcDream.App.Tests.csproj --filter "FullyQualifiedName~MarkupListBindingsTests"` → all pass.

**Steps:**

- [ ] **Step 1: Write the failing tests** — create `tests/AcDream.App.Tests/UI/MarkupListBindingsTests.cs`:

```csharp
using System;
using System.Collections.Generic;
using AcDream.App.UI.Markup;

namespace AcDream.App.Tests.UI;

public sealed class MarkupListBindingsTests
{
    private sealed class Binding
    {
        public (int Column, bool Descending)? LastSort { get; private set; }
        public Action<int, bool> SetSort => (c, d) => LastSort = (c, d);
        public Action<int> WrongSort => _ => { };
        public List<float> Widths { get; } = new() { 120f, 80f };
        public IReadOnlyList<int> IntWidths { get; } = new[] { 1, 2 };
        public IReadOnlyList<float>? LastWidths { get; private set; }
        public Action<IReadOnlyList<float>> SetWidths => w => LastWidths = w;
        public IReadOnlyList<double> DistKeys { get; } = new[] { 1.5, 2.5 };
        public IReadOnlyList<string> NameKeys { get; } = new[] { "a", "b" };
        public IReadOnlyList<bool> Flags { get; } = new[] { true };
    }

    [Fact]
    public void IntBoolAction_MissingIsNullAndBoundInvokes()
    {
        var b = new Binding();
        Assert.Null(MarkupBindings.BindIntBoolAction(null, b, "list onsort"));
        MarkupBindings.BindIntBoolAction("{SetSort}", b, "list onsort")!(2, true);
        Assert.Equal((2, true), b.LastSort);
    }

    [Theory]
    [InlineData("SetSort")]
    [InlineData("{WrongSort}")]
    [InlineData("{Missing}")]
    public void IntBoolAction_RejectsLiteralsAndWrongTypes(string attribute)
    {
        var ex = Assert.Throws<FormatException>(
            () => MarkupBindings.BindIntBoolAction(attribute, new Binding(), "list onsort"));
        Assert.Contains("list onsort", ex.Message);
        Assert.Contains("Action<int, bool>", ex.Message);
    }

    [Fact]
    public void FloatListReader_SnapshotsTheList()
    {
        var b = new Binding();
        var read = MarkupBindings.BindRequiredFloatListReader("{Widths}", b, "list widths");
        IReadOnlyList<float> first = read();
        b.Widths[0] = 99f;
        Assert.Equal(new[] { 120f, 80f }, first);
        Assert.Equal(new[] { 99f, 80f }, read());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("120,80")]
    [InlineData("{Missing}")]
    [InlineData("{IntWidths}")]
    public void FloatListReader_RejectsAbsentLiteralsAndWrongTypes(string? expression)
    {
        var ex = Assert.Throws<FormatException>(
            () => MarkupBindings.BindRequiredFloatListReader(expression, new Binding(), "list widths"));
        Assert.Contains("list widths", ex.Message);
    }

    [Fact]
    public void FloatListAction_MissingIsNullAndBoundInvokes()
    {
        var b = new Binding();
        Assert.Null(MarkupBindings.BindFloatListAction(null, b, "list onresize"));
        MarkupBindings.BindFloatListAction("{SetWidths}", b, "list onresize")!(new[] { 1f });
        Assert.Equal(new[] { 1f }, b.LastWidths);
    }

    [Theory]
    [InlineData("SetWidths")]
    [InlineData("{SetSort}")]
    public void FloatListAction_RejectsLiteralsAndWrongTypes(string attribute)
    {
        var ex = Assert.Throws<FormatException>(
            () => MarkupBindings.BindFloatListAction(attribute, new Binding(), "list onresize"));
        Assert.Contains("Action<IReadOnlyList<float>>", ex.Message);
    }

    [Fact]
    public void SortKeys_TakeDoublesOrStrings()
    {
        var b = new Binding();
        var numbers = MarkupBindings.BindSortKeys("{DistKeys}", b, "column[1] sortkeys");
        Assert.Null(numbers.Texts);
        Assert.Equal(new[] { 1.5, 2.5 }, numbers.Numbers!());
        var texts = MarkupBindings.BindSortKeys("{NameKeys}", b, "column[0] sortkeys");
        Assert.Null(texts.Numbers);
        Assert.Equal(new[] { "a", "b" }, texts.Texts!());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("DistKeys")]
    [InlineData("{Missing}")]
    [InlineData("{Flags}")]
    public void SortKeys_RejectEverythingElse(string? expression)
    {
        var ex = Assert.Throws<FormatException>(
            () => MarkupBindings.BindSortKeys(expression, new Binding(), "column[1] sortkeys"));
        Assert.Contains("column[1] sortkeys", ex.Message);
    }
}
```

- [ ] **Step 2: Run to see them fail**

Run: `dotnet test tests/AcDream.App.Tests/AcDream.App.Tests.csproj --filter "FullyQualifiedName~MarkupListBindingsTests"`
Expected: build error. `MarkupBindings` has none of the four helpers.

- [ ] **Step 3: Implement** — in `MarkupBindings`, after `BindFloatAction`:

```csharp
    /// <summary>An optional <c>Action&lt;int, bool&gt;</c>; null when the attribute is absent.</summary>
    public static Action<int, bool>? BindIntBoolAction(string? attribute, object binding, string context)
    {
        if (attribute is null)
            return null;
        if (!MarkupDocument.IsBinding(attribute))
            throw new FormatException($"{context} must be an Action<int, bool> binding");
        PropertyInfo? property = binding.GetType().GetProperty(attribute[1..^1]);
        if (property is null || !typeof(Action<int, bool>).IsAssignableFrom(property.PropertyType))
        {
            throw new FormatException(
                $"{attribute} did not resolve to an Action<int, bool> property on "
                + binding.GetType().Name + $" ({context})");
        }
        return (first, second) => (property.GetValue(binding) as Action<int, bool>)?.Invoke(first, second);
    }

    /// <summary>A required list of floats (any <c>IEnumerable&lt;float&gt;</c>), copied on each read.</summary>
    public static Func<IReadOnlyList<float>> BindRequiredFloatListReader(
        string? expression, object binding, string context)
    {
        if (string.IsNullOrWhiteSpace(expression) || !MarkupDocument.IsBinding(expression))
            throw new FormatException($"{context} must be a float-list binding");
        PropertyInfo? property = binding.GetType().GetProperty(expression[1..^1]);
        if (property is null || !typeof(IEnumerable<float>).IsAssignableFrom(property.PropertyType))
        {
            throw new FormatException(
                $"{expression} did not resolve to an IEnumerable<float> property on "
                + binding.GetType().Name + $" ({context})");
        }
        return () => property.GetValue(binding) is IEnumerable<float> values
            ? values.ToArray()
            : Array.Empty<float>();
    }

    /// <summary>An optional <c>Action&lt;IReadOnlyList&lt;float&gt;&gt;</c>; null when the attribute is absent.</summary>
    public static Action<IReadOnlyList<float>>? BindFloatListAction(string? attribute, object binding, string context) =>
        BindOptionalAction<IReadOnlyList<float>>(attribute, binding, context, "IReadOnlyList<float>");

    /// <summary>
    /// Sort keys for a list column: a list of doubles (compared as numbers) or
    /// of strings (compared as the column's sort kind says). Exactly one of the
    /// pair is set.
    /// </summary>
    public static (Func<IReadOnlyList<double>>? Numbers, Func<IReadOnlyList<string>>? Texts) BindSortKeys(
        string? expression, object binding, string context)
    {
        if (string.IsNullOrWhiteSpace(expression) || !MarkupDocument.IsBinding(expression))
            throw new FormatException($"{context} must be a binding to a list of double or string");
        PropertyInfo? property = binding.GetType().GetProperty(expression[1..^1]);
        if (property is not null && typeof(IEnumerable<double>).IsAssignableFrom(property.PropertyType))
        {
            return (() => property.GetValue(binding) is IEnumerable<double> values
                ? values.ToArray()
                : Array.Empty<double>(), null);
        }
        if (property is not null && typeof(IEnumerable<string>).IsAssignableFrom(property.PropertyType))
        {
            return (null, () => property.GetValue(binding) is IEnumerable<string> values
                ? values.ToArray()
                : Array.Empty<string>());
        }
        throw new FormatException(
            $"{expression} did not resolve to an IReadOnlyList<double> or IReadOnlyList<string> property on "
            + binding.GetType().Name + $" ({context})");
    }
```

(`ImplicitUsings` is on in `AcDream.App.csproj`, so `System.Linq` is available.)

- [ ] **Step 4: Run** — same command → PASS.

- [ ] **Step 5: Commit**

```bash
git checkout -- '*.lock.json' 2>/dev/null
git add src/AcDream.App/UI/Markup/MarkupBindings.cs tests/AcDream.App.Tests/UI/MarkupListBindingsTests.cs
git commit -m "feat(ui): sort and width bindings for markup lists

Claude-Session: https://claude.ai/code/session_01UzLnYkJ6ifzKGaeNwgsp8W"
```

---

### Task 2: The view map

**Goal:** A pure, tested `UiMarkupListViewMap`: identity or a stable sort by text or number keys, an inverse map, and a `Version` that moves only when the order changes.

**Files:**
- Create: `src/AcDream.App/UI/UiMarkupListViewMap.cs`
- Test: `tests/AcDream.App.Tests/UI/UiMarkupListViewMapTests.cs` (new)

**Acceptance Criteria:**
- [ ] Identity: `PluginRow(v) == v` and `ViewRow(p) == p` inside `Count`; −1 outside, including negatives.
- [ ] Text sort is case-insensitive (`OrdinalIgnoreCase`), with null keys last both ascending and descending. Number sort puts NaN keys last in both directions.
- [ ] Ties keep plugin order in both directions (stable).
- [ ] `ViewRow` is the inverse of `PluginRow` after a sort.
- [ ] `Version` behaviour:
  - It does not move when the same keys are sorted again, or when an identity map only changes its count.
  - It moves when new keys change the order, when going from a non-identity sort back to identity, and when a sorted map's count changes.

**Verify:** `dotnet test tests/AcDream.App.Tests/AcDream.App.Tests.csproj --filter "FullyQualifiedName~UiMarkupListViewMapTests"` → all pass.

**Steps:**

- [ ] **Step 1: Write the failing tests** — create `tests/AcDream.App.Tests/UI/UiMarkupListViewMapTests.cs`:

```csharp
using System.Linq;
using AcDream.App.UI;

namespace AcDream.App.Tests.UI;

public sealed class UiMarkupListViewMapTests
{
    private static int[] Order(UiMarkupListViewMap map) =>
        Enumerable.Range(0, map.Count).Select(map.PluginRow).ToArray();

    [Fact]
    public void IdentityMapsEachRowToItself()
    {
        var map = new UiMarkupListViewMap();
        map.SetIdentity(3);
        Assert.Equal(new[] { 0, 1, 2 }, Order(map));
        Assert.Equal(2, map.ViewRow(2));
        Assert.Equal(-1, map.PluginRow(3));
        Assert.Equal(-1, map.PluginRow(-1));
        Assert.Equal(-1, map.ViewRow(3));
    }

    [Fact]
    public void TextSortIgnoresCaseAndPutsMissingKeysLast()
    {
        var map = new UiMarkupListViewMap();
        string?[] keys = { "mosswart", "Drudge", null, "rat", "Banderling" };
        map.SortByText(keys, 5, descending: false);
        Assert.Equal(new[] { 4, 1, 0, 3, 2 }, Order(map));
        map.SortByText(keys, 5, descending: true);
        Assert.Equal(new[] { 3, 0, 1, 4, 2 }, Order(map));
    }

    [Fact]
    public void NumberSortPutsNaNLastBothWays()
    {
        var map = new UiMarkupListViewMap();
        double[] keys = { 10, double.NaN, 2, 33 };
        map.SortByNumber(keys, 4, descending: false);
        Assert.Equal(new[] { 2, 0, 3, 1 }, Order(map));
        map.SortByNumber(keys, 4, descending: true);
        Assert.Equal(new[] { 3, 0, 2, 1 }, Order(map));
    }

    [Fact]
    public void TiesKeepThePluginOrderBothWays()
    {
        var map = new UiMarkupListViewMap();
        double[] keys = { 1, 0, 1, 0 };
        map.SortByNumber(keys, 4, descending: false);
        Assert.Equal(new[] { 1, 3, 0, 2 }, Order(map));
        map.SortByNumber(keys, 4, descending: true);
        Assert.Equal(new[] { 0, 2, 1, 3 }, Order(map));
    }

    [Fact]
    public void ViewRowInvertsPluginRow()
    {
        var map = new UiMarkupListViewMap();
        map.SortByNumber(new double[] { 5, 3, 9, 1 }, 4, descending: false);
        for (int view = 0; view < map.Count; view++)
            Assert.Equal(view, map.ViewRow(map.PluginRow(view)));
    }

    [Fact]
    public void VersionMovesOnlyWhenTheOrderChanges()
    {
        var map = new UiMarkupListViewMap();
        map.SetIdentity(3);
        int v0 = map.Version;
        map.SetIdentity(4);
        Assert.Equal(v0, map.Version);                 // identity growing is not a reorder

        double[] keys = { 3, 1, 2, 0 };
        map.SortByNumber(keys, 4, descending: false);
        int v1 = map.Version;
        Assert.NotEqual(v0, v1);
        map.SortByNumber(keys, 4, descending: false);
        Assert.Equal(v1, map.Version);                 // same keys, same order

        keys[0] = -1;
        map.SortByNumber(keys, 4, descending: false);
        int v2 = map.Version;
        Assert.NotEqual(v1, v2);

        map.SortByNumber(keys, 3, descending: false);
        int v3 = map.Version;
        Assert.NotEqual(v2, v3);                       // a sorted map that lost a row

        map.SetIdentity(3);
        Assert.NotEqual(v3, map.Version);              // the sort was cleared
    }
}
```

- [ ] **Step 2: Run to see them fail** — build error: `UiMarkupListViewMap` does not exist.

- [ ] **Step 3: Implement** — create `src/AcDream.App/UI/UiMarkupListViewMap.cs`:

```csharp
namespace AcDream.App.UI;

/// <summary>
/// The order a markup list shows its rows in: view row (top to bottom) to
/// plugin row (the plugin's index) and back. Identity until sorted. A sort is
/// stable (ties keep the plugin's order) and puts rows without a key last,
/// ascending or descending. <see cref="Version"/> moves whenever the order
/// changes, so a press can tell when the rows moved under it.
/// </summary>
internal sealed class UiMarkupListViewMap
{
    private int[] _viewToPlugin = Array.Empty<int>();
    private int[] _pluginToView = Array.Empty<int>();
    private int[] _scratch = Array.Empty<int>();
    private bool _identity = true;
    private readonly KeyComparer _comparer = new();

    public int Count { get; private set; }

    public int Version { get; private set; }

    /// <summary>The plugin row shown at a view row; −1 outside the rows.</summary>
    public int PluginRow(int view) =>
        (uint)view >= (uint)Count ? -1 : _identity ? view : _viewToPlugin[view];

    /// <summary>Where a plugin row is shown; −1 outside the rows.</summary>
    public int ViewRow(int plugin) =>
        (uint)plugin >= (uint)Count ? -1 : _identity ? plugin : _pluginToView[plugin];

    public void SetIdentity(int count)
    {
        bool changed = false;
        if (!_identity)
        {
            for (int view = 0; !changed && view < Math.Min(count, Count); view++)
                changed = _viewToPlugin[view] != view;
            changed |= count != Count;
        }
        _identity = true;
        Count = count;
        if (changed)
            Version++;
    }

    /// <summary>Sorts by <paramref name="keys"/>[plugin row]; a null key sorts last.</summary>
    public void SortByText(string?[] keys, int count, bool descending)
    {
        _comparer.Texts = keys;
        _comparer.Numbers = null;
        _comparer.Descending = descending;
        Apply(count);
    }

    /// <summary>Sorts by <paramref name="keys"/>[plugin row]; a NaN key sorts last.</summary>
    public void SortByNumber(double[] keys, int count, bool descending)
    {
        _comparer.Texts = null;
        _comparer.Numbers = keys;
        _comparer.Descending = descending;
        Apply(count);
    }

    private void Apply(int count)
    {
        if (_scratch.Length < count)
            _scratch = new int[count];
        for (int i = 0; i < count; i++)
            _scratch[i] = i;
        Array.Sort(_scratch, 0, count, _comparer);
        _comparer.Texts = null;
        _comparer.Numbers = null;

        bool changed = count != Count;
        for (int view = 0; !changed && view < count; view++)
            changed = _scratch[view] != PluginRow(view);

        (_viewToPlugin, _scratch) = (_scratch, _viewToPlugin);
        if (_pluginToView.Length < count)
            _pluginToView = new int[count];
        for (int view = 0; view < count; view++)
            _pluginToView[_viewToPlugin[view]] = view;
        _identity = false;
        Count = count;
        if (changed)
            Version++;
    }

    private sealed class KeyComparer : IComparer<int>
    {
        public string?[]? Texts;
        public double[]? Numbers;
        public bool Descending;

        public int Compare(int a, int b)
        {
            int order;
            if (Numbers is { } numbers)
            {
                bool aMissing = double.IsNaN(numbers[a]), bMissing = double.IsNaN(numbers[b]);
                if (aMissing != bMissing)
                    return aMissing ? 1 : -1;
                order = aMissing ? 0 : numbers[a].CompareTo(numbers[b]);
            }
            else
            {
                string? x = Texts![a], y = Texts[b];
                if ((x is null) != (y is null))
                    return x is null ? 1 : -1;
                order = x is null ? 0 : string.Compare(x, y, StringComparison.OrdinalIgnoreCase);
            }
            if (order != 0)
                return Descending ? -order : order;
            return a.CompareTo(b);
        }
    }
}
```

- [ ] **Step 4: Run** — same command → PASS.

- [ ] **Step 5: Commit**

```bash
git checkout -- '*.lock.json' 2>/dev/null
git add src/AcDream.App/UI/UiMarkupListViewMap.cs tests/AcDream.App.Tests/UI/UiMarkupListViewMapTests.cs
git commit -m "feat(ui): a view map for sorted markup list rows

Claude-Session: https://claude.ai/code/session_01UzLnYkJ6ifzKGaeNwgsp8W"
```

---

### Task 3: Rows through the view map

**Goal:** `UiMarkupList`'s column form handles all row positions as view rows and converts to plugin rows for every data read and callback, through an identity view map. There is no behaviour change.

**Files:**
- Modify: `src/AcDream.App/UI/UiMarkupList.cs`
- Test: existing `tests/AcDream.App.Tests/UI/MarkupListColumnsTests.cs`, `ListButtonColumnTests.cs`, `UiMarkupListScrollbarTests.cs`, `UiMarkupListScrollSelectionPersistenceTests.cs`

**Acceptance Criteria:**
- [ ] The class is `public sealed partial class UiMarkupList`.
- [ ] `RefreshRows` ends by calling `RebuildView(columns, rowCount)`, which in this task is `_view.SetIdentity(rowCount)`.
- [ ] These all look rows up by `_view.PluginRow(view)`: drawing, text/check/icon clicks, `SelectionChanged`, button captions and `ButtonClicked`.
- [ ] The selection band and the scroll-to-selection use `_view.ViewRow(selected)`. Scroll-to-selection runs again when the map's `Version` changes.
- [ ] A button press is cancelled when `_view.Version` moves between press and release (in `CancelPressIfRowsMoved`).
- [ ] Every existing list test passes unchanged.

**Verify:** `dotnet test tests/AcDream.App.Tests/AcDream.App.Tests.csproj --filter "FullyQualifiedName~MarkupList|FullyQualifiedName~ListButtonColumn|FullyQualifiedName~UiMarkupList"` → all pass (same count as before the change).

**Steps:**

- [ ] **Step 1: Baseline** — run the Verify command before changing anything, and note the pass count.

- [ ] **Step 2: Make the class partial and add the map** — change the declaration to `public sealed partial class UiMarkupList : UiElement`. Next to `_lastRevealedSelected`, add:

```csharp
    private int _lastRevealedVersion = int.MinValue;

    // Which plugin row each view row shows. Positions (_topRow, hover and
    // press rows) are view rows; data and callbacks use plugin rows.
    private readonly UiMarkupListViewMap _view = new();
    private int _pressVersion;
```

- [ ] **Step 3: `RefreshRows`** — replace its last two lines (`_cachedRowCount = rowCount; return rowCount;`) with:

```csharp
        _cachedRowCount = rowCount;
        RebuildView(columns, rowCount);
        return rowCount;
    }

    /// <summary>Re-derives the row order after the rows were read.</summary>
    private void RebuildView(IReadOnlyList<UiMarkupListColumn> columns, int rowCount) =>
        _view.SetIdentity(rowCount);
```

(The `}` shown is `RefreshRows`'s own closing brace; do not leave a duplicate.)

- [ ] **Step 4: `DrawColumns`** — replace the block from `int selected = SelectedIndexSource();` to the end of the row loop with:

```csharp
        int selected = SelectedIndexSource();
        int selectedView = _view.ViewRow(selected);
        if (selected != _lastRevealedSelected || _view.Version != _lastRevealedVersion)
        {
            _lastRevealedSelected = selected;
            _lastRevealedVersion = _view.Version;
            if (selectedView >= 0)
            {
                if (selectedView < _topRow)
                    _topRow = selectedView;
                else if (selectedView >= _topRow + visibleRows)
                    _topRow = selectedView - visibleRows + 1;
            }
        }
        ClampTop(rowCount, visibleRows);
        if (_hasButtons)
        {
            CancelPressIfRowsMoved();
            RefreshHover(contentWidth, visibleRows);
        }

        context.DrawFill(0f, 0f, Width, Height, BackgroundColor);
        context.DrawRectOutline(0f, 0f, Width, Height, BorderColor, 1f);

        int end = Math.Min(rowCount, _topRow + visibleRows);
        for (int view = _topRow; view < end; view++)
        {
            int row = _view.PluginRow(view);
            float y = (view - _topRow) * RowHeight;
            if (view == selectedView && SelectionBandEnabled)
                context.DrawFill(1f, y + 1f, contentWidth - 2f, RowHeight - 1f, SelectedColor);

            for (int c = 0; c < columns.Count; c++)
            {
                (float cellX, float cellW) = _cachedLayout[c];
                if (cellW <= 0f)
                    continue;

                context.PushClip(cellX, y, cellW, RowHeight);
                try
                {
                    switch (columns[c].Kind)
                    {
                        case UiMarkupListColumnKind.Text:
                            DrawTextCell(context, _cachedTextRows[c], _cachedColorRows[c], row, cellX, y);
                            break;
                        case UiMarkupListColumnKind.Check:
                            DrawCheckCell(context, _cachedCheckRows[c], row, cellX, cellW, y, columns[c].Boxed);
                            break;
                        case UiMarkupListColumnKind.Icon:
                            DrawIconCell(context, columns[c], _cachedIconRows[c], row, cellX, cellW, y);
                            break;
                        case UiMarkupListColumnKind.Button:
                            DrawButtonCell(context, c, view, row, cellX, cellW, y);
                            break;
                    }
                }
                finally
                {
                    context.PopClip();
                }
            }
        }
```

- [ ] **Step 5: Buttons** — make these changes:
  - `DrawButtonCell` signature becomes `(UiRenderContext context, int column, int view, int row, float cellX, float cellW, float y)`. Inside it, `HasButtonCaption(column, row, out string caption)` and `ButtonState(view, column)`.
  - `ButtonStateForTest(int index, int column)` becomes `HasButtonCaption(column, _view.PluginRow(index), out _) ? ButtonState(index, column) : UiControlState.Normal`.
  - In `ButtonCellAt`: `if (!HasButtonCaption(c, _view.PluginRow(index), out _))`.
  - In `RememberPressedRow`, add `_pressVersion = _view.Version;` and change its caption read to `HasButtonCaption(_pressColumn, _view.PluginRow(_pressRow), out _pressCaption);`.
  - `CancelPressIfRowsMoved`'s `moved` becomes:

```csharp
        bool moved = _topRow != _pressTopRow
            || _cachedRowCount != _pressRowCount
            || _view.Version != _pressVersion
            || !HasButtonCaption(_pressColumn, _view.PluginRow(_pressRow), out string caption)
            || !string.Equals(caption, _pressCaption, StringComparison.Ordinal);
```

  Also add "or the row order changed (a sort)" to its doc comment's list.
  - In `TrackButtonPointer`'s `MouseUp`: `columns[column].ButtonClicked?.Invoke(_view.PluginRow(index));`

- [ ] **Step 6: Clicks** — in `OnEventColumns`, after the `if (row < 0 || row >= visibleRows || index < 0 || index >= rowCount) return true;` check, add `int pluginRow = _view.PluginRow(index);`. In the column switch, use `pluginRow` in place of `index` for:
  - every `_cached*Rows[c]?.Count` bound check;
  - `onTextClick`, `SelectionChanged`, `CheckChanged` and `IconClicked`.

  The `Button` case keeps `index` (a view row, compared with `_hoverRow`).

- [ ] **Step 7: Run** — the Verify command → PASS, same count as Step 1. The single-column form (`ItemsSource`) is untouched.

- [ ] **Step 8: Commit**

```bash
git checkout -- '*.lock.json' 2>/dev/null
git add src/AcDream.App/UI/UiMarkupList.cs
git commit -m "refactor(ui): markup list rows go through a view map

Positions are view rows; data and callbacks use plugin rows. The map is
the identity until lists can sort.

Claude-Session: https://claude.ai/code/session_01UzLnYkJ6ifzKGaeNwgsp8W"
```

---

### Task 4: Header row

**Goal:** `header="true"` on a column list draws an 18 px header of column captions (literal or bound) in both looks. Rows, row clicks and the scrollbar start below it. Header presses select nothing.

**Files:**
- Create: `src/AcDream.App/UI/UiMarkupList.Header.cs`, `src/AcDream.App/UI/Markup/MarkupListHeader.cs`
- Modify: `src/AcDream.App/UI/UiMarkupList.cs`, `src/AcDream.App/UI/UiMarkupListColumn.cs`, `src/AcDream.App/UI/MarkupDocument.cs`
- Test: `tests/AcDream.App.Tests/UI/MarkupListHeaderTests.cs` (new)

**Acceptance Criteria:**
- [ ] With a header, a click at (60, 27) selects row 0, and a click at (60, 9) selects nothing and fires nothing. The button cell of row 0 fires on a press and release at (270, 27).
- [ ] With a header, `h="108"` shows 5 rows. With 8 rows the scrollbar's Track sprite runs from y 18 to 108 at x 284.
- [ ] Classic face:
  - a Track sprite tiled over (0, 0, 300, 18);
  - a `BorderColor` underline quad at (0, 17, 300, 1);
  - `BorderColor` separators at x 119, 199 and 239 (y 2, h 14).
- [ ] Under the plugin theme (Moss):
  - a `Moss.Field` quad at (0, 0, 300, 18);
  - the underline in `Moss.Border`;
  - no header Track sprite.
- [ ] A column's `header` reads as a literal and as a bound string that updates (`HeaderSource`).
- [ ] Without `header` the list is as before: a click at (60, 9) selects row 0.
- [ ] The build throws `FormatException` in these cases:
  - `header="maybe"`;
  - any of `header`, `resizable`, `sortcolumn`, `sortdescending`, `onsort`, `widths`, `onresize` on a list without `<column>`.

**Verify:** `dotnet test tests/AcDream.App.Tests/AcDream.App.Tests.csproj --filter "FullyQualifiedName~MarkupListHeaderTests|FullyQualifiedName~MarkupList|FullyQualifiedName~ListButtonColumn|FullyQualifiedName~UiMarkupList"` → all pass.

**Steps:**

- [ ] **Step 1: Write the failing tests** — create `tests/AcDream.App.Tests/UI/MarkupListHeaderTests.cs`. This fixture is shared by Tasks 5 and 6, which append to the class. The `sort`, `sortkeys`, `widths` attributes in the fixture are ignored until those tasks implement them.

```csharp
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using AcDream.App.UI;
using AcDream.App.UI.Markup;
using AcDream.UI.Abstractions.Panels.Settings;

namespace AcDream.App.Tests.UI;

public sealed class MarkupListHeaderTests : IDisposable
{
    private readonly string _settingsPath = Path.Combine(Path.GetTempPath(), $"list-header-{Guid.NewGuid():N}.json");

    public void Dispose()
    {
        if (File.Exists(_settingsPath)) File.Delete(_settingsPath);
    }

    private sealed class Binding
    {
        public List<string> Names { get; set; } = new() { "mosswart", "Drudge", "rat", "Banderling" };
        public List<string> Dists { get; set; } = new() { "12.5 m", "3 m", "40 m", "7 m" };
        public List<double> DistKeys { get; set; } = new() { 12.5, 3, 40, 7 };
        public List<string> Levels { get; set; } = new() { "10", "x", "2", "33" };
        public List<bool> On { get; set; } = new() { true, false, true, false };
        public List<string> Acts { get; set; } = new() { "Go", "Go", "Go", "Go" };
        public List<uint> Ids { get; } = new() { 1, 2, 3, 4 };
        public string DistTitle { get; set; } = "Dist";
        public int Selected { get; set; } = -1;
        public List<int> Picks { get; } = new();
        public Action<int> Pick => row => { Selected = row; Picks.Add(row); };
        public List<int> Toggled { get; } = new();
        public Action<int> Toggle => row => Toggled.Add(row);
        public List<int> Acted { get; } = new();
        public Action<int> Act => row => Acted.Add(row);
        public int SortCol { get; set; } = -1;
        public bool SortDesc { get; set; }
        public List<(int, bool)> Sorts { get; } = new();
        public Action<int, bool> SetSort => (c, d) => { SortCol = c; SortDesc = d; Sorts.Add((c, d)); };
        public Action<int, bool> NotifySort => (c, d) => Sorts.Add((c, d));
        public List<float> Widths { get; set; } = new() { 120f, 80f, 40f, 0f };
        public List<IReadOnlyList<float>> Resizes { get; } = new();
        public Action<IReadOnlyList<float>> SetWidths => w => { Widths = w.ToList(); Resizes.Add(w); };
        public Action<IReadOnlyList<float>> NotifyWidths => w => Resizes.Add(w);
    }

    // Columns: Name 0-120, Dist 120-200, On 200-240, button 240-300.
    // Header y 0-18; view row v spans y 18+18v .. 36+18v.
    private const string ColumnsXml = """
          <column type="text" header="Name" width="120" items="{Names}" sort="text"/>
          <column type="text" header="{DistTitle}" width="80" items="{Dists}" sort="number" sortkeys="{DistKeys}"/>
          <column type="checkbox" header="On" width="40" values="{On}" onchange="{Toggle}" sort="bool"/>
          <column type="button" items="{Acts}" onclick="{Act}"/>
        """;

    // header="true" unless the test sets header itself (a repeated attribute is invalid XML).
    private static string Markup(string attributes, string columns = ColumnsXml, string extraPanel = "")
    {
        string header = attributes.Contains("header=") ? "" : "header=\"true\"";
        return $"""
            <panel x="0" y="0" w="400" h="300" {extraPanel}>
              <list name="mobs" x="0" y="0" w="300" h="108" {header} selected="{"{Selected}"}" onchange="{"{Pick}"}" {attributes}>
            {columns}
              </list>
            </panel>
            """;
    }

    /// <summary>Every quad drawn with a texture, as (left, top, right, bottom).</summary>
    private static List<(float X, float Y, float R, float B)> SpriteQuads(
        AcDream.App.Rendering.TextRenderer renderer, uint texture) =>
        renderer.DebugSpriteSegmentVerts
            .Where(s => s.Texture == texture)
            .SelectMany(s => Enumerable.Range(0, s.Verts.Count / 48)
                .Select(q => (s.Verts[q * 48], s.Verts[q * 48 + 1], s.Verts[q * 48 + 8], s.Verts[q * 48 + 9])))
            .ToList();

    private static (UiRoot Root, UiMarkupList List) Mount(
        string attributes, Binding binding, IMarkupControlState? state = null, string columns = ColumnsXml,
        PluginUiThemeSettings? themes = null)
    {
        var panel = MarkupDocument.Build(
            Markup(attributes, columns, themes is null ? "" : "theme=\"plugin\""),
            binding, id => (id, 16, 16), controlState: state, themes: themes);
        var root = new UiRoot { Width = 800, Height = 600 };
        root.AddChild(panel);
        var list = panel.Children.OfType<UiMarkupList>().Single();
        MarkupDrawProbe.Draw(panel, 800f, 600f);   // lists lay out and read rows at draw
        return (root, list);
    }

    private static void Redraw(UiMarkupList list) => MarkupDrawProbe.Draw(list.Parent!, 800f, 600f);

    private static void Click(UiRoot root, int x, int y)
    {
        root.OnMouseDown(UiMouseButton.Left, x, y);
        root.OnMouseUp(UiMouseButton.Left, x, y);
    }

    private static void ClickAndDraw(UiRoot root, UiMarkupList list, int x, int y)
    {
        Click(root, x, y);
        Redraw(list);
    }

    /// <summary>The plugin row picked by clicking the Name cell of each view row in turn.</summary>
    private static int[] ViewOrder(UiRoot root, UiMarkupList list, Binding b)
    {
        var order = new List<int>();
        for (int view = 0; view < 4; view++)
        {
            b.Picks.Clear();
            Click(root, 60, 27 + 18 * view);
            order.Add(Assert.Single(b.Picks));
        }
        Redraw(list);
        return order.ToArray();
    }

    [Fact]
    public void Header_RowsStartBelowItAndHeaderPressesSelectNothing()
    {
        var b = new Binding();
        var (root, _) = Mount("", b);
        Click(root, 60, 9);
        Assert.Empty(b.Picks);
        Click(root, 60, 27);
        Assert.Equal(new[] { 0 }, b.Picks);
        Click(root, 270, 27);
        Assert.Equal(new[] { 0 }, b.Acted);
    }

    [Fact]
    public void Header_ScrollbarStartsBelowIt()
    {
        var b = new Binding { Names = Enumerable.Range(0, 8).Select(i => $"m{i}").ToList() };
        var (_, list) = Mount("", b);
        var renderer = MarkupDrawProbe.Draw(list);
        Assert.Contains((284f, 18f, 300f, 108f), SpriteQuads(renderer, RetailScrollbarChrome.Track));
    }

    [Fact]
    public void Header_ClassicFaceIsTheTrackArtWithAnUnderlineAndSeparators()
    {
        var (_, list) = Mount("", new Binding());
        var renderer = MarkupDrawProbe.Draw(list);
        Assert.Equal(new[] { (0f, 0f, 300f, 18f) }, SpriteQuads(renderer, RetailScrollbarChrome.Track));
        var lines = MarkupDrawProbe.QuadBounds(renderer, list.BorderColor);
        Assert.Contains((0f, 17f, 300f, 1f), lines);
        foreach (float x in new[] { 119f, 199f, 239f })
            Assert.Contains((x, 2f, 1f, 14f), lines);
    }

    [Fact]
    public void Header_PluginThemeUsesTheFieldAndBorderTokens()
    {
        var themes = new PluginUiThemeSettings { Theme = PluginUiTheme.Moss };
        var (_, list) = Mount("", new Binding(), themes: themes);
        var renderer = MarkupDrawProbe.Draw(list);
        Assert.Contains((0f, 0f, 300f, 18f), MarkupDrawProbe.QuadBounds(renderer, PluginUiPalette.Moss.Field));
        Assert.Contains((0f, 17f, 300f, 1f), MarkupDrawProbe.QuadBounds(renderer, PluginUiPalette.Moss.Border));
        Assert.Empty(SpriteQuads(renderer, RetailScrollbarChrome.Track));
    }

    [Fact]
    public void Header_CaptionsAreLiteralOrBound()
    {
        var b = new Binding();
        var (_, list) = Mount("", b);
        Assert.Equal("Name", list.Columns![0].HeaderSource!());
        Assert.Equal("Dist", list.Columns[1].HeaderSource!());
        b.DistTitle = "Range";
        Assert.Equal("Range", list.Columns[1].HeaderSource!());
        Assert.Null(list.Columns[3].HeaderSource);
    }

    [Fact]
    public void Header_OffLeavesTheListAsBefore()
    {
        var b = new Binding();
        var (root, _) = Mount("header=\"false\"", b);
        Click(root, 60, 9);
        Assert.Equal(new[] { 0 }, b.Picks);
    }

    [Theory]
    [InlineData("header=\"maybe\"")]
    public void Header_BadValueThrows(string attributes)
    {
        Assert.Throws<FormatException>(() => Mount(attributes, new Binding()));
    }

    [Theory]
    [InlineData("header=\"true\"")]
    [InlineData("resizable=\"true\"")]
    [InlineData("sortcolumn=\"{SortCol}\" sortdescending=\"{SortDesc}\"")]
    [InlineData("onsort=\"{SetSort}\"")]
    [InlineData("widths=\"{Widths}\"")]
    [InlineData("onresize=\"{SetWidths}\"")]
    public void Header_AttributesOnASingleColumnListThrow(string attributes)
    {
        string xml = $"""
            <panel x="0" y="0" w="400" h="300">
              <list x="0" y="0" w="300" h="108" items="{"{Names}"}" selected="{"{Selected}"}" {attributes}/>
            </panel>
            """;
        Assert.Throws<FormatException>(() => MarkupDocument.Build(xml, new Binding(), id => (id, 16, 16)));
    }
```

Close the class with `}` at the end of the file (Tasks 5 and 6 append tests before that brace).

- [ ] **Step 2: Run to see them fail** — build error: `UiMarkupListColumn` has no `HeaderSource`. (After Step 3 the column tests compile, and the header tests fail on layout.)

- [ ] **Step 3: Column caption** — in `UiMarkupListColumn`, after `ButtonHeight`:

```csharp
    /// <summary>The column's header caption (a literal or a bound string); null for none.</summary>
    public Func<string?>? HeaderSource { get; internal set; }
```

- [ ] **Step 4: The header partial** — create `src/AcDream.App/UI/UiMarkupList.Header.cs`:

```csharp
using System.Numerics;

namespace AcDream.App.UI;

public sealed partial class UiMarkupList
{
    /// <summary>The height of the header row a <c>header="true"</c> list draws above its rows.</summary>
    public const float HeaderHeight = 18f;

    /// <summary>Draws a row of column captions above the rows (the column form only).</summary>
    public bool ShowHeader { get; set; }

    private bool HeaderVisible => ShowHeader && _columns is { Count: > 0 };

    /// <summary>Where the rows and the scrollbar start: under the header when there is one.</summary>
    private float RowsTop => HeaderVisible ? HeaderHeight : 0f;

    private float RowsHeight => MathF.Max(0f, Height - RowsTop);

    // The header cell the left button went down on; -1 for none.
    private int _headerPressColumn = -1;

    /// <summary>
    /// The header: Classic draws the scrollbar track art under the captions,
    /// the plugin theme its field colour; both draw a border-coloured underline
    /// and a line between cells.
    /// </summary>
    private void DrawHeader(UiRenderContext context, IReadOnlyList<UiMarkupListColumn> columns)
    {
        if (ThemePalette is { } palette)
            context.DrawFill(0f, 0f, Width, HeaderHeight, palette.Field);
        else if (SpriteResolve is { } resolve)
            DrawTiledSprite(context, resolve, RetailScrollbarChrome.Track, 0f, 0f, Width, HeaderHeight);
        context.DrawFill(0f, HeaderHeight - 1f, Width, 1f, BorderColor);

        for (int c = 0; c < columns.Count; c++)
        {
            (float cellX, float cellW) = _cachedLayout[c];
            if (cellW <= 0f)
                continue;
            if (c < columns.Count - 1)
                context.DrawFill(cellX + cellW - 1f, 2f, 1f, HeaderHeight - 4f, BorderColor);
            DrawHeaderCell(context, columns[c], c, cellX, cellW);
        }
    }

    private void DrawHeaderCell(UiRenderContext context, UiMarkupListColumn column, int c, float cellX, float cellW)
    {
        string caption = column.HeaderSource?.Invoke() ?? string.Empty;
        context.PushClip(cellX, 0f, cellW, HeaderHeight);
        try
        {
            DrawCaption(context, caption, cellX + Padding, 0f, HeaderHeight, TextColor);
        }
        finally
        {
            context.PopClip();
        }
    }

    private void DrawCaption(UiRenderContext context, string text, float x, float top, float height, Vector4 color)
    {
        float textY = top + MathF.Max(0f, (height - (DatFont?.LineHeight ?? 14f)) * 0.5f);
        if (DatFont is { } font)
            context.DrawStringDat(font, text, x, textY, color, ThemePalette is null);
        else
            context.DrawString(text, x, textY, color);
    }

    private int HeaderColumnAt(float x)
    {
        if (_columns is not { } columns)
            return -1;
        for (int c = 0; c < columns.Count; c++)
        {
            (float cellX, float cellW) = _cachedLayout[c];
            if (x >= cellX && x < cellX + cellW)
                return c;
        }
        return -1;
    }

    /// <summary>
    /// Pointer input over the header. Returns true when it is consumed; moves,
    /// hovers and presses that began in the rows carry on to the rows.
    /// </summary>
    private bool HandleHeaderEvent(in UiEvent e, float contentWidth)
    {
        if (!HeaderVisible)
            return false;
        bool inHeader = e.Data2 >= 0 && e.Data2 < HeaderHeight;
        switch (e.Type)
        {
            case UiEventType.MouseDown:
                if (!inHeader)
                    return false;
                _headerPressColumn = Enabled ? HeaderColumnAt(e.Data1) : -1;
                return true;
            case UiEventType.MouseUp:
            {
                if (_headerPressColumn < 0)
                    return false;
                int pressed = _headerPressColumn;
                _headerPressColumn = -1;
                if (inHeader && Enabled && HeaderColumnAt(e.Data1) == pressed)
                    HeaderClicked(pressed);
                return true;
            }
            case UiEventType.CaptureChanged:
                _headerPressColumn = -1;
                return false;
            case UiEventType.Click:
            case UiEventType.DoubleClick:
                return inHeader;
        }
        return false;
    }

    /// <summary>A press and release on the same header cell.</summary>
    private void HeaderClicked(int column)
    {
    }
}
```

(`contentWidth` is unused until Task 6, which uses it for column drags.)

- [ ] **Step 5: Rows under the header** — in `UiMarkupList.cs`:
  - `VisibleRows` becomes `Math.Max(1, (int)MathF.Floor(RowsHeight / MathF.Max(1f, RowHeight)))`.
  - In `DrawColumns`, right after the background `DrawRectOutline`, add `if (HeaderVisible) DrawHeader(context, columns);`. The row `y` becomes `RowsTop + (view - _topRow) * RowHeight`.
  - In `OnEventColumns`, make this the first statement after `contentWidth` is computed:

```csharp
        if (HandleHeaderEvent(e, contentWidth))
            return true;
```

  - In the same method, the row becomes `int row = (int)MathF.Floor((e.Data2 - RowsTop) / MathF.Max(1f, RowHeight));` and, before that, `if (e.Data2 < RowsTop) return true;`.
  - In `ButtonCellAt`, add `if (localY < RowsTop) return (-1, -1);` at the top. The row becomes `(int)MathF.Floor((localY - RowsTop) / MathF.Max(1f, RowHeight))`. The `ButtonRect` call passes `RowsTop + row * RowHeight`.

- [ ] **Step 6: Scrollbar under the header** — in `DrawScrollbar`, `TryHandleScrollbarEvent` and `DragThumb`, measure the bar from `float top = RowsTop, height = RowsHeight;` instead of `0` and `Height`:
  - **Classic.** `decExtent = Math.Clamp(ScrollButtonExtent, 0f, height)` and `incExtent = Math.Clamp(ScrollButtonExtent, 0f, height - decExtent)`. Draw the Track at `(x, top, ScrollbarWidth, height)`, Up at `top`, and Down at `top + height - incExtent`. Then `trackTop = top + decExtent` and `trackLen = MathF.Max(0f, height - decExtent - incExtent)`.
  - **Theme.** Fill and outline `(x, top, ScrollbarWidth, height)`. Then `button = Math.Min(ScrollButtonExtent, height / 2)` and `ThumbRect(_scroll, top + button, Math.Max(0, height - 2 * button))`. The arrow ticks go at `top + button / 2` and `top + height - button / 2`.
  - **`TryHandleScrollbarEvent`.** Return false when `e.Data2 < top`. Compare `ly` with `top + decExtent` and `top + height - incExtent`. Use `trackTop = top + decExtent`.
  - **`DragThumb`.** Same `trackTop` and `trackLen` as Classic.

  The single-column form has `RowsTop == 0`, so it is unchanged.

- [ ] **Step 7: Builder** — create `src/AcDream.App/UI/Markup/MarkupListHeader.cs`:

```csharp
using System.Xml.Linq;

namespace AcDream.App.UI.Markup;

/// <summary>The header, sort and width attributes of a column <c>&lt;list&gt;</c>.</summary>
internal static class MarkupListHeader
{
    private static readonly string[] ListAttributes =
        ["header", "resizable", "sortcolumn", "sortdescending", "onsort", "widths", "onresize"];

    /// <summary>The single-column form has no header; its attributes there are mistakes.</summary>
    public static void RejectWithoutColumns(XElement el)
    {
        foreach (string name in ListAttributes)
        {
            if (el.Attribute(name) is not null)
                throw new FormatException($"<list {name}> needs <column> children");
        }
    }

    public static void Apply(
        XElement el, IReadOnlyList<XElement> columnEls, UiMarkupList list, object binding,
        MarkupBuildContext context)
    {
        list.ShowHeader = Bool(el, "header");
        IReadOnlyList<UiMarkupListColumn> columns = list.Columns!;
        for (int i = 0; i < columns.Count; i++)
            ApplyColumn(columnEls[i], columns[i], i, binding);
    }

    private static void ApplyColumn(XElement columnEl, UiMarkupListColumn column, int index, object binding)
    {
        string? header = (string?)columnEl.Attribute("header");
        if (header is not null)
            column.HeaderSource = MarkupDocument.BindString(header, binding);
    }

    internal static bool Bool(XElement el, string name) => (string?)el.Attribute(name) switch
    {
        null or "false" => false,
        "true" => true,
        var other => throw new FormatException($"<list {name}=\"{other}\"> must be true or false"),
    };
}
```

  In `MarkupDocument.AddElement`'s `case "list":`, right after `ApplyCommon(list, el, binding);`, add:

```csharp
                if (listUsesColumns)
                    MarkupListHeader.Apply(el, listChildren, list, binding, Context());
                else
                    MarkupListHeader.RejectWithoutColumns(el);
```

- [ ] **Step 8: Run** — the Verify command → PASS. If an existing scrollbar test fails, compare it against Step 6. The single-column and no-header paths must draw exactly as before.

- [ ] **Step 9: Commit**

```bash
git checkout -- '*.lock.json' 2>/dev/null
git add src/AcDream.App/UI/UiMarkupList.cs src/AcDream.App/UI/UiMarkupList.Header.cs \
        src/AcDream.App/UI/UiMarkupListColumn.cs src/AcDream.App/UI/Markup/MarkupListHeader.cs \
        src/AcDream.App/UI/MarkupDocument.cs tests/AcDream.App.Tests/UI/MarkupListHeaderTests.cs
git commit -m "feat(ui): header row for column markup lists

Claude-Session: https://claude.ai/code/session_01UzLnYkJ6ifzKGaeNwgsp8W"
```

---

### Task 5: Sorting

**Goal:** Sortable columns sort the view when their header is clicked (asc → desc → none), with a ▲/▼ glyph. The sort state is bound as a pair, remembered when unbound, and announced through `onsort`. Selection, callbacks and button presses all follow plugin rows.

**Files:**
- Create: `src/AcDream.App/UI/UiMarkupList.Sort.cs`
- Modify: `src/AcDream.App/UI/UiMarkupList.cs` (`RebuildView`), `src/AcDream.App/UI/UiMarkupList.Header.cs` (`HeaderClicked`, glyph), `src/AcDream.App/UI/UiMarkupListColumn.cs`, `src/AcDream.App/UI/Markup/MarkupListHeader.cs`
- Test: `tests/AcDream.App.Tests/UI/MarkupListHeaderTests.cs`

**Acceptance Criteria:**
- [ ] Sorting by each kind:
  - Clicking the Name header gives view order [3, 1, 0, 2]. Clicking again gives [2, 0, 1, 3]. A third click restores [0, 1, 2, 3].
  - Dist, sorted on `sortkeys`, gives [1, 3, 0, 2].
  - On, a bool sort, gives [1, 3, 0, 2] (stable).
  - A `sort="number"` column on `{Levels}` without keys gives [2, 0, 3, 1] ascending and [3, 0, 2, 1] descending (unparseable stays last).
- [ ] Clicking the unsortable button header does nothing. A press on one header released over another does nothing.
- [ ] Rows follow plugin data:
  - With `selectionband="true"` and `Selected=0`, the band moves to view row 2 (y 55) after the Name sort.
  - After the Name sort, a checkbox click at view row 0 calls `Toggle(3)`, and the button at view row 0 calls `Act(3)`.
- [ ] A button press is cancelled when the order changes before release: press at view row 0, set `Names[3] = "zzz"`, then release.
- [ ] Bound sort:
  - With `sortcolumn`/`sortdescending`/`onsort`, a Name click calls `SetSort(0, false)` and the view follows the property. Further clicks call `(0, true)`, then `(-1, false)`.
  - Without `onsort`, a click changes nothing.
  - The plugin setting `SortCol = 1` sorts by Dist.
- [ ] Unbound with `onsort="{NotifySort}"`: notified `(0, false)`.
- [ ] Remembered (named, unbound):
  - Clicks store `"0:asc"`, then `"0:desc"`, then remove the entry.
  - A stored `"1:desc"` applies after a tick or draw.
  - Stored `"9:asc"`, `"3:asc"` (unsortable) and `"0:sideways"` are ignored.
  - A character switch re-reads (real settings file).
- [ ] The glyph: four 1 px-high `TextColor` quads at the sorted header's right edge. Ascending widths are 1, 3, 5, 7 top to bottom; descending are 7, 5, 3, 1.
- [ ] Build throws `FormatException` for:
  - `sort="weird"`;
  - `sort="bool"` on a text column;
  - `sort="text"` on an icon column without `sortkeys`;
  - `sortkeys` with `sort="bool"`;
  - `sortkeys` without `sort`;
  - `sortkeys="{On}"` (a bool list);
  - `sortcolumn` without `sortdescending` and vice versa;
  - `onsort="{Toggle}"` (wrong type).

**Verify:** `dotnet test tests/AcDream.App.Tests/AcDream.App.Tests.csproj --filter "FullyQualifiedName~MarkupListHeaderTests|FullyQualifiedName~MarkupList|FullyQualifiedName~ListButtonColumn|FullyQualifiedName~UiMarkupList"` → all pass.

**Steps:**

- [ ] **Step 1: Write the failing tests** — append inside `MarkupListHeaderTests`:

```csharp
    private const int NameX = 60, DistX = 160, OnX = 220, ButtonX = 270, HeaderY = 9;

    [Fact]
    public void Sort_NameCyclesAscendingDescendingNone()
    {
        var b = new Binding();
        var (root, list) = Mount("", b);
        ClickAndDraw(root, list, NameX, HeaderY);
        Assert.Equal(new[] { 3, 1, 0, 2 }, ViewOrder(root, list, b));
        ClickAndDraw(root, list, NameX, HeaderY);
        Assert.Equal(new[] { 2, 0, 1, 3 }, ViewOrder(root, list, b));
        ClickAndDraw(root, list, NameX, HeaderY);
        Assert.Equal(new[] { 0, 1, 2, 3 }, ViewOrder(root, list, b));
    }

    [Fact]
    public void Sort_SortKeysAndBoolColumns()
    {
        var b = new Binding();
        var (root, list) = Mount("", b);
        ClickAndDraw(root, list, DistX, HeaderY);
        Assert.Equal(new[] { 1, 3, 0, 2 }, ViewOrder(root, list, b));
        ClickAndDraw(root, list, OnX, HeaderY);
        Assert.Equal(new[] { 1, 3, 0, 2 }, ViewOrder(root, list, b));
    }

    [Fact]
    public void Sort_NumberParsesCellsAndKeepsUnparseableLast()
    {
        var b = new Binding();
        string columns = """
              <column type="text" header="Name" width="120" items="{Names}"/>
              <column type="text" header="Lvl" width="80" items="{Levels}" sort="number"/>
              <column type="button" items="{Acts}" onclick="{Act}"/>
            """;
        var (root, list) = Mount("", b, columns: columns);
        ClickAndDraw(root, list, DistX, HeaderY);
        Assert.Equal(new[] { 2, 0, 3, 1 }, ViewOrder(root, list, b));
        ClickAndDraw(root, list, DistX, HeaderY);
        Assert.Equal(new[] { 3, 0, 2, 1 }, ViewOrder(root, list, b));
    }

    [Fact]
    public void Sort_UnsortableHeadersAndDraggedPressesDoNothing()
    {
        var b = new Binding();
        var (root, list) = Mount("", b);
        ClickAndDraw(root, list, ButtonX, HeaderY);
        Assert.Equal(new[] { 0, 1, 2, 3 }, ViewOrder(root, list, b));
        root.OnMouseDown(UiMouseButton.Left, NameX, HeaderY);
        root.OnMouseUp(UiMouseButton.Left, DistX, HeaderY);
        Redraw(list);
        Assert.Equal(new[] { 0, 1, 2, 3 }, ViewOrder(root, list, b));
    }

    [Fact]
    public void Sort_SelectionBandFollowsTheRow()
    {
        var b = new Binding { Selected = 0 };
        var (root, list) = Mount("selectionband=\"true\"", b);
        ClickAndDraw(root, list, NameX, HeaderY);
        var band = Assert.Single(MarkupDrawProbe.QuadBounds(MarkupDrawProbe.Draw(list), list.SelectedColor));
        Assert.Equal(18f + 36f + 1f, band.Y);
    }

    [Fact]
    public void Sort_CallbacksReceivePluginRows()
    {
        var b = new Binding();
        var (root, list) = Mount("", b);
        ClickAndDraw(root, list, NameX, HeaderY);
        Click(root, OnX, 27);
        Assert.Equal(new[] { 3 }, b.Toggled);
        Redraw(list);
        Click(root, ButtonX, 27);
        Assert.Equal(new[] { 3 }, b.Acted);
    }

    [Fact]
    public void Sort_APressIsCancelledWhenTheOrderChangesUnderIt()
    {
        var b = new Binding();
        var (root, list) = Mount("", b);
        ClickAndDraw(root, list, NameX, HeaderY);
        root.OnMouseMove(ButtonX, 27);
        root.OnMouseDown(UiMouseButton.Left, ButtonX, 27);
        b.Names[3] = "zzz";
        Redraw(list);
        root.OnMouseUp(UiMouseButton.Left, ButtonX, 27);
        Assert.Empty(b.Acted);
    }

    [Fact]
    public void Sort_BoundBelongsToThePlugin()
    {
        var b = new Binding();
        var (root, list) = Mount("sortcolumn=\"{SortCol}\" sortdescending=\"{SortDesc}\" onsort=\"{SetSort}\"", b);
        ClickAndDraw(root, list, NameX, HeaderY);
        Assert.Equal(new[] { 3, 1, 0, 2 }, ViewOrder(root, list, b));
        ClickAndDraw(root, list, NameX, HeaderY);
        ClickAndDraw(root, list, NameX, HeaderY);
        Assert.Equal(new[] { (0, false), (0, true), (-1, false) }, b.Sorts);
        b.SortCol = 1;
        Redraw(list);
        Assert.Equal(new[] { 1, 3, 0, 2 }, ViewOrder(root, list, b));
    }

    [Fact]
    public void Sort_BoundWithoutOnsortOnlyDisplays()
    {
        var b = new Binding();
        var (root, list) = Mount("sortcolumn=\"{SortCol}\" sortdescending=\"{SortDesc}\"", b);
        ClickAndDraw(root, list, NameX, HeaderY);
        Assert.Equal(new[] { 0, 1, 2, 3 }, ViewOrder(root, list, b));
    }

    [Fact]
    public void Sort_UnboundStillNotifies()
    {
        var b = new Binding();
        var (root, list) = Mount("onsort=\"{NotifySort}\"", b);
        ClickAndDraw(root, list, NameX, HeaderY);
        Assert.Equal(new[] { (0, false) }, b.Sorts);
    }

    [Fact]
    public void Sort_RemembersAndForgetsNone()
    {
        var state = new MarkupControlStateStore(null, () => "alice").ForWindow("win");
        var b = new Binding();
        var (root, list) = Mount("", b, state);
        ClickAndDraw(root, list, NameX, HeaderY);
        Assert.Equal("0:asc", state.Get("mobs", "sort"));
        ClickAndDraw(root, list, NameX, HeaderY);
        Assert.Equal("0:desc", state.Get("mobs", "sort"));
        ClickAndDraw(root, list, NameX, HeaderY);
        Assert.Null(state.Get("mobs", "sort"));
    }

    [Theory]
    [InlineData("1:desc", new[] { 2, 0, 3, 1 })]
    [InlineData("9:asc", new[] { 0, 1, 2, 3 })]
    [InlineData("3:asc", new[] { 0, 1, 2, 3 })]
    [InlineData("0:sideways", new[] { 0, 1, 2, 3 })]
    public void Sort_StoredValuesApplyOrAreIgnored(string stored, int[] order)
    {
        var state = new MarkupControlStateStore(null, () => "alice").ForWindow("win");
        state.Set("mobs", "sort", stored);
        var b = new Binding();
        var (root, list) = Mount("", b, state);
        Assert.Equal(order, ViewOrder(root, list, b));
    }

    [Fact]
    public void Sort_ACharacterSwitchReReads()
    {
        string character = "alice";
        var store = new MarkupControlStateStore(new SettingsStore(_settingsPath), () => character);
        var state = store.ForWindow("win");
        var b = new Binding();
        var (root, list) = Mount("", b, state);
        ClickAndDraw(root, list, NameX, HeaderY);
        store.Save();
        character = "bob";
        root.Tick(0.016, 0);
        Redraw(list);
        Assert.Equal(new[] { 0, 1, 2, 3 }, ViewOrder(root, list, b));
        character = "alice";
        root.Tick(0.016, 0);
        Redraw(list);
        Assert.Equal(new[] { 3, 1, 0, 2 }, ViewOrder(root, list, b));
    }

    [Fact]
    public void Sort_GlyphPointsUpThenDown()
    {
        var b = new Binding();
        var (root, list) = Mount("", b);
        ClickAndDraw(root, list, NameX, HeaderY);
        var up = MarkupDrawProbe.QuadBounds(MarkupDrawProbe.Draw(list), list.TextColor)
            .Where(q => q.H == 1f && q.X >= 110f && q.X < 120f).OrderBy(q => q.Y).Select(q => q.W).ToArray();
        Assert.Equal(new[] { 1f, 3f, 5f, 7f }, up);
        ClickAndDraw(root, list, NameX, HeaderY);
        var down = MarkupDrawProbe.QuadBounds(MarkupDrawProbe.Draw(list), list.TextColor)
            .Where(q => q.H == 1f && q.X >= 110f && q.X < 120f).OrderBy(q => q.Y).Select(q => q.W).ToArray();
        Assert.Equal(new[] { 7f, 5f, 3f, 1f }, down);
    }

    [Theory]
    [InlineData("""<column type="text" width="80" items="{Names}" sort="weird"/><column type="button" items="{Acts}" onclick="{Act}"/>""")]
    [InlineData("""<column type="text" width="80" items="{Names}" sort="bool"/><column type="button" items="{Acts}" onclick="{Act}"/>""")]
    [InlineData("""<column type="icon" width="20" values="{Ids}" onclick="{Act}" sort="text"/><column type="button" items="{Acts}" onclick="{Act}"/>""")]
    [InlineData("""<column type="checkbox" width="20" values="{On}" onchange="{Toggle}" sort="bool" sortkeys="{DistKeys}"/><column type="button" items="{Acts}" onclick="{Act}"/>""")]
    [InlineData("""<column type="text" width="80" items="{Names}" sortkeys="{DistKeys}"/><column type="button" items="{Acts}" onclick="{Act}"/>""")]
    [InlineData("""<column type="text" width="80" items="{Names}" sort="number" sortkeys="{On}"/><column type="button" items="{Acts}" onclick="{Act}"/>""")]
    public void Sort_BadColumnsThrow(string columns)
    {
        Assert.Throws<FormatException>(() => Mount("", new Binding(), columns: columns));
    }

    [Theory]
    [InlineData("sortcolumn=\"{SortCol}\"")]
    [InlineData("sortdescending=\"{SortDesc}\"")]
    [InlineData("onsort=\"{Toggle}\"")]
    public void Sort_BadListAttributesThrow(string attributes)
    {
        Assert.Throws<FormatException>(() => Mount(attributes, new Binding()));
    }
```

- [ ] **Step 2: Run to see them fail** — the sort tests fail (no sorting yet); the build-error theories fail because nothing throws.

- [ ] **Step 3: Column sort properties** — in `UiMarkupListColumn.cs`, add above the class:

```csharp
public enum UiMarkupListSortKind
{
    None,
    Text,
    Number,
    Bool,
}
```

and inside the class, after `HeaderSource`:

```csharp
    /// <summary>How a header click sorts by this column; None is not sortable.</summary>
    public UiMarkupListSortKind Sort { get; internal set; }

    /// <summary>Keys that replace the cell values when sorting (one of the two, or neither).</summary>
    public Func<IReadOnlyList<double>>? SortNumberKeys { get; internal set; }
    public Func<IReadOnlyList<string>>? SortTextKeys { get; internal set; }
```

- [ ] **Step 4: The sort partial** — create `src/AcDream.App/UI/UiMarkupList.Sort.cs`:

```csharp
using System.Globalization;
using AcDream.App.UI.Markup;

namespace AcDream.App.UI;

public sealed partial class UiMarkupList
{
    /// <summary>Bound sort column (−1 none); when set, the plugin owns the sort.</summary>
    public Func<int>? SortColumnSource { get; set; }

    /// <summary>Bound sort direction, read with <see cref="SortColumnSource"/>.</summary>
    public Func<bool>? SortDescendingSource { get; set; }

    /// <summary>Called with the sort a header click asks for: column (−1 none) and descending.</summary>
    public Action<int, bool>? SortChanged { get; set; }

    internal RememberedValue? RememberedSort { get; set; }

    private (int Column, bool Descending)? _hostSort;
    private string?[] _textKeys = Array.Empty<string?>();
    private double[] _numberKeys = Array.Empty<double>();

    /// <summary>The sort in force (bound, remembered or none); an unsortable or missing column is none.</summary>
    public (int Column, bool Descending) CurrentSort
    {
        get
        {
            (int column, bool descending) = SortColumnSource is { } source
                ? (source(), SortDescendingSource?.Invoke() ?? false)
                : HostSort;
            return IsSortable(column) ? (column, descending) : (-1, false);
        }
    }

    private (int Column, bool Descending) HostSort
    {
        get
        {
            if (RememberedSort is { } remembered && remembered.TryRefresh(out string? text))
                _hostSort = DecodeSort(text) ?? (-1, false);
            return _hostSort ??= (-1, false);
        }
    }

    private bool IsSortable(int column) =>
        _columns is { } columns && column >= 0 && column < columns.Count
        && columns[column].Sort != UiMarkupListSortKind.None;

    private static (int, bool)? DecodeSort(string? text)
    {
        if (text is null)
            return null;
        int colon = text.IndexOf(':');
        if (colon <= 0
            || !int.TryParse(text.AsSpan(0, colon), NumberStyles.None, CultureInfo.InvariantCulture, out int column))
            return null;
        return text[(colon + 1)..] switch
        {
            "asc" => (column, false),
            "desc" => (column, true),
            _ => null,
        };
    }

    private static string EncodeSort(int column, bool descending) =>
        column.ToString(CultureInfo.InvariantCulture) + (descending ? ":desc" : ":asc");

    /// <summary>A click on a sortable header: ascending, then descending, then no sort.</summary>
    private void CycleSort(int column)
    {
        if (!IsSortable(column))
            return;
        (int current, bool descending) = CurrentSort;
        (int Column, bool Descending) next = current != column
            ? (column, false)
            : !descending ? (column, true) : (-1, false);
        if (SortColumnSource is not null)
        {
            SortChanged?.Invoke(next.Column, next.Descending);
            return;
        }
        _hostSort = next;
        RememberedSort?.Remember(next.Column < 0 ? null : EncodeSort(next.Column, next.Descending));
        SortChanged?.Invoke(next.Column, next.Descending);
    }

    /// <summary>Orders the view by the sorted column's keys (the cells, or its sortkeys).</summary>
    private void SortView(IReadOnlyList<UiMarkupListColumn> columns, int rowCount)
    {
        (int sorted, bool descending) = CurrentSort;
        if (sorted < 0)
        {
            _view.SetIdentity(rowCount);
            return;
        }
        if (_textKeys.Length < rowCount)
            _textKeys = new string?[rowCount];
        if (_numberKeys.Length < rowCount)
            _numberKeys = new double[rowCount];

        UiMarkupListColumn column = columns[sorted];
        if (column.SortNumberKeys is { } numbers)
        {
            IReadOnlyList<double> keys = numbers();
            for (int row = 0; row < rowCount; row++)
                _numberKeys[row] = row < keys.Count ? keys[row] : double.NaN;
            _view.SortByNumber(_numberKeys, rowCount, descending);
            return;
        }
        if (column.Sort == UiMarkupListSortKind.Bool)
        {
            IReadOnlyList<bool>? flags = _cachedCheckRows[sorted];
            for (int row = 0; row < rowCount; row++)
                _numberKeys[row] = flags is not null && row < flags.Count ? (flags[row] ? 1d : 0d) : double.NaN;
            _view.SortByNumber(_numberKeys, rowCount, descending);
            return;
        }
        IReadOnlyList<string>? cells = column.SortTextKeys?.Invoke() ?? _cachedTextRows[sorted];
        if (column.Sort == UiMarkupListSortKind.Number)
        {
            for (int row = 0; row < rowCount; row++)
            {
                _numberKeys[row] = cells is not null && row < cells.Count
                    && double.TryParse(cells[row], NumberStyles.Float, CultureInfo.InvariantCulture, out double value)
                        ? value
                        : double.NaN;
            }
            _view.SortByNumber(_numberKeys, rowCount, descending);
            return;
        }
        for (int row = 0; row < rowCount; row++)
            _textKeys[row] = cells is not null && row < cells.Count ? cells[row] : null;
        _view.SortByText(_textKeys, rowCount, descending);
    }

    /// <summary>▲ (ascending) or ▼ at the right of a sorted header cell.</summary>
    private void DrawSortGlyph(UiRenderContext context, bool descending, float cellRight)
    {
        float left = cellRight - 10f;
        float top = MathF.Floor((HeaderHeight - 4f) * 0.5f);
        for (int r = 0; r < 4; r++)
        {
            float width = 1f + 2f * (descending ? 3 - r : r);
            context.DrawFill(left + (7f - width) * 0.5f, top + r, width, 1f, TextColor);
        }
    }
}
```

- [ ] **Step 5: Wire it in.**
  - In `UiMarkupList.cs`, `RebuildView`'s body becomes `SortView(columns, rowCount);`.
  - In `UiMarkupList.Header.cs`, `HeaderClicked`'s body becomes `CycleSort(column);`.
  - In `DrawHeader`, read `(int sorted, bool descending) = CurrentSort;` once before the loop, and pass both to `DrawHeaderCell`. Its signature becomes `(..., float cellW, bool sorted, bool descending)` with `c == sorted` for the `sorted` argument. In `DrawHeaderCell`, clip the caption to `MathF.Max(0f, cellW - (sorted ? 12f : 0f))` wide. After the caption's `finally`, add `if (sorted) DrawSortGlyph(context, descending, cellX + cellW);`.

- [ ] **Step 6: Builder** — in `MarkupListHeader`:
  - `Apply` gains, after `ShowHeader`:

```csharp
        string? sortColumn = (string?)el.Attribute("sortcolumn");
        string? sortDescending = (string?)el.Attribute("sortdescending");
        if ((sortColumn is null) != (sortDescending is null))
            throw new FormatException("<list sortcolumn> and <list sortdescending> are bound together or not at all");
        if (sortColumn is not null)
        {
            list.SortColumnSource = MarkupDocument.BindRequiredIntReader(sortColumn, binding, "list sortcolumn");
            list.SortDescendingSource = MarkupDocument.BindRequiredBoolReader(sortDescending, binding, "list sortdescending");
        }
        list.SortChanged = MarkupBindings.BindIntBoolAction((string?)el.Attribute("onsort"), binding, "list onsort");
        if (list.SortColumnSource is null)
            list.RememberedSort = new RememberedValue(context.ControlState, list.Name, "sort");
```

  - `ApplyColumn` gains, after the header caption:

```csharp
        UiMarkupListSortKind sort = (string?)columnEl.Attribute("sort") switch
        {
            null or "none" => UiMarkupListSortKind.None,
            "text" => UiMarkupListSortKind.Text,
            "number" => UiMarkupListSortKind.Number,
            "bool" => UiMarkupListSortKind.Bool,
            var other => throw new FormatException(
                $"column[{index}] sort=\"{other}\" must be text, number, bool or none"),
        };
        string? keys = (string?)columnEl.Attribute("sortkeys");
        if (keys is not null && sort is not (UiMarkupListSortKind.Text or UiMarkupListSortKind.Number))
            throw new FormatException($"column[{index}] sortkeys needs sort=\"text\" or sort=\"number\"");
        if (sort == UiMarkupListSortKind.Bool && column.Kind != UiMarkupListColumnKind.Check)
            throw new FormatException($"column[{index}] sort=\"bool\" needs a check or checkbox column");
        if (sort is UiMarkupListSortKind.Text or UiMarkupListSortKind.Number && keys is null
            && column.Kind is not (UiMarkupListColumnKind.Text or UiMarkupListColumnKind.Button))
        {
            throw new FormatException(
                $"column[{index}] sort=\"{columnEl.Attribute("sort")!.Value}\" on a {column.Kind} column needs sortkeys");
        }
        column.Sort = sort;
        if (keys is not null)
            (column.SortNumberKeys, column.SortTextKeys) =
                MarkupBindings.BindSortKeys(keys, binding, $"column[{index}] sortkeys");
```

  (`MarkupDocument.BindRequiredIntReader` and `BindRequiredBoolReader` are already `internal static`.)

- [ ] **Step 7: Run** — the Verify command → PASS.

- [ ] **Step 8: Commit**

```bash
git checkout -- '*.lock.json' 2>/dev/null
git add src/AcDream.App/UI tests/AcDream.App.Tests/UI/MarkupListHeaderTests.cs
git commit -m "feat(ui): sort markup lists by a header click

Bound as sortcolumn/sortdescending with onsort, or remembered per
character. Every row index a plugin sees stays a plugin row.

Claude-Session: https://claude.ai/code/session_01UzLnYkJ6ifzKGaeNwgsp8W"
```

---

### Task 6: Resizable columns

**Goal:** With `resizable="true"`, dragging a header boundary resizes the column to its left (at least 16 px), and double-clicking a boundary restores the markup width. A resize cursor shows over boundaries. Widths are bound (`widths`/`onresize`) or remembered.

**Files:**
- Create: `src/AcDream.App/UI/UiMarkupList.Widths.cs`
- Modify: `src/AcDream.App/UI/UiMarkupList.cs` (`Columns` setter, `ComputeColumnLayout`, `ReceivesHoverMouseMove`), `src/AcDream.App/UI/UiMarkupList.Header.cs` (`HandleHeaderEvent`), `src/AcDream.App/UI/Markup/MarkupListHeader.cs`
- Test: `tests/AcDream.App.Tests/UI/MarkupListHeaderTests.cs`

**Acceptance Criteria:**
- [ ] Dragging the Name|Dist boundary from x 120 to 150 gives Name `(0, 150)` and Dist `(150, 80)` after a draw. Dragging to x 0 stops Name at 16. The press-and-release does not sort.
- [ ] Double-clicking the boundary at its current position restores Name to 120.
- [ ] `onresize` notifications:
  - Unbound `onresize="{NotifyWidths}"` receives `[150, 80, 40, 0]` once for a one-move drag.
  - Bound `widths`/`onresize` calls `SetWidths([150, 80, 40, 0])`, and the layout follows the property.
  - Bound without `onresize` changes nothing.
  - A bound list of the wrong length lays out at the markup widths.
- [ ] Remembered (named, unbound):
  - A drag stores `"150,80,40,0"`, and a reset removes it.
  - A stored `"100,80,40,0"` lays Name out at 100.
  - Stored `"100,80"` and `"a,b,c,d"` are ignored.
- [ ] Cursor:
  - Hovering within 3 px of a boundary gives `ActiveCursor().File == 0x06006128`; hovering elsewhere gives no valid cursor.
  - A list that isn't resizable never shows it.
  - A disabled list ignores drags and shows no resize cursor.
- [ ] The build throws for:
  - `resizable="true"` without `header="true"`;
  - `widths` or `onresize` without `resizable`;
  - `widths="{Names}"` (not floats);
  - `onresize="{Toggle}"`.

**Verify:** `dotnet test tests/AcDream.App.Tests/AcDream.App.Tests.csproj --filter "FullyQualifiedName~MarkupListHeaderTests|FullyQualifiedName~MarkupList|FullyQualifiedName~ListButtonColumn|FullyQualifiedName~UiMarkupList"` → all pass.

**Steps:**

- [ ] **Step 1: Write the failing tests** — append inside `MarkupListHeaderTests`:

```csharp
    private static void DragHeader(UiRoot root, UiMarkupList list, int fromX, int toX)
    {
        root.OnMouseDown(UiMouseButton.Left, fromX, HeaderY);
        root.OnMouseMove(toX, HeaderY);
        root.OnMouseUp(UiMouseButton.Left, toX, HeaderY);
        Redraw(list);
    }

    [Fact]
    public void Resize_DragsTheColumnLeftOfTheBoundary()
    {
        var b = new Binding();
        var (root, list) = Mount("resizable=\"true\"", b);
        DragHeader(root, list, 120, 150);
        Assert.Equal((0f, 150f), list.ColumnLayoutForTest(0));
        Assert.Equal((150f, 80f), list.ColumnLayoutForTest(1));
        Assert.Equal(new[] { 0, 1, 2, 3 }, ViewOrder(root, list, b));   // no sort
        DragHeader(root, list, 150, 0);
        Assert.Equal(UiMarkupList.MinColumnWidth, list.ColumnLayoutForTest(0).W);
    }

    [Fact]
    public void Resize_DoubleClickRestoresTheMarkupWidth()
    {
        var (root, list) = Mount("resizable=\"true\"", new Binding());
        DragHeader(root, list, 120, 150);
        ClickAndDraw(root, list, 150, HeaderY);
        ClickAndDraw(root, list, 150, HeaderY);
        Assert.Equal((0f, 120f), list.ColumnLayoutForTest(0));
    }

    [Fact]
    public void Resize_UnboundNotifiesEachWidth()
    {
        var b = new Binding();
        var (root, list) = Mount("resizable=\"true\" onresize=\"{NotifyWidths}\"", b);
        DragHeader(root, list, 120, 150);
        var widths = Assert.Single(b.Resizes);
        Assert.Equal(new[] { 150f, 80f, 40f, 0f }, widths);
    }

    [Fact]
    public void Resize_BoundWidthsBelongToThePlugin()
    {
        var b = new Binding();
        var (root, list) = Mount("resizable=\"true\" widths=\"{Widths}\" onresize=\"{SetWidths}\"", b);
        DragHeader(root, list, 120, 150);
        Assert.Equal(new[] { 150f, 80f, 40f, 0f }, Assert.Single(b.Resizes));
        Assert.Equal((0f, 150f), list.ColumnLayoutForTest(0));
        b.Widths = new() { 60f, 80f, 40f, 0f };
        Redraw(list);
        Assert.Equal((0f, 60f), list.ColumnLayoutForTest(0));
        b.Widths = new() { 60f };
        Redraw(list);
        Assert.Equal((0f, 120f), list.ColumnLayoutForTest(0));
    }

    [Fact]
    public void Resize_BoundWithoutOnresizeOnlyDisplays()
    {
        var b = new Binding();
        var (root, list) = Mount("resizable=\"true\" widths=\"{Widths}\"", b);
        DragHeader(root, list, 120, 150);
        Assert.Equal((0f, 120f), list.ColumnLayoutForTest(0));
    }

    [Fact]
    public void Resize_RemembersAndForgetsTheMarkupWidths()
    {
        var state = new MarkupControlStateStore(null, () => "alice").ForWindow("win");
        var (root, list) = Mount("resizable=\"true\"", new Binding(), state);
        DragHeader(root, list, 120, 150);
        Assert.Equal("150,80,40,0", state.Get("mobs", "widths"));
        ClickAndDraw(root, list, 150, HeaderY);
        ClickAndDraw(root, list, 150, HeaderY);
        Assert.Null(state.Get("mobs", "widths"));
    }

    [Theory]
    [InlineData("100,80,40,0", 100f)]
    [InlineData("100,80", 120f)]
    [InlineData("a,b,c,d", 120f)]
    public void Resize_StoredWidthsApplyOrAreIgnored(string stored, float nameWidth)
    {
        var state = new MarkupControlStateStore(null, () => "alice").ForWindow("win");
        state.Set("mobs", "widths", stored);
        var (_, list) = Mount("resizable=\"true\"", new Binding(), state);
        Redraw(list);
        Assert.Equal(nameWidth, list.ColumnLayoutForTest(0).W);
    }

    [Fact]
    public void Resize_CursorShowsOverBoundariesOnly()
    {
        var (root, list) = Mount("resizable=\"true\"", new Binding());
        root.OnMouseMove(122, HeaderY);
        Assert.Equal(0x06006128u, list.ActiveCursor().File);
        root.OnMouseMove(60, HeaderY);
        Assert.False(list.ActiveCursor().IsValid);

        var (plainRoot, plain) = Mount("", new Binding());
        plainRoot.OnMouseMove(120, HeaderY);
        Assert.False(plain.ActiveCursor().IsValid);
    }

    [Fact]
    public void Resize_DisabledIgnoresDragsAndShowsNoCursor()
    {
        var (root, list) = Mount("resizable=\"true\" enabled=\"false\"", new Binding());
        list.OnEvent(new UiEvent { Type = UiEventType.MouseDown, Data1 = 120, Data2 = HeaderY });
        list.OnEvent(new UiEvent { Type = UiEventType.MouseMove, Data1 = 150, Data2 = HeaderY });
        list.OnEvent(new UiEvent { Type = UiEventType.MouseUp, Data1 = 150, Data2 = HeaderY });
        Redraw(list);
        Assert.Equal((0f, 120f), list.ColumnLayoutForTest(0));
        list.OnEvent(new UiEvent { Type = UiEventType.HoverEnter, Data1 = 120, Data2 = HeaderY });
        Assert.False(list.ActiveCursor().IsValid);
    }

    [Theory]
    [InlineData("header=\"false\" resizable=\"true\"")]
    [InlineData("widths=\"{Widths}\"")]
    [InlineData("onresize=\"{SetWidths}\"")]
    [InlineData("resizable=\"true\" widths=\"{Names}\"")]
    [InlineData("resizable=\"true\" onresize=\"{Toggle}\"")]
    public void Resize_BadAttributesThrow(string attributes)
    {
        Assert.Throws<FormatException>(() => Mount(attributes, new Binding()));
    }
```


- [ ] **Step 2: Run to see them fail** — build error: there is no `ColumnLayoutForTest` or `MinColumnWidth`.

- [ ] **Step 3: The widths partial** — create `src/AcDream.App/UI/UiMarkupList.Widths.cs`:

```csharp
using System.Globalization;
using AcDream.App.UI.Markup;

namespace AcDream.App.UI;

public sealed partial class UiMarkupList
{
    /// <summary>The narrowest a player can drag a column.</summary>
    public const float MinColumnWidth = 16f;

    // How far either side of a header boundary a press grabs it.
    private const float ResizeGrip = 3f;

    private const string ResizeCursorState = "resize";

    private static readonly IReadOnlyDictionary<string, UiCursorMedia> ResizeCursors = MakeResizeCursors();
    private static readonly IReadOnlyDictionary<string, UiCursorMedia> NoCursors =
        new Dictionary<string, UiCursorMedia>();

    private static IReadOnlyDictionary<string, UiCursorMedia> MakeResizeCursors()
    {
        RetailCursorCatalog.TryGetWindowControlCursor(CursorFeedbackKind.ResizeHorizontal, out UiCursorMedia cursor);
        return new Dictionary<string, UiCursorMedia> { [ResizeCursorState] = cursor };
    }

    /// <summary>Header boundaries can be dragged to resize the column to their left.</summary>
    public bool Resizable
    {
        get => _resizable;
        set
        {
            _resizable = value;
            SetStateCursors(value ? ResizeCursors : NoCursors);
        }
    }

    /// <summary>Bound widths, one per column (0 = automatic); when set, the plugin owns them.</summary>
    public Func<IReadOnlyList<float>>? WidthsSource { get; set; }

    /// <summary>Called with every column's width each time a drag or reset asks for a new one.</summary>
    public Action<IReadOnlyList<float>>? WidthsChanged { get; set; }

    internal RememberedValue? RememberedWidths { get; set; }

    private bool _resizable;

    // The markup's widths, one per column: 0 for automatic (`*`, and always the last).
    private float[] _markupWidths = Array.Empty<float>();
    private float[]? _hostWidths;
    private float[]? _askedWidths;
    private float[]? _resizeStartWidths;
    private int _resizeColumn = -1;
    private int _hoverBoundary = -1;
    private float _resizeStartWidth;
    private float _resizePressX;

    public override string ActiveCursorStateName =>
        Enabled && (_resizeColumn >= 0 || _hoverBoundary >= 0) ? ResizeCursorState : "";

    internal (float X, float W) ColumnLayoutForTest(int column) => _cachedLayout[column];

    /// <summary>Every column's width as asked for: bound, remembered or the markup's (0 = automatic).</summary>
    private IReadOnlyList<float> WidthState
    {
        get
        {
            if (WidthsSource is { } source)
            {
                IReadOnlyList<float> bound = source();
                return bound.Count == _markupWidths.Length ? bound : _markupWidths;
            }
            if (RememberedWidths is { } remembered && remembered.TryRefresh(out string? text))
                _hostWidths = DecodeWidths(text);
            return _hostWidths ?? (IReadOnlyList<float>)_markupWidths;
        }
    }

    private static string EncodeWidths(IReadOnlyList<float> widths) =>
        string.Join(",", widths.Select(w => ((int)MathF.Round(w)).ToString(CultureInfo.InvariantCulture)));

    private float[]? DecodeWidths(string? text)
    {
        if (text is null)
            return null;
        string[] parts = text.Split(',');
        if (parts.Length != _markupWidths.Length)
            return null;
        var widths = new float[parts.Length];
        for (int i = 0; i < parts.Length; i++)
        {
            if (!int.TryParse(parts[i], NumberStyles.None, CultureInfo.InvariantCulture, out int width))
                return null;
            widths[i] = width;
        }
        return widths;
    }

    /// <summary>The boundary to the right of a column within grabbing distance; -1 for none.</summary>
    private int BoundaryAt(float x, float y)
    {
        if (!_resizable || !HeaderVisible || y < 0f || y >= HeaderHeight || _columns is not { } columns)
            return -1;
        for (int c = 0; c < columns.Count - 1; c++)
        {
            (float cellX, float cellW) = _cachedLayout[c];
            if (MathF.Abs(x - (cellX + cellW)) <= ResizeGrip)
                return c;
        }
        return -1;
    }

    private void BeginResize(int column, float x)
    {
        _resizeColumn = column;
        _resizePressX = x;
        _resizeStartWidth = _cachedLayout[column].w;
        _resizeStartWidths = WidthState.ToArray();
        _askedWidths = _resizeStartWidths;
    }

    private void DragResize(float x, float contentWidth)
    {
        (float cellX, _) = _cachedLayout[_resizeColumn];
        float max = MathF.Max(MinColumnWidth, contentWidth - cellX - MinColumnWidth);
        float width = Math.Clamp(MathF.Round(_resizeStartWidth + x - _resizePressX), MinColumnWidth, max);
        AskWidth(_resizeColumn, width);
    }

    /// <summary>Asks for one column's width; each distinct set of widths is reported once.</summary>
    private void AskWidth(int column, float width)
    {
        if (WidthsSource is not null && WidthsChanged is null)
            return;
        float[] next = (_askedWidths ?? WidthState).ToArray();
        if (next[column] == width)
            return;
        next[column] = width;
        _askedWidths = next;
        if (WidthsSource is null)
            _hostWidths = next;
        WidthsChanged?.Invoke(next);
    }

    private void EndResize()
    {
        if (_resizeColumn < 0)
            return;
        _resizeColumn = -1;
        CommitWidths(_resizeStartWidths);
        _askedWidths = null;
        _resizeStartWidths = null;
    }

    /// <summary>Remembers an unbound change once the player commits it.</summary>
    private void CommitWidths(float[]? before)
    {
        if (WidthsSource is not null || _askedWidths is null
            || (before is not null && before.AsSpan().SequenceEqual(_askedWidths)))
            return;
        string encoded = EncodeWidths(_askedWidths);
        RememberedWidths?.Remember(encoded == EncodeWidths(_markupWidths) ? null : encoded);
    }

    /// <summary>A double-click on a boundary: the column to its left goes back to its markup width.</summary>
    private void ResetWidth(int column)
    {
        float[] before = WidthState.ToArray();
        _askedWidths = before;
        AskWidth(column, _markupWidths[column]);
        CommitWidths(before);
        _askedWidths = null;
    }
}
```

- [ ] **Step 4: Layout from the width state** — in `UiMarkupList.cs`:
  - In the `Columns` setter, after `_cachedLayout = …`, add:

```csharp
            _markupWidths = new float[count];
            for (int i = 0; i < count; i++)
                _markupWidths[i] = i == count - 1 || value![i].IsAutoWidth ? 0f : value[i].Width;
            _hostWidths = null;
```

  - In `ComputeColumnLayout`, read `IReadOnlyList<float> widths = WidthState;` once at the top. In the first loop, `bool auto = last || columns[i].IsAutoWidth;` becomes `bool auto = last || !(widths[i] > 0f);`, and `columns[i].Width` becomes `widths[i]`. (With no resizing, `WidthState` is `_markupWidths`, which reproduces today's layout exactly.)
  - `ReceivesHoverMouseMove` becomes `=> _hasButtons || _resizable;`.

- [ ] **Step 5: Header input** — in `HandleHeaderEvent` (`UiMarkupList.Header.cs`):
  - At the very top, before `if (!HeaderVisible)`, add:

```csharp
        if (_resizeColumn >= 0)
        {
            switch (e.Type)
            {
                case UiEventType.MouseMove:
                    DragResize(e.Data1, contentWidth);
                    return true;
                case UiEventType.MouseUp:
                    EndResize();
                    return true;
                case UiEventType.CaptureChanged:
                    EndResize();
                    break;
            }
        }
```

  - In the switch, add these cases, and put the boundary press before the header-column press:

```csharp
            case UiEventType.HoverEnter:
            case UiEventType.MouseMove:
                _hoverBoundary = BoundaryAt(e.Data1, e.Data2);
                return false;
            case UiEventType.HoverLeave:
                _hoverBoundary = -1;
                return false;
```

  - `MouseDown` becomes:

```csharp
            case UiEventType.MouseDown:
                if (!inHeader)
                    return false;
                if (Enabled && BoundaryAt(e.Data1, e.Data2) is var boundary and >= 0)
                {
                    BeginResize(boundary, e.Data1);
                    return true;
                }
                _headerPressColumn = Enabled ? HeaderColumnAt(e.Data1) : -1;
                return true;
```

  - `DoubleClick` becomes its own case:

```csharp
            case UiEventType.DoubleClick:
                if (inHeader && Enabled && BoundaryAt(e.Data1, e.Data2) is var reset and >= 0)
                    ResetWidth(reset);
                return inHeader;
```

  (Keep `Click` returning `inHeader`.)

- [ ] **Step 6: Builder** — in `MarkupListHeader.Apply`, after the sort lines:

```csharp
        list.Resizable = Bool(el, "resizable");
        if (list.Resizable && !list.ShowHeader)
            throw new FormatException("<list resizable=\"true\"> needs header=\"true\"");
        string? widths = (string?)el.Attribute("widths");
        string? onResize = (string?)el.Attribute("onresize");
        if (!list.Resizable && (widths is not null || onResize is not null))
            throw new FormatException("<list widths> and <list onresize> need resizable=\"true\"");
        if (widths is not null)
            list.WidthsSource = MarkupBindings.BindRequiredFloatListReader(widths, binding, "list widths");
        list.WidthsChanged = MarkupBindings.BindFloatListAction(onResize, binding, "list onresize");
        if (list.Resizable && list.WidthsSource is null)
            list.RememberedWidths = new RememberedValue(context.ControlState, list.Name, "widths");
```

- [ ] **Step 7: Run** — the Verify command → PASS. A double-click in tests depends on two clicks within 500 ms of real time at the same spot (`UiRoot.DoubleClickDelayMs`). `ClickAndDraw` twice satisfies that.

- [ ] **Step 8: Commit**

```bash
git checkout -- '*.lock.json' 2>/dev/null
git add src/AcDream.App/UI tests/AcDream.App.Tests/UI/MarkupListHeaderTests.cs
git commit -m "feat(ui): resizable markup list columns

Drag a header boundary, double-click to reset. Widths are bound with
widths/onresize or remembered per character.

Claude-Session: https://claude.ai/code/session_01UzLnYkJ6ifzKGaeNwgsp8W"
```

---

### Task 7: Docs

**Goal:** `docs/plugin-ui-markup.md` documents the header, sorting and resizable columns.

**Files:**
- Modify: `docs/plugin-ui-markup.md`

**Acceptance Criteria:**
- [ ] The Elements table `list` row gains `header resizable sortcolumn sortdescending onsort widths onresize`, noted as column form only.
- [ ] The Bindings table gains rows:
  - `list sortcolumn` (`int`, −1 none) and `list sortdescending` (`bool`), bound together;
  - `list onsort` (`Action<int, bool>`);
  - `list widths` (`IReadOnlyList<float>`, 0 = automatic);
  - `list onresize` (`Action<IReadOnlyList<float>>`).
- [ ] The Columns attribute table gains rows:
  - `header` (all; literal or bound string);
  - `sort` (`text`/`number` for text and button columns or with `sortkeys`, `bool` for check/checkbox);
  - `sortkeys` (`IReadOnlyList<double>` or `IReadOnlyList<string>`).
- [ ] The "There is no header row; place `<label>` elements above the list" sentence is replaced. A `### Header, sorting and column widths` subsection at the end of `## Columns` covers:
  - **Header:** 18 px; rows and the scrollbar start under it; both looks.
  - **Sorting:** the click cycle, key rules, stability, unparseable last, and that every index stays a plugin row. Selection follows the row, and a sort cancels a pending button press.
  - **Sort state:** bound pair vs remembered.
  - **Widths:** dragging (≥16 px), double-click reset, 0 = automatic, and the last entry ignored. Bound vs remembered; a mismatched count is ignored; `onresize` fires per pixel during a drag.
  - **Build errors.**
  - **The older-host line:** a host older than list headers ignores the attributes. Check how the earlier sections phrase it. These are attributes on an existing element, so say that an older host draws the list without a header and ignores sorting and widths.

**Verify:** `grep -c 'sortkeys\|onsort\|onresize' docs/plugin-ui-markup.md` → at least 6.

**Steps:**

- [ ] **Step 1: Tables** — edit the three tables as listed in the Acceptance Criteria. Keep each table's column layout and wording style.

- [ ] **Step 2: Section** — replace the "There is no header row…" sentence with "With `header="true"` a list draws a header row; see below." Then append, before `## Slider range`:

````markdown
### Header, sorting and column widths

```xml
<list name="mobs" x="8" y="24" w="320" h="200" header="true" resizable="true"
      selected="{Sel}" onchange="{Pick}">
  <column type="text" header="Name" width="140" items="{Names}" sort="text"/>
  <column type="text" header="Dist" width="80" items="{Dists}" sort="number" sortkeys="{DistKeys}"/>
  <column type="checkbox" header="On" width="40" values="{On}" onchange="{Toggle}" sort="bool"/>
  <column type="button" items="{Acts}" onclick="{Act}"/>
</list>
```

`header="true"` draws an 18 px row of column captions above the rows; the
rows and the scrollbar start under it. A column's `header` is a literal or a
bound string. Classic draws the header on the game's scrollbar track art; a
themed window uses its field colour. Both draw a line under the header and
between cells.

A column with `sort` can be sorted by clicking its header: the first click
sorts ascending, the second descending, the third goes back to the plugin's
order. The sorted column shows ▲ or ▼. `text` compares the cells ignoring
case; `number` reads them as numbers (invariant culture) and puts cells that
aren't numbers last; `bool` sorts a check or checkbox column unticked first.
`sortkeys` replaces the cells as the keys, for a column that shows something
other than what it sorts by, such as "12.3 m". Ties keep the plugin's order.

Sorting only changes what is drawn where. Every row index the list hands
the plugin (`selected`, `onchange`, every column callback) is still the
plugin's own index, the selection stays on the same row, and a button press
in progress is cancelled if a sort moves the rows before the release.

- Without `sortcolumn` the client keeps the sort and, for a list with a
  `name`, remembers it per character; `onsort` is told each change.
- With `sortcolumn="{Int}" sortdescending="{Bool}"` (always both) the plugin
  keeps it: a header click only calls `onsort` with the column (−1 for none)
  and direction asked for. Without `onsort` it only displays.

With `resizable="true"` (which needs a header) the player drags the line
between two header cells to resize the column to its left, down to 16 px,
and double-clicks it to put the column back to its markup width. The resize
cursor shows over those lines.

- Without `widths` the client keeps the widths and, for a named list,
  remembers them per character when the player lets go; `onresize` is told
  every column's width for each pixel the line moves.
- With `widths="{Floats}"` the plugin keeps them: one entry per column in
  pixels, 0 for automatic, the last entry ignored (the last column always
  takes the rest). A list of the wrong length is ignored. A drag only calls
  `onresize`; without it the widths only display.

`header`, `resizable`, `sortcolumn`, `sortdescending`, `onsort`, `widths`
and `onresize` need `<column>` children. `resizable` without a header,
`widths` or `onresize` without `resizable`, `sort="bool"` on a column that
isn't a check or checkbox, and `sort="text"` or `"number"` on an icon
column without `sortkeys` fail the build. On a host older than list headers
the list draws without a header, and sorting and widths are ignored.
````

Re-check every sentence against the code in `UiMarkupList.Sort.cs`, `UiMarkupList.Widths.cs` and `MarkupListHeader.cs`. Older hosts: verify that an older host really ignores unknown `<list>` and `<column>` attributes rather than failing. Read `MarkupDocument`'s list case on base 391e709a (`git show 391e709a:src/AcDream.App/UI/MarkupDocument.cs`). If it ignores them, keep the sentence; if not, say what happens instead.

- [ ] **Step 3: Commit**

```bash
git add docs/plugin-ui-markup.md
git commit -m "docs(ui): list headers, sorting and resizable columns

Claude-Session: https://claude.ai/code/session_01UzLnYkJ6ifzKGaeNwgsp8W"
```

---

### Task 8: Series gate

**Goal:** Release build clean; no new failures in App, Plugin or UI.Abstractions tests; every commit builds.

**Files:** none changed.

**Acceptance Criteria:**
- [ ] `dotnet build AcDream.slnx -c Release` → 0 errors, 0 warnings.
- [ ] App + Plugin failures ⊆ `mc-base.txt`; UI.Abstractions failures ⊆ `mc-uiabs-base.txt` (empty).
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
done | norm > $T/mc7-after.txt
dotnet test tests/AcDream.UI.Abstractions.Tests/AcDream.UI.Abstractions.Tests.csproj -c Release 2>&1 | norm > $T/mc7-uiabs-after.txt
echo "new App/Plugin:"; comm -13 $T/mc-base.txt $T/mc7-after.txt
echo "new UI.Abstractions:"; comm -13 $T/mc-uiabs-base.txt $T/mc7-uiabs-after.txt
git checkout -- '*.lock.json' 2>/dev/null; git status --short
```
Expected: `0 Warning(s)`, `0 Error(s)`, nothing after either "new" line.

- [ ] **Step 2: Each commit builds alone**

```bash
cd /Users/davidsmith/code/OpenAC/OpenAC
for c in $(git rev-list --reverse 5561afcb..feat/markup-controls); do
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
git log --oneline 5561afcb..HEAD
git diff --stat 391e709a..HEAD | grep superpowers || echo "no superpowers docs"
```
Expected: the 7 commits of Tasks 1-7 (plus any review-fix commits); "no superpowers docs".

No commit.
