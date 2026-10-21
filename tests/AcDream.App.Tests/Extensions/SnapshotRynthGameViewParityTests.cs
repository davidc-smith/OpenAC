using System.Numerics;
using AcDream.Core.Items;
using AcDream.Core.Net;
using AcDream.Core.Net.Messages;
using AcDream.Core.Physics;
using AcDream.Core.Spells;
using AcDream.Plugin.Abstractions;
using AcDream.Runtime.Entities;
using AcDream.Rynth.EngineUi;
using AcDream.Rynth.Host;

namespace AcDream.App.Tests.Extensions;

[Collection("Rynth")]
public sealed class SnapshotRynthGameViewParityTests
{
    private const uint Player = 0x50000001, Vendor = 0x80000700, Door = 0x7A9B4001;

    [Fact]
    public void EveryReadAgreesWithTheLiveView()
    {
        var runtime = GameRuntimeTestFactory.Create();
        var automation = new FakeRynthAutomation();
        PopulateLikeRuntimeRynthGameViewTests(runtime, automation);
        var live = new RuntimeRynthGameView(runtime, automation: automation);
        using var builder = new RynthSnapshotBuilder(live, runtime, screen: null);
        var queue = new RynthActionQueue(_ => { }, () => false);
        var snap = new SnapshotRynthGameView(builder.Build(), queue, live, _ => { });

        // Not vacuous: the fixture gives real answers.
        Assert.True(live.TryGetPlayerVitals(out _));
        Assert.True(live.TryGetPlayerPose(out _, out _, out _));
        Assert.True(live.TryGetObjectHeading(Vendor, out _));
        Assert.True(live.TryGetObjectState(Door, out _));
        Assert.NotNull(live.Vendor);
        Assert.NotEmpty(live.KnownSpells!);
        Assert.NotEmpty(live.GetObjectEnchantments(Player)!);
        Assert.NotEmpty(live.GetContainerContents(Player));

        uint[] ids = [0x50000001, 0x80000110, 0x80000111, 0x80000112, 0x80000700, 0x12345678, Door, 0];
        Assert.Equal(live.PlayerId, snap.PlayerId);
        Assert.Equal(live.SelectedObjectId, snap.SelectedObjectId);
        Assert.Equal(live.AccountName, snap.AccountName);
        Assert.Equal(live.WorldName, snap.WorldName);
        Assert.Equal(live.IsPortaling, snap.IsPortaling);
        Assert.Equal(live.BusyCount, snap.BusyCount);
        Assert.Equal(live.CombatMode, snap.CombatMode);
        Assert.Equal(live.VitaeMultiplier, snap.VitaeMultiplier);
        Assert.Equal(live.OpenContainerId, snap.OpenContainerId);
        Assert.Equal(live.IsAttackInProgress, snap.IsAttackInProgress);
        Assert.Equal(live.InventoryRevision, snap.InventoryRevision);
        Assert.Equal(live.IsCastGestureActive, snap.IsCastGestureActive);
        Assert.Equal(live.AutoRunActive, snap.AutoRunActive);
        Assert.Equal(live.CurrentMovementIntent, snap.CurrentMovementIntent);
        Assert.Equal(live.ServerReplies, snap.ServerReplies);
        Assert.Equal(live.KnownSpells, snap.KnownSpells);
        Assert.Equal(live.PlayerEnchantments, snap.PlayerEnchantments);
        Assert.Equal(live.PlayerStatMods, snap.PlayerStatMods);
        Assert.Equivalent(live.PlayerProgress, snap.PlayerProgress, strict: true);
        Assert.Equal(live.Trade, snap.Trade);
        Assert.Equal(live.LiveObjectIds?.Order(), snap.LiveObjectIds?.Order());
        Assert.Equal(live.TryGetPlayerVitals(out var v1), snap.TryGetPlayerVitals(out var v2)); Assert.Equal(v1, v2);
        Assert.Equal(live.TryGetPlayerBaseVitals(out var a1, out var b1, out var c1), snap.TryGetPlayerBaseVitals(out var a2, out var b2, out var c2));
        Assert.Equal((a1, b1, c1), (a2, b2, c2));
        Assert.Equal(live.TryGetPlayerPose(out var pc1, out var po1, out var pq1), snap.TryGetPlayerPose(out var pc2, out var po2, out var pq2));
        Assert.Equal((pc1, po1, pq1), (pc2, po2, pq2));
        Assert.Equal(live.TryGetPlayerHeading(out var h1), snap.TryGetPlayerHeading(out var h2)); Assert.Equal(h1, h2);
        Assert.Equal(live.TryGetPendingItemRequest(out var t1, out var o1), snap.TryGetPendingItemRequest(out var t2, out var o2));
        Assert.Equal((t1, o1), (t2, o2));
        Assert.Equal(live.Vendor?.VendorId, snap.Vendor?.VendorId);
        Assert.Equal(live.Vendor?.Items.Count, snap.Vendor?.Items.Count);
        Assert.Equal(live.TryWorldToScreen(1, 2, 3, out var sx1, out var sy1), snap.TryWorldToScreen(1, 2, 3, out var sx2, out var sy2));
        Assert.Equal((sx1, sy1), (sx2, sy2));
        Assert.Equal(live.TryGetViewportSize(out var w1, out var vh1), snap.TryGetViewportSize(out var w2, out var vh2));
        Assert.Equal((w1, vh1), (w2, vh2));
        Assert.Equal(live.TryGetRadarRect(out var r0, out var r1, out var r2, out var r3), snap.TryGetRadarRect(out var q0, out var q1, out var q2, out var q3));
        Assert.Equal((r0, r1, r2, r3), (q0, q1, q2, q3));
        foreach (uint who in new uint[] { Player, Vendor, 0 })
        {
            for (uint skill = 0; skill <= 64; skill++)
            {
                Assert.Equal(live.TryGetSkill(who, skill, out var x1, out var y1, out var z1), snap.TryGetSkill(who, skill, out var x2, out var y2, out var z2));
                Assert.Equal((x1, y1, z1), (x2, y2, z2));
            }
            for (uint attribute = 0; attribute <= 7; attribute++)
            {
                Assert.Equal(live.TryGetAttribute(who, attribute, out var x1, out var y1), snap.TryGetAttribute(who, attribute, out var x2, out var y2));
                Assert.Equal((x1, y1), (x2, y2));
                Assert.Equal(live.TryGetVitalBase(who, attribute, out var m1), snap.TryGetVitalBase(who, attribute, out var m2));
                Assert.Equal(m1, m2);
            }
            foreach (uint spell in new uint[] { 1, 2, 5, 17, 30 })
                Assert.Equal(live.IsSpellKnown(who, spell), snap.IsSpellKnown(who, spell));
        }
        foreach (uint id in ids)
        {
            Assert.Equal(live.TryGetObjectName(id, out var n1), snap.TryGetObjectName(id, out var n2)); Assert.Equal(n1, n2);
            foreach (uint property in new uint[] { 1, 6 })
            {
                Assert.Equal(live.TryGetInt64Property(id, property, out var l1), snap.TryGetInt64Property(id, property, out var l2));
                Assert.Equal(l1, l2);
            }
            Assert.Equal(live.TryGetObject(id, out var i1), snap.TryGetObject(id, out var i2));
            if (i1 is not null) { Assert.Equal(i1.StackSize, i2.StackSize); Assert.Equal(i1.ContainerId, i2.ContainerId); Assert.Equal(i1.Ints, i2.Ints); }
            Assert.Equal(live.GetContainerContents(id), snap.GetContainerContents(id));
            Assert.Equal(live.TryCountContents(id, out var ci1, out var cc1), snap.TryCountContents(id, out var ci2, out var cc2)); Assert.Equal((ci1, cc1), (ci2, cc2));
            Assert.Equal(live.TryGetObjectPosition(id, out var oc1, out var oo1), snap.TryGetObjectPosition(id, out var oc2, out var oo2)); Assert.Equal((oc1, oo1), (oc2, oo2));
            Assert.Equal(live.TryGetObjectHeading(id, out var oh1), snap.TryGetObjectHeading(id, out var oh2)); Assert.Equal(oh1, oh2, 3);
            Assert.Equal(live.TryGetObjectState(id, out var s1), snap.TryGetObjectState(id, out var s2)); Assert.Equal(s1, s2);
            Assert.Equal(live.TryGetObjectMotionOn(id, out var m1), snap.TryGetObjectMotionOn(id, out var m2)); Assert.Equal(m1, m2);
            Assert.Equal(live.GetPalettes(id), snap.GetPalettes(id));
            Assert.Equal(live.GetObjectEnchantments(id), snap.GetObjectEnchantments(id));
            Assert.Equal(live.IsAttackable(id), snap.IsAttackable(id));
            Assert.Equal(live.IsOwnedItem(id), snap.IsOwnedItem(id));
            Assert.Equal(live.IsOwnedTinkeringTool(id), snap.IsOwnedTinkeringTool(id));
            Assert.Equal(live.IsSpellKnown(id, 1), snap.IsSpellKnown(id, 1));
            Assert.Equal(live.TryGetTargetVitals(id, out var tv1), snap.TryGetTargetVitals(id, out var tv2)); Assert.Equal(tv1, tv2);
            Assert.Equal(live.TryGetHealthRatio(id, out var r1h), snap.TryGetHealthRatio(id, out var r2h)); Assert.Equal(r1h, r2h);
        }
    }

    // The player's enchantments follow the spellbook part: a later build on an idle object table
    // still shows a new enchantment (the object entries are not re-derived then).
    [Fact]
    public void ThePlayersObjectEnchantmentsFollowTheSpellbook()
    {
        var runtime = GameRuntimeTestFactory.Create();
        var automation = new FakeRynthAutomation();
        PopulateLikeRuntimeRynthGameViewTests(runtime, automation);
        var live = new RuntimeRynthGameView(runtime, automation: automation);
        using var builder = new RynthSnapshotBuilder(live, runtime, screen: null);
        var queue = new RynthActionQueue(_ => { }, () => false);
        builder.Build();

        double clock = runtime.EffectClock.NowSeconds;
        runtime.CharacterOwner.Spellbook.OnEnchantmentAdded(
            new ActiveEnchantmentRecord(0x1500, 1, -1, Player, Bucket: 2, StartTime: clock));
        var snap = new SnapshotRynthGameView(builder.Build(), queue, live, _ => { });

        Assert.Contains(snap.GetObjectEnchantments(Player)!, e => e.SpellId == 0x1500);
        Assert.Equal(live.GetObjectEnchantments(Player), snap.GetObjectEnchantments(Player));
        Assert.Null(snap.GetObjectEnchantments(Vendor));
    }

    private static void PopulateLikeRuntimeRynthGameViewTests(AcDream.Runtime.GameRuntime runtime, FakeRynthAutomation automation)
    {
        // TheOpenVendorComesFromTheVendorStateWithRynthCoresCounts: player with coins, packs, stacks, an open vendor.
        runtime.PlayerIdentity.ServerGuid = Player;
        var player = new ClientObject { ObjectId = Player, Name = "Sammy", Type = ItemType.Creature };
        player.Properties.Ints[20] = 1_234;
        player.Properties.Int64s[1] = 42;
        runtime.InventoryOwner.Objects.AddOrUpdate(player);
        runtime.InventoryOwner.Objects.AddOrUpdate(new ClientObject
        {
            ObjectId = Vendor, Name = "Thorsten", Type = ItemType.Creature, PublicWeenieBitfield = 0x10,
        });
        runtime.InventoryOwner.Objects.AddOrUpdate(
            new ClientObject { ObjectId = 0x80000110, ContainerId = Player, ItemsCapacity = 24 });
        runtime.InventoryOwner.Objects.AddOrUpdate(
            new ClientObject { ObjectId = 0x80000111, ContainerId = Player, WeenieClassId = 300, StackSize = 7 });
        runtime.InventoryOwner.Objects.AddOrUpdate(
            new ClientObject { ObjectId = 0x80000112, ContainerId = 0x80000110, WeenieClassId = 300, StackSize = 5, Name = "Token" });
        runtime.InventoryOwner.Objects.ReplaceContents(Player, new uint[] { 0x80000110, 0x80000111 });
        runtime.InventoryOwner.Objects.ReplaceContents(0x80000110, new uint[] { 0x80000112 });
        runtime.InventoryOwner.Vendor.Apply(
            Vendor,
            new VendorShopProfile(0x1u, 0u, uint.MaxValue, true, 0.9f, 1.3f, 300u, 12u, "Tokens"),
            [
                new VendorShopItem(0x80000701, 5, 30, "Sword", 1u, 0x06001234, 100, Burden: 200),
                new VendorShopItem(0x80000702, -1, 31, "Gem", 0x800u, 0x06005678, 50, DescStackSize: 5,
                    MaxStackSize: 100),
                new VendorShopItem(0x80000703, 1, 32, "Pack", 0x200u, 0x06009999, 10, ItemsCapacity: 24),
            ]);

        // ThePlayerPoseIsTheEntitysBodyCellPosition: the player and the vendor have placed bodies.
        RuntimeEntityRecord me = runtime.EntityObjects.Entities.AddActive(Spawn(Player));
        var body = new PhysicsBody { Orientation = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, 0.5f) };
        body.SnapToCell(0x0101001Au, new Vector3(10, 20, 30), new Vector3(1, 2, 3));
        me.SetPhysicsBody(body);
        RuntimeEntityRecord vendor = runtime.EntityObjects.Entities.AddActive(Spawn(Vendor));
        var vendorBody = new PhysicsBody { Orientation = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, 1.25f) };
        vendorBody.SnapToCell(0x0101001Bu, new Vector3(12, 22, 30), new Vector3(4, 5, 6));
        vendor.SetPhysicsBody(vendorBody);
        // PaletteOffsetsAreColourIndexesInOffsetOrder.
        vendor.Snapshot = vendor.Snapshot with
        {
            SubPalettes =
            [
                new CreateObject.SubPaletteSwap(0x04001234, 3, 1),
                new CreateObject.SubPaletteSwap(0x04005678, 0, 2),
            ],
        };
        // DoorStateComesFromTheEntityRecord: a door with no body, its last motion On.
        RuntimeEntityRecord door = runtime.EntityObjects.Entities.AddActive(Spawn(Door));
        door.Snapshot = door.Snapshot with { MotionState = new CreateObject.ServerMotionState(Stance: 0x3D, ForwardCommand: 0x000B) };

        // StateComesFromTheSurfaceInRynthCoresUnits / TryGetVitalBase / Attributes / Heading tests.
        automation.CombatArea.Snapshot = default(PluginCombatSnapshot) with { Mode = PluginCombatMode.Missile };
        automation.CharacterArea.MaxHealth = 100;
        automation.CharacterArea.CurrentHealth = 40;
        automation.CharacterArea.MaxStamina = 80;
        automation.CharacterArea.CurrentStamina = 70;
        automation.CharacterArea.BaseHealth = 90;
        automation.CharacterArea.BaseStamina = 95;
        automation.CharacterArea.BaseMana = 99;
        automation.CharacterArea.SkillList.Add(new PluginSkillInfo(6, "Melee Defense", PluginSkillTraining.Specialized, 250) { Base = 200 });
        automation.CharacterArea.AttributeList.Add(new PluginAttributeInfo(0, "Strength", 120) { Base = 100 });
        automation.CharacterArea.AttributeList.Add(new PluginAttributeInfo(1, "Endurance", 90));
        automation.CharacterArea.VitalList.Add(new PluginVitalInfo(0, "Health", 10, 100) { Base = 90 });
        automation.CharacterArea.VitalList.Add(new PluginVitalInfo(1, "Stamina", 10, 110) { Base = 95 });
        automation.CharacterArea.VitalList.Add(new PluginVitalInfo(2, "Mana", 10, 120) { Base = 99 });
        automation.RecoveryArea.Busy = 3;
        automation.ObjectArea.OpenContainer = 0x80000044;
        // The real surface answers IsKnown from the runtime's spellbook; the fake is kept in step.
        foreach (uint spell in new uint[] { 30, 5, 17, 2 })
        {
            runtime.CharacterOwner.Spellbook.OnSpellLearned(spell);
            automation.SpellArea.Known.Add(spell);
        }
        runtime.InventoryOwner.ServerReplies.RecordUseDone(0x36);
        var position = new PluginNavigationPosition(0x0101001A, 1, 2, 3, 123.5f, true);
        automation.NavigationArea.Snapshot = default(PluginNavigationSnapshot) with { IsAvailable = true, Position = position };
        automation.ObjectArea.Known[Vendor] = new PluginWorldObject(Vendor, 1, "Thorsten", PluginObjectClass.Unknown, 0, 0, 0)
        { HasPosition = true, Position = position with { HeadingDegrees = 45f } };

        // PlayerEnchantmentsComeFromTheFullRegistryAsRynthCoreReadsThem (untimed, so no clock jitter).
        double clock = runtime.EffectClock.NowSeconds;
        runtime.CharacterOwner.Spellbook.OnEnchantmentAdded(new ActiveEnchantmentRecord(0x1300, 1, -1, Player, Bucket: 2, StartTime: clock));
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
}
