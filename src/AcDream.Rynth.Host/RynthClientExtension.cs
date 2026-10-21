using AcDream.ClientExtensions;
using AcDream.Plugin.Abstractions;
using AcDream.Core.Combat;
using AcDream.Core.Items;
using AcDream.Core.Player;
using AcDream.Rynth.EngineUi;
using AcDream.Runtime;
using RynthCore.Engine.Compatibility;
using RynthCore.Engine.ImGuiBackend;
using RynthCore.Engine.UI.ScriptWindows;

namespace AcDream.Rynth.Host;

/// <summary>
/// Runs RynthCore plugins (RynthTracker, RynthOracle, RynthInventory, RynthChat, RynthAi) inside the client: the
/// API table reads the runtime, runtime events become plugin callbacks,
/// script windows draw on the client's ImGui overlay when it has one, with a
/// launcher bar and their layout saved per install, RynthCore's chat window
/// (over RynthChat's scrollback) with a Chat button on the bar, RynthCore's RynthAi dashboard with a RynthAi button, RynthCore's RynthNav panel and arrow with a RynthNav button, and
/// every line typed in either chat bar is offered to the plugins first, as in RynthCore, so <c>/rnav</c>, <c>/ro</c>, <c>/ginv</c>, <c>/ra</c>, <c>/mt</c>, <c>/ub</c>, <c>/vt</c>, <c>/lua</c> and script commands reach them; <c>/rynth tracker</c> and <c>/rc</c> are the host's.
/// </summary>
public sealed class RynthClientExtension : IClientExtension
{
    private RynthHost? _host;
    private IRynthGameView? _view;
    private string? _navDataHint;
    private RynthChatHookup? _chatHookup;
    private RynthInventoryHookup? _inventoryHookup;
    private RynthAiHookup? _aiHookup;
    private RynthMapHookup? _mapHookup;
    private Action<string>? _log;
    private IDisposable? _runtimeSubscription;
    private IDisposable? _frameCallback;
    private readonly List<IDisposable> _commands = new();
    private CombatState? _combat;
    private RynthCombatEvents? _combatEvents;
    private GameRuntime? _runtime;
    private RynthLayoutStore? _layout;
    private RynthBar? _bar;
    private Func<ScriptIconKind, uint, IntPtr>? _iconSource;
    private Func<float, IntPtr>? _fontSource;
    private RynthChatKeyboard? _chatKeyboard;
    private Func<string>? _clipboardText;
    private Action<string>? _setClipboardText;
    private IDisposable? _keyboardSink;
    private IDisposable? _hotkeySink;
    private IClientItemDrops? _itemDrops;
    private Func<bool>? _isTyping;
    private IDisposable? _chatClaim;
    private IClientChat? _clientChat;
    private bool? _chatHidden;
    private RynthPump? _pump;
    private RynthSnapshotBuilder? _builder;
    private RynthActionQueue? _actions;
    private RynthWorldSnapshot? _currentSnapshot;
    private IRynthGameView? _currentView;
    // The events taken with the current snapshot, for the pass it wakes (set on the frame, taken on the pump).
    private RynthEventBridge.EventBatch? _passEvents;
    private long _hubFaultLoggedAt = long.MinValue;
    private int _hubFaults;
    private const long HubFaultLogEveryMs = 10_000;

    /// <summary>
    /// Plugin passes on their own pump thread (RynthCore's 16 ms StartNormalPluginPump). Off:
    /// today's inline ticking on the frame thread. The client sets it from
    /// <c>ACDREAM_RYNTH_PUMP</c> / the session's <c>rynthPump</c>; off unless set, so a bare
    /// extension (tests, headless) ticks inline.
    /// </summary>
    public bool PumpEnabled { get; init; }

    /// <summary>Test seam: where pump passes run (a real thread unless set).</summary>
    internal Func<IRynthPumpScheduler> PumpScheduler { get; init; } = () => new ThreadPumpScheduler();

    /// <summary>Test seam: how long Dispose waits for a running pass (the spec's 5 s).</summary>
    internal TimeSpan PumpShutdownTimeout { get; init; } = TimeSpan.FromSeconds(5);

    /// <summary>The running pump, or null with the pump off (tests).</summary>
    internal RynthPump? Pump => _pump;

    /// <summary>Test seam: the plugin host.</summary>
    internal RynthHost? Host => _host;

    /// <summary>The RynthNav panel needs an overlay to draw in and a loaded RynthNav to show.</summary>
    internal static bool ShouldStartNavPanel(bool hasOverlay, IReadOnlyList<RynthPlugin> plugins) =>
        hasOverlay && RynthPluginLookup.Candidates(plugins, "RynthNav").Any();

    /// <summary>Test seam: extra plugin exports types loaded beside the shipped plugins.</summary>
    internal IReadOnlyList<Type> AdditionalPluginExports { get; init; } = [];

    /// <summary>The bar's panel buttons, in order (RynthCore's bar has the Radar too).</summary>
    internal static readonly IReadOnlyList<string> BarPanels =
        [RynthChatWindow.Title, RynthAiDashboard.Title, RynthNavPanel.Title, RynthCore.Engine.ImGuiBackend.Panels.RadarFace.Title];

    public void Attach(ClientExtensionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        _log = context.Log;
        if (context.Overlay is not null)
        {
            _layout = new RynthLayoutStore(context.StateDirectory, context.Log);
            ImGuiPanelHost.Layout = _layout;
            _bar = new RynthBar(_layout, context.Log) { PanelButtons = () => BarPanels };
            IClientOverlayIcons icons = context.Overlay.Icons;
            _iconSource = (kind, id) => kind switch
            {
                ScriptIconKind.Icon => icons.Icon(id),
                ScriptIconKind.Object => icons.ObjectIcon(id),
                ScriptIconKind.Spell => icons.SpellIcon(id),
                _ => IntPtr.Zero,
            };
            ScriptIcons.Source = _iconSource;
            _fontSource = context.Overlay.Font;
            ImGuiFonts.Source = _fontSource;
            if (PhosphorFont.Load() is { } phosphor)
                context.Overlay.AddMergedFont(phosphor, PhosphorIcons.Baked, 1.2f);
            else
                context.Log("[Rynth] the Phosphor icon font is missing; the bar shows letters.");
        }

        RynthInstallFolders.Apply();
        _navDataHint = RynthNavDataHint.For(Environment.GetEnvironmentVariable(RynthInstallFolders.NavDataVariable));
        if (_navDataHint is { } navHint)
            context.Log(navHint);
        // RynthAi finds the dats through its registry read (StandIns/RegistryStandIn); it
        // starts loading them on a pool thread in Startup, so this is set first.
        AcDream.Rynth.Plugin.RynthAi.StandIns.RegistryStandIn.InstallPath = context.DatDirectory;
        // RynthCore's engine finds client_portal.dat beside Environment.ProcessPath (redirected to this stand-in).
        AcDream.Rynth.EngineUi.ProcessPathStandIn.DatDirectory = context.DatDirectory;
        var live = new RuntimeRynthGameView(context.Runtime, context.SubmitChatCommand, context.Automation, context.Screen);
        _actions = new RynthActionQueue(
            line => context.Log($"[Rynth] {line}"),
            () => Volatile.Read(ref _currentSnapshot) is { } s && (s.IsPortaling || s.Dead));
        // Every slot and hook holds the routed view: live on the frame thread (and always,
        // with the pump off), the current pass's snapshot view anywhere else.
        var view = new ThreadRoutedGameView(live, () => Volatile.Read(ref _currentView) ?? live);
        _view = view;
        bool scriptWindows = context.Overlay is not null;
        _host = new RynthHost(
            view,
            context.Log,
            [
                typeof(RynthCore.Plugin.RynthTracker.PluginExports),
                typeof(RynthCore.Plugin.RynthOracle.PluginExports),
                typeof(RynthCore.Plugin.RynthInventory.PluginExports),
                typeof(RynthCore.Plugin.RynthChat.PluginExports),
                typeof(RynthCore.Plugin.RynthNav.PluginExports),
                typeof(RynthCore.Plugin.RynthAi.PluginExports),
                typeof(RynthCore.Plugin.RynthLua.PluginExports),
                .. AdditionalPluginExports,
            ],
            scriptWindows,
            nav3DLayer: context.WorldTriangles?.CreateLayer());
        _chatHookup = new RynthChatHookup(_host, view, context.Log);
        _inventoryHookup = new RynthInventoryHookup(view, context.Overlay?.Icons, context.Log);
        _aiHookup = new RynthAiHookup(_host, view, context.Log);
        _mapHookup = new RynthMapHookup(view, context.Log);
        if (context.Overlay is not null)
            Guard("RynthAi", RynthAiDashboard.Start);
        // RynthCore registers the RynthNav panel only with RynthNav installed; here, only when it loaded.
        if (ShouldStartNavPanel(context.Overlay is not null, _host.Plugins))
            Guard("RynthNav", RynthNavPanel.Start);
        if (context.Overlay is { } chatOverlay)
            StartChat(context, chatOverlay);
        if (context.Overlay is { } keyOverlay)
            StartHotkeys(context, keyOverlay);
        if (context.Overlay is not null && context.ItemDrops is { } drops)
            StartDrops(drops);
        if (PumpEnabled)
            StartPump(context, live);
        _runtimeSubscription = context.Runtime.Subscribe(_host.Bridge);
        _combat = context.Runtime.ActionOwner.Combat;
        _combatEvents = new RynthCombatEvents(context.Runtime, _host.Bridge, view);
        _combat.KillLanded += OnKillLanded;
        _runtime = context.Runtime;
        _runtime.CommunicationOwner.LocalPlayerDied += OnLocalPlayerDied;
        _runtime.InventoryOwner.ExternalContainers.Changed += OnExternalContainerChanged;
        _runtime.InventoryOwner.Vendor.Changed += OnVendorChanged;
        _runtime.InventoryOwner.Objects.ContainerContentsReplaced += OnContainerContentsReplaced;
        _runtime.InventoryOwner.Objects.ObjectMoved += OnObjectMoved;
        _commands.Add(context.RegisterChatCommand("rynth", arguments => OnRynthCommand(view, arguments)));
        _commands.Add(context.RegisterChatCommand("rc", arguments =>
            Guard("RynthAi", () => RynthCoreChatCommands.TryHandle(arguments, line => view.WriteChatLine(line, 0)))));
        _commands.AddRange(RouteChatToPlugins(context.Automation?.Chat, OfferChatBarText, context.RegisterChatCommand));
        if (context.Overlay is { } overlay)
            _frameCallback = overlay.AddFrameCallback(DrawFrame);
    }

    /// <summary>
    /// The pump: the snapshot builder, the action queue on the table and the stand-ins, the
    /// frame thread latched, logout quiesced around the plugins' OnLogout, a stopped pump
    /// replaced at the next login.
    /// </summary>
    private void StartPump(ClientExtensionContext context, RuntimeRynthGameView live)
    {
        RynthHost host = _host!;
        RynthActionQueue actions = _actions!;
        _builder = new RynthSnapshotBuilder(live, context.Runtime, context.Screen)
        {
            RynthVendorGeneration = () => host.Vendor.Generation,
        };
        host.Table.Actions = actions;
        RynthFrameActions.Queue = actions;
        RynthFrameThread.Latch();
        host.Bridge.BeforeLogout = () =>
        {
            _pump?.Quiesce(TimeSpan.FromSeconds(5));
            actions.Clear();
        };
        host.Bridge.AfterLogout = () => _pump?.Resume();
        host.Bridge.EnteringWorld = () =>
        {
            if (_pump is { Stopped: true } stopped)
            {
                stopped.Dispose();
                _pump = NewPump(context, live);
                _pump.Start();
                context.Log("[Rynth] the plugin pump restarted for this login.");
            }
        };
        _pump = NewPump(context, live);
        _pump.Start();
    }

    private RynthPump NewPump(ClientExtensionContext context, RuntimeRynthGameView live)
    {
        RynthHost host = _host!;
        RynthActionQueue actions = _actions!;
        RynthSnapshotBuilder builder = _builder!;
        return new RynthPump(
            pass: () =>
            {
                // Exactly the events taken with this pass's snapshot: none newer than it.
                host.PumpPass(Interlocked.Exchange(ref _passEvents, null) ?? RynthEventBridge.EventBatch.Empty);
                // The hub's own faults are logged here, so only a failing PumpPass counts toward the stop.
                RunHubPumpStep(context.Log);
            },
            publish: () =>
            {
                RynthWorldSnapshot snapshot = builder.Build();
                Volatile.Write(ref _currentSnapshot, snapshot);
                Volatile.Write(ref _currentView, new SnapshotRynthGameView(
                    snapshot, actions, live, reason => host.Vendor.FailQueued(reason)));
                // In the same frame-thread step as the snapshot, so nothing queued after it is in this batch.
                Volatile.Write(ref _passEvents, host.Bridge.TakeQueued());
            },
            actions: actions,
            log: line => context.Log($"[Rynth] pump: {line}"),
            notify: line => live.WriteChatLine(line, 0),
            scheduler: PumpScheduler());
    }

    /// <summary>
    /// The pump half of the hub step inside its own catch (RynthCore's StepFaultLog): a fault is
    /// logged at most once per 10 s with the count since the last line, and never fails the pass.
    /// </summary>
    private void RunHubPumpStep(Action<string> log)
    {
        try
        {
            RynthEngineHub.PumpTick();
        }
        catch (Exception error)
        {
            _hubFaults++;
            long now = Environment.TickCount64;
            if (_hubFaultLoggedAt == long.MinValue || now - _hubFaultLoggedAt >= HubFaultLogEveryMs)
            {
                _hubFaultLoggedAt = now;
                log($"[Rynth] hub step failed: {error.GetType().Name}: {error.Message} ({_hubFaults} since the last report)");
                _hubFaults = 0;
            }
        }
    }

    /// <summary>RynthCore's hotkeys: every key press offered to their HotkeyService before OpenAC sees it.</summary>
    private void StartHotkeys(ClientExtensionContext context, IClientOverlay overlay)
    {
        if (context.Input is { } input)
        {
            _isTyping = () => input.IsTyping;
            AcKeyboardFocus.IsTyping = _isTyping;
            AcKeyboardFocus.Where = () => input.TypingDescription;
        }

        _hotkeySink = overlay.AddKeyboardSink(new RynthHotkeyKeyboard(() => overlay.WantsKeyboard));
    }

    /// <summary>RynthCore's drop targets: items dragged out of the client's packs land on their panels.</summary>
    private void StartDrops(IClientItemDrops drops)
    {
        _itemDrops = drops;
        RynthCore.Engine.Compatibility.UiElementHooks.DragSource = () => drops.DraggedItem;
        Win32CursorStandIn.Source = () => ImGuiNET.ImGui.GetIO().MousePos;
        drops.SetDropClaimer(RynthDropClaim.Claim);
    }

    /// <summary>
    /// RynthCore's chat window, typed into through the overlay keyboard, and
    /// OpenAC's chat-entry key offered to it first. A bad chat settings file
    /// is logged; the rest of the extension carries on.
    /// </summary>
    private void StartChat(ClientExtensionContext context, IClientOverlay overlay)
    {
        if (!GuardChat(RynthChatWindow.Start))
            return;
        _clipboardText = () => overlay.ClipboardText;
        _setClipboardText = text => overlay.ClipboardText = text;
        Win32ClipboardStandIn.GetText = _clipboardText;
        Win32ClipboardStandIn.SetText = _setClipboardText;
        _chatKeyboard = new RynthChatKeyboard(context.Log);
        _keyboardSink = overlay.AddKeyboardSink(_chatKeyboard);
        if (context.Chat is { } chat)
        {
            _clientChat = chat;
            _chatClaim = chat.ClaimChatEntry(_chatKeyboard.TryClaimEntry);
        }
    }

    /// <summary>Runs extension work; a throw is logged under <paramref name="area"/>. True when it ran clean.</summary>
    private bool Guard(string area, Action work)
    {
        try
        {
            work();
            return true;
        }
        catch (Exception error)
        {
            _log?.Invoke($"[Rynth] {area}: {error.GetType().Name}: {error.Message}");
            return false;
        }
    }

    /// <summary>Runs chat work; a throw is logged as a chat failure. True when it ran clean.</summary>
    private bool GuardChat(Action work) => Guard("chat", work);

    /// <summary>Each tick: the chat source, the deferred send and Tell, and OpenAC's chat window hidden while Rynth chat owns chat.</summary>
    private void TickChat()
    {
        RynthChatWindow.Tick();
        _chatKeyboard?.Tick();
        if (_clientChat is { } clientChat)
        {
            bool hide = RynthCore.Engine.Compatibility.ChatHooks.RynthChatOwnsChat;
            if (_chatHidden != hide)
            {
                _chatHidden = hide;
                clientChat.SetChatWindowHidden(hide);
            }
        }
    }

    /// <summary>
    /// The overlay frame: the script windows, then the bar over them. Nothing at the
    /// character list: the windows stay open and come back with the next login.
    /// </summary>
    private void DrawFrame()
    {
        if (!RynthCore.Engine.Compatibility.LoginLifecycleHooks.HasObservedLoginComplete)
        {
            ImGuiPanelHost.SweepEmbedded();
            return;
        }
        Guard("RynthAi", RynthAiDashboard.BeginFrame);
        ImGuiPanelHost.DrawAll();
        _bar?.Draw();
    }

    public void Tick(double deltaSeconds)
    {
        if (_pump is { } pump)
        {
            // The frame's half: held keys, queued actions, and (when the pump is idle and due)
            // a snapshot and a pass; then the hub's frame-side hooks.
            _host?.FrameTick();
            pump.FrameTick();
            Guard("Rynth", RynthEngineHub.FrameTick);
        }
        else
        {
            _host?.Tick();
        }
        // The chat bar is not there at the character list: the hint waits for the first login.
        if (_navDataHint is { } hint && RynthCore.Engine.Compatibility.LoginLifecycleHooks.HasObservedLoginComplete)
        {
            _navDataHint = null;
            Guard("RynthNav", () => _view?.WriteChatLine(hint, 0));
        }
        GuardChat(TickChat);
        if (_inventoryHookup is { } inventory)
            Guard("RynthAi", inventory.Tick);
        if (_pump is null)
            Guard("Rynth", () => RynthEngineHub.Tick());
    }

    public void Dispose()
    {
        // The pump first: no pass may run while anything below comes down, and its thread must
        // be gone before the host frees the table's native buffers. If a pass outlived the
        // shutdown, the plugins are not shut down under it and the table is left in place.
        bool pumpExited = true;
        if (_pump is { } pump)
        {
            // A shutdown that throws leaves the thread's state unknown: treated as still running.
            pumpExited = false;
            Guard("pump", () => pumpExited = pump.Shutdown(PumpShutdownTimeout));
            if (!pumpExited)
                _log?.Invoke("[Rynth] the pump thread did not exit: the plugins are left running and their API table is not freed.");
            _pump = null;
        }

        // The chat seams go first, each on its own, so a failure in one still lets
        // the rest (and the plugin host below) shut down.
        if (_chatClaim is { } claim)
            GuardChat(claim.Dispose);
        if (_keyboardSink is { } sink)
            GuardChat(sink.Dispose);
        if (_hotkeySink is { } hotkeys)
            Guard("hotkeys", hotkeys.Dispose);
        _hotkeySink = null;
        if (_isTyping is not null && AcKeyboardFocus.IsTyping == _isTyping)
        {
            AcKeyboardFocus.IsTyping = null;
            AcKeyboardFocus.Where = null;
        }
        _isTyping = null;
        Guard("hotkeys", RynthCore.Engine.Hotkeys.HotkeyService.Shutdown);
        Guard("hotkeys", RynthHotkeyKeyboard.ForgetHeldKeys);
        if (_itemDrops is { } drops)
        {
            drops.SetDropClaimer(null);
            _itemDrops = null;
        }
        RynthCore.Engine.Compatibility.UiElementHooks.DragSource = null;
        Win32CursorStandIn.Source = null;
        if (_chatHidden == true && _clientChat is { } clientChat)
            GuardChat(() => clientChat.SetChatWindowHidden(false));
        _chatClaim = null;
        _keyboardSink = null;
        _clientChat = null;
        _chatHidden = null;
        _chatKeyboard = null;
        if (_clipboardText is not null && Win32ClipboardStandIn.GetText == _clipboardText)
            Win32ClipboardStandIn.GetText = null;
        if (_setClipboardText is not null && Win32ClipboardStandIn.SetText == _setClipboardText)
            Win32ClipboardStandIn.SetText = null;
        _clipboardText = null;
        _setClipboardText = null;
        _frameCallback?.Dispose();
        _frameCallback = null;
        foreach (IDisposable command in _commands)
            command.Dispose();
        _commands.Clear();
        if (_combat is not null)
        {
            _combat.KillLanded -= OnKillLanded;
            _combat = null;
        }

        if (_runtime is not null)
        {
            _runtime.CommunicationOwner.LocalPlayerDied -= OnLocalPlayerDied;
            _runtime.InventoryOwner.ExternalContainers.Changed -= OnExternalContainerChanged;
            _runtime.InventoryOwner.Vendor.Changed -= OnVendorChanged;
            _runtime.InventoryOwner.Objects.ContainerContentsReplaced -= OnContainerContentsReplaced;
            _runtime.InventoryOwner.Objects.ObjectMoved -= OnObjectMoved;
            _runtime = null;
        }

        _combatEvents?.Dispose();
        _combatEvents = null;
        _runtimeSubscription?.Dispose();
        _runtimeSubscription = null;
        try
        {
            Guard("RynthNav", RynthNavPanel.Stop);
            Guard("RynthAi", RynthAiDashboard.Stop);
            _aiHookup?.Dispose();
            _aiHookup = null;
            _mapHookup?.Dispose();
            _mapHookup = null;
            GuardChat(RynthChatWindow.Stop);
            _chatHookup?.Dispose();
            _chatHookup = null;
            _inventoryHookup?.Dispose();
            _inventoryHookup = null;
        }
        finally
        {
            _host?.Dispose(pumpExited);
            _host = null;
            _builder?.Dispose();
            _builder = null;
            if (_actions is { } actions)
            {
                actions.Clear();
                if (RynthFrameActions.Queue == actions)
                {
                    RynthFrameThread.Reset();
                    RynthFrameActions.Queue = null;
                }
            }
            _actions = null;
            Volatile.Write(ref _currentView, null);
            Volatile.Write(ref _currentSnapshot, null);
            AcDream.Rynth.Plugin.RynthAi.StandIns.RegistryStandIn.InstallPath = null;
            AcDream.Rynth.EngineUi.ProcessPathStandIn.DatDirectory = null;
        }

        if (_layout is not null && ImGuiPanelHost.Layout == _layout)
            ImGuiPanelHost.Layout = null;
        _layout = null;
        if (_iconSource is not null && ScriptIcons.Source == _iconSource)
            ScriptIcons.Source = null;
        _iconSource = null;
        if (_fontSource is not null && ImGuiFonts.Source == _fontSource)
            ImGuiFonts.Source = null;
        _fontSource = null;
        _bar = null;
    }

    private void OnKillLanded(string deathMessage, uint victimId) =>
        _host?.Bridge.OnKillLanded(deathMessage);

    // The death message is the server's word that the player died, so the
    // tracker is told even before the player's vitals (and so its max) arrive.
    private void OnLocalPlayerDied(string deathMessage)
    {
        if (_runtime is not { } runtime)
            return;
        uint maxHealth = runtime.CharacterOwner.LocalPlayer.GetMaxApprox(LocalPlayerState.VitalKind.Health) ?? 0;
        _host?.Bridge.OnLocalPlayerDied(runtime.PlayerIdentity.ServerGuid, Math.Max(maxHealth, 1));
    }

    private void OnExternalContainerChanged(ExternalContainerTransition transition)
    {
        if (_host is not { } host)
            return;
        switch (transition.Kind)
        {
            case ExternalContainerTransitionKind.Opened:
                host.Bridge.OnContainerOpened(transition.PreviousContainerId, transition.ContainerId);
                break;
            // Opening B while A is open raises ReplacementRequested(prev=A) and drops A's
            // contents; the later Opened carries previous 0, so this is where A closes.
            case ExternalContainerTransitionKind.ReplacementRequested:
            case ExternalContainerTransitionKind.Closed:
            case ExternalContainerTransitionKind.Reset:
                host.Bridge.OnContainerClosed(transition.PreviousContainerId);
                break;
        }
    }

    // VendorState carries the vendor that closed (or was replaced) in PreviousVendorId.
    private void OnVendorChanged(VendorTransition transition)
    {
        if (_host is not { } host)
            return;
        RynthVendorChange change = transition.Kind switch
        {
            VendorStateTransitionKind.Opened => RynthVendorChange.Opened,
            VendorStateTransitionKind.Refreshed => RynthVendorChange.Refreshed,
            VendorStateTransitionKind.Closed => RynthVendorChange.Closed,
            _ => RynthVendorChange.Reset,
        };
        host.OnVendorChanged(change, transition.PreviousVendorId, transition.VendorId);
    }

    private void OnContainerContentsReplaced(uint containerId) =>
        _host?.Bridge.OnContainerContentsChanged(containerId);

    private void OnObjectMoved(ClientObjectMove move)
    {
        if (_host is not { } host)
            return;
        host.Bridge.OnContainerContentsChanged(move.Previous.ContainerId);
        if (move.Current.ContainerId != move.Previous.ContainerId)
            host.Bridge.OnContainerContentsChanged(move.Current.ContainerId);
    }

    /// <summary>The plugin commands forwarded as verbs when the client has no chat interceptor.</summary>
    internal static readonly string[] ForwardedVerbs = ["ro", "ginv", "ra", "mt", "ub", "vt", "lua", "rnav"];

    /// <summary>
    /// RynthCore's chat-bar pre-dispatch (ChatCommandDispatcher.Dispatch): every line typed
    /// or sent through InvokeChatParser goes to each plugin's OnChatBarEnter first, and one
    /// that eats it stops it. Here that is the client's chat input interceptor; a client
    /// without one (tests, headless) still reaches the plugins' own commands through verbs.
    /// </summary>
    internal static IReadOnlyList<IDisposable> RouteChatToPlugins(
        IPluginChat? chat, Func<string, bool> offer, Func<string, Action<string>, IDisposable> register)
    {
        if (chat is not null)
            return [chat.RegisterInputInterceptor(line => offer(line) ? PluginChatInputDecision.Suppress : PluginChatInputDecision.Pass)];
        var handles = new List<IDisposable>();
        foreach (string verb in ForwardedVerbs)
            handles.Add(register(verb, arguments => offer($"/{verb} " + arguments)));
        return handles;
    }

    /// <summary>One plugin's OnChatBarEnter: the line and the eat flag shared by all of them.</summary>
    internal delegate void ChatBarOffer(string text, ref int eat);

    /// <summary>Set while a line is being offered, so a line a plugin submits from inside it skips the pre-dispatch.</summary>
    [ThreadStatic]
    private static bool _inPluginPreDispatch;

    /// <summary>
    /// RynthCore's DispatchChatBarEnter: the line goes to every plugin in load order with one
    /// shared eat flag (an eaten line still reaches later plugins, which RynthLua relies on).
    /// A line submitted from inside a plugin's handler is not offered again, as RynthCore's
    /// _inPluginPreDispatch. True when a plugin ate it.
    /// </summary>
    internal static bool OfferToPlugins(IEnumerable<ChatBarOffer> offers, string text)
    {
        if (_inPluginPreDispatch)
            return false;
        _inPluginPreDispatch = true;
        try
        {
            int eat = 0;
            foreach (ChatBarOffer offer in offers)
                offer(text, ref eat);
            return eat != 0;
        }
        finally
        {
            _inPluginPreDispatch = false;
        }
    }

    /// <summary>Offers chat-bar text to every plugin. True when one ate it.</summary>
    private bool OfferChatBarText(string text) =>
        _host is not null
        && OfferToPlugins(_host.Plugins.Select(plugin => new ChatBarOffer(plugin.OnChatBarEnter)), text.TrimEnd());

    private void OnRynthCommand(IRynthGameView view, string arguments)
    {
        if (!string.Equals(arguments.Trim(), "tracker", StringComparison.OrdinalIgnoreCase))
        {
            view.WriteChatLine("Usage: /rynth tracker", 0);
            return;
        }

        RynthPlugin? tracker = _host?.Plugins.FirstOrDefault(
            plugin => plugin.Name.EndsWith("RynthTracker", StringComparison.Ordinal));
        view.WriteChatLine(
            TrackerSnapshotText.Format(tracker?.CallStringExport("RynthTrackerGetSnapshotJson")),
            0);
    }
}
