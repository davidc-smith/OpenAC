using AcDream.App.Spells;
using AcDream.Core.Items;

namespace AcDream.App.Tests.Spells;

public sealed class ItemSpellSourceTests
{
    private const uint Player = 0x50000001u;

    [Fact]
    public void Find_HeldCasterWithSpell_ReturnsItAndItsSpell()
    {
        var objects = new ClientObjectTable();
        AddEquipped(objects, 0x70000001u, ItemType.Caster, EquipMask.Held, spellId: 1234u);

        ItemSpell? found = ItemSpellSource.Find(objects, Player);

        Assert.Equal(new ItemSpell(0x70000001u, 1234u), found);
    }

    [Theory]
    [InlineData(ItemType.Caster, (uint)EquipMask.Held, 0u)]          // no spell
    [InlineData(ItemType.MeleeWeapon, (uint)EquipMask.Held, 99u)]     // not a caster
    [InlineData(ItemType.Caster, (uint)EquipMask.HeadWear, 99u)]      // not held
    public void Find_IgnoresItemsThatDoNotQualify(ItemType type, uint location, uint spellId)
    {
        var objects = new ClientObjectTable();
        AddEquipped(objects, 0x70000002u, type, (EquipMask)location, spellId);

        Assert.Null(ItemSpellSource.Find(objects, Player));
    }

    // Same construction as SpellcastingUiControllerTests (an Orb wielded by the player).
    private static void AddEquipped(ClientObjectTable objects, uint id, ItemType type, EquipMask where, uint spellId)
    {
        objects.AddOrUpdate(new ClientObject { ObjectId = Player, Name = "Player" });
        objects.AddOrUpdate(new ClientObject
        {
            ObjectId = id,
            Name = "Orb",
            Type = type,
            WielderId = Player,
            CurrentlyEquippedLocation = where,
            SpellId = spellId == 0u ? null : spellId,
        });
    }
}
