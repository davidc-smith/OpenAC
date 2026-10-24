using AcDream.App.Input;
using AcDream.App.Net;
using AcDream.App.Plugins;
using AcDream.Core.Chat;
using AcDream.Core.Net;
using AcDream.Plugin.Abstractions;
using AcDream.Runtime;
using AcDream.Runtime.Chat;
using AcDream.Runtime.Entities;
using AcDream.Runtime.Session;

namespace AcDream.App.Hosting;

/// <summary>
/// The graphical client's live session, built by the session host before any
/// window and kept for the life of the process: the session host the runtime
/// connects through, the command surface plugins and chat send through, the
/// login commands, the chat transcript file, and how a world connection's
/// commands are routed.
///
/// What only a window can do -- take hold of a world connection's events with
/// what it draws, take what it draws down at a new generation, the client
/// commands that need something to look at, the login steps that restore its
/// layout -- is lent by the attached window and reached at the moment it is
/// needed. With no window, a reset puts the runtime alone back to its start,
/// a command that needs a window says so, and a world connection is taken
/// hold of by the route every client without a window builds.
/// </summary>
internal sealed class GraphicalLiveSession
{
    private readonly GameRuntime _runtime;
    private readonly Action<string> _log;
    private readonly string? _chatLogDirectory;
    private readonly AcDream.Runtime.Session.Windowless
        .RuntimeWindowlessGenerationResetHost _resetWithoutWindow = new();
    private GraphicalSessionPresentation? _presentation;
    private ChatSessionLog? _chatSessionLog;
    private ChatTranscriptLogWriter? _chatLogWriter;

    internal GraphicalLiveSession(
        GameRuntime runtime,
        LocalPlayerIdentityState identity,
        RuntimeOptions options,
        SessionStatusWriter statusWriter,
        string sessionId,
        Func<string, bool>? tryHandlePluginCommand,
        Func<string, PluginChatInputDecision>? interceptChatInput,
        Action<string> log,
        Func<WorldSession, ILiveSessionEventRouting> createEventsWithoutWindow,
        Action? beforeResetWithoutWindow = null,
        string? chatLogDirectory = null,
        TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(createEventsWithoutWindow);
        _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
        ArgumentNullException.ThrowIfNull(identity);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(statusWriter);
        ArgumentNullException.ThrowIfNull(sessionId);
        _log = log ?? throw new ArgumentNullException(nameof(log));
        if (options.LoginCommandDelayMs < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                "The login command delay cannot be negative.");
        }
        _chatLogDirectory = chatLogDirectory;

        Commands = new LiveSessionCommandSurface(
            tryHandlePluginCommand,
            interceptChatInput);
        LoginCommands = new LoginCommandSequence(
            options.LoginCommands is null ? [] : [.. options.LoginCommands],
            TimeSpan.FromMilliseconds(options.LoginCommandDelayMs),
            new RuntimeChatCommandFeedback(runtime.CommunicationOwner),
            Commands,
            failure => statusWriter.LoginCommandFailed(
                sessionId,
                failure.CommandIndex,
                failure.Command,
                failure.Error),
            timeProvider ?? TimeProvider.System);
        Host = new LiveSessionHost(
            runtime.Session,
            GraphicalAutomationCapabilities.BuildSessionHostBindings(
                new GraphicalSessionHostParts
                {
                    CreateCommands = session => Commands.Attach(
                        new LiveSessionCommandRouter(
                            CreateCommandBindings(session))),
                    ResetWithoutWindow = generation =>
                    {
                        // A portal reveal ends before the world transit
                        // resets, as it does with a window.
                        beforeResetWithoutWindow?.Invoke();
                        _runtime.ResetGeneration(generation, _resetWithoutWindow);
                    },
                    CreateEventsWithoutWindow = createEventsWithoutWindow,
                    Presentation = () => _presentation,
                    Identity = identity,
                    Communication = runtime.CommunicationOwner,
                    Combat = runtime.ActionOwner.Combat,
                    StatusWriter = statusWriter,
                    SessionId = sessionId,
                    LoginCommands = LoginCommands,
                    Warn = log,
                }),
            new LiveSessionConnectOptions(
                options.LiveMode,
                options.LiveHost,
                options.LivePort,
                options.LiveUser ?? string.Empty,
                options.LivePass ?? string.Empty,
                options.LiveCharacterSelector,
                AwaitCharacterSelection: options.LiveCharacterSelector is null)
            {
                PollConnectionDuringTicks = true,
            },
            runtime);
    }

    /// <summary>The session host the runtime's live session runs through.</summary>
    internal LiveSessionHost Host { get; }

    /// <summary>Where plugin and chat commands for the world connection go.</summary>
    internal LiveSessionCommandSurface Commands { get; }

    /// <summary>The commands a session document runs once in the world.</summary>
    internal LoginCommandSequence LoginCommands { get; }

    /// <summary>Whether a window is presenting the session.</summary>
    internal bool HasPresentation => _presentation is not null;

    /// <summary>
    /// Lends the session what a window does for it, until the returned lease
    /// is disposed. One window at a time.
    /// </summary>
    internal IDisposable AttachPresentation(GraphicalSessionPresentation presentation)
    {
        ArgumentNullException.ThrowIfNull(presentation);
        if (_presentation is not null)
        {
            throw new InvalidOperationException(
                "A window is already presenting this session.");
        }

        _presentation = presentation;
        return new PresentationLease(this, presentation);
    }

    private void Detach(GraphicalSessionPresentation presentation)
    {
        if (ReferenceEquals(_presentation, presentation))
            _presentation = null;
    }

    private LiveSessionCommandBindings CreateCommandBindings(WorldSession session) =>
        LiveSessionCommandBindingFactory.Create(
            _runtime,
            session,
            RuntimeClientCommandBindings.Build(
                _runtime,
                session,
                GraphicalAutomationCapabilities.ResolveClientCommandsPerCall(
                    () => _presentation?.ClientCommands,
                    () => _runtime.CommunicationOwner.Chat.OnSystemMessage(
                        RuntimeClientCommandBindings.NotAvailableWithoutAWindow,
                        0x00u)),
                SetChatLogFile),
            _log);

    private ChatLogResult SetChatLogFile(string name)
    {
        ChatSessionLog log = _chatSessionLog ??= new ChatSessionLog(
            _chatLogDirectory
            ?? AcDream.Platform.ApplicationPathSet.Resolve().LogsDirectory);
        ChatTranscriptLogWriter writer = _chatLogWriter ??= new ChatTranscriptLogWriter(log);
        string? closedName = log.CurrentName;

        writer.Detach();
        bool closed = log.Close();

        if (string.IsNullOrWhiteSpace(name))
            return new ChatLogResult(Opened: false, closed, string.Empty, closedName);

        bool opened = log.Open(name, out string resolved);
        if (opened)
            writer.Attach(_runtime.CommunicationOwner.Chat);

        return new ChatLogResult(opened, closed, resolved, closedName);
    }

    private sealed class PresentationLease(
        GraphicalLiveSession owner,
        GraphicalSessionPresentation presentation) : IDisposable
    {
        private GraphicalLiveSession? _owner = owner;

        public void Dispose()
        {
            _owner?.Detach(presentation);
            _owner = null;
        }
    }
}
