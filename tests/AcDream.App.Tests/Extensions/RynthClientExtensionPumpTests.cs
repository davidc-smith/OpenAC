using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using AcDream.ClientExtensions;
using AcDream.Rynth.EngineUi;
using AcDream.Rynth.Host;
using AcDream.TestSupport;
using RynthCore.PluginSdk;

namespace AcDream.App.Tests.Extensions;

[Collection("Rynth")]
public sealed class RynthClientExtensionPumpTests : IDisposable
{
    private readonly RynthConfigSandbox _sandbox = new("rynth-pump-ext-");

    public RynthClientExtensionPumpTests() => PassProbe.Reset();

    public void Dispose()
    {
        RynthFrameThread.Reset();
        RynthFrameActions.Queue = null;
        _sandbox.Dispose();
    }

    private sealed class Registration : IDisposable
    {
        public void Dispose()
        {
        }
    }

    private static unsafe class PassProbe
    {
        public static volatile int SleepMs;
        public static volatile bool InTick;
        public static int Ticks;
        public static int TickThread;
        public static bool LogoutDuringTick;
        public static int Logouts;
        public static int Shutdowns;

        public static void Reset()
        {
            Shutdowns = 0;
            SleepMs = 0;
            InTick = false;
            Ticks = 0;
            TickThread = 0;
            LogoutDuringTick = false;
            Logouts = 0;
        }

        [UnmanagedCallersOnly(EntryPoint = "RynthPluginInit", CallConvs = new[] { typeof(CallConvCdecl) })]
        private static int Init(RynthCoreApiNative* api) => 0;

        [UnmanagedCallersOnly(EntryPoint = "RynthPluginTick", CallConvs = new[] { typeof(CallConvCdecl) })]
        private static void Tick()
        {
            InTick = true;
            Interlocked.Increment(ref Ticks);
            TickThread = Environment.CurrentManagedThreadId;
            if (SleepMs > 0)
                Thread.Sleep(SleepMs);
            InTick = false;
        }

        [UnmanagedCallersOnly(EntryPoint = "RynthPluginShutdown", CallConvs = new[] { typeof(CallConvCdecl) })]
        private static void Shutdown() => Shutdowns++;

        [UnmanagedCallersOnly(EntryPoint = "RynthPluginOnLogout", CallConvs = new[] { typeof(CallConvCdecl) })]
        private static void Logout()
        {
            Logouts++;
            LogoutDuringTick |= InTick;
        }
    }

    private static ClientExtensionContext Context(List<string> log) => new()
    {
        Runtime = GameRuntimeTestFactory.Create(),
        Log = line => { lock (log) log.Add(line); },
        RegisterChatCommand = (_, _) => new Registration(),
    };

    [Fact]
    public void WithThePumpOffNothingLatchesAndPluginsTickInline()
    {
        var log = new List<string>();
        using var extension = new RynthClientExtension { AdditionalPluginExports = [typeof(PassProbe)] };
        extension.Attach(Context(log));

        Assert.Null(extension.Pump);
        Assert.False(RynthFrameThread.Latched);
        Assert.Null(RynthFrameActions.Queue);
        extension.Tick(1.0 / 60);
        Assert.Equal(1, PassProbe.Ticks);
        Assert.Equal(Environment.CurrentManagedThreadId, PassProbe.TickThread);
    }

    [Fact]
    public void WithThePumpOnPluginsTickOnThePumpAndDisposeUnlatches()
    {
        var log = new List<string>();
        var scheduler = new ManualPumpScheduler();
        var extension = new RynthClientExtension
        {
            AdditionalPluginExports = [typeof(PassProbe)],
            PumpEnabled = true,
            PumpScheduler = () => scheduler,
        };
        extension.Attach(Context(log));

        Assert.NotNull(extension.Pump);
        Assert.True(RynthFrameThread.IsCurrent);
        Assert.NotNull(RynthFrameActions.Queue);
        extension.Tick(1.0 / 60);
        Assert.Equal(0, PassProbe.Ticks);  // the pass waits for the scheduler
        scheduler.RunPending();
        Assert.Equal(1, PassProbe.Ticks);
        Assert.NotEqual(Environment.CurrentManagedThreadId, PassProbe.TickThread);

        extension.Dispose();
        Assert.False(RynthFrameThread.Latched);
        Assert.Null(RynthFrameActions.Queue);
        lock (log)
            Assert.DoesNotContain(log, line => line.Contains("failed", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void LogoutWaitsForTheRunningPass()
    {
        var log = new List<string>();
        using var extension = new RynthClientExtension
        {
            AdditionalPluginExports = [typeof(PassProbe)],
            PumpEnabled = true,
        };
        extension.Attach(Context(log));
        PassProbe.SleepMs = 300;
        extension.Tick(1.0 / 60);
        var started = System.Diagnostics.Stopwatch.StartNew();
        while (!PassProbe.InTick && started.ElapsedMilliseconds < 2000)
            Thread.Sleep(1);
        Assert.True(PassProbe.InTick);

        extension.Host!.Bridge.OnLeavingWorld();

        Assert.Equal(1, PassProbe.Logouts);
        Assert.False(PassProbe.LogoutDuringTick);
        Assert.False(extension.Pump!.Stopped);
    }

    // Only a failing PumpPass counts toward the 50-failure stop: a hub step that throws every pass
    // (a bad manifest, a stand-in not wired) is logged, rate-limited, and the plugins keep ticking.
    [Fact]
    public void AFaultingHubStepNeitherStopsThePumpNorLogsEveryPass()
    {
        var log = new List<string>();
        var scheduler = new ManualPumpScheduler();
        using var extension = new RynthClientExtension
        {
            AdditionalPluginExports = [typeof(PassProbe)],
            PumpEnabled = true,
            PumpScheduler = () => scheduler,
        };
        extension.Attach(Context(log));
        RynthEngineHub.Join(new RynthEngineHub.Part("Faulty", [], BeforeDrain: () => throw new InvalidOperationException("hub boom")));
        try
        {
            for (int i = 0; i < 60; i++)
            {
                extension.Tick(1.0 / 60);
                scheduler.RunPending();
                extension.Pump!.AdvanceClockForTests(RynthPump.IntervalMs + 1);
            }

            Assert.False(extension.Pump!.Stopped);
            Assert.Equal(60, PassProbe.Ticks);
            lock (log)
                Assert.Single(log, line => line.Contains("hub boom", StringComparison.Ordinal));
        }
        finally { RynthEngineHub.Leave("Faulty"); }
    }

    // RynthCore's plugins hear of an object only once it is readable: a pass delivers the events
    // queued before its snapshot was built, and those queued after wait for the next snapshot.
    [Fact]
    public void APassDeliversOnlyTheEventsQueuedBeforeItsSnapshot()
    {
        var log = new List<string>();
        var scheduler = new ManualPumpScheduler();
        using var extension = new RynthClientExtension
        {
            AdditionalPluginExports = [typeof(PassProbe)],
            PumpEnabled = true,
            PumpScheduler = () => scheduler,
        };
        extension.Attach(Context(log));
        var delivered = new List<string>();
        RynthEventBridge bridge = extension.Host!.Bridge;

        bridge.Enqueue(RynthEventBridge.EventKind.CreateObject, () => delivered.Add("before"));
        extension.Tick(1.0 / 60);   // the snapshot, and a pass woken
        bridge.Enqueue(RynthEventBridge.EventKind.CreateObject, () => delivered.Add("after"));   // the next frame, before the pass starts
        scheduler.RunPending();
        Assert.Equal(["before"], delivered);

        extension.Pump!.AdvanceClockForTests(RynthPump.IntervalMs + 1);
        extension.Tick(1.0 / 60);
        scheduler.RunPending();
        Assert.Equal(["before", "after"], delivered);
    }

    // A pass still inside plugin code when Dispose gives up: no Shutdown and no freed table under it,
    // and the frame is not held for a second long join.
    [Fact]
    public void DisposeUnderAnOverrunningPassLeavesThePluginsAndDoesNotStall()
    {
        var log = new List<string>();
        var extension = new RynthClientExtension
        {
            AdditionalPluginExports = [typeof(PassProbe)],
            PumpEnabled = true,
            PumpShutdownTimeout = TimeSpan.FromMilliseconds(50),
        };
        extension.Attach(Context(log));
        PassProbe.SleepMs = 800;
        extension.Tick(1.0 / 60);
        var started = System.Diagnostics.Stopwatch.StartNew();
        while (!PassProbe.InTick && started.ElapsedMilliseconds < 2000)
            Thread.Sleep(1);
        Assert.True(PassProbe.InTick);

        var t = System.Diagnostics.Stopwatch.StartNew();
        extension.Dispose();
        long took = t.ElapsedMilliseconds;
        bool inTick = PassProbe.InTick;
        started.Restart();
        while (PassProbe.InTick && started.ElapsedMilliseconds < 3000)
            Thread.Sleep(5);
        Thread.Sleep(50);

        Assert.True(inTick);
        Assert.True(took < 500, $"dispose took {took} ms");
        Assert.Equal(0, PassProbe.Shutdowns);
        lock (log)
            Assert.Contains(log, line => line.Contains("did not exit", StringComparison.Ordinal));
    }
}
