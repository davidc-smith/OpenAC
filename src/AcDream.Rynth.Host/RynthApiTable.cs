using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using AcDream.Rynth.EngineUi;
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
    public const uint ApiVersion = 88;

    private static RynthApiTable? _current;

    private readonly IRynthGameView _view;

    /// <summary>The held movement keys RynthCore's SetMotion and jump slots keep.</summary>
    internal RynthMotionHolds Holds { get; }

    /// <summary>The Nav3D buffer the table's Nav3D slots fill and the host commits each tick.</summary>
    internal RynthNav3D Nav3D { get; }

    /// <summary>RynthCore's salvage panel, kept by the host.</summary>
    internal RynthSalvagePanel Salvage { get; }

    /// <summary>RynthCore's vendor buy/sell requests and the vendor list generation.</summary>
    internal RynthVendorTrades Vendor { get; }

    /// <summary>The host's loaded plugins, for GetPluginInterface. Set by the host before the first Init.</summary>
    internal IReadOnlyList<RynthPlugin> Plugins { get; set; } = [];
    private readonly Action<string> _log;
    private readonly bool _scriptWindows;

    // The string slots' return buffers, one set per calling thread (the frame thread and the
    // pump both call them): a pointer handed to a plugin stays valid until that same thread's
    // next call to the same slot, as on one thread before. The table keeps every thread's set
    // so Dispose frees them all.
    [ThreadStatic]
    private static StringBuffers? t_buffers;
    private readonly List<StringBuffers> _allBuffers = [];

    public RynthApiTable(IRynthGameView view, Action<string> log, bool scriptWindows = false,
        AcDream.Plugin.Abstractions.IPluginWorldTriangleLayer? nav3DLayer = null)
    {
        ArgumentNullException.ThrowIfNull(view);
        ArgumentNullException.ThrowIfNull(log);
        if (Interlocked.CompareExchange(ref _current, this, null) is not null)
            throw new InvalidOperationException("Only one RynthApiTable can be live at a time.");

        _view = view;
        Holds = new RynthMotionHolds(view);
        Nav3D = new RynthNav3D(view, nav3DLayer);
        Salvage = new RynthSalvagePanel(view);
        Vendor = new RynthVendorTrades(view);
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
        BindStateSlots();
        BindActionSlots();
        BindCombatSlots();
        BindMovementSlots();
        BindNav3DSlots();
        BindLootSlots();
        BindSalvageSlots();
        BindVendorSlots();
        BindTradeSlots();
        BindScreenSlots();
        BindInterfaceSlots();
        if (scriptWindows)
        {
            Native->UiSubmitFn = (IntPtr)(delegate* unmanaged[Cdecl]<byte*, int, int>)&UiSubmit;
            Native->UiPollEventsFn = (IntPtr)(delegate* unmanaged[Cdecl]<byte*, int, int*, int>)&UiPollEvents;
            Native->UiGetInfoFn = (IntPtr)(delegate* unmanaged[Cdecl]<RynthCore.Engine.Plugins.UiInfoNative*, int>)&UiGetInfo;
            EngineSettings.EnableImGuiBackend = true;
        }
    }

    /// <summary>The frame action queue; set when the pump starts.</summary>
    internal RynthActionQueue? Actions
    {
        get => Volatile.Read(ref _actions);
        set => Volatile.Write(ref _actions, value);
    }

    private RynthActionQueue? _actions;

    /// <summary>
    /// Runs <paramref name="work"/> now on the frame thread (or before the frame thread is
    /// latched); elsewhere queues it for the frame. The held keys (<see cref="Holds"/>) live on
    /// the frame side, so every slot that changes them comes through here. False when refused
    /// or there is no queue.
    /// </summary>
    internal bool OnFrame(RynthActionKind kind, string label, Func<bool> work) =>
        !RynthFrameThread.Latched || RynthFrameThread.IsCurrent
            ? work()
            : Actions?.TryEnqueue(kind, label, work) ?? false;

    /// <summary>The table, in native memory, for as long as this object lives.</summary>
    public RynthCoreApiNative* Native { get; private set; }

    public void Dispose()
    {
        if (Native is null)
            return;
        Nav3D.Dispose();
        Vendor.Dispose();
        NativeMemory.Free(Native);
        Native = null;
        lock (_allBuffers)
        {
            foreach (StringBuffers buffers in _allBuffers)
                buffers.Free();
            _allBuffers.Clear();
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
    // frees, so the host keeps one buffer per function and calling thread, and replaces it
    // on that thread's next call.
    private static IntPtr Replace(ref IntPtr buffer, string text)
    {
        IntPtr previous = buffer;
        buffer = Marshal.StringToHGlobalAnsi(text);
        if (previous != IntPtr.Zero)
            Marshal.FreeHGlobal(previous);
        return buffer;
    }

    /// <summary>This thread's return buffers for this table, made on the thread's first string call.</summary>
    private StringBuffers Buffers
    {
        get
        {
            StringBuffers? buffers = t_buffers;
            if (buffers is not null && ReferenceEquals(buffers.Owner, this))
                return buffers;
            // A set left by an earlier table on this thread was freed by that table's Dispose.
            buffers = new StringBuffers(this);
            lock (_allBuffers)
                _allBuffers.Add(buffers);
            t_buffers = buffers;
            return buffers;
        }
    }

    /// <summary>One thread's string-slot buffers. Only its thread replaces them; Dispose frees them.</summary>
    private sealed class StringBuffers(RynthApiTable owner)
    {
        public RynthApiTable Owner { get; } = owner;
        public IntPtr Name;
        public IntPtr Account;
        public IntPtr World;
        public IntPtr String;

        public void Free()
        {
            FreeOne(ref Name);
            FreeOne(ref Account);
            FreeOne(ref World);
            FreeOne(ref String);
        }

        private static void FreeOne(ref IntPtr buffer)
        {
            if (buffer != IntPtr.Zero)
                Marshal.FreeHGlobal(buffer);
            buffer = IntPtr.Zero;
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
