using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using RynthCore.PluginSdk;

namespace AcDream.Rynth.Host;

// The table's vendor slots (RynthAi sub-project #5): RynthCore's VendorTrade fills and
// its buy/sell request model, kept by RynthVendorTrades.
public sealed unsafe partial class RynthApiTable
{
    private void BindVendorSlots()
    {
        Native->GetVendorInfoFn = (IntPtr)(delegate* unmanaged[Cdecl]<VendorInfoNative*, int>)&GetVendorInfo;
        Native->GetVendorItemsFn = (IntPtr)(delegate* unmanaged[Cdecl]<VendorItemNative*, int, int>)&GetVendorItems;
        Native->VendorBuyFn = (IntPtr)(delegate* unmanaged[Cdecl]<uint, VendorTradeEntryNative*, int, uint>)&VendorBuy;
        Native->VendorSellFn = (IntPtr)(delegate* unmanaged[Cdecl]<uint, uint*, int, uint>)&VendorSell;
        Native->GetVendorTradeStatusFn =
            (IntPtr)(delegate* unmanaged[Cdecl]<VendorTradeStatusNative*, int>)&GetVendorTradeStatus;
    }

    // RynthCore's FillInfo: 1 with the open vendor, 0 with none.
    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static int GetVendorInfo(VendorInfoNative* info)
    {
        RynthApiTable? table = _current;
        if (table is null || info is null)
            return 0;
        try
        {
            *info = default;
            if (table._view.Vendor is not { } vendor)
                return 0;
            info->VendorId = vendor.VendorId;
            info->Generation = table.Vendor.Generation;
            info->ItemTypes = vendor.ItemTypes;
            info->MinValue = vendor.MinValue;
            info->MaxValue = vendor.MaxValue;
            info->DealsMagic = vendor.DealsMagic ? 1 : 0;
            info->BuyRate = vendor.BuyRate;
            info->SellRate = vendor.SellRate;
            info->AltCurrencyWcid = vendor.AltCurrencyWcid;
            info->AltCurrencyServerCount = vendor.AltCurrencyServerCount;
            info->AltCurrencyHave = vendor.AltCurrencyHave;
            info->PlayerCoins = vendor.PlayerCoins;
            info->ItemCount = vendor.Items.Count;
            info->Flags = 1u | (table.Vendor.InFlight ? 2u : 0u);
            WriteAnsi(info->Name, 64, vendor.Name);
            WriteAnsi(info->AltCurrencyName, 64, vendor.AltCurrencyName);
            return 1;
        }
        catch (Exception error)
        {
            table.Fail(nameof(GetVendorInfo), error);
            return 0;
        }
    }

    // RynthCore's FillItems: fills up to max entries and returns the total, -1 with no vendor.
    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static int GetVendorItems(VendorItemNative* items, int max)
    {
        RynthApiTable? table = _current;
        if (table is null)
            return -1;
        try
        {
            if (table._view.Vendor is not { } vendor)
                return -1;
            IReadOnlyList<RynthVendorItem> source = vendor.Items;
            if (items is not null && max > 0)
            {
                int n = Math.Min(max, source.Count);
                for (int i = 0; i < n; i++)
                {
                    RynthVendorItem item = source[i];
                    VendorItemNative* d = items + i;
                    *d = default;
                    d->ObjectId = item.ObjectId;
                    d->Wcid = item.Wcid;
                    d->ItemType = item.ItemType;
                    d->IconId = item.IconId;
                    d->Amount = item.Unlimited ? -1 : item.Amount;
                    d->StackSize = item.StackSize;
                    d->MaxStackSize = item.MaxStackSize;
                    d->Value = item.Value;
                    d->UnitValue = item.UnitValue;
                    d->UnitPrice = item.UnitPrice;
                    d->Burden = item.Burden;
                    d->Flags = (item.Unlimited ? 1u : 0u) | (item.Stackable ? 2u : 0u)
                        | (item.NeedsContainerSlot ? 4u : 0u);
                    WriteAnsi(d->Name, 64, item.Name);
                }
            }
            return source.Count;
        }
        catch (Exception error)
        {
            table.Fail(nameof(GetVendorItems), error);
            return -1;
        }
    }

    // RynthCore's RequestBuy: the request id, or 0 when refused (the reason is in the status).
    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static uint VendorBuy(uint vendorId, VendorTradeEntryNative* entries, int count)
    {
        RynthApiTable? table = _current;
        if (table is null || (entries is null && count > 0))
            return 0;
        try
        {
            // A count over the limit still reaches Buy, which records the refusal.
            int n = Math.Clamp(count, 0, RynthVendorTrades.MaxLines + 1);
            var lines = new (uint ObjectId, int Amount)[n];
            for (int i = 0; i < n; i++)
                lines[i] = (entries[i].ObjectId, entries[i].Amount);
            return table.Vendor.Buy(vendorId, lines);
        }
        catch (Exception error)
        {
            table.Fail(nameof(VendorBuy), error);
            return 0;
        }
    }

    // RynthCore's RequestSell: the request id, or 0 when refused (the reason is in the status).
    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static uint VendorSell(uint vendorId, uint* itemIds, int count)
    {
        RynthApiTable? table = _current;
        if (table is null || (itemIds is null && count > 0))
            return 0;
        try
        {
            int n = Math.Clamp(count, 0, RynthVendorTrades.MaxLines + 1);
            var ids = new uint[n];
            for (int i = 0; i < n; i++)
                ids[i] = itemIds[i];
            return table.Vendor.Sell(vendorId, ids);
        }
        catch (Exception error)
        {
            table.Fail(nameof(VendorSell), error);
            return 0;
        }
    }

    // RynthCore's FillStatus: 0 before the first request.
    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static int GetVendorTradeStatus(VendorTradeStatusNative* status)
    {
        RynthApiTable? table = _current;
        if (table is null || status is null)
            return 0;
        try
        {
            *status = default;
            var current = table.Vendor.Status;
            if (current.RequestId == 0)
                return 0;
            status->RequestId = current.RequestId;
            status->State = current.State;
            status->Result = current.Result;
            status->IsBuy = current.IsBuy ? 1 : 0;
            status->VendorId = current.VendorId;
            status->EntryCount = current.EntryCount;
            status->Estimate = 0;
            WriteAnsi(status->Message, 128, current.Message);
            return 1;
        }
        catch (Exception error)
        {
            table.Fail(nameof(GetVendorTradeStatus), error);
            return 0;
        }
    }

    // RynthCore's WriteAnsi: Latin-1 bytes, cut to fit, always zero-terminated.
    private static void WriteAnsi(byte* buffer, int size, string? text)
    {
        int n = 0;
        if (!string.IsNullOrEmpty(text))
            for (; n < text.Length && n < size - 1; n++)
                buffer[n] = text[n] <= 0xFF ? (byte)text[n] : (byte)'?';
        buffer[n] = 0;
    }
}
