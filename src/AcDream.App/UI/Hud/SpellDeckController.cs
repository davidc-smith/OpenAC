using AcDream.App.Spells;
using AcDream.UI.Abstractions.Input;

namespace AcDream.App.UI.Hud;

/// <summary>
/// The Spell Deck's live state and behaviour: one current tab, shown over
/// <see cref="RowCount"/> rows of <see cref="PerRow"/> slots (row r shows spells
/// r·N … r·N + N − 1), the outline, hotkeys, clicks, drops and row and tab edits,
/// run through the pure model and outline rules. Tabs come from
/// <see cref="SpellDeckTabs"/>: server tabs 0-7 are changed through
/// <see cref="ISpellDeckActions"/>, local tabs 8-15 in the settings. Every change
/// to the tab, the rows, attachment or a local tab saves the settings once.
/// </summary>
public sealed class SpellDeckController
{
    public const string FullTabMessage = "That spell tab is full.";

    private readonly ISpellDeckActions _actions;
    private readonly SpellDeckTabs _tabs;
    private readonly Func<ItemSpell?> _itemSpell;
    private readonly Func<int> _capacity;
    private readonly Action<SpellDeckSettings> _save;
    private readonly Func<bool> _clickCasts;
    private readonly Func<bool> _canAddRow;
    private int _tab;
    private int _rowCount;
    private bool _attached;
    private int? _detachedRows;
    /// <summary>Above zero while one change runs: the saves it causes become one, at its end.</summary>
    private int _batch;
    private bool _dirty;

    /// <param name="favorites">The server's favourite list for a tab 0-7 (the spellbook's).</param>
    /// <param name="capacity">Slots per row the deck shows now (from its geometry).</param>
    /// <param name="canAddRow">Whether one more row would still fit on screen.</param>
    public SpellDeckController(
        ISpellDeckActions actions,
        Func<int, IReadOnlyList<uint>> favorites,
        Func<ItemSpell?> itemSpell,
        Func<int> capacity,
        SpellDeckSettings settings,
        Action<SpellDeckSettings> save,
        Func<bool> clickCasts,
        Func<bool> canAddRow)
    {
        _actions = actions ?? throw new ArgumentNullException(nameof(actions));
        _tabs = new SpellDeckTabs(favorites ?? throw new ArgumentNullException(nameof(favorites)), actions, Changed);
        _itemSpell = itemSpell ?? throw new ArgumentNullException(nameof(itemSpell));
        _capacity = capacity ?? throw new ArgumentNullException(nameof(capacity));
        _save = save ?? throw new ArgumentNullException(nameof(save));
        _clickCasts = clickCasts ?? throw new ArgumentNullException(nameof(clickCasts));
        _canAddRow = canAddRow ?? throw new ArgumentNullException(nameof(canAddRow));
        Apply(settings);
    }

    private void Apply(SpellDeckSettings settings)
    {
        SpellDeckSettings s = (settings ?? SpellDeckSettings.Default).Normalize();
        _tabs.Load(s.LocalTabs);
        _tab = s.Tab;
        _rowCount = s.RowCount;
        _attached = s.Attached;
        _detachedRows = s.DetachedRows;
    }

    private SpellDeckSettings ToSettings() => new(_tab, _rowCount, _attached, _detachedRows, _tabs.Local);

    // ── State ─────────────────────────────────────────────────────

    /// <summary>The tab the whole deck shows (0-7 server, 8-15 local).</summary>
    public int Tab => _tab;
    public int RowCount => _rowCount;
    /// <summary>Slots per row (from the deck's width), at least 1.</summary>
    public int PerRow => Math.Max(1, _capacity());
    /// <summary>How many tabs exist: 8 server tabs plus the local ones.</summary>
    public int TabCount => _tabs.Count;
    public SpellDeckTabs Tabs => _tabs;
    public SpellDeckMark Outline { get; private set; }
    public bool Attached => _attached;

    /// <summary>The row count the stored detached placement goes with; null when not recorded.</summary>
    public int? DetachedRows => _detachedRows;
    public bool ClickCasts => _clickCasts();
    public ItemSpell? ItemSpell => _itemSpell();

    public IReadOnlyList<uint> Favorites(int tab) => _tabs.Get(tab);

    /// <summary>The spells the deck shows (and can cast): the current tab's first <c>RowCount·PerRow</c>.</summary>
    public IReadOnlyList<uint> Visible => SpellDeckModel.Visible(_tabs.Get(_tab), _rowCount, PerRow);

    /// <summary>The spells row <paramref name="row"/> shows; empty for a row that is not shown.</summary>
    public IReadOnlyList<uint> RowSpells(int row) =>
        row < 0 || row >= _rowCount ? [] : SpellDeckModel.Slice(_tabs.Get(_tab), row, PerRow);

    /// <summary>The fewest slots per row with which the rows hold the fullest of all tabs, server and local.</summary>
    public int MinSlotsPerRow => SpellDeckModel.MinSlotsPerRow(_rowCount, AllTabCounts());

    private IEnumerable<int> AllTabCounts()
    {
        for (int t = 0; t < _tabs.Count; t++) yield return _tabs.Get(t).Count;
    }

    private int Capacity => _rowCount * PerRow;

    private SpellDeckMarkContext Context => new(_tab, Visible, _itemSpell() is not null);

    /// <summary>Replaces the tab, rows, local tabs and attachment (character change), without saving. The outline is repaired.</summary>
    public void Load(SpellDeckSettings settings)
    {
        Apply(settings);
        Outline = SpellDeckMark.None;
        Tick();
    }

    /// <summary>Re-checks the outline against the current tab, favourites, rows, capacity and item.</summary>
    public void Tick() => Outline = SpellDeckMark.Validate(Outline, Context);

    // ── Command Deck ──────────────────────────────────────────────
    public bool CanCastOutlined => Outline.Kind != SpellDeckMarkKind.None;
    public uint? OutlinedSpellId => Outline.Kind switch
    {
        SpellDeckMarkKind.Spell => Outline.SpellId,
        SpellDeckMarkKind.Item => _itemSpell()?.SpellId,
        _ => null,
    };
    public void CastOutlined()
    {
        switch (Outline.Kind)
        {
            case SpellDeckMarkKind.Spell: _actions.CastSpell(Outline.SpellId); break;
            case SpellDeckMarkKind.Item when _itemSpell() is { } item: _actions.CastItemSpell(item.ItemId); break;
        }
    }

    // ── Tabs ──────────────────────────────────────────────────────

    /// <summary>Shows tab <paramref name="tab"/>; its first visible spell is outlined.</summary>
    public void ShowTab(int tab)
    {
        if (tab < 0 || tab >= _tabs.Count || tab == _tab) return;
        _tab = tab;
        Outline = SpellDeckMark.ShowTab(Context);
        Save();
    }

    public void NextTab() => ShowTab((_tab + 1) % _tabs.Count);
    public void PrevTab() => ShowTab((_tab + _tabs.Count - 1) % _tabs.Count);
    public void FirstTab() => ShowTab(0);
    public void LastTab() => ShowTab(_tabs.Count - 1);

    public bool CanAddLocalTab => _tabs.CanAddLocal;
    public bool CanRemoveLastLocalTab => _tabs.LocalCount > 0;

    /// <summary>Adds an empty local tab and shows it.</summary>
    public void AddLocalTab()
    {
        Batch(() =>
        {
            if (_tabs.AddLocal()) ShowTab(_tabs.Count - 1);
        });
    }

    /// <summary>Removes the last local tab and its spells; when it was shown, the tab before it is shown.</summary>
    public void RemoveLastLocalTab()
    {
        Batch(() =>
        {
            int last = _tabs.Count - 1;
            if (!_tabs.RemoveLastLocal()) return;
            if (_tab == last)
            {
                _tab = last - 1;
                Outline = SpellDeckMark.ShowTab(Context);
            }
        });
    }

    // ── Hotkeys ───────────────────────────────────────────────────
    public bool Handle(InputAction action)
    {
        if (SpellBarActions.TryParse(action, out int row, out int slot))
        {
            if (SlotSpell(row, slot) is uint id) { Outline = SpellDeckMark.Spell(_tab, id); CastOutlined(); }
            return true;
        }
        switch (action)
        {
            case InputAction.CastItemSpell: ClickItem(cast: true); return true;
            case InputAction.CombatPrevSpellTab: PrevTab(); return true;
            case InputAction.CombatNextSpellTab: NextTab(); return true;
            case InputAction.CombatFirstSpellTab: FirstTab(); return true;
            case InputAction.CombatLastSpellTab: LastTab(); return true;
            case InputAction.CombatPrevSpell: Outline = SpellDeckMark.Prev(Outline, Context); return true;
            case InputAction.CombatNextSpell: Outline = SpellDeckMark.Next(Outline, Context); return true;
            case InputAction.CombatFirstSpell: Outline = SpellDeckMark.First(Context); return true;
            case InputAction.CombatLastSpell: Outline = SpellDeckMark.Last(Context); return true;
            case InputAction.CombatCastCurrentSpell: CastOutlined(); return true;
            default: return false;
        }
    }

    /// <summary>The spell slot <paramref name="slot"/> of row <paramref name="row"/> shows, or null
    /// (an empty slot, a row not shown, or a slot past the row's end).</summary>
    public uint? SlotSpell(int row, int slot)
    {
        if (slot < 0 || slot >= PerRow) return null;
        IReadOnlyList<uint> spells = RowSpells(row);
        return slot < spells.Count ? spells[slot] : null;
    }

    // ── Pointer ───────────────────────────────────────────────────
    public void Click(int row, int slot)
    {
        if (SlotSpell(row, slot) is not uint id) return;
        Outline = SpellDeckMark.Spell(_tab, id);
        if (ClickCasts) CastOutlined();
    }

    public void DoubleClick(int row, int slot)
    {
        if (ClickCasts || SlotSpell(row, slot) is not uint id) return;
        Outline = SpellDeckMark.Spell(_tab, id);
        CastOutlined();
    }

    public void ClickItem() => ClickItem(cast: ClickCasts);
    public void DoubleClickItem() { if (!ClickCasts) ClickItem(cast: true); }

    private void ClickItem(bool cast)
    {
        if (_itemSpell() is null) return;
        Outline = SpellDeckMark.Item;
        if (cast) CastOutlined();
    }

    // ── Drops ─────────────────────────────────────────────────────

    /// <summary>The plan for a drop on (row, slot) of the current tab; null off the shown slots.
    /// A slot lifted from another tab (the tab changed mid-drag) leaves that tab.</summary>
    private SpellDeckDropPlan? PlanSlot(SpellDeckDragSource source, int row, int slot)
    {
        if (row < 0 || row >= _rowCount || slot < 0 || slot >= PerRow) return null;
        SpellDeckDropPlan plan = SpellDeckModel.PlanDropOnSlot(source, _tab, row * PerRow + slot, _tabs.Get(_tab), Capacity);
        if (plan.Accepted && !source.IsFromSpellbook && source.Tab != _tab)
            plan = new(SpellDeckDropKind.Move, [.. plan.Ops, SpellDeckFavoriteOp.Remove(source.Tab, source.SpellId)]);
        return plan;
    }

    private SpellDeckDropPlan? PlanTab(SpellDeckDragSource source, int tab) =>
        tab < 0 || tab >= _tabs.Count ? null
        : SpellDeckModel.PlanDropOnTab(source, tab, _tabs.Get(tab), Capacity);

    /// <summary>Whether a drop on (row, slot) would be accepted, for the drag highlight.</summary>
    public bool CanDropOnSlot(SpellDeckDragSource source, int row, int slot) =>
        PlanSlot(source, row, slot)?.Accepted == true;

    /// <summary>Whether a drop on tab <paramref name="tab"/>'s bookmark would change something.</summary>
    public bool CanDropOnTab(SpellDeckDragSource source, int tab) =>
        PlanTab(source, tab) is { Accepted: true, Kind: not SpellDeckDropKind.None };

    /// <summary>A drop on row <paramref name="row"/>, slot <paramref name="slot"/>: position <c>row·PerRow + slot</c> of the current tab.</summary>
    public void DropOnSlot(SpellDeckDragSource source, int row, int slot) => Run(PlanSlot(source, row, slot));

    /// <summary>A drop on a tab bookmark: to the end of that tab (a slot's spell moves there).</summary>
    public void DropOnTab(SpellDeckDragSource source, int tab) => Run(PlanTab(source, tab));

    /// <summary>A slot dragged off every window leaves its tab; a spellbook spell does nothing.</summary>
    public void DropOutside(SpellDeckDragSource source)
    {
        if (source.IsFromSpellbook) return;
        Run(SpellDeckModel.PlanRemove(source));
    }

    /// <summary>Removes <paramref name="spellId"/> from <paramref name="tab"/>, only while the deck still shows that tab and spell.</summary>
    public void RemoveFromTab(int tab, uint spellId)
    {
        if (Shows(tab, spellId)) DropOutside(new SpellDeckDragSource(tab, spellId));
    }

    /// <summary>Examines <paramref name="spellId"/>, only while the deck still shows <paramref name="tab"/> and that spell.</summary>
    public void Examine(int tab, uint spellId)
    {
        if (Shows(tab, spellId)) _actions.ExamineSpell(spellId);
    }

    private bool Shows(int tab, uint spellId) => tab == _tab && Visible.Contains(spellId);

    /// <summary>The spellbook's "add to favourites": the end of the current tab; a spell already there is outlined.</summary>
    public void AddFavoriteFromSpellbook(uint spellId)
    {
        IReadOnlyList<uint> tab = _tabs.Get(_tab);
        if (tab.Contains(spellId))
        {
            Outline = SpellDeckMark.Spell(_tab, spellId);
            Tick();
            return;
        }
        Run(SpellDeckModel.PlanDropOnSlot(SpellDeckDragSource.FromSpellbook(spellId), _tab, tab.Count, tab, Capacity));
    }

    private void Run(SpellDeckDropPlan? plan)
    {
        if (plan is null) return;
        if (!plan.Accepted) { _actions.ShowMessage(FullTabMessage); return; }
        Batch(() =>
        {
            foreach (SpellDeckFavoriteOp op in plan.Ops)
                if (op.IsRemove) _tabs.Remove(op.Tab, op.SpellId);
                else _tabs.Set(op.Tab, op.Position, op.SpellId);
        });
        Tick();
    }

    // ── Rows ──────────────────────────────────────────────────────
    public bool CanAddRow() => _rowCount < SpellDeckGeometry.MaxRows && _canAddRow();
    public bool CanRemoveRow => SpellDeckModel.CanRemoveRow(_rowCount, AllTabCounts());

    public void AddRow()
    {
        if (!CanAddRow()) return;
        _rowCount++;
        Changed();
    }

    public void RemoveRow()
    {
        if (!CanRemoveRow) return;
        _rowCount--;
        Changed();
    }

    // ── Attachment ────────────────────────────────────────────────
    public void SetAttached(bool attached)
    {
        if (_attached == attached) return;
        _attached = attached;
        Changed();
    }

    /// <summary>Records the row count of the detached placement just placed or saved.</summary>
    public void SetDetachedRows(int rows)
    {
        if (_detachedRows == rows) return;
        _detachedRows = rows;
        Save();
    }

    // ── Saving ────────────────────────────────────────────────────
    private void Changed()
    {
        Tick();
        Save();
    }

    private void Save()
    {
        if (_batch > 0) { _dirty = true; return; }
        _save(ToSettings());
    }

    /// <summary>Runs <paramref name="change"/> as one change: whatever it saves is saved once, at the end.</summary>
    private void Batch(Action change)
    {
        _batch++;
        try { change(); }
        finally { _batch--; }
        if (_batch == 0 && _dirty)
        {
            _dirty = false;
            Save();
        }
    }
}
