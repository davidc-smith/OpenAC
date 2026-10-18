using AcDream.App.Plugins;
using AcDream.Core.Plugins;
using AcDream.Core.Selection;
using AcDream.Plugin.Abstractions;

namespace AcDream.App.Tests.Plugins;

/// <summary>
/// The graphical host tells plugins the interface's size: 0×0 until the
/// interface first draws, then each size it draws at, raised once per real
/// change and never for the same size twice.
/// </summary>
public sealed class BufferedUiRegistryScreenSizeTests
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
    public void TheSizeIsZeroUntilTheInterfaceDraws()
    {
        (IPluginHost host, _) = Host();

        Assert.Equal(new PluginSize(0, 0), host.Ui.ScreenSize);
    }

    [Fact]
    public void EachRealResizeIsReadAndHeardOnce()
    {
        (IPluginHost host, BufferedUiRegistry registry) = Host();
        var heard = new List<PluginSize>();
        host.Ui.ScreenSizeChanged += heard.Add;

        // The interface publishes every frame it draws.
        registry.PublishScreenSize(new PluginSize(800, 600));
        registry.PublishScreenSize(new PluginSize(800, 600));
        Assert.Equal(new PluginSize(800, 600), host.Ui.ScreenSize);
        registry.PublishScreenSize(new PluginSize(1280, 720));
        registry.PublishScreenSize(new PluginSize(1280, 720));
        registry.PublishScreenSize(new PluginSize(1280, 720));

        Assert.Equal(new PluginSize(1280, 720), host.Ui.ScreenSize);
        Assert.Equal([new PluginSize(800, 600), new PluginSize(1280, 720)], heard);
    }

    [Fact]
    public void APluginWhoseHandlerThrowsDoesNotStopTheNextFromHearing()
    {
        (IPluginHost host, BufferedUiRegistry registry) = Host();
        int heard = 0;
        host.Ui.ScreenSizeChanged += _ => throw new InvalidOperationException("plugin bug");
        host.Ui.ScreenSizeChanged += _ => heard++;

        registry.PublishScreenSize(new PluginSize(1024, 768));

        Assert.Equal(1, heard);
    }

    [Fact]
    public void ARemovedHandlerIsNotCalled()
    {
        (IPluginHost host, BufferedUiRegistry registry) = Host();
        int heard = 0;
        Action<PluginSize> handler = _ => heard++;

        host.Ui.ScreenSizeChanged += handler;
        host.Ui.ScreenSizeChanged -= handler;
        registry.PublishScreenSize(new PluginSize(1024, 768));

        Assert.Equal(0, heard);
        Assert.Equal(new PluginSize(1024, 768), host.Ui.ScreenSize);
    }
}
