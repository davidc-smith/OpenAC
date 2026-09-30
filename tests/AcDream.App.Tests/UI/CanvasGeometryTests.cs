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
}
