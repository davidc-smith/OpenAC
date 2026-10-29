using AcDream.Core.Items;
using AcDream.Core.Items.Gear;
using Xunit;

namespace AcDream.Core.Tests.Items.Gear;

public sealed class GearInventoryModelTests
{
    private const uint Player = 0x50000001;

    private static ClientObjectTable Table()
    {
        var t = new ClientObjectTable();
        t.AddOrUpdate(new ClientObject { ObjectId = Player, Type = ItemType.Creature, ContainersCapacity = 7 });
        return t;
    }

    private static void Place(ClientObjectTable t, ClientObject o, int slot)
    {
        t.AddOrUpdate(o);
        Assert.True(t.MoveItem(o.ObjectId, Player, slot));
    }

    [Fact]
    public void NoPlayer_IsEmpty()
    {
        Assert.Same(GearInventoryModel.Empty, GearInventoryModel.Build(new ClientObjectTable(), Player, 100));
    }

    [Fact]
    public void Packs_MainFirst_ThenSidePacksInSlotOrder()
    {
        var t = Table();
        Place(t, new ClientObject { ObjectId = 0xB2, Type = ItemType.Container, ItemsCapacity = 24 }, 1);
        Place(t, new ClientObject { ObjectId = 0xB1, Type = ItemType.Container, ItemsCapacity = 24 }, 0);

        GearInventoryModel m = GearInventoryModel.Build(t, Player, 100);

        Assert.Equal(new uint[] { Player, 0xB1, 0xB2 }, m.Packs.Select(p => p.Id).ToArray());
        Assert.Equal(102, m.Packs[0].Capacity);
        Assert.Equal(24, m.Packs[1].Capacity);
        Assert.Equal(7, m.SidePackSlots);
    }

    [Fact]
    public void Contents_SlotOrder_NoBagsNoWorn()
    {
        var t = Table();
        Place(t, new ClientObject { ObjectId = 0xA2, Name = "Two" }, 1);
        Place(t, new ClientObject { ObjectId = 0xA1, Name = "One" }, 0);
        Place(t, new ClientObject { ObjectId = 0xB1, Type = ItemType.Container, ItemsCapacity = 24 }, 2);
        t.AddOrUpdate(new ClientObject { ObjectId = 0xC1, WielderId = Player });
        Assert.True(t.MoveItem(0xC1, Player, -1, EquipMask.HeadWear));

        GearInventoryModel m = GearInventoryModel.Build(t, Player, 100);

        Assert.Equal(new uint[] { 0xA1, 0xA2 }, m.Contents(Player).Select(o => o.ObjectId).ToArray());
        Assert.Equal(new uint[] { 0xC1 }, m.Worn.Select(o => o.ObjectId).ToArray());
    }

    [Fact]
    public void Burden_UsesWireEncumbranceWhenPresent()
    {
        var t = Table();
        t.Get(Player)!.Properties.Ints[5] = 1500;

        GearInventoryModel m = GearInventoryModel.Build(t, Player, 100);

        Assert.Equal(1500, m.BurdenUnits);
        Assert.Equal(BurdenMath.EncumbranceCapacity(100, 0), m.CapacityUnits);
        float load = BurdenMath.LoadRatio(m.CapacityUnits, 1500);
        Assert.Equal(BurdenMath.LoadToPercent(load), m.BurdenPercent);
        Assert.Equal(BurdenMath.LoadToFill(load), m.BurdenFill);
    }

    [Fact]
    public void Burden_FallsBackToCarriedSum_AndStrength10()
    {
        var t = Table();
        Place(t, new ClientObject { ObjectId = 0xA1, Burden = 40 }, 0);

        GearInventoryModel m = GearInventoryModel.Build(t, Player, strength: null);

        Assert.Equal(t.SumCarriedBurden(Player), m.BurdenUnits);
        Assert.Equal(BurdenMath.EncumbranceCapacity(10, 0), m.CapacityUnits);
    }
}
