namespace AcDream.Core.Chat.Deck;

/// <summary>What a draft rule would catch in the lines already received.</summary>
/// <param name="Count">Matching lines.</param>
/// <param name="Total">Lines looked at (fewer than given when a timeout stopped the count).</param>
/// <param name="Samples">Up to the requested number of matching lines, newest first, as worded.</param>
public sealed record ChatDeckPreviewResult(
    int Count, int Total, IReadOnlyList<string> Samples, ChatDeckRuleStatus Status, string? Error,
    bool TimedOut, bool MatchesEverything);

public static class ChatDeckRulePreview
{
    /// <summary>
    /// Runs the draft over <paramref name="lines"/> (oldest first). The draft is previewed as if it were on,
    /// whatever its enabled or faulted state. A timeout stops the count.
    /// </summary>
    public static ChatDeckPreviewResult Run(
        ChatDeckRule draft, IReadOnlyList<ChatDeckLine> lines, string characterName, int samples = 3)
    {
        ChatDeckCompiledRule compiled = ChatDeckMatcher.Compile(draft with { Enabled = true, Faulted = false }, characterName);
        if (compiled.Status != ChatDeckRuleStatus.Ok)
            return new ChatDeckPreviewResult(0, 0, [], compiled.Status, compiled.Error, false, false);

        bool everything = ChatDeckMatcher.MatchesEmpty(compiled);
        var found = new List<string>();
        int count = 0, total = 0;
        for (int i = lines.Count - 1; i >= 0; i--)
        {
            ChatDeckMatch m = ChatDeckMatcher.Match(compiled, lines[i].Facts, false);
            if (m.Outcome == ChatDeckMatchOutcome.Timeout)
                return new ChatDeckPreviewResult(count, total, found, ChatDeckRuleStatus.Ok, null, true, everything);
            total++;
            if (m.Outcome != ChatDeckMatchOutcome.Match) continue;
            count++;
            if (found.Count < samples) found.Add(lines[i].Facts.Line);
        }
        return new ChatDeckPreviewResult(count, total, found, ChatDeckRuleStatus.Ok, null, false, everything);
    }
}
