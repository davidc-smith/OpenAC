using AcDream.App.Plugins;
using AcDream.App.UI;
using AcDream.Plugin.Abstractions;

namespace AcDream.App.Tests.Plugins;

/// <summary>
/// A plugin's dialogs name its own mounted windows as owners, are checked
/// before they are queued, and reach the bound dialog host.
/// </summary>
public sealed class BufferedUiRegistryDialogTests
{
    private static readonly PluginUiOwner Owner = new("acdream.test", "Test");

    private sealed class Fixture
    {
        public BufferedUiRegistry Registry { get; } = new();
        public UiRoot Root { get; } = new() { Width = 1280f, Height = 720f };
        public PluginDialogHost Host { get; }

        public Fixture(bool bind = true)
        {
            Host = new PluginDialogHost(Root, static () => null, font: null);
            if (bind)
            {
                Registry.BindPluginWindowControl(Root.ShowWindow, Root.HideWindow);
                Registry.BindPluginDialogs(Host);
            }
        }

        public UiPanel Mount(PluginUiOwner owner, string windowId, string title, bool startVisible = true)
        {
            Registry.RegisterPanelContent(owner, new PluginPanelDescriptor(windowId, title), "<panel />", new object());
            BufferedUiRegistry.Pending pending = Assert.Single(Registry.Drain());
            var frame = new UiPanel { Left = 100f, Top = 100f, Width = 200f, Height = 100f };
            var visibility = new PluginWindowVisibilityController(null, startVisible);
            frame.VisibleSource = visibility.ShouldBeVisible;
            frame.Visible = visibility.ShouldBeVisible();
            Root.AddChild(frame);
            Registry.CompleteMount(pending, Root, frame);
            Root.WindowManager.Register(pending.WindowName, frame, frame, visibility);
            Registry.CompleteWindowMount(pending, () => Root.WindowManager.Unregister(pending.WindowName));
            return frame;
        }
    }

    [Fact]
    public void WithoutADialogHostNothingIsQueued()
    {
        var f = new Fixture(bind: false);

        Assert.Null(f.Registry.Confirm(Owner, new PluginDialog("T", "M"), _ => { }));
        Assert.Null(f.Registry.Confirm(new PluginDialog("T", "M"), _ => { }));
    }

    [Fact]
    public void TheOwnerIsThePluginsMountedWindowByIdOrTitle()
    {
        var f = new Fixture();
        UiPanel main = f.Mount(Owner, "main", "Main");

        Assert.NotNull(f.Registry.Confirm(Owner, new PluginDialog("T", "M", OwnerView: "main"), _ => { }));
        Assert.Equal(1, main.ModalBlockCount);
        UiPluginDialog view = f.Host.ShownFor(main)!;
        Assert.Equal(MathF.Round(200f - view.Width * 0.5f), view.Left);

        f.Registry.Confirm(Owner, new PluginDialog("T", "M", OwnerView: "Main"), _ => { });
        Assert.Equal(2, f.Host.PendingCount);
    }

    [Fact]
    public void AnUnknownOrForeignOwnerBlocksNothing()
    {
        var f = new Fixture();
        UiPanel main = f.Mount(Owner, "main", "Main");

        Assert.NotNull(f.Registry.Confirm(Owner, new PluginDialog("T", "M", OwnerView: "missing"), _ => { }));
        Assert.NotNull(f.Registry.Confirm(
            new PluginUiOwner("another", "Other"), new PluginDialog("T", "M", OwnerView: "main"), _ => { }));

        Assert.Equal(0, main.ModalBlockCount);
        Assert.NotNull(f.Host.ShownFor(null));
    }

    [Fact]
    public void ADialogWithoutButtonsShowsOkAndCancel()
    {
        var f = new Fixture();

        f.Registry.Confirm(Owner, new PluginDialog("T", "M"), _ => { });

        var view = Assert.IsType<UiDrawnPluginDialog>(f.Host.ShownFor(null));
        Assert.Equal(["OK", "Cancel"], view.Buttons.Select(b => b.Text));
    }

    [Fact]
    public void BadButtonsAreRefusedEvenWithoutAHost()
    {
        foreach (bool bind in new[] { true, false })
        {
            var f = new Fixture(bind);
            Assert.Throws<ArgumentException>(() => f.Registry.Confirm(Owner, new PluginDialog("T", "M", []), _ => { }));
            Assert.Throws<ArgumentException>(() => f.Registry.Confirm(
                Owner, new PluginDialog("T", "M", ["1", "2", "3", "4", "5"]), _ => { }));
            Assert.Throws<ArgumentException>(() => f.Registry.Confirm(Owner, new PluginDialog("T", "M", ["OK", " "]), _ => { }));
        }
    }

    [Fact]
    public void DisposingTheHandleAnswersTheLastButton()
    {
        var f = new Fixture();
        var answers = new List<int>();

        f.Registry.Confirm(Owner, new PluginDialog("T", "M", ["A", "B", "C"]), answers.Add)!.Dispose();

        Assert.Equal([2], answers);
    }

    [Fact]
    public void ShowModalShowsTheWindowAndBlocksItsOwnerUntilItHides()
    {
        var f = new Fixture();
        UiPanel main = f.Mount(Owner, "main", "Main");
        UiPanel options = f.Mount(Owner, "options", "Options", startVisible: false);

        Assert.True(f.Registry.ShowModal(Owner, "options", "main"));
        Assert.True(options.Visible);
        Assert.Equal(1, main.ModalBlockCount);

        Assert.True(f.Registry.HidePanel(Owner, "options"));
        f.Root.Tick(0.016d, 16L);
        f.Host.Tick();
        Assert.Equal(0, main.ModalBlockCount);
    }

    [Fact]
    public void ShowModalWithoutAnOwnerShowsButBlocksNothing()
    {
        var f = new Fixture();
        UiPanel main = f.Mount(Owner, "main", "Main");
        UiPanel options = f.Mount(Owner, "options", "Options", startVisible: false);

        Assert.True(f.Registry.ShowModal(Owner, "options"));

        Assert.True(options.Visible);
        Assert.Equal(0, main.ModalBlockCount);
    }

    [Fact]
    public void ShowModalOfAnUnknownWindowOrWithoutAHostIsFalse()
    {
        var f = new Fixture();
        f.Mount(Owner, "main", "Main");
        f.Mount(Owner, "options", "Options", startVisible: false);
        Assert.False(f.Registry.ShowModal(Owner, "missing", "main"));

        f.Registry.UnbindClientWindowControl();
        Assert.False(f.Registry.ShowModal(Owner, "options", "main"));
        Assert.Null(f.Registry.Confirm(Owner, new PluginDialog("T", "M"), _ => { }));

        var unbound = new Fixture(bind: false);
        unbound.Mount(Owner, "options", "Options", startVisible: false);
        Assert.False(unbound.Registry.ShowModal(Owner, "options"));
    }
}
