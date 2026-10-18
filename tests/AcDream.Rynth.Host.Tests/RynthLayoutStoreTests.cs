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
}
