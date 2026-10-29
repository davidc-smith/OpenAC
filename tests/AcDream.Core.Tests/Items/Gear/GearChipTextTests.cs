using AcDream.Core.Items.Gear;
using Xunit;

namespace AcDream.Core.Tests.Items.Gear;

public sealed class GearChipTextTests
{
    private static string L(GearChip c) => GearChipText.Label(c, id => id == 7 ? "Pack 2" : "Main");

    [Fact]
    public void Labels()
    {
        Assert.Equal("\"kit\"", L(GearChip.NameContains("kit")));
        Assert.Equal("Salvage", L(GearChip.OfType(GearTypeGroup.Salvage)));
        Assert.Equal("Steel", L(GearChip.OfMaterial(64)));
        Assert.Equal("Pack 2", L(GearChip.InPack(7)));
        Assert.Equal("Worn", L(GearChip.WornOnly));
        Assert.Equal("Work >= 7", L(GearChip.Range(GearField.Workmanship, 7, null)));
        Assert.Equal("Value <= 500", L(GearChip.Range(GearField.Value, null, 500)));
        Assert.Equal("Burden 50-100", L(GearChip.Range(GearField.Burden, 50, 100)));
        Assert.Equal("Stack = 20", L(GearChip.Range(GearField.Stack, 20, 20)));
        Assert.Equal("Work >= 7.25", L(GearChip.Range(GearField.Workmanship, 7.25, null)));
    }

    [Fact]
    public void Suggestions()
    {
        Assert.Equal("Type: Salvage", GearChipText.Suggestion(GearChip.OfType(GearTypeGroup.Salvage)));
        Assert.Equal("Material: Steel", GearChipText.Suggestion(GearChip.OfMaterial(64)));
        Assert.Equal("Name contains \"kit\"", GearChipText.Suggestion(GearChip.NameContains("kit")));
        Assert.Equal("Worn items", GearChipText.Suggestion(GearChip.WornOnly));
        Assert.Equal("Work >= 7.5", GearChipText.Suggestion(GearChip.Range(GearField.Workmanship, 7.5, null)));
        Assert.Equal("Stack = 20", GearChipText.Suggestion(GearChip.Range(GearField.Stack, 20, 20)));
        Assert.Equal("Pack", GearChipText.Suggestion(GearChip.InPack(7)));
    }

    [Fact]
    public void ARangeWithNoBounds_IsItsFieldName() =>
        Assert.Equal("Value", L(GearChip.Range(GearField.Value, null, null)));

    [Fact]
    public void SortFieldOf_NamesTheRangeFieldsOnly()
    {
        Assert.Equal(GearSortField.Workmanship, GearChipText.SortFieldOf(GearField.Workmanship));
        Assert.Equal(GearSortField.Value, GearChipText.SortFieldOf(GearField.Value));
        Assert.Equal(GearSortField.Burden, GearChipText.SortFieldOf(GearField.Burden));
        Assert.Equal(GearSortField.Stack, GearChipText.SortFieldOf(GearField.Stack));
        Assert.Null(GearChipText.SortFieldOf(GearField.Material));
        Assert.Null(GearChipText.SortFieldOf(GearField.Name));
    }

    [Fact]
    public void SortLabelsAndValues()
    {
        Assert.Equal("Work", GearChipText.SortLabel(GearSortField.Workmanship));
        Assert.Equal("Value", GearChipText.SortLabel(GearSortField.Value));
        var row = new GearItemView(1, "x", GearTypeGroup.Salvage, 64, 7.5f, 1200, 50, 3, 100, 1, 0, 0, false, false);
        Assert.Equal("7.50", GearChipText.Value(GearSortField.Workmanship, row));
        Assert.Equal("1,200", GearChipText.Value(GearSortField.Value, row));
        Assert.Equal("Steel", GearChipText.Value(GearSortField.Material, row));
        Assert.Equal("Salvage", GearChipText.Value(GearSortField.Type, row));
        Assert.Equal("", GearChipText.Value(GearSortField.Name, row));
        Assert.Equal("", GearChipText.Value(GearSortField.Workmanship, row with { Workmanship = 0 }));
        Assert.Equal("50", GearChipText.Value(GearSortField.Burden, row));
        Assert.Equal("3", GearChipText.Value(GearSortField.Stack, row));
        Assert.Equal("", GearChipText.Value(GearSortField.Material, row with { Material = 0 }));
        Assert.Equal("Burden", GearChipText.SortLabel(GearSortField.Burden));
        Assert.Equal("Name", GearChipText.SortLabel(GearSortField.Name));
        Assert.Equal("Pack", GearChipText.SortLabel(GearSortField.Pack));
        Assert.Equal("Pack order", GearChipText.SortMenuLabel(GearSortField.Pack));
        Assert.Equal("Work", GearChipText.SortMenuLabel(GearSortField.Workmanship));
        Assert.Equal("", GearChipText.Value(GearSortField.Pack, row));
    }
}
