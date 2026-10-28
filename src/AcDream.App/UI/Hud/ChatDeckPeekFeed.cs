using AcDream.Core.Chat.Deck;

namespace AcDream.App.UI.Hud;

/// <summary>
/// The lines shown beside the collapsed strip: the newest <see cref="MaxLines"/> that went to a
/// tab with peek on, or are alerts, never your own. Each lives <see cref="LifeSeconds"/> and
/// fades over its last <see cref="FadeSeconds"/>. Oldest first.
/// </summary>
internal sealed class ChatDeckPeekFeed(Func<ChatDeckLine, string> text)
{
    public const int MaxLines = 3;
    public const double LifeSeconds = 6.0;
    public const double FadeSeconds = 1.0;

    public readonly record struct Entry(ChatDeckLine Line, string Text, double Age);

    private readonly List<Entry> _lines = [];

    public IReadOnlyList<Entry> Lines => _lines;

    public static bool Peeks(ChatDeckDelivery delivery, IReadOnlyList<ChatDeckTab> tabs) =>
        !delivery.Line.Facts.IsOwn
        && (delivery.Line.Alert || delivery.TabIds.Any(id => tabs.Any(t => t.Id == id && t.Peek)));

    public void Add(IReadOnlyList<ChatDeckDelivery> deliveries, IReadOnlyList<ChatDeckTab> tabs)
    {
        foreach (ChatDeckDelivery delivery in deliveries)
        {
            if (!Peeks(delivery, tabs)) continue;
            _lines.Add(new Entry(delivery.Line, text(delivery.Line), 0));
            if (_lines.Count > MaxLines) _lines.RemoveAt(0);
        }
    }

    public void Advance(double deltaSeconds)
    {
        for (int i = _lines.Count - 1; i >= 0; i--)
        {
            Entry aged = _lines[i] with { Age = _lines[i].Age + deltaSeconds };
            if (aged.Age >= LifeSeconds) _lines.RemoveAt(i);
            else _lines[i] = aged;
        }
    }

    /// <summary>1 until the last second of a line's life, then down to 0.</summary>
    public static float Alpha(double age) =>
        age <= LifeSeconds - FadeSeconds ? 1f : (float)Math.Max(0, (LifeSeconds - age) / FadeSeconds);

    public void Clear() => _lines.Clear();
}
