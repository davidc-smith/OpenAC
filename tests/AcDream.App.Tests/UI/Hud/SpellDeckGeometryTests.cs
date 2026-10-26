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

    [Fact]
    public void StretchFor_IsTheSmallestStretchThatFits()
    {
        Assert.Equal(1f, SpellDeckGeometry.StretchFor(12));
        for (int n = 1; n <= 27; n++)
            Assert.True(SpellDeckGeometry.Capacity(SpellDeckGeometry.StretchFor(n)) >= n, $"n={n}");
        Assert.True(SpellDeckGeometry.Capacity(SpellDeckGeometry.StretchFor(20) - 0.01f) < 20);
        Assert.Equal(2f, SpellDeckGeometry.StretchFor(40));
    }

    [Theory]
    [InlineData(1, 1f, 40f)]
    [InlineData(3, 1f, 96f)]
    [InlineData(10, 1.5f, 438f)]
    public void Height_FollowsRowsAndScale(int rows, float scale, float height) =>
        Assert.Equal(height, SpellDeckGeometry.Create(scale, 1f, rows).Height, 3);

    [Theory]
    [InlineData(1)] [InlineData(3)] [InlineData(10)]
    public void ItemSlot_IsCentredVertically(int rows)
    {
        var g = SpellDeckGeometry.Create(1f, 1f, rows);
        DeckRect item = g.ItemSlot;
        Assert.Equal(g.Height / 2f, item.Y + item.H / 2f, 3);
    }

    [Theory]
    [InlineData(0.75f)] [InlineData(1.5f)]
    public void HitAt_FindsEachPart(float scale)
    {
        var g = SpellDeckGeometry.Create(scale, 1f, 3);
        DeckRect s = g.Slot(2, 5), c = g.TabChip(1), i = g.ItemSlot;
        Assert.Equal(new SpellDeckHit(SpellDeckZone.Slot, 2, 5), g.HitAt(s.X + 1, s.Y + 1));
        Assert.Equal(new SpellDeckHit(SpellDeckZone.TabChip, 1, -1), g.HitAt(c.X + 1, c.Y + 1));
        Assert.Equal(new SpellDeckHit(SpellDeckZone.Item, -1, -1), g.HitAt(i.X + i.W / 2, i.Y + i.H / 2));
        Assert.Equal(SpellDeckZone.Frame, g.HitAt(g.Width - 1, g.Height / 2).Zone);
        Assert.Equal(SpellDeckZone.None, g.HitAt(-1, -1).Zone);
    }

    [Theory]
    [InlineData(0.75f, 1f)] [InlineData(1f, 1.5f)] [InlineData(1.5f, 2f)]
    public void FromSize_RoundTrips(float scale, float stretch)
    {
        var g = SpellDeckGeometry.Create(scale, stretch, 4);
        var back = SpellDeckGeometry.FromSize(g.Width, g.Height, 4);
        Assert.Equal(g.Scale, back.Scale);
        Assert.Equal(g.Stretch, back.Stretch, 3);
    }
}
