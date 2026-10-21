using System.Numerics;
using AcDream.App.UI;
using AcDream.ClientExtensions;

namespace AcDream.App.Extensions;

/// <summary>
/// <see cref="IClientItemDrops"/> over the retained UI's drag and the overlay's hover. Its
/// <see cref="InterceptRelease"/> is the UI root's release interceptor: a drag let go over an
/// ImGui window never reaches the retail UI or the world; an inventory item is offered to the
/// claimer first, anything else is just cancelled.
/// </summary>
internal sealed class ClientItemDrops(Func<object?> dragPayload, Func<bool> pointerOverOverlay, Action<string> log)
    : IClientItemDrops
{
    private Func<uint, Vector2, bool>? _claimer;

    public uint DraggedItem => InventoryItem(dragPayload());

    public void SetDropClaimer(Func<uint, Vector2, bool>? claimer) => _claimer = claimer;

    public bool InterceptRelease(object payload, Vector2 windowPoint)
    {
        if (!pointerOverOverlay())
            return false;
        uint item = InventoryItem(payload);
        if (item != 0 && _claimer is { } claimer)
        {
            try { claimer(item, windowPoint); }
            catch (Exception error) { log($"[Extensions] item drop claimer failed: {error.GetType().Name}: {error.Message}"); }
        }

        return true;
    }

    private static uint InventoryItem(object? payload) =>
        payload is ItemDragPayload { SourceKind: ItemDragSource.Inventory } item ? item.ObjId : 0;
}
