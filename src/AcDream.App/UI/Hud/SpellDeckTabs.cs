namespace AcDream.App.UI.Hud;

/// <summary>
/// The Spell Deck's tabs and their slot layouts: the server's eight favourite lists (tabs 0-7) followed by
/// up to eight local tabs (8-15). Every tab's layout is saved in this PC's settings for the character; a local
/// tab is only its layout. A server tab's list is kept equal to its layout's distinct spells in reading order:
/// each edit sends exactly the ops (<see cref="SpellDeckLayout.SyncOps"/>) that make it so, through
/// <see cref="ISpellDeckActions"/>. Server spells the layout cannot show stay on the server, after the
/// layout's own spells. Any layout change calls <c>changed</c> once per edit or reconcile.
/// </summary>
public sealed class SpellDeckTabs
{
    public const int ServerTabs = 8;
    public const int MaxTabs = ServerTabs + SpellDeckSettings.MaxLocalTabs;

    private readonly Func<int, IReadOnlyList<uint>> _server;
    private readonly ISpellDeckActions _actions;
    private readonly Action _changed;
    /// <summary>One per tab: the eight server tabs, then the local ones.</summary>
    private readonly List<SpellDeckLayout> _layouts = [.. Enumerable.Repeat(SpellDeckLayout.Empty, ServerTabs)];

    public SpellDeckTabs(Func<int, IReadOnlyList<uint>> serverFavorites, ISpellDeckActions actions, Action changed)
    {
        _server = serverFavorites ?? throw new ArgumentNullException(nameof(serverFavorites));
        _actions = actions ?? throw new ArgumentNullException(nameof(actions));
        _changed = changed ?? throw new ArgumentNullException(nameof(changed));
    }

    /// <summary>All tabs: 8 server tabs plus the local ones.</summary>
    public int Count => _layouts.Count;
    public int LocalCount => _layouts.Count - ServerTabs;

    /// <summary>Changes whenever the set of local tabs changes (a load, an add, a remove), so a
    /// caller can tell that "the last local tab" is still the one it saw.</summary>
    public int LocalGeneration { get; private set; }
    public bool CanAddLocal => LocalCount < SpellDeckSettings.MaxLocalTabs;
    public bool IsLocal(int tab) => tab >= ServerTabs && tab < Count;
    public bool IsServer(int tab) => tab >= 0 && tab < ServerTabs;

    /// <summary>True while this sends its own ops to the server; a server change seen then is their echo.</summary>
    public bool IsSyncing { get; private set; }

    /// <summary>The tab's layout; empty for a tab that does not exist.</summary>
    public SpellDeckLayout Layout(int tab) => tab >= 0 && tab < Count ? _layouts[tab] : SpellDeckLayout.Empty;

    /// <summary>Every tab's layout, in tab order.</summary>
    public IReadOnlyList<SpellDeckLayout> All => [.. _layouts];

    /// <summary>The non-empty layouts by tab, for saving.</summary>
    public IReadOnlyDictionary<int, SpellDeckLayout> Layouts =>
        Enumerable.Range(0, Count).Where(t => !_layouts[t].IsEmpty).ToDictionary(t => t, t => _layouts[t]);

    /// <summary>Each local tab's spells once, in reading order, for saving.</summary>
    public IReadOnlyList<IReadOnlyList<uint>> Local => [.. _layouts.Skip(ServerTabs).Select(l => (IReadOnlyList<uint>)[.. l.Distinct])];

    /// <summary>A copy of the server's list for a server tab; empty otherwise.</summary>
    public IReadOnlyList<uint> Server(int tab) => IsServer(tab) ? [.. _server(tab)] : [];

    /// <summary>Replaces every layout and the local tab count (character change). Not a change: nothing is
    /// saved or sent. Layouts of tabs that do not exist are dropped.</summary>
    public void Load(IReadOnlyDictionary<int, SpellDeckLayout> layouts, int localCount)
    {
        LocalGeneration++;
        _layouts.Clear();
        int count = ServerTabs + Math.Clamp(localCount, 0, SpellDeckSettings.MaxLocalTabs);
        for (int t = 0; t < count; t++)
            _layouts.Add(layouts is not null && layouts.TryGetValue(t, out SpellDeckLayout? l) && l is not null ? l : SpellDeckLayout.Empty);
    }

    /// <summary>Sets each tab's new layout, in order. A server tab sends its sync ops: a spell whose last copy the
    /// edit removed leaves the server; server spells the old layout never held stay, after the layout's own.
    /// Calls <c>changed</c> once when any layout changed; returns whether one did.</summary>
    public bool Apply(IReadOnlyList<SpellDeckTabLayout> edits)
    {
        bool changed = false;
        foreach ((int tab, SpellDeckLayout layout) in edits)
        {
            if (tab < 0 || tab >= Count || layout is null || layout.Equals(_layouts[tab])) continue;
            SpellDeckLayout previous = _layouts[tab];
            _layouts[tab] = layout;
            changed = true;
            if (IsServer(tab)) Send(tab, layout, previous);
        }
        if (changed) _changed();
        return changed;
    }

    /// <summary>
    /// Brings every server tab's layout in line with the server (login, character change, a change from
    /// elsewhere): spells the server lost are cleared from every slot; server spells the layout lacks go into
    /// the first empty slots in reading order within <paramref name="rows"/> × <paramref name="cols"/>, growing
    /// rows first (up to <see cref="SpellDeckGeometry.MaxRows"/>), then columns (up to
    /// <see cref="SpellDeckGeometry.MaxCapacity"/>); a later tab may use what an earlier one grew. Spells that
    /// still do not fit stay on the server, unshown. Then the server is put in the layout's order (only when
    /// it differs, so reconciling again sends nothing). Skipped while this sends its own ops, and while every
    /// server tab is empty (the favourites have not arrived, or were cleared at logout). Calls <c>changed</c>
    /// once when a layout changed; returns whether one did.
    /// </summary>
    public bool Reconcile(int rows, int cols)
    {
        if (IsSyncing) return false;
        IReadOnlyList<uint>[] servers = [.. Enumerable.Range(0, ServerTabs).Select(Server)];
        if (servers.All(s => s.Count == 0)) return false;
        int r = Math.Clamp(rows, 1, SpellDeckGeometry.MaxRows);
        int c = Math.Clamp(cols, 1, SpellDeckGeometry.MaxCapacity);
        bool changed = false;
        for (int tab = 0; tab < ServerTabs; tab++)
        {
            IReadOnlyList<uint> server = servers[tab];
            SpellDeckReconcile rec = _layouts[tab].Reconcile(server, r, c);
            while (rec.Unplaced.Count > 0 && r < SpellDeckGeometry.MaxRows) rec = rec.Layout.Reconcile(server, ++r, c);
            while (rec.Unplaced.Count > 0 && c < SpellDeckGeometry.MaxCapacity) rec = rec.Layout.Reconcile(server, r, ++c);
            if (!rec.Layout.Equals(_layouts[tab]))
            {
                _layouts[tab] = rec.Layout;
                changed = true;
            }
            Send(tab, rec.Layout, previous: null, server);
        }
        if (changed) _changed();
        return changed;
    }

    /// <summary>Adds an empty local tab at the end; false at <see cref="SpellDeckSettings.MaxLocalTabs"/>.</summary>
    public bool AddLocal()
    {
        if (!CanAddLocal) return false;
        _layouts.Add(SpellDeckLayout.Empty);
        LocalGeneration++;
        _changed();
        return true;
    }

    /// <summary>Removes the last local tab and its layout; false when there is none (server tabs stay).</summary>
    public bool RemoveLastLocal()
    {
        if (LocalCount == 0) return false;
        _layouts.RemoveAt(_layouts.Count - 1);
        LocalGeneration++;
        _changed();
        return true;
    }

    /// <summary>Sends the ops that make the server list equal <paramref name="layout"/>'s distinct spells, followed
    /// by the server spells it does not show and <paramref name="previous"/> did not hold either (so only a spell
    /// whose last copy an edit removed leaves the server).</summary>
    private void Send(int tab, SpellDeckLayout layout, SpellDeckLayout? previous, IReadOnlyList<uint>? server = null)
    {
        server ??= Server(tab);
        uint[] kept = [.. server.Where(s => !layout.Contains(s) && previous?.Contains(s) != true).Distinct()];
        SpellDeckLayout target = kept.Length == 0 ? layout : SpellDeckLayout.FromSlots(
            layout.Filled.Select(f => (f.Row, f.Col, f.SpellId))
                .Concat(kept.Select((spell, i) => (layout.MaxRow + 1, i, spell))));
        IReadOnlyList<SpellDeckSyncOp> ops = target.SyncOps(server);
        if (ops.Count == 0) return;
        IsSyncing = true;
        try
        {
            foreach (SpellDeckSyncOp op in ops)
                if (op.IsRemove) _actions.RemoveFavorite(tab, op.SpellId);
                else _actions.SetFavorite(tab, op.Position, op.SpellId);
        }
        finally { IsSyncing = false; }
    }
}
