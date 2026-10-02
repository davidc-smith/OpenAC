using System.Numerics;
using AcDream.App.Rendering;
using AcDream.App.UI;
using AcDream.Plugin.Abstractions;

namespace AcDream.App.Tests.UI;

public sealed class PluginDockChromeTests
{
    private static (UiRoot Root, PluginSidePanel Dock, List<RetailWindowHandle> Windows) Mount(
        int count, float height = 600f, PluginUiThemeSettings? settings = null,
        (uint, int, int)? fileIcon = null)
    {
        var root = new UiRoot { Width = 800f, Height = height };
        var dock = new PluginSidePanel(root.WindowManager, _ => (0u, 0, 0), font: null, settings);
        root.AddChild(dock);
        root.WindowManager.Register(WindowNames.PluginShelf, dock, dock, controller: dock);
        var windows = new List<RetailWindowHandle>();
        for (int i = 0; i < count; i++)
        {
            var frame = new UiPanel { Left = 300f, Width = 200f, Height = 100f };
            root.AddChild(frame);
            RetailWindowHandle handle = root.WindowManager.Register($"plugin:test:{i}", frame);
            dock.Add(new PluginUiOwner($"test.{i}", $"Plugin {i}"),
                new PluginPanelDescriptor("main", $"Plugin {i}"), handle, fileIcon);
            windows.Add(handle);
        }
        root.Tick(0.016d, 16L);
        return (root, dock, windows);
    }

    private static UiElement Grip(PluginSidePanel dock) =>
        Assert.Single(dock.Children, c => c.WindowMoveHandle);

    private static UiSimpleButton Toggle(PluginSidePanel dock) =>
        (UiSimpleButton)Assert.Single(dock.Children, c => c.GetTooltipText() is "Collapse the dock" or "Expand the dock");

    [Fact]
    public void HandleAndToggle_FollowTheLayout_ExpandedAndCollapsed_AfterADraw()
    {
        var (root, dock, _) = Mount(2);
        ThemeDrawCapture.Draw(root);

        Assert.Equal((0f, 0f, 30f, 16f), (Grip(dock).Left, Grip(dock).Top, Grip(dock).Width, Grip(dock).Height));
        Assert.Equal((30f, 2f, 12f, 12f), (Toggle(dock).Left, Toggle(dock).Top, Toggle(dock).Width, Toggle(dock).Height));

        int x = (int)(dock.Left + 36f), y = (int)(dock.Top + 8f);
        root.OnMouseDown(UiMouseButton.Left, x, y);
        root.OnMouseUp(UiMouseButton.Left, x, y);
        ThemeDrawCapture.Draw(root);

        Assert.True(dock.CaptureWindowState().Collapsed);
        Assert.Equal((48f, 24f), (dock.Width, dock.Height));
        Assert.Equal((0f, 0f, 30f, 24f), (Grip(dock).Left, Grip(dock).Top, Grip(dock).Width, Grip(dock).Height));
        Assert.Equal((30f, 0f, 12f, 24f), (Toggle(dock).Left, Toggle(dock).Top, Toggle(dock).Width, Toggle(dock).Height));
        Assert.Equal("Expand the dock", Toggle(dock).GetTooltipText());

        root.OnMouseDown(UiMouseButton.Left, x, y);
        root.OnMouseUp(UiMouseButton.Left, x, y);
        Assert.False(dock.CaptureWindowState().Collapsed);
    }

    [Fact]
    public void AfterScrollingAndADraw_EveryVisibleSlotMatchesAFreshLayout()
    {
        var (root, dock, _) = Mount(12, height: 300f);
        ThemeDrawCapture.Draw(root);
        dock.OnEvent(new UiEvent { Type = UiEventType.Scroll, Data0 = -1 });
        dock.OnEvent(new UiEvent { Type = UiEventType.Scroll, Data0 = -1 });
        ThemeDrawCapture.Draw(root);

        string[] owners = Enumerable.Range(0, 12).Select(i => $"test.{i}").ToArray();
        PluginDockLayout fresh = PluginDockLayout.Compute(
            PluginDockMode.Floating, false, owners, 300f - dock.Top - 8f, 2);
        var buttons = dock.Children.OfType<PluginSidePanel.PluginShelfButton>().ToArray();
        for (int i = 0; i < buttons.Length; i++)
        {
            Assert.Equal(fresh.Slots[i] is not null, buttons[i].Visible);
            if (fresh.Slots[i] is { } slot)
                Assert.Equal((slot.X, slot.Y), (buttons[i].Left, buttons[i].Top));
        }
        Assert.Equal(fresh.Height, dock.Height);
    }

    [Fact]
    public void HandleDotsAndToggle_AreDrawnOnlyWhileThePointerIsOverTheDock()
    {
        var (root, dock, _) = Mount(1, settings: new PluginUiThemeSettings { Theme = PluginUiTheme.Moss });
        root.OnMouseMove(700, 500);
        root.Tick(0.016d, 32L);
        int away = ThemeDrawCapture.Draw(root).Length;

        root.OnMouseMove((int)dock.Left + 20, (int)dock.Top + 40);
        root.Tick(0.016d, 48L);
        Assert.True(dock.PointerOver);
        int over = ThemeDrawCapture.Draw(root).Length;

        Assert.True(over > away, $"hovered draw ({over} floats) is not larger than the idle one ({away})");
    }

    [Fact]
    public void AnIconlessPlugin_GetsItsMonogramHue()
    {
        var (_, dock, _) = Mount(1, settings: new PluginUiThemeSettings { Theme = PluginUiTheme.Brass });
        var (renderer, ctx) = ThemeDrawCapture.Context(800, 600);
        dock.DrawSelfAndChildren(ctx);

        Vector4 hue = PluginUiStyle.MonogramHue("test.0");
        Assert.True(ThemeDrawCapture.HasColor(ThemeDrawCapture.Vertices(renderer), hue));
    }

    [Theory]
    [InlineData("ABC", "Loot Editor", "AB")]
    [InlineData(null, "Loot Editor", "LE")]
    [InlineData(null, "Golem", "GO")]
    public void MonogramLetters_AreTwoAtMost_InEveryTheme(string? iconText, string title, string expected)
    {
        foreach (PluginUiTheme theme in Enum.GetValues<PluginUiTheme>())
        {
            var root = new UiRoot { Width = 800f, Height = 600f };
            using var dock = new PluginSidePanel(root.WindowManager, _ => (0u, 0, 0), font: null,
                new PluginUiThemeSettings { Theme = theme });
            root.AddChild(dock);
            var frame = new UiPanel { Width = 200f, Height = 100f };
            root.AddChild(frame);
            dock.Add(new PluginUiOwner("test", "Test"),
                new PluginPanelDescriptor("main", title) { IconText = iconText },
                root.WindowManager.Register("plugin:test:main", frame));

            Assert.Equal(expected, Assert.Single(dock.Children.OfType<PluginSidePanel.PluginShelfButton>()).Text);
        }
    }

    [Fact]
    public void ColourArt_IsDimmerWhileItsWindowIsClosed()
    {
        var (_, dock, windows) = Mount(1, fileIcon: (7u, 64, 64));
        PluginSidePanel.PluginShelfButton button =
            Assert.Single(dock.Children.OfType<PluginSidePanel.PluginShelfButton>());

        windows[0].Hide();
        Assert.Equal(PluginUiStyle.ClosedArtAlpha, IconAlpha(button), 3);
        windows[0].Show();
        Assert.Equal(1f, IconAlpha(button), 3);
    }

    private static float IconAlpha(PluginSidePanel.PluginShelfButton button)
    {
        var (renderer, ctx) = ThemeDrawCapture.Context(200, 200);
        button.DrawSelfAndChildren(ctx);
        var icon = Assert.Single(renderer.DebugSpriteSegmentVerts, s => s.Texture == 7u);
        return icon.Verts[TextRenderer.FloatsPerVertex - 1];
    }
}
