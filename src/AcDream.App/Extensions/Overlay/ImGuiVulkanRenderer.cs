using System.Numerics;
using AcDream.App.Rendering;
using AcDream.App.Rendering.Gpu;
using ImGuiNET;

namespace AcDream.App.Extensions.Overlay;

/// <summary>
/// Draws ImGui through OpenAC's GPU layer: one single-sample pass onto the
/// backbuffer after everything else, straight-alpha blending, no depth, a
/// scissor per command. The font atlas is rasterised at the framebuffer scale
/// (sharp on Retina) with its sizes in points, and rebuilt when the scale changes.
/// </summary>
internal sealed unsafe class ImGuiVulkanRenderer : IImGuiDrawRenderer
{
    internal const float FontSizePoints = 13f;

    /// <summary>Largest vertex + index upload drawn in one frame; bigger frames are skipped.</summary>
    internal const int MaxFrameUploadBytes = 4 * 1024 * 1024;

    /// <summary>How many distinct bad texture ids are logged before going quiet.</summary>
    private const int MaxReportedBadTextures = 16;

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
    private readonly Action<string>? _log;
    private readonly OverlayFonts? _fonts;
    private readonly Func<IntPtr, bool>? _issued;
    private int _fontsVersion = -1;
    private readonly HashSet<IntPtr> _reportedBadTextures = [];
    private bool _reportedOversize;
    private bool _reportedRingFull;
    private bool _reportedCallback;

    public ImGuiVulkanRenderer(
        IGpuDevice device,
        ICurrentGpuFrameSource frames,
        Action<string>? log = null,
        OverlayFonts? fonts = null,
        Func<IntPtr, bool>? issued = null)
    {
        _device = device ?? throw new ArgumentNullException(nameof(device));
        _frames = frames ?? throw new ArgumentNullException(nameof(frames));
        _log = log;
        _fonts = fonts;
        _issued = issued;
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

    /// <summary>
    /// The bindless slot for an ImGui texture id. Ids are UI texture handles
    /// (slot + 1): the font atlas's, or an icon the overlay's icon service
    /// handed out. Anything else, such as a foreign plugin pointer, 0 or a
    /// stale id, would index the bindless table out of range, so it is never drawn.
    /// </summary>
    internal static bool TryGetDrawSlot(IntPtr textureId, GpuTextureSlot atlas, Func<IntPtr, bool>? issued, out uint slot)
    {
        slot = 0;
        long value = textureId.ToInt64();
        if (value <= 0 || value > uint.MaxValue)
            return false;
        uint handle = (uint)value;
        if (handle != UiTextureTableHandle.FromSlot(atlas) && issued?.Invoke(textureId) != true)
            return false;
        slot = UiTextureTableHandle.ToSlot(handle).Index;
        return true;
    }

    /// <summary>
    /// Clears the atlas and adds the overlay's font and the merged fonts, sized in
    /// points and rasterised at <paramref name="scale"/> pixels per point. The font's
    /// own size stays in points, so code that draws with <c>font.FontSize</c> (as
    /// RynthCore's faces do) gets the same size as ImGui's widgets.
    /// </summary>
    internal static void AddFonts(ImFontAtlasPtr atlas, OverlayFonts? fonts, float scale)
    {
        atlas.Clear();
        fonts?.ClearBaked();
        AddFont(atlas, fonts, FontSizePoints, scale);
        if (fonts is null)
            return;
        foreach (float points in fonts.SizesAsked)
            AddFont(atlas, fonts, points, scale);
    }

    // The default font at one size, with the merged fonts on it.
    private static void AddFont(ImFontAtlasPtr atlas, OverlayFonts? fonts, float points, float scale)
    {
        ImFontConfigPtr config = ImGuiNative.ImFontConfig_ImFontConfig();
        config.SizePixels = points;
        config.RasterizerDensity = scale;
        config.OversampleH = 1;
        config.OversampleV = 1;
        config.PixelSnapH = true;
        ImFontPtr font = atlas.AddFontDefault(config);
        config.Destroy();
        fonts?.MergeInto(atlas, points, scale);
        fonts?.Baked(points, font);
    }

    public void PrepareFrame(ImGuiIOPtr io, Vector2 framebufferScale)
    {
        float scale = MathF.Max(1f, framebufferScale.X);
        int fontsVersion = _fonts?.Version ?? 0;
        if (_atlas is not null && Math.Abs(scale - _atlasScale) < 0.01f && fontsVersion == _fontsVersion)
            return;

        ReleaseAtlas();
        AddFonts(io.Fonts, _fonts, scale);
        io.FontGlobalScale = 1f;
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
        _log?.Invoke($"[ImGui] font atlas {width}x{height} at scale {scale}");
        _atlasScale = scale;
        _fontsVersion = fontsVersion;
        io.Fonts.SetTexID((IntPtr)UiTextureTableHandle.FromSlot(_atlasSlot));
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
        if ((long)vertexBytes + indexBytes > MaxFrameUploadBytes)
        {
            if (!_reportedOversize)
            {
                _reportedOversize = true;
                _log?.Invoke($"[ImGui] skipping a frame of {vertexBytes + (long)indexBytes} bytes (limit {MaxFrameUploadBytes}).");
            }

            return;
        }

        GpuRingAllocation vertices;
        GpuRingAllocation indices;
        try
        {
            vertices = frame.AllocateRing(vertexBytes, GpuRingUsage.Vertex);
            indices = frame.AllocateRing(indexBytes, GpuRingUsage.Index);
        }
        catch (InvalidOperationException error)
        {
            if (!_reportedRingFull)
            {
                _reportedRingFull = true;
                _log?.Invoke($"[ImGui] skipping a frame: upload ring full ({error.Message}).");
            }

            return;
        }

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
                if (cmd.UserCallback != IntPtr.Zero)
                {
                    if (!_reportedCallback)
                    {
                        _reportedCallback = true;
                        _log?.Invoke("[ImGui] draw callbacks are not supported; skipping them.");
                    }

                    continue;
                }

                if (cmd.ElemCount == 0)
                    continue;
                if (!TryGetDrawSlot(cmd.TextureId, _atlasSlot, _issued, out uint texture))
                {
                    ReportBadTexture(cmd.TextureId);
                    continue;
                }

                if (!ImGuiScissor.TryMap(cmd.ClipRect, drawData.DisplayPos, drawData.FramebufferScale, fbWidth, fbHeight, out var s))
                    continue;
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

    private void ReportBadTexture(IntPtr textureId)
    {
        if (_reportedBadTextures.Count >= MaxReportedBadTextures || !_reportedBadTextures.Add(textureId))
            return;
        _log?.Invoke($"[ImGui] skipping draws with unknown texture id 0x{textureId:X}; only the font atlas and client icons can be drawn.");
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
