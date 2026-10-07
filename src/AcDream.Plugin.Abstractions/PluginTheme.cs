namespace AcDream.Plugin.Abstractions;

/// <summary>The plugin themes the player can pick under Appearance.</summary>
public enum PluginUiThemeKind
{
    /// <summary>The client's own black and gold; themed windows draw unthemed.</summary>
    Classic,

    /// <summary>Charcoal with moss-green accents.</summary>
    Moss,

    /// <summary>Warm graphite with brass accents.</summary>
    Brass,
}

/// <summary>
/// The plugin theme the player picked, read from
/// <see cref="IUiRegistry.Theme"/>, so a canvas can draw in the same colours
/// as the plugin's themed windows.
/// </summary>
/// <param name="Kind">Which theme is selected.</param>
/// <param name="DisplayName">What the Appearance picker calls it: "Classic", "Charcoal + moss" or "Warm graphite + brass".</param>
/// <param name="Palette">The theme's colours; null under Classic, where themed windows draw unthemed.</param>
public sealed record PluginUiThemeInfo(PluginUiThemeKind Kind, string DisplayName, PluginThemePalette? Palette)
{
    /// <summary>Classic: what a host without themes, or without a window, answers.</summary>
    public static PluginUiThemeInfo Classic { get; } = new(PluginUiThemeKind.Classic, "Classic", null);
}

/// <summary>The colours a plugin theme draws themed windows in.</summary>
/// <param name="Background">The window's face.</param>
/// <param name="Field">The face of inputs, lists and other sunken areas.</param>
/// <param name="Border">Window and control edges.</param>
/// <param name="Text">Body text.</param>
/// <param name="Muted">Secondary text: captions, hints and disabled labels.</param>
/// <param name="Accent">Highlights: a switch that is on, focus, links.</param>
/// <param name="Selected">The fill behind a selected row or tab.</param>
public sealed record PluginThemePalette(
    PluginColor Background,
    PluginColor Field,
    PluginColor Border,
    PluginColor Text,
    PluginColor Muted,
    PluginColor Accent,
    PluginColor Selected);

/// <summary>
/// The sizes themed windows are drawn with, in interface pixels, so a canvas
/// drawn to match uses the same ones. They are constants: a plugin takes the
/// values of the contract it was built against.
/// </summary>
public static class PluginThemeMetrics
{
    /// <summary>The corner radius of a themed window.</summary>
    public const float WindowRadius = 10f;

    /// <summary>The corner radius of a button, input or other control.</summary>
    public const float ControlRadius = 6f;

    /// <summary>The height of a themed window's title band.</summary>
    public const float HeaderHeight = 24f;

    /// <summary>The width of a switch's track.</summary>
    public const float SwitchWidth = 26f;

    /// <summary>The height of a switch's track.</summary>
    public const float SwitchHeight = 14f;

    /// <summary>The diameter of a switch's knob.</summary>
    public const float SwitchKnob = 8f;
}
