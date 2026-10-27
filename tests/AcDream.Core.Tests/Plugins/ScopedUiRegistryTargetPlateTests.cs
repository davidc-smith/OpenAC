using AcDream.Core.Plugins;
using AcDream.Core.Selection;
using AcDream.Plugin.Abstractions;

namespace AcDream.Core.Tests.Plugins;

/// <summary>
/// A plugin's world-marker surface is asked of the host once, under the
/// plugin's own owner, and goes with the plugin through the scoped
/// registry's existing rollback.
/// </summary>
public sealed class ScopedUiRegistryTargetPlateTests
{
    private sealed class FakePlate : IPluginTargetPlate, IDisposable
    {
        public int Disposals { get; private set; }

        public bool IsAvailable => true;

        public IDisposable AddProvider(IPluginTargetPlateProvider provider) =>
            NoOpPluginTargetPlate.Instance.AddProvider(provider);

        public void Refresh(uint objectId) { }

        public void Dispose() => Disposals++;
    }

    private sealed class FakeScopedUiRegistry : IScopedUiRegistry
    {
        public List<PluginUiOwner> Asked { get; } = [];
        public FakePlate Plate { get; } = new();

        public void AddMarkupPanel(string markupPath, object binding)
        {
        }

        public IDisposable RegisterMarkupPanel(string markupPath, object binding) =>
            NoOpUiRegistration.Instance;

        public IPluginTargetPlate TargetPlateFor(PluginUiOwner owner)
        {
            Asked.Add(owner);
            return Plate;
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

        IPluginTargetPlate first = scoped.Ui.TargetPlate;
        IPluginTargetPlate second = scoped.Ui.TargetPlate;

        Assert.Same(inner.Plate, first);
        Assert.Same(first, second);
        Assert.Equal(new PluginUiOwner("example.plugin", "Example"), Assert.Single(inner.Asked));
        scoped.Dispose();
    }

    [Fact]
    public void TheSurfaceGoesWithThePlugin()
    {
        var inner = new FakeScopedUiRegistry();
        var scoped = new ScopedPluginHost(new StubHost(inner), "example.plugin", "Example");
        _ = scoped.Ui.TargetPlate;

        scoped.Dispose();

        Assert.Equal(1, inner.Plate.Disposals);
    }
}
