using System.Collections.Generic;
using System.Numerics;
using AcDream.App.Plugins;
using AcDream.App.Rendering;
using AcDream.App.Rendering.Gpu;
using AcDream.App.Tests.Rendering.Gpu;
using AcDream.App.UI;
using AcDream.App.UI.Layout;
using AcDream.Plugin.Abstractions;
using Silk.NET.Input;

namespace AcDream.App.Tests.UI.Layout;

/// <summary>
/// Keyboard input on a plugin canvas: a canvas that opted in and has a
/// key handler takes the interface's keyboard focus from a left press or
/// on request, never from anything else that has it; while focused it
/// gets keys, typed text and its own repeat, and the game gets none; and
/// focus goes back, with exactly one focus lost, on every path that ends
/// it. A canvas that did not opt in is untouched by all of it.
/// </summary>
public sealed class PluginCanvasKeyboardTests
{
    private sealed class FrameSource : ICurrentGpuFrameSource
    {
        public IGpuFrame? CurrentFrame { get; set; }
    }

    /// <summary>A clock the test advances by hand: each guarded call costs what the test says.</summary>
    private sealed class ManualClock
    {
        public double Milliseconds { get; private set; }
        public double NextCallCosts { get; set; }
        private bool _atCallStart = true;

        public double Read()
        {
            if (_atCallStart)
            {
                _atCallStart = false;
                return Milliseconds;
            }
            _atCallStart = true;
            Milliseconds += NextCallCosts;
            return Milliseconds;
        }
    }

    private sealed class Harness
    {
        public RecordingGpuDevice Device { get; } = new();
        public FrameSource Frames { get; } = new();
        public BufferedUiRegistry Registry { get; } = new();
        public UiRoot Root { get; } = new() { Width = 800f, Height = 600f };
        public UiOverlayLayer Layer { get; }
        public PluginCanvasSurface Surface { get; }
        public TextRenderer MainRenderer { get; }
        public UiRenderContext MainContext { get; }
        public List<string> Reports { get; } = [];
        public ManualClock Clock { get; } = new();
        public PluginKeyModifiers Modifiers { get; set; }
        public bool KeyboardCaptured { get; set; }
        public PluginUiOwner Owner { get; } = new("example.plugin", "Example");
        public List<(UiMouseButton Button, int X, int Y)> WorldPresses { get; } = [];
        private long _now;

        public Harness()
        {
            UiOverlayHost host = UiOverlayHost.Mount(Root);
            Layer = host.AddLayer("PluginCanvases");
            Layer.Visible = true;
            var services = new PluginCanvasHostServices(Device, Frames, "unused", null);
            Surface = new PluginCanvasSurface(services, font: null);
            MainRenderer = new TextRenderer(Device, Frames, "unused");
            MainContext = new UiRenderContext(MainRenderer, new Vector2(800f, 600f));
            Root.WorldMouseFallThrough += (button, x, y, _) => WorldPresses.Add((button, x, y));
        }

        public (PluginCanvasRegistration Registration, PluginCanvasElement Element) Mount(
            PluginCanvasDescriptor descriptor,
            Func<PluginKeyEvent, bool>? keys,
            Action<PluginPointerEvent>? pointer = null,
            Action<IPluginPainter>? paint = null)
        {
            var registration = (PluginCanvasRegistration)Registry.RegisterCanvas(Owner, descriptor, paint ?? (_ => { }));
            IPluginCanvas canvas = registration;
            canvas.KeyHandler = keys;
            canvas.PointerHandler = pointer;
            Assert.Single(Registry.DrainCanvases());
            var element = new PluginCanvasElement(
                registration, Surface, () => Registry.FindImages(Owner), Reports.Add, Clock.Read,
                () => Modifiers, keyboardCaptured: () => KeyboardCaptured);
            Layer.AddChild(element, takesInput: descriptor.AcceptsPointerInput);
            Registry.CompleteCanvasMount(registration, () =>
            {
                Layer.RemoveChild(element);
                element.ReleaseTargets();
            });
            Tick();
            return (registration, element);
        }

        public void Tick(double seconds = 0.016)
        {
            _now += (long)(seconds * 1000.0);
            Root.Tick(seconds, _now);
        }

        /// <summary>One interface frame with a GPU frame open, so an invalidated canvas paints.</summary>
        public void Frame()
        {
            using IGpuFrame frame = Device.BeginFrame();
            Frames.CurrentFrame = frame;
            try
            {
                Tick();
                MainRenderer.Begin(new Vector2(Root.Width, Root.Height));
                MainContext.Begin(new Vector2(Root.Width, Root.Height), null);
                Root.Draw(MainContext);
                MainRenderer.Flush(null);
            }
            finally
            {
                Frames.CurrentFrame = null;
            }
        }

        public void Press(int x, int y)
        {
            Root.OnMouseDown(UiMouseButton.Left, x, y);
            Root.OnMouseUp(UiMouseButton.Left, x, y);
        }

        public void Type(Key key)
        {
            Root.OnKeyDown((int)key);
            Root.OnKeyUp((int)key);
        }
    }

    /// <summary>A plugin's key handler that records every event and answers as told.</summary>
    private sealed class Recorder
    {
        public List<PluginKeyEvent> Events { get; } = [];
        public bool HandlesEscape { get; set; }

        public bool Handle(PluginKeyEvent e)
        {
            Events.Add(e);
            return e.Key != PluginKey.Escape || HandlesEscape;
        }

        public List<PluginKeyEventKind> Kinds => Events.ConvertAll(e => e.Kind);

        public int Count(PluginKeyEventKind kind) => Events.FindAll(e => e.Kind == kind).Count;
    }

    // At (10, 20), 200 x 100: a press at (100, 60) lands on it.
    private static PluginCanvasDescriptor Pad(bool pointer = true, bool keyboard = true) =>
        new("pad", 200, 100)
        {
            Offset = new PluginPoint(10, 20),
            AcceptsPointerInput = pointer,
            AcceptsKeyboardInput = keyboard,
        };

    private static (Harness Harness, PluginCanvasRegistration Registration, PluginCanvasElement Element, Recorder Keys)
        MountedPad(bool pointer = true, bool keyboard = true)
    {
        var harness = new Harness();
        var keys = new Recorder();
        (PluginCanvasRegistration registration, PluginCanvasElement element) =
            harness.Mount(Pad(pointer, keyboard), keys.Handle, pointer: _ => { });
        return (harness, registration, element, keys);
    }

    private static (Harness Harness, PluginCanvasRegistration Registration, PluginCanvasElement Element, Recorder Keys)
        FocusedPad()
    {
        var mounted = MountedPad();
        mounted.Harness.Press(100, 60);
        Assert.True(mounted.Registration.HasKeyboardFocus);
        mounted.Keys.Events.Clear();
        return mounted;
    }

    [Fact]
    public void ALeftPressGivesTheCanvasFocus()
    {
        (Harness harness, PluginCanvasRegistration registration, PluginCanvasElement element, Recorder keys) = MountedPad();

        harness.Press(100, 60);

        Assert.Same(element, harness.Root.KeyboardFocus);
        Assert.True(registration.HasKeyboardFocus);
        Assert.Equal(
            [new PluginKeyEvent(PluginKeyEventKind.FocusGained, PluginKey.Unknown, PluginKeyModifiers.None)],
            keys.Events);
    }

    [Fact]
    public void ARightPressDoesNotGiveFocus()
    {
        (Harness harness, PluginCanvasRegistration registration, _, Recorder keys) = MountedPad();

        harness.Root.OnMouseDown(UiMouseButton.Right, 100, 60);
        harness.Root.OnMouseUp(UiMouseButton.Right, 100, 60);

        Assert.False(registration.HasKeyboardFocus);
        Assert.Empty(keys.Events);
    }

    [Fact]
    public void ACanvasThatDidNotOptInNeverTakesFocusAndSeesNoKeys()
    {
        (Harness harness, PluginCanvasRegistration registration, _, Recorder keys) = MountedPad(keyboard: false);

        harness.Press(100, 60);
        harness.Type(Key.A);

        Assert.Null(harness.Root.KeyboardFocus);
        Assert.False(registration.RequestKeyboardFocus());
        Assert.False(registration.HasKeyboardFocus);
        Assert.Empty(keys.Events);
    }

    [Fact]
    public void WithoutAKeyHandlerAPressDoesNotGiveFocusButStillReachesThePointerHandler()
    {
        var harness = new Harness();
        var pointer = new List<PluginPointerEvent>();
        (PluginCanvasRegistration registration, _) = harness.Mount(Pad(), keys: null, pointer: pointer.Add);

        harness.Press(100, 60);

        Assert.Null(harness.Root.KeyboardFocus);
        Assert.False(registration.HasKeyboardFocus);
        Assert.Equal([PluginPointerEventKind.Down, PluginPointerEventKind.Up], pointer.ConvertAll(e => e.Kind));
    }

    [Fact]
    public void AKeyboardOnlyCanvasTakesFocusOnRequestOnce()
    {
        (Harness harness, PluginCanvasRegistration registration, PluginCanvasElement element, Recorder keys) = MountedPad(pointer: false);

        Assert.True(registration.RequestKeyboardFocus());
        Assert.True(registration.RequestKeyboardFocus());

        Assert.Same(element, harness.Root.KeyboardFocus);
        Assert.Equal([PluginKeyEventKind.FocusGained], keys.Kinds);
        // Click-through for the pointer all the same.
        harness.Press(100, 60);
        Assert.Equal(2, harness.WorldPresses.Count);
    }

    [Fact]
    public void ARequestNeverTakesFocusFromATextField()
    {
        (Harness harness, PluginCanvasRegistration registration, _, Recorder keys) = MountedPad();
        var field = new UiField { Width = 100, Height = 20 };
        harness.Root.AddChild(field);
        harness.Root.SetKeyboardFocus(field);

        Assert.False(registration.RequestKeyboardFocus());

        Assert.Same(field, harness.Root.KeyboardFocus);
        Assert.Empty(keys.Events);
    }

    [Fact]
    public void ARequestIsRefusedWhileAModalIsOpen()
    {
        (Harness harness, PluginCanvasRegistration registration, _, Recorder keys) = MountedPad();
        var dialog = new UiPanel { Width = 300, Height = 150 };
        harness.Root.AddChild(dialog);
        harness.Root.Modal = dialog;

        Assert.False(registration.RequestKeyboardFocus());

        harness.Root.Modal = null;
        Assert.True(registration.RequestKeyboardFocus());
        Assert.Equal(1, keys.Count(PluginKeyEventKind.FocusGained));
    }

    [Fact]
    public void ARequestIsRefusedWhileAKeyRebindIsBeingCaptured()
    {
        (Harness harness, PluginCanvasRegistration registration, _, _) = MountedPad();
        harness.KeyboardCaptured = true;

        Assert.False(registration.RequestKeyboardFocus());

        harness.KeyboardCaptured = false;
        Assert.True(registration.RequestKeyboardFocus());
    }

    [Fact]
    public void ARequestIsRefusedWhileTheCanvasIsHiddenOrHasNoHandler()
    {
        (Harness harness, PluginCanvasRegistration registration, _, _) = MountedPad();
        IPluginCanvas canvas = registration;

        canvas.IsVisible = false;
        harness.Tick();
        Assert.False(canvas.RequestKeyboardFocus());

        canvas.IsVisible = true;
        harness.Tick();
        Func<PluginKeyEvent, bool>? handler = canvas.KeyHandler;
        canvas.KeyHandler = null;
        Assert.False(canvas.RequestKeyboardFocus());

        canvas.KeyHandler = handler;
        Assert.True(canvas.RequestKeyboardFocus());
    }

    [Fact]
    public void ARequestIsRefusedOnceTheCanvasIsDisposed()
    {
        (_, PluginCanvasRegistration registration, _, Recorder keys) = MountedPad();

        registration.Dispose();

        Assert.False(registration.RequestKeyboardFocus());
        Assert.False(registration.HasKeyboardFocus);
        Assert.Empty(keys.Events);
    }

    [Fact]
    public void APressElsewhereGivesFocusBack()
    {
        (Harness harness, PluginCanvasRegistration registration, _, Recorder keys) = FocusedPad();

        harness.Press(400, 300);

        Assert.False(registration.HasKeyboardFocus);
        Assert.Equal([PluginKeyEventKind.FocusLost], keys.Kinds);
    }

    [Fact]
    public void FocusMovesBetweenTwoCanvasesWithOneLostAndOneGained()
    {
        (Harness harness, PluginCanvasRegistration first, _, Recorder firstKeys) = FocusedPad();
        var secondKeys = new Recorder();
        (PluginCanvasRegistration second, _) = harness.Mount(
            new PluginCanvasDescriptor("other", 100, 100)
            {
                Offset = new PluginPoint(400, 300),
                AcceptsPointerInput = true,
                AcceptsKeyboardInput = true,
            },
            secondKeys.Handle,
            pointer: _ => { });

        harness.Press(450, 350);

        Assert.False(first.HasKeyboardFocus);
        Assert.True(second.HasKeyboardFocus);
        Assert.Equal([PluginKeyEventKind.FocusLost], firstKeys.Kinds);
        Assert.Equal([PluginKeyEventKind.FocusGained], secondKeys.Kinds);
    }

    [Fact]
    public void HidingTheCanvasGivesFocusBack()
    {
        (Harness harness, PluginCanvasRegistration registration, _, Recorder keys) = FocusedPad();

        registration.IsVisible = false;
        harness.Tick();
        harness.Type(Key.A);

        Assert.Null(harness.Root.KeyboardFocus);
        Assert.Equal([PluginKeyEventKind.FocusLost], keys.Kinds);
    }

    [Fact]
    public void DisposingTheCanvasGivesFocusBackBeforeTheHandlerIsDropped()
    {
        (Harness harness, PluginCanvasRegistration registration, _, Recorder keys) = FocusedPad();

        registration.Dispose();
        harness.Type(Key.A);

        Assert.Null(harness.Root.KeyboardFocus);
        Assert.Null(registration.KeyHandler);
        Assert.Equal([PluginKeyEventKind.FocusLost], keys.Kinds);
    }

    [Fact]
    public void TearingTheInterfaceDownGivesFocusBackAndKeepsTheHandler()
    {
        (Harness harness, PluginCanvasRegistration registration, _, Recorder keys) = FocusedPad();

        harness.Registry.UnbindCanvasHost();

        Assert.Null(harness.Root.KeyboardFocus);
        Assert.False(registration.HasKeyboardFocus);
        Assert.NotNull(registration.KeyHandler);
        Assert.Equal([PluginKeyEventKind.FocusLost], keys.Kinds);
    }

    [Fact]
    public void TakingTheHandlerAwayGivesFocusBackOnTheNextTick()
    {
        (Harness harness, PluginCanvasRegistration registration, _, Recorder keys) = FocusedPad();

        registration.KeyHandler = null;
        harness.Tick();

        Assert.Null(harness.Root.KeyboardFocus);
        Assert.False(registration.HasKeyboardFocus);
        Assert.Empty(keys.Events);
    }

    [Fact]
    public void AModalOpeningGivesFocusBack()
    {
        (Harness harness, PluginCanvasRegistration registration, _, Recorder keys) = FocusedPad();
        var dialog = new UiPanel { Width = 300, Height = 150 };
        harness.Root.AddChild(dialog);

        harness.Root.Modal = dialog;
        harness.Tick();

        Assert.False(registration.HasKeyboardFocus);
        Assert.Equal([PluginKeyEventKind.FocusLost], keys.Kinds);
    }

    [Fact]
    public void ReleasingFocusDeliversOneFocusLostAndASecondReleaseDoesNothing()
    {
        (Harness harness, PluginCanvasRegistration registration, _, Recorder keys) = FocusedPad();

        registration.ReleaseKeyboardFocus();
        registration.ReleaseKeyboardFocus();

        Assert.Null(harness.Root.KeyboardFocus);
        Assert.Equal([PluginKeyEventKind.FocusLost], keys.Kinds);
    }

    [Fact]
    public void ADroppedPaintCallbackGivesFocusBack()
    {
        var harness = new Harness();
        var keys = new Recorder();
        bool fail = false;
        (PluginCanvasRegistration registration, _) = harness.Mount(
            Pad(), keys.Handle, pointer: _ => { },
            paint: _ =>
            {
                if (fail) throw new InvalidOperationException("paint boom");
            });
        harness.Frame();
        harness.Press(100, 60);
        keys.Events.Clear();

        fail = true;
        registration.Invalidate();
        harness.Frame();

        Assert.True(registration.IsDropped);
        Assert.Null(harness.Root.KeyboardFocus);
        Assert.Equal([PluginKeyEventKind.FocusLost], keys.Kinds);
    }

    [Fact]
    public void AHandlerThatThrowsOnFocusGainedIsDroppedAndFocusEndsNowhere()
    {
        var harness = new Harness();
        (PluginCanvasRegistration registration, PluginCanvasElement element) = harness.Mount(
            Pad(), _ => throw new InvalidOperationException("focus boom"), pointer: _ => { });

        Assert.False(registration.RequestKeyboardFocus());

        Assert.True(element.IsKeyInputDropped);
        Assert.Null(harness.Root.KeyboardFocus);
        Assert.False(registration.HasKeyboardFocus);
        Assert.Contains("focus boom", Assert.Single(harness.Reports));
    }
}
