using System.Numerics;
using AcDream.App.Plugins;
using AcDream.App.Rendering;
using AcDream.App.Rendering.Gpu;
using AcDream.App.Tests.Rendering.Gpu;
using AcDream.App.UI;
using AcDream.Plugin.Abstractions;
using AcDream.Tests.Fixtures.PluginIcons;

namespace AcDream.App.Tests.UI;

/// <summary>A dock button's icon order: the window's own <c>IconFile</c> (.svg or .png), the
/// plugin's icon.svg, its icon.png, then the surface and initials. A window PNG is drawn in its
/// own colours, and a file that cannot be used is logged once and skipped.</summary>
public sealed class PluginDockIconSourceTests : IDisposable
{
    private const string Plugin = "acme.hello";
    private const string Svg = """<svg viewBox="0 0 24 24"><circle cx="12" cy="12" r="8"/></svg>""";
    private static readonly (uint, int, int) PluginPng = (7u, 64, 64);

    private readonly string _root = Path.Combine(Path.GetTempPath(), $"acdream-dock-icons-{Guid.NewGuid():N}");
    private readonly List<string> _reports = [];
    private readonly List<string> _uploads = [];
    private readonly Backend _backend = new();
    private readonly PluginSvgIconCache _svgs;
    private readonly PluginFileIconCache _images;
    private uint _next = 900;

    public PluginDockIconSourceTests()
    {
        Directory.CreateDirectory(Path.Combine(_root, "icons"));
        _svgs = new PluginSvgIconCache(_backend, _reports.Add);
        _images = new PluginFileIconCache(
            (_, _, _, name) =>
            {
                _uploads.Add(name);
                return _next++;
            },
            _ => true,
            _reports.Add,
            rules: PluginFileIconCache.Rules.DockIcons);
    }

    public void Dispose()
    {
        _images.Dispose();
        _svgs.Dispose();
        Directory.Delete(_root, recursive: true);
    }

    private sealed class Backend : IPluginFontBackend
    {
        private uint _next = 500;

        public uint UploadCoverage(byte[] coverage, int width, int height, string debugName) => _next++;

        public bool ReleaseCoverage(uint texture) => true;
    }

    private sealed class NullFrames : ICurrentGpuFrameSource
    {
        public IGpuFrame? CurrentFrame => null;
    }

    private void Write(string relative, byte[] bytes) => File.WriteAllBytes(Path.Combine(_root, relative), bytes);

    private void WriteSvg(string relative) => File.WriteAllText(Path.Combine(_root, relative), Svg);

    private PluginDockIcons Resolve(string? iconFile, string? directory = "", bool pluginPng = true) =>
        new PluginDockIconSource(_svgs, _images, (_, _) => pluginPng ? PluginPng : null).Resolve(
            Plugin,
            new PluginPanelDescriptor("main", "Main") { IconFile = iconFile },
            directory == "" ? _root : directory);

    [Fact]
    public void AWindowPngBeatsThePluginsIconSvgAndIconPng()
    {
        Write("icons/combat.png", PngTestData.Valid());
        WriteSvg("icon.svg");

        PluginDockIcons icons = Resolve("icons/combat.png");

        Assert.NotNull(icons.WindowImage);
        Assert.Equal((900u, 64, 64), (icons.WindowImage.Texture, icons.WindowImage.Width, icons.WindowImage.Height));
        Assert.Null(icons.Svg);
        Assert.Equal(0, _svgs.EntryCount);
        Assert.Equal(PluginPng, icons.PluginImage);
        Assert.Empty(_reports);
    }

    [Fact]
    public void AWindowSvgIsUsedAsBefore()
    {
        WriteSvg("icons/loot.svg");
        WriteSvg("icon.svg");

        PluginDockIcons icons = Resolve("icons/loot.svg");

        Assert.Null(icons.WindowImage);
        Assert.NotNull(icons.Svg);
        Assert.Equal(1, _svgs.EntryCount);
        Assert.Equal(PluginPng, icons.PluginImage);
    }

    [Fact]
    public void WithNoIconFileThePluginsIconSvgThenIconPngAreUsed()
    {
        WriteSvg("icon.svg");
        PluginDockIcons withSvg = Resolve(null);
        Assert.NotNull(withSvg.Svg);
        Assert.Equal(PluginPng, withSvg.PluginImage);

        File.Delete(Path.Combine(_root, "icon.svg"));
        PluginDockIcons withoutSvg = Resolve(null);
        Assert.Equal((null, null, PluginPng), (withoutSvg.WindowImage, withoutSvg.Svg, withoutSvg.PluginImage));
    }

    public static TheoryData<string, string> UnusablePngs() => new()
    {
        { nameof(PngTestData.WrongDimensions), "64x64" },
        { nameof(PngTestData.OverMaximumBytes), "64 KiB" },
        { nameof(PngTestData.NoSignature), "not a PNG" },
    };

    [Theory]
    [MemberData(nameof(UnusablePngs))]
    public void AWindowPngThatBreaksTheRulesFallsBackToIconSvgAndIsLoggedOnce(string fixture, string named)
    {
        Write("icons/combat.png", fixture switch
        {
            nameof(PngTestData.WrongDimensions) => PngTestData.WrongDimensions(),
            nameof(PngTestData.OverMaximumBytes) => PngTestData.OverMaximumBytes(),
            _ => PngTestData.NoSignature(),
        });
        WriteSvg("icon.svg");

        PluginDockIcons first = Resolve("icons/combat.png");
        Resolve("icons/combat.png");

        Assert.Null(first.WindowImage);
        Assert.NotNull(first.Svg);
        Assert.Empty(_uploads);
        string report = Assert.Single(_reports);
        Assert.Contains("plugin icon 'acme.hello/icons/combat.png' ignored", report, StringComparison.Ordinal);
        Assert.Contains(named, report, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("../outside.png", "outside")]
    [InlineData("/abs/combat.png", "outside")]
    [InlineData("icons/missing.png", "does not exist")]
    public void AWindowPngPathThatCannotBeUsedFallsBackToIconPng(string iconFile, string named)
    {
        PluginDockIcons icons = Resolve(iconFile);

        Assert.Equal((null, null, PluginPng), (icons.WindowImage, icons.Svg, icons.PluginImage));
        Assert.Contains(named, Assert.Single(_reports), StringComparison.Ordinal);
    }

    [Fact]
    public void AnIconFileThatIsNeitherSvgNorPngIsLoggedOnceAndSkipped()
    {
        Write("icons/combat.jpg", PngTestData.Valid());
        WriteSvg("icon.svg");

        PluginDockIcons icons = Resolve("icons/combat.jpg");
        Resolve("icons/combat.jpg");

        Assert.Null(icons.WindowImage);
        Assert.NotNull(icons.Svg);
        Assert.Contains("is not an .svg or .png file", Assert.Single(_reports), StringComparison.Ordinal);
    }

    [Fact]
    public void IconFileIsIgnoredWithoutAPluginFolder()
    {
        PluginDockIcons icons = Resolve("icons/combat.png", directory: null, pluginPng: false);

        Assert.Equal((null, null, null), (icons.WindowImage, icons.Svg, icons.PluginImage));
        Assert.Empty(_reports);
    }

    [Fact]
    public void TwoWindowsNamingTheSamePngUploadItOnce()
    {
        Write("icons/combat.png", PngTestData.Valid());

        PluginDockIcons first = Resolve("icons/combat.png");
        PluginDockIcons second = Resolve(@"icons\combat.png");

        Assert.Same(first.WindowImage, second.WindowImage);
        Assert.Single(_uploads);
    }

    [Fact]
    public void TheDockDrawsTheWindowPngInItsOwnColoursAheadOfAnSvg()
    {
        Write("icons/combat.png", PngTestData.Valid());
        WriteSvg("icon.svg");
        PluginDockIcons own = Resolve("icons/combat.png");
        PluginSvgIconCache.PluginSvgIconEntry svg = _svgs.Acquire("acme.hello/icon.svg", Path.Combine(_root, "icon.svg"))!;
        (PluginSidePanel shelf, PluginSidePanel.PluginShelfButton button) =
            Mount(own with { Svg = svg }, visible: true, new PluginUiThemeSettings { Theme = PluginUiTheme.Moss });
        Assert.Equal(string.Empty, button.Text);

        TextRenderer renderer = Draw(button);

        IReadOnlyList<float> verts = Assert.Single(renderer.DebugSpriteSegmentVerts, seg => seg.Texture == 900u).Verts;
        Assert.Equal(Vector4.One, new Vector4(verts[4], verts[5], verts[6], verts[7]));
        Assert.DoesNotContain(renderer.DebugSpriteSegmentCoverage, texture => texture != 0u);
        shelf.Dispose();
        Assert.Equal(0, _svgs.EntryCount);
    }

    [Fact]
    public void AWindowPngTakesItsLightAndDarkVersionsAlongWhenThereAreAny()
    {
        Write("icons/combat.png", PngTestData.Valid());
        Write("icons/combat.light.png", PngTestData.Valid());

        PluginDockIcons icons = Resolve("icons/combat.png");

        Assert.Equal(900u, icons.WindowImage!.Texture);
        Assert.Equal(901u, icons.WindowImageLight!.Texture);
        Assert.Null(icons.WindowImageDark);
        Assert.Empty(_reports);
    }

    [Theory]
    [InlineData(PluginUiTheme.Parchment, 901u)]
    [InlineData(PluginUiTheme.Moss, 902u)]
    [InlineData(PluginUiTheme.Classic, 902u)]
    public void TheDockDrawsTheVersionForTheTheme(PluginUiTheme theme, uint drawn)
    {
        Write("icons/combat.png", PngTestData.Valid());
        Write("icons/combat.light.png", PngTestData.Valid());
        Write("icons/combat.dark.png", PngTestData.Valid());
        PluginDockIcons icons = Resolve("icons/combat.png");
        (_, PluginSidePanel.PluginShelfButton button) =
            Mount(icons, visible: true, new PluginUiThemeSettings { Theme = theme });

        TextRenderer renderer = Draw(button);

        Assert.Single(renderer.DebugSpriteSegmentVerts, seg => seg.Texture == drawn);
        Assert.DoesNotContain(renderer.DebugSpriteSegmentVerts, seg => seg.Texture is >= 900u and <= 902u && seg.Texture != drawn);
    }

    [Fact]
    public void AWindowPngGivenBackWithItsCacheFallsThroughToTheInitials()
    {
        Write("icons/combat.png", PngTestData.Valid());
        PluginDockIcons own = Resolve("icons/combat.png", pluginPng: false);
        (_, PluginSidePanel.PluginShelfButton button) = Mount(own, visible: false);
        _images.Dispose();

        TextRenderer renderer = Draw(button);

        Assert.Equal("MA", button.Text);
        Assert.DoesNotContain(renderer.DebugSpriteSegmentVerts, seg => seg.Texture == 900u);
    }

    private static (PluginSidePanel, PluginSidePanel.PluginShelfButton) Mount(
        PluginDockIcons icons, bool visible, PluginUiThemeSettings? themes = null)
    {
        var root = new UiRoot { Width = 800f, Height = 600f };
        var shelf = new PluginSidePanel(
            root.WindowManager,
            _ => throw new InvalidOperationException("the DAT resolver must not run"),
            font: null,
            themes);
        root.AddChild(shelf);
        var frame = new UiPanel { Left = 300f, Width = 200f, Height = 100f, Visible = visible };
        root.AddChild(frame);
        RetailWindowHandle handle = root.WindowManager.Register("plugin:acme.hello:main", frame);
        shelf.Add(new PluginUiOwner(Plugin, "Hello"), new PluginPanelDescriptor("main", "Main"), handle, icons);
        return (shelf, Assert.Single(shelf.Children.OfType<PluginSidePanel.PluginShelfButton>()));
    }

    private static TextRenderer Draw(UiElement button)
    {
        var renderer = new TextRenderer(new RecordingGpuDevice(), new NullFrames(), "unused");
        renderer.Begin(new Vector2(200f, 200f));
        var context = new UiRenderContext(renderer, new Vector2(200f, 200f));
        context.Begin(new Vector2(200f, 200f), null, 1f);
        button.DrawSelfAndChildren(context);
        return renderer;
    }
}
