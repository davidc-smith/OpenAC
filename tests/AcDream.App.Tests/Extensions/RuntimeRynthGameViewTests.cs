using AcDream.Core.Combat;
using System.Numerics;
using AcDream.Core.Items;
using AcDream.Core.Net;
using AcDream.Core.Net.Messages;
using AcDream.Core.Physics;
using AcDream.Core.Spells;
using AcDream.Plugin.Abstractions;
using AcDream.Runtime.Entities;
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
    }

    [Fact]
    public void PlayerEnchantmentsComeFromTheFullRegistryAsRynthCoreReadsThem()
    {
        var runtime = GameRuntimeTestFactory.Create();
        runtime.PlayerIdentity.ServerGuid = 0x50000001;
        double clock = runtime.EffectClock.NowSeconds;
        Spellbook book = runtime.CharacterOwner.Spellbook;
        // Two layers of one spell: the stronger covers the weaker, and both are reported.
        book.OnEnchantmentAdded(new ActiveEnchantmentRecord(0x1234, 1, 60, 0x50000001, Bucket: 1, StartTime: clock));
        book.OnEnchantmentAdded(new ActiveEnchantmentRecord(0x1234, 2, 90, 0x50000001, Bucket: 1, StartTime: clock));
        book.OnEnchantmentAdded(new ActiveEnchantmentRecord(0x1300, 1, -1, 0x50000001, Bucket: 2, StartTime: clock));
        book.OnEnchantmentAdded(new ActiveEnchantmentRecord(
            Spellbook.CooldownSpellOffset + 5, 1, 30, 0x50000001, Bucket: Spellbook.CooldownBucket, StartTime: clock));
        // Id 0 once masked to 16 bits: RynthCore's walk skips it.
        book.OnEnchantmentAdded(new ActiveEnchantmentRecord(0x10000, 1, 60, 0x50000001, Bucket: 1, StartTime: clock));
        book.OnEnchantmentAdded(new ActiveEnchantmentRecord(0x1400, 1, -1, 0x50000001, Bucket: 4, StartTime: clock));
        var view = new RuntimeRynthGameView(runtime, automation: new FakeRynthAutomation());

        IReadOnlyList<RynthEnchantment> enchantments = view.PlayerEnchantments!;

        Assert.Equal(4, enchantments.Count);   // vitae (bucket 4) is not in the three lists
        double now = view.ServerTime;
        RynthEnchantment[] byId = [.. enchantments.OrderBy(e => e.SpellId)];
        Assert.Equal([0x1234u, 0x1234u, 0x1300u, 0x8005u], byId.Select(e => (uint)e.SpellId));
        Assert.Equal(double.MaxValue, byId[2].ExpiryServerTime);
        Assert.InRange(byId[3].ExpiryServerTime - now, 29.0, 30.0);
        double[] layers = [.. byId.Take(2).Select(e => e.ExpiryServerTime - now).Order()];
        Assert.InRange(layers[0], 59.0, 60.0);
        Assert.InRange(layers[1], 89.0, 90.0);
    }

    [Fact]
    public void QueryHealthIsFalseWhenNotInWorld()
    {
        // The test runtime has no session, so this pins the contract only; it does not cover the in-world gate against a live session.
        var runtime = GameRuntimeTestFactory.Create();
        var automation = new FakeRynthAutomation { Available = false };
        var view = new RuntimeRynthGameView(runtime, automation: automation);

        Assert.False(view.QueryHealth(0x80000010));
    }

    [Fact]
    public void KnownSpellsAreAscendingOnceLoggedIn()
    {
        var runtime = GameRuntimeTestFactory.Create();
        runtime.PlayerIdentity.ServerGuid = 0x50000001;
        foreach (uint spell in new uint[] { 30, 5, 17 })
            runtime.CharacterOwner.Spellbook.OnSpellLearned(spell);
        var automation = new FakeRynthAutomation { Available = false };
        var view = new RuntimeRynthGameView(runtime, automation: automation);

        Assert.Null(view.KnownSpells);   // the same gate as IsSpellKnown

        automation.Available = true;
        Assert.Equal([5u, 17u, 30u], view.KnownSpells);
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

    [Fact]
    public void HeadingAndPortalingComeFromTheNavigationAndObjectAreas()
    {
        var runtime = GameRuntimeTestFactory.Create();
        var automation = new FakeRynthAutomation();
        var position = new PluginNavigationPosition(0x01010101, 1, 2, 3, 123.5f, true);
        automation.NavigationArea.Snapshot = default(PluginNavigationSnapshot) with
        {
            IsAvailable = true,
            IsPortalSpace = true,
            Position = position,
        };
        automation.ObjectArea.Known[0x80000010] = new PluginWorldObject(
            0x80000010, 1, "A", PluginObjectClass.Unknown, 0, 0, 0)
        { HasPosition = true, Position = position with { HeadingDegrees = 45f } };
        automation.ObjectArea.Known[0x80000011] = new PluginWorldObject(
            0x80000011, 1, "B", PluginObjectClass.Unknown, 0, 0, 0)
        { HasPosition = false, Position = position };
        var view = new RuntimeRynthGameView(runtime, automation: automation);

        Assert.True(view.TryGetPlayerHeading(out float heading));
        Assert.Equal(123.5f, heading);
        Assert.True(view.IsPortaling);
        Assert.True(view.TryGetObjectHeading(0x80000010, out float objectHeading));
        Assert.Equal(45f, objectHeading);
        Assert.False(view.TryGetObjectHeading(0x80000011, out _));   // known, but no position
        Assert.False(view.TryGetObjectHeading(0x80000012, out _));   // unknown

        automation.NavigationArea.Snapshot = default;
        Assert.False(view.TryGetPlayerHeading(out float none));
        Assert.Equal(0f, none);
        Assert.False(view.IsPortaling);
    }

    [Fact]
    public void AttributesAreTheirStatIdsAndPlayerOnly()
    {
        var runtime = GameRuntimeTestFactory.Create();
        runtime.PlayerIdentity.ServerGuid = 0x50000001;
        var automation = new FakeRynthAutomation();
        automation.CharacterArea.AttributeList.Add(new PluginAttributeInfo(0, "Strength", 120) { Base = 100 });
        automation.CharacterArea.AttributeList.Add(new PluginAttributeInfo(1, "Endurance", 90));
        var view = new RuntimeRynthGameView(runtime, automation: automation);

        Assert.True(view.TryGetAttribute(0x50000001, 1, out uint buffed, out uint baseLevel));   // StatId = kind + 1
        Assert.Equal((120u, 100u), (buffed, baseLevel));
        Assert.True(view.TryGetAttribute(0x50000001, 2, out buffed, out baseLevel));
        Assert.Equal((90u, 90u), (buffed, baseLevel));
        Assert.False(view.TryGetAttribute(0x50000001, 6, out _, out _));
        Assert.False(view.TryGetAttribute(0x80000010, 1, out _, out _));
    }

    [Fact]
    public void OnlyCreaturesAreAttackableAndNothingBeforeLogin()
    {
        var runtime = GameRuntimeTestFactory.Create();
        var creature = new ClientObject
        {
            ObjectId = 0x80000010,
            Type = ItemType.Creature,
            PublicWeenieBitfield = 0x10,   // attackable
        };
        var sword = new ClientObject { ObjectId = 0x80000011, Type = ItemType.MeleeWeapon, PublicWeenieBitfield = 0x10 };
        runtime.InventoryOwner.Objects.AddOrUpdate(creature);
        runtime.InventoryOwner.Objects.AddOrUpdate(sword);
        // The policy needs the player's own object to weigh a creature against.
        runtime.InventoryOwner.Objects.AddOrUpdate(new ClientObject { ObjectId = 0x50000001, Type = ItemType.Creature });
        var view = new RuntimeRynthGameView(runtime);

        Assert.False(view.IsAttackable(0x80000010));   // not logged in

        runtime.PlayerIdentity.ServerGuid = 0x50000001;
        Assert.True(view.IsAttackable(0x80000010));
        Assert.False(view.IsAttackable(0x80000011));   // not a creature
        Assert.False(view.IsAttackable(0x80000012));   // unknown object
    }

    [Fact]
    public void SelectingZeroClearsTheSelection()
    {
        var runtime = GameRuntimeTestFactory.Create();
        var view = new RuntimeRynthGameView(runtime);
        Assert.True(view.SelectObject(0x80000010));

        Assert.True(view.SelectObject(0));

        Assert.Equal(0u, view.SelectedObjectId);
    }

    [Fact]
    public void ThePlayerPoseIsTheEntitysBodyCellPosition()
    {
        var runtime = GameRuntimeTestFactory.Create();
        runtime.PlayerIdentity.ServerGuid = 0x50000001;
        var view = new RuntimeRynthGameView(runtime);
        Assert.False(view.TryGetPlayerPose(out _, out _, out _));

        RuntimeEntityRecord record = runtime.EntityObjects.Entities.AddActive(Spawn(0x50000001));
        var body = new PhysicsBody { Orientation = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, 0.5f) };
        body.SnapToCell(0x0101001Au, new Vector3(10, 20, 30), new Vector3(1, 2, 3));
        record.SetPhysicsBody(body);

        Assert.True(view.TryGetPlayerPose(out uint cell, out Vector3 origin, out Quaternion orientation));
        Assert.Equal(body.CellPosition.ObjCellId, cell);
        Assert.Equal(body.CellPosition.Frame.Origin, origin);
        Assert.Equal(body.CellPosition.Frame.Orientation, orientation);
        Assert.Equal(new Vector3(1, 2, 3), origin);
        Assert.True(view.TryGetObjectPosition(0x50000001, out uint objectCell, out _));
        Assert.Equal(cell, objectCell);
    }

    private static WorldSession.EntitySpawn Spawn(uint guid)
    {
        var position = new CreateObject.ServerPosition(0x0101FFFFu, 10f, 20f, 5f, 1f, 0f, 0f, 0f);
        var timestamps = new PhysicsTimestamps(
            Position: 1, Movement: 1, State: 1, Vector: 1, Teleport: 0,
            ServerControlledMove: 1, ForcePosition: 0, ObjDesc: 1, Instance: 1);
        var physics = new PhysicsSpawnData(
            RawState: 0x408u, Position: position, Movement: null, AnimationFrame: null,
            SetupTableId: 0x02000001u, MotionTableId: 0x09000001u, SoundTableId: null,
            PhysicsScriptTableId: null, Parent: null, Children: null, Scale: null, Friction: null,
            Elasticity: null, Translucency: null, Velocity: null, Acceleration: null,
            AngularVelocity: null, DefaultScriptType: null, DefaultScriptIntensity: null,
            Timestamps: timestamps);
        return new WorldSession.EntitySpawn(
            guid, position, 0x02000001u,
            Array.Empty<CreateObject.AnimPartChange>(),
            Array.Empty<CreateObject.TextureChange>(),
            Array.Empty<CreateObject.SubPaletteSwap>(),
            null, null, "fixture", null, null, 0x09000001u,
            PhysicsState: 0x408u, InstanceSequence: 1, MovementSequence: 1,
            ServerControlSequence: 1, PositionSequence: 1, Physics: physics);
    }

    [Fact]
    public void CombatReadsComeFromTheRuntime()
    {
        var runtime = GameRuntimeTestFactory.Create();
        runtime.PlayerIdentity.ServerGuid = 0x50000001;
        var view = new RuntimeRynthGameView(runtime, automation: new FakeRynthAutomation());

        Assert.False(view.IsCastGestureActive);   // the test runtime has no body
        Assert.False(view.TryGetTargetVitals(0x80000010, out _));
        runtime.ActionOwner.Combat.OnCreatureProfile(
            0x80000010, new CombatState.CreatureVitals(75, 100, 40, 80, null, null));
        Assert.True(view.TryGetTargetVitals(0x80000010, out RynthVitals vitals));
        Assert.Equal(new RynthVitals(75, 100, 40, 80, 0, 0), vitals);
        Assert.Null(view.GetObjectEnchantments(0x80000010));
    }

    [Fact]
    public void CombatActionsNeedASessionAndUseObjectOnGoesThroughTheItems()
    {
        var runtime = GameRuntimeTestFactory.Create();
        runtime.PlayerIdentity.ServerGuid = 0x50000001;
        var automation = new FakeRynthAutomation();
        var view = new RuntimeRynthGameView(runtime, automation: automation);

        Assert.False(view.MeleeAttack(0x80000010, 2, 0.5f));   // the test runtime has no session
        Assert.False(view.MissileAttack(0x80000010, 2, 0.5f));
        Assert.False(view.CancelAttack());
        Assert.True(view.CastSpell(0x80000010, 1));            // not known: refused locally as chat, so the call still ran
        Assert.True(view.UseObjectOn(0x80000020, 0x50000001));
        Assert.Contains("apply 80000020 50000001", automation.ItemArea.Calls);

        automation.Available = false;
        Assert.False(view.CastSpell(0x80000010, 1));
    }
}
