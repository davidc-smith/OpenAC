using AcDream.App.UI;
using AcDream.Plugin.Abstractions;
using AcDream.Plugins.ControlsDemo;

namespace AcDream.App.Tests.UI;

/// <summary>
/// The ControlsDemo sample's windows build against its real bindings and draw
/// in every plugin theme, with their controls inside the window, and the
/// interactions each window demonstrates reach the binding as the docs say.
/// </summary>
public sealed class ControlsDemoSampleTests
{
    public static TheoryData<PluginUiTheme> Themes => new() { PluginUiTheme.Classic, PluginUiTheme.Moss, PluginUiTheme.Brass };

    private static string Markup(string file)
    {
        string? directory = AppContext.BaseDirectory;
        while (directory is not null && !File.Exists(Path.Combine(directory, "AcDream.slnx")))
            directory = Directory.GetParent(directory)?.FullName;
        Assert.NotNull(directory);
        return File.ReadAllText(Path.Combine(directory, "samples", "AcDream.Plugins.ControlsDemo", file));
    }

    private static (UiRoot Root, UiPanel Panel) Mount(string file, object binding, PluginUiTheme theme = PluginUiTheme.Classic)
    {
        UiPanel panel = MarkupDocument.Build(Markup(file), binding, id => (id, 16, 16),
            themes: new PluginUiThemeSettings { Theme = theme });
        var root = new UiRoot { Width = 1600, Height = 900 };
        root.AddChild(panel);
        root.Tick(0, 0);
        MarkupDrawProbe.Draw(root, 1600, 900);
        return (root, panel);
    }

    /// <summary>Every child starts inside the window; all but labels (sized by their text) end inside it.</summary>
    private static void AssertInside(UiPanel panel)
    {
        foreach (UiElement child in panel.Children)
        {
            Assert.True(child.Left >= 0f && child.Top >= 0f, $"{child.GetType().Name} starts outside the window");
            if (child is UiLabel) continue;
            Assert.True(child.Left + child.Width <= panel.Width, $"{child.GetType().Name} overflows the width");
            Assert.True(child.Top + child.Height <= panel.Height, $"{child.GetType().Name} overflows the height");
        }
    }

    /// <summary>A left click at a point given in the window's own coordinates.</summary>
    private static void Click(UiRoot root, UiPanel panel, float x, float y)
    {
        int sx = (int)(panel.Left + x), sy = (int)(panel.Top + y);
        root.OnMouseDown(UiMouseButton.Left, sx, sy);
        root.OnMouseUp(UiMouseButton.Left, sx, sy);
        root.Tick(0, 0);
    }

    [Theory]
    [MemberData(nameof(Themes))]
    public void Inputs_builds_and_lays_out(PluginUiTheme theme)
    {
        var (_, panel) = Mount("inputs.xml", new InputsBinding(), theme);
        AssertInside(panel);
    }

    [Fact]
    public void Inputs_select_all_ticks_every_pickup_and_shows_a_dash_when_they_disagree()
    {
        var binding = new InputsBinding();
        var (root, panel) = Mount("inputs.xml", binding);
        Assert.Null(binding.AllPickups);

        Click(root, panel, 20, 54);   // "Everything below"
        Assert.True(binding.Rares && binding.Scrolls && binding.Salvage);
        Assert.Equal(true, binding.AllPickups);

        Click(root, panel, 38, 74);   // "Rares"
        Assert.False(binding.Rares);
        Assert.Null(binding.AllPickups);
        Assert.Equal("Rares off", binding.Status);
    }

    [Theory]
    [MemberData(nameof(Themes))]
    public void Pickers_builds_and_lays_out(PluginUiTheme theme)
    {
        var (_, panel) = Mount("pickers.xml", new PickersBinding(), theme);
        AssertInside(panel);
    }

    [Fact]
    public void Pickers_note_says_which_chords_a_plugin_hotkey_can_use()
    {
        var binding = new PickersBinding();
        Assert.Equal("Hotkeys: radar usable, pause unbound", binding.KeyNote);

        binding.SetRadarKey("F13");

        Assert.Equal("Hotkeys: radar not usable, pause unbound", binding.KeyNote);
        Assert.Equal("Radar key set to F13", binding.Status);
    }
}
