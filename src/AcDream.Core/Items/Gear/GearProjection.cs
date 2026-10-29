namespace AcDream.Core.Items.Gear;

/// <summary>One item as the query bar sees it: public data only (what create and update messages carry).</summary>
public sealed record GearItemView(
    uint Id, string Name, GearTypeGroup Group, uint Material, float Workmanship,
    int Value, int Burden, int Stack, int StackMax,
    uint PackId, int PackIndex, int Slot, bool Worn, bool IsPack);

/// <summary>
/// The Gear Deck's items as flat rows. <see cref="ForPack"/> keeps the model's
/// slot order, so a row's <see cref="GearItemView.Slot"/> is the index a drop
/// on it uses. <see cref="All"/> is what a query searches.
/// </summary>
public sealed class GearProjection
{
    public static GearProjection Empty { get; } = new([], new Dictionary<uint, IReadOnlyList<GearItemView>>(), 0);

    private readonly IReadOnlyDictionary<uint, IReadOnlyList<GearItemView>> _byPack;

    private GearProjection(IReadOnlyList<GearItemView> all, IReadOnlyDictionary<uint, IReadOnlyList<GearItemView>> byPack, int packItems)
    {
        All = all;
        _byPack = byPack;
        PackItemCount = packItems;
    }

    public IReadOnlyList<GearItemView> All { get; }
    public int PackItemCount { get; }

    public IReadOnlyList<GearItemView> ForPack(uint packId) =>
        _byPack.TryGetValue(packId, out IReadOnlyList<GearItemView>? rows) ? rows : [];

    public static GearProjection Build(GearInventoryModel model, ClientObjectTable objects, Func<ClientObject, string> displayName)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(objects);
        ArgumentNullException.ThrowIfNull(displayName);
        if (model.Packs.Count == 0) return Empty;

        var all = new List<GearItemView>();
        var byPack = new Dictionary<uint, IReadOnlyList<GearItemView>>();
        for (int p = 0; p < model.Packs.Count; p++)
        {
            GearPack pack = model.Packs[p];
            IReadOnlyList<ClientObject> contents = model.Contents(pack.Id);
            var rows = new List<GearItemView>(contents.Count);
            for (int s = 0; s < contents.Count; s++)
                rows.Add(View(contents[s], displayName, pack.Id, p, s, worn: false, isPack: false));
            byPack[pack.Id] = rows;
            all.AddRange(rows);
        }
        int packItems = all.Count;

        uint main = model.Packs[0].Id;
        for (int p = 1; p < model.Packs.Count; p++)
            if (objects.Get(model.Packs[p].Id) is { } bag)
                all.Add(View(bag, displayName, main, 0, -1, worn: false, isPack: true));
        foreach (ClientObject worn in model.Worn)
            all.Add(View(worn, displayName, 0u, -1, -1, worn: true, isPack: false));

        return new GearProjection(all, byPack, packItems);
    }

    private static GearItemView View(ClientObject o, Func<ClientObject, string> displayName,
        uint packId, int packIndex, int slot, bool worn, bool isPack) =>
        new(o.ObjectId, displayName(o), GearTypeGroups.Classify(o.Type, o.Name, o.SpellId),
            o.MaterialType ?? 0u, o.Workmanship, o.Value, o.Burden, o.StackSize, o.StackSizeMax,
            packId, packIndex, slot, worn, isPack);
}
