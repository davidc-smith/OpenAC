using AcDream.Core.Items.Gear;
using Xunit;

namespace AcDream.Core.Tests.Items.Gear;

public sealed class PackTidyPlannerTests
{
    private static GearItemView Row(uint id, uint weenie = 0, int stack = 1, int max = 1, uint pack = 1, int packIndex = 0,
        int slot = 0, bool locked = false, string name = "x", GearTypeGroup group = GearTypeGroup.Other, float work = 0f,
        bool worn = false, bool isPack = false) =>
        new(id, name, group, 0u, work, 0, 0, stack, max, pack, packIndex, slot, worn, isPack, locked, weenie);

    [Fact]
    public void ReordersByGroupNameThenWorkmanship_MovingOnlyWhatIsOutOfPlace()
    {
        var rows = new[]
        {
            Row(1, name: "Zircon", group: GearTypeGroup.Gems, slot: 0),
            Row(2, name: "Steel", group: GearTypeGroup.Salvage, work: 5f, slot: 1),
            Row(3, name: "steel", group: GearTypeGroup.Salvage, work: 9f, slot: 2),
            Row(4, name: "Amber", group: GearTypeGroup.Gems, slot: 3),
        };
        Assert.Equal(new[] { GearMove.Put(3, 1, 0), GearMove.Put(2, 1, 1), GearMove.Put(4, 1, 2) },
            PackTidyPlanner.Plan(1u, rows));
    }

    [Fact]
    public void ATidyPack_NeedsNoMoves()
    {
        var rows = new[] { Row(1, name: "A", slot: 0), Row(2, name: "B", slot: 1) };
        Assert.Empty(PackTidyPlanner.Plan(1u, rows));
    }
}
