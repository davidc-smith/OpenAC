using AcDream.App.UI.Hud;
using AcDream.Core.Chat.Deck;

namespace AcDream.App.Tests.UI.Hud;

public sealed class ChatDeckIconsTests
{
    [Fact]
    public void EveryTabIcon_HasItsOwnBundledIcon()
    {
        var names = Enum.GetValues<ChatDeckIcon>().Select(ChatDeckIcons.Name).ToList();
        Assert.All(names, n => Assert.Contains(n, HudIcons.All));
        Assert.Equal(names.Count, names.Distinct().Count());
    }

    [Fact]
    public void DrawerIcons_AreListed()
    {
        Assert.Contains(HudIcons.Pin, HudIcons.All);
        Assert.Contains(HudIcons.Collapse, HudIcons.All);
        Assert.Contains(HudIcons.Expand, HudIcons.All);
    }
}
