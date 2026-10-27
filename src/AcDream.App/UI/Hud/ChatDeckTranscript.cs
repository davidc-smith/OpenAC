using System.Globalization;
using System.Numerics;
using System.Runtime.CompilerServices;
using AcDream.App.UI.Layout;
using AcDream.Core.Chat;
using AcDream.Core.Chat.Deck;
using AcDream.Runtime.Chat;

namespace AcDream.App.UI.Hud;

/// <summary>How a transcript is drawn: wrap width and font, timestamps, colours, and the search text ("" for none).</summary>
/// <param name="FontKey">The font the widths were measured with; a new font wraps again.</param>
internal readonly record struct ChatDeckTranscriptStyle(
    float WrapWidth,
    Func<string, float> Measure,
    object? FontKey,
    ChatDeckTimestamps Timestamps,
    Func<ChatDeckLine, Vector4> ChannelColour,
    Vector4 TagColour,
    Vector4 TimestampColour,
    string Search);

/// <summary>A tab laid out for a <see cref="UiText"/>: one entry per visual line in every list.</summary>
internal sealed class ChatDeckTranscriptLayout
{
    public List<UiText.Line> Lines { get; } = [];
    public List<IReadOnlyList<UiText.TextRun>?> Runs { get; } = [];
    public List<IReadOnlyList<(int Start, int Length, ChatTextTag Tag)>?> Tags { get; } = [];
    public List<UiText.LineKey> Keys { get; } = [];
    public List<Vector4?> Markers { get; } = [];
}

/// <summary>
/// Lays a tab's lines out for the transcript. A line is worded once (it is shared by
/// every tab it is in) and wrapped again only when the width, font or timestamp
/// mode changes, so a busy tab costs a copy per frame, not a re-wording.
/// </summary>
internal sealed class ChatDeckTranscript
{
    private sealed class Cached
    {
        public required ChatProjectedLine Projected;
        public float WrapWidth = float.NaN;
        public object? FontKey;
        public ChatDeckTimestamps Stamps;
        public string Stamp = "";
        public string[] Fragments = [];
        public int[] Starts = [];
    }

    private readonly ConditionalWeakTable<ChatDeckLine, Cached> _cache = new();

    internal int ProjectionsForTest { get; private set; }

    public ChatDeckTranscriptLayout Build(IReadOnlyList<ChatDeckLine> lines, ChatDeckTranscriptStyle style)
    {
        var layout = new ChatDeckTranscriptLayout();
        foreach (ChatDeckLine line in lines)
        {
            Cached c = Get(line);
            if (style.Search.Length > 0
                && c.Projected.Text.IndexOf(style.Search, StringComparison.OrdinalIgnoreCase) < 0)
                continue;
            Wrap(c, line, style);
            AddLine(layout, line, c, style);
        }
        return layout;
    }

    private Cached Get(ChatDeckLine line)
    {
        if (_cache.TryGetValue(line, out Cached? cached)) return cached;
        ProjectionsForTest++;
        cached = new Cached { Projected = ChatLineProjector.Project(line.Entry) };
        _cache.AddOrUpdate(line, cached);
        return cached;
    }

    private static void Wrap(Cached c, ChatDeckLine line, ChatDeckTranscriptStyle style)
    {
        if (c.WrapWidth.Equals(style.WrapWidth) && ReferenceEquals(c.FontKey, style.FontKey) && c.Stamps == style.Timestamps)
            return;
        c.WrapWidth = style.WrapWidth;
        c.FontKey = style.FontKey;
        c.Stamps = style.Timestamps;
        c.Stamp = Stamp(line.Entry.Received, style.Timestamps);
        string full = c.Stamp + c.Projected.Text;
        var fragments = new List<string>();
        var starts = new List<int>();
        int searchFrom = 0;
        foreach (string frag in ChatTranscriptRenderer.WrapText(full, style.WrapWidth, style.Measure))
        {
            int at = frag.Length == 0 ? searchFrom : full.IndexOf(frag, searchFrom, StringComparison.Ordinal);
            if (at < 0) at = searchFrom;
            else if (frag.Length > 0) searchFrom = at + frag.Length;
            fragments.Add(frag);
            starts.Add(at);
        }
        c.Fragments = [.. fragments];
        c.Starts = [.. starts];
    }

    internal static string Stamp(DateTime receivedUtc, ChatDeckTimestamps mode) => mode switch
    {
        ChatDeckTimestamps.HourMinute => receivedUtc.ToLocalTime().ToString(@"H\:mm ", CultureInfo.InvariantCulture),
        ChatDeckTimestamps.HourMinuteSecond => receivedUtc.ToLocalTime().ToString(@"H\:mm\:ss ", CultureInfo.InvariantCulture),
        _ => "",
    };

    private static void AddLine(ChatDeckTranscriptLayout layout, ChatDeckLine line, Cached c, ChatDeckTranscriptStyle style)
    {
        Vector4 lineColour = line.LineColour is uint argb ? Argb(argb) : style.ChannelColour(line);
        Vector4? marker = line.LineColour is uint bar ? Argb(bar) : null;

        // The line's spans with the timestamp in front, as the feed builds them, so tag
        // ranges and colours are measured over the text as shown.
        var spans = new List<ChatTextSpan>();
        if (c.Stamp.Length > 0) spans.Add(new ChatTextSpan(c.Stamp, null, ChatSpanRole.Timestamp));
        if (c.Projected.Spans is { } own) spans.AddRange(own);
        else spans.Add(new ChatTextSpan(c.Projected.Text, null));

        Vector4[] colours = Colours(line, c, spans, lineColour, style);
        for (int i = 0; i < c.Fragments.Length; i++)
        {
            string frag = c.Fragments[i];
            int at = c.Starts[i];
            layout.Lines.Add(new UiText.Line(frag, lineColour));
            layout.Runs.Add(Runs(frag, at, colours, lineColour));
            layout.Tags.Add(frag.Length == 0 ? null : ChatTranscriptRenderer.TaggedRangesForFragment(spans, at, frag.Length));
            layout.Keys.Add(new UiText.LineKey(line.Sequence, Math.Max(0, at - c.Stamp.Length)));
            layout.Markers.Add(marker);
        }
    }

    /// <summary>One colour per character of the shown line (timestamp included).</summary>
    private static Vector4[] Colours(
        ChatDeckLine line, Cached c, List<ChatTextSpan> spans, Vector4 lineColour, ChatDeckTranscriptStyle style)
    {
        int stamp = c.Stamp.Length;
        var colours = new Vector4[stamp + c.Projected.Text.Length];
        Array.Fill(colours, lineColour);
        for (int i = 0; i < stamp; i++) colours[i] = style.TimestampColour;

        if (line.MatchColour is uint match)
        {
            Vector4 ink = Argb(match);
            foreach (ChatDeckSpan span in line.MatchSpans)
            {
                int origin = span.Field switch
                {
                    ChatDeckMatchField.Text => c.Projected.Text.LastIndexOf(line.Entry.Text, StringComparison.Ordinal),
                    ChatDeckMatchField.Sender => line.Entry.Sender.Length == 0 ? -1
                        : c.Projected.Text.IndexOf(line.Entry.Sender, StringComparison.Ordinal),
                    _ => 0,
                };
                if (origin < 0) continue;
                int from = stamp + origin + span.Start;
                int to = Math.Min(colours.Length, from + span.Length);
                for (int i = Math.Max(stamp, from); i < to; i++) colours[i] = ink;
            }
        }

        int at = 0;
        foreach (ChatTextSpan span in spans)
        {
            if (span.Tag is { } tag && ChatTranscriptRenderer.DrawsInTagColor(tag))
                for (int i = at; i < at + span.Text.Length && i < colours.Length; i++) colours[i] = style.TagColour;
            at += span.Text.Length;
        }
        return colours;
    }

    /// <summary>The fragment as runs of one colour; null when it is all the line colour.</summary>
    private static IReadOnlyList<UiText.TextRun>? Runs(string frag, int at, Vector4[] colours, Vector4 lineColour)
    {
        if (frag.Length == 0) return null;
        var runs = new List<UiText.TextRun>();
        bool plain = true;
        int start = 0;
        for (int i = 1; i <= frag.Length; i++)
        {
            if (i < frag.Length && colours[at + i] == colours[at + start]) continue;
            Vector4 colour = colours[at + start];
            plain &= colour == lineColour;
            runs.Add(new UiText.TextRun(frag[start..i], colour));
            start = i;
        }
        return plain ? null : runs;
    }

    internal static Vector4 Argb(uint argb) => new(
        ((argb >> 16) & 0xFF) / 255f, ((argb >> 8) & 0xFF) / 255f, (argb & 0xFF) / 255f, ((argb >> 24) & 0xFF) / 255f);
}
