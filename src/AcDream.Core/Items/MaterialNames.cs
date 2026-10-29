namespace AcDream.Core.Items;

/// <summary>The retail material names by MaterialType id (1-77), as the salvage result message prints them.</summary>
public static class MaterialNames
{
    private static readonly string[] Names =
    [
        "Unknown",
        "Ceramic", "Porcelain", "Cloth", "Linen", "Satin", "Silk", "Velvet", "Wool",
        "Gem", "Agate", "Amber", "Amethyst", "Aquamarine", "Azurite", "Black Garnet",
        "Black Opal", "Bloodstone", "Carnelian", "Citrine", "Diamond", "Emerald", "Fire Opal",
        "Green Garnet", "Green Jade", "Hematite", "Imperial Topaz", "Jet", "Lapis Lazuli",
        "Lavender Jade", "Malachite", "Moonstone", "Onyx", "Opal", "Peridot", "Red Garnet",
        "Red Jade", "Rose Quartz", "Ruby", "Sapphire", "Smokey Quartz", "Sunstone", "Tiger Eye",
        "Tourmaline", "Turquoise", "White Jade", "White Quartz", "White Sapphire", "Yellow Garnet",
        "Yellow Topaz", "Zircon", "Ivory", "Leather", "Armoredillo Hide", "Gromnie Hide", "Reed Shark Hide",
        "Metal", "Brass", "Bronze", "Copper", "Gold", "Iron", "Pyreal", "Silver",
        "Steel", "Stone", "Alabaster", "Granite", "Marble", "Obsidian", "Sandstone", "Serpentine",
        "Wood", "Ebony", "Mahogany", "Oak", "Pine", "Teak",
    ];

    public static string Name(uint id) => id >= 1 && id < Names.Length ? Names[id] : Names[0];

    public static IEnumerable<(uint Id, string Name)> All
    {
        get
        {
            for (uint i = 1; i < Names.Length; i++) yield return (i, Names[i]);
        }
    }
}
