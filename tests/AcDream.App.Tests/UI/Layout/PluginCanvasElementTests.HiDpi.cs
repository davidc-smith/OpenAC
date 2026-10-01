using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using AcDream.App.Plugins;
using AcDream.App.Rendering.Gpu;
using AcDream.App.Tests.Rendering.Gpu;
using AcDream.App.UI;
using AcDream.App.UI.Layout;
using AcDream.Plugin.Abstractions;

namespace AcDream.App.Tests.UI.Layout;

/// <summary>
/// On a high-density display a canvas is painted at the pixel scale: its
/// target and the surface's projection grow, while the painter, the clip,
/// the pointer and the canvas's place on the interface stay in canvas
/// pixels. A change of scale gives the targets back and repaints.
/// </summary>
public sealed partial class PluginCanvasElementTests
{
    [Fact]
    public void AtTwiceTheDensityTheTargetAndProjectionDoubleAndThePainterStaysInCanvasPixels()
    {
        var harness = new Harness { FramebufferPerPoint = new Vector2(2f, 2f) };
        (int Width, int Height, double Scale) seen = default;
        (_, PluginCanvasElement element) = harness.Mount(Hud(), painter =>
        {
            seen = (painter.Width, painter.Height, painter.PixelScale);
            painter.FillRect(new PluginRect(0, 0, 200, 100), PluginColor.White);
        });

        harness.Frame();

        Assert.Equal((200, 100, 2.0), seen);
        Assert.Equal(2f, element.PixelScale);
        GpuRecordedRenderTargetCreate created = Assert.Single(harness.Device.OfKind<GpuRecordedRenderTargetCreate>());
        Assert.Equal((400, 200), (created.Description.Width, created.Description.Height));
        Assert.Equal((400f, 200f), SurfaceProjection(harness));

        // The fill's vertices come out in target pixels.
        (uint _, IReadOnlyList<float> verts) = Assert.Single(harness.Surface.Renderer.DebugSpriteSegmentVerts);
        Assert.Equal(400f, Enumerable.Range(0, 6).Max(i => verts[i * AcDream.App.Rendering.TextRenderer.FloatsPerVertex]));
        Assert.Equal(200f, Enumerable.Range(0, 6).Max(i => verts[i * AcDream.App.Rendering.TextRenderer.FloatsPerVertex + 1]));

        // The interface still shows it as a 200 x 100 quad at its anchored place.
        Assert.Equal((200f, 100f), (element.Width, element.Height));
        (uint _, IReadOnlyList<float> blit) = Assert.Single(harness.MainRenderer.DebugSpriteSegmentVerts);
        float left = Enumerable.Range(0, 6).Min(i => blit[i * AcDream.App.Rendering.TextRenderer.FloatsPerVertex]);
        float right = Enumerable.Range(0, 6).Max(i => blit[i * AcDream.App.Rendering.TextRenderer.FloatsPerVertex]);
        Assert.Equal(200f, right - left);
    }

    [Fact]
    public void AtOneToOneNothingChanges()
    {
        var harness = new Harness();
        double scale = 0;
        harness.Mount(Hud(), painter =>
        {
            scale = painter.PixelScale;
            painter.FillRect(new PluginRect(0, 0, 200, 100), PluginColor.White);
        });

        harness.Frame();

        Assert.Equal(1.0, scale);
        GpuRecordedRenderTargetCreate created = Assert.Single(harness.Device.OfKind<GpuRecordedRenderTargetCreate>());
        Assert.Equal((200, 100), (created.Description.Width, created.Description.Height));
        Assert.Equal((200f, 100f), SurfaceProjection(harness));
    }

    [Fact]
    public void TheFixedCanvasStretchCountsTowardsTheScale()
    {
        var harness = new Harness();
        harness.Root.DeclareFixedCanvas(this, new Vector2(400f, 300f));
        double scale = 0;
        harness.Mount(Hud(), painter => scale = painter.PixelScale);

        harness.Frame();

        Assert.Equal(2.0, scale);
    }

    [Fact]
    public void AChangeOfScaleGivesTheTargetsBackAndRepaintsAtTheNewSize()
    {
        var harness = new Harness();
        var scales = new List<double>();
        (_, PluginCanvasElement element) = harness.Mount(Hud(), painter => scales.Add(painter.PixelScale));
        harness.Frame();
        harness.Frame();
        Assert.Equal(1, element.TargetCount);
        int slotsBefore = harness.Device.LiveTextureSlotCount;
        harness.Device.Clear();

        // The window moves to a high-density display.
        harness.FramebufferPerPoint = new Vector2(2f, 2f);
        harness.Frame();

        Assert.Equal([1.0, 2.0], scales);
        // The old target is given back at once; the device holds it until
        // the frames in flight are done with it.
        Assert.Single(harness.Retirement.Pending);
        harness.Retirement.RunAll();
        Assert.Equal(slotsBefore, harness.Device.LiveTextureSlotCount);
        GpuRecordedRenderTargetCreate created = Assert.Single(harness.Device.OfKind<GpuRecordedRenderTargetCreate>());
        Assert.Equal((400, 200), (created.Description.Width, created.Description.Height));
        Assert.Equal(1, element.TargetCount);
        Assert.NotEqual(0u, element.ShownTextureHandle);

        // Unchanged afterwards: no more repaints than the plugin asks for.
        harness.Frame();
        Assert.Equal(2, scales.Count);
    }

    [Fact]
    public void ACanvasTooLargeForTheDeviceIsPaintedAtTheLargestScaleThatFits()
    {
        var harness = new Harness(maximumImageDimension: 350) { FramebufferPerPoint = new Vector2(2f, 2f) };
        double scale = 0;
        harness.Mount(Hud(), painter => scale = painter.PixelScale);

        harness.Frame();

        Assert.Equal(1.75, scale);
        GpuRecordedRenderTargetCreate created = Assert.Single(harness.Device.OfKind<GpuRecordedRenderTargetCreate>());
        Assert.Equal((350, 175), (created.Description.Width, created.Description.Height));
    }

    [Fact]
    public void PointerPositionsStayInCanvasPixels()
    {
        var harness = new Harness { FramebufferPerPoint = new Vector2(2f, 2f) };
        var events = new List<PluginPointerEvent>();
        (PluginCanvasRegistration registration, _) = harness.Mount(Hud() with { AcceptsPointerInput = true }, _ => { });
        ((IPluginCanvas)registration).PointerHandler = events.Add;
        harness.Frame();

        // The canvas sits at (590, 490): 800 x 600, anchored bottom right, 10 in.
        harness.Root.OnMouseDown(UiMouseButton.Left, 600, 500);
        harness.Root.OnMouseUp(UiMouseButton.Left, 789, 589);

        Assert.Equal(
            [new PluginPoint(10, 10), new PluginPoint(199, 99)],
            events.Select(e => e.Position).ToArray());
    }

    [Fact]
    public void AKeptPainterStillThrowsForItsPixelScale()
    {
        var harness = new Harness { FramebufferPerPoint = new Vector2(2f, 2f) };
        IPluginPainter? kept = null;
        harness.Mount(Hud(), painter => kept = painter);

        harness.Frame();

        Assert.NotNull(kept);
        Assert.Throws<InvalidOperationException>(() => kept!.PixelScale);
    }

    /// <summary>The projection size the surface's pass pushed: what one target pixel of the canvas pass is.</summary>
    private static (float Width, float Height) SurfaceProjection(Harness harness)
    {
        bool inCanvasPass = false;
        foreach (GpuRecordedCall call in harness.Device.Calls)
        {
            if (call is GpuRecordedPassBegin begin)
                inCanvasPass = begin.Name == PluginCanvasSurface.PassName;
            else if (inCanvasPass && call is GpuRecordedPushConstants push)
                return (push.Constants.ParamA, push.Constants.ParamB);
        }
        throw new Xunit.Sdk.XunitException("The canvas pass pushed no constants.");
    }
}
