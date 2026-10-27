using AcDream.App.UI;
using AcDream.App.UI.Hud;

namespace AcDream.App.Tests.UI.Hud;

public sealed class DeckFontsTests
{
    [Theory]
    [InlineData(14f, 1f, 14f)]
    [InlineData(14f, 0.75f, 11f)]   // 10.5 rounds away from zero
    [InlineData(12f, 0.75f, 9f)]
    [InlineData(14f, 1.5f, 21f)]
    [InlineData(12f, 1.25f, 15f)]
    public void PixelHeightRoundsTheScaledSize(float basePx, float scale, float expected) =>
        Assert.Equal(expected, DeckFonts.PixelHeight(basePx, scale));

    [Fact]
    public void BakesEachFaceOncePerScale_OnlyWhenUsed()
    {
        var baked = new List<(float Px, BundledUiFontWeight Weight)>();
        var fonts = new DeckFonts((px, weight) => { baked.Add((px, weight)); return null!; });

        DeckFontSet big = fonts.At(1.5f);
        Assert.Empty(baked);

        _ = big.Text; _ = big.Text; _ = big.Small;
        Assert.Equal(new[] { (21f, BundledUiFontWeight.Regular), (18f, BundledUiFontWeight.Regular) }, baked);

        Assert.Same(big, fonts.At(1.5f));
        _ = fonts.Bold;
        Assert.Equal((14f, BundledUiFontWeight.SemiBold), baked[^1]);

        _ = big.SmallBold;
        Assert.Equal((18f, BundledUiFontWeight.SemiBold), baked[^1]);
    }
}
