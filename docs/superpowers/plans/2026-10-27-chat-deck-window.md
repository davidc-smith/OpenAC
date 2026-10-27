# Chat Deck Window Implementation Plan (plan 2 of 5)

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers-extended-cc:subagent-driven-development (recommended) or superpowers-extended-cc:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** The pinned Chat Deck window on screen while Modern HUD is on: tab bar with unread and alert badges, a coloured transcript with links, search and a "new lines" marker, an input with a per-tab send channel and a one-line channel override, the chat-entry actions routed to it, its settings saved per character, and the retail chat and floaties stepping aside while it is shown.

**Architecture:** Plan 1's pure `ChatDeckModel` (Core) is wrapped by an App-side `ChatDeckSession` (one model per logged-in character, ticked every frame) and `ChatDeckStorage` (the `chatDeck` JSON in the settings file). `UiChatDeck` is a native element in `src/AcDream.App/UI/Hud` that draws the frame, tabs and studs itself and hosts a `UiText` transcript, a `UiField` input and two `UiMarkupContextMenu` menus as children. The wording and links come from a new `ChatLineProjector` that `RuntimeChatFeed` also uses. `ModernHudSwitch` becomes a list of replacements (Command Deck, Chat Deck), each with its own mounted state; the Chat Deck's retail windows keep their saved visibility through a new visibility hold in `RetailWindowLayoutPersistence`. Every chat-entry action goes through one `IChatEntryTarget`.

**Tech Stack:** C# / .NET 10, xUnit, the retained UI in `src/AcDream.App/UI` (no new libraries).

**Spec:** `docs/superpowers/specs/2026-10-26-chat-deck-design.md` (branch `docs/chat-deck-spec`). Read "Decisions", "ChatDeckModel (Core)" → *Read state* and *Send channel* and *Chat entry ownership*, "Settings and persistence", "Window → Pinned layout" and "Size and placement", and "Modern HUD switch" before starting. Plan 1 (`docs/superpowers/plans/2026-10-27-chat-deck-model.md`) built everything under `src/AcDream.Core/Chat/Deck/`; read `ChatDeckModel.cs`, `ChatDeckTabState.cs`, `ChatDeckSettings.cs`, `ChatDeckSettingsJson.cs` and `ChatDeckChannel.cs` there.

**Worktree:** implement in `~/code/OpenAC/OpenAC-chat-deck-window` on branch `feat/chat-deck-window`, created from `main` (plan 1 is merged there, main 9d4b65c0 or later).

**Running tests:** always through the Mac runner, narrowed with `MAC_TEST_FILTER` (never a second `--filter`):

```bash
MAC_TEST_FILTER='FullyQualifiedName~ChatDeck' ~/code/OpenAC/test-baseline/run-mac-tests.sh ~/code/OpenAC/OpenAC-chat-deck-window tests/AcDream.App.Tests/AcDream.App.Tests.csproj
```

Test projects used: `tests/AcDream.Core.Tests`, `tests/AcDream.Runtime.Tests`, `tests/AcDream.UI.Abstractions.Tests`, `tests/AcDream.App.Tests` (each `<name>/<name>.csproj`). `TreatWarningsAsErrors` is on.

## Global Constraints

- **Concurrent work.** Another session's Spell Deck (`feat/spell-deck`, not merged when this plan was written) also edits `ModernHudSwitch.cs`, `RetailUiRuntime.cs`, `RetailWindowLayoutPersistence.cs`, `SettingsStore.cs`, `WindowNames.cs`, `DeckFonts.cs` and adds `DeckChrome.cs`. Before Task 1, run `git log --oneline main -5` and `git branch --merged main | grep spell-deck`. If the Spell Deck has merged, branch from that main and: keep its `spellDeckMounted` / `magicMode` / `onSpellDeckHidden` behaviour in the reworked switch (Task 12; the Spell Deck is not a replacement, it shows with the Command Deck in magic mode), keep its persistence capture gates beside the new visibility hold (Task 11), and draw the frame through `DeckChrome` instead of `ChatDeckPalette` (Task 9). If it has not merged, build as written; whoever merges second resolves the same way. Never edit the Spell Deck's worktree or branch.
- Chat Deck UI code lives in `src/AcDream.App/UI/Hud/` (namespace `AcDream.App.UI.Hud`), tests in `tests/AcDream.App.Tests/UI/Hud/` (namespace `AcDream.App.Tests.UI.Hud`). Core additions stay in `src/AcDream.Core/Chat/Deck/` with no UI references.
- Retail chat behaviour is unchanged: `ChatWindowController`, `FloatingChatWindowController` and `ChatWindowState` keep their filters, wording and look. The only change to `ChatWindowController` is that it implements `IChatEntryTarget` (Task 7) with the methods it already has.
- `RuntimeChatFeed` output is byte-for-byte unchanged (Task 1 moves code, it does not change it).
- No plugin contract change; `ForkContractVersion` stays as it is.
- Fonts: the bundled sans through `DeckFonts` (never the monospace debug font). Transcript and input at `ChatDeckAppearance.FontSize` / `InputFontSize` (default 14), labels at 12.
- Window: name `WindowNames.ChatDeck = "chat-deck"`, resizable from every edge and corner, minimum 280×140, clamped on screen, geometry saved through the existing window layout; default size 460×220 at the retail chat's default place (left margin, bottom).
- Settings: per character under `character[toonKey].chatDeck`; never loaded or saved under the `"default"` key (`RuntimeSettingsController.DefaultToonKey`, made `internal` in Task 5). A save whose JSON is over `ChatDeckLimits.MaxSettingsJsonLength` is refused and logged once.
- The model's `Tick()` runs every frame while a character session lives, whether or not the window is shown (plan 1's contract: the queue is unbounded).
- Badges: gold pill with the unread count, shown as `99+` above 99; red pill while the tab's alert flag is set (it shows the same count).
- Plan 2 draws only the studs it uses: search ⌕. Filters & Tabs ⧩ and Settings ⚙ (plans 4–5), pin and collapse (plan 3) come with their plans. The tab right-click menu has *Mark as read* and *Mark all read* only; *+*, Rename, Edit tab… and Delete come with the editor (plan 5). The transcript's line right-click menu is plan 5.
- Commit messages are short, in the existing style (`Chat Deck: ...`), and end with the line `Claude-Session: https://claude.ai/code/session_01WFS4EHSNafq2P1qfUuh258`.
- Never launch the game client for a live check until the user says they are available; live checks use the local ACE server only (10.10.20.20 via `~/OpenAC-dev/dev-client.env`), never Dreamweave.

**User decisions (already made):**
- "A" — a new Modern HUD chat window, not an upgrade of retail chat.
- Own tabs built from channel groups, each with a default send channel ("A and yes").
- Text filtering: search, hide words, highlight words, per-tab text rules ("Yes, incl 5").
- RynthChat features 1–9 adopted (rule actions, standard presets, rule from a line, test box, copy lines, read state, mention alert, auto-scroll marker, input history) ("Looks good").
- Scrollback per tab, cleared on logout ("option A").
- Five plans, one per series; plan 2 is the window ("Ok let's do plan 2").

---

## File map

| File | Responsibility |
|---|---|
| `src/AcDream.Runtime/Chat/ChatLineProjector.cs` (new) | timestamp-free wording, tag spans and coordinate links for one entry |
| `src/AcDream.Runtime/Chat/RuntimeChatFeed.cs` (modify) | `BuildLine` uses the projector, then adds the retail timestamp |
| `src/AcDream.Runtime/Chat/RuntimeChatEntryOwner.cs` (modify) | `SubmitTo(text, channel, …)` for a front end with its own send channel |
| `src/AcDream.UI.Abstractions/Panels/Settings/SettingsStore.cs` (modify) | `LoadChatDeck` / `SaveChatDeck`; `SaveCharacter` keeps sibling keys |
| `src/AcDream.Core/Chat/Deck/ChatDeckTabState.cs` (modify) | `Version` and `Added` counters |
| `tests/AcDream.Core.Tests/Chat/Deck/ChatDeckPresetEndToEndTests.cs` (new) | presets through real `ChatLog` wording and classification |
| `src/AcDream.App/UI/Hud/ChatDeckStorage.cs` (new) | load/parse/log, serialize/size-check/save |
| `src/AcDream.App/UI/Hud/ChatDeckSession.cs` (new) | one model per character session; ticks it; saves its settings |
| `src/AcDream.App/UI/Hud/ChatDeckTranscript.cs` (new) | tab lines → wrapped, coloured `UiText` lines, runs, tags, keys, markers |
| `src/AcDream.App/UI/UiText.cs` (modify) | `LineMarkerProvider`: a 2 px bar left of a line |
| `src/AcDream.App/UI/UiField.cs` (modify) | `OnControlKey`: lets an owner take Ctrl+key before the field swallows it |
| `src/AcDream.App/UI/IChatEntryTarget.cs` (new) | the chat-entry actions, one front end at a time |
| `src/AcDream.App/UI/Hud/ChatDeckSend.cs` (new) | send-channel names and `ChatChannelKind` mapping |
| `src/AcDream.App/UI/Hud/ChatDeckGeometry.cs` (new) | rectangles for tabs, studs, transcript, marker, input row; badge text |
| `src/AcDream.App/UI/Hud/ChatDeckPalette.cs` (new) | the deck colours (until `DeckChrome` is on main) |
| `src/AcDream.App/UI/Hud/UiChatDeck.cs`, `UiChatDeck.Draw.cs`, `UiChatDeck.Input.cs` (new) | the element |
| `src/AcDream.App/UI/RetailWindowLayoutPersistence.cs` (modify) | visibility hold for replaced windows |
| `src/AcDream.App/UI/Hud/ModernHudSwitch.cs` (modify) | replacements list |
| `src/AcDream.App/UI/RetailUiRuntime.cs`, `WindowNames.cs`, `WindowPlacementGeometry.cs`, `Layout/ChatWindowController.cs`, `Composition/InteractionRetainedUiComposition.cs` (modify) | mount, wiring, entry target |

---

### Task 1: `ChatLineProjector`

**Goal:** One timestamp-free place that words an entry and reads its tags and coordinate links, used by `RuntimeChatFeed` (unchanged output) and later by the Chat Deck.

**Files:**
- Create: `src/AcDream.Runtime/Chat/ChatLineProjector.cs`
- Modify: `src/AcDream.Runtime/Chat/RuntimeChatFeed.cs` (`BuildLine(ChatEntry, bool)`, lines ~122-174)
- Test: `tests/AcDream.Runtime.Tests/Chat/ChatLineProjectorTests.cs`

**Acceptance Criteria:**
- [ ] `ChatLineProjector.Project(entry)` returns the text and spans `RuntimeChatFeed.BuildLine` produced before this change, without a timestamp, for a plain line, a player tell (tell tag), a plugin line with a coordinate pair, and a combat line showing a coordinate pair (not linked).
- [ ] `RuntimeChatFeed.BuildLine` calls the projector and only adds the timestamp; every existing `RuntimeChatFeedTests` test passes unchanged.

**Verify:** `MAC_TEST_FILTER='FullyQualifiedName~ChatLineProjector|FullyQualifiedName~RuntimeChatFeed' ~/code/OpenAC/test-baseline/run-mac-tests.sh ~/code/OpenAC/OpenAC-chat-deck-window tests/AcDream.Runtime.Tests/AcDream.Runtime.Tests.csproj` → all pass

**Steps:**

- [ ] **Step 1: Write the failing test**

```csharp
using AcDream.Core.Chat;
using AcDream.Runtime.Chat;

namespace AcDream.Runtime.Tests.Chat;

public sealed class ChatLineProjectorTests
{
    private const uint PlayerGuid = 0x5000000Au;

    public static TheoryData<string> Cases() => ["plain", "tell", "plugin-coord", "combat-coord", "channel"];

    private static ChatEntry Entry(string name) => name switch
    {
        "plain" => new ChatEntry(ChatKind.System, "", "The server says hi.", 0u, 0u),
        "tell" => new ChatEntry(ChatKind.Tell, "Bob", "meet me", PlayerGuid, 0u) { LogTextType = 0x03u },
        "plugin-coord" => new ChatEntry(ChatKind.System, "", "Go to 12.3N, 45.6E now", 0u, 0u) { AllowsMarkup = true },
        "combat-coord" => new ChatEntry(ChatKind.Combat, "", "You hit 12.3N, 45.6E for 5", 0u, 0u),
        "channel" => new ChatEntry(ChatKind.Channel, "Bob", "group up", PlayerGuid, 0x800u) { ChannelName = "Fellowship" },
        _ => throw new ArgumentOutOfRangeException(nameof(name)),
    };

    private static string Shape(IReadOnlyList<ChatTextSpan>? spans) =>
        spans is null ? "<none>" : string.Join("|", spans.Select(s => $"{s.Text}#{s.Tag?.ToString() ?? "-"}#{s.Role}"));

    [Theory]
    [MemberData(nameof(Cases))]
    public void Project_MatchesTheFeedWithoutTimestamps(string name)
    {
        ChatEntry entry = Entry(name);
        var feed = new RuntimeChatFeed(new ChatLog());   // no DisplayTimestampsSource: timestamps off

        RuntimeChatLine fed = feed.BuildLine(entry);
        ChatProjectedLine projected = ChatLineProjector.Project(entry);

        Assert.Equal(fed.Text, projected.Text);
        Assert.Equal(Shape(fed.Spans), Shape(projected.Spans));
    }

    [Fact]
    public void Tell_HasATellTagAroundTheSender()
    {
        ChatProjectedLine projected = ChatLineProjector.Project(Entry("tell"));
        Assert.Contains(projected.Spans!, s => s.Tag is { IsTell: true } && s.Text == "Bob");
        Assert.Equal("Bob tells you, \"meet me\"", projected.Text);
    }

    [Fact]
    public void CombatLine_DoesNotLinkItsCoordinates()
    {
        ChatProjectedLine projected = ChatLineProjector.Project(Entry("combat-coord"));
        Assert.True(projected.Spans is null || projected.Spans.All(s => s.Tag is null));
    }
}
```

If `ChatEntry` has no settable `AllowsMarkup` (check `src/AcDream.Core/Chat/ChatLog.cs` for the record's members), build the plugin line the way `RuntimeChatFeedTests` does (search it for `AllowsMarkup`).

- [ ] **Step 2: Run test to verify it fails**

Run: the Verify command. Expected: build error, `ChatLineProjector` / `ChatProjectedLine` not defined.

- [ ] **Step 3: Write the projector**

```csharp
using System.Collections.Generic;
using System.Linq;
using AcDream.Core.Chat;

namespace AcDream.Runtime.Chat;

/// <summary>One entry worded as it reads on screen, split at its tags, without a timestamp.</summary>
/// <param name="Spans">The text split at its tags (a tell tag, a coordinate link), or null when it has none.
/// The spans always rejoin to <paramref name="Text"/>.</param>
public readonly record struct ChatProjectedLine(string Text, IReadOnlyList<ChatTextSpan>? Spans);

/// <summary>
/// Words one chat entry and reads its tags and coordinate links. Every chat
/// front end starts here and adds its own timestamp, prefix and colours.
/// </summary>
public static class ChatLineProjector
{
    public static ChatProjectedLine Project(ChatEntry entry)
    {
        // Moved unchanged from RuntimeChatFeed.BuildLine: everything up to the timestamp.
        string markup = ChatLineWording.FormatTagged(entry);
        IReadOnlyList<ChatTextSpan>? spans;
        if (entry.AllowsMarkup)
        {
            // A plugin's own line: its link tags and escapes are read, and a
            // coordinate pair it writes in plain text is a link too.
            IReadOnlyList<ChatTextSpan> read = ChatTagMarkup.ParseLinks(markup);
            read = ChatCoordinateLinks.Link(read) ?? read;
            if (read.Any(static span => span.Tag is not null))
            {
                spans = read;
            }
            else
            {
                spans = null;
                markup = string.Concat(read.Select(static span => span.Text));
            }
        }
        else
        {
            // (keep the existing comment block from RuntimeChatFeed here verbatim)
            spans = ChatTagMarkup.ParseLine(markup);
            if (entry.Kind != ChatKind.Combat)
                spans = ChatCoordinateLinks.LinkShown(spans) ?? spans;
            if (!spans.Any(static span => span.Tag is not null))
            {
                markup = string.Concat(spans.Select(static span => span.Text));
                spans = null;
            }
        }
        string text = spans is null
            ? markup
            : string.Concat(spans.Select(span => span.Text));
        return new ChatProjectedLine(text, spans);
    }
}
```

Then replace the top of `RuntimeChatFeed.BuildLine(ChatEntry entry, bool timestamps)` (from `string markup = FormatTagged(entry);` down to the `string text = …;` statement) with:

```csharp
        ChatProjectedLine projected = ChatLineProjector.Project(entry);
        string text = projected.Text;
        IReadOnlyList<ChatTextSpan>? spans = projected.Spans;
```

leaving the `if (timestamps)` block and the `return new RuntimeChatLine(…)` exactly as they are. Move the long comment that sat in the `else` branch into the projector unchanged.

- [ ] **Step 4: Run tests to verify they pass**

Run: the Verify command. Expected: all pass, including every existing `RuntimeChatFeedTests` test.

- [ ] **Step 5: Commit**

```bash
git add src/AcDream.Runtime/Chat/ChatLineProjector.cs src/AcDream.Runtime/Chat/RuntimeChatFeed.cs tests/AcDream.Runtime.Tests/Chat/ChatLineProjectorTests.cs
git commit -m "Chat Deck: ChatLineProjector, the feed's wording without its timestamp

Claude-Session: https://claude.ai/code/session_01WFS4EHSNafq2P1qfUuh258"
```

---

### Task 2: `SettingsStore` keeps the `chatDeck` object

**Goal:** `SettingsStore` reads and replaces `character[toonKey].chatDeck` on its own, and `SaveCharacter` stops dropping keys it does not own.

**Files:**
- Modify: `src/AcDream.UI.Abstractions/Panels/Settings/SettingsStore.cs` (`SaveCharacter`, lines ~341-361; new methods after it)
- Test: `tests/AcDream.UI.Abstractions.Tests/Panels/Settings/SettingsStoreChatDeckTests.cs`

**Acceptance Criteria:**
- [ ] `SaveCharacter` keeps every other key in `character[toonKey]` (a saved `chatDeck` survives it).
- [ ] `SaveChatDeck(toonKey, json)` writes the object under `character[toonKey].chatDeck`, keeping the character's other keys, other characters and other sections.
- [ ] `LoadChatDeck(toonKey)` returns that object's JSON text, or null when the file, the character or the key is missing, or the file does not parse.
- [ ] `SaveChatDeck` with text that is not a JSON object throws `ArgumentException` and writes nothing.

**Verify:** `MAC_TEST_FILTER='FullyQualifiedName~SettingsStore' ~/code/OpenAC/test-baseline/run-mac-tests.sh ~/code/OpenAC/OpenAC-chat-deck-window tests/AcDream.UI.Abstractions.Tests/AcDream.UI.Abstractions.Tests.csproj` → all pass

**Steps:**

- [ ] **Step 1: Write the failing tests**

```csharp
using System.IO;
using System.Text.Json.Nodes;
using AcDream.UI.Abstractions.Panels.Settings;

namespace AcDream.UI.Abstractions.Tests.Panels.Settings;

public sealed class SettingsStoreChatDeckTests : System.IDisposable
{
    private readonly string _path = Path.Combine(
        Path.GetTempPath(), $"acdream-settings-test-{System.Guid.NewGuid():N}.json");

    public void Dispose()
    {
        if (File.Exists(_path)) File.Delete(_path);
    }

    private JsonObject Root() => (JsonObject)JsonNode.Parse(File.ReadAllText(_path))!;

    [Fact]
    public void Missing_ReturnsNull()
    {
        var store = new SettingsStore(_path);
        Assert.Null(store.LoadChatDeck("Davian"));
        store.SaveCharacter("Davian", CharacterSettings.Default);
        Assert.Null(store.LoadChatDeck("Davian"));
    }

    [Fact]
    public void SaveThenLoad_RoundTripsTheObject()
    {
        var store = new SettingsStore(_path);
        store.SaveChatDeck("Davian", """{"version":1,"tabs":[],"extra":{"a":2}}""");

        string? json = store.LoadChatDeck("Davian");
        var loaded = (JsonObject)JsonNode.Parse(json!)!;
        Assert.Equal(1, (int)loaded["version"]!);
        Assert.Equal(2, (int)loaded["extra"]!["a"]!);
    }

    [Fact]
    public void SaveCharacter_KeepsChatDeck_AndSaveChatDeck_KeepsCharacterKeys()
    {
        var store = new SettingsStore(_path);
        store.SaveCharacter("Davian", CharacterSettings.Default with { AutoAttack = true });
        store.SaveChatDeck("Davian", """{"version":1}""");
        store.SaveCharacter("Davian", CharacterSettings.Default with { ConfirmSalvage = false });

        Assert.NotNull(store.LoadChatDeck("Davian"));
        Assert.False(store.LoadCharacter("Davian").ConfirmSalvage);
        store.SaveChatDeck("Davian", """{"version":2}""");
        Assert.False(store.LoadCharacter("Davian").ConfirmSalvage);
        Assert.Equal(2, (int)Root()["character"]!["Davian"]!["chatDeck"]!["version"]!);
    }

    [Fact]
    public void SaveCharacter_KeepsUnknownCharacterKeys()
    {
        var store = new SettingsStore(_path);
        store.SaveCharacter("Davian", CharacterSettings.Default);
        JsonObject root = Root();
        root["character"]!["Davian"]!["fromNewerBuild"] = "kept";
        File.WriteAllText(_path, root.ToJsonString());

        store.SaveCharacter("Davian", CharacterSettings.Default with { AutoAttack = true });
        Assert.Equal("kept", (string)Root()["character"]!["Davian"]!["fromNewerBuild"]!);
    }

    [Fact]
    public void OtherCharactersAndSections_AreKept()
    {
        var store = new SettingsStore(_path);
        store.SaveChat(ChatSettings.Default);
        store.SaveChatDeck("Bob", """{"version":1}""");
        store.SaveChatDeck("Davian", """{"version":1}""");
        Assert.NotNull(store.LoadChatDeck("Bob"));
        Assert.NotNull(Root()["chat"]);
    }

    [Fact]
    public void NotAnObject_ThrowsAndWritesNothing()
    {
        var store = new SettingsStore(_path);
        Assert.Throws<System.ArgumentException>(() => store.SaveChatDeck("Davian", "[1,2]"));
        Assert.Throws<System.ArgumentException>(() => store.SaveChatDeck("Davian", "{oops"));
        Assert.False(File.Exists(_path));
    }

    [Fact]
    public void UnparseableFile_LoadsNull()
    {
        File.WriteAllText(_path, "{ not json");
        Assert.Null(new SettingsStore(_path).LoadChatDeck("Davian"));
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: the Verify command. Expected: build error (`LoadChatDeck` / `SaveChatDeck` missing).

- [ ] **Step 3: Implement**

In `SaveCharacter`, replace building a fresh `toonObj` with updating the existing one:

```csharp
        JsonObject root = LoadMutableRoot();
        JsonObject characterMap = GetOrCreateObject(root, "character");
        // Only these four keys are the character settings'; anything else in
        // the object (the Chat Deck's, a newer build's) is kept.
        JsonObject toonObj = GetOrCreateObject(characterMap, toonKey);
        toonObj["autoAttack"]         = settings.AutoAttack;
        toonObj["confirmSalvage"]     = settings.ConfirmSalvage;
        toonObj["defaultChatChannel"] = settings.DefaultChatChannel;
        toonObj["showPickupMessages"] = settings.ShowPickupMessages;
        root["version"] = CurrentSchemaVersion;
        WriteMutableRoot(root);
```

Add after `SaveCharacter`:

```csharp
    /// <summary>
    /// The Chat Deck's saved object for one character, as JSON text, read from
    /// <c>character[toonKey].chatDeck</c>; null when there is none or the file
    /// cannot be read. The Chat Deck parses and checks it itself.
    /// </summary>
    public string? LoadChatDeck(string toonKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(toonKey);
        if (!File.Exists(_path)) return null;
        try
        {
            var root = JsonNode.Parse(ReadSettingsText()) as JsonObject;
            return root?["character"]?[toonKey]?["chatDeck"] is JsonObject deck ? deck.ToJsonString() : null;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"settings: failed to load chat deck from {_path}: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Replaces <c>character[toonKey].chatDeck</c> with <paramref name="json"/>,
    /// keeping the character's other keys, other characters and every other section.
    /// </summary>
    /// <exception cref="ArgumentException">The text is not a JSON object.</exception>
    public void SaveChatDeck(string toonKey, string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(toonKey);
        ArgumentNullException.ThrowIfNull(json);
        JsonObject deck;
        try
        {
            deck = JsonNode.Parse(json) as JsonObject
                ?? throw new ArgumentException("The Chat Deck settings are not a JSON object.", nameof(json));
        }
        catch (JsonException ex)
        {
            throw new ArgumentException("The Chat Deck settings are not valid JSON.", nameof(json), ex);
        }

        JsonObject root = LoadMutableRoot();
        GetOrCreateObject(GetOrCreateObject(root, "character"), toonKey)["chatDeck"] = deck;
        root["version"] = CurrentSchemaVersion;
        WriteMutableRoot(root);
    }
```

(`JsonException` is `System.Text.Json.JsonException`; add the `using` if the file lacks it.)

- [ ] **Step 4: Run tests to verify they pass**

Run: the Verify command. Expected: all `SettingsStore*` tests pass (existing ones included).

- [ ] **Step 5: Commit**

```bash
git add src/AcDream.UI.Abstractions/Panels/Settings/SettingsStore.cs tests/AcDream.UI.Abstractions.Tests/Panels/Settings/SettingsStoreChatDeckTests.cs
git commit -m "Chat Deck: settings store keeps character.chatDeck; SaveCharacter keeps sibling keys

Claude-Session: https://claude.ai/code/session_01WFS4EHSNafq2P1qfUuh258"
```

---

### Task 3: Presets end to end through real wording

**Goal:** Prove a handful of presets catch the lines `ChatLog` really produces (real wording, real channel classification), so wording drift is caught by tests; fix any preset that does not.

**Files:**
- Test: `tests/AcDream.Core.Tests/Chat/Deck/ChatDeckPresetEndToEndTests.cs`
- Modify only if a case fails on the preset (not the test): `src/AcDream.Core/Chat/Deck/ChatDeckPresets.cs`

**Acceptance Criteria:**
- [ ] Seven presets — `social.general`, `social.bot_spam`, `combat.melee_dealt`, `cast.fizzle`, `npc.arbitrator_spam`, `item.salvage`, `social.emotes` — each set as a *Hide* rule, hide a line appended through the matching `ChatLog` method and keep a near-miss line, observed through `ChatDeckModel` (`FindTab("all").Count`).
- [ ] Each case asserts its hit line reached `ChatLog` (the log is not what dropped it).
- [ ] A preset changed to pass keeps its RynthCore attribution comment and has its `Version` raised by one.

**Verify:** `MAC_TEST_FILTER='FullyQualifiedName~ChatDeckPreset' ~/code/OpenAC/test-baseline/run-mac-tests.sh ~/code/OpenAC/OpenAC-chat-deck-window tests/AcDream.Core.Tests/AcDream.Core.Tests.csproj` → all pass

**Steps:**

- [ ] **Step 1: Write the tests**

```csharp
using AcDream.Core.Chat;
using AcDream.Core.Chat.Deck;

namespace AcDream.Core.Tests.Chat.Deck;

/// <summary>
/// Presets against lines as ChatLog words and classifies them, not hand-made facts:
/// a change in wording or channels that breaks a preset fails here.
/// </summary>
public sealed class ChatDeckPresetEndToEndTests
{
    private const uint PlayerGuid = 0x50000002u;
    private const uint NpcGuid = 0x80000123u;

    private static readonly Dictionary<string, (Action<ChatLog> Hit, Action<ChatLog> Miss)> Cases = new()
    {
        ["social.general"] = (
            l => l.OnChannelBroadcast(0u, "Bob", "anyone around?", (uint)RetailLogTextType.TurbineGeneral, "General"),
            l => l.OnLocalSpeech("Bob", "[General] is quiet", PlayerGuid, isRanged: false, (uint)RetailLogTextType.Speech)),
        ["social.bot_spam"] = (
            l => l.OnLocalSpeech("BuffBot", "Buffs at the Holtburg lifestone -b-", PlayerGuid, isRanged: false, (uint)RetailLogTextType.Speech),
            l => l.OnLocalSpeech("BuffBot", "Buffs at the Holtburg lifestone", PlayerGuid, isRanged: false, (uint)RetailLogTextType.Speech)),
        ["combat.melee_dealt"] = (
            l => l.OnCombatLine("You slash Drudge Slinker for 54 points of slashing damage!", (uint)RetailLogTextType.CombatSelf),
            l => l.OnLocalSpeech("Bob", "You slash for fun", PlayerGuid, isRanged: false, (uint)RetailLogTextType.Speech)),
        ["cast.fizzle"] = (
            l => l.OnSystemMessage("Your spell fizzled.", (uint)RetailLogTextType.Magic),
            l => l.OnTellReceived("Bob", "Your spell fizzled again", PlayerGuid, (uint)RetailLogTextType.Tell)),
        ["npc.arbitrator_spam"] = (
            l => l.OnTellReceived("Master Arbitrator", "You shall be known as a champion!", NpcGuid, (uint)RetailLogTextType.Tell),
            l => l.OnTellReceived("Master Arbitrator", "Well done!", NpcGuid, (uint)RetailLogTextType.Tell)),
        ["item.salvage"] = (
            l => l.OnSystemMessage("You obtain 12 Steel (ws 7.00) using your knowledge of Weapon Tinkering.", (uint)RetailLogTextType.Salvaging),
            l => l.OnLocalSpeech("Bob", "You obtain 12 Steel (ws 7.00) using your knowledge of Weapon Tinkering.", PlayerGuid, isRanged: false, (uint)RetailLogTextType.Speech)),
        ["social.emotes"] = (
            l => l.OnEmote("Bob", "waves.", PlayerGuid),
            l => l.OnLocalSpeech("Bob", "waves", PlayerGuid, isRanged: false, (uint)RetailLogTextType.Speech)),
    };

    [Theory]
    [InlineData("social.general")]
    [InlineData("social.bot_spam")]
    [InlineData("combat.melee_dealt")]
    [InlineData("cast.fizzle")]
    [InlineData("npc.arbitrator_spam")]
    [InlineData("item.salvage")]
    [InlineData("social.emotes")]
    public void HidesTheRealLine_KeepsTheNearMiss(string presetId)
    {
        Assert.NotNull(ChatDeckPresets.Get(presetId));
        var settings = ChatDeckDefaults.Create() with
        {
            Rules =
            [
                new ChatDeckRule
                {
                    Id = "p", Kind = ChatDeckMatchKind.Preset, PresetId = presetId,
                    Action = ChatDeckRuleAction.Hide,
                },
            ],
        };
        var log = new ChatLog();
        using var model = new ChatDeckModel(log, settings, "Davian");
        (Action<ChatLog> hit, Action<ChatLog> miss) = Cases[presetId];

        hit(log);
        Assert.Single(log.Snapshot());
        model.Tick();
        Assert.Equal(0, model.FindTab(ChatDeckDefaults.AllTabId)!.Count);

        miss(log);
        model.Tick();
        Assert.Equal(1, model.FindTab(ChatDeckDefaults.AllTabId)!.Count);
    }
}
```

Check each `RetailLogTextType` member name against `src/AcDream.Core/Chat/RetailLogTextType.cs` (or wherever `grep -rn "enum RetailLogTextType" src` points) and each `ChatLog` method's parameters against `src/AcDream.Core/Chat/ChatLog.cs` before running.

- [ ] **Step 2: Run the tests**

Run: the Verify command. Expected: pass. A failing case means either the test called `ChatLog` wrongly (fix the call) or the preset does not match the real line.

- [ ] **Step 3: Fix any preset that misses its real line**

For each preset that fails on its *hit* (or catches its *miss*), print the real line with a throwaway assertion (`Assert.Equal("", ChatLineWording.Format(log.Snapshot()[0]))`) and the channel (`ChatDeckChannels.Classify(log.Snapshot()[0])`), then fix the preset in `ChatDeckPresets.cs`: its pattern, or its channel scope. Raise that preset's version argument by one, add the real line to its `Catches` (or the near miss to its `Spares`), and keep the file's attribution header. Remove the throwaway assertion. Re-run until everything passes, including `ChatDeckPresetsTests`.

- [ ] **Step 4: Commit**

```bash
git add tests/AcDream.Core.Tests/Chat/Deck/ChatDeckPresetEndToEndTests.cs src/AcDream.Core/Chat/Deck/ChatDeckPresets.cs
git commit -m "Chat Deck: presets checked end to end through ChatLog wording

Claude-Session: https://claude.ai/code/session_01WFS4EHSNafq2P1qfUuh258"
```

---

### Task 4: `ChatDeckStorage` — load, size check, save

**Goal:** Turn the saved `chatDeck` text into settings and back, keeping unknown keys, logging a bad object once, and refusing a save that would be too large to load again.

**Files:**
- Create: `src/AcDream.App/UI/Hud/ChatDeckStorage.cs`
- Test: `tests/AcDream.App.Tests/UI/Hud/ChatDeckStorageTests.cs`

**Acceptance Criteria:**
- [ ] `Load(key)` with nothing saved returns `ChatDeckDefaults.Create()` and logs nothing.
- [ ] `Load(key)` with a malformed or over-size object returns defaults and logs one line naming the character; the stored text is not rewritten by the load.
- [ ] `Save(key, settings)` serializes with the last loaded (or saved) text as `previousJson`, so unknown keys survive a load–save round trip.
- [ ] A save whose JSON is longer than `ChatDeckLimits.MaxSettingsJsonLength` writes nothing, returns false and logs once per session (per `Load`); later over-size saves stay silent.
- [ ] A store that throws on save (`IOException`, `UnauthorizedAccessException`) is caught, logged and reported as false.
- [ ] With no store (null), `Load` returns defaults and `Save` returns false silently.

**Verify:** `MAC_TEST_FILTER='FullyQualifiedName~ChatDeckStorage' ~/code/OpenAC/test-baseline/run-mac-tests.sh ~/code/OpenAC/OpenAC-chat-deck-window tests/AcDream.App.Tests/AcDream.App.Tests.csproj` → all pass

**Steps:**

- [ ] **Step 1: Write the failing tests**

```csharp
using System.Text.Json.Nodes;
using AcDream.App.UI.Hud;
using AcDream.Core.Chat.Deck;

namespace AcDream.App.Tests.UI.Hud;

public sealed class ChatDeckStorageTests
{
    private sealed class MemoryStore : IChatDeckStore
    {
        public readonly Dictionary<string, string> Saved = [];
        public int Saves;
        public Exception? Throw;
        public string? Load(string toonKey) => Saved.TryGetValue(toonKey, out string? json) ? json : null;
        public void Save(string toonKey, string json)
        {
            if (Throw is not null) throw Throw;
            Saves++;
            Saved[toonKey] = json;
        }
    }

    private static (ChatDeckStorage Storage, MemoryStore Store, List<string> Log) Make()
    {
        var store = new MemoryStore();
        var log = new List<string>();
        return (new ChatDeckStorage(store, log.Add), store, log);
    }

    [Fact]
    public void NothingSaved_LoadsDefaultsQuietly()
    {
        var (storage, _, log) = Make();
        ChatDeckSettings settings = storage.Load("Davian");
        Assert.Equal(ChatDeckDefaults.Create().Tabs.Select(t => t.Id), settings.Tabs.Select(t => t.Id));
        Assert.Empty(log);
    }

    [Fact]
    public void Malformed_LoadsDefaults_LogsOnce_LeavesTheTextAlone()
    {
        var (storage, store, log) = Make();
        store.Saved["Davian"] = "{\"tabs\": 5, ";
        storage.Load("Davian");
        Assert.Single(log);
        Assert.Contains("Davian", log[0]);
        Assert.Equal("{\"tabs\": 5, ", store.Saved["Davian"]);
        Assert.Equal(0, store.Saves);
    }

    [Fact]
    public void UnknownKeys_SurviveLoadAndSave()
    {
        var (storage, store, _) = Make();
        string first = ChatDeckSettingsJson.Serialize(ChatDeckDefaults.Create(), previousJson: null);
        var root = (JsonObject)JsonNode.Parse(first)!;
        root["fromNewerBuild"] = 7;
        store.Saved["Davian"] = root.ToJsonString();

        ChatDeckSettings settings = storage.Load("Davian");
        Assert.True(storage.Save("Davian", settings with { Layout = settings.Layout with { LastTabId = "combat" } }));
        var saved = (JsonObject)JsonNode.Parse(store.Saved["Davian"])!;
        Assert.Equal(7, (int)saved["fromNewerBuild"]!);
        Assert.Equal("combat", (string)saved["layout"]!["lastTab"]!);
    }

    [Fact]
    public void OverSize_IsRefused_LoggedOncePerSession()
    {
        var (storage, store, log) = Make();
        storage.Load("Davian");
        ChatDeckSettings huge = ChatDeckDefaults.Create() with
        {
            Rules = Enumerable.Range(0, ChatDeckLimits.MaxRules).Select(i => new ChatDeckRule
            {
                Id = "r" + i, Kind = ChatDeckMatchKind.Contains, Pattern = new string('x', 2000),
                Action = ChatDeckRuleAction.Hide,
            }).ToList(),
        };

        Assert.False(storage.Save("Davian", huge));
        Assert.False(storage.Save("Davian", huge));
        Assert.Equal(0, store.Saves);
        Assert.Single(log);

        storage.Load("Davian");
        Assert.False(storage.Save("Davian", huge));
        Assert.Equal(2, log.Count);
    }

    [Fact]
    public void StoreFailure_IsCaughtAndLogged()
    {
        var (storage, store, log) = Make();
        storage.Load("Davian");
        store.Throw = new IOException("disk full");
        Assert.False(storage.Save("Davian", ChatDeckDefaults.Create()));
        Assert.Contains(log, l => l.Contains("disk full"));
    }

    [Fact]
    public void NoStore_DefaultsAndNoSave()
    {
        var log = new List<string>();
        var storage = new ChatDeckStorage(null, log.Add);
        Assert.NotEmpty(storage.Load("Davian").Tabs);
        Assert.False(storage.Save("Davian", ChatDeckDefaults.Create()));
        Assert.Empty(log);
    }
}
```

(The 200 × 2,000-character patterns are well over 256 KB once serialized; the engine never compiles them because nothing ticks.)

- [ ] **Step 2: Run tests to verify they fail**

Run: the Verify command. Expected: build error (`ChatDeckStorage`, `IChatDeckStore` missing).

- [ ] **Step 3: Implement**

```csharp
using AcDream.Core.Chat.Deck;
using AcDream.UI.Abstractions.Panels.Settings;

namespace AcDream.App.UI.Hud;

/// <summary>Where the Chat Deck's JSON is kept: the settings file, or a stand-in in tests.</summary>
internal interface IChatDeckStore
{
    string? Load(string toonKey);
    void Save(string toonKey, string json);
}

internal sealed class SettingsStoreChatDeckStore(SettingsStore store) : IChatDeckStore
{
    public string? Load(string toonKey) => store.LoadChatDeck(toonKey);
    public void Save(string toonKey, string json) => store.SaveChatDeck(toonKey, json);
}

/// <summary>
/// The Chat Deck's settings for one character at a time: read once at login and
/// written whenever they change. Keys this build does not know are carried from
/// the text last read or written. A save that would come out larger than the
/// loader accepts is refused, so a bad save never resets the character's tabs.
/// </summary>
internal sealed class ChatDeckStorage(IChatDeckStore? store, Action<string> log)
{
    private string? _previousJson;
    private bool _reportedOverSize;

    public ChatDeckSettings Load(string toonKey)
    {
        _reportedOverSize = false;
        _previousJson = null;
        if (store is null) return ChatDeckDefaults.Create();

        string? json = store.Load(toonKey);
        ChatDeckSettingsJson.ParseResult result = ChatDeckSettingsJson.Parse(json);
        if (result.Error is { } error)
        {
            // Left on disk as it is; the first save replaces it.
            log($"[UI] Chat Deck: settings for {toonKey} could not be read; using defaults. {error}");
            return result.Settings;
        }
        _previousJson = json;
        return result.Settings;
    }

    public bool Save(string toonKey, ChatDeckSettings settings)
    {
        if (store is null) return false;
        string json = ChatDeckSettingsJson.Serialize(settings, _previousJson);
        if (json.Length > ChatDeckLimits.MaxSettingsJsonLength)
        {
            if (!_reportedOverSize)
            {
                _reportedOverSize = true;
                log($"[UI] Chat Deck: settings for {toonKey} are larger than {ChatDeckLimits.MaxSettingsJsonLength} characters; not saved.");
            }
            return false;
        }
        try
        {
            store.Save(toonKey, json);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            log($"[UI] Chat Deck: saving settings for {toonKey} failed: {ex.Message}");
            return false;
        }
        _previousJson = json;
        return true;
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: the Verify command. Expected: all pass.

- [ ] **Step 5: Commit**

```bash
git add src/AcDream.App/UI/Hud/ChatDeckStorage.cs tests/AcDream.App.Tests/UI/Hud/ChatDeckStorageTests.cs
git commit -m "Chat Deck: storage with unknown-key carry-over and a save-size check

Claude-Session: https://claude.ai/code/session_01WFS4EHSNafq2P1qfUuh258"
```

---

### Task 5: `ChatDeckSession` — one model per character

**Goal:** Create the Chat Deck model when a character is in the world, tick it every frame, save its settings when they change, and drop it (history, unread) when the session ends.

**Files:**
- Create: `src/AcDream.App/UI/Hud/ChatDeckSession.cs`
- Modify: `src/AcDream.App/Settings/RuntimeSettingsController.cs:133` (`DefaultToonKey` from `private` to `internal`)
- Test: `tests/AcDream.App.Tests/UI/Hud/ChatDeckSessionTests.cs`

**Acceptance Criteria:**
- [ ] No model while the character key is blank or `"default"`, or while gameplay is not on screen; nothing is loaded or saved under `"default"`.
- [ ] The first `Tick` with a real key and gameplay on creates a model with that character's saved settings and that name; `ModelChanged` fires.
- [ ] `Tick` ticks the model every call, so lines appended to the `ChatLog` reach its tabs whether or not anything draws.
- [ ] The model's `SettingsChanged` saves through the storage under the model's own key.
- [ ] `EndSession()` disposes the model (it stops receiving lines) and fires `ModelChanged`; the next `Tick` for the same character starts a fresh, empty model.
- [ ] A key change (another character) disposes the old model and starts one for the new key.
- [ ] `Save()` writes the current model's settings (used after `ApplySettings` by later plans).

**Verify:** `MAC_TEST_FILTER='FullyQualifiedName~ChatDeckSession' ~/code/OpenAC/test-baseline/run-mac-tests.sh ~/code/OpenAC/OpenAC-chat-deck-window tests/AcDream.App.Tests/AcDream.App.Tests.csproj` → all pass

**Steps:**

- [ ] **Step 1: Write the failing tests**

```csharp
using AcDream.App.UI.Hud;
using AcDream.Core.Chat;
using AcDream.Core.Chat.Deck;

namespace AcDream.App.Tests.UI.Hud;

public sealed class ChatDeckSessionTests
{
    private sealed class MemoryStore : IChatDeckStore
    {
        public readonly Dictionary<string, string> Saved = [];
        public readonly List<string> Loads = [];
        public string? Load(string toonKey) { Loads.Add(toonKey); return Saved.GetValueOrDefault(toonKey); }
        public void Save(string toonKey, string json) => Saved[toonKey] = json;
    }

    private sealed class Harness
    {
        public readonly ChatLog Log = new();
        public readonly MemoryStore Store = new();
        public string Key = "default";
        public bool Gameplay = true;
        public int Changes;
        public readonly ChatDeckSession Session;

        public Harness()
        {
            Session = new ChatDeckSession(Log, new ChatDeckStorage(Store, _ => { }), () => Key, () => Gameplay);
            Session.ModelChanged += () => Changes++;
        }

        public void Say(string text) =>
            Log.OnLocalSpeech("Bob", text, 0x50000002u, isRanged: false, logTextType: 0x02u);
    }

    [Fact]
    public void DefaultKeyOrNoGameplay_NoModel()
    {
        var h = new Harness();
        h.Session.Tick();
        Assert.Null(h.Session.Model);
        h.Key = "Davian";
        h.Gameplay = false;
        h.Session.Tick();
        Assert.Null(h.Session.Model);
        Assert.Empty(h.Store.Loads);
    }

    [Fact]
    public void RealCharacter_StartsWithItsSettings_AndTicks()
    {
        var h = new Harness();
        h.Store.Saved["Davian"] = ChatDeckSettingsJson.Serialize(
            ChatDeckDefaults.Create() with { Layout = new ChatDeckLayoutState { LastTabId = "combat" } }, null);
        h.Key = "Davian";

        h.Session.Tick();
        ChatDeckModel model = Assert.IsType<ChatDeckModel>(h.Session.Model);
        Assert.Equal("Davian", model.CharacterName);
        Assert.Equal("combat", model.SelectedTabId);
        Assert.Equal(1, h.Changes);

        h.Say("hello");
        h.Session.Tick();
        Assert.Equal(1, model.FindTab("all")!.Count);
    }

    [Fact]
    public void SettingsChanged_Saves()
    {
        var h = new Harness { Key = "Davian" };
        h.Session.Tick();
        h.Session.Model!.SelectTab("group");
        Assert.Contains("\"group\"", h.Store.Saved["Davian"]);
    }

    [Fact]
    public void EndSession_DropsTheModel_SameCharacterStartsEmpty()
    {
        var h = new Harness { Key = "Davian" };
        h.Session.Tick();
        ChatDeckModel first = h.Session.Model!;
        h.Say("before");
        h.Session.Tick();

        h.Session.EndSession();
        Assert.Null(h.Session.Model);
        Assert.Equal(2, h.Changes);
        h.Say("while gone");
        first.Tick();
        Assert.Equal(1, first.FindTab("all")!.Count);   // disposed: no longer listening

        h.Session.Tick();
        Assert.NotSame(first, h.Session.Model);
        Assert.Equal(0, h.Session.Model!.FindTab("all")!.Count);
    }

    [Fact]
    public void AnotherCharacter_ReplacesTheModel()
    {
        var h = new Harness { Key = "Davian" };
        h.Session.Tick();
        ChatDeckModel first = h.Session.Model!;
        h.Key = "Bob";
        h.Session.Tick();
        Assert.Equal("Bob", h.Session.Model!.CharacterName);
        Assert.NotSame(first, h.Session.Model);
        Assert.Equal(["Davian", "Bob"], h.Store.Loads);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: the Verify command. Expected: build error (`ChatDeckSession` missing).

- [ ] **Step 3: Implement**

Change `RuntimeSettingsController.DefaultToonKey` to `internal const string DefaultToonKey = "default";`.

```csharp
using AcDream.App.Settings;
using AcDream.Core.Chat;
using AcDream.Core.Chat.Deck;

namespace AcDream.App.UI.Hud;

/// <summary>
/// The Chat Deck's model for the character in the world. It starts on the first
/// tick a real character is on screen and ends when the session does (logout,
/// character switch, disconnect), so history and unread never outlive a login.
/// <see cref="Tick"/> runs every frame, shown or not: the model queues every line.
/// </summary>
internal sealed class ChatDeckSession(
    ChatLog log,
    ChatDeckStorage storage,
    Func<string> characterKey,
    Func<bool> gameplayActive) : IDisposable
{
    private string? _key;

    public ChatDeckModel? Model { get; private set; }

    /// <summary>A model started or ended; the window drops what it drew from the old one.</summary>
    public event Action? ModelChanged;

    public void Tick()
    {
        string key = characterKey();
        bool character = gameplayActive()
            && !string.IsNullOrWhiteSpace(key)
            && key != RuntimeSettingsController.DefaultToonKey;
        if (character && key != _key)
        {
            Stop();
            Start(key);
        }
        Model?.Tick();
    }

    /// <summary>The session ended; the next character (or the same one again) starts empty.</summary>
    public void EndSession()
    {
        if (Model is null) return;
        Stop();
        ModelChanged?.Invoke();
    }

    /// <summary>Saves the model's settings as they stand (after the caller applied new ones).</summary>
    public void Save()
    {
        if (Model is { } model && _key is { } key) storage.Save(key, model.Settings);
    }

    public void Dispose() => Stop();

    private void Start(string key)
    {
        _key = key;
        Model = new ChatDeckModel(log, storage.Load(key), key);
        Model.SettingsChanged += OnSettingsChanged;
        ModelChanged?.Invoke();
    }

    private void Stop()
    {
        if (Model is { } model)
        {
            model.SettingsChanged -= OnSettingsChanged;
            model.Dispose();
        }
        Model = null;
        _key = null;
    }

    private void OnSettingsChanged(ChatDeckSettings settings)
    {
        if (_key is { } key) storage.Save(key, settings);
    }
}
```

Note: `Start` after `Stop` fires `ModelChanged` once (from `Start`); `EndSession` fires it once.

- [ ] **Step 4: Run tests to verify they pass**

Run: the Verify command. Expected: all pass.

- [ ] **Step 5: Commit**

```bash
git add src/AcDream.App/UI/Hud/ChatDeckSession.cs src/AcDream.App/Settings/RuntimeSettingsController.cs tests/AcDream.App.Tests/UI/Hud/ChatDeckSessionTests.cs
git commit -m "Chat Deck: one model per character session, ticked every frame

Claude-Session: https://claude.ai/code/session_01WFS4EHSNafq2P1qfUuh258"
```

---

### Task 6: Transcript layout

**Goal:** Turn one tab's lines into the wrapped, coloured lines a `UiText` draws — timestamp, channel or rule colour, tell and coordinate links, matched text coloured, a left bar on highlighted lines, search filtering — cached so a busy tab does not re-word its history every frame.

**Files:**
- Modify: `src/AcDream.Core/Chat/Deck/ChatDeckTabState.cs` (add `Version`, `Added`)
- Create: `src/AcDream.App/UI/Hud/ChatDeckTranscript.cs`
- Modify: `src/AcDream.App/UI/UiText.cs` (`LineMarkerProvider`, drawn in the line loop near line 489)
- Test: `tests/AcDream.Core.Tests/Chat/Deck/ChatDeckTabStateTests.cs` (add cases), `tests/AcDream.App.Tests/UI/Hud/ChatDeckTranscriptTests.cs`, `tests/AcDream.App.Tests/UI/UiTextLineMarkerTests.cs`

**Acceptance Criteria:**
- [ ] `ChatDeckTabState.Version` changes on every `Add` and `Clear`; `Added` counts lines added since the last `Clear` (it keeps counting past the ring's capacity).
- [ ] Each source line becomes one or more visual lines wrapped to the width, oldest first; each carries `LineKey(entry.Sequence, offset)`.
- [ ] Timestamps: `Off` none, `HourMinute` `"H:mm "`, `HourMinuteSecond` `"H:mm:ss "` in local time (invariant culture), drawn in the timestamp colour.
- [ ] Line colour: the verdict's `LineColour` if set, otherwise the supplied channel colour function.
- [ ] A tell tag around a speaker is drawn in the tag colour and listed in that visual line's tag ranges (offsets relative to the visual line, timestamp included).
- [ ] With a `MatchColour`, the matched characters (field `Text`, `Sender` or `Line`) are drawn in it; tags keep the tag colour.
- [ ] A line with a `LineColour` gets that colour as the marker of each of its visual lines; others get null.
- [ ] A non-empty search keeps only source lines whose shown text contains it (ordinal, ignoring case).
- [ ] The projection of a line is computed once per line, and its wrap once per (width, font, timestamp mode).
- [ ] `UiText.LineMarkerProvider` draws a 2 px wide, line-high bar at `x = Padding + MarginLeft - 4` for each visible line it returns a colour for.

**Verify:** `MAC_TEST_FILTER='FullyQualifiedName~ChatDeckTabState' ~/code/OpenAC/test-baseline/run-mac-tests.sh ~/code/OpenAC/OpenAC-chat-deck-window tests/AcDream.Core.Tests/AcDream.Core.Tests.csproj` and `MAC_TEST_FILTER='FullyQualifiedName~ChatDeckTranscript|FullyQualifiedName~UiTextLineMarker' ~/code/OpenAC/test-baseline/run-mac-tests.sh ~/code/OpenAC/OpenAC-chat-deck-window tests/AcDream.App.Tests/AcDream.App.Tests.csproj` → all pass

**Steps:**

- [ ] **Step 1: Tab counters (test first)**

Add to `ChatDeckTabStateTests`:

```csharp
    [Fact]
    public void VersionAndAdded_TrackAddsAndClears()
    {
        var tab = new ChatDeckTabState("t", capacity: 2);
        long v0 = tab.Version;
        tab.Add(Line("a"), seen: true);
        tab.Add(Line("b"), seen: true);
        tab.Add(Line("c"), seen: true);
        Assert.Equal(3, tab.Added);
        Assert.Equal(2, tab.Count);
        Assert.Equal(v0 + 3, tab.Version);

        tab.Clear();
        Assert.Equal(0, tab.Added);
        Assert.Equal(v0 + 4, tab.Version);
    }
```

(Use the file's existing helper for making a `ChatDeckLine`; if it has none, build one with `new ChatDeckLine(ChatDeckLineFacts.From(new ChatEntry(ChatKind.System, "", text, 0u, 0u)), verdict)` the way the file's other tests do.)

Then in `ChatDeckTabState`:

```csharp
    /// <summary>Changes on every add and clear; a reader that laid the tab out keeps its copy while this is unchanged.</summary>
    public long Version { get; private set; }
    /// <summary>Lines added since the last clear, including those the ring has since dropped.</summary>
    public long Added { get; private set; }
```

`Add` ends its bookkeeping with `Version++; Added++;` (before the `if (seen) return;`), `Clear` with `Added = 0; Version++;`. Run the Core verify command: pass.

- [ ] **Step 2: Write the failing transcript tests**

```csharp
using System.Globalization;
using System.Numerics;
using AcDream.App.UI;
using AcDream.App.UI.Hud;
using AcDream.Core.Chat;
using AcDream.Core.Chat.Deck;

namespace AcDream.App.Tests.UI.Hud;

public sealed class ChatDeckTranscriptTests
{
    private static readonly Vector4 Channel = new(0.8f, 0.8f, 0.8f, 1f);
    private static readonly Vector4 Tag = new(0.4f, 0.8f, 1f, 1f);
    private static readonly Vector4 Stamp = new(0.5f, 0.5f, 0.5f, 1f);
    private const uint PlayerGuid = 0x5000000Au;

    private static ChatDeckTranscriptStyle Style(
        float width = 1000f, ChatDeckTimestamps stamps = ChatDeckTimestamps.Off, string search = "") =>
        new(width, s => s.Length * 7f, FontKey: null, stamps, _ => Channel, Tag, Stamp, search);

    private static ChatDeckLine Line(ChatEntry entry, uint? lineColour = null, uint? matchColour = null,
        IReadOnlyList<ChatDeckSpan>? spans = null)
    {
        var facts = ChatDeckLineFacts.From(entry, "Davian");
        var verdict = new ChatDeckVerdict(
            Hidden: false, Destinations: ["all"], LineColour: lineColour, MatchColour: matchColour,
            MatchSpans: spans ?? [], Alert: false, Hits: [], TimedOutRuleIds: []);
        return new ChatDeckLine(facts, verdict);
    }

    private static ChatEntry Sys(string text, long seq = 1) =>
        new(ChatKind.System, "", text, 0u, 0u) { Sequence = seq };

    [Fact]
    public void Wraps_AndKeysByEntry()
    {
        var t = new ChatDeckTranscript();
        ChatDeckTranscriptLayout layout = t.Build([Line(Sys("aaaa bbbb cccc", seq: 42))], Style(width: 35f));

        Assert.True(layout.Lines.Count >= 2);
        Assert.All(layout.Keys, k => Assert.Equal(42, k.Source));
        Assert.Equal(0, layout.Keys[0].Start);
        Assert.Equal("aaaa bbbb cccc", string.Join(" ", layout.Lines.Select(l => l.Text.Trim())));
    }

    [Fact]
    public void Timestamp_PrefixInTimestampColour()
    {
        var received = new DateTime(2026, 10, 27, 9, 5, 7, DateTimeKind.Utc);
        var entry = Sys("hello") with { Received = received };
        var t = new ChatDeckTranscript();
        string expected = received.ToLocalTime().ToString(@"H\:mm\:ss ", CultureInfo.InvariantCulture);

        ChatDeckTranscriptLayout layout = t.Build([Line(entry)], Style(stamps: ChatDeckTimestamps.HourMinuteSecond));

        Assert.StartsWith(expected, layout.Lines[0].Text);
        IReadOnlyList<UiText.TextRun> runs = layout.Runs[0]!;
        Assert.Equal(expected, runs[0].Text);
        Assert.Equal(Stamp, runs[0].Color);
    }

    [Fact]
    public void LineColour_OverridesChannel_AndMarksEveryVisualLine()
    {
        uint red = 0xFFFF0000u;
        var t = new ChatDeckTranscript();
        ChatDeckTranscriptLayout layout = t.Build([Line(Sys("aaaa bbbb cccc"), lineColour: red)], Style(width: 35f));

        var expected = new Vector4(1f, 0f, 0f, 1f);
        Assert.All(layout.Lines, l => Assert.Equal(expected, l.Color));
        Assert.All(layout.Markers, m => Assert.Equal(expected, m));
    }

    [Fact]
    public void PlainLine_HasNoMarker_AndChannelColour()
    {
        ChatDeckTranscriptLayout layout = new ChatDeckTranscript().Build([Line(Sys("plain"))], Style());
        Assert.Null(layout.Markers[0]);
        Assert.Equal(Channel, layout.Lines[0].Color);
    }

    [Fact]
    public void TellTag_IsColouredAndListed()
    {
        var tell = new ChatEntry(ChatKind.Tell, "Bob", "meet me", PlayerGuid, 0u) { LogTextType = 0x03u };
        ChatDeckTranscriptLayout layout = new ChatDeckTranscript().Build([Line(tell)], Style());

        Assert.Contains(layout.Runs[0]!, r => r.Text == "Bob" && r.Color == Tag);
        var (start, length, tag) = Assert.Single(layout.Tags[0]!);
        Assert.Equal((0, 3), (start, length));
        Assert.True(tag.IsTell);
    }

    [Fact]
    public void MatchColour_ColoursTheMatchedText()
    {
        uint gold = 0xFFFFD700u;
        var entry = new ChatEntry(ChatKind.LocalSpeech, "Bob", "I found a key", PlayerGuid, 0u) { LogTextType = 0x02u };
        // "key" in the Text field: offset 10, length 3
        ChatDeckLine line = Line(entry, matchColour: gold, spans: [new ChatDeckSpan(ChatDeckMatchField.Text, 10, 3)]);

        ChatDeckTranscriptLayout layout = new ChatDeckTranscript().Build([line], Style());

        var expected = new Vector4(1f, 215f / 255f, 0f, 1f);
        Assert.Contains(layout.Runs[0]!, r => r.Text == "key" && r.Color == expected);
    }

    [Fact]
    public void Search_KeepsMatchingLinesOnly()
    {
        ChatDeckTranscriptLayout layout = new ChatDeckTranscript().Build(
            [Line(Sys("the drudge", 1)), Line(Sys("a banderling", 2))], Style(search: "DRUDGE"));
        Assert.Single(layout.Lines);
        Assert.Equal(1, layout.Keys[0].Source);
    }

    [Fact]
    public void ProjectsEachLineOnce()
    {
        var t = new ChatDeckTranscript();
        ChatDeckLine line = Line(Sys("once"));
        t.Build([line], Style());
        t.Build([line], Style(width: 500f));
        Assert.Equal(1, t.ProjectionsForTest);
    }
}
```

Check `ChatDeckVerdict`'s constructor (`src/AcDream.Core/Chat/Deck/ChatDeckRuleEngine.cs:6`) and `ChatEntry`'s `Sequence`/`Received` members (`ChatLog.cs`), and adjust the `Line` helper's argument names to match.

- [ ] **Step 3: Run tests to verify they fail**

Run: the App verify command. Expected: build error (`ChatDeckTranscript` missing).

- [ ] **Step 4: Implement `ChatDeckTranscript`**

```csharp
using System.Globalization;
using System.Numerics;
using System.Runtime.CompilerServices;
using AcDream.App.UI.Layout;
using AcDream.Core.Chat;
using AcDream.Core.Chat.Deck;
using AcDream.Runtime.Chat;

namespace AcDream.App.UI.Hud;

/// <summary>How a transcript is drawn: wrap width and font, timestamps, colours, and the search text ("" for none).</summary>
/// <param name="FontKey">The font the widths were measured with; a new font wraps again.</param>
internal readonly record struct ChatDeckTranscriptStyle(
    float WrapWidth,
    Func<string, float> Measure,
    object? FontKey,
    ChatDeckTimestamps Timestamps,
    Func<ChatDeckLine, Vector4> ChannelColour,
    Vector4 TagColour,
    Vector4 TimestampColour,
    string Search);

/// <summary>A tab laid out for a <see cref="UiText"/>: one entry per visual line in every list.</summary>
internal sealed class ChatDeckTranscriptLayout
{
    public List<UiText.Line> Lines { get; } = [];
    public List<IReadOnlyList<UiText.TextRun>?> Runs { get; } = [];
    public List<IReadOnlyList<(int Start, int Length, ChatTextTag Tag)>?> Tags { get; } = [];
    public List<UiText.LineKey> Keys { get; } = [];
    public List<Vector4?> Markers { get; } = [];
}

/// <summary>
/// Lays a tab's lines out for the transcript. A line is worded once (it is shared by
/// every tab it is in) and wrapped again only when the width, font or timestamp
/// mode changes, so a busy tab costs a copy per frame, not a re-wording.
/// </summary>
internal sealed class ChatDeckTranscript
{
    private sealed class Cached
    {
        public required ChatProjectedLine Projected;
        public float WrapWidth = float.NaN;
        public object? FontKey;
        public ChatDeckTimestamps Stamps;
        public string Stamp = "";
        public string[] Fragments = [];
        public int[] Starts = [];
    }

    private readonly ConditionalWeakTable<ChatDeckLine, Cached> _cache = new();

    internal int ProjectionsForTest { get; private set; }

    public ChatDeckTranscriptLayout Build(IReadOnlyList<ChatDeckLine> lines, ChatDeckTranscriptStyle style)
    {
        var layout = new ChatDeckTranscriptLayout();
        foreach (ChatDeckLine line in lines)
        {
            Cached c = Get(line);
            if (style.Search.Length > 0
                && c.Projected.Text.IndexOf(style.Search, StringComparison.OrdinalIgnoreCase) < 0)
                continue;
            Wrap(c, line, style);
            AddLine(layout, line, c, style);
        }
        return layout;
    }

    private Cached Get(ChatDeckLine line)
    {
        if (_cache.TryGetValue(line, out Cached? cached)) return cached;
        ProjectionsForTest++;
        cached = new Cached { Projected = ChatLineProjector.Project(line.Entry) };
        _cache.AddOrUpdate(line, cached);
        return cached;
    }

    private static void Wrap(Cached c, ChatDeckLine line, ChatDeckTranscriptStyle style)
    {
        if (c.WrapWidth.Equals(style.WrapWidth) && ReferenceEquals(c.FontKey, style.FontKey) && c.Stamps == style.Timestamps)
            return;
        c.WrapWidth = style.WrapWidth;
        c.FontKey = style.FontKey;
        c.Stamps = style.Timestamps;
        c.Stamp = Stamp(line.Entry.Received, style.Timestamps);
        string full = c.Stamp + c.Projected.Text;
        var fragments = new List<string>();
        var starts = new List<int>();
        int searchFrom = 0;
        foreach (string frag in ChatTranscriptRenderer.WrapText(full, style.WrapWidth, style.Measure))
        {
            int at = frag.Length == 0 ? searchFrom : full.IndexOf(frag, searchFrom, StringComparison.Ordinal);
            if (at < 0) at = searchFrom;
            else if (frag.Length > 0) searchFrom = at + frag.Length;
            fragments.Add(frag);
            starts.Add(at);
        }
        c.Fragments = [.. fragments];
        c.Starts = [.. starts];
    }

    internal static string Stamp(DateTime receivedUtc, ChatDeckTimestamps mode) => mode switch
    {
        ChatDeckTimestamps.HourMinute => receivedUtc.ToLocalTime().ToString(@"H\:mm ", CultureInfo.InvariantCulture),
        ChatDeckTimestamps.HourMinuteSecond => receivedUtc.ToLocalTime().ToString(@"H\:mm\:ss ", CultureInfo.InvariantCulture),
        _ => "",
    };

    private static void AddLine(ChatDeckTranscriptLayout layout, ChatDeckLine line, Cached c, ChatDeckTranscriptStyle style)
    {
        Vector4 lineColour = line.LineColour is uint argb ? Argb(argb) : style.ChannelColour(line);
        Vector4? marker = line.LineColour is uint bar ? Argb(bar) : null;

        // The line's spans with the timestamp in front, as the feed builds them, so tag
        // ranges and colours are measured over the text as shown.
        var spans = new List<ChatTextSpan>();
        if (c.Stamp.Length > 0) spans.Add(new ChatTextSpan(c.Stamp, null, ChatSpanRole.Timestamp));
        if (c.Projected.Spans is { } own) spans.AddRange(own);
        else spans.Add(new ChatTextSpan(c.Projected.Text, null));

        Vector4[] colours = Colours(line, c, spans, lineColour, style);
        for (int i = 0; i < c.Fragments.Length; i++)
        {
            string frag = c.Fragments[i];
            int at = c.Starts[i];
            layout.Lines.Add(new UiText.Line(frag, lineColour));
            layout.Runs.Add(Runs(frag, at, colours, lineColour));
            layout.Tags.Add(frag.Length == 0 ? null : ChatTranscriptRenderer.TaggedRangesForFragment(spans, at, frag.Length));
            layout.Keys.Add(new UiText.LineKey(line.Sequence, Math.Max(0, at - c.Stamp.Length)));
            layout.Markers.Add(marker);
        }
    }

    /// <summary>One colour per character of the shown line (timestamp included).</summary>
    private static Vector4[] Colours(
        ChatDeckLine line, Cached c, List<ChatTextSpan> spans, Vector4 lineColour, ChatDeckTranscriptStyle style)
    {
        int stamp = c.Stamp.Length;
        var colours = new Vector4[stamp + c.Projected.Text.Length];
        Array.Fill(colours, lineColour);
        for (int i = 0; i < stamp; i++) colours[i] = style.TimestampColour;

        if (line.MatchColour is uint match)
        {
            Vector4 ink = Argb(match);
            foreach (ChatDeckSpan span in line.MatchSpans)
            {
                int origin = span.Field switch
                {
                    ChatDeckMatchField.Text => c.Projected.Text.LastIndexOf(line.Entry.Text, StringComparison.Ordinal),
                    ChatDeckMatchField.Sender => line.Entry.Sender.Length == 0 ? -1
                        : c.Projected.Text.IndexOf(line.Entry.Sender, StringComparison.Ordinal),
                    _ => 0,
                };
                if (origin < 0) continue;
                int from = stamp + origin + span.Start;
                int to = Math.Min(colours.Length, from + span.Length);
                for (int i = Math.Max(stamp, from); i < to; i++) colours[i] = ink;
            }
        }

        int at = 0;
        foreach (ChatTextSpan span in spans)
        {
            if (span.Tag is { } tag && ChatTranscriptRenderer.DrawsInTagColor(tag))
                for (int i = at; i < at + span.Text.Length && i < colours.Length; i++) colours[i] = style.TagColour;
            at += span.Text.Length;
        }
        return colours;
    }

    /// <summary>The fragment as runs of one colour; null when it is all the line colour.</summary>
    private static IReadOnlyList<UiText.TextRun>? Runs(string frag, int at, Vector4[] colours, Vector4 lineColour)
    {
        if (frag.Length == 0) return null;
        var runs = new List<UiText.TextRun>();
        bool plain = true;
        int start = 0;
        for (int i = 1; i <= frag.Length; i++)
        {
            if (i < frag.Length && colours[at + i] == colours[at + start]) continue;
            Vector4 colour = colours[at + start];
            plain &= colour == lineColour;
            runs.Add(new UiText.TextRun(frag[start..i], colour));
            start = i;
        }
        return plain ? null : runs;
    }

    internal static Vector4 Argb(uint argb) => new(
        ((argb >> 16) & 0xFF) / 255f, ((argb >> 8) & 0xFF) / 255f, (argb & 0xFF) / 255f, ((argb >> 24) & 0xFF) / 255f);
}
```

`ChatTranscriptRenderer.TaggedRangesForFragment` and `DrawsInTagColor` are `internal static` in `AcDream.App.UI.Layout`; reuse them, do not copy them. `WrapText` is public there.

- [ ] **Step 5: Line marker in `UiText` (test first)**

```csharp
using System.Numerics;
using AcDream.App.UI;
using DatReaderWriter.Types;   // FontCharDesc

namespace AcDream.App.Tests.UI;

public sealed class UiTextLineMarkerTests
{
    private static UiDatFont Font()
    {
        var glyphs = new Dictionary<char, FontCharDesc>();
        foreach (char c in "ABCDEFGH") glyphs[c] = new FontCharDesc { Unicode = c, Width = 6, Height = 8 };
        return new UiDatFont(fgTex: 1u, fgW: 32, fgH: 32, bgTex: 0, bgW: 0, bgH: 0,
            lineHeight: 16f, baselineOffset: 12f, glyphs);
    }

    [Fact]
    public void Marker_IsABarLeftOfTheLine()
    {
        var marker = new Vector4(0.9f, 0.1f, 0.2f, 1f);
        var text = new UiText
        {
            Width = 200, Height = 60, DatFont = Font(), Padding = 2f, MarginLeft = 6f,
            LinesProvider = () => [new UiText.Line("ABC", Vector4.One), new UiText.Line("DEF", Vector4.One)],
            LineMarkerProvider = i => i == 1 ? marker : null,
        };

        var bounds = MarkupDrawProbe.QuadBounds(MarkupDrawProbe.Draw(text, 200, 60), marker);

        var (x, _, w, h) = Assert.Single(bounds);
        Assert.Equal(2f + 6f - 4f, x, 2);
        Assert.Equal(2f, w, 2);
        Assert.Equal(16f, h, 2);
    }
}
```

In `UiText` add, next to `LineRunsProvider`:

```csharp
    /// <summary>A colour for a 2 px bar left of a line (a highlighted chat line), or null for none.</summary>
    public Func<int, Vector4?>? LineMarkerProvider { get; set; }
```

and in the draw loop, right after `if (!LineIntersectsViewport(y, lh, top, bottom)) continue;`:

```csharp
            if (LineMarkerProvider?.Invoke(i) is { } marker)
                ctx.DrawFill(Padding + MarginLeft - 4f, y, 2f, lh, marker);
```

- [ ] **Step 6: Run tests to verify they pass**

Run: both verify commands. Expected: all pass.

- [ ] **Step 7: Commit**

```bash
git add src/AcDream.Core/Chat/Deck/ChatDeckTabState.cs tests/AcDream.Core.Tests/Chat/Deck/ChatDeckTabStateTests.cs src/AcDream.App/UI/Hud/ChatDeckTranscript.cs src/AcDream.App/UI/UiText.cs tests/AcDream.App.Tests/UI/Hud/ChatDeckTranscriptTests.cs tests/AcDream.App.Tests/UI/UiTextLineMarkerTests.cs
git commit -m "Chat Deck: transcript layout (timestamps, colours, links, markers, search)

Claude-Session: https://claude.ai/code/session_01WFS4EHSNafq2P1qfUuh258"
```

---

### Task 7: Chat-entry plumbing

**Goal:** The pieces the Chat Deck input needs from the rest of the client: one `IChatEntryTarget` that every chat-entry action goes through (still the retail chat after this task), a way to send a line on a given channel through the shared entry owner, the send-channel names and mapping, and a Ctrl+key hook on `UiField`.

**Files:**
- Create: `src/AcDream.App/UI/IChatEntryTarget.cs`, `src/AcDream.App/UI/Hud/ChatDeckSend.cs`
- Modify: `src/AcDream.App/UI/Layout/ChatWindowController.cs` (implement the interface), `src/AcDream.App/UI/RetailUiRuntime.cs` (input actions ~lines 929-960, `FocusChatEntry` ~line 1116), `src/AcDream.Runtime/Chat/RuntimeChatEntryOwner.cs` (`SubmitTo`), `src/AcDream.App/UI/UiField.cs` (`OnControlKey`)
- Test: `tests/AcDream.Runtime.Tests/Chat/RuntimeChatEntryOwnerTests.cs` (add), `tests/AcDream.App.Tests/UI/Hud/ChatDeckSendTests.cs`, `tests/AcDream.App.Tests/UI/UiFieldControlKeyTests.cs`

**Acceptance Criteria:**
- [ ] `RuntimeChatEntryOwner.SubmitTo(text, channel, feedback, bus)` sends plain text as a `SendChatCmd` on `channel`, ignores the owner's own `ActiveChannel` and tell target (and leaves them unchanged), routes `/` and `@` commands through `ChatCommandRouter` as `Submit` does, remembers the line and clears the draft; blank text is `SubmitOutcome.Empty` and clears the draft.
- [ ] Every `ChatDeckSendChannel` maps to the `ChatChannelKind` of the same name and has a display name (`CoVassals` → "Co-vassals", `Lfg` → "LFG").
- [ ] `UiField.OnControlKey(key, shift)` runs for a key pressed with Ctrl held, before the field's own Ctrl handling; returning true consumes the key, false leaves the field's handling (Ctrl+A/C/X/V) as it was.
- [ ] `ChatWindowController` implements `IChatEntryTarget`; `RetailUiRuntime` sends `ChatReply`, `ChatMonarchReply`, `ChatPatronReply`, `ChatStartCommand`, `ChatTellToSelected`, `EnterChatMode`, `ToggleChatEntry` and `FocusChatEntry()` through a `ChatEntryTarget` property (the retail controller for now). Behaviour is unchanged: existing App tests pass.

**Verify:** `MAC_TEST_FILTER='FullyQualifiedName~RuntimeChatEntryOwner' ~/code/OpenAC/test-baseline/run-mac-tests.sh ~/code/OpenAC/OpenAC-chat-deck-window tests/AcDream.Runtime.Tests/AcDream.Runtime.Tests.csproj` and `MAC_TEST_FILTER='FullyQualifiedName~ChatDeckSend|FullyQualifiedName~UiFieldControlKey|FullyQualifiedName~ChatWindow' ~/code/OpenAC/test-baseline/run-mac-tests.sh ~/code/OpenAC/OpenAC-chat-deck-window tests/AcDream.App.Tests/AcDream.App.Tests.csproj` → all pass

**Steps:**

- [ ] **Step 1: `SubmitTo` (test first)**

Add to `RuntimeChatEntryOwnerTests` (it already has `RecordingBus` and `RecordingFeedback`):

```csharp
    [Fact]
    public void SubmitTo_SendsPlainTextOnTheGivenChannel_LeavingTheEntryAimed()
    {
        var owner = new RuntimeChatEntryOwner();
        owner.SetTellTarget("Bob", 0x50000001u);
        owner.SetDraft("group up");
        var bus = new RecordingBus();

        SubmitOutcome outcome = owner.SubmitTo(null, ChatChannelKind.Fellowship, new RecordingFeedback(), bus);

        Assert.Equal(SubmitOutcome.Sent, outcome);
        var sent = Assert.IsType<SendChatCmd>(Assert.Single(bus.Published));
        Assert.Equal(ChatChannelKind.Fellowship, sent.Channel);
        Assert.Equal("group up", sent.Text);
        Assert.Equal("", owner.Draft);
        Assert.Equal(1, owner.Count);
        Assert.Equal(ChatChannelKind.Tell, owner.ActiveChannel);
        Assert.Equal("Bob", owner.TellTarget);
    }

    [Fact]
    public void SubmitTo_Blank_IsEmptyAndClears()
    {
        var owner = new RuntimeChatEntryOwner();
        owner.SetDraft("   ");
        Assert.Equal(SubmitOutcome.Empty,
            owner.SubmitTo(null, ChatChannelKind.General, new RecordingFeedback(), new RecordingBus()));
        Assert.Equal("", owner.Draft);
        Assert.Equal(0, owner.Count);
    }
```

Implement in `RuntimeChatEntryOwner`, after `Submit`:

```csharp
    /// <summary>
    /// Sends a line the way <see cref="Submit"/> does, but with plain text aimed
    /// at <paramref name="channel"/> instead of the entry's own channel and tell
    /// target, which stay as they are: for a front end that decides where plain
    /// text goes itself (a Chat Deck tab). Commands are routed unchanged.
    /// </summary>
    public SubmitOutcome SubmitTo(
        string? text,
        ChatChannelKind channel,
        IChatCommandFeedback feedback,
        ICommandBus bus)
    {
        ArgumentNullException.ThrowIfNull(feedback);
        ArgumentNullException.ThrowIfNull(bus);
        string line = text ?? _draft;
        _composedDraftPending = false;
        if (line.Trim().Length == 0)
        {
            Clear();
            return SubmitOutcome.Empty;
        }

        SubmitOutcome outcome = ChatCommandRouter.Submit(line, feedback, bus, channel);
        Remember(line);
        Clear();
        return outcome;
    }
```

Run the Runtime verify command: pass.

- [ ] **Step 2: Send channels (test first)**

```csharp
using AcDream.App.UI.Hud;
using AcDream.Core.Chat.Deck;
using AcDream.Runtime.Chat;

namespace AcDream.App.Tests.UI.Hud;

public sealed class ChatDeckSendTests
{
    [Fact]
    public void EverySendChannel_MapsByName_AndHasAName()
    {
        foreach (ChatDeckSendChannel channel in Enum.GetValues<ChatDeckSendChannel>())
        {
            Assert.Equal(channel.ToString(), ChatDeckSend.ToKind(channel).ToString());
            Assert.False(string.IsNullOrWhiteSpace(ChatDeckSend.DisplayName(channel)));
        }
        Assert.Equal("Co-vassals", ChatDeckSend.DisplayName(ChatDeckSendChannel.CoVassals));
        Assert.Equal("LFG", ChatDeckSend.DisplayName(ChatDeckSendChannel.Lfg));
    }
}
```

```csharp
using AcDream.Core.Chat.Deck;
using AcDream.Runtime.Chat;

namespace AcDream.App.UI.Hud;

/// <summary>Where a Chat Deck line goes when it has no <c>/</c> command: names and router channels.</summary>
internal static class ChatDeckSend
{
    public static IReadOnlyList<ChatDeckSendChannel> All { get; } = Enum.GetValues<ChatDeckSendChannel>();

    public static ChatChannelKind ToKind(ChatDeckSendChannel channel) => channel switch
    {
        ChatDeckSendChannel.Say => ChatChannelKind.Say,
        ChatDeckSendChannel.Fellowship => ChatChannelKind.Fellowship,
        ChatDeckSendChannel.Allegiance => ChatChannelKind.Allegiance,
        ChatDeckSendChannel.Patron => ChatChannelKind.Patron,
        ChatDeckSendChannel.Vassals => ChatChannelKind.Vassals,
        ChatDeckSendChannel.Monarch => ChatChannelKind.Monarch,
        ChatDeckSendChannel.CoVassals => ChatChannelKind.CoVassals,
        ChatDeckSendChannel.General => ChatChannelKind.General,
        ChatDeckSendChannel.Trade => ChatChannelKind.Trade,
        ChatDeckSendChannel.Lfg => ChatChannelKind.Lfg,
        ChatDeckSendChannel.Roleplay => ChatChannelKind.Roleplay,
        ChatDeckSendChannel.Society => ChatChannelKind.Society,
        ChatDeckSendChannel.Olthoi => ChatChannelKind.Olthoi,
        _ => ChatChannelKind.Say,
    };

    public static string DisplayName(ChatDeckSendChannel channel) => channel switch
    {
        ChatDeckSendChannel.CoVassals => "Co-vassals",
        ChatDeckSendChannel.Lfg => "LFG",
        _ => channel.ToString(),
    };
}
```

- [ ] **Step 3: `UiField.OnControlKey` (test first)**

```csharp
using AcDream.App.Tests.Plugins;
using AcDream.App.UI;
using Silk.NET.Input;

namespace AcDream.App.Tests.UI;

public sealed class UiFieldControlKeyTests
{
    private static bool Press(UiField field, Key key) =>
        field.OnEvent(new UiEvent(0, field, UiEventType.KeyDown, Data0: (int)key));

    [Fact]
    public void ControlKey_GoesToTheOwnerFirst()
    {
        var keyboard = new FakeKeyboard();
        keyboard.Held.Add(Key.ControlLeft);
        keyboard.Held.Add(Key.ShiftLeft);
        var seen = new List<(Key, bool)>();
        var field = new UiField
        {
            Keyboard = keyboard, Selectable = true,
            OnControlKey = (key, shift) => { seen.Add((key, shift)); return key == Key.Tab; },
        };

        Assert.True(Press(field, Key.Tab));
        Assert.Equal([(Key.Tab, true)], seen);

        field.SetText("abc");
        Assert.True(Press(field, Key.A));   // not taken: the field selects all as before
        keyboard.Held.Clear();
        field.OnEvent(new UiEvent(0, field, UiEventType.Char, Data0: 'x'));
        Assert.Equal("x", field.Text);      // the typed character replaced the selection
    }
}
```

In `UiField`, next to `OnUp`/`OnDown`:

```csharp
    /// <summary>
    /// A key pressed with Ctrl held, offered to the owner before the field's own
    /// Ctrl handling (select all, copy, cut, paste); true when the owner took it.
    /// The second argument says whether Shift is held too.
    /// </summary>
    public Func<Silk.NET.Input.Key, bool, bool>? OnControlKey { get; set; }
```

and at the top of the `if (CtrlHeld())` block in `OnEvent` (KeyDown):

```csharp
                    if (OnControlKey?.Invoke(key, ShiftHeld()) == true)
                        return true;
```

- [ ] **Step 4: `IChatEntryTarget` and routing**

```csharp
using AcDream.UI.Abstractions.Input;

namespace AcDream.App.UI;

/// <summary>
/// The chat input the chat-entry actions act on: the retail chat window, or the
/// Chat Deck while it is shown. Every front end shares the one entry owner
/// (draft, recall history), so switching between them loses nothing.
/// </summary>
internal interface IChatEntryTarget
{
    /// <summary>Focus the input and select its text; the key that did it is not typed.</summary>
    void EnterChatMode(KeyChord? physicalChord);
    /// <summary>Focus the input, or give the keyboard back to the game if it has it.</summary>
    void ToggleChatEntry(KeyChord? physicalChord);
    /// <summary>Focus the input with "/" typed.</summary>
    void StartCommand();
    /// <summary>Focus the input with a tell to <paramref name="name"/> started.</summary>
    void StartTell(string name);
    /// <summary>A tell to the last person who spoke on that line, when there is one.</summary>
    void StartReply(string? name);
    /// <summary>Focus the input; false when it cannot take the keyboard.</summary>
    bool Focus();
}
```

`ChatWindowController` gains `IChatEntryTarget` in its base list and explicit implementations that forward to its existing methods:

```csharp
    void IChatEntryTarget.EnterChatMode(KeyChord? physicalChord) => EnterChatMode(physicalChord);
    void IChatEntryTarget.ToggleChatEntry(KeyChord? physicalChord) => ToggleChatEntry(physicalChord);
    void IChatEntryTarget.StartCommand() => StartCommand();
    void IChatEntryTarget.StartTell(string name) => StartTell(name);
    void IChatEntryTarget.StartReply(string? name) => StartReply(name);

    bool IChatEntryTarget.Focus()
    {
        if (FindRootOf(Input) is not { } root) return false;
        root.SetKeyboardFocus(Input);
        return true;
    }
```

In `RetailUiRuntime` add:

```csharp
    /// <summary>The chat input the chat-entry actions go to.</summary>
    private IChatEntryTarget? ChatEntryTarget => _chatWindowController;
```

and replace `_chatWindowController?.StartReply(…)`, `.StartCommand()`, `.StartTell(name)`, `.EnterChatMode(…)` and `.ToggleChatEntry(…)` in the input-action switch with `ChatEntryTarget?.…`. `FocusChatEntry()` becomes:

```csharp
    public void FocusChatEntry() => ChatEntryTarget?.Focus();
```

(Before, it focused `Host.Root.DefaultTextInput`, which is the retail chat input; same result.) Leave `ComposeChatText` and every other `_chatWindowController` use alone.

- [ ] **Step 5: Run tests**

Run: both verify commands, then `MAC_TEST_FILTER='FullyQualifiedName~Chat' ~/code/OpenAC/test-baseline/run-mac-tests.sh ~/code/OpenAC/OpenAC-chat-deck-window tests/AcDream.App.Tests/AcDream.App.Tests.csproj`. Expected: all pass.

- [ ] **Step 6: Commit**

```bash
git add src/AcDream.App/UI/IChatEntryTarget.cs src/AcDream.App/UI/Hud/ChatDeckSend.cs src/AcDream.App/UI/Layout/ChatWindowController.cs src/AcDream.App/UI/RetailUiRuntime.cs src/AcDream.Runtime/Chat/RuntimeChatEntryOwner.cs src/AcDream.App/UI/UiField.cs tests/AcDream.Runtime.Tests/Chat/RuntimeChatEntryOwnerTests.cs tests/AcDream.App.Tests/UI/Hud/ChatDeckSendTests.cs tests/AcDream.App.Tests/UI/UiFieldControlKeyTests.cs
git commit -m "Chat Deck: chat-entry target, SubmitTo a channel, send channels, Ctrl keys for fields

Claude-Session: https://claude.ai/code/session_01WFS4EHSNafq2P1qfUuh258"
```

---

### Task 8: Window geometry, badges, palette, icons and fonts

**Goal:** The pure layout of the window (tabs, search stud, search field, transcript, "new" marker, input row, channel chip) with hit-testing and badge text, plus the colours, two icons and the font sizes the element draws with.

**Files:**
- Create: `src/AcDream.App/UI/Hud/ChatDeckGeometry.cs`, `src/AcDream.App/UI/Hud/ChatDeckPalette.cs`, `assets/hud/search.svg`, `assets/hud/chevron-down.svg`
- Modify: `src/AcDream.App/UI/Hud/EmbeddedSvgSpriteCache.cs` (`HudIcons.Search`, `HudIcons.ChevronDown`, both in `All`), `src/AcDream.App/UI/Hud/DeckFonts.cs` (`Face(pixelHeight, weight)`)
- Test: `tests/AcDream.App.Tests/UI/Hud/ChatDeckGeometryTests.cs`, `tests/AcDream.App.Tests/UI/Hud/DeckFontsTests.cs` (add)

**Acceptance Criteria:**
- [ ] Tabs sit left to right on the top row at their natural width (name + padding + badge) when they fit; when they do not, every tab gets the same width, never under 34 px; tabs that still do not fit get width 0 and are not hit.
- [ ] The search stud is at the top right; with search open a 22 px field row sits under the tabs and the transcript starts below it.
- [ ] The input row is at the bottom (26 px), the chip at its left sized to its text, the input filling the rest; the transcript fills the space between.
- [ ] `HitAt(x, y)` returns the tab index for a tab, `SearchStud`, `Chip`, `Frame` for the rest of the window, `None` outside.
- [ ] `ChatDeckBadges.Text(n)`: `""` for 0 or less, the number up to 99, `"99+"` above.
- [ ] `HudIcons.Search` and `HudIcons.ChevronDown` load as embedded SVGs (the existing all-icons test covers them).
- [ ] `DeckFonts.Face(pixelHeight, weight)` bakes a face once per (whole-pixel height, weight).

**Verify:** `MAC_TEST_FILTER='FullyQualifiedName~ChatDeckGeometry|FullyQualifiedName~DeckFonts|FullyQualifiedName~EmbeddedSvgSpriteCache' ~/code/OpenAC/test-baseline/run-mac-tests.sh ~/code/OpenAC/OpenAC-chat-deck-window tests/AcDream.App.Tests/AcDream.App.Tests.csproj` → all pass

**Steps:**

- [ ] **Step 1: Write the failing tests**

```csharp
using AcDream.App.UI.Hud;

namespace AcDream.App.Tests.UI.Hud;

public sealed class ChatDeckGeometryTests
{
    private static float Measure(string s) => s.Length * 7f;

    private static ChatDeckGeometry Make(float w = 460, float h = 220, int tabs = 6, bool search = false, string chip = "Say") =>
        ChatDeckGeometry.Create(w, h,
            Enumerable.Range(0, tabs).Select(i => new ChatDeckTabLabel("Tab" + i, i == 1 ? "3" : "")).ToList(),
            search, chip, Measure);

    [Fact]
    public void Tabs_NaturalWidth_LeftToRight()
    {
        ChatDeckGeometry g = Make(tabs: 3);
        Assert.Equal(ChatDeckGeometry.Pad, g.Tabs[0].X);
        float plain = 2 * ChatDeckGeometry.TabPadX + Measure("Tab0");
        Assert.Equal(plain, g.Tabs[0].W, 2);
        Assert.True(g.Tabs[1].W > plain);   // the badge adds room
        Assert.Equal(g.Tabs[0].X + g.Tabs[0].W + ChatDeckGeometry.TabGap, g.Tabs[1].X, 2);
    }

    [Fact]
    public void ManyTabs_ShareTheRow_NeverBelowMinimum_RestHidden()
    {
        ChatDeckGeometry squeezed = Make(w: 460, tabs: 9);
        Assert.All(squeezed.Tabs, t => Assert.True(t.W >= ChatDeckGeometry.MinTabWidth));
        Assert.Single(squeezed.Tabs.Select(t => MathF.Round(t.W, 2)).Distinct());

        ChatDeckGeometry overflow = Make(w: 280, tabs: 16);
        Assert.Contains(overflow.Tabs, t => t.W == 0);
        Assert.All(overflow.Tabs.Where(t => t.W > 0), t => Assert.True(t.X + t.W <= overflow.SearchStud.X));
    }

    [Fact]
    public void Rows_StackTopToBottom()
    {
        ChatDeckGeometry g = Make();
        Assert.Equal(460 - ChatDeckGeometry.Pad - ChatDeckGeometry.StudSize, g.SearchStud.X, 2);
        Assert.Equal(220 - ChatDeckGeometry.Pad - ChatDeckGeometry.InputHeight, g.InputRow.Y, 2);
        Assert.Equal(g.InputRow.X, g.Chip.X);
        Assert.Equal(g.Chip.X + g.Chip.W + 4, g.Input.X, 2);
        Assert.Equal(g.InputRow.X + g.InputRow.W, g.Input.X + g.Input.W, 2);
        Assert.True(g.Transcript.Y > g.Tabs[0].Y + g.Tabs[0].H);
        Assert.True(g.Transcript.Y + g.Transcript.H < g.InputRow.Y);
        Assert.Equal(0, g.SearchField.W);
    }

    [Fact]
    public void SearchOpen_PushesTheTranscriptDown()
    {
        ChatDeckGeometry closed = Make(), open = Make(search: true);
        Assert.True(open.SearchField.W > 0);
        Assert.Equal(closed.Transcript.Y + ChatDeckGeometry.SearchHeight + 4, open.Transcript.Y, 2);
    }

    [Fact]
    public void HitAt_FindsEachZone()
    {
        ChatDeckGeometry g = Make();
        DeckRect t2 = g.Tabs[2];
        Assert.Equal(new ChatDeckHit(ChatDeckZone.Tab, 2), g.HitAt(t2.X + 2, t2.Y + 2));
        Assert.Equal(ChatDeckZone.SearchStud, g.HitAt(g.SearchStud.X + 2, g.SearchStud.Y + 2).Zone);
        Assert.Equal(ChatDeckZone.Chip, g.HitAt(g.Chip.X + 2, g.Chip.Y + 2).Zone);
        Assert.Equal(ChatDeckZone.Frame, g.HitAt(2, 2).Zone);
        Assert.Equal(ChatDeckZone.None, g.HitAt(-1, 5).Zone);
    }

    [Theory]
    [InlineData(0, "")]
    [InlineData(-3, "")]
    [InlineData(7, "7")]
    [InlineData(99, "99")]
    [InlineData(100, "99+")]
    [InlineData(5000, "99+")]
    public void Badge_IsCapped(int unread, string text) => Assert.Equal(text, ChatDeckBadges.Text(unread));
}
```

Add to `DeckFontsTests` (it builds `DeckFonts` with a recording bake function that returns `null!`):

```csharp
    [Fact]
    public void Face_BakesOncePerPixelHeightAndWeight()
    {
        var baked = new List<(float Px, BundledUiFontWeight Weight)>();
        var fonts = new DeckFonts((px, weight) => { baked.Add((px, weight)); return null!; });

        fonts.Face(14f, BundledUiFontWeight.Regular);
        fonts.Face(14.2f, BundledUiFontWeight.Regular);   // rounds to the same 14 px face
        fonts.Face(14f, BundledUiFontWeight.SemiBold);
        fonts.Face(16f, BundledUiFontWeight.Regular);

        Assert.Equal(
            [(14f, BundledUiFontWeight.Regular), (14f, BundledUiFontWeight.SemiBold), (16f, BundledUiFontWeight.Regular)],
            baked);
    }
```

- [ ] **Step 2: Run tests to verify they fail**

Run: the Verify command. Expected: build errors.

- [ ] **Step 3: Implement geometry and badges**

```csharp
namespace AcDream.App.UI.Hud;

internal enum ChatDeckZone { None, Frame, Tab, SearchStud, Chip }

internal readonly record struct ChatDeckHit(ChatDeckZone Zone, int Tab = -1);

/// <summary>One tab as the top row shows it: its name and its badge text ("" for none).</summary>
internal readonly record struct ChatDeckTabLabel(string Name, string Badge);

internal static class ChatDeckBadges
{
    public static string Text(int unread) => unread <= 0 ? "" : unread > 99 ? "99+" : unread.ToString(System.Globalization.CultureInfo.InvariantCulture);
}

/// <summary>Where everything in the pinned Chat Deck window goes, for one size and set of tabs.</summary>
internal sealed class ChatDeckGeometry
{
    public const float Pad = 8f;
    public const float TabHeight = 20f;
    public const float TabPadX = 9f;
    public const float TabGap = 3f;
    public const float MinTabWidth = 34f;
    public const float BadgePadX = 4f;
    public const float BadgeGap = 5f;
    public const float BadgeHeight = 14f;
    public const float StudSize = 20f;
    public const float SearchHeight = 22f;
    public const float InputHeight = 26f;
    public const float ChipArrow = 12f;
    public const float MarkerWidth = 96f;
    public const float MarkerHeight = 18f;
    public const float MinWidth = 280f;
    public const float MinHeight = 140f;
    public const float DefaultWidth = 460f;
    public const float DefaultHeight = 220f;

    public required DeckRect Frame { get; init; }
    public required IReadOnlyList<DeckRect> Tabs { get; init; }
    public required DeckRect SearchStud { get; init; }
    /// <summary>Zero width while search is closed.</summary>
    public required DeckRect SearchField { get; init; }
    public required DeckRect Transcript { get; init; }
    public required DeckRect NewLines { get; init; }
    public required DeckRect InputRow { get; init; }
    public required DeckRect Chip { get; init; }
    public required DeckRect Input { get; init; }

    public static float BadgeWidth(string badge, Func<string, float> measure) =>
        badge.Length == 0 ? 0f : measure(badge) + 2 * BadgePadX;

    public static ChatDeckGeometry Create(
        float width, float height, IReadOnlyList<ChatDeckTabLabel> tabs, bool searchOpen, string chipText,
        Func<string, float> measureLabel)
    {
        var stud = new DeckRect(width - Pad - StudSize, Pad, StudSize, StudSize);
        float tabsRight = stud.X - TabGap;
        float room = MathF.Max(0f, tabsRight - Pad);

        var natural = tabs.Select(t =>
            2 * TabPadX + measureLabel(t.Name)
            + (t.Badge.Length > 0 ? BadgeGap + BadgeWidth(t.Badge, measureLabel) : 0f)).ToArray();
        float gaps = TabGap * Math.Max(0, tabs.Count - 1);
        bool fits = natural.Sum() + gaps <= room;
        float shared = tabs.Count == 0 ? 0f : MathF.Max(MinTabWidth, (room - gaps) / tabs.Count);

        var rects = new List<DeckRect>(tabs.Count);
        float x = Pad;
        for (int i = 0; i < tabs.Count; i++)
        {
            float w = fits ? natural[i] : shared;
            if (x + w > tabsRight + 0.01f)
            {
                rects.Add(new DeckRect(x, Pad, 0f, 0f));
                continue;
            }
            rects.Add(new DeckRect(x, Pad, w, TabHeight));
            x += w + TabGap;
        }

        float top = Pad + TabHeight + 4f;
        var search = new DeckRect(Pad, top, 0f, SearchHeight);
        if (searchOpen)
        {
            search = search with { W = width - 2 * Pad };
            top += SearchHeight + 4f;
        }

        var inputRow = new DeckRect(Pad, height - Pad - InputHeight, width - 2 * Pad, InputHeight);
        float chipW = MathF.Min(inputRow.W / 2f, measureLabel(chipText) + 2 * TabPadX + ChipArrow);
        var chip = new DeckRect(inputRow.X, inputRow.Y, chipW, InputHeight);
        var input = new DeckRect(chip.X + chip.W + 4f, inputRow.Y, MathF.Max(0f, inputRow.W - chip.W - 4f), InputHeight);
        var transcript = new DeckRect(Pad, top, width - 2 * Pad, MathF.Max(0f, inputRow.Y - 4f - top));
        var marker = new DeckRect(
            transcript.X + (transcript.W - MarkerWidth) / 2f,
            transcript.Y + transcript.H - MarkerHeight - 4f,
            MarkerWidth, MarkerHeight);

        return new ChatDeckGeometry
        {
            Frame = new DeckRect(0f, 0f, width, height),
            Tabs = rects,
            SearchStud = stud,
            SearchField = search,
            Transcript = transcript,
            NewLines = marker,
            InputRow = inputRow,
            Chip = chip,
            Input = input,
        };
    }

    public ChatDeckHit HitAt(float x, float y)
    {
        for (int i = 0; i < Tabs.Count; i++)
            if (Tabs[i].W > 0 && Tabs[i].Contains(x, y)) return new(ChatDeckZone.Tab, i);
        if (SearchStud.Contains(x, y)) return new(ChatDeckZone.SearchStud);
        if (Chip.Contains(x, y)) return new(ChatDeckZone.Chip);
        return Frame.Contains(x, y) ? new(ChatDeckZone.Frame) : new(ChatDeckZone.None);
    }
}
```

- [ ] **Step 4: Palette, icons, fonts**

`ChatDeckPalette.cs` — the Command Deck's colours plus the chat ones. If `DeckChrome` is on main (Spell Deck merged), alias the shared ones to it (`internal static readonly Vector4 FrameTop = DeckChrome.FrameTop;` and so on) and call `DeckChrome.DrawFrame` from `DrawFrame`; otherwise:

```csharp
using System.Numerics;

namespace AcDream.App.UI.Hud;

/// <summary>The Chat Deck's colours: the Command Deck's frame and studs, plus tabs, badges and fields.</summary>
internal static class ChatDeckPalette
{
    internal static Vector4 Rgb(int hex, float a = 1f) =>
        new(((hex >> 16) & 0xFF) / 255f, ((hex >> 8) & 0xFF) / 255f, (hex & 0xFF) / 255f, a);

    internal static readonly Vector4 FrameTop = Rgb(0x2B2A31);
    internal static readonly Vector4 FrameBottom = Rgb(0x17161B);
    internal static readonly Vector4 FrameEdge = Rgb(0x0B0A0D);
    internal static readonly Vector4 Bronze = Rgb(0x6B5A3A, 0.7f);
    internal static readonly Vector4 TextInk = Rgb(0xFFFFFF);
    internal static readonly Vector4 LabelInk = Rgb(0xC9BFA6);
    internal static readonly Vector4 StudOff = Rgb(0x34323B);
    internal static readonly Vector4 StudOffInk = Rgb(0xA8A3B5);
    internal static readonly Vector4 StudOn = Rgb(0xD14A5C);
    internal static readonly Vector4 TabSelected = Rgb(0x3A3842);
    internal static readonly Vector4 TabUnderline = Rgb(0xE2C27A);
    internal static readonly Vector4 BadgeGold = Rgb(0xE2C27A);
    internal static readonly Vector4 BadgeRed = Rgb(0xD14A5C);
    internal static readonly Vector4 BadgeInk = Rgb(0x17161B);
    internal static readonly Vector4 FieldFill = Rgb(0x0C0B0F, 0.9f);
    internal static readonly Vector4 TagInk = Rgb(0x8FD3FF);
    internal static readonly Vector4 TimestampInk = Rgb(0x8C8698);
    internal static readonly Vector4 MarkerFill = Rgb(0x3A3842, 0.95f);

    /// <summary>The dark rounded frame, its fill at <paramref name="opacity"/> (the edge and rule stay solid).</summary>
    internal static void DrawFrame(UiRenderContext ctx, DeckRect f, float opacity)
    {
        ctx.FillRoundedRect(f.X, f.Y, f.W, f.H, 8f, FrameEdge with { W = opacity });
        ctx.FillVerticalGradient(f.X + 1f, f.Y + 1f, f.W - 2f, f.H - 2f, new CanvasCornerRadii(7f, 7f, 7f, 7f),
            FrameTop with { W = opacity }, FrameBottom with { W = opacity });
        ctx.StrokeRoundedRect(f.X + 3f, f.Y + 3f, f.W - 6f, f.H - 6f, 5f, Bronze, 1f);
    }
}
```

(The Command Deck draws its frame inset by 2 px inside a margin; the Chat Deck has no orb, so its frame fills the element. Check `CanvasCornerRadii`'s namespace in `UiCommandDeck.Draw.cs` and add its `using`.)

`assets/hud/search.svg`:

```xml
<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 24 24"><circle cx="10" cy="10" r="6" fill="none" stroke="#fff" stroke-width="2.5"/><path d="M14.5 14.5 L20 20" stroke="#fff" stroke-width="3" stroke-linecap="round"/></svg>
```

`assets/hud/chevron-down.svg`:

```xml
<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 24 24"><path d="M5 9 L12 16 L19 9" fill="none" stroke="#fff" stroke-width="3" stroke-linecap="round" stroke-linejoin="round"/></svg>
```

In `HudIcons` add `public const string Search = "search";` and `public const string ChevronDown = "chevron-down";` and both to `All`.

In `DeckFonts` add:

```csharp
    private readonly Dictionary<(float, BundledUiFontWeight), Lazy<UiDatFont>> _faces = [];

    /// <summary>A bundled sans face at a whole-pixel height (the Chat Deck's text sizes), baked once.</summary>
    public UiDatFont Face(float pixelHeight, BundledUiFontWeight weight)
    {
        float px = PixelHeight(pixelHeight, 1f);
        if (!_faces.TryGetValue((px, weight), out Lazy<UiDatFont>? face))
            _faces[(px, weight)] = face = new(() => bake(px, weight));
        return face.Value;
    }
```

- [ ] **Step 5: Run tests to verify they pass**

Run: the Verify command. Expected: all pass.

- [ ] **Step 6: Commit**

```bash
git add src/AcDream.App/UI/Hud/ChatDeckGeometry.cs src/AcDream.App/UI/Hud/ChatDeckPalette.cs assets/hud/search.svg assets/hud/chevron-down.svg src/AcDream.App/UI/Hud/EmbeddedSvgSpriteCache.cs src/AcDream.App/UI/Hud/DeckFonts.cs tests/AcDream.App.Tests/UI/Hud/ChatDeckGeometryTests.cs tests/AcDream.App.Tests/UI/Hud/DeckFontsTests.cs
git commit -m "Chat Deck: window geometry, capped badges, palette, search and chevron icons, font faces

Claude-Session: https://claude.ai/code/session_01WFS4EHSNafq2P1qfUuh258"
```

---

### Task 9: `UiChatDeck` — frame, tabs, transcript, search, "new" marker

**Goal:** The window element up to the input row: it draws the frame and the tab bar with badges, hosts the transcript, switches tabs on a click, marks tabs read through the model's view state, offers *Mark as read* / *Mark all read* on a tab's right-click, filters with search, and shows "N new" while scrolled up.

**Files:**
- Create: `src/AcDream.App/UI/Hud/UiChatDeck.cs`, `src/AcDream.App/UI/Hud/UiChatDeck.Draw.cs`
- Test: `tests/AcDream.App.Tests/UI/Hud/UiChatDeckTests.cs`

**Acceptance Criteria:**
- [ ] A press on a tab selects it in the model (`SelectedTabId`), scrolls the transcript to the newest line and clears the "new" count; a press on the frame body is left to the window drag (`HandlesClick` false there).
- [ ] The tab labels carry the model's unread counts through `ChatDeckBadges.Text`; a tab with its alert flag draws its badge red.
- [ ] The transcript shows the selected tab's lines (built by `ChatDeckTranscript` with the appearance's timestamps and channel colours) and rebuilds only when the tab's `Version`, the tab, the width, the font, the search text or the settings change.
- [ ] `OnShown()` / `OnHidden()` and the transcript's scroll position reach `model.SetView(visible, atBottom)` on every refresh, so the selected tab's lines are read while it is shown at the bottom.
- [ ] Right-click on a tab opens a menu with *Mark as read* and *Mark all read* that do what they say; right-click elsewhere opens nothing.
- [ ] The search stud opens a field under the tabs; typing narrows the transcript; Esc or closing the stud clears it.
- [ ] Scrolled up, lines added to the selected tab show "N new" over the bottom of the transcript; clicking it scrolls to the newest line; scrolling back to the bottom hides it.
- [ ] A new model (relog) or the model's `Cleared` resets the view (no stale lines, no stale count).
- [ ] With no model (no character), the window draws its frame and no tabs.

**Verify:** `MAC_TEST_FILTER='FullyQualifiedName~UiChatDeck' ~/code/OpenAC/test-baseline/run-mac-tests.sh ~/code/OpenAC/OpenAC-chat-deck-window tests/AcDream.App.Tests/AcDream.App.Tests.csproj` → all pass

**Steps:**

- [ ] **Step 1: Write the failing tests**

```csharp
using AcDream.App.Tests.Plugins;
using AcDream.App.UI;
using AcDream.App.UI.Hud;
using AcDream.Core.Chat;
using AcDream.Core.Chat.Deck;
using AcDream.Plugin.Abstractions;
using AcDream.Runtime.Chat;

namespace AcDream.App.Tests.UI.Hud;

public sealed class UiChatDeckTests
{
    internal sealed class RecordingBus : ICommandBus
    {
        public readonly List<object> Published = [];
        public void Publish<T>(T command) where T : notnull => Published.Add(command);
    }

    internal sealed class Feedback : IChatCommandFeedback
    {
        public readonly List<string> Lines = [];
        public string? LastIncomingTellSender { get; set; }
        public string? LastOutgoingTellTarget { get; set; }
        public void ShowInterfaceText(string text) => Lines.Add(text);
        public void ShowSystemMessage(string text) => Lines.Add(text);
    }

    internal sealed class Harness
    {
        public readonly ChatLog Log = new();
        public ChatDeckModel? Model;
        public readonly RuntimeChatEntryOwner Entry = new();
        public readonly RecordingBus Bus = new();
        public readonly Feedback Feedback = new();
        public readonly List<PluginChatLinkClicked> Links = [];
        public readonly FakeKeyboard Keyboard = new();
        public readonly UiChatDeck Deck;

        public Harness(bool withModel = true)
        {
            if (withModel) Model = new ChatDeckModel(Log, ChatDeckDefaults.Create(), "Davian");
            Deck = new UiChatDeck(
                new ChatDeckBindings(
                    Model: () => Model,
                    Entry: Entry,
                    Feedback: Feedback,
                    Bus: () => Bus,
                    LastIncomingTellSender: () => Feedback.LastIncomingTellSender,
                    StayInChatMode: () => false,
                    LinkClicked: Links.Add,
                    Keyboard: Keyboard),
                fonts: null, icons: null)
            { Width = ChatDeckGeometry.DefaultWidth, Height = ChatDeckGeometry.DefaultHeight };
            Deck.Refresh();
        }

        public void Say(string text, string sender = "Bob")
        {
            Log.OnLocalSpeech(sender, text, 0x50000002u, isRanged: false, logTextType: 0x02u);
            Model!.Tick();
            Deck.Refresh();
        }

        public void Press(DeckRect r) =>
            Deck.OnEvent(new UiEvent(0, Deck, UiEventType.MouseDown, Data1: (int)(r.X + 2), Data2: (int)(r.Y + 2)));

        public int TabIndex(string id) => Model!.Settings.Tabs.ToList().FindIndex(t => t.Id == id);
    }

    [Fact]
    public void PressOnATab_SelectsIt()
    {
        var h = new Harness();
        h.Press(h.Deck.Geometry.Tabs[h.TabIndex("combat")]);
        Assert.Equal("combat", h.Model!.SelectedTabId);
    }

    [Fact]
    public void FrameBody_IsLeftToTheWindowDrag()
    {
        var h = new Harness();
        Assert.True(h.Deck.HitTestLocal(2, 2));
        Assert.False(h.Deck.HandlesClick);
        DeckRect tab = h.Deck.Geometry.Tabs[0];
        h.Deck.HitTestLocal(tab.X + 2, tab.Y + 2);
        Assert.True(h.Deck.HandlesClick);
    }

    [Fact]
    public void Badges_ShowUnread()
    {
        var h = new Harness();
        h.Say("hello");
        Assert.Equal("1", h.Deck.TabLabels[h.TabIndex("local")].Badge);
        Assert.Equal("", h.Deck.TabLabels[h.TabIndex("combat")].Badge);
    }

    [Fact]
    public void Transcript_ShowsTheSelectedTab()
    {
        var h = new Harness();
        h.Say("hello");
        Assert.Single(h.Deck.Transcript.LinesProvider());
        h.Press(h.Deck.Geometry.Tabs[h.TabIndex("combat")]);
        h.Deck.Refresh();
        Assert.Empty(h.Deck.Transcript.LinesProvider());
    }

    [Fact]
    public void Transcript_RebuildsOnlyWhenSomethingChanged()
    {
        var h = new Harness();
        h.Say("hello");
        int builds = h.Deck.TranscriptBuilds;
        h.Deck.Refresh();
        h.Deck.Refresh();
        Assert.Equal(builds, h.Deck.TranscriptBuilds);
        h.Say("again");
        Assert.Equal(builds + 1, h.Deck.TranscriptBuilds);
    }

    [Fact]
    public void ShownAndAtBottom_ReadsTheSelectedTab()
    {
        var h = new Harness();
        h.Deck.OnShown();
        h.Deck.Refresh();
        Assert.True(h.Model!.ViewVisible);
        h.Say("seen");
        Assert.Equal(0, h.Model.FindTab("all")!.Unread);

        h.Deck.OnHidden();
        h.Deck.Refresh();
        Assert.False(h.Model.ViewVisible);
        h.Say("unseen");
        Assert.Equal(1, h.Model.FindTab("all")!.Unread);
    }

    [Fact]
    public void TabMenu_MarksRead()
    {
        var h = new Harness();
        h.Say("one");
        DeckRect local = h.Deck.Geometry.Tabs[h.TabIndex("local")];
        Assert.Equal(["Mark as read", "Mark all read"], h.Deck.TabMenuItemsAt(local.X + 2, local.Y + 2));
        h.Deck.ChooseTabMenuItem(0);
        Assert.Equal(0, h.Model!.FindTab("local")!.Unread);
        Assert.Equal(1, h.Model.FindTab("all")!.Unread);

        Assert.Empty(h.Deck.TabMenuItemsAt(5, h.Deck.Geometry.Transcript.Y + 10));
        h.Deck.TabMenuItemsAt(local.X + 2, local.Y + 2);
        h.Deck.ChooseTabMenuItem(1);
        Assert.Equal(0, h.Model.FindTab("all")!.Unread);
    }

    [Fact]
    public void Search_NarrowsAndEscClears()
    {
        var h = new Harness();
        h.Say("a drudge appears");
        h.Say("a banderling appears");
        h.Press(h.Deck.Geometry.SearchStud);
        h.Deck.Refresh();
        Assert.True(h.Deck.SearchOpen);

        h.Deck.SearchField.SetText("drudge");
        h.Deck.Refresh();
        Assert.Single(h.Deck.Transcript.LinesProvider());

        h.Deck.SearchField.OnEvent(new UiEvent(0, h.Deck.SearchField, UiEventType.KeyDown, Data0: (int)Silk.NET.Input.Key.Escape));
        h.Deck.Refresh();
        Assert.False(h.Deck.SearchOpen);
        Assert.Equal(2, h.Deck.Transcript.LinesProvider().Count);
    }

    [Fact]
    public void ScrolledUp_CountsNewLines_ClickFollows()
    {
        var h = new Harness();
        h.Say("first");
        UiScrollable scroll = h.Deck.Transcript.Scroll;
        scroll.SetExtents(1000, 100);
        scroll.SetScrollY(0);
        h.Deck.Refresh();

        h.Say("second");
        h.Say("third");
        Assert.Equal(2, h.Deck.NewLineCount);
        Assert.True(h.Deck.NewLinesMarker.Visible);

        h.Deck.NewLinesMarker.OnEvent(new UiEvent(0, h.Deck.NewLinesMarker, UiEventType.MouseDown));
        h.Deck.Refresh();
        Assert.True(scroll.AtEnd);
        Assert.Equal(0, h.Deck.NewLineCount);
        Assert.False(h.Deck.NewLinesMarker.Visible);
    }

    [Fact]
    public void ClearAndNewModel_ResetTheView()
    {
        var h = new Harness();
        h.Say("before");
        h.Log.Clear();
        h.Model!.Tick();
        h.Deck.Refresh();
        Assert.Empty(h.Deck.Transcript.LinesProvider());

        h.Say("again");
        h.Model = new ChatDeckModel(h.Log, ChatDeckDefaults.Create(), "Davian");
        h.Deck.Refresh();
        Assert.Empty(h.Deck.Transcript.LinesProvider());
    }

    [Fact]
    public void NoModel_NoTabs()
    {
        var h = new Harness(withModel: false);
        Assert.Empty(h.Deck.Geometry.Tabs);
        Assert.Empty(h.Deck.Transcript.LinesProvider());
        MarkupDrawProbe.Draw(h.Deck, 460, 220);   // draws without throwing
    }

    [Fact]
    public void Draws_WithTabsBadgesAndSearchOpen()
    {
        var h = new Harness();
        h.Say("hello");
        h.Press(h.Deck.Geometry.SearchStud);
        h.Deck.Refresh();
        var renderer = MarkupDrawProbe.Draw(h.Deck, 460, 220);
        Assert.True(MarkupDrawProbe.CountQuads(renderer, ChatDeckPalette.BadgeGold) > 0);
        Assert.True(MarkupDrawProbe.CountQuads(renderer, ChatDeckPalette.TabSelected) > 0);
    }
}
```

`CountQuads` matches untextured quads by colour; if the rounded-rect fills are textured or tessellated differently (check `MarkupDrawProbe` and `UiRenderContext.FillRoundedRect`), assert on the colour with `QuadBounds` or drop to `Assert.NotEmpty(renderer.DebugSpriteSegmentVerts)`.

- [ ] **Step 2: Run tests to verify they fail**

Run: the Verify command. Expected: build errors (`UiChatDeck`, `ChatDeckBindings` missing).

- [ ] **Step 3: Implement the element**

`UiChatDeck.cs`:

```csharp
using System.Numerics;
using AcDream.App.UI.Layout;
using AcDream.Core.Chat;
using AcDream.Core.Chat.Deck;
using AcDream.Plugin.Abstractions;
using AcDream.Runtime.Chat;
using AcDream.UI.Abstractions.Panels.Chat;

namespace AcDream.App.UI.Hud;

/// <summary>What the Chat Deck reads and sends through.</summary>
/// <param name="Model">The character's model, or null between sessions.</param>
/// <param name="Entry">The one chat entry every front end shares (draft, recall).</param>
internal sealed record ChatDeckBindings(
    Func<ChatDeckModel?> Model,
    RuntimeChatEntryOwner Entry,
    IChatCommandFeedback Feedback,
    Func<ICommandBus> Bus,
    Func<string?> LastIncomingTellSender,
    Func<bool> StayInChatMode,
    Action<PluginChatLinkClicked>? LinkClicked = null,
    Silk.NET.Input.IKeyboard? Keyboard = null);

/// <summary>
/// The Chat Deck window (pinned layout): a tab bar, a transcript of the selected
/// tab, and an input that sends on the tab's channel. The deck draws its frame,
/// tabs and studs itself; the transcript, the search field, the input and the
/// menus are child elements, so selection, copy, scrolling and typing behave as
/// they do everywhere else.
/// </summary>
internal sealed partial class UiChatDeck : UiElement
{
    private const float LabelPixels = 12f;

    private readonly ChatDeckBindings _b;
    private readonly DeckFonts? _fonts;
    private readonly EmbeddedSvgSpriteCache? _icons;
    private readonly ChatDeckTranscript _transcriptBuilder = new();
    private readonly UiMarkupContextMenu _tabMenu = new();
    private ChatDeckTranscriptLayout _layout = new();
    private ChatDeckModel? _model;
    private ChatDeckGeometry _geometry;
    private IReadOnlyList<ChatDeckTabLabel> _labels = [];
    private object? _builtFor;
    private bool _searchOpen;
    private string _searchText = "";
    private bool _shown;
    private long? _addedAtScrollUp;
    private int _menuTab = -1;
    private ChatDeckHit _hit;

    public UiChatDeck(ChatDeckBindings bindings, DeckFonts? fonts, EmbeddedSvgSpriteCache? icons)
    {
        _b = bindings ?? throw new ArgumentNullException(nameof(bindings));
        _fonts = fonts;
        _icons = icons;
        Width = ChatDeckGeometry.DefaultWidth;
        Height = ChatDeckGeometry.DefaultHeight;
        MinWidth = ChatDeckGeometry.MinWidth;
        MinHeight = ChatDeckGeometry.MinHeight;
        Resizable = true;

        Transcript = new UiText
        {
            Selectable = true,
            WheelScrollEnabled = true,
            PreserveEndOnLayout = true,
            Padding = 2f,
            MarginLeft = 6f,
            Keyboard = bindings.Keyboard,
            TagColor = ChatDeckPalette.TagInk,
            LinesProvider = () => _layout.Lines,
            LineKeysProvider = () => _layout.Keys,
        };
        Transcript.LineRunsProvider = i => i >= 0 && i < _layout.Runs.Count ? _layout.Runs[i] : null;
        Transcript.LineMarkerProvider = i => i >= 0 && i < _layout.Markers.Count ? _layout.Markers[i] : null;
        Transcript.OnCharClick = OnTranscriptClick;

        NewLinesMarker = new NewLines(this) { Visible = false };
        SearchField = new UiField
        {
            OneLine = true, Selectable = true, Keyboard = bindings.Keyboard,
            BackgroundColor = ChatDeckPalette.FieldFill, TextColor = ChatDeckPalette.TextInk,
            ClearOnSubmit = false, RecordHistory = false, Visible = false,
        };
        SearchField.OnTextChanged = text => _searchText = text.Trim();
        SearchField.OnEscape = CloseSearch;

        _tabMenu.Opening = (x, y) => _menuTab = _geometry.HitAt(x, y) is { Zone: ChatDeckZone.Tab } hit ? hit.Tab : -1;
        _tabMenu.ItemsSource = () => _menuTab >= 0 ? TabMenuItems : [];
        _tabMenu.Selected = ChooseTabMenuItem;
        ContextMenu = _tabMenu;

        AddChild(Transcript);
        AddChild(NewLinesMarker);
        AddChild(SearchField);
        AddChild(_tabMenu);
        CreateInputRow();          // UiChatDeck.Input.cs (Task 10); until then an empty partial method
        _geometry = ChatDeckGeometry.Create(Width, Height, [], false, ChipText, MeasureLabel);
    }

    private static readonly string[] TabMenuItems = ["Mark as read", "Mark all read"];

    internal UiText Transcript { get; }
    internal UiField SearchField { get; }
    internal UiElement NewLinesMarker { get; }
    internal ChatDeckGeometry Geometry => _geometry;
    internal IReadOnlyList<ChatDeckTabLabel> TabLabels => _labels;
    internal bool SearchOpen => _searchOpen;
    internal int NewLineCount { get; private set; }
    internal int TranscriptBuilds { get; private set; }

    public override bool HandlesClick => IsControl(_hit);

    private static bool IsControl(ChatDeckHit hit) =>
        hit.Zone is ChatDeckZone.Tab or ChatDeckZone.SearchStud or ChatDeckZone.Chip;

    internal bool HitTestLocal(float x, float y) => OnHitTest(x, y);

    protected override bool OnHitTest(float localX, float localY)
    {
        _hit = _geometry.HitAt(localX, localY);
        CapturesPointerDrag = IsControl(_hit);
        return _hit.Zone != ChatDeckZone.None;
    }

    protected override void OnTick(double deltaSeconds) => Refresh();

    /// <summary>The window is on screen (the switch showed it).</summary>
    public void OnShown() => _shown = true;

    /// <summary>The window left the screen: lines arriving now are unread.</summary>
    public void OnHidden()
    {
        _shown = false;
        _tabMenu.Close(restoreFocus: false);
        OnInputHidden();           // UiChatDeck.Input.cs (Task 10)
    }

    internal ChatDeckModel? Model => _model;

    private ChatDeckTabState? SelectedTab => _model?.FindTab(_model.SelectedTabId);

    /// <summary>Samples the model, lays the window out and rebuilds the transcript if anything it shows changed.</summary>
    internal void Refresh()
    {
        ChatDeckModel? model = _b.Model();
        if (!ReferenceEquals(model, _model))
        {
            if (_model is not null) _model.Cleared -= ResetView;
            _model = model;
            if (_model is not null) _model.Cleared += ResetView;
            ResetView();
        }

        _labels = _model is null
            ? []
            : _model.Settings.Tabs
                .Select(t => new ChatDeckTabLabel(t.Name, ChatDeckBadges.Text(_model.FindTab(t.Id)?.Unread ?? 0)))
                .ToList();
        _geometry = ChatDeckGeometry.Create(Width, Height, _labels, _searchOpen, ChipText, MeasureLabel);

        Place(Transcript, _geometry.Transcript);
        Place(SearchField, _searchOpen ? _geometry.SearchField : default);
        SearchField.Visible = _searchOpen;
        Place(NewLinesMarker, _geometry.NewLines);
        LayoutInputRow();          // UiChatDeck.Input.cs (Task 10)

        ChatDeckAppearance appearance = _model?.Settings.Appearance ?? new ChatDeckAppearance();
        Transcript.DatFont = _fonts?.Face(appearance.FontSize, BundledUiFontWeight.Regular);
        SearchField.DatFont = _fonts?.Face(appearance.InputFontSize, BundledUiFontWeight.Regular);

        RebuildTranscript(appearance);
        TrackNewLines();
        _model?.SetView(_shown, Transcript.Scroll.AtEnd);
    }

    private static void Place(UiElement child, DeckRect r)
    {
        child.Left = r.X;
        child.Top = r.Y;
        child.Width = r.W;
        child.Height = r.H;
    }

    private void RebuildTranscript(ChatDeckAppearance appearance)
    {
        ChatDeckTabState? tab = SelectedTab;
        float wrap = MathF.Max(1f, Transcript.Width - 2 * Transcript.Padding - Transcript.MarginLeft - Transcript.MarginRight);
        UiDatFont? font = Transcript.DatFont;
        var key = (_model, tab, tab?.Version ?? -1, wrap, font, _searchText, _model?.Settings);
        if (Equals(key, _builtFor)) return;
        _builtFor = key;
        TranscriptBuilds++;

        if (tab is null)
        {
            _layout = new ChatDeckTranscriptLayout();
            return;
        }
        Func<string, float> measure = font is not null ? font.MeasureWidth : static s => s.Length * 7f;
        _layout = _transcriptBuilder.Build(tab.Snapshot(), new ChatDeckTranscriptStyle(
            wrap, measure, font, appearance.Timestamps,
            line => ChannelColour(line, appearance),
            ChatDeckPalette.TagInk, ChatDeckPalette.TimestampInk, _searchText));
    }

    private static Vector4 ChannelColour(ChatDeckLine line, ChatDeckAppearance appearance) =>
        appearance.ChannelColours.TryGetValue(line.Channel, out uint argb) ? ChatDeckTranscript.Argb(argb)
        : RetailChatColorTable.TryGetColor(line.Entry.LogTextType, out Vector4 colour) ? colour
        : ChatDeckPalette.TextInk;

    private void TrackNewLines()
    {
        ChatDeckTabState? tab = SelectedTab;
        if (tab is null || Transcript.Scroll.AtEnd)
        {
            _addedAtScrollUp = null;
            NewLineCount = 0;
        }
        else
        {
            if (_addedAtScrollUp is not long since || tab.Added < since) _addedAtScrollUp = since = tab.Added;
            NewLineCount = (int)Math.Min(int.MaxValue, tab.Added - since);
        }
        NewLinesMarker.Visible = NewLineCount > 0;
    }

    private void ResetView()
    {
        _addedAtScrollUp = null;
        NewLineCount = 0;
        _builtFor = null;
        Transcript.Scroll.ScrollToEnd();
        OnViewReset();             // UiChatDeck.Input.cs (Task 10): drops a one-line channel override
    }

    private void FollowNewest()
    {
        Transcript.Scroll.ScrollToEnd();
        _addedAtScrollUp = null;
        NewLineCount = 0;
        NewLinesMarker.Visible = false;
    }

    private void SelectTab(int index)
    {
        if (_model is null || index < 0 || index >= _model.Settings.Tabs.Count) return;
        _model.SelectTab(_model.Settings.Tabs[index].Id);
        ResetView();
    }

    private void ToggleSearch()
    {
        if (_searchOpen)
        {
            CloseSearch();
            return;
        }
        _searchOpen = true;
        FindRoot()?.SetKeyboardFocus(SearchField);
    }

    private void CloseSearch()
    {
        _searchOpen = false;
        _searchText = "";
        SearchField.SetText("");
    }

    /// <summary>The tab menu's items for a right-click at a point (empty off the tabs), as the menu reads them.</summary>
    internal IReadOnlyList<string> TabMenuItemsAt(float x, float y)
    {
        _tabMenu.Opening!(x, y);
        return _tabMenu.ItemsSource();
    }

    internal void ChooseTabMenuItem(int index)
    {
        if (_model is null || _menuTab < 0 || _menuTab >= _model.Settings.Tabs.Count) return;
        if (index == 0) _model.FindTab(_model.Settings.Tabs[_menuTab].Id)?.MarkRead();
        else if (index == 1) _model.MarkAllRead();
    }

    private bool OnTranscriptClick(UiText.Pos position)
    {
        if (position.Line < 0 || position.Line >= _layout.Tags.Count || _layout.Tags[position.Line] is not { } ranges)
            return false;
        foreach ((int start, int length, ChatTextTag tag) in ranges)
        {
            if (position.Col < start || position.Col >= start + length) continue;
            if (tag.TryGetCoordinate(out double eastWest, out double northSouth))
            {
                _b.LinkClicked?.Invoke(new PluginChatLinkClicked(
                    PluginChatLinkKind.Coordinate,
                    ChatCoordinateLinks.Describe(eastWest, northSouth),
                    new PluginChatCoordinate(eastWest, northSouth)));
                return true;
            }
            if (tag.IsTell && tag.TryGetIidString(out _, out string name) && name.Length > 0)
            {
                StartTell(name);   // UiChatDeck.Input.cs (Task 10)
                return true;
            }
        }
        return false;
    }

    private float MeasureLabel(string text) =>
        _fonts?.Face(LabelPixels, BundledUiFontWeight.Regular).MeasureWidth(text) ?? text.Length * 7f;

    public override bool OnEvent(in UiEvent e)
    {
        switch (e.Type)
        {
            case UiEventType.MouseDown:
            {
                ChatDeckHit hit = _geometry.HitAt(e.Data1, e.Data2);
                switch (hit.Zone)
                {
                    case ChatDeckZone.Tab: SelectTab(hit.Tab); return true;
                    case ChatDeckZone.SearchStud: ToggleSearch(); return true;
                    case ChatDeckZone.Chip: OpenChipMenu(e.Data1, e.Data2); return true;   // Task 10
                    default: return false;
                }
            }
            case UiEventType.Click:
                return IsControl(_hit);
            default:
                return false;
        }
    }

    /// <summary>"N new" over the bottom of the transcript while scrolled up; a click follows the newest line.</summary>
    private sealed class NewLines(UiChatDeck deck) : UiElement
    {
        public override bool HandlesClick => true;

        protected override bool OnHitTest(float localX, float localY) => true;

        public override bool OnEvent(in UiEvent e)
        {
            if (e.Type is not (UiEventType.MouseDown or UiEventType.Click)) return false;
            if (e.Type == UiEventType.MouseDown) deck.FollowNewest();
            return true;
        }

        protected override void OnDraw(UiRenderContext ctx) => deck.DrawNewLines(ctx, Width, Height);
    }
}
```

Until Task 10, add `UiChatDeck.Input.cs` with stubs so this task builds:

```csharp
namespace AcDream.App.UI.Hud;

internal sealed partial class UiChatDeck
{
    private string ChipText => "Say";
    private void CreateInputRow() { }
    private void LayoutInputRow() { }
    private void OnInputHidden() { }
    private void OnViewReset() { }
    private void OpenChipMenu(float x, float y) { }
    private void StartTell(string name) { }
}
```

`UiChatDeck.Draw.cs`:

```csharp
using System.Numerics;

namespace AcDream.App.UI.Hud;

internal sealed partial class UiChatDeck
{
    private DeckFace LabelFace => new(_fonts?.Face(LabelPixels, BundledUiFontWeight.Regular));
    private DeckFace LabelBold => new(_fonts?.Face(LabelPixels, BundledUiFontWeight.SemiBold));

    protected override void OnDraw(UiRenderContext ctx)
    {
        float opacity = _model?.Settings.Appearance.BackgroundOpacity ?? new ChatDeckAppearance().BackgroundOpacity;
        ChatDeckPalette.DrawFrame(ctx, _geometry.Frame, opacity);
        DrawTabs(ctx);
        DrawStud(ctx, _geometry.SearchStud, HudIcons.Search, _searchOpen);
        if (_searchOpen) ctx.FillRoundedRect(_geometry.SearchField.X, _geometry.SearchField.Y,
            _geometry.SearchField.W, _geometry.SearchField.H, 4f, ChatDeckPalette.FieldFill);
        DrawInputRow(ctx);        // UiChatDeck.Input.cs (Task 10); a no-op until then
    }

    private void DrawTabs(UiRenderContext ctx)
    {
        if (_model is null) return;
        IReadOnlyList<ChatDeckTab> tabs = _model.Settings.Tabs;
        for (int i = 0; i < _geometry.Tabs.Count && i < tabs.Count; i++)
        {
            DeckRect r = _geometry.Tabs[i];
            if (r.W <= 0) continue;
            bool selected = tabs[i].Id == _model.SelectedTabId;
            if (selected)
            {
                ctx.FillRoundedRect(r.X, r.Y, r.W, r.H, 5f, ChatDeckPalette.TabSelected);
                ctx.DrawFill(r.X + 4f, r.Y + r.H - 2f, r.W - 8f, 2f, ChatDeckPalette.TabUnderline);
            }

            string badge = _labels[i].Badge;
            float badgeW = ChatDeckGeometry.BadgeWidth(badge, s => LabelFace.Measure(ctx, s));
            float nameRoom = r.W - 2 * ChatDeckGeometry.TabPadX - (badge.Length > 0 ? badgeW + ChatDeckGeometry.BadgeGap : 0f);
            string name = Fit(ctx, LabelFace, tabs[i].Name, nameRoom);
            DrawLabel(ctx, LabelFace, name, r.X + ChatDeckGeometry.TabPadX, r,
                selected ? ChatDeckPalette.TextInk : ChatDeckPalette.LabelInk);

            if (badge.Length == 0) continue;
            bool alert = _model.FindTab(tabs[i].Id)?.Alert == true;
            var pill = new DeckRect(r.X + r.W - ChatDeckGeometry.TabPadX - badgeW,
                r.Y + (r.H - ChatDeckGeometry.BadgeHeight) / 2f, badgeW, ChatDeckGeometry.BadgeHeight);
            ctx.FillRoundedRect(pill.X, pill.Y, pill.W, pill.H, pill.H / 2f,
                alert ? ChatDeckPalette.BadgeRed : ChatDeckPalette.BadgeGold);
            DrawLabel(ctx, LabelBold, badge, pill.X + ChatDeckGeometry.BadgePadX, pill,
                alert ? ChatDeckPalette.TextInk : ChatDeckPalette.BadgeInk);
        }
    }

    private void DrawStud(UiRenderContext ctx, DeckRect r, string icon, bool on)
    {
        ctx.FillRoundedRect(r.X - 1f, r.Y - 1f, r.W + 2f, r.H + 2f, 6f, ChatDeckPalette.FrameEdge);
        ctx.FillRoundedRect(r.X, r.Y, r.W, r.H, 5f, on ? ChatDeckPalette.StudOn : ChatDeckPalette.StudOff);
        DrawIcon(ctx, icon, r.X + (r.W - 14f) / 2f, r.Y + (r.H - 14f) / 2f, 14f,
            on ? ChatDeckPalette.TextInk : ChatDeckPalette.StudOffInk);
    }

    private void DrawIcon(UiRenderContext ctx, string name, float x, float y, float size, Vector4 ink)
    {
        if (_icons is null) return;
        int pixels = Math.Max(1, (int)MathF.Ceiling(size * ctx.PixelScale));
        uint texture = _icons.Texture(name, pixels);
        if (texture != 0) ctx.DrawCoverageIcon(texture, x, y, pixels, ink);
    }

    private void DrawNewLines(UiRenderContext ctx, float w, float h)
    {
        ctx.FillRoundedRect(0f, 0f, w, h, h / 2f, ChatDeckPalette.MarkerFill);
        DrawIcon(ctx, HudIcons.ChevronDown, 8f, (h - 10f) / 2f, 10f, ChatDeckPalette.TabUnderline);
        DrawLabel(ctx, LabelFace, $"{NewLineCount} new", 22f, new DeckRect(0f, 0f, w, h), ChatDeckPalette.TextInk);
    }

    private static void DrawLabel(UiRenderContext ctx, DeckFace face, string text, float x, DeckRect band, Vector4 ink) =>
        face.Draw(ctx, text, x, band.Y + (band.H - face.LineHeight(ctx)) / 2f, ink);

    /// <summary>The text cut to fit <paramref name="room"/>, ending in "…" when cut.</summary>
    private static string Fit(UiRenderContext ctx, DeckFace face, string text, float room)
    {
        if (face.Measure(ctx, text) <= room) return text;
        for (int n = text.Length - 1; n > 0; n--)
        {
            string cut = text[..n] + "…";
            if (face.Measure(ctx, cut) <= room) return cut;
        }
        return "";
    }
}
```

Add `private void DrawInputRow(UiRenderContext ctx) { }` to the Task 9 stub file.

- [ ] **Step 4: Run tests to verify they pass**

Run: the Verify command. Expected: all pass. If `UiElement` has no settable `ContextMenu` from this assembly or `UiMarkupContextMenu.Close` has a different signature, follow what `MarkupContextMenu.cs` does when it wires a context menu.

- [ ] **Step 5: Commit**

```bash
git add src/AcDream.App/UI/Hud/UiChatDeck.cs src/AcDream.App/UI/Hud/UiChatDeck.Draw.cs src/AcDream.App/UI/Hud/UiChatDeck.Input.cs tests/AcDream.App.Tests/UI/Hud/UiChatDeckTests.cs
git commit -m "Chat Deck: window element with tabs, badges, transcript, search and new-lines marker

Claude-Session: https://claude.ai/code/session_01WFS4EHSNafq2P1qfUuh258"
```

---

### Task 10: `UiChatDeck` — input row, sending and the entry target

**Goal:** The input row: a channel chip showing where plain text goes (the tab's send channel, or a one-line override picked from the chip's menu), an input that shares the draft and recall history with every other chat front end and sends through `SubmitTo`, Ctrl+Tab / Ctrl+Shift+Tab to change tab, tell links, and the `IChatEntryTarget` actions.

**Files:**
- Modify: `src/AcDream.App/UI/Hud/UiChatDeck.Input.cs` (replace the Task 9 stubs)
- Test: `tests/AcDream.App.Tests/UI/Hud/UiChatDeckInputTests.cs`

**Acceptance Criteria:**
- [ ] Plain text typed and submitted is sent as a `SendChatCmd` on the selected tab's send channel; a `/` or `@` command goes to the router unchanged; the line is remembered in the shared history and the draft cleared.
- [ ] The chip shows the channel's display name; picking a channel from its menu overrides it for the next line only; the override also ends when the tab changes or the model is replaced.
- [ ] The input shows the shared draft (set elsewhere) and writes its typing back to it; Up recalls from the shared history.
- [ ] Ctrl+Tab selects the next tab, Ctrl+Shift+Tab the previous, wrapping at both ends; Ctrl+A/C/X/V still work in the input.
- [ ] `EnterChatMode`, `ToggleChatEntry`, `StartCommand` ("/"), `StartTell` ("@tell Name, "), `StartReply` (nothing for no name) and `Focus` act on the Chat Deck's input as the retail chat does on its own.
- [ ] A click on a tell tag in the transcript starts a tell to that name; on a coordinate link it raises `LinkClicked`.
- [ ] `OnHidden()` takes the keyboard away from the input and search field if either had it, and closes the chip menu.
- [ ] `Detach()` stops listening to the shared draft.

**Verify:** `MAC_TEST_FILTER='FullyQualifiedName~UiChatDeck' ~/code/OpenAC/test-baseline/run-mac-tests.sh ~/code/OpenAC/OpenAC-chat-deck-window tests/AcDream.App.Tests/AcDream.App.Tests.csproj` → all pass

**Steps:**

- [ ] **Step 1: Write the failing tests**

```csharp
using AcDream.App.UI;
using AcDream.App.UI.Hud;
using AcDream.Core.Chat;
using AcDream.Core.Chat.Deck;
using AcDream.Runtime.Chat;
using Silk.NET.Input;
using Harness = AcDream.App.Tests.UI.Hud.UiChatDeckTests.Harness;

namespace AcDream.App.Tests.UI.Hud;

public sealed class UiChatDeckInputTests
{
    private static void Submit(Harness h, string text)
    {
        h.Deck.Input.SetText(text);
        h.Deck.Input.Submit();
    }

    private static SendChatCmd LastSent(Harness h) => Assert.IsType<SendChatCmd>(h.Bus.Published[^1]);

    private static void Key(UiField field, Key key) =>
        field.OnEvent(new UiEvent(0, field, UiEventType.KeyDown, Data0: (int)key));

    [Fact]
    public void Send_UsesTheTabsChannel_AndTheSharedEntry()
    {
        var h = new Harness();
        h.Press(h.Deck.Geometry.Tabs[h.TabIndex("group")]);
        h.Deck.Refresh();
        Assert.Equal("Fellowship", h.Deck.ChipLabel);

        Submit(h, "on my way");
        SendChatCmd sent = LastSent(h);
        Assert.Equal(ChatChannelKind.Fellowship, sent.Channel);
        Assert.Equal("on my way", sent.Text);
        Assert.Equal(1, h.Entry.Count);
        Assert.Equal("", h.Entry.Draft);
    }

    [Fact]
    public void ChipOverride_LastsOneLine()
    {
        var h = new Harness();
        h.Deck.ChooseChannel(ChatDeckSendChannel.General);
        h.Deck.Refresh();
        Assert.Equal("General", h.Deck.ChipLabel);

        Submit(h, "wts sword");
        Assert.Equal(ChatChannelKind.General, LastSent(h).Channel);
        Submit(h, "hello");
        Assert.Equal(ChatChannelKind.Say, LastSent(h).Channel);

        h.Deck.ChooseChannel(ChatDeckSendChannel.Trade);
        h.Press(h.Deck.Geometry.Tabs[h.TabIndex("local")]);
        Assert.Null(h.Deck.ChannelOverride);
    }

    [Fact]
    public void Commands_GoToTheRouterUnchanged()
    {
        var h = new Harness();
        h.Press(h.Deck.Geometry.Tabs[h.TabIndex("group")]);
        Submit(h, "@tell Bob, hi there");
        SendChatCmd sent = LastSent(h);
        Assert.Equal(ChatChannelKind.Tell, sent.Channel);
        Assert.Equal("Bob", sent.TargetName);
    }

    [Fact]
    public void Draft_IsShared_AndUpRecalls()
    {
        var h = new Harness();
        h.Entry.SetDraft("half typed");
        Assert.Equal("half typed", h.Deck.Input.Text);

        h.Deck.Input.SetText("typed here");
        Assert.Equal("typed here", h.Entry.Draft);

        h.Entry.Clear();
        h.Entry.Remember("an old line");
        Key(h.Deck.Input, Silk.NET.Input.Key.Up);
        Assert.Equal("an old line", h.Deck.Input.Text);
    }

    [Fact]
    public void CtrlTab_CyclesTabs()
    {
        var h = new Harness();
        h.Keyboard.Held.Add(Silk.NET.Input.Key.ControlLeft);
        Key(h.Deck.Input, Silk.NET.Input.Key.Tab);
        Assert.Equal("local", h.Model!.SelectedTabId);

        h.Keyboard.Held.Add(Silk.NET.Input.Key.ShiftLeft);
        Key(h.Deck.Input, Silk.NET.Input.Key.Tab);
        Key(h.Deck.Input, Silk.NET.Input.Key.Tab);
        Assert.Equal(h.Model.Settings.Tabs[^1].Id, h.Model.SelectedTabId);
    }

    [Fact]
    public void EntryTarget_ActsOnTheInput()
    {
        var h = new Harness();
        var root = new UiRoot { Width = 800, Height = 600 };
        root.AddChild(h.Deck);
        IChatEntryTarget target = h.Deck;

        target.StartTell("Bob");
        Assert.Equal("@tell Bob, ", h.Deck.Input.Text);
        Assert.Same(h.Deck.Input, root.KeyboardFocus);

        target.StartCommand();
        Assert.Equal("/", h.Deck.Input.Text);

        target.StartReply(null);
        Assert.Equal("/", h.Deck.Input.Text);

        root.SetKeyboardFocus(null);
        Assert.True(target.Focus());
        Assert.Same(h.Deck.Input, root.KeyboardFocus);

        target.ToggleChatEntry(null);
        Assert.Null(root.KeyboardFocus);
        target.ToggleChatEntry(null);
        Assert.Same(h.Deck.Input, root.KeyboardFocus);

        h.Deck.OnHidden();
        Assert.Null(root.KeyboardFocus);
    }

    [Fact]
    public void TellLink_StartsATell()
    {
        var h = new Harness();
        h.Model!.ApplySettings(h.Model.Settings with
        {
            Appearance = h.Model.Settings.Appearance with { Timestamps = ChatDeckTimestamps.Off },
        });
        h.Log.OnTellReceived("Bob", "meet me", 0x5000000Au, (uint)RetailLogTextType.Tell);
        h.Model!.Tick();
        h.Deck.Refresh();
        Assert.True(h.Deck.Transcript.OnCharClick!(new UiText.Pos(0, 1)));
        Assert.Equal("@tell Bob, ", h.Deck.Input.Text);
    }

    [Fact]
    public void Detach_StopsFollowingTheDraft()
    {
        var h = new Harness();
        h.Deck.Detach();
        h.Entry.SetDraft("elsewhere");
        Assert.NotEqual("elsewhere", h.Deck.Input.Text);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: the Verify command. Expected: build errors (`Input`, `ChipLabel`, `ChooseChannel`, `ChannelOverride`, `Detach` missing; `UiChatDeck` not an `IChatEntryTarget`).

- [ ] **Step 3: Implement**

Replace `UiChatDeck.Input.cs` with:

```csharp
using System.Numerics;
using AcDream.Core.Chat.Deck;
using AcDream.Runtime.Chat;
using AcDream.UI.Abstractions.Input;

namespace AcDream.App.UI.Hud;

internal sealed partial class UiChatDeck : IChatEntryTarget
{
    private readonly UiMarkupContextMenu _chipMenu = new();
    private ChatDeckSendChannel? _override;

    internal UiField Input { get; private set; } = null!;
    internal ChatDeckSendChannel? ChannelOverride => _override;
    internal string ChipLabel => ChipText;

    private ChatDeckSendChannel SendChannel =>
        _model?.SendChannelFor(_model.SelectedTabId, _override) ?? _override ?? ChatDeckSendChannel.Say;

    private string ChipText => ChatDeckSend.DisplayName(SendChannel);

    private void CreateInputRow()
    {
        Input = new UiField
        {
            OneLine = true,
            Selectable = true,
            Keyboard = _b.Keyboard,
            BackgroundColor = ChatDeckPalette.FieldFill,
            TextColor = ChatDeckPalette.TextInk,
            // The entry owner sends the line, remembers it and clears the draft;
            // the field keeps the caret, the selection and the drawing.
            RecordHistory = false,
            SharedHistory = _b.Entry,
            StayFocusedAfterSubmit = _b.StayInChatMode,
        };
        Input.TextReplacer = text => ChatTextReplacements.Expand(text, _b.LastIncomingTellSender());
        Input.OnSubmit = Send;
        Input.OnTextChanged = text => _b.Entry.SetDraft(text);
        Input.OnControlKey = (key, shift) => key == Silk.NET.Input.Key.Tab && CycleTab(shift ? -1 : 1);
        _b.Entry.DraftChanged += OnDraftChanged;
        Input.ShowText(_b.Entry.Draft);

        _chipMenu.ItemsSource = () => ChatDeckSend.All.Select(ChatDeckSend.DisplayName).ToArray();
        _chipMenu.Selected = index =>
        {
            if (index >= 0 && index < ChatDeckSend.All.Count) ChooseChannel(ChatDeckSend.All[index]);
            FindRoot()?.SetKeyboardFocus(Input);
        };

        AddChild(Input);
        AddChild(_chipMenu);
    }

    /// <summary>Stops following the shared draft; the deck is being torn down.</summary>
    internal void Detach() => _b.Entry.DraftChanged -= OnDraftChanged;

    private void OnDraftChanged(string text)
    {
        if (!string.Equals(Input.Text, text, StringComparison.Ordinal))
            Input.ShowText(text);
    }

    private void LayoutInputRow()
    {
        Place(Input, _geometry.Input);
        ChatDeckAppearance appearance = _model?.Settings.Appearance ?? new ChatDeckAppearance();
        Input.DatFont = _fonts?.Face(appearance.InputFontSize, BundledUiFontWeight.Regular);
    }

    /// <summary>Sends the next line on <paramref name="channel"/> instead of the tab's own (null: the tab's).</summary>
    internal void ChooseChannel(ChatDeckSendChannel? channel)
    {
        ChatDeckSendChannel tabChannel = _model?.SendChannelFor(_model.SelectedTabId, null) ?? ChatDeckSendChannel.Say;
        _override = channel == tabChannel ? null : channel;
    }

    private void OpenChipMenu(float x, float y)
    {
        Vector2 origin = ScreenPosition;
        _chipMenu.TryOpen((int)(origin.X + x), (int)(origin.Y + y));
    }

    private void Send(string text)
    {
        ChatChannelKind channel = ChatDeckSend.ToKind(SendChannel);
        _override = null;
        _b.Entry.SubmitTo(text, channel, _b.Feedback, _b.Bus());
    }

    private bool CycleTab(int step)
    {
        if (_model is null || _model.Settings.Tabs.Count == 0) return false;
        IReadOnlyList<ChatDeckTab> tabs = _model.Settings.Tabs;
        int at = Math.Max(0, tabs.ToList().FindIndex(t => t.Id == _model.SelectedTabId));
        SelectTab(((at + step) % tabs.Count + tabs.Count) % tabs.Count);
        return true;
    }

    private void OnInputHidden()
    {
        _chipMenu.Close(restoreFocus: false);
        if (FindRoot() is { } root
            && (ReferenceEquals(root.KeyboardFocus, Input) || ReferenceEquals(root.KeyboardFocus, SearchField)))
            root.SetKeyboardFocus(null);
    }

    private void OnViewReset() => _override = null;

    // ── IChatEntryTarget: the retail chat window's behaviour, on this input ──

    public void EnterChatMode(KeyChord? physicalChord)
    {
        UiRoot? root = FindRoot();
        root?.SetKeyboardFocus(Input);
        if (physicalChord is { Device: 0 } chord)
            root?.SuppressPhysicalKeyUntilRelease(chord.Key);
        Input.SelectAllText();
    }

    public void ToggleChatEntry(KeyChord? physicalChord)
    {
        if (FindRoot() is not { } root) return;
        root.SetKeyboardFocus(ReferenceEquals(root.KeyboardFocus, Input) ? null : Input);
        if (physicalChord is { Device: 0 } chord)
            root.SuppressPhysicalKeyUntilRelease(chord.Key);
    }

    public void StartCommand()
    {
        Input.SetText("/");
        FindRoot()?.SetKeyboardFocus(Input);
    }

    public void StartTell(string name)
    {
        Input.SetText($"@tell {name}, ");
        FindRoot()?.SetKeyboardFocus(Input);
    }

    public void StartReply(string? name)
    {
        if (!string.IsNullOrEmpty(name)) StartTell(name);
    }

    public bool Focus()
    {
        if (FindRoot() is not { } root) return false;
        root.SetKeyboardFocus(Input);
        return true;
    }

    private void DrawInputRow(UiRenderContext ctx)
    {
        DeckRect chip = _geometry.Chip;
        bool overridden = _override is not null;
        ctx.FillRoundedRect(chip.X, chip.Y, chip.W, chip.H, 5f,
            overridden ? ChatDeckPalette.StudOn : ChatDeckPalette.StudOff);
        DrawLabel(ctx, LabelBold, ChipText, chip.X + ChatDeckGeometry.TabPadX, chip,
            overridden ? ChatDeckPalette.TextInk : ChatDeckPalette.LabelInk);
        DrawIcon(ctx, HudIcons.ChevronDown, chip.X + chip.W - ChatDeckGeometry.ChipArrow - 2f,
            chip.Y + (chip.H - 10f) / 2f, 10f, ChatDeckPalette.StudOffInk);

        DeckRect input = _geometry.Input;
        ctx.FillRoundedRect(input.X, input.Y, input.W, input.H, 4f, ChatDeckPalette.FieldFill);
    }
}
```

`ChatTextReplacements` is in `AcDream.Runtime.Chat`. `KeyChord` is in `AcDream.UI.Abstractions.Input`. Check `UiRoot.SuppressPhysicalKeyUntilRelease`'s parameter type against `ChatWindowController.EnterChatMode` and do the same.

Remove the Task 9 stubs that this file now defines (`ChipText`, `CreateInputRow`, `LayoutInputRow`, `OnInputHidden`, `OnViewReset`, `OpenChipMenu`, `StartTell`, `DrawInputRow`).

- [ ] **Step 4: Run tests to verify they pass**

Run: the Verify command. Expected: all `UiChatDeck*` tests pass.

- [ ] **Step 5: Commit**

```bash
git add src/AcDream.App/UI/Hud/UiChatDeck.Input.cs tests/AcDream.App.Tests/UI/Hud/UiChatDeckInputTests.cs
git commit -m "Chat Deck: input row with per-tab channel, one-line override, shared draft and entry target

Claude-Session: https://claude.ai/code/session_01WFS4EHSNafq2P1qfUuh258"
```

---

### Task 11: Visibility hold for replaced windows

**Goal:** While the Chat Deck stands in for the retail chat and floaties, their saved visibility stays the player's: hiding them does not save "hidden", a login restore records the saved visibility without showing them, and letting go returns the visibility they should go back to.

**Files:**
- Modify: `src/AcDream.App/UI/RetailWindowLayoutPersistence.cs`
- Test: `tests/AcDream.App.Tests/UI/RetailWindowLayoutPersistenceTests.VisibilityHold.cs`

**Acceptance Criteria:**
- [ ] `HoldVisibility(name)` records the window's current visibility; repeated holds keep the first record.
- [ ] While held, a hide or show of the window saves its placement with the recorded visibility, not the current one.
- [ ] While held, `RestoreAll` and `RestoreNamed` record the saved visibility for the window and leave it hidden (geometry still restores).
- [ ] While held, `SaveNamed` writes the recorded visibility.
- [ ] `ReleaseVisibility(name)` ends the hold and returns the recorded visibility (the current visibility when not held).
- [ ] Windows that are not held behave exactly as before (existing persistence tests pass).

**Verify:** `MAC_TEST_FILTER='FullyQualifiedName~RetailWindowLayoutPersistence' ~/code/OpenAC/test-baseline/run-mac-tests.sh ~/code/OpenAC/OpenAC-chat-deck-window tests/AcDream.App.Tests/AcDream.App.Tests.csproj` → all pass

**Steps:**

- [ ] **Step 1: Write the failing tests**

```csharp
using AcDream.App.UI;
using AcDream.UI.Abstractions.Panels.Settings;

namespace AcDream.App.Tests.UI;

public sealed partial class RetailWindowLayoutPersistenceTests
{
    private static (UiRoot Root, UiPanel Chat) MountChat(bool visible = true)
    {
        var root = new UiRoot { Width = 1600, Height = 900 };
        var chat = new UiPanel { Width = 300, Height = 200, Left = 10, Top = 600, Visible = visible };
        root.AddChild(chat);
        root.RegisterWindow(WindowNames.Chat, chat, chat, null);
        return (root, chat);
    }

    private static bool SavedVisible(SettingsStore store) =>
        store.LoadWindowLayout("Alice", "1600x900", WindowNames.Chat, default)!.Value.Visible;

    [Fact]
    public void Held_HideDoesNotSaveHidden()
    {
        var store = new SettingsStore(PathName);
        var (root, _) = MountChat();
        using var persistence = new RetailWindowLayoutPersistence(root.WindowManager, store, () => "Alice", () => (1600, 900));
        persistence.RestoreAll();

        persistence.HoldVisibility(WindowNames.Chat);
        root.HideWindow(WindowNames.Chat);
        Assert.True(root.WindowManager.TryGet(WindowNames.Chat, out RetailWindowHandle? handle));
        handle!.MoveTo(40, 500);   // a geometry save while held

        Assert.True(SavedVisible(store));
        Assert.True(persistence.ReleaseVisibility(WindowNames.Chat));
    }

    [Fact]
    public void Held_RestoreRecordsButDoesNotShow()
    {
        var store = new SettingsStore(PathName);
        var (root, _) = MountChat();
        using (var first = new RetailWindowLayoutPersistence(root.WindowManager, store, () => "Alice", () => (1600, 900)))
            first.RestoreAll();   // saves chat visible

        var (root2, chat2) = MountChat(visible: false);
        using var persistence = new RetailWindowLayoutPersistence(root2.WindowManager, store, () => "Alice", () => (1600, 900));
        persistence.HoldVisibility(WindowNames.Chat);   // recorded hidden
        persistence.RestoreAll();

        Assert.False(chat2.Visible);
        Assert.True(persistence.ReleaseVisibility(WindowNames.Chat));   // the restore recorded "visible"
    }

    [Fact]
    public void Held_NamedLayoutsUseTheRecordedVisibility()
    {
        var store = new SettingsStore(PathName);
        var (root, chat) = MountChat();
        using var persistence = new RetailWindowLayoutPersistence(root.WindowManager, store, () => "Alice", () => (1600, 900));
        persistence.RestoreAll();
        persistence.HoldVisibility(WindowNames.Chat);
        root.HideWindow(WindowNames.Chat);

        persistence.SaveNamed("raid");
        Assert.True(store.LoadNamedWindowLayout("raid", WindowNames.Chat, default)!.Value.Visible);

        persistence.RestoreNamed("raid");
        Assert.False(chat.Visible);
    }

    [Fact]
    public void Release_WhenNotHeld_IsTheCurrentVisibility()
    {
        var (root, _) = MountChat(visible: false);
        using var persistence = new RetailWindowLayoutPersistence(root.WindowManager, null, () => "Alice", () => (1600, 900));
        Assert.False(persistence.ReleaseVisibility(WindowNames.Chat));
    }
}
```

Check `UiRoot.RegisterWindow`'s parameters (`UiRoot.cs:1256`), `LoadNamedWindowLayout`'s signature (`SettingsStore.cs:770`) and how the other partial files make `PathName`; adjust the calls to match.

- [ ] **Step 2: Run tests to verify they fail**

Run: the Verify command. Expected: build errors (`HoldVisibility`, `ReleaseVisibility` missing).

- [ ] **Step 3: Implement**

In `RetailWindowLayoutPersistence`:

```csharp
    private readonly Dictionary<string, bool> _heldVisibility = new(StringComparer.Ordinal);

    /// <summary>
    /// Hands a window's visibility to whatever stands in for it (a Modern HUD
    /// window): from now its own shows and hides are not saved, a restore records
    /// the saved visibility instead of applying it, and every save writes the
    /// visibility recorded here. A second hold keeps the first record.
    /// </summary>
    public void HoldVisibility(string windowName)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(windowName);
        if (_heldVisibility.ContainsKey(windowName)) return;
        RetailWindowHandle? handle = _attached.Find(h => h.Name == windowName);
        _heldVisibility[windowName] = handle is not null && Capture(handle).Visible;
    }

    /// <summary>Ends a hold; the visibility the window should go back to.</summary>
    public bool ReleaseVisibility(string windowName)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(windowName);
        return _heldVisibility.Remove(windowName, out bool visible)
            ? visible
            : _attached.Find(h => h.Name == windowName)?.IsVisible ?? false;
    }

    /// <summary>The window's layout as it should be saved: a held window keeps its recorded visibility.</summary>
    private UiWindowLayout CaptureForSave(RetailWindowHandle handle)
    {
        UiWindowLayout layout = Capture(handle);
        return _heldVisibility.TryGetValue(handle.Name, out bool visible) ? layout with { Visible = visible } : layout;
    }
```

Then:
- `WithCurrentState` becomes an instance method and reads `var current = CaptureForSave(handle);`.
- In `RestoreHandle(handle, fallback, saved, screen, restoreVisibility)`, after `_placements[handle.Name] = placement;` add `bool held = _heldVisibility.ContainsKey(handle.Name); if (held) _heldVisibility[handle.Name] = placement.Layout.Visible;` and pass `restoreVisibility && !held && !_stateManagedVisibilityWindows.Contains(handle.Name)` to `ApplyPlacement`.
- In `RestoreNamed`, likewise record `layout.Visible` for a held window and pass `restoreVisibility: !held && !_stateManagedVisibilityWindows.Contains(handle.Name)`; build the stored placement with `CaptureForSave(handle)`.
- In `SaveNamed`, write `CaptureForSave(handle)` instead of `Capture(handle)`.
- Any other place that saves `Capture(handle)` for a placement (the batched `SaveAll` path) uses `CaptureForSave`.

If the Spell Deck's capture gates are present (`Capturing(handle)`), keep them as they are; the visibility hold is independent of them.

- [ ] **Step 4: Run tests to verify they pass**

Run: the Verify command. Expected: all pass.

- [ ] **Step 5: Commit**

```bash
git add src/AcDream.App/UI/RetailWindowLayoutPersistence.cs tests/AcDream.App.Tests/UI/RetailWindowLayoutPersistenceTests.VisibilityHold.cs
git commit -m "Chat Deck: window layout keeps a replaced window's saved visibility

Claude-Session: https://claude.ai/code/session_01WFS4EHSNafq2P1qfUuh258"
```

---

### Task 12: Modern HUD switch as a list of replacements

**Goal:** `ModernHudSwitch` handles each Modern HUD window as its own replacement with its own retail windows and mounted state, so the Chat Deck can take over the retail chat and floaties independently of the Command Deck.

**Files:**
- Modify: `src/AcDream.App/UI/Hud/ModernHudSwitch.cs`
- Modify: `src/AcDream.App/UI/RetailUiRuntime.cs` (the switch's construction, ~line 520; nothing else yet)
- Test: `tests/AcDream.App.Tests/UI/Hud/ModernHudSwitchTests.cs`, `tests/AcDream.App.Tests/UI/Hud/ModernHudWiringTests.cs` (update constructors)

**Acceptance Criteria:**
- [ ] Each replacement (`ModernHudReplacement`: name, window, retail windows, mounted, on-shown, on-hidden, optional resync, whether it holds retail visibility) is applied on its own: turning Modern HUD on hides a replacement's retail windows only when it is mounted; one that is not mounted logs once and leaves its retail windows alone, and the others still apply.
- [ ] `Active` is true while Modern HUD is on and at least one replacement took over; `GatedShow` / `GateVisibility` refuse to show a retail window only while its own replacement is in force.
- [ ] `Tick` shows each replacement's window while it is in force and gameplay is on screen, and hides it otherwise (calling its on-shown / on-hidden once per change); for a replacement that holds retail visibility, a retail window that became visible is hidden again.
- [ ] Turning off hides each replacement, calls its resync, and for one that holds retail visibility shows each retail window whose released visibility is true.
- [ ] `IsShown(window)` says whether a replacement's window is shown now.
- [ ] The Command Deck's behaviour is unchanged (the existing switch and wiring tests pass after their constructors are updated).

**Verify:** `MAC_TEST_FILTER='FullyQualifiedName~ModernHud' ~/code/OpenAC/test-baseline/run-mac-tests.sh ~/code/OpenAC/OpenAC-chat-deck-window tests/AcDream.App.Tests/AcDream.App.Tests.csproj` → all pass

**Steps:**

- [ ] **Step 1: Update the harness and write the new tests**

Replace the `Harness` in `ModernHudSwitchTests` with:

```csharp
    private sealed class Harness
    {
        public readonly HashSet<string> Visible = [WindowNames.Vitals, WindowNames.Combat, WindowNames.Chat];
        public readonly List<string> Log = [];
        public readonly List<string> Calls = [];
        public readonly Dictionary<string, bool> Held = [];
        public bool Mounted = true;
        public bool ChatMounted = true;
        public bool Gameplay = true;
        public int Resyncs;
        public int DeckHidden;
        public ModernHudSwitch Switch = null!;

        public Harness(bool withChat = false)
        {
            var replacements = new List<ModernHudReplacement>
            {
                new("Command Deck", WindowNames.CommandDeck, ModernHudSwitch.CommandDeckRetailWindows,
                    () => Mounted, OnShown: () => { }, OnHidden: () => DeckHidden++, ResyncRetail: () => Resyncs++),
            };
            if (withChat)
                replacements.Add(new("Chat Deck", WindowNames.ChatDeck, ModernHudSwitch.ChatDeckRetailWindows,
                    () => ChatMounted, OnShown: () => Calls.Add("chat shown"), OnHidden: () => Calls.Add("chat hidden"),
                    HoldsRetailVisibility: true));
            Switch = new ModernHudSwitch(
                show: n => Visible.Add(n),
                hide: n => Visible.Remove(n),
                isVisible: Visible.Contains,
                gameplayActive: () => Gameplay,
                replacements,
                holdVisibility: n => Held.TryAdd(n, Visible.Contains(n)),
                releaseVisibility: n => Held.Remove(n, out bool v) && v,
                log: Log.Add);
        }
    }
```

Existing tests keep using `new Harness()`. Add `WindowNames.ChatDeck = "chat-deck"` to `WindowNames.cs` now (Task 13 uses it too). Then add:

```csharp
    [Fact]
    public void ChatDeck_HidesAndRestoresTheRetailChat()
    {
        var h = new Harness(withChat: true);
        h.Switch.Apply(true);
        Assert.DoesNotContain(WindowNames.Chat, h.Visible);
        Assert.Contains(WindowNames.ChatDeck, h.Visible);
        Assert.True(h.Switch.IsShown(WindowNames.ChatDeck));
        Assert.Equal(["chat shown"], h.Calls);
        Assert.True(h.Held[WindowNames.Chat]);
        Assert.False(h.Held[WindowNames.ChatWindow1]);

        h.Switch.Apply(false);
        Assert.Contains(WindowNames.Chat, h.Visible);
        Assert.DoesNotContain(WindowNames.ChatWindow1, h.Visible);
        Assert.DoesNotContain(WindowNames.ChatDeck, h.Visible);
        Assert.Equal(["chat shown", "chat hidden"], h.Calls);
        Assert.Empty(h.Held);
    }

    [Fact]
    public void ChatDeck_RetailChatShownWhileActive_IsHiddenAgain()
    {
        var h = new Harness(withChat: true);
        h.Switch.Apply(true);
        h.Visible.Add(WindowNames.ChatWindow2);
        h.Switch.Tick();
        Assert.DoesNotContain(WindowNames.ChatWindow2, h.Visible);
    }

    [Fact]
    public void ChatDeckNotMounted_KeepsRetailChat_CommandDeckStillApplies()
    {
        var h = new Harness(withChat: true) { ChatMounted = false };
        h.Switch.Apply(true);
        Assert.True(h.Switch.Active);
        Assert.Contains(WindowNames.Chat, h.Visible);
        Assert.DoesNotContain(WindowNames.Vitals, h.Visible);
        Assert.Contains(WindowNames.CommandDeck, h.Visible);
        Assert.Single(h.Log);
        Assert.True(h.Switch.GatedShow(WindowNames.Chat));
    }

    [Fact]
    public void CommandDeckNotMounted_ChatDeckStillApplies()
    {
        var h = new Harness(withChat: true) { Mounted = false };
        h.Switch.Apply(true);
        Assert.Contains(WindowNames.Vitals, h.Visible);
        Assert.DoesNotContain(WindowNames.Chat, h.Visible);
        Assert.True(h.Switch.IsShown(WindowNames.ChatDeck));
        Assert.True(h.Switch.GatedShow(WindowNames.SideVitals));
    }

    [Fact]
    public void ChatDeck_FollowsGameplay()
    {
        var h = new Harness(withChat: true);
        h.Switch.Apply(true);
        h.Gameplay = false;
        h.Switch.Tick();
        Assert.False(h.Switch.IsShown(WindowNames.ChatDeck));
        Assert.DoesNotContain(WindowNames.Chat, h.Visible);
        h.Gameplay = true;
        h.Switch.Tick();
        Assert.Equal(["chat shown", "chat hidden", "chat shown"], h.Calls);
    }
```

In `ModernHudWiringTests`, build the switch the same way (one Command Deck replacement whose resync is `vitals!.SyncVisibility()` and whose `isVisible` is `root.IsWindowVisible`).

- [ ] **Step 2: Run tests to verify they fail**

Run: the Verify command. Expected: build errors.

- [ ] **Step 3: Implement**

```csharp
namespace AcDream.App.UI.Hud;

/// <summary>A Modern HUD window and the retail windows it stands in for.</summary>
/// <param name="Name">For the log line when it did not mount ("Command Deck").</param>
/// <param name="ResyncRetail">After Modern HUD turns off: lets the retail controllers show what they own.</param>
/// <param name="HoldsRetailVisibility">The retail windows' visibility is the player's, kept for them while
/// replaced and put back after (the chat windows); otherwise their controllers decide it (the vitals).</param>
public sealed record ModernHudReplacement(
    string Name,
    string Window,
    IReadOnlyList<string> RetailWindows,
    Func<bool> Mounted,
    Action OnShown,
    Action OnHidden,
    Action? ResyncRetail = null,
    bool HoldsRetailVisibility = false);

/// <summary>
/// Turns Modern HUD on and off. Each replacement that mounted takes over its
/// retail windows while Modern HUD is on: they cannot be shown (their
/// controllers' show requests go through <see cref="GateVisibility"/> /
/// <see cref="GatedShow"/>, and a chat window shown anyway is hidden again on
/// the next tick), and the replacement is shown while gameplay is on screen.
/// A replacement that did not mount leaves its retail windows alone; the
/// others still apply.
/// </summary>
public sealed class ModernHudSwitch
{
    public static IReadOnlyList<string> CommandDeckRetailWindows { get; } =
    [
        WindowNames.Vitals,
        WindowNames.SideVitals,
        WindowNames.Combat,
        WindowNames.JumpPowerbar,
    ];

    public static IReadOnlyList<string> ChatDeckRetailWindows { get; } =
    [
        WindowNames.Chat,
        WindowNames.ChatWindow1,
        WindowNames.ChatWindow2,
        WindowNames.ChatWindow3,
        WindowNames.ChatWindow4,
    ];

    private readonly Func<string, bool> _show;
    private readonly Func<string, bool> _hide;
    private readonly Func<string, bool> _isVisible;
    private readonly Func<bool> _gameplayActive;
    private readonly IReadOnlyList<ModernHudReplacement> _replacements;
    private readonly Action<string> _holdVisibility;
    private readonly Func<string, bool>? _releaseVisibility;
    private readonly Action<string> _log;
    private readonly HashSet<string> _inForce = new(StringComparer.Ordinal);
    private readonly HashSet<string> _shown = new(StringComparer.Ordinal);
    private readonly HashSet<string> _reportedMountFailure = new(StringComparer.Ordinal);
    private readonly Dictionary<string, bool> _wasVisible = new(StringComparer.Ordinal);
    private bool? _requested;

    public ModernHudSwitch(
        Func<string, bool> show,
        Func<string, bool> hide,
        Func<string, bool> isVisible,
        Func<bool> gameplayActive,
        IReadOnlyList<ModernHudReplacement> replacements,
        Action<string>? holdVisibility = null,
        Func<string, bool>? releaseVisibility = null,
        Action<string>? log = null)
    {
        _show = show ?? throw new ArgumentNullException(nameof(show));
        _hide = hide ?? throw new ArgumentNullException(nameof(hide));
        _isVisible = isVisible ?? throw new ArgumentNullException(nameof(isVisible));
        _gameplayActive = gameplayActive ?? throw new ArgumentNullException(nameof(gameplayActive));
        _replacements = replacements ?? throw new ArgumentNullException(nameof(replacements));
        _holdVisibility = holdVisibility ?? (_ => { });
        _releaseVisibility = releaseVisibility;
        _log = log ?? Console.WriteLine;
    }

    /// <summary>Modern HUD is on and at least one replacement took over.</summary>
    public bool Active => _inForce.Count > 0;

    /// <summary>Whether a replacement's window is on screen now.</summary>
    public bool IsShown(string window) => _shown.Contains(window);

    /// <summary>A retail controller's <c>setWindowVisible</c> delegate, gated.</summary>
    public Action<bool> GateVisibility(string window) => visible =>
    {
        if (!visible) _hide(window);
        else if (!IsReplaced(window)) _show(window);
    };

    /// <summary>A show call that does nothing while the window is replaced.</summary>
    public bool GatedShow(string window) => !IsReplaced(window) && _show(window);

    private bool IsReplaced(string retailWindow) =>
        _replacements.Any(r => _inForce.Contains(r.Window) && r.RetailWindows.Contains(retailWindow));

    public void Apply(bool enabled)
    {
        if (_requested == enabled) return;
        _requested = enabled;

        if (enabled)
        {
            foreach (ModernHudReplacement r in _replacements)
            {
                if (!r.Mounted())
                {
                    if (_reportedMountFailure.Add(r.Window))
                        _log($"[UI] modern HUD: {r.Name} unavailable; keeping the retail windows.");
                    continue;
                }
                _inForce.Add(r.Window);
                foreach (string window in r.RetailWindows)
                {
                    if (r.HoldsRetailVisibility)
                    {
                        _wasVisible.TryAdd(window, _isVisible(window));
                        _holdVisibility(window);
                    }
                    _hide(window);
                }
            }
            Tick();
            return;
        }

        foreach (ModernHudReplacement r in _replacements)
        {
            if (!_inForce.Remove(r.Window)) continue;
            SetShown(r, false);
            if (r.HoldsRetailVisibility)
            {
                foreach (string window in r.RetailWindows)
                {
                    bool was = _wasVisible.Remove(window, out bool recorded) && recorded;
                    if (_releaseVisibility?.Invoke(window) ?? was) _show(window);
                }
            }
            r.ResyncRetail?.Invoke();
        }
    }

    /// <summary>Keeps each replacement shown only while it is in force and gameplay is on screen.</summary>
    public void Tick()
    {
        bool gameplay = _gameplayActive();
        foreach (ModernHudReplacement r in _replacements)
        {
            bool inForce = _inForce.Contains(r.Window);
            SetShown(r, inForce && gameplay);
            if (!inForce || !r.HoldsRetailVisibility) continue;
            foreach (string window in r.RetailWindows)
                if (_isVisible(window)) _hide(window);
        }
    }

    private void SetShown(ModernHudReplacement r, bool shown)
    {
        if (shown == _shown.Contains(r.Window)) return;
        if (shown)
        {
            _shown.Add(r.Window);
            _show(r.Window);
            r.OnShown();
        }
        else
        {
            _shown.Remove(r.Window);
            _hide(r.Window);
            r.OnHidden();
        }
    }
}
```

In `RetailUiRuntime.Initialize`, build the switch with one replacement for now (Task 13 adds the Chat Deck):

```csharp
        _hudSwitch = new Hud.ModernHudSwitch(
            show: Host.ShowWindow,
            hide: Host.HideWindow,
            isVisible: Host.IsWindowVisible,
            gameplayActive: () => _bindings.IsGameplayDisplay?.Invoke() ?? true,
            [
                new Hud.ModernHudReplacement(
                    "Command Deck", WindowNames.CommandDeck, Hud.ModernHudSwitch.CommandDeckRetailWindows,
                    Mounted: () => _commandDeck is not null,
                    OnShown: () => { },
                    OnHidden: () => _commandDeck?.OnHidden(),
                    ResyncRetail: () =>
                    {
                        CombatUiController?.SyncVisibility();
                        JumpPowerbarController?.SyncVisibility();
                        _vitalsSideBySide?.SyncVisibility();
                    }),
            ],
            holdVisibility: window => _persistence?.HoldVisibility(window),
            releaseVisibility: window => _persistence?.ReleaseVisibility(window) ?? false);
```

`_persistence` is always created in `Initialize` before the first `Apply` runs, so a hold always reaches it; the switch's own record is only the fallback for a switch built without persistence (tests).

`ModernHudSwitch.RetailWindows` is gone; replace every use with `CommandDeckRetailWindows` (`grep -rn "ModernHudSwitch.RetailWindows" src tests`).

If the Spell Deck is on main: keep its `SpellDeckActive`, `magicMode` and Spell Deck show/hide in this class (it shows with the Command Deck's replacement in magic mode, after `SetShown` for the Command Deck in `Tick`, and is hidden before the Command Deck on turn-off), and keep its constructor arguments as optional parameters after `log`.

- [ ] **Step 4: Run tests to verify they pass**

Run: the Verify command, then `MAC_TEST_FILTER='FullyQualifiedName~UI' ~/code/OpenAC/test-baseline/run-mac-tests.sh ~/code/OpenAC/OpenAC-chat-deck-window tests/AcDream.App.Tests/AcDream.App.Tests.csproj`. Expected: all pass.

- [ ] **Step 5: Commit**

```bash
git add src/AcDream.App/UI/Hud/ModernHudSwitch.cs src/AcDream.App/UI/RetailUiRuntime.cs src/AcDream.App/UI/WindowNames.cs tests/AcDream.App.Tests/UI/Hud/ModernHudSwitchTests.cs tests/AcDream.App.Tests/UI/Hud/ModernHudWiringTests.cs
git commit -m "Chat Deck: Modern HUD switch handles each deck as its own replacement

Claude-Session: https://claude.ai/code/session_01WFS4EHSNafq2P1qfUuh258"
```

---

### Task 13: Mount and wire the Chat Deck

**Goal:** Mount the Chat Deck in the client: its window, its per-character session ticked every frame, its replacement in the Modern HUD switch, the chat-entry actions and Enter going to it while it is shown, and the session ended on logout.

**Files:**
- Modify: `src/AcDream.App/UI/RetailUiRuntime.cs` (`ChatRuntimeBindings`, fields, `Initialize`, `Tick`, `ResetSessionTransientUi`, `ChatEntryTarget`, dispose), `src/AcDream.App/UI/Hud/UiChatDeck.cs` (`MountOptions`), `src/AcDream.App/UI/WindowPlacementGeometry.cs` (default place), `src/AcDream.App/Composition/InteractionRetainedUiComposition.cs` (pass the `ChatLog`)
- Test: `tests/AcDream.App.Tests/UI/Hud/ChatDeckWiringTests.cs`, `tests/AcDream.App.Tests/UI/WindowPlacementGeometryTests.cs` (add a case if the file exists; otherwise put the case in the wiring tests)

**Acceptance Criteria:**
- [ ] `ChatRuntimeBindings` carries the `ChatLog` (`Log`), set by the composition from `d.Communication.Chat`.
- [ ] `MountChatDeck` (after `MountCommandDeck`) mounts a `UiChatDeck` as window `chat-deck` (imported chrome, resizable every edge, 280×140 minimum, clamped to the screen, hidden until the switch shows it) and creates its `ChatDeckSession`; if mounting throws, it logs once and the retail chat stays.
- [ ] The switch has a Chat Deck replacement (holds retail visibility; on shown: `deck.OnShown()` and the root's default text input becomes the deck's input; on hidden: `deck.OnHidden()` and the default text input is cleared if it was the deck's).
- [ ] `chat-deck` is in the persistence's state-managed visibility list; its default place is the retail chat's (left margin, bottom).
- [ ] `RetailUiRuntime.Tick` ticks the session every frame, whether Modern HUD is on or not; `ResetSessionTransientUi` ends the session; dispose detaches the deck and disposes the session.
- [ ] `ChatEntryTarget` is the deck while the switch shows it, the retail chat otherwise; the shared entry's focus binding goes through `ChatEntryTarget`, so a composed line lands in whichever is showing.
- [ ] Headless wiring tests: Modern HUD on hides the retail chat and floaties and shows the deck with its input as the default text input; off brings the retail chat back as it was (and its input as the default); a retail chat shown while the deck is up is hidden again; the deck not mounted leaves the retail chat and the Command Deck replacement working; the retail chat's saved visibility stays "visible" throughout.

**Verify:** `MAC_TEST_FILTER='FullyQualifiedName~ChatDeck|FullyQualifiedName~ModernHud|FullyQualifiedName~WindowPlacement' ~/code/OpenAC/test-baseline/run-mac-tests.sh ~/code/OpenAC/OpenAC-chat-deck-window tests/AcDream.App.Tests/AcDream.App.Tests.csproj` → all pass

**Steps:**

- [ ] **Step 1: Write the failing wiring tests**

These mirror what `RetailUiRuntime` builds, with plain panels for the retail windows (as `ModernHudWiringTests` does) and the real deck and persistence:

```csharp
using AcDream.App.UI;
using AcDream.App.UI.Hud;
using AcDream.App.UI.Layout;
using AcDream.Core.Chat;
using AcDream.Core.Chat.Deck;
using AcDream.Runtime.Chat;
using AcDream.UI.Abstractions.Panels.Settings;

namespace AcDream.App.Tests.UI.Hud;

public sealed class ChatDeckWiringTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"acdream-chatdeck-wiring-{Guid.NewGuid():N}.json");

    public void Dispose()
    {
        if (File.Exists(_path)) File.Delete(_path);
    }

    private sealed class World
    {
        public readonly UiRoot Root = new() { Width = 1600, Height = 900 };
        public readonly UiField RetailInput = new();
        public readonly UiPanel Chat = new() { Width = 300, Height = 200, Left = 10, Top = 690 };
        public readonly UiPanel[] Floaties = [new(), new(), new(), new()];
        public readonly UiChatDeck Deck;
        public readonly RetailWindowLayoutPersistence Persistence;
        public readonly ModernHudSwitch Switch;
        public bool DeckMounted = true;

        public World(string path)
        {
            Chat.AddChild(RetailInput);
            Root.AddChild(Chat);
            Root.RegisterWindow(WindowNames.Chat, Chat, Chat, null);
            string[] floatyNames = [WindowNames.ChatWindow1, WindowNames.ChatWindow2, WindowNames.ChatWindow3, WindowNames.ChatWindow4];
            for (int i = 0; i < 4; i++)
            {
                Floaties[i].Visible = false;
                Root.AddChild(Floaties[i]);
                Root.RegisterWindow(floatyNames[i], Floaties[i], Floaties[i], null);
            }
            Root.DefaultTextInput = RetailInput;

            var log = new ChatLog();
            var model = new ChatDeckModel(log, ChatDeckDefaults.Create(), "Alice");
            Deck = new UiChatDeck(new ChatDeckBindings(
                () => model, new RuntimeChatEntryOwner(), new UiChatDeckTests.Feedback(), () => new UiChatDeckTests.RecordingBus(),
                () => null, () => false), fonts: null, icons: null);
            RetailWindowFrame.Mount(Root, Deck, _ => (0u, 0, 0), UiChatDeck.MountOptions(10f, 670f));

            Persistence = new RetailWindowLayoutPersistence(
                Root.WindowManager, new SettingsStore(path), () => "Alice", () => (1600, 900),
                stateManagedVisibilityWindows: [WindowNames.ChatDeck]);
            Persistence.RestoreAll();

            Switch = new ModernHudSwitch(
                Root.ShowWindow, Root.HideWindow, Root.IsWindowVisible, () => true,
                [
                    new ModernHudReplacement("Chat Deck", WindowNames.ChatDeck, ModernHudSwitch.ChatDeckRetailWindows,
                        () => DeckMounted,
                        OnShown: () => { Deck.OnShown(); Root.DefaultTextInput = Deck.Input; },
                        OnHidden: () =>
                        {
                            Deck.OnHidden();
                            if (ReferenceEquals(Root.DefaultTextInput, Deck.Input)) Root.DefaultTextInput = null;
                        },
                        HoldsRetailVisibility: true),
                ],
                holdVisibility: Persistence.HoldVisibility,
                releaseVisibility: Persistence.ReleaseVisibility);
        }
    }

    private bool SavedChatVisible() =>
        new SettingsStore(_path).LoadWindowLayout("Alice", "1600x900", WindowNames.Chat, default)!.Value.Visible;

    [Fact]
    public void On_ReplacesTheRetailChat_Off_BringsItBack()
    {
        var w = new World(_path);
        w.Switch.Apply(true);
        Assert.False(w.Chat.Visible);
        Assert.All(w.Floaties, f => Assert.False(f.Visible));
        Assert.True(w.Root.IsWindowVisible(WindowNames.ChatDeck));
        Assert.Same(w.Deck.Input, w.Root.DefaultTextInput);
        Assert.True(SavedChatVisible());

        w.Switch.Apply(false);
        Assert.True(w.Chat.Visible);
        Assert.All(w.Floaties, f => Assert.False(f.Visible));
        Assert.False(w.Root.IsWindowVisible(WindowNames.ChatDeck));
        Assert.Same(w.RetailInput, w.Root.DefaultTextInput);
        Assert.True(SavedChatVisible());
    }

    [Fact]
    public void RetailChatShownWhileUp_IsHiddenAgain()
    {
        var w = new World(_path);
        w.Switch.Apply(true);
        w.Root.ShowWindow(WindowNames.Chat);
        w.Switch.Tick();
        Assert.False(w.Chat.Visible);
        Assert.True(SavedChatVisible());
    }

    [Fact]
    public void NotMounted_LeavesTheRetailChat()
    {
        var w = new World(_path) { DeckMounted = false };
        w.Switch.Apply(true);
        Assert.True(w.Chat.Visible);
        Assert.Same(w.RetailInput, w.Root.DefaultTextInput);
    }

    [Fact]
    public void MountOptions_ResizeEverySide_WithTheMinimum()
    {
        RetailWindowFrame.Options o = UiChatDeck.MountOptions(10f, 20f);
        Assert.Equal(WindowNames.ChatDeck, o.WindowName);
        Assert.True(o.Resizable && o.ResizeX && o.ResizeY && o.ConstrainResizeToParent);
        Assert.Equal(ChatDeckGeometry.MinWidth, o.MinWidth);
        Assert.Equal(ChatDeckGeometry.MinHeight, o.MinHeight);
        Assert.False(o.Visible);
    }
}
```

Make `Feedback` and `RecordingBus` in `UiChatDeckTests` `internal` (they already are in Task 9's code) so this file can use them. Check the restore-then-default-input sequence against `RetailWindowManager.PrepareToHide` / `OnElementVisibilityChanged` (`RetailWindowManager.cs:370-405`): the retail input comes back as the default text input when the retail chat is shown again only if it was the default when the chat was hidden, which the test sets up.

- [ ] **Step 2: Run tests to verify they fail**

Run: the Verify command. Expected: build error (`UiChatDeck.MountOptions` missing).

- [ ] **Step 3: `MountOptions` and the default place**

In `UiChatDeck`:

```csharp
    /// <summary>How the runtime mounts the Chat Deck: imported chrome, resizable from every side, hidden until shown.</summary>
    internal static RetailWindowFrame.Options MountOptions(float left, float top) => new()
    {
        WindowName = WindowNames.ChatDeck,
        Chrome = RetailWindowChrome.Imported,
        Left = left,
        Top = top,
        Resizable = true,
        ResizeX = true,
        ResizeY = true,
        ConstrainResizeToParent = true,
        MinWidth = ChatDeckGeometry.MinWidth,
        MinHeight = ChatDeckGeometry.MinHeight,
        Visible = false,
        ContentClickThrough = false,
    };
```

In `WindowPlacementGeometry.Default`, add `WindowNames.ChatDeck => (margin, bottom),` beside `WindowNames.Chat`.

- [ ] **Step 4: Wire `RetailUiRuntime`**

1. `ChatRuntimeBindings` gains a last parameter `AcDream.Core.Chat.ChatLog? Log = null`. In `InteractionRetainedUiComposition` (~line 646) pass `Log: d.Communication.Chat` (check it is the `ChatLog` the `ChatVM` is built from in `CreateChatViewModel`).

2. Fields beside `_commandDeck`:

```csharp
    private Hud.UiChatDeck? _chatDeck;
    private Hud.ChatDeckSession? _chatDeckSession;
```

and, if the file has no `_deckFonts` field yet (Spell Deck not merged), add `private Hud.DeckFonts? _deckFonts;` and have `MountCommandDeck` assign it (`_deckFonts = Hud.DeckFonts.Load(_bindings.Assets.TextureCache)` where it now passes `Hud.DeckFonts.Load(...)` inline).

3. The switch gets the Chat Deck replacement after the Command Deck's:

```csharp
                new Hud.ModernHudReplacement(
                    "Chat Deck", WindowNames.ChatDeck, Hud.ModernHudSwitch.ChatDeckRetailWindows,
                    Mounted: () => _chatDeck is not null,
                    OnShown: () =>
                    {
                        _chatDeck!.OnShown();
                        Host.Root.DefaultTextInput = _chatDeck.Input;
                    },
                    OnHidden: () =>
                    {
                        _chatDeck?.OnHidden();
                        if (_chatDeck is not null && ReferenceEquals(Host.Root.DefaultTextInput, _chatDeck.Input))
                            Host.Root.DefaultTextInput = null;
                    },
                    HoldsRetailVisibility: true),
```

4. `MountChatDeck()`, called right after `MountCommandDeck()` in `Initialize`:

```csharp
    /// <summary>
    /// The Chat Deck (Modern HUD's chat window) and its per-character model.
    /// Without it the retail chat and floaties stay while Modern HUD is on.
    /// </summary>
    private void MountChatDeck()
    {
        if (_bindings.CommandDeck is null || _bindings.Chat.Log is not { } log) return;

        var storage = new Hud.ChatDeckStorage(
            _bindings.Persistence?.Store is { } store ? new Hud.SettingsStoreChatDeckStore(store) : null,
            Console.WriteLine);
        var session = new Hud.ChatDeckSession(
            log,
            storage,
            _bindings.Persistence?.CharacterKey ?? (() => "default"),
            () => _bindings.IsGameplayDisplay?.Invoke() ?? true);
        _hudIcons ??= new Hud.EmbeddedSvgSpriteCache(new RetailPluginFontBackend(_bindings.Assets.TextureCache));
        _deckFonts ??= Hud.DeckFonts.Load(_bindings.Assets.TextureCache);

        var deck = new Hud.UiChatDeck(
            new Hud.ChatDeckBindings(
                Model: () => session.Model,
                Entry: _bindings.Chat.Entry,
                Feedback: _bindings.Chat.ViewModel,
                Bus: _bindings.Chat.CommandBus,
                LastIncomingTellSender: () => _bindings.Chat.ViewModel.LastIncomingTellSender,
                StayInChatMode: () => _bindings.Options.CurrentCharacterOption((uint)CharacterOptionId.StayInChatMode),
                LinkClicked: _bindings.Chat.LinkClicked,
                Keyboard: Host.Keyboard),
            _deckFonts,
            _hudIcons);

        float top = MathF.Max(0f, Host.Root.Height - Hud.ChatDeckGeometry.DefaultHeight - 10f);
        try
        {
            RetailWindowFrame.Mount(Host.Root, deck, _bindings.Assets.ResolveSprite, Hud.UiChatDeck.MountOptions(10f, top));
        }
        catch (InvalidOperationException ex)
        {
            deck.Detach();
            session.Dispose();
            Console.WriteLine($"[UI] Chat Deck unavailable; keeping the retail chat. ({ex.Message})");
            return;
        }

        _chatDeck = deck;
        _chatDeckSession = session;
        // A composed line (automation, a plugin) goes to whichever chat input is showing.
        _bindings.Chat.Entry.BindEntryFocus(() => ChatEntryTarget?.Focus() == true);
        Console.WriteLine("[UI] Chat Deck mounted (Modern HUD).");
    }
```

5. `ChatEntryTarget` becomes:

```csharp
    /// <summary>The chat input the chat-entry actions go to: the Chat Deck while it is shown, the retail chat otherwise.</summary>
    private IChatEntryTarget? ChatEntryTarget =>
        _chatDeck is not null && _hudSwitch?.IsShown(WindowNames.ChatDeck) == true
            ? _chatDeck
            : _chatWindowController;
```

6. Persistence: add `WindowNames.ChatDeck` to `stateManagedVisibilityWindows`.

7. `Tick`: right after `_hudSwitch?.Tick();` add `_chatDeckSession?.Tick();` (unconditional: the model queues every line).

8. `ResetSessionTransientUi`: add `_chatDeckSession?.EndSession();`.

9. Dispose, next to `_commandDeck?.Detach();`: `_chatDeck?.Detach(); _chatDeckSession?.Dispose();`.

10. `CharacterOptionId` is already imported in this file for the retail chat's `stayInChatMode`; reuse the same expression.

- [ ] **Step 5: Run tests to verify they pass**

Run: the Verify command, then the whole App test project once:
`MAC_TEST_FILTER='FullyQualifiedName~AcDream.App.Tests' ~/code/OpenAC/test-baseline/run-mac-tests.sh ~/code/OpenAC/OpenAC-chat-deck-window tests/AcDream.App.Tests/AcDream.App.Tests.csproj`. Expected: all pass.

- [ ] **Step 6: Commit**

```bash
git add src/AcDream.App/UI/RetailUiRuntime.cs src/AcDream.App/UI/Hud/UiChatDeck.cs src/AcDream.App/UI/WindowPlacementGeometry.cs src/AcDream.App/Composition/InteractionRetainedUiComposition.cs tests/AcDream.App.Tests/UI/Hud/ChatDeckWiringTests.cs
git commit -m "Chat Deck: mount, session ticking, Modern HUD replacement and chat-entry routing

Claude-Session: https://claude.ai/code/session_01WFS4EHSNafq2P1qfUuh258"
```

---

### Task 14: Full suites, live check, wrap-up

**Goal:** Every Mac test passes on the branch, the window is checked in the real client on the local ACE server with the user, and the plan's state is recorded.

**Files:**
- Modify: `docs/superpowers/plans/2026-10-27-chat-deck-window.md.tasks.json` (statuses) on `docs/chat-deck-spec`

**Acceptance Criteria:**
- [ ] `~/code/OpenAC/test-baseline/run-mac-tests.sh ~/code/OpenAC/OpenAC-chat-deck-window` (all projects) reports 0 failed.
- [ ] The user said they are available before the client was launched; the client ran against the local ACE server (10.10.20.20, `~/OpenAC-dev/dev-client.env`), never Dreamweave.
- [ ] In the live client with Modern HUD on: the Chat Deck replaces the retail chat and floaties; fellowship, General, tells and combat lines land in the expected default tabs; badges count and clear; sending from All (Say), Group (Fellowship) and Social (General) goes to the right channel; the chip override sends one line elsewhere; Enter, Esc, `/r` and tell-to-selected act on the deck's input; search narrows; the "new" marker appears when scrolled up; size, place and last tab survive a relog; Modern HUD off brings the retail chat back as it was. The user confirms each, or a defect is recorded and fixed (with a test) before the plan is called done.
- [ ] Branch not merged or pushed without the user's say-so; the memory note for the Chat Deck records the branch head and what is left.

**Verify:** `~/code/OpenAC/test-baseline/run-mac-tests.sh ~/code/OpenAC/OpenAC-chat-deck-window` → `Failed: 0`

**Steps:**

- [ ] **Step 1: Run every suite**

```bash
~/code/OpenAC/test-baseline/run-mac-tests.sh ~/code/OpenAC/OpenAC-chat-deck-window
```

Expected: every project passes. Fix any failure with a test before going on.

- [ ] **Step 2: Ask the user for the live check**

Ask whether they are available for a live check now. Do not launch the client until they say yes. When they do: build the branch into the dev root the way earlier live checks did (see the memory notes `live-testing-rules.md` and `local-ace-test-server.md`), launch against 10.10.20.20 and walk the list in the acceptance criteria with them. Record each result.

- [ ] **Step 3: Fix what the live check finds**

Each defect: a failing test first, then the fix, its own commit; re-run the affected suite and repeat the live step it broke.

- [ ] **Step 4: Record the state**

Update the `.tasks.json` statuses on `docs/chat-deck-spec` and commit there. Update the Chat Deck memory note (branch head, suites green, live check result, what plan 3 starts from). Then offer the merge choice (main and rynth, as plan 1 was) through `superpowers-extended-cc:finishing-a-development-branch`.
