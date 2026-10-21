using System.Diagnostics;
using System.Numerics;
using AcDream.ClientExtensions;
using AcDream.Core.Items;
using AcDream.Core.Net;
using AcDream.Core.Net.Messages;
using AcDream.Core.Physics;
using AcDream.Plugin.Abstractions;
using AcDream.Runtime.Entities;
using AcDream.Rynth.Host;

namespace AcDream.App.Tests.Extensions;

[Collection("Rynth")]
public sealed class RynthSnapshotBuilderTests
{
    private static (AcDream.Runtime.GameRuntime Runtime, RuntimeRynthGameView Live, RynthSnapshotBuilder Builder) Make(
        IClientScreen? screen = null)
    {
        var runtime = GameRuntimeTestFactory.Create();
        runtime.PlayerIdentity.ServerGuid = 0x50000001;
        runtime.InventoryOwner.Objects.AddOrUpdate(new ClientObject { ObjectId = 0x50000001, Name = "Sammy" });
        var live = new RuntimeRynthGameView(runtime, screen: screen);
        return (runtime, live, new RynthSnapshotBuilder(live, runtime, screen));
    }

    [Fact]
    public void ScalarsComeFromTheLiveView()
    {
        var (_, live, builder) = Make();
        RynthWorldSnapshot s = builder.Build();
        Assert.Equal(live.PlayerId, s.PlayerId);
        Assert.Equal(live.SelectedObjectId, s.SelectedObjectId);
        Assert.Equal(live.IsPortaling, s.IsPortaling);
        Assert.Equal(live.BusyCount, s.BusyCount);
        Assert.Equal(live.CombatMode, s.CombatMode);
        Assert.Equal(live.AutoRunActive, s.AutoRunActive);
        Assert.Equal(live.OpenContainerId, s.OpenContainerId);
        Assert.Equal(live.AccountName, s.AccountName);
        Assert.Equal(live.WorldName, s.WorldName);
        Assert.Equal(live.ServerReplies, s.ServerReplies);
        Assert.Equal(live.CurrentMovementIntent, s.MovementIntent);
        Assert.Equal(live.IsAttackInProgress, s.IsAttackInProgress);
        Assert.Equal(live.IsCastGestureActive, s.IsCastGestureActive);
        Assert.Equal(live.TryGetPendingItemRequest(out uint acType, out uint pendingId), s.HasPendingItemRequest);
        Assert.Equal((acType, pendingId), (s.PendingAcType, s.PendingObjectId));
        Assert.Equal(live.TryGetPlayerHeading(out float heading), s.HasHeading);
        Assert.Equal(heading, s.Heading);
        Assert.False(s.Dead);   // no vitals readable
        Assert.Equal(1, s.Sequence);
        Assert.Equal(2, builder.Build().Sequence);
    }

    [Fact]
    public void DeadIsReadableVitalsWithNoHealth()
    {
        var runtime = GameRuntimeTestFactory.Create();
        runtime.PlayerIdentity.ServerGuid = 0x50000001;
        var automation = new FakeRynthAutomation();
        automation.CharacterArea.MaxHealth = 100;
        automation.CharacterArea.CurrentHealth = 40;
        var builder = new RynthSnapshotBuilder(new RuntimeRynthGameView(runtime, automation: automation), runtime, screen: null);
        Assert.False(builder.Build().Dead);

        automation.CharacterArea.CurrentHealth = 0;
        Assert.True(builder.Build().Dead);

        automation.Available = false;   // vitals not readable
        Assert.False(builder.Build().Dead);
    }

    [Fact]
    public void EntitiesHoldWhatTheLiveViewReads()
    {
        var (runtime, live, builder) = Make();
        RuntimeEntityRecord player = runtime.EntityObjects.Entities.AddActive(Spawn(0x50000001));
        var body = new PhysicsBody { Orientation = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, 0.5f) };
        body.SnapToCell(0x0101001Au, new Vector3(10, 20, 30), new Vector3(1, 2, 3));
        player.SetPhysicsBody(body);
        RuntimeEntityRecord door = runtime.EntityObjects.Entities.AddActive(Spawn(0x7A9B4001));
        door.Snapshot = door.Snapshot with
        {
            MotionState = new CreateObject.ServerMotionState(Stance: 0x3D, ForwardCommand: 0x000B),
            SubPalettes = [new CreateObject.SubPaletteSwap(0x04001234, 3, 1)],
        };
        var doorBody = new PhysicsBody();
        doorBody.SnapToCell(0xA9B40001u, Vector3.Zero, new Vector3(4, 5, 6));
        door.SetPhysicsBody(doorBody);
        runtime.EntityObjects.Entities.AddActive(Spawn(0x80000099));   // no body: no pose, as the live view

        RynthWorldSnapshot s = builder.Build();

        Assert.Equal(2, s.Entities.Count);
        Assert.True(live.TryGetPlayerPose(out uint cell, out Vector3 origin, out Quaternion orientation));
        RynthEntityPose pose = s.Entities[0x50000001];
        Assert.Equal((cell, origin, orientation), (pose.CellId, pose.Origin, pose.Orientation));
        Assert.True(live.TryGetObjectPosition(0x7A9B4001, out uint doorCell, out Vector3 doorOrigin));
        RynthEntityPose doorPose = s.Entities[0x7A9B4001];
        Assert.Equal((doorCell, doorOrigin), (doorPose.CellId, doorPose.Origin));
        Assert.True(live.TryGetObjectState(0x7A9B4001, out uint state));
        Assert.Equal(state, doorPose.RawState);
        Assert.True(doorPose.MotionOn);
        Assert.Equal(live.GetPalettes(0x7A9B4001), doorPose.Palettes);
        Assert.Null(pose.MotionOn);
        Assert.False(s.Entities.ContainsKey(0x80000099));
        Assert.False(live.TryGetObjectPosition(0x80000099, out _, out _));
    }

    [Fact]
    public void CharacterIsSharedUntilItsRevisionMoves()
    {
        var (runtime, _, builder) = Make();
        RynthWorldSnapshot a = builder.Build();
        RynthWorldSnapshot b = builder.Build();
        Assert.Same(a.Character, b.Character);
        runtime.CharacterOwner.LocalPlayer.OnVitalUpdate(vitalId: 1u, ranks: 0u, start: 10u, xp: 0u, current: 10u);
        RynthWorldSnapshot c = builder.Build();
        Assert.NotSame(b.Character, c.Character);
        Assert.Same(c.Character, builder.Build().Character);

        runtime.PlayerIdentity.ServerGuid = 0x50000002;
        Assert.NotSame(c.Character, builder.Build().Character);
    }

    [Fact]
    public void SpellbookIsSharedUntilItsRevisionMoves()
    {
        var (runtime, _, builder) = Make();
        RynthWorldSnapshot a = builder.Build();
        Assert.Same(a.Spellbook, builder.Build().Spellbook);
        runtime.CharacterOwner.Spellbook.OnSpellLearned(1);
        RynthWorldSnapshot c = builder.Build();
        Assert.NotSame(a.Spellbook, c.Spellbook);
        Assert.Same(c.Spellbook, builder.Build().Spellbook);
    }

    [Fact]
    public void SpellbookIsRefreshedEachSecondWithoutARevision()
    {
        var (_, _, builder) = Make();
        RynthWorldSnapshot a = builder.Build();
        Thread.Sleep(1100);
        Assert.NotSame(a.Spellbook, builder.Build().Spellbook);
    }

    [Fact]
    public void ServerTimeKeepsRunningAfterThePublish()
    {
        var (_, live, builder) = Make();
        RynthWorldSnapshot s = builder.Build();
        long later = s.PublishedAt + Stopwatch.Frequency * 2;
        Assert.Equal(s.ServerTime + 2.0, s.ServerTimeAt(later), 6);
    }

    [Fact]
    public void VendorIsRebuiltOnlyWhenItsGenerationOrTheObjectTableMoves()
    {
        var (runtime, _, builder) = Make();
        uint generation = 0;
        builder.RynthVendorGeneration = () => generation;
        runtime.InventoryOwner.Objects.AddOrUpdate(new ClientObject { ObjectId = 0x80000700, Name = "Thorsten" });
        Assert.Null(builder.Build().Vendor);

        runtime.InventoryOwner.Vendor.Apply(
            0x80000700,
            new VendorShopProfile(0x1u, 0u, uint.MaxValue, true, 0.9f, 1.3f, 0u, 0u, ""),
            [new VendorShopItem(0x80000701, 5, 30, "Sword", 1u, 0x06001234, 100, Burden: 200)]);
        generation++;
        RynthVendor vendor = Assert.IsType<RynthVendor>(builder.Build().Vendor);
        Assert.Equal(0x80000700u, vendor.VendorId);
        Assert.Same(vendor, builder.Build().Vendor);   // nothing it watches moved

        generation++;
        RynthVendor regenerated = Assert.IsType<RynthVendor>(builder.Build().Vendor);
        Assert.NotSame(vendor, regenerated);
        Assert.Same(regenerated, builder.Build().Vendor);

        runtime.InventoryOwner.Objects.AddOrUpdate(new ClientObject { ObjectId = 0x80000800, Name = "Coin" });
        Assert.NotSame(regenerated, builder.Build().Vendor);
    }

    [Fact]
    public void TheScreenIsCapturedWhenPresent()
    {
        Assert.Null(Make().Builder.Build().Projection);

        var screen = new CapturingScreen();
        var (_, live, builder) = Make(screen);
        RynthWorldSnapshot s = builder.Build();
        Assert.Same(screen.Projection, s.Projection);
        Assert.True(live.TryGetViewportSize(out uint w, out uint h));
        Assert.Equal((true, w, h), (s.HasViewport, s.ViewportWidth, s.ViewportHeight));
        Assert.True(live.TryGetRadarRect(out int x0, out int y0, out int x1, out int y1));
        Assert.Equal((true, (x0, y0, x1, y1)), (s.HasRadar, s.Radar));
    }

    private sealed class CapturingScreen : IClientScreen
    {
        public readonly ClientScreenProjection Projection = new(Matrix4x4.Identity, new Vector2(1280, 720), 0, 0);
        public bool TryGetViewportSize(out Vector2 size) { size = new Vector2(1280, 720); return true; }
        public bool TryProject(PluginNavigationPosition position, out Vector2 screen) { screen = default; return false; }
        public bool TryCaptureProjection(out ClientScreenProjection projection) { projection = Projection; return true; }
        public bool TryGetRadarDisc(out Vector2 centre, out float radius)
        {
            (centre, radius) = (new Vector2(100, 50), 50f);
            return true;
        }
    }

    [Fact]
    public void ObjectsHoldThePlayerObject()
    {
        var (_, live, builder) = Make();
        RynthWorldSnapshot s = builder.Build();
        Assert.True(s.Objects.TryGet(live.PlayerId, out RynthObjectEntry player));
        Assert.Equal("Sammy", player.Name);
    }

    [Fact]
    public void DisposeStopsTheObjectMirrorListening()
    {
        var (runtime, live, builder) = Make();
        builder.Build().Objects.TryGet(live.PlayerId, out RynthObjectEntry before);
        builder.Dispose();
        runtime.InventoryOwner.Objects.AddOrUpdate(new ClientObject { ObjectId = 0x50000001, Name = "Sammy II" });
        // No longer subscribed: the update marks nothing, so the entry is the one copied before.
        builder.Build().Objects.TryGet(live.PlayerId, out RynthObjectEntry after);
        Assert.Same(before, after);
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
