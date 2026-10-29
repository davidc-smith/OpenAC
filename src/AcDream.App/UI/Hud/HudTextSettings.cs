using System.Text.Json.Nodes;

namespace AcDream.App.UI.Hud;

/// <summary>The Modern HUD decks, as the text size names them.</summary>
internal enum HudDeck { Command, Spell, Chat, Target, Gear }

/// <summary>
/// The Modern HUD's text size: one HUD-wide percent and any deck's own override.
/// Every percent is clamped to 80–150 and snapped to 10s, on load and on set.
/// "Same as HUD" is the absence of an override.
/// </summary>
internal sealed class HudTextSettings : IEquatable<HudTextSettings>
{
    public const int MinPercent = 80, MaxPercent = 150, StepPercent = 10, DefaultPercent = 100;

    /// <summary>Every size the menus and the Config row offer, smallest first.</summary>
    public static IReadOnlyList<int> Steps { get; } = [80, 90, 100, 110, 120, 130, 140, 150];

    public static HudTextSettings Default { get; } = new(DefaultPercent);

    private readonly Dictionary<HudDeck, int> _overrides = [];

    public HudTextSettings(int global, IReadOnlyDictionary<HudDeck, int>? overrides = null)
    {
        Global = Snap(global);
        if (overrides is null) return;
        foreach ((HudDeck deck, int percent) in overrides)
            if (Enum.IsDefined(deck)) _overrides[deck] = Snap(percent);
    }

    public int Global { get; }

    public IReadOnlyDictionary<HudDeck, int> Overrides => _overrides;

    /// <summary>Clamped to 80–150 and rounded to the nearest 10 (half away from zero).</summary>
    public static int Snap(int percent)
    {
        int stepped = (int)Math.Round(percent / (double)StepPercent, MidpointRounding.AwayFromZero) * StepPercent;
        return Math.Clamp(stepped, MinPercent, MaxPercent);
    }

    public bool HasOverride(HudDeck deck) => _overrides.ContainsKey(deck);

    /// <summary>The deck's override, or the HUD-wide size.</summary>
    public int PercentFor(HudDeck deck) => _overrides.TryGetValue(deck, out int p) ? p : Global;

    /// <summary>The deck's text factor: 1.0 at 100%.</summary>
    public float FactorFor(HudDeck deck) => PercentFor(deck) / 100f;

    public HudTextSettings WithGlobal(int percent) => new(percent, _overrides);

    /// <summary>Sets the deck's override; null ("Same as HUD") removes it.</summary>
    public HudTextSettings WithOverride(HudDeck deck, int? percent)
    {
        var next = new Dictionary<HudDeck, int>(_overrides);
        if (percent is int p) next[deck] = p;
        else next.Remove(deck);
        return new HudTextSettings(Global, next);
    }

    /// <summary>Reads <c>{"version":1,"global":N,"overrides":{"gear":N,…}}</c>; anything it cannot read is the defaults.</summary>
    public static HudTextSettings FromJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return Default;
        try
        {
            if (JsonNode.Parse(json) is not JsonObject o) return Default;
            int global = ReadInt(o["global"]) ?? DefaultPercent;
            var overrides = new Dictionary<HudDeck, int>();
            if (o["overrides"] is JsonObject map)
                foreach ((string key, JsonNode? value) in map)
                    if (TryDeck(key, out HudDeck deck) && ReadInt(value) is int p)
                        overrides[deck] = p;
            return new HudTextSettings(global, overrides);
        }
        catch (Exception) { return Default; }
    }

    public string ToJson()
    {
        var overrides = new JsonObject();
        foreach (HudDeck deck in Enum.GetValues<HudDeck>())
            if (_overrides.TryGetValue(deck, out int p)) overrides[Key(deck)] = p;
        return new JsonObject { ["version"] = 1, ["global"] = Global, ["overrides"] = overrides }.ToJsonString();
    }

    private static string Key(HudDeck deck) => deck.ToString().ToLowerInvariant();

    private static bool TryDeck(string key, out HudDeck deck)
    {
        foreach (HudDeck d in Enum.GetValues<HudDeck>())
        {
            if (!string.Equals(Key(d), key, StringComparison.Ordinal)) continue;
            deck = d;
            return true;
        }
        deck = default;
        return false;
    }

    private static int? ReadInt(JsonNode? node)
    {
        if (node is not JsonValue v) return null;
        if (v.TryGetValue(out int i)) return i;
        if (v.TryGetValue(out double d) && double.IsFinite(d))
            return (int)Math.Clamp(Math.Round(d), int.MinValue, int.MaxValue);
        return null;
    }

    public bool Equals(HudTextSettings? other) =>
        other is not null && Global == other.Global && _overrides.Count == other._overrides.Count
        && _overrides.All(kv => other._overrides.TryGetValue(kv.Key, out int p) && p == kv.Value);

    public override bool Equals(object? obj) => Equals(obj as HudTextSettings);

    public override int GetHashCode() => HashCode.Combine(Global, _overrides.Count);
}
