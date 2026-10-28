namespace AcDream.Core.Targeting;

public enum TargetVitalsState { Unknown, Fresh, Stale, Failed }
public enum TargetCon { Unknown, Lower, Even, Higher }
public enum TargetChip { None, Yours, Fellow, Pk, Npk, PkLite }

/// <summary>The actions a plate can offer; the deck maps each to an icon and an action call.</summary>
public enum TargetAction
{
    None, Assess, UseItemOn, Follow, More, Trade, Give, Tell, FellowshipInvite,
    UseOrTalk, PickUpOrUse, Wield, Open, UseKey, UsePortal, Use, CharacterSheet,
    Examine, CopyName, SwearAllegiance, Deselect,
}

public readonly record struct TargetStud(TargetAction Action, bool Enabled, string? DisabledReason = null);
public readonly record struct TargetRow(string Label, string Value, bool Dim = false);
/// <summary>Fractions for the bars; the totals, when an assess gave them, for the text.</summary>
public readonly record struct TargetVitals(
    float Health, float? Stamina, float? Mana, uint? HealthMax,
    uint? StaminaCurrent, uint? StaminaMax, uint? ManaCurrent, uint? ManaMax, TargetVitalsState State);

/// <summary>A row a plugin added. Colour is RGBA, or null for the default ink.</summary>
public readonly record struct TargetPluginRow(string Label, string Value, uint? Rgba, string? Tooltip);

/// <summary>A ⋯ menu entry a plugin added, grouped under <paramref name="PluginName"/>.</summary>
public readonly record struct TargetPluginAction(string PluginId, string PluginName, string ActionId, string Label, bool Enabled);

public sealed record TargetPlateModel(
    uint ObjectId,
    TargetKind Kind,
    WorldObjectKind WorldObject,
    string Name,
    string SubLine,
    int? Level,
    TargetCon Con,
    IReadOnlyList<TargetChip> Chips,
    TargetVitals? Vitals,
    IReadOnlyList<TargetRow> Rows,
    IReadOnlyList<TargetStud> Studs,
    IReadOnlyList<TargetAction> MoreMenu,
    IReadOnlyList<TargetPluginRow> PluginRows,
    string? PluginOverflowTooltip,
    IReadOnlyList<TargetPluginAction> PluginActions,
    bool NeedsAssess);
