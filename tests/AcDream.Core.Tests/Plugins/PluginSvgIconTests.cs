using System.Text;
using AcDream.Core.Plugins;

namespace AcDream.Core.Tests.Plugins;

public sealed class PluginSvgIconTests
{
    private const string Loot = """
        <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 24 24" fill="none" stroke="currentColor"
             stroke-width="1.7" stroke-linecap="round" stroke-linejoin="round">
          <path d="M5.5 8.5h13l-1 11h-11l-1-11Z"/>
          <path d="M9 8.5V7a3 3 0 0 1 6 0v1.5"/>
          <path d="M9.5 13h5"/>
        </svg>
        """;

    private static SvgIconDocument Accept(string svg)
    {
        Assert.True(PluginSvgIcon.TryParse(Encoding.UTF8.GetBytes(svg), out SvgIconDocument? document, out string? reason),
            reason);
        return document;
    }

    private static string Reject(string svg)
    {
        Assert.False(PluginSvgIcon.TryParse(Encoding.UTF8.GetBytes(svg), out _, out string? reason));
        return reason;
    }

    private static string Wrap(string body, string attributes = "") =>
        $"""<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 24 24" {attributes}>{body}</svg>""";

    [Fact]
    public void AStrokedLucideIconParsesIntoStrokeLayers()
    {
        SvgIconDocument doc = Accept(Loot);
        Assert.Equal((0d, 0d, 24d, 24d), (doc.MinX, doc.MinY, doc.Width, doc.Height));
        Assert.Equal(3, doc.Layers.Count);
        Assert.All(doc.Layers, l =>
        {
            Assert.Equal(SvgPaintKind.Stroke, l.Kind);
            Assert.Equal(new SvgStrokeStyle(1.7, SvgLineCap.Round, SvgLineJoin.Round, 4), l.Stroke);
        });
    }

    [Fact]
    public void FillDefaultsToInkAndStrokeToNone()
    {
        SvgPaintLayer layer = Assert.Single(Accept(Wrap("""<circle cx="12" cy="12" r="5"/>""")).Layers);
        Assert.Equal(SvgPaintKind.Fill, layer.Kind);
    }

    [Fact]
    public void FillThenStrokeKeepDocumentOrder()
    {
        SvgIconDocument doc = Accept(Wrap("""<rect width="10" height="10" stroke="red"/><circle r="2"/>"""));
        Assert.Equal([SvgPaintKind.Fill, SvgPaintKind.Stroke, SvgPaintKind.Fill], doc.Layers.Select(l => l.Kind));
    }

    [Fact]
    public void StyleInheritsThroughGroupsAndTheStyleAttributeWins()
    {
        SvgIconDocument doc = Accept(Wrap("""
            <g stroke="#123" stroke-width="2" fill="none">
              <g style="stroke-linecap: square"><line x1="0" y1="0" x2="5" y2="5" stroke-width="9" style="stroke-width: 3"/></g>
            </g>
            """));
        SvgPaintLayer layer = Assert.Single(doc.Layers);
        Assert.Equal(3, layer.Stroke!.Width);
        Assert.Equal(SvgLineCap.Square, layer.Stroke.Cap);
    }

    [Fact]
    public void GroupOpacityMultipliesIntoEachChild()
    {
        SvgIconDocument doc = Accept(Wrap("""<g opacity="0.5"><path d="M0 0h4v4z" fill-opacity="0.5"/></g>"""));
        Assert.Equal(0.25, Assert.Single(doc.Layers).Opacity, 9);
    }

    [Fact]
    public void TransformsComposeFromTheRoot()
    {
        SvgIconDocument doc = Accept(Wrap("""<g transform="translate(10 0)"><rect width="1" height="1" transform="scale(2)"/></g>"""));
        SvgMatrix m = Assert.Single(doc.Layers).Transform;
        Assert.Equal(new SvgPoint(12, 2), m.Apply(new SvgPoint(1, 1)));
    }

    [Fact]
    public void RotateAboutAPointKeepsThatPointStill()
    {
        SvgIconDocument doc = Accept(Wrap("""<rect width="1" height="1" transform="rotate(90 12 12)"/>"""));
        SvgPoint p = Assert.Single(doc.Layers).Transform.Apply(new SvgPoint(12, 12));
        Assert.Equal(12, p.X, 9);
        Assert.Equal(12, p.Y, 9);
    }

    [Fact]
    public void WidthAndHeightStandInForAMissingViewBox()
    {
        SvgIconDocument doc = Accept("""<svg xmlns="http://www.w3.org/2000/svg" width="32px" height="16"><circle r="1"/></svg>""");
        Assert.Equal((32d, 16d), (doc.Width, doc.Height));
    }

    [Fact]
    public void TitleDescAndMetadataAreSkipped()
    {
        Accept(Wrap("""<title>Loot</title><desc>bag</desc><metadata><x/></metadata><defs/><path d="M0 0h1v1z"><title>t</title></path>"""));
    }

    [Fact]
    public void ANoNamespaceSvgIsAccepted()
    {
        Accept("""<svg viewBox="0 0 8 8"><rect width="8" height="8"/></svg>""");
    }

    [Theory]
    [InlineData("<use href=\"#a\"/>", "<use>")]
    [InlineData("<image href=\"a.png\"/>", "<image>")]
    [InlineData("<text>A</text>", "<text>")]
    [InlineData("<style>path{}</style>", "<style>")]
    [InlineData("<script>x</script>", "<script>")]
    [InlineData("<mask/>", "<mask>")]
    [InlineData("<clipPath/>", "<clipPath>")]
    [InlineData("<linearGradient/>", "<linearGradient>")]
    [InlineData("<filter/>", "<filter>")]
    [InlineData("<a><path d=\"M0 0h1v1z\"/></a>", "<a>")]
    [InlineData("<foreignObject/>", "<foreignObject>")]
    [InlineData("<svg/>", "<svg>")]
    public void UnsupportedElementsAreRejectedByName(string body, string named)
    {
        Assert.Contains(named, Reject(Wrap(body)), StringComparison.Ordinal);
    }

    [Fact]
    public void ADefsWithContentIsRejected()
    {
        Assert.Contains("<defs>", Reject(Wrap("""<defs><path id="a" d="M0 0h1v1z"/></defs>""")), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("fill=\"url(#g)\"", "paint server")]
    [InlineData("fill-rule=\"evenodd\"", "nonzero")]
    [InlineData("style=\"fill-rule:evenodd\"", "nonzero")]
    [InlineData("stroke-dasharray=\"2 2\" stroke=\"red\"", "stroke-dasharray")]
    [InlineData("display=\"none\"", "display")]
    [InlineData("mask=\"url(#m)\"", "'mask'")]
    [InlineData("filter=\"url(#f)\"", "'filter'")]
    [InlineData("style=\"mix-blend-mode:multiply\"", "mix-blend-mode")]
    [InlineData("transform=\"scale(0)\"", "collapses")]
    [InlineData("transform=\"spin(3)\"", "transform")]
    [InlineData("xmlns:xlink=\"http://www.w3.org/1999/xlink\" xlink:href=\"#a\"", "xlink:href")]
    public void UnsupportedAttributesAreRejected(string attribute, string named)
    {
        Assert.Contains(named, Reject(Wrap($"""<path d="M0 0h1v1z" {attribute}/>""")), StringComparison.Ordinal);
    }

    [Fact]
    public void ADtdIsRejected()
    {
        string svg = """<?xml version="1.0"?><!DOCTYPE svg [<!ENTITY x "boom">]><svg viewBox="0 0 1 1"><title>&x;</title></svg>""";
        Assert.Contains("DTD", Reject(svg), StringComparison.Ordinal);
    }

    [Fact]
    public void AFileOverTheSizeLimitIsRejected()
    {
        string svg = Wrap("<title>" + new string('a', PluginSvgIcon.MaximumBytes) + "</title>");
        Assert.Contains("16 KiB", Reject(svg), StringComparison.Ordinal);
    }

    [Fact]
    public void TooManyElementsAreRejected()
    {
        string body = string.Concat(Enumerable.Repeat("<g/>", PluginSvgIcon.MaximumElements));
        Assert.Contains("elements", Reject(Wrap(body)), StringComparison.Ordinal);
    }

    [Fact]
    public void NestingTooDeepIsRejected()
    {
        string body = string.Concat(Enumerable.Repeat("<g>", PluginSvgIcon.MaximumDepth))
            + string.Concat(Enumerable.Repeat("</g>", PluginSvgIcon.MaximumDepth));
        Assert.Contains("deep", Reject(Wrap(body)), StringComparison.Ordinal);
    }

    [Fact]
    public void TooManyPathCommandsAcrossTheFileAreRejected()
    {
        string d = "M0 0" + string.Concat(Enumerable.Repeat("h1", PluginSvgIcon.MaximumPathCommands / 2));
        Assert.Contains("path commands", Reject(Wrap($"""<path d="{d}"/><path d="{d}"/>""")), StringComparison.Ordinal);
    }

    [Fact]
    public void MalformedPathDataNamesTheOffset()
    {
        Assert.Contains("offset", Reject(Wrap("""<path d="M0 0 L1 x"/>""")), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("""<svg viewBox="0 0 0 24"/>""")]
    [InlineData("""<svg viewBox="0 0 24"/>""")]
    [InlineData("""<svg/>""")]
    [InlineData("""<html/>""")]
    public void ADocumentWithoutAUsableViewBoxIsRejected(string svg)
    {
        Reject(svg);
    }

    [Fact]
    public void NotXmlIsRejected()
    {
        Assert.Contains("XML", Reject("<svg"), StringComparison.Ordinal);
    }

    [Fact]
    public void EmptyShapesDrawNothing()
    {
        Assert.Empty(Accept(Wrap("""<circle r="0"/><rect width="0" height="4"/><path d=""/>""")).Layers);
    }

    [Fact]
    public void RectCornerRadiiFollowTheSvgRules()
    {
        SvgPaintLayer layer = Assert.Single(Accept(Wrap("""<rect width="10" height="4" rx="9"/>""")).Layers);
        SvgSubpath rect = Assert.Single(layer.Subpaths);
        // rx clamps to half the width, ry copies rx then clamps to half the height.
        Assert.Equal(new SvgPoint(5, 0), rect.Start);
        Assert.Equal(new SvgPoint(10, 2), rect.Segments[1].End);
    }

    [Fact]
    public void TryLoadReadsAFile()
    {
        using var directory = new TemporaryDirectory();
        string path = Path.Combine(directory.Path, PluginSvgIcon.FileName);
        File.WriteAllText(path, Loot);
        Assert.True(PluginSvgIcon.TryLoad(path, out SvgIconDocument? doc, out _));
        Assert.Equal(3, doc.Layers.Count);
    }

    [Fact]
    public void TryLoadRejectsAnOversizedFileWithoutReadingAllOfIt()
    {
        using var directory = new TemporaryDirectory();
        string path = Path.Combine(directory.Path, PluginSvgIcon.FileName);
        File.WriteAllBytes(path, new byte[PluginSvgIcon.MaximumBytes * 4]);
        Assert.False(PluginSvgIcon.TryLoad(path, out _, out string? reason));
        Assert.Contains("16 KiB", reason, StringComparison.Ordinal);
    }

    [Fact]
    public void ResolvePathAcceptsAFileInsideTheFolder()
    {
        using var directory = new TemporaryDirectory();
        Directory.CreateDirectory(Path.Combine(directory.Path, "icons"));
        File.WriteAllText(Path.Combine(directory.Path, "icons", "loot.svg"), Loot);
        Assert.True(PluginSvgIcon.TryResolvePath(directory.Path, @"icons\loot.svg", out string? full, out _));
        Assert.EndsWith("loot.svg", full, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("../outside.svg", "points outside")]
    [InlineData("icons/../../outside.svg", "points outside")]
    [InlineData(@"..\outside.svg", "points outside")]
    [InlineData("/etc/icon.svg", "points outside")]
    [InlineData("C:/icon.svg", "points outside")]
    [InlineData(@"\\server\x.svg", "points outside")]
    [InlineData("icons/loot.png", "not an .svg")]
    [InlineData("missing.svg", "does not exist")]
    public void ResolvePathRejectsAnythingElse(string iconFile, string named)
    {
        using var outer = new TemporaryDirectory();
        string folder = Path.Combine(outer.Path, "plugin");
        Directory.CreateDirectory(Path.Combine(folder, "icons"));
        File.WriteAllText(Path.Combine(outer.Path, "outside.svg"), Loot);
        File.WriteAllText(Path.Combine(folder, "icons", "loot.png"), "x");
        Assert.False(PluginSvgIcon.TryResolvePath(folder, iconFile, out _, out string? reason));
        Assert.Contains(named, reason, StringComparison.Ordinal);
    }

    private static bool TryLink(Action create)
    {
        try
        {
            create();
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
        {
            return false; // Symlinks need a privilege on some Windows machines.
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ResolvePathRejectsAFileLinkThroughADirectoryLinkOut(bool absoluteTarget)
    {
        using var outer = new TemporaryDirectory();
        string folder = Path.Combine(outer.Path, "plugin");
        string outside = Path.Combine(outer.Path, "outside");
        Directory.CreateDirectory(folder);
        Directory.CreateDirectory(outside);
        File.WriteAllText(Path.Combine(outside, "real.svg"), Loot);
        if (!TryLink(() => Directory.CreateSymbolicLink(Path.Combine(folder, "outdir"), "../outside"))) return;
        string target = absoluteTarget ? Path.Combine(folder, "outdir", "real.svg") : "outdir/real.svg";
        if (!TryLink(() => File.CreateSymbolicLink(Path.Combine(folder, "via.svg"), target))) return;

        Assert.False(PluginSvgIcon.TryResolvePath(folder, "via.svg", out _, out string? reason));
        Assert.Contains("outside", reason, StringComparison.Ordinal);
    }

    [Fact]
    public void ResolvePathRejectsAPathThroughADirectoryLinkOut()
    {
        using var outer = new TemporaryDirectory();
        string folder = Path.Combine(outer.Path, "plugin");
        string outside = Path.Combine(outer.Path, "outside");
        Directory.CreateDirectory(folder);
        Directory.CreateDirectory(outside);
        File.WriteAllText(Path.Combine(outside, "real.svg"), Loot);
        if (!TryLink(() => Directory.CreateSymbolicLink(Path.Combine(folder, "outdir"), "../outside"))) return;

        Assert.False(PluginSvgIcon.TryResolvePath(folder, "outdir/real.svg", out _, out string? reason));
        Assert.Contains("outside", reason, StringComparison.Ordinal);
    }

    [Fact]
    public void ResolvePathAcceptsALinkThatStaysInside()
    {
        using var directory = new TemporaryDirectory();
        Directory.CreateDirectory(Path.Combine(directory.Path, "real"));
        File.WriteAllText(Path.Combine(directory.Path, "real", "loot.svg"), Loot);
        if (!TryLink(() => File.CreateSymbolicLink(Path.Combine(directory.Path, "icon.svg"), "real/loot.svg"))) return;

        Assert.True(PluginSvgIcon.TryResolvePath(directory.Path, "icon.svg", out string? full, out string? reason), reason);
        Assert.EndsWith("loot.svg", full, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("""transform="translate(1e308) translate(1e308)" """)]
    [InlineData("""transform="matrix(1 0 0 1 1e999 0)" """)]
    [InlineData("""transform="scale(1e-7)" """)]
    public void ANonFiniteOrCollapsedTransformIsRejected(string attribute)
    {
        Assert.Contains("transform", Reject(Wrap($"""<path d="M0 0h1v1z" {attribute}/>""")), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("""<g transform="scale(1e150)"><g transform="scale(1e150)"><g transform="scale(1e150)"><path d="M0 0h1v1z"/></g></g></g>""")]
    [InlineData("""<g transform="translate(1e308)"><g transform="translate(1e308)"><path d="M0 0h1v1z"/></g></g>""")]
    [InlineData("""<g transform="scale(1e-5)"><g transform="scale(1e-5)"><g transform="scale(1e-5)"><path d="M0 0h1v1z"/></g></g></g>""")]
    public void ANonFiniteOrCollapsedComposedTransformIsRejected(string body)
    {
        Assert.Contains("transform", Reject(Wrap(body)), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("""<rect x="1e308" width="1e308" height="1"/>""")]
    [InlineData("""<circle cx="1e308" r="1e308"/>""")]
    [InlineData("""<ellipse cx="1e308" rx="1e308" ry="1"/>""")]
    public void NonFiniteShapeCoordinatesAreRejected(string body)
    {
        Assert.Contains("not finite", Reject(Wrap(body)), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("red")]
    [InlineData("#abc")]
    [InlineData("#abcd")]
    [InlineData("#aabbcc")]
    [InlineData("#aabbccdd")]
    [InlineData("rgb(1,2,3)")]
    [InlineData("rgba(1,2,3,0.5)")]
    [InlineData("hsl(1,2%,3%)")]
    [InlineData("currentColor")]
    [InlineData("none")]
    public void KnownPaintsAreAccepted(string paint)
    {
        Accept(Wrap($"""<path d="M0 0h1v1z" fill="{paint}"/>"""));
    }

    [Theory]
    [InlineData("bogus")]
    [InlineData("")]
    [InlineData("None")]
    [InlineData("#zzz")]
    [InlineData("#ab")]
    [InlineData("rgb(1,2,3")]
    public void UnknownPaintsAreRejected(string paint)
    {
        Assert.Contains("unknown paint", Reject(Wrap($"""<path d="M0 0h1v1z" fill="{paint}"/>""")), StringComparison.Ordinal);
    }

    [Fact]
    public void ElementsInsideSkippedContainersCountTowardTheLimit()
    {
        string body = "<metadata>" + string.Concat(Enumerable.Repeat("<x/>", 300)) + "</metadata>";
        Assert.Contains("elements", Reject(Wrap(body)), StringComparison.Ordinal);
    }

    [Fact]
    public void ADtdReasonDoesNotSuggestEnablingDtds()
    {
        string svg = """<?xml version="1.0"?><!DOCTYPE svg [<!ENTITY x "boom">]><svg viewBox="0 0 1 1"/>""";
        string reason = Reject(svg);
        Assert.Contains("DTD", reason, StringComparison.Ordinal);
        Assert.DoesNotContain("DtdProcessing", reason, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("")]
    [InlineData("a\0b")]
    public void TryLoadNeverThrowsOnABadPath(string path)
    {
        Assert.False(PluginSvgIcon.TryLoad(path, out _, out string? reason));
        Assert.NotNull(reason);
    }

    [Fact]
    public void ResolvePathRejectsALinkThatLeavesTheFolder()
    {
        using var outside = new TemporaryDirectory();
        using var directory = new TemporaryDirectory();
        string target = Path.Combine(outside.Path, "real.svg");
        File.WriteAllText(target, Loot);
        string link = Path.Combine(directory.Path, "icon.svg");
        try
        {
            File.CreateSymbolicLink(link, target);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return; // Symlinks need a privilege on some Windows machines.
        }

        Assert.False(PluginSvgIcon.TryResolvePath(directory.Path, "icon.svg", out _, out string? reason));
        Assert.Contains("outside", reason, StringComparison.Ordinal);
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        internal TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                $"acdream-plugin-svg-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path);
        }

        internal string Path { get; }

        public void Dispose()
        {
            if (Directory.Exists(Path))
                Directory.Delete(Path, recursive: true);
        }
    }
}
