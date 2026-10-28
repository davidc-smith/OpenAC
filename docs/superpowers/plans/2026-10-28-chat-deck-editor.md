# Chat Deck Filters & Tabs Editor Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers-extended-cc:subagent-driven-development (recommended) or superpowers-extended-cc:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Plan 5 of 5 for the Chat Deck. Add the Filters & Tabs editor (Tabs page, Rules page with the rule form, standard filters and a test box), rule-from-line from the transcript's right-click menu, the ⧩ stud, the `+` tab button, the full tab menu (Rename, Edit tab…, Delete), and settle the two search items carried from plans 2 and 4.

**Architecture:** Every settings change the editor makes is a pure function in Core (`ChatDeckSettingsEdits`), applied through one new model method, `ChatDeckModel.Edit`, which normalizes, rebuilds the rules and raises `SettingsChanged`. `ChatDeckSession` already saves on that event, with 0.5 s coalescing. Rule-from-line, the match preview, the rule summaries, the test line and the verdict wording are also pure Core classes. The editor is one markup window built with `MarkupDocument.Build` from a long-lived binding object, `ChatDeckEditorBinding`. It is hosted, drawn and docked like the plan 4 settings popup (`PluginAppearanceWindow`, `ChatDeckSettingsBinding.Prepare`, `ChatDeckSettingsPlacement.Dock`). The deck asks the runtime to open it with a `ChatDeckEditorRequest`, which names the page, the tab, a new rule or a test line.

**Tech Stack:** C# / .NET 10, xUnit, the OpenAC retained UI (`UiElement`, `UiText`, `UiField`, `UiMarkupContextMenu`), plugin markup (`MarkupDocument`, `theme="plugin"`, `<repeat>`, `<list>` columns, `<contextmenu>`).

**Spec:** `docs/superpowers/specs/2026-10-26-chat-deck-design.md` (branch `docs/chat-deck-spec`). The relevant sections are "Filters & Tabs editor", "Window → Pinned layout" (the tab menu, `+`, the line right-click menu, rule-from-line) and "Settings and persistence" (a deleted tab's rules). Plans 1–4 are merged on main.

## Global Constraints

- Fork-only, client-only. **No plugin contract change** (`ForkContractVersion` stays `0.1.30-fork.2`). The editor uses markup internally; no plugin API changes.
- **The retail chat must not change.** `UiText` only gains two `internal` read-only helpers (`HitCharAt`, `TryGetSelection`). Every existing `UiText*` test passes unchanged.
- **Modern HUD windows look like the decks, never like the plugin theme** (user, plan 4). The editor is built with the same fixed look as the settings popup: `PluginUiThemeSettings.WithFixedLook(ChatDeckPalette.Window, body, title)` with the deck fonts, framed by `ChatDeckSettingsBinding.Prepare`.
- The deck draws with the bundled sans fonts (`DeckFonts`), never the monospace debug font.
- Limits already in Core apply: 16 tabs, 200 rules, tab names of 24 characters, patterns of 256 characters (`ChatDeckLimits`, `ChatDeckMatcher.MaxPatternLength`). An edit that would break a limit changes nothing.
- The settings JSON stays `version: 1`. Saving still goes through `ChatDeckSettingsJson.Serialize` into a clone of the previous object, so unknown fields survive.
- Worktree: `~/code/OpenAC/OpenAC-chat-deck-editor` on a new branch `feat/chat-deck-editor` from **`origin/main`**. Local `main` is behind: origin has `9995975f` (house hook fix) on top of `b729ca05`. Run `git fetch` first. Do not work in the main checkout.
- .NET SDK: `~/.dotnet/dotnet` (put `~/.dotnet` first on `PATH`). Run each task's tests with its filtered `dotnet test` command. For the full suite, use only `~/code/OpenAC/test-baseline/run-mac-tests.sh <worktree>`. Its second pass is `--no-build` and can print "Passed" from stale DLLs after a compile error, so trust the exit code.
- Commit messages end with the session attribution line the controller supplies.
- Never launch the game client yourself. The live check (Task 12) waits for the user to say they are available, and uses the local ACE server only.

**User decisions (already made):**
- "Modern HUD windows must look like the decks, never follow the plugin theme" (plan 4). This applies to the editor too.
- The window has no pin stud and it "should not come back" (plan 3). The studs are search ⌕, Filters & Tabs ⧩, Settings ⚙ and collapse.
- The tab keys stay as they are: Ctrl+Tab / Ctrl+Shift+Tab, Option+Cmd+Left/Right, plus the optional extra keys (plan 4).

**Design choices made in this plan** (stated so a reviewer can check them against the spec):
- **When changes apply.** Tab edits, rule on/off, reordering, delete and preset checkboxes apply at once, as the settings popup does. A tab name applies on Enter or when the field loses focus. Rule *contents* are edited in a form and apply only on **Add** / **Save**, as the spec requires for rule-from-line. This also keeps regexes from being rebuilt on every keystroke; only the form's own draft is compiled, for its preview.
- **Confirmation** ("Reset to defaults (asks first)", Delete tab) is a confirm row inside the editor, not a modal dialog. `PluginDialogHost` only exists when plugins mount, so a modal could not be relied on. The tab menu's *Delete* opens the editor on that tab with the confirm row already showing.
- **Reset to defaults** resets the tabs only. Rules stay; a rule whose target tab is gone is disabled by the normalizer and labelled "(tab deleted)".
- **All tab.** It cannot be deleted or moved and always stays first. Its channels are not editable: the normalizer now forces them to every channel (a plan 1 deferral). The Tabs page shows a note instead of channel checkboxes.
- **Tab icons.** Markup lists cannot draw the bundled SVG icons, so the tab list shows names only and the icon is picked from a drop-down of icon names. The deck's strip shows the icon.
- **The ⋮ row menu** is the rules table's right-click menu (Edit, Duplicate, Delete, Move up, Move down), plus Edit, Up and Down buttons under the table. Markup cannot open a menu from a button.
- **Tab menu.** Rename edits the name in place over the tab (a deck field; Enter or focus loss applies it, Esc cancels). Edit tab… opens the editor on that tab.
- **Rule-from-line** builds a `Contains` rule on the message text (`Text` field) limited to the line's channel. Two checkboxes generalise it: *Any name in place of "Bob"* (shown only when the sender's name appears in the text) and *Any number*. Either one turns the rule into an escaped `Regex` with `.+?` for the name and `\d+(?:[.,]\d+)*` for numbers. The form shows the pattern with those parts marked `[any name]` and `[any number]`. When several lines are selected, the rule is built from the line that was right-clicked (or the first selected), and its channels are every selected line's channels. The preview counts matches over the *All* tab's history, which is every line this session that was not hidden. It shows up to 3 samples, newest first, and warns when the pattern matches empty text ("matches every line").
- **The test box** uses the rules as applied (`ChatDeckModel.Test`). A typed line gets real wording from a synthesized `ChatEntry` for the chosen channel, so presets that read the worded line (`[General] Bob says, "…"`) behave as they would live. A line sent from the transcript keeps its real facts until a field is edited.
- **Search and read state** (plan 2 carry-over). While a search narrows the view, the view does not count as "at the bottom" for read state, so new lines stay unread. Closing the search marks them read if the view is at the bottom. The "N new" marker counts only lines the view shows, matching lines while searching.
- **Search reads the shown text** (plan 4 carry-over). With channel names hidden, a search for "General" no longer matches a line whose `[General] ` prefix is not shown.
- **The editor and the settings popup** dock to the same place, so opening one closes the other. Both close when the deck hides or the character changes.

---

## File Structure

| File | Change | Responsibility |
|---|---|---|
| `src/AcDream.Core/Chat/Deck/ChatDeckSettingsEdits.cs` | create | pure tab and rule edits, id generation |
| `src/AcDream.Core/Chat/Deck/ChatDeckSettings.cs` | modify | normalizer: All tab has every channel |
| `src/AcDream.Core/Chat/Deck/ChatDeckModel.cs` | modify | `Edit` |
| `src/AcDream.Core/Chat/Deck/ChatDeckRuleDraft.cs` | create | rule-from-line, generalise, marked preview |
| `src/AcDream.Core/Chat/Deck/ChatDeckRulePreview.cs` | create | count + samples of a draft over history |
| `src/AcDream.Core/Chat/Deck/ChatDeckMatcher.cs` | modify | `MatchesEmpty` |
| `src/AcDream.Core/Chat/Deck/ChatDeckRuleSummary.cs` | create | When / From / Do / status text |
| `src/AcDream.Core/Chat/Deck/ChatDeckTestLine.cs` | create | facts for a typed test line |
| `src/AcDream.Core/Chat/Deck/ChatDeckVerdictText.cs` | create | a verdict in words, rule by rule |
| `src/AcDream.App/UI/Hud/ChatDeckTranscript.cs` | modify | search on shown text; `Sources` / `SourceOf` |
| `src/AcDream.App/UI/Hud/UiChatDeck.cs` | modify | read state while searching, N new by source, tab menu, bindings |
| `src/AcDream.App/UI/Hud/ChatDeckEditorRequest.cs` | create | what the deck asks the editor to show |
| `src/AcDream.App/UI/Hud/ChatDeckChannelPicker.cs` | create | grouped channel checkboxes over one set |
| `src/AcDream.App/UI/Hud/ChatDeckEditorBinding.cs` | create | editor shell: pages, open, markup |
| `src/AcDream.App/UI/Hud/ChatDeckEditorBinding.Tabs.cs` | create | Tabs page |
| `src/AcDream.App/UI/Hud/ChatDeckEditorBinding.Rules.cs` | create | rules table |
| `src/AcDream.App/UI/Hud/ChatDeckEditorBinding.RuleForm.cs` | create | rule form + preview |
| `src/AcDream.App/UI/Hud/ChatDeckEditorBinding.Presets.cs` | create | standard filters checklist |
| `src/AcDream.App/UI/Hud/ChatDeckEditorBinding.Test.cs` | create | test a line |
| `src/AcDream.App/UI/Hud/ChatDeckGeometry.cs` | modify | ⧩ stud, `+` tab button |
| `src/AcDream.App/UI/Hud/UiChatDeck.Draw.cs` | modify | draw ⧩ and `+` |
| `src/AcDream.App/UI/Hud/UiChatDeck.TabEdit.cs` | create | tab menu actions, rename field, `+` |
| `src/AcDream.App/UI/Hud/UiChatDeck.LineMenu.cs` | create | transcript right-click menu |
| `src/AcDream.App/UI/UiText.cs` | modify | `HitCharAt`, `TryGetSelection` |
| `assets/hud/filters.svg` | create | funnel icon |
| `src/AcDream.App/UI/Hud/EmbeddedSvgSpriteCache.cs` | modify | `HudIcons.Filters` |
| `src/AcDream.App/UI/PluginAppearanceWindow.cs` | modify | `Show` |
| `src/AcDream.App/UI/RetailUiRuntime.cs` | modify | editor host, deck bindings, close on hide / model change |
| tests (see each task) | create/modify | |

---

### Task 1: Core — settings edits and `ChatDeckModel.Edit`

**Goal:** Every change the editor makes is a pure, tested function on `ChatDeckSettings`, and the model applies one with `Edit`, which saves through `SettingsChanged`.

**Files:**
- Create: `src/AcDream.Core/Chat/Deck/ChatDeckSettingsEdits.cs`
- Modify: `src/AcDream.Core/Chat/Deck/ChatDeckSettings.cs` (normalizer, All tab)
- Modify: `src/AcDream.Core/Chat/Deck/ChatDeckModel.cs` (`Edit`)
- Test: `tests/AcDream.Core.Tests/Chat/Deck/ChatDeckSettingsEditsTests.cs` (create), `tests/AcDream.Core.Tests/Chat/Deck/ChatDeckModelTests.cs`, `tests/AcDream.Core.Tests/Chat/Deck/ChatDeckSettingsTests.cs`

**Acceptance Criteria:**
- [ ] `AddTab` appends a tab with a fresh id (`tab-1`, `tab-2`, …, skipping ids in use), no channels and send Say; at 16 tabs it changes nothing and gives a null id
- [ ] `UpdateTab` replaces one tab and keeps its id; `DeleteTab` removes it, disables every Move/Copy rule that targets it (keeping `TargetTabId`), and moves `Layout.LastTabId` to All if it pointed there; All cannot be deleted
- [ ] `MoveTab` moves a tab one place; All never moves, and nothing moves above it
- [ ] `ResetTabs` gives the default tabs and leaves the rules as they are
- [ ] `AddRule` appends with a fresh id (`rule-1`, …) and gives that id; at 200 rules it changes nothing. `UpdateRule` replaces the rule's contents, keeps its id and clears `Faulted`. `DuplicateRule` inserts a copy with a fresh id just below. `DeleteRule` and `MoveRule` work as named. `SetRuleEnabled` sets the flag and clears `Faulted` when turning a rule on
- [ ] `SetPresetEnabled(id, true)` adds a `Preset` / `Hide` rule if there is none for that preset, or turns the existing one on; `false` turns it off and keeps its action and colour
- [ ] An edit with an unknown id returns the *same* settings instance
- [ ] `ChatDeckModel.Edit(change)` applies the result (normalized, rules rebuilt), raises `SettingsChanged` once and returns true; a change that returns the same instance raises nothing and returns false
- [ ] The normalizer gives the All tab every channel, whatever was saved

**Verify:** `~/.dotnet/dotnet test tests/AcDream.Core.Tests --filter "FullyQualifiedName~ChatDeck"` → all pass

**Steps:**

- [ ] **Step 1: Write the failing tests.** Create `tests/AcDream.Core.Tests/Chat/Deck/ChatDeckSettingsEditsTests.cs`:

```csharp
using AcDream.Core.Chat.Deck;

namespace AcDream.Core.Tests.Chat.Deck;

public sealed class ChatDeckSettingsEditsTests
{
    private static ChatDeckSettings D => ChatDeckDefaults.Create();

    private static ChatDeckRule Move(string id, string target) => new()
    {
        Id = id, Kind = ChatDeckMatchKind.Contains, Pattern = "x", Action = ChatDeckRuleAction.MoveTo, TargetTabId = target,
    };

    [Fact]
    public void AddTab_FreshId_NoChannels_SaySend()
    {
        ChatDeckSettings s = ChatDeckSettingsEdits.AddTab(D, "Loot", out string? id);
        Assert.Equal("tab-1", id);
        ChatDeckTab tab = s.Tabs[^1];
        Assert.Equal("Loot", tab.Name);
        Assert.Empty(tab.Channels);
        Assert.Equal(ChatDeckSendChannel.Say, tab.SendChannel);
        ChatDeckSettingsEdits.AddTab(s, "More", out string? second);
        Assert.Equal("tab-2", second);
    }

    [Fact]
    public void AddTab_AtLimit_ChangesNothing()
    {
        ChatDeckSettings s = D;
        while (s.Tabs.Count < ChatDeckLimits.MaxTabs) s = ChatDeckSettingsEdits.AddTab(s, "T", out _);
        ChatDeckSettings after = ChatDeckSettingsEdits.AddTab(s, "One too many", out string? id);
        Assert.Same(s, after);
        Assert.Null(id);
    }

    [Fact]
    public void DeleteTab_DisablesItsRules_KeepsTarget_FixesLastTab()
    {
        ChatDeckSettings s = D with
        {
            Rules = [.. D.Rules, Move("r", "combat")],
            Layout = D.Layout with { LastTabId = "combat" },
        };
        ChatDeckSettings after = ChatDeckSettingsEdits.DeleteTab(s, "combat");
        Assert.DoesNotContain(after.Tabs, t => t.Id == "combat");
        ChatDeckRule rule = after.Rules.Single(r => r.Id == "r");
        Assert.False(rule.Enabled);
        Assert.Equal("combat", rule.TargetTabId);
        Assert.Equal(ChatDeckDefaults.AllTabId, after.Layout.LastTabId);
    }

    [Fact]
    public void AllTab_CannotBeDeletedOrMoved()
    {
        ChatDeckSettings s = D;
        Assert.Same(s, ChatDeckSettingsEdits.DeleteTab(s, ChatDeckDefaults.AllTabId));
        Assert.Same(s, ChatDeckSettingsEdits.MoveTab(s, ChatDeckDefaults.AllTabId, 1));
        Assert.Same(s, ChatDeckSettingsEdits.MoveTab(s, "local", -1));   // nothing goes above All
    }

    [Fact]
    public void MoveTab_SwapsNeighbours()
    {
        ChatDeckSettings after = ChatDeckSettingsEdits.MoveTab(D, "local", 1);
        Assert.Equal(["all", "social", "local", "group", "combat", "system"], after.Tabs.Select(t => t.Id));
    }

    [Fact]
    public void ResetTabs_KeepsRules()
    {
        ChatDeckSettings s = ChatDeckSettingsEdits.AddTab(D, "Loot", out _) with { Rules = [.. D.Rules, Move("r", "local")] };
        ChatDeckSettings after = ChatDeckSettingsEdits.ResetTabs(s);
        Assert.Equal(D.Tabs.Select(t => t.Id), after.Tabs.Select(t => t.Id));
        Assert.Equal(s.Rules, after.Rules);
    }

    [Fact]
    public void AddRule_FreshId_AndLimit()
    {
        var draft = new ChatDeckRule { Id = "", Kind = ChatDeckMatchKind.Contains, Pattern = "a" };
        ChatDeckSettings s = ChatDeckSettingsEdits.AddRule(D, draft, out string? id);
        Assert.Equal("rule-1", id);
        Assert.Equal("rule-1", s.Rules[^1].Id);
        while (s.Rules.Count < ChatDeckLimits.MaxRules) s = ChatDeckSettingsEdits.AddRule(s, draft, out _);
        Assert.Same(s, ChatDeckSettingsEdits.AddRule(s, draft, out string? none));
        Assert.Null(none);
    }

    [Fact]
    public void UpdateRule_KeepsId_ClearsFaulted()
    {
        ChatDeckSettings s = D with { Rules = [Move("r", "local") with { Faulted = true }] };
        ChatDeckSettings after = ChatDeckSettingsEdits.UpdateRule(s, "r", Move("ignored", "group"));
        ChatDeckRule rule = Assert.Single(after.Rules);
        Assert.Equal("r", rule.Id);
        Assert.Equal("group", rule.TargetTabId);
        Assert.False(rule.Faulted);
    }

    [Fact]
    public void DuplicateDeleteMove_Rules()
    {
        ChatDeckSettings s = D with { Rules = [Move("a", "local"), Move("b", "group")] };
        ChatDeckSettings dup = ChatDeckSettingsEdits.DuplicateRule(s, "a");
        Assert.Equal(["a", "rule-1", "b"], dup.Rules.Select(r => r.Id));
        Assert.Equal(["b", "a"], ChatDeckSettingsEdits.MoveRule(s, "a", 1).Rules.Select(r => r.Id));
        Assert.Equal(["b"], ChatDeckSettingsEdits.DeleteRule(s, "a").Rules.Select(r => r.Id));
    }

    [Fact]
    public void SetRuleEnabled_On_ClearsFaulted()
    {
        ChatDeckSettings s = D with { Rules = [Move("a", "local") with { Enabled = false, Faulted = true }] };
        ChatDeckRule rule = ChatDeckSettingsEdits.SetRuleEnabled(s, "a", true).Rules[0];
        Assert.True(rule.Enabled);
        Assert.False(rule.Faulted);
    }

    [Fact]
    public void SetPresetEnabled_AddsThenToggles_KeepingAction()
    {
        string id = ChatDeckPresets.All[0].Id;
        ChatDeckSettings on = ChatDeckSettingsEdits.SetPresetEnabled(D, id, true);
        ChatDeckRule rule = on.Rules.Single(r => r.Kind == ChatDeckMatchKind.Preset);
        Assert.Equal(id, rule.PresetId);
        Assert.Equal(ChatDeckRuleAction.Hide, rule.Action);
        Assert.True(rule.Enabled);

        ChatDeckSettings coloured = ChatDeckSettingsEdits.UpdateRule(on, rule.Id, rule with { Action = ChatDeckRuleAction.ColourLine, Colour = 0xFF00FF00 });
        ChatDeckSettings off = ChatDeckSettingsEdits.SetPresetEnabled(coloured, id, false);
        ChatDeckRule kept = off.Rules.Single(r => r.Kind == ChatDeckMatchKind.Preset);
        Assert.False(kept.Enabled);
        Assert.Equal(ChatDeckRuleAction.ColourLine, kept.Action);
        Assert.Single(ChatDeckSettingsEdits.SetPresetEnabled(off, id, true).Rules, r => r.Kind == ChatDeckMatchKind.Preset);
    }

    [Fact]
    public void UnknownIds_ReturnTheSameInstance()
    {
        ChatDeckSettings s = D;
        Assert.Same(s, ChatDeckSettingsEdits.UpdateTab(s, "nope", t => t with { Name = "x" }));
        Assert.Same(s, ChatDeckSettingsEdits.DeleteTab(s, "nope"));
        Assert.Same(s, ChatDeckSettingsEdits.UpdateRule(s, "nope", Move("x", "local")));
        Assert.Same(s, ChatDeckSettingsEdits.DeleteRule(s, "nope"));
        Assert.Same(s, ChatDeckSettingsEdits.MoveRule(s, "my-name", 1));   // already last
    }
}
```

Add to `ChatDeckModelTests.cs`, using the existing construction (`new ChatDeckModel(new ChatLog(), ChatDeckDefaults.Create(), "Davian")`):

```csharp
[Fact]
public void Edit_AppliesAndRaisesOnce_NoChangeRaisesNothing()
{
    var model = new ChatDeckModel(new ChatLog(), ChatDeckDefaults.Create(), "Davian");
    int raised = 0;
    model.SettingsChanged += _ => raised++;
    Assert.False(model.Edit(s => s));
    Assert.Equal(0, raised);
    Assert.True(model.Edit(s => ChatDeckSettingsEdits.AddTab(s, "Loot", out _)));
    Assert.Equal(1, raised);
    Assert.NotNull(model.FindTab("tab-1"));
}

[Fact]
public void Edit_RebuildsRules()
{
    var model = new ChatDeckModel(new ChatLog(), ChatDeckDefaults.Create(), "Davian");
    model.Edit(s => ChatDeckSettingsEdits.AddRule(s,
        new ChatDeckRule { Id = "", Kind = ChatDeckMatchKind.Regex, Pattern = "(" }, out _));
    Assert.Equal(ChatDeckRuleStatus.Invalid, model.CompiledRules[^1].Status);
}
```

Add to `ChatDeckSettingsTests.cs`:

```csharp
[Fact]
public void Normalize_AllTabHasEveryChannel()
{
    ChatDeckSettings s = ChatDeckDefaults.Create();
    var shrunk = s with { Tabs = [s.Tabs[0] with { Channels = new HashSet<ChatDeckChannel> { ChatDeckChannel.Say } }, .. s.Tabs.Skip(1)] };
    ChatDeckTab all = ChatDeckSettingsNormalizer.Normalize(shrunk).Tabs[0];
    Assert.Equal(Enum.GetValues<ChatDeckChannel>().Length, all.Channels.Count);
}
```

- [ ] **Step 2: Run them and see them fail** (compile errors: `ChatDeckSettingsEdits` and `Edit` do not exist).

Run: `~/.dotnet/dotnet test tests/AcDream.Core.Tests --filter "FullyQualifiedName~ChatDeck"`

- [ ] **Step 3: Implement.** Create `src/AcDream.Core/Chat/Deck/ChatDeckSettingsEdits.cs`:

```csharp
namespace AcDream.Core.Chat.Deck;

/// <summary>
/// The editor's changes to the settings, each a pure function from one value to the next.
/// An edit that cannot apply (an unknown id, a full list, the All tab) returns the same instance,
/// so <see cref="ChatDeckModel.Edit"/> can tell nothing changed.
/// </summary>
public static class ChatDeckSettingsEdits
{
    /// <summary>The first <c>prefix + n</c> (n from 1) not in <paramref name="taken"/>.</summary>
    public static string NewId(string prefix, IEnumerable<string> taken)
    {
        var used = taken.ToHashSet(StringComparer.Ordinal);
        for (int n = 1; ; n++)
        {
            string id = $"{prefix}{n}";
            if (!used.Contains(id)) return id;
        }
    }

    public static ChatDeckSettings AddTab(ChatDeckSettings s, string name, out string? tabId)
    {
        tabId = null;
        if (s.Tabs.Count >= ChatDeckLimits.MaxTabs) return s;
        tabId = NewId("tab-", s.Tabs.Select(t => t.Id));
        var tab = new ChatDeckTab
        {
            Id = tabId, Name = name, Icon = ChatDeckIcon.Star,
            Channels = new HashSet<ChatDeckChannel>(), SendChannel = ChatDeckSendChannel.Say,
        };
        return s with { Tabs = [.. s.Tabs, tab] };
    }

    public static ChatDeckSettings UpdateTab(ChatDeckSettings s, string tabId, Func<ChatDeckTab, ChatDeckTab> change)
    {
        int i = IndexOfTab(s, tabId);
        if (i < 0) return s;
        var tabs = s.Tabs.ToList();
        tabs[i] = change(tabs[i]) with { Id = tabId };
        return s with { Tabs = tabs };
    }

    /// <summary>Removes a tab. Rules that move or copy to it stay, turned off, still naming it ("(tab deleted)").</summary>
    public static ChatDeckSettings DeleteTab(ChatDeckSettings s, string tabId)
    {
        if (tabId == ChatDeckDefaults.AllTabId || IndexOfTab(s, tabId) < 0) return s;
        var rules = s.Rules.Select(r => Targets(r) && r.TargetTabId == tabId ? r with { Enabled = false } : r).ToList();
        string last = s.Layout.LastTabId == tabId ? ChatDeckDefaults.AllTabId : s.Layout.LastTabId;
        return s with
        {
            Tabs = s.Tabs.Where(t => t.Id != tabId).ToList(),
            Rules = rules,
            Layout = s.Layout with { LastTabId = last },
        };
    }

    /// <summary>Moves a tab one place (<paramref name="step"/> −1 or +1). All stays first.</summary>
    public static ChatDeckSettings MoveTab(ChatDeckSettings s, string tabId, int step)
    {
        int i = IndexOfTab(s, tabId);
        int j = i + Math.Sign(step);
        if (i <= 0 || j <= 0 || j >= s.Tabs.Count) return s;
        var tabs = s.Tabs.ToList();
        (tabs[i], tabs[j]) = (tabs[j], tabs[i]);
        return s with { Tabs = tabs };
    }

    /// <summary>The default tabs; the rules are kept (the normalizer turns off any whose tab is gone).</summary>
    public static ChatDeckSettings ResetTabs(ChatDeckSettings s) => s with { Tabs = ChatDeckDefaults.Create().Tabs };

    public static ChatDeckSettings AddRule(ChatDeckSettings s, ChatDeckRule draft, out string? ruleId)
    {
        ruleId = null;
        if (s.Rules.Count >= ChatDeckLimits.MaxRules) return s;
        ruleId = NewId("rule-", s.Rules.Select(r => r.Id));
        return s with { Rules = [.. s.Rules, draft with { Id = ruleId, Faulted = false }] };
    }

    /// <summary>New contents for a rule; it keeps its id, and an edit turns a faulted rule back on.</summary>
    public static ChatDeckSettings UpdateRule(ChatDeckSettings s, string ruleId, ChatDeckRule contents) =>
        ReplaceRule(s, ruleId, _ => contents with { Id = ruleId, Faulted = false });

    public static ChatDeckSettings DuplicateRule(ChatDeckSettings s, string ruleId)
    {
        int i = IndexOfRule(s, ruleId);
        if (i < 0 || s.Rules.Count >= ChatDeckLimits.MaxRules) return s;
        var rules = s.Rules.ToList();
        rules.Insert(i + 1, rules[i] with { Id = NewId("rule-", s.Rules.Select(r => r.Id)) });
        return s with { Rules = rules };
    }

    public static ChatDeckSettings DeleteRule(ChatDeckSettings s, string ruleId) =>
        IndexOfRule(s, ruleId) < 0 ? s : s with { Rules = s.Rules.Where(r => r.Id != ruleId).ToList() };

    public static ChatDeckSettings MoveRule(ChatDeckSettings s, string ruleId, int step)
    {
        int i = IndexOfRule(s, ruleId);
        int j = i + Math.Sign(step);
        if (i < 0 || j < 0 || j >= s.Rules.Count) return s;
        var rules = s.Rules.ToList();
        (rules[i], rules[j]) = (rules[j], rules[i]);
        return s with { Rules = rules };
    }

    public static ChatDeckSettings SetRuleEnabled(ChatDeckSettings s, string ruleId, bool enabled) =>
        ReplaceRule(s, ruleId, r => r with { Enabled = enabled, Faulted = enabled ? false : r.Faulted });

    /// <summary>
    /// A standard filter on or off. On adds a Hide rule for it when there is none; off keeps the rule
    /// (its action and colour) and only turns it off.
    /// </summary>
    public static ChatDeckSettings SetPresetEnabled(ChatDeckSettings s, string presetId, bool enabled)
    {
        ChatDeckRule? existing = s.Rules.FirstOrDefault(r => r.Kind == ChatDeckMatchKind.Preset && r.PresetId == presetId);
        if (existing is not null) return SetRuleEnabled(s, existing.Id, enabled);
        if (!enabled) return s;
        return AddRule(s, new ChatDeckRule
        {
            Id = "", Kind = ChatDeckMatchKind.Preset, PresetId = presetId, Action = ChatDeckRuleAction.Hide,
        }, out _);
    }

    public static bool Targets(ChatDeckRule rule) => rule.Action is ChatDeckRuleAction.MoveTo or ChatDeckRuleAction.CopyTo;

    private static ChatDeckSettings ReplaceRule(ChatDeckSettings s, string ruleId, Func<ChatDeckRule, ChatDeckRule> change)
    {
        int i = IndexOfRule(s, ruleId);
        if (i < 0) return s;
        var rules = s.Rules.ToList();
        rules[i] = change(rules[i]);
        return s with { Rules = rules };
    }

    private static int IndexOfTab(ChatDeckSettings s, string tabId)
    {
        for (int i = 0; i < s.Tabs.Count; i++) if (s.Tabs[i].Id == tabId) return i;
        return -1;
    }

    private static int IndexOfRule(ChatDeckSettings s, string ruleId)
    {
        for (int i = 0; i < s.Rules.Count; i++) if (s.Rules[i].Id == ruleId) return i;
        return -1;
    }
}
```

In `ChatDeckSettingsNormalizer.Normalize` (`ChatDeckSettings.cs`), change the line that adds All so the tab always has every channel:

```csharp
tabs.Add((all ?? ChatDeckDefaults.AllTab()) with
{
    Name = CleanName(all?.Name ?? "All"),
    Channels = ChatDeckDefaults.AllTab().Channels,   // All shows every line that is not hidden
});
```

In `ChatDeckModel.cs`, after `ApplySettings`:

```csharp
/// <summary>
/// Applies an editor change (one of <see cref="ChatDeckSettingsEdits"/>) and raises
/// <see cref="SettingsChanged"/> so it is saved. A change that hands back the same
/// instance changed nothing: nothing is applied or raised.
/// </summary>
public bool Edit(Func<ChatDeckSettings, ChatDeckSettings> change)
{
    ChatDeckSettings next = change(Settings);
    if (ReferenceEquals(next, Settings)) return false;
    ApplySettings(next);
    SettingsChanged?.Invoke(Settings);
    return true;
}
```

- [ ] **Step 4: Run the tests and see them pass.** Same command → PASS. The existing JSON round-trip tests must still pass; the All tab's saved channels were already every channel by default.

- [ ] **Step 5: Commit**

```bash
git add src/AcDream.Core/Chat/Deck tests/AcDream.Core.Tests/Chat/Deck
git commit -m "Chat Deck: settings edits for the editor and ChatDeckModel.Edit; All tab always has every channel"
```

---

### Task 2: Core — rule-from-line and the match preview

**Goal:** A rule can be written from a transcript line, with the sender's name and numbers optionally generalised, and any draft rule can be previewed against the session's history.

**Files:**
- Create: `src/AcDream.Core/Chat/Deck/ChatDeckRuleDraft.cs`
- Create: `src/AcDream.Core/Chat/Deck/ChatDeckRulePreview.cs`
- Modify: `src/AcDream.Core/Chat/Deck/ChatDeckMatcher.cs` (`MatchesEmpty`)
- Test: `tests/AcDream.Core.Tests/Chat/Deck/ChatDeckRuleDraftTests.cs` (create), `tests/AcDream.Core.Tests/Chat/Deck/ChatDeckRulePreviewTests.cs` (create)

**Acceptance Criteria:**
- [ ] `FromLine(facts, action)` gives an enabled `Contains` rule on `Text` whose pattern is the message text, limited to the line's channel, with the given action; colour actions get `ChatDeckRuleDraft.DefaultColour`
- [ ] With *any number*, `"You receive 1,250 Pyreals."` gives a `Regex` that matches `"You receive 7 Pyreals."` and the marked preview `You receive [any number] Pyreals.`
- [ ] With *any name*, a sender found in the text is replaced by `.+?` and marked `[any name]`; `CanGeneraliseSender` is false when the sender is blank or not in the text
- [ ] Regex metacharacters in the text are escaped; the pattern never exceeds `ChatDeckMatcher.MaxPatternLength`
- [ ] `ChatDeckRulePreview.Run` counts the matching lines, gives up to 3 samples (newest first, the worded line), reports an invalid pattern's status and error without counting, stops on a timeout and says so, and sets `MatchesEverything` for a pattern that matches empty text
- [ ] `ChatDeckMatcher.MatchesEmpty` is false for invalid rules and for patterns that need text

**Verify:** `~/.dotnet/dotnet test tests/AcDream.Core.Tests --filter "FullyQualifiedName~ChatDeck"` → all pass

**Steps:**

- [ ] **Step 1: Write the failing tests.** Create `tests/AcDream.Core.Tests/Chat/Deck/ChatDeckRuleDraftTests.cs` (`ChatDeckMatcherTests.Facts(text, channel, sender)` is the existing internal helper):

```csharp
using AcDream.Core.Chat.Deck;

namespace AcDream.Core.Tests.Chat.Deck;

public sealed class ChatDeckRuleDraftTests
{
    private static bool Matches(ChatDeckRule rule, string text, ChatDeckChannel channel = ChatDeckChannel.System) =>
        ChatDeckMatcher.Match(ChatDeckMatcher.Compile(rule, "Davian"), ChatDeckMatcherTests.Facts(text, channel, sender: ""), false).Outcome
            == ChatDeckMatchOutcome.Match;

    [Fact]
    public void FromLine_ContainsOnText_LimitedToChannel()
    {
        var facts = ChatDeckMatcherTests.Facts("You receive 1,250 Pyreals.", ChatDeckChannel.System, sender: "");
        ChatDeckRule rule = ChatDeckRuleDraft.FromLine(facts, ChatDeckRuleAction.Hide);
        Assert.True(rule.Enabled);
        Assert.Equal(ChatDeckMatchKind.Contains, rule.Kind);
        Assert.Equal(ChatDeckMatchField.Text, rule.Field);
        Assert.Equal("You receive 1,250 Pyreals.", rule.Pattern);
        Assert.Equal([ChatDeckChannel.System], rule.Channels);
        Assert.Equal(ChatDeckRuleAction.Hide, rule.Action);
        Assert.Equal(ChatDeckRuleDraft.DefaultColour, ChatDeckRuleDraft.FromLine(facts, ChatDeckRuleAction.ColourLine).Colour);
    }

    [Fact]
    public void AnyNumber_GeneralisesAndMarks()
    {
        var facts = ChatDeckMatcherTests.Facts("You receive 1,250 Pyreals.", ChatDeckChannel.System, sender: "");
        Assert.True(ChatDeckRuleDraft.CanGeneraliseNumber(facts));
        ChatDeckRule rule = ChatDeckRuleDraft.FromLine(facts, ChatDeckRuleAction.Hide, anyNumber: true);
        Assert.Equal(ChatDeckMatchKind.Regex, rule.Kind);
        Assert.True(Matches(rule, "You receive 7 Pyreals."));
        Assert.False(Matches(rule, "You receive many Pyreals."));
        Assert.Equal("You receive [any number] Pyreals.", ChatDeckRuleDraft.Preview(facts, anySender: false, anyNumber: true));
    }

    [Fact]
    public void AnyName_ReplacesTheSenderInTheText()
    {
        var facts = ChatDeckMatcherTests.Facts("Bob waves at Bob's dog", ChatDeckChannel.Emote, sender: "Bob");
        Assert.True(ChatDeckRuleDraft.CanGeneraliseSender(facts));
        ChatDeckRule rule = ChatDeckRuleDraft.FromLine(facts, ChatDeckRuleAction.Hide, anySender: true);
        Assert.True(Matches(rule, "Alice waves at Alice's dog", ChatDeckChannel.Emote));
        Assert.Equal("[any name] waves at [any name]'s dog", ChatDeckRuleDraft.Preview(facts, anySender: true, anyNumber: false));
        Assert.False(ChatDeckRuleDraft.CanGeneraliseSender(ChatDeckMatcherTests.Facts("hello", ChatDeckChannel.Say, sender: "Bob")));
        Assert.False(ChatDeckRuleDraft.CanGeneraliseSender(ChatDeckMatcherTests.Facts("hello", ChatDeckChannel.System, sender: "")));
    }

    [Fact]
    public void Metacharacters_AreEscaped_AndLengthIsCapped()
    {
        var facts = ChatDeckMatcherTests.Facts("(2) [x] a+b? " + new string('z', 400), ChatDeckChannel.System, sender: "");
        ChatDeckRule rule = ChatDeckRuleDraft.FromLine(facts, ChatDeckRuleAction.Hide, anyNumber: true);
        Assert.True(rule.Pattern.Length <= ChatDeckMatcher.MaxPatternLength);
        Assert.Equal(ChatDeckRuleStatus.Ok, ChatDeckMatcher.Compile(rule, "Davian").Status);
        Assert.True(Matches(rule, "(9) [x] a+b? " + new string('z', 400)));
        Assert.True(ChatDeckRuleDraft.FromLine(facts, ChatDeckRuleAction.Hide).Pattern.Length <= ChatDeckMatcher.MaxPatternLength);
    }
}
```

Create `tests/AcDream.Core.Tests/Chat/Deck/ChatDeckRulePreviewTests.cs`:

```csharp
using AcDream.Core.Chat;
using AcDream.Core.Chat.Deck;

namespace AcDream.Core.Tests.Chat.Deck;

public sealed class ChatDeckRulePreviewTests
{
    private static ChatDeckLine Line(string text) =>
        new(ChatDeckMatcherTests.Facts(text, ChatDeckChannel.Say), new ChatDeckVerdict(false, ["all"], null, null, [], false, [], []));

    private static ChatDeckRule Contains(string pattern) =>
        new() { Id = "", Kind = ChatDeckMatchKind.Contains, Pattern = pattern, Action = ChatDeckRuleAction.Hide };

    [Fact]
    public void CountsAndSamplesNewestFirst()
    {
        var lines = new[] { Line("loot one"), Line("nothing"), Line("loot two"), Line("loot three"), Line("loot four") };
        ChatDeckPreviewResult r = ChatDeckRulePreview.Run(Contains("loot"), lines, "Davian");
        Assert.Equal(4, r.Count);
        Assert.Equal(5, r.Total);
        Assert.Equal(3, r.Samples.Count);
        Assert.Contains("loot four", r.Samples[0]);
        Assert.Equal(ChatDeckRuleStatus.Ok, r.Status);
    }

    [Fact]
    public void InvalidPattern_ReportsWithoutCounting()
    {
        ChatDeckPreviewResult r = ChatDeckRulePreview.Run(
            new ChatDeckRule { Id = "", Kind = ChatDeckMatchKind.Regex, Pattern = "(" }, [Line("x")], "Davian");
        Assert.Equal(ChatDeckRuleStatus.Invalid, r.Status);
        Assert.NotNull(r.Error);
        Assert.Equal(0, r.Count);
    }

    [Fact]
    public void EmptyMatchingPattern_Warns()
    {
        ChatDeckPreviewResult r = ChatDeckRulePreview.Run(
            new ChatDeckRule { Id = "", Kind = ChatDeckMatchKind.Regex, Pattern = "a*" }, [Line("x")], "Davian");
        Assert.True(r.MatchesEverything);
        Assert.False(ChatDeckRulePreview.Run(Contains("x"), [Line("x")], "Davian").MatchesEverything);
    }

    [Fact]
    public void Timeout_StopsAndSaysSo()
    {
        // Backreference: the backtracking engine, so the 20 ms timeout can trip.
        var rule = new ChatDeckRule { Id = "", Kind = ChatDeckMatchKind.Regex, Pattern = @"^(\w+\s?)*\1$" };
        string evil = string.Concat(Enumerable.Repeat("aaaa ", 30)) + "!";
        ChatDeckPreviewResult r = ChatDeckRulePreview.Run(rule, [Line(evil), Line(evil)], "Davian");
        Assert.True(r.TimedOut);
    }

    [Fact]
    public void DisabledOrFaultedDraft_IsStillPreviewed()
    {
        ChatDeckPreviewResult r = ChatDeckRulePreview.Run(Contains("x") with { Enabled = false, Faulted = true }, [Line("x")], "Davian");
        Assert.Equal(1, r.Count);
    }
}
```

If `Timeout_StopsAndSaysSo` does not time out on this machine, take the pathological pattern and line from the existing fault test in `ChatDeckModelTests` (search for `Faulted`) and use those instead.

- [ ] **Step 2: Run them and see them fail** (compile errors).

Run: `~/.dotnet/dotnet test tests/AcDream.Core.Tests --filter "FullyQualifiedName~ChatDeckRuleDraft|FullyQualifiedName~ChatDeckRulePreview"`

- [ ] **Step 3: Implement.** Add to `ChatDeckMatcher` (after `Match`):

```csharp
/// <summary>Whether the rule's pattern matches empty text, and so every line; the editor warns.</summary>
public static bool MatchesEmpty(ChatDeckCompiledRule compiled)
{
    if (compiled.Status != ChatDeckRuleStatus.Ok || compiled.Regex is null) return false;
    try
    {
        return compiled.Regex.IsMatch("");
    }
    catch (RegexMatchTimeoutException)
    {
        return false;
    }
}
```

Create `src/AcDream.Core/Chat/Deck/ChatDeckRuleDraft.cs`:

```csharp
using System.Text;
using System.Text.RegularExpressions;

namespace AcDream.Core.Chat.Deck;

/// <summary>
/// A rule written from a line in the transcript: a Contains on its message text, on its channel,
/// optionally with the sender's name and the numbers generalised (which makes it a Regex).
/// </summary>
public static class ChatDeckRuleDraft
{
    public const uint DefaultColour = 0xFFFFD24Au;
    public const string NameMark = "[any name]";
    public const string NumberMark = "[any number]";
    private const string NamePattern = ".+?";
    private const string NumberPattern = @"\d+(?:[.,]\d+)*";
    private static readonly Regex Numbers = new(NumberPattern, RegexOptions.CultureInvariant);

    private enum PartKind { Literal, Name, Number }
    private readonly record struct Part(PartKind Kind, string Text);

    public static ChatDeckRule FromLine(
        ChatDeckLineFacts facts, ChatDeckRuleAction action, bool anySender = false, bool anyNumber = false)
    {
        (ChatDeckMatchKind kind, string pattern) = Pattern(facts, anySender, anyNumber);
        return new ChatDeckRule
        {
            Id = "", Kind = kind, Pattern = pattern, Field = ChatDeckMatchField.Text,
            Channels = new HashSet<ChatDeckChannel> { facts.Channel },
            Action = action,
            Colour = action is ChatDeckRuleAction.ColourLine or ChatDeckRuleAction.ColourMatch ? DefaultColour : 0,
        };
    }

    public static bool CanGeneraliseSender(ChatDeckLineFacts facts) =>
        facts.Sender.Trim().Length > 0 && facts.Text.Contains(facts.Sender.Trim(), StringComparison.OrdinalIgnoreCase);

    public static bool CanGeneraliseNumber(ChatDeckLineFacts facts) => Numbers.IsMatch(facts.Text);

    /// <summary>The text the rule matches, with the generalised parts shown as marks.</summary>
    public static string Preview(ChatDeckLineFacts facts, bool anySender, bool anyNumber) =>
        string.Concat(Parts(facts, anySender, anyNumber).Select(p => p.Kind switch
        {
            PartKind.Name => NameMark,
            PartKind.Number => NumberMark,
            _ => p.Text,
        }));

    public static (ChatDeckMatchKind Kind, string Pattern) Pattern(ChatDeckLineFacts facts, bool anySender, bool anyNumber)
    {
        List<Part> parts = Parts(facts, anySender, anyNumber);
        if (parts.All(p => p.Kind == PartKind.Literal))
        {
            string text = facts.Text;
            return (ChatDeckMatchKind.Contains,
                text.Length > ChatDeckMatcher.MaxPatternLength ? text[..ChatDeckMatcher.MaxPatternLength] : text);
        }

        // Drop parts from the end until the pattern fits; a shorter unanchored pattern still matches the line.
        while (true)
        {
            var sb = new StringBuilder();
            foreach (Part p in parts)
                sb.Append(p.Kind switch
                {
                    PartKind.Name => NamePattern,
                    PartKind.Number => NumberPattern,
                    _ => Regex.Escape(p.Text),
                });
            if (sb.Length <= ChatDeckMatcher.MaxPatternLength || parts.Count <= 1)
                return (ChatDeckMatchKind.Regex, sb.Length <= ChatDeckMatcher.MaxPatternLength ? sb.ToString() : sb.ToString()[..ChatDeckMatcher.MaxPatternLength]);
            parts.RemoveAt(parts.Count - 1);
        }
    }

    private static List<Part> Parts(ChatDeckLineFacts facts, bool anySender, bool anyNumber)
    {
        string text = facts.Text;
        var pieces = new List<Part>();
        string name = facts.Sender.Trim();
        if (anySender && CanGeneraliseSender(facts))
        {
            int at = 0;
            for (int i = text.IndexOf(name, StringComparison.OrdinalIgnoreCase); i >= 0;
                 i = text.IndexOf(name, at, StringComparison.OrdinalIgnoreCase))
            {
                if (i > at) pieces.Add(new Part(PartKind.Literal, text[at..i]));
                pieces.Add(new Part(PartKind.Name, text.Substring(i, name.Length)));
                at = i + name.Length;
            }
            if (at < text.Length) pieces.Add(new Part(PartKind.Literal, text[at..]));
        }
        else
        {
            pieces.Add(new Part(PartKind.Literal, text));
        }

        if (!anyNumber) return pieces;
        var parts = new List<Part>();
        foreach (Part p in pieces)
        {
            if (p.Kind != PartKind.Literal)
            {
                parts.Add(p);
                continue;
            }
            int at = 0;
            foreach (Match m in Numbers.Matches(p.Text))
            {
                if (m.Index > at) parts.Add(new Part(PartKind.Literal, p.Text[at..m.Index]));
                parts.Add(new Part(PartKind.Number, m.Value));
                at = m.Index + m.Length;
            }
            if (at < p.Text.Length) parts.Add(new Part(PartKind.Literal, p.Text[at..]));
        }
        return parts;
    }
}
```

Create `src/AcDream.Core/Chat/Deck/ChatDeckRulePreview.cs`:

```csharp
namespace AcDream.Core.Chat.Deck;

/// <summary>What a draft rule would catch in the lines already received.</summary>
/// <param name="Count">Matching lines.</param>
/// <param name="Total">Lines looked at (fewer than given when a timeout stopped the count).</param>
/// <param name="Samples">Up to the requested number of matching lines, newest first, as worded.</param>
public sealed record ChatDeckPreviewResult(
    int Count, int Total, IReadOnlyList<string> Samples, ChatDeckRuleStatus Status, string? Error,
    bool TimedOut, bool MatchesEverything);

public static class ChatDeckRulePreview
{
    /// <summary>
    /// Runs the draft over <paramref name="lines"/> (oldest first). The draft is previewed as if it were on,
    /// whatever its enabled or faulted state. A timeout stops the count.
    /// </summary>
    public static ChatDeckPreviewResult Run(
        ChatDeckRule draft, IReadOnlyList<ChatDeckLine> lines, string characterName, int samples = 3)
    {
        ChatDeckCompiledRule compiled = ChatDeckMatcher.Compile(draft with { Enabled = true, Faulted = false }, characterName);
        if (compiled.Status != ChatDeckRuleStatus.Ok)
            return new ChatDeckPreviewResult(0, 0, [], compiled.Status, compiled.Error, false, false);

        bool everything = ChatDeckMatcher.MatchesEmpty(compiled);
        bool wantSpans = draft.Action == ChatDeckRuleAction.ColourMatch;
        var found = new List<string>();
        int count = 0, total = 0;
        for (int i = lines.Count - 1; i >= 0; i--)
        {
            ChatDeckMatch m = ChatDeckMatcher.Match(compiled, lines[i].Facts, wantSpans);
            if (m.Outcome == ChatDeckMatchOutcome.Timeout)
                return new ChatDeckPreviewResult(count, total, found, ChatDeckRuleStatus.Ok, null, true, everything);
            total++;
            if (m.Outcome != ChatDeckMatchOutcome.Match) continue;
            count++;
            if (found.Count < samples) found.Add(lines[i].Facts.Line);
        }
        return new ChatDeckPreviewResult(count, total, found, ChatDeckRuleStatus.Ok, null, false, everything);
    }
}
```

- [ ] **Step 4: Run the tests and see them pass.** Run `~/.dotnet/dotnet test tests/AcDream.Core.Tests --filter "FullyQualifiedName~ChatDeck"` → PASS.

- [ ] **Step 5: Commit**

```bash
git add src/AcDream.Core/Chat/Deck tests/AcDream.Core.Tests/Chat/Deck
git commit -m "Chat Deck: rule-from-line with generalised name and numbers; match preview over history"
```

---

### Task 3: Core — rule summaries, test lines and the verdict in words

**Goal:** The rules table can describe each rule (When / From / Do / status), the test box can turn a typed line into facts, and a verdict can be explained rule by rule.

**Files:**
- Create: `src/AcDream.Core/Chat/Deck/ChatDeckRuleSummary.cs`
- Create: `src/AcDream.Core/Chat/Deck/ChatDeckTestLine.cs`
- Create: `src/AcDream.Core/Chat/Deck/ChatDeckVerdictText.cs`
- Test: `tests/AcDream.Core.Tests/Chat/Deck/ChatDeckRuleSummaryTests.cs` (create), `tests/AcDream.Core.Tests/Chat/Deck/ChatDeckVerdictTextTests.cs` (create)

**Acceptance Criteria:**
- [ ] `When`: `Text contains "loot"`, `Line matches /x+/`, `Sender contains "Bob"`, `My name`, `Standard: <label>`, and `Standard: <id> (unknown)` for an unknown preset
- [ ] `From`: `Any channel` for empty; a whole group shows the group name (`Combat`); other channels show display names; more than 3 items show the first 3 then `+N`; presets read their own channels
- [ ] `Do`: `Hide`, `Move to Combat`, `Also show in Group`, `Colour line`, `Colour match`, `(tab deleted)` when the target is gone, and ` + alert` when the rule alerts
- [ ] `Status`: empty for OK; `Off` for disabled; `Invalid: <error>`, `Timed out; edit it to turn it back on`, `Unknown standard filter`
- [ ] `ChatDeckTestLine.Facts(General, "Bob", "hi")` has channel General and the worded line `[General] Bob says, "hi"`, and the `social.general` preset matches it; a Combat line's worded line is its text; every channel gives facts on that channel
- [ ] `ChatDeckVerdictText.Lines` says "Hidden by Rule N (…)" for a hidden line; otherwise "Goes to: All, …", each Move/Copy that decided, the line colour's source or "Channel colour", the alert's source ("Alert: Rule N" / "Alert: incoming tell") or "No alert", each matched rule that had no effect, and "No rule matched." when none did

**Verify:** `~/.dotnet/dotnet test tests/AcDream.Core.Tests --filter "FullyQualifiedName~ChatDeck"` → all pass

**Steps:**

- [ ] **Step 1: Write the failing tests.** Create `tests/AcDream.Core.Tests/Chat/Deck/ChatDeckRuleSummaryTests.cs`:

```csharp
using AcDream.Core.Chat.Deck;

namespace AcDream.Core.Tests.Chat.Deck;

public sealed class ChatDeckRuleSummaryTests
{
    private static readonly IReadOnlyList<ChatDeckTab> Tabs = ChatDeckDefaults.Create().Tabs;

    private static ChatDeckRule R(ChatDeckMatchKind kind = ChatDeckMatchKind.Contains, string pattern = "loot",
        ChatDeckMatchField field = ChatDeckMatchField.Text) =>
        new() { Id = "r", Kind = kind, Pattern = pattern, Field = field };

    [Fact]
    public void When_DescribesEachKind()
    {
        Assert.Equal("Text contains \"loot\"", ChatDeckRuleSummary.When(R()));
        Assert.Equal("Line matches /x+/", ChatDeckRuleSummary.When(R(ChatDeckMatchKind.Regex, "x+", ChatDeckMatchField.Line)));
        Assert.Equal("Sender contains \"Bob\"", ChatDeckRuleSummary.When(R(pattern: "Bob", field: ChatDeckMatchField.Sender)));
        Assert.Equal("My name", ChatDeckRuleSummary.When(R(ChatDeckMatchKind.MyName)));
        ChatDeckPreset p = ChatDeckPresets.All[0];
        Assert.Equal($"Standard: {p.Label}", ChatDeckRuleSummary.When(R(ChatDeckMatchKind.Preset) with { PresetId = p.Id }));
        Assert.Equal("Standard: gone (unknown)", ChatDeckRuleSummary.When(R(ChatDeckMatchKind.Preset) with { PresetId = "gone" }));
    }

    [Fact]
    public void From_GroupsAndCounts()
    {
        Assert.Equal("Any channel", ChatDeckRuleSummary.From(R()));
        Assert.Equal("Combat", ChatDeckRuleSummary.From(R() with { Channels = ChatDeckChannels.Group("Combat").ToHashSet() }));
        Assert.Equal("Say, Trade", ChatDeckRuleSummary.From(R() with { Channels = new HashSet<ChatDeckChannel> { ChatDeckChannel.Trade, ChatDeckChannel.Say } }));
        var many = new HashSet<ChatDeckChannel> { ChatDeckChannel.Say, ChatDeckChannel.Trade, ChatDeckChannel.Help, ChatDeckChannel.Recall, ChatDeckChannel.Craft };
        Assert.Equal("Say, Trade, Help +2", ChatDeckRuleSummary.From(R() with { Channels = many }));
    }

    [Fact]
    public void Do_NamesTargetsAndAlert()
    {
        Assert.Equal("Hide", ChatDeckRuleSummary.Do(R() with { Action = ChatDeckRuleAction.Hide }, Tabs));
        Assert.Equal("Move to Combat", ChatDeckRuleSummary.Do(R() with { Action = ChatDeckRuleAction.MoveTo, TargetTabId = "combat" }, Tabs));
        Assert.Equal("Also show in Group + alert", ChatDeckRuleSummary.Do(R() with { Action = ChatDeckRuleAction.CopyTo, TargetTabId = "group", Alert = true }, Tabs));
        Assert.Equal("Move to (tab deleted)", ChatDeckRuleSummary.Do(R() with { Action = ChatDeckRuleAction.MoveTo, TargetTabId = "gone" }, Tabs));
        Assert.Equal("Colour line", ChatDeckRuleSummary.Do(R() with { Action = ChatDeckRuleAction.ColourLine }, Tabs));
    }

    [Fact]
    public void Status_ReadsTheCompiledRule()
    {
        Assert.Equal("", ChatDeckRuleSummary.Status(ChatDeckMatcher.Compile(R(), "Davian")));
        Assert.Equal("Off", ChatDeckRuleSummary.Status(ChatDeckMatcher.Compile(R() with { Enabled = false }, "Davian")));
        Assert.StartsWith("Invalid: ", ChatDeckRuleSummary.Status(ChatDeckMatcher.Compile(R(ChatDeckMatchKind.Regex, "("), "Davian")));
        Assert.StartsWith("Timed out", ChatDeckRuleSummary.Status(ChatDeckMatcher.Compile(R() with { Faulted = true }, "Davian")));
    }

    [Fact]
    public void TestLine_GeneralIsWorded_AndPresetsSeeIt()
    {
        ChatDeckLineFacts facts = ChatDeckTestLine.Facts(ChatDeckChannel.General, "Bob", "hi", "Davian");
        Assert.Equal(ChatDeckChannel.General, facts.Channel);
        Assert.Equal("[General] Bob says, \"hi\"", facts.Line);
        ChatDeckRule preset = new() { Id = "p", Kind = ChatDeckMatchKind.Preset, PresetId = "social.general" };
        Assert.Equal(ChatDeckMatchOutcome.Match,
            ChatDeckMatcher.Match(ChatDeckMatcher.Compile(preset, "Davian"), facts, false).Outcome);
        Assert.Equal("You hit the drudge for 5 points of slashing damage!",
            ChatDeckTestLine.Facts(ChatDeckChannel.CombatSelf, "", "You hit the drudge for 5 points of slashing damage!", "Davian").Line);
        foreach (ChatDeckChannel c in Enum.GetValues<ChatDeckChannel>())
            Assert.Equal(c, ChatDeckTestLine.Facts(c, "Bob", "hi", "Davian").Channel);
    }
}
```

Create `tests/AcDream.Core.Tests/Chat/Deck/ChatDeckVerdictTextTests.cs`:

```csharp
using AcDream.Core.Chat.Deck;

namespace AcDream.Core.Tests.Chat.Deck;

public sealed class ChatDeckVerdictTextTests
{
    private static ChatDeckSettings With(params ChatDeckRule[] rules) => ChatDeckDefaults.Create() with { Rules = rules };

    private static ChatDeckRule R(string id, ChatDeckRuleAction action, string target = "", bool alert = false) =>
        new() { Id = id, Kind = ChatDeckMatchKind.Contains, Pattern = "loot", Action = action, TargetTabId = target,
                Colour = 0xFF00FF00, Alert = alert };

    private static IReadOnlyList<string> Explain(ChatDeckSettings s, ChatDeckLineFacts facts) =>
        ChatDeckVerdictText.Lines(new ChatDeckRuleEngine(s.Rules, s.Tabs, "Davian").Evaluate(facts), s);

    [Fact]
    public void Hidden_NamesTheRule()
    {
        var s = With(R("a", ChatDeckRuleAction.CopyTo, "combat"), R("b", ChatDeckRuleAction.Hide));
        IReadOnlyList<string> lines = Explain(s, ChatDeckMatcherTests.Facts("free loot"));
        Assert.Equal("Hidden by Rule 2 (Text contains \"loot\")", lines[0]);
        Assert.Contains("Rule 1 (Text contains \"loot\") also matched (no effect)", lines);
    }

    [Fact]
    public void Shown_ExplainsDestinationsColourAndAlert()
    {
        var s = With(R("m", ChatDeckRuleAction.MoveTo, "combat"), R("c", ChatDeckRuleAction.ColourLine, alert: true));
        IReadOnlyList<string> lines = Explain(s, ChatDeckMatcherTests.Facts("free loot"));
        Assert.Equal("Goes to: All, Combat", lines[0]);
        Assert.Contains("Rule 1 (Text contains \"loot\"): Move to Combat", lines);
        Assert.Contains("Line colour: Rule 2", lines);
        Assert.Contains("Alert: Rule 2", lines);
    }

    [Fact]
    public void NoRules_SaysSo_AndTellsAlert()
    {
        IReadOnlyList<string> lines = Explain(With(), ChatDeckMatcherTests.Facts("hello", ChatDeckChannel.TellIn));
        Assert.Contains("Channel colour", lines);
        Assert.Contains("Alert: incoming tell", lines);
        Assert.Contains("No rule matched.", lines);
        Assert.Contains("No alert", Explain(With(), ChatDeckMatcherTests.Facts("hello")));
    }
}
```

- [ ] **Step 2: Run them and see them fail** (compile errors).

Run: `~/.dotnet/dotnet test tests/AcDream.Core.Tests --filter "FullyQualifiedName~ChatDeckRuleSummary|FullyQualifiedName~ChatDeckVerdictText"`

- [ ] **Step 3: Implement.** Create `src/AcDream.Core/Chat/Deck/ChatDeckRuleSummary.cs`:

```csharp
namespace AcDream.Core.Chat.Deck;

/// <summary>A rule as the editor's table describes it.</summary>
public static class ChatDeckRuleSummary
{
    public const string DeletedTab = "(tab deleted)";

    public static string When(ChatDeckRule rule) => rule.Kind switch
    {
        ChatDeckMatchKind.MyName => "My name",
        ChatDeckMatchKind.Preset => ChatDeckPresets.Get(rule.PresetId) is { } p
            ? $"Standard: {p.Label}"
            : $"Standard: {rule.PresetId} (unknown)",
        ChatDeckMatchKind.Regex => $"{FieldName(rule.Field)} matches /{rule.Pattern}/",
        _ => $"{FieldName(rule.Field)} contains \"{rule.Pattern}\"",
    };

    public static string From(ChatDeckRule rule)
    {
        IReadOnlySet<ChatDeckChannel> channels = rule.Kind == ChatDeckMatchKind.Preset && ChatDeckPresets.Get(rule.PresetId) is { } p
            ? p.Channels
            : rule.Channels;
        if (channels.Count == 0) return "Any channel";
        var items = new List<string>();
        foreach (ChatDeckChannelGroup group in ChatDeckChannels.Groups)
        {
            if (group.Channels.All(channels.Contains)) items.Add(group.Name);
            else items.AddRange(group.Channels.Where(channels.Contains).Select(ChatDeckChannels.DisplayName));
        }
        return items.Count <= 3 ? string.Join(", ", items) : $"{string.Join(", ", items.Take(3))} +{items.Count - 3}";
    }

    public static string Do(ChatDeckRule rule, IReadOnlyList<ChatDeckTab> tabs)
    {
        string target = tabs.FirstOrDefault(t => t.Id == rule.TargetTabId)?.Name ?? DeletedTab;
        string action = rule.Action switch
        {
            ChatDeckRuleAction.Hide => "Hide",
            ChatDeckRuleAction.MoveTo => $"Move to {target}",
            ChatDeckRuleAction.CopyTo => $"Also show in {target}",
            ChatDeckRuleAction.ColourLine => "Colour line",
            ChatDeckRuleAction.ColourMatch => "Colour match",
            _ => rule.Action.ToString(),
        };
        return rule.Alert ? action + " + alert" : action;
    }

    /// <summary>Why a rule does not run, for the table and the form; empty when it runs.</summary>
    public static string Status(ChatDeckCompiledRule compiled) => compiled.Status switch
    {
        ChatDeckRuleStatus.Disabled => "Off",
        ChatDeckRuleStatus.Invalid => $"Invalid: {compiled.Error}",
        ChatDeckRuleStatus.Faulted => "Timed out; edit it to turn it back on",
        ChatDeckRuleStatus.UnknownPreset => "Unknown standard filter",
        _ => "",
    };

    /// <summary>Statuses the editor shows in red.</summary>
    public static bool IsProblem(ChatDeckRuleStatus status) =>
        status is ChatDeckRuleStatus.Invalid or ChatDeckRuleStatus.Faulted or ChatDeckRuleStatus.UnknownPreset;

    public static string FieldName(ChatDeckMatchField field) => field switch
    {
        ChatDeckMatchField.Sender => "Sender",
        ChatDeckMatchField.Line => "Line",
        _ => "Text",
    };
}
```

Create `src/AcDream.Core/Chat/Deck/ChatDeckTestLine.cs`:

```csharp
namespace AcDream.Core.Chat.Deck;

/// <summary>
/// A line typed into the editor's test box, as the model would see it: a chat entry built for the
/// chosen channel so the worded line ("[General] Bob says, …") is what presets read live.
/// </summary>
public static class ChatDeckTestLine
{
    private const string Someone = "Someone";

    public static ChatDeckLineFacts Facts(ChatDeckChannel channel, string sender, string text, string characterName)
    {
        string who = sender.Trim();
        ChatEntry entry = Entry(channel, who, text);
        // The channel is the one picked, whatever the synthesized entry would classify as.
        return ChatDeckLineFacts.From(entry, characterName) with { Channel = channel, IsOwn = false };
    }

    private static ChatEntry Entry(ChatDeckChannel channel, string sender, string text)
    {
        string speaker = sender.Length > 0 ? sender : Someone;
        ChatEntry Speech(ChatKind kind, RetailLogTextType type) => new(kind, speaker, text, 0u, 0u) { LogTextType = (uint)type };
        ChatEntry Room(string name) => new(ChatKind.Channel, speaker, text, 0u, 0u) { ChannelName = name };
        ChatEntry Legacy(uint id) => new(ChatKind.Channel, speaker, text, 0u, id)
        {
            LogTextType = LegacyChannelChatType.Resolve(id, ownSend: false),
        };
        ChatEntry Plain(RetailLogTextType type) => new(ChatKind.System, sender, text, 0u, 0u) { LogTextType = (uint)type };

        return channel switch
        {
            ChatDeckChannel.Say => Speech(ChatKind.LocalSpeech, RetailLogTextType.Speech),
            ChatDeckChannel.Emote => Speech(ChatKind.Emote, RetailLogTextType.Emote),
            ChatDeckChannel.TellIn => Speech(ChatKind.Tell, RetailLogTextType.Tell),
            ChatDeckChannel.TellOut => Speech(ChatKind.Tell, RetailLogTextType.SpeechDirectSend),
            ChatDeckChannel.Fellowship => Legacy(0x00000800u),
            ChatDeckChannel.Patron => Legacy(0x00001000u),
            ChatDeckChannel.Vassals => Legacy(0x00002000u),
            ChatDeckChannel.Monarch => Legacy(0x00004000u),
            ChatDeckChannel.CoVassals => Legacy(0x01000000u),
            ChatDeckChannel.Allegiance => Room("Allegiance"),
            ChatDeckChannel.General => Room("General"),
            ChatDeckChannel.Trade => Room("Trade"),
            ChatDeckChannel.Lfg => Room("LFG"),
            ChatDeckChannel.Roleplay => Room("Roleplay"),
            ChatDeckChannel.Society => Room("Society"),
            ChatDeckChannel.Olthoi => Room("Olthoi"),
            ChatDeckChannel.Help => Legacy(0x00000400u),
            ChatDeckChannel.OtherChannel => Room("Other"),
            ChatDeckChannel.Combat => Plain(RetailLogTextType.Combat),
            ChatDeckChannel.CombatEnemy => Plain(RetailLogTextType.CombatEnemy),
            ChatDeckChannel.CombatSelf => Plain(RetailLogTextType.CombatSelf),
            ChatDeckChannel.Magic => Plain(RetailLogTextType.Magic),
            ChatDeckChannel.Spellcasting => Speech(ChatKind.LocalSpeech, RetailLogTextType.Spellcasting),
            ChatDeckChannel.Advancement => Plain(RetailLogTextType.Advancement),
            ChatDeckChannel.Appraisal => Plain(RetailLogTextType.Appraisal),
            ChatDeckChannel.Recall => Plain(RetailLogTextType.Recall),
            ChatDeckChannel.Craft => Plain(RetailLogTextType.Craft),
            ChatDeckChannel.Salvaging => Plain(RetailLogTextType.Salvaging),
            ChatDeckChannel.WorldBroadcast => Plain(RetailLogTextType.WorldBroadcast),
            ChatDeckChannel.Plugin => Plain(RetailLogTextType.ClientLocal),
            _ => Plain(RetailLogTextType.System),
        };
    }
}
```

`LegacyChannelChatType.Resolve(uint, bool ownSend)` is the helper `ChatLog.OnChannelBroadcast` already uses; check its namespace with `grep -rn "class LegacyChannelChatType" src/AcDream.Core` and add the `using` if it is not `AcDream.Core.Chat`. If a combat or system line's worded `Line` is not its plain text, check `ChatLineWording.Format` for `ChatKind.System` and use `ChatKind.Combat` for the combat channels instead. The test pins the expected wording.

Create `src/AcDream.Core/Chat/Deck/ChatDeckVerdictText.cs`:

```csharp
namespace AcDream.Core.Chat.Deck;

/// <summary>A verdict in words, for the editor's test box: what happened to the line and which rule decided each part.</summary>
public static class ChatDeckVerdictText
{
    public static IReadOnlyList<string> Lines(ChatDeckVerdict verdict, ChatDeckSettings settings)
    {
        var lines = new List<string>();
        string Rule(string id)
        {
            int i = -1;
            for (int k = 0; k < settings.Rules.Count; k++) if (settings.Rules[k].Id == id) { i = k; break; }
            return i < 0 ? id : $"Rule {i + 1} ({ChatDeckRuleSummary.When(settings.Rules[i])})";
        }
        string RuleNumber(string id)
        {
            for (int k = 0; k < settings.Rules.Count; k++) if (settings.Rules[k].Id == id) return $"Rule {k + 1}";
            return id;
        }
        ChatDeckRule? Find(string id) => settings.Rules.FirstOrDefault(r => r.Id == id);

        if (verdict.Hidden)
        {
            ChatDeckRuleHit hide = verdict.Hits.First(h => h.Decided && h.Action == ChatDeckRuleAction.Hide);
            lines.Add($"Hidden by {Rule(hide.RuleId)}");
        }
        else
        {
            var names = verdict.Destinations.Select(id => settings.Tabs.FirstOrDefault(t => t.Id == id)?.Name ?? id);
            lines.Add($"Goes to: {string.Join(", ", names)}");
            foreach (ChatDeckRuleHit hit in verdict.Hits)
                if (hit.Decided && hit.Action is ChatDeckRuleAction.MoveTo or ChatDeckRuleAction.CopyTo && Find(hit.RuleId) is { } r)
                    lines.Add($"{Rule(hit.RuleId)}: {ChatDeckRuleSummary.Do(r with { Alert = false }, settings.Tabs)}");

            // ChatDeckRuleHit is a struct: with no such hit, FirstOrDefault gives one with Decided == false.
            ChatDeckRuleHit lineHit = verdict.Hits.FirstOrDefault(h => h.Decided && h.Action == ChatDeckRuleAction.ColourLine);
            lines.Add(lineHit.Decided ? $"Line colour: {RuleNumber(lineHit.RuleId)}" : "Channel colour");
            ChatDeckRuleHit matchHit = verdict.Hits.FirstOrDefault(h => h.Decided && h.Action == ChatDeckRuleAction.ColourMatch);
            if (matchHit.Decided) lines.Add($"Match colour: {RuleNumber(matchHit.RuleId)}");

            var alerting = verdict.Hits.Where(h => Find(h.RuleId)?.Alert == true).Select(h => RuleNumber(h.RuleId)).ToList();
            if (!verdict.Alert) lines.Add("No alert");
            else lines.Add(alerting.Count > 0 ? $"Alert: {string.Join(", ", alerting)}" : "Alert: incoming tell");
        }

        foreach (ChatDeckRuleHit hit in verdict.Hits)
            if (!hit.Decided) lines.Add($"{Rule(hit.RuleId)} also matched (no effect)");
        foreach (string id in verdict.TimedOutRuleIds)
            lines.Add($"{Rule(id)} timed out on this line");
        if (verdict.Hits.Count == 0) lines.Add("No rule matched.");
        return lines;
    }
}
```

- [ ] **Step 4: Run the tests and see them pass.** Run `~/.dotnet/dotnet test tests/AcDream.Core.Tests --filter "FullyQualifiedName~ChatDeck"` → PASS. If `ChatDeckLineFacts` is a positional record without `with` support for `IsOwn` (it is a `sealed record`, so `with` works), no change is needed.

- [ ] **Step 5: Commit**

```bash
git add src/AcDream.Core/Chat/Deck tests/AcDream.Core.Tests/Chat/Deck
git commit -m "Chat Deck: rule summaries, test-box lines and verdicts in words"
```

---

### Task 4: Deck — search reads the shown text; read state and "N new" while searching

**Goal:** Search matches only what the transcript shows, a narrowed view does not mark lines read, and the "N new" marker counts only the lines the view would show.

**Files:**
- Modify: `src/AcDream.App/UI/Hud/ChatDeckTranscript.cs` (`Build`, `Cached`, `ChatDeckTranscriptLayout`)
- Modify: `src/AcDream.App/UI/Hud/UiChatDeck.cs` (`Refresh`, `TrackNewLines`, `ResetView`, `FollowNewest`)
- Test: `tests/AcDream.App.Tests/UI/Hud/ChatDeckTranscriptTests.cs`, `tests/AcDream.App.Tests/UI/Hud/UiChatDeckSearchTests.cs`

**Acceptance Criteria:**
- [ ] With channel names hidden, searching "General" does not match `[General] Bob says, "hi"`; with them shown it does
- [ ] `ChatDeckTranscriptLayout.Sources` lists each included `ChatDeckLine` once, oldest first, and `SourceOf[i]` is the index into `Sources` of visual line `i`
- [ ] With a search open and the view at the bottom, a new line in the selected tab (matching or not) raises the tab's unread count; closing the search (view at the bottom) clears it
- [ ] Scrolled up with a search open, "N new" counts only new lines that match the search
- [ ] The existing `ScrolledUp_CountsNewLines_ClickFollows` test and every other `ChatDeck` App test still pass

**Verify:** `~/.dotnet/dotnet test tests/AcDream.App.Tests --filter "FullyQualifiedName~ChatDeck"` → all pass

**Steps:**

- [ ] **Step 1: Write the failing tests.** In `ChatDeckTranscriptTests.cs`, follow the file's existing helpers for building a `ChatDeckLine` from a `ChatEntry` and a style (`grep -n "static" tests/AcDream.App.Tests/UI/Hud/ChatDeckTranscriptTests.cs`). Add:

```csharp
[Fact]
public void Search_ReadsTheShownText_NotAHiddenPrefix()
{
    ChatDeckLine line = ChannelLine("General", "Bob", "hi");   // the file's helper for a named-channel line; add one if missing
    var builder = new ChatDeckTranscript();
    Assert.Single(builder.Build([line], Style(search: "General", channelPrefix: true)).Sources);
    Assert.Empty(builder.Build([line], Style(search: "General", channelPrefix: false)).Sources);
    Assert.Single(builder.Build([line], Style(search: "Bob", channelPrefix: false)).Sources);
}

[Fact]
public void Layout_MapsVisualLinesToSources()
{
    var builder = new ChatDeckTranscript();
    ChatDeckLine a = SayLine(new string('a', 300)), b = SayLine("short");
    ChatDeckTranscriptLayout layout = builder.Build([a, b], Style(wrapWidth: 200f));
    Assert.Equal([a, b], layout.Sources);
    Assert.Equal(layout.Lines.Count, layout.SourceOf.Count);
    Assert.Equal(0, layout.SourceOf[0]);
    Assert.Equal(1, layout.SourceOf[^1]);
}
```

If the file has no `Style(...)` helper taking these named arguments, add one that builds `ChatDeckTranscriptStyle(wrapWidth, s => s.Length * 7f, null, ChatDeckTimestamps.Off, _ => Vector4.One, Vector4.One, Vector4.One, search, ChannelPrefix: channelPrefix)`. A named-channel line comes from `ChatLog.OnChannelBroadcast(0, "Bob", "hi", channelName: "General")` fed through a `ChatDeckModel` (as `UiChatDeckTests.Harness` does), then taken from `model.FindTab("all")!.Snapshot()`.

In `UiChatDeckSearchTests.cs`, using `UiChatDeckTests.Harness` (it has `Say(text)`, which ticks the model; check whether it also calls `Deck.Refresh()` and call it if not):

```csharp
[Fact]
public void Searching_KeepsNewLinesUnread_UntilTheSearchCloses()
{
    var h = new UiChatDeckTests.Harness();
    h.Deck.OnShown();
    h.Deck.Refresh();
    h.Deck.SearchField.SetText("zzz");
    h.Deck.SearchField.OnTextChanged!("zzz");
    h.Say("hello");
    h.Deck.Refresh();
    ChatDeckTabState all = h.Model!.FindTab("all")!;
    Assert.Equal(1, all.Unread);

    h.Deck.SearchField.OnEscape!();   // closes the search
    h.Deck.Refresh();
    Assert.Equal(0, all.Unread);
}

[Fact]
public void ScrolledUpWhileSearching_CountsOnlyMatchingLines()
{
    var h = new UiChatDeckTests.Harness();
    h.Deck.OnShown();
    h.Say("loot one");
    h.Deck.SearchField.OnTextChanged!("loot");
    h.Deck.Refresh();
    UiScrollable scroll = h.Deck.Transcript.Scroll;
    scroll.SetExtents(1000, 100);
    scroll.SetScrollY(0);
    h.Deck.Refresh();

    h.Say("loot two");
    h.Say("other");
    h.Say("loot three");
    h.Deck.Refresh();
    Assert.Equal(2, h.Deck.NewLineCount);
}
```

The search is opened in the first test by setting the text directly: `_searchText` follows `OnTextChanged`. If `OnShown` needs a mounted window in this harness, follow how the existing `UiChatDeckSearchTests` reach a shown, visible deck.

- [ ] **Step 2: Run them and see them fail.** The layout tests fail to compile (`Sources`, `SourceOf`); the deck tests fail on the unread and count asserts.

Run: `~/.dotnet/dotnet test tests/AcDream.App.Tests --filter "FullyQualifiedName~ChatDeck"`

- [ ] **Step 3: Implement.** In `ChatDeckTranscript.cs`, add to `ChatDeckTranscriptLayout`:

```csharp
/// <summary>The lines included, oldest first, once each.</summary>
public List<ChatDeckLine> Sources { get; } = [];
/// <summary>For each visual line, its index in <see cref="Sources"/>.</summary>
public List<int> SourceOf { get; } = [];
```

In `Cached` add `public string? Stripped;`, the text with the prefix taken off, worked out the first time a search needs it. In `Build`, replace the search test and record the sources:

```csharp
if (style.Search.Length > 0
    && SearchText(c, line, style.ChannelPrefix).IndexOf(style.Search, StringComparison.OrdinalIgnoreCase) < 0)
    continue;
// ... wrap and shape as before ...
layout.Sources.Add(line);
AddLine(layout, c);
```

```csharp
/// <summary>The text a search reads: the line as shown, so a hidden channel prefix is not found.</summary>
private static string SearchText(Cached c, ChatDeckLine line, bool prefix) =>
    prefix ? c.Projected.Text : c.Stripped ??= ChatDeckPrefix.Strip(c.Projected, line.Entry).Text;
```

In `AddLine`, after the other lists: `for (int i = 0; i < c.Lines.Length; i++) layout.SourceOf.Add(layout.Sources.Count - 1);`. Since `AddLine` runs after `Sources.Add`, the index is that line's.

In `UiChatDeck.cs`, replace `_addedAtScrollUp` with the newest line the view showed when it left the bottom:

```csharp
private long? _seenThrough;   // the newest line shown when the view left the bottom
```

```csharp
private void TrackNewLines()
{
    if (SelectedTab is null || Transcript.Scroll.AtEnd)
    {
        _seenThrough = null;
        NewLineCount = 0;
    }
    else
    {
        List<ChatDeckLine> shown = _layout.Sources;
        _seenThrough ??= shown.Count > 0 ? shown[^1].Sequence : long.MinValue;
        int n = 0;
        for (int i = shown.Count - 1; i >= 0 && shown[i].Sequence > _seenThrough; i--) n++;
        NewLineCount = n;
    }
    NewLinesMarker.Visible = NewLineCount > 0;
}
```

In `ResetView` and `FollowNewest`, replace `_addedAtScrollUp = null;` with `_seenThrough = null;`. In `Refresh`, report a narrowed view as not at the bottom:

```csharp
// A search hides lines, so a narrowed view has not seen what arrives; closing it shows them (and marks them read).
_model?.SetView(_shown && _drawer.DeckVisible, Transcript.Scroll.AtEnd && _searchText.Length == 0);
```

- [ ] **Step 4: Run the tests and see them pass.** Same command → PASS, including `ScrolledUp_CountsNewLines_ClickFollows`.

- [ ] **Step 5: Commit**

```bash
git add src/AcDream.App/UI/Hud tests/AcDream.App.Tests/UI/Hud
git commit -m "Chat Deck: search reads the shown text; a narrowed view leaves new lines unread; N new counts shown lines"
```

---

### Task 5: Editor — shell, request and the Tabs page

**Goal:** A markup editor window binding with two pages; its Tabs page lists the tabs and edits the selected one's name, icon, send channel, peek and channels, adds, reorders, deletes (with a confirm row) and resets to defaults (with a confirm row).

**Files:**
- Create: `src/AcDream.App/UI/Hud/ChatDeckEditorRequest.cs`
- Create: `src/AcDream.App/UI/Hud/ChatDeckChannelPicker.cs`
- Create: `src/AcDream.App/UI/Hud/ChatDeckEditorBinding.cs`
- Create: `src/AcDream.App/UI/Hud/ChatDeckEditorBinding.Tabs.cs`
- Create: `src/AcDream.App/UI/Hud/ChatDeckEditorBinding.Rules.cs` (placeholder page, filled by Task 6)
- Test: `tests/AcDream.App.Tests/UI/Hud/ChatDeckEditorTabsTests.cs` (create)

**Acceptance Criteria:**
- [ ] `ChatDeckEditorBinding.Markup` builds with `MarkupDocument.Build(markup, binding, _ => (0u, 0, 0))` and with `ChatDeckSettingsBinding.Prepare`
- [ ] `Open(new ChatDeckEditorRequest { Page = Tabs, TabId = "group" })` shows the Tabs page with Group selected; `ConfirmDeleteTab = true` shows the delete confirm row
- [ ] The tab list shows every tab's name in order; picking a row selects that tab
- [ ] Setting the name (on submit or blur), icon, send channel and peek changes only that field of the selected tab, through `ChatDeckModel.Edit`
- [ ] The channel picker lists the five groups with their channels; a channel checkbox adds or removes that channel; a group box is true for all, false for none, null for some, and setting it sets the whole group; All shows the note and no picker
- [ ] *+ New tab* adds and selects a tab, and is disabled at 16 tabs; Up and Down move the selected tab and are disabled for All and at the ends
- [ ] *Delete tab* (disabled for All) shows the confirm row; *Delete* removes the tab and selects All; *Cancel* hides the row. *Reset to defaults* works the same way through its own confirm row
- [ ] A new model (character switch) puts the editor back to the Tabs page on All with no confirm row showing

**Verify:** `~/.dotnet/dotnet test tests/AcDream.App.Tests --filter "FullyQualifiedName~ChatDeckEditor"` → all pass

**Steps:**

- [ ] **Step 1: Write the failing tests.** Create `tests/AcDream.App.Tests/UI/Hud/ChatDeckEditorTabsTests.cs`:

```csharp
using AcDream.App.UI;
using AcDream.App.UI.Hud;
using AcDream.App.UI.Markup;
using AcDream.Core.Chat;
using AcDream.Core.Chat.Deck;

namespace AcDream.App.Tests.UI.Hud;

public sealed class ChatDeckEditorTabsTests
{
    internal static (ChatDeckModel Model, ChatDeckEditorBinding Binding) Make()
    {
        var model = new ChatDeckModel(new ChatLog(), ChatDeckDefaults.Create(), "Davian");
        return (model, new ChatDeckEditorBinding(() => model));
    }

    [Fact]
    public void Markup_Builds()
    {
        var (_, b) = Make();
        UiElement window = ChatDeckSettingsBinding.Prepare(MarkupDocument.Build(ChatDeckEditorBinding.Markup, b, _ => (0u, 0, 0)), () => { });
        Assert.NotNull(window);
    }

    [Fact]
    public void Open_SelectsPageTabAndConfirm()
    {
        var (_, b) = Make();
        b.Open(new ChatDeckEditorRequest { Page = ChatDeckEditorPage.Tabs, TabId = "group", ConfirmDeleteTab = true });
        Assert.True(b.OnTabsPage);
        Assert.Equal("Group", b.TabName);
        Assert.Equal(3, b.SelectedTabIndex);
        Assert.True(b.ConfirmingDelete);
    }

    [Fact]
    public void Fields_EditOnlyTheSelectedTab()
    {
        var (m, b) = Make();
        b.SelectTabRow(1);   // Local
        b.SetTabName("Near");
        b.SetTabIcon("Coin");
        b.SetTabSend("Trade");
        b.SetTabPeek(false);
        ChatDeckTab local = m.Settings.Tabs[1];
        Assert.Equal(("Near", ChatDeckIcon.Coin, ChatDeckSendChannel.Trade, false), (local.Name, local.Icon, local.SendChannel, local.Peek));
        Assert.Equal("Social", m.Settings.Tabs[2].Name);
        Assert.Equal(["All", "Near", "Social", "Group", "Combat", "System"], b.TabNames);
    }

    [Fact]
    public void ChannelPicker_GroupsAndSingles()
    {
        var (m, b) = Make();
        b.SelectTabRow(1);   // Local: Say, Emote, TellIn, TellOut
        ChatDeckChannelPicker.GroupRow localGroup = b.TabChannels.Groups.Single(g => g.Name == "Local");
        Assert.True(localGroup.All);
        localGroup.Channels.Single(c => c.Id == nameof(ChatDeckChannel.Emote)).SetOn(false);
        Assert.Null(localGroup.All);
        Assert.DoesNotContain(ChatDeckChannel.Emote, m.Settings.Tabs[1].Channels);
        b.TabChannels.Groups.Single(g => g.Name == "Combat").SetAll(true);
        Assert.True(ChatDeckChannels.Group("Combat").All(m.Settings.Tabs[1].Channels.Contains));

        b.SelectTabRow(0);
        Assert.False(b.ChannelsEditable);
    }

    [Fact]
    public void AddMoveDelete()
    {
        var (m, b) = Make();
        b.AddTab();
        Assert.Equal("tab-1", m.Settings.Tabs[^1].Id);
        Assert.Equal(m.Settings.Tabs.Count - 1, b.SelectedTabIndex);
        Assert.False(b.CanMoveTabDown);
        b.MoveTabUp();
        Assert.Equal("tab-1", m.Settings.Tabs[^2].Id);

        b.SelectTabRow(0);
        Assert.False(b.CanDeleteTab);
        Assert.False(b.CanMoveTabUp);
        Assert.False(b.CanMoveTabDown);

        b.SelectTabRow(2);   // Social
        b.AskDeleteTab();
        Assert.True(b.ConfirmingDelete);
        b.CancelConfirm();
        Assert.False(b.ConfirmingDelete);
        b.AskDeleteTab();
        b.DeleteTabConfirmed();
        Assert.DoesNotContain(m.Settings.Tabs, t => t.Id == "social");
        Assert.Equal(0, b.SelectedTabIndex);
    }

    [Fact]
    public void AddTab_DisabledAtLimit_ResetAsksFirst()
    {
        var (m, b) = Make();
        while (m.Settings.Tabs.Count < ChatDeckLimits.MaxTabs) b.AddTab();
        Assert.False(b.CanAddTab);
        b.AskReset();
        Assert.True(b.ConfirmingReset);
        b.ResetConfirmed();
        Assert.Equal(ChatDeckDefaults.Create().Tabs.Count, m.Settings.Tabs.Count);
        Assert.False(b.ConfirmingReset);
    }

    [Fact]
    public void NewModel_ResetsTheEditor()
    {
        var log = new ChatLog();
        ChatDeckModel model = new(log, ChatDeckDefaults.Create(), "Davian");
        var b = new ChatDeckEditorBinding(() => model);
        b.Open(new ChatDeckEditorRequest { Page = ChatDeckEditorPage.Rules, TabId = "group", ConfirmDeleteTab = true });
        model = new ChatDeckModel(log, ChatDeckDefaults.Create(), "Other");
        Assert.True(b.OnTabsPage);
        Assert.Equal(0, b.SelectedTabIndex);
        Assert.False(b.ConfirmingDelete);
    }
}
```

- [ ] **Step 2: Run them and see them fail** (compile errors).

Run: `~/.dotnet/dotnet test tests/AcDream.App.Tests --filter "FullyQualifiedName~ChatDeckEditor"`

- [ ] **Step 3: Implement.** Create `src/AcDream.App/UI/Hud/ChatDeckEditorRequest.cs`:

```csharp
using AcDream.Core.Chat.Deck;

namespace AcDream.App.UI.Hud;

internal enum ChatDeckEditorPage { Tabs, Rules }

/// <summary>What the deck asks the Filters &amp; Tabs editor to show when it opens.</summary>
internal sealed record ChatDeckEditorRequest
{
    public ChatDeckEditorPage Page { get; init; }
    /// <summary>The tab to select on the Tabs page; null keeps the current one.</summary>
    public string? TabId { get; init; }
    /// <summary>Open with the selected tab's delete confirmation showing (the tab menu's Delete).</summary>
    public bool ConfirmDeleteTab { get; init; }
    /// <summary>A new rule to open in the rule form; added only when the player presses Add.</summary>
    public ChatDeckRule? NewRule { get; init; }
    /// <summary>The line <see cref="NewRule"/> was written from, for the generalise options.</summary>
    public ChatDeckLineFacts? FromLine { get; init; }
    /// <summary>A line to put in the test box, with its real channel and sender.</summary>
    public ChatDeckLineFacts? TestLine { get; init; }
}
```

The binding is `public` for markup reflection, but `Open` takes an internal type, so make `Open` `internal`. Make `ChatDeckChannelPicker` and its nested row classes `public`: markup binds to their public properties.

Create `src/AcDream.App/UI/Hud/ChatDeckChannelPicker.cs`:

```csharp
using AcDream.Core.Chat.Deck;

namespace AcDream.App.UI.Hud;

/// <summary>Channels as grouped checkboxes over one channel set: a tab's, or the rule form's.</summary>
public sealed class ChatDeckChannelPicker
{
    private readonly Func<IReadOnlySet<ChatDeckChannel>> _get;
    private readonly Action<IReadOnlySet<ChatDeckChannel>> _set;

    public ChatDeckChannelPicker(Func<IReadOnlySet<ChatDeckChannel>> get, Action<IReadOnlySet<ChatDeckChannel>> set)
    {
        _get = get;
        _set = set;
        Groups = ChatDeckChannels.Groups.Select(g => new GroupRow(this, g)).ToList();
    }

    public IReadOnlyList<GroupRow> Groups { get; }

    private bool Has(ChatDeckChannel c) => _get().Contains(c);

    private void Set(IEnumerable<ChatDeckChannel> channels, bool on)
    {
        var next = _get().ToHashSet();
        foreach (ChatDeckChannel c in channels)
            if (on) next.Add(c);
            else next.Remove(c);
        _set(next);
    }

    /// <summary>One group: a box for the whole group (null when only some are on), then its channels.</summary>
    public sealed class GroupRow(ChatDeckChannelPicker owner, ChatDeckChannelGroup group)
    {
        public string Name => group.Name;
        public bool? All
        {
            get
            {
                int on = group.Channels.Count(owner.Has);
                return on == 0 ? false : on == group.Channels.Count ? true : null;
            }
        }
        public Action<bool> SetAll => on => owner.Set(group.Channels, on);
        public IReadOnlyList<ChannelRow> Channels { get; } = group.Channels.Select(c => new ChannelRow(owner, c)).ToList();
    }

    public sealed class ChannelRow(ChatDeckChannelPicker owner, ChatDeckChannel channel)
    {
        public string Id => channel.ToString();
        public string Name => ChatDeckChannels.DisplayName(channel);
        public bool On => owner.Has(channel);
        public Action<bool> SetOn => on => owner.Set([channel], on);
    }
}
```

Create `src/AcDream.App/UI/Hud/ChatDeckEditorBinding.cs`:

```csharp
using AcDream.Core.Chat.Deck;

namespace AcDream.App.UI.Hud;

/// <summary>
/// What the Filters &amp; Tabs editor reads and writes. Built once and kept, like the window;
/// it follows whichever model the session holds, and starts over (Tabs page, All) on a new one.
/// Every change goes through <see cref="ChatDeckModel.Edit"/>, which saves it.
/// </summary>
public sealed partial class ChatDeckEditorBinding
{
    private readonly Func<ChatDeckModel?> _model;
    private ChatDeckModel? _seen;

    public ChatDeckEditorBinding(Func<ChatDeckModel?> model)
    {
        _model = model;
        TabChannels = new ChatDeckChannelPicker(
            () => SelectedTab?.Channels ?? ChatDeckRule.AnyChannel,
            set => EditTab(t => t with { Channels = set }));
        CreateRules();             // ChatDeckEditorBinding.Rules.cs
    }

    /// <summary>The model, after starting over if it is not the one this editor last saw.</summary>
    private ChatDeckModel? M
    {
        get
        {
            ChatDeckModel? m = _model();
            if (!ReferenceEquals(m, _seen))
            {
                _seen = m;
                Reset();
            }
            return m;
        }
    }

    private ChatDeckSettings S => M?.Settings ?? ChatDeckDefaults.Create();

    private bool Edit(Func<ChatDeckSettings, ChatDeckSettings> change) => M?.Edit(change) ?? false;

    private ChatDeckEditorPage _page;
    public bool OnTabsPage => Page == ChatDeckEditorPage.Tabs;
    public bool OnRulesPage => Page == ChatDeckEditorPage.Rules;
    private ChatDeckEditorPage Page { get { _ = M; return _page; } }
    public Action ShowTabsPage => () => _page = ChatDeckEditorPage.Tabs;
    public Action ShowRulesPage => () => _page = ChatDeckEditorPage.Rules;
    public Action? Close { get; set; }

    /// <summary>Points the editor at what the deck asked for; the runtime then shows the window.</summary>
    internal void Open(ChatDeckEditorRequest request)
    {
        _ = M;
        _page = request.Page;
        if (request.TabId is { } id && S.Tabs.Any(t => t.Id == id)) _tabId = id;
        _confirmDelete = request.ConfirmDeleteTab;
        _confirmReset = false;
        OpenRules(request);        // ChatDeckEditorBinding.Rules.cs
    }

    private void Reset()
    {
        _page = ChatDeckEditorPage.Tabs;
        _tabId = ChatDeckDefaults.AllTabId;
        _confirmDelete = _confirmReset = false;
        ResetRules();              // ChatDeckEditorBinding.Rules.cs
    }

    public const string Markup = Head + TabsPageMarkup + RulesPageMarkup + Foot;

    private const string Head = """
        <panel x="90" y="60" w="640" h="600" title="Filters &amp; Tabs" theme="plugin" layout="column" padding="10" gap="6">
          <group layout="row" gap="4" h="24">
            <tab text="Tabs" selected="{OnTabsPage}" onclick="{ShowTabsPage}" w="90" h="22" />
            <tab text="Rules" selected="{OnRulesPage}" onclick="{ShowRulesPage}" w="90" h="22" />
          </group>
        """;

    private const string Foot = """
          <group layout="row" gap="8" h="24">
            <group grow="1" />
            <button text="Close" onclick="{Close}" w="72" h="24" />
          </group>
        </panel>
        """;
}
```

Create `src/AcDream.App/UI/Hud/ChatDeckEditorBinding.Tabs.cs`:

```csharp
using AcDream.Core.Chat.Deck;

namespace AcDream.App.UI.Hud;

public sealed partial class ChatDeckEditorBinding
{
    private string _tabId = ChatDeckDefaults.AllTabId;
    private bool _confirmDelete;
    private bool _confirmReset;

    private ChatDeckTab? SelectedTab => S.Tabs.FirstOrDefault(t => t.Id == _tabId) ?? S.Tabs.FirstOrDefault();
    private bool AllSelected => SelectedTab?.Id == ChatDeckDefaults.AllTabId;
    private void EditTab(Func<ChatDeckTab, ChatDeckTab> change)
    {
        if (SelectedTab is { } tab) Edit(s => ChatDeckSettingsEdits.UpdateTab(s, tab.Id, change));
    }

    public IReadOnlyList<string> TabNames => S.Tabs.Select(t => t.Name).ToList();
    public int SelectedTabIndex => Math.Max(0, S.Tabs.ToList().FindIndex(t => t.Id == SelectedTab?.Id));
    public Action<int> SelectTabRow => i =>
    {
        if (i < 0 || i >= S.Tabs.Count) return;
        _tabId = S.Tabs[i].Id;
        _confirmDelete = false;
    };

    public string TabName => SelectedTab?.Name ?? "";
    public Action<string> SetTabName => v => { if (v.Trim().Length > 0) EditTab(t => t with { Name = v }); };

    public IReadOnlyList<string> IconNames { get; } = Enum.GetNames<ChatDeckIcon>();
    public string TabIcon => (SelectedTab?.Icon ?? ChatDeckIcon.All).ToString();
    public Action<string> SetTabIcon => v => { if (Enum.TryParse(v, out ChatDeckIcon icon)) EditTab(t => t with { Icon = icon }); };

    public IReadOnlyList<string> SendNames { get; } = ChatDeckSend.All.Select(ChatDeckSend.DisplayName).ToList();
    public string TabSend => ChatDeckSend.DisplayName(SelectedTab?.SendChannel ?? ChatDeckSendChannel.Say);
    public Action<string> SetTabSend => v =>
    {
        foreach (ChatDeckSendChannel c in ChatDeckSend.All)
            if (ChatDeckSend.DisplayName(c) == v) EditTab(t => t with { SendChannel = c });
    };

    public bool TabPeek => SelectedTab?.Peek ?? false;
    public Action<bool> SetTabPeek => v => EditTab(t => t with { Peek = v });

    public ChatDeckChannelPicker TabChannels { get; }
    public IReadOnlyList<ChatDeckChannelPicker.GroupRow> TabChannelGroups => TabChannels.Groups;
    public bool ChannelsEditable => !AllSelected;
    public bool ShowAllNote => AllSelected;

    public bool CanAddTab => S.Tabs.Count < ChatDeckLimits.MaxTabs;
    public Action AddTab => () =>
    {
        string? id = null;
        if (Edit(s => ChatDeckSettingsEdits.AddTab(s, "New tab", out id)) && id is not null) _tabId = id;
    };

    public bool CanMoveTabUp => SelectedTabIndex > 1;
    public bool CanMoveTabDown => !AllSelected && SelectedTabIndex < S.Tabs.Count - 1;
    public Action MoveTabUp => () => { if (SelectedTab is { } t) Edit(s => ChatDeckSettingsEdits.MoveTab(s, t.Id, -1)); };
    public Action MoveTabDown => () => { if (SelectedTab is { } t) Edit(s => ChatDeckSettingsEdits.MoveTab(s, t.Id, 1)); };

    public bool CanDeleteTab => !AllSelected;
    public bool ConfirmingDelete => _confirmDelete && !AllSelected;
    public string DeleteQuestion => $"Delete the tab \"{TabName}\"? Its lines stay in All.";
    public Action AskDeleteTab => () => { _confirmDelete = !AllSelected; _confirmReset = false; };
    public Action DeleteTabConfirmed => () =>
    {
        if (SelectedTab is { } t) Edit(s => ChatDeckSettingsEdits.DeleteTab(s, t.Id));
        _tabId = ChatDeckDefaults.AllTabId;
        _confirmDelete = false;
    };

    public bool ConfirmingReset => _confirmReset;
    public Action AskReset => () => { _confirmReset = true; _confirmDelete = false; };
    public Action ResetConfirmed => () =>
    {
        Edit(ChatDeckSettingsEdits.ResetTabs);
        _tabId = ChatDeckDefaults.AllTabId;
        _confirmReset = false;
    };
    public Action CancelConfirm => () => _confirmDelete = _confirmReset = false;

    private const string TabsPageMarkup = """
          <group layout="row" gap="10" grow="1" visible="{OnTabsPage}">
            <group layout="column" gap="6" w="170">
              <list items="{TabNames}" selected="{SelectedTabIndex}" onchange="{SelectTabRow}" rowheight="18" selectionband="true" grow="1" />
              <group layout="row" gap="4" h="22">
                <button text="+ New tab" onclick="{AddTab}" enabled="{CanAddTab}" grow="1" h="22" />
                <button text="Up" onclick="{MoveTabUp}" enabled="{CanMoveTabUp}" w="40" h="22" />
                <button text="Down" onclick="{MoveTabDown}" enabled="{CanMoveTabDown}" w="44" h="22" />
              </group>
            </group>
            <group layout="column" gap="6" grow="1">
              <group layout="row" gap="8" align="center" h="22">
                <label text="Name" w="80" />
                <field text="{TabName}" onsubmit="{SetTabName}" onblur="{SetTabName}" maxlength="24" grow="1" h="20" />
              </group>
              <group layout="row" gap="8" align="center" h="22">
                <label text="Icon" w="80" />
                <menu items="{IconNames}" selected="{TabIcon}" onchange="{SetTabIcon}" rows="12" grow="1" h="20" />
              </group>
              <group layout="row" gap="8" align="center" h="22">
                <label text="Send as" w="80" />
                <menu items="{SendNames}" selected="{TabSend}" onchange="{SetTabSend}" rows="13" grow="1" h="20" />
              </group>
              <checkbox text="Peek new lines while collapsed" checked="{TabPeek}" onchange="{SetTabPeek}" h="16" />
              <label text="Channels" h="16" />
              <label text="All shows every line that no rule hides." visible="{ShowAllNote}" h="16" />
              <group layout="column" gap="2" scroll="y" grow="1" visible="{ChannelsEditable}">
                <repeat items="{TabChannelGroups}" key="{Name}">
                  <checkbox text="{Name}" checked="{All}" onchange="{SetAll}" h="16" />
                  <repeat items="{Channels}" key="{Id}">
                    <group layout="row" h="16">
                      <group w="18" />
                      <checkbox text="{Name}" checked="{On}" onchange="{SetOn}" grow="1" h="16" />
                    </group>
                  </repeat>
                </repeat>
              </group>
              <group layout="row" gap="8" h="22">
                <button text="Delete tab" onclick="{AskDeleteTab}" enabled="{CanDeleteTab}" w="90" h="22" />
                <group grow="1" />
                <button text="Reset to defaults" onclick="{AskReset}" w="130" h="22" />
              </group>
              <group layout="row" gap="8" align="center" h="22" visible="{ConfirmingDelete}">
                <label text="{DeleteQuestion}" grow="1" />
                <button text="Delete" onclick="{DeleteTabConfirmed}" w="64" h="22" />
                <button text="Cancel" onclick="{CancelConfirm}" w="64" h="22" />
              </group>
              <group layout="row" gap="8" align="center" h="22" visible="{ConfirmingReset}">
                <label text="Put the tabs back to the defaults? Rules stay." grow="1" />
                <button text="Reset" onclick="{ResetConfirmed}" w="64" h="22" />
                <button text="Cancel" onclick="{CancelConfirm}" w="64" h="22" />
              </group>
            </group>
          </group>
        """;
}
```

The tests call `b.AddTab()`, `b.SelectTabRow(1)` and so on: those are delegate-typed properties, so the calls invoke them.

Create `src/AcDream.App/UI/Hud/ChatDeckEditorBinding.Rules.cs` as a placeholder that Task 6 replaces:

```csharp
namespace AcDream.App.UI.Hud;

public sealed partial class ChatDeckEditorBinding
{
    private void CreateRules() { }
    private void ResetRules() { }
    private void OpenRules(ChatDeckEditorRequest request) { }

    private const string RulesPageMarkup = """
          <group layout="column" gap="6" grow="1" visible="{OnRulesPage}">
            <label text="Rules" h="16" />
          </group>
        """;
}
```

Check the markup against `docs/plugin-ui-markup.md` if a build fails: `menu selected` is a `string`, `checkbox checked` takes `bool?` for the group box, `field` takes `maxlength`, `onsubmit` and `onblur`, and `list` takes `selectionband`. A `<repeat>` must be the direct child of a flex container: the scrolling `<group layout="column">` is one.

- [ ] **Step 4: Run the tests and see them pass.** Same command → PASS.

- [ ] **Step 5: Commit**

```bash
git add src/AcDream.App/UI/Hud tests/AcDream.App.Tests/UI/Hud
git commit -m "Chat Deck: Filters & Tabs editor window with its Tabs page"
```

---

### Task 6: Editor — the rules table

**Goal:** The Rules page lists every rule in order (on/off, When, From, Do), shows problem rules in red with the selected rule's status, and edits, duplicates, deletes and reorders rules from a right-click menu and buttons.

**Files:**
- Modify: `src/AcDream.App/UI/Hud/ChatDeckEditorBinding.Rules.cs` (replace the placeholder)
- Test: `tests/AcDream.App.Tests/UI/Hud/ChatDeckEditorRulesTests.cs` (create)

**Acceptance Criteria:**
- [ ] `RuleOn`, `RuleWhen`, `RuleFrom` and `RuleDo` give one entry per rule, in order, from `ChatDeckRuleSummary`
- [ ] `RuleColours` is red (`0xFF6B6B`) for Invalid / Faulted / UnknownPreset rules and the normal ink otherwise; `SelectedRuleStatus` is the selected rule's `ChatDeckRuleSummary.Status`
- [ ] `ToggleRule(i)` flips that rule's enabled flag (through `SetRuleEnabled`)
- [ ] The row menu is `Edit|Duplicate|Delete|-|Move up|Move down`. `RuleMenuOpen(row)` selects the row; Duplicate, Delete, Move up and Move down act on it; Edit opens the rule form (Task 7) on it
- [ ] The Up / Down buttons move the selected rule, and the selection follows it
- [ ] The row lists are rebuilt only when the settings or compiled rules change (same instances on two reads)

**Verify:** `~/.dotnet/dotnet test tests/AcDream.App.Tests --filter "FullyQualifiedName~ChatDeckEditor"` → all pass

**Steps:**

- [ ] **Step 1: Write the failing tests.** Create `tests/AcDream.App.Tests/UI/Hud/ChatDeckEditorRulesTests.cs`:

```csharp
using AcDream.App.UI.Hud;
using AcDream.Core.Chat.Deck;

namespace AcDream.App.Tests.UI.Hud;

public sealed class ChatDeckEditorRulesTests
{
    private static ChatDeckRule R(string pattern, ChatDeckMatchKind kind = ChatDeckMatchKind.Contains) =>
        new() { Id = "", Kind = kind, Pattern = pattern, Action = ChatDeckRuleAction.Hide };

    private static (ChatDeckModel, ChatDeckEditorBinding) WithRules(params ChatDeckRule[] rules)
    {
        var (m, b) = ChatDeckEditorTabsTests.Make();
        foreach (ChatDeckRule r in rules) m.Edit(s => ChatDeckSettingsEdits.AddRule(s, r, out _));
        return (m, b);
    }

    [Fact]
    public void Rows_DescribeEachRule_ProblemsInRed()
    {
        var (_, b) = WithRules(R("loot"), R("(", ChatDeckMatchKind.Regex));
        Assert.Equal(["My name", "Text contains \"loot\"", "Text matches /(/"], b.RuleWhen);
        Assert.Equal([true, true, true], b.RuleOn);
        Assert.Equal("Hide", b.RuleDo[1]);
        Assert.Equal("Any channel", b.RuleFrom[1]);
        Assert.Equal(ChatDeckEditorBinding.ProblemColour, b.RuleColours[2]);
        Assert.NotEqual(ChatDeckEditorBinding.ProblemColour, b.RuleColours[1]);
        b.SelectRuleRow(2);
        Assert.StartsWith("Invalid: ", b.SelectedRuleStatus);
        Assert.Same(b.RuleWhen, b.RuleWhen);
    }

    [Fact]
    public void Toggle_FlipsEnabled()
    {
        var (m, b) = WithRules(R("loot"));
        b.ToggleRule(1);
        Assert.False(m.Settings.Rules[1].Enabled);
        b.ToggleRule(1);
        Assert.True(m.Settings.Rules[1].Enabled);
    }

    [Fact]
    public void RowMenu_DuplicateDeleteMove()
    {
        var (m, b) = WithRules(R("a"), R("b"));
        Assert.Equal(["Edit", "Duplicate", "Delete", "-", "Move up", "Move down"], b.RuleMenu);
        b.RuleMenuOpen(1);
        b.RuleMenuSelect(1);   // Duplicate
        Assert.Equal(["my-name", "rule-1", "rule-3", "rule-2"], m.Settings.Rules.Select(r => r.Id));
        b.RuleMenuOpen(3);
        b.RuleMenuSelect(4);   // Move up
        Assert.Equal("rule-2", m.Settings.Rules[2].Id);
        Assert.Equal(2, b.SelectedRuleIndex);
        b.RuleMenuOpen(2);
        b.RuleMenuSelect(2);   // Delete
        Assert.DoesNotContain(m.Settings.Rules, r => r.Id == "rule-2");
    }

    [Fact]
    public void Buttons_MoveTheSelection()
    {
        var (m, b) = WithRules(R("a"), R("b"));
        b.SelectRuleRow(2);
        b.MoveRuleUp();
        Assert.Equal("rule-2", m.Settings.Rules[1].Id);
        Assert.Equal(1, b.SelectedRuleIndex);
        b.MoveRuleDown();
        Assert.Equal(2, b.SelectedRuleIndex);
    }
}
```

- [ ] **Step 2: Run them and see them fail** (compile errors).

Run: `~/.dotnet/dotnet test tests/AcDream.App.Tests --filter "FullyQualifiedName~ChatDeckEditorRules"`

- [ ] **Step 3: Implement.** Replace `ChatDeckEditorBinding.Rules.cs`:

```csharp
using AcDream.Core.Chat.Deck;

namespace AcDream.App.UI.Hud;

public sealed partial class ChatDeckEditorBinding
{
    public const uint ProblemColour = 0xFF6B6Bu;   // 0xRRGGBB, as list colours are
    private static readonly uint InkColour = ChatDeckChannelColours.Argb(ChatDeckPalette.TextInk) & 0xFFFFFFu;

    private string? _ruleId;
    private object? _rowsFor;
    private RuleRows _rows = new([], [], [], [], [], []);

    private sealed record RuleRows(
        IReadOnlyList<bool> On, IReadOnlyList<string> When, IReadOnlyList<string> From,
        IReadOnlyList<string> Do, IReadOnlyList<uint> Colours, IReadOnlyList<string> Status);

    private void CreateRules()
    {
        CreateRuleForm();          // ChatDeckEditorBinding.RuleForm.cs
    }

    private void ResetRules()
    {
        _ruleId = null;
        _rowsFor = null;
        ResetPanes();              // ChatDeckEditorBinding.RuleForm.cs
    }

    private void OpenRules(ChatDeckEditorRequest request)
    {
        if (request.NewRule is { } rule) StartForm(rule, editingId: null, request.FromLine);   // RuleForm.cs
        if (request.TestLine is { } line) StartTest(line);                                      // Test.cs
    }

    /// <summary>The table's columns, rebuilt only when the settings or the compiled rules change.</summary>
    private RuleRows Rows
    {
        get
        {
            ChatDeckModel? m = M;
            var key = (m?.Settings, m?.CompiledRules);
            if (Equals(key, _rowsFor)) return _rows;
            _rowsFor = key;
            ChatDeckSettings s = S;
            IReadOnlyList<ChatDeckCompiledRule> compiled = m?.CompiledRules ?? [];
            ChatDeckRuleStatus StatusAt(int i) => i < compiled.Count ? compiled[i].Status : ChatDeckRuleStatus.Ok;
            _rows = new RuleRows(
                s.Rules.Select(r => r.Enabled).ToList(),
                s.Rules.Select(ChatDeckRuleSummary.When).ToList(),
                s.Rules.Select(ChatDeckRuleSummary.From).ToList(),
                s.Rules.Select(r => ChatDeckRuleSummary.Do(r, s.Tabs)).ToList(),
                s.Rules.Select((_, i) => ChatDeckRuleSummary.IsProblem(StatusAt(i)) ? ProblemColour : InkColour).ToList(),
                s.Rules.Select((_, i) => i < compiled.Count ? ChatDeckRuleSummary.Status(compiled[i]) : "").ToList());
            return _rows;
        }
    }

    public IReadOnlyList<bool> RuleOn => Rows.On;
    public IReadOnlyList<string> RuleWhen => Rows.When;
    public IReadOnlyList<string> RuleFrom => Rows.From;
    public IReadOnlyList<string> RuleDo => Rows.Do;
    public IReadOnlyList<uint> RuleColours => Rows.Colours;

    public int SelectedRuleIndex => _ruleId is null ? -1 : S.Rules.ToList().FindIndex(r => r.Id == _ruleId);
    public Action<int> SelectRuleRow => i => _ruleId = i >= 0 && i < S.Rules.Count ? S.Rules[i].Id : null;
    public string SelectedRuleStatus => SelectedRuleIndex is var i and >= 0 && i < Rows.Status.Count ? Rows.Status[i] : "";
    public bool HasSelectedRule => SelectedRuleIndex >= 0;

    public Action<int> ToggleRule => i =>
    {
        if (i < 0 || i >= S.Rules.Count) return;
        ChatDeckRule r = S.Rules[i];
        Edit(s => ChatDeckSettingsEdits.SetRuleEnabled(s, r.Id, !r.Enabled));
    };

    public IReadOnlyList<string> RuleMenu { get; } = ["Edit", "Duplicate", "Delete", "-", "Move up", "Move down"];
    public Action<int> RuleMenuOpen => row => SelectRuleRow(row);
    public Action<int> RuleMenuSelect => index =>
    {
        if (_ruleId is not { } id) return;
        switch (index)
        {
            case 0: EditSelectedRule(); break;
            case 1: Edit(s => ChatDeckSettingsEdits.DuplicateRule(s, id)); break;
            case 2: Edit(s => ChatDeckSettingsEdits.DeleteRule(s, id)); _ruleId = null; break;
            case 4: Edit(s => ChatDeckSettingsEdits.MoveRule(s, id, -1)); break;
            case 5: Edit(s => ChatDeckSettingsEdits.MoveRule(s, id, 1)); break;
        }
    };

    public Action MoveRuleUp => () => { if (_ruleId is { } id) Edit(s => ChatDeckSettingsEdits.MoveRule(s, id, -1)); };
    public Action MoveRuleDown => () => { if (_ruleId is { } id) Edit(s => ChatDeckSettingsEdits.MoveRule(s, id, 1)); };
    public Action EditRule => EditSelectedRule;

    private void EditSelectedRule()
    {
        if (S.Rules.FirstOrDefault(r => r.Id == _ruleId) is { } rule) StartForm(rule, rule.Id, fromLine: null);
    }

    private const string RulesPageMarkup = """
          <group layout="column" gap="6" grow="1" visible="{OnRulesPage}">
            <list header="true" selected="{SelectedRuleIndex}" onchange="{SelectRuleRow}" rowheight="18" selectionband="true" grow="1">
              <column type="checkbox" header="On" width="30" values="{RuleOn}" onchange="{ToggleRule}" />
              <column type="text" header="When" width="*" items="{RuleWhen}" colors="{RuleColours}" />
              <column type="text" header="From" width="130" items="{RuleFrom}" colors="{RuleColours}" />
              <column type="text" header="Do" width="150" items="{RuleDo}" colors="{RuleColours}" />
              <contextmenu items="{RuleMenu}" onopen="{RuleMenuOpen}" onselect="{RuleMenuSelect}" />
            </list>
            <label text="{SelectedRuleStatus}" color="#FFFF6B6B" h="16" />
            <group layout="row" gap="6" h="22">
              <button text="+ Rule" onclick="{NewRule}" w="70" h="22" />
              <button text="Standard filters..." onclick="{ShowPresets}" w="130" h="22" />
              <button text="Test a line..." onclick="{ShowTest}" w="100" h="22" />
              <group grow="1" />
              <button text="Edit" onclick="{EditRule}" enabled="{HasSelectedRule}" w="44" h="22" />
              <button text="Up" onclick="{MoveRuleUp}" enabled="{HasSelectedRule}" w="40" h="22" />
              <button text="Down" onclick="{MoveRuleDown}" enabled="{HasSelectedRule}" w="44" h="22" />
            </group>
        """ + RuleFormMarkup + PresetsMarkup + TestMarkup + """
          </group>
        """;
}
```

`RuleFormMarkup`, `PresetsMarkup` and `TestMarkup` and the members `NewRule`, `ShowPresets`, `ShowTest`, `CreateRuleForm`, `ResetPanes`, `StartForm` and `StartTest` come in Tasks 7 and 8. For this task to build, add `ChatDeckEditorBinding.RuleForm.cs` with these stubs; Task 7 replaces the file:

```csharp
using AcDream.Core.Chat.Deck;

namespace AcDream.App.UI.Hud;

public sealed partial class ChatDeckEditorBinding
{
    private void CreateRuleForm() { }
    private void ResetPanes() { }
    private void StartForm(ChatDeckRule rule, string? editingId, ChatDeckLineFacts? fromLine) { _page = ChatDeckEditorPage.Rules; }
    private void StartTest(ChatDeckLineFacts line) { _page = ChatDeckEditorPage.Rules; }
    public Action NewRule => () => { };
    public Action ShowPresets => () => { };
    public Action ShowTest => () => { };
    private const string RuleFormMarkup = "";
    private const string PresetsMarkup = "";
    private const string TestMarkup = "";
}
```

`ChatDeckChannelColours.Argb(Vector4)` exists from plan 4; if its name differs, check `ChatDeckChannelColours.cs`. The plain-dot "Standard filters..." avoids depending on the "…" glyph in the markup font.

- [ ] **Step 4: Run the tests and see them pass.** Run `~/.dotnet/dotnet test tests/AcDream.App.Tests --filter "FullyQualifiedName~ChatDeckEditor"` → PASS, including `Markup_Builds`.

- [ ] **Step 5: Commit**

```bash
git add src/AcDream.App/UI/Hud tests/AcDream.App.Tests/UI/Hud
git commit -m "Chat Deck editor: rules table with row menu, reordering and problem rules in red"
```

---

### Task 7: Editor — the rule form, its preview and rule-from-line

**Goal:** A form under the table that creates or edits one rule (kind, pattern, field, channels, action, target tab, colour, alert), previews it against the session's history, offers the generalise options for a rule written from a line, and applies only on Add / Save.

**Files:**
- Modify: `src/AcDream.App/UI/Hud/ChatDeckEditorBinding.RuleForm.cs` (replace the stubs)
- Test: `tests/AcDream.App.Tests/UI/Hud/ChatDeckEditorRuleFormTests.cs` (create)

**Acceptance Criteria:**
- [ ] *+ Rule* opens the form with a blank Contains / Text / any channel / Hide rule; `SaveText` is "Add" and Add is disabled while the pattern is empty
- [ ] Editing kind, pattern, field, action, target, colour and alert changes only the draft; the model's settings change only on Add / Save. Add appends the rule and selects it; Save replaces the edited rule (keeping its id) and clears `Faulted`; Cancel closes the form and changes nothing
- [ ] Move / Copy need a target: `TargetNames` lists every tab except All, and Add / Save stay disabled until the target is a real tab. The colour swatch shows only for the colour actions, the target menu only for Move / Copy, the pattern only for Contains / Regex, and the kind radio not at all for a standard-filter rule
- [ ] The form's channel picker edits the draft's channels (empty = any channel)
- [ ] `PreviewText` reads "Matches N of the last M lines." from `ChatDeckRulePreview` over the All tab; it reports an invalid pattern's error, a timeout, and "This pattern matches every line." for an empty match. `PreviewSamples` gives up to 3 sample rows. The preview is recomputed only when the draft or the All tab's version changes
- [ ] Opened with `NewRule` + `FromLine`, the form shows the generalise checkboxes (each enabled only when `CanGeneraliseSender` / `CanGeneraliseNumber`) and the marked pattern (`PatternPreview`); ticking one rewrites the draft's kind and pattern from the line

**Verify:** `~/.dotnet/dotnet test tests/AcDream.App.Tests --filter "FullyQualifiedName~ChatDeckEditor"` → all pass

**Steps:**

- [ ] **Step 1: Write the failing tests.** Create `tests/AcDream.App.Tests/UI/Hud/ChatDeckEditorRuleFormTests.cs`:

```csharp
using AcDream.App.UI.Hud;
using AcDream.Core.Chat;
using AcDream.Core.Chat.Deck;

namespace AcDream.App.Tests.UI.Hud;

public sealed class ChatDeckEditorRuleFormTests
{
    private static (ChatLog Log, ChatDeckModel Model, ChatDeckEditorBinding Binding) Make()
    {
        var log = new ChatLog();
        var model = new ChatDeckModel(log, ChatDeckDefaults.Create(), "Davian");
        return (log, model, new ChatDeckEditorBinding(() => model));
    }

    private static void System(ChatLog log, ChatDeckModel model, string text)
    {
        log.OnSystemMessage(text, 0x05u);
        model.Tick();
    }

    [Fact]
    public void NewRule_AddsOnlyOnAdd()
    {
        var (_, m, b) = Make();
        int before = m.Settings.Rules.Count;
        b.NewRule();
        Assert.True(b.FormOpen);
        Assert.Equal("Add", b.SaveText);
        Assert.False(b.CanSave);
        b.SetPattern("loot");
        b.SetDraftAction(0);
        Assert.Equal(before, m.Settings.Rules.Count);
        Assert.True(b.CanSave);
        b.SaveRule();
        Assert.Equal(before + 1, m.Settings.Rules.Count);
        Assert.Equal("loot", m.Settings.Rules[^1].Pattern);
        Assert.False(b.FormOpen);
        Assert.Equal(m.Settings.Rules.Count - 1, b.SelectedRuleIndex);
    }

    [Fact]
    public void EditRule_SavesInPlace_Cancel_ChangesNothing()
    {
        var (_, m, b) = Make();
        m.Edit(s => ChatDeckSettingsEdits.AddRule(s, new ChatDeckRule { Id = "", Kind = ChatDeckMatchKind.Contains, Pattern = "a" }, out _));
        m.Edit(s => s with { Rules = s.Rules.Select(r => r.Id == "rule-1" ? r with { Faulted = true } : r).ToList() });
        b.SelectRuleRow(1);
        b.EditRule();
        Assert.Equal("Save", b.SaveText);
        b.SetPattern("b");
        b.CancelRule();
        Assert.Equal("a", m.Settings.Rules[1].Pattern);

        b.EditRule();
        b.SetPattern("b");
        b.SaveRule();
        Assert.Equal(("rule-1", "b", false), (m.Settings.Rules[1].Id, m.Settings.Rules[1].Pattern, m.Settings.Rules[1].Faulted));
    }

    [Fact]
    public void MoveNeedsATarget_ControlsFollowTheAction()
    {
        var (_, m, b) = Make();
        b.NewRule();
        b.SetPattern("x");
        b.SetDraftAction((int)ChatDeckRuleAction.MoveTo);
        Assert.True(b.ShowsTarget);
        Assert.False(b.ShowsColour);
        Assert.DoesNotContain("All", b.TargetNames);
        Assert.False(b.CanSave);
        b.SetTarget("Combat");
        Assert.True(b.CanSave);
        b.SetDraftAction((int)ChatDeckRuleAction.ColourLine);
        Assert.True(b.ShowsColour);
        Assert.False(b.ShowsTarget);
        b.SetDraftKind(2);   // My name
        Assert.False(b.PatternEditable);
    }

    [Fact]
    public void FormChannels_EditTheDraft()
    {
        var (_, m, b) = Make();
        b.NewRule();
        b.DraftChannelGroups.Single(g => g.Name == "Combat").SetAll(true);
        b.SetPattern("x");
        b.SaveRule();
        Assert.True(ChatDeckChannels.Group("Combat").All(m.Settings.Rules[^1].Channels.Contains));
    }

    [Fact]
    public void Preview_CountsHistory_AndWarns()
    {
        var (log, m, b) = Make();
        System(log, m, "You receive 5 Pyreals.");
        System(log, m, "Something else.");
        b.NewRule();
        b.SetPattern("Pyreals");
        Assert.Equal("Matches 1 of the last 2 lines.", b.PreviewText);
        Assert.Single(b.PreviewSamples);
        b.SetDraftKind(1);   // Regex
        b.SetPattern("(");
        Assert.StartsWith("Invalid: ", b.PreviewText);
        b.SetPattern("z*");
        Assert.Contains("matches every line", b.PreviewText);
    }

    [Fact]
    public void FromLine_GeneraliseRewritesTheDraft()
    {
        var (log, m, b) = Make();
        System(log, m, "You receive 5 Pyreals.");
        ChatDeckLineFacts facts = m.FindTab("all")!.Snapshot()[0].Facts;
        b.Open(new ChatDeckEditorRequest
        {
            Page = ChatDeckEditorPage.Rules,
            NewRule = ChatDeckRuleDraft.FromLine(facts, ChatDeckRuleAction.Hide),
            FromLine = facts,
        });
        Assert.True(b.OnRulesPage);
        Assert.True(b.FromLine);
        Assert.False(b.CanAnySender);
        Assert.True(b.CanAnyNumber);
        b.SetAnyNumber(true);
        Assert.Equal("You receive [any number] Pyreals.", b.PatternPreview);
        Assert.Equal(1, b.DraftKind);   // Regex
        System(log, m, "You receive 12 Pyreals.");
        Assert.Equal("Matches 2 of the last 2 lines.", b.PreviewText);
    }
}
```

Check the system chat type with `grep -n "OnSystemMessage" tests/AcDream.App.Tests/UI/Hud/*.cs`; use whatever type the existing tests pass for a plain system line.

- [ ] **Step 2: Run them and see them fail** (compile errors).

Run: `~/.dotnet/dotnet test tests/AcDream.App.Tests --filter "FullyQualifiedName~ChatDeckEditorRuleForm"`

- [ ] **Step 3: Implement.** Replace `ChatDeckEditorBinding.RuleForm.cs`. It keeps the pane state shared with Task 8. `ShowPresets`, `ShowTest`, `StartTest`, `PresetsMarkup` and `TestMarkup` move to Task 8's files, so leave stubs for those in `ChatDeckEditorBinding.Presets.cs` (`public Action ShowPresets => () => { _pane = Pane.Presets; _page = ChatDeckEditorPage.Rules; }; private const string PresetsMarkup = "";`) and `ChatDeckEditorBinding.Test.cs` (`public Action ShowTest => () => { _pane = Pane.Test; _page = ChatDeckEditorPage.Rules; }; private void StartTest(ChatDeckLineFacts line) => ShowTest(); private void ResetTest() { } private const string TestMarkup = "";`) so this task builds. `ResetPanes` below calls `ResetTest`.

```csharp
using System.Globalization;
using AcDream.Core.Chat.Deck;

namespace AcDream.App.UI.Hud;

public sealed partial class ChatDeckEditorBinding
{
    private enum Pane { None, Form, Presets, Test }

    private Pane _pane;
    private ChatDeckRule? _draft;
    private string? _editingId;
    private ChatDeckLineFacts? _fromLine;
    private bool _anySender, _anyNumber;
    private object? _previewFor;
    private ChatDeckPreviewResult? _preview;

    private void CreateRuleForm()
    {
        DraftChannels = new ChatDeckChannelPicker(
            () => _draft?.Channels ?? ChatDeckRule.AnyChannel,
            set => { if (_draft is not null) _draft = _draft with { Channels = set }; });
    }

    private void ResetPanes()
    {
        _pane = Pane.None;
        _draft = null;
        _editingId = null;
        _fromLine = null;
        _anySender = _anyNumber = false;
        _previewFor = null;
        ResetTest();               // ChatDeckEditorBinding.Test.cs
    }

    private void StartForm(ChatDeckRule rule, string? editingId, ChatDeckLineFacts? fromLine)
    {
        _page = ChatDeckEditorPage.Rules;
        _pane = Pane.Form;
        _draft = rule;
        _editingId = editingId;
        _fromLine = fromLine;
        _anySender = _anyNumber = false;
    }

    public bool FormOpen => _pane == Pane.Form && _draft is not null;
    public string FormTitle => _editingId is null ? "New rule" : "Edit rule";
    public string SaveText => _editingId is null ? "Add" : "Save";

    public Action NewRule => () => StartForm(
        new ChatDeckRule { Id = "", Kind = ChatDeckMatchKind.Contains, Action = ChatDeckRuleAction.Hide },
        editingId: null, fromLine: null);

    private void SetDraft(Func<ChatDeckRule, ChatDeckRule> change) { if (_draft is not null) _draft = change(_draft); }

    public bool IsPresetDraft => _draft?.Kind == ChatDeckMatchKind.Preset;
    public bool KindEditable => FormOpen && !IsPresetDraft;
    public string PresetLabel => IsPresetDraft && _draft is not null ? ChatDeckRuleSummary.When(_draft) : "";
    /// <summary>0 Contains, 1 Regex, 2 My name.</summary>
    public int DraftKind => _draft?.Kind switch
    {
        ChatDeckMatchKind.Regex => 1,
        ChatDeckMatchKind.MyName => 2,
        _ => 0,
    };
    public Action<int> SetDraftKind => v => SetDraft(r => r with
    {
        Kind = v switch { 1 => ChatDeckMatchKind.Regex, 2 => ChatDeckMatchKind.MyName, _ => ChatDeckMatchKind.Contains },
    });

    public bool PatternEditable => _draft?.Kind is ChatDeckMatchKind.Contains or ChatDeckMatchKind.Regex;
    public string Pattern => _draft?.Pattern ?? "";
    public Action<string> SetPattern => v => SetDraft(r => r with { Pattern = v });
    public int DraftField => (int)(_draft?.Field ?? ChatDeckMatchField.Text);
    public Action<int> SetDraftField => v => SetDraft(r => r with { Field = (ChatDeckMatchField)Math.Clamp(v, 0, 2) });

    public ChatDeckChannelPicker DraftChannels { get; private set; } = null!;
    public IReadOnlyList<ChatDeckChannelPicker.GroupRow> DraftChannelGroups => DraftChannels.Groups;
    public bool ChannelsInForm => FormOpen && !IsPresetDraft;

    /// <summary>The action, in <see cref="ChatDeckRuleAction"/> order: Hide, Move to, Also show in, Colour line, Colour match.</summary>
    public int DraftAction => (int)(_draft?.Action ?? ChatDeckRuleAction.Hide);
    public Action<int> SetDraftAction => v => SetDraft(r => r with
    {
        Action = (ChatDeckRuleAction)Math.Clamp(v, 0, 4),
        Colour = r.Colour == 0 ? ChatDeckRuleDraft.DefaultColour : r.Colour,
    });
    public bool ShowsTarget => _draft?.Action is ChatDeckRuleAction.MoveTo or ChatDeckRuleAction.CopyTo;
    public bool ShowsColour => _draft?.Action is ChatDeckRuleAction.ColourLine or ChatDeckRuleAction.ColourMatch;

    public IReadOnlyList<string> TargetNames => S.Tabs.Where(t => t.Id != ChatDeckDefaults.AllTabId).Select(t => t.Name).ToList();
    public string TargetName => S.Tabs.FirstOrDefault(t => t.Id == _draft?.TargetTabId)?.Name ?? "";
    public Action<string> SetTarget => v =>
    {
        if (S.Tabs.FirstOrDefault(t => t.Id != ChatDeckDefaults.AllTabId && t.Name == v) is { } tab)
            SetDraft(r => r with { TargetTabId = tab.Id });
    };

    public string DraftColour => $"#{_draft?.Colour ?? ChatDeckRuleDraft.DefaultColour:X8}";
    public Action<string> SetDraftColour => v =>
    {
        if (uint.TryParse(v.TrimStart('#'), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint argb))
            SetDraft(r => r with { Colour = argb });
    };

    public bool DraftAlert => _draft?.Alert ?? false;
    public Action<bool> SetDraftAlert => v => SetDraft(r => r with { Alert = v });

    // ── rule-from-line ──
    public bool FromLine => FormOpen && _fromLine is not null;
    public bool CanAnySender => _fromLine is { } f && ChatDeckRuleDraft.CanGeneraliseSender(f);
    public bool CanAnyNumber => _fromLine is { } f && ChatDeckRuleDraft.CanGeneraliseNumber(f);
    public string AnySenderText => $"Any name in place of \"{_fromLine?.Sender.Trim()}\"";
    public bool AnySender => _anySender;
    public bool AnyNumber => _anyNumber;
    public Action<bool> SetAnySender => v => { _anySender = v; Regeneralise(); };
    public Action<bool> SetAnyNumber => v => { _anyNumber = v; Regeneralise(); };
    public string PatternPreview => _fromLine is { } f ? ChatDeckRuleDraft.Preview(f, _anySender, _anyNumber) : "";

    private void Regeneralise()
    {
        if (_fromLine is not { } f) return;
        (ChatDeckMatchKind kind, string pattern) = ChatDeckRuleDraft.Pattern(f, _anySender, _anyNumber);
        SetDraft(r => r with { Kind = kind, Pattern = pattern });
    }

    // ── preview ──
    private ChatDeckPreviewResult? Preview
    {
        get
        {
            if (!FormOpen || M is not { } m || m.FindTab(ChatDeckDefaults.AllTabId) is not { } all) return null;
            var key = (_draft, all, all.Version);
            if (Equals(key, _previewFor)) return _preview;
            _previewFor = key;
            _preview = ChatDeckRulePreview.Run(_draft!, all.Snapshot(), m.CharacterName);
            return _preview;
        }
    }

    public string PreviewText => Preview switch
    {
        null => "",
        { Status: not ChatDeckRuleStatus.Ok } p => p.Error is { } e ? $"Invalid: {e}" : ChatDeckRuleSummary.Status(ChatDeckMatcher.Compile(_draft!, M?.CharacterName ?? "")),
        { TimedOut: true } p => $"Too slow: it timed out on a line after {p.Count} matches. It would be turned off.",
        { MatchesEverything: true } => "This pattern matches every line.",
        var p => $"Matches {p.Count} of the last {p.Total} lines.",
    };
    public uint PreviewColour => Preview is { Status: ChatDeckRuleStatus.Ok, TimedOut: false, MatchesEverything: false }
        ? 0xFF000000u | InkColour : 0xFF000000u | ProblemColour;

    public sealed record SampleRow(string Text);
    public IReadOnlyList<SampleRow> PreviewSamples => Preview?.Samples.Select(s => new SampleRow(s)).ToList() ?? [];

    // ── save ──
    public bool CanSave => _draft is { } d && Preview is { Status: ChatDeckRuleStatus.Ok }
        && (!ChatDeckSettingsEdits.Targets(d) || S.Tabs.Any(t => t.Id == d.TargetTabId && t.Id != ChatDeckDefaults.AllTabId));

    public Action SaveRule => () =>
    {
        if (!CanSave || _draft is not { } draft) return;
        if (_editingId is { } id)
        {
            Edit(s => ChatDeckSettingsEdits.UpdateRule(s, id, draft));
            _ruleId = id;
        }
        else
        {
            string? added = null;
            if (Edit(s => ChatDeckSettingsEdits.AddRule(s, draft with { Enabled = true }, out added))) _ruleId = added;
        }
        CloseForm();
    };

    public Action CancelRule => CloseForm;

    private void CloseForm()
    {
        _pane = Pane.None;
        _draft = null;
        _editingId = null;
        _fromLine = null;
        _previewFor = null;
    }

    private const string RuleFormMarkup = """
            <group layout="column" gap="5" h="270" visible="{FormOpen}">
              <separator h="1" />
              <label text="{FormTitle}" h="16" />
              <label text="{PresetLabel}" visible="{IsPresetDraft}" h="16" />
              <group layout="row" gap="8" align="center" h="22" visible="{KindEditable}">
                <radio items="Contains|Regex|My name" selected="{DraftKind}" onchange="{SetDraftKind}" orientation="horizontal" style="segmented" w="240" h="20" />
                <radio items="Text|Sender|Whole line" selected="{DraftField}" onchange="{SetDraftField}" orientation="horizontal" style="segmented" grow="1" h="20" />
              </group>
              <field text="{Pattern}" onchange="{SetPattern}" maxlength="256" h="20" visible="{PatternEditable}" />
              <group layout="row" gap="12" h="16" visible="{FromLine}">
                <checkbox text="{AnySenderText}" checked="{AnySender}" onchange="{SetAnySender}" enabled="{CanAnySender}" grow="1" h="16" />
                <checkbox text="Any number" checked="{AnyNumber}" onchange="{SetAnyNumber}" enabled="{CanAnyNumber}" w="120" h="16" />
              </group>
              <label text="{PatternPreview}" visible="{FromLine}" h="16" />
              <group layout="row" gap="8" h="96">
                <group layout="column" gap="2" scroll="y" w="200" visible="{ChannelsInForm}">
                  <label text="Channels (none = any)" h="16" />
                  <repeat items="{DraftChannelGroups}" key="{Name}">
                    <checkbox text="{Name}" checked="{All}" onchange="{SetAll}" h="16" />
                    <repeat items="{Channels}" key="{Id}">
                      <group layout="row" h="16">
                        <group w="18" />
                        <checkbox text="{Name}" checked="{On}" onchange="{SetOn}" grow="1" h="16" />
                      </group>
                    </repeat>
                  </repeat>
                </group>
                <group layout="column" gap="5" grow="1">
                  <radio items="Hide|Move to|Also show in|Colour line|Colour match" selected="{DraftAction}" onchange="{SetDraftAction}" orientation="horizontal" style="segmented" h="20" />
                  <group layout="row" gap="8" align="center" h="22" visible="{ShowsTarget}">
                    <label text="Tab" w="40" />
                    <menu items="{TargetNames}" selected="{TargetName}" onchange="{SetTarget}" rows="15" grow="1" h="20" />
                  </group>
                  <group layout="row" gap="8" align="center" h="22" visible="{ShowsColour}">
                    <label text="Colour" w="40" />
                    <color value="{DraftColour}" onchange="{SetDraftColour}" w="40" h="16" />
                  </group>
                  <checkbox text="Alert (red badge, peek, beep)" checked="{DraftAlert}" onchange="{SetDraftAlert}" h="16" />
                </group>
              </group>
              <label text="{PreviewText}" color="{PreviewColour}" h="16" />
              <group layout="column" gap="1" h="50">
                <repeat items="{PreviewSamples}">
                  <label text="{Text}" h="16" />
                </repeat>
              </group>
              <group layout="row" gap="8" h="22">
                <group grow="1" />
                <button text="{SaveText}" onclick="{SaveRule}" enabled="{CanSave}" w="64" h="22" />
                <button text="Cancel" onclick="{CancelRule}" w="64" h="22" />
              </group>
            </group>
        """;
}
```

`ChatDeckEditorBinding.Rules.cs` already has `EditRule`. Drop the stubs this task replaces from `RuleForm.cs`.

The model's `CharacterName` property exists (plan 1). `ChatDeckTabState.Version` changes on every add and clear, so a new line refreshes the preview. Keying on the draft record works because every change makes a new record, and a new channel set makes a new set instance.

If a markup attribute fails to build (an unknown attribute on `radio`, or `button text` refusing a binding), check `docs/plugin-ui-markup.md`. `button text` takes a literal or binding like any text attribute; if it does not, use two buttons with `visible="{IsNew}"` / `visible="{IsEdit}"`.

- [ ] **Step 4: Run the tests and see them pass.** Run `~/.dotnet/dotnet test tests/AcDream.App.Tests --filter "FullyQualifiedName~ChatDeckEditor"` → PASS, including `Markup_Builds`.

- [ ] **Step 5: Commit**

```bash
git add src/AcDream.App/UI/Hud tests/AcDream.App.Tests/UI/Hud
git commit -m "Chat Deck editor: rule form with live preview and rule-from-line generalising"
```

---

### Task 8: Editor — standard filters and Test a line

**Goal:** A grouped checklist of the standard filters (presets), and a test box that shows what the applied rules would do with a typed line or one sent from the transcript.

**Files:**
- Modify: `src/AcDream.App/UI/Hud/ChatDeckEditorBinding.Presets.cs` (replace the stub)
- Modify: `src/AcDream.App/UI/Hud/ChatDeckEditorBinding.Test.cs` (replace the stub)
- Test: `tests/AcDream.App.Tests/UI/Hud/ChatDeckEditorPresetsTests.cs` (create)

**Acceptance Criteria:**
- [ ] `PresetGroups` lists the groups in `ChatDeckPresets.GroupOrder`, each with its presets (id, label, description as tooltip); `On` is true when an enabled preset rule exists; `SetOn` goes through `SetPresetEnabled`
- [ ] *Standard filters...* shows the presets pane and hides the form; *Test a line...* shows the test pane; *Done* hides either
- [ ] The test box has a channel menu (every channel's display name), a sender field and a text field; `TestResult` gives `ChatDeckVerdictText.Lines` for `model.Test(facts)`, recomputed only when the inputs or the settings change; a hidden verdict's first row is red
- [ ] `Open` with `TestLine` shows the test pane on the Rules page, filled with that line's channel, sender and text, and uses the real facts until a field is edited; then it uses `ChatDeckTestLine.Facts`

**Verify:** `~/.dotnet/dotnet test tests/AcDream.App.Tests --filter "FullyQualifiedName~ChatDeckEditor"` → all pass

**Steps:**

- [ ] **Step 1: Write the failing tests.** Create `tests/AcDream.App.Tests/UI/Hud/ChatDeckEditorPresetsTests.cs`:

```csharp
using AcDream.App.UI.Hud;
using AcDream.Core.Chat;
using AcDream.Core.Chat.Deck;

namespace AcDream.App.Tests.UI.Hud;

public sealed class ChatDeckEditorPresetsTests
{
    [Fact]
    public void Presets_GroupedAndToggled()
    {
        var (m, b) = ChatDeckEditorTabsTests.Make();
        Assert.Equal(ChatDeckPresets.GroupOrder, b.PresetGroups.Select(g => g.Name));
        ChatDeckEditorBinding.PresetRow row = b.PresetGroups[0].Presets[0];
        Assert.False(row.On);
        row.SetOn(true);
        Assert.True(row.On);
        Assert.Contains(m.Settings.Rules, r => r.Kind == ChatDeckMatchKind.Preset && r.PresetId == row.Id && r.Enabled);
        row.SetOn(false);
        Assert.False(row.On);
    }

    [Fact]
    public void Panes_SwitchAndClose()
    {
        var (_, b) = ChatDeckEditorTabsTests.Make();
        b.NewRule();
        b.ShowPresets();
        Assert.True(b.PresetsOpen);
        Assert.False(b.FormOpen);
        b.ShowTest();
        Assert.True(b.TestOpen);
        Assert.False(b.PresetsOpen);
        b.ClosePane();
        Assert.False(b.TestOpen);
    }

    [Fact]
    public void TestBox_TypedLine_UsesAppliedRules()
    {
        var (m, b) = ChatDeckEditorTabsTests.Make();
        m.Edit(s => ChatDeckSettingsEdits.AddRule(s,
            new ChatDeckRule { Id = "", Kind = ChatDeckMatchKind.Contains, Pattern = "spam", Action = ChatDeckRuleAction.Hide }, out _));
        b.ShowTest();
        b.SetTestChannel("General");
        b.SetTestSender("Bob");
        b.SetTestText("buy spam");
        Assert.Equal("Hidden by Rule 2 (Text contains \"spam\")", b.TestResult[0].Text);
        Assert.Equal(0xFF000000u | ChatDeckEditorBinding.ProblemColour, b.TestResult[0].Colour);
        b.SetTestText("hello");
        Assert.Equal("Goes to: All, Social", b.TestResult[0].Text);
    }

    [Fact]
    public void TestBox_RealLine_KeepsFactsUntilEdited()
    {
        var log = new ChatLog();
        var m = new ChatDeckModel(log, ChatDeckDefaults.Create(), "Davian");
        var b = new ChatDeckEditorBinding(() => m);
        log.OnTellReceived("Bob", "psst", 0x50000002u, 0x50000001u);   // check the real signature; any incoming tell
        m.Tick();
        ChatDeckLineFacts facts = m.FindTab("all")!.Snapshot()[^1].Facts;
        b.Open(new ChatDeckEditorRequest { Page = ChatDeckEditorPage.Rules, TestLine = facts });
        Assert.True(b.TestOpen);
        Assert.Equal("Tells received", b.TestChannel);
        Assert.Equal("Bob", b.TestSender);
        Assert.True(b.TestFromTranscript);
        Assert.Contains(b.TestResult, r => r.Text == "Alert: incoming tell");
        b.SetTestText("changed");
        Assert.False(b.TestFromTranscript);
    }
}
```

Check `ChatLog.OnTellReceived`'s parameters with `sed -n 148,165p src/AcDream.Core/Chat/ChatLog.cs` and pass an incoming tell from Bob.

- [ ] **Step 2: Run them and see them fail** (compile errors).

Run: `~/.dotnet/dotnet test tests/AcDream.App.Tests --filter "FullyQualifiedName~ChatDeckEditorPresets"`

- [ ] **Step 3: Implement.** Replace `ChatDeckEditorBinding.Presets.cs`:

```csharp
using AcDream.Core.Chat.Deck;

namespace AcDream.App.UI.Hud;

public sealed partial class ChatDeckEditorBinding
{
    private IReadOnlyList<PresetGroup>? _presetGroups;

    public IReadOnlyList<PresetGroup> PresetGroups => _presetGroups ??= ChatDeckPresets.GroupOrder
        .Select(g => new PresetGroup(g, ChatDeckPresets.All.Where(p => p.Group == g).Select(p => new PresetRow(this, p)).ToList()))
        .ToList();

    public bool PresetsOpen => _pane == Pane.Presets;
    public Action ShowPresets => () => { _pane = Pane.Presets; _page = ChatDeckEditorPage.Rules; };
    public Action ClosePane => () => _pane = Pane.None;

    private bool PresetOn(string id) => S.Rules.Any(r => r.Kind == ChatDeckMatchKind.Preset && r.PresetId == id && r.Enabled);

    public sealed record PresetGroup(string Name, IReadOnlyList<PresetRow> Presets);

    public sealed class PresetRow(ChatDeckEditorBinding owner, ChatDeckPreset preset)
    {
        public string Id => preset.Id;
        public string Label => preset.Label;
        public string Description => preset.Description;
        public bool On => owner.PresetOn(preset.Id);
        public Action<bool> SetOn => on => owner.Edit(s => ChatDeckSettingsEdits.SetPresetEnabled(s, preset.Id, on));
    }

    private const string PresetsMarkup = """
            <group layout="column" gap="4" h="270" visible="{PresetsOpen}">
              <separator h="1" />
              <group layout="row" gap="8" align="center" h="22">
                <label text="Standard filters (a ticked filter hides its lines; edit its rule to colour or move them instead)" grow="1" />
                <button text="Done" onclick="{ClosePane}" w="60" h="22" />
              </group>
              <group layout="column" gap="2" scroll="y" grow="1">
                <repeat items="{PresetGroups}" key="{Name}">
                  <label text="{Name}" h="16" />
                  <repeat items="{Presets}" key="{Id}">
                    <group layout="row" h="16">
                      <group w="14" />
                      <checkbox text="{Label}" tooltip="{Description}" checked="{On}" onchange="{SetOn}" grow="1" h="16" />
                    </group>
                  </repeat>
                </repeat>
              </group>
            </group>
        """;
}
```

Replace `ChatDeckEditorBinding.Test.cs`:

```csharp
using AcDream.Core.Chat.Deck;

namespace AcDream.App.UI.Hud;

public sealed partial class ChatDeckEditorBinding
{
    private ChatDeckChannel _testChannel = ChatDeckChannel.Say;
    private string _testSender = "";
    private string _testText = "";
    private ChatDeckLineFacts? _testFacts;   // a real line from the transcript, until a field is edited
    private object? _testFor;
    private IReadOnlyList<ResultRow> _testResult = [];

    private void ResetTest()
    {
        _testChannel = ChatDeckChannel.Say;
        _testSender = _testText = "";
        _testFacts = null;
        _testFor = null;
    }

    private void StartTest(ChatDeckLineFacts line)
    {
        _page = ChatDeckEditorPage.Rules;
        _pane = Pane.Test;
        _testFacts = line;
        _testChannel = line.Channel;
        _testSender = line.Sender;
        _testText = line.Text;
    }

    public bool TestOpen => _pane == Pane.Test;
    public Action ShowTest => () => { _pane = Pane.Test; _page = ChatDeckEditorPage.Rules; };
    public bool TestFromTranscript => _testFacts is not null;

    public IReadOnlyList<string> ChannelNames { get; } =
        Enum.GetValues<ChatDeckChannel>().Select(ChatDeckChannels.DisplayName).ToList();
    public string TestChannel => ChatDeckChannels.DisplayName(_testChannel);
    public Action<string> SetTestChannel => v =>
    {
        foreach (ChatDeckChannel c in Enum.GetValues<ChatDeckChannel>())
            if (ChatDeckChannels.DisplayName(c) == v) { _testChannel = c; _testFacts = null; }
    };
    public string TestSender => _testSender;
    public Action<string> SetTestSender => v => { _testSender = v; _testFacts = null; };
    public string TestText => _testText;
    public Action<string> SetTestText => v => { _testText = v; _testFacts = null; };

    public sealed record ResultRow(string Text, uint Colour);

    public IReadOnlyList<ResultRow> TestResult
    {
        get
        {
            if (M is not { } m || (_testFacts is null && _testText.Length == 0)) return [];
            var key = (_testFacts, _testChannel, _testSender, _testText, m.Settings, m.CompiledRules);
            if (Equals(key, _testFor)) return _testResult;
            _testFor = key;
            ChatDeckLineFacts facts = _testFacts ?? ChatDeckTestLine.Facts(_testChannel, _testSender, _testText, m.CharacterName);
            ChatDeckVerdict verdict = m.Test(facts);
            uint ink = 0xFF000000u | InkColour, red = 0xFF000000u | ProblemColour;
            _testResult = ChatDeckVerdictText.Lines(verdict, m.Settings)
                .Select((text, i) => new ResultRow(text, i == 0 && verdict.Hidden ? red : ink)).ToList();
            return _testResult;
        }
    }

    private const string TestMarkup = """
            <group layout="column" gap="4" h="270" visible="{TestOpen}">
              <separator h="1" />
              <group layout="row" gap="8" align="center" h="22">
                <label text="Test a line against the rules as they are now" grow="1" />
                <button text="Done" onclick="{ClosePane}" w="60" h="22" />
              </group>
              <group layout="row" gap="8" align="center" h="22">
                <label text="Channel" w="60" />
                <menu items="{ChannelNames}" selected="{TestChannel}" onchange="{SetTestChannel}" rows="16" w="160" h="20" />
                <label text="Sender" w="50" />
                <field text="{TestSender}" onchange="{SetTestSender}" maxlength="40" grow="1" h="20" />
              </group>
              <field text="{TestText}" onchange="{SetTestText}" maxlength="2000" h="20" />
              <label text="(the line from the transcript)" visible="{TestFromTranscript}" h="16" />
              <group layout="column" gap="1" scroll="y" grow="1">
                <repeat items="{TestResult}">
                  <label text="{Text}" color="{Colour}" h="16" />
                </repeat>
              </group>
            </group>
        """;
}
```

Remove the `ShowPresets`, `ShowTest` and `StartTest` stubs from wherever Task 7 left them, so each member is defined once. Opening the form (`StartForm`) and *+ Rule* set `_pane = Pane.Form`, so the panes replace one another.

- [ ] **Step 4: Run the tests and see them pass.** Run `~/.dotnet/dotnet test tests/AcDream.App.Tests --filter "FullyQualifiedName~ChatDeckEditor"` → PASS, including `Markup_Builds`.

- [ ] **Step 5: Commit**

```bash
git add src/AcDream.App/UI/Hud tests/AcDream.App.Tests/UI/Hud
git commit -m "Chat Deck editor: standard filters checklist and Test a line"
```

---

### Task 9: Deck — ⧩ stud, `+` tab button, the full tab menu and rename in place

**Goal:** The deck opens the editor from a new ⧩ stud, adds a tab from a `+` button after the tabs, and its tab menu gains Rename (in place), Edit tab… and Delete.

**Files:**
- Create: `assets/hud/filters.svg`
- Modify: `src/AcDream.App/UI/Hud/EmbeddedSvgSpriteCache.cs` (`HudIcons.Filters`, `All`)
- Modify: `src/AcDream.App/UI/Hud/ChatDeckGeometry.cs` (zones `FiltersStud`, `AddTab`; `canAddTab`)
- Modify: `src/AcDream.App/UI/Hud/UiChatDeck.cs` (bindings record, hit zones, `OnEvent`, tab menu)
- Modify: `src/AcDream.App/UI/Hud/UiChatDeck.Draw.cs` (draw the stud and `+`)
- Create: `src/AcDream.App/UI/Hud/UiChatDeck.TabEdit.cs` (tab menu actions, rename field, `+`)
- Test: `tests/AcDream.App.Tests/UI/Hud/ChatDeckGeometryTests.cs`, `tests/AcDream.App.Tests/UI/Hud/UiChatDeckTests.cs` (harness + existing tab menu tests), `tests/AcDream.App.Tests/UI/Hud/UiChatDeckTabEditTests.cs` (create)

**Acceptance Criteria:**
- [ ] The studs from right to left are collapse, settings, filters, search, with the same size and gap; `HitAt` reports `FiltersStud` and `AddTab`
- [ ] With `canAddTab`, a `+` button (stud-sized) sits after the last tab when there is room; the tabs leave room for it. Without it (16 tabs), there is no `+`
- [ ] Pressing ⧩ calls `ToggleEditor`; the stud is lit while `EditorOpen()` is true
- [ ] Pressing `+` adds a tab "New tab" through `ChatDeckModel.Edit`, selects it, and asks the editor to open on it (Tabs page)
- [ ] The tab menu is `Rename|Edit tab…|-|Mark as read|Mark all read|-|Delete`, with Delete disabled on All; existing tests that chose "Mark as read" / "Mark all read" by index are updated to the new indexes
- [ ] Edit tab… opens the editor on that tab; Delete opens it on that tab with `ConfirmDeleteTab`
- [ ] Rename shows a field over the tab with the name selected; Enter or losing focus applies a non-blank name (through `UpdateTab`), Esc cancels, and the field hides either way
- [ ] `HudIcons.All` includes `Filters` and the embedded `filters.svg` loads (the existing icon test covers every name in `All`)

**Verify:** `~/.dotnet/dotnet test tests/AcDream.App.Tests --filter "FullyQualifiedName~ChatDeck|FullyQualifiedName~HudIcon|FullyQualifiedName~EmbeddedSvg"` → all pass

**Steps:**

- [ ] **Step 1: Write the failing tests.** In `ChatDeckGeometryTests.cs`, follow the file's `Create(...)` calls and add:

```csharp
[Fact]
public void Studs_RightToLeft_CollapseSettingsFiltersSearch()
{
    ChatDeckGeometry g = ChatDeckGeometry.Create(460f, 220f, [], false, "Say", s => s.Length * 7f);
    Assert.True(g.CollapseStud.X > g.SettingsStud.X && g.SettingsStud.X > g.FiltersStud.X && g.FiltersStud.X > g.SearchStud.X);
    Assert.Equal(ChatDeckGeometry.StudSize + ChatDeckGeometry.StudGap, g.SettingsStud.X - g.FiltersStud.X);
    Assert.Equal(ChatDeckZone.FiltersStud, g.HitAt(g.FiltersStud.X + 2, g.FiltersStud.Y + 2).Zone);
}

[Fact]
public void AddTab_AfterTheLastTab_OnlyWhenAllowed()
{
    var tabs = new[] { new ChatDeckTabLabel("All", ""), new ChatDeckTabLabel("Local", "") };
    ChatDeckGeometry g = ChatDeckGeometry.Create(460f, 220f, tabs, false, "Say", s => s.Length * 7f, canAddTab: true);
    Assert.Equal(ChatDeckGeometry.StudSize, g.AddTab.W);
    Assert.True(g.AddTab.X >= g.Tabs[^1].X + g.Tabs[^1].W);
    Assert.Equal(ChatDeckZone.AddTab, g.HitAt(g.AddTab.X + 2, g.AddTab.Y + 2).Zone);
    Assert.Equal(0f, ChatDeckGeometry.Create(460f, 220f, tabs, false, "Say", s => s.Length * 7f).AddTab.W);
}
```

In `UiChatDeckTests.Harness`, add fields `public readonly List<ChatDeckEditorRequest> EditorRequests = []; public int EditorToggles; public bool EditorIsOpen;` and pass `OpenEditor: EditorRequests.Add, ToggleEditor: () => EditorToggles++, EditorOpen: () => EditorIsOpen` to `ChatDeckBindings`. Find every existing call of `ChooseTabMenuItem(` / `TabMenuItemsAt(` with `grep -rn "ChooseTabMenuItem\|TabMenuItemsAt" tests/AcDream.App.Tests` and change index `0` (Mark as read) to `3` and `1` (Mark all read) to `4`. Change any assertion of the old two-item list to the new seven-item list.

Create `tests/AcDream.App.Tests/UI/Hud/UiChatDeckTabEditTests.cs`. The deck reacts to `MouseDown` with local coordinates, as the existing tests press studs (follow their helper, for example `PressDeck` in `ChatDeckWiringTests` or a direct `OnEvent(new UiEvent(...))`):

```csharp
using AcDream.App.UI;
using AcDream.App.UI.Hud;
using AcDream.Core.Chat.Deck;

namespace AcDream.App.Tests.UI.Hud;

public sealed class UiChatDeckTabEditTests
{
    private static void Press(UiChatDeck deck, DeckRect r) =>
        deck.OnEvent(new UiEvent(0, deck, UiEventType.MouseDown, 0, (int)(r.X + 2), (int)(r.Y + 2)));   // match the existing press helper's UiEvent shape

    [Fact]
    public void FiltersStud_TogglesTheEditor_AndLights()
    {
        var h = new UiChatDeckTests.Harness();
        Press(h.Deck, h.Deck.Geometry.FiltersStud);
        Assert.Equal(1, h.EditorToggles);
    }

    [Fact]
    public void Plus_AddsSelectsAndOpensTheEditorOnIt()
    {
        var h = new UiChatDeckTests.Harness();
        Press(h.Deck, h.Deck.Geometry.AddTab);
        Assert.Equal("tab-1", h.Model!.SelectedTabId);
        ChatDeckEditorRequest r = Assert.Single(h.EditorRequests);
        Assert.Equal((ChatDeckEditorPage.Tabs, "tab-1"), (r.Page, r.TabId));
    }

    [Fact]
    public void TabMenu_ItemsAndDeleteOnAll()
    {
        var h = new UiChatDeckTests.Harness();
        DeckRect all = h.Deck.Geometry.Tabs[0];
        Assert.Equal(["Rename", "Edit tab…", "-", "Mark as read", "Mark all read", "-", "Delete"],
            h.Deck.TabMenuItemsAt(all.X + 2, all.Y + 2));
        Assert.False(h.Deck.TabMenu.ItemsEnabledSource!()[6]);

        DeckRect group = h.Deck.Geometry.Tabs[3];
        h.Deck.TabMenuItemsAt(group.X + 2, group.Y + 2);
        Assert.True(h.Deck.TabMenu.ItemsEnabledSource!()[6]);
        h.Deck.ChooseTabMenuItem(6);
        ChatDeckEditorRequest del = Assert.Single(h.EditorRequests);
        Assert.Equal(("group", true), (del.TabId, del.ConfirmDeleteTab));
        h.Deck.ChooseTabMenuItem(1);
        Assert.Equal(("group", false), (h.EditorRequests[1].TabId, h.EditorRequests[1].ConfirmDeleteTab));
    }

    [Fact]
    public void Rename_EnterApplies_EscCancels()
    {
        var h = new UiChatDeckTests.Harness();
        DeckRect local = h.Deck.Geometry.Tabs[1];
        h.Deck.TabMenuItemsAt(local.X + 2, local.Y + 2);
        h.Deck.ChooseTabMenuItem(0);
        Assert.True(h.Deck.RenameField.Visible);
        Assert.Equal("Local", h.Deck.RenameField.Text);
        h.Deck.RenameField.OnSubmit!("Near");
        Assert.Equal("Near", h.Model!.Settings.Tabs[1].Name);
        Assert.False(h.Deck.RenameField.Visible);

        h.Deck.TabMenuItemsAt(local.X + 2, local.Y + 2);
        h.Deck.ChooseTabMenuItem(0);
        h.Deck.RenameField.OnEscape!();
        h.Deck.RenameField.OnFocusLost?.Invoke("Ignored");
        Assert.Equal("Near", h.Model.Settings.Tabs[1].Name);
        Assert.False(h.Deck.RenameField.Visible);
    }
}
```

- [ ] **Step 2: Run them and see them fail** (compile errors).

Run: `~/.dotnet/dotnet test tests/AcDream.App.Tests --filter "FullyQualifiedName~ChatDeck"`

- [ ] **Step 3: Implement.**

Create `assets/hud/filters.svg` (white strokes on a 24-unit box, like `settings.svg`):

```xml
<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 24 24"><path d="M3.5 4.5h17l-6.5 8.2v5.8l-4 2.2v-8z" fill="none" stroke="#fff" stroke-width="2.4" stroke-linejoin="round"/></svg>
```

In `HudIcons`, add `public const string Filters = "filters";` and put `Filters` after `Settings` in `All`.

In `ChatDeckGeometry.cs`: add `FiltersStud` and `AddTab` to `ChatDeckZone`, add `public required DeckRect FiltersStud { get; init; }` and `public required DeckRect AddTab { get; init; }`, and change `Create`:

```csharp
public static ChatDeckGeometry Create(
    float width, float height, IReadOnlyList<ChatDeckTabLabel> tabs, bool searchOpen, string chipText,
    Func<string, float> measureLabel, bool canAddTab = false)
{
    var collapse = new DeckRect(width - Pad - StudSize, Pad, StudSize, StudSize);
    var settings = collapse with { X = collapse.X - StudGap - StudSize };
    var filters = settings with { X = settings.X - StudGap - StudSize };
    var stud = filters with { X = filters.X - StudGap - StudSize };   // search
    float tabsRight = stud.X - TabGap - (canAddTab ? StudSize + TabGap : 0f);
    // ... the existing room / natural / shared / rects code, unchanged, using tabsRight ...

    DeckRect add = default;
    if (canAddTab)
    {
        float after = rects.Where(r => r.W > 0).Select(r => r.X + r.W + TabGap).DefaultIfEmpty(Pad).Max();
        add = new DeckRect(after, Pad, StudSize, StudSize);
    }
    // ... rest unchanged; set FiltersStud = filters, AddTab = add in the initializer ...
}
```

In `HitAt`, after the tabs loop: `if (AddTab.W > 0 && AddTab.Contains(x, y)) return new(ChatDeckZone.AddTab);`, and after the search stud: `if (FiltersStud.Contains(x, y)) return new(ChatDeckZone.FiltersStud);`.

In `UiChatDeck.cs`:
- Add to `ChatDeckBindings`: `Action<ChatDeckEditorRequest>? OpenEditor = null, Action? ToggleEditor = null, Func<bool>? EditorOpen = null` (after `SettingsOpen`).
- `IsControl` includes `ChatDeckZone.FiltersStud or ChatDeckZone.AddTab`.
- In `Refresh`, pass `canAddTab: _model is { } m && m.Settings.Tabs.Count < ChatDeckLimits.MaxTabs` to `ChatDeckGeometry.Create`, and call `LayoutRenameField();` after `LayoutInputRow();`.
- In `OnEvent` `MouseDown`: `case ChatDeckZone.FiltersStud: _b.ToggleEditor?.Invoke(); return true;` and `case ChatDeckZone.AddTab: AddTabAndEdit(); return true;`.
- Replace the tab menu items and choice:

```csharp
private static readonly string[] TabMenuItems = ["Rename", "Edit tab…", "-", "Mark as read", "Mark all read", "-", "Delete"];
```

In the constructor, after `_tabMenu.ItemsSource = …`:

```csharp
_tabMenu.ItemsEnabledSource = () => TabMenuItems
    .Select(item => item != "Delete" || (_model is { } m && _menuTab > 0 && _menuTab < m.Settings.Tabs.Count))
    .ToArray();
```

(All is always the first tab, so `_menuTab > 0` is "not All".) Replace `ChooseTabMenuItem`:

```csharp
internal void ChooseTabMenuItem(int index)
{
    if (_model is null || _menuTab < 0 || _menuTab >= _model.Settings.Tabs.Count || index < 0 || index >= TabMenuItems.Length) return;
    string tabId = _model.Settings.Tabs[_menuTab].Id;
    switch (TabMenuItems[index])
    {
        case "Rename": StartRename(_menuTab); break;                                    // UiChatDeck.TabEdit.cs
        case "Edit tab…": _b.OpenEditor?.Invoke(new ChatDeckEditorRequest { Page = ChatDeckEditorPage.Tabs, TabId = tabId }); break;
        case "Mark as read": _model.FindTab(tabId)?.MarkRead(); break;
        case "Mark all read": _model.MarkAllRead(); break;
        case "Delete":
            if (tabId != ChatDeckDefaults.AllTabId)
                _b.OpenEditor?.Invoke(new ChatDeckEditorRequest { Page = ChatDeckEditorPage.Tabs, TabId = tabId, ConfirmDeleteTab = true });
            break;
    }
}
```

Create `src/AcDream.App/UI/Hud/UiChatDeck.TabEdit.cs`:

```csharp
using AcDream.Core.Chat.Deck;

namespace AcDream.App.UI.Hud;

internal sealed partial class UiChatDeck
{
    private string? _renaming;   // the tab id being renamed in place

    internal UiField RenameField { get; private set; } = null!;

    /// <summary>Called from the constructor, after the input row.</summary>
    private void CreateRenameField()
    {
        RenameField = new UiField
        {
            OneLine = true, Selectable = true, Keyboard = _b.Keyboard,
            BackgroundColor = ChatDeckPalette.FieldFill, TextColor = ChatDeckPalette.TextInk,
            ClearOnSubmit = false, RecordHistory = false, Visible = false,
            MaxCharacters = ChatDeckLimits.MaxTabNameLength,
        };
        RenameField.OnSubmit = text => FinishRename(text);
        RenameField.OnFocusLost = text => FinishRename(text);
        RenameField.OnEscape = () => FinishRename(null);
        AddChild(RenameField);
    }

    private void StartRename(int tabIndex)
    {
        if (_model is null || tabIndex < 0 || tabIndex >= _model.Settings.Tabs.Count) return;
        _renaming = _model.Settings.Tabs[tabIndex].Id;
        RenameField.SetText(_model.Settings.Tabs[tabIndex].Name);
        RenameField.Visible = true;
        LayoutRenameField();
        FindRoot()?.SetKeyboardFocus(RenameField);
        RenameField.SelectAllText();
    }

    /// <summary>Applies a non-blank name (null cancels) and hides the field; a second call does nothing.</summary>
    private void FinishRename(string? text)
    {
        if (_renaming is not { } id) return;
        _renaming = null;
        RenameField.Visible = false;
        if (FindRoot() is { } root && ReferenceEquals(root.KeyboardFocus, RenameField)) root.SetKeyboardFocus(null);
        if (text is { } name && name.Trim().Length > 0)
            _model?.Edit(s => ChatDeckSettingsEdits.UpdateTab(s, id, t => t with { Name = name.Trim() }));
    }

    private void LayoutRenameField()
    {
        if (_renaming is not { } id || _model is null) return;
        int i = _model.Settings.Tabs.ToList().FindIndex(t => t.Id == id);
        if (i < 0 || i >= _geometry.Tabs.Count || _geometry.Tabs[i].W <= 0)
        {
            FinishRename(null);
            return;
        }
        DeckRect r = _geometry.Tabs[i];
        Place(RenameField, r with { W = MathF.Max(r.W, 90f) });
        RenameField.DatFont = _fonts?.Face(LabelPixels, BundledUiFontWeight.Regular);
    }

    private void AddTabAndEdit()
    {
        if (_model is null) return;
        string? id = null;
        if (!_model.Edit(s => ChatDeckSettingsEdits.AddTab(s, "New tab", out id)) || id is null) return;
        _model.SelectTab(id);
        ResetView();
        _b.OpenEditor?.Invoke(new ChatDeckEditorRequest { Page = ChatDeckEditorPage.Tabs, TabId = id });
    }
}
```

Call `CreateRenameField();` in the constructor after `CreateInputRow();`, and in `OnHidden` call `FinishRename(null);`. If `UiField` has no `SelectAllText` (the input uses it in `EnterChatMode`, so it should), use what `EnterChatMode` uses.

In `UiChatDeck.Draw.cs`, after the search stud:

```csharp
DrawStud(ctx, _geometry.FiltersStud, HudIcons.Filters, _b.EditorOpen?.Invoke() == true);
if (_geometry.AddTab.W > 0)
{
    DeckRect add = _geometry.AddTab;
    ctx.FillRoundedRect(add.X, add.Y, add.W, add.H, 5f, ChatDeckPalette.StudOff);
    float w = LabelBold.Measure(ctx, "+");
    DrawLabel(ctx, LabelBold, "+", add.X + (add.W - w) / 2f, add, ChatDeckPalette.LabelInk);
}
```

- [ ] **Step 4: Run the tests and see them pass.** Run `~/.dotnet/dotnet test tests/AcDream.App.Tests --filter "FullyQualifiedName~ChatDeck|FullyQualifiedName~HudIcon|FullyQualifiedName~EmbeddedSvg"` → PASS.

- [ ] **Step 5: Commit**

```bash
git add assets/hud/filters.svg src/AcDream.App/UI/Hud tests/AcDream.App.Tests/UI/Hud
git commit -m "Chat Deck: Filters & Tabs stud, + tab button, full tab menu with rename in place"
```

---

### Task 10: Deck — the transcript's right-click menu (rule-from-line)

**Goal:** Right-clicking a line, or a drag-selection, offers Copy, Move these to…, Also show in…, Hide these, Colour these… and Test this line. The rule actions open the editor's rule form with a rule written from the line.

**Files:**
- Modify: `src/AcDream.App/UI/UiText.cs` (`HitCharAt`, `TryGetSelection`, both `internal`)
- Create: `src/AcDream.App/UI/Hud/UiChatDeck.LineMenu.cs`
- Modify: `src/AcDream.App/UI/Hud/UiChatDeck.cs` (create the menu in the constructor; share `ShownText` with the peek)
- Test: `tests/AcDream.App.Tests/UI/Hud/UiChatDeckLineMenuTests.cs` (create)

**Acceptance Criteria:**
- [ ] Right-click on a line gives `Copy line|-|Move these to…|Also show in…|Hide these|Colour these…|-|Test this line`; on two or more selected lines (the click inside the selection) the first item reads `Copy lines` and Test this line is disabled; on empty space no menu opens
- [ ] Copy puts the lines' shown text (prefix setting applied, no timestamp), one per line, on the clipboard
- [ ] Hide these / Move these to… / Also show in… / Colour these… open the editor on the Rules page with `NewRule = ChatDeckRuleDraft.FromLine(facts, action)` and `FromLine = facts` of the line right-clicked (or the first selected); the rule's channels are every selected line's channels; Move / Also show in default the target to the first tab other than All
- [ ] Test this line opens the editor on the Rules page with `TestLine` = that line's facts
- [ ] `UiText`'s retail behaviour is unchanged (every `UiText*` test passes)

**Verify:** `~/.dotnet/dotnet test tests/AcDream.App.Tests --filter "FullyQualifiedName~ChatDeck|FullyQualifiedName~UiText"` → all pass

**Steps:**

- [ ] **Step 1: Write the failing tests.** Create `tests/AcDream.App.Tests/UI/Hud/UiChatDeckLineMenuTests.cs`. Without a font, `UiText` uses a 16 px line and a base Y of 0 until it is drawn, so after `Transcript.RefreshLines()` line `i` is at y `i*16 + 5`:

```csharp
using AcDream.App.UI.Hud;
using AcDream.Core.Chat.Deck;

namespace AcDream.App.Tests.UI.Hud;

public sealed class UiChatDeckLineMenuTests
{
    private static UiChatDeckTests.Harness WithLines(params string[] lines)
    {
        var h = new UiChatDeckTests.Harness();
        foreach (string text in lines) h.Say(text);
        h.Deck.Refresh();
        h.Deck.Transcript.RefreshLines();
        return h;
    }

    [Fact]
    public void OneLine_ItemsAndCopy()
    {
        var h = WithLines("first", "second");
        Assert.Equal(["Copy line", "-", "Move these to…", "Also show in…", "Hide these", "Colour these…", "-", "Test this line"],
            h.Deck.LineMenuItemsAt(5f, 16f + 5f));
        h.Deck.ChooseLineMenuItem(0);
        Assert.Contains("second", h.Keyboard.StoredText);
        Assert.DoesNotContain("first", h.Keyboard.StoredText);
    }

    [Fact]
    public void HideThese_OpensTheFormWithARuleFromTheLine()
    {
        var h = WithLines("buy my stuff");
        h.Deck.LineMenuItemsAt(5f, 5f);
        h.Deck.ChooseLineMenuItem(4);   // Hide these
        ChatDeckEditorRequest r = Assert.Single(h.EditorRequests);
        Assert.Equal(ChatDeckEditorPage.Rules, r.Page);
        Assert.Equal("buy my stuff", r.NewRule!.Pattern);
        Assert.Equal(ChatDeckRuleAction.Hide, r.NewRule.Action);
        Assert.Equal([ChatDeckChannel.Say], r.NewRule.Channels);
        Assert.Equal("buy my stuff", r.FromLine!.Text);
    }

    [Fact]
    public void MoveThese_DefaultsTheTarget()
    {
        var h = WithLines("hello");
        h.Deck.LineMenuItemsAt(5f, 5f);
        h.Deck.ChooseLineMenuItem(2);
        Assert.Equal("local", h.EditorRequests[0].NewRule!.TargetTabId);
    }

    [Fact]
    public void TestThisLine_SendsItsFacts()
    {
        var h = WithLines("hello");
        h.Deck.LineMenuItemsAt(5f, 5f);
        h.Deck.ChooseLineMenuItem(7);
        Assert.Equal("hello", h.EditorRequests[0].TestLine!.Text);
    }

    [Fact]
    public void EmptySpace_NoMenu()
    {
        var h = new UiChatDeckTests.Harness();
        h.Deck.Transcript.RefreshLines();
        Assert.Empty(h.Deck.LineMenuItemsAt(5f, 5f));
    }
}
```

`h.Keyboard.StoredText` assumes the harness's `FakeKeyboard` is `AcDream.App.Tests.Plugins.FakeKeyboard`. If it is another fake, read the clipboard through `((Silk.NET.Input.IKeyboard)h.Keyboard).ClipboardText`. For the multi-line case, add a test that selects two lines through `UiText` mouse events (`MouseDown` on line 0, `MouseMove` to line 1, `MouseUp`, as the existing `UiText` selection tests do; find one with `grep -rn "SelectedText()" tests/AcDream.App.Tests/UI`). Then assert `Copy lines` and `ItemsEnabledSource()[7] == false`.

- [ ] **Step 2: Run them and see them fail** (compile errors).

Run: `~/.dotnet/dotnet test tests/AcDream.App.Tests --filter "FullyQualifiedName~UiChatDeckLineMenu"`

- [ ] **Step 3: Implement.** In `UiText.cs`, next to `SelectedText()`:

```csharp
/// <summary>The line and column under a local point (the Chat Deck's line menu).</summary>
internal Pos HitCharAt(float localX, float localY) => HitChar(localX, localY);

/// <summary>The selection's ordered ends; false when nothing is selected.</summary>
internal bool TryGetSelection(out Pos start, out Pos end) => TryGetOrderedSelection(out start, out end);
```

In `UiChatDeck.cs`, add a helper for a line's shown text and use it in the peek lambda:

```csharp
/// <summary>A line's text as the transcript shows it, without a timestamp.</summary>
private string ShownText(ChatDeckLine line)
{
    AcDream.Runtime.Chat.ChatProjectedLine projected = AcDream.Runtime.Chat.ChatLineProjector.Project(line.Entry);
    return (_model?.Settings.Appearance.ChannelPrefix ?? true) ? projected.Text : ChatDeckPrefix.Strip(projected, line.Entry).Text;
}
```

Replace the `ChatDeckPeekFeed` constructor argument with `ShownText`, and call `CreateLineMenu();` after `CreateDrawer();` in the constructor.

Create `src/AcDream.App/UI/Hud/UiChatDeck.LineMenu.cs`:

```csharp
using AcDream.App.UI.Markup;
using AcDream.Core.Chat.Deck;

namespace AcDream.App.UI.Hud;

internal sealed partial class UiChatDeck
{
    private readonly UiMarkupContextMenu _lineMenu = new() { ThemePalette = ChatDeckPalette.Menu };
    private IReadOnlyList<ChatDeckLine> _menuLines = [];
    private ChatDeckLine? _menuLine;   // the line the rule is written from

    internal UiMarkupContextMenu LineMenu => _lineMenu;

    private void CreateLineMenu()
    {
        _lineMenu.Opening = (x, y) => PickMenuLines(x, y);
        _lineMenu.ItemsSource = () => _menuLines.Count == 0 ? [] :
            [_menuLines.Count > 1 ? "Copy lines" : "Copy line", "-",
             "Move these to…", "Also show in…", "Hide these", "Colour these…", "-", "Test this line"];
        _lineMenu.ItemsEnabledSource = () => [true, true, true, true, true, true, true, _menuLines.Count == 1];
        _lineMenu.Selected = ChooseLineMenuItem;
        Transcript.AddChild(_lineMenu);
        Transcript.ContextMenu = _lineMenu;
    }

    /// <summary>The line menu's items for a right-click at a transcript-local point (empty off the lines).</summary>
    internal IReadOnlyList<string> LineMenuItemsAt(float x, float y)
    {
        PickMenuLines(x, y);
        return _lineMenu.ItemsSource();
    }

    /// <summary>The selected lines when the click is inside the selection, else the line under the click.</summary>
    private void PickMenuLines(float x, float y)
    {
        _menuLines = [];
        _menuLine = null;
        if (_layout.SourceOf.Count == 0) return;
        UiText.Pos at = Transcript.HitCharAt(x, y);
        if (at.Line < 0 || at.Line >= _layout.SourceOf.Count) return;
        ChatDeckLine clicked = _layout.Sources[_layout.SourceOf[at.Line]];

        if (Transcript.TryGetSelection(out UiText.Pos start, out UiText.Pos end) && at.Line >= start.Line && at.Line <= end.Line)
        {
            var lines = new List<ChatDeckLine>();
            for (int i = start.Line; i <= end.Line && i < _layout.SourceOf.Count; i++)
            {
                ChatDeckLine line = _layout.Sources[_layout.SourceOf[i]];
                if (lines.Count == 0 || !ReferenceEquals(lines[^1], line)) lines.Add(line);
            }
            _menuLines = lines;
        }
        else
        {
            _menuLines = [clicked];
        }
        _menuLine = clicked;
    }

    internal void ChooseLineMenuItem(int index)
    {
        if (_menuLine is not { } line || _model is null) return;
        switch (index)
        {
            case 0:
                if (_b.Keyboard is { } keyboard) keyboard.ClipboardText = string.Join("\n", _menuLines.Select(ShownText));
                break;
            case 2: OpenRuleFromLines(ChatDeckRuleAction.MoveTo); break;
            case 3: OpenRuleFromLines(ChatDeckRuleAction.CopyTo); break;
            case 4: OpenRuleFromLines(ChatDeckRuleAction.Hide); break;
            case 5: OpenRuleFromLines(ChatDeckRuleAction.ColourLine); break;
            case 7:
                if (_menuLines.Count == 1)
                    _b.OpenEditor?.Invoke(new ChatDeckEditorRequest { Page = ChatDeckEditorPage.Rules, TestLine = line.Facts });
                break;
        }
    }

    private void OpenRuleFromLines(ChatDeckRuleAction action)
    {
        if (_menuLine is not { } line || _model is null) return;
        ChatDeckRule rule = ChatDeckRuleDraft.FromLine(line.Facts, action) with
        {
            Channels = _menuLines.Select(l => l.Channel).ToHashSet(),
        };
        if (ChatDeckSettingsEdits.Targets(rule))
            rule = rule with
            {
                TargetTabId = _model.Settings.Tabs.FirstOrDefault(t => t.Id != ChatDeckDefaults.AllTabId)?.Id ?? "",
            };
        _b.OpenEditor?.Invoke(new ChatDeckEditorRequest
        {
            Page = ChatDeckEditorPage.Rules, NewRule = rule, FromLine = line.Facts,
        });
    }
}
```

Check that `UiText.Pos` is reachable as `UiText.Pos` (it is a nested `public readonly record struct`). `UiElement.ContextMenu` is `internal`, so it is settable from the App assembly. The menu must be a child of its owner (`TryOpen` checks `Parent`), so it is added to `Transcript`. In `OnHidden`, add `_lineMenu.Close(restoreFocus: false);`.

- [ ] **Step 4: Run the tests and see them pass.** Run `~/.dotnet/dotnet test tests/AcDream.App.Tests --filter "FullyQualifiedName~ChatDeck|FullyQualifiedName~UiText"` → PASS.

- [ ] **Step 5: Commit**

```bash
git add src/AcDream.App/UI tests/AcDream.App.Tests/UI/Hud
git commit -m "Chat Deck: transcript line menu with copy, rule-from-line and test this line"
```

---

### Task 11: Runtime — host the editor and wire the deck

**Goal:** The runtime builds the editor window once, in the deck look, docks it to the deck when it opens, opens it for each deck request, and closes it when the deck hides or the character changes. The editor and the settings popup replace each other.

**Files:**
- Modify: `src/AcDream.App/UI/PluginAppearanceWindow.cs` (`Show`)
- Modify: `src/AcDream.App/UI/RetailUiRuntime.cs` (around `ToggleChatDeckSettings`, the Chat Deck replacement's `OnHidden`, `MountChatDeck`, teardown)
- Test: `tests/AcDream.App.Tests/UI/PluginAppearanceWindowTests.cs` (create, or add to an existing one: `grep -rln PluginAppearanceWindow tests/AcDream.App.Tests`), `tests/AcDream.App.Tests/UI/Hud/ChatDeckWiringTests.cs`

**Acceptance Criteria:**
- [ ] `PluginAppearanceWindow.Show()` opens a closed window (running `onOpen`, so it docks) and only brings an open one to the front (no re-dock, no close)
- [ ] The ⧩ stud opens the editor docked to the deck's top-right (as the settings popup docks) and closes it on a second press
- [ ] A deck request (`OpenEditor`) opens the editor at the requested page, tab, rule form or test line
- [ ] Opening the editor closes the settings popup and the other way round
- [ ] Turning Modern HUD off (the Chat Deck hidden) closes the editor; a new session model (`ChatDeckSession.ModelChanged`) closes it too
- [ ] Wiring test: a line said in the world, right-click → Hide these → the editor is open on the Rules page with the form showing that line's text and `PreviewText` "Matches 1 of the last 1 lines." (the spec's "rule-from-line opens the editor with a preview")
- [ ] The editor is built with `WithFixedLook(ChatDeckPalette.Window, …)` and the deck fonts, exactly as the settings popup is

**Verify:** `~/.dotnet/dotnet test tests/AcDream.App.Tests --filter "FullyQualifiedName~ChatDeck|FullyQualifiedName~PluginAppearanceWindow"` → all pass

**Steps:**

- [ ] **Step 1: Write the failing tests.** `PluginAppearanceWindow` test:

```csharp
using AcDream.App.UI;

namespace AcDream.App.Tests.UI;

public sealed class PluginAppearanceWindowTests
{
    [Fact]
    public void Show_OpensOnce_ThenOnlyBringsToFront()
    {
        var root = new UiRoot();   // follow how other tests build a UiRoot (size it as they do)
        int opens = 0;
        var window = new PluginAppearanceWindow(root, _ => new UiPanel { Width = 100, Height = 80 }, onOpen: _ => opens++);
        window.Show();
        Assert.True(window.IsOpen);
        window.Show();
        Assert.True(window.IsOpen);
        Assert.Equal(1, opens);
    }
}
```

In `ChatDeckWiringTests.World`, add the editor beside `SettingsWindow`:

```csharp
public readonly ChatDeckEditorBinding EditorBinding;
public readonly PluginAppearanceWindow EditorWindow;
```

```csharp
EditorBinding = new ChatDeckEditorBinding(() => Model);
EditorWindow = new PluginAppearanceWindow(Root, close =>
{
    EditorBinding.Close = close;
    return ChatDeckSettingsBinding.Prepare(MarkupDocument.Build(ChatDeckEditorBinding.Markup, EditorBinding, _ => (0u, 0, 0),
        themes: new PluginUiThemeSettings().WithFixedPalette(ChatDeckPalette.Window)), close);
}, onOpen: window => ChatDeckSettingsPlacement.Dock(Root, Deck, window));
```

Pass `OpenEditor: r => { EditorBinding.Open(r); if (SettingsWindow.IsOpen) SettingsWindow.Toggle(); EditorWindow.Show(); }`, `ToggleEditor: () => { if (SettingsWindow.IsOpen) SettingsWindow.Toggle(); EditorWindow.Toggle(); }` and `EditorOpen: () => EditorWindow.IsOpen` to the deck's bindings. Make the settings `OpenSettings` close the editor first in the same way. In the switch's `OnHidden`, close the editor as the settings window is closed. The `World` mirrors the runtime, so the runtime code in Step 3 does the same. The editor and settings windows are created before `Deck` but their `onOpen` reads `Deck` only when opened, so the order is fine. Then add:

```csharp
[Fact]
public void FiltersStud_OpensTheEditorDocked_AndReplacesSettings()
{
    var w = new World(_path);
    w.Switch.Apply(true);
    PressDeck(w.Deck, w.Deck.Geometry.SettingsStud);
    Assert.True(w.SettingsWindow.IsOpen);
    PressDeck(w.Deck, w.Deck.Geometry.FiltersStud);
    Assert.True(w.EditorWindow.IsOpen);
    Assert.False(w.SettingsWindow.IsOpen);
    UiElement editor = w.Root.Children[^1];
    Assert.Equal(w.Deck.Left + w.Deck.Width, editor.Left + editor.Width);
    PressDeck(w.Deck, w.Deck.Geometry.FiltersStud);
    Assert.False(w.EditorWindow.IsOpen);
}

[Fact]
public void ModernHudOff_ClosesTheEditor()
{
    var w = new World(_path);
    w.Switch.Apply(true);
    PressDeck(w.Deck, w.Deck.Geometry.FiltersStud);
    w.Switch.Apply(false);
    Assert.False(w.EditorWindow.IsOpen);
}

[Fact]
public void RuleFromLine_OpensTheEditorWithAPreview()
{
    var w = new World(_path);
    w.Switch.Apply(true);
    w.Log.OnLocalSpeech("Bob", "buy my stuff", 0x50000002u, isRanged: false, logTextType: 0x02u);   // the World's ChatLog; expose it if it is a local
    w.Model!.Tick();
    w.Deck.Refresh();
    w.Deck.Transcript.RefreshLines();
    w.Deck.LineMenuItemsAt(5f, 5f);
    w.Deck.ChooseLineMenuItem(4);   // Hide these
    Assert.True(w.EditorWindow.IsOpen);
    Assert.True(w.EditorBinding.OnRulesPage);
    Assert.True(w.EditorBinding.FormOpen);
    Assert.Equal("buy my stuff", w.EditorBinding.Pattern);
    Assert.Equal("Matches 1 of the last 1 lines.", w.EditorBinding.PreviewText);
}
```

The `World` constructor creates its `ChatLog` as a local (`var log = new ChatLog();`). Make it a field `public readonly ChatLog Log = new();` and use it.

- [ ] **Step 2: Run them and see them fail.**

Run: `~/.dotnet/dotnet test tests/AcDream.App.Tests --filter "FullyQualifiedName~ChatDeckWiring|FullyQualifiedName~PluginAppearanceWindow"`

- [ ] **Step 3: Implement.** In `PluginAppearanceWindow.cs`:

```csharp
/// <summary>Opens the window when it is closed (placed as any open places it), else brings it to the front.</summary>
internal void Show()
{
    if (IsOpen)
    {
        root.BringToFront(_window!);
        return;
    }
    Toggle();
}
```

In `RetailUiRuntime.cs`, next to `_chatDeckSettings`, factor the fixed deck look into one helper that both popups use, and add the editor:

```csharp
/// <summary>The look of the Chat Deck's popups: the deck's palette and sans, whatever the plugin theme.</summary>
private PluginUiThemeSettings ChatDeckPopupThemes()
{
    Hud.DeckFonts deck = _deckFonts ??= Hud.DeckFonts.Load(_bindings.Assets.TextureCache);
    Lazy<UiDatFont> body = new(() => deck.Face(Hud.DeckFonts.TextPixelHeight, BundledUiFontWeight.Regular));
    Lazy<UiDatFont> title = new(() => deck.Face(Hud.DeckFonts.SmallPixelHeight, BundledUiFontWeight.SemiBold));
    return (_pluginThemes ?? new PluginUiThemeSettings()).WithFixedLook(Hud.ChatDeckPalette.Window, body, title);
}

private PluginAppearanceWindow? _chatDeckEditor;
private Hud.ChatDeckEditorBinding? _chatDeckEditorBinding;

private PluginAppearanceWindow ChatDeckEditorWindow()
{
    _chatDeckEditorBinding ??= new Hud.ChatDeckEditorBinding(() => _chatDeckSession?.Model);
    Hud.ChatDeckEditorBinding binding = _chatDeckEditorBinding;
    return _chatDeckEditor ??= new PluginAppearanceWindow(Host.Root, close =>
    {
        binding.Close = close;
        return Hud.ChatDeckSettingsBinding.Prepare(MarkupDocument.Build(Hud.ChatDeckEditorBinding.Markup, binding,
            _bindings.Assets.ResolveSprite, _bindings.Assets.Controls, _bindings.Assets.DefaultFont,
            themes: ChatDeckPopupThemes(), describeKey: DescribeKey(allowCommand: true)), close);
    }, onOpen: window => Hud.ChatDeckSettingsPlacement.Dock(Host.Root, _chatDeck, window));
}

private void ToggleChatDeckEditor()
{
    CloseChatDeckSettings();
    ChatDeckEditorWindow().Toggle();
}

private void OpenChatDeckEditor(Hud.ChatDeckEditorRequest request)
{
    CloseChatDeckSettings();
    PluginAppearanceWindow window = ChatDeckEditorWindow();
    _chatDeckEditorBinding!.Open(request);
    window.Show();
}

private void CloseChatDeckEditor()
{
    if (_chatDeckEditor is { IsOpen: true } open) open.Toggle();
}
```

Rewrite `ToggleChatDeckSettings` to use `ChatDeckPopupThemes()` (dropping its own font lines) and to call `CloseChatDeckEditor();` first. In the Chat Deck replacement's `OnHidden`, add `CloseChatDeckEditor();` next to `CloseChatDeckSettings();`. In `MountChatDeck`, add to the `ChatDeckBindings`:

```csharp
OpenEditor: OpenChatDeckEditor,
ToggleEditor: ToggleChatDeckEditor,
EditorOpen: () => _chatDeckEditor?.IsOpen == true
```

Where `session!.ModelChanged += _chatDeck.SyncDrawerFromModel;` is (around line 3762), add `session.ModelChanged += CloseChatDeckEditor;`. At teardown (around line 6264, `ModelChanged -= _chatDeck.SyncDrawerFromModel`), add the matching `-= CloseChatDeckEditor;`. `ChatDeckSession.ModelChanged` is an `Action`, and `CloseChatDeckEditor` is a `void()` method, so it subscribes directly.

`PluginAppearanceWindow`'s `_window` is kept between opens, so the binding (built first, kept in `_chatDeckEditorBinding`) is the one the window reads. `Open(request)` runs before `Show()`, so the first frame shows the requested page. The window's opening size follows the `visible` bindings as they read at build time, and the window is fixed size (`w="640" h="600"`), so both pages fit.

- [ ] **Step 4: Run the tests and see them pass.** Same command → PASS. Then run the whole App and Core Chat Deck sets once: `~/.dotnet/dotnet test tests/AcDream.App.Tests --filter "FullyQualifiedName~ChatDeck|FullyQualifiedName~UiText|FullyQualifiedName~UiField"` and `~/.dotnet/dotnet test tests/AcDream.Core.Tests --filter "FullyQualifiedName~ChatDeck"`.

- [ ] **Step 5: Commit**

```bash
git add src/AcDream.App/UI tests/AcDream.App.Tests/UI
git commit -m "Chat Deck: runtime hosts the Filters & Tabs editor, docked, closing with the deck"
```

---

### Task 12: Full suite and live check

**Goal:** Prove the branch with the full Mac suite, then a live check on the local ACE server once the user says they are available.

> **USER-ORDERED GATE — NON-SKIPPABLE.** This task was requested by the user in the current conversation. It MUST NOT be closed by walking around it, by declaring it "verified inline", or by substituting a cheaper check. Close only after every item in `acceptanceCriteria` has been re-validated independently, with output captured.

**Files:**
- none (verification only; fixes found here get their own commits with tests)

**Acceptance Criteria:**
- [ ] `~/code/OpenAC/test-baseline/run-mac-tests.sh ~/code/OpenAC/OpenAC-chat-deck-editor` exits 0, and its first (building) pass shows no failures
- [ ] The user said they are available before any client is launched (never launch on your own; local ACE 10.10.20.20 via `~/OpenAC-dev/dev-client.env` only, never Dreamweave)
- [ ] Live, the user confirms: ⧩ opens the editor docked to the deck in the deck look; the Tabs page renames, adds (also via `+`), reorders, deletes with confirmation, and resets with confirmation, and the deck follows at once; tab channels and send channel take effect on new lines
- [ ] Live, the user confirms: a rule added from a line (Hide these) hides the next such line; Move / Also show in / Colour these work; the generalise checkboxes and preview count look right; a standard filter ticked in the checklist hides its lines; Test a line explains a typed line and one sent from the transcript
- [ ] Live, the user confirms: rename in place (Enter, Esc), the tab menu's Delete opening the confirm row, and that a rule and a renamed tab survive a client restart
- [ ] Live, the user confirms: searching with channel names hidden, and unread badges while a search is open, behave as the plan's design choices say

**Verify:** `~/code/OpenAC/test-baseline/run-mac-tests.sh ~/code/OpenAC/OpenAC-chat-deck-editor; echo exit=$?` → `exit=0`; then the user's live confirmation of each item above

**Steps:**

- [ ] **Step 1: Run the full suite.** `~/code/OpenAC/test-baseline/run-mac-tests.sh ~/code/OpenAC/OpenAC-chat-deck-editor; echo exit=$?`. Capture the summary lines and the exit code. If anything fails, fix it with a test first, commit, and run again.

- [ ] **Step 2: Ask the user** whether they are available for the live check. Do not launch anything until they say so.

- [ ] **Step 3: Live check** with the user, item by item from the Acceptance Criteria, on the local ACE server. Record each item's result. Fix any problem found with a failing test first, then a separate commit, and re-check that item live.

- [ ] **Step 4: Report** the suite result and the live results to the controller. Merging into `main` and `rynth` and pushing happen only after the user agrees, as for plans 1–4.
