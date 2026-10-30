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
    /// <summary>A query of <paramref name="chips"/> sorted by name (the default is Pack order).</summary>
    private static GearQuery Q(params GearChip[] chips) =>
        GearQuery.Empty with { Chips = chips, Sort = new GearSort(GearSortField.Name, false) };

    [Fact]
    public void LockedChip_KeepsOnlyLockedRows()
    {
        var a = Row(1, "Axe") with { Locked = true };
        var b = Row(2, "Bow");
        IReadOnlyList<GearItemView> hits = GearQueryEngine.Run([a, b], GearQuery.Empty.With(GearChip.LockedOnly));
        Assert.Equal(new[] { 1u }, hits.Select(h => h.Id));
    }

    [Fact]
    public void Workmanship_ComparesAsDisplayed_SoA73fRowMatches73()
    {
        GearItemView[] rows = [Row(1, "A", work: 7.3f)];
        Assert.Single(GearQueryEngine.Run(rows, Q(GearChip.Range(GearField.Workmanship, 7.3, 7.3))));
        Assert.Single(GearQueryEngine.Run(rows, Q(GearChip.Range(GearField.Workmanship, null, 7.3))));
        Assert.Single(GearQueryEngine.Run(rows, Q(GearChip.Range(GearField.Workmanship, 7.3, null))));
    }

    [Fact]
    public void Sort_TiesEndInIdOrder_ForWornRowsWithTheSameName()
    {
        GearItemView[] ab = [Row(9, "Ring", worn: true), Row(4, "Ring", worn: true)];
        GearItemView[] ba = [ab[1], ab[0]];
        var q = Q(GearChip.WornOnly);
        Assert.Equal([4u, 9], GearQueryEngine.Run(ab, q).Select(v => v.Id).ToArray());
        Assert.Equal([4u, 9], GearQueryEngine.Run(ba, q).Select(v => v.Id).ToArray());
    }

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
    public void SortByMaterial_RowsWithoutOneLastBothWays()
    {
        var all = Q(GearChip.Range(GearField.Value, 0, null));   // the four pack rows; the apple has no material
        Assert.Equal([3u, 1, 2, 4], Ids(all with { Sort = new GearSort(GearSortField.Material, false) }));   // Iron, Steel, Steel
        Assert.Equal([1u, 2, 3, 4], Ids(all with { Sort = new GearSort(GearSortField.Material, true) }));
    }

    [Fact]
    public void Ties_InOnePack_FallToTheSlot()
    {
        GearItemView[] rows = [Row(7, "Gem", slot: 5), Row(8, "Gem", slot: 2), Row(9, "Gem", slot: 3)];
        Assert.Equal([8u, 9, 7], GearQueryEngine.Run(rows, Q(GearChip.Range(GearField.Value, 0, null))).Select(v => v.Id));
    }

    [Fact]
    public void APackChipWithTheWornChip_MatchesNothing_ForWornRowsAreInNoPack()
    {
        Assert.Empty(Ids(Q(GearChip.InPack(Main), GearChip.WornOnly)));
        Assert.Equal([5u], Ids(Q(GearChip.WornOnly)));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(1)]
    [InlineData(99)]
    public void Without_AnIndexOutOfRange_ChangesNothing(int index)
    {
        GearQuery q = Q(GearChip.OfMaterial(64));
        Assert.Same(q, q.Without(index));
    }

    [Fact]
    public void PackOrder_IsTheDefaultSort_AndASortAloneIsNoQuery()
    {
        Assert.Equal(new GearSort(GearSortField.Pack, false), GearSort.Default);
        Assert.Equal(GearSort.Default, GearQuery.Empty.Sort);
        Assert.False(GearSort.DefaultFor(GearSortField.Pack).Descending);
        Assert.True((GearQuery.Empty with { Sort = new GearSort(GearSortField.Name, false) }).IsEmpty);
    }

    [Fact]
    public void SortFieldNumbers_AreUnchanged_WithPackAddedLast()
    {
        Assert.Equal(new[] { 0, 1, 2, 3, 4, 5, 6, 7 }, new[]
        {
            (int)GearSortField.Name, (int)GearSortField.Type, (int)GearSortField.Material, (int)GearSortField.Workmanship,
            (int)GearSortField.Value, (int)GearSortField.Burden, (int)GearSortField.Stack, (int)GearSortField.Pack,
        });
    }

    /// <summary>The rows as a projection lists them: main pack by slot, the bag by slot, the bag row itself, worn.</summary>
    private static readonly IReadOnlyList<GearItemView> Projected =
    [
        Row(3, "Zinc", value: 5, slot: 0),
        Row(1, "Apple", value: 50, slot: 1),
        Row(2, "Mace", value: 20, pack: Bag, packIndex: 1, slot: 0),
        new(9, "Pack", GearTypeGroup.Containers, 0, 0, 1, 0, 1, 1, Main, 0, -1, false, true),
        Row(5, "Helm", value: 30, worn: true),
    ];

    [Fact]
    public void PackOrder_KeepsTheResultsInProjectionOrder_EitherDirection()
    {
        GearQuery any = GearQuery.Empty.With(GearChip.Range(GearField.Value, 0, null));   // the default sort
        Assert.Equal([3u, 1, 2, 9], GearQueryEngine.Run(Projected, any).Select(v => v.Id));
        Assert.Equal([3u, 1, 2, 9], GearQueryEngine.Run(Projected, any with { Sort = new GearSort(GearSortField.Pack, true) }).Select(v => v.Id));
        Assert.Equal([5u], GearQueryEngine.Run(Projected, GearQuery.Empty.With(GearChip.WornOnly)).Select(v => v.Id));
    }

    [Fact]
    public void SortRows_UnderPackOrder_ReturnsTheRowsAsTheyAre()
    {
        Assert.Same(Projected, GearQueryEngine.Sort(Projected, GearSort.Default));
        Assert.Same(Projected, GearQueryEngine.Sort(Projected, new GearSort(GearSortField.Pack, true)));
    }

    [Fact]
    public void SortRows_UsesTheResultsComparator()
    {
        GearItemView[] pack = [Row(7, "Gem", value: 10, slot: 0), Row(4, "Axe", value: 30, slot: 1), Row(8, "Gem", value: 10, slot: 2),
            Row(6, "Gem", value: 10, slot: 2)];
        Assert.Equal([4u, 7, 6, 8], GearQueryEngine.Sort(pack, GearSort.DefaultFor(GearSortField.Value)).Select(v => v.Id));
        Assert.Equal([7u, 6, 8, 4], GearQueryEngine.Sort(pack, new GearSort(GearSortField.Value, false)).Select(v => v.Id));
        Assert.Equal([4u, 7, 6, 8], GearQueryEngine.Sort(pack, new GearSort(GearSortField.Name, false)).Select(v => v.Id));
        Assert.Equal([7u, 4, 8, 6], pack.Select(v => v.Id));   // the input is left as it was
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
