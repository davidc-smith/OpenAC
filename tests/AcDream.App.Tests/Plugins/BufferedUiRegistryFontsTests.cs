using System.Threading;
using AcDream.App.Plugins;
using AcDream.Core.Plugins;
using AcDream.Core.Selection;
using AcDream.Plugin.Abstractions;

namespace AcDream.App.Tests.Plugins;

/// <summary>
/// The graphical host's font surface, reached the way a plugin reaches it:
/// inert until the interface binds its texture services, live after, one
/// table per plugin, and let go when the plugin or the interface goes.
/// </summary>
public sealed class BufferedUiRegistryFontsTests
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
    public void ThroughTheHostTheSurfaceIsInertUntilTheInterfaceBindsAndLiveAfter()
    {
        (IPluginHost host, BufferedUiRegistry registry) = Host();
        IPluginFonts fonts = host.Ui.Fonts;

        Assert.NotSame(NoOpPluginFonts.Instance, fonts);
        Assert.False(fonts.IsAvailable);
        Assert.Equal(PluginFont.None, fonts.Bundled(16f));
        Assert.Equal(PluginFontBudget.Default.MaximumCount, fonts.MaximumCount);
        Assert.Equal(PluginFontBudget.Default.MaximumBytes, fonts.MaximumBytes);
        Assert.Equal(PluginFontBudget.Default.MaximumGlyphs, fonts.MaximumGlyphs);
        Assert.Equal(6f, fonts.MinimumPixelSize);
        Assert.Equal(64f, fonts.MaximumPixelSize);

        registry.BindFontServices(new PluginFontTableTests.FakeFontBackend());

        Assert.True(fonts.IsAvailable);
        Assert.True(fonts.Bundled(16f).IsValid);
        Assert.Equal(1, fonts.Count);
        Assert.Same(fonts, host.Ui.Fonts);
    }

    [Fact]
    public void ASurfaceMadeAfterBindingIsBoundAtOnce()
    {
        (_, BufferedUiRegistry registry) = Host();
        registry.BindFontServices(new PluginFontTableTests.FakeFontBackend());

        IPluginFonts fonts = registry.FontsFor(new PluginUiOwner("late.plugin", "Late"));

        Assert.True(fonts.IsAvailable);
    }

    [Fact]
    public void ASurfaceFirstAskedForOnAWorkerStillAnswersOnlyTheInterfaceThread()
    {
        (_, BufferedUiRegistry registry) = Host();
        registry.BindFontServices(new PluginFontTableTests.FakeFontBackend());
        var owner = new PluginUiOwner("worker.plugin", "Worker");
        Exception? workerFailure = null;

        var worker = new Thread(() =>
        {
            try { registry.FontsFor(owner).Bundled(16f); }
            catch (Exception caught) { workerFailure = caught; }
        });
        worker.Start();
        worker.Join();

        Assert.IsType<InvalidOperationException>(workerFailure);
        Assert.True(registry.FontsFor(owner).Bundled(16f).IsValid);
    }

    [Fact]
    public void DisposingAPluginsSurfaceLetsItsFontsGoAndTheRegistryForgetsIt()
    {
        (_, BufferedUiRegistry registry) = Host();
        var backend = new PluginFontTableTests.FakeFontBackend();
        registry.BindFontServices(backend);
        var owner = new PluginUiOwner("example.plugin", "Example");
        IPluginFonts fonts = registry.FontsFor(owner);
        fonts.Bundled(16f);

        ((IDisposable)fonts).Dispose();

        Assert.Single(backend.Released);
        Assert.Null(registry.FindFonts(owner));
        Assert.NotSame(fonts, registry.FontsFor(owner));
    }

    [Fact]
    public void UnbindingLetsEveryPluginsFontsGo()
    {
        (_, BufferedUiRegistry registry) = Host();
        var backend = new PluginFontTableTests.FakeFontBackend();
        registry.BindFontServices(backend);
        IPluginFonts first = registry.FontsFor(new PluginUiOwner("a.plugin", "A"));
        IPluginFonts second = registry.FontsFor(new PluginUiOwner("b.plugin", "B"));
        first.Bundled(16f);
        second.Bundled(16f);
        second.Bundled(20f);

        registry.UnbindFontServices();

        Assert.False(first.IsAvailable);
        Assert.Equal(0, first.Count);
        Assert.Equal(2, backend.Released.Count);
        registry.UnbindFontServices();
        registry.BindFontServices(backend);
        Assert.Throws<InvalidOperationException>(() => registry.BindFontServices(backend));
    }

    [Fact]
    public void AfterAReconnectAHandleFromBeforeNoLongerResolvesAndANewOneDoes()
    {
        (_, BufferedUiRegistry registry) = Host();
        var backend = new PluginFontTableTests.FakeFontBackend();
        registry.BindFontServices(backend);
        var owner = new PluginUiOwner("example.plugin", "Example");
        IPluginFonts fonts = registry.FontsFor(owner);
        PluginFont before = fonts.Bundled(16f);

        registry.UnbindFontServices();
        registry.BindFontServices(backend);

        Assert.False(registry.FindFonts(owner)!.TryResolve(before, out _));
        Assert.True(fonts.IsAvailable);
        PluginFont after = fonts.Bundled(16f);
        Assert.True(after.IsValid);
        Assert.NotEqual(before.Handle, after.Handle);
    }
}
