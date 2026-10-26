using AcDream.App.UI.Hud;

namespace AcDream.App.Tests.UI.Hud;

public sealed class SpellDeckModelTests
{
    private static Func<int, IReadOnlyList<uint>> Tabs(params uint[][] tabs) =>
        tab => tab < tabs.Length ? tabs[tab] : [];

    [Fact]
    public void Visible_IsTheFirstSlotsThatFit() =>
        Assert.Equal([1u, 2u], SpellDeckModel.Visible([1u, 2u, 3u], 2));

    [Fact]
    public void MinSlots_IsTheFullestShownRow_Capped()
    {
        var rows = new[] { new SpellDeckRow(1, 0), new SpellDeckRow(2, 2) };
        uint[] big = Enumerable.Range(1, 40).Select(i => (uint)i).ToArray();
        Assert.Equal(3, SpellDeckModel.MinSlots(rows, Tabs([1, 2], [], [5, 6, 7])));
        Assert.Equal(SpellDeckGeometry.MaxCapacity, SpellDeckModel.MinSlots(rows, Tabs(big)));
        Assert.Equal(1, SpellDeckModel.MinSlots(rows, Tabs()));
    }

    [Fact]
    public void TabForNewRow_LowestUnshown_ElseFirst()
    {
        Assert.Equal(1, SpellDeckModel.TabForNewRow([new(1, 0), new(2, 2)]));
        var all = Enumerable.Range(0, 8).Select(t => new SpellDeckRow(t + 1, t)).ToArray();
        Assert.Equal(0, SpellDeckModel.TabForNewRow(all));
    }

    [Fact]
    public void Drop_ReorderInSameTab_OneSet_EvenWhenFull()
    {
        var plan = SpellDeckModel.PlanDrop(
            new SpellDeckDragSource(Tab: 0, SpellId: 1u), destTab: 0, index: 5,
            favorites: Tabs([1, 2, 3]), capacity: 3);
        Assert.Equal(SpellDeckDropKind.Reorder, plan.Kind);
        Assert.Equal([SpellDeckFavoriteOp.Set(0, 2, 1u)], plan.Ops);
    }

    [Fact]
    public void Drop_SpellbookSpellAlreadyInTab_IsAReorder()
    {
        var plan = SpellDeckModel.PlanDrop(
            SpellDeckDragSource.FromSpellbook(3u), destTab: 0, index: 0,
            favorites: Tabs([1, 2, 3]), capacity: 3);
        Assert.Equal(SpellDeckDropKind.Reorder, plan.Kind);
        Assert.Equal([SpellDeckFavoriteOp.Set(0, 0, 3u)], plan.Ops);
    }

    [Fact]
    public void Drop_NewSpell_Inserts_ClampedToTheEnd()
    {
        var plan = SpellDeckModel.PlanDrop(
            SpellDeckDragSource.FromSpellbook(9u), destTab: 1, index: 7,
            favorites: Tabs([], [4, 5]), capacity: 12);
        Assert.Equal(SpellDeckDropKind.Insert, plan.Kind);
        Assert.Equal([SpellDeckFavoriteOp.Set(1, 2, 9u)], plan.Ops);
    }

    [Fact]
    public void Drop_AcrossTabs_AddsThenRemoves()
    {
        var plan = SpellDeckModel.PlanDrop(
            new SpellDeckDragSource(Tab: 0, SpellId: 1u), destTab: 2, index: 0,
            favorites: Tabs([1, 2], [], [7]), capacity: 12);
        Assert.Equal(SpellDeckDropKind.Move, plan.Kind);
        Assert.Equal([SpellDeckFavoriteOp.Set(2, 0, 1u), SpellDeckFavoriteOp.Remove(0, 1u)], plan.Ops);
    }

    [Fact]
    public void Drop_NewSpellIntoFullRow_IsRefused_WithNoOps()
    {
        var plan = SpellDeckModel.PlanDrop(
            SpellDeckDragSource.FromSpellbook(9u), destTab: 0, index: 0,
            favorites: Tabs([1, 2, 3]), capacity: 3);
        Assert.Equal(SpellDeckDropKind.RefusedFull, plan.Kind);
        Assert.Empty(plan.Ops);
    }

    [Fact]
    public void Remove_IsOneRemove() =>
        Assert.Equal([SpellDeckFavoriteOp.Remove(4, 8u)],
            SpellDeckModel.PlanRemove(new SpellDeckDragSource(4, 8u)).Ops);
}
