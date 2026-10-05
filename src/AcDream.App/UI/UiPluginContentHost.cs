using AcDream.App.UI.Layout.Flex;

namespace AcDream.App.UI;

/// <summary>
/// The content area of a plugin window with chrome: owns every authored
/// child, sits inside the frame's border and title bar, and stretches with
/// the frame. It draws nothing and lets presses through to the frame, so the
/// window still drags from any empty point. When the root panel has
/// <c>layout</c> it is the root of the window's flex tree.
/// </summary>
internal sealed class UiPluginContentHost : UiElement
{
    private FlexSize? _pendingMinimum;

    public UiPluginContentHost()
    {
        ClickThrough = true;
        Anchors = AnchorEdges.Left | AnchorEdges.Top | AnchorEdges.Right | AnchorEdges.Bottom;
    }

    /// <summary>The content area's flex layout, or null when its children are placed by coordinates.</summary>
    public UiFlexBox? Flex { get; private set; }

    /// <summary>
    /// Raised on the tick after a layout found that the smallest size the
    /// content fits in has changed (a longer caption), with that size. Raised
    /// from the tick rather than the draw so the window can be resized safely.
    /// </summary>
    internal event Action<FlexSize>? ContentMinimumChanged;

    internal bool ClipsChildrenForTest => ClipsChildren;

    /// <summary>Makes the content area a flex container; its children must then be added through <see cref="Flex"/>.</summary>
    internal UiFlexBox UseFlex()
    {
        Flex = new UiFlexBox { TracksMinimum = true };
        Flex.MinimumChanged += minimum => _pendingMinimum = minimum;
        return Flex;
    }

    protected override void OnTick(double deltaSeconds)
    {
        if (_pendingMinimum is not { } minimum) return;
        _pendingMinimum = null;
        ContentMinimumChanged?.Invoke(minimum);
    }

    private protected override void LayoutChildren()
    {
        Flex?.EnsureLayout(Width, Height);
        base.LayoutChildren();
    }
}
