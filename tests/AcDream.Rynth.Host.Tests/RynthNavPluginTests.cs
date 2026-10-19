using System.Runtime.InteropServices;
using AcDream.Rynth.Host;
using AcDream.TestSupport;
using RynthCore.Engine.Compatibility;
using RynthCore.Engine.ImGuiBackend;
using RynthCore.Engine.UI.Data;

namespace AcDream.Rynth.Host.Tests;

public sealed unsafe class RynthNavPluginTests : IDisposable
{
    private readonly List<string> _log = new();
    private readonly RynthConfigSandbox _sandbox = new("rynth-nav-");
    private readonly FakeGameView _view = new() { PlayerId = 0x50000002u };

    public RynthNavPluginTests()
    {
        ImGuiPanelHost.Reset();
        ImGuiPanelHost.Log = _log.Add;
        UiDataHub.Clear();
    }

    public void Dispose()
    {
        ImGuiPanelHost.Reset();
        ImGuiPanelHost.Log = null;
        LoginLifecycleHooks.HasObservedLoginComplete = false;
        _sandbox.Dispose();
    }

    // RynthAi's own types are in two assemblies (RynthAi and RynthNav both compile
    // Shared/RynthNavPathApi.cs), so the test reads the table through its own mirror.
    [StructLayout(LayoutKind.Sequential)]
    private struct PathTable
    {
        public uint Version;
        public uint StructSize;
        public IntPtr FindPath;
    }

    private RynthHost CreateHost() => new(
        _view,
        _log.Add,
        [
            typeof(RynthCore.Plugin.RynthNav.PluginExports),
            typeof(RynthCore.Plugin.RynthAi.PluginExports),
            typeof(RynthCore.Plugin.RynthLua.PluginExports),
        ]);

    internal static IntPtr Interface(RynthHost host, string plugin, string iface, uint version)
    {
        IntPtr name = Marshal.StringToHGlobalAnsi(plugin);
        IntPtr ifaceName = Marshal.StringToHGlobalAnsi(iface);
        try
        {
            return ((delegate* unmanaged[Cdecl]<IntPtr, IntPtr, uint, IntPtr>)host.Api->GetPluginInterfaceFn)(name, ifaceName, version);
        }
        finally
        {
            Marshal.FreeHGlobal(name);
            Marshal.FreeHGlobal(ifaceName);
        }
    }

    [Fact]
    public void RynthNavLoadsAndTicksBesideRynthAiAndRynthLua()
    {
        using RynthHost host = CreateHost();
        for (int i = 0; i < 30; i++)
            host.Tick();

        RynthPlugin nav = Assert.Single(host.Plugins, p => p.ReportedName == "RynthNav");
        Assert.True(nav.IsActive);
        Assert.Equal(3, host.Plugins.Count(p => p.IsActive));
        Assert.DoesNotContain(_log, line => line.Contains("Exception", StringComparison.Ordinal));
    }

    [Fact]
    public void RynthNavReadsItsTilesFromTheHostsFolder()
    {
        using RynthHost host = CreateHost();
        Assert.Contains(_log, line => line.Contains($"Tiles: {TestNavData.Directory} (RYNTHNAV_NAVDATA)", StringComparison.Ordinal));
    }

    [Fact]
    public void RynthAiCanFetchRynthNavsPathTable()
    {
        using RynthHost host = CreateHost();
        IntPtr table = Interface(host, "RynthNav", "RynthNav.Path", 1);

        Assert.NotEqual(IntPtr.Zero, table);
        PathTable read = *(PathTable*)table;
        Assert.Equal(1u, read.Version);
        Assert.Equal(16u, read.StructSize);
        Assert.NotEqual(IntPtr.Zero, read.FindPath);
        Assert.Equal(IntPtr.Zero, Interface(host, "RynthNav", "RynthNav.Path", 2));
    }

    [Fact]
    public void RynthLuaCanFetchRynthAisScriptTable()
    {
        using RynthHost host = CreateHost();
        Assert.NotEqual(IntPtr.Zero, Interface(host, "RynthAi", "RynthAi.Script", 1));
    }

    [Fact]
    public void TheNavDataFolderGoesUnderTheCoreFolderOffWindows()
    {
        var env = new Dictionary<string, string?>();
        RynthInstallFolders.Apply(k => env.GetValueOrDefault(k), (k, v) => env[k] = v, isWindows: false, "/home/x/.config");
        Assert.Equal(Path.Combine("/home/x/.config", "RynthCore", "NavData"), env[RynthInstallFolders.NavDataVariable]);

        env.Clear();
        env[RynthInstallFolders.NavDataVariable] = "/tiles";
        RynthInstallFolders.Apply(k => env.GetValueOrDefault(k), (k, v) => env[k] = v, isWindows: false, "/home/x/.config");
        Assert.Equal("/tiles", env[RynthInstallFolders.NavDataVariable]);

        env.Clear();
        RynthInstallFolders.Apply(k => env.GetValueOrDefault(k), (k, v) => env[k] = v, isWindows: true, @"C:\Users\x\AppData\Roaming");
        Assert.False(env.ContainsKey(RynthInstallFolders.NavDataVariable));
    }

    [Fact]
    public void TheHintAppearsOnlyWithoutTiles()
    {
        string empty = Directory.CreateTempSubdirectory("rynth-nav-hint-").FullName;
        string tiled = Directory.CreateTempSubdirectory("rynth-nav-hint-").FullName;
        try
        {
            File.WriteAllBytes(Path.Combine(tiled, "nav_A9B4.tile"), [1]);
            Assert.Null(RynthNavDataHint.For(null));
            Assert.Null(RynthNavDataHint.For(" "));
            Assert.Null(RynthNavDataHint.For(tiled));
            Assert.Contains(empty, RynthNavDataHint.For(empty), StringComparison.Ordinal);
            Assert.Contains(RynthNavDataHint.InstallCommand, RynthNavDataHint.For(Path.Combine(empty, "missing")), StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(empty, true);
            Directory.Delete(tiled, true);
        }
    }
}
