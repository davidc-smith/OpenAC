using AcDream.App.UI;
using AcDream.App.UI.Hud;

namespace AcDream.App.Tests.UI.Hud;

public sealed class HudTextMenuTests
{
    [Fact]
    public void WithoutAnOverride_SameAsHudIsMarked_WithTheGlobalSize()
    {
        HudTextSettings s = HudTextSettings.Default.WithGlobal(120);
        Assert.Equal(
            ["Text size", "* Same as HUD (120%)", "80%", "90%", "100%", "110%", "120%", "130%", "140%", "150%"],
            HudTextMenu.Labels(s, HudDeck.Command));
    }

    [Fact]
    public void WithAnOverride_ItsSizeIsMarked_OtherDecksStaySameAsHud()
    {
        HudTextSettings s = HudTextSettings.Default.WithOverride(HudDeck.Gear, 140);
        string[] labels = HudTextMenu.Labels(s, HudDeck.Gear);
        Assert.Equal("Same as HUD (100%)", labels[1]);
        Assert.Equal("* 140%", labels[8]);
        Assert.Equal("* Same as HUD (100%)", HudTextMenu.Labels(s, HudDeck.Chat)[1]);
    }

    [Fact]
    public void TheHeadingIsDisabled_EveryChoiceIsEnabled()
    {
        Assert.Equal(10, HudTextMenu.Count);
        Assert.False(HudTextMenu.Enabled(0));
        for (int i = 1; i < HudTextMenu.Count; i++) Assert.True(HudTextMenu.Enabled(i));
        Assert.False(HudTextMenu.Enabled(HudTextMenu.Count));
    }

    [Fact]
    public void EnabledFlags_MatchEnabledForEveryEntry()
    {
        bool[] flags = HudTextMenu.EnabledFlags();
        Assert.Equal(HudTextMenu.Count, flags.Length);
        for (int i = 0; i < flags.Length; i++) Assert.Equal(HudTextMenu.Enabled(i), flags[i]);
    }

    [Fact]
    public void Choose_SetsOrClearsTheDecksOverride()
    {
        var text = new HudText();
        HudTextMenu.Choose(text, HudDeck.Target, 9);   // 150%
        Assert.Equal(150, text.Settings.PercentFor(HudDeck.Target));
        HudTextMenu.Choose(text, HudDeck.Target, 1);   // Same as HUD
        Assert.False(text.Settings.HasOverride(HudDeck.Target));
        HudTextMenu.Choose(text, HudDeck.Target, 0);   // the heading does nothing
        Assert.False(text.Settings.HasOverride(HudDeck.Target));
    }

    [Fact]
    public void StyleMenu_GivesTheMenuTheDecksScaleAndTheFactorsFace()
    {
        var menu = new UiMarkupContextMenu();
        HudTextMenu.StyleMenu(menu, FakeDeckFonts.Make(), 1.2f, 1.5f);
        float big = FakeDeckFonts.Line(DeckFonts.PixelHeight(DeckFonts.SmallPixelHeight, 1.2f * 1.5f));
        float own = FakeDeckFonts.Line(DeckFonts.PixelHeight(DeckFonts.SmallPixelHeight, 1.2f));
        Assert.Equal(1.2f, menu.Scale);
        Assert.Equal(big, menu.DatFont!.LineHeight);
        Assert.Equal(big - own, menu.RowGrow, 3);
    }
}
