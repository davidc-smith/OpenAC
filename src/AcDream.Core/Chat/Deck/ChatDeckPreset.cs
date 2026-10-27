namespace AcDream.Core.Chat.Deck;

public readonly record struct ChatDeckPresetSample(string Line, ChatDeckChannel Channel);

public sealed class ChatDeckPreset(
    string id, string group, string label, string description, int version, string pattern,
    IReadOnlySet<ChatDeckChannel> channels,
    IReadOnlyList<ChatDeckPresetSample> catches,
    IReadOnlyList<ChatDeckPresetSample> spares)
{
    public string Id { get; } = id;
    public string Group { get; } = group;
    public string Label { get; } = label;
    public string Description { get; } = description;
    public int Version { get; } = version;
    public string Pattern { get; } = pattern;
    /// <summary>Channels it applies to; empty means any.</summary>
    public IReadOnlySet<ChatDeckChannel> Channels { get; } = channels;
    public IReadOnlyList<ChatDeckPresetSample> Catches { get; } = catches;
    public IReadOnlyList<ChatDeckPresetSample> Spares { get; } = spares;
}

/// <summary>RynthChat's preset scopes and chat types, as Chat Deck channel sets.</summary>
public static class ChatDeckPresetScopes
{
    public static readonly IReadOnlySet<ChatDeckChannel> Any = new HashSet<ChatDeckChannel>();
    public static readonly IReadOnlySet<ChatDeckChannel> CombatOnly = Of("Combat");
    public static readonly IReadOnlySet<ChatDeckChannel> ChannelsOnly = Of("Social");
    /// <summary>Anything a player cannot type: the Combat and System groups.</summary>
    public static readonly IReadOnlySet<ChatDeckChannel> NotPlayerChat = Of("Combat", "System");

    /// <summary>
    /// A RynthChat "type:N" as the whole group its channel is in: RynthChat's numbers and
    /// ours do not always agree on the exact channel (spell words, notices), the group does.
    /// </summary>
    public static IReadOnlySet<ChatDeckChannel> ForType(int chatType)
    {
        ChatDeckChannel channel = SampleChannel(chatType);
        string group = ChatDeckChannels.Groups.First(g => g.Channels.Contains(channel)).Name;
        return Of(group);
    }

    /// <summary>The channel a RynthChat fixture's chat type stands for.</summary>
    public static ChatDeckChannel SampleChannel(int chatType) => chatType switch
    {
        26 => ChatDeckChannel.System,   // RynthChat's "Notice"
        27 => ChatDeckChannel.General,  // RynthChat's "ServerChannel"
        _ => ChatDeckChannels.ByLogTextType((uint)chatType),
    };

    private static IReadOnlySet<ChatDeckChannel> Of(params string[] groups) =>
        groups.SelectMany(ChatDeckChannels.Group).ToHashSet();
}
