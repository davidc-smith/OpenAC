using AcDream.Core.Chat;
using AcDream.Core.Chat.Deck;

namespace AcDream.Core.Tests.Chat.Deck;

public sealed class ChatDeckChannelsTests
{
    private static ChatEntry Channel(uint id, string sender, uint type, string name = "") =>
        new(ChatKind.Channel, sender, "hi", 0u, id) { LogTextType = type, ChannelName = name };

    [Theory]
    [InlineData(0x00000800u, ChatDeckChannel.Fellowship)]
    [InlineData(0x00001000u, ChatDeckChannel.Vassals)]
    [InlineData(0x00002000u, ChatDeckChannel.Patron)]
    [InlineData(0x00004000u, ChatDeckChannel.Monarch)]
    [InlineData(0x01000000u, ChatDeckChannel.CoVassals)]
    [InlineData(0x02000000u, ChatDeckChannel.Allegiance)]
    [InlineData(0x40000000u, ChatDeckChannel.Olthoi)]
    [InlineData(0x00000400u, ChatDeckChannel.Help)]
    [InlineData(0x00000001u, ChatDeckChannel.OtherChannel)]
    public void LegacyChannel_ReceivedAndOwnSent_ClassifyAlike(uint id, ChatDeckChannel expected)
    {
        Assert.Equal(expected, ChatDeckChannels.Classify(Channel(id, "Bob", LegacyChannelChatType.Resolve(id, false))));
        Assert.Equal(expected, ChatDeckChannels.Classify(Channel(id, "", LegacyChannelChatType.Resolve(id, true))));
    }

    [Theory]
    [InlineData("Allegiance", 0x12u, ChatDeckChannel.Allegiance)]
    [InlineData("Olthoi", 0x12u, ChatDeckChannel.Olthoi)]
    [InlineData("General", 0x1Bu, ChatDeckChannel.General)]
    [InlineData("Trade", 0x1Cu, ChatDeckChannel.Trade)]
    [InlineData("LFG", 0x1Du, ChatDeckChannel.Lfg)]
    [InlineData("Roleplay", 0x1Eu, ChatDeckChannel.Roleplay)]
    [InlineData("Society", 0x20u, ChatDeckChannel.Society)]
    [InlineData("Celestial Hand", 0x20u, ChatDeckChannel.Society)]
    [InlineData("Eldrytch Web", 0x20u, ChatDeckChannel.Society)]
    [InlineData("Radiant Blood", 0x20u, ChatDeckChannel.Society)]
    [InlineData("Room 0x00ABCDEF", 0x00u, ChatDeckChannel.OtherChannel)]
    public void TurbineRoom_ClassifiesByName(string name, uint type, ChatDeckChannel expected)
    {
        // Room ids are arbitrary numbers; 0x800 here must not be read as Fellowship.
        Assert.Equal(expected, ChatDeckChannels.Classify(Channel(0x800u, "Bob", type, name)));
    }

    [Fact]
    public void Tells_SplitByDirection()
    {
        var inbound = new ChatEntry(ChatKind.Tell, "Bob", "hi", 0x50000001u, 0u) { LogTextType = 0x03u };
        var admin = inbound with { LogTextType = 0x1Fu };
        var outbound = new ChatEntry(ChatKind.Tell, "Bob", "hi", 0u, 0u) { LogTextType = 0x04u };
        Assert.Equal(ChatDeckChannel.TellIn, ChatDeckChannels.Classify(inbound));
        Assert.Equal(ChatDeckChannel.TellIn, ChatDeckChannels.Classify(admin));
        Assert.Equal(ChatDeckChannel.TellOut, ChatDeckChannels.Classify(outbound));
    }

    [Theory]
    [InlineData(ChatKind.LocalSpeech, 0x02u, ChatDeckChannel.Say)]
    [InlineData(ChatKind.RangedSpeech, 0x02u, ChatDeckChannel.Say)]
    [InlineData(ChatKind.LocalSpeech, 0x11u, ChatDeckChannel.Spellcasting)]
    [InlineData(ChatKind.Emote, 0x0Cu, ChatDeckChannel.Emote)]
    [InlineData(ChatKind.SoulEmote, 0x0Cu, ChatDeckChannel.Emote)]
    [InlineData(ChatKind.Combat, 0x15u, ChatDeckChannel.CombatEnemy)]
    [InlineData(ChatKind.Combat, 0x16u, ChatDeckChannel.CombatSelf)]
    [InlineData(ChatKind.Combat, 0x06u, ChatDeckChannel.Combat)]
    [InlineData(ChatKind.System, 0x07u, ChatDeckChannel.Magic)]
    [InlineData(ChatKind.System, 0x05u, ChatDeckChannel.System)]
    [InlineData(ChatKind.System, 0x0Du, ChatDeckChannel.Advancement)]
    [InlineData(ChatKind.System, 0x19u, ChatDeckChannel.Salvaging)]
    [InlineData(ChatKind.System, 0x1Au, ChatDeckChannel.Plugin)]
    [InlineData(ChatKind.System, 0x00u, ChatDeckChannel.System)]
    [InlineData(ChatKind.Popup, 0x00u, ChatDeckChannel.System)]
    public void OtherKinds_ClassifyByKindAndType(ChatKind kind, uint type, ChatDeckChannel expected)
    {
        var entry = new ChatEntry(kind, "", "text", 0u, 0u) { LogTextType = type };
        Assert.Equal(expected, ChatDeckChannels.Classify(entry));
    }

    [Fact]
    public void IsOwnLine_RecognisesOwnLines()
    {
        Assert.True(ChatDeckChannels.IsOwnLine(new ChatEntry(ChatKind.LocalSpeech, "You", "hi", 1u, 0u) { LogTextType = 0x02u }));
        Assert.True(ChatDeckChannels.IsOwnLine(new ChatEntry(ChatKind.Tell, "Bob", "hi", 0u, 0u) { LogTextType = 0x04u }));
        Assert.True(ChatDeckChannels.IsOwnLine(Channel(0x800u, "", 0x13u)));
        Assert.True(ChatDeckChannels.IsOwnLine(Channel(0x1000u, "", 0x0Bu)));
        Assert.False(ChatDeckChannels.IsOwnLine(Channel(0x800u, "Bob", 0x13u)));
        Assert.False(ChatDeckChannels.IsOwnLine(new ChatEntry(ChatKind.LocalSpeech, "Bob", "hi", 2u, 0u) { LogTextType = 0x02u }));
    }

    [Fact]
    public void EveryChannel_IsInExactlyOneGroup()
    {
        var all = Enum.GetValues<ChatDeckChannel>();
        var grouped = ChatDeckChannels.Groups.SelectMany(g => g.Channels).ToList();
        Assert.Equal(all.Length, grouped.Count);
        Assert.Equal(all.OrderBy(c => c), grouped.OrderBy(c => c));
        Assert.All(all, c => Assert.False(string.IsNullOrEmpty(ChatDeckChannels.DisplayName(c))));
    }
}
