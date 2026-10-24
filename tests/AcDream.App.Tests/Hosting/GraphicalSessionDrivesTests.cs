using System.Numerics;
using AcDream.App.Hosting;
using AcDream.Content;
using AcDream.Content.Pak;
using AcDream.Core.Physics;
using AcDream.Core.Plugins;
using AcDream.Runtime.Entities;
using AcDream.Runtime.Physics;
using AcDream.Runtime.Session;

namespace AcDream.App.Tests.Hosting;

/// <summary>
/// The session's drives are the host's, one of each for the life of the
/// process; a window lends them what it knows while it is attached.
/// </summary>
public sealed class GraphicalSessionDrivesTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        "acdream-session-drives-" + Guid.NewGuid().ToString("N"));

    public GraphicalSessionDrivesTests() => Directory.CreateDirectory(_directory);

    public void Dispose()
    {
        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    private GraphicalSessionHost NewHost() =>
        new(
            RuntimeOptions.Parse(_directory, _ => null) with
            {
                StatusFilePath = Path.Combine(_directory, "status.jsonl"),
            },
            new WorldGameState(),
            new WorldEvents());

    [Fact]
    public void Every_window_and_the_windowless_route_drive_the_same_instances()
    {
        using GraphicalSessionHost host = NewHost();
        var collision = new NoCollision();

        GraphicalSessionDrives first = host.EnsureDrives(collision);
        GraphicalSessionDrives second = host.EnsureDrives(new NoCollision());

        Assert.Same(first, second);
        Assert.Same(first, host.Drives);
        Assert.Same(first.FirstEntry, first.Windowless.FirstEntry);
        Assert.Same(first.AcceptedPosition, first.Windowless.AcceptedPosition);
        Assert.Same(first.RemoteArming, first.Windowless.RemoteArming);
    }

    [Fact]
    public void A_window_answers_while_attached_and_the_session_answers_after_it_lets_go()
    {
        using GraphicalSessionHost host = NewHost();
        GraphicalSessionDrives drives = host.EnsureDrives(new NoCollision());
        RuntimeEntityRecord record = null!;
        var drawnBody = new RuntimeLocalPlayerPhysicsActivationPreparation(
            0.9f,
            2.1f,
            RuntimeLocalPlayerShadowDisposition.RegisteredAuthoredPayload);
        var window = new FixedServiceWindow(answer: true);

        Assert.Equal(GraphicalSessionDrives.WithoutWindow, drives.PrepareLocalActivation(record));
        // No collision is published yet, and with no window that is the
        // whole question.
        Assert.False(drives.RemotePlacementServiceWindow.IsWithinServiceWindow(0xA9B40001u));

        IDisposable lease = drives.Present(new GraphicalSessionDrivePresentation(
            _ => drawnBody,
            window,
            RuntimeRemoteArmingHostFacts.FromRecords(host.Runtime.EntityObjects)));
        Assert.True(drives.HasPresentation);
        Assert.Equal(drawnBody, drives.PrepareLocalActivation(record));
        Assert.True(drives.RemotePlacementServiceWindow.IsWithinServiceWindow(0xA9B40001u));
        Assert.Throws<InvalidOperationException>(() => drives.Present(
            new GraphicalSessionDrivePresentation(
                _ => drawnBody,
                window,
                RuntimeRemoteArmingHostFacts.FromRecords(host.Runtime.EntityObjects))));

        lease.Dispose();

        Assert.False(drives.HasPresentation);
        Assert.Equal(GraphicalSessionDrives.WithoutWindow, drives.PrepareLocalActivation(record));
        Assert.False(drives.RemotePlacementServiceWindow.IsWithinServiceWindow(0xA9B40001u));
    }

    private sealed class FixedServiceWindow(bool answer)
        : IRuntimeRemotePlacementServiceWindow
    {
        public bool IsWithinServiceWindow(uint landblockId) => answer;
    }

    private sealed class NoCollision : IPreparedCollisionSource
    {
        public PreparedAssetPresence ProbeCollision(
            PakAssetType type, uint sourceFileId) =>
            PreparedAssetPresence.Available;

        public PreparedCollisionReadResult<FlatSetupCollision> ReadSetupCollision(
            uint sourceFileId,
            CancellationToken cancellationToken = default) =>
            PreparedCollisionReadResult<FlatSetupCollision>.Loaded(
                new FlatSetupCollision(
                    System.Collections.Immutable
                        .ImmutableArray<FlatCollisionCylinder>.Empty,
                    [new FlatCollisionSphere(Vector3.Zero, 0.48f)],
                    height: 0f,
                    radius: 0f,
                    stepUpHeight: 0.4f,
                    stepDownHeight: 0.4f));

        public PreparedCollisionReadResult<FlatGfxObjCollisionAsset> ReadGfxObjCollision(
            uint sourceFileId,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public PreparedCollisionReadResult<FlatCellStructureCollisionAsset>
            ReadCellStructureCollision(
                uint sourceFileId,
                CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public PreparedCollisionReadResult<FlatEnvCellTopology> ReadEnvCellTopology(
            uint sourceFileId,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public PreparedCollisionSourceStats CollisionStats => default;

        public void Dispose() { }
    }
}
