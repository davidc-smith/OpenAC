using System.Numerics;

namespace AcDream.ClientExtensions;

/// <summary>
/// Items dragged out of the client's packs or main inventory, for extension panels that take
/// dropped items. Points are window points (the overlay's space). Game thread.
/// </summary>
public interface IClientItemDrops
{
    /// <summary>The object being dragged out of a pack or the main inventory now; 0 otherwise.</summary>
    uint DraggedItem { get; }

    /// <summary>
    /// The one claimer asked when such an item is let go over an overlay window: the item and
    /// the release point; true when a panel took it. Null removes it.
    /// </summary>
    void SetDropClaimer(Func<uint, Vector2, bool>? claimer);
}
