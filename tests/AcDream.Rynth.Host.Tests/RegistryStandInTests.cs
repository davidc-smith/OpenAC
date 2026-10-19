using AcDream.Rynth.Plugin.RynthAi.StandIns;

namespace AcDream.Rynth.Host.Tests;

public sealed class RegistryStandInTests : IDisposable
{
    private const string AcKey = @"SOFTWARE\WOW6432Node\Turbine\Asheron's Call";

    public void Dispose() => RegistryStandIn.InstallPath = null;

    [Fact]
    public void NoKeyWhileThePathIsUnset()
    {
        RegistryStandIn.InstallPath = null;
        Assert.Null(RegistryStandIn.LocalMachine.OpenSubKey(AcKey));
    }

    [Fact]
    public void TheAcKeyAnswersInstallPath()
    {
        RegistryStandIn.InstallPath = "/dats";
        using RegistryKeyStandIn? key = RegistryStandIn.LocalMachine.OpenSubKey(AcKey.ToUpperInvariant());
        Assert.NotNull(key);
        Assert.Equal("/dats", key!.GetValue("installpath"));
        Assert.Null(key.GetValue("Version"));
    }

    [Fact]
    public void OtherKeysAreMissing()
    {
        RegistryStandIn.InstallPath = "/dats";
        Assert.Null(RegistryStandIn.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft"));
    }
}
