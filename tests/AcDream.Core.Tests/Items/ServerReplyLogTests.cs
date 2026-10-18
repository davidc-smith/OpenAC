using AcDream.Core.Items;

namespace AcDream.Core.Tests.Items;

public sealed class ServerReplyLogTests
{
    [Fact]
    public void StartsEmptyAndCountsEachReply()
    {
        var log = new ServerReplyLog();
        Assert.Equal(default, log.Snapshot);

        log.RecordUseDone(0);
        log.RecordUseDone(0x0036);
        log.RecordWeenieError(0x028A, 0x04A, 0);
        log.RecordWeenieError(0x00A0, 0x0029, 0x80000010);

        ServerReplySnapshot snapshot = log.Snapshot;
        Assert.Equal(2, snapshot.UseDoneSequence);
        Assert.Equal(0x0036u, snapshot.LastUseDoneError);
        Assert.Equal(2, snapshot.WeenieErrorSequence);
        Assert.Equal(0x0029u, snapshot.LastWeenieError);
        Assert.Equal(0x00A0u, snapshot.LastWeenieErrorEventType);
        Assert.Equal(0x80000010u, snapshot.LastWeenieErrorObjectId);
    }
}
