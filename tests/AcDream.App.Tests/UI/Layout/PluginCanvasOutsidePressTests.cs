using System.Collections.Generic;
using System.Numerics;
using AcDream.App.Plugins;
using AcDream.App.Rendering;
using AcDream.App.Rendering.Gpu;
using AcDream.App.Tests.Rendering.Gpu;
using AcDream.App.UI;
using AcDream.App.UI.Layout;
using AcDream.Plugin.Abstractions;

namespace AcDream.App.Tests.UI.Layout;

/// <summary>
/// A canvas that asked for outside presses hears of every press that starts
/// outside its rectangle -- over the world, a window, a dialog or another
/// canvas, its own plugin's included -- before that press reaches its
/// target, which still gets it. Nothing is reported for a press inside, while
/// hidden, while the canvas holds the pointer, or without the flag. The
/// screen size plugins are told is the rectangle both layers are laid out in.
/// </summary>
public sealed class PluginCanvasOutsidePressTests
{
    private sealed class FrameSource : ICurrentGpuFrameSource
    {
        public IGpuFrame? CurrentFrame { get; set; }
    }

    /// <summary>A window that records the presses it is the target of.</summary>
    private sealed class RecordingWindow : UiPanel
    {
        private readonly List<string> _log;

        public RecordingWindow(List<string> log) => _log = log;

        public override bool OnEvent(in UiEvent e)
        {
            if (ReferenceEquals(e.Target, this) && e.Type is UiEventType.MouseDown or UiEventType.RightDown)
            {
                _log.Add($"window {(e.Type == UiEventType.MouseDown ? "Left" : "Right")} down");
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
            // As the runtime wires it: the layer over the windows first.
            Root.PressStarting += (button, x, y) =>
            {
                AboveWindows.ReportPress(button, x, y);
                World.ReportPress(button, x, y);
            };
            Root.WorldMouseFallThrough += (button, x, y, _) => Log.Add($"world {button} down at {x},{y}");
        }

        public BufferedUiRegistry Registry { get; } = new();
        public UiRoot Root { get; } = new() { Width = 800f, Height = 600f };
        public UiOverlayHost Host { get; }
        public PluginCanvasStack World { get; }
        public PluginCanvasStack AboveWindows { get; }
        public PluginUiOwner Owner { get; } = new("example.plugin", "Example");
        public PluginKeyModifiers Modifiers { get; set; }

        /// <summary>Everything that happened, in order: plugin events and where presses landed.</summary>
        public List<string> Log { get; } = [];
        public List<(string CanvasId, PluginPointerEvent Event)> Events { get; } = [];

        public (PluginCanvasRegistration Registration, PluginCanvasElement Element) Mount(
            PluginCanvasDescriptor descriptor, Action<PluginPointerEvent>? handler = null)
        {
            var registration = (PluginCanvasRegistration)Registry.RegisterCanvas(Owner, descriptor, _ => { });
            ((IPluginCanvas)registration).PointerHandler = handler ?? (pointer =>
            {
                Events.Add((descriptor.CanvasId, pointer));
                Log.Add($"{descriptor.CanvasId} {pointer.Kind}");
            });
            Assert.Single(Registry.DrainCanvases());
            var element = new PluginCanvasElement(
                registration, _surface, () => Registry.FindImages(Owner), modifiers: () => Modifiers);
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

        public RecordingWindow Window(float left, float top)
        {
            var window = new RecordingWindow(Log) { Left = left, Top = top, Width = 200f, Height = 200f };
            Root.AddChild(window);
            Root.BringToFront(window);
            return window;
        }

        public void Tick()
        {
            _now += 16;
            Root.Tick(0.016, _now);
        }
    }

    /// <summary>A small menu over the windows at (300, 200), 100 by 50.</summary>
    private static PluginCanvasDescriptor Menu(bool wantsOutsidePresses = true, bool input = true) =>
        new("menu", 100, 50)
        {
            Layer = PluginCanvasLayer.AboveWindows,
            Offset = new PluginPoint(300, 200),
            AcceptsPointerInput = input,
            WantsOutsidePresses = wantsOutsidePresses,
        };

    /// <summary>A HUD under the windows at (500, 400), 200 by 100, taking input.</summary>
    private static PluginCanvasDescriptor Hud() =>
        new("hud", 200, 100)
        {
            Offset = new PluginPoint(500, 400),
            AcceptsPointerInput = true,
        };

    [Fact]
    public void APressOverTheWorldIsReportedInTheMenusPixelsAndStillReachesTheWorld()
    {
        var harness = new Harness();
        harness.Mount(Menu());
        harness.Modifiers = PluginKeyModifiers.Shift;

        harness.Root.OnMouseDown(UiMouseButton.Left, 10, 10);

        Assert.Equal(
            new PluginPointerEvent(
                PluginPointerEventKind.PressedOutside,
                new PluginPoint(10 - 300, 10 - 200),
                PluginPointerButton.Left,
                PluginKeyModifiers.Shift),
            Assert.Single(harness.Events).Event);
        Assert.Equal(["menu PressedOutside", "world Left down at 10,10"], harness.Log);
    }

    [Fact]
    public void APressOverAWindowIsReportedAndTheWindowStillGetsIt()
    {
        var harness = new Harness();
        harness.Mount(Menu());
        harness.Window(0f, 0f);

        harness.Root.OnMouseDown(UiMouseButton.Right, 50, 60);

        PluginPointerEvent reported = Assert.Single(harness.Events).Event;
        Assert.Equal(PluginPointerEventKind.PressedOutside, reported.Kind);
        Assert.Equal(PluginPointerButton.Right, reported.Button);
        Assert.Equal(new PluginPoint(-250, -140), reported.Position);
        Assert.Equal(0, reported.WheelDelta);
        Assert.Equal(["menu PressedOutside", "window Right down"], harness.Log);
    }

    [Fact]
    public void APressOnAnotherCanvasOfTheSamePluginIsReportedBeforeThatCanvasGetsItsDown()
    {
        var harness = new Harness();
        harness.Mount(Menu());
        harness.Mount(Hud());

        harness.Root.OnMouseDown(UiMouseButton.Right, 510, 420);

        Assert.Equal(["menu PressedOutside", "hud Down"], harness.Log);
        Assert.Equal(new PluginPoint(210, 220), harness.Events[0].Event.Position);
        Assert.Equal(new PluginPoint(10, 20), harness.Events[1].Event.Position);
    }

    [Fact]
    public void APressOutsideAnOpenDialogIsReportedThoughTheDialogSwallowsIt()
    {
        var harness = new Harness();
        harness.Mount(Menu());
        var dialog = new UiPanel { Left = 600f, Top = 0f, Width = 100f, Height = 100f };
        harness.Root.AddChild(dialog);
        harness.Root.Modal = dialog;

        harness.Root.OnMouseDown(UiMouseButton.Left, 10, 10);

        Assert.Equal(["menu PressedOutside"], harness.Log);
    }

    [Fact]
    public void APressInsideTheMenuIsItsOwnDownAndNotReportedAsOutside()
    {
        var harness = new Harness();
        harness.Mount(Menu());

        harness.Root.OnMouseDown(UiMouseButton.Left, 300, 200);
        harness.Root.OnMouseUp(UiMouseButton.Left, 300, 200);
        harness.Root.OnMouseDown(UiMouseButton.Left, 399, 249);

        Assert.Equal(["menu Down", "menu Up", "menu Down"], harness.Log);
    }

    [Fact]
    public void TheEdgesJustOutsideTheMenuAreOutside()
    {
        var harness = new Harness();
        harness.Mount(Menu());

        harness.Root.OnMouseDown(UiMouseButton.Left, 400, 225);
        harness.Root.OnMouseUp(UiMouseButton.Left, 400, 225);
        harness.Root.OnMouseDown(UiMouseButton.Left, 350, 199);

        Assert.Equal(
            [new PluginPoint(100, 25), new PluginPoint(50, -1)],
            harness.Events.ConvertAll(entry => entry.Event.Position));
        Assert.All(harness.Events, entry => Assert.Equal(PluginPointerEventKind.PressedOutside, entry.Event.Kind));
    }

    [Fact]
    public void AHiddenMenuHearsNothingAndTheWorldStillGetsThePress()
    {
        var harness = new Harness();
        (PluginCanvasRegistration registration, _) = harness.Mount(Menu());
        registration.IsVisible = false;

        harness.Root.OnMouseDown(UiMouseButton.Left, 10, 10);

        Assert.Equal(["world Left down at 10,10"], harness.Log);
    }

    [Fact]
    public void AMenuHiddenFromItsOwnReportDoesNotStopThePressReachingAnotherCanvas()
    {
        var harness = new Harness();
        (PluginCanvasRegistration menu, _) = harness.Mount(Menu());
        // The plugin closes its menu as soon as it hears of a press elsewhere.
        ((IPluginCanvas)menu).PointerHandler = pointer =>
        {
            harness.Log.Add($"menu {pointer.Kind}");
            menu.IsVisible = false;
        };
        harness.Mount(Hud());

        harness.Root.OnMouseDown(UiMouseButton.Left, 510, 420);
        harness.Root.OnMouseUp(UiMouseButton.Left, 510, 420);
        harness.Root.OnMouseDown(UiMouseButton.Left, 520, 420);

        Assert.Equal(["menu PressedOutside", "hud Down", "hud Up", "hud Down"], harness.Log);
    }

    [Fact]
    public void WithoutTheFlagNothingIsReported()
    {
        var harness = new Harness();
        harness.Mount(Menu(wantsOutsidePresses: false));
        harness.Window(0f, 0f);

        harness.Root.OnMouseDown(UiMouseButton.Left, 50, 50);
        harness.Root.OnMouseUp(UiMouseButton.Left, 50, 50);
        harness.Root.OnMouseDown(UiMouseButton.Left, 700, 10);

        Assert.Empty(harness.Events);
        Assert.Equal(["window Left down", "world Left down at 700,10"], harness.Log);
    }

    [Fact]
    public void WithoutAHandlerNothingIsReported()
    {
        var harness = new Harness();
        (PluginCanvasRegistration registration, _) = harness.Mount(Menu());
        ((IPluginCanvas)registration).PointerHandler = null;

        harness.Root.OnMouseDown(UiMouseButton.Left, 10, 10);

        Assert.Equal(["world Left down at 10,10"], harness.Log);
    }

    [Fact]
    public void AMenuHoldingThePointerFromItsOwnPressHearsNoOutsidePressForAnotherButton()
    {
        var harness = new Harness();
        harness.Mount(Menu());

        harness.Root.OnMouseDown(UiMouseButton.Left, 310, 210);
        harness.Root.OnMouseMove(10, 10);
        harness.Root.OnMouseDown(UiMouseButton.Right, 10, 10);
        harness.Root.OnMouseUp(UiMouseButton.Left, 10, 10);
        harness.Root.OnMouseDown(UiMouseButton.Left, 10, 10);

        Assert.DoesNotContain(
            harness.Events.GetRange(0, harness.Events.Count - 1),
            entry => entry.Event.Kind == PluginPointerEventKind.PressedOutside);
        Assert.Equal(PluginPointerEventKind.PressedOutside, harness.Events[^1].Event.Kind);
    }

    [Fact]
    public void AClickThroughCanvasHearsOutsidePressesAndLetsItsOwnRectangleThrough()
    {
        var harness = new Harness();
        harness.Mount(Menu(input: false));

        harness.Root.OnMouseDown(UiMouseButton.Left, 310, 210);
        harness.Root.OnMouseUp(UiMouseButton.Left, 310, 210);
        harness.Root.OnMouseDown(UiMouseButton.Left, 10, 10);

        Assert.Equal(
            ["world Left down at 310,210", "world Left down at 310,210", "menu PressedOutside", "world Left down at 10,10"],
            harness.Log);
    }

    [Fact]
    public void AThrowingHandlerIsDroppedAndThePressStillLands()
    {
        var harness = new Harness();
        harness.Mount(Menu(), _ => throw new InvalidOperationException("plugin bug"));

        harness.Root.OnMouseDown(UiMouseButton.Left, 10, 10);
        harness.Root.OnMouseUp(UiMouseButton.Left, 10, 10);
        harness.Root.OnMouseDown(UiMouseButton.Left, 20, 20);

        Assert.Equal(
            ["world Left down at 10,10", "world Left down at 10,10", "world Left down at 20,20"],
            harness.Log);
    }

    [Fact]
    public void ATopLeftCanvasPlacedFromTheScreenSizeSitsInTheCornerOfEitherLayerAfterAResize()
    {
        var harness = new Harness();
        (_, PluginCanvasElement world) = harness.Mount(new PluginCanvasDescriptor("world", 40, 30));
        (_, PluginCanvasElement above) = harness.Mount(
            new PluginCanvasDescriptor("above", 40, 30) { Layer = PluginCanvasLayer.AboveWindows });

        harness.Root.Width = 1280f;
        harness.Root.Height = 720f;
        harness.Host.FollowRoot();
        Vector2 screen = UiOverlayHost.ViewportOf(harness.Root);
        foreach (PluginCanvasElement element in new[] { world, above })
        {
            ((IPluginCanvas)element.Registration).Offset = new PluginPoint(screen.X - 40, screen.Y - 30);
        }
        harness.Tick();

        Assert.Equal(new Vector2(1280f, 720f), screen);
        foreach (PluginCanvasElement element in new[] { world, above })
        {
            Assert.Equal(screen, new Vector2(element.Parent!.Width, element.Parent.Height));
            Assert.Equal(new Vector2(1240f, 690f), element.ScreenPosition);
        }
    }
}
