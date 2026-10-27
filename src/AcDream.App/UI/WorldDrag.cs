using System;
using AcDream.App.UI.Layout;
using AcDream.Core.Items;

namespace AcDream.App.UI;

/// <summary>What a drag lifted off the world carries, and the picture that
/// follows the pointer (null draws none).</summary>
public readonly record struct WorldDragStart(object Payload, (uint tex, int w, int h)? Ghost);

/// <summary>The world side of a drag: what the object under a press point
/// lifts. Only the player lifts, and only out of combat, as the game does:
/// dragging your own character carries your main pack, so it can go on the
/// hotbar as a shortcut that targets yourself (healing kits, mana stones).
/// Other world objects lift nothing yet.</summary>
public static class WorldDrag
{
    /// <summary>Where the main pack sits in the pack row: lifting the player
    /// off the world is the same lift as one started on that slot, so every
    /// drop target that knows that payload treats it the same.</summary>
    public const int MainPackSlotIndex = 0;

    /// <summary>The drag ghost's size in pixels, the paperdoll figure's.</summary>
    public const int GhostSize = 32;

    /// <summary>The lift for the object <paramref name="hit"/> under the press
    /// point, or null when it lifts nothing.</summary>
    public static WorldDragStart? Build(
        uint hit,
        uint playerGuid,
        bool outOfCombat,
        Func<ItemType, uint, uint, uint, uint, uint> iconIds,
        Func<ItemType, uint, uint, uint, uint, uint>? dragIconIds)
    {
        ArgumentNullException.ThrowIfNull(iconIds);
        if (!outOfCombat || hit == 0u || hit != playerGuid)
            return null;
        var payload = new ItemDragPayload(
            hit, ItemDragSource.Inventory, MainPackSlotIndex, SourceCell: null);
        // The player has no icon of their own; the pack row draws the main pack
        // with a fixed one, and this lift shows the same picture.
        uint tex = dragIconIds?.Invoke(
            ItemType.Container, InventoryController.PlayerPackBaseIcon, 0u, 0u, 0u) ?? 0u;
        if (tex == 0u)
            tex = iconIds(ItemType.Container, InventoryController.PlayerPackBaseIcon, 0u, 0u, 0u);
        return new WorldDragStart(payload, tex != 0u ? (tex, GhostSize, GhostSize) : null);
    }
}
