using AcDream.App.Tests.Plugins;
using AcDream.App.UI;
using Silk.NET.Input;

namespace AcDream.App.Tests.UI;

public sealed class UiFieldControlKeyTests
{
    private static bool Press(UiField field, Key key) =>
        field.OnEvent(new UiEvent(0, field, UiEventType.KeyDown, Data0: (int)key));

    [Fact]
    public void ControlKey_GoesToTheOwnerFirst()
    {
        var keyboard = new FakeKeyboard();
        keyboard.Held.Add(Key.ControlLeft);
        keyboard.Held.Add(Key.ShiftLeft);
        var seen = new List<(Key, bool)>();
        var field = new UiField
        {
            Keyboard = keyboard, Selectable = true,
            OnControlKey = (key, shift) => { seen.Add((key, shift)); return key == Key.Tab; },
        };

        Assert.True(Press(field, Key.Tab));
        Assert.Equal([(Key.Tab, true)], seen);

        field.SetText("abc");
        Assert.True(Press(field, Key.A));   // not taken: the field selects all as before
        keyboard.Held.Clear();
        field.OnEvent(new UiEvent(0, field, UiEventType.Char, Data0: 'x'));
        Assert.Equal("x", field.Text);      // the typed character replaced the selection
    }
}
