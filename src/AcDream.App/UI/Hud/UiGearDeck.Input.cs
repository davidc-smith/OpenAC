using AcDream.App.UI.Layout;
using AcDream.Core.Items;
using AcDream.Core.Items.Gear;
using AcDream.Runtime.Gameplay;

namespace AcDream.App.UI.Hud;

internal sealed partial class UiGearDeck
{
    private static readonly GearDeckHit NoHit = new(GearZone.None);

    private readonly Func<double> _clock;
    /// <summary>The bindings' actions, noting what the dispatcher sent for the drop log line.</summary>
    private readonly LoggedGearItemActions _actions;
    /// <summary>The table <see cref="_dispatcher"/> was built over; a new table rebuilds it.</summary>
    private ClientObjectTable _dispatcherTable;
    private GearDropDispatcher _dispatcher;
    /// <summary>The zone under the last hit test; decides click and drag ownership.</summary>
    private GearDeckHit _hit = NoHit;
    /// <summary>Where the last press landed; the drag lifts the item under it.</summary>
    private GearDeckHit _press = NoHit;
    /// <summary>When the pointer reached <see cref="Hover"/>; the tooltip waits <see cref="UiCommandDeck.TooltipDwell"/> from here.</summary>
    private double _hoverSince;
    /// <summary>Set by a press; keeps the tooltip hidden until the pointer moves to another zone.</summary>
    private bool _tooltipSuppressed;
    /// <summary>The payload of a drag over the deck (DragEnter to leave or drop); null otherwise.</summary>
    private object? _dropPayload;
    private readonly HudTooltipLayoutCache _tooltipLayout = new();
    private (GearDeckHit Hit, ulong Revision, uint Pack, int QueryVersion, HudTooltipContent? Content)? _tooltipCache;

    /// <summary>Every drop and take-off goes through the one dispatcher, over the current table.</summary>
    private GearDropDispatcher Dispatcher
    {
        get
        {
            ClientObjectTable table = _b.Objects();
            if (!ReferenceEquals(table, _dispatcherTable))
            {
                _dispatcherTable = table;
                _dispatcher = new GearDropDispatcher(table, _b.PlayerGuid, _actions);
            }
            return _dispatcher;
        }
    }

    /// <summary>The zones a press acts on. Empty frame space is not one, so a press there drags the window.</summary>
    private static bool IsControl(GearZone zone) => zone is not (GearZone.None or GearZone.Frame);

    public override bool HandlesClick => IsControl(_hit.Zone);
    public override bool ReceivesHoverMouseMove => true;
    protected override bool ExpandsClipForPopup => true;

    internal bool HitTestLocal(float x, float y) => OnHitTest(x, y);

    protected override bool OnHitTest(float localX, float localY)
    {
        _hit = _g.HitAt(localX, localY);
        // A control owns its press, so it never drags the window; a drag source must not
        // claim it, or a locked UI (which skips the drag-source check) never starts the drag.
        CapturesPointerDrag = IsControl(_hit.Zone) && DragItem(_hit) is null;
        return _hit.Zone != GearZone.None;
    }

    public override bool OnEvent(in UiEvent e)
    {
        switch (e.Type)
        {
            case UiEventType.MouseDown:
                _tooltipSuppressed = true;
                _press = _g.HitAt(e.Data1, e.Data2);
                _starCloses = _namingClosedByStar && _press.Zone == GearZone.SaveStud;   // UiGearDeck.Query.cs
                _namingClosedByStar = false;
                // Empty contents space is no control, so no Click follows it: clear on the press.
                if (_press.Zone == GearZone.Frame && _g.Contents.Contains(e.Data1, e.Data2)) ClearSelection();
                // The root took the focus from the field for this press; a press on a suggestion
                // or the field gives it back, so the rows stay up until the click acts.
                if (_press.Zone is GearZone.Suggestion or GearZone.QueryField)
                    FindRoot()?.SetKeyboardFocus(QueryInput);
                return false;   // the window drag, the item drag or the click has the press
            case UiEventType.Click:
                return ReleasedOnPress(e, out GearDeckHit clicked) ? Click(clicked) : IsControl(clicked.Zone);
            case UiEventType.DoubleClick:
                return ReleasedOnPress(e, out GearDeckHit doubled) ? DoubleClick(doubled) : IsControl(doubled.Zone);
            case UiEventType.MouseMove:
                SetHover(_g.HitAt(e.Data1, e.Data2));
                return Hover.Zone != GearZone.None;
            case UiEventType.HoverLeave:
                // The root stops hit-testing the deck once the pointer is elsewhere, so the last hit is stale.
                _hit = NoHit;
                SetHover(NoHit);
                return true;
            case UiEventType.Scroll:
                if (!_g.Contents.Contains(e.Data1, e.Data2)) return false;
                ScrollTo(_scrollRow - e.Data0);   // Data0 is +1 per notch up
                SetHover(_g.HitAt(e.Data1, e.Data2));
                return true;
            case UiEventType.DragBegin:
                if (e.Payload is ItemDragPayload lifted)
                    _b.Log?.Invoke($"[gear-deck] drag 0x{lifted.ObjId:X8} from {_press.Zone} {_press.Index}");
                return false;
            case UiEventType.DragEnter:
                _dropPayload = e.Payload;
                SetDropHighlight(e.Data1, e.Data2);
                return false;
            case UiEventType.DragOver:   // the leave
                ClearDropHighlight();
                return false;
            case UiEventType.DropReleased:
                ClearDropHighlight();
                return DropReleased(e.Payload, e.Data1, e.Data2);
            default:
                return false;
        }
    }

    // ---- selection ----

    private readonly HashSet<uint> _selection = [];
    /// <summary>The <see cref="Shown"/> index a range click spans from, or -1.</summary>
    private int _anchor = -1;
    /// <summary>The anchor row's id; <see cref="_anchor"/> is re-found from it when <see cref="Shown"/> is rebuilt,
    /// so a pack or query change that drops the row drops the anchor.</summary>
    private uint _anchorId;
    private int _selectionVersion;

    /// <summary>The selected item ids; always a subset of <see cref="Shown"/>.</summary>
    internal IReadOnlyCollection<uint> Selection
    {
        get
        {
            _ = Shown;   // a stale list prunes here, so the ids read are always shown ones
            return _selection;
        }
    }

    /// <summary>Bumped on every selection change, for the caches that follow it.</summary>
    internal int SelectionVersion => _selectionVersion;

    private (bool Toggle, bool Range) ReadModifiers()
    {
        if (_b.Modifiers is { } modifiers) return modifiers();
        return FindRoot() is { } root ? (root.CopyModifierHeld, root.ShiftHeld) : (false, false);
    }

    private void ClearSelection()
    {
        _anchor = -1;
        _anchorId = 0u;
        if (_selection.Count == 0) return;
        _selection.Clear();
        _selectionVersion++;
    }

    private void SelectOnly(int index)
    {
        _selection.Clear();
        _selection.Add(Shown[index].Id);
        SetAnchor(index);
        _selectionVersion++;
    }

    private void SetAnchor(int index)
    {
        _anchor = index;
        _anchorId = index >= 0 ? Shown[index].Id : 0u;
    }

    /// <summary>Drops the ids a rebuilt <see cref="Shown"/> no longer holds.</summary>
    private void PruneSelection(IReadOnlyList<GearItemView> rows)
    {
        _anchor = -1;
        if (_anchorId != 0u)
            for (int i = 0; i < rows.Count; i++)
                if (rows[i].Id == _anchorId) { _anchor = i; break; }
        if (_anchor < 0) _anchorId = 0u;
        if (_selection.Count == 0) return;
        int before = _selection.Count;
        var live = new HashSet<uint>(rows.Count);
        foreach (GearItemView row in rows) live.Add(row.Id);
        _selection.IntersectWith(live);
        if (_selection.Count != before) _selectionVersion++;
    }

    /// <summary>A click on an item: plain selects only it, toggle adds or removes it, range selects
    /// everything between the anchor and it.</summary>
    private void ClickItem(int index)
    {
        (bool toggle, bool range) = ReadModifiers();
        IReadOnlyList<GearItemView> rows = Shown;
        if (range && _anchor >= 0 && _anchor < rows.Count)
        {
            _selection.Clear();
            for (int i = Math.Min(_anchor, index); i <= Math.Max(_anchor, index); i++) _selection.Add(rows[i].Id);
            _selectionVersion++;
        }
        else if (toggle)
        {
            uint id = rows[index].Id;
            if (!_selection.Remove(id)) _selection.Add(id);
            SetAnchor(index);
            _selectionVersion++;
        }
        else SelectOnly(index);
    }

    private void SelectAll()
    {
        _selection.Clear();
        foreach (GearItemView row in Shown) _selection.Add(row.Id);
        SetAnchor(Shown.Count > 0 ? 0 : -1);
        _selectionVersion++;
    }

    /// <summary>The menu's Show in pack: leaves the search (keeping the sort, which orders the pack view too),
    /// opens the row's pack, scrolls to the row where that order shows it and selects it.</summary>
    private void ShowInPack(GearItemView row)
    {
        SetQuery(GearQuery.Empty with { Sort = _query.Sort });
        QueryInput.SetText("");
        foreach (GearPack pack in _model.Packs)
            if (pack.Id == row.PackId) { SelectPack(pack); break; }
        IReadOnlyList<GearItemView> rows = Shown;
        for (int i = 0; i < rows.Count; i++)
        {
            if (rows[i].Id != row.Id) continue;
            bool list = _b.Session.Settings.View == GearPackView.List;
            int line = list ? i : i / GearDeckGeometry.Columns;
            if (line < _scrollRow || line >= _scrollRow + Math.Max(1, VisibleRows)) ScrollTo(line);
            SelectOnly(i);
            return;
        }
        ClearSelection();
    }

    // ---- clicks ----

    /// <summary>A click acts on the zone it was pressed on only: a press on one stud
    /// released over another (the close box, say) does nothing.</summary>
    private bool ReleasedOnPress(in UiEvent e, out GearDeckHit hit)
    {
        hit = _g.HitAt(e.Data1, e.Data2);
        return hit == _press;
    }

    private bool Click(GearDeckHit hit)
    {
        switch (hit.Zone)
        {
            case GearZone.Close:
                _b.Close();
                return true;
            case GearZone.FoldStud:
                _b.Session.Update(s => s with { Folded = !s.Folded });
                ResetFigurePose();
                ScrollTo(_scrollRow);
                return true;
            case GearZone.SlotsStud:
                _b.Session.Update(s => s with { Folded = false, Equip = GearEquipView.Slots });
                ResetFigurePose();
                ScrollTo(_scrollRow);
                return true;
            case GearZone.FigureStud:
                if (!_figureAttached) return true;   // dim without a figure
                _b.Session.Update(s => s with { Folded = false, Equip = GearEquipView.Figure });
                ScrollTo(_scrollRow);
                return true;
            case GearZone.GridStud:
                SetView(GearPackView.Grid);
                return true;
            case GearZone.ListStud:
                SetView(GearPackView.List);
                return true;
            case GearZone.SidePack when hit.Index < _model.Packs.Count:
                if (ShowsResults) TogglePackChip(_model.Packs[hit.Index]);
                else SelectPack(_model.Packs[hit.Index]);
                return true;
            case GearZone.Item when hit.Index >= 0 && hit.Index < Shown.Count:
                ClickItem(hit.Index);
                return true;
            case GearZone.FooterButton:
                AddTargetsToSalvage();
                return true;
            case GearZone.Chip when hit.Index < _query.Chips.Count:
                SetQuery(_query.Without(hit.Index));
                return true;
            case GearZone.ChipMore when _g.ChipMore is { } more:
                OpenMenuAt(more);   // UiGearDeck.Menu.cs
                return true;
            case GearZone.SortStud:
                OpenMenuAt(_g.SortStud);
                return true;
            case GearZone.DirStud:
                if (!DirStudDim) SetQuery(_query with { Sort = _query.Sort with { Descending = !_query.Sort.Descending } });
                return true;
            case GearZone.SaveStud:
                if (!_starCloses) StartSave();   // UiGearDeck.Query.cs; a press that closed the name field only closes it
                _starCloses = false;
                return true;
            case GearZone.SavedFilter:
                ClickSaved(hit.Index);
                return true;
            case GearZone.SavedMore when _g.SavedMore is { } savedMore:
                OpenMenuAt(savedMore);
                return true;
            case GearZone.Suggestion:
                TakeSuggestion(hit.Index);
                return true;
            case GearZone.QueryField:   // the field child normally takes the click itself
                FindRoot()?.SetKeyboardFocus(QueryInput);
                return true;
            default:
                return IsControl(hit.Zone);
        }
    }

    /// <summary>A grid row and a list row hold different items, so a view change starts at the top.</summary>
    private void SetView(GearPackView view)
    {
        if (_b.Session.Settings.View == view) return;
        _b.Session.Update(s => s with { View = view });
        ScrollTo(0);
    }

    /// <summary>Shows a pack's contents from the top. A side pack is opened on the server too,
    /// as retail's OpenContainer does; the main pack is the player and needs no open. The
    /// pack already shown is left as it is (OpenContainer returns early for the open one),
    /// so a double-click sends one Use and keeps the scroll.</summary>
    private void SelectPack(GearPack pack)
    {
        if (pack.Id == SelectedPackId) return;
        _b.Session.Update(s => s with { SelectedPack = pack.Id });
        ScrollTo(0);
        if (!pack.IsMain) _b.Actions.OpenPack(pack.Id);
    }

    /// <summary>While results are shown a pack cell narrows the search: it adds a Pack chip
    /// for that pack, or removes the one it has. Several are OR'd; none means every pack.</summary>
    private void TogglePackChip(GearPack pack)
    {
        int i = -1;
        for (int c = 0; c < _query.Chips.Count; c++)
            if (_query.Chips[c] is { Field: GearField.Pack } chip && chip.Id == pack.Id) { i = c; break; }
        SetQuery(i >= 0 ? _query.Without(i) : _query.With(GearChip.InPack(pack.Id)));
    }

    /// <summary>Whether the query has a Pack chip for <paramref name="packId"/> (the side column outlines it).</summary>
    private bool HasPackChip(uint packId)
    {
        IReadOnlyList<GearChip> chips = _query.Chips;
        for (int i = 0; i < chips.Count; i++)
            if (chips[i].Field == GearField.Pack && chips[i].Id == packId) return true;
        return false;
    }

    private bool DoubleClick(GearDeckHit hit)
    {
        switch (hit.Zone)
        {
            case GearZone.Item when hit.Index >= 0 && hit.Index < Shown.Count:
                GearItemView row = Shown[hit.Index];
                // A toggle double-click's two clicks added the row and took it out again; the row used stays selected.
                if (_selection.Add(row.Id))
                {
                    SetAnchor(hit.Index);
                    _selectionVersion++;
                }
                if (row.Worn) Dispatcher.TakeOff(row.Id);   // a worn result comes off, as its slot does
                else _b.Actions.Activate(row.Id);
                return true;
            case GearZone.SidePack when ShowsResults && hit.Index < _model.Packs.Count:
                // Each click toggled the pack's chip, so the two cancelled out: a double-click toggles it once.
                TogglePackChip(_model.Packs[hit.Index]);
                return true;
            case GearZone.EquipSlot or GearZone.WornCell:
                if (WornAt(hit) is uint worn) Dispatcher.TakeOff(worn);
                return true;
            default:
                return IsControl(hit.Zone);   // each click already acted
        }
    }

    /// <summary>The item worn in an equipment slot or folded worn cell, or null.</summary>
    private uint? WornAt(GearDeckHit hit)
    {
        if (hit.Index < 0) return null;
        if (hit.Zone == GearZone.EquipSlot && hit.Index < GearSlotCatalog.Slots.Count)
            return SlotOccupant(hit.Index) is var id && id != 0u ? id : null;
        if (hit.Zone == GearZone.WornCell && hit.Index < _model.Worn.Count)
            return _model.Worn[hit.Index].ObjectId;
        return null;
    }

    // ---- drag source ----

    /// <summary>The item a press on <paramref name="hit"/> would lift, and where it leaves from.
    /// A side pack lifts like an item (to the ground, or onto the grid); the main pack is the player.
    /// A worn result row lifts as equipment, as its slot does.</summary>
    private (uint Id, ItemDragSource Kind)? DragItem(GearDeckHit hit)
    {
        if (hit.Zone == GearZone.Item && hit.Index >= 0 && hit.Index < Shown.Count)
        {
            GearItemView row = Shown[hit.Index];
            return (row.Id, row.Worn ? ItemDragSource.Equipment : ItemDragSource.Inventory);
        }
        if (hit.Zone == GearZone.SidePack && hit.Index >= 1 && hit.Index < _model.Packs.Count
            && !_model.Packs[hit.Index].IsMain)
            return (_model.Packs[hit.Index].Id, ItemDragSource.Inventory);
        return WornAt(hit) is uint worn ? (worn, ItemDragSource.Equipment) : null;
    }

    // _hit is the hit test of the press itself: the root asks before it sends the MouseDown.
    public override bool IsDragSource => DragItem(_hit) is not null;

    public override object? GetDragPayload() =>
        DragItem(_press) is { } lifted ? new ItemDragPayload(lifted.Id, lifted.Kind, _press.Index, null) : null;

    public override (uint tex, int w, int h)? GetDragGhost()
    {
        if (GetDragPayload() is not ItemDragPayload p || _b.Objects().Get(p.ObjId) is not { } o) return null;
        return (_b.ResolveDragIcon(o.Type, o.IconId, o.IconUnderlayId, o.IconOverlayId, o.Effects), 32, 32);
    }

    // ---- drop target ----

    /// <summary>An item payload the deck takes; a shortcut leaving the bar is not an item move.</summary>
    private static ItemDragPayload? ItemPayload(object? payload) =>
        payload is ItemDragPayload { SourceKind: not ItemDragSource.ShortcutBar, ObjId: not 0u } p ? p : null;

    /// <summary>What a drop at a point lands on: a pack, an item, an equipment slot or worn
    /// cell, or (as <see cref="GearZone.Frame"/>) the empty contents area; null anywhere else.
    /// Results come from many packs, so their empty space is not a target.</summary>
    private GearDeckHit? DropTargetAt(float x, float y)
    {
        if (_model.Packs.Count == 0) return null;
        GearDeckHit hit = _g.HitAt(x, y);
        return hit.Zone switch
        {
            GearZone.SidePack when hit.Index < _model.Packs.Count => hit,
            GearZone.Item when hit.Index >= 0 && hit.Index < Shown.Count => hit,
            GearZone.EquipSlot when hit.Index < GearSlotCatalog.Slots.Count => hit,
            GearZone.WornCell when hit.Index < _model.Worn.Count => hit,
            GearZone.Frame when !ShowsResults && _g.Contents.Contains(x, y) => hit,
            _ => null,
        };
    }

    /// <summary>Where a worn item is worn, for a drop on its result row.</summary>
    private EquipMask WornMask(uint id) => _b.Objects().Get(id)?.CurrentlyEquippedLocation ?? EquipMask.None;

    /// <summary>The slot mask a drop on an equipment slot or worn cell asks for.</summary>
    private EquipMask SlotMaskAt(GearDeckHit hit) => hit.Zone == GearZone.EquipSlot
        ? GearSlotCatalog.Slots[hit.Index].Mask
        : _model.Worn[hit.Index].CurrentlyEquippedLocation;

    private bool DropReleased(object? payload, float x, float y)
    {
        if (ItemPayload(payload) is not { } p || DropTargetAt(x, y) is not { } hit) return false;
        bool fromGround = p.SourceKind == ItemDragSource.Ground;
        _actions.Last = null;
        string action;
        switch (hit.Zone)
        {
            case GearZone.SidePack:
                action = nameof(GearDropDispatcher.DropOnPack);
                Dispatcher.DropOnPack(p.ObjId, _model.Packs[hit.Index].Id, fromGround);
                break;
            case GearZone.Item:
            {
                // Every row names its own pack and slot (a sorted pack view's index is not the
                // slot), or is a side pack or a worn item.
                GearItemView row = Shown[hit.Index];
                if (row.Worn)
                {
                    action = nameof(GearDropDispatcher.DropOnSlot);
                    Dispatcher.DropOnSlot(p.ObjId, WornMask(row.Id));
                }
                else if (row.IsPack)
                {
                    action = nameof(GearDropDispatcher.DropOnPack);
                    Dispatcher.DropOnPack(p.ObjId, row.Id, fromGround);
                }
                else
                {
                    action = nameof(GearDropDispatcher.DropOnCell);
                    Dispatcher.DropOnCell(p.ObjId, row.PackId, row.Id, row.Slot, fromGround);
                }
                break;
            }
            case GearZone.Frame:   // empty grid space: after the last item, as retail's list does
                action = nameof(GearDropDispatcher.DropOnCell);
                Dispatcher.DropOnCell(p.ObjId, SelectedPackId, 0u, -1, fromGround);
                break;
            default:   // EquipSlot or WornCell
                action = nameof(GearDropDispatcher.DropOnSlot);
                Dispatcher.DropOnSlot(p.ObjId, SlotMaskAt(hit));
                break;
        }
        _b.Log?.Invoke($"[gear-deck] drop 0x{p.ObjId:X8} on {hit.Zone} {hit.Index} -> {action} {_actions.Last ?? "nothing sent"}");
        return true;
    }

    private void SetDropHighlight(float x, float y)
    {
        if (ItemPayload(_dropPayload) is not { } p || DropTargetAt(x, y) is not { } hit)
        {
            DropHighlight = null;
            return;
        }
        // A slot shows whether the item can go there; a pack or cell always takes the drop
        // (the dispatcher falls through to a pack with room, or reports why not).
        bool accept = hit.Zone is not (GearZone.EquipSlot or GearZone.WornCell)
            || (_b.Objects().Get(p.ObjId) is { } item
                && (ItemEquipRules.IsOffHandWeaponDrop(item, SlotMaskAt(hit))
                    || ItemEquipRules.ResolvePaperdollDropWieldMask(item, SlotMaskAt(hit)) != EquipMask.None));
        DropHighlight = (hit, accept);
    }

    private void ClearDropHighlight()
    {
        _dropPayload = null;
        DropHighlight = null;
    }

    /// <summary>The root sends DragEnter once per element, so the highlight follows the
    /// pointer from target to target here; a drag that ended without a leave clears it.</summary>
    private void TrackDropHighlight()
    {
        if (_dropPayload is null) return;
        if (FindRoot() is not { } root || root.DragSource is null)
        {
            ClearDropHighlight();
            return;
        }
        System.Numerics.Vector2 origin = ScreenPosition;
        SetDropHighlight(root.MouseX - origin.X, root.MouseY - origin.Y);
    }

    // ---- hover and tooltips ----

    private void SetHover(GearDeckHit hit)
    {
        if (!IsControl(hit.Zone)) hit = NoHit;
        if (hit == Hover) return;
        Hover = hit;
        _hoverSince = _clock();
        _tooltipSuppressed = false;
    }

    private void ClearHover()
    {
        SetHover(NoHit);
        _tooltipSuppressed = false;
        _press = NoHit;
        ClearDropHighlight();
    }

    /// <summary>
    /// The tooltip the deck is showing now, or null. The deck is one element
    /// with many zones, so it times its own tooltip, as the Command Deck does.
    /// </summary>
    internal HudTooltipContent? VisibleTooltip =>
        Hover.Zone == GearZone.None || _tooltipSuppressed || _dropPayload is not null
            || _clock() - _hoverSince < UiCommandDeck.TooltipDwell
            ? null
            : CachedTooltip(Hover);

    private HudTooltipContent? CachedTooltip(GearDeckHit hit)
    {
        uint pack = SelectedPackId;
        if (_tooltipCache is { } c && c.Hit == hit && c.Revision == _lastRevision && c.Pack == pack
            && c.QueryVersion == _queryVersion) return c.Content;
        HudTooltipContent? content = TooltipFor(hit) is { } text ? new HudTooltipContent(text, null, null, null) : null;
        _tooltipCache = (hit, _lastRevision, pack, _queryVersion, content);
        return content;
    }

    /// <summary>An item's caption (the selection caption's name, with its stack), or an empty slot's label.</summary>
    private string? TooltipFor(GearDeckHit hit)
    {
        if (hit.Index < 0) return null;
        switch (hit.Zone)
        {
            case GearZone.Item when hit.Index < Shown.Count:
                return Caption(Shown[hit.Index].Id);
            case GearZone.EquipSlot when hit.Index < GearSlotCatalog.Slots.Count:
                return WornAt(hit) is uint worn ? Caption(worn) : GearSlotCatalog.Slots[hit.Index].Label;
            case GearZone.WornCell when WornAt(hit) is uint cell:
                return Caption(cell);
            case GearZone.SidePack when hit.Index < _model.Packs.Count && !_model.Packs[hit.Index].IsMain:
                return Caption(_model.Packs[hit.Index].Id);
            case GearZone.Chip:
                return "Click to remove";
            default:
                return null;
        }
    }

    private string? Caption(uint id) => ItemTooltipCaption.Resolve(_b.Objects(), id, _b.DisplayName);

    protected override void OnDrawOverlay(UiRenderContext ctx)
    {
        DrawSuggestions(ctx);   // UiGearDeck.Draw.cs: above the grid, below the tooltip
        if (VisibleTooltip is not { } tip || RectFor(Hover) is not { } anchor) return;
        ctx.PushAlphaAbsolute(1f);
        try
        {
            HudTooltip.Draw(ctx, tip, HudTooltipStyle.Compact, anchor,
                ScreenPosition, FindRoot()?.Width, _tooltipLayout, _fonts, _scale, _textFactor);
        }
        finally { ctx.PopAlpha(); }
    }
}
