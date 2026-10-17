using AcDream.ClientExtensions;
using AcDream.Core.Combat;

namespace AcDream.Rynth.Host;

/// <summary>
/// Runs RynthCore plugins (this PoC: RynthTracker) inside the client:
/// the API table reads the runtime, runtime events become plugin callbacks,
/// and <c>/rynth tracker</c> prints the tracker's numbers.
/// </summary>
public sealed class RynthClientExtension : IClientExtension
{
    private RynthHost? _host;
    private IDisposable? _runtimeSubscription;
    private IDisposable? _command;
    private CombatState? _combat;

    public void Attach(ClientExtensionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var view = new RuntimeRynthGameView(context.Runtime);
        _host = new RynthHost(
            view,
            context.Log,
            [typeof(RynthCore.Plugin.RynthTracker.PluginExports)]);
        _runtimeSubscription = context.Runtime.Subscribe(_host.Bridge);
        _combat = context.Runtime.ActionOwner.Combat;
        _combat.HealthChanged += OnHealthChanged;
        _combat.KillLanded += OnKillLanded;
        _command = context.RegisterChatCommand("rynth", arguments => OnCommand(view, arguments));
    }

    public void Tick(double deltaSeconds) => _host?.Tick();

    public void Dispose()
    {
        _command?.Dispose();
        if (_combat is not null)
        {
            _combat.HealthChanged -= OnHealthChanged;
            _combat.KillLanded -= OnKillLanded;
        }

        _runtimeSubscription?.Dispose();
        _host?.Dispose();
        _host = null;
    }

    private void OnHealthChanged(uint targetId, float healthRatio) =>
        _host?.Bridge.OnHealthChanged(targetId, healthRatio);

    private void OnKillLanded(string deathMessage, uint victimId) =>
        _host?.Bridge.OnKillLanded(deathMessage);

    private void OnCommand(IRynthGameView view, string arguments)
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
