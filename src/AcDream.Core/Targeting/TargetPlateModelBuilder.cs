using System.Globalization;
using AcDream.Core.Combat;
using AcDream.Core.Items;
using AcDream.Core.Properties;

namespace AcDream.Core.Targeting;

public sealed record TargetPlateInput(
    ClientObject? Object, uint PlayerId, int PlayerLevel, CombatState Combat, TargetAppraisalLog Log,
    double Now, float? DistanceMeters, uint SelectedPackItemId, Func<uint, bool> IsFellow,
    IReadOnlyList<TargetPluginRow> PluginRows, string? PluginOverflowTooltip,
    IReadOnlyList<TargetPluginAction> PluginActions, Func<uint, string?> NameOf);

public static class TargetPlateModelBuilder
{
    public const double StaleAfterSeconds = 10.0;
    // The pack item is the source of a pending use-with-target, so the hint
    // says to start the use from the pack, not merely to select an item.
    private const string NoPackItem = "Use an item from your pack first, then pick this";
    private const string NoPackKey = "Use a key or lockpick from your pack first, then pick this";

    public static TargetPlateModel? Build(TargetPlateInput input)
    {
        ClientObject? obj = input.Object;
        if (obj is null)
            return null;
        TargetClassification c = TargetKindClassifier.Classify(obj, input.PlayerId);
        bool appraised = input.Log.TryGet(obj.ObjectId, out TargetAppraisalEntry entry);
        int? level = obj.Properties.Ints.TryGetValue((uint)PropertyInt.Level, out int lv) && lv > 0 ? lv : null;
        bool hasPackItem = input.SelectedPackItemId != 0u;
        var rows = new List<TargetRow>();
        var chips = new List<TargetChip>();
        TargetVitals? vitals = null;
        TargetStud[] studs;
        TargetAction[] more = { TargetAction.Examine, TargetAction.CopyName };
        string kindWord;
        bool needsAssess = false;
        string? owner = null;

        TargetStud Give() => new(TargetAction.Give, hasPackItem, hasPackItem ? null : NoPackItem);
        TargetStud UseOn() => new(TargetAction.UseItemOn, hasPackItem, hasPackItem ? null : NoPackItem);
        TargetStud Key() => new(TargetAction.UseKey, hasPackItem, hasPackItem ? null : NoPackKey);
        TargetStud S(TargetAction a) => new(a, true);

        switch (c.Kind)
        {
            case TargetKind.Monster:
                kindWord = "MONSTER";
                vitals = Vitals(input, obj.ObjectId, appraised, entry);
                studs = new[] { S(TargetAction.Assess), UseOn(), S(TargetAction.Follow), S(TargetAction.More) };
                break;
            case TargetKind.Summon:
                kindWord = "SUMMON";
                if (c.OwnedByPlayer) chips.Add(TargetChip.Yours);
                else if (obj.PetOwnerId != 0u && input.NameOf(obj.PetOwnerId) is string ownerName)
                    owner = ownerName;
                vitals = Vitals(input, obj.ObjectId, appraised, entry);
                studs = new[] { S(TargetAction.Assess), S(TargetAction.Follow), S(TargetAction.More) };
                break;
            case TargetKind.Player:
                kindWord = "PLAYER";
                chips.Add(PkChip(obj));
                if (input.IsFellow(obj.ObjectId)) chips.Add(TargetChip.Fellow);
                if (appraised || input.IsFellow(obj.ObjectId))
                    vitals = Vitals(input, obj.ObjectId, appraised, entry);
                studs = new[] { S(TargetAction.Trade), Give(), S(TargetAction.Tell), S(TargetAction.More) };
                more = new[] { TargetAction.FellowshipInvite, TargetAction.Follow, TargetAction.SwearAllegiance,
                               TargetAction.Examine, TargetAction.CopyName };
                break;
            case TargetKind.Npc:
                kindWord = (PublicWeenieFlagsExtensions.FlagsOf(obj) & PublicWeenieFlags.Vendor) != 0 ? "VENDOR" : "NPC";
                studs = new[] { S(TargetAction.UseOrTalk), Give(), S(TargetAction.Follow), S(TargetAction.More) };
                break;
            case TargetKind.Item:
                kindWord = "ITEM";
                rows.Add(new("Value", obj.Value.ToString("N0", CultureInfo.InvariantCulture)));
                rows.Add(new("Burden", obj.Burden.ToString("N0", CultureInfo.InvariantCulture)));
                if (!appraised) { needsAssess = true; rows.Add(new("", "Assess for details", Dim: true)); }
                else AddItemDetails(obj, rows);
                studs = new[] { S(TargetAction.PickUpOrUse), S(TargetAction.Wield), S(TargetAction.More) };
                break;
            case TargetKind.Corpse:
                kindWord = "CORPSE";
                studs = new[] { S(TargetAction.Open), S(TargetAction.More) };
                break;
            case TargetKind.Container:
                kindWord = "CONTAINER";
                if (!appraised) { needsAssess = true; rows.Add(new("", "Assess for details", Dim: true)); }
                studs = new[] { S(TargetAction.Open), Key(), S(TargetAction.More) };
                break;
            case TargetKind.Portal:
                kindWord = "PORTAL";
                AddPortalLevels(obj, input.PlayerLevel, rows, appraised, ref needsAssess);
                studs = new[] { S(TargetAction.UsePortal), S(TargetAction.More) };
                break;
            case TargetKind.WorldObject:
                kindWord = c.WorldObject switch
                {
                    WorldObjectKind.Door => "DOOR", WorldObjectKind.LifeStone => "LIFESTONE",
                    WorldObjectKind.BindStone => "BINDSTONE", WorldObjectKind.Hook => "HOOK", _ => "OBJECT",
                };
                studs = c.WorldObject == WorldObjectKind.Door
                    ? new[] { S(TargetAction.Use), Key(), S(TargetAction.More) }
                    : new[] { S(TargetAction.Use), S(TargetAction.More) };
                break;
            case TargetKind.Self:
                kindWord = "YOU";
                studs = new[] { S(TargetAction.CharacterSheet), S(TargetAction.More) };
                more = new[] { TargetAction.Examine };
                break;
            default:
                return null;
        }

        string sub = c.Kind == TargetKind.Self
            ? (input.PlayerLevel > 0 ? string.Create(CultureInfo.InvariantCulture, $"LEVEL {input.PlayerLevel} · YOU") : "YOU")
            : (level is int l && (c.Kind is TargetKind.Monster or TargetKind.Summon or TargetKind.Player))
                ? string.Create(CultureInfo.InvariantCulture, $"LEVEL {l} · {kindWord}") : kindWord;
        // Another's summon names its owner on the sub line: kind rows are not
        // drawn while the plate shows vitals, which a summon always does.
        if (owner is not null)
            sub += " · " + owner;
        if (input.DistanceMeters is float d && c.Kind != TargetKind.Self)
            sub += string.Create(CultureInfo.InvariantCulture, $" · {MathF.Round(d):0} m");

        return new TargetPlateModel(obj.ObjectId, c.Kind, c.WorldObject, obj.Name, sub, level,
            Con(level, input.PlayerLevel, c.Kind), chips, vitals, rows, studs, more,
            input.PluginRows, input.PluginOverflowTooltip, input.PluginActions, needsAssess);
    }

    private static TargetVitals Vitals(TargetPlateInput input, uint id, bool appraised, TargetAppraisalEntry entry)
    {
        float health = input.Combat.GetHealthPercent(id);
        TargetVitalsState state = !appraised ? TargetVitalsState.Unknown
            : !entry.Success ? TargetVitalsState.Failed
            : input.Now - entry.At > StaleAfterSeconds ? TargetVitalsState.Stale
            : TargetVitalsState.Fresh;
        if (state is TargetVitalsState.Fresh or TargetVitalsState.Stale
            && input.Combat.TryGetCreatureVitals(id, out CombatState.CreatureVitals v))
        {
            float? Frac(uint? cur, uint? max) => cur is uint c && max is uint m && m > 0 ? Math.Clamp(c / (float)m, 0f, 1f) : null;
            return new(health, Frac(v.Stamina, v.StaminaMax), Frac(v.Mana, v.ManaMax), v.StaminaMax, v.ManaMax, state);
        }
        return new(health, null, null, null, null, state);
    }

    private static TargetCon Con(int? level, int playerLevel, TargetKind kind)
    {
        if (level is not int l || playerLevel <= 0 || kind is not (TargetKind.Monster or TargetKind.Summon))
            return TargetCon.Unknown;
        int diff = l - playerLevel;
        return diff >= 5 ? TargetCon.Higher : diff <= -5 ? TargetCon.Lower : TargetCon.Even;
    }

    private static TargetChip PkChip(ClientObject obj)
    {
        PublicWeenieFlags f = PublicWeenieFlagsExtensions.FlagsOf(obj);
        return (f & PublicWeenieFlags.PlayerKiller) != 0 ? TargetChip.Pk
            : (f & PublicWeenieFlags.PlayerKillerLite) != 0 ? TargetChip.PkLite
            : TargetChip.Npk;
    }

    private static void AddItemDetails(ClientObject obj, List<TargetRow> rows)
    {
        int work = obj.Properties.GetInt((uint)PropertyInt.ItemWorkmanship);
        int spells = obj.AppraisedSpellIds.Count;
        if (work > 0 || spells > 0)
            rows.Add(new("Work", spells > 0 ? $"{work} · {spells} spells" : $"{work}"));
        int wieldDiff = obj.Properties.GetInt((uint)PropertyInt.WieldDifficulty);
        if (wieldDiff > 0)
            rows.Add(new("Wield", wieldDiff.ToString(CultureInfo.InvariantCulture)));
    }

    private static void AddPortalLevels(ClientObject obj, int playerLevel, List<TargetRow> rows, bool appraised, ref bool needsAssess)
    {
        int min = obj.Properties.GetInt((uint)PropertyInt.MinLevel);
        int max = obj.Properties.GetInt((uint)PropertyInt.MaxLevel);
        if (min == 0 && max == 0)
        {
            if (!appraised) { needsAssess = true; rows.Add(new("", "Assess for details", Dim: true)); }
            return;
        }
        bool ok = (min == 0 || playerLevel >= min) && (max == 0 || playerLevel <= max);
        rows.Add(new("Levels", $"{(min == 0 ? 1 : min)} – {(max == 0 ? "any" : max.ToString(CultureInfo.InvariantCulture))}", Dim: !ok));
    }
}
