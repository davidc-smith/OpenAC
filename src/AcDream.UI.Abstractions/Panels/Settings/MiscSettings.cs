namespace AcDream.UI.Abstractions.Panels.Settings;

/// <param name="ExtendedLevels">
/// Measure levels past the experience table on ACECustom's curve, for servers
/// that raise characters beyond 275. Off on retail-style servers, where the
/// table is the whole curve.
/// </param>
/// <param name="ModernHud">
/// Replace the retail vitals, combat and jump windows with the Command Deck.
/// </param>
/// <param name="SpellDeckClickCasts">
/// The Spell Deck casts on a single click instead of outlining; double-click casts either way when off.
/// </param>
public sealed record MiscSettings(
    bool TooltipEnable,
    float TooltipDelaySeconds,
    bool ExtendedLevels = false,
    bool ModernHud = false,
    bool SpellDeckClickCasts = false)
{
    public static MiscSettings Default { get; } = new(
        TooltipEnable: true,
        TooltipDelaySeconds: 0.25f);
}
