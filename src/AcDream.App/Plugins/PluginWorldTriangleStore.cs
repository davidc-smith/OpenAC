using AcDream.Plugin.Abstractions;

namespace AcDream.App.Plugins;

/// <summary>
/// The filled triangles plugins put in the world, layer by layer. A layer's
/// set is copied when it is set and swapped whole, so the renderer always
/// reads a complete set.
/// </summary>
public sealed class PluginWorldTriangleStore : IPluginWorldTriangles
{
    /// <summary>The most triangles one layer keeps; the rest of a longer list is dropped.</summary>
    internal const int MaxTrianglesPerLayer = 65536;

    private readonly object _gate = new();
    private readonly List<Layer> _layers = [];
    private PluginWorldTriangle[][] _snapshot = [];

    /// <summary>Every layer's current set, as of the last change.</summary>
    internal IReadOnlyList<PluginWorldTriangle[]> Layers => Volatile.Read(ref _snapshot);

    public IPluginWorldTriangleLayer CreateLayer()
    {
        var layer = new Layer(this);
        lock (_gate)
            _layers.Add(layer);
        return layer;
    }

    private void Changed()
    {
        lock (_gate)
            Volatile.Write(ref _snapshot, _layers.Select(static layer => layer.Triangles).ToArray());
    }

    private sealed class Layer(PluginWorldTriangleStore owner) : IPluginWorldTriangleLayer
    {
        private bool _disposed;

        public PluginWorldTriangle[] Triangles { get; private set; } = [];

        public void SetTriangles(IReadOnlyList<PluginWorldTriangle> triangles)
        {
            ArgumentNullException.ThrowIfNull(triangles);
            ObjectDisposedException.ThrowIf(_disposed, this);
            Triangles = triangles.Take(MaxTrianglesPerLayer).ToArray();
            owner.Changed();
        }

        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;
            Triangles = [];
            lock (owner._gate)
                owner._layers.Remove(this);
            owner.Changed();
        }
    }
}
