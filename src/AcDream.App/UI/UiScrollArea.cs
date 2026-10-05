using System.Numerics;
using AcDream.App.UI.Layout.Flex;

namespace AcDream.App.UI;

/// <summary>An element whose content can scroll: a markup <c>&lt;group scroll&gt;</c> or a scrolling content area.</summary>
internal interface IUiScrollHost
{
    /// <summary>The element's scrolling, or null when it does not scroll.</summary>
    UiScrollArea? ScrollArea { get; }
}

/// <summary>
/// The scrolling of one markup container (a group, or a window's content
/// area, with <c>scroll</c>): a scroll model and a 16-point bar per axis it
/// scrolls on, and the host's <see cref="UiElement.ContentViewport"/> and
/// <see cref="UiElement.ContentOffset"/>. A bar is shown only while the
/// content overflows on its axis, and is placed beside the viewport rather
/// than over it; when both show, the corner square stays empty.
///
/// <para>Each frame, before the host's children are drawn, the host settles
/// the bars and passes the content's extent here: a flex host reads what the
/// engine settled (<see cref="ApplyFlex"/>); an absolute host applies its
/// children's anchors against the viewport and measures them
/// (<see cref="SettleAbsolute"/>). The scroll position is kept across
/// hide/show and theme changes, clamped when the extent or viewport shrinks,
/// and never saved.</para>
/// </summary>
internal sealed class UiScrollArea
{
    /// <summary>A bar's thickness, which the engine reserves too.</summary>
    internal const float BarSize = FlexLayout.ScrollbarThickness;

    /// <summary>Points moved by a bar's arrow button.</summary>
    internal const int LineStep = 16;

    /// <summary>Lines moved by one wheel step.</summary>
    internal const int WheelLines = 3;

    private readonly UiElement _host;
    private readonly Action<UiScrollArea> _settle;

    /// <param name="settle">
    /// Settles the bars and extent from the host's current geometry (the
    /// host's own per-frame step); run before revealing a descendant, as a
    /// group scrolled out of its parent's view has not been drawn.
    /// </param>
    public UiScrollArea(
        UiElement host, bool scrollsX, bool scrollsY, Func<uint, (uint, int, int)> resolve, Action<UiScrollArea> settle)
    {
        _host = host;
        _settle = settle;
        ScrollsX = scrollsX;
        ScrollsY = scrollsY;
        // Hit-testable, so the wheel reaches it over empty space; a press there
        // still drags the window, as the host neither handles clicks nor captures.
        host.ClickThrough = false;

        Vertical = new UiScrollbar
        {
            Model = Y, Width = BarSize, SpriteResolve = resolve, Anchors = AnchorEdges.None,
            ScrollChrome = true, ZOrder = int.MaxValue, Visible = false,
        };
        RetailScrollbarChrome.ApplyVertical(Vertical);
        Horizontal = new UiScrollbar
        {
            Model = X, Horizontal = true, Height = BarSize, SpriteResolve = resolve, Anchors = AnchorEdges.None,
            ScrollChrome = true, ZOrder = int.MaxValue, Visible = false,
        };
        RetailScrollbarChrome.ApplyHorizontal(Horizontal);
        if (scrollsY) host.AddChild(Vertical);
        if (scrollsX) host.AddChild(Horizontal);

        X.PositionChanged += SyncOffset;
        Y.PositionChanged += SyncOffset;
    }

    public bool ScrollsX { get; }
    public bool ScrollsY { get; }

    /// <summary>The horizontal position: its <see cref="UiScrollable.ScrollY"/> is the offset along x.</summary>
    public UiScrollable X { get; } = new() { LineHeight = LineStep };

    public UiScrollable Y { get; } = new() { LineHeight = LineStep };

    internal UiScrollbar Vertical { get; }
    internal UiScrollbar Horizontal { get; }

    /// <summary>Takes the bars, viewport and extent a flex arrange settled for the host's node.</summary>
    internal void ApplyFlex(FlexNode node) =>
        Apply(node.ContentSize.Width, node.ContentSize.Height, node.Viewport.Width, node.Viewport.Height,
            node.ScrollbarX, node.ScrollbarY);

    /// <summary>
    /// Places an absolute host's children for its viewport and settles its
    /// bars the way the engine does: start without bars, apply the children's
    /// anchors against the viewport and take the extent, add every bar whose
    /// axis overflows, and repeat in the smaller viewport until the bars stop
    /// changing. The extent is the right and bottom edges of the visible
    /// children, from the origin: a child at negative coordinates is not
    /// reachable by scrolling.
    /// </summary>
    internal void SettleAbsolute()
    {
        float width = _host.Width, height = _host.Height;
        bool barX = false, barY = false;
        float viewW = MathF.Max(0f, width), viewH = MathF.Max(0f, height);
        for (int pass = 0; pass < 3; pass++)
        {
            (float right, float bottom) = PlaceAbsolute(viewW, viewH);
            bool needX = barX || (ScrollsX && right > viewW);
            bool needY = barY || (ScrollsY && bottom > viewH);
            if (needX == barX && needY == barY) break;
            barX = needX;
            barY = needY;
            viewW = MathF.Max(0f, width - (barY ? BarSize : 0f));
            viewH = MathF.Max(0f, height - (barX ? BarSize : 0f));
        }
        (float extentW, float extentH) = PlaceAbsolute(viewW, viewH);
        Apply(extentW, extentH, viewW, viewH, barX, barY);
    }

    private (float Right, float Bottom) PlaceAbsolute(float viewW, float viewH)
    {
        float right = 0f, bottom = 0f;
        IReadOnlyList<UiElement> children = _host.Children;
        for (int i = 0; i < children.Count; i++)
        {
            UiElement child = children[i];
            if (child.ScrollChrome) continue;
            child.ApplyAnchor(viewW, viewH);
            if (!child.Visible) continue;
            right = MathF.Max(right, child.Left + child.Width);
            bottom = MathF.Max(bottom, child.Top + child.Height);
        }
        return (right, bottom);
    }

    private void Apply(float extentW, float extentH, float viewW, float viewH, bool barX, bool barY)
    {
        _host.ContentViewport = new Vector2(viewW, viewH);
        X.SetExtents(ScrollsX ? (int)MathF.Ceiling(extentW) : 0, (int)MathF.Floor(viewW));
        Y.SetExtents(ScrollsY ? (int)MathF.Ceiling(extentH) : 0, (int)MathF.Floor(viewH));

        // A bar sits along the host's far edge, hidden or not, so it never reaches past the host.
        Vertical.Visible = barY;
        Vertical.Left = MathF.Max(0f, _host.Width - BarSize);
        Vertical.Top = 0f;
        Vertical.Height = viewH;
        Horizontal.Visible = barX;
        Horizontal.Left = 0f;
        Horizontal.Top = MathF.Max(0f, _host.Height - BarSize);
        Horizontal.Width = viewW;
        SyncOffset();
    }

    private void SyncOffset() =>
        _host.ContentOffset = new Vector2(ScrollsX ? X.ScrollY : 0f, ScrollsY ? Y.ScrollY : 0f);

    /// <summary>
    /// Scrolls with the wheel: a vertical step on an area that scrolls
    /// vertically, a horizontal step on one that scrolls horizontally, and
    /// only while there is something to scroll; anything else is left to
    /// bubble on to an outer area.
    /// </summary>
    internal bool OnEvent(in UiEvent e)
    {
        if (e.Type == UiEventType.Scroll && ScrollsY && Y.HasOverflow)
        {
            Y.ScrollByLines(-Math.Sign(e.Data0) * WheelLines);
            return true;
        }
        if (e.Type == UiEventType.ScrollHorizontal && ScrollsX && X.HasOverflow)
        {
            X.ScrollByLines(-Math.Sign(e.Data0) * WheelLines);
            return true;
        }
        return false;
    }

    /// <summary>
    /// Scrolls the least distance that brings <paramref name="target"/>, a
    /// descendant of the host, into view: its start when it is larger than
    /// the viewport. Uses the geometry of the last frame.
    /// </summary>
    internal void Reveal(UiElement target)
    {
        _settle(this);
        if (_host.ContentViewport is not { } viewport) return;
        Vector2 start = target.ScreenPosition - _host.ScreenPosition + _host.ContentOffset;
        if (ScrollsY) Y.SetScrollY(Into(Y.ScrollY, start.Y, target.Height, viewport.Y));
        if (ScrollsX) X.SetScrollY(Into(X.ScrollY, start.X, target.Width, viewport.X));
    }

    private static int Into(int offset, float start, float size, float view)
    {
        if (start < offset) return (int)MathF.Floor(start);
        if (start + size > offset + view) return (int)MathF.Ceiling(MathF.Min(start, start + size - view));
        return offset;
    }
}
