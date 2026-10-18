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

    [Theory]
    [InlineData(1, AcDream.Core.Combat.AttackHeight.High)]
    [InlineData(2, AcDream.Core.Combat.AttackHeight.Medium)]
    [InlineData(3, AcDream.Core.Combat.AttackHeight.Low)]
    public void AttackHeightsMapOneToThree(int rynth, AcDream.Core.Combat.AttackHeight expected) =>
        Assert.Equal(expected, RynthTranslate.ToAttackHeight(rynth));

    [Theory]
    [InlineData(0)]
    [InlineData(4)]
    [InlineData(-1)]
    public void OtherAttackHeightsAreRefused(int rynth) => Assert.Null(RynthTranslate.ToAttackHeight(rynth));
}
