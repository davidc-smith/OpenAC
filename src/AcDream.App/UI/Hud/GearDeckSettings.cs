using System.Text.Json.Nodes;
using AcDream.Core.Items.Gear;

namespace AcDream.App.UI.Hud;

internal enum GearPackView { Grid, List }

/// <summary>The expanded Equipment section: the 3D figure with the slot ring, or the 6-column slot grid.</summary>
internal enum GearEquipView { Figure, Slots }

/// <summary>The Gear Deck's per-character view state and saved filters. Unknown keys, and saved
/// filters this build cannot read, are kept and written back.</summary>
internal sealed record GearDeckSettings(bool Folded, GearPackView View, uint SelectedPack)
{
    public static GearDeckSettings Default { get; } = new(false, GearPackView.Grid, 0u);
    private JsonObject? Extra { get; init; }
    private JsonArray? UnreadFilters { get; init; }
    public IReadOnlyList<SavedFilter> SavedFilters { get; init; } = [];
    /// <summary>Figure or Slots when unfolded. Figure is the default, as the retail paper doll's doll view is.</summary>
    public GearEquipView Equip { get; init; } = GearEquipView.Figure;
    /// <summary>The figure's padlock: locked turns and zooms the body and lifts no gear; unlocked (the default)
    /// keeps the figure at rest and takes gear off.</summary>
    public bool FigureLocked { get; init; }
    /// <summary>Locked object ids, ascending.</summary>
    public IReadOnlyList<uint> LockedIds { get; init; } = [];
    public IReadOnlyList<GearPackRule> PackRules { get; init; } = [];
    private JsonArray? UnreadRules { get; init; }
    public bool CombineOnArrival { get; init; }

    public static GearDeckSettings FromJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return Default;
        try
        {
            if (JsonNode.Parse(json) is not JsonObject o) return Default;
            // A value of the wrong type reads as that setting's default; the others still load.
            bool folded = Read<bool>(o, "folded");
            GearPackView view = Read<string>(o, "view") == "list" ? GearPackView.List : GearPackView.Grid;
            uint pack = Read<uint>(o, "selectedPack");
            GearEquipView equip = Read<string>(o, "equip") == "slots" ? GearEquipView.Slots : GearEquipView.Figure;
            bool figureLocked = Read<bool>(o, "figureLocked");
            var saved = new List<SavedFilter>();
            JsonArray? unread = null;
            bool hasFilters = o["savedFilters"] is JsonArray;
            if (o["savedFilters"] is JsonArray filters)
                foreach (JsonNode? f in filters)
                {
                    if (GearQueryJson.TryRead(f, out SavedFilter? read)) saved.Add(read!);
                    else (unread ??= []).Add(f?.DeepClone());
                }
            uint[] locked = o["lockedIds"] is JsonArray ids
                ? ids.Select(n => n is JsonValue v && v.TryGetValue(out uint id) ? id : 0u).Where(id => id != 0u).Distinct().Order().ToArray()
                : [];
            bool combine = Read<bool>(o, "combineOnArrival");
            var rules = new List<GearPackRule>();
            JsonArray? unreadRules = null;
            bool hasRules = o["packRules"] is JsonArray;
            if (o["packRules"] is JsonArray ruleNodes)
                foreach (JsonNode? r in ruleNodes)
                {
                    if (TryReadRule(r, out GearPackRule? rule)) rules.Add(rule!);
                    else (unreadRules ??= []).Add(r?.DeepClone());
                }
            var extra = (JsonObject)o.DeepClone();
            foreach (string k in new[] { "version", "folded", "view", "selectedPack", "equip", "figureLocked", "lockedIds", "combineOnArrival" }) extra.Remove(k);
            if (hasFilters) extra.Remove("savedFilters");
            if (hasRules) extra.Remove("packRules");
            return new GearDeckSettings(folded, view, pack)
            {
                Extra = extra.Count > 0 ? extra : null,
                Equip = equip,
                FigureLocked = figureLocked,
                SavedFilters = saved,
                UnreadFilters = unread,
                LockedIds = locked,
                CombineOnArrival = combine,
                PackRules = rules,
                UnreadRules = unreadRules,
            };
        }
        catch (Exception) { return Default; }
    }

    private static bool TryReadRule(JsonNode? node, out GearPackRule? rule)
    {
        rule = null;
        if (node is not JsonObject r) return false;
        if (r["filter"] is not JsonValue f || !f.TryGetValue(out string? filter) || filter is null) return false;
        if (r["pack"] is not JsonValue p || !p.TryGetValue(out uint pack)) return false;
        bool arrival = r["arrival"] is JsonValue a && a.TryGetValue(out bool av) && av;
        bool on = r["on"] is not JsonValue e || !e.TryGetValue(out bool ev) || ev;
        rule = new GearPackRule(filter, pack, arrival, on);
        return true;
    }

    private static T? Read<T>(JsonObject o, string key) =>
        o[key] is JsonValue v && v.TryGetValue(out T? value) ? value : default;

    public string ToJson()
    {
        var o = Extra is null ? new JsonObject() : (JsonObject)Extra.DeepClone();
        o["version"] = 1;
        o["folded"] = Folded;
        o["view"] = View == GearPackView.List ? "list" : "grid";
        o["selectedPack"] = SelectedPack;
        o["equip"] = Equip == GearEquipView.Slots ? "slots" : "figure";
        o["figureLocked"] = FigureLocked;
        if (SavedFilters.Count > 0 || UnreadFilters is { Count: > 0 })
        {
            var filters = new JsonArray();
            foreach (SavedFilter f in SavedFilters) filters.Add(GearQueryJson.Write(f));
            if (UnreadFilters is not null) foreach (JsonNode? f in UnreadFilters) filters.Add(f?.DeepClone());
            o["savedFilters"] = filters;
        }
        if (LockedIds.Count > 0) o["lockedIds"] = new JsonArray(LockedIds.Select(id => (JsonNode?)JsonValue.Create(id)).ToArray());
        o["combineOnArrival"] = CombineOnArrival;
        if (PackRules.Count > 0 || UnreadRules is { Count: > 0 })
        {
            var rules = new JsonArray();
            foreach (GearPackRule r in PackRules)
                rules.Add(new JsonObject { ["filter"] = r.Filter, ["pack"] = r.PackId, ["arrival"] = r.OnArrival, ["on"] = r.Enabled });
            if (UnreadRules is not null) foreach (JsonNode? r in UnreadRules) rules.Add(r?.DeepClone());
            o["packRules"] = rules;
        }
        return o.ToJsonString();
    }

    public GearDeckSettings WithLock(IEnumerable<uint> ids, bool locked)
    {
        var set = new SortedSet<uint>(LockedIds);
        foreach (uint id in ids) { if (id == 0u) continue; if (locked) set.Add(id); else set.Remove(id); }
        return this with { LockedIds = set.ToArray() };
    }

    public GearDeckSettings WithRules(IReadOnlyList<GearPackRule> rules) => this with { PackRules = rules };

    /// <summary>Saves a filter: a same-named one (ignoring case) is replaced in place; a new one is added up to the cap.</summary>
    public GearDeckSettings WithSaved(SavedFilter filter)
    {
        string name = filter.Name.Trim();
        if (name.Length > SavedFilter.MaxNameLength) name = name[..SavedFilter.MaxNameLength].TrimEnd();
        if (name.Length == 0) return this;
        var named = filter with { Name = name };
        var list = SavedFilters.ToList();
        int i = list.FindIndex(f => string.Equals(f.Name, name, StringComparison.OrdinalIgnoreCase));
        if (i >= 0) list[i] = named;
        else if (list.Count >= SavedFilter.MaxCount) return this;
        else list.Add(named);
        return this with { SavedFilters = list };
    }

    /// <summary>Renames filter <paramref name="oldName"/> to <paramref name="filter"/>'s name and query, in place,
    /// and points the pack rules that named it at the new name. Unchanged when the old one is gone or the new
    /// name (ignoring case) belongs to another filter.</summary>
    public GearDeckSettings WithRenamed(string oldName, SavedFilter filter)
    {
        string name = filter.Name.Trim();
        if (name.Length > SavedFilter.MaxNameLength) name = name[..SavedFilter.MaxNameLength].TrimEnd();
        if (name.Length == 0) return this;
        var list = SavedFilters.ToList();
        int i = list.FindIndex(f => string.Equals(f.Name, oldName.Trim(), StringComparison.OrdinalIgnoreCase));
        if (i < 0) return this;
        int taken = list.FindIndex(f => string.Equals(f.Name, name, StringComparison.OrdinalIgnoreCase));
        if (taken >= 0 && taken != i) return this;
        string old = list[i].Name;
        list[i] = filter with { Name = name };
        return this with
        {
            SavedFilters = list,
            PackRules = PackRules
                .Select(r => string.Equals(r.Filter, old, StringComparison.OrdinalIgnoreCase) ? r with { Filter = name } : r)
                .ToList(),
        };
    }

    public GearDeckSettings WithoutSaved(string name) => this with
    {
        SavedFilters = SavedFilters.Where(f => !string.Equals(f.Name, name.Trim(), StringComparison.OrdinalIgnoreCase)).ToList(),
    };

    public bool Equals(GearDeckSettings? other) =>
        other is not null && Folded == other.Folded && View == other.View && SelectedPack == other.SelectedPack && Equip == other.Equip
        && FigureLocked == other.FigureLocked && CombineOnArrival == other.CombineOnArrival
        && LockedIds.SequenceEqual(other.LockedIds) && PackRules.SequenceEqual(other.PackRules)
        && SavedFilters.SequenceEqual(other.SavedFilters);
    public override int GetHashCode() => HashCode.Combine(Folded, View, SelectedPack, Equip, FigureLocked, SavedFilters.Count);
}
