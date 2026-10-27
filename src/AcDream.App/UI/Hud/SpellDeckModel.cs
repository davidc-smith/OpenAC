namespace AcDream.App.UI.Hud;

/// <summary>What is being dragged: a slot's spell (Tab 0-15; 8-15 are local), or a spellbook spell (Tab -1).</summary>
public readonly record struct SpellDeckDragSource(int Tab, uint SpellId)
{
    public static SpellDeckDragSource FromSpellbook(uint spellId) => new(-1, spellId);
    public bool IsFromSpellbook => Tab < 0;
}

/// <summary>A favourite change sent to the server (and applied locally by the runtime).</summary>
public readonly record struct SpellDeckFavoriteOp(bool IsRemove, int Tab, int Position, uint SpellId)
{
    public static SpellDeckFavoriteOp Set(int tab, int position, uint spellId) => new(false, tab, position, spellId);
    public static SpellDeckFavoriteOp Remove(int tab, uint spellId) => new(true, tab, -1, spellId);
}

/// <summary><see cref="None"/>: an accepted drop with nothing to do (a bookmark drop on the
/// spell's own tab, or on a tab that already holds it).</summary>
public enum SpellDeckDropKind { Reorder, Insert, Move, Remove, RefusedFull, None }

public sealed record SpellDeckDropPlan(SpellDeckDropKind Kind, IReadOnlyList<SpellDeckFavoriteOp> Ops)
{
    public bool Accepted => Kind != SpellDeckDropKind.RefusedFull;
}

/// <summary>Pure Spell Deck rules. Nothing here changes state: a drop is a plan
/// the caller sends, so a refused or cancelled drag can never lose a favourite.</summary>
public static class SpellDeckModel
{
    // One current tab; row r shows spells r·perRow … r·perRow + perRow − 1 of it.

    /// <summary>The spells row <paramref name="row"/> (0-based) shows: <c>row·perRow … row·perRow + perRow − 1</c> of the tab
    /// (shorter, or empty, where the tab ends).</summary>
    public static IReadOnlyList<uint> Slice(IReadOnlyList<uint> tab, int row, int perRow)
    {
        if (row < 0 || perRow <= 0) return [];
        long start = (long)row * perRow;
        if (start >= tab.Count) return [];
        return tab.Skip((int)start).Take(perRow).ToArray();
    }

    /// <summary>The spells the deck shows (and can cast): the first <c>rows·perRow</c> of the tab.</summary>
    public static IReadOnlyList<uint> Visible(IReadOnlyList<uint> tab, int rows, int perRow)
    {
        int capacity = (int)Math.Clamp((long)Math.Max(0, rows) * Math.Max(0, perRow), 0, int.MaxValue);
        return tab.Count <= capacity ? tab : tab.Take(capacity).ToArray();
    }

    /// <summary>The fewest slots per row with which <paramref name="rows"/> rows hold the fullest of all tabs
    /// (server and local): <c>ceil(max / rows)</c>, at least 1, capped at <see cref="SpellDeckGeometry.MaxCapacity"/>.</summary>
    public static int MinSlotsPerRow(int rows, IEnumerable<int> allTabCounts)
    {
        int most = Most(allTabCounts);
        rows = Math.Max(1, rows);
        int need = (most + rows - 1) / rows;
        return Math.Clamp(need, 1, Math.Max(1, SpellDeckGeometry.MaxCapacity));
    }

    /// <summary>Remove row is allowed when there is more than one row and the remaining rows,
    /// at the widest stretch, still hold the fullest of all tabs.</summary>
    public static bool CanRemoveRow(int rows, IEnumerable<int> allTabCounts) =>
        rows > 1 && (long)(rows - 1) * SpellDeckGeometry.MaxCapacity >= Most(allTabCounts);

    /// <summary>A drop on a slot at <paramref name="position"/> (<c>r·perRow + n</c>) of <paramref name="tab"/>, whose spells are
    /// <paramref name="favorites"/>. A spell already in the tab (or a slot from this tab) is a reorder, always allowed, to
    /// <c>min(position, count − 1)</c>; a new spell is inserted at <c>min(position, count)</c> when <c>count + 1 &lt;= capacity</c>,
    /// else refused. <paramref name="capacity"/> is <c>rows·perRow</c>.</summary>
    public static SpellDeckDropPlan PlanDropOnSlot(
        SpellDeckDragSource source, int tab, int position, IReadOnlyList<uint> favorites, int capacity)
    {
        bool sameTabSlot = !source.IsFromSpellbook && source.Tab == tab;
        if (sameTabSlot || favorites.Contains(source.SpellId))
            return new(SpellDeckDropKind.Reorder,
                [SpellDeckFavoriteOp.Set(tab, Math.Clamp(position, 0, Math.Max(0, favorites.Count - 1)), source.SpellId)]);
        if (favorites.Count + 1 > capacity)
            return new(SpellDeckDropKind.RefusedFull, []);
        return new(SpellDeckDropKind.Insert,
            [SpellDeckFavoriteOp.Set(tab, Math.Clamp(position, 0, favorites.Count), source.SpellId)]);
    }

    /// <summary>A drop on <paramref name="destTab"/>'s bookmark, whose spells are <paramref name="destFavorites"/>.
    /// Its own tab, or a tab that already holds the spell: <see cref="SpellDeckDropKind.None"/> (no ops, not refused).
    /// A full tab (<c>count + 1 &gt; capacity</c>): refused. A spellbook spell: inserted at the end. A slot from another tab:
    /// moved to the end (add there first, then remove from its tab).</summary>
    public static SpellDeckDropPlan PlanDropOnTab(
        SpellDeckDragSource source, int destTab, IReadOnlyList<uint> destFavorites, int capacity)
    {
        if ((!source.IsFromSpellbook && source.Tab == destTab) || destFavorites.Contains(source.SpellId))
            return new(SpellDeckDropKind.None, []);
        if (destFavorites.Count + 1 > capacity)
            return new(SpellDeckDropKind.RefusedFull, []);
        var add = SpellDeckFavoriteOp.Set(destTab, destFavorites.Count, source.SpellId);
        return source.IsFromSpellbook
            ? new(SpellDeckDropKind.Insert, [add])
            : new(SpellDeckDropKind.Move, [add, SpellDeckFavoriteOp.Remove(source.Tab, source.SpellId)]);
    }

    private static int Most(IEnumerable<int> counts)
    {
        int most = 0;
        foreach (int c in counts) most = Math.Max(most, c);
        return most;
    }

    public static SpellDeckDropPlan PlanRemove(SpellDeckDragSource source) =>
        new(SpellDeckDropKind.Remove, [SpellDeckFavoriteOp.Remove(source.Tab, source.SpellId)]);
}
