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
