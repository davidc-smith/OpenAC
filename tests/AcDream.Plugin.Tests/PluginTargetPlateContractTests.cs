using AcDream.Plugin.Abstractions;
using Xunit;

namespace AcDream.Plugin.Tests;

public sealed class PluginTargetPlateContractTests
{
    private sealed class Provider : IPluginTargetPlateProvider
    {
        public PluginTargetPlateContent? Describe(PluginTargetPlateQuery query) => null;
        public void OnAction(uint objectId, string actionId) { }
    }

    [Fact]
    public void AHostThatDrawsNothingHasNoTargetPlate()
    {
        IUiRegistry ui = NoOpUiRegistry.Instance;
        IScopedUiRegistry scoped = NoOpUiRegistry.Instance;

        Assert.False(ui.TargetPlate.IsAvailable);
        using (ui.TargetPlate.AddProvider(new Provider())) { }
        ui.TargetPlate.Refresh(1u);
        Assert.Same(NoOpPluginTargetPlate.Instance, scoped.TargetPlateFor(new PluginUiOwner("example.plugin", "Example")));
    }

    [Fact]
    public void ContentLimitsArePublished()
    {
        Assert.Equal(2, PluginTargetPlateContent.MaximumRows);
        Assert.Equal(4, PluginTargetPlateContent.MaximumActions);
    }
}
