using System.Numerics;
using AcDream.App.UI;
using AcDream.UI.Abstractions.Input;
using AcDream.App.Tests.Plugins;
using Silk.NET.Input;

namespace AcDream.App.Tests.UI;

public class UiMarkupHotkeyTests
{
    private sealed class Model
    {
        public string Value = "";
        public List<string> Reports { get; } = new();
        public ModifierMask Mods = ModifierMask.None;
    }

    private static (UiRoot Root, UiMarkupHotkey Box, Model Model) Mount(string value = "")
    {
        var model = new Model { Value = value };
        var box = new UiMarkupHotkey
        {
            Left = 10, Top = 10, Width = 140, Height = 20,
            ValueSource = () => model.Value,
            Changed = v => { model.Reports.Add(v); model.Value = v; },
            ModifiersSource = () => model.Mods,
        };
        var root = new UiRoot { Width = 800, Height = 600 };
        root.AddChild(box);
        return (root, box, model);
    }

    private static void Click(UiRoot root)
    {
        root.OnMouseDown(UiMouseButton.Left, 20, 15);
        root.OnMouseUp(UiMouseButton.Left, 20, 15);
    }

    private static void Press(UiRoot root, Key key)
    {
        root.OnKeyDown((int)key);
        root.OnKeyUp((int)key);
    }

    [Fact]
    public void Click_CapturesAChordAndGivesFocusBackOnRelease()
    {
        var (root, box, model) = Mount();
        Click(root);
        Assert.Same(box, root.KeyboardFocus);
        Assert.True(box.IsCapturing);
        Assert.Equal(UiMarkupHotkey.PromptText, box.DisplayText);

        model.Mods = ModifierMask.Ctrl;
        root.OnKeyDown((int)Key.ControlLeft);
        Assert.True(box.IsCapturing);
        root.OnKeyDown((int)Key.F);
        Assert.False(box.IsCapturing);
        Assert.Equal(["Ctrl+F"], model.Reports);
        Assert.Same(box, root.KeyboardFocus);   // still swallowing F until it goes up
        root.OnKeyUp((int)Key.F);
        Assert.Null(root.KeyboardFocus);
    }

    [Fact]
    public void Escape_CancelsWithoutReporting()
    {
        var (root, box, model) = Mount("Ctrl+F");
        Click(root);
        Press(root, Key.Escape);
        Assert.False(box.IsCapturing);
        Assert.Empty(model.Reports);
        Assert.Null(root.KeyboardFocus);
    }

    [Theory]
    [InlineData(Key.Backspace)]
    [InlineData(Key.Delete)]
    public void BackspaceOrDelete_ReportsUnbound(Key key)
    {
        var (root, _, model) = Mount("Ctrl+F");
        Click(root);
        Press(root, key);
        Assert.Equal([""], model.Reports);
    }

    [Fact]
    public void ClearingAnUnboundKey_ReportsNothing()
    {
        var (root, _, model) = Mount("");
        Click(root);
        Press(root, Key.Backspace);
        Assert.Empty(model.Reports);
    }

    [Fact]
    public void TheSameChord_ReportsNothing()
    {
        var (root, _, model) = Mount("F5");
        Click(root);
        Press(root, Key.F5);
        Assert.Empty(model.Reports);
    }

    [Fact]
    public void AKeyWithWinHeld_KeepsWaiting()
    {
        var (root, box, model) = Mount();
        Click(root);
        model.Mods = ModifierMask.Win;
        root.OnKeyDown((int)Key.E);
        Assert.True(box.IsCapturing);
        Assert.Empty(model.Reports);
    }

    [Fact]
    public void Enter_StartsAKeyboardCaptureThatKeepsFocus()
    {
        var (root, box, model) = Mount();
        root.SetKeyboardFocus(box);
        root.OnKeyDown((int)Key.Enter);
        Assert.True(box.IsCapturing);
        root.OnKeyDown((int)Key.Enter);   // the starting key repeating
        Assert.True(box.IsCapturing);
        root.OnKeyUp((int)Key.Enter);
        Press(root, Key.Enter);
        Assert.Equal(["Enter"], model.Reports);
        Assert.Same(box, root.KeyboardFocus);
    }

    [Fact]
    public void LosingFocus_CancelsWithoutReporting()
    {
        var (root, box, model) = Mount();
        Click(root);
        root.SetKeyboardFocus(null);
        Assert.False(box.IsCapturing);
        Press(root, Key.G);
        Assert.Empty(model.Reports);
    }

    [Fact]
    public void Disabling_CancelsOnTheNextTick()
    {
        var (root, box, model) = Mount();
        Click(root);
        box.Enabled = false;
        root.Tick(0.016, 16);
        Assert.False(box.IsCapturing);
        Assert.Null(root.KeyboardFocus);
        Assert.Empty(model.Reports);
    }

    [Fact]
    public void Disabled_IgnoresClickAndEnter()
    {
        var (root, box, _) = Mount();
        box.Enabled = false;
        box.OnEvent(new UiEvent { Type = UiEventType.Click });
        box.OnEvent(new UiEvent { Type = UiEventType.KeyDown, Data0 = (int)Key.Enter });
        Assert.False(box.IsCapturing);
    }

    [Theory]
    [InlineData("", UiMarkupHotkey.UnboundText)]
    [InlineData("Ctrl+Nope", UiMarkupHotkey.UnboundText)]
    [InlineData("shift+ctrl+f", "Ctrl+Shift+F")]
    public void DisplayText_WithoutKeyNamesShowsTheCanonicalString(string value, string shown)
    {
        var (_, box, _) = Mount(value);
        Assert.Equal(shown, box.DisplayText);
    }

    [Fact]
    public void DescribeKey_RunsOncePerDistinctValue()
    {
        var (_, box, model) = Mount("Ctrl+F");
        int calls = 0;
        box.DescribeKey = chord => { calls++; return "described " + chord.Key; };
        Assert.Equal("described F", box.DisplayText);
        Assert.Equal("described F", box.DisplayText);
        Assert.Equal(1, calls);
        model.Value = "G";
        Assert.Equal("described G", box.DisplayText);
        Assert.Equal(2, calls);
    }

    [Fact]
    public void SettingDescribeKey_RenamesTheShownChord()
    {
        var (_, box, _) = Mount("Ctrl+F");
        Assert.Equal("Ctrl+F", box.DisplayText);
        box.DescribeKey = chord => "described " + chord.Key;
        Assert.Equal("described F", box.DisplayText);
    }

    [Fact]
    public void ALostEndKeyRelease_GivesFocusBackOnTheNextTick()
    {
        var (root, box, _) = Mount();
        var keyboard = new FakeKeyboard();
        root.Keyboard = keyboard;
        Click(root);
        keyboard.Held.Add(Key.F);
        root.OnKeyDown((int)Key.F);
        root.Tick(0.016, 0);
        Assert.Same(box, root.KeyboardFocus);   // F still held

        keyboard.Held.Remove(Key.F);            // released, but its KeyUp never came
        root.Tick(0.016, 16);
        Assert.Null(root.KeyboardFocus);
    }

    [Fact]
    public void Classic_DrawsFillAndBorder()
    {
        var (_, box, _) = Mount("F5");
        var renderer = MarkupDrawProbe.Draw(box);
        Assert.Equal(1, MarkupDrawProbe.CountQuads(renderer, UiMarkupHotkey.ClassicBackground));
        Assert.Equal(4, MarkupDrawProbe.CountQuads(renderer, UiMarkupHotkey.ClassicBorder));
    }

    [Fact]
    public void Capturing_DrawsTheFocusBorder()
    {
        var (root, box, _) = Mount("F5");
        Click(root);
        var renderer = MarkupDrawProbe.Draw(box);
        // The outline's 4 quads; the prompt text is gold too, but glyphs are textured.
        Assert.True(MarkupDrawProbe.CountQuads(renderer, UiMarkupHotkey.FocusColor) >= 4);
        Assert.Equal(0, MarkupDrawProbe.CountQuads(renderer, UiMarkupHotkey.ClassicBorder));
    }

    [Fact]
    public void PluginTheme_DrawsPaletteFillAndBorder()
    {
        var (_, box, _) = Mount("F5");
        box.ThemePalette = PluginUiPalette.Moss;
        var renderer = MarkupDrawProbe.Draw(box);
        // The modern rounded field (PluginUiStyle.Field), not the Classic square box.
        Assert.Contains(ThemeDrawCapture.Vertices(renderer), v => Vector4.Distance(v.Color, PluginUiPalette.Moss.Field) < 0.01f);
        Assert.Contains(ThemeDrawCapture.Vertices(renderer), v => Vector4.Distance(v.Color, PluginUiPalette.Moss.Border) < 0.01f);
        Assert.Equal(0, MarkupDrawProbe.CountQuads(renderer, UiMarkupHotkey.ClassicBorder));
    }

    [Fact]
    public void Disabled_DrawsFaded()
    {
        var (_, box, _) = Mount("F5");
        box.Enabled = false;
        var renderer = MarkupDrawProbe.Draw(box);
        Vector4 faded = UiMarkupHotkey.ClassicBorder with
        {
            W = UiMarkupHotkey.ClassicBorder.W * UiMarkupToggle.DisabledAlpha,
        };
        Assert.Equal(4, MarkupDrawProbe.CountQuads(renderer, faded));
    }
}
