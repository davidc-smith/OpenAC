using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace AcDream.Rynth.Host;

// The table's loot, give and container-count slots (RynthAi sub-project #5).
// Counts and palettes answer -1 for an unknown object, as RynthCore does.
public sealed unsafe partial class RynthApiTable
{
    private void BindLootSlots()
    {
        Native->MoveItemExternalFn = (IntPtr)(delegate* unmanaged[Cdecl]<uint, uint, int, int>)&MoveItemExternal;
        Native->GiveObjectToFn = (IntPtr)(delegate* unmanaged[Cdecl]<uint, uint, int, int>)&GiveObjectTo;
        Native->CloseContainerFn = (IntPtr)(delegate* unmanaged[Cdecl]<uint, int>)&CloseContainer;
        Native->GetObjectPalettesFn = (IntPtr)(delegate* unmanaged[Cdecl]<uint, uint*, uint*, int, int>)&GetObjectPalettes;
        Native->GetNumContainedItemsFn = (IntPtr)(delegate* unmanaged[Cdecl]<uint, int>)&GetNumContainedItems;
        Native->GetNumContainedContainersFn = (IntPtr)(delegate* unmanaged[Cdecl]<uint, int>)&GetNumContainedContainers;
    }

    // RynthAi's general move: loot, drop, pack move or give; the view picks which from the objects.
    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static int MoveItemExternal(uint objectId, uint targetId, int amount)
    {
        RynthApiTable? table = _current;
        if (table is null || objectId == 0 || amount < 0)
            return 0;
        try
        {
            return table._view.MoveItemExternal(objectId, targetId, (uint)amount) ? 1 : 0;
        }
        catch (Exception error)
        {
            table.Fail(nameof(MoveItemExternal), error);
            return 0;
        }
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static int GiveObjectTo(uint objectId, uint targetId, int amount)
    {
        RynthApiTable? table = _current;
        if (table is null || objectId == 0 || targetId == 0 || amount < 0)
            return 0;
        try { return table._view.GiveItem(objectId, targetId, (uint)amount) ? 1 : 0; }
        catch (Exception error)
        {
            table.Fail(nameof(GiveObjectTo), error);
            return 0;
        }
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static int CloseContainer(uint containerId)
    {
        RynthApiTable? table = _current;
        if (table is null || containerId == 0)
            return 0;
        try { return table._view.CloseContainer(containerId) ? 1 : 0; }
        catch (Exception error)
        {
            table.Fail(nameof(CloseContainer), error);
            return 0;
        }
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static int GetObjectPalettes(uint objectId, uint* subIds, uint* offsets, int maxCount)
    {
        RynthApiTable? table = _current;
        if (table is null)
            return -1;
        try
        {
            if (table._view.GetPalettes(objectId) is not { } palettes)
                return -1;
            if (subIds is not null && offsets is not null)
            {
                int n = Math.Min(Math.Max(maxCount, 0), palettes.Count);
                for (int i = 0; i < n; i++)
                {
                    subIds[i] = palettes[i].SubPaletteId;
                    offsets[i] = palettes[i].Offset;
                }
            }
            return palettes.Count;
        }
        catch (Exception error)
        {
            table.Fail(nameof(GetObjectPalettes), error);
            return -1;
        }
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static int GetNumContainedItems(uint containerId)
    {
        RynthApiTable? table = _current;
        if (table is null)
            return -1;
        try { return table._view.TryCountContents(containerId, out int items, out _) ? items : -1; }
        catch (Exception error)
        {
            table.Fail(nameof(GetNumContainedItems), error);
            return -1;
        }
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static int GetNumContainedContainers(uint containerId)
    {
        RynthApiTable? table = _current;
        if (table is null)
            return -1;
        try { return table._view.TryCountContents(containerId, out _, out int containers) ? containers : -1; }
        catch (Exception error)
        {
            table.Fail(nameof(GetNumContainedContainers), error);
            return -1;
        }
    }
}
