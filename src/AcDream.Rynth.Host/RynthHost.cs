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

    /// <summary>Delivers the frame's queued events, then ticks each plugin inside one Nav3D frame, as RynthCore's frame does.</summary>
    public void Tick()
    {
        Bridge.DeliverQueued();
        try { _table.Vendor.Tick(); }
        catch (Exception error) { _log($"vendor tick failed: {error.Message}"); }
        // A view fault here must not cost the plugins their tick.
        try { _table.Holds.Sync(); }
        catch (Exception error) { _log($"holding movement failed: {error.Message}"); }
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

    public void Dispose()
    {
        foreach (RynthPlugin plugin in _plugins)
        {
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
        _table.Dispose();
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
        _plugins.Add(plugin);
        _log($"{name} loaded.");
    }
}
