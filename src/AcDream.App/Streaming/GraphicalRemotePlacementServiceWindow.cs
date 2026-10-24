using AcDream.Runtime.Session;

namespace AcDream.App.Streaming;

/// <summary>
/// Where another creature's body may be put down by an accepted
/// re-placement: on collision the session has published, in a landblock
/// this window draws with its objects.
/// </summary>
internal sealed class GraphicalRemotePlacementServiceWindow
    : IRuntimeRemotePlacementServiceWindow
{
    private readonly GpuWorldState _state;
    private readonly Func<uint, bool> _isPresentationReady;
    private readonly Func<uint, bool> _isCollisionPublished;

    internal GraphicalRemotePlacementServiceWindow(
        GpuWorldState state,
        Func<uint, bool> isPresentationReady,
        Func<uint, bool> isCollisionPublished)
    {
        _state = state ?? throw new ArgumentNullException(nameof(state));
        _isPresentationReady = isPresentationReady
            ?? throw new ArgumentNullException(nameof(isPresentationReady));
        _isCollisionPublished = isCollisionPublished
            ?? throw new ArgumentNullException(nameof(isCollisionPublished));
    }

    public bool IsWithinServiceWindow(uint landblockId)
    {
        uint canonical = (landblockId & 0xFFFF0000u) | 0xFFFFu;
        return _isCollisionPublished(landblockId)
            && _isPresentationReady(canonical)
            && _state.IsNearTier(canonical);
    }
}
