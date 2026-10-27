using AcDream.Core.Combat;
using AcDream.Core.Items;
using Xunit;

namespace AcDream.Core.Tests.Items;

public sealed class PublicWeenieFlagsTests
{
    [Theory]
    [InlineData(PublicWeenieFlags.Openable, 0x1u)]
    [InlineData(PublicWeenieFlags.Player, 0x8u)]
    [InlineData(PublicWeenieFlags.Attackable, 0x10u)]
    [InlineData(PublicWeenieFlags.PlayerKiller, 0x20u)]
    [InlineData(PublicWeenieFlags.Vendor, 0x200u)]
    [InlineData(PublicWeenieFlags.Door, 0x1000u)]
    [InlineData(PublicWeenieFlags.Corpse, 0x2000u)]
    [InlineData(PublicWeenieFlags.Lifestone, 0x4000u)]
    [InlineData(PublicWeenieFlags.Portal, 0x40000u)]
    [InlineData(PublicWeenieFlags.FreePlayerKiller, 0x200000u)]
    [InlineData(PublicWeenieFlags.PlayerKillerLite, 0x2000000u)]
    [InlineData(PublicWeenieFlags.Bindstone, 0x8000000u)]
    public void BitsMatchTheRetailBitfield(PublicWeenieFlags flag, uint bit) =>
        Assert.Equal(bit, (uint)flag);

    [Fact]
    public void TheHealthPolicyConstantsAreTheEnumBits()
    {
        Assert.Equal((uint)PublicWeenieFlags.Player, SelectedObjectHealthPolicy.BfPlayer);
        Assert.Equal((uint)PublicWeenieFlags.Attackable, SelectedObjectHealthPolicy.BfAttackable);
        Assert.Equal((uint)PublicWeenieFlags.PlayerKiller, SelectedObjectHealthPolicy.BfPlayerKiller);
        Assert.Equal((uint)PublicWeenieFlags.FreePlayerKiller, SelectedObjectHealthPolicy.BfFreePkStatus);
        Assert.Equal((uint)PublicWeenieFlags.PlayerKillerLite, SelectedObjectHealthPolicy.BfPkLiteStatus);
    }

    [Fact]
    public void FlagsOfReadsTheObjectsBitfield()
    {
        var obj = new ClientObject { ObjectId = 1, PublicWeenieBitfield = 0x2010u };
        Assert.Equal(PublicWeenieFlags.Corpse | PublicWeenieFlags.Attackable, PublicWeenieFlagsExtensions.FlagsOf(obj));
        Assert.Equal(PublicWeenieFlags.None, PublicWeenieFlagsExtensions.FlagsOf(new ClientObject { ObjectId = 2 }));
    }
}
