# Markup controls, plan 4: `<separator>` + remembered control state Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers-extended-cc:subagent-driven-development (recommended) or superpowers-extended-cc:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Ship `<separator>`, and build the per-character store that later controls (expander, split, list sort/widths/tree) use to remember state the plugin hasn't bound.

**Architecture:** `SettingsStore` gains a `markupControls` section: `markupControls[toonKey][window][control][kind] = "value"`. `MarkupControlStateStore` keeps the current character's values in memory, loads them when the character key changes (flushing the previous character's unsaved values first), writes them on `Save()`, and hands each window an `IMarkupControlState` with a `Generation` counter controls use to notice a character switch. Nothing reads the store yet; plan 5 (expander) wires it into `MarkupDocument.Build` and `RetailUiRuntime` together with its first consumer. `<separator>` is a click-through 1 px line.

**Tech Stack:** C# / .NET 10, System.Text.Json, xUnit.

**Spec:** `docs/superpowers/specs/2026-10-21-markup-controls-design.md` (branch `docs/markup-controls-spec`): "Shared rules → Remembered state", section 1 (shared infrastructure), section 5 (separator).

## Global Constraints

- Worktree `/Users/davidsmith/code/OpenAC/OpenAC/.worktrees/markup-controls`, branch `feat/markup-controls` (HEAD 0b42572a, from upstream 391e709a). Never merge fork `main` in; never add `docs/superpowers/` files there. Push only when the controller says so.
- Every shell: `export DOTNET_ROOT=$HOME/.dotnet PATH=$HOME/.dotnet:$PATH`. After building, `git checkout -- '*.lock.json'`; never commit lock files.
- Every commit must build and pass its own tests.
- Commit messages end with `Claude-Session: https://claude.ai/code/session_01UzLnYkJ6ifzKGaeNwgsp8W`.
- Remembered state (spec "Shared rules"): per character, per window, per control name, per kind; values are short strings; unknown or malformed stored values are ignored; saved with the window layout (wiring in plan 5); a blank character key is never written.
- `SettingsStore` saves must keep every other section intact (it loads, mutates and rewrites the whole file).
- `<separator>`: 1 px line, horizontal unless `h > w`, centred across its box; default colour `UiCheckLamp.UncheckedInner` in Classic and the palette `Border` under the theme; `color` (literal or bound) overrides both; not focusable, no events, clicks pass through it.

**User decisions (already made):**
- One branch, one commit series per control, upstream-portable.
- Remembered state is "both": host remembers unbound state per character/window/control; bound state belongs to the plugin.
- Plan 4 = `<separator>` + the remembered-state groundwork; the expander follows in its own plan.

---

## File Structure

| File | Responsibility |
|---|---|
| `src/AcDream.UI.Abstractions/Panels/Settings/SettingsStore.cs` | `MarkupControlKey`; `LoadMarkupControlState` / `SaveMarkupControlState` |
| `src/AcDream.App/UI/Markup/IMarkupControlState.cs` (new) | What one window's controls can remember |
| `src/AcDream.App/UI/Markup/MarkupControlStateStore.cs` (new) | In-memory per-character values, character switching, `Save()` |
| `src/AcDream.App/UI/UiMarkupSeparator.cs` (new) | The separator line |
| `src/AcDream.App/UI/Markup/MarkupSeparator.cs` (new) | `<separator>` builder |
| `src/AcDream.App/UI/MarkupDocument.cs` | `case "separator"` |
| `src/AcDream.App/UI/PluginMarkupTheme.cs` | Separator colour under the theme |
| `tests/AcDream.UI.Abstractions.Tests/Panels/Settings/MarkupControlStateSettingsTests.cs` (new) | Settings section tests |
| `tests/AcDream.App.Tests/UI/MarkupControlStateStoreTests.cs` (new) | Store tests |
| `tests/AcDream.App.Tests/UI/MarkupSeparatorTests.cs` (new) | Separator tests |
| `tests/AcDream.App.Tests/UI/MarkupDrawProbe.cs` | `QuadBounds` helper |
| `docs/plugin-ui-markup.md` | Separator docs |

---

### Task 0: Settings test baseline

**Goal:** Record the `AcDream.UI.Abstractions.Tests` failures before this series touches `SettingsStore` (earlier series never ran that project).

**Files:**
- Create: `/private/tmp/claude-501/-Users-davidsmith-code-OpenAC-OpenAC/mc-uiabs-base.txt`

**Acceptance Criteria:**
- [ ] The file lists the failing test names of `AcDream.UI.Abstractions.Tests` at 0b42572a, timings stripped, sorted (may be empty).

**Verify:** `wc -l < /private/tmp/claude-501/-Users-davidsmith-code-OpenAC-OpenAC/mc-uiabs-base.txt` → a count.

**Steps:**

- [ ] **Step 1: Run it on the untouched branch**

```bash
export DOTNET_ROOT=$HOME/.dotnet PATH=$HOME/.dotnet:$PATH
cd /Users/davidsmith/code/OpenAC/OpenAC/.worktrees/markup-controls
git status --short; git log --oneline -1
dotnet test tests/AcDream.UI.Abstractions.Tests/AcDream.UI.Abstractions.Tests.csproj -c Release 2>&1 \
  | tee /private/tmp/claude-501/-Users-davidsmith-code-OpenAC-OpenAC/mc-uiabs-base.log \
  | grep -E '^[[:space:]]+Failed [A-Za-z]' | sed -E 's/^[[:space:]]+Failed //; s/ \[[^]]*\]$//' | sort -u \
  > /private/tmp/claude-501/-Users-davidsmith-code-OpenAC-OpenAC/mc-uiabs-base.txt
grep -E '^(Passed!|Failed!)' /private/tmp/claude-501/-Users-davidsmith-code-OpenAC-OpenAC/mc-uiabs-base.log
git checkout -- '*.lock.json' 2>/dev/null; git status --short
```
Expected: clean tree at `0b42572a`; a summary line; the file exists.

No commit.

---

### Task 1: The `markupControls` settings section

**Goal:** `SettingsStore` loads and saves what a character's markup controls remember, without disturbing any other section.

**Files:**
- Modify: `src/AcDream.UI.Abstractions/Panels/Settings/SettingsStore.cs`
- Test: `tests/AcDream.UI.Abstractions.Tests/Panels/Settings/MarkupControlStateSettingsTests.cs`

**Acceptance Criteria:**
- [ ] `SaveMarkupControlState(toon, values)` then `LoadMarkupControlState(toon)` returns the same keys and values.
- [ ] A save replaces that character's whole set (keys left out are gone) and leaves other characters and every other section (e.g. `windowPositions`) intact; another section's save leaves `markupControls` intact.
- [ ] A missing file loads empty; non-object windows/controls and non-string values are skipped while valid siblings still load; a blank or null key throws `ArgumentException`.

**Verify:** `dotnet test tests/AcDream.UI.Abstractions.Tests/AcDream.UI.Abstractions.Tests.csproj --filter "FullyQualifiedName~MarkupControlStateSettingsTests"` → all pass.

**Steps:**

- [ ] **Step 1: Write the failing tests** — create `tests/AcDream.UI.Abstractions.Tests/Panels/Settings/MarkupControlStateSettingsTests.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.IO;
using AcDream.UI.Abstractions.Panels.Settings;

namespace AcDream.UI.Abstractions.Tests.Panels.Settings;

public sealed class MarkupControlStateSettingsTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"markup-controls-{Guid.NewGuid():N}.json");

    public void Dispose() => File.Delete(_path);

    private static Dictionary<MarkupControlKey, string> Values(params (string W, string C, string K, string V)[] entries)
    {
        var values = new Dictionary<MarkupControlKey, string>();
        foreach (var (w, c, k, v) in entries)
            values[new MarkupControlKey(w, c, k)] = v;
        return values;
    }

    [Fact]
    public void SavedValuesLoadBack()
    {
        var store = new SettingsStore(_path);
        var saved = Values(("nav", "adv", "expanded", "false"), ("nav", "mobs", "widths", "120,40"),
            ("loot", "adv", "expanded", "true"));
        store.SaveMarkupControlState("alice", saved);
        Assert.Equal(saved, store.LoadMarkupControlState("alice"));
    }

    [Fact]
    public void ASaveReplacesTheCharactersSetAndKeepsEverythingElse()
    {
        var store = new SettingsStore(_path);
        store.SaveWindowPosition("alice", "chat", new UiWindowPosition(10, 20));
        store.SaveMarkupControlState("bob", Values(("nav", "adv", "expanded", "true")));
        store.SaveMarkupControlState("alice", Values(("nav", "adv", "expanded", "false"), ("nav", "x", "k", "1")));
        store.SaveMarkupControlState("alice", Values(("nav", "adv", "expanded", "true")));

        Assert.Equal(Values(("nav", "adv", "expanded", "true")), store.LoadMarkupControlState("alice"));
        Assert.Equal(Values(("nav", "adv", "expanded", "true")), store.LoadMarkupControlState("bob"));
        Assert.Equal(new UiWindowPosition(10, 20), store.LoadWindowPosition("alice", "chat"));
    }

    [Fact]
    public void OtherSectionsSavesKeepTheMarkupControls()
    {
        var store = new SettingsStore(_path);
        store.SaveMarkupControlState("alice", Values(("nav", "adv", "expanded", "false")));
        store.SaveWindowPosition("alice", "chat", new UiWindowPosition(1, 2));
        store.SavePluginUiTheme("Moss");
        Assert.Equal(Values(("nav", "adv", "expanded", "false")), store.LoadMarkupControlState("alice"));
    }

    [Fact]
    public void AMissingFileLoadsEmpty()
    {
        Assert.Empty(new SettingsStore(_path).LoadMarkupControlState("alice"));
    }

    [Fact]
    public void MalformedEntriesAreSkipped()
    {
        File.WriteAllText(_path, """
            {"markupControls":{"alice":{
              "nav":{"adv":{"expanded":5,"open":"yes"},"bad":3},
              "loot":7
            },"bob":"nope"}}
            """);
        var store = new SettingsStore(_path);
        Assert.Equal(Values(("nav", "adv", "open", "yes")), store.LoadMarkupControlState("alice"));
        Assert.Empty(store.LoadMarkupControlState("bob"));
    }

    [Fact]
    public void ANonObjectSectionLoadsEmpty()
    {
        File.WriteAllText(_path, """{"markupControls":42}""");
        Assert.Empty(new SettingsStore(_path).LoadMarkupControlState("alice"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void ABlankCharacterKeyThrows(string? key)
    {
        var store = new SettingsStore(_path);
        Assert.ThrowsAny<ArgumentException>(() => store.LoadMarkupControlState(key!));
        Assert.ThrowsAny<ArgumentException>(() => store.SaveMarkupControlState(key!, Values()));
    }
}
```

- [ ] **Step 2: Run and see them fail**

Run: `dotnet test tests/AcDream.UI.Abstractions.Tests/AcDream.UI.Abstractions.Tests.csproj --filter "FullyQualifiedName~MarkupControlStateSettingsTests"`
Expected: build FAILS: `MarkupControlKey` not found.

- [ ] **Step 3: Add the key and the two methods**

After `public readonly record struct UiWindowPosition(float X, float Y);` add:

```csharp
/// <summary>One remembered markup control value: which window, which named control, which kind of state.</summary>
public readonly record struct MarkupControlKey(string Window, string Control, string Kind);
```

Inside `SettingsStore`, after `SaveWindowPosition`, add:

```csharp
    /// <summary>
    /// What a character's markup controls remember, read from
    /// <c>markupControls[toonKey][window][control][kind]</c>. Anything that is
    /// not an object where one is expected, or not a string value, is skipped.
    /// </summary>
    public Dictionary<MarkupControlKey, string> LoadMarkupControlState(string toonKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(toonKey);
        var values = new Dictionary<MarkupControlKey, string>();
        if (!File.Exists(_path)) return values;
        try
        {
            var root = JsonNode.Parse(File.ReadAllText(_path)) as JsonObject;
            if (root?["markupControls"] is not JsonObject characters
                || characters[toonKey] is not JsonObject windows)
            {
                return values;
            }
            foreach (var (window, windowNode) in windows)
            {
                if (windowNode is not JsonObject controls) continue;
                foreach (var (control, controlNode) in controls)
                {
                    if (controlNode is not JsonObject kinds) continue;
                    foreach (var (kind, valueNode) in kinds)
                    {
                        if (valueNode is JsonValue value && value.TryGetValue(out string? text) && text is not null)
                            values[new MarkupControlKey(window, control, kind)] = text;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"settings: failed to load markup control state from {_path}: {ex.Message}");
            values.Clear();
        }
        return values;
    }

    /// <summary>
    /// Replaces everything a character's markup controls remember, keeping
    /// every other section and every other character.
    /// </summary>
    public void SaveMarkupControlState(string toonKey, IReadOnlyDictionary<MarkupControlKey, string> values)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(toonKey);
        ArgumentNullException.ThrowIfNull(values);

        JsonObject root = LoadMutableRoot();
        var windows = new JsonObject();
        foreach (var (key, value) in values)
        {
            JsonObject controls = GetOrCreateObject(windows, key.Window);
            JsonObject kinds = GetOrCreateObject(controls, key.Control);
            kinds[key.Kind] = value;
        }
        GetOrCreateObject(root, "markupControls")[toonKey] = windows;
        root["version"] = CurrentSchemaVersion;
        WriteMutableRoot(root);
    }
```

- [ ] **Step 4: Run the tests**

Run: `dotnet test tests/AcDream.UI.Abstractions.Tests/AcDream.UI.Abstractions.Tests.csproj --filter "FullyQualifiedName~MarkupControlStateSettingsTests|FullyQualifiedName~WindowPlacementStoreTests|FullyQualifiedName~SettingsStoreTests"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git checkout -- '*.lock.json' 2>/dev/null
git add src/AcDream.UI.Abstractions/Panels/Settings/SettingsStore.cs tests/AcDream.UI.Abstractions.Tests/Panels/Settings/MarkupControlStateSettingsTests.cs
git commit -m "feat(settings): a section for what markup controls remember

markupControls[toon][window][control][kind] holds short strings that
plugin controls remember for a character, such as an expander's open
state. Malformed entries are skipped.

Claude-Session: https://claude.ai/code/session_01UzLnYkJ6ifzKGaeNwgsp8W"
```

---

### Task 2: `MarkupControlStateStore`

**Goal:** One in-memory store for every markup window's remembered values, following the logged-in character and saving on request.

**Files:**
- Create: `src/AcDream.App/UI/Markup/IMarkupControlState.cs`
- Create: `src/AcDream.App/UI/Markup/MarkupControlStateStore.cs`
- Test: `tests/AcDream.App.Tests/UI/MarkupControlStateStoreTests.cs`

**Acceptance Criteria:**
- [ ] `ForWindow(name).Set(control, kind, value)` then `Get` returns it; windows, controls and kinds don't collide.
- [ ] `Save()` writes only when something changed; values set but not saved are not in the file; saved values load into a fresh store.
- [ ] When the character key changes, the previous character's unsaved changes are written first, the new character's values are loaded, and `Generation` increases; `Generation` is stable while the character stays the same.
- [ ] With no `SettingsStore`, values still work for the session and `Save()` does nothing; a blank character key is never written.
- [ ] `ForWindow` with a blank name throws.

**Verify:** `dotnet test tests/AcDream.App.Tests/AcDream.App.Tests.csproj --filter "FullyQualifiedName~MarkupControlStateStoreTests"` → all pass.

**Steps:**

- [ ] **Step 1: Write the failing tests** — create `tests/AcDream.App.Tests/UI/MarkupControlStateStoreTests.cs`:

```csharp
using System;
using System.IO;
using AcDream.App.UI.Markup;
using AcDream.UI.Abstractions.Panels.Settings;

namespace AcDream.App.Tests.UI;

public sealed class MarkupControlStateStoreTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"markup-state-{Guid.NewGuid():N}.json");
    private string _character = "alice";

    public void Dispose()
    {
        if (File.Exists(_path)) File.Delete(_path);
    }

    private MarkupControlStateStore NewStore() => new(new SettingsStore(_path), () => _character);

    [Fact]
    public void ValuesAreKeptPerWindowControlAndKind()
    {
        var store = NewStore();
        var nav = store.ForWindow("nav");
        var loot = store.ForWindow("loot");
        nav.Set("adv", "expanded", "false");
        nav.Set("adv", "widths", "10");
        loot.Set("adv", "expanded", "true");
        Assert.Equal("false", nav.Get("adv", "expanded"));
        Assert.Equal("10", nav.Get("adv", "widths"));
        Assert.Equal("true", loot.Get("adv", "expanded"));
        Assert.Null(nav.Get("other", "expanded"));
    }

    [Fact]
    public void SavedValuesLoadIntoAFreshStore()
    {
        var store = NewStore();
        store.ForWindow("nav").Set("adv", "expanded", "false");
        store.Save();
        Assert.Equal("false", NewStore().ForWindow("nav").Get("adv", "expanded"));
    }

    [Fact]
    public void UnsavedValuesAreNotWritten()
    {
        NewStore().ForWindow("nav").Set("adv", "expanded", "false");
        Assert.Null(NewStore().ForWindow("nav").Get("adv", "expanded"));
    }

    [Fact]
    public void SaveWritesOnlyWhenSomethingChanged()
    {
        var store = NewStore();
        var nav = store.ForWindow("nav");
        nav.Set("adv", "expanded", "false");
        store.Save();
        File.Delete(_path);
        store.Save();
        Assert.False(File.Exists(_path));
        nav.Set("adv", "expanded", "false");
        store.Save();
        Assert.False(File.Exists(_path));
        nav.Set("adv", "expanded", "true");
        store.Save();
        Assert.True(File.Exists(_path));
    }

    [Fact]
    public void EachCharacterHasItsOwnValues()
    {
        var store = NewStore();
        var nav = store.ForWindow("nav");
        nav.Set("adv", "expanded", "false");
        int aliceGeneration = nav.Generation;

        _character = "bob";
        Assert.Null(nav.Get("adv", "expanded"));
        Assert.True(nav.Generation > aliceGeneration);
        nav.Set("adv", "expanded", "true");

        _character = "alice";
        Assert.Equal("false", nav.Get("adv", "expanded"));
        store.Save();

        var fresh = NewStore().ForWindow("nav");
        Assert.Equal("false", fresh.Get("adv", "expanded"));
        _character = "bob";
        Assert.Equal("true", fresh.Get("adv", "expanded"));
    }

    [Fact]
    public void GenerationIsStableForTheSameCharacter()
    {
        var nav = NewStore().ForWindow("nav");
        int first = nav.Generation;
        nav.Set("adv", "expanded", "false");
        Assert.Equal(first, nav.Generation);
    }

    [Fact]
    public void WithoutASettingsFileValuesLastTheSession()
    {
        var store = new MarkupControlStateStore(null, () => "alice");
        var nav = store.ForWindow("nav");
        nav.Set("adv", "expanded", "false");
        store.Save();
        Assert.Equal("false", nav.Get("adv", "expanded"));
    }

    [Fact]
    public void ABlankCharacterIsNeverWritten()
    {
        _character = "";
        var store = NewStore();
        store.ForWindow("nav").Set("adv", "expanded", "false");
        store.Save();
        Assert.False(File.Exists(_path));
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public void ABlankWindowNameThrows(string name)
    {
        Assert.ThrowsAny<ArgumentException>(() => NewStore().ForWindow(name));
    }
}
```

- [ ] **Step 2: Run and see them fail**

Run: `dotnet test tests/AcDream.App.Tests/AcDream.App.Tests.csproj --filter "FullyQualifiedName~MarkupControlStateStoreTests"`
Expected: build FAILS: `MarkupControlStateStore` not found.

- [ ] **Step 3: Create `src/AcDream.App/UI/Markup/IMarkupControlState.cs`**

```csharp
namespace AcDream.App.UI.Markup;

/// <summary>
/// What one markup window's controls remember for the logged-in character:
/// short string values keyed by the control's <c>name</c> and a kind such as
/// <c>"expanded"</c> or <c>"widths"</c>.
/// </summary>
public interface IMarkupControlState
{
    /// <summary>
    /// Changes whenever every remembered value may have changed, which is when
    /// another character logs in. A control that caches a value re-reads it
    /// when this moves.
    /// </summary>
    int Generation { get; }

    string? Get(string control, string kind);

    void Set(string control, string kind, string value);
}
```

- [ ] **Step 4: Create `src/AcDream.App/UI/Markup/MarkupControlStateStore.cs`**

```csharp
using AcDream.UI.Abstractions.Panels.Settings;

namespace AcDream.App.UI.Markup;

/// <summary>
/// Every markup window's remembered control values for the logged-in
/// character, kept in memory and written to the settings file by
/// <see cref="Save"/>. The character is read on every access, so a relog
/// writes the old character's changes and loads the new one's.
/// </summary>
public sealed class MarkupControlStateStore
{
    private readonly SettingsStore? _store;
    private readonly Func<string> _characterKey;
    private Dictionary<MarkupControlKey, string> _values = new();
    private string? _character;
    private int _generation;
    private bool _dirty;

    public MarkupControlStateStore(SettingsStore? store, Func<string> characterKey)
    {
        _store = store;
        _characterKey = characterKey ?? throw new ArgumentNullException(nameof(characterKey));
    }

    /// <summary>The remembered values of one window's controls.</summary>
    public IMarkupControlState ForWindow(string windowName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(windowName);
        return new WindowState(this, windowName);
    }

    /// <summary>Writes the current character's values if any changed since the last save.</summary>
    public void Save()
    {
        if (!_dirty)
            return;
        _dirty = false;
        if (_store is not null && !string.IsNullOrWhiteSpace(_character))
            _store.SaveMarkupControlState(_character, _values);
    }

    private void FollowCharacter()
    {
        string character = _characterKey() ?? string.Empty;
        if (_generation > 0 && string.Equals(character, _character, StringComparison.Ordinal))
            return;
        Save();
        _character = character;
        _values = _store is not null && !string.IsNullOrWhiteSpace(character)
            ? _store.LoadMarkupControlState(character)
            : new Dictionary<MarkupControlKey, string>();
        _generation++;
    }

    private int CurrentGeneration
    {
        get
        {
            FollowCharacter();
            return _generation;
        }
    }

    private string? Get(string window, string control, string kind)
    {
        FollowCharacter();
        return _values.TryGetValue(new MarkupControlKey(window, control, kind), out string? value) ? value : null;
    }

    private void Set(string window, string control, string kind, string value)
    {
        FollowCharacter();
        var key = new MarkupControlKey(window, control, kind);
        if (_values.TryGetValue(key, out string? current) && string.Equals(current, value, StringComparison.Ordinal))
            return;
        _values[key] = value;
        _dirty = true;
    }

    private sealed class WindowState(MarkupControlStateStore owner, string window) : IMarkupControlState
    {
        public int Generation => owner.CurrentGeneration;
        public string? Get(string control, string kind) => owner.Get(window, control, kind);
        public void Set(string control, string kind, string value) => owner.Set(window, control, kind, value);
    }
}
```

- [ ] **Step 5: Run the tests**

Run: `dotnet test tests/AcDream.App.Tests/AcDream.App.Tests.csproj --filter "FullyQualifiedName~MarkupControlStateStoreTests"`
Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git checkout -- '*.lock.json' 2>/dev/null
git add src/AcDream.App/UI/Markup/IMarkupControlState.cs src/AcDream.App/UI/Markup/MarkupControlStateStore.cs tests/AcDream.App.Tests/UI/MarkupControlStateStoreTests.cs
git commit -m "feat(ui): a store for what markup controls remember

Keeps the logged-in character's values in memory, writes them on Save,
and switches characters on relog. Controls will use it from the next
series for state the plugin doesn't bind.

Claude-Session: https://claude.ai/code/session_01UzLnYkJ6ifzKGaeNwgsp8W"
```

---

### Task 3: `<separator>`

**Goal:** A `<separator>` element: a click-through 1 px line in the window's border colour.

**Files:**
- Create: `src/AcDream.App/UI/UiMarkupSeparator.cs`
- Create: `src/AcDream.App/UI/Markup/MarkupSeparator.cs`
- Modify: `src/AcDream.App/UI/MarkupDocument.cs` (`case "separator"`)
- Modify: `src/AcDream.App/UI/PluginMarkupTheme.cs`
- Modify: `tests/AcDream.App.Tests/UI/MarkupDrawProbe.cs` (`QuadBounds`)
- Test: `tests/AcDream.App.Tests/UI/MarkupSeparatorTests.cs`

**Acceptance Criteria:**
- [ ] A 200×5 separator draws one `ClassicColor` quad 200 wide and 1 tall; a 5×100 one draws one quad 1 wide and 100 tall; each is centred across its box.
- [ ] `color` literal and bound colours draw instead of the default; under `theme="plugin"` (Moss) the default becomes `Moss.Border` but an explicit `color` is kept.
- [ ] A click on a separator lying over a button reaches the button.
- [ ] A 0-size separator draws nothing and doesn't throw.

**Verify:** `dotnet test tests/AcDream.App.Tests/AcDream.App.Tests.csproj --filter "FullyQualifiedName~MarkupSeparatorTests"` → all pass.

**Steps:**

- [ ] **Step 1: Add `QuadBounds` to `MarkupDrawProbe`** (inside the class, after `CountQuads`):

```csharp
    /// <summary>The screen rectangles of untextured quads drawn in a colour (within 0.01).</summary>
    public static List<(float X, float Y, float W, float H)> QuadBounds(TextRenderer renderer, Vector4 color)
    {
        var bounds = new List<(float, float, float, float)>();
        foreach (var s in renderer.DebugSpriteSegmentVerts)
        {
            if (s.Texture != 0u)
                continue;
            for (int q = 0; q + 48 <= s.Verts.Count; q += 48)
            {
                if (MathF.Abs(s.Verts[q + 4] - color.X) >= 0.01f
                    || MathF.Abs(s.Verts[q + 5] - color.Y) >= 0.01f
                    || MathF.Abs(s.Verts[q + 6] - color.Z) >= 0.01f
                    || MathF.Abs(s.Verts[q + 7] - color.W) >= 0.01f)
                {
                    continue;
                }
                float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
                for (int v = 0; v < 6; v++)
                {
                    float x = s.Verts[q + v * 8], y = s.Verts[q + v * 8 + 1];
                    minX = MathF.Min(minX, x); maxX = MathF.Max(maxX, x);
                    minY = MathF.Min(minY, y); maxY = MathF.Max(maxY, y);
                }
                bounds.Add((minX, minY, maxX - minX, maxY - minY));
            }
        }
        return bounds;
    }
```

(Vertex layout as `CountQuads` assumes: 8 floats per vertex, position first, colour at +4. Add `using System.Collections.Generic;` if the file lacks it.)

- [ ] **Step 2: Write the failing tests** — create `tests/AcDream.App.Tests/UI/MarkupSeparatorTests.cs`:

```csharp
using System.Linq;
using System.Numerics;
using AcDream.App.UI;

namespace AcDream.App.Tests.UI;

public class MarkupSeparatorTests
{
    private sealed class Binding
    {
        public uint LineColor { get; set; } = 0xFF00FF00u;
        public int Clicks { get; private set; }
        public Action Click => () => Clicks++;
    }

    private static UiMarkupSeparator Build(string attributes, Binding binding, string theme = "")
    {
        var themes = theme == "plugin" ? new PluginUiThemeSettings { Theme = PluginUiTheme.Moss } : null;
        var panel = MarkupDocument.Build(
            $"<panel {(theme == "plugin" ? "theme=\"plugin\" " : "")}x=\"0\" y=\"0\" w=\"300\" h=\"200\">"
            + $"<separator x=\"0\" y=\"0\" {attributes}/></panel>",
            binding, _ => (1u, 16, 16), themes: themes);
        return panel.Children.OfType<UiMarkupSeparator>().Single();
    }

    [Fact]
    public void Separator_HorizontalIsOneLineAcrossTheMiddle()
    {
        var separator = Build("w=\"200\" h=\"5\"", new Binding());
        var line = Assert.Single(MarkupDrawProbe.QuadBounds(MarkupDrawProbe.Draw(separator), UiMarkupSeparator.ClassicColor));
        Assert.Equal(200f, line.W, 3);
        Assert.Equal(1f, line.H, 3);
        Assert.Equal(2f, line.Y, 3);
    }

    [Fact]
    public void Separator_TallerThanWideIsVertical()
    {
        var separator = Build("w=\"5\" h=\"100\"", new Binding());
        var line = Assert.Single(MarkupDrawProbe.QuadBounds(MarkupDrawProbe.Draw(separator), UiMarkupSeparator.ClassicColor));
        Assert.Equal(1f, line.W, 3);
        Assert.Equal(100f, line.H, 3);
        Assert.Equal(2f, line.X, 3);
    }

    [Fact]
    public void Separator_LiteralAndBoundColours()
    {
        var literal = Build("w=\"100\" h=\"1\" color=\"#FFFF0000\"", new Binding());
        Assert.Equal(1, MarkupDrawProbe.CountQuads(MarkupDrawProbe.Draw(literal), new Vector4(1f, 0f, 0f, 1f)));

        var binding = new Binding();
        var bound = Build("w=\"100\" h=\"1\" color=\"{LineColor}\"", binding);
        Assert.Equal(1, MarkupDrawProbe.CountQuads(MarkupDrawProbe.Draw(bound), new Vector4(0f, 1f, 0f, 1f)));
        binding.LineColor = 0xFF0000FFu;
        Assert.Equal(1, MarkupDrawProbe.CountQuads(MarkupDrawProbe.Draw(bound), new Vector4(0f, 0f, 1f, 1f)));
    }

    [Fact]
    public void Separator_PluginThemeUsesTheBorderColourUnlessOneIsGiven()
    {
        var themed = Build("w=\"100\" h=\"1\"", new Binding(), theme: "plugin");
        Assert.Equal(PluginUiPalette.Moss.Border, themed.Color);
        var explicitColour = Build("w=\"100\" h=\"1\" color=\"#FFFF0000\"", new Binding(), theme: "plugin");
        Assert.Equal(new Vector4(1f, 0f, 0f, 1f), explicitColour.Color);
    }

    [Fact]
    public void Separator_ClicksPassThroughToWhatIsBeneath()
    {
        var binding = new Binding();
        var panel = MarkupDocument.Build(
            "<panel x=\"0\" y=\"0\" w=\"300\" h=\"200\">"
            + "<button x=\"0\" y=\"0\" w=\"100\" h=\"20\" text=\"Go\" onclick=\"{Click}\"/>"
            + "<separator x=\"0\" y=\"0\" w=\"100\" h=\"20\"/></panel>",
            binding, _ => (1u, 16, 16));
        var root = new UiRoot { Width = 800, Height = 600 };
        root.AddChild(panel);
        root.OnMouseDown(UiMouseButton.Left, 10, 10);
        root.OnMouseUp(UiMouseButton.Left, 10, 10);
        Assert.Equal(1, binding.Clicks);
    }

    [Fact]
    public void Separator_ZeroSizeDrawsNothing()
    {
        var separator = Build("w=\"0\" h=\"0\"", new Binding());
        Assert.Empty(MarkupDrawProbe.QuadBounds(MarkupDrawProbe.Draw(separator), UiMarkupSeparator.ClassicColor));
    }
}
```

If the panel adds chrome so that `QuadBounds` positions are offset, assert positions relative to the separator's own `Left`/`Top` (here 0); if `DrawSelfAndChildren` translates by `Left/Top`, the expected values above still hold because the separator sits at (0, 0).

- [ ] **Step 3: Run and see them fail**

Run: `dotnet test tests/AcDream.App.Tests/AcDream.App.Tests.csproj --filter "FullyQualifiedName~MarkupSeparatorTests"`
Expected: build FAILS: `UiMarkupSeparator` not found.

- [ ] **Step 4: Create `src/AcDream.App/UI/UiMarkupSeparator.cs`**

```csharp
using System.Numerics;

namespace AcDream.App.UI;

/// <summary>
/// A markup separator: a 1 px line across the middle of its box, horizontal
/// unless the box is taller than wide. It takes no input; clicks pass through
/// to whatever lies beneath.
/// </summary>
public sealed class UiMarkupSeparator : UiElement
{
    public static readonly Vector4 ClassicColor = UiCheckLamp.UncheckedInner;

    public Vector4 Color { get; set; } = ClassicColor;

    /// <summary>Re-read every frame in place of <see cref="Color"/>.</summary>
    public Func<Vector4>? ColorSource { get; set; }

    public bool Vertical => Height > Width;

    public UiMarkupSeparator()
    {
        ClickThrough = true;
    }

    protected override void OnDraw(UiRenderContext ctx)
    {
        if (Width <= 0f || Height <= 0f)
            return;
        Vector4 color = ColorSource?.Invoke() ?? Color;
        if (Vertical)
            ctx.DrawFill(MathF.Floor((Width - 1f) * 0.5f), 0f, 1f, Height, color);
        else
            ctx.DrawFill(0f, MathF.Floor((Height - 1f) * 0.5f), Width, 1f, color);
    }
}
```

- [ ] **Step 5: Create `src/AcDream.App/UI/Markup/MarkupSeparator.cs`**

```csharp
using System.Xml.Linq;

namespace AcDream.App.UI.Markup;

internal static class MarkupSeparator
{
    public static UiMarkupSeparator Build(XElement el, object binding, MarkupBuildContext context)
    {
        var separator = new UiMarkupSeparator
        {
            Left = MarkupDocument.F(el, "x"),
            Top = MarkupDocument.F(el, "y"),
            Width = MarkupDocument.F(el, "w"),
            Height = MarkupDocument.F(el, "h"),
        };
        string? color = (string?)el.Attribute("color");
        if (color is not null)
            separator.Color = MarkupDocument.Color(color);
        context.BindColor(color, value => separator.Color = value, source => separator.ColorSource = source);
        MarkupDocument.ApplyCommon(separator, el, binding);
        return separator;
    }
}
```

- [ ] **Step 6: Wire and theme it**

In `MarkupDocument.AddElement`, after the `case "radio": … break;` block, separated by a blank line:

```csharp
            case "separator":
                parent.AddChild(MarkupSeparator.Build(el, binding, Context()));
                break;
```

In `PluginMarkupTheme.Register`'s second switch, after the `case UiMarkupRadio radio:` section:

```csharp
            case UiMarkupSeparator separator:
                var separatorColor = separator.Color;
                panel.AddThemeAction(p => {
                    if (Default("color")) separator.Color = p?.Border ?? separatorColor; });
                break;
```

- [ ] **Step 7: Run the tests, then all markup tests**

Run: `dotnet test tests/AcDream.App.Tests/AcDream.App.Tests.csproj --filter "FullyQualifiedName~MarkupSeparatorTests"` → PASS.
Run: `dotnet test tests/AcDream.App.Tests/AcDream.App.Tests.csproj --filter "FullyQualifiedName~AcDream.App.Tests.UI.Markup|FullyQualifiedName~AcDream.App.Tests.UI.UiFieldTests"` → PASS.

- [ ] **Step 8: Commit**

```bash
git checkout -- '*.lock.json' 2>/dev/null
git add src/AcDream.App/UI tests/AcDream.App.Tests/UI
git commit -m "feat(ui): <separator> markup element

A 1 px line across the middle of its box, vertical when taller than
wide, in the window's border colour. Clicks pass through it.

Claude-Session: https://claude.ai/code/session_01UzLnYkJ6ifzKGaeNwgsp8W"
```

---

### Task 4: Docs

**Goal:** `docs/plugin-ui-markup.md` documents `<separator>`.

**Files:**
- Modify: `docs/plugin-ui-markup.md`

**Acceptance Criteria:**
- [ ] Elements table has a `separator` row after `radio`.
- [ ] A short `## Separator` section after `## Radio group` covers orientation, colour (Classic, theme, `color`), and that it takes no input.
- [ ] The older-host line is there.

**Verify:** `grep -c 'separator' docs/plugin-ui-markup.md` → at least 4.

**Steps:**

- [ ] **Step 1: Table** — after the `radio` row:

```markdown
| `separator` | A 1 px dividing line | `x y w h color` |
```

- [ ] **Step 2: Section** — after the `## Radio group` section:

````markdown
## Separator

```xml
<separator x="8" y="40" w="200" h="1"/>
<separator x="110" y="48" w="1" h="120" color="theme:border"/>
```

A `<separator>` draws a 1 px line across the middle of its box: horizontal,
or vertical when the box is taller than it is wide. It uses the window's
border colour (in the plugin theme, the theme's border token); `color`
gives it any other colour, literal or bound. It takes no input, and a click
on it reaches whatever lies beneath.

On a host older than separators, a `<separator>` fails the build of its
window as unknown.
````

(Check `theme:border` is a valid token in this file's "Shared plugin appearance" section; if the token syntax there differs, use an `#AARRGGBB` literal in the example instead.)

- [ ] **Step 3: Commit**

```bash
git add docs/plugin-ui-markup.md
git commit -m "docs(ui): document <separator>

Claude-Session: https://claude.ai/code/session_01UzLnYkJ6ifzKGaeNwgsp8W"
```

---

### Task 5: Series gate

**Goal:** Release build clean; no new failures in App, Plugin or UI.Abstractions tests; every commit builds.

**Files:** none changed.

**Acceptance Criteria:**
- [ ] `dotnet build AcDream.slnx -c Release` → 0 errors, 0 warnings.
- [ ] App + Plugin failing names (timings stripped) ⊆ `mc-base.txt`; UI.Abstractions failing names ⊆ `mc-uiabs-base.txt`.
- [ ] Each of the series' commits builds and passes `UI.Markup|UiFieldTests` and `MarkupControlStateSettingsTests` on its own.
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
done | norm > $T/mc4-after.txt
dotnet test tests/AcDream.UI.Abstractions.Tests/AcDream.UI.Abstractions.Tests.csproj -c Release 2>&1 | norm > $T/mc4-uiabs-after.txt
echo "new App/Plugin:"; comm -13 $T/mc-base.txt $T/mc4-after.txt
echo "new UI.Abstractions:"; comm -13 $T/mc-uiabs-base.txt $T/mc4-uiabs-after.txt
git checkout -- '*.lock.json' 2>/dev/null; git status --short
```
Expected: `0 Warning(s)`, `0 Error(s)`, nothing after either "new" line.

- [ ] **Step 2: Each commit builds alone**

```bash
cd /Users/davidsmith/code/OpenAC/OpenAC
for c in $(git rev-list --reverse 0b42572a..feat/markup-controls); do
  git worktree add -q --detach .worktrees/mc-commit-check $c
  (cd .worktrees/mc-commit-check \
   && dotnet test tests/AcDream.App.Tests/AcDream.App.Tests.csproj -c Release \
        --filter "FullyQualifiedName~AcDream.App.Tests.UI.Markup|FullyQualifiedName~AcDream.App.Tests.UI.UiFieldTests" 2>&1 \
        | grep -E ': error|: warning|Passed!|Failed!' | sort -u | sed "s/^/$(git rev-parse --short $c) app /" \
   && dotnet test tests/AcDream.UI.Abstractions.Tests/AcDream.UI.Abstractions.Tests.csproj -c Release \
        --filter "FullyQualifiedName~MarkupControlStateSettingsTests" 2>&1 \
        | grep -E ': error|: warning|Passed!|Failed!|No test matches' | sort -u | sed "s/^/$(git rev-parse --short $c) settings /")
  git worktree remove --force .worktrees/mc-commit-check
done
```
Expected: `Passed!` lines (the settings filter may report no matching tests on commits before Task 1's), no `error`/`warning` lines.

- [ ] **Step 3: Branch contents**

```bash
cd /Users/davidsmith/code/OpenAC/OpenAC/.worktrees/markup-controls
git log --oneline 0b42572a..HEAD
git diff --stat 391e709a..HEAD | grep superpowers || echo "no superpowers docs"
```
Expected: the 4 commits of Tasks 1-4; "no superpowers docs".

No commit.
