using System;
using System.Collections.Generic;
using System.IO;
using AcDream.UI.Abstractions.Panels.Settings;

namespace AcDream.UI.Abstractions.Tests.Panels.Settings;

public sealed class MarkupControlStateSettingsTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"markup-controls-{Guid.NewGuid():N}.json");

    public void Dispose() => File.Delete(_path);

    private static Dictionary<MarkupControlKey, string> Load(SettingsStore store, string toon)
    {
        Assert.True(store.TryLoadMarkupControlState(toon, out var values));
        return values;
    }

    private static Dictionary<MarkupControlKey, string> Values(params (string W, string C, string K, string V)[] entries)
    {
        var values = new Dictionary<MarkupControlKey, string>();
        foreach (var (w, c, k, v) in entries)
            values[new MarkupControlKey(w, c, k)] = v;
        return values;
    }

    [Fact]
    public void SavedValuesLoadBack()
    {
        var store = new SettingsStore(_path);
        var saved = Values(("nav", "adv", "expanded", "false"), ("nav", "mobs", "widths", "120,40"),
            ("loot", "adv", "expanded", "true"));
        store.SaveMarkupControlState("alice", saved);
        Assert.Equal(saved, Load(store, "alice"));
    }

    [Fact]
    public void ASaveReplacesTheCharactersSetAndKeepsEverythingElse()
    {
        var store = new SettingsStore(_path);
        store.SaveWindowPosition("alice", "chat", new UiWindowPosition(10, 20));
        store.SaveMarkupControlState("bob", Values(("nav", "adv", "expanded", "true")));
        store.SaveMarkupControlState("alice", Values(("nav", "adv", "expanded", "false"), ("nav", "x", "k", "1")));
        store.SaveMarkupControlState("alice", Values(("nav", "adv", "expanded", "true")));

        Assert.Equal(Values(("nav", "adv", "expanded", "true")), Load(store, "alice"));
        Assert.Equal(Values(("nav", "adv", "expanded", "true")), Load(store, "bob"));
        Assert.Equal(new UiWindowPosition(10, 20), store.LoadWindowPosition("alice", "chat"));
    }

    [Fact]
    public void OtherSectionsSavesKeepTheMarkupControls()
    {
        var store = new SettingsStore(_path);
        store.SaveMarkupControlState("alice", Values(("nav", "adv", "expanded", "false")));
        store.SaveWindowPosition("alice", "chat", new UiWindowPosition(1, 2));
        store.SavePluginUiTheme("Moss");
        Assert.Equal(Values(("nav", "adv", "expanded", "false")), Load(store, "alice"));
    }

    [Fact]
    public void AMissingFileLoadsEmpty()
    {
        Assert.Empty(Load(new SettingsStore(_path), "alice"));
    }

    [Fact]
    public void MalformedEntriesAreSkipped()
    {
        File.WriteAllText(_path, """
            {"markupControls":{"alice":{
              "nav":{"adv":{"expanded":5,"open":"yes"},"bad":3},
              "loot":7
            },"bob":"nope"}}
            """);
        var store = new SettingsStore(_path);
        Assert.Equal(Values(("nav", "adv", "open", "yes")), Load(store, "alice"));
        Assert.Empty(Load(store, "bob"));
    }

    [Fact]
    public void ANonObjectSectionLoadsEmpty()
    {
        File.WriteAllText(_path, """{"markupControls":42}""");
        Assert.Empty(Load(new SettingsStore(_path), "alice"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void ABlankCharacterKeyThrows(string? key)
    {
        var store = new SettingsStore(_path);
        Assert.ThrowsAny<ArgumentException>(() => store.TryLoadMarkupControlState(key!, out _));
        Assert.ThrowsAny<ArgumentException>(() => store.SaveMarkupControlState(key!, Values()));
    }

    [Fact]
    public void AnUnreadableFileReportsFailure()
    {
        File.WriteAllText(_path, "{ not json");
        Assert.False(new SettingsStore(_path).TryLoadMarkupControlState("alice", out var values));
        Assert.Empty(values);
    }
}
