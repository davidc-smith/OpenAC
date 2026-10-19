using AcDream.ClientExtensions;
using AcDream.Core.Combat;
using AcDream.Core.Items;
using AcDream.Core.Player;
using AcDream.Rynth.EngineUi;
using AcDream.Runtime;
using RynthCore.Engine.ImGuiBackend;
using RynthCore.Engine.UI.ScriptWindows;

namespace AcDream.Rynth.Host;

/// <summary>
/// Runs RynthCore plugins (RynthTracker, RynthOracle, RynthInventory, RynthChat, RynthAi) inside the client: the
/// API table reads the runtime, runtime events become plugin callbacks,
/// script windows draw on the client's ImGui overlay when it has one, with a
/// launcher bar and their layout saved per install, RynthCore's chat window
/// (over RynthChat's scrollback) with a Chat button on the bar, RynthCore's RynthAi dashboard with a RynthAi button, and
/// <c>/rynth tracker</c>, <c>/ro</c>, <c>/ginv</c> and RynthAi's <c>/ra</c>, <c>/mt</c>, <c>/ub</c>, <c>/vt</c> reach the plugins.
/// </summary>
public sealed class RynthClientExtension : IClientExtension
{
    private RynthHost? _host;
    private RynthChatHookup? _chatHookup;
    private RynthAiHookup? _aiHookup;
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
    private RynthChatKeyboard? _chatKeyboard;
    private Func<string>? _clipboardText;
    private Action<string>? _setClipboardText;
    private IDisposable? _keyboardSink;
    private IDisposable? _chatClaim;
    private IClientChat? _clientChat;
    private bool? _chatHidden;

    /// <summary>Test seam: extra plugin exports types loaded beside the shipped plugins.</summary>
    internal IReadOnlyList<Type> AdditionalPluginExports { get; init; } = [];

    public void Attach(ClientExtensionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        _log = context.Log;
        if (context.Overlay is not null)
        {
            _layout = new RynthLayoutStore(context.StateDirectory, context.Log);
            ImGuiPanelHost.Layout = _layout;
            _bar = new RynthBar(_layout, context.Log) { PanelButtons = () => [RynthChatWindow.Title, RynthAiDashboard.Title] };
            IClientOverlayIcons icons = context.Overlay.Icons;
            _iconSource = (kind, id) => kind switch
            {
                ScriptIconKind.Icon => icons.Icon(id),
                ScriptIconKind.Object => icons.ObjectIcon(id),
                ScriptIconKind.Spell => icons.SpellIcon(id),
                _ => IntPtr.Zero,
            };
            ScriptIcons.Source = _iconSource;
            if (PhosphorFont.Load() is { } phosphor)
                context.Overlay.AddMergedFont(phosphor, PhosphorIcons.Baked, 1.2f);
            else
                context.Log("[Rynth] the Phosphor icon font is missing; the bar shows letters.");
        }

        RynthInstallFolders.Apply();
        var view = new RuntimeRynthGameView(context.Runtime, context.SubmitChatCommand, context.Automation);
        bool scriptWindows = context.Overlay is not null;
        _host = new RynthHost(
            view,
            context.Log,
            [
                typeof(RynthCore.Plugin.RynthTracker.PluginExports),
                typeof(RynthCore.Plugin.RynthOracle.PluginExports),
                typeof(RynthCore.Plugin.RynthInventory.PluginExports),
                typeof(RynthCore.Plugin.RynthChat.PluginExports),
                typeof(RynthCore.Plugin.RynthAi.PluginExports),
                .. AdditionalPluginExports,
            ],
            scriptWindows,
            nav3DLayer: context.WorldTriangles?.CreateLayer());
        _chatHookup = new RynthChatHookup(_host, view, context.Log);
        _aiHookup = new RynthAiHookup(_host, view, context.Log);
        if (context.Overlay is not null)
            Guard("RynthAi", RynthAiDashboard.Start);
        if (context.Overlay is { } chatOverlay)
            StartChat(context, chatOverlay);
        _runtimeSubscription = context.Runtime.Subscribe(_host.Bridge);
        _combat = context.Runtime.ActionOwner.Combat;
        _combatEvents = new RynthCombatEvents(context.Runtime, _host.Bridge);
        _combat.KillLanded += OnKillLanded;
        _runtime = context.Runtime;
        _runtime.CommunicationOwner.LocalPlayerDied += OnLocalPlayerDied;
        _runtime.InventoryOwner.ExternalContainers.Changed += OnExternalContainerChanged;
        _runtime.InventoryOwner.Vendor.Changed += OnVendorChanged;
        _runtime.InventoryOwner.Objects.ContainerContentsReplaced += OnContainerContentsReplaced;
        _runtime.InventoryOwner.Objects.ObjectMoved += OnObjectMoved;
        _commands.Add(context.RegisterChatCommand("rynth", arguments => OnRynthCommand(view, arguments)));
        _commands.Add(context.RegisterChatCommand("ro", arguments => OfferChatBarText("/ro " + arguments)));
        _commands.Add(context.RegisterChatCommand("ginv", arguments => OfferChatBarText("/ginv " + arguments)));
        foreach (string verb in new[] { "ra", "mt", "ub", "vt" })
            _commands.Add(context.RegisterChatCommand(verb, arguments => OfferChatBarText($"/{verb} " + arguments)));
        if (context.Overlay is { } overlay)
            _frameCallback = overlay.AddFrameCallback(DrawFrame);
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

    /// <summary>The overlay frame: the script windows, then the bar over them.</summary>
    private void DrawFrame()
    {
        ImGuiPanelHost.DrawAll();
        _bar?.Draw();
    }

    public void Tick(double deltaSeconds)
    {
        _host?.Tick();
        GuardChat(TickChat);
        Guard("RynthAi", () => RynthAiDashboard.Tick());
    }

    public void Dispose()
    {
        // The chat seams go first, each on its own, so a failure in one still lets
        // the rest (and the plugin host below) shut down.
        if (_chatClaim is { } claim)
            GuardChat(claim.Dispose);
        if (_keyboardSink is { } sink)
            GuardChat(sink.Dispose);
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
            Guard("RynthAi", RynthAiDashboard.Stop);
            _aiHookup?.Dispose();
            _aiHookup = null;
            GuardChat(RynthChatWindow.Stop);
            _chatHookup?.Dispose();
            _chatHookup = null;
        }
        finally
        {
            _host?.Dispose();
            _host = null;
        }

        if (_layout is not null && ImGuiPanelHost.Layout == _layout)
            ImGuiPanelHost.Layout = null;
        _layout = null;
        if (_iconSource is not null && ScriptIcons.Source == _iconSource)
            ScriptIcons.Source = null;
        _iconSource = null;
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

    /// <summary>Offers chat-bar text to each plugin until one eats it.</summary>
    private void OfferChatBarText(string text)
    {
        if (_host is null)
            return;
        foreach (RynthPlugin plugin in _host.Plugins)
        {
            if (plugin.OnChatBarEnter(text.TrimEnd()))
                return;
        }
    }

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
