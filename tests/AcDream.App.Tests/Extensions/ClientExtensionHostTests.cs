using AcDream.App.Extensions;

namespace AcDream.App.Tests.Extensions;

public sealed class ClientExtensionHostTests
{
    [Fact]
    public void RynthIsCreatedByDefault()
    {
        using var host = new ClientExtensionHost();

        Assert.Equal(1, host.Count);
    }

    [Fact]
    public void RynthTurnedOffIsNeverCreated()
    {
        using var host = new ClientExtensionHost(rynth: false);

        Assert.Equal(0, host.Count);
    }
}
