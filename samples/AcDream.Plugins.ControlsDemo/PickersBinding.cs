using AcDream.Plugin.Abstractions;

namespace AcDream.Plugins.ControlsDemo;

/// <summary>
/// The Keys and colours window: two hotkey boxes, with a note saying whether
/// each chord could be registered as a plugin hotkey, and two colour pickers
/// whose colours paint a bar and a heading beside them.
/// </summary>
public sealed class PickersBinding
{
    public string RadarKey { get; private set; } = "Ctrl+R";
    public Action<string> SetRadarKey => value => { RadarKey = value; Status = $"Radar key {Describe(value)}"; };
    public string PauseKey { get; private set; } = string.Empty;
    public Action<string> SetPauseKey => value => { PauseKey = value; Status = $"Pause key {Describe(value)}"; };

    /// <summary>A box can record keys (F13, Caps Lock) that a plugin hotkey can't use.</summary>
    public string KeyNote => $"Hotkeys: radar {Usable(RadarKey)}, pause {Usable(PauseKey)}";

    /// <summary>0xAARRGGBB, with the picker's alpha strip.</summary>
    public uint BarColor { get; private set; } = 0xC040A040;
    public Action<uint> SetBarColor => value => { BarColor = value; Status = $"Bar #{value:X8}"; };

    /// <summary>#AARRGGBB; the picker has no alpha strip, so it stays opaque.</summary>
    public string HeadingColor { get; private set; } = "#FFE8C060";
    public Action<string> SetHeadingColor => value => { HeadingColor = value; Status = $"Heading {value}"; };

    public string Status { get; private set; } = "Click a box or a swatch";

    private static string Describe(string chord) => chord.Length == 0 ? "cleared" : $"set to {chord}";

    private static string Usable(string chord) =>
        chord.Length == 0 ? "unbound"
        : PluginKeyChord.TryParseHotkeyText(chord, out _) ? "usable"
        : "not usable";
}
