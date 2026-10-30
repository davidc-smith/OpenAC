using System.Globalization;
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

    /// <summary>A drag of the selection onto a pack: every selected pack item, in shown order, from
    /// <paramref name="firstSlot"/> on. Worn items and packs stay where they are.</summary>
    private bool MoveSelection(uint packId, int firstSlot)
    {
        var moves = new List<GearMove>();
        int slot = Math.Max(0, firstSlot);
        foreach (GearItemView row in Shown)
            if (_selection.Contains(row.Id) && !row.Worn && !row.IsPack)
                moves.Add(GearMove.Put(row.Id, packId, slot++));
        return Run("Moving", moves);
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
