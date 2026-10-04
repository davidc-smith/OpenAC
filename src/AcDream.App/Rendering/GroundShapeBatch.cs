using System.Numerics;
using System.Runtime.InteropServices;
using AcDream.App.Rendering.Gpu;

namespace AcDream.App.Rendering;

/// <summary>
/// See-through coloured triangles drawn into the open world pass with its
/// depth: tested against the world, so walls and hills hide them, but never
/// written to it, so they never hide each other or what is drawn after them.
/// Plugins' ground shapes are drawn with it.
/// </summary>
internal sealed class GroundShapeBatch : IDisposable
{
    internal const int FloatsPerVertex = 7;
    private const int VertexStrideBytes = FloatsPerVertex * sizeof(float);

    /// <summary>
    /// The most vertices one batch holds: 2 MiB of them, an eighth of the
    /// frame's upload ring, which the rest of the frame shares and which
    /// cannot grow mid-frame. Past the budget, further triangles are left out.
    /// </summary>
    internal const int VertexBudget = 2 * 1024 * 1024 / VertexStrideBytes;

    internal static readonly GpuVertexLayout VertexLayout = GpuVertexLayout.Interleaved(
        strideBytes: VertexStrideBytes,
        [
            new GpuVertexAttribute(0, GpuVertexFormat.Float3, 0),
            new GpuVertexAttribute(1, GpuVertexFormat.Float4, 12),
        ]);

    private readonly ICurrentGpuFrameSource _frameSource;
    private readonly IGpuPipeline _pipeline;
    private readonly List<float> _buffer = new(4096);
    private int _vertexCount;

    internal GroundShapeBatch(IGpuDevice device, ICurrentGpuFrameSource frameSource, IWorldPassScope worldPass)
    {
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(worldPass);
        _frameSource = frameSource ?? throw new ArgumentNullException(nameof(frameSource));
        _pipeline = device.CreatePipeline(new GpuPipelineDescription
        {
            Name = "ground-shape",
            Shaders = new GpuShaderSet("ground_shape"),
            VertexLayout = VertexLayout,
            Topology = GpuPrimitiveTopology.TriangleList,
            Blend = GpuBlendMode.StraightAlpha,
            Depth = GpuDepthState.TranslucentDefault,
            Cull = GpuCullMode.None,
            AlphaToCoverage = false,
            ColorWrite = true,
            SampleCount = worldPass.SampleCount,
        });
    }

    /// <summary>How many more vertices this batch takes before it is full.</summary>
    internal int RemainingVertexBudget => VertexBudget - _vertexCount;

    internal void Begin()
    {
        _buffer.Clear();
        _vertexCount = 0;
    }

    /// <summary>Adds a triangle of one colour, or nothing once the budget is spent.</summary>
    internal void AddTriangle(Vector3 a, Vector3 b, Vector3 c, Vector4 color)
    {
        if (RemainingVertexBudget < 3)
            return;
        AddVertex(a, color);
        AddVertex(b, color);
        AddVertex(c, color);
    }

    private void AddVertex(Vector3 position, Vector4 color)
    {
        _buffer.Add(position.X); _buffer.Add(position.Y); _buffer.Add(position.Z);
        _buffer.Add(color.X); _buffer.Add(color.Y); _buffer.Add(color.Z); _buffer.Add(color.W);
        _vertexCount++;
    }

    /// <summary>
    /// Draws the triangles added since <see cref="Begin"/> into
    /// <paramref name="encoder"/>'s pass, which must have depth.
    /// </summary>
    internal void Flush(IGpuPassEncoder encoder, Matrix4x4 viewProjection, int width, int height)
    {
        if (_vertexCount == 0 || encoder.Pass.Depth is null)
            return;
        IGpuFrame frame = _frameSource.CurrentFrame
            ?? throw new InvalidOperationException("Ground shapes require an active frame.");
        encoder.BindPipeline(_pipeline);
        encoder.SetViewport(0, 0, width, height);
        encoder.SetScissor(0, 0, width, height);
        encoder.SetDepthWrite(false);
        encoder.SetStencil(GpuStencilState.Default);
        GpuPushConstants constants = GpuPushConstants.Default;
        constants.ViewProjection = viewProjection;
        encoder.SetPushConstants(constants);
        GpuRingAllocation allocation = frame.AllocateRing(_buffer.Count * sizeof(float), GpuRingUsage.Vertex);
        CollectionsMarshal.AsSpan(_buffer).CopyTo(allocation.AsSpan<float>());
        encoder.BindVertexBuffer(0, allocation.Buffer, allocation.OffsetBytes);
        encoder.Draw((uint)_vertexCount, 1, 0, 0);
    }

    public void Dispose() => _pipeline.Dispose();
}
