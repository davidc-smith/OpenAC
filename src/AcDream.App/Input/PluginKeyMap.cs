using AcDream.Plugin.Abstractions;
using Silk.NET.Input;

namespace AcDream.App.Input;

/// <summary>
/// The one table between the plugin contract's <see cref="PluginKey"/> and
/// the window's Silk <see cref="Key"/>: plugin hotkeys read it one way, a
/// plugin canvas with keyboard focus the other.
/// </summary>
internal static class PluginKeyMap
{
    private static readonly Dictionary<Key, PluginKey> FromSilkTable = BuildFromSilk();

    /// <summary>The Silk key a plugin key names, or null for <see cref="PluginKey.Unknown"/>.</summary>
    internal static Key? ToSilk(PluginKey key) => key switch
    {
        PluginKey.A => Key.A, PluginKey.B => Key.B, PluginKey.C => Key.C,
        PluginKey.D => Key.D, PluginKey.E => Key.E, PluginKey.F => Key.F,
        PluginKey.G => Key.G, PluginKey.H => Key.H, PluginKey.I => Key.I,
        PluginKey.J => Key.J, PluginKey.K => Key.K, PluginKey.L => Key.L,
        PluginKey.M => Key.M, PluginKey.N => Key.N, PluginKey.O => Key.O,
        PluginKey.P => Key.P, PluginKey.Q => Key.Q, PluginKey.R => Key.R,
        PluginKey.S => Key.S, PluginKey.T => Key.T, PluginKey.U => Key.U,
        PluginKey.V => Key.V, PluginKey.W => Key.W, PluginKey.X => Key.X,
        PluginKey.Y => Key.Y, PluginKey.Z => Key.Z,
        PluginKey.Number0 => Key.Number0, PluginKey.Number1 => Key.Number1,
        PluginKey.Number2 => Key.Number2, PluginKey.Number3 => Key.Number3,
        PluginKey.Number4 => Key.Number4, PluginKey.Number5 => Key.Number5,
        PluginKey.Number6 => Key.Number6, PluginKey.Number7 => Key.Number7,
        PluginKey.Number8 => Key.Number8, PluginKey.Number9 => Key.Number9,
        PluginKey.F1 => Key.F1, PluginKey.F2 => Key.F2, PluginKey.F3 => Key.F3,
        PluginKey.F4 => Key.F4, PluginKey.F5 => Key.F5, PluginKey.F6 => Key.F6,
        PluginKey.F7 => Key.F7, PluginKey.F8 => Key.F8, PluginKey.F9 => Key.F9,
        PluginKey.F10 => Key.F10, PluginKey.F11 => Key.F11, PluginKey.F12 => Key.F12,
        PluginKey.Space => Key.Space,
        PluginKey.Enter => Key.Enter,
        PluginKey.Escape => Key.Escape,
        PluginKey.Tab => Key.Tab,
        PluginKey.Backspace => Key.Backspace,
        PluginKey.Delete => Key.Delete,
        PluginKey.Insert => Key.Insert,
        PluginKey.Home => Key.Home,
        PluginKey.End => Key.End,
        PluginKey.PageUp => Key.PageUp,
        PluginKey.PageDown => Key.PageDown,
        PluginKey.Up => Key.Up,
        PluginKey.Down => Key.Down,
        PluginKey.Left => Key.Left,
        PluginKey.Right => Key.Right,
        PluginKey.Minus => Key.Minus,
        PluginKey.Equal => Key.Equal,
        PluginKey.LeftBracket => Key.LeftBracket,
        PluginKey.RightBracket => Key.RightBracket,
        PluginKey.BackSlash => Key.BackSlash,
        PluginKey.Semicolon => Key.Semicolon,
        PluginKey.Apostrophe => Key.Apostrophe,
        PluginKey.Comma => Key.Comma,
        PluginKey.Period => Key.Period,
        PluginKey.Slash => Key.Slash,
        PluginKey.Numpad0 => Key.Keypad0, PluginKey.Numpad1 => Key.Keypad1,
        PluginKey.Numpad2 => Key.Keypad2, PluginKey.Numpad3 => Key.Keypad3,
        PluginKey.Numpad4 => Key.Keypad4, PluginKey.Numpad5 => Key.Keypad5,
        PluginKey.Numpad6 => Key.Keypad6, PluginKey.Numpad7 => Key.Keypad7,
        PluginKey.Numpad8 => Key.Keypad8, PluginKey.Numpad9 => Key.Keypad9,
        PluginKey.NumpadDecimal => Key.KeypadDecimal,
        PluginKey.NumpadDivide => Key.KeypadDivide,
        PluginKey.NumpadMultiply => Key.KeypadMultiply,
        PluginKey.NumpadSubtract => Key.KeypadSubtract,
        PluginKey.NumpadAdd => Key.KeypadAdd,
        PluginKey.NumpadEnter => Key.KeypadEnter,
        PluginKey.Grave => Key.GraveAccent,
        PluginKey.PrintScreen => Key.PrintScreen,
        PluginKey.Pause => Key.Pause,
        _ => null,
    };

    /// <summary>The plugin key for a Silk key, or <see cref="PluginKey.Unknown"/> when the contract cannot name it.</summary>
    internal static PluginKey FromSilk(Key key) =>
        FromSilkTable.TryGetValue(key, out PluginKey named) ? named : PluginKey.Unknown;

    private static Dictionary<Key, PluginKey> BuildFromSilk()
    {
        var table = new Dictionary<Key, PluginKey>();
        foreach (PluginKey key in Enum.GetValues<PluginKey>())
        {
            if (ToSilk(key) is { } silk)
                table.Add(silk, key);
        }
        return table;
    }
}
