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

    [Fact]
    public void CopyBeforeMove_StillAdds()
    {
        var v = Eval(Line(), R("c", ChatDeckRuleAction.CopyTo, target: "group"), R("m", ChatDeckRuleAction.MoveTo, target: "combat"));
        Assert.Equal(["all", "group", "combat"], v.Destinations);
    }
}
