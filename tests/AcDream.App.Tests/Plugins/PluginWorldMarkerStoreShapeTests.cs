using AcDream.App.Plugins;
using AcDream.Core.Plugins;
using AcDream.Plugin.Abstractions;

namespace AcDream.App.Tests.Plugins;

/// <summary>
/// What the store takes from a plugin's ground shapes and what it hands the
/// renderer: the drop and normalise rules, the cap kept apart from icons, the
/// order the renderer sees, and how shapes go away with layers, surfaces and
/// the end of a stay in the world.
/// </summary>
public sealed class PluginWorldMarkerStoreShapeTests
{
    private static readonly PluginColor Red = new(220, 60, 60, 120);
    private static readonly PluginMarkerAnchor One = PluginMarkerAnchor.Object(1u);

    private static PluginGroundShape RingUnder(uint objectId) =>
        PluginGroundShape.Ring(PluginMarkerAnchor.Object(objectId), 2f, 0.2f, Red);

    private static PluginWorldIcon IconOver(uint objectId) =>
        new(PluginMarkerAnchor.Object(objectId), new PluginImage(1, 32, 32));

    private static IPluginWorldMarkerLayer Layer(PluginWorldMarkerStore store, string owner = "a.plugin") =>
        store.For(owner).CreateLayer()!;

    [Fact]
    public void TheSetIsCopiedSoTheListMayBeReused()
    {
        var store = new PluginWorldMarkerStore();
        var list = new List<PluginGroundShape> { RingUnder(1u) };

        Assert.True(Layer(store).SetShapes(list));
        list.Add(RingUnder(2u));

        Assert.Single(store.CaptureShapes());
    }

    [Fact]
    public void InvalidShapesAreDroppedAndTheRestShown()
    {
        var store = new PluginWorldMarkerStore();
        var badPosition = new PluginNavigationPosition(1u, double.NaN, 0, 0, 0f, true);

        Assert.True(Layer(store).SetShapes(
        [
            RingUnder(1u) with { Anchor = default },
            RingUnder(0u),
            PluginGroundShape.Disc(PluginMarkerAnchor.At(badPosition), 2f, Red),
            RingUnder(1u) with { Kind = PluginGroundShapeKind.None },
            RingUnder(1u) with { Kind = (PluginGroundShapeKind)99 },
            PluginGroundShape.Disc(One, 0.05f, Red),
            PluginGroundShape.Disc(One, 100.5f, Red),
            PluginGroundShape.Disc(One, float.NaN, Red),
            PluginGroundShape.Ring(One, 2f, 0f, Red),
            PluginGroundShape.Ring(One, 2f, -0.1f, Red),
            PluginGroundShape.Ring(One, 2f, 2.5f, Red),
            PluginGroundShape.Ring(One, 2f, float.PositiveInfinity, Red),
            PluginGroundShape.Arc(One, 2f, 0f, 0f, filled: true, Red),
            PluginGroundShape.Arc(One, 2f, 0f, -90f, filled: true, Red),
            PluginGroundShape.Arc(One, 2f, 0f, 361f, filled: true, Red),
            PluginGroundShape.Arc(One, 2f, float.NaN, 90f, filled: true, Red),
            PluginGroundShape.Arc(One, 2f, 0f, float.NaN, filled: true, Red),
            PluginGroundShape.Arc(One, 2f, 0f, 90f, filled: false, Red, width: 3f),
            RingUnder(7u),
        ]));

        PluginGroundShape only = Assert.Single(store.CaptureShapes());
        Assert.Equal(7u, only.Anchor.ObjectId);
    }

    [Fact]
    public void TheLimitsThemselvesAreAccepted()
    {
        var store = new PluginWorldMarkerStore();

        Assert.True(Layer(store).SetShapes(
        [
            PluginGroundShape.Disc(One, PluginGroundShape.MinimumRadius, Red),
            PluginGroundShape.Disc(One, PluginGroundShape.MaximumRadius, Red),
            PluginGroundShape.Ring(One, 2f, 2f, Red),
            PluginGroundShape.Arc(One, 2f, 0f, 360f, filled: true, Red),
        ]));

        Assert.Equal(4, store.CaptureShapes().Count);
    }

    [Fact]
    public void RingsAndDiscsBecomeFullTurnsFromNorthAndFilledShapesLoseTheirWidth()
    {
        var store = new PluginWorldMarkerStore();

        Layer(store).SetShapes(
        [
            RingUnder(1u) with { StartDegrees = 30f, SweepDegrees = 10f, Filled = true, FacesObject = true },
            PluginGroundShape.Disc(One, 3f, Red) with { Width = 1f, StartDegrees = 30f, SweepDegrees = 10f, Filled = false },
            PluginGroundShape.Arc(One, 5f, 30f, 90f, filled: true, Red, width: 1f),
            PluginGroundShape.Arc(One, 5f, 30f, 90f, filled: false, Red, width: 1f),
        ]);

        IReadOnlyList<PluginGroundShape> shapes = store.CaptureShapes();
        Assert.Equal((0f, 360f, false, false, 0.2f),
            (shapes[0].StartDegrees, shapes[0].SweepDegrees, shapes[0].Filled, shapes[0].FacesObject, shapes[0].Width));
        Assert.Equal((0f, 360f, true, 0f),
            (shapes[1].StartDegrees, shapes[1].SweepDegrees, shapes[1].Filled, shapes[1].Width));
        Assert.Equal((30f, 90f, true, 0f),
            (shapes[2].StartDegrees, shapes[2].SweepDegrees, shapes[2].Filled, shapes[2].Width));
        Assert.Equal((30f, 90f, false, 1f),
            (shapes[3].StartDegrees, shapes[3].SweepDegrees, shapes[3].Filled, shapes[3].Width));
    }

    [Fact]
    public void AListClaimingAbsurdlyManyShapesIsRefusedWithoutBeingCopied()
    {
        var store = new PluginWorldMarkerStore();
        IPluginWorldMarkerLayer layer = Layer(store);
        Assert.True(layer.SetShapes([RingUnder(1u)]));

        // Refused on its Count alone: the fake throws if anything reads it.
        Assert.False(layer.SetShapes(new HugeList()));

        Assert.Single(store.CaptureShapes());
    }

    private sealed class HugeList : IReadOnlyList<PluginGroundShape>
    {
        public int Count => int.MaxValue;
        public PluginGroundShape this[int index] => throw new InvalidOperationException();
        public IEnumerator<PluginGroundShape> GetEnumerator() => throw new InvalidOperationException();
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => throw new InvalidOperationException();
    }

    [Fact]
    public void TheCapCountsEveryLayerOfThePluginAndRefusesTheWholeSet()
    {
        var store = new PluginWorldMarkerStore();
        IPluginWorldMarkerLayer first = Layer(store);
        IPluginWorldMarkerLayer second = Layer(store);
        Assert.True(first.SetShapes(Enumerable.Repeat(RingUnder(1u), 200).ToArray()));
        Assert.True(second.SetShapes([RingUnder(2u)]));

        Assert.False(second.SetShapes(Enumerable.Repeat(RingUnder(3u), 57).ToArray()));
        Assert.Equal(201, store.CaptureShapes().Count);
        Assert.Equal(2u, store.CaptureShapes()[200].Anchor.ObjectId);

        Assert.True(second.SetShapes(Enumerable.Repeat(RingUnder(3u), 56).ToArray()));
        Assert.Equal(IPluginWorldMarkers.MaximumShapes, store.CaptureShapes().Count);
    }

    [Fact]
    public void IconsAndShapesAreCappedApart()
    {
        var store = new PluginWorldMarkerStore();
        IPluginWorldMarkerLayer layer = Layer(store);

        Assert.True(layer.SetIcons(Enumerable.Repeat(IconOver(1u), IPluginWorldMarkers.MaximumIcons).ToArray()));
        Assert.True(layer.SetShapes(Enumerable.Repeat(RingUnder(1u), IPluginWorldMarkers.MaximumShapes).ToArray()));
    }

    [Fact]
    public void SettingShapesLeavesTheLayersIconsAloneAndTheOtherWayRound()
    {
        var store = new PluginWorldMarkerStore();
        IPluginWorldMarkerLayer layer = Layer(store);
        layer.SetIcons([IconOver(1u)]);
        layer.SetShapes([RingUnder(2u)]);

        layer.SetShapes([RingUnder(3u)]);
        Assert.Single(store.CaptureIcons());

        layer.SetIcons([]);
        Assert.Equal(3u, Assert.Single(store.CaptureShapes()).Anchor.ObjectId);
    }

    [Fact]
    public void TheRendererSeesShapesByPluginThenLayerThenTheOrderGiven()
    {
        var store = new PluginWorldMarkerStore();
        IPluginWorldMarkerLayer bFirst = Layer(store, "b.plugin");
        IPluginWorldMarkerLayer aFirst = Layer(store, "a.plugin");
        IPluginWorldMarkerLayer aSecond = Layer(store, "a.plugin");
        bFirst.SetShapes([RingUnder(10u)]);
        aSecond.SetShapes([RingUnder(30u), RingUnder(31u)]);
        aFirst.SetShapes([RingUnder(20u)]);

        Assert.Equal(new[] { 20u, 30u, 31u, 10u }, store.CaptureShapes().Select(s => s.Anchor.ObjectId));
    }

    [Fact]
    public void TheShapeSnapshotIsReusedUntilSomethingChanges()
    {
        var store = new PluginWorldMarkerStore();
        IPluginWorldMarkerLayer layer = Layer(store);
        layer.SetShapes([RingUnder(1u)]);

        IReadOnlyList<PluginGroundShape> first = store.CaptureShapes();
        Assert.Same(first, store.CaptureShapes());

        layer.SetShapes([RingUnder(2u)]);
        Assert.NotSame(first, store.CaptureShapes());
    }

    [Fact]
    public void DisposingALayerTakesItsShapesAndRefusesFurtherUse()
    {
        var store = new PluginWorldMarkerStore();
        IPluginWorldMarkerLayer layer = Layer(store);
        layer.SetShapes([RingUnder(1u)]);

        layer.Dispose();

        Assert.Empty(store.CaptureShapes());
        Assert.Throws<ObjectDisposedException>(() => layer.SetShapes([RingUnder(2u)]));
    }

    [Fact]
    public void DisposingAPluginsSurfaceTakesItsShapes()
    {
        var store = new PluginWorldMarkerStore();
        IPluginWorldMarkers markers = store.For("a.plugin");
        markers.CreateLayer()!.SetShapes([RingUnder(1u)]);

        ((IDisposable)markers).Dispose();

        Assert.Empty(store.CaptureShapes());
    }

    [Fact]
    public void ClearingEmptiesShapesButTheLayersStayUsable()
    {
        var store = new PluginWorldMarkerStore();
        IPluginWorldMarkerLayer layer = Layer(store);
        layer.SetShapes([RingUnder(1u)]);

        store.Clear();

        Assert.Empty(store.CaptureShapes());
        Assert.True(layer.SetShapes([RingUnder(2u)]));
        Assert.Single(store.CaptureShapes());
    }

    [Fact]
    public void LeavingTheWorldClearsShapes()
    {
        var store = new PluginWorldMarkerStore();
        var events = new WorldEvents();
        Layer(store).SetShapes([RingUnder(1u)]);
        using IDisposable subscription = store.ClearOn(events);

        events.FireLogoff();

        Assert.Empty(store.CaptureShapes());
    }
}
