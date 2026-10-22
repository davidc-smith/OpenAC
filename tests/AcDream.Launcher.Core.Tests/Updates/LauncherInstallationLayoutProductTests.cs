using AcDream.Launcher.Core.Updates;
using AcDream.Platform;

namespace AcDream.Launcher.Core.Tests.Updates;

public sealed class LauncherInstallationLayoutProductTests
{
    private static readonly string Container =
        Path.GetFullPath(Path.Combine(Path.GetTempPath(), "layout-product"));

    [Fact]
    public void TheMacBundleIsNamedAfterTheProduct()
    {
        string bundle = Path.Combine(Container, OpenACProductIdentity.MacAppBundleName);

        LauncherInstallationLayout layout = LauncherInstallationLayout.MacBundle(bundle, "osx-arm64");

        Assert.Equal(OpenACProductIdentity.MacAppBundleName, layout.PayloadRoot);
        Assert.Equal(
            $"{OpenACProductIdentity.MacAppBundleName}/Contents/MacOS/acdream-launcher",
            layout.PayloadLauncherPath);
    }

    /// <summary>Mutation: still accepting upstream's OpenAC.app would let one product's launcher
    /// run (and self-update) inside the other's or upstream's bundle.</summary>
    [Fact]
    public void UpstreamsBundleNameIsRejected()
    {
        LauncherUpdateException error = Assert.Throws<LauncherUpdateException>(() =>
            LauncherInstallationLayout.MacBundle(Path.Combine(Container, "OpenAC.app"), "osx-arm64"));

        Assert.Contains(OpenACProductIdentity.MacAppBundleName, error.Message, StringComparison.Ordinal);
    }
}
