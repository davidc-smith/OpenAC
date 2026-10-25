using AcDream.App.Rendering.Wb;
using AcDream.App.Rendering.Scene;
using AcDream.Core.Terrain;
using AcDream.Core.World;

namespace AcDream.App.Streaming;

public readonly record struct LandblockPresentationDiagnostics(
    LandblockRenderPublisherDiagnostics Render,
    LandblockStaticPresentationDiagnostics Statics);

internal readonly record struct LandblockPublicationAdvance(
    bool Completed,
    bool Progressed);

public sealed class LandblockPresentationPipeline
{
    private enum PublicationKind : byte
    {
        Loaded,
        PromoteExisting,
        PromoteSelfContained,
        Far,
    }

    private sealed class PublicationTransaction
    {
        public required PublicationKind Kind;
        public required LandblockBuild Build;
        public required LandblockMeshData MeshData;
        public required uint LandblockId;
        public required LandblockStreamCostEstimate Cost;
        public LandblockStreamTier Tier;
        public bool PresentationCommitted;
        public LandblockRenderPublication? RenderPublication;
        public LandblockStaticPresentationPublication? StaticPublication;
        public bool SpatialCommitted;
        public GpuLandblockSpatialPublication? SpatialPublication;
        public bool SpatialPresentationCommitted;
        public bool EnvCellReplayCommitted;
        public bool LiveRecoveryCommitted;

        public PublicationStageTimings? Timing;
    }

    private readonly Action<LandblockBuild, LandblockMeshData>?
        _publishBeforeSpatialCommit;
    private readonly LandblockRenderPublisher? _renderPublisher;

    internal LandblockRenderPublisher? RenderPublisher => _renderPublisher;
    private readonly LandblockStaticPresentationPublisher? _staticPublisher;
    private readonly GpuWorldState _state;
    private readonly Action<uint>? _onLandblockLoaded;
    private readonly Action<EnvCellLandblockBuild>? _ensureEnvCellMeshes;
    private readonly IRenderStaticProjectionJournalSink? _staticProjectionSink;
    private readonly LandblockRetirementCoordinator _retirements;
    private readonly Dictionary<LandblockStreamResult, PublicationTransaction> _publications =
        new(ReferenceEqualityComparer.Instance);

    internal LandblockPresentationPipeline(
        Action<LandblockBuild, LandblockMeshData> publishBeforeSpatialCommit,
        GpuWorldState state,
        Action<uint>? onLandblockLoaded = null,
        Action<EnvCellLandblockBuild>? ensureEnvCellMeshes = null,
        LandblockRetirementCoordinator? retirementCoordinator = null,
        Action<uint>? removeTerrain = null,
        Action<uint>? demoteNearLayer = null,
        IRenderStaticProjectionJournalSink? staticProjectionSink = null)
    {
        ArgumentNullException.ThrowIfNull(publishBeforeSpatialCommit);
        ArgumentNullException.ThrowIfNull(state);

        _publishBeforeSpatialCommit = publishBeforeSpatialCommit;
        _state = state;
        _onLandblockLoaded = onLandblockLoaded;
        _ensureEnvCellMeshes = ensureEnvCellMeshes;
        _staticProjectionSink = staticProjectionSink;
        if (retirementCoordinator is not null
            && !retirementCoordinator.MatchesState(_state))
        {
            throw new ArgumentException(
                "The retirement coordinator must own the pipeline's world state.",
                nameof(retirementCoordinator));
        }
        _retirements = retirementCoordinator
            ?? LandblockRetirementCoordinator.CreateLegacy(
                state,
                removeTerrain,
                demoteNearLayer);
    }

    public LandblockPresentationPipeline(
        LandblockRenderPublisher renderPublisher,
        LandblockStaticPresentationPublisher staticPublisher,
        GpuWorldState state,
        LandblockPresentationRetirementOwner retirementOwner,
        Action<uint>? onLandblockLoaded = null,
        Action<EnvCellLandblockBuild>? ensureEnvCellMeshes = null)
        : this(
            renderPublisher,
            staticPublisher,
            state,
            retirementOwner,
            onLandblockLoaded,
            ensureEnvCellMeshes,
            staticProjectionSink: null)
    {
    }

    internal LandblockPresentationPipeline(
        LandblockRenderPublisher renderPublisher,
        LandblockStaticPresentationPublisher staticPublisher,
        GpuWorldState state,
        LandblockPresentationRetirementOwner retirementOwner,
        Action<uint>? onLandblockLoaded,
        Action<EnvCellLandblockBuild>? ensureEnvCellMeshes,
        IRenderStaticProjectionJournalSink? staticProjectionSink)
    {
        _renderPublisher = renderPublisher
            ?? throw new ArgumentNullException(nameof(renderPublisher));
        _staticPublisher = staticPublisher
            ?? throw new ArgumentNullException(nameof(staticPublisher));
        _state = state ?? throw new ArgumentNullException(nameof(state));
        if (!_renderPublisher.MatchesState(_state))
        {
            throw new ArgumentException(
                "The render publisher must own the pipeline's world state.",
                nameof(state));
        }
        ArgumentNullException.ThrowIfNull(retirementOwner);
        if (!retirementOwner.Matches(
                _renderPublisher,
                _staticPublisher))
        {
            throw new ArgumentException(
                "The retirement owner must reference the pipeline's concrete publishers.",
                nameof(retirementOwner));
        }
        _onLandblockLoaded = onLandblockLoaded;
        _ensureEnvCellMeshes = ensureEnvCellMeshes;
        _staticProjectionSink = staticProjectionSink;
        _retirements = LandblockRetirementCoordinator.CreateBudgeted(
            state,
            retirementOwner.AdvanceOne,
            retirementOwner.Advance,
            staticProjectionSink is null
                ? null
                : staticProjectionSink.Retire);
    }

    /// <summary>
    /// Building visibility registries published by the render owner. Legacy
    /// test pipelines have no render owner and therefore expose an empty view.
    /// </summary>
    private static readonly Dictionary<uint, BuildingRegistry> NoBuildingRegistries = new();

    /// <summary>The publisher's building registries, walked once per frame by
    /// the frame's building gather; the concrete collection walks without
    /// boxing an enumerator.</summary>
    public Dictionary<uint, BuildingRegistry>.ValueCollection BuildingRegistries =>
        _renderPublisher?.BuildingRegistries ?? NoBuildingRegistries.Values;

    public LandblockPresentationDiagnostics Diagnostics => new(
        _renderPublisher?.Diagnostics ?? default,
        _staticPublisher?.Diagnostics ?? default);

    public int PendingRetirementCount => _retirements.PendingCount;
    internal bool UsesBudgetedRetirementSteps => _retirements.UsesBudgetedSteps;
    public int PendingPublicationCount => _publications.Count;

    internal bool MatchesState(GpuWorldState state) =>
        ReferenceEquals(_state, state);

    internal bool IsLandblockPresentationReady(uint landblockId)
    {
        uint canonical = (landblockId & 0xFFFF0000u) | 0xFFFFu;
        foreach (PublicationTransaction transaction in _publications.Values)
        {
            if ((transaction.LandblockId & 0xFFFF0000u) ==
                    (canonical & 0xFFFF0000u)
                && (!transaction.PresentationCommitted
                    || !transaction.SpatialPresentationCommitted))
            {
                return false;
            }
        }
        return true;
    }

    public bool IsRetirementPending(uint landblockId) =>
        _retirements.IsPending(landblockId);

    public bool HasPendingPublication(LandblockStreamResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        return _publications.ContainsKey(result);
    }

    public IReadOnlyList<LandblockStreamResult> GetPendingPublicationResults() =>
        _publications.Keys.ToArray();

    internal bool CancelPendingPublications()
    {
        if (_publications.Count == 0)
            return true;
        // A pending transaction holds no collision (the session host publishes
        // it), so there is nothing to wait for: they are dropped.
        _publications.Clear();
        return true;
    }

    public void ResumePublication(LandblockStreamResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        if (!_publications.TryGetValue(result, out PublicationTransaction? transaction))
            return;
        Advance(result, transaction, meter: null, ensureProgress: false);
    }

    internal LandblockPublicationAdvance ResumePublication(
        LandblockStreamResult result,
        StreamingWorkMeter meter,
        bool ensureProgress)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(meter);
        if (!_publications.TryGetValue(result, out PublicationTransaction? transaction))
            return new LandblockPublicationAdvance(Completed: true, Progressed: false);
        return Advance(result, transaction, meter, ensureProgress);
    }

    public void AdvanceRetirements()
    {
        _retirements.Advance();
    }

    public void AdvanceRetirements(StreamingWorkMeter meter)
    {
        _retirements.Advance(meter);
    }

    /// <summary>
    /// Takes, within the meter, the static render projections of an origin
    /// recenter's detached landblocks out of the render scene. True once
    /// none is left.
    /// </summary>
    internal bool AdvanceDetachedProjectionRetirements(
        StreamingWorkMeter meter) =>
        _retirements.AdvanceStaticProjectionRetirements(meter);

    internal void AdvancePriorityRetirement(
        uint landblockId,
        StreamingWorkMeter meter) =>
        _retirements.AdvancePriority(landblockId, meter);

    public void BeginFullRetirement(uint landblockId)
    {
        _retirements.BeginFull(landblockId);
        if (_retirements.UsesBudgetedSteps)
        {
            _retirements.Advance();
            if (_retirements.IsPending(landblockId))
                _retirements.Advance();
        }
    }

    public void BeginNearLayerRetirement(uint landblockId)
    {
        _retirements.BeginNearLayer(landblockId);
        if (_retirements.UsesBudgetedSteps)
        {
            _retirements.Advance();
            if (_retirements.IsPending(landblockId))
                _retirements.Advance();
        }
    }

    internal void EnqueueFullRetirement(uint landblockId) =>
        _retirements.BeginFull(landblockId);

    internal void EnqueueNearLayerRetirement(uint landblockId) =>
        _retirements.BeginNearLayer(landblockId);

    internal GpuWorldRecenterRetirement DetachAllForOriginRecenter()
    {
        GpuWorldRecenterRetirement detached =
            _state.DetachAllForOriginRecenter();
        Exception? adoptionFailure;
        try
        {
            adoptionFailure =
                _retirements.AdoptDetachedFull(detached.Landblocks);
        }
        catch (Exception error)
        {
            throw new StreamingMutationException(
                "Origin-recenter retirement receipt adoption failed after " +
                "the spatial generation detached.",
                mutationCommitted: true,
                error);
        }
        Exception? failure = (detached.ObserverFailure, adoptionFailure) switch
        {
            (null, null) => null,
            ({ } observer, null) => observer,
            (null, { } adoption) => adoption,
            ({ } observer, { } adoption) => new AggregateException(
                "Origin-recenter spatial observers and retirement adoption failed.",
                observer,
                adoption),
        };
        return detached with { ObserverFailure = failure };
    }

    public void PublishLoaded(LandblockStreamResult.Loaded loaded)
    {
        ArgumentNullException.ThrowIfNull(loaded);
        PublicationTransaction transaction = GetOrCreate(
            loaded,
            PublicationKind.Loaded,
            loaded.Build,
            loaded.MeshData,
            loaded.LandblockId,
            loaded.Tier,
            estimate: null);
        Advance(loaded, transaction, meter: null, ensureProgress: false);
    }

    internal LandblockPublicationAdvance PublishLoaded(
        LandblockStreamResult.Loaded loaded,
        LandblockStreamCostEstimate estimate,
        StreamingWorkMeter meter,
        bool ensureProgress)
    {
        ArgumentNullException.ThrowIfNull(loaded);
        ArgumentNullException.ThrowIfNull(meter);
        PublicationTransaction transaction = GetOrCreate(
            loaded,
            PublicationKind.Loaded,
            loaded.Build,
            loaded.MeshData,
            loaded.LandblockId,
            loaded.Tier,
            estimate);
        return Advance(loaded, transaction, meter, ensureProgress);
    }

    public void PublishPromoted(
        LandblockStreamResult.Promoted promoted,
        bool mergeIntoExistingLandblock)
    {
        ArgumentNullException.ThrowIfNull(promoted);
        PublicationKind kind = mergeIntoExistingLandblock
            ? PublicationKind.PromoteExisting
            : PublicationKind.PromoteSelfContained;
        PublicationTransaction transaction = GetOrCreate(
            promoted,
            kind,
            promoted.Build,
            promoted.MeshData,
            promoted.LandblockId,
            LandblockStreamTier.Near,
            estimate: null);
        Advance(promoted, transaction, meter: null, ensureProgress: false);
    }

    internal LandblockPublicationAdvance PublishPromoted(
        LandblockStreamResult.Promoted promoted,
        bool mergeIntoExistingLandblock,
        LandblockStreamCostEstimate estimate,
        StreamingWorkMeter meter,
        bool ensureProgress)
    {
        ArgumentNullException.ThrowIfNull(promoted);
        ArgumentNullException.ThrowIfNull(meter);
        PublicationKind kind = mergeIntoExistingLandblock
            ? PublicationKind.PromoteExisting
            : PublicationKind.PromoteSelfContained;
        PublicationTransaction transaction = GetOrCreate(
            promoted,
            kind,
            promoted.Build,
            promoted.MeshData,
            promoted.LandblockId,
            LandblockStreamTier.Near,
            estimate);
        return Advance(promoted, transaction, meter, ensureProgress);
    }

    public void PublishAsFar(
        LandblockStreamResult acceptedResult,
        LandblockBuild completedBuild,
        LandblockMeshData meshData)
    {
        ArgumentNullException.ThrowIfNull(acceptedResult);
        ArgumentNullException.ThrowIfNull(completedBuild);
        ArgumentNullException.ThrowIfNull(meshData);

        if (!_publications.TryGetValue(acceptedResult, out PublicationTransaction? transaction))
        {
            LoadedLandblock completed = completedBuild.Landblock;
            var farLandblock = new LoadedLandblock(
                completed.LandblockId,
                completed.Heightmap,
                Array.Empty<WorldEntity>(),
                PhysicsDatBundle.Empty);
            LandblockBuild farBuild = new(
                farLandblock,
                Origin: completedBuild.Origin,
                TerrainBounds: completedBuild.TerrainBounds);
            transaction = new PublicationTransaction
            {
                Kind = PublicationKind.Far,
                Build = farBuild,
                MeshData = meshData,
                LandblockId = farLandblock.LandblockId,
                Cost = LandblockStreamResultCost.Estimate(
                    farBuild,
                    meshData),
                Tier = LandblockStreamTier.Far,
                Timing = PublicationTimingProbe.CreateTimings(),
            };
            _publications.Add(acceptedResult, transaction);
        }
        else if (transaction.Kind != PublicationKind.Far)
        {
            throw new InvalidOperationException(
                $"Landblock publication 0x{transaction.LandblockId:X8} changed kind while pending.");
        }

        Advance(
            acceptedResult,
            transaction,
            meter: null,
            ensureProgress: false);
    }

    internal LandblockPublicationAdvance PublishAsFar(
        LandblockStreamResult acceptedResult,
        LandblockBuild completedBuild,
        LandblockMeshData meshData,
        StreamingWorkMeter meter,
        bool ensureProgress)
    {
        ArgumentNullException.ThrowIfNull(acceptedResult);
        ArgumentNullException.ThrowIfNull(completedBuild);
        ArgumentNullException.ThrowIfNull(meshData);
        ArgumentNullException.ThrowIfNull(meter);

        if (!_publications.TryGetValue(
                acceptedResult,
                out PublicationTransaction? transaction))
        {
            LoadedLandblock completed = completedBuild.Landblock;
            var farLandblock = new LoadedLandblock(
                completed.LandblockId,
                completed.Heightmap,
                Array.Empty<WorldEntity>(),
                PhysicsDatBundle.Empty);
            LandblockBuild farBuild = new(
                farLandblock,
                Origin: completedBuild.Origin,
                TerrainBounds: completedBuild.TerrainBounds);
            transaction = new PublicationTransaction
            {
                Kind = PublicationKind.Far,
                Build = farBuild,
                MeshData = meshData,
                LandblockId = farLandblock.LandblockId,
                Cost = LandblockStreamResultCost.Estimate(farBuild, meshData),
                Tier = LandblockStreamTier.Far,
                Timing = PublicationTimingProbe.CreateTimings(),
            };
            _publications.Add(acceptedResult, transaction);
        }
        else if (transaction.Kind != PublicationKind.Far)
        {
            throw new InvalidOperationException(
                $"Landblock publication 0x{transaction.LandblockId:X8} changed kind while pending.");
        }

        return Advance(acceptedResult, transaction, meter, ensureProgress);
    }

    private PublicationTransaction GetOrCreate(
        LandblockStreamResult result,
        PublicationKind kind,
        LandblockBuild build,
        LandblockMeshData meshData,
        uint landblockId,
        LandblockStreamTier tier,
        LandblockStreamCostEstimate? estimate)
    {
        if (_publications.TryGetValue(result, out PublicationTransaction? existing))
        {
            if (existing.Kind != kind
                && !(existing.Kind is PublicationKind.PromoteExisting
                    or PublicationKind.PromoteSelfContained
                    && kind is PublicationKind.PromoteExisting
                        or PublicationKind.PromoteSelfContained))
            {
                throw new InvalidOperationException(
                    $"Landblock publication 0x{existing.LandblockId:X8} changed kind while pending.");
            }
            return existing;
        }

        var created = new PublicationTransaction
        {
            Kind = kind,
            Build = build,
            MeshData = meshData,
            LandblockId = landblockId,
            Cost = estimate
                ?? LandblockStreamResultCost.Estimate(build, meshData),
            Tier = tier,
            Timing = PublicationTimingProbe.CreateTimings(),
        };
        _publications.Add(result, created);
        return created;
    }

    private enum PublicationStep : byte
    {
        PrepareRender,
        PrepareStatics,
        ValidateStatics,
        RenderPrefix,
        RenderSuffix,
        StaticCleanup,
        StaticLighting,
        PluginProjection,
        LegacyPresentation,
        SpatialCommit,
        EnvCellReplay,
        LiveRecovery,
    }

    private static string StageName(PublicationStep step) => step switch
    {
        PublicationStep.PrepareRender => "publication-prepare-render",
        PublicationStep.PrepareStatics => "publication-prepare-statics",
        PublicationStep.ValidateStatics => "publication-validate-statics",
        PublicationStep.RenderPrefix => "publication-render-prefix",
        PublicationStep.RenderSuffix => "publication-render-suffix",
        PublicationStep.StaticCleanup => "publication-static-cleanup",
        PublicationStep.StaticLighting => "publication-static-lighting",
        PublicationStep.PluginProjection => "publication-plugin-projection",
        PublicationStep.LegacyPresentation => "publication-legacy-presentation",
        PublicationStep.SpatialCommit => "publication-spatial-commit",
        PublicationStep.EnvCellReplay => "publication-envcell-replay",
        PublicationStep.LiveRecovery => "publication-live-recovery",
        _ => throw new ArgumentOutOfRangeException(nameof(step), step, null),
    };

    /// <summary>One publication step; a method, not a delegate per step, so
    /// the steps that run once per entity make no garbage.</summary>
    private void RunStep(
        LandblockStreamResult result,
        PublicationTransaction transaction,
        PublicationStep step)
    {
        switch (step)
        {
            case PublicationStep.PrepareRender:
                transaction.RenderPublication = _renderPublisher!.PreparePublication(
                    transaction.Build,
                    transaction.MeshData);
                break;
            case PublicationStep.PrepareStatics:
                transaction.StaticPublication = _staticPublisher!.CreatePublication(
                    transaction.RenderPublication!.Build);
                break;
            case PublicationStep.ValidateStatics:
                _staticPublisher!.AdvancePreparationOne(transaction.StaticPublication!);
                break;
            case PublicationStep.RenderPrefix:
                _renderPublisher!.AdvanceBeginOne(transaction.RenderPublication!);
                break;
            case PublicationStep.RenderSuffix:
                _renderPublisher!.AdvanceCompleteOne(transaction.RenderPublication!);
                break;
            case PublicationStep.StaticCleanup:
                _staticPublisher!.AdvanceBeginOne(transaction.StaticPublication!);
                break;
            case PublicationStep.StaticLighting:
                _staticPublisher!.AdvanceLightingOne(transaction.StaticPublication!);
                break;
            case PublicationStep.PluginProjection:
                _staticPublisher!.AdvanceCompleteOne(transaction.StaticPublication!);
                break;
            case PublicationStep.LegacyPresentation:
                try
                {
                    (_publishBeforeSpatialCommit
                        ?? throw new InvalidOperationException(
                            "The presentation pipeline has no publication owners."))(
                        transaction.Build,
                        transaction.MeshData);
                }
                catch (Exception error)
                {
                    if (error is StreamingMutationException
                        {
                            MutationCommitted: true,
                        })
                    {
                        _publications.Remove(result);
                    }
                    throw;
                }
                break;
            case PublicationStep.SpatialCommit:
                CommitSpatialPresentation(transaction);
                break;
            case PublicationStep.EnvCellReplay:
                if (transaction.Kind != PublicationKind.Far
                    && transaction.Build.EnvCells is
                        {
                            Shells.Length: > 0,
                        } envCells)
                {
                    _ensureEnvCellMeshes?.Invoke(envCells);
                }
                transaction.EnvCellReplayCommitted = true;
                break;
            case PublicationStep.LiveRecovery:
                _onLandblockLoaded?.Invoke(transaction.LandblockId);
                transaction.LiveRecoveryCommitted = true;
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(step), step, null);
        }
    }

    private void RunTimed(
        LandblockStreamResult result,
        PublicationTransaction transaction,
        PublicationStep step)
    {
        if (transaction.Timing is not { } timing)
        {
            RunStep(result, transaction, step);
            return;
        }

        long start = System.Diagnostics.Stopwatch.GetTimestamp();
        try
        {
            RunStep(result, transaction, step);
        }
        finally
        {
            timing.Add(
                StageName(step),
                System.Diagnostics.Stopwatch.GetTimestamp() - start);
        }
    }

    /// <summary>Runs one step when the meter admits it (always, without a
    /// meter). False when the step yielded to the next frame.</summary>
    private bool TryRun(
        LandblockStreamResult result,
        PublicationTransaction transaction,
        StreamingWorkMeter? meter,
        bool ensureProgress,
        ref bool progressed,
        StreamingWorkCost cost,
        PublicationStep step)
    {
        if (meter is null)
        {
            RunTimed(result, transaction, step);
            progressed = true;
            return true;
        }

        StreamingWorkAdmission admission = meter.TryReserve(
            cost,
            StageName(step),
            ensureProgress && !progressed);
        if (admission == StreamingWorkAdmission.Yielded)
            return false;
        try
        {
            RunTimed(result, transaction, step);
            meter.Complete();
            progressed = true;
            return true;
        }
        catch
        {
            meter.Fail();
            throw;
        }
    }

    private LandblockPublicationAdvance Advance(
        LandblockStreamResult result,
        PublicationTransaction transaction,
        StreamingWorkMeter? meter,
        bool ensureProgress)
    {
        bool progressed = false;
        LandblockPublicationAdvance Yield() => new(false, progressed);

        if (!transaction.PresentationCommitted)
        {
            if (_renderPublisher is not null
                && _staticPublisher is not null)
            {
                while (transaction.RenderPublication is null)
                {
                    if (!TryRun(result, transaction, meter, ensureProgress, ref progressed,
                            new StreamingWorkCost(EntityOperations: 1),
                            PublicationStep.PrepareRender))
                    {
                        return Yield();
                    }
                }
                while (transaction.StaticPublication is null)
                {
                    if (!TryRun(result, transaction, meter, ensureProgress, ref progressed,
                            default,
                            PublicationStep.PrepareStatics))
                    {
                        return Yield();
                    }
                }
                while (!transaction.StaticPublication.PreparationCommitted)
                {
                    int entityOperations =
                        transaction.StaticPublication.PreparationCursor
                        < transaction.Build.Landblock.Entities.Count
                            ? 1
                            : 0;
                    if (!TryRun(result, transaction, meter, ensureProgress, ref progressed,
                            new StreamingWorkCost(
                                EntityOperations: entityOperations),
                            PublicationStep.ValidateStatics))
                    {
                        return Yield();
                    }
                }

                while (!transaction.RenderPublication.BeginCommitted)
                {
                    StreamingWorkCost cost =
                        !transaction.RenderPublication.TerrainCommitted
                            ? new StreamingWorkCost(
                                GpuUploadBytes:
                                    transaction.Cost.TerrainPayloadBytes)
                            : !transaction.RenderPublication.VisibilityCommitted
                                ? new StreamingWorkCost(
                                    EntityOperations:
                                        transaction.Cost.VisibilityCells)
                                : default;
                    if (!TryRun(result, transaction, meter, ensureProgress, ref progressed,
                            cost,
                            PublicationStep.RenderPrefix))
                    {
                        return Yield();
                    }
                }

                while (!transaction.RenderPublication.CompletionCommitted)
                {
                    int entityOperations =
                        transaction.RenderPublication.BuildingPublication is
                            { PreparationCommitted: false } buildingPublication
                        && buildingPublication.BuildingCursor
                            < buildingPublication.Buildings.Length
                            ? 1
                            : transaction.RenderPublication.EnvCellPublication is
                                { PreparationCommitted: false } envCellPublication
                            && envCellPublication.ShellCursor
                                < envCellPublication.Build.Shells.Length
                                ? 1
                            : !transaction.RenderPublication.EnvCellsCommitted
                                && transaction.RenderPublication
                                    .EnvCellPublication is null
                                    ? transaction.Cost.EnvCellShells
                                : 0;
                    if (!TryRun(result, transaction, meter, ensureProgress, ref progressed,
                            new StreamingWorkCost(
                                EntityOperations: entityOperations),
                            PublicationStep.RenderSuffix))
                    {
                        return Yield();
                    }
                }

                while (!transaction.StaticPublication.BeginCommitted)
                {
                    int entityOperations =
                        transaction.StaticPublication.PriorCleanupCursor
                        < transaction.StaticPublication
                            .OrderedPreviouslyActiveIds.Count
                            ? 1
                            : 0;
                    if (!TryRun(result, transaction, meter, ensureProgress, ref progressed,
                            new StreamingWorkCost(
                                EntityOperations: entityOperations),
                            PublicationStep.StaticCleanup))
                    {
                        return Yield();
                    }
                }
                // No collision is published here: the session host builds it
                // around the character whether or not anything is drawn.
                while (!transaction.StaticPublication.LightingCommitted)
                {
                    int entityOperations =
                        transaction.StaticPublication.LightingCursor
                        < transaction.Build.Landblock.Entities.Count
                            ? 1
                            : 0;
                    if (!TryRun(result, transaction, meter, ensureProgress, ref progressed,
                            new StreamingWorkCost(
                                EntityOperations: entityOperations),
                            PublicationStep.StaticLighting))
                    {
                        return Yield();
                    }
                }
                while (!transaction.StaticPublication.CompletionCommitted)
                {
                    int entityOperations =
                        transaction.StaticPublication.PluginCursor
                        < transaction.StaticPublication.OrderedSnapshots.Count
                            ? 1
                            : 0;
                    if (!TryRun(result, transaction, meter, ensureProgress, ref progressed,
                            new StreamingWorkCost(
                                EntityOperations: entityOperations),
                            PublicationStep.PluginProjection))
                    {
                        return Yield();
                    }
                }
            }
            else
            {
                StreamingWorkCost legacyCost = new(
                    EntityOperations: transaction.Cost.Entities,
                    GpuUploadBytes: transaction.Cost.TerrainPayloadBytes);
                if (!TryRun(result, transaction, meter, ensureProgress, ref progressed,
                        legacyCost,
                        PublicationStep.LegacyPresentation))
                {
                    return Yield();
                }
            }
            transaction.PresentationCommitted = true;
        }

        if (!transaction.SpatialPresentationCommitted)
        {
            if (!TryRun(result, transaction, meter, ensureProgress, ref progressed,
                    default,
                    PublicationStep.SpatialCommit))
            {
                return Yield();
            }
        }

        if (!transaction.EnvCellReplayCommitted)
        {
            if (!TryRun(result, transaction, meter, ensureProgress, ref progressed,
                    new StreamingWorkCost(
                        EntityOperations: transaction.Cost.EnvCellShells),
                    PublicationStep.EnvCellReplay))
            {
                return Yield();
            }
        }

        if (!transaction.LiveRecoveryCommitted)
        {
            if (!TryRun(result, transaction, meter, ensureProgress, ref progressed,
                    default,
                    PublicationStep.LiveRecovery))
            {
                return Yield();
            }
        }

        _publications.Remove(result);
        if (transaction.Timing is { } completedTiming)
        {
            PublicationTimingProbe.EmitPublication(
                transaction.LandblockId,
                transaction.Kind.ToString(),
                completedTiming);
        }
        return new LandblockPublicationAdvance(true, progressed);
    }

    private void CommitSpatialPresentation(PublicationTransaction transaction)
    {
        using GpuWorldState.MutationBatch mutation = _state.BeginMutationBatch();
        if (!transaction.SpatialCommitted)
        {
            IEnumerable<ulong>? renderIds =
                transaction.Build.EnvCells?.Shells.Select(
                    static shell => shell.GeometryId);
            IEnumerable<ulong>? ordinaryRenderIds =
                transaction.Build.EnvCells?.WalkBuildingMeshDependencies;
            transaction.SpatialPublication = transaction.Kind switch
            {
                PublicationKind.Loaded
                    or PublicationKind.PromoteSelfContained
                    or PublicationKind.Far =>
                    _state.CommitLandblockSpatial(
                        transaction.Build.Landblock,
                        renderIds,
                        transaction.Tier,
                        ordinaryRenderIds),
                PublicationKind.PromoteExisting =>
                    _state.CommitEntitiesToExistingLandblockSpatial(
                        transaction.LandblockId,
                        transaction.Build.Landblock.Entities,
                        renderIds,
                        ordinaryRenderIds),
                _ => throw new InvalidOperationException(
                    $"Unknown landblock publication kind {transaction.Kind}."),
            };
            transaction.SpatialCommitted = true;
        }

        _state.ActivateLandblockPresentation(
            transaction.SpatialPublication
            ?? throw new InvalidOperationException(
                "A committed spatial publication has no activation receipt."));
        if (transaction.SpatialPublication.RequiresActivation)
        {
            _staticProjectionSink?.Reconcile(
                transaction.Build,
                transaction.SpatialPublication);
        }
        transaction.SpatialPresentationCommitted = true;
    }
}
