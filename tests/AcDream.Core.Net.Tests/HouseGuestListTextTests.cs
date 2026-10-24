using AcDream.Core.Net.Messages;
using Xunit;

namespace AcDream.Core.Net.Tests;

public sealed class HouseGuestListTextTests
{
    [Fact]
    public void AnEmptyList_ReadsGuestsThenNone()
    {
        var har = new GameEvents.HouseAccess(0u, 0u, [], []);

        Assert.Equal("Guests:\n  None\n", HouseGuestListText.Compose(har));
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

        Assert.Equal("Guests:\n  Alice\n  Bob *\n", HouseGuestListText.Compose(har));
    }

    [Theory]
    [InlineData(0x2u, "Allegiance Permissions: guest access\n")]
    [InlineData(0x4u, "Allegiance Permissions: storage access\n")]
    public void OneAllegianceBit_AddsItsPermissionLine(uint bitmask, string line)
    {
        var har = new GameEvents.HouseAccess(bitmask, 0x50000001u, [], []);

        Assert.Equal("Guests:\n  None\n" + line, HouseGuestListText.Compose(har));
    }

    [Fact]
    public void BothAllegianceBits_RunTogetherWithNoSeparator()
    {
        var har = new GameEvents.HouseAccess(0x6u, 0x50000001u,
            [new GameEvents.HouseGuest(0x50000002u, "Alice", true)],
            []);

        Assert.Equal(
            "Guests:\n  Alice *\nAllegiance Permissions: guest accessstorage access\n",
            HouseGuestListText.Compose(har));
    }

    [Fact]
    public void AnOpenHouse_EndsWithTheOpenHouseLine()
    {
        var har = new GameEvents.HouseAccess(0x1u, 0u, [], []);

        Assert.Equal("Guests:\n  None\n[ open house ]\n", HouseGuestListText.Compose(har));
    }

    [Fact]
    public void AllFlags_PrintPermissionsBeforeOpenHouse_AndNoRoommates()
    {
        var har = new GameEvents.HouseAccess(0x7u, 0x50000001u,
            [new GameEvents.HouseGuest(0x50000002u, "Alice", false)],
            [0x50000004u]);

        Assert.Equal(
            "Guests:\n  Alice\n"
            + "Allegiance Permissions: guest accessstorage access\n"
            + "[ open house ]\n",
            HouseGuestListText.Compose(har));
    }
}
