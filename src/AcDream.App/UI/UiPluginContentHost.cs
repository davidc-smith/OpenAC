using AcDream.App.UI.Layout.Flex;

namespace AcDream.App.UI;

/// <summary>
/// The content area of a plugin window with chrome: owns every authored
/// child, sits inside the frame's border and title bar, and stretches with
/// the frame. It draws nothing and lets presses through to the frame, so the
/// window still drags from any empty point. When the root panel has
/// <c>layout</c> it is the root of the window's flex tree.
/// </summary>
internal sealed class UiPluginContentHost : UiElement, IUiScrollHost
{
    private FlexSize? _pendingMinimum;

    public UiPluginContentHost()
    {
        ClickThrough = true;
        Anchors = AnchorEdges.Left | AnchorEdges.Top | AnchorEdges.Right | AnchorEdges.Bottom;
    }

    /// <summary>The content area's flex layout, or null when its children are placed by coordinates.</summary>
    public UiFlexBox? Flex { get; private set; }

    /// <summary>The content area's scrolling (root <c>scroll</c>), or null.</summary>
    public UiScrollArea? ScrollArea { get; private set; }

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

    /// <summary>Makes the content area scroll on the given axes; call after <see cref="UseFlex"/> for a flex root.</summary>
    internal void UseScroll(bool scrollsX, bool scrollsY, Func<uint, (uint, int, int)> resolve)
    {
        ScrollArea = new UiScrollArea(this, scrollsX, scrollsY, resolve, Settle);
        if (Flex is not { } flex) return;
        flex.Node.ScrollX = scrollsX;
        flex.Node.ScrollY = scrollsY;
        flex.Node.ScrollbarSize = UiScrollArea.BarSize;
    }

    private void Settle(UiScrollArea area)
    {
        if (Flex is { } flex) area.ApplyFlex(flex.Node);
        else area.SettleAbsolute();
    }

    public override bool OnEvent(in UiEvent e) => ScrollArea?.OnEvent(e) == true || base.OnEvent(e);

    protected override void OnTick(double deltaSeconds)
    {
        if (_pendingMinimum is not { } minimum) return;
        _pendingMinimum = null;
        ContentMinimumChanged?.Invoke(minimum);
    }

    private protected override void LayoutChildren()
    {
        if (Flex is { } flex)
        {
            flex.EnsureLayout(Width, Height);
            ScrollArea?.ApplyFlex(flex.Node);
            base.LayoutChildren();
            return;
        }
        if (ScrollArea is { } area) Settle(area);
        else base.LayoutChildren();
    }
}
