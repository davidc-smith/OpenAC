namespace AcDream.Core.Items;

/// <summary>
/// The items the player has locked against loss: a locked item can't be sold,
/// given, dropped or salvaged. One set is shared by the Gear Deck (which saves
/// it per character) and the retail paths that refuse it.
/// </summary>
public sealed class ItemLocks
{
    private readonly HashSet<uint> _ids = [];

    /// <summary>Bumped on every change, for caches that follow the locks.</summary>
    public int Revision { get; private set; }
    public event Action? Changed;
    public IReadOnlyCollection<uint> Ids => _ids;

    public bool IsLocked(uint id) => id != 0u && _ids.Contains(id);

    /// <summary>Locks or unlocks one item; true when that changed anything.</summary>
    public bool Set(uint id, bool locked)
    {
        if (id == 0u) return false;
        bool changed = locked ? _ids.Add(id) : _ids.Remove(id);
        if (changed) Bump();
        return changed;
    }

    /// <summary>Makes the set exactly <paramref name="ids"/> (zeros ignored).</summary>
    public void Replace(IEnumerable<uint> ids)
    {
        var next = new HashSet<uint>(ids.Where(id => id != 0u));
        if (next.SetEquals(_ids)) return;
        _ids.Clear();
        _ids.UnionWith(next);
        Bump();
    }

    /// <summary>The locked item that stops <paramref name="id"/> leaving: itself, or for a pack the first
    /// locked item inside it; 0 when nothing is locked.</summary>
    public uint BlockerOf(ClientObjectTable objects, uint id)
    {
        ArgumentNullException.ThrowIfNull(objects);
        if (_ids.Count == 0 || id == 0u) return 0u;
        if (_ids.Contains(id)) return id;
        foreach (uint inside in objects.GetContainedItems(id))
            if (_ids.Contains(inside)) return inside;
        return 0u;
    }

    public static string Message(string name) => $"The {name} is locked.";

    private void Bump()
    {
        Revision++;
        Changed?.Invoke();
    }
}
