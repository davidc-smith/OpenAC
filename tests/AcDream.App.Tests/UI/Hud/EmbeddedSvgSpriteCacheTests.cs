using AcDream.App.Plugins;
using AcDream.App.UI.Hud;
using AcDream.Core.Plugins;

namespace AcDream.App.Tests.UI.Hud;

public sealed class EmbeddedSvgSpriteCacheTests
{
    private sealed class FakeBackend : IPluginFontBackend
    {
        public readonly List<uint> Uploaded = [];
        public readonly List<uint> Released = [];
        private uint _next = 100;
        public uint UploadCoverage(byte[] coverage, int width, int height, string debugName)
        {
            uint id = _next++;
            Uploaded.Add(id);
            return id;
        }
        public bool ReleaseCoverage(uint texture) { Released.Add(texture); return true; }
    }

    [Fact]
    public void EveryHudIconIsEmbeddedAndParses()
    {
        foreach (string name in HudIcons.All)
        {
            byte[]? bytes = EmbeddedSvgSpriteCache.LoadFromAssembly(name);
            Assert.True(bytes is not null, name);
            Assert.True(PluginSvgIcon.TryParse(bytes!, out _, out string? reason), name + ": " + reason);
        }
    }

    [Fact]
    public void UploadsOncePerSize_AndReleasesTheOldSize()
    {
        var backend = new FakeBackend();
        using var cache = new EmbeddedSvgSpriteCache(backend);

        uint a = cache.Texture(HudIcons.Melee, 32);
        uint again = cache.Texture(HudIcons.Melee, 32);
        Assert.NotEqual(0u, a);
        Assert.Equal(a, again);
        Assert.Single(backend.Uploaded);

        uint b = cache.Texture(HudIcons.Melee, 64);
        Assert.NotEqual(a, b);
        Assert.Equal([a], backend.Released);
    }

    [Fact]
    public void MissingIconReturnsZeroAndLogsOnce()
    {
        var log = new List<string>();
        using var cache = new EmbeddedSvgSpriteCache(new FakeBackend(), report: log.Add);

        Assert.Equal(0u, cache.Texture("no-such-icon", 32));
        Assert.Equal(0u, cache.Texture("no-such-icon", 32));
        Assert.Single(log);
    }

    [Fact]
    public void DisposeReleasesEverything()
    {
        var backend = new FakeBackend();
        var cache = new EmbeddedSvgSpriteCache(backend);
        cache.Texture(HudIcons.Melee, 32);
        cache.Texture(HudIcons.Magic, 32);

        cache.Dispose();

        Assert.Equal(backend.Uploaded.Order(), backend.Released.Order());
    }
}
