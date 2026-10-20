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
