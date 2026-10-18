using AcDream.App.Plugins;
using AcDream.App.UI;
using AcDream.Core.Plugins;
using AcDream.Core.Selection;
using AcDream.Plugin.Abstractions;

namespace AcDream.App.Tests.Plugins;

/// <summary>
/// The graphical host tells plugins the theme the player picked under
/// Appearance, in the palette themed windows draw in, and offers the
/// semibold sans themed titles are set in.
/// </summary>
public sealed class BufferedUiRegistryThemeTests
{
    private sealed class SilentLogger : IPluginLogger
    {
        public void Info(string message) { }
        public void Warn(string message) { }
        public void Error(string message, Exception? error = null) { }
    }

    private static (IPluginHost Host, BufferedUiRegistry Registry) Host()
    {
        var registry = new BufferedUiRegistry();
        var host = new AppPluginHost(
            new SilentLogger(),
            new WorldGameState(),
            new WorldEvents(),
            new SelectionState(),
            registry,
            NoOpAutomationSurface.Instance);
        return (host, registry);
    }

    [Fact]
    public void ClassicIsWhatPluginsReadUntilTheInterfaceSaysOtherwise()
    {
        (IPluginHost host, _) = Host();

        Assert.Same(PluginUiThemeInfo.Classic, host.Ui.Theme);
    }

    [Fact]
    public void EachThemeIsReadWithItsNameAndItsWindowColours()
    {
        var settings = new PluginUiThemeSettings();
        Assert.Same(PluginUiThemeInfo.Classic, settings.ThemeInfo);

        settings.Theme = PluginUiTheme.Moss;
        PluginUiThemeInfo moss = settings.ThemeInfo;
        Assert.Equal(PluginUiThemeKind.Moss, moss.Kind);
        Assert.Equal("Charcoal + moss", moss.DisplayName);
        Assert.Equal(new PluginThemePalette(
            new(0x17, 0x1D, 0x1B), new(0x10, 0x16, 0x13), new(0x35, 0x44, 0x3B), new(0xE0, 0xE8, 0xE2),
            new(0x9A, 0xA9, 0x9E), new(0x97, 0xBE, 0x81), new(0x34, 0x4B, 0x37)), moss.Palette);

        settings.Theme = PluginUiTheme.Brass;
        PluginUiThemeInfo brass = settings.ThemeInfo;
        Assert.Equal(PluginUiThemeKind.Brass, brass.Kind);
        Assert.Equal("Warm graphite + brass", brass.DisplayName);
        Assert.Equal(new PluginColor(0x22, 0x1F, 0x1B), brass.Palette!.Background);
        Assert.Equal(new PluginColor(0xC9, 0xA6, 0x65), brass.Palette.Accent);
        Assert.Equal(new PluginColor(0x51, 0x44, 0x2D), brass.Palette.Selected);
    }

    [Fact]
    public void TheNamesAreTheOnesTheAppearancePickerShows()
    {
        var binding = new PluginAppearanceBinding(new PluginUiThemeSettings());

        foreach (PluginUiTheme theme in Enum.GetValues<PluginUiTheme>())
        {
            var settings = new PluginUiThemeSettings { Theme = theme };
            Assert.Equal(binding.Themes[(int)theme], settings.ThemeInfo.DisplayName);
            Assert.Equal(theme.ToString(), settings.ThemeInfo.Kind.ToString());
        }
    }

    [Fact]
    public void TheSettingsSayWhenThePlayerPicksAnotherTheme()
    {
        var settings = new PluginUiThemeSettings();
        int changes = 0;
        settings.ThemeChanged += () => changes++;
        var binding = new PluginAppearanceBinding(settings);

        binding.Select("Warm graphite + brass");
        binding.Select("Warm graphite + brass");
        settings.Dock = PluginDockMode.Left;

        Assert.Equal(1, changes);
    }

    [Fact]
    public void FollowingTheSettingsPublishesTheSavedThemeThenEachPickUntilLetGo()
    {
        (IPluginHost host, BufferedUiRegistry registry) = Host();
        var settings = new PluginUiThemeSettings { Theme = PluginUiTheme.Moss };
        var heard = new List<PluginUiThemeKind>();
        host.Ui.ThemeChanged += theme => heard.Add(theme.Kind);
        var binding = new PluginAppearanceBinding(settings);

        IDisposable follow = registry.FollowTheme(settings);
        binding.Select("Warm graphite + brass");
        follow.Dispose();
        follow.Dispose();
        binding.Select("Classic");

        Assert.Equal([PluginUiThemeKind.Moss, PluginUiThemeKind.Brass], heard);
        Assert.Equal(PluginUiThemeKind.Brass, host.Ui.Theme.Kind);
    }

    [Fact]
    public void APublishedThemeIsReadAndHeardOnceByEveryPlugin()
    {
        (IPluginHost host, BufferedUiRegistry registry) = Host();
        var heard = new List<PluginUiThemeInfo>();
        host.Ui.ThemeChanged += heard.Add;
        PluginUiThemeInfo moss = new PluginUiThemeSettings { Theme = PluginUiTheme.Moss }.ThemeInfo;

        registry.PublishTheme(moss);
        registry.PublishTheme(new PluginUiThemeSettings { Theme = PluginUiTheme.Moss }.ThemeInfo);

        Assert.Equal(moss, host.Ui.Theme);
        Assert.Equal([moss], heard);
    }

    [Fact]
    public void APluginWhoseHandlerThrowsDoesNotStopTheNextFromHearing()
    {
        (IPluginHost host, BufferedUiRegistry registry) = Host();
        int heard = 0;
        host.Ui.ThemeChanged += _ => throw new InvalidOperationException("plugin bug");
        host.Ui.ThemeChanged += _ => heard++;

        registry.PublishTheme(new PluginUiThemeSettings { Theme = PluginUiTheme.Brass }.ThemeInfo);

        Assert.Equal(1, heard);
    }

    [Fact]
    public void ARemovedHandlerIsNotCalled()
    {
        (IPluginHost host, BufferedUiRegistry registry) = Host();
        int heard = 0;
        Action<PluginUiThemeInfo> handler = _ => heard++;
        host.Ui.ThemeChanged += handler;
        host.Ui.ThemeChanged -= handler;

        registry.PublishTheme(new PluginUiThemeSettings { Theme = PluginUiTheme.Brass }.ThemeInfo);

        Assert.Equal(0, heard);
    }

    [Fact]
    public void TheSemiboldSansIsItsOwnFontBesideTheRegular()
    {
        (IPluginHost host, BufferedUiRegistry registry) = Host();
        var backend = new PluginFontTableTests.FakeFontBackend();
        registry.BindFontServices(backend);
        IPluginFonts fonts = host.Ui.Fonts;

        PluginFont regular = fonts.Bundled(16f);
        PluginFont sameRegular = fonts.Bundled(16f, PluginFontWeight.Regular);
        PluginFont semiBold = fonts.Bundled(16f, PluginFontWeight.SemiBold);
        PluginFont semiBoldAgain = fonts.Bundled(16f, PluginFontWeight.SemiBold);

        Assert.Equal(regular, sameRegular);
        Assert.True(semiBold.IsValid);
        Assert.NotEqual(regular.Handle, semiBold.Handle);
        Assert.Equal(semiBold, semiBoldAgain);
        Assert.Equal(2, fonts.Count);
        Assert.Equal(2, backend.Uploaded.Count);

        Assert.True(fonts.Release(semiBold));
        Assert.True(fonts.Release(semiBold));
        Assert.Single(backend.Released);
        Assert.Equal(1, fonts.Count);
    }

    [Fact]
    public void TwoPluginsShareOneSemiboldBake()
    {
        (_, BufferedUiRegistry registry) = Host();
        var backend = new PluginFontTableTests.FakeFontBackend();
        registry.BindFontServices(backend);

        PluginFont first = registry.FontsFor(new PluginUiOwner("a.plugin", "A")).Bundled(18f, PluginFontWeight.SemiBold);
        PluginFont second = registry.FontsFor(new PluginUiOwner("b.plugin", "B")).Bundled(18f, PluginFontWeight.SemiBold);

        Assert.True(first.IsValid);
        Assert.True(second.IsValid);
        Assert.Single(backend.Uploaded);
    }

    [Fact]
    public void UnbindingLetsTheSemiboldBakesGo()
    {
        (_, BufferedUiRegistry registry) = Host();
        var backend = new PluginFontTableTests.FakeFontBackend();
        registry.BindFontServices(backend);
        IPluginFonts fonts = registry.FontsFor(new PluginUiOwner("a.plugin", "A"));
        fonts.Bundled(16f, PluginFontWeight.SemiBold);

        registry.UnbindFontServices();

        Assert.Single(backend.Released);
        Assert.Equal(PluginFont.None, fonts.Bundled(16f, PluginFontWeight.SemiBold));
    }

    [Fact]
    public void ATableWithoutTheSemiboldBakesRefusesThatWeightOnce()
    {
        var backend = new PluginFontTableTests.FakeFontBackend();
        using var bundled = new BundledCanvasFontCache(backend, BundledUiFont.ReadEmbeddedFontBytes, 2048);
        var reports = new List<string>();
        using var table = new PluginFontTable("example.plugin", PluginFontBudget.Default, reports.Add);
        table.Bind(backend, bundled, Environment.CurrentManagedThreadId);

        Assert.Equal(PluginFont.None, table.AcquireBundled(16f, PluginFontWeight.SemiBold));
        Assert.Equal(PluginFont.None, table.AcquireBundled(16f, PluginFontWeight.SemiBold));
        Assert.True(table.AcquireBundled(16f).IsValid);

        Assert.Contains("SemiBold", Assert.Single(reports), StringComparison.Ordinal);
    }
}
