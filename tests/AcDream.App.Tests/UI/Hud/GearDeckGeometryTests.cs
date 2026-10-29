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

    [Fact]
    public void AtDefaultHeight_ExpandedShowsTheMainPackAndSevenSidePacks()
    {
        var g = GearDeckGeometry.Create(GearDeckGeometry.DefaultHeight, Input(sideSlots: 7));
        Assert.Equal(8, g.VisibleSidePacks);
        DeckRect last = g.SidePack(7);
        Assert.Equal(new GearDeckHit(GearZone.SidePack, 7), g.HitAt(last.X + 1, last.Y + last.H - 1));
    }

    [Theory]
    [InlineData(false, 7)]
    [InlineData(true, 7)]
    [InlineData(false, 0)]
    [InlineData(true, 30)]
    public void MinHeightFor_FitsEverySideCell_WithAFloor(bool folded, int sideSlots)
    {
        GearDeckLayoutInput input = Input(folded: folded, sideSlots: sideSlots);
        float min = GearDeckGeometry.MinHeightFor(input);
        Assert.True(min >= GearDeckGeometry.MinHeight);
        Assert.Equal(1 + sideSlots, GearDeckGeometry.Create(min, input).VisibleSidePacks);
        if (min > GearDeckGeometry.MinHeight)
            Assert.True(GearDeckGeometry.Create(min - 1f, input).VisibleSidePacks < 1 + sideSlots);
    }

    [Fact]
    public void MinHeightFor_FoldedIsLessThanExpanded()
    {
        Assert.True(GearDeckGeometry.MinHeightFor(Input(folded: true)) < GearDeckGeometry.MinHeightFor(Input()));
        Assert.Equal(GearDeckGeometry.MinHeight, GearDeckGeometry.MinHeightFor(Input(sideSlots: 0)));
    }

    private static readonly Func<string, float> Six = s => s.Length * 6f;

    [Fact]
    public void QueryBar_SitsUnderTheBurdenBar()
    {
        var g = GearDeckGeometry.Create(620f, new GearDeckLayoutInput(false, false, 2, 30, 7, 0));
        Assert.Equal(g.BurdenBar.Y + GearDeckGeometry.BurdenH + GearDeckGeometry.Gap, g.QueryBar.Y);
        Assert.Equal(GearDeckGeometry.QueryH, g.QueryBar.H);
        Assert.Equal(g.QueryBar.Y + GearDeckGeometry.QueryH + GearDeckGeometry.Gap, g.Contents.Y);
        Assert.Equal(0f, g.SavedRow.H);
    }

    [Fact]
    public void SavedRow_OnlyWithSavedFilters_AndMinHeightGrows()
    {
        var none = new GearDeckLayoutInput(false, false, 2, 30, 7, 0);
        var some = none with { SavedCount = 2 };
        var g = GearDeckGeometry.Create(620f, some);
        Assert.Equal(GearDeckGeometry.SavedH, g.SavedRow.H);
        Assert.Equal(g.SavedRow.Y + GearDeckGeometry.SavedH + GearDeckGeometry.Gap, g.Contents.Y);
        Assert.Equal(GearDeckGeometry.MinHeightFor(none) + GearDeckGeometry.SavedH + GearDeckGeometry.Gap,
            GearDeckGeometry.MinHeightFor(some));
    }

    [Fact]
    public void EverySidePackFits_AtMinHeight_WithSavedRow()
    {
        var input = new GearDeckLayoutInput(false, false, 2, 30, 7, 0, SavedCount: 1);
        var g = GearDeckGeometry.Create(GearDeckGeometry.MinHeightFor(input), input);
        Assert.Equal(8, g.VisibleSidePacks);
    }

    [Fact]
    public void QueryStuds_RightToLeft()
    {
        var g = GearDeckGeometry.Create(620f, new GearDeckLayoutInput(false, false, 0, 0, 7, 0));
        Assert.Equal(GearDeckGeometry.Width - GearDeckGeometry.Pad, g.DirStud.X + g.DirStud.W);
        Assert.Equal(g.DirStud.X - GearDeckGeometry.StudGap, g.SortStud.X + g.SortStud.W);
        Assert.Equal(g.SortStud.X - GearDeckGeometry.StudGap, g.SaveStud.X + g.SaveStud.W);
        Assert.Equal(g.SaveStud.X - GearDeckGeometry.Gap, g.QueryText.X + g.QueryText.W);
    }

    [Fact]
    public void Chips_FitThenMore_FieldKeepsMinimum()
    {
        var input = new GearDeckLayoutInput(false, false, 0, 0, 7, 0);
        var pills = new GearPills(["Salvage", "Steel", "Work >= 7", "Iron", "Copper"], [], Six);
        var g = GearDeckGeometry.Create(620f, input, pills);
        Assert.True(g.Chips.Count < 5);
        Assert.NotNull(g.ChipMore);
        Assert.Equal(5 - g.Chips.Count, g.HiddenChips);
        Assert.True(g.QueryField.W >= GearDeckGeometry.FieldMinW);
        Assert.Equal(g.QueryText.X + g.QueryText.W, g.QueryField.X + g.QueryField.W);
        Assert.Equal(new GearDeckHit(GearZone.Chip, 0), g.HitAt(g.Chips[0].X + 2, g.Chips[0].Y + 2));
        Assert.Equal(GearZone.ChipMore, g.HitAt(g.ChipMore!.Value.X + 2, g.ChipMore.Value.Y + 2).Zone);
        Assert.Equal(GearZone.QueryField, g.HitAt(g.QueryField.X + 2, g.QueryField.Y + 2).Zone);
    }

    [Fact]
    public void TheHeadlineQuery_SteelAndSalvage_FitsWithoutAMorePill()
    {
        var input = new GearDeckLayoutInput(false, false, 0, 0, 7, 0);
        var g = GearDeckGeometry.Create(620f, input, new GearPills(["Steel", "Salvage"], [], s => s.Length * 5.5f));
        Assert.Equal(2, g.Chips.Count);
        Assert.Null(g.ChipMore);
        Assert.True(g.QueryField.W >= GearDeckGeometry.FieldMinW);
    }

    [Fact]
    public void NoChips_FieldFillsQueryText()
    {
        var g = GearDeckGeometry.Create(620f, new GearDeckLayoutInput(false, false, 0, 0, 7, 0));
        Assert.Empty(g.Chips);
        Assert.Null(g.ChipMore);
        Assert.Equal(g.QueryText.X, g.QueryField.X);
    }

    [Fact]
    public void Suggestions_OverlayAndHitFirst()
    {
        var g = GearDeckGeometry.Create(620f, new GearDeckLayoutInput(false, false, 0, 30, 7, 0, Suggestions: 3));
        DeckRect s1 = g.Suggestion(1);
        Assert.Equal(g.QueryBar.Y + GearDeckGeometry.QueryH + 1 + GearDeckGeometry.SuggestH, s1.Y);
        Assert.Equal(new GearDeckHit(GearZone.Suggestion, 1), g.HitAt(s1.X + 2, s1.Y + 2));   // over the grid
    }

    [Fact]
    public void FooterButton_OnlyWhenShown()
    {
        var off = GearDeckGeometry.Create(620f, new GearDeckLayoutInput(false, false, 0, 0, 7, 0));
        var on = GearDeckGeometry.Create(620f, new GearDeckLayoutInput(false, false, 0, 0, 7, 0, SalvageButton: true));
        DeckRect b = on.FooterButton;
        Assert.Equal(GearDeckGeometry.FooterButtonW, b.W);
        Assert.Equal(GearZone.FooterButton, on.HitAt(b.X + 2, b.Y + 2).Zone);
        Assert.NotEqual(GearZone.FooterButton, off.HitAt(b.X + 2, b.Y + 2).Zone);
    }

    [Fact]
    public void SavedStuds_HitByIndex()
    {
        var input = new GearDeckLayoutInput(false, false, 0, 0, 7, 0, SavedCount: 2);
        var g = GearDeckGeometry.Create(620f, input, new GearPills([], ["Steel", "Kits"], Six));
        Assert.Equal(2, g.SavedStuds.Count);
        Assert.Equal(new GearDeckHit(GearZone.SavedFilter, 1), g.HitAt(g.SavedStuds[1].X + 2, g.SavedStuds[1].Y + 2));
    }
}
