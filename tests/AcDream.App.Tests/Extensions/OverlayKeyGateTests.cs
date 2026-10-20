using AcDream.App.Extensions.Overlay;
using AcDream.App.Input;
using AcDream.App.Rendering;
using AcDream.ClientExtensions;
using Silk.NET.Input;

namespace AcDream.App.Tests.Extensions;

[Collection("Rynth")]
public sealed class OverlayKeyGateTests : IDisposable
{
    private readonly ImGuiOverlay _overlay = new(_ => { });
    private readonly OverlayKeyGate _gate;
    private readonly TakingSink _sink = new();
    private readonly IDisposable _registration;

    public OverlayKeyGateTests()
    {
        _gate = new OverlayKeyGate(_overlay.OfferKeyPress, _overlay.SinkFocusLost);
        OverlayInputGate.BindKeys(_gate);
        _registration = _overlay.AddKeyboardSink(_sink);
    }

    public void Dispose()
    {
        OverlayInputGate.BindKeys(null);
        _registration.Dispose();
        _overlay.Dispose();
    }

    private sealed class TakingSink : IOverlayKeyboardSink
    {
        public int TakeVk { get; set; } = 0x70;
        public bool Throw { get; set; }
        public bool DeclineRepeats { get; set; }
        public List<OverlayKeyEvent> Presses { get; } = new();
        public int FocusLosses { get; private set; }
        public bool Capturing => false;
        public void KeyDown(OverlayKey key, OverlayModifiers modifiers) { }
        public void KeyUp(OverlayKey key) { }
        public void Character(char value) { }
        public bool KeyPress(OverlayKeyEvent e)
        {
            if (Throw) throw new InvalidOperationException("sink failed");
            Presses.Add(e);
            if (DeclineRepeats && e.Repeat) return false;
            return e.VirtualKey == TakeVk && (e.Modifiers & OverlayModifiers.Control) != 0;
        }
        public void FocusLost() => FocusLosses++;
    }

    private sealed class KeyboardSurface : IKeyboardEventSurface
    {
        private Action<Key>? _down, _up;
        public HashSet<Key> Held { get; } = new();
        public void AddKeyDown(Action<Key> callback) => _down = callback;
        public void RemoveKeyDown(Action<Key> callback) => _down = null;
        public void AddKeyUp(Action<Key> callback) => _up = callback;
        public void RemoveKeyUp(Action<Key> callback) => _up = null;
        public bool IsKeyPressed(Key key) => Held.Contains(key);
        public void Down(Key key) { Held.Add(key); _down?.Invoke(key); }
        public void Up(Key key) { Held.Remove(key); _up?.Invoke(key); }
    }

    [Theory]
    [InlineData(Key.A, 0x41, false)]
    [InlineData(Key.Number1, 0x31, false)]
    [InlineData(Key.F1, 0x70, false)]
    [InlineData(Key.F12, 0x7B, false)]
    [InlineData(Key.Keypad5, 0x65, false)]
    [InlineData(Key.KeypadEnter, 0x0D, true)]
    [InlineData(Key.KeypadDivide, 0x6F, true)]
    [InlineData(Key.Up, 0x26, true)]
    [InlineData(Key.Home, 0x24, true)]
    [InlineData(Key.ShiftLeft, 0xA0, false)]
    [InlineData(Key.ControlRight, 0xA3, true)]
    [InlineData(Key.AltLeft, 0xA4, false)]
    [InlineData(Key.Semicolon, 0xBA, false)]
    [InlineData(Key.GraveAccent, 0xC0, false)]
    [InlineData(Key.Unknown, 0, false)]
    public void SilkKeysMapToWindowsKeyCodes(Key key, int vk, bool extended) =>
        Assert.Equal((vk, extended), OverlayVirtualKeys.Map(key));

    [Fact]
    public void ATakenPressNeverReachesTheDispatcherOrMovement()
    {
        var surface = new KeyboardSurface();
        var source = SilkKeyboardSource.CreateDetached(surface, new HostQuiescenceGate());
        var seen = new List<string>();
        source.KeyDown += (key, _) => seen.Add("down " + key);
        source.KeyUp += (key, _) => seen.Add("up " + key);
        source.Attach();

        _gate.KeyDown(Key.F1, OverlayModifiers.Control);
        surface.Down(Key.F1);
        Assert.False(source.IsHeld(Key.F1));
        Assert.True(OverlayInputGate.CharTaken);
        _gate.KeyDown(Key.F1, OverlayModifiers.Control);
        surface.Down(Key.F1);
        _gate.KeyUp(Key.F1, OverlayModifiers.Control);
        surface.Up(Key.F1);

        Assert.Empty(seen);
        Assert.False(OverlayInputGate.CharTaken);
        Assert.Equal([false, true, false], _sink.Presses.Select(p => p.Repeat && p.Down));
        Assert.False(_sink.Presses[^1].Down);
        source.Dispose();
    }

    [Fact]
    public void AnUntakenPressPassesAsBefore()
    {
        var surface = new KeyboardSurface();
        var source = SilkKeyboardSource.CreateDetached(surface, new HostQuiescenceGate());
        var seen = new List<string>();
        source.KeyDown += (key, _) => seen.Add("down " + key);
        source.KeyUp += (key, _) => seen.Add("up " + key);
        source.Attach();

        _gate.KeyDown(Key.F1, OverlayModifiers.None);
        surface.Down(Key.F1);
        Assert.True(source.IsHeld(Key.F1));
        _gate.KeyUp(Key.F1, OverlayModifiers.None);
        surface.Up(Key.F1);

        Assert.Equal(["down F1", "up F1"], seen);
        source.Dispose();
    }

    [Fact]
    public void ATakenPressStaysTakenThroughADeclinedRepeat()
    {
        var surface = new KeyboardSurface();
        var source = SilkKeyboardSource.CreateDetached(surface, new HostQuiescenceGate());
        var seen = new List<string>();
        source.KeyDown += (key, _) => seen.Add("down " + key);
        source.KeyUp += (key, _) => seen.Add("up " + key);
        source.Attach();
        _sink.DeclineRepeats = true;

        _gate.KeyDown(Key.F1, OverlayModifiers.Control);
        surface.Down(Key.F1);
        _gate.KeyDown(Key.F1, OverlayModifiers.Control);
        Assert.True(OverlayInputGate.KeyTaken(Key.F1));
        surface.Down(Key.F1);
        Assert.False(source.IsHeld(Key.F1));
        _gate.KeyUp(Key.F1, OverlayModifiers.None);
        Assert.True(OverlayInputGate.KeyTaken(Key.F1));
        surface.Up(Key.F1);

        Assert.Empty(seen);
        source.Dispose();
    }

    [Fact]
    public void ATakenKeysReleaseAfterFocusLossIsStillTaken()
    {
        var surface = new KeyboardSurface();
        var source = SilkKeyboardSource.CreateDetached(surface, new HostQuiescenceGate());
        var seen = new List<string>();
        source.KeyDown += (key, _) => seen.Add("down " + key);
        source.KeyUp += (key, _) => seen.Add("up " + key);
        source.Attach();

        _gate.KeyDown(Key.F1, OverlayModifiers.Control);
        surface.Down(Key.F1);
        _gate.FocusLost();
        Assert.False(OverlayInputGate.KeyTaken(Key.F1));

        // GLFW sends the held key's release after the focus-lost callback.
        _gate.KeyUp(Key.F1, OverlayModifiers.None);
        Assert.True(OverlayInputGate.KeyTaken(Key.F1));
        surface.Up(Key.F1);
        Assert.Empty(seen);

        _gate.KeyDown(Key.A, OverlayModifiers.None);
        Assert.False(OverlayInputGate.KeyTaken(Key.F1));
        _gate.KeyUp(Key.F1, OverlayModifiers.None);
        Assert.False(OverlayInputGate.KeyTaken(Key.F1));   // only the one release
        source.Dispose();
    }

    [Fact]
    public void AKeyDownAfterFocusLossClearsTheReleasePending()
    {
        _gate.KeyDown(Key.F1, OverlayModifiers.Control);
        _gate.FocusLost();

        _gate.KeyDown(Key.F1, OverlayModifiers.None);
        Assert.False(OverlayInputGate.KeyTaken(Key.F1));
        _gate.KeyUp(Key.F1, OverlayModifiers.None);
        Assert.False(OverlayInputGate.KeyTaken(Key.F1));
    }

    [Fact]
    public void FocusLossClearsTakenKeysAndReachesTheSinks()
    {
        _gate.KeyDown(Key.F1, OverlayModifiers.Control);
        Assert.True(OverlayInputGate.KeyTaken(Key.F1));

        _gate.FocusLost();

        Assert.False(OverlayInputGate.KeyTaken(Key.F1));
        Assert.Equal(1, _sink.FocusLosses);
    }

    [Fact]
    public void AThrowingSinkIsDroppedAndThePressPasses()
    {
        _sink.Throw = true;

        _gate.KeyDown(Key.F1, OverlayModifiers.Control);

        Assert.False(OverlayInputGate.KeyTaken(Key.F1));
        _sink.Throw = false;
        _gate.KeyUp(Key.F1, OverlayModifiers.Control);
        _gate.KeyDown(Key.F1, OverlayModifiers.Control);
        Assert.Empty(_sink.Presses);   // dropped: never asked again
    }

    private sealed class FakeKeyboard : IKeyboard
    {
        public HashSet<Key> Held { get; } = new();
        public string Name => "fake";
        public int Index => 0;
        public bool IsConnected => true;
        public IReadOnlyList<Key> SupportedKeys => [];
        public string ClipboardText { get; set; } = string.Empty;
        public event Action<IKeyboard, Key, int>? KeyDown;
        public event Action<IKeyboard, Key, int>? KeyUp;
        public event Action<IKeyboard, char>? KeyChar;
        public bool IsKeyPressed(Key key) => Held.Contains(key);
        public bool IsScancodePressed(int scancode) => false;
        public void BeginInput() { }
        public void EndInput() { }
        public void Down(Key key) { Held.Add(key); KeyDown?.Invoke(this, key, 0); }
        public void Up(Key key) { Held.Remove(key); KeyUp?.Invoke(this, key, 0); }
        public void Char(char value) => KeyChar?.Invoke(this, value);
    }

    private sealed class FakeInput(IKeyboard keyboard) : IInputContext
    {
        public nint Handle => 0;
        public IReadOnlyList<IGamepad> Gamepads => [];
        public IReadOnlyList<IJoystick> Joysticks => [];
        public IReadOnlyList<IKeyboard> Keyboards => [keyboard];
        public IReadOnlyList<IMouse> Mice => [];
        public IReadOnlyList<IInputDevice> OtherDevices => [];
        public event Action<IInputDevice, bool>? ConnectionChanged { add { } remove { } }
        public void Dispose() { }
    }

    private static int Queued(ImGuiInputQueue queue) =>
        ((System.Collections.ICollection)typeof(ImGuiInputQueue)
            .GetField("_pending", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .GetValue(queue)!).Count;

    [Fact]
    public void ATakenKeystrokeNeverReachesImGuiButItsSinksStillDo()
    {
        var keyboard = new FakeKeyboard();
        var input = new FakeInput(keyboard);
        using IDisposable attached = _gate.Attach(input, window: null);
        var queue = new ImGuiInputQueue { Accepting = true };
        using var feed = new ImGuiInputFeed(input, queue);

        keyboard.Down(Key.ControlLeft);
        int afterCtrl = Queued(queue);
        keyboard.Down(Key.F1);
        keyboard.Char('x');
        keyboard.Up(Key.F1);
        Assert.Equal(afterCtrl, Queued(queue));
        Assert.Equal([true, false], _sink.Presses.Where(p => p.VirtualKey == 0x70).Select(p => p.Down));

        keyboard.Up(Key.ControlLeft);
        keyboard.Down(Key.F2);
        keyboard.Char('y');
        keyboard.Up(Key.F2);
        Assert.Equal(afterCtrl + 4, Queued(queue));
    }
}
