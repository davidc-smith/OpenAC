using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using RynthCore.Engine.Plugins;
using RynthCore.Engine.UI.ScriptWindows;
using RynthCore.PluginSdk;

namespace AcDream.Rynth.Host;

/// <summary>
/// The RynthCore engine API table handed to every plugin's RynthPluginInit.
/// Only the slots this host implements are set; the rest stay zero, which a
/// plugin's <c>Has*</c> checks read as "not available".
/// </summary>
/// <remarks>
/// The table functions are static (a function pointer carries no instance),
/// so they reach the live table through <see cref="_current"/>: one table per
/// process. An exception must never leave them, because an exception cannot
/// cross an [UnmanagedCallersOnly] boundary (the runtime ends the process), so
/// each one catches everything, logs, and returns 0.
/// </remarks>
public sealed unsafe class RynthApiTable : IDisposable
{
    /// <summary>The RynthCore engine API version this host implements.</summary>
    public const uint ApiVersion = 86;

    private static RynthApiTable? _current;

    private readonly IRynthGameView _view;
    private readonly Action<string> _log;
    private readonly bool _scriptWindows;
    private IntPtr _nameBuffer;
    private IntPtr _accountBuffer;
    private IntPtr _worldBuffer;

    public RynthApiTable(IRynthGameView view, Action<string> log, bool scriptWindows = false)
    {
        ArgumentNullException.ThrowIfNull(view);
        ArgumentNullException.ThrowIfNull(log);
        if (Interlocked.CompareExchange(ref _current, this, null) is not null)
            throw new InvalidOperationException("Only one RynthApiTable can be live at a time.");

        _view = view;
        _log = log;
        _scriptWindows = scriptWindows;
        Native = (RynthCoreApiNative*)NativeMemory.AllocZeroed((nuint)sizeof(RynthCoreApiNative));
        Native->Version = ApiVersion;
        Native->LogFn = (IntPtr)(delegate* unmanaged[Cdecl]<IntPtr, void>)&Log;
        Native->WriteToChatFn = (IntPtr)(delegate* unmanaged[Cdecl]<IntPtr, int, int>)&WriteToChat;
        Native->GetPlayerIdFn = (IntPtr)(delegate* unmanaged[Cdecl]<uint>)&GetPlayerId;
        Native->GetObjectNameFn = (IntPtr)(delegate* unmanaged[Cdecl]<uint, IntPtr>)&GetObjectName;
        Native->GetObjectQuadPropertyFn =
            (IntPtr)(delegate* unmanaged[Cdecl]<uint, uint, long*, int>)&GetObjectQuadProperty;
        Native->InvokeChatParserFn = (IntPtr)(delegate* unmanaged[Cdecl]<IntPtr, int>)&InvokeChatParser;
        Native->GetSelectedItemIdFn = (IntPtr)(delegate* unmanaged[Cdecl]<uint>)&GetSelectedItemId;
        Native->GetObjectIntPropertyFn = (IntPtr)(delegate* unmanaged[Cdecl]<uint, uint, int*, int>)&GetObjectIntProperty;
        Native->GetObjectDataIdPropertyFn = (IntPtr)(delegate* unmanaged[Cdecl]<uint, uint, uint*, int>)&GetObjectDataIdProperty;
        Native->GetObjectInstanceIdPropertyFn = (IntPtr)(delegate* unmanaged[Cdecl]<uint, uint, uint*, int>)&GetObjectInstanceIdProperty;
        Native->GetContainerContentsFn = (IntPtr)(delegate* unmanaged[Cdecl]<uint, uint*, int, int>)&GetContainerContents;
        Native->GetObjectOwnershipInfoFn = (IntPtr)(delegate* unmanaged[Cdecl]<uint, uint*, uint*, uint*, int>)&GetObjectOwnershipInfo;
        Native->GetObjectWielderInfoFn = (IntPtr)(delegate* unmanaged[Cdecl]<uint, uint*, uint*, int>)&GetObjectWielderInfo;
        Native->GetItemTypeFn = (IntPtr)(delegate* unmanaged[Cdecl]<uint, uint*, int>)&GetItemType;
        Native->GetObjectWcidFn = (IntPtr)(delegate* unmanaged[Cdecl]<uint, uint>)&GetObjectWcid;
        Native->GetObjectBitfieldFn = (IntPtr)(delegate* unmanaged[Cdecl]<uint, uint>)&GetObjectBitfield;
        Native->HasAppraisalDataFn = (IntPtr)(delegate* unmanaged[Cdecl]<uint, int>)&HasAppraisalData;
        Native->GetObjectSpellIdsFn = (IntPtr)(delegate* unmanaged[Cdecl]<uint, uint*, int, int>)&GetObjectSpellIds;
        Native->GetObjectPositionFn = (IntPtr)(delegate* unmanaged[Cdecl]<uint, uint*, float*, float*, float*, int>)&GetObjectPosition;
        Native->GetCurCoordsFn = (IntPtr)(delegate* unmanaged[Cdecl]<double*, double*, int>)&GetCurCoords;
        Native->GetAccountNameFn = (IntPtr)(delegate* unmanaged[Cdecl]<IntPtr>)&GetAccountName;
        Native->GetWorldNameFn = (IntPtr)(delegate* unmanaged[Cdecl]<IntPtr>)&GetWorldName;
        if (scriptWindows)
        {
            Native->UiSubmitFn = (IntPtr)(delegate* unmanaged[Cdecl]<byte*, int, int>)&UiSubmit;
            Native->UiPollEventsFn = (IntPtr)(delegate* unmanaged[Cdecl]<byte*, int, int*, int>)&UiPollEvents;
            Native->UiGetInfoFn = (IntPtr)(delegate* unmanaged[Cdecl]<RynthCore.Engine.Plugins.UiInfoNative*, int>)&UiGetInfo;
            EngineSettings.EnableImGuiBackend = true;
        }
    }

    /// <summary>The table, in native memory, for as long as this object lives.</summary>
    public RynthCoreApiNative* Native { get; private set; }

    public void Dispose()
    {
        if (Native is null)
            return;
        NativeMemory.Free(Native);
        Native = null;
        if (_nameBuffer != IntPtr.Zero)
        {
            Marshal.FreeHGlobal(_nameBuffer);
            _nameBuffer = IntPtr.Zero;
        }

        if (_accountBuffer != IntPtr.Zero)
        {
            Marshal.FreeHGlobal(_accountBuffer);
            _accountBuffer = IntPtr.Zero;
        }

        if (_worldBuffer != IntPtr.Zero)
        {
            Marshal.FreeHGlobal(_worldBuffer);
            _worldBuffer = IntPtr.Zero;
        }

        if (_scriptWindows)
            EngineSettings.EnableImGuiBackend = false;
        Interlocked.CompareExchange(ref _current, null, this);
    }

    private void Fail(string function, Exception error)
    {
        try { _log($"{function} threw {error.GetType().Name}: {error.Message}"); }
        catch { }
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static void Log(IntPtr message)
    {
        RynthApiTable? table = _current;
        if (table is null || message == IntPtr.Zero)
            return;
        try { table._log(Marshal.PtrToStringAnsi(message) ?? string.Empty); }
        catch { }
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static int WriteToChat(IntPtr textUtf16, int chatType)
    {
        RynthApiTable? table = _current;
        if (table is null || textUtf16 == IntPtr.Zero)
            return 0;
        try
        {
            table._view.WriteChatLine(Marshal.PtrToStringUni(textUtf16) ?? string.Empty, chatType);
            return 1;
        }
        catch (Exception error)
        {
            table.Fail(nameof(WriteToChat), error);
            return 0;
        }
    }

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

    // RynthCore hands back a pointer the plugin reads at once (PtrToStringAnsi) and never
    // frees, so the host keeps one buffer per function and replaces it on the next call.
    private static IntPtr Replace(ref IntPtr buffer, string text)
    {
        IntPtr previous = buffer;
        buffer = Marshal.StringToHGlobalAnsi(text);
        if (previous != IntPtr.Zero)
            Marshal.FreeHGlobal(previous);
        return buffer;
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

    // RynthCore: runs the text as if typed (ClientHelperHooks.InvokeParser); 1 = handled.
    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static int InvokeChatParser(IntPtr textUtf16)
    {
        RynthApiTable? table = _current;
        if (table is null || textUtf16 == IntPtr.Zero)
            return 0;
        try
        {
            string text = Marshal.PtrToStringUni(textUtf16) ?? string.Empty;
            return text.Trim().Length > 0 && table._view.SubmitChatCommand(text.Trim()) ? 1 : 0;
        }
        catch (Exception error)
        {
            table.Fail(nameof(InvokeChatParser), error);
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

    // RynthCore return codes: 0 ok, -1 malformed or threw, -2 not inside a plugin call,
    // -3 over a limit, -4 unsupported format (the last three from the registry).
    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static int UiSubmit(byte* data, int length)
    {
        RynthApiTable? table = _current;
        if (table is null)
            return -2;
        try
        {
            RynthPlugin? owner = RynthDispatch.Current;
            return owner is null ? -2 : ScriptWindowRegistry.Submit(owner.Owner, data, length);
        }
        catch (Exception error)
        {
            table.Fail(nameof(UiSubmit), error);
            return -1;
        }
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static int UiPollEvents(byte* buffer, int capacity, int* remaining)
    {
        RynthApiTable? table = _current;
        if (remaining != null)
            *remaining = 0;
        if (table is null)
            return 0;
        try
        {
            RynthPlugin? owner = RynthDispatch.Current;
            return owner is null ? -2 : ScriptWindowRegistry.PollEvents(owner.Owner, buffer, capacity, remaining);
        }
        catch (Exception error)
        {
            table.Fail(nameof(UiPollEvents), error);
            return 0;
        }
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static int UiGetInfo(RynthCore.Engine.Plugins.UiInfoNative* info)
    {
        RynthApiTable? table = _current;
        if (table is null || info is null)
            return 0;
        try { return ScriptWindowRegistry.GetInfo(info); }
        catch (Exception error)
        {
            table.Fail(nameof(UiGetInfo), error);
            return 0;
        }
    }
}
