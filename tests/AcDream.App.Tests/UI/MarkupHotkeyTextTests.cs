using AcDream.App.UI.Markup;
using AcDream.Plugin.Abstractions;
using AcDream.UI.Abstractions.Input;
using Silk.NET.Input;

namespace AcDream.App.Tests.UI;

public class MarkupHotkeyTextTests
{
    private sealed class Binding
    {
        public string? Key { get; set; } = "Ctrl+F";
        public int NotString { get; set; }
        public string? Last { get; private set; }
        public Action<string> SetKey => v => Last = v;
        public Action<int> WrongAction => _ => { };
    }

    [Fact]
    public void Describe_names_modifiers_plainly_and_only_the_key_by_the_game()
    {
        var asked = new List<KeyChord>();
        string text = MarkupHotkeyText.Describe(
            new KeyChord(Key.F, ModifierMask.Shift | ModifierMask.Ctrl | ModifierMask.Alt),
            chord => { asked.Add(chord); return "F-KEY"; });

        Assert.Equal("Ctrl+Alt+Shift+F-KEY", text);
        Assert.Equal([new KeyChord(Key.F, ModifierMask.None)], asked);
    }

    [Fact]
    public void Describe_without_modifiers_is_the_game_name()
    {
        Assert.Equal("NUMPAD 5", MarkupHotkeyText.Describe(new KeyChord(Key.Keypad5, ModifierMask.None), _ => "NUMPAD 5"));
    }

    [Fact]
    public void Format_WritesModifiersInCanonicalOrder()
    {
        var mods = ModifierMask.Shift | ModifierMask.Ctrl | ModifierMask.Alt | ModifierMask.Win;
        Assert.Equal("Ctrl+Alt+Shift+F", MarkupHotkeyText.Format(new KeyChord(Key.F, mods)));
        Assert.Equal("F5", MarkupHotkeyText.Format(new KeyChord(Key.F5, ModifierMask.None)));
    }

    [Fact]
    public void Format_SpellsTheKeyAsItsPluginKeyName()
    {
        Assert.Equal("Numpad5", MarkupHotkeyText.Format(new KeyChord(Key.Keypad5, ModifierMask.None)));
        Assert.Equal("Ctrl+Grave", MarkupHotkeyText.Format(new KeyChord(Key.GraveAccent, ModifierMask.Ctrl)));
        Assert.Equal("Alt+Number1", MarkupHotkeyText.Format(new KeyChord(Key.Number1, ModifierMask.Alt)));
        Assert.Equal("NumpadEnter", MarkupHotkeyText.Format(new KeyChord(Key.KeypadEnter, ModifierMask.None)));
    }

    [Fact]
    public void Format_RefusesWhatNoHotkeyCanHold()
    {
        Assert.Equal("", MarkupHotkeyText.Format(new KeyChord(Key.F, ModifierMask.None, Device: 1)));
        Assert.Equal("", MarkupHotkeyText.Format(new KeyChord(Key.Unknown, ModifierMask.Ctrl)));
        Assert.Equal("", MarkupHotkeyText.Format(new KeyChord(Key.ShiftLeft, ModifierMask.Shift)));
        Assert.Equal("", MarkupHotkeyText.Format(new KeyChord(Key.F13, ModifierMask.None)));
        Assert.Equal("", MarkupHotkeyText.Format(new KeyChord(Key.CapsLock, ModifierMask.Ctrl)));
        Assert.Equal("", MarkupHotkeyText.Format(default));
    }

    [Fact]
    public void EveryPluginKeyRoundTripsByItsOwnName()
    {
        foreach (PluginKey key in Enum.GetValues<PluginKey>())
        {
            if (key == PluginKey.Unknown)
                continue;
            KeyChord? chord = MarkupHotkeyText.Parse(key.ToString());
            Assert.True(chord is not null, $"{key} does not parse");
            Assert.Equal(key.ToString(), MarkupHotkeyText.Format(chord.Value));
        }
    }

    [Theory]
    [InlineData("Ctrl+Alt+Shift+F", "Ctrl+Alt+Shift+F")]
    [InlineData("shift+ctrl+f", "Ctrl+Shift+F")]
    [InlineData("F12", "F12")]
    [InlineData("numpad5", "Numpad5")]
    [InlineData("Alt+Number1", "Alt+Number1")]
    [InlineData("Ctrl+grave", "Ctrl+Grave")]
    public void Parse_ReadsAndRoundTripsToCanonical(string text, string canonical)
    {
        KeyChord? chord = MarkupHotkeyText.Parse(text);
        Assert.NotNull(chord);
        Assert.Equal(canonical, MarkupHotkeyText.Format(chord.Value));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Ctrl+")]
    [InlineData("+F")]
    [InlineData("Ctrl+Ctrl+F")]
    [InlineData("Meta+F")]
    [InlineData("5")]
    [InlineData("A,B")]
    [InlineData("Unknown")]
    [InlineData("ShiftLeft")]
    [InlineData("Ctrl+Nope")]
    [InlineData("Keypad5")]
    [InlineData("GraveAccent")]
    [InlineData("F13")]
    [InlineData("CapsLock")]
    public void Parse_UnreadableIsUnbound(string? text) =>
        Assert.Null(MarkupHotkeyText.Parse(text));

    [Fact]
    public void StringReader_ReadsEachCallAndNullIsEmpty()
    {
        var binding = new Binding();
        Func<string> read = MarkupBindings.BindRequiredStringReader("{Key}", binding, "hotkey value");
        Assert.Equal("Ctrl+F", read());
        binding.Key = null;
        Assert.Equal("", read());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("Ctrl+F")]
    [InlineData("{NotString}")]
    [InlineData("{Missing}")]
    public void StringReader_RejectsAnythingButAStringBinding(string? expression)
    {
        var ex = Assert.Throws<FormatException>(
            () => MarkupBindings.BindRequiredStringReader(expression, new Binding(), "hotkey value"));
        Assert.Contains("hotkey value", ex.Message);
    }

    [Fact]
    public void StringAction_Invokes()
    {
        var binding = new Binding();
        MarkupBindings.BindRequiredStringAction("{SetKey}", binding, "hotkey onchange")("Alt+G");
        Assert.Equal("Alt+G", binding.Last);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("SetKey")]
    [InlineData("{WrongAction}")]
    [InlineData("{Missing}")]
    public void StringAction_RejectsAbsentLiteralAndWrongTypes(string? expression)
    {
        var ex = Assert.Throws<FormatException>(
            () => MarkupBindings.BindRequiredStringAction(expression, new Binding(), "hotkey onchange"));
        Assert.Contains("hotkey onchange", ex.Message);
    }
}
