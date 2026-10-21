using System;
using System.Threading;

namespace AcDream.Rynth.EngineUi;

/// <summary>
/// The client's frame thread, latched when the Rynth pump starts. Off it, the routed view
/// answers from the snapshot and queues actions; before a latch everything is live.
/// </summary>
public static class RynthFrameThread
{
    private static int _id;

    public static bool Latched => Volatile.Read(ref _id) != 0;

    public static bool IsCurrent => Volatile.Read(ref _id) == Environment.CurrentManagedThreadId;

    public static void Latch() => Volatile.Write(ref _id, Environment.CurrentManagedThreadId);

    /// <summary>Back to "not latched" (pump stopped, tests).</summary>
    public static void Reset() => Volatile.Write(ref _id, 0);
}
