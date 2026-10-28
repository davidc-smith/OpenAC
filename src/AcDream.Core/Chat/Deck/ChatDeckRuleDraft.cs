using System.Text;
using System.Text.RegularExpressions;

namespace AcDream.Core.Chat.Deck;

/// <summary>
/// A rule written from a line in the transcript: a Contains on its message text, on its channel,
/// optionally with the sender's name and the numbers generalised (which makes it a Regex).
/// </summary>
public static class ChatDeckRuleDraft
{
    public const uint DefaultColour = 0xFFFFD24Au;
    public const string NameMark = "[any name]";
    public const string NumberMark = "[any number]";
    private const string NamePattern = ".+?";
    private const string NumberPattern = @"\d+(?:[.,]\d+)*";
    private static readonly Regex Numbers = new(NumberPattern, RegexOptions.CultureInvariant);

    private enum PartKind { Literal, Name, Number }
    private readonly record struct Part(PartKind Kind, string Text);

    public static ChatDeckRule FromLine(
        ChatDeckLineFacts facts, ChatDeckRuleAction action, bool anySender = false, bool anyNumber = false)
    {
        (ChatDeckMatchKind kind, string pattern) = Pattern(facts, anySender, anyNumber);
        return new ChatDeckRule
        {
            Id = "", Kind = kind, Pattern = pattern, Field = ChatDeckMatchField.Text,
            Channels = new HashSet<ChatDeckChannel> { facts.Channel },
            Action = action,
            Colour = action is ChatDeckRuleAction.ColourLine or ChatDeckRuleAction.ColourMatch ? DefaultColour : 0,
        };
    }

    public static bool CanGeneraliseSender(ChatDeckLineFacts facts) =>
        facts.Sender.Trim().Length > 0 && facts.Text.Contains(facts.Sender.Trim(), StringComparison.OrdinalIgnoreCase);

    public static bool CanGeneraliseNumber(ChatDeckLineFacts facts) => Numbers.IsMatch(facts.Text);

    /// <summary>The text the rule matches, with the generalised parts shown as marks.</summary>
    public static string Preview(ChatDeckLineFacts facts, bool anySender, bool anyNumber) =>
        string.Concat(Parts(facts, anySender, anyNumber).Select(p => p.Kind switch
        {
            PartKind.Name => NameMark,
            PartKind.Number => NumberMark,
            _ => p.Text,
        }));

    /// <summary>
    /// Whether generalising leaves no words of the line (only names, numbers, spaces and punctuation),
    /// so the rule would match almost any line on its channel; the editor warns.
    /// </summary>
    public static bool IsTooGeneral(ChatDeckLineFacts facts, bool anySender, bool anyNumber)
    {
        List<Part> parts = Parts(facts, anySender, anyNumber);
        return parts.Any(p => p.Kind != PartKind.Literal)
            && !parts.Any(p => p.Kind == PartKind.Literal && p.Text.Any(char.IsLetter));
    }

    public static (ChatDeckMatchKind Kind, string Pattern) Pattern(ChatDeckLineFacts facts, bool anySender, bool anyNumber)
    {
        List<Part> parts = Parts(facts, anySender, anyNumber);
        if (parts.All(p => p.Kind == PartKind.Literal))
        {
            string text = facts.Text;
            return (ChatDeckMatchKind.Contains,
                text.Length > ChatDeckMatcher.MaxPatternLength ? text[..ChatDeckMatcher.MaxPatternLength] : text);
        }

        // Drop parts from the end until the pattern fits; a shorter unanchored pattern still matches the line.
        while (true)
        {
            var sb = new StringBuilder();
            foreach (Part p in parts)
                sb.Append(p.Kind switch
                {
                    PartKind.Name => NamePattern,
                    PartKind.Number => NumberPattern,
                    _ => Regex.Escape(p.Text),
                });
            if (sb.Length <= ChatDeckMatcher.MaxPatternLength || parts.Count <= 1)
                return (ChatDeckMatchKind.Regex, sb.Length <= ChatDeckMatcher.MaxPatternLength ? sb.ToString() : sb.ToString()[..ChatDeckMatcher.MaxPatternLength]);
            parts.RemoveAt(parts.Count - 1);
        }
    }

    private static List<Part> Parts(ChatDeckLineFacts facts, bool anySender, bool anyNumber)
    {
        string text = facts.Text;
        var pieces = new List<Part>();
        string name = facts.Sender.Trim();
        if (anySender && CanGeneraliseSender(facts))
        {
            int at = 0;
            for (int i = text.IndexOf(name, StringComparison.OrdinalIgnoreCase); i >= 0;
                 i = text.IndexOf(name, at, StringComparison.OrdinalIgnoreCase))
            {
                if (i > at) pieces.Add(new Part(PartKind.Literal, text[at..i]));
                pieces.Add(new Part(PartKind.Name, text.Substring(i, name.Length)));
                at = i + name.Length;
            }
            if (at < text.Length) pieces.Add(new Part(PartKind.Literal, text[at..]));
        }
        else
        {
            pieces.Add(new Part(PartKind.Literal, text));
        }

        if (!anyNumber) return pieces;
        var parts = new List<Part>();
        foreach (Part p in pieces)
        {
            if (p.Kind != PartKind.Literal)
            {
                parts.Add(p);
                continue;
            }
            int at = 0;
            foreach (Match m in Numbers.Matches(p.Text))
            {
                if (m.Index > at) parts.Add(new Part(PartKind.Literal, p.Text[at..m.Index]));
                parts.Add(new Part(PartKind.Number, m.Value));
                at = m.Index + m.Length;
            }
            if (at < p.Text.Length) parts.Add(new Part(PartKind.Literal, p.Text[at..]));
        }
        return parts;
    }
}
