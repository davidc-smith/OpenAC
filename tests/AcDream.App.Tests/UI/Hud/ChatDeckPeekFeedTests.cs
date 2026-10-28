using AcDream.App.UI.Hud;
using AcDream.Core.Chat;
using AcDream.Core.Chat.Deck;

namespace AcDream.App.Tests.UI.Hud;

public sealed class ChatDeckPeekFeedTests
{
    private sealed class Rig
    {
        public readonly ChatLog Log = new();
        public readonly ChatDeckModel Model;
        public readonly ChatDeckPeekFeed Feed = new(l => l.Facts.Text);

        public Rig(ChatDeckSettings? settings = null)
        {
            Model = new ChatDeckModel(Log, settings ?? ChatDeckDefaults.Create(), "Davian");
            Model.LinesDelivered += d => Feed.Add(d, Model.Settings.Tabs);
        }

        public void Say(string text, string sender = "Bob")
        {
            Log.OnLocalSpeech(sender, text, 0x50000002u, isRanged: false, logTextType: (uint)RetailLogTextType.Speech);
            Model.Tick();
        }

        public void Tell(string sender, string text)
        {
            Log.OnTellReceived(sender, text, 0x50000003u, (uint)RetailLogTextType.Tell);
            Model.Tick();
        }
    }

    /// <summary>Default settings with every tab's peek switched off, so only alerts can peek.</summary>
    private static ChatDeckSettings AllPeekOff() =>
        ChatDeckDefaults.Create() with
        {
            Tabs = ChatDeckDefaults.Create().Tabs.Select(t => t with { Peek = false }).ToList(),
        };

    [Fact]
    public void PeekTabsPeek_SystemAndOwnLinesDoNot()
    {
        var r = new Rig();
        r.Say("hello");                                                          // Local: peek on
        r.Log.OnSystemMessage("You feel refreshed.", (uint)RetailLogTextType.System); // System: peek off
        r.Model.Tick();
        r.Say("me again", sender: "Davian");                                     // own line, Local
        r.Tell("Bo", "got a sec?");                                              // incoming tell: Local peek on, and an alert

        Assert.Equal(["hello", "got a sec?"], r.Feed.Lines.Select(l => l.Text));
    }

    [Fact]
    public void IncomingTellPeeksOnAlertAlone_WhenNoTabPeeks()
    {
        var r = new Rig(AllPeekOff());
        r.Say("hello");                  // Say: no alert, no peek tab -> must not peek
        r.Tell("Bo", "got a sec?");      // TellIn: alert -> peeks on its own

        Assert.Equal(["got a sec?"], r.Feed.Lines.Select(l => l.Text));
    }

    [Fact]
    public void OwnAlertDoesNotPeek_WhenNoTabPeeks()
    {
        var r = new Rig(AllPeekOff());
        r.Tell("Davian", "talking to myself"); // TellIn alert, but sender is the character -> own

        Assert.Empty(r.Feed.Lines);
    }

    [Fact]
    public void KeepsTheNewestThree_OldestFirst()
    {
        var r = new Rig();
        foreach (string s in new[] { "1", "2", "3", "4" }) r.Say(s);
        Assert.Equal(["2", "3", "4"], r.Feed.Lines.Select(l => l.Text));
    }

    [Fact]
    public void FadesOverTheLastSecond_ThenGoes()
    {
        var r = new Rig();
        r.Say("hello");
        r.Feed.Advance(5.0);
        Assert.Equal(1f, ChatDeckPeekFeed.Alpha(r.Feed.Lines[0].Age));
        r.Feed.Advance(0.5);
        Assert.Equal(0.5f, ChatDeckPeekFeed.Alpha(r.Feed.Lines[0].Age), 3);
        r.Feed.Advance(0.5);
        Assert.Empty(r.Feed.Lines);
    }

    [Fact]
    public void Clear_EmptiesIt()
    {
        var r = new Rig();
        r.Say("hello");
        r.Feed.Clear();
        Assert.Empty(r.Feed.Lines);
    }
}
