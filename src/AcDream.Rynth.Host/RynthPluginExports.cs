using System.Reflection;
using System.Runtime.InteropServices;

namespace AcDream.Rynth.Host;

/// <summary>
/// A RynthCore plugin's exports: each static method marked
/// <c>[UnmanagedCallersOnly(EntryPoint = "...")]</c>, by entry point. RynthCore
/// finds them in the plugin's native DLL; here the plugin is managed code, so
/// they are found by reflection and called through the same function pointers.
/// </summary>
public sealed class RynthPluginExports
{
    private readonly Dictionary<string, IntPtr> _byEntryPoint;

    private RynthPluginExports(Dictionary<string, IntPtr> byEntryPoint) =>
        _byEntryPoint = byEntryPoint;

    /// <summary>The entry point names found.</summary>
    public IReadOnlyCollection<string> Names => _byEntryPoint.Keys;

    /// <summary>Reads the exports of a plugin's exports class.</summary>
    public static RynthPluginExports FromType(Type exportsType)
    {
        ArgumentNullException.ThrowIfNull(exportsType);
        var byEntryPoint = new Dictionary<string, IntPtr>(StringComparer.Ordinal);
        foreach (MethodInfo method in exportsType.GetMethods(
                     BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic))
        {
            if (method.GetCustomAttribute<UnmanagedCallersOnlyAttribute>()?.EntryPoint
                is { Length: > 0 } entryPoint)
            {
                byEntryPoint[entryPoint] = method.MethodHandle.GetFunctionPointer();
            }
        }

        return new RynthPluginExports(byEntryPoint);
    }

    /// <summary>The function pointer of an export, if the plugin has it.</summary>
    public bool TryGet(string entryPoint, out IntPtr pointer) =>
        _byEntryPoint.TryGetValue(entryPoint, out pointer);
}
