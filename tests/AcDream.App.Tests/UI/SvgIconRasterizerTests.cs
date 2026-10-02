using System.Runtime.CompilerServices;
using System.Text;
using AcDream.App.UI;
using AcDream.Core.Plugins;

namespace AcDream.App.Tests.UI;

public sealed class SvgIconRasterizerTests
{
    private const string GoldenFile = "SvgIconRasterizerTests.x24.pgm";

    private static SvgIconDocument Parse(string body, string viewBox = "0 0 16 16")
    {
        string svg = $"""<svg xmlns="http://www.w3.org/2000/svg" viewBox="{viewBox}">{body}</svg>""";
        Assert.True(PluginSvgIcon.TryParse(Encoding.UTF8.GetBytes(svg), out SvgIconDocument? doc, out string? reason), reason);
        return doc;
    }

    private static byte At(byte[] coverage, int size, int x, int y) => coverage[y * size + x];

    [Fact]
    public void AFilledSquareOnPixelEdgesIsSolidInsideAndEmptyOutside()
    {
        byte[] c = SvgIconRasterizer.Bake(Parse("""<rect x="4" y="4" width="8" height="8"/>"""), 16)!;
        for (int y = 0; y < 16; y++)
        for (int x = 0; x < 16; x++)
        {
            bool inside = x is >= 4 and < 12 && y is >= 4 and < 12;
            Assert.Equal(inside ? 255 : 0, At(c, 16, x, y));
        }
    }

    [Fact]
    public void AHalfPixelEdgeIsHalfCovered()
    {
        byte[] c = SvgIconRasterizer.Bake(Parse("""<rect x="4.5" y="4" width="8" height="8"/>"""), 16)!;
        Assert.InRange(At(c, 16, 4, 8), 126, 129);
        Assert.Equal(255, At(c, 16, 5, 8));
        Assert.InRange(At(c, 16, 12, 8), 126, 129);
    }

    [Fact]
    public void AnOppositeWindingInnerContourIsAHole()
    {
        byte[] c = SvgIconRasterizer.Bake(Parse("""<path d="M2 2H14V14H2Z M6 6V10H10V6Z"/>"""), 16)!;
        Assert.Equal(255, At(c, 16, 3, 8));
        Assert.Equal(0, At(c, 16, 8, 8));
    }

    [Fact]
    public void AStrokeAcrossAFillsHoleIsStillDrawn()
    {
        byte[] c = SvgIconRasterizer.Bake(Parse("""
            <path d="M2 2H14V14H2Z M6 6V10H10V6Z"/>
            <line x1="0" y1="8.5" x2="16" y2="8.5" stroke="black" stroke-width="1"/>
            """), 16)!;
        Assert.Equal(255, At(c, 16, 8, 8));
        Assert.Equal(0, At(c, 16, 8, 6));
    }

    [Fact]
    public void OpacityScalesCoverage()
    {
        byte[] c = SvgIconRasterizer.Bake(Parse("""<rect width="16" height="16" fill-opacity="0.5"/>"""), 16)!;
        Assert.InRange(At(c, 16, 8, 8), 127, 128);
    }

    [Fact]
    public void LayersCompositeSourceOver()
    {
        byte[] c = SvgIconRasterizer.Bake(Parse("""
            <rect width="16" height="16" fill-opacity="0.5"/><rect width="16" height="16" fill-opacity="0.5"/>
            """), 16)!;
        Assert.InRange(At(c, 16, 8, 8), 190, 192);
    }

    [Fact]
    public void ANonSquareViewBoxIsCentred()
    {
        byte[] c = SvgIconRasterizer.Bake(Parse("""<rect width="32" height="16"/>""", viewBox: "0 0 32 16"), 16)!;
        Assert.Equal(0, At(c, 16, 8, 3));
        Assert.Equal(255, At(c, 16, 8, 4));
        Assert.Equal(255, At(c, 16, 8, 11));
        Assert.Equal(0, At(c, 16, 8, 12));
    }

    [Fact]
    public void TheSameIconCoversTheSameShareAtEverySize()
    {
        SvgIconDocument doc = Parse("""<circle cx="8" cy="8" r="6" fill="none" stroke="black" stroke-width="1.5"/>""");
        double Share(int size) => SvgIconRasterizer.Bake(doc, size)!.Sum(b => b / 255.0) / (size * size);
        double reference = Share(48);
        Assert.InRange(Share(16), reference * 0.93, reference * 1.07);
        Assert.InRange(Share(24), reference * 0.95, reference * 1.05);
    }

    [Fact]
    public void TooManyPointsFailsTheBake()
    {
        SvgIconDocument doc = Parse("""<circle cx="8" cy="8" r="6" fill="none" stroke="black"/>""");
        var heavy = doc with { Layers = Enumerable.Repeat(doc.Layers[0], 2000).ToArray() };
        Assert.Null(SvgIconRasterizer.Bake(heavy, 128));
    }

    [Fact]
    public void ADeviceSpaceOverflowFailsTheBake()
    {
        SvgIconDocument doc = Parse("""<path d="M0 0h1e308v1e308z" transform="scale(10)"/>""");
        Assert.Null(SvgIconRasterizer.Bake(doc, 16));
    }

    [Fact]
    public void AStrokeOverTheRemainingBudgetFailsTheBake()
    {
        var segments = new List<SvgSegment>();
        for (int i = 1; i <= 40_000; i++) segments.Add(SvgSegment.Line(new SvgPoint(i % 2 == 0 ? 1 : 15, 1 + i % 13)));
        var layer = new SvgPaintLayer(SvgPaintKind.Stroke, 1, [new SvgSubpath(new SvgPoint(1, 1), segments, false)],
            new SvgStrokeStyle(1, SvgLineCap.Butt, SvgLineJoin.Miter, 4), SvgMatrix.Identity);
        var doc = new SvgIconDocument(0, 0, 16, 16, [layer]);
        Assert.Null(SvgIconRasterizer.Bake(doc, 128));
    }

    [Fact]
    public void AStrokeDeviceSpaceOverflowFailsTheBake()
    {
        SvgIconDocument doc = Parse("""<path d="M0 0h1e308" stroke="black" transform="scale(10)"/>""");
        Assert.Null(SvgIconRasterizer.Bake(doc, 16));
    }

    [Fact]
    public void ManyLargeArcsAreChargedTheirFlattenedPoints()
    {
        var sb = new StringBuilder("M8 8");
        for (int i = 0; i < 1000; i++) sb.Append(i % 2 == 0 ? "a60 60 0 1 0 1 0" : "a60 60 0 1 1-1 0");
        SvgIconDocument doc = Parse($"""<path d="{sb}"/>""");
        Assert.Null(SvgIconRasterizer.Bake(doc, 128));
        Assert.NotNull(SvgIconRasterizer.Bake(Parse("""<circle cx="8" cy="8" r="6"/>"""), 128));
    }

    private static SvgSubpath Zigzag(int count)
    {
        var segments = new List<SvgSegment>();
        for (int i = 1; i <= count; i++) segments.Add(SvgSegment.Line(new SvgPoint(i % 2 == 0 ? 1 : 15, 1 + i % 13)));
        return new SvgSubpath(new SvgPoint(1, 1), segments, false);
    }

    [Fact]
    public void AStrokeAfterAFillIsGivenOnlyTheRemainingBudget()
    {
        var stroke = new SvgPaintLayer(SvgPaintKind.Stroke, 1, [Zigzag(3_000)],
            new SvgStrokeStyle(1, SvgLineCap.Butt, SvgLineJoin.Miter, 4), SvgMatrix.Identity);
        var fill = new SvgPaintLayer(SvgPaintKind.Fill, 1, [Zigzag(55_000)], null, SvgMatrix.Identity);
        Assert.NotNull(SvgIconRasterizer.Bake(new SvgIconDocument(0, 0, 16, 16, [stroke]), 128));
        Assert.Null(SvgIconRasterizer.Bake(new SvgIconDocument(0, 0, 16, 16, [fill, stroke]), 128));
    }

    [Fact]
    public void AStrokedXWithRoundCapsMatchesItsGolden()
    {
        SvgIconDocument doc = Parse(
            """<path d="M6 6l12 12M18 6L6 18" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round"/>""",
            viewBox: "0 0 24 24");
        byte[] c = SvgIconRasterizer.Bake(doc, 24)!;
        CompareWithGolden(c, 24);
    }

    private static void CompareWithGolden(byte[] coverage, int size, [CallerFilePath] string source = "")
    {
        string path = Path.Combine(Path.GetDirectoryName(source)!, GoldenFile);
        var text = new StringBuilder($"P2\n{size} {size}\n255\n");
        for (int y = 0; y < size; y++)
            text.AppendLine(string.Join(' ', Enumerable.Range(0, size).Select(x => coverage[y * size + x].ToString("D3"))));
        bool update = Environment.GetEnvironmentVariable("ACDREAM_UPDATE_SVG_GOLDEN") == "1";
        if (!File.Exists(path) || update)
        {
            File.WriteAllText(path, text.ToString());
            Assert.True(update, $"There was no golden, so it was written to {path}; review it and run again.");
            return;
        }

        int[] expected = File.ReadAllText(path).Split((char[])[' ', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries)
            .Skip(4).Select(int.Parse).ToArray();
        Assert.Equal(size * size, expected.Length);
        for (int i = 0; i < expected.Length; i++)
        {
            Assert.True(Math.Abs(expected[i] - coverage[i]) <= 2,
                $"pixel ({i % size},{i / size}) is {coverage[i]}, golden {expected[i]}; "
                + "if the change is meant, rewrite it with ACDREAM_UPDATE_SVG_GOLDEN=1");
        }
    }
}
