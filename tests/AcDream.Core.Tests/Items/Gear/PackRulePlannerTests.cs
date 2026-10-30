using AcDream.Core.Items.Gear;
using Xunit;

namespace AcDream.Core.Tests.Items.Gear;

public sealed class PackRulePlannerTests
{
    private static GearItemView Row(uint id, uint weenie = 0, int stack = 1, int max = 1, uint pack = 1, int packIndex = 0,
        int slot = 0, bool locked = false, string name = "x", GearTypeGroup group = GearTypeGroup.Other, float work = 0f,
        bool worn = false, bool isPack = false) =>
        new(id, name, group, 0u, work, 0, 0, stack, max, pack, packIndex, slot, worn, isPack, locked, weenie);

    private static readonly GearPack Main = new(1u, 102, true), Side = new(2u, 3, false);

    private static (GearPackRule, GearQuery) Rule(uint pack, GearChip chip, bool on = true) =>
        (new GearPackRule("f", pack, false, on), GearQuery.Empty.With(chip));

    [Fact]
    public void FirstMatchingRuleWins_AndSkipsWornPacksLockedAndInPlace()
    {
        var rows = new[]
        {
            Row(10, group: GearTypeGroup.Salvage, pack: 1, slot: 0),
            Row(11, group: GearTypeGroup.Salvage, pack: 1, slot: 1, locked: true),
            Row(12, group: GearTypeGroup.Salvage, pack: 2, packIndex: 1, slot: 0),
            Row(13, group: GearTypeGroup.Gems, pack: 1, slot: 2),
            Row(14, group: GearTypeGroup.Salvage, worn: true, pack: 0, packIndex: -1, slot: -1),
        };
        var rules = new[] { Rule(2u, GearChip.OfType(GearTypeGroup.Salvage)), Rule(1u, GearChip.OfType(GearTypeGroup.Salvage)) };
        GearRulePlan plan = PackRulePlanner.Plan(rows, [Main, Side], rules);
        Assert.Equal(new[] { GearMove.Put(10, 2, -1) }, plan.Moves);
        Assert.Empty(plan.Full);
    }

    [Fact]
    public void AFullPack_TakesWhatFits_AndCountsTheRest()
    {
        var rows = Enumerable.Range(0, 4).Select(i => Row(20u + (uint)i, group: GearTypeGroup.Gems, pack: 1, slot: i))
            .Append(Row(30, pack: 2, packIndex: 1, slot: 0)).Append(Row(31, pack: 2, packIndex: 1, slot: 1)).ToArray();
        GearRulePlan plan = PackRulePlanner.Plan(rows, [Main, Side], [Rule(2u, GearChip.OfType(GearTypeGroup.Gems))]);
        Assert.Single(plan.Moves);
        Assert.Equal(3, plan.Full[2u]);
    }

    [Fact]
    public void DisabledEmptyAndMissingPackRules_AreSkipped_AndOnlyLimitsTheItems()
    {
        var rows = new[] { Row(40, group: GearTypeGroup.Gems, pack: 1, slot: 0), Row(41, group: GearTypeGroup.Gems, pack: 1, slot: 1) };
        var rules = new (GearPackRule, GearQuery)[]
        {
            Rule(2u, GearChip.OfType(GearTypeGroup.Gems), on: false),
            (new GearPackRule("e", 2u, false), GearQuery.Empty),
            Rule(99u, GearChip.OfType(GearTypeGroup.Gems)),
            Rule(2u, GearChip.OfType(GearTypeGroup.Gems)),
        };
        Assert.Equal(new[] { GearMove.Put(41, 2, -1) }, PackRulePlanner.Plan(rows, [Main, Side], rules, only: [41]).Moves);
    }
}
