using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace AcDream.Rynth.Host;

// The table's salvage panel and SalvageDirect slots (RynthAi sub-project #5).
public sealed unsafe partial class RynthApiTable
{
    private void BindSalvageSlots()
    {
        Native->SalvagePanelOpenFn = (IntPtr)(delegate* unmanaged[Cdecl]<uint, int>)&SalvagePanelOpen;
        Native->SalvagePanelAddItemFn = (IntPtr)(delegate* unmanaged[Cdecl]<uint, int>)&SalvagePanelAddItem;
        Native->SalvagePanelExecuteFn = (IntPtr)(delegate* unmanaged[Cdecl]<int>)&SalvagePanelExecute;
        Native->SalvageDirectFn = (IntPtr)(delegate* unmanaged[Cdecl]<uint, uint*, int, int>)&SalvageDirect;
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static int SalvagePanelOpen(uint toolId)
    {
        RynthApiTable? table = _current;
        if (table is null)
            return 0;
        try { return table.Salvage.Open(toolId) ? 1 : 0; }
        catch (Exception error)
        {
            table.Fail(nameof(SalvagePanelOpen), error);
            return 0;
        }
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static int SalvagePanelAddItem(uint itemId)
    {
        RynthApiTable? table = _current;
        if (table is null)
            return 0;
        try { return table.Salvage.Add(itemId) ? 1 : 0; }
        catch (Exception error)
        {
            table.Fail(nameof(SalvagePanelAddItem), error);
            return 0;
        }
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static int SalvagePanelExecute()
    {
        RynthApiTable? table = _current;
        if (table is null)
            return 0;
        try { return table.Salvage.Execute() ? 1 : 0; }
        catch (Exception error)
        {
            table.Fail(nameof(SalvagePanelExecute), error);
            return 0;
        }
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static int SalvageDirect(uint toolId, uint* itemIds, int count)
    {
        RynthApiTable? table = _current;
        if (table is null || toolId == 0 || itemIds is null || count <= 0 || count > RynthSalvagePanel.MaxItems)
            return 0;
        try
        {
            var ids = new uint[count];
            for (int i = 0; i < count; i++)
            {
                if (itemIds[i] == 0 || itemIds[i] == toolId)
                    return 0;
                ids[i] = itemIds[i];
            }
            return table._view.Salvage(toolId, ids) ? 1 : 0;
        }
        catch (Exception error)
        {
            table.Fail(nameof(SalvageDirect), error);
            return 0;
        }
    }
}
