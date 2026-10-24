using System.Collections.Immutable;
using System.Numerics;
using AcDream.App.Hosting;
using AcDream.App.Update;
using AcDream.Content;
using AcDream.Core.Net;
using AcDream.Core.Net.Messages;
using AcDream.Core.Physics;
using AcDream.Core.Plugins;
using AcDream.Core.World;
using AcDream.Runtime;
using AcDream.Runtime.Entities;
using AcDream.Runtime.World;
using DatReaderWriter.DBObjs;
using DatReaderWriter.Types;

namespace AcDream.App.Tests.Hosting;

/// <summary>
/// The graphical session's collision belongs to the session host: it is built
/// around the character by the host's own update, from the game files, with no
/// window and nothing drawn. It is the windowless client's neighborhood: 3x3
/// landblocks measured in the runtime's frame.
/// </summary>
public sealed class GraphicalSessionCollisionTests : IDisposable
{
    private const uint Player = 0x5000000Bu;
    private const uint Arrival = 0x282A0001u;

    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        "acdream-session-collision-" + Guid.NewGuid().ToString("N"));

    public GraphicalSessionCollisionTests() => Directory.CreateDirectory(_directory);

    public void Dispose()
    {
        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    [Fact]
    public void TheHostsUpdatePublishesCollisionAroundTheCharacterWithNoWindow()
    {
        using GraphicalSessionHost host = NewHost();
        using IDisposable content = host.Collision.BindContent(new FlatContent());
        Arrive(host.Runtime, Arrival);

        TickUntil(host, () => host.Collision.IsReady(Arrival)
            && host.Runtime.EntityObjects.Physics.Engine.LandblockCount == 9);

        Assert.Null(host.Window);
        Assert.True(host.Collision.IsReady(Arrival));
        PhysicsEngine engine = host.Runtime.EntityObjects.Physics.Engine;
        Assert.Equal(9, engine.LandblockCount);
        Assert.Equal(Vector3.Zero, OffsetOf(engine, Arrival));
        Assert.Equal(new Vector3(192f, 192f, 0f), OffsetOf(engine, 0x292B0001u));
    }

    [Fact]
    public void WithNoGameFilesBoundTheUpdateBuildsNothing()
    {
        using GraphicalSessionHost host = NewHost();
        Arrive(host.Runtime, Arrival);

        host.Tick(1d / 60d, NoFrame.Instance);

        Assert.False(host.Collision.HasContent);
        Assert.False(host.Collision.IsReady(Arrival));
        Assert.Equal(0, host.Runtime.EntityObjects.Physics.Engine.LandblockCount);
    }

    [Fact]
    public void ASessionThatLeftTheWorldHasItsCollisionWithdrawn()
    {
        using GraphicalSessionHost host = NewHost();
        using IDisposable content = host.Collision.BindContent(new FlatContent());
        Arrive(host.Runtime, Arrival);
        TickUntil(host, () => host.Collision.IsReady(Arrival)
            && host.Runtime.EntityObjects.Physics.Engine.LandblockCount == 9);
        Assert.Equal(9, host.Runtime.EntityObjects.Physics.Engine.LandblockCount);

        // The session ends: the runtime no longer measures from anywhere.
        host.Runtime.EntityObjects.Physics.ResetSessionPhysics();
        TickUntil(host, () => !host.Collision.Neighborhood.HasPublishedCollision);

        Assert.Equal(0, host.Runtime.EntityObjects.Physics.Engine.LandblockCount);
    }

    [Fact]
    public void ContentCanBeBoundOnceAtATime()
    {
        using GraphicalSessionHost host = NewHost();
        IDisposable first = host.Collision.BindContent(new FlatContent());

        Assert.Throws<InvalidOperationException>(() =>
            host.Collision.BindContent(new FlatContent()));
        first.Dispose();
        using IDisposable second = host.Collision.BindContent(new FlatContent());
        Assert.True(host.Collision.HasContent);
    }

    /// <summary>
    /// Runs the host's update until the condition holds: landblocks load on
    /// worker threads, so it can take more than one update.
    /// </summary>
    private static void TickUntil(GraphicalSessionHost host, Func<bool> done)
    {
        var clock = System.Diagnostics.Stopwatch.StartNew();
        while (!done() && clock.Elapsed < TimeSpan.FromSeconds(10))
        {
            host.Tick(1d / 60d, NoFrame.Instance);
            Thread.Sleep(1);
        }
        Assert.True(done(), "The host's update never got there.");
    }

    private GraphicalSessionHost NewHost() =>
        new(
            RuntimeOptions.Parse(_directory, _ => null) with
            {
                SessionId = "collision",
                StatusFilePath = Path.Combine(_directory, "status.jsonl"),
            },
            new WorldGameState(),
            new WorldEvents());

    /// <summary>The server creates the character at a cell.</summary>
    private static void Arrive(GameRuntime runtime, uint cellId)
    {
        runtime.PlayerIdentity.ServerGuid = Player;
        Assert.NotNull(runtime.EntityObjects
            .RegisterEntity(Spawn(Player, cellId))
            .Canonical);
        // What accepting the character's own creation does to the frame.
        runtime.EntityObjects.Physics.ObserveLocalPlayerCreate(cellId);
    }

    private static Vector3 OffsetOf(PhysicsEngine engine, uint cell)
    {
        Assert.True(
            engine.TryGetLandblockCollision(
                (cell & 0xFFFF0000u) | 0xFFFFu,
                out _,
                out _,
                out Vector3 offset),
            $"Landblock 0x{cell & 0xFFFF0000u:X8} has no collision.");
        return offset;
    }

    private static WorldSession.EntitySpawn Spawn(uint guid, uint cellId)
    {
        var position = new CreateObject.ServerPosition(
            cellId, 96f, 97f, 50f, 1f, 0f, 0f, 0f);
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
            RawState: (uint)PhysicsStateFlags.ReportCollisions,
            Position: position,
            Movement: null,
            AnimationFrame: null,
            SetupTableId: 0x02000001u,
            MotionTableId: null,
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
            "+Horan",
            null,
            null,
            null,
            PhysicsState: physics.RawState,
            InstanceSequence: 1,
            MovementSequence: 1,
            ServerControlSequence: 1,
            PositionSequence: 1,
            Physics: physics);
    }

    private sealed class NoFrame : IGraphicalSessionFrame
    {
        internal static readonly NoFrame Instance = new();

        public void TickSessionFrame(UpdateFrameInput input)
        {
        }
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

        public bool TryLoadCollisionLandblock(
            uint landblockId,
            Vector3 origin,
            out LoadedLandblock landblock,
            out LandblockCollisionBuild collisions)
        {
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
