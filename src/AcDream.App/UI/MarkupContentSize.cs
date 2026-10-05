using AcDream.App.UI.Layout.Flex;

namespace AcDream.App.UI;

/// <summary>
/// How big each markup control would like to be inside a flex container,
/// and how small it can go: the content sizes of spec section 3.2. Every
/// default lives here so the look can be tuned in one place. Sizes are in
/// points and come from the control's current font, so a theme switch that
/// changes the font changes them.
/// </summary>
internal static class MarkupContentSize
{
    /// <summary>Text width per character when a control has no dat font (headless only).</summary>
    internal const float FallbackCharWidth = 7f;

    /// <summary>Line height when a control has no dat font (headless only).</summary>
    internal const float FallbackLineHeight = 14f;

    /// <summary>The smallest height of a button, tab, field or menu.</summary>
    internal const float ControlHeight = 24f;

    /// <summary>Room above and below a control's text, together.</summary>
    internal const float ControlTextPadding = 8f;

    /// <summary>Room left and right of a button's caption, each side.</summary>
    internal const float ButtonPaddingX = 12f;

    /// <summary>The smallest height of a toggle.</summary>
    internal const float ToggleHeight = 20f;

    /// <summary>Where a Classic toggle's caption starts: lamp and gap.</summary>
    internal const float ClassicToggleCaptionX = 17f;

    /// <summary>Where a themed toggle's caption starts: switch and gap.</summary>
    internal const float ThemedToggleCaptionX = 1f + PluginUiStyle.SwitchWidth + PluginUiStyle.SwitchCaptionGap;

    internal const float IconSize = 32f;

    internal const float FieldWidth = 120f;
    internal const float FieldMinWidth = 40f;

    internal const float SliderHeight = 16f;
    internal const float MeterHeight = 12f;

    internal const float ListWidth = 160f;
    internal const float ListHeight = 80f;
    internal const float ListMinWidth = 60f;
    internal const float ListMinHeight = 40f;

    /// <summary>The width of <paramref name="text"/> in <paramref name="font"/>.</summary>
    internal static float TextWidth(UiDatFont? font, string text) =>
        font?.MeasureWidth(text) ?? text.Length * FallbackCharWidth;

    internal static float LineHeight(UiDatFont? font) => font?.LineHeight ?? FallbackLineHeight;

    /// <summary>The height of a button, tab, field or menu set in <paramref name="font"/>.</summary>
    internal static float ControlHeightFor(UiDatFont? font) =>
        MathF.Max(ControlHeight, MathF.Ceiling(LineHeight(font) + ControlTextPadding));

    /// <summary>
    /// The content size of a markup control, or null for an element this
    /// class does not size (groups are sized by their own layout or their
    /// authored size). <paramref name="text"/> is the caption the control
    /// shows now, already resolved.
    /// </summary>
    internal static FlexMeasurement? Measure(UiElement element, string text)
    {
        switch (element)
        {
            case UiLabel label:
                return Fixed(MathF.Ceiling(TextWidth(label.DatFont, text)), LineHeight(label.DatFont));
            case UiSimpleButton button:
            {
                float height = ControlHeightFor(button.DatFont);
                // A button with an icon keeps a square column for it on the left
                // (UiSimpleButton.OnDraw), and centres its caption in the rest.
                float icon = button.IconSource is not null ? height : 0f;
                float caption = text.Length == 0 ? 0f : MathF.Ceiling(TextWidth(button.DatFont, text)) + 2f * ButtonPaddingX;
                return Fixed(MathF.Max(icon + caption, height), height);
            }
            case UiMarkupToggle toggle:
            {
                float captionX = toggle.ThemePalette is null ? ClassicToggleCaptionX : ThemedToggleCaptionX;
                float height = MathF.Max(ToggleHeight, MathF.Ceiling(LineHeight(toggle.DatFont) + 4f));
                return Fixed(captionX + MathF.Ceiling(TextWidth(toggle.DatFont, text)), height);
            }
            case UiMarkupIcon:
                return Fixed(IconSize, IconSize);
            case UiField field:
                return Range(FieldWidth, FieldMinWidth, ControlHeightFor(field.DatFont));
            case UiMenu menu:
                return Range(FieldWidth, FieldMinWidth, ControlHeightFor(menu.ButtonDatFont ?? menu.DatFont));
            case UiScrollbar:
                return Range(FieldWidth, FieldMinWidth, SliderHeight);
            case UiMeter:
                return Range(FieldWidth, FieldMinWidth, MeterHeight);
            case UiMarkupList or UiMarkupLog:
                return new FlexMeasurement(new FlexSize(ListWidth, ListHeight), new FlexSize(ListMinWidth, ListMinHeight));
            default:
                return null;
        }
    }

    /// <summary>The caption a control shows now: what its size depends on besides its font.</summary>
    internal static string Text(UiElement element) => element switch
    {
        UiLabel label => label.TextSource?.Invoke() ?? label.Text,
        UiSimpleButton button => button.TextSource?.Invoke() ?? button.Text,
        UiMarkupToggle toggle => toggle.TextSource?.Invoke() ?? toggle.Text,
        _ => string.Empty,
    };

    /// <summary>The font a control's size is measured in, or null when its size does not depend on one.</summary>
    internal static UiDatFont? Font(UiElement element) => element switch
    {
        UiLabel label => label.DatFont,
        UiSimpleButton button => button.DatFont,
        UiMarkupToggle toggle => toggle.DatFont,
        UiField field => field.DatFont,
        UiMenu menu => menu.ButtonDatFont ?? menu.DatFont,
        _ => null,
    };

    /// <summary>Whether a control's size depends on the theme beyond its font.</summary>
    internal static bool Themed(UiElement element) => element is UiMarkupToggle { ThemePalette: not null };

    private static FlexMeasurement Fixed(float width, float height) =>
        new(new FlexSize(width, height), new FlexSize(width, height));

    private static FlexMeasurement Range(float width, float minWidth, float height) =>
        new(new FlexSize(width, height), new FlexSize(minWidth, height));
}
