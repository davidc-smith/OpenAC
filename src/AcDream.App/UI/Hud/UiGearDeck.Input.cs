using AcDream.App.UI.Layout;
using AcDream.Core.Items;
using AcDream.Core.Items.Gear;
using AcDream.Runtime.Gameplay;

namespace AcDream.App.UI.Hud;

internal sealed partial class UiGearDeck
{
    private static readonly GearDeckHit NoHit = new(GearZone.None);

    private readonly Func<double> _clock;
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
    private (GearDeckHit Hit, ulong Revision, uint Pack, HudTooltipContent? Content)? _tooltipCache;

    /// <summary>Every drop and take-off goes through the one dispatcher, over the current table.</summary>
    private GearDropDispatcher Dispatcher
    {
        get
        {
            ClientObjectTable table = _b.Objects();
            if (!ReferenceEquals(table, _dispatcherTable))
            {
                _dispatcherTable = table;
                _dispatcher = new GearDropDispatcher(table, _b.PlayerGuid, _b.Actions);
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
        CapturesPointerDrag = IsControl(_hit.Zone);
        return _hit.Zone != GearZone.None;
    }

    public override bool OnEvent(in UiEvent e)
    {
        switch (e.Type)
        {
            case UiEventType.MouseDown:
                _tooltipSuppressed = true;
                _press = _g.HitAt(e.Data1, e.Data2);
                return false;   // the window drag, the item drag or the click has the press
            case UiEventType.Click:
                return ReleasedOnPress(e, out GearDeckHit clicked) ? Click(clicked) : IsControl(clicked.Zone);
            case UiEventType.DoubleClick:
                return ReleasedOnPress(e, out GearDeckHit doubled) ? DoubleClick(doubled) : IsControl(doubled.Zone);
            case UiEventType.MouseMove:
                SetHover(_g.HitAt(e.Data1, e.Data2));
                return Hover.Zone != GearZone.None;
            case UiEventType.HoverLeave:
                SetHover(NoHit);
                return true;
            case UiEventType.Scroll:
                if (!_g.Contents.Contains(e.Data1, e.Data2)) return false;
                ScrollTo(_scrollRow - e.Data0);   // Data0 is +1 per notch up
                SetHover(_g.HitAt(e.Data1, e.Data2));
                return true;
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
                ScrollTo(_scrollRow);
                return true;
            case GearZone.SlotsStud:   // Slots is plan 1's only expanded view
                _b.Session.Update(s => s with { Folded = false });
                ScrollTo(_scrollRow);
                return true;
            case GearZone.GridStud:
                SetView(GearPackView.Grid);
                return true;
            case GearZone.ListStud:
                SetView(GearPackView.List);
                return true;
            case GearZone.SidePack when hit.Index < _model.Packs.Count:
                SelectPack(_model.Packs[hit.Index]);
                return true;
            case GearZone.FigureStud:   // Figure is plan 3
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

    private bool DoubleClick(GearDeckHit hit)
    {
        switch (hit.Zone)
        {
            case GearZone.Item when hit.Index < Items.Count:
                _b.Actions.Activate(Items[hit.Index].ObjectId);
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

    /// <summary>The item a press on <paramref name="hit"/> would lift, and where it leaves from.</summary>
    private (uint Id, ItemDragSource Kind)? DragItem(GearDeckHit hit)
    {
        if (hit.Zone == GearZone.Item && hit.Index >= 0 && hit.Index < Items.Count)
            return (Items[hit.Index].ObjectId, ItemDragSource.Inventory);
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
    /// cell, or (as <see cref="GearZone.Frame"/>) the empty contents area; null anywhere else.</summary>
    private GearDeckHit? DropTargetAt(float x, float y)
    {
        if (_model.Packs.Count == 0) return null;
        GearDeckHit hit = _g.HitAt(x, y);
        return hit.Zone switch
        {
            GearZone.SidePack when hit.Index < _model.Packs.Count => hit,
            GearZone.Item when hit.Index < Items.Count => hit,
            GearZone.EquipSlot when hit.Index < GearSlotCatalog.Slots.Count => hit,
            GearZone.WornCell when hit.Index < _model.Worn.Count => hit,
            GearZone.Frame when _g.Contents.Contains(x, y) => hit,
            _ => null,
        };
    }

    /// <summary>The slot mask a drop on an equipment slot or worn cell asks for.</summary>
    private EquipMask SlotMaskAt(GearDeckHit hit) => hit.Zone == GearZone.EquipSlot
        ? GearSlotCatalog.Slots[hit.Index].Mask
        : _model.Worn[hit.Index].CurrentlyEquippedLocation;

    private bool DropReleased(object? payload, float x, float y)
    {
        if (ItemPayload(payload) is not { } p || DropTargetAt(x, y) is not { } hit) return false;
        bool fromGround = p.SourceKind == ItemDragSource.Ground;
        switch (hit.Zone)
        {
            case GearZone.SidePack:
                Dispatcher.DropOnPack(p.ObjId, _model.Packs[hit.Index].Id, fromGround);
                break;
            case GearZone.Item:
                Dispatcher.DropOnCell(p.ObjId, SelectedPackId, Items[hit.Index].ObjectId, hit.Index, fromGround);
                break;
            case GearZone.Frame:   // empty grid space: after the last item, as retail's list does
                Dispatcher.DropOnCell(p.ObjId, SelectedPackId, 0u, -1, fromGround);
                break;
            case GearZone.EquipSlot or GearZone.WornCell:
                Dispatcher.DropOnSlot(p.ObjId, SlotMaskAt(hit));
                break;
        }
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
        if (_tooltipCache is { } c && c.Hit == hit && c.Revision == _lastRevision && c.Pack == pack) return c.Content;
        HudTooltipContent? content = TooltipFor(hit) is { } text ? new HudTooltipContent(text, null, null, null) : null;
        _tooltipCache = (hit, _lastRevision, pack, content);
        return content;
    }

    /// <summary>An item's caption (the selection caption's name, with its stack), or an empty slot's label.</summary>
    private string? TooltipFor(GearDeckHit hit)
    {
        if (hit.Index < 0) return null;
        switch (hit.Zone)
        {
            case GearZone.Item when hit.Index < Items.Count:
                return Caption(Items[hit.Index].ObjectId);
            case GearZone.EquipSlot when hit.Index < GearSlotCatalog.Slots.Count:
                return WornAt(hit) is uint worn ? Caption(worn) : GearSlotCatalog.Slots[hit.Index].Label;
            case GearZone.WornCell when WornAt(hit) is uint cell:
                return Caption(cell);
            case GearZone.SidePack when hit.Index < _model.Packs.Count && !_model.Packs[hit.Index].IsMain:
                return Caption(_model.Packs[hit.Index].Id);
            default:
                return null;
        }
    }

    private string? Caption(uint id) => ItemTooltipCaption.Resolve(_b.Objects(), id, _b.DisplayName);

    protected override void OnDrawOverlay(UiRenderContext ctx)
    {
        if (VisibleTooltip is not { } tip || RectFor(Hover) is not { } anchor) return;
        ctx.PushAlphaAbsolute(1f);
        try
        {
            HudTooltip.Draw(ctx, tip, HudTooltipStyle.Compact, anchor,
                ScreenPosition, FindRoot()?.Width, _tooltipLayout, _fonts);
        }
        finally { ctx.PopAlpha(); }
    }
}
