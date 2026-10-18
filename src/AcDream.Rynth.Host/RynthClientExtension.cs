using AcDream.ClientExtensions;
using AcDream.Core.Combat;
using AcDream.Core.Items;
using AcDream.Core.Player;
using AcDream.Rynth.ChatWindow;
using AcDream.Runtime;
using RynthCore.Engine.ImGuiBackend;
using RynthCore.Engine.UI.ScriptWindows;

namespace AcDream.Rynth.Host;

/// <summary>
/// Runs RynthCore plugins (RynthTracker, RynthOracle, RynthInventory, RynthChat) inside the client: the
/// API table reads the runtime, runtime events become plugin callbacks,
/// script windows draw on the client's ImGui overlay when it has one, with a
/// launcher bar and their layout saved per install, RynthCore's chat window
/// (over RynthChat's scrollback) with a Chat button on the bar, and
/// <c>/rynth tracker</c>, <c>/ro</c> and <c>/ginv</c> reach the plugins.
/// </summary>
public sealed class RynthClientExtension : IClientExtension
{
    private RynthHost? _host;
    private RynthChatHookup? _chatHookup;
    private Action<string>? _log;
    private IDisposable? _runtimeSubscription;
    private IDisposable? _frameCallback;
    private readonly List<IDisposable> _commands = new();
    private CombatState? _combat;
    private GameRuntime? _runtime;
    private RynthLayoutStore? _layout;
    private RynthBar? _bar;
    private Func<ScriptIconKind, uint, IntPtr>? _iconSource;

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
            _bar = new RynthBar(_layout, context.Log) { PanelButtons = () => [RynthChatWindow.Title] };
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

        var view = new RuntimeRynthGameView(context.Runtime, context.SubmitChatCommand);
        bool scriptWindows = context.Overlay is not null;
        _host = new RynthHost(
            view,
            context.Log,
            [
                typeof(RynthCore.Plugin.RynthTracker.PluginExports),
                typeof(RynthCore.Plugin.RynthOracle.PluginExports),
                typeof(RynthCore.Plugin.RynthInventory.PluginExports),
                typeof(RynthCore.Plugin.RynthChat.PluginExports),
                .. AdditionalPluginExports,
            ],
            scriptWindows);
        _chatHookup = new RynthChatHookup(_host, view, context.Log);
        if (context.Overlay is not null)
            RynthChatWindow.Start();
        _runtimeSubscription = context.Runtime.Subscribe(_host.Bridge);
        _combat = context.Runtime.ActionOwner.Combat;
        _combat.HealthChanged += OnHealthChanged;
        _combat.KillLanded += OnKillLanded;
        _runtime = context.Runtime;
        _runtime.CommunicationOwner.LocalPlayerDied += OnLocalPlayerDied;
        _runtime.InventoryOwner.ExternalContainers.Changed += OnExternalContainerChanged;
        _runtime.InventoryOwner.Objects.ContainerContentsReplaced += OnContainerContentsReplaced;
        _runtime.InventoryOwner.Objects.ObjectMoved += OnObjectMoved;
        _commands.Add(context.RegisterChatCommand("rynth", arguments => OnRynthCommand(view, arguments)));
        _commands.Add(context.RegisterChatCommand("ro", arguments => OfferChatBarText("/ro " + arguments)));
        _commands.Add(context.RegisterChatCommand("ginv", arguments => OfferChatBarText("/ginv " + arguments)));
        if (context.Overlay is { } overlay)
            _frameCallback = overlay.AddFrameCallback(DrawFrame);
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
        try { RynthChatWindow.Tick(); }
        catch (Exception error) { _log?.Invoke($"[Rynth] chat: {error.GetType().Name}: {error.Message}"); }
    }

    public void Dispose()
    {
        _frameCallback?.Dispose();
        _frameCallback = null;
        foreach (IDisposable command in _commands)
            command.Dispose();
        _commands.Clear();
        if (_combat is not null)
        {
            _combat.HealthChanged -= OnHealthChanged;
            _combat.KillLanded -= OnKillLanded;
            _combat = null;
        }

        if (_runtime is not null)
        {
            _runtime.CommunicationOwner.LocalPlayerDied -= OnLocalPlayerDied;
            _runtime.InventoryOwner.ExternalContainers.Changed -= OnExternalContainerChanged;
            _runtime.InventoryOwner.Objects.ContainerContentsReplaced -= OnContainerContentsReplaced;
            _runtime.InventoryOwner.Objects.ObjectMoved -= OnObjectMoved;
            _runtime = null;
        }

        _runtimeSubscription?.Dispose();
        _runtimeSubscription = null;
        RynthChatWindow.Stop();
        _chatHookup?.Dispose();
        _chatHookup = null;
        _host?.Dispose();
        _host = null;
        if (_layout is not null && ImGuiPanelHost.Layout == _layout)
            ImGuiPanelHost.Layout = null;
        _layout = null;
        if (_iconSource is not null && ScriptIcons.Source == _iconSource)
            ScriptIcons.Source = null;
        _iconSource = null;
        _bar = null;
    }

    private void OnHealthChanged(uint targetId, float healthRatio) =>
        _host?.Bridge.OnHealthChanged(targetId, healthRatio);

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
