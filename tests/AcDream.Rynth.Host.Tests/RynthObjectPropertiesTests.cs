using AcDream.Rynth.Host;

namespace AcDream.Rynth.Host.Tests;

public sealed class RynthObjectPropertiesTests
{
    private static RynthObjectInfo Item(
        Dictionary<uint, int>? ints = null,
        Dictionary<uint, uint>? dataIds = null,
        Dictionary<uint, uint>? instanceIds = null) => new()
    {
        ContainerId = 0x50000001,
        WielderId = 0,
        EquipLocation = 0,
        ItemType = 0x200,
        Wcid = 136,
        Bitfield = 0x1,
        IconId = 0x06001234,
        IconUnderlayId = 0,
        IconOverlayId = 0x06005678,
        SpellId = null,
        HouseOwnerId = null,
        StackSize = 3,
        StackSizeMax = 100,
        Value = 25,
        Burden = 15,
        ItemsCapacity = 24,
        Structure = 7,
        MaxStructure = 10,
        Workmanship = 6.6f,
        MaterialType = null,
        Identified = false,
        AppraisedSpellIds = [],
        Ints = ints ?? new(),
        DataIds = dataIds ?? new(),
        InstanceIds = instanceIds ?? new(),
    };

    [Theory]
    [InlineData(1u, 0x200)]
    [InlineData(5u, 15)]
    [InlineData(6u, 24)]
    [InlineData(10u, 0)]
    [InlineData(11u, 100)]
    [InlineData(12u, 3)]
    [InlineData(19u, 25)]
    [InlineData(91u, 10)]
    [InlineData(92u, 7)]
    [InlineData(105u, 7)]
    public void IntsComeFromTheTypedFields(uint stat, int expected)
    {
        Assert.True(RynthObjectProperties.TryGetInt(Item(), stat, out int value));
        Assert.Equal(expected, value);
    }

    [Fact]
    public void OtherIntsComeFromTheDictionaryAndMaterialOnlyWhenKnown()
    {
        RynthObjectInfo item = Item(ints: new() { [28] = 120 });

        Assert.True(RynthObjectProperties.TryGetInt(item, 28, out int armor));
        Assert.Equal(120, armor);
        Assert.False(RynthObjectProperties.TryGetInt(item, 131, out _));
        Assert.False(RynthObjectProperties.TryGetInt(item, 265, out _));
        Assert.True(RynthObjectProperties.TryGetInt(item with { MaterialType = 61 }, 131, out int material));
        Assert.Equal(61, material);
    }

    [Fact]
    public void IconsAnswerEvenWhenNone()
    {
        RynthObjectInfo item = Item(dataIds: new() { [22] = 0x0D000001 });

        Assert.True(RynthObjectProperties.TryGetDataId(item, 8, out uint icon));
        Assert.Equal(0x06001234u, icon);
        Assert.True(RynthObjectProperties.TryGetDataId(item, 52, out uint underlay));
        Assert.Equal(0u, underlay);
        Assert.True(RynthObjectProperties.TryGetDataId(item, 50, out uint overlay));
        Assert.Equal(0x06005678u, overlay);
        Assert.False(RynthObjectProperties.TryGetDataId(item, 28, out _));
        Assert.True(RynthObjectProperties.TryGetDataId(item with { SpellId = 2053 }, 28, out uint spell));
        Assert.Equal(2053u, spell);
        Assert.True(RynthObjectProperties.TryGetDataId(item, 22, out uint other));
        Assert.Equal(0x0D000001u, other);
    }

    [Fact]
    public void InstanceIdsAreFalseWhenUnset()
    {
        RynthObjectInfo item = Item(instanceIds: new() { [26] = 0x50000009 });

        Assert.True(RynthObjectProperties.TryGetInstanceId(item, 2, out uint container));
        Assert.Equal(0x50000001u, container);
        Assert.False(RynthObjectProperties.TryGetInstanceId(item, 3, out _));
        Assert.False(RynthObjectProperties.TryGetInstanceId(item, 32, out _));
        Assert.True(RynthObjectProperties.TryGetInstanceId(item with { HouseOwnerId = 0x50000002 }, 32, out uint owner));
        Assert.Equal(0x50000002u, owner);
        Assert.True(RynthObjectProperties.TryGetInstanceId(item, 26, out uint monarch));
        Assert.Equal(0x50000009u, monarch);
    }
}
