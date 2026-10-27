using System.Collections.Immutable;
using System.Numerics;
using AcDream.App.Input;
using AcDream.App.Physics;
using AcDream.App.Rendering;
using AcDream.App.Rendering.Scene;
using AcDream.App.Rendering.Vfx;
using AcDream.App.Rendering.Wb;
using AcDream.App.Streaming;
using AcDream.App.World;
using AcDream.Core.Items;
using AcDream.Core.Lighting;
using AcDream.Core.Net;
using AcDream.Core.Net.Messages;
using AcDream.Core.Physics;
using AcDream.Core.Plugins;
using AcDream.Core.Rendering;
using AcDream.Core.Rendering.Wb;
using AcDream.Core.Terrain;
using AcDream.Core.Vfx;
using AcDream.Core.World;
using AcDream.Runtime;
using AcDream.Runtime.Entities;
using AcDream.Runtime.Physics;
using AcDream.Runtime.World;
using DatReaderWriter.DBObjs;
using DatReaderWriter.Types;

namespace AcDream.App.Tests.Streaming;

public sealed class LandblockPlacementPublicationCompositionTests
{
    private const uint LandblockId = 0x3031FFFFu;
    private const uint CellId = 0x30310014u;
    private const uint PlayerGuid = 0x5000000Au;

    [Fact]
    public void PresentationFence_IsExactLandblockAndStopsBeforeLiveRecovery()
    {
        const uint unrelated = 0x4040FFFFu;
        using (var fixture = new Fixture())
        {
            RuntimePlacementProjectionToken seeded =
                fixture.SeedResident(PlayerGuid);
            Assert.True(fixture.Lifetime.Physics.SetPosition.AcknowledgeProjection(
                seeded));
            using var subscription = fixture.Subscribe(PlayerGuid);
            fixture.State.AddLandblock(new LoadedLandblock(
                unrelated,
                new LandBlock(),
                Array.Empty<WorldEntity>()));
            var window = new GraphicalRemotePlacementServiceWindow(
                fixture.State,
                fixture.Controller.IsLandblockPresentationReady,
                static _ => true);

            // The fence is the exact landblock: another one being drawn
            // does not open it.
            Assert.False(window.IsWithinServiceWindow(CellId));
            Assert.True(window.IsWithinServiceWindow(unrelated));

            fixture.Pipeline.PublishLoaded(fixture.Result());
            Assert.True(window.IsWithinServiceWindow(CellId));
        }

        bool failRecovery = true;
        using (var fixture = new Fixture(onLandblockLoaded: _ =>
        {
            if (failRecovery)
            {
                failRecovery = false;
                throw new InvalidOperationException("injected recovery failure");
            }
        }))
        {
            LandblockStreamResult.Loaded result = fixture.Result();
            Assert.Throws<InvalidOperationException>(() =>
                fixture.Pipeline.PublishLoaded(result));
            Assert.True(fixture.Pipeline.HasPendingPublication(result));
            Assert.True(fixture.Controller.IsLandblockPresentationReady(
                LandblockId));
            Assert.True(fixture.Controller.IsRenderNeighborhoodResident(
                LandblockId,
                nearRadius: 0,
                farRadius: 0));

            fixture.Pipeline.ResumePublication(result);
            Assert.False(fixture.Pipeline.HasPendingPublication(result));
        }
    }

    [Fact]
    public void DebtFreeFullRetirementsDrainMultipleRealOwnersInOneBudgetedFrame()
    {
        uint[] landblockIds =
        [
            0x3031FFFFu,
            0x3032FFFFu,
            0x3033FFFFu,
        ];
        var removalOrder = new List<uint>();
        using var fixture = new Fixture(removeTerrain: removalOrder.Add);
        foreach (uint landblockId in landblockIds)
        {
            fixture.AddDebtFreeLandblock(landblockId);
            fixture.Pipeline.EnqueueFullRetirement(landblockId);
        }
        Assert.Equal(landblockIds.Length, fixture.Pipeline.PendingRetirementCount);

        var meter = new StreamingWorkMeter(RetirementBudget(
            maxEntityOperations: 64,
            maxGlRetireOperations: 16));
        fixture.Pipeline.AdvanceRetirements(meter);
        meter.FinishFrame();

        Assert.Equal(0, fixture.Pipeline.PendingRetirementCount);
        Assert.Equal(landblockIds, removalOrder);
        Assert.Equal(removalOrder.Count, removalOrder.Distinct().Count());
        // Ten stages per landblock: the drawn ones only, no collision.
        Assert.Equal(30, meter.Snapshot.Operations);
        Assert.Equal(30, meter.Snapshot.CompletedOperations);
        Assert.Equal(0, meter.Snapshot.YieldCount);
        Assert.All(landblockIds, landblockId =>
        {
            Assert.False(fixture.State.IsLoaded(landblockId));
            // Retiring what is drawn leaves the session's collision alone.
            Assert.True(fixture.Lifetime.Physics.Engine
                .IsLandblockTerrainResident(landblockId));
            Assert.False(fixture.Pipeline.IsRetirementPending(landblockId));
        });
        RuntimePhysicsOwnershipSnapshot ownership =
            fixture.Lifetime.Physics.CaptureOwnership();
        Assert.Equal(0, ownership.CollisionPrefixMutationCount);
        Assert.Equal(0, ownership.CollisionPrefixQuiescenceCount);
        Assert.Equal(0, ownership.PendingCollisionPrefixProjectionCount);
    }

    [Fact]
    public void DebtFreeFullRetirementsRespectSmallMeterAndResumeExactlyOnce()
    {
        uint[] landblockIds =
        [
            0x3031FFFFu,
            0x3032FFFFu,
            0x3033FFFFu,
        ];
        using var fixture = new Fixture();
        foreach (uint landblockId in landblockIds)
        {
            fixture.AddDebtFreeLandblock(landblockId);
            fixture.Pipeline.EnqueueFullRetirement(landblockId);
        }

        int frames = 0;
        while (fixture.Pipeline.PendingRetirementCount != 0 && frames++ < 8)
        {
            var meter = new StreamingWorkMeter(RetirementBudget(
                maxEntityOperations: 7,
                maxGlRetireOperations: 4));
            fixture.Pipeline.AdvanceRetirements(meter);
            meter.FinishFrame();
            Assert.InRange(meter.Snapshot.Used.EntityOperations, 1, 7);
            Assert.InRange(meter.Snapshot.Used.GlRetireOperations, 0, 4);
            Assert.Equal(meter.Snapshot.Operations,
                meter.Snapshot.CompletedOperations);
        }

        Assert.Equal(5, frames);
        Assert.Equal(0, fixture.Pipeline.PendingRetirementCount);
        Assert.All(landblockIds, landblockId =>
            Assert.True(fixture.Lifetime.Physics.Engine
                .IsLandblockTerrainResident(landblockId)));
    }

    private static StreamingWorkBudget RetirementBudget(
        int maxEntityOperations,
        int maxGlRetireOperations) => new(
        TimeSpan.FromSeconds(10),
        maxCompletionAdmissions: 64,
        maxAdoptedCpuBytes: 1_000_000,
        maxEntityOperations,
        maxGpuUploadBytes: 1_000_000,
        maxGlRetireOperations,
        destinationReserveFraction: 0.75f);

    private sealed class Fixture : IDisposable
    {
        internal Fixture(
            IRenderStaticProjectionJournalSink? staticProjectionSink = null,
            Action<uint>? onLandblockLoaded = null,
            Action<uint>? removeTerrain = null)
        {
            Lifetime = new RuntimeEntityObjectLifetime(new PhysicsDataCache());
            Lifetime.BindEventContext(
                static () => new RuntimeGenerationToken(1UL),
                static () => 1UL);
            RuntimePhysicsState physics = Lifetime.Physics;
            physics.SetPosition.BeginCollisionGeneration(LandblockId, 1UL);
            physics.Engine.AddLandblock(
                LandblockId,
                new TerrainSurface(new byte[81], new float[256]),
                Array.Empty<CellSurface>(),
                Array.Empty<PortalPlane>(),
                worldOffsetX: 0f,
                worldOffsetY: 0f);
            physics.SetPosition.CommitCollisionGeneration(
                LandblockId,
                1UL,
                ready: true);
            physics.ObserveLocalWorldFrame(CellId, teleportAdvanced: false);

            Meshes = new CountingMeshAdapter();
            State = new GpuWorldState(new LandblockSpawnAdapter(Meshes));
            Runtime = new LiveEntityRuntime(State, new NoopResources(), Lifetime);

            var render = new LandblockRenderPublisher(
                static (_, _, _) => { },
                removeTerrain ?? (static _ => { }),
                new CellVisibility(),
                State);
            var lighting = new LightingHookSink(
                new LightManager(),
                new NullPoseSource());
            var translucency = new TranslucencyFadeManager();
            var staticPublisher = new LandblockStaticPresentationPublisher(
                lighting,
                translucency,
                new WorldGameState());
            var retirement = new LandblockPresentationRetirementOwner(
                render,
                staticPublisher,
                lighting,
                translucency);
            Pipeline = new LandblockPresentationPipeline(
                render,
                staticPublisher,
                State,
                retirement,
                onLandblockLoaded,
                ensureEnvCellMeshes: null,
                staticProjectionSink: staticProjectionSink);
            Controller = new StreamingController(
                enqueueLoad: static (_, _, _) => { },
                enqueueUnload: static (_, _) => { },
                drainCompletions: static _ =>
                    Array.Empty<LandblockStreamResult>(),
                State,
                nearRadius: 0,
                farRadius: 0,
                Pipeline);
        }

        internal RuntimeEntityObjectLifetime Lifetime { get; }
        internal CountingMeshAdapter Meshes { get; }
        internal GpuWorldState State { get; }
        internal LiveEntityRuntime Runtime { get; }
        internal LandblockPresentationPipeline Pipeline { get; }
        internal StreamingController Controller { get; }

        internal void AddCollisionLandblock(uint landblockId)
        {
            RuntimePhysicsState physics = Lifetime.Physics;
            physics.SetPosition.BeginCollisionGeneration(landblockId, 1UL);
            physics.Engine.AddLandblock(
                landblockId,
                new TerrainSurface(new byte[81], new float[256]),
                Array.Empty<CellSurface>(),
                Array.Empty<PortalPlane>(),
                worldOffsetX: 192f,
                worldOffsetY: 0f);
            physics.SetPosition.CommitCollisionGeneration(
                landblockId,
                1UL,
                ready: true);
        }

        internal void AddDebtFreeLandblock(uint landblockId)
        {
            if (!Lifetime.Physics.Engine
                .IsLandblockTerrainResident(landblockId))
            {
                AddCollisionLandblock(landblockId);
            }
            if (!State.IsLoaded(landblockId))
            {
                State.AddLandblock(new LoadedLandblock(
                    landblockId,
                    new LandBlock
                    {
                        Terrain = new TerrainInfo[81],
                        Height = new byte[81],
                    },
                    Array.Empty<WorldEntity>()));
            }
        }

        internal RuntimePlacementProjectionToken SeedResident(
            uint guid,
            bool isLocalPlayer = true,
            uint cellId = CellId,
            Vector3? position = null)
        {
            Vector3 resolvedPosition = position ?? new Vector3(60f, 84f, 0f);
            LiveEntityRecord record = Runtime.RegisterAndMaterializeProjection(
                Spawn(guid, cellId, resolvedPosition),
                isLocalPlayer: isLocalPlayer);
            Assert.False(Runtime.HasActiveInitialCreateResidence(record.Canonical));
            var body = new PhysicsBody
            {
                Position = resolvedPosition,
                Orientation = Quaternion.Identity,
                LastUpdateTime = 1d,
                State = PhysicsStateFlags.Gravity,
                TransientState = TransientStateFlags.Active,
            };
            body.SnapToCell(cellId, body.Position, body.Position);
            Lifetime.Entities.SetFinalPhysicsState(
                record.Canonical,
                PhysicsStateFlags.Gravity);
            Lifetime.Entities.SetFullCell(
                record.Canonical,
                cellId,
                (cellId & 0xFFFF0000u) | 0xFFFFu);
            Lifetime.Entities.SetPhysicsBody(record.Canonical, body);
            record.Canonical.ObjectClock.Activate();
            Lifetime.Physics.AcknowledgeSpatialProjection(
                record.Canonical,
                spatial: true);

            var request = new PhysicsSetPositionRequest(
                body.Position,
                body.Orientation,
                cellId,
                body.CellPosition.Frame.Origin,
                [new FlatCollisionSphere(Vector3.Zero, 0.48f)],
                1f,
                0.4f,
                0.4f,
                Flags: PhysicsSetPositionFlags.Placement
                    | PhysicsSetPositionFlags.Slide,
                CurrentCellId: cellId);
            RuntimeSetPositionOutcome placed = Lifetime.Physics.SetPosition.Apply(
                record.Canonical,
                record.Canonical.PositionAuthorityVersion,
                new RuntimeSetPositionCommand(
                    request,
                    RuntimeSetPositionOperationKind.LocalAuthoritative,
                    1d,
                    record.Canonical.VelocityAuthorityVersion));
            Assert.Equal(
                RuntimeSetPositionStatus.CommittedHostAcknowledgementPending,
                placed.Status);
            return placed.Projection;
        }

        internal RuntimePlacementProjectionSubscription Subscribe(uint guid)
        {
            return new RuntimePlacementProjectionSubscription(
                Lifetime.Placements,
                static () => new RuntimeGenerationToken(1UL),
                CreatePlacementSink(guid));
        }

        private IRuntimePlacementProjectionSink CreatePlacementSink(uint guid)
        {
            var origin = new LiveWorldOriginState();
            origin.SetPlaceholder(0, 0);
            return new RuntimePlacementPresentationSink(
                Runtime,
                new RuntimeWorldTransitState(),
                new EntityEffectPoseRegistry(),
                new LocalPlayerShadowSynchronizer(
                    Lifetime.Physics.Engine,
                    Runtime,
                    origin,
                    new LocalPlayerShadowState()),
                () => guid);
        }

        internal LandblockStreamResult.Loaded Result()
        {
            var landblock = new LoadedLandblock(
                LandblockId,
                new LandBlock
                {
                    Terrain = new TerrainInfo[81],
                    Height = new byte[81],
                },
                Array.Empty<WorldEntity>(),
                PhysicsDatBundle.Empty);
            var shell = new EnvCellShellPlacement(
                CellId: 0x30310100u,
                GeometryId: 0x2_0000_0474UL,
                EnvironmentId: 0x0D000001u,
                CellStructure: 1,
                Surfaces: ImmutableArray<ushort>.Empty,
                WorldPosition: Vector3.Zero,
                Rotation: Quaternion.Identity,
                Transform: Matrix4x4.Identity,
                LocalBounds: new WbBoundingBox(Vector3.Zero, Vector3.One),
                WorldBounds: new WbBoundingBox(Vector3.Zero, Vector3.One));
            var build = new LandblockBuild(
                landblock,
                new EnvCellLandblockBuild(
                    LandblockId,
                    Array.Empty<LoadedCell>(),
                    [shell]),
                Origin: new LandblockBuildOrigin(0x30, 0x31));
            return new LandblockStreamResult.Loaded(
                LandblockId,
                LandblockStreamTier.Near,
                build,
                new LandblockMeshData(
                    Array.Empty<TerrainVertex>(),
                    Array.Empty<uint>()));
        }

        public void Dispose()
        {
            Runtime.Clear();
            Lifetime.Dispose();
        }
    }

    private static WorldSession.EntitySpawn Spawn(
        uint guid,
        uint cellId = CellId,
        Vector3? worldPosition = null)
    {
        Vector3 positionValue = worldPosition ?? new Vector3(60f, 84f, 0f);
        var position = new CreateObject.ServerPosition(
            cellId,
            positionValue.X,
            positionValue.Y,
            positionValue.Z,
            1f,
            0f,
            0f,
            0f);
        var timestamps = new PhysicsTimestamps(
            Position: 1,
            Movement: 1,
            State: 1,
            Vector: 1,
            Teleport: 0,
            ServerControlledMove: 1,
            ForcePosition: 0,
            ObjDesc: 1,
            Instance: 1);
        var physics = new PhysicsSpawnData(
            RawState: (uint)PhysicsStateFlags.Gravity,
            Position: position,
            Movement: null,
            AnimationFrame: null,
            SetupTableId: 0x02000001u,
            MotionTableId: 0x09000001u,
            SoundTableId: null,
            PhysicsScriptTableId: null,
            Parent: null,
            Children: null,
            Scale: null,
            Friction: null,
            Elasticity: null,
            Translucency: null,
            Velocity: null,
            Acceleration: null,
            AngularVelocity: null,
            DefaultScriptType: null,
            DefaultScriptIntensity: null,
            Timestamps: timestamps);
        return new WorldSession.EntitySpawn(
            guid,
            position,
            0x02000001u,
            [],
            [],
            [],
            null,
            null,
            "resident",
            (uint)ItemType.Creature,
            null,
            0x09000001u,
            PhysicsState: (uint)PhysicsStateFlags.Gravity,
            InstanceSequence: 1,
            MovementSequence: 1,
            ServerControlSequence: 1,
            PositionSequence: 1,
            Physics: physics);
    }

    private sealed class NoopResources : ILiveEntityResourceLifecycle
    {
        public void Register(WorldEntity entity) { }
        public void Unregister(WorldEntity entity) { }
    }

    private sealed class PlacementRecorder(
        Action<RuntimePlacementProjectionSnapshot>? onPlacement = null)
        : IRuntimePlacementObserver
    {
        internal List<RuntimePlacementProjectionSnapshot> Placements { get; } = [];

        public void OnPlacement(in RuntimePlacementDelta delta)
        {
            Placements.Add(delta.Placement);
            onPlacement?.Invoke(delta.Placement);
        }
    }

    private sealed class FailFirstStaticProjectionSink
        : IRenderStaticProjectionJournalSink
    {
        internal int Attempts { get; private set; }

        public void Reconcile(
            LandblockBuild build,
            GpuLandblockSpatialPublication publication)
        {
            Attempts++;
            if (Attempts == 1)
                throw new InvalidOperationException("injected projection failure");
        }

        public void Retire(GpuLandblockRetirement retirement) { }
    }

    internal sealed class CountingMeshAdapter : IWbMeshAdapter
    {
        internal int ReferenceCount { get; private set; }

        public void IncrementRefCount(ulong id) => ReferenceCount++;
        public void PinPreparedRenderData(ulong id) => ReferenceCount++;
        public void DecrementRefCount(ulong id) => ReferenceCount--;
    }

    private sealed class NullPoseSource : IEntityEffectPoseSource
    {
        public bool TryGetRootPose(uint localEntityId, out Matrix4x4 rootWorld)
        {
            rootWorld = default;
            return false;
        }

        public bool TryGetPartPose(
            uint localEntityId,
            int partIndex,
            out Matrix4x4 partLocal)
        {
            partLocal = default;
            return false;
        }
    }
}
