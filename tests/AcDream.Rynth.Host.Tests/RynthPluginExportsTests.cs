using AcDream.Rynth.Host;

namespace AcDream.Rynth.Host.Tests;

public sealed class RynthPluginExportsTests
{
    [Fact]
    public void FromType_FindsRynthTrackerExports()
    {
        RynthPluginExports exports = RynthPluginExports.FromType(
            typeof(RynthCore.Plugin.RynthTracker.PluginExports));

        Assert.Contains("RynthPluginInit", exports.Names);
        Assert.Contains("RynthPluginTick", exports.Names);
        Assert.Contains("RynthPluginOnChatWindowText", exports.Names);
        Assert.Contains("RynthPluginOnKillNotification", exports.Names);
        Assert.Contains("RynthTrackerGetSnapshotJson", exports.Names);
        Assert.True(exports.TryGet("RynthPluginInit", out IntPtr init));
        Assert.NotEqual(IntPtr.Zero, init);
        Assert.False(exports.TryGet("RynthPluginOnChatBarEnter", out _));
    }
}
