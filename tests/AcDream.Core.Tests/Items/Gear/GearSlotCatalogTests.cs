using AcDream.Core.Items;
using AcDream.Core.Items.Gear;
using Xunit;

namespace AcDream.Core.Tests.Items.Gear;

public sealed class GearSlotCatalogTests
{
    private const uint Player = 0x50000001;

    [Fact]
    public void TwentyFourSlots_FirstAndLast()
    {
        Assert.Equal(24, GearSlotCatalog.Slots.Count);
        Assert.Equal(EquipMask.HeadWear, GearSlotCatalog.Slots[0].Mask);
        Assert.Equal("Head", GearSlotCatalog.Slots[0].Label);
        Assert.Equal(EquipMask.TrinketOne, GearSlotCatalog.Slots[23].Mask);
    }

    [Fact]
    public void SigilsAreAetheria()
    {
        Assert.Equal(3, GearSlotCatalog.Slots.Count(s => s.IsAetheria));
        Assert.All(GearSlotCatalog.Slots.Where(s => s.IsAetheria), s => Assert.True((s.Mask & EquipMask.Sigil) != 0));
    }

    [Fact]
    public void Occupant_WornItem_ElseZero()
    {
        var t = new ClientObjectTable();
        t.AddOrUpdate(new ClientObject { ObjectId = Player });
        t.AddOrUpdate(new ClientObject { ObjectId = 0xC1, WielderId = Player });
        Assert.True(t.MoveItem(0xC1, Player, -1, EquipMask.HeadWear));

        Assert.Equal(0xC1u, GearSlotCatalog.Occupant(t, Player, GearSlotCatalog.Slots[0]));
        Assert.Equal(0u, GearSlotCatalog.Occupant(t, Player, GearSlotCatalog.Slots[1]));
    }
}
