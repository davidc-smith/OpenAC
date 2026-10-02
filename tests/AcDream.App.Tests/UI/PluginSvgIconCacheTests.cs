using AcDream.App.Plugins;
using AcDream.App.UI;

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
        public List<int> UploadedSizes { get; } = [];
        public List<uint> Released { get; } = [];

        public uint UploadCoverage(byte[] coverage, int width, int height, string debugName)
        {
            if (Refuse) return 0;
            UploadedSizes.Add(width);
            return _next++;
        }

        public bool ReleaseCoverage(uint texture)
        {
            Released.Add(texture);
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
}
