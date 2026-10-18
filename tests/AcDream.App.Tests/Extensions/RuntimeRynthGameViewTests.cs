using System.Numerics;
using AcDream.Core.Items;
using AcDream.Plugin.Abstractions;
using AcDream.Rynth.Host;

namespace AcDream.App.Tests.Extensions;

[Collection("Rynth")]
public sealed class RuntimeRynthGameViewTests
{
    [Fact]
    public void ReadsThePlayerAndObjectsFromTheRuntime()
    {
        var runtime = GameRuntimeTestFactory.Create();
        runtime.PlayerIdentity.ServerGuid = 0x50000001;
        var item = new ClientObject { ObjectId = 0x80000010, Name = "Drudge Skulker" };
        item.Properties.Int64s[1] = 42;
        runtime.InventoryOwner.Objects.AddOrUpdate(item);
        var view = new RuntimeRynthGameView(runtime);

        Assert.Equal(0x50000001u, view.PlayerId);
        Assert.True(view.TryGetObjectName(0x80000010, out string name));
        Assert.Equal("Drudge Skulker", name);
        Assert.False(view.TryGetObjectName(0x80000011, out _));
        Assert.True(view.TryGetInt64Property(0x80000010, 1, out long value));
        Assert.Equal(42, value);
        Assert.False(view.TryGetInt64Property(0x80000010, 6, out _));
    }

    [Fact]
    public void WriteChatLine_AddsASystemLine()
    {
        var runtime = GameRuntimeTestFactory.Create();
        var view = new RuntimeRynthGameView(runtime);

        view.WriteChatLine("Rynth tracker: no data yet", 0);

        Assert.Contains(
            runtime.CommunicationOwner.Chat.Snapshot(),
            entry => entry.Text == "Rynth tracker: no data yet");
    }

    [Fact]
    public void ReadsObjectFieldsAndContentsWithWieldedItems()
    {
        var runtime = GameRuntimeTestFactory.Create();
        runtime.PlayerIdentity.ServerGuid = 0x50000001;
        var pack = new ClientObject
        {
            ObjectId = 0x80000020,
            Name = "Pack",
            Type = ItemType.Container,
            ContainerId = 0x50000001,
            ItemsCapacity = 24,
            IconId = 0x06001111,
            WeenieClassId = 136,
            PublicWeenieBitfield = 0x1,
        };
        pack.Properties.Ints[28] = 5;
        var sword = new ClientObject
        {
            ObjectId = 0x80000010,
            Name = "Sword",
            WielderId = 0x50000001,
            CurrentlyEquippedLocation = EquipMask.MeleeWeapon,
        };
        runtime.InventoryOwner.Objects.AddOrUpdate(pack);
        runtime.InventoryOwner.Objects.AddOrUpdate(sword);
        // AddOrUpdate indexes wielded items only; pack contents arrive as a server listing.
        runtime.InventoryOwner.Objects.ReplaceContents(0x50000001, new uint[] { 0x80000020 });
        var view = new RuntimeRynthGameView(runtime);

        Assert.False(view.TryGetObject(0x80000099, out _));
        Assert.True(view.TryGetObject(0x80000020, out RynthObjectInfo info));
        Assert.Equal(0x50000001u, info.ContainerId);
        Assert.Equal((uint)ItemType.Container, info.ItemType);
        Assert.Equal(24, info.ItemsCapacity);
        Assert.Equal(0x06001111u, info.IconId);
        Assert.Equal(136u, info.Wcid);
        Assert.Equal(0x1u, info.Bitfield);
        Assert.False(info.Identified);
        Assert.Equal(5, info.Ints[28]);
        Assert.True(view.TryGetObject(0x80000010, out RynthObjectInfo swordInfo));
        Assert.Equal((uint)EquipMask.MeleeWeapon, swordInfo.EquipLocation);
        Assert.Equal(0u, swordInfo.Bitfield);

        Assert.Equal([0x80000010u, 0x80000020u], view.GetContainerContents(0x50000001));
        Assert.Empty(view.GetContainerContents(0x80000020));
        Assert.False(view.TryGetObjectPosition(0x80000020, out _, out _));
        Assert.Equal(string.Empty, view.WorldName);
        Assert.Equal(string.Empty, view.AccountName);
    }

    [Fact]
    public void ThePlayerObjectMergesTheDescriptionOverItsOwnTables()
    {
        var runtime = GameRuntimeTestFactory.Create();
        runtime.PlayerIdentity.ServerGuid = 0x50000001;
        var player = new ClientObject { ObjectId = 0x50000001, Name = "Tester" };
        player.Properties.Ints[7] = 1;
        player.Properties.Ints[8] = 3;
        runtime.InventoryOwner.Objects.AddOrUpdate(player);
        var description = new PropertyBundle();
        description.Ints[7] = 2;
        var view = new RuntimeRynthGameView(runtime);

        // Before a description lands, the object's own table answers.
        Assert.True(view.TryGetObject(0x50000001, out RynthObjectInfo before));
        Assert.Equal(1, before.Ints[7]);

        runtime.CharacterOwner.LocalPlayer.OnProperties(description);

        Assert.True(view.TryGetObject(0x50000001, out RynthObjectInfo merged));
        Assert.Equal(2, merged.Ints[7]);
        Assert.Equal(3, merged.Ints[8]);
        Assert.Equal(1, player.Properties.Ints[7]);
    }

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(true, false, true)]
    [InlineData(true, true, false)]
    public void IdentifiedNeedsAnAnsweredAppraisalThatSucceeded(bool answered, bool unsuccessful, bool expected)
    {
        var runtime = GameRuntimeTestFactory.Create();
        var item = new ClientObject
        {
            ObjectId = 0x80000010,
            AppraisalAnswered = answered,
            LastAppraisalUnsuccessful = unsuccessful,
        };
        runtime.InventoryOwner.Objects.AddOrUpdate(item);
        var view = new RuntimeRynthGameView(runtime);

        Assert.True(view.TryGetObject(0x80000010, out RynthObjectInfo info));
        Assert.Equal(expected, info.Identified);
    }

    [Fact]
    public void StateComesFromTheSurfaceInRynthCoresUnits()
    {
        var runtime = GameRuntimeTestFactory.Create();
        runtime.PlayerIdentity.ServerGuid = 0x50000001;
        var automation = new FakeRynthAutomation();
        automation.CombatArea.Snapshot = default(PluginCombatSnapshot) with { Mode = PluginCombatMode.Missile };
        automation.CharacterArea.MaxHealth = 100;
        automation.CharacterArea.CurrentHealth = 40;
        automation.CharacterArea.EnchantmentList.Add(new PluginActiveEnchantment(2, 1, 1, 60));
        automation.CharacterArea.EnchantmentList.Add(new PluginActiveEnchantment(3, 1, 1, double.PositiveInfinity));
        automation.CharacterArea.SkillList.Add(new PluginSkillInfo(6, "Melee Defense", PluginSkillTraining.Specialized, 250) { Base = 200 });
        automation.SpellArea.Known.Add(2);
        automation.RecoveryArea.Busy = 3;
        automation.ObjectArea.OpenContainer = 0x80000044;
        runtime.InventoryOwner.ServerReplies.RecordUseDone(0x36);
        var view = new RuntimeRynthGameView(runtime, automation: automation);

        Assert.Equal(4, view.CombatMode);
        Assert.True(view.TryGetPlayerVitals(out RynthVitals vitals));
        Assert.Equal((40u, 100u), (vitals.Health, vitals.MaxHealth));
        Assert.Equal(3, view.BusyCount);
        Assert.Equal(0x80000044u, view.OpenContainerId);
        Assert.Equal(1, view.IsSpellKnown(0x50000001, 2));
        Assert.Equal(0, view.IsSpellKnown(0x50000001, 9));
        Assert.Equal(-1, view.IsSpellKnown(0x80000010, 2));
        Assert.True(view.TryGetSkill(0x50000001, 6, out int buffed, out int baseLevel, out int training));
        Assert.Equal((250, 200, 3), (buffed, baseLevel, training));
        Assert.False(view.TryGetSkill(0x80000010, 6, out _, out _, out _));
        Assert.Equal((1, 0x36u), (view.ServerReplies.UseDoneSequence, view.ServerReplies.LastUseDoneError));

        IReadOnlyList<RynthEnchantment> enchantments = view.PlayerEnchantments!;
        double now = view.ServerTime;
        Assert.InRange(enchantments[0].ExpiryServerTime - now, 59.0, 60.0);
        Assert.Equal(double.MaxValue, enchantments[1].ExpiryServerTime);
    }

    [Fact]
    public void BeforeLoginTheStateReadsAsNotAvailable()
    {
        var runtime = GameRuntimeTestFactory.Create();
        var view = new RuntimeRynthGameView(runtime);   // no surface: the no-op one

        Assert.Equal(0, view.ServerTime);
        Assert.Null(view.KnownSpells);
        Assert.Null(view.PlayerEnchantments);
        Assert.Equal(1, view.CombatMode);
        Assert.False(view.TryGetPlayerVitals(out _));
        Assert.False(view.TryGetPlayerPose(out _, out _, out _));
        Assert.Equal(-1, view.IsSpellKnown(0, 1));
    }

    [Fact]
    public void ObjectBoolsFloatsAndStringsComeThroughTryGetObject()
    {
        var runtime = GameRuntimeTestFactory.Create();
        var item = new ClientObject { ObjectId = 0x80000010, Name = "Wand" };
        item.Properties.Bools[22] = true;
        item.Properties.Floats[5] = 0.25;
        item.Properties.Strings[16] = "A wand";
        runtime.InventoryOwner.Objects.AddOrUpdate(item);
        var view = new RuntimeRynthGameView(runtime);

        Assert.True(view.TryGetObject(0x80000010, out RynthObjectInfo info));
        Assert.True(info.Bools[22]);
        Assert.Equal(0.25, info.Floats[5]);
        Assert.Equal("A wand", info.Strings[16]);
    }

    [Fact]
    public void ActionsGoThroughTheSurface()
    {
        var runtime = GameRuntimeTestFactory.Create();
        var automation = new FakeRynthAutomation();
        automation.RecoveryArea.Busy = 3;
        var view = new RuntimeRynthGameView(runtime, automation: automation);

        Assert.True(view.UseObject(0x80000010));
        Assert.True(view.MoveItem(0x80000011, 0x50000001, 2, 5));
        Assert.False(view.MoveItem(0x80000011, 0x50000001, 2, 0));
        Assert.True(view.MergeStack(0x80000012, 0x80000013));
        Assert.Equal(
            new[] { "use 80000010", "move 80000011 50000001 5 2", "merge 80000012 80000013 0" },
            automation.ItemArea.Calls);
        Assert.True(view.RequestIdentify(0x80000014));
        Assert.Equal(new[] { 0x80000014u }, automation.ObjectArea.Identified);
        Assert.True(view.WieldItem(0x80000015, 0x00200000));
        Assert.Equal(new[] { (0x80000015u, 0x00200000u) }, automation.EquipmentArea.Equipped);
        Assert.True(view.ChangeCombatMode(8));
        Assert.False(view.ChangeCombatMode(3));
        Assert.Equal(new[] { PluginCombatMode.Magic }, automation.CombatArea.ModesRequested);

        view.ResetBusyCount();
        Assert.Equal(0, automation.RecoveryArea.Busy);

        automation.ItemArea.Answer = PluginItemCommandStatus.Busy;
        Assert.False(view.UseObject(0x80000010));
    }

    [Fact]
    public void SelectIsTrueWhenTheObjectEndsUpSelectedAndQueryHealthNeedsASession()
    {
        var runtime = GameRuntimeTestFactory.Create();
        var view = new RuntimeRynthGameView(runtime);

        Assert.True(view.SelectObject(0x80000010));
        Assert.True(view.SelectObject(0x80000010));   // already selected: still true, as in RynthCore
        Assert.Equal(0x80000010u, view.SelectedObjectId);
        Assert.False(view.QueryHealth(0x80000010));   // the test runtime has no session
    }

    [Fact]
    public void TryGetVitalBase_MapsRynthCoreStatIdsToVitalKinds()
    {
        var runtime = GameRuntimeTestFactory.Create();
        runtime.PlayerIdentity.ServerGuid = 0x50000001;
        var automation = new FakeRynthAutomation();
        automation.CharacterArea.VitalList.Add(new PluginVitalInfo(0, "Health", 10, 100) { Base = 90 });
        automation.CharacterArea.VitalList.Add(new PluginVitalInfo(1, "Stamina", 10, 110) { Base = 95 });
        automation.CharacterArea.VitalList.Add(new PluginVitalInfo(2, "Mana", 10, 120) { Base = 99 });
        var view = new RuntimeRynthGameView(runtime, automation: automation);

        Assert.True(view.TryGetVitalBase(0x50000001, 1, out uint health));
        Assert.Equal(90u, health);
        Assert.True(view.TryGetVitalBase(0x50000001, 3, out uint stamina));
        Assert.Equal(95u, stamina);
        Assert.True(view.TryGetVitalBase(0x50000001, 5, out uint mana));
        Assert.Equal(99u, mana);
        Assert.False(view.TryGetVitalBase(0x50000001, 2, out _));
        Assert.False(view.TryGetVitalBase(0x50000001, 6, out _));
        Assert.False(view.TryGetVitalBase(0x80000010, 1, out _));
    }
}
