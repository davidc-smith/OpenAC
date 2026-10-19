namespace AcDream.Core.Chat;

public readonly record struct ChatTextTag(string Type, string Format, string Data)
{
    public bool TryGetCoordinate(out double eastWest, out double northSouth)
    {
        eastWest = 0d;
        northSouth = 0d;
        if (!string.Equals(Type, "COORD", StringComparison.OrdinalIgnoreCase)
            || !string.Equals(Format, "World", StringComparison.OrdinalIgnoreCase))
            return false;
        return TryParseCoordinatePair(Data, out eastWest, out northSouth);
    }

    public static bool TryParseCoordinatePair(string? text, out double eastWest, out double northSouth)
    {
        eastWest = 0d;
        northSouth = 0d;
        if (string.IsNullOrWhiteSpace(text))
            return false;
        string[] parts = text.Split(',', StringSplitOptions.TrimEntries);
        return parts.Length == 2
            && TryParseCoordinate(parts[0], 'N', 'S', out northSouth)
            && TryParseCoordinate(parts[1], 'E', 'W', out eastWest);
    }

    private static bool TryParseCoordinate(string text, char positive, char negative, out double value)
    {
        value = 0d;
        if (text.Length < 2)
            return false;
        char suffix = char.ToUpperInvariant(text[^1]);
        if (suffix != positive && suffix != negative)
            return false;
        if (!double.TryParse(text[..^1].Trim(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double magnitude)
            || magnitude < 0d)
            return false;
        value = suffix == negative ? -magnitude : magnitude;
        return true;
    }

    public bool TryGetIidString(out uint objectId, out string name)
    {
        objectId = 0;
        name = string.Empty;
        if (!string.Equals(Format, "IIDString", StringComparison.Ordinal))
            return false;

        int split = Data.IndexOf(':');
        if (split <= 0 || split == Data.Length - 1)
            return false;
        if (!uint.TryParse(
                Data.AsSpan(0, split),
                System.Globalization.NumberStyles.Integer,
                System.Globalization.CultureInfo.InvariantCulture,
                out objectId))
        {
            return false;
        }

        name = Data[(split + 1)..];
        return true;
    }
}

public enum ChatSpanRole
{
    Body,

    Timestamp,
}

public readonly record struct ChatTextSpan(
    string Text,
    ChatTextTag? Tag,
    ChatSpanRole Role = ChatSpanRole.Body);

public static class ChatTagMarkup
{
    /// <summary>
    /// Whether a plugin's line holds at least one link tag
    /// <see cref="ParseLinks"/> reads: a well-formed
    /// <c>&lt;COORD:World:42.1N, 33.5E&gt;</c>. Any other angle-bracket text,
    /// such as "Press &lt;Ctrl:F&gt;", is not a tag.
    /// </summary>
    public static bool ContainsTag(string? text)
    {
        if (string.IsNullOrEmpty(text))
            return false;
        for (int open = text.IndexOf('<'); open >= 0; open = text.IndexOf('<', open + 1))
        {
            int close = text.IndexOf('>', open + 1);
            if (close < 0)
                return false;
            if (TryParseLinkTag(text[(open + 1)..close]) is not null)
                return true;
        }
        return false;
    }

    /// <summary>
    /// Reads the link tags in a line a plugin posted. Only a well-formed
    /// <c>&lt;COORD:World:pair&gt;</c> opens a link, and only its own
    /// <c>&lt;\COORD&gt;</c> closes it; every other angle-bracket text is shown
    /// as written. A link left open runs to the end of the line.
    /// </summary>
    public static IReadOnlyList<ChatTextSpan> ParseLinks(string? text)
    {
        if (string.IsNullOrEmpty(text))
            return Array.Empty<ChatTextSpan>();

        var spans = new List<ChatTextSpan>();
        ChatTextTag? open = null;
        int runStart = 0;

        for (int i = text.IndexOf('<'); i >= 0; i = text.IndexOf('<', i + 1))
        {
            int close = text.IndexOf('>', i + 1);
            if (close < 0)
                break;

            string inner = text[(i + 1)..close];
            ChatTextTag? next;
            if (TryParseLinkTag(inner) is { } start)
                next = start;
            else if (open is { } current && IsEndTag(inner, current))
                next = null;
            else
                continue;   // not markup: shown as written

            if (i > runStart)
                spans.Add(new ChatTextSpan(text[runStart..i], open));
            open = next;
            runStart = close + 1;
            i = close;
        }

        if (runStart < text.Length)
            spans.Add(new ChatTextSpan(text[runStart..], open));

        return spans;
    }

    /// <summary>A plugin's line with its link tags removed: what <see cref="ParseLinks"/> shows.</summary>
    public static string ToPlainText(string? text) =>
        string.Concat(ParseLinks(text).Select(static span => span.Text));

    /// <summary>
    /// The spans with every coordinate tag dropped and its caption kept as
    /// plain text. What a player or the server writes may hold a coordinate
    /// tag; it is shown in the line's colour and is not a link.
    /// </summary>
    public static IReadOnlyList<ChatTextSpan> WithoutCoordinateTags(IReadOnlyList<ChatTextSpan> spans)
    {
        ArgumentNullException.ThrowIfNull(spans);
        ChatTextSpan[]? result = null;
        for (int i = 0; i < spans.Count; i++)
        {
            if (spans[i].Tag is not { } tag || !IsCoordinateType(tag.Type))
                continue;
            result ??= spans.ToArray();
            result[i] = spans[i] with { Tag = null };
        }
        return result ?? spans;
    }

    private static bool IsCoordinateType(string type) =>
        string.Equals(type, "COORD", StringComparison.OrdinalIgnoreCase);

    private static ChatTextTag? TryParseLinkTag(string inner) =>
        TryParseStartTag(inner) is { } tag
            && IsCoordinateType(tag.Type)
            && tag.TryGetCoordinate(out _, out _)
            ? tag
            : null;

    private static bool IsEndTag(string inner, ChatTextTag open) =>
        inner.Length == open.Type.Length + 1
        && inner[0] == '\\'
        && string.Equals(inner[1..], open.Type, StringComparison.OrdinalIgnoreCase);

    public static IReadOnlyList<ChatTextSpan> Parse(string? text)
    {
        if (string.IsNullOrEmpty(text))
            return Array.Empty<ChatTextSpan>();
        if (text.IndexOf('<') < 0)
            return new[] { new ChatTextSpan(text, null) };

        var spans = new List<ChatTextSpan>();
        ChatTextTag? open = null;
        int runStart = 0;

        for (int i = 0; i < text.Length; i++)
        {
            if (text[i] != '<')
                continue;

            int close = text.IndexOf('>', i + 1);
            if (close < 0)
                break;   // unterminated: the rest is plain text

            // Flush the text before this marker under whatever tag was open.
            if (i > runStart)
                spans.Add(new ChatTextSpan(text[runStart..i], open));

            open = TryParseStartTag(text[(i + 1)..close]);
            runStart = close + 1;
            i = close;
        }

        if (runStart < text.Length)
            spans.Add(new ChatTextSpan(text[runStart..], open));

        return spans;
    }

    private static ChatTextTag? TryParseStartTag(string inner)
    {
        int first = inner.IndexOf(':');
        if (first <= 0 || first == inner.Length - 1)
            return null;

        int second = inner.IndexOf(':', first + 1);
        if (second < 0)
        {
            // TYPE:FORMAT with no payload. Still a tag — the data is empty.
            return new ChatTextTag(inner[..first], inner[(first + 1)..], string.Empty);
        }

        return new ChatTextTag(
            inner[..first],
            inner[(first + 1)..second],
            inner[(second + 1)..]);
    }
}
