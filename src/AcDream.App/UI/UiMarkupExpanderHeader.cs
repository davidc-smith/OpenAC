using System.Numerics;

namespace AcDream.App.UI;

/// <summary>
/// An expander's header: a ▸/▾ arrow and caption. It toggles its expander on
/// a press and release over it, or on Enter/Space while it has focus.
/// </summary>
public sealed class UiMarkupExpanderHeader : UiElement
{
    public static readonly Vector4 ClassicArrow = new(0.80f, 0.74f, 0.55f, 1f);
    public static readonly Vector4 FocusColor = new(1f, 0.82f, 0.25f, 1f);

    private readonly UiMarkupExpander _owner;
    private readonly UiKeyboardActivation _keyboardActivation = new();
    private bool _pressed;

    public PluginUiPalette? ThemePalette { get; set; }
    public string Text { get; set; } = string.Empty;
    public Func<string?>? TextSource { get; set; }
    public UiDatFont? DatFont { get; set; }
    public Vector4 TextColor { get; set; } = new(0.86f, 0.84f, 0.74f, 1f);

    /// <summary>Re-read every frame in place of <see cref="TextColor"/>.</summary>
    public Func<Vector4>? TextColorSource { get; set; }

    public override bool HandlesClick => true;

    internal UiMarkupExpanderHeader(UiMarkupExpander owner) => _owner = owner;

    /// <summary>The header takes the hover, so it shows its expander's tooltip.</summary>
    public override string? GetTooltipText() => _owner.GetTooltipText();

    private bool Live => Enabled && _owner.Enabled;

    public override bool OnEvent(in UiEvent e)
    {
        if (_keyboardActivation.HandleEvent(in e, TabStop, Live, _owner.Toggle))
            return true;
        switch (e.Type)
        {
            case UiEventType.MouseDown:
                _pressed = Live;
                return true;
            case UiEventType.MouseUp:
                if (_pressed && e.Data1 >= 0 && e.Data2 >= 0 && e.Data1 < Width && e.Data2 < Height)
                    _owner.Toggle();
                _pressed = false;
                return true;
            case UiEventType.CaptureChanged:
                _pressed = false;
                return true;
            case UiEventType.Click:
            case UiEventType.DoubleClick:
                return true;
        }
        return false;
    }

    private Vector4 Fade(Vector4 color) =>
        Live ? color : color with { W = color.W * UiMarkupToggle.DisabledAlpha };

    protected override void OnDraw(UiRenderContext ctx)
    {
        Vector4 arrow = Fade(ThemePalette?.Text ?? ClassicArrow);
        float top = MathF.Floor((Height - 5f) * 0.5f);
        if (_owner.LaidOutExpanded)
        {
            for (int row = 0; row < 3; row++)
                ctx.DrawFill(3f + row, top + 1f + row, 5f - row * 2, 1f, arrow);
        }
        else
        {
            for (int column = 0; column < 3; column++)
                ctx.DrawFill(4f + column, top + column, 1f, 5f - column * 2, arrow);
        }

        string caption = TextSource?.Invoke() ?? Text;
        Vector4 text = Fade(TextColorSource?.Invoke() ?? TextColor);
        UiCaption.Draw(ctx, DatFont, caption, 14f, 0f, Height, text, outline: ThemePalette is null);

        if (_keyboardActivation.Focused)
            ctx.DrawRectOutline(0f, 0f, Width, Height, FocusColor, 1f);
    }
}
