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
