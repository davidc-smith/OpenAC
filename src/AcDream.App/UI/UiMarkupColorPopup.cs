using System.Numerics;
using AcDream.App.UI.Markup;
using Silk.NET.Input;

namespace AcDream.App.UI;

/// <summary>
/// The colour picker's popup. It has a saturation/value square, a hue strip,
/// an alpha strip when the picker allows alpha, a hex field and two swatch
/// rows (palette, then recent colours). It is a child of its
/// <see cref="UiMarkupColor"/>, takes no room there and draws in the overlay
/// pass, like <see cref="UiMarkupContextMenu"/>. Open, it holds the root's
/// popup slot and the keyboard focus.
/// <para>Edits are reported through the owner's Changed on the next tick,
/// so at most once per frame. Closing keeps the colour, except Escape, which
/// puts back the colour the popup opened with.</para>
/// </summary>
public sealed class UiMarkupColorPopup : UiElement
{
    public const float Pad = 6f;
    public const float SquareSize = 128f;
    public const float StripWidth = 16f;
    public const float RowHeight = 18f;
    public const float SwatchSize = 14f;
    public const float SwatchStep = 16f;
    public const float Gap = 2f;
    public const float HueX = Pad + SquareSize + Pad;              // 140
    public const float AlphaX = HueX + StripWidth + Pad;           // 162
    public const float HexY = Pad + SquareSize + Pad;              // 140
    public const float PaletteY = HexY + RowHeight + Pad;          // 164
    public const float RecentY = PaletteY + SwatchStep;            // 180
    public const float PopupHeight = RecentY + SwatchSize + Pad;   // 200
    public const int MaxHexLength = 9;

    public static readonly uint[] ClassicSwatches =
    [
        0xFFFFFFFFu, 0xFF000000u, 0xFF808080u, 0xFFD03030u, 0xFFFFD140u, 0xFF40B040u, 0xFF4070D0u,
    ];

    private static readonly Vector4 MarkerDark = new(0f, 0f, 0f, 1f);
    private static readonly Vector4 MarkerLight = new(1f, 1f, 1f, 1f);

    private enum DragTarget { None, Square, Hue, Alpha }

    private readonly UiMarkupColor _owner;
    private bool _open;
    private float _x, _y, _w, _h;   // the popup, relative to this element's origin (the owner's)
    private float _hue, _sat, _val, _alpha;
    private uint _color;             // the working colour
    private uint _opened;            // the bound colour when it opened
    private uint? _pending;          // edited, not yet reported
    private DragTarget _drag;
    private bool _hexEditing;
    private string _hexText = string.Empty;
    private UiElement? _returnFocus;
    private UiRoot? _root;
    private IMarkupTextureService? _textureService;
    private uint _texWhite, _texBlack, _texHue, _texAlpha;

    internal UiMarkupColorPopup(UiMarkupColor owner)
    {
        _owner = owner;
        AcceptsFocus = true;
        FocusOnMouseClick = true;    // a press inside keeps the focus here
        CapturesPointerDrag = true;  // dragging in the square never moves the window
        Anchors = AnchorEdges.None;
    }

    public bool IsOpen => _open;

    /// <summary>The working colour.</summary>
    public uint Color => _color;

    /// <summary>The hex field's text: what is being typed, else the working colour.</summary>
    public string HexText => _hexEditing ? _hexText : MarkupColorValue.Format(_color, _owner.AllowAlpha);

    /// <summary>The open popup in canvas coordinates.</summary>
    internal (float X, float Y, float W, float H) PopupScreenRect
    {
        get
        {
            Vector2 origin = ScreenPosition;
            return (origin.X + _x, origin.Y + _y, _w, _h);
        }
    }

    public override bool HandlesClick => true;
    protected override bool ClipsChildren => false;
    protected override bool ExpandsClipForPopup => true;

    private byte AlphaByte(float alpha) => _owner.AllowAlpha ? MarkupColorValue.ToByte(alpha) : (byte)0xFF;

    internal bool TryOpen()
    {
        if (_open || FindRoot() is not { } root || !Available(_owner))
            return false;
        _opened = _owner.Value;
        _hue = 0f;
        _sat = 0f;
        SetWorking(_owner.AllowAlpha ? _opened : MarkupColorValue.WithAlpha(_opened, 0xFF));
        _pending = null;
        _drag = DragTarget.None;
        _hexEditing = false;
        Place(root);
        _open = true;
        _root = root;
        IsEditControl = true;   // UiRoot only sends Char events to edit controls
        root.SetActivePopup(this, Dismissed);
        _returnFocus = root.KeyboardFocus;
        root.SetKeyboardFocus(this);
        UploadTextures();
        return true;
    }

    private void Place(UiRoot root)
    {
        _w = (_owner.AllowAlpha ? AlphaX : HueX) + StripWidth + Pad;
        _h = PopupHeight;
        Vector2 origin = ScreenPosition;
        float x = Math.Clamp(origin.X, 0f, MathF.Max(0f, root.Width - _w));
        float y = origin.Y + _owner.Height + Gap;
        if (y + _h > root.Height)
            y = origin.Y - Gap - _h;
        y = Math.Clamp(y, 0f, MathF.Max(0f, root.Height - _h));
        _x = x - origin.X;
        _y = y - origin.Y;
    }

    /// <summary>
    /// The root's popup dismiss: a press outside, another popup opening, or the
    /// owner hiding or leaving the tree. UiRoot swallows an outside press but
    /// not its release, which has no capture target and would reach the world,
    /// so while a button is down with nothing captured (the outside press) the
    /// closed popup takes the capture and swallows the release too.
    /// </summary>
    private void Dismissed()
    {
        UiRoot? root = _root;
        bool pressTail = root is not null && root.Captured is null
            && (root.LeftButtonDown || root.RightButtonDown || root.MiddleButtonDown);
        Close(keep: true);
        if (pressTail && root!.Captured is null && ReferenceEquals(FindRoot(), root))
            root.SetCapture(this);
    }

    /// <summary>
    /// Closes the popup. Keeping applies the hex text and reports the colour;
    /// otherwise the colour it opened with is put back. Callbacks run last.
    /// </summary>
    public void Close(bool keep = true, bool restoreFocus = true)
    {
        if (!_open)
            return;
        if (keep)
            CommitHex();
        uint? report = keep ? _pending : _opened;
        bool remember = keep && _color != _opened;
        uint final = _color;

        _open = false;
        _pending = null;
        _drag = DragTarget.None;
        _hexEditing = false;
        IsEditControl = false;
        ReleaseTextures();
        UiElement? previous = _returnFocus;
        _returnFocus = null;
        UiRoot? root = _root;
        _root = null;
        if (root is not null)
        {
            root.ClearActivePopup(this);
            if (restoreFocus && ReferenceEquals(root.KeyboardFocus, this))
                root.SetKeyboardFocus(previous);   // the root drops it if no longer available
        }

        if (remember)
            _owner.Recent.Add(final);
        if (report is { } color && color != _owner.Value)
            _owner.Changed?.Invoke(color);
    }

    protected override void OnTick(double deltaSeconds)
    {
        if (!_open)
            return;
        if (!Available(_owner))
        {
            Close(keep: true);
            return;
        }
        Flush();
    }

    private void Flush()
    {
        if (_pending is not { } color)
            return;
        _pending = null;
        if (color != _owner.Value)
            _owner.Changed?.Invoke(color);
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

    public override bool OnEvent(in UiEvent e)
    {
        if (e.Type == UiEventType.FocusLost)
        {
            Close(keep: true, restoreFocus: false);
            return true;
        }
        if (!_open)
            return ReferenceEquals(e.Target, this);   // the tail of the press that closed it
        switch (e.Type)
        {
            case UiEventType.KeyDown:
                KeyDown((Key)e.Data0);
                return true;
            case UiEventType.Char:
                TypeHex((char)e.Data0);
                return true;
            case UiEventType.MouseDown:
                Press(e.Data1 - _x, e.Data2 - _y);
                return true;
            case UiEventType.MouseMove:
                if (_drag != DragTarget.None)
                    DragTo(e.Data1 - _x, e.Data2 - _y);
                return true;
            case UiEventType.MouseUp:
            case UiEventType.CaptureChanged:
                _drag = DragTarget.None;
                return true;
            default:
                return true;   // clicks, key ups, other buttons and the wheel all stay here
        }
    }

    private void KeyDown(Key key)
    {
        switch (key)
        {
            case Key.Escape:
                Close(keep: false);
                break;
            case Key.Enter:
            case Key.KeypadEnter:
                if (_hexEditing)
                    CommitHex();
                else
                    Close(keep: true);
                break;
            case Key.Backspace:
                if (_hexEditing && _hexText.Length > 0)
                    _hexText = _hexText[..^1];
                break;
        }
    }

    private void TypeHex(char c)
    {
        if (_hexEditing && (char.IsAsciiHexDigit(c) || c == '#') && _hexText.Length < MaxHexLength)
            _hexText += c;
    }

    // Points from here on are relative to the popup's top-left corner.
    private static bool In(float x, float y, float left, float top, float w, float h) =>
        x >= left && x < left + w && y >= top && y < top + h;

    private bool InHex(float x, float y) => In(x, y, Pad, HexY, _w - 2f * Pad, RowHeight);

    private void Press(float x, float y)
    {
        if (_hexEditing && !InHex(x, y))
            CommitHex();
        if (In(x, y, Pad, Pad, SquareSize, SquareSize))
            _drag = DragTarget.Square;
        else if (In(x, y, HueX, Pad, StripWidth, SquareSize))
            _drag = DragTarget.Hue;
        else if (_owner.AllowAlpha && In(x, y, AlphaX, Pad, StripWidth, SquareSize))
            _drag = DragTarget.Alpha;
        else if (InHex(x, y))
        {
            if (!_hexEditing)
            {
                _hexText = HexText;
                _hexEditing = true;
            }
            return;
        }
        else
        {
            if (SwatchAt(x, y) is { } swatch)
                Pick(swatch);
            return;
        }
        DragTo(x, y);
    }

    private void DragTo(float x, float y)
    {
        float across = Math.Clamp((x - Pad) / SquareSize, 0f, 1f);
        float down = Math.Clamp((y - Pad) / SquareSize, 0f, 1f);
        switch (_drag)
        {
            case DragTarget.Square:
                _sat = across;
                _val = 1f - down;
                break;
            case DragTarget.Hue:
                _hue = down * 360f;
                break;
            case DragTarget.Alpha:
                _alpha = 1f - down;
                break;
            default:
                return;
        }
        _color = MarkupColorValue.FromHsv(_hue, _sat, _val, AlphaByte(_alpha));
        _pending = _color;
    }

    /// <summary>A swatch colour (palette or recent) under a point, or null.</summary>
    private uint? SwatchAt(float x, float y)
    {
        if (x < Pad)
            return null;
        int index = (int)((x - Pad) / SwatchStep);
        if (x - Pad - index * SwatchStep >= SwatchSize)
            return null;
        IReadOnlyList<uint> row;
        if (In(x, y, Pad, PaletteY, _w, SwatchSize))
            row = PaletteSwatches();
        else if (In(x, y, Pad, RecentY, _w, SwatchSize))
            row = _owner.Recent.Colors;
        else
            return null;
        return index < row.Count ? row[index] : null;
    }

    internal IReadOnlyList<uint> PaletteSwatches()
    {
        if (_owner.ThemePalette is not { } p)
            return ClassicSwatches;
        return new[] { p.Background, p.Field, p.Border, p.Text, p.Muted, p.Accent, p.Selected }
            .Select(MarkupColorValue.FromVector4).ToArray();
    }

    private void Pick(uint color)
    {
        SetWorking(_owner.AllowAlpha ? color : MarkupColorValue.WithAlpha(color, 0xFF));
        _pending = _color;
    }

    private void CommitHex()
    {
        if (!_hexEditing)
            return;
        _hexEditing = false;
        if (MarkupColorValue.Parse(_hexText, MarkupColorValue.Alpha(_color)) is { } color && color != _color)
            Pick(color);
    }

    /// <summary>Makes a colour the working one; grey keeps the hue, black the hue and saturation.</summary>
    private void SetWorking(uint color)
    {
        _color = color;
        var (h, s, v) = MarkupColorValue.ToHsv(color);
        if (v > 0f)
        {
            if (s > 0f)
                _hue = h;
            _sat = s;
        }
        _val = v;
        _alpha = MarkupColorValue.Alpha(color) / 255f;
    }

    private void UploadTextures()
    {
        _textureService = _owner.Textures;
        if (_textureService is not { } t)
            return;
        int side = (int)SquareSize, strip = (int)StripWidth;
        _texWhite = t.Upload(MarkupColorRamps.WhiteToClear(side, side), side, side, "markup-color-white");
        _texBlack = t.Upload(MarkupColorRamps.ClearToBlack(side, side), side, side, "markup-color-black");
        _texHue = t.Upload(MarkupColorRamps.Hue(strip, side), strip, side, "markup-color-hue");
        if (_owner.AllowAlpha)
            _texAlpha = t.Upload(MarkupColorRamps.AlphaRamp(strip, side), strip, side, "markup-color-alpha");
    }

    private void ReleaseTextures()
    {
        if (_textureService is { } t)
        {
            foreach (uint texture in new[] { _texWhite, _texBlack, _texHue, _texAlpha })
            {
                if (texture != 0u)
                    t.Release(texture);
            }
        }
        _texWhite = _texBlack = _texHue = _texAlpha = 0u;
        _textureService = null;
    }

    protected override void OnDrawOverlay(UiRenderContext ctx)
    {
        if (!_open)
            return;
        PluginUiPalette? p = _owner.ThemePalette;
        Vector4 border = p?.Border ?? UiMarkupContextMenu.ClassicBorder;
        ctx.PushAlphaAbsolute(1f);
        try
        {
            ctx.DrawFill(_x, _y, _w, _h, p?.Field ?? UiMarkupContextMenu.ClassicBackground);
            ctx.DrawRectOutline(_x, _y, _w, _h, border, 1f);
            DrawSquare(ctx, _x + Pad, _y + Pad);
            DrawHue(ctx, _x + HueX, _y + Pad);
            if (_owner.AllowAlpha)
                DrawAlpha(ctx, _x + AlphaX, _y + Pad);
            DrawHex(ctx, p);
            DrawRow(ctx, PaletteSwatches(), _y + PaletteY, border, recent: false);
            DrawRow(ctx, _owner.Recent.Colors, _y + RecentY, border, recent: true);
        }
        finally { ctx.PopAlpha(); }
    }

    private void DrawSquare(UiRenderContext ctx, float x, float y)
    {
        if (_texWhite != 0u && _texBlack != 0u)
        {
            ctx.DrawFill(x, y, SquareSize, SquareSize, MarkupColorValue.ToVector4(MarkupColorValue.FromHsv(_hue, 1f, 1f, 0xFF)));
            ctx.DrawSprite(_texWhite, x, y, SquareSize, SquareSize, 0f, 0f, 1f, 1f, Vector4.One);
            ctx.DrawSprite(_texBlack, x, y, SquareSize, SquareSize, 0f, 0f, 1f, 1f, Vector4.One);
        }
        else
        {
            const int cells = 16;
            float cell = SquareSize / cells;
            for (int j = 0; j < cells; j++)
            {
                for (int i = 0; i < cells; i++)
                {
                    uint c = MarkupColorValue.FromHsv(_hue, (i + 0.5f) / cells, 1f - (j + 0.5f) / cells, 0xFF);
                    ctx.DrawFill(x + i * cell, y + j * cell, cell, cell, MarkupColorValue.ToVector4(c));
                }
            }
        }
        float mx = x + _sat * SquareSize, my = y + (1f - _val) * SquareSize;
        ctx.DrawRectOutline(mx - 3f, my - 3f, 7f, 7f, MarkerDark, 1f);
        ctx.DrawRectOutline(mx - 2f, my - 2f, 5f, 5f, MarkerLight, 1f);
    }

    private void DrawHue(UiRenderContext ctx, float x, float y)
    {
        if (_texHue != 0u)
            ctx.DrawSprite(_texHue, x, y, StripWidth, SquareSize, 0f, 0f, 1f, 1f, Vector4.One);
        else
        {
            const int cells = 32;
            float cell = SquareSize / cells;
            for (int j = 0; j < cells; j++)
            {
                uint c = MarkupColorValue.FromHsv(360f * (j + 0.5f) / cells, 1f, 1f, 0xFF);
                ctx.DrawFill(x, y + j * cell, StripWidth, cell, MarkupColorValue.ToVector4(c));
            }
        }
        DrawStripMarker(ctx, x, y + _hue / 360f * SquareSize);
    }

    private void DrawAlpha(UiRenderContext ctx, float x, float y)
    {
        UiMarkupColor.DrawChecker(ctx, x, y, StripWidth, SquareSize, static c => c);
        Vector4 rgb = MarkupColorValue.ToVector4(_color) with { W = 1f };
        if (_texAlpha != 0u)
            ctx.DrawSprite(_texAlpha, x, y, StripWidth, SquareSize, 0f, 0f, 1f, 1f, rgb);
        else
        {
            const int cells = 16;
            float cell = SquareSize / cells;
            for (int j = 0; j < cells; j++)
                ctx.DrawFill(x, y + j * cell, StripWidth, cell, rgb with { W = 1f - (j + 0.5f) / cells });
        }
        DrawStripMarker(ctx, x, y + (1f - _alpha) * SquareSize);
    }

    private static void DrawStripMarker(UiRenderContext ctx, float x, float y)
    {
        ctx.DrawFill(x - 1f, y - 1f, StripWidth + 2f, 3f, MarkerDark);
        ctx.DrawFill(x, y, StripWidth, 1f, MarkerLight);
    }

    private void DrawHex(UiRenderContext ctx, PluginUiPalette? p)
    {
        float x = _x + Pad, y = _y + HexY, w = _w - 2f * Pad;
        ctx.DrawFill(x, y, w, RowHeight, p?.Field ?? UiMarkupHotkey.ClassicBackground);
        Vector4 border = _hexEditing ? p?.Accent ?? UiMarkupHotkey.FocusColor : p?.Border ?? UiMarkupHotkey.ClassicBorder;
        ctx.DrawRectOutline(x, y, w, RowHeight, border, 1f);
        UiCaption.Draw(ctx, _owner.DatFont, HexText, x + UiMarkupHotkey.TextInset, y, RowHeight,
            p?.Text ?? UiMarkupContextMenu.ClassicText, outline: p is null);
    }

    private void DrawRow(UiRenderContext ctx, IReadOnlyList<uint> colors, float y, Vector4 border, bool recent)
    {
        // The recent row shows all its slots, empty ones as outlines.
        int slots = recent ? MarkupRecentColors.Capacity : colors.Count;
        for (int i = 0; i < slots; i++)
        {
            float x = _x + Pad + i * SwatchStep;
            if (i < colors.Count)
            {
                uint c = colors[i];
                if (MarkupColorValue.Alpha(c) < 0xFF)
                    UiMarkupColor.DrawChecker(ctx, x, y, SwatchSize, SwatchSize, static v => v);
                ctx.DrawFill(x, y, SwatchSize, SwatchSize, MarkupColorValue.ToVector4(c));
            }
            ctx.DrawRectOutline(x, y, SwatchSize, SwatchSize, border, 1f);
        }
    }
}
