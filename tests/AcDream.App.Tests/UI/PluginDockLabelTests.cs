using AcDream.App.UI;
using AcDream.Plugin.Abstractions;

namespace AcDream.App.Tests.UI;

public sealed class PluginDockLabelTests
{
    private static (UiRoot Root, PluginSidePanel Dock) Mount(PluginUiThemeSettings settings, string title, string owner)
    {
        var root = new UiRoot { Width = 800f, Height = 600f };
        var dock = new PluginSidePanel(root.WindowManager, _ => (0u, 0, 0), font: null, settings);
        root.AddChild(dock);
        root.WindowManager.Register(WindowNames.PluginShelf, dock, dock, controller: dock);
        var frame = new UiPanel { Left = 300f, Width = 200f, Height = 100f };
        root.AddChild(frame);
        RetailWindowHandle handle = root.WindowManager.Register("plugin:test:main", frame);
        dock.Add(new PluginUiOwner("test", owner), new PluginPanelDescriptor("main", title), handle);
        root.Tick(0.016d, 16L);
        return (root, dock);
    }

    private static PluginSidePanel.PluginShelfButton Slot(PluginSidePanel dock) =>
        Assert.Single(dock.Children.OfType<PluginSidePanel.PluginShelfButton>());

    private static void HoverSlot(UiRoot root, PluginSidePanel dock)
    {
        var slot = Slot(dock);
        root.OnMouseMove((int)(dock.Left + slot.Left + 10), (int)(dock.Top + slot.Top + 10));
        root.Tick(0.016d, 32L);
    }

    [Fact]
    public void HoveringASlot_ShowsItsLabel_AndTheSlotHasNoTooltip()
    {
        var (root, dock) = Mount(new PluginUiThemeSettings(), "Loot Editor", "MossTank");
        Assert.Null(dock.HoveredSlot);

        HoverSlot(root, dock);

        Assert.Same(Slot(dock), dock.HoveredSlot);
        Assert.Equal(("Loot Editor", "MossTank"), PluginSidePanel.LabelText(Slot(dock)));
        Assert.Null(Slot(dock).GetTooltipText());
    }

    [Fact]
    public void ALabelIsOneLine_WhenTheTitleIsThePluginsName()
    {
        var (_, dock) = Mount(new PluginUiThemeSettings(), "GoArrow", "GoArrow");
        Assert.Equal(("GoArrow", (string?)null), PluginSidePanel.LabelText(Slot(dock)));
    }

    [Fact]
    public void TheLabelSitsAwayFromTheScreenEdge()
    {
        var settings = new PluginUiThemeSettings();
        var (root, dock) = Mount(settings, "Loot Editor", "MossTank");
        DockRect floatingLeft = dock.LabelRect(Slot(dock));
        Assert.Equal(dock.Width + PluginUiStyle.LabelGap, floatingLeft.X);
        Assert.Equal(Slot(dock).Top + Slot(dock).Height / 2f, floatingLeft.Y + floatingLeft.H / 2f, 0);

        settings.Dock = PluginDockMode.Right;
        root.Tick(0.016d, 32L);
        DockRect right = dock.LabelRect(Slot(dock));
        Assert.Equal(-PluginUiStyle.LabelGap - right.W, right.X);

        settings.Dock = PluginDockMode.Floating;
        root.Tick(0.016d, 48L);
        Assert.True(dock.Left > 400f);   // floating in the right half of the screen
        Assert.True(dock.LabelRect(Slot(dock)).X < 0f);
    }

    [Fact]
    public void TheLabelIsNotHitTestable_AndIsDrawnOutsideTheDock()
    {
        var (root, dock) = Mount(new PluginUiThemeSettings { Theme = PluginUiTheme.Moss }, "Loot Editor", "MossTank");
        HoverSlot(root, dock);
        DockRect label = dock.LabelRect(Slot(dock));
        int x = (int)(dock.Left + label.X + label.W / 2f), y = (int)(dock.Top + label.Y + label.H / 2f);

        Assert.Null(root.Pick(x, y));
        // The label is drawn in the overlay pass; draw that pass alone so its vertices are recorded.
        var (renderer, ctx) = ThemeDrawCapture.Context(root.Width, root.Height);
        dock.DrawOverlays(ctx);
        var vertices = ThemeDrawCapture.Vertices(renderer);
        Assert.Contains(vertices, v => v.Position.X > dock.Left + dock.Width + PluginUiStyle.LabelGap
            && ThemeDrawCapture.HasColor([v], PluginUiPalette.Moss.Background));
    }

    [Fact]
    public void HoveringTheGear_LabelsItPluginAppearance()
    {
        var (root, dock) = Mount(new PluginUiThemeSettings(), "Loot Editor", "MossTank");
        DockRect gear = dock.Layout.Gear;
        root.OnMouseMove((int)(dock.Left + gear.X + 10), (int)(dock.Top + gear.Y + 10));
        root.Tick(0.016d, 32L);

        Assert.NotNull(dock.HoveredSlot);
        Assert.Equal(("Plugin appearance", (string?)null), PluginSidePanel.LabelText(dock.HoveredSlot!));
    }
}
