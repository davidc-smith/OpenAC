namespace AcDream.ClientExtensions;

/// <summary>The keys an overlay keyboard sink can tell apart; everything else is <see cref="Other"/>.</summary>
public enum OverlayKey
{
    Other,
    Enter,
    Escape,
    Backspace,
    Delete,
    Left,
    Right,
    Up,
    Down,
    Home,
    End,
    Insert,
    C,
    V,
    X,
}

/// <summary>Modifiers held with a key. Control includes Command on macOS; Alt is Option there.</summary>
[Flags]
public enum OverlayModifiers
{
    None = 0,
    Control = 1,
    Shift = 2,
    Alt = 4,
}

/// <summary>
/// One key press or release as Windows numbers it: <paramref name="VirtualKey"/> is the Win32
/// virtual-key code (0 for a key with none), <paramref name="Extended"/> the extended-key flag
/// (arrows, the navigation block, right Ctrl/Alt, numpad Enter and /), <paramref name="Repeat"/>
/// a key-down for a key already down.
/// </summary>
public readonly record struct OverlayKeyEvent(int VirtualKey, OverlayModifiers Modifiers, bool Down, bool Repeat, bool Extended);

/// <summary>
/// Raw keyboard for an extension that runs its own text entry on the overlay.
/// Every key and character goes to every sink; while a sink reports
/// <see cref="Capturing"/>, the client's own input ignores the keyboard.
/// Game thread.
/// </summary>
public interface IOverlayKeyboardSink
{
    /// <summary>Read on every key: true while this sink owns the keyboard.</summary>
    bool Capturing { get; }

    void KeyDown(OverlayKey key, OverlayModifiers modifiers);

    void KeyUp(OverlayKey key);

    void Character(char value);

    /// <summary>
    /// Every key press and release, offered before the client's own keyboard input sees it.
    /// True takes it: the client never sees that key (its repeats, characters and release
    /// included) until it comes back up.
    /// </summary>
    bool KeyPress(OverlayKeyEvent e) => false;

    /// <summary>The client window lost the keyboard; held keys are gone.</summary>
    void FocusLost()
    {
    }
}
