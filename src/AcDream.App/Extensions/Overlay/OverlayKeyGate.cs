using AcDream.ClientExtensions;
using Silk.NET.Input;
using Silk.NET.Windowing;

namespace AcDream.App.Extensions.Overlay;

/// <summary>
/// Offers every key press to the overlay's keyboard sinks before the client's own
/// keyboard input sees it, and remembers which keys a sink took: the client's input
/// (<see cref="OverlayInputGate.KeyTaken"/>) skips a taken key, its repeats and
/// characters, until it comes back up. Silk calls keyboard handlers in subscription
/// order, so <see cref="Attach"/> must run before anything else subscribes (GameWindow
/// does it as it creates the input context). Game thread.
/// </summary>
internal sealed class OverlayKeyGate
{
    private readonly Func<OverlayKeyEvent, bool> _offer;
    private readonly Action _focusLost;
    private readonly HashSet<Key> _down = new();
    private readonly HashSet<Key> _taken = new();
    private Key? _upTaken;

    public OverlayKeyGate(Func<OverlayKeyEvent, bool> offer, Action focusLost)
    {
        _offer = offer ?? throw new ArgumentNullException(nameof(offer));
        _focusLost = focusLost ?? throw new ArgumentNullException(nameof(focusLost));
    }

    /// <summary>True while the key's press, repeat or release belongs to a sink.</summary>
    public bool IsTaken(Key key) => _taken.Contains(key) || _upTaken == key;

    /// <summary>True while a taken key is down: its characters are the sink's too.</summary>
    public bool CharTaken => _taken.Count > 0;

    public void KeyDown(Key key, OverlayModifiers modifiers)
    {
        _upTaken = null;
        bool repeat = !_down.Add(key);
        (int vk, bool extended) = OverlayVirtualKeys.Map(key);
        if (_offer(new OverlayKeyEvent(vk, modifiers, Down: true, repeat, extended)))
            _taken.Add(key);
        else
            _taken.Remove(key);
    }

    public void KeyUp(Key key, OverlayModifiers modifiers)
    {
        _upTaken = null;
        _down.Remove(key);
        bool wasTaken = _taken.Remove(key);
        (int vk, bool extended) = OverlayVirtualKeys.Map(key);
        bool took = _offer(new OverlayKeyEvent(vk, modifiers, Down: false, Repeat: false, extended));
        if (wasTaken || took)
            _upTaken = key;
    }

    public void FocusLost()
    {
        _down.Clear();
        _taken.Clear();
        _upTaken = null;
        _focusLost();
    }

    /// <summary>Subscribes to every keyboard and the window's focus. Dispose to unsubscribe.</summary>
    public IDisposable Attach(IInputContext input, IView? window)
    {
        ArgumentNullException.ThrowIfNull(input);
        var keyboards = input.Keyboards.ToArray();
        Action<IKeyboard, Key, int> down = (keyboard, key, _) => KeyDown(key, Modifiers(keyboard));
        Action<IKeyboard, Key, int> up = (keyboard, key, _) => KeyUp(key, Modifiers(keyboard));
        Action<bool> focus = focused => { if (!focused) FocusLost(); };
        foreach (IKeyboard keyboard in keyboards)
        {
            keyboard.KeyDown += down;
            keyboard.KeyUp += up;
        }

        if (window is not null)
            window.FocusChanged += focus;
        return new Subscription(() =>
        {
            foreach (IKeyboard keyboard in keyboards)
            {
                keyboard.KeyDown -= down;
                keyboard.KeyUp -= up;
            }

            if (window is not null)
                window.FocusChanged -= focus;
        });
    }

    private static OverlayModifiers Modifiers(IKeyboard keyboard)
    {
        bool Held(Key a, Key b) => keyboard.IsKeyPressed(a) || keyboard.IsKeyPressed(b);
        return (Held(Key.ControlLeft, Key.ControlRight) || Held(Key.SuperLeft, Key.SuperRight) ? OverlayModifiers.Control : OverlayModifiers.None)
            | (Held(Key.ShiftLeft, Key.ShiftRight) ? OverlayModifiers.Shift : OverlayModifiers.None)
            | (Held(Key.AltLeft, Key.AltRight) ? OverlayModifiers.Alt : OverlayModifiers.None);
    }

    private sealed class Subscription(Action release) : IDisposable
    {
        private Action? _release = release;

        public void Dispose() => Interlocked.Exchange(ref _release, null)?.Invoke();
    }
}
