namespace AcDream.App.UI.Hud;

public enum SpellDeckMarkKind { None, Item, Slot }

/// <summary>The gold "current spell": nothing, the item spell, or a slot <c>(row, column)</c> of a tab.
/// The outline marks the slot, not its spell: a spell moved away leaves it behind, and a slot that is
/// cleared (or no longer shown) is repaired.</summary>
public readonly record struct SpellDeckMark(SpellDeckMarkKind Kind, int Tab, int Row, int Col)
{
    public static SpellDeckMark None => default;
    public static SpellDeckMark Item => new(SpellDeckMarkKind.Item, -1, -1, -1);
    public static SpellDeckMark Slot(int tab, int row, int col) => new(SpellDeckMarkKind.Slot, tab, row, col);

    /// <summary>The current tab's first filled shown slot, else the item spell, else nothing.</summary>
    public static SpellDeckMark Repair(in SpellDeckMarkContext ctx) => Edge(ctx, last: false);

    /// <summary>Showing another tab: its first filled shown slot, else the item, else nothing
    /// (<paramref name="ctx"/> describes the newly shown tab).</summary>
    public static SpellDeckMark ShowTab(in SpellDeckMarkContext ctx) => Repair(ctx);

    /// <summary>The outline after any change to the tab, its layout, the shown slots or the item: a filled,
    /// shown slot of the current tab keeps it; anything else is repaired.</summary>
    public static SpellDeckMark Validate(SpellDeckMark m, in SpellDeckMarkContext ctx) => m.Kind switch
    {
        SpellDeckMarkKind.Item => ctx.HasItem ? m : Repair(ctx),
        SpellDeckMarkKind.Slot => m.Tab == ctx.Tab && ctx.Filled.Contains(new SpellDeckSlot(m.Row, m.Col)) ? m : Repair(ctx),
        _ => Repair(ctx),
    };

    /// <summary>The next of: the item spell (if any), then the filled shown slots in reading order; wrapping.</summary>
    public static SpellDeckMark Next(SpellDeckMark m, in SpellDeckMarkContext ctx) => Step(m, ctx, +1);
    public static SpellDeckMark Prev(SpellDeckMark m, in SpellDeckMarkContext ctx) => Step(m, ctx, -1);

    /// <summary>The first filled shown slot, else the item, else nothing.</summary>
    public static SpellDeckMark First(in SpellDeckMarkContext ctx) => Edge(ctx, last: false);
    /// <summary>The last filled shown slot, else the item, else nothing.</summary>
    public static SpellDeckMark Last(in SpellDeckMarkContext ctx) => Edge(ctx, last: true);

    private static SpellDeckMark Step(SpellDeckMark m, in SpellDeckMarkContext ctx, int delta)
    {
        if (m.Kind == SpellDeckMarkKind.None) return Edge(ctx, last: delta < 0);
        var cycle = new List<SpellDeckMark>(ctx.Filled.Count + 1);
        if (ctx.HasItem) cycle.Add(Item);
        foreach (SpellDeckSlot s in ctx.Filled) cycle.Add(Slot(ctx.Tab, s.Row, s.Col));
        if (cycle.Count == 0) return m;
        int at = cycle.IndexOf(m);
        if (at < 0) return delta > 0 ? Edge(ctx, last: false) : Edge(ctx, last: true);
        return cycle[(at + delta + cycle.Count) % cycle.Count];
    }

    private static SpellDeckMark Edge(in SpellDeckMarkContext ctx, bool last) =>
        ctx.Filled.Count > 0 ? Slot(ctx.Tab, (last ? ctx.Filled[^1] : ctx.Filled[0]).Row, (last ? ctx.Filled[^1] : ctx.Filled[0]).Col)
        : ctx.HasItem ? Item
        : None;
}

/// <summary>What the outline rules look at: the current tab, its filled shown slots in reading order, and
/// whether there is an item spell.</summary>
public readonly record struct SpellDeckMarkContext(int Tab, IReadOnlyList<SpellDeckSlot> Filled, bool HasItem);
