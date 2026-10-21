using AcDream.Core.Combat;
using AcDream.Core.Items;
using AcDream.Core.Selection;
using AcDream.Core.Spells;
using AcDream.Runtime;
using AcDream.Rynth.EngineUi;

namespace AcDream.Rynth.Host;

/// <summary>
/// Forwards the client's combat, selection, enchantment, busy and status-line
/// events to the RynthCore callbacks RynthAi's combat and buffing read, in
/// RynthCore's terms. Raised on the game update thread.
/// </summary>
public sealed class RynthCombatEvents : IDisposable
{
    // RynthCore raises one event per busy step; a reset can drop many at once.
    private const int MaxBusySteps = 64;

    // RynthCore drops a damage figure of 0 or above this as a bad read.
    private const uint MaxDamage = 1_000_000;

    private readonly GameRuntime _runtime;
    private readonly RynthEventBridge _bridge;
    private readonly IRynthGameView _view;
    private readonly CombatState _combat;
    private readonly SelectionState _selection;
    private readonly Spellbook _spellbook;
    private readonly InventoryTransactionState _transactions;
    private int _combatMode;
    private int _busyCount;
    private bool _disposed;

    /// <param name="view">The host's view: the login push reads the mode and selection through it, so on the pump it reads the snapshot.</param>
    public RynthCombatEvents(GameRuntime runtime, RynthEventBridge bridge, IRynthGameView view)
    {
        _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
        _bridge = bridge ?? throw new ArgumentNullException(nameof(bridge));
        _view = view ?? throw new ArgumentNullException(nameof(view));
        _combat = runtime.ActionOwner.Combat;
        _selection = runtime.ActionOwner.Selection;
        _spellbook = runtime.CharacterOwner.Spellbook;
        _transactions = runtime.InventoryOwner.Transactions;
        _combatMode = (int)_combat.CurrentMode;
        _busyCount = _transactions.BusyCount;

        _combat.HealthChanged += OnHealthChanged;
        _combat.CombatModeChanged += OnCombatModeChanged;
        _combat.DamageDealtAccepted += OnDamageDealt;
        _combat.DamageTaken += OnDamageTaken;
        _selection.Changed += OnSelectionChanged;
        _spellbook.EnchantmentAdded += OnEnchantmentAdded;
        _spellbook.EnchantmentRemovedByServer += OnEnchantmentRemoved;
        _transactions.StateChanged += OnTransactionsChanged;
        _runtime.CommunicationOwner.StatusTextShown += OnStatusTextShown;
        _bridge.LoggedIn += OnLoggedIn;
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _combat.HealthChanged -= OnHealthChanged;
        _combat.CombatModeChanged -= OnCombatModeChanged;
        _combat.DamageDealtAccepted -= OnDamageDealt;
        _combat.DamageTaken -= OnDamageTaken;
        _selection.Changed -= OnSelectionChanged;
        _spellbook.EnchantmentAdded -= OnEnchantmentAdded;
        _spellbook.EnchantmentRemovedByServer -= OnEnchantmentRemoved;
        _transactions.StateChanged -= OnTransactionsChanged;
        _runtime.CommunicationOwner.StatusTextShown -= OnStatusTextShown;
        _bridge.LoggedIn -= OnLoggedIn;
    }

    // The server's health updates carry a share; with an appraised maximum the current
    // health is that share of it, as RynthCore computes it (SmartBoxHooks case 0x01C0): the
    // share is capped to 0..1 for the current health but forwarded as received. 0/0 when never appraised.
    private void OnHealthChanged(uint targetId, float healthRatio)
    {
        uint max = _combat.TryGetCreatureVitals(targetId, out CombatState.CreatureVitals vitals) ? vitals.HealthMax : 0u;
        uint current = max == 0 ? 0u : (uint)Math.Round(max * (double)Math.Clamp(healthRatio, 0f, 1f));
        _bridge.OnHealthChanged(targetId, healthRatio, current, max);
    }

    // OpenAC's CombatMode values are RynthCore's 1/2/4/8.
    private void OnCombatModeChanged(CombatMode mode)
    {
        int previous = _combatMode;
        _combatMode = (int)mode;
        if (_combatMode != previous)
            _bridge.OnCombatModeChange(_combatMode, previous);
    }

    private void OnDamageDealt(CombatState.DamageDealt hit)
    {
        if (hit.Damage is > 0 and <= MaxDamage)
            _bridge.OnCombatDamage(hit.Damage, hit.DamageType, hit.Critical ? 1u : 0u, isAttacker: 1u);
    }

    private void OnDamageTaken(CombatState.DamageIncoming hit)
    {
        if (hit.Damage is > 0 and <= MaxDamage)
            _bridge.OnCombatDamage(hit.Damage, hit.DamageType, hit.Critical ? 1u : 0u, isAttacker: 0u);
    }

    // A reset raises null -> null; RynthCore reports changes only.
    private void OnSelectionChanged(SelectionTransition transition)
    {
        uint current = transition.SelectedObjectId ?? 0u;
        uint previous = transition.PreviousObjectId ?? 0u;
        if (current != previous)
            _bridge.OnSelectedTargetChange(current, previous);
    }

    private void OnEnchantmentAdded(ActiveEnchantmentRecord record) =>
        _bridge.OnEnchantmentAdded(record.SpellId & 0xFFFFu, record.Duration);

    // RynthCore reports the server's removals, cooldowns included, never a local expiry.
    // The spell id arrives with its layer already stripped.
    private void OnEnchantmentRemoved(uint spellId) => _bridge.OnEnchantmentRemoved(spellId);

    private void OnTransactionsChanged()
    {
        int current = _transactions.BusyCount;
        int delta = current - _busyCount;
        _busyCount = current;
        int steps = Math.Min(Math.Abs(delta), MaxBusySteps);
        for (int i = 0; i < steps; i++)
        {
            if (delta > 0)
                _bridge.OnBusyCountIncremented();
            else
                _bridge.OnBusyCountDecremented();
        }
    }

    // The bridge queues it for the tick, so a refusal line lands after the call that caused it returns.
    private void OnStatusTextShown(string text) => _bridge.OnStatusText(text);

    // RynthCore tells a plugin the mode (from NonCombat) and any selection once the player is in the world.
    // The bridge raises LoggedIn where it delivers: on the pump thread when the pump runs. There the
    // mode and selection come from the view (the snapshot) and _combatMode is left to the frame
    // thread's own handler; on the frame thread (or before a latch) they are read live, as before.
    private void OnLoggedIn()
    {
        int mode;
        uint selected;
        if (!RynthFrameThread.Latched || RynthFrameThread.IsCurrent)
        {
            _combatMode = mode = (int)_combat.CurrentMode;
            selected = _selection.SelectedObjectId ?? 0u;
        }
        else
        {
            mode = _view.CombatMode;
            selected = _view.SelectedObjectId;
        }
        _bridge.OnCombatModeChange(mode, (int)CombatMode.NonCombat);
        if (selected != 0u)
            _bridge.OnSelectedTargetChange(selected, 0u);
    }
}
