using AcDream.Core.Items;

namespace AcDream.Core.Tests.Items;

public sealed class InventoryContainerPlacementPolicyTests
{
    private const uint Player = 0x50000001u;

    [Fact]
    public void FullItemCapacityRejectsNewItemButAllowsReorder()
    {
        var objects = new ClientObjectTable();
        objects.AddOrUpdate(new ClientObject
        {
            ObjectId = Player,
            Name = "Player",
            ItemsCapacity = 1,
            ContainersCapacity = 7,
        });
        objects.AddOrUpdate(new ClientObject { ObjectId = 2u });
        objects.MoveItem(2u, Player, 0);
        objects.AddOrUpdate(new ClientObject { ObjectId = 3u });

        Assert.Equal(
            InventoryContainerPlacementRejection.ItemCapacityFull,
            InventoryContainerPlacementPolicy.Evaluate(objects, 3u, Player, Player));
        Assert.Equal(
            InventoryContainerPlacementRejection.None,
            InventoryContainerPlacementPolicy.Evaluate(objects, 2u, Player, Player));
    }

    [Fact]
    public void ContainerCycleAndTradeAreRejected()
    {
        var objects = new ClientObjectTable();
        objects.AddOrUpdate(new ClientObject
        {
            ObjectId = 10u, Type = ItemType.Container, ItemsCapacity = 24,
        });
        objects.AddOrUpdate(new ClientObject
        {
            ObjectId = 11u, Type = ItemType.Container, ItemsCapacity = 24,
        });
        objects.MoveItem(11u, 10u, 0);

        Assert.Equal(
            InventoryContainerPlacementRejection.RecursiveContainment,
            InventoryContainerPlacementPolicy.Evaluate(objects, 10u, 11u, Player));

        objects.Get(10u)!.TradeState = 1;
        Assert.Equal(
            InventoryContainerPlacementRejection.SourceBeingTraded,
            InventoryContainerPlacementPolicy.Evaluate(objects, 10u, Player, Player));
    }

    private const uint FloorHook = 1u;
    private const uint WallHook = 2u;

    [Theory]
    [InlineData(WallHook, InventoryContainerPlacementRejection.None)]
    [InlineData(FloorHook, InventoryContainerPlacementRejection.WrongHookType)]
    public void ItemGoesOnlyOnTheHookTypesItFits(
        uint hookType,
        InventoryContainerPlacementRejection expected)
    {
        ClientObjectTable objects = HookedHouse(hookType);
        objects.AddOrUpdate(new ClientObject
        {
            ObjectId = 3u, Name = "Long Sword", Type = ItemType.MeleeWeapon, HookType = WallHook,
        });

        Assert.Equal(
            expected,
            InventoryContainerPlacementPolicy.Evaluate(objects, 3u, 2u, Player));
    }

    [Fact]
    public void ItemWithNoHookTypeFitsNoHook()
    {
        ClientObjectTable objects = HookedHouse(WallHook);
        objects.AddOrUpdate(new ClientObject
        {
            ObjectId = 3u, Name = "Coat", Type = ItemType.Armor,
        });

        Assert.Equal(
            InventoryContainerPlacementRejection.WrongHookType,
            InventoryContainerPlacementPolicy.Evaluate(objects, 3u, 2u, Player));
    }

    [Fact]
    public void HookRefusesItemTypesItDoesNotTake()
    {
        ClientObjectTable objects = HookedHouse(WallHook, hookItemTypes: (uint)ItemType.Armor);
        objects.AddOrUpdate(new ClientObject
        {
            ObjectId = 3u, Name = "Long Sword", Type = ItemType.MeleeWeapon, HookType = WallHook,
        });

        Assert.Equal(
            InventoryContainerPlacementRejection.WrongHookType,
            InventoryContainerPlacementPolicy.Evaluate(objects, 3u, 2u, Player));
    }

    [Theory]
    [InlineData(null)]
    [InlineData(0u)]
    public void HookOfAnUnownedHouseRefusesEvenAFittingItem(uint? houseOwnerId)
    {
        ClientObjectTable objects = HookedHouse(WallHook, houseOwnerId: houseOwnerId);
        objects.AddOrUpdate(new ClientObject
        {
            ObjectId = 3u, Name = "Long Sword", Type = ItemType.MeleeWeapon, HookType = WallHook,
        });

        Assert.Equal(
            InventoryContainerPlacementRejection.HookHouseNotOwned,
            InventoryContainerPlacementPolicy.Evaluate(objects, 3u, 2u, Player));
    }

    [Fact]
    public void WrongHookTypeMessage()
    {
        Assert.Equal(
            "That item cannot be placed on the hook.",
            InventoryContainerPlacementPolicy.ComposeClientLocal(
                InventoryContainerPlacementRejection.WrongHookType,
                new ClientObject { ObjectId = 3u, Name = "Coat" },
                new ClientObject { ObjectId = 2u, Name = "Wall Hook" },
                Player));
    }

    [Fact]
    public void UnownedHookMessageAddsTheOwnershipSentence()
    {
        Assert.Equal(
            "That item cannot be placed on the hook. You must own the house to manipulate the hook.",
            InventoryContainerPlacementPolicy.ComposeClientLocal(
                InventoryContainerPlacementRejection.HookHouseNotOwned,
                new ClientObject { ObjectId = 3u, Name = "Long Sword" },
                new ClientObject { ObjectId = 2u, Name = "Wall Hook" },
                Player));
    }

    private const uint HouseOwner = 0x50000002u;

    private static ClientObjectTable HookedHouse(
        uint hookType,
        uint hookItemTypes = 0xFFFFFFFFu,
        uint? houseOwnerId = HouseOwner)
    {
        var objects = new ClientObjectTable();
        objects.AddOrUpdate(new ClientObject
        {
            ObjectId = 2u,
            Name = "Hook",
            ItemsCapacity = 1,
            HookType = hookType,
            HookItemTypes = hookItemTypes,
            HouseOwnerId = houseOwnerId,
        });
        return objects;
    }

    [Fact]
    public void FullMessageMatchesRetailContainerTypeBranches()
    {
        var player = new ClientObject { ObjectId = Player, Name = "Backpack" };
        var bag = new ClientObject { ObjectId = 2u, Name = "Pack" };
        Assert.Equal(
            "Backpack is completely full!",
            InventoryContainerPlacementPolicy.ComposeClientLocal(
                InventoryContainerPlacementRejection.ItemCapacityFull,
                null,
                player,
                Player));
        Assert.Equal(
            "The Pack can fit no more containers!",
            InventoryContainerPlacementPolicy.ComposeClientLocal(
                InventoryContainerPlacementRejection.ContainerCapacityFull,
                null,
                bag,
                Player));
    }
}
