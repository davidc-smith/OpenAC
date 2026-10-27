using AcDream.Core.Combat;
using AcDream.Core.Items;
using AcDream.Core.Net;
using AcDream.Core.Net.Messages;
using AcDream.Core.Spells;
using AcDream.Content;
using AcDream.Runtime.Gameplay;

namespace AcDream.Runtime.Session.Windowless;

/// <summary>
/// What a session with no window does when it attacks, picks a combat
/// target, changes its combat mode or casts a spell: the one implementation
/// every client without a window uses, the windowless client for its whole
/// life and the graphical client while no window is attached. It sends on
/// the world connection whose command route is active, and only while the
/// character is in the world; what a window would show goes to chat.
/// </summary>
internal sealed class RuntimeWindowlessGameplayOperations
    : IRuntimeCombatAttackOperations,
      IRuntimeCombatTargetOperations,
      IRuntimeCombatModeOperations,
      IRuntimeSpellCastOperations
{
    private sealed class SessionRoute(
        RuntimeWindowlessGameplayOperations owner,
        WorldSession session)
        : ILiveSessionCommandRouting
    {
        private bool _active;

        public void Activate()
        {
            if (_active)
                return;
            owner.Activate(session, this);
            _active = true;
        }

        public void Dispose()
        {
            owner.Deactivate(this);
            _active = false;
        }
    }

    private readonly object _gate = new();
    private GameRuntime? _runtime;
    private SpellComponentRequirementService? _componentRequirements;
    private Func<string>? _accountName;
    private WorldSession? _session;
    private SessionRoute? _route;

    internal void Bind(
        GameRuntime runtime,
        MagicCatalog? catalog,
        Func<string> accountName)
    {
        ArgumentNullException.ThrowIfNull(runtime);
        ArgumentNullException.ThrowIfNull(accountName);
        if (_runtime is not null)
        {
            throw new InvalidOperationException(
                "Windowless gameplay operations are already bound.");
        }

        _runtime = runtime;
        _accountName = accountName;
        if (catalog is not null)
            UseMagicCatalog(catalog);
    }

    /// <summary>
    /// The spell tables a cast's components are checked against, for a
    /// session whose game files open after it was built. Until then a cast
    /// is checked against the character's own "components required" flag.
    /// </summary>
    internal void UseMagicCatalog(MagicCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        GameRuntime runtime = RequireRuntime();
        if (_componentRequirements is not null)
        {
            throw new InvalidOperationException(
                "Windowless gameplay operations already have their spell tables.");
        }

        _componentRequirements = catalog.CreateRequirementService(
            runtime.InventoryOwner.Objects,
            () => runtime.PlayerIdentity.ServerGuid,
            _accountName!);
    }

    internal ILiveSessionCommandRouting CreateRoute(
        WorldSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        return new SessionRoute(this, session);
    }

    public bool CanStartAttack(bool allowAutoTarget)
    {
        GameRuntime runtime = RequireRuntime();
        if (!IsInWorld
            || !CombatInputPlanner.SupportsTargetedAttack(
                runtime.ActionOwner.Combat.CurrentMode))
        {
            return false;
        }
        return GetSelectedOrClosestTarget(runtime, allowAutoTarget) is not null;
    }

    public void PrepareAttackRequest() =>
        RequireRuntime().PrepareLocalPlayerForAttackRequest();

    public bool SendAttack(AttackHeight height, float power, bool allowAutoTarget)
    {
        GameRuntime runtime = RequireRuntime();
        uint? target = GetSelectedOrClosestTarget(runtime, allowAutoTarget);
        if (target is null || !TryGetSession(out WorldSession? session))
            return false;

        power = Math.Clamp(power, 0f, 1f);
        if (runtime.ActionOwner.Combat.CurrentMode == CombatMode.Missile)
            session!.SendMissileAttack(target.Value, height, power);
        else
            session!.SendMeleeAttack(target.Value, height, power);
        return true;
    }

    public void SendCancelAttack()
    {
        if (TryGetSession(out WorldSession? session))
            session!.SendCancelAttack();
    }

    public bool IsDualWield =>
        RequireRuntime().MovementOwner.IsDualWield;
    public bool PlayerReadyForAttack
    {
        get
        {
            GameRuntime runtime = RequireRuntime();
            return runtime.MovementOwner.IsReadyForAttack(
                runtime.ActionOwner.Combat.CurrentMode);
        }
    }
    public bool AutoRepeatAttack =>
        RequireRuntime().CharacterOwner.Options.GetOptionBit(CharacterOptionId.AutoRepeatAttack);
    public bool AutoTarget =>
        RequireRuntime().CharacterOwner.Options.GetOptionBit(CharacterOptionId.AutoTarget);

    // Auto-target means the same thing here as it does under a window: one
    // owner answers for both hosts.
    public uint? SelectClosestTarget() =>
        RuntimeAttackTargetResolver.SelectClosest(RequireRuntime());

    public bool IsInWorld => _runtime?.Session.IsInWorld == true;
    public IReadOnlyList<ClientObject> GetOrderedEquipment()
    {
        GameRuntime runtime = RequireRuntime();
        return runtime.InventoryOwner.Objects.GetEquippedBy(
            runtime.PlayerIdentity.ServerGuid);
    }

    // A stance the player asked for stands down whatever equipment switch
    // the item owner has in flight. There is one such owner per client, the
    // runtime's, so this tells that one.
    public void NotifyExplicitCombatModeRequest()
    {
        _runtime?.ItemInteractionOwner.NotifyExplicitCombatModeRequest();
    }

    public void SendChangeCombatMode(CombatMode mode)
    {
        if (TryGetSession(out WorldSession? session))
            session!.SendChangeCombatMode(mode);
    }

    public uint LocalPlayerId =>
        _runtime?.PlayerIdentity.ServerGuid ?? 0u;
    public bool CanSend => TryGetSession(out _);

    public bool HasRequiredComponents(uint spellId)
    {
        if (_componentRequirements is { } requirements)
            return requirements.HasRequiredComponents(spellId);

        GameRuntime runtime = RequireRuntime();
        ClientObject? player = runtime.InventoryOwner.Objects.Get(
            runtime.PlayerIdentity.ServerGuid);
        return player is not null
            && !player.Properties.GetBool(
                SpellComponentRequirementService
                    .SpellComponentsRequiredProperty,
                true);
    }

    public bool IsTargetCompatible(
        uint targetId,
        SpellMetadata spell,
        bool showMessage)
    {
        GameRuntime runtime = RequireRuntime();
        ClientObject? target =
            runtime.InventoryOwner.Objects.Get(targetId);
        if (target is null)
            return false;
        SpellTargetPolicyResult result =
            RetailSpellTargetPolicy.Evaluate(
                runtime.PlayerIdentity.ServerGuid,
                target,
                spell);
        if (showMessage
            && !result.Allowed
            && result.Message is { } message)
        {
            DisplayMessage(message);
        }
        return result.Allowed;
    }

    public void StopCompletely() =>
        RequireRuntime().PrepareLocalPlayerForAttackRequest();

    public void SendUntargeted(uint spellId)
    {
        if (TryGetSession(out WorldSession? session))
            session!.SendCastUntargetedSpell(spellId);
    }

    public void SendTargeted(uint targetId, uint spellId)
    {
        if (TryGetSession(out WorldSession? session))
            session!.SendCastTargetedSpell(targetId, spellId);
    }

    public void DisplayMessage(string message) =>
        RequireRuntime().CommunicationOwner.Chat.OnSystemMessage(
            message,
            chatType: 0x00u);

    public void IncrementBusy() =>
        RequireRuntime().ActionOwner.Transactions
            .IncrementCastBusyCount();

    private static uint? GetSelectedOrClosestTarget(
        GameRuntime runtime,
        bool allowAutoTarget) =>
        RuntimeAttackTargetResolver.Resolve(runtime, allowAutoTarget).Target;

    private GameRuntime RequireRuntime() =>
        _runtime
        ?? throw new InvalidOperationException(
            "Windowless gameplay operations are not bound.");

    private bool TryGetSession(out WorldSession? session)
    {
        lock (_gate)
        {
            session = _session;
            return _route is not null
                && session is not null
                && IsInWorld;
        }
    }

    private void Activate(
        WorldSession session,
        SessionRoute route)
    {
        lock (_gate)
        {
            if (_route is not null)
            {
                throw new InvalidOperationException(
                    "Windowless gameplay operations already have an active session.");
            }
            _session = session;
            _route = route;
        }
    }

    private void Deactivate(SessionRoute route)
    {
        lock (_gate)
        {
            if (!ReferenceEquals(_route, route))
                return;
            _session = null;
            _route = null;
        }
    }
}
