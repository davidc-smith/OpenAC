using Silk.NET.Input;

namespace AcDream.App.Input;

/// <summary>
/// The interface's focus state as plugin hotkeys see it, read at each key
/// press.
/// </summary>
public interface IHotkeyFocusSource
{
    /// <summary>
    /// Whether an interface element holding keyboard focus takes the keyboard
    /// from the game. A focused plugin dialog does not: it claims only the
    /// keys that answer it (see <see cref="ClaimsKey"/>).
    /// </summary>
    bool HasKeyboardFocus { get; }

    /// <summary>Whether the focused element keeps <paramref name="key"/>'s press from the game.</summary>
    bool ClaimsKey(Key key) => HasKeyboardFocus;

    /// <summary>Whether the focused element takes every key press (a hotkey box recording).</summary>
    bool IsCapturingKeys { get; }

    /// <summary>Whether a modal dialog is open.</summary>
    bool IsModalOpen { get; }
}
