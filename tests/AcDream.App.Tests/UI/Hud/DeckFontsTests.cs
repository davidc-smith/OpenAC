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
    public void Face_BakesOncePerPixelHeightAndWeight()
    {
        var baked = new List<(float Px, BundledUiFontWeight Weight)>();
        var fonts = new DeckFonts((px, weight) => { baked.Add((px, weight)); return null!; });

        fonts.Face(14f, BundledUiFontWeight.Regular);
        fonts.Face(14.2f, BundledUiFontWeight.Regular);   // rounds to the same 14 px face
        fonts.Face(14f, BundledUiFontWeight.SemiBold);
        fonts.Face(16f, BundledUiFontWeight.Regular);

        Assert.Equal(
            [(14f, BundledUiFontWeight.Regular), (14f, BundledUiFontWeight.SemiBold), (16f, BundledUiFontWeight.Regular)],
            baked);
    }

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

    [Theory]
    [InlineData(1f, 1.5f, 21f, 18f)]
    [InlineData(0.75f, 1.5f, 16f, 14f)]   // 15.75 and 13.5 round away from zero
    [InlineData(1.25f, 0.8f, 14f, 12f)]
    [InlineData(1.5f, 1f, 21f, 18f)]
    public void At_WithATextFactor_BakesRoundOfBaseTimesScaleTimesFactor(float scale, float factor, float text, float small)
    {
        var baked = new List<(float Px, BundledUiFontWeight Weight)>();
        var fonts = new DeckFonts((px, weight) => { baked.Add((px, weight)); return null!; });
        DeckFontSet set = fonts.At(scale, factor);
        _ = set.Text;
        _ = set.Small;
        Assert.Equal(new[] { (text, BundledUiFontWeight.Regular), (small, BundledUiFontWeight.Regular) }, baked);
    }

    [Fact]
    public void ScalesThatRoundToTheSamePixelHeight_ShareOneBake()
    {
        var baked = new List<(float Px, BundledUiFontWeight Weight)>();
        var fonts = new DeckFonts((px, weight) => { baked.Add((px, weight)); return null!; });
        _ = fonts.At(1.4f).Text;    // 19.6 rounds to 20
        _ = fonts.At(1.45f).Text;   // 20.3 rounds to 20
        _ = fonts.Face(20f, BundledUiFontWeight.Regular);
        Assert.Equal(new[] { (20f, BundledUiFontWeight.Regular) }, baked);
    }

    [Fact]
    public void FitFace_KeepsTheFactorFaceWhenItsLineFits()
    {
        DeckFonts fonts = FakeDeckFonts.Make();
        UiDatFont face = fonts.FitFace(DeckFonts.TextPixelHeight, BundledUiFontWeight.Regular, 1f, 1.5f, boxHeight: 30f);
        Assert.Equal(FakeDeckFonts.Line(21f), face.LineHeight);
    }

    [Fact]
    public void FitFace_DropsToTheLargestFaceWhoseLineFitsTheBox()
    {
        var fits = new List<DeckFit>();
        DeckFonts fonts = FakeDeckFonts.Make(fits);
        UiDatFont face = fonts.FitFace(DeckFonts.TextPixelHeight, BundledUiFontWeight.Regular, 1f, 1.5f, boxHeight: 20f);
        Assert.Equal(20f, face.LineHeight);   // 18 px + 2
        DeckFit fit = Assert.Single(fits);
        Assert.Equal(18f, fit.Pixels);
        Assert.Equal(20f, fit.BoxHeight);
        Assert.Equal(1.5f, fit.TextFactor);
        FakeDeckFonts.AssertAllFit(fits);
    }

    [Fact]
    public void FitFace_NeverGoesBelowTheDecksOwnFace()
    {
        DeckFonts fonts = FakeDeckFonts.Make();
        UiDatFont face = fonts.FitFace(DeckFonts.SmallPixelHeight, BundledUiFontWeight.Regular, 1f, 1.5f, boxHeight: 8f);
        Assert.Equal(FakeDeckFonts.Line(12f), face.LineHeight);
    }

    [Fact]
    public void FitFace_ReportsTheFloorCase_WhenEvenTheDecksOwnFaceIsTallerThanTheBox()
    {
        var fits = new List<DeckFit>();
        DeckFonts fonts = FakeDeckFonts.Make(fits);
        fonts.FitFace(DeckFonts.SmallPixelHeight, BundledUiFontWeight.Regular, 1f, 1.5f, boxHeight: 8f);
        DeckFit fit = Assert.Single(fits);
        Assert.Equal(DeckFonts.PixelHeight(DeckFonts.SmallPixelHeight, 1f), fit.Pixels);
        Assert.True(fit.LineHeight > fit.BoxHeight);
        FakeDeckFonts.AssertAllFit(fits);
    }

    [Theory]
    [InlineData(7f, 8f)]
    [InlineData(2f, 8f)]
    [InlineData(8f, 8f)]
    [InlineData(32f, 32f)]
    [InlineData(40f, 32f)]
    public void Face_ClampsToTheBakersRange(float asked, float baked)
    {
        var bakes = new List<(float Px, BundledUiFontWeight Weight)>();
        DeckFonts fonts = FakeDeckFonts.Make(baked: bakes);
        UiDatFont face = fonts.Face(asked, BundledUiFontWeight.Regular);
        Assert.Equal(FakeDeckFonts.Line(baked), face.LineHeight);
        Assert.Equal([(baked, BundledUiFontWeight.Regular)], bakes);
    }

    [Fact]
    public void At_ASmallScaleAndFactor_BakesNoFaceBelowTheMinimum()
    {
        DeckFonts fonts = FakeDeckFonts.Make();
        DeckFontSet set = fonts.At(0.75f, 0.8f);   // small: 12 × 0.6 = 7.2 → 7, below the baker's range
        Assert.Equal(FakeDeckFonts.Line(BundledUiFont.MinimumPixelHeight), set.Small.LineHeight);
        Assert.Equal(FakeDeckFonts.Line(BundledUiFont.MinimumPixelHeight), set.SmallBold.LineHeight);
        Assert.Equal(FakeDeckFonts.Line(8f), set.Text.LineHeight);   // 14 × 0.6 = 8.4 → 8
    }

    [Fact]
    public void FitFace_NeverStepsBelowTheMinimum()
    {
        var fits = new List<DeckFit>();
        DeckFonts fonts = FakeDeckFonts.Make(fits);
        // 9 px × 0.8 = 7.2 → 7, and a box too short even for 8 px: the face stays at the minimum.
        UiDatFont face = fonts.FitFace(9f, BundledUiFontWeight.SemiBold, 1f, 0.8f, boxHeight: 6f);
        Assert.Equal(FakeDeckFonts.Line(BundledUiFont.MinimumPixelHeight), face.LineHeight);
        Assert.Equal(BundledUiFont.MinimumPixelHeight, Assert.Single(fits).Pixels);
    }

    [Fact]
    public void FitFace_ASmallerFactorIsNeverRaised()
    {
        DeckFonts fonts = FakeDeckFonts.Make();
        UiDatFont face = fonts.FitFace(DeckFonts.TextPixelHeight, BundledUiFontWeight.Regular, 1f, 0.8f, boxHeight: 100f);
        Assert.Equal(FakeDeckFonts.Line(11f), face.LineHeight);   // 11.2 rounds to 11
    }

    [Theory]
    [InlineData(true, 1.5f, 6f)]
    [InlineData(true, 1f, 0f)]
    [InlineData(true, 0.8f, 0f)]
    [InlineData(false, 1.5f, 6f)]
    public void RowGrow_IsTheLinesGrowthOverTheDecksOwnFace(bool withFonts, float factor, float grow)
    {
        DeckFonts? fonts = withFonts ? FakeDeckFonts.Make() : null;
        Assert.Equal(grow, DeckFonts.RowGrow(fonts, DeckFonts.SmallPixelHeight, BundledUiFontWeight.Regular, 1f, factor), 3);
    }

    [Fact]
    public void DeckFace_In_UsesTheFactor_AndTheDefaultFontWithoutFonts()
    {
        DeckFonts fonts = FakeDeckFonts.Make();
        Assert.Equal(20f, DeckFace.In(fonts, DeckFonts.TextPixelHeight, BundledUiFontWeight.Regular, 1f, 1.5f, 20f).Font!.LineHeight);
        Assert.Null(DeckFace.In(null, DeckFonts.TextPixelHeight, BundledUiFontWeight.Regular, 1f, 1.5f, 20f).Font);
    }
}
