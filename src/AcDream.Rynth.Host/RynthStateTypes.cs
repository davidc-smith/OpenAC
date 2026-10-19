namespace AcDream.Rynth.Host;

/// <summary>The player's vitals as RynthCore's GetPlayerVitals reports them.</summary>
public readonly record struct RynthVitals(
    uint Health, uint MaxHealth, uint Stamina, uint MaxStamina, uint Mana, uint MaxMana);

/// <summary>One enchantment on the player; expiry in server time, <see cref="double.MaxValue"/> = permanent.</summary>
public readonly record struct RynthEnchantment(uint SpellId, double ExpiryServerTime);

/// <summary>The open vendor as RynthCore's GetVendorInfo reports it; -1 for a count the client cannot tell.</summary>
public sealed record RynthVendor(
    uint VendorId,
    string Name,
    uint ItemTypes,
    int MinValue,
    int MaxValue,
    bool DealsMagic,
    float BuyRate,
    float SellRate,
    uint AltCurrencyWcid,
    string AltCurrencyName,
    int AltCurrencyServerCount,
    int AltCurrencyHave,
    int PlayerCoins,
    IReadOnlyList<RynthVendorItem> Items);

/// <summary>One vendor listing as RynthCore's GetVendorItems reports it; Amount -1 = unlimited.</summary>
public readonly record struct RynthVendorItem(
    uint ObjectId, uint Wcid, string Name, uint ItemType, uint IconId, int Amount, int StackSize,
    int MaxStackSize, int Value, int UnitValue, int UnitPrice, int Burden, bool NeedsContainerSlot)
{
    public bool Unlimited => Amount < 0;
    public bool Stackable => MaxStackSize > 0;
}

/// <summary>The server's answer to the last buy or sell.</summary>
public readonly record struct RynthVendorOutcome(bool Success, string? Notice);

/// <summary>How the client's open vendor changed (VendorState's transitions).</summary>
public enum RynthVendorChange
{
    /// <summary>A vendor (other than the open one, if any) is now open.</summary>
    Opened,
    /// <summary>The open vendor's list arrived again.</summary>
    Refreshed,
    Closed,
    /// <summary>Session teardown.</summary>
    Reset,
}

/// <summary>The trade window and its event counters, as RynthCore's GetTradeState reports them.</summary>
public readonly record struct RynthTradeState(
    bool IsOpen, bool YouAccepted, bool PartnerAccepted, uint PartnerId,
    IReadOnlyList<uint> YourItems, IReadOnlyList<uint> PartnerItems,
    uint Generation, uint Sequence, uint InitiatorId, uint LastEventType,
    uint FailureCount, uint LastFailureItemId, uint LastFailureReason, uint LastCloseReason,
    uint LastAcceptedBy, uint LastDeclinedBy, uint LastResetBy, uint CompletedCount, uint PartnerAcceptCount);
