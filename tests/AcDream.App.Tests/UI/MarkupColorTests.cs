using AcDream.App.UI;
using AcDream.App.UI.Markup;

namespace AcDream.App.Tests.UI;

public class MarkupColorTests
{
    private sealed class Binding
    {
        public uint Bar { get; set; } = 0xFFFF0000u;
        public string Text { get; set; } = "#FFFF0000";
        public double Wrong { get; set; }
        public uint? LastBar;
        public Action<uint> SetBar => v => { Bar = v; LastBar = v; };
        public Action<string> SetText => v => Text = v;
    }

    private sealed class FakeTextures : IMarkupTextureService
    {
        public uint Upload(byte[] rgba, int width, int height, string debugName) => 1u;
        public void Release(uint texture) { }
    }

    private sealed class FakeState : IMarkupControlState
    {
        public Dictionary<(string, string), string> Values { get; } = new();
        public int Generation => 1;
        public string? Get(string control, string kind) => Values.GetValueOrDefault((control, kind));
        public void Set(string control, string kind, string value) => Values[(control, kind)] = value;
        public void Remove(string control, string kind) => Values.Remove((control, kind));
    }

    private const string Attrs = "value=\"{Bar}\" onchange=\"{SetBar}\"";

    private static UiNineSlicePanel BuildPanel(string inner, object binding,
        IMarkupTextureService? textures = null, IMarkupControlState? shared = null) =>
        MarkupDocument.Build(
            $"<panel x=\"0\" y=\"0\" w=\"300\" h=\"100\">{inner}</panel>",
            binding, _ => (1u, 16, 16), textures: textures, sharedControlState: shared);

    private static UiMarkupColor BuildOne(string attributes, object binding) =>
        Assert.IsType<UiMarkupColor>(Assert.Single(
            BuildPanel($"<color x=\"8\" y=\"8\" w=\"40\" h=\"18\" {attributes}/>", binding).Children));

    [Fact]
    public void Color_BuildsWithPositionBindingsAndAlpha()
    {
        var binding = new Binding();
        var color = BuildOne(Attrs + " alpha=\"true\" name=\"bar\" enabled=\"false\" tooltip=\"Bar\"", binding);
        Assert.Equal((8f, 8f, 40f, 18f), (color.Left, color.Top, color.Width, color.Height));
        Assert.Equal(0xFFFF0000u, color.ValueSource!());
        color.Changed!(0x80000000u);
        Assert.Equal(0x80000000u, binding.LastBar);
        Assert.True(color.AllowAlpha);
        Assert.False(color.Enabled);
        Assert.Null(color.Textures);
    }

    [Fact]
    public void Color_AlphaDefaultsToFalse() => Assert.False(BuildOne(Attrs, new Binding()).AllowAlpha);

    [Theory]
    [InlineData("onchange=\"{SetBar}\"", "color value")]
    [InlineData("value=\"#FFFF0000\" onchange=\"{SetBar}\"", "color value")]
    [InlineData("value=\"{Wrong}\" onchange=\"{SetBar}\"", "color value")]
    [InlineData("value=\"{Bar}\"", "color onchange")]
    [InlineData("value=\"{Bar}\" onchange=\"{SetText}\"", "color onchange")]
    [InlineData("value=\"{Text}\" onchange=\"{SetBar}\"", "color onchange")]
    [InlineData(Attrs + " alpha=\"yes\"", "alpha")]
    public void Color_BadAttributesFailTheBuild(string attributes, string context)
    {
        var ex = Assert.Throws<FormatException>(() => BuildOne(attributes, new Binding()));
        Assert.Contains(context, ex.Message);
    }

    [Fact]
    public void Color_EveryPickerSharesTheTexturesAndRecentColours()
    {
        var textures = new FakeTextures();
        var shared = new FakeState();
        var panel = BuildPanel(
            $"<color x=\"8\" y=\"8\" w=\"40\" h=\"18\" {Attrs}/>"
            + $"<group x=\"0\" y=\"40\" w=\"100\" h=\"40\"><color x=\"8\" y=\"8\" w=\"40\" h=\"18\" {Attrs}/></group>",
            new Binding(), textures, shared);
        var first = (UiMarkupColor)panel.Children[0];
        var second = (UiMarkupColor)panel.Children[1].Children[0];
        Assert.Same(textures, first.Textures);
        Assert.Same(textures, second.Textures);
        Assert.Same(first.Recent, second.Recent);
        first.Recent.Add(0xFF010203u);
        Assert.Equal("#FF010203", shared.Values[("color", "recent")]);
    }

    [Fact]
    public void Color_FollowsThePluginTheme()
    {
        var themes = new PluginUiThemeSettings { Theme = PluginUiTheme.Moss };
        var panel = MarkupDocument.Build(
            "<panel theme=\"plugin\" x=\"0\" y=\"0\" w=\"300\" h=\"100\">"
            + $"<color x=\"8\" y=\"8\" w=\"40\" h=\"18\" {Attrs}/></panel>",
            new Binding(), _ => (1u, 16, 16), themes: themes);
        Assert.Same(PluginUiPalette.Moss, panel.Children.OfType<UiMarkupColor>().Single().ThemePalette);
    }

    [Fact]
    public void Color_ThroughUiRootReportsInTheBindingsType()
    {
        var binding = new Binding();
        var panel = BuildPanel("<color x=\"8\" y=\"8\" w=\"40\" h=\"18\" value=\"{Text}\" onchange=\"{SetText}\"/>", binding);
        var root = new UiRoot { Width = 800, Height = 600 };
        root.AddChild(panel);
        var swatch = (UiMarkupColor)panel.Children[0];
        root.OnMouseDown(UiMouseButton.Left, 20, 15);
        root.OnMouseUp(UiMouseButton.Left, 20, 15);
        Assert.True(swatch.IsOpen);
        var (px, py, _, _) = swatch.Popup.PopupScreenRect;
        root.OnMouseDown(UiMouseButton.Left, (int)(px + UiMarkupColorPopup.HueX + 8), (int)(py + 6 + 64));
        root.OnMouseUp(UiMouseButton.Left, (int)(px + UiMarkupColorPopup.HueX + 8), (int)(py + 6 + 64));
        root.Tick(0.016, 16);
        Assert.Equal("#FF00FFFF", binding.Text);
    }
}
