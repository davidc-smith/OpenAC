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
/// plugin hotkey can bind. Empty is unbound.
/// </summary>
internal static class MarkupHotkeyText
{
    private static readonly Dictionary<Key, PluginKey> PluginKeys = MapPluginKeys();

    /// <summary>The canonical string for a keyboard chord; empty for one no hotkey can hold.</summary>
    public static string Format(KeyChord chord)
    {
        if (chord.Device != 0 || !PluginKeys.TryGetValue(chord.Key, out PluginKey key))
            return string.Empty;
        var text = new StringBuilder();
        AppendModifiers(text, chord.Modifiers);
        return text.Append(key.ToString()).ToString();
    }

    /// <summary>
    /// What a hotkey box shows: the modifiers as Ctrl, Alt and Shift (the
    /// string never says which side), then the key by the given name, which
    /// is asked for the key alone.
    /// </summary>
    public static string Describe(KeyChord chord, Func<KeyChord, string> keyName)
    {
        var text = new StringBuilder();
        AppendModifiers(text, chord.Modifiers);
        return text.Append(keyName(chord with { Modifiers = ModifierMask.None })).ToString();
    }

    /// <summary>
    /// Reads a hotkey string: modifiers in any order and any case, then the
    /// key's <see cref="PluginKey"/> name in any case. Null for empty or
    /// unreadable text, which shows as unbound.
    /// </summary>
    public static KeyChord? Parse(string? text)
    {
        if (string.IsNullOrEmpty(text))
            return null;
        string[] parts = text.Split('+');
        ModifierMask mods = ModifierMask.None;
        for (int i = 0; i < parts.Length - 1; i++)
        {
            ModifierMask flag = ModifierNamed(parts[i]);
            if (flag == ModifierMask.None || (mods & flag) != 0)
                return null;
            mods |= flag;
        }
        string name = parts[^1];
        if (name.Length == 0 || !char.IsAsciiLetter(name[0]) || !name.All(char.IsAsciiLetterOrDigit)
            || !Enum.TryParse(name, ignoreCase: true, out PluginKey pluginKey)
            || AppHotkeyRegistry.MapKey(pluginKey) is not { } key)
            return null;
        return new KeyChord(key, mods);
    }

    /// <summary>A key a hotkey can end on: one with a <see cref="PluginKey"/> name.</summary>
    public static bool IsChordKey(Key key) => PluginKeys.ContainsKey(key);

    private static Dictionary<Key, PluginKey> MapPluginKeys()
    {
        var keys = new Dictionary<Key, PluginKey>();
        foreach (PluginKey key in Enum.GetValues<PluginKey>())
        {
            if (AppHotkeyRegistry.MapKey(key) is { } silk)
                keys.Add(silk, key);
        }
        return keys;
    }

    private static void AppendModifiers(StringBuilder text, ModifierMask modifiers)
    {
        if ((modifiers & ModifierMask.Ctrl) != 0) text.Append("Ctrl+");
        if ((modifiers & ModifierMask.Alt) != 0) text.Append("Alt+");
        if ((modifiers & ModifierMask.Shift) != 0) text.Append("Shift+");
    }

    private static ModifierMask ModifierNamed(string name) =>
        string.Equals(name, "Ctrl", StringComparison.OrdinalIgnoreCase) ? ModifierMask.Ctrl
        : string.Equals(name, "Alt", StringComparison.OrdinalIgnoreCase) ? ModifierMask.Alt
        : string.Equals(name, "Shift", StringComparison.OrdinalIgnoreCase) ? ModifierMask.Shift
        : ModifierMask.None;
}
