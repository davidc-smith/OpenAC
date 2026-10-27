using AcDream.App.UI.Hud;

namespace AcDream.App.Tests.UI.Hud;

/// <summary>One tab for the whole deck, rows are consecutive slices of it.</summary>
public sealed class SpellDeckModelTests
{
    private static readonly uint[] Ten = [1, 2, 3, 4, 5, 6, 7, 8, 9, 10];
    private static int Max => SpellDeckGeometry.MaxCapacity;

    // --- Slice / Visible ---

    [Fact]
    public void Slice_Row0_IsTheFirstPerRowSpells() =>
        Assert.Equal([1u, 2u, 3u, 4u], SpellDeckModel.Slice(Ten, row: 0, perRow: 4));

    [Fact]
    public void Slice_Row1_ContinuesTheTab() =>
        Assert.Equal([5u, 6u, 7u, 8u], SpellDeckModel.Slice(Ten, row: 1, perRow: 4));

    [Fact]
    public void Slice_LastRow_IsShortWhenTheTabEnds() =>
        Assert.Equal([9u, 10u], SpellDeckModel.Slice(Ten, row: 2, perRow: 4));

    [Fact]
    public void Slice_PastTheEnd_IsEmpty() =>
        Assert.Empty(SpellDeckModel.Slice(Ten, row: 3, perRow: 4));

    [Fact]
    public void Slice_BadArguments_AreEmpty()
    {
        Assert.Empty(SpellDeckModel.Slice(Ten, row: -1, perRow: 4));
        Assert.Empty(SpellDeckModel.Slice(Ten, row: 0, perRow: 0));
    }

    [Fact]
    public void Visible_IsTheFirstRowsTimesPerRow() =>
        Assert.Equal([1u, 2u, 3u, 4u, 5u, 6u], SpellDeckModel.Visible(Ten, rows: 2, perRow: 3));

    [Fact]
    public void Visible_WhenEverythingFits_ReturnsAll() =>
        Assert.Equal(Ten, SpellDeckModel.Visible(Ten, rows: 3, perRow: 4));

    [Fact]
    public void Visible_SlicesTogetherAreTheVisibleSpells()
    {
        var joined = Enumerable.Range(0, 3).SelectMany(r => SpellDeckModel.Slice(Ten, r, 3));
        Assert.Equal(SpellDeckModel.Visible(Ten, 3, 3), joined.ToArray());
    }

    // --- MinSlotsPerRow ---

    [Fact]
    public void MinSlotsPerRow_IsCeilOfTheFullestTabOverRows() =>
        Assert.Equal(4, SpellDeckModel.MinSlotsPerRow(rows: 3, allTabCounts: [2, 10, 0, 5]));

    [Fact]
    public void MinSlotsPerRow_CountsLocalTabsToo() =>
        // Tabs 8+ (local) are just more counts: the fullest of all tabs wins.
        Assert.Equal(7, SpellDeckModel.MinSlotsPerRow(rows: 2, allTabCounts: [1, 2, 3, 4, 5, 6, 7, 8, 13]));

    [Fact]
    public void MinSlotsPerRow_IsAtLeastOne()
    {
        Assert.Equal(1, SpellDeckModel.MinSlotsPerRow(rows: 2, allTabCounts: [0, 0]));
        Assert.Equal(1, SpellDeckModel.MinSlotsPerRow(rows: 2, allTabCounts: []));
    }

    [Fact]
    public void MinSlotsPerRow_IsCappedAtMaxCapacity() =>
        Assert.Equal(Max, SpellDeckModel.MinSlotsPerRow(rows: 1, allTabCounts: [Max * 3]));

    [Fact]
    public void MinSlotsPerRow_ExactMultiple_DoesNotRoundUp() =>
        Assert.Equal(5, SpellDeckModel.MinSlotsPerRow(rows: 2, allTabCounts: [10]));

    // --- CanRemoveRow ---

    [Fact]
    public void CanRemoveRow_OneRow_IsFalse() =>
        Assert.False(SpellDeckModel.CanRemoveRow(rows: 1, allTabCounts: [0]));

    [Fact]
    public void CanRemoveRow_WhenTheRemainingRowsHoldTheFullestTab_IsTrue() =>
        Assert.True(SpellDeckModel.CanRemoveRow(rows: 3, allTabCounts: [1, Max * 2]));

    [Fact]
    public void CanRemoveRow_WhenTheRemainingRowsCannotHoldTheFullestTab_IsFalse() =>
        Assert.False(SpellDeckModel.CanRemoveRow(rows: 3, allTabCounts: [1, Max * 2 + 1]));

    [Fact]
    public void CanRemoveRow_EmptyTabs_IsTrueWithTwoRows() =>
        Assert.True(SpellDeckModel.CanRemoveRow(rows: 2, allTabCounts: []));

    // --- PlanDropOnSlot ---

    [Fact]
    public void DropOnSlot_SpellAlreadyInTab_IsAReorder_EvenWhenFull()
    {
        var plan = SpellDeckModel.PlanDropOnSlot(
            SpellDeckDragSource.FromSpellbook(2u), tab: 3, position: 0, favorites: [1, 2, 3], capacity: 3);
        Assert.Equal(SpellDeckDropKind.Reorder, plan.Kind);
        Assert.Equal([SpellDeckFavoriteOp.Set(3, 0, 2u)], plan.Ops);
    }

    [Fact]
    public void DropOnSlot_Reorder_ClampsPositionToCountMinusOne() =>
        Assert.Equal([SpellDeckFavoriteOp.Set(0, 2, 1u)],
            SpellDeckModel.PlanDropOnSlot(new SpellDeckDragSource(0, 1u), 0, position: 40, favorites: [1, 2, 3], capacity: 12).Ops);

    [Fact]
    public void DropOnSlot_SlotFromTheSameTab_IsAlwaysAReorder()
    {
        var plan = SpellDeckModel.PlanDropOnSlot(new SpellDeckDragSource(9, 2u), 9, position: 1, favorites: [1, 2], capacity: 2);
        Assert.Equal(SpellDeckDropKind.Reorder, plan.Kind);
        Assert.Equal([SpellDeckFavoriteOp.Set(9, 1, 2u)], plan.Ops);
    }

    [Fact]
    public void DropOnSlot_NewSpell_InsertsAtPosition()
    {
        var plan = SpellDeckModel.PlanDropOnSlot(SpellDeckDragSource.FromSpellbook(7u), 1, position: 1, favorites: [1, 2], capacity: 3);
        Assert.Equal(SpellDeckDropKind.Insert, plan.Kind);
        Assert.Equal([SpellDeckFavoriteOp.Set(1, 1, 7u)], plan.Ops);
    }

    [Fact]
    public void DropOnSlot_Insert_ClampsPositionToCount()
    {
        Assert.Equal([SpellDeckFavoriteOp.Set(1, 2, 7u)],
            SpellDeckModel.PlanDropOnSlot(SpellDeckDragSource.FromSpellbook(7u), 1, position: 15, favorites: [1, 2], capacity: 24).Ops);
        Assert.Equal([SpellDeckFavoriteOp.Set(1, 0, 7u)],
            SpellDeckModel.PlanDropOnSlot(SpellDeckDragSource.FromSpellbook(7u), 1, position: -3, favorites: [1, 2], capacity: 24).Ops);
    }

    [Fact]
    public void DropOnSlot_NewSpellIntoFullTab_IsRefused_WithNoOps()
    {
        var plan = SpellDeckModel.PlanDropOnSlot(SpellDeckDragSource.FromSpellbook(7u), 1, 0, favorites: [1, 2, 3], capacity: 3);
        Assert.Equal(SpellDeckDropKind.RefusedFull, plan.Kind);
        Assert.False(plan.Accepted);
        Assert.Empty(plan.Ops);
    }

    [Fact]
    public void DropOnSlot_InsertThatExactlyFills_IsAccepted() =>
        Assert.Equal(SpellDeckDropKind.Insert,
            SpellDeckModel.PlanDropOnSlot(SpellDeckDragSource.FromSpellbook(7u), 1, 0, favorites: [1, 2], capacity: 3).Kind);

    // --- PlanDropOnTab ---

    [Fact]
    public void DropOnTab_SlotFromAnotherTab_MovesToTheEnd_AddFirst()
    {
        var plan = SpellDeckModel.PlanDropOnTab(new SpellDeckDragSource(0, 5u), destTab: 9, destFavorites: [1, 2], capacity: 12);
        Assert.Equal(SpellDeckDropKind.Move, plan.Kind);
        Assert.Equal([SpellDeckFavoriteOp.Set(9, 2, 5u), SpellDeckFavoriteOp.Remove(0, 5u)], plan.Ops);
    }

    [Fact]
    public void DropOnTab_SpellbookSpell_InsertsAtTheEnd()
    {
        var plan = SpellDeckModel.PlanDropOnTab(SpellDeckDragSource.FromSpellbook(5u), destTab: 2, destFavorites: [1], capacity: 12);
        Assert.Equal(SpellDeckDropKind.Insert, plan.Kind);
        Assert.Equal([SpellDeckFavoriteOp.Set(2, 1, 5u)], plan.Ops);
    }

    [Fact]
    public void DropOnTab_DestAlreadyHoldsTheSpell_IsNone_NotRefused()
    {
        foreach (var source in new[] { new SpellDeckDragSource(0, 2u), SpellDeckDragSource.FromSpellbook(2u) })
        {
            var plan = SpellDeckModel.PlanDropOnTab(source, destTab: 1, destFavorites: [1, 2], capacity: 2);
            Assert.Equal(SpellDeckDropKind.None, plan.Kind);
            Assert.True(plan.Accepted);
            Assert.Empty(plan.Ops);
        }
    }

    [Fact]
    public void DropOnTab_OnItsOwnTab_IsNone()
    {
        var plan = SpellDeckModel.PlanDropOnTab(new SpellDeckDragSource(4, 2u), destTab: 4, destFavorites: [1, 2], capacity: 12);
        Assert.Equal(SpellDeckDropKind.None, plan.Kind);
        Assert.Empty(plan.Ops);
    }

    [Fact]
    public void DropOnTab_DestFull_IsRefused()
    {
        foreach (var source in new[] { new SpellDeckDragSource(0, 9u), SpellDeckDragSource.FromSpellbook(9u) })
        {
            var plan = SpellDeckModel.PlanDropOnTab(source, destTab: 1, destFavorites: [1, 2], capacity: 2);
            Assert.Equal(SpellDeckDropKind.RefusedFull, plan.Kind);
            Assert.Empty(plan.Ops);
        }
    }

    [Fact]
    public void Remove_IsOneRemove() =>
        Assert.Equal([SpellDeckFavoriteOp.Remove(4, 8u)],
            SpellDeckModel.PlanRemove(new SpellDeckDragSource(4, 8u)).Ops);
}
