using AcDream.Core.Items;
using AcDream.Core.Items.Gear;

namespace AcDream.App.UI.Hud;

/// <summary>
/// Keeps the Gear Deck's organising going while the deck is closed: drops lock ids for objects that are
/// gone (once per login, when the inventory is complete), and after items arrive from outside (pickup,
/// loot, trade, purchase) queues combine-on-arrival and the on-arrival pack rules for them.
/// </summary>
internal sealed class GearInventoryWatcher : IDisposable
{
    public const double QuietSeconds = 0.5;
    public const string Label = "Sorting new items";

    private readonly Func<ClientObjectTable> _objects;
    private readonly Func<uint> _player;
    private readonly GearDeckSession _session;
    private readonly GearMoveQueue _queue;
    private readonly Func<ClientObject, string> _displayName;
    private readonly ItemLocks? _locks;
    private readonly Func<double> _clock;
    private readonly Action<string>? _log;
    private readonly HashSet<uint> _arrived = [];
    private double _lastArrival;
    private bool _pruned;
    private bool _disposed;
    private ClientObjectTable? _table;

    public GearInventoryWatcher(Func<ClientObjectTable> objects, Func<uint> player, GearDeckSession session, GearMoveQueue queue,
        Func<ClientObject, string> displayName, ItemLocks? locks, Func<double> clock, Action<string>? log = null)
    {
        _objects = objects; _player = player; _session = session; _queue = queue;
        _displayName = displayName; _locks = locks; _clock = clock; _log = log;
        _session.Ended += OnEnded;
        Follow(_objects());
    }

    public void Tick()
    {
        if (_disposed) return;
        Follow(_objects());
        uint player = _player();
        if (player == 0u || _table is null) return;
        if (!_pruned && _table.HasCompleteInventory(player))
        {
            _pruned = true;
            ClientObjectTable t = _table;
            uint[] kept = _session.Settings.LockedIds.Where(id => t.Get(id) is not null).ToArray();
            int stale = _session.Settings.LockedIds.Count - kept.Length;
            if (stale > 0)
            {
                _session.Update(s => s with { LockedIds = kept });
                _log?.Invoke($"[gear-deck] dropped {stale} stale locks");
            }
        }
        if (_arrived.Count == 0 || _queue.IsRunning || _clock() - _lastArrival < QuietSeconds) return;
        uint[] only = _arrived.ToArray();
        _arrived.Clear();
        GearInventoryModel model = GearInventoryModel.Build(_table, player, null);
        GearProjection rows = GearProjection.Build(model, _table, _displayName, _locks is { } l ? l.IsLocked : null);
        GearDeckSettings s = _session.Settings;
        var moves = new List<GearMove>();
        if (s.CombineOnArrival) moves.AddRange(StackCombinePlanner.Plan(rows.All, only));
        GearRulePlan rules = PackRulePlanner.Plan(rows.All, model.Packs, GearRules.Resolve(s, r => r.OnArrival), only);
        moves.AddRange(rules.Moves);
        if (moves.Count > 0 && _queue.Start(Label, moves))
            _log?.Invoke($"[gear-deck] {Label}: {moves.Count} moves for {only.Length} arrivals");
    }

    private void Follow(ClientObjectTable table)
    {
        if (ReferenceEquals(table, _table)) return;
        if (_table is not null) { _table.ObjectMoved -= OnMoved; _table.ObjectAdded -= OnAdded; }
        _table = table;
        _table.ObjectMoved += OnMoved;
        _table.ObjectAdded += OnAdded;
        _arrived.Clear();
        _pruned = false;
    }

    /// <summary>The player or one of their side packs (not equipped).</summary>
    private bool IsOwnPack(uint container)
    {
        uint player = _player();
        if (container == 0u || player == 0u) return false;
        if (container == player) return true;
        return _table?.Get(container) is { } c && c.ContainerId == player && c.CurrentlyEquippedLocation == EquipMask.None
            && IsPack(c);
    }

    /// <summary>ClientObjectTable.IsContainerListMember (internal to Core): what the table lists as a container.</summary>
    private static bool IsPack(ClientObject o) =>
        o.ContainerTypeHint != 0u || (o.Type & ItemType.Container) != 0 || o.ItemsCapacity > 0;

    private void OnMoved(ClientObjectMove move)
    {
        if (move.Origin != ClientObjectMoveOrigin.AuthoritativeResponse) return;
        if (!IsOwnPack(move.Current.ContainerId)) return;
        // A move inside the inventory, or off the body, is not an arrival.
        if (IsOwnPack(move.Previous.ContainerId) || (move.Previous.WielderId != 0u && move.Previous.WielderId == _player())) return;
        Arrive(move.ItemId);
    }

    private void OnAdded(ClientObject o)
    {
        // Before the inventory is complete, adds are the login listing.
        if (_pruned && IsOwnPack(o.ContainerId)) Arrive(o.ObjectId);
    }

    private void Arrive(uint id)
    {
        _arrived.Add(id);
        _lastArrival = _clock();
    }

    private void OnEnded()
    {
        _pruned = false;
        _arrived.Clear();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _session.Ended -= OnEnded;
        if (_table is not null) { _table.ObjectMoved -= OnMoved; _table.ObjectAdded -= OnAdded; }
        _table = null;
        _arrived.Clear();
    }
}
