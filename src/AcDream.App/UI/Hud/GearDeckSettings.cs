using System.Text.Json.Nodes;

namespace AcDream.App.UI.Hud;

internal enum GearPackView { Grid, List }

/// <summary>The Gear Deck's per-character view state. Unknown keys from a newer build are kept and written back.</summary>
internal sealed record GearDeckSettings(bool Folded, GearPackView View, uint SelectedPack)
{
    public static GearDeckSettings Default { get; } = new(false, GearPackView.Grid, 0u);
    private JsonObject? Extra { get; init; }

    public static GearDeckSettings FromJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return Default;
        try
        {
            if (JsonNode.Parse(json) is not JsonObject o) return Default;
            bool folded = o["folded"]?.GetValue<bool>() ?? false;
            GearPackView view = (string?)o["view"] == "list" ? GearPackView.List : GearPackView.Grid;
            uint pack = o["selectedPack"]?.GetValue<uint>() ?? 0u;
            var extra = (JsonObject)o.DeepClone();
            foreach (string k in new[] { "version", "folded", "view", "selectedPack" }) extra.Remove(k);
            return new GearDeckSettings(folded, view, pack) { Extra = extra.Count > 0 ? extra : null };
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
        return o.ToJsonString();
    }

    public bool Equals(GearDeckSettings? other) =>
        other is not null && Folded == other.Folded && View == other.View && SelectedPack == other.SelectedPack;
    public override int GetHashCode() => HashCode.Combine(Folded, View, SelectedPack);
}
