using AcDream.Plugin.Abstractions;
using AcDream.Rynth.Host;

namespace AcDream.Rynth.Host.Tests;

public sealed class RynthTranslateTests
{
    [Theory]
    [InlineData(PluginCombatMode.Unknown, 1)]
    [InlineData(PluginCombatMode.Peace, 1)]
    [InlineData(PluginCombatMode.Melee, 2)]
    [InlineData(PluginCombatMode.Missile, 4)]
    [InlineData(PluginCombatMode.Magic, 8)]
    public void CombatModesBecomeRetailFlags(PluginCombatMode mode, int rynth) =>
        Assert.Equal(rynth, RynthTranslate.ToRynthCombatMode(mode));

    [Fact]
    public void RetailFlagsBecomeCombatModes()
    {
        Assert.Equal(PluginCombatMode.Peace, RynthTranslate.FromRynthCombatMode(1));
        Assert.Equal(PluginCombatMode.Melee, RynthTranslate.FromRynthCombatMode(2));
        Assert.Equal(PluginCombatMode.Missile, RynthTranslate.FromRynthCombatMode(4));
        Assert.Equal(PluginCombatMode.Magic, RynthTranslate.FromRynthCombatMode(8));
        Assert.Null(RynthTranslate.FromRynthCombatMode(3));
        Assert.Null(RynthTranslate.FromRynthCombatMode(0));
    }

    [Fact]
    public void ExpiryIsServerNowPlusRemainingAndPermanentIsMax()
    {
        Assert.Equal(1_030.5, RynthTranslate.ToServerExpiry(1_000.0, 30.5));
        Assert.Equal(double.MaxValue, RynthTranslate.ToServerExpiry(1_000.0, double.PositiveInfinity));
        Assert.Equal(double.MaxValue, RynthTranslate.ToServerExpiry(1_000.0, double.NaN));
    }
}
