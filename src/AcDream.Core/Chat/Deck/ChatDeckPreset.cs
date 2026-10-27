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
