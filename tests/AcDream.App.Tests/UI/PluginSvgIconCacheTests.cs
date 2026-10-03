using AcDream.App.Plugins;
using AcDream.App.UI;
using AcDream.Core.Plugins;

namespace AcDream.App.Tests.UI;

public sealed class PluginSvgIconCacheTests : IDisposable
{
    private const string Icon = """<svg viewBox="0 0 24 24"><circle cx="12" cy="12" r="8"/></svg>""";

    private readonly string _root = Path.Combine(Path.GetTempPath(), $"acdream-svg-cache-{Guid.NewGuid():N}");
    private readonly Backend _backend = new();
    private readonly List<string> _reports = [];

    public PluginSvgIconCacheTests() => Directory.CreateDirectory(_root);

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private sealed class Backend : IPluginFontBackend
    {
        private uint _next = 700;
        public bool Refuse { get; set; }
        public bool Throw { get; set; }
        public bool ThrowOnRelease { get; set; }
        public int UploadCalls { get; private set; }
        public List<int> UploadedSizes { get; } = [];
        public List<uint> Released { get; } = [];

        public uint UploadCoverage(byte[] coverage, int width, int height, string debugName)
        {
            UploadCalls++;
            if (Throw) throw new InvalidOperationException("gl exploded");
            if (Refuse) return 0;
            UploadedSizes.Add(width);
            return _next++;
        }

        public bool ReleaseCoverage(uint texture)
        {
            Released.Add(texture);
            if (ThrowOnRelease) throw new InvalidOperationException("release exploded " + texture);
            return true;
        }
    }

    private PluginSvgIconCache Cache() => new(_backend, _reports.Add);

    private string Write(string name, string content = Icon)
    {
        string path = Path.Combine(_root, name);
        File.WriteAllText(path, content);
        return path;
    }

    private SvgIconDocument Doc()
    {
        Assert.True(PluginSvgIcon.TryLoad(Write("doc.svg"), out SvgIconDocument? document, out _));
        return document!;
    }

    [Fact]
    public void AnUploadThatThrowsFailsTheIconInsteadOfEscaping()
    {
        using PluginSvgIconCache cache = Cache();
        PluginSvgIconCache.PluginSvgIconEntry entry = cache.Acquire("p/icon.svg", Write("icon.svg"))!;
        _backend.Throw = true;
        Assert.Equal(0u, entry.TextureFor(24));
        Assert.True(entry.Failed);
        Assert.Equal(0u, entry.TextureFor(24));
        Assert.Equal(1, _backend.UploadCalls);
        string report = Assert.Single(_reports);
        Assert.Contains("could not be uploaded: gl exploded", report);
    }

    [Fact]
    public void ABakeThatThrowsFailsTheIconInsteadOfEscaping()
    {
        int bakes = 0;
        using PluginSvgIconCache cache = new(_backend, _reports.Add, (_, _) => { bakes++; throw new InvalidOperationException("bad bake"); });
        PluginSvgIconCache.PluginSvgIconEntry entry = cache.Acquire("p/icon.svg", Write("icon.svg"))!;
        Assert.Equal(0u, entry.TextureFor(24));
        Assert.Equal(0u, entry.TextureFor(24));
        Assert.Equal(1, bakes);
        string report = Assert.Single(_reports);
        Assert.Contains("could not be drawn at this size: bad bake", report);
    }

    [Fact]
    public void EachSizeIsBakedOnce()
    {
        using PluginSvgIconCache cache = Cache();
        PluginSvgIconCache.PluginSvgIconEntry entry = cache.Acquire("p/icon.svg", Write("icon.svg"))!;
        uint first = entry.TextureFor(48);
        Assert.Equal(first, entry.TextureFor(48));
        Assert.Equal([48], _backend.UploadedSizes);
    }

    [Fact]
    public void TwoButtonsShowingOneFileShareOneEntry()
    {
        using PluginSvgIconCache cache = Cache();
        string path = Write("icon.svg");
        Assert.Same(cache.Acquire("p/icon.svg", path), cache.Acquire("p/icon.svg", path));
        Assert.Equal(1, cache.EntryCount);
    }

    [Fact]
    public void AThirdSizeReleasesTheLeastRecentlyDrawn()
    {
        using PluginSvgIconCache cache = Cache();
        PluginSvgIconCache.PluginSvgIconEntry entry = cache.Acquire("p/icon.svg", Write("icon.svg"))!;
        uint at24 = entry.TextureFor(24);
        uint at48 = entry.TextureFor(48);
        entry.TextureFor(24);
        entry.TextureFor(36);
        Assert.Equal([at48], _backend.Released);
        Assert.Equal(2, entry.BakedSizes);
        Assert.Equal(at24, entry.TextureFor(24));
    }

    [Fact]
    public void TheLastHolderGivesEveryTextureBack()
    {
        using PluginSvgIconCache cache = Cache();
        string path = Write("icon.svg");
        PluginSvgIconCache.PluginSvgIconEntry entry = cache.Acquire("p/icon.svg", path)!;
        cache.Acquire("p/icon.svg", path);
        uint a = entry.TextureFor(24), b = entry.TextureFor(48);

        entry.Release();
        Assert.Empty(_backend.Released);
        entry.Release();
        Assert.Equal([a, b], _backend.Released.Order());
        Assert.Equal(0, cache.EntryCount);
    }

    [Fact]
    public void AChangedFileLoadsAgain()
    {
        using PluginSvgIconCache cache = Cache();
        string path = Write("icon.svg");
        PluginSvgIconCache.PluginSvgIconEntry before = cache.Acquire("p/icon.svg", path)!;
        File.WriteAllText(path, Icon.Replace("r=\"8\"", "r=\"10\"", StringComparison.Ordinal));
        File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddMinutes(1));
        Assert.NotSame(before, cache.Acquire("p/icon.svg", path));
    }

    [Fact]
    public void AFailedUploadFallsBackAndIsNotRetried()
    {
        using PluginSvgIconCache cache = Cache();
        PluginSvgIconCache.PluginSvgIconEntry entry = cache.Acquire("p/icon.svg", Write("icon.svg"))!;
        _backend.Refuse = true;
        Assert.Equal(0u, entry.TextureFor(24));
        _backend.Refuse = false;
        Assert.Equal(0u, entry.TextureFor(24));
        Assert.True(entry.Failed);
        Assert.Empty(_backend.UploadedSizes);
        Assert.Single(_reports, r => r.Contains("could not be uploaded", StringComparison.Ordinal));
    }

    [Fact]
    public void ARejectedFileIsLoggedOnceAndGivesNull()
    {
        using PluginSvgIconCache cache = Cache();
        string path = Write("icon.svg", """<svg viewBox="0 0 1 1"><mask/></svg>""");
        Assert.Null(cache.Acquire("acdream.mosstank/icon.svg", path));
        Assert.Null(cache.Acquire("acdream.mosstank/icon.svg", path));
        string report = Assert.Single(_reports);
        Assert.Equal("[UI] plugin icon 'acdream.mosstank/icon.svg' ignored: uses <mask>, which plugin icons do not support", report);
    }

    [Theory]
    [InlineData(24f, 1f, 24)]
    [InlineData(24f, 1.5f, 36)]
    [InlineData(24f, 2f, 48)]
    [InlineData(22f, 1.25f, 28)]
    [InlineData(100f, 4f, 128)]
    public void DevicePixelsRoundUpAndCap(float extent, float scale, int expected)
    {
        Assert.Equal(expected, PluginSvgIconCache.DevicePixels(extent, scale));
    }

    [Theory]
    [InlineData(float.NaN)]
    [InlineData(0f)]
    [InlineData(-2f)]
    [InlineData(float.NegativeInfinity)]
    public void ANonPositiveOrNonFinitePixelScaleGivesOnePixelNotAnException(float scale)
    {
        Assert.Equal(1, PluginSvgIconCache.DevicePixels(24f, scale));
        Assert.Equal(1, PluginSvgIconCache.DevicePixels(float.NaN, 1f));
                Assert.Equal(128, PluginSvgIconCache.DevicePixels(24f, float.PositiveInfinity));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    [InlineData(129)]
    [InlineData(int.MaxValue)]
    public void AnOutOfRangeSizeIsClampedNotThrown(int size)
    {
        using PluginSvgIconCache cache = Cache();
        PluginSvgIconCache.PluginSvgIconEntry entry = cache.Acquire("p/icon.svg", Write("icon.svg"))!;
        Assert.NotEqual(0u, entry.TextureFor(size));
        Assert.Equal(Math.Clamp(size, 1, 128), Assert.Single(_backend.UploadedSizes));
    }

    [Fact]
    public void ABakeThatReturnsNullFailsTheIconAndIsNotRetried()
    {
        int bakes = 0;
        using var cache = new PluginSvgIconCache(_backend, _reports.Add, (_, _) => { bakes++; return null; });
        PluginSvgIconCache.PluginSvgIconEntry entry = cache.Acquire("p/icon.svg", Write("icon.svg"))!;
        Assert.Equal(0u, entry.TextureFor(24));
        Assert.Equal(0u, entry.TextureFor(24));
        Assert.Equal(0u, entry.TextureFor(48));
        Assert.True(entry.Failed);
        Assert.Equal(1, bakes);
        Assert.Empty(_backend.UploadedSizes);
        string report = Assert.Single(_reports);
        Assert.Equal("[UI] plugin icon 'p/icon.svg' ignored: could not be drawn at this size", report);
    }

    [Fact]
    public void DisposeReleasesEverything()
    {
        PluginSvgIconCache cache = Cache();
        PluginSvgIconCache.PluginSvgIconEntry entry = cache.Acquire("p/icon.svg", Write("icon.svg"))!;
        uint t = entry.TextureFor(24);
        cache.Dispose();
        Assert.Equal([t], _backend.Released);
    }

    [Fact]
    public void ABlankBakeFallsBackWithoutUploadingAndIsNotRetried()
    {
        int bakes = 0;
        using var cache = new PluginSvgIconCache(_backend, _reports.Add, (_, size) => { bakes++; return new byte[size * size]; });
        PluginSvgIconCache.PluginSvgIconEntry entry = cache.Acquire("p/icon.svg", Write("icon.svg"))!;
        Assert.Equal(0u, entry.TextureFor(24));
        Assert.Equal(0u, entry.TextureFor(24));
        Assert.True(entry.Failed);
        Assert.Equal(1, bakes);
        Assert.Equal(0, _backend.UploadCalls);
        string report = Assert.Single(_reports);
        Assert.Contains("draws nothing", report);
    }

    [Fact]
    public void ABlankBakeReleasesTheOtherSizes()
    {
        using var cache = new PluginSvgIconCache(_backend, _reports.Add, (_, size) => size == 36 ? new byte[size * size] : SvgIconRasterizer.Bake(Doc(), size));
        PluginSvgIconCache.PluginSvgIconEntry entry = cache.Acquire("p/icon.svg", Write("icon.svg"))!;
        uint at24 = entry.TextureFor(24);
        Assert.NotEqual(0u, at24);
        Assert.Equal(0u, entry.TextureFor(36));
        Assert.Equal([at24], _backend.Released);
        Assert.Equal(0, entry.BakedSizes);
    }

    [Fact]
    public void ADocumentThatBakesToNothingFallsBack()
    {
        using PluginSvgIconCache cache = Cache();
        string path = Write("blank.svg", """<svg viewBox="0 0 24 24"><path d="M0 0h5"/></svg>""");
        PluginSvgIconCache.PluginSvgIconEntry? entry = cache.Acquire("p/blank.svg", path);
        Assert.NotNull(entry);
        Assert.Equal(0u, entry.TextureFor(24));
        Assert.Equal(0, _backend.UploadCalls);
        Assert.Contains("draws nothing", Assert.Single(_reports));
    }

    [Fact]
    public void ALoneMovetoStrokeFallsBack()
    {
        using PluginSvgIconCache cache = Cache();
        string path = Write("dot.svg", """<svg viewBox="0 0 24 24"><path d="M12 12" fill="none" stroke="black" stroke-linecap="round"/></svg>""");
        PluginSvgIconCache.PluginSvgIconEntry? entry = cache.Acquire("p/dot.svg", path);
        Assert.NotNull(entry);
        Assert.Equal(0u, entry.TextureFor(24));
        Assert.Equal(0, _backend.UploadCalls);
        Assert.Contains("draws nothing", Assert.Single(_reports));
    }

    [Fact]
    public void AReleaseThatThrowsDuringEvictionDoesNotEscape()
    {
        using PluginSvgIconCache cache = Cache();
        PluginSvgIconCache.PluginSvgIconEntry entry = cache.Acquire("p/icon.svg", Write("icon.svg"))!;
        uint at24 = entry.TextureFor(24);
        entry.TextureFor(48);
        entry.TextureFor(24);
        _backend.ThrowOnRelease = true;
        uint at36 = entry.TextureFor(36);
        Assert.NotEqual(0u, at36);
        Assert.Equal(2, entry.BakedSizes);
        Assert.Equal(at24, entry.TextureFor(24));
        Assert.Equal(at36, entry.TextureFor(36));
        Assert.False(entry.Failed);
        Assert.Single(_backend.Released);
        string report = Assert.Single(_reports);
        Assert.Contains("could not release a texture: release exploded", report);
    }

    [Fact]
    public void AReleaseThatThrowsForTheLastHolderDoesNotEscape()
    {
        using PluginSvgIconCache cache = Cache();
        PluginSvgIconCache.PluginSvgIconEntry entry = cache.Acquire("p/icon.svg", Write("icon.svg"))!;
        entry.TextureFor(24);
        _backend.ThrowOnRelease = true;
        entry.Release();
        Assert.Equal(0, cache.EntryCount);
        Assert.Equal(0, entry.BakedSizes);
        Assert.Single(_reports, r => r.Contains("could not release a texture", StringComparison.Ordinal));
    }

    [Fact]
    public void DisposeReleasesEveryTextureEvenWhenOneThrows()
    {
        PluginSvgIconCache cache = Cache();
        cache.Acquire("p/a.svg", Write("a.svg"))!.TextureFor(24);
        cache.Acquire("p/b.svg", Write("b.svg", Icon.Replace("r=\"8\"", "r=\"9\"", StringComparison.Ordinal)))!.TextureFor(24);
        _backend.ThrowOnRelease = true;
        cache.Dispose();
        Assert.Equal(2, _backend.Released.Count);
        Assert.Equal(2, _reports.Count(r => r.Contains("could not release a texture", StringComparison.Ordinal)));
    }
}
