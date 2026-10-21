using System.Diagnostics;
using AcDream.Rynth.EngineUi;

namespace AcDream.Rynth.Host;

/// <summary>Where passes run: a real thread in the client, on demand in tests.</summary>
public interface IRynthPumpScheduler : IDisposable
{
    void Start(Action pass);

    /// <summary>A pass may start now (called by the frame thread after a publish).</summary>
    void Wake();
}

/// <summary>RynthCore's pump: one background thread, woken by the frame thread.</summary>
public sealed class ThreadPumpScheduler : IRynthPumpScheduler
{
    private readonly AutoResetEvent _wake = new(false);
    private Thread? _thread;
    private volatile bool _stop;

    /// <summary>True while the pump thread is alive.</summary>
    public bool IsRunning => _thread is { IsAlive: true };

    public void Start(Action pass)
    {
        _thread = new Thread(() =>
        {
            try
            {
                while (true)
                {
                    _wake.WaitOne();
                    if (_stop)
                        return;
                    pass();
                }
            }
            catch (ObjectDisposedException)
            {
                // Disposed under a pass that overran the join: nothing left to wake us.
            }
        }) { IsBackground = true, Name = "Rynth pump" };
        _thread.Start();
    }

    public void Wake()
    {
        if (!_stop)
            _wake.Set();
    }

    public void Dispose()
    {
        _stop = true;
        _wake.Set();
        // A pass still running past the join keeps the event; it exits on its next wait.
        if (_thread is null || _thread.Join(TimeSpan.FromSeconds(5)))
            _wake.Dispose();
    }
}

/// <summary>Tests: a woken pass runs on a fresh thread when <see cref="RunPending"/> is called, and is joined.</summary>
public sealed class ManualPumpScheduler : IRynthPumpScheduler
{
    private Action? _pass;
    private bool _woken;

    public void Start(Action pass) => _pass = pass;

    public void Wake() => _woken = true;

    public void RunPending()
    {
        if (!_woken || _pass is null)
            return;
        _woken = false;
        var thread = new Thread(() => _pass()) { Name = "Rynth pump (test)" };
        thread.Start();
        thread.Join();
    }

    public void Dispose()
    {
    }
}

/// <summary>
/// Runs the Rynth plugins' passes off the frame thread (RynthCore's StartNormalPluginPump).
/// The frame thread calls <see cref="FrameTick"/> each frame: it drains the queued actions,
/// and when the pump is idle, 16 ms have passed since the last pass started and it isn't
/// quiesced, publishes a snapshot and wakes a pass. While a pass runs no snapshot is built,
/// so a long pass costs the frame nothing; it only delays the next pass.
/// </summary>
public sealed class RynthPump(
    Action pass, Action publish, RynthActionQueue actions, Action<string> log, Action<string> notify,
    IRynthPumpScheduler scheduler) : IDisposable
{
    public const int IntervalMs = 16;
    private const int MaxFailures = 50;

    /// <summary>The chat line written when the pump stops after <see cref="MaxFailures"/> failing passes.</summary>
    internal const string StoppedNotice =
        "[Rynth] plugins stopped: 50 failing ticks in a row (see the log). Relog to restart them.";

    private readonly ManualResetEventSlim _idle = new(true);
    private long _lastPassStart = long.MinValue / 2;
    private long _clockOffsetMs;
    private int _failures;
    private volatile bool _paused;
    private volatile bool _stopped;
    private bool _notified;
    private bool _disposed;

    /// <summary>True once 50 passes in a row failed: no pass starts again on this pump.</summary>
    public bool Stopped => _stopped;

    public void Start() => scheduler.Start(RunPass);

    /// <summary>Frame thread, each frame: queued actions, then (when due) a snapshot and a pass.</summary>
    public void FrameTick()
    {
        actions.Drain();
        if (_stopped)
        {
            if (!_notified)
            {
                _notified = true;
                try { notify(StoppedNotice); }
                catch (Exception error) { log($"pump notice failed: {error.GetType().Name}: {error.Message}"); }
            }
            return;
        }

        if (_paused || !_idle.IsSet || NowMs() - _lastPassStart < IntervalMs)
            return;
        try
        {
            publish();
        }
        catch (Exception error)
        {
            // The previous snapshot stays; no pass without a fresh one.
            log($"snapshot failed: {error.GetType().Name}: {error.Message}");
            return;
        }

        _idle.Reset();
        _lastPassStart = NowMs();
        scheduler.Wake();
    }

    /// <summary>Stops scheduling passes and waits for the running one. False on timeout (logged).</summary>
    public bool Quiesce(TimeSpan timeout)
    {
        _paused = true;
        if (_idle.Wait(timeout))
            return true;
        log($"the pump pass did not end within {timeout.TotalSeconds:0.##} s; going on without it");
        return false;
    }

    /// <summary>Passes may start again (after <see cref="Quiesce"/>).</summary>
    public void Resume() => _paused = false;

    internal void AdvanceClockForTests(long ms) => _clockOffsetMs += ms;

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        bool idle = Quiesce(TimeSpan.FromSeconds(5));
        scheduler.Dispose();
        // A pass that overran still sets the event when it ends.
        if (idle)
            _idle.Dispose();
    }

    private void RunPass()
    {
        try
        {
            pass();
            _failures = 0;
        }
        catch (Exception error)
        {
            log($"pump pass failed: {error.GetType().Name}: {error.Message}");
            if (++_failures >= MaxFailures)
                _stopped = true;
        }
        finally
        {
            _idle.Set();
        }
    }

    private long NowMs() => (long)(Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency * 1000) + _clockOffsetMs;
}
