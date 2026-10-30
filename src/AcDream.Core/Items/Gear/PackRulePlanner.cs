namespace AcDream.Core.Items.Gear;

/// <summary>The moves a rule run makes, and per target pack how many matches stayed put because it was full.</summary>
public sealed record GearRulePlan(IReadOnlyList<GearMove> Moves, IReadOnlyDictionary<uint, int> Full);

/// <summary>Applies pack rules in order: the first rule an item matches decides its pack. Worn items, packs and
/// locked items never move; an item already in its rule's pack is left there.</summary>
public static class PackRulePlanner
{
    public static GearRulePlan Plan(IReadOnlyList<GearItemView> rows, IReadOnlyList<GearPack> packs,
        IReadOnlyList<(GearPackRule Rule, GearQuery Query)> rules, IReadOnlyCollection<uint>? only = null)
    {
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(packs);
        ArgumentNullException.ThrowIfNull(rules);
        var free = new Dictionary<uint, int>();
        foreach (GearPack p in packs) free[p.Id] = p.Capacity - rows.Count(r => !r.Worn && !r.IsPack && r.PackId == p.Id);
        var claimed = new HashSet<uint>();
        var moves = new List<GearMove>();
        var full = new Dictionary<uint, int>();
        foreach ((GearPackRule rule, GearQuery query) in rules)
        {
            if (!rule.Enabled || query.IsEmpty || !free.ContainsKey(rule.PackId)) continue;
            foreach (GearItemView row in GearQueryEngine.Run(rows, query with { Sort = GearSort.Default }))
            {
                if (row.Worn || row.IsPack || row.Locked || !claimed.Add(row.Id)) continue;
                if (only is not null && !only.Contains(row.Id)) continue;
                if (row.PackId == rule.PackId) continue;
                if (free[rule.PackId] <= 0)
                {
                    full[rule.PackId] = full.GetValueOrDefault(rule.PackId) + 1;
                    continue;
                }
                moves.Add(GearMove.Put(row.Id, rule.PackId, -1));
                free[rule.PackId]--;
                if (free.ContainsKey(row.PackId)) free[row.PackId]++;
            }
        }
        return new GearRulePlan(moves, full);
    }
}
