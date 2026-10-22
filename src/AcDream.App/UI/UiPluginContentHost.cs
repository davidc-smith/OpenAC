namespace AcDream.App.UI;

/// <summary>
/// The content area of a plugin window with chrome: owns every authored
/// child, sits inside the frame's border and title bar, and stretches with
/// the frame. It draws nothing and lets presses through to the frame, so the
/// window still drags from any empty point.
/// </summary>
internal sealed class UiPluginContentHost : UiElement
{
    public UiPluginContentHost()
    {
        ClickThrough = true;
        Anchors = AnchorEdges.Left | AnchorEdges.Top | AnchorEdges.Right | AnchorEdges.Bottom;
    }

    internal bool ClipsChildrenForTest => ClipsChildren;
}
