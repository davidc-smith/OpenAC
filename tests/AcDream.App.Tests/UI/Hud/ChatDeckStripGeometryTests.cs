using AcDream.App.UI.Hud;
using AcDream.Core.Chat.Deck;

namespace AcDream.App.Tests.UI.Hud;

public sealed class ChatDeckStripGeometryTests
{
    [Fact]
    public void Studs_FromTheTop_PinStudAtTheBottom()
    {
        var g = ChatDeckStripGeometry.Create(220f, 6);
        Assert.Equal(6, g.Studs.Count);
        Assert.Equal(new DeckRect(5f, 10f, 24f, 22f), g.Studs[0]);
        Assert.Equal(10f + 27f, g.Studs[1].Y);
        Assert.Equal(new DeckRect(5f, 220f - 8f - 22f, 24f, 22f), g.PinStud);
        Assert.Equal(new ChatDeckHit(ChatDeckZone.Tab, 1), g.HitAt(7f, g.Studs[1].Y + 2f));
        Assert.Equal(ChatDeckZone.PinStud, g.HitAt(7f, g.PinStud.Y + 2f).Zone);
        Assert.Equal(ChatDeckZone.Frame, g.HitAt(1f, 1f).Zone);
    }

    [Fact]
    public void StudsWithNoRoom_AreDropped()
    {
        var g = ChatDeckStripGeometry.Create(140f, 6);
        Assert.All(g.Studs.Where(s => s.W > 0), s => Assert.True(s.Y + s.H <= g.PinStud.Y - ChatDeckStripGeometry.StudGap + 0.01f));
        Assert.Contains(g.Studs, s => s.W == 0);
    }

    [Fact]
    public void Place_OnTheEdge_TallEnoughForEveryStud_OnScreen()
    {
        DeckRect left = ChatDeckStripGeometry.Place(ChatDeckEdge.Left, 800f, 140f, 6, 1600f, 900f);
        Assert.Equal(0f, left.X);
        Assert.Equal(ChatDeckStripGeometry.FullHeight(6), left.H);
        Assert.Equal(900f - left.H, left.Y);           // pulled up to stay on screen

        DeckRect right = ChatDeckStripGeometry.Place(ChatDeckEdge.Right, 100f, 300f, 6, 1600f, 900f);
        Assert.Equal(1600f - 34f, right.X);
        Assert.Equal(100f, right.Y);
        Assert.Equal(300f, right.H);

        DeckRect tall = ChatDeckStripGeometry.Place(ChatDeckEdge.Left, 0f, 2000f, 6, 1600f, 900f);
        Assert.Equal(900f, tall.H);
    }

    [Fact]
    public void FlyoutLeft_TouchesTheStripOnceOut()
    {
        Assert.Equal(34f, ChatDeckStripGeometry.FlyoutLeft(ChatDeckEdge.Left, 460f, 1600f, 1));
        Assert.Equal(34f - 16f, ChatDeckStripGeometry.FlyoutLeft(ChatDeckEdge.Left, 460f, 1600f, 0));
        Assert.Equal(1600f - 34f - 460f, ChatDeckStripGeometry.FlyoutLeft(ChatDeckEdge.Right, 460f, 1600f, 1));
        Assert.Equal(1600f - 34f - 460f + 16f, ChatDeckStripGeometry.FlyoutLeft(ChatDeckEdge.Right, 460f, 1600f, 0));
    }
}
