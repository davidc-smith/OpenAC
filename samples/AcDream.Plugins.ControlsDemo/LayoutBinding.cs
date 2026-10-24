namespace AcDream.Plugins.ControlsDemo;

/// <summary>
/// The Layout window: a split the player drags (the client remembers where),
/// a section list on the left, and on the right an expander the client
/// remembers, an expander the plugin keeps open or folded, with another
/// remembered one inside it, and a status line that moves up as they fold.
/// </summary>
public sealed class LayoutBinding
{
    public IReadOnlyList<string> Sections { get; } = ["Radar", "Chat", "Combat", "Looting", "Navigation"];
    public int Section { get; private set; }
    public Action<int> SetSection => index => { Section = index; Status = $"{Sections[index]} section"; };

    /// <summary>Told each pixel the bar moves; the client keeps the position.</summary>
    public Action<float> SplitMoved => size => Status = $"List {size:0} px wide";

    /// <summary>The "General" expander is unbound: the client keeps and remembers it.</summary>
    public Action<bool> GeneralChanged => open => Status = open ? "General opened" : "General folded";
    public bool ShowNames { get; private set; } = true;
    public Action<bool> SetShowNames => value => { ShowNames = value; Status = $"Names {(value ? "on" : "off")}"; };
    public bool ShowDistances { get; private set; }
    public Action<bool> SetShowDistances => value => { ShowDistances = value; Status = $"Distances {(value ? "on" : "off")}"; };

    /// <summary>The "Advanced" expander is bound: it follows this property.</summary>
    public bool AdvancedOpen { get; private set; } = true;
    public Action<bool> SetAdvancedOpen => open => { AdvancedOpen = open; Status = open ? "Advanced opened" : "Advanced folded"; };
    public int Scan { get; private set; } = 40;
    public Action<int> SetScan => value => { Scan = value; Status = $"Scan {value} m"; };
    public bool Verbose { get; private set; }
    public Action<bool> SetVerbose => value => { Verbose = value; Status = $"Verbose log {(value ? "on" : "off")}"; };

    public string Status { get; private set; } = "Drag the bar or fold a section";
}
