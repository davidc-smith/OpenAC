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

    private static readonly PluginColor Paint = new(10, 20, 30, 40);

    [Fact]
    public void ARingIsAFullBandOfTheGivenWidth()
    {
        PluginGroundShape ring = PluginGroundShape.Ring(PluginMarkerAnchor.Object(1u), radius: 2f, width: 0.2f, Paint);

        Assert.Equal(PluginGroundShapeKind.Ring, ring.Kind);
        Assert.Equal(PluginMarkerAnchor.Object(1u), ring.Anchor);
        Assert.Equal(2f, ring.Radius);
        Assert.Equal(0.2f, ring.Width);
        Assert.Equal(360f, ring.SweepDegrees);
        Assert.False(ring.Filled);
        Assert.Equal(Paint, ring.Color);
    }

    [Fact]
    public void ADiscIsAFilledFullCircle()
    {
        PluginGroundShape disc = PluginGroundShape.Disc(PluginMarkerAnchor.At(Spot), radius: 6f, Paint);

        Assert.Equal(PluginGroundShapeKind.Disc, disc.Kind);
        Assert.Equal(6f, disc.Radius);
        Assert.Equal(0f, disc.Width);
        Assert.Equal(360f, disc.SweepDegrees);
        Assert.True(disc.Filled);
    }

    [Fact]
    public void AnArcIsAThinBandWhenNoWidthIsGiven()
    {
        PluginGroundShape arc = PluginGroundShape.Arc(
            PluginMarkerAnchor.Object(1u), radius: 5f, startDegrees: -45f, sweepDegrees: 90f, filled: false, Paint);

        Assert.Equal(PluginGroundShapeKind.Arc, arc.Kind);
        Assert.Equal(PluginGroundShape.DefaultWidth, arc.Width);
        Assert.Equal(0.15f, PluginGroundShape.DefaultWidth);
        Assert.Equal(-45f, arc.StartDegrees);
        Assert.Equal(90f, arc.SweepDegrees);
        Assert.False(arc.Filled);
        Assert.False(arc.FacesObject);
    }

    [Fact]
    public void AWedgeCanBeMadeToFaceItsObject()
    {
        PluginGroundShape wedge = PluginGroundShape.Arc(
            PluginMarkerAnchor.Object(1u), 5f, -30f, 60f, filled: true, Paint) with { FacesObject = true };

        Assert.True(wedge.Filled);
        Assert.True(wedge.FacesObject);
    }

    [Fact]
    public void TheDefaultShapeIsOfNoKind()
    {
        Assert.Equal(PluginGroundShapeKind.None, default(PluginGroundShape).Kind);
    }

    [Fact]
    public void APluginMayHaveTwoHundredAndFiftySixShapesSet()
    {
        Assert.Equal(256, IPluginWorldMarkers.MaximumShapes);
    }
}
