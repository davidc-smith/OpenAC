namespace AcDream.App.UI.Hud;

internal readonly record struct GearDeckLayoutInput(bool Folded, bool List, int WornCount, int ItemCount, int SidePackSlots, int ScrollRow,
    int SavedCount = 0, int Suggestions = 0, bool SalvageButton = false, bool Figure = false);

internal enum GearZone
{
    None, Frame, Close, FoldStud, FigureStud, SlotsStud, GridStud, ListStud, EquipSlot, WornCell, Item, SidePack,
    QueryField, Chip, ChipMore, SaveStud, SortStud, DirStud, SavedFilter, SavedMore, Suggestion, FooterButton, Figure,
    FigureLock,
}

/// <summary>The labels the query bar's pills show and how wide a label draws.</summary>
internal sealed record GearPills(IReadOnlyList<string> Chips, IReadOnlyList<string> Saved, Func<string, float> Measure);

internal readonly record struct GearDeckHit(GearZone Zone, int Index = -1);

/// <summary>
/// Where everything in the Gear Deck column goes, for one height, state and
/// deck scale. The constants are the sizes at scale 1 (the 300-point column);
/// <see cref="Scale"/> multiplies every one of them, so a deck at 1.5 is 450
/// points wide with every cell, stud and gap half as big again.
/// </summary>
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
    public const float FigureW = 140f, FigureH = 214f, RingCell = 30f, RingGap = 4f, RingSideGap = 6f;
    /// <summary>The retail click map (the doll mask 0x100001D6): 100 x 214, its left edge 61 points left of
    /// the doll viewport's centre (the 224-wide viewport 0x100001D5 holds it at x+51).</summary>
    public const float MapW = 100f, MapH = 214f, MapLeftOfCentre = 61f;
    public const int RingRows = 6;
    /// <summary>The figure's padlock stud, inset from the figure's top-right corner.</summary>
    public const float LockSize = 18f, LockInset = 4f;

    /// <summary>Each <see cref="AcDream.Core.Items.Gear.GearSlotCatalog.Slots"/> entry's ring cell in Figure view
    /// (column 0-3 left to right, row 0-5 top down): head, torso and arms outer left; cloak, clothes, legs and
    /// feet inner left; jewellery inner right; Aetheria and weapons outer right; each column runs head to foot.</summary>
    internal static readonly (int Col, int Row)[] RingCells =
    [
        (0, 0), (2, 0), (1, 0),            // Head, Neck, Cloak
        (3, 0), (3, 1), (3, 2),            // Blue, Yellow, Red
        (0, 1), (0, 2), (0, 3), (0, 4),    // Chest, Abdomen, Upper arm, Lower arm
        (1, 1), (1, 2), (1, 3), (1, 4),    // Shirt, Pants, Upper leg, Lower leg
        (0, 5), (1, 5),                    // Hands, Feet
        (2, 1), (2, 2), (2, 3), (2, 4),    // L wrist, R wrist, L ring, R ring
        (3, 3), (3, 4), (3, 5), (2, 5),    // Weapon, Shield, Ammo, Trinket
    ];

    public required DeckRect Frame { get; init; }
    public required DeckRect Title { get; init; }
    public required DeckRect Close { get; init; }
    public required DeckRect EquipHeader { get; init; }
    public required DeckRect FoldStud { get; init; }
    public required DeckRect FigureStud { get; init; }
    public required DeckRect SlotsStud { get; init; }
    public required DeckRect Equipment { get; init; }
    public required DeckRect Figure { get; init; }
    public required DeckRect ClickMap { get; init; }
    /// <summary>The padlock stud in the figure's top-right corner; zero-size while the figure is not shown.</summary>
    public required DeckRect FigureLock { get; init; }
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
    /// <summary>The deck scale every size here was multiplied by.</summary>
    public required float Scale { get; init; }
    /// <summary>Points every text row (list rows, suggestions) is grown by: <see cref="DeckFonts.RowGrow"/> at the deck's text size.</summary>
    public required float TextGrow { get; init; }

    /// <summary>A list row's height: its height at the scale, grown for the text size.</summary>
    public float ListRowHeight => S(ListRowH) + TextGrow;
    /// <summary>A suggestion row's height: its height at the scale, grown for the text size.</summary>
    public float SuggestRowHeight => S(SuggestH) + TextGrow;

    /// <summary>The column's width at <paramref name="scale"/>.</summary>
    public static float WidthAt(float scale) => Width * scale;
    /// <summary>The window's smallest height at <paramref name="scale"/>, before the side packs ask for more.</summary>
    public static float MinHeightAt(float scale) => MinHeight * scale;
    /// <summary>A new window's height at <paramref name="scale"/>.</summary>
    public static float DefaultHeightAt(float scale) => DefaultHeight * scale;

    /// <summary>
    /// The scale a window <paramref name="width"/> wide is at: the decks' step
    /// nearest width / 300, lowered a step when that would be wider than the
    /// width (as <see cref="DeckGeometry.FromSize"/> reads a height), within the
    /// decks' range. A saved or hand-edited width loads at a step.
    /// </summary>
    public static float ScaleForWidth(float width)
    {
        if (!float.IsFinite(width)) return 1f;
        float scale = DeckGeometry.SnapScale(width / Width);
        if (WidthAt(scale) > width + 0.5f) scale = DeckGeometry.StepDown(width / Width);
        return scale;
    }

    private float S(float points) => points * Scale;

    public int VisibleGridRows => Math.Max(0, (int)MathF.Floor((Contents.H + S(ItemGap)) / (S(ItemCell) + S(ItemGap))));
    public int VisibleListRows => Math.Max(0, (int)MathF.Floor(Contents.H / ListRowHeight));

    /// <summary>How many side-column cells (main pack included) are shown: the pack count, clipped to the area.</summary>
    public int VisibleSidePacks =>
        Math.Clamp((int)MathF.Floor((SideColumn.H + S(SideGap)) / (S(SideCell) + S(SideGap))), 0, 1 + Math.Max(0, Input.SidePackSlots));

    /// <summary>
    /// The smallest height that shows every side-column cell (the main pack and
    /// each side-pack slot) for <paramref name="input"/>'s fold state at
    /// <paramref name="scale"/>, never below <see cref="MinHeightAt"/>. The
    /// window's minimum follows it.
    /// </summary>
    public static float MinHeightFor(GearDeckLayoutInput input, float scale = 1f)
    {
        float S(float points) => points * scale;
        int cells = 1 + Math.Max(0, input.SidePackSlots);
        float top = S(TitleH) + S(Gap) + S(HeaderH) + S(Gap) + EquipmentHeight(input, scale) + S(Gap) + S(HeaderH) + S(Gap)
            + S(BurdenH) + S(Gap) + S(QueryH) + S(Gap) + (input.SavedCount > 0 ? S(SavedH) + S(Gap) : 0f);
        float side = cells * S(SideCell) + (cells - 1) * S(SideGap);
        return MathF.Max(MinHeightAt(scale), MathF.Ceiling(top + side + S(Gap) + S(FooterH) + S(Pad)));
    }

    private static float EquipmentHeight(GearDeckLayoutInput input, float scale)
    {
        if (!input.Folded) return input.Figure
            ? FigureH * scale
            : SlotRows * SlotCell * scale + (SlotRows - 1) * SlotGap * scale;
        int rows = Math.Clamp((input.WornCount + WornPerRow - 1) / WornPerRow, 1, MaxWornRows);
        return rows * WornCellSize * scale + (rows - 1) * WornGap * scale;
    }

    public static GearDeckGeometry Create(float height, GearDeckLayoutInput input, GearPills? pills = null, float scale = 1f,
        float textGrow = 0f)
    {
        float S(float points) => points * scale;
        float h = MathF.Max(height, MinHeightAt(scale));
        float width = WidthAt(scale), pad = S(Pad), gap = S(Gap);
        float inner = width - 2 * pad;

        var title = new DeckRect(0f, 0f, width, S(TitleH));
        var close = new DeckRect(width - pad - S(CloseSize), (S(TitleH) - S(CloseSize)) / 2f, S(CloseSize), S(CloseSize));

        float y = S(TitleH) + gap;
        var equipHeader = new DeckRect(pad, y, inner, S(HeaderH));
        float studY = y + (S(HeaderH) - S(StudH)) / 2f;
        var slotsStud = new DeckRect(width - pad - S(StudW), studY, S(StudW), S(StudH));
        var figureStud = slotsStud with { X = slotsStud.X - S(StudGap) - S(StudW) };
        var foldStud = new DeckRect(pad, studY, S(FoldW), S(StudH));
        y += S(HeaderH) + gap;

        float equipH = EquipmentHeight(input, scale);
        var equipment = new DeckRect(pad, y, inner, equipH);
        y += equipH + gap;
        bool figureShown = input.Figure && !input.Folded;
        var figure = figureShown
            ? new DeckRect(equipment.X + (equipment.W - S(FigureW)) / 2f, equipment.Y, S(FigureW), S(FigureH))
            : new DeckRect(equipment.X, equipment.Y, 0f, 0f);
        var clickMap = figureShown
            ? new DeckRect(figure.X + figure.W / 2f - S(MapLeftOfCentre), figure.Y, S(MapW), S(MapH))
            : new DeckRect(equipment.X, equipment.Y, 0f, 0f);
        var figureLock = figureShown
            ? new DeckRect(figure.X + figure.W - S(LockInset) - S(LockSize), figure.Y + S(LockInset), S(LockSize), S(LockSize))
            : new DeckRect(equipment.X, equipment.Y, 0f, 0f);

        var packsHeader = new DeckRect(pad, y, inner, S(HeaderH));
        studY = y + (S(HeaderH) - S(StudH)) / 2f;
        var listStud = new DeckRect(width - pad - S(ViewStudW), studY, S(ViewStudW), S(StudH));
        var gridStud = listStud with { X = listStud.X - S(StudGap) - S(ViewStudW) };
        y += S(HeaderH) + gap;

        var burden = new DeckRect(pad, y, inner, S(BurdenH));
        y += S(BurdenH) + gap;

        float queryH = S(QueryH), pillH = S(PillH);
        var queryBar = new DeckRect(pad, y, inner, queryH);
        float qStudY = y + (queryH - S(StudH)) / 2f;
        var dir = new DeckRect(width - pad - S(DirW), qStudY, S(DirW), S(StudH));
        var sort = new DeckRect(dir.X - S(StudGap) - S(SortW), qStudY, S(SortW), S(StudH));
        var save = new DeckRect(sort.X - S(StudGap) - S(SaveW), qStudY, S(SaveW), S(StudH));
        var queryText = new DeckRect(pad, y, MathF.Max(0f, save.X - gap - pad), queryH);
        float pillY = y + (queryH - pillH) / 2f;
        float[] chipW = pills is null ? [] : pills.Chips.Select(c => MathF.Min(S(MaxPillW), pills.Measure(c) + 2 * S(PillPadX) + S(ChipCloseW))).ToArray();
        var (chips, chipMore, hiddenChips) = FitPills(chipW, queryText.X, pillY, queryText.W - S(FieldMinW) - gap, pillH, scale);
        float fieldX = chipMore is { } m ? m.X + m.W + S(PillGap)
            : chips.Length > 0 ? chips[^1].X + chips[^1].W + S(PillGap) : queryText.X;
        var queryField = new DeckRect(fieldX, y, queryText.X + queryText.W - fieldX, queryH);
        y += queryH + gap;

        DeckRect savedRow = new(pad, y, inner, 0f);
        DeckRect[] savedStuds = [];
        DeckRect? savedMore = null;
        int hiddenSaved = 0;
        if (input.SavedCount > 0)
        {
            savedRow = savedRow with { H = S(SavedH) };
            float[] savedW = pills is null ? [] : pills.Saved.Select(n => MathF.Min(S(MaxPillW), pills.Measure(n) + 2 * S(PillPadX))).ToArray();
            (savedStuds, savedMore, hiddenSaved) = FitPills(savedW, pad, y, inner, S(SavedH), scale);
            y += S(SavedH) + gap;
        }

        var footer = new DeckRect(pad, h - pad - S(FooterH), inner, S(FooterH));
        var footerButton = new DeckRect(footer.X + footer.W - S(FooterButtonW), footer.Y, S(FooterButtonW), S(FooterH));
        float areaH = MathF.Max(0f, footer.Y - gap - y);
        var packArea = new DeckRect(pad, y, inner, areaH);
        float contentsW = inner - S(SideW) - gap;
        var contents = new DeckRect(pad, y, contentsW, areaH);
        var side = new DeckRect(pad + contentsW + gap, y, S(SideW), areaH);

        return new GearDeckGeometry
        {
            Frame = new DeckRect(0f, 0f, width, h),
            Title = title, Close = close,
            EquipHeader = equipHeader, FoldStud = foldStud, FigureStud = figureStud, SlotsStud = slotsStud,
            Equipment = equipment, Figure = figure, ClickMap = clickMap, FigureLock = figureLock,
            PacksHeader = packsHeader, GridStud = gridStud, ListStud = listStud,
            BurdenBar = burden, PackArea = packArea, Contents = contents, SideColumn = side, Footer = footer,
            QueryBar = queryBar, QueryText = queryText, QueryField = queryField,
            SaveStud = save, SortStud = sort, DirStud = dir,
            SavedRow = savedRow, FooterButton = footerButton,
            Chips = chips, SavedStuds = savedStuds, ChipMore = chipMore, SavedMore = savedMore,
            HiddenChips = hiddenChips, HiddenSaved = hiddenSaved,
            Input = input, Scale = scale, TextGrow = MathF.Max(0f, textGrow),
        };
    }

    /// <summary>Pills left to right while each fits, keeping room for a "+N" pill when more follow.</summary>
    internal static (DeckRect[] Rects, DeckRect? More, int Hidden) FitPills(IReadOnlyList<float> widths, float x, float y, float room, float h,
        float scale = 1f)
    {
        float pillGap = PillGap * scale, moreW = MoreW * scale;
        var rects = new List<DeckRect>();
        float used = 0f;
        int i = 0;
        for (; i < widths.Count; i++)
        {
            bool last = i == widths.Count - 1;
            float need = widths[i] + (last ? 0f : pillGap + moreW);
            if (used + need > room) break;
            rects.Add(new DeckRect(x + used, y, widths[i], h));
            used += widths[i] + pillGap;
        }
        int hidden = widths.Count - i;
        return (rects.ToArray(), hidden > 0 ? new DeckRect(x + used, y, moreW, h) : null, hidden);
    }

    public DeckRect Suggestion(int index) =>
        new(QueryText.X, QueryBar.Y + S(QueryH) + S(1f) + index * SuggestRowHeight, QueryText.W, SuggestRowHeight);

    public DeckRect? GridCell(int index, int scrollRow)
    {
        if (index < 0) return null;
        int row = index / Columns - scrollRow;
        if (row < 0 || row >= VisibleGridRows) return null;
        int col = index % Columns;
        float step = S(ItemCell) + S(ItemGap);
        return new DeckRect(Contents.X + col * step, Contents.Y + row * step, S(ItemCell), S(ItemCell));
    }

    public DeckRect? ListRow(int index, int scrollRow)
    {
        if (index < 0) return null;
        int row = index - scrollRow;
        if (row < 0 || row >= VisibleListRows) return null;
        return new DeckRect(Contents.X, Contents.Y + row * ListRowHeight, Contents.W, ListRowHeight);
    }

    public DeckRect EquipSlot(int index)
    {
        if (Input.Figure && !Input.Folded) return RingSlot(index);
        float cell = S(SlotCell), gap = S(SlotGap);
        float gridW = Columns * cell + (Columns - 1) * gap;
        float x0 = Equipment.X + (Equipment.W - gridW) / 2f;
        int col = index % Columns, row = index / Columns;
        return new DeckRect(x0 + col * (cell + gap), Equipment.Y + row * (cell + gap), cell, cell);
    }

    /// <summary>A slot's ring cell: two columns each side of <see cref="Figure"/>, six rows centred on it.</summary>
    private DeckRect RingSlot(int index)
    {
        (int col, int row) = RingCells[index];
        float cell = S(RingCell), gap = S(RingGap), side = S(RingSideGap);
        float x = col switch
        {
            0 => Figure.X - side - cell - gap - cell,
            1 => Figure.X - side - cell,
            2 => Figure.X + Figure.W + side,
            _ => Figure.X + Figure.W + side + cell + gap,
        };
        float ringH = RingRows * cell + (RingRows - 1) * gap;
        float y = Equipment.Y + (Equipment.H - ringH) / 2f + row * (cell + gap);
        return new DeckRect(x, y, cell, cell);
    }

    public DeckRect WornCell(int index)
    {
        int col = index % WornPerRow, row = index / WornPerRow;
        float cell = S(WornCellSize), step = cell + S(WornGap);
        return new DeckRect(Equipment.X + col * step, Equipment.Y + row * step, cell, cell);
    }

    public DeckRect SidePack(int index) =>
        new(SideColumn.X + S(1f), SideColumn.Y + index * (S(SideCell) + S(SideGap)), S(SideCell), S(SideCell));

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
                if (Input.Figure && FigureLock.Contains(x, y)) return new GearDeckHit(GearZone.FigureLock);
                if (Input.Figure && Figure.Contains(x, y)) return new GearDeckHit(GearZone.Figure);
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
