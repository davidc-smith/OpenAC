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

/// <summary>Modifiers held with a key. Control includes Command on macOS.</summary>
[Flags]
public enum OverlayModifiers
{
    None = 0,
    Control = 1,
    Shift = 2,
}

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
}
