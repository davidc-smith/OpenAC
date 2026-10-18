using System.Collections.Generic;
using System.Linq;
using AcDream.App.Plugins;
using AcDream.App.Rendering;
using AcDream.App.Rendering.Gpu;
using AcDream.App.Tests.Rendering.Gpu;
using AcDream.App.UI;
using AcDream.App.UI.Layout;
using AcDream.Plugin.Abstractions;

namespace AcDream.App.Tests.UI.Layout;

/// <summary>
/// Canvases are stacked in two layers, under and over every window. Each
/// plugin has a group per layer, stacked in the order its first canvas
/// mounted; inside a group its canvases are ranked by ZOrder, then by
/// registration, and restacked when the plugin changes a ZOrder. A canvas
/// over windows takes the pointer from the windows beneath it, gets
/// nothing under a modal dialog, and lets a drag's drop through.
/// </summary>
public sealed class PluginCanvasStackTests
{
    private sealed class FrameSource : ICurrentGpuFrameSource
    {
        public IGpuFrame? CurrentFrame { get; set; }
    }

    /// <summary>A window that can start a drag and records what is dropped on it.</summary>
    private sealed class DragWindow : UiPanel
    {
        public List<object?> Drops { get; } = [];
        public bool StartsDrags { get; init; }

        public override bool IsDragSource => StartsDrags;

        public override object? GetDragPayload() => StartsDrags ? "item" : null;

        public override bool OnEvent(in UiEvent e)
        {
            if (e.Type == UiEventType.DropReleased && ReferenceEquals(e.Target, this))
            {
                Drops.Add(e.Payload);
                return true;
            }
            return base.OnEvent(in e);
        }
    }

    private sealed class Harness
    {
        private readonly PluginCanvasSurface _surface;
        private long _now;

        public Harness()
        {
            Host = UiOverlayHost.Mount(Root);
            World = new PluginCanvasStack(Host.AddLayer("PluginCanvases"));
            AboveWindows = new PluginCanvasStack(Host.AddLayerAboveWindows("PluginCanvasesAboveWindows"));
            var services = new PluginCanvasHostServices(new RecordingGpuDevice(), new FrameSource(), "unused", null);
            _surface = new PluginCanvasSurface(services, font: null);
        }

        public BufferedUiRegistry Registry { get; } = new();
        public UiRoot Root { get; } = new() { Width = 800f, Height = 600f };
        public UiOverlayHost Host { get; }
        public PluginCanvasStack World { get; }
        public PluginCanvasStack AboveWindows { get; }
        public List<PluginPointerEvent> Events { get; } = [];

        /// <summary>Mounts as the runtime does: into the stack of the descriptor's layer.</summary>
        public (PluginCanvasRegistration Registration, PluginCanvasElement Element) Mount(
            PluginUiOwner owner, PluginCanvasDescriptor descriptor)
        {
            var registration = (PluginCanvasRegistration)Registry.RegisterCanvas(owner, descriptor, _ => { });
            ((IPluginCanvas)registration).PointerHandler = Events.Add;
            Assert.Single(Registry.DrainCanvases());
            var element = new PluginCanvasElement(registration, _surface, () => Registry.FindImages(owner));
            PluginCanvasStack stack = descriptor.Layer == PluginCanvasLayer.AboveWindows ? AboveWindows : World;
            stack.Add(element);
            Registry.CompleteCanvasMount(registration, () =>
            {
                PluginCanvasStack.Remove(element);
                element.ReleaseTargets();
            });
            Tick();
            return (registration, element);
        }

        public void Tick()
        {
            _now += 16;
            Root.Tick(0.016, _now);
        }

        /// <summary>Every canvas element under the root, back to front, as the root draws them.</summary>
        public List<PluginCanvasElement> DrawOrder()
        {
            var order = new List<PluginCanvasElement>();
            Collect(Root, order);
            return order;
        }

        private static void Collect(UiElement element, List<PluginCanvasElement> order)
        {
            if (element is PluginCanvasElement canvas)
                order.Add(canvas);
            foreach (UiElement child in element.ChildrenBackToFrontSnapshot())
                Collect(child, order);
        }
    }

    private static readonly PluginUiOwner A = new("a.plugin", "A");
    private static readonly PluginUiOwner B = new("b.plugin", "B");

    private static PluginCanvasDescriptor Canvas(
        string id,
        int zOrder = 0,
        PluginCanvasLayer layer = PluginCanvasLayer.World,
        bool input = false) =>
        new(id, 100, 100) { ZOrder = zOrder, Layer = layer, AcceptsPointerInput = input };

    private static DragWindow Window(UiRoot root, bool startsDrags = false, float left = 0f)
    {
        var window = new DragWindow { Left = left, Width = 200f, Height = 200f, StartsDrags = startsDrags };
        root.AddChild(window);
        root.BringToFront(window);
        return window;
    }

    [Fact]
    public void TheTwoLayersSitUnderAndOverTheWindowBand()
    {
        var harness = new Harness();

        Assert.Same(harness.Host.Root, harness.World.Layer.Parent);
        Assert.Equal(UiOverlayZOrder.SharedHostRoot, harness.Host.Root.ZOrder);
        Assert.Same(harness.Root, harness.AboveWindows.Layer.Parent);
        Assert.Equal(UiBands.CanvasesAboveWindows, harness.AboveWindows.Layer.ZOrder);
        Assert.True(harness.World.Layer.Visible);
        Assert.True(harness.AboveWindows.Layer.Visible);
        Assert.Equal((800f, 600f), (harness.AboveWindows.Layer.Width, harness.AboveWindows.Layer.Height));

        harness.Root.Width = 1024f;
        harness.Root.Height = 768f;
        harness.Host.FollowRoot();
        Assert.Equal((1024f, 768f), (harness.AboveWindows.Layer.Width, harness.AboveWindows.Layer.Height));
    }

    [Fact]
    public void WithinAPluginHigherZOrderIsOnTopAndEqualValuesKeepRegistrationOrder()
    {
        var harness = new Harness();
        (_, PluginCanvasElement top) = harness.Mount(A, Canvas("top", zOrder: 5));
        (_, PluginCanvasElement first) = harness.Mount(A, Canvas("first"));
        (_, PluginCanvasElement second) = harness.Mount(A, Canvas("second"));
        (_, PluginCanvasElement bottom) = harness.Mount(A, Canvas("bottom", zOrder: -3));

        Assert.Equal([bottom, first, second, top], harness.DrawOrder());
        Assert.Equal([0, 1, 2, 3], harness.DrawOrder().Select(element => element.ZOrder));
    }

    [Fact]
    public void APluginsZOrderNeverLiftsItOverAPluginThatMountedFirst()
    {
        var harness = new Harness();
        (_, PluginCanvasElement b) = harness.Mount(B, Canvas("hud"));
        (_, PluginCanvasElement a) = harness.Mount(A, Canvas("hud", zOrder: 1_000));
        (_, PluginCanvasElement bLater) = harness.Mount(B, Canvas("map", zOrder: -1_000));

        Assert.Equal([bLater, b, a], harness.DrawOrder());
        Assert.NotSame(b.Parent, a.Parent);
        Assert.Same(b.Parent, bLater.Parent);
    }

    [Fact]
    public void ChangingZOrderRestacksOnTheNextTick()
    {
        var harness = new Harness();
        (PluginCanvasRegistration back, PluginCanvasElement backElement) = harness.Mount(A, Canvas("back"));
        (_, PluginCanvasElement frontElement) = harness.Mount(A, Canvas("front"));
        Assert.Equal([backElement, frontElement], harness.DrawOrder());

        ((IPluginCanvas)back).ZOrder = 1;
        harness.Tick();

        Assert.Equal([frontElement, backElement], harness.DrawOrder());
    }

    [Fact]
    public void ADisposedCanvasLeavesItsGroupAndTheRestCloseUp()
    {
        var harness = new Harness();
        (PluginCanvasRegistration first, PluginCanvasElement firstElement) = harness.Mount(A, Canvas("first"));
        (_, PluginCanvasElement second) = harness.Mount(A, Canvas("second"));
        (_, PluginCanvasElement third) = harness.Mount(A, Canvas("third"));

        first.Dispose();

        Assert.Null(firstElement.Parent);
        Assert.Equal([second, third], harness.DrawOrder());
        Assert.Equal([0, 1], harness.DrawOrder().Select(element => element.ZOrder));
    }

    [Fact]
    public void EveryWorldCanvasDrawsBeforeEveryWindowAndEveryCanvasOverWindowsAfter()
    {
        var harness = new Harness();
        DragWindow window = Window(harness.Root);
        (_, PluginCanvasElement under) = harness.Mount(A, Canvas("under", zOrder: 9));
        (_, PluginCanvasElement over) = harness.Mount(A, Canvas("over", layer: PluginCanvasLayer.AboveWindows));

        UiElement[] rootOrder = harness.Root.ChildrenBackToFrontSnapshot();
        int Index(UiElement element)
        {
            while (!ReferenceEquals(element.Parent, harness.Root))
                element = element.Parent!;
            return Array.IndexOf(rootOrder, element);
        }

        Assert.True(Index(under) < Index(window));
        Assert.True(Index(window) < Index(over));
    }

    [Fact]
    public void AnInputCanvasOverWindowsTakesThePointerFromTheWindowBeneath()
    {
        var harness = new Harness();
        DragWindow window = Window(harness.Root);
        (_, PluginCanvasElement over) = harness.Mount(A, Canvas("over", layer: PluginCanvasLayer.AboveWindows, input: true));
        (_, PluginCanvasElement under) = harness.Mount(B, Canvas("under", input: true));

        Assert.Same(over, harness.Root.Pick(50, 50));
        Assert.NotSame(under, harness.Root.Pick(50, 50));
        harness.Root.OnMouseDown(UiMouseButton.Left, 50, 50);
        harness.Root.OnMouseUp(UiMouseButton.Left, 50, 50);
        Assert.Equal([PluginPointerEventKind.Down, PluginPointerEventKind.Up], harness.Events.Select(e => e.Kind));

        // Outside the canvas the window beneath still answers.
        Assert.NotSame(over, harness.Root.Pick(150, 150));
        Assert.NotNull(harness.Root.Pick(150, 150));
    }

    [Fact]
    public void AWorldInputCanvasUnderAWindowGetsNothingWhereTheWindowIs()
    {
        var harness = new Harness();
        (_, PluginCanvasElement under) = harness.Mount(A, Canvas("under", input: true));
        Window(harness.Root);

        Assert.NotSame(under, harness.Root.Pick(50, 50));
    }

    [Fact]
    public void UnderAModalDialogACanvasOverWindowsGetsNoInput()
    {
        var harness = new Harness();
        harness.Mount(A, Canvas("over", layer: PluginCanvasLayer.AboveWindows, input: true));
        var dialog = new UiPanel { Left = 400f, Top = 400f, Width = 100f, Height = 100f };
        harness.Root.AddChild(dialog);
        harness.Root.BringToFront(dialog, UiBand.DialogsAndTooltips);
        harness.Root.Modal = dialog;

        Assert.Null(harness.Root.Pick(50, 50));
        harness.Root.OnMouseDown(UiMouseButton.Left, 50, 50);
        harness.Root.OnMouseUp(UiMouseButton.Left, 50, 50);

        Assert.Empty(harness.Events);
    }

    [Fact]
    public void ADragsDropGoesThroughACanvasToTheWindowBeneath()
    {
        var harness = new Harness();
        DragWindow source = Window(harness.Root, startsDrags: true, left: 400f);
        DragWindow target = Window(harness.Root);
        harness.Mount(A, Canvas("over", layer: PluginCanvasLayer.AboveWindows, input: true));

        harness.Root.OnMouseDown(UiMouseButton.Left, 450, 50);
        harness.Root.OnMouseMove(300, 50);
        harness.Root.OnMouseMove(50, 50);
        Assert.Same(source, harness.Root.DragSource);
        Assert.Same(target, harness.Root.Pick(50, 50));
        harness.Root.OnMouseUp(UiMouseButton.Left, 50, 50);

        Assert.Equal(["item"], target.Drops);
        Assert.Empty(harness.Events);
        Assert.Null(harness.Root.DragSource);
    }

    [Fact]
    public void DrainingHandsCanvasesOutInRegistrationOrderEvenAfterRemovals()
    {
        var registry = new BufferedUiRegistry();
        IPluginCanvas gone = registry.RegisterCanvas(A, Canvas("gone"), _ => { });
        registry.RegisterCanvas(A, Canvas("first"), _ => { });
        gone.Dispose();
        registry.RegisterCanvas(B, Canvas("second"), _ => { });
        registry.RegisterCanvas(A, Canvas("third"), _ => { });

        Assert.Equal(
            ["first", "second", "third"],
            registry.DrainCanvases().Select(registration => registration.CanvasId));
    }
}
