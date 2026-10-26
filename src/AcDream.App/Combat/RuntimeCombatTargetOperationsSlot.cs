using AcDream.Runtime.Gameplay;

namespace AcDream.App.Combat;

internal sealed class RuntimeCombatTargetOperationsSlot
    : IRuntimeCombatTargetOperations
{
    private IRuntimeCombatTargetOperations? _owner;
    private IRuntimeCombatTargetOperations? _session;

    /// <summary>
    /// The session's own operations, answered whenever nothing else is
    /// bound: a session with no window attached plays through these. A
    /// window's operations, bound while it is attached, take precedence;
    /// when the window lets go, the session's operations answer again.
    /// </summary>
    public void UseSessionOperations(IRuntimeCombatTargetOperations session)
    {
        ArgumentNullException.ThrowIfNull(session);
        if (_session is not null && !ReferenceEquals(_session, session))
            throw new InvalidOperationException(
                "The session's own operations are already in use.");
        _session = session;
    }

    private IRuntimeCombatTargetOperations? Current => _owner ?? _session;

    public IDisposable BindOwned(IRuntimeCombatTargetOperations owner)
    {
        ArgumentNullException.ThrowIfNull(owner);
        if (_owner is not null)
            throw new InvalidOperationException(
                "Runtime combat-target operations are already bound.");
        _owner = owner;
        return new Binding(this, owner);
    }

    private void Unbind(IRuntimeCombatTargetOperations expected)
    {
        if (ReferenceEquals(_owner, expected))
            _owner = null;
    }

    public bool AutoTarget => Current?.AutoTarget == true;
    public uint? SelectClosestTarget() => Current?.SelectClosestTarget();

    private sealed class Binding : IDisposable
    {
        private RuntimeCombatTargetOperationsSlot? _slot;
        private readonly IRuntimeCombatTargetOperations _expected;

        public Binding(
            RuntimeCombatTargetOperationsSlot slot,
            IRuntimeCombatTargetOperations expected)
        {
            _slot = slot;
            _expected = expected;
        }

        public void Dispose() =>
            Interlocked.Exchange(ref _slot, null)?.Unbind(_expected);
    }
}

internal sealed class LiveCombatTargetOperations(
    Func<bool> autoTarget,
    Func<uint?> selectClosestTarget)
    : IRuntimeCombatTargetOperations
{
    private readonly Func<bool> _autoTarget = autoTarget
        ?? throw new ArgumentNullException(nameof(autoTarget));
    private readonly Func<uint?> _selectClosestTarget = selectClosestTarget
        ?? throw new ArgumentNullException(nameof(selectClosestTarget));

    public bool AutoTarget => _autoTarget();
    public uint? SelectClosestTarget() => _selectClosestTarget();
}
