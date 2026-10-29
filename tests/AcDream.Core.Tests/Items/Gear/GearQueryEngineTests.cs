using AcDream.Core.Items.Gear;
using Xunit;

namespace AcDream.Core.Tests.Items.Gear;

public sealed class GearQueryEngineTests
{
    private const uint Main = 0x50000001, Bag = 0xB1;

    private static GearItemView Row(uint id, string name, GearTypeGroup group = GearTypeGroup.Other, uint material = 0,
        float work = 0, int value = 0, int burden = 0, int stack = 1, uint pack = Main, int packIndex = 0, int slot = 0,
        bool worn = false) =>
        new(id, name, group, material, work, value, burden, stack, 100, worn ? 0u : pack, worn ? -1 : packIndex, worn ? -1 : slot, worn, false);

    private static readonly IReadOnlyList<GearItemView> Items =
    [
        Row(1, "Salvaged Steel", GearTypeGroup.Salvage, material: 64, work: 7.5f, value: 900, slot: 0),
        Row(2, "Salvaged Steel", GearTypeGroup.Salvage, material: 64, work: 9.0f, value: 1200, pack: Bag, packIndex: 1, slot: 0),
        Row(3, "Salvaged Iron", GearTypeGroup.Salvage, material: 61, work: 5.0f, value: 300, slot: 1),
        Row(4, "Apple", GearTypeGroup.Food, value: 5, stack: 20, slot: 2),
        Row(5, "Steel Helm", GearTypeGroup.Armour, material: 64, work: 6.0f, worn: true),
    ];

    private static uint[] Ids(GearQuery q) => GearQueryEngine.Run(Items, q).Select(v => v.Id).ToArray();
    private static GearQuery Q(params GearChip[] chips) => GearQuery.Empty with { Chips = chips };

    [Fact]
    public void EmptyQuery_ReturnsItemsUnchanged()
    {
        Assert.Same(Items, GearQueryEngine.Run(Items, GearQuery.Empty));
        Assert.True((GearQuery.Empty with { Sort = new GearSort(GearSortField.Value, true) }).IsEmpty);
    }

    [Fact]
    public void SameField_IsOr_DifferentFields_AreAnd()
    {
        Assert.Equal([3u, 1, 2], Ids(Q(GearChip.OfMaterial(64), GearChip.OfMaterial(61))));
        Assert.Equal([1u, 2], Ids(Q(GearChip.OfMaterial(64), GearChip.OfType(GearTypeGroup.Salvage))));
    }

    [Fact]
    public void FreeText_IsAndedNameContains()
    {
        Assert.Equal([1u, 2], Ids(GearQuery.Empty with { Text = "STEEL" }));
        Assert.Empty(Ids(Q(GearChip.OfType(GearTypeGroup.Food)) with { Text = "steel" }));
    }

    [Fact]
    public void Ranges_AreInclusive_AndMissingWorkmanshipNeverMatches()
    {
        Assert.Equal([1u, 2], Ids(Q(GearChip.Range(GearField.Workmanship, 7.5, null))));
        Assert.Equal([3u], Ids(Q(GearChip.Range(GearField.Workmanship, null, 5.0))));
        Assert.Equal([4u], Ids(Q(GearChip.Range(GearField.Stack, 20, 20))));
        Assert.DoesNotContain(4u, Ids(Q(GearChip.Range(GearField.Workmanship, null, 10))));
    }

    [Fact]
    public void Worn_ExcludedUnlessChip()
    {
        Assert.DoesNotContain(5u, Ids(Q(GearChip.OfMaterial(64))));
        Assert.Equal([5u], Ids(Q(GearChip.OfMaterial(64), GearChip.WornOnly)));
    }

    [Fact]
    public void PackChip_MatchesPack()
    {
        Assert.Equal([2u], Ids(Q(GearChip.InPack(Bag), GearChip.OfType(GearTypeGroup.Salvage))));
        Assert.Equal([3u, 1, 2], Ids(Q(GearChip.InPack(Bag), GearChip.InPack(Main), GearChip.OfType(GearTypeGroup.Salvage))));
    }

    [Fact]
    public void SortByWorkmanshipBestFirst_MissingLastBothWays()
    {
        var all = Q(GearChip.Range(GearField.Value, 0, null));
        Assert.Equal([2u, 1, 3, 4], Ids(all with { Sort = new GearSort(GearSortField.Workmanship, true) }));
        Assert.Equal([3u, 1, 2, 4], Ids(all with { Sort = new GearSort(GearSortField.Workmanship, false) }));
    }

    [Fact]
    public void Ties_ByNameThenPackOrderThenSlot()
    {
        var steel = Q(GearChip.OfMaterial(64)) with { Sort = new GearSort(GearSortField.Material, false) };
        Assert.Equal([1u, 2], Ids(steel));   // same material and name: main pack (0) before the bag (1)
        var byName = Q(GearChip.Range(GearField.Value, 0, null)) with { Sort = new GearSort(GearSortField.Name, true) };
        Assert.Equal([1u, 2, 3, 4], Ids(byName));
    }

    [Fact]
    public void DefaultDirections()
    {
        Assert.True(GearSort.DefaultFor(GearSortField.Workmanship).Descending);
        Assert.True(GearSort.DefaultFor(GearSortField.Value).Descending);
        Assert.False(GearSort.DefaultFor(GearSortField.Name).Descending);
        Assert.False(GearSort.DefaultFor(GearSortField.Material).Descending);
    }

    [Fact]
    public void QueryEquality_AndChipEdits()
    {
        GearQuery a = Q(GearChip.OfMaterial(64)), b = Q(GearChip.OfMaterial(64));
        Assert.Equal(a, b);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
        Assert.Same(a, a.With(GearChip.OfMaterial(64)));
        GearQuery two = a.With(GearChip.WornOnly);
        Assert.Equal(2, two.Chips.Count);
        Assert.Equal(a, two.Without(1));
    }
}
