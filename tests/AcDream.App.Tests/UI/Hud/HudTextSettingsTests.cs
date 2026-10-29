using AcDream.App.UI.Hud;

namespace AcDream.App.Tests.UI.Hud;

public sealed class HudTextSettingsTests
{
    [Fact]
    public void Default_Is100PercentForEveryDeck()
    {
        HudTextSettings s = HudTextSettings.Default;
        Assert.Equal(100, s.Global);
        Assert.Empty(s.Overrides);
        foreach (HudDeck deck in Enum.GetValues<HudDeck>())
            Assert.Equal(1f, s.FactorFor(deck));
    }

    [Fact]
    public void AnOverride_WinsForItsDeckOnly_AndSameAsHudClearsIt()
    {
        HudTextSettings s = HudTextSettings.Default.WithGlobal(120).WithOverride(HudDeck.Gear, 140);
        Assert.Equal(1.4f, s.FactorFor(HudDeck.Gear), 3);
        Assert.Equal(1.2f, s.FactorFor(HudDeck.Command), 3);
        Assert.True(s.HasOverride(HudDeck.Gear));

        HudTextSettings cleared = s.WithOverride(HudDeck.Gear, null);
        Assert.False(cleared.HasOverride(HudDeck.Gear));
        Assert.Equal(120, cleared.PercentFor(HudDeck.Gear));
    }

    [Theory]
    [InlineData(100, 100)]
    [InlineData(104, 100)]
    [InlineData(105, 110)]
    [InlineData(149, 150)]
    [InlineData(10, 80)]
    [InlineData(79, 80)]
    [InlineData(1000, 150)]
    [InlineData(-5, 80)]
    public void PercentsClampAndSnapToTens(int given, int expected)
    {
        Assert.Equal(expected, HudTextSettings.Snap(given));
        Assert.Equal(expected, HudTextSettings.Default.WithGlobal(given).Global);
        Assert.Equal(expected, HudTextSettings.Default.WithOverride(HudDeck.Chat, given).PercentFor(HudDeck.Chat));
    }

    [Fact]
    public void Json_RoundTrips()
    {
        HudTextSettings s = HudTextSettings.Default.WithGlobal(90)
            .WithOverride(HudDeck.Gear, 150).WithOverride(HudDeck.Target, 80);
        HudTextSettings back = HudTextSettings.FromJson(s.ToJson());
        Assert.Equal(s, back);
        Assert.Equal(150, back.PercentFor(HudDeck.Gear));
        Assert.Equal(90, back.PercentFor(HudDeck.Spell));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("[1,2]")]
    [InlineData("{\"global\":\"big\"}")]
    public void MissingOrBadJson_ReadsAsDefaults(string? json) =>
        Assert.Equal(HudTextSettings.Default, HudTextSettings.FromJson(json));

    [Fact]
    public void Json_OffStepValuesSnap_AndUnknownOrNumericDeckKeysAreIgnored()
    {
        HudTextSettings s = HudTextSettings.FromJson(
            """{"global":133,"overrides":{"gear":71,"radar":120,"1":140,"chat":"x"}}""");
        Assert.Equal(130, s.Global);
        Assert.Equal(80, s.PercentFor(HudDeck.Gear));
        Assert.Equal(new[] { HudDeck.Gear }, s.Overrides.Keys.ToArray());
    }
}
