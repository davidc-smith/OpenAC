# Markup controls, plan 5: `<expander>` Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers-extended-cc:subagent-driven-development (recommended) or superpowers-extended-cc:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Ship `<expander>`, a collapsible section whose siblings below it move up when it collapses, and make it the first control to remember its state per character through the plan-4 store, wired into the real client.

**Architecture:**
- **Shape.** `UiMarkupExpander` is a click-through container holding a focusable `UiMarkupExpanderHeader` (arrow + caption, 18 px) and a `Content` panel that holds the authored children. Children are laid out in content coordinates: y = 0 is the first pixel below the header.
- **Collapse.** Collapsing hides `Content`, sets the expander's height to the header's, and moves every sibling at or below its old bottom up by the difference. Each moved element then calls `UiElement.CaptureCurrentAnchorBaseline()`, so the per-frame anchor pass keeps the new geometry. The expander records which siblings it moved and moves exactly those back when it expands, so stacked expanders compose.
- **State.** Bound state (`expanded="{bool}"`) belongs to the plugin. Unbound state is the host's: it starts from `open`, and for a named expander it is remembered through `IMarkupControlState` via a small `RememberedValue` helper, which re-reads after a character switch. A value equal to the markup default is removed rather than stored.
- **Plumbing.** `MarkupDocument.Build` gains an optional `IMarkupControlState`. `MarkupBuildContext` carries it plus an `AddChild` callback, so container builders recurse through `AddElement`. `RetailUiRuntime` gives each plugin window its own state (`ForWindow(panel.WindowName)`) and saves the store with the window layout.

**Tech Stack:** C# / .NET 10, xUnit, headless `UiRoot` + `RecordingGpuDevice` tests.

**Spec:** `docs/superpowers/specs/2026-10-21-markup-controls-design.md` (branch `docs/markup-controls-spec`), "Shared rules" (remembered state) and section 6. Plan 4 built the store.

## Global Constraints

- **Workspace.** Worktree `/Users/davidsmith/code/OpenAC/OpenAC/.worktrees/markup-controls`, branch `feat/markup-controls` (HEAD 7696c39d, from upstream 391e709a). Never merge fork `main` in; never add `docs/superpowers/` files there. Push only when the controller says so.
- **Build hygiene.** Every shell runs `export DOTNET_ROOT=$HOME/.dotnet PATH=$HOME/.dotnet:$PATH` first. After building, `git checkout -- '*.lock.json'`; never commit lock files.
- **Commits.** Every commit must build and pass its own tests. Commit messages end with `Claude-Session: https://claude.ai/code/session_01UzLnYkJ6ifzKGaeNwgsp8W`.
- **Helper conventions.** `BindRequired*` throw when absent or mistyped; other `Bind*` return null when absent and throw when mistyped; `context` is `"<element> <attribute>"`. Builders return exactly one element.
- **Remembered-state contract** (plan 4 final review), enforced by `RememberedValue`:
  - (a) re-read whenever `Generation` moves;
  - (b) build every new value from a fresh read, never a stale cache;
  - (c) write only in response to a user action on that control;
  - (d) unnamed controls and bound (plugin-owned) state never touch the store;
  - (e) remove the entry when the value equals the markup default.
- **Window names.** The store is reached only on the UI thread, and is keyed by the registered `panel.WindowName`.
- **`<expander>` contract.**
  - Attributes: `name x y w h text expanded onchange open color` plus the common ones.
  - `h` must be at least the 18 px header.
  - `expanded` is a required-if-present `bool` binding. When present, the plugin owns the state: a click calls `onchange(new)` and nothing else, and the layout follows `expanded` every tick.
  - `onchange` is an optional `Action<bool>` receiving the new state.
  - `open` is `true` (default) or `false`; anything else throws.
  - The header toggles on press and release over it, or on Enter/Space while it has focus. Clicking doesn't take keyboard focus. The header is the group's one tab stop.
  - Disabled fires nothing and draws faded.
  - Collapsing moves siblings whose top is at or below the expander's bottom; siblings above or beside it don't move.
- **Caption reachability.** `SetControlLabel` on a named expander relabels its header.

**User decisions (already made):**
- One branch, one commit series per control, upstream-portable.
- Remembered state is "both": unbound state is host-remembered per character/window/control; bound state is the plugin's.
- In absolute layout, collapsing an expander moves the siblings below it up (accordion); flex reflow is a fork-only concern for later.

---

## File Structure

| File | Responsibility |
|---|---|
| `src/AcDream.App/UI/Markup/IMarkupControlState.cs` | `Remove` |
| `src/AcDream.App/UI/Markup/MarkupControlStateStore.cs` | `Remove`, and a load retry in `Save` after a failed load |
| `src/AcDream.App/UI/Markup/RememberedValue.cs` (new) | One remembered value per control and kind, enforcing the contract |
| `src/AcDream.App/UI/Markup/MarkupBuildContext.cs` | `ControlState`, `AddChild` |
| `src/AcDream.App/UI/MarkupDocument.cs` | `Build(..., controlState)`; `AddElement` threads it; `Context()` fills it; `BindRequiredBoolReader` → internal; `case "expander"` |
| `src/AcDream.App/UI/UiMarkupExpander.cs` (new) | The expander, its layout and state |
| `src/AcDream.App/UI/UiMarkupExpanderHeader.cs` (new) | The header: input, focus, arrow and caption |
| `src/AcDream.App/UI/Markup/MarkupExpander.cs` (new) | `<expander>` builder |
| `src/AcDream.App/UI/PluginMarkupTheme.cs` | Expander theme + font |
| `src/AcDream.App/Plugins/BufferedUiRegistry.cs` | `SetControlLabel` reaches expanders |
| `src/AcDream.App/UI/RetailUiRuntime.cs` | Store per runtime, window state per plugin panel, save with the layout |
| `tests/AcDream.App.Tests/UI/MarkupControlStateStoreTests.cs`, `RememberedValueTests.cs` (new), `MarkupExpanderTests.cs` (new) | Tests |
| `docs/plugin-ui-markup.md` | Expander docs; separator nits from plan 4 |

---

### Task 1: `Remove`, a load retry, and `RememberedValue`

**Goal:** The store can forget a value, recovers after a transient load failure, and controls get one helper that follows the remembered-state contract.

**Files:**
- Modify: `src/AcDream.App/UI/Markup/IMarkupControlState.cs`, `src/AcDream.App/UI/Markup/MarkupControlStateStore.cs`
- Create: `src/AcDream.App/UI/Markup/RememberedValue.cs`
- Test: `tests/AcDream.App.Tests/UI/MarkupControlStateStoreTests.cs`, `tests/AcDream.App.Tests/UI/RememberedValueTests.cs`

**Acceptance Criteria:**
- [ ] `Remove(control, kind)` forgets a value (and `Save` then writes the set without it); removing a missing value doesn't mark the store dirty; blank names throw.
- [ ] After a failed load, a later `Save` retries the load; if it now succeeds, the session's changes are laid over what's on disk and saved, and saving works normally from then on. While loads keep failing, the file is never written.
- [ ] `RememberedValue.TryRefresh` reports a value the first time and after every character switch, and nothing in between; `Remember(value)` stores, `Remember(null)` removes; with no state or a blank control name it does nothing and `IsActive` is false.

**Verify:** `dotnet test tests/AcDream.App.Tests/AcDream.App.Tests.csproj --filter "FullyQualifiedName~MarkupControlStateStoreTests|FullyQualifiedName~RememberedValueTests"` → all pass.

**Steps:**

- [ ] **Step 1: Write the failing store tests** — append inside `MarkupControlStateStoreTests` (it has `_path`, `_character`, `NewStore()`):

```csharp
    [Fact]
    public void RemoveForgetsAValue()
    {
        var store = NewStore();
        var nav = store.ForWindow("nav");
        nav.Set("adv", "expanded", "false");
        nav.Set("mobs", "widths", "1");
        store.Save();
        nav.Remove("adv", "expanded");
        Assert.Null(nav.Get("adv", "expanded"));
        store.Save();
        var fresh = NewStore().ForWindow("nav");
        Assert.Null(fresh.Get("adv", "expanded"));
        Assert.Equal("1", fresh.Get("mobs", "widths"));
    }

    [Fact]
    public void RemovingAMissingValueChangesNothing()
    {
        var store = NewStore();
        store.ForWindow("nav").Remove("adv", "expanded");
        store.Save();
        Assert.False(File.Exists(_path));
    }

    [Theory]
    [InlineData("", "k")]
    [InlineData("c", " ")]
    public void RemoveRejectsBlankNames(string control, string kind)
    {
        Assert.ThrowsAny<ArgumentException>(() => NewStore().ForWindow("nav").Remove(control, kind));
    }

    [Fact]
    public void ALaterSaveRetriesAFailedLoad()
    {
        File.WriteAllText(_path, "{ not json");
        var store = NewStore();
        var nav = store.ForWindow("nav");
        nav.Set("adv", "expanded", "false");
        store.Save();
        Assert.Equal("{ not json", File.ReadAllText(_path));

        new SettingsStore(_path + ".tmp").SaveWindowPosition("alice", "chat", new UiWindowPosition(1, 2));
        File.Copy(_path + ".tmp", _path, overwrite: true);
        File.Delete(_path + ".tmp");
        store.Save();

        var settings = new SettingsStore(_path);
        Assert.Equal(new UiWindowPosition(1, 2), settings.LoadWindowPosition("alice", "chat"));
        Assert.Equal("false", NewStore().ForWindow("nav").Get("adv", "expanded"));
    }
```

(`using AcDream.UI.Abstractions.Panels.Settings;` is already in the file.)

- [ ] **Step 2: Write the failing helper tests** — create `tests/AcDream.App.Tests/UI/RememberedValueTests.cs`:

```csharp
using AcDream.App.UI.Markup;

namespace AcDream.App.Tests.UI;

public class RememberedValueTests
{
    private string _character = "alice";

    private (MarkupControlStateStore Store, IMarkupControlState State) NewState()
    {
        var store = new MarkupControlStateStore(null, () => _character);
        return (store, store.ForWindow("nav"));
    }

    [Fact]
    public void RefreshReportsTheFirstReadAndEachCharacterSwitch()
    {
        var (_, state) = NewState();
        state.Set("adv", "expanded", "false");
        var value = new RememberedValue(state, "adv", "expanded");
        Assert.True(value.TryRefresh(out string? first));
        Assert.Equal("false", first);
        Assert.False(value.TryRefresh(out _));

        _character = "bob";
        Assert.True(value.TryRefresh(out string? bob));
        Assert.Null(bob);
        Assert.False(value.TryRefresh(out _));
    }

    [Fact]
    public void RememberStoresAndNullRemoves()
    {
        var (_, state) = NewState();
        var value = new RememberedValue(state, "adv", "expanded");
        value.Remember("false");
        Assert.Equal("false", state.Get("adv", "expanded"));
        value.Remember(null);
        Assert.Null(state.Get("adv", "expanded"));
    }

    [Fact]
    public void RememberWritesToTheCurrentCharacter()
    {
        var (_, state) = NewState();
        var value = new RememberedValue(state, "adv", "expanded");
        value.TryRefresh(out _);
        _character = "bob";
        value.Remember("false");
        Assert.Equal("false", state.Get("adv", "expanded"));
        _character = "alice";
        Assert.Null(state.Get("adv", "expanded"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void WithoutANameNothingIsRemembered(string? control)
    {
        var (_, state) = NewState();
        var value = new RememberedValue(state, control, "expanded");
        Assert.False(value.IsActive);
        Assert.False(value.TryRefresh(out _));
        value.Remember("false");
        Assert.Null(state.Get("other", "expanded"));
    }

    [Fact]
    public void WithoutStateNothingIsRemembered()
    {
        var value = new RememberedValue(null, "adv", "expanded");
        Assert.False(value.IsActive);
        Assert.False(value.TryRefresh(out _));
        value.Remember("false");
    }
}
```

- [ ] **Step 3: Run and see them fail**

Run: `dotnet test tests/AcDream.App.Tests/AcDream.App.Tests.csproj --filter "FullyQualifiedName~MarkupControlStateStoreTests|FullyQualifiedName~RememberedValueTests"`
Expected: build FAILS: `Remove` / `RememberedValue` not found.

- [ ] **Step 4: Add `Remove` to the interface** — in `IMarkupControlState`, after `Set`:

```csharp
    /// <summary>Forgets a value, e.g. when the control is back at its markup default.</summary>
    void Remove(string control, string kind);
```

- [ ] **Step 5: Add `Remove` and the load retry to the store**

In `MarkupControlStateStore`, add after the private `Set` (match how `Get`/`Set` validate names):

```csharp
    private void Remove(string window, string control, string kind)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(control);
        ArgumentException.ThrowIfNullOrWhiteSpace(kind);
        FollowCharacter();
        if (_values.Remove(new MarkupControlKey(window, control, kind)))
            _dirty = true;
    }
```

and in the nested `WindowState`:

```csharp
        public void Remove(string control, string kind) => owner.Remove(window, control, kind);
```

Change the start of `Save()` so a failed load is retried before giving up. Read the current `Save` and `FollowCharacter` first; keep their logging and `_dirty` handling, and add this branch where `Save` currently returns early because the load failed:

```csharp
        if (_loadFailed)
        {
            if (_store is null || !CanPersist(_character)
                || !_store.TryLoadMarkupControlState(_character!, out var onDisk))
            {
                return;
            }
            // The load failed before anything was read, so _values holds only this
            // session's changes: lay them over what is on disk now.
            foreach (var (key, value) in _values)
                onDisk[key] = value;
            _values = onDisk;
            _loadFailed = false;
        }
```

(If the field or helper is named differently in the file, use the existing names. A removal made during a failed-load session can't be applied to a set that was never read; that's acceptable and needs no code.)

- [ ] **Step 6: Create `src/AcDream.App/UI/Markup/RememberedValue.cs`**

```csharp
namespace AcDream.App.UI.Markup;

/// <summary>
/// One value a markup control remembers for the logged-in character, such as
/// an expander's open state. It re-reads after a character switch and writes
/// to whichever character is current, so a control that builds each new value
/// from <see cref="TryRefresh"/> never carries one character's state into
/// another's. Inactive (does nothing) without state or without a control name:
/// unnamed controls aren't remembered.
/// </summary>
internal sealed class RememberedValue(IMarkupControlState? state, string? control, string kind)
{
    private int _generation = int.MinValue;

    public bool IsActive => state is not null && !string.IsNullOrWhiteSpace(control);

    /// <summary>
    /// True, with the remembered text (null when none), the first time it is
    /// asked and after every character switch; false when nothing changed.
    /// </summary>
    public bool TryRefresh(out string? value)
    {
        value = null;
        if (!IsActive)
            return false;
        int generation = state!.Generation;
        if (generation == _generation)
            return false;
        _generation = generation;
        value = state.Get(control!, kind);
        return true;
    }

    /// <summary>Remembers what the player just chose; null forgets it (back at the default).</summary>
    public void Remember(string? value)
    {
        if (!IsActive)
            return;
        _generation = state!.Generation;
        if (value is null)
            state.Remove(control!, kind);
        else
            state.Set(control!, kind, value);
    }
}
```

- [ ] **Step 7: Run the tests and commit**

Run: `dotnet test tests/AcDream.App.Tests/AcDream.App.Tests.csproj --filter "FullyQualifiedName~MarkupControlStateStoreTests|FullyQualifiedName~RememberedValueTests|FullyQualifiedName~AcDream.App.Tests.UI.Markup"` → PASS.

```bash
git checkout -- '*.lock.json' 2>/dev/null
git add src/AcDream.App/UI/Markup tests/AcDream.App.Tests/UI
git commit -m "feat(ui): markup control state can forget values; RememberedValue

Remove clears a value that is back at its default; a save after a
failed load retries it and keeps the session's changes. RememberedValue
is the one way controls read and write remembered state.

Claude-Session: https://claude.ai/code/session_01UzLnYkJ6ifzKGaeNwgsp8W"
```

---

### Task 2: `<expander>` with build plumbing

**Goal:** A working `<expander>`: header, content, accordion collapse of siblings, bound or remembered state, keyboard; and the build plumbing it needs (`controlState`, `AddChild`).

**Files:**
- Modify: `src/AcDream.App/UI/Markup/MarkupBuildContext.cs`, `src/AcDream.App/UI/MarkupDocument.cs`
- Create: `src/AcDream.App/UI/UiMarkupExpander.cs`, `src/AcDream.App/UI/UiMarkupExpanderHeader.cs`, `src/AcDream.App/UI/Markup/MarkupExpander.cs`
- Test: `tests/AcDream.App.Tests/UI/MarkupExpanderTests.cs`

**Acceptance Criteria:**
- [ ] `MarkupDocument.Build(..., controlState: state)` makes `state` reach every element builder through `MarkupBuildContext.ControlState`, including inside groups and expanders; `AddChild` builds child markup into a container with theme registration intact.
- [ ] Children of an expander are laid out below its 18 px header (content y = 0 is the first pixel under it) and stay clickable while expanded; they aren't hit while collapsed.
- [ ] Collapsing sets the expander's height to 18, hides the content, and moves exactly the siblings whose top is at or below its old bottom up by the difference; expanding moves those same siblings back. Siblings beside it don't move. The new positions survive a draw (the anchor pass). Two stacked expanders compose in any order.
- [ ] Unbound: starts from `open` (default true); a header click toggles at once and calls `onchange(new)` if bound. With `expanded="{bool}"`: a click only calls `onchange(new)`; the layout follows the property each tick; without `onchange` it only displays.
- [ ] Remembered (named, unbound, with control state): a toggle stores `"false"`/`"true"` under kind `"expanded"`, or removes it when the new state equals `open`; a new build with that state starts in the remembered state after a tick; a character switch re-reads and re-lays out. Unnamed or bound expanders never touch the state.
- [ ] Focused header: Enter/Space toggle; a click doesn't take focus; disabled ignores clicks and keys.
- [ ] Build throws for `h` < 18, a mistyped `expanded` or `onchange`, and `open` other than true/false.

**Verify:** `dotnet test tests/AcDream.App.Tests/AcDream.App.Tests.csproj --filter "FullyQualifiedName~MarkupExpanderTests"` → all pass.

**Steps:**

- [ ] **Step 1: Write the failing tests** — create `tests/AcDream.App.Tests/UI/MarkupExpanderTests.cs`:

```csharp
using System;
using System.IO;
using System.Linq;
using AcDream.App.UI;
using AcDream.App.UI.Markup;
using AcDream.UI.Abstractions.Panels.Settings;
using Silk.NET.Input;

namespace AcDream.App.Tests.UI;

public sealed class MarkupExpanderTests : IDisposable
{
    private readonly string _settingsPath = Path.Combine(Path.GetTempPath(), $"expander-{Guid.NewGuid():N}.json");

    public void Dispose()
    {
        if (File.Exists(_settingsPath)) File.Delete(_settingsPath);
    }

    private sealed class Binding
    {
        public bool AdvOpen { get; set; } = true;
        public int Changes { get; private set; }
        public bool? LastChange { get; private set; }
        public Action<bool> SetAdvOpen => v => { AdvOpen = v; LastChange = v; Changes++; };
        public Action<bool> Notify => v => { LastChange = v; Changes++; };
        public int InnerClicks { get; private set; }
        public Action Inner => () => InnerClicks++;
        public int NotBool { get; set; }
        public Action<int> WrongAction => _ => { };
    }

    // Expander at (8, 8), 200×100; its header is y 8-26, content from y 26.
    // "Below" (y 120) and "Low" (y 150) sit under its bottom (108); "Beside" (y 40, x 220) beside it.
    private static string Markup(string attributes) => $"""
        <panel x="0" y="0" w="400" h="300">
          <expander name="adv" x="8" y="8" w="200" h="100" text="Advanced" {attributes}>
            <button x="4" y="4" w="80" h="20" text="Inner" onclick="{"{Inner}"}"/>
          </expander>
          <label name="below" x="8" y="120" text="Below"/>
          <label name="beside" x="220" y="40" text="Beside"/>
          <label name="low" x="8" y="150" text="Low"/>
        </panel>
        """;

    private static (UiRoot Root, UiNineSlicePanel Panel, UiMarkupExpander Expander) Mount(
        string attributes, Binding binding, IMarkupControlState? state = null)
    {
        var panel = MarkupDocument.Build(Markup(attributes), binding, _ => (1u, 16, 16), controlState: state);
        var root = new UiRoot { Width = 800, Height = 600 };
        root.AddChild(panel);
        return (root, panel, panel.Children.OfType<UiMarkupExpander>().Single());
    }

    private static UiElement Named(UiElement parent, string name) => parent.Children.Single(c => c.Name == name);

    private static void Click(UiRoot root, int x, int y)
    {
        root.OnMouseDown(UiMouseButton.Left, x, y);
        root.OnMouseUp(UiMouseButton.Left, x, y);
    }

    private static void ClickHeader(UiRoot root) => Click(root, 30, 14);

    [Fact]
    public void Expander_ChildrenSitBelowTheHeaderAndAreClickable()
    {
        var b = new Binding();
        var (root, _, expander) = Mount("", b);
        Assert.Equal(100f, expander.Height);
        Assert.Equal(UiMarkupExpander.HeaderHeight, expander.Content.Top);
        Click(root, 8 + 4 + 5, 26 + 4 + 5);
        Assert.Equal(1, b.InnerClicks);
    }

    [Fact]
    public void Expander_CollapsingMovesWhatIsBelowAndKeepsWhatIsBeside()
    {
        var b = new Binding();
        var (root, panel, expander) = Mount("", b);
        ClickHeader(root);
        Assert.False(expander.Expanded);
        Assert.Equal(UiMarkupExpander.HeaderHeight, expander.Height);
        Assert.False(expander.Content.Visible);
        Assert.Equal(120f - 82f, Named(panel, "below").Top);
        Assert.Equal(150f - 82f, Named(panel, "low").Top);
        Assert.Equal(40f, Named(panel, "beside").Top);

        MarkupDrawProbe.Draw(panel, 800f, 600f);
        Assert.Equal(38f, Named(panel, "below").Top);
        Assert.Equal(UiMarkupExpander.HeaderHeight, expander.Height);

        Click(root, 8 + 4 + 5, 26 + 4 + 5);
        Assert.Equal(0, b.InnerClicks);

        ClickHeader(root);
        Assert.True(expander.Expanded);
        Assert.Equal(100f, expander.Height);
        Assert.Equal(120f, Named(panel, "below").Top);
        Assert.Equal(150f, Named(panel, "low").Top);
        MarkupDrawProbe.Draw(panel, 800f, 600f);
        Assert.Equal(120f, Named(panel, "below").Top);
    }

    [Fact]
    public void Expander_StackedExpandersCompose()
    {
        var b = new Binding();
        var panel = MarkupDocument.Build("""
            <panel x="0" y="0" w="400" h="400">
              <expander name="one" x="8" y="8" w="200" h="100" text="One"/>
              <expander name="two" x="8" y="120" w="200" h="60" text="Two"/>
              <label name="end" x="8" y="200" text="End"/>
            </panel>
            """, b, _ => (1u, 16, 16));
        var root = new UiRoot { Width = 800, Height = 600 };
        root.AddChild(panel);
        var one = (UiMarkupExpander)Named(panel, "one");
        var two = (UiMarkupExpander)Named(panel, "two");
        var end = Named(panel, "end");

        Click(root, 30, 14);                 // collapse one (-82)
        Assert.Equal(38f, two.Top);
        Assert.Equal(118f, end.Top);
        Click(root, 30, 38 + 6);              // collapse two (-42)
        Assert.Equal(76f, end.Top);
        Click(root, 30, 14);                 // expand one (+82)
        Assert.Equal(120f, two.Top);
        Assert.Equal(158f, end.Top);
        Click(root, 30, 120 + 6);             // expand two (+42)
        Assert.Equal(200f, end.Top);
    }

    [Fact]
    public void Expander_OpenFalseStartsCollapsedAfterATick()
    {
        var (root, panel, expander) = Mount("open=\"false\"", new Binding());
        root.Tick(0.016, 0);
        Assert.False(expander.Expanded);
        Assert.Equal(38f, Named(panel, "below").Top);
    }

    [Fact]
    public void Expander_UnboundNotifiesOnchange()
    {
        var b = new Binding();
        var (root, _, expander) = Mount("onchange=\"{Notify}\"", b);
        ClickHeader(root);
        Assert.False(expander.Expanded);
        Assert.Equal(false, b.LastChange);
        Assert.Equal(1, b.Changes);
    }

    [Fact]
    public void Expander_BoundStateBelongsToThePlugin()
    {
        var b = new Binding { AdvOpen = true };
        var (root, panel, expander) = Mount("expanded=\"{AdvOpen}\" onchange=\"{SetAdvOpen}\"", b);
        ClickHeader(root);
        Assert.Equal(false, b.LastChange);
        root.Tick(0.016, 0);
        Assert.False(expander.Expanded);
        Assert.Equal(38f, Named(panel, "below").Top);

        b.AdvOpen = true;
        root.Tick(0.016, 0);
        Assert.True(expander.Expanded);
        Assert.Equal(120f, Named(panel, "below").Top);
    }

    [Fact]
    public void Expander_BoundWithoutOnchangeOnlyDisplays()
    {
        var b = new Binding { AdvOpen = true };
        var (root, _, expander) = Mount("expanded=\"{AdvOpen}\"", b);
        ClickHeader(root);
        root.Tick(0.016, 0);
        Assert.True(expander.Expanded);
    }

    [Fact]
    public void Expander_RemembersPerCharacterAndForgetsTheDefault()
    {
        string character = "alice";
        // A real settings file: switching characters writes alice's choice and reads it back.
        var store = new MarkupControlStateStore(new SettingsStore(_settingsPath), () => character);
        var state = store.ForWindow("win");
        var (root, _, _) = Mount("", new Binding(), state);
        root.Tick(0.016, 0);
        ClickHeader(root);
        Assert.Equal("false", state.Get("adv", "expanded"));
        ClickHeader(root);
        Assert.Null(state.Get("adv", "expanded"));
        ClickHeader(root);
        Assert.Equal("false", state.Get("adv", "expanded"));

        var (root2, panel2, expander2) = Mount("", new Binding(), state);
        root2.Tick(0.016, 0);
        Assert.False(expander2.Expanded);
        Assert.Equal(38f, Named(panel2, "below").Top);

        character = "bob";
        root2.Tick(0.016, 0);
        Assert.True(expander2.Expanded);
        Assert.Equal(120f, Named(panel2, "below").Top);
        character = "alice";
        root2.Tick(0.016, 0);
        Assert.False(expander2.Expanded);
    }

    [Fact]
    public void Expander_UnnamedOrBoundIsNeverRemembered()
    {
        var store = new MarkupControlStateStore(null, () => "alice");
        var state = store.ForWindow("win");
        var b = new Binding();
        var panel = MarkupDocument.Build("""
            <panel x="0" y="0" w="400" h="300">
              <expander x="8" y="8" w="200" h="100" text="Unnamed"/>
              <expander name="bound" x="8" y="150" w="200" h="100" text="Bound" expanded="{AdvOpen}" onchange="{SetAdvOpen}"/>
            </panel>
            """, b, _ => (1u, 16, 16), controlState: state);
        var root = new UiRoot { Width = 800, Height = 600 };
        root.AddChild(panel);
        Click(root, 30, 150 + 6);   // the bound one first: collapsing the unnamed one would move it up
        Click(root, 30, 14);
        Assert.Null(state.Get("bound", "expanded"));
        store.Save();
        Assert.All(panel.Children.OfType<UiMarkupExpander>(), e => Assert.False(e.IsRemembered));
    }

    [Fact]
    public void Expander_KeyboardTogglesAndClicksDoNotTakeFocus()
    {
        var (root, _, expander) = Mount("", new Binding());
        ClickHeader(root);
        Assert.Null(root.KeyboardFocus);
        Assert.True(expander.Header.TabStop);
        root.SetKeyboardFocus(expander.Header);
        root.OnKeyDown((int)Key.Space);
        Assert.True(expander.Expanded);
        root.OnKeyDown((int)Key.Enter);
        Assert.False(expander.Expanded);
    }

    [Fact]
    public void Expander_DisabledIgnoresClicksAndKeys()
    {
        var (root, _, expander) = Mount("enabled=\"false\"", new Binding());
        ClickHeader(root);
        expander.Header.OnEvent(new UiEvent { Type = UiEventType.FocusGained });
        expander.Header.OnEvent(new UiEvent { Type = UiEventType.KeyDown, Data0 = (int)Key.Space });
        Assert.True(expander.Expanded);
    }

    [Theory]
    [InlineData("h=\"10\"")]
    [InlineData("expanded=\"{NotBool}\"")]
    [InlineData("onchange=\"{WrongAction}\"")]
    [InlineData("open=\"maybe\"")]
    public void Expander_BadAttributesThrowAtBuild(string attributes)
    {
        var markup = attributes.StartsWith("h=")
            ? "<panel x=\"0\" y=\"0\" w=\"300\" h=\"300\"><expander x=\"8\" y=\"8\" w=\"200\" " + attributes + " text=\"A\"/></panel>"
            : Markup(attributes);
        Assert.Throws<FormatException>(() => MarkupDocument.Build(markup, new Binding(), _ => (1u, 16, 16)));
    }
}
```

`Markup()` uses a raw interpolated string; `{"{Inner}"}` emits the literal `{Inner}`. If the panel's `Children` order or the root's hit-testing needs the panel at a different origin, keep the coordinates relative to the panel at (0, 0) as here.

- [ ] **Step 2: Run and see them fail**

Run: `dotnet test tests/AcDream.App.Tests/AcDream.App.Tests.csproj --filter "FullyQualifiedName~MarkupExpanderTests"`
Expected: build FAILS: `controlState` parameter / `UiMarkupExpander` not found.

- [ ] **Step 3: Extend `MarkupBuildContext`** — replace the record with:

```csharp
/// <summary>
/// What an element builder outside <see cref="MarkupDocument"/> gets from the
/// document build: the same resolvers and font every built-in element uses,
/// the window's remembered control state (null when nothing is remembered),
/// and <see cref="AddChild"/>, which builds one child element into a
/// container exactly as the document builds any element.
/// </summary>
internal sealed record MarkupBuildContext(
    Func<uint, (uint, int, int)> Resolve,
    UiDatFont? DatFont,
    IMarkupIconResolver? Icons,
    MarkupColorBinder BindColor,
    IMarkupControlState? ControlState = null,
    Action<UiElement, XElement>? AddChild = null);
```

Add `using System.Xml.Linq;` at the top.

- [ ] **Step 4: Thread `controlState` through `MarkupDocument`**

- `Build`: add a last parameter `IMarkupControlState? controlState = null` and pass it to every `AddElement` call it makes.
- `AddElement`: add a last parameter `IMarkupControlState? controlState`; pass it in the `group` case's recursive call.
- `Context()` becomes:

```csharp
        MarkupBuildContext Context() => new(
            resolve, datFont, icons,
            (expression, setValue, setSource) => BindColorSource(expression, binding, setValue, setSource),
            controlState,
            (container, child) => AddElement(
                container, child, binding, resolve, datFont, icons, themedPanel, themes, controlState));
```

- Change `private static Func<bool> BindRequiredBoolReader(` to `internal static`.
- After the `case "separator": … break;` block, separated by a blank line:

```csharp
            case "expander":
                parent.AddChild(MarkupExpander.Build(el, binding, Context()));
                break;
```

- [ ] **Step 5: Create `src/AcDream.App/UI/UiMarkupExpander.cs`**

```csharp
using AcDream.App.UI.Markup;

namespace AcDream.App.UI;

/// <summary>
/// A markup expander: a header that folds away the content below it. When it
/// collapses, the siblings at or below its bottom move up by the height it
/// lost, and come back when it expands. Each moved element re-records its
/// anchor baseline, so the per-frame anchor pass keeps the new layout.
/// </summary>
public sealed class UiMarkupExpander : UiElement
{
    public const float HeaderHeight = 18f;

    public UiMarkupExpanderHeader Header { get; }

    /// <summary>Holds the authored children; y = 0 is the first pixel under the header.</summary>
    public UiPanel Content { get; }

    /// <summary>Bound state: when set, the plugin owns it and the layout follows it.</summary>
    public Func<bool>? ExpandedSource { get; set; }

    /// <summary>Called with the state a click or key press asks for.</summary>
    public Action<bool>? Changed { get; set; }

    /// <summary>The markup's <c>open</c>: where unbound state starts, and what isn't remembered.</summary>
    public bool OpenByDefault { get; set; } = true;

    internal RememberedValue? Remembered { get; set; }

    public bool IsRemembered => Remembered?.IsActive == true;

    private bool? _hostExpanded;
    private bool _laidOutExpanded = true;
    private float _expandedHeight;
    private readonly List<UiElement> _moved = new();

    public UiMarkupExpander()
    {
        ClickThrough = true;
        Header = new UiMarkupExpanderHeader(this)
        {
            AcceptsFocus = true,
            FocusOnMouseClick = false,
            TabStop = true,
            Height = HeaderHeight,
            Anchors = AnchorEdges.Left | AnchorEdges.Right | AnchorEdges.Top,
        };
        Content = new UiPanel
        {
            Top = HeaderHeight,
            ClickThrough = true,
            BackgroundColor = System.Numerics.Vector4.Zero,
            BorderColor = System.Numerics.Vector4.Zero,
            Anchors = AnchorEdges.Left | AnchorEdges.Right | AnchorEdges.Top | AnchorEdges.Bottom,
        };
        AddChild(Header);
        AddChild(Content);
    }

    /// <summary>Sizes the header and content to the expander's authored box.</summary>
    internal void LayoutParts()
    {
        Header.Left = 0f;
        Header.Top = 0f;
        Header.Width = Width;
        Content.Left = 0f;
        Content.Top = HeaderHeight;
        Content.Width = Width;
        Content.Height = MathF.Max(0f, Height - HeaderHeight);
    }

    public bool Expanded => ExpandedSource?.Invoke() ?? HostExpanded;

    private bool HostExpanded
    {
        get
        {
            if (Remembered is { } remembered && remembered.TryRefresh(out string? text))
                _hostExpanded = Parse(text) ?? OpenByDefault;
            return _hostExpanded ??= OpenByDefault;
        }
    }

    private static bool? Parse(string? text) => text switch
    {
        "true" => true,
        "false" => false,
        _ => null,
    };

    /// <summary>What a header click or Enter/Space does.</summary>
    internal void Toggle()
    {
        if (!Enabled)
            return;
        bool next = !Expanded;
        if (ExpandedSource is not null)
        {
            Changed?.Invoke(next);
            return;
        }
        _hostExpanded = next;
        Remembered?.Remember(next == OpenByDefault ? null : next ? "true" : "false");
        ApplyLayout(next);
        Changed?.Invoke(next);
    }

    protected override void OnTick(double deltaSeconds) => ApplyLayout(Expanded);

    private void ApplyLayout(bool expanded)
    {
        if (expanded == _laidOutExpanded)
            return;
        _laidOutExpanded = expanded;
        if (!expanded)
        {
            _expandedHeight = Height;
            float delta = Height - HeaderHeight;
            float bottom = Top + Height;
            _moved.Clear();
            if (Parent is { } parent)
            {
                foreach (UiElement sibling in parent.Children)
                {
                    if (ReferenceEquals(sibling, this) || sibling.Top < bottom - 0.5f)
                        continue;
                    sibling.Top -= delta;
                    sibling.CaptureCurrentAnchorBaseline();
                    _moved.Add(sibling);
                }
            }
            Content.Visible = false;
            Height = HeaderHeight;
            CaptureCurrentAnchorBaseline();
        }
        else
        {
            float delta = _expandedHeight - HeaderHeight;
            Height = _expandedHeight;
            CaptureCurrentAnchorBaseline();
            Content.Visible = true;
            foreach (UiElement sibling in _moved)
            {
                if (!ReferenceEquals(sibling.Parent, Parent))
                    continue;
                sibling.Top += delta;
                sibling.CaptureCurrentAnchorBaseline();
            }
            _moved.Clear();
        }
    }
}
```

(`CaptureCurrentAnchorBaseline` is `internal` on `UiElement` and re-records the anchor margins from the current rectangle; applying anchors at the same parent size is the identity, so nothing jumps.)

- [ ] **Step 6: Create `src/AcDream.App/UI/UiMarkupExpanderHeader.cs`**

```csharp
using System.Numerics;

namespace AcDream.App.UI;

/// <summary>
/// An expander's header: a ▸/▾ arrow and caption. It toggles its expander on
/// a press and release over it, or on Enter/Space while it has focus.
/// </summary>
public sealed class UiMarkupExpanderHeader : UiElement
{
    public static readonly Vector4 ClassicArrow = new(0.80f, 0.74f, 0.55f, 1f);
    public static readonly Vector4 FocusColor = new(1f, 0.82f, 0.25f, 1f);

    private readonly UiMarkupExpander _owner;
    private readonly UiKeyboardActivation _keyboardActivation = new();
    private bool _pressed;

    public PluginUiPalette? ThemePalette { get; set; }
    public string Text { get; set; } = string.Empty;
    public Func<string?>? TextSource { get; set; }
    public UiDatFont? DatFont { get; set; }
    public Vector4 TextColor { get; set; } = new(0.86f, 0.84f, 0.74f, 1f);

    /// <summary>Re-read every frame in place of <see cref="TextColor"/>.</summary>
    public Func<Vector4>? TextColorSource { get; set; }

    public override bool HandlesClick => true;

    internal UiMarkupExpanderHeader(UiMarkupExpander owner) => _owner = owner;

    private bool Live => Enabled && _owner.Enabled;

    public override bool OnEvent(in UiEvent e)
    {
        if (_keyboardActivation.HandleEvent(in e, TabStop, Live, _owner.Toggle))
            return true;
        switch (e.Type)
        {
            case UiEventType.MouseDown:
                _pressed = Live;
                return true;
            case UiEventType.MouseUp:
                if (_pressed && e.Data1 >= 0 && e.Data2 >= 0 && e.Data1 < Width && e.Data2 < Height)
                    _owner.Toggle();
                _pressed = false;
                return true;
            case UiEventType.CaptureChanged:
                _pressed = false;
                return true;
            case UiEventType.Click:
            case UiEventType.DoubleClick:
                return true;
        }
        return false;
    }

    private Vector4 Fade(Vector4 color) =>
        Live ? color : color with { W = color.W * UiMarkupToggle.DisabledAlpha };

    protected override void OnDraw(UiRenderContext ctx)
    {
        Vector4 arrow = Fade(ThemePalette?.Text ?? ClassicArrow);
        float top = MathF.Floor((Height - 5f) * 0.5f);
        if (_owner.Expanded)
        {
            for (int row = 0; row < 3; row++)
                ctx.DrawFill(3f + row, top + 1f + row, 5f - row * 2, 1f, arrow);
        }
        else
        {
            for (int column = 0; column < 3; column++)
                ctx.DrawFill(4f + column, top + column, 1f, 5f - column * 2, arrow);
        }

        string caption = TextSource?.Invoke() ?? Text;
        Vector4 text = Fade(TextColorSource?.Invoke() ?? TextColor);
        UiCaption.Draw(ctx, DatFont, caption, 14f, 0f, Height, text, outline: ThemePalette is null);

        if (_keyboardActivation.Focused)
            ctx.DrawRectOutline(0f, 0f, Width, Height, FocusColor, 1f);
    }
}
```

- [ ] **Step 7: Create `src/AcDream.App/UI/Markup/MarkupExpander.cs`**

```csharp
using System.Xml.Linq;

namespace AcDream.App.UI.Markup;

internal static class MarkupExpander
{
    public static UiMarkupExpander Build(XElement el, object binding, MarkupBuildContext context)
    {
        float height = MarkupDocument.F(el, "h");
        if (height < UiMarkupExpander.HeaderHeight)
        {
            throw new FormatException(
                $"<expander h=\"{height}\"> must be at least {UiMarkupExpander.HeaderHeight} (its header)");
        }

        var expander = new UiMarkupExpander
        {
            Left = MarkupDocument.F(el, "x"),
            Top = MarkupDocument.F(el, "y"),
            Width = MarkupDocument.F(el, "w"),
            Height = height,
            OpenByDefault = Open(el),
            Changed = MarkupBindings.BindBoolAction(
                (string?)el.Attribute("onchange"), binding, "expander onchange"),
        };
        expander.LayoutParts();

        string? expandedAttr = (string?)el.Attribute("expanded");
        if (expandedAttr is not null)
            expander.ExpandedSource = MarkupDocument.BindRequiredBoolReader(expandedAttr, binding, "expander expanded");

        UiMarkupExpanderHeader header = expander.Header;
        string? caption = (string?)el.Attribute("text");
        header.Text = caption ?? string.Empty;
        header.TextSource = MarkupDocument.BindString(caption, binding);
        header.DatFont = context.DatFont;
        string? color = (string?)el.Attribute("color");
        if (color is not null)
            header.TextColor = MarkupDocument.Color(color);
        context.BindColor(color, value => header.TextColor = value, source => header.TextColorSource = source);

        MarkupDocument.ApplyCommon(expander, el, binding);
        if (expander.ExpandedSource is null)
            expander.Remembered = new RememberedValue(context.ControlState, expander.Name, "expanded");

        if (context.AddChild is { } addChild)
        {
            foreach (XElement child in el.Elements())
                addChild(expander.Content, child);
        }
        return expander;
    }

    private static bool Open(XElement el) => (string?)el.Attribute("open") switch
    {
        null or "true" => true,
        "false" => false,
        var other => throw new FormatException($"<expander open=\"{other}\"> must be true or false"),
    };
}
```

- [ ] **Step 8: Run the tests**

Run: `dotnet test tests/AcDream.App.Tests/AcDream.App.Tests.csproj --filter "FullyQualifiedName~MarkupExpanderTests"`
Expected: PASS. If a geometry test fails, check `UiElement.CaptureCurrentAnchorBaseline` and `ApplyAnchor` (UiElement.cs ~526-580) before changing the widget, and report what you found; don't change expected positions.

- [ ] **Step 9: Run all markup tests and commit**

Run: `dotnet test tests/AcDream.App.Tests/AcDream.App.Tests.csproj --filter "FullyQualifiedName~AcDream.App.Tests.UI.Markup|FullyQualifiedName~AcDream.App.Tests.UI.UiFieldTests|FullyQualifiedName~RememberedValueTests"` → PASS.

```bash
git checkout -- '*.lock.json' 2>/dev/null
git add src/AcDream.App/UI tests/AcDream.App.Tests/UI
git commit -m "feat(ui): <expander> markup element

A header that folds its content away; siblings below it move up while
it is collapsed. Bound state is the plugin's; unbound state starts from
open and, for a named expander, is remembered per character. Builds can
now carry the window's remembered state and build child markup into a
container.

Claude-Session: https://claude.ai/code/session_01UzLnYkJ6ifzKGaeNwgsp8W"
```

---

### Task 3: Theme, caption relabel and drawing

**Goal:** The expander takes the plugin theme, plugins can relabel its header with `SetControlLabel`, and its drawing is pinned.

**Files:**
- Modify: `src/AcDream.App/UI/PluginMarkupTheme.cs`, `src/AcDream.App/Plugins/BufferedUiRegistry.cs`, `src/AcDream.Plugin.Abstractions/IUiRegistry.cs` (doc comment only)
- Test: `tests/AcDream.App.Tests/UI/MarkupExpanderTests.cs`, and the existing SetControlLabel test file

**Acceptance Criteria:**
- [ ] Under `theme="plugin"` (Moss) the header's `ThemePalette` is the palette and its caption colour is `Moss.Text` unless `color` is set; the modern font (when set) reaches the header.
- [ ] Classic: expanded draws 3 `ClassicArrow` quads in rows (▾), collapsed 3 in columns (▸); disabled draws them at `DisabledAlpha`; a focused header draws 4 `FocusColor` quads.
- [ ] `SetControlLabel` on a named expander sets its header text and returns true; the `IUiRegistry.SetControlLabel` comment names expander.
- [ ] Children inside an expander in a themed window are themed (e.g. a `<toggle>` inside gets the palette).

**Verify:** `dotnet test tests/AcDream.App.Tests/AcDream.App.Tests.csproj --filter "FullyQualifiedName~MarkupExpanderTests|FullyQualifiedName~BufferedUiRegistryTests"` → all pass.

**Steps:**

- [ ] **Step 1: Write the tests** — append inside `MarkupExpanderTests`:

```csharp
    [Fact]
    public void Expander_ArrowPointsDownWhenOpenAndRightWhenClosed()
    {
        var (root, _, expander) = Mount("", new Binding());
        var open = MarkupDrawProbe.QuadBounds(MarkupDrawProbe.Draw(expander.Header), UiMarkupExpanderHeader.ClassicArrow);
        Assert.Equal(3, open.Count);
        Assert.All(open, q => Assert.Equal(1f, q.H, 3));
        ClickHeader(root);
        var closed = MarkupDrawProbe.QuadBounds(MarkupDrawProbe.Draw(expander.Header), UiMarkupExpanderHeader.ClassicArrow);
        Assert.Equal(3, closed.Count);
        Assert.All(closed, q => Assert.Equal(1f, q.W, 3));
    }

    [Fact]
    public void Expander_DisabledDrawsFaded()
    {
        var (_, _, expander) = Mount("enabled=\"false\"", new Binding());
        var renderer = MarkupDrawProbe.Draw(expander.Header);
        var faded = UiMarkupExpanderHeader.ClassicArrow with { W = UiMarkupToggle.DisabledAlpha };
        Assert.Equal(3, MarkupDrawProbe.CountQuads(renderer, faded));
        Assert.Equal(0, MarkupDrawProbe.CountQuads(renderer, UiMarkupExpanderHeader.ClassicArrow));
    }

    [Fact]
    public void Expander_FocusDrawsAnOutline()
    {
        var (_, _, expander) = Mount("", new Binding());
        expander.Header.OnEvent(new UiEvent { Type = UiEventType.FocusGained });
        var renderer = MarkupDrawProbe.Draw(expander.Header);
        Assert.Equal(4, MarkupDrawProbe.CountQuads(renderer, UiMarkupExpanderHeader.FocusColor));
    }

    [Fact]
    public void Expander_PluginThemeReachesTheHeaderAndTheChildren()
    {
        var themes = new PluginUiThemeSettings { Theme = PluginUiTheme.Moss };
        var panel = MarkupDocument.Build("""
            <panel theme="plugin" x="0" y="0" w="400" h="300">
              <expander name="adv" x="8" y="8" w="200" h="100" text="Advanced">
                <toggle x="4" y="4" w="120" h="16" text="Inner" checked="{AdvOpen}"/>
              </expander>
            </panel>
            """, new Binding(), _ => (1u, 16, 16), themes: themes);
        var expander = panel.Children.OfType<UiMarkupExpander>().Single();
        Assert.Same(PluginUiPalette.Moss, expander.Header.ThemePalette);
        Assert.Equal(PluginUiPalette.Moss.Text, expander.Header.TextColor);
        var toggle = Assert.IsType<UiMarkupToggle>(expander.Content.Children[0]);
        Assert.Same(PluginUiPalette.Moss, toggle.ThemePalette);
    }

    [Fact]
    public void Expander_ExplicitColorSurvivesTheTheme()
    {
        var themes = new PluginUiThemeSettings { Theme = PluginUiTheme.Moss };
        var panel = MarkupDocument.Build("""
            <panel theme="plugin" x="0" y="0" w="400" h="300">
              <expander x="8" y="8" w="200" h="100" text="A" color="#FFFF0000"/>
            </panel>
            """, new Binding(), _ => (1u, 16, 16), themes: themes);
        var expander = panel.Children.OfType<UiMarkupExpander>().Single();
        Assert.Equal(new System.Numerics.Vector4(1f, 0f, 0f, 1f), expander.Header.TextColor);
    }
```

For `SetControlLabel`, add a test next to the existing `SetControlLabelReachesCheckboxes` test (find it with `grep -rn SetControlLabelReachesCheckboxes tests/`), mirroring it with an `<expander name="adv" … text="Old"/>` and asserting `expander.Header.Text == "New"` and `expander.Header.TextSource is null`.

- [ ] **Step 2: Run and see the theme and label tests fail**

Run: `dotnet test tests/AcDream.App.Tests/AcDream.App.Tests.csproj --filter "FullyQualifiedName~MarkupExpanderTests|FullyQualifiedName~BufferedUiRegistryTests"`
Expected: the theme and SetControlLabel tests FAIL; the Classic draw tests pass.

- [ ] **Step 3: Theme** — in `PluginMarkupTheme.Register`'s font switch, after the radio line:

```csharp
                case UiMarkupExpander expander: Bind(() => expander.Header.DatFont, f => expander.Header.DatFont = f); break;
```

In the second switch, after the `case UiMarkupSeparator separator:` section:

```csharp
            case UiMarkupExpander expander:
                var expanderText = expander.Header.TextColor;
                panel.AddThemeAction(p => { expander.Header.ThemePalette = p;
                    if (Default("color")) expander.Header.TextColor = p?.Text ?? expanderText; });
                break;
```

- [ ] **Step 4: `SetControlLabel`** — in `BufferedUiRegistry.SetControlLabel`, after the `UiMarkupCheckbox` case:

```csharp
            case UiMarkupExpander expander:
                expander.Header.TextSource = null;
                expander.Header.Text = label;
                return true;
```

In `IUiRegistry.cs`, update the `SetControlLabel` doc comment's list ("button, toggle, checkbox or label") to include expander.

- [ ] **Step 5: Run, then commit**

Run: `dotnet test tests/AcDream.App.Tests/AcDream.App.Tests.csproj --filter "FullyQualifiedName~MarkupExpanderTests|FullyQualifiedName~BufferedUiRegistryTests|FullyQualifiedName~AcDream.App.Tests.UI.Markup"` → PASS.

```bash
git checkout -- '*.lock.json' 2>/dev/null
git add src/AcDream.App src/AcDream.Plugin.Abstractions/IUiRegistry.cs tests/AcDream.App.Tests
git commit -m "feat(ui): plugin theme and SetControlLabel for <expander>

Claude-Session: https://claude.ai/code/session_01UzLnYkJ6ifzKGaeNwgsp8W"
```

---

### Task 4: Wire the store into the client

**Goal:** Plugin windows in the running client remember their expanders per character, and the store is saved with the window layout.

**Files:**
- Modify: `src/AcDream.App/UI/RetailUiRuntime.cs`

**Acceptance Criteria:**
- [ ] `RetailUiRuntime` holds one `MarkupControlStateStore`, created on first use in `MountPlugins` from `_bindings.Persistence?.Store` and `_bindings.Persistence?.CharacterKey ?? (() => "default")`.
- [ ] Each plugin panel is built with `controlState: _controlState.ForWindow(panel.WindowName)`.
- [ ] `SaveLayout()` calls `_controlState?.Save()` after the layout is saved.
- [ ] Release build clean; the App test filter for UI still passes.

**Verify:** `dotnet build AcDream.slnx -c Release` → 0 warnings, 0 errors; `git show --stat HEAD` touches only RetailUiRuntime.cs.

**Steps:**

- [ ] **Step 1: Field** — next to `private PluginUiThemeSettings? _pluginThemes;` add:

```csharp
    /// <summary>What plugin windows' markup controls remember per character; saved with the layout.</summary>
    private MarkupControlStateStore? _controlState;
```

(Add `using AcDream.App.UI.Markup;` if the file lacks it.)

- [ ] **Step 2: Create it in `MountPlugins`** — right after the `if (_pluginThemes is null) { … }` block:

```csharp
        _controlState ??= new MarkupControlStateStore(
            _bindings.Persistence?.Store,
            _bindings.Persistence?.CharacterKey ?? (() => "default"));
```

- [ ] **Step 3: Pass it to each panel** — in the `MarkupDocument.Build(` call inside the panel loop, add the final argument:

```csharp
                    iconResolver, _pluginThemes,
                    controlState: _controlState.ForWindow(panel.WindowName));
```

(`panel.WindowName` is the registered, plugin-scoped window id the window manager uses; keep the existing arguments as they are.)

- [ ] **Step 4: Save it** — in `SaveLayout()`:

```csharp
    public void SaveLayout()
    {
        _persistence?.SaveAll();
        _controlState?.Save();
        SaveChatWindowFilters();
    }
```

- [ ] **Step 5: Build, test, commit**

```bash
dotnet build AcDream.slnx -c Release 2>&1 | grep -E ': error|: warning|Warning\(s\)|Error\(s\)' | sort -u
dotnet test tests/AcDream.App.Tests/AcDream.App.Tests.csproj --filter "FullyQualifiedName~AcDream.App.Tests.UI" 2>&1 | grep -E 'Passed!|Failed!'
git checkout -- '*.lock.json' 2>/dev/null
```
Expected: 0 warnings, 0 errors. The UI filter may show the known baseline failures (compare by name against `/private/tmp/claude-501/-Users-davidsmith-code-OpenAC-OpenAC/mc-base.txt`); none may be new.

```bash
git add src/AcDream.App/UI/RetailUiRuntime.cs
git commit -m "feat(ui): plugin windows remember markup control state per character

Each plugin window gets its own view of one store, saved with the
window layout.

Claude-Session: https://claude.ai/code/session_01UzLnYkJ6ifzKGaeNwgsp8W"
```

---

### Task 5: Docs

**Goal:** `docs/plugin-ui-markup.md` documents `<expander>` and fixes plan 4's two separator nits.

**Files:**
- Modify: `docs/plugin-ui-markup.md`

**Acceptance Criteria:**
- [ ] Elements table has an `expander` row after `separator`; Bindings table has `expander expanded` and `expander onchange` rows.
- [ ] The focus sentence names `expander` among the controls Enter/Space activate (its header).
- [ ] A `## Expander` section after `## Separator` covers: header and content coordinates, `h` ≥ 18, open/expanded/onchange (who owns the state), siblings moving, what is remembered (named + unbound, per character, cleared at its default), keyboard, and the older-host line.
- [ ] Separator section: the `theme:border|…` paragraph sits directly under its example (before the older-host line), and the long unwrapped line is wrapped like the rest of the file.

**Verify:** `grep -c 'expander' docs/plugin-ui-markup.md` → at least 8.

**Steps:**

- [ ] **Step 1: Tables**

Elements table, after the `separator` row:

```markdown
| `expander` | A header that folds away the content below it | `name x y w h text expanded onchange open color` |
```

Bindings table, after the radio rows:

```markdown
| `expander expanded` | `bool`; when bound, the plugin owns the state | throws |
| `expander onchange` | `Action<bool>`, the new state | throws |
```

- [ ] **Step 2: Focus sentence** — add `expander` (its header) to the list of controls whose action Enter or Space runs.

- [ ] **Step 3: Section** — after `## Separator`:

````markdown
## Expander

```xml
<expander name="advanced" x="8" y="60" w="240" h="120" text="Advanced">
  <toggle x="4" y="4" w="140" h="16" text="Loot everything" checked="{LootAll}" onclick="{ToggleLootAll}"/>
</expander>
<label x="8" y="190" text="Below the expander"/>
```

An `<expander>` is a header (an arrow and its `text`) with content beneath
it. `h` includes the 18 px header and must be at least 18. Its children are
placed below the header: a child at `y="0"` sits right under it.

Clicking the header, or pressing Enter or Space while it has focus, folds
the content away or brings it back. While it is folded, every element in
the same parent whose top is at or below the expander's bottom moves up by
the height it gave up, and moves back when it opens; elements above or
beside it stay put.

- Without `expanded` the client keeps the state. It starts from `open`
  (`true` by default, or `false`). If the expander has a `name`, the client
  remembers the player's choice per character with the window's other
  settings, and forgets it again when it matches `open`. `onchange`, if
  given, is told each new state.
- With `expanded="{Bool}"` the plugin keeps the state: a click only calls
  `onchange` with the state asked for, and the expander follows the property.
  Without `onchange` it only displays. Bound state is never remembered by
  the client.

On a host older than expanders, an `<expander>` fails the build of its
window as unknown.
````

- [ ] **Step 4: Separator nits** — in `## Separator`, move the paragraph explaining `color="theme:border|#FF61573B"` so it directly follows the example block, and re-wrap the long line (the one containing "A separator with zero width or height draws nothing") to about 78 columns like the surrounding prose.

- [ ] **Step 5: Commit**

```bash
git add docs/plugin-ui-markup.md
git commit -m "docs(ui): document <expander>; tidy the separator section

Claude-Session: https://claude.ai/code/session_01UzLnYkJ6ifzKGaeNwgsp8W"
```

---

### Task 6: Series gate

**Goal:** Release build clean; no new failures in App, Plugin or UI.Abstractions tests; every commit builds.

**Files:** none changed.

**Acceptance Criteria:**
- [ ] `dotnet build AcDream.slnx -c Release` → 0 errors, 0 warnings.
- [ ] App + Plugin failures ⊆ `mc-base.txt`; UI.Abstractions failures ⊆ `mc-uiabs-base.txt` (empty).
- [ ] Each of the series' commits builds and passes `UI.Markup|UiFieldTests|RememberedValueTests` on its own.
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
done | norm > $T/mc5-after.txt
dotnet test tests/AcDream.UI.Abstractions.Tests/AcDream.UI.Abstractions.Tests.csproj -c Release 2>&1 | norm > $T/mc5-uiabs-after.txt
echo "new App/Plugin:"; comm -13 $T/mc-base.txt $T/mc5-after.txt
echo "new UI.Abstractions:"; comm -13 $T/mc-uiabs-base.txt $T/mc5-uiabs-after.txt
git checkout -- '*.lock.json' 2>/dev/null; git status --short
```
Expected: `0 Warning(s)`, `0 Error(s)`, nothing after either "new" line.

- [ ] **Step 2: Each commit builds alone**

```bash
cd /Users/davidsmith/code/OpenAC/OpenAC
for c in $(git rev-list --reverse 7696c39d..feat/markup-controls); do
  git worktree add -q --detach .worktrees/mc-commit-check $c
  (cd .worktrees/mc-commit-check && dotnet test tests/AcDream.App.Tests/AcDream.App.Tests.csproj -c Release \
     --filter "FullyQualifiedName~AcDream.App.Tests.UI.Markup|FullyQualifiedName~AcDream.App.Tests.UI.UiFieldTests|FullyQualifiedName~RememberedValueTests" 2>&1 \
     | grep -E ': error|: warning|Passed!|Failed!' | sort -u | sed "s/^/$(git rev-parse --short $c) /")
  git worktree remove --force .worktrees/mc-commit-check
done
```
Expected: one `Passed!` per commit, no `error`/`warning`.

- [ ] **Step 3: Branch contents**

```bash
cd /Users/davidsmith/code/OpenAC/OpenAC/.worktrees/markup-controls
git log --oneline 7696c39d..HEAD
git diff --stat 391e709a..HEAD | grep superpowers || echo "no superpowers docs"
```
Expected: the 5 commits of Tasks 1-5; "no superpowers docs".

No commit.
