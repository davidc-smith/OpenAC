namespace AcDream.App.UI.Hud;

/// <summary>
/// The Spell Deck's tabs: the server's eight favourite lists (tabs 0-7, read
/// from the spellbook) followed by up to eight local tabs (8-15, saved in this
/// PC's settings for the character). An edit to a server tab is sent through
/// <see cref="ISpellDeckActions"/>; an edit to a local tab changes the local
/// list and calls <c>localChanged</c> once, and never reaches the server.
/// </summary>
public sealed class SpellDeckTabs
{
    public const int ServerTabs = 8;
    public const int MaxTabs = ServerTabs + SpellDeckSettings.MaxLocalTabs;

    private readonly Func<int, IReadOnlyList<uint>> _server;
    private readonly ISpellDeckActions _actions;
    private readonly Action _localChanged;
    private readonly List<List<uint>> _local = [];

    public SpellDeckTabs(Func<int, IReadOnlyList<uint>> serverFavorites, ISpellDeckActions actions, Action localChanged)
    {
        _server = serverFavorites ?? throw new ArgumentNullException(nameof(serverFavorites));
        _actions = actions ?? throw new ArgumentNullException(nameof(actions));
        _localChanged = localChanged ?? throw new ArgumentNullException(nameof(localChanged));
    }

    /// <summary>All tabs: 8 server tabs plus the local ones.</summary>
    public int Count => ServerTabs + _local.Count;
    public int LocalCount => _local.Count;
    public bool CanAddLocal => _local.Count < SpellDeckSettings.MaxLocalTabs;
    public bool IsLocal(int tab) => tab >= ServerTabs && tab < Count;

    /// <summary>A copy of the local tabs' spell ids, for saving.</summary>
    public IReadOnlyList<IReadOnlyList<uint>> Local => [.. _local.Select(l => (IReadOnlyList<uint>)l.ToArray())];

    /// <summary>Replaces the local tabs (character change). Not a change: nothing is saved.</summary>
    public void Load(IReadOnlyList<IReadOnlyList<uint>> local)
    {
        _local.Clear();
        foreach (IReadOnlyList<uint> tab in (local ?? []).Take(SpellDeckSettings.MaxLocalTabs))
            _local.Add([.. tab ?? []]);
    }

    /// <summary>The tab's spells in order; empty for a tab that does not exist.</summary>
    public IReadOnlyList<uint> Get(int tab) =>
        tab < 0 || tab >= Count ? []
        : tab < ServerTabs ? _server(tab)
        : _local[tab - ServerTabs].ToArray();

    /// <summary>Puts <paramref name="spellId"/> at <paramref name="position"/> of the tab (taken out
    /// first if already there; past the end appends), as the server's favourite list does.</summary>
    public void Set(int tab, int position, uint spellId)
    {
        if (tab < 0 || tab >= Count) return;
        if (tab < ServerTabs) { _actions.SetFavorite(tab, position, spellId); return; }
        if (position < 0) return;
        List<uint> list = _local[tab - ServerTabs];
        int was = list.IndexOf(spellId);
        list.Remove(spellId);
        int at = Math.Min(position, list.Count);
        list.Insert(at, spellId);
        if (was != at) _localChanged();
    }

    public void Remove(int tab, uint spellId)
    {
        if (tab < 0 || tab >= Count) return;
        if (tab < ServerTabs) { _actions.RemoveFavorite(tab, spellId); return; }
        if (_local[tab - ServerTabs].Remove(spellId)) _localChanged();
    }

    /// <summary>Adds an empty local tab at the end; false at <see cref="SpellDeckSettings.MaxLocalTabs"/>.</summary>
    public bool AddLocal()
    {
        if (!CanAddLocal) return false;
        _local.Add([]);
        _localChanged();
        return true;
    }

    /// <summary>Removes the last local tab and its spells; false when there is none (server tabs stay).</summary>
    public bool RemoveLastLocal()
    {
        if (_local.Count == 0) return false;
        _local.RemoveAt(_local.Count - 1);
        _localChanged();
        return true;
    }
}
