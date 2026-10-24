using AcDream.App.Audio;
using AcDream.App.Input;
using AcDream.App.Rendering;
using AcDream.App.Settings;
using AcDream.App.Streaming;
using AcDream.App.UI;
using AcDream.Core.Combat;
using AcDream.Core.Net;
using AcDream.Runtime;
using AcDream.Runtime.Gameplay;
using AcDream.Runtime.Chat;
using AcDream.Runtime.Session;
using AcDream.UI.Abstractions.Panels.Vitals;

namespace AcDream.App.Plugins;

/// <summary>
/// The parts the windowed host's live-session bindings are built from. The
/// session host owns these for the life of the process, before any window;
/// what only a window can do is reached through <see cref="Presentation"/>,
/// asked again at every call, so a binding reaches the window attached when
/// it runs and a session with no window is answered without one. Named here
/// so the record the session host is really given can be built -- and
/// inspected -- without opening a window.
/// </summary>
internal sealed record GraphicalSessionHostParts
{
    /// <summary>The outbound half of an opened world connection.</summary>
    public required Func<WorldSession, ILiveSessionCommandRouting> CreateCommands
    {
        get; init;
    }

    /// <summary>
    /// Puts the runtime back to its starting state for a new generation when
    /// no window is attached: nothing is drawn, so nothing drawn is taken
    /// down.
    /// </summary>
    public required Action<RuntimeGenerationToken> ResetWithoutWindow { get; init; }

    /// <summary>
    /// How the session takes hold of an opened world connection when no
    /// window is attached: the route every client without a window builds.
    /// </summary>
    public required Func<WorldSession, ILiveSessionEventRouting> CreateEventsWithoutWindow
    {
        get; init;
    }

    /// <summary>The window presenting the session right now, if any.</summary>
    public required Func<GraphicalSessionPresentation?> Presentation { get; init; }

    public required LocalPlayerIdentityState Identity { get; init; }

    public required RuntimeCommunicationState Communication { get; init; }

    public required CombatState Combat { get; init; }

    public required SessionStatusWriter StatusWriter { get; init; }

    public required string SessionId { get; init; }

    public LoginCommandSequence? LoginCommands { get; init; }

    /// <summary>
    /// Where a binding problem is reported. Host-shaped, not a binding: it
    /// lends the shared check somewhere to put a line about a declared
    /// binding that arrived empty.
    /// </summary>
    public Action<string>? Warn { get; init; }
}

/// <summary>
/// What a window lends the live session while it is attached: how it takes
/// hold of an opened world connection's events, how it puts what it draws
/// back to the start of a generation, the client commands that need
/// something to look at, and the owners the session's login steps reach.
/// </summary>
internal sealed record GraphicalSessionPresentation
{
    /// <summary>How this window takes hold of an opened world connection.</summary>
    public required Func<WorldSession, ILiveSessionEventRouting> CreateEvents
    {
        get; init;
    }

    /// <summary>
    /// Puts every owner, the runtime's and the window's, back to its starting
    /// state for a new generation, in the window's order.
    /// </summary>
    public required Action<RuntimeGenerationToken> Reset { get; init; }

    /// <summary>The client commands this window carries out.</summary>
    public required RuntimeClientCommandHostBindings ClientCommands { get; init; }

    public required RuntimeSettingsController Settings { get; init; }

    public required PlayerModeAutoEntry PlayerModeAutoEntry { get; init; }

    public required GpuWorldState WorldState { get; init; }

    public required DeferredLocalPlayerTeleportNetworkSink Teleport { get; init; }

    public VitalsVM? Vitals { get; init; }

    public RetailUiRuntime? RetainedUi { get; init; }

    public PaperdollFramePresenter? Paperdoll { get; init; }

    public WorldAudioSessionGate? WorldAudio { get; init; }

    /// <summary>
    /// The window's half of the server saying what the character options are
    /// (given the second options word), replayed when the window opens on a
    /// session whose options were said before it.
    /// </summary>
    public Action<uint>? ApplyCharacterOptions { get; init; }

    /// <summary>
    /// Draws the world of a session that already plays: the world frame's
    /// centre landblock and the cell the character stands in.
    /// </summary>
    public Action<uint, uint>? PresentPlayingWorld { get; init; }
}

/// <summary>
/// The parts the windowed host's character bindings are built from, for the
/// same reason.
/// </summary>
internal sealed record GraphicalCharacterSessionParts
{
    public required RuntimeCharacterState Character { get; init; }

    public required CombatState Combat { get; init; }

    public required RuntimeSettingsController Settings { get; init; }

    /// <summary>
    /// The runtime's own owner of how fast the character runs and jumps.
    /// </summary>
    public required AcDream.Runtime.Gameplay.RuntimeMovementStatsApplier
        MovementStats { get; init; }

    /// <summary>Turns a raw skill level into the level the server credits.</summary>
    public required Func<uint, uint, IReadOnlyDictionary<uint, uint>, uint>
        ResolveSkillFormulaBonus { get; init; }

    /// <summary>The clock a spell or attack window is measured against.</summary>
    public required Func<double> ClientTime { get; init; }

    public RetailUiRuntime? RetainedUi { get; init; }

    /// <summary>
    /// Told when the server has said what the character options are, so the
    /// options a session document declared can be compared against them.
    /// </summary>
    public required Action NoteOptionsSeeded { get; init; }

    /// <summary>Where a binding problem is reported, as above.</summary>
    public Action<string>? Warn { get; init; }
}

internal static partial class GraphicalAutomationCapabilities
{
    /// <summary>
    /// Builds the live-session bindings this host really hands the session
    /// host, so the census reads the code rather than a list beside it.
    /// </summary>
    internal static LiveSessionHostBindings BuildSessionHostBindings(
        GraphicalSessionHostParts parts)
    {
        ArgumentNullException.ThrowIfNull(parts);
        SessionStatusWriter status = parts.StatusWriter;
        string sessionId = parts.SessionId;
        LocalPlayerIdentityState identity = parts.Identity;
        RuntimeCommunicationState communication = parts.Communication;
        Func<GraphicalSessionPresentation?> window = parts.Presentation;
        var bindings = new LiveSessionHostBindings(
            Routing: new(
                session => window() is { } attached
                    ? attached.CreateEvents(session)
                    : parts.CreateEventsWithoutWindow(session),
                parts.CreateCommands),
            Reset: generation =>
            {
                if (window() is { } attached)
                    attached.Reset(generation);
                else
                    parts.ResetWithoutWindow(generation);
            },
            Selection: new(
                SetPlayerIdentity: id => identity.ServerGuid = id,
                SetVitalsIdentity: id => window()?.Vitals?.SetLocalPlayerGuid(id),
                SetChatIdentity: id =>
                    communication.Chat.SetLocalPlayerGuid(id),
                MarkPersistent: id => window()?.WorldState.MarkPersistent(id),
                SetVanishProbeIdentity: id => EntityVanishProbe.PlayerGuid = id,
                ClearCombat: () => parts.Combat.Clear(),
                ArmLoginTunnel: () => window()?.Teleport.ArmLoginTunnel()),
            EnteredWorld: new(
                SetActiveCharacter: name =>
                    window()?.Settings.SetActiveCharacter(name),
                RestoreLayout: () =>
                {
                    if (window() is { } attached)
                        RestoreLayout(attached);
                },
                SyncToolbar: () =>
                    window()?.RetainedUi?.SyncToolbarWindowButtons(),
                LoadCharacterSettings: name =>
                {
                    if (window() is { } attached)
                        LoadCharacterSettings(attached, name);
                },
                ArmPlayerModeAutoEntry: () =>
                    window()?.PlayerModeAutoEntry.Arm(),
                ResumeWorldAudio: () =>
                    window()?.WorldAudio?.ResumeForWorldEntry()),
            Connecting: (host, port, user) =>
                communication.Chat.OnSystemMessage(
                    $"connecting to {host}:{port} as {user}",
                    chatType: 1),
            Connected: () =>
            {
                communication.Chat.OnSystemMessage(
                    "connected — character list received",
                    chatType: 1);
                status.Connected(sessionId);
            },
            Roster: roster => status.CharacterList(sessionId, roster),
            CharacterEntered: selection => status.EnteredWorld(
                sessionId,
                selection.CharacterId,
                selection.CharacterName),
            LoginCommands: parts.LoginCommands,
            CharacterCreated: identityCreated => status.CharacterCreated(
                sessionId,
                identityCreated.Guid,
                identityCreated.Name),
            CreationFailed: rejection => status.CreationFailed(
                sessionId,
                rejection.RawCode,
                rejection.Reason,
                rejection.AttemptedName));
        LiveSessionBindingDeclarations.ReportDeclaredButUnfilled(
            "windowed",
            "live-session host binding",
            bindings,
            DeclaredSessionHostBindings,
            ConditionalSessionHostBindings,
            parts.Warn);
        return bindings;
    }

    /// <summary>
    /// The window's half of the server saying what the character options
    /// are: the chat settings and the UI lock follow them, open panels re-read
    /// them, and the options a session document declared are compared against
    /// them.
    /// </summary>
    internal static void ApplyServerCharacterOptions(
        RuntimeSettingsController settings,
        AcDream.Runtime.Gameplay.RuntimeCharacterOptionsState options,
        uint options2,
        Action noteOptionsSeeded)
    {
        settings.SyncChatFromServerOptions(options2);
        settings.SetUiLocked(options.GetOptionBit(
            AcDream.Core.Net.Messages.CharacterOptionId.LockUI));
        // Open option-bearing panels re-read the live bits at every seed,
        // which happens on login and again on a reconnect.
        settings.NotifyServerOptionsSeeded();
        noteOptionsSeeded();
    }

    private static void RestoreLayout(GraphicalSessionPresentation window)
    {
        RetailUiRuntime? retainedUi = window.RetainedUi;
        window.Settings.SetGameplayDisplay(true);
        retainedUi?.RestoreLayout();
        retainedUi?.InventoryPanelController?.Populate();
        window.Paperdoll?.MarkDirty();
        retainedUi?.RedeclareSocialPanelAfterWorldEntry();
    }

    private static void LoadCharacterSettings(
        GraphicalSessionPresentation window,
        string name)
    {
        window.Settings.LoadCharacterContext(name);
        window.RetainedUi?.LoadJournal(name);
    }

    /// <summary>
    /// What a window opening on a session that already plays is given, in
    /// the order a window present from the start was given it: the
    /// character's identity where the window shows it, its entry into the
    /// world (sound, the character's settings and layout, the toolbar, the
    /// camera following the character once it is drawn), the character
    /// options the server has said, then the world around the character.
    /// The session's own half of each step happened when the session took
    /// it; only the window's half is run here.
    /// </summary>
    internal static void PresentPlayingSession(
        GraphicalSessionPresentation window,
        LiveSessionCharacterSelection? entered,
        AcDream.Runtime.Gameplay.RuntimeCharacterOptionsState options,
        uint frameCenterLandblockId,
        uint playerCellId)
    {
        ArgumentNullException.ThrowIfNull(window);
        ArgumentNullException.ThrowIfNull(options);
        if (entered is not { } character)
            return;

        uint id = character.CharacterId;
        string name = character.CharacterName;
        window.Vitals?.SetLocalPlayerGuid(id);
        window.WorldState.MarkPersistent(id);
        window.WorldAudio?.ResumeForWorldEntry();
        window.Settings.SetActiveCharacter(name);
        RestoreLayout(window);
        window.RetainedUi?.SyncToolbarWindowButtons();
        LoadCharacterSettings(window, name);
        window.PlayerModeAutoEntry.Arm();
        if (options.HasServerSeed)
            window.ApplyCharacterOptions?.Invoke(options.Options2);
        window.PresentPlayingWorld?.Invoke(frameCenterLandblockId, playerCellId);
    }

    /// <summary>
    /// The client-command hooks a world connection's command route is built
    /// with. Each asks for the hooks of the window attached when the command
    /// runs, so a route opened before a window, or kept across one, reaches
    /// the window that is there; with none, a command that needs one says so
    /// in the words the windowless client uses.
    /// </summary>
    internal static RuntimeClientCommandHostBindings ResolveClientCommandsPerCall(
        Func<RuntimeClientCommandHostBindings?> current,
        Action needsAWindow)
    {
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(needsAWindow);
        return new RuntimeClientCommandHostBindings
        {
            ToggleFrameRate = () => Run(current()?.ToggleFrameRate),
            // The lock is a character option either way; only showing it
            // needs a window, so with none there is nothing more to do.
            SetUiLocked = locked => current()?.SetUiLocked?.Invoke(locked),
            // With no window the question is not asked and the continuation
            // does not run: a command that needs consent must not act
            // without it.
            ShowConfirmation = (message, completed) =>
            {
                if (current()?.ShowConfirmation is { } ask)
                    ask(message, completed);
                else
                    needsAWindow();
            },
            SaveUi = name => RunWith(current()?.SaveUi, name),
            LoadUi = name => RunWith(current()?.LoadUi, name),
            SaveAutoUi = () => Run(current()?.SaveAutoUi),
            LoadAutoUi = () => Run(current()?.LoadAutoUi),
            FillComponentBuyList = (category, maximumPrice) =>
            {
                if (current()?.FillComponentBuyList is { } fill)
                    fill(category, maximumPrice);
                else
                    needsAWindow();
            },
            SetLandscapeRadius = radius => RunWith(current()?.SetLandscapeRadius, radius),
            SetFieldOfView = degrees => RunWith(current()?.SetFieldOfView, degrees),
        };

        void Run(Action? hook)
        {
            if (hook is not null)
                hook();
            else
                needsAWindow();
        }

        void RunWith<T>(Action<T>? hook, T value)
        {
            if (hook is not null)
                hook(value);
            else
                needsAWindow();
        }
    }

    /// <summary>
    /// The character bindings of the session's route while no window is
    /// attached: nothing is shown, so a confirmation the server asks for goes
    /// to plugins alone, as on every client without a window, and the
    /// options the server seeds have no panel or chat box to follow them.
    /// </summary>
    internal static LiveCharacterSessionBindings BuildWindowlessCharacterSessionBindings(
        RuntimeCharacterState character,
        CombatState combat,
        AcDream.Runtime.Gameplay.RuntimeMovementStatsApplier movementStats,
        Func<uint, uint, IReadOnlyDictionary<uint, uint>, uint> resolveSkillFormulaBonus,
        Func<double> clientTime,
        Action<AcDream.Plugin.Abstractions.PluginConfirmation>? confirmationRequested,
        Action<string>? warn)
    {
        ArgumentNullException.ThrowIfNull(character);
        ArgumentNullException.ThrowIfNull(combat);
        ArgumentNullException.ThrowIfNull(movementStats);
        ArgumentNullException.ThrowIfNull(resolveSkillFormulaBonus);
        ArgumentNullException.ThrowIfNull(clientTime);
        var bindings = new LiveCharacterSessionBindings(
            combat,
            character,
            ResolveSkillFormulaBonus: (skill, raw, credits) =>
                resolveSkillFormulaBonus(skill, raw, credits),
            OnSkillsUpdated: (_, _) => movementStats.Apply("skills"),
            OnConfirmationRequest: request =>
                confirmationRequested?.Invoke(
                    new AcDream.Plugin.Abstractions.PluginConfirmation(
                        request.ContextId,
                        (int)request.Type,
                        request.Message)),
            OnConfirmationDone: static _ => { },
            ClientTime: () => clientTime(),
            OnMovementStatsUpdated: () => movementStats.Apply("stats"),
            OnCharacterOptionsChanged: static (_, _) => { });
        LiveSessionBindingDeclarations.ReportDeclaredButUnfilled(
            "windowed (no window)",
            "character-session binding",
            bindings,
            DeclaredCharacterSessionBindings,
            ConditionalCharacterSessionBindings,
            warn);
        return bindings;
    }

    /// <summary>
    /// Builds the character bindings this host really hands the event router.
    /// </summary>
    internal static LiveCharacterSessionBindings BuildCharacterSessionBindings(
        GraphicalCharacterSessionParts parts)
    {
        ArgumentNullException.ThrowIfNull(parts);
        RetailUiRuntime? retainedUi = parts.RetainedUi;
        RuntimeSettingsController settings = parts.Settings;
        RuntimeCharacterState character = parts.Character;
        AcDream.Runtime.Gameplay.RuntimeMovementStatsApplier movementStats =
            parts.MovementStats;
        var bindings = new LiveCharacterSessionBindings(
            parts.Combat,
            character,
            ResolveSkillFormulaBonus: (skill, raw, credits) =>
                parts.ResolveSkillFormulaBonus(skill, raw, credits),
            OnSkillsUpdated: (runSkill, jumpSkill) =>
                movementStats.Apply("skills"),
            OnConfirmationRequest: request =>
                retainedUi?.HandleConfirmationRequest(request),
            OnConfirmationDone: done =>
                retainedUi?.HandleConfirmationDone(done),
            ClientTime: () => parts.ClientTime(),
            OnMovementStatsUpdated: () => movementStats.Apply("stats"),
            OnCharacterOptionsChanged: (_, options2) =>
                ApplyServerCharacterOptions(
                    settings,
                    character.Options,
                    options2,
                    parts.NoteOptionsSeeded));
        LiveSessionBindingDeclarations.ReportDeclaredButUnfilled(
            "windowed",
            "character-session binding",
            bindings,
            DeclaredCharacterSessionBindings,
            ConditionalCharacterSessionBindings,
            parts.Warn);
        return bindings;
    }
}
