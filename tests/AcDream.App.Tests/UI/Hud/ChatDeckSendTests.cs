using AcDream.App.UI.Hud;
using AcDream.Core.Chat.Deck;
using AcDream.Runtime.Chat;

namespace AcDream.App.Tests.UI.Hud;

public sealed class ChatDeckSendTests
{
    [Fact]
    public void EverySendChannel_MapsByName_AndHasAName()
    {
        foreach (ChatDeckSendChannel channel in Enum.GetValues<ChatDeckSendChannel>())
        {
            Assert.Equal(channel.ToString(), ChatDeckSend.ToKind(channel).ToString());
            Assert.False(string.IsNullOrWhiteSpace(ChatDeckSend.DisplayName(channel)));
        }
        Assert.Equal("Co-vassals", ChatDeckSend.DisplayName(ChatDeckSendChannel.CoVassals));
        Assert.Equal("LFG", ChatDeckSend.DisplayName(ChatDeckSendChannel.Lfg));
    }
}
