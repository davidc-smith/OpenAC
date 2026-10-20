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

        /// <summary>The host's item actions, as RynthCore sends them through acclient's own calls. Game thread.</summary>
        internal static Func<uint, bool>? Use { get; set; }
        internal static Func<uint, uint, int, int, bool>? Move { get; set; }      // item, container, slot, amount (0 = whole)
        internal static Func<uint, uint, bool>? Wield { get; set; }
        internal static Func<uint, bool>? Drop { get; set; }
        internal static Func<uint, uint, int, bool>? Give { get; set; }
        internal static Func<uint, bool>? ContainerFull { get; set; }

        public static bool HasDropItem => Drop is not null;
        public static bool HasPutItemInContainer => Move is not null;
        public static bool HasGiveObjectTo => Give is not null;
        public static bool HasWieldItem => Wield is not null;

        public static bool UseObject(uint objectId) => Use?.Invoke(objectId) ?? false;
        public static bool PutItemInContainerAt(uint objectId, uint containerId, int slot) =>
            Move?.Invoke(objectId, containerId, slot, 0) ?? false;
        // acclient's MoveItemInternal / SplitStackInternal walk the pack's slot table for the first
        // empty slot (RynthCore always passes slot 0) and MoveItemInternal refuses a full pack of the
        // player's own. ACE keeps places contiguous, so the first empty one is the item count.
        internal static Func<uint, uint, int>? FirstEmptySlot { get; set; }   // container, moving item → slot

        public static bool MoveItemInternal(uint objectId, uint targetContainerId, int slot, int amount) =>
            !IsOwnedContainerFull(targetContainerId)
            && (Move?.Invoke(objectId, targetContainerId, FirstEmptySlot?.Invoke(targetContainerId, objectId) ?? 0, amount) ?? false);
        // A split leaves the source stack where it is, so even the item's own container counts it.
        public static bool SplitStackInternal(uint objectId, uint targetContainerId, int slot, int amount) =>
            Move?.Invoke(objectId, targetContainerId, FirstEmptySlot?.Invoke(targetContainerId, 0) ?? 0, amount) ?? false;
        internal static bool IsOwnedContainerFull(uint target) => ContainerFull?.Invoke(target) ?? false;
        public static bool WieldItem(uint objectId, uint equipMask) => Wield?.Invoke(objectId, equipMask) ?? false;
        public static bool DropItem(uint objectId) => Drop?.Invoke(objectId) ?? false;
        public static bool GiveObjectTo(uint objectId, uint targetId, int amount = 0) =>
            Give?.Invoke(objectId, targetId, amount) ?? false;
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

        internal delegate bool ObjectPositionReader(uint objectId, out uint cellId, out float x, out float y, out float z);

        /// <summary>The ids of the objects the client knows now; the host sets it.</summary>
        internal static Func<uint[]>? LiveIds { get; set; }

        /// <summary>An object's cell and landblock-local origin; the host sets it.</summary>
        internal static ObjectPositionReader? PositionSnapshot { get; set; }

        public static uint[] LiveObjectIds => LiveIds?.Invoke() ?? Array.Empty<uint>();

        public static bool TryGetSnapshotPosition(uint objectId, out uint objCellId, out float x, out float y, out float z)
        {
            objCellId = 0; x = y = z = 0;
            return PositionSnapshot is { } read && read(objectId, out objCellId, out x, out y, out z);
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

        internal delegate bool ItemFieldsReader(uint objectId, out ItemPwdFields fields);
        internal delegate bool IntPropertyReader(uint objectId, uint stype, out int value);
        internal delegate bool AttributeReader(uint objectId, uint stype, int raw, out uint value);

        /// <summary>The host's item fields, int properties, attributes and container contents.</summary>
        internal static ItemFieldsReader? ItemFields { get; set; }
        internal static IntPropertyReader? IntProperty { get; set; }
        internal static AttributeReader? Attribute { get; set; }
        internal static Func<uint, IReadOnlyList<uint>>? Contents { get; set; }

        // RynthCore reads these PublicWeenieDesc fields out of acclient's object in one go.
        internal static bool TryReadItemFields(uint objectId, out ItemPwdFields f)
        {
            f = default;
            return ItemFields is { } read && read(objectId, out f);
        }

        public static bool TryGetObjectIntProperty(uint objectId, uint stype, out int value)
        {
            value = 0;
            return IntProperty is { } read && read(objectId, stype, out value);
        }

        public static bool TryGetObjectAttribute(uint objectId, uint stype, int raw, out uint value)
        {
            value = 0;
            return Attribute is { } read && read(objectId, stype, raw, out value);
        }

        // RynthCore walks its ~100 ms copy of acclient's objects; here, the client's table now.
        internal static int CollectOwnedIdsFromSnapshot(uint containerId, Span<uint> dest)
        {
            if (Contents?.Invoke(containerId) is not { } ids)
                return 0;
            int n = Math.Min(ids.Count, dest.Length);
            for (int i = 0; i < n; i++)
                dest[i] = ids[i];
            return n;
        }

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
    /// <summary>Which face a panel shows (RynthCore: Avalonia, ImGui or both, per player choice). OpenAC has only ImGui.</summary>
    internal enum PanelFace
    {
        Avalonia,
        ImGui,
        Both,
    }

    /// <summary>RynthCore opens and closes panels through this; here, our panel host.</summary>
    internal static class PanelRouter
    {
        /// <summary>The face a docked panel uses: always ImGui here.</summary>
        public static PanelFace ResolveDockedFace(string title) => PanelFace.ImGui;

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

    /// <summary>RynthCore's Avalonia overlay flags; OpenAC has no Avalonia overlay (their ImGui faces only set them).</summary>
    internal static class AvaloniaOverlay
    {
        internal static volatile bool ChatCtrlGatedClickThrough;
        internal static volatile bool RadarCtrlGatedClickThrough;
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
        public static readonly RadarSource Radar = new();
        public static readonly RynthNavSource RynthNav = new();
        public static readonly RynthNavAtlasSource RynthNavAtlas = new();
        public static readonly RynthNavArrowWatch RynthNavArrowWatch = new();
        public static readonly CharactersSource Characters = new();
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
}
