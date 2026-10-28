namespace AcDream.Core.Chat.Deck;

public enum ChatDeckIcon { All, Local, Group, Social, Combat, System, Loot, Star, Tell, Shield, Scroll, Coin }
public enum ChatDeckTimestamps { Off, HourMinute, HourMinuteSecond }
public enum ChatDeckEdge { Left, Right }

public static class ChatDeckLimits
{
    public const int MaxTabs = 16;
    public const int MaxRules = 200;
    public const int MaxTabNameLength = 24;
    public const int MaxSettingsJsonLength = 256 * 1024;
    public const int HistoryPerTab = 1000;
}

public sealed record ChatDeckTab
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public ChatDeckIcon Icon { get; init; }
    public IReadOnlySet<ChatDeckChannel> Channels { get; init; } = ChatDeckRule.AnyChannel;
    public ChatDeckSendChannel SendChannel { get; init; }
    public bool Peek { get; init; }
}

public sealed record ChatDeckAppearance
{
    public int FontSize { get; init; } = 14;
    public int InputFontSize { get; init; } = 14;
    public float LineSpacing { get; init; } = 1.2f;
    public bool WrapIndent { get; init; } = true;
    public float BackgroundOpacity { get; init; } = 0.85f;
    public ChatDeckTimestamps Timestamps { get; init; } = ChatDeckTimestamps.HourMinute;
    public bool ChannelPrefix { get; init; } = true;
    /// <summary>Per-channel colour overrides, 0xAARRGGBB; a channel not listed uses its default colour.</summary>
    public IReadOnlyDictionary<ChatDeckChannel, uint> ChannelColours { get; init; } = new Dictionary<ChatDeckChannel, uint>();
    public bool FadeWhenIdle { get; init; }
    public int FadeAfterSeconds { get; init; } = 10;
    public bool BeepOnAlert { get; init; }
}

public sealed record ChatDeckLayoutState
{
    public bool Pinned { get; init; } = true;
    public ChatDeckEdge Edge { get; init; } = ChatDeckEdge.Left;
    public string LastTabId { get; init; } = ChatDeckDefaults.AllTabId;
}

/// <summary>
/// Extra keys that cycle tabs while the deck's input has focus, as hotkey strings
/// ("Ctrl+Alt+N"); empty is unbound. The built-in keys always work as well.
/// </summary>
public sealed record ChatDeckKeys
{
    public const int MaxLength = 40;
    public string NextTab { get; init; } = "";
    public string PreviousTab { get; init; } = "";
}

public sealed record ChatDeckSettings
{
    public const int CurrentVersion = 1;

    public int Version { get; init; } = CurrentVersion;
    public required IReadOnlyList<ChatDeckTab> Tabs { get; init; }
    public required IReadOnlyList<ChatDeckRule> Rules { get; init; }
    public ChatDeckAppearance Appearance { get; init; } = new();
    public ChatDeckLayoutState Layout { get; init; } = new();
    public ChatDeckKeys Keys { get; init; } = new();
}

public static class ChatDeckDefaults
{
    public const string AllTabId = "all";
    public const string MyNameRuleId = "my-name";
    public const uint MyNameColour = 0xFFFF7A8Cu;

    public static ChatDeckTab AllTab() => new()
    {
        Id = AllTabId, Name = "All", Icon = ChatDeckIcon.All,
        Channels = Enum.GetValues<ChatDeckChannel>().ToHashSet(),
        SendChannel = ChatDeckSendChannel.Say,
    };

    public static ChatDeckSettings Create() => new()
    {
        Tabs =
        [
            AllTab(),
            Tab("local", "Local", ChatDeckIcon.Local, "Local", ChatDeckSendChannel.Say, peek: true),
            Tab("social", "Social", ChatDeckIcon.Social, "Social", ChatDeckSendChannel.General, peek: false),
            Tab("group", "Group", ChatDeckIcon.Group, "Group", ChatDeckSendChannel.Fellowship, peek: true),
            Tab("combat", "Combat", ChatDeckIcon.Combat, "Combat", ChatDeckSendChannel.Say, peek: false),
            Tab("system", "System", ChatDeckIcon.System, "System", ChatDeckSendChannel.Say, peek: false),
        ],
        Rules =
        [
            new ChatDeckRule
            {
                Id = MyNameRuleId, Kind = ChatDeckMatchKind.MyName,
                Action = ChatDeckRuleAction.ColourLine, Colour = MyNameColour, Alert = true,
            },
        ],
    };

    private static ChatDeckTab Tab(
        string id, string name, ChatDeckIcon icon, string group, ChatDeckSendChannel send, bool peek) => new()
    {
        Id = id, Name = name, Icon = icon,
        Channels = ChatDeckChannels.Group(group).ToHashSet(),
        SendChannel = send, Peek = peek,
    };
}

public static class ChatDeckSettingsNormalizer
{
    public static ChatDeckSettings Normalize(ChatDeckSettings settings)
    {
        var tabs = new List<ChatDeckTab>();
        var tabIds = new HashSet<string>(StringComparer.Ordinal);
        ChatDeckTab? all = settings.Tabs.FirstOrDefault(t => t.Id == ChatDeckDefaults.AllTabId);
        tabs.Add((all ?? ChatDeckDefaults.AllTab()) with { Name = CleanName(all?.Name ?? "All") });
        tabIds.Add(ChatDeckDefaults.AllTabId);
        foreach (ChatDeckTab tab in settings.Tabs)
        {
            if (tabs.Count >= ChatDeckLimits.MaxTabs) break;
            if (string.IsNullOrEmpty(tab.Id) || !tabIds.Add(tab.Id)) continue;
            tabs.Add(tab with { Name = CleanName(tab.Name) });
        }

        var rules = new List<ChatDeckRule>();
        var ruleIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (ChatDeckRule rule in settings.Rules)
        {
            if (rules.Count >= ChatDeckLimits.MaxRules) break;
            if (string.IsNullOrEmpty(rule.Id) || !ruleIds.Add(rule.Id)) continue;
            bool targets = rule.Action is ChatDeckRuleAction.MoveTo or ChatDeckRuleAction.CopyTo;
            rules.Add(targets && !tabIds.Contains(rule.TargetTabId) ? rule with { Enabled = false } : rule);
        }

        ChatDeckAppearance a = settings.Appearance;
        var appearance = a with
        {
            FontSize = Math.Clamp(a.FontSize, 10, 20),
            InputFontSize = Math.Clamp(a.InputFontSize, 10, 20),
            LineSpacing = Math.Clamp(a.LineSpacing, 1.0f, 1.6f),
            BackgroundOpacity = Math.Clamp(a.BackgroundOpacity, 0f, 1f),
            FadeAfterSeconds = Math.Clamp(a.FadeAfterSeconds, 1, 600),
        };

        var layout = tabIds.Contains(settings.Layout.LastTabId)
            ? settings.Layout
            : settings.Layout with { LastTabId = ChatDeckDefaults.AllTabId };

        // "Mod+...+Key": a string with no '+' has no modifier, so it would type instead of firing; unbound.
        static string Key(string text) =>
            text.Trim() is { Length: <= ChatDeckKeys.MaxLength } t && t.Contains('+') ? t : "";
        var keys = new ChatDeckKeys { NextTab = Key(settings.Keys.NextTab), PreviousTab = Key(settings.Keys.PreviousTab) };

        return settings with { Tabs = tabs, Rules = rules, Appearance = appearance, Layout = layout, Keys = keys };
    }

    private static string CleanName(string name)
    {
        string trimmed = name.Trim();
        if (trimmed.Length == 0) return "Tab";
        return trimmed.Length > ChatDeckLimits.MaxTabNameLength ? trimmed[..ChatDeckLimits.MaxTabNameLength] : trimmed;
    }
}
