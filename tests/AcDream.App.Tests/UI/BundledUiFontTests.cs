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
        var settings = new PluginUiThemeSettings(modernFont: new(() => modern));
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
        var sharp = new UiDatFontSharp(sharpAtlas.CreateFont(22), 2f);
        font.SharpSource = scale => scale == 2f ? sharp : null;
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

    [Theory]
    [InlineData(16f, 1f, 1f)]
    [InlineData(16f, 1.25f, 1.25f)]
    [InlineData(16f, 1.5f, 1.5f)]
    [InlineData(16f, 2f, 2f)]
    [InlineData(16f, 3f, 2f)]       // 48 pixels is past the largest bake
    [InlineData(12f, 3f, 2.5f)]     // 32 / 12, down to a quarter step
    [InlineData(16f, 1.6f, 1.5f)]
    public void ASharpTwinIsBakedAtTheDisplayScaleWhereItFits(float pixelHeight, float pixelScale, float expected) =>
        Assert.Equal(expected, BundledUiFont.SharpScaleFor(pixelHeight, pixelScale));

    [Fact]
    public void AtOneAndAHalfTheSharpTwinPutsEachTexelOnOneDevicePixel()
    {
        var font = BundledUiFont.Bake(16f).CreateFont(11);
        var atlas = BundledUiFont.Bake(24f);
        var sharp = new UiDatFontSharp(atlas.CreateFont(33), 1.5f);
        font.SharpSource = scale => scale == 1.5f ? sharp : null;

        var (renderer, ctx) = ThemeDrawCapture.Context(pixelScale: 1.5f);
        ctx.DrawStringDat(font, "Buff", 3.3f, 7.1f, Vector4.One);

        Assert.All(renderer.DebugSpriteSegmentVerts, s => Assert.Equal(33u, s.Texture));
        var v = ThemeDrawCapture.Vertices(renderer);
        Assert.NotEmpty(v);
        Assert.All(v, p =>
        {
            Assert.Equal(MathF.Round(p.Position.X * 1.5f), p.Position.X * 1.5f, 3);
            Assert.Equal(MathF.Round(p.Position.Y * 1.5f), p.Position.Y * 1.5f, 3);
        });
        Assert.True(atlas.Glyphs.TryGetValue('B', out var fine));
        var first = v.Take(6).Select(p => p.Position.X).ToList();
        Assert.Equal(fine.Width / 1.5f, first.Max() - first.Min(), 3);
    }

    [Fact]
    public void ASharpTwinIsAskedForOncePerScaleAndNeverOnAStandardDisplay()
    {
        var font = BundledUiFont.Bake(16f).CreateFont(11);
        var asked = new List<float>();
        font.SharpSource = scale => { asked.Add(scale); return null; };

        var (_, standard) = ThemeDrawCapture.Context(pixelScale: 1f);
        standard.DrawStringDat(font, "Buff", 0f, 0f, Vector4.One);
        Assert.Empty(asked);

        var (_, dense) = ThemeDrawCapture.Context(pixelScale: 2f);
        dense.DrawStringDat(font, "Buff", 0f, 0f, Vector4.One);
        dense.DrawStringDat(font, "Bot", 0f, 20f, Vector4.One);
        Assert.Equal([2f], asked);
    }

    [Fact]
    public void AClassicThemedWindowNeverLoadsTheModernFonts()
    {
        var classic = BundledUiFont.Bake(12).CreateFont(2);
        var modern = BundledUiFont.Bake().CreateFont(1);
        int loads = 0;
        var settings = new PluginUiThemeSettings(
            modernFont: new(() => { loads++; return modern; }),
            modernTitleFont: new(() => { loads++; return modern; }));
        var panel = MarkupDocument.Build("""
            <panel theme="plugin" x="0" y="0" w="200" h="100" title="Title">
              <label x="1" y="20" text="Body" />
              <button x="1" y="40" w="80" h="24" text="Go" />
            </panel>
            """, new object(), _ => (0u, 0, 0), datFont: classic, themes: settings);
        var root = new UiRoot { Width = 300, Height = 200 }; root.AddChild(panel);
        root.Tick(.016, 1);
        ThemeDrawCapture.Draw(root);
        Assert.Equal(0, loads);

        settings.Theme = PluginUiTheme.Moss; root.Tick(.016, 2);
        Assert.Equal(2, loads);
        Assert.Same(modern, ((UiLabel)panel.Children[1]).DatFont);
    }
}
