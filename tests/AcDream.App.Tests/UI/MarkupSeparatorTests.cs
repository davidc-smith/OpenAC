using System.Linq;
using System.Numerics;
using AcDream.App.UI;

namespace AcDream.App.Tests.UI;

public class MarkupSeparatorTests
{
    private sealed class Binding
    {
        public uint LineColor { get; set; } = 0xFF00FF00u;
        public int Clicks { get; private set; }
        public Action Click => () => Clicks++;
    }

    private static UiMarkupSeparator Build(string attributes, Binding binding, string theme = "")
    {
        var themes = theme == "plugin" ? new PluginUiThemeSettings { Theme = PluginUiTheme.Moss } : null;
        var panel = MarkupDocument.Build(
            $"<panel {(theme == "plugin" ? "theme=\"plugin\" " : "")}x=\"0\" y=\"0\" w=\"300\" h=\"200\">"
            + $"<separator x=\"0\" y=\"0\" {attributes}/></panel>",
            binding, _ => (1u, 16, 16), themes: themes);
        return panel.Children.OfType<UiMarkupSeparator>().Single();
    }

    [Fact]
    public void Separator_HorizontalIsOneLineAcrossTheMiddle()
    {
        var separator = Build("w=\"200\" h=\"5\"", new Binding());
        var line = Assert.Single(MarkupDrawProbe.QuadBounds(MarkupDrawProbe.Draw(separator), UiMarkupSeparator.ClassicColor));
        Assert.Equal(200f, line.W, 3);
        Assert.Equal(1f, line.H, 3);
        Assert.Equal(2f, line.Y, 3);
    }

    [Fact]
    public void Separator_TallerThanWideIsVertical()
    {
        var separator = Build("w=\"5\" h=\"100\"", new Binding());
        var line = Assert.Single(MarkupDrawProbe.QuadBounds(MarkupDrawProbe.Draw(separator), UiMarkupSeparator.ClassicColor));
        Assert.Equal(1f, line.W, 3);
        Assert.Equal(100f, line.H, 3);
        Assert.Equal(2f, line.X, 3);
    }

    [Fact]
    public void Separator_LiteralAndBoundColours()
    {
        var literal = Build("w=\"100\" h=\"1\" color=\"#FFFF0000\"", new Binding());
        Assert.Equal(1, MarkupDrawProbe.CountQuads(MarkupDrawProbe.Draw(literal), new Vector4(1f, 0f, 0f, 1f)));

        var binding = new Binding();
        var bound = Build("w=\"100\" h=\"1\" color=\"{LineColor}\"", binding);
        Assert.Equal(1, MarkupDrawProbe.CountQuads(MarkupDrawProbe.Draw(bound), new Vector4(0f, 1f, 0f, 1f)));
        binding.LineColor = 0xFF0000FFu;
        Assert.Equal(1, MarkupDrawProbe.CountQuads(MarkupDrawProbe.Draw(bound), new Vector4(0f, 0f, 1f, 1f)));
    }

    [Fact]
    public void Separator_PluginThemeUsesTheBorderColourUnlessOneIsGiven()
    {
        var themed = Build("w=\"100\" h=\"1\"", new Binding(), theme: "plugin");
        Assert.Equal(PluginUiPalette.Moss.Border, themed.Color);
        var explicitColour = Build("w=\"100\" h=\"1\" color=\"#FFFF0000\"", new Binding(), theme: "plugin");
        Assert.Equal(new Vector4(1f, 0f, 0f, 1f), explicitColour.Color);
    }

    [Fact]
    public void Separator_ClicksPassThroughToWhatIsBeneath()
    {
        var binding = new Binding();
        var panel = MarkupDocument.Build(
            "<panel x=\"0\" y=\"0\" w=\"300\" h=\"200\">"
            + "<button x=\"0\" y=\"0\" w=\"100\" h=\"20\" text=\"Go\" onclick=\"{Click}\"/>"
            + "<separator x=\"0\" y=\"0\" w=\"100\" h=\"20\"/></panel>",
            binding, _ => (1u, 16, 16));
        var root = new UiRoot { Width = 800, Height = 600 };
        root.AddChild(panel);
        root.OnMouseDown(UiMouseButton.Left, 10, 10);
        root.OnMouseUp(UiMouseButton.Left, 10, 10);
        Assert.Equal(1, binding.Clicks);
    }

    [Fact]
    public void Separator_ZeroSizeDrawsNothing()
    {
        var separator = Build("w=\"0\" h=\"0\"", new Binding());
        Assert.Empty(MarkupDrawProbe.QuadBounds(MarkupDrawProbe.Draw(separator), UiMarkupSeparator.ClassicColor));
    }
}
