using AcDream.App.UI;
using AcDream.Plugin.Abstractions;
using AcDream.UI.Abstractions.Panels.Settings;

namespace AcDream.App.Tests.UI;

public sealed class PluginDockModeTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(), "acdream-dock-mode-tests-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
    }

    private SettingsStore Store() => new(Path.Combine(_directory, "settings.json"));

    private static (UiRoot Root, PluginSidePanel Dock, List<RetailWindowHandle> Windows) Mount(
        PluginUiThemeSettings settings, int count = 2)
    {
        var root = new UiRoot { Width = 800f, Height = 600f };
        var dock = new PluginSidePanel(root.WindowManager, _ => (0u, 0, 0), font: null, settings);
        root.AddChild(dock);
        root.WindowManager.Register(WindowNames.PluginShelf, dock, dock, controller: dock);
        var windows = new List<RetailWindowHandle>();
        for (int i = 0; i < count; i++)
        {
            var frame = new UiPanel { Left = 300f, Top = 50f + i * 120f, Width = 200f, Height = 100f };
            root.AddChild(frame);
            RetailWindowHandle handle = root.WindowManager.Register($"plugin:test:{i}", frame);
            dock.Add(new PluginUiOwner("test", "Test"), new PluginPanelDescriptor($"w{i}", $"Window {i}"), handle);
            windows.Add(handle);
        }
        root.Tick(0.016d, 16L);
        return (root, dock, windows);
    }

    private static void Drag(UiRoot root, PluginSidePanel dock, int dx, int dy, bool release = true)
    {
        int x = (int)dock.Left + 10, y = (int)dock.Top + 5;   // on the handle row
        root.OnMouseDown(UiMouseButton.Left, x, y);
        root.OnMouseMove(x + dx, y + dy);
        if (release) root.OnMouseUp(UiMouseButton.Left, x + dx, y + dy);
    }

    [Fact]
    public void PickingAModeInTheSettings_MovesTheDockAndKeepsItsTop()
    {
        var settings = new PluginUiThemeSettings();
        var (root, dock, _) = Mount(settings);
        float top = dock.Top;

        settings.Dock = PluginDockMode.Left;
        root.Tick(0.016d, 32L);
        Assert.Equal((0f, top, PluginUiStyle.RailWidth), (dock.Left, dock.Top, dock.Width));

        settings.Dock = PluginDockMode.Right;
        root.Tick(0.016d, 48L);
        Assert.Equal((800f - PluginUiStyle.RailWidth, top), (dock.Left, dock.Top));

        settings.Dock = PluginDockMode.Floating;
        root.Tick(0.016d, 64L);
        Assert.Equal((800f - PluginUiStyle.DockWidth - 10f, top, PluginUiStyle.DockWidth), (dock.Left, dock.Top, dock.Width));

        settings.Dock = PluginDockMode.Left;
        root.Tick(0.016d, 80L);
        settings.Dock = PluginDockMode.Floating;
        root.Tick(0.016d, 96L);
        Assert.Equal(10f, dock.Left);
    }

    [Fact]
    public void DraggingNearTheLeftEdge_SnapsAtOnce_AndSavesTheModeOnlyWhenTheDragEnds()
    {
        var store = Store();
        var settings = new PluginUiThemeSettings(store);
        var (root, dock, _) = Mount(settings);

        Drag(root, dock, dx: -4, dy: 30, release: false);
        Assert.Equal(PluginDockMode.Left, dock.Mode);
        Assert.Equal(0f, dock.Left);
        Assert.Equal(PluginUiStyle.RailWidth, dock.Width);
        Assert.Equal(PluginDockMode.Floating, settings.Dock);
        Assert.Equal("floating", store.LoadPluginUi().Dock);

        root.OnMouseUp(UiMouseButton.Left, (int)dock.Left + 6, (int)dock.Top + 5);
        Assert.Equal(PluginDockMode.Left, settings.Dock);
        Assert.Equal("left", store.LoadPluginUi().Dock);
        Assert.Equal(146f, dock.Top);
    }

    [Fact]
    public void DraggingNearTheRightEdge_SnapsToTheRight()
    {
        var settings = new PluginUiThemeSettings();
        var (root, dock, _) = Mount(settings);

        Drag(root, dock, dx: 800 - 48 - 10 - 4, dy: 0);

        Assert.Equal(PluginDockMode.Right, settings.Dock);
        Assert.Equal(800f - PluginUiStyle.RailWidth, dock.Left);
    }

    [Fact]
    public void ARail_SlidesAlongItsEdge_AndFloatsWhenPulledMoreThan32Away()
    {
        var settings = new PluginUiThemeSettings { Dock = PluginDockMode.Left };
        var (root, dock, _) = Mount(settings);
        Assert.Equal(0f, dock.Left);

        Drag(root, dock, dx: 20, dy: 60);   // pointer ends at x = 30
        Assert.Equal(PluginDockMode.Left, settings.Dock);
        Assert.Equal((0f, 176f), (dock.Left, dock.Top));

        Drag(root, dock, dx: 25, dy: 0);    // pointer ends at x = 35
        Assert.Equal(PluginDockMode.Floating, settings.Dock);
        Assert.Equal(PluginUiStyle.DockWidth, dock.Width);
        Assert.Equal(25f, dock.Left);
    }

    [Fact]
    public void ARightRail_StaysOnTheEdgeWhenTheScreenResizes()
    {
        var settings = new PluginUiThemeSettings { Dock = PluginDockMode.Right };
        var (root, dock, _) = Mount(settings);
        Assert.Equal(800f - 46f, dock.Left);

        root.Width = 1000f;
        root.Tick(0.016d, 32L);
        Assert.Equal(1000f - 46f, dock.Left);

        dock.RestoreWindowState(new RetainedWindowState(Collapsed: true));
        root.Tick(0.016d, 48L);
        Assert.Equal((1000f - PluginUiStyle.RailTabWidth, PluginUiStyle.RailTabHeight), (dock.Left, dock.Height));
    }

    [Fact]
    public void EdgePills_EaseToTheirHeights_AndTheFrontWindowGetsTheTallOne()
    {
        var settings = new PluginUiThemeSettings { Dock = PluginDockMode.Left };
        var (root, dock, windows) = Mount(settings, count: 3);
        windows[0].Show();
        windows[1].Show();
        windows[2].Hide();
        root.WindowManager.BringToFront(windows[0].OuterFrame);
        var buttons = dock.Children.OfType<PluginSidePanel.PluginShelfButton>().ToArray();

        root.Tick(0.06d, 100L);
        Assert.InRange(buttons[0].PillHeight, 1f, 19f);   // still easing: 20pt takes 120ms

        for (int i = 0; i < 10; i++) root.Tick(0.016d, 200L + i);
        Assert.Same(windows[0], dock.FrontWindow);
        Assert.Equal(PluginUiStyle.RailPillFront, buttons[0].PillHeight);
        Assert.Equal(PluginUiStyle.RailPillOpen, buttons[1].PillHeight);
        Assert.Equal(0f, buttons[2].PillHeight);

        root.WindowManager.BringToFront(windows[1].OuterFrame);
        for (int i = 0; i < 10; i++) root.Tick(0.016d, 400L + i);
        Assert.Equal(PluginUiStyle.RailPillOpen, buttons[0].PillHeight);
        Assert.Equal(PluginUiStyle.RailPillFront, buttons[1].PillHeight);
    }

    [Fact]
    public void ASavedRail_RestoresToItsEdgeWithItsTop()
    {
        var store = Store();
        var settings = new PluginUiThemeSettings(store) { Dock = PluginDockMode.Right };
        var (root, dock, _) = Mount(settings);
        using (var persistence = new RetailWindowLayoutPersistence(
                   root.WindowManager, store, () => "Alice", () => (800, 600)))
        {
            root.WindowManager.MoveTo(WindowNames.PluginShelf, 300f, 222f);
        }

        var settings2 = new PluginUiThemeSettings(store);
        var (root2, dock2, _) = Mount(settings2);
        using var persistence2 = new RetailWindowLayoutPersistence(
            root2.WindowManager, store, () => "Alice", () => (800, 600));
        persistence2.RestoreAll();
        root2.Tick(0.016d, 32L);

        Assert.Equal(PluginDockMode.Right, dock2.Mode);
        Assert.Equal((800f - 46f, 222f), (dock2.Left, dock2.Top));
    }
}
