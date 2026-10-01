using System.Numerics;
using AcDream.App.Rendering;
using AcDream.App.Rendering.Gpu;
using AcDream.App.Tests.Rendering.Gpu;
using AcDream.App.UI;

namespace AcDream.App.Tests.Rendering;

/// <summary>
/// A frame is drawn as three layers -- main, upper, overlay -- each with its
/// sprite runs before its rectangles and text. Everything the root draws
/// from the plugin canvases over windows upwards is in the upper layer, so a
/// window's rectangles and text, which draw after every sprite of their
/// layer, can no longer show through a canvas or a dialog above them.
/// </summary>
public sealed class TextRendererDrawLayerTests
{
    private const uint UpperTexture = 77u;

    private sealed class FrameSource : ICurrentGpuFrameSource
    {
        public IGpuFrame? CurrentFrame { get; set; }
    }

    /// <summary>A window that draws through the renderer's rectangle bucket, which flushes after its layer's sprites.</summary>
    private sealed class RectWindow : UiElement
    {
        protected override void OnDraw(UiRenderContext ctx) =>
            ctx.TextRenderer.DrawRect(0f, 0f, Width, Height, Vector4.One);
    }

    /// <summary>Stands in for a canvas drawn over windows: one textured sprite.</summary>
    private sealed class SpriteElement : UiElement
    {
        protected override void OnDraw(UiRenderContext ctx) =>
            ctx.DrawSprite(UpperTexture, 0f, 0f, Width, Height, 0f, 0f, 1f, 1f, Vector4.One);
    }

    /// <summary>The texture slot of every draw, in submission order.</summary>
    private static List<uint> DrawnTextureSlots(RecordingGpuDevice device)
    {
        var slots = new List<uint>();
        uint current = 0;
        foreach (GpuRecordedCall call in device.Calls)
        {
            if (call is GpuRecordedPushConstants constants)
                current = constants.Constants.TextureIndexA;
            else if (call is GpuRecordedDraw)
                slots.Add(current);
        }
        return slots;
    }

    private static List<uint> Flush(RecordingGpuDevice device, FrameSource frames, TextRenderer renderer)
    {
        device.Clear();
        using IGpuFrame frame = device.BeginFrame();
        frames.CurrentFrame = frame;
        try
        {
            renderer.Flush(null);
        }
        finally
        {
            frames.CurrentFrame = null;
        }
        return DrawnTextureSlots(device);
    }

    private static uint Slot(uint handle) => UiTextureTableHandle.ToSlot(handle).Index;

    [Fact]
    public void TheUpperLayerDrawsAfterTheMainLayersRectangles()
    {
        var device = new RecordingGpuDevice();
        var frames = new FrameSource();
        var renderer = new TextRenderer(device, frames, "unused");
        renderer.Begin(new Vector2(100f, 100f));

        renderer.DrawRect(0f, 0f, 10f, 10f, Vector4.One);
        renderer.Layer = UiDrawLayer.Upper;
        renderer.DrawSprite(UpperTexture, 0f, 0f, 10f, 10f, 0f, 0f, 1f, 1f, Vector4.One);

        Assert.Equal([Slot(UiTextureTableHandle.None), Slot(UpperTexture)], Flush(device, frames, renderer));
    }

    [Fact]
    public void BeginPutsDrawsBackInTheMainLayerAndOverlayModeStillNamesTheOverlay()
    {
        var renderer = new TextRenderer(new RecordingGpuDevice(), new FrameSource(), "unused");
        renderer.Layer = UiDrawLayer.Upper;

        renderer.Begin(new Vector2(10f, 10f));
        Assert.Equal(UiDrawLayer.Main, renderer.Layer);

        renderer.OverlayMode = true;
        Assert.Equal(UiDrawLayer.Overlay, renderer.Layer);
        renderer.OverlayMode = false;
        Assert.Equal(UiDrawLayer.Main, renderer.Layer);
    }

    [Fact]
    public void TheRootDrawsFromTheCanvasesOverWindowsUpwardsInTheUpperLayer()
    {
        var device = new RecordingGpuDevice();
        var frames = new FrameSource();
        var renderer = new TextRenderer(device, frames, "unused");
        var root = new UiRoot { Width = 100f, Height = 100f };
        root.AddChild(new RectWindow { Width = 50f, Height = 50f, ZOrder = 4 });
        root.AddChild(new SpriteElement { Width = 50f, Height = 50f, ZOrder = UiBands.CanvasesAboveWindows });

        renderer.Begin(new Vector2(100f, 100f));
        root.Draw(new UiRenderContext(renderer, new Vector2(100f, 100f)));

        // Without the upper layer the window's rectangle would draw last, over the canvas.
        Assert.Equal([Slot(UiTextureTableHandle.None), Slot(UpperTexture)], Flush(device, frames, renderer));
        Assert.Equal(UiDrawLayer.Main, renderer.Layer);
    }

    [Fact]
    public void ARootWithNothingOverWindowsDrawsEverythingInTheMainLayer()
    {
        var device = new RecordingGpuDevice();
        var frames = new FrameSource();
        var renderer = new TextRenderer(device, frames, "unused");
        var root = new UiRoot { Width = 100f, Height = 100f };
        root.AddChild(new RectWindow { Width = 50f, Height = 50f, ZOrder = 4 });
        root.AddChild(new SpriteElement { Width = 50f, Height = 50f, ZOrder = 9 });

        renderer.Begin(new Vector2(100f, 100f));
        root.Draw(new UiRenderContext(renderer, new Vector2(100f, 100f)));

        // One layer: its sprite runs, then its rectangles, as before.
        Assert.Equal([Slot(UpperTexture), Slot(UiTextureTableHandle.None)], Flush(device, frames, renderer));
    }
}
