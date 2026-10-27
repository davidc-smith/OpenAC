namespace AcDream.App.UI.Hud;

public enum SpellDeckMarkKind { None, Item, Spell }

/// <summary>The gold "current spell": nothing, the item spell, or a spell of a tab.
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

/// <summary>What the outline rules look at: the current tab, its visible spells
/// (<see cref="SpellDeckModel.Visible(IReadOnlyList{uint}, int, int)"/>) and whether there is an item spell.</summary>
public readonly record struct SpellDeckMarkContext(int Tab, IReadOnlyList<uint> Visible, bool HasItem);
