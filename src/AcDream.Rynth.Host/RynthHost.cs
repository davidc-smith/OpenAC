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
    public RynthHost(IRynthGameView view, Action<string> log, IEnumerable<Type> exportsTypes)
    {
        ArgumentNullException.ThrowIfNull(exportsTypes);
        ArgumentNullException.ThrowIfNull(log);
        _log = line => log($"[Rynth] {line}");
        _table = new RynthApiTable(view, _log);
        foreach (Type exportsType in exportsTypes)
            Load(exportsType);
        Bridge = new RynthEventBridge(_plugins);
    }

    public IReadOnlyList<RynthPlugin> Plugins => _plugins;

    /// <summary>The runtime observer that feeds the plugins.</summary>
    public RynthEventBridge Bridge { get; }

    public void Tick()
    {
        foreach (RynthPlugin plugin in _plugins)
            plugin.Tick();
    }

    public void Dispose()
    {
        foreach (RynthPlugin plugin in _plugins)
            plugin.Shutdown();
        _plugins.Clear();
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

        _plugins.Add(plugin);
        _log($"{name} loaded.");
    }
}
