using System;
using System.IO;
using AcDream.App.UI;
using AcDream.Plugin.Abstractions;
using AcDream.UI.Abstractions.Panels.Settings;

namespace AcDream.App.Tests.UI;

public sealed class PluginCanvasPositionPersistenceTests : IDisposable
{
    private sealed class FakeCanvas(string key, int width = 200, int height = 100) : IRememberedCanvas
    {
        public string Key { get; } = key;
        public PluginCanvasAnchor Anchor { get; set; } = PluginCanvasAnchor.TopLeft;
        public PluginPoint Offset { get; set; } = new(10, 20);
        public (int Width, int Height) Size { get; } = (width, height);
        public bool IsHoldingPointer { get; set; }
    }

    private readonly string _path = Path.Combine(Path.GetTempPath(), $"canvas-persist-{Guid.NewGuid():N}.json");
    private readonly SettingsStore _store;
    private string _character = "alice";

    public PluginCanvasPositionPersistenceTests() => _store = new SettingsStore(_path);

    public void Dispose()
    {
        if (File.Exists(_path)) File.Delete(_path);
    }

    private PluginCanvasPositionPersistence Create() =>
        new(_store, () => _character, () => (800f, 600f));

    private PluginCanvasPosition? Saved(string key = "p/hud", string? character = null) =>
        _store.LoadPluginCanvasPosition(character ?? _character, key);

    [Fact]
    public void OnlyOptedInScreenCanvasesAreTracked()
    {
        var plain = new PluginCanvasDescriptor("a", 1, 1);
        Assert.False(PluginCanvasPositionPersistence.Tracks(plain));
        Assert.True(PluginCanvasPositionPersistence.Tracks(plain with { RemembersPosition = true }));
        Assert.False(PluginCanvasPositionPersistence.Tracks(plain with { RemembersPosition = true, InMarkup = true }));
    }

    [Fact]
    public void RestoreSetsTheSavedAnchorAndOffset()
    {
        _store.SavePluginCanvasPosition("alice", "p/hud", new PluginCanvasPosition("BottomRight", -30, -40));
        var canvas = new FakeCanvas("p/hud");
        using var persistence = Create();
        persistence.Add(canvas);

        persistence.RestoreAll();

        Assert.Equal(PluginCanvasAnchor.BottomRight, canvas.Anchor);
        Assert.Equal(new PluginPoint(-30, -40), canvas.Offset);
    }

    [Fact]
    public void ARestoredPositionIsKeptOnScreen()
    {
        _store.SavePluginCanvasPosition("alice", "p/hud", new PluginCanvasPosition("TopLeft", 5000, -10));
        var canvas = new FakeCanvas("p/hud");
        using var persistence = Create();
        persistence.Add(canvas);

        persistence.RestoreAll();

        Assert.Equal(new PluginPoint(600, 0), canvas.Offset);
    }

    [Fact]
    public void WithNothingSavedTheCanvasStaysWhereItIs()
    {
        var canvas = new FakeCanvas("p/hud") { Anchor = PluginCanvasAnchor.Center, Offset = new(1, 2) };
        using var persistence = Create();
        persistence.Add(canvas);

        persistence.RestoreAll();
        persistence.Tick(1.0);

        Assert.Equal(PluginCanvasAnchor.Center, canvas.Anchor);
        Assert.Equal(new PluginPoint(1, 2), canvas.Offset);
        Assert.Null(Saved());
    }

    [Fact]
    public void AnUnknownSavedAnchorIsIgnored()
    {
        _store.SavePluginCanvasPosition("alice", "p/hud", new PluginCanvasPosition("Sideways", 1, 1));
        var canvas = new FakeCanvas("p/hud");
        using var persistence = Create();
        persistence.Add(canvas);

        persistence.RestoreAll();

        Assert.Equal(new PluginPoint(10, 20), canvas.Offset);
    }

    [Fact]
    public void AMoveIsSavedOnceItHasSettled()
    {
        var canvas = new FakeCanvas("p/hud");
        using var persistence = Create();
        persistence.Add(canvas);
        persistence.RestoreAll();

        canvas.Offset = new PluginPoint(50, 60);
        persistence.Tick(0.016);
        persistence.Tick(0.4);
        Assert.Null(Saved());

        persistence.Tick(0.2);
        Assert.Equal(new PluginCanvasPosition("TopLeft", 50, 60), Saved());
    }

    [Fact]
    public void AnAnchorChangeAloneIsSaved()
    {
        var canvas = new FakeCanvas("p/hud");
        using var persistence = Create();
        persistence.Add(canvas);
        persistence.RestoreAll();

        canvas.Anchor = PluginCanvasAnchor.BottomLeft;
        persistence.Tick(0.016);
        persistence.Tick(0.6);

        Assert.Equal(new PluginCanvasPosition("BottomLeft", 10, 20), Saved());
    }

    [Fact]
    public void AHeldPressDelaysTheSaveUntilRelease()
    {
        var canvas = new FakeCanvas("p/hud");
        using var persistence = Create();
        persistence.Add(canvas);
        persistence.RestoreAll();

        canvas.IsHoldingPointer = true;
        canvas.Offset = new PluginPoint(50, 60);
        persistence.Tick(0.016);
        persistence.Tick(2.0);
        Assert.Null(Saved());

        canvas.IsHoldingPointer = false;
        persistence.Tick(0.016);
        Assert.Equal(new PluginCanvasPosition("TopLeft", 50, 60), Saved());
    }

    [Fact]
    public void ACanvasMovingEveryTickIsSavedOnlyWhenItStops()
    {
        var canvas = new FakeCanvas("p/hud");
        using var persistence = Create();
        persistence.Add(canvas);
        persistence.RestoreAll();

        for (int i = 1; i <= 100; i++)
        {
            canvas.Offset = new PluginPoint(i, 0);
            persistence.Tick(0.1);
            Assert.Null(Saved());
        }
        persistence.Tick(0.6);
        Assert.Equal(new PluginCanvasPosition("TopLeft", 100, 0), Saved());
    }

    [Theory]
    [InlineData("default")]
    [InlineData("DEFAULT")]
    [InlineData("")]
    [InlineData("  ")]
    public void ADefaultOrEmptyCharacterNeverSaves(string character)
    {
        _character = character;
        var canvas = new FakeCanvas("p/hud");
        using (var persistence = Create())
        {
            persistence.Add(canvas);
            persistence.RestoreAll();
            canvas.Offset = new PluginPoint(50, 60);
            persistence.Tick(0.016);
            persistence.Tick(1.0);
        }
        Assert.False(File.Exists(_path));
    }

    [Fact]
    public void NothingIsSavedBeforeTheFirstRestore()
    {
        var canvas = new FakeCanvas("p/hud");
        using (var persistence = Create())
        {
            persistence.Add(canvas);
            canvas.Offset = new PluginPoint(50, 60);
            persistence.Tick(0.016);
            persistence.Tick(1.0);
        }
        Assert.False(File.Exists(_path));
    }

    [Fact]
    public void RemovingACanvasFlushesItsPendingMove()
    {
        var canvas = new FakeCanvas("p/hud");
        using var persistence = Create();
        persistence.Add(canvas);
        persistence.RestoreAll();

        canvas.Offset = new PluginPoint(50, 60);
        persistence.Remove(canvas);

        Assert.Equal(new PluginCanvasPosition("TopLeft", 50, 60), Saved());
        canvas.Offset = new PluginPoint(70, 80);
        persistence.Tick(1.0);
        Assert.Equal(new PluginCanvasPosition("TopLeft", 50, 60), Saved());
    }

    [Fact]
    public void DisposeFlushesPendingMoves()
    {
        var canvas = new FakeCanvas("p/hud");
        var persistence = Create();
        persistence.Add(canvas);
        persistence.RestoreAll();

        canvas.Offset = new PluginPoint(50, 60);
        persistence.Dispose();

        Assert.Equal(new PluginCanvasPosition("TopLeft", 50, 60), Saved());
    }

    [Fact]
    public void ACharacterSwitchSavesToTheOldCharacterAndRestoresTheNewOne()
    {
        _store.SavePluginCanvasPosition("bob", "p/hud", new PluginCanvasPosition("Center", 3, 4));
        var canvas = new FakeCanvas("p/hud");
        using var persistence = Create();
        persistence.Add(canvas);
        persistence.RestoreAll();

        canvas.Offset = new PluginPoint(50, 60);
        _character = "bob";
        persistence.RestoreAll();

        Assert.Equal(new PluginCanvasPosition("TopLeft", 50, 60), Saved(character: "alice"));
        Assert.Equal(PluginCanvasAnchor.Center, canvas.Anchor);
        Assert.Equal(new PluginPoint(3, 4), canvas.Offset);
        persistence.Tick(1.0);
        Assert.Equal(new PluginCanvasPosition("Center", 3, 4), Saved(character: "bob"));
    }

    [Fact]
    public void ACanvasAddedAfterTheRestoreIsRestoredAtOnce()
    {
        _store.SavePluginCanvasPosition("alice", "p/hud", new PluginCanvasPosition("TopRight", -5, 6));
        using var persistence = Create();
        persistence.RestoreAll();

        var canvas = new FakeCanvas("p/hud");
        persistence.Add(canvas);

        Assert.Equal(PluginCanvasAnchor.TopRight, canvas.Anchor);
        Assert.Equal(new PluginPoint(-5, 6), canvas.Offset);
    }

    [Fact]
    public void ACanvasAddedAfterTheCharacterChangedWaitsForTheNextRestore()
    {
        _store.SavePluginCanvasPosition("bob", "p/hud", new PluginCanvasPosition("TopRight", -5, 6));
        using var persistence = Create();
        persistence.RestoreAll();
        _character = "bob";

        var canvas = new FakeCanvas("p/hud");
        persistence.Add(canvas);

        Assert.Equal(PluginCanvasAnchor.TopLeft, canvas.Anchor);
    }

    [Fact]
    public void ARegistrationIsKeyedByOwnerAndCanvasId()
    {
        var registry = new AcDream.App.Plugins.BufferedUiRegistry();
        IRememberedCanvas canvas = (IRememberedCanvas)registry.RegisterCanvas(
            new PluginUiOwner("aethrix", "Aethrix"), new PluginCanvasDescriptor("hud", 30, 40), _ => { });
        Assert.Equal("aethrix/hud", canvas.Key);
        Assert.Equal((30, 40), canvas.Size);
        Assert.False(canvas.IsHoldingPointer);
    }
}
