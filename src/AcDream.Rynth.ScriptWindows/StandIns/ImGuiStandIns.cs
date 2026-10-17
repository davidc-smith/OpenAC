// Stand-ins for the RynthCore ImGui-backend types the compiled files use.
using System;
using System.Numerics;
using ImGuiNET;
using RynthCore.Engine.UI.ScriptWindows;

namespace RynthCore.Engine.ImGuiBackend
{
    internal static class EngineFrameController
    {
        /// <summary>RynthCore's UI scale. The overlay already handles the display scale, so 1.</summary>
        internal static float FontScale => 1f;
    }

    internal static class ImGuiTextFocus
    {
        public static void NoteActive(string id)
        {
        }
    }

    internal static class ScriptIcons
    {
        /// <summary>No AC icons yet: the replay draws its placeholder square.</summary>
        public static bool TryGet(ScriptIconKind kind, uint id, out IntPtr texture)
        {
            texture = IntPtr.Zero;
            return false;
        }
    }

    internal enum UiFont
    {
        Ui11,
    }

    internal static class ImGuiFonts
    {
        /// <summary>RynthCore's sized UI fonts; here, the overlay's current font.</summary>
        public static ImFontPtr Get(UiFont font) => ImGui.GetFont();
    }

    internal enum PanelChrome
    {
        Standard,
        None,
    }

    /// <summary>The members of RynthCore's PanelSpec that the registry sets.</summary>
    internal sealed record PanelSpec(
        Vector2 DefaultSize,
        Vector2 MinSize,
        PanelChrome Chrome = PanelChrome.Standard,
        uint? Background = null,
        bool NoPopOut = false,
        ImGuiWindowFlags BodyFlags = ImGuiWindowFlags.None,
        Vector2? DefaultPos = null);

    internal interface IImGuiPanel
    {
        void OnShown()
        {
        }

        void OnHidden()
        {
        }

        void Draw();

        Vector2? MinSize => null;

        bool ClickThrough => false;

        int PopOutIdleHz => 10;

        bool HiddenWhileDocked => false;
    }
}
