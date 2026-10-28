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
