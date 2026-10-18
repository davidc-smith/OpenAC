using AcDream.ClientExtensions;
using AcDream.Core.Combat;
using RynthCore.Engine.ImGuiBackend;
using RynthCore.Engine.UI.ScriptWindows;

namespace AcDream.Rynth.Host;

/// <summary>
/// Runs RynthCore plugins (RynthTracker, RynthOracle) inside the client: the
/// API table reads the runtime, runtime events become plugin callbacks,
/// script windows draw on the client's ImGui overlay when it has one, with a
/// launcher bar and their layout saved per install, and
/// <c>/rynth tracker</c> and <c>/ro</c> reach the plugins.
/// </summary>
public sealed class RynthClientExtension : IClientExtension
{
    private RynthHost? _host;
    private IDisposable? _runtimeSubscription;
    private IDisposable? _frameCallback;
    private readonly List<IDisposable> _commands = new();
    private CombatState? _combat;
    private RynthLayoutStore? _layout;
    private RynthBar? _bar;
    private Func<ScriptIconKind, uint, IntPtr>? _iconSource;

    public void Attach(ClientExtensionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (context.Overlay is not null)
        {
            _layout = new RynthLayoutStore(context.StateDirectory, context.Log);
            ImGuiPanelHost.Layout = _layout;
            _bar = new RynthBar(_layout, context.Log);
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
            ],
            scriptWindows);
        _runtimeSubscription = context.Runtime.Subscribe(_host.Bridge);
        _combat = context.Runtime.ActionOwner.Combat;
        _combat.HealthChanged += OnHealthChanged;
        _combat.KillLanded += OnKillLanded;
        _commands.Add(context.RegisterChatCommand("rynth", arguments => OnRynthCommand(view, arguments)));
        _commands.Add(context.RegisterChatCommand("ro", arguments => OfferChatBarText("/ro " + arguments)));
        if (context.Overlay is { } overlay)
            _frameCallback = overlay.AddFrameCallback(DrawFrame);
    }

    /// <summary>The overlay frame: the script windows, then the bar over them.</summary>
    private void DrawFrame()
    {
        ImGuiPanelHost.DrawAll();
        _bar?.Draw();
    }

    public void Tick(double deltaSeconds) => _host?.Tick();

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

        _runtimeSubscription?.Dispose();
        _runtimeSubscription = null;
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
