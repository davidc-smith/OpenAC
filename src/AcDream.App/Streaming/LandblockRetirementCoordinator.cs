using AcDream.Core.World;

namespace AcDream.App.Streaming;

[Flags]
public enum LandblockRetirementStage : ushort
{
    None = 0,
    MeshReferences = 1 << 0,
    StaticScripts = 1 << 1,
    Classification = 1 << 2,
    EntityLighting = 1 << 3,
    EntityTranslucency = 1 << 4,
    PluginProjection = 1 << 5,
    Physics = 1 << 6,
    Terrain = 1 << 7,
    CellVisibility = 1 << 8,
    BuildingRegistry = 1 << 9,
    EnvironmentCells = 1 << 10,
    LegacyPresentation = 1 << 11,
    /// <summary>
    /// The landblock's static render projections leave the render scene. A
    /// ticket carries this stage only when the whole window was detached at
    /// once (an origin recenter); it then runs before every other stage of
    /// the ticket, budgeted like them, instead of all landblocks' projections
    /// leaving in the frame that detached them.
    /// </summary>
    StaticProjections = 1 << 12,
}

internal enum LandblockRetirementOperationResult : byte
{
    NoWork,
    Progressed,
    Pending,
    Failed,
}

public sealed class LandblockRetirementTicket
{
    private readonly Dictionary<LandblockRetirementStage, int> _entityCursors = new();
    private readonly Dictionary<LandblockRetirementStage, Exception> _failures = new();
    private Exception? _callbackFailure;
    private Exception? _lastReportedFailure;

    internal LandblockRetirementTicket(
        GpuLandblockRetirement state,
        LandblockRetirementStage requiredStages)
    {
        State = state;
        RequiredStages = requiredStages;
    }

    public GpuLandblockRetirement State { get; }
    public uint LandblockId => State.LandblockId;
    public LandblockRetirementKind Kind => State.Kind;
    public IReadOnlyList<WorldEntity> Entities => State.Entities;
    public LandblockRetirementStage RequiredStages { get; }
    public LandblockRetirementStage CompletedStages { get; private set; }
    public bool IsComplete =>
        (CompletedStages & RequiredStages) == RequiredStages
        && _callbackFailure is null;

    public bool RunOnce(LandblockRetirementStage stage, Action operation)
    {
        ValidateSingleStage(stage);
        ArgumentNullException.ThrowIfNull(operation);
        if ((CompletedStages & stage) != 0)
            return true;

        try
        {
            operation();
            CompletedStages |= stage;
            _failures.Remove(stage);
            return true;
        }
        catch (Exception error)
        {
            _failures[stage] = error;
            return false;
        }
    }

    public bool RunOnce(LandblockRetirementStage stage, Func<bool> operation)
    {
        ValidateSingleStage(stage);
        ArgumentNullException.ThrowIfNull(operation);
        if ((CompletedStages & stage) != 0)
            return true;

        try
        {
            if (!operation())
                return false;
            CompletedStages |= stage;
            _failures.Remove(stage);
            return true;
        }
        catch (Exception error)
        {
            _failures[stage] = error;
            return false;
        }
    }

    internal LandblockRetirementOperationResult RunOnceStep(
        LandblockRetirementStage stage,
        Action operation)
    {
        ValidateSingleStage(stage);
        ArgumentNullException.ThrowIfNull(operation);
        if ((CompletedStages & stage) != 0)
            return LandblockRetirementOperationResult.NoWork;

        try
        {
            operation();
            CompletedStages |= stage;
            _failures.Remove(stage);
            return LandblockRetirementOperationResult.Progressed;
        }
        catch (Exception error)
        {
            _failures[stage] = error;
            return LandblockRetirementOperationResult.Failed;
        }
    }

    internal LandblockRetirementOperationResult RunOnceStep(
        LandblockRetirementStage stage,
        Func<bool> operation)
    {
        ValidateSingleStage(stage);
        ArgumentNullException.ThrowIfNull(operation);
        if ((CompletedStages & stage) != 0)
            return LandblockRetirementOperationResult.NoWork;

        try
        {
            if (!operation())
                return LandblockRetirementOperationResult.Pending;

            CompletedStages |= stage;
            _failures.Remove(stage);
            return LandblockRetirementOperationResult.Progressed;
        }
        catch (Exception error)
        {
            _failures[stage] = error;
            return LandblockRetirementOperationResult.Failed;
        }
    }

    public bool RunForEachEntity(
        LandblockRetirementStage stage,
        Func<WorldEntity, bool> predicate,
        Action<WorldEntity> operation)
    {
        ValidateSingleStage(stage);
        ArgumentNullException.ThrowIfNull(predicate);
        ArgumentNullException.ThrowIfNull(operation);
        if ((CompletedStages & stage) != 0)
            return true;

        _entityCursors.TryGetValue(stage, out int cursor);
        while (cursor < Entities.Count)
        {
            WorldEntity entity = Entities[cursor];
            if (predicate(entity))
            {
                try
                {
                    operation(entity);
                }
                catch (Exception error)
                {
                    _entityCursors[stage] = cursor;
                    _failures[stage] = error;
                    return false;
                }
            }

            cursor++;
            _entityCursors[stage] = cursor;
        }

        CompletedStages |= stage;
        _entityCursors.Remove(stage);
        _failures.Remove(stage);
        return true;
    }

    internal LandblockRetirementOperationResult RunEntityStep(
        LandblockRetirementStage stage,
        Func<WorldEntity, bool> predicate,
        Action<WorldEntity> operation)
    {
        ValidateSingleStage(stage);
        ArgumentNullException.ThrowIfNull(predicate);
        ArgumentNullException.ThrowIfNull(operation);
        if ((CompletedStages & stage) != 0)
            return LandblockRetirementOperationResult.NoWork;

        _entityCursors.TryGetValue(stage, out int cursor);
        if (cursor >= Entities.Count)
        {
            CompletedStages |= stage;
            _entityCursors.Remove(stage);
            _failures.Remove(stage);
            return LandblockRetirementOperationResult.Progressed;
        }

        WorldEntity entity = Entities[cursor];
        if (predicate(entity))
        {
            try
            {
                operation(entity);
            }
            catch (Exception error)
            {
                _entityCursors[stage] = cursor;
                _failures[stage] = error;
                return LandblockRetirementOperationResult.Failed;
            }
        }

        cursor++;
        if (cursor == Entities.Count)
        {
            CompletedStages |= stage;
            _entityCursors.Remove(stage);
            _failures.Remove(stage);
        }
        else
        {
            _entityCursors[stage] = cursor;
        }

        return LandblockRetirementOperationResult.Progressed;
    }

    internal LandblockRetirementStage NextIncompleteStage
    {
        get
        {
            ushort remaining = (ushort)(RequiredStages & ~CompletedStages);
            if (remaining == 0)
                return LandblockRetirementStage.None;
            // What the render scene still holds of the landblock leaves
            // before the owners its projections draw with are released.
            if ((remaining & (ushort)LandblockRetirementStage.StaticProjections) != 0)
                return LandblockRetirementStage.StaticProjections;

            ushort lowest = (ushort)(remaining & (ushort)-(short)remaining);
            return (LandblockRetirementStage)lowest;
        }
    }

    /// <summary>
    /// The meter's label for one of this ticket's stages, made once per
    /// stage: a stage that walks the landblock's entities reserves once per
    /// entity, and a label per reservation was garbage per entity.
    /// </summary>
    internal string StageLabel(LandblockRetirementStage stage)
    {
        int index = System.Numerics.BitOperations.TrailingZeroCount(
            (uint)(ushort)stage);
        _stageLabels ??= new string?[16];
        return _stageLabels[index] ??= $"retire-{stage}-0x{LandblockId:X8}";
    }

    private string?[]? _stageLabels;

    internal bool RequiresStage(LandblockRetirementStage stage) =>
        (RequiredStages & stage) == stage
        && (CompletedStages & stage) == 0;

    internal void BeginAttempt() => _callbackFailure = null;

    internal void RecordCallbackFailure(Exception error) =>
        _callbackFailure = error;

    internal bool TryTakeNewFailure(out Exception? error)
    {
        error = _callbackFailure ?? _failures.Values.FirstOrDefault();
        if (error is null || ReferenceEquals(error, _lastReportedFailure))
            return false;

        _lastReportedFailure = error;
        return true;
    }

    private static void ValidateSingleStage(LandblockRetirementStage stage)
    {
        ushort value = (ushort)stage;
        if (value == 0 || (value & (value - 1)) != 0)
            throw new ArgumentOutOfRangeException(
                nameof(stage),
                stage,
                "A retirement operation must own exactly one stage bit.");
    }
}

public sealed class LandblockRetirementCoordinator
{
    private enum BudgetedAdvanceResult : byte
    {
        Progressed,
        Yielded,
        Failed,
    }

    private enum RequestKind : byte
    {
        BeginFull,
        BeginNearLayer,
        Advance,
    }

    private readonly record struct Request(RequestKind Kind, uint LandblockId);

    private const LandblockRetirementStage CoreStages =
        LandblockRetirementStage.MeshReferences
        | LandblockRetirementStage.StaticScripts
        | LandblockRetirementStage.Classification;

    public const LandblockRetirementStage ProductionPresentationStages =
        LandblockRetirementStage.EntityLighting
        | LandblockRetirementStage.EntityTranslucency
        | LandblockRetirementStage.PluginProjection
        | LandblockRetirementStage.Terrain
        | LandblockRetirementStage.CellVisibility
        | LandblockRetirementStage.BuildingRegistry
        | LandblockRetirementStage.EnvironmentCells;

    private readonly GpuWorldState _state;
    private readonly Action<LandblockRetirementTicket> _advancePresentation;
    private readonly Func<
        LandblockRetirementTicket,
        LandblockRetirementOperationResult>? _advancePresentationStep;
    private readonly Func<LandblockRetirementKind, LandblockRetirementStage>
        _requiredPresentationStages;
    private readonly Action<GpuLandblockRetirement>? _onDetached;
    private readonly Dictionary<uint, List<LandblockRetirementTicket>> _pending = new();
    private readonly LinkedList<LandblockRetirementTicket> _pendingOrder = new();
    private readonly Dictionary<
        LandblockRetirementTicket,
        LinkedListNode<LandblockRetirementTicket>> _pendingNodes =
            new(ReferenceEqualityComparer.Instance);
    private readonly List<uint> _completedIds = new();
    private readonly Queue<Request> _requests = new();
    private readonly HashSet<Request> _seenDuringDrain = new();
    private bool _drainingRequests;

    public LandblockRetirementCoordinator(
        GpuWorldState state,
        Action<LandblockRetirementTicket> advancePresentation,
        Func<LandblockRetirementKind, LandblockRetirementStage>? requiredPresentationStages = null)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(advancePresentation);
        _state = state;
        _advancePresentation = advancePresentation;
        _advancePresentationStep = null;
        _onDetached = null;
        _requiredPresentationStages = requiredPresentationStages
            ?? (kind => kind == LandblockRetirementKind.Full
                ? ProductionPresentationStages
                : ProductionPresentationStages & ~LandblockRetirementStage.Terrain);
    }

    private LandblockRetirementCoordinator(
        GpuWorldState state,
        Func<LandblockRetirementTicket, LandblockRetirementOperationResult>
            advancePresentationStep,
        Action<LandblockRetirementTicket> advancePresentation,
        Func<LandblockRetirementKind, LandblockRetirementStage>
            requiredPresentationStages,
        Action<GpuLandblockRetirement>? onDetached)
    {
        _state = state ?? throw new ArgumentNullException(nameof(state));
        _advancePresentationStep = advancePresentationStep
            ?? throw new ArgumentNullException(nameof(advancePresentationStep));
        _advancePresentation = advancePresentation
            ?? throw new ArgumentNullException(nameof(advancePresentation));
        _requiredPresentationStages = requiredPresentationStages
            ?? throw new ArgumentNullException(nameof(requiredPresentationStages));
        _onDetached = onDetached;
    }

    public int PendingCount { get; private set; }

    /// <summary>
    /// Tickets whose static render projections have not yet left the render
    /// scene (see <see cref="LandblockRetirementStage.StaticProjections"/>).
    /// </summary>
    internal int PendingStaticProjectionRetirements { get; private set; }

    public bool IsPending(uint landblockId) =>
        _pending.ContainsKey(Canonicalize(landblockId));

    internal bool MatchesState(GpuWorldState state) =>
        ReferenceEquals(_state, state);
    internal bool UsesBudgetedSteps => _advancePresentationStep is not null;

    internal Exception? AdoptDetachedFull(
        IReadOnlyList<GpuLandblockRetirement> retirements)
    {
        ArgumentNullException.ThrowIfNull(retirements);
        var incomingIds = new HashSet<uint>();
        for (int index = 0; index < retirements.Count; index++)
        {
            GpuLandblockRetirement retirement = retirements[index];
            if (retirement.Kind != LandblockRetirementKind.Full)
            {
                throw new ArgumentException(
                    "Origin-recenter adoption accepts only full retirements.",
                    nameof(retirements));
            }

            uint canonical = Canonicalize(retirement.LandblockId);
            if (!incomingIds.Add(canonical))
            {
                throw new ArgumentException(
                    $"Origin-recenter receipts contain duplicate landblock " +
                    $"0x{canonical:X8}.",
                    nameof(retirements));
            }
            if (_pending.TryGetValue(
                    canonical,
                    out List<LandblockRetirementTicket>? existing)
                && existing.Any(
                    static ticket =>
                        ticket.Kind == LandblockRetirementKind.Full))
            {
                throw new InvalidOperationException(
                    $"Landblock 0x{canonical:X8} already has a full " +
                    "retirement receipt.");
            }
        }

        // The render scene's static projections of every detached landblock
        // leave as budgeted ticket steps, not all in the detaching frame.
        bool deferProjections =
            _advancePresentationStep is not null && _onDetached is not null;
        LandblockRetirementStage required =
            CoreStages
            | _requiredPresentationStages(LandblockRetirementKind.Full);
        if (deferProjections)
            required |= LandblockRetirementStage.StaticProjections;
        List<Exception>? failures = null;
        for (int index = 0; index < retirements.Count; index++)
        {
            GpuLandblockRetirement retirement = retirements[index];
            uint canonical = Canonicalize(retirement.LandblockId);
            _pending.TryGetValue(
                canonical,
                out List<LandblockRetirementTicket>? existing);

            var ticket = new LandblockRetirementTicket(
                retirement,
                required);
            if (existing is null)
            {
                existing = new List<LandblockRetirementTicket>(1);
                _pending.Add(canonical, existing);
            }
            existing.Add(ticket);
            PendingCount++;
            if (_advancePresentationStep is not null)
                EnqueueBudgetedTicket(ticket);

            if (deferProjections)
            {
                PendingStaticProjectionRetirements++;
            }
            else
            {
                try
                {
                    _onDetached?.Invoke(retirement);
                }
                catch (Exception error)
                {
                    (failures ??= []).Add(error);
                }
            }

            if (_advancePresentationStep is null)
            {
                AdvanceTicket(ticket);
                if (ticket.IsComplete)
                {
                    existing.Remove(ticket);
                    PendingCount--;
                    if (existing.Count == 0)
                        _pending.Remove(canonical);
                }
            }
        }

        return failures switch
        {
            null => null,
            { Count: 1 } => failures[0],
            _ => new AggregateException(
                "One or more detached landblock observers failed.",
                failures),
        };
    }

    public void BeginFull(uint landblockId) => EnqueueRequest(
        new Request(RequestKind.BeginFull, Canonicalize(landblockId)));

    public void BeginNearLayer(uint landblockId) => EnqueueRequest(
        new Request(RequestKind.BeginNearLayer, Canonicalize(landblockId)));

    public void Advance() => EnqueueRequest(
        new Request(RequestKind.Advance, 0u));

    internal static LandblockRetirementCoordinator CreateBudgeted(
        GpuWorldState state,
        Func<LandblockRetirementTicket, LandblockRetirementOperationResult>
            advancePresentationStep,
        Action<LandblockRetirementTicket> advancePresentation,
        Action<GpuLandblockRetirement>? onDetached = null)
    {
        return new LandblockRetirementCoordinator(
            state,
            advancePresentationStep,
            advancePresentation,
            kind => kind == LandblockRetirementKind.Full
                ? ProductionPresentationStages
                : ProductionPresentationStages & ~LandblockRetirementStage.Terrain,
            onDetached);
    }

    public void Advance(StreamingWorkMeter meter)
    {
        ArgumentNullException.ThrowIfNull(meter);
        if (_advancePresentationStep is null)
        {
            Advance();
            return;
        }

        while (_pendingOrder.First is { } head)
        {
            LandblockRetirementTicket ticket = head.Value;
            if (ticket.IsComplete)
            {
                RemoveCompletedTicket(ticket);
                continue;
            }

            if (AdvanceBudgetedTicket(ticket, meter)
                != BudgetedAdvanceResult.Progressed)
                return;
            if (ticket.IsComplete)
                RemoveCompletedTicket(ticket);
        }
    }

    internal void AdvancePriority(uint landblockId, StreamingWorkMeter meter)
    {
        ArgumentNullException.ThrowIfNull(meter);
        if (_advancePresentationStep is null)
        {
            Advance();
            return;
        }

        uint canonical = Canonicalize(landblockId);
        while (_pending.TryGetValue(
            canonical,
            out List<LandblockRetirementTicket>? tickets))
        {
            if (tickets.Count == 0)
                throw new InvalidOperationException(
                    "A pending retirement owner has no tickets.");

            LandblockRetirementTicket ticket = tickets[0];
            if (!ticket.IsComplete
                && AdvanceBudgetedTicket(ticket, meter)
                    != BudgetedAdvanceResult.Progressed)
            {
                return;
            }

            if (ticket.IsComplete)
                RemoveCompletedTicket(ticket);
        }
    }

    private void AdvanceCore()
    {
        if (_advancePresentationStep is not null)
        {
            AdvanceBudgetedEagerAttempt();
            return;
        }

        if (_pending.Count == 0)
            return;

        _completedIds.Clear();
        foreach ((uint id, List<LandblockRetirementTicket> tickets) in _pending)
        {
            for (int i = tickets.Count - 1; i >= 0; i--)
            {
                LandblockRetirementTicket ticket = tickets[i];
                AdvanceTicket(ticket);
                if (!ticket.IsComplete)
                    continue;

                tickets.RemoveAt(i);
                PendingCount--;
            }

            if (tickets.Count == 0)
                _completedIds.Add(id);
        }

        for (int i = 0; i < _completedIds.Count; i++)
            _pending.Remove(_completedIds[i]);
    }

    internal static LandblockRetirementCoordinator CreateLegacy(
        GpuWorldState state,
        Action<uint>? removeTerrain,
        Action<uint>? demoteNearLayer)
    {
        LandblockRetirementStage Required(LandblockRetirementKind kind) =>
            kind switch
            {
                LandblockRetirementKind.Full when removeTerrain is not null =>
                    LandblockRetirementStage.LegacyPresentation,
                LandblockRetirementKind.NearLayer when demoteNearLayer is not null =>
                    LandblockRetirementStage.LegacyPresentation,
                _ => LandblockRetirementStage.None,
            };

        void AdvanceLegacy(LandblockRetirementTicket ticket)
        {
            Action<uint>? callback = ticket.Kind == LandblockRetirementKind.Full
                ? removeTerrain
                : demoteNearLayer;
            if (callback is not null)
            {
                ticket.RunOnce(
                    LandblockRetirementStage.LegacyPresentation,
                    () => callback(ticket.LandblockId));
            }
        }

        return new LandblockRetirementCoordinator(state, AdvanceLegacy, Required);
    }

    private void BeginCore(uint landblockId, LandblockRetirementKind kind)
    {
        uint canonical = Canonicalize(landblockId);
        if (_pending.TryGetValue(canonical, out List<LandblockRetirementTicket>? existing))
        {
            for (int i = 0; i < existing.Count; i++)
            {
                if (existing[i].Kind == kind)
                {
                    LandblockRetirementTicket existingTicket = existing[i];
                    if (_advancePresentationStep is not null)
                        return;

                    AdvanceTicket(existingTicket);
                    if (existingTicket.IsComplete)
                    {
                        existing.RemoveAt(i);
                        PendingCount--;
                        if (existing.Count == 0)
                            _pending.Remove(canonical);
                    }
                    return;
                }
            }
        }

        LandblockRetirementStage required =
            CoreStages | _requiredPresentationStages(kind);
        GpuLandblockRetirement? stateRetirement = null;
        Exception? detachmentObserverFailure = null;
        GpuWorldState.MutationBatch mutation = _state.BeginMutationBatch();
        try
        {
            stateRetirement = kind == LandblockRetirementKind.Full
                ? _state.DetachLandblock(canonical)
                : _state.DetachNearLayer(canonical);
        }
        finally
        {
            try
            {
                mutation.Dispose();
            }
            catch (Exception error)
            {
                detachmentObserverFailure = error;
            }
        }
        if (stateRetirement is null)
        {
            if (detachmentObserverFailure is not null)
                throw detachmentObserverFailure;
            return;
        }

        var ticket = new LandblockRetirementTicket(stateRetirement, required);
        if (existing is null)
        {
            existing = new List<LandblockRetirementTicket>(1);
            _pending.Add(canonical, existing);
        }
        existing.Add(ticket);
        PendingCount++;
        if (_advancePresentationStep is not null)
            EnqueueBudgetedTicket(ticket);

        _onDetached?.Invoke(stateRetirement);

        if (_advancePresentationStep is null)
        {
            AdvanceTicket(ticket);
            if (ticket.IsComplete)
            {
                existing.Remove(ticket);
                PendingCount--;
                if (existing.Count == 0)
                    _pending.Remove(canonical);
            }
        }
        if (detachmentObserverFailure is not null)
            throw detachmentObserverFailure;
    }

    private void AdvanceTicket(LandblockRetirementTicket ticket)
    {
        ticket.BeginAttempt();
        // Nothing the projections draw with is released while the render
        // scene still holds them.
        if (!ticket.RequiresStage(LandblockRetirementStage.StaticProjections)
            || RetireStaticProjections(ticket)
                != LandblockRetirementOperationResult.Failed)
        {
            ticket.RunOnce(
                LandblockRetirementStage.MeshReferences,
                () => _state.ReleaseLandblockMeshReferences(ticket.LandblockId));
            ticket.RunForEachEntity(
                LandblockRetirementStage.StaticScripts,
                IsStatic,
                _stopStaticEntityScript ??= _state.StopStaticEntityScript);
            ticket.RunOnce(
                LandblockRetirementStage.Classification,
                () => _state.InvalidateLandblockClassification(ticket.LandblockId));

            try
            {
                _advancePresentation(ticket);
            }
            catch (Exception error)
            {
                ticket.RecordCallbackFailure(error);
            }
        }

        if (ticket.TryTakeNewFailure(out Exception? failure))
        {
            Console.WriteLine(
                $"streaming: retirement for 0x{ticket.LandblockId:X8} " +
                $"({ticket.Kind}) remains pending: {failure}");
        }
    }

    private void AdvanceBudgetedEagerAttempt()
    {
        if (_pendingOrder.First is not { } head)
            return;
        LandblockRetirementTicket ticket = head.Value;

        if (ticket.IsComplete)
        {
            RemoveCompletedTicket(ticket);
            return;
        }

        AdvanceTicket(ticket);
        if (ticket.IsComplete)
            RemoveCompletedTicket(ticket);
    }

    private static readonly Func<WorldEntity, bool> IsStatic =
        static entity => entity.ServerGuid == 0;
    private Action<WorldEntity>? _stopStaticEntityScript;

    private LandblockRetirementOperationResult RetireStaticProjections(
        LandblockRetirementTicket ticket)
    {
        Action<GpuLandblockRetirement> retire = _onDetached
            ?? throw new InvalidOperationException(
                "A deferred static-projection retirement has no owner.");
        LandblockRetirementOperationResult result = ticket.RunOnceStep(
            LandblockRetirementStage.StaticProjections,
            () => retire(ticket.State));
        if (result == LandblockRetirementOperationResult.Progressed)
            PendingStaticProjectionRetirements--;
        return result;
    }

    /// <summary>
    /// Advances, in ticket order and within the meter, the static-projection
    /// stage of every ticket that still has it, and nothing else. True once
    /// no ticket's projections remain in the render scene.
    /// </summary>
    internal bool AdvanceStaticProjectionRetirements(StreamingWorkMeter meter)
    {
        ArgumentNullException.ThrowIfNull(meter);
        for (LinkedListNode<LandblockRetirementTicket>? node = _pendingOrder.First;
             node is not null && PendingStaticProjectionRetirements != 0;
             node = node.Next)
        {
            LandblockRetirementTicket ticket = node.Value;
            if (!ticket.RequiresStage(LandblockRetirementStage.StaticProjections))
                continue;
            if (AdvanceBudgetedTicket(ticket, meter)
                != BudgetedAdvanceResult.Progressed)
            {
                return false;
            }
        }
        return PendingStaticProjectionRetirements == 0;
    }

    private LandblockRetirementOperationResult AdvanceTicketOne(
        LandblockRetirementTicket ticket)
    {
        ticket.BeginAttempt();
        LandblockRetirementStage stage = ticket.NextIncompleteStage;
        LandblockRetirementOperationResult result;
        try
        {
            result = stage switch
            {
                LandblockRetirementStage.StaticProjections =>
                    RetireStaticProjections(ticket),
                LandblockRetirementStage.MeshReferences =>
                    ticket.RunOnceStep(
                        stage,
                        () => _state.ReleaseLandblockMeshReferences(
                            ticket.LandblockId)),
                LandblockRetirementStage.StaticScripts =>
                    ticket.RunEntityStep(
                        stage,
                        IsStatic,
                        _stopStaticEntityScript ??= _state.StopStaticEntityScript),
                LandblockRetirementStage.Classification =>
                    ticket.RunOnceStep(
                        stage,
                        () => _state.InvalidateLandblockClassification(
                            ticket.LandblockId)),
                LandblockRetirementStage.None =>
                    LandblockRetirementOperationResult.NoWork,
                _ => _advancePresentationStep!(ticket),
            };
        }
        catch (Exception error)
        {
            ticket.RecordCallbackFailure(error);
            result = LandblockRetirementOperationResult.Failed;
        }

        if (ticket.TryTakeNewFailure(out Exception? failure))
        {
            Console.WriteLine(
                $"streaming: retirement for 0x{ticket.LandblockId:X8} " +
                $"({ticket.Kind}) remains pending: {failure}");
        }

        return result;
    }

    private BudgetedAdvanceResult AdvanceBudgetedTicket(
        LandblockRetirementTicket ticket,
        StreamingWorkMeter meter)
    {
        LandblockRetirementStage stage = ticket.NextIncompleteStage;
        if (stage == LandblockRetirementStage.None)
        {
            throw new InvalidOperationException(
                "An incomplete retirement ticket has no unfinished stage.");
        }

        // Retiring a landblock's static projections is, per entity, one
        // unregistration here and one scene removal when the frame's journal
        // drains: two entity operations each.
        int projectionOperations = Math.Max(1, ticket.Entities.Count * 2);
        if (stage == LandblockRetirementStage.StaticProjections)
        {
            // The scene removals run when the frame's journal drains, outside
            // this meter, so a reveal's widened budget must not widen them:
            // a frame retires at most an ordinary frame's entity operations
            // of projections (always one landblock's).
            if (meter.FrameSerial != _projectionFrameSerial)
            {
                _projectionFrameSerial = meter.FrameSerial;
                _projectionOperationsThisFrame = 0;
            }
            if (_projectionOperationsThisFrame != 0
                && _projectionOperationsThisFrame + projectionOperations
                    > StaticProjectionOperationsPerFrame)
            {
                return BudgetedAdvanceResult.Yielded;
            }
        }
        StreamingWorkCost cost = new(
            EntityOperations:
                stage == LandblockRetirementStage.StaticProjections
                    ? projectionOperations
                    : 1,
            GlRetireOperations:
                stage is LandblockRetirementStage.MeshReferences
                    or LandblockRetirementStage.Terrain
                    ? 1
                    : 0);
        StreamingWorkAdmission admission = meter.TryReserve(
            cost,
            ticket.StageLabel(stage));
        if (admission == StreamingWorkAdmission.Yielded)
            return BudgetedAdvanceResult.Yielded;

        LandblockRetirementOperationResult result = AdvanceTicketOne(ticket);
        if (result == LandblockRetirementOperationResult.Failed)
        {
            meter.Fail();
            return BudgetedAdvanceResult.Failed;
        }

        meter.Complete();
        if (stage == LandblockRetirementStage.StaticProjections)
            _projectionOperationsThisFrame += projectionOperations;
        if (result == LandblockRetirementOperationResult.Pending)
            return BudgetedAdvanceResult.Yielded;
        return BudgetedAdvanceResult.Progressed;
    }

    /// <summary>The most static-projection entity operations one frame
    /// retires: an ordinary frame's entity budget.</summary>
    internal static readonly int StaticProjectionOperationsPerFrame =
        StreamingWorkBudgetOptions.Default.MaxEntityOperations;

    private long _projectionFrameSerial;
    private int _projectionOperationsThisFrame;

    private void EnqueueBudgetedTicket(LandblockRetirementTicket ticket)
    {
        LinkedListNode<LandblockRetirementTicket> node =
            _pendingOrder.AddLast(ticket);
        if (!_pendingNodes.TryAdd(ticket, node))
        {
            _pendingOrder.Remove(node);
            throw new InvalidOperationException(
                "A retirement ticket cannot enter the budgeted FIFO twice.");
        }
    }

    private void RemoveCompletedTicket(LandblockRetirementTicket ticket)
    {
        if (!_pendingNodes.Remove(
                ticket,
                out LinkedListNode<LandblockRetirementTicket>? node))
        {
            throw new InvalidOperationException(
                "Completed retirement ticket is missing from its budgeted order.");
        }
        _pendingOrder.Remove(node);

        uint id = Canonicalize(ticket.LandblockId);
        if (!_pending.TryGetValue(id, out List<LandblockRetirementTicket>? tickets)
            || !tickets.Remove(ticket))
        {
            throw new InvalidOperationException(
                "Completed retirement ticket is missing from its owner ledger.");
        }

        PendingCount--;
        if (tickets.Count == 0)
            _pending.Remove(id);
    }

    private static uint Canonicalize(uint id) =>
        (id & 0xFFFF0000u) | 0xFFFFu;

    private void EnqueueRequest(Request request)
    {
        if (_drainingRequests)
        {
            if (_seenDuringDrain.Add(request))
                _requests.Enqueue(request);
            return;
        }

        _drainingRequests = true;
        _seenDuringDrain.Clear();
        _requests.Clear();
        _seenDuringDrain.Add(request);
        _requests.Enqueue(request);
        List<Exception>? failures = null;
        try
        {
            while (_requests.TryDequeue(out Request pending))
            {
                try
                {
                    switch (pending.Kind)
                    {
                        case RequestKind.BeginFull:
                            BeginCore(
                                pending.LandblockId,
                                LandblockRetirementKind.Full);
                            break;
                        case RequestKind.BeginNearLayer:
                            BeginCore(
                                pending.LandblockId,
                                LandblockRetirementKind.NearLayer);
                            break;
                        case RequestKind.Advance:
                            AdvanceCore();
                            break;
                        default:
                            throw new InvalidOperationException(
                                $"Unknown retirement request {pending.Kind}.");
                    }
                }
                catch (Exception error)
                {
                    (failures ??= new List<Exception>()).Add(error);
                }
            }
        }
        finally
        {
            _requests.Clear();
            _seenDuringDrain.Clear();
            _drainingRequests = false;
        }

        if (failures is { Count: 1 })
            System.Runtime.ExceptionServices.ExceptionDispatchInfo
                .Capture(failures[0])
                .Throw();
        if (failures is { Count: > 1 })
        {
            throw new AggregateException(
                "One or more serialized landblock retirement requests failed.",
                failures);
        }
    }
}
