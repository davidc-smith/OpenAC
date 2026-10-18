using AcDream.App.UI;
using AcDream.Plugin.Abstractions;
using AcDream.UI.Abstractions.Panels.Settings;

namespace AcDream.App.Tests.UI;

public sealed class MarkupWindowMountTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(), "acdream-markup-mount-tests-" + Guid.NewGuid().ToString("N"));
    private string PathName => Path.Combine(_directory, "settings.json");

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
    }

    private sealed class Binding
    {
        public bool Available { get; set; } = true;
    }

    private const string BarXml =
        "<panel x=\"100\" y=\"100\" w=\"300\" h=\"200\" titlebar=\"true\" title=\"T\" visible=\"{Available}\" />";

    private static (UiRoot Root, MarkupWindow Window, RetailWindowHandle Handle, Binding Bound) Mount(
        string xml = BarXml)
    {
        var bound = new Binding();
        MarkupWindow window = MarkupDocument.BuildWindow(xml, bound, _ => (0u, 0, 0));
        var visibility = new PluginWindowVisibilityController(window.Frame.VisibleSource, startVisible: true);
        window.Frame.VisibleSource = visibility.ShouldBeVisible;
        window.Frame.Visible = visibility.ShouldBeVisible();
        var root = new UiRoot { Width = 1280, Height = 720 };
        root.AddChild(window.Frame);
        RetailWindowHandle handle = window.Register(root.WindowManager, "plugin:t:main", visibility);
        root.Tick(0, 0);
        return (root, window, handle, bound);
    }

    [Fact]
    public void RegisterUsesTheContentHostAndTheAuthoredRevision()
    {
        var (_, window, handle, _) = Mount();
        Assert.Same(window.Frame, handle.OuterFrame);
        Assert.Same(window.ContentRoot, handle.ContentRoot);
        Assert.IsType<UiPluginContentHost>(handle.ContentRoot);
        Assert.Equal(window.AuthoredGeometryRevision, handle.AuthoredGeometryRevision);
    }

    [Fact]
    public void CloseClearsThePlayersRequest_AndRaisesHiddenThenClosed()
    {
        var (root, window, handle, bound) = Mount();
        var events = new List<string>();
        handle.Hidden += _ => events.Add("hidden");
        handle.Closed += _ => events.Add("closed");

        PluginCloseButton close = window.TitleBar!.Close;
        (int x, int y) = (100 + (int)close.Left + 9, 100 + (int)close.Top + 9);
        root.OnMouseDown(UiMouseButton.Left, x, y);
        root.OnMouseUp(UiMouseButton.Left, x, y);
        root.Tick(0.016, 1);

        Assert.Equal(new[] { "hidden", "closed" }, events);
        Assert.False(window.Frame.Visible);

        bound.Available = false;
        root.Tick(0.016, 2);
        bound.Available = true;
        root.Tick(0.016, 3);
        Assert.False(window.Frame.Visible);   // the binding does not reopen a window the player closed

        handle.Show();
        root.Tick(0.016, 4);
        Assert.True(window.Frame.Visible);
    }

    [Fact]
    public void TheDockLeavesItsMinimizeOffAWindowWithABar()
    {
        var root = new UiRoot { Width = 1280, Height = 720 };
        var settings = new PluginUiThemeSettings();
        using var shelf = new PluginSidePanel(root.WindowManager, _ => (0u, 0, 0), null, settings);
        root.AddChild(shelf);

        MarkupWindow bar = MarkupDocument.BuildWindow(
            "<panel x=\"100\" y=\"100\" w=\"200\" h=\"100\" titlebar=\"true\" />", new object(), _ => (0u, 0, 0));
        root.AddChild(bar.Frame);
        RetailWindowHandle barHandle = bar.Register(root.WindowManager, "plugin:a:main",
            new PluginWindowVisibilityController(null, true));
        shelf.Add(new PluginUiOwner("a", "A"), new PluginPanelDescriptor("main", "A"), barHandle);

        UiNineSlicePanel plain = MarkupDocument.Build(
            "<panel x=\"100\" y=\"300\" w=\"200\" h=\"100\" />", new object(), _ => (0u, 0, 0));
        root.AddChild(plain);
        RetailWindowHandle plainHandle = root.WindowManager.Register("plugin:b:main", plain);
        shelf.Add(new PluginUiOwner("b", "B"), new PluginPanelDescriptor("main", "B"), plainHandle);

        Assert.DoesNotContain(bar.Frame.Children, c => c is UiSimpleButton { Text: "–" });
        Assert.Equal(PluginWindowChrome.DockedCloseTooltip, bar.TitleBar!.Close.GetTooltipText());
        Assert.Contains(plain.Children, c => c is UiSimpleButton { Text: "–" });

        root.WindowManager.Unregister("plugin:a:main");
        root.WindowManager.Unregister("plugin:b:main");
    }

    [Fact]
    public void TheDockLeavesItsMinimizeOffALayoutWindowWithoutABar()
    {
        var root = new UiRoot { Width = 1280, Height = 720 };
        using var shelf = new PluginSidePanel(root.WindowManager, _ => (0u, 0, 0), null, new PluginUiThemeSettings());
        root.AddChild(shelf);

        MarkupWindow bare = MarkupDocument.BuildWindow(
            "<panel x=\"100\" y=\"100\" layout=\"column\" titlebar=\"false\"><button text=\"OK\" /></panel>",
            new object(), _ => (0u, 0, 0));
        root.AddChild(bare.Frame);
        RetailWindowHandle handle = bare.Register(root.WindowManager, "plugin:c:main",
            new PluginWindowVisibilityController(null, true));
        shelf.Add(new PluginUiOwner("c", "C"), new PluginPanelDescriptor("main", "C"), handle);

        Assert.DoesNotContain(bare.Frame.Children, c => c is UiSimpleButton { Text: "–" });

        root.WindowManager.Unregister("plugin:c:main");
    }

    [Fact]
    public void ASavedSizeSurvivesTheSameBarMarkup()
    {
        const string xml = "<panel x=\"0\" y=\"0\" w=\"300\" h=\"200\" resizable=\"true\" titlebar=\"true\" />";
        var store = new SettingsStore(PathName);
        MarkupWindow first = MarkupDocument.BuildWindow(xml, new object(), _ => (1u, 32, 32));
        var root = new UiRoot { Width = 1280, Height = 720 };
        root.AddChild(first.Frame);
        RetailWindowHandle handle = first.Register(root.WindowManager, "w", new PluginWindowVisibilityController(null, true));
        using (new RetailWindowLayoutPersistence(root.WindowManager, store, () => "Alice", () => (1280, 720)))
        {
            handle.MoveTo(50f, 50f);
            handle.ResizeTo(500f, 400f);
        }

        MarkupWindow fresh = MarkupDocument.BuildWindow(xml, new object(), _ => (1u, 32, 32));
        var freshRoot = new UiRoot { Width = 1280, Height = 720 };
        freshRoot.AddChild(fresh.Frame);
        fresh.Register(freshRoot.WindowManager, "w", new PluginWindowVisibilityController(null, true));
        using var persistence = new RetailWindowLayoutPersistence(freshRoot.WindowManager, store, () => "Alice", () => (1280, 720));
        persistence.RestoreAll();
        Assert.Equal((500f, 400f), (fresh.Frame.Width, fresh.Frame.Height));
    }

    [Fact]
    public void AdoptingTheBarResetsTheSavedSizeButKeepsThePosition()
    {
        var store = new SettingsStore(PathName);
        MarkupWindow old = MarkupDocument.BuildWindow(
            "<panel x=\"0\" y=\"0\" w=\"300\" h=\"200\" resizable=\"true\" />", new object(), _ => (1u, 32, 32));
        var root = new UiRoot { Width = 1280, Height = 720 };
        root.AddChild(old.Frame);
        RetailWindowHandle handle = old.Register(root.WindowManager, "w", new PluginWindowVisibilityController(null, true));
        using (new RetailWindowLayoutPersistence(root.WindowManager, store, () => "Alice", () => (1280, 720)))
        {
            handle.MoveTo(120f, 90f);
            handle.ResizeTo(500f, 400f);
        }

        MarkupWindow bar = MarkupDocument.BuildWindow(
            "<panel x=\"0\" y=\"0\" w=\"300\" h=\"200\" resizable=\"true\" titlebar=\"true\" />",
            new object(), _ => (1u, 32, 32));
        var freshRoot = new UiRoot { Width = 1280, Height = 720 };
        freshRoot.AddChild(bar.Frame);
        bar.Register(freshRoot.WindowManager, "w", new PluginWindowVisibilityController(null, true));
        using var persistence = new RetailWindowLayoutPersistence(freshRoot.WindowManager, store, () => "Alice", () => (1280, 720));
        persistence.RestoreAll();
        Assert.Equal((310f, 229f), (bar.Frame.Width, bar.Frame.Height));
        Assert.Equal((120f, 90f), (bar.Frame.Left, bar.Frame.Top));
    }
}
