namespace AcDream.UI.Abstractions.Panels.Settings;

/// <param name="ExtendedLevels">
/// Measure levels past the experience table on ACECustom's curve, for servers
/// that raise characters beyond 275. Off on retail-style servers, where the
/// table is the whole curve.
/// </param>
public sealed record MiscSettings(
    bool TooltipEnable,
    float TooltipDelaySeconds,
    bool ExtendedLevels = false)
{
    public static MiscSettings Default { get; } = new(
        TooltipEnable: true,
        TooltipDelaySeconds: 0.25f);
}
