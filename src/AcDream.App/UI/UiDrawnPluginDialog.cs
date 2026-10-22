using System.Numerics;

namespace AcDream.App.UI;

/// <summary>
/// A plugin dialog drawn with primitives: a title bar with a close button,
/// the wrapped message and a centred row of buttons, the first marked as
/// the default. Under a plugin theme it takes the owner window's palette
/// (re-read every tick); with no palette it takes the Classic context-menu
/// colours, which is how it stands in when the retail art is not to hand.
/// </summary>
internal sealed class UiDrawnPluginDialog : UiPluginDialog
{
    public const float Pad = 10f;
    public const float TitleHeight = 20f;
    public const float CloseSize = 16f;
    public const float ButtonHeight = 22f;
    public const float ButtonMinWidth = 64f;
    public const float ButtonTextPad = 8f;
    public const float ButtonGap = 8f;
    public const float BoxMinWidth = 260f;

    public static readonly Vector4 ClassicBackground = UiMarkupContextMenu.ClassicBackground;
    public static readonly Vector4 ClassicBorder = UiMarkupContextMenu.ClassicBorder;
    public static readonly Vector4 ClassicText = UiMarkupContextMenu.ClassicText;
    public static readonly Vector4 ClassicTitleBar = UiMarkupContextMenu.ClassicHighlight;
    /// <summary>A markup button's own background.</summary>
    public static readonly Vector4 ClassicButton = new(0.1f, 0.1f, 0.15f, 0.8f);
    /// <summary>The default button's border in Classic: the gold of the focus outline.</summary>
    public static readonly Vector4 ClassicDefaultBorder = UiMarkupHotkey.FocusColor;

    private readonly string _title;
    private readonly UiDatFont? _font;
    private readonly Func<PluginUiPalette?> _palette;
    private readonly float _lineHeight;
    private readonly UiSimpleButton[] _buttons;

    public UiDrawnPluginDialog(
        string title, string message, IReadOnlyList<string> buttons,
        UiDatFont? font, Func<PluginUiPalette?> palette)
        : base(buttons.Count)
    {
        _title = title;
        _font = font;
        _palette = palette;
        Func<string, float> measure = font is { } dat ? dat.MeasureWidth : static text => text.Length * 7f;
        _lineHeight = font?.LineHeight ?? 14f;

        var widths = new float[buttons.Count];
        float row = ButtonGap * (buttons.Count - 1);
        for (int i = 0; i < buttons.Count; i++)
        {
            widths[i] = MathF.Max(ButtonMinWidth, MathF.Ceiling(measure(buttons[i])) + 2f * ButtonTextPad);
            row += widths[i];
        }
        Width = MathF.Max(BoxMinWidth, MathF.Max(row + 2f * Pad,
            MathF.Ceiling(measure(title)) + 2f * Pad + CloseSize + 4f));
        Lines = message.Length == 0 ? Array.Empty<string>() : UiText.WrapWords(message, measure, Width - 2f * Pad);
        float messageHeight = Lines.Count * _lineHeight;
        Height = TitleHeight + Pad + (messageHeight > 0f ? messageHeight + Pad : 0f) + ButtonHeight + Pad;

        _buttons = new UiSimpleButton[buttons.Count];
        float x = MathF.Round((Width - row) * 0.5f);
        for (int i = 0; i < buttons.Count; i++)
        {
            int index = i;
            var button = new UiSimpleButton
            {
                Left = x, Top = Height - Pad - ButtonHeight, Width = widths[i], Height = ButtonHeight,
                Text = buttons[i], DatFont = font,
            };
            button.Click += () => Choose(index);
            AddChild(button);
            _buttons[i] = button;
            x += widths[i] + ButtonGap;
        }
        CloseButton = new UiSimpleButton
        {
            Left = Width - CloseSize - 2f, Top = MathF.Round((TitleHeight - CloseSize) * 0.5f),
            Width = CloseSize, Height = CloseSize, Text = "x", DatFont = font,
        };
        CloseButton.Click += () => Choose(CancelIndex);
        AddChild(CloseButton);
        ApplyColors(palette());
    }

    public IReadOnlyList<UiSimpleButton> Buttons => _buttons;

    public UiSimpleButton CloseButton { get; }

    /// <summary>The message as wrapped to the box.</summary>
    public IReadOnlyList<string> Lines { get; }

    protected override void OnTick(double deltaSeconds) => ApplyColors(_palette());

    private void ApplyColors(PluginUiPalette? p)
    {
        for (int i = 0; i < _buttons.Length; i++)
            Style(_buttons[i], p, isDefault: i == 0);
        Style(CloseButton, p, isDefault: false);
    }

    private static void Style(UiSimpleButton button, PluginUiPalette? p, bool isDefault)
    {
        button.BackgroundColor = p?.Field ?? ClassicButton;
        button.BorderColor = isDefault ? p?.Accent ?? ClassicDefaultBorder : p?.Border ?? ClassicBorder;
        button.TextColor = p?.Text ?? ClassicText;
        button.Outline = p is null;
    }

    protected override void OnDraw(UiRenderContext ctx)
    {
        PluginUiPalette? p = _palette();
        Vector4 text = p?.Text ?? ClassicText;
        ctx.DrawFill(0f, 0f, Width, Height, p?.Background ?? ClassicBackground);
        ctx.DrawFill(0f, 0f, Width, TitleHeight, p?.Field ?? ClassicTitleBar);
        UiCaption.Draw(ctx, _font, _title, Pad, 0f, TitleHeight, text, outline: p is null);
        float y = TitleHeight + Pad;
        foreach (string line in Lines)
        {
            UiCaption.Draw(ctx, _font, line, Pad, y, _lineHeight, text, outline: p is null);
            y += _lineHeight;
        }
    }

    protected override void OnDrawAfterChildren(UiRenderContext ctx) =>
        ctx.DrawRectOutline(0f, 0f, Width, Height, _palette()?.Border ?? ClassicBorder, 1f);
}
