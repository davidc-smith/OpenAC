using AcDream.ClientExtensions;
using AcDream.Rynth.EngineUi;
using RynthCore.Engine.Hotkeys;

namespace AcDream.Rynth.Host;

/// <summary>
/// OpenAC's keyboard for RynthCore's hotkeys: what their Win32Backend hands HotkeyService
/// from acclient's WndProc, made from the overlay's key events. Each press becomes the
/// WM_KEYDOWN / WM_SYSKEYDOWN (Alt held) or key-up message Windows would send, the modifier
/// state their GetKeyState reads is set first (Win32KeyStateStandIn), and their answer
/// decides whether OpenAC sees the key. Characters and focus loss go to them too. Game thread.
/// </summary>
internal sealed class RynthHotkeyKeyboard : IOverlayKeyboardSink
{
    private const uint WmKillFocus = 0x0008, WmKeyDown = 0x0100, WmKeyUp = 0x0101, WmChar = 0x0102,
        WmSysKeyDown = 0x0104, WmSysKeyUp = 0x0105;
    private const long Extended = 0x01000000, PreviousDown = 0x40000000, Transition = 0x80000000;

    private readonly Func<bool> _overlayHasKeyboard;

    /// <param name="overlayHasKeyboard">True while an ImGui text field or Rynth chat has the keyboard (their "Rynth text focus").</param>
    public RynthHotkeyKeyboard(Func<bool> overlayHasKeyboard)
    {
        ArgumentNullException.ThrowIfNull(overlayHasKeyboard);
        _overlayHasKeyboard = overlayHasKeyboard;
    }

    public bool Capturing => false;

    public bool KeyPress(OverlayKeyEvent e)
    {
        if (e.VirtualKey == 0)
            return false;
        Win32KeyStateStandIn.Focused = true;   // OpenAC only sends keys while its window has them
        Win32KeyStateStandIn.Control = (e.Modifiers & OverlayModifiers.Control) != 0;
        Win32KeyStateStandIn.Shift = (e.Modifiers & OverlayModifiers.Shift) != 0;
        Win32KeyStateStandIn.Alt = (e.Modifiers & OverlayModifiers.Alt) != 0;
        bool alt = Win32KeyStateStandIn.Alt;
        uint message = e.Down ? (alt ? WmSysKeyDown : WmKeyDown) : (alt ? WmSysKeyUp : WmKeyUp);
        long lParam = 1
            | (e.Extended ? Extended : 0)
            | (e.Repeat || !e.Down ? PreviousDown : 0)
            | (e.Down ? 0 : Transition);
        return HotkeyService.HandleKeyMessage(message, (IntPtr)e.VirtualKey, (IntPtr)lParam, _overlayHasKeyboard());
    }

    public void Character(char value) =>
        HotkeyService.HandleKeyMessage(WmChar, (IntPtr)value, (IntPtr)1, _overlayHasKeyboard());

    public void FocusLost()
    {
        Win32KeyStateStandIn.Focused = false;
        HotkeyService.HandleKeyMessage(WmKillFocus, IntPtr.Zero, IntPtr.Zero, false);
    }

    public void KeyDown(OverlayKey key, OverlayModifiers modifiers)
    {
    }

    public void KeyUp(OverlayKey key)
    {
    }
}
