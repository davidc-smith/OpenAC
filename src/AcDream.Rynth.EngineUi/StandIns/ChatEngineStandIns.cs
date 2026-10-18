// Stand-ins for the RynthCore engine types the compiled chat window files use.
// Each keeps only the members those files touch. None of these is RynthCore's
// real class: there is no P/Invoke, no window procedure and no acclient hook
// here. The host points the delegates at OpenAC (RynthChatHookup).
using System;
using System.Collections.Generic;
using RynthCore.Engine.UI.Data;

namespace RynthCore.Engine.Compatibility
{
    /// <summary>
    /// RynthCore's chat-ownership rule, unchanged: Rynth chat owns Enter (and
    /// OpenAC's chat window hides) while "Hide retail chat" is ticked, the chat
    /// window is shown, and the player is logged in. RynthCore's real ChatHooks
    /// hooks acclient's chat; this one only keeps the flags.
    /// </summary>
    internal static class ChatHooks
    {
        public static bool SuppressOriginalChat;
        public static volatile bool ChatPanelShown;

        public static bool RynthChatOwnsChat =>
            SuppressOriginalChat && ChatPanelShown && LoginLifecycleHooks.HasObservedLoginComplete;
    }

    internal static class ClientHelperHooks
    {
        internal static Func<uint>? PlayerId { get; set; }

        public static uint GetPlayerId() => PlayerId?.Invoke() ?? 0;
    }

    internal static class ClientObjectHooks
    {
        internal delegate bool ItemTypeReader(uint objectId, out uint typeFlags);
        internal delegate bool NameReader(uint objectId, out string name);

        internal static ItemTypeReader? ItemType { get; set; }
        internal static NameReader? Name { get; set; }

        public static bool TryGetItemType(uint objectId, out uint typeFlags)
        {
            typeFlags = 0;
            return ItemType is { } read && read(objectId, out typeFlags);
        }

        public static bool TryGetObjectName(uint objectId, out string name)
        {
            name = string.Empty;
            return Name is { } read && read(objectId, out name);
        }
    }

    internal static class SelectedTargetHooks
    {
        internal static Func<uint>? SelectedId { get; set; }

        public static uint ReadCurrentSelectedId() => SelectedId?.Invoke() ?? 0;
    }

    internal static class SessionStateRegistry
    {
        internal static Func<string>? CharacterName { get; set; }

        internal static string LastCharacterName => CharacterName?.Invoke() ?? string.Empty;
    }
}

namespace RynthCore.Engine.UI
{
    /// <summary>RynthCore opens and closes panels through this; here, our panel host.</summary>
    internal static class PanelRouter
    {
        public static void Toggle(string title) => RynthCore.Engine.ImGuiBackend.ImGuiPanelHost.Toggle(title);

        /// <summary>OpenAC has no pop-out windows.</summary>
        public static bool CanPopOut => false;

        /// <summary>RynthCore's Monsters panel is not in OpenAC yet; its buttons do nothing.</summary>
        public static void ToggleMonsters()
        {
        }

        /// <summary>RynthCore maps old panel names to current ones; ours are already current.</summary>
        public static string Canonical(string title) => title;
    }

    /// <summary>RynthCore's Avalonia overlay flag; OpenAC has no Avalonia overlay.</summary>
    internal static class AvaloniaOverlay
    {
        internal static volatile bool ChatCtrlGatedClickThrough;
    }
}

namespace RynthCore.Engine.UI.Data
{
    /// <summary>Finds a loaded plugin's export by name; the host sets <see cref="Resolver"/>.</summary>
    internal static class PluginExportBinder
    {
        internal static Func<string, string, IntPtr>? Resolver { get; set; }

        public static IntPtr Resolve(string pluginNamePart, string exportName) =>
            Resolver?.Invoke(pluginNamePart, exportName) ?? IntPtr.Zero;
    }

    /// <summary>RynthCore's source hub; only the sources of the faces OpenAC compiles.</summary>
    internal static class UiSources
    {
        public static readonly ChatSource Chat = new();
        public static readonly RynthAiSource RynthAi = new();
        public static readonly PatrolSource Patrol = new();
        public static readonly NavSource Nav = new();
        public static readonly SettingsSource Settings = new();
        public static readonly RynthSortWatch RynthSortWatch = new();
    }

    /// <summary>RynthNav's "Arrow to" / "Go to"; no RynthNav in OpenAC.</summary>
    internal static class RynthNavCommands
    {
        public static void Arrow(string name, double ns, double ew)
        {
        }

        public static void Go(string name, double ns, double ew)
        {
        }
    }
}

namespace RynthCore.Engine.UI.Data.Discord
{
    /// <summary>RynthCore's Discord relay; off in OpenAC.</summary>
    internal static class ChatDiscord
    {
        internal static void PumpRead(IReadOnlyList<ChatLine> added)
        {
        }

        internal static void PumpSourceReset()
        {
        }

        internal static void PumpTick()
        {
        }

        public static string StatusLine() => "not available in OpenAC";

        public static string SwitchLine() => "Discord relay:";
    }
}

namespace RynthCore.Engine.ImGuiBackend
{
    /// <summary>
    /// Only the chat-input members of RynthCore's Win32Backend, which their
    /// ChatModel fills and reads. Their real Win32Backend subclasses acclient's
    /// window; this is a plain holder that OpenAC's keyboard sink
    /// (RynthChatKeyboard) drives on every OS.
    /// </summary>
    internal static class Win32Backend
    {
        public static volatile bool ChatCaptureActive;
        public static Action? OnChatCaptureActivated;
        public static Action? OnChatTellSelected;
        public static Action<char>? OnChatChar;
        public static Action? OnChatBackspace;
        public static Action? OnChatDelete;
        public static Action? OnChatSend;
        public static Action? OnChatCancel;
        public static Action? OnChatLeft;
        public static Action? OnChatRight;
        public static Action? OnChatHome;
        public static Action? OnChatEnd;
        public static Action? OnChatUp;
        public static Action? OnChatDown;
        public static Action? OnChatPaste;
        public static Action? OnChatCopy;
        public static Action? OnChatCut;

        /// <summary>The Tell button: run <see cref="OnChatTellSelected"/> on the next tick (RynthCore posts it).</summary>
        internal static bool TellRequested;

        public static void RequestTellSelected() => TellRequested = true;

        /// <summary>RynthCore takes keyboard focus back from acclient; OpenAC's overlay already owns it.</summary>
        public static void ReclaimUi()
        {
        }
    }

    /// <summary>RynthCore's pop-out windows; OpenAC has none.</summary>
    internal static class ImGuiPopOuts
    {
        public static bool InPopOutFrame => false;
    }
}

namespace RynthCore.Engine.ImGuiBackend.Panels
{
    /// <summary>The Discord relay's panel; off in OpenAC, so its switch draws nothing.</summary>
    internal static class ChatDiscordFace
    {
        public const string Title = "Discord";

        public static void DrawSwitch()
        {
        }
    }

    /// <summary>RynthNav's panel title; never registered here, so coordinate links stay hidden.</summary>
    internal static class RynthNavFace
    {
        public const string Title = "RynthNav";
    }
}
