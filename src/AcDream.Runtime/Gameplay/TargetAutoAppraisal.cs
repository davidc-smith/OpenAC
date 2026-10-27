using AcDream.Core.Combat;
using AcDream.Core.Items;
using AcDream.Core.Selection;
using AcDream.Core.Targeting;

namespace AcDream.Runtime.Gameplay;

/// <summary>
/// Identifies the selected monster, summon or corpse so the Target Deck can
/// show its stamina and mana: once on select, then again when something has
/// probably changed them (the player's hit lands on it, or its health moves),
/// never more often than <see cref="MinimumGapSeconds"/> per target. The
/// request goes out with the Automation origin, so the examination window is
/// never opened or switched by it.
/// </summary>
public sealed class TargetAutoAppraisal : IDisposable
{
    public const double MinimumGapSeconds = 2.0;

    private readonly SelectionState _selection;
    private readonly ClientObjectTable _objects;
    private readonly CombatState _combat;
    private readonly TargetAppraisalLog _log;
    private readonly Func<uint> _playerGuid;
    private readonly Func<uint, bool> _requestAppraisal;
    private readonly Func<double> _clock;
    private uint _target;
    private double _lastSentAt = double.NegativeInfinity;
    private bool _pending;
    private bool _disposed;

    public TargetAutoAppraisal(SelectionState selection, ClientObjectTable objects, CombatState combat,
        TargetAppraisalLog log, Func<uint> playerGuid, Func<uint, bool> requestAppraisal, Func<double> clock)
    {
        _selection = selection ?? throw new ArgumentNullException(nameof(selection));
        _objects = objects ?? throw new ArgumentNullException(nameof(objects));
        _combat = combat ?? throw new ArgumentNullException(nameof(combat));
        _log = log ?? throw new ArgumentNullException(nameof(log));
        _playerGuid = playerGuid ?? throw new ArgumentNullException(nameof(playerGuid));
        _requestAppraisal = requestAppraisal ?? throw new ArgumentNullException(nameof(requestAppraisal));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _selection.Changed += OnSelectionChanged;
        _combat.DamageDealtAccepted += OnDamageDealt;
        _combat.HealthChanged += OnHealthChanged;
        _objects.ObjectRemoved += OnObjectRemoved;
        Retarget(_selection.SelectedObjectId ?? 0u);
    }

    /// <summary>The object auto-ID is following; zero when none qualifies.</summary>
    public uint TargetId => _target;

    public static bool Qualifies(TargetKind kind) =>
        kind is TargetKind.Monster or TargetKind.Summon or TargetKind.Corpse;

    /// <summary>Sends a combined trigger once the gap has passed. Call every frame.</summary>
    public void Tick()
    {
        if (_pending && _target != 0u && _clock() - _lastSentAt >= MinimumGapSeconds)
            Send();
    }

    /// <summary>An identify reply arrived for <paramref name="objectId"/> (any origin).</summary>
    public void OnAppraised(uint objectId, bool success) =>
        _log.Record(objectId, success, _combat.GetHealthPercent(objectId), _clock());

    private void OnSelectionChanged(SelectionTransition _) => Retarget(_selection.SelectedObjectId ?? 0u);

    private void Retarget(uint selected)
    {
        _pending = false;
        _lastSentAt = double.NegativeInfinity;
        TargetKind kind = TargetKindClassifier.Classify(_objects.Get(selected), _playerGuid()).Kind;
        _target = selected != 0u && Qualifies(kind) ? selected : 0u;
        if (_target == 0u)
            return;
        if (_log.TryGet(_target, out TargetAppraisalEntry e) && e.Success && _clock() - e.At < MinimumGapSeconds)
            return;
        Trigger();
    }

    private void OnDamageDealt(CombatState.DamageDealt hit)
    {
        if (_target != 0u && _objects.Get(_target) is { } obj
            && string.Equals(obj.Name, hit.DefenderName, StringComparison.Ordinal))
            Trigger();
    }

    private void OnHealthChanged(uint guid, float percent)
    {
        if (guid != _target || _target == 0u)
            return;
        if (_log.TryGet(guid, out TargetAppraisalEntry e) && Math.Abs(e.HealthPercent - percent) < 0.0001f)
            return;
        Trigger();
    }

    private void OnObjectRemoved(ClientObject obj) => _log.Forget(obj.ObjectId);

    private void Trigger()
    {
        _pending = true;
        Tick();
    }

    private void Send()
    {
        _pending = false;
        _lastSentAt = _clock();
        _requestAppraisal(_target);
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _selection.Changed -= OnSelectionChanged;
        _combat.DamageDealtAccepted -= OnDamageDealt;
        _combat.HealthChanged -= OnHealthChanged;
        _objects.ObjectRemoved -= OnObjectRemoved;
    }
}
