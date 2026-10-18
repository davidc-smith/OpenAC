using AcDream.App.UI;

namespace AcDream.App.Tests.UI;

public sealed class PluginAppearanceBindingTests
{
    [Fact]
    public void TheDockMenuShowsAndSetsTheDockMode()
    {
        var settings = new PluginUiThemeSettings();
        var binding = new PluginAppearanceBinding(settings);

        Assert.Equal(["Floating", "Left edge", "Right edge"], binding.Docks);
        Assert.Equal("Floating", binding.SelectedDock);

        binding.SelectDock("Right edge");
        Assert.Equal(PluginDockMode.Right, settings.Dock);
        Assert.Equal("Right edge", binding.SelectedDock);

        settings.Dock = PluginDockMode.Left;   // a snap while dragging
        Assert.Equal("Left edge", binding.SelectedDock);

        binding.SelectDock("Sideways");
        Assert.Equal(PluginDockMode.Left, settings.Dock);
    }

    [Fact]
    public void TheThemeMenuStillWorks()
    {
        var settings = new PluginUiThemeSettings();
        var binding = new PluginAppearanceBinding(settings);
        binding.Select("Warm graphite + brass");
        Assert.Equal(PluginUiTheme.Brass, settings.Theme);
        Assert.Equal("Warm graphite + brass", binding.Selected);
    }
}
