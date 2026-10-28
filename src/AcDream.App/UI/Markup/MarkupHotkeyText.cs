using System.Text;
using AcDream.App.Input;
using AcDream.Plugin.Abstractions;
using AcDream.UI.Abstractions.Input;
using Silk.NET.Input;

namespace AcDream.App.UI.Markup;

/// <summary>
/// The hotkey string a markup <c>&lt;hotkey&gt;</c> hands its plugin:
/// <c>Ctrl+Alt+Shift+Key</c>, modifiers in that order and the key spelled
/// as its <see cref="PluginKey"/> name, so the box holds exactly the keys a
/// plugin hotkey can bind. Empty is unbound. The Chat Deck alone opts in to
/// a trailing <c>Cmd+</c> modifier (<c>allowCommand</c>); plugin strings never carry it.
/// </summary>
internal static class MarkupHotkeyText
{
    /// <summary>The canonical string for a keyboard chord; empty for one no hotkey can hold.</summary>
    public static string Format(KeyChord chord, bool allowCommand = false)
    {
        PluginKey key = PluginKeyMap.FromSilk(chord.Key);
        if (chord.Device != 0 || key == PluginKey.Unknown)
            return string.Empty;
        var text = new StringBuilder();
        AppendModifiers(text, chord.Modifiers, allowCommand);
        return text.Append(key.ToString()).ToString();
    }

    /// <summary>
    /// What a hotkey box shows: the modifiers as Ctrl, Alt and Shift (the
    /// string never says which side), then the key by the given name, which
    /// is asked for the key alone.
    /// </summary>
    public static string Describe(KeyChord chord, Func<KeyChord, string> keyName, bool allowCommand = false)
    {
        var text = new StringBuilder();
        AppendModifiers(text, chord.Modifiers, allowCommand);
        return text.Append(keyName(chord with { Modifiers = ModifierMask.None })).ToString();
    }

    /// <summary>
    /// Reads a hotkey string: modifiers in any order and any case, then the
    /// key's <see cref="PluginKey"/> name in any case. Null for empty or
    /// unreadable text, which shows as unbound.
    /// </summary>
    public static KeyChord? Parse(string? text, bool allowCommand = false)
    {
        if (string.IsNullOrEmpty(text))
            return null;
        string[] parts = text.Split('+');
        ModifierMask mods = ModifierMask.None;
        for (int i = 0; i < parts.Length - 1; i++)
        {
            ModifierMask flag = ModifierNamed(parts[i], allowCommand);
            if (flag == ModifierMask.None || (mods & flag) != 0)
                return null;
            mods |= flag;
        }
        string name = parts[^1];
        if (name.Length == 0 || !char.IsAsciiLetter(name[0]) || !name.All(char.IsAsciiLetterOrDigit)
            || !Enum.TryParse(name, ignoreCase: true, out PluginKey pluginKey)
            || PluginKeyMap.ToSilk(pluginKey) is not { } key)
            return null;
        return new KeyChord(key, mods);
    }

    /// <summary>A key a hotkey can end on: one with a <see cref="PluginKey"/> name.</summary>
    public static bool IsChordKey(Key key) => PluginKeyMap.FromSilk(key) != PluginKey.Unknown;

    private static void AppendModifiers(StringBuilder text, ModifierMask modifiers, bool allowCommand)
    {
        if ((modifiers & ModifierMask.Ctrl) != 0) text.Append("Ctrl+");
        if ((modifiers & ModifierMask.Alt) != 0) text.Append("Alt+");
        if ((modifiers & ModifierMask.Shift) != 0) text.Append("Shift+");
        if (allowCommand && (modifiers & ModifierMask.Win) != 0) text.Append("Cmd+");
    }

    private static ModifierMask ModifierNamed(string name, bool allowCommand) =>
        string.Equals(name, "Ctrl", StringComparison.OrdinalIgnoreCase) ? ModifierMask.Ctrl
        : string.Equals(name, "Alt", StringComparison.OrdinalIgnoreCase) ? ModifierMask.Alt
        : string.Equals(name, "Shift", StringComparison.OrdinalIgnoreCase) ? ModifierMask.Shift
        : allowCommand && (string.Equals(name, "Cmd", StringComparison.OrdinalIgnoreCase)
            || string.Equals(name, "Win", StringComparison.OrdinalIgnoreCase)) ? ModifierMask.Win
        : ModifierMask.None;
}
