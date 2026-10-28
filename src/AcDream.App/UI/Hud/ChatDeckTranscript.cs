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
/// <param name="ColourKey">
/// What <paramref name="ChannelColour"/> reads (the appearance settings), compared by reference:
/// a new key colours every line again, the same key reuses each line's colours.
/// </param>
internal readonly record struct ChatDeckTranscriptStyle(
    float WrapWidth,
    Func<string, float> Measure,
    object? FontKey,
    ChatDeckTimestamps Timestamps,
    Func<ChatDeckLine, Vector4> ChannelColour,
    Vector4 TagColour,
    Vector4 TimestampColour,
    string Search,
    object? ColourKey = null,
    bool ChannelPrefix = true,
    float WrapIndent = 0f);

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
/// every tab it is in), wrapped again only when the width, font or timestamp mode
/// changes, and coloured again only when that or the colours change, so a busy tab
/// costs a copy per frame for the lines it already had, not a re-colouring.
/// </summary>
internal sealed class ChatDeckTranscript
{
    private sealed class Cached
    {
        public required ChatProjectedLine Projected;
        public ChatProjectedLine Shown;
        public bool Prefix = true;
        public float Indent;
        public float WrapWidth = float.NaN;
        public object? FontKey;
        public ChatDeckTimestamps Stamps;
        public string Stamp = "";
        public string[] Fragments = [];
        public int[] Starts = [];

        // The finished visual lines, valid while the wrap and the colour inputs below hold.
        public bool Shaped;
        public object? ColourKey;
        public Vector4 TagColour;
        public Vector4 TimestampColour;
        public UiText.Line[] Lines = [];
        public IReadOnlyList<UiText.TextRun>?[] Runs = [];
        public IReadOnlyList<(int Start, int Length, ChatTextTag Tag)>?[] Tags = [];
        public UiText.LineKey[] Keys = [];
        public Vector4? Marker;
    }

    private readonly ConditionalWeakTable<ChatDeckLine, Cached> _cache = new();

    internal int ProjectionsForTest { get; private set; }
    internal int ColourPassesForTest { get; private set; }

    public ChatDeckTranscriptLayout Build(IReadOnlyList<ChatDeckLine> lines, ChatDeckTranscriptStyle style)
    {
        var layout = new ChatDeckTranscriptLayout();
        foreach (ChatDeckLine line in lines)
        {
            Cached c = Get(line);
            if (style.Search.Length > 0
                && c.Projected.Text.IndexOf(style.Search, StringComparison.OrdinalIgnoreCase) < 0)
                continue;
            bool rewrapped = Wrap(c, line, style);
            if (rewrapped || !c.Shaped || !ReferenceEquals(c.ColourKey, style.ColourKey)
                || c.TagColour != style.TagColour || c.TimestampColour != style.TimestampColour)
            {
                ColourPassesForTest++;
                Shape(line, c, style);
            }
            AddLine(layout, c);
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

    /// <summary>Wraps the line again if the width, font or timestamp mode changed; true when it did.</summary>
    private static bool Wrap(Cached c, ChatDeckLine line, ChatDeckTranscriptStyle style)
    {
        if (c.WrapWidth.Equals(style.WrapWidth) && ReferenceEquals(c.FontKey, style.FontKey) && c.Stamps == style.Timestamps
            && c.Prefix == style.ChannelPrefix && c.Indent.Equals(style.WrapIndent))
            return false;
        c.WrapWidth = style.WrapWidth;
        c.FontKey = style.FontKey;
        c.Stamps = style.Timestamps;
        c.Prefix = style.ChannelPrefix;
        c.Indent = style.WrapIndent;
        c.Shown = style.ChannelPrefix ? c.Projected : ChatDeckPrefix.Strip(c.Projected, line.Entry);
        c.Stamp = Stamp(line.Entry.Received, style.Timestamps);
        string full = c.Stamp + c.Shown.Text;
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
        return true;
    }

    internal static string Stamp(DateTime receivedUtc, ChatDeckTimestamps mode) => mode switch
    {
        ChatDeckTimestamps.HourMinute => receivedUtc.ToLocalTime().ToString(@"H\:mm ", CultureInfo.InvariantCulture),
        ChatDeckTimestamps.HourMinuteSecond => receivedUtc.ToLocalTime().ToString(@"H\:mm\:ss ", CultureInfo.InvariantCulture),
        _ => "",
    };

    private static void AddLine(ChatDeckTranscriptLayout layout, Cached c)
    {
        layout.Lines.AddRange(c.Lines);
        layout.Runs.AddRange(c.Runs);
        layout.Tags.AddRange(c.Tags);
        layout.Keys.AddRange(c.Keys);
        for (int i = 0; i < c.Lines.Length; i++) layout.Markers.Add(c.Marker);
    }

    /// <summary>Colours the line's fragments and keeps them as its finished visual lines.</summary>
    private static void Shape(ChatDeckLine line, Cached c, ChatDeckTranscriptStyle style)
    {
        Vector4 lineColour = line.LineColour is uint argb ? Argb(argb) : style.ChannelColour(line);

        // The line's spans with the timestamp in front, as the feed builds them, so tag
        // ranges and colours are measured over the text as shown.
        var spans = new List<ChatTextSpan>();
        if (c.Stamp.Length > 0) spans.Add(new ChatTextSpan(c.Stamp, null, ChatSpanRole.Timestamp));
        if (c.Shown.Spans is { } own) spans.AddRange(own);
        else spans.Add(new ChatTextSpan(c.Shown.Text, null));

        Vector4[] colours = Colours(line, c, spans, lineColour, style);
        int count = c.Fragments.Length;
        c.Lines = new UiText.Line[count];
        c.Runs = new IReadOnlyList<UiText.TextRun>?[count];
        c.Tags = new IReadOnlyList<(int Start, int Length, ChatTextTag Tag)>?[count];
        c.Keys = new UiText.LineKey[count];
        for (int i = 0; i < count; i++)
        {
            string frag = c.Fragments[i];
            int at = c.Starts[i];
            c.Lines[i] = new UiText.Line(frag, lineColour, i > 0 ? style.WrapIndent : 0f);
            c.Runs[i] = Runs(frag, at, colours, lineColour);
            c.Tags[i] = frag.Length == 0 ? null : ChatTranscriptRenderer.TaggedRangesForFragment(spans, at, frag.Length);
            // The offset into the shown text, timestamp included: UiText anchors a selection as
            // Start + column, and its columns count the timestamp on the first line.
            c.Keys[i] = new UiText.LineKey(line.Sequence, at);
        }
        c.Marker = line.LineColour is uint bar ? Argb(bar) : null;
        c.ColourKey = style.ColourKey;
        c.TagColour = style.TagColour;
        c.TimestampColour = style.TimestampColour;
        c.Shaped = true;
    }

    /// <summary>One colour per character of the shown line (timestamp included).</summary>
    private static Vector4[] Colours(
        ChatDeckLine line, Cached c, List<ChatTextSpan> spans, Vector4 lineColour, ChatDeckTranscriptStyle style)
    {
        int stamp = c.Stamp.Length;
        var colours = new Vector4[stamp + c.Shown.Text.Length];
        Array.Fill(colours, lineColour);
        for (int i = 0; i < stamp; i++) colours[i] = style.TimestampColour;

        if (line.MatchColour is uint match)
        {
            Vector4 ink = Argb(match);
            foreach (ChatDeckSpan span in line.MatchSpans)
            {
                int origin = span.Field switch
                {
                    ChatDeckMatchField.Text => c.Shown.Text.LastIndexOf(line.Entry.Text, StringComparison.Ordinal),
                    ChatDeckMatchField.Sender => line.Entry.Sender.Length == 0 ? -1
                        : c.Shown.Text.IndexOf(line.Entry.Sender, StringComparison.Ordinal),
                    _ => 0,
                };
                if (origin < 0) continue;
                // Line spans are measured over the full worded line; the shown line may have lost its prefix,
                // so they start that far to the left (the clip below drops what falls inside the prefix).
                if (span.Field is not (ChatDeckMatchField.Text or ChatDeckMatchField.Sender))
                    origin = c.Shown.Text.Length - c.Projected.Text.Length;
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

    /// <summary>
    /// The fragment as runs of one colour; null when it is all the line colour. A character
    /// past the colours (a fragment placed where WrapText's text could not be found) takes
    /// the line colour.
    /// </summary>
    internal static IReadOnlyList<UiText.TextRun>? Runs(string frag, int at, Vector4[] colours, Vector4 lineColour)
    {
        if (frag.Length == 0) return null;
        Vector4 ColourAt(int i) => (uint)(at + i) < (uint)colours.Length ? colours[at + i] : lineColour;
        var runs = new List<UiText.TextRun>();
        bool plain = true;
        int start = 0;
        for (int i = 1; i <= frag.Length; i++)
        {
            if (i < frag.Length && ColourAt(i) == ColourAt(start)) continue;
            Vector4 colour = ColourAt(start);
            plain &= colour == lineColour;
            runs.Add(new UiText.TextRun(frag[start..i], colour));
            start = i;
        }
        return plain ? null : runs;
    }

    internal static Vector4 Argb(uint argb) => new(
        ((argb >> 16) & 0xFF) / 255f, ((argb >> 8) & 0xFF) / 255f, (argb & 0xFF) / 255f, ((argb >> 24) & 0xFF) / 255f);
}
