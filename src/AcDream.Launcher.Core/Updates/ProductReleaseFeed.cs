using System.Xml;
using System.Xml.Linq;

namespace AcDream.Launcher.Core.Updates;

/// <summary>One release of this launcher's product found on the repo's releases feed.</summary>
public sealed record ProductFeedRelease(string Tag, LauncherVersion Version);

/// <summary>
/// Reads a repo's <c>releases.atom</c> feed for the newest release of one product: the highest
/// version among entries whose tag is the product's prefix (<c>gateway-v</c>,
/// <c>gateway-rynth-v</c>) followed by a SemVer version without build metadata. Both products
/// release from one repository, so GitHub's single "latest" release cannot serve either. A
/// <c>link</c> only counts as a direct child of an <c>entry</c> that names, on <c>github.com</c>
/// over https with no query or fragment, exactly <c>/&lt;owner&gt;/&lt;repo&gt;/releases/tag/&lt;tag&gt;</c>
/// for this repo. The feed lists the newest ten releases. Kept apart from upstream's
/// <c>PluginReleaseFeed</c> (prerelease plugin tags) so upstream syncs do not touch it.
/// </summary>
public static class ProductReleaseFeed
{
    private static readonly XNamespace AtomNamespace = "http://www.w3.org/2005/Atom";

    /// <summary>Ten entries with full release notes; the same allowance the plugin feed has.</summary>
    private const long MaxCharactersInDocument = 4_000_000;

    /// <summary>The newest release tagged with <paramref name="tagPrefix"/>, or null when the feed
    /// has none. Malformed XML, a DTD or a non-Atom root throws
    /// <see cref="LauncherUpdateException"/>.</summary>
    public static ProductFeedRelease? Newest(byte[] content, string repo, string tagPrefix)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentException.ThrowIfNullOrWhiteSpace(repo);
        ArgumentException.ThrowIfNullOrWhiteSpace(tagPrefix);

        XDocument document;
        var settings = new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            MaxCharactersInDocument = MaxCharactersInDocument,
        };
        try
        {
            using var stream = new MemoryStream(content);
            using XmlReader reader = XmlReader.Create(stream, settings);
            document = XDocument.Load(reader);
        }
        catch (XmlException ex)
        {
            throw new LauncherUpdateException("The releases feed is not well-formed XML.", ex);
        }

        XElement? feed = document.Root;
        if (feed is null || feed.Name != AtomNamespace + "feed")
        {
            throw new LauncherUpdateException("The releases feed has no Atom <feed> root.");
        }

        ProductFeedRelease? newest = null;
        foreach (XElement entry in feed.Elements(AtomNamespace + "entry"))
        {
            foreach (XElement link in entry.Elements(AtomNamespace + "link"))
            {
                if (TryMatchTag((string?)link.Attribute("href"), repo, tagPrefix, out ProductFeedRelease? candidate)
                    && (newest is null || candidate!.Version.CompareTo(newest.Version) > 0))
                {
                    newest = candidate;
                }
            }
        }

        return newest;
    }

    private static bool TryMatchTag(
        string? href, string repo, string tagPrefix, out ProductFeedRelease? release)
    {
        release = null;
        if (string.IsNullOrEmpty(href)
            || !Uri.TryCreate(href, UriKind.Absolute, out Uri? uri)
            || uri.Scheme != Uri.UriSchemeHttps
            || !string.Equals(uri.Host, "github.com", StringComparison.OrdinalIgnoreCase)
            || uri.Port != 443
            || !string.IsNullOrEmpty(uri.Query)
            || !string.IsNullOrEmpty(uri.Fragment))
        {
            return false;
        }

        string[] segments = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        string[] repoParts = repo.Split('/');
        if (segments.Length != 5
            || !string.Equals(segments[0], repoParts[0], StringComparison.OrdinalIgnoreCase)
            || !string.Equals(segments[1], repoParts[1], StringComparison.OrdinalIgnoreCase)
            || segments[2] != "releases"
            || segments[3] != "tag")
        {
            return false;
        }

        string tag = Uri.UnescapeDataString(segments[4]);
        if (!tag.StartsWith(tagPrefix, StringComparison.Ordinal))
        {
            return false;
        }

        string versionText = tag[tagPrefix.Length..];
        if (versionText.Contains('+')
            || !LauncherVersion.TryParse(versionText, out LauncherVersion? version))
        {
            return false;
        }

        release = new ProductFeedRelease(tag, version);
        return true;
    }
}
