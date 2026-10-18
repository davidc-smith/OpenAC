// Copyright (c) OpenAC contributors.
// Distributed under the terms of the MIT license.

using AcDream.Plugin.Abstractions;
using AcDream.Plugin.Tests.Fixtures;

namespace AcDream.Plugin.Tests;

/// <summary>
/// Layers and stacking order are additive: a descriptor defaults to the
/// world layer at zero, the inert canvas keeps the order the plugin sets,
/// and a canvas from a host that predates layers answers zero.
/// </summary>
public sealed class PluginCanvasLayerContractTests
{
    /// <summary>A canvas written before layers existed: none of the new members.</summary>
    private sealed class OlderHostCanvas : IPluginCanvas
    {
        public string CanvasId => "old";
        public int Width => 1;
        public int Height => 1;
        public bool IsVisible { get; set; }
        public PluginCanvasAnchor Anchor { get; set; }
        public PluginPoint Offset { get; set; }
        public void Invalidate() { }
        public void Dispose() { }
    }

    [Fact]
    public void ADescriptorDefaultsToTheWorldLayerAtZero()
    {
        var descriptor = new PluginCanvasDescriptor("map", 64, 64);

        Assert.Equal(PluginCanvasLayer.World, descriptor.Layer);
        Assert.Equal(0, descriptor.ZOrder);
        Assert.Equal(0, (int)PluginCanvasLayer.World);
        Assert.Equal(1, (int)PluginCanvasLayer.AboveWindows);
    }

    [Fact]
    public void TheInertCanvasStartsAtTheDescriptorsZOrderAndKeepsWhatThePluginSets()
    {
        IPluginCanvas canvas = new NoOpPluginCanvas(
            new PluginCanvasDescriptor("hud", 8, 8) { Layer = PluginCanvasLayer.AboveWindows, ZOrder = 5 });

        Assert.Equal(5, canvas.ZOrder);
        canvas.ZOrder = -1;
        Assert.Equal(-1, canvas.ZOrder);
    }

    [Fact]
    public void TheFakeHostKeepsTheZOrder()
    {
        var host = new FakePluginHost();

        IPluginCanvas canvas = host.Ui.RegisterCanvas(
            new PluginCanvasDescriptor("map", 8, 8) { ZOrder = 2 }, _ => { });
        canvas.ZOrder = 6;

        Assert.Equal(6, canvas.ZOrder);
        Assert.False(canvas.IsAvailable);
    }

    [Fact]
    public void ACanvasFromAnOlderHostAnswersZeroAndIgnoresTheSet()
    {
        IPluginCanvas canvas = new OlderHostCanvas();

        canvas.ZOrder = 3;

        Assert.Equal(0, canvas.ZOrder);
    }
}
