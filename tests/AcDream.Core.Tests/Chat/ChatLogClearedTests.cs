using AcDream.Core.Chat;

namespace AcDream.Core.Tests.Chat;

public sealed class ChatLogClearedTests
{
    [Fact]
    public void Clear_RaisesClearedOnceAfterTheBufferIsEmpty()
    {
        var log = new ChatLog();
        log.OnSystemMessage("one", 0x05u);
        int raised = 0;
        int countWhenRaised = -1;
        log.Cleared += () => { raised++; countWhenRaised = log.Count; };

        log.Clear();

        Assert.Equal(1, raised);
        Assert.Equal(0, countWhenRaised);
    }
}
