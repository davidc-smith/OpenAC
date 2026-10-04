using System.Numerics;
using System.Runtime.InteropServices;
using AcDream.App.Interaction;
using AcDream.App.Plugins;
using AcDream.App.Rendering;
using AcDream.App.Rendering.Gpu;
using AcDream.App.Tests.Rendering.Gpu;
using AcDream.App.World;
using AcDream.Plugin.Abstractions;

namespace AcDream.App.Tests.Rendering;

/// <summary>
/// Where each plugin's ground shapes land in the world, which are drawn, and
/// what reaches the batch.
/// </summary>
public sealed class PluginGroundShapeRendererTests
{
    private const uint Creature = 0x8000_0001u;
    private static readonly PluginColor Orange = new(255, 128, 0, 64);
    private static readonly Func<float, float, float?> FlatLand = static (_, _) => 0f;
    private static readonly Func<float, float, float?> NoLand = static (_, _) => null;

    [Fact]
    public void NothingIsDrawnBeforeTheWorldsOriginIsKnown()
    {
        using var scene = new Scene(camera: Vector3.Zero, originKnown: false);

        scene.Draw(PluginGroundShape.Disc(At(new Vector3(5f, 0f, 0f)), 2f, Orange));

        Assert.Equal(0, scene.DrawnVertexCount);
    }

    [Fact]
    public void AShapeOverAnObjectTheClientDoesNotHoldIsNotDrawn()
    {
        using var scene = new Scene(camera: Vector3.Zero);

        scene.Draw(PluginGroundShape.Ring(PluginMarkerAnchor.Object(Creature), 2f, 0.2f, Orange));

        Assert.Equal(0, scene.DrawnVertexCount);
    }

    [Fact]
    public void AShapeWhoseCentreIsBeyondTheDrawRangeIsNotDrawn()
    {
        using var scene = new Scene(camera: Vector3.Zero);

        scene.Draw(
            PluginGroundShape.Disc(At(new Vector3(260f, 0f, 0f)), 2f, Orange),
            PluginGroundShape.Disc(At(new Vector3(240f, 0f, 0f)), 2f, Orange));

        Vector3[] points = scene.UploadedPositions();
        Assert.NotEmpty(points);
        Assert.All(points, p => Assert.True(p.X < 250f));
    }

    [Fact]
    public void AShapeOutsideTheCamerasViewIsNotDrawn()
    {
        // Ten metres up, looking level to the east.
        using var scene = new Scene(LookingEast());
        PluginGroundShape ahead = PluginGroundShape.Disc(At(new Vector3(50f, 0f, 0f)), 2f, Orange);
        PluginGroundShape behind = PluginGroundShape.Disc(At(new Vector3(-50f, 0f, 0f)), 2f, Orange);
        PluginGroundShape beside = PluginGroundShape.Disc(At(new Vector3(0f, 60f, 0f)), 2f, Orange);
        // Centred behind the camera, but reaching out in front of it.
        PluginGroundShape around = PluginGroundShape.Ring(At(new Vector3(-10f, 0f, 0f)), 30f, 0.5f, Orange);

        scene.Draw(ahead, behind, beside, around);

        Assert.Equal(VertexCount(ahead) + VertexCount(around), scene.DrawnVertexCount);
    }

    [Fact]
    public void AShapeOnTheLandIsInViewWhereTheLandIsNotWhereItsAnchorIs()
    {
        // Pinned 50 m under the land, below the view; it is drawn on the land, in view.
        using var scene = new Scene(LookingEast());
        PluginGroundShape sunk = PluginGroundShape.Disc(At(new Vector3(50f, 0f, -50f)), 2f, Orange);

        scene.Draw(sunk);

        Assert.Equal(VertexCount(sunk), scene.DrawnVertexCount);
    }

    [Fact]
    public void AShapeOffScreenDoesNotTakeTheBudgetFromOneOnIt()
    {
        using var scene = new Scene(LookingEast());
        var shapes = new List<PluginGroundShape>();
        // Large discs wholly behind the camera and nearer to it than the ring
        // ahead: together far more than the budget.
        for (int i = 0; i < 20; i++)
            shapes.Add(PluginGroundShape.Disc(At(new Vector3(-60f, i - 10f, 0f)), 50f, Orange));
        shapes.Add(PluginGroundShape.Ring(At(new Vector3(70f, 0f, 0f)), 1f, 0.2f, Orange));
        Assert.True(20L * VertexCount(shapes[0]) > GroundShapeBatch.VertexBudget);

        scene.Draw([.. shapes]);

        Assert.Equal(VertexCount(shapes[^1]), scene.DrawnVertexCount);
    }

    [Fact]
    public void AShapeOverAnObjectLiesAroundItsFeet()
    {
        using var scene = new Scene(camera: Vector3.Zero);
        scene.Objects[Creature] = new WorldLabelAnchor(new Vector3(10f, 20f, 0f), 2f, WorldLabelAnchorSource.PhysicsCylinder);

        scene.Draw(PluginGroundShape.Ring(PluginMarkerAnchor.Object(Creature), 2f, 0.2f, Orange));

        Vector3[] points = scene.UploadedPositions();
        Assert.NotEmpty(points);
        // The depth nudge moves points a few centimetres toward the camera.
        Assert.All(points, p => Assert.InRange(
            Vector2.Distance(new Vector2(p.X, p.Y), new Vector2(10f, 20f)), 1.8f - 0.1f, 2f + 0.1f));
    }

    [Fact]
    public void AWedgeThatFacesItsObjectTurnsWithIt()
    {
        var facingEast = new WorldLabelAnchor(Vector3.Zero, 2f, WorldLabelAnchorSource.PhysicsCylinder) { HeadingDegrees = 90f };
        PluginGroundShape wedge = PluginGroundShape.Arc(
            PluginMarkerAnchor.Object(Creature), 5f, -10f, 20f, filled: true, Orange);

        using var turning = new Scene(camera: new Vector3(0f, 0f, 50f));
        turning.Objects[Creature] = facingEast;
        turning.Draw(wedge with { FacesObject = true });
        using var fixedNorth = new Scene(camera: new Vector3(0f, 0f, 50f));
        fixedNorth.Objects[Creature] = facingEast;
        fixedNorth.Draw(wedge);

        Assert.Contains(turning.UploadedPositions(), p => p.X > 4.5f);
        Assert.All(turning.UploadedPositions(), p => Assert.True(p.X >= -0.01f));
        Assert.Contains(fixedNorth.UploadedPositions(), p => p.Y > 4.5f);
    }

    [Fact]
    public void AWedgeAtAFixedSpotDoesNotTurnWithTheSpotsHeading()
    {
        // FacesObject is for arcs over objects; at a spot facing east the wedge still points north.
        PluginMarkerAnchor spot = PluginMarkerAnchor.At(At(new Vector3(5f, 6f, 0f)).Position with { HeadingDegrees = 90f });
        PluginGroundShape wedge = PluginGroundShape.Arc(spot, 5f, -10f, 20f, filled: true, Orange) with { FacesObject = true };

        Assert.True(PluginGroundShapeRenderer.TryPlace(wedge, _ => null, FlatLand, 127, 127, out GroundShapePlacement placement));

        Assert.Equal(-10f, placement.StartDegrees);
    }

    [Fact]
    public void EachFrameDrawsOnlyThatFramesShapes()
    {
        using var scene = new Scene(camera: Vector3.Zero);
        PluginGroundShape disc = PluginGroundShape.Disc(At(new Vector3(5f, 0f, 0f)), 2f, Orange);
        PluginGroundShape ring = PluginGroundShape.Ring(At(new Vector3(-5f, 0f, 0f)), 1f, 0.2f, Orange);

        scene.Draw(disc, ring);
        scene.Draw(ring);
        scene.Draw(ring);

        int discVertices = VertexCount(disc);
        int ringVertices = VertexCount(ring);
        Assert.Equal([(uint)(discVertices + ringVertices), (uint)ringVertices, (uint)ringVertices], scene.DrawnVertexCounts);
        // The last frame's upload is the ring alone, round its own spot.
        Vector3[] last = scene.UploadedPositions()[^ringVertices..];
        Assert.All(last, p => Assert.InRange(
            Vector2.Distance(new Vector2(p.X, p.Y), new Vector2(-5f, 0f)), 0.8f - 0.1f, 1f + 0.1f));
    }

    [Theory]
    [InlineData(0.3f, true)]   // standing on the land
    [InlineData(-0.9f, true)]  // a little sunk into it
    [InlineData(6f, false)]    // up on a roof or a bridge
    public void AnObjectFollowsTheLandOnlyWhenItStandsOnIt(float baseHeight, bool follows)
    {
        PluginGroundShape ring = PluginGroundShape.Ring(PluginMarkerAnchor.Object(Creature), 2f, 0.2f, Orange);
        var anchor = new WorldLabelAnchor(new Vector3(0f, 0f, baseHeight), 2f, WorldLabelAnchorSource.PhysicsCylinder) { IsOutdoor = true };

        Assert.True(PluginGroundShapeRenderer.TryPlace(ring, _ => anchor, FlatLand, 127, 127, out GroundShapePlacement placement));

        Assert.Equal(follows, placement.FollowTerrain);
        Assert.Equal(new Vector3(0f, 0f, baseHeight), placement.Centre);
    }

    [Fact]
    public void AnObjectInsideABuildingIsFlatEvenOnTheLand()
    {
        PluginGroundShape ring = PluginGroundShape.Ring(PluginMarkerAnchor.Object(Creature), 2f, 0.2f, Orange);
        var anchor = new WorldLabelAnchor(new Vector3(0f, 0f, 0.3f), 2f, WorldLabelAnchorSource.PhysicsCylinder) { IsOutdoor = false };

        Assert.True(PluginGroundShapeRenderer.TryPlace(ring, _ => anchor, FlatLand, 127, 127, out GroundShapePlacement placement));

        Assert.False(placement.FollowTerrain);
    }

    [Theory]
    [InlineData(1.0f, true)]
    [InlineData(1.01f, false)]
    public void TheOneMetreBoundaryIsInclusive(float baseHeight, bool follows)
    {
        PluginGroundShape ring = PluginGroundShape.Ring(PluginMarkerAnchor.Object(Creature), 2f, 0.2f, Orange);
        var anchor = new WorldLabelAnchor(new Vector3(0f, 0f, baseHeight), 2f, WorldLabelAnchorSource.PhysicsCylinder) { IsOutdoor = true };

        Assert.True(PluginGroundShapeRenderer.TryPlace(ring, _ => anchor, FlatLand, 127, 127, out GroundShapePlacement placement));

        Assert.Equal(follows, placement.FollowTerrain);
    }

    [Fact]
    public void AnObjectWithNoLandUnderItIsFlat()
    {
        PluginGroundShape ring = PluginGroundShape.Ring(PluginMarkerAnchor.Object(Creature), 2f, 0.2f, Orange);
        var anchor = new WorldLabelAnchor(Vector3.Zero, 2f, WorldLabelAnchorSource.PhysicsCylinder) { IsOutdoor = true };

        Assert.True(PluginGroundShapeRenderer.TryPlace(ring, _ => anchor, NoLand, 127, 127, out GroundShapePlacement placement));

        Assert.False(placement.FollowTerrain);
    }

    [Theory]
    [InlineData(true, 0f, true)]     // outdoors, on the land
    [InlineData(true, 6f, false)]    // outdoors but up on a roof
    [InlineData(false, 0f, false)]   // indoors
    [InlineData(true, -20f, true)]   // outdoors, far below the land
    public void ASpotFollowsTheLandOnlyOutdoorsAndOnIt(bool outdoor, float z, bool follows)
    {
        PluginGroundShape disc = PluginGroundShape.Disc(
            PluginMarkerAnchor.At(At(new Vector3(5f, 6f, z)).Position with { IsOutdoor = outdoor }), 2f, Orange);

        Assert.True(PluginGroundShapeRenderer.TryPlace(disc, _ => null, FlatLand, 127, 127, out GroundShapePlacement placement));

        Assert.Equal(follows, placement.FollowTerrain);
        Assert.Equal(5f, placement.Centre.X, 3);
        Assert.Equal(6f, placement.Centre.Y, 3);
        Assert.Equal(z, placement.Centre.Z, 3);
    }

    [Fact]
    public void TheShapesColourAndOpacityReachTheBatch()
    {
        using var scene = new Scene(camera: Vector3.Zero);

        scene.Draw(PluginGroundShape.Disc(At(new Vector3(5f, 0f, 0f)), 2f, Orange));

        float[] first = scene.UploadedVertices()[0];
        Assert.Equal(new[] { 1f, 128f / 255f, 0f, 64f / 255f }, first[3..7]);
    }

    [Fact]
    public void EveryPointIsPulledSlightlyTowardTheCamera()
    {
        Vector3 nudged = PluginGroundShapeRenderer.TowardCamera(new Vector3(100f, 0f, 0f), Vector3.Zero);

        Assert.Equal(99.8f, nudged.X, 3);
        Assert.Equal(0f, nudged.Y);
        Assert.Equal(0f, nudged.Z);
    }

    [Fact]
    public void WhenTheBudgetRunsOutTheShapesNearestTheCameraAreTheOnesDrawn()
    {
        using var scene = new Scene(camera: Vector3.Zero);
        var shapes = new List<PluginGroundShape>();
        for (int i = 0; i < 100; i++)
        {
            // Large discs on a ring 200 m out, sent first: far more than the budget.
            float angle = i * MathF.Tau / 100f;
            shapes.Add(PluginGroundShape.Disc(
                At(new Vector3(MathF.Cos(angle) * 200f, MathF.Sin(angle) * 200f, 0f)), 100f, Orange));
        }
        // The one small ring right beside the camera is sent last.
        shapes.Add(PluginGroundShape.Ring(At(new Vector3(5f, 0f, 0f)), 1f, 0.2f, Orange));

        scene.Draw([.. shapes]);

        Assert.InRange(scene.DrawnVertexCount, 1, GroundShapeBatch.VertexBudget);
        Assert.Contains(scene.UploadedPositions(), p => MathF.Abs(p.X - 5f) <= 1.2f && MathF.Abs(p.Y) <= 1.2f);
    }

    private static int VertexCount(PluginGroundShape shape)
    {
        Assert.True(WorldMarkerRules.TryNormalizeShape(shape, out PluginGroundShape normalized));
        return GroundShapeTessellator.VertexCount(normalized);
    }

    // With the world's origin at the centre landblock, a local position is
    // the map position times 240 plus 84 m on each ground axis.
    private static PluginMarkerAnchor At(Vector3 local) => PluginMarkerAnchor.At(new PluginNavigationPosition(
        CellId: 0u,
        EastWest: (local.X - 84d) / 240d,
        NorthSouth: (local.Y - 84d) / 240d,
        Elevation: local.Z / 240d,
        HeadingDegrees: 0f,
        IsOutdoor: true));

    private sealed class Scene : IDisposable
    {
        private readonly RecordingGpuDevice _device = new(ringCapacityBytes: 64 * 1024 * 1024);
        private readonly IGpuFrame _frame;
        private readonly GroundShapeBatch _batch;
        private readonly PluginWorldMarkerStore _store = new();
        private readonly PluginGroundShapeRenderer _renderer;
        private IPluginWorldMarkerLayer? _layer;

        public Scene(Vector3 camera, bool originKnown = true)
            : this(new FixedCamera(camera), originKnown)
        {
        }

        public Scene(IWorldFrameCameraSource camera, bool originKnown = true)
        {
            _frame = _device.BeginFrame();
            _batch = new GroundShapeBatch(_device, new FixedFrame(_frame), new FourSampleWorldPass());
            var origin = new LiveWorldOriginState();
            if (originKnown)
                origin.TryInitialize(127, 127);
            _renderer = new PluginGroundShapeRenderer(
                _store.CaptureShapes,
                _batch,
                camera,
                origin,
                id => Objects.TryGetValue(id, out WorldLabelAnchor anchor) ? anchor : null,
                FlatLand);
        }

        public Dictionary<uint, WorldLabelAnchor> Objects { get; } = [];

        public long DrawnVertexCount => _device.Calls.OfType<GpuRecordedDraw>().Sum(draw => (long)draw.VertexCount);

        public uint[] DrawnVertexCounts => [.. _device.Calls.OfType<GpuRecordedDraw>().Select(draw => draw.VertexCount)];

        public void Draw(params PluginGroundShape[] shapes)
        {
            _layer ??= _store.For("a.plugin").CreateLayer()!;
            Assert.True(_layer.SetShapes(shapes));
            using IGpuPassEncoder encoder = _frame.BeginPass(WorldPass());
            _renderer.Render(encoder, 1280, 720);
        }

        public float[][] UploadedVertices()
        {
            var result = new List<float[]>();
            foreach (GpuRecordedRingAllocation allocation in _device.Calls.OfType<GpuRecordedRingAllocation>())
            {
                ReadOnlySpan<float> floats = MemoryMarshal.Cast<byte, float>(
                    _device.RingBytes.Slice((int)allocation.OffsetBytes, allocation.ByteCount));
                for (int i = 0; i + GroundShapeBatch.FloatsPerVertex <= floats.Length; i += GroundShapeBatch.FloatsPerVertex)
                    result.Add(floats.Slice(i, GroundShapeBatch.FloatsPerVertex).ToArray());
            }
            return [.. result];
        }

        public Vector3[] UploadedPositions() =>
            [.. UploadedVertices().Select(v => new Vector3(v[0], v[1], v[2]))];

        public void Dispose() => _batch.Dispose();
    }

    private static GpuPassDescription WorldPass() => new()
    {
        Name = "vk-world",
        Color = new GpuColorAttachment(
            Target: null,
            Load: GpuLoadOp.Clear,
            Store: GpuStoreOp.Store,
            ClearColor: default),
        Depth = new GpuDepthAttachment(
            Load: GpuLoadOp.Clear,
            Store: GpuStoreOp.DontCare,
            ClearDepth: 1f,
            ClearStencil: 0),
        SampleCount = 4,
    };

    private static IWorldFrameCameraSource LookingEast()
    {
        var position = new Vector3(0f, 0f, 10f);
        Matrix4x4 view = Matrix4x4.CreateLookAt(position, new Vector3(100f, 0f, 10f), Vector3.UnitZ);
        Matrix4x4 projection = Matrix4x4.CreatePerspectiveFieldOfView(MathF.PI / 3f, 16f / 9f, 0.5f, 1000f);
        return new ViewingCamera(position, view * projection);
    }

    private sealed class ViewingCamera(Vector3 position, Matrix4x4 viewProjection) : IWorldFrameCameraSource
    {
        public WorldCameraFrame Resolve() => new(
            Camera: null!,
            Projection: Matrix4x4.Identity,
            ViewProjection: viewProjection,
            Frustum: FrustumPlanes.FromViewProjection(viewProjection),
            InverseView: Matrix4x4.Identity,
            Position: position);
    }

    // Sees everywhere: an all-zero frustum culls nothing.
    private sealed class FixedCamera(Vector3 position) : IWorldFrameCameraSource
    {
        public WorldCameraFrame Resolve() => new(
            Camera: null!,
            Projection: Matrix4x4.Identity,
            ViewProjection: Matrix4x4.Identity,
            Frustum: default,
            InverseView: Matrix4x4.Identity,
            Position: position);
    }

    private sealed class FixedFrame(IGpuFrame frame) : ICurrentGpuFrameSource
    {
        public IGpuFrame? CurrentFrame => frame;
    }

    private sealed class FourSampleWorldPass : IWorldPassScope
    {
        public int SampleCount => 4;

        public IGpuPassEncoder? CurrentEncoder => null;

        public int AttachmentWidth => 1280;

        public int AttachmentHeight => 720;

        public WorldFrameSections Sections { get; } = new();

        public IGpuPassEncoder RequireEncoder() =>
            throw new InvalidOperationException("No world pass is open.");

        public void ClearInteriorDepth()
        {
        }

        public IDisposable Publish(IGpuPassEncoder encoder) =>
            throw new NotSupportedException();
    }
}
