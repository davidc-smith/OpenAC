namespace AcDream.App.UI.Hud;

internal readonly record struct GearDeckLayoutInput(bool Folded, bool List, int WornCount, int ItemCount, int SidePackSlots, int ScrollRow,
    int SavedCount = 0, int Suggestions = 0, bool SalvageButton = false);

internal enum GearZone
{
    None, Frame, Close, FoldStud, FigureStud, SlotsStud, GridStud, ListStud, EquipSlot, WornCell, Item, SidePack,
    QueryField, Chip, ChipMore, SaveStud, SortStud, DirStud, SavedFilter, SavedMore, Suggestion, FooterButton,
}

/// <summary>The labels the query bar's pills show and how wide a label draws.</summary>
internal sealed record GearPills(IReadOnlyList<string> Chips, IReadOnlyList<string> Saved, Func<string, float> Measure);

internal readonly record struct GearDeckHit(GearZone Zone, int Index = -1);

/// <summary>Where everything in the 300-point Gear Deck column goes, for one height and state.</summary>
internal sealed class GearDeckGeometry
{
    public const float Width = 300f, MinHeight = 360f, DefaultHeight = 648f, Pad = 10f, Gap = 6f;
    public const float TitleH = 22f, HeaderH = 20f, BurdenH = 8f, FooterH = 16f;
    public const float CloseSize = 16f, StudW = 52f, StudH = 18f, StudGap = 4f, FoldW = 20f, ViewStudW = 30f;
    public const float SlotCell = 38f, SlotGap = 4f, WornCellSize = 24f, WornGap = 3f;
    public const float ItemCell = 36f, ItemGap = 2f, ListRowH = 22f, SideW = 36f, SideCell = 34f, SideGap = 4f;
    public const float QueryH = 22f, SavedH = 18f, PillH = 18f, PillGap = 4f, PillPadX = 5f, ChipCloseW = 8f, MaxPillW = 110f;
    public const float MoreW = 26f, SortW = 58f, DirW = 18f, SaveW = 18f, FieldMinW = 44f, SuggestH = 18f, FooterButtonW = 120f;
    public const int Columns = 6, WornPerRow = 10, SlotRows = 4, MaxWornRows = 3;

    public required DeckRect Frame { get; init; }
    public required DeckRect Title { get; init; }
    public required DeckRect Close { get; init; }
    public required DeckRect EquipHeader { get; init; }
    public required DeckRect FoldStud { get; init; }
    public required DeckRect FigureStud { get; init; }
    public required DeckRect SlotsStud { get; init; }
    public required DeckRect Equipment { get; init; }
    public required DeckRect PacksHeader { get; init; }
    public required DeckRect GridStud { get; init; }
    public required DeckRect ListStud { get; init; }
    public required DeckRect BurdenBar { get; init; }
    public required DeckRect PackArea { get; init; }
    public required DeckRect Contents { get; init; }
    public required DeckRect SideColumn { get; init; }
    public required DeckRect Footer { get; init; }
    public required DeckRect QueryBar { get; init; }
    public required DeckRect QueryText { get; init; }
    public required DeckRect QueryField { get; init; }
    public required DeckRect SaveStud { get; init; }
    public required DeckRect SortStud { get; init; }
    public required DeckRect DirStud { get; init; }
    public required DeckRect SavedRow { get; init; }
    public required DeckRect FooterButton { get; init; }
    public required IReadOnlyList<DeckRect> Chips { get; init; }
    public required IReadOnlyList<DeckRect> SavedStuds { get; init; }
    public required DeckRect? ChipMore { get; init; }
    public required DeckRect? SavedMore { get; init; }
    public required int HiddenChips { get; init; }
    public required int HiddenSaved { get; init; }
    public required GearDeckLayoutInput Input { get; init; }

    public int VisibleGridRows => Math.Max(0, (int)MathF.Floor((Contents.H + ItemGap) / (ItemCell + ItemGap)));
    public int VisibleListRows => Math.Max(0, (int)MathF.Floor(Contents.H / ListRowH));

    /// <summary>How many side-column cells (main pack included) are shown: the pack count, clipped to the area.</summary>
    public int VisibleSidePacks =>
        Math.Clamp((int)MathF.Floor((SideColumn.H + SideGap) / (SideCell + SideGap)), 0, 1 + Math.Max(0, Input.SidePackSlots));

    /// <summary>
    /// The smallest height that shows every side-column cell (the main pack and
    /// each side-pack slot) for <paramref name="input"/>'s fold state, never
    /// below <see cref="MinHeight"/>. The window's minimum follows it.
    /// </summary>
    public static float MinHeightFor(GearDeckLayoutInput input)
    {
        int cells = 1 + Math.Max(0, input.SidePackSlots);
        float top = TitleH + Gap + HeaderH + Gap + EquipmentHeight(input) + Gap + HeaderH + Gap + BurdenH + Gap
            + QueryH + Gap + (input.SavedCount > 0 ? SavedH + Gap : 0f);
        float side = cells * SideCell + (cells - 1) * SideGap;
        return MathF.Max(MinHeight, MathF.Ceiling(top + side + Gap + FooterH + Pad));
    }

    private static float EquipmentHeight(GearDeckLayoutInput input)
    {
        if (!input.Folded) return SlotRows * SlotCell + (SlotRows - 1) * SlotGap;
        int rows = Math.Clamp((input.WornCount + WornPerRow - 1) / WornPerRow, 1, MaxWornRows);
        return rows * WornCellSize + (rows - 1) * WornGap;
    }

    public static GearDeckGeometry Create(float height, GearDeckLayoutInput input, GearPills? pills = null)
    {
        float h = MathF.Max(height, MinHeight);
        float inner = Width - 2 * Pad;

        var title = new DeckRect(0f, 0f, Width, TitleH);
        var close = new DeckRect(Width - Pad - CloseSize, (TitleH - CloseSize) / 2f, CloseSize, CloseSize);

        float y = TitleH + Gap;
        var equipHeader = new DeckRect(Pad, y, inner, HeaderH);
        float studY = y + (HeaderH - StudH) / 2f;
        var slotsStud = new DeckRect(Width - Pad - StudW, studY, StudW, StudH);
        var figureStud = slotsStud with { X = slotsStud.X - StudGap - StudW };
        var foldStud = new DeckRect(Pad, studY, FoldW, StudH);
        y += HeaderH + Gap;

        float equipH = EquipmentHeight(input);
        var equipment = new DeckRect(Pad, y, inner, equipH);
        y += equipH + Gap;

        var packsHeader = new DeckRect(Pad, y, inner, HeaderH);
        studY = y + (HeaderH - StudH) / 2f;
        var listStud = new DeckRect(Width - Pad - ViewStudW, studY, ViewStudW, StudH);
        var gridStud = listStud with { X = listStud.X - StudGap - ViewStudW };
        y += HeaderH + Gap;

        var burden = new DeckRect(Pad, y, inner, BurdenH);
        y += BurdenH + Gap;

        var queryBar = new DeckRect(Pad, y, inner, QueryH);
        float qStudY = y + (QueryH - StudH) / 2f;
        var dir = new DeckRect(Width - Pad - DirW, qStudY, DirW, StudH);
        var sort = new DeckRect(dir.X - StudGap - SortW, qStudY, SortW, StudH);
        var save = new DeckRect(sort.X - StudGap - SaveW, qStudY, SaveW, StudH);
        var queryText = new DeckRect(Pad, y, MathF.Max(0f, save.X - Gap - Pad), QueryH);
        float pillY = y + (QueryH - PillH) / 2f;
        float[] chipW = pills is null ? [] : pills.Chips.Select(c => MathF.Min(MaxPillW, pills.Measure(c) + 2 * PillPadX + ChipCloseW)).ToArray();
        var (chips, chipMore, hiddenChips) = FitPills(chipW, queryText.X, pillY, queryText.W - FieldMinW - Gap, PillH);
        float fieldX = chipMore is { } m ? m.X + m.W + PillGap
            : chips.Length > 0 ? chips[^1].X + chips[^1].W + PillGap : queryText.X;
        var queryField = new DeckRect(fieldX, y, queryText.X + queryText.W - fieldX, QueryH);
        y += QueryH + Gap;

        DeckRect savedRow = new(Pad, y, inner, 0f);
        DeckRect[] savedStuds = [];
        DeckRect? savedMore = null;
        int hiddenSaved = 0;
        if (input.SavedCount > 0)
        {
            savedRow = savedRow with { H = SavedH };
            float[] savedW = pills is null ? [] : pills.Saved.Select(s => MathF.Min(MaxPillW, pills.Measure(s) + 2 * PillPadX)).ToArray();
            (savedStuds, savedMore, hiddenSaved) = FitPills(savedW, Pad, y, inner, SavedH);
            y += SavedH + Gap;
        }

        var footer = new DeckRect(Pad, h - Pad - FooterH, inner, FooterH);
        var footerButton = new DeckRect(footer.X + footer.W - FooterButtonW, footer.Y, FooterButtonW, FooterH);
        float areaH = MathF.Max(0f, footer.Y - Gap - y);
        var packArea = new DeckRect(Pad, y, inner, areaH);
        float contentsW = inner - SideW - Gap;
        var contents = new DeckRect(Pad, y, contentsW, areaH);
        var side = new DeckRect(Pad + contentsW + Gap, y, SideW, areaH);

        return new GearDeckGeometry
        {
            Frame = new DeckRect(0f, 0f, Width, h),
            Title = title, Close = close,
            EquipHeader = equipHeader, FoldStud = foldStud, FigureStud = figureStud, SlotsStud = slotsStud,
            Equipment = equipment,
            PacksHeader = packsHeader, GridStud = gridStud, ListStud = listStud,
            BurdenBar = burden, PackArea = packArea, Contents = contents, SideColumn = side, Footer = footer,
            QueryBar = queryBar, QueryText = queryText, QueryField = queryField,
            SaveStud = save, SortStud = sort, DirStud = dir,
            SavedRow = savedRow, FooterButton = footerButton,
            Chips = chips, SavedStuds = savedStuds, ChipMore = chipMore, SavedMore = savedMore,
            HiddenChips = hiddenChips, HiddenSaved = hiddenSaved,
            Input = input,
        };
    }

    /// <summary>Pills left to right while each fits, keeping room for a "+N" pill when more follow.</summary>
    internal static (DeckRect[] Rects, DeckRect? More, int Hidden) FitPills(IReadOnlyList<float> widths, float x, float y, float room, float h)
    {
        var rects = new List<DeckRect>();
        float used = 0f;
        int i = 0;
        for (; i < widths.Count; i++)
        {
            bool last = i == widths.Count - 1;
            float need = widths[i] + (last ? 0f : PillGap + MoreW);
            if (used + need > room) break;
            rects.Add(new DeckRect(x + used, y, widths[i], h));
            used += widths[i] + PillGap;
        }
        int hidden = widths.Count - i;
        return (rects.ToArray(), hidden > 0 ? new DeckRect(x + used, y, MoreW, h) : null, hidden);
    }

    public DeckRect Suggestion(int index) =>
        new(QueryText.X, QueryBar.Y + QueryH + 1f + index * SuggestH, QueryText.W, SuggestH);

    public DeckRect? GridCell(int index, int scrollRow)
    {
        if (index < 0) return null;
        int row = index / Columns - scrollRow;
        if (row < 0 || row >= VisibleGridRows) return null;
        int col = index % Columns;
        return new DeckRect(Contents.X + col * (ItemCell + ItemGap), Contents.Y + row * (ItemCell + ItemGap), ItemCell, ItemCell);
    }

    public DeckRect? ListRow(int index, int scrollRow)
    {
        if (index < 0) return null;
        int row = index - scrollRow;
        if (row < 0 || row >= VisibleListRows) return null;
        return new DeckRect(Contents.X, Contents.Y + row * ListRowH, Contents.W, ListRowH);
    }

    public DeckRect EquipSlot(int index)
    {
        float gridW = Columns * SlotCell + (Columns - 1) * SlotGap;
        float x0 = Equipment.X + (Equipment.W - gridW) / 2f;
        int col = index % Columns, row = index / Columns;
        return new DeckRect(x0 + col * (SlotCell + SlotGap), Equipment.Y + row * (SlotCell + SlotGap), SlotCell, SlotCell);
    }

    public DeckRect WornCell(int index)
    {
        int col = index % WornPerRow, row = index / WornPerRow;
        return new DeckRect(Equipment.X + col * (WornCellSize + WornGap), Equipment.Y + row * (WornCellSize + WornGap), WornCellSize, WornCellSize);
    }

    public DeckRect SidePack(int index) =>
        new(SideColumn.X + 1f, SideColumn.Y + index * (SideCell + SideGap), SideCell, SideCell);

    public GearDeckHit HitAt(float x, float y)
    {
        if (!Frame.Contains(x, y)) return new GearDeckHit(GearZone.None);
        if (Close.Contains(x, y)) return new GearDeckHit(GearZone.Close);
        if (FoldStud.Contains(x, y)) return new GearDeckHit(GearZone.FoldStud);
        if (FigureStud.Contains(x, y)) return new GearDeckHit(GearZone.FigureStud);
        if (SlotsStud.Contains(x, y)) return new GearDeckHit(GearZone.SlotsStud);
        if (GridStud.Contains(x, y)) return new GearDeckHit(GearZone.GridStud);
        if (ListStud.Contains(x, y)) return new GearDeckHit(GearZone.ListStud);

        for (int i = 0; i < Input.Suggestions; i++)
            if (Suggestion(i).Contains(x, y)) return new GearDeckHit(GearZone.Suggestion, i);
        if (SaveStud.Contains(x, y)) return new GearDeckHit(GearZone.SaveStud);
        if (SortStud.Contains(x, y)) return new GearDeckHit(GearZone.SortStud);
        if (DirStud.Contains(x, y)) return new GearDeckHit(GearZone.DirStud);
        for (int i = 0; i < Chips.Count; i++)
            if (Chips[i].Contains(x, y)) return new GearDeckHit(GearZone.Chip, i);
        if (ChipMore is { } cm && cm.Contains(x, y)) return new GearDeckHit(GearZone.ChipMore);
        if (QueryField.Contains(x, y)) return new GearDeckHit(GearZone.QueryField);
        for (int i = 0; i < SavedStuds.Count; i++)
            if (SavedStuds[i].Contains(x, y)) return new GearDeckHit(GearZone.SavedFilter, i);
        if (SavedMore is { } sm && sm.Contains(x, y)) return new GearDeckHit(GearZone.SavedMore);
        if (Input.SalvageButton && FooterButton.Contains(x, y)) return new GearDeckHit(GearZone.FooterButton);

        if (Equipment.Contains(x, y))
        {
            if (Input.Folded)
            {
                int max = Math.Min(Input.WornCount, MaxWornRows * WornPerRow);
                for (int i = 0; i < max; i++)
                    if (WornCell(i).Contains(x, y)) return new GearDeckHit(GearZone.WornCell, i);
            }
            else
            {
                for (int i = 0; i < Columns * SlotRows; i++)
                    if (EquipSlot(i).Contains(x, y)) return new GearDeckHit(GearZone.EquipSlot, i);
            }
        }

        if (Contents.Contains(x, y))
        {
            if (Input.List)
            {
                for (int r = 0; r < VisibleListRows; r++)
                {
                    int i = Input.ScrollRow + r;
                    if (i >= Input.ItemCount) break;
                    if (ListRow(i, Input.ScrollRow) is { } rect && rect.Contains(x, y)) return new GearDeckHit(GearZone.Item, i);
                }
            }
            else
            {
                for (int r = 0; r < VisibleGridRows; r++)
                    for (int c = 0; c < Columns; c++)
                    {
                        int i = (Input.ScrollRow + r) * Columns + c;
                        if (i >= Input.ItemCount) break;
                        if (GridCell(i, Input.ScrollRow) is { } rect && rect.Contains(x, y)) return new GearDeckHit(GearZone.Item, i);
                    }
            }
        }

        if (SideColumn.Contains(x, y))
        {
            int n = VisibleSidePacks;
            for (int i = 0; i < n; i++)
                if (SidePack(i).Contains(x, y)) return new GearDeckHit(GearZone.SidePack, i);
        }

        return new GearDeckHit(GearZone.Frame);
    }
}
