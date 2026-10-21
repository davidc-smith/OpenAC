using Silk.NET.Input;

namespace AcDream.App.Extensions.Overlay;

/// <summary>
/// Silk.NET keys → Win32 virtual-key codes and the extended-key flag, as Windows
/// reports them in WM_KEYDOWN (RynthCore's hotkeys read these). 0 for a key with none.
/// </summary>
internal static class OverlayVirtualKeys
{
    public static (int Vk, bool Extended) Map(Key key) => key switch
    {
        >= Key.A and <= Key.Z => (0x41 + (key - Key.A), false),
        >= Key.Number0 and <= Key.Number9 => (0x30 + (key - Key.Number0), false),
        >= Key.F1 and <= Key.F24 => (0x70 + (key - Key.F1), false),
        >= Key.Keypad0 and <= Key.Keypad9 => (0x60 + (key - Key.Keypad0), false),
        Key.KeypadMultiply => (0x6A, false),
        Key.KeypadAdd => (0x6B, false),
        Key.KeypadSubtract => (0x6D, false),
        Key.KeypadDecimal => (0x6E, false),
        Key.KeypadDivide => (0x6F, true),
        Key.KeypadEnter => (0x0D, true),
        Key.Backspace => (0x08, false),
        Key.Tab => (0x09, false),
        Key.Enter => (0x0D, false),
        Key.Pause => (0x13, false),
        Key.CapsLock => (0x14, false),
        Key.Escape => (0x1B, false),
        Key.Space => (0x20, false),
        Key.PageUp => (0x21, true),
        Key.PageDown => (0x22, true),
        Key.End => (0x23, true),
        Key.Home => (0x24, true),
        Key.Left => (0x25, true),
        Key.Up => (0x26, true),
        Key.Right => (0x27, true),
        Key.Down => (0x28, true),
        Key.PrintScreen => (0x2C, true),
        Key.Insert => (0x2D, true),
        Key.Delete => (0x2E, true),
        Key.SuperLeft => (0x5B, true),
        Key.SuperRight => (0x5C, true),
        Key.Menu => (0x5D, true),
        Key.NumLock => (0x90, true),
        Key.ScrollLock => (0x91, false),
        Key.ShiftLeft => (0xA0, false),
        Key.ShiftRight => (0xA1, false),
        Key.ControlLeft => (0xA2, false),
        Key.ControlRight => (0xA3, true),
        Key.AltLeft => (0xA4, false),
        Key.AltRight => (0xA5, true),
        Key.Semicolon => (0xBA, false),
        Key.Equal => (0xBB, false),
        Key.Comma => (0xBC, false),
        Key.Minus => (0xBD, false),
        Key.Period => (0xBE, false),
        Key.Slash => (0xBF, false),
        Key.GraveAccent => (0xC0, false),
        Key.LeftBracket => (0xDB, false),
        Key.BackSlash => (0xDC, false),
        Key.RightBracket => (0xDD, false),
        Key.Apostrophe => (0xDE, false),
        _ => (0, false),
    };
}
