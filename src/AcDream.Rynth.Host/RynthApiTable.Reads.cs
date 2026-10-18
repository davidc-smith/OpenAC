using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace AcDream.Rynth.Host;

// The table's object and player reads (see RynthApiTable for the rules every function follows).
public sealed unsafe partial class RynthApiTable
{
    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static uint GetPlayerId()
    {
        RynthApiTable? table = _current;
        if (table is null)
            return 0;
        try { return table._view.PlayerId; }
        catch (Exception error)
        {
            table.Fail(nameof(GetPlayerId), error);
            return 0;
        }
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static IntPtr GetObjectName(uint objectId)
    {
        RynthApiTable? table = _current;
        if (table is null)
            return IntPtr.Zero;
        try
        {
            if (!table._view.TryGetObjectName(objectId, out string name))
                return IntPtr.Zero;
            return Replace(ref table._nameBuffer, name);
        }
        catch (Exception error)
        {
            table.Fail(nameof(GetObjectName), error);
            return IntPtr.Zero;
        }
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static int GetObjectQuadProperty(uint objectId, uint propertyId, long* value)
    {
        RynthApiTable? table = _current;
        if (table is null || value is null)
            return 0;
        try
        {
            if (!table._view.TryGetInt64Property(objectId, propertyId, out long found))
                return 0;
            *value = found;
            return 1;
        }
        catch (Exception error)
        {
            table.Fail(nameof(GetObjectQuadProperty), error);
            return 0;
        }
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static uint GetSelectedItemId()
    {
        RynthApiTable? table = _current;
        if (table is null)
            return 0;
        try { return table._view.SelectedObjectId; }
        catch (Exception error)
        {
            table.Fail(nameof(GetSelectedItemId), error);
            return 0;
        }
    }

    private bool TryObject(uint objectId, out RynthObjectInfo info) => _view.TryGetObject(objectId, out info);

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static int GetObjectIntProperty(uint objectId, uint stat, int* value)
    {
        RynthApiTable? table = _current;
        if (table is null || value is null)
            return 0;
        try
        {
            if (!table.TryObject(objectId, out RynthObjectInfo info)
                || !RynthObjectProperties.TryGetInt(info, stat, out int found))
                return 0;
            *value = found;
            return 1;
        }
        catch (Exception error)
        {
            table.Fail(nameof(GetObjectIntProperty), error);
            return 0;
        }
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static int GetObjectDataIdProperty(uint objectId, uint stat, uint* value)
    {
        RynthApiTable? table = _current;
        if (table is null || value is null)
            return 0;
        try
        {
            if (!table.TryObject(objectId, out RynthObjectInfo info)
                || !RynthObjectProperties.TryGetDataId(info, stat, out uint found))
                return 0;
            *value = found;
            return 1;
        }
        catch (Exception error)
        {
            table.Fail(nameof(GetObjectDataIdProperty), error);
            return 0;
        }
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static int GetObjectInstanceIdProperty(uint objectId, uint stat, uint* value)
    {
        RynthApiTable? table = _current;
        if (table is null || value is null)
            return 0;
        try
        {
            if (!table.TryObject(objectId, out RynthObjectInfo info)
                || !RynthObjectProperties.TryGetInstanceId(info, stat, out uint found))
                return 0;
            *value = found;
            return 1;
        }
        catch (Exception error)
        {
            table.Fail(nameof(GetObjectInstanceIdProperty), error);
            return 0;
        }
    }

    // RynthCore: copies at most max ids and returns how many there are.
    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static int GetContainerContents(uint containerId, uint* ids, int max)
    {
        RynthApiTable? table = _current;
        if (table is null || ids is null || max <= 0)
            return 0;
        try
        {
            IReadOnlyList<uint> contents = table._view.GetContainerContents(containerId);
            int count = Math.Min(contents.Count, max);
            for (int i = 0; i < count; i++)
                ids[i] = contents[i];
            return contents.Count;
        }
        catch (Exception error)
        {
            table.Fail(nameof(GetContainerContents), error);
            return 0;
        }
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static int GetObjectOwnershipInfo(uint objectId, uint* container, uint* wielder, uint* location)
    {
        RynthApiTable? table = _current;
        if (table is null || container is null || wielder is null || location is null)
            return 0;
        try
        {
            if (!table.TryObject(objectId, out RynthObjectInfo info))
                return 0;
            *container = info.ContainerId;
            *wielder = info.WielderId;
            *location = info.EquipLocation;
            return 1;
        }
        catch (Exception error)
        {
            table.Fail(nameof(GetObjectOwnershipInfo), error);
            return 0;
        }
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static int GetObjectWielderInfo(uint objectId, uint* wielder, uint* location)
    {
        RynthApiTable? table = _current;
        if (table is null || wielder is null || location is null)
            return 0;
        try
        {
            if (!table.TryObject(objectId, out RynthObjectInfo info))
                return 0;
            *wielder = info.WielderId;
            *location = info.EquipLocation;
            return 1;
        }
        catch (Exception error)
        {
            table.Fail(nameof(GetObjectWielderInfo), error);
            return 0;
        }
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static int GetItemType(uint objectId, uint* flags)
    {
        RynthApiTable? table = _current;
        if (table is null || flags is null)
            return 0;
        try
        {
            if (!table.TryObject(objectId, out RynthObjectInfo info))
                return 0;
            *flags = info.ItemType;
            return 1;
        }
        catch (Exception error)
        {
            table.Fail(nameof(GetItemType), error);
            return 0;
        }
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static uint GetObjectWcid(uint objectId)
    {
        RynthApiTable? table = _current;
        if (table is null)
            return 0;
        try { return table.TryObject(objectId, out RynthObjectInfo info) ? info.Wcid : 0; }
        catch (Exception error)
        {
            table.Fail(nameof(GetObjectWcid), error);
            return 0;
        }
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static uint GetObjectBitfield(uint objectId)
    {
        RynthApiTable? table = _current;
        if (table is null)
            return 0;
        try { return table.TryObject(objectId, out RynthObjectInfo info) ? info.Bitfield : 0; }
        catch (Exception error)
        {
            table.Fail(nameof(GetObjectBitfield), error);
            return 0;
        }
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static int HasAppraisalData(uint objectId)
    {
        RynthApiTable? table = _current;
        if (table is null)
            return 0;
        try { return table.TryObject(objectId, out RynthObjectInfo info) && info.Identified ? 1 : 0; }
        catch (Exception error)
        {
            table.Fail(nameof(HasAppraisalData), error);
            return 0;
        }
    }

    // RynthCore: -1 when the object was never identified, else the spell count (may exceed max).
    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static int GetObjectSpellIds(uint objectId, uint* ids, int max)
    {
        RynthApiTable? table = _current;
        if (table is null)
            return -1;
        try
        {
            if (!table.TryObject(objectId, out RynthObjectInfo info) || !info.Identified)
                return -1;
            int count = ids is null ? 0 : Math.Min(info.AppraisedSpellIds.Count, Math.Max(max, 0));
            for (int i = 0; i < count; i++)
                ids[i] = info.AppraisedSpellIds[i];
            return info.AppraisedSpellIds.Count;
        }
        catch (Exception error)
        {
            table.Fail(nameof(GetObjectSpellIds), error);
            return -1;
        }
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static int GetObjectPosition(uint objectId, uint* cellId, float* x, float* y, float* z)
    {
        RynthApiTable? table = _current;
        if (table is null || cellId is null || x is null || y is null || z is null)
            return 0;
        try
        {
            if (!table._view.TryGetObjectPosition(objectId, out uint cell, out Vector3 origin))
                return 0;
            *cellId = cell;
            *x = origin.X;
            *y = origin.Y;
            *z = origin.Z;
            return 1;
        }
        catch (Exception error)
        {
            table.Fail(nameof(GetObjectPosition), error);
            return 0;
        }
    }

    // RynthCore's map coordinates from the player's cell (PlayerPhysicsHooks.TryGetLiveCoords):
    // EW = ((cell >> 24) * 8 + x / 24 - 1019.5) / 10, NS likewise from (cell >> 16) & 0xFF and y.
    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static int GetCurCoords(double* northSouth, double* eastWest)
    {
        RynthApiTable? table = _current;
        if (table is null || northSouth is null || eastWest is null)
            return 0;
        try
        {
            if (!table._view.TryGetObjectPosition(table._view.PlayerId, out uint cell, out Vector3 origin))
                return 0;
            *eastWest = (((cell >> 24) & 0xFF) * 8.0 + origin.X / 24.0 - 1019.5) / 10.0;
            *northSouth = (((cell >> 16) & 0xFF) * 8.0 + origin.Y / 24.0 - 1019.5) / 10.0;
            return 1;
        }
        catch (Exception error)
        {
            table.Fail(nameof(GetCurCoords), error);
            return 0;
        }
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static IntPtr GetAccountName()
    {
        RynthApiTable? table = _current;
        if (table is null)
            return IntPtr.Zero;
        try
        {
            string name = table._view.AccountName;
            return name.Length == 0 ? IntPtr.Zero : Replace(ref table._accountBuffer, name);
        }
        catch (Exception error)
        {
            table.Fail(nameof(GetAccountName), error);
            return IntPtr.Zero;
        }
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static IntPtr GetWorldName()
    {
        RynthApiTable? table = _current;
        if (table is null)
            return IntPtr.Zero;
        try
        {
            string name = table._view.WorldName;
            return name.Length == 0 ? IntPtr.Zero : Replace(ref table._worldBuffer, name);
        }
        catch (Exception error)
        {
            table.Fail(nameof(GetWorldName), error);
            return IntPtr.Zero;
        }
    }
}
