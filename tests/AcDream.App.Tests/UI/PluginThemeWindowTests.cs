using System.Numerics;
using AcDream.App.UI;

namespace AcDream.App.Tests.UI;

public sealed class PluginThemeWindowTests
{
    private static (UiRoot Root, UiNineSlicePanel Panel, PluginUiThemeSettings Settings) Build(
        string title, UiDatFont? modern = null, UiDatFont? bold = null, UiDatFont? classic = null)
    {
        var settings = new PluginUiThemeSettings(modernFont: modern, modernTitleFont: bold);
        var panel = MarkupDocument.Build(
            $"<panel x=\"100\" y=\"100\" w=\"200\" h=\"120\" {title} theme=\"plugin\"><label x=\"12\" y=\"30\" text=\"Body\" /></panel>",
            new object(), _ => (0u, 0, 0), datFont: classic, themes: settings);
        var root = new UiRoot { Width = 600, Height = 600 };
        root.AddChild(panel);
        return (root, panel, settings);
    }

    [Fact]
    public void ATitledThemedWindowDrawsShadowHeaderAndSeparator()
    {
        var (root, _, settings) = Build("title=\"Buff Bot\"");
        settings.Theme = PluginUiTheme.Moss;
        root.Tick(0.016, 1);
        var (renderer, ctx) = ThemeDrawCapture.Context();
        root.Draw(ctx);
        var v = ThemeDrawCapture.Vertices(renderer);
        var p = PluginUiPalette.Moss;
        Assert.Contains(v, x => x.Position.X < 100f);
        Assert.True(ThemeDrawCapture.HasColor(v, PluginUiStyle.Mix(p.Background, p.Text, 0.06f), 0.01f));
        Assert.Contains(v, x => MathF.Abs(x.Position.Y - 124f) < 0.01f
            && Vector4.Distance(x.Color, p.Border) < 0.01f);
    }

    [Fact]
    public void AnUntitledThemedWindowHasNoHeader()
    {
        var (root, _, settings) = Build("");
        settings.Theme = PluginUiTheme.Moss;
        root.Tick(0.016, 1);
        var (renderer, ctx) = ThemeDrawCapture.Context();
        root.Draw(ctx);
        var p = PluginUiPalette.Moss;
        Assert.False(ThemeDrawCapture.HasColor(
            ThemeDrawCapture.Vertices(renderer), PluginUiStyle.Mix(p.Background, p.Text, 0.06f), 0.002f));
    }

    [Fact]
    public void TheTitleMovesIntoTheHeaderInTheTitleFontAndBack()
    {
        var classic = BundledUiFont.Bake(12).CreateFont(1);
        var modern = BundledUiFont.Bake().CreateFont(2);
        var bold = BundledUiFont.Bake(16, BundledUiFontWeight.SemiBold).CreateFont(3);
        var (root, panel, settings) = Build("title=\"Buff Bot\"", modern, bold, classic);
        var title = Assert.IsType<UiLabel>(panel.Children[0]);
        Assert.Equal((8f, 4f), (title.Left, title.Top));
        settings.Theme = PluginUiTheme.Brass;
        root.Tick(0.016, 1);
        Assert.Same(bold, title.DatFont);
        Assert.Equal(12f, title.Left);
        Assert.Equal(MathF.Round((PluginUiStyle.HeaderHeight - bold.LineHeight) / 2f), title.Top);
        settings.Theme = PluginUiTheme.Classic;
        root.Tick(0.016, 2);
        Assert.Same(classic, title.DatFont);
        Assert.Equal((8f, 4f), (title.Left, title.Top));
    }
}
