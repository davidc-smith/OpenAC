using AcDream.Core.Combat;
using AcDream.Core.Items;
using AcDream.Core.Properties;
using AcDream.Core.Targeting;
using Xunit;

namespace AcDream.Core.Tests.Targeting;

public sealed class TargetPlateModelBuilderTests
{
    private const uint Me = 0x50000001u;
    private double _now = 50.0;

    private TargetPlateModel? Build(ClientObject obj, CombatState? combat = null, TargetAppraisalLog? log = null,
        int playerLevel = 100, uint selectedPackItem = 0, float distance = 4f)
    {
        var input = new TargetPlateInput(
            Object: obj, PlayerId: Me, PlayerLevel: playerLevel, Combat: combat ?? new CombatState(),
            Log: log ?? new TargetAppraisalLog(), Now: _now, DistanceMeters: distance,
            SelectedPackItemId: selectedPackItem, IsFellow: _ => false,
            PluginRows: Array.Empty<TargetPluginRow>(), PluginOverflowTooltip: null,
            PluginActions: Array.Empty<TargetPluginAction>());
        return TargetPlateModelBuilder.Build(input);
    }

    private static ClientObject Monster(int level = 45)
    {
        var o = new ClientObject { ObjectId = 7, Name = "Drudge Ravener", Type = ItemType.Creature,
            PublicWeenieBitfield = (uint)PublicWeenieFlags.Attackable };
        o.Properties.Ints[(uint)PropertyInt.Level] = level;
        return o;
    }

    [Fact]
    public void AMonsterHasVitalsAndNoAttackStud()
    {
        TargetPlateModel m = Build(Monster())!;
        Assert.Equal(TargetKind.Monster, m.Kind);
        Assert.NotNull(m.Vitals);
        Assert.Equal(new[] { TargetAction.Assess, TargetAction.UseItemOn, TargetAction.Follow, TargetAction.More },
            m.Studs.Select(s => s.Action));
        Assert.Contains(TargetAction.Examine, m.MoreMenu);
        Assert.Contains(TargetAction.CopyName, m.MoreMenu);
    }

    [Theory]
    [InlineData(100, 106, TargetCon.Higher)]
    [InlineData(100, 104, TargetCon.Even)]
    [InlineData(100, 95, TargetCon.Lower)]
    public void ConComparesLevels(int player, int target, TargetCon expected) =>
        Assert.Equal(expected, Build(Monster(target), playerLevel: player)!.Con);

    [Fact]
    public void VitalsAreStaleAfterTenSeconds()
    {
        var log = new TargetAppraisalLog();
        log.Record(7, true, 1f, now: 30.0);
        Assert.Equal(TargetVitalsState.Stale, Build(Monster(), log: log)!.Vitals!.Value.State);
        log.Record(7, true, 1f, now: 45.0);
        Assert.Equal(TargetVitalsState.Fresh, Build(Monster(), log: log)!.Vitals!.Value.State);
        log.Record(7, false, 1f, now: 49.0);
        Assert.Equal(TargetVitalsState.Failed, Build(Monster(), log: log)!.Vitals!.Value.State);
    }

    [Fact]
    public void AnUnassessedItemAsksForAssess()
    {
        var sword = new ClientObject { ObjectId = 9, Name = "Fine Silver Long Sword", Type = ItemType.MeleeWeapon, Value = 4200, Burden = 320 };
        TargetPlateModel m = Build(sword)!;
        Assert.Equal(TargetKind.Item, m.Kind);
        Assert.True(m.NeedsAssess);
        Assert.Null(m.Vitals);
        Assert.Contains(m.Rows, r => r.Label == "Value" && r.Value == "4,200");
        Assert.Contains(m.Rows, r => r.Value == "Assess for details");
    }

    [Fact]
    public void GiveIsDisabledWithoutAPackItem()
    {
        var player = new ClientObject { ObjectId = 2, Name = "Sammy", Type = ItemType.Creature, PublicWeenieBitfield = (uint)PublicWeenieFlags.Player };
        TargetStud give = Build(player)!.Studs.Single(s => s.Action == TargetAction.Give);
        Assert.False(give.Enabled);
        Assert.False(string.IsNullOrEmpty(give.DisabledReason));
        Assert.True(Build(player, selectedPackItem: 0x80001234u)!.Studs.Single(s => s.Action == TargetAction.Give).Enabled);
    }

    [Fact]
    public void EveryKindHasFourOrFewerStudsEndingInMore()
    {
        ClientObject[] objs =
        {
            Monster(),
            new() { ObjectId = 3, Name = "Wolf", Type = ItemType.Creature, PetOwnerId = Me },
            new() { ObjectId = 2, Name = "Sammy", Type = ItemType.Creature, PublicWeenieBitfield = (uint)PublicWeenieFlags.Player },
            new() { ObjectId = 4, Name = "Thorsten", Type = ItemType.Creature, PublicWeenieBitfield = (uint)PublicWeenieFlags.Vendor },
            new() { ObjectId = 5, Name = "Sword", Type = ItemType.MeleeWeapon },
            new() { ObjectId = 6, Name = "Corpse of Drudge", Type = ItemType.Container, PublicWeenieBitfield = (uint)PublicWeenieFlags.Corpse },
            new() { ObjectId = 8, Name = "Chest", Type = ItemType.Container },
            new() { ObjectId = 10, Name = "Portal to Ayan Baqur", Type = ItemType.Portal, PublicWeenieBitfield = (uint)PublicWeenieFlags.Portal },
            new() { ObjectId = 11, Name = "Door", Type = ItemType.Misc, PublicWeenieBitfield = (uint)PublicWeenieFlags.Door },
            new() { ObjectId = Me, Name = "Me", Type = ItemType.Creature, PublicWeenieBitfield = (uint)PublicWeenieFlags.Player },
        };
        foreach (ClientObject o in objs)
        {
            TargetPlateModel m = Build(o)!;
            Assert.InRange(m.Studs.Count, 2, 4);
            Assert.Equal(TargetAction.More, m.Studs[^1].Action);
            Assert.DoesNotContain(m.Studs, s => s.Action == TargetAction.None);
        }
    }
}
