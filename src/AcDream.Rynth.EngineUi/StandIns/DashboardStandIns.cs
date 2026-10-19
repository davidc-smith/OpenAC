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

        /// <summary>
        /// RynthCore's own cimgui.dll, which carries their native code editor. OpenAC's cimgui
        /// has no editor, so none: CodeEditor reports itself unavailable and Meta's Source tab
        /// says so (the rule list and rule editor don't need it).
        /// </summary>
        internal static IntPtr ImGuiNativeHandle => IntPtr.Zero;
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

        /// <summary>RynthCore draws a plugin's embedded widgets; RynthAi exports none, so "no plugin".</summary>
        internal static int RenderEmbed(string pluginName, ReadOnlySpan<byte> surfaceUtf8, Vector2 size, out Vector2 want)
        {
            want = Vector2.Zero;
            return EmbedNoPlugin;
        }
    }
}

namespace RynthCore.Engine.Compatibility
{
    /// <summary>RynthCore queues work for acclient's thread; only the chat write their dashboard code uses.</summary>
    internal static class AcMainThreadQueue
    {
        internal static Action<string, int>? WriteToChat { get; set; }

        public static bool EnqueueWriteToChat(string text, int color)
        {
            WriteToChat?.Invoke(text, color);
            return WriteToChat is not null;
        }
    }

    /// <summary>
    /// RynthCore works out whether the client is on Aelrynth (their server) to show
    /// its difficulty tiers; OpenAC never connects there, so never.
    /// </summary>
    internal static class ServerInfo
    {
        public static bool IsAelrynth => false;
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
    /// (RynthAiDashboard.Tick), on the game thread, in order.
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

    /// <summary>RynthCore's Dungeon Map; not in OpenAC yet (it needs their radar), so its button does nothing.</summary>
    internal static class DungeonMapFace
    {
        public const string Title = "Dungeon Map";

        public static void Register()
        {
        }
    }

    internal static class WorldMapFace
    {
        public const string Title = "World Map";
    }

    internal static class MiniRemoteFace
    {
        public const string Title = "Mini Remote";

        public static bool ShowsSection(string sectionId) => false;
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

    internal static class InventoryFace
    {
        public const string Title = "Inventory";
    }

    internal static class ItemCountFace
    {
        public const string Title = "Item Count";
    }

    internal static class SkillsFace
    {
        public const string Title = "Skills";
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

namespace RynthCore.Engine.ImGuiBackend.Hud
{
    /// <summary>
    /// RynthCore reads spell names from client_portal.dat; not wired in OpenAC yet,
    /// so recall spells show by id.
    /// </summary>
    internal static class PortalSpellTable
    {
        public static void EnsureLoadQueued()
        {
        }

        /// <summary>The table is never read here, so never ready.</summary>
        public static bool Ready => false;

        public static bool TryGetName(uint spellId, out string name)
        {
            name = string.Empty;
            return false;
        }
    }
}
