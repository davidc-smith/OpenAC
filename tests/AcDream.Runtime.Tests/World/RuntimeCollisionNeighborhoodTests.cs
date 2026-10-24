using System.Collections.Immutable;
using System.Numerics;
using AcDream.Content;
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

        /// <summary>The thread each landblock was loaded on.</summary>
        public System.Collections.Concurrent.ConcurrentBag<int> LoadThreads { get; } = [];

        public bool TryLoadCollisionLandblock(
            uint landblockId,
            Vector3 origin,
            out LoadedLandblock landblock,
            out LandblockCollisionBuild collisions)
        {
            LoadThreads.Add(System.Environment.CurrentManagedThreadId);
            landblock = new LoadedLandblock(
                landblockId,
                new LandBlock
                {
                    Terrain = new TerrainInfo[81],
                    Height = new byte[81],
                },
                Array.Empty<WorldEntity>(),
                PhysicsDatBundle.Empty);
            collisions = LandblockCollisionBuild.Empty;
            return true;
        }
    }
}
