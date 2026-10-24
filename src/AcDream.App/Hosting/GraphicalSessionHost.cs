using AcDream.App.Plugins;
using AcDream.App.Rendering;
using AcDream.App.Update;
using AcDream.Core.Plugins;
using AcDream.Runtime;
using AcDream.Runtime.Plugins;
using AcDream.Runtime.Session;

namespace AcDream.App.Hosting;

/// <summary>
/// What a session host runs each update: the frame of whatever presents the
/// session right now. The game window is one; it ticks its frame graph.
/// </summary>
internal interface IGraphicalSessionFrame
{
    void TickSessionFrame(UpdateFrameInput input);
}

/// <summary>
/// The graphical client's session, owned for the life of the process rather
/// than the life of a window. It is built before any window and holds what
/// the session is: the runtime and the operation slots it was built with,
/// the status file, the plugin session and what plugins see of the world,
/// the plugin tick, and the queue that brings plugin work onto the one
/// update thread. A window is built over it and borrows those owners.
///
/// The update tick is the host's: it drains the main-thread queue, runs the
/// current session frame, then raises the plugin tick, in that order.
/// </summary>
internal sealed class GraphicalSessionHost : IDisposable
{
    private readonly RuntimeOptions _options;
    private readonly RuntimePluginTickClock _pluginTick;
    private GraphicalPluginSession? _pluginSession;
    private GameWindow? _window;
    private bool _disposed;

    internal GraphicalSessionHost(
        RuntimeOptions options,
        WorldGameState worldGameState,
        WorldEvents worldEvents,
        RuntimeAutomationSurface? automation = null,
        string? chatLogDirectory = null)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        WorldGameState = worldGameState
            ?? throw new ArgumentNullException(nameof(worldGameState));
        WorldEvents = worldEvents
            ?? throw new ArgumentNullException(nameof(worldEvents));
        Automation = automation;
        StatusWriter = new SessionStatusWriter(options.StatusFilePath);
        Runtime = new GameRuntime(
            GraphicalAutomationCapabilities.BuildRuntimeDependencies(
                CombatAttackOperations,
                CombatTargetOperations,
                CombatModeOperations,
                SpellCastOperations,
                timeSyncDiagnostic:
                    options.DumpSky ? Console.WriteLine : null));
        RuntimeHostLease = Runtime.AcquireHostLease("graphical session host");
        automation?.Bind(Runtime, Runtime.CharacterOwner, Runtime.ActionOwner.SpellCast);
        // Settings the runtime reads off the character's own options, which
        // hold whether or not anything is drawn.
        Runtime.EnvironmentOwner.Weather.DisableDistanceFogSource = () =>
            Runtime.CharacterOwner.Options.GetOptionBit(
                AcDream.Core.Net.Messages.CharacterOptionId.DisableDistanceFog);
        Runtime.CommunicationOwner.DisplayTimestampsSource = () =>
            Runtime.CharacterOwner.Options.GetOptionBit(
                AcDream.Core.Net.Messages.CharacterOptionId.DisplayTimeStamps);
        Runtime.MovementOwner.RunAsDefaultMovementSource = () =>
            Runtime.CharacterOwner.Options.GetOptionBit(
                AcDream.Core.Net.Messages.CharacterOptionId.ToggleRun);
        _pluginTick = new RuntimePluginTickClock(
            WorldEvents.FireTick,
            () => Runtime.Generation.Value);
        // What a plugin sees in the world comes from the runtime's object
        // directory, not from what happens to be drawn: the same producer the
        // windowless client uses, so both answer the same population, ids
        // and event order.
        PluginWorldEntities = new RuntimeWorldEntityProjection(
            Runtime,
            // A plugin handler that throws is skipped, not hidden: a plugin
            // author with an overlay that stopped updating needs to be told
            // why.
            line => Serilog.Log.Warning("{Line}", line));
        WorldGameState.BindWorldEntities(PluginWorldEntities);
        WorldEvents.BindWorldEntities(PluginWorldEntities);
        PlayerIdentity = new AcDream.App.Input.LocalPlayerIdentityState(
            Runtime.PlayerIdentity);
        // The live session is the host's too: it connects, routes commands and
        // runs the login steps whether or not a window is open; a window lends
        // it what only a window can do while it is attached.
        LiveSession = new GraphicalLiveSession(
            Runtime,
            PlayerIdentity,
            options,
            StatusWriter,
            SessionId,
            automation is null ? null : automation.TryHandlePluginCommand,
            automation is null ? null : automation.InterceptChatInput,
            Console.WriteLine,
            chatLogDirectory);
    }

    internal RuntimeOptions Options => _options;

    /// <summary>The id the status file and plugins know this session by.</summary>
    internal string SessionId => _options.SessionId ?? "app";

    internal SessionStatusWriter StatusWriter { get; }

    internal GameRuntime Runtime { get; }

    internal IDisposable RuntimeHostLease { get; }

    internal AcDream.App.Combat.CombatAttackOperationsSlot CombatAttackOperations { get; } =
        new();

    internal AcDream.App.Combat.RuntimeCombatTargetOperationsSlot CombatTargetOperations { get; } =
        new();

    internal AcDream.App.Combat.RuntimeCombatModeOperationsSlot CombatModeOperations { get; } =
        new();

    internal AcDream.App.Spells.RuntimeSpellCastOperationsSlot SpellCastOperations { get; } =
        new();

    internal WorldGameState WorldGameState { get; }

    internal WorldEvents WorldEvents { get; }

    internal RuntimeAutomationSurface? Automation { get; }

    internal RuntimeWorldEntityProjection PluginWorldEntities { get; }

    /// <summary>Which character this session plays, as the client reads it.</summary>
    internal AcDream.App.Input.LocalPlayerIdentityState PlayerIdentity { get; }

    /// <summary>The live session: connection, command routing, login steps.</summary>
    internal GraphicalLiveSession LiveSession { get; }

    /// <summary>
    /// Marshals plugin work that must run on the update thread (clipboard,
    /// window state) onto it. Built on the thread that runs the host.
    /// </summary>
    internal MainThreadDispatchQueue MainThreadDispatch { get; } = new();

    /// <summary>The lines plugins draw in the world; kept across windows.</summary>
    internal PluginWorldLineStore WorldLines { get; } = new();

    internal GraphicalPluginSession? PluginSession => _pluginSession;

    /// <summary>The window presenting the session, if one is open.</summary>
    internal GameWindow? Window => _window;

    internal void StartPluginHosting(GraphicalPluginSession pluginSession)
    {
        ArgumentNullException.ThrowIfNull(pluginSession);
        if (_pluginSession is not null)
        {
            throw new InvalidOperationException(
                "The graphical plugin session is already attached.");
        }

        _pluginSession = pluginSession;
        pluginSession.Start();
    }

    /// <summary>
    /// One update of the session: plugin work queued for this thread, the
    /// frame of what presents the session, then the plugin tick.
    /// </summary>
    internal void Tick(double hostDeltaSeconds, IGraphicalSessionFrame frame)
    {
        ArgumentNullException.ThrowIfNull(frame);
        MainThreadDispatch.Drain();
        frame.TickSessionFrame(new UpdateFrameInput(hostDeltaSeconds));
        _pluginTick.Feed(hostDeltaSeconds);
    }

    /// <summary>
    /// Writes that the session left the world, when it is in it. Said once,
    /// as the session starts to stop.
    /// </summary>
    internal void ReportSessionStopping()
    {
        if (Runtime.Session.IsInWorld)
            StatusWriter.Disconnected(SessionId, "stopped");
    }

    /// <summary>
    /// Runs the session with a window open from the start until the window
    /// closes, which ends the session. The process's last status line is
    /// written from how the window's shutdown went.
    /// </summary>
    internal void Run(Func<GraphicalSessionHost, GameWindow> openWindow)
    {
        ArgumentNullException.ThrowIfNull(openWindow);
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_window is not null)
            throw new InvalidOperationException("A window is already open on this session.");

        GameWindow window = openWindow(this);
        _window = window;
        try
        {
            window.Run();
        }
        finally
        {
            window.Dispose();
            _window = null;
            ReportExited(window.LifetimeReport, window.RunFailed);
        }
    }

    internal void ReportExited(GameWindowLifetimeReport report, bool crashed)
    {
        ArgumentNullException.ThrowIfNull(report);
        if (crashed)
        {
            StatusWriter.Exited(SessionId, 1, "crashed");
            return;
        }

        if (report.Status == GameWindowLifetimeStatus.Complete)
            StatusWriter.Exited(SessionId, 0, "graceful");
        else
            StatusWriter.Exited(SessionId, 1, "shutdown-incomplete");
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        // After the terminal status line, release the status file so it can
        // be read, moved or deleted freely.
        StatusWriter.Dispose();
    }
}
