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

    /// <summary>
    /// How long to wait before asking again after a failed reply. ACE answers
    /// a repeat request for a target whose assess has not succeeded, inside 5 s,
    /// with a failure and no new roll, so an earlier retry is wasted.
    /// </summary>
    public const double FailedRetrySeconds = 5.5;

    private readonly SelectionState _selection;
    private readonly ClientObjectTable _objects;
    private readonly CombatState _combat;
    private readonly TargetAppraisalLog _log;
    private readonly Func<uint> _playerGuid;
    private readonly Func<uint, bool> _requestAppraisal;
    private readonly Func<double> _clock;
    private uint _target;
    private readonly Dictionary<uint, double> _lastSentAt = new();
    private readonly Dictionary<uint, double> _notBefore = new();
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
        if (_pending && _target != 0u && GapElapsed())
            Send();
    }

    /// <summary>
    /// Starts over from the current selection, as a fresh select would. The
    /// plate calls it when it is shown, since selections made while it was
    /// hidden may have been refused.
    /// </summary>
    public void Resync() => Retarget(_selection.SelectedObjectId ?? 0u);

    /// <summary>An identify reply arrived for <paramref name="objectId"/> (any origin).</summary>
    public void OnAppraised(uint objectId, bool success)
    {
        _log.Record(objectId, success, _combat.GetHealthPercent(objectId), _clock());
        // The reply is current, so a trigger still waiting for the target is
        // answered by it. This also drops the one the reply's own creature
        // profile raised through HealthChanged just before it was logged.
        if (success)
            _notBefore.Remove(objectId);
        else
            _notBefore[objectId] = _clock() + FailedRetrySeconds;
        // A failure keeps the target pending, so it is retried once the server
        // would roll again, with no hit or health change needed to prompt it.
        if (objectId == _target)
            _pending = !success;
    }

    private void OnSelectionChanged(SelectionTransition _) => Retarget(_selection.SelectedObjectId ?? 0u);

    private void Retarget(uint selected)
    {
        _pending = false;
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
        // Pending only, never sent from here: an identify reply raises this
        // before it is logged, and OnAppraised then drops the trigger before
        // the next frame's Tick could send it.
        _pending = true;
    }

    // The gap is kept per target and survives a change of selection, so
    // flicking between two targets cannot send more often than the gap.
    private bool GapElapsed() =>
        (!_lastSentAt.TryGetValue(_target, out double at) || _clock() - at >= MinimumGapSeconds)
        && (!_notBefore.TryGetValue(_target, out double notBefore) || _clock() >= notBefore);

    private void OnObjectRemoved(ClientObject obj)
    {
        _log.Forget(obj.ObjectId);
        _lastSentAt.Remove(obj.ObjectId);
        _notBefore.Remove(obj.ObjectId);
    }

    private void Trigger()
    {
        _pending = true;
        Tick();
    }

    // A refused request (no session, say) stays pending, so a later Tick retries it.
    private void Send()
    {
        if (!_requestAppraisal(_target))
            return;
        _pending = false;
        _lastSentAt[_target] = _clock();
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
