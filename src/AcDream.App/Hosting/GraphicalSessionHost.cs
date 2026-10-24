using AcDream.App.Plugins;
using AcDream.App.Rendering;
using AcDream.App.Update;
using AcDream.Core.Plugins;
using AcDream.Runtime;
using AcDream.Runtime.Plugins;
using AcDream.Runtime.Session;
using AcDream.Runtime.Session.Windowless;

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
    private GraphicalSessionContent? _content;
    private IDisposable? _collisionContentLease;
    private RuntimeWindowlessSessionRoute? _windowlessRoute;
    private GraphicalWindowlessSessionFrame? _windowlessFrame;
    private GraphicalSessionDrives? _drives;
    private bool _sessionStarted;
    private bool _startFailed;
    private DirectGameRuntimeCommandAdapter? _commandsWithoutWindow;
    private GraphicalSessionWindowLoop? _windowLoop;
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
        // Collision is the session's, built here with no window: the same
        // neighborhood around the character the windowless client keeps.
        Collision = new GraphicalSessionCollision(Runtime);
        // What the character tells the server about its own movement, sent
        // with or without a window.
        MovementTruth = new AcDream.App.Input.MovementTruthDiagnosticController(
            options.DumpMoveTruth,
            Runtime.MovementOwner,
            PlayerIdentity);
        LocalPlayerOutbound = new AcDream.Runtime.Gameplay.LocalPlayerOutboundController(
            MovementTruth);
        // The sky and the world clock: set up once from the game files and
        // kept for the life of the process; a window draws them.
        WorldEnvironment = new AcDream.App.World.WorldEnvironmentController(
            Runtime.EnvironmentOwner,
            options.ForcedDayGroupIndex,
            Console.WriteLine,
            options.PinnedWorldDayFraction);
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
            CreateEventsWithoutWindow,
            () => _windowlessRoute?.EndPortalRevealForSessionReset(),
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
    /// What the character tells the server about its own movement, and the
    /// diagnostic that watches it.
    /// </summary>
    internal AcDream.App.Input.MovementTruthDiagnosticController MovementTruth { get; }

    internal AcDream.Runtime.Gameplay.LocalPlayerOutboundController LocalPlayerOutbound { get; }

    /// <summary>The sky and the world clock, set up once from the game files.</summary>
    internal AcDream.App.World.WorldEnvironmentController WorldEnvironment { get; }

    /// <summary>
    /// The drives that carry the character and every other creature into the
    /// world, once the session has game files. Whichever route holds the
    /// world connection drives these.
    /// </summary>
    internal GraphicalSessionDrives? Drives => _drives;

    /// <summary>
    /// The session's drives, built over <paramref name="collision"/> the first
    /// time the session has game files to build them from.
    /// </summary>
    internal GraphicalSessionDrives EnsureDrives(
        AcDream.Content.IPreparedCollisionSource collision)
    {
        ArgumentNullException.ThrowIfNull(collision);
        return _drives ??= new GraphicalSessionDrives(
            Runtime,
            collision,
            LocalPlayerOutbound,
            () => LiveSession.Host.CurrentSession,
            landblock => Collision.IsCollisionPublished(landblock));
    }

    /// <summary>
    /// The session's collision around the character, kept whether or not a
    /// window is open.
    /// </summary>
    internal GraphicalSessionCollision Collision { get; }

    /// <summary>
    /// The landblock whose corner a window draws the world from. It is the
    /// session's, kept across windows, and agrees with the frame the runtime
    /// and the collision measure in.
    /// </summary>
    internal AcDream.App.World.LiveWorldOriginState WorldOrigin { get; } = new();

    /// <summary>
    /// Marshals plugin work that must run on the update thread (clipboard,
    /// window state) onto it. Built on the thread that runs the host.
    /// </summary>
    internal MainThreadDispatchQueue MainThreadDispatch { get; } = new();

    /// <summary>The lines plugins draw in the world; kept across windows.</summary>
    internal PluginWorldLineStore WorldLines { get; } = new();

    internal GraphicalPluginSession? PluginSession => _pluginSession;

    /// <summary>
    /// The game files the session is played from, once opened. A window
    /// borrows them.
    /// </summary>
    internal GraphicalSessionContent? Content => _content;

    /// <summary>
    /// Takes the game files the session is played from, for the life of the
    /// process: installs what the session reads from them (spell tables,
    /// character creation choices, trade notes) and builds the session's
    /// collision from them, with or without a window.
    /// </summary>
    internal void UseContent(GraphicalSessionContent content)
    {
        ArgumentNullException.ThrowIfNull(content);
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_content is not null)
            throw new InvalidOperationException("The session's game files are already open.");

        _content = content;
        Runtime.CharacterOwner.InstallSpellMetadata(content.Magic.SpellTable);
        Console.WriteLine(
            $"spells: loaded {content.Magic.SpellTable.Count} entries from portal.dat");
        AcDream.Core.CharGen.ChargenOptions chargen;
        lock (content.DatLock)
        {
            chargen = AcDream.Content.CharGen.ChargenTableReader.Load(content.Dats);
            Runtime.HousePurchaseOwner.InstallTradeNoteDenominations(
                AcDream.Content.TradeNoteDenominations.Load(content.Dats));
        }
        Runtime.Session.CharacterCreationState.InstallOptions(chargen);
        Console.WriteLine(
            $"chargen: loaded {chargen.HeritagesById.Count} heritage(s) from portal.dat");
        _collisionContentLease = Collision.BindContent(content.CreateCollisionContent());
        DatReaderWriter.DBObjs.Region region;
        lock (content.DatLock)
        {
            region = content.Dats.Get<DatReaderWriter.DBObjs.Region>(0x13000000u)
                ?? throw new InvalidDataException("Region dat id 0x13000000 missing");
            WorldEnvironment.Initialize(region, content.Dats);
        }
        EnsureDrives(content.PreparedCollision);
    }

    /// <summary>
    /// The route that takes hold of a world connection while no window is
    /// attached: the one every client without a window builds, over this
    /// session's own collision.
    /// </summary>
    internal RuntimeWindowlessSessionRoute WindowlessRoute =>
        _windowlessRoute ??= new RuntimeWindowlessSessionRoute(
            Runtime,
            _content?.CreateCollisionContent(),
            "windowless graphical",
            Console.WriteLine,
            collision: () => Collision.Neighborhood,
            drives: _drives?.Windowless);

    /// <summary>The session's frame while no window presents it.</summary>
    internal IGraphicalSessionFrame WindowlessFrame =>
        _windowlessFrame ??= new GraphicalWindowlessSessionFrame(
            Runtime,
            LiveSession.Host,
            WindowlessRoute);

    private ILiveSessionEventRouting CreateEventsWithoutWindow(
        AcDream.Core.Net.WorldSession session)
    {
        AcDream.Content.Skills.LiveSkillCreditResolver skills;
        if (_content is { } content)
        {
            lock (content.DatLock)
            {
                skills = new AcDream.Content.Skills.LiveSkillCreditResolver(
                    content.Dats.Get<DatReaderWriter.DBObjs.SkillTable>(0x0E000004u));
            }
        }
        else
        {
            skills = new AcDream.Content.Skills.LiveSkillCreditResolver(null);
        }

        return WindowlessRoute.Create(
            session,
            GraphicalAutomationCapabilities.BuildWindowlessCharacterSessionBindings(
                Runtime.CharacterOwner,
                Runtime.ActionOwner.Combat,
                Runtime.MovementStats,
                skills.Resolve,
                () => Runtime.EffectClock.NowSeconds,
                Automation is { } automation
                    ? automation.RaiseConfirmationRequested
                    : null,
                Console.WriteLine));
    }


    /// <summary>
    /// Starts the session with no window: connects and plays as the session
    /// document says. A window opened later joins it.
    /// </summary>
    internal void StartSession()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_sessionStarted)
            throw new InvalidOperationException("The session has already started.");
        _sessionStarted = true;
        RuntimeSessionStartResult result =
            CommandsWithoutWindow.Session.Start(Runtime.Generation);
        _startFailed = result.Status is RuntimeSessionStartStatus.Failed
            or RuntimeSessionStartStatus.MissingCredentials
            or RuntimeSessionStartStatus.NoCharacters;
        AcDream.App.Composition.SessionStartCompositionPhase.Report(
            result,
            Console.WriteLine);
    }

    /// <summary>
    /// Whether the session's connection is gone with no window to show it: a
    /// start that could not connect, or a server that went silent. The
    /// session then ends with the connection's exit code.
    /// </summary>
    internal bool ConnectionEnded =>
        _startFailed
        || LiveSession.Host.ConnectionLost
        || LiveSession.Host.Connection.Snapshot.Status is RuntimeConnectionStatus.Failed;

    /// <summary>Whether the session has started, with or without a window.</summary>
    internal bool HasSessionStarted => _sessionStarted;

    /// <summary>
    /// Called by a window once it is built. A window opened at the start of
    /// the process starts the session itself, as it always has (false). A
    /// window opened on a session already started joins it (true): it takes
    /// hold of the connection the session plays on, the route that held it
    /// lets go, and the window is given what it missed.
    /// </summary>
    internal bool JoinPlayingSession()
    {
        if (!_sessionStarted)
        {
            _sessionStarted = true;
            return false;
        }

        if (LiveSession.TakeOverPlayingConnection(_drives))
            _windowlessRoute?.ReleaseConnection();
        return true;
    }

    /// <summary>
    /// The session's commands with no window: the command adapter the
    /// client without a window uses, over this session's live session.
    /// </summary>
    internal DirectGameRuntimeCommandAdapter CommandsWithoutWindow =>
        _commandsWithoutWindow ??= new DirectGameRuntimeCommandAdapter(
            Runtime,
            LiveSession.Host);

    /// <summary>
    /// Sends a chat line as the chat box would, with no window: the console's
    /// lines and a plugin's submitted text take this route.
    /// </summary>
    internal bool SubmitChatText(string text)
    {
        if (string.IsNullOrWhiteSpace(text) || LiveSession.Host.CurrentSession is null)
            return false;
        AcDream.Runtime.Chat.SubmitOutcome outcome = AcDream.Runtime.Chat.ChatCommandRouter.Submit(
            text,
            new AcDream.Runtime.Chat.RuntimeChatCommandFeedback(Runtime.CommunicationOwner),
            LiveSession.Commands,
            AcDream.Runtime.Chat.ChatChannelKind.Say);
        return outcome is not (AcDream.Runtime.Chat.SubmitOutcome.Empty
            or AcDream.Runtime.Chat.SubmitOutcome.UnknownCommand
            or AcDream.Runtime.Chat.SubmitOutcome.Dropped);
    }

    /// <summary>
    /// Lends the plugin surface what the session can do with no window: at a
    /// start without one, and again when a window closes while the session
    /// plays on, so nothing a plugin calls reaches the window that went.
    /// A window lends its own when it attaches.
    /// </summary>
    internal void ApplyAutomationWithoutWindow()
    {
        if (Automation is not { } automation)
            return;
        AcDream.Runtime.Plugins.RuntimeAutomationBindings.Apply(
            automation,
            Runtime,
            GraphicalAutomationCapabilities.Build(
                new GraphicalAutomationParts
                {
                    Runtime = Runtime,
                    Warn = Console.Error.WriteLine,
                    Content = _content?.Dats,
                    DatLock = _content?.DatLock,
                    MagicCatalog = _content?.Magic,
                    Events = WorldEvents,
                    SessionCommandsWithoutWindow = CommandsWithoutWindow,
                    SubmitChatTextWithoutWindow = SubmitChatText,
                }));
    }

    /// <summary>
    /// Called by a window that closes while the session plays on, before it
    /// lets go of anything: the connection goes back to the route of a
    /// session with no window, and plugins are given what the session can do
    /// without one.
    /// </summary>
    internal void LetGoOfWindow()
    {
        LiveSession.HandConnectionBackFromWindow(_drives);
        ApplyAutomationWithoutWindow();
    }
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
    /// collision around the character, the frame of what presents the
    /// session, then the plugin tick.
    /// </summary>
    internal void Tick(double hostDeltaSeconds, IGraphicalSessionFrame frame)
    {
        ArgumentNullException.ThrowIfNull(frame);
        MainThreadDispatch.Drain();
        if (_window is not null)
            _windowLoop?.DrainWhileAttached();
        Collision.Tick();
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
    /// Runs the session until it ends: with a window from the start (the
    /// client as it always was), or with none until one is asked for. The
    /// process's last status line is written from how the session ended.
    /// </summary>
    internal GraphicalSessionEnd Run(
        Func<GraphicalSessionHost, GameWindow> openWindow,
        bool windowAtStart = true,
        GraphicalWindowEvents? events = null,
        bool console = false)
    {
        ArgumentNullException.ThrowIfNull(openWindow);
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_windowLoop is not null)
            throw new InvalidOperationException("The session is already running.");
        return new GraphicalSessionWindowLoop(this, openWindow, events, console)
            .Run(windowAtStart);
    }

    /// <summary>The loop running the session, while it runs.</summary>
    internal GraphicalSessionWindowLoop? WindowLoop => _windowLoop;

    internal void UseWindowLoop(GraphicalSessionWindowLoop? loop) => _windowLoop = loop;

    internal void UseWindow(GameWindow? window) => _window = window;

    /// <summary>Called by a window once it is built and presents the session.</summary>
    internal void NoteWindowOpened() => _windowLoop?.NoteWindowOpened();

    /// <summary>
    /// Logs the character off and shuts the session down with no window, in
    /// the order a window's shutdown does it: the session stops, then the
    /// plugins, what plugins see of the world, and the runtime.
    /// </summary>
    internal GraphicalSessionEnd EndSessionWithoutWindow(bool connectionEnded = false)
    {
        if (connectionEnded)
            StatusWriter.Disconnected(SessionId, "connection-lost");
        else
            ReportSessionStopping();
        var failures = new List<Exception>();
        void Step(string name, Action step)
        {
            try
            {
                step();
            }
            catch (Exception error)
            {
                Console.Error.WriteLine($"[shutdown] '{name}': {error}");
                failures.Add(error);
            }
        }

        Step("game runtime session", Runtime.StopSession);
        Step("plugins", () => _pluginSession?.Dispose());
        Step("plugin world objects", PluginWorldEntities.Dispose);
        Step("graphical runtime host lease", RuntimeHostLease.Dispose);
        Step("game runtime", Runtime.Dispose);
        GraphicalSessionEnd end = connectionEnded
            ? GraphicalSessionEnd.ConnectionLost
            : failures.Count == 0
                ? GraphicalSessionEnd.Graceful
                : GraphicalSessionEnd.Incomplete;
        (int code, string reason) = end switch
        {
            GraphicalSessionEnd.Graceful => (0, "graceful"),
            GraphicalSessionEnd.ConnectionLost => (5, "connection"),
            _ => (1, "shutdown-incomplete"),
        };
        StatusWriter.Exited(SessionId, code, reason);
        return end;
    }

    /// <summary>Says what went wrong letting go of a window the session outlived.</summary>
    internal static void ReportWindowLifetime(GameWindowLifetimeReport report)
    {
        ArgumentNullException.ThrowIfNull(report);
        if (report.Status == GameWindowLifetimeStatus.Complete)
            return;
        Console.Error.WriteLine(
            $"[window] closing did not complete: status={report.Status}, "
            + $"blocked={report.BlockedStage ?? "none"}");
    }

    internal GraphicalSessionEnd ReportExited(GameWindowLifetimeReport report, bool crashed)
    {
        ArgumentNullException.ThrowIfNull(report);
        if (crashed)
        {
            StatusWriter.Exited(SessionId, 1, "crashed");
            return GraphicalSessionEnd.Crashed;
        }

        if (report.Status == GameWindowLifetimeStatus.Complete)
        {
            StatusWriter.Exited(SessionId, 0, "graceful");
            return GraphicalSessionEnd.Graceful;
        }
        StatusWriter.Exited(SessionId, 1, "shutdown-incomplete");
        return GraphicalSessionEnd.Incomplete;
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        // After the terminal status line, release the status file so it can
        // be read, moved or deleted freely.
        StatusWriter.Dispose();
        _windowlessRoute?.Dispose();
        // No landblock may still be read in the background once the game
        // files are closed.
        Collision.Neighborhood.StopBackgroundWork();
        _collisionContentLease?.Dispose();
        // The game files outlive every window that borrowed them.
        _content?.Dispose();
    }
}
