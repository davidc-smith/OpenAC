# Layout upgrade plan 2: `<repeat>`

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers-extended-cc:subagent-driven-development (recommended) or superpowers-extended-cc:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Plugin markup gets a `<repeat items="{List}" key="{Id}">` element. It builds its template once per list item inside a flex container, binds each copy to its item, and keeps copies (with their client state) by key when the list changes. This is Aethrix api-gaps #78.

**Architecture:** Bindings stop capturing `object binding`. Each helper resolves through a `MarkupBindingSource` (a type to look names up on plus a current object), so a template can be checked against the list's element type with no item, and a kept copy can follow its key to a new item object. A zero-size marker element (`UiMarkupRepeat`) sits where `<repeat>` was written in the flex container. Each tick it reads the list and reconciles the copies, whose elements are ordinary items of the surrounding container placed right after the marker. Theme actions a copy registers are scoped and dropped with it. Remembered state inside a copy is keyed `name#keypath`.

**Tech Stack:** C# / .NET 10 (SDK 10.0.300 in `~/.dotnet`), xUnit, the retained UI in `src/AcDream.App/UI`.

**Spec:** `docs/superpowers/specs/2026-10-22-layout-upgrade-design.md` Part 2 (2a–2c, all agreed), on branch `docs/layout-upgrade-spec`. The request page lists rules R1–R11: https://claude.ai/artifact/3aKQAURCfwswFEDoFKWw3K. They are restated below where they bind a task.

## Global Constraints

- Work in `/Users/davidsmith/code/OpenAC/OpenAC/.worktrees/markup-repeat`, on branch `feat/markup-repeat`, which starts at ba1f2693 (the tip of `feat/plugin-flex-layout`). That branch is upstream-bound, so nothing fork-only (no `PluginUiStyle`, no markup canvas, no fork dock code). Tasks 1–6 never touch `main`, `upstream-main`, `feat/plugin-flex-layout` or `feat/plugin-controls-upgrade`.
- No `docs/superpowers/**` files on `feat/markup-repeat`.
- Every commit builds `AcDream.slnx -c Release` with 0 warnings outside `external/`, and its failing test names are a subset of `baseline-1a777f1a-failures.txt` (335 names; the flex branch matches it). Gate, from anywhere:

```bash
OUT=/private/tmp/claude-501/-Users-davidsmith-code-OpenAC-OpenAC/ee434c69-cb5a-4da7-98e6-8c90bc95af3b/scratchpad/gate
NOTES=/Users/davidsmith/code/OpenAC/OpenAC/.worktrees/layout-upgrade-spec/docs/superpowers/plans/2026-10-22-layout-1-flex-port
GATE_OUT=$OUT zsh $NOTES/gate.sh /Users/davidsmith/code/OpenAC/OpenAC/.worktrees/markup-repeat repeatN
comm -13 $NOTES/baseline-1a777f1a-failures.txt $OUT/repeatN-failures.txt   # must print nothing
cat $OUT/repeatN-warnings.txt                                               # must print nothing
cd /Users/davidsmith/code/OpenAC/OpenAC/.worktrees/markup-repeat && git grep -n -E 'PluginUiStyle|ModernTitleFont|UiMarkupCanvas|CollectCanvases|OnDrawingChild|UiPointerState|DrawThemedPlain|FillRoundedRect|DrawSmoothLine|BundledUiFontWeight|RetainedWheel' -- src tests samples   # must print nothing
```

  The full gate takes about 1–2 minutes. While iterating, run only the relevant classes: `DOTNET_ROOT=$HOME/.dotnet PATH=$HOME/.dotnet:$PATH dotnet test tests/AcDream.App.Tests -c Release --filter "FullyQualifiedName~<Class>"`.
- Don't commit lock-file churn (`git checkout -- '*.lock.json'`; the gate does it).
- Commit messages follow the conventional style of this branch and end with `Claude-Session: https://claude.ai/code/session_01C83Twxs4TYLEbsJtnPsN4a`.
- Code reads like the surrounding code. Doc comments are short and say what a thing is for.
- Tests are TDD: write each new test first, see it fail for the right reason, then implement. For each new behaviour, break the line it pins once and see the test fail.

**User decisions (already made):**
- Spec 2a: a binding source in place of `object binding`, landing as its own no-behaviour-change commit.
- Spec 2b: copies go straight into the surrounding flex container after a zero-size marker (R6). Copies are kept by key and moved, not rebuilt (R5). Everything a removed copy registered is released.
- Remembered state inside a repeat is per item, stored as `name#keypath`. A named element inside a repeat with no `key` fails the build (R8).
- Plugin test support is docs only: the docs state the scope rule, and there is no public validator API.
- `<repeat>` goes upstream as its own PR stacked on the flex PR. Fork `main` takes it by merge, with the next contract version, and Aethrix is told.
- Live client checks only after the user says they are available. Local ACE server only (10.10.20.20, clean dev root `~/OpenAC-flexcheck`), never Dreamweave.

## The element, as plugins see it (binding for every task)

```xml
<repeat items="{Rows}" key="{Id}">
  <group layout="row" gap="6" h="22">
    <checkbox checked="{On}" onchange="{SetOn}" w="20"/>
    <label text="{Label}" grow="1"/>
    <number value="{Value}" onchange="{SetValue}" w="80"/>
  </group>
</repeat>
```

- `items` (required) binds a property declared as `IReadOnlyList<T>`, `IList<T>` or `IEnumerable<T>`. `T` comes from the declared type. Any other type, a missing attribute or a literal fails the build with `FormatException`.
- `key` (optional) binds a property on `T`. Keys are compared with `Equals`.
- No other attributes. `x y w h anchor` and flex item attributes on `<repeat>` fail the build; the copies' top-level elements take flex item attributes.
- R1: one copy per item, in list order. An empty or null list builds nothing.
- R2: inside a template, `{Name}` resolves only on the innermost repeat's item, with the same rules and types as on a window binding.
- R3: names are checked against `T` at build time, even when the list is empty. Members only on a subtype of `T` are invisible. Attributes that fall back silently today still do.
- R4: `items` and every key are read each tick. Nothing changes when the count, the key sequence and every item reference all match the last tick, even if the list is a new instance (ruled in the final review).
- R5: with `key`, a copy whose key is still in the list is kept (with its focus, half-typed field text, open menu, expander state and scroll) and moved, and its source follows the new item object. New keys get new copies and missing keys lose theirs. Without `key`, copies match by index. Duplicate keys are logged once per repeat and fall back to index matching.
- R6: a `<repeat>` whose parent is not a flex container (a window root or a `<group>` with `layout`) fails the build.
- R7: repeats nest at any depth.
- R8: `name` inside a template is remembered per item, as `name#k1/k2…` (the keys of every enclosing repeat, outermost first, `Convert.ToString(key, CultureInfo.InvariantCulture)`). A named element under any enclosing repeat without `key` fails the build.
- R9: everything else works inside a template: visible, enabled and tooltip bindings, bound colours, `contextmenu`, scrolling groups, the theme.
- R10: about 150 copies of a five-control row inside a scrolling group reconcile and lay out without a visible hitch (Task 5 sets the bound).
- R11: an older host fails on the unknown element, which is fine.

---

### Task 1: Binding source in place of `object binding`

**Goal:** Every markup binding helper resolves through `MarkupBindingSource` instead of capturing `object binding`, with no change in behaviour.

**Files:**
- Create: `src/AcDream.App/UI/Markup/MarkupBindingSource.cs`
- Modify: `src/AcDream.App/UI/MarkupDocument.cs`, `src/AcDream.App/UI/Markup/MarkupBindings.cs`, and every `src/AcDream.App/UI/Markup/Markup*.cs` builder that takes `object binding` (Checkbox, Number, Radio, Separator, Expander, Split, ListHeader, ListSelect, ListTree, ContextMenu, Hotkey, Color, …)
- Test: `tests/AcDream.App.Tests/UI/MarkupBindingSourceTests.cs` (new). Existing markup tests change only where they call an `internal` helper with an `object`.

**Acceptance Criteria:**
- [ ] `git grep -n "object binding" -- src/AcDream.App/UI` prints nothing, `git grep -n "binding.GetType()" -- src/AcDream.App/UI` prints nothing, and `MarkupDocument.Build`/`BuildWindow` keep their public `object binding` signatures.
- [ ] `MarkupBindingSourceTests` pass: lookup goes through `Type`, a null `Current` reads as null and invokes nothing, and a window source behaves exactly as the object did.
- [ ] Every existing test passes unchanged, apart from mechanical call-site edits for `internal` helpers. The gate passes (label `repeat1`).
- [ ] One commit: `refactor(ui): markup bindings resolve through a binding source`.

**Verify:** `dotnet test tests/AcDream.App.Tests -c Release --filter "FullyQualifiedName~Markup"` → no failures outside the baseline; then the gate.

**Steps:**

- [ ] **Step 1: Write `MarkupBindingSourceTests`** (fails: the type doesn't exist):

```csharp
using AcDream.App.UI.Markup;
using Xunit;

namespace AcDream.App.Tests.UI;

public sealed class MarkupBindingSourceTests
{
    private class Row { public string Label { get; set; } = "a"; public Action? Click { get; set; } }
    private sealed class FancyRow : Row { public int Extra => 1; }

    [Fact]
    public void A_window_source_reads_its_object()
    {
        var row = new Row { Label = "x" };
        var source = MarkupBindingSource.For(row);
        Assert.Equal(typeof(Row), source.Type);
        Assert.Equal("x", source.Read(source.Property("Label")!));
    }

    [Fact]
    public void Names_resolve_on_the_declared_type_not_the_object()
    {
        var source = new MarkupBindingSource(typeof(Row)) { Current = new FancyRow() };
        Assert.Null(source.Property("Extra"));
        Assert.NotNull(source.Property("Label"));
    }

    [Fact]
    public void With_no_current_object_reads_are_null_and_actions_do_nothing()
    {
        var source = new MarkupBindingSource(typeof(Row));
        Assert.Null(source.Read(source.Property("Label")!));
        Assert.Null(source.Read(source.Property("Click")!));
    }

    [Fact]
    public void A_kept_source_follows_a_new_current_object()
    {
        var source = new MarkupBindingSource(typeof(Row)) { Current = new Row { Label = "old" } };
        var label = source.Property("Label")!;
        source.Current = new Row { Label = "new" };
        Assert.Equal("new", source.Read(label));
    }
}
```

- [ ] **Step 2: Create `MarkupBindingSource`:**

```csharp
using System.Reflection;

namespace AcDream.App.UI.Markup;

/// <summary>
/// Where markup bindings resolve: names are looked up on <see cref="Type"/>
/// and values read from <see cref="Current"/>. A window's source is its
/// binding object; a repeat copy's source is the list's element type with the
/// copy's item, which the repeat swaps when a kept key gets a new object.
/// While <see cref="Current"/> is null (a repeat's build-time check) reads
/// give null and bound actions do nothing.
/// </summary>
internal sealed class MarkupBindingSource(Type type)
{
    public Type Type { get; } = type;

    public object? Current { get; set; }

    public string TypeName => Type.Name;

    public static MarkupBindingSource For(object binding) =>
        new(binding.GetType()) { Current = binding };

    public PropertyInfo? Property(string name) => Type.GetProperty(name);

    public object? Read(PropertyInfo property) =>
        Current is { } target ? property.GetValue(target) : null;
}
```

- [ ] **Step 3: Apply the rewrite mechanically**, in every helper and builder under `src/AcDream.App/UI` (MarkupDocument.cs, Markup/*.cs):
  - The parameter `object binding` becomes `MarkupBindingSource binding`, and nested local functions that took `object model` take `MarkupBindingSource model`.
  - `binding.GetType().GetProperty(X)` becomes `binding.Property(X)`.
  - `binding.GetType().Name` becomes `binding.TypeName`.
  - `property.GetValue(binding)` (or any `p.GetValue(binding)`) becomes `binding.Read(property)`.
  - `Build`/`BuildWindow` keep `object binding` publicly and create `var source = MarkupBindingSource.For(binding);` once, passing `source` everywhere below.
  - Nothing else changes. If a site does anything with `binding` other than look up, name or read (for example passing it to plugin code), stop and report it.
  - Existing tests that call internal helpers such as `MarkupDocument.BindString(attr, obj)` change to `MarkupDocument.BindString(attr, MarkupBindingSource.For(obj))`.
- [ ] **Step 4:** Run the verify filter, then the full gate, then commit:

```bash
git add -A src tests
git commit -m "refactor(ui): markup bindings resolve through a binding source

Every binding helper looks names up on a type and reads a current object
instead of capturing the window's binding object. A window behaves as
before; this is what lets a repeated template be checked against a list's
element type and follow a row to a new object.

Claude-Session: https://claude.ai/code/session_01C83Twxs4TYLEbsJtnPsN4a"
```

---

### Task 2: Plumbing for copies

**Goal:** The pieces a repeat needs from the UI tree, with tests and no visible behaviour change:
- children inserted and moved at an index,
- ticks that tolerate a child changing its parent's children,
- flex items inserted, removed and moved, with the layout noticing,
- theme actions registered in a scope that can be dropped,
- control state keyed by an item key path.

**Files:**
- Modify: `src/AcDream.App/UI/UiElement.cs` (`InsertChild(int, UiElement)`, `MoveChild(UiElement, int)`, insertion-order tick snapshot), `src/AcDream.App/UI/UiFlexBox.cs` (`Insert`, `Remove`, `Move`, structure-changed flag), `src/AcDream.App/UI/PluginUiThemeSettings.cs` (`UiPluginMarkupPanel.BeginThemeScope()`)
- Create: `src/AcDream.App/UI/Markup/KeyedControlState.cs`
- Test: `tests/AcDream.App.Tests/UI/UiElementChildOrderTests.cs`, `UiFlexBoxStructureTests.cs`, `PluginThemeScopeTests.cs`, `KeyedControlStateTests.cs` (all new)

**Acceptance Criteria:**
- [ ] `InsertChild(index, child)` places the child at `index` in `Children` (reparenting like `AddChild`). `MoveChild(child, index)` reorders without reparenting and without calling `OnSubtreeRemoving`. Both invalidate the cached child order.
- [ ] `TickSelfAndChildren` iterates an insertion-order snapshot that is cached until the children change, so steady-state ticks allocate nothing. A child that inserts or removes siblings during its own `OnTick` doesn't make the loop skip or repeat a child of that tick's snapshot. A test pins this.
- [ ] `UiFlexBox`:
  - `Insert(int index, UiFlexItem)` and `Remove(UiElement)` / `Move(UiElement, int index)` keep `Items` and `Node.Children` in the same order.
  - Any of them marks the box changed, so the next `EnsureLayout` on its root re-measures and re-arranges. This holds even when the box is nested (`IsRoot == false`): a nested box's change is seen by `Changed(root)`.
  - `AddLeaf`/`AddContainer` keep appending as today.
- [ ] `UiPluginMarkupPanel.BeginThemeScope()` returns a `ThemeActionScope : IDisposable`. While it is open, `AddThemeAction` also records each action in the scope. `Dispose` removes those actions from the panel, and they never run again. Scopes don't nest; opening a second while one is open throws `InvalidOperationException`.
- [ ] `KeyedControlState(IMarkupControlState inner, string key)` stores `control` as `control#key`. Wrapping a `KeyedControlState` in another gives `control#outer/inner` (one `#`, keys joined with `/`). `Generation` passes through.
- [ ] The gate passes (label `repeat2`). One commit: `feat(ui): plumbing for repeated markup copies`.

**Verify:** `dotnet test tests/AcDream.App.Tests -c Release --filter "FullyQualifiedName~UiElementChildOrderTests|FullyQualifiedName~UiFlexBoxStructureTests|FullyQualifiedName~PluginThemeScopeTests|FullyQualifiedName~KeyedControlStateTests|FullyQualifiedName~Flex"` → all pass; then the gate.

**Steps:**

- [ ] **Step 1: Tests first** (each fails to compile or fails):
  - `UiElementChildOrderTests`:
    - Insert at 0, at the middle and at Count, and check `Children` order.
    - Move forward and backward.
    - `MoveChild` doesn't clear the moved child's keyboard focus: put the child under a `UiRoot`, focus it, move it, and assert `root.KeyboardFocus` is still the child.
    - A child whose `OnTick` inserts a sibling before itself and removes another: every child in the snapshot ticks exactly once, counted by a tick counter on test elements.
  - `UiFlexBoxStructureTests`: build a root box with three leaves, `EnsureLayout`, then for each of `Insert`, `Remove` and `Move` assert that the next `EnsureLayout(sameW, sameH)` returns true and that placements follow the new order. Repeat the check on a nested box inside a root.
  - `PluginThemeScopeTests`:
    - Actions added inside a scope run on a theme switch until the scope is disposed, and never after.
    - Actions added outside a scope are unaffected.
    - A nested `BeginThemeScope` throws.
  - `KeyedControlStateTests`: use an in-memory `IMarkupControlState` fake, which the existing tests already have (`grep -rn "IMarkupControlState" tests/AcDream.App.Tests | head`). Check that `Get`/`Set`/`Remove` map names, that nesting gives `a#k1/k2`, and that `Generation` passes through.
- [ ] **Step 2: Implement.**

`KeyedControlState`:

```csharp
namespace AcDream.App.UI.Markup;

/// <summary>
/// The remembered state of one repeat copy: every control name is stored as
/// <c>name#key</c>, with the keys of nested repeats joined outermost first
/// (<c>name#outer/inner</c>), so copies don't share one remembered value.
/// </summary>
internal sealed class KeyedControlState : IMarkupControlState
{
    private readonly IMarkupControlState _root;
    private readonly string _path;

    public KeyedControlState(IMarkupControlState inner, string key)
    {
        (_root, _path) = inner is KeyedControlState keyed
            ? (keyed._root, keyed._path + "/" + key)
            : (inner, key);
    }

    public int Generation => _root.Generation;

    public string? Get(string control, string kind) => _root.Get(Name(control), kind);

    public void Set(string control, string kind, string value) => _root.Set(Name(control), kind, value);

    public void Remove(string control, string kind) => _root.Remove(Name(control), kind);

    private string Name(string control) => control + "#" + _path;
}
```

`UiPluginMarkupPanel` scope. The panel keeps `_apply` as today and adds:

```csharp
    private ThemeActionScope? _scope;

    /// <summary>
    /// Starts recording the theme actions added until the scope is disposed;
    /// disposing it removes them, so a repeat copy's look stops following the
    /// theme once the copy is gone.
    /// </summary>
    internal ThemeActionScope BeginThemeScope()
    {
        if (_scope is not null) throw new InvalidOperationException("a theme scope is already open");
        return _scope = new ThemeActionScope(this);
    }

    internal sealed class ThemeActionScope(UiPluginMarkupPanel panel) : IDisposable
    {
        internal readonly List<Action<PluginUiPalette?>> Actions = new();
        private bool _closed, _disposed;

        /// <summary>Stops recording; the actions keep running until <see cref="Dispose"/>.</summary>
        public void Close()
        {
            if (_closed) return;
            _closed = true;
            if (ReferenceEquals(panel._scope, this)) panel._scope = null;
        }

        public void Dispose()
        {
            if (_disposed) return;
            Close();
            _disposed = true;
            foreach (var action in Actions) panel._apply.Remove(action);
        }
    }
```

  `AddThemeAction` also does `_scope?.Actions.Add(action);`. A copy's build does `using`-free `var scope = panel.BeginThemeScope(); … build … scope.Close();`, and the copy keeps the scope to dispose when it is removed.

  - `UiElement`:
    - `InsertChild(int index, UiElement child)` mirrors `AddChild`: it detaches from the old parent, sets `Parent`, `_children.Insert(index, child)` and `InvalidateChildOrder()`.
    - `MoveChild(UiElement child, int index)` throws `ArgumentException` if `child.Parent != this`, then does remove-at-old and insert-at-new on `_children` (adjusting the index when old < new) and `InvalidateChildOrder()`. It does not call `OnSubtreeRemoving`.
    - The tick snapshot is a cached `UiElement[]? _childrenTickOrder`, cleared in `InvalidateChildOrder()` and rebuilt from `_children` on demand. `TickSelfAndChildren` loops over it.
  - `UiFlexBox`: a private `bool _structureChanged`. `Insert`/`Remove`/`Move` edit `Items` and `Node.Children` at the same indices and set it. `Changed(box)` returns true when `box._structureChanged`, checked before the per-item loop. `EnsureLayout` clears it on every box it walks after `Place`; add a `ClearStructure(box)` recursion over `Inner` boxes. `Remove(UiElement)` finds the item by `ReferenceEquals(item.Element, element)` and returns false when absent.
- [ ] **Step 3:** Run the verify filter, then the gate, and commit with message `feat(ui): plumbing for repeated markup copies` (short body, Claude-Session line).

---

### Task 3: `<repeat>` builds its copies

**Goal:** The markup builder accepts `<repeat>`, validates it (R3, R6, R8 build errors, attributes, `items` type), places a marker, and builds one copy per item at build time with each copy bound to its item. It also builds a throwaway check copy when the list is empty.

**Files:**
- Create: `src/AcDream.App/UI/Markup/MarkupRepeat.cs` (parse, validate, build a copy), `src/AcDream.App/UI/UiMarkupRepeat.cs` (the marker element and its copies; reconcile comes in Task 4)
- Modify: `src/AcDream.App/UI/MarkupDocument.cs`:
  - a `case "repeat"`;
  - the tail skips theme registration, context menu and flex item for the marker;
  - the window build scans for named elements under keyless repeats;
  - `AddElement` gains the parameters a copy needs, explained below.
- Test: `tests/AcDream.App.Tests/UI/MarkupRepeatBuildTests.cs` (new)

**Acceptance Criteria:**
- [ ] Each of these is a passing test in `MarkupRepeatBuildTests` (a window with `layout="column"` unless the test says otherwise; binding classes local to the test):
  1. With 0 items the marker is the only child; with 1 item there is one copy; with 3 items there are three copies in list order. Each is checked through the copies' label text and their order in `Children` and in the flex `Items`.
  2. A copy's bindings read its item, and its `Action`/`Action<T>` callbacks run on its item: click a copy's button and the item's own handler runs.
  3. A misspelt required binding in the template (`<checkbox checked="{Onn}"/>`) throws `FormatException` at build with an empty list and with a null list.
  4. A member only on a subtype of `T` is invisible: the property is declared on `FancyRow : Row`, the list is declared `IReadOnlyList<Row>` and holds `FancyRow`s, and a required binding to that member throws.
  5. `items` errors: a missing `items`, a literal `items`, a non-list property, a plain `IEnumerable` and a `string` (which is `IEnumerable<char>` but is refused explicitly) each throw `FormatException`.
  6. `<repeat>` in a window without `layout`, or in a `<group>` without `layout`, throws `FormatException` (R6).
  7. `<repeat>` with any attribute other than `items`/`key` (`x`, `w`, `grow`, `anchor`, …) throws.
  8. A `name`d element inside a repeat without `key` throws, and so does one inside a keyed repeat nested in a keyless one (R8). A named element in a keyed repeat builds.
  9. Nested repeats build: outer sections, inner rows, and the inner bindings read the inner item.
  10. The window's first measurement includes the copies: a 3-row repeat in a window without `h` opens taller than the same window with 0 rows.
  11. A themed window's copy follows a theme switch: a label's colour changes to the palette text.
  12. `<contextmenu>` inside a template element attaches to that copy's element and invokes the copy's item action.
- [ ] Nothing changes for markup without `<repeat>`. Existing tests pass, and the gate passes (label `repeat3`).
- [ ] One commit: `feat(ui): <repeat> builds a template once per list item`.

**Verify:** `dotnet test tests/AcDream.App.Tests -c Release --filter "FullyQualifiedName~MarkupRepeat|FullyQualifiedName~Markup"` → no failures outside the baseline; then the gate.

**Steps:**

- [ ] **Step 1: Write the 12 tests** (they fail: unknown element `<repeat>`).
- [ ] **Step 2: AddElement plumbing.** A copy is built with exactly the arguments the enclosing `AddElement` got, with three substitutions:
  - `binding` becomes the copy's source;
  - `controlState` becomes `new KeyedControlState(controlState, keyText)` when keyed and `controlState` is not null;
  - `flex` is the surrounding container's box.

  So the repeat case needs all of `AddElement`'s current parameters. Pass them in a small record, `MarkupRepeat.BuildArgs` (resolve, datFont, icons, themedPanel, themes, controlState, describeKey, textures, recentColors), plus a delegate that calls `AddElement` with a given parent, element, source, control state and flex box. Keep `AddElement` private: `MarkupDocument` hands `MarkupRepeat` a `Func`/`Action` closure, the same way `MarkupBuildContext.AddChild` does today.
- [ ] **Step 3: `MarkupRepeat.Build`**, called from `case "repeat"`:
  - **Validation:**
    - Throw `FormatException("<repeat> goes in a flex container (a window or <group> with layout)")` when `flex is null`.
    - Reject attributes other than `items`/`key`.
    - `items` must be a `{binding}`. `binding.Property(name)` must exist. Its `PropertyType` must not be `string` and must be or implement `IEnumerable<T>`. Prefer `IReadOnlyList<T>`/`IList<T>`/`IEnumerable<T>` as the declared type itself; otherwise take the single `IEnumerable<>` interface it implements. Exactly one `T`, or throw.
    - `key`, when present, must be a `{binding}` resolving on `T`.
  - **Template:** the `<repeat>`'s child elements. Zero children is allowed and builds nothing.
  - **Marker:** create a `UiMarkupRepeat` (zero size, `ClickThrough = true`, `Anchors = AnchorEdges.None`, no flex item) and `parent.AddChild(marker)`. The `AddElement` tail must skip theme registration, context menu attach and `AddFlexItem` when `built is UiMarkupRepeat`. `MarkupContentSize` never sees it.
  - **Initial copies:** read the list once via the parent source. When it is null or empty, run the check build: a source with `Current = null`, built into a detached `UiPanel` scratch parent and a scratch `UiFlexBox` with its theme scope opened then disposed at once, so a misspelt name throws now (R3) and nothing stays registered. Otherwise build one copy per item (next step) and run no separate check build: the real copies check the same names against `T`.
  - **Building one copy** (`UiMarkupRepeat.BuildCopy(item, index)`):
    1. Open a theme scope when `themedPanel` is not null.
    2. Create `source = new MarkupBindingSource(T) { Current = item }`.
    3. Remember `firstChild = parent.Children.Count` and `firstItem = flex.Items.Count`, then call the AddElement delegate once per template child into `parent` with `flex`.
    4. Collect the new elements (`parent.Children[firstChild..]`) and the new flex items.
    5. Close the scope.
    6. Call `CaptureAuthoredAnchorBaselines()` on each new element, so absolute content inside a copy keeps its anchors.
    7. Move the elements and items to just after the previous copy, or after the marker for the first.
    8. Record a `Copy` (key, index, source, elements, scope, KeyedControlState).
- [ ] **Step 4: R8 scan.** In `BuildWindow`, before building, walk every `XElement` with a `name` attribute. If any ancestor is a `<repeat>` without `key`, throw `FormatException($"<{el.Name.LocalName} name=\"…\"> inside a <repeat> needs key= on every enclosing repeat")`.
- [ ] **Step 5:** Run the verify filter, then the gate, then commit (short body, Claude-Session line).

---

### Task 4: Reconcile each tick

**Goal:** Each tick `UiMarkupRepeat` re-reads the list and keys. It keeps, moves, adds and removes copies per R4/R5, follows kept keys to new item objects, falls back to index matching on duplicate keys, and releases everything a removed copy had.

**Files:**
- Modify: `src/AcDream.App/UI/UiMarkupRepeat.cs`
- Test: `tests/AcDream.App.Tests/UI/MarkupRepeatReconcileTests.cs` (new)

**Acceptance Criteria:**
- [ ] Each of these is a passing test (`UiRoot`-mounted window, ticked with `root.Tick(0.016, n)`):
  1. Editing an item in place (same list, same objects, a changed `Label`) rebuilds nothing: the same element instances, and the label shows the new text after a tick.
  2. A new list with the same keys and new item objects keeps every copy (same element instances), and the copies read the new objects.
  3. A focused `<field>` in a kept copy keeps its focus and its half-typed text when a new list with the same keys arrives. The text is typed through `UiRoot` key/char events, as the field tests do.
  4. A removed key drops its copy:
     - its elements leave `Children` and the flex `Items`;
     - its theme actions stop running (switch the theme, and the detached label's colour doesn't change);
     - focus inside it is cleared.
  5. A reorder moves copies: the same instances in the new order, in `Children` and in `Items`, and the next layout places them in the new order.
  6. Adding a key in the middle inserts a copy at that position.
  7. Without `key`: a shorter list removes copies from the end and a longer list adds them at the end; copies match by index and read the item now at their index.
  8. Duplicate keys log one line once per repeat (capture `Console.Out`) and match by index; a later list with unique keys goes back to keyed matching.
  9. Nested repeats reconcile at both levels: removing an outer section removes its inner rows and their scopes, and an inner list change touches only that section's copies.
  10. A `name`d expander inside a keyed template is remembered per key. Expanding the copy with key `b` writes `section#b`, a rebuilt window opens `b` expanded and `a` collapsed, and a nested copy writes `name#outer/inner`.
  11. A list swapped for null or an empty list removes every copy; swapping back builds again.
- [ ] Removing a copy disposes any element in its subtree that implements `IDisposable`. Check whether the colour picker or any other control holds a texture or other resource (`grep -n "IDisposable\|Release(" src/AcDream.App/UI/UiMarkup*.cs`); if one does, a test pins its release on copy removal.
- [ ] The gate passes (label `repeat4`). One commit: `feat(ui): <repeat> keeps rows by key as its list changes`.

**Verify:** `dotnet test tests/AcDream.App.Tests -c Release --filter "FullyQualifiedName~MarkupRepeat"` → all pass; then the gate.

**Steps:**

- [ ] **Step 1: Write the 11 tests** (they fail, since no reconcile exists yet).
- [ ] **Step 2: Implement `UiMarkupRepeat.OnTick`:**
  - **Read.** Read `items` through the parent source into a reused `List<object?>`. Read keys through the key property, or use indices when there is no key.
  - **Unchanged check.** Compare with the last tick's list reference, items and keys, held in reused arrays. Return when everything is equal, without allocating.
  - **Keyed match** (unique keys):
    - build a `Dictionary<object, Copy>` of the current copies;
    - for each new item, take the existing copy with its key (set `source.Current = item`) or build a new one;
    - remove copies whose keys are gone;
    - move the kept and new copies into order after the marker (`MoveChild` and `UiFlexBox.Move`, in sequence).
  - **Duplicate keys.** Detect them with a `HashSet`. Then log once (`Console.WriteLine($"markup <repeat items=\"{…}\">: duplicate keys; rows are matched by position")`, guarded by a bool reset when keys are unique again) and match by index.
  - **Index match:** set `Current` on copies 0..min-1, remove extras from the end, and build the missing copies at the end.
  - **Removing a copy:** for each of its elements, call `parent.RemoveChild(element)`. That calls `UiRoot.OnSubtreeRemoving`, which clears focus, capture and popups. Also call `flex.Remove(element)`, dispose any `IDisposable` in its subtree, and dispose its theme scope. Nested repeat markers inside it are removed with it; their copies' scopes are disposed recursively, because a marker disposes its own copies when it leaves its parent (override `OnRemoved`, or have the outer remove call `marker.DisposeCopies()` while walking the subtree).
  - **Where it runs.** All of this runs from `OnTick`, so the tick snapshot from Task 2 keeps the parent's loop safe. Copies built in a tick tick from the next tick on, and their sources apply their values when they first tick. That's acceptable; say so in a comment.
- [ ] **Step 3:** Run the verify filter, then the gate, then commit (short body, Claude-Session line).

---

### Task 5: Many rows stay smooth

**Goal:** R10 is checked by a test. 150 copies of a five-control row (checkbox, label, menu, number, button) in a `<group layout="column" scroll="y">` reconcile and lay out within a bound, and an unchanged tick allocates nothing.

**Files:**
- Test: `tests/AcDream.App.Tests/UI/MarkupRepeatScaleTests.cs` (new)
- Modify: `src/AcDream.App/UI/UiMarkupRepeat.cs` or `UiFlexBox.cs` only if the test finds a hot spot

**Acceptance Criteria:**
- [ ] The build time of the 150-row window, the time of an unchanged tick plus layout, and the time of a shuffle of all 150 keys plus layout are each measured as the least of five rounds, the same as the flex allocation test (`FlexLayoutScalingTests`). Bounds are set at 4× the measured Release time on this machine, written as constants with a comment giving the measured values and the date.
- [ ] An unchanged tick allocates 0 bytes in the repeat's reconcile (`GC.GetAllocatedBytesForCurrentThread` around `marker.Tick` only, excluding other elements).
- [ ] If any measurement shows a hot spot (for example, an O(n²) move loop), it is fixed in this task, with the fix described in the commit.
- [ ] The gate passes (label `repeat5`). One commit: `test(ui): a 150-row repeat reconciles and lays out within bounds` (or `perf(ui): …` if code changed).

**Verify:** `dotnet test tests/AcDream.App.Tests -c Release --filter "FullyQualifiedName~MarkupRepeatScaleTests"` → passes 3 runs in a row; then the gate.

**Steps:**

- [ ] **Step 1:** Write the test with generous provisional bounds, run it in Release, and record the numbers.
- [ ] **Step 2:** Set the bounds to 4× the measured values, then run the test three times. Then run the gate and commit.

---

### Task 6: Docs and the ControlsDemo Repeat window

**Goal:** Plugin authors can read how `<repeat>` works and see it running.

**Files:**
- Modify: `docs/plugin-ui-markup.md` (a `## Repeat` section after Scrolling groups, a row in the element table, the scope rule, and the test classes in `## Tests`)
- Create: `samples/AcDream.Plugins.ControlsDemo/repeat.xml`, `samples/AcDream.Plugins.ControlsDemo/RepeatBinding.cs`
- Modify: `samples/AcDream.Plugins.ControlsDemo/ControlsDemoPlugin.cs` (register the window), `samples/AcDream.Plugins.ControlsDemo/AcDream.Plugins.ControlsDemo.csproj` (copy `repeat.xml`, like the others)
- Test: the ControlsDemo sample test class, which builds each window against its real binding (`grep -rln "ControlsDemo" tests/AcDream.App.Tests/UI | head`)

**Acceptance Criteria:**
- [ ] The docs `## Repeat` section covers:
  - the example from "The element, as plugins see it";
  - `items` types and the declared-type rule;
  - the scope rule, written so a plugin's own XML checker can follow it: "inside `<repeat items="{X}">`, names resolve on the element type of property `X` of the enclosing scope, and nothing else is visible";
  - keys and what a kept row keeps;
  - index matching without `key`, and duplicate keys;
  - flex-only placement;
  - nesting;
  - remembered `name`s per key, and that a named element needs `key` on every enclosing repeat;
  - that older hosts fail on the element.
  The style matches the surrounding sections: plain, short sentences.
- [ ] The ControlsDemo "Controls: Repeat" window has `theme="plugin"`, `layout="column"` and a title bar. It shows a scrolling list of rows (checkbox, label, menu, number) with Add, Remove last and Shuffle buttons, and a status label "n of m on". Shuffle uses a deterministic order (`new Random(42)` held by the binding), so the sample test can rely on it. Remove last and Shuffle publish a new list; editing a row's number edits the row object in place.
- [ ] The window is placed per the sample's layout rules (x ≥ 56 to clear the shelf) without overlapping the other ControlsDemo windows' default rects.
- [ ] A sample test builds the window against `RepeatBinding` and checks:
  - 5 initial rows;
  - after Add, 6;
  - after Shuffle, the same element instances in a new order;
  - after Remove last, 5.
- [ ] The gate passes (label `repeat6`). One commit: `feat(samples): ControlsDemo Repeat window; docs for <repeat>`.

**Verify:** `dotnet test tests/AcDream.App.Tests -c Release --filter "FullyQualifiedName~ControlsDemo|FullyQualifiedName~MarkupRepeat"` → all pass; then the gate.

**Steps:**

- [ ] **Step 1:** Write the sample test, see it fail, add the binding, markup and registration, and see it pass.
- [ ] **Step 2:** Write the docs, then run the gate and commit.

---

### Task 7: Whole-branch check, push, live check (user present)

**Goal:** `feat/markup-repeat` is reviewed as a whole, pushed, and seen working in a client.

**Files:** none, unless the review or live check finds something. Fixes made before the push are folded into the commit they belong to (the branch is unpushed); after the push they become new commits.

**Acceptance Criteria:**
- [ ] The final whole-branch review is clean. Any fix wave passes the gate on every rewritten commit.
- [ ] `git push -u origin feat/markup-repeat`. No upstream PR is opened; the user opens it as davidc-smith.
- [ ] Live check, only after the user says they are available:
  - Build the client and ControlsDemo into `~/OpenAC-flexcheck` and launch with Aethrix's `tools/run-dev-client.sh` (`OPENAC_DEV_ROOT=$HOME/OpenAC-flexcheck`, `OPENAC_ROOT=<this worktree>`, `ACDREAM_DAT_DIR=/Users/davidsmith/AsheronsCall`, `--no-build --no-aethrix --no-rynth`, after building with `--artifacts-path ~/OpenAC-flexcheck/build/openac`).
  - The user checks the Repeat window in Classic and theme: rows render, Add, Remove and Shuffle work, a half-typed number survives Shuffle, scrolling is smooth, and an open menu in a row survives Shuffle.
  - The user also takes a quick look at the sidepanel "–" theming carried over from the flex branch.

**Verify:** the user's confirmation in the conversation.

**Steps:**

- [ ] **Step 1:** Run the final review, any fix wave and the push.
- [ ] **Step 2:** Ask the user whether they are available, then run the live check.

---

### Task 8: Fork main takes it

**Goal:** Fork `main` gets flex and `<repeat>` by merge, with the next contract version, and Aethrix is told.

**Files:** a merge commit on `main`, `src/AcDream.Plugin.Abstractions/AcDream.Plugin.Abstractions.csproj` (`ForkContractVersion`), and the contract notes doc where `fork.5` is documented (`git grep -n "fork.5" -- docs src | head`)

**Acceptance Criteria:**
- [ ] On a `merge/markup-repeat` branch from current `main` (worktree `.worktrees/merge-repeat`), run `git merge --no-ff feat/markup-repeat`. Every conflict in a flex, title-bar, scroll or theme file is resolved to `main`'s version. The repeat additions are kept and adapted to `main`'s code: `main`'s `MarkupDocument` has file icons and markup canvas, so the binding-source rewrite must also cover those code paths.
- [ ] On `main` a `<canvas>` inside a repeat template fails the build (R9), with a test.
- [ ] `git diff main merge/markup-repeat --stat` shows only:
  - repeat work: `MarkupBindingSource`, `KeyedControlState`, `MarkupRepeat`, `UiMarkupRepeat`, the AddElement/Bind* rewrite, the Task 2 plumbing, tests, docs, the sample;
  - the PR 236 → flex fixes that `main` lacked: the themed "–" (only if it doesn't clash with `main`'s ghost minimize; otherwise keep `main`'s) and the flat thumb (only where `main` has no modern thumb).
  Anything else is a merge mistake.
- [ ] `ForkContractVersion` is bumped to the next free version after whatever `main` has then. The gateway-split plan 2 may have changed the scheme; follow it.
- [ ] The fork gate: Release build with 0 warnings outside `external/`; App test failures equal `main`'s baseline by name (measure `main` first with the same script).
- [ ] Merge into `main` (fast-forward or `--no-ff` per the fork's habit), push `main`, then merge `main` into `rynth` per `BRANCHING.md`, build, test and push.
- [ ] Aethrix is told the contract version and fork commit. Update `~/code/OpenAC/OpenAC-Plugins-Aethrix`'s api-gaps #78 to resolved, in the same style as earlier resolved gaps, without committing Aethrix unless the user asks.

**Verify:** the fork gate output, and `git log --oneline -1 origin/main`.

**Steps:**

- [ ] **Step 1:** Measure `main`'s baseline.
- [ ] **Step 2:** Merge, resolve and adapt.
- [ ] **Step 3:** Add the canvas rule with its test.
- [ ] **Step 4:** Bump the contract, run the gate, merge, push, carry it into `rynth`, and write the Aethrix note.
