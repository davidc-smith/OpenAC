using System.Runtime.InteropServices;
using RynthCore.PluginSdk;

namespace AcDream.Rynth.Host;

/// <summary>
/// One loaded RynthCore plugin: typed calls into its exports, with the
/// signatures RynthCore's PluginManager uses. A missing export is skipped, as
/// RynthCore does. Exceptions inside a plugin never reach here: the plugin's
/// RynthPluginRuntime catches and logs them inside each export.
/// </summary>
public sealed unsafe class RynthPlugin
{
    private readonly RynthPluginExports _exports;

    public RynthPlugin(string name, RynthPluginExports exports)
    {
        Name = name;
        Owner.DisplayName = name;
        _exports = exports;
    }

    public string Name { get; }

    /// <summary>This plugin's identity in RynthCore's script-window registry.</summary>
    internal RynthCore.Engine.Plugins.LoadedPlugin Owner { get; } = new();

    /// <summary>Initialised and not yet shut down.</summary>
    public bool IsActive { get; private set; }

    /// <summary>Calls RynthPluginInit; 0 is success.</summary>
    public int Initialize(RynthCoreApiNative* api)
    {
        if (!_exports.TryGet("RynthPluginInit", out IntPtr init))
            return -1;
        int result;
        using (RynthDispatch.Enter(this))
            result = ((delegate* unmanaged[Cdecl]<RynthCoreApiNative*, int>)init)(api);
        IsActive = result == 0;
        return result;
    }

    public void Tick() => CallVoid("RynthPluginTick");

    public void OnLoginComplete() => CallVoid("RynthPluginOnLoginComplete");

    public void OnLogout() => CallVoid("RynthPluginOnLogout");

    public void OnCreateObject(uint objectId) => CallObject("RynthPluginOnCreateObject", objectId);

    public void OnDeleteObject(uint objectId) => CallObject("RynthPluginOnDeleteObject", objectId);

    /// <summary>
    /// A target's health. The server's health updates carry a ratio only, so
    /// current and max are 0 unless the caller knows them (the player's own death).
    /// </summary>
    public void OnUpdateHealth(uint targetId, float healthRatio, uint currentHealth = 0, uint maxHealth = 0)
    {
        if (IsActive && _exports.TryGet("RynthPluginOnUpdateHealth", out IntPtr export))
        {
            using RynthDispatch.Scope scope = RynthDispatch.Enter(this);
            ((delegate* unmanaged[Cdecl]<uint, float, uint, uint, void>)export)(
                targetId, healthRatio, currentHealth, maxHealth);
        }
    }

    /// <summary>The eat flag is passed but ignored (the client shows the line regardless).</summary>
    public void OnChatWindowText(string text, int chatType)
    {
        if (!IsActive || !_exports.TryGet("RynthPluginOnChatWindowText", out IntPtr export))
            return;
        int eat = 0;
        using RynthDispatch.Scope scope = RynthDispatch.Enter(this);
        fixed (char* textUtf16 = text)
        {
            ((delegate* unmanaged[Cdecl]<IntPtr, int, IntPtr, void>)export)(
                (IntPtr)textUtf16, chatType, (IntPtr)(&eat));
        }
    }

    /// <summary>
    /// Text typed in the chat bar, offered to the plugin before the client
    /// handles it. Returns true when the plugin ate it. Like RynthCore, this
    /// runs outside the dispatch scope.
    /// </summary>
    public bool OnChatBarEnter(string text)
    {
        if (!IsActive || !_exports.TryGet("RynthPluginOnChatBarEnter", out IntPtr export))
            return false;
        int eat = 0;
        fixed (char* textUtf16 = text)
        {
            ((delegate* unmanaged[Cdecl]<IntPtr, IntPtr, void>)export)((IntPtr)textUtf16, (IntPtr)(&eat));
        }

        return eat != 0;
    }

    public void OnKillNotification(string deathMessage)
    {
        if (!IsActive || !_exports.TryGet("RynthPluginOnKillNotification", out IntPtr export))
            return;
        using RynthDispatch.Scope scope = RynthDispatch.Enter(this);
        fixed (char* textUtf16 = deathMessage)
        {
            ((delegate* unmanaged[Cdecl]<IntPtr, void>)export)((IntPtr)textUtf16);
        }
    }

    /// <summary>Calls an export that returns an ANSI string, such as a snapshot.</summary>
    public string? CallStringExport(string entryPoint)
    {
        if (!IsActive || !_exports.TryGet(entryPoint, out IntPtr export))
            return null;
        using RynthDispatch.Scope scope = RynthDispatch.Enter(this);
        IntPtr result = ((delegate* unmanaged[Cdecl]<IntPtr>)export)();
        return result == IntPtr.Zero ? null : Marshal.PtrToStringAnsi(result);
    }

    public void Shutdown()
    {
        if (!IsActive)
            return;
        CallVoid("RynthPluginShutdown");
        IsActive = false;
    }

    private void CallVoid(string entryPoint)
    {
        if (IsActive && _exports.TryGet(entryPoint, out IntPtr export))
        {
            using RynthDispatch.Scope scope = RynthDispatch.Enter(this);
            ((delegate* unmanaged[Cdecl]<void>)export)();
        }
    }

    private void CallObject(string entryPoint, uint objectId)
    {
        if (IsActive && _exports.TryGet(entryPoint, out IntPtr export))
        {
            using RynthDispatch.Scope scope = RynthDispatch.Enter(this);
            ((delegate* unmanaged[Cdecl]<uint, void>)export)(objectId);
        }
    }
}
