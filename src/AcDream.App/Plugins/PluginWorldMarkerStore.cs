using AcDream.Plugin.Abstractions;

namespace AcDream.App.Plugins;

/// <summary>One icon as the overlay draws it, with the plugin it belongs to.</summary>
/// <param name="OwnerId">The plugin's id; its images are the only ones the icon's image is looked up in.</param>
/// <param name="Icon">The icon, already checked and with its size in range.</param>
internal readonly record struct WorldIconEntry(string OwnerId, PluginWorldIcon Icon);

/// <summary>
/// Every plugin's world markers. Each plugin gets one surface, the same until
/// it is disposed, and makes layers on it; the overlay reads one merged,
/// ordered snapshot. Everything is guarded by one lock: plugins set markers
/// on the tick thread, but a logoff can clear them from whichever thread
/// ends the session.
/// </summary>
public sealed class PluginWorldMarkerStore
{
    private readonly object _gate = new();
    private readonly SortedDictionary<string, Markers> _owners = new(StringComparer.Ordinal);
    private WorldIconEntry[]? _merged = [];

    /// <summary>
    /// One plugin's surface, the same object on every call until it is
    /// disposed; disposing it takes down every layer the plugin made.
    /// </summary>
    internal IPluginWorldMarkers For(string ownerId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ownerId);
        lock (_gate)
        {
            if (_owners.TryGetValue(ownerId, out Markers? existing))
                return existing;
            var markers = new Markers(this, ownerId);
            _owners.Add(ownerId, markers);
            return markers;
        }
    }

    /// <summary>
    /// Every icon of every plugin, by plugin id, then layer in the order the
    /// layers were made, then the order given. The same array until something
    /// changes, so a caller reading it every frame allocates nothing; the
    /// caller never writes to it.
    /// </summary>
    internal IReadOnlyList<WorldIconEntry> CaptureIcons()
    {
        lock (_gate)
        {
            if (_merged is { } merged)
                return merged;
            var icons = new List<WorldIconEntry>();
            foreach (Markers owner in _owners.Values)
            {
                foreach (Layer layer in owner.Layers)
                {
                    foreach (PluginWorldIcon icon in layer.Icons)
                        icons.Add(new WorldIconEntry(owner.OwnerId, icon));
                }
            }
            return _merged = icons.ToArray();
        }
    }

    /// <summary>
    /// Empties every layer of every plugin. The layers stay, so a plugin sets
    /// its markers again on its next stay in the world without asking for a
    /// new layer.
    /// </summary>
    public void Clear()
    {
        lock (_gate)
        {
            foreach (Markers owner in _owners.Values)
            {
                foreach (Layer layer in owner.Layers)
                    layer.Icons = [];
            }
            _merged = null;
        }
    }

    /// <summary>
    /// Clears the store whenever <paramref name="events"/> raises
    /// <see cref="IEvents.Logoff"/> -- once per stay, on every way out of the
    /// world -- until the returned subscription is disposed. Markers over
    /// objects mean nothing in the next session.
    /// </summary>
    public IDisposable ClearOn(IEvents events)
    {
        ArgumentNullException.ThrowIfNull(events);
        Action clear = Clear;
        events.Logoff += clear;
        return new Subscription(events, clear);
    }

    private sealed class Subscription(IEvents events, Action clear) : IDisposable
    {
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
                events.Logoff -= clear;
        }
    }

    private sealed class Markers(PluginWorldMarkerStore store, string ownerId)
        : IPluginWorldMarkers, IDisposable
    {
        private bool _disposed;

        internal string OwnerId => ownerId;

        internal List<Layer> Layers { get; } = [];

        public IPluginWorldMarkerLayer? CreateLayer()
        {
            lock (store._gate)
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                var layer = new Layer(store, this);
                Layers.Add(layer);
                return layer;
            }
        }

        internal int IconCountExcept(Layer except)
        {
            int count = 0;
            foreach (Layer layer in Layers)
            {
                if (!ReferenceEquals(layer, except))
                    count += layer.Icons.Length;
            }
            return count;
        }

        public void Dispose()
        {
            lock (store._gate)
            {
                if (_disposed)
                    return;
                _disposed = true;
                foreach (Layer layer in Layers)
                    layer.Detach();
                Layers.Clear();
                if (store._owners.TryGetValue(ownerId, out Markers? current)
                    && ReferenceEquals(current, this))
                {
                    store._owners.Remove(ownerId);
                }
                store._merged = null;
            }
        }
    }

    private sealed class Layer(PluginWorldMarkerStore store, Markers owner) : IPluginWorldMarkerLayer
    {
        private bool _disposed;

        internal PluginWorldIcon[] Icons { get; set; } = [];

        public bool SetIcons(IReadOnlyList<PluginWorldIcon> icons)
        {
            ArgumentNullException.ThrowIfNull(icons);
            // The plugin's list is read once, outside the lock, so a list that
            // throws or crawls cannot do so while other callers wait on it.
            // Count is read once: a set over the cap on its own can never be
            // accepted, so it is refused before anything is allocated.
            int count = icons.Count;
            if (count > IPluginWorldMarkers.MaximumIcons)
                return false;
            var given = new PluginWorldIcon[count];
            for (int i = 0; i < given.Length; i++)
                given[i] = icons[i];
            lock (store._gate)
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                if (given.Length + owner.IconCountExcept(this) > IPluginWorldMarkers.MaximumIcons)
                    return false;
                var accepted = new List<PluginWorldIcon>(given.Length);
                for (int i = 0; i < given.Length; i++)
                {
                    if (WorldMarkerRules.TryNormalizeIcon(given[i], out PluginWorldIcon icon))
                        accepted.Add(icon);
                }
                Icons = accepted.ToArray();
                store._merged = null;
                return true;
            }
        }

        /// <summary>Marks the layer gone and empties it; the caller removes it from its owner.</summary>
        internal void Detach()
        {
            _disposed = true;
            Icons = [];
        }

        public void Dispose()
        {
            lock (store._gate)
            {
                if (_disposed)
                    return;
                Detach();
                owner.Layers.Remove(this);
                store._merged = null;
            }
        }
    }
}
