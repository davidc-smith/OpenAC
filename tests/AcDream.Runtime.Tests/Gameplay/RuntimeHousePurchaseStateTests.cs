using AcDream.Core.Items;
using AcDream.Core.Net.Messages;
using AcDream.Runtime.Gameplay;

namespace AcDream.Runtime.Tests.Gameplay;

public sealed class RuntimeHousePurchaseStateTests
{
    private const uint Player = 0x50000001u;
    private const uint Pack = 0x60000900u;
    private const uint Ground = 0x70000999u;
    private const uint Slumlord = 0x70000100u;
    private const uint OtherSlumlord = 0x70000200u;
    private const uint Pyreal = 273u;
    private const uint Totem = 9000u;

    private readonly ClientObjectTable _objects = new();
    private readonly RuntimeHousePurchaseState _state;

    public RuntimeHousePurchaseStateTests()
    {
        _objects.AddOrUpdate(new ClientObject { ObjectId = Player, Type = ItemType.Creature });
        _objects.AddOrUpdate(new ClientObject { ObjectId = Ground, Type = ItemType.Container });
        _objects.AddOrUpdate(new ClientObject { ObjectId = Pack, Type = ItemType.Container });
        _objects.MoveItem(Pack, Player, 0);
        _state = new RuntimeHousePurchaseState(_objects, () => Player);
    }

    private ClientObject Item(
        uint id, uint wcid, int stack = 1, int value = 0,
        ItemType type = ItemType.Misc, uint container = Player)
    {
        var item = new ClientObject
        {
            ObjectId = id, WeenieClassId = wcid, StackSize = stack, Value = value, Type = type,
        };
        _objects.AddOrUpdate(item);
        _objects.MoveItem(id, container, 0);
        return item;
    }

    private static GameEvents.HousePayment Pay(uint wcid, int num, int paid = 0) =>
        new(num, paid, wcid, "Thing", "Things");

    private static GameEvents.HouseProfile Profile(
        uint slumlord = Slumlord,
        uint owner = 0u,
        bool maintenanceFree = false,
        GameEvents.HousePayment[]? buy = null,
        GameEvents.HousePayment[]? rent = null) =>
        new(slumlord, 0x12340001u, owner, 1u, -1, -1, -1, -1,
            maintenanceFree, 1u, owner == 0u ? "" : "Owner",
            buy ?? [Pay(Pyreal, 50_000), Pay(Totem, 1)],
            rent ?? [Pay(Pyreal, 5_000)]);

    [Fact]
    public void ApplyProfile_OpensAndRaisesRevision()
    {
        long before = _state.View.Snapshot.Revision;

        _state.ApplyProfile(Profile());

        RuntimeHousePurchaseSnapshot snapshot = _state.View.Snapshot;
        Assert.True(snapshot.IsOpen);
        Assert.Equal(Slumlord, snapshot.Profile!.Value.SlumlordGuid);
        Assert.True(snapshot.Revision > before);
    }

    [Fact]
    public void Stage_WithNoProfile_IsNotOpen()
    {
        Item(1, Pyreal, stack: 100);

        Assert.Equal(HouseStageResult.NotOpen, _state.Stage(HousePurchaseTab.Buy, 1));
    }

    [Fact]
    public void Stage_MatchingItem_IsStagedAndCountsItsStack()
    {
        Item(1, Pyreal, stack: 20_000);
        _state.ApplyProfile(Profile());

        Assert.Equal(HouseStageResult.Staged, _state.Stage(HousePurchaseTab.Buy, 1));
        Assert.Equal([1u], _state.View.GetStaged(HousePurchaseTab.Buy));
        Assert.Equal(20_000, _state.View.GetStagedAmount(HousePurchaseTab.Buy, 0));
        Assert.Empty(_state.View.GetStaged(HousePurchaseTab.Rent));
    }

    [Fact]
    public void Stage_ItemInASidePack_IsStaged()
    {
        Item(1, Totem, container: Pack);
        _state.ApplyProfile(Profile());

        Assert.Equal(HouseStageResult.Staged, _state.Stage(HousePurchaseTab.Buy, 1));
    }

    [Fact]
    public void Stage_TradeNote_CountsItsValueTowardsPyreals()
    {
        Item(1, 2621u, value: 10_000, type: ItemType.PromissoryNote);
        _state.ApplyProfile(Profile());

        Assert.Equal(HouseStageResult.Staged, _state.Stage(HousePurchaseTab.Buy, 1));
        Assert.Equal(10_000, _state.View.GetStagedAmount(HousePurchaseTab.Buy, 0));
    }

    [Fact]
    public void Stage_RefusesTheWrongClass()
    {
        Item(1, 1234u);
        _state.ApplyProfile(Profile());

        Assert.Equal(HouseStageResult.NotRequired, _state.Stage(HousePurchaseTab.Buy, 1));
    }

    [Fact]
    public void Stage_RefusesTheSameItemTwice()
    {
        Item(1, Pyreal, stack: 10);
        _state.ApplyProfile(Profile());
        _state.Stage(HousePurchaseTab.Buy, 1);

        Assert.Equal(HouseStageResult.AlreadyStaged, _state.Stage(HousePurchaseTab.Buy, 1));
    }

    [Fact]
    public void Stage_RefusesALineAlreadyCovered()
    {
        Item(1, Pyreal, stack: 30_000);
        Item(2, Pyreal, stack: 5);
        _state.ApplyProfile(Profile(buy: [Pay(Pyreal, 50_000, paid: 20_000)]));

        Assert.Equal(HouseStageResult.Staged, _state.Stage(HousePurchaseTab.Buy, 1));
        Assert.Equal(HouseStageResult.AlreadyCovered, _state.Stage(HousePurchaseTab.Buy, 2));
    }

    [Fact]
    public void Stage_RefusesBuyingAnOwnedHouse()
    {
        Item(1, Pyreal, stack: 10);
        _state.ApplyProfile(Profile(owner: 0x50000042u));

        Assert.Equal(HouseStageResult.AlreadyOwned, _state.Stage(HousePurchaseTab.Buy, 1));
    }

    [Fact]
    public void Stage_RefusesRentWhenMaintenanceIsFree()
    {
        Item(1, Pyreal, stack: 10);
        _state.ApplyProfile(Profile(owner: Player, maintenanceFree: true));

        Assert.Equal(HouseStageResult.MaintenanceFree, _state.Stage(HousePurchaseTab.Rent, 1));
    }

    [Fact]
    public void Stage_RefusesItemsNotInTheInventory()
    {
        Item(1, Pyreal, stack: 10, container: Ground);
        ClientObject wielded = Item(2, Totem);
        wielded.WielderId = Player;
        _state.ApplyProfile(Profile());

        Assert.Equal(HouseStageResult.NotInInventory, _state.Stage(HousePurchaseTab.Buy, 1));
        Assert.Equal(HouseStageResult.NotInInventory, _state.Stage(HousePurchaseTab.Buy, 2));
        Assert.Equal(HouseStageResult.NotInInventory, _state.Stage(HousePurchaseTab.Buy, 99));
    }

    [Fact]
    public void ApplyProfile_FromAnotherSlumlord_ClearsStagedItems()
    {
        Item(1, Pyreal, stack: 10);
        _state.ApplyProfile(Profile());
        _state.Stage(HousePurchaseTab.Buy, 1);

        _state.ApplyProfile(Profile(slumlord: OtherSlumlord));

        Assert.Empty(_state.View.GetStaged(HousePurchaseTab.Buy));
    }

    [Fact]
    public void ApplyProfile_FromTheSameSlumlord_KeepsItemsStillHeld()
    {
        Item(1, Pyreal, stack: 10);
        Item(2, Pyreal, stack: 20);
        _state.ApplyProfile(Profile(owner: Player));
        _state.Stage(HousePurchaseTab.Rent, 1);
        _state.Stage(HousePurchaseTab.Rent, 2);
        _objects.Remove(2);

        _state.ApplyProfile(Profile(owner: Player, rent: [Pay(Pyreal, 5_000, paid: 20)]));

        Assert.Equal([1u], _state.View.GetStaged(HousePurchaseTab.Rent));
    }

    [Fact]
    public void Revalidate_DropsItemsThatLeftTheInventory()
    {
        Item(1, Pyreal, stack: 10);
        _state.ApplyProfile(Profile());
        _state.Stage(HousePurchaseTab.Buy, 1);
        long before = _state.View.Snapshot.Revision;

        Assert.False(_state.Revalidate());
        Assert.Equal(before, _state.View.Snapshot.Revision);

        _objects.MoveItem(1, Ground, 0);

        Assert.Empty(_state.View.GetStaged(HousePurchaseTab.Buy));
        Assert.True(_state.Revalidate());
        Assert.True(_state.View.Snapshot.Revision > before);
    }

    [Fact]
    public void Unstage_AndClose()
    {
        Item(1, Pyreal, stack: 10);
        Item(2, Totem);
        _state.ApplyProfile(Profile());
        _state.Stage(HousePurchaseTab.Buy, 1);
        _state.Stage(HousePurchaseTab.Buy, 2);

        _state.Unstage(HousePurchaseTab.Buy, 1);
        Assert.Equal([2u], _state.View.GetStaged(HousePurchaseTab.Buy));

        _state.Close();
        Assert.False(_state.View.Snapshot.IsOpen);
        Assert.Null(_state.View.Snapshot.Profile);
        Assert.Empty(_state.View.GetStaged(HousePurchaseTab.Buy));
    }
}
