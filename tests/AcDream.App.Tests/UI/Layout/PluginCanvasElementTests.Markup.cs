using System.Collections.Generic;
using AcDream.App.Plugins;
using AcDream.App.Rendering.Gpu;
using AcDream.App.Tests.Rendering.Gpu;
using AcDream.App.UI;
using AcDream.App.UI.Layout;
using AcDream.Plugin.Abstractions;

namespace AcDream.App.Tests.UI.Layout;

/// <summary>
/// A canvas hosted by a markup <c>&lt;canvas&gt;</c> element: it fills its
/// host, rounded to whole pixels, is painted at that size and again when it
/// changes, keeps its last size while the host has no room, and is hidden and
/// hit-tested with its host.
/// </summary>
public sealed partial class PluginCanvasElementTests
{
    private static PluginCanvasRegistration RegisterInMarkup(
        Harness harness, string id, Action<IPluginPainter> paint, bool pointer = false, bool outside = false)
    {
        var registration = (PluginCanvasRegistration)harness.Registry.RegisterCanvas(
            harness.Owner,
            new PluginCanvasDescriptor(id, 50, 50)
            {
                InMarkup = true,
                AcceptsPointerInput = pointer,
                WantsOutsidePresses = outside,
            },
            paint);
        Assert.Contains(registration, harness.Registry.DrainCanvases());
        return registration;
    }

    private static PluginCanvasElement HostedElement(Harness harness, PluginCanvasRegistration registration) =>
        new(registration, harness.Surface, () => harness.Registry.FindImages(harness.Owner), harness.Reports.Add,
            harness.Clock.Read, fonts: () => harness.Registry.FindFonts(harness.Owner), hosted: true);

    private static (PluginCanvasRegistration Registration, UiMarkupCanvas Host, List<(int Width, int Height)> Sizes)
        HostedCanvas(Harness harness, bool pointer = false)
    {
        var sizes = new List<(int Width, int Height)>();
        PluginCanvasRegistration registration =
            RegisterInMarkup(harness, "map", painter => sizes.Add((painter.Width, painter.Height)), pointer);
        // No anchors, as under a flex container: the test sets the host's size by hand.
        var host = new UiMarkupCanvas("map")
        {
            Left = 40f, Top = 30f, Width = 120.4f, Height = 80.6f, Anchors = AnchorEdges.None,
        };
        harness.Root.AddChild(host);
        host.Host(HostedElement(harness, registration));
        return (registration, host, sizes);
    }

    [Fact]
    public void AHostedCanvasFillsItsHostRoundedToWholePixels()
    {
        var harness = new Harness();
        (PluginCanvasRegistration registration, UiMarkupCanvas host, List<(int, int)> sizes) = HostedCanvas(harness);

        harness.Frame();

        Assert.Equal([(120, 81)], sizes);
        Assert.Equal((120, 81), (registration.Width, registration.Height));
        var element = Assert.IsType<PluginCanvasElement>(host.Hosted);
        Assert.Equal((0f, 0f, 120f, 81f), (element.Left, element.Top, element.Width, element.Height));
    }

    [Fact]
    public void AHostResizeRepaintsOnceAtTheNewSize()
    {
        var harness = new Harness();
        (PluginCanvasRegistration registration, UiMarkupCanvas host, List<(int, int)> sizes) = HostedCanvas(harness);
        harness.Frame();
        harness.Device.Clear();

        host.Width = 200f;
        host.Height = 50f;
        harness.Frame();
        harness.Frame();

        Assert.Equal([(120, 81), (200, 50)], sizes);
        GpuRecordedRenderTargetCreate created = Assert.Single(harness.Device.OfKind<GpuRecordedRenderTargetCreate>());
        Assert.Equal((200, 50), (created.Description.Width, created.Description.Height));
    }

    [Fact]
    public void AHostWithNoRoomKeepsTheLastSizeAndPaintsNothingNew()
    {
        var harness = new Harness();
        (PluginCanvasRegistration registration, UiMarkupCanvas host, List<(int, int)> sizes) = HostedCanvas(harness);
        harness.Frame();

        host.Width = 0f;
        harness.Frame();

        Assert.Single(sizes);
        Assert.Equal((120, 81), (registration.Width, registration.Height));
    }

    [Fact]
    public void APressOnAHostedCanvasArrivesInCanvasPixels()
    {
        var harness = new Harness();
        (PluginCanvasRegistration registration, _, _) = HostedCanvas(harness, pointer: true);
        var events = new List<PluginPointerEvent>();
        registration.PointerHandler = events.Add;
        harness.Frame();

        harness.Root.OnMouseDown(UiMouseButton.Left, 50, 40);
        harness.Root.OnMouseUp(UiMouseButton.Left, 50, 40);

        Assert.Equal(
            [(PluginPointerEventKind.Down, new PluginPoint(10, 10)), (PluginPointerEventKind.Up, new PluginPoint(10, 10))],
            events.ConvertAll(e => (e.Kind, e.Position)));
    }

    [Fact]
    public void AHiddenHostHidesItsCanvas()
    {
        var harness = new Harness();
        (PluginCanvasRegistration registration, UiMarkupCanvas host, List<(int, int)> sizes) =
            HostedCanvas(harness, pointer: true);
        var events = new List<PluginPointerEvent>();
        registration.PointerHandler = events.Add;
        host.Visible = false;

        harness.Frame();
        harness.Root.OnMouseDown(UiMouseButton.Left, 50, 40);
        harness.Root.OnMouseUp(UiMouseButton.Left, 50, 40);
        Assert.Empty(sizes);
        Assert.Empty(events);

        host.Visible = true;
        harness.Frame();
        Assert.Equal([(120, 81)], sizes);
    }

    [Fact]
    public void UnhostingGivesTheElementBack()
    {
        var harness = new Harness();
        (_, UiMarkupCanvas host, _) = HostedCanvas(harness);
        harness.Frame();
        var element = Assert.IsType<PluginCanvasElement>(host.Hosted);

        Assert.Same(element, host.Unhost());

        Assert.Null(host.Hosted);
        Assert.Null(element.Parent);
        Assert.Null(host.Unhost());
    }
}
