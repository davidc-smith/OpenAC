using AcDream.App.UI;

namespace AcDream.App.Plugins;

/// <summary>
/// The bundled font's bakes, shared by every plugin: one per size, uploaded
/// once, held while any plugin holds that size and given back on the last
/// release. Like client art, a shared bake costs no plugin anything against
/// its byte budget.
/// </summary>
internal sealed class BundledCanvasFontCache : IDisposable
{
    private readonly IPluginFontBackend _backend;
    private readonly Func<byte[]> _readFontBytes;
    private readonly int _maximumGlyphs;
    private readonly Dictionary<float, (CanvasFont Font, int Holds)> _bySize = [];
    private byte[]? _fontBytes;
    private bool _disposed;

    internal BundledCanvasFontCache(IPluginFontBackend backend, Func<byte[]> readFontBytes, int maximumGlyphs)
    {
        _backend = backend ?? throw new ArgumentNullException(nameof(backend));
        _readFontBytes = readFontBytes ?? throw new ArgumentNullException(nameof(readFontBytes));
        _maximumGlyphs = maximumGlyphs;
    }

    /// <summary>How many sizes are baked and held.</summary>
    internal int HeldCount => _bySize.Count;

    /// <summary>The bake for a size, made on the first request. Null, with a reason, when it cannot be made.</summary>
    internal CanvasFont? Acquire(float pixelSize, out string? failure)
    {
        if (_disposed)
        {
            failure = "the interface is going away";
            return null;
        }
        if (_bySize.TryGetValue(pixelSize, out (CanvasFont Font, int Holds) held))
        {
            _bySize[pixelSize] = (held.Font, held.Holds + 1);
            failure = null;
            return held.Font;
        }
        _fontBytes ??= _readFontBytes();
        if (!CanvasFontBaker.TryBake(
                _fontBytes, pixelSize, CanvasFontBaker.DefaultRanges, _maximumGlyphs,
                out CanvasFontBake? bake, out failure))
            return null;
        uint texture = _backend.UploadCoverage(
            bake.Coverage, bake.AtlasWidth, bake.AtlasHeight, $"plugin-font-bundled-{pixelSize}px");
        var font = new CanvasFont(bake, texture, _fontBytes);
        _bySize.Add(pixelSize, (font, 1));
        return font;
    }

    /// <summary>Lets go of one hold; the last one gives the texture back.</summary>
    internal void Release(CanvasFont font)
    {
        ArgumentNullException.ThrowIfNull(font);
        if (!_bySize.TryGetValue(font.PixelSize, out (CanvasFont Font, int Holds) held)
            || !ReferenceEquals(held.Font, font))
            return;
        if (held.Holds > 1)
        {
            _bySize[font.PixelSize] = (held.Font, held.Holds - 1);
            return;
        }
        _bySize.Remove(font.PixelSize);
        _backend.ReleaseCoverage(font.AtlasTexture);
        font.Dispose();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        foreach ((CanvasFont font, int _) in _bySize.Values)
        {
            _backend.ReleaseCoverage(font.AtlasTexture);
            font.Dispose();
        }
        _bySize.Clear();
    }
}
