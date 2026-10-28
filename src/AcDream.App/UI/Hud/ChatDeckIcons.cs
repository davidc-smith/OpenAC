using AcDream.Core.Chat.Deck;

namespace AcDream.App.UI.Hud;

/// <summary>The bundled icon each tab icon choice draws with.</summary>
internal static class ChatDeckIcons
{
    public static string Name(ChatDeckIcon icon) => icon switch
    {
        ChatDeckIcon.Local => HudIcons.ChatLocal,
        ChatDeckIcon.Group => HudIcons.ChatGroup,
        ChatDeckIcon.Social => HudIcons.ChatSocial,
        ChatDeckIcon.Combat => HudIcons.ChatCombat,
        ChatDeckIcon.System => HudIcons.ChatSystem,
        ChatDeckIcon.Loot => HudIcons.ChatLoot,
        ChatDeckIcon.Star => HudIcons.ChatStar,
        ChatDeckIcon.Tell => HudIcons.ChatTell,
        ChatDeckIcon.Shield => HudIcons.ChatShield,
        ChatDeckIcon.Scroll => HudIcons.ChatScroll,
        ChatDeckIcon.Coin => HudIcons.ChatCoin,
        _ => HudIcons.ChatAll,
    };
}
