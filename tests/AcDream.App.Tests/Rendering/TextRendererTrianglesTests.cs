using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using AcDream.App.Rendering;
using AcDream.App.Rendering.Gpu;
using AcDream.App.Tests.Rendering.Gpu;

namespace AcDream.App.Tests.Rendering;

/// <summary>
/// Untextured coloured triangles join the interface's untextured sprite
/// run, so they keep painter's order with fills and images, and each
/// corner keeps its own colour.
/// </summary>
public sealed class TextRendererTrianglesTests
{
    private sealed class FrameSource : ICurrentGpuFrameSource
    {
        public IGpuFrame? CurrentFrame { get; set; }
    }

    private static readonly Vector4 Red = new(1f, 0f, 0f, 1f);
    private static readonly Vector4 Blue = new(0f, 0f, 1f, 0.5f);

    private static UiColorVertex[] Triangle(float x) =>
    [
        new(new Vector2(x, 0f), Red),
        new(new Vector2(x + 10f, 0f), Blue),
        new(new Vector2(x, 10f), Red),
    ];

    private static TextRenderer Renderer(RecordingGpuDevice device)
    {
        var renderer = new TextRenderer(device, new FrameSource(), "unused");
        renderer.Begin(new Vector2(64f, 64f));
        return renderer;
    }

    [Fact]
    public void TrianglesJoinTheUntexturedRunAndKeepPaintersOrder()
    {
        using var device = new RecordingGpuDevice();
        using TextRenderer renderer = Renderer(device);

        renderer.DrawFill(0f, 0f, 10f, 10f, Vector4.One);
        renderer.DrawTriangles(Triangle(0f));
        renderer.DrawSprite(7u, 0f, 0f, 10f, 10f, 0f, 0f, 1f, 1f, Vector4.One);
        renderer.DrawTriangles(Triangle(20f));

        Assert.Equal([0u, 7u, 0u], renderer.DebugSpriteSegments.Select(run => run.Texture).ToArray());
        Assert.Equal([9, 6, 3], renderer.DebugSpriteSegments.Select(run => run.VertexCount).ToArray());
    }

    [Fact]
    public void EachCornerKeepsItsOwnColourAndNoTextureCoordinate()
    {
        using var device = new RecordingGpuDevice();
        using TextRenderer renderer = Renderer(device);

        renderer.DrawTriangles(Triangle(5f));

        (uint texture, IReadOnlyList<float> verts) = Assert.Single(renderer.DebugSpriteSegmentVerts);
        Assert.Equal(UiTextureTableHandle.None, texture);
        Assert.Equal(
            [5f, 0f, 0f, 0f, 1f, 0f, 0f, 1f,
             15f, 0f, 0f, 0f, 0f, 0f, 1f, 0.5f,
             5f, 10f, 0f, 0f, 1f, 0f, 0f, 1f],
            verts.ToArray());
    }

    [Fact]
    public void TheFixedCanvasScaleStretchesTrianglesLikeEverythingElse()
    {
        using var device = new RecordingGpuDevice();
        using TextRenderer renderer = Renderer(device);
        renderer.CanvasScale = new Vector2(2f, 3f);

        renderer.DrawTriangles(Triangle(5f));

        float[] verts = Assert.Single(renderer.DebugSpriteSegmentVerts).Verts.ToArray();
        Assert.Equal((10f, 0f), (verts[0], verts[1]));
        Assert.Equal((30f, 0f), (verts[8], verts[9]));
        Assert.Equal((10f, 30f), (verts[16], verts[17]));
    }

    [Fact]
    public void AListThatIsNotWholeTrianglesIsRefused()
    {
        using var device = new RecordingGpuDevice();
        using TextRenderer renderer = Renderer(device);
        UiColorVertex[] two = Triangle(0f)[..2];

        Assert.Throws<ArgumentException>(() => renderer.DrawTriangles(two));
    }
}
