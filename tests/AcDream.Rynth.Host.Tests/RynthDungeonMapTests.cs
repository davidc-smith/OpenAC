using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using AcDream.Rynth.Host;
using AcDream.TestSupport;
using RynthCore.Engine.Compatibility;
using RynthCore.Engine.ImGuiBackend;
using RynthCore.Engine.ImGuiBackend.Panels;
using RynthCore.Engine.UI.Data;
using RynthCore.Engine.UI.Panels;

namespace AcDream.Rynth.Host.Tests;

public sealed unsafe class RynthDungeonMapTests : IDisposable
{
    // Indoor: landblock 0x0153 (Shreth Hive), one floor at z 0 with a strip, one wall, a monster and a door.
    private const string IndoorJson =
        "{\"mapVersion\":7,\"geometryIncluded\":true,\"isIndoor\":true," +
        "\"player\":{\"cellId\":22217000,\"landblock\":339,\"x\":10,\"y\":10,\"z\":0,\"worldX\":10,\"worldY\":10,\"heading\":90}," +
        "\"layerZs\":[0],\"currentLayerZ\":0," +
        "\"walls\":[{\"z\":0,\"segments\":[0,0,20,0,10,10]}]," +
        "\"fills\":[{\"z\":0,\"strips\":[0,0,20,20,0]}]," +
        "\"visited\":[{\"z\":0,\"strips\":[0,0,10,10]}]," +
        "\"markers\":[{\"kind\":0,\"x\":12,\"y\":12,\"z\":0,\"label\":\"Drudge\",\"id\":1},{\"kind\":3,\"x\":5,\"y\":0,\"z\":0,\"label\":\"Door\",\"id\":2}]}";

    private const string OutdoorJson =
        "{\"mapVersion\":8,\"geometryIncluded\":false,\"isIndoor\":false," +
        "\"player\":{\"cellId\":2847146017,\"landblock\":43444,\"x\":100,\"y\":100,\"z\":50,\"worldX\":100,\"worldY\":100,\"heading\":0}," +
        "\"ns\":12.5,\"ew\":40.1,\"markers\":[]}";

    private static IntPtr _json;

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static IntPtr Snapshot(uint mapVersion) => _json;

    private readonly List<string> _log = new();
    private readonly RynthConfigSandbox _sandbox = new("rynth-dmap-");
    private readonly FakeGameView _view = new() { PlayerId = 0x50000001u };

    public RynthDungeonMapTests()
    {
        ImGuiPanelHost.Reset();
        ImGuiPanelHost.Log = _log.Add;
        UiDataHub.Clear();
    }

    public void Dispose()
    {
        AcDream.Rynth.EngineUi.RynthAiDashboard.Stop();
        PluginExportBinder.Resolver = null;
        ImGuiPanelHost.Reset();
        ImGuiPanelHost.Log = null;
        LoginLifecycleHooks.HasObservedLoginComplete = false;
        SetJson(null);
        _sandbox.Dispose();
    }

    private static void SetJson(string? json)
    {
        IntPtr old = _json;
        _json = json is null ? IntPtr.Zero : Marshal.StringToHGlobalAnsi(json);
        if (old != IntPtr.Zero)
            Marshal.FreeHGlobal(old);
    }

    private static void UseTestExport() =>
        PluginExportBinder.Resolver = (plugin, export) =>
            plugin == "RynthAi" && export == "RynthPluginGetRadarSnapshot"
                ? (IntPtr)(delegate* unmanaged[Cdecl]<uint, IntPtr>)&Snapshot
                : IntPtr.Zero;

    [Fact]
    public void TheRadarFaceStandInHasTheirLegendRows()
    {
        string theirs = File.ReadAllText(Path.Combine(RynthAiDashboardTests.RepoRoot(),
            "external/RynthCore/src/RynthCore.Engine/ImGui/Panels/RadarFace.cs"));
        string table = theirs[theirs.IndexOf("KindRowsOrder", StringComparison.Ordinal)..];
        table = table[..table.IndexOf("};", StringComparison.Ordinal)];
        var rows = Regex.Matches(table, @"\(RadarKind\.(\w+), ""([^""]+)""\)")
            .Select(m => ((byte)typeof(RadarKind).GetField(m.Groups[1].Value)!.GetValue(null)!, m.Groups[2].Value))
            .ToArray();

        Assert.Equal(12, rows.Length);
        Assert.Equal(rows, RadarFace.KindRowsOrder.Select(r => (r.Kind, r.Label)).ToArray());
    }

    [Fact]
    public void TheRealRynthAiRadarExportReachesTheRadarSource()
    {
        _view.Pose = (0xA9B40021u, new System.Numerics.Vector3(100, 100, 50), System.Numerics.Quaternion.Identity);
        _view.PlayerHeading = 0f;
        using var host = new RynthHost(_view, _log.Add, [typeof(RynthCore.Plugin.RynthAi.PluginExports)]);
        using var exports = new RynthChatHookup(host, _view, _log.Add);
        using var ai = new RynthAiHookup(host, _view, _log.Add);
        AcDream.Rynth.EngineUi.RynthAiDashboard.Start();
        UiSources.Radar.Subscribe();
        try
        {
            UiSources.Radar.RequestRefresh();
            AcDream.Rynth.EngineUi.RynthAiDashboard.Tick();

            RadarView? view = UiSources.Radar.Current?.Value;
            Assert.NotNull(view);
            Assert.Equal(0xA9B40021u, view!.Live.Player.CellId);
            Assert.False(view.Live.IsIndoor);
        }
        finally { UiSources.Radar.Unsubscribe(); }
    }

    [Fact]
    public void TheMapRegistersWithTheDashboardAndDrawsAnIndoorSnapshot()
    {
        using var imgui = new HeadlessImGui();
        LoginLifecycleHooks.HasObservedLoginComplete = true;
        AcDream.Rynth.EngineUi.RynthAiDashboard.Start();
        UseTestExport();
        SetJson(IndoorJson);
        Assert.True(ImGuiPanelHost.HasFace(DungeonMapFace.Title));

        RynthCore.Engine.UI.PanelRouter.Toggle(DungeonMapFace.Title);
        try
        {
            for (int i = 0; i < 3; i++)
            {
                AcDream.Rynth.EngineUi.RynthAiDashboard.Tick();
                int vertices = imgui.Frame(ImGuiPanelHost.DrawAll);
                Assert.True(vertices > 100, $"indoor frame {i}: only {vertices} vertices");
            }

            Assert.True(UiSources.Radar.Current?.Value?.Live.IsIndoor);
            Assert.Empty(_log);
        }
        finally { RynthCore.Engine.UI.PanelRouter.Toggle(DungeonMapFace.Title); }
    }

    [Fact]
    public void TheMapHidesOutdoorsAndStaysOpen()
    {
        using var imgui = new HeadlessImGui();
        LoginLifecycleHooks.HasObservedLoginComplete = true;
        AcDream.Rynth.EngineUi.RynthAiDashboard.Start();
        UseTestExport();
        SetJson(OutdoorJson);

        RynthCore.Engine.UI.PanelRouter.Toggle(DungeonMapFace.Title);
        try
        {
            int vertices = 0;
            for (int i = 0; i < 3; i++)
            {
                AcDream.Rynth.EngineUi.RynthAiDashboard.Tick();
                vertices = imgui.Frame(ImGuiPanelHost.DrawAll);
            }

            Assert.False(UiSources.Radar.Current?.Value?.Live.IsIndoor);
            Assert.True(ImGuiPanelHost.IsOpen(DungeonMapFace.Title));
            Assert.Equal(0, vertices);
        }
        finally { RynthCore.Engine.UI.PanelRouter.Toggle(DungeonMapFace.Title); }
    }

    [Fact]
    public void TheAtlasIsInstalledWhenMissingAndNamesTheDungeon()
    {
        string core = Path.Combine(_sandbox.Root, "atlas-core");
        var log = new List<string>();

        Assert.True(AcDream.Rynth.EngineUi.NavAtlasInstall.Ensure(core, isWindows: false, log.Add));

        string path = AcDream.Rynth.EngineUi.NavAtlasInstall.PathUnder(core);
        Assert.True(File.Exists(path));
        Assert.False(File.Exists(path + ".tmp"));
        Assert.Empty(log);
        // Their loader runs inline here (UiBackgroundWriter stand-in), so names are ready after the call.
        RynthNavAtlas.EnsureLoaded(path);
        Assert.Equal("Shreth Hive", RynthNavAtlas.DungeonLabel(0x0153));
        Assert.Equal("LB FFFF", RynthNavAtlas.DungeonLabel(0xFFFF));
    }

    [Fact]
    public void AMatchingAtlasIsLeftAlone()
    {
        string core = Path.Combine(_sandbox.Root, "atlas-same");
        AcDream.Rynth.EngineUi.NavAtlasInstall.Ensure(core, isWindows: false, _ => { });
        string path = AcDream.Rynth.EngineUi.NavAtlasInstall.PathUnder(core);
        var stamp = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(path, stamp);

        Assert.True(AcDream.Rynth.EngineUi.NavAtlasInstall.Ensure(core, isWindows: false, _ => { }));

        Assert.Equal(stamp, File.GetLastWriteTimeUtc(path));
    }

    [Fact]
    public void ADifferentAtlasIsReplacedOffWindowsOnly()
    {
        string core = Path.Combine(_sandbox.Root, "atlas-diff");
        string path = AcDream.Rynth.EngineUi.NavAtlasInstall.PathUnder(core);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "{\"locations\":[]}");

        Assert.True(AcDream.Rynth.EngineUi.NavAtlasInstall.Ensure(core, isWindows: true, _ => { }));
        Assert.Equal("{\"locations\":[]}", File.ReadAllText(path));

        Assert.True(AcDream.Rynth.EngineUi.NavAtlasInstall.Ensure(core, isWindows: false, _ => { }));
        Assert.True(new FileInfo(path).Length > 1_000_000);
    }

    [Fact]
    public void AFolderManagedByThePinnedArchiveIsLeftAlone()
    {
        string core = Path.Combine(_sandbox.Root, "atlas-managed");
        string path = AcDream.Rynth.EngineUi.NavAtlasInstall.PathUnder(core);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(Path.Combine(Path.GetDirectoryName(path)!, "navdata.id"), "abc");
        File.WriteAllText(path, "{\"locations\":[]}");

        Assert.True(AcDream.Rynth.EngineUi.NavAtlasInstall.Ensure(core, isWindows: false, _ => { }));

        Assert.Equal("{\"locations\":[]}", File.ReadAllText(path));
    }

    [Fact]
    public void AnUnwritableFolderIsLoggedNotThrown()
    {
        // A file where the RynthCore folder should be: creating NavData under it fails.
        string core = Path.Combine(_sandbox.Root, "atlas-blocked");
        File.WriteAllText(core, "not a folder");
        var log = new List<string>();

        Assert.False(AcDream.Rynth.EngineUi.NavAtlasInstall.Ensure(core, isWindows: false, log.Add));

        string line = Assert.Single(log);
        Assert.Contains(AcDream.Rynth.EngineUi.NavAtlasInstall.PathUnder(core), line, StringComparison.Ordinal);
    }

    [Fact]
    public void StartInstallsTheAtlas()
    {
        AcDream.Rynth.EngineUi.RynthAiDashboard.Start();

        Assert.True(File.Exists(AcDream.Rynth.EngineUi.NavAtlasInstall.PathUnder(
            RynthCore.Install.RynthInstallPaths.CoreDir)));
    }

    [Fact]
    public void StartLoadsTheRadarSettingsFile()
    {
        string file = Path.Combine(_sandbox.Root, "radar_settings.txt");
        File.WriteAllText(file, "showGroundItems=1\n");
        string? previous = Environment.GetEnvironmentVariable("RYNTHCORE_RADAR_SETTINGS");
        bool previousItems = RadarSettingsStore.ShowGroundItems;
        var latch = typeof(RadarSettingsStore).GetField("_loaded", BindingFlags.NonPublic | BindingFlags.Static)!;
        try
        {
            Environment.SetEnvironmentVariable("RYNTHCORE_RADAR_SETTINGS", file);
            RadarSettingsStore.ShowGroundItems = false;
            latch.SetValue(null, false); // Load reads once per process: re-arm it for this test.

            AcDream.Rynth.EngineUi.RynthAiDashboard.Start();

            Assert.True(RadarSettingsStore.ShowKind(RadarKind.GroundItem));
        }
        finally
        {
            Environment.SetEnvironmentVariable("RYNTHCORE_RADAR_SETTINGS", previous);
            RadarSettingsStore.ShowGroundItems = previousItems;
        }
    }
}
