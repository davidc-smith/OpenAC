using AcDream.Core.Net.Messages;

namespace AcDream.Runtime.Gameplay;

public static class HousePaymentNames
{
    /// <summary>The item name for a payment line: singular for one,
    /// otherwise the plural the server sent, or an English guess.</summary>
    public static string For(GameEvents.HousePayment payment)
    {
        if (payment.Num == 1)
            return payment.Name;
        if (!string.IsNullOrEmpty(payment.PluralName))
            return payment.PluralName;

        return payment.Name.EndsWith('s') || payment.Name.EndsWith('x')
            ? payment.Name + "es"
            : payment.Name + "s";
    }
}
