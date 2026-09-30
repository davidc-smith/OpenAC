using System.Numerics;
using AcDream.App.Rendering;
using AcDream.App.Rendering.Gpu;
using AcDream.App.Rendering.Gpu.Vk;
using AcDream.App.UI;
using AcDream.App.UI.Layout;
using AcDream.Plugin.Abstractions;

namespace AcDream.App.Tests.Rendering.Gpu.Vk;

/// <summary>
/// Shapes painted on a real device: solid inside, clear outside, and
/// partly covered across the one-pixel fringe at their edges.
/// </summary>
public sealed class PluginCanvasShapeOffscreenTests
{
    private const int Extent = 16;

    private sealed class FrameSource : ICurrentGpuFrameSource
    {
        public IGpuFrame? CurrentFrame { get; set; }
    }

    [Trait("Lane", "Vulkan")]
    [Fact]
    public void AStraightEdgeThroughAPixelCentreCoversHalfOfIt()
    {
        byte[] painted = Paint(painter => painter.FillRoundedRect(
            new PluginRect(0, 0, 8.5, Extent), default, PluginColor.White));

        AssertNear([255, 255, 255, 255], Pixel(painted, 7, 4), 1);
        AssertNear([128, 128, 128, 128], Pixel(painted, 8, 4), 2);
        AssertNear([0, 0, 0, 0], Pixel(painted, 9, 4), 0);
    }

    [Trait("Lane", "Vulkan")]
    [Fact]
    public void ACircleIsSolidInsideClearOutsideAndPartlyCoveredAtItsRim()
    {
        byte[] painted = Paint(painter => painter.FillCircle(
            new PluginPoint(8, 8), Math.Sqrt(20.5), PluginColor.White));

        AssertNear([255, 255, 255, 255], Pixel(painted, 8, 8), 1);
        AssertNear([0, 0, 0, 0], Pixel(painted, 0, 0), 0);
        foreach ((int x, int y) in new[] { (3, 8), (12, 8) })
        {
            ReadOnlySpan<byte> rim = Pixel(painted, x, y);
            Assert.InRange(rim[3], (byte)80, (byte)150);
            for (int channel = 0; channel < 3; channel++)
                Assert.InRange(rim[channel], (byte)(rim[3] - 2), (byte)(rim[3] + 2));
        }
    }

    private static byte[] Paint(Action<IPluginPainter> paint)
    {
        using var host = HeadlessVulkanTestHost.Create(HeadlessVulkanTestHost.CommittedShaderDirectory());
        VulkanGpuDevice device = host.Device;
        var frames = new FrameSource();
        using var renderer = new TextRenderer(device, frames, "unused", GpuBlendMode.StraightAlphaIntoPremultiplied);
        using IGpuRenderTarget canvas = device.CreateRenderTarget(new GpuRenderTargetDescription(
            "canvas-shape", Extent, Extent, GpuTextureFormat.Rgba8UnormRenderTarget, DepthFormat: null, SampleCount: 1));
        var size = new Vector2(Extent, Extent);
        var context = new UiRenderContext(renderer, size);
        var painter = new PluginPainter();

        using (IGpuFrame frame = device.BeginFrame())
        {
            frames.CurrentFrame = frame;
            renderer.Begin(size);
            context.Begin(size, null);
            context.PushClip(0f, 0f, Extent, Extent);
            painter.Bind(context, null, null, Extent, Extent);
            paint(painter);
            painter.Unbind();
            context.PopClip();
            renderer.FlushTo(canvas, Vector4.Zero, null, "canvas-shape-paint");
            frames.CurrentFrame = null;
        }
        device.WaitIdle();
        return host.ReadBack(canvas, Extent, Extent);
    }

    private static ReadOnlySpan<byte> Pixel(byte[] image, int x, int y) => image.AsSpan((y * Extent + x) * 4, 4);

    private static void AssertNear(int[] expected, ReadOnlySpan<byte> actual, int tolerance)
    {
        for (int channel = 0; channel < 4; channel++)
        {
            Assert.True(
                Math.Abs(expected[channel] - actual[channel]) <= tolerance,
                $"channel {channel}: expected {expected[channel]}±{tolerance}, read {actual[channel]} "
                + $"(pixel {actual[0]},{actual[1]},{actual[2]},{actual[3]})");
        }
    }
}
