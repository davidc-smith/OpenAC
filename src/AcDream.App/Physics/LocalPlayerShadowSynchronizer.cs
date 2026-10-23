using AcDream.App.Input;
using AcDream.App.World;
using AcDream.Core.Physics;
using AcDream.Core.World;

namespace AcDream.App.Physics;

internal sealed class LocalPlayerShadowSynchronizer
{
    private readonly PhysicsEngine _physics;
    private readonly LiveEntityRuntime _liveEntities;
    private readonly LiveWorldOriginState _origin;
    private readonly LocalPlayerShadowState _state;

    public LocalPlayerShadowSynchronizer(
        PhysicsEngine physics,
        LiveEntityRuntime liveEntities,
        LiveWorldOriginState origin,
        LocalPlayerShadowState state)
    {
        _physics = physics ?? throw new ArgumentNullException(nameof(physics));
        _liveEntities = liveEntities ?? throw new ArgumentNullException(nameof(liveEntities));
        _origin = origin ?? throw new ArgumentNullException(nameof(origin));
        _state = state ?? throw new ArgumentNullException(nameof(state));
    }

    public void Sync(WorldEntity playerEntity, uint cellId, bool force = false)
        => SyncPose(
            playerEntity,
            playerEntity.Position,
            playerEntity.Rotation,
            cellId,
            force);

    public LocalPlayerShadowState.Snapshot? Capture() => _state.Current;

    public void SyncPose(
        WorldEntity playerEntity,
        System.Numerics.Vector3 position,
        System.Numerics.Quaternion orientation,
        uint cellId,
        bool force = false)
    {
        ArgumentNullException.ThrowIfNull(playerEntity);
        // Hiding does not take the player out of collision: a hidden player
        // still stands in its cell and still blocks. The server hides the
        // player for the whole portal-space stay, and an object created at the
        // destination in that time has to be placed against the player, not
        // on top of it.
        if (cellId == 0
            || !IsCurrentVisibleProjection(playerEntity))
        {
            Suspend(playerEntity);
            return;
        }

        if (!force
            && _state.Current is { } last
            && last.CellId == cellId
            && System.Numerics.Vector3.DistanceSquared(
                last.Position,
                position) <= 1e-4f
            && MathF.Abs(System.Numerics.Quaternion.Dot(
                last.Orientation,
                orientation)) >= 0.99999f)
        {
            return;
        }

        ShadowPositionSynchronizer.Sync(
            _physics.ShadowObjects,
            playerEntity.Id,
            position,
            orientation,
            cellId,
            _origin.CenterX,
            _origin.CenterY);
        _state.Set(position, orientation, cellId);
    }

    public void Restore(
        WorldEntity playerEntity,
        LocalPlayerShadowState.Snapshot? snapshot)
    {
        ArgumentNullException.ThrowIfNull(playerEntity);
        if (snapshot is not { } prior || !IsCurrentVisibleProjection(playerEntity))
        {
            Suspend(playerEntity);
            return;
        }

        SyncPose(
            playerEntity,
            prior.Position,
            prior.Orientation,
            prior.CellId,
            force: true);
    }

    public bool IsCurrentVisibleProjection(WorldEntity playerEntity) =>
        _liveEntities.TryGetRecord(playerEntity.ServerGuid, out LiveEntityRecord record)
        && ReferenceEquals(record.WorldEntity, playerEntity)
        && _liveEntities.IsCurrentSpatialRootObject(record);

    public void Suspend(WorldEntity playerEntity)
    {
        ArgumentNullException.ThrowIfNull(playerEntity);
        _physics.ShadowObjects.Suspend(playerEntity.Id);
        _state.Clear();
    }

    public void ResetSession() => _state.Clear();
}
