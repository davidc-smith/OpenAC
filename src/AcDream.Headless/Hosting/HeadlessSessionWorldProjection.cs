using System.Numerics;
using AcDream.Content;
using AcDream.Core.Net.Messages;
using AcDream.Core.Physics;
using AcDream.Core.World;
using AcDream.Runtime;
using AcDream.Runtime.Entities;
using AcDream.Runtime.Gameplay;
using AcDream.Runtime.Physics;
using AcDream.Runtime.Session;
using AcDream.Runtime.World;

namespace AcDream.Headless.Hosting;

internal sealed class HeadlessSessionWorldProjection
    : IRuntimeDirectWorldProjection
{
    private readonly GameRuntime _runtime;
    private readonly IRuntimeCollisionNeighborhood _collision;
    private readonly RuntimeFirstEntryDriveController? _firstEntry;
    private readonly RuntimeAcceptedPositionDriveController? _acceptedPositionDrive;
    private readonly Action<string>? _onNonQuiescentStall;
    private uint _requestedLocalPlayerCell;

    private const int NonQuiescentStallPumpThreshold = 300;

    private int _nonQuiescentPumpCount;
    private bool _reportedNonQuiescentStall;

    internal HeadlessSessionWorldProjection(
        GameRuntime runtime,
        HeadlessProcessContentOwner.HeadlessProcessContentLease content,
        RuntimeFirstEntryDriveController? firstEntry = null,
        RuntimeAcceptedPositionDriveController? acceptedPositionDrive = null,
        Action<string>? onNonQuiescentStall = null)
        : this(
            runtime,
            new RuntimeCollisionNeighborhood(runtime, content),
            firstEntry,
            acceptedPositionDrive,
            onNonQuiescentStall)
    {
    }

    internal HeadlessSessionWorldProjection(
        GameRuntime runtime,
        IRuntimeCollisionNeighborhood collision,
        RuntimeFirstEntryDriveController? firstEntry = null,
        RuntimeAcceptedPositionDriveController? acceptedPositionDrive = null,
        Action<string>? onNonQuiescentStall = null)
    {
        _runtime = runtime
            ?? throw new ArgumentNullException(nameof(runtime));
        _collision = collision
            ?? throw new ArgumentNullException(nameof(collision));
        _firstEntry = firstEntry;
        _acceptedPositionDrive = acceptedPositionDrive;
        _onNonQuiescentStall = onNonQuiescentStall;
    }

    /// <summary>
    /// The test an accepted re-placement of another creature's body must pass
    /// before it is carried out here: the collision it would land on is
    /// published. Without a window that is the whole of the question; a client
    /// with one also asks whether the landblock is drawn.
    /// </summary>
    /// <remarks>
    /// Made once and kept: it is read while the session is being built, and a
    /// fresh one per read would be a new object on a path that is asked the
    /// same question over and over.
    /// </remarks>
    internal IRuntimeRemotePlacementServiceWindow RemotePlacementServiceWindow =>
        _remotePlacementServiceWindow ??=
            new PublishedCollisionServiceWindow(_collision);

    private IRuntimeRemotePlacementServiceWindow?
        _remotePlacementServiceWindow;

    private sealed class PublishedCollisionServiceWindow(
        IRuntimeCollisionNeighborhood collision)
        : IRuntimeRemotePlacementServiceWindow
    {
        public bool IsWithinServiceWindow(uint landblockId) =>
            collision.IsCollisionPublished(landblockId);
    }

    public void ProjectSpawn(
        RuntimeEntityRecord record,
        bool isLocalPlayer)
    {
        if (isLocalPlayer
            && record.ServerGuid == _runtime.PlayerIdentity.ServerGuid
            && record.Snapshot.Position is { LandblockId: not 0u } position)
        {
            // Order matters, and it is the placement's, not the collision's.
            // Publishing collision for a landblock requires quiescence over
            // that landblock's prefix, and anything still waiting for its
            // mover to be prepared there is cancelled outright to obtain it -
            // a cancellation the owning initial-Create residence cannot come
            // back from. So the placement this spawn just opened is driven to
            // its preparation FIRST; a prepared placement whose destination
            // cell is not resident yet parks instead of being cancelled, and
            // the publication below is what wakes it. The conductor still
            // never runs while an admission is open, so the pump is the
            // quiescent case only - the same gate every other drive site uses.
            PumpPlacementDrivesIfQuiescent();
            _requestedLocalPlayerCell = position.LandblockId;
            PrepareLocalPlacementBeforeCenterOn();
            _collision.CenterOn(position.LandblockId);
        }
        else if (!isLocalPlayer
            && record.Snapshot.Position is
                { LandblockId: not 0u } remotePosition
            && !_collision.IsWithinServiceWindow(remotePosition.LandblockId))
        {
            _ = _runtime.EntityObjects
                .TryConvertInitialResidenceToCellessRoute(record);
        }
        if (!_collision.IsQuiescent)
            return;
        _firstEntry?.DriveAll();
        _acceptedPositionDrive?.Advance();
    }

    public void ProjectPosition(
        RuntimeEntityRecord record,
        bool isLocalPlayer,
        PositionTimestampDisposition disposition)
    {
        if (!isLocalPlayer)
            return;

        if (_runtime.MovementOwner.Controller is null)
        {
            if (record.Snapshot.Position is { LandblockId: not 0u } position)
            {
                // Same ordering rule as ProjectSpawn: preparation before
                // publication, or the publication cancels the preparation.
                PumpPlacementDrivesIfQuiescent();
                _requestedLocalPlayerCell = position.LandblockId;
                PrepareLocalPlacementBeforeCenterOn();
                _collision.CenterOn(position.LandblockId);
            }
            if (!_collision.IsQuiescent)
                return;
            _firstEntry?.DriveAll();
            _acceptedPositionDrive?.Advance();
        }
    }

    /// <summary>
    /// Advances every placement drive one pass before a collision
    /// neighbourhood mutation, so that placements reach their prepared (and
    /// therefore parkable) state instead of being cancelled as unprepared
    /// debt against the prefix about to be published. Skipped while the
    /// neighbourhood is working: the conductor never runs against a half
    /// published collision generation.
    /// </summary>
    private void PumpPlacementDrivesIfQuiescent()
    {
        if (!_collision.IsQuiescent)
            return;
        _firstEntry?.DriveAll();
        _acceptedPositionDrive?.Advance();
    }

    public void CenterOnAcceptedForcePosition(RuntimeEntityRecord record)
    {
        if (record.ServerGuid != _runtime.PlayerIdentity.ServerGuid
            || record.Snapshot.Position is not { LandblockId: not 0u } position)
        {
            return;
        }

        _requestedLocalPlayerCell = position.LandblockId;
        PrepareLocalPlacementBeforeCenterOn();
        _collision.CenterOn(position.LandblockId);
    }

    // Committing a landblock's collision generation cancels any of the local
    // player's own still-unprepared placements, so drive one first.
    private void PrepareLocalPlacementBeforeCenterOn()
    {
        if (!_collision.IsQuiescent)
            return;
        _firstEntry?.DriveAll();
        _acceptedPositionDrive?.Advance();
    }

    internal void PumpFirstEntry()
    {
        if (_runtime.MovementOwner.Controller is
            {
                State: not PlayerState.PortalSpace,
                CellId: not 0u,
            } controller)
        {
            _collision.Follow(controller.CellId);
        }
        if (_requestedLocalPlayerCell != 0u)
            _ = _collision.IsReady(_requestedLocalPlayerCell);
        if (!_collision.IsQuiescent)
        {
            if (++_nonQuiescentPumpCount >= NonQuiescentStallPumpThreshold
                && !_reportedNonQuiescentStall)
            {
                _reportedNonQuiescentStall = true;
                _onNonQuiescentStall?.Invoke(FormattableString.Invariant(
                    $"collision neighborhood non-quiescent for {_nonQuiescentPumpCount} pumps, requested landblock=0x{_requestedLocalPlayerCell:X8}"));
            }
            return;
        }
        _nonQuiescentPumpCount = 0;
        _reportedNonQuiescentStall = false;
        _firstEntry?.DriveAll();
        _acceptedPositionDrive?.Advance();
    }

    public void BeginTeleport()
    {
        if (_runtime.MovementOwner.Controller is { } controller)
            controller.State = PlayerState.PortalSpace;
    }

    private bool _awaitingPortalWake;

    private long _awaitingPortalWakeGeneration;
    private ushort _awaitingPortalWakeSequence;

    private int _notApplicableRetryCount;
    private const int NotApplicableRetryBudget = 50;

    public RuntimeDestinationReadiness PrepareDestination(
        long revealGeneration,
        RuntimeTeleportDestination destination,
        RuntimeWorldHostProjectionToken portal)
    {
        _collision.CenterOn(destination.CellId);
        if (_acceptedPositionDrive is null)
        {
            throw new InvalidOperationException(
                "Headless portal placement requires a wired "
                + "RuntimeAcceptedPositionDriveController - a composition "
                + "regression must not silently disable placement (A3).");
        }

        if (_awaitingPortalWake
            && (_awaitingPortalWakeGeneration != revealGeneration
                || _awaitingPortalWakeSequence != destination.TeleportSequence))
        {
            _awaitingPortalWake = false;
        }

        bool committed;
        if (_awaitingPortalWake)
        {
            if (_acceptedPositionDrive.PendingCount != 0)
            {
                committed = false;
            }
            else
            {
                _awaitingPortalWake = false;
                committed = _acceptedPositionDrive.TryConsumePortalCommit(
                    revealGeneration, destination.TeleportSequence);
            }
        }
        else if (!_collision.IsReady(destination.CellId))
        {
            committed = false;
        }
        else
        {
            var authority = new RuntimePortalPlacementAuthority(
                Present: true,
                RevealGeneration: revealGeneration,
                TeleportSequence: destination.TeleportSequence,
                Projection: portal);
            RuntimeAcceptedPositionExecutionStatus status =
                _acceptedPositionDrive.TryExecuteAcceptedPortalArrival(
                    destination,
                    authority);
            switch (status)
            {
                case RuntimeAcceptedPositionExecutionStatus.Committed:
                    committed = true;
                    _notApplicableRetryCount = 0;
                    break;
                case RuntimeAcceptedPositionExecutionStatus.DeferredCell:
                    _awaitingPortalWake = true;
                    _awaitingPortalWakeGeneration = revealGeneration;
                    _awaitingPortalWakeSequence = destination.TeleportSequence;
                    committed = false;
                    break;
                case RuntimeAcceptedPositionExecutionStatus.Contention:
                    // Transient - some other operation still owns the
                    // entity's placement token. Retried next pump; never a
                    // hard error, matching the graphical arm's D-T5
                    // refusal shape.
                    committed = false;
                    break;
                case RuntimeAcceptedPositionExecutionStatus.NotApplicable:
                    _notApplicableRetryCount++;
                    PhysicsDiagnostics.LogTeleport(
                        "REFUSED",
                        destination.CellId,
                        $"cause=NotApplicable attempt={_notApplicableRetryCount}");
                    if (_notApplicableRetryCount > NotApplicableRetryBudget)
                    {
                        throw new InvalidOperationException(
                            "Headless portal placement stayed NotApplicable "
                            + $"for {_notApplicableRetryCount} consecutive "
                            + "attempts (no canonical body, or an active "
                            + "initial-Create residence still owns the "
                            + "record) - exceeded the bounded retry budget.");
                    }
                    committed = false;
                    break;
                default:
                    throw new InvalidOperationException(
                        $"Headless portal placement refused with "
                        + $"status={status} even though the destination's "
                        + "collision neighborhood reported ready - the "
                        + "reveal itself is stale, not recoverable by "
                        + "waiting (contract §4 item 5 forbids "
                        + "acknowledging a materialization that did not "
                        + "happen).");
            }
        }

        if (committed && _runtime.MovementOwner.Controller is { } controller)
            controller.State = PlayerState.InWorld;

        bool indoor = (destination.CellId & 0xFFFFu) >= 0x0100u;
        return new RuntimeDestinationReadiness(
            revealGeneration,
            destination.CellId,
            indoor,
            IsUnhydratable: false,
            RequiredRenderRadius: indoor ? 0 : 1,
            IsRenderNeighborhoodReady: true,
            AreCompositeTexturesReady: true,
            IsCollisionReady: committed);
    }

}
