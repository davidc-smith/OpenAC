# Chat Deck Appearance Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers-extended-cc:subagent-driven-development (recommended) or superpowers-extended-cc:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Plan 4 of 5 for the Chat Deck. Add the Settings popup (from a new ⚙ stud) and draw every appearance setting the deck already stores but ignores today: line spacing, wrap indent, channel prefix, fade when idle and beep on alert. Add optional extra keys for cycling tabs.

**Architecture:** The settings live in `ChatDeckSettings` (Core). Two model methods, `SetAppearance` and `SetKeys`, raise `SettingsChanged`, and `ChatDeckSession` already saves on that event. Drawing changes stay in the App: `UiText` gains a line-spacing multiplier and a per-line indent, both defaulting to today's behaviour. `ChatDeckTranscript` strips the channel prefix and indents wrapped lines. Two small pure classes handle the idle fade and the alert beep. The popup is a markup window built with `MarkupDocument.Build` from a binding object. It is hosted like the dock's "Plugin appearance" window, reusing `PluginAppearanceWindow`, and opened by the new stud.

**Tech Stack:** C# / .NET 10, xUnit, the OpenAC retained UI (`UiElement`, `UiText`, `UiField`), plugin markup (`MarkupDocument`, `theme="plugin"`), `UiSoundController` for the beep.

**Spec:** `docs/superpowers/specs/2026-10-26-chat-deck-design.md` (branch `docs/chat-deck-spec`), sections "Settings and persistence" (appearance list) and "Window". Plans 1–3 are merged on main (`8d9c6f5d`).

## Global Constraints

- Fork-only, client-only. **No plugin contract change** (`ForkContractVersion` stays `0.1.30-fork.1`).
- **The retail chat must not change.** `UiText` is shared with the retail chat: every new `UiText` member defaults to the current behaviour (`LineSpacing = 1`, `Line.Indent = 0`), and the existing `UiText*` tests keep passing unchanged.
- The deck draws with the bundled sans fonts (`DeckFonts`), never the monospace debug font.
- Appearance ranges are the ones `ChatDeckSettingsNormalizer` already enforces: font sizes 10–20, line spacing 1.0–1.6, opacity 0–1, fade after 1–600 s.
- Settings JSON stays `version: 1`, and unknown fields are kept (`Serialize` writes into a clone of the previous object). The new `keys` object follows the same rule.
- Worktree: `~/code/OpenAC/OpenAC-chat-deck-appearance` on a new branch `feat/chat-deck-appearance` from `main` (`8d9c6f5d` or later). Do not work in the main checkout.
- .NET SDK: `~/.dotnet/dotnet` (put `~/.dotnet` first on `PATH`). Run each task's tests with the filtered `dotnet test` command given. For the full suite, use only `~/code/OpenAC/test-baseline/run-mac-tests.sh <worktree>`.
- Commit messages end with the session attribution line the controller supplies.

**User decisions (already made):**
- "We removed the pin so it's no longer there and should not come back." The window has no pin stud; only the strip's ⇥ pins back.
- The built-in tab keys stay as they are: Ctrl+Tab / Ctrl+Shift+Tab (Windows/Linux) and Option+Cmd+Left/Right (macOS keeps Ctrl+Tab and Ctrl+Left/Right for itself).
- The user asked whether the tab keys could be configured (2026-10-27). This plan answers it with optional extra keys (Task 6).

**Design choices made in this plan** (stated so a reviewer can check them against the spec):
- *Channel prefix* means the `[Channel name] ` that the retail wording puts in front of a named-channel line (`ChatLineWording.ChannelLine`: `[General] Bob says, "…"`). Turned off, the deck drops it; turned on (the default), the line reads exactly as it does today. Lines worded as sentences (fellowship, allegiance legacy channels) carry no prefix to drop and are unchanged either way.
- *Wrap indent*: when on, every line wraps at `width − 16 px`, and continuation lines are drawn 16 px in. The first line loses 16 px of width, which keeps the wrap a single pass.
- *Fade when idle* (pinned only, as the spec says): only the frame background fades, to transparent over 0.4 s, after `FadeAfterSeconds` without activity. Activity is any of: the pointer over the window, the input or search field focused, a menu open, a drag-select, or a line arriving in the selected tab. The text never fades.
- *Beep on alert*: an alert line (`ChatDeckLine.Alert`) plays the retail UI sound `SoundId.UI_TransientMessage` at most once every 2 s, only while the deck is shown. The live check confirms the sound is right (the alternative is `UI_Bell`).
- *Extra tab keys*: one optional hotkey per direction ("Next tab", "Previous tab"), stored as hotkey strings (`Ctrl+Alt+Shift+Key`, the format `MarkupHotkeyText` already reads and writes). They work while the deck's input has focus, in addition to the built-in keys. A chord needs at least one modifier, so plain typing can never trigger one.

---

## File Structure

| File | Change | Responsibility |
|---|---|---|
| `src/AcDream.Core/Chat/Deck/ChatDeckSettings.cs` | modify | `ChatDeckKeys` record, `ChatDeckSettings.Keys`, normalize keys |
| `src/AcDream.Core/Chat/Deck/ChatDeckSettingsJson.cs` | modify | read/write `keys` |
| `src/AcDream.Core/Chat/Deck/ChatDeckModel.cs` | modify | `SetAppearance`, `SetKeys` |
| `src/AcDream.App/UI/UiText.cs` | modify | `LineSpacing`, `Line.Indent` |
| `src/AcDream.App/UI/Hud/ChatDeckPrefix.cs` | create | drop a line's `[Channel] ` prefix |
| `src/AcDream.App/UI/Hud/ChatDeckTranscript.cs` | modify | prefix + indent in the cached layout |
| `src/AcDream.App/UI/Hud/ChatDeckGeometry.cs` | modify | `WrapIndent` const, `SettingsStud`, zone |
| `src/AcDream.App/UI/Hud/UiChatDeck.cs` | modify | style args, line spacing, bindings record, press |
| `src/AcDream.App/UI/Hud/UiChatDeck.Strip.cs` | modify | peek uses the prefix setting |
| `src/AcDream.App/UI/Hud/ChatDeckIdleFade.cs` | create | idle fade factor |
| `src/AcDream.App/UI/Hud/ChatDeckAlertBeep.cs` | create | throttled alert sound |
| `src/AcDream.App/UI/Hud/UiChatDeck.Drawer.cs` | modify | tick fade + beep, wake on lines |
| `src/AcDream.App/UI/Hud/UiChatDeck.Draw.cs` | modify | faded frame, ⚙ stud |
| `src/AcDream.App/UI/UiField.cs` | modify | `OnChordKey` hook |
| `src/AcDream.App/UI/Hud/UiChatDeck.Input.cs` | modify | extra tab keys |
| `assets/hud/settings.svg` | create | gear icon |
| `src/AcDream.App/UI/Hud/EmbeddedSvgSpriteCache.cs` | modify | `HudIcons.Settings` |
| `src/AcDream.App/UI/Hud/ChatDeckChannelColours.cs` | create | default colour per channel |
| `src/AcDream.App/UI/Hud/ChatDeckSettingsBinding.cs` | create | popup binding + markup |
| `src/AcDream.App/UI/RetailUiRuntime.cs` | modify | `PlayUiSound` binding, popup host, deck bindings |
| `src/AcDream.App/Composition/InteractionRetainedUiComposition.cs` | modify | pass UI sounds |
| `src/AcDream.App/Rendering/GameWindow.cs` | modify | hand `UiSounds` to the UI dependencies |
| tests (see each task) | create/modify | |

---

### Task 1: Settings — extra tab keys, SetAppearance, SetKeys

**Goal:** Store the two extra tab keys with the settings, and give the model `SetAppearance` / `SetKeys`, which normalize, replace and raise `SettingsChanged` so the session saves.

**Files:**
- Modify: `src/AcDream.Core/Chat/Deck/ChatDeckSettings.cs`
- Modify: `src/AcDream.Core/Chat/Deck/ChatDeckSettingsJson.cs`
- Modify: `src/AcDream.Core/Chat/Deck/ChatDeckModel.cs`
- Test: `tests/AcDream.Core.Tests/Chat/Deck/ChatDeckSettingsJsonTests.cs`, `tests/AcDream.Core.Tests/Chat/Deck/ChatDeckModelTests.cs`

**Acceptance Criteria:**
- [ ] `ChatDeckSettings.Keys` defaults to empty `NextTab` / `PreviousTab`
- [ ] JSON round-trips `keys`; an object without `keys` loads empty keys; unknown fields inside `keys` survive a save
- [ ] Keys longer than 40 characters are trimmed to empty on normalize (unreadable, so unbound)
- [ ] `SetAppearance` clamps through the normalizer (e.g. font size 99 → 20) and raises `SettingsChanged` once
- [ ] `SetKeys` stores the keys and raises `SettingsChanged`; setting the same keys again raises nothing

**Verify:** `~/.dotnet/dotnet test tests/AcDream.Core.Tests --filter "FullyQualifiedName~ChatDeck"` → all pass

**Steps:**

- [ ] **Step 1: Write the failing tests.** Add these tests to `ChatDeckSettingsJsonTests.cs` (match the existing usings; `ChatDeckSettingsJson.Load` returns a result with `.Settings`. Check the current name with `grep -n "public static" src/AcDream.Core/Chat/Deck/ChatDeckSettingsJson.cs` and use it):

```csharp
[Fact]
public void Keys_RoundTrip_AndKeepUnknownFields()
{
    ChatDeckSettings s = ChatDeckDefaults.Create() with { Keys = new ChatDeckKeys { NextTab = "Ctrl+Alt+N", PreviousTab = "Ctrl+Alt+P" } };
    string json = ChatDeckSettingsJson.Serialize(s, """{"keys":{"future":1}}""");
    Assert.Contains("\"future\":1", json);
    ChatDeckSettings back = ChatDeckSettingsJson.Load(json).Settings;
    Assert.Equal("Ctrl+Alt+N", back.Keys.NextTab);
    Assert.Equal("Ctrl+Alt+P", back.Keys.PreviousTab);
}

[Fact]
public void Keys_MissingObject_LoadsEmpty()
{
    ChatDeckSettings back = ChatDeckSettingsJson.Load("""{"version":1}""").Settings;
    Assert.Equal("", back.Keys.NextTab);
    Assert.Equal("", back.Keys.PreviousTab);
}

[Fact]
public void Keys_TooLong_NormalizeToUnbound()
{
    var s = ChatDeckDefaults.Create() with { Keys = new ChatDeckKeys { NextTab = new string('A', 41) } };
    Assert.Equal("", ChatDeckSettingsNormalizer.Normalize(s).Keys.NextTab);
}
```

Add to `ChatDeckModelTests.cs` (it already builds models as `new ChatDeckModel(new ChatLog(), ChatDeckDefaults.Create(), "Davian")`. Follow its helper if it has one):

```csharp
[Fact]
public void SetAppearance_Normalizes_AndRaisesOnce()
{
    var model = new ChatDeckModel(new ChatLog(), ChatDeckDefaults.Create(), "Davian");
    int raised = 0;
    model.SettingsChanged += _ => raised++;
    model.SetAppearance(model.Settings.Appearance with { FontSize = 99 });
    Assert.Equal(20, model.Settings.Appearance.FontSize);
    Assert.Equal(1, raised);
}

[Fact]
public void SetKeys_Stores_AndIgnoresNoChange()
{
    var model = new ChatDeckModel(new ChatLog(), ChatDeckDefaults.Create(), "Davian");
    int raised = 0;
    model.SettingsChanged += _ => raised++;
    var keys = new ChatDeckKeys { NextTab = "Ctrl+Alt+N" };
    model.SetKeys(keys);
    model.SetKeys(keys with { });
    Assert.Equal("Ctrl+Alt+N", model.Settings.Keys.NextTab);
    Assert.Equal(1, raised);
}
```

- [ ] **Step 2: Run them and see them fail** (compile errors: `ChatDeckKeys`, `Keys`, `SetAppearance`, `SetKeys` do not exist).

Run: `~/.dotnet/dotnet test tests/AcDream.Core.Tests --filter "FullyQualifiedName~ChatDeck"`

- [ ] **Step 3: Implement.** In `ChatDeckSettings.cs`, after `ChatDeckLayoutState`:

```csharp
/// <summary>
/// Extra keys that cycle tabs while the deck's input has focus, as hotkey strings
/// ("Ctrl+Alt+N"); empty is unbound. The built-in keys always work as well.
/// </summary>
public sealed record ChatDeckKeys
{
    public const int MaxLength = 40;
    public string NextTab { get; init; } = "";
    public string PreviousTab { get; init; } = "";
}
```

In `ChatDeckSettings` add `public ChatDeckKeys Keys { get; init; } = new();`. In `ChatDeckSettingsNormalizer.Normalize`, before the `return`:

```csharp
static string Key(string text) => text.Trim() is { Length: <= ChatDeckKeys.MaxLength } t ? t : "";
var keys = new ChatDeckKeys { NextTab = Key(settings.Keys.NextTab), PreviousTab = Key(settings.Keys.PreviousTab) };
```

and return `settings with { Tabs = tabs, Rules = rules, Appearance = appearance, Layout = layout, Keys = keys }`.

In `ChatDeckSettingsJson.cs`, in `Load` add `Keys = root["keys"] is JsonObject k ? ReadKeys(k) : defaults.Keys,`. In `Serialize`, add `root["keys"] = WriteKeys(settings.Keys, Clone(previous["keys"] as JsonObject));`, and add these beside the layout readers and writers:

```csharp
private static ChatDeckKeys ReadKeys(JsonObject o) => new()
{
    NextTab = Str(o, "nextTab", ""),
    PreviousTab = Str(o, "previousTab", ""),
};

private static JsonObject WriteKeys(ChatDeckKeys k, JsonObject o)
{
    o["nextTab"] = k.NextTab;
    o["previousTab"] = k.PreviousTab;
    return o;
}
```

In `ChatDeckModel.cs`, after `SetLayout`:

```csharp
/// <summary>New appearance settings, kept in range; saved with the settings.</summary>
public void SetAppearance(ChatDeckAppearance appearance)
{
    ChatDeckAppearance next = ChatDeckSettingsNormalizer.Normalize(Settings with { Appearance = appearance }).Appearance;
    Settings = Settings with { Appearance = next };
    SettingsChanged?.Invoke(Settings);
}

/// <summary>The extra tab keys; saved with the settings.</summary>
public void SetKeys(ChatDeckKeys keys)
{
    ChatDeckKeys next = ChatDeckSettingsNormalizer.Normalize(Settings with { Keys = keys }).Keys;
    if (next == Settings.Keys) return;
    Settings = Settings with { Keys = next };
    SettingsChanged?.Invoke(Settings);
}
```

- [ ] **Step 4: Run the tests and see them pass.** Same command → PASS.

- [ ] **Step 5: Commit**

```bash
git add src/AcDream.Core/Chat/Deck tests/AcDream.Core.Tests/Chat/Deck
git commit -m "Chat Deck: extra tab keys in the settings; model SetAppearance and SetKeys"
```

---

### Task 2: UiText line spacing and per-line indent

**Goal:** `UiText` can space lines out by a factor and indent a line, with defaults that leave every existing use unchanged.

**Files:**
- Modify: `src/AcDream.App/UI/UiText.cs` (the `Line` record at line ~32, multi-line draw ~455–560, `HorizontalOffset` ~643, `HitChar` ~973)
- Test: `tests/AcDream.App.Tests/UI/UiTextSpacingTests.cs` (create)

**Acceptance Criteria:**
- [ ] `UiText.LineSpacing` (default 1) multiplies the line pitch for drawing, scroll extents and hit-testing; values below 1 act as 1
- [ ] Glyphs sit centred in the taller pitch: line *i* draws at `baseY + i·pitch + (pitch − fontLineHeight)/2`
- [ ] `UiText.Line(string Text, Vector4 Color, float Indent = 0f)`: a left-aligned line's text, selection and hit-test start `Indent` px further right; centred and right-aligned text ignore it
- [ ] Every existing `UiText*` test passes unchanged

**Verify:** `~/.dotnet/dotnet test tests/AcDream.App.Tests --filter "FullyQualifiedName~UiText"` → all pass

**Steps:**

- [ ] **Step 1: Write the failing tests** in `tests/AcDream.App.Tests/UI/UiTextSpacingTests.cs`:

```csharp
using System.Numerics;
using AcDream.App.UI;
using DatReaderWriter.Types;   // FontCharDesc

namespace AcDream.App.Tests.UI;

public sealed class UiTextSpacingTests
{
    private static readonly Vector4 First = new(1f, 0f, 0f, 1f);
    private static readonly Vector4 Second = new(0f, 1f, 0f, 1f);

    private static UiDatFont Font()
    {
        var glyphs = new Dictionary<char, FontCharDesc>();
        foreach (char c in "ABCDEFGH") glyphs[c] = new FontCharDesc { Unicode = c, Width = 6, Height = 8 };
        return new UiDatFont(fgTex: 1u, fgW: 32, fgH: 32, bgTex: 0, bgW: 0, bgH: 0,
            lineHeight: 16f, baselineOffset: 12f, glyphs);
    }

    private static UiText Text(float spacing, float indent) => new()
    {
        Width = 200, Height = 100, DatFont = Font(), Padding = 0f, LineSpacing = spacing,
        LinesProvider = () => [new UiText.Line("ABC", First), new UiText.Line("DEF", Second, indent)],
    };

    [Fact]
    public void LineSpacing_SpreadsLines_AndCentresGlyphs()
    {
        var renderer = MarkupDrawProbe.Draw(Text(1.5f, 0f), 200, 100);
        float y1 = MarkupDrawProbe.QuadBounds(renderer, First).Min(b => b.Y);
        float y2 = MarkupDrawProbe.QuadBounds(renderer, Second).Min(b => b.Y);
        Assert.Equal(24f, y2 - y1, 2);   // 16 px font × 1.5
    }

    [Fact]
    public void Indent_MovesOnlyThatLine()
    {
        var plain = MarkupDrawProbe.Draw(Text(1f, 0f), 200, 100);
        var indented = MarkupDrawProbe.Draw(Text(1f, 16f), 200, 100);
        Assert.Equal(MarkupDrawProbe.QuadBounds(plain, First).Min(b => b.X),
            MarkupDrawProbe.QuadBounds(indented, First).Min(b => b.X), 2);
        Assert.Equal(MarkupDrawProbe.QuadBounds(plain, Second).Min(b => b.X) + 16f,
            MarkupDrawProbe.QuadBounds(indented, Second).Min(b => b.X), 2);
    }

    [Fact]
    public void DefaultSpacing_IsTheFontsLineHeight()
    {
        var renderer = MarkupDrawProbe.Draw(Text(1f, 0f), 200, 100);
        float y1 = MarkupDrawProbe.QuadBounds(renderer, First).Min(b => b.Y);
        float y2 = MarkupDrawProbe.QuadBounds(renderer, Second).Min(b => b.Y);
        Assert.Equal(16f, y2 - y1, 2);
    }
}
```

- [ ] **Step 2: Run and see them fail** (no `LineSpacing`, `Line` has no third parameter).

Run: `~/.dotnet/dotnet test tests/AcDream.App.Tests --filter "FullyQualifiedName~UiTextSpacingTests"`

- [ ] **Step 3: Implement** in `UiText.cs`:
  1. Change the record to `public readonly record struct Line(string Text, Vector4 Color, float Indent = 0f);`
  2. Add, next to `Padding`: `/// <summary>Line pitch as a multiple of the font's line height (1 = as the font sets it).</summary> public float LineSpacing { get; set; } = 1f;`
  3. In the multi-line draw, keep the font's height and use the pitch for layout:
     ```csharp
     float fontLineHeight = datFont is not null ? datFont.LineHeight : bitmapFont!.LineHeight;
     _lastLineHeight = fontLineHeight * MathF.Max(1f, LineSpacing);
     ```
     (replacing the `_lastLineHeight = …` line). `lh` stays `_lastLineHeight`, so extents, `Scroll.LineHeight`, the marker bar and the selection fill use the pitch. Glyphs go at `float glyphY = y + (lh - fontLineHeight) * 0.5f;`, so use `glyphY` instead of `y` in the four text-drawing calls inside the loop (the `datLines.Add` calls and both `ctx.DrawString` calls). Selection and marker fills keep `y` and `lh`.
  4. Add an overload that takes the indent, and use it for the loop and `HitChar`:
     ```csharp
     private float HorizontalOffset(in Line line, UiDatFont? datFont, BitmapFont? bitmapFont)
     {
         float x = HorizontalOffset(line.Text, datFont, bitmapFont);
         return Centered || RightAligned ? x : x + line.Indent;
     }
     ```
     In the draw loop use `float lineX = HorizontalOffset(lines[i], datFont, bitmapFont);`. In `HitChar` use `float lineX = HorizontalOffset(lines[line], _lastDatFont, _lastFont);`. Leave the other `HorizontalOffset(string …)` callers alone.

- [ ] **Step 4: Run all `UiText` tests and see them pass.** Run: `~/.dotnet/dotnet test tests/AcDream.App.Tests --filter "FullyQualifiedName~UiText"` → all PASS, old and new.

- [ ] **Step 5: Commit**

```bash
git add src/AcDream.App/UI/UiText.cs tests/AcDream.App.Tests/UI/UiTextSpacingTests.cs
git commit -m "UiText: line spacing and a per-line indent (both off by default)"
```

---

### Task 3: Transcript — channel prefix, wrap indent, line spacing

**Goal:** The deck's transcript and peek honour `ChannelPrefix`, `WrapIndent` and `LineSpacing`.

**Files:**
- Create: `src/AcDream.App/UI/Hud/ChatDeckPrefix.cs`
- Modify: `src/AcDream.App/UI/Hud/ChatDeckTranscript.cs`, `src/AcDream.App/UI/Hud/ChatDeckGeometry.cs`, `src/AcDream.App/UI/Hud/UiChatDeck.cs` (`Refresh` around line 192, `RebuildTranscript`), `src/AcDream.App/UI/Hud/UiChatDeck.Strip.cs` (`_peek` field)
- Test: `tests/AcDream.App.Tests/UI/Hud/ChatDeckPrefixTests.cs` (create), `tests/AcDream.App.Tests/UI/Hud/ChatDeckTranscriptTests.cs`, `tests/AcDream.App.Tests/UI/Hud/UiChatDeckTests.cs`

**Acceptance Criteria:**
- [ ] `ChatDeckPrefix.Strip` drops `[ChannelName] ` from a `ChatKind.Channel` line that starts with it, trimming the spans so they still rejoin to the text; any other line comes back unchanged
- [ ] With `ChannelPrefix` off, a `[General] Bob says, "hi"` line shows `Bob says, "hi"` in the transcript and the peek; with it on, it is unchanged
- [ ] With `WrapIndent` on, the transcript wraps at `width − ChatDeckGeometry.WrapIndent`, and every continuation line has `Indent == ChatDeckGeometry.WrapIndent` (16); the first line has 0
- [ ] `Transcript.LineSpacing` equals `appearance.LineSpacing` after `Refresh`
- [ ] Toggling either setting rebuilds the cached lines (no stale wrap)

**Verify:** `~/.dotnet/dotnet test tests/AcDream.App.Tests --filter "FullyQualifiedName~ChatDeck"` → all pass

**Steps:**

- [ ] **Step 1: Write the failing tests.** `ChatDeckPrefixTests.cs`. Build entries the way `ChatDeckTranscriptTests` does: look at that file's helper for a channel `ChatEntry` (`ChatLog.OnChannel…`, or `new ChatEntry { Kind = ChatKind.Channel, ChannelName = "General", Sender = "Bob", Text = "hi" … }`) and use the same:

```csharp
[Fact]
public void Strip_DropsTheChannelName_AndKeepsSpansWhole()
{
    ChatEntry entry = ChannelEntry("General", "Bob", "hi");          // helper as in ChatDeckTranscriptTests
    ChatProjectedLine projected = ChatLineProjector.Project(entry);
    Assert.StartsWith("[General] ", projected.Text);

    ChatProjectedLine shown = ChatDeckPrefix.Strip(projected, entry);

    Assert.Equal(projected.Text["[General] ".Length..], shown.Text);
    if (shown.Spans is { } spans) Assert.Equal(shown.Text, string.Concat(spans.Select(s => s.Text)));
}

[Fact]
public void Strip_LeavesOtherLinesAlone()
{
    ChatEntry entry = SayEntry("Bob", "[General] is not a prefix here");   // a local Say line
    ChatProjectedLine projected = ChatLineProjector.Project(entry);
    Assert.Equal(projected, ChatDeckPrefix.Strip(projected, entry));
}
```

In `UiChatDeckTests.cs`, using its `Harness` (add a `Channel(...)` helper beside `Say` that calls the `ChatLog` method for a named channel line, then `Model!.Tick(); Deck.Refresh();`):

```csharp
[Fact]
public void ChannelPrefixOff_DropsTheNameFromTheTranscript()
{
    var h = new Harness();
    h.Model!.SetAppearance(h.Model.Settings.Appearance with { ChannelPrefix = false });
    h.Channel("General", "Bob", "hi");
    Assert.DoesNotContain(TranscriptText(h), t => t.StartsWith("[General]"));
}

[Fact]
public void WrapIndent_IndentsContinuationLinesOnly()
{
    var h = new Harness();
    h.Model!.SetAppearance(h.Model.Settings.Appearance with { WrapIndent = true });
    h.Say(string.Join(" ", Enumerable.Repeat("wrapping", 40)));
    var lines = h.Deck.Transcript.LinesProvider!();
    Assert.True(lines.Count > 1);
    Assert.Equal(0f, lines[0].Indent);
    Assert.All(lines.Skip(1), l => Assert.Equal(ChatDeckGeometry.WrapIndent, l.Indent));
}

[Fact]
public void LineSpacing_ReachesTheTranscript()
{
    var h = new Harness();
    h.Model!.SetAppearance(h.Model.Settings.Appearance with { LineSpacing = 1.4f });
    h.Deck.Refresh();
    Assert.Equal(1.4f, h.Deck.Transcript.LineSpacing, 3);
}
```

`TranscriptText(h)` is `h.Deck.Transcript.LinesProvider!().Select(l => l.Text)`. If the harness has no font, the deck measures `s.Length * 7f`, so 40 × "wrapping" wraps in a 460 px window.

- [ ] **Step 2: Run and see them fail.** Run: `~/.dotnet/dotnet test tests/AcDream.App.Tests --filter "FullyQualifiedName~ChatDeck"`

- [ ] **Step 3: Implement.** Create `ChatDeckPrefix.cs`:

```csharp
using AcDream.Core.Chat;
using AcDream.Runtime.Chat;

namespace AcDream.App.UI.Hud;

/// <summary>The "[Channel] " the retail wording puts in front of a named-channel line.</summary>
internal static class ChatDeckPrefix
{
    /// <summary>The line without that prefix; any line that does not start with it comes back unchanged.</summary>
    public static ChatProjectedLine Strip(ChatProjectedLine projected, in ChatEntry entry)
    {
        if (entry.Kind != ChatKind.Channel || string.IsNullOrEmpty(entry.ChannelName)) return projected;
        string prefix = $"[{entry.ChannelName}] ";
        if (!projected.Text.StartsWith(prefix, StringComparison.Ordinal)) return projected;
        string text = projected.Text[prefix.Length..];
        if (projected.Spans is null) return new ChatProjectedLine(text, null);
        var spans = new List<ChatTextSpan>();
        int cut = prefix.Length;
        foreach (ChatTextSpan span in projected.Spans)
        {
            if (cut >= span.Text.Length) { cut -= span.Text.Length; continue; }
            spans.Add(cut > 0 ? span with { Text = span.Text[cut..] } : span);
            cut = 0;
        }
        return new ChatProjectedLine(text, spans);
    }
}
```

(If `ChatEntry` is a class, drop the `in`.) In `ChatDeckGeometry` add `public const float WrapIndent = 16f;`.

In `ChatDeckTranscript.cs`:
- Add two trailing parameters to `ChatDeckTranscriptStyle`: `bool ChannelPrefix = true, float WrapIndent = 0f`.
- In `Cached`, add `public ChatProjectedLine Shown; public bool Prefix = true; public float Indent;`.
- In `Wrap`, add `&& c.Prefix == style.ChannelPrefix && c.Indent.Equals(style.WrapIndent)` to the early-return condition; then set `c.Prefix = style.ChannelPrefix; c.Indent = style.WrapIndent; c.Shown = style.ChannelPrefix ? c.Projected : ChatDeckPrefix.Strip(c.Projected, line.Entry);`, and build `full` from `c.Shown.Text`.
- In `Shape` and `Colours`, use `c.Shown` wherever they read `c.Projected` (text, spans, `LastIndexOf` / `IndexOf` origins, the colours array length). In `Shape`, build lines as `new UiText.Line(frag, lineColour, i > 0 ? style.WrapIndent : 0f)`.

In `UiChatDeck.cs`, in `Refresh` after setting `Transcript.DatFont`, add `Transcript.LineSpacing = appearance.LineSpacing;`. In `RebuildTranscript`, set `float indent = appearance.WrapIndent ? ChatDeckGeometry.WrapIndent : 0f;`, pass `MathF.Max(1f, wrap - indent)` as the wrap width, and pass `ChannelPrefix: appearance.ChannelPrefix, WrapIndent: indent` to the style. The `_builtFor` key already holds `_model?.Settings`, which every `SetAppearance` replaces.

In `UiChatDeck.Strip.cs`, replace the `_peek` initializer with a constructor-time assignment that reads the setting (a field initializer cannot use `this`). Declare `private readonly ChatDeckPeekFeed _peek;` and assign it in the `UiChatDeck` constructor (a readonly field can only be set there):

```csharp
_peek = new ChatDeckPeekFeed(line =>
{
    ChatProjectedLine projected = ChatLineProjector.Project(line.Entry);
    return (_model?.Settings.Appearance.ChannelPrefix ?? true) ? projected.Text : ChatDeckPrefix.Strip(projected, line.Entry).Text;
});
```

- [ ] **Step 4: Run and see them pass.** Same command → PASS. Also run `--filter "FullyQualifiedName~UiText"` → PASS.

- [ ] **Step 5: Commit**

```bash
git add src/AcDream.App/UI/Hud tests/AcDream.App.Tests/UI/Hud
git commit -m "Chat Deck: channel prefix, wrap indent and line spacing in the transcript and peek"
```

---

### Task 4: Fade when idle

**Goal:** While pinned with `FadeWhenIdle` on, the deck's frame background fades out after `FadeAfterSeconds` without activity, and comes back at once on activity.

**Files:**
- Create: `src/AcDream.App/UI/Hud/ChatDeckIdleFade.cs`
- Modify: `src/AcDream.App/UI/Hud/UiChatDeck.Drawer.cs` (`TickDrawer`, `OnLinesDelivered`), `src/AcDream.App/UI/Hud/UiChatDeck.Draw.cs` (`OnDraw`)
- Test: `tests/AcDream.App.Tests/UI/Hud/ChatDeckIdleFadeTests.cs` (create), `tests/AcDream.App.Tests/UI/Hud/UiChatDeckDrawerTests.cs`

**Acceptance Criteria:**
- [ ] `ChatDeckIdleFade.Factor` is 1 until `afterSeconds` of idle ticks, then falls linearly to 0 over `FadeSeconds` (0.4)
- [ ] Any active tick, `Wake()` or the setting being off puts the factor back to 1 and restarts the count
- [ ] The deck (pinned, setting on, pointer away, input unfocused) reports `BackgroundFade == 0` after `FadeAfterSeconds + 0.4` s of `TickDrawer`; moving the pointer over it gives 1 on the next tick
- [ ] A line arriving in the selected tab wakes the fade; collapsed or flown out never fades

**Verify:** `~/.dotnet/dotnet test tests/AcDream.App.Tests --filter "FullyQualifiedName~ChatDeck"` → all pass

**Steps:**

- [ ] **Step 1: Write the failing tests.** `ChatDeckIdleFadeTests.cs`:

```csharp
using AcDream.App.UI.Hud;

namespace AcDream.App.Tests.UI.Hud;

public sealed class ChatDeckIdleFadeTests
{
    [Fact]
    public void FadesAfterTheDelay_OverFadeSeconds()
    {
        var fade = new ChatDeckIdleFade();
        fade.Tick(9.9, active: false, enabled: true, afterSeconds: 10);
        Assert.Equal(1f, fade.Factor);
        fade.Tick(0.3, active: false, enabled: true, afterSeconds: 10);   // 0.2 s into the fade
        Assert.Equal(0.5f, fade.Factor, 3);
        fade.Tick(1.0, active: false, enabled: true, afterSeconds: 10);
        Assert.Equal(0f, fade.Factor);
    }

    [Fact]
    public void ActivityWakeOrDisabled_RestoreAndRestart()
    {
        var fade = new ChatDeckIdleFade();
        fade.Tick(20, active: false, enabled: true, afterSeconds: 10);
        fade.Tick(0.016, active: true, enabled: true, afterSeconds: 10);
        Assert.Equal(1f, fade.Factor);
        fade.Tick(20, active: false, enabled: true, afterSeconds: 10);
        fade.Wake();
        Assert.Equal(1f, fade.Factor);
        fade.Tick(20, active: false, enabled: false, afterSeconds: 10);
        Assert.Equal(1f, fade.Factor);
    }
}
```

In `UiChatDeckDrawerTests.cs` (uses `DrawerWorld`):

```csharp
[Fact]
public void Pinned_IdleFadesTheBackground_PointerBringsItBack()
{
    var w = new DrawerWorld();
    w.H.Model!.SetAppearance(w.H.Model.Settings.Appearance with { FadeWhenIdle = true, FadeAfterSeconds = 2 });
    w.Root.OnMouseMove(1500, 100);   // away from the deck
    w.Tick(2.5);
    Assert.Equal(0f, w.Deck.BackgroundFade);
    w.Root.OnMouseMove((int)w.Deck.Left + 20, (int)w.Deck.Top + 60);
    w.Tick(0.016);
    Assert.Equal(1f, w.Deck.BackgroundFade);
}

[Fact]
public void ALineInTheSelectedTab_WakesTheFade()
{
    var w = new DrawerWorld();
    w.H.Model!.SetAppearance(w.H.Model.Settings.Appearance with { FadeWhenIdle = true, FadeAfterSeconds = 2 });
    w.Root.OnMouseMove(1500, 100);
    w.Tick(2.5);
    w.H.Say("hello");   // the All tab is selected
    Assert.Equal(1f, w.Deck.BackgroundFade);
}
```

- [ ] **Step 2: Run and see them fail.**

- [ ] **Step 3: Implement.** `ChatDeckIdleFade.cs`:

```csharp
namespace AcDream.App.UI.Hud;

/// <summary>The pinned deck's background fading out when nothing has happened for a while. Text never fades.</summary>
internal sealed class ChatDeckIdleFade
{
    public const double FadeSeconds = 0.4;
    private double _idle;

    /// <summary>1 = the background as set; 0 = faded out.</summary>
    public float Factor { get; private set; } = 1f;

    public void Tick(double deltaSeconds, bool active, bool enabled, int afterSeconds)
    {
        if (!enabled || active)
        {
            Wake();
            return;
        }
        _idle += deltaSeconds;
        double over = _idle - afterSeconds;
        Factor = over <= 0 ? 1f : (float)Math.Max(0, 1 - over / FadeSeconds);
    }

    public void Wake()
    {
        _idle = 0;
        Factor = 1f;
    }
}
```

In `UiChatDeck.Drawer.cs`: add the field `private readonly ChatDeckIdleFade _fade = new();` and `internal float BackgroundFade => _fade.Factor;`. In `TickDrawer`, after `_drawer.Tick(...)`:

```csharp
ChatDeckAppearance appearance = _model?.Settings.Appearance ?? new ChatDeckAppearance();
_fade.Tick(deltaSeconds, DeckActive(), appearance.FadeWhenIdle && _drawer.Pinned, appearance.FadeAfterSeconds);
```

with

```csharp
/// <summary>Anything that keeps a pinned deck from fading: the pointer over it, typing, a menu, a drag-select.</summary>
private bool DeckActive()
{
    if (FindRoot() is not { } root) return true;
    UiElement? focus = root.KeyboardFocus;
    return Inside(Left, Top, Width, Height, root.MouseX, root.MouseY)
        || ReferenceEquals(focus, Input) || ReferenceEquals(focus, SearchField)
        || _tabMenu.IsOpen || _chipMenu.IsOpen || Transcript.IsSelecting;
}
```

In `OnLinesDelivered`, first line of the method: `if (_model is { } m && deliveries.Any(d => d.TabIds.Contains(m.SelectedTabId))) _fade.Wake();`.

In `UiChatDeck.Draw.cs` `OnDraw`: `ChatDeckPalette.DrawFrame(ctx, _geometry.Frame, opacity * _fade.Factor);`.

- [ ] **Step 4: Run and see them pass.**

- [ ] **Step 5: Commit**

```bash
git add src/AcDream.App/UI/Hud tests/AcDream.App.Tests/UI/Hud
git commit -m "Chat Deck: fade the pinned background when idle"
```

---

### Task 5: Beep on alert

**Goal:** With `BeepOnAlert` on, an alert line plays a UI sound, at most once every 2 s, through a new `PlayUiSound` runtime binding wired to the client's `UiSoundController`.

**Files:**
- Create: `src/AcDream.App/UI/Hud/ChatDeckAlertBeep.cs`
- Modify: `src/AcDream.App/UI/Hud/UiChatDeck.cs` (`ChatDeckBindings`: add `Action? PlayAlert = null`), `src/AcDream.App/UI/Hud/UiChatDeck.Drawer.cs`, `src/AcDream.App/UI/RetailUiRuntime.cs` (`RetailUiRuntimeBindings`: add `Action<AcDream.Core.Audio.SoundId>? PlayUiSound = null` after `CommandDeck`; deck construction in `MountChatDeck`), `src/AcDream.App/Composition/InteractionRetainedUiComposition.cs` (dependencies record + bindings construction near `CommandDeck: new CommandDeckRuntimeBindings(`), `src/AcDream.App/Rendering/GameWindow.cs` (`new InteractionRetainedUiDependencies(` near line 1321)
- Test: `tests/AcDream.App.Tests/UI/Hud/ChatDeckAlertBeepTests.cs` (create), `tests/AcDream.App.Tests/UI/Hud/UiChatDeckTests.cs`

**Acceptance Criteria:**
- [ ] `ChatDeckAlertBeep.Lines` calls `play` once for a batch holding an alert line, never when disabled or for batches without one, and not again until 2 s of `Tick` have passed
- [ ] The deck beeps for an alert line only while shown and `BeepOnAlert` is on (a line with the character's name alerts through the default My-name rule)
- [ ] `RetailUiRuntimeBindings.PlayUiSound` is set from `contentEffectsAudio.Audio?.UiSounds` in the real client, and the deck's `PlayAlert` plays `ChatDeckAlertBeep.Sound` (`SoundId.UI_TransientMessage`)
- [ ] The app builds; the composition wiring tests still pass

**Verify:** `~/.dotnet/dotnet build && ~/.dotnet/dotnet test tests/AcDream.App.Tests --filter "FullyQualifiedName~ChatDeck|FullyQualifiedName~Composition"` → build succeeds, tests pass

**Steps:**

- [ ] **Step 1: Write the failing tests.** `ChatDeckAlertBeepTests.cs` (the throttle on its own; the deck test below uses the real model):

```csharp
using AcDream.App.UI.Hud;

namespace AcDream.App.Tests.UI.Hud;

public sealed class ChatDeckAlertBeepTests
{
    [Fact]
    public void Alert_PlaysOnce_ThenWaitsTwoSeconds()
    {
        int played = 0;
        var beep = new ChatDeckAlertBeep(() => played++);
        beep.Lines(hasAlert: true, enabled: true);
        beep.Lines(hasAlert: true, enabled: true);
        Assert.Equal(1, played);
        beep.Tick(2.0);
        beep.Lines(hasAlert: true, enabled: true);
        Assert.Equal(2, played);
    }

    [Fact]
    public void NoAlertOrDisabled_PlaysNothing()
    {
        int played = 0;
        var beep = new ChatDeckAlertBeep(() => played++);
        beep.Lines(hasAlert: false, enabled: true);
        beep.Lines(hasAlert: true, enabled: false);
        Assert.Equal(0, played);
    }
}
```

In `UiChatDeckTests.cs`: give `Harness` a `public int Beeps;` and pass `PlayAlert: () => Beeps++` in its `ChatDeckBindings`. Then:

```csharp
[Fact]
public void AlertLine_Beeps_OnlyWhenOnAndShown()
{
    var h = new Harness();
    h.Deck.OnShown();
    h.Say("hello Davian");            // My-name rule: alert
    Assert.Equal(0, h.Beeps);         // off by default
    h.Model!.SetAppearance(h.Model.Settings.Appearance with { BeepOnAlert = true });
    h.Deck.TickDrawer(2.0);
    h.Say("Davian again");
    Assert.Equal(1, h.Beeps);
    h.Deck.OnHidden();
    h.Deck.TickDrawer(2.0);
    h.Say("Davian once more");
    Assert.Equal(1, h.Beeps);
}
```

(`OnShown` / `OnHidden` may need a root: if they return early without one, mount `h.Deck` the way `DrawerWorld` does, or use `DrawerWorld` for this test.)

- [ ] **Step 2: Run and see them fail.**

- [ ] **Step 3: Implement.** `ChatDeckAlertBeep.cs`:

```csharp
using AcDream.Core.Audio;

namespace AcDream.App.UI.Hud;

/// <summary>The sound an alert line makes, at most once every <see cref="MinGapSeconds"/>.</summary>
internal sealed class ChatDeckAlertBeep(Action play)
{
    public const SoundId Sound = SoundId.UI_TransientMessage;
    public const double MinGapSeconds = 2.0;
    private double _sincePlayed = MinGapSeconds;

    public void Tick(double deltaSeconds) => _sincePlayed = Math.Min(MinGapSeconds, _sincePlayed + deltaSeconds);

    public void Lines(bool hasAlert, bool enabled)
    {
        if (!hasAlert || !enabled || _sincePlayed < MinGapSeconds) return;
        play();
        _sincePlayed = 0;
    }
}
```

`UiChatDeck`: add `PlayAlert` to `ChatDeckBindings` (trailing `Action? PlayAlert = null`). In `UiChatDeck.Drawer.cs`: add the field `private readonly ChatDeckAlertBeep _beep;`, assign `_beep = new ChatDeckAlertBeep(() => _b.PlayAlert?.Invoke());` in the constructor (where `CreateDrawer()` is called), call `_beep.Tick(deltaSeconds);` at the top of `TickDrawer` (before the `_shown` check, so the gap counts while hidden too), and in `OnLinesDelivered`:

```csharp
_beep.Lines(deliveries.Any(d => d.Line.Alert), _shown && (_model?.Settings.Appearance.BeepOnAlert ?? false));
```

Runtime: add `Action<AcDream.Core.Audio.SoundId>? PlayUiSound = null` as the last `RetailUiRuntimeBindings` parameter. In `MountChatDeck`'s `new Hud.ChatDeckBindings(...)`, add `PlayAlert: _bindings.PlayUiSound is { } play ? () => play(Hud.ChatDeckAlertBeep.Sound) : null`.

Composition: add `AcDream.App.Audio.UiSoundController? UiSounds = null` as the last parameter of `InteractionRetainedUiDependencies`. In `GameWindow.cs`, in the `new InteractionRetainedUiDependencies(` call, pass `UiSounds: contentEffectsAudio.Audio?.UiSounds` (the same property `SessionPlayerComposition` reads as `content.Audio?.UiSounds`). In `InteractionRetainedUiComposition`, where `CommandDeck: new CommandDeckRuntimeBindings(...)` is passed, add `PlayUiSound: d.UiSounds is { } uiSounds ? sound => uiSounds.Play(sound) : null,` (`d` is the dependencies there; if the local is named differently, use it).

- [ ] **Step 4: Run the build and tests and see them pass.** Run the Verify command.

- [ ] **Step 5: Commit**

```bash
git add src tests
git commit -m "Chat Deck: beep on alert through the client's UI sounds"
```

---

### Task 6: Extra tab keys

**Goal:** The two hotkeys in `ChatDeckSettings.Keys` cycle tabs while the deck's input has focus, in addition to the built-in keys.

**Files:**
- Modify: `src/AcDream.App/UI/UiField.cs` (add `OnChordKey`, check it first in `KeyDown` ~line 885), `src/AcDream.App/UI/Hud/UiChatDeck.Input.cs` (~line 42)
- Test: `tests/AcDream.App.Tests/UI/Hud/UiChatDeckInputTests.cs`, `tests/AcDream.App.Tests/UI/UiFieldTests.cs` (or the existing UiField test file: `ls tests/AcDream.App.Tests/UI | grep -i field`)

**Acceptance Criteria:**
- [ ] `UiField.OnChordKey(Key, ModifierMask)` is asked first for a key pressed with at least one of Ctrl/Alt/Shift/Super held; returning true consumes it. With no modifier held it is never called
- [ ] A deck with `Keys.NextTab = "Ctrl+Alt+N"` moves to the next tab on Ctrl+Alt+N and leaves typing alone on plain N; `PreviousTab` moves back
- [ ] Ctrl+Tab and Option+Cmd+Left/Right still cycle; Ctrl+A / Ctrl+C still select and copy (existing tests pass)
- [ ] An empty or unreadable key string matches nothing

**Verify:** `~/.dotnet/dotnet test tests/AcDream.App.Tests --filter "FullyQualifiedName~UiChatDeckInputTests|FullyQualifiedName~UiField"` → all pass

**Steps:**

- [ ] **Step 1: Write the failing tests** in `UiChatDeckInputTests.cs` (it has `Key(field, key)` and the harness's `FakeKeyboard`):

```csharp
[Fact]
public void ExtraTabKeys_CycleTabs_PlainKeyStillTypes()
{
    var h = new Harness();
    h.Model!.SetKeys(new ChatDeckKeys { NextTab = "Ctrl+Alt+N", PreviousTab = "Ctrl+Alt+P" });
    h.Keyboard.Held.Add(Silk.NET.Input.Key.ControlLeft);
    h.Keyboard.Held.Add(Silk.NET.Input.Key.AltLeft);
    Key(h.Deck.Input, Silk.NET.Input.Key.N);
    Assert.Equal("local", h.Model.SelectedTabId);
    Key(h.Deck.Input, Silk.NET.Input.Key.P);
    Assert.Equal(ChatDeckDefaults.AllTabId, h.Model.SelectedTabId);

    h.Keyboard.Held.Clear();
    Key(h.Deck.Input, Silk.NET.Input.Key.N);
    Assert.Equal(ChatDeckDefaults.AllTabId, h.Model.SelectedTabId);
}

[Fact]
public void UnboundOrUnreadableKeys_MatchNothing()
{
    var h = new Harness();
    h.Model!.SetKeys(new ChatDeckKeys { NextTab = "Ctrl+Nonsense" });
    h.Keyboard.Held.Add(Silk.NET.Input.Key.ControlLeft);
    Key(h.Deck.Input, Silk.NET.Input.Key.N);
    Assert.Equal(ChatDeckDefaults.AllTabId, h.Model.SelectedTabId);
}
```

- [ ] **Step 2: Run and see them fail.**

- [ ] **Step 3: Implement.** In `UiField.cs` add, next to `OnCommandOptionKey`:

```csharp
/// <summary>Asked first for a key pressed with a modifier held; true consumes it. Never asked for a bare key.</summary>
public Func<Silk.NET.Input.Key, ModifierMask, bool>? OnChordKey { get; set; }

private ModifierMask HeldModifiers() =>
    (CtrlHeld() ? ModifierMask.Ctrl : 0) | (AltHeld() ? ModifierMask.Alt : 0)
    | (ShiftHeld() ? ModifierMask.Shift : 0) | (SuperHeld() ? ModifierMask.Win : 0);
```

(`using AcDream.UI.Abstractions.Input;` for `ModifierMask`.) In `KeyDown`, right after `var key = (Silk.NET.Input.Key)e.Data0;`:

```csharp
if (OnChordKey is not null && HeldModifiers() is var mods && mods != ModifierMask.None && OnChordKey(key, mods))
    return true;
```

In `UiChatDeck.Input.cs`, beside the `OnControlKey` / `OnCommandOptionKey` setup:

```csharp
Input.OnChordKey = (key, mods) =>
    Matches(_model?.Settings.Keys.NextTab, key, mods) ? CycleTab(1)
    : Matches(_model?.Settings.Keys.PreviousTab, key, mods) && CycleTab(-1);
```

and

```csharp
/// <summary>Whether a saved hotkey string is exactly this key and these modifiers.</summary>
private static bool Matches(string? hotkey, Silk.NET.Input.Key key, ModifierMask mods) =>
    MarkupHotkeyText.Parse(hotkey) is { } chord && chord.Key == key && chord.Modifiers == mods;
```

(`using AcDream.App.UI.Markup; using AcDream.UI.Abstractions.Input;`.)

- [ ] **Step 4: Run and see them pass**, including the existing `CtrlTab_CyclesTabs` and `OptionCmdArrows_CycleTabs`.

- [ ] **Step 5: Commit**

```bash
git add src/AcDream.App/UI tests/AcDream.App.Tests/UI
git commit -m "Chat Deck: optional extra keys to cycle tabs"
```

---

### Task 7: The ⚙ settings stud

**Goal:** The top row gets a ⚙ stud between search and collapse. Pressing it calls `ChatDeckBindings.OpenSettings`, and it shows lit while `SettingsOpen` returns true.

**Files:**
- Create: `assets/hud/settings.svg`
- Modify: `src/AcDream.App/UI/Hud/EmbeddedSvgSpriteCache.cs` (`HudIcons.Settings`, add to `All`), `src/AcDream.App/UI/Hud/ChatDeckGeometry.cs` (zone, rect, layout), `src/AcDream.App/UI/Hud/UiChatDeck.cs` (bindings, hit zones line ~120, press switch line ~355), `src/AcDream.App/UI/Hud/UiChatDeck.Draw.cs`
- Test: `tests/AcDream.App.Tests/UI/Hud/ChatDeckGeometryTests.cs`, `tests/AcDream.App.Tests/UI/Hud/UiChatDeckTests.cs`, `tests/AcDream.App.Tests/UI/Hud/ChatDeckIconsTests.cs` (if it checks `HudIcons.All` against the embedded files)

**Acceptance Criteria:**
- [ ] Studs right to left: collapse at the right pad, then settings, then search, each `StudSize` wide with `StudGap` between; tabs end left of search
- [ ] `HitAt` on the settings stud gives `ChatDeckZone.SettingsStud`; a press calls `OpenSettings` once
- [ ] The stud draws `HudIcons.Settings`, lit while `SettingsOpen()` is true
- [ ] `settings.svg` is embedded (the icon tests that load every `HudIcons.All` entry pass)

**Verify:** `~/.dotnet/dotnet test tests/AcDream.App.Tests --filter "FullyQualifiedName~ChatDeck|FullyQualifiedName~HudIcon"` → all pass

**Steps:**

- [ ] **Step 1: Write the failing tests.** In `ChatDeckGeometryTests.cs` (follow its `Create(...)` call):

```csharp
[Fact]
public void Studs_CollapseSettingsSearch_RightToLeft()
{
    ChatDeckGeometry g = Create(460f, 220f);   // the file's helper
    Assert.Equal(g.CollapseStud.X - ChatDeckGeometry.StudGap - ChatDeckGeometry.StudSize, g.SettingsStud.X);
    Assert.Equal(g.SettingsStud.X - ChatDeckGeometry.StudGap - ChatDeckGeometry.StudSize, g.SearchStud.X);
    Assert.Equal(ChatDeckZone.SettingsStud, g.HitAt(g.SettingsStud.X + 2, g.SettingsStud.Y + 2).Zone);
}
```

In `UiChatDeckTests.cs` give the `Harness` a `public int SettingsOpened;` and pass `OpenSettings: () => SettingsOpened++` in its bindings, then:

```csharp
[Fact]
public void SettingsStud_OpensTheSettings()
{
    var h = new Harness();
    h.Press(h.Deck.Geometry.SettingsStud);
    Assert.Equal(1, h.SettingsOpened);
}
```

- [ ] **Step 2: Run and see them fail.**

- [ ] **Step 3: Implement.** `assets/hud/settings.svg` (single path strokes and a circle, the same features `search.svg` uses):

```svg
<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 24 24"><path d="M12 2.5v3M12 18.5v3M2.5 12h3M18.5 12h3M5.3 5.3l2.1 2.1M16.6 16.6l2.1 2.1M5.3 18.7l2.1-2.1M16.6 7.4l2.1-2.1" stroke="#fff" stroke-width="3" stroke-linecap="round"/><circle cx="12" cy="12" r="5" fill="none" stroke="#fff" stroke-width="2.5"/></svg>
```

`HudIcons`: `public const string Settings = "settings";` and add `Settings` after `Search` in `All`.

`ChatDeckGeometry`: add `SettingsStud` to the zone enum, a `public required DeckRect SettingsStud { get; init; }`, and in `Create`:

```csharp
var collapse = new DeckRect(width - Pad - StudSize, Pad, StudSize, StudSize);
var settings = collapse with { X = collapse.X - StudGap - StudSize };
var stud = settings with { X = settings.X - StudGap - StudSize };   // search
```

Set `SettingsStud = settings` in the initializer, and in `HitAt` add `if (SettingsStud.Contains(x, y)) return new(ChatDeckZone.SettingsStud);` after the search check. Update any existing geometry test that pinned the old search position.

`UiChatDeck.cs`: add `Action? OpenSettings = null, Func<bool>? SettingsOpen = null` to `ChatDeckBindings`. Add `ChatDeckZone.SettingsStud` to the zone list at line ~120, and add `case ChatDeckZone.SettingsStud: _b.OpenSettings?.Invoke(); return true;` to the press switch. `UiChatDeck.Draw.cs`: after the search stud, `DrawStud(ctx, _geometry.SettingsStud, HudIcons.Settings, _b.SettingsOpen?.Invoke() == true);`. If the deck has stud tooltips (grep `"Search"` in `UiChatDeck*.cs`), add `"Settings"` the same way.

- [ ] **Step 4: Run and see them pass.**

- [ ] **Step 5: Commit**

```bash
git add assets/hud/settings.svg src/AcDream.App/UI/Hud tests/AcDream.App.Tests/UI/Hud
git commit -m "Chat Deck: settings stud"
```

---

### Task 8: Settings popup binding and markup

**Goal:** `ChatDeckSettingsBinding` exposes every appearance setting, the two extra keys and the per-channel colours to markup, writes through `SetAppearance` / `SetKeys`, and ships the popup's markup. The markup builds with this binding.

**Files:**
- Create: `src/AcDream.App/UI/Hud/ChatDeckChannelColours.cs`, `src/AcDream.App/UI/Hud/ChatDeckSettingsBinding.cs`
- Test: `tests/AcDream.App.Tests/UI/Hud/ChatDeckSettingsBindingTests.cs` (create)

**Acceptance Criteria:**
- [ ] `ChatDeckChannelColours.Default(channel)` gives every `ChatDeckChannel` an opaque colour: the retail colour of a chat type that `ChatDeckChannels.ByLogTextType` maps to it. Channels with no such type use: Patron/Vassals/Monarch/CoVassals → Social, Olthoi → Allegiance, OtherChannel → Channel, Plugin → ClientLocal
- [ ] Getters read the model's current settings; with no model they read the defaults and setters do nothing
- [ ] Setting font size, input font size, line spacing, opacity (as a percent 0–100), timestamps (index 0–2), prefix, wrap indent, fade on/off, fade seconds and beep each changes exactly that field through `SetAppearance`
- [ ] `NextTabKey` / `PreviousTabKey` round-trip through `SetKeys`
- [ ] Each colour row shows the override or the default as `#AARRGGBB`; setting the default removes the override; `ResetColours` clears all overrides
- [ ] `MarkupDocument.Build(ChatDeckSettingsBinding.Markup, binding, _ => (0u, 0, 0))` builds without throwing

**Verify:** `~/.dotnet/dotnet test tests/AcDream.App.Tests --filter "FullyQualifiedName~ChatDeckSettingsBinding"` → all pass

**Steps:**

- [ ] **Step 1: Write the failing tests** `ChatDeckSettingsBindingTests.cs`:

```csharp
using AcDream.App.UI;
using AcDream.App.UI.Hud;
using AcDream.Core.Chat;
using AcDream.Core.Chat.Deck;

namespace AcDream.App.Tests.UI.Hud;

public sealed class ChatDeckSettingsBindingTests
{
    private static (ChatDeckModel Model, ChatDeckSettingsBinding Binding) Make()
    {
        var model = new ChatDeckModel(new ChatLog(), ChatDeckDefaults.Create(), "Davian");
        return (model, new ChatDeckSettingsBinding(() => model));
    }

    [Fact]
    public void EveryChannel_HasAnOpaqueDefault()
    {
        foreach (ChatDeckChannel c in Enum.GetValues<ChatDeckChannel>())
            Assert.Equal(0xFFu, ChatDeckChannelColours.Default(c) >> 24);
    }

    [Fact]
    public void Setters_WriteTheirOwnField()
    {
        var (model, b) = Make();
        b.SetFontSize(16);
        b.SetOpacity(40);
        b.SetTimestamps(2);
        b.SetChannelPrefix(false);
        b.SetBeep(true);
        ChatDeckAppearance a = model.Settings.Appearance;
        Assert.Equal(16, a.FontSize);
        Assert.Equal(0.4f, a.BackgroundOpacity, 3);
        Assert.Equal(ChatDeckTimestamps.HourMinuteSecond, a.Timestamps);
        Assert.False(a.ChannelPrefix);
        Assert.True(a.BeepOnAlert);
        Assert.Equal(new ChatDeckAppearance().InputFontSize, a.InputFontSize);
        Assert.Equal(40, b.Opacity);
    }

    [Fact]
    public void Keys_RoundTrip()
    {
        var (model, b) = Make();
        b.SetNextTabKey("Ctrl+Alt+N");
        Assert.Equal("Ctrl+Alt+N", model.Settings.Keys.NextTab);
        Assert.Equal("Ctrl+Alt+N", b.NextTabKey);
    }

    [Fact]
    public void Colours_OverrideDefaultAndReset()
    {
        var (model, b) = Make();
        ChatDeckSettingsBinding.ColourRow row = b.Colours.First(r => r.Id == nameof(ChatDeckChannel.General));
        Assert.Equal($"#{ChatDeckChannelColours.Default(ChatDeckChannel.General):X8}", row.Hex);
        row.SetHex("#FF112233");
        Assert.Equal(0xFF112233u, model.Settings.Appearance.ChannelColours[ChatDeckChannel.General]);
        row.SetHex(row.DefaultHex);
        Assert.False(model.Settings.Appearance.ChannelColours.ContainsKey(ChatDeckChannel.General));
        row.SetHex("#FF445566");
        b.ResetColours();
        Assert.Empty(model.Settings.Appearance.ChannelColours);
    }

    [Fact]
    public void NoModel_ReadsDefaults_IgnoresSets()
    {
        var b = new ChatDeckSettingsBinding(() => null);
        b.SetFontSize(18);
        Assert.Equal(new ChatDeckAppearance().FontSize, b.FontSize);
    }

    [Fact]
    public void Markup_Builds()
    {
        var (_, b) = Make();
        UiElement window = MarkupDocument.Build(ChatDeckSettingsBinding.Markup, b, _ => (0u, 0, 0));
        Assert.NotNull(window);
    }
}
```

- [ ] **Step 2: Run and see them fail.**

- [ ] **Step 3: Implement.** `ChatDeckChannelColours.cs`:

```csharp
using System.Numerics;
using AcDream.Core.Chat;
using AcDream.Core.Chat.Deck;
using AcDream.UI.Abstractions.Panels.Chat;

namespace AcDream.App.UI.Hud;

/// <summary>The colour a channel's lines take when no override is set: the retail chat colour of its chat type.</summary>
internal static class ChatDeckChannelColours
{
    private static readonly Dictionary<ChatDeckChannel, uint> Defaults = Build();

    public static uint Default(ChatDeckChannel channel) =>
        Defaults.TryGetValue(channel, out uint argb) ? argb : Argb(ChatDeckPalette.TextInk);

    private static Dictionary<ChatDeckChannel, uint> Build()
    {
        var map = new Dictionary<ChatDeckChannel, uint>();
        foreach (RetailLogTextType type in Enum.GetValues<RetailLogTextType>())
        {
            ChatDeckChannel channel = ChatDeckChannels.ByLogTextType((uint)type);
            if (!map.ContainsKey(channel) && RetailChatColorTable.TryGetColor((uint)type, out Vector4 colour))
                map[channel] = Argb(colour);
        }
        Borrow(map, RetailLogTextType.Social, ChatDeckChannel.Patron, ChatDeckChannel.Vassals, ChatDeckChannel.Monarch, ChatDeckChannel.CoVassals);
        Borrow(map, RetailLogTextType.Allegiance, ChatDeckChannel.Olthoi);
        Borrow(map, RetailLogTextType.Channel, ChatDeckChannel.OtherChannel);
        Borrow(map, RetailLogTextType.ClientLocal, ChatDeckChannel.Plugin);
        return map;
    }

    private static void Borrow(Dictionary<ChatDeckChannel, uint> map, RetailLogTextType type, params ChatDeckChannel[] channels)
    {
        if (!RetailChatColorTable.TryGetColor((uint)type, out Vector4 colour)) return;
        foreach (ChatDeckChannel c in channels) map.TryAdd(c, Argb(colour));
    }

    internal static uint Argb(Vector4 c) =>
        (uint)(Math.Clamp(c.W, 0f, 1f) * 255f + 0.5f) << 24 | (uint)(Math.Clamp(c.X, 0f, 1f) * 255f + 0.5f) << 16
        | (uint)(Math.Clamp(c.Y, 0f, 1f) * 255f + 0.5f) << 8 | (uint)(Math.Clamp(c.Z, 0f, 1f) * 255f + 0.5f);
}
```

(`RetailLogTextType`: find its namespace with `grep -rn "enum RetailLogTextType" src` and add the using.) If a `ByLogTextType` result is the fallback for unknown types (check its `_ =>` arm), skip types that are not explicitly mapped by also requiring `Enum.IsDefined`. The test only demands an opaque colour for every channel.

`ChatDeckSettingsBinding.cs`:

```csharp
using System.Globalization;
using AcDream.Core.Chat.Deck;

namespace AcDream.App.UI.Hud;

/// <summary>What the Chat Deck settings popup reads and writes: the appearance, the extra tab keys, the channel colours.</summary>
public sealed class ChatDeckSettingsBinding
{
    private readonly Func<ChatDeckModel?> _model;

    public ChatDeckSettingsBinding(Func<ChatDeckModel?> model)
    {
        _model = model;
        Colours = Enum.GetValues<ChatDeckChannel>().Select(c => new ColourRow(this, c)).ToList();
    }

    private ChatDeckAppearance A => _model()?.Settings.Appearance ?? new ChatDeckAppearance();
    private ChatDeckKeys K => _model()?.Settings.Keys ?? new ChatDeckKeys();
    private void Set(Func<ChatDeckAppearance, ChatDeckAppearance> change) { if (_model() is { } m) m.SetAppearance(change(m.Settings.Appearance)); }
    private void SetKeys(Func<ChatDeckKeys, ChatDeckKeys> change) { if (_model() is { } m) m.SetKeys(change(m.Settings.Keys)); }

    public int FontSize => A.FontSize;
    public Action<int> SetFontSize => v => Set(a => a with { FontSize = v });
    public int InputFontSize => A.InputFontSize;
    public Action<int> SetInputFontSize => v => Set(a => a with { InputFontSize = v });
    public double LineSpacing => Math.Round(A.LineSpacing, 1);
    public Action<double> SetLineSpacing => v => Set(a => a with { LineSpacing = (float)v });
    /// <summary>Background opacity as a percent.</summary>
    public int Opacity => (int)MathF.Round(A.BackgroundOpacity * 100f);
    public Action<int> SetOpacity => v => Set(a => a with { BackgroundOpacity = v / 100f });
    public int Timestamps => (int)A.Timestamps;
    public Action<int> SetTimestamps => v => Set(a => a with { Timestamps = (ChatDeckTimestamps)Math.Clamp(v, 0, 2) });
    public bool ChannelPrefix => A.ChannelPrefix;
    public Action<bool> SetChannelPrefix => v => Set(a => a with { ChannelPrefix = v });
    public bool WrapIndent => A.WrapIndent;
    public Action<bool> SetWrapIndent => v => Set(a => a with { WrapIndent = v });
    public bool FadeWhenIdle => A.FadeWhenIdle;
    public Action<bool> SetFadeWhenIdle => v => Set(a => a with { FadeWhenIdle = v });
    public int FadeAfterSeconds => A.FadeAfterSeconds;
    public Action<int> SetFadeAfterSeconds => v => Set(a => a with { FadeAfterSeconds = v });
    public bool Beep => A.BeepOnAlert;
    public Action<bool> SetBeep => v => Set(a => a with { BeepOnAlert = v });

    public string NextTabKey => K.NextTab;
    public Action<string> SetNextTabKey => v => SetKeys(k => k with { NextTab = v });
    public string PreviousTabKey => K.PreviousTab;
    public Action<string> SetPreviousTabKey => v => SetKeys(k => k with { PreviousTab = v });

    public IReadOnlyList<ColourRow> Colours { get; }
    public Action ResetColours => () => Set(a => a with { ChannelColours = new Dictionary<ChatDeckChannel, uint>() });
    public Action? Close { get; set; }

    internal uint ColourOf(ChatDeckChannel c) =>
        A.ChannelColours.TryGetValue(c, out uint argb) ? argb : ChatDeckChannelColours.Default(c);

    internal void SetColour(ChatDeckChannel c, string hex)
    {
        if (!uint.TryParse(hex.TrimStart('#'), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint argb)) return;
        Set(a =>
        {
            var map = new Dictionary<ChatDeckChannel, uint>(a.ChannelColours);
            if (argb == ChatDeckChannelColours.Default(c)) map.Remove(c);
            else map[c] = argb;
            return a with { ChannelColours = map };
        });
    }

    /// <summary>One channel's colour in the popup's list.</summary>
    public sealed class ColourRow(ChatDeckSettingsBinding owner, ChatDeckChannel channel)
    {
        public string Id => channel.ToString();
        public string Name => ChatDeckChannels.DisplayName(channel);
        public string Hex => $"#{owner.ColourOf(channel):X8}";
        public string DefaultHex => $"#{ChatDeckChannelColours.Default(channel):X8}";
        public Action<string> SetHex => v => owner.SetColour(channel, v);
    }

    public const string Markup = """
        <panel x="90" y="80" w="340" h="560" title="Chat Deck settings" theme="plugin" layout="column" padding="10" gap="6">
          <group layout="row" gap="8" align="center" h="22">
            <label text="Font size" w="150" />
            <number value="{FontSize}" min="10" max="20" step="1" onchange="{SetFontSize}" w="70" />
          </group>
          <group layout="row" gap="8" align="center" h="22">
            <label text="Input font size" w="150" />
            <number value="{InputFontSize}" min="10" max="20" step="1" onchange="{SetInputFontSize}" w="70" />
          </group>
          <group layout="row" gap="8" align="center" h="22">
            <label text="Line spacing" w="150" />
            <number value="{LineSpacing}" min="1" max="1.6" step="0.1" decimals="1" onchange="{SetLineSpacing}" w="70" />
          </group>
          <group layout="row" gap="8" align="center" h="22">
            <label text="Background opacity %" w="150" />
            <number value="{Opacity}" min="0" max="100" step="5" onchange="{SetOpacity}" w="70" />
          </group>
          <group layout="row" gap="8" align="center" h="22">
            <label text="Timestamps" w="90" />
            <radio items="Off|HH:MM|HH:MM:SS" selected="{Timestamps}" onchange="{SetTimestamps}" orientation="horizontal" style="segmented" grow="1" h="20" />
          </group>
          <checkbox text="Show channel names ([General])" checked="{ChannelPrefix}" onchange="{SetChannelPrefix}" h="16" />
          <checkbox text="Indent wrapped lines" checked="{WrapIndent}" onchange="{SetWrapIndent}" h="16" />
          <group layout="row" gap="8" align="center" h="22">
            <checkbox text="Fade when idle after (s)" checked="{FadeWhenIdle}" onchange="{SetFadeWhenIdle}" w="190" h="16" />
            <number value="{FadeAfterSeconds}" min="1" max="600" step="1" onchange="{SetFadeAfterSeconds}" w="70" />
          </group>
          <checkbox text="Beep on alert" checked="{Beep}" onchange="{SetBeep}" h="16" />
          <separator h="1" />
          <group layout="row" gap="8" align="center" h="22">
            <label text="Next tab key" w="110" />
            <hotkey value="{NextTabKey}" onchange="{SetNextTabKey}" grow="1" h="20" />
          </group>
          <group layout="row" gap="8" align="center" h="22">
            <label text="Previous tab key" w="110" />
            <hotkey value="{PreviousTabKey}" onchange="{SetPreviousTabKey}" grow="1" h="20" />
          </group>
          <separator h="1" />
          <group layout="row" gap="8" align="center" h="22">
            <label text="Channel colours" grow="1" />
            <button text="Reset all" onclick="{ResetColours}" w="80" h="20" />
          </group>
          <group layout="column" gap="3" scroll="y" grow="1">
            <repeat items="{Colours}" key="{Id}">
              <group layout="row" gap="8" align="center" h="20">
                <label text="{Name}" color="{Hex}" grow="1" />
                <color value="{Hex}" onchange="{SetHex}" w="40" h="16" />
              </group>
            </repeat>
          </group>
          <group layout="row" gap="8" h="24">
            <group grow="1" />
            <button text="Close" onclick="{Close}" w="72" h="24" />
          </group>
        </panel>
        """;
}
```

If `Markup_Builds` throws, the message names the attribute or binding it could not use. Fix the markup to match what `MarkupDocument` accepts (compare with `samples/AcDream.Plugins.ControlsDemo/*.xml`, which use every control here), and do not change the binding's public names. A `<number>` bound to a `double` needs `decimals`; one bound to an `int` must not have it.

- [ ] **Step 4: Run and see them pass.**

- [ ] **Step 5: Commit**

```bash
git add src/AcDream.App/UI/Hud tests/AcDream.App.Tests/UI/Hud
git commit -m "Chat Deck: settings popup binding and markup"
```

---

### Task 9: Open the popup from the deck

**Goal:** In the real client, the ⚙ stud opens and closes the settings popup in front, on the session's current model. Hiding the deck (Modern HUD off) closes it.

**Files:**
- Modify: `src/AcDream.App/UI/RetailUiRuntime.cs` (`MountChatDeck` ~3615, the Chat Deck switch replacement's `OnHidden` ~564, `TogglePluginAppearance` ~4722, the `describeKey` local ~4618)
- Test: `tests/AcDream.App.Tests/UI/Hud/ChatDeckWiringTests.cs`

**Acceptance Criteria:**
- [ ] A `PluginAppearanceWindow` (the existing toggle host) builds `ChatDeckSettingsBinding.Markup` with a binding on `() => _chatDeckSession?.Model` and the runtime's asset, theme and key-name services; the deck's `OpenSettings` toggles it and `SettingsOpen` reports `IsOpen`
- [ ] The popup's Close button hides it; turning Modern HUD off (the replacement's `OnHidden`) hides it if open
- [ ] Hotkey boxes show retail key names: the `describeKey` the plugin windows use is a shared private method, used by both
- [ ] Wiring test: pressing the deck's settings stud with a toggle host bound like the runtime's shows a window over the root; pressing again hides it

**Verify:** `~/.dotnet/dotnet build && ~/.dotnet/dotnet test tests/AcDream.App.Tests --filter "FullyQualifiedName~ChatDeck"` → build succeeds, tests pass

**Steps:**

- [ ] **Step 1: Write the failing test.** In `ChatDeckWiringTests.World`, add a `PluginAppearanceWindow Settings` built as the runtime will build it, but with the test resolver:

```csharp
public readonly PluginAppearanceWindow SettingsWindow;
// in the constructor, before the Deck is built:
SettingsWindow = new PluginAppearanceWindow(Root, close =>
    MarkupDocument.Build(ChatDeckSettingsBinding.Markup, new ChatDeckSettingsBinding(() => Model) { Close = close }, _ => (0u, 0, 0)));
```

Pass `OpenSettings: () => SettingsWindow.Toggle(), SettingsOpen: () => SettingsWindow.IsOpen` into the deck's `ChatDeckBindings`. Because `SettingsWindow` must exist before the bindings capture it, build it first, or capture through a lambda as shown. Then:

```csharp
[Fact]
public void SettingsStud_TogglesThePopup_AndHidingTheDeckClosesIt()
{
    var w = new World(_path);
    w.Switch.Apply(true);
    PressDeck(w.Deck, w.Deck.Geometry.SettingsStud);
    Assert.True(w.SettingsWindow.IsOpen);
    PressDeck(w.Deck, w.Deck.Geometry.SettingsStud);
    Assert.False(w.SettingsWindow.IsOpen);
    PressDeck(w.Deck, w.Deck.Geometry.SettingsStud);
    w.Switch.Apply(false);
    Assert.False(w.SettingsWindow.IsOpen);
}
```

For the last assertion, the test's replacement `OnHidden` must close it like the runtime: add `if (SettingsWindow.IsOpen) SettingsWindow.Toggle();` to the `World`'s Chat Deck `OnHidden` lambda. That mirrors the runtime change, so the test pins the same behaviour.

- [ ] **Step 2: Run and see it fail** (nothing opens until the bindings are passed; adding them makes the first assertions pass). The runtime change below is the part this test guards by mirroring it.

- [ ] **Step 3: Implement in `RetailUiRuntime`.**
  1. Turn the `describeKey` local (lines ~4612–4622) into a private method that builds it once:
     ```csharp
     private Func<KeyChord, string>? _describeKey;
     private Func<KeyChord, string> DescribeKey()
     {
         if (_describeKey is not null) return _describeKey;
         Layout.RetailKeyNames keyNames;
         lock (_bindings.Assets.DatLock)
         {
             var keyStrings = new DatStringResolver(_bindings.Assets.Dats);
             keyNames = new Layout.RetailKeyNames((tableId, stringId) => keyStrings.Resolve(tableId, stringId));
         }
         return _describeKey = chord => MarkupHotkeyText.Describe(chord, key =>
         {
             lock (_bindings.Assets.DatLock)
                 return keyNames.Describe(key);
         });
     }
     ```
     Replace the local with `Func<KeyChord, string> describeKey = DescribeKey();`.
  2. Add the field `private PluginAppearanceWindow? _chatDeckSettings;` and:
     ```csharp
     private void ToggleChatDeckSettings()
     {
         _chatDeckSettings ??= new PluginAppearanceWindow(Host.Root, close =>
             MarkupDocument.Build(Hud.ChatDeckSettingsBinding.Markup,
                 new Hud.ChatDeckSettingsBinding(() => _chatDeckSession?.Model) { Close = close },
                 _bindings.Assets.ResolveSprite, _bindings.Assets.Controls, _bindings.Assets.DefaultFont,
                 themes: _pluginThemes, describeKey: DescribeKey()));
         _chatDeckSettings.Toggle();
     }

     private void CloseChatDeckSettings()
     {
         if (_chatDeckSettings is { IsOpen: true } open) open.Toggle();
     }
     ```
     (Check the session's model property name: `grep -n "public ChatDeckModel" src/AcDream.App/UI/Hud/ChatDeckSession.cs`.)
  3. In `MountChatDeck`'s `new Hud.ChatDeckBindings(...)`, add `OpenSettings: ToggleChatDeckSettings, SettingsOpen: () => _chatDeckSettings?.IsOpen == true`.
  4. In the Chat Deck replacement's `OnHidden` lambda (~line 564), call `CloseChatDeckSettings();` first.

- [ ] **Step 4: Build and run the tests and see them pass.**

- [ ] **Step 5: Commit**

```bash
git add src/AcDream.App/UI/RetailUiRuntime.cs tests/AcDream.App.Tests/UI/Hud/ChatDeckWiringTests.cs
git commit -m "Chat Deck: the settings stud opens the settings popup"
```

---

### Task 10: Full suite and live check

**Goal:** The whole Mac suite is green on the branch, and the user confirms on the local ACE server that every setting works.

> **USER-ORDERED GATE — NON-SKIPPABLE.** This task was requested by the user in the current conversation. It MUST NOT be closed by walking around it, by declaring it "verified inline", or by substituting a cheaper check. Close only after every item in `acceptanceCriteria` has been re-validated independently, with output captured.

**Files:**
- none (fixes found live get their own commits with tests)

**Acceptance Criteria:**
- [ ] `~/code/OpenAC/test-baseline/run-mac-tests.sh ~/code/OpenAC/OpenAC-chat-deck-appearance` exits 0 with no `Failed!` line
- [ ] Only after the user says they are available: the dev client is launched from the worktree against 10.10.20.20 (never Dreamweave). Launch it through a `.command` script opened with `open -a Terminal`, because UDP from Claude's shell gets "No route to host". The user confirms each of these: ⚙ opens and closes the popup; font size, input font size, line spacing, opacity, timestamps, channel names, wrap indent visibly change the deck; fade when idle fades only the background and the pointer brings it back; beep on alert sounds on a line naming the character (the user says whether `UI_TransientMessage` is the right sound); the next/previous tab keys work; a colour change and Reset all work; every setting survives a relog
- [ ] Anything the user reports as wrong is fixed with a test and rechecked

**Verify:** `~/code/OpenAC/test-baseline/run-mac-tests.sh ~/code/OpenAC/OpenAC-chat-deck-appearance; echo exit $?` → `exit 0`, then the user's confirmation in the conversation

**Steps:**

- [ ] **Step 1: Run the full suite** and capture the summary lines.
- [ ] **Step 2: Ask the user whether they are available for the live check.** Do not launch a client before they say so.
- [ ] **Step 3: Launch** the player client (and the admin client from `~/OpenAC-dev-admin` with `--admin --no-build --no-aethrix` if they want tells), as in the memory note `live-testing-rules`.
- [ ] **Step 4: Walk the checklist with the user** and record pass or fail for each item.
- [ ] **Step 5: Fix and recheck** anything that fails, then report. Merging into main and rynth happens after this, once the user agrees.
