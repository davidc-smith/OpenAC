using AcDream.Platform;

namespace AcDream.Platform.Tests;

public sealed class OpenACProductIdentityTests
{
    [Fact]
    public void TheProductIsAnOpenACGatewayEdition() =>
        Assert.StartsWith("OpenAC Gateway", OpenACProductIdentity.Name, StringComparison.Ordinal);

    /// <summary>Mutation: a slug that keeps spaces or capitals, or is not the product's, fails this.</summary>
    [Fact]
    public void TheSlugIsTheProductInLowerCaseJoinedByHyphens() =>
        Assert.Equal(
            OpenACProductIdentity.Name.ToLowerInvariant().Replace(' ', '-'),
            OpenACProductIdentity.Slug);

    [Fact]
    public void FolderAndBundleNamesComeFromTheProduct()
    {
        Assert.Equal(OpenACProductIdentity.Name, OpenACProductIdentity.DataFolderName);
        Assert.Equal(OpenACProductIdentity.Slug, OpenACProductIdentity.LinuxDataFolderName);
        Assert.Equal(OpenACProductIdentity.Name + ".app", OpenACProductIdentity.MacAppBundleName);
    }

    /// <summary>Two products must never share a data folder with each other or with upstream's
    /// OpenAC, so side-by-side installs cannot see each other's clients and settings.</summary>
    [Fact]
    public void TheDataFolderIsNotUpstreamsOpenACFolder()
    {
        Assert.NotEqual("OpenAC", OpenACProductIdentity.DataFolderName);
        Assert.NotEqual("openac", OpenACProductIdentity.LinuxDataFolderName);
    }
}
