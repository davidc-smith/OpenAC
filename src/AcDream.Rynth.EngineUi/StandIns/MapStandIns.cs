using System;

namespace RynthCore.Engine.Compatibility
{
    /// <summary>RynthCore's player vitals snapshot (their PlayerVitalsHooks.cs, the record only).</summary>
    internal readonly record struct PlayerVitalsSnapshot(
        uint Health,
        uint MaxHealth,
        uint Stamina,
        uint MaxStamina,
        uint Mana,
        uint MaxMana);

    /// <summary>RynthCore reads the vitals from acclient's qualities; here the host's game view.</summary>
    internal static class PlayerVitalsHooks
    {
        internal delegate bool SnapshotReader(out PlayerVitalsSnapshot snapshot);

        internal static SnapshotReader? Reader { get; set; }

        public static bool TryGetSnapshot(out PlayerVitalsSnapshot snapshot)
        {
            snapshot = default;
            return Reader is { } read && read(out snapshot);
        }
    }

    /// <summary>RynthCore watches acclient's teleport state; here the host's game view.</summary>
    internal static class TeleportStateHooks
    {
        internal static Func<bool>? Portaling { get; set; }

        public static bool IsPortaling => Portaling?.Invoke() ?? false;
    }
}

namespace RynthCore.Engine.ImGuiBackend.Hud
{
    /// <summary>RynthCore's health reading (their MonsterHudData.cs, the record only).</summary>
    internal readonly record struct HealthObservation(float Ratio, uint Current, uint Max, long Ticks, float Prev = -1f, bool Estimated = false);

    /// <summary>RynthCore keeps every health update it sees; here the host answers from the client's latest reading.</summary>
    internal static class MonsterHudData
    {
        internal delegate bool HealthReader(uint objectId, out uint current, out uint max);

        internal static HealthReader? Reader { get; set; }

        public static bool TryGetHealth(uint id, out HealthObservation h)
        {
            h = default;
            if (Reader is not { } read || !read(id, out uint current, out uint max) || max == 0)
                return false;
            h = new HealthObservation(current / (float)max, current, max, Environment.TickCount64);
            return true;
        }
    }
}
