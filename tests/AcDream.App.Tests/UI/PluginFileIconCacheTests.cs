using AcDream.App.UI;
using AcDream.Tests.Fixtures.PluginIcons;

namespace AcDream.App.Tests.UI;

public sealed class PluginFileIconCacheTests : IDisposable
{
    private const string Plugin = "acme.hello";

    private readonly string _root = Path.Combine(Path.GetTempPath(), $"acdream-file-icons-{Guid.NewGuid():N}");
    private readonly List<string> _reports = [];
    private readonly List<(int Width, int Height)> _uploads = [];
    private readonly List<uint> _released = [];
    private uint _next = 900;
    private bool _refuseUpload;
    private bool _throwOnUpload;
    private bool _throwOnRelease;

    public PluginFileIconCacheTests() => Directory.CreateDirectory(Path.Combine(_root, "icons"));

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private PluginFileIconCache Cache(int maximumPerPlugin = 256) =>
        new(Upload, Release, _reports.Add, maximumPerPlugin);

    private uint Upload(byte[] rgba, int width, int height, string debugName)
    {
        if (_throwOnUpload) throw new InvalidOperationException("gl exploded");
        if (_refuseUpload) return 0u;
        Assert.Equal(width * height * 4, rgba.Length);
        _uploads.Add((width, height));
        return _next++;
    }

    private bool Release(uint texture)
    {
        _released.Add(texture);
        if (_throwOnRelease) throw new InvalidOperationException("release exploded");
        return true;
    }

    private void Write(string relative, byte[] bytes) =>
        File.WriteAllBytes(Path.Combine(_root, relative), bytes);

    [Fact]
    public void AcquireDecodesAndUploadsOncePerFile()
    {
        Write("icons/sword.png", PngTestData.Sized(24, 16));
        using var cache = Cache();

        PluginFileIconCache.Entry? first = cache.Acquire(Plugin, _root, "icons/sword.png");
        PluginFileIconCache.Entry? second = cache.Acquire(Plugin, _root, @"icons\sword.png");

        Assert.NotNull(first);
        Assert.Same(first, second);
        Assert.Equal((900u, 24, 16), (first.Texture, first.Width, first.Height));
        Assert.Single(_uploads);
    }

    [Fact]
    public void AcquireLoadsAJpeg()
    {
        Write("icons/red.jpg", JpegTestData.Red());
        using var cache = Cache();

        PluginFileIconCache.Entry? entry = cache.Acquire(Plugin, _root, "icons/red.jpg");

        Assert.NotNull(entry);
        Assert.Equal((JpegTestData.Width, JpegTestData.Height), (entry.Width, entry.Height));
    }

    [Fact]
    public void AFileThatCannotBeUsedIsReportedOnceWithItsReason()
    {
        using var cache = Cache();

        Assert.Null(cache.Acquire(Plugin, _root, "icons/missing.png"));
        Assert.Null(cache.Acquire(Plugin, _root, "icons/missing.png"));

        string report = Assert.Single(_reports);
        Assert.Contains("acme.hello/icons/missing.png", report, StringComparison.Ordinal);
        Assert.Contains("does not exist", report, StringComparison.Ordinal);
    }

    [Fact]
    public void AnImageThatDoesNotDecodeIsReportedAndNotUploaded()
    {
        Write("icons/fake.png", "not an image"u8.ToArray());
        using var cache = Cache();

        Assert.Null(cache.Acquire(Plugin, _root, "icons/fake.png"));

        Assert.Empty(_uploads);
        Assert.Contains("not a PNG or JPEG", Assert.Single(_reports), StringComparison.Ordinal);
    }

    [Fact]
    public void AChangedFileIsLoadedAgainAndTheOldTextureGivenBack()
    {
        Write("icons/sword.png", PngTestData.Sized(8, 8));
        using var cache = Cache();
        PluginFileIconCache.Entry old = cache.Acquire(Plugin, _root, "icons/sword.png")!;

        Write("icons/sword.png", PngTestData.Sized(16, 16));
        File.SetLastWriteTimeUtc(Path.Combine(_root, "icons/sword.png"), DateTime.UtcNow.AddMinutes(1));
        PluginFileIconCache.Entry? fresh = cache.Acquire(Plugin, _root, "icons/sword.png");

        Assert.NotNull(fresh);
        Assert.NotSame(old, fresh);
        Assert.Equal(16, fresh.Width);
        Assert.Equal(0u, old.Texture);
        Assert.Equal([900u], _released);
    }

    [Fact]
    public void APluginPastItsLimitIsRefusedAndReportedOnce()
    {
        Write("icons/a.png", PngTestData.Sized(4, 4));
        Write("icons/b.png", PngTestData.Sized(4, 4));
        Write("icons/c.png", PngTestData.Sized(4, 4));
        using var cache = Cache(maximumPerPlugin: 2);

        Assert.NotNull(cache.Acquire(Plugin, _root, "icons/a.png"));
        Assert.NotNull(cache.Acquire(Plugin, _root, "icons/b.png"));
        Assert.Null(cache.Acquire(Plugin, _root, "icons/c.png"));
        Assert.Null(cache.Acquire(Plugin, _root, "icons/c.png"));
        Assert.NotNull(cache.Acquire("other.plugin", _root, "icons/c.png"));

        Assert.Contains("2 images", Assert.Single(_reports), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void AnUploadThatFailsIsReportedAndDrawsNothing(bool throws)
    {
        Write("icons/sword.png", PngTestData.Sized(4, 4));
        _throwOnUpload = throws;
        _refuseUpload = !throws;
        using var cache = Cache();

        Assert.Null(cache.Acquire(Plugin, _root, "icons/sword.png"));

        Assert.Contains("could not be uploaded", Assert.Single(_reports), StringComparison.Ordinal);
    }

    [Fact]
    public void DisposeGivesBackEveryTextureEvenWhenOneReleaseThrows()
    {
        Write("icons/a.png", PngTestData.Sized(4, 4));
        Write("icons/b.png", PngTestData.Sized(4, 4));
        var cache = Cache();
        PluginFileIconCache.Entry a = cache.Acquire(Plugin, _root, "icons/a.png")!;
        PluginFileIconCache.Entry b = cache.Acquire(Plugin, _root, "icons/b.png")!;
        _throwOnRelease = true;

        cache.Dispose();

        Assert.Equal([900u, 901u], _released.Order());
        Assert.Equal((0u, 0u), (a.Texture, b.Texture));
        Assert.Null(cache.Acquire(Plugin, _root, "icons/a.png"));
    }
}
