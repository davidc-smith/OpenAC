using System.Collections.Immutable;
using System.Numerics;
using System.Reflection;
using AcDream.App.Rendering;
using AcDream.App.Rendering.Gpu;
using ImGuiNET;

namespace AcDream.App.Extensions.Overlay;

/// <summary>
/// Draws ImGui through OpenAC's GPU layer: one single-sample pass onto the
/// backbuffer after everything else, straight-alpha blending, no depth, a
/// scissor per command. The font atlas is rasterised at the framebuffer scale
/// (sharp on Retina) and drawn back at 1/scale, and rebuilt when the scale changes.
/// </summary>
internal sealed unsafe class ImGuiVulkanRenderer : IImGuiDrawRenderer
{
    internal const float FontSizePoints = 13f;

    internal static readonly GpuVertexLayout VertexLayout = GpuVertexLayout.Interleaved(
        strideBytes: 20,
        [
            new GpuVertexAttribute(0, GpuVertexFormat.Float2, 0),
            new GpuVertexAttribute(1, GpuVertexFormat.Float2, 8),
            new GpuVertexAttribute(2, GpuVertexFormat.UByte4Normalized, 16),
        ]);

    private readonly IGpuDevice _device;
    private readonly ICurrentGpuFrameSource _frames;
    private readonly IGpuPipeline _pipeline;
    private IGpuTexture? _atlas;
    private GpuTextureSlot _atlasSlot = GpuTextureSlot.Unassigned;
    private float _atlasScale;

    public ImGuiVulkanRenderer(IGpuDevice device, ICurrentGpuFrameSource frames)
    {
        _device = device ?? throw new ArgumentNullException(nameof(device));
        _frames = frames ?? throw new ArgumentNullException(nameof(frames));
        _pipeline = device.CreatePipeline(new GpuPipelineDescription
        {
            Name = "imgui-overlay",
            Shaders = new GpuShaderSet(
                "imgui-overlay",
                LoadSpirv("AcDream.App.Overlay.imgui_overlay.vert.spv"),
                LoadSpirv("AcDream.App.Overlay.imgui_overlay.frag.spv")),
            VertexLayout = VertexLayout,
            Topology = GpuPrimitiveTopology.TriangleList,
            Blend = GpuBlendMode.StraightAlpha,
            Depth = GpuDepthState.Disabled,
            Cull = GpuCullMode.None,
            SampleCount = 1,
        });
    }

    internal static byte[] LoadSpirv(string resourceName)
    {
        using Stream stream = typeof(ImGuiVulkanRenderer).Assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"missing embedded shader {resourceName}");
        using var memory = new MemoryStream();
        stream.CopyTo(memory);
        return memory.ToArray();
    }

    public void PrepareFrame(ImGuiIOPtr io, Vector2 framebufferScale)
    {
        float scale = MathF.Max(1f, framebufferScale.X);
        if (_atlas is not null && Math.Abs(scale - _atlasScale) < 0.01f)
            return;

        ReleaseAtlas();
        io.Fonts.Clear();
        ImFontConfigPtr config = ImGuiNative.ImFontConfig_ImFontConfig();
        config.SizePixels = FontSizePoints * scale;
        io.Fonts.AddFontDefault(config);
        config.Destroy();
        io.FontGlobalScale = 1f / scale;
        io.Fonts.GetTexDataAsRGBA32(out IntPtr pixels, out int width, out int height, out int bytesPerPixel);

        IGpuTexture texture = _device.CreateTexture(new GpuTextureDescription(
            "imgui-font-atlas", GpuTextureKind.Texture2D, GpuTextureFormat.Rgba8Unorm, width, height, 1, 1));
        try
        {
            texture.Upload(0, 0, new ReadOnlySpan<byte>((void*)pixels, width * height * bytesPerPixel));
            _atlasSlot = _device.RegisterTexture(texture, _device.CreateSampler(GpuSamplerDescription.WorldClamp));
        }
        catch
        {
            texture.Dispose();
            throw;
        }

        _atlas = texture;
        _atlasScale = scale;
        io.Fonts.SetTexID((IntPtr)_atlasSlot.Index);
        io.Fonts.ClearTexData();
    }

    public void Render(ImDrawDataPtr drawData, Vector2 framebufferSize)
    {
        IGpuFrame? frame = _frames.CurrentFrame;
        if (frame is null || drawData.CmdListsCount == 0 || drawData.TotalVtxCount == 0)
            return;

        int fbWidth = (int)framebufferSize.X;
        int fbHeight = (int)framebufferSize.Y;
        if (fbWidth <= 0 || fbHeight <= 0)
            return;

        int vertexBytes = drawData.TotalVtxCount * sizeof(ImDrawVert);
        int indexBytes = drawData.TotalIdxCount * sizeof(ushort);
        GpuRingAllocation vertices = frame.AllocateRing(vertexBytes, GpuRingUsage.Vertex);
        GpuRingAllocation indices = frame.AllocateRing(indexBytes, GpuRingUsage.Index);
        int vtxWritten = 0;
        int idxWritten = 0;
        for (int n = 0; n < drawData.CmdListsCount; n++)
        {
            ImDrawListPtr list = drawData.CmdLists[n];
            var vtx = new ReadOnlySpan<byte>((void*)list.VtxBuffer.Data, list.VtxBuffer.Size * sizeof(ImDrawVert));
            var idx = new ReadOnlySpan<byte>((void*)list.IdxBuffer.Data, list.IdxBuffer.Size * sizeof(ushort));
            vtx.CopyTo(vertices.Data[vtxWritten..]);
            idx.CopyTo(indices.Data[idxWritten..]);
            vtxWritten += vtx.Length;
            idxWritten += idx.Length;
        }

        using IGpuPassEncoder encoder = frame.BeginPass(new GpuPassDescription
        {
            Name = "imgui-overlay",
            Color = new GpuColorAttachment(Target: null, Load: GpuLoadOp.Load, Store: GpuStoreOp.Store, ClearColor: default),
            Depth = null,
            SampleCount = 1,
        });
        encoder.BindPipeline(_pipeline);
        encoder.BindVertexBuffer(0, vertices.Buffer, vertices.OffsetBytes);
        encoder.BindIndexBuffer(indices.Buffer, indices.OffsetBytes, GpuIndexType.UInt16);

        GpuPushConstants constants = GpuPushConstants.Default;
        constants.ParamA = drawData.DisplaySize.X;
        constants.ParamB = drawData.DisplaySize.Y;
        uint boundTexture = uint.MaxValue;
        int listVtxBase = 0;
        int listIdxBase = 0;
        for (int n = 0; n < drawData.CmdListsCount; n++)
        {
            ImDrawListPtr list = drawData.CmdLists[n];
            for (int c = 0; c < list.CmdBuffer.Size; c++)
            {
                ImDrawCmdPtr cmd = list.CmdBuffer[c];
                if (cmd.UserCallback != IntPtr.Zero || cmd.ElemCount == 0)
                    continue;
                if (!ImGuiScissor.TryMap(cmd.ClipRect, drawData.DisplayPos, drawData.FramebufferScale, fbWidth, fbHeight, out var s))
                    continue;
                uint texture = (uint)cmd.TextureId;
                if (texture != boundTexture)
                {
                    constants.TextureIndexA = texture;
                    encoder.SetPushConstants(constants);
                    boundTexture = texture;
                }

                encoder.SetScissor(s.X, s.Y, s.Width, s.Height);
                encoder.DrawIndexed(cmd.ElemCount, 1, (uint)(listIdxBase + cmd.IdxOffset), listVtxBase + (int)cmd.VtxOffset, 0);
            }

            listVtxBase += list.VtxBuffer.Size;
            listIdxBase += list.IdxBuffer.Size;
        }
    }

    public void Dispose()
    {
        ReleaseAtlas();
        _pipeline.Dispose();
    }

    private void ReleaseAtlas()
    {
        if (_atlasSlot.IsAssigned)
            _device.ReleaseTextureSlot(_atlasSlot);
        _atlasSlot = GpuTextureSlot.Unassigned;
        _atlas?.Dispose();
        _atlas = null;
    }
}
