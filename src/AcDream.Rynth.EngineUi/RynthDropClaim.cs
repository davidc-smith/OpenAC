using System.Numerics;
using RynthCore.Engine.ImGuiBackend;

namespace AcDream.Rynth.EngineUi;

/// <summary>
/// An inventory item let go over an overlay window, offered to RynthCore's drop targets as
/// their UIElement drop detour does: the cursor reads the release point while their TryClaim
/// hit-tests, and the face takes the item with TryTake on its next draw. Game thread.
/// </summary>
internal static class RynthDropClaim
{
    internal static bool Claim(uint itemId, Vector2 point)
    {
        Win32CursorStandIn.Override = point;
        try
        {
            return UiDropTargets.TryClaim(itemId, 0, out _);
        }
        finally
        {
            Win32CursorStandIn.Override = null;
        }
    }
}
