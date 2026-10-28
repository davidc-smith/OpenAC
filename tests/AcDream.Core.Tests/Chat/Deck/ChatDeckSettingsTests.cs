using AcDream.Core.Chat.Deck;

namespace AcDream.Core.Tests.Chat.Deck;

public sealed class ChatDeckSettingsTests
{
    [Fact]
    public void KeyWithoutAModifier_NormalizesToUnbound()
    {
        ChatDeckSettings s = ChatDeckSettingsNormalizer.Normalize(ChatDeckDefaults.Create() with
        {
            Keys = new ChatDeckKeys { NextTab = "N", PreviousTab = " Ctrl+P " },
        });
        Assert.Equal("", s.Keys.NextTab);
        Assert.Equal("Ctrl+P", s.Keys.PreviousTab);
    }

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

    [Fact]
    public void Normalize_AllTabHasEveryChannel()
    {
        ChatDeckSettings s = ChatDeckDefaults.Create();
        var shrunk = s with { Tabs = [s.Tabs[0] with { Channels = new HashSet<ChatDeckChannel> { ChatDeckChannel.Say } }, .. s.Tabs.Skip(1)] };
        ChatDeckTab all = ChatDeckSettingsNormalizer.Normalize(shrunk).Tabs[0];
        Assert.Equal(Enum.GetValues<ChatDeckChannel>().Length, all.Channels.Count);
    }
}
