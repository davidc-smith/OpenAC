using AcDream.App.Extensions;

namespace AcDream.App.Tests.Extensions;

public sealed class ClientInputStateTests
{
    [Theory]
    [InlineData(false, false, false, "nothing")]
    [InlineData(true, false, true, "the chat entry")]
    [InlineData(false, true, true, "a client text box")]
    [InlineData(true, true, true, "the chat entry")]
    public void TypingFollowsTheChatEntryAndUiFocus(bool chat, bool uiFocus, bool typing, string where)
    {
        var state = new ClientInputState(() => uiFocus, () => chat);

        Assert.Equal(typing, state.IsTyping);
        Assert.Equal(where, state.TypingDescription);
    }
}
