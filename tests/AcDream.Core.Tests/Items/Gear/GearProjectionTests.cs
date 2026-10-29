using AcDream.Core.Items;
using AcDream.Core.Items.Gear;
using Xunit;

namespace AcDream.Core.Tests.Items.Gear;

public sealed class GearProjectionTests
{
    private const uint Player = 0x50000001, Bag = 0xB1, Helm = 0xC1;

    private static void Place(ClientObjectTable t, ClientObject o, uint container, int slot)
    {
        t.AddOrUpdate(o);
        Assert.True(t.MoveItem(o.ObjectId, container, slot));
    }

    private static ClientObjectTable World()
    {
        var t = new ClientObjectTable();
        t.AddOrUpdate(new ClientObject { ObjectId = Player, Type = ItemType.Creature, ItemsCapacity = 102, ContainersCapacity = 7 });
        Place(t, new ClientObject { ObjectId = Bag, Type = ItemType.Container, ItemsCapacity = 24, Name = "Pack" }, Player, 0);
        Place(t, new ClientObject { ObjectId = 0xA0, Name = "Salvaged Steel", Type = ItemType.TinkeringMaterial,
            MaterialType = 64, Workmanship = 7.5f, Value = 900, Burden = 50, StackSize = 1 }, Player, 0);
        Place(t, new ClientObject { ObjectId = 0xA1, Name = "Apple", Type = ItemType.Food, StackSize = 5, StackSizeMax = 100 }, Player, 1);
        Place(t, new ClientObject { ObjectId = 0xA2, Name = "Sapphire", Type = ItemType.Gem }, Bag, 0);
        t.AddOrUpdate(new ClientObject { ObjectId = Helm, WielderId = Player, Name = "Helm", Type = ItemType.Armor, ValidLocations = EquipMask.HeadWear });
        Assert.True(t.MoveItem(Helm, Player, -1, EquipMask.HeadWear));
        return t;
    }

    private static GearProjection Build(ClientObjectTable t) =>
        GearProjection.Build(GearInventoryModel.Build(t, Player, 100), t, o => "[" + o.Name + "]");

    [Fact]
    public void ForPack_MatchesModelContentsOrder()
    {
        ClientObjectTable t = World();
        GearInventoryModel model = GearInventoryModel.Build(t, Player, 100);
        GearProjection p = GearProjection.Build(model, t, o => o.Name);
        Assert.Equal(model.Contents(Player).Select(o => o.ObjectId), p.ForPack(Player).Select(v => v.Id));
        Assert.Equal([0, 1], p.ForPack(Player).Select(v => v.Slot));
        Assert.All(p.ForPack(Bag), v => Assert.Equal(1, v.PackIndex));
        Assert.Empty(p.ForPack(0xDEAD));
    }

    [Fact]
    public void All_IsPackItemsThenSidePacksThenWorn()
    {
        GearProjection p = Build(World());
        Assert.Equal([0xA0u, 0xA1u, 0xA2u, Bag, Helm], p.All.Select(v => v.Id));
        GearItemView bag = p.All[3];
        Assert.True(bag.IsPack);
        Assert.Equal((Player, 0, -1), (bag.PackId, bag.PackIndex, bag.Slot));
        GearItemView helm = p.All[4];
        Assert.True(helm.Worn);
        Assert.Equal((0u, -1, -1), (helm.PackId, helm.PackIndex, helm.Slot));
        Assert.Equal(3, p.PackItemCount);
    }

    [Fact]
    public void Rows_CarryPublicFields()
    {
        GearItemView steel = Build(World()).All[0];
        Assert.Equal("[Salvaged Steel]", steel.Name);
        Assert.Equal(GearTypeGroup.Salvage, steel.Group);
        Assert.Equal(64u, steel.Material);
        Assert.Equal(7.5f, steel.Workmanship);
        Assert.Equal((900, 50, 1), (steel.Value, steel.Burden, steel.Stack));
    }

    [Fact]
    public void EmptyModel_IsEmpty()
    {
        GearProjection p = GearProjection.Build(GearInventoryModel.Empty, new ClientObjectTable(), o => o.Name);
        Assert.Empty(p.All);
        Assert.Equal(0, p.PackItemCount);
    }
}
