using AcDream.Launcher.Core.Orchestration;
using AcDream.Launcher.Core.Profiles;

namespace AcDream.Launcher.Core.Tests.Orchestration;

public sealed class LauncherPlatformCapabilitiesTests
{
    [Fact]
    public void MacOSProvidesBothLauncherModes()
    {
        LauncherPlatformCapabilities capabilities =
            LauncherPlatformCapabilities.ForOperatingSystem(
                isWindows: false,
                isLinux: false,
                isMacOS: true);

        Assert.True(capabilities.IsMacOS);
        Assert.False(capabilities.IsWindows);
        Assert.False(capabilities.IsLinux);
        Assert.Equal("macOS", capabilities.PlatformName);
        Assert.True(capabilities.CanLaunchGraphicalClient);
        Assert.True(capabilities.CanRunHeadless);
        Assert.True(capabilities.ForLaunchMode(LaunchMode.Gui).IsAvailable);
        Assert.True(capabilities.ForLaunchMode(LaunchMode.GuiSelect).IsAvailable);
        Assert.True(capabilities.ForLaunchMode(LaunchMode.Headless).IsAvailable);
    }

    /// <summary>
    /// Linux without a display keeps headless rows on the windowless host; Windows and
    /// macOS always have one.
    /// </summary>
    [Fact]
    public void LinuxWithoutADisplayHasNoWindowSupport()
    {
        Assert.False(LauncherPlatformCapabilities.ForOperatingSystem(
            isWindows: false, isLinux: true, isMacOS: false, hasDisplay: false).HasDisplay);
        Assert.True(LauncherPlatformCapabilities.ForOperatingSystem(
            isWindows: false, isLinux: true, isMacOS: false, hasDisplay: true).HasDisplay);
        Assert.True(LauncherPlatformCapabilities.ForOperatingSystem(
            isWindows: true, isLinux: false, isMacOS: false, hasDisplay: false).HasDisplay);
        Assert.True(LauncherPlatformCapabilities.ForOperatingSystem(
            isWindows: false, isLinux: false, isMacOS: true, hasDisplay: false).HasDisplay);
    }
}
