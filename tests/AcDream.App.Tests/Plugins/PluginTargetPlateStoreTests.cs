using AcDream.App.Plugins;
using AcDream.Core.Targeting;
using AcDream.Plugin.Abstractions;
using Xunit;

namespace AcDream.App.Tests.Plugins;

public sealed class PluginTargetPlateStoreTests
{
    private sealed class P(Func<PluginTargetPlateQuery, PluginTargetPlateContent?> describe) : IPluginTargetPlateProvider
    {
        public int Calls;
        public List<(uint, string)> Actions = new();
        public PluginTargetPlateContent? Describe(PluginTargetPlateQuery q) { Calls++; return describe(q); }
        public void OnAction(uint objectId, string actionId) => Actions.Add((objectId, actionId));
    }

    private static PluginTargetPlateContent Rows(params string[] labels) =>
        new(labels.Select(l => new PluginTargetPlateRow(l, l + "v")).ToArray(), Array.Empty<PluginTargetPlateAction>());

    private static readonly PluginTargetPlateQuery Q = new(7, PluginTargetKind.Monster, "Drudge", 1, 45, false, true, 1f, null, null);

    [Fact]
    public void TwoProvidersShareTheTwoLineBudgetInRegistrationOrder()
    {
        var warnings = new List<string>();
        var store = new PluginTargetPlateStore(() => true, (id, msg) => warnings.Add($"{id}:{msg}"));
        store.For("a", "Alpha").AddProvider(new P(_ => Rows("a1", "a2")));
        store.For("b", "Beta").AddProvider(new P(_ => Rows("b1", "b2")));

        TargetPlatePluginContent c = store.Describe(Q);

        Assert.Equal(new[] { "a1", "b1" }, c.Rows.Select(r => r.Label));
        Assert.Equal("a2: a2v\nb2: b2v", c.OverflowTooltip);
        Assert.Empty(warnings);
    }

    [Fact]
    public void ExtraRowsAreDroppedWithOneWarning()
    {
        var warnings = new List<string>();
        var store = new PluginTargetPlateStore(() => true, (id, msg) => warnings.Add(id));
        var p = new P(_ => Rows("1", "2", "3"));
        store.For("a", "Alpha").AddProvider(p);
        Assert.Equal(2, store.Describe(Q).Rows.Count);
        store.Invalidate(7);
        store.Describe(Q);
        Assert.Single(warnings);
    }

    [Fact]
    public void AThrowingProviderIsSkippedAndLoggedOnce()
    {
        var warnings = new List<string>();
        var store = new PluginTargetPlateStore(() => true, (id, msg) => warnings.Add(id));
        store.For("bad", "Bad").AddProvider(new P(_ => throw new InvalidOperationException("boom")));
        store.For("good", "Good").AddProvider(new P(_ => Rows("g")));
        TargetPlatePluginContent c = store.Describe(Q);
        Assert.Equal(new[] { "g" }, c.Rows.Select(r => r.Label));
        Assert.Equal(new[] { "bad" }, warnings);
    }

    [Fact]
    public void ResultsAreCachedUntilRefreshed()
    {
        var store = new PluginTargetPlateStore(() => true, (_, _) => { });
        var p = new P(_ => Rows("x"));
        IPluginTargetPlate plate = store.For("a", "Alpha");
        plate.AddProvider(p);
        store.Describe(Q);
        store.Describe(Q);
        Assert.Equal(1, p.Calls);
        plate.Refresh(7);
        store.Describe(Q);
        Assert.Equal(2, p.Calls);
    }

    [Fact]
    public void InvalidateAllDropsTheCacheAndTheActionRoutes()
    {
        var store = new PluginTargetPlateStore(() => true, (_, _) => { });
        var p = new P(_ => new PluginTargetPlateContent(new[] { new PluginTargetPlateRow("x", "y") },
            new[] { new PluginTargetPlateAction("nav", "Navigate here") }));
        store.For("a", "Alpha").AddProvider(p);
        store.Describe(Q);
        store.InvalidateAll();
        store.RunAction("a", 7, "nav");
        Assert.Empty(p.Actions);
        store.Describe(Q);
        Assert.Equal(2, p.Calls);
    }

    [Fact]
    public void DisposingARegistrationRemovesItsRows()
    {
        var store = new PluginTargetPlateStore(() => true, (_, _) => { });
        IDisposable reg = store.For("a", "Alpha").AddProvider(new P(_ => Rows("x")));
        Assert.Single(store.Describe(Q).Rows);
        reg.Dispose();
        Assert.Empty(store.Describe(Q).Rows);
    }

    [Fact]
    public void ActionsAreGroupedByPluginAndRouted()
    {
        var store = new PluginTargetPlateStore(() => true, (_, _) => { });
        var p = new P(_ => new PluginTargetPlateContent(Array.Empty<PluginTargetPlateRow>(),
            new[] { new PluginTargetPlateAction("nav", "Navigate here") }));
        store.For("aethrix", "Aethrix").AddProvider(p);
        TargetPluginAction a = store.Describe(Q).Actions.Single();
        Assert.Equal(("aethrix", "Aethrix", "nav", "Navigate here"), (a.PluginId, a.PluginName, a.ActionId, a.Label));
        store.RunAction("aethrix", 7, "nav");
        Assert.Equal(new[] { (7u, "nav") }, p.Actions);
    }

    [Fact]
    public void AvailabilityFollowsTheDelegate()
    {
        bool on = false;
        var store = new PluginTargetPlateStore(() => on, (_, _) => { });
        Assert.False(store.For("a", "A").IsAvailable);
        on = true;
        Assert.True(store.For("a", "A").IsAvailable);
    }

    [Fact]
    public void ADisposedSurfaceIsReplacedByAFreshOneOnReload()
    {
        var store = new PluginTargetPlateStore(() => true, (_, _) => { });
        IPluginTargetPlate first = store.For("a", "Alpha");
        first.AddProvider(new P(_ => Rows("old")));
        ((IDisposable)first).Dispose();
        Assert.Empty(store.Describe(Q).Rows);

        IPluginTargetPlate second = store.For("a", "Alpha");
        Assert.NotSame(first, second);
        second.AddProvider(new P(_ => Rows("new")));
        Assert.Equal(new[] { "new" }, store.Describe(Q).Rows.Select(r => r.Label));
    }

    [Fact]
    public void ALoneProviderUsesBothLinesWithNoTooltip()
    {
        var store = new PluginTargetPlateStore(() => true, (_, _) => { });
        store.For("a", "Alpha").AddProvider(new P(_ => Rows("a1", "a2")));
        TargetPlatePluginContent c = store.Describe(Q);
        Assert.Equal(new[] { "a1", "a2" }, c.Rows.Select(r => r.Label));
        Assert.Null(c.OverflowTooltip);
    }

    [Fact]
    public void AnEmptyOrNullProviderDoesNotStopALoneProviderUsingBothLines()
    {
        var store = new PluginTargetPlateStore(() => true, (_, _) => { });
        store.For("a", "Alpha").AddProvider(new P(_ => Rows("a1", "a2")));
        store.For("b", "Beta").AddProvider(new P(_ => Rows()));
        store.For("c", "Gamma").AddProvider(new P(_ => null));
        TargetPlatePluginContent c = store.Describe(Q);
        Assert.Equal(new[] { "a1", "a2" }, c.Rows.Select(r => r.Label));
        Assert.Null(c.OverflowTooltip);
    }

    [Fact]
    public void NullRowsAndActionsAreTreatedAsEmpty()
    {
        var store = new PluginTargetPlateStore(() => true, (_, _) => { });
        store.For("a", "Alpha").AddProvider(new P(_ => new PluginTargetPlateContent(null!, null!)));
        TargetPlatePluginContent c = store.Describe(Q);
        Assert.Empty(c.Rows);
        Assert.Empty(c.Actions);
    }

    [Fact]
    public void AnActionGoesOnlyToTheProviderThatOfferedIt()
    {
        var store = new PluginTargetPlateStore(() => true, (_, _) => { });
        PluginTargetPlateContent With(string id) => new(Array.Empty<PluginTargetPlateRow>(), new[] { new PluginTargetPlateAction(id, id) });
        var p1 = new P(_ => With("one"));
        var p2 = new P(_ => With("two"));
        IPluginTargetPlate plate = store.For("a", "Alpha");
        plate.AddProvider(p1);
        plate.AddProvider(p2);
        Assert.Equal(2, store.Describe(Q).Actions.Count);
        store.RunAction("a", 7, "one");
        store.RunAction("a", 7, "two");
        Assert.Equal(new[] { (7u, "one") }, p1.Actions);
        Assert.Equal(new[] { (7u, "two") }, p2.Actions);
    }

    [Fact]
    public void AddingAProviderToADisposedSurfaceRegistersNothing()
    {
        var store = new PluginTargetPlateStore(() => true, (_, _) => { });
        IPluginTargetPlate plate = store.For("a", "Alpha");
        ((IDisposable)plate).Dispose();
        IDisposable reg = plate.AddProvider(new P(_ => Rows("x")));
        reg.Dispose();
        Assert.Empty(store.Describe(Q).Rows);
    }
}
