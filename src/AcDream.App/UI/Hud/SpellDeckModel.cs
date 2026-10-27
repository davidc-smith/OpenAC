namespace AcDream.App.UI.Hud;

/// <summary>What is being dragged: a slot's spell (Tab 0-15, 8-15 local; the slot it was lifted from),
/// or a spellbook spell (Tab, Row and Col -1).</summary>
public readonly record struct SpellDeckDragSource(int Tab, int Row, int Col, uint SpellId)
{
    public static SpellDeckDragSource FromSpellbook(uint spellId) => new(-1, -1, -1, spellId);
    public bool IsFromSpellbook => Tab < 0;
}

/// <summary>A favourite change sent to the server (and applied locally by the runtime).</summary>
public readonly record struct SpellDeckFavoriteOp(bool IsRemove, int Tab, int Position, uint SpellId)
{
    public static SpellDeckFavoriteOp Set(int tab, int position, uint spellId) => new(false, tab, position, spellId);
    public static SpellDeckFavoriteOp Remove(int tab, uint spellId) => new(true, tab, -1, spellId);
}

/// <summary><see cref="None"/>: an accepted drop with nothing to do (a slot dropped on itself or on its own
/// tab's bookmark, or a spell dropped on a slot that already holds it).</summary>
public enum SpellDeckEditKind { Place, Replace, Move, Swap, Copy, Clear, RefusedFull, None }

/// <summary>A tab's new layout.</summary>
public readonly record struct SpellDeckTabLayout(int Tab, SpellDeckLayout Layout);

/// <summary>A planned edit: the new layout of each tab it changes, in the order to apply them (a move's
/// destination before its source).</summary>
public sealed record SpellDeckEdit(SpellDeckEditKind Kind, IReadOnlyList<SpellDeckTabLayout> Layouts)
{
    public bool Accepted => Kind != SpellDeckEditKind.RefusedFull;
    public static SpellDeckEdit Nothing { get; } = new(SpellDeckEditKind.None, []);
    public static SpellDeckEdit Full { get; } = new(SpellDeckEditKind.RefusedFull, []);
}

/// <summary>Pure Spell Deck rules over slot layouts. Nothing here changes state: a drop is a plan the caller
/// applies, so a refused or cancelled drag can never lose a spell. A plan is null when its slot source no
/// longer holds the dragged spell (the layout changed during the drag).</summary>
public static class SpellDeckModel
{
    /// <summary>A drop on slot (<paramref name="row"/>, <paramref name="col"/>) of <paramref name="tab"/>.
    /// A spellbook spell is placed in an empty slot or replaces a filled one. A slot's spell moves to an empty
    /// slot or swaps with a filled one (across tabs too); with <paramref name="copy"/> it is copied instead,
    /// overwriting the target and keeping the source.</summary>
    public static SpellDeckEdit? PlanDropOnSlot(
        SpellDeckDragSource source, int tab, int row, int col, bool copy, Func<int, SpellDeckLayout> layoutOf)
    {
        if (row < 0 || col < 0 || !Holds(source, layoutOf)) return null;
        SpellDeckLayout dest = layoutOf(tab);
        uint target = dest.Get(row, col);
        uint spell = source.SpellId;
        if (source.IsFromSpellbook || copy)
        {
            if (target == spell) return SpellDeckEdit.Nothing;
            SpellDeckEditKind kind = copy && !source.IsFromSpellbook ? SpellDeckEditKind.Copy
                : target == 0 ? SpellDeckEditKind.Place : SpellDeckEditKind.Replace;
            return new(kind, [new(tab, dest.Replace(row, col, spell))]);
        }
        if (source.Tab == tab)
        {
            if ((source.Row == row && source.Col == col) || target == spell) return SpellDeckEdit.Nothing;
            return target == 0
                ? new(SpellDeckEditKind.Move, [new(tab, dest.Move(source.Row, source.Col, row, col))])
                : new(SpellDeckEditKind.Swap, [new(tab, dest.Swap(source.Row, source.Col, row, col))]);
        }
        if (target == spell) return SpellDeckEdit.Nothing;
        SpellDeckLayout from = layoutOf(source.Tab);
        return target == 0
            ? new(SpellDeckEditKind.Move,
                [new(tab, dest.Replace(row, col, spell)), new(source.Tab, from.Clear(source.Row, source.Col))])
            : new(SpellDeckEditKind.Swap,
                [new(tab, dest.Replace(row, col, spell)), new(source.Tab, from.Replace(source.Row, source.Col, target))]);
    }

    /// <summary>A drop on <paramref name="tab"/>'s bookmark: the spell goes into that tab's first empty slot within
    /// <paramref name="rows"/> × <paramref name="cols"/>; refused when there is none. A slot's spell moves there
    /// (destination first, then the source slot is cleared), or with <paramref name="copy"/> is copied. A slot
    /// dropped on its own tab's bookmark without copy does nothing.</summary>
    public static SpellDeckEdit? PlanDropOnTab(
        SpellDeckDragSource source, int tab, bool copy, int rows, int cols, Func<int, SpellDeckLayout> layoutOf)
    {
        if (!Holds(source, layoutOf)) return null;
        if (!source.IsFromSpellbook && source.Tab == tab && !copy) return SpellDeckEdit.Nothing;
        SpellDeckLayout dest = layoutOf(tab);
        if (dest.FirstEmpty(rows, cols) is not { } empty) return SpellDeckEdit.Full;
        var placed = new SpellDeckTabLayout(tab, dest.Place(empty.Row, empty.Col, source.SpellId));
        if (source.IsFromSpellbook) return new(SpellDeckEditKind.Place, [placed]);
        if (copy) return new(SpellDeckEditKind.Copy, [placed]);
        return new(SpellDeckEditKind.Move, [placed, new(source.Tab, layoutOf(source.Tab).Clear(source.Row, source.Col))]);
    }

    /// <summary>A slot dragged off every window (or Remove from tab): that slot is cleared. Null for a
    /// spellbook spell.</summary>
    public static SpellDeckEdit? PlanClear(SpellDeckDragSource source, Func<int, SpellDeckLayout> layoutOf) =>
        source.IsFromSpellbook || !Holds(source, layoutOf) ? null
        : new(SpellDeckEditKind.Clear, [new(source.Tab, layoutOf(source.Tab).Clear(source.Row, source.Col))]);

    /// <summary>The fewest slots per row that show every tab's right-most occupied column; at least 1.</summary>
    public static int MinColumns(IEnumerable<SpellDeckLayout> all) => Math.Max(1, all.Select(l => l.MaxCol + 1).DefaultIfEmpty(0).Max());

    /// <summary>The fewest rows that show every tab's lowest occupied row; at least 1.</summary>
    public static int MinRows(IEnumerable<SpellDeckLayout> all) => Math.Max(1, all.Select(l => l.MaxRow + 1).DefaultIfEmpty(0).Max());

    /// <summary>Remove row is allowed when there is more than one row and no tab holds a spell in the last one.</summary>
    public static bool CanRemoveRow(int rows, IEnumerable<SpellDeckLayout> all) => rows > 1 && rows - 1 >= MinRows(all);

    /// <summary>A spellbook source always holds its spell; a slot source only while its slot still holds it.</summary>
    private static bool Holds(SpellDeckDragSource source, Func<int, SpellDeckLayout> layoutOf) =>
        source.SpellId != 0
        && (source.IsFromSpellbook
            || (source.Row >= 0 && source.Col >= 0 && layoutOf(source.Tab).Get(source.Row, source.Col) == source.SpellId));
}
