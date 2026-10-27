using AcDream.Core.Targeting;
using AcDream.Plugin.Abstractions;

namespace AcDream.App.Plugins;

/// <summary>What every plugin added for one object, after the plate's budget.</summary>
public sealed record TargetPlatePluginContent(
    IReadOnlyList<TargetPluginRow> Rows, string? OverflowTooltip, IReadOnlyList<TargetPluginAction> Actions)
{
    public static TargetPlatePluginContent Empty { get; } = new(Array.Empty<TargetPluginRow>(), null, Array.Empty<TargetPluginAction>());
}

/// <summary>
/// Every plugin's Target Deck providers. Asks them once per object (until a
/// refresh), keeps each to its limits, shares the plate's two plugin lines in
/// registration order and keeps a throwing provider from breaking the plate.
/// </summary>
public sealed class PluginTargetPlateStore
{
    public const int PlateLines = 2;

    private readonly Func<bool> _available;
    private readonly Action<string, string> _warn;
    private readonly List<Registration> _registrations = new();
    private readonly Dictionary<string, Surface> _surfaces = new(StringComparer.Ordinal);
    private readonly HashSet<(Registration, uint)> _warned = new();
    // Which provider produced each action of the cached object, by (plugin id, action id).
    private readonly Dictionary<(string, string), Registration> _actionOwners = new();
    private uint _cachedObject;
    private TargetPlatePluginContent? _cached;

    /// <param name="available">True while the Target Deck is mounted (Modern HUD on).</param>
    /// <param name="warn">Writes one warning to a plugin's log: (plugin id, message).</param>
    public PluginTargetPlateStore(Func<bool> available, Action<string, string> warn)
    {
        _available = available ?? throw new ArgumentNullException(nameof(available));
        _warn = warn ?? throw new ArgumentNullException(nameof(warn));
    }

    public IPluginTargetPlate For(string pluginId, string displayName)
    {
        if (!_surfaces.TryGetValue(pluginId, out Surface? s))
            _surfaces[pluginId] = s = new Surface(this, pluginId, displayName);
        return s;
    }

    public void Invalidate(uint objectId)
    {
        if (_cachedObject == objectId)
            _cached = null;
    }

    /// <summary>Forgets every cached description and action route (a session reset).</summary>
    public void InvalidateAll()
    {
        _cached = null;
        _actionOwners.Clear();
    }

    public TargetPlatePluginContent Describe(PluginTargetPlateQuery query)
    {
        if (_cached is not null && _cachedObject == query.ObjectId)
            return _cached;
        var plate = new List<TargetPluginRow>();
        var overflow = new List<string>();
        var actions = new List<TargetPluginAction>();
        _actionOwners.Clear();
        var perProvider = new List<(Registration Reg, List<PluginTargetPlateRow> Rows)>();
        foreach (Registration reg in _registrations.ToArray())
        {
            PluginTargetPlateContent? content;
            try { content = reg.Provider.Describe(query); }
            catch (Exception error) { WarnOnce(reg, query.ObjectId, $"target plate provider threw {error.GetType().Name}: {error.Message}"); continue; }
            if (content is null)
                continue;
            IReadOnlyList<PluginTargetPlateRow> contentRows = content.Rows ?? Array.Empty<PluginTargetPlateRow>();
            IReadOnlyList<PluginTargetPlateAction> contentActions = content.Actions ?? Array.Empty<PluginTargetPlateAction>();
            var rows = contentRows.Take(PluginTargetPlateContent.MaximumRows).ToList();
            if (contentRows.Count > PluginTargetPlateContent.MaximumRows || contentActions.Count > PluginTargetPlateContent.MaximumActions)
                WarnOnce(reg, query.ObjectId, $"target plate provider returned more than {PluginTargetPlateContent.MaximumRows} rows or {PluginTargetPlateContent.MaximumActions} actions; the rest were dropped");
            perProvider.Add((reg, rows));
            foreach (PluginTargetPlateAction a in contentActions.Take(PluginTargetPlateContent.MaximumActions))
            {
                actions.Add(new(reg.Surface.Id, reg.Surface.Name, a.Id, a.Label, a.Enabled));
                _actionOwners.TryAdd((reg.Surface.Id, a.Id), reg);
            }
        }
        int withRows = perProvider.Count(p => p.Rows.Count > 0);
        // One line per provider in order, then the rest go to the tooltip.
        foreach (var (_, rows) in perProvider)
            if (rows.Count > 0 && plate.Count < PlateLines) { plate.Add(Map(rows[0])); rows.RemoveAt(0); }
        foreach (var (_, rows) in perProvider)
            foreach (PluginTargetPlateRow r in rows)
                if (plate.Count < PlateLines && withRows == 1) plate.Add(Map(r));
                else overflow.Add($"{r.Label}: {r.Value}");
        _cachedObject = query.ObjectId;
        _cached = new(plate, overflow.Count > 0 ? string.Join("\n", overflow) : null, actions);
        return _cached;
    }

    public void RunAction(string pluginId, uint objectId, string actionId)
    {
        if (!_actionOwners.TryGetValue((pluginId, actionId), out Registration? reg))
            return;
        try { reg.Provider.OnAction(objectId, actionId); }
        catch (Exception error) { _warn(pluginId, $"target plate action '{actionId}' threw {error.GetType().Name}: {error.Message}"); }
    }

    private static TargetPluginRow Map(PluginTargetPlateRow r) =>
        new(r.Label, r.Value, r.Color is { } c ? (uint)(c.R << 24 | c.G << 16 | c.B << 8 | c.A) : null, r.Tooltip);

    private void WarnOnce(Registration reg, uint objectId, string message)
    {
        if (_warned.Add((reg, objectId)))
            _warn(reg.Surface.Id, message);
    }

    private void Remove(Registration reg)
    {
        _registrations.Remove(reg);
        _warned.RemoveWhere(w => w.Item1 == reg);
        foreach (var key in _actionOwners.Where(o => o.Value == reg).Select(o => o.Key).ToArray())
            _actionOwners.Remove(key);
        _cached = null;
    }

    private sealed record Registration(Surface Surface, IPluginTargetPlateProvider Provider);

    private sealed class Surface(PluginTargetPlateStore store, string id, string name) : IPluginTargetPlate, IDisposable
    {
        public string Id => id;
        public string Name => name;
        public bool IsAvailable => store._available();

        public IDisposable AddProvider(IPluginTargetPlateProvider provider)
        {
            ArgumentNullException.ThrowIfNull(provider);
            if (!store._surfaces.TryGetValue(id, out Surface? live) || live != this)
                return NoRegistration.Instance;
            var reg = new Registration(this, provider);
            store._registrations.Add(reg);
            store._cached = null;
            return new Unregister(store, reg);
        }

        // The deck pulls each frame, so dropping the cache is enough.
        public void Refresh(uint objectId) => store.Invalidate(objectId);

        public void Dispose()
        {
            foreach (Registration reg in store._registrations.Where(r => r.Surface == this).ToArray())
                store.Remove(reg);
            store._surfaces.Remove(id);
        }
    }

    private sealed class NoRegistration : IDisposable
    {
        public static NoRegistration Instance { get; } = new();
        public void Dispose() { }
    }

    private sealed class Unregister(PluginTargetPlateStore store, Registration reg) : IDisposable
    {
        private bool _done;
        public void Dispose()
        {
            if (_done) return;
            _done = true;
            store.Remove(reg);
        }
    }
}
