using AcDream.App.Plugins;
using AcDream.App.UI;

namespace AcDream.App.Tests.Plugins;

/// <summary>The bundled font is baked once per size for every plugin, and freed with its last holder.</summary>
public sealed class BundledCanvasFontCacheTests
{
    [Fact]
    public void OneBakePerSizeSharedUntilTheLastRelease()
    {
        var backend = new PluginFontTableTests.FakeFontBackend();
        using var cache = new BundledCanvasFontCache(backend, BundledUiFont.ReadEmbeddedFontBytes, 2048);

        CanvasFont? first = cache.Acquire(16f, out _);
        CanvasFont? second = cache.Acquire(16f, out _);

        Assert.NotNull(first);
        Assert.Same(first, second);
        Assert.Single(backend.Uploaded);
        cache.Release(first!);
        Assert.Empty(backend.Released);
        cache.Release(second!);
        Assert.Equal([PluginFontTableTests.FakeFontBackend.FirstTexture], backend.Released);
        Assert.Equal(0, cache.HeldCount);
    }

    [Fact]
    public void DisposingGivesBackWhatIsStillHeld()
    {
        var backend = new PluginFontTableTests.FakeFontBackend();
        var cache = new BundledCanvasFontCache(backend, BundledUiFont.ReadEmbeddedFontBytes, 2048);
        cache.Acquire(16f, out _);
        cache.Acquire(20f, out _);

        cache.Dispose();

        Assert.Equal(2, backend.Released.Count);
        Assert.Null(cache.Acquire(16f, out string? failure));
        Assert.NotNull(failure);
    }
}
