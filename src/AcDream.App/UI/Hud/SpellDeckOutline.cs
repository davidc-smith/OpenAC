namespace AcDream.App.UI.Hud;

public enum SpellDeckOutlineKind { None, Item, Slot }

/// <summary>The gold "current spell": nothing, the item spell, or one spell in
/// one row (by stable row id, so rows showing the same tab are told apart).</summary>
public readonly record struct SpellDeckOutline(SpellDeckOutlineKind Kind, int RowId, int Tab, uint SpellId)
{
    public static SpellDeckOutline None => default;
    public static SpellDeckOutline Item => new(SpellDeckOutlineKind.Item, 0, -1, 0u);
    public static SpellDeckOutline ForSlot(int rowId, int tab, uint spellId) => new(SpellDeckOutlineKind.Slot, rowId, tab, spellId);

    /// <summary>Row 0's first filled slot, else the item, else nothing.</summary>
    public static SpellDeckOutline Repair(in SpellDeckOutlineContext ctx) =>
        ctx.Rows.Count > 0 && FirstOf(ctx, ctx.Rows[0]) is { } first ? first
        : ctx.HasItem ? Item
        : None;

    /// <summary>The outline after any change to rows, favourites or the item (the outline table's state rows).</summary>
    public static SpellDeckOutline Validate(SpellDeckOutline o, in SpellDeckOutlineContext ctx)
    {
        switch (o.Kind)
        {
            case SpellDeckOutlineKind.Item:
                return ctx.HasItem ? o : Repair(ctx);
            case SpellDeckOutlineKind.Slot:
                int index = ctx.IndexOfRow(o.RowId);
                if (index < 0) return Repair(ctx);
                SpellDeckRow row = ctx.Rows[index];
                if (row.Tab != o.Tab) return FirstOf(ctx, row) ?? Repair(ctx);
                return ctx.Visible(row.Tab).Contains(o.SpellId) ? o : Repair(ctx);
            default:
                return Repair(ctx);
        }
    }

    public static SpellDeckOutline Next(SpellDeckOutline o, in SpellDeckOutlineContext ctx) => Step(o, ctx, +1);
    public static SpellDeckOutline Prev(SpellDeckOutline o, in SpellDeckOutlineContext ctx) => Step(o, ctx, -1);

    public static SpellDeckOutline First(SpellDeckOutline o, in SpellDeckOutlineContext ctx) => Edge(o, ctx, last: false);
    public static SpellDeckOutline Last(SpellDeckOutline o, in SpellDeckOutlineContext ctx) => Edge(o, ctx, last: true);

    private static SpellDeckOutline Step(SpellDeckOutline o, in SpellDeckOutlineContext ctx, int delta)
    {
        if (o.Kind == SpellDeckOutlineKind.None) return Edge(o, ctx, last: delta < 0);
        SpellDeckRow row = o.Kind == SpellDeckOutlineKind.Item || ctx.IndexOfRow(o.RowId) < 0
            ? (ctx.Rows.Count > 0 ? ctx.Rows[0] : default)
            : ctx.Rows[ctx.IndexOfRow(o.RowId)];
        // The cycle for this row: the item first when this is row 0 and there is one, then the filled slots.
        var cycle = new List<SpellDeckOutline>();
        bool isRow0 = ctx.Rows.Count > 0 && ctx.Rows[0].Id == row.Id;
        if (isRow0 && ctx.HasItem) cycle.Add(Item);
        foreach (uint spell in ctx.Visible(row.Tab)) cycle.Add(ForSlot(row.Id, row.Tab, spell));
        if (cycle.Count == 0) return o;
        int at = cycle.IndexOf(o);
        if (at < 0) return delta > 0 ? cycle[0] : cycle[^1];
        return cycle[(at + delta + cycle.Count) % cycle.Count];
    }

    private static SpellDeckOutline Edge(SpellDeckOutline o, in SpellDeckOutlineContext ctx, bool last)
    {
        int index = o.Kind == SpellDeckOutlineKind.Slot ? ctx.IndexOfRow(o.RowId) : 0;
        if (index >= 0 && index < ctx.Rows.Count)
        {
            SpellDeckRow row = ctx.Rows[index];
            IReadOnlyList<uint> spells = ctx.Visible(row.Tab);
            if (spells.Count > 0) return ForSlot(row.Id, row.Tab, last ? spells[^1] : spells[0]);
        }
        return ctx.HasItem ? Item : None;
    }

    private static SpellDeckOutline? FirstOf(in SpellDeckOutlineContext ctx, SpellDeckRow row)
    {
        IReadOnlyList<uint> spells = ctx.Visible(row.Tab);
        return spells.Count > 0 ? ForSlot(row.Id, row.Tab, spells[0]) : null;
    }
}

/// <summary>What the outline rules look at: the rows in order, each tab's visible spells, and whether there is an item spell.</summary>
public readonly record struct SpellDeckOutlineContext(
    IReadOnlyList<SpellDeckRow> Rows, Func<int, IReadOnlyList<uint>> Visible, bool HasItem)
{
    public int IndexOfRow(int id)
    {
        for (int i = 0; i < Rows.Count; i++) if (Rows[i].Id == id) return i;
        return -1;
    }
}
