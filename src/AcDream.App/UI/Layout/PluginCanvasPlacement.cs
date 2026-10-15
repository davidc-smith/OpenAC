using AcDream.Plugin.Abstractions;

namespace AcDream.App.UI.Layout;

/// <summary>
/// Where a screen canvas sits for its anchor and offset, and the offset
/// that brings it back on screen. Shared by the element that places the
/// canvas each tick and the persistence that puts a saved position back.
/// </summary>
internal static class PluginCanvasPlacement
{
    /// <summary>
    /// The canvas's top-left corner inside a layer of
    /// <paramref name="layerWidth"/> by <paramref name="layerHeight"/>,
    /// before rounding to whole pixels.
    /// </summary>
    internal static (float X, float Y) TopLeft(
        PluginCanvasAnchor anchor, PluginPoint offset, float width, float height, float layerWidth, float layerHeight)
    {
        float x = (float)offset.X;
        float y = (float)offset.Y;
        switch (anchor)
        {
            case PluginCanvasAnchor.TopCenter:
            case PluginCanvasAnchor.Center:
            case PluginCanvasAnchor.BottomCenter:
                x += (layerWidth - width) * 0.5f;
                break;
            case PluginCanvasAnchor.TopRight:
            case PluginCanvasAnchor.CenterRight:
            case PluginCanvasAnchor.BottomRight:
                x += layerWidth - width;
                break;
        }
        switch (anchor)
        {
            case PluginCanvasAnchor.CenterLeft:
            case PluginCanvasAnchor.Center:
            case PluginCanvasAnchor.CenterRight:
                y += (layerHeight - height) * 0.5f;
                break;
            case PluginCanvasAnchor.BottomLeft:
            case PluginCanvasAnchor.BottomCenter:
            case PluginCanvasAnchor.BottomRight:
                y += layerHeight - height;
                break;
        }
        return (x, y);
    }

    /// <summary>
    /// The offset, from the same anchor, that puts the whole canvas inside
    /// the layer: unchanged when it already is, and against the top or left
    /// edge on an axis where the canvas is larger than the layer. A layer
    /// under a pixel either way (not sized yet) leaves the offset alone.
    /// </summary>
    internal static PluginPoint ClampOnScreen(
        PluginCanvasAnchor anchor, PluginPoint offset, float width, float height, float layerWidth, float layerHeight)
    {
        if (layerWidth < 1f || layerHeight < 1f) return offset;
        (float x, float y) = TopLeft(anchor, offset, width, height, layerWidth, layerHeight);
        float clampedX = Math.Clamp(x, 0f, MathF.Max(0f, layerWidth - width));
        float clampedY = Math.Clamp(y, 0f, MathF.Max(0f, layerHeight - height));
        if (clampedX == x && clampedY == y) return offset;
        return new PluginPoint(offset.X + (clampedX - x), offset.Y + (clampedY - y));
    }
}
