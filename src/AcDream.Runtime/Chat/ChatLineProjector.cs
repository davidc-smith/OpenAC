using System.Collections.Generic;
using System.Linq;
using AcDream.Core.Chat;

namespace AcDream.Runtime.Chat;

/// <summary>One entry worded as it reads on screen, split at its tags, without a timestamp.</summary>
/// <param name="Text">The words.</param>
/// <param name="Spans">The text split at its tags (a tell tag, a coordinate link), or null when it has none.
/// The spans always rejoin to <paramref name="Text"/>.</param>
public readonly record struct ChatProjectedLine(string Text, IReadOnlyList<ChatTextSpan>? Spans);

/// <summary>
/// Words one chat entry and reads its tags and coordinate links. Every chat
/// front end starts here and adds its own timestamp, prefix and colours.
/// </summary>
public static class ChatLineProjector
{
    public static ChatProjectedLine Project(ChatEntry entry)
    {
        string markup = ChatLineWording.FormatTagged(entry);
        IReadOnlyList<ChatTextSpan>? spans;
        if (entry.AllowsMarkup)
        {
            // A plugin's own line: its link tags and escapes are read, and a
            // coordinate pair it writes in plain text is a link too.
            IReadOnlyList<ChatTextSpan> read = ChatTagMarkup.ParseLinks(markup);
            read = ChatCoordinateLinks.Link(read) ?? read;
            if (read.Any(static span => span.Tag is not null))
            {
                spans = read;
            }
            else
            {
                spans = null;
                markup = string.Concat(read.Select(static span => span.Text));
            }
        }
        else
        {
            // Every other line, the server's, the character's own and a
            // combat line among them, is read for tags the same way: a tag is
            // hidden, and the speaker's name in its tell tag is a link that
            // starts a tell. A coordinate tag in the words is shown as plain
            // text and its hidden value goes nowhere. A coordinate pair the
            // line shows is a link to exactly that pair, except in combat
            // text, so a click reports what the reader sees.
            spans = ChatTagMarkup.ParseLine(markup);
            if (entry.Kind != ChatKind.Combat)
                spans = ChatCoordinateLinks.LinkShown(spans) ?? spans;
            if (!spans.Any(static span => span.Tag is not null))
            {
                markup = string.Concat(spans.Select(static span => span.Text));
                spans = null;
            }
        }
        string text = spans is null
            ? markup
            : string.Concat(spans.Select(span => span.Text));
        return new ChatProjectedLine(text, spans);
    }
}
