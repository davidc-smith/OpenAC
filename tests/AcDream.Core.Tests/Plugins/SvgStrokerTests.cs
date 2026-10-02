using AcDream.Core.Plugins;

namespace AcDream.Core.Tests.Plugins;

public sealed class SvgStrokerTests
{
    private static SvgPaintLayer Stroke(
        SvgSubpath path, double width = 2, SvgLineCap cap = SvgLineCap.Butt, SvgLineJoin join = SvgLineJoin.Miter,
        double miterLimit = 4, SvgMatrix? transform = null) =>
        new(SvgPaintKind.Stroke, 1, [path], new SvgStrokeStyle(width, cap, join, miterLimit), transform ?? SvgMatrix.Identity);

    private static SvgSubpath Open(params SvgPoint[] points) => SvgOutline.Poly(points, closed: false);

    private static List<SvgPoint[]> Expand(SvgPaintLayer layer) => SvgStroker.Expand(layer, SvgMatrix.Identity, 0.05)!;

    [Fact]
    public void ANonFiniteTransformedPieceMakesExpandReturnNull()
    {
        var huge = new SvgMatrix(10, 0, 0, 10, 0, 0);
        Assert.Null(SvgStroker.Expand(Stroke(Open(new(0, 0), new(1e308, 0))), huge, 0.05));
    }

    [Fact]
    public void EveryPieceHasPositiveArea()
    {
        SvgSubpath zigzag = Open(new(0, 0), new(10, 0), new(10, 10), new(0, 10), new(5, 3));
        foreach (SvgLineJoin join in Enum.GetValues<SvgLineJoin>())
        foreach (SvgLineCap cap in Enum.GetValues<SvgLineCap>())
        {
            Assert.All(Expand(Stroke(zigzag, cap: cap, join: join)),
                piece => Assert.True(SvgStroker.SignedArea(piece) > 0));
        }
    }

    [Fact]
    public void AReflectingTransformStillGivesPositiveArea()
    {
        var flip = new SvgMatrix(-1, 0, 0, 1, 24, 0);
        Assert.All(Expand(Stroke(Open(new(0, 0), new(10, 0), new(10, 10)), transform: flip)),
            piece => Assert.True(SvgStroker.SignedArea(piece) > 0));
    }

    [Fact]
    public void AnOpenPolylineWithRoundJoinsAndCapsHasRectsJoinsAndCaps()
    {
        List<SvgPoint[]> pieces = Expand(Stroke(Open(new(0, 0), new(10, 0), new(10, 10)),
            cap: SvgLineCap.Round, join: SvgLineJoin.Round));
        Assert.Equal(2 + 1 + 2, pieces.Count);
    }

    [Fact]
    public void ButtCapsAddNothing()
    {
        Assert.Single(Expand(Stroke(Open(new(0, 0), new(10, 0)))));
    }

    [Fact]
    public void SquareCapsExtendTheEndsByHalfTheWidth()
    {
        SvgPoint[] rect = Assert.Single(Expand(Stroke(Open(new(0, 0), new(10, 0)), width: 2, cap: SvgLineCap.Square)));
        Assert.Equal(-1, rect.Min(p => p.X), 9);
        Assert.Equal(11, rect.Max(p => p.X), 9);
        Assert.Equal(-1, rect.Min(p => p.Y), 9);
        Assert.Equal(1, rect.Max(p => p.Y), 9);
    }

    [Fact]
    public void ARightAngleMiterReachesTheCorner()
    {
        List<SvgPoint[]> pieces = Expand(Stroke(Open(new(0, 0), new(10, 0), new(10, 10)), width: 2));
        SvgPoint[] miter = Assert.Single(pieces, p => p.Length == 4 && p.Contains(new SvgPoint(10, 0)));
        Assert.Contains(miter, p => Math.Abs(p.X - 11) < 1e-9 && Math.Abs(p.Y + 1) < 1e-9);
    }

    [Fact]
    public void ASharpMiterPastTheLimitFallsBackToABevel()
    {
        // A 10° spike needs a miter ratio of about 11.5, over the default limit of 4.
        double angle = 10 * Math.PI / 180;
        var tip = new SvgPoint(10, 0);
        var back = new SvgPoint(10 - 10 * Math.Cos(angle), 10 * Math.Sin(angle));
        List<SvgPoint[]> pieces = Expand(Stroke(Open(new(0, 0), tip, back)));
        Assert.Contains(pieces, p => p.Length == 3);
        Assert.DoesNotContain(pieces, p => p.Length == 4 && p.Contains(tip));
    }

    [Fact]
    public void ABevelJoinIsATriangle()
    {
        List<SvgPoint[]> pieces = Expand(Stroke(Open(new(0, 0), new(10, 0), new(10, 10)), join: SvgLineJoin.Bevel));
        Assert.Equal(3, pieces.Count);
        Assert.Single(pieces, p => p.Length == 3);
    }

    [Fact]
    public void AClosedSquareHasAJoinAtEveryCornerAndNoCaps()
    {
        SvgSubpath square = SvgOutline.Rect(0, 0, 10, 10, 0, 0);
        List<SvgPoint[]> pieces = Expand(Stroke(square, cap: SvgLineCap.Round));
        Assert.Equal(4 + 4, pieces.Count);
        Assert.DoesNotContain(pieces, p => p.Length > 4);
    }

    [Theory]
    [InlineData(SvgLineCap.Round, 1)]
    [InlineData(SvgLineCap.Square, 1)]
    [InlineData(SvgLineCap.Butt, 0)]
    public void AZeroLengthSubpathIsADotOnlyWithRoundOrSquareCaps(SvgLineCap cap, int expected)
    {
        Assert.Equal(expected, Expand(Stroke(Open(new(3, 3), new(3, 3)), cap: cap)).Count);
    }

    [Fact]
    public void ANonUniformScaleStretchesTheStroke()
    {
        var stretch = new SvgMatrix(1, 0, 0, 3, 0, 0);
        SvgPoint[] rect = Assert.Single(SvgStroker.Expand(Stroke(Open(new(0, 0), new(10, 0)), width: 2, transform: stretch),
            SvgMatrix.Identity, 0.05)!);
        Assert.Equal(6, rect.Max(p => p.Y) - rect.Min(p => p.Y), 9);
    }

    [Fact]
    public void FlatteningStaysWithinTolerance()
    {
        SvgSubpath circle = SvgOutline.Ellipse(0, 0, 10, 10);
        List<SvgPoint> points = SvgStroker.Flatten(circle, 0.05);
        for (int i = 1; i < points.Count; i++)
        {
            SvgPoint mid = (points[i - 1] + points[i]) * 0.5;
            Assert.InRange(mid.Length, 10 - 0.06, 10 + 0.03);
        }
    }

    [Fact]
    public void ToleranceIsMeasuredInDeviceUnits()
    {
        SvgSubpath circle = SvgOutline.Ellipse(0, 0, 1, 1);
        var big = new SvgMatrix(50, 0, 0, 50, 0, 0);
        int small = Expand(Stroke(circle, width: 0.1, join: SvgLineJoin.Bevel)).Count;
        int large = SvgStroker.Expand(Stroke(circle, width: 0.1, join: SvgLineJoin.Bevel), big, 0.05)!.Count;
        Assert.True(large > small, $"{large} pieces at 50x should exceed {small} at 1x");
    }

    private static void AssertAllFinite(IEnumerable<SvgPoint[]> pieces) =>
        Assert.All(pieces, piece => Assert.All(piece, p => Assert.True(double.IsFinite(p.X) && double.IsFinite(p.Y))));

    [Fact]
    public void ZeroWidthProducesNoNonFinitePoints()
    {
        foreach (SvgLineJoin join in Enum.GetValues<SvgLineJoin>())
        foreach (SvgLineCap cap in Enum.GetValues<SvgLineCap>())
        {
            AssertAllFinite(Expand(Stroke(Open(new(0, 0), new(10, 0), new(10, 10)), width: 0, cap: cap, join: join)));
            AssertAllFinite(Expand(Stroke(Open(new(3, 3), new(3, 3)), width: 0, cap: cap, join: join)));
        }
    }

    [Fact]
    public void CoincidentAndRepeatedPointsAreSkippedNotNormalised()
    {
        SvgSubpath path = Open(new(0, 0), new(0, 0), new(10, 0), new(10, 0), new(10, 10), new(10, 10));
        foreach (SvgLineJoin join in Enum.GetValues<SvgLineJoin>())
        foreach (SvgLineCap cap in Enum.GetValues<SvgLineCap>())
        {
            List<SvgPoint[]> pieces = Expand(Stroke(path, cap: cap, join: join));
            AssertAllFinite(pieces);
            Assert.NotEmpty(pieces);
        }
    }

    [Fact]
    public void AClosedSubpathCollapsedToOnePointIsADotOrNothing()
    {
        SvgSubpath collapsed = SvgOutline.Poly([new SvgPoint(5, 5), new SvgPoint(5, 5), new SvgPoint(5, 5)], closed: true);
        AssertAllFinite(Expand(Stroke(collapsed, cap: SvgLineCap.Butt)));
        Assert.Empty(Expand(Stroke(collapsed, cap: SvgLineCap.Butt)));
        Assert.Single(Expand(Stroke(collapsed, cap: SvgLineCap.Round)));
    }

    [Fact]
    public void AFullReversalIsFinite()
    {
        foreach (SvgLineJoin join in Enum.GetValues<SvgLineJoin>())
            AssertAllFinite(Expand(Stroke(Open(new(0, 0), new(10, 0), new(0, 0)), join: join)));
    }

    [Fact]
    public void ADegenerateTransformGivesNoPiecesAndNoNaN()
    {
        var squash = new SvgMatrix(0, 0, 0, 0, 0, 0);
        List<SvgPoint[]>? pieces = SvgStroker.Expand(Stroke(Open(new(0, 0), new(10, 0), new(10, 10)), transform: squash),
            SvgMatrix.Identity, 0.05);
        Assert.NotNull(pieces);
        Assert.Empty(pieces);
    }

    [Fact]
    public void AHostileInputExceedsThePointBudgetAndReturnsNullQuickly()
    {
        var segments = new List<SvgSegment>();
        for (int i = 0; i < 4096; i++)
            segments.Add(SvgSegment.Cubic(new(i, 100), new(-i, -100), new(i % 7, i % 5)));
        var path = new SvgSubpath(new SvgPoint(0, 0), segments, false);
        var huge = new SvgMatrix(1000, 0, 0, 1000, 0, 0);
        var watch = System.Diagnostics.Stopwatch.StartNew();
        List<SvgPoint[]>? result = SvgStroker.Expand(Stroke(path, cap: SvgLineCap.Round, join: SvgLineJoin.Round), huge, 0.05);
        watch.Stop();
        Assert.Null(result);
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(2), $"took {watch.Elapsed}");
    }

    [Fact]
    public void ANormalIconStillExpandsUnderTheBudget()
    {
        Assert.NotNull(SvgStroker.Expand(Stroke(SvgOutline.Ellipse(12, 12, 8, 8), join: SvgLineJoin.Round),
            new SvgMatrix(5, 0, 0, 5, 0, 0), 0.05));
    }

    [Fact]
    public void ASmallBudgetReturnsNull()
    {
        Assert.Null(SvgStroker.Expand(Stroke(Open(new(0, 0), new(10, 0))), SvgMatrix.Identity, 0.05, maxPoints: 3));
    }

    [Theory]
    [InlineData(SvgLineCap.Square)]
    [InlineData(SvgLineCap.Round)]
    public void AClosedTwoPointSubpathGetsNoCaps(SvgLineCap cap)
    {
        SvgSubpath line = SvgOutline.Poly([new SvgPoint(0, 0), new SvgPoint(10, 0)], closed: true);
        List<SvgPoint[]> pieces = Expand(Stroke(line, width: 2, cap: cap, join: SvgLineJoin.Miter));
        Assert.Equal(2, pieces.Count);
        Assert.Equal(0, pieces.SelectMany(p => p).Min(p => p.X), 9);
        Assert.Equal(10, pieces.SelectMany(p => p).Max(p => p.X), 9);
    }

    [Fact]
    public void AClosedTwoPointSubpathWithRoundJoinsAddsCircles()
    {
        SvgSubpath line = SvgOutline.Poly([new SvgPoint(0, 0), new SvgPoint(10, 0)], closed: true);
        List<SvgPoint[]> pieces = Expand(Stroke(line, width: 2, cap: SvgLineCap.Butt, join: SvgLineJoin.Round));
        Assert.Equal(4, pieces.Count);
        Assert.Equal(-1, pieces.SelectMany(p => p).Min(p => p.X), 2);
    }
}
