using AcDream.App.Plugins;
using AcDream.App.Rendering;
using AcDream.App.Rendering.Gpu;
using AcDream.App.Rendering.Gpu.Vk;
using AcDream.App.UI;
using AcDream.App.UI.Layout;
using AcDream.Plugin.Abstractions;

namespace AcDream.App.Tests.Rendering.Gpu.Vk;

/// <summary>
/// A canvas painted through the canvas surface at twice the density, on a
/// real device: the target holds two pixels per canvas pixel, and a shape's
/// fringe is one of those pixels wide, not one canvas pixel.
/// </summary>
public sealed class PluginCanvasHiDpiOffscreenTests
{
    private const int Extent = 16;

    private const float Scale = 2f;

    private const int Device = Extent * 2;

    private sealed class FrameSource : ICurrentGpuFrameSource
    {
        public IGpuFrame? CurrentFrame { get; set; }
    }

    [Trait("Lane", "Vulkan")]
    [Fact]
    public void AnEdgeOnADevicePixelBoundaryIsSharpAtTwiceTheDensity()
    {
        // At one pixel per canvas pixel the fringe of this edge would cover
        // canvas pixels 7.5 to 8.5, so device pixels 15 and 16 would read 3/4 and 1/4.
        byte[] painted = Paint(painter => painter.FillRoundedRect(
            new PluginRect(0, 0, 8, Extent), default, PluginColor.White));

        AssertNear([255, 255, 255, 255], Pixel(painted, 14, 8), 1);
        AssertNear([255, 255, 255, 255], Pixel(painted, 15, 8), 1);
        AssertNear([0, 0, 0, 0], Pixel(painted, 16, 8), 1);
    }

    [Trait("Lane", "Vulkan")]
    [Fact]
    public void AnEdgeThroughADevicePixelCentreCoversHalfOfIt()
    {
        byte[] painted = Paint(painter => painter.FillRoundedRect(
            new PluginRect(0, 0, 8.25, Extent), default, PluginColor.White));

        AssertNear([255, 255, 255, 255], Pixel(painted, 15, 8), 1);
        AssertNear([128, 128, 128, 128], Pixel(painted, 16, 8), 2);
        AssertNear([0, 0, 0, 0], Pixel(painted, 17, 8), 0);
    }

    private static byte[] Paint(Action<IPluginPainter> paint)
    {
        using var host = HeadlessVulkanTestHost.Create(HeadlessVulkanTestHost.CommittedShaderDirectory());
        VulkanGpuDevice device = host.Device;
        var frames = new FrameSource();
        var registry = new BufferedUiRegistry();
        var registration = (PluginCanvasRegistration)registry.RegisterCanvas(
            new PluginUiOwner("example.plugin", "Example"), new PluginCanvasDescriptor("hud", Extent, Extent), paint);
        using var surface = new PluginCanvasSurface(
            new PluginCanvasHostServices(device, frames, "unused", null), font: null);
        using IGpuRenderTarget canvas = device.CreateRenderTarget(new GpuRenderTargetDescription(
            "canvas-hidpi", Device, Device, GpuTextureFormat.Rgba8UnormRenderTarget, DepthFormat: null, SampleCount: 1));

        using (IGpuFrame frame = device.BeginFrame())
        {
            frames.CurrentFrame = frame;
            Assert.True(surface.Repaint(
                registration, canvas, new UiDrawCallbackGuard("hidpi"), images: null, pixelScale: Scale));
            frames.CurrentFrame = null;
        }
        device.WaitIdle();
        return host.ReadBack(canvas, Device, Device);
    }

    private static ReadOnlySpan<byte> Pixel(byte[] image, int x, int y) => image.AsSpan((y * Device + x) * 4, 4);

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
