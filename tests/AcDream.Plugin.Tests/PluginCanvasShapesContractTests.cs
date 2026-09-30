// Copyright (c) OpenAC contributors.
// Distributed under the terms of the MIT license.

using AcDream.Plugin.Abstractions;

namespace AcDream.Plugin.Tests;

/// <summary>
/// Shapes are additive: a painter written before them still builds and
/// accepts every shape call, drawing nothing; a circle is the ellipse in
/// its square.
/// </summary>
public sealed class PluginCanvasShapesContractTests
{
    /// <summary>A painter that implements only what the contract had before shapes.</summary>
    private class OlderPainter : IPluginPainter
    {
        public int Calls { get; private set; }

        public int Width => 10;

        public int Height => 10;

        public void Clear(PluginColor color) => Calls++;

        public void FillRect(PluginRect rect, PluginColor color) => Calls++;

        public void StrokeRect(PluginRect rect, PluginColor color, float thickness = 1f) => Calls++;

        public void DrawLine(PluginPoint from, PluginPoint to, PluginColor color, float thickness = 1f) => Calls++;

        public void DrawText(string text, PluginPoint position, PluginColor color, bool outline = false) => Calls++;

        public PluginSize MeasureText(string text) => default;

        public void DrawImage(PluginImage image, PluginRect destination, PluginColor tint) => Calls++;

        public void DrawImageTransformed(
            PluginImage image, PluginRect destination, PluginColor tint, double rotationRadians,
            PluginPoint pivot, double scaleX = 1.0, double scaleY = 1.0) => Calls++;

        public void PushClip(PluginRect rect) => Calls++;

        public void PopClip() => Calls++;
    }

    /// <summary>A painter that draws ellipses and nothing else new.</summary>
    private sealed class EllipsePainter : OlderPainter, IPluginPainter
    {
        public List<(PluginRect Bounds, PluginColor Color)> Ellipses { get; } = [];

        public void FillEllipse(PluginRect bounds, PluginColor color) => Ellipses.Add((bounds, color));
    }

    [Fact]
    public void APainterWrittenBeforeShapesAcceptsEveryShapeAndDrawsNothing()
    {
        var older = new OlderPainter();
        IPluginPainter painter = older;
        var rect = new PluginRect(1, 2, 3, 4);

        painter.FillPolygon([new PluginPoint(0, 0), new PluginPoint(4, 0), new PluginPoint(0, 4)], PluginColor.White);
        painter.FillPolygon(
            [new PluginPoint(0, 0), new PluginPoint(4, 0), new PluginPoint(0, 4)],
            [PluginColor.White, PluginColor.White, PluginColor.Transparent]);
        painter.FillRoundedRect(rect, PluginCornerRadii.Uniform(2), PluginColor.White);
        painter.StrokeRoundedRect(rect, PluginCornerRadii.Uniform(2), PluginColor.White, 2f);
        painter.FillEllipse(rect, PluginColor.White);
        painter.StrokeEllipse(rect, PluginColor.White);
        painter.FillCircle(new PluginPoint(5, 5), 3, PluginColor.White);
        painter.FillRectGradient(rect, PluginColor.White, PluginColor.Transparent, PluginGradientDirection.Vertical);

        Assert.Equal(0, older.Calls);
    }

    [Fact]
    public void ACircleIsTheEllipseInItsSquare()
    {
        var ellipses = new EllipsePainter();
        IPluginPainter painter = ellipses;

        painter.FillCircle(new PluginPoint(10, 20), 5, new PluginColor(1, 2, 3, 4));

        Assert.Equal((new PluginRect(5, 15, 10, 10), new PluginColor(1, 2, 3, 4)), Assert.Single(ellipses.Ellipses));
    }

    [Fact]
    public void UniformRadiiAreTheSameAtEveryCornerAndGradientsRunTwoWays()
    {
        Assert.Equal(new PluginCornerRadii(6, 6, 6, 6), PluginCornerRadii.Uniform(6));
        Assert.Equal(new PluginCornerRadii(0, 0, 0, 0), default(PluginCornerRadii));
        Assert.Equal(0, (int)PluginGradientDirection.Horizontal);
        Assert.Equal(1, (int)PluginGradientDirection.Vertical);
    }
}
