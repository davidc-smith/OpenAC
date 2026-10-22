using System.Text;
using AcDream.Launcher.Core.Updates;

namespace AcDream.Launcher.Core.Tests.Updates;

public sealed class ProductReleaseFeedTests
{
    private const string Repo = "davidc-smith/OpenAC";

    [Fact]
    public void TheHighestOwnReleaseWinsWhateverTheFeedOrder()
    {
        ProductFeedRelease? release = Parse(
            Entry(Link("gateway-v0.1.26-gateway.9")),
            Entry(Link("gateway-v0.1.26-gateway.10")),
            Entry(Link("gateway-v0.1.26-gateway.2")));

        Assert.Equal("gateway-v0.1.26-gateway.10", release!.Tag);
        Assert.Equal("0.1.26-gateway.10", release.Version.Value);
    }

    /// <summary>Mutation: a plain StartsWith on "gateway" (no "-v") lets the Gateway launcher take a
    /// Rynth release; every case here must be ignored.</summary>
    [Theory]
    [InlineData("gateway-rynth-v0.1.27-rynth.1")]
    [InlineData("v0.1.27")]
    [InlineData("gateway-v0.1.27-gateway.1+meta")]
    [InlineData("gateway-vnot-a-version")]
    [InlineData("gateway-v")]
    public void OtherProductsAndMalformedTagsAreIgnored(string tag)
    {
        Assert.Null(Parse(Entry(Link(tag))));
    }

    [Fact]
    public void TheRynthPrefixTakesOnlyRynthReleases()
    {
        ProductFeedRelease? release = ParseWithPrefix(
            "gateway-rynth-v",
            Entry(Link("gateway-v0.1.27-gateway.1")),
            Entry(Link("gateway-rynth-v0.1.26-rynth.3")));

        Assert.Equal("gateway-rynth-v0.1.26-rynth.3", release!.Tag);
    }

    [Theory]
    [InlineData("https://github.com/someone-else/OpenAC/releases/tag/gateway-v0.1.27-gateway.1")]
    [InlineData("http://github.com/davidc-smith/OpenAC/releases/tag/gateway-v0.1.27-gateway.1")]
    [InlineData("https://github.com/davidc-smith/OpenAC/releases/tag/gateway-v0.1.27-gateway.1?x=1")]
    [InlineData("https://example.test/davidc-smith/OpenAC/releases/tag/gateway-v0.1.27-gateway.1")]
    public void LinksOutsideThisRepoOrHttpsAreIgnored(string href)
    {
        Assert.Null(Parse(Entry($"""<link rel="alternate" href="{href}"/>""")));
    }

    [Fact]
    public void AReleaseLinkInsideContentDoesNotCount()
    {
        Assert.Null(Parse(Entry(
            $"""<content type="html"><link href="https://github.com/{Repo}/releases/tag/gateway-v0.1.27-gateway.1"/></content>""")));
    }

    [Theory]
    [InlineData("<feed")]
    [InlineData("""<!DOCTYPE feed [<!ENTITY x "y">]><feed xmlns="http://www.w3.org/2005/Atom"/>""")]
    [InlineData("<other/>")]
    public void MalformedFeedsThrow(string xml)
    {
        Assert.Throws<LauncherUpdateException>(() =>
            ProductReleaseFeed.Newest(Encoding.UTF8.GetBytes(xml), Repo, "gateway-v"));
    }

    [Theory]
    [InlineData("davidc-smith")]
    [InlineData("davidc-smith/OpenAC/extra")]
    public void ARepoThatIsNotOwnerSlashNameIsRejected(string repo) =>
        Assert.Throws<ArgumentException>(() =>
            ProductReleaseFeed.Newest(Encoding.UTF8.GetBytes(Feed(Entry(Link("gateway-v1.0.0")))), repo, "gateway-v"));

    internal static string Feed(params string[] entries) =>
        $"""<?xml version="1.0" encoding="UTF-8"?><feed xmlns="http://www.w3.org/2005/Atom">{string.Concat(entries)}</feed>""";

    internal static string Entry(string body) => $"<entry>{body}</entry>";

    internal static string Link(string tag) =>
        $"""<link rel="alternate" type="text/html" href="https://github.com/{Repo}/releases/tag/{tag}"/>""";

    private static ProductFeedRelease? Parse(params string[] entries) => ParseWithPrefix("gateway-v", entries);

    /// <summary>Named apart from <see cref="Parse"/>: as an overload, a call with several entries
    /// would bind here and take the first entry as the prefix.</summary>
    private static ProductFeedRelease? ParseWithPrefix(string prefix, params string[] entries) =>
        ProductReleaseFeed.Newest(Encoding.UTF8.GetBytes(Feed(entries)), Repo, prefix);
}
