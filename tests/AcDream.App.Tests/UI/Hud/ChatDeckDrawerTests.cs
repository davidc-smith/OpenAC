using AcDream.App.UI.Hud;
using AcDream.Core.Chat.Deck;

namespace AcDream.App.Tests.UI.Hud;

public sealed class ChatDeckDrawerTests
{
    private static readonly ChatDeckDrawerInput Away = new(false, false, false, true, false, false);
    private static readonly ChatDeckDrawerInput OnStrip = Away with { PointerOverStrip = true };

    private static (ChatDeckDrawer Drawer, List<int> Changes) Collapsed()
    {
        var d = new ChatDeckDrawer();
        var changes = new List<int>();
        d.LayoutChanged += () => changes.Add(1);
        d.Collapse(ChatDeckEdge.Left);
        changes.Clear();
        return (d, changes);
    }

    [Fact]
    public void Collapse_And_Pin_ReportTheLayoutOnce()
    {
        var d = new ChatDeckDrawer();
        int changes = 0;
        d.LayoutChanged += () => changes++;
        Assert.True(d.Pinned);

        d.Collapse(ChatDeckEdge.Right);
        Assert.Equal(ChatDeckDrawerState.Collapsed, d.State);
        Assert.Equal(ChatDeckEdge.Right, d.Edge);
        Assert.True(d.StripVisible);
        Assert.False(d.DeckVisible);
        Assert.Equal(1, changes);

        d.Pin();
        d.Pin();
        Assert.True(d.Pinned);
        Assert.Equal(2, changes);
    }

    [Fact]
    public void Restore_RaisesNothing()
    {
        var d = new ChatDeckDrawer();
        int changes = 0;
        d.LayoutChanged += () => changes++;
        d.Restore(pinned: false, ChatDeckEdge.Right);
        Assert.Equal(ChatDeckDrawerState.Collapsed, d.State);
        Assert.Equal(ChatDeckEdge.Right, d.Edge);
        Assert.Equal(0, changes);
    }

    [Fact]
    public void Hover_OpensAfterTheDelay_AndSlidesOut()
    {
        var (d, _) = Collapsed();
        d.Tick(0.2, OnStrip);
        Assert.Equal(ChatDeckDrawerState.Collapsed, d.State);
        d.Tick(0.11, OnStrip);
        Assert.Equal(ChatDeckDrawerState.Open, d.State);
        Assert.Equal(ChatDeckOpenReason.Hover, d.OpenedBy);
        Assert.Equal(0, d.Slide);

        d.Tick(0.06, OnStrip);
        Assert.InRange(d.Slide, 0.4, 0.6);
        d.Tick(0.07, OnStrip);
        Assert.Equal(1, d.Slide);
    }

    [Fact]
    public void Hover_ThatBreaks_StartsAgain()
    {
        var (d, _) = Collapsed();
        d.Tick(0.25, OnStrip);
        d.Tick(0.01, Away);
        d.Tick(0.25, OnStrip);
        Assert.Equal(ChatDeckDrawerState.Collapsed, d.State);
    }

    [Fact]
    public void Open_ClosesAfterThePointerHasBeenAwayLongEnough()
    {
        var (d, _) = Collapsed();
        d.Open(ChatDeckOpenReason.Hover);
        d.Tick(0.5, Away);
        Assert.Equal(ChatDeckDrawerState.Open, d.State);
        d.Tick(0.11, Away);
        Assert.Equal(ChatDeckDrawerState.Collapsed, d.State);
        Assert.Equal(ChatDeckOpenReason.None, d.OpenedBy);
    }

    public static TheoryData<ChatDeckDrawerInput> Holds() =>
    [
        Away with { PointerOverStrip = true },
        Away with { PointerOverDeck = true },
        Away with { InputFocused = true },
        Away with { InputEmpty = false },
        Away with { ScrolledUp = true },
        Away with { Busy = true },
    ];

    [Theory]
    [MemberData(nameof(Holds))]
    public void Open_StaysWhileSomethingHoldsIt(ChatDeckDrawerInput hold)
    {
        var (d, _) = Collapsed();
        d.Open(ChatDeckOpenReason.Hover);
        d.Tick(5.0, hold);
        Assert.Equal(ChatDeckDrawerState.Open, d.State);
    }

    [Fact]
    public void EntryDone_ClosesOnlyAnEntryFlyout()
    {
        var (d, _) = Collapsed();
        d.Open(ChatDeckOpenReason.Hover);
        d.EntryDone();
        Assert.Equal(ChatDeckDrawerState.Open, d.State);

        d.Close();
        d.Open(ChatDeckOpenReason.Entry);
        d.EntryDone();
        Assert.Equal(ChatDeckDrawerState.Collapsed, d.State);
    }

    [Fact]
    public void Pin_FromAFlyout_StopsItClosing()
    {
        var (d, changes) = Collapsed();
        d.Open(ChatDeckOpenReason.Stud);
        d.Pin();
        d.Tick(5.0, Away);
        Assert.True(d.Pinned);
        Assert.Equal(1, d.Slide);
        Assert.Single(changes);
    }

    [Fact]
    public void Open_OnlyFromCollapsed()
    {
        var d = new ChatDeckDrawer();
        d.Open(ChatDeckOpenReason.Entry);
        Assert.True(d.Pinned);
    }

    [Theory]
    [InlineData(10f, ChatDeckEdge.Left)]
    [InlineData(1100f, ChatDeckEdge.Right)]
    [InlineData(570f, ChatDeckEdge.Left)]   // centre exactly at the middle goes left
    public void NearestEdge_ByTheWindowCentre(float left, ChatDeckEdge edge) =>
        Assert.Equal(edge, ChatDeckDrawer.NearestEdge(left, 460f, 1600f));
}
