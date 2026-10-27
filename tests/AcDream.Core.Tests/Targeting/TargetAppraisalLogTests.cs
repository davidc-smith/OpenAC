using AcDream.Core.Targeting;
using Xunit;

namespace AcDream.Core.Tests.Targeting;

public sealed class TargetAppraisalLogTests
{
    [Fact]
    public void RecordsTimeSuccessAndHealth()
    {
        var log = new TargetAppraisalLog();
        log.Record(7, success: true, healthPercent: 0.5f, now: 10.0);
        Assert.True(log.TryGet(7, out TargetAppraisalEntry e));
        Assert.Equal(10.0, e.At);
        Assert.True(e.Success);
        Assert.Equal(0.5f, e.HealthPercent);
        log.Forget(7);
        Assert.False(log.TryGet(7, out _));
    }
}
