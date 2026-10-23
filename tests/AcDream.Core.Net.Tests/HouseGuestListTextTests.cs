using AcDream.Core.Net.Messages;
using Xunit;

namespace AcDream.Core.Net.Tests;

public sealed class HouseGuestListTextTests
{
    [Fact]
    public void AnEmptyList_ReadsGuestsThenNone()
    {
        var har = new GameEvents.HouseAccess(0u, 0u, [], []);

        Assert.Equal(new[] { "Guests:", " None" }, HouseGuestListText.Lines(har));
    }

    [Fact]
    public void AStorageGuest_IsMarkedWithAnAsterisk()
    {
        var har = new GameEvents.HouseAccess(0u, 0u,
            [
                new GameEvents.HouseGuest(0x50000002u, "Alice", false),
                new GameEvents.HouseGuest(0x50000003u, "Bob", true),
            ],
            []);

        Assert.Equal(new[] { "Guests:", " Alice", " Bob *" }, HouseGuestListText.Lines(har));
    }
}
