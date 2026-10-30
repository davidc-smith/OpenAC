using AcDream.Core.Items.Gear;
using Xunit;

namespace AcDream.Core.Tests.Items.Gear;

public sealed class StackCombinePlannerTests
{
    private static GearItemView Row(uint id, uint weenie = 0, int stack = 1, int max = 1, uint pack = 1, int packIndex = 0,
        int slot = 0, bool locked = false, string name = "x", GearTypeGroup group = GearTypeGroup.Other, float work = 0f,
        bool worn = false, bool isPack = false) =>
        new(id, name, group, 0u, work, 0, 0, stack, max, pack, packIndex, slot, worn, isPack, locked, weenie);

    [Fact]
    public void MergesEmptiestIntoFullest_WithoutOverflow()
    {
        var rows = new[] { Row(1, 9, 50, 100, slot: 0), Row(2, 9, 70, 100, slot: 1), Row(3, 9, 40, 100, slot: 2) };
        Assert.Equal(new[] { GearMove.Merge(3, 2, 30), GearMove.Merge(3, 1, 10) }, StackCombinePlanner.Plan(rows));
    }

    [Fact]
    public void ALockedStack_IsFilled_ButNeverEmptied()
    {
        var rows = new[] { Row(1, 9, 10, 100, locked: true), Row(2, 9, 60, 100, slot: 1) };
        Assert.Empty(StackCombinePlanner.Plan(rows));
        var filled = new[] { Row(1, 9, 60, 100, locked: true), Row(2, 9, 10, 100, slot: 1) };
        Assert.Equal(new[] { GearMove.Merge(2, 1, 10) }, StackCombinePlanner.Plan(filled));
    }

    [Fact]
    public void IgnoresFullUnstackableWornAndPackRows_AndOnlyLimitsTheGroups()
    {
        var rows = new[]
        {
            Row(1, 9, 100, 100), Row(2, 9, 5, 100, slot: 1), Row(3, 9, 5, 100, slot: 2),
            Row(4, 8, 1, 1, slot: 3), Row(5, 7, 5, 10, slot: 4), Row(6, 7, 5, 10, worn: true),
            Row(7, 6, 5, 10, slot: 5), Row(8, 6, 5, 10, slot: 6),
        };
        Assert.Equal(new[] { GearMove.Merge(3, 2, 5) }, StackCombinePlanner.Plan(rows, only: [2]));
        Assert.Equal(new[] { GearMove.Merge(3, 2, 5), GearMove.Merge(8, 7, 5) }, StackCombinePlanner.Plan(rows));
    }
}
