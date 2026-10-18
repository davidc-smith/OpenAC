using AcDream.App.UI;
using AcDream.Tests.Fixtures.PluginIcons;

namespace AcDream.App.Tests.UI;

public sealed class PluginMarkupIconResolverTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"acdream-markup-icons-{Guid.NewGuid():N}");
    private readonly List<string> _reports = [];
    private int _uploads;
    private readonly PluginFileIconCache _cache;

    public PluginMarkupIconResolverTests()
    {
        Directory.CreateDirectory(Path.Combine(_root, "icons"));
        File.WriteAllBytes(Path.Combine(_root, "icons", "sword.png"), PngTestData.Sized(20, 10));
        _cache = new PluginFileIconCache((_, _, _, _) => (uint)(500 + ++_uploads), _ => true, _reports.Add);
    }

    public void Dispose()
    {
        _cache.Dispose();
        Directory.Delete(_root, recursive: true);
    }

    private sealed class Retail : IMarkupIconResolver
    {
        public (uint tex, int w, int h) ResolveDid(uint did) => (1u, 32, 32);

        public (uint tex, int w, int h) ResolveSpell(uint spellId) => (2u, 32, 32);

        public (uint tex, int w, int h) ResolveItem(uint objectId) => (3u, 32, 32);
    }

    private PluginMarkupIconResolver Resolver(string? directory) =>
        new(new Retail(), _cache, "acme.hello", directory);

    [Fact]
    public void ClientIconsGoToTheRetailResolver()
    {
        PluginMarkupIconResolver resolver = Resolver(_root);

        Assert.Equal(1u, resolver.ResolveDid(9u).tex);
        Assert.Equal(2u, resolver.ResolveSpell(9u).tex);
        Assert.Equal(3u, resolver.ResolveItem(9u).tex);
    }

    [Fact]
    public void AFileIsLoadedFromThePluginFolderOnceHoweverOftenItIsDrawn()
    {
        PluginMarkupIconResolver resolver = Resolver(_root);

        Assert.Equal((501u, 20, 10), resolver.ResolveFile("icons/sword.png"));
        Assert.Equal((501u, 20, 10), resolver.ResolveFile("icons/sword.png"));
        Assert.Equal(1, _uploads);
    }

    [Fact]
    public void AMissingFileIsLookedUpOnceAndReportedOnce()
    {
        PluginMarkupIconResolver resolver = Resolver(_root);

        Assert.Equal((0u, 0, 0), resolver.ResolveFile("icons/gone.png"));
        Directory.Delete(Path.Combine(_root, "icons"), recursive: true);
        Assert.Equal((0u, 0, 0), resolver.ResolveFile("icons/gone.png"));

        Assert.Contains("acme.hello/icons/gone.png", Assert.Single(_reports), StringComparison.Ordinal);
    }

    [Fact]
    public void AWindowWithNoPluginFolderDrawsNoFilesAndSaysWhyOnce()
    {
        PluginMarkupIconResolver resolver = Resolver(null);

        Assert.Equal((0u, 0, 0), resolver.ResolveFile("icons/sword.png"));
        Assert.Equal((0u, 0, 0), resolver.ResolveFile("icons/sword.png"));

        Assert.Contains("no plugin folder", Assert.Single(_reports), StringComparison.Ordinal);
        Assert.Equal(0, _uploads);
    }

    [Fact]
    public void AnImageGivenBackDrawsNothing()
    {
        PluginMarkupIconResolver resolver = Resolver(_root);
        resolver.ResolveFile("icons/sword.png");

        _cache.Dispose();

        Assert.Equal((0u, 0, 0), resolver.ResolveFile("icons/sword.png"));
    }
}
