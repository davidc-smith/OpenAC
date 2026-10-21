namespace AcDream.Rynth.EngineUi;

/// <summary>The frame action queue the pump set up, for code (stand-ins) that has no host to ask; null when the pump is off.</summary>
public static class RynthFrameActions
{
    public static RynthActionQueue? Queue { get; set; }
}
