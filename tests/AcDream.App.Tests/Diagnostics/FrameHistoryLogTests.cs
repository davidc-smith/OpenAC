using System.Linq;
using AcDream.App.Diagnostics;

namespace AcDream.App.Tests.Diagnostics;

public sealed class FrameHistoryLogTests
{
    private static FrameHistoryRecord Row(int frame) =>
        new(frame, frame * 2.5, frame, -1, 0, 0, 0, 0, 0, 0, 0, 0);

    [Fact]
    public void RowsComeBackInOrderAcrossBlocks()
    {
        var log = new FrameHistoryLog();
        int count = (FrameHistoryLog.BlockRows * 2) + 7;
        for (int i = 0; i < count; i++)
            log.Add(Row(i));

        Assert.Equal(count, log.Count);
        Assert.Equal(Enumerable.Range(0, count), log.Rows().Select(r => r.FrameIndex));
    }

    [Fact]
    public void ARowIsUpdatedInPlace()
    {
        var log = new FrameHistoryLog();
        for (int i = 0; i < FrameHistoryLog.BlockRows + 3; i++)
            log.Add(Row(i));

        ref FrameHistoryRecord row = ref log[FrameHistoryLog.BlockRows + 1];
        row = row with { GpuUs = 42 };

        Assert.Equal(42, log[FrameHistoryLog.BlockRows + 1].GpuUs);
        Assert.Equal(-1, log[FrameHistoryLog.BlockRows].GpuUs);
        Assert.Throws<ArgumentOutOfRangeException>(() => log[FrameHistoryLog.BlockRows + 3]);
    }

    [Fact]
    public void AShortSessionTakesOneBlock()
    {
        var log = new FrameHistoryLog();
        log.Add(Row(0));

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 1; i < 2000; i++)
            log.Add(Row(i));
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.Equal(0, allocated);
    }
}
