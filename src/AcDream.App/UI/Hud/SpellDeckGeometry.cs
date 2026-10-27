namespace AcDream.App.UI.Hud;

public enum SpellDeckZone { None, Frame, Item, Bookmark, Plus, Slot }

/// <summary>What a point on the Spell Deck is over. Row and Slot are -1 when not
/// used; Tab is the bookmark's tab for <see cref="SpellDeckZone.Bookmark"/>, else -1.</summary>
public readonly record struct SpellDeckHit(SpellDeckZone Zone, int Row, int Slot, int Tab = -1)
{
    public static SpellDeckHit ForBookmark(int tab) => new(SpellDeckZone.Bookmark, -1, -1, tab);
}

/// <summary>
/// The Spell Deck's geometry in points for one size. Like <see cref="DeckGeometry"/>:
/// <see cref="Scale"/> multiplies everything and <see cref="Stretch"/> widens it
/// (more slots per row, never bigger slots). A tab strip of bookmarks runs across
/// the top; the rows sit below it and the height follows the row count.
/// Scale changes the size, never the slot count.
/// </summary>
public sealed record SpellDeckGeometry
{
    /// <summary>The width at stretch 1: from the Command Deck's orb to its right edge.</summary>
    public const float BaseWidth = 388f;
    /// <summary>What one whole stretch adds, as on the Command Deck.</summary>
    public const float StretchWidth = DeckGeometry.BaseWidth;
    public const int MinRows = 1;
    public const int MaxRows = 10;
    private const float Pad = 6f, Gutter = 40f, TabStrip = 18f, SlotsLeft = 46f;
    private const float SlotSize = 26f, SlotPitch = 28f, RowPitch = 28f, TopPad = 7f, BottomPad = 7f;
    private const float ItemSize = 30f;
    private const float BookmarkTop = 4f, BookmarkWidth = 24f, MinBookmarkWidth = 16f, BookmarkGap = 2f;

    public float Scale { get; }
    public float Stretch { get; }
    public int Rows { get; }

    private SpellDeckGeometry(float scale, float stretch, int rows) { Scale = scale; Stretch = stretch; Rows = rows; }

    public static SpellDeckGeometry Create(float scale, float stretch, int rows) => new(
        DeckGeometry.SnapScale(scale),
        Math.Clamp(float.IsFinite(stretch) ? stretch : 1f, DeckGeometry.MinStretch, DeckGeometry.MaxStretch),
        Math.Clamp(rows, MinRows, MaxRows));

    /// <summary>The width in base points at a stretch: <c>388 + 420·(stretch − 1)</c>.</summary>
    public static float BaseWidthAt(float stretch) => BaseWidth + StretchWidth * (stretch - 1f);

    /// <summary>The stretch whose base width is <paramref name="baseWidth"/> (unclamped).</summary>
    private static float StretchAt(float baseWidth) => (baseWidth - BaseWidth) / StretchWidth + 1f;

    /// <summary>The height in base points: <c>12 + 18 + 28·rows</c>.</summary>
    public static float BaseHeight(int rows) => TabStrip + TopPad + rows * RowPitch - (RowPitch - SlotSize) + BottomPad;

    public float Width => BaseWidthAt(Stretch) * Scale;
    public float Height => BaseHeight(Rows) * Scale;
    public int SlotsPerRow => Capacity(Stretch);

    /// <summary>Slots that fit in one row at a stretch.</summary>
    public static int Capacity(float stretch) =>
        (int)MathF.Floor((BaseWidthAt(stretch) - SlotsLeft - Pad + (SlotPitch - SlotSize)) / SlotPitch + 1e-4f);

    public static int MaxCapacity => Capacity(DeckGeometry.MaxStretch);

    /// <summary>The smallest stretch whose rows hold <paramref name="slots"/> slots (clamped to the stretch range).</summary>
    public static float StretchFor(int slots)
    {
        float width = SlotsLeft + Pad + slots * SlotPitch - (SlotPitch - SlotSize);
        return Math.Clamp(StretchAt(width), DeckGeometry.MinStretch, DeckGeometry.MaxStretch);
    }

    /// <summary>The geometry for a window size and row count: scale from the height, stretch from the width.</summary>
    public static SpellDeckGeometry FromSize(float width, float height, int rows)
    {
        rows = Math.Clamp(rows, MinRows, MaxRows);
        if (!float.IsFinite(width) || !float.IsFinite(height)) return Create(1f, 1f, rows);
        float scale = DeckGeometry.SnapScale(height / BaseHeight(rows));
        return Create(scale, StretchAt(width / scale), rows);
    }

    public SpellDeckGeometry WithRows(int rows) => Create(Scale, Stretch, rows);
    public SpellDeckGeometry WithStretch(float stretch) => Create(Scale, stretch, Rows);
    public SpellDeckGeometry WithWidth(float width) => Create(Scale, StretchAt(width / Scale), Rows);

    /// <summary>A corner resize measured from this geometry (the drag start), as <see cref="DeckGeometry.WithCornerSize"/>.</summary>
    public SpellDeckGeometry WithCornerSize(float width, float height)
    {
        float fromWidth = width / BaseWidthAt(Stretch);
        float fromHeight = height / BaseHeight(Rows);
        float raw = MathF.Abs(fromWidth - Scale) > MathF.Abs(fromHeight - Scale) ? fromWidth : fromHeight;
        return Create(raw, Stretch, Rows);
    }

    private DeckRect S(float x, float y, float w, float h) => new(x * Scale, y * Scale, w * Scale, h * Scale);

    private const float RowsTop = TabStrip + TopPad;

    public DeckRect Frame => new(0f, 0f, Width, Height);

    /// <summary>The round item socket, in the gutter, centred on the rows.</summary>
    public DeckRect ItemSlot => S(Pad + (Gutter - ItemSize) / 2f,
        (RowsTop + BaseHeight(Rows) - BottomPad - ItemSize) / 2f, ItemSize, ItemSize);

    /// <summary>The rule between the gutter and the slots, beside the rows (not the tab strip).</summary>
    public DeckRect Divider => S(Pad + Gutter - 1f, RowsTop, 1f, BaseHeight(Rows) - RowsTop - BottomPad);
    public DeckRect Slot(int row, int index) => S(SlotsLeft + index * SlotPitch, RowsTop + row * RowPitch, SlotSize, SlotSize);

    /// <summary>The tab strip's band: the bookmarks' height, from the slots' left edge to the right padding.</summary>
    public DeckRect TabStripBand => S(SlotsLeft, BookmarkTop, BaseWidthAt(Stretch) - SlotsLeft - Pad, TabStrip);

    /// <summary>Bookmark <paramref name="index"/> of <paramref name="count"/> tabs. Bookmarks are 24 wide
    /// with a 2 gap; when they and the <c>+</c> do not fit the strip they shrink evenly, down to 16.</summary>
    public DeckRect Bookmark(int index, int count) => BookmarkAt(index, count);

    /// <summary>The <c>+</c> bookmark, after the last tab (not shown at <see cref="SpellDeckTabs.MaxTabs"/>).</summary>
    public DeckRect Plus(int count) => BookmarkAt(count, count);

    /// <summary>Whether <paramref name="count"/> tabs show a <c>+</c>.</summary>
    public static bool ShowsPlus(int count) => count < SpellDeckTabs.MaxTabs;

    private DeckRect BookmarkAt(int position, int count)
    {
        float width = BookmarkWidthFor(count);
        return S(SlotsLeft + position * (width + BookmarkGap), BookmarkTop, width, TabStrip);
    }

    private float BookmarkWidthFor(int count)
    {
        int items = Math.Max(1, count + (ShowsPlus(count) ? 1 : 0));
        float room = BaseWidthAt(Stretch) - SlotsLeft - Pad;
        return Math.Clamp((room + BookmarkGap) / items - BookmarkGap, MinBookmarkWidth, BookmarkWidth);
    }

    /// <summary>What the point is over, with <paramref name="tabs"/> bookmarks in the strip.</summary>
    public SpellDeckHit HitAt(float x, float y, int tabs)
    {
        if (!Frame.Contains(x, y)) return new(SpellDeckZone.None, -1, -1);
        if (ItemSlot.Contains(x, y)) return new(SpellDeckZone.Item, -1, -1);
        DeckRect band = TabStripBand;
        if (y >= band.Y && y < band.Y + band.H)
        {
            for (int i = 0; i < tabs; i++)
                if (Bookmark(i, tabs).Contains(x, y)) return SpellDeckHit.ForBookmark(i);
            if (ShowsPlus(tabs) && Plus(tabs).Contains(x, y)) return new(SpellDeckZone.Plus, -1, -1);
        }
        for (int row = 0; row < Rows; row++)
        {
            DeckRect first = Slot(row, 0);
            if (y < first.Y || y >= first.Y + first.H || x < first.X) continue;
            int index = (int)((x - first.X) / (SlotPitch * Scale));
            if (index < SlotsPerRow && Slot(row, index).Contains(x, y))
                return new(SpellDeckZone.Slot, row, index);
        }
        return new(SpellDeckZone.Frame, -1, -1);
    }
}
