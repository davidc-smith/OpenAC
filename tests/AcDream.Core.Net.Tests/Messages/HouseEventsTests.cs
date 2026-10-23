using AcDream.Core.Net.Messages;
using Xunit;

namespace AcDream.Core.Net.Tests.Messages;

public sealed class HouseEventsTests
{
    [Fact]
    public void BuildHouseQuery_WritesEnvelopeSequenceOpcodeOnly()
    {
        byte[] body = ClientCommandRequests.BuildHouseQuery(9);

        Assert.Equal(12, body.Length);
        Assert.Equal(ClientCommandRequests.HouseQueryOpcode,
            System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(body.AsSpan(8)));
    }

    [Fact]
    public void ParseHouseStatus_ReadsWeenieError()
    {
        byte[] wire = new AceWireWriter().Write(0u).ToArray();

        Assert.Equal(0u, GameEvents.ParseHouseStatus(wire));
    }

    [Fact]
    public void ParseHouseStatus_TruncatedPayload_ReturnsNull()
    {
        Assert.Null(GameEvents.ParseHouseStatus(System.Array.Empty<byte>()));
    }

    [Fact]
    public void ParseUpdateRentTime_ReadsTimestamp()
    {
        byte[] wire = new AceWireWriter().Write(1_700_000_000u).ToArray();

        Assert.Equal(1_700_000_000u, GameEvents.ParseUpdateRentTime(wire));
    }

    [Fact]
    public void ParseUpdateRentPayment_EmptyList_RoundTrips()
    {
        byte[] wire = new AceWireWriter().Write(0).ToArray();

        var payments = GameEvents.ParseUpdateRentPayment(wire);

        Assert.NotNull(payments);
        Assert.Empty(payments);
    }

    [Fact]
    public void ParseUpdateRentPayment_OneEntry_RoundTrips()
    {
        byte[] wire = new AceWireWriter()
            .Write(1)
            .Write(400)      // Num
            .Write(150)      // Paid
            .Write(273u)     // WeenieID (pyreal)
            .WriteString16L("Pyreal")
            .WriteString16L("Pyreals")
            .ToArray();

        var payments = GameEvents.ParseUpdateRentPayment(wire);

        Assert.NotNull(payments);
        GameEvents.HousePayment payment = Assert.Single(payments);
        Assert.Equal(400, payment.Num);
        Assert.Equal(150, payment.Paid);
        Assert.Equal(273u, payment.WeenieID);
        Assert.Equal("Pyreal", payment.Name);
        Assert.Equal("Pyreals", payment.PluralName);
    }

    [Fact]
    public void ParseHouseData_NoHouseOwned_EmptyListsAndZeroFields()
    {
        byte[] wire = new AceWireWriter()
            .Write(0u)  // BuyTime
            .Write(0u)  // RentTime
            .Write(0u)  // Type (Undef)
            .Write(0u)  // MaintenanceFree
            .Write(0)
            .Write(0)
            .Write(0x00120001u)
            .Write(10f).Write(20f).Write(30f)
            .Write(1f).Write(0f).Write(0f).Write(0f)
            .ToArray();

        GameEvents.HouseData? data = GameEvents.ParseHouseData(wire);

        Assert.NotNull(data);
        Assert.Equal(0u, data!.Value.BuyTime);
        Assert.Empty(data.Value.Buy);
        Assert.Empty(data.Value.Rent);
        Assert.Equal(0x00120001u, data.Value.Position.LandblockId);
        Assert.Equal(10f, data.Value.Position.PositionX);
        Assert.Equal(30f, data.Value.Position.PositionZ);
        Assert.Equal(1f, data.Value.Position.RotationW);
    }

    [Fact]
    public void ParseHouseData_OwnedHouse_ReadsBuyAndRentLists()
    {
        byte[] wire = new AceWireWriter()
            .Write(1_650_000_000u)  // BuyTime
            .Write(1_699_000_000u)  // RentTime
            .Write(1u)
            .Write(0u)               // MaintenanceFree = false
            .Write(1)
                .Write(1).Write(1).Write(273u)
                .WriteString16L("Pyreal").WriteString16L("Pyreals")
            .Write(2)
                .Write(300).Write(300).Write(273u)
                .WriteString16L("Pyreal").WriteString16L("Pyreals")
                .Write(1).Write(0).Write(1049u)
                .WriteString16L("Writ of the Chosen").WriteString16L("Writs of the Chosen")
            // Position
            .Write(0x00340002u)
            .Write(-15f).Write(45f).Write(0f)
            .Write(0.7071f).Write(0f).Write(0f).Write(0.7071f)
            .ToArray();

        GameEvents.HouseData? data = GameEvents.ParseHouseData(wire);

        Assert.NotNull(data);
        Assert.Equal(1_650_000_000u, data!.Value.BuyTime);
        Assert.Equal(1_699_000_000u, data.Value.RentTime);
        Assert.Equal(1u, data.Value.Type);
        Assert.False(data.Value.MaintenanceFree);
        Assert.Single(data.Value.Buy);
        Assert.Equal(2, data.Value.Rent.Count);
        Assert.Equal("Writ of the Chosen", data.Value.Rent[1].Name);
        Assert.Equal(0x00340002u, data.Value.Position.LandblockId);
    }

    [Fact]
    public void ParseHouseData_TruncatedPayload_ReturnsNull()
    {
        byte[] wire = new AceWireWriter().Write(0u).Write(0u).ToArray();

        Assert.Null(GameEvents.ParseHouseData(wire));
    }

    private static AceWireWriter ProfileHeader(
        AceWireWriter w,
        uint slumlord = 0x70000100u,
        uint owner = 0u,
        int minLevel = -1,
        int maxLevel = -1,
        int minRank = -1,
        int maxRank = -1,
        bool maintenanceFree = false,
        uint type = 1u,
        string ownerName = "") =>
        w.Write(slumlord)
            .Write(0x12340001u)   // dwelling id
            .Write(owner)
            .Write(1u)            // bitmask: Active
            .Write(minLevel)
            .Write(maxLevel)
            .Write(minRank)
            .Write(maxRank)
            .Write(maintenanceFree ? 1u : 0u)
            .Write(type)
            .WriteString16L(ownerName);

    [Fact]
    public void ParseHouseProfile_Unowned_EmptyLists()
    {
        byte[] wire = ProfileHeader(new AceWireWriter())
            .Write(0).Write(0)
            .ToArray();

        GameEvents.HouseProfile? parsed = GameEvents.ParseHouseProfile(wire);

        Assert.NotNull(parsed);
        GameEvents.HouseProfile p = parsed.Value;
        Assert.Equal(0x70000100u, p.SlumlordGuid);
        Assert.Equal(0x12340001u, p.DwellingId);
        Assert.Equal(0u, p.OwnerGuid);
        Assert.Equal(1u, p.Bitmask);
        Assert.Equal(-1, p.MinLevel);
        Assert.Equal(-1, p.MaxAllegianceRank);
        Assert.False(p.MaintenanceFree);
        Assert.Equal(1u, p.Type);
        Assert.Equal("", p.OwnerName);
        Assert.Empty(p.Buy);
        Assert.Empty(p.Rent);
    }

    [Fact]
    public void ParseHouseProfile_Owned_ReadsHeaderAndBothLists()
    {
        byte[] wire = ProfileHeader(
                new AceWireWriter(),
                owner: 0x50000042u,
                minLevel: 20,
                maxLevel: 99,
                minRank: 6,
                maxRank: 10,
                maintenanceFree: true,
                type: 3u,
                ownerName: "Sammy")
            .Write(2)
            .Write(50_000).Write(50_000).Write(273u)
                .WriteString16L("Pyreal").WriteString16L("Pyreals")
            .Write(1).Write(1).Write(9000u)
                .WriteString16L("Bone Totem").WriteString16L("")
            .Write(1)
            .Write(5_000).Write(1_250).Write(273u)
                .WriteString16L("Pyreal").WriteString16L("Pyreals")
            .ToArray();

        GameEvents.HouseProfile p = GameEvents.ParseHouseProfile(wire)!.Value;

        Assert.Equal(0x50000042u, p.OwnerGuid);
        Assert.Equal(20, p.MinLevel);
        Assert.Equal(99, p.MaxLevel);
        Assert.Equal(6, p.MinAllegianceRank);
        Assert.Equal(10, p.MaxAllegianceRank);
        Assert.True(p.MaintenanceFree);
        Assert.Equal(3u, p.Type);
        Assert.Equal("Sammy", p.OwnerName);
        Assert.Equal(2, p.Buy.Count);
        Assert.Equal(new GameEvents.HousePayment(1, 1, 9000u, "Bone Totem", ""), p.Buy[1]);
        GameEvents.HousePayment rent = Assert.Single(p.Rent);
        Assert.Equal(5_000, rent.Num);
        Assert.Equal(1_250, rent.Paid);
    }

    [Fact]
    public void ParseHouseProfile_TruncatedHeader_ReturnsNull()
    {
        byte[] wire = new AceWireWriter().Write(0x70000100u).Write(1u).ToArray();

        Assert.Null(GameEvents.ParseHouseProfile(wire));
    }

    [Fact]
    public void ParseHouseProfile_TruncatedPayment_ReturnsNull()
    {
        byte[] wire = ProfileHeader(new AceWireWriter())
            .Write(1)
            .Write(50_000).Write(0)   // the wcid and names are missing
            .ToArray();

        Assert.Null(GameEvents.ParseHouseProfile(wire));
    }

    [Fact]
    public void BuildBuyHouse_WritesSlumlordCountAndGuids()
    {
        byte[] body = ClientCommandRequests.BuildBuyHouse(
            7u, 0x70000100u, [0x60000001u, 0x60000002u]);

        Assert.Equal(28, body.Length);
        Assert.Equal(7u, System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(body.AsSpan(4)));
        Assert.Equal(0x021Cu, System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(body.AsSpan(8)));
        Assert.Equal(0x70000100u, System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(body.AsSpan(12)));
        Assert.Equal(2u, System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(body.AsSpan(16)));
        Assert.Equal(0x60000001u, System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(body.AsSpan(20)));
        Assert.Equal(0x60000002u, System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(body.AsSpan(24)));
    }

    [Fact]
    public void BuildRentHouse_EmptyList_WritesZeroCount()
    {
        byte[] body = ClientCommandRequests.BuildRentHouse(3u, 0x70000100u, []);

        Assert.Equal(20, body.Length);
        Assert.Equal(0x0221u, System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(body.AsSpan(8)));
        Assert.Equal(0x70000100u, System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(body.AsSpan(12)));
        Assert.Equal(0u, System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(body.AsSpan(16)));
    }
}
