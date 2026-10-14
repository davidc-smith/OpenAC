using System.Collections.Generic;
using System.Linq;
using AcDream.App.Plugins;
using AcDream.App.UI;
using AcDream.App.UI.Layout;
using AcDream.Plugin.Abstractions;

namespace AcDream.App.Tests.UI.Layout;

/// <summary>
/// The interface's pairing of markup canvases with <c>&lt;canvas&gt;</c>
/// elements, end to end with real markup windows: either side may come first,
/// the window's layout sizes the canvas, hiding and scrolling work through the
/// tree, a canvas outlives its window and moves to another of the plugin's,
/// and what shows nothing says why once.
/// </summary>
public sealed partial class PluginCanvasElementTests
{
    private sealed class PageBinding
    {
        public bool Shown { get; set; } = true;
    }

    private const string GrowMap = """
        <group x="0" y="0" w="300" h="200" layout="column">
          <label text="Map" />
          <canvas id="map" grow="1" />
        </group>
        """;

    private static PluginMarkupCanvases Table(Harness harness) =>
        new(registration => HostedElement(harness, registration),
            (owner, id) => harness.Registry.FindCanvas(owner, id),
            harness.Reports.Add);

    private static MarkupWindow MountWindow(
        Harness harness, PluginMarkupCanvases table, string body, object? binding = null, int x = 0)
    {
        MarkupWindow window = MarkupDocument.BuildWindow(
            $"<panel x=\"{x}\" y=\"0\" w=\"300\" h=\"200\">{body}</panel>", binding ?? new object(), _ => (0u, 0, 0));
        harness.Root.AddChild(window.Frame);
        table.AddWindow(harness.Owner.Id, window.Canvases);
        return window;
    }

    private static void UnmountWindow(Harness harness, PluginMarkupCanvases table, MarkupWindow window)
    {
        table.RemoveWindow(window.Canvases);
        harness.Root.RemoveChild(window.Frame);
    }

    /// <summary>Mirrors the runtime: the table takes the canvas, and its teardown takes it out again.</summary>
    private static void AddToTable(Harness harness, PluginMarkupCanvases table, PluginCanvasRegistration registration)
    {
        table.AddCanvas(registration);
        harness.Registry.CompleteCanvasMount(registration, () => table.RemoveCanvas(registration));
    }

    private static PluginCanvasRegistration MapCanvas(
        Harness harness, PluginMarkupCanvases table, List<(int, int)> sizes, bool pointer = false, bool outside = false)
    {
        PluginCanvasRegistration registration =
            RegisterInMarkup(harness, "map", painter => sizes.Add((painter.Width, painter.Height)), pointer, outside);
        AddToTable(harness, table, registration);
        return registration;
    }

    [Fact]
    public void ACanvasRegisteredFirstIsHostedWhenItsWindowMounts()
    {
        var harness = new Harness();
        PluginMarkupCanvases table = Table(harness);
        var sizes = new List<(int, int)>();
        PluginCanvasRegistration registration = MapCanvas(harness, table, sizes);
        harness.Frame();
        Assert.Empty(sizes);
        Assert.False(registration.IsAvailable);
        Assert.Single(harness.Reports, line => line.Contains("waiting for a <canvas id=\"map\">"));

        MountWindow(harness, table, GrowMap);
        harness.Frame();

        Assert.Equal([(300, 186)], sizes);
        Assert.True(registration.IsAvailable);
        Assert.Single(harness.Reports, line => line.Contains("waiting"));
    }

    [Fact]
    public void AWindowMountedFirstHostsTheCanvasWhenItIsRegistered()
    {
        var harness = new Harness();
        PluginMarkupCanvases table = Table(harness);
        MountWindow(harness, table, GrowMap);
        harness.Frame();

        var sizes = new List<(int, int)>();
        MapCanvas(harness, table, sizes);
        harness.Frame();

        Assert.Equal([(300, 186)], sizes);
        Assert.Empty(harness.Reports);
    }

    [Fact]
    public void AHiddenPageOrAHiddenCanvasPaintsNothing()
    {
        var harness = new Harness();
        PluginMarkupCanvases table = Table(harness);
        var binding = new PageBinding { Shown = false };
        MountWindow(harness, table, """
            <group x="0" y="0" w="300" h="200" visible="{Shown}">
              <canvas id="map" x="0" y="0" w="100" h="100" />
            </group>
            """, binding);
        var sizes = new List<(int, int)>();
        PluginCanvasRegistration registration = MapCanvas(harness, table, sizes);

        harness.Frame();
        Assert.Empty(sizes);

        binding.Shown = true;
        harness.Frame();
        Assert.Equal([(100, 100)], sizes);

        registration.IsVisible = false;
        registration.Invalidate();
        harness.Frame();
        Assert.Single(sizes);
    }

    [Fact]
    public void InAScrolledGroupAPressArrivesInCanvasPixels()
    {
        var harness = new Harness();
        PluginMarkupCanvases table = Table(harness);
        MarkupWindow window = MountWindow(harness, table, """
            <group name="g" x="0" y="0" w="100" h="100" scroll="y">
              <canvas id="map" x="0" y="150" w="80" h="40" />
            </group>
            """);
        var sizes = new List<(int, int)>();
        PluginCanvasRegistration registration = MapCanvas(harness, table, sizes, pointer: true);
        var events = new List<PluginPointerEvent>();
        registration.PointerHandler = events.Add;
        harness.Frame();

        UiScrollArea area = ((IUiScrollHost)Find(window.Frame, "g")).ScrollArea!;
        area.Y.SetScrollY(90);
        harness.Frame();
        harness.Root.OnMouseDown(UiMouseButton.Left, 10, 70);
        harness.Root.OnMouseUp(UiMouseButton.Left, 10, 70);

        Assert.Equal(90, area.Y.ScrollY);
        Assert.Equal(new PluginPoint(10, 10), Assert.Single(events, e => e.Kind == PluginPointerEventKind.Down).Position);
    }

    [Fact]
    public void AWheelOverACanvasWithoutPointerInputScrollsItsGroup()
    {
        var harness = new Harness();
        PluginMarkupCanvases table = Table(harness);
        MarkupWindow window = MountWindow(harness, table, """
            <group name="g" x="0" y="0" w="100" h="100" scroll="y">
              <canvas id="map" x="0" y="0" w="80" h="300" />
            </group>
            """);
        MapCanvas(harness, table, []);
        harness.Frame();

        harness.Root.OnMouseMove(40, 40);
        harness.Root.OnScroll(0, -1, shift: false);
        harness.Frame();

        Assert.Equal(48, ((IUiScrollHost)Find(window.Frame, "g")).ScrollArea!.Y.ScrollY);
    }

    [Fact]
    public void OutsidePressesReachAShownHostedCanvasOnly()
    {
        var harness = new Harness();
        PluginMarkupCanvases table = Table(harness);
        var binding = new PageBinding();
        MountWindow(harness, table, """
            <group x="0" y="0" w="300" h="200" visible="{Shown}">
              <canvas id="map" x="20" y="20" w="100" h="100" />
            </group>
            """, binding);
        PluginCanvasRegistration registration = MapCanvas(harness, table, [], pointer: true, outside: true);
        var events = new List<PluginPointerEvent>();
        registration.PointerHandler = events.Add;
        harness.Frame();

        table.ReportPress(UiMouseButton.Left, 250, 150);
        binding.Shown = false;
        harness.Frame();
        table.ReportPress(UiMouseButton.Left, 250, 150);

        PluginPointerEvent outside = Assert.Single(events);
        Assert.Equal(PluginPointerEventKind.PressedOutside, outside.Kind);
        Assert.Equal(new PluginPoint(230, 130), outside.Position);
    }

    [Fact]
    public void ACanvasOutlivesItsWindowAndIsHostedAgainByTheNext()
    {
        var harness = new Harness();
        PluginMarkupCanvases table = Table(harness);
        var sizes = new List<(int, int)>();
        PluginCanvasRegistration registration = MapCanvas(harness, table, sizes);
        MarkupWindow first = MountWindow(harness, table, GrowMap);
        harness.Frame();
        PluginCanvasElement element = Assert.Single(first.Canvases).Hosted!;
        harness.Device.Clear();

        UnmountWindow(harness, table, first);

        Assert.False(registration.IsAvailable);
        Assert.Equal(0, element.TargetCount);
        Assert.Null(Assert.Single(first.Canvases).Hosted);
        Assert.Single(harness.Reports, line => line.Contains("waiting"));

        MountWindow(harness, table, GrowMap);
        harness.Frame();
        Assert.True(registration.IsAvailable);
        Assert.Equal(2, sizes.Count);
    }

    [Fact]
    public void DisposingTheCanvasEmptiesItsElement()
    {
        var harness = new Harness();
        PluginMarkupCanvases table = Table(harness);
        PluginCanvasRegistration registration = MapCanvas(harness, table, []);
        MarkupWindow window = MountWindow(harness, table, GrowMap);
        harness.Frame();

        registration.Dispose();

        Assert.Null(Assert.Single(window.Canvases).Hosted);
        Assert.False(registration.IsAvailable);
        harness.Frame();
    }

    [Fact]
    public void TheFirstOfTwoWindowsHostsTheCanvasAndTheOtherTakesOver()
    {
        var harness = new Harness();
        PluginMarkupCanvases table = Table(harness);
        var sizes = new List<(int, int)>();
        PluginCanvasRegistration registration = MapCanvas(harness, table, sizes);
        MarkupWindow first = MountWindow(harness, table, GrowMap);
        MarkupWindow second = MountWindow(harness, table, GrowMap, x: 400);
        harness.Frame();
        Assert.Single(sizes);

        Assert.NotNull(Assert.Single(first.Canvases).Hosted);
        Assert.Null(Assert.Single(second.Canvases).Hosted);
        Assert.Single(harness.Reports, line => line.Contains("another window"));

        UnmountWindow(harness, table, first);

        Assert.NotNull(Assert.Single(second.Canvases).Hosted);
        Assert.True(registration.IsAvailable);
        harness.Frame();
        Assert.Equal(2, sizes.Count);
    }

    [Fact]
    public void AnElementShadowedBeforeItsCanvasArrivesSaysWhyOnce()
    {
        var harness = new Harness();
        PluginMarkupCanvases table = Table(harness);
        MarkupWindow first = MountWindow(harness, table, GrowMap);
        MarkupWindow second = MountWindow(harness, table, GrowMap, x: 400);

        MapCanvas(harness, table, []);
        harness.Frame();

        Assert.NotNull(Assert.Single(first.Canvases).Hosted);
        Assert.Null(Assert.Single(second.Canvases).Hosted);
        Assert.Single(harness.Reports, line => line.Contains("another window"));
    }

    [Fact]
    public void ACanvasDisposedWhileItsWindowIsTornDownLeavesCleanly()
    {
        var harness = new Harness();
        PluginMarkupCanvases table = Table(harness);
        MarkupWindow window = MountWindow(harness, table, GrowMap);
        PluginCanvasRegistration registration = MapCanvas(harness, table, [], pointer: true);
        var events = new List<PluginPointerEventKind>();
        registration.PointerHandler = e =>
        {
            events.Add(e.Kind);
            if (e.Kind == PluginPointerEventKind.Cancelled) registration.Dispose();
        };
        harness.Frame();
        harness.Root.OnMouseDown(UiMouseButton.Left, 40, 40);

        UnmountWindow(harness, table, window);

        Assert.Contains(PluginPointerEventKind.Cancelled, events);
        Assert.False(registration.IsAvailable);
        Assert.Null(Assert.Single(window.Canvases).Hosted);
    }

    [Fact]
    public void ACanvasElementNamingAPlainCanvasShowsNothingAndSaysWhy()
    {
        var harness = new Harness();
        PluginMarkupCanvases table = Table(harness);
        harness.Registry.RegisterCanvas(harness.Owner, new PluginCanvasDescriptor("map", 50, 50), _ => { });
        MarkupWindow window = MountWindow(harness, table, GrowMap);
        harness.Frame();

        Assert.Null(Assert.Single(window.Canvases).Hosted);
        Assert.Single(harness.Reports, line => line.Contains("without InMarkup"));
    }

    private static UiElement Find(UiElement root, string name)
    {
        if (root.Name == name) return root;
        foreach (UiElement child in root.Children)
        {
            UiElement? found = TryFindNamed(child, name);
            if (found is not null) return found;
        }
        throw new InvalidOperationException($"no element named {name}");
    }

    private static UiElement? TryFindNamed(UiElement element, string name)
    {
        if (element.Name == name) return element;
        foreach (UiElement child in element.Children)
        {
            UiElement? found = TryFindNamed(child, name);
            if (found is not null) return found;
        }
        return null;
    }
}
