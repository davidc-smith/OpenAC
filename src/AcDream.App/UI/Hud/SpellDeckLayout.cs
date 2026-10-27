using System.Collections.Immutable;

namespace AcDream.App.UI.Hud;

/// <summary>A Spell Deck slot: row and column, both 0-based. Ordered in reading order (row by row, left to right).</summary>
public readonly record struct SpellDeckSlot(int Row, int Col) : IComparable<SpellDeckSlot>
{
    public int CompareTo(SpellDeckSlot other) =>
        Row != other.Row ? Row.CompareTo(other.Row) : Col.CompareTo(other.Col);
}

/// <summary>One filled slot of a <see cref="SpellDeckLayout"/>.</summary>
public readonly record struct SpellDeckSlotSpell(int Row, int Col, uint SpellId);

/// <summary>A server favourite change for one tab, in the order to send it.
/// <see cref="Set"/> inserts the spell at <c>min(position, count)</c>; ACE's <c>AddSpellToBar</c> ignores it when
/// the spell is already in the bar (the client's <c>Spellbook.SetFavorite</c> removes and re-inserts it instead),
/// so a spell is only ever Set when it is not in the list. <see cref="Remove"/> removes it.</summary>
public readonly record struct SpellDeckSyncOp(bool IsRemove, int Position, uint SpellId)
{
    public static SpellDeckSyncOp Set(int position, uint spellId) => new(false, position, spellId);
    public static SpellDeckSyncOp Remove(uint spellId) => new(true, -1, spellId);

    public SpellDeckFavoriteOp ToFavoriteOp(int tab) =>
        IsRemove ? SpellDeckFavoriteOp.Remove(tab, SpellId) : SpellDeckFavoriteOp.Set(tab, Position, SpellId);
}

/// <summary>The result of <see cref="SpellDeckLayout.Reconcile"/>: the reconciled layout and the server
/// spells that had no empty slot within the given rows × columns (in server order).</summary>
public sealed record SpellDeckReconcile(SpellDeckLayout Layout, IReadOnlyList<uint> Unplaced);

/// <summary>
/// One tab's slot layout: which slot <c>(row, column)</c> holds which spell. Slots may be empty and the
/// same spell may sit in several slots. Immutable: every edit returns a new layout (or this one when
/// nothing changes). Spell id 0 means an empty slot and is never stored.
/// <para>The server keeps each favourite tab as a dense ordered list with each spell once; the layout's
/// counterpart is <see cref="Distinct"/> (each spell at its first slot in reading order), and
/// <see cref="SyncOps"/> plans the server changes that make the server list equal it.</para>
/// </summary>
public sealed class SpellDeckLayout : IEquatable<SpellDeckLayout>
{
    public static readonly SpellDeckLayout Empty = new(ImmutableSortedDictionary<SpellDeckSlot, uint>.Empty);

    private readonly ImmutableSortedDictionary<SpellDeckSlot, uint> _slots;
    private IReadOnlyList<SpellDeckSlotSpell>? _filled;
    private IReadOnlyList<uint>? _distinct;

    private SpellDeckLayout(ImmutableSortedDictionary<SpellDeckSlot, uint> slots) => _slots = slots;

    /// <summary>A layout holding <paramref name="slots"/> (spell 0 entries are skipped; a later entry for
    /// the same slot wins).</summary>
    public static SpellDeckLayout FromSlots(IEnumerable<(int Row, int Col, uint Spell)> slots)
    {
        ImmutableSortedDictionary<SpellDeckSlot, uint>.Builder b = ImmutableSortedDictionary.CreateBuilder<SpellDeckSlot, uint>();
        foreach ((int row, int col, uint spell) in slots)
        {
            CheckSlot(row, col);
            if (spell == 0) b.Remove(new SpellDeckSlot(row, col));
            else b[new SpellDeckSlot(row, col)] = spell;
        }
        return b.Count == 0 ? Empty : new SpellDeckLayout(b.ToImmutable());
    }

    /// <summary><paramref name="list"/> laid out in reading order, <paramref name="cols"/> per row
    /// (v2 migration, or a tab with no saved layout). A 0 entry leaves its slot empty.</summary>
    public static SpellDeckLayout FromList(IReadOnlyList<uint> list, int cols)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(cols, 1);
        return FromSlots(list.Select((spell, i) => (i / cols, i % cols, spell)));
    }

    public bool IsEmpty => _slots.Count == 0;

    /// <summary>The spell in the slot, or 0 when it is empty (or the slot is negative).</summary>
    public uint Get(int row, int col) => _slots.TryGetValue(new SpellDeckSlot(row, col), out uint spell) ? spell : 0;

    /// <summary>Whether any slot holds <paramref name="spellId"/>.</summary>
    public bool Contains(uint spellId) => spellId != 0 && _slots.ContainsValue(spellId);

    /// <summary>The filled slots in reading order.</summary>
    public IReadOnlyList<SpellDeckSlotSpell> Filled =>
        _filled ??= _slots.Select(kv => new SpellDeckSlotSpell(kv.Key.Row, kv.Key.Col, kv.Value)).ToArray();

    /// <summary>Each spell once, at its first slot in reading order: what the server list should be.</summary>
    public IReadOnlyList<uint> Distinct => _distinct ??= _slots.Values.Distinct().ToArray();

    /// <summary>The highest occupied row, or −1 when empty.</summary>
    public int MaxRow => IsEmpty ? -1 : _slots.Keys.Last().Row;

    /// <summary>The highest occupied column in any row, or −1 when empty.</summary>
    public int MaxCol => IsEmpty ? -1 : _slots.Keys.Max(s => s.Col);

    /// <summary>The first empty slot in reading order within <paramref name="rows"/> × <paramref name="cols"/>, or null when they are all filled.</summary>
    public SpellDeckSlot? FirstEmpty(int rows, int cols)
    {
        for (int r = 0; r < rows; r++)
            for (int c = 0; c < cols; c++)
                if (!_slots.ContainsKey(new SpellDeckSlot(r, c))) return new SpellDeckSlot(r, c);
        return null;
    }

    /// <summary>Puts <paramref name="spellId"/> in an empty slot. A filled slot is left as it is (use <see cref="Replace"/>).</summary>
    public SpellDeckLayout Place(int row, int col, uint spellId)
    {
        CheckSlot(row, col);
        ArgumentOutOfRangeException.ThrowIfZero(spellId);
        return _slots.ContainsKey(new SpellDeckSlot(row, col)) ? this : With(_slots.Add(new SpellDeckSlot(row, col), spellId));
    }

    /// <summary>Puts <paramref name="spellId"/> in the slot, whatever it held.</summary>
    public SpellDeckLayout Replace(int row, int col, uint spellId)
    {
        CheckSlot(row, col);
        ArgumentOutOfRangeException.ThrowIfZero(spellId);
        return Get(row, col) == spellId ? this : With(_slots.SetItem(new SpellDeckSlot(row, col), spellId));
    }

    /// <summary>Empties the slot.</summary>
    public SpellDeckLayout Clear(int row, int col) =>
        _slots.ContainsKey(new SpellDeckSlot(row, col)) ? With(_slots.Remove(new SpellDeckSlot(row, col))) : this;

    /// <summary>Moves the spell in <c>from</c> to <c>to</c>, overwriting what <c>to</c> held, and empties <c>from</c>.
    /// Nothing changes when <c>from</c> is empty or the same slot.</summary>
    public SpellDeckLayout Move(int fromRow, int fromCol, int toRow, int toCol)
    {
        CheckSlot(toRow, toCol);
        uint spell = Get(fromRow, fromCol);
        if (spell == 0 || (fromRow == toRow && fromCol == toCol)) return this;
        return With(_slots.Remove(new SpellDeckSlot(fromRow, fromCol)).SetItem(new SpellDeckSlot(toRow, toCol), spell));
    }

    /// <summary>Exchanges the two slots' contents (with an empty slot, a move).</summary>
    public SpellDeckLayout Swap(int rowA, int colA, int rowB, int colB)
    {
        CheckSlot(rowA, colA);
        CheckSlot(rowB, colB);
        uint a = Get(rowA, colA), b = Get(rowB, colB);
        if (a == b) return this;
        ImmutableSortedDictionary<SpellDeckSlot, uint> s = _slots;
        s = b == 0 ? s.Remove(new SpellDeckSlot(rowA, colA)) : s.SetItem(new SpellDeckSlot(rowA, colA), b);
        s = a == 0 ? s.Remove(new SpellDeckSlot(rowB, colB)) : s.SetItem(new SpellDeckSlot(rowB, colB), a);
        return With(s);
    }

    /// <summary>Copies the spell in <c>from</c> to <c>to</c>, overwriting what <c>to</c> held. Nothing changes when <c>from</c> is empty.</summary>
    public SpellDeckLayout Copy(int fromRow, int fromCol, int toRow, int toCol)
    {
        uint spell = Get(fromRow, fromCol);
        return spell == 0 ? this : Replace(toRow, toCol, spell);
    }

    /// <summary>
    /// Brings the layout in line with the server list: every copy of a spell not in
    /// <paramref name="serverList"/> is cleared; each server spell the layout lacks goes into the first empty
    /// slot in reading order within <paramref name="rows"/> × <paramref name="cols"/> (in server order).
    /// Spells already placed keep their slots whatever the server order. Server spells with no empty slot are
    /// returned in <see cref="SpellDeckReconcile.Unplaced"/>.
    /// </summary>
    public SpellDeckReconcile Reconcile(IReadOnlyList<uint> serverList, int rows, int cols)
    {
        var onServer = new HashSet<uint>(serverList);
        ImmutableSortedDictionary<SpellDeckSlot, uint>.Builder b = _slots.ToBuilder();
        foreach (KeyValuePair<SpellDeckSlot, uint> kv in _slots)
            if (!onServer.Contains(kv.Value)) b.Remove(kv.Key);

        var present = new HashSet<uint>(b.Values);
        var unplaced = new List<uint>();
        int next = 0;
        int capacity = Math.Max(0, rows) * Math.Max(0, cols);
        foreach (uint spell in serverList)
        {
            if (spell == 0 || !present.Add(spell)) continue;
            while (next < capacity && b.ContainsKey(new SpellDeckSlot(next / cols, next % cols))) next++;
            if (next >= capacity) { unplaced.Add(spell); continue; }
            b[new SpellDeckSlot(next / cols, next % cols)] = spell;
        }

        SpellDeckLayout layout = b.Count == _slots.Count && b.All(kv => _slots.TryGetValue(kv.Key, out uint v) && v == kv.Value)
            ? this
            : b.Count == 0 ? Empty : new SpellDeckLayout(b.ToImmutable());
        return new SpellDeckReconcile(layout, unplaced);
    }

    /// <summary>
    /// The server changes that make <paramref name="serverList"/> (a tab's dense list, each spell once) equal
    /// <see cref="Distinct"/>, when applied in order with ACE's rules (a Set of a spell already in the list is
    /// ignored) or the client's (a Set removes the spell first): first a <see cref="SpellDeckSyncOp.Remove"/> for
    /// each server spell the layout no longer holds, then, in reading order, a <see cref="SpellDeckSyncOp.Set"/>
    /// for each spell that is new, or a Remove then a Set for each spell that is out of place. Spells on the
    /// longest run already in the right relative order are not touched, so one moved spell is one Remove and
    /// one Set.
    /// </summary>
    public IReadOnlyList<SpellDeckSyncOp> SyncOps(IReadOnlyList<uint> serverList)
    {
        IReadOnlyList<uint> target = Distinct;
        var targetIndex = new Dictionary<uint, int>(target.Count);
        for (int i = 0; i < target.Count; i++) targetIndex[target[i]] = i;

        var ops = new List<SpellDeckSyncOp>();
        var work = new List<uint>(serverList.Count);
        foreach (uint spell in serverList)
        {
            if (targetIndex.ContainsKey(spell)) { if (!work.Contains(spell)) work.Add(spell); }
            else if (!ops.Contains(SpellDeckSyncOp.Remove(spell))) ops.Add(SpellDeckSyncOp.Remove(spell));
        }

        // Spells that stay put: the longest run of the server list already in target order.
        HashSet<uint> keep = LongestIncreasingRun(work, targetIndex);

        // In target order, put each other spell right after its target predecessor. Kept and already-placed
        // spells are always in target order relative to each other, so the list ends equal to the target.
        for (int i = 0; i < target.Count; i++)
        {
            uint spell = target[i];
            if (keep.Contains(spell)) continue;
            if (work.Remove(spell)) ops.Add(SpellDeckSyncOp.Remove(spell));   // ACE ignores a Set of a spell it has
            int position = i == 0 ? 0 : work.IndexOf(target[i - 1]) + 1;
            work.Insert(position, spell);
            ops.Add(SpellDeckSyncOp.Set(position, spell));
        }
        return ops;
    }

    private static HashSet<uint> LongestIncreasingRun(List<uint> work, Dictionary<uint, int> targetIndex)
    {
        int n = work.Count;
        var tails = new List<int>();     // tails[k] = index in work of the smallest tail of a run of length k+1
        var previous = new int[n];
        for (int i = 0; i < n; i++)
        {
            int value = targetIndex[work[i]];
            int lo = 0, hi = tails.Count;
            while (lo < hi)
            {
                int mid = (lo + hi) / 2;
                if (targetIndex[work[tails[mid]]] < value) lo = mid + 1; else hi = mid;
            }
            previous[i] = lo > 0 ? tails[lo - 1] : -1;
            if (lo == tails.Count) tails.Add(i); else tails[lo] = i;
        }
        var keep = new HashSet<uint>();
        for (int i = tails.Count > 0 ? tails[^1] : -1; i >= 0; i = previous[i]) keep.Add(work[i]);
        return keep;
    }

    private SpellDeckLayout With(ImmutableSortedDictionary<SpellDeckSlot, uint> slots) =>
        slots.Count == 0 ? Empty : new SpellDeckLayout(slots);

    private static void CheckSlot(int row, int col)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(row);
        ArgumentOutOfRangeException.ThrowIfNegative(col);
    }

    public bool Equals(SpellDeckLayout? other) =>
        other is not null && (ReferenceEquals(this, other) || Filled.SequenceEqual(other.Filled));

    public override bool Equals(object? obj) => Equals(obj as SpellDeckLayout);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        foreach (KeyValuePair<SpellDeckSlot, uint> kv in _slots) { hash.Add(kv.Key); hash.Add(kv.Value); }
        return hash.ToHashCode();
    }

    public override string ToString() => "[" + string.Join(", ", Filled.Select(s => $"({s.Row},{s.Col})={s.SpellId}")) + "]";
}
