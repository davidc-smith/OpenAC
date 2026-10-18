using AcDream.App.Plugins;
using AcDream.Plugin.Abstractions;

namespace AcDream.App.Tests.Plugins;

/// <summary>
/// The graphical host hands each plugin the store's surface under the
/// plugin's own id, so the overlay knows whose images an icon's image is.
/// </summary>
public sealed class BufferedUiRegistryWorldMarkersTests
{
    private static readonly PluginWorldIcon Icon =
        new(PluginMarkerAnchor.Object(1u), new PluginImage(1, 32, 32));

    [Fact]
    public void EachPluginGetsItsOwnSurfaceAndItsIconsCarryItsId()
    {
        var registry = new BufferedUiRegistry();
        IPluginWorldMarkers a = registry.WorldMarkersFor(new PluginUiOwner("a.plugin", "A"));

        Assert.Same(a, registry.WorldMarkersFor(new PluginUiOwner("a.plugin", "A")));
        Assert.NotSame(a, registry.WorldMarkersFor(new PluginUiOwner("b.plugin", "B")));

        Assert.True(a.CreateLayer()!.SetIcons([Icon]));
        Assert.Equal("a.plugin", Assert.Single(registry.WorldMarkerStore.CaptureIcons()).OwnerId);
    }

    [Fact]
    public void TheUnscopedSurfaceBelongsToTheUnscopedOwner()
    {
        var registry = new BufferedUiRegistry();

        Assert.True(registry.WorldMarkers.CreateLayer()!.SetIcons([Icon]));

        Assert.Equal("unscoped", Assert.Single(registry.WorldMarkerStore.CaptureIcons()).OwnerId);
    }
}
