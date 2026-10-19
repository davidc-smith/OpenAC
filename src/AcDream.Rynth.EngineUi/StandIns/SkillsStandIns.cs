using System;

namespace RynthCore.Engine.Compatibility
{
    /// <summary>RynthCore records acclient's main thread; OpenAC updates and draws on one thread, so every call is on it.</summary>
    internal static class MainThreadGuard
    {
        public static bool IsOnMainThread() => true;
    }

    /// <summary>RynthCore reads acclient's server clock; here the host answers the game view's.</summary>
    internal static class TimeSyncHooks
    {
        internal static Func<double>? ServerTime { get; set; }

        public static double GetCurrentServerTime() => ServerTime?.Invoke() ?? 0;
    }

    /// <summary>RynthCore copies the player's enchantment registry from acclient; here the host fills the arrays.</summary>
    internal static class EnchantmentHooks
    {
        internal delegate int StatModCopier(uint[] spell, double[] expiry, uint[] type, uint[] key, float[] value, uint[] category);

        internal static StatModCopier? Copier { get; set; }

        public static int CopyPlayerStatMods(uint[] spell, double[] expiry, uint[] type, uint[] key, float[] value, uint[] category) =>
            Copier?.Invoke(spell, expiry, type, key, value, category) ?? 0;
    }

    /// <summary>
    /// RynthCore calls acclient's CM_Train senders; here the host's sender requests the
    /// raise or training through OpenAC. Calls already run on the game thread, so defer is ignored.
    /// </summary>
    internal static class PlayerTraining
    {
        internal enum TrainKind : uint
        {
            Attribute = 1,
            Vital = 2,
            Skill = 3,
            TrainWithCredits = 4,
        }

        internal static Func<TrainKind, uint, uint, bool>? Sender { get; set; }

        public static bool Available => Sender is not null;

        public static bool TrainAvailable => Sender is not null;

        public static bool Raise(TrainKind kind, uint stype, uint xp, bool defer = false)
        {
            if (Sender is not { } send || xp == 0 || !send(kind, stype, xp))
                return false;
            PlayerProgressHooks.RefreshSoon();
            return true;
        }

        public static bool TrainSkill(uint stype, uint credits, bool defer = false) =>
            Raise(TrainKind.TrainWithCredits, stype, credits, defer);
    }

    /// <summary>RynthCore's Aelrynth mastery feed (a chat-line protocol); not on this server (ServerInfo.IsAelrynth is false).</summary>
    internal static class MasteryFeed
    {
        public static MasterySnapshot? Snapshot => null;

        public static bool Unsupported => false;

        public static bool NoChatHook => false;

        public static bool Awaiting => false;

        public static string Status => "not available in OpenAC";

        public static void PanelOpened()
        {
        }

        public static void RequestRefresh()
        {
        }

        public static bool QueueRaise(MasterySkill skill, int count, out string why)
        {
            why = "Mastery is not available in OpenAC";
            return false;
        }

        public static long EstimateRadiance(MasterySnapshot s) => 0;
    }
}

namespace RynthCore.Engine.UI.Data
{
    /// <summary>RynthCore relays these to the RynthILT plugin; OpenAC loads no RynthILT.</summary>
    internal static class IltCommands
    {
        public static void Progression(string value)
        {
        }
    }
}
