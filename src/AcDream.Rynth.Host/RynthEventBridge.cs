using AcDream.Core.Chat;
using AcDream.Runtime;

namespace AcDream.Rynth.Host;

/// <summary>
/// Turns OpenAC runtime events into RynthCore plugin callbacks. Raised on the
/// game update thread, which is the thread RynthCore plugins expect (the
/// retail client's message pump).
/// </summary>
/// <remarks>
/// RynthCore queues every plugin event by type and drains the queues once per
/// frame in a fixed order (PluginManager.ProcessPendingActions), so a plugin
/// never hears of an event inside the call that caused it, and same-frame events
/// arrive grouped by type. This bridge does the same: each event waits in its
/// type's queue until <see cref="DeliverQueued"/>, which the host calls each tick
/// before the plugins' own tick. Login is told after the queues of that delivery,
/// as RynthCore dispatches it at the end of the frame's drain, so the entering-world
/// create flood reaches the plugins first. Logout is told at once, as RynthCore
/// dispatches it before the queues.
/// </remarks>
public sealed class RynthEventBridge : IRuntimeEventObserver
{
    // RynthCore's drain order.
    private enum EventKind
    {
        BusyIncremented,
        BusyDecremented,
        CombatMode,
        ViewContents,
        StopViewingContents,
        UpdateInventory,
        CreateObject,
        DeleteObject,
        SelectedTarget,
        UpdateHealth,
        CombatDamage,
        KillNotification,
        EnchantmentAdded,
        EnchantmentRemoved,
        ChatWindowText,
    }

    private const int KindCount = (int)EventKind.ChatWindowText + 1;

    private readonly IReadOnlyList<RynthPlugin> _plugins;
    private readonly List<Action>[] _queues = new List<Action>[KindCount];
    private bool _loginPending;

    public RynthEventBridge(IReadOnlyList<RynthPlugin> plugins)
    {
        _plugins = plugins;
        for (int i = 0; i < KindCount; i++)
            _queues[i] = [];
    }

    /// <summary>Raised once the plugins have been told of the login, for pushes that follow it.</summary>
    public event Action? LoggedIn;

    public void OnLifecycle(in RuntimeLifecycleDelta delta)
    {
        if (delta.Current == RuntimeLifecycleState.InWorld
            && delta.Previous != RuntimeLifecycleState.InWorld)
        {
            RynthCore.Engine.Compatibility.LoginLifecycleHooks.HasObservedLoginComplete = true;
            _loginPending = true;
        }
    }

    // Raised while the session's state is still intact, once per stay in the world.
    public void OnLeavingWorld()
    {
        RynthCore.Engine.Compatibility.LoginLifecycleHooks.HasObservedLoginComplete = false;
        // A login not yet delivered is dropped: the plugins never saw it, so they
        // must not see it after logging out.
        _loginPending = false;
        foreach (RynthPlugin plugin in _plugins)
            plugin.OnLogout();
    }

    public void OnEntity(in RuntimeEntityDelta delta)
    {
        uint objectId = delta.Entity.Identity.ServerGuid;
        if (delta.Change == RuntimeEntityChange.Registered)
        {
            Enqueue(EventKind.CreateObject, () =>
            {
                foreach (RynthPlugin plugin in _plugins)
                    plugin.OnCreateObject(objectId);
            });
        }
        else if (delta.Change == RuntimeEntityChange.Deleted && !delta.ReplacedInPlace)
        {
            Enqueue(EventKind.DeleteObject, () =>
            {
                foreach (RynthPlugin plugin in _plugins)
                    plugin.OnDeleteObject(objectId);
            });
        }
    }

    public void OnChat(in RuntimeChatDelta delta)
    {
        RuntimeChatEntry entry = delta.Entry;
        string text = entry.DisplayText.Length > 0 ? entry.DisplayText : entry.Text;
        int chatType = entry.LogTextType;
        Enqueue(EventKind.ChatWindowText, () =>
        {
            foreach (RynthPlugin plugin in _plugins)
                plugin.OnChatWindowText(text, chatType);
        });
    }

    /// <summary>From <c>CombatState.HealthChanged</c>; current and max are 0 unless the creature was appraised.</summary>
    public void OnHealthChanged(uint targetId, float healthRatio, uint currentHealth = 0, uint maxHealth = 0)
    {
        Enqueue(EventKind.UpdateHealth, () =>
        {
            foreach (RynthPlugin plugin in _plugins)
                plugin.OnUpdateHealth(targetId, healthRatio, currentHealth, maxHealth);
        });
    }

    public void OnBusyCountIncremented()
    {
        Enqueue(EventKind.BusyIncremented, () =>
        {
            foreach (RynthPlugin plugin in _plugins)
                plugin.OnBusyCountIncremented();
        });
    }

    public void OnBusyCountDecremented()
    {
        Enqueue(EventKind.BusyDecremented, () =>
        {
            foreach (RynthPlugin plugin in _plugins)
                plugin.OnBusyCountDecremented();
        });
    }

    public void OnSelectedTargetChange(uint currentTargetId, uint previousTargetId)
    {
        Enqueue(EventKind.SelectedTarget, () =>
        {
            foreach (RynthPlugin plugin in _plugins)
                plugin.OnSelectedTargetChange(currentTargetId, previousTargetId);
        });
    }

    public void OnCombatModeChange(int currentMode, int previousMode)
    {
        Enqueue(EventKind.CombatMode, () =>
        {
            foreach (RynthPlugin plugin in _plugins)
                plugin.OnCombatModeChange(currentMode, previousMode);
        });
    }

    public void OnCombatDamage(uint damage, uint damageType, uint critical, uint isAttacker)
    {
        Enqueue(EventKind.CombatDamage, () =>
        {
            foreach (RynthPlugin plugin in _plugins)
                plugin.OnCombatDamage(damage, damageType, critical, isAttacker);
        });
    }

    public void OnEnchantmentAdded(uint spellId, double durationSeconds)
    {
        Enqueue(EventKind.EnchantmentAdded, () =>
        {
            foreach (RynthPlugin plugin in _plugins)
                plugin.OnEnchantmentAdded(spellId, durationSeconds);
        });
    }

    public void OnEnchantmentRemoved(uint spellId)
    {
        Enqueue(EventKind.EnchantmentRemoved, () =>
        {
            foreach (RynthPlugin plugin in _plugins)
                plugin.OnEnchantmentRemoved(spellId);
        });
    }

    /// <summary>
    /// A client status line ("You're too busy!"). The retail client shows these in the
    /// chat window, where RynthCore reads them; OpenAC shows them as floating text only.
    /// </summary>
    public void OnStatusText(string text)
    {
        Enqueue(EventKind.ChatWindowText, () =>
        {
            foreach (RynthPlugin plugin in _plugins)
                plugin.OnChatWindowText(text, (int)RetailLogTextType.ClientLocal);
        });
    }

    /// <summary>
    /// From <c>RuntimeCommunicationState.LocalPlayerDied</c>: the player's health
    /// reaching zero, which is the death RynthTracker counts.
    /// </summary>
    public void OnLocalPlayerDied(uint playerId, uint maxHealth)
    {
        Enqueue(EventKind.UpdateHealth, () =>
        {
            foreach (RynthPlugin plugin in _plugins)
                plugin.OnUpdateHealth(playerId, 0f, 0, maxHealth);
        });
    }

    /// <summary>From <c>CombatState.KillLanded</c>: the server's killer notification text.</summary>
    public void OnKillLanded(string deathMessage)
    {
        Enqueue(EventKind.KillNotification, () =>
        {
            foreach (RynthPlugin plugin in _plugins)
                plugin.OnKillNotification(deathMessage);
        });
    }

    /// <summary>
    /// From <c>ExternalContainerState.Changed</c> (Opened): the client now shows
    /// <paramref name="containerId"/>, having stopped showing <paramref name="previousId"/>.
    /// </summary>
    public void OnContainerOpened(uint previousId, uint containerId)
    {
        // A guard only: ExternalContainerState raises Opened with previous 0 (the client
        // reports the replaced container through ReplacementRequested -> OnContainerClosed).
        if (previousId != 0 && previousId != containerId)
            OnContainerClosed(previousId);
        if (containerId == 0)
            return;
        Enqueue(EventKind.ViewContents, () =>
        {
            foreach (RynthPlugin plugin in _plugins)
                plugin.OnViewObjectContents(containerId);
        });
    }

    /// <summary>From <c>ExternalContainerState.Changed</c> (Closed or Reset).</summary>
    public void OnContainerClosed(uint containerId)
    {
        if (containerId == 0)
            return;
        Enqueue(EventKind.StopViewingContents, () =>
        {
            foreach (RynthPlugin plugin in _plugins)
                plugin.OnStopViewingObjectContents(containerId);
        });
    }

    /// <summary>From the object table: a container's contents were replaced or an item moved in or out.</summary>
    public void OnContainerContentsChanged(uint containerId)
    {
        if (containerId == 0)
            return;
        Enqueue(EventKind.UpdateInventory, () =>
        {
            foreach (RynthPlugin plugin in _plugins)
                plugin.OnUpdateObjectInventory(containerId);
        });
    }

    /// <summary>
    /// Delivers the queued events, type by type in RynthCore's order and each type in
    /// arrival order. An event raised while delivering joins this delivery when its
    /// type has not run yet and waits for the next one otherwise, as with RynthCore's
    /// per-type queues. A pending login is told last, as RynthCore's frame does.
    /// </summary>
    public void DeliverQueued()
    {
        for (int i = 0; i < KindCount; i++)
        {
            List<Action> pending = _queues[i];
            if (pending.Count == 0)
                continue;
            _queues[i] = [];
            foreach (Action deliver in pending)
                deliver();
        }

        if (_loginPending)
        {
            _loginPending = false;
            foreach (RynthPlugin plugin in _plugins)
                plugin.OnLoginComplete();
            LoggedIn?.Invoke();
        }
    }

    private void Enqueue(EventKind kind, Action deliver) => _queues[(int)kind].Add(deliver);

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
