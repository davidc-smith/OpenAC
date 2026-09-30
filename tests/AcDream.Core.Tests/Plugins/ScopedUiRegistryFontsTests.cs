using AcDream.Core.Plugins;
using AcDream.Core.Selection;
using AcDream.Plugin.Abstractions;

namespace AcDream.Core.Tests.Plugins;

/// <summary>
/// A plugin's font surface is asked of the host once, under the plugin's own
/// owner, and goes with the plugin: the scoped registry tracks the host's
/// disposable surface like any other registration.
/// </summary>
public sealed class ScopedUiRegistryFontsTests
{
    private sealed class FakeFonts : IPluginFonts, IDisposable
    {
        public int Disposals { get; private set; }

        public void Dispose() => Disposals++;
    }

    private sealed class FakeScopedUiRegistry : IScopedUiRegistry
    {
        public List<PluginUiOwner> Asked { get; } = [];
        public FakeFonts Fonts { get; } = new();

        public void AddMarkupPanel(string markupPath, object binding)
        {
        }

        public IDisposable RegisterMarkupPanel(string markupPath, object binding) =>
            NoOpUiRegistration.Instance;

        public IPluginFonts FontsFor(PluginUiOwner owner)
        {
            Asked.Add(owner);
            return Fonts;
        }
    }

    private sealed class StubHost(IScopedUiRegistry ui) : IPluginHost
    {
        public bool HasUi => true;
        public IPluginLogger Log { get; } = new SilentLogger();
        public IGameState State { get; } = new EmptyGameState();
        public IEvents Events { get; } = new WorldEvents();
        public ISelectionService Selection { get; } = new SelectionState();
        public IUiRegistry Ui { get; } = ui;
        public IAutomationSurface Automation { get; } = NoOpAutomationSurface.Instance;

        private sealed class SilentLogger : IPluginLogger
        {
            public void Info(string message) { }
            public void Warn(string message) { }
            public void Error(string message, Exception? error = null) { }
        }

        private sealed class EmptyGameState : IGameState
        {
            public IReadOnlyList<WorldEntitySnapshot> Entities { get; } = [];
        }
    }

    [Fact]
    public void TheSurfaceIsAskedOnceUnderThePluginsOwnerAndKept()
    {
        var inner = new FakeScopedUiRegistry();
        var scoped = new ScopedPluginHost(new StubHost(inner), "example.plugin", "Example");

        IPluginFonts first = scoped.Ui.Fonts;
        IPluginFonts second = scoped.Ui.Fonts;

        Assert.Same(inner.Fonts, first);
        Assert.Same(first, second);
        Assert.Equal(new PluginUiOwner("example.plugin", "Example"), Assert.Single(inner.Asked));
        scoped.Dispose();
    }

    [Fact]
    public void DisposingThePluginDisposesItsSurface()
    {
        var inner = new FakeScopedUiRegistry();
        var scoped = new ScopedPluginHost(new StubHost(inner), "example.plugin", "Example");
        _ = scoped.Ui.Fonts;

        scoped.Dispose();

        Assert.Equal(1, inner.Fonts.Disposals);
        Assert.Throws<ObjectDisposedException>(() => scoped.Ui.Fonts);
    }

    [Fact]
    public void APluginThatNeverAskedLeavesNothingToDispose()
    {
        var inner = new FakeScopedUiRegistry();
        var scoped = new ScopedPluginHost(new StubHost(inner), "example.plugin", "Example");

        scoped.Dispose();

        Assert.Empty(inner.Asked);
        Assert.Equal(0, inner.Fonts.Disposals);
    }
}
