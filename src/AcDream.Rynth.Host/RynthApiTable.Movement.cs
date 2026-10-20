using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace AcDream.Rynth.Host;

// The table's movement and door slots (RynthAi sub-project #4). Same rules as every
// table function: catch everything, log, return RynthCore's "not available" value.
public sealed unsafe partial class RynthApiTable
{
    /// <summary>TapJump's charge: RynthCore presses and releases at once, the least a jump can be.</summary>
    internal const float TapJumpPower = 1f / 30f;

    private void BindMovementSlots()
    {
        Native->SetAutoRunFn = (IntPtr)(delegate* unmanaged[Cdecl]<int, int>)&SetAutoRun;
        Native->TurnToHeadingFn = (IntPtr)(delegate* unmanaged[Cdecl]<float, int>)&TurnToHeading;
        Native->SetMotionFn = (IntPtr)(delegate* unmanaged[Cdecl]<uint, int, int>)&SetMotion;
        Native->DoMovementFn = (IntPtr)(delegate* unmanaged[Cdecl]<uint, float, int, int>)&DoMovement;
        Native->StopMovementFn = (IntPtr)(delegate* unmanaged[Cdecl]<uint, int, int>)&StopMovement;
        Native->StopCompletelyFn = (IntPtr)(delegate* unmanaged[Cdecl]<int>)&StopCompletely;
        Native->JumpNonAutonomousFn = (IntPtr)(delegate* unmanaged[Cdecl]<float, int>)&JumpNonAutonomous;
        Native->TapJumpFn = (IntPtr)(delegate* unmanaged[Cdecl]<int>)&TapJump;
        Native->CommenceJumpFn = (IntPtr)(delegate* unmanaged[Cdecl]<int>)&CommenceJump;
        Native->DoJumpFn = (IntPtr)(delegate* unmanaged[Cdecl]<int, int>)&DoJump;
        Native->LaunchJumpWithMotionFn = (IntPtr)(delegate* unmanaged[Cdecl]<int, int, int, int, int, int>)&LaunchJumpWithMotion;
        Native->GetObjectStateFn = (IntPtr)(delegate* unmanaged[Cdecl]<uint, uint*, int>)&GetObjectState;
        Native->GetObjectMotionOnFn = (IntPtr)(delegate* unmanaged[Cdecl]<uint, int*, int>)&GetObjectMotionOn;
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static int SetAutoRun(int enabled)
    {
        RynthApiTable? table = _current;
        if (table is null)
            return 0;
        try
        {
            if (!table._view.SetAutoRun(enabled != 0))
                return 0;
            table.Holds.Refresh();
            return 1;
        }
        catch (Exception error)
        {
            table.Fail(nameof(SetAutoRun), error);
            return 0;
        }
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static int TurnToHeading(float headingDegrees)
    {
        RynthApiTable? table = _current;
        if (table is null)
            return 0;
        try { return table._view.SnapHeading(headingDegrees) ? 1 : 0; }
        catch (Exception error)
        {
            table.Fail(nameof(TurnToHeading), error);
            return 0;
        }
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static int SetMotion(uint motion, int enabled)
    {
        RynthApiTable? table = _current;
        if (table is null)
            return 0;
        try { return table.Holds.SetMotion(motion, enabled != 0) ? 1 : 0; }
        catch (Exception error)
        {
            table.Fail(nameof(SetMotion), error);
            return 0;
        }
    }

    // RynthCore's DoMovementCommand: here a held key, as SetMotion holds it. Speed and the
    // hold key are ignored; run or walk follows the held run state.
    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static int DoMovement(uint motion, float speed, int holdKey)
    {
        RynthApiTable? table = _current;
        if (table is null)
            return 0;
        try { return table.Holds.SetMotion(motion, true) ? 1 : 0; }
        catch (Exception error)
        {
            table.Fail(nameof(DoMovement), error);
            return 0;
        }
    }

    // RynthCore's StopMovementCommand: lets go of the held key.
    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static int StopMovement(uint motion, int holdKey)
    {
        RynthApiTable? table = _current;
        if (table is null)
            return 0;
        try { return table.Holds.SetMotion(motion, false) ? 1 : 0; }
        catch (Exception error)
        {
            table.Fail(nameof(StopMovement), error);
            return 0;
        }
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static int StopCompletely()
    {
        RynthApiTable? table = _current;
        if (table is null)
            return 0;
        try { return table.Holds.StopCompletely() ? 1 : 0; }
        catch (Exception error)
        {
            table.Fail(nameof(StopCompletely), error);
            return 0;
        }
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static int JumpNonAutonomous(float extent)
    {
        RynthApiTable? table = _current;
        if (table is null || !float.IsFinite(extent))
            return 0;
        try { return table._view.BeginJump(Math.Clamp(extent, TapJumpPower, 1f)) ? 1 : 0; }
        catch (Exception error)
        {
            table.Fail(nameof(JumpNonAutonomous), error);
            return 0;
        }
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static int TapJump()
    {
        RynthApiTable? table = _current;
        if (table is null)
            return 0;
        try { return table._view.BeginJump(TapJumpPower) ? 1 : 0; }
        catch (Exception error)
        {
            table.Fail(nameof(TapJump), error);
            return 0;
        }
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static int CommenceJump()
    {
        RynthApiTable? table = _current;
        if (table is null)
            return 0;
        try { return table.Holds.CommenceJump() ? 1 : 0; }
        catch (Exception error)
        {
            table.Fail(nameof(CommenceJump), error);
            return 0;
        }
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static int DoJump(int autonomous)
    {
        // autonomous is ignored: OpenAC's jump is always the player's own.
        RynthApiTable? table = _current;
        if (table is null)
            return 0;
        try { return table.Holds.DoJump() ? 1 : 0; }
        catch (Exception error)
        {
            table.Fail(nameof(DoJump), error);
            return 0;
        }
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static int LaunchJumpWithMotion(int shift, int w, int x, int z, int c)
    {
        RynthApiTable? table = _current;
        if (table is null)
            return 0;
        try { return table.Holds.LaunchJumpWithMotion(shift != 0, w != 0, x != 0, z != 0, c != 0) ? 1 : 0; }
        catch (Exception error)
        {
            table.Fail(nameof(LaunchJumpWithMotion), error);
            return 0;
        }
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static int GetObjectState(uint objectId, uint* state)
    {
        RynthApiTable? table = _current;
        if (table is null || state is null)
            return 0;
        try
        {
            if (!table._view.TryGetObjectState(objectId, out uint value))
                return 0;
            *state = value;
            return 1;
        }
        catch (Exception error)
        {
            table.Fail(nameof(GetObjectState), error);
            return 0;
        }
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static int GetObjectMotionOn(uint objectId, int* isOn)
    {
        RynthApiTable? table = _current;
        if (table is null || isOn is null)
            return 0;
        try
        {
            if (!table._view.TryGetObjectMotionOn(objectId, out bool on))
                return 0;
            *isOn = on ? 1 : 0;
            return 1;
        }
        catch (Exception error)
        {
            table.Fail(nameof(GetObjectMotionOn), error);
            return 0;
        }
    }
}
