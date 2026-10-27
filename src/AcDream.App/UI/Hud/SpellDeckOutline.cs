namespace AcDream.App.UI.Hud;

// v1 — removed in Task 17 (SpellDeckOutlineKind, SpellDeckOutline, SpellDeckOutlineContext; v2 is SpellDeckMark below)
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

// ---- v2 ----

public enum SpellDeckMarkKind { None, Item, Spell }

/// <summary>v2 gold "current spell": nothing, the item spell, or a spell of a tab.
/// Rows are slices of one current tab, so no row id is needed.</summary>
public readonly record struct SpellDeckMark(SpellDeckMarkKind Kind, int Tab, uint SpellId)
{
    public static SpellDeckMark None => default;
    public static SpellDeckMark Item => new(SpellDeckMarkKind.Item, -1, 0u);
    public static SpellDeckMark Spell(int tab, uint spellId) => new(SpellDeckMarkKind.Spell, tab, spellId);

    /// <summary>The current tab's first visible spell, else the item spell, else nothing.</summary>
    public static SpellDeckMark Repair(in SpellDeckMarkContext ctx) =>
        ctx.Visible.Count > 0 ? Spell(ctx.Tab, ctx.Visible[0])
        : ctx.HasItem ? Item
        : None;

    /// <summary>Showing another tab: that tab's first visible spell, else the item, else nothing
    /// (<paramref name="ctx"/> describes the newly shown tab).</summary>
    public static SpellDeckMark ShowTab(in SpellDeckMarkContext ctx) => Repair(ctx);

    /// <summary>The outline after any change to the tab, its favourites, the visible range or the item:
    /// a spell still visible in the current tab keeps it (it follows a reorder); anything else is repaired.</summary>
    public static SpellDeckMark Validate(SpellDeckMark m, in SpellDeckMarkContext ctx) => m.Kind switch
    {
        SpellDeckMarkKind.Item => ctx.HasItem ? m : Repair(ctx),
        SpellDeckMarkKind.Spell => m.Tab == ctx.Tab && ctx.Visible.Contains(m.SpellId) ? m : Repair(ctx),
        _ => Repair(ctx),
    };

    /// <summary>The next of: the item spell (if any), then the visible spells in order; wrapping.</summary>
    public static SpellDeckMark Next(SpellDeckMark m, in SpellDeckMarkContext ctx) => Step(m, ctx, +1);
    public static SpellDeckMark Prev(SpellDeckMark m, in SpellDeckMarkContext ctx) => Step(m, ctx, -1);

    /// <summary>The first visible spell, else the item, else nothing.</summary>
    public static SpellDeckMark First(in SpellDeckMarkContext ctx) => Edge(ctx, last: false);
    /// <summary>The last visible spell, else the item, else nothing.</summary>
    public static SpellDeckMark Last(in SpellDeckMarkContext ctx) => Edge(ctx, last: true);

    private static SpellDeckMark Step(SpellDeckMark m, in SpellDeckMarkContext ctx, int delta)
    {
        if (m.Kind == SpellDeckMarkKind.None) return Edge(ctx, last: delta < 0);
        var cycle = new List<SpellDeckMark>(ctx.Visible.Count + 1);
        if (ctx.HasItem) cycle.Add(Item);
        foreach (uint spell in ctx.Visible) cycle.Add(Spell(ctx.Tab, spell));
        if (cycle.Count == 0) return m;
        int at = cycle.IndexOf(m);
        if (at < 0) return delta > 0 ? Edge(ctx, last: false) : Edge(ctx, last: true);
        return cycle[(at + delta + cycle.Count) % cycle.Count];
    }

    private static SpellDeckMark Edge(in SpellDeckMarkContext ctx, bool last) =>
        ctx.Visible.Count > 0 ? Spell(ctx.Tab, last ? ctx.Visible[^1] : ctx.Visible[0])
        : ctx.HasItem ? Item
        : None;
}

/// <summary>What the v2 outline rules look at: the current tab, its visible spells
/// (<see cref="SpellDeckModel.Visible(IReadOnlyList{uint}, int, int)"/>) and whether there is an item spell.</summary>
public readonly record struct SpellDeckMarkContext(int Tab, IReadOnlyList<uint> Visible, bool HasItem);
