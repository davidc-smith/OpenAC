namespace AcDream.Core.Chat.Deck;

/// <summary>
/// A line typed into the editor's test box, as the model would see it: a chat entry built for the
/// chosen channel so the worded line ("[General] Bob says, …") is what presets read live.
/// </summary>
public static class ChatDeckTestLine
{
    private const string Someone = "Someone";

    public static ChatDeckLineFacts Facts(ChatDeckChannel channel, string sender, string text, string characterName)
    {
        string who = sender.Trim();
        ChatEntry entry = Entry(channel, who, text);
        // The channel is the one picked, whatever the synthesized entry would classify as.
        return ChatDeckLineFacts.From(entry, characterName) with { Channel = channel, IsOwn = false, Sender = who };
    }

    private static ChatEntry Entry(ChatDeckChannel channel, string sender, string text)
    {
        string speaker = sender.Length > 0 ? sender : Someone;
        ChatEntry Speech(ChatKind kind, RetailLogTextType type, uint guid = 0u) => new(kind, speaker, text, guid, 0u) { LogTextType = (uint)type };
        ChatEntry Room(string name) => new(ChatKind.Channel, speaker, text, 0u, 0u) { ChannelName = name };
        ChatEntry Legacy(uint id) => new(ChatKind.Channel, speaker, text, 0u, id)
        {
            LogTextType = LegacyChannelChatType.Resolve(id, ownSend: false),
        };
        ChatEntry Plain(RetailLogTextType type) => new(ChatKind.System, sender, text, 0u, 0u) { LogTextType = (uint)type };

        return channel switch
        {
            ChatDeckChannel.Say => Speech(ChatKind.LocalSpeech, RetailLogTextType.Speech),
            ChatDeckChannel.Emote => Speech(ChatKind.Emote, RetailLogTextType.Emote),
            // A player-range guid makes the wording "Bob tells you"; guid 0 would read as a sent tell.
            ChatDeckChannel.TellIn => Speech(ChatKind.Tell, RetailLogTextType.Tell, PlayerObjectIds.First),
            ChatDeckChannel.TellOut => Speech(ChatKind.Tell, RetailLogTextType.SpeechDirectSend),
            ChatDeckChannel.Fellowship => Legacy(0x00000800u),
            ChatDeckChannel.Patron => Legacy(0x00001000u),
            ChatDeckChannel.Vassals => Legacy(0x00002000u),
            ChatDeckChannel.Monarch => Legacy(0x00004000u),
            ChatDeckChannel.CoVassals => Legacy(0x01000000u),
            ChatDeckChannel.Allegiance => Room("Allegiance"),
            ChatDeckChannel.General => Room("General"),
            ChatDeckChannel.Trade => Room("Trade"),
            ChatDeckChannel.Lfg => Room("LFG"),
            ChatDeckChannel.Roleplay => Room("Roleplay"),
            ChatDeckChannel.Society => Room("Society"),
            ChatDeckChannel.Olthoi => Room("Olthoi"),
            ChatDeckChannel.Help => Legacy(0x00000400u),
            ChatDeckChannel.OtherChannel => Room("Other"),
            ChatDeckChannel.Combat => Plain(RetailLogTextType.Combat),
            ChatDeckChannel.CombatEnemy => Plain(RetailLogTextType.CombatEnemy),
            ChatDeckChannel.CombatSelf => Plain(RetailLogTextType.CombatSelf),
            ChatDeckChannel.Magic => Plain(RetailLogTextType.Magic),
            ChatDeckChannel.Spellcasting => Speech(ChatKind.LocalSpeech, RetailLogTextType.Spellcasting),
            ChatDeckChannel.Advancement => Plain(RetailLogTextType.Advancement),
            ChatDeckChannel.Appraisal => Plain(RetailLogTextType.Appraisal),
            ChatDeckChannel.Recall => Plain(RetailLogTextType.Recall),
            ChatDeckChannel.Craft => Plain(RetailLogTextType.Craft),
            ChatDeckChannel.Salvaging => Plain(RetailLogTextType.Salvaging),
            ChatDeckChannel.WorldBroadcast => Plain(RetailLogTextType.WorldBroadcast),
            ChatDeckChannel.Plugin => Plain(RetailLogTextType.ClientLocal),
            _ => Plain(RetailLogTextType.System),
        };
    }
}
