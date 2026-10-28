using AcDream.App.UI;
using AcDream.App.UI.Hud;

namespace AcDream.App.Tests.UI.Hud;

public sealed class UiChatDeckStripTests
{
    private static UiChatDeckTests.Harness Laid()
    {
        var h = new UiChatDeckTests.Harness();
        h.Deck.Strip.Width = ChatDeckStripGeometry.Width;
        h.Deck.Strip.Height = 220f;
        h.Deck.LayOutStrip();
        return h;
    }

    [Fact]
    public void Strip_TakesPresses_PeekNever()
    {
        var h = Laid();
        h.Deck.Strip.Visible = true;
        h.Deck.Peek.Visible = true;
        h.Deck.Peek.Width = h.Deck.Peek.Height = 50f;
        Assert.Same(h.Deck.Strip, h.Deck.Strip.HitTest(10f, 20f));
        Assert.True(h.Deck.Strip.HandlesClick);
        Assert.Null(h.Deck.Peek.HitTest(5f, 5f));
    }

    [Fact]
    public void Tooltip_NamesTheTab_OrThePin()
    {
        var h = Laid();
        DeckRect group = h.Deck.StripGeometry.Studs[h.TabIndex("group")];
        Assert.Equal("Group", h.Deck.StripTooltipAt(group.X + 2f, group.Y + 2f));
        DeckRect pin = h.Deck.StripGeometry.PinStud;
        Assert.Equal("Pin the chat", h.Deck.StripTooltipAt(pin.X + 2f, pin.Y + 2f));
        Assert.Null(h.Deck.StripTooltipAt(1f, 1f));
    }
}
