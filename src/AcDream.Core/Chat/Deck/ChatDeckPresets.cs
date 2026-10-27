namespace AcDream.Core.Chat.Deck;

public static class ChatDeckPresets
{
    public static IReadOnlyList<ChatDeckPreset> All { get; } = [];

    public static ChatDeckPreset? Get(string id) =>
        All.FirstOrDefault(p => string.Equals(p.Id, id, StringComparison.OrdinalIgnoreCase));
}
