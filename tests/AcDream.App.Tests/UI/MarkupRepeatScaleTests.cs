using System.Diagnostics;
using AcDream.App.UI;
using AcDream.App.UI.Markup;

namespace AcDream.App.Tests.UI;

/// <summary>
/// R10: 150 copies of a five-control row in a scrolling column build, reconcile
/// and lay out within bounds, and an unchanged tick of the repeat allocates
/// nothing. Timing runs alone so other tests cannot disturb it.
/// </summary>
[Collection(MarkupRepeatConsoleCollection.Name)]
public sealed class MarkupRepeatScaleTests
{
    // Least of five rounds, Release, this machine, 2026-10-22 (later runs 6.2 / 0.45 / 2.5):
    //   build 150 rows        7.6 ms
    //   unchanged frame       0.55 ms  (a tick plus a recorded draw, which lays out)
    //   shuffle all 150 keys  2.9 ms
    // Each bound is 4x the measured value.
    private const double BuildBoundMs = 30.4;
    private const double TickBoundMs = 2.2;
    private const double ShuffleBoundMs = 11.6;

    private const int RowCount = 150;

    private sealed class Row
    {
        public string Id { get; set; } = "";
        public bool On { get; set; }
        public int Value { get; set; }
        public string Label { get; set; } = "";
        public IReadOnlyList<string> Choices { get; } = ["One", "Two", "Three"];
        public string Picked { get; set; } = "One";
        public Action<bool> SetOn => v => On = v;
        public Action<int> SetValue => v => Value = v;
        public Action<string> Pick => v => Picked = v;
        public Action Press => () => { };
    }

    private sealed class Binding
    {
        public IReadOnlyList<Row> Rows { get; set; } = [];
    }

    private const string Body = """
        <group layout="column" scroll="y" gap="2" grow="1">
          <repeat items="{Rows}" key="{Id}">
            <group layout="row" gap="6" h="22">
              <checkbox checked="{On}" onchange="{SetOn}" w="20"/>
              <label text="{Label}" grow="1"/>
              <menu items="{Choices}" selected="{Picked}" onchange="{Pick}" w="80"/>
              <number value="{Value}" onchange="{SetValue}" w="60"/>
              <button text="Go" onclick="{Press}" w="40"/>
            </group>
          </repeat>
        </group>
        """;

    private static Row[] MakeRows() =>
        Enumerable.Range(0, RowCount).Select(i => new Row { Id = "r" + i, Label = "Row " + i, Value = i }).ToArray();

    private sealed class Rig
    {
        private long _now;
        public UiRoot Root = null!;
        public UiMarkupRepeat Marker = null!;
        public void Frame()
        {
            Root.Tick(0.016, _now += 16);
            ThemeDrawCapture.Draw(Root);
        }
    }

    // Binding must expose Choices as items for the menu; Picked is the selection.
    private static Rig Build(Binding binding)
    {
        var window = MarkupDocument.BuildWindow(
            $"<panel x=\"0\" y=\"0\" w=\"400\" h=\"300\" layout=\"column\">{Body}</panel>",
            binding, _ => (0u, 0, 0), fallbackTitle: "Scale");
        var root = new UiRoot { Width = 800, Height = 600 };
        root.AddChild(window.Frame);
        var rig = new Rig { Root = root };
        rig.Frame();
        var scroll = FindMarkerParent(window.ContentRoot!);
        rig.Marker = scroll.Children.OfType<UiMarkupRepeat>().Single();
        return rig;
    }

    private static UiElement FindMarkerParent(UiElement element)
    {
        if (element.Children.OfType<UiMarkupRepeat>().Any()) return element;
        foreach (var child in element.Children)
        {
            var found = FindMarkerParentOrNull(child);
            if (found is not null) return found;
        }
        throw new InvalidOperationException("no repeat marker");
    }

    private static UiElement? FindMarkerParentOrNull(UiElement element)
    {
        if (element.Children.OfType<UiMarkupRepeat>().Any()) return element;
        foreach (var child in element.Children)
        {
            var found = FindMarkerParentOrNull(child);
            if (found is not null) return found;
        }
        return null;
    }

    private static double Ms(long ticks) => ticks * 1000.0 / Stopwatch.Frequency;

    [Fact]
    [Trait("Lane", "Timing")]
    public void A_150_row_repeat_builds_reconciles_and_lays_out_within_bounds()
    {
        double build = double.MaxValue, tick = double.MaxValue, shuffle = double.MaxValue;
        var random = new Random(12345);
        for (int round = 0; round < 5; round++)
        {
            var binding = new Binding { Rows = MakeRows() };
            long t0 = Stopwatch.GetTimestamp();
            var rig = Build(binding);
            build = Math.Min(build, Ms(Stopwatch.GetTimestamp() - t0));
            Assert.Equal(RowCount, rig.Marker.Copies.Count);

            rig.Frame();
            t0 = Stopwatch.GetTimestamp();
            rig.Frame();
            tick = Math.Min(tick, Ms(Stopwatch.GetTimestamp() - t0));

            var shuffled = binding.Rows.OrderBy(_ => random.Next()).ToArray();
            binding.Rows = shuffled;
            t0 = Stopwatch.GetTimestamp();
            rig.Frame();
            shuffle = Math.Min(shuffle, Ms(Stopwatch.GetTimestamp() - t0));
            Assert.Equal(RowCount, rig.Marker.Copies.Count);
        }

        Assert.True(build <= BuildBoundMs, $"build {build:F2} ms");
        Assert.True(tick <= TickBoundMs, $"tick {tick:F2} ms");
        Assert.True(shuffle <= ShuffleBoundMs, $"shuffle {shuffle:F2} ms");
    }

    [Fact]
    public void An_unchanged_tick_of_150_rows_allocates_nothing()
    {
        var rig = Build(new Binding { Rows = MakeRows() });
        for (int i = 0; i < 5; i++) rig.Marker.TickSelfAndChildren(0.016);

        long least = long.MaxValue;
        for (int round = 0; round < 5; round++)
        {
            long before = GC.GetAllocatedBytesForCurrentThread();
            rig.Marker.TickSelfAndChildren(0.016);
            least = Math.Min(least, GC.GetAllocatedBytesForCurrentThread() - before);
        }
        Assert.Equal(0L, least);
    }
}
