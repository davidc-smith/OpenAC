// Copyright (c) OpenAC contributors.
// Distributed under the terms of the MIT license.

using AcDream.Plugin.Abstractions;

namespace AcDream.Plugin.Tests;

/// <summary>
/// The pixel scale is additive: a painter written before it still builds
/// and reports one screen pixel per canvas pixel.
/// </summary>
public sealed class PluginCanvasPixelScaleContractTests
{
    /// <summary>A painter that implements only what the contract had before the pixel scale.</summary>
    private sealed class OlderPainter : IPluginPainter
    {
        public int Width => 10;

        public int Height => 10;

        public void Clear(PluginColor color) { }

        public void FillRect(PluginRect rect, PluginColor color) { }

        public void StrokeRect(PluginRect rect, PluginColor color, float thickness = 1f) { }

        public void DrawLine(PluginPoint from, PluginPoint to, PluginColor color, float thickness = 1f) { }

        public void DrawText(string text, PluginPoint position, PluginColor color, bool outline = false) { }

        public PluginSize MeasureText(string text) => default;

        public void DrawImage(PluginImage image, PluginRect destination, PluginColor tint) { }

        public void DrawImageTransformed(
            PluginImage image, PluginRect destination, PluginColor tint, double rotationRadians,
            PluginPoint pivot, double scaleX = 1.0, double scaleY = 1.0) { }

        public void PushClip(PluginRect rect) { }

        public void PopClip() { }
    }

    [Fact]
    public void APainterWrittenBeforeThePixelScaleReportsOne()
    {
        IPluginPainter painter = new OlderPainter();

        Assert.Equal(1.0, painter.PixelScale);
    }
}
