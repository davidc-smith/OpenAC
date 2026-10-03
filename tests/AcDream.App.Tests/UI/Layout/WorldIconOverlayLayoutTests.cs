using System.Numerics;
using AcDream.App.Interaction;
using AcDream.App.Plugins;
using AcDream.App.UI.Layout;
using AcDream.Plugin.Abstractions;

namespace AcDream.App.Tests.UI.Layout;

/// <summary>
/// Where icons land on the screen: an object's icons in centred rows of
/// eight above its head and its labels, a position's icon centred on it, the
/// range and screen cuts, the fade, and the far-to-near order.
/// </summary>
public sealed class WorldIconOverlayLayoutTests
{
    private const float LineHeight = 16f;
    private static readonly Vector2 Viewport = new(800f, 600f);

    // The camera stands at the origin looking along +Y with +Z up, so an
    // object at (0, d, 0) is d metres away, straight ahead.
    private static readonly Matrix4x4 View =
        Matrix4x4.CreateLookAt(Vector3.Zero, Vector3.UnitY, Vector3.UnitZ);

    // A 90 degree vertical field of view: a head 2 m up at 10 m away
    // projects to (400, 240) on an 800x600 screen.
    private static readonly Matrix4x4 Projection =
        Matrix4x4.CreatePerspectiveFieldOfView(MathF.PI / 2f, 4f / 3f, 0.1f, 100f);

    private static readonly PluginImage Image = new(1, 32, 32);

    private static WorldIconEntry Over(uint id, float size = 24f, float range = 60f) =>
        new("a.plugin", new PluginWorldIcon(PluginMarkerAnchor.Object(id), Image)
        {
            SizePixels = size,
            MaxRange = range,
        });

    private static WorldIconEntry AtSpot(float range = 60f) =>
        new("a.plugin", new PluginWorldIcon(
            PluginMarkerAnchor.At(new PluginNavigationPosition(1u, 0, 0, 0, 0f, true)), Image)
        {
            MaxRange = range,
        });

    private static WorldLabelAnchor? StandingAt(float distance, float x = 0f) =>
        new(new Vector3(x, distance, 0f), 2f, WorldLabelAnchorSource.PhysicsCylinder);

    private static List<WorldIconPlacement> Place(
        IReadOnlyList<WorldIconEntry> icons,
        Func<uint, WorldLabelAnchor?> anchor,
        Func<uint, int>? labelLines = null,
        Func<PluginNavigationPosition, Vector3?>? position = null)
    {
        var output = new List<WorldIconPlacement>();
        new WorldIconLayout().Place(
            icons,
            anchor,
            position ?? (_ => null),
            labelLines ?? (_ => 0),
            LineHeight,
            View,
            Projection,
            Viewport,
            output);
        return output;
    }

    [Fact]
    public void AnIconSitsCentredOverTheHeadWithItsBottomJustAboveIt()
    {
        WorldIconPlacement only = Assert.Single(Place([Over(1u)], _ => StandingAt(10f)));

        // Head at (400, 240); the row's bottom is a 2-point gap above it.
        Assert.Equal(388f, only.X, 3);
        Assert.Equal(214f, only.Y, 3);
        Assert.Equal(24f, only.Size);
        Assert.Equal(10f, only.Depth, 3);
        Assert.Equal(1f, only.Alpha);
    }

    [Fact]
    public void IconsSitAboveTheObjectsLabels()
    {
        WorldIconPlacement only = Assert.Single(Place(
            [Over(1u)], _ => StandingAt(10f), labelLines: id => id == 1u ? 2 : 0));

        Assert.Equal(240f - 2 * LineHeight - 2f - 24f, only.Y, 3);
    }

    [Fact]
    public void AnObjectsIconsAreCentredInARowWithGaps()
    {
        List<WorldIconPlacement> placed = Place([Over(1u), Over(1u), Over(1u)], _ => StandingAt(10f));

        Assert.Equal(new[] { 362f, 388f, 414f }, placed.OrderBy(p => p.IconIndex).Select(p => MathF.Round(p.X, 3)));
        Assert.All(placed, p => Assert.Equal(214f, p.Y, 3));
    }

    [Fact]
    public void TheNinthIconStartsANewRowAbove()
    {
        List<WorldIconPlacement> placed = Place(
            Enumerable.Range(0, 10).Select(_ => Over(1u)).ToArray(), _ => StandingAt(10f));

        var byIndex = placed.OrderBy(p => p.IconIndex).ToArray();
        Assert.All(byIndex.Take(8), p => Assert.Equal(214f, p.Y, 3));
        Assert.Equal(188f, byIndex[8].Y, 3);
        Assert.Equal(375f, byIndex[8].X, 3);
        Assert.Equal(401f, byIndex[9].X, 3);
    }

    [Fact]
    public void IconsOfDifferentSizesShareTheRowsBottom()
    {
        List<WorldIconPlacement> placed = Place([Over(1u, size: 16f), Over(1u, size: 32f)], _ => StandingAt(10f));

        var byIndex = placed.OrderBy(p => p.IconIndex).ToArray();
        Assert.Equal(375f, byIndex[0].X, 3);
        Assert.Equal(222f, byIndex[0].Y, 3);
        Assert.Equal(393f, byIndex[1].X, 3);
        Assert.Equal(206f, byIndex[1].Y, 3);
    }

    [Fact]
    public void AnObjectsIconsKeepTheOrderGivenEvenWhenOtherIconsComeBetween()
    {
        List<WorldIconPlacement> placed = Place(
            [Over(1u), Over(2u), Over(1u)],
            id => id == 1u ? StandingAt(10f) : StandingAt(20f));

        WorldIconPlacement first = placed.Single(p => p.IconIndex == 0);
        WorldIconPlacement third = placed.Single(p => p.IconIndex == 2);
        Assert.True(first.X < third.X);
    }

    [Fact]
    public void AnIconPastItsRangeIsCutAndTheRowClosesUp()
    {
        List<WorldIconPlacement> placed = Place([Over(1u, range: 5f), Over(1u)], _ => StandingAt(10f));

        WorldIconPlacement only = Assert.Single(placed);
        Assert.Equal(1, only.IconIndex);
        Assert.Equal(388f, only.X, 3);
    }

    [Fact]
    public void TheLastFifthOfTheRangeFades()
    {
        WorldIconPlacement only = Assert.Single(Place([Over(1u, range: 11f)], _ => StandingAt(10f)));

        // Fade starts at 8.8 m and ends at 11 m: at 10 m it is 1/2.2 solid.
        Assert.Equal(1f / 2.2f, only.Alpha, 3);
    }

    [Fact]
    public void AMissingObjectAnObjectBehindTheCameraAndAnIconOffScreenAreNotPlaced()
    {
        Assert.Empty(Place([Over(1u)], _ => null));
        Assert.Empty(Place([Over(1u)], _ => StandingAt(-10f)));
        Assert.Empty(Place([Over(1u)], _ => StandingAt(10f, x: 50f)));
    }

    [Fact]
    public void APositionsIconIsCentredOnIt()
    {
        WorldIconPlacement only = Assert.Single(Place(
            [AtSpot()], _ => null, position: _ => new Vector3(0f, 10f, 2f)));

        Assert.Equal(388f, only.X, 3);
        Assert.Equal(228f, only.Y, 3);
        Assert.Equal(10f, only.Depth, 3);
    }

    [Fact]
    public void APositionTheClientCannotPlaceIsNotPlaced()
    {
        Assert.Empty(Place([AtSpot()], _ => null, position: _ => null));
        Assert.Empty(Place([AtSpot(range: 9f)], _ => null, position: _ => new Vector3(0f, 10f, 2f)));
    }

    [Fact]
    public void PlacementsRunFarToNearThenByIndex()
    {
        List<WorldIconPlacement> placed = Place(
            [Over(1u), Over(2u), Over(2u)],
            id => id == 1u ? StandingAt(10f) : StandingAt(20f));

        Assert.Equal(new[] { 1, 2, 0 }, placed.Select(p => p.IconIndex));
    }
}
