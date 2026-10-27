namespace AcDream.Core.Chat.Deck;

/// <summary>
/// What a Chat Deck line is, for tabs and rules. Finer than a chat type: several
/// channels share one <see cref="RetailLogTextType"/> (Patron, Vassals, Monarch and
/// Co-vassals are all Social; Allegiance and Olthoi are both 0x12).
/// </summary>
public enum ChatDeckChannel
{
    Say, Emote, TellIn, TellOut,
    Fellowship, Allegiance, Patron, Vassals, Monarch, CoVassals,
    General, Trade, Lfg, Roleplay, Society, Olthoi, Help, OtherChannel,
    Combat, CombatEnemy, CombatSelf, Magic, Spellcasting,
    System, Advancement, Appraisal, Recall, Craft, Salvaging, WorldBroadcast, Plugin,
}

/// <summary>Where typed text goes when it has no <c>/</c> command. Names match the Runtime's ChatChannelKind.</summary>
public enum ChatDeckSendChannel
{
    Say, Fellowship, Allegiance, Patron, Vassals, Monarch, CoVassals,
    General, Trade, Lfg, Roleplay, Society, Olthoi,
}

public readonly record struct ChatDeckChannelGroup(string Name, IReadOnlyList<ChatDeckChannel> Channels);

public static class ChatDeckChannels
{
    public static IReadOnlyList<ChatDeckChannelGroup> Groups { get; } =
    [
        new("Local", [ChatDeckChannel.Say, ChatDeckChannel.Emote, ChatDeckChannel.TellIn, ChatDeckChannel.TellOut]),
        new("Group", [ChatDeckChannel.Fellowship, ChatDeckChannel.Allegiance, ChatDeckChannel.Patron,
                      ChatDeckChannel.Vassals, ChatDeckChannel.Monarch, ChatDeckChannel.CoVassals]),
        new("Social", [ChatDeckChannel.General, ChatDeckChannel.Trade, ChatDeckChannel.Lfg, ChatDeckChannel.Roleplay,
                       ChatDeckChannel.Society, ChatDeckChannel.Olthoi, ChatDeckChannel.Help, ChatDeckChannel.OtherChannel]),
        new("Combat", [ChatDeckChannel.Combat, ChatDeckChannel.CombatEnemy, ChatDeckChannel.CombatSelf,
                       ChatDeckChannel.Magic, ChatDeckChannel.Spellcasting]),
        new("System", [ChatDeckChannel.System, ChatDeckChannel.Advancement, ChatDeckChannel.Appraisal, ChatDeckChannel.Recall,
                       ChatDeckChannel.Craft, ChatDeckChannel.Salvaging, ChatDeckChannel.WorldBroadcast, ChatDeckChannel.Plugin]),
    ];

    public static IReadOnlyList<ChatDeckChannel> Group(string name) =>
        Groups.First(g => g.Name == name).Channels;

    public static string DisplayName(ChatDeckChannel channel) => channel switch
    {
        ChatDeckChannel.Say => "Say",
        ChatDeckChannel.Emote => "Emotes",
        ChatDeckChannel.TellIn => "Tells received",
        ChatDeckChannel.TellOut => "Tells sent",
        ChatDeckChannel.Fellowship => "Fellowship",
        ChatDeckChannel.Allegiance => "Allegiance",
        ChatDeckChannel.Patron => "Patron",
        ChatDeckChannel.Vassals => "Vassals",
        ChatDeckChannel.Monarch => "Monarch",
        ChatDeckChannel.CoVassals => "Co-vassals",
        ChatDeckChannel.General => "General",
        ChatDeckChannel.Trade => "Trade",
        ChatDeckChannel.Lfg => "LFG",
        ChatDeckChannel.Roleplay => "Roleplay",
        ChatDeckChannel.Society => "Society",
        ChatDeckChannel.Olthoi => "Olthoi",
        ChatDeckChannel.Help => "Help",
        ChatDeckChannel.OtherChannel => "Other channels",
        ChatDeckChannel.Combat => "Combat",
        ChatDeckChannel.CombatEnemy => "Combat against you",
        ChatDeckChannel.CombatSelf => "Your combat",
        ChatDeckChannel.Magic => "Magic",
        ChatDeckChannel.Spellcasting => "Spell words",
        ChatDeckChannel.System => "System",
        ChatDeckChannel.Advancement => "Advancement",
        ChatDeckChannel.Appraisal => "Appraisal",
        ChatDeckChannel.Recall => "Recall",
        ChatDeckChannel.Craft => "Crafting",
        ChatDeckChannel.Salvaging => "Salvaging",
        ChatDeckChannel.WorldBroadcast => "World broadcasts",
        ChatDeckChannel.Plugin => "Plugins",
        _ => channel.ToString(),
    };

    public static ChatDeckChannel Classify(in ChatEntry entry) => entry.Kind switch
    {
        ChatKind.LocalSpeech or ChatKind.RangedSpeech =>
            entry.LogTextType == (uint)RetailLogTextType.Spellcasting
                ? ChatDeckChannel.Spellcasting
                : ChatDeckChannel.Say,
        ChatKind.Emote or ChatKind.SoulEmote => ChatDeckChannel.Emote,
        ChatKind.Tell => entry.LogTextType == (uint)RetailLogTextType.SpeechDirectSend
            ? ChatDeckChannel.TellOut
            : ChatDeckChannel.TellIn,
        ChatKind.Channel => ClassifyChannel(entry),
        _ => ByLogTextType(entry.LogTextType),
    };

    /// <summary>The channel a line of this type belongs to when nothing else says (system, combat and popup lines).</summary>
    public static ChatDeckChannel ByLogTextType(uint logTextType) => (RetailLogTextType)logTextType switch
    {
        RetailLogTextType.Speech => ChatDeckChannel.Say,
        RetailLogTextType.Tell or RetailLogTextType.AdminTell => ChatDeckChannel.TellIn,
        RetailLogTextType.SpeechDirectSend => ChatDeckChannel.TellOut,
        RetailLogTextType.Emote => ChatDeckChannel.Emote,
        RetailLogTextType.Combat => ChatDeckChannel.Combat,
        RetailLogTextType.CombatEnemy => ChatDeckChannel.CombatEnemy,
        RetailLogTextType.CombatSelf => ChatDeckChannel.CombatSelf,
        RetailLogTextType.Magic => ChatDeckChannel.Magic,
        RetailLogTextType.Spellcasting => ChatDeckChannel.Spellcasting,
        RetailLogTextType.Channel or RetailLogTextType.ChannelSend or RetailLogTextType.Abuse => ChatDeckChannel.OtherChannel,
        RetailLogTextType.Social or RetailLogTextType.SocialSend or RetailLogTextType.Allegiance => ChatDeckChannel.Allegiance,
        RetailLogTextType.Fellowship => ChatDeckChannel.Fellowship,
        RetailLogTextType.Help => ChatDeckChannel.Help,
        RetailLogTextType.TurbineGeneral => ChatDeckChannel.General,
        RetailLogTextType.TurbineTrade => ChatDeckChannel.Trade,
        RetailLogTextType.TurbineLFG => ChatDeckChannel.Lfg,
        RetailLogTextType.TurbineRoleplay => ChatDeckChannel.Roleplay,
        RetailLogTextType.TurbineSociety => ChatDeckChannel.Society,
        RetailLogTextType.Advancement => ChatDeckChannel.Advancement,
        RetailLogTextType.Appraisal => ChatDeckChannel.Appraisal,
        RetailLogTextType.Recall => ChatDeckChannel.Recall,
        RetailLogTextType.Craft => ChatDeckChannel.Craft,
        RetailLogTextType.Salvaging => ChatDeckChannel.Salvaging,
        RetailLogTextType.WorldBroadcast => ChatDeckChannel.WorldBroadcast,
        RetailLogTextType.ClientLocal => ChatDeckChannel.Plugin,
        _ => ChatDeckChannel.System,
    };

    /// <summary>A line the character itself said or sent.</summary>
    public static bool IsOwnLine(in ChatEntry entry) =>
        entry.Sender == "You"
        || entry.LogTextType is (uint)RetailLogTextType.SpeechDirectSend
            or (uint)RetailLogTextType.ChannelSend
            or (uint)RetailLogTextType.SocialSend
        || (entry.Kind == ChatKind.Channel && string.IsNullOrEmpty(entry.Sender));

    private static ChatDeckChannel ClassifyChannel(in ChatEntry entry)
    {
        // A Turbine room always carries its display name (an unknown room reads "Room 0x…");
        // its id is a room number, not a legacy channel bit, so the name decides.
        if (!string.IsNullOrEmpty(entry.ChannelName))
        {
            return entry.ChannelName switch
            {
                "Allegiance" => ChatDeckChannel.Allegiance,
                "General" => ChatDeckChannel.General,
                "Trade" => ChatDeckChannel.Trade,
                "LFG" => ChatDeckChannel.Lfg,
                "Roleplay" => ChatDeckChannel.Roleplay,
                "Society" or "Celestial Hand" or "Eldrytch Web" or "Radiant Blood" => ChatDeckChannel.Society,
                "Olthoi" => ChatDeckChannel.Olthoi,
                _ => ChatDeckChannel.OtherChannel,
            };
        }

        bool own = IsOwnLine(entry);

        // Legacy channel bits, as Runtime's ChannelResolver and RetailChannelTagTable number them.
        return entry.ChannelId switch
        {
            0x00000800u => ChatDeckChannel.Fellowship,
            // The hierarchy ids name the speaker's side: a line heard on 0x1000 comes from your
            // patron, one you send on 0x1000 goes to your vassals. Channels name the partner.
            0x00001000u => own ? ChatDeckChannel.Vassals : ChatDeckChannel.Patron,
            0x00002000u => own ? ChatDeckChannel.Patron : ChatDeckChannel.Vassals,
            0x00004000u => own ? ChatDeckChannel.Monarch : ChatDeckChannel.Vassals,
            0x01000000u => ChatDeckChannel.CoVassals,
            0x02000000u => ChatDeckChannel.Allegiance,
            0x40000000u => ChatDeckChannel.Olthoi,
            0x00000400u => ChatDeckChannel.Help,
            _ => ChatDeckChannel.OtherChannel,
        };
    }
}
