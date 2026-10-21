# Markup controls, plan 11b: hotkey string ↔ `PluginKeyChord` bridge Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers-extended-cc:subagent-driven-development (recommended) or superpowers-extended-cc:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** A plugin can turn the string a markup `<hotkey>` box reports into a `PluginKeyChord` for `IHotkeyRegistry`/`Rebind`, and turn a registration's chord back into the string a box shows, with one call each.

**Architecture:**
- **Contract.** `PluginKeyChord` (in `AcDream.Plugin.Abstractions/HotkeyAutomation.cs`) gains a body with two public members:
  - `static bool TryParse(string? text, out PluginKeyChord chord)` reads a `<hotkey>` string. Modifiers can come in any order and any case. The key can be spelled the box's way (`Keypad5`, `GraveAccent`) or by its `PluginKey` name (`Numpad5`, `Grave`). It returns false for empty or unreadable text, and for keys `PluginKey` has no member for.
  - `string ToHotkeyText()` writes the box's canonical form, `Ctrl+Alt+Shift+Key` with the box's key names.
  - The abstractions assembly stays free of Silk.NET. The 17 names that differ (7 table rows) are a small string table.
- **Consistency check.** An App test cross-checks every `PluginKey` against the client's own `AppHotkeyRegistry.MapKey` and `MarkupHotkeyText`, so the table can't drift from the client's key mapping. `MapKey` goes from `private` to `internal` for this.
- **Docs.** `plugin-ui-markup.md` (Hotkey section) and `plugin-api.md` (Hotkeys section) show the round trip.

**Status:** DONE 2026-10-22 — feat 8e627c4c..618e80d4 (5 commits: 3 tasks, 1 docs fix round, 1 final-review fix; gate = baseline 162, 0 warnings). Final review: `TryParse` renamed `TryParseHotkeyText` (the plain name implies a ToString round trip); `IsBound`/`EffectiveChord`/`Rebind` XML docs corrected (plugin-collision re-resolve; Rebind changes EffectiveChord; `Rebind(default)` unbinds and persists, now tested); docs example unbinds when the box is cleared. The code below still says `TryParse`; the branch uses `TryParseHotkeyText`. Deferred: client-binding sentence omits that re-register/Rebind also re-resolves.

**Tech Stack:** C# / .NET 10, xUnit. Plugin.Abstractions is `TreatWarningsAsErrors` with `GenerateDocumentationFile`, so every new public member needs an XML doc comment.

**Spec:** `docs/superpowers/specs/2026-10-21-markup-controls-design.md`, section 12 (`<hotkey>`), plus the amendment "Bridge to `PluginKeyChord`" added with this plan. Builds on plan 11 (`MarkupHotkeyText`, feat 8e627c4c).

## Global Constraints

- **Workspace.** Worktree `/Users/davidsmith/code/OpenAC/OpenAC/.worktrees/markup-controls`, branch `feat/markup-controls` (HEAD 8e627c4c).
  - Never merge fork `main` in, and never add `docs/superpowers/` or `.superpowers/` files.
  - Push only when the controller says so.
- **Build hygiene.** Every shell runs `export DOTNET_ROOT=$HOME/.dotnet PATH=$HOME/.dotnet:$PATH` first. After building, run `git checkout -- '*.lock.json'`; never commit lock files.
- **Commits.** Every commit must build and pass its own tests. Commit messages end with `Claude-Session: https://claude.ai/code/session_014SAojwkxRKxrwp1RMDGPVC`.
- **Contract change is additive only.**
  - No change to `PluginKeyChord`'s positional parameters, `PluginKey`'s members, or the record's generated `ToString`/equality.
  - Upstream's version number is Erik's call. The fork bump happens at the fork merge, in the same bump as the rest of the branch.
- **String rules (must match `MarkupHotkeyText` exactly).**
  - Canonical form: `Ctrl+`, `Alt+`, `Shift+` in that order, then the key name.
  - Parse rules:
    - Split on `+`.
    - Each part before the last must be `Ctrl`, `Alt` or `Shift`, case-insensitive. An unknown or repeated modifier fails.
    - The last part must be non-empty, start with an ASCII letter, and be all ASCII letters and digits.
    - Case-insensitive.
- **Key names.**
  - Box names that differ from `PluginKey` names:

    | Box name | `PluginKey` |
    |---|---|
    | `Keypad0`…`Keypad9` | `Numpad0`…`Numpad9` |
    | `KeypadDecimal` | `NumpadDecimal` |
    | `KeypadDivide` | `NumpadDivide` |
    | `KeypadMultiply` | `NumpadMultiply` |
    | `KeypadSubtract` | `NumpadSubtract` |
    | `KeypadAdd` | `NumpadAdd` |
    | `KeypadEnter` | `NumpadEnter` |
    | `GraveAccent` | `Grave` |

  - Every other `PluginKey` member has the same name in both.
  - `TryParse` accepts both spellings. `ToHotkeyText` writes the box spelling.
  - `PluginKey.Unknown` and undefined values are never a chord: `TryParse` returns false, and `ToHotkeyText` returns `""`.
- **On failure** `TryParse` sets `chord = default`.

**User decisions (already made):**
- "Then create and implement the bridge plan" (user, 2026-10-22). This chose the public `PluginKeyChord.TryParse` bridge (option (a) of the plan 11 final review) over restricting the box to `PluginKey` keys or documenting a manual mapping.
- One branch, one commit series per control, upstream-portable.

**Plan choices (flag in the final review if wrong):**
- The bridge lives on `PluginKeyChord` itself (static `TryParse` plus an instance `ToHotkeyText`), not on a separate static class. `ToString()` is not overridden, because that would change the record's existing output.
- The `<hotkey>` box itself is unchanged. It can still record keys `PluginKey` lacks (`F13`, `CapsLock`). A plugin sees `TryParse` return false and decides what to do. No `keys="plugin"` restriction attribute (YAGNI until a plugin asks).

---

## File Structure

| File | Responsibility |
|---|---|
| `src/AcDream.Plugin.Abstractions/HotkeyAutomation.cs` | `PluginKeyChord.TryParse`, `ToHotkeyText`, alias table |
| `tests/AcDream.Plugin.Tests/PluginKeyChordTextTests.cs` (new) | Contract tests |
| `src/AcDream.App/Input/AppHotkeyRegistry.cs` | `MapKey` `private` → `internal` |
| `tests/AcDream.App.Tests/Input/PluginKeyChordBridgeTests.cs` (new) | Cross-check against `MapKey` and `MarkupHotkeyText` |
| `docs/plugin-ui-markup.md`, `docs/plugin-api.md` | The round trip |

---

### Task 1: `PluginKeyChord.TryParse` and `ToHotkeyText`

**Goal:** `PluginKeyChord` reads and writes the `<hotkey>` string per the Global Constraints, with contract tests.

**Files:**
- Modify: `src/AcDream.Plugin.Abstractions/HotkeyAutomation.cs` (the `PluginKeyChord` record, ~line 228)
- Test: `tests/AcDream.Plugin.Tests/PluginKeyChordTextTests.cs`

**Acceptance Criteria:**
- [ ] `TryParse` reads these:
  - `"Ctrl+Alt+Shift+F"` → `(F, true, true, true)`
  - `"shift+ctrl+keypad5"` → `(Numpad5, Ctrl, Shift)`
  - `"Numpad5"` → `Numpad5`
  - `"GraveAccent"` and `"Grave"` → `Grave`
  - `"KeypadEnter"` → `NumpadEnter`
  - `"F12"` → `F12`
- [ ] `TryParse` returns false and `default` for: null, `""`, `"F13"`, `"CapsLock"`, `"Ctrl+"`, `"+F"`, `"Ctrl+Ctrl+F"`, `"Meta+F"`, `"5"`, `"A,B"`, `"Unknown"`, `"Ctrl+Nope"`.
- [ ] `ToHotkeyText` writes these:
  - `(F, Ctrl, Alt, Shift)` → `"Ctrl+Alt+Shift+F"`
  - `Numpad5` → `"Keypad5"`
  - `Grave` → `"GraveAccent"`
  - `NumpadEnter` → `"KeypadEnter"`
  - `default` and `(PluginKey)999` → `""`
- [ ] For every defined `PluginKey` except `Unknown`, and every combination of the three modifiers, `TryParse(chord.ToHotkeyText())` returns true and gives back the same chord.
- [ ] `dotnet build src/AcDream.Plugin.Abstractions -c Release` → 0 warnings (XML docs present).

**Verify:** `dotnet test tests/AcDream.Plugin.Tests/AcDream.Plugin.Tests.csproj --filter "FullyQualifiedName~PluginKeyChordTextTests"` → all pass.

**Steps:**

- [ ] **Step 1: Write the failing tests.** Create `tests/AcDream.Plugin.Tests/PluginKeyChordTextTests.cs`:

```csharp
// Copyright (c) OpenAC contributors.
// Distributed under the terms of the MIT license.

using AcDream.Plugin.Abstractions;

namespace AcDream.Plugin.Tests;

/// <summary>
/// A markup hotkey box reports its chord as text; a plugin turns that text
/// into the chord its hotkey registry takes, and back again.
/// </summary>
public sealed class PluginKeyChordTextTests
{
    public static TheoryData<string, PluginKeyChord> Readable => new()
    {
        { "Ctrl+Alt+Shift+F", new PluginKeyChord(PluginKey.F, Ctrl: true, Alt: true, Shift: true) },
        { "shift+ctrl+keypad5", new PluginKeyChord(PluginKey.Numpad5, Ctrl: true, Shift: true) },
        { "Numpad5", new PluginKeyChord(PluginKey.Numpad5) },
        { "GraveAccent", new PluginKeyChord(PluginKey.Grave) },
        { "Grave", new PluginKeyChord(PluginKey.Grave) },
        { "KeypadEnter", new PluginKeyChord(PluginKey.NumpadEnter) },
        { "F12", new PluginKeyChord(PluginKey.F12) },
    };

    [Theory]
    [MemberData(nameof(Readable))]
    public void TryParse_ReadsAHotkeyBoxString(string text, PluginKeyChord expected)
    {
        Assert.True(PluginKeyChord.TryParse(text, out PluginKeyChord chord));
        Assert.Equal(expected, chord);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("F13")]
    [InlineData("CapsLock")]
    [InlineData("Ctrl+")]
    [InlineData("+F")]
    [InlineData("Ctrl+Ctrl+F")]
    [InlineData("Meta+F")]
    [InlineData("5")]
    [InlineData("A,B")]
    [InlineData("Unknown")]
    [InlineData("Ctrl+Nope")]
    public void TryParse_RefusesWhatNoPluginHotkeyCanHold(string? text)
    {
        Assert.False(PluginKeyChord.TryParse(text, out PluginKeyChord chord));
        Assert.Equal(default, chord);
    }

    [Theory]
    [InlineData(PluginKey.F, true, true, true, "Ctrl+Alt+Shift+F")]
    [InlineData(PluginKey.Numpad5, false, false, false, "Keypad5")]
    [InlineData(PluginKey.Grave, false, false, false, "GraveAccent")]
    [InlineData(PluginKey.NumpadEnter, false, true, false, "Alt+KeypadEnter")]
    public void ToHotkeyText_WritesTheBoxsSpelling(PluginKey key, bool ctrl, bool alt, bool shift, string expected) =>
        Assert.Equal(expected, new PluginKeyChord(key, ctrl, alt, shift).ToHotkeyText());

    [Fact]
    public void ToHotkeyText_NoKeyIsEmpty()
    {
        Assert.Equal("", default(PluginKeyChord).ToHotkeyText());
        Assert.Equal("", new PluginKeyChord((PluginKey)999, Ctrl: true).ToHotkeyText());
    }

    [Fact]
    public void EveryPluginKey_RoundTripsWithEveryModifierSet()
    {
        foreach (PluginKey key in Enum.GetValues<PluginKey>())
        {
            if (key == PluginKey.Unknown)
                continue;
            for (int mods = 0; mods < 8; mods++)
            {
                var chord = new PluginKeyChord(key, (mods & 1) != 0, (mods & 2) != 0, (mods & 4) != 0);
                string text = chord.ToHotkeyText();
                Assert.True(PluginKeyChord.TryParse(text, out PluginKeyChord back), text);
                Assert.Equal(chord, back);
            }
        }
    }
}
```

- [ ] **Step 2: Run to verify they fail.** Run `--filter "FullyQualifiedName~PluginKeyChordTextTests"`. Expected: a build error, because `TryParse`/`ToHotkeyText` don't exist yet.

- [ ] **Step 3: Implement.** In `HotkeyAutomation.cs`, keep the existing `<summary>`/`<param>` docs on `PluginKeyChord`. Add one sentence to the summary: "A markup `<hotkey>` box reports a chord as text; <see cref="TryParse"/> and <see cref="ToHotkeyText"/> convert between the two." Then replace the record's terminating `;` with a body:

```csharp
public readonly record struct PluginKeyChord(
    PluginKey Key,
    bool Ctrl = false,
    bool Alt = false,
    bool Shift = false)
{
    /// <summary>
    /// The markup hotkey box's names for the keys whose <see cref="PluginKey"/>
    /// name differs; every other key is spelled the same way in both.
    /// </summary>
    private static readonly (PluginKey Key, string Name)[] HotkeyBoxNames =
    [
        (PluginKey.Numpad0, "Keypad0"), (PluginKey.Numpad1, "Keypad1"),
        (PluginKey.Numpad2, "Keypad2"), (PluginKey.Numpad3, "Keypad3"),
        (PluginKey.Numpad4, "Keypad4"), (PluginKey.Numpad5, "Keypad5"),
        (PluginKey.Numpad6, "Keypad6"), (PluginKey.Numpad7, "Keypad7"),
        (PluginKey.Numpad8, "Keypad8"), (PluginKey.Numpad9, "Keypad9"),
        (PluginKey.NumpadDecimal, "KeypadDecimal"),
        (PluginKey.NumpadDivide, "KeypadDivide"),
        (PluginKey.NumpadMultiply, "KeypadMultiply"),
        (PluginKey.NumpadSubtract, "KeypadSubtract"),
        (PluginKey.NumpadAdd, "KeypadAdd"),
        (PluginKey.NumpadEnter, "KeypadEnter"),
        (PluginKey.Grave, "GraveAccent"),
    ];

    /// <summary>
    /// Reads the text a markup <c>&lt;hotkey&gt;</c> box reports, such as
    /// <c>Ctrl+Shift+F</c>: modifiers in any order and any case, then the
    /// key, spelled the box's way (<c>Keypad5</c>, <c>GraveAccent</c>) or
    /// as its <see cref="PluginKey"/> name (<c>Numpad5</c>, <c>Grave</c>).
    /// </summary>
    /// <param name="text">The box's text.</param>
    /// <param name="chord">The chord read, or the default chord when this returns false.</param>
    /// <returns>
    /// False for empty text (an unbound box), text it can't read, and keys a
    /// plugin hotkey can't use (a box can record keys such as F13 or Caps
    /// Lock that <see cref="PluginKey"/> has no member for).
    /// </returns>
    public static bool TryParse(string? text, out PluginKeyChord chord)
    {
        chord = default;
        if (string.IsNullOrEmpty(text))
            return false;
        string[] parts = text.Split('+');
        bool ctrl = false, alt = false, shift = false;
        for (int i = 0; i < parts.Length - 1; i++)
        {
            string part = parts[i];
            if (!ctrl && IsNamed(part, "Ctrl")) ctrl = true;
            else if (!alt && IsNamed(part, "Alt")) alt = true;
            else if (!shift && IsNamed(part, "Shift")) shift = true;
            else return false;
        }
        if (!TryReadKey(parts[^1], out PluginKey key))
            return false;
        chord = new PluginKeyChord(key, ctrl, alt, shift);
        return true;
    }

    /// <summary>
    /// This chord as the text a markup <c>&lt;hotkey&gt;</c> box shows, so a
    /// box bound to it displays a registration's
    /// <see cref="IPluginHotkeyRegistration.EffectiveChord"/>:
    /// <c>Ctrl+</c>, <c>Alt+</c>, <c>Shift+</c> in that order, then the key
    /// by the box's name. Empty when there is no key.
    /// </summary>
    /// <returns>The box's text for this chord, or an empty string.</returns>
    public string ToHotkeyText()
    {
        if (Key == PluginKey.Unknown || !Enum.IsDefined(Key))
            return string.Empty;
        var text = new System.Text.StringBuilder();
        if (Ctrl) text.Append("Ctrl+");
        if (Alt) text.Append("Alt+");
        if (Shift) text.Append("Shift+");
        return text.Append(HotkeyBoxName(Key)).ToString();
    }

    private static bool IsNamed(string part, string name) =>
        string.Equals(part, name, StringComparison.OrdinalIgnoreCase);

    private static string HotkeyBoxName(PluginKey key)
    {
        foreach ((PluginKey aliased, string name) in HotkeyBoxNames)
        {
            if (aliased == key)
                return name;
        }
        return key.ToString();
    }

    private static bool TryReadKey(string name, out PluginKey key)
    {
        foreach ((PluginKey aliased, string boxName) in HotkeyBoxNames)
        {
            if (IsNamed(name, boxName))
            {
                key = aliased;
                return true;
            }
        }
        key = default;
        return name.Length > 0 && char.IsAsciiLetter(name[0]) && name.All(char.IsAsciiLetterOrDigit)
            && Enum.TryParse(name, ignoreCase: true, out key)
            && key != PluginKey.Unknown && Enum.IsDefined(key);
    }
}
```

  If the first-letter/alnum guard fails, `Enum.TryParse` is never called and `key` stays `default`. When `Enum.TryParse` itself fails, it sets `key` to `default`. Either way the caller only reads `key` on true.

- [ ] **Step 4: Run to verify they pass.** Run the same filter: all pass. Then `dotnet build src/AcDream.Plugin.Abstractions/AcDream.Plugin.Abstractions.csproj -c Release 2>&1 | grep -E 'Warning\(s\)|Error\(s\)'` → 0/0. Then `git checkout -- '*.lock.json'`.

- [ ] **Step 5: Commit.**

```bash
git add src/AcDream.Plugin.Abstractions/HotkeyAutomation.cs tests/AcDream.Plugin.Tests/PluginKeyChordTextTests.cs
git commit -m "feat(plugins): read and write hotkey box text as a PluginKeyChord

Claude-Session: https://claude.ai/code/session_014SAojwkxRKxrwp1RMDGPVC"
```

---

### Task 2: Cross-check against the client's key mapping

**Goal:** One App test proves that the bridge, the `<hotkey>` box codec and `AppHotkeyRegistry.MapKey` all name the same physical key for every `PluginKey`, and that the bridge refuses exactly the box keys `MapKey` can't bind.

**Files:**
- Modify: `src/AcDream.App/Input/AppHotkeyRegistry.cs` (`private static Key? MapKey` → `internal static`)
- Test: `tests/AcDream.App.Tests/Input/PluginKeyChordBridgeTests.cs`

**Acceptance Criteria:**
- [ ] For every `PluginKey` except `Unknown`, `MarkupHotkeyText.Parse(new PluginKeyChord(key, Ctrl: true).ToHotkeyText())` returns a chord with `Key == AppHotkeyRegistry.MapKey(key)` and `Modifiers == ModifierMask.Ctrl`.
- [ ] For every Silk `Key` where `MarkupHotkeyText.IsChordKey(key)` holds, `PluginKeyChord.TryParse(MarkupHotkeyText.Format(new KeyChord(key, ModifierMask.Shift)), out var c)`:
  - returns true with `MapKey(c.Key) == key` and `c.Shift` when some `PluginKey` maps to `key`;
  - returns false otherwise.
- [ ] `MapKey`'s only change is its accessibility.

**Verify:** `dotnet test tests/AcDream.App.Tests/AcDream.App.Tests.csproj --filter "FullyQualifiedName~PluginKeyChordBridgeTests|FullyQualifiedName~AppHotkeyRegistry"` → all pass.

**Steps:**

- [ ] **Step 1: Write the test.** Create `tests/AcDream.App.Tests/Input/PluginKeyChordBridgeTests.cs`:

```csharp
using AcDream.App.Input;
using AcDream.App.UI.Markup;
using AcDream.Plugin.Abstractions;
using AcDream.UI.Abstractions.Input;
using Silk.NET.Input;

namespace AcDream.App.Tests.Input;

/// <summary>
/// A markup hotkey box, the plugin chord bridge and the hotkey registry must
/// agree on which physical key every name means.
/// </summary>
public sealed class PluginKeyChordBridgeTests
{
    [Fact]
    public void EveryPluginKey_NamesTheKeyTheRegistryBinds()
    {
        foreach (PluginKey key in Enum.GetValues<PluginKey>())
        {
            if (key == PluginKey.Unknown)
                continue;
            string text = new PluginKeyChord(key, Ctrl: true).ToHotkeyText();
            KeyChord? box = MarkupHotkeyText.Parse(text);
            Assert.True(box is not null, $"{key} wrote '{text}', which a hotkey box can't read");
            Assert.Equal(AppHotkeyRegistry.MapKey(key), box.Value.Key);
            Assert.Equal(ModifierMask.Ctrl, box.Value.Modifiers);
        }
    }

    [Fact]
    public void EveryRecordableKey_ParsesExactlyWhenTheRegistryCanBindIt()
    {
        var bindable = new Dictionary<Key, PluginKey>();
        foreach (PluginKey key in Enum.GetValues<PluginKey>())
        {
            if (AppHotkeyRegistry.MapKey(key) is { } silk)
                bindable[silk] = key;
        }

        foreach (Key key in Enum.GetValues<Key>().Distinct())
        {
            if (!MarkupHotkeyText.IsChordKey(key))
                continue;
            string text = MarkupHotkeyText.Format(new KeyChord(key, ModifierMask.Shift));
            bool read = PluginKeyChord.TryParse(text, out PluginKeyChord chord);
            if (bindable.TryGetValue(key, out PluginKey expected))
            {
                Assert.True(read, $"'{text}' should read as {expected}");
                Assert.Equal(new PluginKeyChord(expected, Shift: true), chord);
            }
            else
            {
                Assert.False(read, $"'{text}' read as {chord.Key}, which the registry can't bind");
            }
        }
    }
}
```

  Check the namespace of `AppHotkeyRegistry` (`grep -n "^namespace" src/AcDream.App/Input/AppHotkeyRegistry.cs`) and of the other tests in `tests/AcDream.App.Tests/Input/`, and match them. Silk's `Key` enum can have two names for one value. `Distinct()` over the values plus `Format`'s `ToString()` is fine: both sides go through the same name.

- [ ] **Step 2: Run to verify it fails.** Run the Verify filter. Expected: a build error, because `MapKey` is inaccessible.

- [ ] **Step 3: Implement.** In `AppHotkeyRegistry.cs` change `private static Key? MapKey(PluginKey key)` to `internal static Key? MapKey(PluginKey key)`. Nothing else.

- [ ] **Step 4: Run to verify it passes.** Run the Verify filter: all pass. If a key fails, the bridge's alias table (Task 1) is wrong for that key. Fix the table and its Task 1 test together, not this test, and say so in the report. Then `git checkout -- '*.lock.json'`.

- [ ] **Step 5: Commit.**

```bash
git add src/AcDream.App/Input/AppHotkeyRegistry.cs tests/AcDream.App.Tests/Input/PluginKeyChordBridgeTests.cs
git commit -m "test(plugins): hotkey box, chord bridge and registry agree on every key

Claude-Session: https://claude.ai/code/session_014SAojwkxRKxrwp1RMDGPVC"
```

---

### Task 3: Docs

**Goal:** Both docs show the box ↔ registration round trip.

**Files:**
- Modify: `docs/plugin-ui-markup.md` (`## Hotkey`)
- Modify: `docs/plugin-api.md` (`## Hotkeys`)

**Acceptance Criteria:**
- [ ] In `plugin-ui-markup.md`, the bullet saying the string is not a `PluginKeyChord` and the plugin must map names itself is replaced. The new text says:
  - `PluginKeyChord.TryParse` reads the box's string, and `chord.ToHotkeyText()` gives the string for a chord;
  - `TryParse` returns false for an unbound box and for keys a plugin hotkey can't use (F13, Caps Lock);
  - it links to the Hotkeys section of `plugin-api.md`.
- [ ] In `plugin-api.md`, after the `Rebind` paragraph, a new paragraph and example show a `<hotkey>` box bound to a registration:
  - `value` = `handle.EffectiveChord.ToHotkeyText()`;
  - `onchange` → `TryParse` → `Rebind`;
  - false keeps the current chord.
- [ ] Every name and behaviour in the new text matches `HotkeyAutomation.cs`. The example compiles in principle: real member names, and `Rebind` takes a `PluginKeyChord`.

**Verify:** `grep -n "ToHotkeyText\|PluginKeyChord.TryParse" docs/plugin-ui-markup.md docs/plugin-api.md` → hits in both files.

**Steps:**

- [ ] **Step 1: `plugin-ui-markup.md`.** Replace the `PluginKeyChord` bullet in `## Hotkey` with:

````markdown
- The string uses the box's own key names (`Keypad5`, `GraveAccent`), which
  are not always `PluginKey` names (`Numpad5`, `Grave`). To register or
  rebind the recorded chord as a plugin hotkey, read it with
  `PluginKeyChord.TryParse(text, out PluginKeyChord chord)`; to show a
  hotkey's chord in a box, bind `value` to `chord.ToHotkeyText()`. `TryParse`
  returns false for an empty (unbound) string and for keys a plugin hotkey
  can't use, such as F13 or Caps Lock, which the box can still record. See
  [Hotkeys](plugin-api.md#hotkeys).
````

  Keep the bullet list's existing wrap width.

- [ ] **Step 2: `plugin-api.md`.** After the paragraph that begins "`IPluginHotkeyRegistration.Rebind(chord)` stores a new chord", insert:

````markdown
A markup `<hotkey>` box lets the player pick the chord. Bind its `value` to
the registration's chord as text and rebind from its `onchange`:

```csharp
public string QuickHealKey => _quickHeal.EffectiveChord.ToHotkeyText();

public Action<string> SetQuickHealKey => text =>
{
    if (PluginKeyChord.TryParse(text, out PluginKeyChord chord))
        _quickHeal.Rebind(chord);
};
```

```xml
<hotkey x="8" y="8" w="140" h="20" value="{QuickHealKey}" onchange="{SetQuickHealKey}"/>
```

`PluginKeyChord.TryParse` reads the box's text (modifiers in any order, the
key by the box's name such as `Keypad5` or by its `PluginKey` name such as
`Numpad5`) and `ToHotkeyText()` writes it back. `TryParse` returns false for
an empty string (the player cleared the box) and for keys `PluginKey` has no
member for, such as F13; the example then keeps the current chord. A chord
that `Rebind` can't bind (it collides with a client binding or another
hotkey) leaves `IsBound` false, so check it after rebinding if the plugin
should tell the player.
````

  Check the last sentence against `AppHotkeyRegistry.Rebind`/`Resolve`. If a colliding `Rebind` behaves differently (for example it keeps the old chord bound), describe what it actually does.

- [ ] **Step 3: Commit.**

```bash
git add docs/plugin-ui-markup.md docs/plugin-api.md
git commit -m "docs(plugins): hotkey box text to and from PluginKeyChord

Claude-Session: https://claude.ai/code/session_014SAojwkxRKxrwp1RMDGPVC"
```

---

### Task 4: Series gate

**Goal:** The Release build is clean, App, Plugin and UI.Abstractions tests have no new failures, and every commit builds.

**Files:** none changed.

**Acceptance Criteria:**
- [ ] `dotnet build AcDream.slnx -c Release` → 0 errors, 0 warnings.
- [ ] App + Plugin failures ⊆ `mc-base.txt` (162 names); UI.Abstractions failures ⊆ `mc-uiabs-base.txt` (empty).
- [ ] Each of the series' commits builds and passes the hotkey/bridge test filters on its own.
- [ ] Nothing under `docs/superpowers/` on the branch.

**Verify:** both `comm -13` comparisons print nothing.

**Steps:**

- [ ] **Step 1: Release build and tests.**

```bash
export DOTNET_ROOT=$HOME/.dotnet PATH=$HOME/.dotnet:$PATH
cd /Users/davidsmith/code/OpenAC/OpenAC/.worktrees/markup-controls
T=/private/tmp/claude-501/-Users-davidsmith-code-OpenAC-OpenAC
norm() { grep -E '^[[:space:]]+Failed [A-Za-z]' | sed -E 's/^[[:space:]]+Failed //; s/ \[[^]]*\]$//' | sort -u; }
dotnet build AcDream.slnx -c Release 2>&1 | grep -E ': error|: warning|Warning\(s\)|Error\(s\)' | sort -u
for p in tests/AcDream.App.Tests/AcDream.App.Tests.csproj tests/AcDream.Plugin.Tests/AcDream.Plugin.Tests.csproj; do
  dotnet test "$p" -c Release 2>&1
done | norm > $T/mc11b-after.txt
dotnet test tests/AcDream.UI.Abstractions.Tests/AcDream.UI.Abstractions.Tests.csproj -c Release 2>&1 | norm > $T/mc11b-uiabs-after.txt
echo "new App/Plugin:"; comm -13 $T/mc-base.txt $T/mc11b-after.txt
echo "new UI.Abstractions:"; comm -13 $T/mc-uiabs-base.txt $T/mc11b-uiabs-after.txt
git checkout -- '*.lock.json' 2>/dev/null; git status --short
```
Expected: `0 Warning(s)`, `0 Error(s)`, nothing after either "new" line.

- [ ] **Step 2: Each commit builds alone.**

```bash
cd /Users/davidsmith/code/OpenAC/OpenAC
for c in $(git rev-list --reverse 8e627c4c..feat/markup-controls); do
  git worktree add -q --detach .worktrees/mc-commit-check $c
  (cd .worktrees/mc-commit-check \
    && dotnet test tests/AcDream.Plugin.Tests/AcDream.Plugin.Tests.csproj -c Release --filter "FullyQualifiedName~PluginKeyChord" 2>&1 \
       | grep -E ': error|: warning|Passed!|Failed!' | sort -u | sed "s/^/$(git rev-parse --short $c) plugin /" \
    ; dotnet test tests/AcDream.App.Tests/AcDream.App.Tests.csproj -c Release --filter "FullyQualifiedName~Hotkey|FullyQualifiedName~PluginKeyChord" 2>&1 \
       | grep -E ': error|: warning|Passed!|Failed!' | sort -u | sed "s/^/$(git rev-parse --short $c) app /")
  git worktree remove --force .worktrees/mc-commit-check
done
```
Expected: `Passed!` for each commit and project (a commit before Task 1 has no `PluginKeyChord` tests, so the Plugin filter may report no tests; that is fine). No `error`/`warning` lines.

- [ ] **Step 3: Branch contents.**

```bash
cd /Users/davidsmith/code/OpenAC/OpenAC/.worktrees/markup-controls
git log --oneline 8e627c4c..HEAD
git diff --stat 391e709a..HEAD | grep superpowers || echo "no superpowers docs"
```
Expected: the 3 commits of Tasks 1-3 (plus any review-fix commits) and "no superpowers docs".

No commit.
