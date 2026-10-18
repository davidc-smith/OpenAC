using System.Numerics;
using AcDream.Rynth.Host;
using RynthCore.Engine.ImGuiBackend;

namespace AcDream.Rynth.Host.Tests;

public sealed class RynthLayoutStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "rynth-layout-" + Guid.NewGuid().ToString("N"));
    private readonly List<string> _log = new();

    private string FilePath => Path.Combine(_dir, RynthLayoutStore.FileName);

    public void Dispose()
    {
        if (Directory.Exists(_dir))
            Directory.Delete(_dir, recursive: true);
    }

    [Fact]
    public void WhatIsSavedIsReadBackByTheNextStore()
    {
        var first = new RynthLayoutStore(_dir, _log.Add);
        first.SetRect("script:A/Main", new PanelRect(new Vector2(30, 40), new Vector2(500, 300)));
        first.SetOpen("script:A/Main", true);
        first.SetOpen("script:A/Other", false);
        first.SetBarPosition(new Vector2(12, 90));

        var second = new RynthLayoutStore(_dir, _log.Add);

        Assert.Equal(new PanelPlacement(new PanelRect(new Vector2(30, 40), new Vector2(500, 300)), true), second.Get("script:A/Main"));
        Assert.Equal(new PanelPlacement(null, false), second.Get("script:A/Other"));
        Assert.Equal(new Vector2(12, 90), second.BarPosition);
        Assert.False(File.Exists(FilePath + ".tmp"));
        Assert.Empty(_log);
    }

    [Fact]
    public void AMissingFileIsAnEmptyLayout()
    {
        var store = new RynthLayoutStore(_dir, _log.Add);

        Assert.Null(store.Get("script:A/Main"));
        Assert.Null(store.BarPosition);
        Assert.False(Directory.Exists(_dir));
        Assert.Empty(_log);
    }

    [Fact]
    public void AnUnreadableFileIsLoggedOnceAndLeftAlone()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(FilePath, "{ not json");

        var store = new RynthLayoutStore(_dir, _log.Add);

        Assert.Null(store.Get("script:A/Main"));
        Assert.Single(_log, line => line.Contains("layout file unreadable", StringComparison.Ordinal));
        Assert.Equal("{ not json", File.ReadAllText(FilePath));
    }

    [Fact]
    public void WithoutAFolderNothingIsWritten()
    {
        var store = new RynthLayoutStore(null, _log.Add);
        store.SetOpen("script:A/Main", true);
        store.SetBarPosition(new Vector2(1, 2));

        Assert.Equal(new PanelPlacement(null, true), store.Get("script:A/Main"));
        Assert.False(Directory.Exists(_dir));
    }

    [Fact]
    public void OpenAndRectAreKeptIndependently()
    {
        var store = new RynthLayoutStore(_dir, _log.Add);
        var rect = new PanelRect(new Vector2(5, 6), new Vector2(200, 100));

        store.SetOpen("script:A/Main", true);
        store.SetRect("script:A/Main", rect);
        Assert.Equal(new PanelPlacement(rect, true), store.Get("script:A/Main"));

        store.SetOpen("script:A/Main", false);
        Assert.Equal(new PanelPlacement(rect, false), store.Get("script:A/Main"));
    }

    [Fact]
    public void AFileWithADuplicateKeyIsLoggedOnceAndTreatedAsEmpty()
    {
        Directory.CreateDirectory(_dir);
        const string json = "{\"windows\":{\"a\":{},\"a\":{}}}";
        File.WriteAllText(FilePath, json);

        var store = new RynthLayoutStore(_dir, _log.Add);

        Assert.Null(store.Get("a"));
        Assert.Null(store.BarPosition);
        Assert.Single(_log, line => line.Contains("layout file unreadable", StringComparison.Ordinal));
        Assert.Equal(json, File.ReadAllText(FilePath));
    }

    [Fact]
    public void ANonFiniteRectOrBarPositionIsIgnored()
    {
        var store = new RynthLayoutStore(_dir, _log.Add);

        store.SetRect("script:A/Main", new PanelRect(new Vector2(float.NaN, 1), new Vector2(10, 10)));
        store.SetRect("script:A/Main", new PanelRect(new Vector2(1, 1), new Vector2(float.PositiveInfinity, 10)));
        store.SetBarPosition(new Vector2(float.NegativeInfinity, 0));

        Assert.Null(store.Get("script:A/Main"));
        Assert.Null(store.BarPosition);
        Assert.False(File.Exists(FilePath));
        Assert.Empty(_log);
    }

    [Fact]
    public void AnOutOfRangeNumberLoadsThatWindowAsUnplaced()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(FilePath,
            "{\"version\":1,\"bar\":{\"x\":1e300,\"y\":5},\"windows\":{\"a\":{\"x\":1e300,\"y\":2,\"w\":3,\"h\":4,\"open\":true},\"b\":{\"x\":1,\"y\":2,\"w\":3,\"h\":4,\"open\":false}}}");

        var store = new RynthLayoutStore(_dir, _log.Add);

        Assert.Equal(new PanelPlacement(null, true), store.Get("a"));
        Assert.Equal(new PanelPlacement(new PanelRect(new Vector2(1, 2), new Vector2(3, 4)), false), store.Get("b"));
        Assert.Null(store.BarPosition);
        Assert.Empty(_log);
    }

    [Fact]
    public void TheFileOnDiskHasTheDocumentedShape()
    {
        var store = new RynthLayoutStore(_dir, _log.Add);
        store.SetOpen("script:A/Unplaced", true);
        store.SetRect("script:A/Placed", new PanelRect(new Vector2(1, 2), new Vector2(3, 4)));
        store.SetBarPosition(new Vector2(7, 8));

        var root = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(FilePath))!.AsObject();
        Assert.Equal(1, (int)root["version"]!);
        Assert.Equal(7f, (float)root["bar"]!["x"]!);
        Assert.Equal(8f, (float)root["bar"]!["y"]!);
        var unplaced = root["windows"]!["script:A/Unplaced"]!.AsObject();
        Assert.False(unplaced.ContainsKey("x") || unplaced.ContainsKey("y") || unplaced.ContainsKey("w") || unplaced.ContainsKey("h"));
        Assert.True((bool)unplaced["open"]!);
        var placed = root["windows"]!["script:A/Placed"]!.AsObject();
        Assert.Equal([1f, 2f, 3f, 4f], new[] { (float)placed["x"]!, (float)placed["y"]!, (float)placed["w"]!, (float)placed["h"]! });
    }
}
