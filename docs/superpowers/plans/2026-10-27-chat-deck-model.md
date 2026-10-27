# Chat Deck Model and Rules Engine Implementation Plan (plan 1 of 5)

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers-extended-cc:subagent-driven-development (recommended) or superpowers-extended-cc:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** The pure Core layer of the Chat Deck: channel classification, rules and presets, the verdict engine, per-tab history and read state, settings with JSON persistence, and the session model that ties them to `ChatLog`.

**Architecture:** Everything lives in `src/AcDream.Core/Chat/Deck/` (namespace `AcDream.Core.Chat.Deck`) with no UI types. `ChatDeckModel` subscribes to `ChatLog.EntryAppended` and a new `ChatLog.Cleared`, queues entries from any thread and applies them on `Tick()`. Each entry is classified into a `ChatDeckChannel`, evaluated once by `ChatDeckRuleEngine` into an immutable `ChatDeckLine`, and the line is referenced from each destination tab's `ChatDeckTabState`. Settings are records; `ChatDeckSettingsJson` reads and writes them while keeping unknown keys.

**Tech Stack:** C# / .NET 10, `System.Text.RegularExpressions`, `System.Text.Json.Nodes`, xUnit.

**Spec:** `docs/superpowers/specs/2026-10-26-chat-deck-design.md` (branch `docs/chat-deck-spec`). Read the "ChatDeckModel (Core)" and "Settings and persistence" sections before starting.

**Worktree:** implement in `~/code/OpenAC/OpenAC-chat-deck-model` on branch `feat/chat-deck-model`, created from `main`.

**Running tests:** always through the Mac runner, narrowed with `MAC_TEST_FILTER` (never a second `--filter`):

```bash
MAC_TEST_FILTER='FullyQualifiedName~ChatDeck' ~/code/OpenAC/test-baseline/run-mac-tests.sh ~/code/OpenAC/OpenAC-chat-deck-model tests/AcDream.Core.Tests/AcDream.Core.Tests.csproj
```

`TreatWarningsAsErrors` is on: unused variables, missing nullability annotations and similar warnings fail the build.

## Global Constraints

- Namespace `AcDream.Core.Chat.Deck`, folder `src/AcDream.Core/Chat/Deck/`; tests in `tests/AcDream.Core.Tests/Chat/Deck/`, namespace `AcDream.Core.Tests.Chat.Deck`.
- No UI, rendering, Runtime or App references from these files. Core only.
- The *All* tab id is `"all"`. It cannot be removed; normalisation puts it first.
- Limits (spec): 16 tabs, 200 rules, tab name 24 characters, pattern 256 characters, match input 2,000 characters, settings JSON 262,144 characters (256 KB), history 1,000 line references per tab, regex match timeout 20 ms.
- Regex options: user `Regex` rules compile with `RegexOptions.NonBacktracking | IgnoreCase | CultureInvariant`; if that throws `NotSupportedException` (lookarounds, backreferences) they compile with `IgnoreCase | CultureInvariant` and the 20 ms timeout instead. `Contains` rules use `Regex.Escape` + NonBacktracking. `MyName` and presets use the backtracking engine with the 20 ms timeout (they need lookarounds; their patterns are ours and tested).
- Verdict algorithm exactly as the spec's "Verdict" list: Hide anywhere wins; subscriptions plus *All*; first Move replaces subscriptions; every Copy adds; de-duplicate; first ColourLine / first ColourMatch; any alert rule, or an incoming tell, makes the line an alert.
- Rule changes apply to lines that arrive afterwards; lines already in a tab stay where they are.
- JSON keys are camelCase; enums are written as their C# names; colours as `#AARRGGBB`. Unknown object keys are kept on save; unknown enum values inside arrays are dropped.
- Commit messages end with the line `Claude-Session: https://claude.ai/code/session_01WFS4EHSNafq2P1qfUuh258`.

**User decisions (already made):**
- "A" — a new Modern HUD chat window, not an upgrade of retail chat.
- Own tabs built from channel groups, each with a default send channel ("A and yes").
- Text filtering: search, hide words, highlight words, per-tab text rules ("Yes, incl 5").
- RynthChat features 1–9 adopted (rule actions, standard presets, rule from a line, test box, copy lines, read state, mention alert, auto-scroll marker, input history) ("Looks good").
- Scrollback per tab, cleared on logout ("option A").
- All Codex review fixes accepted except the machine-specific test-runner point ("yes").

---

## File map

| File | Responsibility |
|---|---|
| `src/AcDream.Core/Chat/ChatLog.cs` (modify) | add `Cleared` event raised by `Clear()` |
| `Chat/Deck/ChatDeckChannel.cs` | `ChatDeckChannel`, `ChatDeckSendChannel`, `ChatDeckChannels` (classify, own line, groups, names) |
| `Chat/Deck/ChatDeckRule.cs` | rule record and its enums |
| `Chat/Deck/ChatDeckLineFacts.cs` | the facts a rule matches on; `ChatDeckSpan` |
| `Chat/Deck/ChatDeckMatcher.cs` | compiling rules (regex safety) and matching one line |
| `Chat/Deck/ChatDeckPreset.cs` | preset type, samples, scope helpers |
| `Chat/Deck/ChatDeckPresets.cs` | the ~70 standard presets (ported, MIT) |
| `Chat/Deck/ChatDeckSettings.cs` | tab, appearance, layout, settings records; limits; defaults; normaliser |
| `Chat/Deck/ChatDeckRuleEngine.cs` | verdict for one line |
| `Chat/Deck/ChatDeckLine.cs` | immutable evaluated line |
| `Chat/Deck/ChatDeckTabState.cs` | per-tab history ring and read state |
| `Chat/Deck/ChatDeckSettingsJson.cs` | parse / serialise settings, keeping unknown keys |
| `Chat/Deck/ChatDeckModel.cs` | session model: queue, tick, tabs, selection, faults, lifecycle |
| `NOTICE.md` (modify) | RynthCore MIT attribution for the presets |

---

### Task 1: `ChatLog.Cleared` event

**Goal:** `ChatLog.Clear()` raises a new `Cleared` event so listeners (the Chat Deck) can drop what they hold on `/clear`.

**Files:**
- Modify: `src/AcDream.Core/Chat/ChatLog.cs` (the `EntryAppended` declaration near line 33 and `Clear()` near line 293)
- Test: `tests/AcDream.Core.Tests/Chat/ChatLogClearedTests.cs`

**Acceptance Criteria:**
- [ ] `ChatLog.Clear()` raises `Cleared` exactly once per call, after the buffer is empty.
- [ ] Existing `ChatLog` behaviour is unchanged (existing ChatLog tests pass).

**Verify:** `MAC_TEST_FILTER='FullyQualifiedName~ChatLog' ~/code/OpenAC/test-baseline/run-mac-tests.sh ~/code/OpenAC/OpenAC-chat-deck-model tests/AcDream.Core.Tests/AcDream.Core.Tests.csproj` → all pass

**Steps:**

- [ ] **Step 1: Write the failing test**

```csharp
using AcDream.Core.Chat;

namespace AcDream.Core.Tests.Chat;

public sealed class ChatLogClearedTests
{
    [Fact]
    public void Clear_RaisesClearedOnceAfterTheBufferIsEmpty()
    {
        var log = new ChatLog();
        log.OnSystemMessage("one", 0x05u);
        int raised = 0;
        int countWhenRaised = -1;
        log.Cleared += () => { raised++; countWhenRaised = log.Count; };

        log.Clear();

        Assert.Equal(1, raised);
        Assert.Equal(0, countWhenRaised);
    }
}
```

- [ ] **Step 2: Run it and see it fail** (compile error: `Cleared` does not exist).

- [ ] **Step 3: Implement**

In `ChatLog.cs`, below the `EntryAppended` declaration:

```csharp
    /// <summary>Fires after <see cref="Clear"/> has emptied the log.</summary>
    public event Action? Cleared;
```

and change `Clear()` to:

```csharp
    public void Clear()
    {
        while (_buffer.TryDequeue(out _)) { /* drain */ }
        Interlocked.Increment(ref _revision);
        Cleared?.Invoke();
    }
```

- [ ] **Step 4: Run the Verify command** → PASS.

- [ ] **Step 5: Commit**

```bash
git add src/AcDream.Core/Chat/ChatLog.cs tests/AcDream.Core.Tests/Chat/ChatLogClearedTests.cs
git commit -m "Chat log: raise Cleared when cleared

Claude-Session: https://claude.ai/code/session_01WFS4EHSNafq2P1qfUuh258"
```

---

### Task 2: Channels — classify, own line, groups

**Goal:** Map every `ChatEntry` to exactly one `ChatDeckChannel`, tell own lines apart, and expose the editor's channel groups and names.

**Files:**
- Create: `src/AcDream.Core/Chat/Deck/ChatDeckChannel.cs`
- Test: `tests/AcDream.Core.Tests/Chat/Deck/ChatDeckChannelsTests.cs`

**Acceptance Criteria:**
- [ ] Allegiance (Turbine room and legacy `0x02000000`) and Olthoi (Turbine room and legacy `0x40000000`) classify differently.
- [ ] Legacy ids `0x1000` Vassals, `0x2000` Patron, `0x4000` Monarch, `0x01000000` CoVassals, `0x800` Fellowship classify to their own channels, received and own-sent alike.
- [ ] Turbine names General, Trade, LFG, Roleplay, Society (and the three society names), Olthoi, Allegiance classify by name; an unknown room name (`Room 0x…`) is `OtherChannel`.
- [ ] Tells: `0x04` is `TellOut`, `0x03` and `0x1F` are `TellIn`.
- [ ] `IsOwnLine` is true for "You" speech, `0x04`/`0x09`/`0x0B` types, and channel lines with no sender; false for another player's line.
- [ ] Every channel appears in exactly one group.

**Verify:** `MAC_TEST_FILTER='FullyQualifiedName~ChatDeckChannels' ~/code/OpenAC/test-baseline/run-mac-tests.sh ~/code/OpenAC/OpenAC-chat-deck-model tests/AcDream.Core.Tests/AcDream.Core.Tests.csproj` → all pass

**Steps:**

- [ ] **Step 1: Write the failing tests**

```csharp
using AcDream.Core.Chat;
using AcDream.Core.Chat.Deck;

namespace AcDream.Core.Tests.Chat.Deck;

public sealed class ChatDeckChannelsTests
{
    private static ChatEntry Channel(uint id, string sender, uint type, string name = "") =>
        new(ChatKind.Channel, sender, "hi", 0u, id) { LogTextType = type, ChannelName = name };

    [Theory]
    [InlineData(0x00000800u, ChatDeckChannel.Fellowship)]
    [InlineData(0x00001000u, ChatDeckChannel.Vassals)]
    [InlineData(0x00002000u, ChatDeckChannel.Patron)]
    [InlineData(0x00004000u, ChatDeckChannel.Monarch)]
    [InlineData(0x01000000u, ChatDeckChannel.CoVassals)]
    [InlineData(0x02000000u, ChatDeckChannel.Allegiance)]
    [InlineData(0x40000000u, ChatDeckChannel.Olthoi)]
    [InlineData(0x00000400u, ChatDeckChannel.Help)]
    [InlineData(0x00000001u, ChatDeckChannel.OtherChannel)]
    public void LegacyChannel_ReceivedAndOwnSent_ClassifyAlike(uint id, ChatDeckChannel expected)
    {
        Assert.Equal(expected, ChatDeckChannels.Classify(Channel(id, "Bob", LegacyChannelChatType.Resolve(id, false))));
        Assert.Equal(expected, ChatDeckChannels.Classify(Channel(id, "", LegacyChannelChatType.Resolve(id, true))));
    }

    [Theory]
    [InlineData("Allegiance", 0x12u, ChatDeckChannel.Allegiance)]
    [InlineData("Olthoi", 0x12u, ChatDeckChannel.Olthoi)]
    [InlineData("General", 0x1Bu, ChatDeckChannel.General)]
    [InlineData("Trade", 0x1Cu, ChatDeckChannel.Trade)]
    [InlineData("LFG", 0x1Du, ChatDeckChannel.Lfg)]
    [InlineData("Roleplay", 0x1Eu, ChatDeckChannel.Roleplay)]
    [InlineData("Society", 0x20u, ChatDeckChannel.Society)]
    [InlineData("Celestial Hand", 0x20u, ChatDeckChannel.Society)]
    [InlineData("Eldrytch Web", 0x20u, ChatDeckChannel.Society)]
    [InlineData("Radiant Blood", 0x20u, ChatDeckChannel.Society)]
    [InlineData("Room 0x00ABCDEF", 0x00u, ChatDeckChannel.OtherChannel)]
    public void TurbineRoom_ClassifiesByName(string name, uint type, ChatDeckChannel expected)
    {
        // Room ids are arbitrary numbers; 0x800 here must not be read as Fellowship.
        Assert.Equal(expected, ChatDeckChannels.Classify(Channel(0x800u, "Bob", type, name)));
    }

    [Fact]
    public void Tells_SplitByDirection()
    {
        var inbound = new ChatEntry(ChatKind.Tell, "Bob", "hi", 0x50000001u, 0u) { LogTextType = 0x03u };
        var admin = inbound with { LogTextType = 0x1Fu };
        var outbound = new ChatEntry(ChatKind.Tell, "Bob", "hi", 0u, 0u) { LogTextType = 0x04u };
        Assert.Equal(ChatDeckChannel.TellIn, ChatDeckChannels.Classify(inbound));
        Assert.Equal(ChatDeckChannel.TellIn, ChatDeckChannels.Classify(admin));
        Assert.Equal(ChatDeckChannel.TellOut, ChatDeckChannels.Classify(outbound));
    }

    [Theory]
    [InlineData(ChatKind.LocalSpeech, 0x02u, ChatDeckChannel.Say)]
    [InlineData(ChatKind.RangedSpeech, 0x02u, ChatDeckChannel.Say)]
    [InlineData(ChatKind.LocalSpeech, 0x11u, ChatDeckChannel.Spellcasting)]
    [InlineData(ChatKind.Emote, 0x0Cu, ChatDeckChannel.Emote)]
    [InlineData(ChatKind.SoulEmote, 0x0Cu, ChatDeckChannel.Emote)]
    [InlineData(ChatKind.Combat, 0x15u, ChatDeckChannel.CombatEnemy)]
    [InlineData(ChatKind.Combat, 0x16u, ChatDeckChannel.CombatSelf)]
    [InlineData(ChatKind.Combat, 0x06u, ChatDeckChannel.Combat)]
    [InlineData(ChatKind.System, 0x07u, ChatDeckChannel.Magic)]
    [InlineData(ChatKind.System, 0x05u, ChatDeckChannel.System)]
    [InlineData(ChatKind.System, 0x0Du, ChatDeckChannel.Advancement)]
    [InlineData(ChatKind.System, 0x19u, ChatDeckChannel.Salvaging)]
    [InlineData(ChatKind.System, 0x1Au, ChatDeckChannel.Plugin)]
    [InlineData(ChatKind.System, 0x00u, ChatDeckChannel.System)]
    [InlineData(ChatKind.Popup, 0x00u, ChatDeckChannel.System)]
    public void OtherKinds_ClassifyByKindAndType(ChatKind kind, uint type, ChatDeckChannel expected)
    {
        var entry = new ChatEntry(kind, "", "text", 0u, 0u) { LogTextType = type };
        Assert.Equal(expected, ChatDeckChannels.Classify(entry));
    }

    [Fact]
    public void IsOwnLine_RecognisesOwnLines()
    {
        Assert.True(ChatDeckChannels.IsOwnLine(new ChatEntry(ChatKind.LocalSpeech, "You", "hi", 1u, 0u) { LogTextType = 0x02u }));
        Assert.True(ChatDeckChannels.IsOwnLine(new ChatEntry(ChatKind.Tell, "Bob", "hi", 0u, 0u) { LogTextType = 0x04u }));
        Assert.True(ChatDeckChannels.IsOwnLine(Channel(0x800u, "", 0x13u)));
        Assert.True(ChatDeckChannels.IsOwnLine(Channel(0x1000u, "", 0x0Bu)));
        Assert.False(ChatDeckChannels.IsOwnLine(Channel(0x800u, "Bob", 0x13u)));
        Assert.False(ChatDeckChannels.IsOwnLine(new ChatEntry(ChatKind.LocalSpeech, "Bob", "hi", 2u, 0u) { LogTextType = 0x02u }));
    }

    [Fact]
    public void EveryChannel_IsInExactlyOneGroup()
    {
        var all = Enum.GetValues<ChatDeckChannel>();
        var grouped = ChatDeckChannels.Groups.SelectMany(g => g.Channels).ToList();
        Assert.Equal(all.Length, grouped.Count);
        Assert.Equal(all.OrderBy(c => c), grouped.OrderBy(c => c));
        Assert.All(all, c => Assert.False(string.IsNullOrEmpty(ChatDeckChannels.DisplayName(c))));
    }
}
```

- [ ] **Step 2: Run the Verify command** → compile failure (types missing).

- [ ] **Step 3: Implement `ChatDeckChannel.cs`**

```csharp
namespace AcDream.Core.Chat.Deck;

/// <summary>
/// What a Chat Deck line is, for tabs and rules. Finer than a chat type: several
/// channels share one <see cref="RetailLogTextType"/> (Patron, Vassals, Monarch and
/// Co-vassals are all Social; Allegiance and Olthoi are both 0x12).
/// </summary>
public enum ChatDeckChannel
{
    Say, Emote, TellIn, TellOut,
    Fellowship, Allegiance, Patron, Vassals, Monarch, CoVassals,
    General, Trade, Lfg, Roleplay, Society, Olthoi, Help, OtherChannel,
    Combat, CombatEnemy, CombatSelf, Magic, Spellcasting,
    System, Advancement, Appraisal, Recall, Craft, Salvaging, WorldBroadcast, Plugin,
}

/// <summary>Where typed text goes when it has no <c>/</c> command. Names match the Runtime's ChatChannelKind.</summary>
public enum ChatDeckSendChannel
{
    Say, Fellowship, Allegiance, Patron, Vassals, Monarch, CoVassals,
    General, Trade, Lfg, Roleplay, Society, Olthoi,
}

public readonly record struct ChatDeckChannelGroup(string Name, IReadOnlyList<ChatDeckChannel> Channels);

public static class ChatDeckChannels
{
    public static IReadOnlyList<ChatDeckChannelGroup> Groups { get; } =
    [
        new("Local", [ChatDeckChannel.Say, ChatDeckChannel.Emote, ChatDeckChannel.TellIn, ChatDeckChannel.TellOut]),
        new("Group", [ChatDeckChannel.Fellowship, ChatDeckChannel.Allegiance, ChatDeckChannel.Patron,
                      ChatDeckChannel.Vassals, ChatDeckChannel.Monarch, ChatDeckChannel.CoVassals]),
        new("Social", [ChatDeckChannel.General, ChatDeckChannel.Trade, ChatDeckChannel.Lfg, ChatDeckChannel.Roleplay,
                       ChatDeckChannel.Society, ChatDeckChannel.Olthoi, ChatDeckChannel.Help, ChatDeckChannel.OtherChannel]),
        new("Combat", [ChatDeckChannel.Combat, ChatDeckChannel.CombatEnemy, ChatDeckChannel.CombatSelf,
                       ChatDeckChannel.Magic, ChatDeckChannel.Spellcasting]),
        new("System", [ChatDeckChannel.System, ChatDeckChannel.Advancement, ChatDeckChannel.Appraisal, ChatDeckChannel.Recall,
                       ChatDeckChannel.Craft, ChatDeckChannel.Salvaging, ChatDeckChannel.WorldBroadcast, ChatDeckChannel.Plugin]),
    ];

    public static IReadOnlyList<ChatDeckChannel> Group(string name) =>
        Groups.First(g => g.Name == name).Channels;

    public static string DisplayName(ChatDeckChannel channel) => channel switch
    {
        ChatDeckChannel.Say => "Say",
        ChatDeckChannel.Emote => "Emotes",
        ChatDeckChannel.TellIn => "Tells received",
        ChatDeckChannel.TellOut => "Tells sent",
        ChatDeckChannel.Fellowship => "Fellowship",
        ChatDeckChannel.Allegiance => "Allegiance",
        ChatDeckChannel.Patron => "Patron",
        ChatDeckChannel.Vassals => "Vassals",
        ChatDeckChannel.Monarch => "Monarch",
        ChatDeckChannel.CoVassals => "Co-vassals",
        ChatDeckChannel.General => "General",
        ChatDeckChannel.Trade => "Trade",
        ChatDeckChannel.Lfg => "LFG",
        ChatDeckChannel.Roleplay => "Roleplay",
        ChatDeckChannel.Society => "Society",
        ChatDeckChannel.Olthoi => "Olthoi",
        ChatDeckChannel.Help => "Help",
        ChatDeckChannel.OtherChannel => "Other channels",
        ChatDeckChannel.Combat => "Combat",
        ChatDeckChannel.CombatEnemy => "Combat against you",
        ChatDeckChannel.CombatSelf => "Your combat",
        ChatDeckChannel.Magic => "Magic",
        ChatDeckChannel.Spellcasting => "Spell words",
        ChatDeckChannel.System => "System",
        ChatDeckChannel.Advancement => "Advancement",
        ChatDeckChannel.Appraisal => "Appraisal",
        ChatDeckChannel.Recall => "Recall",
        ChatDeckChannel.Craft => "Crafting",
        ChatDeckChannel.Salvaging => "Salvaging",
        ChatDeckChannel.WorldBroadcast => "World broadcasts",
        ChatDeckChannel.Plugin => "Plugins",
        _ => channel.ToString(),
    };

    public static ChatDeckChannel Classify(in ChatEntry entry) => entry.Kind switch
    {
        ChatKind.LocalSpeech or ChatKind.RangedSpeech =>
            entry.LogTextType == (uint)RetailLogTextType.Spellcasting
                ? ChatDeckChannel.Spellcasting
                : ChatDeckChannel.Say,
        ChatKind.Emote or ChatKind.SoulEmote => ChatDeckChannel.Emote,
        ChatKind.Tell => entry.LogTextType == (uint)RetailLogTextType.SpeechDirectSend
            ? ChatDeckChannel.TellOut
            : ChatDeckChannel.TellIn,
        ChatKind.Channel => ClassifyChannel(entry),
        _ => ByLogTextType(entry.LogTextType),
    };

    /// <summary>The channel a line of this type belongs to when nothing else says (system, combat and popup lines).</summary>
    public static ChatDeckChannel ByLogTextType(uint logTextType) => (RetailLogTextType)logTextType switch
    {
        RetailLogTextType.Speech => ChatDeckChannel.Say,
        RetailLogTextType.Tell or RetailLogTextType.AdminTell => ChatDeckChannel.TellIn,
        RetailLogTextType.SpeechDirectSend => ChatDeckChannel.TellOut,
        RetailLogTextType.Emote => ChatDeckChannel.Emote,
        RetailLogTextType.Combat => ChatDeckChannel.Combat,
        RetailLogTextType.CombatEnemy => ChatDeckChannel.CombatEnemy,
        RetailLogTextType.CombatSelf => ChatDeckChannel.CombatSelf,
        RetailLogTextType.Magic => ChatDeckChannel.Magic,
        RetailLogTextType.Spellcasting => ChatDeckChannel.Spellcasting,
        RetailLogTextType.Channel or RetailLogTextType.ChannelSend or RetailLogTextType.Abuse => ChatDeckChannel.OtherChannel,
        RetailLogTextType.Social or RetailLogTextType.SocialSend or RetailLogTextType.Allegiance => ChatDeckChannel.Allegiance,
        RetailLogTextType.Fellowship => ChatDeckChannel.Fellowship,
        RetailLogTextType.Help => ChatDeckChannel.Help,
        RetailLogTextType.TurbineGeneral => ChatDeckChannel.General,
        RetailLogTextType.TurbineTrade => ChatDeckChannel.Trade,
        RetailLogTextType.TurbineLFG => ChatDeckChannel.Lfg,
        RetailLogTextType.TurbineRoleplay => ChatDeckChannel.Roleplay,
        RetailLogTextType.TurbineSociety => ChatDeckChannel.Society,
        RetailLogTextType.Advancement => ChatDeckChannel.Advancement,
        RetailLogTextType.Appraisal => ChatDeckChannel.Appraisal,
        RetailLogTextType.Recall => ChatDeckChannel.Recall,
        RetailLogTextType.Craft => ChatDeckChannel.Craft,
        RetailLogTextType.Salvaging => ChatDeckChannel.Salvaging,
        RetailLogTextType.WorldBroadcast => ChatDeckChannel.WorldBroadcast,
        RetailLogTextType.ClientLocal => ChatDeckChannel.Plugin,
        _ => ChatDeckChannel.System,
    };

    /// <summary>A line the character itself said or sent.</summary>
    public static bool IsOwnLine(in ChatEntry entry) =>
        entry.Sender == "You"
        || entry.LogTextType is (uint)RetailLogTextType.SpeechDirectSend
            or (uint)RetailLogTextType.ChannelSend
            or (uint)RetailLogTextType.SocialSend
        || (entry.Kind == ChatKind.Channel && string.IsNullOrEmpty(entry.Sender));

    private static ChatDeckChannel ClassifyChannel(in ChatEntry entry)
    {
        // A Turbine room always carries its display name (an unknown room reads "Room 0x…");
        // its id is a room number, not a legacy channel bit, so the name decides.
        if (!string.IsNullOrEmpty(entry.ChannelName))
        {
            return entry.ChannelName switch
            {
                "Allegiance" => ChatDeckChannel.Allegiance,
                "General" => ChatDeckChannel.General,
                "Trade" => ChatDeckChannel.Trade,
                "LFG" => ChatDeckChannel.Lfg,
                "Roleplay" => ChatDeckChannel.Roleplay,
                "Society" or "Celestial Hand" or "Eldrytch Web" or "Radiant Blood" => ChatDeckChannel.Society,
                "Olthoi" => ChatDeckChannel.Olthoi,
                _ => ChatDeckChannel.OtherChannel,
            };
        }

        // Legacy channel bits, as Runtime's ChannelResolver and RetailChannelTagTable number them.
        return entry.ChannelId switch
        {
            0x00000800u => ChatDeckChannel.Fellowship,
            0x00001000u => ChatDeckChannel.Vassals,
            0x00002000u => ChatDeckChannel.Patron,
            0x00004000u => ChatDeckChannel.Monarch,
            0x01000000u => ChatDeckChannel.CoVassals,
            0x02000000u => ChatDeckChannel.Allegiance,
            0x40000000u => ChatDeckChannel.Olthoi,
            0x00000400u => ChatDeckChannel.Help,
            _ => ChatDeckChannel.OtherChannel,
        };
    }
}
```

- [ ] **Step 4: Run the Verify command** → PASS.

- [ ] **Step 5: Commit**

```bash
git add src/AcDream.Core/Chat/Deck/ChatDeckChannel.cs tests/AcDream.Core.Tests/Chat/Deck/ChatDeckChannelsTests.cs
git commit -m "Chat Deck: channels from chat entries

Claude-Session: https://claude.ai/code/session_01WFS4EHSNafq2P1qfUuh258"
```

---

### Task 3: Rules, line facts and the matcher

**Goal:** The rule record, the facts a rule reads from a line, and `ChatDeckMatcher`: compiling a rule safely and matching it against one line, with spans for colour-match.

**Files:**
- Create: `src/AcDream.Core/Chat/Deck/ChatDeckRule.cs`
- Create: `src/AcDream.Core/Chat/Deck/ChatDeckLineFacts.cs`
- Create: `src/AcDream.Core/Chat/Deck/ChatDeckMatcher.cs`
- Create (temporary stub, filled in Task 4): `src/AcDream.Core/Chat/Deck/ChatDeckPreset.cs`, `src/AcDream.Core/Chat/Deck/ChatDeckPresets.cs`
- Test: `tests/AcDream.Core.Tests/Chat/Deck/ChatDeckMatcherTests.cs`

**Acceptance Criteria:**
- [ ] `Contains` is case-insensitive and treats the pattern literally (`a.b` does not match `axb`).
- [ ] `Regex` with a lookahead compiles (backtracking fallback); `(` is `Invalid`; an empty or 257-character pattern is `Invalid`.
- [ ] A pathological regex on a long input returns `Timeout` rather than hanging (test completes in well under a second).
- [ ] `MyName` matches the character name as a whole word, case-insensitive, never on own lines, and is `Disabled` with no character name.
- [ ] Field `Sender` and `Line` read the sender and the worded line; channels limit the rule (empty = any).
- [ ] Disabled, faulted and unknown-preset rules report their status and never match.
- [ ] Spans: up to 16, in the chosen field, from the first 2,000 characters only.

**Verify:** `MAC_TEST_FILTER='FullyQualifiedName~ChatDeckMatcher' ~/code/OpenAC/test-baseline/run-mac-tests.sh ~/code/OpenAC/OpenAC-chat-deck-model tests/AcDream.Core.Tests/AcDream.Core.Tests.csproj` → all pass

**Steps:**

- [ ] **Step 1: Write the failing tests**

```csharp
using System.Diagnostics;
using AcDream.Core.Chat;
using AcDream.Core.Chat.Deck;

namespace AcDream.Core.Tests.Chat.Deck;

public sealed class ChatDeckMatcherTests
{
    internal static ChatDeckLineFacts Facts(
        string text, ChatDeckChannel channel = ChatDeckChannel.Say, string sender = "Bob", bool own = false)
    {
        var entry = new ChatEntry(ChatKind.LocalSpeech, sender, text, 0u, 0u) { LogTextType = 0x02u };
        return new ChatDeckLineFacts(entry, channel, text, sender, $"{sender} says, \"{text}\"", own);
    }

    private static ChatDeckRule Rule(ChatDeckMatchKind kind, string pattern = "", ChatDeckMatchField field = ChatDeckMatchField.Text) =>
        new() { Id = "r1", Kind = kind, Pattern = pattern, Field = field, Action = ChatDeckRuleAction.Hide };

    private static ChatDeckMatchOutcome Outcome(ChatDeckRule rule, ChatDeckLineFacts facts, string name = "Davian") =>
        ChatDeckMatcher.Match(ChatDeckMatcher.Compile(rule, name), facts, wantSpans: false).Outcome;

    [Fact]
    public void Contains_IsCaseInsensitiveAndLiteral()
    {
        Assert.Equal(ChatDeckMatchOutcome.Match, Outcome(Rule(ChatDeckMatchKind.Contains, "HELLO"), Facts("well hello there")));
        Assert.Equal(ChatDeckMatchOutcome.NoMatch, Outcome(Rule(ChatDeckMatchKind.Contains, "a.b"), Facts("axb")));
        Assert.Equal(ChatDeckMatchOutcome.Match, Outcome(Rule(ChatDeckMatchKind.Contains, "a.b"), Facts("x a.b y")));
    }

    [Fact]
    public void Regex_LookaheadFallsBack_BadPatternsAreInvalid()
    {
        var look = ChatDeckMatcher.Compile(Rule(ChatDeckMatchKind.Regex, "^(?!You ).+ says"), "Davian");
        Assert.Equal(ChatDeckRuleStatus.Ok, look.Status);
        Assert.Equal(ChatDeckRuleStatus.Invalid, ChatDeckMatcher.Compile(Rule(ChatDeckMatchKind.Regex, "("), "Davian").Status);
        Assert.Equal(ChatDeckRuleStatus.Invalid, ChatDeckMatcher.Compile(Rule(ChatDeckMatchKind.Regex, ""), "Davian").Status);
        Assert.Equal(ChatDeckRuleStatus.Invalid, ChatDeckMatcher.Compile(Rule(ChatDeckMatchKind.Contains, new string('a', 257)), "Davian").Status);
        Assert.NotNull(ChatDeckMatcher.Compile(Rule(ChatDeckMatchKind.Regex, "("), "Davian").Error);
    }

    [Fact]
    public void Regex_PathologicalPattern_TimesOut()
    {
        // Backreference forces the backtracking engine, so the timeout is what saves us.
        var rule = Rule(ChatDeckMatchKind.Regex, @"^(\w+\s?)*\1$");
        var watch = Stopwatch.StartNew();
        var outcome = Outcome(rule, Facts(new string('a', 1990) + "!"));
        watch.Stop();
        Assert.Equal(ChatDeckMatchOutcome.Timeout, outcome);
        Assert.True(watch.ElapsedMilliseconds < 1000, $"took {watch.ElapsedMilliseconds} ms");
    }

    [Fact]
    public void MyName_WholeWord_NotOwnLines_DisabledWithoutName()
    {
        var rule = Rule(ChatDeckMatchKind.MyName);
        Assert.Equal(ChatDeckMatchOutcome.Match, Outcome(rule, Facts("davian, heal please")));
        Assert.Equal(ChatDeckMatchOutcome.NoMatch, Outcome(rule, Facts("Davianne is here")));
        Assert.Equal(ChatDeckMatchOutcome.NoMatch, Outcome(rule, Facts("I am Davian", own: true)));
        Assert.Equal(ChatDeckMatchOutcome.Match, Outcome(rule, Facts("hi +Admin!"), name: "+Admin"));
        Assert.Equal(ChatDeckRuleStatus.Disabled, ChatDeckMatcher.Compile(rule, "").Status);
    }

    [Fact]
    public void Fields_AndChannels()
    {
        Assert.Equal(ChatDeckMatchOutcome.Match,
            Outcome(Rule(ChatDeckMatchKind.Contains, "bob", ChatDeckMatchField.Sender), Facts("hi")));
        Assert.Equal(ChatDeckMatchOutcome.NoMatch,
            Outcome(Rule(ChatDeckMatchKind.Contains, "bob", ChatDeckMatchField.Text), Facts("hi")));
        Assert.Equal(ChatDeckMatchOutcome.Match,
            Outcome(Rule(ChatDeckMatchKind.Contains, "says, \"hi", ChatDeckMatchField.Line), Facts("hi")));

        var combatOnly = Rule(ChatDeckMatchKind.Contains, "hi") with
        {
            Channels = new HashSet<ChatDeckChannel> { ChatDeckChannel.Combat },
        };
        Assert.Equal(ChatDeckMatchOutcome.NoMatch, Outcome(combatOnly, Facts("hi")));
        Assert.Equal(ChatDeckMatchOutcome.Match, Outcome(combatOnly, Facts("hi", ChatDeckChannel.Combat)));
    }

    [Fact]
    public void StatusRules_NeverMatch()
    {
        var facts = Facts("hello");
        var disabled = Rule(ChatDeckMatchKind.Contains, "hello") with { Enabled = false };
        var faulted = Rule(ChatDeckMatchKind.Contains, "hello") with { Faulted = true };
        var unknown = new ChatDeckRule { Id = "p", Kind = ChatDeckMatchKind.Preset, PresetId = "no.such.preset" };
        Assert.Equal(ChatDeckRuleStatus.Disabled, ChatDeckMatcher.Compile(disabled, "Davian").Status);
        Assert.Equal(ChatDeckRuleStatus.Faulted, ChatDeckMatcher.Compile(faulted, "Davian").Status);
        Assert.Equal(ChatDeckRuleStatus.UnknownPreset, ChatDeckMatcher.Compile(unknown, "Davian").Status);
        Assert.Equal(ChatDeckMatchOutcome.NoMatch, Outcome(disabled, facts));
        Assert.Equal(ChatDeckMatchOutcome.NoMatch, Outcome(faulted, facts));
        Assert.Equal(ChatDeckMatchOutcome.NoMatch, Outcome(unknown, facts));
    }

    [Fact]
    public void Spans_CappedAndWithinInputLimit()
    {
        var rule = Rule(ChatDeckMatchKind.Contains, "ab");
        var compiled = ChatDeckMatcher.Compile(rule, "Davian");
        var many = ChatDeckMatcher.Match(compiled, Facts(string.Concat(Enumerable.Repeat("ab ", 40))), wantSpans: true);
        Assert.Equal(ChatDeckMatcher.MaxSpans, many.Spans.Count);
        Assert.Equal(new ChatDeckSpan(ChatDeckMatchField.Text, 0, 2), many.Spans[0]);
        Assert.Equal(new ChatDeckSpan(ChatDeckMatchField.Text, 3, 2), many.Spans[1]);

        var late = ChatDeckMatcher.Match(compiled, Facts(new string('x', 2000) + "ab"), wantSpans: true);
        Assert.Equal(ChatDeckMatchOutcome.NoMatch, late.Outcome);
    }
}
```

- [ ] **Step 2: Run the Verify command** → compile failure.

- [ ] **Step 3: Implement `ChatDeckRule.cs`**

```csharp
namespace AcDream.Core.Chat.Deck;

public enum ChatDeckMatchKind { Contains, Regex, MyName, Preset }

/// <summary>Which part of a line a rule reads.</summary>
public enum ChatDeckMatchField
{
    /// <summary>The message body, without sender or channel prefix.</summary>
    Text,
    Sender,
    /// <summary>The whole line as worded on screen (<see cref="ChatLineWording.Format"/>).</summary>
    Line,
}

public enum ChatDeckRuleAction { Hide, MoveTo, CopyTo, ColourLine, ColourMatch }

public sealed record ChatDeckRule
{
    public static readonly IReadOnlySet<ChatDeckChannel> AnyChannel = new HashSet<ChatDeckChannel>();

    public required string Id { get; init; }
    public bool Enabled { get; init; } = true;
    public ChatDeckMatchKind Kind { get; init; }
    public string Pattern { get; init; } = "";
    public string PresetId { get; init; } = "";
    public ChatDeckMatchField Field { get; init; } = ChatDeckMatchField.Text;
    /// <summary>Channels the rule applies to; empty means any. Ignored for presets (they carry their own).</summary>
    public IReadOnlySet<ChatDeckChannel> Channels { get; init; } = AnyChannel;
    public ChatDeckRuleAction Action { get; init; }
    /// <summary>Tab id for <see cref="ChatDeckRuleAction.MoveTo"/> / <see cref="ChatDeckRuleAction.CopyTo"/>.</summary>
    public string TargetTabId { get; init; } = "";
    /// <summary>0xAARRGGBB for the colour actions.</summary>
    public uint Colour { get; init; }
    public bool Alert { get; init; }
    /// <summary>Set when the rule timed out on a line; it stays off until edited.</summary>
    public bool Faulted { get; init; }
}
```

- [ ] **Step 4: Implement `ChatDeckLineFacts.cs`**

```csharp
namespace AcDream.Core.Chat.Deck;

/// <summary>A run of matched characters in one field of a line.</summary>
public readonly record struct ChatDeckSpan(ChatDeckMatchField Field, int Start, int Length);

/// <summary>Everything a rule may read from one line, worked out once.</summary>
public sealed record ChatDeckLineFacts(
    ChatEntry Entry,
    ChatDeckChannel Channel,
    string Text,
    string Sender,
    string Line,
    bool IsOwn)
{
    public static ChatDeckLineFacts From(in ChatEntry entry) => new(
        entry,
        ChatDeckChannels.Classify(entry),
        entry.Text,
        entry.Sender,
        ChatLineWording.Format(entry),
        ChatDeckChannels.IsOwnLine(entry));

    public string Read(ChatDeckMatchField field) => field switch
    {
        ChatDeckMatchField.Sender => Sender,
        ChatDeckMatchField.Line => Line,
        _ => Text,
    };
}
```

- [ ] **Step 5: Create the preset stubs** so the matcher compiles (Task 4 replaces both files' contents):

`ChatDeckPreset.cs`:

```csharp
namespace AcDream.Core.Chat.Deck;

public readonly record struct ChatDeckPresetSample(string Line, ChatDeckChannel Channel);

public sealed class ChatDeckPreset(
    string id, string group, string label, string description, int version, string pattern,
    IReadOnlySet<ChatDeckChannel> channels,
    IReadOnlyList<ChatDeckPresetSample> catches,
    IReadOnlyList<ChatDeckPresetSample> spares)
{
    public string Id { get; } = id;
    public string Group { get; } = group;
    public string Label { get; } = label;
    public string Description { get; } = description;
    public int Version { get; } = version;
    public string Pattern { get; } = pattern;
    /// <summary>Channels it applies to; empty means any.</summary>
    public IReadOnlySet<ChatDeckChannel> Channels { get; } = channels;
    public IReadOnlyList<ChatDeckPresetSample> Catches { get; } = catches;
    public IReadOnlyList<ChatDeckPresetSample> Spares { get; } = spares;
}
```

`ChatDeckPresets.cs`:

```csharp
namespace AcDream.Core.Chat.Deck;

public static class ChatDeckPresets
{
    public static IReadOnlyList<ChatDeckPreset> All { get; } = [];

    public static ChatDeckPreset? Get(string id) =>
        All.FirstOrDefault(p => string.Equals(p.Id, id, StringComparison.OrdinalIgnoreCase));
}
```

- [ ] **Step 6: Implement `ChatDeckMatcher.cs`**

```csharp
using System.Text.RegularExpressions;

namespace AcDream.Core.Chat.Deck;

public enum ChatDeckRuleStatus { Ok, Disabled, Invalid, Faulted, UnknownPreset }

public enum ChatDeckMatchOutcome { NoMatch, Match, Timeout }

public readonly record struct ChatDeckMatch(ChatDeckMatchOutcome Outcome, IReadOnlyList<ChatDeckSpan> Spans)
{
    public static readonly ChatDeckMatch None = new(ChatDeckMatchOutcome.NoMatch, []);
    public static readonly ChatDeckMatch TimedOut = new(ChatDeckMatchOutcome.Timeout, []);
}

/// <summary>A rule ready to run: its regex built, its field and channels resolved.</summary>
public sealed class ChatDeckCompiledRule
{
    internal ChatDeckCompiledRule(
        ChatDeckRule rule, ChatDeckRuleStatus status, string? error,
        Regex? regex, ChatDeckMatchField field, IReadOnlySet<ChatDeckChannel> channels, bool skipOwnLines)
    {
        Rule = rule;
        Status = status;
        Error = error;
        Regex = regex;
        Field = field;
        Channels = channels;
        SkipOwnLines = skipOwnLines;
    }

    public ChatDeckRule Rule { get; }
    public ChatDeckRuleStatus Status { get; }
    /// <summary>Why the rule is invalid, for the editor; null otherwise.</summary>
    public string? Error { get; }
    internal Regex? Regex { get; }
    internal ChatDeckMatchField Field { get; }
    internal IReadOnlySet<ChatDeckChannel> Channels { get; }
    internal bool SkipOwnLines { get; }
}

public static class ChatDeckMatcher
{
    public const int MaxPatternLength = 256;
    public const int MaxInputLength = 2000;
    public const int MaxSpans = 16;
    public static readonly TimeSpan MatchTimeout = TimeSpan.FromMilliseconds(20);

    private const RegexOptions Base = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;

    public static ChatDeckCompiledRule Compile(ChatDeckRule rule, string characterName)
    {
        if (!rule.Enabled) return Status(rule, ChatDeckRuleStatus.Disabled, null);
        if (rule.Faulted) return Status(rule, ChatDeckRuleStatus.Faulted, "Timed out on a line; edit it to turn it back on.");

        switch (rule.Kind)
        {
            case ChatDeckMatchKind.Preset:
            {
                ChatDeckPreset? preset = ChatDeckPresets.Get(rule.PresetId);
                if (preset is null) return Status(rule, ChatDeckRuleStatus.UnknownPreset, null);
                var regex = new Regex(preset.Pattern, Base, MatchTimeout);
                return new ChatDeckCompiledRule(rule, ChatDeckRuleStatus.Ok, null, regex,
                    ChatDeckMatchField.Line, preset.Channels, skipOwnLines: false);
            }
            case ChatDeckMatchKind.MyName:
            {
                if (string.IsNullOrWhiteSpace(characterName))
                    return Status(rule, ChatDeckRuleStatus.Disabled, null);
                // Lookarounds, not \b: a name may start with '+' (admins).
                var regex = new Regex($@"(?<!\w){Regex.Escape(characterName.Trim())}(?!\w)", Base, MatchTimeout);
                return new ChatDeckCompiledRule(rule, ChatDeckRuleStatus.Ok, null, regex,
                    ChatDeckMatchField.Text, rule.Channels, skipOwnLines: true);
            }
            default:
            {
                if (rule.Pattern.Length == 0)
                    return Status(rule, ChatDeckRuleStatus.Invalid, "The pattern is empty.");
                if (rule.Pattern.Length > MaxPatternLength)
                    return Status(rule, ChatDeckRuleStatus.Invalid, $"The pattern is longer than {MaxPatternLength} characters.");
                string pattern = rule.Kind == ChatDeckMatchKind.Contains ? Regex.Escape(rule.Pattern) : rule.Pattern;
                Regex? regex;
                try
                {
                    regex = BuildUserRegex(pattern);
                }
                catch (ArgumentException ex)
                {
                    return Status(rule, ChatDeckRuleStatus.Invalid, ex.Message);
                }
                return new ChatDeckCompiledRule(rule, ChatDeckRuleStatus.Ok, null, regex,
                    rule.Field, rule.Channels, skipOwnLines: false);
            }
        }
    }

    public static ChatDeckMatch Match(ChatDeckCompiledRule compiled, ChatDeckLineFacts facts, bool wantSpans)
    {
        if (compiled.Status != ChatDeckRuleStatus.Ok || compiled.Regex is null) return ChatDeckMatch.None;
        if (compiled.Channels.Count > 0 && !compiled.Channels.Contains(facts.Channel)) return ChatDeckMatch.None;
        if (compiled.SkipOwnLines && facts.IsOwn) return ChatDeckMatch.None;

        string input = facts.Read(compiled.Field);
        if (input.Length > MaxInputLength) input = input[..MaxInputLength];
        try
        {
            if (!wantSpans)
                return compiled.Regex.IsMatch(input)
                    ? new ChatDeckMatch(ChatDeckMatchOutcome.Match, [])
                    : ChatDeckMatch.None;

            var spans = new List<ChatDeckSpan>();
            for (Match m = compiled.Regex.Match(input); m.Success && spans.Count < MaxSpans; m = m.NextMatch())
            {
                if (m.Length == 0) continue;
                spans.Add(new ChatDeckSpan(compiled.Field, m.Index, m.Length));
            }
            return spans.Count > 0 ? new ChatDeckMatch(ChatDeckMatchOutcome.Match, spans) : ChatDeckMatch.None;
        }
        catch (RegexMatchTimeoutException)
        {
            return ChatDeckMatch.TimedOut;
        }
    }

    private static Regex BuildUserRegex(string pattern)
    {
        try
        {
            return new Regex(pattern, Base | RegexOptions.NonBacktracking, MatchTimeout);
        }
        catch (NotSupportedException)
        {
            // Lookarounds and backreferences need the backtracking engine; the timeout guards it.
            return new Regex(pattern, Base, MatchTimeout);
        }
    }

    private static ChatDeckCompiledRule Status(ChatDeckRule rule, ChatDeckRuleStatus status, string? error) =>
        new(rule, status, error, null, rule.Field, rule.Channels, skipOwnLines: false);
}
```

Note: a regex with only zero-length matches (e.g. `^`) still counts as a match for `IsMatch` but yields no spans; with `wantSpans: true` it returns `NoMatch`. That is acceptable: such a rule has nothing to colour.

- [ ] **Step 7: Run the Verify command** → PASS. If `Regex_PathologicalPattern_TimesOut` does not time out on this machine, lengthen the input to the 2,000-character cap (`new string('a', 2100)`), never raise the timeout.

- [ ] **Step 8: Commit**

```bash
git add src/AcDream.Core/Chat/Deck/ChatDeckRule.cs src/AcDream.Core/Chat/Deck/ChatDeckLineFacts.cs src/AcDream.Core/Chat/Deck/ChatDeckMatcher.cs src/AcDream.Core/Chat/Deck/ChatDeckPreset.cs src/AcDream.Core/Chat/Deck/ChatDeckPresets.cs tests/AcDream.Core.Tests/Chat/Deck/ChatDeckMatcherTests.cs
git commit -m "Chat Deck: rules and a safe matcher

Claude-Session: https://claude.ai/code/session_01WFS4EHSNafq2P1qfUuh258"
```

---

### Task 4: Standard presets (ported from RynthCore, MIT)

**Goal:** About 70 standard presets with fixtures, ported from RynthCore's two preset files, each tested against lines it must catch and near-misses it must spare.

**Files:**
- Modify: `src/AcDream.Core/Chat/Deck/ChatDeckPreset.cs` (add scope helpers)
- Modify: `src/AcDream.Core/Chat/Deck/ChatDeckPresets.cs` (the list)
- Modify: `NOTICE.md` (attribution section)
- Test: `tests/AcDream.Core.Tests/Chat/Deck/ChatDeckPresetsTests.cs`

**Sources (read both in full first; the submodule lives in the rynth worktree, commit `286abd1`):**
- `~/code/OpenAC/OpenAC-gateway-rynth/external/RynthCore/src/RynthCore.Engine/UI/Panels/RynthChatPresets.cs` — 63 presets with an `Example` and a `Scope`.
- `~/code/OpenAC/OpenAC-gateway-rynth/external/RynthCore/src/RynthCore.Engine/UI/Data/ChatFilterPresets.cs` — 37 presets with `Catches`/`Spares` fixtures and a `When` (`""`, `"type:N"`, `"chan:Combat"`).
- Both are MIT, Copyright (c) 2026 Tom Bohar (`external/RynthCore/LICENSE`).

**Porting rules:**

1. Take every preset in `RynthChatPresets.cs` (63) with its id, group, label, description and pattern copied verbatim (including the shared `KillPattern`, `HitPrefix`, `KnownHitPrefix`, `DealtMelee`, `DealtMagic`, `TakenMelee`, `TakenMagic` constants, copied as private constants).
2. Add the `ChatFilterPresets.cs` presets that have no equivalent: `cast.self`, `cast.surpassed`, `cast.no_components`, `cast.no_mana`, `notice.pk_battle`, `social.emotes` (6), giving 69.
3. These `ChatFilterPresets.cs` ids are the same preset as a `RynthChatPresets.cs` one; keep the `RynthChatPresets.cs` id and pattern and **add the `ChatFilterPresets.cs` catches and spares to it**: `cast.mine`, `cast.others`, `cast.components`, `cast.fizzle`, `cast.expires`, `combat.damage_taken`, `combat.melee_dealt`, `combat.magic_dealt`, `combat.melee_taken`, `combat.magic_taken`, `combat.recklessness`, `combat.sneak_attack`, `combat.stamina`, `combat.mana`, `combat.attack_resists`, `combat.defense_resists`, `combat.dirty_fighting`, `combat.missile_miss`, `item.heal_ok`, `item.salvage`, `item.aetheria_surge`, `item.banked_lum`, `social.kill_task`, `social.kill_task_progress` (same id); `combat.you_evaded` → `combat.defense_evades`; `combat.evaded_you` → `combat.attack_evades`; `combat.periodic_healing` → `item.regen`; `notice.busy` → `social.too_busy`; `chan.general` → `social.general`; `chan.trade` → `social.trade`; `chan.lfg` → `social.lfg`.
4. Channels: map RynthChat scopes with the helpers below — `Scope.NotPlayerChat` → `ChatDeckPresetScopes.NotPlayerChat`, `Scope.CombatOnly` and `"chan:Combat"` → `CombatOnly`, `Scope.ChannelsOnly` → `ChannelsOnly`, `Scope.Any` and `""` → `Any`, `"type:N"` → `ChatDeckPresetScopes.ForType(N)`. When the two files disagree for a merged preset, use the `RynthChatPresets.cs` scope.
5. Samples: a `ChatFilterPresets.cs` fixture `(text, channel, type)` becomes `new ChatDeckPresetSample(text, ChatDeckPresetScopes.SampleChannel(type))`. A `RynthChatPresets.cs` `Example` becomes a catch on the first channel of the preset's channel set in `ChatDeckChannels.Groups` order (`ChatDeckChannel.System` when the set is empty). Every preset with no spare gets a spare you write: for a preset limited to non-player channels, the same words said by a player (`Bob says, "<example>"` on `ChatDeckChannel.Say`); for an `Any` preset, a line with the same key words in a different shape that the pattern must not catch. Every preset ends with at least one catch and one spare.
6. Version 1 for all. Order: groups in the order Casting, Combat, Items, Social, Notices, NPCs, Pets; within a group, the source order.
7. If a ported pattern fails one of its own fixtures under our scopes, fix the **scope or sample channel** (our channel names differ from RynthChat's tabs), not the pattern; if the pattern itself is wrong for the fixture, keep the pattern and drop the fixture with a one-line comment saying why.

**Acceptance Criteria:**
- [ ] `ChatDeckPresets.All` has between 65 and 75 presets with unique ids (case-insensitive).
- [ ] Every preset has at least one catch and one spare; every catch matches and every spare does not, through `ChatDeckMatcher` with a `Preset` rule.
- [ ] Every preset pattern compiles with the backtracking engine and matches its samples within the timeout.
- [ ] `NOTICE.md` has a "RynthCore chat filter presets" section with the MIT text, the copyright line and the two source paths.

**Verify:** `MAC_TEST_FILTER='FullyQualifiedName~ChatDeckPresets' ~/code/OpenAC/test-baseline/run-mac-tests.sh ~/code/OpenAC/OpenAC-chat-deck-model tests/AcDream.Core.Tests/AcDream.Core.Tests.csproj` → all pass

**Steps:**

- [ ] **Step 1: Write the failing tests**

```csharp
using AcDream.Core.Chat;
using AcDream.Core.Chat.Deck;

namespace AcDream.Core.Tests.Chat.Deck;

public sealed class ChatDeckPresetsTests
{
    public static TheoryData<string> PresetIds()
    {
        var data = new TheoryData<string>();
        foreach (var p in ChatDeckPresets.All) data.Add(p.Id);
        return data;
    }

    private static ChatDeckMatchOutcome Run(ChatDeckPreset preset, ChatDeckPresetSample sample)
    {
        var rule = new ChatDeckRule { Id = "x", Kind = ChatDeckMatchKind.Preset, PresetId = preset.Id };
        var entry = new ChatEntry(ChatKind.System, "", sample.Line, 0u, 0u);
        var facts = new ChatDeckLineFacts(entry, sample.Channel, sample.Line, "", sample.Line, IsOwn: false);
        return ChatDeckMatcher.Match(ChatDeckMatcher.Compile(rule, "Davian"), facts, wantSpans: false).Outcome;
    }

    [Fact]
    public void Count_AndUniqueIds()
    {
        Assert.InRange(ChatDeckPresets.All.Count, 65, 75);
        Assert.Equal(ChatDeckPresets.All.Count,
            ChatDeckPresets.All.Select(p => p.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    [Theory]
    [MemberData(nameof(PresetIds))]
    public void Preset_CatchesItsSamplesAndSparesItsNearMisses(string id)
    {
        ChatDeckPreset preset = ChatDeckPresets.Get(id)!;
        Assert.NotEmpty(preset.Catches);
        Assert.NotEmpty(preset.Spares);
        Assert.All(preset.Catches, s => Assert.Equal(ChatDeckMatchOutcome.Match, Run(preset, s)));
        Assert.All(preset.Spares, s => Assert.Equal(ChatDeckMatchOutcome.NoMatch, Run(preset, s)));
    }

    [Fact]
    public void Scopes_MapRynthChatTypes()
    {
        Assert.True(ChatDeckPresetScopes.ForType(7).SetEquals(ChatDeckChannels.Group("Combat")));
        Assert.True(ChatDeckPresetScopes.ForType(21).SetEquals(ChatDeckChannels.Group("Combat")));
        Assert.True(ChatDeckPresetScopes.ForType(12).SetEquals(ChatDeckChannels.Group("Local")));
        Assert.True(ChatDeckPresetScopes.ForType(26).SetEquals(ChatDeckChannels.Group("System")));
        Assert.Equal(ChatDeckChannel.CombatSelf, ChatDeckPresetScopes.SampleChannel(22));
        Assert.Equal(ChatDeckChannel.Say, ChatDeckPresetScopes.SampleChannel(2));
        Assert.Empty(ChatDeckPresetScopes.Any);
    }
}
```

- [ ] **Step 2: Run the Verify command** → compile failure (`ChatDeckPresetScopes` missing).

- [ ] **Step 3: Add the scope helpers to `ChatDeckPreset.cs`** (below the existing types):

```csharp
/// <summary>RynthChat's preset scopes and chat types, as Chat Deck channel sets.</summary>
public static class ChatDeckPresetScopes
{
    public static readonly IReadOnlySet<ChatDeckChannel> Any = new HashSet<ChatDeckChannel>();
    public static readonly IReadOnlySet<ChatDeckChannel> CombatOnly = Of("Combat");
    public static readonly IReadOnlySet<ChatDeckChannel> ChannelsOnly = Of("Social");
    /// <summary>Anything a player cannot type: the Combat and System groups.</summary>
    public static readonly IReadOnlySet<ChatDeckChannel> NotPlayerChat = Of("Combat", "System");

    /// <summary>
    /// A RynthChat "type:N" as the whole group its channel is in: RynthChat's numbers and
    /// ours do not always agree on the exact channel (spell words, notices), the group does.
    /// </summary>
    public static IReadOnlySet<ChatDeckChannel> ForType(int chatType)
    {
        ChatDeckChannel channel = SampleChannel(chatType);
        string group = ChatDeckChannels.Groups.First(g => g.Channels.Contains(channel)).Name;
        return Of(group);
    }

    /// <summary>The channel a RynthChat fixture's chat type stands for.</summary>
    public static ChatDeckChannel SampleChannel(int chatType) => chatType switch
    {
        26 => ChatDeckChannel.System,   // RynthChat's "Notice"
        27 => ChatDeckChannel.General,  // RynthChat's "ServerChannel"
        _ => ChatDeckChannels.ByLogTextType((uint)chatType),
    };

    private static IReadOnlySet<ChatDeckChannel> Of(params string[] groups) =>
        groups.SelectMany(ChatDeckChannels.Group).ToHashSet();
}
```

- [ ] **Step 4: Write `ChatDeckPresets.cs`** following the porting rules. The file starts:

```csharp
namespace AcDream.Core.Chat.Deck;

// Standard filters for the Chat Deck. Ported from RynthCore's
// UI/Panels/RynthChatPresets.cs and UI/Data/ChatFilterPresets.cs (MIT,
// Copyright (c) 2026 Tom Bohar; see NOTICE.md), commit 286abd1: ids, labels,
// descriptions and patterns as written there, scopes mapped to Chat Deck
// channels, fixtures from ChatFilterPresets plus our own spares.
public static class ChatDeckPresets
{
    private static readonly IReadOnlySet<ChatDeckChannel> NotPlayerChat = ChatDeckPresetScopes.NotPlayerChat;
    private static readonly IReadOnlySet<ChatDeckChannel> CombatOnly = ChatDeckPresetScopes.CombatOnly;
    private static readonly IReadOnlySet<ChatDeckChannel> ChannelsOnly = ChatDeckPresetScopes.ChannelsOnly;
    private static readonly IReadOnlySet<ChatDeckChannel> Any = ChatDeckPresetScopes.Any;

    private static ChatDeckPresetSample S(string line, ChatDeckChannel channel) => new(line, channel);
    private static ChatDeckPresetSample S(string line, int rynthChatType) => new(line, ChatDeckPresetScopes.SampleChannel(rynthChatType));

    // Shared patterns, copied from RynthChatPresets.cs.
    private const string KillPattern = /* copy verbatim */ "";
    // ... HitPrefix, KnownHitPrefix, DealtMelee, DealtMagic, TakenMelee, TakenMagic

    public static IReadOnlyList<ChatDeckPreset> All { get; } =
    [
        // ── Casting ──
        new("cast.mine", "Casting", "Your spell words",
            "Your own incantations (You say, \"Malar Zhavik\"). Only spell-word lines: what you say in Local stays.",
            1, /* pattern from RynthChatPresets.cs cast.mine */ "",
            CombatOnly,
            [S("You say, \"Malar Zhavik\"", 17), S("You say, \"Puish Zharil\"", 17)],
            [S("You say, \"anyone selling keys?\"", 2)]),
        // ... every other preset, in the order given by porting rule 6
    ];

    public static IReadOnlyList<string> GroupOrder { get; } =
        ["Casting", "Combat", "Items", "Social", "Notices", "NPCs", "Pets"];

    public static ChatDeckPreset? Get(string id) =>
        All.FirstOrDefault(p => string.Equals(p.Id, id, StringComparison.OrdinalIgnoreCase));
}
```

The `/* copy verbatim */` markers above show *where* the source text goes. Replace each one with the exact text from the source file; none may remain in the committed file (`grep -n "copy verbatim\|from RynthChatPresets" src/AcDream.Core/Chat/Deck/ChatDeckPresets.cs` must print nothing).

- [ ] **Step 5: Add the NOTICE.md section** after the "Chorizite mesh primitives" section, in the same shape as the WorldBuilder one:

```markdown
## RynthCore chat filter presets

The Chat Deck's standard filters (`src/AcDream.Core/Chat/Deck/ChatDeckPresets.cs`)
are ported from RynthCore (https://aelrynth.com/git/rynth/RynthCore.git), MIT-licensed:
`src/RynthCore.Engine/UI/Panels/RynthChatPresets.cs` and
`src/RynthCore.Engine/UI/Data/ChatFilterPresets.cs` (commit 286abd1). Their ids,
labels, descriptions and patterns are kept; scopes are mapped to Chat Deck channels.

Original MIT license text:

MIT License

Copyright (c) 2026 Tom Bohar

(the rest of the MIT text exactly as in external/RynthCore/LICENSE)
```

Copy the full licence body from `~/code/OpenAC/OpenAC-gateway-rynth/external/RynthCore/LICENSE` in place of the last bracketed line.

- [ ] **Step 6: Run the Verify command** → PASS (follow porting rule 7 for any failing fixture).

- [ ] **Step 7: Commit**

```bash
git add src/AcDream.Core/Chat/Deck/ChatDeckPreset.cs src/AcDream.Core/Chat/Deck/ChatDeckPresets.cs NOTICE.md tests/AcDream.Core.Tests/Chat/Deck/ChatDeckPresetsTests.cs
git commit -m "Chat Deck: standard filter presets from RynthCore

Claude-Session: https://claude.ai/code/session_01WFS4EHSNafq2P1qfUuh258"
```

---

### Task 5: Settings records, defaults and normalisation

**Goal:** Tabs, appearance, layout and settings records; the limits; the default settings; and a normaliser that makes any settings value safe to use.

**Files:**
- Create: `src/AcDream.Core/Chat/Deck/ChatDeckSettings.cs`
- Test: `tests/AcDream.Core.Tests/Chat/Deck/ChatDeckSettingsTests.cs`

**Acceptance Criteria:**
- [ ] Defaults: tabs All, Local, Social, Group, Combat, System with the spec's channels, send channels and peek; one rule, the enabled My-name rule (`ColourLine`, alert on).
- [ ] Normalise: All present and first; duplicate tab ids removed; names trimmed to 24 (empty → "Tab"); at most 16 tabs and 200 rules; duplicate rule ids removed; Move/Copy to a missing tab disabled; appearance clamped; an unknown last tab becomes `"all"`.
- [ ] Every channel is in at least one default tab other than All.

**Verify:** `MAC_TEST_FILTER='FullyQualifiedName~ChatDeckSettingsTests' ~/code/OpenAC/test-baseline/run-mac-tests.sh ~/code/OpenAC/OpenAC-chat-deck-model tests/AcDream.Core.Tests/AcDream.Core.Tests.csproj` → all pass

**Steps:**

- [ ] **Step 1: Write the failing tests**

```csharp
using AcDream.Core.Chat.Deck;

namespace AcDream.Core.Tests.Chat.Deck;

public sealed class ChatDeckSettingsTests
{
    [Fact]
    public void Defaults_MatchTheSpec()
    {
        ChatDeckSettings s = ChatDeckDefaults.Create();
        Assert.Equal(["all", "local", "social", "group", "combat", "system"], s.Tabs.Select(t => t.Id));
        ChatDeckTab group = s.Tabs.Single(t => t.Id == "group");
        Assert.Equal(ChatDeckSendChannel.Fellowship, group.SendChannel);
        Assert.True(group.Peek);
        Assert.True(group.Channels.SetEquals(ChatDeckChannels.Group("Group")));
        Assert.Equal(ChatDeckSendChannel.General, s.Tabs.Single(t => t.Id == "social").SendChannel);
        Assert.True(s.Tabs.Single(t => t.Id == "local").Peek);
        Assert.False(s.Tabs.Single(t => t.Id == "combat").Peek);

        ChatDeckRule myName = Assert.Single(s.Rules);
        Assert.Equal(ChatDeckDefaults.MyNameRuleId, myName.Id);
        Assert.Equal(ChatDeckMatchKind.MyName, myName.Kind);
        Assert.Equal(ChatDeckRuleAction.ColourLine, myName.Action);
        Assert.True(myName.Alert && myName.Enabled);
    }

    [Fact]
    public void Defaults_CoverEveryChannelOutsideAll()
    {
        var covered = ChatDeckDefaults.Create().Tabs.Where(t => t.Id != "all").SelectMany(t => t.Channels).ToHashSet();
        Assert.All(Enum.GetValues<ChatDeckChannel>(), c => Assert.Contains(c, covered));
    }

    [Fact]
    public void Normalize_RepairsTabs()
    {
        var d = ChatDeckDefaults.Create();
        var local = d.Tabs[1];
        var many = Enumerable.Range(0, 30).Select(i => local with { Id = $"t{i}", Name = new string('x', 40) }).ToList();
        many.Insert(3, local with { Id = "t1", Name = "dup" });
        many.Add(local with { Id = "t99", Name = "   " });
        var s = ChatDeckSettingsNormalizer.Normalize(d with { Tabs = many });

        Assert.Equal("all", s.Tabs[0].Id);
        Assert.Equal(ChatDeckLimits.MaxTabs, s.Tabs.Count);
        Assert.Equal(s.Tabs.Count, s.Tabs.Select(t => t.Id).Distinct().Count());
        Assert.All(s.Tabs, t => Assert.InRange(t.Name.Length, 1, ChatDeckLimits.MaxTabNameLength));
        var blank = ChatDeckSettingsNormalizer.Normalize(d with { Tabs = [d.Tabs[0], local with { Name = "  " }] });
        Assert.Equal("Tab", blank.Tabs[1].Name);
    }

    [Fact]
    public void Normalize_RepairsRulesAppearanceAndLayout()
    {
        var d = ChatDeckDefaults.Create();
        var move = new ChatDeckRule { Id = "m", Kind = ChatDeckMatchKind.Contains, Pattern = "x",
            Action = ChatDeckRuleAction.MoveTo, TargetTabId = "gone" };
        var rules = Enumerable.Range(0, 250)
            .Select(i => new ChatDeckRule { Id = $"r{i}", Kind = ChatDeckMatchKind.Contains, Pattern = "x" })
            .Prepend(move).Prepend(move).ToList();
        var s = ChatDeckSettingsNormalizer.Normalize(d with
        {
            Rules = rules,
            Appearance = d.Appearance with { FontSize = 99, InputFontSize = 1, LineSpacing = 3f, BackgroundOpacity = -1f, FadeAfterSeconds = 0 },
            Layout = d.Layout with { LastTabId = "nope" },
        });

        Assert.Equal(ChatDeckLimits.MaxRules, s.Rules.Count);
        Assert.Single(s.Rules, r => r.Id == "m");
        Assert.False(s.Rules.Single(r => r.Id == "m").Enabled);
        Assert.Equal(20, s.Appearance.FontSize);
        Assert.Equal(10, s.Appearance.InputFontSize);
        Assert.Equal(1.6f, s.Appearance.LineSpacing);
        Assert.Equal(0f, s.Appearance.BackgroundOpacity);
        Assert.Equal(1, s.Appearance.FadeAfterSeconds);
        Assert.Equal("all", s.Layout.LastTabId);
    }

    [Fact]
    public void Normalize_InsertsAllWhenMissing()
    {
        var d = ChatDeckDefaults.Create();
        var s = ChatDeckSettingsNormalizer.Normalize(d with { Tabs = d.Tabs.Skip(1).ToList() });
        Assert.Equal("all", s.Tabs[0].Id);
        Assert.Equal(d.Tabs.Count, s.Tabs.Count);
    }
}
```

- [ ] **Step 2: Run the Verify command** → compile failure.

- [ ] **Step 3: Implement `ChatDeckSettings.cs`**

```csharp
namespace AcDream.Core.Chat.Deck;

public enum ChatDeckIcon { All, Local, Group, Social, Combat, System, Loot, Star, Tell, Shield, Scroll, Coin }
public enum ChatDeckTimestamps { Off, HourMinute, HourMinuteSecond }
public enum ChatDeckEdge { Left, Right }

public static class ChatDeckLimits
{
    public const int MaxTabs = 16;
    public const int MaxRules = 200;
    public const int MaxTabNameLength = 24;
    public const int MaxSettingsJsonLength = 256 * 1024;
    public const int HistoryPerTab = 1000;
}

public sealed record ChatDeckTab
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public ChatDeckIcon Icon { get; init; }
    public IReadOnlySet<ChatDeckChannel> Channels { get; init; } = ChatDeckRule.AnyChannel;
    public ChatDeckSendChannel SendChannel { get; init; }
    public bool Peek { get; init; }
}

public sealed record ChatDeckAppearance
{
    public int FontSize { get; init; } = 14;
    public int InputFontSize { get; init; } = 14;
    public float LineSpacing { get; init; } = 1.2f;
    public bool WrapIndent { get; init; } = true;
    public float BackgroundOpacity { get; init; } = 0.85f;
    public ChatDeckTimestamps Timestamps { get; init; } = ChatDeckTimestamps.HourMinute;
    public bool ChannelPrefix { get; init; } = true;
    /// <summary>Per-channel colour overrides, 0xAARRGGBB; a channel not listed uses its default colour.</summary>
    public IReadOnlyDictionary<ChatDeckChannel, uint> ChannelColours { get; init; } = new Dictionary<ChatDeckChannel, uint>();
    public bool FadeWhenIdle { get; init; }
    public int FadeAfterSeconds { get; init; } = 10;
    public bool BeepOnAlert { get; init; }
}

public sealed record ChatDeckLayoutState
{
    public bool Pinned { get; init; } = true;
    public ChatDeckEdge Edge { get; init; } = ChatDeckEdge.Left;
    public string LastTabId { get; init; } = ChatDeckDefaults.AllTabId;
}

public sealed record ChatDeckSettings
{
    public const int CurrentVersion = 1;

    public int Version { get; init; } = CurrentVersion;
    public required IReadOnlyList<ChatDeckTab> Tabs { get; init; }
    public required IReadOnlyList<ChatDeckRule> Rules { get; init; }
    public ChatDeckAppearance Appearance { get; init; } = new();
    public ChatDeckLayoutState Layout { get; init; } = new();
}

public static class ChatDeckDefaults
{
    public const string AllTabId = "all";
    public const string MyNameRuleId = "my-name";
    public const uint MyNameColour = 0xFFFF7A8Cu;

    public static ChatDeckTab AllTab() => new()
    {
        Id = AllTabId, Name = "All", Icon = ChatDeckIcon.All,
        Channels = Enum.GetValues<ChatDeckChannel>().ToHashSet(),
        SendChannel = ChatDeckSendChannel.Say,
    };

    public static ChatDeckSettings Create() => new()
    {
        Tabs =
        [
            AllTab(),
            Tab("local", "Local", ChatDeckIcon.Local, "Local", ChatDeckSendChannel.Say, peek: true),
            Tab("social", "Social", ChatDeckIcon.Social, "Social", ChatDeckSendChannel.General, peek: false),
            Tab("group", "Group", ChatDeckIcon.Group, "Group", ChatDeckSendChannel.Fellowship, peek: true),
            Tab("combat", "Combat", ChatDeckIcon.Combat, "Combat", ChatDeckSendChannel.Say, peek: false),
            Tab("system", "System", ChatDeckIcon.System, "System", ChatDeckSendChannel.Say, peek: false),
        ],
        Rules =
        [
            new ChatDeckRule
            {
                Id = MyNameRuleId, Kind = ChatDeckMatchKind.MyName,
                Action = ChatDeckRuleAction.ColourLine, Colour = MyNameColour, Alert = true,
            },
        ],
    };

    private static ChatDeckTab Tab(
        string id, string name, ChatDeckIcon icon, string group, ChatDeckSendChannel send, bool peek) => new()
    {
        Id = id, Name = name, Icon = icon,
        Channels = ChatDeckChannels.Group(group).ToHashSet(),
        SendChannel = send, Peek = peek,
    };
}

public static class ChatDeckSettingsNormalizer
{
    public static ChatDeckSettings Normalize(ChatDeckSettings settings)
    {
        var tabs = new List<ChatDeckTab>();
        var tabIds = new HashSet<string>(StringComparer.Ordinal);
        ChatDeckTab? all = settings.Tabs.FirstOrDefault(t => t.Id == ChatDeckDefaults.AllTabId);
        tabs.Add((all ?? ChatDeckDefaults.AllTab()) with { Name = CleanName(all?.Name ?? "All") });
        tabIds.Add(ChatDeckDefaults.AllTabId);
        foreach (ChatDeckTab tab in settings.Tabs)
        {
            if (tabs.Count >= ChatDeckLimits.MaxTabs) break;
            if (string.IsNullOrEmpty(tab.Id) || !tabIds.Add(tab.Id)) continue;
            tabs.Add(tab with { Name = CleanName(tab.Name) });
        }

        var rules = new List<ChatDeckRule>();
        var ruleIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (ChatDeckRule rule in settings.Rules)
        {
            if (rules.Count >= ChatDeckLimits.MaxRules) break;
            if (string.IsNullOrEmpty(rule.Id) || !ruleIds.Add(rule.Id)) continue;
            bool targets = rule.Action is ChatDeckRuleAction.MoveTo or ChatDeckRuleAction.CopyTo;
            rules.Add(targets && !tabIds.Contains(rule.TargetTabId) ? rule with { Enabled = false } : rule);
        }

        ChatDeckAppearance a = settings.Appearance;
        var appearance = a with
        {
            FontSize = Math.Clamp(a.FontSize, 10, 20),
            InputFontSize = Math.Clamp(a.InputFontSize, 10, 20),
            LineSpacing = Math.Clamp(a.LineSpacing, 1.0f, 1.6f),
            BackgroundOpacity = Math.Clamp(a.BackgroundOpacity, 0f, 1f),
            FadeAfterSeconds = Math.Clamp(a.FadeAfterSeconds, 1, 600),
        };

        var layout = tabIds.Contains(settings.Layout.LastTabId)
            ? settings.Layout
            : settings.Layout with { LastTabId = ChatDeckDefaults.AllTabId };

        return settings with { Tabs = tabs, Rules = rules, Appearance = appearance, Layout = layout };
    }

    private static string CleanName(string name)
    {
        string trimmed = name.Trim();
        if (trimmed.Length == 0) return "Tab";
        return trimmed.Length > ChatDeckLimits.MaxTabNameLength ? trimmed[..ChatDeckLimits.MaxTabNameLength] : trimmed;
    }
}
```

- [ ] **Step 4: Run the Verify command** → PASS.

- [ ] **Step 5: Commit**

```bash
git add src/AcDream.Core/Chat/Deck/ChatDeckSettings.cs tests/AcDream.Core.Tests/Chat/Deck/ChatDeckSettingsTests.cs
git commit -m "Chat Deck: settings, defaults and limits

Claude-Session: https://claude.ai/code/session_01WFS4EHSNafq2P1qfUuh258"
```

---

### Task 6: The verdict engine

**Goal:** `ChatDeckRuleEngine.Evaluate` turns one line's facts into a verdict exactly as the spec's verdict list says, recording which rules matched and decided, and which timed out.

**Files:**
- Create: `src/AcDream.Core/Chat/Deck/ChatDeckRuleEngine.cs`
- Test: `tests/AcDream.Core.Tests/Chat/Deck/ChatDeckRuleEngineTests.cs`

**Acceptance Criteria:**
- [ ] Hide after a Move or Copy still hides; a hidden line has no destinations and no alert.
- [ ] Destinations: subscribed tabs plus All; the first Move replaces subscriptions (All kept); later Moves ignored; every Copy adds; Copy to the Move target and Move to All each give one entry; missing targets ignored; order follows the tab list.
- [ ] First ColourLine and first ColourMatch win; ColourMatch carries spans.
- [ ] An alert rule or an incoming tell makes the line an alert.
- [ ] A rule that times out counts as no match and its id is in `TimedOutRuleIds`.
- [ ] `Hits` lists every matching rule in order, with `Decided` set on the ones that took effect.

**Verify:** `MAC_TEST_FILTER='FullyQualifiedName~ChatDeckRuleEngine' ~/code/OpenAC/test-baseline/run-mac-tests.sh ~/code/OpenAC/OpenAC-chat-deck-model tests/AcDream.Core.Tests/AcDream.Core.Tests.csproj` → all pass

**Steps:**

- [ ] **Step 1: Write the failing tests**

```csharp
using AcDream.Core.Chat.Deck;

namespace AcDream.Core.Tests.Chat.Deck;

public sealed class ChatDeckRuleEngineTests
{
    private static readonly IReadOnlyList<ChatDeckTab> Tabs = ChatDeckDefaults.Create().Tabs;

    private static ChatDeckRule R(string id, ChatDeckRuleAction action, string pattern = "loot",
        string target = "", uint colour = 0, bool alert = false) =>
        new() { Id = id, Kind = ChatDeckMatchKind.Contains, Pattern = pattern, Action = action,
                TargetTabId = target, Colour = colour, Alert = alert };

    private static ChatDeckVerdict Eval(ChatDeckLineFacts facts, params ChatDeckRule[] rules) =>
        new ChatDeckRuleEngine(rules, Tabs, "Davian").Evaluate(facts);

    private static ChatDeckLineFacts Line(string text = "free loot here", ChatDeckChannel channel = ChatDeckChannel.Say) =>
        ChatDeckMatcherTests.Facts(text, channel);

    [Fact]
    public void NoRules_GoesToSubscribedTabsAndAll()
    {
        var v = Eval(Line());
        Assert.False(v.Hidden);
        Assert.Equal(["all", "local"], v.Destinations);
    }

    [Fact]
    public void Hide_WinsWhereverItSits()
    {
        var v = Eval(Line(), R("m", ChatDeckRuleAction.MoveTo, target: "combat"), R("c", ChatDeckRuleAction.CopyTo, target: "group"),
            R("h", ChatDeckRuleAction.Hide, alert: true));
        Assert.True(v.Hidden);
        Assert.Empty(v.Destinations);
        Assert.False(v.Alert);
        Assert.Contains(v.Hits, h => h.RuleId == "h" && h.Decided);
    }

    [Fact]
    public void FirstMove_ReplacesSubscriptions_CopiesAdd()
    {
        var v = Eval(Line(),
            R("m1", ChatDeckRuleAction.MoveTo, target: "combat"),
            R("m2", ChatDeckRuleAction.MoveTo, target: "system"),
            R("c1", ChatDeckRuleAction.CopyTo, target: "group"),
            R("c2", ChatDeckRuleAction.CopyTo, target: "combat"),
            R("c3", ChatDeckRuleAction.CopyTo, target: "gone"));
        Assert.Equal(["all", "group", "combat"], v.Destinations);
        Assert.True(v.Hits.Single(h => h.RuleId == "m1").Decided);
        Assert.False(v.Hits.Single(h => h.RuleId == "m2").Decided);
        Assert.False(v.Hits.Single(h => h.RuleId == "c3").Decided);
    }

    [Fact]
    public void MoveToAll_GivesOnlyAll()
    {
        Assert.Equal(["all"], Eval(Line(), R("m", ChatDeckRuleAction.MoveTo, target: "all")).Destinations);
    }

    [Fact]
    public void MoveToMissingTab_IsIgnored()
    {
        Assert.Equal(["all", "local"], Eval(Line(), R("m", ChatDeckRuleAction.MoveTo, target: "gone")).Destinations);
    }

    [Fact]
    public void FirstColourOfEachKindWins()
    {
        var v = Eval(Line(),
            R("l1", ChatDeckRuleAction.ColourLine, colour: 0xFF000001),
            R("l2", ChatDeckRuleAction.ColourLine, colour: 0xFF000002),
            R("x1", ChatDeckRuleAction.ColourMatch, colour: 0xFF000003),
            R("x2", ChatDeckRuleAction.ColourMatch, colour: 0xFF000004));
        Assert.Equal(0xFF000001u, v.LineColour);
        Assert.Equal(0xFF000003u, v.MatchColour);
        Assert.Equal(new ChatDeckSpan(ChatDeckMatchField.Text, 5, 4), Assert.Single(v.MatchSpans));
    }

    [Fact]
    public void Alerts_FromRuleOrIncomingTell()
    {
        Assert.True(Eval(Line(), R("a", ChatDeckRuleAction.ColourLine, alert: true)).Alert);
        Assert.True(Eval(Line("hi", ChatDeckChannel.TellIn)).Alert);
        Assert.False(Eval(Line("hi", ChatDeckChannel.TellOut)).Alert);
        Assert.False(Eval(Line()).Alert);
    }

    [Fact]
    public void TimedOutRule_IsReportedAndIgnored()
    {
        var slow = new ChatDeckRule { Id = "slow", Kind = ChatDeckMatchKind.Regex, Pattern = @"^(\w+\s?)*\1$", Action = ChatDeckRuleAction.Hide };
        var v = Eval(Line(new string('a', 1990) + "!"), slow);
        Assert.False(v.Hidden);
        Assert.Equal(["slow"], v.TimedOutRuleIds);
    }
}
```

- [ ] **Step 2: Run the Verify command** → compile failure.

- [ ] **Step 3: Implement `ChatDeckRuleEngine.cs`**

```csharp
namespace AcDream.Core.Chat.Deck;

/// <summary>One rule that matched a line; <see cref="Decided"/> when it took effect.</summary>
public readonly record struct ChatDeckRuleHit(string RuleId, ChatDeckRuleAction Action, bool Decided);

public sealed record ChatDeckVerdict(
    bool Hidden,
    IReadOnlyList<string> Destinations,
    uint? LineColour,
    uint? MatchColour,
    IReadOnlyList<ChatDeckSpan> MatchSpans,
    bool Alert,
    IReadOnlyList<ChatDeckRuleHit> Hits,
    IReadOnlyList<string> TimedOutRuleIds);

/// <summary>Rules and tabs, ready to decide where each line goes.</summary>
public sealed class ChatDeckRuleEngine
{
    private readonly IReadOnlyList<ChatDeckTab> _tabs;
    private readonly HashSet<string> _tabIds;

    public ChatDeckRuleEngine(IReadOnlyList<ChatDeckRule> rules, IReadOnlyList<ChatDeckTab> tabs, string characterName)
    {
        _tabs = tabs;
        _tabIds = tabs.Select(t => t.Id).ToHashSet(StringComparer.Ordinal);
        Rules = rules.Select(r => ChatDeckMatcher.Compile(r, characterName)).ToList();
    }

    public IReadOnlyList<ChatDeckCompiledRule> Rules { get; }

    public ChatDeckVerdict Evaluate(ChatDeckLineFacts facts)
    {
        var matched = new List<(ChatDeckCompiledRule Rule, ChatDeckMatch Match)>();
        var timedOut = new List<string>();
        foreach (ChatDeckCompiledRule rule in Rules)
        {
            bool wantSpans = rule.Rule.Action == ChatDeckRuleAction.ColourMatch;
            ChatDeckMatch m = ChatDeckMatcher.Match(rule, facts, wantSpans);
            if (m.Outcome == ChatDeckMatchOutcome.Timeout) timedOut.Add(rule.Rule.Id);
            else if (m.Outcome == ChatDeckMatchOutcome.Match) matched.Add((rule, m));
        }

        int hideIndex = matched.FindIndex(x => x.Rule.Rule.Action == ChatDeckRuleAction.Hide);
        if (hideIndex >= 0)
        {
            var hideHits = matched.Select((x, i) => new ChatDeckRuleHit(x.Rule.Rule.Id, x.Rule.Rule.Action, i == hideIndex)).ToList();
            return new ChatDeckVerdict(true, [], null, null, [], false, hideHits, timedOut);
        }

        var destinations = new HashSet<string>(StringComparer.Ordinal) { ChatDeckDefaults.AllTabId };
        foreach (ChatDeckTab tab in _tabs)
            if (tab.Id != ChatDeckDefaults.AllTabId && tab.Channels.Contains(facts.Channel))
                destinations.Add(tab.Id);

        var hits = new List<ChatDeckRuleHit>();
        bool moved = false;
        uint? lineColour = null, matchColour = null;
        IReadOnlyList<ChatDeckSpan> spans = [];
        bool alert = facts.Channel == ChatDeckChannel.TellIn;

        foreach (var (compiled, match) in matched)
        {
            ChatDeckRule rule = compiled.Rule;
            bool decided = false;
            switch (rule.Action)
            {
                case ChatDeckRuleAction.MoveTo when !moved && _tabIds.Contains(rule.TargetTabId):
                    destinations.Clear();
                    destinations.Add(ChatDeckDefaults.AllTabId);
                    destinations.Add(rule.TargetTabId);
                    moved = decided = true;
                    break;
                case ChatDeckRuleAction.CopyTo when _tabIds.Contains(rule.TargetTabId):
                    destinations.Add(rule.TargetTabId);
                    decided = true;
                    break;
                case ChatDeckRuleAction.ColourLine when lineColour is null:
                    lineColour = rule.Colour;
                    decided = true;
                    break;
                case ChatDeckRuleAction.ColourMatch when matchColour is null:
                    matchColour = rule.Colour;
                    spans = match.Spans;
                    decided = true;
                    break;
            }
            if (rule.Alert) alert = true;
            hits.Add(new ChatDeckRuleHit(rule.Id, rule.Action, decided));
        }

        // Copies were added before the Move cleared; re-apply copies that matched after it.
        // Simplest correct order: rebuild from the decided hits.
        if (moved)
        {
            foreach (ChatDeckRuleHit hit in hits)
                if (hit.Decided && hit.Action == ChatDeckRuleAction.CopyTo)
                    destinations.Add(matched.First(x => x.Rule.Rule.Id == hit.RuleId).Rule.Rule.TargetTabId);
        }

        var ordered = _tabs.Where(t => destinations.Contains(t.Id)).Select(t => t.Id).ToList();
        return new ChatDeckVerdict(false, ordered, lineColour, matchColour, spans, alert, hits, timedOut);
    }
}
```

The re-apply loop exists because a Copy listed **before** the first Move is added and then wiped by the Move's `Clear()`; the spec says every Copy adds regardless of position.

- [ ] **Step 4: Add a test for that order** to `ChatDeckRuleEngineTests`:

```csharp
    [Fact]
    public void CopyBeforeMove_StillAdds()
    {
        var v = Eval(Line(), R("c", ChatDeckRuleAction.CopyTo, target: "group"), R("m", ChatDeckRuleAction.MoveTo, target: "combat"));
        Assert.Equal(["all", "group", "combat"], v.Destinations);
    }
```

- [ ] **Step 5: Run the Verify command** → PASS.

- [ ] **Step 6: Commit**

```bash
git add src/AcDream.Core/Chat/Deck/ChatDeckRuleEngine.cs tests/AcDream.Core.Tests/Chat/Deck/ChatDeckRuleEngineTests.cs
git commit -m "Chat Deck: verdict engine

Claude-Session: https://claude.ai/code/session_01WFS4EHSNafq2P1qfUuh258"
```

---

### Task 7: Lines and per-tab state

**Goal:** The immutable `ChatDeckLine` and `ChatDeckTabState`: a 1,000-entry ring of line references with unread count and alert flag.

**Files:**
- Create: `src/AcDream.Core/Chat/Deck/ChatDeckLine.cs`
- Create: `src/AcDream.Core/Chat/Deck/ChatDeckTabState.cs`
- Test: `tests/AcDream.Core.Tests/Chat/Deck/ChatDeckTabStateTests.cs`

**Acceptance Criteria:**
- [ ] The ring keeps the newest `capacity` lines, oldest first, and holds the same object references it was given.
- [ ] `Add(line, seen: false)` raises `Unread` and, for an alert line, sets `Alert`; `seen: true` changes neither.
- [ ] `MarkRead()` clears both; `Clear()` also empties the history.

**Verify:** `MAC_TEST_FILTER='FullyQualifiedName~ChatDeckTabState' ~/code/OpenAC/test-baseline/run-mac-tests.sh ~/code/OpenAC/OpenAC-chat-deck-model tests/AcDream.Core.Tests/AcDream.Core.Tests.csproj` → all pass

**Steps:**

- [ ] **Step 1: Write the failing tests**

```csharp
using AcDream.Core.Chat.Deck;

namespace AcDream.Core.Tests.Chat.Deck;

public sealed class ChatDeckTabStateTests
{
    private static ChatDeckLine Line(string text, bool alert = false)
    {
        var facts = ChatDeckMatcherTests.Facts(text);
        var verdict = new ChatDeckVerdict(false, ["all"], null, null, [], alert, [], []);
        return new ChatDeckLine(facts, verdict);
    }

    [Fact]
    public void Ring_KeepsNewestInOrder_SameReferences()
    {
        var tab = new ChatDeckTabState("local", capacity: 3);
        var lines = Enumerable.Range(0, 5).Select(i => Line($"l{i}")).ToList();
        foreach (var l in lines) tab.Add(l, seen: true);

        Assert.Equal(3, tab.Count);
        Assert.Same(lines[2], tab[0]);
        Assert.Same(lines[4], tab[2]);
        Assert.Equal(lines.Skip(2), tab.Snapshot());
    }

    [Fact]
    public void ReadState()
    {
        var tab = new ChatDeckTabState("local");
        tab.Add(Line("a"), seen: true);
        Assert.Equal(0, tab.Unread);
        tab.Add(Line("b"), seen: false);
        tab.Add(Line("c", alert: true), seen: false);
        Assert.Equal(2, tab.Unread);
        Assert.True(tab.Alert);
        tab.Add(Line("d", alert: true), seen: true);
        Assert.Equal(2, tab.Unread);

        tab.MarkRead();
        Assert.Equal(0, tab.Unread);
        Assert.False(tab.Alert);
        Assert.Equal(4, tab.Count);

        tab.Add(Line("e"), seen: false);
        tab.Clear();
        Assert.Equal(0, tab.Count);
        Assert.Equal(0, tab.Unread);
    }
}
```

- [ ] **Step 2: Run the Verify command** → compile failure.

- [ ] **Step 3: Implement `ChatDeckLine.cs`**

```csharp
namespace AcDream.Core.Chat.Deck;

/// <summary>One line as the Chat Deck shows it, evaluated once and shared by every tab it is in.</summary>
public sealed class ChatDeckLine(ChatDeckLineFacts facts, ChatDeckVerdict verdict)
{
    public ChatDeckLineFacts Facts { get; } = facts;
    public ChatEntry Entry => Facts.Entry;
    public ChatDeckChannel Channel => Facts.Channel;
    public long Sequence => Facts.Entry.Sequence;
    public uint? LineColour { get; } = verdict.LineColour;
    public uint? MatchColour { get; } = verdict.MatchColour;
    public IReadOnlyList<ChatDeckSpan> MatchSpans { get; } = verdict.MatchSpans;
    public bool Alert { get; } = verdict.Alert;
}
```

- [ ] **Step 4: Implement `ChatDeckTabState.cs`**

```csharp
namespace AcDream.Core.Chat.Deck;

/// <summary>One tab's lines (newest <see cref="Capacity"/>, oldest first) and whether it has unseen ones.</summary>
public sealed class ChatDeckTabState
{
    private readonly ChatDeckLine?[] _ring;
    private int _start;

    public ChatDeckTabState(string tabId, int capacity = ChatDeckLimits.HistoryPerTab)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(capacity, 1);
        TabId = tabId;
        _ring = new ChatDeckLine?[capacity];
    }

    public string TabId { get; }
    public int Capacity => _ring.Length;
    public int Count { get; private set; }
    public int Unread { get; private set; }
    public bool Alert { get; private set; }

    public ChatDeckLine this[int index]
    {
        get
        {
            ArgumentOutOfRangeException.ThrowIfNegative(index);
            ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, Count);
            return _ring[(_start + index) % _ring.Length]!;
        }
    }

    public IReadOnlyList<ChatDeckLine> Snapshot()
    {
        var copy = new ChatDeckLine[Count];
        for (int i = 0; i < Count; i++) copy[i] = this[i];
        return copy;
    }

    public void Add(ChatDeckLine line, bool seen)
    {
        if (Count < _ring.Length)
        {
            _ring[(_start + Count) % _ring.Length] = line;
            Count++;
        }
        else
        {
            _ring[_start] = line;
            _start = (_start + 1) % _ring.Length;
        }
        if (seen) return;
        Unread++;
        if (line.Alert) Alert = true;
    }

    public void MarkRead()
    {
        Unread = 0;
        Alert = false;
    }

    public void Clear()
    {
        Array.Clear(_ring);
        _start = 0;
        Count = 0;
        MarkRead();
    }
}
```

- [ ] **Step 5: Run the Verify command** → PASS.

- [ ] **Step 6: Commit**

```bash
git add src/AcDream.Core/Chat/Deck/ChatDeckLine.cs src/AcDream.Core/Chat/Deck/ChatDeckTabState.cs tests/AcDream.Core.Tests/Chat/Deck/ChatDeckTabStateTests.cs
git commit -m "Chat Deck: lines and per-tab history

Claude-Session: https://claude.ai/code/session_01WFS4EHSNafq2P1qfUuh258"
```

---

### Task 8: Settings JSON

**Goal:** Read and write `ChatDeckSettings` as the `chatDeck` JSON object, keeping unknown keys, and falling back to defaults on bad input.

**Files:**
- Create: `src/AcDream.Core/Chat/Deck/ChatDeckSettingsJson.cs`
- Test: `tests/AcDream.Core.Tests/Chat/Deck/ChatDeckSettingsJsonTests.cs`

**Format** (camelCase keys, enum names, `#AARRGGBB` colours):

```json
{
  "version": 1,
  "tabs": [ { "id": "local", "name": "Local", "icon": "Local", "channels": ["Say", "Emote"], "send": "Say", "peek": true } ],
  "rules": [ { "id": "r1", "enabled": true, "kind": "Contains", "pattern": "x", "presetId": "", "field": "Text",
               "channels": [], "action": "Hide", "target": "", "colour": "#FFFF0000", "alert": false, "faulted": false } ],
  "appearance": { "fontSize": 14, "inputFontSize": 14, "lineSpacing": 1.2, "wrapIndent": true, "backgroundOpacity": 0.85,
                  "timestamps": "HourMinute", "channelPrefix": true, "channelColours": { "Say": "#FFE0E0E0" },
                  "fadeWhenIdle": false, "fadeAfterSeconds": 10, "beepOnAlert": false },
  "layout": { "pinned": true, "edge": "Left", "lastTab": "all" }
}
```

**Acceptance Criteria:**
- [ ] Round trip: `Parse(Serialize(s))` equals `s` field by field (sets compared with `SetEquals`).
- [ ] `null`/empty → defaults, no error; malformed JSON, a non-object, or over 262,144 characters → defaults with a non-null error.
- [ ] Unknown keys at top level, inside a tab, a rule, appearance and layout survive `Serialize(settings, previousJson)`.
- [ ] Unknown preset ids survive a round trip; unknown enum values in arrays are dropped; an unknown scalar enum value falls back to that field's default.
- [ ] Parsed settings are normalised (`ChatDeckSettingsNormalizer`).
- [ ] A `version` higher than 1 is read and kept.

**Verify:** `MAC_TEST_FILTER='FullyQualifiedName~ChatDeckSettingsJson' ~/code/OpenAC/test-baseline/run-mac-tests.sh ~/code/OpenAC/OpenAC-chat-deck-model tests/AcDream.Core.Tests/AcDream.Core.Tests.csproj` → all pass

**Steps:**

- [ ] **Step 1: Write the failing tests**

```csharp
using System.Text.Json.Nodes;
using AcDream.Core.Chat.Deck;

namespace AcDream.Core.Tests.Chat.Deck;

public sealed class ChatDeckSettingsJsonTests
{
    private static ChatDeckSettings Sample()
    {
        var d = ChatDeckDefaults.Create();
        return d with
        {
            Rules =
            [
                .. d.Rules,
                new ChatDeckRule { Id = "r1", Kind = ChatDeckMatchKind.Regex, Pattern = "^x", Field = ChatDeckMatchField.Line,
                    Channels = new HashSet<ChatDeckChannel> { ChatDeckChannel.Trade }, Action = ChatDeckRuleAction.CopyTo,
                    TargetTabId = "group", Colour = 0x80112233, Alert = true, Faulted = true },
                new ChatDeckRule { Id = "p1", Kind = ChatDeckMatchKind.Preset, PresetId = "future.preset", Action = ChatDeckRuleAction.Hide },
            ],
            Appearance = d.Appearance with
            {
                FontSize = 17, LineSpacing = 1.4f, Timestamps = ChatDeckTimestamps.HourMinuteSecond, ChannelPrefix = false,
                ChannelColours = new Dictionary<ChatDeckChannel, uint> { [ChatDeckChannel.Say] = 0xFFE0E0E0 },
                FadeWhenIdle = true, BeepOnAlert = true,
            },
            Layout = new ChatDeckLayoutState { Pinned = false, Edge = ChatDeckEdge.Right, LastTabId = "group" },
        };
    }

    [Fact]
    public void RoundTrip()
    {
        ChatDeckSettings s = Sample();
        var (back, error) = ChatDeckSettingsJson.Parse(ChatDeckSettingsJson.Serialize(s, previousJson: null));
        Assert.Null(error);
        Assert.Equal(s.Tabs.Count, back.Tabs.Count);
        for (int i = 0; i < s.Tabs.Count; i++)
        {
            Assert.Equal(s.Tabs[i] with { Channels = ChatDeckRule.AnyChannel }, back.Tabs[i] with { Channels = ChatDeckRule.AnyChannel });
            Assert.True(s.Tabs[i].Channels.SetEquals(back.Tabs[i].Channels));
        }
        Assert.Equal(s.Rules.Count, back.Rules.Count);
        for (int i = 0; i < s.Rules.Count; i++)
        {
            Assert.Equal(s.Rules[i] with { Channels = ChatDeckRule.AnyChannel }, back.Rules[i] with { Channels = ChatDeckRule.AnyChannel });
            Assert.True(s.Rules[i].Channels.SetEquals(back.Rules[i].Channels));
        }
        var noColours = new Dictionary<ChatDeckChannel, uint>();   // one instance: records compare dictionaries by reference
        Assert.Equal(s.Appearance with { ChannelColours = noColours }, back.Appearance with { ChannelColours = noColours });
        Assert.Equal(s.Appearance.ChannelColours, back.Appearance.ChannelColours);
        Assert.Equal(s.Layout, back.Layout);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Missing_GivesDefaultsWithoutError(string? json)
    {
        var (s, error) = ChatDeckSettingsJson.Parse(json);
        Assert.Null(error);
        Assert.Equal(ChatDeckDefaults.Create().Tabs.Select(t => t.Id), s.Tabs.Select(t => t.Id));
    }

    [Theory]
    [InlineData("{ not json")]
    [InlineData("[1,2]")]
    public void Bad_GivesDefaultsWithError(string json)
    {
        var (s, error) = ChatDeckSettingsJson.Parse(json);
        Assert.NotNull(error);
        Assert.Equal(6, s.Tabs.Count);
    }

    [Fact]
    public void TooLarge_GivesDefaultsWithError()
    {
        string json = "{\"pad\":\"" + new string('x', ChatDeckLimits.MaxSettingsJsonLength) + "\"}";
        Assert.NotNull(ChatDeckSettingsJson.Parse(json).Error);
    }

    [Fact]
    public void UnknownKeys_Survive()
    {
        string previous = ChatDeckSettingsJson.Serialize(Sample(), null);
        var node = JsonNode.Parse(previous)!.AsObject();
        node["future"] = 1;
        node["tabs"]![1]!["future"] = "t";
        node["rules"]![1]!["future"] = "r";
        node["appearance"]!["future"] = true;
        node["layout"]!["future"] = 2;

        var (s, _) = ChatDeckSettingsJson.Parse(node.ToJsonString());
        var again = JsonNode.Parse(ChatDeckSettingsJson.Serialize(s, node.ToJsonString()))!.AsObject();

        Assert.Equal(1, (int)again["future"]!);
        Assert.Equal("t", (string)again["tabs"]![1]!["future"]!);
        Assert.Equal("r", (string)again["rules"]![1]!["future"]!);
        Assert.True((bool)again["appearance"]!["future"]!);
        Assert.Equal(2, (int)again["layout"]!["future"]!);
    }

    [Fact]
    public void UnknownEnums_DroppedOrDefaulted_VersionKept()
    {
        string json = """
        { "version": 3,
          "tabs": [ { "id": "all", "name": "All" },
                    { "id": "x", "name": "X", "icon": "Dragon", "channels": ["Say", "Telepathy"], "send": "Shout" } ],
          "rules": [], "layout": { "edge": "Top" } }
        """;
        var (s, error) = ChatDeckSettingsJson.Parse(json);
        Assert.Null(error);
        Assert.Equal(3, s.Version);
        ChatDeckTab x = s.Tabs.Single(t => t.Id == "x");
        Assert.Equal(ChatDeckIcon.All, x.Icon);
        Assert.True(x.Channels.SetEquals([ChatDeckChannel.Say]));
        Assert.Equal(ChatDeckSendChannel.Say, x.SendChannel);
        Assert.Equal(ChatDeckEdge.Left, s.Layout.Edge);
    }
}
```

- [ ] **Step 2: Run the Verify command** → compile failure.

- [ ] **Step 3: Implement `ChatDeckSettingsJson.cs`**

```csharp
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace AcDream.Core.Chat.Deck;

/// <summary>
/// The Chat Deck's saved settings as the <c>chatDeck</c> JSON object. Keys this build does
/// not know are carried over from the previous object, so an older or newer build loses nothing.
/// </summary>
public static class ChatDeckSettingsJson
{
    /// <summary>The settings to use, and why they are defaults when the saved ones could not be read.</summary>
    public readonly record struct ParseResult(ChatDeckSettings Settings, string? Error);

    public static ParseResult Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new(ChatDeckDefaults.Create(), null);
        if (json.Length > ChatDeckLimits.MaxSettingsJsonLength)
            return new(ChatDeckDefaults.Create(), $"Chat Deck settings are larger than {ChatDeckLimits.MaxSettingsJsonLength} characters.");

        JsonObject root;
        try
        {
            if (JsonNode.Parse(json) is not JsonObject obj)
                return new(ChatDeckDefaults.Create(), "Chat Deck settings are not a JSON object.");
            root = obj;
        }
        catch (JsonException ex)
        {
            return new(ChatDeckDefaults.Create(), $"Chat Deck settings are not valid JSON: {ex.Message}");
        }

        ChatDeckSettings defaults = ChatDeckDefaults.Create();
        var settings = new ChatDeckSettings
        {
            Version = Int(root, "version", ChatDeckSettings.CurrentVersion),
            Tabs = root["tabs"] is JsonArray tabs ? tabs.OfType<JsonObject>().Select(ReadTab).ToList() : defaults.Tabs,
            Rules = root["rules"] is JsonArray rules ? rules.OfType<JsonObject>().Select(ReadRule).ToList() : defaults.Rules,
            Appearance = root["appearance"] is JsonObject a ? ReadAppearance(a) : defaults.Appearance,
            Layout = root["layout"] is JsonObject l ? ReadLayout(l) : defaults.Layout,
        };
        return new(ChatDeckSettingsNormalizer.Normalize(settings), null);
    }

    public static string Serialize(ChatDeckSettings settings, string? previousJson)
    {
        JsonObject previous = TryParseObject(previousJson) ?? new JsonObject();
        JsonObject root = (JsonObject)previous.DeepClone();

        root["version"] = settings.Version;
        root["tabs"] = WriteArray(settings.Tabs, previous["tabs"] as JsonArray, t => t.Id, WriteTab);
        root["rules"] = WriteArray(settings.Rules, previous["rules"] as JsonArray, r => r.Id, WriteRule);
        root["appearance"] = WriteAppearance(settings.Appearance, Clone(previous["appearance"] as JsonObject));
        root["layout"] = WriteLayout(settings.Layout, Clone(previous["layout"] as JsonObject));
        return root.ToJsonString();
    }

    // ── reading ──────────────────────────────────────────────────────────────

    private static ChatDeckTab ReadTab(JsonObject o) => new()
    {
        Id = Str(o, "id", ""),
        Name = Str(o, "name", "Tab"),
        Icon = Enum(o, "icon", ChatDeckIcon.All),
        Channels = Channels(o, "channels"),
        SendChannel = Enum(o, "send", ChatDeckSendChannel.Say),
        Peek = Bool(o, "peek", false),
    };

    private static ChatDeckRule ReadRule(JsonObject o) => new()
    {
        Id = Str(o, "id", ""),
        Enabled = Bool(o, "enabled", true),
        Kind = Enum(o, "kind", ChatDeckMatchKind.Contains),
        Pattern = Str(o, "pattern", ""),
        PresetId = Str(o, "presetId", ""),
        Field = Enum(o, "field", ChatDeckMatchField.Text),
        Channels = Channels(o, "channels"),
        Action = Enum(o, "action", ChatDeckRuleAction.Hide),
        TargetTabId = Str(o, "target", ""),
        Colour = Colour(o["colour"]) ?? 0u,
        Alert = Bool(o, "alert", false),
        Faulted = Bool(o, "faulted", false),
    };

    private static ChatDeckAppearance ReadAppearance(JsonObject o)
    {
        var d = new ChatDeckAppearance();
        var colours = new Dictionary<ChatDeckChannel, uint>();
        if (o["channelColours"] is JsonObject map)
            foreach (var (key, value) in map)
                if (System.Enum.TryParse(key, out ChatDeckChannel channel) && System.Enum.IsDefined(channel)
                    && Colour(value) is { } c)
                    colours[channel] = c;
        return new ChatDeckAppearance
        {
            FontSize = Int(o, "fontSize", d.FontSize),
            InputFontSize = Int(o, "inputFontSize", d.InputFontSize),
            LineSpacing = Float(o, "lineSpacing", d.LineSpacing),
            WrapIndent = Bool(o, "wrapIndent", d.WrapIndent),
            BackgroundOpacity = Float(o, "backgroundOpacity", d.BackgroundOpacity),
            Timestamps = Enum(o, "timestamps", d.Timestamps),
            ChannelPrefix = Bool(o, "channelPrefix", d.ChannelPrefix),
            ChannelColours = colours,
            FadeWhenIdle = Bool(o, "fadeWhenIdle", d.FadeWhenIdle),
            FadeAfterSeconds = Int(o, "fadeAfterSeconds", d.FadeAfterSeconds),
            BeepOnAlert = Bool(o, "beepOnAlert", d.BeepOnAlert),
        };
    }

    private static ChatDeckLayoutState ReadLayout(JsonObject o)
    {
        var d = new ChatDeckLayoutState();
        return new ChatDeckLayoutState
        {
            Pinned = Bool(o, "pinned", d.Pinned),
            Edge = Enum(o, "edge", d.Edge),
            LastTabId = Str(o, "lastTab", d.LastTabId),
        };
    }

    // ── writing ──────────────────────────────────────────────────────────────

    private static JsonArray WriteArray<T>(
        IReadOnlyList<T> items, JsonArray? previous, Func<T, string> id, Func<T, JsonObject, JsonObject> write)
    {
        var byId = new Dictionary<string, JsonObject>(StringComparer.Ordinal);
        if (previous is not null)
            foreach (JsonObject o in previous.OfType<JsonObject>())
                if (o["id"] is JsonValue v && v.TryGetValue(out string? key) && key is not null)
                    byId.TryAdd(key, o);
        var array = new JsonArray();
        foreach (T item in items)
            array.Add(write(item, byId.TryGetValue(id(item), out var old) ? (JsonObject)old.DeepClone() : new JsonObject()));
        return array;
    }

    private static JsonObject WriteTab(ChatDeckTab t, JsonObject o)
    {
        o["id"] = t.Id;
        o["name"] = t.Name;
        o["icon"] = t.Icon.ToString();
        o["channels"] = ChannelArray(t.Channels);
        o["send"] = t.SendChannel.ToString();
        o["peek"] = t.Peek;
        return o;
    }

    private static JsonObject WriteRule(ChatDeckRule r, JsonObject o)
    {
        o["id"] = r.Id;
        o["enabled"] = r.Enabled;
        o["kind"] = r.Kind.ToString();
        o["pattern"] = r.Pattern;
        o["presetId"] = r.PresetId;
        o["field"] = r.Field.ToString();
        o["channels"] = ChannelArray(r.Channels);
        o["action"] = r.Action.ToString();
        o["target"] = r.TargetTabId;
        o["colour"] = ColourText(r.Colour);
        o["alert"] = r.Alert;
        o["faulted"] = r.Faulted;
        return o;
    }

    private static JsonObject WriteAppearance(ChatDeckAppearance a, JsonObject o)
    {
        o["fontSize"] = a.FontSize;
        o["inputFontSize"] = a.InputFontSize;
        o["lineSpacing"] = a.LineSpacing;
        o["wrapIndent"] = a.WrapIndent;
        o["backgroundOpacity"] = a.BackgroundOpacity;
        o["timestamps"] = a.Timestamps.ToString();
        o["channelPrefix"] = a.ChannelPrefix;
        var colours = new JsonObject();
        foreach (var (channel, colour) in a.ChannelColours.OrderBy(kv => kv.Key))
            colours[channel.ToString()] = ColourText(colour);
        o["channelColours"] = colours;
        o["fadeWhenIdle"] = a.FadeWhenIdle;
        o["fadeAfterSeconds"] = a.FadeAfterSeconds;
        o["beepOnAlert"] = a.BeepOnAlert;
        return o;
    }

    private static JsonObject WriteLayout(ChatDeckLayoutState l, JsonObject o)
    {
        o["pinned"] = l.Pinned;
        o["edge"] = l.Edge.ToString();
        o["lastTab"] = l.LastTabId;
        return o;
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    private static JsonObject? TryParseObject(string? json)
    {
        if (string.IsNullOrWhiteSpace(json) || json.Length > ChatDeckLimits.MaxSettingsJsonLength) return null;
        try { return JsonNode.Parse(json) as JsonObject; }
        catch (JsonException) { return null; }
    }

    private static JsonObject Clone(JsonObject? o) => o is null ? new JsonObject() : (JsonObject)o.DeepClone();

    private static JsonArray ChannelArray(IReadOnlySet<ChatDeckChannel> channels) =>
        new(channels.OrderBy(c => c).Select(c => (JsonNode)JsonValue.Create(c.ToString())).ToArray());

    private static IReadOnlySet<ChatDeckChannel> Channels(JsonObject o, string key)
    {
        var set = new HashSet<ChatDeckChannel>();
        if (o[key] is JsonArray array)
            foreach (JsonNode? n in array)
                if (n is JsonValue v && v.TryGetValue(out string? s)
                    && System.Enum.TryParse(s, out ChatDeckChannel c) && System.Enum.IsDefined(c))
                    set.Add(c);
        return set;
    }

    private static string Str(JsonObject o, string key, string fallback) =>
        o[key] is JsonValue v && v.TryGetValue(out string? s) && s is not null ? s : fallback;

    private static bool Bool(JsonObject o, string key, bool fallback) =>
        o[key] is JsonValue v && v.TryGetValue(out bool b) ? b : fallback;

    private static int Int(JsonObject o, string key, int fallback) =>
        o[key] is JsonValue v && v.TryGetValue(out int i) ? i : fallback;

    private static float Float(JsonObject o, string key, float fallback) =>
        o[key] is JsonValue v && v.TryGetValue(out double d) ? (float)d : fallback;

    private static TEnum Enum<TEnum>(JsonObject o, string key, TEnum fallback) where TEnum : struct, System.Enum =>
        o[key] is JsonValue v && v.TryGetValue(out string? s)
            && System.Enum.TryParse(s, out TEnum value) && System.Enum.IsDefined(value)
            ? value
            : fallback;

    private static string ColourText(uint argb) => "#" + argb.ToString("X8", CultureInfo.InvariantCulture);

    private static uint? Colour(JsonNode? node) =>
        node is JsonValue v && v.TryGetValue(out string? s) && s is { Length: 9 } && s[0] == '#'
            && uint.TryParse(s.AsSpan(1), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint argb)
            ? argb
            : null;
}
```

`System.Enum` is written in full inside this file because the class has a private `Enum<TEnum>` helper.

- [ ] **Step 4: Run the Verify command** → PASS. Floating point: `lineSpacing` round-trips through `double`; if `RoundTrip` fails on `1.4f`, compare with `Assert.Equal(expected, actual, precision)` only for the float fields, never change the stored type.

- [ ] **Step 5: Commit**

```bash
git add src/AcDream.Core/Chat/Deck/ChatDeckSettingsJson.cs tests/AcDream.Core.Tests/Chat/Deck/ChatDeckSettingsJsonTests.cs
git commit -m "Chat Deck: settings JSON that keeps unknown keys

Claude-Session: https://claude.ai/code/session_01WFS4EHSNafq2P1qfUuh258"
```

---

### Task 9: The session model

**Goal:** `ChatDeckModel`: subscribes to a `ChatLog`, queues entries from any thread, applies them on `Tick()` into tab states with read state, handles `/clear`, settings changes, tab selection, faulted rules, and disposal.

**Files:**
- Create: `src/AcDream.Core/Chat/Deck/ChatDeckModel.cs`
- Test: `tests/AcDream.Core.Tests/Chat/Deck/ChatDeckModelTests.cs`

**Acceptance Criteria:**
- [ ] Entries appended (from any thread) appear in tabs only after `Tick()`; `LinesDelivered` fires once per tick with every delivered line and its tab ids.
- [ ] Read state: a line into the selected tab while `SetView(visible: true, atBottom: true)` is read; while hidden or scrolled up it is unread; `SelectTab` and `SetView(true, true)` clear the selected tab; `MarkAllRead` clears all.
- [ ] `ChatLog.Clear()` empties every tab and its unread state on the next `Tick()` and raises `Cleared`; ordering with entries appended before and after the clear is kept.
- [ ] `ApplySettings` keeps history of tabs that still exist, starts new tabs empty, drops removed ones, and falls back to All when the selected tab is gone.
- [ ] A rule that times out is set `Faulted` in `Settings`, stops running, and `SettingsChanged` is raised once for that tick.
- [ ] `SelectTab` updates `Settings.Layout.LastTabId` and raises `SettingsChanged`.
- [ ] After `Dispose()` (twice is fine) new entries are ignored and `Tick()` does nothing.
- [ ] `SendChannelFor` returns the override if given, else the tab's send channel, else Say.

**Verify:** `MAC_TEST_FILTER='FullyQualifiedName~ChatDeckModel' ~/code/OpenAC/test-baseline/run-mac-tests.sh ~/code/OpenAC/OpenAC-chat-deck-model tests/AcDream.Core.Tests/AcDream.Core.Tests.csproj` → all pass

**Steps:**

- [ ] **Step 1: Write the failing tests**

```csharp
using AcDream.Core.Chat;
using AcDream.Core.Chat.Deck;

namespace AcDream.Core.Tests.Chat.Deck;

public sealed class ChatDeckModelTests
{
    private static (ChatLog Log, ChatDeckModel Model) Create(ChatDeckSettings? settings = null)
    {
        var log = new ChatLog();
        return (log, new ChatDeckModel(log, settings ?? ChatDeckDefaults.Create(), "Davian"));
    }

    private static void Say(ChatLog log, string text, string sender = "Bob") =>
        log.OnLocalSpeech(sender, text, 0x50000002u, isRanged: false, logTextType: 0x02u);

    [Fact]
    public void Entries_AppearOnTick_FromAnyThread()
    {
        var (log, model) = Create();
        var delivered = new List<ChatDeckDelivery>();
        model.LinesDelivered += batch => delivered.AddRange(batch);

        Task.Run(() => Say(log, "hello")).Wait();
        Assert.Equal(0, model.FindTab("local")!.Count);

        model.Tick();
        Assert.Equal(1, model.FindTab("local")!.Count);
        Assert.Equal(1, model.FindTab("all")!.Count);
        Assert.Same(model.FindTab("local")![0], model.FindTab("all")![0]);
        ChatDeckDelivery d = Assert.Single(delivered);
        Assert.Equal(["all", "local"], d.TabIds);
    }

    [Fact]
    public void ReadState_FollowsSelectionAndView()
    {
        var (log, model) = Create();
        model.SelectTab("local");
        model.SetView(visible: true, atBottom: true);
        Say(log, "seen");
        model.Tick();
        Assert.Equal(0, model.FindTab("local")!.Unread);
        Assert.Equal(1, model.FindTab("all")!.Unread);

        model.SetView(visible: true, atBottom: false);
        Say(log, "scrolled up");
        model.Tick();
        Assert.Equal(1, model.FindTab("local")!.Unread);

        model.SetView(visible: true, atBottom: true);
        Assert.Equal(0, model.FindTab("local")!.Unread);

        model.SetView(visible: false, atBottom: true);
        Say(log, "hidden");
        model.Tick();
        Assert.Equal(1, model.FindTab("local")!.Unread);

        model.SelectTab("all");
        Assert.Equal(0, model.FindTab("all")!.Unread);
        model.MarkAllRead();
        Assert.All(model.Tabs, t => Assert.Equal(0, t.Unread));
    }

    [Fact]
    public void IncomingTell_SetsAlert()
    {
        var (log, model) = Create();
        log.OnTellReceived("Bob", "psst", 0x50000002u, 0x03u);
        model.Tick();
        Assert.True(model.FindTab("local")!.Alert);
    }

    [Fact]
    public void Clear_EmptiesTabsInOrder()
    {
        var (log, model) = Create();
        int cleared = 0;
        model.Cleared += () => cleared++;
        Say(log, "before");
        log.Clear();
        Say(log, "after");
        model.Tick();

        Assert.Equal(1, cleared);
        ChatDeckTabState local = model.FindTab("local")!;
        Assert.Equal(1, local.Count);
        Assert.Equal("after", local[0].Entry.Text);
    }

    [Fact]
    public void ApplySettings_KeepsSurvivingHistory()
    {
        var (log, model) = Create();
        model.SelectTab("combat");
        Say(log, "hello");
        model.Tick();

        var s = model.Settings;
        var newTab = new ChatDeckTab { Id = "loot", Name = "Loot", Channels = new HashSet<ChatDeckChannel> { ChatDeckChannel.Say } };
        model.ApplySettings(s with { Tabs = [.. s.Tabs.Where(t => t.Id != "combat"), newTab] });

        Assert.Equal(1, model.FindTab("local")!.Count);
        Assert.Equal(0, model.FindTab("loot")!.Count);
        Assert.Null(model.FindTab("combat"));
        Assert.Equal("all", model.SelectedTabId);

        Say(log, "again");
        model.Tick();
        Assert.Equal(1, model.FindTab("loot")!.Count);
    }

    [Fact]
    public void TimedOutRule_IsFaultedAndReported()
    {
        var d = ChatDeckDefaults.Create();
        var slow = new ChatDeckRule { Id = "slow", Kind = ChatDeckMatchKind.Regex, Pattern = @"^(\w+\s?)*\1$", Action = ChatDeckRuleAction.Hide };
        var (log, model) = Create(d with { Rules = [.. d.Rules, slow] });
        var changes = new List<ChatDeckSettings>();
        model.SettingsChanged += changes.Add;

        Say(log, new string('a', 1990) + "!");
        Say(log, new string('a', 1990) + "?");
        model.Tick();

        Assert.True(model.Settings.Rules.Single(r => r.Id == "slow").Faulted);
        Assert.Single(changes);
        Assert.Equal(2, model.FindTab("local")!.Count);
    }

    [Fact]
    public void SelectTab_RecordsLastTab()
    {
        var (_, model) = Create();
        ChatDeckSettings? changed = null;
        model.SettingsChanged += s => changed = s;
        model.SelectTab("group");
        Assert.Equal("group", model.Settings.Layout.LastTabId);
        Assert.Equal("group", changed!.Layout.LastTabId);
    }

    [Fact]
    public void StartsOnLastTab()
    {
        var d = ChatDeckDefaults.Create();
        var (_, model) = Create(d with { Layout = d.Layout with { LastTabId = "social" } });
        Assert.Equal("social", model.SelectedTabId);
    }

    [Fact]
    public void Dispose_StopsListening()
    {
        var (log, model) = Create();
        model.Dispose();
        model.Dispose();
        Say(log, "late");
        model.Tick();
        Assert.Equal(0, model.FindTab("all")!.Count);
    }

    [Fact]
    public void SendChannel_OverrideThenTabThenSay()
    {
        var (_, model) = Create();
        Assert.Equal(ChatDeckSendChannel.Fellowship, model.SendChannelFor("group", null));
        Assert.Equal(ChatDeckSendChannel.Trade, model.SendChannelFor("group", ChatDeckSendChannel.Trade));
        Assert.Equal(ChatDeckSendChannel.Say, model.SendChannelFor("missing", null));
    }

    [Fact]
    public void Test_EvaluatesWithoutSideEffects()
    {
        var (_, model) = Create();
        var verdict = model.Test(ChatDeckMatcherTests.Facts("Davian, heal"));
        Assert.True(verdict.Alert);
        Assert.Equal(0, model.FindTab("all")!.Count);
    }
}
```

- [ ] **Step 2: Run the Verify command** → compile failure.

- [ ] **Step 3: Implement `ChatDeckModel.cs`**

```csharp
using System.Collections.Concurrent;

namespace AcDream.Core.Chat.Deck;

/// <summary>A line delivered on one tick and the tabs it went to.</summary>
public sealed record ChatDeckDelivery(ChatDeckLine Line, IReadOnlyList<string> TabIds);

/// <summary>
/// The Chat Deck for one game session. Lines may arrive on any thread; they are queued
/// and applied by <see cref="Tick"/>, which, like every other member, runs on the game thread.
/// </summary>
public sealed class ChatDeckModel : IDisposable
{
    private readonly ChatLog _log;
    private readonly string _characterName;
    private readonly ConcurrentQueue<ChatEntry?> _queue = new();   // null marks a /clear
    private readonly List<ChatDeckTabState> _tabs = [];
    private ChatDeckRuleEngine _engine;
    private bool _disposed;

    public ChatDeckModel(ChatLog log, ChatDeckSettings settings, string characterName)
    {
        _log = log;
        _characterName = characterName;
        Settings = ChatDeckSettingsNormalizer.Normalize(settings);
        _engine = new ChatDeckRuleEngine(Settings.Rules, Settings.Tabs, _characterName);
        RebuildTabs();
        SelectedTabId = Settings.Layout.LastTabId;
        _log.EntryAppended += OnEntryAppended;
        _log.Cleared += OnCleared;
    }

    public ChatDeckSettings Settings { get; private set; }
    public IReadOnlyList<ChatDeckTabState> Tabs => _tabs;
    public string SelectedTabId { get; private set; }
    public bool ViewVisible { get; private set; }
    public bool ViewAtBottom { get; private set; } = true;

    /// <summary>Lines applied by one <see cref="Tick"/>; not raised when there were none.</summary>
    public event Action<IReadOnlyList<ChatDeckDelivery>>? LinesDelivered;
    /// <summary>The chat log was cleared and every tab emptied.</summary>
    public event Action? Cleared;
    /// <summary>The model changed its own settings (a rule faulted, the selected tab changed); save them.</summary>
    public event Action<ChatDeckSettings>? SettingsChanged;

    public ChatDeckTabState? FindTab(string tabId) => _tabs.FirstOrDefault(t => t.TabId == tabId);

    public void Tick()
    {
        if (_disposed) return;
        List<ChatDeckDelivery>? delivered = null;
        bool settingsChanged = false;

        while (_queue.TryDequeue(out ChatEntry? item))
        {
            if (item is not { } entry)
            {
                foreach (ChatDeckTabState tab in _tabs) tab.Clear();
                Cleared?.Invoke();
                continue;
            }

            ChatDeckLineFacts facts = ChatDeckLineFacts.From(entry);
            ChatDeckVerdict verdict = _engine.Evaluate(facts);
            if (verdict.TimedOutRuleIds.Count > 0)
            {
                // Turn the rule off at once, so the next line in this tick does not wait on it too.
                var ids = verdict.TimedOutRuleIds.ToHashSet(StringComparer.Ordinal);
                Settings = Settings with { Rules = MarkFaulted(Settings.Rules, ids) };
                _engine = new ChatDeckRuleEngine(Settings.Rules, Settings.Tabs, _characterName);
                settingsChanged = true;
            }
            if (verdict.Hidden) continue;

            var line = new ChatDeckLine(facts, verdict);
            foreach (string tabId in verdict.Destinations)
                FindTab(tabId)?.Add(line, seen: IsSeen(tabId));
            (delivered ??= []).Add(new ChatDeckDelivery(line, verdict.Destinations));
        }

        if (settingsChanged) SettingsChanged?.Invoke(Settings);
        if (delivered is not null) LinesDelivered?.Invoke(delivered);
    }

    public void ApplySettings(ChatDeckSettings settings)
    {
        Settings = ChatDeckSettingsNormalizer.Normalize(settings);
        _engine = new ChatDeckRuleEngine(Settings.Rules, Settings.Tabs, _characterName);
        RebuildTabs();
        if (FindTab(SelectedTabId) is null) SelectedTabId = ChatDeckDefaults.AllTabId;
    }

    public void SelectTab(string tabId)
    {
        if (FindTab(tabId) is not { } tab) return;
        SelectedTabId = tabId;
        tab.MarkRead();
        if (Settings.Layout.LastTabId == tabId) return;
        Settings = Settings with { Layout = Settings.Layout with { LastTabId = tabId } };
        SettingsChanged?.Invoke(Settings);
    }

    /// <summary>Whether the window shows the transcript (pinned or flown out) and whether it is scrolled to the newest line.</summary>
    public void SetView(bool visible, bool atBottom)
    {
        ViewVisible = visible;
        ViewAtBottom = atBottom;
        if (visible && atBottom) FindTab(SelectedTabId)?.MarkRead();
    }

    public void MarkAllRead()
    {
        foreach (ChatDeckTabState tab in _tabs) tab.MarkRead();
    }

    /// <summary>The verdict a line would get, for the editor's test box. Changes nothing.</summary>
    public ChatDeckVerdict Test(ChatDeckLineFacts facts) => _engine.Evaluate(facts);

    public ChatDeckSendChannel SendChannelFor(string tabId, ChatDeckSendChannel? overrideChannel) =>
        overrideChannel
        ?? Settings.Tabs.FirstOrDefault(t => t.Id == tabId)?.SendChannel
        ?? ChatDeckSendChannel.Say;

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _log.EntryAppended -= OnEntryAppended;
        _log.Cleared -= OnCleared;
        _queue.Clear();
    }

    private void OnEntryAppended(ChatEntry entry)
    {
        if (!_disposed) _queue.Enqueue(entry);
    }

    private void OnCleared()
    {
        if (!_disposed) _queue.Enqueue(null);
    }

    private bool IsSeen(string tabId) => tabId == SelectedTabId && ViewVisible && ViewAtBottom;

    private void RebuildTabs()
    {
        var old = _tabs.ToDictionary(t => t.TabId, StringComparer.Ordinal);
        _tabs.Clear();
        foreach (ChatDeckTab tab in Settings.Tabs)
            _tabs.Add(old.TryGetValue(tab.Id, out var kept) ? kept : new ChatDeckTabState(tab.Id));
    }

    private static IReadOnlyList<ChatDeckRule> MarkFaulted(IReadOnlyList<ChatDeckRule> rules, HashSet<string> ids) =>
        rules.Select(r => ids.Contains(r.Id) ? r with { Faulted = true } : r).ToList();
}
```

- [ ] **Step 4: Run the Verify command** → PASS.

- [ ] **Step 5: Commit**

```bash
git add src/AcDream.Core/Chat/Deck/ChatDeckModel.cs tests/AcDream.Core.Tests/Chat/Deck/ChatDeckModelTests.cs
git commit -m "Chat Deck: session model

Claude-Session: https://claude.ai/code/session_01WFS4EHSNafq2P1qfUuh258"
```

---

### Task 10: Full suite and wrap-up

**Goal:** Prove nothing else broke, then hand the branch to the user for review and merge.

**Files:** none new.

**Acceptance Criteria:**
- [ ] The full Mac suite (`run-mac-tests.sh` with no filter) exits 0 on `feat/chat-deck-model`.
- [ ] `git grep -n "copy verbatim" src/` prints nothing.
- [ ] No file outside `src/AcDream.Core/Chat/`, `tests/AcDream.Core.Tests/Chat/` and `NOTICE.md` changed (`git diff --stat main...HEAD`).

**Verify:** `~/code/OpenAC/test-baseline/run-mac-tests.sh ~/code/OpenAC/OpenAC-chat-deck-model` → exit code 0

**Steps:**

- [ ] **Step 1:** Run the Verify command. On a failure, run that test alone three times; if it passes 3/3 alone and is unrelated to Chat Deck, it is load-flaky — report it, do not change the runner without the user. A Chat Deck failure is fixed in the task that owns it.
- [ ] **Step 2:** Run the two `git` checks from the acceptance criteria.
- [ ] **Step 3:** Use superpowers-extended-cc:finishing-a-development-branch. The user decides whether to merge into `main` and `rynth` now (the code is unused until plan 2) or hold until plan 2. Do not push without the user saying so.
