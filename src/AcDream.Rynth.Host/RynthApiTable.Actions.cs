using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace AcDream.Rynth.Host;

// The table's action slots (RynthAi sub-project #2): 1 = sent, 0 = refused or failed.
public sealed unsafe partial class RynthApiTable
{
    private void BindActionSlots()
    {
        Native->SelectItemFn = (IntPtr)(delegate* unmanaged[Cdecl]<uint, int>)&SelectItem;
        Native->UseObjectFn = (IntPtr)(delegate* unmanaged[Cdecl]<uint, int>)&UseObject;
        Native->RequestIdFn = (IntPtr)(delegate* unmanaged[Cdecl]<uint, int>)&RequestId;
        Native->QueryHealthFn = (IntPtr)(delegate* unmanaged[Cdecl]<uint, int>)&QueryHealth;
        Native->ChangeCombatModeFn = (IntPtr)(delegate* unmanaged[Cdecl]<int, int>)&ChangeCombatMode;
        Native->MoveItemInternalFn = (IntPtr)(delegate* unmanaged[Cdecl]<uint, uint, int, int, int>)&MoveItemInternal;
        Native->SplitStackInternalFn = (IntPtr)(delegate* unmanaged[Cdecl]<uint, uint, int, int, int>)&SplitStackInternal;
        Native->MergeStackInternalFn = (IntPtr)(delegate* unmanaged[Cdecl]<uint, uint, int>)&MergeStackInternal;
        Native->ForceResetBusyCountFn = (IntPtr)(delegate* unmanaged[Cdecl]<void>)&ForceResetBusyCount;
        Native->WieldItemFn = (IntPtr)(delegate* unmanaged[Cdecl]<uint, uint, int>)&WieldItem;
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static int SelectItem(uint objectId)
    {
        RynthApiTable? table = _current;
        if (table is null)
            return 0;
        try
        {
            return table._view.SelectObject(objectId) ? 1 : 0;
        }
        catch (Exception error)
        {
            table.Fail(nameof(SelectItem), error);
            return 0;
        }
    }

    // RynthAi opens the salvage panel by using a Ust (SalvageManager → UseFor), not through
    // SalvagePanelOpen. Using a Ust only opens the panel, as in retail: nothing is sent.
    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static int UseObject(uint objectId)
    {
        RynthApiTable? table = _current;
        if (table is null)
            return 0;
        try
        {
            if (objectId == 0)
                return 0;
            if (table._view.IsOwnedTinkeringTool(objectId))
                return table.Salvage.Open(objectId) ? 1 : 0;
            return table._view.UseObject(objectId) ? 1 : 0;
        }
        catch (Exception error)
        {
            table.Fail(nameof(UseObject), error);
            return 0;
        }
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static int RequestId(uint objectId)
    {
        RynthApiTable? table = _current;
        if (table is null)
            return 0;
        try
        {
            return objectId != 0 && table._view.RequestIdentify(objectId) ? 1 : 0;
        }
        catch (Exception error)
        {
            table.Fail(nameof(RequestId), error);
            return 0;
        }
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static int QueryHealth(uint objectId)
    {
        RynthApiTable? table = _current;
        if (table is null)
            return 0;
        try
        {
            return objectId != 0 && table._view.QueryHealth(objectId) ? 1 : 0;
        }
        catch (Exception error)
        {
            table.Fail(nameof(QueryHealth), error);
            return 0;
        }
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static int ChangeCombatMode(int mode)
    {
        RynthApiTable? table = _current;
        if (table is null)
            return 0;
        try
        {
            return RynthTranslate.FromRynthCombatMode(mode) is not null && table._view.ChangeCombatMode(mode) ? 1 : 0;
        }
        catch (Exception error)
        {
            table.Fail(nameof(ChangeCombatMode), error);
            return 0;
        }
    }

    // RynthCore requires amount > 0 (its whole stack is the stack size, not 0).
    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static int MoveItemInternal(uint objectId, uint containerId, int slot, int amount)
    {
        RynthApiTable? table = _current;
        if (table is null)
            return 0;
        try
        {
            return objectId != 0 && containerId != 0 && amount > 0 && table._view.MoveItem(objectId, containerId, slot, amount) ? 1 : 0;
        }
        catch (Exception error)
        {
            table.Fail(nameof(MoveItemInternal), error);
            return 0;
        }
    }

    // OpenAC splits by moving an amount smaller than the stack, so this is the same call as MoveItemInternal.
    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static int SplitStackInternal(uint objectId, uint containerId, int slot, int amount)
    {
        RynthApiTable? table = _current;
        if (table is null)
            return 0;
        try
        {
            return objectId != 0 && containerId != 0 && amount > 0 && table._view.MoveItem(objectId, containerId, slot, amount) ? 1 : 0;
        }
        catch (Exception error)
        {
            table.Fail(nameof(SplitStackInternal), error);
            return 0;
        }
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static int MergeStackInternal(uint sourceObjectId, uint targetObjectId)
    {
        RynthApiTable? table = _current;
        if (table is null)
            return 0;
        try
        {
            return sourceObjectId != 0 && targetObjectId != 0 && table._view.MergeStack(sourceObjectId, targetObjectId) ? 1 : 0;
        }
        catch (Exception error)
        {
            table.Fail(nameof(MergeStackInternal), error);
            return 0;
        }
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static void ForceResetBusyCount()
    {
        RynthApiTable? table = _current;
        if (table is null)
            return;
        try
        {
            table._view.ResetBusyCount();
        }
        catch (Exception error)
        {
            table.Fail(nameof(ForceResetBusyCount), error);
        }
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static int WieldItem(uint objectId, uint equipMask)
    {
        RynthApiTable? table = _current;
        if (table is null)
            return 0;
        try
        {
            return objectId != 0 && table._view.WieldItem(objectId, equipMask) ? 1 : 0;
        }
        catch (Exception error)
        {
            table.Fail(nameof(WieldItem), error);
            return 0;
        }
    }
}
