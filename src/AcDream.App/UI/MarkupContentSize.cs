using AcDream.App.UI.Layout.Flex;

namespace AcDream.App.UI;

/// <summary>
/// How big each markup control would like to be inside a flex container,
/// and how small it can go: the content sizes of flex layout. Every
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

    /// <summary>Where a toggle's caption starts, in either look: lamp (or check) and gap.</summary>
    internal const float ClassicToggleCaptionX = 17f;

    /// <summary>Where a checkbox's or radio button's caption starts: box and gap.</summary>
    internal const float CheckCaptionX = 17f;

    /// <summary>Room after each round radio option's caption.</summary>
    internal const float RadioOptionGap = 8f;

    /// <summary>A colour swatch's width.</summary>
    internal const float ColorWidth = 40f;

    /// <summary>A separator's thickness across its line: the line and room either side.</summary>
    internal const float SeparatorSize = 9f;

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

    /// <summary>The height of a toggle, checkbox or round radio option set in <paramref name="font"/>.</summary>
    internal static float ToggleHeightFor(UiDatFont? font) =>
        MathF.Max(ToggleHeight, MathF.Ceiling(LineHeight(font) + 4f));

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
            {
                // A label may shrink to nothing (it draws "..." when cut short), so
                // a long bound caption never raises its window's minimum.
                float height = LineHeight(label.DatFont);
                return new FlexMeasurement(
                    new FlexSize(MathF.Ceiling(TextWidth(label.DatFont, text)), height), new FlexSize(0f, height));
            }
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
                float captionX = ClassicToggleCaptionX;
                float height = ToggleHeightFor(toggle.DatFont);
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
            case UiMarkupCheckbox checkbox:
                return Fixed(CheckCaptionX + MathF.Ceiling(TextWidth(checkbox.DatFont, text)), ToggleHeightFor(checkbox.DatFont));
            case UiMarkupRadio radio:
                return RadioSize(radio);
            case UiMarkupNumber number:
                return Range(FieldWidth, FieldMinWidth + UiMarkupNumber.SpinWidth, ControlHeightFor(number.Field.DatFont));
            case UiMarkupHotkey hotkey:
                return Range(FieldWidth, FieldMinWidth, ControlHeightFor(hotkey.DatFont));
            case UiMarkupColor:
                return Fixed(ColorWidth, ControlHeight);
            case UiMarkupSeparator:
                // Stretched along the container's cross axis by the default align,
                // it lies across a column and down a row (UiMarkupSeparator.Vertical).
                return Fixed(SeparatorSize, SeparatorSize);
            case UiMarkupSplit:
                return new FlexMeasurement(new FlexSize(ListWidth, ListHeight), new FlexSize(ListMinWidth, ListMinHeight));
            case UiMarkupExpander expander:
            {
                // The header's arrow and caption, at the height for its state.
                float min = UiMarkupExpanderHeader.CaptionX + MathF.Ceiling(TextWidth(expander.Header.DatFont, text));
                float height = expander.Expanded ? expander.ExpandedHeight : UiMarkupExpander.HeaderHeight;
                return new FlexMeasurement(new FlexSize(MathF.Max(ListWidth, min), height), new FlexSize(min, height));
            }
            default:
                return null;
        }
    }

    /// <summary>A radio group's cells: each as wide as its widest caption, one per option along its orientation.</summary>
    private static FlexMeasurement RadioSize(UiMarkupRadio radio)
    {
        IReadOnlyList<string> items = radio.Items;
        float widest = 0f;
        foreach (string item in items)
            widest = MathF.Max(widest, MathF.Ceiling(TextWidth(radio.DatFont, item ?? string.Empty)));
        float cellW = radio.Segmented ? widest + 2f * ButtonPaddingX : CheckCaptionX + widest + RadioOptionGap;
        float cellH = radio.Segmented ? ControlHeightFor(radio.DatFont) : ToggleHeightFor(radio.DatFont);
        int count = Math.Max(1, items.Count);
        return radio.Horizontal ? Fixed(cellW * count, cellH) : Fixed(cellW, cellH * count);
    }

    /// <summary>
    /// The caption a control shows now: what its size depends on besides its
    /// font. A radio group's options are compared item by item instead
    /// (<see cref="UiFlexItem"/>), so they are not joined into one string each frame.
    /// </summary>
    internal static string Text(UiElement element) => element switch
    {
        UiLabel label => label.TextSource?.Invoke() ?? label.Text,
        UiSimpleButton button => button.TextSource?.Invoke() ?? button.Text,
        UiMarkupToggle toggle => toggle.TextSource?.Invoke() ?? toggle.Text,
        UiMarkupCheckbox checkbox => checkbox.TextSource?.Invoke() ?? checkbox.Text,
        UiMarkupExpander expander => expander.Header.TextSource?.Invoke() ?? expander.Header.Text,
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
        UiMarkupCheckbox checkbox => checkbox.DatFont,
        UiMarkupRadio radio => radio.DatFont,
        UiMarkupNumber number => number.Field.DatFont,
        UiMarkupHotkey hotkey => hotkey.DatFont,
        UiMarkupExpander expander => expander.Header.DatFont,
        _ => null,
    };

    /// <summary>Whether a control is an expander showing only its header.</summary>
    internal static bool Collapsed(UiElement element) => element is UiMarkupExpander { Expanded: false };

    /// <summary>Whether the control draws themed; a theme switch re-measures it.</summary>
    internal static bool Themed(UiElement element) => element is UiMarkupToggle { ThemePalette: not null };

    private static FlexMeasurement Fixed(float width, float height) =>
        new(new FlexSize(width, height), new FlexSize(width, height));

    private static FlexMeasurement Range(float width, float minWidth, float height) =>
        new(new FlexSize(width, height), new FlexSize(minWidth, height));
}
