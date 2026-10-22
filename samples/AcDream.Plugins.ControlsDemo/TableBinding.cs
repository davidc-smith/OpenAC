using System.Globalization;
using AcDream.Plugin.Abstractions;

namespace AcDream.Plugins.ControlsDemo;

/// <summary>
/// The Table window: loot rows with a select-all keep column, sortable name,
/// value and burden columns (burden sorts by its number, not its "50 bu"
/// text), a Use button per row, multi-select, and a right-click menu. Every
/// row index the list hands back is the plugin's own, however it is sorted.
/// Removing rows asks first, in a box that blocks only this window.
/// </summary>
public sealed class TableBinding
{
    private sealed class Item(string name, int value, int burden)
    {
        public string Name { get; } = name;
        public int Value { get; } = value;
        public int Burden { get; } = burden;
        public bool Keep { get; set; }
    }

    private readonly IUiRegistry _ui;
    private readonly List<Item> _items =
    [
        new("Mana Stone", 3000, 50), new("Steel Salvage", 520, 100),
        new("Scroll of Blade Blessing", 1200, 30), new("Peridot", 400, 10),
        new("Ornate Bracelet", 2600, 40), new("Lockpick", 25, 5),
        new("Healing Kit", 180, 25), new("Pyreal Nugget", 75, 15),
    ];
    private int _found;

    public TableBinding(IUiRegistry ui)
    {
        _ui = ui;
        Rebuild();
    }

    public IReadOnlyList<bool> Keep { get; private set; } = [];
    public IReadOnlyList<string> Names { get; private set; } = [];
    public IReadOnlyList<string> Values { get; private set; } = [];
    public IReadOnlyList<string> Burdens { get; private set; } = [];
    public IReadOnlyList<double> BurdenKeys { get; private set; } = [];
    public IReadOnlyList<string> Actions { get; private set; } = [];

    /// <summary>The selected rows, as the plugin's own indices in ascending order.</summary>
    public IReadOnlyList<int> Picked { get; private set; } = [];
    public Action<IReadOnlyList<int>> SetPicked => rows => { Picked = rows; Status = $"{rows.Count} selected"; };
    public bool HasSelection => Picked.Count > 0;

    public Action<int> ToggleKeep => row =>
    {
        _items[row].Keep = !_items[row].Keep;
        Rebuild();
        Status = $"{(_items[row].Keep ? "Keeping" : "Not keeping")} {_items[row].Name}";
    };

    /// <summary>The value every row should take, from the select-all header.</summary>
    public Action<bool> KeepAll => keep =>
    {
        foreach (Item item in _items) item.Keep = keep;
        Rebuild();
        Status = keep ? "Keeping everything" : "Keeping nothing";
    };

    /// <summary>A free-text note; its only job is to give the window a field to click, so Tab can reach the list.</summary>
    public string Note { get; private set; } = string.Empty;
    public Action<string> SetNote => value => Note = value;

    public Action<int> Act => row => Status = $"Used {_items[row].Name}";

    /// <summary>The right-clicked row (already selected by the list), or -1 over the header.</summary>
    public Action<int> MenuOpened => row => Status = row < 0 ? "Menu over the header" : $"Menu on {_items[row].Name}";

    /// <summary>Items are Keep|Don't keep|-|Remove...; the index counts the separator.</summary>
    public Action<int> MenuPicked => index =>
    {
        switch (index)
        {
            case 0: SetKeep(true); break;
            case 1: SetKeep(false); break;
            case 3: RemoveSelected(); break;
        }
    };

    public Action AddRow => () =>
    {
        _found++;
        _items.Add(new Item($"Found item {_found}", 10 * _found, _found));
        Rebuild();
        Status = $"Added Found item {_found}";
    };

    public Action RemoveSelected => () =>
    {
        if (Picked.Count == 0) return;
        int[] rows = [.. Picked];
        string what = rows.Length == 1 ? _items[rows[0]].Name : $"{rows.Length} items";
        // The box blocks this window, so the rows can't change before the answer.
        bool shown = _ui.Confirm(
            new PluginDialog("Remove items", $"Remove {what} from the table?", ["Remove", "Keep"], OwnerView: "table"),
            answer =>
            {
                if (answer != 0) { Status = "Nothing removed"; return; }
                foreach (int row in rows.OrderDescending()) _items.RemoveAt(row);
                Picked = [];
                Rebuild();
                Status = $"Removed {what}";
            });
        if (!shown) Status = "This host has no dialogs";
    };

    public string Status { get; private set; } = "Click, Ctrl-click or Shift-click rows";

    private void SetKeep(bool keep)
    {
        foreach (int row in Picked) _items[row].Keep = keep;
        Rebuild();
        Status = $"{(keep ? "Keeping" : "Not keeping")} {Picked.Count} rows";
    }

    private void Rebuild()
    {
        Keep = _items.Select(i => i.Keep).ToArray();
        Names = _items.Select(i => i.Name).ToArray();
        Values = _items.Select(i => i.Value.ToString(CultureInfo.InvariantCulture)).ToArray();
        Burdens = _items.Select(i => $"{i.Burden} bu").ToArray();
        BurdenKeys = _items.Select(i => (double)i.Burden).ToArray();
        Actions = _items.Select(_ => "Use").ToArray();
    }
}
