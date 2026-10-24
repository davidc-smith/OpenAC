namespace AcDream.App.Hosting;

/// <summary>Whether a window is attached to the session.</summary>
internal enum WindowAttachState
{
    /// <summary>No window. The session plays on and the host waits for commands.</summary>
    Detached,

    /// <summary>A window is open and presenting the session.</summary>
    Attached,

    /// <summary>Shutdown was asked for; every later input is ignored.</summary>
    Exited,
}

/// <summary>Something that happened, fed to <see cref="WindowAttachMachine.Step"/>.</summary>
internal enum WindowAttachInput
{
    /// <summary>A window was asked for.</summary>
    AttachRequested,

    /// <summary>Closing the window was asked for; the session plays on.</summary>
    DetachRequested,

    /// <summary>The player closed the window (title bar, Alt+F4).</summary>
    WindowClosedByUser,

    /// <summary>Opening the window or its GPU device failed; nothing is open.</summary>
    AttachFailed,

    /// <summary>Presenting a frame failed in a way the window cannot recover from.</summary>
    RenderFailed,

    /// <summary>The host was asked to stop.</summary>
    ShutdownRequested,
}

/// <summary>Why a window closed.</summary>
internal enum WindowDetachReason
{
    /// <summary>Closing it was asked for.</summary>
    Requested,

    /// <summary>The player closed it.</summary>
    ClosedByUser,

    /// <summary>The host is shutting down.</summary>
    Shutdown,

    /// <summary>Drawing failed and the window was let go.</summary>
    RenderFailed,
}

/// <summary>What the host does next, in order.</summary>
internal readonly record struct WindowAttachAction(
    WindowAttachActionKind Kind,
    WindowDetachReason Reason = WindowDetachReason.Requested)
{
    internal static WindowAttachAction OpenWindow { get; } = new(WindowAttachActionKind.OpenWindow);

    internal static WindowAttachAction RejectAttach { get; } = new(WindowAttachActionKind.RejectAttach);

    internal static WindowAttachAction ExitLoop { get; } = new(WindowAttachActionKind.ExitLoop);

    internal static WindowAttachAction CloseWindow(WindowDetachReason reason) =>
        new(WindowAttachActionKind.CloseWindow, reason);
}

internal enum WindowAttachActionKind
{
    /// <summary>Build the window and its GPU device.</summary>
    OpenWindow,

    /// <summary>Let go of the window and everything built for it.</summary>
    CloseWindow,

    /// <summary>A window was asked for while one is open; say it was ignored.</summary>
    RejectAttach,

    /// <summary>Stop the host's loop.</summary>
    ExitLoop,
}

/// <summary>
/// Decides what the session host does about its window from what happened:
/// a command from another thread, the player closing the window, a failure to
/// draw. It touches no window and no GPU, so it is tested on its own.
/// Starts with no window.
/// </summary>
internal sealed class WindowAttachMachine
{
    internal WindowAttachState State { get; private set; } = WindowAttachState.Detached;

    /// <summary>Applies one input and returns the actions to carry out, in order.</summary>
    internal IReadOnlyList<WindowAttachAction> Step(WindowAttachInput input)
    {
        (WindowAttachState next, WindowAttachAction[] actions) = (State, input) switch
        {
            (WindowAttachState.Exited, _) => (WindowAttachState.Exited, Array.Empty<WindowAttachAction>()),

            (WindowAttachState.Detached, WindowAttachInput.AttachRequested) =>
                (WindowAttachState.Attached, [WindowAttachAction.OpenWindow]),
            // Nothing is open, so there is nothing to close; a late close from
            // a window that is already gone lands here too.
            (WindowAttachState.Detached, WindowAttachInput.ShutdownRequested) =>
                (WindowAttachState.Exited, [WindowAttachAction.ExitLoop]),
            (WindowAttachState.Detached, _) => (WindowAttachState.Detached, Array.Empty<WindowAttachAction>()),

            (WindowAttachState.Attached, WindowAttachInput.AttachRequested) =>
                (WindowAttachState.Attached, [WindowAttachAction.RejectAttach]),
            (WindowAttachState.Attached, WindowAttachInput.DetachRequested) =>
                (WindowAttachState.Detached,
                    [WindowAttachAction.CloseWindow(WindowDetachReason.Requested)]),
            (WindowAttachState.Attached, WindowAttachInput.WindowClosedByUser) =>
                (WindowAttachState.Detached,
                    [WindowAttachAction.CloseWindow(WindowDetachReason.ClosedByUser)]),
            // The open failed, so no window exists to close.
            (WindowAttachState.Attached, WindowAttachInput.AttachFailed) =>
                (WindowAttachState.Detached, Array.Empty<WindowAttachAction>()),
            (WindowAttachState.Attached, WindowAttachInput.RenderFailed) =>
                (WindowAttachState.Detached,
                    [WindowAttachAction.CloseWindow(WindowDetachReason.RenderFailed)]),
            (WindowAttachState.Attached, WindowAttachInput.ShutdownRequested) =>
                (WindowAttachState.Exited,
                    [
                        WindowAttachAction.CloseWindow(WindowDetachReason.Shutdown),
                        WindowAttachAction.ExitLoop,
                    ]),
            _ => throw new ArgumentOutOfRangeException(nameof(input), input, null),
        };
        State = next;
        return actions;
    }
}
