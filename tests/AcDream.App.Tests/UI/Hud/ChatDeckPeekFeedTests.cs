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

        public Rig()
        {
            Model = new ChatDeckModel(Log, ChatDeckDefaults.Create(), "Davian");
            Model.LinesDelivered += d => Feed.Add(d, Model.Settings.Tabs);
        }

        public void Say(string text, string sender = "Bob")
        {
            Log.OnLocalSpeech(sender, text, 0x50000002u, isRanged: false, logTextType: (uint)RetailLogTextType.Speech);
            Model.Tick();
        }
    }

    [Fact]
    public void PeekTabsAndAlertsPeek_OthersAndOwnLinesDoNot()
    {
        var r = new Rig();
        r.Say("hello");                                                          // Local: peek on
        r.Log.OnSystemMessage("You feel refreshed.", (uint)RetailLogTextType.System); // System: peek off
        r.Model.Tick();
        r.Say("me again", sender: "Davian");                                     // own line
        r.Log.OnTellReceived("Bo", "got a sec?", 0x50000003u, (uint)RetailLogTextType.Tell); // alert
        r.Model.Tick();

        Assert.Equal(["hello", "got a sec?"], r.Feed.Lines.Select(l => l.Text));
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
