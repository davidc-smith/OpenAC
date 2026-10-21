using System.Numerics;

namespace AcDream.App.UI;

/// <summary>
/// A markup checkbox: a tick box and caption. Unlike <see cref="UiMarkupToggle"/>
/// it can be indeterminate, and it reports the value a click asks for rather
/// than leaving the plugin to flip its own.
/// </summary>
public sealed class UiMarkupCheckbox : UiElement
{
    public PluginUiPalette? ThemePalette { get; set; }

    public string Text { get; set; } = string.Empty;
    public Func<string?>? TextSource { get; set; }

    /// <summary>The bound state; null is indeterminate.</summary>
    public Func<bool?>? CheckedSource { get; set; }
    public UiDatFont? DatFont { get; set; }
    public Vector4 TextColor { get; set; } = new(0.86f, 0.84f, 0.74f, 1f);

    /// <summary>Re-read every frame in place of <see cref="TextColor"/>.</summary>
    public Func<Vector4>? TextColorSource { get; set; }

    /// <summary>Called with the new value a click or key press asks for.</summary>
    public Action<bool>? Changed { get; set; }

    public bool? State => CheckedSource is null ? false : CheckedSource();

    /// <summary>Unchecked and indeterminate become checked; checked becomes unchecked.</summary>
    public static bool NextValue(bool? state) => state != true;

    public override bool HandlesClick => true;

    private readonly UiKeyboardActivation _keyboardActivation = new();

    private void Activate()
    {
        if (Enabled)
            Changed?.Invoke(NextValue(State));
    }

    public override bool OnEvent(in UiEvent e)
    {
        if (_keyboardActivation.HandleEvent(in e, TabStop, Enabled, Activate))
            return true;
        if (e.Type != UiEventType.Click || !Enabled)
            return false;
        Activate();
        return true;
    }

    protected override void OnDraw(UiRenderContext ctx)
    {
        if (_keyboardActivation.Focused)
            ctx.DrawRectOutline(0f, 0f, Width, Height, new Vector4(1f, 0.82f, 0.25f, 1f), 1f);

        float boxY = MathF.Max(1f, (Height - UiCheckBoxGlyph.Size) * 0.5f);
        if (!Enabled) ctx.PushAlpha(UiMarkupToggle.DisabledAlpha);
        try
        {
            UiCheckBoxGlyph.Draw(ctx, 1f, boxY, State, ThemePalette);
        }
        finally
        {
            if (!Enabled) ctx.PopAlpha();
        }

        string caption = TextSource?.Invoke() ?? Text;
        Vector4 textColor = TextColorSource?.Invoke() ?? TextColor;
        Vector4 color = Enabled ? textColor : textColor with { W = UiMarkupToggle.DisabledAlpha };
        float y = DatFont is { } font ? (Height - font.LineHeight) * 0.5f : 1f;
        if (DatFont is { } dat)
            ctx.DrawStringDat(dat, caption, 17f, y, color, outline: ThemePalette is null);
        else
            ctx.DrawString(caption, 17f, y, color);
    }
}
