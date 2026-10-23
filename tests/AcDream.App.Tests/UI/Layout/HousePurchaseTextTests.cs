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
    public void OwnerLine_NamesTheOwnerOrSaysForSale()
    {
        Assert.Equal("For Sale", HousePurchaseText.OwnerLine(Profile()));
        Assert.Equal("Owner: Sammy",
            HousePurchaseText.OwnerLine(Profile(owner: 5u, ownerName: "Sammy")));
    }

    [Fact]
    public void RequirementLines_Buy_TypeThenPaymentsFilledInByStaged()
    {
        IReadOnlyList<string> lines = HousePurchaseText.RequirementLines(
            Profile(), HousePurchaseTab.Buy, [8_000, 0]);

        Assert.Equal(
            ["Cottage", "8,000/50,000 Pyreals", "0/1 Bone Totem"],
            lines);
    }

    [Fact]
    public void RequirementLines_ShowLimits()
    {
        Assert.Contains("Requires level 20+",
            HousePurchaseText.RequirementLines(Profile(minLevel: 20), HousePurchaseTab.Buy, [0, 0]));
        Assert.Contains("Requires level 20 to 50",
            HousePurchaseText.RequirementLines(Profile(minLevel: 20, maxLevel: 50), HousePurchaseTab.Buy, [0, 0]));
        Assert.Contains("Requires allegiance rank 6+",
            HousePurchaseText.RequirementLines(Profile(type: 3u, minRank: 6), HousePurchaseTab.Buy, [0, 0]));
    }

    [Fact]
    public void RequirementLines_Rent_PaidAmountsOrFree()
    {
        Assert.Equal(
            ["Villa", "1,250/5,000 Pyreals"],
            HousePurchaseText.RequirementLines(Profile(type: 2u), HousePurchaseTab.Rent, [0]));
        Assert.Equal(
            ["Mansion", "Maintenance is free this period."],
            HousePurchaseText.RequirementLines(
                Profile(type: 3u, maintenanceFree: true), HousePurchaseTab.Rent, [0]));
    }

    [Fact]
    public void Refusal_HasASentenceForEveryRefusal()
    {
        foreach (HouseStageResult result in Enum.GetValues<HouseStageResult>())
        {
            if (result == HouseStageResult.Staged) continue;
            Assert.False(string.IsNullOrWhiteSpace(HousePurchaseText.Refusal(result)));
        }
    }
}
