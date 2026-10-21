using System.Diagnostics;
using AcDream.Rynth.EngineUi;
using AcDream.Rynth.Host;

namespace AcDream.Rynth.Host.Tests;

[Collection("Rynth")]
public sealed class RynthPumpTests : IDisposable
{
    public void Dispose() => RynthFrameThread.Reset();

    private sealed class Probe
    {
        public int Passes;
        public int Snapshots;
        public int SleepMs;
        public bool Throw;
    }

    private static RynthPump NewPump(
        Probe probe, IRynthPumpScheduler scheduler, RynthActionQueue? queue = null, List<string>? log = null,
        List<string>? notices = null)
    {
        List<string> sink = log ?? [];
        List<string> said = notices ?? [];
        return new(
            pass: () =>
            {
                Interlocked.Increment(ref probe.Passes);
                if (probe.SleepMs > 0) Thread.Sleep(probe.SleepMs);
                if (probe.Throw) throw new InvalidOperationException("boom");
            },
            publish: () => Interlocked.Increment(ref probe.Snapshots),
            actions: queue ?? new RynthActionQueue(_ => { }, () => false),
            log: line => { lock (sink) sink.Add(line); },
            notify: said.Add,
            scheduler: scheduler);
    }

    [Fact]
    public void ASlowPassDoesNotHoldUpTheFrame()
    {
        RynthFrameThread.Latch();
        var probe = new Probe { SleepMs = 500 };
        using var pump = NewPump(probe, new ThreadPumpScheduler());
        pump.Start();
        var worst = TimeSpan.Zero;
        int frames = 0;
        var until = Stopwatch.StartNew();
        while (until.ElapsedMilliseconds < 1200)
        {
            var t = Stopwatch.StartNew();
            pump.FrameTick();
            worst = t.Elapsed > worst ? t.Elapsed : worst;
            frames++;
            Thread.Sleep(5);
        }
        Assert.True(frames >= 50, $"only {frames} frame ticks");
        Assert.True(worst < TimeSpan.FromMilliseconds(2), $"worst frame tick {worst.TotalMilliseconds} ms");
        Assert.InRange(probe.Passes, 2, 4);
    }

    [Fact]
    public void ActionsDrainBeforeTheSnapshotAndPassesWaitForOne()
    {
        RynthFrameThread.Latch();
        var probe = new Probe();
        var queue = new RynthActionQueue(_ => { }, () => false);
        var scheduler = new ManualPumpScheduler();
        using var pump = NewPump(probe, scheduler, queue);
        pump.Start();
        int order = 0, drainedAt = 0;
        queue.TryEnqueue(RynthActionKind.Start, "a", () => { drainedAt = ++order; return true; });
        pump.FrameTick();
        Assert.Equal(1, drainedAt);
        Assert.Equal(1, probe.Snapshots);
        scheduler.RunPending();
        Assert.Equal(1, probe.Passes);
        scheduler.RunPending();             // no new snapshot yet: nothing runs
        Assert.Equal(1, probe.Passes);
    }

    [Fact]
    public void PassesStartNoMoreOftenThanTheInterval()
    {
        RynthFrameThread.Latch();
        var probe = new Probe();
        var scheduler = new ManualPumpScheduler();
        using var pump = NewPump(probe, scheduler);
        pump.Start();
        pump.FrameTick();
        scheduler.RunPending();
        pump.FrameTick();                   // well inside 16 ms: no publish, no pass
        scheduler.RunPending();
        Assert.Equal(1, probe.Snapshots);
        Assert.Equal(1, probe.Passes);
        pump.AdvanceClockForTests(RynthPump.IntervalMs);
        pump.FrameTick();
        scheduler.RunPending();
        Assert.Equal(2, probe.Snapshots);
        Assert.Equal(2, probe.Passes);
    }

    [Fact]
    public void NoSnapshotIsBuiltWhileAPassRuns()
    {
        RynthFrameThread.Latch();
        var probe = new Probe { SleepMs = 300 };
        using var pump = NewPump(probe, new ThreadPumpScheduler());
        pump.Start();
        pump.FrameTick();
        Thread.Sleep(50);
        for (int i = 0; i < 10; i++) { pump.FrameTick(); Thread.Sleep(10); }
        Assert.Equal(1, probe.Snapshots);
    }

    [Fact]
    public void QuiesceWaitsForTheRunningPass()
    {
        RynthFrameThread.Latch();
        var probe = new Probe { SleepMs = 200 };
        using var pump = NewPump(probe, new ThreadPumpScheduler());
        pump.Start();
        pump.FrameTick();
        Thread.Sleep(20);
        var t = Stopwatch.StartNew();
        Assert.True(pump.Quiesce(TimeSpan.FromSeconds(5)));
        Assert.True(t.ElapsedMilliseconds >= 100);
        int passes = probe.Passes;
        for (int i = 0; i < 5; i++) { pump.FrameTick(); Thread.Sleep(20); }
        Assert.Equal(passes, probe.Passes);
        pump.Resume();
    }

    [Fact]
    public void AQuiesceThatOverrunsIsLogged()
    {
        RynthFrameThread.Latch();
        var probe = new Probe { SleepMs = 300 };
        var log = new List<string>();
        using var pump = NewPump(probe, new ThreadPumpScheduler(), log: log);
        pump.Start();
        pump.FrameTick();
        Thread.Sleep(20);
        Assert.False(pump.Quiesce(TimeSpan.FromMilliseconds(10)));
        lock (log)
            Assert.Contains(log, line => line.Contains("did not end", StringComparison.Ordinal));
    }

    [Fact]
    public void FiftyFailingPassesStopThePump()
    {
        RynthFrameThread.Latch();
        var probe = new Probe { Throw = true };
        var scheduler = new ManualPumpScheduler();
        var log = new List<string>();
        var notices = new List<string>();
        using var pump = NewPump(probe, scheduler, log: log, notices: notices);
        pump.Start();
        for (int i = 0; i < 60; i++) { pump.FrameTick(); scheduler.RunPending(); pump.AdvanceClockForTests(17); }
        Assert.Equal(50, probe.Passes);
        Assert.True(pump.Stopped);
        Assert.Single(notices);
    }

    [Fact]
    public void ASuccessfulPassResetsTheFailureCount()
    {
        RynthFrameThread.Latch();
        var probe = new Probe { Throw = true };
        var scheduler = new ManualPumpScheduler();
        using var pump = NewPump(probe, scheduler);
        pump.Start();
        void Run(int n)
        {
            for (int i = 0; i < n; i++) { pump.FrameTick(); scheduler.RunPending(); pump.AdvanceClockForTests(17); }
        }
        Run(49);
        probe.Throw = false;
        Run(1);
        probe.Throw = true;
        Run(49);
        Assert.False(pump.Stopped);
        Assert.Equal(99, probe.Passes);
    }

    [Fact]
    public void ASnapshotThatThrowsWakesNoPass()
    {
        RynthFrameThread.Latch();
        var scheduler = new ManualPumpScheduler();
        var log = new List<string>();
        int passes = 0;
        using var pump = new RynthPump(
            pass: () => passes++,
            publish: () => throw new InvalidOperationException("bad"),
            actions: new RynthActionQueue(_ => { }, () => false),
            log: log.Add,
            notify: _ => { },
            scheduler: scheduler);
        pump.Start();
        pump.FrameTick();
        scheduler.RunPending();
        Assert.Equal(0, passes);
        Assert.Contains(log, line => line.Contains("snapshot failed", StringComparison.Ordinal));
    }

    [Fact]
    public void DisposeEndsThePumpThread()
    {
        RynthFrameThread.Latch();
        var probe = new Probe();
        var scheduler = new ThreadPumpScheduler();
        var pump = NewPump(probe, scheduler);
        pump.Start();
        pump.FrameTick();
        pump.Dispose();
        Assert.False(scheduler.IsRunning);
    }

    [Fact]
    public void ASnapshotThatKeepsFailingIsLoggedOncePerTenSeconds()
    {
        RynthFrameThread.Latch();
        var scheduler = new ManualPumpScheduler();
        var log = new List<string>();
        using var pump = new RynthPump(
            pass: () => { },
            publish: () => throw new InvalidOperationException("bad"),
            actions: new RynthActionQueue(_ => { }, () => false),
            log: log.Add,
            notify: _ => { },
            scheduler: scheduler);
        pump.Start();
        for (int i = 0; i < 50; i++) { pump.FrameTick(); pump.AdvanceClockForTests(17); }
        Assert.Single(log, line => line.Contains("snapshot failed", StringComparison.Ordinal));
        pump.AdvanceClockForTests(10_000);
        pump.FrameTick();
        Assert.Equal(2, log.Count(line => line.Contains("snapshot failed", StringComparison.Ordinal)));
    }

    [Fact]
    public void ShutdownSaysTheThreadExited()
    {
        RynthFrameThread.Latch();
        var scheduler = new ThreadPumpScheduler();
        var pump = NewPump(new Probe(), scheduler);
        pump.Start();
        pump.FrameTick();
        Assert.True(pump.Shutdown(TimeSpan.FromSeconds(5)));
        Assert.False(scheduler.IsRunning);
    }

    // A pass that outlives the quiesce: Shutdown says the thread is still there, and does not then
    // wait out a second long join on the frame.
    [Fact]
    public void ShutdownUnderAnOverrunningPassSaysSoWithoutStalling()
    {
        RynthFrameThread.Latch();
        var probe = new Probe { SleepMs = 800 };
        var scheduler = new ThreadPumpScheduler();
        var pump = NewPump(probe, scheduler);
        pump.Start();
        pump.FrameTick();
        var started = Stopwatch.StartNew();
        while (Volatile.Read(ref probe.Passes) == 0 && started.ElapsedMilliseconds < 2000)
            Thread.Sleep(1);

        var t = Stopwatch.StartNew();
        Assert.False(pump.Shutdown(TimeSpan.FromMilliseconds(50)));
        Assert.True(t.ElapsedMilliseconds < 400, $"shutdown took {t.ElapsedMilliseconds} ms");

        started.Restart();
        while (scheduler.IsRunning && started.ElapsedMilliseconds < 3000)
            Thread.Sleep(10);
        Assert.False(scheduler.IsRunning);   // the pass ended and the thread left on its own
    }
}
