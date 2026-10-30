using System.Text.Json.Nodes;

namespace AcDream.Core.Items.Gear;

/// <summary>Saved filters as JSON. An entry this build cannot read is reported unreadable, never half-read.</summary>
public static class GearQueryJson
{
    private static readonly (GearField Field, string Key)[] FieldKeys =
    [
        (GearField.Name, "name"), (GearField.Type, "type"), (GearField.Material, "material"),
        (GearField.Workmanship, "work"), (GearField.Value, "value"), (GearField.Burden, "burden"),
        (GearField.Stack, "stack"), (GearField.Pack, "pack"), (GearField.Worn, "worn"), (GearField.Locked, "locked"),
    ];

    private static readonly (GearSortField Field, string Key)[] SortKeys =
    [
        (GearSortField.Name, "name"), (GearSortField.Type, "type"), (GearSortField.Material, "material"),
        (GearSortField.Workmanship, "work"), (GearSortField.Value, "value"), (GearSortField.Burden, "burden"),
        (GearSortField.Stack, "stack"), (GearSortField.Pack, "pack"),
    ];

    public static JsonObject Write(SavedFilter filter)
    {
        var chips = new JsonArray();
        foreach (GearChip c in filter.Query.Chips) chips.Add(WriteChip(c));
        return new JsonObject
        {
            ["name"] = filter.Name,
            ["chips"] = chips,
            ["text"] = filter.Query.Text,
            ["sort"] = KeyOf(SortKeys, filter.Query.Sort.Field),
            ["desc"] = filter.Query.Sort.Descending,
        };
    }

    private static JsonObject WriteChip(GearChip c)
    {
        var o = new JsonObject { ["f"] = KeyOf(FieldKeys, c.Field) };
        switch (c.Field)
        {
            case GearField.Name: o["text"] = c.Text; break;
            case GearField.Type: o["group"] = c.Group.ToString(); break;
            case GearField.Material or GearField.Pack: o["id"] = c.Id; break;
            case GearField.Worn or GearField.Locked: break;
            default:
                if (c.Min is double min) o["min"] = min;
                if (c.Max is double max) o["max"] = max;
                break;
        }
        return o;
    }

    /// <summary>A field's key; a field with none (a new one this table missed) is named in the exception.</summary>
    private static string KeyOf<T>((T Field, string Key)[] keys, T field) where T : struct, Enum
    {
        foreach ((T f, string key) in keys)
            if (EqualityComparer<T>.Default.Equals(f, field)) return key;
        throw new ArgumentOutOfRangeException(nameof(field), field, $"No saved-filter key for {typeof(T).Name} {field}.");
    }

    public static bool TryRead(JsonNode? node, out SavedFilter? filter)
    {
        filter = null;
        try
        {
            if (node is not JsonObject o || o["chips"] is not JsonArray array) return false;
            string name = ((string?)o["name"] ?? "").Trim();
            if (name.Length == 0) return false;
            var chips = new List<GearChip>();
            foreach (JsonNode? c in array)
            {
                if (ReadChip(c) is not { } chip) return false;
                chips.Add(chip);
            }
            string text = (string?)o["text"] ?? "";
            GearSort sort = GearSort.Default;
            if (o["sort"] is { } sortNode)
            {
                string key = (string?)sortNode ?? "";
                int i = Array.FindIndex(SortKeys, k => k.Key == key);
                if (i < 0) return false;
                sort = new GearSort(SortKeys[i].Field, o["desc"] is { } desc ? desc.GetValue<bool>() : false);
            }
            filter = new SavedFilter(name, new GearQuery(chips, text, sort));
            return true;
        }
        catch (Exception)
        {
            filter = null;
            return false;
        }
    }

    private static GearChip? ReadChip(JsonNode? node)
    {
        if (node is not JsonObject o) return null;
        string key = (string?)o["f"] ?? "";
        int i = Array.FindIndex(FieldKeys, k => k.Key == key);
        if (i < 0) return null;
        GearField field = FieldKeys[i].Field;
        switch (field)
        {
            case GearField.Name: return GearChip.NameContains((string?)o["text"] ?? "");
            case GearField.Type:
                // A group name only: Enum.TryParse also takes numbers (" 3" trimmed) and comma lists ("Salvage, Food").
                string g = (string?)o["group"] ?? "";
                return g.Length > 0 && g.All(char.IsAsciiLetter) && Enum.TryParse(g, ignoreCase: true, out GearTypeGroup group)
                    && Enum.IsDefined(group) ? GearChip.OfType(group) : null;
            case GearField.Material: return o["id"] is { } m ? GearChip.OfMaterial(m.GetValue<uint>()) : null;
            case GearField.Pack: return o["id"] is { } p ? GearChip.InPack(p.GetValue<uint>()) : null;
            case GearField.Worn: return GearChip.WornOnly;
            case GearField.Locked: return GearChip.LockedOnly;
            default: return GearChip.Range(field, o["min"]?.GetValue<double>(), o["max"]?.GetValue<double>());
        }
    }
}
