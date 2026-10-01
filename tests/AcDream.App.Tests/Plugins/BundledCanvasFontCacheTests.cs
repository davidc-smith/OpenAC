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

    [Fact]
    public void EverySizeSharesOneFace()
    {
        var backend = new PluginFontTableTests.FakeFontBackend();
        using var cache = new BundledCanvasFontCache(backend, BundledUiFont.ReadEmbeddedFontBytes, 2048);

        CanvasFont? sixteen = cache.Acquire(16f, out _);
        CanvasFont? twenty = cache.Acquire(20f, out _);

        Assert.Same(sixteen!.Face, twenty!.Face);
    }

    [Fact]
    public void ASizesSharperBakeIsMadeOnceForEveryHolderAndGoesWithTheLastRelease()
    {
        var backend = new PluginFontTableTests.FakeFontBackend();
        using var cache = new BundledCanvasFontCache(backend, BundledUiFont.ReadEmbeddedFontBytes, 2048);
        CanvasFont font = cache.Acquire(16f, out _)!;
        cache.Acquire(16f, out _);

        cache.PrepareScale(font, 2f, out string? shortfall);
        cache.PrepareScale(font, 2f, out _);

        Assert.Null(shortfall);
        Assert.Equal(2f, font.Sharp!.Scale);
        Assert.Equal(2, backend.Uploaded.Count);
        Assert.Equal((1024, 512), (backend.Uploaded[1].Width, backend.Uploaded[1].Height));
        cache.Release(font);
        Assert.Empty(backend.Released);
        cache.Release(font);
        Assert.Equal(
            [PluginFontTableTests.FakeFontBackend.FirstTexture + 1, PluginFontTableTests.FakeFontBackend.FirstTexture],
            backend.Released);
    }

    [Fact]
    public void DisposingGivesBackSharperBakesToo()
    {
        var backend = new PluginFontTableTests.FakeFontBackend();
        var cache = new BundledCanvasFontCache(backend, BundledUiFont.ReadEmbeddedFontBytes, 2048);
        CanvasFont font = cache.Acquire(16f, out _)!;
        cache.PrepareScale(font, 2f, out _);

        cache.Dispose();

        Assert.Equal(2, backend.Released.Count);
        Assert.Null(font.Face.Info);
    }
}
