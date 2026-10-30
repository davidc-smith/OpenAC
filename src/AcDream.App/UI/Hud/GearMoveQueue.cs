using AcDream.Core.Items;
using AcDream.Core.Items.Gear;
using AcDream.Runtime.Gameplay;

namespace AcDream.App.UI.Hud;

/// <summary>When the game can take another inventory request, and which item's request the server settled.</summary>
internal interface IGearMoveChannel
{
    /// <summary>No request is pending and nothing is busy. Never prints anything.</summary>
    bool CanSend { get; }
    event Action<uint>? Completed;
    event Action<uint>? Failed;
}

internal enum GearMoveResult { Sent, Skipped, Refused }

internal interface IGearMoveSender { GearMoveResult Send(GearMove move); }

/// <summary>
/// Sends a planner's moves one at a time: the next goes out once the server confirmed the last and the game is
/// free. It stops at the first failure (the runtime has already printed the server's message), at a refusal,
/// after <see cref="TimeoutSeconds"/> without an answer (a request that never settles blocks every later one),
/// or on <see cref="Cancel"/>. One queue runs at a time.
/// </summary>
internal sealed class GearMoveQueue : IDisposable
{
    public const double TimeoutSeconds = 2.0;

    private readonly IGearMoveChannel _channel;
    private readonly IGearMoveSender _sender;
    private readonly Func<double> _clock;
    private readonly Action<string>? _report;
    private IReadOnlyList<GearMove> _moves = [];
    private int _next;
    private uint _waiting;
    private double _sentAt;

    public GearMoveQueue(IGearMoveChannel channel, IGearMoveSender sender, Func<double> clock, Action<string>? report = null)
    {
        _channel = channel;
        _sender = sender;
        _clock = clock;
        _report = report;
        _channel.Completed += OnCompleted;
        _channel.Failed += OnFailed;
    }

    public bool IsRunning { get; private set; }
    public string Label { get; private set; } = "";
    public int Done { get; private set; }
    public int Total => _moves.Count;
    /// <summary>Bumped on every change, for the deck's cached progress text.</summary>
    public int Version { get; private set; }
    public event Action? Changed;

    public bool Start(string label, IReadOnlyList<GearMove> moves)
    {
        ArgumentNullException.ThrowIfNull(moves);
        if (IsRunning)
        {
            _report?.Invoke($"{Label} is still running.");
            return false;
        }
        if (moves.Count == 0) return false;
        Label = label;
        _moves = moves;
        _next = 0;
        _waiting = 0u;
        Done = 0;
        IsRunning = true;
        Bump();
        Tick();
        return true;
    }

    public void Cancel() => Stop(null);

    public void Tick()
    {
        if (!IsRunning) return;
        if (_waiting != 0u)
        {
            if (_clock() - _sentAt >= TimeoutSeconds) Stop($"{Label} stopped: no answer from the server.");
            return;
        }
        while (IsRunning && _waiting == 0u && _next < _moves.Count && _channel.CanSend)
        {
            GearMove move = _moves[_next];
            switch (_sender.Send(move))
            {
                case GearMoveResult.Sent:
                    _waiting = move.Item;
                    _sentAt = _clock();
                    _next++;
                    break;
                case GearMoveResult.Skipped:
                    _next++;
                    Done++;
                    Bump();
                    break;
                default:
                    Stop($"{Label} stopped at {Done} of {Total}.");
                    return;
            }
        }
        if (IsRunning && _waiting == 0u && _next >= _moves.Count) Stop(null);
    }

    private void OnCompleted(uint id)
    {
        if (!IsRunning || id != _waiting) return;
        _waiting = 0u;
        Done++;
        Bump();
    }

    private void OnFailed(uint id)
    {
        if (IsRunning && id == _waiting) Stop(null);
    }

    private void Stop(string? line)
    {
        if (!IsRunning) return;
        IsRunning = false;
        _waiting = 0u;
        if (line is not null) _report?.Invoke(line);
        Bump();
    }

    private void Bump()
    {
        Version++;
        Changed?.Invoke();
    }

    public void Dispose()
    {
        _channel.Completed -= OnCompleted;
        _channel.Failed -= OnFailed;
    }
}

/// <summary>Turns a <see cref="GearMove"/> into the retail inventory's request, re-checking the table as it goes:
/// a move the game already made is skipped, a pack that can't take the item is refused with retail's text.</summary>
internal sealed class GearMoveSender(ClientObjectTable objects, Func<uint> playerGuid, IGearItemActions actions) : IGearMoveSender
{
    public GearMoveResult Send(GearMove move)
    {
        if (objects.Get(move.Item) is not { } item) return GearMoveResult.Skipped;
        switch (move.Kind)
        {
            case GearMoveKind.Merge:
                if (objects.Get(move.Target) is null) return GearMoveResult.Skipped;
                return actions.Merge(move.Item, move.Target) ? GearMoveResult.Sent : GearMoveResult.Refused;
            case GearMoveKind.Split:
                return actions.Split(move.Item, move.Target, move.Slot, move.Amount) ? GearMoveResult.Sent : GearMoveResult.Refused;
            default:
                IReadOnlyList<uint> inside = objects.GetContainedItems(move.Target);
                int slot = move.Slot < 0 ? inside.Count : move.Slot;
                if (item.ContainerId == move.Target && slot < inside.Count && inside[slot] == move.Item) return GearMoveResult.Skipped;
                uint player = playerGuid();
                InventoryContainerPlacementRejection refusal =
                    InventoryContainerPlacementPolicy.Evaluate(objects, move.Item, move.Target, player);
                if (refusal != InventoryContainerPlacementRejection.None)
                {
                    if (InventoryContainerPlacementPolicy.ComposeClientLocal(refusal, item, objects.Get(move.Target), player) is { } text)
                        actions.Report(text);
                    return GearMoveResult.Refused;
                }
                return actions.PutInContainer(move.Item, move.Target, slot, fromGround: false)
                    ? GearMoveResult.Sent : GearMoveResult.Refused;
        }
    }
}

/// <summary><see cref="IGearMoveChannel"/> over the runtime's inventory transaction state.</summary>
internal sealed class RuntimeGearMoveChannel : IGearMoveChannel, IDisposable
{
    private readonly RuntimeItemInteraction _items;
    private readonly InventoryTransactionState _state;

    public RuntimeGearMoveChannel(RuntimeItemInteraction items)
    {
        _items = items;
        _state = items.RuntimeTransactions.Inventory;
        _state.RequestCompleted += OnCompleted;
        _state.RequestFailed += OnFailed;
    }

    public bool CanSend => _items.CanMakeInventoryRequest;
    public event Action<uint>? Completed;
    public event Action<uint>? Failed;

    private void OnCompleted(PendingInventoryRequest r) => Completed?.Invoke(r.ItemId);
    private void OnFailed(PendingInventoryRequest r, uint _) => Failed?.Invoke(r.ItemId);

    public void Dispose()
    {
        _state.RequestCompleted -= OnCompleted;
        _state.RequestFailed -= OnFailed;
    }
}
