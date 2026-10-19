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

        /// <summary>The selected object, as SelectedTargetHooks reads it.</summary>
        public static uint GetSelectedItemId() => SelectedTargetHooks.ReadCurrentSelectedId();
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

        /// <summary>Fills their PlayerProgress from the game view; the host sets it. False when not in the world.</summary>
        internal static Func<PlayerProgress, bool>? ProgressReader { get; set; }

        public static bool ReadPlayerProgressLive(PlayerProgress dst) => ProgressReader is { } read && read(dst);

        /// <summary>The PublicWeenieDesc fields RynthCore reads in one go; their InventoryItem compares against it.</summary>
        internal struct ItemPwdFields
        {
            public uint Wcid, Icon, Container, Wielder, ValidLocations, Location, Type, Value, Useability,
                StackSize, MaxStackSize, Bitfield, SpellId;
            public int ItemsCapacity, Burden, MaterialType;
            public float Workmanship;
        }

        internal delegate bool FactsReader(uint objectId, out uint wcid, out uint bitfield, out uint containerId, out uint wielderId);
        internal delegate bool InstanceIdReader(uint objectId, uint stat, out uint value);

        /// <summary>An object's weenie class, public bitfield, container and wielder; the host sets it.</summary>
        internal static FactsReader? Facts { get; set; }

        internal static InstanceIdReader? InstanceId { get; set; }

        internal static Func<uint, bool>? Attackable { get; set; }

        // RynthCore reads these from its per-frame copy of acclient's objects ("snapshot");
        // here they are the same reads as the live ones.
        public static bool TryGetSnapshotName(uint objectId, out string name) => TryGetObjectName(objectId, out name);

        public static bool TryGetSnapshotItemType(uint objectId, out uint typeFlags) => TryGetItemType(objectId, out typeFlags);

        public static bool TryGetSnapshotPwdInfo(uint objectId, out uint bitfield, out uint containerId, out uint wielderId)
        {
            bitfield = containerId = wielderId = 0;
            return Facts is { } read && read(objectId, out _, out bitfield, out containerId, out wielderId);
        }

        public static bool TryGetObjectWcid(uint objectId, out uint wcid)
        {
            wcid = 0;
            return Facts is { } read && read(objectId, out wcid, out _, out _, out _);
        }

        public static bool TryGetObjectInstanceIdProperty(uint objectId, uint stype, out uint value)
        {
            value = 0;
            return InstanceId is { } read && read(objectId, stype, out value);
        }

        public static bool TryGetSnapshotAttackable(uint objectId, out bool attackable)
        {
            attackable = false;
            if (Attackable is not { } read || !TryGetItemType(objectId, out _))
                return false;
            attackable = read(objectId);
            return true;
        }
    }

    /// <summary>
    /// RynthCore copies the player's pose every frame; here the host answers from the
    /// game view on each call. Only the cell and position: no rotation or heading.
    /// </summary>
    internal static class PlayerPhysicsHooks
    {
        internal delegate bool PositionReader(out uint cell, out float x, out float y, out float z);

        internal static PositionReader? Position { get; set; }

        public static bool TryGetPlayerPoseSnapshot(out uint objCellId, out float x, out float y, out float z,
                                                    out float qw, out float qz, out bool headingValid, out float heading)
        {
            qw = qz = heading = 0;
            headingValid = false;
            objCellId = 0;
            x = y = z = 0;
            return Position is { } read && read(out objCellId, out x, out y, out z);
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

        internal static Func<string>? ServerName { get; set; }

        internal static string LastServerName => ServerName?.Invoke() ?? string.Empty;
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

        /// <summary>The dashboard's Monsters button: the Damage panel (RynthCore retired the basic Monsters panel).</summary>
        public static void ToggleMonsters() => Toggle(Canonical("Monsters"));

        /// <summary>The panel that shows <paramref name="title"/>: "Monsters" is the Damage panel, as in RynthCore.</summary>
        public static string Canonical(string title) =>
            title.Equals("Monsters", StringComparison.OrdinalIgnoreCase)
                ? RynthCore.Engine.ImGuiBackend.Panels.DamageFace.Title
                : title;
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
        public static readonly MetaSource Meta = new();
        public static readonly LootEditSource LootEdit = new();
        public static readonly LuaSource Lua = new();
        public static readonly ItemsSource Items = new();
        public static readonly DamageSource Damage = new();
        public static readonly ProgressionSource Progression = new();
        public static readonly MonsterRulesSource MonsterRules = new();
        public static readonly MonsterDetailSource MonsterDetail = new();
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
