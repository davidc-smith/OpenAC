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
public sealed unsafe partial class RynthApiTable : IDisposable
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
