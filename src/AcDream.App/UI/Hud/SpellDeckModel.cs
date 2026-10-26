namespace AcDream.App.UI.Hud;

/// <summary>One Spell Deck row: a stable id (survives other rows being added
/// or removed) and the favourite tab it shows (0-7).</summary>
public readonly record struct SpellDeckRow(int Id, int Tab);

/// <summary>What is being dragged: a slot's spell (Tab 0-7), or a spellbook spell (Tab -1).</summary>
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

public enum SpellDeckDropKind { Reorder, Insert, Move, Remove, RefusedFull }

public sealed record SpellDeckDropPlan(SpellDeckDropKind Kind, IReadOnlyList<SpellDeckFavoriteOp> Ops)
{
    public bool Accepted => Kind != SpellDeckDropKind.RefusedFull;
}

/// <summary>Pure Spell Deck rules. Nothing here changes state: a drop is a plan
/// the caller sends, so a refused or cancelled drag can never lose a favourite.</summary>
public static class SpellDeckModel
{
    public const int Tabs = 8;

    public static IReadOnlyList<uint> Visible(IReadOnlyList<uint> tab, int capacity) =>
        tab.Count <= capacity ? tab : tab.Take(Math.Max(0, capacity)).ToArray();

    public static int MinSlots(IReadOnlyList<SpellDeckRow> rows, Func<int, IReadOnlyList<uint>> favorites)
    {
        int most = 1;
        foreach (SpellDeckRow row in rows) most = Math.Max(most, favorites(row.Tab).Count);
        return Math.Min(most, SpellDeckGeometry.MaxCapacity);
    }

    public static int TabForNewRow(IReadOnlyList<SpellDeckRow> rows)
    {
        for (int tab = 0; tab < Tabs; tab++)
            if (!rows.Any(r => r.Tab == tab)) return tab;
        return 0;
    }

    /// <summary>A drop of <paramref name="source"/> on slot <paramref name="index"/> of a row showing <paramref name="destTab"/>.
    /// The capacity is checked against the tab's count after the drop.</summary>
    public static SpellDeckDropPlan PlanDrop(
        SpellDeckDragSource source, int destTab, int index,
        Func<int, IReadOnlyList<uint>> favorites, int capacity)
    {
        IReadOnlyList<uint> dest = favorites(destTab);
        if (dest.Contains(source.SpellId))
            return new(SpellDeckDropKind.Reorder,
                [SpellDeckFavoriteOp.Set(destTab, Math.Clamp(index, 0, dest.Count - 1), source.SpellId)]);
        if (dest.Count + 1 > capacity)
            return new(SpellDeckDropKind.RefusedFull, []);
        var set = SpellDeckFavoriteOp.Set(destTab, Math.Clamp(index, 0, dest.Count), source.SpellId);
        return source.IsFromSpellbook || source.Tab == destTab
            ? new(SpellDeckDropKind.Insert, [set])
            : new(SpellDeckDropKind.Move, [set, SpellDeckFavoriteOp.Remove(source.Tab, source.SpellId)]);
    }

    public static SpellDeckDropPlan PlanRemove(SpellDeckDragSource source) =>
        new(SpellDeckDropKind.Remove, [SpellDeckFavoriteOp.Remove(source.Tab, source.SpellId)]);
}
