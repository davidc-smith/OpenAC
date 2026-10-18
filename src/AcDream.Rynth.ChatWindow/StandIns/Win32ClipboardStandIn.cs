// Stand-in for the Win32 clipboard API RynthCore's ChatModel calls. ChatModel
// is compiled unchanged; after compile, tools/RynthInteropRedirect points each
// of its user32/kernel32 P/Invokes named below at the method of the same name
// and signature here. The host points GetText/SetText at OpenAC's clipboard.
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace AcDream.Rynth.ChatWindow;

/// <summary>
/// The Win32 clipboard as ChatModel drives it (open, put or get a
/// CF_UNICODETEXT global, close), over OpenAC's clipboard on every OS. Global
/// memory is native memory tracked here; a global handed to SetClipboardData
/// is freed here, as the system would own it. Game thread.
/// </summary>
internal static class Win32ClipboardStandIn
{
    private const uint CfUnicodeText = 13;

    private static readonly object Gate = new();
    private static readonly Dictionary<IntPtr, int> Sizes = new();
    // GetClipboardData's global: the clipboard's, freed at CloseClipboard.
    private static IntPtr _held;

    /// <summary>OpenAC's clipboard text; null when there is no clipboard.</summary>
    internal static Func<string>? GetText { get; set; }

    /// <summary>Writes OpenAC's clipboard; null when there is no clipboard.</summary>
    internal static Action<string>? SetText { get; set; }

    public static bool OpenClipboard(IntPtr hWndNewOwner) => true;

    public static bool CloseClipboard()
    {
        lock (Gate)
        {
            Release(_held);
            _held = IntPtr.Zero;
        }

        return true;
    }

    public static bool EmptyClipboard() => true;

    public static unsafe IntPtr SetClipboardData(uint uFormat, IntPtr hMem)
    {
        if (uFormat != CfUnicodeText || SetText is not { } set)
            return IntPtr.Zero;
        int size;
        lock (Gate)
        {
            if (!Sizes.TryGetValue(hMem, out size))
                return IntPtr.Zero;
        }

        char* chars = (char*)hMem;
        int length = 0;
        while (length < size / 2 && chars[length] != '\0')
            length++;
        try { set(new string(chars, 0, length)); }
        catch (Exception) { return IntPtr.Zero; }   // ownership not taken: theirs to free

        lock (Gate)
            Release(hMem);
        return hMem;
    }

    public static unsafe IntPtr GetClipboardData(uint uFormat)
    {
        string text = uFormat == CfUnicodeText ? ReadText() : string.Empty;
        if (text.Length == 0)
            return IntPtr.Zero;
        IntPtr global = GlobalAlloc(0, (UIntPtr)((text.Length + 1) * 2));
        if (global == IntPtr.Zero)
            return IntPtr.Zero;
        text.AsSpan().CopyTo(new Span<char>((void*)global, text.Length));
        lock (Gate)
        {
            Release(_held);
            _held = global;
        }

        return global;
    }

    public static bool IsClipboardFormatAvailable(uint format) => format == CfUnicodeText && ReadText().Length > 0;

    public static unsafe IntPtr GlobalAlloc(uint uFlags, UIntPtr dwBytes)
    {
        if (dwBytes == UIntPtr.Zero || dwBytes > int.MaxValue)
            return IntPtr.Zero;
        var global = (IntPtr)NativeMemory.AllocZeroed((nuint)dwBytes);
        lock (Gate)
            Sizes[global] = (int)dwBytes;
        return global;
    }

    public static IntPtr GlobalLock(IntPtr hMem)
    {
        lock (Gate)
            return Sizes.ContainsKey(hMem) ? hMem : IntPtr.Zero;
    }

    public static bool GlobalUnlock(IntPtr hMem)
    {
        lock (Gate)
            return Sizes.ContainsKey(hMem);
    }

    public static IntPtr GlobalFree(IntPtr hMem)
    {
        lock (Gate)
            return Release(hMem) ? IntPtr.Zero : hMem;
    }

    public static UIntPtr GlobalSize(IntPtr hMem)
    {
        lock (Gate)
            return Sizes.TryGetValue(hMem, out int size) ? (UIntPtr)size : UIntPtr.Zero;
    }

    private static string ReadText()
    {
        try { return GetText?.Invoke() ?? string.Empty; }
        catch (Exception) { return string.Empty; }
    }

    // Under Gate.
    private static unsafe bool Release(IntPtr global)
    {
        if (!Sizes.Remove(global))
            return false;
        NativeMemory.Free((void*)global);
        return true;
    }
}
