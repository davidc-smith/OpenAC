using AcDream.App.UI.Hud;

namespace AcDream.App.Tests.UI.Hud;

public sealed class GearDeckGeometryTests
{
    private static GearDeckLayoutInput Input(bool folded = false, bool list = false, int worn = 12, int itemCount = 96, int sideSlots = 7) =>
        new(folded, list, worn, itemCount, sideSlots, ScrollRow: 0);

    [Fact]
    public void SectionsStackTopToBottom()
    {
        var g = GearDeckGeometry.Create(600, Input());
        Assert.True(g.Title.Y < g.EquipHeader.Y);
        Assert.True(g.EquipHeader.Y < g.Equipment.Y);
        Assert.True(g.Equipment.Y + g.Equipment.H <= g.PacksHeader.Y);
        Assert.True(g.PacksHeader.Y < g.BurdenBar.Y);
        Assert.True(g.BurdenBar.Y < g.PackArea.Y);
        Assert.True(g.PackArea.Y + g.PackArea.H <= g.Footer.Y);
        Assert.Equal(600, g.Frame.H);
        Assert.Equal(300, g.Frame.W);
    }

    [Fact]
    public void Folding_GivesRoomToPacks()
    {
        var open = GearDeckGeometry.Create(600, Input());
        var folded = GearDeckGeometry.Create(600, Input(folded: true));
        Assert.True(folded.PackArea.H > open.PackArea.H);
        Assert.True(folded.VisibleGridRows > open.VisibleGridRows);
    }

    [Theory]
    [InlineData(5, 1)]
    [InlineData(10, 1)]
    [InlineData(11, 2)]
    [InlineData(30, 3)]
    [InlineData(0, 1)]
    public void FoldedRowCount(int worn, int rows)
    {
        var g = GearDeckGeometry.Create(600, Input(folded: true, worn: worn));
        Assert.Equal(rows * 24 + (rows - 1) * 3, g.Equipment.H);
    }

    [Fact]
    public void GridCell_SixColumns_ScrolledOutIsNull()
    {
        var g = GearDeckGeometry.Create(600, Input());
        DeckRect a = g.GridCell(0, 0)!.Value, b = g.GridCell(5, 0)!.Value, c = g.GridCell(6, 0)!.Value;
        Assert.Equal(a.Y, b.Y);
        Assert.Equal(a.X, c.X);
        Assert.Equal(a.Y + 38, c.Y);
        Assert.Null(g.GridCell(0, scrollRow: 1));
    }

    [Fact]
    public void Hit_RoundTrips()
    {
        var g = GearDeckGeometry.Create(600, Input());
        DeckRect cell = g.GridCell(7, 0)!.Value;
        Assert.Equal(new GearDeckHit(GearZone.Item, 7), g.HitAt(cell.X + 2, cell.Y + 2));
        DeckRect slot = g.EquipSlot(3);
        Assert.Equal(new GearDeckHit(GearZone.EquipSlot, 3), g.HitAt(slot.X + 1, slot.Y + 1));
        DeckRect pack = g.SidePack(2);
        Assert.Equal(new GearDeckHit(GearZone.SidePack, 2), g.HitAt(pack.X + 1, pack.Y + 1));
        Assert.Equal(GearZone.Close, g.HitAt(g.Close.X + 1, g.Close.Y + 1).Zone);
    }

    [Fact]
    public void ListRows_22High()
    {
        var g = GearDeckGeometry.Create(600, Input(list: true));
        Assert.Equal(22, g.ListRow(1, 0)!.Value.Y - g.ListRow(0, 0)!.Value.Y);
    }

    [Fact]
    public void BelowMinHeight_ClampsToMin()
    {
        var g = GearDeckGeometry.Create(100, Input());
        Assert.Equal(GearDeckGeometry.MinHeight, g.Frame.H);
        Assert.True(g.PackArea.H > 0);
    }

    [Fact]
    public void Hit_FoldedWornCellsAndExistenceLimits()
    {
        var g = GearDeckGeometry.Create(600, Input(folded: true, worn: 3, itemCount: 2));
        DeckRect w = g.WornCell(2);
        Assert.Equal(new GearDeckHit(GearZone.WornCell, 2), g.HitAt(w.X + 1, w.Y + 1));
        DeckRect w3 = g.WornCell(3);
        Assert.Equal(GearZone.Frame, g.HitAt(w3.X + 1, w3.Y + 1).Zone);
        DeckRect noItem = g.GridCell(2, 0)!.Value;
        Assert.Equal(GearZone.Frame, g.HitAt(noItem.X + 1, noItem.Y + 1).Zone);
        Assert.Equal(GearZone.None, g.HitAt(-1, -1).Zone);
    }
}
