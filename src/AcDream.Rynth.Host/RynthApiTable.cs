using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
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
    private IntPtr _nameBuffer;

    public RynthApiTable(IRynthGameView view, Action<string> log)
    {
        ArgumentNullException.ThrowIfNull(view);
        ArgumentNullException.ThrowIfNull(log);
        if (Interlocked.CompareExchange(ref _current, this, null) is not null)
            throw new InvalidOperationException("Only one RynthApiTable can be live at a time.");

        _view = view;
        _log = log;
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
    // frees, so the host keeps one buffer and replaces it on the next call.
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
            IntPtr previous = table._nameBuffer;
            table._nameBuffer = Marshal.StringToHGlobalAnsi(name);
            if (previous != IntPtr.Zero)
                Marshal.FreeHGlobal(previous);
            return table._nameBuffer;
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
}
