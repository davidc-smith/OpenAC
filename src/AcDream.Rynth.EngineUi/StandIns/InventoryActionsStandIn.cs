using System;
using System.Diagnostics;
using System.Threading;

namespace RynthCore.Engine.Compatibility
{
    /// <summary>
    /// RynthCore's InventoryModel item actions: one at a time, sent on acclient's main thread
    /// behind the client's item gate and watched until the server answers. OpenAC keeps their
    /// rules and texts for Use (the only kind the Mini Remote sends): the host's hooks send it
    /// through the same UseObject RynthAi's slot uses, "busy" is the client's busy count, and
    /// the answer is read from the UseDone / WeenieError counters. Other kinds are refused.
    /// </summary>
    internal static partial class InventoryModel
    {
        internal delegate bool ItemReader(uint id, out uint container, out uint wielder, out int stack);

        private static readonly long Freq = Stopwatch.Frequency;
        private static readonly long ActionTimeoutTicks = Freq * 6;   // no effect seen: stop waiting
        private static readonly long GateWaitTicks = Freq * 10;       // held for the game at most this long
        private const string BusyWithText = "another item action";

        /// <summary>Sends a use (the host: IRynthGameView.UseObject). Game thread.</summary>
        internal static Func<uint, bool>? UseSender { get; set; }

        /// <summary>The client has actions outstanding (BusyCount &gt; 0).</summary>
        internal static Func<bool>? Busy { get; set; }

        /// <summary>The server's UseDone and refusal counters.</summary>
        internal static Func<(int UseDoneSeq, uint UseDoneError, int ErrorSeq, uint Error)>? Replies { get; set; }

        /// <summary>An item's container, wielder and stack size; false when it isn't known.</summary>
        internal static ItemReader? ReadItem { get; set; }

        internal static Func<uint>? PlayerId { get; set; }

        /// <summary>Stopwatch ticks (tests replace it).</summary>
        internal static Func<long> Clock { get; set; } = Stopwatch.GetTimestamp;

        private sealed class InFlight
        {
            public InventoryRequest Request = null!;
            public long Started;
            public int UseDoneSeq, ErrorSeq;
            public uint Container, Wielder;
            public int Stack;
        }

        private static InventoryRequest? _requested;
        private static InventoryRequest? _held;
        private static long _heldSince;
        private static InFlight? _inFlight;
        private static InventoryPending? _pending;
        private static InventoryResult? _result;

        public static InventoryPending? Pending => Volatile.Read(ref _pending);

        public static InventoryResult? LastResult => Volatile.Read(ref _result);

        /// <summary>Their Request: refused (with the reason) while another action waits; nothing is queued.</summary>
        public static bool Request(InventoryRequest request, out string why)
        {
            InventoryPending? busy = Pending;
            if (busy != null)
            {
                why = (busy.WaitingForGame ? "Still waiting for the game: " : "Still waiting for the server: ") + busy.Label;
                return false;
            }
            if (Interlocked.CompareExchange(ref _requested, request, null) != null)
            {
                why = "Another item action is about to be sent.";
                return false;
            }
            why = string.Empty;
            return true;
        }

        /// <summary>Game thread, every dashboard tick: take a request, release a held one, watch the one in flight.</summary>
        internal static void MainThreadTick()
        {
            long now = Clock();
            if (_inFlight != null) Watch(now);
            else if (_held != null) TryReleaseHeld(now);
            InventoryRequest? r = Interlocked.Exchange(ref _requested, null);
            if (r != null) Begin(r, now);
        }

        private static string ActionLabel(InventoryRequest r) => "Using " + r.ItemName;

        private static void Begin(InventoryRequest r, long now)
        {
            if (_inFlight != null || _held != null) { Finish("Still waiting for the server.", false, now); return; }
            if (r.Kind != InventoryActionKind.Use) { Finish("Not available in OpenAC.", false, now); return; }
            if (!(Busy?.Invoke() ?? false)) { Send(r, now); return; }
            _held = r;
            _heldSince = now;
            Volatile.Write(ref _pending, new InventoryPending
            {
                Kind = r.Kind, ItemId = r.ItemId, Label = ActionLabel(r), StartedTicks = now,
                WaitingForGame = true, BusyWith = BusyWithText,
            });
        }

        private static void TryReleaseHeld(long now)
        {
            InventoryRequest r = _held!;
            if (!(Busy?.Invoke() ?? false))
            {
                _held = null;
                Volatile.Write(ref _pending, null);
                Send(r, now);
                return;
            }
            if (now - _heldSince >= GateWaitTicks)
            {
                _held = null;
                Volatile.Write(ref _pending, null);
                Finish($"Not sent: the game stayed busy with {BusyWithText}. Try again.", false, now);
            }
        }

        private static void Send(InventoryRequest r, long now)
        {
            uint player = PlayerId?.Invoke() ?? 0;
            if (player == 0) { Finish("Not in the world.", false, now); return; }
            if (ReadItem == null || !ReadItem(r.ItemId, out uint container, out uint wielder, out int stack))
            {
                Finish($"{r.ItemName} is gone.", false, now);
                return;
            }
            if (wielder != player && !(wielder == 0 && Carries(container, player)))
            {
                Finish($"{r.ItemName} is no longer in your packs.", false, now);
                return;
            }
            if (!(UseSender?.Invoke(r.ItemId) ?? false))
            {
                Finish($"Couldn't send: {ActionLabel(r)}.", false, now);
                return;
            }
            var replies = Replies?.Invoke() ?? default;
            _inFlight = new InFlight
            {
                Request = r, Started = now, UseDoneSeq = replies.UseDoneSeq, ErrorSeq = replies.ErrorSeq,
                Container = container, Wielder = wielder, Stack = stack,
            };
            Volatile.Write(ref _pending, new InventoryPending { Kind = r.Kind, ItemId = r.ItemId, Label = ActionLabel(r), StartedTicks = now });
        }

        /// <summary>The player's main pack, or a pack in it (their OwnedPack).</summary>
        private static bool Carries(uint container, uint player)
        {
            if (container == player) return true;
            return container != 0 && ReadItem != null && ReadItem(container, out uint outer, out _, out _) && outer == player;
        }

        private static void Watch(long now)
        {
            InFlight a = _inFlight!;
            InventoryRequest r = a.Request;
            var replies = Replies?.Invoke() ?? default;
            uint container = 0, wielder = 0;
            int stack = 0;
            bool exists = ReadItem != null && ReadItem(r.ItemId, out container, out wielder, out stack);
            string? done = null;
            bool ok = true;
            if (replies.ErrorSeq != a.ErrorSeq)
            {
                done = $"Refused: {ActionLabel(r)} (error 0x{replies.Error:X}).";
                ok = false;
            }
            else if (!exists)
                done = $"Used {r.ItemName}.";
            else if (replies.UseDoneSeq != a.UseDoneSeq)
            {
                ok = replies.UseDoneError == 0;
                done = ok ? $"Used {r.ItemName}." : $"Couldn't use {r.ItemName} (error 0x{replies.UseDoneError:X}).";
            }
            else if (container != a.Container || wielder != a.Wielder || stack != a.Stack)
                done = $"Used {r.ItemName}.";
            else if (now - a.Started >= ActionTimeoutTicks)
            {
                done = $"No answer seen for: {ActionLabel(r)}. You can act again.";
                ok = false;
            }
            if (done == null) return;
            _inFlight = null;
            Volatile.Write(ref _pending, null);
            Finish(done, ok, now);
        }

        private static void Finish(string text, bool ok, long now) =>
            Volatile.Write(ref _result, new InventoryResult { Text = text, Ok = ok, AtTicks = now });

        /// <summary>Forgets every request, held and in-flight action and the last result (host stop, tests).</summary>
        internal static void ResetActions()
        {
            Volatile.Write(ref _requested, null);
            _held = null;
            _inFlight = null;
            Volatile.Write(ref _pending, null);
            Volatile.Write(ref _result, null);
        }
    }
}
