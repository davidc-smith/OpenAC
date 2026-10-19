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
        /// <summary>
        /// The client's icons: a texture id for an icon, object or spell id, or
        /// zero for none. Set by the host while it has an overlay; null draws
        /// the replay's placeholder square.
        /// </summary>
        public static Func<ScriptIconKind, uint, IntPtr>? Source { get; set; }

        public static bool TryGet(ScriptIconKind kind, uint id, out IntPtr texture)
        {
            texture = IntPtr.Zero;
            if (kind > ScriptIconKind.Spell || id == 0 || Source is not { } source)
                return false;
            texture = source(kind, id);
            return texture != IntPtr.Zero;
        }
    }

    internal enum UiFont
    {
        Ui10, Ui11, Ui14,
        UiBold11, UiBold14,
        Mono9, Mono10, Mono12,
        Code12,
        Chat8, Chat10, Chat12, Chat14, Chat16, Chat18,
        Ui9,
        Dash9, Dash10, Dash11, Dash14,
        NumBold24,
        Ui18, NumBold48,
        Count,
    }

    internal static class ImGuiFonts
    {
        /// <summary>RynthCore's sized UI fonts; here, the overlay's current font.</summary>
        public static ImFontPtr Get(UiFont font) => ImGui.GetFont();

        /// <summary>
        /// The overlay's font at a point size (an ImFont*), or zero while that size isn't
        /// baked yet. Set by the host while it has an overlay; null for none.
        /// </summary>
        public static Func<float, IntPtr>? Source { get; set; }

        /// <summary>
        /// RynthCore's chat font nearest a point size (they bake 8, 10, ... 18); here, the
        /// overlay's font at that size, or the current font until the overlay has baked it.
        /// </summary>
        public static unsafe ImFontPtr Chat(float points)
        {
            int step = Math.Clamp((int)MathF.Round((points - 8f) / 2f), 0, 5);
            IntPtr font = Source?.Invoke(8 + step * 2) ?? IntPtr.Zero;
            return font == IntPtr.Zero ? ImGui.GetFont() : new ImFontPtr((ImFont*)font);
        }

        /// <summary>RynthCore's baked text-size step; the overlay has one font size, so 0 (normal).</summary>
        public static int CurrentStep;

        /// <summary>The baked step nearest a panel's text size; only normal is baked here.</summary>
        public static int BakedStepNear(int step) => 0;

        /// <summary>RynthCore re-bakes its fonts for a new text size; the overlay's font doesn't change.</summary>
        public static void RequestRebuild(int delayMs = 0)
        {
        }
    }

    internal enum PanelChrome
    {
        Standard,
        None,
    }

    /// <summary>The members of RynthCore's PanelSpec that their faces set (of Background, BorderColor and Rounding only a zero Background is honoured).</summary>
    internal sealed record PanelSpec(
        Vector2 DefaultSize,
        Vector2 MinSize,
        PanelChrome Chrome = PanelChrome.Standard,
        uint? Background = null,
        bool NoPopOut = false,
        ImGuiWindowFlags BodyFlags = ImGuiWindowFlags.None,
        Vector2? DefaultPos = null,
        bool EdgeToEdge = false,
        bool GripInBody = false,
        uint? BorderColor = null,
        float Rounding = 0f,
        bool OpenCentered = false);

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
