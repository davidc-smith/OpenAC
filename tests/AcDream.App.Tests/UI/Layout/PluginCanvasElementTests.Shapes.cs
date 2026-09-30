using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using AcDream.App.Plugins;
using AcDream.App.Rendering;
using AcDream.App.UI;
using AcDream.App.UI.Layout;
using AcDream.Plugin.Abstractions;

namespace AcDream.App.Tests.UI.Layout;

/// <summary>
/// Shapes on a canvas: in painter's order among fills and images, cut to
/// the clip and the canvas, budgeted per paint, and bad input drawing
/// nothing and reported once per canvas.
/// </summary>
public sealed partial class PluginCanvasElementTests
{
    private static int CircleVertices(double radius)
    {
        var triangles = new List<UiColorVertex>();
        CanvasGeometry.FillEllipse(0f, 0f, (float)(radius * 2), (float)(radius * 2), Vector4.One, 1f, triangles);
        return triangles.Count;
    }

    /// <summary>What a run's triangles cover, each weighted by its mean alpha.</summary>
    private static double CoveredArea(IReadOnlyList<float> verts)
    {
        int stride = TextRenderer.FloatsPerVertex;
        double sum = 0;
        for (int i = 0; i + 3 * stride <= verts.Count; i += 3 * stride)
        {
            double ax = verts[i], ay = verts[i + 1];
            double bx = verts[i + stride], by = verts[i + stride + 1];
            double cx = verts[i + 2 * stride], cy = verts[i + 2 * stride + 1];
            double area = Math.Abs((bx - ax) * (cy - ay) - (cx - ax) * (by - ay)) / 2;
            sum += area * (verts[i + 7] + verts[i + stride + 7] + verts[i + 2 * stride + 7]) / 3;
        }
        return sum;
    }

    [Fact]
    public void ShapesKeepTheirPlaceAmongFillsAndImages()
    {
        var harness = new Harness();
        PluginImage art = harness.Registry.ImagesFor(harness.Owner).FromClientArt(1u);
        harness.Mount(Hud(), painter =>
        {
            painter.FillRect(new PluginRect(0, 0, 200, 100), new PluginColor(0, 0, 0, 160));
            painter.DrawImage(art, new PluginRect(10, 10, 32, 32), PluginColor.White);
            painter.FillCircle(new PluginPoint(100, 50), 30, PluginColor.White);
            painter.FillRect(new PluginRect(0, 90, 200, 10), PluginColor.White);
        });

        harness.Frame();

        Assert.Equal([0u, FakeImageBackend.ArtTexture, 0u], harness.SurfaceRuns.Select(run => run.Texture).ToArray());
        Assert.Equal([6, 6, CircleVertices(30) + 6], harness.SurfaceRuns.Select(run => run.VertexCount).ToArray());
    }

    [Fact]
    public void AGradientRunsFromOneColourToTheOther()
    {
        var harness = new Harness();
        harness.Mount(Hud(), painter => painter.FillRectGradient(
            new PluginRect(0, 0, 200, 100), new PluginColor(255, 0, 0), new PluginColor(0, 0, 255),
            PluginGradientDirection.Vertical));

        harness.Frame();

        IReadOnlyList<float> verts = Assert.Single(harness.Surface.Renderer.DebugSpriteSegmentVerts).Verts;
        for (int i = 0; i < verts.Count; i += TextRenderer.FloatsPerVertex)
        {
            // Solid from y = 0.5 (red) to y = 99.5 (blue); the fringe keeps its edge's colour.
            float expectedRed = 1f - Math.Clamp((verts[i + 1] - 0.5f) / 99f, 0f, 1f);
            Assert.InRange(verts[i + 4], expectedRed - 0.02f, expectedRed + 0.02f);
            Assert.InRange(verts[i + 4] + verts[i + 6], 1f - 1e-4f, 1f + 1e-4f);
        }
    }

    [Fact]
    public void AShapeIsCutToThePushedClip()
    {
        var harness = new Harness();
        harness.Mount(Hud(), painter =>
        {
            painter.PushClip(new PluginRect(0, 0, 50, 50));
            painter.FillCircle(new PluginPoint(50, 50), 20, PluginColor.White);
            painter.PopClip();
        });

        harness.Frame();

        IReadOnlyList<float> verts = Assert.Single(harness.Surface.Renderer.DebugSpriteSegmentVerts).Verts;
        for (int i = 0; i < verts.Count; i += TextRenderer.FloatsPerVertex)
        {
            Assert.InRange(verts[i], -1e-3f, 50f + 1e-3f);
            Assert.InRange(verts[i + 1], -1e-3f, 50f + 1e-3f);
        }
        double quarter = Math.PI * 20 * 20 / 4;
        Assert.InRange(CoveredArea(verts), quarter * 0.98, quarter * 1.02);
    }

    [Fact]
    public void AShapeStaysInsideTheCanvasAndOneWhollyOutsideDrawsNothing()
    {
        var harness = new Harness();
        harness.Mount(Hud(), painter =>
            painter.StrokeRoundedRect(
                new PluginRect(-20, 10, 300, 60), PluginCornerRadii.Uniform(10), PluginColor.White, 4f));

        harness.Frame();

        IReadOnlyList<float> verts = Assert.Single(harness.Surface.Renderer.DebugSpriteSegmentVerts).Verts;
        for (int i = 0; i < verts.Count; i += TextRenderer.FloatsPerVertex)
        {
            Assert.InRange(verts[i], -1e-3f, 200f + 1e-3f);
            Assert.InRange(verts[i + 1], -1e-3f, 100f + 1e-3f);
        }

        var outside = new Harness();
        (PluginCanvasRegistration registration, _) = outside.Mount(
            Hud(), painter => painter.FillEllipse(new PluginRect(500, 500, 10, 10), PluginColor.White));
        outside.Frame();
        Assert.Empty(outside.SurfaceRuns);
        Assert.True(registration.IsAvailable);
    }

    [Fact]
    public void BadShapeInputDrawsNothingNeverThrowsAndIsReportedOncePerCanvas()
    {
        var harness = new Harness();
        Action<IPluginPainter> paint = painter =>
        {
            painter.FillPolygon(
                [new PluginPoint(0, 0), new PluginPoint(20, 0), new PluginPoint(10, 5), new PluginPoint(20, 20), new PluginPoint(0, 20)],
                PluginColor.White);
            painter.FillPolygon([new PluginPoint(0, 0), new PluginPoint(20, 0)], PluginColor.White);
            painter.FillPolygon(
                [new PluginPoint(0, 0), new PluginPoint(20, 0), new PluginPoint(0, 20)], [PluginColor.White]);
            painter.FillRoundedRect(new PluginRect(0, 0, -5, 5), PluginCornerRadii.Uniform(1), PluginColor.White);
            painter.FillRoundedRect(new PluginRect(0, 0, 5, 5), PluginCornerRadii.Uniform(-1), PluginColor.White);
            painter.FillEllipse(new PluginRect(double.NaN, 0, 5, 5), PluginColor.White);
            painter.StrokeEllipse(new PluginRect(0, 0, 5, 5), PluginColor.White, -1f);
            painter.FillCircle(new PluginPoint(5, 5), double.PositiveInfinity, PluginColor.White);
            painter.FillRectGradient(
                new PluginRect(0, 0, 5, 5), PluginColor.White, PluginColor.White, (PluginGradientDirection)7);
        };
        (PluginCanvasRegistration hud, PluginCanvasElement element) = harness.Mount(Hud(), paint);
        harness.Mount(new PluginCanvasDescriptor("second", 50, 50), paint);

        harness.Frame();
        hud.Invalidate();
        harness.Frame();

        Assert.Empty(harness.SurfaceRuns);
        Assert.False(element.Guard.IsTripped);
        Assert.True(hud.IsAvailable);
        Assert.Equal(2, harness.Reports.Count);
        string hudReport = Assert.Single(harness.Reports, report => report.Contains("example.plugin/hud", StringComparison.Ordinal));
        Assert.Contains("FillPolygon", hudReport, StringComparison.Ordinal);
        Assert.Contains("convex", hudReport, StringComparison.Ordinal);
        Assert.Single(harness.Reports, report => report.Contains("example.plugin/second", StringComparison.Ordinal));
    }

    [Fact]
    public void ShapesWithNoAreaOrThicknessDrawNothingAndAreNotReported()
    {
        var harness = new Harness();
        harness.Mount(Hud(), painter =>
        {
            painter.FillEllipse(new PluginRect(0, 0, 0, 10), PluginColor.White);
            painter.StrokeRoundedRect(new PluginRect(0, 0, 10, 10), PluginCornerRadii.Uniform(2), PluginColor.White, 0f);
            painter.FillPolygon([new PluginPoint(0, 0), new PluginPoint(10, 0), new PluginPoint(20, 0)], PluginColor.White);
            painter.FillRectGradient(
                new PluginRect(0, 0, 10, 0), PluginColor.White, PluginColor.Transparent, PluginGradientDirection.Horizontal);
            painter.FillCircle(new PluginPoint(5, 5), 0, PluginColor.White);
        });

        harness.Frame();

        Assert.Empty(harness.SurfaceRuns);
        Assert.Empty(harness.Reports);
    }

    [Fact]
    public void APaintOverTheShapeBudgetDrawsWhatFitsReportsOnceAndCountsAsAnOverrun()
    {
        var harness = new Harness();
        int perCircle = CircleVertices(40);
        (PluginCanvasRegistration registration, PluginCanvasElement element) = harness.Mount(Hud(), painter =>
        {
            for (int i = 0; i < 400; i++)
                painter.FillCircle(new PluginPoint(100, 50), 40, PluginColor.White);
        });

        harness.Frame();

        int fits = PluginPainter.MaximumShapeVerticesPerPaint / perCircle;
        Assert.Equal(fits * perCircle, harness.SurfaceRuns.Sum(run => run.VertexCount));
        Assert.Equal(1, element.Guard.ConsecutiveOverruns);
        Assert.False(element.Guard.IsTripped);
        string report = Assert.Single(harness.Reports);
        Assert.Contains("example.plugin/hud", report, StringComparison.Ordinal);
        Assert.Contains("32768 shape vertices", report, StringComparison.Ordinal);

        for (int frame = 1; frame < UiDrawCallbackGuard.ConsecutiveOverrunsBeforeTrip; frame++)
        {
            registration.Invalidate();
            harness.Frame();
        }

        Assert.True(element.Guard.IsTripped);
        Assert.True(registration.IsDropped);
        Assert.Equal(2, harness.Reports.Count);
        Assert.Contains("drawing budget", harness.Reports[1], StringComparison.Ordinal);
    }

    [Fact]
    public void APainterKeptPastItsCallbackThrowsOnShapesToo()
    {
        var harness = new Harness();
        IPluginPainter? kept = null;
        harness.Mount(Hud(), painter => kept = painter);
        harness.Frame();

        Assert.NotNull(kept);
        Assert.Throws<InvalidOperationException>(() => kept.FillCircle(new PluginPoint(5, 5), 3, PluginColor.White));
        Assert.Throws<InvalidOperationException>(() => kept.FillPolygon(
            [new PluginPoint(0, 0), new PluginPoint(4, 0), new PluginPoint(0, 4)], PluginColor.White));
        Assert.Throws<InvalidOperationException>(() => kept.StrokeRoundedRect(
            new PluginRect(0, 0, 4, 4), PluginCornerRadii.Uniform(1), PluginColor.White));
    }
}
