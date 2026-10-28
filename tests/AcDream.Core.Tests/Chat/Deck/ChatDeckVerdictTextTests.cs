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
