namespace AcDream.App.UI;

public sealed class PluginAppearanceBinding(PluginUiThemeSettings settings)
{
    public IReadOnlyList<string> Themes { get; } = ["Classic", "Charcoal + moss", "Warm graphite + brass"];
    public string Selected => Themes[(int)settings.Theme];
    public Action<string> Select => name =>
    {
        for (int i = 0; i < Themes.Count; i++)
            if (Themes[i] == name) { settings.Theme = (PluginUiTheme)i; return; }
    };

    /// <summary>Where the plugin dock sits, in <see cref="PluginDockMode"/> order.</summary>
    public IReadOnlyList<string> Docks { get; } = ["Floating", "Left edge", "Right edge"];
    public string SelectedDock => Docks[(int)settings.Dock];
    public Action<string> SelectDock => name =>
    {
        for (int i = 0; i < Docks.Count; i++)
            if (Docks[i] == name) { settings.Dock = (PluginDockMode)i; return; }
    };

    public Action? Close { get; set; }
}
