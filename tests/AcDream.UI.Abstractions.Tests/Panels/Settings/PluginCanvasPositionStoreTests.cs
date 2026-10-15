using System;
using System.IO;
using AcDream.UI.Abstractions.Panels.Settings;

namespace AcDream.UI.Abstractions.Tests.Panels.Settings;

public sealed class PluginCanvasPositionStoreTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"canvas-position-{Guid.NewGuid():N}.json");

    public void Dispose() => File.Delete(_path);

    [Fact]
    public void ASavedPositionLoadsBackForThatCharacterAndCanvasOnly()
    {
        var store = new SettingsStore(_path);
        var position = new PluginCanvasPosition("BottomRight", -12.5, -40);
        store.SavePluginCanvasPosition("alice", "aethrix/hud", position);

        Assert.Equal(position, store.LoadPluginCanvasPosition("alice", "aethrix/hud"));
        Assert.Null(store.LoadPluginCanvasPosition("bob", "aethrix/hud"));
        Assert.Null(store.LoadPluginCanvasPosition("alice", "aethrix/xp"));
    }

    [Fact]
    public void SavingAPositionKeepsTheOtherSections()
    {
        var store = new SettingsStore(_path);
        var placement = new UiWindowPlacement(new UiWindowLayout(1, 2, 300, 200, true, false, false), 1920, 1080);
        store.SaveWindowPlacement("alice", "chat", placement);
        store.SavePluginCanvasPosition("alice", "aethrix/hud", new PluginCanvasPosition("TopLeft", 5, 6));

        Assert.Equal(placement, store.LoadWindowPlacement("alice", "1920x1080", "chat", placement.Layout));
        store.SavePluginCanvasPosition("alice", "aethrix/xp", new PluginCanvasPosition("Center", 0, 0));
        Assert.Equal(new PluginCanvasPosition("TopLeft", 5, 6), store.LoadPluginCanvasPosition("alice", "aethrix/hud"));
    }

    [Theory]
    [InlineData("{")]
    [InlineData("[]")]
    [InlineData("""{"pluginCanvasPositions":{"alice":{"aethrix/hud":{"x":1,"y":2}}}}""")]
    [InlineData("""{"pluginCanvasPositions":{"alice":{"aethrix/hud":{"anchor":"TopLeft","x":"one","y":2}}}}""")]
    [InlineData("""{"pluginCanvasPositions":{"alice":{"aethrix/hud":{"anchor":3,"x":1,"y":2}}}}""")]
    [InlineData("""{"pluginCanvasPositions":{"alice":"nope"}}""")]
    public void AMalformedEntryLoadsAsNull(string json)
    {
        File.WriteAllText(_path, json);
        Assert.Null(new SettingsStore(_path).LoadPluginCanvasPosition("alice", "aethrix/hud"));
    }

    [Fact]
    public void ANonFiniteNumberLoadsAsNull()
    {
        var store = new SettingsStore(_path);
        store.SavePluginCanvasPosition("alice", "aethrix/hud", new PluginCanvasPosition("TopLeft", 1, 2));
        File.WriteAllText(_path, File.ReadAllText(_path).Replace("\"x\": 1", "\"x\": 1e400"));
        Assert.Null(store.LoadPluginCanvasPosition("alice", "aethrix/hud"));
    }
}
