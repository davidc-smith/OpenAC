using System.Collections.Immutable;
using System.Numerics;
using AcDream.Content;
using AcDream.Core.Physics;
using AcDream.Core.World;
using AcDream.Runtime.Physics;

namespace AcDream.Runtime.World;

/// <summary>
/// What a collision neighborhood reads its landblocks from: the game files,
/// the lock every read of them is made under, the prepared collision and the
/// terrain height table. A client with a window and one without hand it the
/// same things.
/// </summary>
internal interface IRuntimeCollisionContent
{
    IDatReaderWriter Dats { get; }

    object DatLock { get; }

    IPreparedCollisionSource PreparedCollision { get; }

    ImmutableArray<float> HeightTable { get; }

    /// <summary>
    /// One landblock's collision content, placed at <paramref name="origin"/>
    /// in the world's frame. The files are read under <see cref="DatLock"/>:
    /// a plugin may be reading the same files from its own thread.
    /// False when the game data has no such landblock.
    /// </summary>
    bool TryLoadCollisionLandblock(
        uint landblockId,
        Vector3 origin,
        out LoadedLandblock landblock,
        out LandblockCollisionBuild collisions)
    {
        lock (DatLock)
        {
            return LandblockPhysicsContentBuilder.TryLoadCollisionLandblock(
                Dats,
                PreparedCollision,
                HeightTable.AsSpan(),
                landblockId,
                origin,
                out landblock,
                out collisions);
        }
    }
}

internal interface IRuntimeCollisionNeighborhood
{
    /// <summary>
    /// Centres the published collision on an arrival the server decided
    /// (login, portals, forced moves). The collision is measured in the
    /// runtime's frame: when that frame has moved (a portal into another
    /// landblock, a new session) everything is withdrawn and published
    /// afresh; otherwise the window only moves within the same frame.
    /// </summary>
    void CenterOn(uint fullCellId);

    /// <summary>
    /// Keeps the published collision around the local player while it walks:
    /// the window of landblocks moves with the player, the frame the world is
    /// measured in stays where it was. <see cref="CenterOn"/> is for
    /// arrivals the server decides (login, portals, forced moves); walking
    /// into the next landblock is not one of those.
    /// </summary>
    void Follow(uint fullCellId);

    bool IsReady(uint fullCellId);

    bool IsWithinServiceWindow(uint fullCellId);

    /// <summary>
    /// Whether the collision for this cell is published right now. That is a
    /// narrower question than being near the centre, and it is the one an
    /// accepted re-placement of another creature's body has to ask: a body
    /// cannot be put down where there is nothing yet to put it down on.
    /// </summary>
    bool IsCollisionPublished(uint fullCellId);

    bool IsQuiescent { get; }
}

internal readonly record struct RuntimeCollisionGenerationAdvance(
    bool Completed,
    bool Progressed,
    bool WaitingForProjectionAcknowledgement,
    bool YieldToCaller);

internal sealed class RuntimeCollisionGenerationTransaction
{
    private readonly RuntimePhysicsState _physics;
    private readonly RuntimeCollisionAdmission _admission;
    private readonly PreparedLandblockCollisionGeneration _prepared;
    private readonly IRuntimeCollisionStaging _staging;
    private bool _stagingCommitted;
    private bool _ownerCaptureCommitted;
    private int _refreshCursor;
    private bool _sealCommitted;

    private RuntimeCollisionGenerationTransaction(
        RuntimePhysicsState physics,
        RuntimeCollisionAdmission admission,
        PreparedLandblockCollisionGeneration prepared,
        IRuntimeCollisionStaging staging)
    {
        _physics = physics;
        _admission = admission;
        _prepared = prepared;
        _staging = staging;
    }

    internal uint LandblockId => _admission.LandblockId;
    internal bool EngineMutationCommitted { get; private set; }
    internal bool CompletionCommitted { get; private set; }
    internal bool CancellationRequested { get; private set; }

    /// <summary>
    /// A generation whose collision is staged all at once, before this
    /// returns.
    /// </summary>
    internal static RuntimeCollisionGenerationTransaction Begin(
        RuntimePhysicsState physics,
        uint landblockId,
        Action<RuntimeCollisionAdmission>? afterAdmission,
        Action<RuntimeCollisionAdmission,
            PreparedLandblockCollisionGeneration> stage)
    {
        ArgumentNullException.ThrowIfNull(stage);
        return Begin(
            physics,
            landblockId,
            afterAdmission,
            (admission, prepared) =>
            {
                stage(admission, prepared);
                return AlreadyStaged.Instance;
            });
    }

    /// <summary>
    /// A generation whose collision is staged by the returned steps, one
    /// per <see cref="Advance"/>, before it is sealed and committed.
    /// </summary>
    internal static RuntimeCollisionGenerationTransaction Begin(
        RuntimePhysicsState physics,
        uint landblockId,
        Action<RuntimeCollisionAdmission>? afterAdmission,
        Func<RuntimeCollisionAdmission,
            PreparedLandblockCollisionGeneration,
            IRuntimeCollisionStaging> stage)
    {
        ArgumentNullException.ThrowIfNull(physics);
        ArgumentNullException.ThrowIfNull(stage);

        RuntimeCollisionAdmission admission =
            physics.BeginCollisionAdmission(landblockId);
        PreparedLandblockCollisionGeneration? prepared = null;
        try
        {
            afterAdmission?.Invoke(admission);
            prepared = physics.PrepareCollisionGeneration(admission);
            IRuntimeCollisionStaging staging = stage(admission, prepared);
            return new RuntimeCollisionGenerationTransaction(
                physics,
                admission,
                prepared,
                staging);
        }
        catch (Exception publicationError)
        {
            if (!physics.CancelCollisionGeneration(admission, prepared))
            {
                throw new AggregateException(
                    "Collision preparation failed and its pre-engine cancellation did not converge.",
                    publicationError);
            }
            throw;
        }
    }

    internal RuntimeCollisionGenerationAdvance Advance()
    {
        if (CancellationRequested)
            throw new InvalidOperationException(
                "A cancelled collision generation cannot resume.");
        if (CompletionCommitted)
            return new(true, false, false, false);

        if (!_stagingCommitted)
        {
            try
            {
                _stagingCommitted = _staging.AdvanceOne();
            }
            catch (Exception stagingError)
            {
                CancellationRequested = true;
                if (!_physics.CancelCollisionGeneration(_admission, _prepared))
                {
                    throw new AggregateException(
                        "Collision staging failed and its pre-engine cancellation did not converge.",
                        stagingError);
                }
                throw;
            }
            return new(false, true, false, false);
        }

        if (!EngineMutationCommitted)
        {
            if (!_ownerCaptureCommitted)
            {
                RuntimeCollisionOwnerCaptureStep capture =
                    _physics.AdvanceCollisionRetainedOwnerCapture(
                        _admission,
                        _prepared);
                _ownerCaptureCommitted = capture.Completed;
                return new(false, true, false, false);
            }

            if (_refreshCursor < _prepared.RetainedOwnerIds.Count)
            {
                _physics.RefreshCollisionRetainedOwner(
                    _admission,
                    _prepared,
                    _prepared.RetainedOwnerIds[_refreshCursor]);
                _refreshCursor++;
                return new(false, true, false, false);
            }

            if (!_sealCommitted)
            {
                RuntimeCollisionSealStep seal =
                    _physics.AdvanceCollisionGenerationSeal(
                        _admission,
                        _prepared);
                _sealCommitted = seal.Completed;
                if (seal.Restarted)
                {
                    _ownerCaptureCommitted = false;
                    _refreshCursor = 0;
                }
                return new(false, true, false, false);
            }
        }

        RuntimeCollisionGenerationCommit commit =
            _physics.CommitCollisionGeneration(_admission, _prepared);
        EngineMutationCommitted |= commit.EngineCommitted;
        CompletionCommitted = commit.Completed;
        bool waiting = !commit.Completed
            && _physics.CaptureOwnership()
                .PendingCollisionPrefixProjectionCount != 0;
        if (!commit.Completed && !commit.EngineCommitted)
            _sealCommitted = false;
        return new(
            commit.Completed,
            Progressed: true,
            WaitingForProjectionAcknowledgement: waiting,
            YieldToCaller: !commit.Completed);
    }

    internal bool TryCancel()
    {
        if (CompletionCommitted)
            return true;
        CancellationRequested = true;
        bool completed = _physics.CancelCollisionGeneration(
            _admission,
            _prepared);
        if (completed && EngineMutationCommitted)
            CompletionCommitted = true;
        return completed;
    }

}

/// <summary>
/// Builds a landblock's collision into its prepared generation, a little at
/// a time, before the generation is sealed and committed.
/// </summary>
internal interface IRuntimeCollisionStaging
{
    /// <summary>Does one small piece of the work; true once all of it is done.</summary>
    bool AdvanceOne();
}

/// <summary>Staging that was done before the transaction began.</summary>
internal sealed class AlreadyStaged : IRuntimeCollisionStaging
{
    internal static readonly AlreadyStaged Instance = new();

    private AlreadyStaged()
    {
    }

    public bool AdvanceOne() => true;
}

/// <summary>
/// One loaded landblock's collision staged into its prepared generation one
/// piece per step: each interior cell, each building, each prepared object
/// and setup, the landblock itself, each static object, each owner to
/// re-file. A step is small, so a client drawing frames can stop between any
/// two; the order is the order a whole landblock is published in at once.
/// </summary>
internal sealed class LandblockCollisionStaging : IRuntimeCollisionStaging
{
    private enum Phase
    {
        Cells,
        Buildings,
        GfxObjects,
        Setups,
        Landblock,
        Statics,
        RefloodCapture,
        Reflood,
        Done,
    }

    private readonly RuntimePhysicsState _physics;
    private readonly RuntimeCollisionAdmission _admission;
    private readonly PreparedLandblockCollisionGeneration _prepared;
    private readonly LoadedLandblock _landblock;
    private readonly LandblockCollisionBuild _collisions;
    private readonly TerrainSurface _terrain;
    private readonly Vector3 _origin;
    private readonly uint _currentCellId;
    private readonly List<CellSurface> _cellSurfaces = [];
    private readonly List<PortalPlane> _portalPlanes = [];
    private readonly KeyValuePair<uint, FlatGfxObjCollisionAsset>[] _gfxObjects;
    private readonly KeyValuePair<uint, FlatSetupCollision>[] _setups;
    private uint[] _refloodOwners = [];
    private Phase _phase;
    private int _cursor;

    internal LandblockCollisionStaging(
        RuntimePhysicsState physics,
        RuntimeCollisionAdmission admission,
        PreparedLandblockCollisionGeneration prepared,
        LoadedCollisionContent content,
        TerrainSurface terrain,
        Vector3 origin,
        uint currentCellId)
    {
        _physics = physics;
        _admission = admission;
        _prepared = prepared;
        _landblock = content.Landblock;
        _collisions = content.Collisions;
        _terrain = terrain;
        _origin = origin;
        _currentCellId = currentCellId;
        _gfxObjects = [.. _collisions.GfxObjs];
        _setups = [.. _collisions.Setups];
    }

    public bool AdvanceOne()
    {
        PhysicsDataCache cache = _prepared.DataCache;
        while (true)
        {
            switch (_phase)
            {
                case Phase.Cells:
                    if ((uint)_cursor
                        < LandblockPhysicsContentBuilder.CellCount(_landblock))
                    {
                        LandblockPhysicsContentBuilder.PublishPreparedCell(
                            cache,
                            _landblock,
                            _collisions,
                            _origin,
                            (uint)_cursor++,
                            _cellSurfaces,
                            _portalPlanes);
                        return false;
                    }
                    Next();
                    continue;
                case Phase.Buildings:
                    if (_cursor
                        < LandblockPhysicsContentBuilder.BuildingCount(_landblock))
                    {
                        LandblockPhysicsContentBuilder.CacheBuilding(
                            cache,
                            _landblock,
                            _terrain,
                            _origin,
                            _cursor++);
                        return false;
                    }
                    Next();
                    continue;
                case Phase.GfxObjects:
                    if (_cursor < _gfxObjects.Length)
                    {
                        (uint id, FlatGfxObjCollisionAsset asset) =
                            _gfxObjects[_cursor++];
                        cache.CacheGfxObj(id, asset);
                        return false;
                    }
                    Next();
                    continue;
                case Phase.Setups:
                    if (_cursor < _setups.Length)
                    {
                        (uint id, FlatSetupCollision setup) =
                            _setups[_cursor++];
                        cache.CacheSetup(id, setup);
                        return false;
                    }
                    Next();
                    continue;
                case Phase.Landblock:
                    _physics.StageCollisionAssets(
                        _admission,
                        _prepared,
                        new RuntimeLandblockCollisionAssets(
                            _admission.LandblockId,
                            _terrain,
                            _cellSurfaces,
                            _portalPlanes,
                            _origin.X,
                            _origin.Y,
                            _currentCellId));
                    Next();
                    return false;
                case Phase.Statics:
                    if (_cursor < _landblock.Entities.Count)
                    {
                        _ = LandblockPhysicsContentBuilder.PublishStaticEntity(
                            _prepared.Engine,
                            cache,
                            _landblock,
                            _origin,
                            _landblock.Entities[_cursor++]);
                        return false;
                    }
                    Next();
                    continue;
                case Phase.RefloodCapture:
                    _refloodOwners = _prepared.Engine.ShadowObjects
                        .CaptureRefloodOwnersForLandblock(_landblock.LandblockId);
                    Next();
                    return false;
                case Phase.Reflood:
                    if (_cursor < _refloodOwners.Length)
                    {
                        _prepared.Engine.ShadowObjects.RefloodOwnerForLandblock(
                            _refloodOwners[_cursor++],
                            _landblock.LandblockId);
                        return false;
                    }
                    Next();
                    continue;
                default:
                    return true;
            }
        }
    }

    private void Next()
    {
        _phase++;
        _cursor = 0;
    }
}

/// <summary>
/// How a collision neighborhood spreads its work. By default it does everything
/// asked of it at once, on the caller's thread, which is what a client with no
/// frame to keep wants. A client drawing frames loads landblocks on worker
/// threads and stops for the frame once its share of the frame is spent.
/// </summary>
internal sealed class RuntimeCollisionPacing
{
    /// <summary>Load each landblock's content on a worker thread.</summary>
    public bool LoadInBackground { get; init; }

    /// <summary>
    /// Asked between steps of work; true stops the work until the next
    /// call. Null never stops.
    /// </summary>
    public Func<bool>? IsFrameSpent { get; init; }
}

/// <summary>One landblock's collision content, loaded.</summary>
internal sealed record LoadedCollisionContent(
    LoadedLandblock Landblock,
    LandblockCollisionBuild Collisions);

internal sealed class RuntimeCollisionNeighborhood
    : IRuntimeCollisionNeighborhood,
      AcDream.Runtime.Session.IRuntimeRemotePlacementServiceWindow
{
    internal readonly record struct PublicationSpec(
        uint LandblockId,
        Vector3 Origin,
        bool Required);

    private readonly GameRuntime _runtime;
    private readonly IRuntimeCollisionContent _content;
    private readonly RuntimeCollisionPacing _pacing;
    private readonly Dictionary<PublicationSpec, Task<LoadedCollisionContent?>> _loads = [];
    private CancellationTokenSource? _loadsSuperseded;
    private readonly List<Task> _workers = [];
    private bool _backgroundStopped;
    private readonly HashSet<uint> _resident = [];
    private readonly Queue<uint> _retirementQueue = [];
    private readonly Queue<PublicationSpec> _publicationQueue = [];
    private RuntimeCollisionGenerationTransaction? _pendingPublication;
    private bool _pendingPublicationCancellation;
    private bool _resetRequired;
    private bool _slideRequired;
    private bool _publicationPlanBuilt;
    private uint _frameLandblock;
    private RuntimeGenerationToken _frameGeneration;
    private uint _requestedFrameLandblock;
    private RuntimeGenerationToken _requestedFrameGeneration;
    private uint _requestedCenterLandblock;
    private uint _requestedFullCell;
    private uint _centerLandblock;

    /// <summary>
    /// What was published in the previous frame is still being withdrawn: a
    /// landblock resident now may be one measured from the old frame.
    /// </summary>
    private bool _retiringOldFrame;

    internal RuntimeCollisionNeighborhood(
        GameRuntime runtime,
        IRuntimeCollisionContent content,
        RuntimeCollisionPacing? pacing = null)
    {
        _runtime = runtime
            ?? throw new ArgumentNullException(nameof(runtime));
        _content = content
            ?? throw new ArgumentNullException(nameof(content));
        _pacing = pacing ?? new RuntimeCollisionPacing();
    }

    public void CenterOn(uint fullCellId)
    {
        uint center = CanonicalLandblock(fullCellId);
        if (center == 0x0000FFFFu)
        {
            throw new ArgumentOutOfRangeException(
                nameof(fullCellId),
                "A collision neighborhood requires a real destination cell.");
        }
        // The collision is measured in the frame the runtime measures every
        // position in. A new frame (the character arrived somewhere through
        // a portal, or a new session began) withdraws everything and
        // publishes afresh; another place in the same frame only moves the
        // window, as a walk does.
        uint frame = DesiredFrame(center);
        RuntimeGenerationToken generation = _runtime.Generation;
        bool frameMoved = _resetRequired
            ? _requestedFrameLandblock != frame
                || _requestedFrameGeneration != generation
            : _frameLandblock != frame
                || _frameGeneration != generation;
        if (frameMoved)
        {
            _requestedFrameLandblock = frame;
            _requestedFrameGeneration = generation;
            _requestedCenterLandblock = center;
            _requestedFullCell = fullCellId;
            _resetRequired = true;
            _publicationPlanBuilt = false;
            _publicationQueue.Clear();
            _pendingPublicationCancellation =
                _pendingPublication is not null;
        }
        else if (_requestedCenterLandblock != center)
        {
            _requestedCenterLandblock = center;
            _requestedFullCell = fullCellId;
            if (!_resetRequired)
                _slideRequired = true;
            _publicationPlanBuilt = false;
            _publicationQueue.Clear();
            _pendingPublicationCancellation =
                _pendingPublication is not null;
        }
        else
        {
            _requestedFullCell = fullCellId;
        }

        if (_centerLandblock == center
            && _resident.Contains(center)
            && _runtime.EntityObjects.Physics.Engine
                .IsLandblockTerrainResident(center))
        {
            _runtime.EntityObjects.Physics.Engine
                .UpdatePlayerCurrCell(fullCellId);
            return;
        }
        AdvanceWork();
    }

    public void Follow(uint fullCellId)
    {
        uint center = CanonicalLandblock(fullCellId);
        // Nothing to follow from until a frame exists, and a new frame the
        // server asked for finishes before the window moves inside it.
        if (center == 0x0000FFFFu
            || _frameLandblock == 0u
            || _resetRequired)
        {
            return;
        }
        if (center != _requestedCenterLandblock)
        {
            _requestedCenterLandblock = center;
            _requestedFullCell = fullCellId;
            _slideRequired = true;
            _publicationPlanBuilt = false;
            _publicationQueue.Clear();
            _pendingPublicationCancellation =
                _pendingPublication is not null;
            AdvanceWork();
            return;
        }
        _requestedFullCell = fullCellId;
        if (!IsQuiescent || _retirementQueue.Count != 0)
            AdvanceWork();
    }

    public bool IsWithinServiceWindow(uint fullCellId)
    {
        if (_requestedCenterLandblock == 0u)
            return true;
        uint target = CanonicalLandblock(fullCellId);
        int dx = Math.Abs(
            (int)((target >> 24) & 0xFFu)
            - (int)((_requestedCenterLandblock >> 24) & 0xFFu));
        int dy = Math.Abs(
            (int)((target >> 16) & 0xFFu)
            - (int)((_requestedCenterLandblock >> 16) & 0xFFu));
        return dx <= 1 && dy <= 1;
    }

    public bool IsQuiescent =>
        _pendingPublication is null
        && _publicationQueue.Count == 0
        && !_pendingPublicationCancellation;

    public bool IsReady(uint fullCellId)
    {
        uint center = CanonicalLandblock(fullCellId);
        if (_requestedCenterLandblock == center)
        {
            _requestedFullCell = fullCellId;
            AdvanceWork();
        }
        // The destination's own landblock, published in the frame the
        // runtime measures in, is what an arrival needs. The rest of the ring
        // may still be publishing, and a landblock commits only once the
        // placements in it are acknowledged -- the arrival's own placement
        // among them, so the arrival cannot wait for the whole ring.
        if (_requestedCenterLandblock != center
            || _resetRequired
            || _retiringOldFrame
            || !_resident.Contains(center)
            || !_runtime.EntityObjects.Physics.Engine
                .IsLandblockTerrainResident(center))
        {
            return false;
        }
        return (fullCellId & 0xFFFFu) < 0x0100u
            || _runtime.EntityObjects.Physics.DataCache
                .GetCellStruct(fullCellId) is not null;
    }

    private bool IsCollisionCurrentlyPublished(uint fullCellId)
    {
        uint landblock = CanonicalLandblock(fullCellId);
        if (!_resident.Contains(landblock)
            || !_runtime.EntityObjects.Physics.Engine
                .IsLandblockTerrainResident(landblock))
        {
            return false;
        }
        return (fullCellId & 0xFFFFu) < 0x0100u
            || _runtime.EntityObjects.Physics.DataCache
                .GetCellStruct(fullCellId) is not null;
    }

    public bool IsCollisionPublished(uint fullCellId) =>
        IsCollisionCurrentlyPublished(fullCellId);

    bool AcDream.Runtime.Session.IRuntimeRemotePlacementServiceWindow
        .IsWithinServiceWindow(uint fullCellId) =>
        IsCollisionCurrentlyPublished(fullCellId);

    private RuntimeCollisionGenerationTransaction? CreatePublication(
        uint landblockId,
        Vector3 origin,
        uint currentCellId,
        bool required,
        LoadedCollisionContent? loaded)
    {
        if (loaded is not { } content)
        {
            if (required)
            {
                throw new InvalidDataException(
                    $"Required landblock 0x{landblockId:X8} is missing.");
            }
            return null;
        }

        RuntimePhysicsState physics = _runtime.EntityObjects.Physics;
        TerrainSurface terrain =
            LandblockPhysicsContentBuilder.BuildTerrainSurface(
                content.Landblock,
                _content.HeightTable.AsSpan());
        return RuntimeCollisionGenerationTransaction.Begin(
            physics,
            landblockId,
            afterAdmission: null,
            (admission, prepared) =>
            {
                prepared.SetAssetClosure(
                    [.. content.Collisions.GfxObjIds],
                    [.. content.Collisions.SetupIds]);
                return new LandblockCollisionStaging(
                    physics,
                    admission,
                    prepared,
                    content,
                    terrain,
                    origin,
                    currentCellId);
            });
    }

    private void AdvanceWork()
    {
        if (_pendingPublicationCancellation
            && _pendingPublication is { } cancelling)
        {
            bool wasCommitted = cancelling.EngineMutationCommitted;
            if (!cancelling.TryCancel())
                return;
            if (wasCommitted || cancelling.EngineMutationCommitted)
                _resident.Add(cancelling.LandblockId);
            _pendingPublication = null;
            _pendingPublicationCancellation = false;
        }

        if (_resetRequired)
        {
            _retirementQueue.Clear();
            foreach (uint landblock in _resident)
                _retirementQueue.Enqueue(landblock);
            _centerLandblock = 0u;
            _retiringOldFrame = _retirementQueue.Count != 0;
            _frameLandblock = _requestedFrameLandblock;
            _frameGeneration = _requestedFrameGeneration;
            _resetRequired = false;
            _slideRequired = false;
        }

        if (_slideRequired)
        {
            _retirementQueue.Clear();
            foreach (uint landblock in _resident)
            {
                if (!IsInWindow(_requestedCenterLandblock, landblock))
                    _retirementQueue.Enqueue(landblock);
            }
            _slideRequired = false;
        }

        while (_retirementQueue.TryPeek(out uint retiring))
        {
            if (IsFrameSpent())
                return;
            RuntimeCollisionMutationResult result = _runtime.EntityObjects
                .Physics.WithdrawCollision(retiring);
            if (!result.Completed)
                return;
            _retirementQueue.Dequeue();
            _resident.Remove(retiring);
        }
        _retiringOldFrame = false;

        // Released: nothing is asked for until the next arrival.
        if (_requestedCenterLandblock == 0u)
            return;

        if (!_publicationPlanBuilt)
        {
            BuildPublicationPlan(
                _requestedCenterLandblock,
                _frameLandblock,
                _resident,
                _publicationQueue);
            _publicationPlanBuilt = true;
            StartBackgroundLoads();
        }

        while (true)
        {
            if (IsFrameSpent())
                return;
            if (_pendingPublication is null)
            {
                if (!_publicationQueue.TryPeek(out PublicationSpec spec))
                {
                    _centerLandblock = _requestedCenterLandblock;
                    _runtime.EntityObjects.Physics.Engine
                        .UpdatePlayerCurrCell(_requestedFullCell);
                    return;
                }
                if (!TryTakeLoaded(spec, out LoadedCollisionContent? loaded))
                    return;
                _publicationQueue.Dequeue();
     _pendingPublication = CreatePublication(
                    spec.LandblockId,
                    spec.Origin,
                    _requestedFullCell,
                    spec.Required,
                    loaded);
                if (_pendingPublication is null)
                    continue;
            }

            RuntimeCollisionGenerationAdvance advance;
            RuntimePhysicsState physics = _runtime.EntityObjects.Physics;
            physics.CollisionWorkFrameSpent = _pacing.IsFrameSpent;
            try
            {
                advance = _pendingPublication.Advance();
            }
            catch when (_pendingPublication.CancellationRequested
                && !_pendingPublication.EngineMutationCommitted)
            {
                // Staging failed and withdrew its generation: nothing of
                // this landblock was published, and the next one is free
                // to go.
                _pendingPublication = null;
                throw;
            }
            finally
            {
                physics.CollisionWorkFrameSpent = null;
            }
            if (advance.Completed)
            {
                _resident.Add(_pendingPublication.LandblockId);
                _pendingPublication = null;
                continue;
            }
            if (advance.WaitingForProjectionAcknowledgement)
                return;
            if (advance.YieldToCaller)
                return;
            if (!advance.Progressed)
                throw new InvalidOperationException(
                    "Collision publication made no progress.");
        }
    }

    /// <summary>
    /// The landblocks to publish for a window centred on
    /// <paramref name="center"/>, centre first, each placed by its distance
    /// from <paramref name="frame"/> - the landblock whose corner is the
    /// world's zero. What is already resident stays as it is.
    /// </summary>
    internal static void BuildPublicationPlan(
        uint center,
        uint frame,
        IReadOnlySet<uint> resident,
        Queue<PublicationSpec> destination)
    {
        int centerX = (int)((center >> 24) & 0xFFu);
        int centerY = (int)((center >> 16) & 0xFFu);
        int frameX = (int)((frame >> 24) & 0xFFu);
        int frameY = (int)((frame >> 16) & 0xFFu);
        if (!resident.Contains(center))
        {
            destination.Enqueue(new PublicationSpec(
                center,
                new Vector3(
                    (centerX - frameX) * 192f,
                    (centerY - frameY) * 192f,
                    0f),
                Required: true));
        }
        for (int dx = -1; dx <= 1; dx++)
        {
            for (int dy = -1; dy <= 1; dy++)
            {
                if (dx == 0 && dy == 0)
                    continue;
                int x = centerX + dx;
                int y = centerY + dy;
                if ((uint)x > byte.MaxValue || (uint)y > byte.MaxValue)
                    continue;
                uint landblock = ((uint)x << 24) | ((uint)y << 16) | 0xFFFFu;
                if (resident.Contains(landblock))
                    continue;
                destination.Enqueue(new PublicationSpec(
                    landblock,
                    new Vector3((x - frameX) * 192f, (y - frameY) * 192f, 0f),
                    Required: false));
            }
        }
    }

    internal static bool IsInWindow(uint center, uint landblock)
    {
        int dx = Math.Abs(
            (int)((landblock >> 24) & 0xFFu) - (int)((center >> 24) & 0xFFu));
        int dy = Math.Abs(
            (int)((landblock >> 16) & 0xFFu) - (int)((center >> 16) & 0xFFu));
        return dx <= 1 && dy <= 1;
    }

    /// <summary>
    /// The landblock whose corner is the world's zero for the collision
    /// published now; 0 before anything is.
    /// </summary>
    internal uint FrameLandblock => _frameLandblock;

    /// <summary>Whether any landblock's collision is published.</summary>
    internal bool HasPublishedCollision => _resident.Count != 0;

    /// <summary>
    /// Withdraws every landblock this published and forgets the centre and
    /// the frame: the session that needed them has ended. The withdrawal
    /// runs over this call and the following <see cref="Pump"/> calls.
    /// </summary>
    internal void Release()
    {
        _requestedFrameLandblock = 0u;
        _requestedFrameGeneration = default;
        _requestedCenterLandblock = 0u;
        _requestedFullCell = 0u;
        _resetRequired = true;
        _slideRequired = false;
        _publicationPlanBuilt = false;
        _publicationQueue.Clear();
        _pendingPublicationCancellation = _pendingPublication is not null;
        AdvanceWork();
    }

    /// <summary>Carries on with whatever work is outstanding.</summary>
    internal void Pump() => AdvanceWork();

    /// <summary>
    /// The runtime's frame. Before the runtime has one (no character placed
    /// yet) the requested centre is the frame: it is the landblock the
    /// runtime adopts when the character is created there.
    /// </summary>
    private uint DesiredFrame(uint center)
    {
        uint runtimeFrame =
            _runtime.EntityObjects.Physics.WorldFrameCenterLandblockId;
        return runtimeFrame != 0u ? runtimeFrame : center;
    }

    private bool IsFrameSpent() => _pacing.IsFrameSpent?.Invoke() == true;

    /// <summary>
    /// Starts loading the landblocks the plan asks for, in the plan's order,
    /// one after another on one worker thread, when this neighborhood loads
    /// in the background. A plan that replaces this one stops the loads it
    /// no longer wants; their results are never read.
    /// </summary>
    /// <summary>
    /// Stops loading in the background, before the game files are closed:
    /// no further load starts, and this returns once the loads in hand have
    /// returned. Their results are never read. Loading is done on the
    /// caller's thread from then on.
    /// </summary>
    /// <summary>
    /// Whether the loads started in the background have been told to stop
    /// (by <see cref="StopBackgroundWork"/> or a newer plan); safe to read
    /// from any thread.
    /// </summary>
    internal bool BackgroundLoadsCancelled =>
        Volatile.Read(ref _loadsSuperseded)?.IsCancellationRequested == true;

    internal void StopBackgroundWork()
    {
        _backgroundStopped = true;
        _loadsSuperseded?.Cancel();
        Task[] running = [.. _workers, .. _loads.Values];
        _workers.Clear();
        _loads.Clear();
        // Waits for completion only: a load that failed is no longer wanted,
        // and its failure is not this caller's.
        Task.WhenAll(running)
            .ContinueWith(
                static _ => { },
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default)
            .Wait();
    }

    private void StartBackgroundLoads()
    {
        if (!_pacing.LoadInBackground || _backgroundStopped)
            return;
        _loadsSuperseded?.Cancel();
        var superseded = new CancellationTokenSource();
        _loadsSuperseded = superseded;
        var wanted = new HashSet<PublicationSpec>(_publicationQueue);
        // Only a finished load carries over to the new plan: one still
        // running belongs to the superseded worker and is loaded again.
        foreach (PublicationSpec stale in _loads
                     .Where(pair => !wanted.Contains(pair.Key)
                         || !pair.Value.IsCompletedSuccessfully)
                     .Select(pair => pair.Key)
                     .ToArray())
        {
            _loads.Remove(stale);
        }
        var batch = new List<(PublicationSpec Spec, TaskCompletionSource<LoadedCollisionContent?> Result)>();
        foreach (PublicationSpec spec in _publicationQueue)
        {
            if (_loads.ContainsKey(spec))
                continue;
            var result = new TaskCompletionSource<LoadedCollisionContent?>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            _loads[spec] = result.Task;
            batch.Add((spec, result));
        }
        if (batch.Count == 0)
            return;
        CancellationToken token = superseded.Token;
        _workers.RemoveAll(static worker => worker.IsCompleted);
        _workers.Add(Task.Run(() =>
        {
            foreach ((PublicationSpec spec, TaskCompletionSource<LoadedCollisionContent?> result) in batch)
            {
                if (token.IsCancellationRequested)
                {
                    result.TrySetCanceled(token);
                    continue;
                }
                try
                {
                    result.TrySetResult(Load(spec));
                }
                catch (Exception error)
                {
                    result.TrySetException(error);
                }
            }
        }));
    }

    /// <summary>
    /// The content for the next landblock to publish: loaded here, or taken
    /// from its worker once that has finished. False while it is still
    /// loading. A load that failed throws here, on the caller's thread.
    /// </summary>
    private bool TryTakeLoaded(
        PublicationSpec spec,
        out LoadedCollisionContent? loaded)
    {
        if (!_pacing.LoadInBackground || _backgroundStopped)
        {
            loaded = Load(spec);
            return true;
        }
        if (!_loads.TryGetValue(spec, out Task<LoadedCollisionContent?>? load))
        {
            load = Task.Run(() => Load(spec));
            _loads[spec] = load;
        }
        if (!load.IsCompleted)
        {
            loaded = null;
            return false;
        }
        _loads.Remove(spec);
        loaded = load.GetAwaiter().GetResult();
        return true;
    }

    private LoadedCollisionContent? Load(PublicationSpec spec) =>
        _content.TryLoadCollisionLandblock(
            spec.LandblockId,
            spec.Origin,
            out LoadedLandblock landblock,
            out LandblockCollisionBuild collisions)
            ? new LoadedCollisionContent(landblock, collisions)
            : null;

    private static uint CanonicalLandblock(uint fullCellId) =>
        (fullCellId & 0xFFFF0000u) | 0xFFFFu;
}
