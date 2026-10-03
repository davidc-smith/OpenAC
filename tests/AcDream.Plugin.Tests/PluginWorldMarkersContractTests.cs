using AcDream.Plugin.Abstractions;

namespace AcDream.Plugin.Tests;

/// <summary>
/// The world-marker contract a plugin compiles against: how anchors are
/// made, what an icon looks like when the plugin says nothing more, and
/// that a host which draws nothing hands out a surface with no layers.
/// </summary>
public sealed class PluginWorldMarkersContractTests
{
    private static readonly PluginNavigationPosition Spot =
        new(0x7D640013u, 12.5, -40.25, 0.5, 90f, IsOutdoor: true);

    [Fact]
    public void AnObjectAnchorNamesTheObjectAndNoPosition()
    {
        PluginMarkerAnchor anchor = PluginMarkerAnchor.Object(0x80001234u);

        Assert.Equal(PluginMarkerAnchorKind.Object, anchor.Kind);
        Assert.Equal(0x80001234u, anchor.ObjectId);
        Assert.Equal(default, anchor.Position);
    }

    [Fact]
    public void APositionAnchorNamesThePositionAndNoObject()
    {
        PluginMarkerAnchor anchor = PluginMarkerAnchor.At(Spot);

        Assert.Equal(PluginMarkerAnchorKind.Position, anchor.Kind);
        Assert.Equal(0u, anchor.ObjectId);
        Assert.Equal(Spot, anchor.Position);
    }

    [Fact]
    public void TheDefaultAnchorPinsNothing()
    {
        Assert.Equal(PluginMarkerAnchorKind.None, default(PluginMarkerAnchor).Kind);
    }

    [Fact]
    public void AnIconDefaultsToAMediumWhiteUnframedIconSeenFromSixtyMetres()
    {
        var icon = new PluginWorldIcon(PluginMarkerAnchor.Object(1u), new PluginImage(3, 32, 32));

        Assert.Equal(24f, icon.SizePixels);
        Assert.Equal(PluginColor.White, icon.Tint);
        Assert.Null(icon.Border);
        Assert.Equal(60f, icon.MaxRange);
        Assert.Equal(8f, PluginWorldIcon.MinimumSize);
        Assert.Equal(128f, PluginWorldIcon.MaximumSize);
        Assert.Equal(256, IPluginWorldMarkers.MaximumIcons);
    }

    [Fact]
    public void AHostThatDrawsNothingHandsOutNoLayers()
    {
        IUiRegistry ui = NoOpUiRegistry.Instance;
        IScopedUiRegistry scoped = NoOpUiRegistry.Instance;

        Assert.Null(NoOpPluginWorldMarkers.Instance.CreateLayer());
        Assert.Null(ui.WorldMarkers.CreateLayer());
        Assert.Same(
            NoOpPluginWorldMarkers.Instance,
            scoped.WorldMarkersFor(new PluginUiOwner("example.plugin", "Example")));
    }
}
