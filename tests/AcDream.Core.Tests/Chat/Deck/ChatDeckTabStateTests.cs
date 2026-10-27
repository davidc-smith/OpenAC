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
