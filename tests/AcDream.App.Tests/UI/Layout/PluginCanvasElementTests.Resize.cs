using System.Collections.Generic;
using System.Linq;
using AcDream.App.Plugins;
using AcDream.App.Rendering.Gpu;
using AcDream.App.Tests.Rendering.Gpu;
using AcDream.App.UI.Layout;
using AcDream.Plugin.Abstractions;

namespace AcDream.App.Tests.UI.Layout;

/// <summary>
/// A canvas the plugin resizes is painted again at the new size and keeps
/// its anchor: one anchored at the bottom right keeps that corner where it
/// was. Its targets are kept while it fits them, so a resize drag does not
/// make new ones every frame.
/// </summary>
public sealed partial class PluginCanvasElementTests
{
    [Fact]
    public void AResizeRepaintsAtTheNewSizeAndKeepsTheAnchoredCorner()
    {
        var harness = new Harness();
        var sizes = new List<(int Width, int Height)>();
        (PluginCanvasRegistration registration, PluginCanvasElement element) =
            harness.Mount(Hud(), painter => sizes.Add((painter.Width, painter.Height)));
        harness.Frame();
        Assert.Equal((590f, 490f), (element.Left, element.Top));
        harness.Device.Clear();

        Assert.True(registration.TryResize(300, 50));
        Assert.Equal((300, 50), (registration.Width, registration.Height));
        harness.Frame();

        Assert.Equal([(200, 100), (300, 50)], sizes);
        Assert.Single(harness.Retirement.Pending);
        // Outgrown: the next target is made with headroom, the canvas at its top left.
        GpuRecordedRenderTargetCreate created = Assert.Single(harness.Device.OfKind<GpuRecordedRenderTargetCreate>());
        Assert.Equal((320, 64), (created.Description.Width, created.Description.Height));
        Assert.Equal((300f, 50f), (element.Width, element.Height));
        // Bottom right, ten pixels in, as before.
        Assert.Equal((790f, 590f), (element.Left + element.Width, element.Top + element.Height));
        Assert.Equal(PluginCanvasAnchor.BottomRight, registration.Anchor);

        // The same size again is not a change: nothing is given back or repainted.
        Assert.True(registration.TryResize(300, 50));
        harness.Frame();
        Assert.Equal(2, sizes.Count);
    }

    [Fact]
    public void AResizeDragWithinTheHeadroomKeepsItsTargets()
    {
        var harness = new Harness();
        var sizes = new List<(int Width, int Height)>();
        (PluginCanvasRegistration registration, _) =
            harness.Mount(Hud(), painter => sizes.Add((painter.Width, painter.Height)));
        harness.Frame();
        harness.Device.Clear();
        Assert.True(registration.TryResize(201, 101));
        harness.Frame();
        Assert.Equal((256, 128), Made(Assert.Single(harness.Device.OfKind<GpuRecordedRenderTargetCreate>())));
        harness.Device.Clear();
        int retired = harness.Retirement.Pending.Count;

        for (int step = 2; step <= 40; step++)
        {
            Assert.True(registration.TryResize(200 + step, 100 + step / 2));
            harness.Frame();
        }

        Assert.Equal((240, 120), sizes[^1]);
        Assert.Equal(41, sizes.Count);
        // Two targets alternate as frames go by; neither is made again nor given back.
        Assert.True(harness.Device.OfKind<GpuRecordedRenderTargetCreate>().Count() <= 1);
        Assert.Equal(retired, harness.Retirement.Pending.Count);
    }

    [Fact]
    public void ACanvasShrunkFarBelowItsTargetsGetsSmallerOnes()
    {
        var harness = new Harness();
        (PluginCanvasRegistration registration, _) = harness.Mount(Hud(), _ => { });
        harness.Frame();
        harness.Device.Clear();

        // Half each way would be given 128x64, over a quarter of the target:
        // kept, and the second target the repaint writes is made at the first one's size.
        Assert.True(registration.TryResize(100, 50));
        harness.Frame();
        Assert.All(harness.Device.OfKind<GpuRecordedRenderTargetCreate>(), created => Assert.Equal((200, 100), Made(created)));
        harness.Device.Clear();

        Assert.True(registration.TryResize(50, 25));
        harness.Frame();
        Assert.Equal((64, 64), Made(Assert.Single(harness.Device.OfKind<GpuRecordedRenderTargetCreate>())));
    }

    [Fact]
    public void ASmallCanvasDraggedWithinItsHeadroomKeepsItsTargets()
    {
        var harness = new Harness();
        (PluginCanvasRegistration registration, _) = harness.Mount(Hud(), _ => { });
        Assert.True(registration.TryResize(10, 10));
        harness.Frame();
        Assert.True(registration.TryResize(11, 11));
        harness.Frame();
        harness.Device.Clear();

        for (int size = 12; size <= 40; size++)
        {
            Assert.True(registration.TryResize(size, size));
            harness.Frame();
        }

        Assert.True(harness.Device.OfKind<GpuRecordedRenderTargetCreate>().Count() <= 1);
    }

    private static (int Width, int Height) Made(GpuRecordedRenderTargetCreate created) =>
        (created.Description.Width, created.Description.Height);

    [Fact]
    public void AResizeToNothingOrAfterDisposalIsRefused()
    {
        var harness = new Harness();
        (PluginCanvasRegistration registration, _) = harness.Mount(Hud(), _ => { });

        Assert.False(registration.TryResize(0, 50));
        Assert.False(registration.TryResize(300, -1));
        Assert.Equal((200, 100), (registration.Width, registration.Height));

        registration.Dispose();
        Assert.False(registration.TryResize(300, 50));
        Assert.Equal((200, 100), (registration.Width, registration.Height));
    }
}
