// Copyright (c) OpenAC contributors.
// Distributed under the terms of the MIT license.

using AcDream.Plugin.Abstractions;

namespace AcDream.Plugin.Tests;

/// <summary>
/// Image regions are additive: a painter written before them still builds
/// and accepts every region call, drawing nothing rather than the whole
/// sheet the region was cut from.
/// </summary>
public sealed class PluginCanvasImageRegionsContractTests
{
    /// <summary>A painter that implements only what the contract had before image regions.</summary>
    private sealed class OlderPainter : IPluginPainter
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

    [Fact]
    public void APainterWrittenBeforeImageRegionsAcceptsEveryRegionCallAndDrawsNothing()
    {
        var older = new OlderPainter();
        IPluginPainter painter = older;
        var sheet = new PluginImage(1, 64, 64);
        var source = new PluginRect(16, 0, 16, 16);
        var destination = new PluginRect(0, 0, 32, 32);

        painter.DrawImageRegion(sheet, source, destination, PluginColor.White);
        painter.DrawImageRegionTransformed(sheet, source, destination, PluginColor.White, 0.5, new PluginPoint(16, 16));
        painter.DrawImageNineSlice(sheet, destination, PluginInsets.Uniform(4), PluginColor.White);
        painter.DrawImageNineSlice(
            sheet, destination, new PluginInsets(1, 2, 3, 4), PluginColor.White, source, drawCenter: false);

        Assert.Equal(0, older.Calls);
    }

    [Fact]
    public void UniformInsetsAreTheSameOnEverySide()
    {
        Assert.Equal(new PluginInsets(6, 6, 6, 6), PluginInsets.Uniform(6));
        Assert.Equal(new PluginInsets(0, 0, 0, 0), default(PluginInsets));
    }
}
