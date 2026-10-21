// Stand-in for the user32 calls RynthCore's HotkeyService makes from acclient's WndProc.
// HotkeyService is compiled unchanged; after compile, tools/RynthInteropRedirect points
// its GetKeyState / GetForegroundWindow / GetWindowThreadProcessId at the methods of the
// same name and signature here. The host's hotkey sink sets the state before each key.
using System;

namespace AcDream.Rynth.EngineUi;

/// <summary>
/// The modifier keys and window focus as HotkeyService asks Windows for them, answered from
/// the overlay key event being handled (OpenAC has one window, ours). Game thread.
/// </summary>
internal static class Win32KeyStateStandIn
{
    private const int VkShift = 0x10, VkControl = 0x11, VkMenu = 0x12;
    private const int VkLShift = 0xA0, VkRShift = 0xA1, VkLControl = 0xA2, VkRControl = 0xA3, VkLMenu = 0xA4, VkRMenu = 0xA5;
    private static readonly IntPtr OurWindow = new(1);

    internal static bool Control { get; set; }

    internal static bool Shift { get; set; }

    internal static bool Alt { get; set; }

    /// <summary>The client window has the keyboard (false after focus loss until the next key).</summary>
    internal static bool Focused { get; set; }

    public static short GetKeyState(int nVirtKey)
    {
        bool down = nVirtKey switch
        {
            VkControl or VkLControl or VkRControl => Control,
            VkShift or VkLShift or VkRShift => Shift,
            VkMenu or VkLMenu or VkRMenu => Alt,
            _ => false,
        };
        return down ? unchecked((short)0x8000) : (short)0;
    }

    public static IntPtr GetForegroundWindow() => Focused ? OurWindow : IntPtr.Zero;

    public static uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid)
    {
        pid = hWnd == OurWindow ? (uint)Environment.ProcessId : 0;
        return pid == 0 ? 0u : 1u;
    }
}
