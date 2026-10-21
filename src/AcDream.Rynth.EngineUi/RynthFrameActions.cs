using System.Threading;

namespace AcDream.Rynth.EngineUi;

/// <summary>The frame action queue the pump set up, for code (stand-ins) that has no host to ask; null when the pump is off.</summary>
public static class RynthFrameActions
{
    private static RynthActionQueue? _queue;

    /// <summary>Set by the pump on the frame thread, read by stand-ins on any thread.</summary>
    public static RynthActionQueue? Queue
    {
        get => Volatile.Read(ref _queue);
        set => Volatile.Write(ref _queue, value);
    }
}
