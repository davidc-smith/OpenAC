using AcDream.App.UI;
using AcDream.App.UI.Hud;
using AcDream.Core.Chat;
using AcDream.Core.Chat.Deck;

namespace AcDream.App.Tests.UI.Hud;

public sealed class UiChatDeckFlyoutTests
{
    private static DrawerWorld Collapsed()
    {
        var w = new DrawerWorld();
        w.Press(w.Deck.Geometry.CollapseStud);
        w.Root.OnMouseMove(800, 100);   // away from everything
        return w;
    }

    private static void PointAtStrip(DrawerWorld w) =>
        w.Root.OnMouseMove((int)(w.Deck.Strip.Left + 2), (int)(w.Deck.Strip.Top + 4));

    [Fact]
    public void Hover_OpensAfterTheDelay_AndSlidesBesideTheStrip()
    {
        var w = Collapsed();
        PointAtStrip(w);
        w.Tick(0.2);
        Assert.Equal(ChatDeckDrawerState.Collapsed, w.Deck.Drawer.State);
        w.Tick(0.11);
        Assert.Equal(ChatDeckDrawerState.Open, w.Deck.Drawer.State);
        Assert.True(w.Deck.Visible);
        w.Tick(0.13);
        Assert.Equal(ChatDeckStripGeometry.Width, w.Deck.Left);
        Assert.Equal(1f, w.Deck.Opacity);
    }

    [Fact]
    public void PointerAway_ClosesAfterTheDelay()
    {
        var w = Collapsed();
        PointAtStrip(w);
        w.Tick(0.31);
        w.Root.OnMouseMove(800, 100);
        w.Tick(0.5);
        Assert.Equal(ChatDeckDrawerState.Open, w.Deck.Drawer.State);
        w.Tick(0.11);
        Assert.Equal(ChatDeckDrawerState.Collapsed, w.Deck.Drawer.State);
        Assert.False(w.Deck.Visible);
    }

    [Fact]
    public void ADraft_KeepsItOpen()
    {
        var w = Collapsed();
        PointAtStrip(w);
        w.Tick(0.31);
        w.Deck.Input.SetText("half a line");
        w.Root.OnMouseMove(800, 100);
        w.Tick(2.0);
        Assert.Equal(ChatDeckDrawerState.Open, w.Deck.Drawer.State);
    }

    public static TheoryData<string> EntryActions() =>
        ["EnterChatMode", "ToggleChatEntry", "StartCommand", "StartTell", "StartReply", "Focus"];

    [Theory]
    [MemberData(nameof(EntryActions))]
    public void EveryChatEntryAction_FliesOutAndFocusesTheInput(string action)
    {
        var w = Collapsed();
        switch (action)
        {
            case "EnterChatMode": w.Deck.EnterChatMode(null); break;
            case "ToggleChatEntry": w.Deck.ToggleChatEntry(null); break;
            case "StartCommand": w.Deck.StartCommand(); break;
            case "StartTell": w.Deck.StartTell("Bo"); break;
            case "StartReply": w.Deck.StartReply("Bo"); break;
            case "Focus": Assert.True(w.Deck.Focus()); break;
        }
        Assert.Equal(ChatDeckDrawerState.Open, w.Deck.Drawer.State);
        Assert.Equal(ChatDeckOpenReason.Entry, w.Deck.Drawer.OpenedBy);
        Assert.Same(w.Deck.Input, w.Root.KeyboardFocus);
    }

    [Fact]
    public void Send_ClosesAnEntryFlyoutAtOnce()
    {
        var w = Collapsed();
        w.Deck.EnterChatMode(null);
        w.Deck.Input.SetText("hello");
        w.Deck.Input.OnSubmit!("hello");
        Assert.Single(w.H.Bus.Published);
        Assert.Equal(ChatDeckDrawerState.Collapsed, w.Deck.Drawer.State);
        Assert.Null(w.Root.KeyboardFocus);
    }

    [Fact]
    public void Escape_ClosesAnEntryFlyout_NotAHoverOne()
    {
        var w = Collapsed();
        w.Deck.EnterChatMode(null);
        w.Deck.Input.OnEscape!();
        Assert.Equal(ChatDeckDrawerState.Collapsed, w.Deck.Drawer.State);

        PointAtStrip(w);
        w.Tick(0.31);
        w.Deck.Input.OnEscape!();
        Assert.Equal(ChatDeckDrawerState.Open, w.Deck.Drawer.State);
    }

    [Fact]
    public void StripStud_OpensOnThatTab()
    {
        var w = Collapsed();
        w.PressStrip(w.Deck.StripGeometry.Studs[w.H.TabIndex("group")]);
        Assert.Equal(ChatDeckDrawerState.Open, w.Deck.Drawer.State);
        Assert.Equal("group", w.H.Model!.SelectedTabId);
    }

    [Fact]
    public void FlyoutStuds_CollapseCloses_PinPinsInPlace()
    {
        var w = Collapsed();
        w.Deck.EnterChatMode(null);
        w.Press(w.Deck.Geometry.CollapseStud);
        Assert.Equal(ChatDeckDrawerState.Collapsed, w.Deck.Drawer.State);

        w.PressStrip(w.Deck.StripGeometry.Studs[0]);
        w.Tick(0.2);
        w.Press(w.Deck.Geometry.PinStud);
        Assert.True(w.Deck.Pinned);
        Assert.Equal(ChatDeckStripGeometry.Width, w.Deck.Left);
    }

    [Fact]
    public void Peek_ShowsPeekLinesWhileCollapsed_ThenFades()
    {
        var w = Collapsed();
        w.H.Say("hello");                                     // Local tab: peek on
        w.H.Log.OnSystemMessage("You feel refreshed.", (uint)RetailLogTextType.System);
        w.H.Model!.Tick();
        w.Tick(0.016);
        Assert.Single(w.Deck.PeekLines);
        Assert.True(w.Deck.Peek.Visible);

        w.Tick(6.0);
        Assert.Empty(w.Deck.PeekLines);
        Assert.False(w.Deck.Peek.Visible);
    }

    [Fact]
    public void Flyout_ClearsThePeek_AndLinesWhileOpenDoNotPeek()
    {
        var w = Collapsed();
        w.H.Say("hello");
        w.Deck.EnterChatMode(null);
        Assert.Empty(w.Deck.PeekLines);
        w.H.Say("while open");
        Assert.Empty(w.Deck.PeekLines);
    }
}
