using AcDream.Core.Net.Messages;
using AcDream.Runtime.Gameplay;

namespace AcDream.App.UI.Layout;

/// <summary>
/// Every line the slumlord window writes itself. The tab and button captions
/// come from the dat.
/// </summary>
internal static class HousePurchaseText
{
    public const uint ApartmentType = 4u;

    public const string BuyConfirmation =
        "When you buy a landscape house like this one, you are restricted from buying another for 30 days. Are you sure you want to buy this house?";

    public const string OthersMaintenanceConfirmation =
        "You are paying maintenance on someone else's house. Are you sure you wish to continue?";

    public static string OwnerLine(GameEvents.HouseProfile profile) =>
        "Owner: " + (profile.OwnerGuid == 0u ? "None" : profile.OwnerName);

    /// <summary>The tab's requirements on one line: "N Name, N Name" to buy,
    /// "paid/num Name, ..." for maintenance.</summary>
    public static string RequirementText(GameEvents.HouseProfile profile, HousePurchaseTab tab)
    {
        IReadOnlyList<GameEvents.HousePayment> payments =
            tab == HousePurchaseTab.Buy ? profile.Buy : profile.Rent;
        return string.Join(", ", payments.Select(payment =>
            tab == HousePurchaseTab.Buy
                ? $"{payment.Num} {HousePaymentNames.For(payment)}"
                : $"{payment.Paid}/{payment.Num} {HousePaymentNames.For(payment)}"));
    }

    /// <summary>The question asked before paying, or null when none is:
    /// buying anything but an apartment, or paying maintenance on a house
    /// someone else owns.</summary>
    public static string? Confirmation(
        HousePurchaseTab tab, GameEvents.HouseProfile profile, uint playerGuid) => tab switch
    {
        HousePurchaseTab.Buy when profile.Type != ApartmentType => BuyConfirmation,
        HousePurchaseTab.Rent when profile.OwnerGuid != playerGuid => OthersMaintenanceConfirmation,
        _ => null,
    };

    /// <summary>The chat line for a refused item, or null for the refusals
    /// that print nothing.</summary>
    public static string? Refusal(HouseStageResult result) => result switch
    {
        HouseStageResult.NotInInventory => "You can only trade items you are carrying",
        _ => null,
    };
}
