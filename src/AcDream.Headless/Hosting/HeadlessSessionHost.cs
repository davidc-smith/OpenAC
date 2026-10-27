using AcDream.Headless.Configuration;
using AcDream.Headless.Credentials;
using AcDream.Headless.Diagnostics;
using AcDream.Headless.Plugins;
using AcDream.Headless.Policies;
using AcDream.Plugin.Abstractions;
using AcDream.Content.CharGen;
using AcDream.Content.Skills;
using AcDream.Content.Vfx;
using AcDream.Core.Chat;
using AcDream.Core.Net.Messages;
using AcDream.Core.Physics;
using AcDream.Runtime;
using AcDream.Runtime.Chat;
using AcDream.Runtime.Entities;
using AcDream.Runtime.Gameplay;
using AcDream.Runtime.Navigation;
using AcDream.Runtime.Physics;
using AcDream.Runtime.Session;
using AcDream.Runtime.Session.Windowless;

namespace AcDream.Headless.Hosting;

internal sealed class HeadlessSessionHost : IDisposable
{
    private sealed class SessionCommandRoute : ILiveSessionCommandRouting
    {
        private ILiveSessionCommandRouting? _gameplay;
        private ILiveSessionCommandRouting? _commands;
        private ILiveSessionCommandRouting? _chat;
        private bool _activated;

        internal SessionCommandRoute(
            ILiveSessionCommandRouting gameplay,
            ILiveSessionCommandRouting commands,
            ILiveSessionCommandRouting chat)
        {
            _gameplay = gameplay
                ?? throw new ArgumentNullException(nameof(gameplay));
            _commands = commands
                ?? throw new ArgumentNullException(nameof(commands));
            _chat = chat
                ?? throw new ArgumentNullException(nameof(chat));
        }

        public void Activate()
        {
            if (_activated)
                return;
            if (_gameplay is null || _commands is null || _chat is null)
                throw new ObjectDisposedException(nameof(SessionCommandRoute));

            _gameplay.Activate();
            try
            {
                _commands.Activate();
                _chat.Activate();
                _activated = true;
            }
            catch (Exception activationError)
            {
                try
                {
                    Dispose();
                }
                catch (Exception disposalError)
                {
                    throw new AggregateException(
                        "Headless command-route activation and rollback failed.",
                        activationError,
                        disposalError);
                }

                throw;
            }
        }

        public void Dispose()
        {
            List<Exception>? failures = null;
            TryDispose(ref _chat, ref failures);
            TryDispose(ref _commands, ref failures);
            TryDispose(ref _gameplay, ref failures);
            if (failures is not null)
            {
                throw new AggregateException(
                    "Headless command routes did not detach cleanly.",
                    failures);
            }
        }

        private static void TryDispose(
            ref ILiveSessionCommandRouting? route,
            ref List<Exception>? failures)
        {
            if (route is not { } current)
                return;

            try
            {
                current.Dispose();
                route = null;
            }
            catch (Exception error)
            {
                (failures ??= []).Add(error);
            }
        }
    }

    private sealed class SessionCommandBridge : IRuntimeSessionCommands
    {
        private HeadlessSessionHost? _owner;

        internal void Bind(HeadlessSessionHost owner)
        {
            if (_owner is not null)
            {
                throw new InvalidOperationException(
                    "The headless session command bridge is already bound.");
            }
            _owner = owner
                ?? throw new ArgumentNullException(nameof(owner));
        }

        public RuntimeSessionStartResult Start(
            RuntimeGenerationToken expectedGeneration) =>
            RequireOwner().StartCore(expectedGeneration, reconnect: false);

        public RuntimeSessionStartResult Reconnect(
            RuntimeGenerationToken expectedGeneration) =>
            RequireOwner().StartCore(expectedGeneration, reconnect: true);

        public RuntimeTeardownAcknowledgement Stop(
            RuntimeGenerationToken expectedGeneration) =>
            RequireOwner()._liveSession.Stop(expectedGeneration);

        private HeadlessSessionHost RequireOwner() =>
            _owner
            ?? throw new InvalidOperationException(
                "The headless session command bridge is not bound.");
    }

    private readonly HeadlessSessionDescriptor _descriptor;
    private readonly HeadlessCredentialSecret _credential;
    private readonly HeadlessDiagnosticWriter _diagnostics;
    private readonly SessionStatusWriter _statusWriter;
    private RuntimeSessionStartStatus? _startOutcome;
    private bool _hasConnected;
    private readonly Dictionary<CharacterOptionId, bool> _declaredCharacterOptions;
    private RuntimeCharacterOptionsSeeder? _optionsSeeder;
    private readonly TimeSpan _reconnectQuiescence;

    /// <summary>
    /// How long a session that lost its server waits before each attempt to
    /// log in again. It keeps trying at this pace until one attempt gets back
    /// into the world.
    /// </summary>
    internal static readonly TimeSpan DefaultLostConnectionRetryInterval =
        TimeSpan.FromSeconds(30);

    private readonly TimeSpan _lostConnectionRetryInterval;
    private readonly TimeProvider _timeProvider;
    private readonly RuntimeWindowlessGenerationResetHost _resetHost = new();
    private readonly IDisposable _hostLease;
    private readonly IHeadlessBotPolicy _policy;
    private readonly IDisposable _policySubscription;
    private readonly HeadlessPluginSession _pluginSession;
    /// <summary>
    /// Paces the plugin tick, which every client raises at a fixed rate
    /// rather than once per turn of this one's own schedule.
    /// </summary>
    private readonly AcDream.Runtime.Plugins.RuntimePluginTickClock _pluginTick;
    private readonly AcDream.Runtime.Session.Windowless.RuntimeWindowlessLogout _logout;
    private readonly LiveChatCommandSurface _chatCommandSurface;
    private readonly LiveSessionHost _liveSession;
    private readonly RuntimeLocalPlayerFrameController _localPlayerFrame;

    /// <summary>The session's walks, when it loaded the game data they plan over.</summary>
    private readonly NavigationWalkController? _navigationWalk;

    private readonly HeadlessProcessContentOwner.HeadlessProcessContentLease?
        _contentLease;
    /// <summary>
    /// How this session takes hold of a world connection with nothing drawn:
    /// the route every client without a window builds.
    /// </summary>
    private readonly RuntimeWindowlessSessionRoute _windowless;
    private AcDream.Core.Net.WorldSession? _currentSession;
    private RuntimeWindowlessEventRoute? _eventRoute;
    /// <summary>The confirmation the server is waiting on, shown nowhere.</summary>
    private readonly RuntimeWindowlessConfirmations _confirmations;
    private int _disposeStage;
    private long _reconnectDeadline;
    private bool _reconnectPending;
    private bool _recoveringLostConnection;
    private ulong _stoppedGeneration;
    private string _accountName = string.Empty;
    private Exception? _fault;
    private bool _faulted;
    private bool _loggedOut;
    private bool _disposed;
    private bool _gracefulStopRequested;

    internal HeadlessSessionHost(
        HeadlessSessionDescriptor descriptor,
        HeadlessCredentialSecret credential,
        HeadlessDiagnosticWriter diagnostics,
        ILiveSessionOperations? sessionOperations = null,
        TimeProvider? timeProvider = null,
        TimeSpan? reconnectQuiescence = null,
        HeadlessProcessContentOwner.HeadlessProcessContentLease?
            contentLease = null,
        IHeadlessBotPolicy? policyOverride = null,
        IRuntimePlacementProjectionSink? placementSinkOverride = null,
        FellowshipAllegianceGateCoordinator? gateCoordinator = null,
        IEnumerable<string>? pluginRoots = null,
        IPluginStorage? storage = null,
        IPluginStorage? vtankProfiles = null,
        Func<bool>? logoutConfirmedOverride = null,
        string? peerDirectory = null,
        TimeSpan? lostConnectionRetryInterval = null)
    {
        _descriptor = descriptor
            ?? throw new ArgumentNullException(nameof(descriptor));
        _credential = credential
            ?? throw new ArgumentNullException(nameof(credential));
        _diagnostics = diagnostics
            ?? throw new ArgumentNullException(nameof(diagnostics));
        _declaredCharacterOptions = ParseDeclaredCharacterOptions(descriptor);
        _timeProvider = timeProvider ?? TimeProvider.System;
        _reconnectQuiescence = reconnectQuiescence
            ?? (sessionOperations is null
                ? TimeSpan.FromMilliseconds(2500)
                : TimeSpan.Zero);
        if (_reconnectQuiescence < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(reconnectQuiescence));
        }
        _confirmations = new RuntimeWindowlessConfirmations(() => _currentSession);
        _lostConnectionRetryInterval = lostConnectionRetryInterval
            ?? DefaultLostConnectionRetryInterval;
        if (_lostConnectionRetryInterval <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(lostConnectionRetryInterval));
        }

        GameRuntime? runtimeRef = null;
        IDisposable? hostLease = null;
        IHeadlessBotPolicy? policy = null;
        IDisposable? policySubscription = null;
        HeadlessPluginSession? pluginSession = null;
        try
        {
            var gameplay = new RuntimeWindowlessGameplayOperations();
            var runtime = new GameRuntime(
                AcDream.Headless.Plugins.HeadlessAutomationCapabilities
                    .BuildRuntimeDependencies(
                        gameplay,
                        _timeProvider,
                        message => diagnostics.Message(descriptor.Id, message),
                        sessionOperations));
            runtimeRef = runtime;
            if (contentLease is { } content)
            {
                runtime.CharacterOwner.InstallSpellMetadata(
                    content.MagicCatalog.SpellTable);
                AcDream.Core.CharGen.ChargenOptions chargenOptions;
                lock (content.DatLock)
                    chargenOptions = ChargenTableReader.Load(content.Dats);
                runtime.Session.CharacterCreationState.InstallOptions(
                    chargenOptions);
                runtime.HousePurchaseOwner.InstallTradeNoteDenominations(
                    content.TradeNotes);
            }
            gameplay.Bind(
                runtime,
                contentLease?.MagicCatalog,
                () => _accountName);
            var logout = new AcDream.Runtime.Session.Windowless.RuntimeWindowlessLogout(
                runtime,
                _timeProvider,
                isConfirmed: logoutConfirmedOverride);

            var bridge = new SessionCommandBridge();
            var commands = new DirectGameRuntimeCommandAdapter(
                runtime,
                bridge);

            var statusWriter = new SessionStatusWriter(
                descriptor.StatusFile,
                timeProvider: null,
                diagnostic: message => diagnostics.Message(
                    descriptor.Id,
                    message));
            // One registry, the plugin surface's own, and it does not exist
            // until the plugin session below is built -- so the verb lookup is
            // resolved when a line arrives rather than captured now.
            var chatCommandSurface = new LiveChatCommandSurface(
                line => pluginSession?.Host.TryHandlePluginCommand(line) == true,
                line => pluginSession?.Host.InterceptChatInput(line)
                    ?? AcDream.Plugin.Abstractions.PluginChatInputDecision.Pass);
            var loginCommands = new LoginCommandSequence(
                descriptor.LoginCommands,
                TimeSpan.FromMilliseconds(descriptor.LoginCommandDelayMs),
                new RuntimeChatCommandFeedback(runtime.CommunicationOwner),
                chatCommandSurface,
                failure => statusWriter.LoginCommandFailed(
                    descriptor.Id,
                    failure.CommandIndex,
                    failure.Command,
                    failure.Error),
                _timeProvider);
            bool SubmitChatText(string text)
            {
                SubmitOutcome outcome = ChatCommandRouter.Submit(
                    text,
                    new RuntimeChatCommandFeedback(runtime.CommunicationOwner),
                    chatCommandSurface,
                    ChatChannelKind.Say);
                return outcome is not (SubmitOutcome.Empty
                    or SubmitOutcome.UnknownCommand
                    or SubmitOutcome.Dropped);
            }
            bool AnswerConfirmation(uint contextId, bool accept) =>
                _confirmations.TryAnswer(contextId, accept);
            NavigationWalkController? navigationWalk = null;
            if (contentLease is { } navigationContent)
            {
                PhysicsEngine physics = runtime.EntityObjects.Physics.Engine;
                navigationWalk = new NavigationWalkController(
                    physics,
                    new RuntimeNavigationWalkBody(runtime.MovementOwner, runtime.Portal),
                    new RuntimeNavigationGoalSource(physics, runtime, runtime.MovementOwner),
                    message => diagnostics.Message(descriptor.Id, message),
                    new RuntimeNavigationDoors(
                        physics,
                        runtime,
                        objectId => commands.TryUseObject(objectId),
                        commands.TryAppraiseQuietly),
                    cellId => SealedDungeonCells.IsSealedDungeon(
                        navigationContent.Dats,
                        navigationContent.DatLock,
                        cellId));
                // A dead grid is tens to hundreds of megabytes the runtime
                // will not collect on its own while the bot idles; a headless
                // session has nothing to lose by collecting the moment the
                // controller lets one go. See the research note on navigation
                // cost.
                navigationWalk.GridReleased += static () => GC.Collect();
            }
            pluginSession = HeadlessPluginSession.Create(
                runtime,
                diagnostics,
                statusWriter,
                descriptor.Id,
                pluginRoots ?? [],
                descriptor.Plugins,
                storage,
                vtankProfiles,
                AcDream.Headless.Configuration.HeadlessPluginSettingsOptions
                    .Resolve(descriptor.PluginSettings),
                SubmitChatText,
                contentLease?.MagicCatalog,
                logout,
                AnswerConfirmation,
                RequestOwnGracefulStop,
                content: contentLease?.Dats,
                contentLock: contentLease?.DatLock,
                sessionCommands: commands,
                navigationWalk: navigationWalk,
                peerDirectory: peerDirectory,
                pluginTags: descriptor.PluginTags);
            // /nav and /motor are registered by the one binding pass both
            // hosts run, on the one registry the plugin surface owns, so
            // nothing is built for them here.
            var liveSession = new LiveSessionHost(
                runtime.Session,
                AcDream.Headless.Plugins.HeadlessAutomationCapabilities
                    .BuildSessionHostBindings(
                        new AcDream.Headless.Plugins.HeadlessSessionHostParts
                        {
                            CreateEvents = CreateEventRoute,
                            CreateCommands = session => new SessionCommandRoute(
                                gameplay.CreateRoute(session),
                                commands.CreateRoute(session),
                                chatCommandSurface.Attach(
                                    new LiveChatCommandRoute(
                                        CreateChatCommandBindings(
                                            session,
                                            runtime)))),
                            Reset = generation =>
                            {
                                // The windowed client ends its portal reveal
                                // before the world transit resets; so does
                                // this one.
                                _windowless?.EndPortalRevealForSessionReset();
                                runtime.ResetGeneration(generation, _resetHost);
                            },
                            Identity = runtime.PlayerIdentity,
                            Communication = runtime.CommunicationOwner,
                            Combat = runtime.ActionOwner.Combat,
                            NoteActiveCharacter = name =>
                            {
                                ActiveCharacterName = name;
                                if (descriptor.Policy?.Role
                                        == HeadlessBotPolicyRole.Recruit
                                    && gateCoordinator is not null)
                                {
                                    gateCoordinator.RecruitCharacterName = name;
                                }
                            },
                            Diagnostic = message => diagnostics.Message(
                                descriptor.Id,
                                message,
                                runtime.Generation.Value),
                            StatusWriter = statusWriter,
                            SessionId = descriptor.Id,
                            NoteConnected = () => _hasConnected = true,
                            LoginCommands = loginCommands,
                            Warn = message => diagnostics.Message(
                                descriptor.Id,
                                message,
                                runtime.Generation.Value),
                        }),
                runtime: runtime);

            Runtime = runtime;
            Commands = commands;
            _liveSession = liveSession;
            _chatCommandSurface = chatCommandSurface;
            _statusWriter = statusWriter;
            _localPlayerFrame =
                runtime.CreateLocalPlayerFrameController(
                    new RuntimeWindowlessLocalPlayerFrameHost(
                        runtime,
                        liveSession),
                    new RuntimeWindowlessMovementInputSource(
                        runtime.MovementOwner));
            _contentLease = contentLease;
            _windowless = new RuntimeWindowlessSessionRoute(
                runtime,
                contentLease,
                "headless",
                message => diagnostics.Message(
                    descriptor.Id,
                    message,
                    runtime.Generation.Value),
                placementSinkOverride: placementSinkOverride);
            _navigationWalk = navigationWalk;
            bridge.Bind(this);

            hostLease = runtime.AcquireHostLease(
                $"headless:{descriptor.Id}");
            policy = policyOverride
                ?? (descriptor.Mode == HeadlessSessionMode.Probe
                    ? new ProbeHeadlessBotPolicy()
                    : HeadlessBotPolicyFactory.Create(
                        descriptor.Policy!,
                        runtime,
                        () => _confirmations.Pending,
                        RespondToConfirmation,
                        gateCoordinator));
            policySubscription = runtime.Subscribe(policy);
            diagnostics.Lifecycle(
                descriptor.Id,
                "constructed",
                runtime);

            _hostLease = hostLease;
            _policy = policy;
            _policySubscription = policySubscription;
            _pluginSession = pluginSession;
            _pluginTick = new AcDream.Runtime.Plugins.RuntimePluginTickClock(
                pluginSession.Host.FireTick,
                () => runtime.Generation.Value);
            _logout = logout;
        }
        catch
        {
            pluginSession?.Dispose();
            policySubscription?.Dispose();
            policy?.Dispose();
            hostLease?.Dispose();
            contentLease?.Dispose();
            credential.Dispose();
            runtimeRef?.Dispose();
            throw;
        }
    }

    internal GameRuntime Runtime { get; }
    internal DirectGameRuntimeCommandAdapter Commands { get; }
    internal RuntimeCharacterOptionsSeeder? OptionsSeeder => _optionsSeeder;
    internal HeadlessPluginSession Plugins => _pluginSession;
    internal AcDream.Plugin.Abstractions.IPluginCommandRegistry PluginCommands =>
        _pluginSession.PluginCommands;
    internal string SessionId => _descriptor.Id;
    internal Action? ConsolePump { get; set; }
    internal string ActiveCharacterName { get; private set; } =
        string.Empty;
    internal bool IsPolicyComplete =>
        _faulted || _loggedOut || _policy.IsComplete || _gracefulStopRequested;
    internal bool IsFaulted => _faulted;
    internal Exception? Fault => _fault;
    internal bool IsReconnectPending => _reconnectPending;

    /// <summary>
    /// True from the moment the server is found gone until a login gets back
    /// into the world.
    /// </summary>
    internal bool IsRecoveringLostConnection => _recoveringLostConnection;
    internal HeadlessProcessContentOwner.HeadlessProcessContentLease?
        Content => _contentLease;
    internal long ReconnectDeadline => _reconnectPending
        ? _reconnectDeadline
        : throw new InvalidOperationException(
            "The headless session has no pending reconnect.");

    internal GameEvents.CharacterConfirmationRequest? PendingConfirmation =>
        _confirmations.Pending;

    /// <summary>The confirmation the server is waiting on, and how it is answered.</summary>
    internal RuntimeWindowlessConfirmations Confirmations => _confirmations;

    internal void RespondToConfirmation(bool accepted) =>
        _confirmations.Respond(accepted);

    // The server can resolve or cancel a confirmation on its own (a
    // different client answered it, the underlying request timed out, and
    // so on) without a matching RespondToConfirmation call. The pending
    // confirmation for that context id is cleared so a stale request does
    // not keep answering "yes" to a dialog that already closed; a newer
    // request that arrived after this one completed is left alone.
    internal void HandleConfirmationDone(
        GameEvents.CharacterConfirmationDone done) =>
        _confirmations.Done(done);

    /// <summary>The chat entry every front end of this session types into.</summary>
    internal RuntimeChatEntryOwner ChatEntry =>
        Runtime.CommunicationOwner.ChatEntryOwner;

    /// <summary>
    /// Whether this session's one command registry already answers a verb, so
    /// a front end with a verb of its own never shadows a plugin's.
    /// </summary>
    internal bool ClaimsPluginVerb(string verb) =>
        _pluginSession.Host.ClaimsPluginVerb(verb);

    /// <summary>
    /// Sends a line the way the chat box sends it: through the one chat entry,
    /// so the active channel, the reply target, the line history and the
    /// staged draft are the same whichever front end typed it. A null line
    /// sends the draft as it stands.
    /// </summary>
    internal SubmitOutcome SubmitConsoleLine(string? line) =>
        ChatEntry.Submit(
            line,
            new RuntimeChatCommandFeedback(Runtime.CommunicationOwner),
            _chatCommandSurface);

    internal RuntimeSessionStartResult Start()
    {
        _statusWriter.Started(_descriptor.Id);
        _pluginSession.Start();
        RuntimeSessionStartResult result =
            Commands.Session.Start(Runtime.Generation);
        _startOutcome = result.Status;
        return result;
    }

    internal RuntimeSessionStartResult Reconnect() =>
        Commands.Session.Reconnect(Runtime.Generation);

    internal void Tick(double deltaSeconds)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_reconnectPending)
            return;
        // The one frame step both clients take: the world's clock stands
        // still while there is no world to simulate.
        _ = Runtime.AdvanceFrameClock(deltaSeconds);
        _navigationWalk?.Tick(deltaSeconds);
        _localPlayerFrame.AdvanceBeforeNetwork(
            checked((float)deltaSeconds));
        // The same place in the frame a client with a window carries them:
        // after the character's own step and before anything the server has
        // said this frame is read, so every body spends the same elapsed time
        // on both clients.
        _windowless.AdvanceRemoteBodies(checked((float)deltaSeconds));
        Runtime.FinishRemoteBodyPass();
        _liveSession.Tick();
        if (_liveSession.ConnectionLost)
        {
            BeginLostConnectionRecovery();
            return;
        }
        _windowless.PumpAfterNetwork();
        _localPlayerFrame.RunPostNetworkCommandPhase();
        Runtime.ActionOwner.CombatAttack.Tick();
        _policy.Tick(Runtime, Commands);
        _pluginTick.Feed(deltaSeconds);
        ConsolePump?.Invoke();
        switch (_logout.Tick())
        {
            case AcDream.Runtime.Session.Windowless.RuntimeWindowlessLogoutOutcome.Completed:
                _loggedOut = true;
                break;
            case AcDream.Runtime.Session.Windowless.RuntimeWindowlessLogoutOutcome.TimedOut:
                Quarantine(new TimeoutException(
                    "A plugin-requested logout was never confirmed by the server."));
                break;
        }
    }

    internal RuntimeTeardownAcknowledgement Stop(string reason = "stopped")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        RuntimeTeardownAcknowledgement result =
            Commands.Session.Stop(Runtime.Generation);
        _currentSession = null;
        _windowless.ForgetSession();
        if (_hasConnected)
        {
            _hasConnected = false;
            _statusWriter.Disconnected(_descriptor.Id, reason);
        }
        return result;
    }

    /// <summary>
    /// Ends this session's own connection gracefully -- Stop() plus the
    /// same terminal-status accounting Dispose() uses -- without disposing
    /// this session's own runtime/plugin/policy objects yet (that still
    /// happens at the process's own final disposal, exactly like a
    /// policy-completed session already leaves them until then). The
    /// completion flag is only set, and the "exited"/graceful status only
    /// written, once Stop() actually converges -- a non-converged
    /// teardown is quarantined the same way any other fault is (so the
    /// scheduler still stops retrying it, but through _faulted, and the
    /// eventual final Dispose() reports the real "runtime-fault" status
    /// instead of a status file that already claimed a clean exit while
    /// process shutdown was still about to throw). Idempotent past a
    /// successful call, and safe to call from inside this session's own
    /// Tick() -- a plugin's RequestClose fires from there, the same
    /// guarantee RequestLogout above already relies on.
    /// </summary>
    internal bool RequestOwnGracefulStop()
    {
        if (_disposed)
            return false;
        if (_gracefulStopRequested)
            return true;

        _reconnectPending = false;
        _reconnectDeadline = 0L;
        _recoveringLostConnection = false;
        RuntimeTeardownAcknowledgement stopped = Stop();
        if (!stopped.IsComplete)
        {
            Quarantine(
                stopped.Error
                ?? new InvalidOperationException(
                    $"Headless session '{_descriptor.Id}' did not "
                        + "converge while ending its own session."));
            return false;
        }

        _gracefulStopRequested = true;
        (int exitCode, string exitReason) = ResolveTerminalStatus();
        _statusWriter.Exited(_descriptor.Id, exitCode, exitReason);
        return true;
    }

    internal void Quarantine(Exception error)
    {
        ArgumentNullException.ThrowIfNull(error);
        if (_faulted)
            return;

        _faulted = true;
        _fault = error;
        _reconnectPending = false;
        _reconnectDeadline = 0L;
        _recoveringLostConnection = false;
        _diagnostics.Failure(
            _descriptor.Id,
            "quarantined",
            error);
        try
        {
            _policySubscription.Dispose();
            RuntimeTeardownAcknowledgement stopped = Stop();
            if (!stopped.IsComplete)
            {
                _fault = new AggregateException(
                    error,
                    stopped.Error
                    ?? new InvalidOperationException(
                        $"Headless session '{_descriptor.Id}' did not quiesce after a fault."));
            }
        }
        catch (Exception teardownError)
        {
            _fault = new AggregateException(error, teardownError);
        }
    }

    internal RuntimeSessionStartResult CompletePendingReconnect(
        long nowTimestamp)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!_reconnectPending)
        {
            return new RuntimeSessionStartResult(
                RuntimeSessionStartStatus.Inactive,
                Runtime.Generation);
        }
        if (nowTimestamp < _reconnectDeadline)
        {
            return new RuntimeSessionStartResult(
                RuntimeSessionStartStatus.Deferred,
                Runtime.Generation);
        }

        _reconnectPending = false;
        _reconnectDeadline = 0L;
        RuntimeSessionStartResult result = StartLive(reconnect: true);
        if (!_recoveringLostConnection)
            return result;

        if (result.Status == RuntimeSessionStartStatus.Failed)
        {
            // The server is still gone or not yet taking logins: the attempt
            // is written to the diagnostics by StartLive, and the next one
            // waits another interval.
            ScheduleLostConnectionRetry(_timeProvider.GetTimestamp());
            return new RuntimeSessionStartResult(
                RuntimeSessionStartStatus.Deferred,
                Runtime.Generation);
        }

        _recoveringLostConnection = false;
        return result;
    }

    /// <summary>
    /// The server stopped answering and the runtime has already ended the
    /// session. The host records the end the way it records any other, then
    /// logs in again on a fixed interval until it is back in the world.
    /// </summary>
    private void BeginLostConnectionRecovery()
    {
        _diagnostics.Lifecycle(
            _descriptor.Id,
            "connection-lost",
            Runtime);
        RuntimeTeardownAcknowledgement stopped = Stop("connection-lost");
        if (!stopped.IsComplete)
        {
            Quarantine(
                stopped.Error
                ?? new InvalidOperationException(
                    $"Headless session '{_descriptor.Id}' did not "
                        + "converge after losing its server."));
            return;
        }

        _recoveringLostConnection = true;
        ScheduleLostConnectionRetry(_timeProvider.GetTimestamp());
    }

    private void ScheduleLostConnectionRetry(long nowTimestamp)
    {
        _reconnectDeadline = HeadlessMonotonicTime.Add(
            _timeProvider,
            nowTimestamp,
            _lostConnectionRetryInterval);
        _reconnectPending = true;
        _diagnostics.Lifecycle(
            _descriptor.Id,
            "reconnect-scheduled",
            Runtime);
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        while (!_disposed)
        {
            switch (_disposeStage)
            {
                case 0:
                {
                    _reconnectPending = false;
                    _reconnectDeadline = 0L;
                    _recoveringLostConnection = false;
                    RuntimeTeardownAcknowledgement stopped = Stop();
                    if (!stopped.IsComplete)
                    {
                        throw stopped.Error
                            ?? new InvalidOperationException(
                                $"Headless session '{_descriptor.Id}' did not complete teardown.");
                    }
                    _stoppedGeneration =
                        stopped.CurrentGeneration.Value;
                    _disposeStage++;
                    break;
                }
                case 1:
                    _diagnostics.Lifecycle(
                        _descriptor.Id,
                        "stopped",
                        Runtime);
                    _disposeStage++;
                    break;
                case 2:
                    _policySubscription.Dispose();
                    _disposeStage++;
                    break;
                case 3:
                    _policy.Dispose();
                    _disposeStage++;
                    break;
                case 4:
                    _pluginSession.Dispose();
                    _disposeStage++;
                    break;
                case 5:
                    _hostLease.Dispose();
                    _disposeStage++;
                    break;
                case 6:
                    _credential.Dispose();
                    _disposeStage++;
                    break;
                case 7:
                    Runtime.Dispose();
                    _disposeStage++;
                    break;
                case 8:
                    _contentLease?.Dispose();
                    _disposeStage++;
                    break;
                case 9:
                    _diagnostics.Message(
                        _descriptor.Id,
                        "disposed",
                        _stoppedGeneration);
                    (int exitCode, string exitReason) =
                        ResolveTerminalStatus();
                    _statusWriter.Exited(
                        _descriptor.Id,
                        exitCode,
                        exitReason);
                    _disposeStage++;
                    break;
                case 10:
                    // Last of all: the status file's handle, so the file is
                    // free the moment the session is gone.
                    _statusWriter.Dispose();
                    _disposeStage++;
                    _disposed = true;
                    break;
                default:
                    throw new InvalidOperationException(
                        "Unknown headless session teardown stage.");
            }
        }
    }

    private (int Code, string Reason) ResolveTerminalStatus()
    {
        if (_faulted)
        {
            return (
                (int)HeadlessExitCode.RuntimeError,
                "runtime-fault");
        }

        return _startOutcome switch
        {
            RuntimeSessionStartStatus.ProbeComplete =>
                ((int)HeadlessExitCode.Success, "probe"),
            null or RuntimeSessionStartStatus.Connected =>
                ((int)HeadlessExitCode.Success, "graceful"),
            _ =>
                ((int)HeadlessExitCode.ConnectionError, "connection-error"),
        };
    }

    private RuntimeSessionStartResult StartCore(
        RuntimeGenerationToken expectedGeneration,
        bool reconnect)
    {
        if (expectedGeneration != Runtime.Generation)
        {
            return new RuntimeSessionStartResult(
                RuntimeSessionStartStatus.StaleGeneration,
                Runtime.Generation);
        }
        if (_reconnectPending)
        {
            return new RuntimeSessionStartResult(
                RuntimeSessionStartStatus.Deferred,
                Runtime.Generation);
        }

        if (reconnect)
        {
            RuntimeTeardownAcknowledgement stopped = Stop("reconnect");
            if (!stopped.IsComplete)
            {
                return new RuntimeSessionStartResult(
                    RuntimeSessionStartStatus.Failed,
                    stopped.CurrentGeneration,
                    Error: stopped.Error
                        ?? new InvalidOperationException(
                            "The prior headless session did not quiesce before reconnect."));
            }

            if (_reconnectQuiescence > TimeSpan.Zero)
            {
                _reconnectDeadline = HeadlessMonotonicTime.Add(
                    _timeProvider,
                    _timeProvider.GetTimestamp(),
                    _reconnectQuiescence);
                _reconnectPending = true;
                _diagnostics.Lifecycle(
                    _descriptor.Id,
                    "reconnect-deferred",
                    Runtime);
                return new RuntimeSessionStartResult(
                    RuntimeSessionStartStatus.Deferred,
                    Runtime.Generation);
            }
        }

        return StartLive(reconnect);
    }

    private RuntimeSessionStartResult StartLive(bool reconnect)
    {
        string password = _credential.Reveal();
        try
        {
            LiveSessionConnectOptions options = new(
                Enabled: true,
                _descriptor.Endpoint.Host,
                _descriptor.Endpoint.Port,
                _descriptor.Account,
                password,
                MapCharacterSelector(_descriptor.Character),
                Probe: _descriptor.Mode == HeadlessSessionMode.Probe);
            LiveSessionStartResult result = _liveSession.Start(options);
            if (result.Selection is { } selection)
                _accountName = selection.AccountName;
            RuntimeSessionStartResult converted = Convert(result);
            if (converted.Error is { } error)
            {
                _diagnostics.Failure(
                    _descriptor.Id,
                    reconnect ? "reconnect" : "start",
                    error);
            }
            _diagnostics.Lifecycle(
                _descriptor.Id,
                reconnect ? "reconnect-result" : "start-result",
                Runtime);
            return converted;
        }
        finally
        {
            password = string.Empty;
        }
    }

    /// <summary>
    /// What a typed line needs to become speech, a tell, a channel message,
    /// a pose or a client command. It is the one runtime construction both
    /// clients use; this client lends it no window and no chat-log file, and
    /// the commands that need those say so.
    /// </summary>
    private LiveChatCommandBindings CreateChatCommandBindings(
        AcDream.Core.Net.WorldSession session,
        GameRuntime runtime) =>
        RuntimeChatCommandBindings.Create(
            runtime,
            session,
            log: message => _diagnostics.Message(
                _descriptor.Id,
                message,
                runtime.Generation.Value));


    /// <summary>The game-data file every host reads skill formulas from.</summary>
    private const uint SkillTableFileId = 0x0E000004u;

    /// <summary>
    /// The one owner of a windowless session's skill arithmetic, built from
    /// the same game data the graphical client reads.
    /// </summary>
    private Func<uint, uint, IReadOnlyDictionary<uint, uint>, uint>?
        CreateSkillFormulaBonusResolver()
    {
        if (_contentLease is not { } content)
            return null;
        DatReaderWriter.DBObjs.SkillTable? skillTable;
        bool haveSkillTable;
        lock (content.DatLock)
        {
            haveSkillTable = content.Dats
                .TryGet<DatReaderWriter.DBObjs.SkillTable>(
                    SkillTableFileId,
                    out skillTable);
        }
        if (!haveSkillTable)
        {
            // Said out loud: without the table the resolver answers nothing
            // for every skill, and a silent nothing looks exactly like a
            // character whose skills the server never sent.
            Console.Error.WriteLine(
                $"warning: skill formulas are unavailable (game-data file "
                + $"0x{SkillTableFileId:X8} could not be read); every skill "
                + "total will be left unadjusted.");
            skillTable = null;
        }

        return new LiveSkillCreditResolver(skillTable).Resolve;
    }

    /// <summary>
    /// The windowless half of the character bindings. The skill-formula
    /// resolver is the load-bearing part: without it every skill the server
    /// sends loses its attribute-derived term, so a bot reads its own skills
    /// far below what the server credits it with.
    /// </summary>
    internal LiveCharacterSessionBindings CreateCharacterBindings() =>
        AcDream.Headless.Plugins.HeadlessAutomationCapabilities
            .BuildCharacterSessionBindings(
                new AcDream.Headless.Plugins.HeadlessCharacterSessionParts
                {
                    Character = Runtime.CharacterOwner,
                    Combat = Runtime.ActionOwner.Combat,
                    ResolveSkillFormulaBonus =
                        CreateSkillFormulaBonusResolver(),
                    // Effects are stamped on the clock the plugin surface
                    // reads their time left against.
                    ClientTime = () => Runtime.EffectClock.NowSeconds,
                    OnConfirmationRequest = request =>
                    {
                        _confirmations.Request(request);
                        _pluginSession.Host.RaiseConfirmationRequested(
                            new PluginConfirmation(
                                request.ContextId,
                                (int)request.Type,
                                request.Message));
                    },
                    OnConfirmationDone = HandleConfirmationDone,
                    MovementStats = Runtime.MovementStats,
                    NoteOptionsSeeded = () =>
                        _optionsSeeder?.NoteOptionsSeeded(),
                    Warn = message => _diagnostics.Message(
                        SessionId,
                        message,
                        Runtime.Generation.Value),
                });

    private ILiveSessionEventRouting CreateEventRoute(
        AcDream.Core.Net.WorldSession session)
    {
        _currentSession = session;
        _confirmations.Forget();
        _optionsSeeder = new RuntimeCharacterOptionsSeeder(
            _declaredCharacterOptions,
            Runtime,
            Commands.Character);
        ILiveSessionEventRouting route = _windowless.Create(
            session,
            CreateCharacterBindings(),
            () => _optionsSeeder?.NoteLoginCompleteSent());
        _eventRoute = _windowless.EventRoute;
        return route;
    }

    private static Dictionary<CharacterOptionId, bool> ParseDeclaredCharacterOptions(
        HeadlessSessionDescriptor descriptor) =>
        RuntimeDeclaredCharacterOptions.Parse(
            descriptor.Id,
            descriptor.CharacterOptions);

    private static LiveSessionCharacterSelector? MapCharacterSelector(
        HeadlessCharacterSelector? selector) =>
        selector is null
            ? null
            : new(
                selector.Index,
                selector.Id,
                selector.Name);

    private RuntimeSessionStartResult Convert(
        LiveSessionStartResult result)
    {
        RuntimeSessionStartStatus status = result.Status switch
        {
            LiveSessionStartStatus.Disabled =>
                RuntimeSessionStartStatus.Disabled,
            LiveSessionStartStatus.MissingCredentials =>
                RuntimeSessionStartStatus.MissingCredentials,
            LiveSessionStartStatus.NoCharacters =>
                RuntimeSessionStartStatus.NoCharacters,
            LiveSessionStartStatus.Connected =>
                RuntimeSessionStartStatus.Connected,
            LiveSessionStartStatus.Deferred =>
                RuntimeSessionStartStatus.Deferred,
            LiveSessionStartStatus.Failed =>
                RuntimeSessionStartStatus.Failed,
            LiveSessionStartStatus.ProbeComplete =>
                RuntimeSessionStartStatus.ProbeComplete,
            _ => throw new ArgumentOutOfRangeException(
                nameof(result),
                result.Status,
                "Unknown live-session start result."),
        };
        return new RuntimeSessionStartResult(
            status,
            Runtime.Generation,
            result.Selection?.CharacterId ?? 0u,
            result.Selection?.CharacterName ?? string.Empty,
            result.Error);
    }
}
