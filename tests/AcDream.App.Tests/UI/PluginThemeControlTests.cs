using System.Numerics;
using AcDream.App.UI;

namespace AcDream.App.Tests.UI;

/// <summary>Themed controls draw the modern shapes and states; see PluginUiStyle.</summary>
public sealed class PluginThemeControlTests
{
    private static readonly PluginUiPalette P = PluginUiPalette.Moss;

    internal sealed class Binding
    {
        public Action Click => () => { };
        public bool On => true;
        public bool Off => false;
        public Action Toggle => () => { };
        public IReadOnlyList<string> Items => ["Alpha", "Beta", "Gamma", "Delta", "Epsilon"];
        public string Selected => "Beta";
        public int Index => 1;
        public float Value => 0.5f;
        public Action<float> Changed => _ => { };
        public IReadOnlyList<string> Log => Enumerable.Range(0, 40).Select(i => $"line {i}").ToArray();
        public float Fill => 0.5f;
    }

    /// <summary>Builds one themed window around <paramref name="body"/>, switched to Moss.</summary>
    internal static (UiRoot Root, UiNineSlicePanel Panel) Themed(string body)
    {
        var settings = new PluginUiThemeSettings { Theme = PluginUiTheme.Moss };
        var panel = MarkupDocument.Build(
            $"<panel x=\"0\" y=\"0\" w=\"400\" h=\"300\" theme=\"plugin\">{body}</panel>",
            new Binding(), _ => (0u, 0, 0), themes: settings);
        var root = new UiRoot { Width = 600, Height = 600 };
        root.AddChild(panel);
        root.Tick(0.016, 1);
        return (root, panel);
    }

    internal static List<(Vector2 Position, Vector4 Color)> DrawElement(UiElement element)
    {
        var (renderer, ctx) = ThemeDrawCapture.Context();
        element.DrawSelfAndChildren(ctx);
        return ThemeDrawCapture.Vertices(renderer);
    }

    [Fact]
    public void AThemedButtonShowsHoverAndPressWithoutChangingWhatItHandles()
    {
        var (_, panel) = Themed("<button x=\"10\" y=\"10\" w=\"80\" h=\"24\" text=\"Go\" onclick=\"{Click}\" />");
        var button = Assert.IsType<UiSimpleButton>(panel.Children[0]);
        Assert.Same(P, button.ThemePalette);
        Assert.True(ThemeDrawCapture.HasColor(DrawElement(button), P.Field));

        button.OnEvent(new UiEvent { Type = UiEventType.HoverEnter });
        Assert.True(ThemeDrawCapture.HasColor(DrawElement(button), PluginUiStyle.Hover(P, P.Field)));
        bool handledDown = button.OnEvent(new UiEvent { Type = UiEventType.MouseDown });
        Assert.False(handledDown);
        Assert.True(ThemeDrawCapture.HasColor(DrawElement(button), PluginUiStyle.Pressed(P.Field)));
    }

    [Fact]
    public void ASelectedThemedTabIsAPillWithoutUnderline()
    {
        var (_, panel) = Themed(
            "<tab x=\"10\" y=\"10\" w=\"80\" h=\"24\" text=\"Status\" selected=\"{On}\" onclick=\"{Click}\" />" +
            "<tab x=\"100\" y=\"10\" w=\"80\" h=\"24\" text=\"Spells\" selected=\"{Off}\" onclick=\"{Click}\" />");
        var selected = Assert.IsType<UiMarkupTabButton>(panel.Children[0]);
        var other = Assert.IsType<UiMarkupTabButton>(panel.Children[1]);
        var v = DrawElement(selected);
        Assert.True(ThemeDrawCapture.HasColor(v, P.Selected));
        Assert.False(ThemeDrawCapture.HasColor(v, P.Accent));
        Assert.Equal(P.Text, selected.TextColor);
        Assert.Equal(P.Muted, other.TextColor);
    }

    [Fact]
    public void AThemedToggleIsASwitchWithItsCaptionAfterIt()
    {
        var (_, panel) = Themed("<toggle x=\"10\" y=\"10\" w=\"160\" h=\"20\" text=\"Auto\" checked=\"{On}\" onclick=\"{Toggle}\" />");
        var toggle = Assert.IsType<UiMarkupToggle>(panel.Children[0]);
        var v = DrawElement(toggle);
        Assert.True(ThemeDrawCapture.HasColor(v, P.Text));                    // knob, on
        Assert.True(ThemeDrawCapture.HasColor(v, P.Accent));                  // switch edge, on
        // No font in this build, so only the switch is drawn: it ends where the caption's gap begins.
        Assert.True(v.Where(p => p.Color.W > 0.05f).Max(p => p.Position.X)
            <= toggle.Left + 1f + PluginUiStyle.SwitchWidth + 0.51f);
    }
}
