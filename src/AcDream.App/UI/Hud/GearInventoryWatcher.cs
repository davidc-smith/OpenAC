using AcDream.Core.Items;
using AcDream.Core.Items.Gear;

namespace AcDream.App.UI.Hud;

/// <summary>
/// Keeps the Gear Deck's organising going while the deck is closed: after items arrive from outside (pickup, loot,
/// trade, purchase) queues combine-on-arrival and the on-arrival pack rules for them. A stack this client
/// split off (the deck's Split, a shift-drag, a queued split) is not an arrival. Locks are never removed here or at
/// login: only the player unlocks, and the ids of items that are gone stay in the saved list.
/// </summary>
internal sealed class GearInventoryWatcher : IDisposable
{
    public const double QuietSeconds = 0.5;
    /// <summary>How long after a split completes an add of the same weenie is still taken as that split's new stack.</summary>
    public const double SplitGraceSeconds = 1.0;
    public const string Label = "Sorting new items";

    private readonly Func<ClientObjectTable> _objects;
    private readonly Func<uint> _player;
    private readonly GearDeckSession _session;
    private readonly GearMoveQueue _queue;
    private readonly Func<ClientObject, string> _displayName;
    private readonly ItemLocks? _locks;
    private readonly Func<double> _clock;
    private readonly Action<string>? _report;
    private readonly Action<string>? _log;
    private readonly IGearRequestWatch? _requests;
    private readonly Func<bool>? _dragging;
    private readonly HashSet<uint> _arrived = [];
    private uint _splitWeenie;
    private double _splitUntil = double.NegativeInfinity;
    private double _lastArrival;
    /// <summary>The character's settings loaded and the login listing is complete: adds from here on are arrivals.</summary>
    private bool _listed;
    private bool _disposed;
    private ClientObjectTable? _table;

    public GearInventoryWatcher(Func<ClientObjectTable> objects, Func<uint> player, GearDeckSession session, GearMoveQueue queue,
        Func<ClientObject, string> displayName, ItemLocks? locks, Func<double> clock,
        Action<string>? report = null, Action<string>? log = null, IGearRequestWatch? requests = null, Func<bool>? dragging = null)
    {
        _objects = objects; _player = player; _session = session; _queue = queue;
        _displayName = displayName; _locks = locks; _clock = clock; _report = report; _log = log; _requests = requests;
        _dragging = dragging;
        _session.Ended += OnEnded;
        if (_requests is not null) _requests.Completed += OnRequestCompleted;
        Follow(_objects());
    }

    public void Tick()
    {
        if (_disposed) return;
        Follow(_objects());
        uint player = _player();
        if (player == 0u || _table is null) return;
        if (!_listed && _session.HasCharacter && _table.HasCompleteInventory(player)) _listed = true;
        // A sort started mid-drag would refuse the player's own drop; the arrivals wait for the drag to end.
        if (_arrived.Count == 0 || _queue.IsRunning || _dragging?.Invoke() == true || _clock() - _lastArrival < QuietSeconds) return;
        uint[] only = _arrived.ToArray();
        _arrived.Clear();
        GearInventoryModel model = GearInventoryModel.Build(_table, player, null);
        GearProjection rows = GearProjection.Build(model, _table, _displayName, _locks is { } l ? l.IsLocked : null);
        GearDeckSettings s = _session.Settings;
        var moves = new List<GearMove>();
        IReadOnlyCollection<uint> sortOnly = only;
        if (s.CombineOnArrival)
        {
            IReadOnlyList<GearMove> merges = StackCombinePlanner.Plan(rows.All, only);
            moves.AddRange(merges);
            sortOnly = AfterMerges(rows.All, merges, only);
        }
        GearRulePlan rules = PackRulePlanner.Plan(rows.All, model.Packs, GearRules.Resolve(s, r => r.OnArrival), sortOnly);
        moves.AddRange(rules.Moves);
        foreach ((uint pack, int left) in rules.Full)
            _report?.Invoke($"{GearRules.PackName(model.Packs, pack)} is full; {left.ToString(System.Globalization.CultureInfo.InvariantCulture)} left where they were.");
        if (moves.Count > 0 && _queue.Start(Label, moves))
            _log?.Invoke($"[gear-deck] {Label}: {moves.Count} moves for {only.Length} arrivals");
    }

    /// <summary>
    /// What the rules sort once the merges ran: an arrival the merges empty is gone, so the stack it went
    /// into is sorted in its place; an arrival that keeps some of its stack is still sorted itself.
    /// </summary>
    private static HashSet<uint> AfterMerges(IReadOnlyList<GearItemView> rows, IReadOnlyList<GearMove> merges, uint[] only)
    {
        var left = new Dictionary<uint, int>();
        foreach (GearItemView r in rows) left[r.Id] = r.Stack;
        var sort = new HashSet<uint>(only);
        foreach (GearMove m in merges)
        {
            if (m.Kind != GearMoveKind.Merge) continue;
            left[m.Item] = left.GetValueOrDefault(m.Item) - m.Amount;
            left[m.Target] = left.GetValueOrDefault(m.Target) + m.Amount;
            if (left[m.Item] <= 0 && sort.Contains(m.Item)) sort.Add(m.Target);
        }
        sort.RemoveWhere(id => left.TryGetValue(id, out int n) && n <= 0);
        return sort;
    }

    private void Follow(ClientObjectTable table)
    {
        if (ReferenceEquals(table, _table)) return;
        if (_table is not null) Unhook(_table);
        _table = table;
        _table.ObjectMoved += OnMoved;
        _table.ObjectAdded += OnAdded;
        _arrived.Clear();
        _listed = false;
    }

    /// <summary>The player or one of their side packs (not equipped).</summary>
    private bool IsOwnPack(uint container)
    {
        uint player = _player();
        if (container == 0u || player == 0u) return false;
        if (container == player) return true;
        return _table?.Get(container) is { } c && c.ContainerId == player && c.CurrentlyEquippedLocation == EquipMask.None
            && ClientObjectTable.IsContainerListMember(c);
    }

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
        if (!_listed || !IsOwnPack(o.ContainerId) || IsOwnSplit(o)) return;
        Arrive(o.ObjectId);
    }

    /// <summary>A drag of this item began: the player placed it, so the rules leave it where it lands.</summary>
    internal void ForgetArrival(uint id) => _arrived.Remove(id);

    /// <summary>The new stack of a split this client asked for: the server creates it in the pack, as an add.
    /// The add can come before or after the request completes, so both are covered.</summary>
    private bool IsOwnSplit(ClientObject o)
    {
        if (_requests?.Pending is { Kind: InventoryRequestKind.SplitToContainer }) return true;
        return o.WeenieClassId != 0u && o.WeenieClassId == _splitWeenie && _clock() < _splitUntil;
    }

    private void OnRequestCompleted(PendingInventoryRequest r)
    {
        if (r.Kind != InventoryRequestKind.SplitToContainer) return;
        uint weenie = r.ItemIdentity?.WeenieClassId ?? _table?.Get(r.ItemId)?.WeenieClassId ?? 0u;
        if (weenie == 0u) return;
        _splitWeenie = weenie;
        _splitUntil = _clock() + SplitGraceSeconds;
    }

    private void Unhook(ClientObjectTable table)
    {
        table.ObjectMoved -= OnMoved;
        table.ObjectAdded -= OnAdded;
    }

    private void Arrive(uint id)
    {
        _arrived.Add(id);
        _lastArrival = _clock();
    }

    private void OnEnded()
    {
        _listed = false;
        _arrived.Clear();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _session.Ended -= OnEnded;
        if (_requests is not null) _requests.Completed -= OnRequestCompleted;
        if (_table is not null) Unhook(_table);
        _table = null;
        _arrived.Clear();
    }
}

/// <summary>The inventory request this client has pending, and the ones the server settled.</summary>
internal interface IGearRequestWatch
{
    PendingInventoryRequest? Pending { get; }
    event Action<PendingInventoryRequest>? Completed;
}

/// <summary>The runtime's <see cref="InventoryTransactionState"/> as the watcher reads it.</summary>
internal sealed class RuntimeGearRequestWatch(InventoryTransactionState state) : IGearRequestWatch
{
    public PendingInventoryRequest? Pending => state.PendingRequest;

    public event Action<PendingInventoryRequest>? Completed
    {
        add => state.RequestCompleted += value;
        remove => state.RequestCompleted -= value;
    }
}
