using System.Collections.Generic;
using AcDream.App.Plugins;
using AcDream.App.Rendering.Gpu;
using AcDream.App.Tests.Rendering.Gpu;
using AcDream.App.UI.Layout;
using AcDream.Plugin.Abstractions;

namespace AcDream.App.Tests.UI.Layout;

/// <summary>
/// A canvas the plugin resizes gives its targets back, is painted again at
/// the new size, and keeps its anchor: one anchored at the bottom right keeps
/// that corner where it was.
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
        GpuRecordedRenderTargetCreate created = Assert.Single(harness.Device.OfKind<GpuRecordedRenderTargetCreate>());
        Assert.Equal((300, 50), (created.Description.Width, created.Description.Height));
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
