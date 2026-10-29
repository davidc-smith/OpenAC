# HUD Text Size Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers-extended-cc:subagent-driven-development (recommended) or superpowers-extended-cc:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Let the player make Modern HUD text bigger or smaller (80%–150%) without changing the size of any deck: one HUD-wide size on the Config page, and a per-deck override from a right-click on the deck's frame or title.

**Architecture:** `HudTextSettings` (a global percent and per-deck overrides) lives in a `HudText` owner that saves it per client in the settings file's top-level `hudText` section, beside the `misc` section that holds the Modern HUD switch, and raises `Changed`. Each deck reads its text factor (`HudText.FactorFor(HudDeck.X)`) and keeps its scale for geometry. `DeckFonts` bakes faces at round(base × scale × factor). Text in a fixed box goes through `DeckFonts.FitFace`, which drops to the largest face whose line fits the box. Rows that hold a run of text grow by `DeckFonts.RowGrow`. A shared `HudTextMenu` block ("Text size", "Same as HUD (N%)", 80%…150%) goes into each deck's frame menu. `UiMarkupContextMenu` and `HudTooltip` take the factor for their faces, and their padding follows the scale.

**Tech Stack:** C# / .NET 10, xUnit, `System.Text.Json.Nodes`, the retained UI (`UiElement`, `UiRoot`, `UiMarkupContextMenu`), `DeckFonts`/`DeckFace`, `SettingsStore`, `ConfigOptionsPageController`.

**Spec:** `docs/superpowers/specs/2026-10-29-hud-text-size-design.md` (approved, binding).

**Worktree:** implement in `~/code/OpenAC/OpenAC-hud-text-size` on branch `feat/hud-text-size` (spec commit `4deb6390`, made from main `67d1e47b`). The Verify commands use that path.

## Global Constraints

- **Range:** 80% to 150% in steps of 10; default 100%. Every percent is clamped and snapped to 10s on load and on set (`HudTextSettings.Snap`).
- **Stored per client**, not per character: one top-level `hudText` object in the settings file, beside `misc` (the Modern HUD switch). A missing or bad section reads as defaults.
- **Face pixel height = round(base × scale × factor)**, rounded away from zero as `DeckFonts.PixelHeight` already does. Geometry (boxes, pads, gaps, icons) uses the deck scale only.
- **The deck window never resizes because of text.** Width, height and min/max sizes are the same at 80%, 100% and 150%.
- **Rows that hold a run of text grow:** height = normal height at scale + `DeckFonts.RowGrow` = max(0, line(scale × factor) − line(scale)). This is the spec's max(normal height, line height + padding), where the padding is the normal height minus the 100% line. A deck at 100% therefore lays out exactly as today.
- **Fixed boxes keep their size:** their text is fitted with "…" and its face is capped by `DeckFonts.FitFace` to the largest face whose line fits the box. The cap never goes below the deck's own (factor 1) face, so 100% draws exactly as today. Where a box was already shorter than the 100% line, it stays as it is now.
- **Chat Deck transcript and input keep their own pixel sizes** from the appearance popup. The factor applies to the rest of the Chat Deck.
- `UiMarkupContextMenu` has **no submenus**. The "Text size" entries are a flat block in the deck's frame menu, as the spec allows: a disabled "Text size" heading, then "Same as HUD (N%)", then 80%…150%. The current choice is marked with "* ", the way the Gear Deck's sort menu marks its sort.
- Right-clicks on items, chips, tabs, slots, studs and other controls keep their current menus, or no menu where they have none today.
- Out of scope: retail windows, plugin windows (their menus keep `RowGrow = 0`), per-character sizes, and the Chat Deck's appearance popup.
- Tests: `cd ~/code/OpenAC/OpenAC-hud-text-size && export PATH=$HOME/.dotnet:$PATH && dotnet test tests/<Project> -c Release --filter "<filter>"`. The full suite runs with `~/code/OpenAC/test-baseline/run-mac-tests.sh ~/code/OpenAC/OpenAC-hud-text-size`. Never run tests that are known to fail.
- Commit messages end with a blank line and `Claude-Session: https://claude.ai/code/session_017hCEwifLBBM9NGyiUdMFVs`.
- Fork-only: no upstream PR, no plugin contract change. Every new type is `internal` to `AcDream.App`, apart from two new public `SettingsStore` methods.

**User decisions (already made):**
- Global size plus a per-deck override.
- Rows grow and the window doesn't; fixed boxes cut their text with … and cap it.
- Right-click a deck's frame or title → Text size submenu.
- Per client, not per character.
- The Chat Deck keeps its own transcript and input sizes.
- Config page stepper under the Modern HUD row.

**Planning decisions (flag at review if wrong):**
- **The Config "stepper" is the page's snapped numeric slider** (`BuildExplicitNumericSliderRow`: min 80, max 150, step 10, integer, range captions "80%" and "150%"). The Config page has no stepper control, and the snapped slider is how its other numeric rows (Voices, Voices Per Sound) are built. The row is added only when the Modern HUD row is, directly under it. Its label is the spec's "HUD text size".
- **The menu is a flat block**, not a submenu (see above).
- **Decks read their factor every frame.** Only the Gear Deck, which caches fitted text and geometry, subscribes to `HudText.Changed` (and unsubscribes in `Detach`). The Command, Spell, Target and Chat decks already re-lay out or re-measure every tick, so subscribing would do nothing more. Read the spec's "every mounted deck listens" as every deck following a change on its next frame.
- **Chat Deck menus** (the channel chip menu and the tab/frame menu) use the chat font size × the factor, because today they use the chat font size. The transcript's line menu stays at the transcript size, since it belongs to the transcript. The tab rename field (an edit box inside a tab) keeps its size. The Chat Deck shows no `HudTooltip`, so "tooltips follow the factor" has nothing to apply to there.
- **Strip preview lines** are the Chat Deck's peek lines. Each one grows, the peek area keeps its height, and so fewer lines show.

---

## File Structure

| File | Responsibility |
|---|---|
| `src/AcDream.App/UI/Hud/HudTextSettings.cs` (create) | `HudDeck` enum; `HudTextSettings`: global and overrides, snap/clamp, factor, JSON |
| `src/AcDream.App/UI/Hud/HudText.cs` (create) | The live settings owner: set, save, `Changed`, `Load(SettingsStore?)` |
| `src/AcDream.UI.Abstractions/Panels/Settings/SettingsStore.cs` (modify) | `LoadHudText` / `SaveHudText`: the top-level `hudText` section |
| `src/AcDream.App/UI/Hud/DeckFonts.cs` (modify) | `At(scale, textFactor)`, faces shared by pixel height, `FitFace`, `Fitted`, `DeckFit`, `LineHeightAt`, `RowGrow`; `DeckFace.For(…, textFactor, …)`, `DeckFace.In` |
| `src/AcDream.App/UI/UiMarkupContextMenu.cs` (modify) | `RowGrow`: points added to every item row |
| `src/AcDream.App/UI/Hud/HudTooltip.cs` (modify) | `textFactor` for the faces; pads follow `scale` |
| `src/AcDream.App/UI/Hud/HudTextMenu.cs` (create) | The shared "Text size" block: labels, enabled flags, choose, `StyleMenu` |
| `src/AcDream.App/UI/Layout/ConfigOptionsPageController.cs` (modify) | `HudTextBindings`, `Bindings.HudText`, the slider row under Modern HUD |
| `src/AcDream.App/UI/RetailUiRuntime.cs` (modify) | `HudTextSize` owner, the Config binding, and passing it to each deck |
| `src/AcDream.App/UI/Hud/GearDeckGeometry.cs` (modify) | `TextGrow`, `ListRowHeight`, `SuggestRowHeight` |
| `src/AcDream.App/UI/Hud/UiGearDeck*.cs` (modify) | Factor, faces in boxes, menu block, fields, tooltip |
| `src/AcDream.App/UI/Hud/UiCommandDeck*.cs` (modify) | Factor, faces in boxes (fit then cap), text menu, tooltip |
| `src/AcDream.App/UI/Hud/UiSpellDeck*.cs` (modify) | Factor, key labels and bookmarks in boxes, menu block, tooltip |
| `src/AcDream.App/UI/Hud/UiTargetDeck*.cs` (modify) | Factor, plate text in boxes, ⋯ menu lines grow, text menu, tooltip |
| `src/AcDream.App/UI/Hud/UiChatDeck*.cs` (modify) | Factor, labels in boxes, peek lines grow, menus, frame menu block |
| `tests/AcDream.App.Tests/UI/Hud/FakeDeckFonts.cs` (create) | Test faces whose line is px + 2, fit recording, `AssertAllFit` |
| `tests/AcDream.App.Tests/UI/Hud/HudTestInput.cs` (create) | Right-click through the real root input path |

---

### Task 1: The text size model, its owner and its storage

**Goal:** `HudTextSettings` holds a global percent and per-deck overrides (clamped to 80–150, snapped to 10s). `HudText` owns the live value, saves it per client in the settings file's `hudText` section, and raises `Changed`.

**Files:**
- Create: `src/AcDream.App/UI/Hud/HudTextSettings.cs`, `src/AcDream.App/UI/Hud/HudText.cs`
- Modify: `src/AcDream.UI.Abstractions/Panels/Settings/SettingsStore.cs` (after `SaveGearDeck`, ~line 467)
- Test: `tests/AcDream.App.Tests/UI/Hud/HudTextSettingsTests.cs`, `tests/AcDream.App.Tests/UI/Hud/HudTextTests.cs`, `tests/AcDream.UI.Abstractions.Tests/Panels/Settings/SettingsStoreHudTextTests.cs`

**Acceptance Criteria:**
- [ ] `HudTextSettings.Default` is 100% with no overrides, and `FactorFor` is 1.0 for every `HudDeck`.
- [ ] `WithOverride(deck, p)` changes only that deck's factor. `WithOverride(deck, null)` ("Same as HUD") removes the override.
- [ ] `Snap` clamps to 80–150 and rounds to the nearest 10, half away from zero (104 → 100, 105 → 110, 79 → 80, 1000 → 150, −5 → 80). `WithGlobal` and `WithOverride` snap.
- [ ] `ToJson` / `FromJson` round-trip. A missing, empty, non-object or non-numeric value reads as defaults. Unknown or numeric deck keys are ignored, and off-step values snap.
- [ ] `HudText.Set` saves and raises `Changed` once per real change. A set to an equal value does neither.
- [ ] `SettingsStore.SaveHudText` writes the top-level `hudText` object and keeps the other sections (the Modern HUD switch included). `LoadHudText` reads it back, and is null when there is none. Text that is not a JSON object is refused with `ArgumentException`.
- [ ] `HudText.Load(store)` reads what an earlier run saved, and `HudText.Load(null)` is the defaults.

**Verify:** `cd ~/code/OpenAC/OpenAC-hud-text-size && export PATH=$HOME/.dotnet:$PATH && dotnet test tests/AcDream.App.Tests -c Release --filter "FullyQualifiedName~HudTextSettingsTests|FullyQualifiedName~HudTextTests" && dotnet test tests/AcDream.UI.Abstractions.Tests -c Release --filter "FullyQualifiedName~SettingsStoreHudTextTests"` → all pass

**Steps:**

- [ ] **Step 1: Write the failing tests.**

`tests/AcDream.App.Tests/UI/Hud/HudTextSettingsTests.cs`:

```csharp
using AcDream.App.UI.Hud;

namespace AcDream.App.Tests.UI.Hud;

public sealed class HudTextSettingsTests
{
    [Fact]
    public void Default_Is100PercentForEveryDeck()
    {
        HudTextSettings s = HudTextSettings.Default;
        Assert.Equal(100, s.Global);
        Assert.Empty(s.Overrides);
        foreach (HudDeck deck in Enum.GetValues<HudDeck>())
            Assert.Equal(1f, s.FactorFor(deck));
    }

    [Fact]
    public void AnOverride_WinsForItsDeckOnly_AndSameAsHudClearsIt()
    {
        HudTextSettings s = HudTextSettings.Default.WithGlobal(120).WithOverride(HudDeck.Gear, 140);
        Assert.Equal(1.4f, s.FactorFor(HudDeck.Gear), 3);
        Assert.Equal(1.2f, s.FactorFor(HudDeck.Command), 3);
        Assert.True(s.HasOverride(HudDeck.Gear));

        HudTextSettings cleared = s.WithOverride(HudDeck.Gear, null);
        Assert.False(cleared.HasOverride(HudDeck.Gear));
        Assert.Equal(120, cleared.PercentFor(HudDeck.Gear));
    }

    [Theory]
    [InlineData(100, 100)]
    [InlineData(104, 100)]
    [InlineData(105, 110)]
    [InlineData(149, 150)]
    [InlineData(10, 80)]
    [InlineData(79, 80)]
    [InlineData(1000, 150)]
    [InlineData(-5, 80)]
    public void PercentsClampAndSnapToTens(int given, int expected)
    {
        Assert.Equal(expected, HudTextSettings.Snap(given));
        Assert.Equal(expected, HudTextSettings.Default.WithGlobal(given).Global);
        Assert.Equal(expected, HudTextSettings.Default.WithOverride(HudDeck.Chat, given).PercentFor(HudDeck.Chat));
    }

    [Fact]
    public void Json_RoundTrips()
    {
        HudTextSettings s = HudTextSettings.Default.WithGlobal(90)
            .WithOverride(HudDeck.Gear, 150).WithOverride(HudDeck.Target, 80);
        HudTextSettings back = HudTextSettings.FromJson(s.ToJson());
        Assert.Equal(s, back);
        Assert.Equal(150, back.PercentFor(HudDeck.Gear));
        Assert.Equal(90, back.PercentFor(HudDeck.Spell));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("[1,2]")]
    [InlineData("{\"global\":\"big\"}")]
    public void MissingOrBadJson_ReadsAsDefaults(string? json) =>
        Assert.Equal(HudTextSettings.Default, HudTextSettings.FromJson(json));

    [Fact]
    public void Json_OffStepValuesSnap_AndUnknownOrNumericDeckKeysAreIgnored()
    {
        HudTextSettings s = HudTextSettings.FromJson(
            """{"global":133,"overrides":{"gear":71,"radar":120,"1":140,"chat":"x"}}""");
        Assert.Equal(130, s.Global);
        Assert.Equal(80, s.PercentFor(HudDeck.Gear));
        Assert.Equal(new[] { HudDeck.Gear }, s.Overrides.Keys.ToArray());
    }
}
```

`tests/AcDream.App.Tests/UI/Hud/HudTextTests.cs`:

```csharp
using AcDream.App.UI.Hud;
using AcDream.UI.Abstractions.Panels.Settings;

namespace AcDream.App.Tests.UI.Hud;

public sealed class HudTextTests
{
    [Fact]
    public void ASet_SavesAndRaisesChangedOnce_AnEqualSetDoesNothing()
    {
        var saved = new List<HudTextSettings>();
        var text = new HudText(null, saved.Add);
        int changed = 0;
        text.Changed += () => changed++;

        text.SetGlobal(130);
        text.SetGlobal(128);                    // snaps to 130: no change
        text.SetOverride(HudDeck.Gear, 150);
        text.SetOverride(HudDeck.Gear, 150);

        Assert.Equal(2, changed);
        Assert.Equal(2, saved.Count);
        Assert.Equal(1.3f, text.FactorFor(HudDeck.Command), 3);
        Assert.Equal(1.5f, text.FactorFor(HudDeck.Gear), 3);
    }

    [Fact]
    public void Load_WithoutAStore_IsTheDefaults() =>
        Assert.Equal(HudTextSettings.Default, HudText.Load(null).Settings);

    [Fact]
    public void Load_ReadsWhatAnEarlierRunSaved()
    {
        string path = Path.Combine(Path.GetTempPath(), $"hudtext-{Guid.NewGuid():N}.json");
        try
        {
            HudText first = HudText.Load(new SettingsStore(path));
            first.SetGlobal(120);
            first.SetOverride(HudDeck.Chat, 90);

            HudText second = HudText.Load(new SettingsStore(path));
            Assert.Equal(120, second.Settings.Global);
            Assert.Equal(90, second.Settings.PercentFor(HudDeck.Chat));
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }
}
```

`tests/AcDream.UI.Abstractions.Tests/Panels/Settings/SettingsStoreHudTextTests.cs`:

```csharp
using AcDream.UI.Abstractions.Panels.Settings;

namespace AcDream.UI.Abstractions.Tests.Panels.Settings;

public sealed class SettingsStoreHudTextTests
{
    private static string TempPath() => Path.Combine(Path.GetTempPath(), $"s-{Guid.NewGuid():N}.json");

    [Fact]
    public void HudText_RoundTrips_BesideTheModernHudSwitch()
    {
        string path = TempPath();
        try
        {
            var store = new SettingsStore(path);
            Assert.Null(store.LoadHudText());
            store.SaveHudText("""{"version":1,"global":120,"overrides":{"gear":150}}""");
            store.SaveMisc(MiscSettings.Default with { ModernHud = true });

            var again = new SettingsStore(path);
            string? json = again.LoadHudText();
            Assert.NotNull(json);
            Assert.Contains("\"global\":120", json);
            Assert.Contains("\"gear\":150", json);
            Assert.True(again.LoadMisc().ModernHud);
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public void SaveHudText_RefusesTextThatIsNotAJsonObject()
    {
        string path = TempPath();
        try
        {
            var store = new SettingsStore(path);
            Assert.Throws<ArgumentException>(() => store.SaveHudText("[1]"));
            Assert.Throws<ArgumentException>(() => store.SaveHudText("nope"));
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }
}
```

- [ ] **Step 2: Run** the Verify command → compile errors (`HudTextSettings`, `HudText`, `LoadHudText` do not exist).

- [ ] **Step 3: Implement `HudTextSettings`.**

`src/AcDream.App/UI/Hud/HudTextSettings.cs`:

```csharp
using System.Text.Json.Nodes;

namespace AcDream.App.UI.Hud;

/// <summary>The Modern HUD decks, as the text size names them.</summary>
internal enum HudDeck { Command, Spell, Chat, Target, Gear }

/// <summary>
/// The Modern HUD's text size: one HUD-wide percent and any deck's own override.
/// Every percent is clamped to 80–150 and snapped to 10s, on load and on set.
/// "Same as HUD" is the absence of an override.
/// </summary>
internal sealed class HudTextSettings : IEquatable<HudTextSettings>
{
    public const int MinPercent = 80, MaxPercent = 150, StepPercent = 10, DefaultPercent = 100;

    /// <summary>Every size the menus and the Config row offer, smallest first.</summary>
    public static IReadOnlyList<int> Steps { get; } = [80, 90, 100, 110, 120, 130, 140, 150];

    public static HudTextSettings Default { get; } = new(DefaultPercent);

    private readonly Dictionary<HudDeck, int> _overrides = [];

    public HudTextSettings(int global, IReadOnlyDictionary<HudDeck, int>? overrides = null)
    {
        Global = Snap(global);
        if (overrides is null) return;
        foreach ((HudDeck deck, int percent) in overrides)
            if (Enum.IsDefined(deck)) _overrides[deck] = Snap(percent);
    }

    public int Global { get; }

    public IReadOnlyDictionary<HudDeck, int> Overrides => _overrides;

    /// <summary>Clamped to 80–150 and rounded to the nearest 10 (half away from zero).</summary>
    public static int Snap(int percent)
    {
        int stepped = (int)Math.Round(percent / (double)StepPercent, MidpointRounding.AwayFromZero) * StepPercent;
        return Math.Clamp(stepped, MinPercent, MaxPercent);
    }

    public bool HasOverride(HudDeck deck) => _overrides.ContainsKey(deck);

    /// <summary>The deck's override, or the HUD-wide size.</summary>
    public int PercentFor(HudDeck deck) => _overrides.TryGetValue(deck, out int p) ? p : Global;

    /// <summary>The deck's text factor: 1.0 at 100%.</summary>
    public float FactorFor(HudDeck deck) => PercentFor(deck) / 100f;

    public HudTextSettings WithGlobal(int percent) => new(percent, _overrides);

    /// <summary>Sets the deck's override; null ("Same as HUD") removes it.</summary>
    public HudTextSettings WithOverride(HudDeck deck, int? percent)
    {
        var next = new Dictionary<HudDeck, int>(_overrides);
        if (percent is int p) next[deck] = p;
        else next.Remove(deck);
        return new HudTextSettings(Global, next);
    }

    /// <summary>Reads <c>{"version":1,"global":N,"overrides":{"gear":N,…}}</c>; anything it cannot read is the defaults.</summary>
    public static HudTextSettings FromJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return Default;
        try
        {
            if (JsonNode.Parse(json) is not JsonObject o) return Default;
            int global = ReadInt(o["global"]) ?? DefaultPercent;
            var overrides = new Dictionary<HudDeck, int>();
            if (o["overrides"] is JsonObject map)
                foreach ((string key, JsonNode? value) in map)
                    if (TryDeck(key, out HudDeck deck) && ReadInt(value) is int p)
                        overrides[deck] = p;
            return new HudTextSettings(global, overrides);
        }
        catch (Exception) { return Default; }
    }

    public string ToJson()
    {
        var overrides = new JsonObject();
        foreach (HudDeck deck in Enum.GetValues<HudDeck>())
            if (_overrides.TryGetValue(deck, out int p)) overrides[Key(deck)] = p;
        return new JsonObject { ["version"] = 1, ["global"] = Global, ["overrides"] = overrides }.ToJsonString();
    }

    private static string Key(HudDeck deck) => deck.ToString().ToLowerInvariant();

    private static bool TryDeck(string key, out HudDeck deck)
    {
        foreach (HudDeck d in Enum.GetValues<HudDeck>())
        {
            if (!string.Equals(Key(d), key, StringComparison.Ordinal)) continue;
            deck = d;
            return true;
        }
        deck = default;
        return false;
    }

    private static int? ReadInt(JsonNode? node)
    {
        if (node is not JsonValue v) return null;
        if (v.TryGetValue(out int i)) return i;
        if (v.TryGetValue(out double d) && double.IsFinite(d))
            return (int)Math.Clamp(Math.Round(d), int.MinValue, int.MaxValue);
        return null;
    }

    public bool Equals(HudTextSettings? other) =>
        other is not null && Global == other.Global && _overrides.Count == other._overrides.Count
        && _overrides.All(kv => other._overrides.TryGetValue(kv.Key, out int p) && p == kv.Value);

    public override bool Equals(object? obj) => Equals(obj as HudTextSettings);

    public override int GetHashCode() => HashCode.Combine(Global, _overrides.Count);
}
```

- [ ] **Step 4: Add the storage section** to `SettingsStore.cs`, right after `SaveGearDeck`:

```csharp
    /// <summary>
    /// The Modern HUD text size, as JSON text, read from the top-level <c>hudText</c>
    /// section: per client, beside <c>misc</c> where the Modern HUD switch is. Null when
    /// there is none or the file cannot be read; the HUD parses and checks it itself.
    /// </summary>
    public string? LoadHudText()
    {
        if (!File.Exists(_path)) return null;
        try
        {
            var root = JsonNode.Parse(ReadSettingsText()) as JsonObject;
            return root?["hudText"] is JsonObject text ? text.ToJsonString() : null;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"settings: failed to load HUD text size from {_path}: {ex.Message}");
            return null;
        }
    }

    /// <summary>Replaces the top-level <c>hudText</c> section with <paramref name="json"/>, keeping every other section.</summary>
    /// <exception cref="ArgumentException">The text is not a JSON object.</exception>
    public void SaveHudText(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        JsonObject text;
        try
        {
            text = JsonNode.Parse(json) as JsonObject
                ?? throw new ArgumentException("The HUD text size is not a JSON object.", nameof(json));
        }
        catch (JsonException ex)
        {
            throw new ArgumentException("The HUD text size is not valid JSON.", nameof(json), ex);
        }

        JsonObject root = LoadMutableRoot();
        root["hudText"] = text;
        root["version"] = CurrentSchemaVersion;
        WriteMutableRoot(root);
    }
```

- [ ] **Step 5: Implement `HudText`.**

`src/AcDream.App/UI/Hud/HudText.cs`:

```csharp
using AcDream.UI.Abstractions.Panels.Settings;

namespace AcDream.App.UI.Hud;

/// <summary>
/// The live Modern HUD text size: the settings each deck reads its text factor from,
/// saved on every change, and <see cref="Changed"/> for the decks that cache text.
/// </summary>
internal sealed class HudText
{
    private readonly Action<HudTextSettings>? _save;

    public HudText(HudTextSettings? settings = null, Action<HudTextSettings>? save = null)
    {
        Settings = settings ?? HudTextSettings.Default;
        _save = save;
    }

    public HudTextSettings Settings { get; private set; }

    /// <summary>Raised after a real change, once it is saved.</summary>
    public event Action? Changed;

    public float FactorFor(HudDeck deck) => Settings.FactorFor(deck);

    public void Set(HudTextSettings next)
    {
        ArgumentNullException.ThrowIfNull(next);
        if (next.Equals(Settings)) return;
        Settings = next;
        _save?.Invoke(next);
        Changed?.Invoke();
    }

    public void SetGlobal(int percent) => Set(Settings.WithGlobal(percent));

    /// <summary>Sets a deck's own size; null is "Same as HUD".</summary>
    public void SetOverride(HudDeck deck, int? percent) => Set(Settings.WithOverride(deck, percent));

    /// <summary>The size the client saved last (defaults without a store); each change is saved back to it.</summary>
    public static HudText Load(SettingsStore? store, Action<string>? report = null)
    {
        if (store is null) return new HudText();
        HudTextSettings settings = HudTextSettings.FromJson(store.LoadHudText());
        return new HudText(settings, s =>
        {
            try { store.SaveHudText(s.ToJson()); }
            catch (Exception ex) { report?.Invoke($"[UI] HUD text size not saved: {ex.Message}"); }
        });
    }
}
```

- [ ] **Step 6: Run** the Verify command → all pass.

- [ ] **Step 7: Commit.**

```bash
git add src/AcDream.App/UI/Hud/HudTextSettings.cs src/AcDream.App/UI/Hud/HudText.cs \
  src/AcDream.UI.Abstractions/Panels/Settings/SettingsStore.cs \
  tests/AcDream.App.Tests/UI/Hud/HudTextSettingsTests.cs tests/AcDream.App.Tests/UI/Hud/HudTextTests.cs \
  tests/AcDream.UI.Abstractions.Tests/Panels/Settings/SettingsStoreHudTextTests.cs
git commit -m "HUD text size: settings, owner and per-client storage

Claude-Session: https://claude.ai/code/session_017hCEwifLBBM9NGyiUdMFVs"
```

---

### Task 2: Deck fonts with a text factor, fitted faces and row growth

**Goal:** `DeckFonts` bakes faces at round(base × scale × factor). It shares one bake per pixel height and weight, fits a face to a box (never below the deck's own face), and reports how much a text row grows. Tests get fake faces whose line height is known.

**Files:**
- Modify: `src/AcDream.App/UI/Hud/DeckFonts.cs`
- Create: `tests/AcDream.App.Tests/UI/Hud/FakeDeckFonts.cs`
- Test: `tests/AcDream.App.Tests/UI/Hud/DeckFontsTests.cs` (add cases)

**Acceptance Criteria:**
- [ ] `DeckFonts.At(scale, textFactor)` bakes Text and Small at round(14 × scale × factor) and round(12 × scale × factor). For example, 0.75 × 1.5 gives 16 and 14.
- [ ] `At(scale)` sets share faces through `Face(px, weight)`, so two scales that round to the same pixel height bake once. The existing `DeckFontsTests` cases still pass.
- [ ] `FitFace(base, weight, scale, factor, boxHeight)` returns the factor face when its line fits the box. Otherwise it returns the largest smaller face whose line fits, never smaller than round(base × scale), and never larger than the factor face (a factor below 1 is not raised). Each call raises `Fitted` with a `DeckFit`.
- [ ] `DeckFonts.RowGrow(fonts, base, weight, scale, factor)` = max(0, line(scale × factor) − line(scale)), using the pixel height as the line when there are no fonts.
- [ ] `DeckFace.For(fonts, scale, textFactor, small)` and `DeckFace.In(…)` return those faces, and the default font (null) without fonts.

**Verify:** `cd ~/code/OpenAC/OpenAC-hud-text-size && export PATH=$HOME/.dotnet:$PATH && dotnet test tests/AcDream.App.Tests -c Release --filter "FullyQualifiedName~DeckFontsTests"` → all pass

**Steps:**

- [ ] **Step 1: Create the test fonts.**

`tests/AcDream.App.Tests/UI/Hud/FakeDeckFonts.cs`:

```csharp
using AcDream.App.UI;
using AcDream.App.UI.Hud;
using DatReaderWriter.Types;

namespace AcDream.App.Tests.UI.Hud;

/// <summary>Deck fonts for tests: a face's line is its pixel height plus two, and every fitted face is recorded.</summary>
internal static class FakeDeckFonts
{
    public static float Line(float pixels) => pixels + 2f;

    public static DeckFonts Make(List<DeckFit>? fits = null, List<(float Px, BundledUiFontWeight Weight)>? baked = null)
    {
        var fonts = new DeckFonts((px, weight) =>
        {
            baked?.Add((px, weight));
            return Font(px);
        });
        if (fits is not null) fonts.Fitted = fits.Add;
        return fonts;
    }

    public static UiDatFont Font(float pixels)
    {
        var glyphs = new Dictionary<char, FontCharDesc>();
        byte w = (byte)Math.Max(1, (int)MathF.Round(pixels * 0.6f)), h = (byte)Math.Max(1, (int)pixels);
        for (char c = ' '; c <= '~'; c++)
            glyphs[c] = new FontCharDesc { Unicode = c, Width = w, Height = h };
        return new UiDatFont(fgTex: 1u, fgW: 256, fgH: 256, bgTex: 0, bgW: 0, bgH: 0,
            lineHeight: Line(pixels), baselineOffset: pixels, glyphs);
    }

    /// <summary>Every fitted face's line is no taller than its box, or than the deck's own (factor 1) face where the box was already smaller.</summary>
    public static void AssertAllFit(IEnumerable<DeckFit> fits)
    {
        foreach (DeckFit f in fits)
        {
            float own = Line(DeckFonts.PixelHeight(f.BasePixels, f.Scale));
            Assert.True(f.LineHeight <= MathF.Max(f.BoxHeight, own) + 0.01f, $"{f} is taller than its box");
        }
    }
}
```

- [ ] **Step 2: Write the failing tests.** Append to `DeckFontsTests`:

```csharp
    [Theory]
    [InlineData(1f, 1.5f, 21f, 18f)]
    [InlineData(0.75f, 1.5f, 16f, 14f)]   // 15.75 and 13.5 round away from zero
    [InlineData(1.25f, 0.8f, 14f, 12f)]
    [InlineData(1.5f, 1f, 21f, 18f)]
    public void At_WithATextFactor_BakesRoundOfBaseTimesScaleTimesFactor(float scale, float factor, float text, float small)
    {
        var baked = new List<(float Px, BundledUiFontWeight Weight)>();
        var fonts = new DeckFonts((px, weight) => { baked.Add((px, weight)); return null!; });
        DeckFontSet set = fonts.At(scale, factor);
        _ = set.Text;
        _ = set.Small;
        Assert.Equal(new[] { (text, BundledUiFontWeight.Regular), (small, BundledUiFontWeight.Regular) }, baked);
    }

    [Fact]
    public void ScalesThatRoundToTheSamePixelHeight_ShareOneBake()
    {
        var baked = new List<(float Px, BundledUiFontWeight Weight)>();
        var fonts = new DeckFonts((px, weight) => { baked.Add((px, weight)); return null!; });
        _ = fonts.At(1.4f).Text;    // 19.6 rounds to 20
        _ = fonts.At(1.45f).Text;   // 20.3 rounds to 20
        _ = fonts.Face(20f, BundledUiFontWeight.Regular);
        Assert.Equal(new[] { (20f, BundledUiFontWeight.Regular) }, baked);
    }

    [Fact]
    public void FitFace_KeepsTheFactorFaceWhenItsLineFits()
    {
        DeckFonts fonts = FakeDeckFonts.Make();
        UiDatFont face = fonts.FitFace(DeckFonts.TextPixelHeight, BundledUiFontWeight.Regular, 1f, 1.5f, boxHeight: 30f);
        Assert.Equal(FakeDeckFonts.Line(21f), face.LineHeight);
    }

    [Fact]
    public void FitFace_DropsToTheLargestFaceWhoseLineFitsTheBox()
    {
        var fits = new List<DeckFit>();
        DeckFonts fonts = FakeDeckFonts.Make(fits);
        UiDatFont face = fonts.FitFace(DeckFonts.TextPixelHeight, BundledUiFontWeight.Regular, 1f, 1.5f, boxHeight: 20f);
        Assert.Equal(20f, face.LineHeight);   // 18 px + 2
        DeckFit fit = Assert.Single(fits);
        Assert.Equal(18f, fit.Pixels);
        Assert.Equal(20f, fit.BoxHeight);
        Assert.Equal(1.5f, fit.TextFactor);
    }

    [Fact]
    public void FitFace_NeverGoesBelowTheDecksOwnFace()
    {
        DeckFonts fonts = FakeDeckFonts.Make();
        UiDatFont face = fonts.FitFace(DeckFonts.SmallPixelHeight, BundledUiFontWeight.Regular, 1f, 1.5f, boxHeight: 8f);
        Assert.Equal(FakeDeckFonts.Line(12f), face.LineHeight);
    }

    [Fact]
    public void FitFace_ASmallerFactorIsNeverRaised()
    {
        DeckFonts fonts = FakeDeckFonts.Make();
        UiDatFont face = fonts.FitFace(DeckFonts.TextPixelHeight, BundledUiFontWeight.Regular, 1f, 0.8f, boxHeight: 100f);
        Assert.Equal(FakeDeckFonts.Line(11f), face.LineHeight);   // 11.2 rounds to 11
    }

    [Theory]
    [InlineData(true, 1.5f, 6f)]
    [InlineData(true, 1f, 0f)]
    [InlineData(true, 0.8f, 0f)]
    [InlineData(false, 1.5f, 6f)]
    public void RowGrow_IsTheLinesGrowthOverTheDecksOwnFace(bool withFonts, float factor, float grow)
    {
        DeckFonts? fonts = withFonts ? FakeDeckFonts.Make() : null;
        Assert.Equal(grow, DeckFonts.RowGrow(fonts, DeckFonts.SmallPixelHeight, BundledUiFontWeight.Regular, 1f, factor), 3);
    }

    [Fact]
    public void DeckFace_ForAndIn_UseTheFactor_AndTheDefaultFontWithoutFonts()
    {
        DeckFonts fonts = FakeDeckFonts.Make();
        Assert.Equal(20f, DeckFace.In(fonts, DeckFonts.TextPixelHeight, BundledUiFontWeight.Regular, 1f, 1.5f, 20f).Font!.LineHeight);
        Assert.Null(DeckFace.In(null, DeckFonts.TextPixelHeight, BundledUiFontWeight.Regular, 1f, 1.5f, 20f).Font);
        Assert.Equal(FakeDeckFonts.Line(18f), DeckFace.For(fonts, 1f, 1.5f, small: true).Font!.LineHeight);
        Assert.Null(DeckFace.For(null, 1f, 1.5f, small: true).Font);
    }
```

- [ ] **Step 3: Run** the Verify command → compile errors (`At(float, float)`, `FitFace`, `Fitted`, `DeckFit`, `RowGrow`, `DeckFace.In` do not exist).

- [ ] **Step 4: Implement in `DeckFonts.cs`.** Replace `At(float scale)` so each set's faces come from `Face`, and add the new members after `PixelHeight`:

```csharp
    /// <summary>The faces for a deck drawn at <paramref name="scale"/>; each bakes when first used,
    /// once per pixel height and weight however many scales round to it.</summary>
    public DeckFontSet At(float scale)
    {
        if (!_sets.TryGetValue(scale, out DeckFontSet? set))
        {
            float text = PixelHeight(TextPixelHeight, scale), small = PixelHeight(SmallPixelHeight, scale);
            _sets[scale] = set = new DeckFontSet(
                new(() => Face(text, BundledUiFontWeight.Regular)),
                new(() => Face(small, BundledUiFontWeight.Regular)),
                new(() => Face(text, BundledUiFontWeight.SemiBold)),
                new(() => Face(small, BundledUiFontWeight.SemiBold)));
        }
        return set;
    }

    /// <summary>The faces for a deck at <paramref name="scale"/> with text factor <paramref name="textFactor"/>:
    /// each face is round(base × scale × factor) pixels.</summary>
    public DeckFontSet At(float scale, float textFactor) => At(scale * textFactor);

    /// <summary>Raised by <see cref="FitFace"/> with what it chose (tests check that no face is taller than its box).</summary>
    internal Action<DeckFit>? Fitted { get; set; }

    /// <summary>
    /// The face for text in a fixed box: round(base × scale × factor) pixels, or, when
    /// that face's line is taller than <paramref name="boxHeight"/>, the largest smaller
    /// face whose line fits. Never below the face at the deck's own scale (factor 1), so
    /// a deck at 100% draws exactly as before; never above the factor's face.
    /// </summary>
    public UiDatFont FitFace(float basePixels, BundledUiFontWeight weight, float scale, float textFactor, float boxHeight)
    {
        float floor = PixelHeight(basePixels, scale);
        float px = PixelHeight(basePixels, scale * textFactor);
        UiDatFont? face = Face(px, weight);
        while (px > floor && face is not null && face.LineHeight > boxHeight + 0.01f)
        {
            px -= 1f;
            face = Face(px, weight);
        }
        Fitted?.Invoke(new DeckFit(basePixels, weight, scale, textFactor, boxHeight, px, face?.LineHeight ?? px));
        return face!;
    }

    /// <summary>The line height of the face at round(base × scale): the baked face's, or the pixel height without fonts.</summary>
    public static float LineHeightAt(DeckFonts? fonts, float basePixels, BundledUiFontWeight weight, float scale)
    {
        float px = PixelHeight(basePixels, scale);
        UiDatFont? face = fonts?.Face(px, weight);
        return face?.LineHeight ?? px;
    }

    /// <summary>
    /// How much a row holding one line of this text grows at <paramref name="textFactor"/>:
    /// the line's growth over the deck's own size, never negative. A growing row is its
    /// normal height plus this (the normal height less the 100% line is the row's padding).
    /// </summary>
    public static float RowGrow(DeckFonts? fonts, float basePixels, BundledUiFontWeight weight, float scale, float textFactor) =>
        MathF.Max(0f, LineHeightAt(fonts, basePixels, weight, scale * textFactor) - LineHeightAt(fonts, basePixels, weight, scale));
```

Add the record after `DeckFontSet`:

```csharp
/// <summary>One <see cref="DeckFonts.FitFace"/> choice: the asked size and box, and the face's pixels and line.</summary>
internal readonly record struct DeckFit(
    float BasePixels, BundledUiFontWeight Weight, float Scale, float TextFactor, float BoxHeight, float Pixels, float LineHeight);
```

Add to `DeckFace`, after `SmallBoldFor`:

```csharp
    /// <summary>The text or small face at <paramref name="scale"/> × <paramref name="textFactor"/>; the default font without fonts.</summary>
    public static DeckFace For(DeckFonts? fonts, float scale, float textFactor, bool small)
    {
        if (fonts is null) return new(null);
        DeckFontSet set = fonts.At(scale, textFactor);
        return new(small ? set.Small : set.Text);
    }

    /// <summary>The face for text in a box <paramref name="boxHeight"/> tall (<see cref="DeckFonts.FitFace"/>); the default font without fonts.</summary>
    public static DeckFace In(DeckFonts? fonts, float basePixels, BundledUiFontWeight weight, float scale, float textFactor, float boxHeight) =>
        new(fonts?.FitFace(basePixels, weight, scale, textFactor, boxHeight));
```

- [ ] **Step 5: Run** the Verify command → all pass, the old cases included.

- [ ] **Step 6: Commit.**

```bash
git add src/AcDream.App/UI/Hud/DeckFonts.cs tests/AcDream.App.Tests/UI/Hud/FakeDeckFonts.cs tests/AcDream.App.Tests/UI/Hud/DeckFontsTests.cs
git commit -m "Deck fonts: text factor, fitted faces and row growth

Claude-Session: https://claude.ai/code/session_017hCEwifLBBM9NGyiUdMFVs"
```

---

### Task 3: Menu row growth, tooltip text factor and the shared Text size block

**Goal:** A deck's popup menu can grow its rows to hold larger text. The tooltip draws its faces at scale × factor while its pads follow the scale. `HudTextMenu` builds, enables, applies and styles the "Text size" block that every deck lists.

**Files:**
- Modify: `src/AcDream.App/UI/UiMarkupContextMenu.cs`, `src/AcDream.App/UI/Hud/HudTooltip.cs`
- Create: `src/AcDream.App/UI/Hud/HudTextMenu.cs`, `tests/AcDream.App.Tests/UI/Hud/HudTestInput.cs`
- Test: `tests/AcDream.App.Tests/UI/UiMarkupContextMenuTests.cs` (add a case), `tests/AcDream.App.Tests/UI/Hud/HudTooltipTests.cs` (add a case), `tests/AcDream.App.Tests/UI/Hud/HudTextMenuTests.cs` (create)

**Acceptance Criteria:**
- [ ] `UiMarkupContextMenu.RowGrow` (default 0) is read as the menu opens and added to every item row's height, but not to separators. Hit testing follows. With `RowGrow` 0, every existing menu test passes unchanged.
- [ ] `HudTooltip.Draw(…, fonts, scale, textFactor)` bakes its body and title faces at round(14 × scale × factor). Pads, gaps and wrap width still follow `scale` alone.
- [ ] `HudTextMenu.Labels(settings, deck)` is `["Text size", "Same as HUD (N%)", "80%", …, "150%"]`, where N is the global size. "* " marks Same as HUD when the deck has no override, else the deck's size. `Enabled(0)` is false and every other entry is enabled. `Choose` sets or clears the deck's override, and choosing the heading does nothing.
- [ ] `HudTextMenu.StyleMenu(menu, fonts, scale, factor)` sets `Scale` = scale, `DatFont` = the small face at scale × factor, and `RowGrow` = `DeckFonts.RowGrow` for that face.
- [ ] `HudTestInput.RightClick` sends a right press and release through `UiRoot` at a point local to an element.

**Verify:** `cd ~/code/OpenAC/OpenAC-hud-text-size && export PATH=$HOME/.dotnet:$PATH && dotnet test tests/AcDream.App.Tests -c Release --filter "FullyQualifiedName~UiMarkupContextMenuTests|FullyQualifiedName~HudTooltipTests|FullyQualifiedName~HudTextMenuTests"` → all pass

**Steps:**

- [ ] **Step 1: Create the input helper** `tests/AcDream.App.Tests/UI/Hud/HudTestInput.cs`:

```csharp
using AcDream.App.UI;

namespace AcDream.App.Tests.UI.Hud;

/// <summary>Input the way the root delivers it, for the decks' right-click menu tests.</summary>
internal static class HudTestInput
{
    /// <summary>A right press and release at a point in <paramref name="element"/>'s local coordinates.</summary>
    public static void RightClick(UiRoot root, UiElement element, float localX, float localY)
    {
        var origin = element.ScreenPosition;
        int x = (int)(origin.X + localX), y = (int)(origin.Y + localY);
        root.OnMouseMove(x, y);
        root.OnMouseDown(UiMouseButton.Right, x, y);
        root.OnMouseUp(UiMouseButton.Right, x, y);
    }
}
```

- [ ] **Step 2: Write the failing tests.**

Append to `UiMarkupContextMenuTests`:

```csharp
    [Fact]
    public void RowGrow_AddsToEveryItemRow_NotToSeparators()
    {
        var m = Mount(CopyPaste);
        m.Menu.RowGrow = 6f;
        int picked = -1;
        m.Menu.Selected = i => picked = i;
        RightClick(m.Root, 100, 100);
        Assert.Equal((100f, 100f, 80f, 61f), m.Menu.PopupScreenRect);   // 3 + 24 + 7 + 24 + 3
        Click(m.Root, 110, 100 + 50);   // Paste is 34 .. 58 with the growth
        Assert.Equal(2, picked);
    }
```

Append to `HudTooltipTests` (add `using AcDream.App.UI;` at the top):

```csharp
    private sealed class TipHost(DeckFonts fonts, float factor) : UiElement
    {
        protected override void OnDraw(UiRenderContext ctx) =>
            HudTooltip.Draw(ctx, Long, HudTooltipStyle.Card, new DeckRect(200f, 200f, 20f, 10f),
                System.Numerics.Vector2.Zero, 800f, null, fonts, scale: 1f, textFactor: factor);
    }

    [Fact]
    public void TheTextFactorSizesTheFaces()
    {
        var baked = new List<(float Px, BundledUiFontWeight Weight)>();
        DeckFonts fonts = FakeDeckFonts.Make(baked: baked);
        MarkupDrawProbe.Draw(new TipHost(fonts, 1.5f) { Width = 800f, Height = 600f }, 800f, 600f);
        Assert.Contains((21f, BundledUiFontWeight.Regular), baked);
        Assert.Contains((21f, BundledUiFontWeight.SemiBold), baked);
        Assert.DoesNotContain((14f, BundledUiFontWeight.Regular), baked);
    }
```

`tests/AcDream.App.Tests/UI/Hud/HudTextMenuTests.cs`:

```csharp
using AcDream.App.UI;
using AcDream.App.UI.Hud;

namespace AcDream.App.Tests.UI.Hud;

public sealed class HudTextMenuTests
{
    [Fact]
    public void WithoutAnOverride_SameAsHudIsMarked_WithTheGlobalSize()
    {
        HudTextSettings s = HudTextSettings.Default.WithGlobal(120);
        Assert.Equal(
            ["Text size", "* Same as HUD (120%)", "80%", "90%", "100%", "110%", "120%", "130%", "140%", "150%"],
            HudTextMenu.Labels(s, HudDeck.Command));
    }

    [Fact]
    public void WithAnOverride_ItsSizeIsMarked_OtherDecksStaySameAsHud()
    {
        HudTextSettings s = HudTextSettings.Default.WithOverride(HudDeck.Gear, 140);
        string[] labels = HudTextMenu.Labels(s, HudDeck.Gear);
        Assert.Equal("Same as HUD (100%)", labels[1]);
        Assert.Equal("* 140%", labels[8]);
        Assert.Equal("* Same as HUD (100%)", HudTextMenu.Labels(s, HudDeck.Chat)[1]);
    }

    [Fact]
    public void TheHeadingIsDisabled_EveryChoiceIsEnabled()
    {
        Assert.Equal(10, HudTextMenu.Count);
        Assert.False(HudTextMenu.Enabled(0));
        for (int i = 1; i < HudTextMenu.Count; i++) Assert.True(HudTextMenu.Enabled(i));
        Assert.False(HudTextMenu.Enabled(HudTextMenu.Count));
    }

    [Fact]
    public void Choose_SetsOrClearsTheDecksOverride()
    {
        var text = new HudText();
        HudTextMenu.Choose(text, HudDeck.Target, 9);   // 150%
        Assert.Equal(150, text.Settings.PercentFor(HudDeck.Target));
        HudTextMenu.Choose(text, HudDeck.Target, 1);   // Same as HUD
        Assert.False(text.Settings.HasOverride(HudDeck.Target));
        HudTextMenu.Choose(text, HudDeck.Target, 0);   // the heading does nothing
        Assert.False(text.Settings.HasOverride(HudDeck.Target));
    }

    [Fact]
    public void StyleMenu_GivesTheMenuTheDecksScaleAndTheFactorsFace()
    {
        var menu = new UiMarkupContextMenu();
        HudTextMenu.StyleMenu(menu, FakeDeckFonts.Make(), 1.2f, 1.5f);
        float big = FakeDeckFonts.Line(DeckFonts.PixelHeight(DeckFonts.SmallPixelHeight, 1.2f * 1.5f));
        float own = FakeDeckFonts.Line(DeckFonts.PixelHeight(DeckFonts.SmallPixelHeight, 1.2f));
        Assert.Equal(1.2f, menu.Scale);
        Assert.Equal(big, menu.DatFont!.LineHeight);
        Assert.Equal(big - own, menu.RowGrow, 3);
    }
}
```

- [ ] **Step 3: Run** the Verify command → compile errors (`RowGrow`, `textFactor`, `HudTextMenu`).

- [ ] **Step 4: Menu row growth** in `UiMarkupContextMenu.cs`. After the `Scale` property add:

```csharp
    /// <summary>
    /// Points added to every item row (not separators), read as the menu opens. A Modern HUD
    /// deck whose text is larger than its scale sets the line's growth here so each row holds
    /// its text; every other menu leaves it at 0.
    /// </summary>
    public float RowGrow { get; set; }
```

Add the field `private float _rowGrow;   // RowGrow as of the open` next to `_scale`. In `TryOpen`, after `_scale = Scale > 0f ? Scale : 1f;`, add `_rowGrow = MathF.Max(0f, RowGrow);`, and change the row step to:

```csharp
            y += separator ? SeparatorHeight * _scale : ItemHeight * _scale + _rowGrow;
```

In `OnDrawOverlay` change `itemHeight = ItemHeight * _scale` to `itemHeight = ItemHeight * _scale + _rowGrow`.

- [ ] **Step 5: Tooltip factor** in `HudTooltip.Draw`. Add a trailing parameter `float textFactor = 1f`, change `DeckFontSet? set = fonts?.At(scale);` to `DeckFontSet? set = fonts?.At(scale, textFactor);`, and add to the doc comment: "<paramref name="textFactor"/> sizes the faces only; pads, gaps and the wrap width follow <paramref name="scale"/>." The existing callers compile unchanged, and Tasks 5–8 pass the factor.

- [ ] **Step 6: The shared block.** Create `src/AcDream.App/UI/Hud/HudTextMenu.cs`:

```csharp
using System.Globalization;

namespace AcDream.App.UI.Hud;

/// <summary>
/// The "Text size" block a deck's frame menu lists. The deck menus have no submenus, so the
/// block is a heading and its choices in the same list: "Text size" (disabled), "Same as HUD (N%)",
/// then 80% … 150%. The deck's current choice is marked "* ", as the Gear Deck's sort menu marks its sort.
/// </summary>
internal static class HudTextMenu
{
    public const string Heading = "Text size";
    public const string Mark = "* ";

    /// <summary>The block's entries: the heading, Same as HUD, and one per size.</summary>
    public static int Count => 2 + HudTextSettings.Steps.Count;

    public static string[] Labels(HudTextSettings settings, HudDeck deck)
    {
        var labels = new string[Count];
        bool own = settings.HasOverride(deck);
        labels[0] = Heading;
        labels[1] = (own ? "" : Mark) + "Same as HUD (" + settings.Global.ToString(CultureInfo.InvariantCulture) + "%)";
        for (int i = 0; i < HudTextSettings.Steps.Count; i++)
        {
            int step = HudTextSettings.Steps[i];
            labels[2 + i] = (own && settings.PercentFor(deck) == step ? Mark : "")
                + step.ToString(CultureInfo.InvariantCulture) + "%";
        }
        return labels;
    }

    /// <summary>Whether block entry <paramref name="index"/> can be chosen: every entry but the heading.</summary>
    public static bool Enabled(int index) => index > 0 && index < Count;

    /// <summary>Applies block entry <paramref name="index"/>: Same as HUD drops the deck's override, a size sets it.</summary>
    public static void Choose(HudText text, HudDeck deck, int index)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (index == 1) text.SetOverride(deck, null);
        else if (index >= 2 && index < Count) text.SetOverride(deck, HudTextSettings.Steps[index - 2]);
    }

    /// <summary>A deck menu at the deck's scale: rows, insets and widths at <paramref name="scale"/>, the
    /// small face at scale × factor, and each row grown to hold that face.</summary>
    public static void StyleMenu(UiMarkupContextMenu menu, DeckFonts? fonts, float scale, float textFactor)
    {
        menu.Scale = scale;
        menu.DatFont = fonts?.At(scale, textFactor).Small;
        menu.RowGrow = DeckFonts.RowGrow(fonts, DeckFonts.SmallPixelHeight, BundledUiFontWeight.Regular, scale, textFactor);
    }
}
```

- [ ] **Step 7: Run** the Verify command → all pass.

- [ ] **Step 8: Commit.**

```bash
git add src/AcDream.App/UI/UiMarkupContextMenu.cs src/AcDream.App/UI/Hud/HudTooltip.cs src/AcDream.App/UI/Hud/HudTextMenu.cs \
  tests/AcDream.App.Tests/UI/Hud/HudTestInput.cs tests/AcDream.App.Tests/UI/UiMarkupContextMenuTests.cs \
  tests/AcDream.App.Tests/UI/Hud/HudTooltipTests.cs tests/AcDream.App.Tests/UI/Hud/HudTextMenuTests.cs
git commit -m "HUD text size: menu row growth, tooltip factor and the Text size block

Claude-Session: https://claude.ai/code/session_017hCEwifLBBM9NGyiUdMFVs"
```

---

### Task 4: The Config page row and the runtime owner

**Goal:** The Config options page shows a "HUD text size" row (80–150, step 10) under the Modern HUD row. The runtime owns one `HudText` loaded from the client's settings file, and the row reads and sets its global size.

**Files:**
- Modify: `src/AcDream.App/UI/Layout/ConfigOptionsPageController.cs` (the `Bindings` record ~line 129, `ModernHudBindings` ~line 140, the Modern HUD row ~line 1370)
- Modify: `src/AcDream.App/UI/RetailUiRuntime.cs` (fields near `_deckFonts` ~line 454; the Config `Bindings` ~line 3219)
- Test: `tests/AcDream.App.Tests/UI/Layout/ConfigOptionsPageControllerTests.cs` (add two cases)

**Acceptance Criteria:**
- [ ] `ConfigOptionsPageController.HudTextBindings(Func<int> Load, Action<int> Save)` exists, and `Bindings` has `HudText { get; init; }`.
- [ ] With both `ModernHud` and `HudText` bound, the page has one more row (41) than with Modern HUD alone. The last row is a `FloatOptionRow` whose default is 100 and whose current value is the loaded size. The row before it is the Modern HUD toggle.
- [ ] Setting the row to 123 saves 120, snapped on the 10 step.
- [ ] With `HudText` bound and no `ModernHud`, no row is added.
- [ ] `RetailUiRuntime.HudTextSize` is loaded once from `_bindings.Persistence?.Store` with `HudText.Load`. The Config row is bound to `HudTextSize.Settings.Global` / `HudTextSize.SetGlobal` whenever the Modern HUD row is bound.
- [ ] The App builds.

**Verify:** `cd ~/code/OpenAC/OpenAC-hud-text-size && export PATH=$HOME/.dotnet:$PATH && dotnet build src/AcDream.App -c Release && dotnet test tests/AcDream.App.Tests -c Release --filter "FullyQualifiedName~ConfigOptionsPageControllerTests"` → build succeeds, all pass

**Steps:**

- [ ] **Step 1: Write the failing tests.** Append to `ConfigOptionsPageControllerTests`:

```csharp
    private static OptionsPanelController NewOptionsPanel(ImportedLayout layout) =>
        OptionsPanelController.Bind(
            layout,
            new OptionsPanelController.Callbacks(
                Toggle: () => { },
                RequestExitToCharacterSelection: () => { },
                ExitGame: () => { },
                UseMouseTurningSettings: () => { },
                DisplaySystemMessage: _ => { }))!;

    [Fact]
    public void HudTextBindings_add_a_slider_under_the_modern_hud_row_that_snaps_to_tens()
    {
        ImportedLayout layout = FixtureLoader.LoadOptionsPanelHost();
        OptionsPanelController controller = NewOptionsPanel(layout);
        int stored = 100;
        var saves = new List<int>();

        bool bound = ConfigOptionsPageController.Bind(
            layout,
            controller.ConfigPage,
            MakeTemplateResolver(),
            (_, _) => null,
            new FakeBindings().ToBindings() with
            {
                ModernHud = new ConfigOptionsPageController.ModernHudBindings(Load: () => false, Save: _ => { }),
                HudText = new ConfigOptionsPageController.HudTextBindings(
                    Load: () => stored,
                    Save: value => { stored = value; saves.Add(value); }),
            });

        Assert.True(bound);
        Assert.Equal(41, controller.ConfigPage.Rows.Count);
        Assert.IsType<BoolOptionRow>(controller.ConfigPage.Rows[^2]);
        var row = Assert.IsType<FloatOptionRow>(controller.ConfigPage.Rows[^1]);
        Assert.Equal(100f, row.DefaultValue);
        Assert.Equal(100f, row.Current);

        row.SetCurrentValue(123f);

        Assert.Equal(120, stored);
        Assert.Equal([120], saves);
    }

    [Fact]
    public void HudTextBindings_without_the_modern_hud_row_add_nothing()
    {
        ImportedLayout layout = FixtureLoader.LoadOptionsPanelHost();
        OptionsPanelController controller = NewOptionsPanel(layout);

        bool bound = ConfigOptionsPageController.Bind(
            layout,
            controller.ConfigPage,
            MakeTemplateResolver(),
            (_, _) => null,
            new FakeBindings().ToBindings() with
            {
                HudText = new ConfigOptionsPageController.HudTextBindings(Load: () => 100, Save: _ => { }),
            });

        Assert.True(bound);
        Assert.Equal(39, controller.ConfigPage.Rows.Count);
    }
```

- [ ] **Step 2: Run** the Verify command → compile errors (`HudTextBindings`, `HudText`).

- [ ] **Step 3: Bindings.** In `ConfigOptionsPageController.Bindings` after `ModernHud`:

```csharp
        /// <summary>The HUD text size row under the Modern HUD row; left out without it (or without Modern HUD).</summary>
        public HudTextBindings? HudText { get; init; }
```

After `ModernHudBindings`:

```csharp
    /// <summary>The Modern HUD's text size in percent: Load reads the HUD-wide size, Save stores and applies it.</summary>
    public sealed record HudTextBindings(Func<int> Load, Action<int> Save);
```

- [ ] **Step 4: The row.** Inside `if (bindings.ModernHud is { } modernHud) { … }`, after its `BuildExplicitToggleRow(...)` call:

```csharp
            // The Modern HUD decks' text size: 80-150% in 10% steps, the decks keep
            // their size. A deck's own right-click choice overrides it.
            if (bindings.HudText is { } hudText)
            {
                BuildExplicitNumericSliderRow(
                    listBox,
                    "HUD text size",
                    Hud.HudTextSettings.MinPercent,
                    Hud.HudTextSettings.MaxPercent,
                    step: Hud.HudTextSettings.StepPercent,
                    integer: true,
                    Hud.HudTextSettings.DefaultPercent,
                    page,
                    read: () => hudText.Load(),
                    apply: value =>
                    {
                        hudText.Save((int)Math.Round(value));
                        return true;
                    },
                    isCurrent: static () => true,
                    tooltip:
                        "How big the Modern HUD's text is, from 80% to 150%. The decks "
                        + "keep their size: lists grow taller rows and short labels shrink "
                        + "to fit. Right-click a deck's frame to give it its own size.",
                    rangeLowText: "80%",
                    rangeHighText: "150%");
            }
```

(`ConfigOptionsPageController` is in `AcDream.App.UI.Layout`, so `Hud.HudTextSettings` resolves to `AcDream.App.UI.Hud.HudTextSettings`.)

- [ ] **Step 5: The runtime owner.** In `RetailUiRuntime`, next to `private Hud.DeckFonts? _deckFonts;`:

```csharp
    private Hud.HudText? _hudText;

    /// <summary>The Modern HUD text size, per client: loaded once from the settings file the
    /// Modern HUD switch lives in, saved there on every change.</summary>
    internal Hud.HudText HudTextSize => _hudText ??= Hud.HudText.Load(_bindings.Persistence?.Store, Console.WriteLine);
```

In the Config `Bindings` initializer, after `ModernHud = _bindings.Options.ModernHud,`:

```csharp
                    HudText = _bindings.Options.ModernHud is null
                        ? null
                        : new Layout.ConfigOptionsPageController.HudTextBindings(
                            Load: () => HudTextSize.Settings.Global,
                            Save: HudTextSize.SetGlobal),
```

- [ ] **Step 6: Run** the Verify command → build succeeds, all pass.

- [ ] **Step 7: Commit.**

```bash
git add src/AcDream.App/UI/Layout/ConfigOptionsPageController.cs src/AcDream.App/UI/RetailUiRuntime.cs \
  tests/AcDream.App.Tests/UI/Layout/ConfigOptionsPageControllerTests.cs
git commit -m "HUD text size: Config row under Modern HUD and the runtime owner

Claude-Session: https://claude.ai/code/session_017hCEwifLBBM9NGyiUdMFVs"
```

---

### Task 5: Gear Deck text size

**Goal:** The Gear Deck draws its text at its scale × its text factor. List and suggestion rows grow. Titles, headers, studs, chips, saved studs, grid captions, side cells, the query field, the footer and the salvage button keep their boxes and fit then cap their text. The deck menu and tooltip follow the factor. A right-click on the frame or title lists the Text size block. A change rebuilds the deck at once.

**Files:**
- Modify: `src/AcDream.App/UI/Hud/GearDeckGeometry.cs`, `src/AcDream.App/UI/Hud/UiGearDeck.cs`, `src/AcDream.App/UI/Hud/UiGearDeck.Draw.cs`, `src/AcDream.App/UI/Hud/UiGearDeck.Query.cs`, `src/AcDream.App/UI/Hud/UiGearDeck.Menu.cs`, `src/AcDream.App/UI/Hud/UiGearDeck.Input.cs`, `src/AcDream.App/UI/RetailUiRuntime.cs` (`MountGearDeck`)
- Test: `tests/AcDream.App.Tests/UI/Hud/GearDeckGeometryTests.cs` (add a case), `tests/AcDream.App.Tests/UI/Hud/UiGearDeckTextSizeTests.cs` (create); every existing `UiGearDeck*Tests` must still pass

**Acceptance Criteria:**
- [ ] `GearDeckBindings` gains a trailing optional `HudText? Text = null`. `UiGearDeck.TextFactor` is `Text.FactorFor(HudDeck.Gear)`, or 1 without it.
- [ ] `GearDeckGeometry.Create(…, scale, textGrow)` adds `TextGrow` to `ListRowHeight` and `SuggestRowHeight` only. `ListRow`, `VisibleListRows` and `Suggestion` use them. Every other rectangle, `MinHeightFor` and the frame are unchanged.
- [ ] At 80%, 100% and 150% the deck's `Width`, `Height` and `MinHeight` are the same.
- [ ] At 150% with fonts, list rows and suggestion rows are taller and fewer list rows show. The sort stud, side cells, footer and title are the same rectangles as at 100%.
- [ ] Drawn at 150% in grid and list (also at deck scale 0.75), every fitted face's line is no taller than its box (`FakeDeckFonts.AssertAllFit`), and the footer is among the fitted boxes.
- [ ] A right-click on the title through `UiRoot` opens the deck menu with `HudTextMenu.Labels(…, HudDeck.Gear)`, the heading disabled. Choosing 150% sets the Gear override only, and `TextFactor` is 1.5 at once. A right-click on an item still lists the item menu (existing tests).
- [ ] `HudText.Changed` rebuilds the geometry at once (no tick), and `Detach` unsubscribes.
- [ ] At 150%, the deck menu's face is the small face at 1.5 and its `RowGrow` is the line growth. The query field's face is capped to its box.
- [ ] The runtime passes `Text: HudTextSize`.

**Verify:** `cd ~/code/OpenAC/OpenAC-hud-text-size && export PATH=$HOME/.dotnet:$PATH && dotnet test tests/AcDream.App.Tests -c Release --filter "FullyQualifiedName~GearDeck"` → all pass

**Steps:**

- [ ] **Step 1: Write the failing tests.**

Append to `GearDeckGeometryTests`:

```csharp
    [Fact]
    public void TextGrow_GrowsListAndSuggestionRowsOnly()
    {
        var input = new GearDeckLayoutInput(false, true, 0, 40, 2, 0, Suggestions: 3);
        GearDeckGeometry normal = GearDeckGeometry.Create(648f, input, null, 1f);
        GearDeckGeometry grown = GearDeckGeometry.Create(648f, input, null, 1f, textGrow: 6f);

        Assert.Equal(GearDeckGeometry.ListRowH + 6f, grown.ListRow(0, 0)!.Value.H);
        Assert.Equal(GearDeckGeometry.SuggestH + 6f, grown.Suggestion(0).H);
        Assert.Equal(grown.Suggestion(0).Y + grown.Suggestion(0).H, grown.Suggestion(1).Y, 3);
        Assert.True(grown.VisibleListRows < normal.VisibleListRows);
        Assert.Equal(normal.Frame, grown.Frame);
        Assert.Equal(normal.Contents, grown.Contents);
        Assert.Equal(normal.SortStud, grown.SortStud);
        Assert.Equal(normal.SidePack(0), grown.SidePack(0));
        Assert.Equal(normal.GridCell(0, 0), grown.GridCell(0, 0));
    }
```

`tests/AcDream.App.Tests/UI/Hud/UiGearDeckTextSizeTests.cs`:

```csharp
using AcDream.App.UI;
using AcDream.App.UI.Hud;
using AcDream.Core.Items;
using Xunit;

namespace AcDream.App.Tests.UI.Hud;

public sealed class UiGearDeckTextSizeTests
{
    private const uint Player = 0x50000001, Bag = 0xB1, First = 0xA00;

    private sealed class NoActions : IGearItemActions
    {
        public bool Ready() => true;
        public void Report(string text) { }
        public bool PutInContainer(uint item, uint container, int placement, bool fromGround) => true;
        public bool Merge(uint from, uint to) => true;
        public bool WieldFromPaperdoll(uint item, EquipMask mask) => true;
        public bool WieldSecondary(uint item) => true;
        public bool Activate(uint item) => true;
        public bool OpenPack(uint pack) => true;
    }

    private static void Place(ClientObjectTable t, ClientObject o, uint container, int slot)
    {
        t.AddOrUpdate(o);
        Assert.True(t.MoveItem(o.ObjectId, container, slot));
    }

    private static HudText At(int percent) => new(HudTextSettings.Default.WithGlobal(percent));

    private static UiGearDeck Make(HudText text, DeckFonts? fonts = null, bool list = false, float scale = 1f)
    {
        var t = new ClientObjectTable();
        t.AddOrUpdate(new ClientObject { ObjectId = Player, Type = ItemType.Creature, ItemsCapacity = 102, ContainersCapacity = 7 });
        Place(t, new ClientObject { ObjectId = Bag, Type = ItemType.Container, ItemsCapacity = 24, Name = "Pack" }, Player, 0);
        for (int i = 0; i < 30; i++)
            Place(t, new ClientObject { ObjectId = First + (uint)i, Name = $"A long item name {i}", Type = ItemType.Misc, StackSize = 25 }, Player, i);
        var session = new GearDeckSession(null, () => "", () => false);
        if (list) session.Update(s => s with { View = GearPackView.List });
        var b = new GearDeckBindings(() => t, () => Player, () => 100, (_, _, _, _, _) => 0u, (_, _, _, _, _) => 0u,
            o => o.Name, session, new NoActions(), _ => false, () => { }, Text: text);
        var deck = new UiGearDeck(b, fonts, null, () => 0.0);
        if (scale != 1f) deck.SetScale(scale);
        deck.Refresh();
        return deck;
    }

    [Theory]
    [InlineData(80)]
    [InlineData(100)]
    [InlineData(150)]
    public void TheWindowKeepsItsSize_AtEveryTextSize(int percent)
    {
        DeckFonts fonts = FakeDeckFonts.Make();
        UiGearDeck plain = Make(new HudText(), fonts);
        UiGearDeck deck = Make(At(percent), fonts);
        Assert.Equal(plain.Width, deck.Width);
        Assert.Equal(plain.Height, deck.Height);
        Assert.Equal(plain.MinHeight, deck.MinHeight);
    }

    [Fact]
    public void ListAndSuggestionRowsGrow_FixedBoxesDoNot()
    {
        DeckFonts fonts = FakeDeckFonts.Make();
        GearDeckGeometry normal = Make(new HudText(), fonts, list: true).Geometry;
        GearDeckGeometry large = Make(At(150), fonts, list: true).Geometry;

        Assert.True(large.ListRow(0, 0)!.Value.H > normal.ListRow(0, 0)!.Value.H);
        Assert.True(large.Suggestion(0).H > normal.Suggestion(0).H);
        Assert.True(large.VisibleListRows < normal.VisibleListRows);
        Assert.Equal(normal.SortStud, large.SortStud);
        Assert.Equal(normal.SidePack(0), large.SidePack(0));
        Assert.Equal(normal.Footer, large.Footer);
        Assert.Equal(normal.Title, large.Title);
    }

    [Theory]
    [InlineData(false, 1f)]
    [InlineData(true, 1f)]
    [InlineData(false, 0.75f)]
    [InlineData(true, 0.75f)]
    public void AtLargeText_NoFaceIsTallerThanItsBox(bool list, float scale)
    {
        var fits = new List<DeckFit>();
        UiGearDeck deck = Make(At(150), FakeDeckFonts.Make(fits), list, scale);
        MarkupDrawProbe.Draw(deck, deck.Width, deck.Height);
        Assert.NotEmpty(fits);
        FakeDeckFonts.AssertAllFit(fits);
        Assert.Contains(fits, f => MathF.Abs(f.BoxHeight - deck.Geometry.Footer.H) < 0.01f);
    }

    [Fact]
    public void ARightClickOnTheTitle_ListsTextSize_AndAChoiceOverridesOnlyThisDeck()
    {
        HudText text = At(110);
        UiGearDeck deck = Make(text);
        var root = new UiRoot { Width = 1200, Height = 900 };
        root.AddChild(deck);
        deck.Refresh();
        DeckRect title = deck.Geometry.Title;
        HudTestInput.RightClick(root, deck, title.X + 40f, title.Y + title.H / 2f);

        Assert.True(deck.Menu.IsOpen);
        Assert.Equal(HudTextMenu.Labels(text.Settings, HudDeck.Gear), deck.Menu.ItemsSource());
        Assert.False(deck.Menu.ItemsEnabledSource!()[0]);

        deck.Menu.Selected!(9);   // 150%
        Assert.Equal(150, text.Settings.PercentFor(HudDeck.Gear));
        Assert.Equal(110, text.Settings.PercentFor(HudDeck.Command));
        Assert.Equal(1.5f, deck.TextFactor, 3);
    }

    [Fact]
    public void AChange_RebuildsAtOnce_AndSameAsHudFollowsTheGlobal()
    {
        var text = new HudText();
        UiGearDeck deck = Make(text, FakeDeckFonts.Make(), list: true);
        float before = deck.Geometry.ListRow(0, 0)!.Value.H;

        text.SetGlobal(150);
        Assert.Equal(1.5f, deck.TextFactor, 3);
        Assert.True(deck.Geometry.ListRow(0, 0)!.Value.H > before);

        text.SetOverride(HudDeck.Gear, 80);
        Assert.Equal(0.8f, deck.TextFactor, 3);
        Assert.Equal(before, deck.Geometry.ListRow(0, 0)!.Value.H, 3);

        text.SetOverride(HudDeck.Gear, null);
        Assert.Equal(1.5f, deck.TextFactor, 3);

        deck.Detach();
        text.SetGlobal(100);
        Assert.Equal(1.5f, deck.TextFactor, 3);   // no longer listening
    }

    [Fact]
    public void TheDeckMenuAndQueryFieldFollowTheTextSize()
    {
        UiGearDeck deck = Make(At(150), FakeDeckFonts.Make());
        Assert.Equal(FakeDeckFonts.Line(18f), deck.Menu.DatFont!.LineHeight);
        Assert.Equal(6f, deck.Menu.RowGrow, 3);
        // The field's box is 22 - 2 x 4 padding = 14: the 18 px face (line 20) drops to the 12 px face (line 14).
        Assert.Equal(FakeDeckFonts.Line(12f), deck.QueryInput.DatFont!.LineHeight);
    }
}
```

- [ ] **Step 2: Run** the Verify command → compile errors (`textGrow`, `Text`, `TextFactor`).

- [ ] **Step 3: Geometry.** In `GearDeckGeometry`:
  - add `public required float TextGrow { get; init; }` (doc: "Points every text row (list rows, suggestions) is grown by: `DeckFonts.RowGrow` at the deck's text size.")
  - add `public float ListRowHeight => S(ListRowH) + TextGrow;` and `public float SuggestRowHeight => S(SuggestH) + TextGrow;`
  - `VisibleListRows` → `Math.Max(0, (int)MathF.Floor(Contents.H / ListRowHeight))`
  - `ListRow` → `new DeckRect(Contents.X, Contents.Y + row * ListRowHeight, Contents.W, ListRowHeight)`
  - `Suggestion` → `new(QueryText.X, QueryBar.Y + S(QueryH) + S(1f) + index * SuggestRowHeight, QueryText.W, SuggestRowHeight)`
  - `Create(float height, GearDeckLayoutInput input, GearPills? pills = null, float scale = 1f, float textGrow = 0f)`, and set `TextGrow = MathF.Max(0f, textGrow)` in the initializer.

- [ ] **Step 4: Factor, growth and the change event** in `UiGearDeck.cs`:
  - `GearDeckBindings`: append `HudText? Text = null` after `IGearSalvage? Salvage = null`.
  - Fields: `private float _textFactor;`. Members: `internal float TextFactor => _textFactor;` and

```csharp
    /// <summary>How much the list and suggestion rows grow at the deck's text size.</summary>
    private float TextGrow => DeckFonts.RowGrow(_fonts, DeckFonts.SmallPixelHeight, BundledUiFontWeight.Regular, _scale, _textFactor);

    /// <summary>A text size change does what a scale change does: drop fitted text, rebuild geometry.</summary>
    private void OnTextSizeChanged() => SyncGeometry();
```

  - Constructor: after `_scale = 1f;` add `_textFactor = _b.Text?.FactorFor(HudDeck.Gear) ?? 1f;`. Change the geometry line to `_g = GearDeckGeometry.Create(Height, LayoutInput(), null, _scale, TextGrow);`. After `_b.Session.Ended += OnSessionEnded;` add `if (_b.Text is { } text) text.Changed += OnTextSizeChanged;`.
  - `Detach`:

```csharp
    internal void Detach()
    {
        _b.Session.Ended -= OnSessionEnded;
        if (_b.Text is { } text) text.Changed -= OnTextSizeChanged;
    }
```

  - `SyncGeometry`: compute `float grow = TextGrow;` after `SyncScale();`. Add `|| _g.TextGrow != grow` to the rebuild condition, and pass it: `_g = GearDeckGeometry.Create(Height, input, pills, _scale, grow);`.
  - `SyncScale`: read the factor beside the scale and treat a change the same way:

```csharp
        float scale = GearDeckGeometry.ScaleForWidth(Width);
        float factor = _b.Text?.FactorFor(HudDeck.Gear) ?? 1f;
        if (scale != _scale || factor != _textFactor)
        {
            _scale = scale;
            _textFactor = factor;
            SetFieldFonts();
            SetMenuScale();   // UiGearDeck.Menu.cs
            _pillsFor = null;   // pills are measured with the face at the scale
            _sortText = null;
            _fittedFooter = null;
            _fittedSalvage = null;
            Array.Clear(_suggestTexts);
            Array.Clear(_slotLabels);
            Array.Clear(_fittedChips);
            Array.Clear(_fittedSaved);
            ClearFitted();
        }
```

- [ ] **Step 5: Faces in boxes** in `UiGearDeck.Draw.cs`. Replace the `Small` and `Bold` properties with:

```csharp
    /// <summary>The small face for text in <paramref name="box"/>: the deck's text size, dropped to the largest that fits the box.</summary>
    private DeckFace SmallIn(DeckRect box) =>
        DeckFace.In(_fonts, DeckFonts.SmallPixelHeight, BundledUiFontWeight.Regular, _scale, _textFactor, box.H);

    /// <summary>The small SemiBold face for text in <paramref name="box"/>.</summary>
    private DeckFace BoldIn(DeckRect box) =>
        DeckFace.In(_fonts, DeckFonts.SmallPixelHeight, BundledUiFontWeight.SemiBold, _scale, _textFactor, box.H);
```

  Then fix every former use. The compiler lists each one. A row that grows already has its taller box, so its face is not capped:

| Where | Old | New |
|---|---|---|
| `OnDraw` "Gear" title | `Bold` | `BoldIn(g.Title)` |
| `OnDraw` footer left/right | `Small` | `SmallIn(g.Footer)` (both calls and the `Measure`) |
| `FittedFooterLeft` | `Small` | `SmallIn(_g.Footer)` |
| `DrawSalvageButton` | `Small` | `SmallIn(r)` (fit and draw) |
| `DrawEquipHeader` "Equipment" | `Bold` | `BoldIn(h)` |
| `DrawPacksHeader` "Packs" / burden | `Bold` / `Small` | `BoldIn(h)` / `SmallIn(h)` |
| `DrawLabelledStud` | `Bold` | `BoldIn(r)` |
| `DrawSlotLabel` | `Small` | `SmallIn(r)`, or `SmallIn(r with { H = r.H / 2f })` when the label splits over two lines: decide the split with `SmallIn(r)`, then draw both lines in the half-height face |
| `DrawGrid` stack and badge | `Small` | `SmallIn(r with { H = r.H / 2f })` per cell (two captions share a cell) |
| `DrawList` | `Small` | `SmallIn(r)` per row (the row has grown) |
| `DrawQueryBar` placeholder / "+N" | `Small` / `Bold` | `SmallIn(well)` / `BoldIn(more)` |
| `DrawQueryStuds` sort label | `Bold` | `BoldIn(g.SortStud)` |
| `DrawSavedRow` saved studs / "+N" | `Bold` | `BoldIn(r)` / `BoldIn(more)` |
| `DrawChip` | `Bold` | `BoldIn(r)` |
| `DrawSuggestions` | `Small` | `SmallIn(_g.Suggestion(0))` (every row is the same height) |
| `DrawSideColumn` badge | `Small` | `SmallIn(_g.SidePack(0) with { H = _g.SidePack(0).H / 2f })` |

  In `UiGearDeck.Query.cs` `Pills()`, change `DeckFace bold = Bold;` to `DeckFace bold = DeckFace.SmallBoldFor(_fonts, _scale);   // pill widths at the deck's own size: the pills are fixed boxes`.

- [ ] **Step 6: Fields, menu and tooltip.**
  - `UiGearDeck.Query.cs` `SetFieldFonts`:

```csharp
    /// <summary>The query and name fields type in the deck's small face at its text size, capped to the field's box.</summary>
    private void SetFieldFonts()
    {
        float padding = 4f * _scale;
        UiDatFont? face = _fonts?.FitFace(DeckFonts.SmallPixelHeight, BundledUiFontWeight.Regular, _scale, _textFactor,
            GearDeckGeometry.QueryH * _scale - 2f * padding);
        QueryInput.DatFont = face;
        SaveNameInput.DatFont = face;
        QueryInput.Padding = SaveNameInput.Padding = padding;
    }
```

  - `UiGearDeck.Menu.cs`: add `TextSize` to `MenuKind`. In `PickMenu`'s switch add `GearZone.Frame when _b.Text is not null => MenuKind.TextSize,` before the `_ =>` arm (the title and empty frame hit `Frame`; items, chips and studs keep their own zones). In `MenuItems()` add `case MenuKind.TextSize: return HudTextMenu.Labels(_b.Text!.Settings, HudDeck.Gear);`. In `MenuEnabled()` after the `Item` block add:

```csharp
        if (_menuKind == MenuKind.TextSize)
            for (int i = 0; i < count; i++) flags[i] = HudTextMenu.Enabled(i);
```

  In `ChooseMenuItem` add `case MenuKind.TextSize when _b.Text is { } text: HudTextMenu.Choose(text, HudDeck.Gear, index); break;`. Replace the body of `SetMenuScale` with `HudTextMenu.StyleMenu(_menu, _fonts, _scale, _textFactor);` and update its summary to "at the deck's scale, in the deck's small face at its text size".
  - `UiGearDeck.Input.cs` `OnDrawOverlay`: `HudTooltip.Draw(ctx, tip, HudTooltipStyle.Compact, anchor, ScreenPosition, FindRoot()?.Width, _tooltipLayout, _fonts, _scale, _textFactor);`

- [ ] **Step 7: Runtime.** In `MountGearDeck`'s `GearDeckBindings`, after the `Salvage:` argument add `Text: HudTextSize`.

- [ ] **Step 8: Run** the Verify command → all pass, the existing Gear Deck tests included.

- [ ] **Step 9: Commit.**

```bash
git add src/AcDream.App/UI/Hud/GearDeckGeometry.cs src/AcDream.App/UI/Hud/UiGearDeck.cs src/AcDream.App/UI/Hud/UiGearDeck.Draw.cs \
  src/AcDream.App/UI/Hud/UiGearDeck.Query.cs src/AcDream.App/UI/Hud/UiGearDeck.Menu.cs src/AcDream.App/UI/Hud/UiGearDeck.Input.cs \
  src/AcDream.App/UI/RetailUiRuntime.cs tests/AcDream.App.Tests/UI/Hud/GearDeckGeometryTests.cs \
  tests/AcDream.App.Tests/UI/Hud/UiGearDeckTextSizeTests.cs
git commit -m "Gear Deck: HUD text size (rows grow, boxes fit and cap, Text size menu)

Claude-Session: https://claude.ai/code/session_017hCEwifLBBM9NGyiUdMFVs"
```

---

### Task 6: Command Deck text size

**Goal:** The Command Deck's vital numbers and labels, row labels, casting line, CAST, H/M/L letters, orb label and jump label are fixed boxes: their text is fitted with "…" and its face capped. The jump bar and studs are unchanged. A right-click on the frame lists the Text size block, and the tooltip follows the factor.

**Files:**
- Modify: `src/AcDream.App/UI/Hud/UiCommandDeck.cs`, `src/AcDream.App/UI/Hud/UiCommandDeck.Draw.cs`, `src/AcDream.App/UI/RetailUiRuntime.cs` (`MountCommandDeck`)
- Test: `tests/AcDream.App.Tests/UI/Hud/UiCommandDeckTextSizeTests.cs` (create); every existing `UiCommandDeck*Tests` must still pass

**Acceptance Criteria:**
- [ ] `UiCommandDeck` takes a trailing optional `HudText? text = null` after `fonts`. `TextFactor` is `FactorFor(HudDeck.Command)`, or 1 without it.
- [ ] At 80%, 100% and 150% the deck stays `DeckGeometry.Default` in size.
- [ ] Drawn at 150% in melee, magic and peace, and at deck scale 0.75, every fitted face fits its box. The vital values are fitted to the bar's height with fewer pixels than the uncapped 150% face.
- [ ] Each bar's label is shortened with "…" so it stops before the value. "CAST" is shortened to the room beside its icon.
- [ ] A right-click on the frame through `UiRoot` opens `TextMenu` with the Command block. Choosing 140% sets the Command override, and `TextFactor` is 1.4. A right-click on the orb (a control) opens nothing.
- [ ] The tooltip is drawn with `textFactor: TextFactor`, and the runtime passes `HudTextSize`.

**Verify:** `cd ~/code/OpenAC/OpenAC-hud-text-size && export PATH=$HOME/.dotnet:$PATH && dotnet test tests/AcDream.App.Tests -c Release --filter "FullyQualifiedName~UiCommandDeck"` → all pass

**Steps:**

- [ ] **Step 1: Write the failing tests.** `tests/AcDream.App.Tests/UI/Hud/UiCommandDeckTextSizeTests.cs`:

```csharp
using AcDream.App.UI;
using AcDream.App.UI.Hud;
using AcDream.Core.Combat;
using AcDream.Core.Net.Messages;
using AcDream.Runtime;

namespace AcDream.App.Tests.UI.Hud;

public sealed class UiCommandDeckTextSizeTests
{
    private sealed class NoActions : ICommandDeckActions
    {
        public void ToggleCombat() { }
        public void PressAttack(AttackHeight height) { }
        public void ReleaseAttack() { }
        public void SetDesiredPower(float power) { }
        public void SetOption(CharacterOptionId option, bool value) { }
        public void Recall(RuntimePortalCommand command) { }
        public void CastOutlined() { }
    }

    private static HudText At(int percent) => new(HudTextSettings.Default.WithGlobal(percent));

    private static UiCommandDeck Make(CombatMode mode, HudText text, DeckFonts? fonts = null, float scale = 1f)
    {
        var input = default(CommandDeckInput) with
        {
            Mode = mode, DesiredPower = 0.5f, PowerBarLevel = 0.3f, SessionKey = 1,
            Health = new DeckVitalInput(0.2f, 1234u, 2000u), Stamina = new DeckVitalInput(0.6f, 600u, 1000u),
            Mana = new DeckVitalInput(1f, 100u, 100u),
            OutlinedSpellName = "Incantation of Flame Bolt", RepeatAttacks = true,
        };
        var deck = new UiCommandDeck(() => input, new CommandDeckModel(), new NoActions(), icons: null, clock: () => 1.0, fonts, text);
        deck.Refresh();
        if (scale != 1f) deck.ApplyGeometry(DeckGeometry.Create(scale, 1f));
        return deck;
    }

    [Theory]
    [InlineData(80)]
    [InlineData(100)]
    [InlineData(150)]
    public void TheDeckKeepsItsSize_AtEveryTextSize(int percent)
    {
        UiCommandDeck deck = Make(CombatMode.Melee, At(percent), FakeDeckFonts.Make());
        Assert.Equal(DeckGeometry.Default.Width, deck.Width);
        Assert.Equal(DeckGeometry.Default.Height, deck.Height);
    }

    [Theory]
    [InlineData(CombatMode.Melee, 1f)]
    [InlineData(CombatMode.Magic, 1f)]
    [InlineData(CombatMode.NonCombat, 1f)]
    [InlineData(CombatMode.Magic, 0.75f)]
    public void AtLargeText_EveryFaceFitsItsBox_AndTheValuesAreCapped(CombatMode mode, float scale)
    {
        var fits = new List<DeckFit>();
        UiCommandDeck deck = Make(mode, At(150), FakeDeckFonts.Make(fits), scale);
        MarkupDrawProbe.Draw(deck, deck.Width, deck.Height);

        Assert.NotEmpty(fits);
        FakeDeckFonts.AssertAllFit(fits);
        float uncapped = DeckFonts.PixelHeight(DeckFonts.TextPixelHeight, scale * 1.5f);
        DeckFit[] values = fits
            .Where(f => f.BasePixels == DeckFonts.TextPixelHeight && MathF.Abs(f.BoxHeight - deck.Geometry.HealthBar.H) < 0.01f)
            .ToArray();
        Assert.NotEmpty(values);
        Assert.All(values, f => Assert.True(f.Pixels < uncapped));
    }

    [Fact]
    public void ARightClickOnTheFrame_ListsTextSize_OnTheOrb_Nothing()
    {
        var text = new HudText();
        UiCommandDeck deck = Make(CombatMode.Melee, text);
        var root = new UiRoot { Width = 1200, Height = 800 };
        root.AddChild(deck);
        DeckRect frame = deck.Geometry.Frame;
        HudTestInput.RightClick(root, deck, frame.X + 20f, frame.Y + 6f);

        Assert.True(deck.TextMenu.IsOpen);
        Assert.Equal(HudTextMenu.Labels(text.Settings, HudDeck.Command), deck.TextMenu.ItemsSource());
        deck.TextMenu.Selected!(8);   // 140%
        Assert.Equal(140, text.Settings.PercentFor(HudDeck.Command));
        Assert.Equal(1.4f, deck.TextFactor, 3);
        deck.TextMenu.Close();

        DeckRect orb = deck.Geometry.Orb;
        HudTestInput.RightClick(root, deck, orb.X + orb.W / 2f, orb.Y + orb.H / 2f);
        Assert.False(deck.TextMenu.IsOpen);
    }
}
```

- [ ] **Step 2: Run** the Verify command → compile errors (the `text` argument, `TextMenu`, `TextFactor`).

- [ ] **Step 3: Constructor, factor and menu** in `UiCommandDeck.cs`:
  - Fields: `private readonly HudText? _text;`, `private readonly UiMarkupContextMenu _textMenu = new() { ThemePalette = ChatDeckPalette.Menu };`, `private DeckZone _menuZone;`
  - Members:

```csharp
    /// <summary>The deck's text factor: its own size or the HUD-wide one (1 without a text size).</summary>
    internal float TextFactor => _text?.FactorFor(HudDeck.Command) ?? 1f;

    /// <summary>The frame's right-click menu: the Text size block, on any part of the deck that is not a control.</summary>
    internal UiMarkupContextMenu TextMenu => _textMenu;
```

  - Constructor: add the parameter `HudText? text = null` after `DeckFonts? fonts = null`, then at the end of the body:

```csharp
        _text = text;
        _textMenu.Opening = (x, y) =>
        {
            _menuZone = _geometry.ZoneAt(_view, x, y);
            HudTextMenu.StyleMenu(_textMenu, _fonts, _geometry.Scale, TextFactor);
        };
        _textMenu.ItemsSource = () => _text is null || IsControl(_menuZone)
            ? []
            : HudTextMenu.Labels(_text.Settings, HudDeck.Command);
        _textMenu.ItemsEnabledSource = () => Enumerable.Range(0, HudTextMenu.Count).Select(HudTextMenu.Enabled).ToArray();
        _textMenu.Selected = index =>
        {
            if (_text is not null) HudTextMenu.Choose(_text, HudDeck.Command, index);
        };
        AddChild(_textMenu);
        ContextMenu = _textMenu;
```

  - `OnHidden`: add `_textMenu.Close(restoreFocus: false);`.
  - `OnDrawOverlay`: `HudTooltip.Draw(ctx, tip, TooltipStyle, _geometry.RectFor(_hover), ScreenPosition, FindRoot()?.Width, _tooltipLayout, _fonts, textFactor: TextFactor);`

- [ ] **Step 4: Faces in boxes** in `UiCommandDeck.Draw.cs`. Replace `Face(bool small)` with:

```csharp
    /// <summary>The face for text in <paramref name="band"/>: the deck's text size, dropped to the largest that fits the band.</summary>
    private DeckFace FaceIn(DeckRect band, bool small, bool bold = false) =>
        DeckFace.In(_fonts, small ? DeckFonts.SmallPixelHeight : DeckFonts.TextPixelHeight,
            bold ? BundledUiFontWeight.SemiBold : BundledUiFontWeight.Regular, _geometry.Scale, TextFactor, band.H);

    /// <summary>A small label shortened with "..." to <paramref name="width"/> (its band's width by default).</summary>
    private string FitLabel(UiRenderContext ctx, string text, DeckRect band, float? width = null) =>
        FitCastingText(text, t => FaceIn(band, small: true).Measure(ctx, t), width ?? band.W);
```

  and the three text helpers:

```csharp
    private void DrawText(UiRenderContext ctx, string text, float x, DeckRect band, Vector4 ink, bool small = false)
    {
        DeckFace face = FaceIn(band, small);
        face.Draw(ctx, text, x, band.Y + (band.H - face.LineHeight(ctx)) / 2f, ink);
    }

    private void DrawTextRight(UiRenderContext ctx, string text, float right, DeckRect band, Vector4 ink) =>
        DrawText(ctx, text, right - FaceIn(band, small: false).Measure(ctx, text), band, ink);

    private void DrawTextCentered(UiRenderContext ctx, string text, float centre, DeckRect band, Vector4 ink, bool small = false) =>
        DrawText(ctx, text, centre - FaceIn(band, small).Measure(ctx, text) / 2f, band, ink, small);
```

  The bar draws the value first, then the label in the room left of it:

```csharp
        float left = r.X + P(8), right = r.X + r.W - P(8);
        float valueW = FaceIn(r, small: false).Measure(ctx, vital.Text);
        DrawText(ctx, FitLabel(ctx, label, r, MathF.Max(0f, right - valueW - P(6) - left)), left, r, LabelInk, small: true);
        DrawTextRight(ctx, vital.Text, right, r, TextInk);
```

  The power row: `DrawText(ctx, FitLabel(ctx, v.PowerLabel, label), label.X, label, LabelInk, small: true);`. The casting row: `FitLabel(ctx, "CASTING", label)` for the label, and `FitCastingText(v.CastingText, t => FaceIn(text, small: true).Measure(ctx, t), text.W)` for the spell. The recall row: `FitLabel(ctx, "RECALL", label)`. Jump: `FitLabel(ctx, "JUMP", label)`. The orb label stays `DrawTextCentered` in its `P(16)` band, which caps it. The stud letters already pass the stud as the band.

  `DrawCastButton`:

```csharp
        Vector4 ink = DeckChrome.DrawStud(ctx, r, _geometry.Scale, on: false, dim);
        DeckFace face = FaceIn(r, small: true, bold: true);
        string word = FitCastingText("CAST", t => face.Measure(ctx, t), r.W - (14f + 3f + 4f) * _geometry.Scale);
        var (iconX, iconY, iconSize, textX) = DeckGeometry.CastLabel(r, face.Measure(ctx, word), _geometry.Scale);
        DrawIcon(ctx, HudIcons.Cast, iconX, iconY, iconSize, ink);
        face.Draw(ctx, word, textX, r.Y + (r.H - face.LineHeight(ctx)) / 2f, ink);
```

- [ ] **Step 5: Runtime.** In `MountCommandDeck`, pass `HudTextSize` after the fonts argument: `_deckFonts ??= Hud.DeckFonts.Load(_bindings.Assets.TextureCache), HudTextSize);`.

- [ ] **Step 6: Run** the Verify command → all pass. `CastingRowDrawsTheCastWordInTheSmallSemiBoldFace` still finds (12, SemiBold), because `FitFace` bakes the deck's own face at 100%.

- [ ] **Step 7: Commit.**

```bash
git add src/AcDream.App/UI/Hud/UiCommandDeck.cs src/AcDream.App/UI/Hud/UiCommandDeck.Draw.cs src/AcDream.App/UI/RetailUiRuntime.cs \
  tests/AcDream.App.Tests/UI/Hud/UiCommandDeckTextSizeTests.cs
git commit -m "Command Deck: HUD text size (fit and cap, Text size menu)

Claude-Session: https://claude.ai/code/session_017hCEwifLBBM9NGyiUdMFVs"
```

---

### Task 7: Spell Deck text size

**Goal:** The Spell Deck's icons are unchanged. Its key labels and bookmark numbers fit and cap in their boxes, its menu and tooltip follow the factor, and its frame menu ends with the Text size block.

**Files:**
- Modify: `src/AcDream.App/UI/Hud/UiSpellDeck.cs`, `src/AcDream.App/UI/Hud/UiSpellDeck.Draw.cs`, `src/AcDream.App/UI/RetailUiRuntime.cs` (`MountSpellDeck`)
- Test: `tests/AcDream.App.Tests/UI/Hud/UiSpellDeckTests.cs` (extend `Make`, add cases); every existing `UiSpellDeck*Tests` must still pass

**Acceptance Criteria:**
- [ ] `UiSpellDeck` takes a trailing optional `HudText? text = null` after `itemName`. `TextFactor` is `FactorFor(HudDeck.Spell)`. `Menu` exposes the deck menu.
- [ ] At 80% and 150% the deck's size is unchanged.
- [ ] At 150% with fonts, key labels (fitted to the slot's width) and bookmark numbers fit their boxes.
- [ ] A right-click on the empty frame through `UiRoot` lists the deck items, a separator, then the Spell block. Choosing its last entry sets the Spell override to 150%. A right-click on a slot does not list "Text size".
- [ ] The menu is styled with `HudTextMenu.StyleMenu(_menu, _fonts, 1f, TextFactor)` as it opens. The tooltip gets `textFactor: TextFactor`, and the runtime passes `text: HudTextSize`.

**Verify:** `cd ~/code/OpenAC/OpenAC-hud-text-size && export PATH=$HOME/.dotnet:$PATH && dotnet test tests/AcDream.App.Tests -c Release --filter "FullyQualifiedName~UiSpellDeck"` → all pass

**Steps:**

- [ ] **Step 1: Write the failing tests.** In `UiSpellDeckTests`, change `Make` to `private static Rig Make(bool clickCasts = false, Func<uint, string?>? itemName = null, HudText? text = null, DeckFonts? fonts = null)` and pass `fonts: fonts, itemName: itemName, text: text` to the `UiSpellDeck` constructor (named, in place of `itemName: itemName`). Then append:

```csharp
    private static HudText TextAt(int percent) => new(HudTextSettings.Default.WithGlobal(percent));

    [Theory]
    [InlineData(80)]
    [InlineData(150)]
    public void TheDeckKeepsItsSize_AtEveryTextSize(int percent)
    {
        Rig plain = Make();
        Rig r = Make(text: TextAt(percent), fonts: FakeDeckFonts.Make());
        Assert.Equal(plain.Deck.Width, r.Deck.Width);
        Assert.Equal(plain.Deck.Height, r.Deck.Height);
    }

    [Fact]
    public void AtLargeText_KeyLabelsAndBookmarksFitTheirBoxes()
    {
        var fits = new List<DeckFit>();
        Rig r = Make(text: TextAt(150), fonts: FakeDeckFonts.Make(fits));
        MarkupDrawProbe.Draw(r.Deck, r.Deck.Width, r.Deck.Height);
        Assert.NotEmpty(fits);
        FakeDeckFonts.AssertAllFit(fits);
    }

    [Fact]
    public void ARightClickOnTheFrame_EndsTheDeckMenuWithTextSize_ASlotsMenuDoesNot()
    {
        HudText text = TextAt(100);
        Rig r = Make(text: text);
        var root = new UiRoot { Width = 1200, Height = 800 };
        root.AddChild(r.Deck);
        HudTestInput.RightClick(root, r.Deck, 3f, r.Deck.Height - 3f);

        Assert.True(r.Deck.Menu.IsOpen);
        IReadOnlyList<string> items = r.Deck.Menu.ItemsSource();
        Assert.Equal(HudTextMenu.Labels(text.Settings, HudDeck.Spell), items.Skip(items.Count - HudTextMenu.Count).ToArray());
        Assert.Equal(UiMarkupContextMenu.Separator, items[items.Count - HudTextMenu.Count - 1]);
        r.Deck.Menu.Selected!(items.Count - 1);   // 150%
        Assert.Equal(150, text.Settings.PercentFor(HudDeck.Spell));
        r.Deck.Menu.Close();

        DeckRect slot = r.Deck.Geometry.Slot(0, 0);
        HudTestInput.RightClick(root, r.Deck, slot.X + slot.W / 2f, slot.Y + slot.H / 2f);
        Assert.DoesNotContain(HudTextMenu.Heading, r.Deck.Menu.ItemsSource());
    }
```

- [ ] **Step 2: Run** the Verify command → compile errors (`text`, `Menu`).

- [ ] **Step 3: Constructor, factor and menu** in `UiSpellDeck.cs`:
  - Field `private readonly HudText? _text;`. Constructor parameter `HudText? text = null` after `Func<uint, string?>? itemName = null`, and `_text = text;` after `_itemName = …`.
  - Members:

```csharp
    /// <summary>The deck's text factor: its own size or the HUD-wide one (1 without a text size).</summary>
    internal float TextFactor => _text?.FactorFor(HudDeck.Spell) ?? 1f;

    internal UiMarkupContextMenu Menu => _menu;
```

  - In `MenuItemsAt`, right after `_menuActions.Clear();`, add `HudTextMenu.StyleMenu(_menu, _fonts, 1f, TextFactor);`. After the `Attach to deck` / `Detach from deck` `Add(...)` and before `return _menuItems;`:

```csharp
        if (hit.Zone == SpellDeckZone.Frame && _text is { } text)
        {
            Separator();
            string[] sizes = HudTextMenu.Labels(text.Settings, HudDeck.Spell);
            for (int i = 0; i < sizes.Length; i++)
            {
                int choice = i;
                Add(sizes[i], HudTextMenu.Enabled(i), () => HudTextMenu.Choose(text, HudDeck.Spell, choice));
            }
        }
```

  - `OnDrawOverlay`: add `textFactor: TextFactor` to `HudTooltip.Draw`.

- [ ] **Step 4: Faces in boxes** in `UiSpellDeck.Draw.cs`. Replace `Face(bool small)` with:

```csharp
    /// <summary>The small face for text in a box <paramref name="boxHeight"/> tall, at the deck's text size.</summary>
    private DeckFace FaceIn(float boxHeight) =>
        DeckFace.In(_fonts, DeckFonts.SmallPixelHeight, BundledUiFontWeight.Regular, _geometry.Scale, TextFactor, boxHeight);
```

  The key label in the slot loop:

```csharp
                string key = KeyLabel(row, slot);
                if (key.Length > 0)
                {
                    DeckFace face = FaceIn(s.H - P(4));
                    string fitted = UiCommandDeck.FitCastingText(key, t => face.Measure(ctx, t), s.W - P(4));
                    face.Draw(ctx, fitted, s.X + P(2), s.Y + P(2), LabelInk);
                }
```

  `DrawTextCentered`: `DeckFace face = FaceIn(band.H);`.

- [ ] **Step 5: Runtime.** In `MountSpellDeck`, add `text: HudTextSize` after the `itemId => …` argument: `itemId => magic.Objects.Get(itemId)?.GetAppropriateName(), text: HudTextSize);`.

- [ ] **Step 6: Run** the Verify command → all pass.

- [ ] **Step 7: Commit.**

```bash
git add src/AcDream.App/UI/Hud/UiSpellDeck.cs src/AcDream.App/UI/Hud/UiSpellDeck.Draw.cs src/AcDream.App/UI/RetailUiRuntime.cs \
  tests/AcDream.App.Tests/UI/Hud/UiSpellDeckTests.cs
git commit -m "Spell Deck: HUD text size (labels fit and cap, Text size menu)

Claude-Session: https://claude.ai/code/session_017hCEwifLBBM9NGyiUdMFVs"
```

---

### Task 8: Target Deck text size

**Goal:** The Target Deck's plate keeps its size. The name, sub line, bars, rows, chips and stud letters fit and cap in their boxes. The ⋯ menu's lines grow. A right-click on the plate (not a stud or the close cross) lists the Text size block, and the tooltip follows the factor.

**Files:**
- Modify: `src/AcDream.App/UI/Hud/UiTargetDeck.cs`, `src/AcDream.App/UI/Hud/UiTargetDeck.Draw.cs`, `src/AcDream.App/UI/RetailUiRuntime.cs` (`MountTargetDeck`)
- Test: `tests/AcDream.App.Tests/UI/Hud/UiTargetDeckTextSizeTests.cs` (create); every existing `UiTargetDeck*Tests` must still pass

**Acceptance Criteria:**
- [ ] `UiTargetDeck` takes a trailing optional `HudText? text = null` after `fonts`. `TextFactor` is `FactorFor(HudDeck.Target)`, and `TextMenu` exposes the text menu.
- [ ] At 80% and 150% the plate is `TargetDeckGeometry.Width × Height`.
- [ ] Drawn at 150% (a monster with vitals and a plugin row, a player, a vendor), every fitted face fits its box. The name is fitted to the 16-point name band with fewer pixels than the uncapped 150% face.
- [ ] Chip and level-pill widths are measured with the deck's own chip face, so chips keep their size. Their text is fitted and capped to the chip height.
- [ ] At 150% with fonts, `MenuEntryRect(0)` is 6 points taller than at 100% (fake fonts).
- [ ] A right-click on the name through `UiRoot` opens `TextMenu` with the Target block, and choosing 80% sets the Target override. A right-click on a stud opens nothing.
- [ ] The tooltip gets `textFactor: TextFactor`, and the runtime passes `HudTextSize`.

**Verify:** `cd ~/code/OpenAC/OpenAC-hud-text-size && export PATH=$HOME/.dotnet:$PATH && dotnet test tests/AcDream.App.Tests -c Release --filter "FullyQualifiedName~UiTargetDeck|FullyQualifiedName~TargetDeckWiringTests"` → all pass

**Steps:**

- [ ] **Step 1: Write the failing tests.** `tests/AcDream.App.Tests/UI/Hud/UiTargetDeckTextSizeTests.cs`:

```csharp
using AcDream.App.UI;
using AcDream.App.UI.Hud;
using AcDream.Core.Items;
using AcDream.Core.Targeting;
using static AcDream.App.Tests.UI.Hud.TargetDeckModels;

namespace AcDream.App.Tests.UI.Hud;

public sealed class UiTargetDeckTextSizeTests
{
    private static HudText At(int percent) => new(HudTextSettings.Default.WithGlobal(percent));

    private static UiTargetDeck Make(TargetPlateModel model, HudText text, DeckFonts? fonts = null)
    {
        var deck = new UiTargetDeck(() => model, new NoActions(), icons: null, clock: () => 1.0, fonts, text);
        deck.Refresh();
        return deck;
    }

    public static IEnumerable<object[]> Plates()
    {
        yield return [Build(Monster(), pluginRows: [PluginRow])];
        yield return [Build(Player())];
        yield return [Build(new ClientObject { ObjectId = 4, Name = "Thorsten the Very Long Named Vendor", Type = ItemType.Creature,
            PublicWeenieBitfield = (uint)PublicWeenieFlags.Vendor })];
    }

    [Theory]
    [InlineData(80)]
    [InlineData(150)]
    public void ThePlateKeepsItsSize(int percent)
    {
        UiTargetDeck deck = Make(Build(Monster()), At(percent), FakeDeckFonts.Make());
        Assert.Equal(TargetDeckGeometry.Width, deck.Width);
        Assert.Equal(TargetDeckGeometry.Height, deck.Height);
    }

    [Theory]
    [MemberData(nameof(Plates))]
    public void AtLargeText_EveryFaceFitsItsBox(TargetPlateModel model)
    {
        var fits = new List<DeckFit>();
        UiTargetDeck deck = Make(model, At(150), FakeDeckFonts.Make(fits));
        MarkupDrawProbe.Draw(deck, TargetDeckGeometry.Width, TargetDeckGeometry.Height);
        Assert.NotEmpty(fits);
        FakeDeckFonts.AssertAllFit(fits);
        Assert.Contains(fits, f => MathF.Abs(f.BoxHeight - TargetDeckGeometry.Name.H) < 0.01f
            && f.Pixels < DeckFonts.PixelHeight(DeckFonts.TextPixelHeight, 1.5f));
    }

    [Fact]
    public void MenuLinesGrowWithLargeText()
    {
        TargetPlateModel model = Build(Monster());
        DeckFonts fonts = FakeDeckFonts.Make();
        UiTargetDeck normal = Make(model, new HudText(), fonts);
        UiTargetDeck large = Make(model, At(150), fonts);
        Assert.Equal(normal.MenuEntryRect(0).H + 6f, large.MenuEntryRect(0).H, 3);
    }

    [Fact]
    public void ARightClickOnTheName_ListsTextSize_OnAStud_Nothing()
    {
        var text = new HudText();
        UiTargetDeck deck = Make(Build(Monster()), text);
        var root = new UiRoot { Width = 1200, Height = 800 };
        root.AddChild(deck);
        DeckRect name = TargetDeckGeometry.Name;
        HudTestInput.RightClick(root, deck, name.X + 4f, name.Y + name.H / 2f);

        Assert.True(deck.TextMenu.IsOpen);
        Assert.Equal(HudTextMenu.Labels(text.Settings, HudDeck.Target), deck.TextMenu.ItemsSource());
        deck.TextMenu.Selected!(2);   // 80%
        Assert.Equal(80, text.Settings.PercentFor(HudDeck.Target));
        deck.TextMenu.Close();

        DeckRect stud = TargetDeckGeometry.Stud(0);
        HudTestInput.RightClick(root, deck, stud.X + stud.W / 2f, stud.Y + stud.H / 2f);
        Assert.False(deck.TextMenu.IsOpen);
    }
}
```

- [ ] **Step 2: Run** the Verify command → compile errors.

- [ ] **Step 3: Constructor, factor, menu and growing ⋯ lines** in `UiTargetDeck.cs`:
  - Fields: `private readonly HudText? _text;`, `private readonly UiMarkupContextMenu _textMenu = new() { ThemePalette = ChatDeckPalette.Menu };`, `private TargetZone _textMenuZone;`
  - Members:

```csharp
    /// <summary>The deck's text factor: its own size or the HUD-wide one (1 without a text size).</summary>
    internal float TextFactor => _text?.FactorFor(HudDeck.Target) ?? 1f;

    /// <summary>The plate's right-click menu: the Text size block, anywhere on the plate but a stud or the cross.</summary>
    internal UiMarkupContextMenu TextMenu => _textMenu;

    /// <summary>A ⋯ menu line: its normal height plus the small face's growth at the deck's text size.</summary>
    private float MenuLineHeight =>
        MenuLine + DeckFonts.RowGrow(_fonts, DeckFonts.SmallPixelHeight, BundledUiFontWeight.Regular, 1f, TextFactor);
```

  - Constructor: parameter `HudText? text = null` after `DeckFonts? fonts = null`, and at the end:

```csharp
        _text = text;
        _textMenu.Opening = (x, y) =>
        {
            _textMenuZone = _view is null ? TargetZone.None : ZoneAt(_view, x, y);
            HudTextMenu.StyleMenu(_textMenu, _fonts, 1f, TextFactor);
        };
        _textMenu.ItemsSource = () => _text is null || _view is null || _menuOpen || Pressable(_textMenuZone)
            ? []
            : HudTextMenu.Labels(_text.Settings, HudDeck.Target);
        _textMenu.ItemsEnabledSource = () => Enumerable.Range(0, HudTextMenu.Count).Select(HudTextMenu.Enabled).ToArray();
        _textMenu.Selected = index =>
        {
            if (_text is not null) HudTextMenu.Choose(_text, HudDeck.Target, index);
        };
        AddChild(_textMenu);
        ContextMenu = _textMenu;
```

  - Update the `ClipsChildren` summary: "The deck's only child is its text menu, a popup".
  - `OnHidden`: add `_textMenu.Close(restoreFocus: false);`.
  - `MenuHeight` becomes an instance method: `private float MenuHeight(int count) => MenuInset * 2 + count * MenuLineHeight;`. `MenuEntryRect`: `new(Width - MenuWidth, MenuTop(Entries().Count) + MenuInset + i * MenuLineHeight, MenuWidth, MenuLineHeight)`.
  - `OnDrawOverlay`: add `textFactor: TextFactor` to `HudTooltip.Draw`.

- [ ] **Step 4: Faces in boxes** in `UiTargetDeck.Draw.cs`. Replace `Small`, `NameFace` and `ChipFace` with:

```csharp
    private DeckFace SmallIn(DeckRect box) => In(DeckFonts.SmallPixelHeight, BundledUiFontWeight.Regular, box.H);
    /// <summary>The name: the 14 px SemiBold face at the deck's text size, capped to its band.</summary>
    private DeckFace NameIn(DeckRect box) => In(DeckFonts.TextPixelHeight, BundledUiFontWeight.SemiBold, box.H);
    private DeckFace HeaderIn(DeckRect box) => In(DeckFonts.SmallPixelHeight, BundledUiFontWeight.SemiBold, box.H);
    private DeckFace ChipIn(float height) => In(ChipPixels, BundledUiFontWeight.SemiBold, height);
    /// <summary>The chip face at the deck's own size: chip widths are measured with it, so chips keep their size.</summary>
    private DeckFace ChipBase => new(_fonts?.Face(ChipPixels, BundledUiFontWeight.SemiBold));

    private DeckFace In(float basePixels, BundledUiFontWeight weight, float boxHeight) =>
        DeckFace.In(_fonts, basePixels, weight, 1f, TextFactor, boxHeight);
```

  Call sites:
  - Sub line in `OnDraw`: `DeckFace sub = SmallIn(TargetDeckGeometry.Sub); DrawText(ctx, Fit(ctx, sub, v.SubLine, TargetDeckGeometry.Sub.W), TargetDeckGeometry.Sub.X, TargetDeckGeometry.Sub, LabelInk, sub);`
  - Portrait glyph: `DeckFace glyphFace = NameIn(p);` for the measure and draw.
  - `DrawIdentity`: `float w = ChipBase.Measure(ctx, text) + 8f;` for the level and each chip. The pill-less level draws with `SmallIn(n)`. The name: `DeckFace face = NameIn(n);`.
  - `DrawChip` becomes an instance method without the face parameter:

```csharp
    private void DrawChip(UiRenderContext ctx, float x, DeckRect band, float w, Vector4 ink, string text)
    {
        float y = band.Y + (band.H - ChipHeight) / 2f;
        ctx.FillRoundedRect(x, y, w, ChipHeight, 3f, ink);
        DeckFace face = ChipIn(ChipHeight);
        DrawText(ctx, Fit(ctx, face, text, w - 8f), x + 4f, new DeckRect(x, y, w, ChipHeight), FrameBottom, face);
    }
```

  - `DrawBar`: `DeckFace face = SmallIn(r); string fitted = Fit(ctx, face, text, r.W - 4f);` then draw `fitted` centred with `face`.
  - `DrawLabelValue`: `DeckFace face = SmallIn(r);`.
  - `DrawStuds` letter: `DeckFace face = SmallIn(r);` for the measure and draw.
  - `DrawMenu`: drop the `face`/`header` locals before the loop. Inside it, after `DeckRect r = MenuEntryRect(i);`, use `DeckFace face = e.Header ? HeaderIn(r) : SmallIn(r);` for both the `Fit` and the `DrawText`.

- [ ] **Step 5: Runtime.** In `MountTargetDeck`: `_deckFonts, HudTextSize);`.

- [ ] **Step 6: Run** the Verify command → all pass. `TheNameIsDrawnInTheBoldFace` still finds (14, SemiBold).

- [ ] **Step 7: Commit.**

```bash
git add src/AcDream.App/UI/Hud/UiTargetDeck.cs src/AcDream.App/UI/Hud/UiTargetDeck.Draw.cs src/AcDream.App/UI/RetailUiRuntime.cs \
  tests/AcDream.App.Tests/UI/Hud/UiTargetDeckTextSizeTests.cs
git commit -m "Target Deck: HUD text size (plate fits and caps, menu lines grow)

Claude-Session: https://claude.ai/code/session_017hCEwifLBBM9NGyiUdMFVs"
```

---

### Task 9: Chat Deck text size, and the full suite

**Goal:** The Chat Deck's tabs, the channel chip, the badges, the "+" and the "N new" marker are fixed boxes with fitted, capped text. Peek (strip preview) lines grow, so fewer show. The chip and tab/frame menus follow the factor. The transcript and input keep their own sizes. A right-click on the empty frame lists the Text size block. The whole test suite passes.

**Files:**
- Modify: `src/AcDream.App/UI/Hud/UiChatDeck.cs`, `src/AcDream.App/UI/Hud/UiChatDeck.Draw.cs`, `src/AcDream.App/UI/Hud/UiChatDeck.Input.cs`, `src/AcDream.App/UI/Hud/UiChatDeck.Strip.cs`, `src/AcDream.App/UI/RetailUiRuntime.cs` (`MountChatDeck`)
- Test: `tests/AcDream.App.Tests/UI/Hud/UiChatDeckTests.cs` (the `Harness` gains `HudText? text`), `tests/AcDream.App.Tests/UI/Hud/UiChatDeckTextSizeTests.cs` (create); every existing Chat Deck test must still pass

**Acceptance Criteria:**
- [ ] `ChatDeckBindings` gains a trailing optional `HudText? Text = null`, and `UiChatDeck.TextFactor` is `FactorFor(HudDeck.Chat)`.
- [ ] At 80% and 150% the window, the tab rectangles and the chip rectangle are the same as at 100% (geometry still measures with the 100% label face).
- [ ] At 150%, the transcript's face is still `appearance.FontSize` and the input's is still `appearance.InputFontSize`. The chip menu and tab menu use round(FontSize × 1.5) with `RowGrow` set.
- [ ] `PeekRow(appearance)` equals `PeekRowHeight(appearance)` at 100% and is taller at 150%. `PeekLinesFitting(MaxLines × PeekRowHeight, PeekRow)` is `MaxLines` at 100% and fewer at 150%. `DrawPeek` draws only that many of the newest lines.
- [ ] Drawn at 150% with an unread badge, every fitted face fits its box, and a tab-height box is among them.
- [ ] A right-click at the frame's edge through `UiRoot` opens the tab menu with the Chat block. Choosing 130% sets the Chat override, and `TextFactor` is 1.3. A right-click on a tab still lists the tab items. Without a `HudText`, the frame lists nothing (the existing `TabMenu_MarksRead` holds).
- [ ] The runtime passes `Text: HudTextSize`.
- [ ] The full Mac suite passes with 0 failures.

**Verify:** `cd ~/code/OpenAC/OpenAC-hud-text-size && export PATH=$HOME/.dotnet:$PATH && dotnet test tests/AcDream.App.Tests -c Release --filter "FullyQualifiedName~ChatDeck" && ~/code/OpenAC/test-baseline/run-mac-tests.sh ~/code/OpenAC/OpenAC-hud-text-size` → all pass, 0 failures

**Steps:**

- [ ] **Step 1: Write the failing tests.** In `UiChatDeckTests.Harness`, change the constructor to `public Harness(bool withModel = true, DeckFonts? fonts = null, HudText? text = null)` and add `Text: text` after `EditorOpen: () => EditorIsOpen` in its `ChatDeckBindings`. Then create `tests/AcDream.App.Tests/UI/Hud/UiChatDeckTextSizeTests.cs`:

```csharp
using AcDream.App.UI;
using AcDream.App.UI.Hud;
using AcDream.Core.Chat.Deck;
using Harness = AcDream.App.Tests.UI.Hud.UiChatDeckTests.Harness;

namespace AcDream.App.Tests.UI.Hud;

public sealed class UiChatDeckTextSizeTests
{
    private static HudText At(int percent) => new(HudTextSettings.Default.WithGlobal(percent));

    [Theory]
    [InlineData(80)]
    [InlineData(150)]
    public void TheWindowTabsAndChipKeepTheirSize(int percent)
    {
        DeckFonts fonts = FakeDeckFonts.Make();
        var plain = new Harness(fonts: fonts);
        var h = new Harness(fonts: fonts, text: At(percent));
        Assert.Equal(plain.Deck.Width, h.Deck.Width);
        Assert.Equal(plain.Deck.Height, h.Deck.Height);
        Assert.Equal(plain.Deck.Geometry.Tabs, h.Deck.Geometry.Tabs);
        Assert.Equal(plain.Deck.Geometry.Chip, h.Deck.Geometry.Chip);
    }

    [Fact]
    public void TheTranscriptAndInputKeepTheirOwnSizes_TheMenusFollowTheFactor()
    {
        var h = new Harness(fonts: FakeDeckFonts.Make(), text: At(150));
        ChatDeckAppearance a = h.Model!.Settings.Appearance;
        float menuLine = FakeDeckFonts.Line(DeckFonts.PixelHeight(a.FontSize, 1.5f));
        Assert.Equal(FakeDeckFonts.Line(a.FontSize), h.Deck.Transcript.DatFont!.LineHeight);
        Assert.Equal(FakeDeckFonts.Line(a.InputFontSize), h.Deck.Input.DatFont!.LineHeight);
        Assert.Equal(menuLine, h.Deck.ChipMenu.DatFont!.LineHeight);
        Assert.Equal(menuLine, h.Deck.TabMenu.DatFont!.LineHeight);
        Assert.Equal(menuLine - FakeDeckFonts.Line(a.FontSize), h.Deck.ChipMenu.RowGrow, 3);
    }

    [Fact]
    public void PeekLinesGrow_SoFewerShow()
    {
        var a = new ChatDeckAppearance();
        var normal = new Harness(fonts: FakeDeckFonts.Make());
        var large = new Harness(fonts: FakeDeckFonts.Make(), text: At(150));
        float height = ChatDeckPeekFeed.MaxLines * UiChatDeck.PeekRowHeight(a);

        Assert.Equal(UiChatDeck.PeekRowHeight(a), normal.Deck.PeekRow(a));
        Assert.True(large.Deck.PeekRow(a) > normal.Deck.PeekRow(a));
        Assert.Equal(ChatDeckPeekFeed.MaxLines, UiChatDeck.PeekLinesFitting(height, normal.Deck.PeekRow(a)));
        Assert.True(UiChatDeck.PeekLinesFitting(height, large.Deck.PeekRow(a)) < ChatDeckPeekFeed.MaxLines);
    }

    [Fact]
    public void AtLargeText_TabsChipAndBadgesFitTheirBoxes()
    {
        var fits = new List<DeckFit>();
        var h = new Harness(fonts: FakeDeckFonts.Make(fits), text: At(150));
        h.Say("one");   // an unread badge on the tabs that show it
        h.Deck.Refresh();
        MarkupDrawProbe.Draw(h.Deck, h.Deck.Width, h.Deck.Height);
        Assert.NotEmpty(fits);
        FakeDeckFonts.AssertAllFit(fits);
        Assert.Contains(fits, f => MathF.Abs(f.BoxHeight - ChatDeckGeometry.TabHeight) < 0.01f);
    }

    [Fact]
    public void ARightClickOnTheFrame_ListsTextSize_OnATab_TheTabMenu()
    {
        HudText text = At(100);
        var h = new Harness(text: text);
        var root = new UiRoot { Width = 1200, Height = 800 };
        root.AddChild(h.Deck);
        h.Deck.Refresh();
        HudTestInput.RightClick(root, h.Deck, 3f, 3f);

        Assert.True(h.Deck.TabMenu.IsOpen);
        Assert.Equal(HudTextMenu.Labels(text.Settings, HudDeck.Chat), h.Deck.TabMenu.ItemsSource());
        h.Deck.TabMenu.Selected!(7);   // 130%
        Assert.Equal(130, text.Settings.PercentFor(HudDeck.Chat));
        Assert.Equal(1.3f, h.Deck.TextFactor, 3);
        h.Deck.TabMenu.Close();

        DeckRect tab = h.Deck.Geometry.Tabs[0];
        HudTestInput.RightClick(root, h.Deck, tab.X + 2f, tab.Y + 2f);
        Assert.Equal(["Rename", "Edit tab…", "-", "Mark as read", "Mark all read", "-", "Delete"], h.Deck.TabMenu.ItemsSource());
    }
}
```

- [ ] **Step 2: Run** the Chat Deck part of the Verify command → compile errors.

- [ ] **Step 3: Bindings, factor, menus** in `UiChatDeck.cs`:
  - `ChatDeckBindings`: append `HudText? Text = null` after `Func<bool>? EditorOpen = null`.
  - Members and field:

```csharp
    /// <summary>The deck's text factor: its own size or the HUD-wide one (1 without a text size).</summary>
    internal float TextFactor => _b.Text?.FactorFor(HudDeck.Chat) ?? 1f;

    /// <summary>Set as the tab menu opens: the press was on the empty frame, so the menu lists the Text size block.</summary>
    private bool _menuTextSize;

    /// <summary>The chip and tab menus' face: the chat font size at the deck's text size (the transcript keeps its own).</summary>
    private UiDatFont? MenuFont(ChatDeckAppearance a) =>
        _fonts?.Face(DeckFonts.PixelHeight(a.FontSize, TextFactor), BundledUiFontWeight.Regular);

    private float MenuGrow(ChatDeckAppearance a) =>
        DeckFonts.RowGrow(_fonts, a.FontSize, BundledUiFontWeight.Regular, 1f, TextFactor);
```

  - Replace the four `_tabMenu` lines in the constructor with:

```csharp
        _tabMenu.Opening = (x, y) =>
        {
            ChatDeckHit hit = _geometry.HitAt(x, y);
            _menuTab = hit.Zone == ChatDeckZone.Tab ? hit.Tab : -1;
            _menuTextSize = hit.Zone == ChatDeckZone.Frame && _b.Text is not null;
        };
        _tabMenu.ItemsSource = () => _menuTab >= 0
            ? TabMenuItems
            : _menuTextSize ? HudTextMenu.Labels(_b.Text!.Settings, HudDeck.Chat) : [];
        _tabMenu.ItemsEnabledSource = () => _menuTextSize
            ? Enumerable.Range(0, HudTextMenu.Count).Select(HudTextMenu.Enabled).ToArray()
            : TabMenuItems
                .Select(item => item != "Delete" || (_model is { } m && _menuTab > 0 && _menuTab < m.Settings.Tabs.Count))
                .ToArray();
        _tabMenu.Selected = index =>
        {
            if (_menuTextSize && _b.Text is { } text) HudTextMenu.Choose(text, HudDeck.Chat, index);
            else ChooseTabMenuItem(index);
        };
```

  - In `Refresh`, replace `_tabMenu.DatFont = Transcript.DatFont;` with `_tabMenu.DatFont = MenuFont(appearance); _tabMenu.RowGrow = MenuGrow(appearance);`.
  - `UiChatDeck.Input.cs` `LayoutInputRow`: replace `_chipMenu.DatFont = …` with `_chipMenu.DatFont = MenuFont(appearance); _chipMenu.RowGrow = MenuGrow(appearance);`.

- [ ] **Step 4: Labels in boxes** in `UiChatDeck.Draw.cs`. Replace `LabelFace` and `LabelBold` with:

```csharp
    /// <summary>The label face for text in <paramref name="box"/>: the deck's text size, dropped to the largest that fits.</summary>
    private DeckFace LabelIn(DeckRect box) =>
        DeckFace.In(_fonts, LabelPixels, BundledUiFontWeight.Regular, 1f, TextFactor, box.H);

    private DeckFace LabelBoldIn(DeckRect box) =>
        DeckFace.In(_fonts, LabelPixels, BundledUiFontWeight.SemiBold, 1f, TextFactor, box.H);

    /// <summary>The label face at the deck's own size: badge widths are measured with it, so badges keep their size.</summary>
    private DeckFace LabelBase => new(_fonts?.Face(LabelPixels, BundledUiFontWeight.Regular));
```

  Call sites:
  - "+": `DeckFace plus = LabelBoldIn(add); float w = plus.Measure(ctx, "+"); DrawLabel(ctx, plus, "+", add.X + (add.W - w) / 2f, add, ChatDeckPalette.LabelInk);`
  - `DrawTabs`: `float badgeW = ChatDeckGeometry.BadgeWidth(badge, s => LabelBase.Measure(ctx, s));`. Name: `DeckFace nameFace = LabelIn(r); string name = Fit(ctx, nameFace, tabs[i].Name, nameRoom); DrawLabel(ctx, nameFace, name, …);`. Badge: `DeckFace badgeFace = LabelBoldIn(pill); DrawLabel(ctx, badgeFace, Fit(ctx, badgeFace, badge, pill.W - 2 * ChatDeckGeometry.BadgePadX), pill.X + ChatDeckGeometry.BadgePadX, pill, …);`
  - `DrawNewLines`: `var band = new DeckRect(0f, 0f, w, h); DeckFace face = LabelIn(band); DrawLabel(ctx, face, Fit(ctx, face, $"{NewLineCount} new", w - 26f), 22f, band, ChatDeckPalette.TextInk);`
  - `UiChatDeck.Input.cs` `DrawInputRow`: `DeckFace chipFace = LabelBoldIn(chip); string chipText = Fit(ctx, chipFace, ChipText, chip.W - ChatDeckGeometry.TabPadX - ChatDeckGeometry.ChipArrow - 4f); DrawLabel(ctx, chipFace, chipText, chip.X + ChatDeckGeometry.TabPadX, chip, …);`
  - `MeasureLabel` (the geometry's measure) is unchanged: the tabs and chip are fixed boxes.

- [ ] **Step 5: Strip badge and peek lines** in `UiChatDeck.Strip.cs`:
  - `DrawStripBadge`: measure the pill with `var baseFace = new DeckFace(_fonts?.Face(StripBadgePixels, BundledUiFontWeight.SemiBold));` (`textW = baseFace.Measure(ctx, badge)`), then draw with the fitted face:

```csharp
        DeckFace face = DeckFace.In(_fonts, StripBadgePixels, BundledUiFontWeight.SemiBold, 1f, TextFactor, StripBadgeHeight);
        string fitted = Fit(ctx, face, badge, pill.W - 2f);
        DrawLabel(ctx, face, fitted, pill.X + (pill.W - face.Measure(ctx, fitted)) / 2f, pill,
            alert ? ChatDeckPalette.TextInk : ChatDeckPalette.BadgeInk);
```

  - Add after `PeekRowHeight`:

```csharp
    /// <summary>A peek line at the deck's text size: its normal height plus the line's growth.</summary>
    internal float PeekRow(ChatDeckAppearance appearance) =>
        PeekRowHeight(appearance) + DeckFonts.RowGrow(_fonts, appearance.FontSize, BundledUiFontWeight.Regular, 1f, TextFactor);

    /// <summary>How many peek lines a <paramref name="height"/> tall peek shows at rows <paramref name="row"/> tall.</summary>
    internal static int PeekLinesFitting(float height, float row) =>
        row <= 0f ? 0 : (int)MathF.Floor((height + 0.01f) / row);
```

  - `PlacePeek` is unchanged (the peek keeps its height). In `DrawPeek`:

```csharp
        var face = new DeckFace(_fonts?.Face(DeckFonts.PixelHeight(appearance.FontSize, TextFactor), BundledUiFontWeight.Regular));
        float row = PeekRow(appearance);
        int shown = Math.Min(lines.Count, PeekLinesFitting(height, row));
        float y = height;
        for (int i = lines.Count - 1; i >= lines.Count - shown; i--)   // newest at the bottom
```

  The loop body stays the same.

- [ ] **Step 6: Runtime.** In `MountChatDeck`'s `ChatDeckBindings`, after `EditorOpen: () => _chatDeckEditor?.IsOpen == true` add `, Text: HudTextSize`.

- [ ] **Step 7: Run** the whole Verify command: first the Chat Deck tests, then the full suite → all pass, 0 failures. Fix anything the full suite finds before committing.

- [ ] **Step 8: Commit.**

```bash
git add src/AcDream.App/UI/Hud/UiChatDeck.cs src/AcDream.App/UI/Hud/UiChatDeck.Draw.cs src/AcDream.App/UI/Hud/UiChatDeck.Input.cs \
  src/AcDream.App/UI/Hud/UiChatDeck.Strip.cs src/AcDream.App/UI/RetailUiRuntime.cs \
  tests/AcDream.App.Tests/UI/Hud/UiChatDeckTests.cs tests/AcDream.App.Tests/UI/Hud/UiChatDeckTextSizeTests.cs
git commit -m "Chat Deck: HUD text size (labels fit and cap, peek lines grow, menus follow)

Claude-Session: https://claude.ai/code/session_017hCEwifLBBM9NGyiUdMFVs"
```

---

### Task 10: Live check (user)

**Goal:** The user confirms HUD text size works in the real client against the local ACE server.

> **USER-ORDERED GATE — NON-SKIPPABLE.** This task was requested by the user in the current conversation. It MUST NOT be closed by walking around it, by declaring it "verified inline", or by substituting a cheaper check. Close only after every item in `acceptanceCriteria` has been re-validated independently, with output captured.

**Files:** none (fixes found here get their own commits)

**Acceptance Criteria:**
- [ ] Ask the user before launching. Use the local ACE server (10.10.20.20 through `~/OpenAC-dev/dev-client.env`), never Dreamweave. Admin client: `cd ~/code/OpenAC/OpenAC-Plugins-Aethrix && OPENAC_ROOT=~/code/OpenAC/OpenAC-hud-text-size OPENAC_DEV_ROOT=~/OpenAC-dev-admin DYLD_FALLBACK_LIBRARY_PATH=/opt/homebrew/lib nohup tools/run-dev-client.sh --admin --no-aethrix > ~/OpenAC-dev/hud-text-size-live.log 2>&1 &`
- [ ] Config page: the "HUD text size" row sits under the Modern HUD row (80%–150%). Moving it to 130% makes the text in every deck larger at once, and no deck window changes size.
- [ ] One deck's override: a right-click on the Gear Deck's title shows the Text size entries. Choosing 150% grows only the Gear Deck's text: its list rows get taller and fewer show, while its window, studs, chips and side cells keep their size. The other decks stay at the HUD size.
- [ ] "Same as HUD": the Gear Deck's menu shows "Same as HUD (130%)", and choosing it puts the deck back on the HUD size.
- [ ] A small Command Deck (resized to its smallest scale) beside a Gear Deck with large text: the Command Deck's vital numbers, labels and CAST stay inside their bars and button, cut with "…" where needed, and the Gear Deck's text is large and readable.
- [ ] The sizes survive a relog and are the same on another character (per client).
- [ ] The user says it passes.

**Verify:** the user's confirmation in the conversation, plus the client log (`~/OpenAC-dev/hud-text-size-live.log`) showing the `[UI] … Deck mounted (Modern HUD).` lines and no `[UI] HUD text size not saved` line.

---

## Self-review notes

- **Spec coverage:**
  - Goal, Decisions (scope, range) → T1 (model), T4 (global row), T5–T9 (decks)
  - Stored per client with the Modern HUD switch → T1 (`hudText` section beside `misc`), T4 (`HudTextSize` from `Persistence.Store`)
  - Global control (Config row under Modern HUD) → T4
  - Per-deck control (right-click frame or title → Text size) → T3 (block), T5–T9 (each deck's menu)
  - When text no longer fits (rows grow, boxes fit then cap) → T2 (`RowGrow`, `FitFace`), T5–T9
  - Chat transcript and input keep their sizes → T9
  - Model: `HudTextSettings`, `Global`, `Overrides`, `FactorFor`, clamp and snap on load and set, Same as HUD removes the override, `HudDeck` enum, missing or bad file reads as defaults, change event → T1
  - Drawing:
    - scale × factor faces, rounded and baked once → T2
    - rows that grow (list, suggestion, popup menu, chat strip, tooltip lines) → T5 (list, suggestion), T3 + T5–T9 (menus), T9 (peek), T3 (tooltip lines grow with the face)
    - fixed boxes → T5–T9
    - menu and tooltip take the factor, padding follows the scale → T3
    - a factor change drops caches and rebuilds → T5 (Gear listens); the others re-measure every frame
  - Per deck: Command → T6, Spell → T7, Target → T8, Chat → T9, Gear → T5
  - Testing:
    - settings → T1
    - fonts → T2
    - each deck at 80/100/150: window unchanged, rows grow, no face taller than its box → T5–T9
    - one right-click test per deck through the real root input path → T5–T9
    - Config row test → T4
    - live check → T10
- **Judgement calls** (also listed under Planning decisions):
  - snapped slider for the Config "stepper"
  - flat menu block (no submenus)
  - the cap never goes below the 100% face
  - only the Gear Deck subscribes to `Changed`
  - Chat menus at chat font size × factor; the line menu and the rename field are unchanged
  - peek lines are the "strip preview lines"
- **Names across tasks:**
  - T1: `HudDeck`, `HudTextSettings` (`MinPercent`, `MaxPercent`, `StepPercent`, `DefaultPercent`, `Steps`, `Default`, `Global`, `Overrides`, `Snap`, `HasOverride`, `PercentFor`, `FactorFor`, `WithGlobal`, `WithOverride`, `FromJson`, `ToJson`); `HudText` (`Settings`, `Changed`, `FactorFor`, `Set`, `SetGlobal`, `SetOverride`, `Load`); `SettingsStore.LoadHudText` / `SaveHudText`
  - T2: `DeckFonts.At(scale, textFactor)`, `FitFace`, `Fitted`, `DeckFit`, `LineHeightAt`, `RowGrow`; `DeckFace.For(fonts, scale, textFactor, small)`, `DeckFace.In`; test `FakeDeckFonts` (`Make`, `Font`, `Line`, `AssertAllFit`)
  - T3: `UiMarkupContextMenu.RowGrow`; `HudTooltip.Draw(…, textFactor)`; `HudTextMenu` (`Heading`, `Mark`, `Count`, `Labels`, `Enabled`, `Choose`, `StyleMenu`); test `HudTestInput.RightClick`
  - T4: `ConfigOptionsPageController.HudTextBindings`, `Bindings.HudText`; `RetailUiRuntime.HudTextSize`
  - T5: `GearDeckBindings.Text`, `UiGearDeck.TextFactor`, `GearDeckGeometry.TextGrow` / `ListRowHeight` / `SuggestRowHeight`, `MenuKind.TextSize`
  - T6: `UiCommandDeck.TextFactor` / `TextMenu`
  - T7: `UiSpellDeck.TextFactor` / `Menu`
  - T8: `UiTargetDeck.TextFactor` / `TextMenu`
  - T9: `ChatDeckBindings.Text`, `UiChatDeck.TextFactor` / `PeekRow` / `PeekLinesFitting`
- Every new constructor parameter or bindings member is trailing and optional, so existing tests and callers compile at every step. The runtime passes `HudTextSize` in the same task that adds each deck's parameter.
