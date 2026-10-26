using AcDream.Core.Combat;
using AcDream.Runtime.Gameplay;

namespace AcDream.App.UI.Hud;

public enum DeckOrbMode { Peace, Melee, Missile, Magic }

public enum DeckContextRow { Power, Casting, Recall }

/// <summary>One vital as the game knows it: percent may be null until the max is known.</summary>
public readonly record struct DeckVitalInput(float? Percent, uint? Current, uint? Max);

/// <summary>Everything the deck reads, sampled once per frame.</summary>
public readonly record struct CommandDeckInput(
    DeckVitalInput Health,
    DeckVitalInput Stamina,
    DeckVitalInput Mana,
    CombatMode Mode,
    float DesiredPower,
    float PowerBarLevel,
    AttackHeight RequestedHeight,
    bool HasPendingCast,
    string? PendingSpellName,
    string? LastSpellName,
    JumpChargeSnapshot Jump,
    bool RepeatAttacks,
    bool AutoTarget,
    bool KeepInView,
    long SessionKey);

public readonly record struct DeckVitalView(float Fill, float Trail, string Text, bool Known);

/// <summary>What to draw this frame.</summary>
public sealed record CommandDeckView(
    DeckVitalView Health,
    DeckVitalView Stamina,
    DeckVitalView Mana,
    bool LowHealth,
    DeckOrbMode Orb,
    DeckContextRow Row,
    string PowerLabel,
    float DesiredPower,
    float PowerFill,
    AttackHeight Height,
    string CastingText,
    bool CastingLit,
    bool RecastEnabled,
    bool ShowStuds,
    bool RepeatAttacks,
    bool AutoTarget,
    bool KeepInView,
    float JumpFill,
    bool JumpCharging)
{
    public static CommandDeckView Empty { get; } = new CommandDeckModel().Update(default, 0.0);
}

/// <summary>Pure view logic for the Command Deck; owns the damage trails.</summary>
public sealed class CommandDeckModel
{
    public const float LowHealthThreshold = 0.25f;

    private readonly VitalTrail _health = new();
    private readonly VitalTrail _stamina = new();
    private readonly VitalTrail _mana = new();
    private long? _session;

    public CommandDeckView Update(in CommandDeckInput input, double now)
    {
        if (_session != input.SessionKey)
        {
            _session = input.SessionKey;
            _health.Reset();
            _stamina.Reset();
            _mana.Reset();
        }

        DeckOrbMode orb = input.Mode switch
        {
            CombatMode.Melee => DeckOrbMode.Melee,
            CombatMode.Missile => DeckOrbMode.Missile,
            CombatMode.Magic => DeckOrbMode.Magic,
            _ => DeckOrbMode.Peace,
        };
        DeckContextRow row = orb switch
        {
            DeckOrbMode.Melee or DeckOrbMode.Missile => DeckContextRow.Power,
            DeckOrbMode.Magic => DeckContextRow.Casting,
            _ => DeckContextRow.Recall,
        };

        (string castingText, bool castingLit) = input switch
        {
            { HasPendingCast: true, PendingSpellName: string name } => (name, true),
            { HasPendingCast: true } => ("Casting...", true),
            { LastSpellName: string last } => (last, false),
            _ => ("No spell cast yet", false),
        };

        return new CommandDeckView(
            Health: Vital(input.Health, _health, now),
            Stamina: Vital(input.Stamina, _stamina, now),
            Mana: Vital(input.Mana, _mana, now),
            LowHealth: input.Health.Percent is float hp && hp < LowHealthThreshold,
            Orb: orb,
            Row: row,
            PowerLabel: orb == DeckOrbMode.Missile ? "ACCURACY" : "POWER",
            DesiredPower: Math.Clamp(input.DesiredPower, 0f, 1f),
            PowerFill: Math.Clamp(input.PowerBarLevel, 0f, 1f),
            Height: input.RequestedHeight,
            CastingText: castingText,
            CastingLit: castingLit,
            RecastEnabled: !input.HasPendingCast && input.LastSpellName is not null,
            ShowStuds: row == DeckContextRow.Power,
            RepeatAttacks: input.RepeatAttacks,
            AutoTarget: input.AutoTarget,
            KeepInView: input.KeepInView,
            JumpFill: input.Jump.IsCharging ? Math.Clamp(input.Jump.Power, 0f, 1f) : 0f,
            JumpCharging: input.Jump.IsCharging);
    }

    private static DeckVitalView Vital(DeckVitalInput vital, VitalTrail trail, double now)
    {
        if (vital.Percent is not float percent)
        {
            trail.Update(null, now);
            return new DeckVitalView(0f, 0f, "-", Known: false);
        }

        float fill = Math.Clamp(percent, 0f, 1f);
        string text = (vital.Current, vital.Max) is (uint current, uint max)
            ? $"{current} / {max}"
            : $"{MathF.Round(fill * 100f):0}%";
        return new DeckVitalView(fill, trail.Update(fill, now), text, Known: true);
    }
}
