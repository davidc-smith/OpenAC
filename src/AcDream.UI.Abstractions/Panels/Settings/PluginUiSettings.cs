namespace AcDream.UI.Abstractions.Panels.Settings;

/// <summary>
/// The plugin appearance the player picked: the theme's name and where the
/// plugin dock sits ("floating", "left" or "right"). Both are saved together
/// in the <c>pluginUi</c> section, so saving one never drops the other.
/// </summary>
public sealed record PluginUiSettings(string Theme, string Dock)
{
    public static PluginUiSettings Default { get; } = new("Classic", "floating");
}
