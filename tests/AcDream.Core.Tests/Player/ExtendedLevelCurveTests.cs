using AcDream.Core.Player;

namespace AcDream.Core.Tests.Player;

public sealed class ExtendedLevelCurveTests
{
    // The top of the retail curve: levels 274 and 275 as the portal files
    // hold them. Everything below is irrelevant to the extension.
    private const ulong Level274 = 187_835_858_847UL;
    private const ulong Level275 = 191_226_310_247UL;

    private static ulong[] RetailTop()
    {
        var levels = new ulong[276];
        for (int i = 0; i < 274; i++)
            levels[i] = (ulong)i;
        levels[274] = Level274;
        levels[275] = Level275;
        return levels;
    }

    [Fact]
    public void LevelsPastTheTableMatchTheServersCurve()
    {
        IReadOnlyList<ulong> levels = ExtendedLevelCurve.Extend(RetailTop());

        // ACECustom's own XPTests pin these two values.
        Assert.Equal(194_665_023_376UL, levels[276]);
        Assert.Equal(198_152_685_222UL, levels[277]);
    }

    [Fact]
    public void TheRetailLevelsAreKept()
    {
        ulong[] retail = RetailTop();

        IReadOnlyList<ulong> levels = ExtendedLevelCurve.Extend(retail);

        Assert.Equal(retail, levels.Take(retail.Length));
    }

    [Fact]
    public void TheCurveStopsWhereTheServerCanNoLongerSendTheTotal()
    {
        IReadOnlyList<ulong> levels = ExtendedLevelCurve.Extend(RetailTop());

        Assert.True(levels[^1] <= long.MaxValue);
        Assert.Equal(1_510, levels.Count - 1);
    }

    [Fact]
    public void AnExtendedCharacterIsMeasuredTowardsItsNextLevel()
    {
        IReadOnlyList<ulong> levels = ExtendedLevelCurve.Extend(RetailTop());

        LevelProgress progress = LevelProgress.Measure(levels, 276, 196_000_000_000L)!.Value;

        Assert.False(progress.AtTopOfTable);
        Assert.Equal(2_152_685_222L, progress.ToNext);
    }

    [Fact]
    public void ATableTooShortToExtendIsReturnedAsItIs()
    {
        ulong[] single = [0UL];

        Assert.Same(single, ExtendedLevelCurve.Extend(single));
    }
}
