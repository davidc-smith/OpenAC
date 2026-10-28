namespace AcDream.Core.Chat.Deck;

/// <summary>A rule as the editor's table describes it.</summary>
public static class ChatDeckRuleSummary
{
    public const string DeletedTab = "(tab deleted)";

    public static string When(ChatDeckRule rule) => rule.Kind switch
    {
        ChatDeckMatchKind.MyName => "My name",
        ChatDeckMatchKind.Preset => ChatDeckPresets.Get(rule.PresetId) is { } p
            ? $"Standard: {p.Label}"
            : $"Standard: {rule.PresetId} (unknown)",
        ChatDeckMatchKind.Regex => $"{FieldName(rule.Field)} matches /{rule.Pattern}/",
        _ => $"{FieldName(rule.Field)} contains \"{rule.Pattern}\"",
    };

    public static string From(ChatDeckRule rule)
    {
        IReadOnlySet<ChatDeckChannel> channels = rule.Kind == ChatDeckMatchKind.Preset && ChatDeckPresets.Get(rule.PresetId) is { } p
            ? p.Channels
            : rule.Channels;
        if (channels.Count == 0) return "Any channel";
        var items = new List<string>();
        foreach (ChatDeckChannelGroup group in ChatDeckChannels.Groups)
        {
            if (group.Channels.All(channels.Contains)) items.Add(group.Name);
            else items.AddRange(group.Channels.Where(channels.Contains).Select(ChatDeckChannels.DisplayName));
        }
        return items.Count <= 3 ? string.Join(", ", items) : $"{string.Join(", ", items.Take(3))} +{items.Count - 3}";
    }

    public static string Do(ChatDeckRule rule, IReadOnlyList<ChatDeckTab> tabs)
    {
        string target = tabs.FirstOrDefault(t => t.Id == rule.TargetTabId)?.Name ?? DeletedTab;
        string action = rule.Action switch
        {
            ChatDeckRuleAction.Hide => "Hide",
            ChatDeckRuleAction.MoveTo => $"Move to {target}",
            ChatDeckRuleAction.CopyTo => $"Also show in {target}",
            ChatDeckRuleAction.ColourLine => "Colour line",
            ChatDeckRuleAction.ColourMatch => "Colour match",
            _ => rule.Action.ToString(),
        };
        return rule.Alert ? action + " + alert" : action;
    }

    /// <summary>Why a rule does not run, for the table and the form; empty when it runs.</summary>
    public static string Status(ChatDeckCompiledRule compiled) => compiled.Status switch
    {
        ChatDeckRuleStatus.Disabled => "Off",
        ChatDeckRuleStatus.Invalid => $"Invalid: {compiled.Error}",
        ChatDeckRuleStatus.Faulted => "Timed out; edit it to turn it back on",
        ChatDeckRuleStatus.UnknownPreset => "Unknown standard filter",
        _ => "",
    };

    /// <summary>Statuses the editor shows in red.</summary>
    public static bool IsProblem(ChatDeckRuleStatus status) =>
        status is ChatDeckRuleStatus.Invalid or ChatDeckRuleStatus.Faulted or ChatDeckRuleStatus.UnknownPreset;

    public static string FieldName(ChatDeckMatchField field) => field switch
    {
        ChatDeckMatchField.Sender => "Sender",
        ChatDeckMatchField.Line => "Line",
        _ => "Text",
    };
}
