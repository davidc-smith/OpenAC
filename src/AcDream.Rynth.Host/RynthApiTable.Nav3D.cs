using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace AcDream.Rynth.Host;

// The table's Nav3D slots (RynthAi sub-project #4): they only fill the tick's buffer.
public sealed unsafe partial class RynthApiTable
{
    private void BindNav3DSlots()
    {
        Native->Nav3DClearFn = (IntPtr)(delegate* unmanaged[Cdecl]<void>)&Nav3DClear;
        Native->Nav3DAddRingFn = (IntPtr)(delegate* unmanaged[Cdecl]<float, float, float, float, float, uint, void>)&Nav3DAddRing;
        Native->Nav3DAddRingExFn = (IntPtr)(delegate* unmanaged[Cdecl]<float, float, float, float, float, float, uint, void>)&Nav3DAddRingEx;
        Native->Nav3DAddLineFn = (IntPtr)(delegate* unmanaged[Cdecl]<float, float, float, float, float, float, float, uint, void>)&Nav3DAddLine;
        Native->Nav3DAddTriangleFn = (IntPtr)(delegate* unmanaged[Cdecl]<float, float, float, float, float, float, float, float, float, uint, void>)&Nav3DAddTriangle;
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static void Nav3DClear()
    {
        RynthApiTable? table = _current;
        if (table is null)
            return;
        try { table.Nav3D.Clear(); }
        catch (Exception error) { table.Fail(nameof(Nav3DClear), error); }
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static void Nav3DAddRing(float x, float y, float z, float radius, float thickness, uint argb)
    {
        RynthApiTable? table = _current;
        if (table is null)
            return;
        try { table.Nav3D.AddRing(x, y, z, radius, thickness, argb); }
        catch (Exception error) { table.Fail(nameof(Nav3DAddRing), error); }
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static void Nav3DAddRingEx(float x, float y, float z, float radius, float thickness, float height, uint argb)
    {
        RynthApiTable? table = _current;
        if (table is null)
            return;
        try { table.Nav3D.AddRingEx(x, y, z, radius, thickness, height, argb); }
        catch (Exception error) { table.Fail(nameof(Nav3DAddRingEx), error); }
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static void Nav3DAddLine(float x1, float y1, float z1, float x2, float y2, float z2, float thickness, uint argb)
    {
        RynthApiTable? table = _current;
        if (table is null)
            return;
        try { table.Nav3D.AddLine(x1, y1, z1, x2, y2, z2, thickness, argb); }
        catch (Exception error) { table.Fail(nameof(Nav3DAddLine), error); }
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static void Nav3DAddTriangle(float x1, float y1, float z1, float x2, float y2, float z2, float x3, float y3, float z3, uint argb)
    {
        RynthApiTable? table = _current;
        if (table is null)
            return;
        try { table.Nav3D.AddTriangle(x1, y1, z1, x2, y2, z2, x3, y3, z3, argb); }
        catch (Exception error) { table.Fail(nameof(Nav3DAddTriangle), error); }
    }
}
