namespace AcDream.Core.Items.Gear;

public enum GearField { Name, Type, Material, Workmanship, Value, Burden, Stack, Pack, Worn }

/// <summary>What the deck sorts by. <see cref="Pack"/> is slot order (results: pack order, then slot) and has no
/// direction. Added last so the earlier members keep their numbers.</summary>
public enum GearSortField { Name, Type, Material, Workmanship, Value, Burden, Stack, Pack }

/// <summary>One filter chip. Which members matter depends on <see cref="Field"/>: Text (Name),
/// Group (Type), Id (Material, Pack), Min/Max (Workmanship, Value, Burden, Stack); Worn has none.</summary>
public sealed record GearChip(GearField Field)
{
    public string Text { get; init; } = "";
    public GearTypeGroup Group { get; init; }
    public uint Id { get; init; }
    public double? Min { get; init; }
    public double? Max { get; init; }

    public bool IsRange => Field is GearField.Workmanship or GearField.Value or GearField.Burden or GearField.Stack;

    public static GearChip NameContains(string text) => new(GearField.Name) { Text = text };
    public static GearChip OfType(GearTypeGroup group) => new(GearField.Type) { Group = group };
    public static GearChip OfMaterial(uint material) => new(GearField.Material) { Id = material };
    public static GearChip InPack(uint packId) => new(GearField.Pack) { Id = packId };
    public static GearChip Range(GearField field, double? min, double? max) => new(field) { Min = min, Max = max };
    public static GearChip WornOnly { get; } = new(GearField.Worn);
}

public readonly record struct GearSort(GearSortField Field, bool Descending)
{
    public static GearSort Default => new(GearSortField.Pack, false);

    /// <summary>Numbers sort best (largest) first; names, types and materials A to Z.</summary>
    public static GearSort DefaultFor(GearSortField field) =>
        new(field, field is GearSortField.Workmanship or GearSortField.Value or GearSortField.Burden or GearSortField.Stack);
}

public sealed record GearQuery(IReadOnlyList<GearChip> Chips, string Text, GearSort Sort)
{
    public static GearQuery Empty { get; } = new([], "", GearSort.Default);

    /// <summary>No chips and no text: the deck shows the normal pack view. The sort alone is not a query.</summary>
    public bool IsEmpty => Chips.Count == 0 && string.IsNullOrWhiteSpace(Text);

    public GearQuery With(GearChip chip) => Chips.Contains(chip) ? this : this with { Chips = [.. Chips, chip] };

    public GearQuery Without(int index) =>
        index < 0 || index >= Chips.Count ? this : this with { Chips = [.. Chips.Take(index), .. Chips.Skip(index + 1)] };

    public bool Equals(GearQuery? other) =>
        other is not null && Text == other.Text && Sort == other.Sort && Chips.SequenceEqual(other.Chips);

    public override int GetHashCode()
    {
        var h = new HashCode();
        h.Add(Text);
        h.Add(Sort);
        foreach (GearChip c in Chips) h.Add(c);
        return h.ToHashCode();
    }
}

/// <summary>A query the player named with the ★ button.</summary>
public sealed record SavedFilter(string Name, GearQuery Query)
{
    public const int MaxNameLength = 24;
    public const int MaxCount = 20;
}
