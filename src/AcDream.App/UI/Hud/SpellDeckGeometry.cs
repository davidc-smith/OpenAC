namespace AcDream.App.UI.Hud;

public enum SpellDeckZone { None, Frame, Item, TabChip, Slot }

/// <summary>What a point on the Spell Deck is over. Row and Slot are -1 when not used.</summary>
public readonly record struct SpellDeckHit(SpellDeckZone Zone, int Row, int Slot);

/// <summary>
/// The Spell Deck's geometry in points for one size. Like <see cref="DeckGeometry"/>:
/// <see cref="Scale"/> multiplies everything and <see cref="Stretch"/> widens it
/// (more slots per row, never bigger slots). The height follows the row count.
/// Scale changes the size, never the slot count.
/// </summary>
public sealed record SpellDeckGeometry
{
    public const float BaseWidth = DeckGeometry.BaseWidth;
    public const int MinRows = 1;
    public const int MaxRows = 10;
    private const float Pad = 6f, Gutter = 40f, ChipWidth = 20f, ChipGap = 4f;
    private const float SlotSize = 26f, SlotPitch = 28f, RowPitch = 28f, TopPad = 7f, BottomPad = 7f;
    private const float SlotsLeft = Pad + Gutter + ChipWidth + ChipGap;   // 70
    private const float ItemSize = 30f;

    public float Scale { get; }
    public float Stretch { get; }
    public int Rows { get; }

    private SpellDeckGeometry(float scale, float stretch, int rows) { Scale = scale; Stretch = stretch; Rows = rows; }

    public static SpellDeckGeometry Create(float scale, float stretch, int rows) => new(
        DeckGeometry.SnapScale(scale),
        Math.Clamp(float.IsFinite(stretch) ? stretch : 1f, DeckGeometry.MinStretch, DeckGeometry.MaxStretch),
        Math.Clamp(rows, MinRows, MaxRows));

    public static float BaseHeight(int rows) => TopPad + rows * RowPitch - (RowPitch - SlotSize) + BottomPad;

    public float Width => BaseWidth * Stretch * Scale;
    public float Height => BaseHeight(Rows) * Scale;
    public int SlotsPerRow => Capacity(Stretch);

    /// <summary>Slots that fit in one row at a stretch.</summary>
    public static int Capacity(float stretch) =>
        (int)MathF.Floor((BaseWidth * stretch - SlotsLeft - Pad + (SlotPitch - SlotSize)) / SlotPitch + 1e-4f);

    public static int MaxCapacity => Capacity(DeckGeometry.MaxStretch);

    /// <summary>The smallest stretch whose rows hold <paramref name="slots"/> slots (clamped to the stretch range).</summary>
    public static float StretchFor(int slots)
    {
        float width = SlotsLeft + Pad + slots * SlotPitch - (SlotPitch - SlotSize);
        return Math.Clamp(width / BaseWidth, DeckGeometry.MinStretch, DeckGeometry.MaxStretch);
    }

    /// <summary>The geometry for a window size and row count: scale from the height, stretch from the width.</summary>
    public static SpellDeckGeometry FromSize(float width, float height, int rows)
    {
        rows = Math.Clamp(rows, MinRows, MaxRows);
        if (!float.IsFinite(width) || !float.IsFinite(height)) return Create(1f, 1f, rows);
        float scale = DeckGeometry.SnapScale(height / BaseHeight(rows));
        return Create(scale, width / (BaseWidth * scale), rows);
    }

    public SpellDeckGeometry WithRows(int rows) => Create(Scale, Stretch, rows);
    public SpellDeckGeometry WithStretch(float stretch) => Create(Scale, stretch, Rows);
    public SpellDeckGeometry WithWidth(float width) => Create(Scale, width / (BaseWidth * Scale), Rows);

    /// <summary>A corner resize measured from this geometry (the drag start), as <see cref="DeckGeometry.WithCornerSize"/>.</summary>
    public SpellDeckGeometry WithCornerSize(float width, float height)
    {
        float fromWidth = width / (BaseWidth * Stretch);
        float fromHeight = height / BaseHeight(Rows);
        float raw = MathF.Abs(fromWidth - Scale) > MathF.Abs(fromHeight - Scale) ? fromWidth : fromHeight;
        return Create(raw, Stretch, Rows);
    }

    private DeckRect S(float x, float y, float w, float h) => new(x * Scale, y * Scale, w * Scale, h * Scale);

    public DeckRect Frame => new(0f, 0f, Width, Height);
    public DeckRect ItemSlot => S(Pad + (Gutter - ItemSize) / 2f, (BaseHeight(Rows) - ItemSize) / 2f, ItemSize, ItemSize);
    public DeckRect Divider => S(Pad + Gutter - 1f, TopPad, 1f, BaseHeight(Rows) - TopPad - BottomPad);
    public DeckRect TabChip(int row) => S(Pad + Gutter, TopPad + row * RowPitch, ChipWidth, SlotSize);
    public DeckRect Slot(int row, int index) => S(SlotsLeft + index * SlotPitch, TopPad + row * RowPitch, SlotSize, SlotSize);

    public SpellDeckHit HitAt(float x, float y)
    {
        if (!Frame.Contains(x, y)) return new(SpellDeckZone.None, -1, -1);
        if (ItemSlot.Contains(x, y)) return new(SpellDeckZone.Item, -1, -1);
        for (int row = 0; row < Rows; row++)
        {
            if (TabChip(row).Contains(x, y)) return new(SpellDeckZone.TabChip, row, -1);
            DeckRect first = Slot(row, 0);
            if (y < first.Y || y >= first.Y + first.H || x < first.X) continue;
            int index = (int)((x - first.X) / (SlotPitch * Scale));
            if (index < SlotsPerRow && Slot(row, index).Contains(x, y))
                return new(SpellDeckZone.Slot, row, index);
        }
        return new(SpellDeckZone.Frame, -1, -1);
    }
}
