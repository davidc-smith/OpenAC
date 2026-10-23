namespace AcDream.Plugins.ControlsDemo;

/// <summary>
/// The Keys and colours window: two hotkey boxes and two colour pickers
/// whose colours paint a bar and a heading beside them.
/// </summary>
public sealed class PickersBinding
{
    public string RadarKey { get; private set; } = "Ctrl+R";
    public Action<string> SetRadarKey => value => { RadarKey = value; Status = Describe("Radar", value); };
    public string PauseKey { get; private set; } = string.Empty;
    public Action<string> SetPauseKey => value => { PauseKey = value; Status = Describe("Pause", value); };

    /// <summary>0xAARRGGBB, with the picker's alpha strip.</summary>
    public uint BarColor { get; private set; } = 0xC040A040;
    public Action<uint> SetBarColor => value => { BarColor = value; Status = $"Bar #{value:X8}"; };

    /// <summary>#AARRGGBB; the picker has no alpha strip, so it stays opaque.</summary>
    public string HeadingColor { get; private set; } = "#FFE8C060";
    public Action<string> SetHeadingColor => value => { HeadingColor = value; Status = $"Heading {value}"; };

    public string Status { get; private set; } = "Click a box or a swatch";

    /// <summary>Kept to 40 characters even for the longest key names.</summary>
    private static string Describe(string what, string chord) => chord.Length == 0 ? $"{what} cleared" : $"{what}: {chord}";
}
