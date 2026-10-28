using System.Collections.Immutable;
using System.Numerics;
using AcDream.Content;
using AcDream.Core.Net.Messages;
using AcDream.Core.Physics;
using AcDream.Runtime.Entities;
using AcDream.Runtime.Physics;
using AcDream.Core.World;
using AcDream.Runtime.Tests.Gameplay;
using AcDream.Runtime.World;
using DatReaderWriter.DBObjs;
using DatReaderWriter.Types;

namespace AcDream.Runtime.Tests.World;

/// <summary>
/// The collision a client publishes around its character is measured in the
/// one frame the runtime measures every position in: the landblock of the
/// character's arrival. Both clients share this neighborhood, so a placement
/// the runtime makes lands on the collision this publishes.
/// </summary>
public sealed class RuntimeCollisionNeighborhoodTests
{
    private const uint Arrival = 0x282A0001u;
    private const uint East = 0x292A0001u;

    [Fact]
    public void ATeleportIntoTheLandblockWalkedIntoMeasuresCollisionFromTheNewFrame()
    {
        using GameRuntime runtime = NewRuntime();
        var neighborhood = new RuntimeCollisionNeighborhood(
            runtime,
            new FlatContent());
        runtime.EntityObjects.Physics.ObserveLocalPlayerCreate(Arrival);
        neighborhood.CenterOn(Arrival);
        Drive(neighborhood, Arrival);

        // Walk east into the next landblock: the frame stays at the arrival.
        neighborhood.Follow(East);
        Drive(neighborhood, East);
        Assert.Equal(new Vector3(192f, 0f, 0f), OffsetOf(runtime, East));

        // The server then teleports the character within that landblock;
        // the runtime measures from the destination's landblock from now on.
        runtime.EntityObjects.Physics.ObserveLocalWorldFrame(
            0x292A0010u,
            teleportAdvanced: true);
        neighborhood.CenterOn(0x292A0010u);
        Drive(neighborhood, 0x292A0010u);

        Assert.Equal(Vector3.Zero, OffsetOf(runtime, East));
        Assert.Equal(new Vector3(-192f, 0f, 0f), OffsetOf(runtime, Arrival));
    }

    [Fact]
    public void AForcedMoveIntoANeighbourKeepsTheFrameTheRuntimeMeasuresIn()
    {
        using GameRuntime runtime = NewRuntime();
        var neighborhood = new RuntimeCollisionNeighborhood(
            runtime,
            new FlatContent());
        runtime.EntityObjects.Physics.ObserveLocalPlayerCreate(Arrival);
        neighborhood.CenterOn(Arrival);
        Drive(neighborhood, Arrival);

        // A server-forced position next door is not a teleport: the runtime
        // keeps measuring from the arrival, so the collision must too.
        neighborhood.CenterOn(East);
        Drive(neighborhood, East);

        Assert.Equal(new Vector3(192f, 0f, 0f), OffsetOf(runtime, East));
        Assert.Equal(Vector3.Zero, OffsetOf(runtime, Arrival));
    }

    [Fact]
    public void ANewSessionRepublishesFromItsOwnArrival()
    {
        using GameRuntime runtime = NewRuntime();
        var content = new FlatContent();
        var neighborhood = new RuntimeCollisionNeighborhood(runtime, content);
        runtime.EntityObjects.Physics.ObserveLocalPlayerCreate(Arrival);
        neighborhood.CenterOn(Arrival);
        Drive(neighborhood, Arrival);

        // The session ends and the next one arrives a landblock east.
        runtime.EntityObjects.Physics.ResetSessionPhysics();
        runtime.EntityObjects.Physics.ObserveLocalPlayerCreate(East);
        neighborhood.CenterOn(East);
        Drive(neighborhood, East);

        Assert.Equal(Vector3.Zero, OffsetOf(runtime, East));
        Assert.Equal(new Vector3(-192f, 0f, 0f), OffsetOf(runtime, Arrival));
    }

    [Fact]
    public void ReleaseWithdrawsEverythingPublishedAndTheNextArrivalStartsAfresh()
    {
        using GameRuntime runtime = NewRuntime();
        var neighborhood = new RuntimeCollisionNeighborhood(
            runtime,
            new FlatContent());
        runtime.EntityObjects.Physics.ObserveLocalPlayerCreate(Arrival);
        neighborhood.CenterOn(Arrival);
        Drive(neighborhood, Arrival);
        Assert.Equal(9, runtime.EntityObjects.Physics.Engine.LandblockCount);

        neighborhood.Release();
        for (int pump = 0; pump < 20 && neighborhood.HasPublishedCollision; pump++)
            neighborhood.Pump();

        Assert.False(neighborhood.HasPublishedCollision);
        Assert.Equal(0, runtime.EntityObjects.Physics.Engine.LandblockCount);
        Assert.Equal(0u, neighborhood.FrameLandblock);
        Assert.False(neighborhood.IsReady(Arrival));

        // The same place again is a fresh arrival, not a no-op.
        neighborhood.CenterOn(Arrival);
        Drive(neighborhood, Arrival);
        Assert.Equal(9, runtime.EntityObjects.Physics.Engine.LandblockCount);
    }

    [Fact]
    public void APacedNeighborhoodStopsWhenTheFrameIsSpentAndCarriesOnNextCall()
    {
        using GameRuntime runtime = NewRuntime();
        int checksThisFrame = 0;
        var neighborhood = new RuntimeCollisionNeighborhood(
            runtime,
            new FlatContent(),
            new RuntimeCollisionPacing
            {
                // Two steps of work per call, then the frame is spent.
                IsFrameSpent = () => ++checksThisFrame > 2,
            });
        runtime.EntityObjects.Physics.ObserveLocalPlayerCreate(Arrival);

        neighborhood.CenterOn(Arrival);

        Assert.False(neighborhood.IsReady(Arrival));
        Assert.Equal(0, runtime.EntityObjects.Physics.Engine.LandblockCount);

        int calls = 1;
        while (runtime.EntityObjects.Physics.Engine.LandblockCount < 9 && calls < 10_000)
        {
            checksThisFrame = 0;
            neighborhood.Pump();
            calls++;
        }
        Assert.Equal(9, runtime.EntityObjects.Physics.Engine.LandblockCount);
        Assert.True(calls > 9, $"Published in {calls} calls, not spread over frames.");
        checksThisFrame = 0;
        Assert.True(neighborhood.IsReady(Arrival));
    }

    [Fact]
    public void ADestinationIsReadyOnceItsOwnLandblockIsPublishedBeforeTheRingAroundIt()
    {
        // A landblock of the ring can wait on the arrival itself: the
        // character's old body, still in that landblock until it is placed
        // at the destination, holds that landblock's commit until its
        // placement is acknowledged. So the arrival cannot wait for the whole
        // ring; it waits for its own landblock, published in the frame the
        // runtime measures in.
        using GameRuntime runtime = NewRuntime();
        int checksThisFrame = 0;
        var neighborhood = new RuntimeCollisionNeighborhood(
            runtime,
            new FlatContent(),
            new RuntimeCollisionPacing
            {
                IsFrameSpent = () => ++checksThisFrame > 2,
            });
        runtime.EntityObjects.Physics.ObserveLocalPlayerCreate(Arrival);
        neighborhood.CenterOn(Arrival);
        for (int calls = 0;
             runtime.EntityObjects.Physics.Engine.LandblockCount < 1 && calls < 10_000;
             calls++)
        {
            checksThisFrame = 0;
            neighborhood.Pump();
        }

        // No more work this frame: the ring around the arrival is unfinished.
        checksThisFrame = int.MaxValue / 2;
        Assert.Equal(1, runtime.EntityObjects.Physics.Engine.LandblockCount);
        Assert.False(neighborhood.IsQuiescent);
        Assert.True(neighborhood.IsReady(Arrival));
        Assert.False(neighborhood.IsReady(East));
    }

    [Fact]
    public void ABackgroundNeighborhoodLoadsLandblocksOffTheCallersThread()
    {
        using GameRuntime runtime = NewRuntime();
        var content = new FlatContent();
        var neighborhood = new RuntimeCollisionNeighborhood(
            runtime,
            content,
            new RuntimeCollisionPacing { LoadInBackground = true });
        runtime.EntityObjects.Physics.ObserveLocalPlayerCreate(Arrival);

        neighborhood.CenterOn(Arrival);
        var clock = System.Diagnostics.Stopwatch.StartNew();
        while (runtime.EntityObjects.Physics.Engine.LandblockCount < 9
            && clock.Elapsed < TimeSpan.FromSeconds(10))
        {
            neighborhood.Pump();
            Thread.Sleep(1);
        }

        Assert.Equal(9, runtime.EntityObjects.Physics.Engine.LandblockCount);
        Assert.Equal(9, content.LoadThreads.Count);
        Assert.DoesNotContain(
            System.Environment.CurrentManagedThreadId,
            content.LoadThreads);
        Assert.True(neighborhood.IsReady(Arrival));
    }

    /// <summary>
    /// The game files are closed when the process ends. A landblock still
    /// being read in the background then reads closed files; stopping the
    /// background work waits for the read in hand and starts no other.
    /// </summary>
    [Fact]
    public async Task StoppingTheBackgroundWorkWaitsForTheLoadInHandAndStartsNoOther()
    {
        using GameRuntime runtime = NewRuntime();
        using var hold = new ManualResetEventSlim(false);
        var content = new FlatContent { Hold = hold };
        var neighborhood = new RuntimeCollisionNeighborhood(
            runtime,
            content,
            new RuntimeCollisionPacing { LoadInBackground = true });
        runtime.EntityObjects.Physics.ObserveLocalPlayerCreate(Arrival);
        neighborhood.CenterOn(Arrival);
        neighborhood.Pump();
        Assert.True(SpinWait.SpinUntil(
            () => Volatile.Read(ref content.LoadsInFlight) == 1,
            TimeSpan.FromSeconds(5)));

        Task stopping = Task.Run(neighborhood.StopBackgroundWork);

        // The load in hand is let go only once the stop has asked the worker
        // to stop: before that, going on to the next landblock is right.
        Assert.True(SpinWait.SpinUntil(
            () => neighborhood.BackgroundLoadsCancelled,
            TimeSpan.FromSeconds(5)));
        Assert.NotSame(stopping, await Task.WhenAny(stopping, Task.Delay(200)));
        hold.Set();
        Assert.Same(stopping, await Task.WhenAny(stopping, Task.Delay(5000)));
        Assert.Equal(0, Volatile.Read(ref content.LoadsInFlight));
        Thread.Sleep(50);
        Assert.Single(content.LoadThreads);
    }

    /// <summary>
    /// Another creature is carried by local physics between the server's
    /// updates only while it is within 96 meters of the player (and stands in
    /// a cell the client holds). 96 meters is half a landblock, so the
    /// whole of that circle lies inside the 3x3 landblocks around the
    /// player's own, and it stays inside them while the window follows a
    /// walk into the next landblock: the landblocks given up are never the
    /// ones the circle reaches.
    /// </summary>
    [Fact]
    public void TheNeighborhoodHoldsEveryCellWithinTheRemoteActivityRangeWhileItFollows()
    {
        const float ActivityRange =
            AcDream.Core.Physics.RetailObjectActivityGate.MaxPhysicsDistance;
        using GameRuntime runtime = NewRuntime();
        bool spent = false;
        var neighborhood = new RuntimeCollisionNeighborhood(
            runtime,
            new FlatContent(),
            new RuntimeCollisionPacing
            {
                IsFrameSpent = () =>
                {
                    if (spent)
                        return true;
                    spent = true;
                    return false;
                },
            });
        runtime.EntityObjects.Physics.ObserveLocalPlayerCreate(Arrival);
        neighborhood.CenterOn(Arrival);
        for (int call = 0; call < 10_000 && !neighborhood.IsReady(Arrival); call++)
        {
            spent = false;
            neighborhood.Pump();
        }
        while (!neighborhood.IsQuiescent)
        {
            spent = false;
            neighborhood.Pump();
        }

        // Corners and edges of the arrival's landblock, in the frame's meters.
        foreach (Vector2 player in new Vector2[]
        {
            new(1f, 1f), new(191f, 1f), new(1f, 191f), new(191f, 191f),
            new(96f, 96f), new(96f, 1f), new(1f, 96f),
        })
        {
            AssertRangeHeld(neighborhood, player, ActivityRange);
        }

        // Walk east over the edge: every step of the window's slide still
        // holds the circle around where the player now stands.
        var walked = new Vector2(193f, 2f);
        neighborhood.Follow(CellAt(walked));
        for (int call = 0; call < 10_000; call++)
        {
            AssertRangeHeld(neighborhood, walked, ActivityRange);
            if (neighborhood.IsQuiescent && HasSlidEast(runtime))
                break;
            spent = false;
            neighborhood.Pump();
        }
        Assert.True(HasSlidEast(runtime), "The window never followed the walk.");
        Assert.Equal(9, runtime.EntityObjects.Physics.Engine.LandblockCount);
        AssertRangeHeld(neighborhood, walked, ActivityRange);
    }

    /// <summary>
    /// A portal into the neighbouring landblock moves the frame every
    /// position is measured in, and every landblock is withdrawn and
    /// published again in the new frame. A creature standing in one of them
    /// is set aside while its landblock is withdrawn and put back once it is
    /// published again: it must come back where it stood, in its own
    /// landblock, not 192 meters away where its old measurement lands in the
    /// new frame.
    /// </summary>
    [Fact]
    public void ACreatureSetAsideByARecallIntoTheNeighbourComesBackWhereItStood()
    {
        const uint Creature = 0x7A0000C1u;
        var standing = new Vector3(20f, 30f, 0f);
        uint creatureCell = CellAt(new Vector2(192f + standing.X, standing.Y));
        Assert.Equal(0x292A0002u, creatureCell);
        using GameRuntime runtime = NewRuntime();
        RuntimePhysicsState physics = runtime.EntityObjects.Physics;
        var neighborhood = new RuntimeCollisionNeighborhood(runtime, new FlatContent());
        physics.ObserveLocalPlayerCreate(Arrival);
        neighborhood.CenterOn(Arrival);
        Drive(neighborhood, Arrival);

        // The creature stands in the landblock east of the arrival, 192 m
        // east of the frame's corner.
        RuntimeEntityRecord creature = runtime.EntityObjects
            .RegisterEntity(RuntimeEntityTestSpawns.Spawn(
                Creature, creatureCell, standing.X, standing.Y, PhysicsStateFlags.Gravity, standing.Z))
            .Canonical!;
        runtime.EntityObjects.Entities.SetFinalPhysicsState(creature, PhysicsStateFlags.Gravity);
        var body = new PhysicsBody
        {
            Position = standing + new Vector3(192f, 0f, 0f),
            Orientation = Quaternion.Identity,
            LastUpdateTime = 1d,
            State = PhysicsStateFlags.Gravity,
            TransientState = TransientStateFlags.Active,
        };
        body.SnapToCell(creatureCell, body.Position, standing);
        runtime.EntityObjects.Entities.SetPhysicsBody(creature, body);
        creature.ObjectClock.Activate();
        RuntimeSetPositionOutcome placed = physics.SetPosition.Apply(
            creature,
            creature.PositionAuthorityVersion,
            new RuntimeSetPositionCommand(
                new PhysicsSetPositionRequest(
                    standing + new Vector3(192f, 0f, 0f),
                    Quaternion.Identity,
                    creatureCell,
                    standing,
                    ImmutableArray<FlatCollisionSphere>.Empty,
                    Scale: 1f,
                    StepUpHeight: 0.4f,
                    StepDownHeight: 0.4f,
                    Flags: PhysicsSetPositionFlags.Placement
                        | PhysicsSetPositionFlags.Slide),
                RuntimeSetPositionOperationKind.RemoteAuthoritative,
                GameTime: 10d,
                ExpectedVelocityAuthorityVersion: 0UL,
                ShadowWorldOffsetX: 192f,
                ShadowWorldOffsetY: 0f));
        Assert.Equal(PhysicsResidenceDisposition.Committed, placed.Residence);
        AcknowledgeEveryProjection(runtime);
        Assert.Equal(creatureCell, creature.FullCellId);
        Assert.True(physics.IsSpatialRoot(creature));

        // The recall: the server puts the character in that landblock, and
        // the runtime measures from it from now on.
        physics.ObserveLocalWorldFrame(0x292A0010u, teleportAdvanced: true);
        neighborhood.CenterOn(0x292A0010u);
        for (int pump = 0;
             pump < 1_000
                 && !(neighborhood.IsReady(0x292A0010u)
                     && neighborhood.IsQuiescent
                     && runtime.Placements.PendingCount == 0);
             pump++)
        {
            AcknowledgeEveryProjection(runtime);
            neighborhood.Pump();
        }
        Assert.True(neighborhood.IsQuiescent, "The ring never finished publishing.");

        Assert.Equal(creatureCell, creature.FullCellId);
        Assert.Equal(standing, creature.PhysicsBody!.Position);
    }

    /// <summary>
    /// A creature the server places in a landblock the client holds no
    /// collision for waits until that landblock is published. A portal may
    /// move the frame while it waits; when it is put down it must stand
    /// where the server put it, measured in the new frame.
    /// </summary>
    [Fact]
    public void ACreatureWaitingForItsLandblockAcrossARecallIsPutDownWhereTheServerPutIt()
    {
        const uint Creature = 0x7A0000C2u;
        var standing = new Vector3(20f, 30f, 0f);
        // Two landblocks east of the arrival: outside the ring around it.
        uint creatureCell = CellAt(new Vector2(384f + standing.X, standing.Y));
        Assert.Equal(0x2A2A0002u, creatureCell);
        using GameRuntime runtime = NewRuntime();
        RuntimePhysicsState physics = runtime.EntityObjects.Physics;
        var neighborhood = new RuntimeCollisionNeighborhood(runtime, new FlatContent());
        physics.ObserveLocalPlayerCreate(Arrival);
        neighborhood.CenterOn(Arrival);
        Drive(neighborhood, Arrival);

        RuntimeEntityRecord creature = runtime.EntityObjects
            .RegisterEntity(RuntimeEntityTestSpawns.Spawn(
                Creature, creatureCell, standing.X, standing.Y, PhysicsStateFlags.Gravity, standing.Z))
            .Canonical!;
        runtime.EntityObjects.Entities.SetFinalPhysicsState(creature, PhysicsStateFlags.Gravity);
        var body = new PhysicsBody
        {
            Orientation = Quaternion.Identity,
            LastUpdateTime = 1d,
            State = PhysicsStateFlags.Gravity,
        };
        runtime.EntityObjects.Entities.SetPhysicsBody(creature, body);
        RuntimeSetPositionOutcome waiting = physics.SetPosition.Apply(
            creature,
            creature.PositionAuthorityVersion,
            new RuntimeSetPositionCommand(
                new PhysicsSetPositionRequest(
                    standing + new Vector3(384f, 0f, 0f),
                    Quaternion.Identity,
                    creatureCell,
                    standing,
                    ImmutableArray<FlatCollisionSphere>.Empty,
                    Scale: 1f,
                    StepUpHeight: 0.4f,
                    StepDownHeight: 0.4f,
                    Flags: PhysicsSetPositionFlags.Placement
                        | PhysicsSetPositionFlags.Slide),
                RuntimeSetPositionOperationKind.RemoteAuthoritative,
                GameTime: 10d,
                ExpectedVelocityAuthorityVersion: 0UL,
                ShadowWorldOffsetX: 384f,
                ShadowWorldOffsetY: 0f));
        Assert.Equal(RuntimeSetPositionStatus.DeferredCell, waiting.Status);
        AcknowledgeEveryProjection(runtime);

        // The recall into the landblock east of the arrival: the creature's
        // landblock is in the new ring.
        physics.ObserveLocalWorldFrame(0x292A0010u, teleportAdvanced: true);
        neighborhood.CenterOn(0x292A0010u);
        for (int pump = 0;
             pump < 1_000
                 && !(neighborhood.IsReady(0x292A0010u)
                     && neighborhood.IsQuiescent
                     && runtime.Placements.PendingCount == 0);
             pump++)
        {
            AcknowledgeEveryProjection(runtime);
            neighborhood.Pump();
        }
        Assert.True(neighborhood.IsQuiescent, "The ring never finished publishing.");

        Assert.Equal(creatureCell, creature.FullCellId);
        Assert.Equal(standing + new Vector3(192f, 0f, 0f), creature.PhysicsBody!.Position);
    }

    private static void AcknowledgeEveryProjection(GameRuntime runtime)
    {
        RuntimeSetPositionState setPosition =
            runtime.EntityObjects.Physics.SetPosition;
        while (setPosition.TryPeekProjection(
                   out RuntimePlacementProjectionSnapshot pending))
        {
            Assert.True(
                setPosition.AcknowledgeProjection(pending.Token),
                $"Projection {pending.Kind} 0x{pending.Token.ExactCellId:X8} was not acknowledged.");
        }
    }

    /// <summary>The column east of the arrival's neighbors is in, the one west of it is out.</summary>
    private static bool HasSlidEast(GameRuntime runtime)
    {
        var engine = runtime.EntityObjects.Physics.Engine;
        for (uint y = 0x29u; y <= 0x2Bu; y++)
        {
            if (!engine.IsLandblockTerrainResident((0x2Au << 24) | (y << 16) | 0xFFFFu)
                || engine.IsLandblockTerrainResident((0x27u << 24) | (y << 16) | 0xFFFFu))
            {
                return false;
            }
        }
        return true;
    }

    private static void AssertRangeHeld(
        RuntimeCollisionNeighborhood neighborhood,
        Vector2 player,
        float range)
    {
        for (int degrees = 0; degrees < 360; degrees += 15)
        {
            float radians = degrees * MathF.PI / 180f;
            Vector2 at = player + range * new Vector2(MathF.Cos(radians), MathF.Sin(radians));
            uint cell = CellAt(at);
            Assert.True(
                neighborhood.IsCollisionPublished(cell),
                $"Cell 0x{cell:X8}, {range} m from the player at {player}, has no collision.");
        }
    }

    /// <summary>The outdoor cell at a point measured from the arrival landblock's corner.</summary>
    private static uint CellAt(Vector2 at)
    {
        int blockX = (int)MathF.Floor(at.X / 192f);
        int blockY = (int)MathF.Floor(at.Y / 192f);
        uint x = (uint)((int)((Arrival >> 24) & 0xFFu) + blockX);
        uint y = (uint)((int)((Arrival >> 16) & 0xFFu) + blockY);
        int cellX = (int)((at.X - (blockX * 192f)) / 24f);
        int cellY = (int)((at.Y - (blockY * 192f)) / 24f);
        return (x << 24) | (y << 16) | (uint)((cellX * 8) + cellY + 1);
    }

    /// <summary>
    /// A door is a server object: its collision is registered, under the
    /// door's own id, in the collision the session host owns, and the server
    /// opening it (the door turning ethereal) or closing it reaches that
    /// collision by the id. It keeps following the door when the door's
    /// landblock is withdrawn behind a walk and published again on the way
    /// back: a door opened meanwhile is open when its landblock returns.
    /// </summary>
    [Fact]
    public void ADoorsCollisionFollowsItsOpenAndClosedStateByItsId()
    {
        const uint Door = 0x7A0000D0u;
        const uint DoorCell = 0x282A0025u;
        var doorAt = new Vector3(96f, 100f, 0f);
        using GameRuntime runtime = NewRuntime();
        var neighborhood = new RuntimeCollisionNeighborhood(runtime, new FlatContent());
        runtime.EntityObjects.Physics.ObserveLocalPlayerCreate(Arrival);
        neighborhood.CenterOn(Arrival);
        Drive(neighborhood, Arrival);
        PhysicsEngine engine = runtime.EntityObjects.Physics.Engine;

        // The server creates the closed door; its collision goes in under
        // its id, the way every server object's does.
        RuntimeEntityRecord door = runtime.EntityObjects
            .RegisterEntity(RuntimeEntityTestSpawns.Spawn(
                Door, DoorCell, doorAt.X, doorAt.Y, PhysicsStateFlags.ReportCollisions, doorAt.Z))
            .Canonical!;
        runtime.EntityObjects.ApplyAcceptedSpawn(
            door,
            door.CreateIntegrationVersion,
            door.Snapshot,
            replaceGeneration: false);
        uint doorId = Assert.IsType<uint>(door.LocalEntityId);
        LiveEntityCollisionBuilder.Register(
            engine.ShadowObjects,
            new LiveEntityCollisionRegistration(
                doorId,
                0x02000001u,
                doorAt,
                Quaternion.Identity,
                [ShadowShape.Cylinder(0x02000001u, Vector3.Zero, Quaternion.Identity, 1f, 1f, 3f)],
                (uint)door.FinalPhysicsState,
                EntityCollisionFlags.HasWeenie,
                0f,
                0f,
                DoorCell,
                DoorCell,
                []));
        Assert.True(Blocked(engine), "The closed door lets a walker through.");

        ushort stateSequence = 1;
        SetDoor(open: true);
        Assert.True(IsEthereal(engine, doorId), "The open door's collision is still solid.");
        Assert.False(Blocked(engine), "The open door still blocks.");
        SetDoor(open: false);
        Assert.False(IsEthereal(engine, doorId));
        Assert.True(Blocked(engine), "The closed door lets a walker through.");

        // Walk two landblocks east: the door's landblock is withdrawn. The
        // server opens the door meanwhile; walk back and it is open.
        neighborhood.Follow(0x292A0001u);
        Drive(neighborhood, 0x292A0001u);
        neighborhood.Follow(0x2A2A0001u);
        Drive(neighborhood, 0x2A2A0001u);
        Assert.False(neighborhood.IsCollisionPublished(DoorCell));
        SetDoor(open: true);
        neighborhood.Follow(0x292A0001u);
        Drive(neighborhood, 0x292A0001u);
        neighborhood.Follow(Arrival);
        Drive(neighborhood, Arrival);
        Assert.True(neighborhood.IsCollisionPublished(DoorCell));
        Assert.True(IsEthereal(engine, doorId), "The door opened while away came back closed.");
        Assert.False(Blocked(engine), "The door opened while away blocks.");
        SetDoor(open: false);
        Assert.True(Blocked(engine), "The door closed again lets a walker through.");

        void SetDoor(bool open)
        {
            stateSequence++;
            uint state = (uint)PhysicsStateFlags.ReportCollisions
                | (open ? (uint)PhysicsStateFlags.Ethereal : 0u);
            Assert.True(runtime.EntityObjects.TryApplyState(
                new SetState.Parsed(Door, state, InstanceSequence: 1, StateSequence: stateSequence),
                null,
                out _,
                out _));
        }
    }

    /// <summary>Whether a walker going north through where the door stands is stopped.</summary>
    private static bool Blocked(PhysicsEngine engine)
    {
        ResolveResult result = engine.ResolveWithTransition(
            new Vector3(96f, 95f, 0f),
            new Vector3(96f, 105f, 0f),
            0x282A0025u,
            sphereRadius: 0.48f,
            sphereHeight: 1.835f,
            stepUpHeight: 0.4f,
            stepDownHeight: 0.4f,
            isOnGround: true);
        return !result.Ok || result.Position.Y < 100f;
    }

    private static bool IsEthereal(PhysicsEngine engine, uint entityId)
    {
        ShadowEntry[] entries = engine.ShadowObjects.AllEntriesForDebug()
            .Where(entry => entry.EntityId == entityId)
            .ToArray();
        Assert.NotEmpty(entries);
        return entries.All(entry =>
            ((PhysicsStateFlags)entry.State).HasFlag(PhysicsStateFlags.Ethereal));
    }

    private static void Drive(
        RuntimeCollisionNeighborhood neighborhood,
        uint cell)
    {
        for (int pump = 0; pump < 200; pump++)
        {
            if (neighborhood.IsReady(cell) && neighborhood.IsQuiescent)
                return;
        }
        Assert.Fail($"The neighborhood never became ready at 0x{cell:X8}.");
    }

    private static Vector3 OffsetOf(GameRuntime runtime, uint cell)
    {
        Assert.True(
            runtime.EntityObjects.Physics.Engine.TryGetLandblockCollision(
                (cell & 0xFFFF0000u) | 0xFFFFu,
                out _,
                out _,
                out Vector3 offset),
            $"Landblock 0x{cell & 0xFFFF0000u:X8} has no collision.");
        return offset;
    }

    private static GameRuntime NewRuntime()
    {
        var gameplay = new RuntimeCreatureDeathStateTests.NoOpOperations();
        return new GameRuntime(new GameRuntimeDependencies(
            gameplay,
            gameplay,
            gameplay,
            gameplay));
    }
    [Fact]
    public void ALandblocksStaticObjectsAreStagedOneStepEach()
    {
        // The same neighborhood published twice, once with no static objects
        // and once with sixty in the arrival's landblock; one step of work
        // per call. Staging a landblock is cut into small steps, so the
        // objects add a step each, instead of all going in one step that
        // holds the caller's frame.
        int Steps(int statics)
        {
            using GameRuntime runtime = NewRuntime();
            int steps = 0;
            bool spent = false;
            var neighborhood = new RuntimeCollisionNeighborhood(
                runtime,
                new FlatContent { StaticsInArrival = statics },
                new RuntimeCollisionPacing
                {
                    IsFrameSpent = () =>
                    {
                        if (spent)
                            return true;
                        spent = true;
                        return false;
                    },
                });
            runtime.EntityObjects.Physics.ObserveLocalPlayerCreate(Arrival);
            neighborhood.CenterOn(Arrival);
            for (int call = 0;
                call < 100_000
                    && !(neighborhood.IsQuiescent
                        && runtime.EntityObjects.Physics.Engine.LandblockCount == 9);
                call++)
            {
                spent = false;
                neighborhood.Pump();
                steps++;
            }
            Assert.Equal(9, runtime.EntityObjects.Physics.Engine.LandblockCount);
            return steps;
        }

        int without = Steps(0);
        int with = Steps(60);

        Assert.True(
            with - without >= 60,
            $"Sixty static objects took {with - without} more steps; they were staged together.");
    }

    /// <summary>Every landblock flat, empty and present.</summary>
    private sealed class FlatContent : IRuntimeCollisionContent
    {
        public IDatReaderWriter Dats =>
            throw new NotSupportedException("Flat content reads no files.");

        public object DatLock { get; } = new();

        public IPreparedCollisionSource PreparedCollision =>
            throw new NotSupportedException("Flat content has no prepared collision.");

        public ImmutableArray<float> HeightTable { get; } =
            ImmutableArray.CreateRange(new float[256]);

        /// <summary>Static objects with no collision in the arrival's landblock.</summary>
        public int StaticsInArrival { get; init; }

        /// <summary>The thread each landblock was loaded on.</summary>
        public System.Collections.Concurrent.ConcurrentBag<int> LoadThreads { get; } = [];

        /// <summary>When set, each load waits for it before reading.</summary>
        public ManualResetEventSlim? Hold { get; init; }

        /// <summary>Loads begun and not yet returned.</summary>
        public int LoadsInFlight;

        public bool TryLoadCollisionLandblock(
            uint landblockId,
            Vector3 origin,
            out LoadedLandblock landblock,
            out LandblockCollisionBuild collisions)
        {
            LoadThreads.Add(System.Environment.CurrentManagedThreadId);
            Interlocked.Increment(ref LoadsInFlight);
            Hold?.Wait();
            Interlocked.Decrement(ref LoadsInFlight);
            landblock = new LoadedLandblock(
                landblockId,
                new LandBlock
                {
                    Terrain = new TerrainInfo[81],
                    Height = new byte[81],
                },
                Statics(landblockId),
                PhysicsDatBundle.Empty);
            collisions = LandblockCollisionBuild.Empty;
            return true;
        }

        private IReadOnlyList<WorldEntity> Statics(uint landblockId)
        {
            if ((landblockId & 0xFFFF0000u) != (Arrival & 0xFFFF0000u))
                return Array.Empty<WorldEntity>();
            var statics = new WorldEntity[StaticsInArrival];
            for (int index = 0; index < statics.Length; index++)
            {
                statics[index] = new WorldEntity
                {
                    Id = 0x80000000u + (uint)index,
                    SourceGfxObjOrSetupId = 0x02000042u,
                    Position = new Vector3(index, 10f, 0f),
                    Rotation = Quaternion.Identity,
                    MeshRefs = Array.Empty<MeshRef>(),
                };
            }
            return statics;
        }
    }
}
