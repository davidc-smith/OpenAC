using AcDream.Core.Plugins;

namespace AcDream.Core.Tests.Plugins;

public sealed class SvgOutlineTests
{
    private static List<SvgSubpath> Parse(string d)
    {
        var into = new List<SvgSubpath>();
        int commands = 0;
        Assert.True(SvgOutline.TryParsePath(d, into, ref commands, 4096, out string? reason), reason);
        return into;
    }

    private static string? Reject(string d, int maximumCommands = 4096)
    {
        int commands = 0;
        Assert.False(SvgOutline.TryParsePath(d, [], ref commands, maximumCommands, out string? reason));
        return reason;
    }

    private static void Near(SvgPoint expected, SvgPoint actual, double tolerance = 1e-6)
    {
        Assert.True(Math.Abs(expected.X - actual.X) <= tolerance && Math.Abs(expected.Y - actual.Y) <= tolerance,
            $"expected {expected}, got {actual}");
    }

    [Fact]
    public void AbsoluteAndRelativeLinesLandOnTheSamePoints()
    {
        SvgSubpath a = Assert.Single(Parse("M1 2 L4 6 H10 V0 Z"));
        SvgSubpath b = Assert.Single(Parse("m1 2 l3 4 h6 v-6 z"));
        Assert.Equal(a.Start, b.Start);
        Assert.Equal(a.Segments.Select(s => s.End), b.Segments.Select(s => s.End));
        Assert.True(a.Closed);
        Assert.Equal(new SvgPoint(10, 0), a.Segments[^1].End);
    }

    [Fact]
    public void ExtraPairsAfterAMovetoAreLinetos()
    {
        SvgSubpath p = Assert.Single(Parse("m1 1 2 0 0 2"));
        Assert.Equal(new SvgPoint(1, 1), p.Start);
        Assert.Equal([new SvgPoint(3, 1), new SvgPoint(3, 3)], p.Segments.Select(s => s.End));
        Assert.All(p.Segments, s => Assert.Equal(SvgSegmentKind.Line, s.Kind));
    }

    [Fact]
    public void CompactNumbersAndExponentsParse()
    {
        SvgSubpath p = Assert.Single(Parse("M1.5.5L-.5-1 1e1,2E-1"));
        Assert.Equal(new SvgPoint(1.5, 0.5), p.Start);
        Assert.Equal(new SvgPoint(-0.5, -1), p.Segments[0].End);
        Assert.Equal(new SvgPoint(10, 0.2), p.Segments[1].End);
    }

    [Fact]
    public void PackedArcFlagsParse()
    {
        SvgSubpath p = Assert.Single(Parse("M0 0a1 1 0 011 1"));
        Near(new SvgPoint(1, 1), p.Segments[^1].End);
        Assert.All(p.Segments, s => Assert.Equal(SvgSegmentKind.Cubic, s.Kind));
    }

    [Fact]
    public void SmoothCubicReflectsThePreviousControlPoint()
    {
        SvgSubpath p = Assert.Single(Parse("M0 0 C0 1 2 1 2 0 S4 -1 4 0"));
        Assert.Equal(new SvgPoint(2, -1), p.Segments[1].C1);
    }

    [Fact]
    public void SmoothCubicWithoutAPreviousCubicUsesTheCurrentPoint()
    {
        SvgSubpath p = Assert.Single(Parse("M0 0 L2 0 S4 -1 4 0"));
        Assert.Equal(new SvgPoint(2, 0), p.Segments[1].C1);
    }

    [Fact]
    public void SmoothQuadraticReflectsThePreviousControlPoint()
    {
        SvgSubpath p = Assert.Single(Parse("M0 0 Q1 1 2 0 T4 0"));
        Assert.Equal(SvgSegmentKind.Quadratic, p.Segments[1].Kind);
        Assert.Equal(new SvgPoint(3, -1), p.Segments[1].C1);
    }

    [Fact]
    public void ASemicircleArcEndsWhereItShouldAndPassesThroughTheTop()
    {
        // From (0,0) to (2,0) with radius 1, sweep=1: on screen (y down) it bulges upward.
        SvgSubpath p = Assert.Single(Parse("M0 0 A1 1 0 0 1 2 0"));
        Assert.Equal(2, p.Segments.Count);
        Near(new SvgPoint(1, -1), p.Segments[0].End);
        Near(new SvgPoint(2, 0), p.Segments[1].End);
    }

    [Fact]
    public void TheSweepFlagChoosesTheSide()
    {
        SvgSubpath p = Assert.Single(Parse("M0 0 A1 1 0 0 0 2 0"));
        Near(new SvgPoint(1, 1), p.Segments[0].End);
    }

    [Fact]
    public void TheLargeArcFlagTakesTheLongWayRound()
    {
        // Quarter-circle endpoints on a unit circle centred at (1,0)... small arc is 90°, large is 270°.
        SvgSubpath small = Assert.Single(Parse("M0 0 A1 1 0 0 1 1 -1"));
        SvgSubpath large = Assert.Single(Parse("M0 0 A1 1 0 1 1 1 -1"));
        Assert.Single(small.Segments);
        Assert.Equal(3, large.Segments.Count);
        Near(new SvgPoint(1, -1), large.Segments[^1].End);
    }

    [Fact]
    public void RadiiTooSmallAreScaledUp()
    {
        // Radius 0.5 cannot span 2 units, so it becomes 1: a semicircle through (1,-1).
        SvgSubpath p = Assert.Single(Parse("M0 0 A0.5 0.5 0 0 1 2 0"));
        Near(new SvgPoint(1, -1), p.Segments[0].End, 1e-9);
    }

    [Fact]
    public void AZeroRadiusArcIsALine()
    {
        SvgSubpath p = Assert.Single(Parse("M0 0 A0 3 0 0 1 2 0"));
        Assert.Equal(SvgSegmentKind.Line, Assert.Single(p.Segments).Kind);
    }

    [Fact]
    public void AnArcCubicStaysOnTheCircle()
    {
        SvgSubpath p = Assert.Single(Parse("M1 0 A1 1 0 0 1 0 1"));
        SvgSegment c = Assert.Single(p.Segments);
        // Midpoint of the cubic at t = 0.5.
        SvgPoint mid = (p.Start + c.C1 * 3 + c.C2 * 3 + c.End) * 0.125;
        Assert.InRange(mid.Length, 1 - 3e-4, 1 + 3e-4);
    }

    [Fact]
    public void AMovetoAfterCloseStartsANewSubpath()
    {
        List<SvgSubpath> paths = Parse("M0 0h2v2z m4 0h1");
        Assert.Equal(2, paths.Count);
        Assert.True(paths[0].Closed);
        Assert.Equal(new SvgPoint(4, 0), paths[1].Start);
        Assert.False(paths[1].Closed);
    }

    [Fact]
    public void DrawingAfterCloseWithoutAMovetoStartsAtTheSubpathStart()
    {
        List<SvgSubpath> paths = Parse("M1 1h2v2zl1 0");
        Assert.Equal(2, paths.Count);
        Assert.Equal(new SvgPoint(1, 1), paths[1].Start);
        Assert.Equal(new SvgPoint(2, 1), paths[1].Segments[0].End);
    }

    [Theory]
    [InlineData("L1 1")]
    [InlineData("M1")]
    [InlineData("M0 0 L1 x")]
    [InlineData("M0 0 A1 1 0 2 1 2 0")]
    [InlineData("M0 0 K1 1")]
    [InlineData("M0 0 L1e999 0")]
    public void MalformedPathDataIsRejected(string d)
    {
        Assert.NotNull(Reject(d));
    }

    [Fact]
    public void TheCommandLimitCountsImplicitRepeats()
    {
        string? reason = Reject("M0 0 1 1 2 2 3 3", maximumCommands: 3);
        Assert.Contains("more than 3 path commands", reason, StringComparison.Ordinal);
    }

    [Fact]
    public void EmptyPathDataDrawsNothing()
    {
        Assert.Empty(Parse("  "));
    }

    [Fact]
    public void AnEllipseIsFourCubicsThroughItsExtremes()
    {
        SvgSubpath e = SvgOutline.Ellipse(5, 5, 4, 2);
        Assert.Equal(new SvgPoint(9, 5), e.Start);
        Assert.Equal([new SvgPoint(5, 7), new SvgPoint(1, 5), new SvgPoint(5, 3), new SvgPoint(9, 5)],
            e.Segments.Select(s => s.End));
        Assert.Equal(new SvgPoint(9, 5 + 2 * SvgOutline.Kappa), e.Segments[0].C1);
    }

    [Fact]
    public void ARoundedRectHasFourLinesAndFourCorners()
    {
        SvgSubpath r = SvgOutline.Rect(0, 0, 10, 6, 2, 2);
        Assert.Equal(4, r.Segments.Count(s => s.Kind == SvgSegmentKind.Line));
        Assert.Equal(4, r.Segments.Count(s => s.Kind == SvgSegmentKind.Cubic));
        Assert.Equal(new SvgPoint(2, 0), r.Start);
        Assert.Equal(r.Start, r.Segments[^1].End);
    }

    [Fact]
    public void MatrixCompositionAppliesTheInnerTransformFirst()
    {
        var translate = new SvgMatrix(1, 0, 0, 1, 10, 0);
        var scale = new SvgMatrix(2, 0, 0, 2, 0, 0);
        Assert.Equal(new SvgPoint(12, 2), translate.Then(scale).Apply(new SvgPoint(1, 1)));
        Assert.Equal(new SvgPoint(22, 2), scale.Then(translate).Apply(new SvgPoint(1, 1)));
    }

    [Fact]
    public void MaximumScaleIsTheLargerStretch()
    {
        Assert.Equal(3, new SvgMatrix(3, 0, 0, 0.5, 0, 0).MaximumScale, 9);
        Assert.Equal(2, new SvgMatrix(0, 2, -2, 0, 0, 0).MaximumScale, 9);
    }

    [Theory]
    [InlineData("M0 0 l1e308 0 l1e308 0")]
    public void ComputedNonFiniteCoordinatesAreRejectedNamingTheOffset(string d)
    {
        Assert.Contains("non-finite coordinate at offset", Reject(d), StringComparison.Ordinal);
    }

    [Fact]
    public void AnArcWhoseRadiiSquareToInfinityFallsBackToAFiniteLine()
    {
        SvgSubpath p = Assert.Single(Parse("M0 0 a1e200 1e200 0 0 1 1e200 0"));
        SvgSegment s = Assert.Single(p.Segments);
        Assert.Equal(SvgSegmentKind.Line, s.Kind);
        Assert.True(double.IsFinite(s.End.X) && double.IsFinite(s.End.Y));
    }

    [Fact]
    public void AnArcSmallerThanTheDoubleSquareRangeFallsBackToAFiniteLine()
    {
        SvgSubpath p = Assert.Single(Parse("M0 0 a1 1 0 0 1 1e-200 0"));
        SvgSegment s = Assert.Single(p.Segments);
        Assert.Equal(SvgSegmentKind.Line, s.Kind);
        Assert.True(double.IsFinite(s.End.X) && double.IsFinite(s.End.Y));
    }

    [Fact]
    public void AZeroLengthArcAddsNoSegment()
    {
        SvgSubpath p = Assert.Single(Parse("M1 1 A1 1 0 0 1 1 1"));
        Assert.Empty(p.Segments);
    }

    [Fact]
    public void SmoothQuadraticAfterANonQuadraticUsesTheCurrentPointAsControl()
    {
        SvgSubpath p = Assert.Single(Parse("M0 0 L2 0 T4 0"));
        Assert.Equal(SvgSegmentKind.Quadratic, p.Segments[1].Kind);
        Assert.Equal(new SvgPoint(2, 0), p.Segments[1].C1);
    }

    [Theory]
    [InlineData("M0 0 L")]
    [InlineData("M0 0 L1e")]
    public void ACommandWithMissingOrTruncatedNumbersIsRejected(string d)
    {
        Assert.NotNull(Reject(d));
    }

    [Fact]
    public void PolyRejectsAnEmptyPointList()
    {
        Assert.Throws<ArgumentException>(() => SvgOutline.Poly([], false));
    }

    [Fact]
    public void PolyOfOnePointIsAnEmptySubpath()
    {
        SvgSubpath p = SvgOutline.Poly([new SvgPoint(1, 2)], false);
        Assert.Equal(new SvgPoint(1, 2), p.Start);
        Assert.Empty(p.Segments);
    }
}
