using System;
using AcDream.App.UI.Layout;
using AcDream.Core.Items;

namespace AcDream.App.UI;

/// <summary>What a drag lifted off the world carries, and the picture that
/// follows the pointer (null draws none).</summary>
public readonly record struct WorldDragStart(object Payload, (uint tex, int w, int h)? Ghost);

/// <summary>The world side of a drag: what the object under a press point
/// lifts. The player lifts only out of combat, as the game does: dragging your
/// own character carries your main pack, so it can go on the hotbar as a
/// shortcut that targets yourself (healing kits, mana stones). A loose item on
/// the ground lifts when the caller binds a ground lift (the Modern HUD does,
/// so an item can be dragged into the Gear Deck); it carries a ground payload,
/// the one an outside container's cell lifts, so a drop on a pack picks it up.
/// Other world objects lift nothing.</summary>
public static class WorldDrag
{
    /// <summary>Where the main pack sits in the pack row: lifting the player
    /// off the world is the same lift as one started on that slot, so every
    /// drop target that knows that payload treats it the same.</summary>
    public const int MainPackSlotIndex = 0;

    /// <summary>The drag ghost's size in pixels, the paperdoll figure's.</summary>
    public const int GhostSize = 32;

    /// <summary>The lift for the object <paramref name="hit"/> under the press
    /// point, or null when it lifts nothing. <paramref name="groundItem"/> answers
    /// the item a world object lifts as, or null when it cannot be picked up;
    /// left null, no world item lifts (the retail HUD's behaviour).</summary>
    public static WorldDragStart? Build(
        uint hit,
        uint playerGuid,
        bool outOfCombat,
        Func<ItemType, uint, uint, uint, uint, uint> iconIds,
        Func<ItemType, uint, uint, uint, uint, uint>? dragIconIds,
        Func<uint, ClientObject?>? groundItem = null)
    {
        ArgumentNullException.ThrowIfNull(iconIds);
        if (hit == 0u)
            return null;
        if (hit != playerGuid)
            return groundItem?.Invoke(hit) is { } item ? GroundLift(item, iconIds, dragIconIds) : null;
        if (!outOfCombat)
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

    private static WorldDragStart GroundLift(
        ClientObject item,
        Func<ItemType, uint, uint, uint, uint, uint> iconIds,
        Func<ItemType, uint, uint, uint, uint, uint>? dragIconIds)
    {
        var payload = new ItemDragPayload(item.ObjectId, ItemDragSource.Ground, 0, SourceCell: null);
        uint tex = dragIconIds?.Invoke(
            item.Type, item.IconId, item.IconUnderlayId, item.IconOverlayId, item.Effects) ?? 0u;
        if (tex == 0u)
            tex = iconIds(item.Type, item.IconId, item.IconUnderlayId, item.IconOverlayId, item.Effects);
        return new WorldDragStart(payload, tex != 0u ? (tex, GhostSize, GhostSize) : null);
    }
}
