using AcDream.App.UI.Layout;
using AcDream.Plugin.Abstractions;

namespace AcDream.App.Tests.UI.Layout;

public sealed class PluginCanvasPlacementTests
{
    [Theory]
    [InlineData(PluginCanvasAnchor.TopLeft, 10, 20)]
    [InlineData(PluginCanvasAnchor.TopCenter, 310, 20)]
    [InlineData(PluginCanvasAnchor.TopRight, 610, 20)]
    [InlineData(PluginCanvasAnchor.CenterLeft, 10, 270)]
    [InlineData(PluginCanvasAnchor.Center, 310, 270)]
    [InlineData(PluginCanvasAnchor.CenterRight, 610, 270)]
    [InlineData(PluginCanvasAnchor.BottomLeft, 10, 520)]
    [InlineData(PluginCanvasAnchor.BottomCenter, 310, 520)]
    [InlineData(PluginCanvasAnchor.BottomRight, 610, 520)]
    public void TopLeftAddsTheOffsetToTheAnchorsCorner(PluginCanvasAnchor anchor, float left, float top)
    {
        (float x, float y) = PluginCanvasPlacement.TopLeft(anchor, new PluginPoint(10, 20), 200, 100, 800, 600);
        Assert.Equal((left, top), (x, y));
    }

    [Fact]
    public void AnOnScreenCanvasKeepsItsOffset()
    {
        var offset = new PluginPoint(-30, -40);
        Assert.Equal(offset, PluginCanvasPlacement.ClampOnScreen(PluginCanvasAnchor.BottomRight, offset, 200, 100, 800, 600));
    }

    [Theory]
    [InlineData(PluginCanvasAnchor.TopLeft, 900, 700, 600, 500)]
    [InlineData(PluginCanvasAnchor.TopLeft, -50, -60, 0, 0)]
    [InlineData(PluginCanvasAnchor.BottomRight, 50, 60, 0, 0)]
    [InlineData(PluginCanvasAnchor.BottomRight, -900, -700, -600, -500)]
    [InlineData(PluginCanvasAnchor.Center, 500, -400, 300, -250)]
    public void AnOffScreenCanvasIsPulledFullyOnScreenKeepingItsAnchor(
        PluginCanvasAnchor anchor, double x, double y, double clampedX, double clampedY)
    {
        Assert.Equal(new PluginPoint(clampedX, clampedY),
            PluginCanvasPlacement.ClampOnScreen(anchor, new PluginPoint(x, y), 200, 100, 800, 600));
    }

    [Fact]
    public void ACanvasLargerThanTheScreenIsPinnedToTheTopLeft()
    {
        Assert.Equal(new PluginPoint(0, 0),
            PluginCanvasPlacement.ClampOnScreen(PluginCanvasAnchor.TopLeft, new PluginPoint(50, 50), 1000, 700, 800, 600));
    }

    [Fact]
    public void AnUnsizedScreenLeavesTheOffsetAlone()
    {
        var offset = new PluginPoint(5000, 5000);
        Assert.Equal(offset, PluginCanvasPlacement.ClampOnScreen(PluginCanvasAnchor.TopLeft, offset, 200, 100, 0, 0));
    }
}
