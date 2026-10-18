// tests/AcDream.Rynth.Host.Tests/RynthInventoryTableTests.cs
using System.Numerics;
using AcDream.Rynth.Host;
using RynthCore.PluginSdk;

namespace AcDream.Rynth.Host.Tests;

public sealed unsafe class RynthInventoryTableTests
{
    private const uint Player = 0x50000001;
    private const uint Pack = 0x80000020;
    private const uint Sword = 0x80000010;

    private readonly List<string> _log = new();
    private readonly FakeGameView _view = new() { PlayerId = Player };

    private static RynthObjectInfo Info(uint container, uint wielder, uint location, uint type) => new()
    {
        ContainerId = container,
        WielderId = wielder,
        EquipLocation = location,
        ItemType = type,
        Wcid = 136,
        Bitfield = 0x2000,
        IconId = 0x06001234,
        IconUnderlayId = 0,
        IconOverlayId = 0,
        SpellId = null,
        HouseOwnerId = null,
        StackSize = 1,
        StackSizeMax = 1,
        Value = 25,
        Burden = 15,
        ItemsCapacity = 24,
        Structure = 0,
        MaxStructure = 0,
        Workmanship = 0,
        MaterialType = null,
        Identified = false,
        AppraisedSpellIds = [],
        Ints = new Dictionary<uint, int>(),
        DataIds = new Dictionary<uint, uint>(),
        InstanceIds = new Dictionary<uint, uint>(),
    };

    private (RynthApiTable Table, RynthCoreHost Host) Create()
    {
        var table = new RynthApiTable(_view, _log.Add);
        return (table, new RynthCoreHost(*table.Native));
    }

    [Fact]
    public void ObjectReadsAnswerFromTheView()
    {
        _view.Objects[Pack] = Info(Player, 0, 0, 0x200);
        _view.Objects[Sword] = Info(0, Player, 0x100, 0x1) with
        {
            Identified = true,
            AppraisedSpellIds = [1, 2, 3],
        };
        var (table, host) = Create();
        using (table)
        {
            Assert.True(host.TryGetObjectOwnershipInfo(Pack, out uint container, out uint wielder, out uint location));
            Assert.Equal((Player, 0u, 0u), (container, wielder, location));
            Assert.False(host.TryGetObjectOwnershipInfo(0x80000099, out _, out _, out _));
            Assert.True(host.TryGetObjectWielderInfo(Sword, out uint swordWielder, out uint swordLocation));
            Assert.Equal((Player, 0x100u), (swordWielder, swordLocation));
            Assert.True(host.TryGetItemType(Pack, out uint type));
            Assert.Equal(0x200u, type);
            Assert.True(host.TryGetObjectWcid(Pack, out uint wcid));
            Assert.Equal(136u, wcid);
            Assert.True(host.TryGetObjectBitfield(Pack, out uint bitfield));
            Assert.Equal(0x2000u, bitfield);
            Assert.True(host.TryGetObjectIntProperty(Pack, 6, out int capacity));
            Assert.Equal(24, capacity);
            Assert.True(host.TryGetObjectDataIdProperty(Pack, 8, out uint icon));
            Assert.Equal(0x06001234u, icon);
            Assert.True(host.TryGetObjectInstanceIdProperty(Sword, 3, out uint iid));
            Assert.Equal(Player, iid);
            Assert.False(host.HasAppraisalData(Pack));
            Assert.True(host.HasAppraisalData(Sword));
            Assert.Equal(-1, host.GetObjectSpellIds(Pack, new uint[4], 4));
            uint[] spells = new uint[2];
            Assert.Equal(3, host.GetObjectSpellIds(Sword, spells, 2));
            Assert.Equal([1u, 2u], spells);
        }
    }

    [Fact]
    public void ContentsCopyAtMostTheBufferAndReturnTheCount()
    {
        _view.Contents[Player] = [Sword, Pack, 0x80000030];
        var (table, host) = Create();
        using (table)
        {
            uint[] two = new uint[2];
            Assert.Equal(2, host.GetContainerContents(Player, two));
            Assert.Equal([Sword, Pack], two);
            Assert.Equal(0, host.GetContainerContents(Pack, new uint[4]));
        }
    }

    [Fact]
    public void TheNativeContentsFunctionReturnsTheFullCountWhenTheBufferIsSmaller()
    {
        _view.Contents[Player] = [Sword, Pack, 0x80000030];
        var (table, _) = Create();
        using (table)
        {
            uint* buffer = stackalloc uint[2];
            int count = ((delegate* unmanaged[Cdecl]<uint, uint*, int, int>)table.Native->GetContainerContentsFn)(
                Player, buffer, 2);

            Assert.Equal(3, count);
            Assert.Equal((Sword, Pack), (buffer[0], buffer[1]));
        }
    }

    [Fact]
    public void NamesAndCoordinates()
    {
        _view.AccountName = "acct";
        _view.WorldName = "Local ACE";
        _view.Positions[Player] = (0xA9B40031u, new Vector3(84, 108, 0));
        var (table, host) = Create();
        using (table)
        {
            Assert.True(host.TryGetAccountName(out string account));
            Assert.Equal("acct", account);
            Assert.True(host.TryGetWorldName(out string world));
            Assert.Equal("Local ACE", world);
            Assert.True(host.TryGetObjectPosition(Player, out uint cell, out float x, out float y, out _));
            Assert.Equal((0xA9B40031u, 84f, 108f), (cell, x, y));
            Assert.True(host.TryGetCurCoords(out double ns, out double ew));
            Assert.Equal((0xA9 * 8 + 84 / 24.0 - 1019.5) / 10, ew, 6);
            Assert.Equal((0xB4 * 8 + 108 / 24.0 - 1019.5) / 10, ns, 6);

            _view.AccountName = string.Empty;
            Assert.False(host.TryGetAccountName(out _));
        }
    }

    [Fact]
    public void AThrowingViewAnswersNothingAndLogs()
    {
        _view.Throw = true;
        var (table, host) = Create();
        using (table)
        {
            Assert.False(host.TryGetObjectIntProperty(Pack, 6, out _));
            Assert.False(host.TryGetObjectDataIdProperty(Pack, 8, out _));
            Assert.False(host.TryGetObjectInstanceIdProperty(Pack, 3, out _));
            Assert.Equal(0, host.GetContainerContents(Player, new uint[4]));
            Assert.False(host.TryGetObjectOwnershipInfo(Pack, out _, out _, out _));
            Assert.False(host.TryGetObjectWielderInfo(Sword, out _, out _));
            Assert.False(host.TryGetItemType(Pack, out _));
            Assert.False(host.TryGetObjectWcid(Pack, out _));
            Assert.True(host.TryGetObjectBitfield(Pack, out uint bitfield));
            Assert.Equal(0u, bitfield);
            Assert.False(host.HasAppraisalData(Sword));
            Assert.Equal(-1, host.GetObjectSpellIds(Sword, new uint[4], 4));
            Assert.False(host.TryGetObjectPosition(Player, out _, out _, out _, out _));
            Assert.False(host.TryGetCurCoords(out _, out _));
            Assert.False(host.TryGetAccountName(out _));
            Assert.False(host.TryGetWorldName(out _));
            Assert.Contains(_log, line => line.Contains("threw InvalidOperationException", StringComparison.Ordinal));
        }
    }
}
