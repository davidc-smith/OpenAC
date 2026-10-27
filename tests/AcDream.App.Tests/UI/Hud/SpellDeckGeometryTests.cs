using AcDream.App.UI.Hud;

namespace AcDream.App.Tests.UI.Hud;

public sealed class SpellDeckGeometryTests
{
    [Fact]
    public void Capacity_AtTheStretchEnds()
    {
        Assert.Equal(12, SpellDeckGeometry.Capacity(1f));
        Assert.Equal(27, SpellDeckGeometry.Capacity(2f));
        Assert.Equal(27, SpellDeckGeometry.MaxCapacity);
    }

    [Theory]
    [InlineData(1f, 1f, 388f)]
    [InlineData(1f, 1.5f, 598f)]
    [InlineData(1f, 2f, 808f)]
    [InlineData(0.75f, 1f, 291f)]
    [InlineData(1.5f, 2f, 1212f)]
    public void Width_IsTheBaseWidthPlusTheStretch(float scale, float stretch, float width) =>
        Assert.Equal(width, SpellDeckGeometry.Create(scale, stretch, 1).Width, 3);

    [Fact]
    public void StretchFor_IsTheSmallestStretchThatFits()
    {
        Assert.Equal(1f, SpellDeckGeometry.StretchFor(12));
        for (int n = 1; n <= 27; n++)
            Assert.True(SpellDeckGeometry.Capacity(SpellDeckGeometry.StretchFor(n)) >= n, $"n={n}");
        for (int n = 13; n <= 27; n++)
            Assert.True(SpellDeckGeometry.Capacity(SpellDeckGeometry.StretchFor(n) - 0.01f) < n, $"n={n}");
        Assert.Equal(2f, SpellDeckGeometry.StretchFor(40));
    }

    [Theory]
    [InlineData(1, 1f, 58f)]
    [InlineData(3, 1f, 114f)]
    [InlineData(10, 1.5f, 465f)]
    public void Height_IsTheStripPlusTheRows(int rows, float scale, float height) =>
        Assert.Equal(height, SpellDeckGeometry.Create(scale, 1f, rows).Height, 3);

    [Theory]
    [InlineData(0.75f, 1f)] [InlineData(1f, 1f)] [InlineData(1.5f, 2f)]
    public void TheRowsSitBelowTheTabStrip(float scale, float stretch)
    {
        var g = SpellDeckGeometry.Create(scale, stretch, 3);
        DeckRect first = g.Slot(0, 0), last = g.Slot(2, g.SlotsPerRow - 1);
        Assert.Equal(46f * scale, first.X, 3);
        Assert.Equal(25f * scale, first.Y, 3);   // TabStrip 18 + TopPad 7
        Assert.Equal(g.Height - 7f * scale, last.Y + last.H, 3);
        Assert.True(last.X + last.W <= g.Width - 6f * scale + 1e-3f);
        DeckRect mark = g.Bookmark(0, 8);
        Assert.True(mark.Y >= 0f && mark.Y + mark.H <= first.Y);
    }

    [Theory]
    [InlineData(1)] [InlineData(3)] [InlineData(10)]
    public void ItemSlot_IsCentredOnTheRowsInTheGutter(int rows)
    {
        var g = SpellDeckGeometry.Create(1f, 1f, rows);
        DeckRect item = g.ItemSlot, top = g.Slot(0, 0), bottom = g.Slot(rows - 1, 0);
        Assert.Equal((top.Y + bottom.Y + bottom.H) / 2f, item.Y + item.H / 2f, 3);
        Assert.Equal(6f + 20f, item.X + item.W / 2f, 3);
    }

    [Theory]
    [InlineData(1f, 8)] [InlineData(1f, 11)] [InlineData(2f, 16)]
    public void Bookmarks_Are24WideWith2GapWhenTheyFit(float stretch, int count)
    {
        var g = SpellDeckGeometry.Create(1f, stretch, 1);
        for (int i = 0; i < count; i++)
        {
            DeckRect b = g.Bookmark(i, count);
            Assert.Equal(24f, b.W, 3);
            Assert.Equal(46f + i * 26f, b.X, 3);
        }
        if (count < SpellDeckTabs.MaxTabs)
        {
            DeckRect plus = g.Plus(count);
            Assert.Equal(46f + count * 26f, plus.X, 3);
            Assert.Equal(24f, plus.W, 3);
        }
    }

    [Theory]
    [InlineData(1f, 15)] [InlineData(1f, 16)] [InlineData(0.75f, 16)] [InlineData(1.5f, 14)]
    public void Bookmarks_ShrinkEvenlyToFitTheStrip(float scale, int count)
    {
        var g = SpellDeckGeometry.Create(scale, 1f, 1);
        int items = count < SpellDeckTabs.MaxTabs ? count + 1 : count;
        float right = g.Width - 6f * scale;
        DeckRect first = g.Bookmark(0, count);
        DeckRect last = count < SpellDeckTabs.MaxTabs ? g.Plus(count) : g.Bookmark(count - 1, count);
        Assert.InRange(first.W, 16f * scale - 1e-3f, 24f * scale + 1e-3f);
        Assert.True(first.W < 24f * scale);   // 13+ items do not fit 336 at 24 wide
        Assert.True(last.X + last.W <= right + 1e-3f);
        for (int i = 1; i < count; i++)
        {
            Assert.Equal(first.W, g.Bookmark(i, count).W, 3);
            Assert.Equal(first.W + 2f * scale, g.Bookmark(i, count).X - g.Bookmark(i - 1, count).X, 3);
        }
        Assert.Equal(items, Math.Round((last.X + last.W - first.X + 2f * scale) / (first.W + 2f * scale)));
    }

    [Theory]
    [InlineData(0.75f, 1f)] [InlineData(1f, 1f)] [InlineData(1.5f, 1f)]
    [InlineData(0.75f, 2f)] [InlineData(1f, 2f)] [InlineData(1.5f, 2f)]
    public void HitAt_FindsEachPart(float scale, float stretch)
    {
        var g = SpellDeckGeometry.Create(scale, stretch, 3);
        DeckRect s = g.Slot(2, 5), i = g.ItemSlot, b = g.Bookmark(9, 10), p = g.Plus(10);
        Assert.Equal(new SpellDeckHit(SpellDeckZone.Slot, 2, 5), g.HitAt(s.X + 1, s.Y + 1, 10));
        Assert.Equal(SpellDeckHit.ForBookmark(9), g.HitAt(b.X + 1, b.Y + b.H / 2, 10));
        Assert.Equal(new SpellDeckHit(SpellDeckZone.Plus, -1, -1), g.HitAt(p.X + p.W / 2, p.Y + p.H / 2, 10));
        Assert.Equal(new SpellDeckHit(SpellDeckZone.Item, -1, -1), g.HitAt(i.X + i.W / 2, i.Y + i.H / 2, 10));
        Assert.Equal(SpellDeckZone.Frame, g.HitAt(g.Width - 1, g.Height / 2, 10).Zone);
        Assert.Equal(SpellDeckZone.Frame, g.HitAt(p.X + p.W + 4f * scale, p.Y + p.H / 2, 10).Zone);
        Assert.Equal(SpellDeckZone.None, g.HitAt(-1, -1, 10).Zone);
    }

    [Fact]
    public void HitAt_HasNoPlusAt16Tabs()
    {
        var g = SpellDeckGeometry.Create(1f, 2f, 1);   // wide enough that the plus would be on the strip
        DeckRect last = g.Bookmark(15, 16), plus = g.Plus(16);
        Assert.True(plus.X + plus.W < g.TabStripBand.X + g.TabStripBand.W);
        Assert.Equal(SpellDeckHit.ForBookmark(15), g.HitAt(last.X + 1, last.Y + 1, 16));
        Assert.Equal(SpellDeckZone.Frame, g.HitAt(plus.X + plus.W / 2, plus.Y + plus.H / 2, 16).Zone);
    }

    [Theory]
    [InlineData(0.75f, 1f)] [InlineData(1f, 1.5f)] [InlineData(1.5f, 2f)]
    public void FromSize_RoundTrips(float scale, float stretch)
    {
        var g = SpellDeckGeometry.Create(scale, stretch, 4);
        var back = SpellDeckGeometry.FromSize(g.Width, g.Height, 4);
        Assert.Equal(g.Scale, back.Scale);
        Assert.Equal(g.Stretch, back.Stretch, 3);
        Assert.Equal(g.Stretch, g.WithWidth(g.Width).Stretch, 3);
    }

    [Fact]
    public void WithCornerSize_ScalesFromTheWidthAtTheSameStretch()
    {
        var g = SpellDeckGeometry.Create(1f, 1.5f, 2);
        var bigger = g.WithCornerSize(g.Width * 1.2f, g.Height);
        Assert.Equal(1.2f, bigger.Scale, 3);
        Assert.Equal(1.5f, bigger.Stretch, 3);
    }
}
