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

    private readonly Xunit.Abstractions.ITestOutputHelper _output;

    public RynthNavPluginTests(Xunit.Abstractions.ITestOutputHelper output)
    {
        _output = output;
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

    // RynthNav remembers the last @ or / line it was offered; a recall announced within five
    // seconds ("Name is recalling home.") followed by a teleport learns that line as the
    // recall's command, which recalls.txt then carries (the default would be "@house recall").
    [Fact]
    public void ATypedRecallCommandIsLearnedFromTheRecallAnnouncement()
    {
        _view.Names[_view.PlayerId] = "Tester";
        _view.WorldName = "TestWorld";
        using RynthHost host = CreateHost();
        RynthPlugin nav = Assert.Single(host.Plugins, p => p.ReportedName == "RynthNav");

        // The stores load off-thread; the recall can only be remembered once they are in.
        _view.Pose = (0xA9B40021u, new System.Numerics.Vector3(100, 100, 50), System.Numerics.Quaternion.Identity);
        for (int i = 0; i < 20; i++)
        {
            host.Tick();
            System.Threading.Thread.Sleep(25);
        }

        Assert.False(nav.OnChatBarEnter("/hr"));   // not eaten: it is still sent
        nav.OnChatWindowText("Tester is recalling home.", 0);
        host.Tick();
        _view.Pose = (0xACB40021u, new System.Numerics.Vector3(100, 100, 50), System.Numerics.Quaternion.Identity);
        host.Tick();
        nav.OnLogout();   // saves recalls.txt now

        // The save is off-thread; wait for the line to land in the shared NavData folder.
        string file = Path.Combine(TestNavData.Directory, "recalls.txt");
        string[] lines = [];
        for (int i = 0; i < 100 && !lines.Any(l => l.StartsWith("TestWorld|Tester\t", StringComparison.Ordinal)); i++)
        {
            System.Threading.Thread.Sleep(50);
            try { lines = File.Exists(file) ? File.ReadAllLines(file) : []; }
            catch (IOException) { }
        }

        string[] line = lines.Single(l => l.StartsWith("TestWorld|Tester\t", StringComparison.Ordinal)).Split('\t');
        Assert.Equal("House", line[1]);
        Assert.Equal("/hr", line[5]);
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

    private const int Ok = 1, NoPath = 0, NoMesh = -1;

    // Two vertices of Holtburg's own tile, far apart: both on the mesh by construction. Tile
    // vertices are world units, x east, y up, z north (DotRecast's frame); RynthNav takes
    // east, north, height.
    private static ((double E, double N, double H) Start, (double E, double N, double H) Goal) TwoMeshPoints()
    {
        string path = Path.Combine(TestNavData.Directory, $"nav_{TestNavData.HoltburgLandblock:X4}.tile");
        using var reader = new BinaryReader(File.OpenRead(path));
        DotRecast.Detour.DtMeshData mesh = new DotRecast.Detour.Io.DtMeshDataReader().Read(reader, 6);
        float[] v = mesh.verts;
        (double, double, double) Vertex(int i) => (v[i * 3], v[i * 3 + 2], v[i * 3 + 1]);
        var start = Vertex(0);
        int best = 0;
        double bestDistance = 0;
        for (int i = 1; i < mesh.header.vertCount; i++)
        {
            var p = Vertex(i);
            double d = Math.Sqrt((p.Item1 - start.Item1) * (p.Item1 - start.Item1) + (p.Item2 - start.Item2) * (p.Item2 - start.Item2));
            if (d > bestDistance && d < 120)
            {
                bestDistance = d;
                best = i;
            }
        }

        return (start, Vertex(best));
    }

    private static int FindPath(RynthHost host, (double E, double N, double H) start, (double E, double N, double H) goal, out double[] corners)
    {
        PathTable table = *(PathTable*)Interface(host, "RynthNav", "RynthNav.Path", 1);
        double[] points = new double[128 * 3];
        int count = 0;
        int result;
        fixed (double* p = points)
            result = ((delegate* unmanaged[Cdecl]<double, double, double, double, double, double, double*, int, int*, int>)table.FindPath)(
                start.E, start.N, start.H, goal.E, goal.N, goal.H, p, 128, &count);
        corners = points[..(count * 3)];
        return result;
    }

    private void StandAt((double E, double N, double H) point)
    {
        uint lbX = (uint)(point.E / 192.0), lbY = (uint)(point.N / 192.0);
        float x = (float)(point.E - lbX * 192.0), y = (float)(point.N - lbY * 192.0);
        uint cell = (lbX << 24) | (lbY << 16) | (uint)((int)(x / 24) * 8 + (int)(y / 24) + 1);
        _view.Pose = (cell, new System.Numerics.Vector3(x, y, (float)point.H), System.Numerics.Quaternion.Identity);
    }

    // xunit 2 (this project) has no dynamic skip; the note shows in the test's output, as the
    // dat-backed tests do (RynthAiInteropTests.GeometryLoaderOpensTheInstalledDatsThroughTheStandIn).
    private bool NoTiles()
    {
        if (TestNavData.HasTiles)
            return false;
        _output.WriteLine("skipped: no RynthNav tiles installed (tools/RynthNavData install)");
        return true;
    }

    [Fact]
    public void RynthNavPlansAPathOverTheInstalledTiles()
    {
        if (NoTiles())
            return;
        using RynthHost host = CreateHost();
        var (start, goal) = TwoMeshPoints();
        Assert.True(Math.Sqrt((goal.E - start.E) * (goal.E - start.E) + (goal.N - start.N) * (goal.N - start.N)) >= 40);
        StandAt(start);
        host.Tick();

        int result = FindPath(host, start, goal, out double[] corners);

        Assert.Equal(Ok, result);
        Assert.True(corners.Length >= 6);
        Assert.True(Math.Abs(corners[0] - start.E) < 1 && Math.Abs(corners[1] - start.N) < 1);
        Assert.True(Math.Abs(corners[^3] - goal.E) < 6 && Math.Abs(corners[^2] - goal.N) < 6);
        Assert.DoesNotContain(_log, line => line.Contains("not added", StringComparison.Ordinal) || line.Contains("failed to load", StringComparison.Ordinal));
    }

    [Fact]
    public void RynthNavHasNoMeshBeforeAPoseAndNoPathTooFarAway()
    {
        if (NoTiles())
            return;
        using RynthHost host = CreateHost();
        var (start, goal) = TwoMeshPoints();

        Assert.Equal(NoMesh, FindPath(host, start, goal, out _));

        StandAt(start);
        host.Tick();
        Assert.Equal(NoPath, FindPath(host, start, (start.E + 10 * 192, start.N + 10 * 192, start.H), out _));
    }
}
