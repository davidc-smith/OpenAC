using AcDream.UI.Abstractions.Input;
using Silk.NET.Input;

namespace AcDream.App.UI.Hud;

/// <summary>The short key label drawn in a slot's corner, from the first chord bound to its action.
/// The modifier glyphs live here so a font without them is a one-line change.</summary>
public static class SpellDeckKeyLabel
{
    public const string Shift = "⇧", Ctrl = "^", Alt = "⌥";

    public static string For(KeyBindings bindings, InputAction action)
    {
        Binding? first = bindings.ForAction(action).Cast<Binding?>().FirstOrDefault();
        if (first is not { } b) return string.Empty;
        string mods = (b.Chord.Modifiers.HasFlag(ModifierMask.Ctrl) ? Ctrl : "")
            + (b.Chord.Modifiers.HasFlag(ModifierMask.Alt) ? Alt : "")
            + (b.Chord.Modifiers.HasFlag(ModifierMask.Shift) ? Shift : "");
        return mods + KeyName(b.Chord.Key);
    }

    private static string KeyName(Key key) => key switch
    {
        >= Key.Number0 and <= Key.Number9 => ((int)(key - Key.Number0)).ToString(),
        Key.Minus => "-",
        Key.Equal => "=",
        _ => key.ToString(),
    };
}
