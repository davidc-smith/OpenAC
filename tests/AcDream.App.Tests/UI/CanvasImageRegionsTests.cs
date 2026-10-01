using AcDream.App.UI;
using AcDream.Plugin.Abstractions;

namespace AcDream.App.Tests.UI;

/// <summary>
/// Source rectangles become texture coordinates: cut to the image, the
/// destination shrinking with the cut; on a linear texture an edge inside
/// the image is pulled in by half a pixel and an edge on its border is not.
/// A nine-slice is up to nine such pieces, corners at their own size,
/// shrinking when the destination is too small for them.
/// </summary>
public sealed class CanvasImageRegionsTests
{
    private const int Sheet = 64;

    private static CanvasImagePiece Region(PluginRect source, PluginRect destination, bool linear)
    {
        Assert.True(CanvasImageRegions.TryMapRegion(Sheet, Sheet, linear, source, destination, out CanvasImagePiece piece));
        return piece;
    }

    private static CanvasImagePiece[] NineSlice(
        PluginRect destination, PluginInsets insets, PluginRect? source = null,
        bool drawCenter = true, bool linear = false)
    {
        var pieces = new CanvasImagePiece[CanvasImageRegions.MaximumNineSlicePieces];
        int count = CanvasImageRegions.NineSlice(Sheet, Sheet, linear, destination, insets, source, drawCenter, pieces);
        return pieces[..count];
    }

    private static float Texel(double pixels) => (float)(pixels / Sheet);

    [Fact]
    public void ARegionOnANearestTextureMapsToItsExactCoordinates()
    {
        CanvasImagePiece piece = Region(new PluginRect(16, 8, 16, 24), new PluginRect(5, 6, 32, 48), linear: false);

        Assert.Equal(new CanvasImagePiece(5, 6, 32, 48, 0.25f, 0.125f, 0.5f, 0.5f), piece);
    }

    [Fact]
    public void ARegionOnALinearTextureIsPulledInByHalfAPixelOnlyWhereItsEdgeIsInsideTheImage()
    {
        // The top edge is the image's own top: not pulled in. The others are inside.
        CanvasImagePiece piece = Region(new PluginRect(16, 0, 16, 16), new PluginRect(0, 0, 16, 16), linear: true);

        Assert.Equal((Texel(16.5), 0f, Texel(31.5), Texel(15.5)), (piece.U0, piece.V0, piece.U1, piece.V1));
        Assert.Equal((0f, 0f, 16f, 16f), (piece.X, piece.Y, piece.Width, piece.Height));
    }

    [Fact]
    public void TheWholeImageAsARegionSamplesExactlyAsAWholeImageDraw()
    {
        CanvasImagePiece piece = Region(new PluginRect(0, 0, Sheet, Sheet), new PluginRect(1, 2, 3, 4), linear: true);

        Assert.Equal(new CanvasImagePiece(1, 2, 3, 4, 0f, 0f, 1f, 1f), piece);
    }

    [Fact]
    public void ASourceRunningOffTheImageIsCutAndTheDestinationShrinksWithIt()
    {
        // Half the source is left of the image; the right half of the destination remains.
        CanvasImagePiece piece = Region(new PluginRect(-16, 56, 32, 16), new PluginRect(0, 0, 64, 32), linear: false);

        Assert.Equal(new CanvasImagePiece(32, 0, 32, 16, 0f, Texel(56), Texel(16), 1f), piece);
    }

    [Fact]
    public void ARegionNarrowerThanAPixelSamplesItsMiddleOnALinearTexture()
    {
        CanvasImagePiece piece = Region(new PluginRect(10.25, 10, 0.5, 4), new PluginRect(0, 0, 8, 8), linear: true);

        Assert.Equal(Texel(10.5), piece.U0);
        Assert.Equal(Texel(10.5), piece.U1);
        Assert.Equal((Texel(10.5), Texel(13.5)), (piece.V0, piece.V1));
    }

    [Theory]
    [InlineData(0, 0, 0, 16)]
    [InlineData(0, 0, 16, -1)]
    [InlineData(64, 0, 16, 16)]
    [InlineData(-32, 0, 16, 16)]
    [InlineData(double.NaN, 0, 16, 16)]
    [InlineData(0, double.PositiveInfinity, 16, 16)]
    public void AnEmptyOffImageOrNonFiniteSourceDrawsNothing(double x, double y, double width, double height)
    {
        Assert.False(CanvasImageRegions.TryMapRegion(
            Sheet, Sheet, true, new PluginRect(x, y, width, height), new PluginRect(0, 0, 16, 16), out _));
    }

    [Theory]
    [InlineData(0, 16)]
    [InlineData(16, -4)]
    [InlineData(double.NaN, 16)]
    public void AnEmptyOrNonFiniteDestinationDrawsNothing(double width, double height)
    {
        Assert.False(CanvasImageRegions.TryMapRegion(
            Sheet, Sheet, false, new PluginRect(0, 0, 16, 16), new PluginRect(0, 0, width, height), out _));
    }

    [Fact]
    public void ALargeNineSliceDrawsCornersAtTheirSizeAndStretchesTheRest()
    {
        CanvasImagePiece[] pieces = NineSlice(new PluginRect(10, 20, 100, 50), new PluginInsets(4, 6, 8, 10));

        Assert.Equal(9, pieces.Length);
        Assert.Equal(
            [
                (10f, 20f, 4f, 6f), (14f, 20f, 88f, 6f), (102f, 20f, 8f, 6f),
                (10f, 26f, 4f, 34f), (14f, 26f, 88f, 34f), (102f, 26f, 8f, 34f),
                (10f, 60f, 4f, 10f), (14f, 60f, 88f, 10f), (102f, 60f, 8f, 10f),
            ],
            pieces.Select(p => (p.X, p.Y, p.Width, p.Height)).ToArray());
        // Columns 0..4, 4..56, 56..64 and rows 0..6, 6..54, 54..64 of the whole image.
        Assert.Equal((0f, 0f, Texel(4), Texel(6)), (pieces[0].U0, pieces[0].V0, pieces[0].U1, pieces[0].V1));
        Assert.Equal((Texel(4), Texel(6), Texel(56), Texel(54)), (pieces[4].U0, pieces[4].V0, pieces[4].U1, pieces[4].V1));
        Assert.Equal((Texel(56), Texel(54), 1f, 1f), (pieces[8].U0, pieces[8].V0, pieces[8].U1, pieces[8].V1));
    }

    [Fact]
    public void ADestinationSmallerThanItsCornersShrinksThemInProportion()
    {
        // 6 wide for corners of 4 + 8: they shrink to 2 + 4 and the middle column goes.
        CanvasImagePiece[] pieces = NineSlice(new PluginRect(0, 0, 6, 40), new PluginInsets(4, 4, 8, 4));

        Assert.Equal(6, pieces.Length);
        Assert.Equal(
            [(0f, 0f, 2f, 4f), (2f, 0f, 4f, 4f), (0f, 4f, 2f, 32f), (2f, 4f, 4f, 32f), (0f, 36f, 2f, 4f), (2f, 36f, 4f, 4f)],
            pieces.Select(p => (p.X, p.Y, p.Width, p.Height)).ToArray());
        // The corners still show their whole inset of the image.
        Assert.Equal((0f, Texel(4)), (pieces[0].U0, pieces[0].U1));
        Assert.Equal((Texel(56), 1f), (pieces[1].U0, pieces[1].U1));
    }

    [Fact]
    public void AFrameWithoutItsMiddleIsTheEightPiecesAroundIt()
    {
        CanvasImagePiece[] pieces = NineSlice(new PluginRect(0, 0, 40, 40), PluginInsets.Uniform(8), drawCenter: false);

        Assert.Equal(8, pieces.Length);
        Assert.DoesNotContain(pieces, p => p.X == 8f && p.Y == 8f);
    }

    [Fact]
    public void ASourceSelectsTheFrameInsideASheetAndOnlyItsOuterEdgesArePulledIn()
    {
        // A 16x16 frame at (16, 16) on a linear sheet, insets of 4.
        CanvasImagePiece[] pieces = NineSlice(
            new PluginRect(0, 0, 32, 32), PluginInsets.Uniform(4), new PluginRect(16, 16, 16, 16), linear: true);

        Assert.Equal(9, pieces.Length);
        // Top-left corner: its outer edges are pulled in, the seams with its neighbours are not.
        Assert.Equal((Texel(16.5), Texel(16.5), Texel(20), Texel(20)), (pieces[0].U0, pieces[0].V0, pieces[0].U1, pieces[0].V1));
        // The middle touches no outer edge.
        Assert.Equal((Texel(20), Texel(20), Texel(28), Texel(28)), (pieces[4].U0, pieces[4].V0, pieces[4].U1, pieces[4].V1));
        // Bottom-right corner.
        Assert.Equal((Texel(28), Texel(28), Texel(31.5), Texel(31.5)), (pieces[8].U0, pieces[8].V0, pieces[8].U1, pieces[8].V1));
    }

    [Fact]
    public void WithoutSideInsetsTheMiddleColumnCarriesTheOuterEdges()
    {
        CanvasImagePiece[] pieces = NineSlice(
            new PluginRect(0, 0, 32, 32), new PluginInsets(0, 4, 0, 4), new PluginRect(16, 16, 16, 16), linear: true);

        Assert.Equal(3, pieces.Length);
        Assert.All(pieces, p => Assert.Equal((Texel(16.5), Texel(31.5)), (p.U0, p.U1)));
        Assert.Equal([(0f, 4f), (4f, 24f), (28f, 4f)], pieces.Select(p => (p.Y, p.Height)).ToArray());
    }

    [Fact]
    public void InsetsLargerThanTheSourceAreScaledDownToFitIt()
    {
        // The source is 16 wide; insets of 24 + 8 = 32 halve to 12 + 4.
        CanvasImagePiece[] pieces = NineSlice(
            new PluginRect(0, 0, 100, 100), new PluginInsets(24, 0, 8, 0), new PluginRect(0, 0, 16, 64));

        // No middle is left between the halves, so two columns, one row.
        Assert.Equal(2, pieces.Length);
        Assert.Equal((0f, Texel(12)), (pieces[0].U0, pieces[0].U1));
        Assert.Equal((Texel(12), Texel(16)), (pieces[1].U0, pieces[1].U1));
        Assert.Equal((12f, 4f), (pieces[0].Width, pieces[1].Width));
    }

    [Theory]
    [InlineData(-1, 0, 0, 0)]
    [InlineData(0, double.NaN, 0, 0)]
    [InlineData(0, 0, double.PositiveInfinity, 0)]
    public void ABadInsetDrawsNothing(double left, double top, double right, double bottom)
    {
        Assert.Empty(NineSlice(new PluginRect(0, 0, 40, 40), new PluginInsets(left, top, right, bottom)));
    }

    [Fact]
    public void ANineSliceWithASourceOffTheImageOrAnEmptyDestinationDrawsNothing()
    {
        Assert.Empty(NineSlice(new PluginRect(0, 0, 40, 40), PluginInsets.Uniform(4), new PluginRect(100, 0, 16, 16)));
        Assert.Empty(NineSlice(new PluginRect(0, 0, 0, 40), PluginInsets.Uniform(4)));
    }
}
