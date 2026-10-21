// Stand-in for the user32 GetCursorPos RynthCore's UiDropTargets reads to hit-test its drop
// targets. UiDropTargets is compiled unchanged; after compile, tools/RynthInteropRedirect points
// its GetCursorPos at the method here (their POINT is private, so CursorPoint has its layout).
using System;
using System.Numerics;

namespace AcDream.Rynth.EngineUi;

/// <summary>
/// The cursor as UiDropTargets asks Windows for it, in ImGui points (OpenAC has one window and
/// no client offset): the host's source (the overlay mouse), or the release point while a drop
/// is being claimed. Game thread.
/// </summary>
internal static class Win32CursorStandIn
{
    public struct CursorPoint
    {
        public int X;
        public int Y;
    }

    /// <summary>The overlay mouse; null: no cursor (nothing is ever under it).</summary>
    internal static Func<Vector2>? Source { get; set; }

    /// <summary>Set by <see cref="RynthDropClaim"/> for the length of one claim.</summary>
    internal static Vector2? Override { get; set; }

    public static bool GetCursorPos(out CursorPoint p)
    {
        Vector2? point = Override ?? Source?.Invoke();
        p = point is { } v ? new CursorPoint { X = (int)MathF.Floor(v.X), Y = (int)MathF.Floor(v.Y) } : default;
        return point is not null;
    }
}
