using System.Globalization;
using AcDream.Core.Items;
using AcDream.Core.Items.Gear;

namespace AcDream.App.UI.Hud;

/// <summary>The deck's organising actions: they plan with the Core planners and hand the moves to the queue.</summary>
internal sealed partial class UiGearDeck
{
    private IReadOnlyList<GearMove> _combineMoves = [];
    private ulong _combineFor = ulong.MaxValue;
    private string _queueText = "";
    private int _queueTextFor = -1;
    /// <summary>The item a drag that began on the deck lifted, until it drops or the capture ends; 0 otherwise.</summary>
    private uint _dragFromDeck;

    /// <summary>Every merge that would combine the pack items' partial stacks; cached per model build.</summary>
    internal IReadOnlyList<GearMove> CombineMoves
    {
        get
        {
            if (_combineFor != _modelBuild)
            {
                _combineMoves = StackCombinePlanner.Plan(_projection.All);
                _combineFor = _modelBuild;
            }
            return _combineMoves;
        }
    }

    /// <summary>"Combining 2 / 5": a running queue's label and progress, built once per queue version.</summary>
    private string QueueText(GearMoveQueue q)
    {
        if (_queueTextFor != q.Version)
        {
            _queueText = $"{q.Label} {q.Done.ToString(CultureInfo.InvariantCulture)} / {q.Total.ToString(CultureInfo.InvariantCulture)}";
            _queueTextFor = q.Version;
        }
        return _queueText;
    }

    private bool Run(string label, IReadOnlyList<GearMove> moves)
    {
        if (_b.Queue is not { } queue || moves.Count == 0) return false;
        bool started = queue.Start(label, moves);
        _b.Log?.Invoke($"[gear-deck] {label}: {moves.Count} moves {(started ? "started" : "refused")}");
        SyncGeometry();
        return started;
    }

    internal void CombineAll() => Run("Combining", CombineMoves);

    internal void StopQueue()
    {
        _b.Queue?.Cancel();
        SyncGeometry();
    }

    /// <summary>A manual drag takes over: a running queue stops so the two don't fight over the inventory.</summary>
    private void CancelQueueForDrag()
    {
        if (_b.Queue is not { IsRunning: true } q) return;
        q.Cancel();
        _b.Log?.Invoke("[gear-deck] drag cancelled the running queue");
        SyncGeometry();
    }

    /// <summary>
    /// A drag of the selection onto a pack: every selected pack item, in shown order, lands together just
    /// before the anchor, the first unselected item at or after <paramref name="firstSlot"/> in that pack
    /// (none: at the end). Worn items and packs stay where they are. Each put takes its item out of the pack
    /// and inserts it (the table's and the server's order), so the slots come from replaying the puts on the
    /// pack's current order: a same-pack move keeps the group together and in order, which plain consecutive
    /// slots from the target do not. A selection already in place sends nothing. False when nothing selected
    /// can move this way (the caller then moves the dragged item alone).
    /// </summary>
    private bool MoveSelection(uint packId, int firstSlot)
    {
        var group = new List<uint>();
        foreach (GearItemView row in Shown)
            if (IsGroupMovable(row)) group.Add(row.Id);
        if (group.Count == 0) return false;
        var picked = new HashSet<uint>(group);

        var order = new List<uint>();
        foreach (ClientObject o in _model.Contents(packId)) order.Add(o.ObjectId);
        uint anchor = 0u;
        for (int i = Math.Max(0, firstSlot); i < order.Count; i++)
            if (!picked.Contains(order[i])) { anchor = order[i]; break; }

        var moves = new List<GearMove>(group.Count);
        var replay = new List<uint>(order);
        foreach (uint id in group)
        {
            replay.Remove(id);
            int slot = anchor == 0u ? replay.Count : replay.IndexOf(anchor);
            replay.Insert(slot, id);
            moves.Add(GearMove.Put(id, packId, slot));
        }
        if (replay.SequenceEqual(order))
        {
            _b.Log?.Invoke($"[gear-deck] Moving: {group.Count} items already in place");
            return true;
        }
        Run("Moving", moves);
        return true;
    }

    /// <summary>A selected loose pack item: what a group drop moves.</summary>
    private bool IsGroupMovable(GearItemView row) => _selection.Contains(row.Id) && !row.Worn && !row.IsPack;

    /// <summary>Whether the dragged item is a shown loose pack item, so its drop can carry the selection.</summary>
    private bool DraggedIsGroupMovable(uint id)
    {
        foreach (GearItemView row in Shown)
            if (row.Id == id) return IsGroupMovable(row);
        return false;
    }

    /// <summary>The pack and first slot a selection dropped on <paramref name="hit"/> goes to, or null for
    /// equipment, the figure, worn rows and anything else that takes one item.</summary>
    private (uint Pack, int Slot)? GroupTarget(GearDeckHit hit) => hit.Zone switch
    {
        GearZone.SidePack => (_model.Packs[hit.Index].Id, 0),
        GearZone.Item when Shown[hit.Index] is { Worn: false, IsPack: true } bag => (bag.Id, 0),
        GearZone.Item when Shown[hit.Index] is { Worn: false } row => (row.PackId, row.Slot),
        GearZone.Frame => (SelectedPackId, Items.Count),
        _ => null,
    };
}
