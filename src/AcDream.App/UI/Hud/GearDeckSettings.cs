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

    public static GearDeckSettings FromJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return Default;
        try
        {
            if (JsonNode.Parse(json) is not JsonObject o) return Default;
            bool folded = o["folded"]?.GetValue<bool>() ?? false;
            GearPackView view = (string?)o["view"] == "list" ? GearPackView.List : GearPackView.Grid;
            uint pack = o["selectedPack"]?.GetValue<uint>() ?? 0u;
            GearEquipView equip = (string?)o["equip"] == "slots" ? GearEquipView.Slots : GearEquipView.Figure;
            bool figureLocked = o["figureLocked"] is JsonValue v && v.TryGetValue(out bool locked) && locked;
            var saved = new List<SavedFilter>();
            JsonArray? unread = null;
            bool hasFilters = o["savedFilters"] is JsonArray;
            if (o["savedFilters"] is JsonArray filters)
                foreach (JsonNode? f in filters)
                {
                    if (GearQueryJson.TryRead(f, out SavedFilter? read)) saved.Add(read!);
                    else (unread ??= []).Add(f?.DeepClone());
                }
            var extra = (JsonObject)o.DeepClone();
            foreach (string k in new[] { "version", "folded", "view", "selectedPack", "equip", "figureLocked" }) extra.Remove(k);
            if (hasFilters) extra.Remove("savedFilters");
            return new GearDeckSettings(folded, view, pack)
            {
                Extra = extra.Count > 0 ? extra : null,
                Equip = equip,
                FigureLocked = figureLocked,
                SavedFilters = saved,
                UnreadFilters = unread,
            };
        }
        catch (Exception) { return Default; }
    }

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
        return o.ToJsonString();
    }

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

    public GearDeckSettings WithoutSaved(string name) => this with
    {
        SavedFilters = SavedFilters.Where(f => !string.Equals(f.Name, name.Trim(), StringComparison.OrdinalIgnoreCase)).ToList(),
    };

    public bool Equals(GearDeckSettings? other) =>
        other is not null && Folded == other.Folded && View == other.View && SelectedPack == other.SelectedPack && Equip == other.Equip
        && FigureLocked == other.FigureLocked
        && SavedFilters.SequenceEqual(other.SavedFilters);
    public override int GetHashCode() => HashCode.Combine(Folded, View, SelectedPack, Equip, FigureLocked, SavedFilters.Count);
}
