using System.Text.RegularExpressions;

namespace AcDream.Core.Chat.Deck;

public enum ChatDeckRuleStatus { Ok, Disabled, Invalid, Faulted, UnknownPreset }

public enum ChatDeckMatchOutcome { NoMatch, Match, Timeout }

public readonly record struct ChatDeckMatch(ChatDeckMatchOutcome Outcome, IReadOnlyList<ChatDeckSpan> Spans)
{
    public static readonly ChatDeckMatch None = new(ChatDeckMatchOutcome.NoMatch, []);
    public static readonly ChatDeckMatch TimedOut = new(ChatDeckMatchOutcome.Timeout, []);
}

/// <summary>A rule ready to run: its regex built, its field and channels resolved.</summary>
public sealed class ChatDeckCompiledRule
{
    internal ChatDeckCompiledRule(
        ChatDeckRule rule, ChatDeckRuleStatus status, string? error,
        Regex? regex, ChatDeckMatchField field, IReadOnlySet<ChatDeckChannel> channels, bool skipOwnLines)
    {
        Rule = rule;
        Status = status;
        Error = error;
        Regex = regex;
        Field = field;
        Channels = channels;
        SkipOwnLines = skipOwnLines;
    }

    public ChatDeckRule Rule { get; }
    public ChatDeckRuleStatus Status { get; }
    /// <summary>Why the rule is invalid, for the editor; null otherwise.</summary>
    public string? Error { get; }
    internal Regex? Regex { get; }
    internal ChatDeckMatchField Field { get; }
    internal IReadOnlySet<ChatDeckChannel> Channels { get; }
    internal bool SkipOwnLines { get; }
}

public static class ChatDeckMatcher
{
    public const int MaxPatternLength = 256;
    public const int MaxInputLength = 2000;
    public const int MaxSpans = 16;
    public static readonly TimeSpan MatchTimeout = TimeSpan.FromMilliseconds(20);

    private const RegexOptions Base = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;

    public static ChatDeckCompiledRule Compile(ChatDeckRule rule, string characterName)
    {
        if (!rule.Enabled) return Status(rule, ChatDeckRuleStatus.Disabled, null);
        if (rule.Faulted) return Status(rule, ChatDeckRuleStatus.Faulted, "Timed out on a line; edit it to turn it back on.");

        switch (rule.Kind)
        {
            case ChatDeckMatchKind.Preset:
            {
                ChatDeckPreset? preset = ChatDeckPresets.Get(rule.PresetId);
                if (preset is null) return Status(rule, ChatDeckRuleStatus.UnknownPreset, null);
                var regex = new Regex(preset.Pattern, Base, MatchTimeout);
                return new ChatDeckCompiledRule(rule, ChatDeckRuleStatus.Ok, null, regex,
                    ChatDeckMatchField.Line, preset.Channels, skipOwnLines: false);
            }
            case ChatDeckMatchKind.MyName:
            {
                if (string.IsNullOrWhiteSpace(characterName))
                    return Status(rule, ChatDeckRuleStatus.Disabled, null);
                // Lookarounds, not \b: a name may start with '+' (admins).
                var regex = new Regex($@"(?<!\w){Regex.Escape(characterName.Trim())}(?!\w)", Base, MatchTimeout);
                return new ChatDeckCompiledRule(rule, ChatDeckRuleStatus.Ok, null, regex,
                    ChatDeckMatchField.Text, rule.Channels, skipOwnLines: true);
            }
            default:
            {
                if (rule.Pattern.Length == 0)
                    return Status(rule, ChatDeckRuleStatus.Invalid, "The pattern is empty.");
                if (rule.Pattern.Length > MaxPatternLength)
                    return Status(rule, ChatDeckRuleStatus.Invalid, $"The pattern is longer than {MaxPatternLength} characters.");
                string pattern = rule.Kind == ChatDeckMatchKind.Contains ? Regex.Escape(rule.Pattern) : rule.Pattern;
                Regex? regex;
                try
                {
                    regex = BuildUserRegex(pattern);
                }
                catch (ArgumentException ex)
                {
                    return Status(rule, ChatDeckRuleStatus.Invalid, ex.Message);
                }
                return new ChatDeckCompiledRule(rule, ChatDeckRuleStatus.Ok, null, regex,
                    rule.Field, rule.Channels, skipOwnLines: false);
            }
        }
    }

    public static ChatDeckMatch Match(ChatDeckCompiledRule compiled, ChatDeckLineFacts facts, bool wantSpans)
    {
        if (compiled.Status != ChatDeckRuleStatus.Ok || compiled.Regex is null) return ChatDeckMatch.None;
        if (compiled.Channels.Count > 0 && !compiled.Channels.Contains(facts.Channel)) return ChatDeckMatch.None;
        if (compiled.SkipOwnLines && facts.IsOwn) return ChatDeckMatch.None;

        string input = facts.Read(compiled.Field);
        if (input.Length > MaxInputLength) input = input[..MaxInputLength];
        try
        {
            if (!wantSpans)
                return compiled.Regex.IsMatch(input)
                    ? new ChatDeckMatch(ChatDeckMatchOutcome.Match, [])
                    : ChatDeckMatch.None;

            var spans = new List<ChatDeckSpan>();
            for (Match m = compiled.Regex.Match(input); m.Success && spans.Count < MaxSpans; m = m.NextMatch())
            {
                if (m.Length == 0) continue;
                spans.Add(new ChatDeckSpan(compiled.Field, m.Index, m.Length));
            }
            return spans.Count > 0 ? new ChatDeckMatch(ChatDeckMatchOutcome.Match, spans) : ChatDeckMatch.None;
        }
        catch (RegexMatchTimeoutException)
        {
            return ChatDeckMatch.TimedOut;
        }
    }

    private static Regex BuildUserRegex(string pattern)
    {
        try
        {
            return new Regex(pattern, Base | RegexOptions.NonBacktracking, MatchTimeout);
        }
        catch (NotSupportedException)
        {
            // Lookarounds and backreferences need the backtracking engine; the timeout guards it.
            return new Regex(pattern, Base, MatchTimeout);
        }
    }

    private static ChatDeckCompiledRule Status(ChatDeckRule rule, ChatDeckRuleStatus status, string? error) =>
        new(rule, status, error, null, rule.Field, rule.Channels, skipOwnLines: false);
}
