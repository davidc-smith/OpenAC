using System.Globalization;

namespace AcDream.Core.Items.Gear;

/// <summary>The short texts the query bar draws. ASCII only (the deck fonts' safe range).</summary>
public static class GearChipText
{
    public static string Label(GearChip chip, Func<uint, string> packName) => chip.Field switch
    {
        GearField.Name => "\"" + chip.Text + "\"",
        GearField.Type => GearTypeGroups.Label(chip.Group),
        GearField.Material => MaterialNames.Name(chip.Id),
        GearField.Pack => packName(chip.Id),
        GearField.Worn => "Worn",
        _ => RangeLabel(chip),
    };

    public static string Suggestion(GearChip chip) => chip.Field switch
    {
        GearField.Name => "Name contains \"" + chip.Text + "\"",
        GearField.Type => "Type: " + GearTypeGroups.Label(chip.Group),
        GearField.Material => "Material: " + MaterialNames.Name(chip.Id),
        GearField.Worn => "Worn items",
        _ => Label(chip, _ => "Pack"),
    };

    public static string SortLabel(GearSortField field) => field == GearSortField.Workmanship ? "Work" : field.ToString();

    /// <summary>A row's value for a list column; empty where the row has none.</summary>
    public static string Value(GearSortField field, GearItemView row) => field switch
    {
        GearSortField.Workmanship => row.Workmanship > 0 ? row.Workmanship.ToString("0.00", CultureInfo.InvariantCulture) : "",
        GearSortField.Value => row.Value.ToString("N0", CultureInfo.InvariantCulture),
        GearSortField.Burden => row.Burden.ToString(CultureInfo.InvariantCulture),
        GearSortField.Stack => row.Stack.ToString(CultureInfo.InvariantCulture),
        GearSortField.Material => row.Material != 0 ? MaterialNames.Name(row.Material) : "",
        GearSortField.Type => GearTypeGroups.Label(row.Group),
        _ => "",
    };

    /// <summary>The sort field a range chip's column shows.</summary>
    public static GearSortField? SortFieldOf(GearField field) => field switch
    {
        GearField.Workmanship => GearSortField.Workmanship,
        GearField.Value => GearSortField.Value,
        GearField.Burden => GearSortField.Burden,
        GearField.Stack => GearSortField.Stack,
        _ => null,
    };

    private static string RangeLabel(GearChip chip)
    {
        string name = chip.Field == GearField.Workmanship ? "Work" : chip.Field.ToString();
        string N(double v) => v.ToString("0.##", CultureInfo.InvariantCulture);
        return (chip.Min, chip.Max) switch
        {
            (double a, double b) when a == b => $"{name} = {N(a)}",
            (double a, double b) => $"{name} {N(a)}-{N(b)}",
            (double a, null) => $"{name} >= {N(a)}",
            (null, double b) => $"{name} <= {N(b)}",
            _ => name,
        };
    }
}
