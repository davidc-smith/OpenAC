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
}

internal interface IRuntimeCollisionNeighborhood
{
    void CenterOn(uint fullCellId);

    /// <summary>
    /// Keeps the published collision around the local player while it walks:
    /// the window of landblocks moves with the player, the frame the world is
    /// measured in stays where it was. <see cref="CenterOn"/> starts a new
    /// frame and is for arrivals the server decides (login, portals, forced
    /// moves); walking into the next landblock is not one of those.
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
    private bool _ownerCaptureCommitted;
    private int _refreshCursor;
    private bool _sealCommitted;

    private RuntimeCollisionGenerationTransaction(
        RuntimePhysicsState physics,
        RuntimeCollisionAdmission admission,
        PreparedLandblockCollisionGeneration prepared)
    {
        _physics = physics;
        _admission = admission;
        _prepared = prepared;
    }

    internal uint LandblockId => _admission.LandblockId;
    internal bool EngineMutationCommitted { get; private set; }
    internal bool CompletionCommitted { get; private set; }
    internal bool CancellationRequested { get; private set; }

    internal static RuntimeCollisionGenerationTransaction Begin(
        RuntimePhysicsState physics,
        uint landblockId,
        Action<RuntimeCollisionAdmission>? afterAdmission,
        Action<RuntimeCollisionAdmission,
            PreparedLandblockCollisionGeneration> stage)
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
            stage(admission, prepared);
            return new RuntimeCollisionGenerationTransaction(
                physics,
                admission,
                prepared);
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
    private readonly HashSet<uint> _resident = [];
    private readonly Queue<uint> _retirementQueue = [];
    private readonly Queue<PublicationSpec> _publicationQueue = [];
    private RuntimeCollisionGenerationTransaction? _pendingPublication;
    private bool _pendingPublicationCancellation;
    private bool _resetRequired;
    private bool _slideRequired;
    private bool _publicationPlanBuilt;
    private uint _frameLandblock;
    private uint _requestedCenterLandblock;
    private uint _requestedFullCell;
    private uint _centerLandblock;

    internal RuntimeCollisionNeighborhood(
        GameRuntime runtime,
        IRuntimeCollisionContent content)
    {
        _runtime = runtime
            ?? throw new ArgumentNullException(nameof(runtime));
        _content = content
            ?? throw new ArgumentNullException(nameof(content));
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
        if (_requestedCenterLandblock != center)
        {
            _requestedCenterLandblock = center;
            _requestedFullCell = fullCellId;
            _resetRequired = true;
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
        if (_centerLandblock != center
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
        bool required)
    {
        // The files are read under the process's lock: a plugin may be
        // reading the same files from its own thread while this host loads.
        bool loaded;
        LoadedLandblock landblock;
        LandblockCollisionBuild collisions;
        lock (_content.DatLock)
        {
            loaded = LandblockPhysicsContentBuilder.TryLoadCollisionLandblock(
                _content.Dats,
                _content.PreparedCollision,
                _content.HeightTable.AsSpan(),
                landblockId,
                origin,
                out landblock,
                out collisions);
        }
        if (!loaded)
        {
            if (required)
            {
                throw new InvalidDataException(
                    $"Required landblock 0x{landblockId:X8} is missing.");
            }
            return null;
        }

        RuntimePhysicsState physics = _runtime.EntityObjects.Physics;
        return RuntimeCollisionGenerationTransaction.Begin(
            physics,
            landblockId,
            afterAdmission: null,
            (admission, prepared) =>
            {
                prepared.SetAssetClosure(
                    [.. collisions.GfxObjIds],
                    [.. collisions.SetupIds]);
                PhysicsDataCache cache = prepared.DataCache;
                TerrainSurface terrain =
                    LandblockPhysicsContentBuilder.BuildTerrainSurface(
                        landblock,
                        _content.HeightTable.AsSpan());
                var cellSurfaces = new List<CellSurface>();
                var portalPlanes = new List<PortalPlane>();
                LandblockPhysicsContentBuilder.PublishPreparedCells(
                    cache,
                    landblock,
                    collisions,
                    origin,
                    cellSurfaces,
                    portalPlanes);
                LandblockPhysicsContentBuilder.CacheBuildings(
                    cache,
                    landblock,
                    terrain,
                    origin);
                LandblockPhysicsContentBuilder.CachePreparedObjects(
                    cache,
                    collisions);
                physics.StageCollisionAssets(
                    admission,
                    prepared,
                    new RuntimeLandblockCollisionAssets(
                        landblockId,
                        terrain,
                        cellSurfaces,
                        portalPlanes,
                        origin.X,
                        origin.Y,
                        currentCellId));
                _ = LandblockPhysicsContentBuilder
                    .PublishStaticCollision(
                        prepared.Engine,
                        cache,
                        landblock,
                        collisions,
                        origin);
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
            _frameLandblock = _requestedCenterLandblock;
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
            RuntimeCollisionMutationResult result = _runtime.EntityObjects
                .Physics.WithdrawCollision(retiring);
            if (!result.Completed)
                return;
            _retirementQueue.Dequeue();
            _resident.Remove(retiring);
        }

        if (!_publicationPlanBuilt)
        {
            BuildPublicationPlan(
                _requestedCenterLandblock,
                _frameLandblock,
                _resident,
                _publicationQueue);
            _publicationPlanBuilt = true;
        }

        while (true)
        {
            if (_pendingPublication is null)
            {
                if (!_publicationQueue.TryDequeue(out PublicationSpec spec))
                {
                    _centerLandblock = _requestedCenterLandblock;
                    _runtime.EntityObjects.Physics.Engine
                        .UpdatePlayerCurrCell(_requestedFullCell);
                    return;
                }
                _pendingPublication = CreatePublication(
                    spec.LandblockId,
                    spec.Origin,
                    _requestedFullCell,
                    spec.Required);
                if (_pendingPublication is null)
                    continue;
            }

            RuntimeCollisionGenerationAdvance advance =
                _pendingPublication.Advance();
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

    private static uint CanonicalLandblock(uint fullCellId) =>
        (fullCellId & 0xFFFF0000u) | 0xFFFFu;
}
