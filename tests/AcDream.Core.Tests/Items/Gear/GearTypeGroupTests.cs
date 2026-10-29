using AcDream.Core.Items;
using AcDream.Core.Items.Gear;
using Xunit;

namespace AcDream.Core.Tests.Items.Gear;

public sealed class GearTypeGroupTests
{
    [Theory]
    [InlineData(ItemType.TinkeringMaterial, "Salvaged Steel", GearTypeGroup.Salvage)]
    [InlineData(ItemType.ManaStone, "Major Mana Stone", GearTypeGroup.ManaStones)]
    [InlineData(ItemType.Misc, "Peerless Healing Kit", GearTypeGroup.HealingKits)]
    [InlineData(ItemType.Food, "Apple", GearTypeGroup.Food)]
    [InlineData(ItemType.Gem, "Sapphire", GearTypeGroup.Gems)]
    [InlineData(ItemType.Writable, "Scroll of Blade Bane", GearTypeGroup.Scrolls)]
    [InlineData(ItemType.Writable, "Journal", GearTypeGroup.Other)]
    [InlineData(ItemType.Key, "Copper Key", GearTypeGroup.Keys)]
    [InlineData(ItemType.Armor, "Chainmail Hauberk", GearTypeGroup.Armour)]
    [InlineData(ItemType.MissileWeapon, "Longbow", GearTypeGroup.Weapons)]
    [InlineData(ItemType.Caster, "Wand", GearTypeGroup.Weapons)]
    [InlineData(ItemType.Jewelry, "Ring", GearTypeGroup.Jewellery)]
    [InlineData(ItemType.Clothing, "Shirt", GearTypeGroup.Clothing)]
    [InlineData(ItemType.Container, "Pack", GearTypeGroup.Containers)]
    [InlineData(ItemType.Misc, "Pyreal Mote", GearTypeGroup.Other)]
    public void Classify(ItemType type, string name, GearTypeGroup expected) =>
        Assert.Equal(expected, GearTypeGroups.Classify(type, name, spellId: null));

    [Fact]
    public void Classify_WritableWithSpell_IsScroll() =>
        Assert.Equal(GearTypeGroup.Scrolls, GearTypeGroups.Classify(ItemType.Writable, "Inscription", spellId: 42u));

    [Fact]
    public void Labels_AreFriendly()
    {
        Assert.Equal("Mana stones", GearTypeGroups.Label(GearTypeGroup.ManaStones));
        Assert.Equal("Healing kits", GearTypeGroups.Label(GearTypeGroup.HealingKits));
        Assert.Equal(13, GearTypeGroups.All.Count);
    }
}
