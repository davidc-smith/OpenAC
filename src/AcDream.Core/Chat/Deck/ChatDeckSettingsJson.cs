using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace AcDream.Core.Chat.Deck;

/// <summary>
/// The Chat Deck's saved settings as the <c>chatDeck</c> JSON object. Keys this build does
/// not know are carried over from the previous object, so an older or newer build loses nothing.
/// </summary>
public static class ChatDeckSettingsJson
{
    /// <summary>The settings to use, and why they are defaults when the saved ones could not be read.</summary>
    public readonly record struct ParseResult(ChatDeckSettings Settings, string? Error);

    public static ParseResult Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new(ChatDeckDefaults.Create(), null);
        if (json.Length > ChatDeckLimits.MaxSettingsJsonLength)
            return new(ChatDeckDefaults.Create(), $"Chat Deck settings are larger than {ChatDeckLimits.MaxSettingsJsonLength} characters.");

        try
        {
            if (ParseNode(json) is not JsonObject root)
                return new(ChatDeckDefaults.Create(), "Chat Deck settings are not a JSON object.");

            ChatDeckSettings defaults = ChatDeckDefaults.Create();
            var settings = new ChatDeckSettings
            {
                Version = Int(root, "version", ChatDeckSettings.CurrentVersion),
                Tabs = root["tabs"] is JsonArray tabs ? tabs.OfType<JsonObject>().Select(ReadTab).ToList() : defaults.Tabs,
                Rules = root["rules"] is JsonArray rules ? rules.OfType<JsonObject>().Select(ReadRule).ToList() : defaults.Rules,
                Appearance = root["appearance"] is JsonObject a ? ReadAppearance(a) : defaults.Appearance,
                Layout = root["layout"] is JsonObject l ? ReadLayout(l) : defaults.Layout,
            };
            return new(ChatDeckSettingsNormalizer.Normalize(settings), null);
        }
        catch (Exception ex) when (ex is JsonException or ArgumentException or InvalidOperationException)
        {
            return new(ChatDeckDefaults.Create(), $"Chat Deck settings are not valid JSON: {ex.Message}");
        }
    }

    public static string Serialize(ChatDeckSettings settings, string? previousJson)
    {
        JsonObject previous = TryParseObject(previousJson) ?? new JsonObject();
        JsonObject root = (JsonObject)previous.DeepClone();

        root["version"] = settings.Version;
        root["tabs"] = WriteArray(settings.Tabs, previous["tabs"] as JsonArray, t => t.Id, WriteTab);
        root["rules"] = WriteArray(settings.Rules, previous["rules"] as JsonArray, r => r.Id, WriteRule);
        root["appearance"] = WriteAppearance(settings.Appearance, Clone(previous["appearance"] as JsonObject));
        root["layout"] = WriteLayout(settings.Layout, Clone(previous["layout"] as JsonObject));
        return root.ToJsonString();
    }

    // ── reading ──────────────────────────────────────────────────────────────

    private static ChatDeckTab ReadTab(JsonObject o) => new()
    {
        Id = Str(o, "id", ""),
        Name = Str(o, "name", "Tab"),
        Icon = Enum(o, "icon", ChatDeckIcon.All),
        Channels = Channels(o, "channels"),
        SendChannel = Enum(o, "send", ChatDeckSendChannel.Say),
        Peek = Bool(o, "peek", false),
    };

    // A rule whose kind, field or action this build cannot read (missing, or written by a newer
    // build) loads switched off: running it on a guess could hide or recolour the wrong lines.
    private static ChatDeckRule ReadRule(JsonObject o) => new()
    {
        Id = Str(o, "id", ""),
        Enabled = Bool(o, "enabled", true) && !Unreadable(o),
        Kind = Enum(o, "kind", ChatDeckMatchKind.Contains),
        Pattern = Str(o, "pattern", ""),
        PresetId = Str(o, "presetId", ""),
        Field = Enum(o, "field", ChatDeckMatchField.Text),
        Channels = Channels(o, "channels"),
        Action = Enum(o, "action", ChatDeckRuleAction.Hide),
        TargetTabId = Str(o, "target", ""),
        Colour = Colour(o["colour"]) ?? ChatDeckDefaults.MyNameColour,
        Alert = Bool(o, "alert", false),
        Faulted = Bool(o, "faulted", false),
    };

    private static ChatDeckAppearance ReadAppearance(JsonObject o)
    {
        var d = new ChatDeckAppearance();
        var colours = new Dictionary<ChatDeckChannel, uint>();
        if (o["channelColours"] is JsonObject map)
            foreach (var (key, value) in map)
                if (System.Enum.TryParse(key, out ChatDeckChannel channel) && System.Enum.IsDefined(channel)
                    && Colour(value) is { } c)
                    colours[channel] = c;
        return new ChatDeckAppearance
        {
            FontSize = Int(o, "fontSize", d.FontSize),
            InputFontSize = Int(o, "inputFontSize", d.InputFontSize),
            LineSpacing = Float(o, "lineSpacing", d.LineSpacing),
            WrapIndent = Bool(o, "wrapIndent", d.WrapIndent),
            BackgroundOpacity = Float(o, "backgroundOpacity", d.BackgroundOpacity),
            Timestamps = Enum(o, "timestamps", d.Timestamps),
            ChannelPrefix = Bool(o, "channelPrefix", d.ChannelPrefix),
            ChannelColours = colours,
            FadeWhenIdle = Bool(o, "fadeWhenIdle", d.FadeWhenIdle),
            FadeAfterSeconds = Int(o, "fadeAfterSeconds", d.FadeAfterSeconds),
            BeepOnAlert = Bool(o, "beepOnAlert", d.BeepOnAlert),
        };
    }

    private static ChatDeckLayoutState ReadLayout(JsonObject o)
    {
        var d = new ChatDeckLayoutState();
        return new ChatDeckLayoutState
        {
            Pinned = Bool(o, "pinned", d.Pinned),
            Edge = Enum(o, "edge", d.Edge),
            LastTabId = Str(o, "lastTab", d.LastTabId),
        };
    }

    // ── writing ──────────────────────────────────────────────────────────────

    private static JsonArray WriteArray<T>(
        IReadOnlyList<T> items, JsonArray? previous, Func<T, string> id, Func<T, JsonObject, JsonObject> write)
    {
        var byId = new Dictionary<string, JsonObject>(StringComparer.Ordinal);
        if (previous is not null)
            foreach (JsonObject o in previous.OfType<JsonObject>())
                if (o["id"] is JsonValue v && v.TryGetValue(out string? key) && key is not null)
                    byId.TryAdd(key, o);
        var array = new JsonArray();
        foreach (T item in items)
            array.Add(write(item, byId.TryGetValue(id(item), out var old) ? (JsonObject)old.DeepClone() : new JsonObject()));
        return array;
    }

    private static JsonObject WriteTab(ChatDeckTab t, JsonObject o)
    {
        o["id"] = t.Id;
        o["name"] = t.Name;
        o["icon"] = t.Icon.ToString();
        o["channels"] = ChannelArray(t.Channels);
        o["send"] = t.SendChannel.ToString();
        o["peek"] = t.Peek;
        return o;
    }

    private static JsonObject WriteRule(ChatDeckRule r, JsonObject o)
    {
        // Keys this build could not read were loaded as fallbacks and the rule switched off;
        // while the rule still holds that fallback and is still off, leave the stored value for a build
        // that can. Loading always switches such a rule off, so an enabled one was deliberately repaired.
        bool keepKind = !r.Enabled && KeepUnreadable(o, "kind", r.Kind, ChatDeckMatchKind.Contains);
        bool keepField = !r.Enabled && KeepUnreadable(o, "field", r.Field, ChatDeckMatchField.Text);
        bool keepAction = !r.Enabled && KeepUnreadable(o, "action", r.Action, ChatDeckRuleAction.Hide);

        o["id"] = r.Id;
        if (!(keepKind || keepField || keepAction)) o["enabled"] = r.Enabled;
        if (!keepKind) o["kind"] = r.Kind.ToString();
        o["pattern"] = r.Pattern;
        o["presetId"] = r.PresetId;
        if (!keepField) o["field"] = r.Field.ToString();
        o["channels"] = ChannelArray(r.Channels);
        if (!keepAction) o["action"] = r.Action.ToString();
        o["target"] = r.TargetTabId;
        o["colour"] = ColourText(r.Colour);
        o["alert"] = r.Alert;
        o["faulted"] = r.Faulted;
        return o;
    }

    private static JsonObject WriteAppearance(ChatDeckAppearance a, JsonObject o)
    {
        o["fontSize"] = a.FontSize;
        o["inputFontSize"] = a.InputFontSize;
        o["lineSpacing"] = a.LineSpacing;
        o["wrapIndent"] = a.WrapIndent;
        o["backgroundOpacity"] = a.BackgroundOpacity;
        o["timestamps"] = a.Timestamps.ToString();
        o["channelPrefix"] = a.ChannelPrefix;
        var colours = new JsonObject();
        foreach (var (channel, colour) in a.ChannelColours.OrderBy(kv => kv.Key))
            colours[channel.ToString()] = ColourText(colour);
        o["channelColours"] = colours;
        o["fadeWhenIdle"] = a.FadeWhenIdle;
        o["fadeAfterSeconds"] = a.FadeAfterSeconds;
        o["beepOnAlert"] = a.BeepOnAlert;
        return o;
    }

    private static JsonObject WriteLayout(ChatDeckLayoutState l, JsonObject o)
    {
        o["pinned"] = l.Pinned;
        o["edge"] = l.Edge.ToString();
        o["lastTab"] = l.LastTabId;
        return o;
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    // Duplicate property names are rejected here, at parse time. Left alone, the tree builds its
    // lookup lazily and throws on the first index or enumerate, far from the guard.
    private static JsonNode? ParseNode(string json) =>
        JsonNode.Parse(json, documentOptions: new JsonDocumentOptions { AllowDuplicateProperties = false });

    private static JsonObject? TryParseObject(string? json)
    {
        if (string.IsNullOrWhiteSpace(json) || json.Length > ChatDeckLimits.MaxSettingsJsonLength) return null;
        try
        {
            return ParseNode(json) as JsonObject;
        }
        catch (Exception ex) when (ex is JsonException or ArgumentException or InvalidOperationException) { return null; }
    }

    private static JsonObject Clone(JsonObject? o) => o is null ? new JsonObject() : (JsonObject)o.DeepClone();

    private static JsonArray ChannelArray(IReadOnlySet<ChatDeckChannel> channels) =>
        new(channels.OrderBy(c => c).Select(c => (JsonNode)JsonValue.Create(c.ToString())).ToArray());

    private static IReadOnlySet<ChatDeckChannel> Channels(JsonObject o, string key)
    {
        var set = new HashSet<ChatDeckChannel>();
        if (o[key] is JsonArray array)
            foreach (JsonNode? n in array)
                if (n is JsonValue v && v.TryGetValue(out string? s)
                    && System.Enum.TryParse(s, out ChatDeckChannel c) && System.Enum.IsDefined(c))
                    set.Add(c);
        return set;
    }

    private static string Str(JsonObject o, string key, string fallback) =>
        o[key] is JsonValue v && v.TryGetValue(out string? s) && s is not null ? s : fallback;

    private static bool Bool(JsonObject o, string key, bool fallback) =>
        o[key] is JsonValue v && v.TryGetValue(out bool b) ? b : fallback;

    private static int Int(JsonObject o, string key, int fallback) =>
        o[key] is JsonValue v && v.TryGetValue(out int i) ? i : fallback;

    private static float Float(JsonObject o, string key, float fallback) =>
        o[key] is JsonValue v && v.TryGetValue(out double d) ? (float)d : fallback;

    private static TEnum Enum<TEnum>(JsonObject o, string key, TEnum fallback) where TEnum : struct, System.Enum =>
        o[key] is JsonValue v && v.TryGetValue(out string? s)
            && System.Enum.TryParse(s, out TEnum value) && System.Enum.IsDefined(value)
            ? value
            : fallback;

    private static bool Unreadable(JsonObject o) =>
        !IsDefined<ChatDeckMatchKind>(o, "kind") || !IsDefined<ChatDeckMatchField>(o, "field")
        || !IsDefined<ChatDeckRuleAction>(o, "action");

    private static bool IsDefined<TEnum>(JsonObject o, string key) where TEnum : struct, System.Enum =>
        o[key] is JsonValue v && v.TryGetValue(out string? s)
        && System.Enum.TryParse(s, out TEnum value) && System.Enum.IsDefined(value);

    // True when the stored value is present but unreadable and the rule still holds the fallback.
    private static bool KeepUnreadable<TEnum>(JsonObject o, string key, TEnum current, TEnum fallback)
        where TEnum : struct, System.Enum =>
        o.ContainsKey(key) && !IsDefined<TEnum>(o, key) && EqualityComparer<TEnum>.Default.Equals(current, fallback);

    private static string ColourText(uint argb) => "#" + argb.ToString("X8", CultureInfo.InvariantCulture);

    private static uint? Colour(JsonNode? node) =>
        node is JsonValue v && v.TryGetValue(out string? s) && s is { Length: 9 } && s[0] == '#'
            && uint.TryParse(s.AsSpan(1), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint argb)
            ? argb
            : null;
}
