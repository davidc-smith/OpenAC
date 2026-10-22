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

    [Theory]
    [MemberData(nameof(Themes))]
    public void Layout_builds_and_lays_out(PluginUiTheme theme)
    {
        var (_, panel) = Mount("layout.xml", new LayoutBinding(), theme);
        AssertInside(panel);
    }

    [Fact]
    public void Layout_bound_expander_only_asks_and_the_unbound_one_reports()
    {
        var binding = new LayoutBinding();
        var (root, panel) = Mount("layout.xml", binding);

        Click(root, panel, 200, 103);   // "Advanced (bound)" header
        Assert.False(binding.AdvancedOpen);
        Assert.Equal("Advanced folded", binding.Status);

        Click(root, panel, 200, 37);    // "General" header
        Assert.Equal("General folded", binding.Status);
    }

    /// <summary>Records dialogs, modal shows and hides; a test answers a dialog by calling its callback.</summary>
    private sealed class FakeUi : IUiRegistry
    {
        public List<(PluginDialog Dialog, Action<int> OnResult)> Dialogs { get; } = [];
        public List<(string View, string? Owner)> Modals { get; } = [];
        public List<string> Hidden { get; } = [];

        public void AddMarkupPanel(string markupPath, object binding) => throw new NotSupportedException();
        public void AddPanel(PluginPanelDescriptor descriptor, string markupPath, object binding) => throw new NotSupportedException();
        public bool Confirm(PluginDialog dialog, Action<int> onResult) { Dialogs.Add((dialog, onResult)); return true; }
        public bool ShowModal(string viewName, string? ownerView = null) { Modals.Add((viewName, ownerView)); return true; }
        public bool HidePanel(string viewName) { Hidden.Add(viewName); return true; }
    }

    [Theory]
    [MemberData(nameof(Themes))]
    public void Table_builds_and_lays_out(PluginUiTheme theme)
    {
        var (_, panel) = Mount("table.xml", new TableBinding(new FakeUi()), theme);
        AssertInside(panel);
    }

    [Fact]
    public void Table_keep_all_and_the_menu_change_the_rows_they_should()
    {
        var binding = new TableBinding(new FakeUi());

        binding.KeepAll(true);
        Assert.All(binding.Keep, Assert.True);

        binding.SetPicked([2, 5]);
        binding.MenuOpened(5);
        binding.MenuPicked(1);   // Don't keep

        Assert.Equal([true, true, false, true, true, false, true, true], binding.Keep);
    }

    [Fact]
    public void Table_remove_asks_first_and_removes_only_on_the_first_button()
    {
        var ui = new FakeUi();
        var binding = new TableBinding(ui);
        binding.SetPicked([1, 3]);

        binding.RemoveSelected();
        var (dialog, answer) = Assert.Single(ui.Dialogs);
        Assert.Equal(["Remove", "Keep"], dialog.EffectiveButtons);
        Assert.Equal("table", dialog.OwnerView);

        answer(1);
        Assert.Equal(8, binding.Names.Count);
        Assert.Equal("Nothing removed", binding.Status);

        binding.RemoveSelected();
        ui.Dialogs[1].OnResult(0);
        Assert.Equal(6, binding.Names.Count);
        Assert.DoesNotContain("Steel Salvage", binding.Names);
        Assert.DoesNotContain("Peridot", binding.Names);
        Assert.Empty(binding.Picked);
    }

    [Theory]
    [MemberData(nameof(Themes))]
    public void Tree_builds_and_lays_out(PluginUiTheme theme)
    {
        var (_, panel) = Mount("tree.xml", new TreeBinding(), theme);
        AssertInside(panel);
    }

    [Fact]
    public void Tree_menu_turns_a_whole_branch_off()
    {
        var binding = new TreeBinding();

        binding.MenuOpened(0);
        binding.MenuPicked(1);   // Disable branch

        Assert.All(binding.On.Take(7), Assert.False);
        Assert.All(binding.On.Skip(7), Assert.True);
        Assert.Equal("Looting branch off", binding.Status);
    }

    [Fact]
    public void Tree_reports_expansion_by_name()
    {
        var binding = new TreeBinding();
        binding.Expanded("combat.targets", false);
        Assert.Equal("Targets collapsed", binding.Status);
    }
}
