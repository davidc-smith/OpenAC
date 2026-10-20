// Stand-ins for what RynthCore's Mini Remote sections (UI/Sections) read about servers:
// OpenAC loads no server plugins and none of ILT's, so no server is ever active here.
using System;

namespace RynthCore.Engine.Compatibility
{
    /// <summary>
    /// RynthCore detects Infinite Leaftide (host, world name, per-character override) for
    /// RynthILT's features. OpenAC loads no ILT plugins, so it is never ILT.
    /// </summary>
    internal static class IltInfo
    {
        public const string ServerId = "ilt";

        private static readonly IltVerdict NotIlt = new() { IsIlt = false, Reason = "OpenAC: no ILT plugins" };

        public static bool IsIlt => false;

        public static bool PluginLoaded => false;

        public static IltVerdict Detected => NotIlt;

        public static void Invalidate()
        {
        }
    }

    /// <summary>RynthCore's Aelrynth verdict (their ServerInfo.cs). Immutable.</summary>
    internal sealed class ServerVerdict
    {
        public bool IsAelrynth { get; init; }

        public string Reason { get; init; } = "";
    }

    /// <summary>RynthCore reads the world name the server announced at login; the host sets the reader.</summary>
    internal static class AccountHooks
    {
        internal static Func<string>? WorldName { get; set; }

        public static string? AnnouncedWorldName
        {
            get
            {
                string name = WorldName?.Invoke() ?? string.Empty;
                return name.Length > 0 ? name : null;
            }
        }
    }
}
