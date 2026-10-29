namespace AcDream.Core.Items.Gear;

public sealed record GearSlot(EquipMask Mask, string Label, bool IsAetheria = false);

/// <summary>The Gear Deck's equipment slots in Slots-view order (six to a row), the same
/// masks as the retail paper doll (PaperdollSlotBackgrounds).</summary>
public static class GearSlotCatalog
{
    public const EquipMask Weapon = EquipMask.MeleeWeapon | EquipMask.MissileWeapon | EquipMask.Held | EquipMask.TwoHanded;

    public static IReadOnlyList<GearSlot> Slots { get; } =
    [
        new(EquipMask.HeadWear, "Head"), new(EquipMask.NeckWear, "Neck"), new(EquipMask.Cloak, "Cloak"),
        new(EquipMask.SigilOne, "Blue", true), new(EquipMask.SigilTwo, "Yellow", true), new(EquipMask.SigilThree, "Red", true),
        new(EquipMask.ChestArmor, "Chest"), new(EquipMask.AbdomenArmor, "Abdomen"), new(EquipMask.UpperArmArmor, "Upper arm"),
        new(EquipMask.LowerArmArmor, "Lower arm"), new(EquipMask.ChestWear, "Shirt"), new(EquipMask.UpperLegWear, "Pants"),
        new(EquipMask.UpperLegArmor, "Upper leg"), new(EquipMask.LowerLegArmor, "Lower leg"), new(EquipMask.HandWear, "Hands"),
        new(EquipMask.FootWear, "Feet"), new(EquipMask.WristWearLeft, "L wrist"), new(EquipMask.WristWearRight, "R wrist"),
        new(EquipMask.FingerWearLeft, "L ring"), new(EquipMask.FingerWearRight, "R ring"), new(Weapon, "Weapon"),
        new(EquipMask.Shield, "Shield"), new(EquipMask.MissileAmmo, "Ammo"), new(EquipMask.TrinketOne, "Trinket"),
    ];

    /// <summary>The item shown in <paramref name="slot"/> (the retail upper-inventory pick), or 0.</summary>
    public static uint Occupant(ClientObjectTable objects, uint playerId, GearSlot slot)
    {
        uint id = PaperdollSelectionPolicy.GetUpperInventoryObject(objects, playerId, slot.Mask);
        return id == playerId ? 0u : id;
    }
}
