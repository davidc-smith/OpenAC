using System.Collections.Concurrent;
using System.Diagnostics;
using AcDream.App.Rendering;

namespace AcDream.App.Hosting;

/// <summary>What the session's window did, as the status file and console tell it.</summary>
internal sealed record GraphicalWindowEvents
{
    /// <summary>A window opened on the session.</summary>
    public Action Opened { get; init; } = static () => { };

    /// <summary>The window closed, and why.</summary>
    public Action<WindowDetachReason> Closed { get; init; } = static _ => { };

    /// <summary>A window could not be opened (or was asked for while one is open).</summary>
    public Action<string> Failed { get; init; } = static _ => { };
}

/// <summary>How the session ended.</summary>
internal enum GraphicalSessionEnd
{
    /// <summary>Logged off and shut down cleanly.</summary>
    Graceful,

    /// <summary>The shutdown did not complete.</summary>
    Incomplete,

    /// <summary>The connection went (or never came) with no window to show it.</summary>
    ConnectionLost,

    /// <summary>The window present from the start failed.</summary>
    Crashed,
}

/// <summary>
/// The one loop of the session's update thread, for the life of the process.
/// With no window it runs the session's own frame at a steady rate; while a
/// window is attached it runs the window's own loop, which ticks the session,
/// until the window closes. Requests (open or close the window, shut down)
/// may come from any thread and are carried out on this one, between the
/// session's steps: a window opens or closes only when
/// <see cref="GraphicalLiveSession.WhyAWindowCannotOpenNow"/> allows it, and
/// waits until it does.
///
/// The player closing the window logs the character off and ends the
/// process, as it always has; a close that was asked for leaves the session
/// playing; a window that fails while opening on a playing session leaves it
/// playing too.
/// </summary>
internal sealed class GraphicalSessionWindowLoop
{
    /// <summary>The session's update rate with no window: 15 ms.</summary>
    internal static TimeSpan FramePeriod { get; } = TimeSpan.FromMilliseconds(15);

    private readonly GraphicalSessionHost _host;
    private readonly Func<GraphicalSessionHost, GameWindow> _openWindow;
    private readonly GraphicalWindowEvents _events;
    private readonly WindowAttachMachine _machine = new();
    private readonly ConcurrentQueue<WindowAttachInput> _requests = new();
    private readonly SemaphoreSlim _wake = new(0);
    private GameWindow? _window;
    private bool _closeIssued;
    private WindowDetachReason _closeReason;
    private readonly bool _console;

    internal GraphicalSessionWindowLoop(
        GraphicalSessionHost host,
        Func<GraphicalSessionHost, GameWindow> openWindow,
        GraphicalWindowEvents? events = null,
        bool console = false)
    {
        _host = host ?? throw new ArgumentNullException(nameof(host));
        _openWindow = openWindow ?? throw new ArgumentNullException(nameof(openWindow));
        _events = events ?? new GraphicalWindowEvents();
        _console = console;
    }

    internal WindowAttachState State => _machine.State;

    /// <summary>Asks for a window on the session. Any thread.</summary>
    internal void RequestWindow() => Request(WindowAttachInput.AttachRequested);

    /// <summary>Asks for the window to close while the session plays on. Any thread.</summary>
    internal void RequestCloseWindow() => Request(WindowAttachInput.DetachRequested);

    /// <summary>Asks the session to log off and the loop to end. Any thread.</summary>
    internal void RequestShutdown() => Request(WindowAttachInput.ShutdownRequested);

    private void Request(WindowAttachInput input)
    {
        _requests.Enqueue(input);
        _wake.Release();
    }

    /// <summary>
    /// Runs until the session ends. With <paramref name="windowAtStart"/> a
    /// window opens at once and starts the session, as the client always
    /// has; without it the session starts with no window.
    /// </summary>
    internal GraphicalSessionEnd Run(bool windowAtStart)
    {
        _host.UseWindowLoop(this);
        try
        {
            if (windowAtStart)
                RequestWindow();
            else
            {
                _host.ApplyAutomationWithoutWindow();
                _host.StartSession();
            }

            var clock = Stopwatch.StartNew();
            TimeSpan last = clock.Elapsed;
            while (true)
            {
                if (TakeRequestWhileDetached() is { } input)
                {
                    foreach (WindowAttachAction action in _machine.Step(input))
                    {
                        switch (action.Kind)
                        {
                            case WindowAttachActionKind.OpenWindow:
                                if (RunWindow() is { } ended)
                                    return ended;
                                last = clock.Elapsed;
                                break;
                            case WindowAttachActionKind.ExitLoop:
                                return _host.EndSessionWithoutWindow();
                        }
                    }
                    continue;
                }

                TimeSpan now = clock.Elapsed;
                _host.Tick((now - last).TotalSeconds, _host.WindowlessFrame);
                if (_host.StepConnectionWithoutWindow())
                {
                    _machine.Step(WindowAttachInput.ShutdownRequested);
                    return _host.EndSessionWithoutWindow(connectionEnded: true);
                }
                last = now;
                TimeSpan wait = FramePeriod - (clock.Elapsed - now);
                if (wait > TimeSpan.Zero)
                    _wake.Wait(wait);
            }
        }
        finally
        {
            _host.UseWindowLoop(null);
        }
    }

    /// <summary>
    /// The next request to act on while no window is attached; an open
    /// that must wait for the session's current step stays queued, and so
    /// does one asked for while the session waits to log in again: the next
    /// connection takes it.
    /// </summary>
    private WindowAttachInput? TakeRequestWhileDetached()
    {
        if (!_requests.TryPeek(out WindowAttachInput input))
            return null;
        if (input is WindowAttachInput.AttachRequested
            && _host.HasSessionStarted
            && (_host.IsRecoveringLostConnection
                || _host.LiveSession.WhyAWindowCannotOpenNow() is not null))
        {
            return null;
        }
        _requests.TryDequeue(out _);
        return input;
    }

    /// <summary>
    /// Carries out requests while a window is attached, from the window's own
    /// update on this thread. A close waits for the session's current step;
    /// once a close is issued nothing more is taken until the window is gone.
    /// </summary>
    internal void DrainWhileAttached()
    {
        if (_window is not { } window)
            return;
        while (!_closeIssued && _requests.TryPeek(out WindowAttachInput input))
        {
            if (input is WindowAttachInput.DetachRequested
                && _host.LiveSession.WhyAWindowCannotOpenNow() is not null)
            {
                return;
            }
            _requests.TryDequeue(out _);
            foreach (WindowAttachAction action in _machine.Step(input))
            {
                switch (action.Kind)
                {
                    case WindowAttachActionKind.RejectAttach:
                        _events.Failed("a window is already open on this session");
                        break;
                    case WindowAttachActionKind.CloseWindow:
                        _closeIssued = true;
                        _closeReason = action.Reason;
                        if (action.Reason is WindowDetachReason.Requested)
                            window.CloseKeepingSession();
                        else
                            window.CloseEndingSession();
                        break;
                }
            }
        }
    }


    /// <summary>
    /// Whether a window's end logs the character off and ends the client.
    /// The player closing the window always does, with the console on or
    /// off. Without the console the window is the session's only interface,
    /// so a window that failed to draw or never opened does too; with it, a
    /// window the console closed or could not open leaves the session playing.
    /// </summary>
    internal static bool LogsOff(
        WindowDetachReason? closed,
        bool attachFailed,
        bool console) =>
        closed is WindowDetachReason.ClosedByUser
        || (!console
            && (attachFailed || closed is WindowDetachReason.RenderFailed));
    /// <summary>Called by the window once it is built and presenting the session.</summary>
    internal void NoteWindowOpened() => _events.Opened();

    /// <summary>
    /// Opens a window and runs it until it closes. Null when the session
    /// plays on without it; otherwise how the session ended.
    /// </summary>
    private GraphicalSessionEnd? RunWindow()
    {
        _closeIssued = false;
        GameWindow window;
        try
        {
            window = _openWindow(_host);
        }
        catch (Exception error) when (_host.HasSessionStarted)
        {
            _machine.Step(WindowAttachInput.AttachFailed);
            _events.Failed(error.Message);
            if (!LogsOff(closed: null, attachFailed: true, _console))
                return null;
            _machine.Step(WindowAttachInput.ShutdownRequested);
            return _host.EndSessionWithoutWindow();
        }

        _window = window;
        _host.UseWindow(window);
        Exception? failure = null;
        try
        {
            window.Run();
        }
        catch (Exception error)
        {
            failure = error;
        }
        finally
        {
            _window = null;
            _host.UseWindow(null);
            window.Dispose();
        }

        if (failure is not null && !window.JoinedPlayingSession)
        {
            // The window present from the start failed: the client ends, as
            // it always has.
            _host.ReportExited(window.LifetimeReport, crashed: true);
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
        }

        if (failure is not null)
        {
            // The window failed while opening on (or drawing) a session that
            // plays on without it.
            _machine.Step(WindowAttachInput.AttachFailed);
            _events.Failed(failure.Message);
            GraphicalSessionHost.ReportWindowLifetime(window.LifetimeReport);
            if (!LogsOff(closed: null, attachFailed: true, _console))
                return null;
            _machine.Step(WindowAttachInput.ShutdownRequested);
            return _host.EndSessionWithoutWindow();
        }

        if (window.RenderFailure is not null)
        {
            // Drawing failed for good: the window closed and handed the
            // session back. With a console the session plays on; without one
            // the window was its only interface, and the character logs off.
            _machine.Step(WindowAttachInput.RenderFailed);
            _events.Closed(WindowDetachReason.RenderFailed);
            if (!LogsOff(WindowDetachReason.RenderFailed, attachFailed: false, _console))
                return null;
            _machine.Step(WindowAttachInput.ShutdownRequested);
            return _host.EndSessionWithoutWindow();
        }

        if (window.ClosedKeepingSession)
        {
            _events.Closed(WindowDetachReason.Requested);
            return null;
        }

        // The session ended with the window: the player closed it, or a
        // shutdown was asked for.
        if (!_closeIssued)
        {
            _machine.Step(WindowAttachInput.WindowClosedByUser);
            _closeReason = WindowDetachReason.ClosedByUser;
        }
        _events.Closed(_closeReason);
        _machine.Step(WindowAttachInput.ShutdownRequested);
        return _host.ReportExited(window.LifetimeReport, window.RunFailed);
    }
}
