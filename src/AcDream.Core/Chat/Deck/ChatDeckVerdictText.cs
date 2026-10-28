namespace AcDream.Core.Chat.Deck;

/// <summary>A verdict in words, for the editor's test box: what happened to the line and which rule decided each part.</summary>
public static class ChatDeckVerdictText
{
    public static IReadOnlyList<string> Lines(ChatDeckVerdict verdict, ChatDeckSettings settings)
    {
        var lines = new List<string>();
        string Rule(string id)
        {
            int i = -1;
            for (int k = 0; k < settings.Rules.Count; k++) if (settings.Rules[k].Id == id) { i = k; break; }
            return i < 0 ? id : $"Rule {i + 1} ({ChatDeckRuleSummary.When(settings.Rules[i])})";
        }
        string RuleNumber(string id)
        {
            for (int k = 0; k < settings.Rules.Count; k++) if (settings.Rules[k].Id == id) return $"Rule {k + 1}";
            return id;
        }
        ChatDeckRule? Find(string id) => settings.Rules.FirstOrDefault(r => r.Id == id);

        if (verdict.Hidden)
        {
            ChatDeckRuleHit hide = verdict.Hits.First(h => h.Decided && h.Action == ChatDeckRuleAction.Hide);
            lines.Add($"Hidden by {Rule(hide.RuleId)}");
        }
        else
        {
            var names = verdict.Destinations.Select(id => settings.Tabs.FirstOrDefault(t => t.Id == id)?.Name ?? id);
            lines.Add($"Goes to: {string.Join(", ", names)}");
            foreach (ChatDeckRuleHit hit in verdict.Hits)
                if (hit.Decided && hit.Action is ChatDeckRuleAction.MoveTo or ChatDeckRuleAction.CopyTo && Find(hit.RuleId) is { } r)
                    lines.Add($"{Rule(hit.RuleId)}: {ChatDeckRuleSummary.Do(r with { Alert = false }, settings.Tabs)}");

            // ChatDeckRuleHit is a struct: with no such hit, FirstOrDefault gives one with Decided == false.
            ChatDeckRuleHit lineHit = verdict.Hits.FirstOrDefault(h => h.Decided && h.Action == ChatDeckRuleAction.ColourLine);
            lines.Add(lineHit.Decided ? $"Line colour: {RuleNumber(lineHit.RuleId)}" : "Channel colour");
            ChatDeckRuleHit matchHit = verdict.Hits.FirstOrDefault(h => h.Decided && h.Action == ChatDeckRuleAction.ColourMatch);
            if (matchHit.Decided) lines.Add($"Match colour: {RuleNumber(matchHit.RuleId)}");

            var alerting = verdict.Hits.Where(h => Find(h.RuleId)?.Alert == true).Select(h => RuleNumber(h.RuleId)).ToList();
            if (!verdict.Alert) lines.Add("No alert");
            else lines.Add(alerting.Count > 0 ? $"Alert: {string.Join(", ", alerting)}" : "Alert: incoming tell");
        }

        foreach (ChatDeckRuleHit hit in verdict.Hits)
            if (!hit.Decided) lines.Add($"{Rule(hit.RuleId)} also matched (no effect)");
        foreach (string id in verdict.TimedOutRuleIds)
            lines.Add($"{Rule(id)} timed out on this line");
        if (verdict.Hits.Count == 0) lines.Add("No rule matched.");
        return lines;
    }
}
