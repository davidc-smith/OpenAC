using System.Numerics;
using Silk.NET.Input;

namespace AcDream.App.UI;

/// <summary>
/// A markup context menu. It is a child of the element it belongs to (its
/// owner) and takes no room there: it sits at the owner's origin with no size
/// and draws its popup in the overlay pass where it was opened. Open, it holds
/// the root's popup slot and the keyboard focus, and gives the focus back when
/// it closes. The items are read once, when it opens; "-" is a separator.
/// </summary>
public sealed class UiMarkupContextMenu : UiElement, IUiContextMenu
{
    public const string Separator = "-";
    public const float ItemHeight = 18f;
    public const float SeparatorHeight = 7f;
    public const float Inset = 3f;
    public const float TextIndent = 8f;
    public const float MinPopupWidth = 80f;

    // UiMenu's plain colours, so a context menu looks like the markup drop-down.
    public static readonly Vector4 ClassicBackground = new(0f, 0f, 0f, 0.92f);
    public static readonly Vector4 ClassicBorder = new(0.46f, 0.37f, 0.16f, 1f);
    public static readonly Vector4 ClassicText = new(0.91f, 0.87f, 0.76f, 1f);
    public static readonly Vector4 ClassicDisabledText = new(0.5f, 0.5f, 0.5f, 1f);
    public static readonly Vector4 ClassicHighlight = new(0.40f, 0.33f, 0.14f, 0.95f);
    public static readonly Vector4 ClassicSeparator = UiCheckLamp.UncheckedInner;

    public PluginUiPalette? ThemePalette { get; set; }
    public UiDatFont? DatFont { get; set; }

    /// <summary>
    /// What the popup's rows, insets, indent and minimum width are multiplied by
    /// (the constants above are the sizes at 1). A Modern HUD deck sets its deck
    /// scale here, with <see cref="DatFont"/> at the same scale. Read as the menu opens.
    /// </summary>
    public float Scale { get; set; } = 1f;
    public Func<IReadOnlyList<string>> ItemsSource { get; set; } = static () => Array.Empty<string>();

    /// <summary>Per-item enabled flags; null, or an item past the end, is enabled.</summary>
    public Func<IReadOnlyList<bool>>? ItemsEnabledSource { get; set; }

    /// <summary>Called with the index of the item picked (separators count), after the menu has closed.</summary>
    public Action<int>? Selected { get; set; }

    /// <summary>Called as a right click opens the menu, before the items are read, with the point in the owner's coordinates.</summary>
    public Action<float, float>? Opening { get; set; }

    public bool IsOpen => _open;

    internal int HighlightForTest => _highlight;

    /// <summary>The open popup in canvas coordinates.</summary>
    internal (float X, float Y, float W, float H) PopupScreenRect
    {
        get
        {
            Vector2 origin = ScreenPosition;
            return (origin.X + _x, origin.Y + _y, _w, _h);
        }
    }

    private bool _open;
    private string[] _items = Array.Empty<string>();
    private bool[] _enabled = Array.Empty<bool>();
    private float[] _rowTops = Array.Empty<float>();   // one more than the items: the last is the bottom
    private float _x, _y, _w, _h;                       // the popup, relative to this menu's origin
    private float _scale = 1f;                          // Scale as of the open
    private int _highlight = -1;
    private UiElement? _returnFocus;
    private UiRoot? _root;                              // the root it opened in, so Close works detached

    public UiMarkupContextMenu()
    {
        AcceptsFocus = true;
        FocusOnMouseClick = true;          // a press inside keeps the focus here
        CapturesPointerDrag = true;        // a press inside never drags the window
        Anchors = AnchorEdges.None;        // stays at the owner's origin with no size
    }

    public override bool HandlesClick => true;
    public override bool ReceivesHoverMouseMove => _open;
    protected override bool ClipsChildren => false;
    protected override bool ExpandsClipForPopup => true;

    public bool TryOpen(int screenX, int screenY)
    {
        if (_open)
            Close();   // so the focus to give back is never this menu
        if (Parent is not { } owner || FindRoot() is not { } root || !Available(owner))
            return false;
        Vector2 ownerOrigin = owner.ScreenPosition;
        Opening?.Invoke(screenX - ownerOrigin.X, screenY - ownerOrigin.Y);
        // Opening runs plugin code that may hide, disable or remove the owner.
        if (!ReferenceEquals(Parent, owner) || !ReferenceEquals(FindRoot(), root) || !Available(owner))
            return false;
        string[] items = ItemsSource().ToArray();
        if (items.Length == 0)
            return false;

        IReadOnlyList<bool>? flags = ItemsEnabledSource?.Invoke();
        _items = items;
        _enabled = new bool[items.Length];
        _rowTops = new float[items.Length + 1];
        _scale = Scale > 0f ? Scale : 1f;
        float y = Inset * _scale, textWidth = 0f;
        for (int i = 0; i < items.Length; i++)
        {
            _rowTops[i] = y;
            bool separator = items[i] == Separator;
            y += (separator ? SeparatorHeight : ItemHeight) * _scale;
            _enabled[i] = !separator && (flags is null || i >= flags.Count || flags[i]);
            if (!separator)
                textWidth = MathF.Max(textWidth, MeasureText(items[i]));
        }
        _rowTops[items.Length] = y;
        _w = MathF.Ceiling(MathF.Max(MinPopupWidth * _scale, textWidth + 2f * TextIndent * _scale));
        _h = y + Inset * _scale;
        Vector2 origin = ScreenPosition;
        _x = Place(screenX, _w, root.Width) - origin.X;
        _y = Place(screenY, _h, root.Height) - origin.Y;
        _highlight = -1;
        _open = true;
        _root = root;
        root.SetActivePopup(this, () => Close());
        _returnFocus = root.KeyboardFocus;
        root.SetKeyboardFocus(this);
        return true;
    }

    /// <summary>
    /// Where a popup starts on one axis: at the point, or on the other side of
    /// it when it would cross the screen edge, then shifted onto the screen.
    /// </summary>
    internal static float Place(float point, float size, float screen)
    {
        float start = point + size > screen ? point - size : point;
        return MathF.Max(0f, MathF.Min(start, screen - size));
    }

    /// <summary>Closes the popup; with <paramref name="restoreFocus"/>, hands the keyboard focus back to whatever had it when it opened.</summary>
    public void Close(bool restoreFocus = true)
    {
        if (!_open)
            return;
        _open = false;
        _highlight = -1;
        UiElement? previous = _returnFocus;
        _returnFocus = null;
        UiRoot? root = _root;
        _root = null;
        if (root is null)
            return;
        root.ClearActivePopup(this);
        if (restoreFocus && ReferenceEquals(root.KeyboardFocus, this))
            root.SetKeyboardFocus(previous);   // the root drops it if no longer available
    }

    protected override void OnTick(double deltaSeconds)
    {
        if (_open && Parent is { } owner && !Available(owner))
            Close();
    }

    /// <summary>Whether an element and all its ancestors are shown and enabled.</summary>
    private static bool Available(UiElement element)
    {
        for (UiElement? e = element; e is not null; e = e.Parent)
        {
            if (!e.Visible || !e.Enabled)
                return false;
        }
        return true;
    }

    protected override bool OnHitTest(float x, float y) =>
        _open && x >= _x && x < _x + _w && y >= _y && y < _y + _h;

    /// <summary>The item at a point in the owner's coordinates, or −1.</summary>
    internal int ItemAt(float x, float y)
    {
        if (!_open || x < _x || x >= _x + _w)
            return -1;
        float local = y - _y;
        for (int i = 0; i < _items.Length; i++)
        {
            if (local >= _rowTops[i] && local < _rowTops[i + 1])
                return i;
        }
        return -1;
    }

    public override bool OnEvent(in UiEvent e)
    {
        if (e.Type == UiEventType.FocusLost)
        {
            // Something else took the focus; Close has already run if it was us.
            Close(restoreFocus: false);
            return true;
        }
        if (!_open)
            return ReferenceEquals(e.Target, this);   // the tail of the press that closed it
        switch (e.Type)
        {
            case UiEventType.KeyDown:
                HandleKey((Key)e.Data0);
                return true;
            case UiEventType.HoverEnter:
            case UiEventType.MouseMove:
            {
                int hovered = ItemAt(e.Data1, e.Data2);
                _highlight = hovered >= 0 && _enabled[hovered] ? hovered : -1;
                return true;
            }
            case UiEventType.HoverLeave:
                _highlight = -1;
                return true;
            case UiEventType.MouseDown:
            {
                int item = ItemAt(e.Data1, e.Data2);
                if (item >= 0 && _enabled[item])
                    Pick(item);
                return true;
            }
            default:
                // Key ups, chars, releases, the right button and the wheel all stay here.
                return true;
        }
    }

    private void HandleKey(Key key)
    {
        switch (key)
        {
            case Key.Escape:
                Close();
                break;
            case Key.Up:
                Move(-1);
                break;
            case Key.Down:
                Move(1);
                break;
            case Key.Enter:
            case Key.KeypadEnter:
                if (_highlight >= 0 && _enabled[_highlight])
                    Pick(_highlight);
                break;
        }
    }

    /// <summary>The next enabled item up or down, wrapping; from none, the first or last.</summary>
    private void Move(int delta)
    {
        int count = _items.Length;
        int index = _highlight;
        for (int step = 0; step < count; step++)
        {
            index = index < 0 ? (delta > 0 ? 0 : count - 1) : (index + delta + count) % count;
            if (_enabled[index])
            {
                _highlight = index;
                return;
            }
        }
    }

    private void Pick(int index)
    {
        Close();
        Selected?.Invoke(index);
    }

    private float MeasureText(string text) => DatFont?.MeasureWidth(text) ?? text.Length * 7f * _scale;

    protected override void OnDrawOverlay(UiRenderContext ctx)
    {
        if (!_open)
            return;
        PluginUiPalette? p = ThemePalette;
        Vector4 background = p?.Field ?? ClassicBackground;
        Vector4 border = p?.Border ?? ClassicBorder;
        Vector4 text = p?.Text ?? ClassicText;
        Vector4 disabled = p?.Muted ?? ClassicDisabledText;
        Vector4 highlight = p is null ? ClassicHighlight : PluginUiStyle.Hover(p, p.Field);
        Vector4 separator = p?.Border ?? ClassicSeparator;
        ctx.PushAlphaAbsolute(1f);
        try
        {
            float inset = Inset * _scale, itemHeight = ItemHeight * _scale, separatorHeight = SeparatorHeight * _scale;
            if (p is not null)
            {
                PluginUiStyle.PopupShadow(ctx, _x, _y, _w, _h);
                PluginUiStyle.Surface(ctx, _x, _y, _w, _h, PluginUiStyle.ContainerRadius * _scale, background, border);
            }
            else
            {
                ctx.DrawFill(_x, _y, _w, _h, background);
                ctx.DrawRectOutline(_x, _y, _w, _h, border, 1f);
            }
            for (int i = 0; i < _items.Length; i++)
            {
                float top = _y + _rowTops[i];
                if (_items[i] == Separator)
                {
                    ctx.DrawFill(_x + inset, top + MathF.Floor((separatorHeight - 1f) * 0.5f),
                        _w - 2f * inset, 1f, separator);
                    continue;
                }
                if (i == _highlight)
                {
                    if (p is not null)
                        PluginUiStyle.Row(ctx, _x, top, _w, itemHeight, highlight);
                    else
                        ctx.DrawFill(_x + 1f, top, _w - 2f, itemHeight, highlight);
                }
                UiCaption.Draw(ctx, DatFont, _items[i], _x + TextIndent * _scale, top, itemHeight,
                    _enabled[i] ? text : disabled, outline: p is null);
            }
        }
        finally { ctx.PopAlpha(); }
    }
}
