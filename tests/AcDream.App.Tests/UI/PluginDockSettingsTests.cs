using AcDream.App.UI;
using AcDream.UI.Abstractions.Panels.Settings;

namespace AcDream.App.Tests.UI;

public sealed class PluginDockSettingsTests : IDisposable
{
    private readonly string _path = Path.Combine(
        Path.GetTempPath(), $"acdream-dock-settings-{Guid.NewGuid():N}.json");

    public void Dispose()
    {
        if (File.Exists(_path)) File.Delete(_path);
    }

    [Fact]
    public void DockDefaultsToFloating_AndPersistsBesideTheTheme()
    {
        var store = new SettingsStore(_path);
        var settings = new PluginUiThemeSettings(store);
        Assert.Equal(PluginDockMode.Floating, settings.Dock);

        settings.Theme = PluginUiTheme.Moss;
        settings.Dock = PluginDockMode.Right;
        Assert.Equal(new PluginUiSettings("Moss", "right"), store.LoadPluginUi());

        settings.Theme = PluginUiTheme.Brass;
        Assert.Equal(new PluginUiSettings("Brass", "right"), store.LoadPluginUi());

        var reloaded = new PluginUiThemeSettings(store);
        Assert.Equal(PluginUiTheme.Brass, reloaded.Theme);
        Assert.Equal(PluginDockMode.Right, reloaded.Dock);
    }

    [Theory]
    [InlineData("left", PluginDockMode.Left)]
    [InlineData("RIGHT", PluginDockMode.Right)]
    [InlineData("floating", PluginDockMode.Floating)]
    [InlineData("top", PluginDockMode.Floating)]
    [InlineData("", PluginDockMode.Floating)]
    public void UnknownDockValuesLoadAsFloating(string stored, PluginDockMode expected)
    {
        new SettingsStore(_path).SavePluginUi(new PluginUiSettings("Classic", stored));
        Assert.Equal(expected, new PluginUiThemeSettings(new SettingsStore(_path)).Dock);
    }

    [Fact]
    public void DockPaletteIsTheThemePaletteOrClassicDock_AndClassicWindowsStayUnthemed()
    {
        var settings = new PluginUiThemeSettings();
        Assert.Null(settings.Palette);
        Assert.Same(PluginUiPalette.ClassicDock, settings.DockPalette);

        settings.Theme = PluginUiTheme.Moss;
        Assert.Same(PluginUiPalette.Moss, settings.DockPalette);
        settings.Theme = PluginUiTheme.Brass;
        Assert.Same(PluginUiPalette.Brass, settings.DockPalette);
    }

    [Fact]
    public void ClassicDockCarriesTodaysClassicDockColours()
    {
        PluginUiPalette p = PluginUiPalette.ClassicDock;
        Assert.Equal(0.88f, p.Background.W, 3);
        Assert.Equal(0f, p.Background.X);
        Assert.Equal(0x9E / 255f, p.Border.X, 3);
        Assert.Equal(0xDB / 255f, p.Accent.X, 3);
        Assert.Equal(0x30 / 255f, p.Selected.Y, 3);
    }
}
