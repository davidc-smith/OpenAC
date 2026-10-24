using AcDream.Content;
using AcDream.Core.Net;
using AcDream.Core.Physics;
using AcDream.Runtime;
using AcDream.Runtime.Entities;
using AcDream.Runtime.Gameplay;
using AcDream.Runtime.Physics;
using AcDream.Runtime.Session;
using AcDream.Runtime.Session.Windowless;

namespace AcDream.App.Hosting;

/// <summary>
/// What a window answers for the session's drives while it is attached: the
/// body the character is given at its first entry (from the shape it draws),
/// whether a landblock is drawn and near enough to re-place another
/// creature's body in, and what it knows about the bodies it draws.
/// </summary>
internal sealed record GraphicalSessionDrivePresentation(
    Func<RuntimeEntityRecord, RuntimeLocalPlayerPhysicsActivationPreparation>
        LocalActivation,
    IRuntimeRemotePlacementServiceWindow RemotePlacementServiceWindow,
    RuntimeRemoteArmingHostFacts ArmingFacts);

/// <summary>
/// The drives that carry the character and every other creature into the
/// world, one of each for the life of the session: the first entry of each
/// body, a correction of the character's place, and the arming and
/// re-placement of every other body. Whichever route holds the world
/// connection -- the window's, or the route of a session with no window --
/// drives these same instances, so work in flight when a window opens or
/// closes is handed to the next route instead of being lost.
///
/// What only a window knows is asked of the window attached at the moment
/// it is needed; with none, each question is answered as a client with no
/// window answers it.
/// </summary>
internal sealed class GraphicalSessionDrives
{
    private readonly RuntimeRemoteArmingHostFacts _recordFacts;
    private readonly Func<uint, bool> _isCollisionPublished;
    private GraphicalSessionDrivePresentation? _presentation;

    internal GraphicalSessionDrives(
        GameRuntime runtime,
        IPreparedCollisionSource collision,
        LocalPlayerOutboundController outbound,
        Func<WorldSession?> session,
        Func<uint, bool> isCollisionPublished)
    {
        ArgumentNullException.ThrowIfNull(runtime);
        ArgumentNullException.ThrowIfNull(collision);
        ArgumentNullException.ThrowIfNull(outbound);
        ArgumentNullException.ThrowIfNull(session);
        _isCollisionPublished = isCollisionPublished
            ?? throw new ArgumentNullException(nameof(isCollisionPublished));
        _recordFacts = RuntimeRemoteArmingHostFacts.FromRecords(runtime.EntityObjects);
        RemotePlacementServiceWindow = new ServiceWindow(this);
        FirstEntry = new RuntimeFirstEntryDriveController(
            runtime.EntityObjects,
            runtime.Clock,
            collision,
            () => PlayerMovementConstructionOptions.From(
                runtime.CharacterOwner.MovementSkills.Snapshot),
            PrepareLocalActivation);
        AcceptedPosition = new RuntimeAcceptedPositionDriveController(
            runtime.EntityObjects,
            runtime.Clock,
            collision,
            outbound,
            () => runtime.Generation,
            () => runtime.PlayerIdentity.ServerGuid,
            () => runtime.MovementOwner.Controller,
            () => runtime.CharacterOwner.UsePositionFromServer,
            session,
            () => runtime.MovementOwner,
            isPortalAuthorityCurrent: portal => runtime.TransitOwner
                .CanPlacePortalDestination(
                    portal.RevealGeneration,
                    portal.TeleportSequence,
                    portal.Projection.DestinationCell));
        RemoteArming = RuntimeRemoteArming.Create(
            runtime.EntityObjects,
            runtime.Clock,
            collision,
            RemotePlacementServiceWindow,
            new RuntimeRemoteArmingHostFacts(
                DrawnBody: guid => Facts.DrawnBody(guid),
                WireOriginToWorld: (cell, x, y, z) =>
                    Facts.WireOriginToWorld(cell, x, y, z),
                InteractionTargetPosition: guid =>
                    Facts.InteractionTargetPosition(guid)));
    }

    /// <summary>The body a character gets at its first entry with no window.</summary>
    internal static RuntimeLocalPlayerPhysicsActivationPreparation WithoutWindow { get; } =
        new(
            Radius: DefaultPlayerBody.Radius,
            Height: DefaultPlayerBody.Height,
            RuntimeLocalPlayerShadowDisposition.ProvenShapeless);

    internal RuntimeFirstEntryDriveController FirstEntry { get; }

    internal RuntimeAcceptedPositionDriveController AcceptedPosition { get; }

    internal RuntimeRemoteArming RemoteArming { get; }

    internal RuntimeRemotePlacementDriveController RemotePlacement =>
        RemoteArming.PlacementDrive;

    /// <summary>The drives as the route of a session with no window takes them.</summary>
    internal RuntimeWindowlessSessionDrives Windowless =>
        new(FirstEntry, AcceptedPosition, RemoteArming);

    /// <summary>Whether a window answers the drives' questions right now.</summary>
    internal bool HasPresentation => _presentation is not null;

    /// <summary>
    /// The body the character gets at its first entry: from the shape the
    /// attached window draws, or the default body with none.
    /// </summary>
    internal RuntimeLocalPlayerPhysicsActivationPreparation PrepareLocalActivation(
        RuntimeEntityRecord record) =>
        _presentation is { } window
            ? window.LocalActivation(record)
            : WithoutWindow;

    /// <summary>
    /// Whether another creature's body may be re-placed in a landblock: where
    /// the attached window draws, or wherever the session's collision is
    /// published with none.
    /// </summary>
    internal IRuntimeRemotePlacementServiceWindow RemotePlacementServiceWindow { get; }

    private RuntimeRemoteArmingHostFacts Facts =>
        _presentation?.ArmingFacts ?? _recordFacts;

    /// <summary>
    /// Lends the drives what a window knows, until the returned lease is
    /// disposed. One window at a time.
    /// </summary>
    internal IDisposable Present(GraphicalSessionDrivePresentation presentation)
    {
        ArgumentNullException.ThrowIfNull(presentation);
        if (_presentation is not null)
        {
            throw new InvalidOperationException(
                "A window already answers for the session's drives.");
        }

        _presentation = presentation;
        return new Lease(this, presentation);
    }

    /// <summary>
    /// Runs <paramref name="replaceRoute"/> -- the route holding the world
    /// connection lets go and the next takes it -- with the drives' work
    /// handed from the one to the other. Work no route took is abandoned.
    /// </summary>
    internal void HandOver(Action replaceRoute)
    {
        ArgumentNullException.ThrowIfNull(replaceRoute);
        FirstEntry.BeginRouteHandOver();
        AcceptedPosition.BeginRouteHandOver();
        RemotePlacement.BeginRouteHandOver();
        try
        {
            replaceRoute();
        }
        finally
        {
            FirstEntry.EndRouteHandOver();
            AcceptedPosition.EndRouteHandOver();
            RemotePlacement.EndRouteHandOver();
        }
    }


    /// <summary>
    /// Stops asking the attached window anything, at once: it is going away
    /// while the session plays on. Its lease, given back later, is a no-op.
    /// </summary>
    internal void ForgetPresentation() => _presentation = null;
    private void Release(GraphicalSessionDrivePresentation presentation)
    {
        if (ReferenceEquals(_presentation, presentation))
            _presentation = null;
    }

    private sealed class ServiceWindow(GraphicalSessionDrives owner)
        : IRuntimeRemotePlacementServiceWindow
    {
        public bool IsWithinServiceWindow(uint landblockId) =>
            owner._presentation is { } window
                ? window.RemotePlacementServiceWindow.IsWithinServiceWindow(landblockId)
                : owner._isCollisionPublished(landblockId);
    }

    private sealed class Lease(
        GraphicalSessionDrives owner,
        GraphicalSessionDrivePresentation presentation) : IDisposable
    {
        private GraphicalSessionDrives? _owner = owner;

        public void Dispose()
        {
            _owner?.Release(presentation);
            _owner = null;
        }
    }
}
