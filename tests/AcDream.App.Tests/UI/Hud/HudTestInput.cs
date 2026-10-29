using AcDream.App.UI;

namespace AcDream.App.Tests.UI.Hud;

/// <summary>Input the way the root delivers it, for the decks' right-click menu tests.</summary>
internal static class HudTestInput
{
    /// <summary>A right press and release at a point in <paramref name="element"/>'s local coordinates.</summary>
    public static void RightClick(UiRoot root, UiElement element, float localX, float localY)
    {
        var origin = element.ScreenPosition;
        int x = (int)(origin.X + localX), y = (int)(origin.Y + localY);
        root.OnMouseMove(x, y);
        root.OnMouseDown(UiMouseButton.Right, x, y);
        root.OnMouseUp(UiMouseButton.Right, x, y);
    }
}
