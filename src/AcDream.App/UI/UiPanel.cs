using System;
using System.Numerics;

namespace AcDream.App.UI;

public class UiPanel : UiElement
{
    public Vector4 BackgroundColor { get; set; } = new(0f, 0f, 0f, 0.55f);

    public Vector4 BorderColor     { get; set; } = new(0.15f, 0.15f, 0.2f, 0.8f);

    /// <summary>
    /// Re-read every frame in place of <see cref="BackgroundColor"/> when it
    /// is set, the way <see cref="UiLabel.TextSource"/> stands in for a
    /// caption: a colour a player can change has to be asked for again, not
    /// captured once.
    /// </summary>
    public Func<Vector4>? BackgroundColorSource { get; set; }

    /// <summary>Re-read every frame in place of <see cref="BorderColor"/>.</summary>
    public Func<Vector4>? BorderColorSource { get; set; }

    public float BorderThickness   { get; set; } = 1f;

    public uint BackgroundSprite { get; set; }

    public Func<uint, (uint tex, int w, int h)>? SpriteResolve { get; set; }

    protected override void OnDraw(UiRenderContext ctx)
    {
        Vector4 background = BackgroundColorSource?.Invoke() ?? BackgroundColor;
        Vector4 border = BorderColorSource?.Invoke() ?? BorderColor;

        if (BackgroundSprite != 0 && SpriteResolve is { } sr)
        {
            var (tex, tw, th) = sr(BackgroundSprite);
            if (tex != 0 && tw != 0 && th != 0)
                ctx.DrawSprite(tex, 0, 0, Width, Height, 0, 0, Width / tw, Height / th, Vector4.One);
        }
        else if (background.W > 0f)
        {
            ctx.DrawFill(0, 0, Width, Height, background);
        }

        if (border.W > 0f && BorderThickness > 0f)
            ctx.DrawRectOutline(0, 0, Width, Height, border, BorderThickness);
    }
}

public class UiLabel : UiElement
{
    public string Text       { get; set; } = string.Empty;
    public Vector4 TextColor { get; set; } = new(1f, 1f, 1f, 1f);

    public Func<string?>? TextSource { get; set; }

    /// <summary>Re-read every frame in place of <see cref="TextColor"/>.</summary>
    public Func<Vector4>? TextColorSource { get; set; }

    public UiDatFont? DatFont { get; set; }

    public bool Outline { get; set; } = true;

    /// <summary>
    /// Set for a label inside a flex container: its size comes from the
    /// layout (<see cref="MarkupContentSize"/>), so it no longer sizes itself
    /// to its text when drawn, its line is centred in the height it is given,
    /// and text wider than that box is cut short with "...".
    /// </summary>
    internal bool SizedByLayout { get; set; }

    private string? _shownFor;
    private float _shownWidth = float.NaN;
    private UiDatFont? _shownFont;
    private string _shown = string.Empty;
    private Func<string, float>? _fitMeasure;

    public UiLabel() { ClickThrough = true; }

    protected override bool ClipsChildren => false;

    protected override void OnDraw(UiRenderContext ctx)
    {
        string text = TextSource?.Invoke() ?? Text;
        Vector4 textColor = TextColorSource?.Invoke() ?? TextColor;
        float h = DatFont?.LineHeight
            ?? ctx.DefaultFont?.LineHeight ?? 14f;
        float y = 0f;
        if (SizedByLayout)
        {
            y = MathF.Floor((Height - h) / 2f + 0.5f);
            text = Fitted(text);
        }
        else
        {
            float w = DatFont is { } df
                ? df.MeasureWidth(text)
                : (ctx.DefaultFont?.MeasureWidth(text) ?? text.Length * 7f);
            if (w != Width) Width = w;
            if (h != Height) Height = h;
        }
        if (DatFont is { } dat)
            ctx.DrawStringDat(dat, text, 0, y, textColor, Outline);
        else
            ctx.DrawString(text, 0, y, textColor);
    }

    /// <summary><paramref name="text"/> shortened to the label's width. Cached per text, width and font.</summary>
    internal string Fitted(string text)
    {
        UiDatFont? font = DatFont;
        if (!string.Equals(_shownFor, text, StringComparison.Ordinal) || _shownWidth != Width || !ReferenceEquals(_shownFont, font))
        {
            // Measured as the layout measures, so a label given its text's width keeps it all.
            _fitMeasure ??= s => MathF.Ceiling(MarkupContentSize.TextWidth(DatFont, s));
            _shown = PluginWindowChrome.Ellipsize(text, _fitMeasure, Width);
            _shownFor = text;
            _shownWidth = Width;
            _shownFont = font;
        }
        return _shown;
    }
}

public class UiSimpleButton : UiPanel
{
    public string Text       { get; set; } = string.Empty;
    public Vector4 TextColor { get; set; } = new(1f, 1f, 1f, 1f);

    public Func<string?>? TextSource { get; set; }

    /// <summary>Re-read every frame in place of <see cref="TextColor"/>.</summary>
    public Func<Vector4>? TextColorSource { get; set; }

    public UiDatFont? DatFont { get; set; }

    public bool Outline { get; set; } = true;

    public Func<(uint tex, int w, int h)>? IconSource { get; set; }

    /// <summary>The color the icon is drawn in; white draws it as authored.</summary>
    private protected virtual Vector4 IconTint => Vector4.One;

    public event System.Action? Click;

    private readonly UiKeyboardActivation _keyboardActivation = new();

    public override bool HandlesClick => true;

    public UiSimpleButton()
    {
        BackgroundColor = new Vector4(0.1f, 0.1f, 0.15f, 0.8f);
        BorderColor     = new Vector4(0.45f, 0.45f, 0.55f, 1f);
    }

    public override bool OnEvent(in UiEvent e)
    {
        if (_keyboardActivation.HandleEvent(in e, TabStop, Enabled, () => Click?.Invoke()))
            return true;
        if (e.Type == UiEventType.Click && Enabled)
        {
            Click?.Invoke();
            return true;
        }
        return false;
    }

    protected override void OnDraw(UiRenderContext ctx)
    {
        base.OnDraw(ctx);
        if (_keyboardActivation.Focused)
            ctx.DrawRectOutline(1f, 1f, Width - 2f, Height - 2f,
                new Vector4(1f, 0.82f, 0.25f, 1f), 1f);

        float iconColumn = 0f;
        if (IconSource is { } iconSource)
        {
            float extent = MathF.Max(0f, MathF.Min(Width, Height) - 6f);
            iconColumn = extent + 6f;

            (uint tex, int w, int h) = iconSource();
            if (tex != 0u && w > 0 && h > 0)
            {
                float scale = MathF.Min(extent / w, extent / h);
                float drawWidth = w * scale;
                float drawHeight = h * scale;
                ctx.DrawSprite(
                    tex,
                    3f + (extent - drawWidth) * 0.5f,
                    (Height - drawHeight) * 0.5f,
                    drawWidth, drawHeight,
                    0f, 0f, 1f, 1f, IconTint);
            }
        }

        string caption = TextSource?.Invoke() ?? Text;
        if (caption.Length == 0) return;
        Vector4 textColor = TextColorSource?.Invoke() ?? TextColor;

        float captionAreaX = iconColumn;
        float captionAreaWidth = MathF.Max(0f, Width - iconColumn);
        if (DatFont is { } dat)
        {
            float datW = dat.MeasureWidth(caption);
            ctx.DrawStringDat(
                dat, caption,
                captionAreaX + (captionAreaWidth - datW) * 0.5f,
                (Height - dat.LineHeight) * 0.5f,
                textColor, Outline);
            return;
        }

        if (ctx.DefaultFont is null) return;
        float textW = ctx.DefaultFont.MeasureWidth(caption);
        float tx = captionAreaX + (captionAreaWidth - textW) * 0.5f;
        float ty = (Height - ctx.DefaultFont.LineHeight) * 0.5f;
        ctx.DrawString(caption, tx, ty, textColor);
    }
}

public class UiClickablePanel : UiPanel
{
    public Action? OnClick { get; set; }

    public string? TooltipText { get; set; }

    /// <inheritdoc />
    public override string? GetTooltipText() =>
        string.IsNullOrWhiteSpace(TooltipText) ? null : TooltipText;

    public UiClickablePanel()
    {
        ClickThrough = false;
    }

    public override bool HandlesClick => true;

    public override bool OnEvent(in UiEvent e)
    {
        if (e.Type == UiEventType.Click && Enabled)
        {
            OnClick?.Invoke();
            return true;
        }
        return false;
    }
}
