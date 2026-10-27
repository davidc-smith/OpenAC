using AcDream.Core.Combat;
using AcDream.Core.Items;
using AcDream.Core.Selection;
using AcDream.Core.Targeting;
using AcDream.Runtime.Gameplay;
using Xunit;

namespace AcDream.Runtime.Tests.Gameplay;

public sealed class TargetAutoAppraisalTests
{
    private const uint Player = 0x50000001u, Monster = 0x80000010u, Monster2 = 0x80000011u, Chest = 0x80000020u, Corpse = 0x80000030u;
    private double _now = 100.0;

    private (TargetAutoAppraisal auto, SelectionState sel, CombatState combat, List<uint> sent, TargetAppraisalLog log) Make()
    {
        var objects = new ClientObjectTable();
        objects.AddOrUpdate(new ClientObject { ObjectId = Monster, Name = "Drudge Ravener", Type = ItemType.Creature, PublicWeenieBitfield = (uint)PublicWeenieFlags.Attackable });
        objects.AddOrUpdate(new ClientObject { ObjectId = Monster2, Name = "Olthoi Soldier", Type = ItemType.Creature, PublicWeenieBitfield = (uint)PublicWeenieFlags.Attackable });
        objects.AddOrUpdate(new ClientObject { ObjectId = Chest, Name = "Chest", Type = ItemType.Container, PublicWeenieBitfield = (uint)PublicWeenieFlags.Openable });
        objects.AddOrUpdate(new ClientObject { ObjectId = Corpse, Name = "Corpse of Drudge", Type = ItemType.Container, PublicWeenieBitfield = (uint)PublicWeenieFlags.Corpse });
        var sel = new SelectionState();
        var combat = new CombatState();
        var sent = new List<uint>();
        var log = new TargetAppraisalLog();
        var auto = new TargetAutoAppraisal(sel, objects, combat, log, () => Player,
            id => { sent.Add(id); return true; }, () => _now);
        return (auto, sel, combat, sent, log);
    }

    [Fact]
    public void SelectingAMonsterAppraisesIt()
    {
        var (auto, sel, _, sent, _) = Make();
        using (auto)
        {
            sel.Select(Monster, SelectionChangeSource.World);
            Assert.Equal(new[] { Monster }, sent);
        }
    }

    [Fact]
    public void SelectingACorpseAppraisesItButAChestIsLeftAlone()
    {
        var (auto, sel, _, sent, _) = Make();
        using (auto)
        {
            sel.Select(Chest, SelectionChangeSource.World);
            Assert.Empty(sent);
            sel.Select(Corpse, SelectionChangeSource.World);
            Assert.Equal(new[] { Corpse }, sent);
        }
    }

    [Fact]
    public void TriggersInsideTheGapCoalesceIntoOneSendWhenItEnds()
    {
        var (auto, sel, combat, sent, _) = Make();
        using (auto)
        {
            sel.Select(Monster, SelectionChangeSource.World);
            _now += 0.5;
            combat.OnAttackerNotification("Drudge Ravener", 1u, 10u, 0.1);
            _now += 0.5;
            combat.OnAttackerNotification("Drudge Ravener", 1u, 10u, 0.1);
            auto.Tick();
            Assert.Single(sent);
            _now += 1.0; // 2.0 s since the first send
            auto.Tick();
            Assert.Equal(new[] { Monster, Monster }, sent);
            auto.Tick();
            Assert.Equal(2, sent.Count);
        }
    }

    [Fact]
    public void AHitOnSomethingElseDoesNotTrigger()
    {
        var (auto, sel, combat, sent, _) = Make();
        using (auto)
        {
            sel.Select(Monster, SelectionChangeSource.World);
            _now += 3.0;
            combat.OnAttackerNotification("Olthoi Soldier", 1u, 10u, 0.1);
            auto.Tick();
            Assert.Single(sent);
        }
    }

    [Fact]
    public void AHealthChangeSinceTheLastIdTriggers()
    {
        var (auto, sel, combat, sent, log) = Make();
        using (auto)
        {
            sel.Select(Monster, SelectionChangeSource.World);
            auto.OnAppraised(Monster, success: true);   // health 1.0 recorded
            _now += 3.0;
            combat.OnUpdateHealth(Monster, 1.0f);       // unchanged: no trigger
            auto.Tick();
            Assert.Single(sent);
            combat.OnUpdateHealth(Monster, 0.6f);
            auto.Tick();
            Assert.Equal(2, sent.Count);
        }
    }

    [Fact]
    public void ChangingTheSelectionCancelsAPendingSend()
    {
        var (auto, sel, combat, sent, _) = Make();
        using (auto)
        {
            sel.Select(Monster, SelectionChangeSource.World);
            combat.OnAttackerNotification("Drudge Ravener", 1u, 10u, 0.1); // pending
            sel.Clear(SelectionChangeSource.System);
            _now += 5.0;
            auto.Tick();
            Assert.Single(sent);
        }
    }

    [Fact]
    public void ReselectingWithAFreshIdSendsNothing()
    {
        var (auto, sel, _, sent, _) = Make();
        using (auto)
        {
            sel.Select(Monster, SelectionChangeSource.World);
            auto.OnAppraised(Monster, success: true);
            _now += 0.5;
            sel.Clear(SelectionChangeSource.System);
            sel.Select(Monster, SelectionChangeSource.World);
            Assert.Single(sent);
        }
    }

    [Fact]
    public void AFailedIdIsLoggedAndRetriedOnTriggers()
    {
        var (auto, sel, combat, sent, log) = Make();
        using (auto)
        {
            sel.Select(Monster, SelectionChangeSource.World);
            auto.OnAppraised(Monster, success: false);
            Assert.True(log.TryGet(Monster, out TargetAppraisalEntry e));
            Assert.False(e.Success);
            _now += 2.5;
            combat.OnAttackerNotification("Drudge Ravener", 1u, 10u, 0.1);
            Assert.Equal(2, sent.Count);
        }
    }

    [Fact]
    public void ReselectingWithinTheGapWaitsForItThenSendsOnce()
    {
        var (auto, sel, _, sent, _) = Make();
        using (auto)
        {
            sel.Select(Monster, SelectionChangeSource.World);
            _now += 0.5;
            sel.Select(Monster2, SelectionChangeSource.World);
            _now += 0.5;
            sel.Select(Monster, SelectionChangeSource.World);
            Assert.Equal(new[] { Monster, Monster2 }, sent);
            _now += 0.9;
            auto.Tick();
            Assert.Equal(2, sent.Count);
            _now += 0.1; // 2.0 s since Monster's send
            auto.Tick();
            Assert.Equal(new[] { Monster, Monster2, Monster }, sent);
            auto.Tick();
            Assert.Equal(3, sent.Count);
        }
    }

    [Fact]
    public void APendingSendForOneTargetIsNotSentAfterTheSelectionMovesOn()
    {
        var (auto, sel, combat, sent, _) = Make();
        using (auto)
        {
            sel.Select(Monster, SelectionChangeSource.World);
            combat.OnAttackerNotification("Drudge Ravener", 1u, 10u, 0.1); // pending for A
            sel.Select(Monster2, SelectionChangeSource.World);
            _now += 5.0;
            auto.Tick();
            Assert.Equal(new[] { Monster, Monster2 }, sent);
        }
    }

    [Fact]
    public void AFailedIdIsRetriedOnReselectWhereASuccessfulOneIsNot()
    {
        var (auto, sel, _, sent, log) = Make();
        using (auto)
        {
            sel.Select(Monster, SelectionChangeSource.World);
            sel.Select(Monster2, SelectionChangeSource.World);
            auto.OnAppraised(Monster, success: false);
            auto.OnAppraised(Monster2, success: true);
            Assert.False(log.TryGet(Monster, out TargetAppraisalEntry f) && f.Success);
            Assert.True(log.TryGet(Monster2, out TargetAppraisalEntry ok) && ok.Success);
            _now += 1.0;
            sel.Select(Monster2, SelectionChangeSource.World);  // fresh success: nothing
            Assert.Equal(2, sent.Count);
            sel.Select(Monster, SelectionChangeSource.World);   // failed: wants a retry, but the gap holds it
            Assert.Equal(2, sent.Count);
            _now += 1.0;
            auto.Tick();
            Assert.Equal(new[] { Monster, Monster2, Monster }, sent);
        }
    }
}
