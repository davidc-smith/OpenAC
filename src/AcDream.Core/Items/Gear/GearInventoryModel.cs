namespace AcDream.Core.Items.Gear;

/// <summary>One of the player's packs: the main pack (the player) or a side pack.</summary>
public readonly record struct GearPack(uint Id, int Capacity, bool IsMain);

/// <summary>
/// What the Gear Deck shows, projected from the object table: the packs, each
/// pack's loose items in slot order, the worn items, and burden. The same rules
/// as the retail inventory (InventoryController): a bag is a container-list member,
/// equipped items never count as pack contents, and burden uses the wire's
/// EncumbranceVal when the server sent it.
/// </summary>
public sealed class GearInventoryModel
{
    public const int MainPackDefaultCapacity = 102;
    public const int SidePackDefaultCapacity = 24;
    public const int SidePackDefaultSlots = 7;

    private const uint EncumbranceValProperty = 5u;
    private const uint EncumbranceAugProperty = 0xE6u;

    public static GearInventoryModel Empty { get; } =
        new([], new Dictionary<uint, IReadOnlyList<ClientObject>>(), [], 0, 0, 0, 0f, 0);

    private readonly IReadOnlyDictionary<uint, IReadOnlyList<ClientObject>> _contents;

    private GearInventoryModel(IReadOnlyList<GearPack> packs, IReadOnlyDictionary<uint, IReadOnlyList<ClientObject>> contents,
        IReadOnlyList<ClientObject> worn, int sidePackSlots, int burden, int capacity, float fill, int percent)
    {
        Packs = packs;
        _contents = contents;
        Worn = worn;
        SidePackSlots = sidePackSlots;
        BurdenUnits = burden;
        CapacityUnits = capacity;
        BurdenFill = fill;
        BurdenPercent = percent;
    }

    public IReadOnlyList<GearPack> Packs { get; }
    public IReadOnlyList<ClientObject> Worn { get; }
    public int SidePackSlots { get; }
    public int BurdenUnits { get; }
    public int CapacityUnits { get; }
    public float BurdenFill { get; }
    public int BurdenPercent { get; }

    public IReadOnlyList<ClientObject> Contents(uint packId) =>
        _contents.TryGetValue(packId, out IReadOnlyList<ClientObject>? items) ? items : [];

    public static GearInventoryModel Build(ClientObjectTable objects, uint playerId, int? strength)
    {
        ArgumentNullException.ThrowIfNull(objects);
        if (playerId == 0 || objects.Get(playerId) is not { } player) return Empty;

        var packs = new List<GearPack> { new(playerId, player.ItemsCapacity > 0 ? player.ItemsCapacity : MainPackDefaultCapacity, true) };
        foreach (uint id in objects.GetContents(playerId))
        {
            if (objects.Get(id) is not { } o || o.CurrentlyEquippedLocation != EquipMask.None) continue;
            if (ClientObjectTable.IsContainerListMember(o))
                packs.Add(new(o.ObjectId, o.ItemsCapacity > 0 ? o.ItemsCapacity : SidePackDefaultCapacity, false));
        }

        var contents = new Dictionary<uint, IReadOnlyList<ClientObject>>();
        foreach (GearPack p in packs)
        {
            var items = new List<ClientObject>();
            foreach (uint id in objects.GetContainedItems(p.Id))
                if (objects.Get(id) is { } o && o.CurrentlyEquippedLocation == EquipMask.None) items.Add(o);
            contents[p.Id] = items;
        }

        int capacity = BurdenMath.EncumbranceCapacity(strength ?? 10, player.Properties.GetInt(EncumbranceAugProperty));
        int burden = player.Properties.Ints.TryGetValue(EncumbranceValProperty, out int wire)
            ? wire
            : objects.SumCarriedBurden(playerId);
        float load = BurdenMath.LoadRatio(capacity, burden);
        return new GearInventoryModel(packs, contents, objects.GetEquippedBy(playerId),
            player.ContainersCapacity > 0 ? player.ContainersCapacity : SidePackDefaultSlots,
            burden, capacity, BurdenMath.LoadToFill(load), BurdenMath.LoadToPercent(load));
    }
}
