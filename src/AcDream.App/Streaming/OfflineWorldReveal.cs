using AcDream.App.Rendering;

namespace AcDream.App.Streaming;

/// <summary>
/// The offline view's reveal. Without a server there is no login and no
/// portal tunnel, but the world still arrives a landblock at a time: this
/// withholds the world from the screen until the area around the camera has
/// loaded, as a login does, and for that time gives streaming and uploads the
/// widened reveal budget (nothing is drawn, so the frame has the time). Once
/// the area is in, the world is shown and the budgets return to normal for
/// the rest of the run.
/// </summary>
internal sealed class OfflineWorldReveal : IWorldViewportWithholdSource
{
    // Offline there is no session and so no login or portal generation to
    // share the schedulers with; this is the one reveal the view ever has.
    internal const long RevealGeneration = 1;

    private readonly IWorldRevealStreamingScheduler _streaming;
    private readonly IWorldRevealRenderResourceScheduler _renderResources;
    private readonly Func<StreamingRevealWindow> _revealWindow;
    private readonly Func<uint, int, int, bool> _isRenderNeighborhoodReady;
    private uint _landblockId;
    private bool _complete;

    public OfflineWorldReveal(
        IWorldRevealStreamingScheduler streaming,
        IWorldRevealRenderResourceScheduler renderResources,
        Func<StreamingRevealWindow> revealWindow,
        Func<uint, int, int, bool> isRenderNeighborhoodReady)
    {
        _streaming = streaming
            ?? throw new ArgumentNullException(nameof(streaming));
        _renderResources = renderResources
            ?? throw new ArgumentNullException(nameof(renderResources));
        _revealWindow = revealWindow
            ?? throw new ArgumentNullException(nameof(revealWindow));
        _isRenderNeighborhoodReady = isRenderNeighborhoodReady
            ?? throw new ArgumentNullException(nameof(isRenderNeighborhoodReady));
    }

    /// <summary>The world is being withheld while its area loads.</summary>
    public bool IsWithholdingWorld => _landblockId != 0u && !_complete;

    /// <summary>The area loaded and the world has been shown.</summary>
    public bool IsComplete => _complete;

    /// <summary>
    /// Called once per streaming frame with the camera's landblock, before
    /// that frame's streaming work: the first call begins the reveal, a
    /// later one shows the world once the area around the camera is ready
    /// (from the work of the frames before), and a camera that moved to
    /// another landblock while withheld moves the reveal with it.
    /// </summary>
    public void Observe(int observerX, int observerY)
    {
        if (_complete
            || observerX < 0 || observerX > 254
            || observerY < 0 || observerY > 254)
        {
            return;
        }

        uint landblockId = ((uint)observerX << 24)
            | ((uint)observerY << 16)
            | 0xFFFFu;
        StreamingRevealWindow window = RequiredWindow();
        if (_landblockId == 0u)
        {
            _landblockId = landblockId;
            _streaming.BeginDestinationReservation(
                RevealGeneration,
                landblockId,
                window.FarRadius);
            _renderResources.BeginDestinationReveal(RevealGeneration);
        }
        else if (_landblockId != landblockId)
        {
            _landblockId = landblockId;
            _streaming.EndDestinationReservation(RevealGeneration);
            _streaming.BeginDestinationReservation(
                RevealGeneration,
                landblockId,
                window.FarRadius);
        }

        if (!_isRenderNeighborhoodReady(
                landblockId,
                window.NearRadius,
                window.FarRadius))
        {
            return;
        }

        _complete = true;
        _streaming.EndDestinationReservation(RevealGeneration);
        _renderResources.EndDestinationReveal(RevealGeneration);
    }

    private StreamingRevealWindow RequiredWindow()
    {
        StreamingRevealWindow window = _revealWindow();
        int far = Math.Max(0, window.FarRadius);
        int near = Math.Clamp(window.NearRadius, 0, far);
        return new StreamingRevealWindow(near, far);
    }
}
