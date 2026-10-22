using System.Reflection;
using System.Text.RegularExpressions;
using AcDream.Core.Plugins;
using AcDream.Launcher.Core.Updates;

namespace AcDream.App.Tests.Release;

public sealed class ReleaseIdentityTests
{
    public static TheoryData<Assembly> ShippedAssemblies => new()
    {
        typeof(AcDream.App.Rendering.GameWindow).Assembly, // any public AcDream.App type works
        typeof(LauncherVersion).Assembly,
    };

    [Theory]
    [MemberData(nameof(ShippedAssemblies))]
    public void TheReleaseVersionIsUpstreamsBasePlusAProductCounter(Assembly assembly)
    {
        string informational = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()!
            .InformationalVersion;
        string release = informational.Split('+')[0];

        Assert.Matches(new Regex(@"^\d+\.\d+\.\d+-[a-z]+\.\d+$"), release);
        Assert.True(LauncherVersion.TryParse(informational, out _), informational);
        PluginHostVersion host = Assert.NotNull(PluginHostVersion.FromInformationalVersion(informational));
        Assert.StartsWith(host.ToString() + "-", release);
    }

    [Fact]
    public void TheClientNamesItselfByProduct() =>
        Assert.Equal(
            typeof(AcDream.App.Rendering.GameWindow).Assembly.GetCustomAttribute<AssemblyProductAttribute>()!.Product,
            AcDream.App.ProductIdentity.Name);

    [Theory]
    [MemberData(nameof(ShippedAssemblies))]
    public void TheProductIsAnOpenACGatewayEdition(Assembly assembly)
    {
        string product = assembly.GetCustomAttribute<AssemblyProductAttribute>()!.Product;
        Assert.StartsWith("OpenAC Gateway", product);
    }
}
