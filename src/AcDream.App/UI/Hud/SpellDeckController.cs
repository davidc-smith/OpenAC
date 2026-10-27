using System.Diagnostics;
using AcDream.App.Spells;
using AcDream.UI.Abstractions.Input;

namespace AcDream.App.UI.Hud;

/// <summary>
/// The Spell Deck's live state and behaviour: one current tab, shown over <see cref="RowCount"/> rows of
/// <see cref="PerRow"/> slots, each tab a slot layout (<see cref="SpellDeckLayout"/>: any slot, gaps and
/// duplicates), the outline, hotkeys, clicks, drops and row and tab edits, run through the pure model and
/// outline rules. Tabs and layouts live in <see cref="SpellDeckTabs"/>, which keeps the server tabs 0-7 equal
/// to their layouts' distinct spells; local tabs 8-15 are only layouts. Server tabs are reconciled on
/// construction, <see cref="Load"/> and <see cref="ServerFavoritesChanged"/>. Every change to the rows,
/// attachment or a layout saves the settings once; a plain tab switch only marks the settings dirty and
/// <see cref="Tick"/> saves once the switching has paused for <see cref="TabSaveDelaySeconds"/> (or
/// <see cref="Load"/> drops it after the host flushed it).
/// </summary>
public sealed class SpellDeckController
{
    public const string FullTabMessage = "That spell tab is full.";
    /// <summary>How long after the last tab switch a pending tab save waits.</summary>
    public const double TabSaveDelaySeconds = 0.5;

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
    private readonly Func<double> _seconds;
    private bool _tabDirty;
    private double _lastTabSwitch;

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
        Func<bool> canAddRow,
        Func<double>? seconds = null)
    {
        if (seconds is null)
        {
            var clock = Stopwatch.StartNew();
            seconds = () => clock.Elapsed.TotalSeconds;
        }
        _seconds = seconds;
        _actions = actions ?? throw new ArgumentNullException(nameof(actions));
        _tabs = new SpellDeckTabs(favorites ?? throw new ArgumentNullException(nameof(favorites)), actions, Changed);
        _itemSpell = itemSpell ?? throw new ArgumentNullException(nameof(itemSpell));
        _capacity = capacity ?? throw new ArgumentNullException(nameof(capacity));
        _save = save ?? throw new ArgumentNullException(nameof(save));
        _clickCasts = clickCasts ?? throw new ArgumentNullException(nameof(clickCasts));
        _canAddRow = canAddRow ?? throw new ArgumentNullException(nameof(canAddRow));
        // Construction is not a change: the first reconcile is kept, but saved with the next change.
        _batch++;
        try
        {
            Apply(settings);
            ReconcileServerTabs();
        }
        finally
        {
            _batch--;
            _dirty = false;
        }
    }

    private void Apply(SpellDeckSettings settings)
    {
        SpellDeckSettings s = (settings ?? SpellDeckSettings.Default).Normalize();
        _tabs.Load(s.LayoutsAt(PerRow), s.LocalTabs.Count);
        _tab = s.Tab;
        _rowCount = s.RowCount;
        _attached = s.Attached;
        _detachedRows = s.DetachedRows;
        FitRows();
    }

    private SpellDeckSettings ToSettings() => new(_tab, _rowCount, _attached, _detachedRows, _tabs.Local, _tabs.Layouts);

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

    /// <summary>The current tab's layout.</summary>
    public SpellDeckLayout Layout => _tabs.Layout(_tab);

    /// <summary>A tab's layout; empty for a tab that does not exist.</summary>
    public SpellDeckLayout LayoutOf(int tab) => _tabs.Layout(tab);

    /// <summary>A tab's spells once each, in reading order (for a server tab, what the server list is kept equal to).</summary>
    public IReadOnlyList<uint> Favorites(int tab) => _tabs.Layout(tab).Distinct;

    /// <summary>The current tab's filled slots the deck shows (row below <see cref="RowCount"/>, column below <see cref="PerRow"/>), in reading order.</summary>
    public IReadOnlyList<SpellDeckSlot> ShownSlots
    {
        get
        {
            int rows = _rowCount, cols = PerRow;
            return [.. Layout.Filled.Where(f => f.Row < rows && f.Col < cols).Select(f => new SpellDeckSlot(f.Row, f.Col))];
        }
    }

    /// <summary>The spells in <see cref="ShownSlots"/> (a duplicate once per slot).</summary>
    public IReadOnlyList<uint> Visible
    {
        get
        {
            SpellDeckLayout layout = Layout;
            return [.. ShownSlots.Select(s => layout.Get(s.Row, s.Col))];
        }
    }

    /// <summary>The fewest slots per row that show every tab's right-most occupied column (server and local); feeds the minimum stretch.</summary>
    public int MinColumns => SpellDeckModel.MinColumns(_tabs.All);

    /// <summary>The fewest rows that show every tab's lowest occupied row (server and local).</summary>
    public int MinRows => SpellDeckModel.MinRows(_tabs.All);

    private SpellDeckMarkContext Context => new(_tab, ShownSlots, _itemSpell() is not null);

    /// <summary>Replaces the tab, rows, layouts and attachment (character change), and reconciles the server
    /// tabs; saves once only when that changed something. The outline is repaired.</summary>
    public void Load(SpellDeckSettings settings)
    {
        // A pending tab switch belongs to the character being left; the host flushes it
        // (FlushPendingSave) before its key changes, so here it is only dropped.
        _tabDirty = false;
        _dirty = false;
        Batch(() =>
        {
            Apply(settings);
            ReconcileServerTabs();
        });
        Outline = SpellDeckMark.None;
        Tick();
    }

    /// <summary>The spellbook's favourites changed (from elsewhere, or the echo of this deck's own ops):
    /// reconciles the server tabs. The echo finds nothing to do, so it sends and saves nothing. Call it only
    /// while the spellbook belongs to the character whose settings are loaded.</summary>
    public void ServerFavoritesChanged()
    {
        if (_tabs.IsSyncing) return;
        Batch(ReconcileServerTabs);
        Revalidate();
    }

    private void ReconcileServerTabs()
    {
        _tabs.Reconcile(Math.Max(_rowCount, MinRows), Math.Max(PerRow, MinColumns));
        if (FitRows()) Changed();
    }

    /// <summary>Adds rows until every tab's lowest occupied row is shown; true when it did.</summary>
    private bool FitRows()
    {
        int need = Math.Min(MinRows, SpellDeckGeometry.MaxRows);
        if (_rowCount >= need) return false;
        _rowCount = need;
        return true;
    }

    /// <summary>Re-checks the outline against the current tab, layout, rows, capacity and item.</summary>
    public void Tick()
    {
        Revalidate();
        if (_tabDirty && _batch == 0 && _seconds() - _lastTabSwitch >= TabSaveDelaySeconds) Save();
    }

    private void Revalidate() => Outline = SpellDeckMark.Validate(Outline, Context);

    /// <summary>Writes a tab switch still waiting for its delay, now. The host calls it before the
    /// character changes, while saves still go to the character being left.</summary>
    public void FlushPendingSave()
    {
        if (_tabDirty) Save();
    }

    // ── Command Deck ──────────────────────────────────────────────
    public bool CanCastOutlined => OutlinedSpellId is not null;
    public uint? OutlinedSpellId => Outline.Kind switch
    {
        SpellDeckMarkKind.Slot when Outline.Tab == _tab && Layout.Get(Outline.Row, Outline.Col) is uint id and not 0 => id,
        SpellDeckMarkKind.Item => _itemSpell()?.SpellId,
        _ => null,
    };
    public void CastOutlined()
    {
        switch (Outline.Kind)
        {
            case SpellDeckMarkKind.Slot when OutlinedSpellId is uint id: _actions.CastSpell(id); break;
            case SpellDeckMarkKind.Item when _itemSpell() is { } item: _actions.CastItemSpell(item.ItemId); break;
        }
    }

    // ── Tabs ──────────────────────────────────────────────────────

    /// <summary>Shows tab <paramref name="tab"/>; its first filled slot is outlined.</summary>
    public void ShowTab(int tab)
    {
        if (tab < 0 || tab >= _tabs.Count || tab == _tab) return;
        _tab = tab;
        Outline = SpellDeckMark.ShowTab(Context);
        _tabDirty = true;
        _lastTabSwitch = _seconds();
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

    /// <summary>Removes the last local tab and its layout; when it was shown, the tab before it is shown.</summary>
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
            if (SlotSpell(row, slot) is not null) { Outline = SpellDeckMark.Slot(_tab, row, slot); CastOutlined(); }
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

    /// <summary>The spell in slot (<paramref name="row"/>, <paramref name="col"/>) of the current tab, or null
    /// (an empty slot, a row not shown, or a column past the row's end).</summary>
    public uint? SlotSpell(int row, int col)
    {
        if (!IsShown(row, col)) return null;
        uint id = Layout.Get(row, col);
        return id == 0 ? null : id;
    }

    private bool IsShown(int row, int col) => row >= 0 && row < _rowCount && col >= 0 && col < PerRow;

    // ── Pointer ───────────────────────────────────────────────────
    public void Click(int row, int col)
    {
        if (SlotSpell(row, col) is null) return;
        Outline = SpellDeckMark.Slot(_tab, row, col);
        if (ClickCasts) CastOutlined();
    }

    public void DoubleClick(int row, int col)
    {
        if (ClickCasts || SlotSpell(row, col) is null) return;
        Outline = SpellDeckMark.Slot(_tab, row, col);
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

    /// <summary>The edit for a drop on shown slot (row, col) of the current tab; null off the shown slots or for a stale source.</summary>
    private SpellDeckEdit? PlanSlot(SpellDeckDragSource source, int row, int col, bool copy) =>
        IsShown(row, col) ? SpellDeckModel.PlanDropOnSlot(source, _tab, row, col, copy, _tabs.Layout) : null;

    private SpellDeckEdit? PlanTab(SpellDeckDragSource source, int tab, bool copy) =>
        tab < 0 || tab >= _tabs.Count ? null
        : SpellDeckModel.PlanDropOnTab(source, tab, copy, _rowCount, PerRow, _tabs.Layout);

    /// <summary>Whether a drop on (row, col) would be accepted, for the drag highlight.</summary>
    public bool CanDropOnSlot(SpellDeckDragSource source, int row, int col, bool copy) =>
        PlanSlot(source, row, col, copy)?.Accepted == true;

    /// <summary>Whether a drop on tab <paramref name="tab"/>'s bookmark would change something.</summary>
    public bool CanDropOnTab(SpellDeckDragSource source, int tab, bool copy) =>
        PlanTab(source, tab, copy) is { Accepted: true, Kind: not SpellDeckEditKind.None };

    /// <summary>A drop on slot (row, col) of the current tab: a spellbook spell is placed (empty slot) or replaces
    /// (filled slot); a slot's spell moves (empty) or swaps (filled); <paramref name="copy"/> (Ctrl held at release)
    /// copies a slot's spell instead.</summary>
    public void DropOnSlot(SpellDeckDragSource source, int row, int col, bool copy) => Run(PlanSlot(source, row, col, copy));

    /// <summary>A drop on a tab bookmark: into that tab's first empty slot (a slot's spell moves there, or with
    /// <paramref name="copy"/> is copied); <see cref="FullTabMessage"/> when it has none.</summary>
    public void DropOnTab(SpellDeckDragSource source, int tab, bool copy) => Run(PlanTab(source, tab, copy));

    /// <summary>A slot dragged off every window is cleared; a spellbook spell does nothing.</summary>
    public void DropOutside(SpellDeckDragSource source) => Run(SpellDeckModel.PlanClear(source, _tabs.Layout));

    /// <summary>Clears slot (row, col) of <paramref name="tab"/>, only while the deck still shows that tab with <paramref name="spellId"/> there.</summary>
    public void RemoveFromTab(int tab, int row, int col, uint spellId)
    {
        if (Shows(tab, row, col, spellId)) DropOutside(new SpellDeckDragSource(tab, row, col, spellId));
    }

    /// <summary>Examines <paramref name="spellId"/>, only while the deck still shows it in that slot of <paramref name="tab"/>.</summary>
    public void Examine(int tab, int row, int col, uint spellId)
    {
        if (Shows(tab, row, col, spellId)) _actions.ExamineSpell(spellId);
    }

    private bool Shows(int tab, int row, int col, uint spellId) => tab == _tab && SlotSpell(row, col) == spellId;

    /// <summary>The spellbook's "add to favourites": the current tab's first empty slot (<see cref="FullTabMessage"/>
    /// when none); a spell the deck already shows is only outlined (its first slot).</summary>
    public void AddFavoriteFromSpellbook(uint spellId)
    {
        SpellDeckLayout layout = Layout;
        foreach (SpellDeckSlot slot in ShownSlots)
            if (layout.Get(slot.Row, slot.Col) == spellId)
            {
                Outline = SpellDeckMark.Slot(_tab, slot.Row, slot.Col);
                Tick();
                return;
            }
        Run(PlanTab(SpellDeckDragSource.FromSpellbook(spellId), _tab, copy: false));
    }

    private void Run(SpellDeckEdit? edit)
    {
        if (edit is null) return;
        if (!edit.Accepted) { _actions.ShowMessage(FullTabMessage); return; }
        Batch(() => _tabs.Apply(edit.Layouts));
        Tick();
    }

    // ── Rows ──────────────────────────────────────────────────────
    public bool CanAddRow() => _rowCount < SpellDeckGeometry.MaxRows && _canAddRow();
    /// <summary>Remove row is allowed while no tab (server or local) holds a spell in the last row.</summary>
    public bool CanRemoveRow => SpellDeckModel.CanRemoveRow(_rowCount, _tabs.All);

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
        Revalidate();
        Save();
    }

    private void Save()
    {
        if (_batch > 0) { _dirty = true; return; }
        _tabDirty = false;
        _save(ToSettings());
    }

    /// <summary>Runs <paramref name="change"/> as one change: whatever it saves is saved once, at the end.</summary>
    private void Batch(Action change)
    {
        _batch++;
        try { change(); }
        finally { _batch--; }
        if (_batch == 0 && (_dirty || _tabDirty))
        {
            _dirty = false;
            Save();
        }
    }
}
