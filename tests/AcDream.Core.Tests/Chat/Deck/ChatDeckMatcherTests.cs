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
