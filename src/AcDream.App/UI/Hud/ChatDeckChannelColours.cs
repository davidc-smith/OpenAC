using System.Numerics;
using AcDream.Core.Chat;
using AcDream.Core.Chat.Deck;
using AcDream.UI.Abstractions.Panels.Chat;

namespace AcDream.App.UI.Hud;

/// <summary>The colour a channel's lines take when no override is set: the retail chat colour of its chat type.</summary>
internal static class ChatDeckChannelColours
{
    private static readonly Dictionary<ChatDeckChannel, uint> Defaults = Build();

    public static uint Default(ChatDeckChannel channel) =>
        Defaults.TryGetValue(channel, out uint argb) ? argb : Argb(ChatDeckPalette.TextInk);

    private static Dictionary<ChatDeckChannel, uint> Build()
    {
        var map = new Dictionary<ChatDeckChannel, uint>();
        foreach (RetailLogTextType type in Enum.GetValues<RetailLogTextType>())
        {
            ChatDeckChannel channel = ChatDeckChannels.ByLogTextType((uint)type);
            // ByLogTextType sends every unmapped type to System; only System itself may claim that channel.
            if (channel == ChatDeckChannel.System && type != RetailLogTextType.System) continue;
            if (!map.ContainsKey(channel) && RetailChatColorTable.TryGetColor((uint)type, out Vector4 colour))
                map[channel] = Argb(colour);
        }
        Borrow(map, RetailLogTextType.Social, ChatDeckChannel.Patron, ChatDeckChannel.Vassals, ChatDeckChannel.Monarch, ChatDeckChannel.CoVassals);
        Borrow(map, RetailLogTextType.Allegiance, ChatDeckChannel.Olthoi);
        Borrow(map, RetailLogTextType.Channel, ChatDeckChannel.OtherChannel);
        Borrow(map, RetailLogTextType.ClientLocal, ChatDeckChannel.Plugin);
        return map;
    }

    private static void Borrow(Dictionary<ChatDeckChannel, uint> map, RetailLogTextType type, params ChatDeckChannel[] channels)
    {
        if (!RetailChatColorTable.TryGetColor((uint)type, out Vector4 colour)) return;
        foreach (ChatDeckChannel c in channels) map.TryAdd(c, Argb(colour));
    }

    internal static uint Argb(Vector4 c) =>
        (uint)(Math.Clamp(c.W, 0f, 1f) * 255f + 0.5f) << 24 | (uint)(Math.Clamp(c.X, 0f, 1f) * 255f + 0.5f) << 16
        | (uint)(Math.Clamp(c.Y, 0f, 1f) * 255f + 0.5f) << 8 | (uint)(Math.Clamp(c.Z, 0f, 1f) * 255f + 0.5f);
}
