using System.IO.Compression;
using AcDream.TestSupport;
using AcDream.Rynth.Host;
using RynthCore.Engine.Compatibility;
using RynthCore.Engine.ImGuiBackend;
using RynthCore.Engine.ImGuiBackend.Panels;
using RynthCore.Engine.UI.Data;

namespace AcDream.Rynth.Host.Tests;

public sealed class RynthWorldMapTests : IDisposable
{
    private readonly Func<byte[], int, int, IntPtr>? _upload = DX9Backend.Upload;
    private readonly Func<IntPtr, bool>? _release = DX9Backend.Release;

    private readonly List<string> _temp = new();

    public void Dispose()
    {
        MapTilesReset.Settle();
        DX9Backend.Upload = _upload;
        DX9Backend.Release = _release;
        foreach (string f in _temp) { try { File.Delete(f); } catch { } }
    }

    // One DXT1 tile (256 x 256 = 64 x 64 blocks): every block c0 red, c1 blue, all indices 0 (c0).
    private static byte[] RedDxt1Tile()
    {
        var blocks = new byte[64 * 64 * 8];
        for (int b = 0; b < blocks.Length; b += 8)
        {
            blocks[b] = 0x00; blocks[b + 1] = 0xF8;      // c0 = 0xF800 (red)
            blocks[b + 2] = 0x1F; blocks[b + 3] = 0x00;  // c1 = 0x001F (blue); c0 > c1: four-colour form
        }
        return blocks;
    }

    [Fact]
    public void ADxt1TileUploadsAsRgba()
    {
        var uploads = new List<(byte[] Rgba, int W, int H)>();
        DX9Backend.Upload = (rgba, w, h) => { uploads.Add((rgba, w, h)); return (IntPtr)7; };

        IntPtr tex = DX9Backend.CreateTextureDxt1(IntPtr.Zero, 256, 256, RedDxt1Tile());

        Assert.Equal((IntPtr)7, tex);
        var (data, width, height) = Assert.Single(uploads);
        Assert.Equal((256, 256, 256 * 256 * 4), (width, height, data.Length));
        Assert.Equal(new byte[] { 255, 0, 0, 255 }, data[..4]);
    }

    [Fact]
    public void WithNoUploadADxt1TileIsNoTexture()
    {
        DX9Backend.Upload = null;
        Assert.Equal(IntPtr.Zero, DX9Backend.CreateTextureDxt1(IntPtr.Zero, 256, 256, RedDxt1Tile()));
    }

    // RMAP v1 (WorldMapTiles' header comment): level 0 = 2 x 2 tiles, level 1 = the top tile; RGB tiles, zlib.
    internal static string WriteMap(List<string> temp, byte r, byte g, byte b)
    {
        const int tile = 256;
        var levels = new (int W, int H, int Cols, int Rows, int First)[] { (512, 512, 2, 2, 0), (256, 256, 1, 1, 4) };
        var tiles = new List<byte[]>();
        for (int i = 0; i < 5; i++)
        {
            var rgb = new byte[tile * tile * 3];
            for (int p = 0; p < rgb.Length; p += 3) { rgb[p] = r; rgb[p + 1] = g; rgb[p + 2] = b; }
            using var ms = new MemoryStream();
            using (var z = new ZLibStream(ms, CompressionLevel.Fastest, leaveOpen: true)) z.Write(rgb);
            tiles.Add(ms.ToArray());
        }
        string path = Path.Combine(Path.GetTempPath(), $"rynth-wmap-{Guid.NewGuid():N}.rmap");
        temp.Add(path);
        using var fs = File.Create(path);
        using var w = new BinaryWriter(fs);
        w.Write(0x5041_4D52u); w.Write(1u); w.Write((uint)tile); w.Write((uint)levels.Length);
        w.Write(2u); w.Write(256u); w.Write(256u);                      // px per cell, cells wide, cells high
        foreach (var l in levels) { w.Write((uint)l.W); w.Write((uint)l.H); w.Write((uint)l.Cols); w.Write((uint)l.Rows); w.Write((uint)l.First); }
        w.Write((uint)tiles.Count);
        long offset = 7 * 4 + levels.Length * 5 * 4 + 4 + tiles.Count * 12;
        foreach (byte[] t in tiles) { w.Write((ulong)offset); w.Write((uint)t.Length); offset += t.Length; }
        foreach (byte[] t in tiles) w.Write(t);
        return path;
    }

    private static void WaitFor(Func<bool> done)
    {
        var until = DateTime.UtcNow.AddSeconds(5);
        while (!done() && DateTime.UtcNow < until) Thread.Sleep(10);
        Assert.True(done());
    }

    [Fact]
    public void TheTopTileOfAMapFileBecomesAnOverlayTexture()
    {
        MapTilesReset.Settle();
        var uploads = new List<byte[]>();
        var released = new List<IntPtr>();
        DX9Backend.Upload = (rgba, w, h) => { Assert.Equal((256, 256), (w, h)); uploads.Add(rgba); return (IntPtr)(100 + uploads.Count); };
        DX9Backend.Release = tex => { released.Add(tex); return true; };

        WorldMapTiles.Open(WriteMap(_temp, 10, 20, 30));
        WaitFor(() => WorldMapTiles.IsOpen);
        Assert.Equal(2, WorldMapTiles.Levels.Count);

        IntPtr tex = IntPtr.Zero;
        WaitFor(() => { WorldMapTiles.BeginFrame(IntPtr.Zero); return WorldMapTiles.TryGet(1, 0, 0, out tex); });
        Assert.Equal((IntPtr)101, tex);
        Assert.Equal(new byte[] { 10, 20, 30, 255 }, uploads[0][..4]);

        WorldMapTiles.Shutdown();
        Assert.Equal([(IntPtr)101], released);
        Assert.False(WorldMapTiles.IsOpen);
    }

    [Fact]
    public void AMissingMapFileSaysSo()
    {
        MapTilesReset.Settle();
        WorldMapTiles.Open(Path.Combine(Path.GetTempPath(), $"rynth-wmap-missing-{Guid.NewGuid():N}.rmap"));
        WaitFor(() => WorldMapTiles.Status.StartsWith("No world map file yet", StringComparison.Ordinal));
    }

    [Fact]
    public void WithNoHostTheMapHooksAnswerNothing()
    {
        Assert.Empty(ClientObjectHooks.LiveObjectIds);
        Assert.False(ClientObjectHooks.TryGetSnapshotPosition(1, out _, out _, out _, out _));
        Assert.False(PlayerVitalsHooks.TryGetSnapshot(out _));
        Assert.False(TeleportStateHooks.IsPortaling);
    }

    [Fact]
    public void TheInstalledWorldMapOpensWhenThereIsOne()
    {
        string path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "RynthCore", "NavData", "worldmap.rmap");
        if (!File.Exists(path)) return;   // NavData not installed on this machine
        MapTilesReset.Settle();
        var uploads = 0;
        DX9Backend.Upload = (_, _, _) => (IntPtr)(++uploads);
        DX9Backend.Release = _ => true;
        WorldMapTiles.Open(path);
        WaitFor(() => WorldMapTiles.IsOpen);
        int top = WorldMapTiles.Levels.Count - 1;
        WaitFor(() => { WorldMapTiles.BeginFrame(IntPtr.Zero); return WorldMapTiles.TryGet(top, 0, 0, out _); });
    }
}

public sealed class RynthWorldMapHostTests : IDisposable
{
    private bool _drewAFace;   // a drawn map face started WorldMapMaps.Refresh
    private readonly List<string> _log = new();
    private readonly RynthConfigSandbox _sandbox = new("rynth-wmap-");
    private readonly FakeGameView _view = new() { PlayerId = 0x50000001u };
    private readonly List<string> _temp = new();

    public RynthWorldMapHostTests()
    {
        ImGuiPanelHost.Reset();
        ImGuiPanelHost.Log = _log.Add;
        UiDataHub.Clear();
    }

    public void Dispose()
    {
        AcDream.Rynth.EngineUi.RynthAiDashboard.Stop();
        MapTilesReset.Settle(awaitScan: _drewAFace);
        ImGuiPanelHost.Reset();
        ImGuiPanelHost.Log = null;
        _sandbox.Dispose();
        foreach (string f in _temp) File.Delete(f);
    }

    [Fact]
    public void TheDashboardRegistersTheWorldMapAndMiniMap()
    {
        AcDream.Rynth.EngineUi.RynthAiDashboard.Start();
        Assert.True(ImGuiPanelHost.HasFace(WorldMapFace.Title));
        Assert.True(ImGuiPanelHost.HasFace(MiniMapFace.Title));
    }

    [Fact]
    public void TheMapHooksAnswerFromTheView()
    {
        _view.Live = [0x80000001u, 0x80000002u];
        _view.Positions[0x80000001u] = (0xA9B40021u, new System.Numerics.Vector3(1, 2, 3));
        _view.Vitals = new RynthVitals(50, 100, 60, 120, 70, 140);
        _view.Portaling = true;
        using (new RynthMapHookup(_view, _log.Add))
        {
            Assert.Equal([0x80000001u, 0x80000002u], ClientObjectHooks.LiveObjectIds);
            Assert.True(ClientObjectHooks.TryGetSnapshotPosition(0x80000001u, out uint cell, out float x, out float y, out float z));
            Assert.Equal((0xA9B40021u, 1f, 2f, 3f), (cell, x, y, z));
            Assert.False(ClientObjectHooks.TryGetSnapshotPosition(0x80000002u, out _, out _, out _, out _));
            Assert.True(PlayerVitalsHooks.TryGetSnapshot(out PlayerVitalsSnapshot v));
            Assert.Equal(new PlayerVitalsSnapshot(50, 100, 60, 120, 70, 140), v);
            Assert.True(TeleportStateHooks.IsPortaling);
        }
        Assert.Empty(ClientObjectHooks.LiveObjectIds);
        Assert.False(TeleportStateHooks.IsPortaling);
    }

    [Fact]
    public void ALaterHookupKeepsItsHooksWhenAnEarlierOneIsDisposed()
    {
        var first = new RynthMapHookup(_view, _log.Add);
        using var second = new RynthMapHookup(_view, _log.Add);
        _view.Portaling = true;
        first.Dispose();
        Assert.True(TeleportStateHooks.IsPortaling);
    }

    [Fact]
    public void AViewThatThrowsIsLoggedAndAnswersNothing()
    {
        using var hookup = new RynthMapHookup(_view, _log.Add);
        _view.Throw = true;
        Assert.Empty(ClientObjectHooks.LiveObjectIds);
        Assert.Contains(_log, l => l.StartsWith("[Rynth] map LiveObjectIds: view failed:", StringComparison.Ordinal));
    }

    [Fact]
    public void TheCharactersSourceIsPumpedByTheDashboard()
    {
        AcDream.Rynth.EngineUi.RynthAiDashboard.Start();
        Assert.Contains(UiSources.Characters, AcDream.Rynth.EngineUi.RynthAiDashboard.HubSources);
    }

    [Fact]
    public void TheWorldMapDrawsWithNoErrors()
    {
        using var imgui = new HeadlessImGui();
        LoginLifecycleHooks.HasObservedLoginComplete = true;
        try
        {
            AcDream.Rynth.EngineUi.RynthAiDashboard.Start();
            _drewAFace = true;
            RynthCore.Engine.UI.PanelRouter.Toggle(WorldMapFace.Title);
            for (int i = 0; i < 3; i++)
                imgui.Frame(() => { AcDream.Rynth.EngineUi.RynthAiDashboard.BeginFrame(); ImGuiPanelHost.DrawAll(); });
            Assert.True(ImGuiPanelHost.IsOpen(WorldMapFace.Title));
            Assert.Empty(_log);
        }
        finally { LoginLifecycleHooks.HasObservedLoginComplete = false; }
    }

    private sealed class RecordingIcons : AcDream.ClientExtensions.IClientOverlayIcons
    {
        private int _next;
        public List<IntPtr> Released { get; } = new();
        public IntPtr Icon(uint iconId) => IntPtr.Zero;
        public IntPtr SpellIcon(uint spellId) => IntPtr.Zero;
        public IntPtr ObjectIcon(uint objectId) => IntPtr.Zero;
        IntPtr AcDream.ClientExtensions.IClientOverlayIcons.Upload(byte[] rgba, int width, int height, string name) => (IntPtr)(++_next);
        bool AcDream.ClientExtensions.IClientOverlayIcons.Release(IntPtr texture) { Released.Add(texture); return true; }
    }

    [Fact]
    public void InventoryTeardownReleasesTheMapTilesThroughItsUpload()
    {
        MapTilesReset.Settle();
        var icons = new RecordingIcons();
        var hookup = new RynthInventoryHookup(_view, icons, _log.Add);
        WorldMapTiles.Open(RynthWorldMapTests.WriteMap(_temp, 1, 2, 3));
        Assert.True(SpinWait.SpinUntil(() => WorldMapTiles.IsOpen, 5000));
        Assert.True(SpinWait.SpinUntil(() => { WorldMapTiles.BeginFrame(IntPtr.Zero); return WorldMapTiles.TryGet(1, 0, 0, out _); }, 5000));
        hookup.Dispose();
        Assert.Single(icons.Released);
    }
}
