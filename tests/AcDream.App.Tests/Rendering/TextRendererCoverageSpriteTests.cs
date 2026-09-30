using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using AcDream.App.Rendering;
using AcDream.App.Rendering.Gpu;
using AcDream.App.Tests.Rendering.Gpu;
using AcDream.App.UI;

namespace AcDream.App.Tests.Rendering;

/// <summary>
/// Glyphs from a single-channel atlas are drawn in the sprite runs, so text
/// keeps its place among fills and images, and a run of glyphs from one atlas
/// is one draw.
/// </summary>
public sealed class TextRendererCoverageSpriteTests
{
    private sealed class FrameSource : ICurrentGpuFrameSource
    {
        public IGpuFrame? CurrentFrame { get; set; }
    }

    private const uint Atlas = 9u;

    [Fact]
    public void CoverageSpritesKeepTheirPlaceAndShareARunPerAtlas()
    {
        using var device = new RecordingGpuDevice();
        var frames = new FrameSource();
        using var renderer = new TextRenderer(device, frames, "unused");
        renderer.Begin(new Vector2(64f, 64f));

        renderer.DrawFill(0f, 0f, 10f, 10f, Vector4.One);
        renderer.DrawCoverageSprite(Atlas, 0f, 0f, 4f, 4f, 0f, 0f, 0.5f, 0.5f, Vector4.One);
        renderer.DrawCoverageSprite(Atlas, 4f, 0f, 4f, 4f, 0.5f, 0f, 1f, 0.5f, Vector4.One);
        renderer.DrawFill(0f, 20f, 10f, 10f, Vector4.One);

        Assert.Equal([UiTextureTableHandle.None, UiTextureTableHandle.None, UiTextureTableHandle.None],
            renderer.DebugSpriteSegments.Select(seg => seg.Texture).ToArray());
        Assert.Equal([UiTextureTableHandle.None, Atlas, UiTextureTableHandle.None],
            renderer.DebugSpriteSegmentCoverage);
        Assert.Equal([6, 12, 6], renderer.DebugSpriteSegments.Select(seg => seg.VertexCount).ToArray());

        device.Clear();
        using (IGpuFrame frame = device.BeginFrame())
        {
            frames.CurrentFrame = frame;
            renderer.Flush(null);
            frames.CurrentFrame = null;
        }

        GpuPushConstants[] constants = device.OfKind<GpuRecordedPushConstants>().Select(c => c.Constants).ToArray();
        Assert.Equal(3, constants.Length);
        Assert.Equal(GpuTextureSlot.Unassigned.Index, constants[1].TextureIndexA);
        Assert.Equal(UiTextureTableHandle.ToSlot(Atlas).Index, constants[1].TextureIndexB);
        Assert.Equal(GpuTextureSlot.Unassigned.Index, constants[0].TextureIndexB);
    }

    [Fact]
    public void ThroughTheContextACoverageSpriteIsClippedAndFaded()
    {
        using var device = new RecordingGpuDevice();
        using var renderer = new TextRenderer(device, new FrameSource(), "unused");
        renderer.Begin(new Vector2(64f, 64f));
        var context = new UiRenderContext(renderer, new Vector2(64f, 64f));

        context.PushClip(0f, 0f, 2f, 4f);
        context.PushAlpha(0.5f);
        context.DrawCoverageSprite(Atlas, 0f, 0f, 4f, 4f, 0f, 0f, 1f, 1f, Vector4.One);

        (uint _, IReadOnlyList<float> verts) = Assert.Single(renderer.DebugSpriteSegmentVerts);
        float[] xs = [.. Enumerable.Range(0, verts.Count / TextRenderer.FloatsPerVertex).Select(v => verts[v * TextRenderer.FloatsPerVertex])];
        float[] us = [.. Enumerable.Range(0, verts.Count / TextRenderer.FloatsPerVertex).Select(v => verts[v * TextRenderer.FloatsPerVertex + 2])];
        Assert.Equal(2f, xs.Max());
        Assert.Equal(0.5f, us.Max());
        Assert.Equal(0.5f, verts[7]);
    }
}
