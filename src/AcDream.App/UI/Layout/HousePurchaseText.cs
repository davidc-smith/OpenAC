using System.Globalization;
using AcDream.Core.Net.Messages;
using AcDream.Runtime.Gameplay;

namespace AcDream.App.UI.Layout;

/// <summary>
/// Every line the slumlord window writes itself. The tab and button captions
/// come from the dat; this wording does not, and is a placeholder until it
/// is checked against a retail client (spec: Captions to check against
/// retail).
/// </summary>
internal static class HousePurchaseText
{
    public static string OwnerLine(GameEvents.HouseProfile profile) =>
        profile.OwnerGuid == 0u ? "For Sale" : "Owner: " + profile.OwnerName;

    public static IReadOnlyList<string> RequirementLines(
        GameEvents.HouseProfile profile,
        HousePurchaseTab tab,
        IReadOnlyList<int> stagedAmounts)
    {
        var lines = new List<string> { TypeName(profile.Type) };

        if (profile.MinLevel >= 0 && profile.MaxLevel >= 0)
            lines.Add($"Requires level {profile.MinLevel} to {profile.MaxLevel}");
        else if (profile.MinLevel >= 0)
            lines.Add($"Requires level {profile.MinLevel}+");
        if (profile.MinAllegianceRank >= 0)
            lines.Add($"Requires allegiance rank {profile.MinAllegianceRank}+");

        if (tab == HousePurchaseTab.Rent && profile.MaintenanceFree)
        {
            lines.Add("Maintenance is free this period.");
            return lines;
        }

        IReadOnlyList<GameEvents.HousePayment> payments =
            tab == HousePurchaseTab.Buy ? profile.Buy : profile.Rent;
        for (int i = 0; i < payments.Count; i++)
        {
            GameEvents.HousePayment payment = payments[i];
            int staged = i < stagedAmounts.Count ? stagedAmounts[i] : 0;
            int count = Math.Min(payment.Paid + staged, payment.Num);
            string line = $"{Number(count)}/{Number(payment.Num)} {HousePaymentNames.For(payment)}";
            lines.Add(line);
        }
        return lines;
    }

    public const uint ApartmentType = 4u;

    public const string BuyConfirmation =
        "When you buy a landscape house like this one, you are restricted from buying another for 30 days. Are you sure you want to buy this house?";

    public const string OthersMaintenanceConfirmation =
        "You are paying maintenance on someone else's house. Are you sure you wish to continue?";

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

    public static string Refusal(HouseStageResult result) => result switch
    {
        HouseStageResult.NotOpen => "There is no house to pay for.",
        HouseStageResult.AlreadyOwned => "This dwelling already has an owner.",
        HouseStageResult.NotOwned => "This dwelling has no owner.",
        HouseStageResult.MaintenanceFree => "Maintenance is free this period.",
        HouseStageResult.NotInInventory => "That item is not in your pack.",
        HouseStageResult.AlreadyStaged => "That item is already offered.",
        HouseStageResult.NotRequired => "That item is not needed here.",
        HouseStageResult.AlreadyCovered => "You have already offered enough of that.",
        HouseStageResult.NoteTooLarge => "That note is worth more than is owed.",
        _ => string.Empty,
    };

    private static string TypeName(uint type) => type switch
    {
        1u => "Cottage",
        2u => "Villa",
        3u => "Mansion",
        4u => "Apartment",
        _ => "Dwelling",
    };

    private static string Number(int value) =>
        value.ToString("N0", CultureInfo.InvariantCulture);
}
