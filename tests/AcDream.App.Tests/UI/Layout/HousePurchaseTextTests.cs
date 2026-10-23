using AcDream.App.UI.Layout;
using AcDream.Core.Net.Messages;
using AcDream.Runtime.Gameplay;

namespace AcDream.App.Tests.UI.Layout;

public sealed class HousePurchaseTextTests
{
    private static GameEvents.HouseProfile Profile(
        uint owner = 0u, string ownerName = "", uint type = 1u,
        int minLevel = -1, int maxLevel = -1, int minRank = -1,
        bool maintenanceFree = false) =>
        new(0x70000100u, 1u, owner, 1u, minLevel, maxLevel, minRank, -1,
            maintenanceFree, type, ownerName,
            [new(50_000, 0, 273u, "Pyreal", "Pyreals"), new(1, 0, 9000u, "Bone Totem", "")],
            [new(5_000, 1_250, 273u, "Pyreal", "Pyreals")]);

    [Fact]
    public void OwnerLine_NamesTheOwnerOrNone()
    {
        Assert.Equal("Owner: None", HousePurchaseText.OwnerLine(Profile()));
        Assert.Equal("Owner: Sammy",
            HousePurchaseText.OwnerLine(Profile(owner: 5u, ownerName: "Sammy")));
    }

    [Fact]
    public void OwnerLine_IsNone_WhenTheNameIsEmpty_WhateverTheOwnerId()
    {
        // The server sends an empty name when it cannot find the owner.
        Assert.Equal("Owner: None", HousePurchaseText.OwnerLine(Profile(owner: 5u, ownerName: "")));
        Assert.Equal("Owner: Sammy", HousePurchaseText.OwnerLine(Profile(owner: 0u, ownerName: "Sammy")));
    }

    [Fact]
    public void RequirementText_Buy_OneLineOfPlainNumbers()
    {
        Assert.Equal("50000 Pyreals, 1 Bone Totem",
            HousePurchaseText.RequirementText(
                Profile(minLevel: 20, minRank: 6, type: 3u), HousePurchaseTab.Buy));
    }

    [Fact]
    public void RequirementText_Maintenance_PaidOverOwed_EvenWhenFree()
    {
        Assert.Equal("1250/5000 Pyreals",
            HousePurchaseText.RequirementText(Profile(), HousePurchaseTab.Rent));
        Assert.Equal("1250/5000 Pyreals",
            HousePurchaseText.RequirementText(Profile(maintenanceFree: true), HousePurchaseTab.Rent));
    }

    [Fact]
    public void Confirmation_BuyingAHouseButNotAnApartment_AndOthersMaintenance()
    {
        const uint me = 5u;
        Assert.Equal(HousePurchaseText.BuyConfirmation,
            HousePurchaseText.Confirmation(HousePurchaseTab.Buy, Profile(type: 1u), me));
        Assert.Null(
            HousePurchaseText.Confirmation(HousePurchaseTab.Buy, Profile(type: 4u), me));
        Assert.Null(
            HousePurchaseText.Confirmation(HousePurchaseTab.Rent, Profile(owner: me), me));
        Assert.Equal(HousePurchaseText.OthersMaintenanceConfirmation,
            HousePurchaseText.Confirmation(HousePurchaseTab.Rent, Profile(owner: 9u), me));
    }

    [Fact]
    public void Refusal_OnlyAnItemNotCarriedIsPrinted()
    {
        foreach (HouseStageResult result in Enum.GetValues<HouseStageResult>())
        {
            string? refusal = HousePurchaseText.Refusal(result);
            if (result == HouseStageResult.NotInInventory)
                Assert.Equal("You can only trade items you are carrying", refusal);
            else
                Assert.Null(refusal);
        }
    }
}
