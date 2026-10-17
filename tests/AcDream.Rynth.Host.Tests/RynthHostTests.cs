using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using AcDream.Runtime;
using AcDream.Rynth.Host;
using RynthCore.PluginSdk;

namespace AcDream.Rynth.Host.Tests;

public sealed class RynthHostTests
{
    private const uint TotalExperience = 1;

    private readonly List<string> _log = new();

    // RynthTracker saves its session on shutdown and restores it on the next login of
    // the same character in the same process, so every test plays a different character.
    private readonly FakeGameView _view = new()
    {
        PlayerId = 0x50000000u | (uint)Random.Shared.Next(1, 0xFFFF),
    };

    private RynthHost CreateTrackerHost() =>
        new(_view, _log.Add, [typeof(RynthCore.Plugin.RynthTracker.PluginExports)]);

    [Fact]
    public void Start_InitialisesRynthTracker()
    {
        using RynthHost host = CreateTrackerHost();

        RynthPlugin plugin = Assert.Single(host.Plugins);
        Assert.True(plugin.IsActive);
        Assert.Contains(_log, line => line.Contains("[RynthTracker] Plugin initialized.", StringComparison.Ordinal));
    }

    [Fact]
    public void TrackerCountsAKillAndTheXpThatFollows()
    {
        using RynthHost host = CreateTrackerHost();
        _view.Int64s[(_view.PlayerId, TotalExperience)] = 1000;

        EnterWorld(host);
        TickHalfASecond(host);            // first poll: baseline 1000, nothing gained yet
        host.Bridge.OnKillLanded("You killed the drudge!");
        _view.Int64s[(_view.PlayerId, TotalExperience)] = 1600;
        TickHalfASecond(host);            // poll inside the 2.5 s kill window

        string json = host.Plugins[0].CallStringExport("RynthTrackerGetSnapshotJson")!;
        Assert.Contains("\"kt\":1", json, StringComparison.Ordinal);
        Assert.Contains("\"xt\":600", json, StringComparison.Ordinal);
    }

    [Fact]
    public void LeavingTheWorldEndsTheSession()
    {
        using RynthHost host = CreateTrackerHost();
        EnterWorld(host);
        host.Bridge.OnKillLanded("You killed the drudge!");

        host.Bridge.OnLeavingWorld();
        host.Bridge.OnKillLanded("You killed another drudge!");
        TickHalfASecond(host);

        string json = host.Plugins[0].CallStringExport("RynthTrackerGetSnapshotJson")!;
        Assert.Contains("\"kt\":1", json, StringComparison.Ordinal);
    }

    [Fact]
    public void APluginWithoutInitIsSkipped()
    {
        using var host = new RynthHost(
            _view,
            _log.Add,
            [typeof(NoExports), typeof(RynthCore.Plugin.RynthTracker.PluginExports)]);

        Assert.Single(host.Plugins);
        Assert.Contains(_log, line => line.Contains("NoExports", StringComparison.Ordinal)
            && line.Contains("no RynthPluginInit", StringComparison.Ordinal));
    }

    [Fact]
    public void APluginWhoseInitFailsIsSkippedAndTheOthersRun()
    {
        using var host = new RynthHost(
            _view,
            _log.Add,
            [typeof(FailingInit), typeof(RynthCore.Plugin.RynthTracker.PluginExports)]);

        RynthPlugin plugin = Assert.Single(host.Plugins);
        Assert.True(plugin.IsActive);
        Assert.Contains(_log, line => line.Contains("RynthPluginInit returned 1, skipped", StringComparison.Ordinal));
    }

    private static void EnterWorld(RynthHost host) =>
        host.Bridge.OnLifecycle(new RuntimeLifecycleDelta(
            default, RuntimeLifecycleState.Starting, RuntimeLifecycleState.InWorld));

    // RynthTracker rebuilds its snapshot and polls XP every 30th tick (0.5 s at 60 Hz).
    private static void TickHalfASecond(RynthHost host)
    {
        for (int i = 0; i < 30; i++)
            host.Tick();
    }

    [Fact]
    public void AFailedStartReleasesEverythingAndLeavesANewHostConstructible()
    {
        static IEnumerable<Type> TrackerThenThrow()
        {
            yield return typeof(RynthCore.Plugin.RynthTracker.PluginExports);
            throw new InvalidOperationException("discovery failed");
        }

        Assert.Throws<InvalidOperationException>(() => new RynthHost(_view, _log.Add, TrackerThenThrow()));

        Assert.Null(RynthCore.Engine.RynthLog.Sink);
        Assert.Null(RynthCore.Engine.ImGuiBackend.ImGuiPanelHost.Log);
        using RynthHost host = CreateTrackerHost();
        Assert.True(Assert.Single(host.Plugins).IsActive);
    }

    private static class NoExports
    {
    }

    private static unsafe class FailingInit
    {
        [UnmanagedCallersOnly(EntryPoint = "RynthPluginInit", CallConvs = new[] { typeof(CallConvCdecl) })]
        private static int Init(RynthCoreApiNative* api) => 1;
    }
}
