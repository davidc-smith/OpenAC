using System.Globalization;
using System.Text.RegularExpressions;
using AcDream.Plugin.Abstractions;

namespace AcDream.Core.Chat;

/// <summary>
/// Turns the coordinate pairs written in a chat line ("meet at 42.1N 33.5E")
/// into the same <c>COORD:World</c> links a line's own markup makes, so a click
/// on one reaches plugins as <see cref="PluginChatLinkKind.Coordinate"/>.
/// </summary>
public static partial class ChatCoordinateLinks
{
    // The pair pattern PluginChatCoordinateParser reads; each match is handed
    // back to it, so a pair is linked exactly when the parser reads it.
    [GeneratedRegex(@"(?<a>\d+(?:\.\d+)?)\s*(?<ad>[NSEW])\s*(?:[,;/|]|\s+)\s*(?<b>\d+(?:\.\d+)?)\s*(?<bd>[NSEW])", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex PairPattern();

    /// <summary>
    /// Splits every plain body span at the coordinate pairs it holds, each pair
    /// becoming a span of its own tagged as a coordinate. Tagged spans and the
    /// timestamp are kept as they are.
    /// </summary>
    /// <returns>The new spans, which rejoin to the same text; null when the
    /// line holds no pair.</returns>
    public static IReadOnlyList<ChatTextSpan>? Link(IReadOnlyList<ChatTextSpan> spans)
    {
        ArgumentNullException.ThrowIfNull(spans);
        List<ChatTextSpan>? result = null;
        for (int i = 0; i < spans.Count; i++)
        {
            ChatTextSpan span = spans[i];
            if (span.Tag is not null || span.Role != ChatSpanRole.Body)
            {
                result?.Add(span);
                continue;
            }

            int runStart = 0;
            foreach (Match match in PairPattern().Matches(span.Text))
            {
                if (!PluginChatCoordinateParser.TryParse(match.Value, out PluginChatCoordinate coordinate))
                    continue;
                if (result is null)
                {
                    result = new List<ChatTextSpan>(spans.Count + 2);
                    for (int j = 0; j < i; j++)
                        result.Add(spans[j]);
                }
                if (match.Index > runStart)
                    result.Add(span with { Text = span.Text[runStart..match.Index] });
                result.Add(span with { Text = match.Value, Tag = TagFor(coordinate) });
                runStart = match.Index + match.Length;
            }

            if (result is null)
                continue;
            if (runStart < span.Text.Length)
                result.Add(span with { Text = span.Text[runStart..] });
        }
        return result;
    }

    /// <summary>The link a coordinate is carried by, in the chat markup's own form.</summary>
    public static ChatTextTag TagFor(PluginChatCoordinate coordinate) =>
        new(
            "COORD",
            "World",
            $"{Axis(coordinate.NorthSouth, 'N', 'S')}, {Axis(coordinate.EastWest, 'E', 'W')}");

    private static string Axis(double value, char positive, char negative) =>
        Math.Abs(value).ToString("R", CultureInfo.InvariantCulture)
            + (value < 0d ? negative : positive);
}
