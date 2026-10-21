// Copyright (c) OpenAC contributors.
// Distributed under the terms of the MIT license.

using AcDream.Plugin.Abstractions;

namespace AcDream.Plugin.Tests;

/// <summary>
/// A markup hotkey box reports its chord as text; a plugin turns that text
/// into the chord its hotkey registry takes, and back again.
/// </summary>
public sealed class PluginKeyChordTextTests
{
    public static TheoryData<string, PluginKeyChord> Readable => new()
    {
        { "Ctrl+Alt+Shift+F", new PluginKeyChord(PluginKey.F, Ctrl: true, Alt: true, Shift: true) },
        { "shift+ctrl+keypad5", new PluginKeyChord(PluginKey.Numpad5, Ctrl: true, Shift: true) },
        { "Numpad5", new PluginKeyChord(PluginKey.Numpad5) },
        { "GraveAccent", new PluginKeyChord(PluginKey.Grave) },
        { "Grave", new PluginKeyChord(PluginKey.Grave) },
        { "KeypadEnter", new PluginKeyChord(PluginKey.NumpadEnter) },
        { "F12", new PluginKeyChord(PluginKey.F12) },
    };

    [Theory]
    [MemberData(nameof(Readable))]
    public void TryParseHotkeyText_ReadsAHotkeyBoxString(string text, PluginKeyChord expected)
    {
        Assert.True(PluginKeyChord.TryParseHotkeyText(text, out PluginKeyChord chord));
        Assert.Equal(expected, chord);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("F13")]
    [InlineData("CapsLock")]
    [InlineData("Ctrl+")]
    [InlineData("+F")]
    [InlineData("Ctrl+Ctrl+F")]
    [InlineData("Meta+F")]
    [InlineData("5")]
    [InlineData("A,B")]
    [InlineData("Unknown")]
    [InlineData("Ctrl+Nope")]
    public void TryParseHotkeyText_RefusesWhatNoPluginHotkeyCanHold(string? text)
    {
        Assert.False(PluginKeyChord.TryParseHotkeyText(text, out PluginKeyChord chord));
        Assert.Equal(default, chord);
    }

    [Theory]
    [InlineData(PluginKey.F, true, true, true, "Ctrl+Alt+Shift+F")]
    [InlineData(PluginKey.Numpad5, false, false, false, "Keypad5")]
    [InlineData(PluginKey.Grave, false, false, false, "GraveAccent")]
    [InlineData(PluginKey.NumpadEnter, false, true, false, "Alt+KeypadEnter")]
    public void ToHotkeyText_WritesTheBoxsSpelling(PluginKey key, bool ctrl, bool alt, bool shift, string expected) =>
        Assert.Equal(expected, new PluginKeyChord(key, ctrl, alt, shift).ToHotkeyText());

    [Fact]
    public void ToHotkeyText_NoKeyIsEmpty()
    {
        Assert.Equal("", default(PluginKeyChord).ToHotkeyText());
        Assert.Equal("", new PluginKeyChord((PluginKey)999, Ctrl: true).ToHotkeyText());
    }

    [Fact]
    public void EveryPluginKey_RoundTripsWithEveryModifierSet()
    {
        foreach (PluginKey key in Enum.GetValues<PluginKey>())
        {
            if (key == PluginKey.Unknown)
                continue;
            for (int mods = 0; mods < 8; mods++)
            {
                var chord = new PluginKeyChord(key, (mods & 1) != 0, (mods & 2) != 0, (mods & 4) != 0);
                string text = chord.ToHotkeyText();
                Assert.True(PluginKeyChord.TryParseHotkeyText(text, out PluginKeyChord back), text);
                Assert.Equal(chord, back);
            }
        }
    }
}
