namespace AcDream.Plugins.ControlsDemo;

/// <summary>
/// The Inputs window: three tick boxes under a select-all box that shows a
/// dash while they disagree, a box that only displays, two number fields (a
/// whole number and one with a decimal place), and a radio group beside a
/// segmented bar.
/// </summary>
public sealed class InputsBinding
{
    private static readonly string[] Modes = ["Melee", "Missile", "Magic"];

    public bool Rares { get; private set; } = true;
    public Action<bool> SetRares => value => { Rares = value; Report("Rares", value); };
    public bool Scrolls { get; private set; }
    public Action<bool> SetScrolls => value => { Scrolls = value; Report("Scrolls", value); };
    public bool Salvage { get; private set; } = true;
    public Action<bool> SetSalvage => value => { Salvage = value; Report("Salvage", value); };

    /// <summary>Ticked when all three are, clear when none are, a dash otherwise.</summary>
    public bool? AllPickups => Rares && Scrolls && Salvage ? true
        : !Rares && !Scrolls && !Salvage ? false
        : null;

    /// <summary>The value every pickup should take: true from clear or a dash, false from ticked.</summary>
    public Action<bool> SetAllPickups => value =>
    {
        Rares = Scrolls = Salvage = value;
        Status = value ? "Picking up everything" : "Picking up nothing";
    };

    /// <summary>Bound to a disabled box with no onchange: it only displays.</summary>
    public bool Pyreals => true;

    public int HealAt { get; private set; } = 60;
    public Action<int> SetHealAt => value => { HealAt = value; Status = $"Heal at {value}%"; };
    public double Range { get; private set; } = 12.5;
    public Action<double> SetRange => value => { Range = value; Status = $"Range {value:0.0} m"; };

    public int Mode { get; private set; }
    public Action<int> SetMode => index => { Mode = index; Status = $"{Modes[index]} mode"; };
    public string ModeHint => Mode switch { 0 => "Swords and fists", 1 => "Bows and atlatls", _ => "War and life" };

    public IReadOnlyList<string> Speeds { get; } = ["Walk", "Jog", "Run"];
    public int Speed { get; private set; } = 2;
    public Action<int> SetSpeed => index => { Speed = index; Status = $"{Speeds[index]} speed"; };

    public string Status { get; private set; } = "Change anything";

    private void Report(string what, bool on) => Status = $"{what} {(on ? "on" : "off")}";
}
