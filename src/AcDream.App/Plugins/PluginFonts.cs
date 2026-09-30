using System.Diagnostics.CodeAnalysis;
using AcDream.App.UI;
using AcDream.Plugin.Abstractions;

namespace AcDream.App.Plugins;

/// <summary>
/// One plugin's font surface as the plugin sees it: the contract over its
/// <see cref="PluginFontTable"/>. Disposing it is how the plugin's fonts go
/// with the plugin -- the scoped registry tracks it -- and the registry that
/// made it forgets it, so a reloaded plugin starts with a fresh table.
/// </summary>
internal sealed class PluginFonts : IPluginFonts, IDisposable
{
    private readonly PluginFontTable _table;
    private readonly Action<PluginFonts> _forget;
    private bool _disposed;

    internal PluginFonts(PluginFontTable table, Action<PluginFonts> forget)
    {
        _table = table ?? throw new ArgumentNullException(nameof(table));
        _forget = forget ?? throw new ArgumentNullException(nameof(forget));
    }

    internal PluginFontTable Table => _table;

    public bool IsAvailable => !_disposed && _table.IsBound;

    public PluginFont Bundled(float pixelSize) =>
        _disposed ? PluginFont.None : _table.AcquireBundled(pixelSize);

    public PluginFont FromStream(string name, Func<Stream> open, float pixelSize, PluginFontOptions? options = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(open);
        return _disposed ? PluginFont.None : _table.AcquireStream(name, open, pixelSize, options?.Ranges);
    }

    public bool Release(PluginFont font) => !_disposed && _table.Release(font);

    public int Count => _disposed ? 0 : _table.Count;

    public int MaximumCount => _table.Budget.MaximumCount;

    public long MaximumBytes => _table.Budget.MaximumBytes;

    public int MaximumGlyphs => _table.Budget.MaximumGlyphs;

    public float MinimumPixelSize => _table.Budget.MinimumPixelSize;

    public float MaximumPixelSize => _table.Budget.MaximumPixelSize;

    /// <summary>A canvas is about to paint: fonts not already held are refused until <see cref="EndPaint"/>.</summary>
    internal void BeginPaint()
    {
        if (!_disposed)
            _table.IsPainting = true;
    }

    /// <summary>The canvas has finished painting, normally or not.</summary>
    internal void EndPaint() => _table.IsPainting = false;

    /// <summary>The baked font behind a handle, for the painter.</summary>
    internal bool TryResolve(PluginFont font, [NotNullWhen(true)] out CanvasFont? resolved)
    {
        if (!_disposed)
            return _table.TryResolve(font, out resolved);
        resolved = null;
        return false;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _table.Dispose();
        _forget(this);
    }
}
