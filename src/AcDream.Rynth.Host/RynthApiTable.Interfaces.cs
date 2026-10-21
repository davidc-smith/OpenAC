using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace AcDream.Rynth.Host;

// GetPluginInterface (API 68): one plugin's typed table for another, as RynthCore's
// PluginManager.GetPluginInterfaceAction does it. RynthAi's outdoor detours ask RynthNav for
// "RynthNav.Path"; RynthLua asks RynthAi for "RynthAi.Script".
public sealed unsafe partial class RynthApiTable
{
    private readonly HashSet<string> _interfaceFailures = new(StringComparer.Ordinal);

    private void BindInterfaceSlots() =>
        Native->GetPluginInterfaceFn = (IntPtr)(delegate* unmanaged[Cdecl]<IntPtr, IntPtr, uint, IntPtr>)&GetPluginInterface;

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static IntPtr GetPluginInterface(IntPtr pluginNameAnsi, IntPtr interfaceNameAnsi, uint version)
    {
        RynthApiTable? table = _current;
        if (table is null || pluginNameAnsi == IntPtr.Zero || interfaceNameAnsi == IntPtr.Zero)
            return IntPtr.Zero;
        string name = string.Empty;
        string iface = string.Empty;
        try
        {
            name = Marshal.PtrToStringAnsi(pluginNameAnsi) ?? string.Empty;
            iface = Marshal.PtrToStringAnsi(interfaceNameAnsi) ?? string.Empty;
            if (name.Length == 0 || iface.Length == 0)
                return IntPtr.Zero;
            foreach (RynthPlugin plugin in RynthPluginLookup.Candidates(table.Plugins, name))
            {
                if (plugin.HasExport("RynthPluginQueryInterface"))
                    return plugin.QueryInterface(interfaceNameAnsi, version);
            }

            return IntPtr.Zero;
        }
        catch (Exception error)
        {
            // Asked every tick by some callers: say it once per plugin/interface pair.
            bool first;
            lock (table._interfaceFailures)
                first = table._interfaceFailures.Add($"{name}/{iface}");
            if (first)
                table.Fail($"{nameof(GetPluginInterface)}({name}, {iface})", error);
            return IntPtr.Zero;
        }
    }
}
