using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using AcDream.Rynth.EngineUi;
using AcDream.Rynth.Host;
using AcDream.TestSupport;
using RynthCore.PluginSdk;

namespace AcDream.Rynth.Host.Tests;

[Collection("Rynth")]
public sealed class RynthPumpEndToEndTests : IDisposable
{
    private readonly List<string> _log = new();
    private readonly RynthConfigSandbox _sandbox = new("rynth-pump-");
    private readonly FakeGameView _view = new() { PlayerId = 0x50000003u };

    public void Dispose()
    {
        RynthAiDashboard.Stop();
        RynthCore.Engine.ImGuiBackend.ImGuiPanelHost.Reset();
        RynthCore.Engine.ImGuiBackend.ImGuiPanelHost.Log = null;
        RynthCore.Engine.Compatibility.LoginLifecycleHooks.HasObservedLoginComplete = false;
        RynthFrameThread.Reset();
        _sandbox.Dispose();
    }

    /// <summary>Loaded after the real plugins: records the thread its tick ran on.</summary>
    private static unsafe class TickProbe
    {
        public static int Ticks;
        public static int Thread;
        public static RynthPlugin? Dispatching;

        [UnmanagedCallersOnly(EntryPoint = "RynthPluginInit", CallConvs = new[] { typeof(CallConvCdecl) })]
        private static int Init(RynthCoreApiNative* api) => 0;

        [UnmanagedCallersOnly(EntryPoint = "RynthPluginTick", CallConvs = new[] { typeof(CallConvCdecl) })]
        private static void Tick()
        {
            Ticks++;
            Thread = Environment.CurrentManagedThreadId;
            Dispatching = RynthDispatch.Current;
        }
    }

    [Fact]
    public void TheRealPluginsTickOnThePumpThread()
    {
        TickProbe.Ticks = 0;
        var routed = new ThreadRoutedGameView(_view, () => _view);
        using var host = new RynthHost(
            routed,
            line => { lock (_log) _log.Add(line); },
            [
                typeof(RynthCore.Plugin.RynthTracker.PluginExports),
                typeof(RynthCore.Plugin.RynthOracle.PluginExports),
                typeof(RynthCore.Plugin.RynthInventory.PluginExports),
                typeof(RynthCore.Plugin.RynthChat.PluginExports),
                typeof(RynthCore.Plugin.RynthNav.PluginExports),
                typeof(RynthCore.Plugin.RynthAi.PluginExports),
                typeof(RynthCore.Plugin.RynthLua.PluginExports),
                typeof(TickProbe),
            ]);
        Assert.Contains(host.Plugins, p => p.ReportedName == "RynthNav");

        RynthFrameThread.Latch();
        int testThread = Environment.CurrentManagedThreadId;
        var passThreads = new List<int>();
        var scheduler = new ManualPumpScheduler();
        var actions = new RynthActionQueue(line => { lock (_log) _log.Add(line); }, () => false);
        host.Table.Actions = actions;
        using var pump = new RynthPump(
            pass: () =>
            {
                passThreads.Add(Environment.CurrentManagedThreadId);
                host.PumpPass();
                RynthEngineHub.PumpTick();
            },
            publish: () => { },
            actions: actions,
            log: line => { lock (_log) _log.Add(line); },
            notify: _ => { },
            scheduler: scheduler);
        pump.Start();

        for (int i = 0; i < 2; i++)
        {
            host.FrameTick();
            pump.FrameTick();
            RynthEngineHub.FrameTick();
            scheduler.RunPending();
            pump.AdvanceClockForTests(RynthPump.IntervalMs + 1);
        }

        Assert.Equal(2, passThreads.Count);
        Assert.All(passThreads, id => Assert.NotEqual(testThread, id));
        Assert.Equal(2, TickProbe.Ticks);
        Assert.Equal(passThreads[^1], TickProbe.Thread);
        Assert.Same(host.Plugins[^1], TickProbe.Dispatching);
        Assert.False(pump.Stopped);
        lock (_log)
            Assert.DoesNotContain(_log, line => line.Contains("failed", StringComparison.OrdinalIgnoreCase));
    }

    // RynthCore reads the Skills panel's data on AC's main thread (MainThreadSnapshots.Tick); with
    // the pump on, the dashboard's hub part must read it on the frame, not in the pump's pass.
    [Fact]
    public void TheSkillsDataIsPublishedWithThePumpOn()
    {
        int level = 4000 + Environment.TickCount % 1000;
        _view.Progress = new RynthPlayerProgress(
            [new RynthSkillProgress(6, 3, 10, 5000, 10, true, 140, 120)],
            [new RynthStatProgress(1, 20, 100, 900, 130, 120)],
            [new RynthStatProgress(1, 4, 0, 300, 80, 70)],
            level, 7, 1_000_000, 2500, 40, 500, 1f);
        var routed = new ThreadRoutedGameView(_view, () => _view);
        using var host = new RynthHost(routed, line => { lock (_log) _log.Add(line); }, []);
        InventoryModelReset.Run();
        using var hookup = new RynthChatHookup(host, routed, line => { lock (_log) _log.Add(line); });
        using var inventory = new RynthInventoryHookup(routed, null, line => { lock (_log) _log.Add(line); });
        RynthCore.Engine.Compatibility.LoginLifecycleHooks.HasObservedLoginComplete = true;
        RynthAiDashboard.Start();

        RynthFrameThread.Latch();
        var scheduler = new ManualPumpScheduler();
        var actions = new RynthActionQueue(line => { lock (_log) _log.Add(line); }, () => false);
        using var pump = new RynthPump(
            pass: () =>
            {
                host.PumpPass();
                RynthEngineHub.PumpTick();
            },
            publish: () => { },
            actions: actions,
            log: line => { lock (_log) _log.Add(line); },
            notify: _ => { },
            scheduler: scheduler);
        pump.Start();

        for (int i = 0; i < 3; i++)
        {
            RynthCore.Engine.Compatibility.PlayerProgressHooks.Want();
            RynthCore.Engine.Compatibility.PlayerProgressHooks.RefreshSoon();
            host.FrameTick();
            pump.FrameTick();
            RynthEngineHub.FrameTick();
            scheduler.RunPending();
            pump.AdvanceClockForTests(RynthPump.IntervalMs + 1);
        }

        Assert.Equal(level, RynthCore.Engine.Compatibility.PlayerProgressHooks.Current?.Level);
    }

    private static unsafe class ShutdownProbe
    {
        public static int Shutdowns;

        [UnmanagedCallersOnly(EntryPoint = "RynthPluginInit", CallConvs = new[] { typeof(CallConvCdecl) })]
        private static int Init(RynthCoreApiNative* api) => 0;

        [UnmanagedCallersOnly(EntryPoint = "RynthPluginShutdown", CallConvs = new[] { typeof(CallConvCdecl) })]
        private static void Shutdown() => Shutdowns++;
    }

    // A pass may still be inside plugin code: no Shutdown under it, and the table it reads stays.
    [Fact]
    public unsafe void AHostDisposedUnderALivePassLeavesThePluginsAndTheTable()
    {
        ShutdownProbe.Shutdowns = 0;
        var host = new RynthHost(_view, line => { lock (_log) _log.Add(line); }, [typeof(ShutdownProbe)]);
        Assert.Single(host.Plugins);
        host.Dispose(pumpExited: false);
        Assert.Equal(0, ShutdownProbe.Shutdowns);
        Assert.True(host.Api != null);
        lock (_log)
            Assert.Contains(_log, line => line.Contains("left in place", StringComparison.Ordinal));

        var clean = new RynthHost(_view, line => { lock (_log) _log.Add(line); }, [typeof(ShutdownProbe)]);
        clean.Dispose();
        Assert.Equal(1, ShutdownProbe.Shutdowns);
        Assert.True(clean.Api == null);
    }
}
