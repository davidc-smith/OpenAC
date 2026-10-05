using System.Numerics;

namespace AcDream.App.UI;

/// <summary>
/// The chrome layer of a plugin window: the window's name and a close button
/// across the top of the frame. It never scrolls or takes part in layout,
/// and comes before the content in Tab order. Presses on it outside the
/// close button fall through to the frame, which drags.
/// </summary>
internal sealed class PluginTitleBar : UiElement
{
    internal const float ClassicTextLeft = 8f;
    internal const float ThemedTextLeft = 12f;
    private const float ThemedCloseTop = 3f;
    private const float TextGap = 4f;

    private PluginUiPalette? _palette;
    private string? _shownFor;
    private float _shownWidth = float.NaN;
    private UiDatFont? _shownFont;
    private string _shown = string.Empty;

    public PluginTitleBar(Func<uint, (uint, int, int)> resolve, float frameWidth)
    {
        ClickThrough = true;
        Width = frameWidth;
        Height = PluginWindowChrome.TitleBarHeight;
        Anchors = AnchorEdges.Left | AnchorEdges.Top | AnchorEdges.Right;
        Close = new PluginCloseButton(resolve)
        {
            Left = frameWidth - PluginWindowChrome.TitleBarHeight,
            Top = PluginWindowChrome.Border,
            Width = PluginWindowChrome.CloseButtonSize,
            Height = PluginWindowChrome.CloseButtonSize,
        };
        Close.Click += () => CloseRequested?.Invoke();
        AddChild(Close);
    }

    internal PluginCloseButton Close { get; }

    /// <summary>Raised when the player clicks or activates the close button.</summary>
    internal event Action? CloseRequested;

    internal string Title { get; set; } = string.Empty;
    internal UiDatFont? DatFont { get; set; }
    internal Vector4 TextColor { get; set; } = Vector4.One;
    internal bool Outline { get; set; } = true;

    /// <summary>The shared theme the bar draws in; null draws Classic.</summary>
    internal PluginUiPalette? ThemePalette
    {
        get => _palette;
        set
        {
            _palette = value;
            Close.ThemePalette = value;
            Close.Top = value is null ? PluginWindowChrome.Border : ThemedCloseTop;
        }
    }

    /// <summary>The title as drawn: shortened to end before the close button. Cached per text, width and font.</summary>
    internal string DisplayedTitle(Func<string, float> measure)
    {
        float left = _palette is null ? ClassicTextLeft : ThemedTextLeft;
        float width = MathF.Max(0f, Close.Left - TextGap - left);
        if (!ReferenceEquals(_shownFor, Title) || _shownWidth != width || !ReferenceEquals(_shownFont, DatFont))
        {
            _shown = PluginWindowChrome.Ellipsize(Title, measure, width);
            _shownFor = Title;
            _shownWidth = width;
            _shownFont = DatFont;
        }
        return _shown;
    }

    protected override void OnDraw(UiRenderContext ctx)
    {
        if (Title.Length == 0) return;
        UiDatFont? dat = DatFont;
        var font = ctx.DefaultFont;
        Func<string, float> measure = dat is not null ? s => dat.MeasureWidth(s)
            : font is not null ? s => font.MeasureWidth(s)
            : static s => s.Length * 7f;
        string text = DisplayedTitle(measure);
        if (text.Length == 0) return;
        float lineHeight = DatFont?.LineHeight ?? ctx.DefaultFont?.LineHeight ?? 14f;
        float x, y;
        if (_palette is null)
        {
            x = ClassicTextLeft;
            y = PluginWindowChrome.Border
                + MathF.Floor((PluginWindowChrome.CloseButtonSize - lineHeight) / 2f + 0.5f);
        }
        else
        {
            x = ThemedTextLeft;
            y = MathF.Floor((PluginWindowChrome.TitleBarHeight - lineHeight) / 2f + 0.5f);
        }
        if (DatFont is { } datFont)
            ctx.DrawStringDat(datFont, text, x, y, TextColor, Outline);
        else if (ctx.DefaultFont is not null)
            ctx.DrawString(text, x, y, TextColor);
    }
}

/// <summary>
/// A plugin window's close button: the retail close art in Classic, a ghost
/// X in the modern themes. Keyboard-focusable like other markup buttons;
/// a click does not take focus from the game.
/// </summary>
internal sealed class PluginCloseButton : UiSimpleButton
{
    private readonly Func<uint, (uint, int, int)> _resolve;

    public PluginCloseButton(Func<uint, (uint, int, int)> resolve)
    {
        _resolve = resolve;
        AcceptsFocus = true;
        TabStop = true;
        FocusOnMouseClick = false;
        Outline = false;
        BackgroundColor = Vector4.Zero;
        BorderColor = Vector4.Zero;
        Anchors = AnchorEdges.Top | AnchorEdges.Right;
    }

    internal string Tooltip { get; set; } = PluginWindowChrome.CloseTooltip;

    public override string? GetTooltipText() => Tooltip;

    protected override void OnDraw(UiRenderContext ctx)
    {
        if (ThemePalette is { } palette)
        {
            PluginUiStyle.CloseButton(ctx, palette, Width, Height, ThemeState, KeyboardFocused);
            return;
        }
        var (tex, tw, th) = _resolve(PluginWindowChrome.ClassicCloseSprite);
        if (tex != 0u && tw > 0 && th > 0)
        {
            float scale = MathF.Min(Width / tw, Height / th);
            float w = tw * scale, h = th * scale;
            ctx.DrawSprite(tex, (Width - w) / 2f, (Height - h) / 2f, w, h, 0f, 0f, 1f, 1f, Vector4.One);
        }
        if (KeyboardFocused)
            ctx.DrawRectOutline(1f, 1f, Width - 2f, Height - 2f, new Vector4(1f, 0.82f, 0.25f, 1f), 1f);
    }
}
