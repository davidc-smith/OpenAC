namespace AcDream.Rynth.Host;

/// <summary>The player's vitals as RynthCore's GetPlayerVitals reports them.</summary>
public readonly record struct RynthVitals(
    uint Health, uint MaxHealth, uint Stamina, uint MaxStamina, uint Mana, uint MaxMana);

/// <summary>One enchantment on the player; expiry in server time, <see cref="double.MaxValue"/> = permanent.</summary>
public readonly record struct RynthEnchantment(uint SpellId, double ExpiryServerTime);
