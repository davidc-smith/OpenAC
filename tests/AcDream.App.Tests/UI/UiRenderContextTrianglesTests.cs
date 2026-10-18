using System.Linq;
using System.Numerics;
using AcDream.App.Rendering;
using AcDream.App.Rendering.Gpu;
using AcDream.App.Tests.Rendering.Gpu;
using AcDream.App.UI;

namespace AcDream.App.Tests.UI;

/// <summary>
/// Triangles drawn through the interface context follow the origin, the
/// alpha stack and the clip, as every other primitive does.
/// </summary>
public sealed class UiRenderContextTrianglesTests
{
    private sealed class FrameSource : ICurrentGpuFrameSource
    {
        public IGpuFrame? CurrentFrame { get; set; }
    }

    private static readonly Vector4 Red = new(1f, 0f, 0f, 1f);
    private static readonly Vector4 Blue = new(0f, 0f, 1f, 1f);

    private static (TextRenderer Renderer, UiRenderContext Context) Build()
    {
        var renderer = new TextRenderer(new RecordingGpuDevice(), new FrameSource(), "unused");
        renderer.Begin(new Vector2(100f, 100f));
        return (renderer, new UiRenderContext(renderer, new Vector2(100f, 100f)));
    }

    private static UiColorVertex[] Triangle() =>
    [
        new(new Vector2(0f, 0f), Red),
        new(new Vector2(20f, 0f), Blue),
        new(new Vector2(0f, 20f), Red),
    ];

    [Fact]
    public void TrianglesMoveWithTheOriginAndFadeWithTheAlpha()
    {
        (TextRenderer renderer, UiRenderContext context) = Build();
        context.PushTransform(10f, 20f);
        context.PushAlpha(0.5f);

        context.DrawTriangles(Triangle());

        float[] verts = Assert.Single(renderer.DebugSpriteSegmentVerts).Verts.ToArray();
        Assert.Equal(3 * TextRenderer.FloatsPerVertex, verts.Length);
        Assert.Equal((10f, 20f), (verts[0], verts[1]));
        Assert.Equal((30f, 20f), (verts[8], verts[9]));
        Assert.Equal((0.5f, 0.5f, 0.5f), (verts[7], verts[15], verts[23]));
    }

    [Fact]
    public void AClipCutsTheTriangleAndBlendsTheColourAtTheCut()
    {
        (TextRenderer renderer, UiRenderContext context) = Build();
        context.PushClip(0f, 0f, 10f, 100f);

        context.DrawTriangles(Triangle());

        float[] verts = Assert.Single(renderer.DebugSpriteSegmentVerts).Verts.ToArray();
        // Four corners are left after the cut, drawn as two triangles.
        Assert.Equal(6 * TextRenderer.FloatsPerVertex, verts.Length);
        for (int i = 0; i < verts.Length; i += TextRenderer.FloatsPerVertex)
        {
            Assert.InRange(verts[i], 0f, 10f + 1e-4f);
            if (MathF.Abs(verts[i] - 10f) < 1e-4f)
                Assert.InRange(verts[i + 4], 0.5f - 1e-4f, 0.5f + 1e-4f);
        }
    }

    [Fact]
    public void ATriangleWhollyOutsideTheClipOrUnderAnEmptyClipDrawsNothing()
    {
        (TextRenderer renderer, UiRenderContext context) = Build();

        context.PushClip(50f, 50f, 10f, 10f);
        context.DrawTriangles(Triangle());
        context.PopClip();
        context.PushClip(0f, 0f, 0f, 10f);
        context.DrawTriangles(Triangle());

        Assert.Empty(renderer.DebugSpriteSegments);
    }
}
