namespace AcDream.Core.Items;

/// <summary>
/// The slot a drop onto a container's item list asks the server for. The
/// dropped item takes the place of the item under the pointer, which moves
/// one slot on, so the dropped item always lands to the left of the item it
/// was dropped on. A drop on an empty cell past the last item appends.
/// </summary>
/// <remarks>
/// The server takes a moved item out of its old slot before it inserts it
/// at the requested one. An item moving later in its own list therefore
/// asks for one slot less than the cell it was dropped on; without that it
/// would land to the right of that item. A split leaves the source stack
/// where it is, so a split never takes the slot off.
/// </remarks>
public static class ItemListDropPlacement
{
    /// <summary>
    /// The placement to request, or <c>null</c> when the drop leaves the
    /// item where it already is and nothing is sent.
    /// </summary>
    /// <param name="dropIndex">Index of the cell under the pointer; any
    /// index at or past <paramref name="itemCount"/> means "after the last
    /// item".</param>
    /// <param name="itemCount">Number of items the list shows, empty cells
    /// not counted.</param>
    /// <param name="sourceIndex">The dragged item's index in this list, or
    /// -1 when it comes from anywhere else (another pack, the paperdoll, the
    /// ground).</param>
    /// <param name="wholeStack">True when the whole item moves; false when a
    /// split leaves the source stack in place.</param>
    public static int? Resolve(int dropIndex, int itemCount, int sourceIndex, bool wholeStack)
    {
        int placement = Math.Min(Math.Max(dropIndex, 0), Math.Max(itemCount, 0));
        if (sourceIndex < 0)
            return placement;
        if (placement == sourceIndex)
            return null;                         // dropped on itself
        if (!wholeStack)
            return placement;
        if (placement == sourceIndex + 1)
            return null;                         // already left of the item it was dropped on
        if (sourceIndex < placement)
            placement--;                         // its old slot closes up first
        return placement;
    }
}
