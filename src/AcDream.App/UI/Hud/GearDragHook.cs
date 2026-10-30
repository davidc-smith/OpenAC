namespace AcDream.App.UI.Hud;

/// <summary>
/// What a manual item drag does to the Gear Deck's organising, wherever it starts (the deck, the retail
/// inventory, an outside container or corpse, the world): it cancels a running queue, since the player took
/// over, and the dragged item is no longer a pending arrival, so the rules do not undo the drag. A shortcut
/// drag moves no item and changes nothing.
/// </summary>
internal static class GearDragHook
{
    /// <summary>True when it cancelled a running queue.</summary>
    public static bool OnDragStarted(object payload, GearMoveQueue? queue, GearInventoryWatcher? watcher, Action<string>? log = null)
    {
        if (payload is not ItemDragPayload { SourceKind: not ItemDragSource.ShortcutBar } item) return false;
        watcher?.ForgetArrival(item.ObjId);
        if (queue is not { IsRunning: true }) return false;
        queue.Cancel();
        log?.Invoke($"[gear-deck] drag of 0x{item.ObjId:X8} cancelled the running queue");
        return true;
    }
}
