using AcDream.Core.Chat.Deck;
using AcDream.Runtime.Chat;

namespace AcDream.App.UI.Hud;

/// <summary>Where a Chat Deck line goes when it has no <c>/</c> command: names and router channels.</summary>
internal static class ChatDeckSend
{
    public static IReadOnlyList<ChatDeckSendChannel> All { get; } = Enum.GetValues<ChatDeckSendChannel>();

    public static ChatChannelKind ToKind(ChatDeckSendChannel channel) => channel switch
    {
        ChatDeckSendChannel.Say => ChatChannelKind.Say,
        ChatDeckSendChannel.Fellowship => ChatChannelKind.Fellowship,
        ChatDeckSendChannel.Allegiance => ChatChannelKind.Allegiance,
        ChatDeckSendChannel.Patron => ChatChannelKind.Patron,
        ChatDeckSendChannel.Vassals => ChatChannelKind.Vassals,
        ChatDeckSendChannel.Monarch => ChatChannelKind.Monarch,
        ChatDeckSendChannel.CoVassals => ChatChannelKind.CoVassals,
        ChatDeckSendChannel.General => ChatChannelKind.General,
        ChatDeckSendChannel.Trade => ChatChannelKind.Trade,
        ChatDeckSendChannel.Lfg => ChatChannelKind.Lfg,
        ChatDeckSendChannel.Roleplay => ChatChannelKind.Roleplay,
        ChatDeckSendChannel.Society => ChatChannelKind.Society,
        ChatDeckSendChannel.Olthoi => ChatChannelKind.Olthoi,
        _ => ChatChannelKind.Say,
    };

    public static string DisplayName(ChatDeckSendChannel channel) => channel switch
    {
        ChatDeckSendChannel.CoVassals => "Co-vassals",
        ChatDeckSendChannel.Lfg => "LFG",
        _ => channel.ToString(),
    };
}
