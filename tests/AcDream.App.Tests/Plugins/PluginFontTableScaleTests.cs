using AcDream.App.Plugins;
using AcDream.App.Tests.UI;
using AcDream.App.UI;
using AcDream.Plugin.Abstractions;

namespace AcDream.App.Tests.Plugins;

/// <summary>
/// A plugin's fonts readied for a scaled canvas: each gets a sharper bake at
/// the scale, or the largest step that fits, without changing its handle or
/// its metrics; the plugin's own count against a budget of their own; the
/// bake for another scale is given back; and sizes keep to quarter pixels.
/// </summary>
public sealed class PluginFontTableScaleTests
{
    private sealed class Rig
    {
        public PluginFontTableTests.FakeFontBackend Backend { get; } = new();
        public BundledCanvasFontCache Bundled { get; }
        public List<string> Reports { get; } = [];
        public PluginFontTable Table { get; }

        public Rig(PluginFontBudget? budget = null)
        {
            Bundled = new BundledCanvasFontCache(Backend, BundledUiFont.ReadEmbeddedFontBytes, 2048);
            Table = new PluginFontTable("example.plugin", budget ?? PluginFontBudget.Default, Reports.Add);
            Table.Bind(Backend, Bundled, Environment.CurrentManagedThreadId);
        }

        public CanvasFont Resolve(PluginFont font)
        {
            Assert.True(Table.TryResolve(font, out CanvasFont? resolved));
            return resolved;
        }
    }

    private static Func<Stream> Otf() => () => new MemoryStream(CanvasFontBakerTests.CffFixture());

    [Fact]
    public void EveryHeldFontGetsASharperBakeAndKeepsItsHandleAndMetrics()
    {
        var rig = new Rig();
        PluginFont bundled = rig.Table.AcquireBundled(16f);
        PluginFont own = rig.Table.AcquireStream("f.otf", Otf(), 16f, null);
        float width = rig.Resolve(bundled).MeasureWidth("AVTo?");
        long owned = rig.Table.OwnedBytes;

        rig.Table.PrepareScale(2f);

        Assert.Equal(2f, rig.Resolve(bundled).Sharp!.Scale);
        Assert.Equal(2f, rig.Resolve(own).Sharp!.Scale);
        Assert.Equal(bundled, rig.Table.AcquireBundled(16f));
        Assert.Equal(width, rig.Resolve(bundled).MeasureWidth("AVTo?"));
        Assert.Equal(owned, rig.Table.OwnedBytes);
        // Only the plugin's own font's sharper atlas counts, and against its own budget.
        Assert.Equal(rig.Resolve(own).Sharp!.AtlasBytes, rig.Table.SharpBytes);
        Assert.Empty(rig.Reports);
    }

    [Fact]
    public void TheSameScaleAgainBakesNothing()
    {
        var rig = new Rig();
        rig.Table.AcquireBundled(16f);
        rig.Table.AcquireStream("f.otf", Otf(), 16f, null);
        rig.Table.PrepareScale(2f);
        int uploads = rig.Backend.Uploaded.Count;

        rig.Table.PrepareScale(2f);

        Assert.Equal(uploads, rig.Backend.Uploaded.Count);
    }

    [Fact]
    public void AnotherScaleGivesTheOldSharperBakesBackAndOneGivesThemAllBack()
    {
        var rig = new Rig();
        PluginFont own = rig.Table.AcquireStream("f.otf", Otf(), 16f, null);
        rig.Table.PrepareScale(2f);
        uint twoX = rig.Resolve(own).Sharp!.AtlasTexture;

        rig.Table.PrepareScale(1.5f);

        Assert.Equal([twoX], rig.Backend.Released);
        Assert.Equal(1.5f, rig.Resolve(own).Sharp!.Scale);
        uint oneAndAHalfX = rig.Resolve(own).Sharp!.AtlasTexture;

        rig.Table.PrepareScale(1f);

        Assert.Null(rig.Resolve(own).Sharp);
        Assert.Equal([twoX, oneAndAHalfX], rig.Backend.Released);
        Assert.Equal(0L, rig.Table.SharpBytes);
    }

    [Fact]
    public void ReleasingAFontGivesBackItsSharperBake()
    {
        var rig = new Rig();
        PluginFont own = rig.Table.AcquireStream("f.otf", Otf(), 16f, null);
        rig.Table.PrepareScale(2f);
        CanvasFont resolved = rig.Resolve(own);
        uint sharp = resolved.Sharp!.AtlasTexture;

        rig.Table.Release(own);

        Assert.Equal([sharp, resolved.AtlasTexture], rig.Backend.Released);
        Assert.Equal(0L, rig.Table.SharpBytes);
    }

    [Fact]
    public void AFontTooLargeToBakeAtTheScaleIsBakedAtTheLargestStepThatFitsAndReportedOnce()
    {
        var rig = new Rig();
        PluginFont large = rig.Table.AcquireBundled(64f);

        rig.Table.PrepareScale(2f);
        rig.Table.PrepareScale(2f);

        Assert.Equal(1.75f, rig.Resolve(large).Sharp!.Scale);
        string report = Assert.Single(rig.Reports);
        Assert.Equal(
            "Plugin 'example.plugin': the bundled font at 64 px is drawn at 1.75x on canvases painted at 2x, "
            + "so it is less sharp than it could be: 1063 glyphs at 128 px do not fit a 2048x2048 atlas.",
            report);
    }

    [Fact]
    public void ThePluginsOwnFontsStayWithinTheirSharperBudget()
    {
        // The fixture's five glyphs fit the smallest atlas at any scale: room for one sharper bake.
        var rig = new Rig(PluginFontBudget.Default with { MaximumSharpBytes = 256 * 256 });
        PluginFont first = rig.Table.AcquireStream("f.otf", Otf(), 16f, null);
        PluginFont second = rig.Table.AcquireStream("f.otf", Otf(), 20f, null);
        PluginFont bundled = rig.Table.AcquireBundled(16f);

        rig.Table.PrepareScale(2f);

        Assert.Equal(2f, rig.Resolve(first).Sharp!.Scale);
        Assert.Null(rig.Resolve(second).Sharp);
        Assert.Equal(256L * 256, rig.Table.SharpBytes);
        // The shared bundled font is not the plugin's to budget.
        Assert.Equal(2f, rig.Resolve(bundled).Sharp!.Scale);
        string report = Assert.Single(rig.Reports);
        Assert.Equal(
            "Plugin 'example.plugin': font 'f.otf' at 20 px is drawn at 1x on canvases painted at 2x, "
            + "so it is less sharp than it could be: the plugin's sharper fonts have 0 of their 65,536 bytes left.",
            report);
    }

    [Theory]
    [InlineData(16.1f, 16f)]
    [InlineData(16.125f, 16.25f)]
    [InlineData(16.3f, 16.25f)]
    [InlineData(63.9f, 64f)]
    public void SizesKeepToQuarterPixels(float asked, float prepared)
    {
        var rig = new Rig();

        PluginFont font = rig.Table.AcquireBundled(asked);

        Assert.Equal(prepared, font.PixelSize);
        Assert.Equal(font, rig.Table.AcquireBundled(prepared));
        Assert.Equal(prepared, rig.Table.AcquireStream("f.otf", Otf(), asked, null).PixelSize);
    }
}
