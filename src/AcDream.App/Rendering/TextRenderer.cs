using System;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.InteropServices;
using AcDream.App.Rendering.Gpu;

namespace AcDream.App.Rendering;

/// <summary>
/// Which of a frame's three draw layers a draw lands in. The layers are
/// drawn in this order, and each one draws its sprite runs, then its
/// rectangles, then its bitmap-font text, so text never shows through
/// anything in a later layer.
/// </summary>
internal enum UiDrawLayer
{
    /// <summary>The world overlays, plugin canvases under windows, and every window.</summary>
    Main,

    /// <summary>
    /// Everything from the plugin canvases drawn over windows upwards:
    /// those canvases, pre-game screens, dialogs and tooltips.
    /// </summary>
    Upper,

    /// <summary>The overlay pass: menus, popups and the drag ghost.</summary>
    Overlay,
}

public sealed class TextRenderer : IDisposable
{
    internal const int FloatsPerVertex = 8;
    private const int VertexStrideBytes = FloatsPerVertex * sizeof(float);

    internal static readonly GpuVertexLayout SpriteVertexLayout = GpuVertexLayout.Interleaved(
        strideBytes: VertexStrideBytes,
        [
            new GpuVertexAttribute(0, GpuVertexFormat.Float2, 0),
            new GpuVertexAttribute(1, GpuVertexFormat.Float2, 8),
            new GpuVertexAttribute(2, GpuVertexFormat.Float4, 16),
        ]);

    private readonly ICurrentGpuFrameSource _frameSource;
    private readonly IGpuPipeline _pipeline;

    private sealed class SpriteSeg { public uint Texture; public readonly List<float> Verts = new(256); }

    /// <summary>What one <see cref="UiDrawLayer"/> collected this frame.</summary>
    private sealed class DrawLayerBuffers(int textCapacity, int rectCapacity)
    {
        public readonly List<float> Text = new(textCapacity);
        public readonly List<float> Rects = new(rectCapacity);
        public readonly List<SpriteSeg> SpriteSegs = new();
        public int SegUsed;
        public int TextVerts;
        public int RectVerts;

        public bool HasAnything => SegUsed > 0 || TextVerts > 0 || RectVerts > 0;

        public void Clear()
        {
            Text.Clear();
            Rects.Clear();
            SegUsed = 0; // pool the SpriteSeg objects across frames
            TextVerts = 0;
            RectVerts = 0;
        }
    }

    // Indexed by UiDrawLayer, and drawn in that order.
    private readonly DrawLayerBuffers[] _layers =
    [
        new(textCapacity: 8192, rectCapacity: 1024),
        new(textCapacity: 1024, rectCapacity: 256),
        new(textCapacity: 1024, rectCapacity: 256),
    ];
    private Vector2 _screenSize;

    private DrawLayerBuffers Current => _layers[(int)Layer];

    /// <summary>The sprite runs below the overlay pass, main layer first, as the frame draws them.</summary>
    private IEnumerable<SpriteSeg> DebugSegs()
    {
        for (int layer = (int)UiDrawLayer.Main; layer <= (int)UiDrawLayer.Upper; layer++)
        {
            DrawLayerBuffers buffers = _layers[layer];
            for (int i = 0; i < buffers.SegUsed; i++)
                yield return buffers.SpriteSegs[i];
        }
    }

    internal long DynamicBufferCapacityBytes => 0;

    internal IReadOnlyList<(uint Texture, int VertexCount, float Alpha)> DebugSpriteSegments
    {
        get
        {
            var result = new List<(uint, int, float)>();
            foreach (SpriteSeg seg in DebugSegs())
            {
                float alpha = seg.Verts.Count > 0 ? seg.Verts[7] : 0f;
                result.Add((seg.Texture, seg.Verts.Count / FloatsPerVertex, alpha));
            }
            return result;
        }
    }

    internal IReadOnlyList<(uint Texture, IReadOnlyList<float> Verts)> DebugSpriteSegmentVerts
    {
        get
        {
            var result = new List<(uint, IReadOnlyList<float>)>();
            foreach (SpriteSeg seg in DebugSegs())
                result.Add((seg.Texture, seg.Verts.ToArray()));
            return result;
        }
    }

    internal (int VertexCount, float Alpha) DebugTextBuffer
    {
        get
        {
            DrawLayerBuffers main = _layers[(int)UiDrawLayer.Main];
            DrawLayerBuffers upper = _layers[(int)UiDrawLayer.Upper];
            List<float> first = main.Text.Count > 0 ? main.Text : upper.Text;
            return (main.TextVerts + upper.TextVerts, first.Count > 0 ? first[7] : 0f);
        }
    }

    internal int DebugRectVertexCount =>
        _layers[(int)UiDrawLayer.Main].RectVerts + _layers[(int)UiDrawLayer.Upper].RectVerts;

    /// <summary>Where draws land; back to <see cref="UiDrawLayer.Main"/> at every <see cref="Begin"/>.</summary>
    internal UiDrawLayer Layer { get; set; }

    /// <summary>Whether draws land in the overlay layer; false puts them back in the main layer.</summary>
    public bool OverlayMode
    {
        get => Layer == UiDrawLayer.Overlay;
        set => Layer = value ? UiDrawLayer.Overlay : UiDrawLayer.Main;
    }

    internal TextRenderer(IGpuDevice device, ICurrentGpuFrameSource frameSource, string shaderDir)
    {
        ArgumentNullException.ThrowIfNull(device);
        _frameSource = frameSource ?? throw new ArgumentNullException(nameof(frameSource));
        ArgumentException.ThrowIfNullOrWhiteSpace(shaderDir);

        _pipeline = device.CreatePipeline(new GpuPipelineDescription
        {
            Name = "ui-text",
            Shaders = new GpuShaderSet("ui_text"),
            VertexLayout = SpriteVertexLayout,
            Topology = GpuPrimitiveTopology.TriangleList,
            Blend = GpuBlendMode.StraightAlpha,
            Depth = GpuDepthState.Disabled,
            Cull = GpuCullMode.None,
            AlphaToCoverage = false,
            ColorWrite = true,
            SampleCount = 1,
        });
    }

    internal Vector2 CanvasScale = Vector2.One;

    internal Func<uint, uint>? LinearTwinResolver { get; set; }

    public void Begin(Vector2 screenSize)
    {
        _screenSize = screenSize;
        foreach (DrawLayerBuffers layer in _layers)
            layer.Clear();
        Layer = UiDrawLayer.Main;
    }

    public void DrawRect(float x, float y, float w, float h, Vector4 color)
    {
        DrawLayerBuffers layer = Current;
        AppendQuad(layer.Rects, x, y, w, h, 0, 0, 0, 0, color);
        layer.RectVerts += 6;
    }

    public void DrawFill(float x, float y, float w, float h, Vector4 color)
        => DrawSprite(UiTextureTableHandle.None, x, y, w, h, 0f, 0f, 1f, 1f, color);

    public void DrawRectOutline(float x, float y, float w, float h, Vector4 color, float thickness = 1f)
    {
        // top, bottom, left, right
        DrawRect(x, y, w, thickness, color);
        DrawRect(x, y + h - thickness, w, thickness, color);
        DrawRect(x, y, thickness, h, color);
        DrawRect(x + w - thickness, y, thickness, h, color);
    }

    /// <summary>
    /// Draw a single line of text at (x,y) where (x,y) is the top-left of the
    /// typographic block. Handles '\n' as a line break.
    /// </summary>
    public void DrawString(BitmapFont font, string text, float x, float y, Vector4 color)
        => DrawStringCore(
            font, text, x, y, color,
            clip: false, 0f, 0f, 0f, 0f);

    internal void DrawStringClipped(
        BitmapFont font,
        string text,
        float x,
        float y,
        Vector4 color,
        float clipLeft,
        float clipTop,
        float clipRight,
        float clipBottom)
        => DrawStringCore(
            font, text, x, y, color,
            clip: true, clipLeft, clipTop, clipRight, clipBottom);

    private void DrawStringCore(
        BitmapFont font,
        string text,
        float x,
        float y,
        Vector4 color,
        bool clip,
        float clipLeft,
        float clipTop,
        float clipRight,
        float clipBottom)
    {
        float cursorX = x;
        float baseline = y + font.Ascent;

        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (c == '\n')
            {
                cursorX = x;
                baseline += font.LineHeight;
                continue;
            }
            if (!font.TryGetGlyph(c, out var g))
            {
                // Unknown glyph — skip its advance width if '?' exists.
                if (font.TryGetGlyph('?', out var q))
                    cursorX += q.Advance;
                continue;
            }

            float gx = cursorX + g.OffsetX;
            float gy = baseline + g.OffsetY;
            float gw = g.Width;
            float gh = g.Height;
            float u0 = g.UvMinX;
            float v0 = g.UvMinY;
            float u1 = g.UvMaxX;
            float v1 = g.UvMaxY;

            if (gw > 0 && gh > 0
                && (!clip || QuadClipper.TryClip(
                    clipLeft, clipTop, clipRight, clipBottom,
                    ref gx, ref gy, ref gw, ref gh,
                    ref u0, ref v0, ref u1, ref v1)))
            {
                DrawLayerBuffers layer = Current;
                AppendQuad(layer.Text, gx, gy, gw, gh, u0, v0, u1, v1, color);
                layer.TextVerts += 6;
            }
            cursorX += g.Advance;
        }
    }

    public void DrawSprite(uint texture, float x, float y, float w, float h,
        float u0, float v0, float u1, float v1, Vector4 tint)
    {
        if (CanvasScale != Vector2.One && LinearTwinResolver is { } resolve)
            texture = resolve(texture);

        DrawLayerBuffers layer = Current;
        SpriteSeg seg = NextSpriteSeg(layer.SpriteSegs, ref layer.SegUsed, texture);
        AppendQuad(seg.Verts, x, y, w, h, u0, v0, u1, v1, tint);
    }

    /// <summary>
    /// Append a convex outline of three to eight corners as a triangle fan.
    /// This is the path for anything that is not an upright rectangle -- a
    /// thick line, a rotated or scaled blit, either of those after clipping.
    ///
    /// <para>It lands in the same per-texture run as <see cref="DrawSprite"/>,
    /// so a rotated blit sandwiched between two upright ones of the same
    /// texture still costs one draw call, and an untextured line drawn next to
    /// untextured fills joins their run.</para>
    /// </summary>
    internal void DrawConvexPolygon(uint texture, ReadOnlySpan<UiQuadVertex> polygon, Vector4 color)
    {
        if (polygon.Length < 3)
            return;
        if (CanvasScale != Vector2.One && LinearTwinResolver is { } resolve)
            texture = resolve(texture);

        DrawLayerBuffers layer = Current;
        SpriteSeg seg = NextSpriteSeg(layer.SpriteSegs, ref layer.SegUsed, texture);

        // Fan from the first corner: (0,1,2), (0,2,3), ... The pipeline takes a
        // plain triangle list, so the fan is written out as separate triangles.
        for (int i = 1; i + 1 < polygon.Length; i++)
        {
            AppendVertex(seg.Verts, polygon[0], color);
            AppendVertex(seg.Verts, polygon[i], color);
            AppendVertex(seg.Verts, polygon[i + 1], color);
        }
    }

    private void AppendVertex(List<float> buf, in UiQuadVertex vertex, Vector4 color)
    {
        float px = vertex.Position.X;
        float py = vertex.Position.Y;
        if (CanvasScale != Vector2.One)
        {
            px *= CanvasScale.X;
            py *= CanvasScale.Y;
        }
        buf.Add(px); buf.Add(py);
        buf.Add(vertex.Uv.X); buf.Add(vertex.Uv.Y);
        buf.Add(color.X); buf.Add(color.Y); buf.Add(color.Z); buf.Add(color.W);
    }

    internal static uint ResolveExternalTextureSlot(GpuTextureSlot slot) =>
        UiTextureTableHandle.FromSlot(slot);

    private static SpriteSeg NextSpriteSeg(List<SpriteSeg> segs, ref int used, uint texture)
    {
        if (used > 0 && segs[used - 1].Texture == texture)
            return segs[used - 1];
        if (used < segs.Count)
        {
            var s = segs[used++];
            s.Texture = texture;
            s.Verts.Clear();
            return s;
        }
        var ns = new SpriteSeg { Texture = texture };
        segs.Add(ns);
        used++;
        return ns;
    }

    private void AppendQuad(List<float> buf,
        float x, float y, float w, float h,
        float u0, float v0, float u1, float v1, Vector4 color)
    {
        if (CanvasScale != Vector2.One)
        {
            x *= CanvasScale.X;
            y *= CanvasScale.Y;
            w *= CanvasScale.X;
            h *= CanvasScale.Y;
        }
        void V(float px, float py, float pu, float pv)
        {
            buf.Add(px); buf.Add(py);
            buf.Add(pu); buf.Add(pv);
            buf.Add(color.X); buf.Add(color.Y); buf.Add(color.Z); buf.Add(color.W);
        }
        V(x,     y,     u0, v0);
        V(x + w, y + h, u1, v1);
        V(x + w, y,     u1, v0);
        V(x,     y,     u0, v0);
        V(x,     y + h, u0, v1);
        V(x + w, y + h, u1, v1);
    }

    /// <summary>Upload + draw accumulated rects + text. font may be null if only DrawRect was used.</summary>
    public void Flush(BitmapFont? font)
    {
        if (!HasAnythingToDraw) return;
        FlushInto(
            new GpuPassDescription
            {
                Name = "ui-text",
                Color = new GpuColorAttachment(
                    Target: null,
                    Load: GpuLoadOp.Load,
                    Store: GpuStoreOp.Store,
                    ClearColor: default),
                Depth = null,
                SampleCount = 1,
            },
            "ui-text",
            font);
    }

    /// <summary>
    /// Draws what was collected into an off-screen target instead of the
    /// frame, clearing the target first. The projection is whatever
    /// <see cref="Begin"/> was given, so the caller begins with the target's
    /// size. Unlike <see cref="Flush"/> this always opens the pass, because
    /// a target that collected nothing still has to be cleared: the caller
    /// asked for a repaint and expects an empty surface, not the last one.
    /// </summary>
    internal void FlushTo(IGpuRenderTarget target, Vector4 clearColor, BitmapFont? font, string passName)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentException.ThrowIfNullOrWhiteSpace(passName);
        FlushInto(
            new GpuPassDescription
            {
                Name = passName,
                Color = new GpuColorAttachment(
                    Target: target,
                    Load: GpuLoadOp.Clear,
                    Store: GpuStoreOp.Store,
                    ClearColor: clearColor),
                Depth = null,
                SampleCount = 1,
            },
            passName,
            font);
    }

    private bool HasAnythingToDraw => Array.Exists(_layers, static layer => layer.HasAnything);

    private void FlushInto(GpuPassDescription pass, string stageName, BitmapFont? font)
    {
        IGpuFrame frame = _frameSource.CurrentFrame
            ?? throw new InvalidOperationException(
                "TextRenderer.Flush requires an open IGpuFrame (see GpuDeviceFrameLifetime) — " +
                "the host must drive IGpuDevice.BeginFrame() before rendering the retained UI.");

        using IGpuPassEncoder encoder = frame.BeginPass(pass);
        using IDisposable? stage = AcDream.App.Diagnostics.GpuStageProfiler.Measure(
            encoder, stageName);
        encoder.BindPipeline(_pipeline);

        foreach (DrawLayerBuffers layer in _layers)
            DrawLayer(layer.SpriteSegs, layer.SegUsed, layer.Rects, layer.RectVerts, layer.Text, layer.TextVerts, font, frame, encoder);
    }

    private void DrawLayer(
        List<SpriteSeg> spriteSegs, int segUsed,
        List<float> rectBuf, int rectVerts,
        List<float> textBuf, int textVerts, BitmapFont? font,
        IGpuFrame frame, IGpuPassEncoder encoder)
    {
        if (segUsed > 0)
        {
            for (int i = 0; i < segUsed; i++)
            {
                var seg = spriteSegs[i];
                if (seg.Verts.Count == 0) continue;
                SetTextures(encoder, colorHandle: seg.Texture, coverageHandle: UiTextureTableHandle.None);
                DrawRing(frame, encoder, seg.Verts);
            }
        }

        if (rectVerts > 0)
        {
            SetTextures(encoder, UiTextureTableHandle.None, UiTextureTableHandle.None);
            DrawRing(frame, encoder, rectBuf);
        }

        if (textVerts > 0 && font is not null)
        {
            SetTextures(encoder, UiTextureTableHandle.None, coverageHandle: font.TextureId);
            DrawRing(frame, encoder, textBuf);
        }
    }

    private void SetTextures(IGpuPassEncoder encoder, uint colorHandle, uint coverageHandle)
    {
        GpuPushConstants constants = GpuPushConstants.Default;
        constants.ParamA = _screenSize.X;
        constants.ParamB = _screenSize.Y;
        constants.TextureIndexA = UiTextureTableHandle.ToSlot(colorHandle).Index;
        constants.TextureIndexB = UiTextureTableHandle.ToSlot(coverageHandle).Index;
        encoder.SetPushConstants(constants);
    }

    private static void DrawRing(IGpuFrame frame, IGpuPassEncoder encoder, List<float> buf)
    {
        if (buf.Count == 0)
            return;
        GpuRingAllocation allocation = frame.AllocateRing(buf.Count * sizeof(float), GpuRingUsage.Vertex);
        CollectionsMarshal.AsSpan(buf).CopyTo(allocation.AsSpan<float>());
        encoder.BindVertexBuffer(0, allocation.Buffer, allocation.OffsetBytes);
        encoder.Draw((uint)(buf.Count / FloatsPerVertex), 1, 0, 0);
    }

    public void Dispose() => _pipeline.Dispose();
}
