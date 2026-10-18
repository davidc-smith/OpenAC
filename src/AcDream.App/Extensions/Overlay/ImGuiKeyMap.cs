using ImGuiNET;
using Silk.NET.Input;

namespace AcDream.App.Extensions.Overlay;

/// <summary>Silk.NET keys to ImGui keys (the keys ImGui widgets use).</summary>
internal static class ImGuiKeyMap
{
    public static ImGuiKey Map(Key key) => key switch
    {
        >= Key.A and <= Key.Z => ImGuiKey.A + (key - Key.A),
        >= Key.Number0 and <= Key.Number9 => ImGuiKey._0 + (key - Key.Number0),
        >= Key.F1 and <= Key.F12 => ImGuiKey.F1 + (key - Key.F1),
        >= Key.Keypad0 and <= Key.Keypad9 => ImGuiKey.Keypad0 + (key - Key.Keypad0),
        Key.Tab => ImGuiKey.Tab,
        Key.Left => ImGuiKey.LeftArrow,
        Key.Right => ImGuiKey.RightArrow,
        Key.Up => ImGuiKey.UpArrow,
        Key.Down => ImGuiKey.DownArrow,
        Key.PageUp => ImGuiKey.PageUp,
        Key.PageDown => ImGuiKey.PageDown,
        Key.Home => ImGuiKey.Home,
        Key.End => ImGuiKey.End,
        Key.Insert => ImGuiKey.Insert,
        Key.Delete => ImGuiKey.Delete,
        Key.Backspace => ImGuiKey.Backspace,
        Key.Space => ImGuiKey.Space,
        Key.Enter => ImGuiKey.Enter,
        Key.KeypadEnter => ImGuiKey.KeypadEnter,
        Key.Escape => ImGuiKey.Escape,
        Key.ControlLeft => ImGuiKey.LeftCtrl,
        Key.ControlRight => ImGuiKey.RightCtrl,
        Key.ShiftLeft => ImGuiKey.LeftShift,
        Key.ShiftRight => ImGuiKey.RightShift,
        Key.AltLeft => ImGuiKey.LeftAlt,
        Key.AltRight => ImGuiKey.RightAlt,
        Key.SuperLeft => ImGuiKey.LeftSuper,
        Key.SuperRight => ImGuiKey.RightSuper,
        Key.Comma => ImGuiKey.Comma,
        Key.Period => ImGuiKey.Period,
        Key.Minus => ImGuiKey.Minus,
        Key.Equal => ImGuiKey.Equal,
        Key.Slash => ImGuiKey.Slash,
        Key.Semicolon => ImGuiKey.Semicolon,
        Key.Apostrophe => ImGuiKey.Apostrophe,
        Key.LeftBracket => ImGuiKey.LeftBracket,
        Key.RightBracket => ImGuiKey.RightBracket,
        Key.BackSlash => ImGuiKey.Backslash,
        Key.GraveAccent => ImGuiKey.GraveAccent,
        _ => ImGuiKey.None,
    };
}
