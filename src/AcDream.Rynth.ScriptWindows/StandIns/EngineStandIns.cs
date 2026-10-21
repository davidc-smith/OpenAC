// Stand-ins for the RynthCore engine types the compiled script-window files use.
// Each keeps only the members those files touch; see the plan's Task 1.
using System;
using System.Runtime.InteropServices;

namespace RynthCore.Engine
{
    /// <summary>RynthCore's log; the host points <see cref="Sink"/> at its own log.</summary>
    internal static class RynthLog
    {
        internal static Action<string>? Sink { get; set; }

        internal static void UI(string msg) => Sink?.Invoke("[ScriptWindows] " + msg);

        internal static void Compat(string msg) => Sink?.Invoke("[Compat] " + msg);
    }
}

namespace RynthCore.Engine.UI
{
    /// <summary>RynthCore queues this work to the thread pool; here it runs inline.</summary>
    internal static class UiBackgroundWriter
    {
        public static void Enqueue(string label, Action work)
        {
            try { work(); }
            catch { }
        }
    }

    /// <summary>RynthCore restyles and places the acclient window; OpenAC's window is its own, so these do nothing.</summary>
    internal static class AcWindowChrome
    {
        public static void RequestApply()
        {
        }

        public static void SaveLayoutNow()
        {
        }
    }
}

namespace RynthCore.Engine.Plugins
{
    /// <summary>A script-window owner: one per hosted plugin, compared by reference.</summary>
    internal sealed class LoadedPlugin
    {
        public string DisplayName { get; set; } = "";
        public string VersionString { get; set; } = "";
        public bool Failed { get; set; }
    }

    internal static class EngineSettings
    {
        /// <summary>True while an ImGui overlay is drawing script windows.</summary>
        public static bool EnableImGuiBackend { get; set; }

        // RynthCore engine settings that their Settings panel shows. They steer
        // acclient hooks OpenAC doesn't have, so here they are only remembered.
        public static bool InsertReleasesUi { get; set; }

        public static bool CloseOnServerDisconnect { get; set; }

        public static Compatibility.AutoIdMode AutoIdMode { get; set; } = Compatibility.AutoIdMode.Limited;

        // Settings > Display and engine reload settings (2026-10-20 snapshot). They steer the
        // acclient window and engine hot reloads, which OpenAC doesn't have: remembered only.
        public static bool BorderlessWindowed { get; set; }

        public static bool RememberWindowLayout { get; set; } = true;

        public static bool RememberAcWindowPosition { get; set; }

        public static bool HoldReloadInWorld { get; set; }

        public static int HoldReloadInWorldMaxMinutes { get; set; } = 10;
    }

    /// <summary>Layout copied from RynthCore.Engine/Plugins/PluginContract.cs (480 bytes).</summary>
    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    internal unsafe struct UiInfoNative
    {
        public uint Size;
        public uint Flags;
        public ushort MaxFormatVersion;
        public ushort OpLevel;
        public uint MaxOpsPerWindow;
        public uint MaxBytesPerWindow;
        public uint MaxWindowsPerOwner;
        public uint MaxBytesPerSubmit;
        public float DisplayWidth;
        public float DisplayHeight;
        public float UiScale;
        public float TextLineHeight;
        public float FrameHeight;
        public float ItemSpacingX;
        public float ItemSpacingY;
        public float FramePaddingX;
        public float FramePaddingY;
        public fixed float AsciiAdvance[95];
        public uint FrameCounter;
        public fixed uint Reserved[8];
    }
}

namespace RynthCore.Engine.Compatibility
{
    /// <summary>RynthCore's background-identify modes (Compatibility/AutoIdPolicy.cs).</summary>
    internal enum AutoIdMode { All = 0, Limited = 1, Off = 2 }

    internal static class LoginLifecycleHooks
    {
        /// <summary>Set by the host's event bridge on login and logout.</summary>
        public static bool HasObservedLoginComplete { get; set; }
    }
}
