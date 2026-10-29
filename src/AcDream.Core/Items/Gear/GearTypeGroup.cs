namespace AcDream.Core.Items.Gear;

/// <summary>The friendly groups the Type chip offers.</summary>
public enum GearTypeGroup { Salvage, ManaStones, HealingKits, Food, Gems, Scrolls, Keys, Armour, Weapons, Jewellery, Clothing, Containers, Other }

public static class GearTypeGroups
{
    public static IReadOnlyList<GearTypeGroup> All { get; } = Enum.GetValues<GearTypeGroup>();

    public static string Label(GearTypeGroup group) => group switch
    {
        GearTypeGroup.Salvage => "Salvage",
        GearTypeGroup.ManaStones => "Mana stones",
        GearTypeGroup.HealingKits => "Healing kits",
        GearTypeGroup.Food => "Food",
        GearTypeGroup.Gems => "Gems",
        GearTypeGroup.Scrolls => "Scrolls",
        GearTypeGroup.Keys => "Keys",
        GearTypeGroup.Armour => "Armour",
        GearTypeGroup.Weapons => "Weapons",
        GearTypeGroup.Jewellery => "Jewellery",
        GearTypeGroup.Clothing => "Clothing",
        GearTypeGroup.Containers => "Containers",
        _ => "Other",
    };

    /// <summary>The group for an item's type bits, with a name rule where the bits are not enough
    /// (healing kits are Misc; scrolls share Writable with books).</summary>
    public static GearTypeGroup Classify(ItemType type, string name, uint? spellId)
    {
        name ??= "";
        if ((type & ItemType.TinkeringMaterial) != 0) return GearTypeGroup.Salvage;
        if ((type & ItemType.ManaStone) != 0) return GearTypeGroup.ManaStones;
        if (name.Contains("Healing Kit", StringComparison.OrdinalIgnoreCase)) return GearTypeGroup.HealingKits;
        if ((type & ItemType.Food) != 0) return GearTypeGroup.Food;
        if ((type & ItemType.Gem) != 0) return GearTypeGroup.Gems;
        if ((type & ItemType.Writable) != 0
            && (spellId is not null || name.StartsWith("Scroll", StringComparison.OrdinalIgnoreCase)))
            return GearTypeGroup.Scrolls;
        if ((type & ItemType.Key) != 0) return GearTypeGroup.Keys;
        if ((type & ItemType.Armor) != 0) return GearTypeGroup.Armour;
        if ((type & (ItemType.MeleeWeapon | ItemType.MissileWeapon | ItemType.Caster)) != 0) return GearTypeGroup.Weapons;
        if ((type & ItemType.Jewelry) != 0) return GearTypeGroup.Jewellery;
        if ((type & ItemType.Clothing) != 0) return GearTypeGroup.Clothing;
        if ((type & ItemType.Container) != 0) return GearTypeGroup.Containers;
        return GearTypeGroup.Other;
    }
}
