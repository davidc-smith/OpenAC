using AcDream.Core.Physics;
using AcDream.Runtime;
using AcDream.Runtime.Navigation;

namespace AcDream.App.Hosting;

/// <summary>
/// What a window answers for the session's walks while it is attached: how a
/// door on the way is opened and checked (as a click on it would), and where
/// what a walk has to say is shown.
/// </summary>
internal sealed record GraphicalSessionNavigationPresentation(
    Action<uint> UseDoor,
    Func<uint, bool> AppraiseDoor,
    Action<string> Report);

/// <summary>
/// The session's walks, one for the life of the session: what a plugin or
/// <c>/nav</c> asks for is planned over the session's own collision and
/// walked by the character whether or not a window is open, and a walk under
/// way when a window opens or closes carries on.
///
/// A window attached at the moment opens a door the way a click on it would
/// and shows what the walk says; with none, a door is used and checked
/// through the session's own commands, as on every client without a window.
/// Whatever presents the session steps the walk once per update: the
/// window's frame while one is attached, the session's own frame otherwise.
/// </summary>
internal sealed class GraphicalSessionNavigation
{
    private readonly Func<uint, bool> _useWithoutWindow;
    private readonly Func<uint, bool> _appraiseWithoutWindow;
    private readonly Action<string> _reportWithoutWindow;
    private GraphicalSessionNavigationPresentation? _presentation;

    internal GraphicalSessionNavigation(
        GameRuntime runtime,
        Func<uint, bool> useWithoutWindow,
        Func<uint, bool> appraiseWithoutWindow,
        Func<uint, bool> isSealedDungeon,
        Action<string> reportWithoutWindow)
    {
        ArgumentNullException.ThrowIfNull(runtime);
        ArgumentNullException.ThrowIfNull(isSealedDungeon);
        _useWithoutWindow = useWithoutWindow
            ?? throw new ArgumentNullException(nameof(useWithoutWindow));
        _appraiseWithoutWindow = appraiseWithoutWindow
            ?? throw new ArgumentNullException(nameof(appraiseWithoutWindow));
        _reportWithoutWindow = reportWithoutWindow
            ?? throw new ArgumentNullException(nameof(reportWithoutWindow));
        PhysicsEngine physics = runtime.EntityObjects.Physics.Engine;
        Walk = new NavigationWalkController(
            physics,
            new RuntimeNavigationWalkBody(runtime.MovementOwner, runtime.Portal),
            new RuntimeNavigationGoalSource(physics, runtime, runtime.MovementOwner),
            Report,
            new RuntimeNavigationDoors(physics, runtime, UseDoor, AppraiseDoor),
            isSealedDungeon);
    }

    /// <summary>The session's walks.</summary>
    internal NavigationWalkController Walk { get; }

    /// <summary>Whether a window answers for the walks right now.</summary>
    internal bool HasPresentation => _presentation is not null;

    /// <summary>
    /// Lends the walks what a window does for them, until the returned lease
    /// is disposed. One window at a time.
    /// </summary>
    internal IDisposable Present(GraphicalSessionNavigationPresentation presentation)
    {
        ArgumentNullException.ThrowIfNull(presentation);
        if (_presentation is not null)
        {
            throw new InvalidOperationException(
                "A window already answers for the session's walks.");
        }

        _presentation = presentation;
        return new Lease(this, presentation);
    }

    /// <summary>
    /// Stops asking the attached window anything, at once: it is going away
    /// while the session plays on. Its lease, given back later, is a no-op.
    /// </summary>
    internal void ForgetPresentation()
    {
        _presentation = null;
        // Nothing draws a grid any more.
        Walk.ShowGrid = false;
    }

    private void UseDoor(uint objectId)
    {
        if (_presentation is { } window)
            window.UseDoor(objectId);
        else
            _ = _useWithoutWindow(objectId);
    }

    private bool AppraiseDoor(uint objectId) =>
        _presentation is { } window
            ? window.AppraiseDoor(objectId)
            : _appraiseWithoutWindow(objectId);

    private void Report(string message)
    {
        if (_presentation is { } window)
            window.Report(message);
        else
            _reportWithoutWindow(message);
    }

    private void Release(GraphicalSessionNavigationPresentation presentation)
    {
        if (ReferenceEquals(_presentation, presentation))
            ForgetPresentation();
    }

    private sealed class Lease(
        GraphicalSessionNavigation owner,
        GraphicalSessionNavigationPresentation presentation) : IDisposable
    {
        private GraphicalSessionNavigation? _owner = owner;

        public void Dispose()
        {
            _owner?.Release(presentation);
            _owner = null;
        }
    }
}
