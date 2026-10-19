using RynthCore.Engine.Compatibility;

namespace AcDream.Rynth.Host.Tests;

/// <summary>
/// A fake game holding a player with an item in the main pack, a side pack with one item
/// in it and one worn item (player ints 5 burden 1200, 20 pyreals 5000), and the host's
/// chat and inventory hookups pointed at it. Their InventoryModel's statics are reset on
/// the way in and out.
/// </summary>
internal sealed class InventoryFixture : IDisposable
{
    public const uint Player = 0x50000001u;
    public const uint Pack = 0x80000010u;
    public const uint MainItem = 0x80000011u;
    public const uint InPack = 0x80000012u;
    public const uint Worn = 0x80000013u;

    /// <summary>ObjectDescriptionFlag.RequiresPackSlot, as their InventoryItem.RequiresPackSlotFlag.</summary>
    public const uint PackSlotFlag = 0x00800000u;

    public InventoryFixture()
    {
        InventoryModelReset.Run();
        View = new FakeGameView { PlayerId = Player };
        Put(Player, "Tester", Item(0, ints: new Dictionary<uint, int> { [5] = 1200, [20] = 5000 }));
        Put(Pack, "Backpack", Item(Player, slot: 0, bitfield: PackSlotFlag, capacity: 24));
        Put(MainItem, "Healing Kit", Item(Player, slot: 0));
        Put(InPack, "Arrow", Item(Pack, slot: 0, stack: 7));
        Put(Worn, "Bow", Item(0, wielder: Player));
        View.Contents[Player] = [Pack, MainItem, Worn];
        View.Contents[Pack] = [InPack];
        Host = new RynthHost(View, Log.Add, []);
        Chat = new RynthChatHookup(Host, View, Log.Add);
        Hookup = new RynthInventoryHookup(View, null, Log.Add);
    }

    public FakeGameView View { get; }

    public List<string> Log { get; } = new();

    public RynthHost Host { get; }

    public RynthChatHookup Chat { get; }

    public RynthInventoryHookup Hookup { get; }

    /// <summary>Puts an object in the view's table, with its name.</summary>
    public void Put(uint id, string name, RynthObjectInfo info)
    {
        View.Objects[id] = info;
        View.Names[id] = name;
    }

    /// <summary>An object with every required member set.</summary>
    public static RynthObjectInfo Item(uint container, uint wielder = 0, int slot = -1, uint bitfield = 0, int capacity = 0,
        int stack = 1, IReadOnlyDictionary<uint, int>? ints = null) => new()
    {
        ContainerId = container, WielderId = wielder, EquipLocation = wielder != 0 ? 0x100u : 0u, ItemType = 0x80, Wcid = 300,
        Bitfield = bitfield, IconId = 0x06001111, IconUnderlayId = 0, IconOverlayId = 0, SpellId = null, HouseOwnerId = null,
        StackSize = stack, StackSizeMax = 100, Value = 5, Burden = 10, ItemsCapacity = capacity, Structure = 0,
        MaxStructure = 0, Workmanship = 0, MaterialType = null, Identified = false,
        AppraisedSpellIds = [], Ints = ints ?? new Dictionary<uint, int>(), DataIds = new Dictionary<uint, uint>(),
        InstanceIds = new Dictionary<uint, uint>(),
        Bools = new Dictionary<uint, bool>(), Floats = new Dictionary<uint, double>(), Strings = new Dictionary<uint, string>(),
        ContainerSlot = slot,
    };

    public void Dispose()
    {
        InventoryModel.Unsubscribe();
        Hookup.Dispose();
        Chat.Dispose();
        Host.Dispose();
        InventoryModelReset.Run();
    }
}
