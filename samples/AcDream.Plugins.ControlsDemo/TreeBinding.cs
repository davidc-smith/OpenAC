namespace AcDream.Plugins.ControlsDemo;

/// <summary>
/// The Tree window: rules flattened parent-first with a depth and a stable
/// key per row. The client keeps (and remembers) which rows are expanded; the
/// plugin is only told. A right click turns a whole branch on or off.
/// </summary>
public sealed class TreeBinding
{
    private sealed class Rule(string key, int level, string name, string value)
    {
        public string Key { get; } = key;
        public int Level { get; } = level;
        public string Name { get; } = name;
        public string Value { get; } = value;
        public bool On { get; set; } = true;
    }

    private readonly Rule[] _rules =
    [
        new("loot", 0, "Looting", ""),
        new("loot.salvage", 1, "Salvage", "workmanship 6+"),
        new("loot.salvage.steel", 2, "Steel", "always"),
        new("loot.salvage.iron", 2, "Iron", "imbue only"),
        new("loot.scrolls", 1, "Scrolls", ""),
        new("loot.scrolls.7", 2, "Level 7", "unknown only"),
        new("loot.scrolls.8", 2, "Level 8", "all"),
        new("combat", 0, "Combat", ""),
        new("combat.buffs", 1, "Buffs", "rebuff at 5 min"),
        new("combat.targets", 1, "Targets", ""),
        new("combat.targets.drudge", 2, "Drudges", "priority 2"),
        new("combat.targets.olthoi", 2, "Olthoi", "priority 1"),
        new("nav", 0, "Navigation", ""),
        new("nav.routes", 1, "Routes", "3 saved"),
    ];
    private int _menuRow = -1;

    public TreeBinding()
    {
        Levels = _rules.Select(r => r.Level).ToArray();
        Keys = _rules.Select(r => r.Key).ToArray();
        Names = _rules.Select(r => r.Name).ToArray();
        Values = _rules.Select(r => r.Value).ToArray();
        Refresh();
    }

    public IReadOnlyList<int> Levels { get; }
    public IReadOnlyList<string> Keys { get; }
    public IReadOnlyList<string> Names { get; }
    public IReadOnlyList<string> Values { get; }
    public IReadOnlyList<bool> On { get; private set; } = [];

    public int Selected { get; private set; } = -1;
    public Action<int> Select => row => { Selected = row; Status = $"{_rules[row].Name} selected"; };

    public Action<int> Toggle => row =>
    {
        _rules[row].On = !_rules[row].On;
        Refresh();
        Status = $"{_rules[row].Name} {(_rules[row].On ? "on" : "off")}";
    };

    /// <summary>Told each expand or collapse; the client applies it.</summary>
    public Action<string, bool> Expanded => (key, open) =>
        Status = $"{_rules.First(r => r.Key == key).Name} {(open ? "expanded" : "collapsed")}";

    public Action<int> MenuOpened => row => _menuRow = row;

    /// <summary>Items are Enable branch|Disable branch.</summary>
    public Action<int> MenuPicked => index =>
    {
        if (_menuRow < 0) return;
        bool on = index == 0;
        int level = _rules[_menuRow].Level;
        _rules[_menuRow].On = on;
        for (int row = _menuRow + 1; row < _rules.Length && _rules[row].Level > level; row++)
            _rules[row].On = on;
        Refresh();
        Status = $"{_rules[_menuRow].Name} branch {(on ? "on" : "off")}";
    };

    public string Status { get; private set; } = "Click an arrow or right-click a rule";

    private void Refresh() => On = _rules.Select(r => r.On).ToArray();
}
