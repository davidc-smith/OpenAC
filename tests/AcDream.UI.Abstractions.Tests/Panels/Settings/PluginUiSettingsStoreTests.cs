using System.IO;
using AcDream.UI.Abstractions.Panels.Settings;

namespace AcDream.UI.Abstractions.Tests.Panels.Settings;

public sealed class PluginUiSettingsStoreTests : System.IDisposable
{
    private readonly string _path = Path.Combine(
        Path.GetTempPath(), $"acdream-plugin-ui-test-{System.Guid.NewGuid():N}.json");

    public void Dispose()
    {
        if (File.Exists(_path)) File.Delete(_path);
    }

    [Fact]
    public void Missing_file_loads_classic_and_floating()
    {
        Assert.Equal(PluginUiSettings.Default, new SettingsStore(_path).LoadPluginUi());
        Assert.Equal(new PluginUiSettings("Classic", "floating"), PluginUiSettings.Default);
    }

    [Fact]
    public void Theme_and_dock_round_trip_together()
    {
        var store = new SettingsStore(_path);
        store.SavePluginUi(new PluginUiSettings("Moss", "left"));

        Assert.Equal(new PluginUiSettings("Moss", "left"), new SettingsStore(_path).LoadPluginUi());
    }

    [Fact]
    public void Saving_one_field_keeps_the_other()
    {
        var store = new SettingsStore(_path);
        store.SavePluginUi(new PluginUiSettings("Brass", "right"));
        store.SavePluginUi(store.LoadPluginUi() with { Theme = "Moss" });
        Assert.Equal(new PluginUiSettings("Moss", "right"), store.LoadPluginUi());

        store.SavePluginUi(store.LoadPluginUi() with { Dock = "floating" });
        Assert.Equal(new PluginUiSettings("Moss", "floating"), store.LoadPluginUi());
    }

    [Fact]
    public void A_file_written_before_the_dock_existed_loads_floating()
    {
        File.WriteAllText(_path, """{ "version": 4, "pluginUi": { "theme": "Brass" } }""");

        Assert.Equal(new PluginUiSettings("Brass", "floating"), new SettingsStore(_path).LoadPluginUi());
    }

    [Fact]
    public void Other_sections_survive_a_plugin_ui_save()
    {
        var store = new SettingsStore(_path);
        store.SaveMisc(MiscSettings.Default with { TooltipDelaySeconds = 0.75f });
        store.SavePluginUi(new PluginUiSettings("Moss", "left"));

        Assert.Equal(0.75f, store.LoadMisc().TooltipDelaySeconds);
    }
}
