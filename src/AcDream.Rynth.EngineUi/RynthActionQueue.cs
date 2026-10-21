using System;
using System.Collections.Generic;
using System.Threading;

namespace AcDream.Rynth.EngineUi;

/// <summary>
/// Start: may be refused. Stop: stops, cancels, releases; may use the reserve. Chat: a chat line,
/// never refused while portalling or dead, but held out of the reserve as a start is.
/// </summary>
public enum RynthActionKind { Start, Stop, Chat }

/// <summary>
/// Actions from off the frame thread, run on it in order (RynthCore's AcMainThreadQueue):
/// a bounded ring, multi-producer under a short lock, drained by the frame thread. Starts are
/// refused while <c>blocked</c> (portalling or dead) and leave a reserve for stops, as do chat
/// lines (never refused, so a burst of them can't take the stops' room); a full ring drops and
/// counts, never runs off-thread.
/// </summary>
public sealed class RynthActionQueue(Action<string> log, Func<bool> blocked, Func<long>? clock = null)
{
    public const int Capacity = 256;
    public const int StopReserve = 32;
    private const long LogEveryMs = 10_000;

    private readonly object _gate = new();
    private readonly Queue<(string Label, Func<bool> Work)> _entries = new();
    private readonly Func<long> _clock = clock ?? (() => Environment.TickCount64);
    private readonly Dictionary<string, long> _lastLogged = new();
    private long _droppedLoggedAt = long.MinValue;
    private int _dropped;

    public int Dropped => Volatile.Read(ref _dropped);

    public bool TryEnqueue(RynthActionKind kind, string label, Func<bool> work)
    {
        if (kind == RynthActionKind.Start && blocked())
            return false;
        lock (_gate)
        {
            int limit = kind == RynthActionKind.Stop ? Capacity : Capacity - StopReserve;
            if (_entries.Count >= limit)
            {
                _dropped++;
                long now = _clock();
                if (_droppedLoggedAt == long.MinValue || now - _droppedLoggedAt >= LogEveryMs)
                {
                    _droppedLoggedAt = now;
                    log($"action queue full: '{label}' dropped ({_dropped} dropped so far)");
                }
                return false;
            }
            _entries.Enqueue((label, work));
            return true;
        }
    }

    /// <summary>Frame thread: runs what is queued now, in order. Returns how many ran.</summary>
    public int Drain()
    {
        (string Label, Func<bool> Work)[] batch;
        lock (_gate)
        {
            if (_entries.Count == 0)
                return 0;
            batch = [.. _entries];
            _entries.Clear();
        }
        foreach ((string label, Func<bool> work) in batch)
        {
            string? failure;
            try { failure = work() ? null : "refused"; }
            catch (Exception error) { failure = $"{error.GetType().Name}: {error.Message}"; }
            if (failure is not null)
                LogFailure(label, failure);
        }
        return batch.Length;
    }

    /// <summary>Drops everything queued (logout, dispose).</summary>
    public void Clear()
    {
        lock (_gate)
            _entries.Clear();
    }

    private void LogFailure(string label, string failure)
    {
        long now = _clock();
        if (_lastLogged.TryGetValue(label, out long at) && now - at < LogEveryMs)
            return;
        _lastLogged[label] = now;
        log($"queued action '{label}' failed: {failure}");
    }
}
