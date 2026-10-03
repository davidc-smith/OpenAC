using AcDream.App.Plugins;
using AcDream.Core.Plugins;
using AcDream.Plugin.Abstractions;

namespace AcDream.App.Tests.Plugins;

/// <summary>
/// What the store takes from a plugin and what it hands the overlay: the
/// drop rules, the cap across a plugin's layers, the order the overlay sees,
/// and how layers, surfaces and the whole set go away.
/// </summary>
public sealed class PluginWorldMarkerStoreTests
{
    private static readonly PluginImage Image = new(1, 32, 32);

    private static PluginWorldIcon Over(uint objectId, PluginImage? image = null) =>
        new(PluginMarkerAnchor.Object(objectId), image ?? Image);

    private static IPluginWorldMarkerLayer Layer(PluginWorldMarkerStore store, string owner = "a.plugin") =>
        store.For(owner).CreateLayer()!;

    [Fact]
    public void TheSetIsCopiedSoTheListMayBeReused()
    {
        var store = new PluginWorldMarkerStore();
        var list = new List<PluginWorldIcon> { Over(1u) };

        Assert.True(Layer(store).SetIcons(list));
        list.Add(Over(2u));

        Assert.Single(store.CaptureIcons());
    }

    [Fact]
    public void InvalidIconsAreDroppedAndTheRestShown()
    {
        var store = new PluginWorldMarkerStore();
        var badPosition = new PluginNavigationPosition(1u, double.NaN, 0, 0, 0f, true);

        Assert.True(Layer(store).SetIcons(
        [
            new PluginWorldIcon(default, Image),
            Over(0u),
            new PluginWorldIcon(PluginMarkerAnchor.At(badPosition), Image),
            Over(1u, PluginImage.None),
            Over(1u) with { SizePixels = float.NaN },
            Over(1u) with { MaxRange = 0f },
            Over(1u) with { MaxRange = -5f },
            Over(1u) with { MaxRange = float.PositiveInfinity },
            Over(7u),
        ]));

        WorldIconEntry only = Assert.Single(store.CaptureIcons());
        Assert.Equal(7u, only.Icon.Anchor.ObjectId);
    }

    [Fact]
    public void SizesAreClampedIntoTheDrawableRange()
    {
        var store = new PluginWorldMarkerStore();

        Layer(store).SetIcons([Over(1u) with { SizePixels = 4f }, Over(2u) with { SizePixels = 500f }]);

        IReadOnlyList<WorldIconEntry> icons = store.CaptureIcons();
        Assert.Equal(PluginWorldIcon.MinimumSize, icons[0].Icon.SizePixels);
        Assert.Equal(PluginWorldIcon.MaximumSize, icons[1].Icon.SizePixels);
    }

    [Fact]
    public void TheCapCountsEveryLayerOfThePluginAndRefusesTheWholeSet()
    {
        var store = new PluginWorldMarkerStore();
        IPluginWorldMarkerLayer first = Layer(store);
        IPluginWorldMarkerLayer second = Layer(store);
        Assert.True(first.SetIcons(Enumerable.Repeat(Over(1u), 200).ToArray()));
        Assert.True(second.SetIcons([Over(2u)]));

        Assert.False(second.SetIcons(Enumerable.Repeat(Over(3u), 57).ToArray()));
        Assert.Equal(201, store.CaptureIcons().Count);
        Assert.Equal(2u, store.CaptureIcons()[200].Icon.Anchor.ObjectId);

        Assert.True(second.SetIcons(Enumerable.Repeat(Over(3u), 56).ToArray()));
        Assert.Equal(IPluginWorldMarkers.MaximumIcons, store.CaptureIcons().Count);
    }

    [Fact]
    public void AnotherPluginsIconsDoNotCountAgainstTheCap()
    {
        var store = new PluginWorldMarkerStore();
        Assert.True(Layer(store, "a.plugin").SetIcons(Enumerable.Repeat(Over(1u), 256).ToArray()));

        Assert.True(Layer(store, "b.plugin").SetIcons(Enumerable.Repeat(Over(2u), 256).ToArray()));
    }

    [Fact]
    public void SettingIconsReplacesTheLayersSet()
    {
        var store = new PluginWorldMarkerStore();
        IPluginWorldMarkerLayer layer = Layer(store);

        layer.SetIcons([Over(1u), Over(2u)]);
        layer.SetIcons([Over(3u)]);

        Assert.Equal(3u, Assert.Single(store.CaptureIcons()).Icon.Anchor.ObjectId);
    }

    [Fact]
    public void TheOverlaySeesIconsByPluginThenLayerThenTheOrderGiven()
    {
        var store = new PluginWorldMarkerStore();
        IPluginWorldMarkerLayer bFirst = Layer(store, "b.plugin");
        IPluginWorldMarkerLayer aFirst = Layer(store, "a.plugin");
        IPluginWorldMarkerLayer aSecond = Layer(store, "a.plugin");
        bFirst.SetIcons([Over(10u)]);
        aSecond.SetIcons([Over(30u), Over(31u)]);
        aFirst.SetIcons([Over(20u)]);

        IReadOnlyList<WorldIconEntry> icons = store.CaptureIcons();

        Assert.Equal(new[] { "a.plugin", "a.plugin", "a.plugin", "b.plugin" }, icons.Select(e => e.OwnerId));
        Assert.Equal(new[] { 20u, 30u, 31u, 10u }, icons.Select(e => e.Icon.Anchor.ObjectId));
    }

    [Fact]
    public void TheSnapshotIsReusedUntilSomethingChanges()
    {
        var store = new PluginWorldMarkerStore();
        IPluginWorldMarkerLayer layer = Layer(store);
        layer.SetIcons([Over(1u)]);

        IReadOnlyList<WorldIconEntry> first = store.CaptureIcons();
        Assert.Same(first, store.CaptureIcons());

        layer.SetIcons([Over(2u)]);
        Assert.NotSame(first, store.CaptureIcons());
    }

    [Fact]
    public void DisposingALayerTakesItsIconsAndRefusesFurtherUse()
    {
        var store = new PluginWorldMarkerStore();
        IPluginWorldMarkerLayer layer = Layer(store);
        layer.SetIcons([Over(1u)]);

        layer.Dispose();
        layer.Dispose();

        Assert.Empty(store.CaptureIcons());
        Assert.Throws<ObjectDisposedException>(() => layer.SetIcons([Over(2u)]));
    }

    [Fact]
    public void DisposingAPluginsSurfaceTakesEveryLayerAndTheNextAskIsFresh()
    {
        var store = new PluginWorldMarkerStore();
        IPluginWorldMarkers markers = store.For("a.plugin");
        IPluginWorldMarkerLayer layer = markers.CreateLayer()!;
        layer.SetIcons([Over(1u)]);

        ((IDisposable)markers).Dispose();

        Assert.Empty(store.CaptureIcons());
        Assert.Throws<ObjectDisposedException>(() => markers.CreateLayer());
        Assert.Throws<ObjectDisposedException>(() => layer.SetIcons([Over(2u)]));
        Assert.NotSame(markers, store.For("a.plugin"));
    }

    [Fact]
    public void TheSameOwnerGetsTheSameSurface()
    {
        var store = new PluginWorldMarkerStore();

        Assert.Same(store.For("a.plugin"), store.For("a.plugin"));
        Assert.NotSame(store.For("a.plugin"), store.For("b.plugin"));
    }

    [Fact]
    public void ClearingEmptiesEveryLayerButTheLayersStayUsable()
    {
        var store = new PluginWorldMarkerStore();
        IPluginWorldMarkerLayer layer = Layer(store);
        layer.SetIcons([Over(1u)]);
        Layer(store, "b.plugin").SetIcons([Over(2u)]);

        store.Clear();

        Assert.Empty(store.CaptureIcons());
        Assert.True(layer.SetIcons([Over(3u)]));
        Assert.Single(store.CaptureIcons());
    }

    [Fact]
    public void LeavingTheWorldClearsTheStoreUntilTheSubscriptionIsDisposed()
    {
        var store = new PluginWorldMarkerStore();
        var events = new WorldEvents();
        IPluginWorldMarkerLayer layer = Layer(store);
        layer.SetIcons([Over(1u)]);
        IDisposable subscription = store.ClearOn(events);

        events.FireLogoff();
        Assert.Empty(store.CaptureIcons());

        layer.SetIcons([Over(2u)]);
        subscription.Dispose();
        events.FireLogoff();
        Assert.Single(store.CaptureIcons());
    }
}
