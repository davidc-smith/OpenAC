using AcDream.App.UI;
using AcDream.App.UI.Markup;
using AcDream.UI.Abstractions.Input;
using Silk.NET.Input;

namespace AcDream.App.Tests.UI;

public class MarkupHotkeyTests
{
    private sealed class Binding
    {
        public string NavKey { get; set; } = "Ctrl+N";
        public int NotString { get; set; }
        public string? Last { get; private set; }
        public Action<string> SetNavKey => v => { NavKey = v; Last = v; };
        public Action<int> WrongAction => _ => { };
    }

    private const string Attrs = "value=\"{NavKey}\" onchange=\"{SetNavKey}\"";

    private static UiMarkupHotkey BuildOne(string attributes, Binding binding,
        Func<KeyChord, string>? describeKey = null)
    {
        var panel = MarkupDocument.Build(
            $"<panel x=\"0\" y=\"0\" w=\"300\" h=\"100\"><hotkey x=\"8\" y=\"8\" w=\"140\" h=\"20\" {attributes}/></panel>",
            binding, _ => (1u, 16, 16), describeKey: describeKey);
        return Assert.IsType<UiMarkupHotkey>(Assert.Single(panel.Children));
    }

    [Fact]
    public void Hotkey_BuildsWithPositionAndBindings()
    {
        var binding = new Binding();
        var hotkey = BuildOne(Attrs + " name=\"nav\" enabled=\"false\"", binding);
        Assert.Equal((8f, 8f, 140f, 20f), (hotkey.Left, hotkey.Top, hotkey.Width, hotkey.Height));
        Assert.Equal("Ctrl+N", hotkey.ValueSource!());
        hotkey.Changed!("Alt+G");
        Assert.Equal("Alt+G", binding.Last);
        Assert.False(hotkey.Enabled);
        Assert.Null(hotkey.DescribeKey);
    }

    [Theory]
    [InlineData("onchange=\"{SetNavKey}\"", "hotkey value")]
    [InlineData("value=\"Ctrl+N\" onchange=\"{SetNavKey}\"", "hotkey value")]
    [InlineData("value=\"{NotString}\" onchange=\"{SetNavKey}\"", "hotkey value")]
    [InlineData("value=\"{NavKey}\"", "hotkey onchange")]
    [InlineData("value=\"{NavKey}\" onchange=\"{WrongAction}\"", "hotkey onchange")]
    [InlineData("value=\"{NavKey}\" onchange=\"{Missing}\"", "hotkey onchange")]
    public void Hotkey_BadBindingsFailTheBuild(string attributes, string context)
    {
        var ex = Assert.Throws<FormatException>(() => BuildOne(attributes, new Binding()));
        Assert.Contains(context, ex.Message);
    }

    [Fact]
    public void Hotkey_GetsTheDocumentsKeyNames()
    {
        var hotkey = BuildOne(Attrs, new Binding(), chord => "named " + chord.Key);
        Assert.Equal("named N", hotkey.DisplayText);
    }

    [Fact]
    public void Hotkey_FollowsThePluginTheme()
    {
        var themes = new PluginUiThemeSettings { Theme = PluginUiTheme.Moss };
        var panel = MarkupDocument.Build(
            "<panel theme=\"plugin\" x=\"0\" y=\"0\" w=\"300\" h=\"100\">"
            + $"<hotkey x=\"8\" y=\"8\" w=\"140\" h=\"20\" {Attrs}/></panel>",
            new Binding(), _ => (1u, 16, 16), themes: themes);
        Assert.Same(PluginUiPalette.Moss, panel.Children.OfType<UiMarkupHotkey>().Single().ThemePalette);
    }

    [Fact]
    public void Hotkey_ClickThroughUiRootTakesFocusAndCaptures()
    {
        var binding = new Binding();
        var panel = MarkupDocument.Build(
            $"<panel x=\"0\" y=\"0\" w=\"300\" h=\"100\"><hotkey x=\"8\" y=\"8\" w=\"140\" h=\"20\" {Attrs}/></panel>",
            binding, _ => (1u, 16, 16));
        var hotkey = (UiMarkupHotkey)panel.Children[0];
        hotkey.ModifiersSource = () => ModifierMask.Alt;
        var root = new UiRoot { Width = 800, Height = 600 };
        root.AddChild(panel);
        root.OnMouseDown(UiMouseButton.Left, 20, 15);
        root.OnMouseUp(UiMouseButton.Left, 20, 15);
        Assert.Same(hotkey, root.KeyboardFocus);
        root.OnKeyDown((int)Key.G);
        root.OnKeyUp((int)Key.G);
        Assert.Equal("Alt+G", binding.Last);
        Assert.Null(root.KeyboardFocus);
    }

    [Fact]
    public void Command_RoundTripsOnlyWhenAllowed()
    {
        var chord = new KeyChord(Key.M, ModifierMask.Ctrl | ModifierMask.Win);
        Assert.Equal("Ctrl+Cmd+M", MarkupHotkeyText.Format(chord, allowCommand: true));
        Assert.Equal(chord, MarkupHotkeyText.Parse("Ctrl+Cmd+M", allowCommand: true));
        Assert.Equal(chord, MarkupHotkeyText.Parse("ctrl+win+m", allowCommand: true));
        Assert.Null(MarkupHotkeyText.Parse("Ctrl+Cmd+M"));
        Assert.Equal("Ctrl+M", MarkupHotkeyText.Format(chord));
    }
}
