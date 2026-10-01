using System.Numerics;
using AcDream.App.UI;
namespace AcDream.App.Tests.UI;

public sealed class BundledUiFontTests
{
    [Theory]
    [InlineData(12f)]
    [InlineData(16f)]
    [InlineData(24f)]
    [InlineData(32f)]
    public void EmbeddedFontBakesWithoutSystemFonts(float size)
    {
        var atlas = BundledUiFont.Bake(size);
        var font = atlas.CreateFont(1);
        Assert.Contains(atlas.Pixels.Where((_, i) => i % 4 == 3), a => a > 0 && a < 255);
        foreach (char c in "BuffProfile_Banes åäö Ω Ж —")
        {
            Assert.True(atlas.Glyphs.ContainsKey(c));
            Assert.True(font.TryGetGlyph(c, out var glyph));
            Assert.True(UiDatFont.GlyphAdvance(glyph) > 0);
            Assert.InRange((int)glyph.OffsetX + glyph.Width, 0, atlas.Width);
            Assert.InRange((int)glyph.OffsetY + glyph.Height, 0, atlas.Height);
        }
        Assert.True(font.MeasureWidth("WWW") > font.MeasureWidth("iii"));
        Assert.Equal(font.MeasureWidth("?"), font.MeasureWidth("漢"));
        Assert.False(font.TryGetGlyph('\n', out _));
    }

    [Fact]
    public void ThemeFontSwitchRestoresClassicFont()
    {
        var modern = BundledUiFont.Bake().CreateFont(1);
        var classic = BundledUiFont.Bake(12).CreateFont(2);
        var settings = new PluginUiThemeSettings(modernFont: modern);
        var panel = MarkupDocument.Build("""
            <panel theme="plugin" x="0" y="0" w="200" h="100" title="Title">
              <label x="1" y="20" text="BuffProfile_Banes" />
              <field x="1" y="40" w="150" h="24" />
            </panel>
            """, new object(), _ => (0u, 0, 0), datFont: classic, themes: settings);
        var root = new UiRoot(); root.AddChild(panel);
        var label = Assert.IsType<UiLabel>(panel.Children[1]);
        Assert.Same(classic, label.DatFont);
        settings.Theme = PluginUiTheme.Moss; root.Tick(.016, 1);
        Assert.Same(modern, label.DatFont);
        Assert.Same(modern, ((UiLabel)panel.Children[0]).DatFont);
        Assert.Same(modern, ((UiField)panel.Children[2]).DatFont);
        settings.Theme = PluginUiTheme.Classic; root.Tick(.016, 2);
        Assert.Same(classic, label.DatFont);
        Assert.Same(classic, ((UiField)panel.Children[2]).DatFont);
    }

    [Fact]
    public void SemiBoldBakesAndDiffersFromRegular()
    {
        var regular = BundledUiFont.Bake(16f);
        var bold = BundledUiFont.Bake(16f, BundledUiFontWeight.SemiBold);
        Assert.True(bold.Glyphs.ContainsKey('W'));
        Assert.NotEqual(regular.Pixels, bold.Pixels);
    }

    [Fact]
    public void ASharpCompanionDrawsAtHalfSizeOnlyAtScaleTwoAndNeverChangesMetrics()
    {
        var font = BundledUiFont.Bake(16f).CreateFont(11);
        float width = font.MeasureWidth("Buff Bot");
        float line = font.LineHeight;
        var sharpAtlas = BundledUiFont.Bake(32f);
        font.Sharp = new UiDatFontSharp(sharpAtlas.CreateFont(22), 2f);
        Assert.Equal(width, font.MeasureWidth("Buff Bot"));
        Assert.Equal(line, font.LineHeight);

        var (r1, c1) = ThemeDrawCapture.Context(pixelScale: 1f);
        c1.DrawStringDat(font, "B", 0f, 0f, Vector4.One);
        Assert.All(r1.DebugSpriteSegmentVerts, s => Assert.Equal(11u, s.Texture));

        var (r2, c2) = ThemeDrawCapture.Context(pixelScale: 2f);
        c2.DrawStringDat(font, "B", 0f, 0f, Vector4.One);
        var segment = Assert.Single(r2.DebugSpriteSegmentVerts);
        Assert.Equal(22u, segment.Texture);
        Assert.True(sharpAtlas.Glyphs.TryGetValue('B', out var fine));
        var xs = ThemeDrawCapture.Vertices(r2).Select(v => v.Position.X).ToList();
        Assert.Equal(fine.Width / 2f, xs.Max() - xs.Min(), 3);
    }
}
