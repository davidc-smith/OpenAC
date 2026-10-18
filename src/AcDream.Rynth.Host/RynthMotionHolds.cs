using AcDream.Plugin.Abstractions;

namespace AcDream.Rynth.Host;

/// <summary>
/// RynthCore's held movement keys (SetMotion on/off, the jump key) kept as a
/// set and handed to the client as one movement intent. The intent replaces
/// the keyboard while anything is held, so it carries autorun too.
/// </summary>
internal sealed class RynthMotionHolds(IRynthGameView view)
{
    private const uint WalkForward = 0x45000005, WalkBackward = 0x45000006, RunForward = 0x41000003,
        TurnRight = 0x6500000D, TurnLeft = 0x6500000E, SidestepRight = 0x6500000F, SidestepLeft = 0x65000010;

    private readonly HashSet<uint> _held = [];
    private bool _jump;
    private PluginMovementIntent? _launch;
    private PluginMovementIntent? _sent;

    /// <summary>Holds or lets go of a motion key. False for a motion RynthCore's SetMotion does not hold.</summary>
    public bool SetMotion(uint motion, bool enabled)
    {
        if (motion is not (WalkForward or WalkBackward or RunForward or TurnRight or TurnLeft or SidestepRight or SidestepLeft))
            return false;
        bool changed = enabled ? _held.Add(motion) : _held.Remove(motion);
        return !changed || Apply();
    }

    /// <summary>Presses the jump key: the client charges as it does for a held space bar.</summary>
    public bool CommenceJump()
    {
        _jump = true;
        return Apply();
    }

    /// <summary>Lets go of the jump key: the client jumps at the power charged.</summary>
    public bool DoJump()
    {
        _jump = false;
        return Apply();
    }

    /// <summary>Lets go of the jump key with these keys down for that frame, so the jump carries their motion.</summary>
    public bool LaunchJumpWithMotion(bool shift, bool forward, bool backward, bool strafeLeft, bool strafeRight)
    {
        _jump = false;
        PluginMovementIntent held = Held();
        _launch = held with
        {
            Forward = held.Forward || forward,
            Backward = held.Backward || backward,
            StrafeLeft = held.StrafeLeft || strafeLeft,
            StrafeRight = held.StrafeRight || strafeRight,
            Run = held.Run || (!shift && (forward || backward || strafeLeft || strafeRight)),
        };
        return Apply();
    }

    /// <summary>Lets go of every key and stops the player.</summary>
    public bool StopCompletely()
    {
        _held.Clear();
        _jump = false;
        _launch = null;
        Apply();
        return view.StopCompletely();
    }

    /// <summary>Lets go of every held key without stopping the player: the host is going away and must not leave the keyboard locked out.</summary>
    public void Release()
    {
        _held.Clear();
        _jump = false;
        _launch = null;
        Apply();
    }

    /// <summary>Once a tick, before plugins tick: ends a launch frame and follows autorun.</summary>
    public void Sync()
    {
        _launch = null;
        Apply();
    }

    /// <summary>Re-sends the held keys after autorun changed, leaving a launch frame alone.</summary>
    public bool Refresh() => Apply();

    private bool Apply()
    {
        PluginMovementIntent? wanted = _launch ?? (_held.Count == 0 && !_jump ? null : Held());
        if (wanted == _sent)
            return true;
        bool taken = wanted is { } intent ? view.SetMovementIntent(intent) : view.ClearMovementIntent();
        if (taken)
            _sent = wanted;
        return taken;
    }

    private PluginMovementIntent Held()
    {
        bool autoRun = view.AutoRunActive;
        return new PluginMovementIntent(
            Forward: autoRun || _held.Contains(RunForward) || _held.Contains(WalkForward),
            Backward: _held.Contains(WalkBackward),
            StrafeLeft: _held.Contains(SidestepLeft),
            StrafeRight: _held.Contains(SidestepRight),
            TurnLeft: _held.Contains(TurnLeft),
            TurnRight: _held.Contains(TurnRight),
            Run: autoRun || _held.Contains(RunForward),
            Jump: _jump);
    }
}
