namespace AcDream.App.UI.Hud;

internal enum ChatDeckZone { None, Frame, Tab, SearchStud, Chip, PinStud, CollapseStud }

internal readonly record struct ChatDeckHit(ChatDeckZone Zone, int Tab = -1);

/// <summary>One tab as the top row shows it: its name and its badge text ("" for none).</summary>
internal readonly record struct ChatDeckTabLabel(string Name, string Badge);

internal static class ChatDeckBadges
{
    public static string Text(int unread) => unread <= 0 ? "" : unread > 99 ? "99+" : unread.ToString(System.Globalization.CultureInfo.InvariantCulture);
}

/// <summary>Where everything in the pinned Chat Deck window goes, for one size and set of tabs.</summary>
internal sealed class ChatDeckGeometry
{
    public const float Pad = 8f;
    /// <summary>How far a wrapped continuation line is indented when Wrap Indent is on.</summary>
    public const float WrapIndent = 16f;
    public const float TabHeight = 20f;
    public const float TabPadX = 9f;
    public const float TabGap = 3f;
    public const float MinTabWidth = 34f;
    public const float BadgePadX = 4f;
    public const float BadgeGap = 5f;
    public const float BadgeHeight = 14f;
    public const float StudSize = 20f;
    public const float StudGap = 4f;
    public const float SearchHeight = 22f;
    public const float InputHeight = 26f;
    public const float ChipArrow = 12f;
    public const float MarkerWidth = 96f;
    public const float MarkerHeight = 18f;
    public const float MinWidth = 280f;
    public const float MinHeight = 140f;
    public const float DefaultWidth = 460f;
    public const float DefaultHeight = 220f;

    public required DeckRect Frame { get; init; }
    public required IReadOnlyList<DeckRect> Tabs { get; init; }
    public required DeckRect SearchStud { get; init; }
    public required DeckRect CollapseStud { get; init; }
    /// <summary>Zero width while search is closed.</summary>
    public required DeckRect SearchField { get; init; }
    public required DeckRect Transcript { get; init; }
    public required DeckRect NewLines { get; init; }
    public required DeckRect InputRow { get; init; }
    public required DeckRect Chip { get; init; }
    public required DeckRect Input { get; init; }

    public static float BadgeWidth(string badge, Func<string, float> measure) =>
        badge.Length == 0 ? 0f : measure(badge) + 2 * BadgePadX;

    public static ChatDeckGeometry Create(
        float width, float height, IReadOnlyList<ChatDeckTabLabel> tabs, bool searchOpen, string chipText,
        Func<string, float> measureLabel)
    {
        var collapse = new DeckRect(width - Pad - StudSize, Pad, StudSize, StudSize);
        var stud = collapse with { X = collapse.X - StudGap - StudSize };
        float tabsRight = stud.X - TabGap;
        float room = MathF.Max(0f, tabsRight - Pad);

        var natural = tabs.Select(t =>
            2 * TabPadX + measureLabel(t.Name)
            + (t.Badge.Length > 0 ? BadgeGap + BadgeWidth(t.Badge, measureLabel) : 0f)).ToArray();
        float gaps = TabGap * Math.Max(0, tabs.Count - 1);
        bool fits = natural.Sum() + gaps <= room;
        float shared = tabs.Count == 0 ? 0f : MathF.Max(MinTabWidth, (room - gaps) / tabs.Count);

        var rects = new List<DeckRect>(tabs.Count);
        float x = Pad;
        for (int i = 0; i < tabs.Count; i++)
        {
            float w = fits ? natural[i] : shared;
            if (x + w > tabsRight + 0.01f)
            {
                rects.Add(new DeckRect(x, Pad, 0f, 0f));
                continue;
            }
            rects.Add(new DeckRect(x, Pad, w, TabHeight));
            x += w + TabGap;
        }

        float top = Pad + TabHeight + 4f;
        var search = new DeckRect(Pad, top, 0f, SearchHeight);
        if (searchOpen)
        {
            search = search with { W = width - 2 * Pad };
            top += SearchHeight + 4f;
        }

        var inputRow = new DeckRect(Pad, height - Pad - InputHeight, width - 2 * Pad, InputHeight);
        float chipW = MathF.Min(inputRow.W / 2f, measureLabel(chipText) + 2 * TabPadX + ChipArrow);
        var chip = new DeckRect(inputRow.X, inputRow.Y, chipW, InputHeight);
        var input = new DeckRect(chip.X + chip.W + 4f, inputRow.Y, MathF.Max(0f, inputRow.W - chip.W - 4f), InputHeight);
        var transcript = new DeckRect(Pad, top, width - 2 * Pad, MathF.Max(0f, inputRow.Y - 4f - top));
        var marker = new DeckRect(
            transcript.X + (transcript.W - MarkerWidth) / 2f,
            transcript.Y + transcript.H - MarkerHeight - 4f,
            MarkerWidth, MarkerHeight);

        return new ChatDeckGeometry
        {
            Frame = new DeckRect(0f, 0f, width, height),
            Tabs = rects,
            SearchStud = stud,
            CollapseStud = collapse,
            SearchField = search,
            Transcript = transcript,
            NewLines = marker,
            InputRow = inputRow,
            Chip = chip,
            Input = input,
        };
    }

    public ChatDeckHit HitAt(float x, float y)
    {
        for (int i = 0; i < Tabs.Count; i++)
            if (Tabs[i].W > 0 && Tabs[i].Contains(x, y)) return new(ChatDeckZone.Tab, i);
        if (SearchStud.Contains(x, y)) return new(ChatDeckZone.SearchStud);
        if (CollapseStud.Contains(x, y)) return new(ChatDeckZone.CollapseStud);
        if (Chip.Contains(x, y)) return new(ChatDeckZone.Chip);
        return Frame.Contains(x, y) ? new(ChatDeckZone.Frame) : new(ChatDeckZone.None);
    }
}
