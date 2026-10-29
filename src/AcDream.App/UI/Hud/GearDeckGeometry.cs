namespace AcDream.App.UI.Hud;

internal readonly record struct GearDeckLayoutInput(bool Folded, bool List, int WornCount, int ItemCount, int SidePackSlots, int ScrollRow);

internal enum GearZone { None, Frame, Close, FoldStud, FigureStud, SlotsStud, GridStud, ListStud, EquipSlot, WornCell, Item, SidePack }

internal readonly record struct GearDeckHit(GearZone Zone, int Index = -1);

/// <summary>Where everything in the 300-point Gear Deck column goes, for one height and state.</summary>
internal sealed class GearDeckGeometry
{
    public const float Width = 300f, MinHeight = 360f, DefaultHeight = 620f, Pad = 10f, Gap = 6f;
    public const float TitleH = 22f, HeaderH = 20f, BurdenH = 8f, FooterH = 16f;
    public const float CloseSize = 16f, StudW = 52f, StudH = 18f, StudGap = 4f, FoldW = 20f, ViewStudW = 30f;
    public const float SlotCell = 38f, SlotGap = 4f, WornCellSize = 24f, WornGap = 3f;
    public const float ItemCell = 36f, ItemGap = 2f, ListRowH = 22f, SideW = 36f, SideCell = 34f, SideGap = 4f;
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
        float top = TitleH + Gap + HeaderH + Gap + EquipmentHeight(input) + Gap + HeaderH + Gap + BurdenH + Gap;
        float side = cells * SideCell + (cells - 1) * SideGap;
        return MathF.Max(MinHeight, MathF.Ceiling(top + side + Gap + FooterH + Pad));
    }

    private static float EquipmentHeight(GearDeckLayoutInput input)
    {
        if (!input.Folded) return SlotRows * SlotCell + (SlotRows - 1) * SlotGap;
        int rows = Math.Clamp((input.WornCount + WornPerRow - 1) / WornPerRow, 1, MaxWornRows);
        return rows * WornCellSize + (rows - 1) * WornGap;
    }

    public static GearDeckGeometry Create(float height, GearDeckLayoutInput input)
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

        var footer = new DeckRect(Pad, h - Pad - FooterH, inner, FooterH);
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
            Input = input,
        };
    }

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
