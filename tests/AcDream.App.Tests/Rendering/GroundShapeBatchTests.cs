using System.Numerics;
using System.Runtime.InteropServices;
using AcDream.App.Rendering;
using AcDream.App.Rendering.Gpu;
using AcDream.App.Tests.Rendering.Gpu;

namespace AcDream.App.Tests.Rendering;

/// <summary>
/// The batch plugins' ground shapes are drawn with: see-through triangles in
/// the world pass, hidden by the world but never hiding anything, and never
/// more than its budget.
/// </summary>
public sealed class GroundShapeBatchTests
{
    private static readonly Vector4 HalfOrange = new(1f, 0.5f, 0f, 0.25f);

    [Fact]
    public void ShapesAreSeeThroughTrianglesTestedAgainstTheWorldsDepth()
    {
        var device = new RecordingGpuDevice();
        IGpuFrame frame = device.BeginFrame();

        using var batch = new GroundShapeBatch(device, new FixedFrame(frame), new FourSampleWorldPass());

        GpuPipelineDescription pipeline = Assert.Single(device.CreatedPipelines).Description;
        Assert.Equal("ground-shape", pipeline.Name);
        Assert.Equal("ground_shape", pipeline.Shaders.Name);
        Assert.Equal(GpuPrimitiveTopology.TriangleList, pipeline.Topology);
        Assert.Equal(GpuBlendMode.StraightAlpha, pipeline.Blend);
        Assert.Equal(GpuDepthState.TranslucentDefault, pipeline.Depth);
        Assert.Equal(GpuCullMode.None, pipeline.Cull);
        Assert.Equal(4, pipeline.SampleCount);
        Assert.Same(GroundShapeBatch.VertexLayout, pipeline.VertexLayout);
    }

    [Fact]
    public void AFlushDrawsEveryTriangleWithDepthWritesOff()
    {
        var device = new RecordingGpuDevice(ringCapacityBytes: 1024 * 1024);
        IGpuFrame frame = device.BeginFrame();
        using var batch = new GroundShapeBatch(device, new FixedFrame(frame), new FourSampleWorldPass());

        batch.Begin();
        batch.AddTriangle(new Vector3(1f, 2f, 3f), new Vector3(4f, 5f, 6f), new Vector3(7f, 8f, 9f), HalfOrange);
        using (IGpuPassEncoder encoder = frame.BeginPass(WorldPass(withDepth: true)))
            batch.Flush(encoder, Matrix4x4.Identity, 1280, 720);

        Assert.Contains(new GpuRecordedPipelineBind("ground-shape"), device.Calls);
        Assert.Contains(new GpuRecordedDepthWrite(false), device.Calls);
        Assert.Equal(3u, Assert.Single(device.Calls.OfType<GpuRecordedDraw>()).VertexCount);
        GpuRecordedRingAllocation upload = Assert.Single(device.Calls.OfType<GpuRecordedRingAllocation>());
        float[] floats = MemoryMarshal.Cast<byte, float>(
            device.RingBytes.Slice((int)upload.OffsetBytes, upload.ByteCount)).ToArray();
        Assert.Equal(3 * GroundShapeBatch.FloatsPerVertex, floats.Length);
        Assert.Equal(new[] { 1f, 2f, 3f, 1f, 0.5f, 0f, 0.25f }, floats[..7]);
    }

    [Fact]
    public void TrianglesPastTheBudgetAreLeftOut()
    {
        var device = new RecordingGpuDevice(ringCapacityBytes: 64 * 1024 * 1024);
        IGpuFrame frame = device.BeginFrame();
        using var batch = new GroundShapeBatch(device, new FixedFrame(frame), new FourSampleWorldPass());

        batch.Begin();
        for (int i = 0; i < (GroundShapeBatch.VertexBudget / 3) + 100; i++)
            batch.AddTriangle(Vector3.Zero, Vector3.UnitX, Vector3.UnitY, HalfOrange);
        Assert.InRange(batch.RemainingVertexBudget, 0, 2);
        using (IGpuPassEncoder encoder = frame.BeginPass(WorldPass(withDepth: true)))
            batch.Flush(encoder, Matrix4x4.Identity, 1280, 720);

        uint drawn = Assert.Single(device.Calls.OfType<GpuRecordedDraw>()).VertexCount;
        Assert.InRange((int)drawn, 1, GroundShapeBatch.VertexBudget);
    }

    [Fact]
    public void NothingIsDrawnWithoutDepthOrWithNothingAdded()
    {
        var device = new RecordingGpuDevice(ringCapacityBytes: 1024 * 1024);
        IGpuFrame frame = device.BeginFrame();
        using var batch = new GroundShapeBatch(device, new FixedFrame(frame), new FourSampleWorldPass());

        batch.Begin();
        using (IGpuPassEncoder encoder = frame.BeginPass(WorldPass(withDepth: true)))
            batch.Flush(encoder, Matrix4x4.Identity, 1280, 720);
        batch.AddTriangle(Vector3.Zero, Vector3.UnitX, Vector3.UnitY, HalfOrange);
        using (IGpuPassEncoder encoder = frame.BeginPass(WorldPass(withDepth: false)))
            batch.Flush(encoder, Matrix4x4.Identity, 1280, 720);

        Assert.Empty(device.Calls.OfType<GpuRecordedDraw>());
    }

    private static GpuPassDescription WorldPass(bool withDepth) => new()
    {
        Name = "vk-world",
        Color = new GpuColorAttachment(
            Target: null,
            Load: GpuLoadOp.Clear,
            Store: GpuStoreOp.Store,
            ClearColor: default),
        Depth = withDepth
            ? new GpuDepthAttachment(
                Load: GpuLoadOp.Clear,
                Store: GpuStoreOp.DontCare,
                ClearDepth: 1f,
                ClearStencil: 0)
            : null,
        SampleCount = 4,
    };

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
