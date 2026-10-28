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
        string unique = name;
        for (int n = 2; s.Tabs.Any(t => t.Name == unique); n++) unique = $"{name} {n}";
        name = unique;
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
        List<ChatDeckRule> all = s.Rules.Where(r => r.Kind == ChatDeckMatchKind.Preset && r.PresetId == presetId).ToList();
        if (!enabled)
        {
            // A duplicated preset rule counts too: switching the preset off turns every copy off.
            ChatDeckSettings off = s;
            foreach (ChatDeckRule r in all.Where(r => r.Enabled)) off = SetRuleEnabled(off, r.Id, false);
            return off;
        }
        if (all.Count > 0) return SetRuleEnabled(s, all[0].Id, true);
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
