using AcDream.App.UI;
using AcDream.App.UI.Hud;
using AcDream.App.UI.Layout;
using AcDream.Core.Chat;
using AcDream.Core.Chat.Deck;

namespace AcDream.App.Tests.UI.Hud;

/// <summary>The deck mounted on a 1600×900 root with its strip and peek, shown as the switch shows it.</summary>
internal sealed class DrawerWorld
{
    public readonly UiRoot Root = new() { Width = 1600, Height = 900 };
    public readonly UiChatDeckTests.Harness H;

    public DrawerWorld(float left = 10f, float top = 670f, ChatDeckSettings? settings = null)
    {
        H = new UiChatDeckTests.Harness();
        if (settings is not null)
        {
            H.Model = new ChatDeckModel(H.Log, settings, "Davian");
            H.Deck.Refresh();
        }
        RetailWindowFrame.Mount(Root, H.Deck, _ => (0u, 0, 0), UiChatDeck.MountOptions(left, top) with { Visible = true });
        Root.AddChild(H.Deck.Strip);
        Root.AddChild(H.Deck.Peek);
        H.Deck.OnShown();
    }

    public UiChatDeck Deck => H.Deck;

    public void Press(DeckRect r) => H.Press(r);

    public void PressStrip(DeckRect r) =>
        Deck.Strip.OnEvent(new UiEvent(0, Deck.Strip, UiEventType.MouseDown, Data1: (int)(r.X + 2), Data2: (int)(r.Y + 2)));

    public void Tick(double seconds) => Deck.TickDrawer(seconds);
}

public sealed class UiChatDeckDrawerTests
{
    [Fact]
    public void Collapse_HidesTheWindow_ShowsTheStripOnTheNearerEdge()
    {
        var w = new DrawerWorld();
        w.Press(w.Deck.Geometry.CollapseStud);

        Assert.False(w.Deck.Visible);
        Assert.True(w.Deck.Strip.Visible);
        Assert.Equal(0f, w.Deck.Strip.Left);
        Assert.Equal(w.Deck.Top, w.Deck.Strip.Top, 1);
        Assert.False(w.H.Model!.Settings.Layout.Pinned);
        Assert.Equal(ChatDeckEdge.Left, w.H.Model.Settings.Layout.Edge);
        Assert.False(w.Deck.Resizable);
        Assert.False(w.Deck.Draggable);
    }

    [Fact]
    public void RightHalf_CollapsesRight()
    {
        var w = new DrawerWorld(left: 1100f, top: 100f);
        w.Press(w.Deck.Geometry.CollapseStud);
        Assert.Equal(1600f - ChatDeckStripGeometry.Width, w.Deck.Strip.Left);
        Assert.Equal(ChatDeckEdge.Right, w.H.Model!.Settings.Layout.Edge);
    }

    [Fact]
    public void PinStud_WhilePinned_Collapses()
    {
        var w = new DrawerWorld();
        w.Press(w.Deck.Geometry.PinStud);
        Assert.Equal(ChatDeckDrawerState.Collapsed, w.Deck.Drawer.State);
    }

    [Fact]
    public void StripPin_PinsBesideTheEdge()
    {
        var w = new DrawerWorld();
        w.Press(w.Deck.Geometry.CollapseStud);
        w.PressStrip(w.Deck.StripGeometry.PinStud);

        Assert.True(w.Deck.Visible);
        Assert.False(w.Deck.Strip.Visible);
        Assert.Equal(ChatDeckStripGeometry.Width, w.Deck.Left);
        Assert.Equal(w.Deck.Strip.Top, w.Deck.Top, 1);
        Assert.True(w.H.Model!.Settings.Layout.Pinned);
        Assert.True(w.Deck.Resizable);
        Assert.True(w.Deck.Draggable);
        Assert.Equal(1f, w.Deck.Opacity);
    }

    [Fact]
    public void SavedCollapsedLayout_ComesBackAtLogin()
    {
        ChatDeckSettings saved = ChatDeckDefaults.Create() with
        {
            Layout = new ChatDeckLayoutState { Pinned = false, Edge = ChatDeckEdge.Right },
        };
        var w = new DrawerWorld(settings: saved);
        w.Tick(0.016);
        Assert.Equal(ChatDeckDrawerState.Collapsed, w.Deck.Drawer.State);
        Assert.False(w.Deck.Visible);
        Assert.Equal(1600f - ChatDeckStripGeometry.Width, w.Deck.Strip.Left);
    }

    [Fact]
    public void ModernHudOff_HidesTheStrip_OnBringsTheCollapsedStateBack()
    {
        var w = new DrawerWorld();
        w.Press(w.Deck.Geometry.CollapseStud);

        w.Deck.Visible = false;     // what the switch's hide does
        w.Deck.OnHidden();
        Assert.False(w.Deck.Strip.Visible);
        Assert.False(w.Deck.Peek.Visible);

        w.Deck.Visible = true;      // what the switch's show does
        w.Deck.OnShown();
        Assert.False(w.Deck.Visible);
        Assert.True(w.Deck.Strip.Visible);
    }

    [Fact]
    public void LinesWhileCollapsed_AreUnread()
    {
        var w = new DrawerWorld();
        w.Press(w.Deck.Geometry.CollapseStud);
        w.H.Say("hello");
        w.Tick(0.016);
        Assert.Equal(1, w.H.Model!.FindTab(ChatDeckDefaults.AllTabId)!.Unread);
    }
}
