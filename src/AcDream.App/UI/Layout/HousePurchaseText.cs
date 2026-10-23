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
            string line = $"{HousePaymentNames.For(payment)}: "
                + $"{Number(payment.Paid)} / {Number(payment.Num)}";
            int staged = i < stagedAmounts.Count ? stagedAmounts[i] : 0;
            if (staged > 0)
                line += $" (+{Number(staged)} staged)";
            lines.Add(line);
        }
        return lines;
    }

    public static string Refusal(HouseStageResult result) => result switch
    {
        HouseStageResult.NotOpen => "There is no house to pay for.",
        HouseStageResult.AlreadyOwned => "This dwelling already has an owner.",
        HouseStageResult.MaintenanceFree => "Maintenance is free this period.",
        HouseStageResult.NotInInventory => "That item is not in your pack.",
        HouseStageResult.AlreadyStaged => "That item is already offered.",
        HouseStageResult.NotRequired => "That item is not needed here.",
        HouseStageResult.AlreadyCovered => "You have already offered enough of that.",
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
