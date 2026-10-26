using Silk.NET.Windowing;

namespace AcDream.App.Rendering;

/// <summary>
/// Runs a window's frame loop and leaves the native window alive when the
/// loop ends.
/// <para>The window's lifetime owner destroys the native window last, after
/// the input context and the graphics surface made from it have let go.
/// Silk's own <c>Run</c> extension destroys the native window as soon as the
/// loop ends; shutdown then released the input context into the freed
/// window (it clears its callbacks there), writing into heap memory that
/// belonged to someone else by then. The loop here is the same frame loop
/// without that destroy. The window is hidden instead, so it still leaves the
/// screen the moment it closes.</para>
/// </summary>
internal static class NativeWindowLoop
{
    public static void Run(IWindow window)
    {
        ArgumentNullException.ThrowIfNull(window);
        window.Initialize();
        window.Run(() =>
        {
            window.DoEvents();
            if (!window.IsClosing)
                window.DoUpdate();
            if (!window.IsClosing)
                window.DoRender();
        });
        window.DoEvents();
        window.IsVisible = false;
    }
}
