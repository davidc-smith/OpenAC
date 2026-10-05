using AcDream.App.UI;

namespace AcDream.App.Tests.UI;

/// <summary>
/// The FlexDemo sample's markup builds, lays out inside its windows, and the
/// settings window grows when its summary gets longer. The bindings here
/// mirror the sample's property names (the sample is not referenced by the
/// test project).
/// </summary>
public sealed class FlexDemoSampleTests
{
    private sealed class Finder
    {
        public string Search => string.Empty;
        public Action<string> SetSearch => _ => { };
        public IReadOnlyList<string> Results { get; } = ["Asheron", "Holtburg"];
        public int Selected => -1;
        public Action<int> Select => _ => { };
        public string Status => "2 found";
        public Action Find => () => { };
        public Action<string> Submit => _ => { };
        public Action Clear => () => { };
        public Action Done => () => { };
    }

    private sealed class Settings
    {
        public IReadOnlyList<string> Profiles { get; } = ["Mage", "Melee"];
        public string Profile => "Mage";
        public Action<string> SetProfile => _ => { };
        public string Target => "Asheron";
        public Action<string> SetTarget => _ => { };
        public bool AutoRebuff => true;
        public Action ToggleAutoRebuff => () => { };
        public bool Announce => false;
        public Action ToggleAnnounce => () => { };
        public string Summary { get; set; } = "Mage";
        public string DetailCaption => "Show details";
        public Action ToggleDetail => () => { };
        public Action Apply => () => { };
    }

    private static string Markup(string file)
    {
        string? directory = AppContext.BaseDirectory;
        while (directory is not null && !File.Exists(Path.Combine(directory, "AcDream.slnx")))
            directory = Directory.GetParent(directory)?.FullName;
        Assert.NotNull(directory);
        return File.ReadAllText(Path.Combine(directory, "samples", "AcDream.Plugins.FlexDemo", file));
    }

    private static (UiRoot Root, MarkupWindow Window) Mount(string file, object binding)
    {
        MarkupWindow window = MarkupDocument.BuildWindow(Markup(file), binding, _ => (0u, 0, 0), themes: new PluginUiThemeSettings());
        var root = new UiRoot { Width = 1280, Height = 720 };
        root.AddChild(window.Frame);
        window.Register(root.WindowManager, file, new PluginWindowVisibilityController(null, startVisible: true));
        root.Tick(0, 0);
        ThemeDrawCapture.Draw(root);
        return (root, window);
    }

    private static void AssertInside(UiElement container)
    {
        foreach (UiElement child in container.Children)
        {
            Assert.True(child.Left >= 0f && child.Top >= 0f, $"{child.GetType().Name} starts outside its parent");
            Assert.True(child.Left + child.Width <= container.Width + 0.01f, $"{child.GetType().Name} overflows the width");
            Assert.True(child.Top + child.Height <= container.Height + 0.01f, $"{child.GetType().Name} overflows the height");
            // A scrolling group's content reaches past it by design.
            if (child is UiFlexGroup { ScrollArea: null }) AssertInside(child);
        }
    }

    [Fact]
    public void The_finder_lays_out_inside_its_window()
    {
        var (_, window) = Mount("finder.xml", new Finder());

        Assert.NotNull(window.TitleBar);
        Assert.Equal((330f, 289f), (window.Frame.Width, window.Frame.Height));
        AssertInside(window.ContentRoot);
        UiElement list = window.ContentRoot.Children[1];
        Assert.IsType<UiMarkupList>(list);
        Assert.True(list.Height > 150f);
    }

    [Fact]
    public void The_settings_window_sizes_itself_and_grows_with_its_summary()
    {
        var settings = new Settings();
        var (root, window) = Mount("settings.xml", settings);
        float width = window.Frame.Width;
        AssertInside(window.ContentRoot);

        settings.Summary = "Mage on Asheron: rebuff on, announcements off, and a much longer line besides";
        for (int i = 0; i < 3; i++) { root.Tick(0, 0); ThemeDrawCapture.Draw(root); }

        Assert.True(window.Frame.Width > width);
        AssertInside(window.ContentRoot);
    }

    [Fact]
    public void The_settings_window_scrolls_when_it_is_made_short()
    {
        var (root, window) = Mount("settings.xml", new Settings());
        Assert.True(root.WindowManager.TryGet("settings.xml", out RetailWindowHandle handle));
        var content = (UiPluginContentHost)window.ContentRoot;
        Assert.False(content.ScrollArea!.Vertical.Visible);

        handle.ResizeTo(window.Frame.Width, 100f);
        root.Tick(0, 0);
        ThemeDrawCapture.Draw(root);

        Assert.Equal(100f, window.Frame.Height);
        Assert.True(content.ScrollArea.Vertical.Visible);
    }

    [Fact]
    public void The_gallery_wraps_its_icons_and_scrolls_them()
    {
        var (root, window) = Mount("gallery.xml", new object());
        AssertInside(window.ContentRoot);

        var grid = Assert.IsType<UiFlexGroup>(window.ContentRoot.Children[1]);
        Assert.True(grid.ScrollArea!.Vertical.Visible);
        // 30 icons of 32 at a 4-point gap, five a line beside the bar: six lines.
        Assert.Equal(212, grid.ScrollArea.Y.ContentHeight);

        var at = grid.ScreenPosition + new System.Numerics.Vector2(20f, 20f);
        root.OnMouseMove((int)at.X, (int)at.Y);
        root.OnScroll(0, -1, shift: false);

        Assert.Equal(48, grid.ScrollArea.Y.ScrollY);

        // The recent strip: twelve icons in a row that scrolls sideways, with Shift and the wheel.
        var strip = Assert.IsType<UiFlexGroup>(window.ContentRoot.Children[3]);
        Assert.True(strip.ScrollArea!.Horizontal.Visible);
        Assert.Equal(48f, strip.Height);
        at = strip.ScreenPosition + new System.Numerics.Vector2(20f, 10f);
        root.OnMouseMove((int)at.X, (int)at.Y);
        root.OnScroll(0, -1, shift: true);

        Assert.Equal(48, strip.ScrollArea.X.ScrollY);
    }
}
