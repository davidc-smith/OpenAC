using System.Numerics;
using AcDream.App.UI;
using AcDream.Plugin.Abstractions;

namespace AcDream.App.Tests.UI;

public sealed class PluginSidePanelThemeTests
{
    [Theory]
    [InlineData(PluginUiTheme.Classic)]
    [InlineData(PluginUiTheme.Moss)]
    [InlineData(PluginUiTheme.Brass)]
    public void EveryThemeHasTheSameDockShape_AndScrollsEveryEntryIntoView(PluginUiTheme theme)
    {
        var root = new UiRoot { Width = 800, Height = 260 };
        var settings = new PluginUiThemeSettings { Theme = theme };
        using var shelf = new PluginSidePanel(root.WindowManager, _ => (0u, 0, 0), null, settings);
        root.AddChild(shelf);
        for (int i = 0; i < 12; i++) Add(root, shelf, i);
        root.Tick(0.016, 16);
        Assert.Equal(PluginUiStyle.DockWidth, shelf.Width);
        Assert.True(shelf.Top + shelf.Height <= root.Height);
        var buttons = shelf.Children.OfType<PluginSidePanel.PluginShelfButton>().ToArray();
        Assert.True(buttons[0].Visible);
        Assert.False(buttons[^1].Visible);
        for (int i = 0; i < 20; i++) shelf.OnEvent(new UiEvent { Type = UiEventType.Scroll, Data0 = -1 });
        Assert.True(buttons[^1].Visible);
        Assert.False(buttons[0].Visible);
        Assert.All(buttons.Where(b => b.Visible), b =>
        {
            Assert.Equal(PluginUiStyle.DockPadding, b.Left);
            Assert.Equal(PluginUiStyle.DockSlot, b.Width);
            Assert.True(b.Top >= PluginUiStyle.DockSlotsTop);
            Assert.True(b.Top + b.Height <= shelf.Height);
        });
    }

    [Theory]
    [InlineData(PluginUiTheme.Classic)]
    [InlineData(PluginUiTheme.Moss)]
    [InlineData(PluginUiTheme.Brass)]
    public void RightClickOnEntryOpensAppearanceWithoutTogglingPlugin(PluginUiTheme theme)
    {
        var root = new UiRoot { Width = 800, Height = 600 };
        var settings = new PluginUiThemeSettings { Theme = theme };
        int requests = 0;
        using var shelf = new PluginSidePanel(root.WindowManager, _ => (0u, 0, 0), null, settings,
            () => requests++);
        var handle = Add(root, shelf, 0);
        root.AddChild(shelf);
        root.Tick(0.016, 16);
        var button = Assert.Single(shelf.Children.OfType<PluginSidePanel.PluginShelfButton>());
        int x = (int)(shelf.Left + button.Left + button.Width / 2);
        int y = (int)(shelf.Top + button.Top + button.Height / 2);
        bool visible = handle.IsVisible;
        root.OnMouseDown(UiMouseButton.Right, x, y, 0);
        root.OnMouseUp(UiMouseButton.Right, x, y, 0);
        Assert.Equal(1, requests);
        Assert.Equal(visible, handle.IsVisible);
    }

    [Fact]
    public void TheGearOpensAppearance()
    {
        var root = new UiRoot { Width = 800, Height = 600 };
        int requests = 0;
        using var shelf = new PluginSidePanel(root.WindowManager, _ => (0u, 0, 0), null,
            new PluginUiThemeSettings(), () => requests++);
        Add(root, shelf, 0);
        root.AddChild(shelf);
        root.Tick(0.016, 16);
        DockRect gear = shelf.Layout.Gear;
        int x = (int)(shelf.Left + gear.X + gear.W / 2);
        int y = (int)(shelf.Top + gear.Y + gear.H / 2);
        root.OnMouseDown(UiMouseButton.Left, x, y, 0);
        root.OnMouseUp(UiMouseButton.Left, x, y, 0);
        Assert.Equal(1, requests);
    }

    private static RetailWindowHandle AddThemed(UiRoot root, PluginSidePanel shelf, PluginUiThemeSettings settings, int index)
    {
        var frame = MarkupDocument.Build(
            "<panel x=\"100\" y=\"100\" w=\"200\" h=\"100\" title=\"T\" theme=\"plugin\" />",
            new object(), _ => (0u, 0, 0), themes: settings);
        root.AddChild(frame);
        var handle = root.WindowManager.Register($"plugin:themed:{index}", frame);
        shelf.Add(new PluginUiOwner($"themed.{index}", $"Themed {index}"),
            new PluginPanelDescriptor("main", $"Themed {index}") { IconText = "TT" }, handle);
        return handle;
    }

    [Fact]
    public void TheDockDrawsInTheDockPalette_AndMinimizeFollowsTheWindowsTheme()
    {
        var root = new UiRoot { Width = 800, Height = 600 };
        var settings = new PluginUiThemeSettings { Theme = PluginUiTheme.Moss };
        using var shelf = new PluginSidePanel(root.WindowManager, _ => (0u, 0, 0), null, settings);
        root.AddChild(shelf);
        var themed = AddThemed(root, shelf, settings, 0);
        var plain = Add(root, shelf, 1);
        root.Tick(0.016, 16);

        var themedMin = themed.OuterFrame.Children.OfType<UiSimpleButton>().Single(b => b.Text == "–");
        var plainMin = plain.OuterFrame.Children.OfType<UiSimpleButton>().Single(b => b.Text == "–");
        Assert.Same(settings.Palette, themedMin.ThemePalette);
        Assert.Equal(settings.Palette!.Muted, themedMin.TextColor);
        Assert.Null(plainMin.ThemePalette);
        Assert.True(plainMin.Outline);

        var (renderer, ctx) = ThemeDrawCapture.Context(800, 600);
        shelf.DrawSelfAndChildren(ctx);
        var moss = ThemeDrawCapture.Vertices(renderer);
        Assert.True(ThemeDrawCapture.HasColor(moss, PluginUiPalette.Moss.Background));
        Assert.Contains(moss, v => v.Position.Y > shelf.Top + shelf.Height);   // the shadow

        settings.Theme = PluginUiTheme.Classic;
        root.Tick(0.016, 32);
        (renderer, ctx) = ThemeDrawCapture.Context(800, 600);
        shelf.DrawSelfAndChildren(ctx);
        Assert.True(ThemeDrawCapture.HasColor(ThemeDrawCapture.Vertices(renderer), PluginUiStyle.Mix(PluginUiPalette.ClassicDock.Border,
                PluginUiPalette.ClassicDock.Background, PluginUiStyle.DockDividerFade),
            tolerance: 0.05f));
        Assert.Null(themedMin.ThemePalette);
        Assert.Equal(Vector4.One, themedMin.TextColor);
    }

    [Fact]
    public void AnOpenWindowGetsAnAccentDot_AndAClosedOneDoesNot()
    {
        var root = new UiRoot { Width = 800, Height = 600 };
        var settings = new PluginUiThemeSettings { Theme = PluginUiTheme.Moss };
        using var shelf = new PluginSidePanel(root.WindowManager, _ => (0u, 0, 0), null, settings);
        root.AddChild(shelf);
        var handle = Add(root, shelf, 0);
        root.Tick(0.016, 16);

        handle.Show();
        var (renderer, ctx) = ThemeDrawCapture.Context(800, 600);
        shelf.DrawSelfAndChildren(ctx);
        Assert.Contains(ThemeDrawCapture.Vertices(renderer), v => IsDot(v, shelf));

        handle.Hide();
        (renderer, ctx) = ThemeDrawCapture.Context(800, 600);
        shelf.DrawSelfAndChildren(ctx);
        Assert.DoesNotContain(ThemeDrawCapture.Vertices(renderer), v => IsDot(v, shelf));
    }

    [Fact]
    public void ARightHandDockPutsTheAccentDotOnItsRightEdge()
    {
        var root = new UiRoot { Width = 800, Height = 600 };
        var settings = new PluginUiThemeSettings { Theme = PluginUiTheme.Moss };
        using var shelf = new PluginSidePanel(root.WindowManager, _ => (0u, 0, 0), null, settings);
        root.AddChild(shelf);
        var handle = Add(root, shelf, 0);
        root.Tick(0.016, 16);
        shelf.Left = 700f;
        handle.Show();

        var (renderer, ctx) = ThemeDrawCapture.Context(800, 600);
        shelf.DrawSelfAndChildren(ctx);
        var verts = ThemeDrawCapture.Vertices(renderer);
        Assert.Contains(verts, v => IsAccent(v) && v.Position.X > shelf.Left + shelf.Width - PluginUiStyle.DockPadding);
        Assert.DoesNotContain(verts, v => IsAccent(v) && v.Position.X < shelf.Left + PluginUiStyle.DockPadding);
    }

    private static bool IsAccent((Vector2 Position, Vector4 Color) v) =>
        v.Color.W > 0.5f
        && MathF.Abs(v.Color.X - PluginUiPalette.Moss.Accent.X) < 0.01f
        && MathF.Abs(v.Color.Y - PluginUiPalette.Moss.Accent.Y) < 0.01f;

    private static bool IsDot((Vector2 Position, Vector4 Color) v, PluginSidePanel shelf) =>
        v.Position.X < shelf.Left + PluginUiStyle.DockPadding
        && v.Color.W > 0.5f
        && MathF.Abs(v.Color.X - PluginUiPalette.Moss.Accent.X) < 0.01f
        && MathF.Abs(v.Color.Y - PluginUiPalette.Moss.Accent.Y) < 0.01f;

    private static RetailWindowHandle Add(UiRoot root, PluginSidePanel shelf, int index)
    {
        var frame = new UiPanel { Left = 100, Width = 200, Height = 100 };
        root.AddChild(frame);
        var handle = root.WindowManager.Register($"plugin:test:{index}", frame);
        shelf.Add(new PluginUiOwner($"test.{index}", $"Plugin {index}"),
            new PluginPanelDescriptor("main", $"Plugin {index}") { IconText = "TP" }, handle);
        return handle;
    }
}
