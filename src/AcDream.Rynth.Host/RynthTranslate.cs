using AcDream.Plugin.Abstractions;

namespace AcDream.Rynth.Host;

/// <summary>OpenAC's plugin values in RynthCore's units.</summary>
public static class RynthTranslate
{
    /// <summary>RynthCore's combat modes (retail CombatMode flags).</summary>
    public const int NonCombat = 1, Melee = 2, Missile = 4, Magic = 8;

    public static int ToRynthCombatMode(PluginCombatMode mode) => mode switch
    {
        PluginCombatMode.Melee => Melee,
        PluginCombatMode.Missile => Missile,
        PluginCombatMode.Magic => Magic,
        _ => NonCombat,
    };

    public static PluginCombatMode? FromRynthCombatMode(int mode) => mode switch
    {
        NonCombat => PluginCombatMode.Peace,
        Melee => PluginCombatMode.Melee,
        Missile => PluginCombatMode.Missile,
        Magic => PluginCombatMode.Magic,
        _ => null,
    };
}
