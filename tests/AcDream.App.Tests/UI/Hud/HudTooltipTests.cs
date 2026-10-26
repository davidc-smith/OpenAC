using AcDream.App.UI.Hud;

namespace AcDream.App.Tests.UI.Hud;

public sealed class HudTooltipTests
{
    private static float SixPerChar(string s) => s.Length * 6f;

    private static readonly HudTooltipContent Long = new(
        "Auto-target", "ON",
        "Pick the nearest enemy when you attack with nothing selected.", "Click to toggle");

    [Fact]
    public void CardWrapsALongDetail()
    {
        var (w, h, lines) = HudTooltip.Measure(Long, HudTooltipStyle.Card, SixPerChar, 14f);

        Assert.True(lines.Count >= 2);
        foreach (string line in lines)
            Assert.True(SixPerChar(line) <= HudTooltip.WrapWidth, line);
        Assert.Equal(Long.Detail, string.Join(" ", lines));
        Assert.True(w <= HudTooltip.WrapWidth + 2 * 11f + 0.01f);
        Assert.True(h >= 14f * (2 + lines.Count));
    }

    [Fact]
    public void CompactIsOneLineWithNoDetail()
    {
        var (w, h, lines) = HudTooltip.Measure(Long, HudTooltipStyle.Compact, SixPerChar, 14f);

        Assert.Empty(lines);
        Assert.Equal(14f + 2 * 5f, h, 3);
        Assert.Equal(SixPerChar("Auto-target") + 6f + SixPerChar("ON") + 2 * 9f, w, 3);
    }

    [Fact]
    public void PlacedCentredAboveWithAGap()
    {
        var anchor = new DeckRect(100f, 50f, 20f, 10f);
        var (x, y, below) = HudTooltip.Place(anchor, 60f, 30f, screenLeft: 500f, screenTop: 600f, screenWidth: 1920f);

        Assert.False(below);
        Assert.Equal(80f, x, 3);
        Assert.Equal(50f - 8f - 30f, y, 3);
    }

    [Fact]
    public void PlacedBelowNearTheScreenTop()
    {
        // 30 tall plus the gap does not fit in the 20 points above the anchor.
        var anchor = new DeckRect(100f, 20f, 20f, 10f);
        var (_, y, below) = HudTooltip.Place(anchor, 60f, 30f, screenLeft: 500f, screenTop: 0f, screenWidth: 1920f);

        Assert.True(below);
        Assert.Equal(20f + 10f + 8f, y, 3);
    }

    [Fact]
    public void ClampedAtTheScreenEdges()
    {
        var anchor = new DeckRect(0f, 50f, 20f, 10f);
        var (left, _, _) = HudTooltip.Place(anchor, 100f, 30f, screenLeft: 0f, screenTop: 600f, screenWidth: 1920f);
        Assert.Equal(4f, left, 3);    // screen x 4

        var (right, _, _) = HudTooltip.Place(anchor, 100f, 30f, screenLeft: 1900f, screenTop: 600f, screenWidth: 1920f);
        Assert.Equal(1920f - 4f - 100f - 1900f, right, 3);

        // No screen width: no horizontal clamp.
        var (free, _, _) = HudTooltip.Place(anchor, 100f, 30f, screenLeft: 1900f, screenTop: 600f, screenWidth: null);
        Assert.Equal(-40f, free, 3);
    }

    [Fact]
    public void LayoutCacheMeasuresOnlyWhenTheContentOrStyleChanges()
    {
        var cache = new HudTooltipLayoutCache();
        int calls = 0;
        float Counting(string t) { calls++; return SixPerChar(t); }

        var first = cache.Get(Long, HudTooltipStyle.Card, null, Counting, 14f);
        int afterFirst = calls;
        var again = cache.Get(Long with { }, HudTooltipStyle.Card, null, Counting, 14f);
        Assert.Equal(afterFirst, calls);
        Assert.Equal(first, again);
        Assert.Equal(HudTooltip.Measure(Long, HudTooltipStyle.Card, SixPerChar, 14f).H, first.H);

        cache.Get(Long, HudTooltipStyle.Compact, null, Counting, 14f);
        Assert.True(calls > afterFirst);
        int afterStyle = calls;
        cache.Get(Long with { State = "OFF" }, HudTooltipStyle.Compact, null, Counting, 14f);
        Assert.True(calls > afterStyle);
    }
}
