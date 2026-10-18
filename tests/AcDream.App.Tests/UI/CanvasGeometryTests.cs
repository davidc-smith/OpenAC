using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Numerics;
using AcDream.App.Rendering;
using AcDream.App.UI;

namespace AcDream.App.Tests.UI;

/// <summary>
/// Canvas shapes as triangles: a fringe one pixel wide, fading to clear,
/// straddles every edge, so what the triangles cover weighted by alpha is
/// the shape's own area; curves are cut finely enough to stay within a
/// tenth of a pixel of the true curve; bad polygons are told apart from
/// empty ones.
/// </summary>
public sealed class CanvasGeometryTests
{
    private static readonly Vector4 White = Vector4.One;

    /// <summary>The area the triangles cover, each weighted by its mean alpha: exact for linear blending.</summary>
    private static double Covered(IReadOnlyList<UiColorVertex> triangles)
    {
        double sum = 0;
        for (int i = 0; i < triangles.Count; i += 3)
        {
            Vector2 a = triangles[i].Position, b = triangles[i + 1].Position, c = triangles[i + 2].Position;
            double area = Math.Abs((b.X - a.X) * (double)(c.Y - a.Y) - (c.X - a.X) * (double)(b.Y - a.Y)) / 2;
            sum += area * (triangles[i].Color.W + triangles[i + 1].Color.W + triangles[i + 2].Color.W) / 3;
        }
        return sum;
    }

    private static void AssertCovers(double expected, IReadOnlyList<UiColorVertex> triangles, double tolerance = 0.01)
    {
        double covered = Covered(triangles);
        Assert.True(
            Math.Abs(covered - expected) <= expected * tolerance,
            string.Create(CultureInfo.InvariantCulture, $"covered {covered:F3}, expected {expected:F3} within {tolerance:P1}"));
    }

    private static Vector4[] Repeat(Vector4 color, int count) => Enumerable.Repeat(color, count).ToArray();

    [Fact]
    public void ACircleCoversItsAreaWithASolidInsideAndAClearRim()
    {
        var triangles = new List<UiColorVertex>();

        CanvasGeometry.FillEllipse(30f, 10f, 40f, 40f, White, 1f, triangles);

        Assert.Equal(282, triangles.Count);
        AssertCovers(Math.PI * 20 * 20, triangles);
        var centre = new Vector2(50f, 30f);
        foreach (UiColorVertex vertex in triangles)
        {
            float distance = Vector2.Distance(vertex.Position, centre);
            if (vertex.Color.W == 1f)
            {
                Assert.InRange(distance, 0f, 19.5f + 1e-3f);
            }
            else
            {
                Assert.Equal(0f, vertex.Color.W);
                Assert.InRange(distance, 20.5f - 1e-3f, 20.5f + 1e-3f);
            }
        }
    }

    [Fact]
    public void ASquareCorneredRectangleHasItsFringeHalfInsideAndHalfOutside()
    {
        var triangles = new List<UiColorVertex>();

        CanvasGeometry.FillRoundedRect(10f, 10f, 20f, 20f, default, White, 1f, triangles);

        Assert.Equal(30, triangles.Count);
        AssertCovers(400, triangles, tolerance: 0.005);
        var clear = new HashSet<Vector2> { new(9.5f, 9.5f), new(30.5f, 9.5f), new(30.5f, 30.5f), new(9.5f, 30.5f) };
        var solid = new HashSet<Vector2> { new(10.5f, 10.5f), new(29.5f, 10.5f), new(29.5f, 29.5f), new(10.5f, 29.5f) };
        Assert.True(clear.SetEquals(triangles.Where(v => v.Color.W == 0f).Select(v => v.Position)));
        Assert.True(solid.SetEquals(triangles.Where(v => v.Color.W == 1f).Select(v => v.Position)));
    }

    [Fact]
    public void RoundedCornersTakeTheirCornersOut()
    {
        var triangles = new List<UiColorVertex>();

        CanvasGeometry.FillRoundedRect(10f, 10f, 60f, 30f, new CanvasCornerRadii(8f, 8f, 8f, 8f), White, 1f, triangles);

        AssertCovers(60 * 30 - (4 - Math.PI) * 8 * 8, triangles);
    }

    [Fact]
    public void RadiiTooLargeForTheRectangleShrinkTogether()
    {
        Assert.Equal(
            new CanvasCornerRadii(15f, 15f, 15f, 15f),
            CanvasGeometry.ClampRadii(new CanvasCornerRadii(40f, 40f, 40f, 40f), 60f, 30f));
        Assert.Equal(
            new CanvasCornerRadii(10f, 0f, 0f, 0f),
            CanvasGeometry.ClampRadii(new CanvasCornerRadii(10f, 0f, 0f, 0f), 60f, 30f));
        CanvasCornerRadii scaled = CanvasGeometry.ClampRadii(new CanvasCornerRadii(30f, 40f, 0f, 0f), 60f, 100f);
        Assert.InRange(scaled.TopLeft, 25.714f, 25.715f);
        Assert.InRange(scaled.TopRight, 34.285f, 34.286f);

        var triangles = new List<UiColorVertex>();
        CanvasGeometry.FillRoundedRect(10f, 10f, 60f, 30f, new CanvasCornerRadii(40f, 40f, 40f, 40f), White, 1f, triangles);
        AssertCovers(60 * 30 - (4 - Math.PI) * 15 * 15, triangles);
    }

    [Fact]
    public void AStrokeIsCentredOnTheOutline()
    {
        var rect = new List<UiColorVertex>();
        CanvasGeometry.StrokeRoundedRect(10f, 10f, 40f, 20f, default, White, 2f, 1f, rect);
        Assert.Equal(72, rect.Count);
        AssertCovers(42 * 22 - 38 * 18, rect, tolerance: 0.005);

        var circle = new List<UiColorVertex>();
        CanvasGeometry.StrokeEllipse(10f, 10f, 40f, 40f, White, 2f, 1f, circle);
        Assert.Equal(648, circle.Count);
        AssertCovers(Math.PI * (21 * 21 - 19 * 19), circle);
    }

    [Fact]
    public void AStrokeThinnerThanAPixelIsOnePixelWideAndProportionallyFainter()
    {
        var circle = new List<UiColorVertex>();
        CanvasGeometry.StrokeEllipse(10f, 10f, 40f, 40f, White, 0.5f, 1f, circle);
        // Two fringes and no solid core between them.
        Assert.Equal(432, circle.Count);
        Assert.Equal(0.5f, circle.Max(v => v.Color.W));
        AssertCovers(Math.PI * 40 * 0.5, circle);

        var rect = new List<UiColorVertex>();
        CanvasGeometry.StrokeRoundedRect(10f, 10f, 40f, 20f, new CanvasCornerRadii(5f, 5f, 5f, 5f), White, 0.25f, 1f, rect);
        Assert.Equal(0.25f, rect.Max(v => v.Color.W));
        AssertCovers((2 * (30 + 10) + 2 * Math.PI * 5) * 0.25, rect);
    }

    [Fact]
    public void AStrokeThickerThanTheShapeFillsIt()
    {
        var triangles = new List<UiColorVertex>();

        CanvasGeometry.StrokeRoundedRect(10f, 10f, 10f, 10f, default, White, 30f, 1f, triangles);

        AssertCovers(40 * 40, triangles, tolerance: 0.005);
    }

    [Fact]
    public void AStrokeThatLeavesNoHoleCoversTheGrownRectangle()
    {
        foreach ((float width, float height, float thickness) in new[] { (100f, 2f, 2f), (100f, 1f, 1f), (100f, 4f, 4f) })
        {
            var triangles = new List<UiColorVertex>();

            CanvasGeometry.StrokeRoundedRect(10f, 10f, width, height, default, White, thickness, 1f, triangles);

            AssertCovers((width + thickness) * (height + thickness), triangles);
        }
    }

    [Fact]
    public void APolygonCoversItsAreaWhicheverWayItWinds()
    {
        Vector2[] clockwise = [new(10f, 10f), new(50f, 10f), new(30f, 40f)];
        Vector2[] anticlockwise = [new(30f, 40f), new(50f, 10f), new(10f, 10f)];
        foreach (Vector2[] points in new[] { clockwise, anticlockwise })
        {
            var triangles = new List<UiColorVertex>();

            Assert.Equal(CanvasShapeOutcome.Drawn, CanvasGeometry.FillConvexPolygon(points, Repeat(White, 3), 1f, triangles));

            Assert.Equal(21, triangles.Count);
            AssertCovers(600, triangles, tolerance: 0.005);
        }
    }

    [Fact]
    public void EachCornerKeepsItsColourAndItsFringeFadesFromIt()
    {
        var red = new Vector4(1f, 0f, 0f, 1f);
        var blue = new Vector4(0f, 0f, 1f, 1f);
        Vector2[] strip = [new(0f, 0f), new(100f, 0f), new(100f, 10f), new(0f, 10f)];
        var triangles = new List<UiColorVertex>();

        CanvasGeometry.FillConvexPolygon(strip, [red, blue, blue, red], 1f, triangles);

        foreach (UiColorVertex vertex in triangles)
        {
            Vector4 expected = vertex.Position.X < 50f ? red : blue;
            Assert.Equal(expected with { W = vertex.Color.W }, vertex.Color);
            Assert.True(vertex.Color.W is 0f or 1f);
        }
        Assert.Contains(triangles, v => v.Position == new Vector2(0.5f, 0.5f) && v.Color == red);
        Assert.Contains(triangles, v => v.Position == new Vector2(100.5f, -0.5f) && v.Color == blue with { W = 0f });
    }

    [Fact]
    public void APolygonThatIsNotConvexIsToldApartFromOneWithNoArea()
    {
        var triangles = new List<UiColorVertex>();
        Vector2[] star = Enumerable.Range(0, 5)
            .Select(i => new Vector2(50f + 20f * MathF.Cos(i * 4f * MathF.PI / 5f), 50f + 20f * MathF.Sin(i * 4f * MathF.PI / 5f)))
            .ToArray();
        Vector2[] tooMany = Enumerable.Range(0, 65)
            .Select(i => new Vector2(50f * MathF.Cos(i * 2f * MathF.PI / 65f), 50f * MathF.Sin(i * 2f * MathF.PI / 65f)))
            .ToArray();

        Assert.Equal(CanvasShapeOutcome.NotConvex, CanvasGeometry.FillConvexPolygon(
            [new(0f, 0f), new(20f, 0f), new(10f, 5f), new(20f, 20f), new(0f, 20f)], Repeat(White, 5), 1f, triangles));
        Assert.Equal(CanvasShapeOutcome.NotConvex, CanvasGeometry.FillConvexPolygon(star, Repeat(White, 5), 1f, triangles));
        Assert.Equal(CanvasShapeOutcome.NotConvex, CanvasGeometry.FillConvexPolygon(
            [new(0f, 0f), new(20f, 0f), new(10f, 0f), new(10f, 10f)], Repeat(White, 4), 1f, triangles));
        Assert.Equal(CanvasShapeOutcome.NotConvex, CanvasGeometry.FillConvexPolygon(
            [new(0f, 0f), new(5f, 0f), new(5f, 3f), new(5f, 0f), new(10f, 0f), new(10f, 10f), new(0f, 10f)],
            Repeat(White, 7), 1f, triangles));
        Assert.Equal(CanvasShapeOutcome.Nothing, CanvasGeometry.FillConvexPolygon(
            [new(0f, 0f), new(10f, 0f), new(20f, 0f)], Repeat(White, 3), 1f, triangles));
        Assert.Equal(CanvasShapeOutcome.TooFewPoints, CanvasGeometry.FillConvexPolygon(
            [new(0f, 0f), new(10f, 10f)], Repeat(White, 2), 1f, triangles));
        Assert.Equal(CanvasShapeOutcome.TooManyPoints, CanvasGeometry.FillConvexPolygon(
            tooMany, Repeat(White, 65), 1f, triangles));
        Assert.Empty(triangles);
    }

    [Fact]
    public void RepeatedPointsAreMerged()
    {
        var triangles = new List<UiColorVertex>();

        CanvasShapeOutcome outcome = CanvasGeometry.FillConvexPolygon(
            [new(0f, 0f), new(0f, 0f), new(20f, 0f), new(20f, 20f), new(0f, 0f)], Repeat(White, 5), 1f, triangles);

        Assert.Equal(CanvasShapeOutcome.Drawn, outcome);
        Assert.Equal(21, triangles.Count);
    }

    [Fact]
    public void CurvesAreCutFinerTheLargerTheyAreOnScreen()
    {
        float turn = MathF.PI * 2f;
        Assert.Equal(0, CanvasGeometry.ArcSegments(0f, turn, 1f));
        Assert.Equal(1, CanvasGeometry.ArcSegments(0.05f, turn, 1f));
        Assert.Equal(32, CanvasGeometry.ArcSegments(20.5f, turn, 1f));
        Assert.Equal(45, CanvasGeometry.ArcSegments(40.5f, turn, 1f));
        // Two device pixels to a canvas pixel cut as finely as twice the radius.
        Assert.Equal(CanvasGeometry.ArcSegments(41f, turn, 1f), CanvasGeometry.ArcSegments(20.5f, turn, 0.5f));
    }

    [Fact]
    public void HugeShapesAreCutIntoABoundedNumberOfPieces()
    {
        var triangles = new List<UiColorVertex>();
        CanvasGeometry.FillRoundedRect(
            0f, 0f, 20000f, 20000f, new CanvasCornerRadii(10000f, 10000f, 10000f, 10000f), White, 1f, triangles);
        // Four corners of 32 chords: 132 points a ring.
        Assert.Equal(1182, triangles.Count);

        triangles.Clear();
        CanvasGeometry.FillEllipse(0f, 0f, 100000f, 100000f, White, 1f, triangles);
        // 256 points a ring.
        Assert.Equal(2298, triangles.Count);
    }

    [Fact]
    public void ShapesWithNoAreaOrNoThicknessWriteNothing()
    {
        var triangles = new List<UiColorVertex>();

        CanvasGeometry.FillEllipse(0f, 0f, 0f, 10f, White, 1f, triangles);
        CanvasGeometry.FillRoundedRect(0f, 0f, 10f, 0f, default, White, 1f, triangles);
        CanvasGeometry.StrokeEllipse(0f, 0f, 10f, 10f, White, 0f, 1f, triangles);
        CanvasGeometry.StrokeRoundedRect(0f, 0f, 10f, 10f, default, White, 0f, 1f, triangles);

        Assert.Empty(triangles);
    }

    [Fact]
    public void ShapesNarrowerThanTheirFringeStillCoverOnlyTheirOwnArea()
    {
        var strip = new List<UiColorVertex>();
        Assert.Equal(CanvasShapeOutcome.Drawn, CanvasGeometry.FillConvexPolygon(
            [new(0f, 0f), new(100f, 0f), new(100f, 0.4f), new(0f, 0.4f)], Repeat(White, 4), 1f, strip));
        AssertCovers(40, strip, tolerance: 0.02);
        Assert.All(strip, v => Assert.InRange(v.Color.W, 0f, 1f));

        // A diagonal quad 0.4 across: corners offset along a 45 degree line.
        var diagonal = new List<UiColorVertex>();
        float d = 0.4f / MathF.Sqrt(2f);
        Assert.Equal(CanvasShapeOutcome.Drawn, CanvasGeometry.FillConvexPolygon(
            [new(0f, 0f), new(100f, 100f), new(100f + d, 100f - d), new(d, -d)], Repeat(White, 4), 1f, diagonal));
        AssertCovers(0.4 * 100 * Math.Sqrt(2), diagonal, tolerance: 0.02);
        Assert.All(diagonal, v => Assert.InRange(v.Color.W, 0f, 1f));

        var rect = new List<UiColorVertex>();
        CanvasGeometry.FillRoundedRect(0f, 0f, 100f, 0.4f, default, White, 1f, rect);
        AssertCovers(40, rect, tolerance: 0.02);
        Assert.All(rect, v => Assert.InRange(v.Color.W, 0f, 1f));

        var dot = new List<UiColorVertex>();
        CanvasGeometry.FillEllipse(0f, 0f, 0.4f, 0.4f, White, 1f, dot);
        AssertCovers(Math.PI * 0.2 * 0.2, dot, tolerance: 0.02);
        Assert.All(dot, v => Assert.InRange(v.Color.W, 0f, 1f));
    }

    [Fact]
    public void AnEllipseThatIsNotACircleCoversItsArea()
    {
        var triangles = new List<UiColorVertex>();

        CanvasGeometry.FillEllipse(0f, 0f, 80f, 20f, White, 1f, triangles);

        AssertCovers(Math.PI * 40 * 10, triangles);
    }

    [Fact]
    public void ASliverTriangleDoesNotFold()
    {
        var triangles = new List<UiColorVertex>();

        Assert.Equal(CanvasShapeOutcome.Drawn, CanvasGeometry.FillConvexPolygon(
            [new(0f, 0f), new(100f, 0f), new(50f, 0.3f)], Repeat(White, 3), 1f, triangles));

        AssertCovers(15, triangles, tolerance: 0.02);
        Assert.All(triangles, v => Assert.InRange(v.Color.W, 0f, 1f));
    }

    [Fact]
    public void ATinyEdgeOnABigPolygonKeepsItSolid()
    {
        var chamfer = new List<UiColorVertex>();
        CanvasGeometry.FillConvexPolygon(
            [new(0f, 0f), new(100f, 0f), new(100f, 99.8f), new(99.8f, 100f), new(0f, 100f)],
            Repeat(White, 5), 1f, chamfer);
        AssertCovers(10000 - 0.02, chamfer, tolerance: 0.01);
        Assert.Equal(1f, chamfer.Max(v => v.Color.W));

        var near = new List<UiColorVertex>();
        CanvasGeometry.FillConvexPolygon(
            [new(0f, 0f), new(100f, 0f), new(100f, 100f), new(99.95f, 100f), new(0f, 100f)],
            Repeat(White, 5), 1f, near);
        AssertCovers(10000, near, tolerance: 0.01);
        Assert.Equal(1f, near.Max(v => v.Color.W));
    }

    [Fact]
    public void AOnePixelStripPolygonCoversItsArea()
    {
        var triangles = new List<UiColorVertex>();

        CanvasGeometry.FillConvexPolygon(
            [new(0f, 0f), new(100f, 0f), new(100f, 1f), new(0f, 1f)], Repeat(White, 4), 1f, triangles);

        AssertCovers(100, triangles, tolerance: 0.01);
    }

    [Fact]
    public void AStripPolygonThinnerThanAPixelIsALineNotASpindle()
    {
        var triangles = new List<UiColorVertex>();

        CanvasGeometry.FillConvexPolygon(
            [new(0f, 0f), new(100f, 0f), new(100f, 0.4f), new(0f, 0.4f)], Repeat(White, 4), 1f, triangles);

        float[] alphas = triangles.Select(v => v.Color.W).Where(w => w > 0f).ToArray();
        Assert.NotEmpty(alphas);
        Assert.InRange(alphas.Max() - alphas.Min(), 0f, 1e-4f);
    }

    [Fact]
    public void AShapeExactlyOnePixelAcrossCoversItsArea()
    {
        var dot = new List<UiColorVertex>();
        CanvasGeometry.FillEllipse(0f, 0f, 1f, 1f, White, 1f, dot);
        AssertCovers(Math.PI / 4, dot, tolerance: 0.02);

        var square = new List<UiColorVertex>();
        CanvasGeometry.FillRoundedRect(0f, 0f, 1f, 1f, default, White, 1f, square);
        AssertCovers(1, square, tolerance: 0.02);
    }

    [Fact]
    public void SharpCornersAndThinShapesCoverTheirArea()
    {
        var triangle = new List<UiColorVertex>();
        CanvasGeometry.FillConvexPolygon([new(0f, 0f), new(100f, 0f), new(50f, 5f)], Repeat(White, 3), 1f, triangle);
        AssertCovers(250, triangle, tolerance: 0.01);

        var needle = new List<UiColorVertex>();
        CanvasGeometry.FillConvexPolygon(
            [new(0f, 0f), new(32f, -1.5f), new(64f, 0f), new(32f, 1.5f)], Repeat(White, 4), 1f, needle);
        AssertCovers(96, needle, tolerance: 0.01);

        var nick = new List<UiColorVertex>();
        CanvasGeometry.FillConvexPolygon(
            [new(0f, 0f), new(100f, 0f), new(100f, 0.4f), new(99.95f, 0.4f), new(0f, 0.4f)], Repeat(White, 5), 1f, nick);
        AssertCovers(40, nick, tolerance: 0.02);
        Assert.True(nick.Max(v => v.Color.W) < 1f);

        Vector2[] slanted =
        [
            new(25.055227f, 0.76358986f), new(96.05029f, 0.80383503f),
            new(93.91133f, -0.019011175f), new(21.913465f, -0.21365674f),
        ];
        double shoelace = 0;
        for (int i = 0; i < 4; i++)
        {
            Vector2 a = slanted[i], b = slanted[(i + 1) % 4];
            shoelace += (double)a.X * b.Y - (double)b.X * a.Y;
        }
        var quad = new List<UiColorVertex>();
        CanvasGeometry.FillConvexPolygon(slanted, Repeat(White, 4), 1f, quad);
        AssertCovers(Math.Abs(shoelace) / 2, quad, tolerance: 0.02);

        var far = new List<UiColorVertex>();
        CanvasGeometry.FillConvexPolygon(
            [new(3000f, 2000f), new(3000.3f, 2000f), new(3000.3f, 2000.3f), new(3000f, 2000.3f)], Repeat(White, 4), 1f, far);
        AssertCovers(0.09, far, tolerance: 0.02);
    }

    private static double ShoelaceArea(Vector2[] polygon)
    {
        double sum = 0;
        for (int i = 0; i < polygon.Length; i++)
        {
            Vector2 a = polygon[i], b = polygon[(i + 1) % polygon.Length];
            sum += (double)a.X * b.Y - (double)b.X * a.Y;
        }
        return Math.Abs(sum) / 2;
    }

    [Fact]
    public void NearlyStraightCornersStillPullTheFringeIn()
    {
        Vector2[][] polygons =
        [
            [
                new(13.501612f, 1.4726709f), new(-14.647315f, -0.41921416f), new(-3.4119291f, -3.5657823f),
                new(-3.4024398f, -3.5651155f), new(10.416538f, -2.593997f),
            ],
            [
                new(4.0694532f, 22.838041f), new(3.5629122f, 48.943974f), new(2.2152758f, 78.548645f),
                new(-2.9446394f, 65.922005f), new(-2.9446561f, 65.89514f), new(-3.0255425f, -64.121704f),
            ],
            [
                new(0.80868554f, 0.015361176f), new(-0.118674494f, 0.030672016f), new(-0.92063224f, 0.0047680126f),
                new(-0.43840435f, -0.027287133f), new(-0.43777233f, -0.027265519f),
            ],
        ];
        foreach (Vector2[] polygon in polygons)
        {
            var triangles = new List<UiColorVertex>();
            CanvasGeometry.FillConvexPolygon(polygon, Repeat(White, polygon.Length), 1f, triangles);
            AssertCovers(ShoelaceArea(polygon), triangles, tolerance: 0.02);
        }
    }

    [Fact]
    public void TriangleLikePolygonsWithATinyExtraEdgeStaySolid()
    {
        Vector2[][] polygons =
        [
            [new(0f, 0f), new(20f, 0f), new(10f, 20f), new(9.99f, 20f)],
            [new(0f, 0f), new(10f, 5f), new(10f, 5.001f), new(0f, 10f)],
            [new(0f, 0f), new(10f, 0f), new(5.15f, 20f), new(4.85f, 20f)],
        ];
        foreach (Vector2[] polygon in polygons)
        {
            var triangles = new List<UiColorVertex>();
            CanvasGeometry.FillConvexPolygon(polygon, Repeat(White, polygon.Length), 1f, triangles);
            AssertCovers(ShoelaceArea(polygon), triangles, tolerance: 0.01);
            Assert.Equal(1f, triangles.Max(v => v.Color.W));
        }
    }
}
