namespace AcDream.Core.Items.Gear;

/// <summary>Filters and sorts the Gear Deck's rows for a query.</summary>
public static class GearQueryEngine
{
    public static IReadOnlyList<GearItemView> Run(IReadOnlyList<GearItemView> items, GearQuery query)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(query);
        if (query.IsEmpty) return items;

        IGrouping<GearField, GearChip>[] fields = query.Chips.Where(c => c.Field != GearField.Worn)
            .GroupBy(c => c.Field).ToArray();
        bool worn = query.Chips.Any(c => c.Field == GearField.Worn);
        string text = query.Text.Trim();

        var hits = new List<GearItemView>();
        foreach (GearItemView item in items)
        {
            if (item.Worn != worn) continue;
            if (text.Length > 0 && !item.Name.Contains(text, StringComparison.OrdinalIgnoreCase)) continue;
            bool ok = true;
            foreach (IGrouping<GearField, GearChip> field in fields)
                if (!field.Any(chip => Matches(chip, item))) { ok = false; break; }
            if (ok) hits.Add(item);
        }
        GearSort sort = query.Sort;
        if (sort.Field != GearSortField.Pack) hits.Sort((a, b) => Compare(sort, a, b));   // Pack: as the items came
        return hits;
    }

    /// <summary>
    /// <paramref name="rows"/> in <paramref name="sort"/>'s order with the results' comparator, as a new list;
    /// under Pack order, <paramref name="rows"/> themselves (a pack's rows are already in slot order).
    /// </summary>
    public static IReadOnlyList<GearItemView> Sort(IReadOnlyList<GearItemView> rows, GearSort sort)
    {
        ArgumentNullException.ThrowIfNull(rows);
        if (sort.Field == GearSortField.Pack) return rows;
        var sorted = new List<GearItemView>(rows);
        sorted.Sort((a, b) => Compare(sort, a, b));
        return sorted;
    }

    private static bool Matches(GearChip chip, GearItemView item) => chip.Field switch
    {
        GearField.Name => item.Name.Contains(chip.Text, StringComparison.OrdinalIgnoreCase),
        GearField.Type => item.Group == chip.Group,
        GearField.Material => item.Material != 0 && item.Material == chip.Id,
        GearField.Pack => !item.Worn && item.PackId == chip.Id,
        GearField.Workmanship => item.Workmanship > 0 && InRange(chip, Math.Round((double)item.Workmanship, 2)),
        GearField.Value => InRange(chip, item.Value),
        GearField.Burden => InRange(chip, item.Burden),
        GearField.Stack => InRange(chip, item.Stack),
        GearField.Locked => item.Locked,
        _ => true,
    };

    private static bool InRange(GearChip chip, double value) =>
        (chip.Min is not double min || value >= min) && (chip.Max is not double max || value <= max);

    private static bool Missing(GearSortField field, GearItemView v) => field switch
    {
        GearSortField.Material => v.Material == 0,
        GearSortField.Workmanship => v.Workmanship <= 0,
        _ => false,
    };

    private static int Primary(GearSortField field, GearItemView a, GearItemView b) => field switch
    {
        GearSortField.Type => a.Group.CompareTo(b.Group),
        GearSortField.Material => string.Compare(MaterialNames.Name(a.Material), MaterialNames.Name(b.Material), StringComparison.OrdinalIgnoreCase),
        GearSortField.Workmanship => a.Workmanship.CompareTo(b.Workmanship),
        GearSortField.Value => a.Value.CompareTo(b.Value),
        GearSortField.Burden => a.Burden.CompareTo(b.Burden),
        GearSortField.Stack => a.Stack.CompareTo(b.Stack),
        _ => 0,
    };

    /// <summary>The sort field and direction; rows missing the field last either way; then name, pack order, slot.</summary>
    private static int Compare(GearSort sort, GearItemView a, GearItemView b)
    {
        int c;
        if (sort.Field == GearSortField.Name)
        {
            c = string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
            if (c != 0) return sort.Descending ? -c : c;
        }
        else
        {
            bool ma = Missing(sort.Field, a), mb = Missing(sort.Field, b);
            if (ma != mb) return ma ? 1 : -1;
            c = Primary(sort.Field, a, b);
            if (c != 0) return sort.Descending ? -c : c;
        }
        c = string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
        if (c != 0) return c;
        c = a.PackIndex.CompareTo(b.PackIndex);
        if (c != 0) return c;
        c = a.Slot.CompareTo(b.Slot);
        return c != 0 ? c : a.Id.CompareTo(b.Id);
    }
}
