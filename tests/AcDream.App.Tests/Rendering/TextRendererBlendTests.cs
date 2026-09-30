using System.Linq;
using System.Numerics;
using AcDream.App.Rendering;
using AcDream.App.Rendering.Gpu;
using AcDream.App.Tests.Rendering.Gpu;

namespace AcDream.App.Tests.Rendering;

/// <summary>
/// The interface renderer paints with straight alpha into the frame, or into
/// a canvas target that then holds premultiplied colour; a canvas is shown
/// through a premultiplied composite run whose pipeline is made on first use.
/// </summary>
public sealed class TextRendererBlendTests
{
    private sealed class FrameSource : ICurrentGpuFrameSource
    {
        public IGpuFrame? CurrentFrame { get; set; }
    }

    private static readonly Vector2 Screen = new(64f, 64f);

    [Fact]
    public void ARendererForCanvasTargetsBuildsItsOnePipelineWithTheIntoPremultipliedBlend()
    {
        using var device = new RecordingGpuDevice();
        int before = device.CreatedPipelines.Count;

        using var renderer = new TextRenderer(
            device, new FrameSource(), "unused", GpuBlendMode.StraightAlphaIntoPremultiplied);

        RecordingGpuPipeline pipeline = Assert.Single(device.CreatedPipelines.Skip(before));
        Assert.Equal(TextRenderer.IntoPremultipliedPipelineName, pipeline.Description.Name);
        Assert.Equal(GpuBlendMode.StraightAlphaIntoPremultiplied, pipeline.Description.Blend);
    }

    [Fact]
    public void ABlendTheInterfaceDoesNotPaintWithIsRefused()
    {
        using var device = new RecordingGpuDevice();

        foreach (GpuBlendMode blend in new[] { GpuBlendMode.None, GpuBlendMode.PremultipliedAlpha, GpuBlendMode.Additive })
        {
            Assert.Throws<ArgumentOutOfRangeException>(
                () => new TextRenderer(device, new FrameSource(), "unused", blend));
        }
    }

    [Fact]
    public void TheCompositePipelineIsMadeOnFirstUseAndBoundOnlyAroundItsRun()
    {
        using var device = new RecordingGpuDevice();
        var frames = new FrameSource();
        using var renderer = new TextRenderer(device, frames, "unused");
        int afterConstruction = device.CreatedPipelines.Count;

        renderer.Begin(Screen);
        renderer.DrawFill(0f, 0f, 10f, 10f, Vector4.One);
        renderer.DrawPremultipliedSprite(5u, 0f, 0f, 10f, 10f, 0f, 0f, 1f, 1f, Vector4.One);
        renderer.DrawPremultipliedSprite(5u, 10f, 0f, 10f, 10f, 0f, 0f, 1f, 1f, Vector4.One);
        renderer.DrawFill(0f, 20f, 10f, 10f, Vector4.One);

        RecordingGpuPipeline composite = Assert.Single(device.CreatedPipelines.Skip(afterConstruction));
        Assert.Equal(TextRenderer.PremultipliedPipelineName, composite.Description.Name);
        Assert.Equal(GpuBlendMode.PremultipliedAlpha, composite.Description.Blend);

        device.Clear();
        using (IGpuFrame frame = device.BeginFrame())
        {
            frames.CurrentFrame = frame;
            renderer.Flush(null);
            frames.CurrentFrame = null;
        }

        Assert.Equal(
            [TextRenderer.PipelineName, TextRenderer.PremultipliedPipelineName, TextRenderer.PipelineName],
            device.OfKind<GpuRecordedPipelineBind>().Select(bind => bind.PipelineName).ToArray());
        Assert.Equal([false, true, false], renderer.DebugSpriteSegmentPremultiplied);
        Assert.Equal(afterConstruction + 1, device.CreatedPipelines.Count);
    }

    [Fact]
    public void APremultipliedSpriteCarriesItsTintPremultiplied()
    {
        using var device = new RecordingGpuDevice();
        using var renderer = new TextRenderer(device, new FrameSource(), "unused");
        renderer.Begin(Screen);

        renderer.DrawPremultipliedSprite(5u, 0f, 0f, 10f, 10f, 0f, 0f, 1f, 1f, new Vector4(1f, 0.5f, 0.25f, 0.5f));

        (uint _, IReadOnlyList<float> verts) = Assert.Single(renderer.DebugSpriteSegmentVerts);
        Assert.Equal([0.5f, 0.25f, 0.125f, 0.5f], verts.Skip(4).Take(4).ToArray());
    }

    [Fact]
    public void StraightAndPremultipliedSpritesOfOneTextureStayInSeparateRuns()
    {
        using var device = new RecordingGpuDevice();
        using var renderer = new TextRenderer(device, new FrameSource(), "unused");
        renderer.Begin(Screen);

        renderer.DrawSprite(5u, 0f, 0f, 10f, 10f, 0f, 0f, 1f, 1f, Vector4.One);
        renderer.DrawPremultipliedSprite(5u, 0f, 0f, 10f, 10f, 0f, 0f, 1f, 1f, Vector4.One);

        Assert.Equal([5u, 5u], renderer.DebugSpriteSegments.Select(seg => seg.Texture).ToArray());
        Assert.Equal([false, true], renderer.DebugSpriteSegmentPremultiplied);
    }

    [Fact]
    public void DisposeDisposesTheCompositePipelineToo()
    {
        using var device = new RecordingGpuDevice();
        var renderer = new TextRenderer(device, new FrameSource(), "unused");
        renderer.Begin(Screen);
        renderer.DrawPremultipliedSprite(5u, 0f, 0f, 1f, 1f, 0f, 0f, 1f, 1f, Vector4.One);

        renderer.Dispose();

        Assert.All(
            device.CreatedPipelines.Where(pipeline => pipeline.Description.Name.StartsWith("ui-text", StringComparison.Ordinal)),
            pipeline => Assert.True(pipeline.IsDisposed));
    }
}
