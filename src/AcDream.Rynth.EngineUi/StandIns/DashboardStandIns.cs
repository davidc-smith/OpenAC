// Stand-ins for the RynthCore engine types RynthAi's dashboard (RynthAiFace and
// the settings it edits) links to but OpenAC doesn't compile yet. Each keeps
// only the members their files touch. Panels named here are not in OpenAC:
// their buttons and menu items do nothing.
using System;
using System.Collections.Generic;
using System.Numerics;

namespace RynthCore.Engine
{
    /// <summary>RynthCore's engine entry; only the start time the dashboard's uptime shows.</summary>
    internal static class EntryPoint
    {
        internal static DateTime InitStartedUtc { get; set; } = DateTime.MinValue;
    }
}

namespace RynthCore.Engine.D3D9
{
    /// <summary>RynthCore measures acclient's frame rate in its EndScene hook; OpenAC has none, so 0.</summary>
    internal static class EndSceneHook
    {
        internal static volatile float MeasuredFps;
    }

    internal enum VideoPatchMode
    {
        Off = 0,
        Unfocused = 1,
        World = 2,
        All = 3,
    }

    internal static class VideoPatchPolicy
    {
        public static readonly string[] ComboItems = { "Off (not in OpenAC)" };

        /// <summary>RynthCore's per-character settings key: "server/character", or "Default" before login. Their rule.</summary>
        public static string CharacterKey(string? server, string? character)
        {
            string c = (character ?? string.Empty).Trim();
            if (c.Length == 0) return "Default";
            string s = (server ?? string.Empty).Trim();
            return (s.Length > 0 ? s + "/" + c : c).Replace('=', '_');
        }
    }

    /// <summary>RynthCore's acclient video patch; OpenAC renders itself, so it stays off.</summary>
    internal static class VideoPatch
    {
        public static VideoPatchMode Mode => VideoPatchMode.Off;

        public static int FrameCapFps => 0;

        public static string SetMode(VideoPatchMode mode, bool save, string source) => "not available in OpenAC";

        public static string SetFrameCap(int fps) => "not available in OpenAC";
    }
}

namespace RynthCore.Engine.Plugins
{
    /// <summary>
    /// The loaded plugins as RynthCore's dashboard reads them: the host sets
    /// <see cref="Plugins"/> and raises <see cref="RaisePluginsUnloaded"/> when
    /// it stops them. The plugins themselves live in AcDream.Rynth.Host.
    /// </summary>
    internal static class PluginManager
    {
        internal const int EmbedDrawn = 1, EmbedNotReady = 0, EmbedNoPlugin = -1, EmbedFailed = -2;

        public static IReadOnlyList<LoadedPlugin> Plugins { get; internal set; } = Array.Empty<LoadedPlugin>();

        public static event Action? PluginsUnloaded;

        internal static void RaisePluginsUnloaded() => PluginsUnloaded?.Invoke();

        /// <summary>RynthCore reads each plugin's RynthPluginGetSectionsJson; RynthAi exports none, so none.</summary>
        internal static List<(string Plugin, string Json)> CollectSectionManifests() => new();

        /// <summary>Server plugins RynthCore didn't load for this host; OpenAC loads none, so none skipped.</summary>
        internal static IReadOnlyList<SkippedServerPlugin> SkippedServerPlugins => Array.Empty<SkippedServerPlugin>();

        internal static void RequestServerPluginLoad(string serverId)
        {
        }

        /// <summary>RynthCore draws a plugin's embedded widgets; RynthAi exports none, so "no plugin".</summary>
        internal static int RenderEmbed(string pluginName, ReadOnlySpan<byte> surfaceUtf8, Vector2 size, out Vector2 want)
        {
            want = Vector2.Zero;
            return EmbedNoPlugin;
        }
    }
}

namespace RynthCore.Engine.Plugins
{
    /// <summary>RynthCore's skipped server plugin (path + manifest); never made in OpenAC.</summary>
    internal sealed record SkippedServerPlugin(string Path, SkippedServerPlugin.ManifestInfo Manifest)
    {
        internal sealed record ServerInfo(string Id, string Name, List<string> WorldNames);

        internal sealed record ManifestInfo(string Name, ServerInfo? Server);

        public string ServerId => Manifest.Server!.Id;

        public string ServerName => Manifest.Server!.Name;

        public string PluginName => Manifest.Name;
    }
}

namespace RynthCore.Engine.Compatibility
{
    /// <summary>
    /// RynthCore queues work for acclient's thread; only the chat write and the selection
    /// their code uses. OpenAC draws and ticks on one thread, so "queue" runs it now.
    /// </summary>
    internal static class AcMainThreadQueue
    {
        internal static Action<string, int>? WriteToChat { get; set; }

        /// <summary>Selects an object in the game (the host: IRynthGameView.SelectObject).</summary>
        internal static Func<uint, bool>? SelectObject { get; set; }

        public static bool EnqueueWriteToChat(string text, int color)
        {
            WriteToChat?.Invoke(text, color);
            return WriteToChat is not null;
        }

        public static bool EnqueueSetSelectedObject(uint objectId) => SelectObject?.Invoke(objectId) ?? false;
    }

    /// <summary>
    /// RynthCore works out whether the client is on Aelrynth (their server) to show
    /// its difficulty tiers; OpenAC never connects there, so never.
    /// </summary>
    internal static class ServerInfo
    {
        public const string ServerId = "aelrynth";

        private static readonly ServerVerdict NotAelrynth = new() { IsAelrynth = false, Reason = "OpenAC never connects to Aelrynth" };

        public static bool IsAelrynth => false;

        public static ServerVerdict Detected => NotAelrynth;

        /// <summary>RynthCore's launch host (from the launcher); OpenAC has none to read.</summary>
        public static string LaunchHost => string.Empty;

        public static void Invalidate()
        {
        }
    }

    /// <summary>RynthCore's acclient UI hooks; only the item AC is dragging, which OpenAC doesn't report (none).</summary>
    internal static class UiElementHooks
    {
        public static uint CurrentDragObject => 0;
    }

    /// <summary>RynthCore hides acclient's vitals bar; OpenAC's own bar is unaffected, the box is only remembered.</summary>
    internal static class RetailVitalsHooks
    {
        public static bool HideRetailVitals { get; set; }
    }
}

namespace RynthCore.Engine.UI
{
    /// <summary>RynthCore's server menu; its commands go to the chat parser like a typed line.</summary>
    internal static class ServerMenus
    {
        internal static Action<string>? Runner { get; set; }

        public static void Run(string command) => Runner?.Invoke(command);

        /// <summary>RynthCore's per-server menu items; none in OpenAC.</summary>
        public static bool DrawImGuiMenu(bool separatorFirst = false) => false;
    }
}

namespace RynthCore.Engine.UI.Data
{
    /// <summary>
    /// RynthCore's UI pump: work posted here runs on the next dashboard tick
    /// (RynthEngineHub.Tick), on the game thread, in order.
    /// </summary>
    internal static class UiDataHub
    {
        private static readonly Queue<(string Label, Action Action)> Posted = new();

        public static bool Post(string label, Action action)
        {
            lock (Posted)
                Posted.Enqueue((label, action));
            return true;
        }

        /// <summary>Runs everything posted so far; a failure is logged and the rest still run.</summary>
        internal static int Drain(Action<string> log)
        {
            int ran = 0;
            while (true)
            {
                (string Label, Action Action) next;
                lock (Posted)
                {
                    if (Posted.Count == 0)
                        return ran;
                    next = Posted.Dequeue();
                }
                try { next.Action(); }
                catch (Exception error) { log($"[RynthAi] {next.Label}: {error.GetType().Name}: {error.Message}"); }
                ran++;
            }
        }

        internal static void Clear()
        {
            lock (Posted)
                Posted.Clear();
        }
    }
}

namespace RynthCore.Engine.ImGuiBackend
{
    /// <summary>
    /// AC items dropped on RynthCore's panels: their UIElement drop detour claims the drop.
    /// OpenAC has no such hook, so no panel ever takes a dropped item.
    /// </summary>
    internal static class UiDropTargets
    {
        public static void Publish(string key, string panelTitle, Vector2 min, Vector2 max, bool accepting)
        {
        }

        public static bool TryTake(string key, out uint itemId)
        {
            itemId = 0;
            return false;
        }

        public static bool IsCursorOver(string key) => false;

        public static bool AnyDragInProgress => false;

        /// <summary>The item acclient is dragging, which RynthCore's detour records; OpenAC never sets it.</summary>
        public static uint EngineDragObject { get; set; }

        /// <summary>RynthCore lets a panel claim a dropped item or spell; no drop reaches a panel here.</summary>
        public static bool TryClaim(uint itemId, uint spellId, out string key)
        {
            key = string.Empty;
            return false;
        }
    }

    /// <summary>RynthCore's own toolbar; OpenAC has the Rynth bar instead, so the rows are only remembered.</summary>
    internal static class ImGuiBar
    {
        public static int Rows { get; private set; } = 1;

        public static int MaxPerRow { get; private set; } = 12;

        public static void SetRowLayout(int rows, int maxPerRow)
        {
            Rows = rows;
            MaxPerRow = maxPerRow;
        }
    }
}

namespace RynthCore.Engine.ImGuiBackend.Panels
{
    /// <summary>RynthCore's hotkeys editor; OpenAC binds its own keys, so the Hotkeys settings tab is empty.</summary>
    internal sealed class HotkeysSettingsUi
    {
        public const string TabName = "Hotkeys";

        public void Leave()
        {
        }

        public bool Draw(float w) => false;
    }

    /// <summary>RynthCore's server picker (launcher profiles); OpenAC picks the server at login.</summary>
    internal static class ServerPickSettingsUi
    {
        public static void Draw(float width, string? tooltip) =>
            ImGuiNET.ImGui.TextDisabled("Server: chosen at OpenAC's login.");
    }

    internal static class WorldMapFace
    {
        public const string Title = "World Map";
    }

    /// <summary>RynthCore's minimap (RynthNav maps); never registered in OpenAC, so the RynthNav panel hides its map buttons.</summary>
    internal static class MiniMapFace
    {
        public const string Title = "Mini Map";
    }

    /// <summary>RynthCore's Report a problem panel; never registered in OpenAC, so Settings hides its button.</summary>
    internal static class ReportFace
    {
        public const string Title = "Report a problem";
    }

    internal static class CharactersFace
    {
        public const string Title = "Characters";
    }
}

namespace RynthCore.Engine.UI.Panels
{
    /// <summary>RynthCore's dashboard launches their external Avalonia loot editor from here; OpenAC has none.</summary>
    internal static class RynthAiPanel
    {
        public static void LaunchLootEditor(string? path) =>
            RynthCore.Engine.Compatibility.AcMainThreadQueue.EnqueueWriteToChat(
                "[RynthAi] The external loot editor isn't in OpenAC; use the Loot Editor panel.", 1);
    }
}

namespace RynthCore.Engine.Report
{
    /// <summary>RynthCore's problem report (it uploads to their staff); not in OpenAC.</summary>
    internal static class ProblemReport
    {
        public static void OpenPanel()
        {
        }
    }
}
