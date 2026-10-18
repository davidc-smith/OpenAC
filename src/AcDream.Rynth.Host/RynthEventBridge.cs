using AcDream.Runtime;

namespace AcDream.Rynth.Host;

/// <summary>
/// Turns OpenAC runtime events into RynthCore plugin callbacks. Raised on the
/// game update thread, which is the thread RynthCore plugins expect (the
/// retail client's message pump).
/// </summary>
public sealed class RynthEventBridge : IRuntimeEventObserver
{
    private readonly IReadOnlyList<RynthPlugin> _plugins;

    public RynthEventBridge(IReadOnlyList<RynthPlugin> plugins) => _plugins = plugins;

    public void OnLifecycle(in RuntimeLifecycleDelta delta)
    {
        if (delta.Current == RuntimeLifecycleState.InWorld
            && delta.Previous != RuntimeLifecycleState.InWorld)
        {
            RynthCore.Engine.Compatibility.LoginLifecycleHooks.HasObservedLoginComplete = true;
            foreach (RynthPlugin plugin in _plugins)
                plugin.OnLoginComplete();
        }
    }

    // Raised while the session's state is still intact, once per stay in the world.
    public void OnLeavingWorld()
    {
        RynthCore.Engine.Compatibility.LoginLifecycleHooks.HasObservedLoginComplete = false;
        foreach (RynthPlugin plugin in _plugins)
            plugin.OnLogout();
    }

    public void OnEntity(in RuntimeEntityDelta delta)
    {
        uint objectId = delta.Entity.Identity.ServerGuid;
        if (delta.Change == RuntimeEntityChange.Registered)
        {
            foreach (RynthPlugin plugin in _plugins)
                plugin.OnCreateObject(objectId);
        }
        else if (delta.Change == RuntimeEntityChange.Deleted && !delta.ReplacedInPlace)
        {
            foreach (RynthPlugin plugin in _plugins)
                plugin.OnDeleteObject(objectId);
        }
    }

    public void OnChat(in RuntimeChatDelta delta)
    {
        RuntimeChatEntry entry = delta.Entry;
        string text = entry.DisplayText.Length > 0 ? entry.DisplayText : entry.Text;
        foreach (RynthPlugin plugin in _plugins)
            plugin.OnChatWindowText(text, entry.LogTextType);
    }

    /// <summary>From <c>CombatState.HealthChanged</c>.</summary>
    public void OnHealthChanged(uint targetId, float healthRatio)
    {
        foreach (RynthPlugin plugin in _plugins)
            plugin.OnUpdateHealth(targetId, healthRatio);
    }

    /// <summary>
    /// From <c>RuntimeCommunicationState.LocalPlayerDied</c>: the player's health
    /// reaching zero, which is the death RynthTracker counts.
    /// </summary>
    public void OnLocalPlayerDied(uint playerId, uint maxHealth)
    {
        foreach (RynthPlugin plugin in _plugins)
            plugin.OnUpdateHealth(playerId, 0f, 0, maxHealth);
    }

    /// <summary>From <c>CombatState.KillLanded</c>: the server's killer notification text.</summary>
    public void OnKillLanded(string deathMessage)
    {
        foreach (RynthPlugin plugin in _plugins)
            plugin.OnKillNotification(deathMessage);
    }

    public void OnCommand(in RuntimeCommandDelta delta)
    {
    }

    public void OnInventory(in RuntimeInventoryDelta delta)
    {
    }

    public void OnMovement(in RuntimeMovementDelta delta)
    {
    }

    public void OnPortal(in RuntimePortalDelta delta)
    {
    }

    public void OnCombat(in RuntimeCombatDelta delta)
    {
    }
}
