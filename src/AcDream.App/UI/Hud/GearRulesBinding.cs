using AcDream.Core.Items.Gear;

namespace AcDream.App.UI.Hud;

/// <summary>Pack rules as the planner takes them, and the pack names the deck shows for them.</summary>
internal static class GearRules
{
    public const string Missing = "(missing)";

    /// <summary>Each rule with its saved filter's query; a rule whose filter is gone gets the empty query (skipped).</summary>
    public static IReadOnlyList<(GearPackRule Rule, GearQuery Query)> Resolve(GearDeckSettings s, Func<GearPackRule, bool>? keep = null) =>
        s.PackRules.Where(r => keep?.Invoke(r) ?? true)
            .Select(r => (r, FilterOf(s, r.Filter)?.Query ?? GearQuery.Empty))
            .ToArray();

    public static SavedFilter? FilterOf(GearDeckSettings s, string name) =>
        s.SavedFilters.FirstOrDefault(f => string.Equals(f.Name, name, StringComparison.OrdinalIgnoreCase));

    public static string PackName(IReadOnlyList<GearPack> packs, uint id)
    {
        for (int i = 0; i < packs.Count; i++)
            if (packs[i].Id == id) return packs[i].IsMain ? "Main" : $"Pack {i + 1}";
        return Missing;
    }
}

/// <summary>
/// The Pack rules popup: which saved filter goes to which pack, in order (the first rule an item matches
/// wins), whether a rule also runs on arrival, and "combine stacks when items arrive". Every edit goes
/// through the session. Built once and kept; it reads the session and the packs when drawn.
/// </summary>
public sealed class GearRulesBinding
{
    private readonly Func<GearDeckSession?> _session;
    private readonly Func<IReadOnlyList<GearPack>> _packs;
    private int _selected = -1;
    private GearDeckSettings? _rowsFor;
    private IReadOnlyList<GearPack>? _rowsPacks;
    private Rows _rows = new([], [], [], []);

    private sealed record Rows(IReadOnlyList<bool> On, IReadOnlyList<string> Filter, IReadOnlyList<string> Pack, IReadOnlyList<bool> Arrival);

    internal GearRulesBinding(Func<GearDeckSession?> session, Func<IReadOnlyList<GearPack>> packs)
    {
        _session = session;
        _packs = packs;
    }

    public const string Markup = """
        <panel x="100" y="80" w="420" h="380" title="Pack rules" theme="plugin" layout="column" padding="10" gap="6">
          <label text="Send what a saved filter matches to a pack. The first rule that matches wins." h="16" />
          <label text="Save a filter first: rules use saved filters." visible="{NoFilters}" h="16" />
          <list header="true" selected="{SelectedRuleIndex}" onchange="{SelectRuleRow}" rowheight="18" selectionband="true" grow="1">
            <column type="checkbox" header="On" width="30" values="{RuleOn}" onchange="{ToggleRule}" />
            <column type="text" header="Filter" width="*" items="{RuleFilter}" />
            <column type="text" header="Pack" width="80" items="{RulePack}" />
            <column type="checkbox" header="On arrival" width="70" values="{RuleArrival}" onchange="{ToggleArrival}" />
          </list>
          <group layout="row" gap="6" align="center" h="24">
            <label text="Filter" w="40" />
            <menu items="{FilterNames}" selected="{SelectedFilter}" onchange="{SelectFilter}" enabled="{HasSelectedRule}" w="150" h="22" />
            <label text="Pack" w="34" />
            <menu items="{PackNames}" selected="{SelectedPack}" onchange="{SelectPack}" enabled="{HasSelectedRule}" w="110" h="22" />
          </group>
          <group layout="row" gap="6" h="22">
            <button text="+ Rule" onclick="{AddRule}" enabled="{HasFilters}" w="60" h="22" />
            <button text="Delete" onclick="{DeleteRule}" enabled="{HasSelectedRule}" w="60" h="22" />
            <group grow="1" />
            <button text="Up" onclick="{MoveUp}" enabled="{HasSelectedRule}" w="40" h="22" />
            <button text="Down" onclick="{MoveDown}" enabled="{HasSelectedRule}" w="44" h="22" />
          </group>
          <checkbox text="Combine stacks when items arrive" checked="{CombineOnArrival}" onchange="{SetCombineOnArrival}" h="16" />
        </panel>
        """;

    private GearDeckSettings S => _session()?.Settings ?? GearDeckSettings.Default;
    private IReadOnlyList<GearPackRule> R => S.PackRules;

    private void Edit(Func<GearDeckSettings, GearDeckSettings> change) => _session()?.Update(change);

    private void EditRules(Func<List<GearPackRule>, bool> change)
    {
        var rules = R.ToList();
        if (change(rules)) Edit(s => s.WithRules(rules));
    }

    /// <summary>The table's columns, rebuilt only when the settings or the packs change.</summary>
    private Rows Table
    {
        get
        {
            GearDeckSettings s = S;
            IReadOnlyList<GearPack> packs = _packs();
            if (ReferenceEquals(s, _rowsFor) && ReferenceEquals(packs, _rowsPacks)) return _rows;
            _rowsFor = s;
            _rowsPacks = packs;
            _rows = new Rows(
                s.PackRules.Select(r => r.Enabled).ToArray(),
                s.PackRules.Select(r => GearRules.FilterOf(s, r.Filter)?.Name ?? GearRules.Missing).ToArray(),
                s.PackRules.Select(r => GearRules.PackName(packs, r.PackId)).ToArray(),
                s.PackRules.Select(r => r.OnArrival).ToArray());
            return _rows;
        }
    }

    public IReadOnlyList<bool> RuleOn => Table.On;
    public IReadOnlyList<string> RuleFilter => Table.Filter;
    public IReadOnlyList<string> RulePack => Table.Pack;
    public IReadOnlyList<bool> RuleArrival => Table.Arrival;

    public int SelectedRuleIndex => _selected >= 0 && _selected < R.Count ? _selected : -1;
    public Action<int> SelectRuleRow => i => _selected = i >= 0 && i < R.Count ? i : -1;
    public bool HasSelectedRule => SelectedRuleIndex >= 0;
    public bool HasFilters => S.SavedFilters.Count > 0;
    public bool NoFilters => !HasFilters;

    public Action<int> ToggleRule => i => EditRules(rules =>
    {
        if (i < 0 || i >= rules.Count) return false;
        rules[i] = rules[i] with { Enabled = !rules[i].Enabled };
        return true;
    });

    public Action<int> ToggleArrival => i => EditRules(rules =>
    {
        if (i < 0 || i >= rules.Count) return false;
        rules[i] = rules[i] with { OnArrival = !rules[i].OnArrival };
        return true;
    });

    public IReadOnlyList<string> FilterNames => S.SavedFilters.Select(f => f.Name).ToArray();
    public string? SelectedFilter => SelectedRuleIndex is var i and >= 0 ? RuleFilter[i] : null;
    public Action<string> SelectFilter => name => EditRules(rules =>
    {
        int i = SelectedRuleIndex;
        if (i < 0 || GearRules.FilterOf(S, name) is not { } f) return false;
        rules[i] = rules[i] with { Filter = f.Name };
        return true;
    });

    public IReadOnlyList<string> PackNames
    {
        get
        {
            IReadOnlyList<GearPack> packs = _packs();
            return packs.Select(p => GearRules.PackName(packs, p.Id)).ToArray();
        }
    }

    public string? SelectedPack => SelectedRuleIndex is var i and >= 0 ? RulePack[i] : null;
    public Action<string> SelectPack => name => EditRules(rules =>
    {
        int i = SelectedRuleIndex, p = PackNames.ToList().IndexOf(name);
        if (i < 0 || p < 0) return false;
        rules[i] = rules[i] with { PackId = _packs()[p].Id };
        return true;
    });

    /// <summary>A new rule: the first saved filter to the main pack, at the end, selected.</summary>
    public Action AddRule => () =>
    {
        if (S.SavedFilters.Count == 0 || _packs() is not { Count: > 0 } packs) return;
        uint main = packs.FirstOrDefault(p => p.IsMain).Id;
        if (main == 0u) main = packs[0].Id;
        EditRules(rules => { rules.Add(new GearPackRule(S.SavedFilters[0].Name, main, OnArrival: false)); return true; });
        _selected = R.Count - 1;
    };

    public Action DeleteRule => () =>
    {
        int i = SelectedRuleIndex;
        if (i < 0) return;
        EditRules(rules => { rules.RemoveAt(i); return true; });
        _selected = -1;
    };

    public Action MoveUp => () => Move(-1);
    public Action MoveDown => () => Move(1);

    private void Move(int by)
    {
        int i = SelectedRuleIndex, j = i + by;
        if (i < 0 || j < 0 || j >= R.Count) return;
        EditRules(rules => { (rules[i], rules[j]) = (rules[j], rules[i]); return true; });
        _selected = j;
    }

    public bool CombineOnArrival => S.CombineOnArrival;
    public Action<bool> SetCombineOnArrival => on => Edit(s => s with { CombineOnArrival = on });

    public Action? Close { get; set; }
}
