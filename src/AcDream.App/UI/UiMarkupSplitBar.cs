using System.Numerics;
using Silk.NET.Input;

namespace AcDream.App.UI;

/// <summary>
/// A split's bar: the player drags it, or moves it with the arrow keys along
/// its axis while it has focus. Shows the retail resize cursor while enabled
/// and highlights on hover and while dragged.
/// </summary>
public sealed class UiMarkupSplitBar : UiElement
{
    public static readonly Vector4 ClassicFill = UiCheckLamp.UncheckedOuter;
    public static readonly Vector4 ClassicGrip = UiCheckLamp.UncheckedInner;
    public static readonly Vector4 ClassicHot = UiMarkupExpanderHeader.ClassicArrow;
    public static readonly Vector4 FocusColor = UiMarkupExpanderHeader.FocusColor;

    private static readonly IReadOnlyDictionary<string, UiCursorMedia> NoCursor =
        new Dictionary<string, UiCursorMedia>();

    private readonly UiMarkupSplit _owner;
    private readonly IReadOnlyDictionary<string, UiCursorMedia> _resizeCursor;
    private bool _cursorShown;
    private bool _focused;
    private bool _hovered;
    private bool _dragging;
    private float _grab;

    public PluginUiPalette? ThemePalette { get; set; }

    internal UiMarkupSplitBar(UiMarkupSplit owner)
    {
        _owner = owner;
        CapturesPointerDrag = true;
        RetailCursorCatalog.TryGetWindowControlCursor(
            owner.Horizontal ? CursorFeedbackKind.ResizeHorizontal : CursorFeedbackKind.ResizeVertical,
            out UiCursorMedia cursor);
        _resizeCursor = new Dictionary<string, UiCursorMedia> { [""] = cursor };
        SetStateCursors(_resizeCursor);
        _cursorShown = true;
    }

    private bool Live => Enabled && _owner.Enabled;

    /// <summary>Hovered or dragged: drawn highlighted.</summary>
    public bool Hot => Live && (_dragging || _hovered);

    /// <summary>The bar takes the hover, so it shows its split's tooltip.</summary>
    public override string? GetTooltipText() => _owner.GetTooltipText();

    protected override void OnTick(double deltaSeconds)
    {
        bool live = Live;
        if (live == _cursorShown)
            return;
        SetStateCursors(live ? _resizeCursor : NoCursor);
        _cursorShown = live;
    }

    private float Along(in UiEvent e) => _owner.Horizontal ? e.Data1 : e.Data2;

    private float Offset => _owner.Horizontal ? Left : Top;

    private int Direction(Key key) => (_owner.Horizontal, key) switch
    {
        (true, Key.Left) or (false, Key.Up) => -1,
        (true, Key.Right) or (false, Key.Down) => 1,
        _ => 0,
    };

    public override bool OnEvent(in UiEvent e)
    {
        switch (e.Type)
        {
            case UiEventType.FocusGained when TabStop:
                _focused = true;
                return true;
            case UiEventType.FocusLost when TabStop:
                _focused = false;
                return true;
            case UiEventType.HoverEnter:
                _hovered = true;
                return true;
            case UiEventType.HoverLeave:
                _hovered = false;
                return true;
            case UiEventType.MouseDown:
                if (!Live)
                    return true;
                _dragging = true;
                _grab = Along(e);
                _owner.BeginMove();
                return true;
            case UiEventType.MouseMove:
                if (_dragging)
                    _owner.MoveTo(Offset + Along(e) - _grab);
                return true;
            case UiEventType.MouseUp:
            case UiEventType.CaptureChanged:
                if (_dragging)
                {
                    _dragging = false;
                    _owner.EndMove();
                }
                return true;
            case UiEventType.Click:
            case UiEventType.DoubleClick:
                return true;
            case UiEventType.KeyDown when _focused && Live:
                int direction = Direction((Key)e.Data0);
                if (direction == 0)
                    return false;
                _owner.Nudge(direction);
                return true;
        }
        return false;
    }

    private Vector4 Fade(Vector4 color) =>
        Live ? color : color with { W = color.W * UiMarkupToggle.DisabledAlpha };

    protected override void OnDraw(UiRenderContext ctx)
    {
        if (Width <= 0f || Height <= 0f)
            return;
        if (ThemePalette is { } p)
        {
            // The plugin theme: no band, only a slim grip pill across the middle.
            Vector4 pill = Fade(Hot ? p.Accent : p.Border);
            const float GripLength = 24f, GripWidth = 3f;
            if (_owner.Horizontal)
            {
                float length = MathF.Min(GripLength, Height);
                ctx.FillRoundedRect(MathF.Floor((Width - GripWidth) * 0.5f), MathF.Floor((Height - length) * 0.5f),
                    GripWidth, length, GripWidth * 0.5f, pill);
            }
            else
            {
                float length = MathF.Min(GripLength, Width);
                ctx.FillRoundedRect(MathF.Floor((Width - length) * 0.5f), MathF.Floor((Height - GripWidth) * 0.5f),
                    length, GripWidth, GripWidth * 0.5f, pill);
            }
            if (_focused)
                PluginUiStyle.FocusRing(ctx, p, Width, Height, PluginUiStyle.SmallRadius);
            return;
        }
        Vector4 fill = Fade(ClassicFill);
        Vector4 grip = Fade(Hot ? ClassicHot : ClassicGrip);
        ctx.DrawFill(0f, 0f, Width, Height, fill);
        if (_owner.Horizontal)
            ctx.DrawFill(MathF.Floor((Width - 1f) * 0.5f), 0f, 1f, Height, grip);
        else
            ctx.DrawFill(0f, MathF.Floor((Height - 1f) * 0.5f), Width, 1f, grip);
        if (_focused)
            ctx.DrawRectOutline(0f, 0f, Width, Height, FocusColor, 1f);
    }
}
