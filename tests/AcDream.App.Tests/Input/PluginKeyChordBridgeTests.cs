using AcDream.App.Input;
using AcDream.App.UI.Markup;
using AcDream.Plugin.Abstractions;
using AcDream.UI.Abstractions.Input;
using Silk.NET.Input;

namespace AcDream.App.Tests.Input;

/// <summary>
/// A markup hotkey box, the plugin chord bridge and the hotkey registry must
/// agree on which physical key every name means.
/// </summary>
public sealed class PluginKeyChordBridgeTests
{
    [Fact]
    public void EveryPluginKey_NamesTheKeyTheRegistryBinds()
    {
        foreach (PluginKey key in Enum.GetValues<PluginKey>())
        {
            if (key == PluginKey.Unknown)
                continue;
            string text = new PluginKeyChord(key, Ctrl: true).ToHotkeyText();
            KeyChord? box = MarkupHotkeyText.Parse(text);
            Assert.True(box is not null, $"{key} wrote '{text}', which a hotkey box can't read");
            Assert.Equal(AppHotkeyRegistry.MapKey(key), box.Value.Key);
            Assert.Equal(ModifierMask.Ctrl, box.Value.Modifiers);
        }
    }

    [Fact]
    public void EveryRecordableKey_ParsesExactlyWhenTheRegistryCanBindIt()
    {
        var bindable = new Dictionary<Key, PluginKey>();
        foreach (PluginKey key in Enum.GetValues<PluginKey>())
        {
            if (AppHotkeyRegistry.MapKey(key) is { } silk)
                bindable.Add(silk, key);
        }

        foreach (Key key in Enum.GetValues<Key>().Distinct())
        {
            if (!MarkupHotkeyText.IsChordKey(key))
                continue;
            string text = MarkupHotkeyText.Format(new KeyChord(key, ModifierMask.Shift));
            bool read = PluginKeyChord.TryParseHotkeyText(text, out PluginKeyChord chord);
            if (bindable.TryGetValue(key, out PluginKey expected))
            {
                Assert.True(read, $"'{text}' should read as {expected}");
                Assert.Equal(new PluginKeyChord(expected, Shift: true), chord);
            }
            else
            {
                Assert.False(read, $"'{text}' read as {chord.Key}, which the registry can't bind");
            }
        }
    }
}
