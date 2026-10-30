using AcDream.Core.Items;
using Xunit;

namespace AcDream.Core.Tests.Items;

public sealed class ItemLocksTests
{
    private const uint Player = 0x50000001, Bag = 0xB1, Sword = 0xA1, Gem = 0xA2;

    private static ClientObjectTable Table()
    {
        var t = new ClientObjectTable();
        t.AddOrUpdate(new ClientObject { ObjectId = Player, Type = ItemType.Creature, ItemsCapacity = 102, ContainersCapacity = 7 });
        t.AddOrUpdate(new ClientObject { ObjectId = Bag, Type = ItemType.Container, ItemsCapacity = 24, Name = "Pack" });
        Assert.True(t.MoveItem(Bag, Player, 0));
        t.AddOrUpdate(new ClientObject { ObjectId = Sword, Type = ItemType.MeleeWeapon, Name = "Sword" });
        Assert.True(t.MoveItem(Sword, Player, 0));
        t.AddOrUpdate(new ClientObject { ObjectId = Gem, Type = ItemType.Gem, Name = "Gem" });
        Assert.True(t.MoveItem(Gem, Bag, 0));
        return t;
    }

    [Fact]
    public void SetAndReplace_ChangeOnlyWhenTheSetChanges()
    {
        var locks = new ItemLocks();
        int changed = 0;
        locks.Changed += () => changed++;
        Assert.True(locks.Set(Sword, true));
        Assert.False(locks.Set(Sword, true));
        Assert.False(locks.Set(0u, true));
        Assert.True(locks.IsLocked(Sword));
        Assert.False(locks.IsLocked(0u));
        locks.Replace([Sword]);
        Assert.Equal(1, changed);
        Assert.Equal(1, locks.Revision);
        locks.Replace([Gem, 0u]);
        Assert.Equal(2, changed);
        Assert.Equal(new[] { Gem }, locks.Ids);
        Assert.True(locks.Set(Gem, false));
        Assert.Empty(locks.Ids);
    }

    [Fact]
    public void BlockerOf_IsTheItemItself_OrALockedItemInsideAContainer()
    {
        ClientObjectTable t = Table();
        var locks = new ItemLocks();
        Assert.Equal(0u, locks.BlockerOf(t, Sword));
        locks.Set(Sword, true);
        Assert.Equal(Sword, locks.BlockerOf(t, Sword));
        Assert.Equal(0u, locks.BlockerOf(t, Bag));
        locks.Set(Gem, true);
        Assert.Equal(Gem, locks.BlockerOf(t, Bag));
    }

    [Fact]
    public void Message_NamesTheItem() => Assert.Equal("The Sword is locked.", ItemLocks.Message("Sword"));
}
