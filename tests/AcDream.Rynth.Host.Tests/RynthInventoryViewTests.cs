using AcDream.Core.Items;

namespace AcDream.Rynth.Host.Tests;

public sealed class RynthInventoryViewTests
{
    [Theory]
    [InlineData(InventoryRequestKind.Merge, 1u)]
    [InlineData(InventoryRequestKind.SplitToContainer, 2u)]
    [InlineData(InventoryRequestKind.SplitToWorld, 2u)]
    [InlineData(InventoryRequestKind.Move, 3u)]
    [InlineData(InventoryRequestKind.Pickup, 4u)]
    [InlineData(InventoryRequestKind.PutInContainer, 5u)]
    [InlineData(InventoryRequestKind.DropToWorld, 6u)]
    [InlineData(InventoryRequestKind.Wield, 7u)]
    [InlineData(InventoryRequestKind.Give, 9u)]
    [InlineData(InventoryRequestKind.Shop, 10u)]
    public void OpenAcRequestKindsAreAcclientRequestTypes(InventoryRequestKind kind, uint acType) =>
        Assert.Equal(acType, RuntimeRynthGameView.AcRequestType(kind));

    [Fact]
    public void ObjectInfoCarriesSlotLocationsAndUseability()
    {
        var info = new RynthObjectInfo
        {
            ContainerId = 1, WielderId = 0, EquipLocation = 0, ItemType = 0, Wcid = 0, Bitfield = 0,
            IconId = 0, IconUnderlayId = 0, IconOverlayId = 0, SpellId = null, HouseOwnerId = null,
            StackSize = 1, StackSizeMax = 1, Value = 0, Burden = 0, ItemsCapacity = 0, Structure = 0,
            MaxStructure = 0, Workmanship = 0, MaterialType = null, Identified = false,
            AppraisedSpellIds = [], Ints = new Dictionary<uint, int>(), DataIds = new Dictionary<uint, uint>(),
            InstanceIds = new Dictionary<uint, uint>(),
        };
        Assert.Equal((0u, 0u, -1), (info.ValidLocations, info.Useability, info.ContainerSlot));
    }
}
