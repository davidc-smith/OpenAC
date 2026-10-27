namespace AcDream.Core.Chat.Deck;

/// <summary>One rule that matched a line; <see cref="Decided"/> when it took effect.</summary>
public readonly record struct ChatDeckRuleHit(string RuleId, ChatDeckRuleAction Action, bool Decided);

public sealed record ChatDeckVerdict(
    bool Hidden,
    IReadOnlyList<string> Destinations,
    uint? LineColour,
    uint? MatchColour,
    IReadOnlyList<ChatDeckSpan> MatchSpans,
    bool Alert,
    IReadOnlyList<ChatDeckRuleHit> Hits,
    IReadOnlyList<string> TimedOutRuleIds);

/// <summary>Rules and tabs, ready to decide where each line goes.</summary>
public sealed class ChatDeckRuleEngine
{
    private readonly IReadOnlyList<ChatDeckTab> _tabs;
    private readonly HashSet<string> _tabIds;

    public ChatDeckRuleEngine(IReadOnlyList<ChatDeckRule> rules, IReadOnlyList<ChatDeckTab> tabs, string characterName)
    {
        _tabs = tabs;
        _tabIds = tabs.Select(t => t.Id).ToHashSet(StringComparer.Ordinal);
        Rules = rules.Select(r => ChatDeckMatcher.Compile(r, characterName)).ToList();
    }

    public IReadOnlyList<ChatDeckCompiledRule> Rules { get; }

    public ChatDeckVerdict Evaluate(ChatDeckLineFacts facts)
    {
        var matched = new List<(ChatDeckCompiledRule Rule, ChatDeckMatch Match)>();
        var timedOut = new List<string>();
        foreach (ChatDeckCompiledRule rule in Rules)
        {
            bool wantSpans = rule.Rule.Action == ChatDeckRuleAction.ColourMatch;
            ChatDeckMatch m = ChatDeckMatcher.Match(rule, facts, wantSpans);
            if (m.Outcome == ChatDeckMatchOutcome.Timeout) timedOut.Add(rule.Rule.Id);
            else if (m.Outcome == ChatDeckMatchOutcome.Match) matched.Add((rule, m));
        }

        int hideIndex = matched.FindIndex(x => x.Rule.Rule.Action == ChatDeckRuleAction.Hide);
        if (hideIndex >= 0)
        {
            var hideHits = matched.Select((x, i) => new ChatDeckRuleHit(x.Rule.Rule.Id, x.Rule.Rule.Action, i == hideIndex)).ToList();
            return new ChatDeckVerdict(true, [], null, null, [], false, hideHits, timedOut);
        }

        var destinations = new HashSet<string>(StringComparer.Ordinal) { ChatDeckDefaults.AllTabId };
        foreach (ChatDeckTab tab in _tabs)
            if (tab.Id != ChatDeckDefaults.AllTabId && tab.Channels.Contains(facts.Channel))
                destinations.Add(tab.Id);

        var hits = new List<ChatDeckRuleHit>();
        bool moved = false;
        uint? lineColour = null, matchColour = null;
        IReadOnlyList<ChatDeckSpan> spans = [];
        bool alert = facts.Channel == ChatDeckChannel.TellIn;

        foreach (var (compiled, match) in matched)
        {
            ChatDeckRule rule = compiled.Rule;
            bool decided = false;
            switch (rule.Action)
            {
                case ChatDeckRuleAction.MoveTo when !moved && _tabIds.Contains(rule.TargetTabId):
                    destinations.Clear();
                    destinations.Add(ChatDeckDefaults.AllTabId);
                    destinations.Add(rule.TargetTabId);
                    moved = decided = true;
                    break;
                case ChatDeckRuleAction.CopyTo when _tabIds.Contains(rule.TargetTabId):
                    destinations.Add(rule.TargetTabId);
                    decided = true;
                    break;
                case ChatDeckRuleAction.ColourLine when lineColour is null:
                    lineColour = rule.Colour;
                    decided = true;
                    break;
                case ChatDeckRuleAction.ColourMatch when matchColour is null:
                    matchColour = rule.Colour;
                    spans = match.Spans;
                    decided = true;
                    break;
            }
            if (rule.Alert) alert = true;
            hits.Add(new ChatDeckRuleHit(rule.Id, rule.Action, decided));
        }

        // Copies were added before the Move cleared; re-apply copies that matched after it.
        // Simplest correct order: rebuild from the decided hits.
        if (moved)
        {
            foreach (ChatDeckRuleHit hit in hits)
                if (hit.Decided && hit.Action == ChatDeckRuleAction.CopyTo)
                    destinations.Add(matched.First(x => x.Rule.Rule.Id == hit.RuleId).Rule.Rule.TargetTabId);
        }

        var ordered = _tabs.Where(t => destinations.Contains(t.Id)).Select(t => t.Id).ToList();
        return new ChatDeckVerdict(false, ordered, lineColour, matchColour, spans, alert, hits, timedOut);
    }
}
