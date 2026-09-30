using System.Numerics;
using AcDream.App.Rendering;
using AcDream.App.Rendering.Gpu;
using AcDream.App.Rendering.Gpu.Vk;

namespace AcDream.App.Tests.Rendering.Gpu.Vk;

/// <summary>
/// A plugin canvas on a real device: what is painted translucent into the
/// canvas's target is shown at the alpha it was painted with.
/// </summary>
public sealed class PluginCanvasCompositeOffscreenTests
{
    private const int Extent = 8;

    private sealed class FrameSource : ICurrentGpuFrameSource
    {
        public IGpuFrame? CurrentFrame { get; set; }
    }

    [Trait("Lane", "Vulkan")]
    [Fact]
    public void AHalfTransparentFillOnACanvasShowsAtHalfNotAQuarter()
    {
        using var host = HeadlessVulkanTestHost.Create(HeadlessVulkanTestHost.CommittedShaderDirectory());
        VulkanGpuDevice device = host.Device;
        var frames = new FrameSource();
        using var canvasRenderer = new TextRenderer(
            device, frames, "unused", GpuBlendMode.StraightAlphaIntoPremultiplied);
        using var screenRenderer = new TextRenderer(device, frames, "unused");
        using IGpuRenderTarget canvas = device.CreateRenderTarget(new GpuRenderTargetDescription(
            "canvas-alpha-canvas", Extent, Extent, GpuTextureFormat.Rgba8UnormRenderTarget,
            DepthFormat: null, SampleCount: 1));
        using IGpuRenderTarget screen = device.CreateRenderTarget(new GpuRenderTargetDescription(
            "canvas-alpha-screen", Extent, Extent, GpuTextureFormat.Rgba8UnormRenderTarget,
            DepthFormat: null, SampleCount: 1));
        GpuTextureSlot slot = device.RegisterTexture(
            canvas.ColorTexture, device.CreateSampler(GpuSamplerDescription.WorldClamp));
        uint canvasHandle = UiTextureTableHandle.FromSlot(slot);
        var size = new Vector2(Extent, Extent);

        using (IGpuFrame frame = device.BeginFrame())
        {
            frames.CurrentFrame = frame;
            canvasRenderer.Begin(size);
            canvasRenderer.DrawFill(0f, 0f, Extent, Extent, new Vector4(1f, 1f, 1f, 0.5f));
            canvasRenderer.FlushTo(canvas, Vector4.Zero, null, "canvas-alpha-paint");

            screenRenderer.Begin(size);
            screenRenderer.DrawPremultipliedSprite(
                canvasHandle, 0f, 0f, Extent, Extent, 0f, 0f, 1f, 1f, Vector4.One);
            screenRenderer.FlushTo(screen, new Vector4(0f, 0f, 0f, 1f), null, "canvas-alpha-composite");
            frames.CurrentFrame = null;
        }
        device.WaitIdle();

        byte[] painted = host.ReadBack(canvas, Extent, Extent);
        byte[] shown = host.ReadBack(screen, Extent, Extent);
        device.ReleaseTextureSlot(slot);

        int centre = ((Extent / 2 * Extent) + Extent / 2) * 4;
        AssertNear([128, 128, 128, 128], painted.AsSpan(centre, 4));
        AssertNear([128, 128, 128, 255], shown.AsSpan(centre, 4));
    }

    private static void AssertNear(int[] expected, ReadOnlySpan<byte> actual)
    {
        for (int channel = 0; channel < 4; channel++)
        {
            Assert.True(
                Math.Abs(expected[channel] - actual[channel]) <= 2,
                $"channel {channel}: expected {expected[channel]}±2, read {actual[channel]} "
                + $"(pixel {actual[0]},{actual[1]},{actual[2]},{actual[3]})");
        }
    }
}
