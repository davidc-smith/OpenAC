using AcDream.App.Spells;
using AcDream.UI.Abstractions.Input;

namespace AcDream.App.UI.Hud;

/// <summary>
/// The Spell Deck's live state (rows, outline) and behaviour: hotkeys,
/// clicks, drops and row edits, run through the pure model and outline rules.
/// Favourites are read through <c>favorites</c>; every change is sent through
/// <see cref="ISpellDeckActions"/> (which also updates the local spellbook).
/// </summary>
public sealed class SpellDeckController
{
    public const string FullRowMessage = "That spell bar row is full.";

    private readonly ISpellDeckActions _actions;
    private readonly Func<int, IReadOnlyList<uint>> _favorites;
    private readonly Func<ItemSpell?> _itemSpell;
    private readonly Func<int> _capacity;
    private readonly Action<SpellDeckSettings> _save;
    private readonly Func<bool> _clickCasts;
    private readonly Func<bool> _canAddRow;
    private readonly List<SpellDeckRow> _rows;
    private bool _attached;
    private int _nextRowId;

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
        _favorites = favorites ?? throw new ArgumentNullException(nameof(favorites));
        _itemSpell = itemSpell ?? throw new ArgumentNullException(nameof(itemSpell));
        _capacity = capacity ?? throw new ArgumentNullException(nameof(capacity));
        _save = save ?? throw new ArgumentNullException(nameof(save));
        _clickCasts = clickCasts ?? throw new ArgumentNullException(nameof(clickCasts));
        _canAddRow = canAddRow ?? throw new ArgumentNullException(nameof(canAddRow));
        SpellDeckSettings s = settings.Normalize();
        _rows = [.. s.Rows];
        _attached = s.Attached;
        _nextRowId = s.NextRowId;
    }

    public IReadOnlyList<SpellDeckRow> Rows => _rows;
    public SpellDeckOutline Outline { get; private set; }
    public bool Attached => _attached;
    public bool ClickCasts => _clickCasts();
    public ItemSpell? ItemSpell => _itemSpell();

    /// <summary>Replaces the rows and attachment (character change). The outline is repaired.</summary>
    public void Load(SpellDeckSettings settings)
    {
        SpellDeckSettings s = settings.Normalize();
        _rows.Clear(); _rows.AddRange(s.Rows);
        _attached = s.Attached; _nextRowId = s.NextRowId;
        Outline = SpellDeckOutline.None;
        Tick();
    }

    public IReadOnlyList<uint> Visible(int tab) => SpellDeckModel.Visible(_favorites(tab), _capacity());
    public IReadOnlyList<uint> Favorites(int tab) => _favorites(tab);

    /// <summary>The fewest slots per row the deck may show: its fullest row.</summary>
    public int MinSlots => SpellDeckModel.MinSlots(_rows, _favorites);

    private SpellDeckOutlineContext Context => new(_rows, Visible, _itemSpell() is not null);

    /// <summary>Re-checks the outline against the current favourites, rows, capacity and item.</summary>
    public void Tick() => Outline = SpellDeckOutline.Validate(Outline, Context);

    // ── Command Deck ──────────────────────────────────────────────
    public bool CanCastOutlined => Outline.Kind != SpellDeckOutlineKind.None;
    public uint? OutlinedSpellId => Outline.Kind switch
    {
        SpellDeckOutlineKind.Slot => Outline.SpellId,
        SpellDeckOutlineKind.Item => _itemSpell()?.SpellId,
        _ => null,
    };
    public void CastOutlined()
    {
        switch (Outline.Kind)
        {
            case SpellDeckOutlineKind.Slot: _actions.CastSpell(Outline.SpellId); break;
            case SpellDeckOutlineKind.Item when _itemSpell() is { } item: _actions.CastItemSpell(item.ItemId); break;
        }
    }

    // ── Hotkeys ───────────────────────────────────────────────────
    public bool Handle(InputAction action)
    {
        if (SpellBarActions.TryParse(action, out int row, out int slot))
        {
            if (SlotSpell(row, slot) is { } hit) { Outline = hit; CastOutlined(); }
            return true;
        }
        switch (action)
        {
            case InputAction.CastItemSpell: ClickItem(cast: true); return true;
            case InputAction.CombatPrevSpellTab: SetRow0Tab(t => (t + 7) % 8); return true;
            case InputAction.CombatNextSpellTab: SetRow0Tab(t => (t + 1) % 8); return true;
            case InputAction.CombatFirstSpellTab: SetRow0Tab(_ => 0); return true;
            case InputAction.CombatLastSpellTab: SetRow0Tab(_ => 7); return true;
            case InputAction.CombatPrevSpell: Outline = SpellDeckOutline.Prev(Outline, Context); return true;
            case InputAction.CombatNextSpell: Outline = SpellDeckOutline.Next(Outline, Context); return true;
            case InputAction.CombatFirstSpell: Outline = SpellDeckOutline.First(Outline, Context); return true;
            case InputAction.CombatLastSpell: Outline = SpellDeckOutline.Last(Outline, Context); return true;
            case InputAction.CombatCastCurrentSpell: CastOutlined(); return true;
            default: return false;
        }
    }

    private void SetRow0Tab(Func<int, int> next)
    {
        if (_rows.Count == 0) return;
        SetRowTab(_rows[0].Id, next(_rows[0].Tab));
    }

    private SpellDeckOutline? SlotSpell(int row, int slot)
    {
        if (row >= _rows.Count) return null;
        IReadOnlyList<uint> spells = Visible(_rows[row].Tab);
        return slot < spells.Count ? SpellDeckOutline.ForSlot(_rows[row].Id, _rows[row].Tab, spells[slot]) : null;
    }

    // ── Pointer ───────────────────────────────────────────────────
    public void Click(int row, int slot)
    {
        if (SlotSpell(row, slot) is not { } hit) return;
        Outline = hit;
        if (ClickCasts) CastOutlined();
    }

    public void DoubleClick(int row, int slot)
    {
        if (ClickCasts || SlotSpell(row, slot) is not { } hit) return;
        Outline = hit;
        CastOutlined();
    }

    public void ClickItem() => ClickItem(cast: ClickCasts);
    public void DoubleClickItem() { if (!ClickCasts) ClickItem(cast: true); }

    private void ClickItem(bool cast)
    {
        if (_itemSpell() is null) return;
        Outline = SpellDeckOutline.Item;
        if (cast) CastOutlined();
    }

    /// <summary>Whether a drop of <paramref name="source"/> on (row, slot) would be accepted, for the drag highlight.</summary>
    public bool CanDrop(SpellDeckDragSource source, int row, int slot) =>
        row < _rows.Count
        && SpellDeckModel.PlanDrop(source, _rows[row].Tab, slot, _favorites, _capacity()).Accepted;

    public void Drop(SpellDeckDragSource source, int row, int slot)
    {
        if (row >= _rows.Count) return;
        SpellDeckRow dest = _rows[row];
        SpellDeckDropPlan plan = SpellDeckModel.PlanDrop(source, dest.Tab, slot, _favorites, _capacity());
        if (!plan.Accepted) { _actions.ShowMessage(FullRowMessage); return; }
        bool wasOutlined = Outline.Kind == SpellDeckOutlineKind.Slot && Outline.SpellId == source.SpellId;
        Send(plan);
        if (wasOutlined || plan.Kind == SpellDeckDropKind.Move)
            Outline = SpellDeckOutline.ForSlot(dest.Id, dest.Tab, source.SpellId);
        Tick();
    }

    public void DropOutside(SpellDeckDragSource source)
    {
        if (source.IsFromSpellbook) return;
        Send(SpellDeckModel.PlanRemove(source));
        Tick();
    }

    public void RemoveFromRow(int row, int slot)
    {
        if (SlotSpell(row, slot) is { } hit) DropOutside(new SpellDeckDragSource(hit.Tab, hit.SpellId));
    }

    public void Examine(int row, int slot)
    {
        if (SlotSpell(row, slot) is { } hit) _actions.ExamineSpell(hit.SpellId);
    }

    private void Send(SpellDeckDropPlan plan)
    {
        foreach (SpellDeckFavoriteOp op in plan.Ops)
            if (op.IsRemove) _actions.RemoveFavorite(op.Tab, op.SpellId);
            else _actions.SetFavorite(op.Tab, op.Position, op.SpellId);
    }

    /// <summary>The spellbook's "add to favourites": the outlined row's tab (row 0 when nothing is outlined), at the end.</summary>
    public void AddFavoriteFromSpellbook(uint spellId)
    {
        int index = Outline.Kind == SpellDeckOutlineKind.Slot ? Math.Max(0, Context.IndexOfRow(Outline.RowId)) : 0;
        if (index >= _rows.Count) return;
        SpellDeckRow row = _rows[index];
        IReadOnlyList<uint> tab = _favorites(row.Tab);
        if (tab.Contains(spellId)) { Outline = SpellDeckOutline.ForSlot(row.Id, row.Tab, spellId); Tick(); return; }
        Drop(SpellDeckDragSource.FromSpellbook(spellId), index, tab.Count);
    }

    // ── Rows ──────────────────────────────────────────────────────
    public bool CanAddRow() => _rows.Count < SpellDeckGeometry.MaxRows && _canAddRow();
    public bool CanRemoveRow => _rows.Count > 1;

    public void AddRowBelow(int rowId)
    {
        if (!CanAddRow()) return;
        int at = Math.Max(0, Context.IndexOfRow(rowId)) + 1;
        _rows.Insert(Math.Min(at, _rows.Count), new SpellDeckRow(_nextRowId++, SpellDeckModel.TabForNewRow(_rows)));
        Changed();
    }

    public void RemoveRow(int rowId)
    {
        int at = Context.IndexOfRow(rowId);
        if (at < 0 || !CanRemoveRow) return;
        _rows.RemoveAt(at);
        Changed();
    }

    public void SetRowTab(int rowId, int tab)
    {
        int at = Context.IndexOfRow(rowId);
        if (at < 0 || tab is < 0 or >= SpellDeckModel.Tabs || _rows[at].Tab == tab) return;
        _rows[at] = _rows[at] with { Tab = tab };
        Changed();
    }

    public void SetAttached(bool attached)
    {
        if (_attached == attached) return;
        _attached = attached;
        Changed();
    }

    private void Changed()
    {
        Tick();
        _save(new SpellDeckSettings([.. _rows], _attached));
    }
}
