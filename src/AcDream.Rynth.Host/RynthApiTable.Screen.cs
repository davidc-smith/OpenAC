using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace AcDream.Rynth.Host;

// The table's screen slots (RynthAi sub-project #6): radar walls and the 2D nav path.
public sealed unsafe partial class RynthApiTable
{
    private void BindScreenSlots()
    {
        Native->WorldToScreenFn = (IntPtr)(delegate* unmanaged[Cdecl]<float, float, float, float*, float*, int>)&WorldToScreen;
        Native->GetViewportSizeFn = (IntPtr)(delegate* unmanaged[Cdecl]<uint*, uint*, int>)&GetViewportSize;
        Native->GetRadarRectFn = (IntPtr)(delegate* unmanaged[Cdecl]<int*, int*, int*, int*, int>)&GetRadarRect;
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static int WorldToScreen(float x, float y, float z, float* screenX, float* screenY)
    {
        RynthApiTable? table = _current;
        if (table is null || screenX is null || screenY is null)
            return 0;
        try
        {
            if (!table._view.TryWorldToScreen(x, y, z, out float sx, out float sy))
                return 0;
            *screenX = sx;
            *screenY = sy;
            return 1;
        }
        catch (Exception error)
        {
            table.Fail(nameof(WorldToScreen), error);
            return 0;
        }
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static int GetViewportSize(uint* width, uint* height)
    {
        RynthApiTable? table = _current;
        if (table is null || width is null || height is null)
            return 0;
        try
        {
            if (!table._view.TryGetViewportSize(out uint w, out uint h))
                return 0;
            *width = w;
            *height = h;
            return 1;
        }
        catch (Exception error)
        {
            table.Fail(nameof(GetViewportSize), error);
            return 0;
        }
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static int GetRadarRect(int* x0, int* y0, int* x1, int* y1)
    {
        RynthApiTable? table = _current;
        if (table is null || x0 is null || y0 is null || x1 is null || y1 is null)
            return 0;
        try
        {
            if (!table._view.TryGetRadarRect(out int left, out int top, out int right, out int bottom))
                return 0;
            *x0 = left;
            *y0 = top;
            *x1 = right;
            *y1 = bottom;
            return 1;
        }
        catch (Exception error)
        {
            table.Fail(nameof(GetRadarRect), error);
            return 0;
        }
    }
}
