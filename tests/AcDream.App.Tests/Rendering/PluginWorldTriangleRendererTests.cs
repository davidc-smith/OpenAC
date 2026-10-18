using System.Numerics;
using System.Runtime.InteropServices;
using AcDream.App.Plugins;
using AcDream.App.Rendering;
using AcDream.App.Rendering.Gpu;
using AcDream.App.Tests.Rendering.Gpu;
using AcDream.App.World;
using AcDream.Plugin.Abstractions;

namespace AcDream.App.Tests.Rendering;

/// <summary>
/// Plugins' filled world triangles: which are drawn, how, and what reaches
/// the batch when there are more than it holds.
///
/// Mutation check (2026-10-14): measuring a triangle's distance from its
/// first corner instead of its nearest point turned
/// <see cref="ALargeTriangleReachingIntoRangeIsDrawn"/> red; leaving out the
/// sort turned <see cref="WhenTheBudgetRunsOutTheTrianglesNearestTheCameraAreTheOnesDrawn"/> red.
/// </summary>
public sealed class PluginWorldTriangleRendererTests
{
    private static readonly PluginColor Red = new(255, 0, 0, 96);

    [Fact]
    public void TrianglesAreDrawnSeeThroughFromBothSidesUnderTheWorldsDepth()
    {
        using var scene = new Scene(camera: Vector3.Zero);

        GpuPipelineDescription pipeline = Assert.Single(scene.Device.CreatedPipelines).Description;
        Assert.Equal("plugin-world-triangle", pipeline.Name);
        Assert.Equal(GpuBlendMode.StraightAlpha, pipeline.Blend);
        Assert.Equal(GpuDepthState.TranslucentDefault, pipeline.Depth);
        Assert.Equal(GpuCullMode.None, pipeline.Cull);
    }

    [Fact]
    public void ATriangleIsDrawnBetweenItsCornersInItsColour()
    {
        using var scene = new Scene(camera: new Vector3(0f, 0f, 10f));

        scene.Draw(Triangle(new Vector3(10f, 0f, 0.1f), new Vector3(34f, 0f, 0.1f), new Vector3(10f, 24f, 2.1f), Red));

        float[][] vertices = scene.UploadedVertices();
        Assert.Equal(3, vertices.Length);
        Vector3[] corners = [new(10f, 0f, 0.1f), new(34f, 0f, 0.1f), new(10f, 24f, 2.1f)];
        for (int i = 0; i < 3; i++)
        {
            // The depth nudge moves each corner a few centimetres toward the camera.
            Assert.True(Vector3.Distance(corners[i], new Vector3(vertices[i][0], vertices[i][1], vertices[i][2])) < 0.1f);
            Assert.Equal(new Vector4(1f, 0f, 0f, 96f / 255f), new Vector4(vertices[i][3], vertices[i][4], vertices[i][5], vertices[i][6]));
        }
    }

    [Fact]
    public void NothingIsDrawnBeforeTheWorldsOriginIsKnown()
    {
        using var scene = new Scene(camera: Vector3.Zero, originKnown: false);

        scene.Draw(Triangle(new Vector3(5f, 0f, 0f), new Vector3(6f, 0f, 0f), new Vector3(5f, 1f, 0f), Red));

        Assert.Equal(0, scene.DrawnVertexCount);
    }

    [Fact]
    public void ATriangleWithACornerThatIsNotANumberIsDropped()
    {
        using var scene = new Scene(camera: Vector3.Zero);
        PluginWorldTriangle good = Triangle(new Vector3(5f, 0f, 0f), new Vector3(6f, 0f, 0f), new Vector3(5f, 1f, 0f), Red);
        PluginWorldTriangle bad = good with { B = good.B with { Elevation = double.NaN } };
        PluginWorldTriangle infinite = good with { C = good.C with { EastWest = double.PositiveInfinity } };

        scene.Draw(bad, good, infinite);

        Assert.Equal(3, scene.DrawnVertexCount);
    }

    [Fact]
    public void ATriangleOutOfRangeIsNotDrawn()
    {
        using var scene = new Scene(camera: Vector3.Zero);

        scene.Draw(Triangle(new Vector3(300f, 0f, 0f), new Vector3(310f, 0f, 0f), new Vector3(300f, 10f, 0f), Red));

        Assert.Equal(0, scene.DrawnVertexCount);
    }

    [Fact]
    public void ALargeTriangleReachingIntoRangeIsDrawn()
    {
        using var scene = new Scene(camera: Vector3.Zero);

        // Every corner is past the range; its long edge passes 50 m away.
        scene.Draw(Triangle(new Vector3(-400f, 50f, 0f), new Vector3(400f, 50f, 0f), new Vector3(0f, 600f, 0f), Red));

        Assert.Equal(3, scene.DrawnVertexCount);
    }

    [Fact]
    public void ATriangleOutOfViewIsNotDrawn()
    {
        using var scene = new Scene(LookingEast());

        scene.Draw(
            Triangle(new Vector3(50f, 0f, 0f), new Vector3(60f, 0f, 0f), new Vector3(50f, 10f, 0f), Red),
            Triangle(new Vector3(-50f, 0f, 0f), new Vector3(-60f, 0f, 0f), new Vector3(-50f, 10f, 0f), Red));

        Assert.Equal(3, scene.DrawnVertexCount);
    }

    [Fact]
    public void WhenTheBudgetRunsOutTheTrianglesNearestTheCameraAreTheOnesDrawn()
    {
        using var scene = new Scene(camera: Vector3.Zero);
        int capacity = PluginWorldTriangleRenderer.VertexBudget / 3;
        var triangles = new List<PluginWorldTriangle>();
        // Far triangles first, more than the budget holds...
        for (int i = 0; i < capacity + 500; i++)
        {
            float angle = i * MathF.Tau / (capacity + 500);
            var at = new Vector3(MathF.Cos(angle) * 200f, MathF.Sin(angle) * 200f, 0f);
            triangles.Add(Triangle(at, at + Vector3.UnitX, at + Vector3.UnitY, Red));
        }
        // ...then one right beside the camera.
        triangles.Add(Triangle(new Vector3(5f, 0f, 0f), new Vector3(6f, 0f, 0f), new Vector3(5f, 1f, 0f), Red));

        scene.Draw([.. triangles]);

        Assert.Equal(capacity * 3, scene.DrawnVertexCount);
        Assert.Contains(scene.UploadedPositions(), static p => MathF.Abs(p.X - 5f) < 0.1f && MathF.Abs(p.Y) < 0.1f);
    }

    [Fact]
    public void LayersAreDrawnTogetherAndADisposedLayerIsGone()
    {
        using var scene = new Scene(camera: Vector3.Zero);
        IPluginWorldTriangleLayer other = scene.Store.CreateLayer();
        other.SetTriangles([Triangle(new Vector3(8f, 0f, 0f), new Vector3(9f, 0f, 0f), new Vector3(8f, 1f, 0f), Red)]);

        scene.Draw(Triangle(new Vector3(5f, 0f, 0f), new Vector3(6f, 0f, 0f), new Vector3(5f, 1f, 0f), Red));
        Assert.Equal(6, scene.DrawnVertexCount);

        other.Dispose();
        scene.Draw(Triangle(new Vector3(5f, 0f, 0f), new Vector3(6f, 0f, 0f), new Vector3(5f, 1f, 0f), Red));
        Assert.Equal(9, scene.DrawnVertexCount);
        Assert.Throws<ObjectDisposedException>(() => other.SetTriangles([]));
    }

    [Fact]
    public void TheStoreCopiesWhatItIsGivenAndKeepsALimit()
    {
        var store = new PluginWorldTriangleStore();
        IPluginWorldTriangleLayer layer = store.CreateLayer();
        var triangles = new List<PluginWorldTriangle>
        {
            Triangle(Vector3.Zero, Vector3.UnitX, Vector3.UnitY, Red),
        };

        layer.SetTriangles(triangles);
        triangles.Clear();

        Assert.Single(Assert.Single(store.Layers));

        layer.SetTriangles(Enumerable.Repeat(
            Triangle(Vector3.Zero, Vector3.UnitX, Vector3.UnitY, Red),
            PluginWorldTriangleStore.MaxTrianglesPerLayer + 10).ToList());
        Assert.Equal(PluginWorldTriangleStore.MaxTrianglesPerLayer, Assert.Single(store.Layers).Length);
    }

    [Theory]
    [InlineData(0f, 0f, 5f, 0f, 0f, 0f)]       // over a corner
    [InlineData(5f, -3f, 0f, 5f, 0f, 0f)]      // beside an edge
    [InlineData(2f, 2f, 7f, 2f, 2f, 0f)]       // over the face
    [InlineData(20f, 20f, 0f, 5f, 5f, 0f)]     // past the long edge
    public void TheNearestPointOfATriangleIsOnItsFaceAnEdgeOrACorner(
        float px, float py, float pz, float ex, float ey, float ez)
    {
        Vector3 nearest = PluginWorldTriangleRenderer.ClosestPoint(
            new Vector3(px, py, pz), Vector3.Zero, new Vector3(10f, 0f, 0f), new Vector3(0f, 10f, 0f));

        Assert.True(Vector3.Distance(new Vector3(ex, ey, ez), nearest) < 0.001f, nearest.ToString());
    }

    // With the world's origin at the centre landblock, a local position is
    // the map position times 240 plus 84 m on each ground axis.
    private static PluginNavigationPosition At(Vector3 local) => new(
        CellId: 0u,
        EastWest: (local.X - 84d) / 240d,
        NorthSouth: (local.Y - 84d) / 240d,
        Elevation: local.Z / 240d,
        HeadingDegrees: 0f,
        IsOutdoor: true);

    private static PluginWorldTriangle Triangle(Vector3 a, Vector3 b, Vector3 c, PluginColor color) =>
        new(At(a), At(b), At(c), color);

    private static IWorldFrameCameraSource LookingEast()
    {
        var position = new Vector3(0f, 0f, 10f);
        Matrix4x4 view = Matrix4x4.CreateLookAt(position, new Vector3(100f, 0f, 10f), Vector3.UnitZ);
        Matrix4x4 projection = Matrix4x4.CreatePerspectiveFieldOfView(MathF.PI / 3f, 16f / 9f, 0.5f, 1000f);
        return new ViewingCamera(position, view * projection);
    }

    private sealed class Scene : IDisposable
    {
        private readonly IGpuFrame _frame;
        private readonly GroundShapeBatch _batch;
        private readonly PluginWorldTriangleRenderer _renderer;
        private IPluginWorldTriangleLayer? _layer;

        public Scene(Vector3 camera, bool originKnown = true)
            : this(new FixedCamera(camera), originKnown)
        {
        }

        public Scene(IWorldFrameCameraSource camera, bool originKnown = true)
        {
            _frame = Device.BeginFrame();
            _batch = new GroundShapeBatch(
                Device, new FixedFrame(_frame), new FourSampleWorldPass(),
                "plugin-world-triangle", PluginWorldTriangleRenderer.VertexBudget);
            var origin = new LiveWorldOriginState();
            if (originKnown)
                origin.TryInitialize(127, 127);
            _renderer = new PluginWorldTriangleRenderer(Store, _batch, camera, origin);
        }

        public RecordingGpuDevice Device { get; } = new(ringCapacityBytes: 64 * 1024 * 1024);

        public PluginWorldTriangleStore Store { get; } = new();

        public long DrawnVertexCount => Device.Calls.OfType<GpuRecordedDraw>().Sum(draw => (long)draw.VertexCount);

        public void Draw(params PluginWorldTriangle[] triangles)
        {
            _layer ??= Store.CreateLayer();
            _layer.SetTriangles(triangles);
            using IGpuPassEncoder encoder = _frame.BeginPass(WorldPass());
            _renderer.Render(encoder, 1280, 720);
        }

        public float[][] UploadedVertices()
        {
            var result = new List<float[]>();
            foreach (GpuRecordedRingAllocation allocation in Device.Calls.OfType<GpuRecordedRingAllocation>())
            {
                ReadOnlySpan<float> floats = MemoryMarshal.Cast<byte, float>(
                    Device.RingBytes.Slice((int)allocation.OffsetBytes, allocation.ByteCount));
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
