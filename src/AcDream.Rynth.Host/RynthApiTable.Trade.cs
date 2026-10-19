using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using RynthCore.PluginSdk;

namespace AcDream.Rynth.Host;

// The table's trade slots (RynthAi sub-project #5): RynthCore's PlayerTrade state, items and actions.
public sealed unsafe partial class RynthApiTable
{
    private void BindTradeSlots()
    {
        Native->GetTradeStateFn = (IntPtr)(delegate* unmanaged[Cdecl]<TradeStateNative*, int>)&GetTradeState;
        Native->GetTradeItemsFn = (IntPtr)(delegate* unmanaged[Cdecl]<int, uint*, int, int>)&GetTradeItems;
        Native->TradeOpenFn = (IntPtr)(delegate* unmanaged[Cdecl]<uint, int>)&TradeOpen;
        Native->TradeAddFn = (IntPtr)(delegate* unmanaged[Cdecl]<uint, uint, int>)&TradeAdd;
        Native->TradeAcceptFn = (IntPtr)(delegate* unmanaged[Cdecl]<int>)&TradeAccept;
        Native->TradeDeclineFn = (IntPtr)(delegate* unmanaged[Cdecl]<int>)&TradeDecline;
        Native->TradeResetFn = (IntPtr)(delegate* unmanaged[Cdecl]<int>)&TradeReset;
        Native->TradeCloseFn = (IntPtr)(delegate* unmanaged[Cdecl]<int>)&TradeClose;
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static int GetTradeState(TradeStateNative* state)
    {
        RynthApiTable? table = _current;
        if (table is null || state is null || state->Size < 8)
            return 0;
        try
        {
            if (table._view.Trade is not { } t)
                return 0;
            uint want = state->Size;
            TradeStateNative s = default;
            // Watching and actions available: OpenAC always sees the trade events and binds every action.
            s.Flags = (t.IsOpen ? 1u : 0u) | (t.YouAccepted ? 2u : 0u) | (t.PartnerAccepted ? 4u : 0u) | 8u | 16u;
            s.Generation = t.Generation;
            s.Sequence = t.Sequence;
            s.PartnerId = t.PartnerId;
            s.InitiatorId = t.InitiatorId;
            s.SelfItemCount = t.YourItems.Count;
            s.PartnerItemCount = t.PartnerItems.Count;
            s.LastEventType = t.LastEventType;
            s.FailureCount = t.FailureCount;
            s.LastFailureItemId = t.LastFailureItemId;
            s.LastFailureReason = t.LastFailureReason;
            s.LastCloseReason = t.LastCloseReason;
            s.LastAcceptedBy = t.LastAcceptedBy;
            s.LastDeclinedBy = t.LastDeclinedBy;
            s.LastResetBy = t.LastResetBy;
            s.CompletedCount = t.CompletedCount;
            s.PartnerAcceptCount = t.PartnerAcceptCount;
            uint n = Math.Min(want, (uint)sizeof(TradeStateNative));
            s.Size = n;
            Buffer.MemoryCopy(&s, state, want, n);
            return (int)n;
        }
        catch (Exception error)
        {
            table.Fail(nameof(GetTradeState), error);
            return 0;
        }
    }

    // RynthCore's GetItems: side 1 is you, 2 the partner; returns that side's total, -1 for a bad side.
    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static int GetTradeItems(int side, uint* buffer, int capacity)
    {
        RynthApiTable? table = _current;
        if (table is null || side is not (1 or 2))
            return -1;
        try
        {
            if (table._view.Trade is not { } t)
                return 0;
            IReadOnlyList<uint> source = side == 1 ? t.YourItems : t.PartnerItems;
            if (buffer is not null && capacity > 0)
            {
                int n = Math.Min(capacity, source.Count);
                for (int i = 0; i < n; i++)
                    buffer[i] = source[i];
            }
            return source.Count;
        }
        catch (Exception error)
        {
            table.Fail(nameof(GetTradeItems), error);
            return 0;
        }
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static int TradeOpen(uint partnerId)
    {
        RynthApiTable? table = _current;
        if (table is null || partnerId == 0)
            return 0;
        try { return table._view.TradeOpen(partnerId) ? 1 : 0; }
        catch (Exception error) { table.Fail(nameof(TradeOpen), error); return 0; }
    }

    // The slot is ignored: the server places the item.
    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static int TradeAdd(uint itemId, uint slot)
    {
        RynthApiTable? table = _current;
        if (table is null || itemId == 0)
            return 0;
        try { return table._view.TradeAdd(itemId) ? 1 : 0; }
        catch (Exception error) { table.Fail(nameof(TradeAdd), error); return 0; }
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static int TradeAccept()
    {
        RynthApiTable? table = _current;
        if (table is null)
            return 0;
        try { return table._view.TradeAccept() ? 1 : 0; }
        catch (Exception error) { table.Fail(nameof(TradeAccept), error); return 0; }
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static int TradeDecline()
    {
        RynthApiTable? table = _current;
        if (table is null)
            return 0;
        try { return table._view.TradeDecline() ? 1 : 0; }
        catch (Exception error) { table.Fail(nameof(TradeDecline), error); return 0; }
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static int TradeReset()
    {
        RynthApiTable? table = _current;
        if (table is null)
            return 0;
        try { return table._view.TradeReset() ? 1 : 0; }
        catch (Exception error) { table.Fail(nameof(TradeReset), error); return 0; }
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static int TradeClose()
    {
        RynthApiTable? table = _current;
        if (table is null)
            return 0;
        try { return table._view.TradeClose() ? 1 : 0; }
        catch (Exception error) { table.Fail(nameof(TradeClose), error); return 0; }
    }
}
