using System.Globalization;
using System.Text.RegularExpressions;

namespace AcDream.Core.Items.Gear;

/// <summary>Turns a typed word into the chips the query bar offers under the field.</summary>
public static partial class GearChipSuggester
{
    public const int MaxSuggestions = 5;

    private static readonly (GearTypeGroup Group, string[] Aliases)[] GroupWords =
    [
        (GearTypeGroup.Salvage, ["salvage"]),
        (GearTypeGroup.ManaStones, ["mana", "stone"]),
        (GearTypeGroup.HealingKits, ["heal", "kit"]),
        (GearTypeGroup.Food, ["food"]),
        (GearTypeGroup.Gems, ["gem"]),
        (GearTypeGroup.Scrolls, ["scroll"]),
        (GearTypeGroup.Keys, ["key"]),
        (GearTypeGroup.Armour, ["armour", "armor"]),
        (GearTypeGroup.Weapons, ["weapon", "wand", "bow", "sword"]),
        (GearTypeGroup.Jewellery, ["jewel", "jewelry", "ring", "necklace"]),
        (GearTypeGroup.Clothing, ["clothing", "shirt", "pants"]),
        (GearTypeGroup.Containers, ["container", "pack", "bag"]),
    ];

    [GeneratedRegex(@"^(?<f>workmanship|work|ws|value|val|burden|bu|stack|st)\s*(?<op><=|>=|<|>|=)?\s*(?<n>\d+(?:\.\d+)?)$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex RangePattern();

    public static IReadOnlyList<GearChip> Suggest(string typed)
    {
        string t = (typed ?? "").Trim();
        if (t.Length == 0) return [];

        var picks = new List<GearChip>();
        if (TryRange(t, out GearChip? range)) picks.Add(range!);
        if (t.Length >= 3)
        {
            foreach ((GearTypeGroup group, string[] aliases) in GroupWords)
                if (StartsWith(GearTypeGroups.Label(group), t) || aliases.Any(a => StartsWith(a, t)))
                    picks.Add(GearChip.OfType(group));
            foreach ((uint id, string name) in MaterialNames.All)
                if (StartsWith(name, t)) picks.Add(GearChip.OfMaterial(id));
            foreach ((uint id, string name) in MaterialNames.All)
                if (!StartsWith(name, t) && name.Contains(t, StringComparison.OrdinalIgnoreCase)) picks.Add(GearChip.OfMaterial(id));
        }
        if (t.Length >= 2 && StartsWith("worn", t)) picks.Add(GearChip.WornOnly);
        if (t.Length >= 2 && StartsWith("locked", t)) picks.Add(GearChip.LockedOnly);

        var result = picks.Distinct().Take(MaxSuggestions - 1).ToList();
        result.Add(GearChip.NameContains(t));
        return result;
    }

    private static bool StartsWith(string text, string prefix) => text.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);

    private static bool TryRange(string t, out GearChip? chip)
    {
        chip = null;
        Match m = RangePattern().Match(t);
        if (!m.Success) return false;
        GearField field = m.Groups["f"].Value.ToLowerInvariant() switch
        {
            "workmanship" or "work" or "ws" => GearField.Workmanship,
            "value" or "val" => GearField.Value,
            "burden" or "bu" => GearField.Burden,
            _ => GearField.Stack,
        };
        double n = double.Parse(m.Groups["n"].Value, CultureInfo.InvariantCulture);
        chip = m.Groups["op"].Value switch
        {
            "<" or "<=" => GearChip.Range(field, null, n),
            "=" => GearChip.Range(field, n, n),
            _ => GearChip.Range(field, n, null),
        };
        return true;
    }
}
