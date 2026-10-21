using System.Collections.Frozen;
using AcDream.Core.Items;
using AcDream.Runtime;

namespace AcDream.Rynth.Host;

/// <summary>One object as the snapshot holds it: a deep copy plus the live view's derived answers.</summary>
public sealed record RynthObjectEntry(
    string Name, RynthObjectInfo Info, IReadOnlyDictionary<uint, long> Int64s,
    bool IsOwnedItem, bool IsOwnedTinkeringTool, bool IsAttackable,
    IReadOnlyList<RynthEnchantment>? Enchantments);

/// <summary>The snapshot's object part: nothing in it references the client's live tables.</summary>
public sealed record RynthObjectsPart
{
    public static readonly RynthObjectsPart Empty = new();

    public FrozenDictionary<uint, RynthObjectEntry> Entries { get; init; } = FrozenDictionary<uint, RynthObjectEntry>.Empty;
    public FrozenDictionary<uint, IReadOnlyList<uint>> ContentsByContainer { get; init; } = FrozenDictionary<uint, IReadOnlyList<uint>>.Empty;
    public FrozenDictionary<uint, (bool Ok, int Items, int Containers)> Counts { get; init; } = FrozenDictionary<uint, (bool, int, int)>.Empty;
    public IReadOnlyList<uint>? LiveObjectIds { get; init; }
    public ulong Revision { get; init; }

    public bool TryGet(uint id, out RynthObjectEntry entry) => Entries.TryGetValue(id, out entry!);

    public IReadOnlyList<uint> Contents(uint containerId) =>
        ContentsByContainer.TryGetValue(containerId, out IReadOnlyList<uint>? ids) ? ids : [];

    public bool TryCountContents(uint containerId, out int items, out int containers)
    {
        (bool ok, items, containers) = Counts.TryGetValue(containerId, out var c) ? c : (false, 0, 0);
        return ok;
    }
}

/// <summary>
/// The object table, mirrored for the snapshot: objects the table reported added, updated, moved or
/// removed are copied again at the next build; the rest keep their entry (the derived answers are
/// re-asked whenever the table moved, and a changed answer gives a new entry sharing the copy).
/// Frame thread only.
/// </summary>
public sealed class RynthObjectMirror : IDisposable
{
    private readonly RuntimeRynthGameView _live;
    private readonly ClientObjectTable _table;
    private readonly HashSet<uint> _dirty = [];
    private readonly Dictionary<uint, RynthObjectEntry> _entries = [];
    private bool _all = true;
    // A contents listing of known objects, or a view ending, moves no revision: only ContainerContentsReplaced says so.
    private bool _contentsDirty;
    private (ulong Revision, uint Player) _key = (ulong.MaxValue, 0);
    private RynthObjectsPart _last = RynthObjectsPart.Empty;

    public RynthObjectMirror(IRynthGameView live, GameRuntime runtime)
    {
        _live = (RuntimeRynthGameView)live;
        _table = runtime.InventoryOwner.Objects;
        _table.ObjectAdded += OnObject;
        _table.ObjectUpdated += OnObject;
        _table.ObjectRemoved += OnObject;
        _table.ObjectMoved += OnMoved;
        _table.Cleared += OnCleared;
        _table.ContainerContentsReplaced += OnContentsReplaced;
    }

    public RynthObjectsPart Build(uint player)
    {
        var key = (_table.MutationRevision, player);
        // Idle frames: nothing moved, the last part is published again.
        if (key == _key && _dirty.Count == 0 && !_all && !_contentsDirty)
            return _last;
        bool recomputeAll = _all || key != _key;
        IReadOnlyList<uint>? liveIds = _live.LiveObjectIds;
        // The live view caches its array between reads; the part gets its own.
        uint[]? idsCopy = liveIds?.ToArray();
        uint[] ids = idsCopy ?? [];
        if (_all)
            _entries.Clear();
        var present = new HashSet<uint>(ids);
        foreach (uint id in _entries.Keys.Where(id => !present.Contains(id)).ToList())
            _entries.Remove(id);
        foreach (uint id in ids)
        {
            bool known = _entries.TryGetValue(id, out RynthObjectEntry? existing);
            if (known && !_dirty.Contains(id) && !PlacementMoved(id, existing!.Info))
            {
                if (recomputeAll)
                    _entries[id] = Rederive(id, existing);
                continue;
            }
            if (!_live.TryCopyObject(id, out RynthObjectInfo info, out IReadOnlyDictionary<uint, long> int64s))
            {
                _entries.Remove(id);
                continue;
            }
            _live.TryGetObjectName(id, out string name);
            _entries[id] = new RynthObjectEntry(name ?? "", info, int64s,
                _live.IsOwnedItem(id), _live.IsOwnedTinkeringTool(id), _live.IsAttackable(id),
                _live.GetObjectEnchantments(id)?.ToArray());
        }
        _dirty.Clear();
        _all = false;
        _contentsDirty = false;

        var contents = new Dictionary<uint, IReadOnlyList<uint>>();
        var counts = new Dictionary<uint, (bool, int, int)>();
        foreach (uint id in ids.Append(player))
        {
            IReadOnlyList<uint> c = _live.GetContainerContents(id);
            if (c.Count > 0)
                contents[id] = [.. c];
            if (_live.TryCountContents(id, out int items, out int containers))
                counts[id] = (true, items, containers);
        }
        _key = key;
        _last = new RynthObjectsPart
        {
            Entries = _entries.ToFrozenDictionary(),
            ContentsByContainer = contents.ToFrozenDictionary(),
            Counts = counts.ToFrozenDictionary(),
            LiveObjectIds = idsCopy,
            Revision = _live.InventoryRevision,
        };
        return _last;
    }

    public void Dispose()
    {
        _table.ObjectAdded -= OnObject;
        _table.ObjectUpdated -= OnObject;
        _table.ObjectRemoved -= OnObject;
        _table.ObjectMoved -= OnMoved;
        _table.Cleared -= OnCleared;
        _table.ContainerContentsReplaced -= OnContentsReplaced;
    }

    // An unreported object's answers can still change (its pack moved, the player changed):
    // asked again, and the entry kept when they are the same.
    private RynthObjectEntry Rederive(uint id, RynthObjectEntry entry)
    {
        bool owned = _live.IsOwnedItem(id);
        bool tool = _live.IsOwnedTinkeringTool(id);
        bool attackable = _live.IsAttackable(id);
        IReadOnlyList<RynthEnchantment>? enchantments = _live.GetObjectEnchantments(id);
        bool sameEnchantments = enchantments is null
            ? entry.Enchantments is null
            : entry.Enchantments is not null && enchantments.SequenceEqual(entry.Enchantments);
        if (owned == entry.IsOwnedItem && tool == entry.IsOwnedTinkeringTool
            && attackable == entry.IsAttackable && sameEnchantments)
        {
            return entry;
        }
        return entry with
        {
            IsOwnedItem = owned, IsOwnedTinkeringTool = tool, IsAttackable = attackable,
            Enchantments = enchantments?.ToArray(),
        };
    }

    // A move renumbers the slots of every sibling in the source and destination containers, but
    // reports only the moved item; a cheap read of the live placement catches the rest.
    private bool PlacementMoved(uint id, RynthObjectInfo info) =>
        _table.Get(id) is not { } item
        || item.ContainerSlot != info.ContainerSlot
        || item.ContainerId != info.ContainerId
        || item.WielderId != info.WielderId;

    private void OnContentsReplaced(uint containerId) => _contentsDirty = true;

    private void OnObject(ClientObject item) => _dirty.Add(item.ObjectId);

    private void OnMoved(ClientObjectMove move) => _dirty.Add(move.ItemId);

    private void OnCleared() => _all = true;
}
