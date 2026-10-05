using System;
using System.Collections.Generic;
using System.Numerics;

namespace AcDream.App.UI;

[System.Flags]
public enum AnchorEdges { None = 0, Left = 1, Top = 2, Right = 4, Bottom = 8 }

public readonly record struct UiCursorMedia(uint File, int HotspotX, int HotspotY)
{
    public bool IsValid => File != 0;
}

public abstract class UiElement
{
    public uint EventId { get; internal set; }

    public uint DatElementId { get; internal set; }

    /// <summary>Human-readable name for debugging / FindByName.</summary>
    public string? Name { get; set; }

    public bool AuthoredInvisible { get; internal set; }

    public bool AuthoredTooltipEnabled { get; internal set; }

    public string? AuthoredTooltipText { get; internal set; }

    public uint AuthoredTooltipRootElementId { get; internal set; }

    public uint AuthoredTooltipLayoutDid { get; internal set; }

    public uint SourceLayoutDid { get; internal set; }

    public Func<uint>? FoundObjectGuidProvider { get; set; }

    public uint AuthoredTooltipTextChildElementId { get; internal set; }

    public float? AuthoredTooltipDelaySeconds { get; internal set; }

    public int? AuthoredResizeMaxWidth { get; internal set; }
    public int? AuthoredResizeMinWidth { get; internal set; }

    public int? AuthoredResizeMaxHeight { get; internal set; }
    public int? AuthoredResizeMinHeight { get; internal set; }

    private readonly Dictionary<string, UiCursorMedia> _stateCursors = new();

    public IReadOnlyDictionary<string, UiCursorMedia> StateCursors => _stateCursors;

    public virtual string ActiveCursorStateName => "";

    public void SetStateCursors(IReadOnlyDictionary<string, UiCursorMedia> cursors)
    {
        _stateCursors.Clear();
        foreach (var kv in cursors)
        {
            if (kv.Value.IsValid)
                _stateCursors[kv.Key] = kv.Value;
        }
    }

    public UiCursorMedia ActiveCursor()
        => CursorForState(ActiveCursorStateName, allowFallback: true);

    public UiCursorMedia CursorForState(string stateName, bool allowFallback = true)
    {
        if (!string.IsNullOrEmpty(stateName)
            && _stateCursors.TryGetValue(stateName, out var named)
            && named.IsValid)
            return named;

        if (!allowFallback)
            return default;

        if (_stateCursors.TryGetValue("", out var direct) && direct.IsValid)
            return direct;
        if (_stateCursors.TryGetValue("Normal", out var normal) && normal.IsValid)
            return normal;

        return default;
    }

    // ── Geometry ────────────────────────────────────────────────────────
    /// <summary>X in the parent's local pixel space.</summary>
    public float Left   { get; set; }
    public float Top    { get; set; }
    public float Width  { get; set; }
    public float Height { get; set; }

    public Vector2 ScreenPosition
    {
        get
        {
            var p = new Vector2(Left, Top);
            UiElement child = this;
            var parent = Parent;
            while (parent is not null)
            {
                p += new Vector2(parent.Left, parent.Top) - parent.OffsetFor(child);
                child = parent;
                parent = parent.Parent;
            }
            return p;
        }
    }

    // ── Scrolling ───────────────────────────────────────────────────────

    /// <summary>
    /// Set on a child that stays put while its parent scrolls (a scrollbar):
    /// it is neither offset nor clipped to the parent's viewport.
    /// </summary>
    internal bool ScrollChrome { get; set; }

    /// <summary>
    /// The size of the part of this element its scrolled children are seen
    /// through, from its top-left corner; null (the default) for an element
    /// that does not scroll. While set, every child except
    /// <see cref="ScrollChrome"/> ones is clipped to it and moved by
    /// <see cref="ContentOffset"/>, in drawing, hit-testing and
    /// <see cref="ScreenPosition"/> alike.
    /// </summary>
    internal Vector2? ContentViewport { get; set; }

    /// <summary>How far this element's content is scrolled, in points; used only while <see cref="ContentViewport"/> is set.</summary>
    internal Vector2 ContentOffset { get; set; }

    /// <summary>How far <paramref name="child"/> is moved by this element's scrolling.</summary>
    private Vector2 OffsetFor(UiElement child) =>
        ContentViewport is null || child.ScrollChrome ? Vector2.Zero : ContentOffset;

    // ── State flags ─────────────────────────────────────────────────────
    private bool _visible = true;
    public bool Visible
    {
        get => _visible;
        set
        {
            if (_visible == value) return;

            UiRoot? root = FindRoot();
            root?.OnElementVisibilityChanging(this, value);
            _visible = value;
            root?.OnElementVisibilityChanged(this, value);
        }
    }
    private bool _enabled = true;
    public bool Enabled
    {
        get => _enabled;
        set
        {
            if (_enabled == value) return;
            _enabled = value;
            OnEnabledChanged();
        }
    }

    public bool ClickThrough    { get; set; }

    /// <summary>
    /// Optional live visibility reader, evaluated once per tick. Markup
    /// <c>visible="{Binding}"</c> uses this so a panel can show and hide itself
    /// from its binding object's state without the owner touching UI objects.
    /// </summary>
    public Func<bool>? VisibleSource { get; set; }

    public Func<bool>? EnabledSource { get; set; }

    /// <summary>
    /// Whether a left press (or Tab, with <see cref="TabStop"/>) gives this
    /// element keyboard focus. Virtual so an element whose answer depends
    /// on live state can compute it when the root asks.
    /// </summary>
    public virtual bool AcceptsFocus { get; set; }
    public bool FocusOnMouseClick { get; set; } = true;
    public bool TabStop         { get; set; }

    /// <summary>
    /// True if this is a text-entry (edit box); used by focus routing
    /// to suppress global hotkeys while typing.
    /// </summary>
    public bool IsEditControl   { get; set; }

    private int _zOrder;

    /// <summary>Painter's-algorithm z-order within siblings. Higher = on top.</summary>
    public int ZOrder
    {
        get => _zOrder;
        set
        {
            if (_zOrder == value) return;
            _zOrder = value;
            Parent?.InvalidateChildOrder();
        }
    }

    public float Opacity        { get; set; } = 1f;

    public bool Draggable { get; set; }

    public bool WindowMoveHandle { get; set; }

    /// <summary>
    /// Called on each pointer move while this window is being dragged by a
    /// move handle, with where the drag would put it (already kept inside the
    /// parent) and the pointer's position. A window that snaps somewhere
    /// changes <paramref name="left"/> and <paramref name="top"/>. Most windows
    /// leave them alone.
    /// </summary>
    internal virtual void ConstrainWindowDrag(ref float left, ref float top, int pointerX, int pointerY) { }

    public bool ConstrainResizeToParent { get; set; }

    public bool Resizable { get; set; }

    public bool CapturesPointerDrag { get; set; }

    /// <summary>Set to make this element an interactive mask over an image map.
    /// Null (the default) leaves every pointer message to the element itself.</summary>
    public UiPointerRegion? PointerRegion { get; set; }

    public virtual bool IsDragSource => PointerRegion?.DragPayloadAt is not null;

    public virtual bool HandlesClick => false;

    public virtual bool ReceivesHoverMouseMove => false;

    /// <summary>Minimum size enforced while resizing.</summary>
    public float MinWidth { get; set; } = 40f;
    public float MinHeight { get; set; } = 40f;

    /// <summary>Maximum size enforced while resizing (default unbounded).</summary>
    public float MaxWidth { get; set; } = float.MaxValue;
    public float MaxHeight { get; set; } = float.MaxValue;

    public bool ResizeX { get; set; } = true;
    public bool ResizeY { get; set; } = true;

    /// <summary>Which of the four FLAT border runs (the spans between the
    /// corners) of the synthesized window border begin a resize; the rest of
    /// that border is the move affordance. The four corner squares are NOT
    /// governed by this set - a corner always offers its own two sides,
    /// filtered only by <see cref="ResizeX"/> / <see cref="ResizeY"/>, so a
    /// height-only window still resizes from its corners while its flat top
    /// run stays a move handle. (An authored resize grip is a separate,
    /// explicit region and this set still gates it.)</summary>
    public ResizeEdges ResizableEdges { get; set; } =
        ResizeEdges.Left | ResizeEdges.Right | ResizeEdges.Top | ResizeEdges.Bottom;

    /// <summary>Edges this element anchors to in its parent. Default Left|Top
    /// (pinned top-left, fixed size — no reflow). Left|Right stretches width.</summary>
    private AnchorEdges _anchors = AnchorEdges.Left | AnchorEdges.Top;

    public AnchorEdges Anchors
    {
        get => _anchors;
        set
        {
            _anchors = value;
            LayoutPolicy = null;
            _anchorCaptured = false;
        }
    }

    public UiLayoutPolicy? LayoutPolicy { get; set; }

    // ── Tree structure ──────────────────────────────────────────────────
    public UiElement? Parent { get; private set; }

    private readonly List<UiElement> _children = new();
    private UiElement[]? _childrenBackToFront;
    private UiElement[]? _childrenFrontToBack;
    public IReadOnlyList<UiElement> Children => _children;

    public virtual void AddChild(UiElement child)
    {
        if (child.Parent is not null) child.Parent.RemoveChild(child);
        child.Parent = this;
        _children.Add(child);
        InvalidateChildOrder();
    }

    public static UiElement? FindDescendant(UiElement root, uint datElementId)
    {
        ArgumentNullException.ThrowIfNull(root);
        if (root.DatElementId == datElementId) return root;
        foreach (UiElement child in root.Children)
        {
            UiElement? found = FindDescendant(child, datElementId);
            if (found is not null) return found;
        }
        return null;
    }

    public virtual bool RemoveChild(UiElement child)
    {
        if (!_children.Contains(child)) return false;
        FindRoot()?.OnSubtreeRemoving(child);
        _children.Remove(child);
        child.Parent = null;
        InvalidateChildOrder();
        return true;
    }

    internal UiElement[] ChildrenBackToFrontSnapshot()
    {
        if (_childrenBackToFront is not null)
            return _childrenBackToFront;

        _childrenBackToFront = _children.ToArray();
        Array.Sort(
            _childrenBackToFront,
            static (a, b) => a.ZOrder.CompareTo(b.ZOrder));
        return _childrenBackToFront;
    }

    internal UiElement[] ChildrenFrontToBackSnapshot()
    {
        if (_childrenFrontToBack is not null)
            return _childrenFrontToBack;

        UiElement[] backToFront = ChildrenBackToFrontSnapshot();
        _childrenFrontToBack = new UiElement[backToFront.Length];
        for (int source = backToFront.Length - 1, destination = 0;
             source >= 0;
             source--, destination++)
        {
            _childrenFrontToBack[destination] = backToFront[source];
        }

        return _childrenFrontToBack;
    }

    private void InvalidateChildOrder()
    {
        _childrenBackToFront = null;
        _childrenFrontToBack = null;
    }

    public virtual bool ConsumesDatChildren => false;

    // ── Virtual overrides ───────────────────────────────────────────────

    protected virtual void OnDraw(UiRenderContext ctx) { }

    protected virtual void OnDrawAfterChildren(UiRenderContext ctx) { }

    /// <summary>Called just before each child is drawn, back to front.</summary>
    private protected virtual void OnDrawingChild(UiRenderContext ctx, UiElement child) { }

    protected virtual void OnDrawOverlay(UiRenderContext ctx) { }

    protected virtual bool ClipsChildren => true;

    protected virtual bool ExpandsClipForPopup => false;

    protected virtual void OnTick(double deltaSeconds) { }

    protected virtual void OnEnabledChanged() { }

    protected virtual bool OnHitTest(float localX, float localY)
        => localX >= 0f && localX < Width && localY >= 0f && localY < Height;

    public virtual bool OnEvent(in UiEvent e) => DispatchToPointerRegion(in e);

    /// <summary>Offer a pointer message to this element's image-map region.
    /// Returns true when the region consumed it. A widget class that overrides
    /// <see cref="OnEvent"/> offers the message here first, so a mask behaves the
    /// same whichever class the layout authored it as.
    /// <para>
    /// Event coordinates belong to the event's own target and are not re-based as
    /// the message bubbles, so only the target may read them. Recording a press
    /// never consumes it: elements above still act on a bubbled press.
    /// </para></summary>
    private protected bool DispatchToPointerRegion(in UiEvent e)
    {
        if (PointerRegion is not { } region || !ReferenceEquals(e.Target, this))
            return false;

        switch (e.Type)
        {
            case UiEventType.MouseDown:
            case UiEventType.RightDown:
                region.PressX = e.Data1;
                region.PressY = e.Data2;
                return false;

            case UiEventType.Click:
                if (region.Clicked is null) return false;
                region.Clicked(e.Data1, e.Data2);
                return true;

            case UiEventType.RightClick:
                if (region.RightClicked is null) return false;
                region.RightClicked(e.Data1, e.Data2);
                return true;

            case UiEventType.DragBegin:
                return region.DragPayloadAt is not null;

            case UiEventType.DragEnter:
                if (region.DragOverAt is null) return false;
                region.DragOverAt(e.Payload, e.Data1, e.Data2);
                return true;

            case UiEventType.DragOver:              // the root fires this one on LEAVE
                if (region.DragOverAt is null && region.DragLeft is null) return false;
                region.DragLeft?.Invoke();
                return true;

            case UiEventType.DropReleased:
                if (region.DropReleasedAt is null) return false;
                region.DropReleasedAt(e.Payload, e.Data1, e.Data2);
                return true;
        }
        return false;
    }

    public virtual object? GetDragPayload()
        => PointerRegion is { DragPayloadAt: { } payload } region
            ? payload(region.PressX, region.PressY)
            : null;

    public virtual (uint tex, int w, int h)? GetDragGhost()
        => PointerRegion is { DragGhostAt: { } ghost } region
            ? ghost(region.PressX, region.PressY)
            : null;

    internal virtual void SetDragSourceActive(bool active, object? payload) { }

    public Func<string?>? RuntimeTooltipTextSource { get; set; }

    public virtual string? GetTooltipText()
    {
        string? text = RuntimeTooltipTextSource?.Invoke();
        return string.IsNullOrWhiteSpace(text) ? null : text;
    }

    /// <summary>Announce that <see cref="GetTooltipText"/> would now return something else.
    /// A tooltip this element already owns is torn down and its dwell re-armed from the last
    /// cursor movement, so the reader sees the text that is current now rather than the text
    /// that happened to be current when the tooltip first appeared. Cheap while no tooltip is
    /// showing, so call it from every runtime tooltip-text change.</summary>
    protected void NotifyTooltipTextChanged() => FindRoot()?.ResetTooltip(this);


    internal void DrawSelfAndChildren(UiRenderContext ctx)
    {
        if (!Visible) return;

        ctx.PushTransform(Left, Top);
        ctx.PushAlpha(Opacity);
        bool clipsChildren = ClipsChildren;
        if (clipsChildren)
            ctx.PushClip(0f, 0f, Width, Height);
        try
        {
            if (!ctx.CurrentClipIsEmpty)
            {
                OnDraw(ctx);

                LayoutChildren();

                if (_children.Count > 0)
                {
                    UiElement[] ordered = ChildrenBackToFrontSnapshot();
                    for (int i = 0; i < ordered.Length; i++)
                    {
                        OnDrawingChild(ctx, ordered[i]);
                        if (ContentViewport is { } viewport && !ordered[i].ScrollChrome)
                            DrawScrolled(ctx, ordered[i], viewport);
                        else
                            ordered[i].DrawSelfAndChildren(ctx);
                    }
                }

                OnDrawAfterChildren(ctx);
            }
        }
        finally
        {
            if (clipsChildren)
                ctx.PopClip();
            ctx.PopAlpha();
            ctx.PopTransform();
        }
    }

    /// <summary>Draws a scrolled child: clipped to the viewport, moved by the content offset.</summary>
    private void DrawScrolled(UiRenderContext ctx, UiElement child, Vector2 viewport)
    {
        ctx.PushClip(0f, 0f, viewport.X, viewport.Y);
        ctx.PushTransform(-ContentOffset.X, -ContentOffset.Y);
        try
        {
            child.DrawSelfAndChildren(ctx);
        }
        finally
        {
            ctx.PopTransform();
            ctx.PopClip();
        }
    }

    internal void DrawOverlays(UiRenderContext ctx)
    {
        if (!Visible) return;
        ctx.PushTransform(Left, Top);
        ctx.PushAlpha(Opacity);
        try
        {
            if (ExpandsClipForPopup)
            {
                ctx.PushClipUnbounded();
                try { OnDrawOverlay(ctx); }
                finally { ctx.PopClip(); }
            }
            else
            {
                OnDrawOverlay(ctx);
            }
            if (_children.Count > 0)
            {
                bool clipsChildren = ClipsChildren;
                if (clipsChildren)
                    ctx.PushClip(0f, 0f, Width, Height);
                try
                {
                    UiElement[] ordered = ChildrenBackToFrontSnapshot();
                    for (int i = 0; i < ordered.Length; i++)
                    {
                        if (ContentViewport is null || ordered[i].ScrollChrome)
                        {
                            ordered[i].DrawOverlays(ctx);
                            continue;
                        }
                        ctx.PushTransform(-ContentOffset.X, -ContentOffset.Y);
                        try { ordered[i].DrawOverlays(ctx); }
                        finally { ctx.PopTransform(); }
                    }
                }
                finally
                {
                    if (clipsChildren)
                        ctx.PopClip();
                }
            }
        }
        finally
        {
            ctx.PopAlpha();
            ctx.PopTransform();
        }
    }

    internal void TickSelfAndChildren(double dt)
    {
        if (VisibleSource is { } visibility)
            Visible = visibility();
        if (!Visible) return;
        if (EnabledSource is { } enabled)
            Enabled = enabled();
        OnTick(dt);
        for (int i = 0; i < _children.Count; i++)
            _children[i].TickSelfAndChildren(dt);
    }

    /// <summary>
    /// Puts the children where they belong for this element's current size,
    /// just before they are drawn: by default each child's anchors. A flex
    /// container lays its items out here instead.
    /// </summary>
    private protected virtual void LayoutChildren()
    {
        for (int i = 0; i < _children.Count; i++)
            _children[i].ApplyAnchor(Width, Height);
    }

    internal UiElement? HitTest(float localX, float localY)
    {
        if (!Visible || !Enabled) return null;
        if (ClipsChildren
            && (localX < 0f || localX >= Width || localY < 0f || localY >= Height))
            return null;

        if (_children.Count > 0)
        {
            UiElement[] ordered = ChildrenFrontToBackSnapshot();
            for (int i = 0; i < ordered.Length; i++)
            {
                var c = ordered[i];
                float x = localX, y = localY;
                if (ContentViewport is { } viewport && !c.ScrollChrome)
                {
                    // Scrolled children are only reachable through the viewport.
                    if (x < 0f || y < 0f || x >= viewport.X || y >= viewport.Y) continue;
                    x += ContentOffset.X;
                    y += ContentOffset.Y;
                }
                var childHit = c.HitTest(x - c.Left, y - c.Top);
                if (childHit is not null) return childHit;
            }
        }

        if (ClickThrough) return null;
        return OnHitTest(localX, localY) ? this : null;
    }

    // ── Anchor layout ────────────────────────────────────────────────────

    private bool _anchorCaptured;
    private float _amL, _amT, _amR, _amB, _aw0, _ah0;

    internal void ApplyAnchor(float parentW, float parentH)
    {
        if (LayoutPolicy is not null)
        {
            var current = UiPixelRect.FromPositionAndSize(
                (int)Left,
                (int)Top,
                (int)Width,
                (int)Height);
            var parent = UiPixelRect.FromPositionAndSize(0, 0, (int)parentW, (int)parentH);
            var next = LayoutPolicy.Apply(current, parent);
            Left = next.X0;
            Top = next.Y0;
            Width = next.Width;
            Height = next.Height;
            return;
        }

        if (Anchors == AnchorEdges.None) return;
        if (!_anchorCaptured)
        {
            _amL = Left; _amT = Top;
            _amR = parentW - (Left + Width);
            _amB = parentH - (Top + Height);
            _aw0 = Width; _ah0 = Height;
            _anchorCaptured = true;
        }
        var (x, y, w, h) = ComputeAnchoredRect(Anchors, _amL, _amT, _amR, _amB, _aw0, _ah0, parentW, parentH);
        Left = x; Top = y; Width = w; Height = h;
    }

    internal void ResetAnchorCapture()
    {
        _anchorCaptured = false;
        if (LayoutPolicy is null || Parent is null) return;

        LayoutPolicy.Rebase(
            UiPixelRect.FromPositionAndSize(
                (int)Left,
                (int)Top,
                (int)Width,
                (int)Height),
            UiPixelRect.FromPositionAndSize(
                0,
                0,
                (int)Parent.Width,
                (int)Parent.Height));
    }

    internal void CaptureCurrentAnchorBaseline()
    {
        if (Parent is null || Anchors == AnchorEdges.None) return;
        _anchorCaptured = false;
        ApplyAnchor(Parent.Width, Parent.Height);
    }

    /// <summary>Fix the anchor baseline of every descendant to the rectangle it was
    /// authored with, measured against the size its parent was authored with. Call it
    /// once on a freshly built tree, while every element still sits where its author
    /// put it.
    ///
    /// Without it a child captures its baseline on the first frame it is drawn, and the
    /// anchor pass only descends into visible subtrees — so a subtree that stays hidden
    /// while its window is resized would measure its margins against the NEW size and
    /// keep its authored rectangle inside a larger parent the first time it is shown.
    /// Capturing up front makes where an anchored child ends up independent of when, or
    /// whether, it was ever visible. Applying an anchor at the size it was captured at
    /// is the identity, so this changes no geometry.</summary>
    internal void CaptureAuthoredAnchorBaselines()
    {
        for (int i = 0; i < _children.Count; i++)
        {
            UiElement child = _children[i];
            child.ApplyAnchor(Width, Height);
            child.CaptureAuthoredAnchorBaselines();
        }
    }

    internal void RebaseChildLayoutBaselines()
    {
        foreach (var child in _children)
            child.ResetAnchorCapture();
    }

    internal UiRoot? FindRoot()
    {
        UiElement e = this;
        while (e.Parent is not null) e = e.Parent;
        return e as UiRoot;
    }

    public static (float x, float y, float w, float h) ComputeAnchoredRect(
        AnchorEdges a, float mL, float mT, float mR, float mB,
        float w0, float h0, float parentW, float parentH)
    {
        bool l = (a & AnchorEdges.Left) != 0, r = (a & AnchorEdges.Right) != 0;
        float x, w;
        if (l && r) { x = mL; w = parentW - mR - mL; }
        else if (r) { w = w0; x = parentW - mR - w0; }
        else { x = mL; w = w0; }

        bool t = (a & AnchorEdges.Top) != 0, b = (a & AnchorEdges.Bottom) != 0;
        float y, h;
        if (t && b) { y = mT; h = parentH - mB - mT; }
        else if (b) { h = h0; y = parentH - mB - h0; }
        else { y = mT; h = h0; }

        if (w < 0) w = 0;
        if (h < 0) h = 0;
        return (x, y, w, h);
    }
}
