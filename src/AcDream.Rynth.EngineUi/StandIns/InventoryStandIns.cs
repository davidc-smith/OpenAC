// Stand-ins for the RynthCore engine types their Inventory panel, InventoryModel,
// RetailSprites and VaultFeed use. Each keeps only the members those files touch;
// the host (RynthInventoryHookup) points the delegates at OpenAC's game view.
using System;
using System.Collections.Generic;
using ImGuiNET;

namespace RynthCore.Engine.Compatibility
{
    /// <summary>
    /// RynthCore reads acclient's item request slot and attack flag from its .data. OpenAC's
    /// InventoryTransactionState keeps the same one pending request; the host answers from it.
    /// </summary>
    internal static class ClientActionGates
    {
        internal delegate bool PendingReader(out uint type, out uint objectId);

        internal static PendingReader? Pending { get; set; }
        internal static Func<bool>? Attacking { get; set; }

        public static bool IsRequestGateAvailable => Pending is not null;
        public static bool IsAttackGateAvailable => Attacking is not null;

        public static bool TryReadPending(out uint type, out uint objectId, out double ageSeconds)
        {
            (type, objectId, ageSeconds) = (0, 0, -1);
            if (Pending is not { } read)
                return false;
            if (!read(out type, out objectId))
                (type, objectId) = (0, 0);
            return true;
        }

        public static bool TryReadAttacking(out bool attacking)
        {
            attacking = false;
            if (Attacking is not { } read)
                return false;
            attacking = read();
            return true;
        }

        // Their names, verbatim (ClientActionGates.RequestTypeName).
        public static string RequestTypeName(uint type) => type switch
        {
            0 => "none",
            1 => "merge",
            2 => "split",
            3 => "move",
            4 => "pick up",
            5 => "put in container",
            6 => "drop",
            7 => "wield",
            9 => "give",
            10 => "vendor buy/sell",
            0x100 => "earlier item action",   // OpenAC's own: its item busy count with no request pending
            _ => $"request {type}",
        };
    }

    /// <summary>
    /// RynthCore counts acclient's UseDone and WeenieError events; the host answers from the
    /// client's ServerReplies (as a tuple: this assembly doesn't reference AcDream.Core).
    /// </summary>
    internal static class SmartBoxHooks
    {
        internal static Func<(int UseDoneSeq, uint UseDoneError, int ErrorSeq, uint Error, uint Event, uint Object)>? Replies { get; set; }

        public static int GetUseDoneSeq() => Replies?.Invoke().UseDoneSeq ?? 0;

        public static bool TryGetLastUseDone(out int seq, out uint error)
        {
            (seq, error) = (0, 0);
            if (Replies is not { } read) return false;
            var r = read();
            (seq, error) = (r.UseDoneSeq, r.UseDoneError);
            return seq != 0;
        }

        public static bool TryGetLastWeenieError(out int seq, out uint error, out uint eventType, out uint objectId)
        {
            (seq, error, eventType, objectId) = (0, 0, 0, 0);
            if (Replies is not { } read) return false;
            var r = read();
            (seq, error, eventType, objectId) = (r.ErrorSeq, r.Error, r.Event, r.Object);
            return seq != 0;
        }
    }

    /// <summary>RynthCore keeps each object's last appraisal; the host answers from the client's object table.</summary>
    internal static class AppraisalHooks
    {
        internal delegate bool AppraisalReader(uint guid, out RynthAppraisal appraisal);

        /// <summary>What one appraisal left: identified, spells, int/bool/float properties.</summary>
        internal sealed record RynthAppraisal(bool Identified, IReadOnlyList<uint> SpellIds,
            IReadOnlyDictionary<uint, int> Ints, IReadOnlyDictionary<uint, bool> Bools, IReadOnlyDictionary<uint, double> Floats);

        internal static AppraisalReader? Reader { get; set; }

        private static RynthAppraisal? Read(uint guid) => Reader is { } r && r(guid, out RynthAppraisal a) ? a : null;

        public static bool HasAppraisalData(uint guid) => Read(guid)?.Identified ?? false;

        public static int GetObjectSpellIds(uint guid, uint[] output, int maxCount)
        {
            if (Read(guid) is not { Identified: true } a) return 0;
            int n = Math.Min(Math.Min(maxCount, output.Length), a.SpellIds.Count);
            for (int i = 0; i < n; i++) output[i] = a.SpellIds[i];
            return n;
        }

        public static bool TryGetCachedIntProperty(uint guid, uint stype, out int value)
        {
            value = 0;
            return Read(guid) is { Identified: true } a && a.Ints.TryGetValue(stype, out value);
        }

        public static bool TryGetCachedBoolProperty(uint guid, uint stype, out bool value)
        {
            value = false;
            return Read(guid) is { Identified: true } a && a.Bools.TryGetValue(stype, out value);
        }

        public static bool TryGetCachedDoubleProperty(uint guid, uint stype, out double value)
        {
            value = 0;
            return Read(guid) is { Identified: true } a && a.Floats.TryGetValue(stype, out value);
        }
    }

    /// <summary>RynthCore calls acclient's RequestId (0xC8); the host sends the client's identify.</summary>
    internal static class ClientActionHooks
    {
        internal static Func<uint, bool>? Identify { get; set; }

        public static bool RequestId(uint objectId) => objectId != 0 && (Identify?.Invoke(objectId) ?? false);
    }

    /// <summary>
    /// RynthCore hooks acclient's incoming chat (VaultFeed reads Aelrynth's mule replies from it).
    /// VaultFeed never runs in OpenAC (ServerInfo.IsAelrynth is false), so never installed.
    /// </summary>
    internal static class ChatCallbackHooks
    {
        public static bool IncomingInstalled => false;
    }

    /// <summary>
    /// RynthCore sends "@" server commands through acclient's chat; only VaultFeed's Aelrynth
    /// commands use it here, and VaultFeed never runs in OpenAC, so nothing is sent.
    /// </summary>
    internal static class ChatCommandDispatcher
    {
        public static bool SendServerCommand(string atCommand) => false;
    }

    /// <summary>RynthCore watches acclient's logout; only VaultFeed reads it here (never runs in OpenAC), so never.</summary>
    internal static class LogoutLifecycleHooks
    {
        public static bool HasObservedLogout => false;
    }
}

namespace RynthCore.Engine.Net
{
    /// <summary>
    /// RynthCore reads each container's order from the server's messages. OpenAC keeps each
    /// item's ContainerSlot; the host lists a container's contents with their slots and pack-slot flag.
    /// </summary>
    internal static class InventoryOrder
    {
        internal readonly record struct Placed(uint Id, int Slot, bool PackSlot);

        internal static Func<uint, IReadOnlyList<Placed>?>? Source { get; set; }
        internal static Func<ulong>? Revision { get; set; }

        public static int Version => unchecked((int)(Revision?.Invoke() ?? 0));

        public static bool TryGetOrder(uint container, bool slotKind, out uint[] ids)
        {
            ids = Array.Empty<uint>();
            if (Source?.Invoke(container) is not { } placed)
                return false;
            var kind = new List<Placed>();
            foreach (Placed p in placed)
            {
                if (p.PackSlot != slotKind) continue;
                if (p.Slot < 0) return false;   // not every place known: their first-empty-slot fallback
                kind.Add(p);
            }
            kind.Sort((a, b) => a.Slot.CompareTo(b.Slot));
            ids = kind.ConvertAll(p => p.Id).ToArray();
            return true;
        }
    }
}

namespace RynthCore.Engine.ImGuiBackend
{
    /// <summary>RynthCore switches its D3D9 sampler to point for crisp icons; the overlay's own sampling is kept.</summary>
    internal static class IconSampling
    {
        public static void Begin(ImDrawListPtr dl) { }
        public static void End(ImDrawListPtr dl) { }
    }

    /// <summary>RynthCore hands an item dragged out of its panel to acclient's drag; OpenAC has no such hand-off.</summary>
    internal static class ItemDragBridge
    {
        public static void NotePopOutDrag(uint itemId) { }
        public static void EndPopOutDrag() { }
    }

    /// <summary>RynthCore makes D3D9 textures; here the overlay's own upload (device unused). DXT1 (World Map packs) is decoded to RGBA first.</summary>
    internal static class DX9Backend
    {
        internal static Func<byte[], int, int, IntPtr>? Upload { get; set; }
        internal static Func<IntPtr, bool>? Release { get; set; }

        public static IntPtr CreateTextureRgba(IntPtr pDevice, int width, int height, ReadOnlySpan<byte> rgba) =>
            Upload?.Invoke(rgba.ToArray(), width, height) ?? IntPtr.Zero;

        public static IntPtr CreateTextureDxt1(IntPtr pDevice, int width, int height, byte[] blocks) =>
            Upload is { } upload ? upload(RynthCore.Imaging.DxtUtil.DecompressDxt1(blocks, width, height), width, height) : IntPtr.Zero;

        public static void ReleaseTexture(IntPtr tex) => Release?.Invoke(tex);
    }
}

namespace RynthCore.Engine
{
    /// <summary>RynthCore's engine stop signal; the host sets it while shutting down.</summary>
    internal static class EngineThreads
    {
        internal static volatile bool StoppingFlag;
        public static bool Stopping => StoppingFlag;
    }
}
