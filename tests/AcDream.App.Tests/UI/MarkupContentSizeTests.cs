using AcDream.App.UI;
using AcDream.App.UI.Layout.Flex;
using DatReaderWriter.Types;

namespace AcDream.App.Tests.UI;

/// <summary>
/// Content sizes of spec section 3.2, with the headless fallback metrics
/// (7 points a character, 14-point lines) and with a dat font.
/// </summary>
public sealed class MarkupContentSizeTests
{
    private static UiDatFont Font(float lineHeight)
    {
        var glyphs = new Dictionary<char, FontCharDesc>();
        foreach (char c in "ABCDEFGH")
            glyphs[c] = new FontCharDesc { Unicode = c, Width = 6, Height = 8 };
        return new UiDatFont(
            fgTex: 1, fgW: 32, fgH: 32, bgTex: 0, bgW: 0, bgH: 0,
            lineHeight: lineHeight, baselineOffset: 12f, glyphs);
    }

    private static FlexMeasurement Measure(UiElement element) =>
        MarkupContentSize.Measure(element, MarkupContentSize.Text(element))!.Value;

    private static FlexMeasurement Size(float w, float h, float minW, float minH) =>
        new(new FlexSize(w, h), new FlexSize(minW, minH));

    [Fact]
    public void A_label_is_its_text_by_its_line()
    {
        Assert.Equal(Size(35f, 14f, 35f, 14f), Measure(new UiLabel { Text = "Hello" }));
        Assert.Equal(Size(18f, 16f, 18f, 16f), Measure(new UiLabel { Text = "ABC", DatFont = Font(16f) }));
    }

    [Fact]
    public void A_bound_label_measures_what_it_shows_now()
    {
        string shown = "AB";
        var label = new UiLabel { Text = "ignored", TextSource = () => shown };

        Assert.Equal(14f, Measure(label).Preferred.Width);
        shown = "ABCD";
        Assert.Equal(28f, Measure(label).Preferred.Width);
    }

    [Fact]
    public void A_button_is_its_caption_padded_at_control_height()
    {
        Assert.Equal(Size(38f, 24f, 38f, 24f), Measure(new UiSimpleButton { Text = "OK" }));
        Assert.Equal(Size(38f, 24f, 38f, 24f), Measure(new UiMarkupTabButton { Text = "OK" }));
    }

    [Fact]
    public void A_button_icon_adds_a_square_column()
    {
        var button = new UiSimpleButton { Text = "OK", IconSource = () => (0u, 0, 0) };
        Assert.Equal(Size(62f, 24f, 62f, 24f), Measure(button));

        var iconOnly = new UiSimpleButton { IconSource = () => (0u, 0, 0) };
        Assert.Equal(Size(24f, 24f, 24f, 24f), Measure(iconOnly));
    }

    [Fact]
    public void A_tall_font_makes_controls_taller()
    {
        Assert.Equal(24f, MarkupContentSize.ControlHeightFor(Font(16f)));
        Assert.Equal(28f, MarkupContentSize.ControlHeightFor(Font(20f)));
        Assert.Equal(28f, Measure(new UiField { DatFont = Font(20f) }).Preferred.Height);
    }

    [Fact]
    public void A_toggle_starts_its_caption_after_its_lamp_or_switch()
    {
        Assert.Equal(Size(45f, 20f, 45f, 20f), Measure(new UiMarkupToggle { Text = "Auto" }));
        var themed = new UiMarkupToggle { Text = "Auto", ThemePalette = PluginUiPalette.Moss };
        Assert.Equal(Size(63f, 20f, 63f, 20f), Measure(themed));
    }

    [Fact]
    public void Fixed_and_stretchy_controls_use_the_defaults()
    {
        Assert.Equal(Size(32f, 32f, 32f, 32f), Measure(new UiMarkupIcon()));
        Assert.Equal(Size(120f, 24f, 40f, 24f), Measure(new UiField()));
        Assert.Equal(Size(120f, 24f, 40f, 24f), Measure(new UiMenu()));
        Assert.Equal(Size(120f, 16f, 40f, 16f), Measure(new UiScrollbar()));
        Assert.Equal(Size(120f, 12f, 40f, 12f), Measure(new UiMeter()));
        Assert.Equal(Size(160f, 80f, 60f, 40f), Measure(new UiMarkupList()));
        Assert.Equal(Size(160f, 80f, 60f, 40f), Measure(new UiMarkupLog(_ => (0u, 0, 0))));
    }

    [Fact]
    public void Groups_are_not_sized_here()
    {
        Assert.Null(MarkupContentSize.Measure(new UiPanel(), string.Empty));
    }
}
