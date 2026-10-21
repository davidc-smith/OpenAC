using RynthCore.PluginSdk;

namespace AcDream.Rynth.Host;

/// <summary>
/// Hosts RynthCore plugins: owns the API table, initialises each plugin,
/// ticks them and shuts them down. One host per process (the table is
/// process-wide).
/// </summary>
public sealed unsafe class RynthHost : IDisposable
{
    private readonly RynthApiTable _table;
    private readonly List<RynthPlugin> _plugins = new();
    private readonly Action<string> _log;

    /// <param name="log">Gets every host and plugin log line, prefixed "[Rynth] ".</param>
    /// <param name="exportsTypes">Each plugin's exports class (its PluginExports).</param>
    public RynthHost(IRynthGameView view, Action<string> log, IEnumerable<Type> exportsTypes, bool scriptWindows = false,
        AcDream.Plugin.Abstractions.IPluginWorldTriangleLayer? nav3DLayer = null)
    {
        ArgumentNullException.ThrowIfNull(exportsTypes);
        ArgumentNullException.ThrowIfNull(log);
        _log = line => log($"[Rynth] {line}");
        _table = new RynthApiTable(view, _log, scriptWindows, nav3DLayer);
        // A plugin may ask for another's interface from its own Init (RynthCore's order: the
        // ones loaded before it are there).
        _table.Plugins = _plugins;
        RynthCore.Engine.RynthLog.Sink = _log;
        RynthCore.Engine.ImGuiBackend.ImGuiPanelHost.Log = _log;
        try
        {
            foreach (Type exportsType in exportsTypes)
                Load(exportsType);
            Bridge = new RynthEventBridge(_plugins, _log);
            Bridge.LoggedOut += _table.Salvage.Close;
        }
        catch
        {
            // Leave nothing behind: initialised plugins, owners, sinks, flags and the table.
            try { Dispose(); }
            catch (Exception cleanup) { _log($"cleanup after a failed start also failed: {cleanup.Message}"); }
            throw;
        }
    }

    internal RynthMotionHolds Holds => _table.Holds;

    internal RynthVendorTrades Vendor => _table.Vendor;

    /// <summary>The engine API table every plugin got (tests call its slots directly).</summary>
    internal RynthCoreApiNative* Api => _table.Native;

    public IReadOnlyList<RynthPlugin> Plugins => _plugins;

    /// <summary>The runtime observer that feeds the plugins.</summary>
    public RynthEventBridge Bridge { get; }

    /// <summary>The client's vendor state changed: RynthCore's vendor events, and the vendor list generation.</summary>
    public void OnVendorChanged(RynthVendorChange change, uint previousVendorId, uint vendorId)
    {
        switch (change)
        {
            case RynthVendorChange.Opened:
                // RynthCore queues the replaced vendor's close before the new one's open.
                if (previousVendorId != 0 && previousVendorId != vendorId)
                    Bridge.OnVendorClosed(previousVendorId);
                _table.Vendor.NoteOpened();
                Bridge.OnVendorOpened(vendorId);
                break;
            case RynthVendorChange.Refreshed:
                _table.Vendor.NoteOpened();
                Bridge.OnVendorOpened(vendorId);
                break;
            case RynthVendorChange.Closed:
                if (previousVendorId != 0)
                    Bridge.OnVendorClosed(previousVendorId);
                break;
            case RynthVendorChange.Reset:
                _table.Vendor.Reset();
                if (previousVendorId != 0)
                    Bridge.OnVendorClosed(previousVendorId);
                break;
        }
    }

    /// <summary>
    /// Inline mode (the pump off): the frame's queued events, the vendor timeout, the held keys,
    /// then each plugin inside one Nav3D frame, as RynthCore's frame does. Today's order.
    /// </summary>
    public void Tick()
    {
        Bridge.DeliverQueued();
        TickVendor();
        // A view fault here must not cost the plugins their tick.
        FrameTick();
        TickPlugins();
    }

    /// <summary>The pump's pass: events, the vendor timeout, then each plugin inside one Nav3D frame (RynthCore's TickAll).</summary>
    public void PumpPass()
    {
        Bridge.DeliverQueued();
        TickVendor();
        TickPlugins();
    }

    /// <summary>
    /// The pump's pass with the events taken when its snapshot was published
    /// (<see cref="RynthEventBridge.TakeQueued"/>): exactly those, then as <see cref="PumpPass()"/>.
    /// </summary>
    internal void PumpPass(RynthEventBridge.EventBatch events)
    {
        Bridge.Deliver(events);
        TickVendor();
        TickPlugins();
    }

    /// <summary>The frame's part: held keys re-sent (RynthMotionHolds watches the client drop them).</summary>
    public void FrameTick()
    {
        try { _table.Holds.Sync(); }
        catch (Exception error) { _log($"holding movement failed: {error.Message}"); }
    }

    /// <summary>The table every plugin got; the pump sets its action queue.</summary>
    internal RynthApiTable Table => _table;

    private void TickVendor()
    {
        try { _table.Vendor.Tick(); }
        catch (Exception error) { _log($"vendor tick failed: {error.Message}"); }
    }

    private void TickPlugins()
    {
        try { _table.Nav3D.BeginTick(); }
        catch (Exception error) { _log($"Nav3D begin failed: {error.Message}"); }
        try
        {
            foreach (RynthPlugin plugin in _plugins)
                plugin.Tick();
        }
        finally
        {
            // RynthCore commits Nav3D after TickAll.
            try { _table.Nav3D.CommitTick(); }
            catch (Exception error) { _log($"Nav3D commit failed: {error.Message}"); }
        }
    }

    public void Dispose() => Dispose(pumpExited: true);

    /// <summary>
    /// <paramref name="pumpExited"/> false: the pump thread outlived its shutdown and a pass may
    /// still be inside plugin code, so the plugins are not shut down under it and the API table
    /// they read (function pointers, string buffers) is left in place rather than freed.
    /// </summary>
    internal void Dispose(bool pumpExited)
    {
        if (!pumpExited)
            _log("a pump pass is still running: the plugins were not shut down and their API table is left in place.");
        foreach (RynthPlugin plugin in _plugins)
        {
            if (pumpExited)
                plugin.Shutdown();
            RynthCore.Engine.UI.ScriptWindows.ScriptWindowRegistry.DropOwner(plugin.Owner);
        }

        _plugins.Clear();
        try { _table.Holds.Release(); }
        catch (Exception error) { _log($"releasing held movement failed: {error.Message}"); }
        RynthCore.Engine.Compatibility.LoginLifecycleHooks.HasObservedLoginComplete = false;
        if (RynthCore.Engine.RynthLog.Sink == _log)
            RynthCore.Engine.RynthLog.Sink = null;
        if (RynthCore.Engine.ImGuiBackend.ImGuiPanelHost.Log == _log)
            RynthCore.Engine.ImGuiBackend.ImGuiPanelHost.Log = null;
        if (pumpExited)
            _table.Dispose();
        else
            _table.Abandon();
    }

    private void Load(Type exportsType)
    {
        RynthPluginExports exports = RynthPluginExports.FromType(exportsType);
        string name = exportsType.Namespace ?? exportsType.Name;
        if (!exports.TryGet("RynthPluginInit", out _))
        {
            _log($"{exportsType.FullName}: no RynthPluginInit export, skipped.");
            return;
        }

        var plugin = new RynthPlugin(name, exports);
        int result = plugin.Initialize(_table.Native);
        if (result != 0)
        {
            _log($"{name}: RynthPluginInit returned {result}, skipped.");
            return;
        }

        // RynthCore's dashboard shows a plugin's version from its loaded-plugin record.
        plugin.Owner.VersionString = plugin.CallStringExport("RynthPluginVersion") ?? string.Empty;
        plugin.ReportedName = plugin.CallStringExport("RynthPluginName") ?? string.Empty;
        _plugins.Add(plugin);
        _log($"{name} loaded.");
    }
}
