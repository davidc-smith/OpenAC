using AcDream.Rynth.Host;
using RynthCore.PluginSdk;

namespace AcDream.Rynth.Host.Tests;

public sealed unsafe class RynthApiTableTests
{
    private readonly List<string> _log = new();

    [Fact]
    public void NewTable_HasApiVersionAndOnlyTheImplementedSlots()
    {
        using var table = new RynthApiTable(new FakeGameView(), _log.Add);
        var host = new RynthCoreHost(*table.Native);

        Assert.Equal(86u, host.Version);
        Assert.Equal(RynthCoreHost.CurrentApiVersion, RynthApiTable.ApiVersion);
        Assert.True(host.HasGetPlayerId);
        Assert.True(host.HasGetObjectName);
        Assert.True(host.HasGetObjectQuadProperty);
        Assert.False(host.HasCastSpell);
        Assert.False(host.HasUseObject);
    }

    [Fact]
    public void TableFunctions_ReadTheGameView()
    {
        var view = new FakeGameView { PlayerId = 0x50000001 };
        view.Names[0x80000010] = "Drudge Skulker";
        view.Int64s[(0x50000001, 1)] = 123_456;
        using var table = new RynthApiTable(view, _log.Add);
        var host = new RynthCoreHost(*table.Native);

        Assert.Equal(0x50000001u, host.GetPlayerId());
        Assert.True(host.TryGetObjectName(0x80000010, out string name));
        Assert.Equal("Drudge Skulker", name);
        Assert.False(host.TryGetObjectName(0x80000011, out _));
        Assert.True(host.TryGetObjectQuadProperty(0x50000001, 1, out long xp));
        Assert.Equal(123_456, xp);
        Assert.False(host.TryGetObjectQuadProperty(0x50000001, 6, out _));

        Assert.True(host.WriteToChat("hello", 3));
        Assert.Equal(("hello", 3), Assert.Single(view.ChatLines));

        host.Log("from the plugin");
        Assert.Contains("from the plugin", _log);
    }

    [Fact]
    public void TableFunctions_ReturnNotAvailableWhenTheViewThrows()
    {
        var view = new FakeGameView { Throw = true };
        using var table = new RynthApiTable(view, _log.Add);
        var host = new RynthCoreHost(*table.Native);

        Assert.False(host.TryGetObjectName(1, out _));
        Assert.False(host.TryGetObjectQuadProperty(1, 1, out _));
        Assert.Equal(0u, host.GetPlayerId());
        Assert.False(host.WriteToChat("hello", 3));
        Assert.Contains(_log, line => line.Contains("GetObjectName threw", StringComparison.Ordinal));
        Assert.Contains(_log, line => line.Contains("GetPlayerId threw", StringComparison.Ordinal));
        Assert.Contains(_log, line => line.Contains("WriteToChat threw", StringComparison.Ordinal));
    }

    [Fact]
    public void OnlyOneTableCanBeLive()
    {
        using var first = new RynthApiTable(new FakeGameView(), _log.Add);
        Assert.Throws<InvalidOperationException>(() => new RynthApiTable(new FakeGameView(), _log.Add));
    }
}
