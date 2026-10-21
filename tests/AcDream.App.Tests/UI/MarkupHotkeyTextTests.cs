using AcDream.App.UI.Markup;
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
    public void Format_WritesModifiersInCanonicalOrder()
    {
        var mods = ModifierMask.Shift | ModifierMask.Ctrl | ModifierMask.Alt | ModifierMask.Win;
        Assert.Equal("Ctrl+Alt+Shift+F", MarkupHotkeyText.Format(new KeyChord(Key.F, mods)));
        Assert.Equal("F5", MarkupHotkeyText.Format(new KeyChord(Key.F5, ModifierMask.None)));
    }

    [Fact]
    public void Format_RefusesWhatNoHotkeyCanHold()
    {
        Assert.Equal("", MarkupHotkeyText.Format(new KeyChord(Key.F, ModifierMask.None, Device: 1)));
        Assert.Equal("", MarkupHotkeyText.Format(new KeyChord(Key.Unknown, ModifierMask.Ctrl)));
        Assert.Equal("", MarkupHotkeyText.Format(new KeyChord(Key.ShiftLeft, ModifierMask.Shift)));
        Assert.Equal("", MarkupHotkeyText.Format(default));
    }

    [Theory]
    [InlineData("Ctrl+Alt+Shift+F", "Ctrl+Alt+Shift+F")]
    [InlineData("shift+ctrl+f", "Ctrl+Shift+F")]
    [InlineData("F12", "F12")]
    [InlineData("Keypad5", "Keypad5")]
    [InlineData("Alt+Number1", "Alt+Number1")]
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
