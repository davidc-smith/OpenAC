using AcDream.App.Input;
using AcDream.Plugin.Abstractions;
using Silk.NET.Input;

namespace AcDream.App.Tests.Input;

// One table serves both directions: hotkeys map a plugin key to the
// window's key, a focused canvas maps the window's key back.
public sealed class PluginKeyMapTests
{
    [Fact]
    public void EveryNamedPluginKeyMapsToADistinctSilkKeyAndBack()
    {
        var seen = new HashSet<Key>();
        foreach (PluginKey key in Enum.GetValues<PluginKey>())
        {
            if (key == PluginKey.Unknown) continue;
            Key? silk = PluginKeyMap.ToSilk(key);
            Assert.True(silk.HasValue, $"{key} has no Silk key");
            Assert.True(seen.Add(silk.Value), $"{silk} is named twice");
            Assert.Equal(key, PluginKeyMap.FromSilk(silk.Value));
        }
    }

    [Fact]
    public void UnknownAndKeysTheContractCannotNameMapToNothing()
    {
        Assert.Null(PluginKeyMap.ToSilk(PluginKey.Unknown));
        Assert.Equal(PluginKey.Unknown, PluginKeyMap.FromSilk(Key.ShiftLeft));
        Assert.Equal(PluginKey.Unknown, PluginKeyMap.FromSilk(Key.ControlRight));
        Assert.Equal(PluginKey.Unknown, PluginKeyMap.FromSilk(Key.CapsLock));
        Assert.Equal(PluginKey.Unknown, PluginKeyMap.FromSilk(Key.Unknown));
    }

    [Fact]
    public void TheWindowsOwnNamesAreTranslated()
    {
        Assert.Equal(PluginKey.Numpad5, PluginKeyMap.FromSilk(Key.Keypad5));
        Assert.Equal(PluginKey.Grave, PluginKeyMap.FromSilk(Key.GraveAccent));
        Assert.Equal(Key.KeypadEnter, PluginKeyMap.ToSilk(PluginKey.NumpadEnter));
    }
}
