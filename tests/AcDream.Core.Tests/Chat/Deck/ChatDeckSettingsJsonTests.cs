using System.Text.Json.Nodes;
using AcDream.Core.Chat.Deck;

namespace AcDream.Core.Tests.Chat.Deck;

public sealed class ChatDeckSettingsJsonTests
{
    private static ChatDeckSettings Sample()
    {
        var d = ChatDeckDefaults.Create();
        return d with
        {
            Rules =
            [
                .. d.Rules,
                new ChatDeckRule { Id = "r1", Kind = ChatDeckMatchKind.Regex, Pattern = "^x", Field = ChatDeckMatchField.Line,
                    Channels = new HashSet<ChatDeckChannel> { ChatDeckChannel.Trade }, Action = ChatDeckRuleAction.CopyTo,
                    TargetTabId = "group", Colour = 0x80112233, Alert = true, Faulted = true },
                new ChatDeckRule { Id = "p1", Kind = ChatDeckMatchKind.Preset, PresetId = "future.preset", Action = ChatDeckRuleAction.Hide },
            ],
            Appearance = d.Appearance with
            {
                FontSize = 17, LineSpacing = 1.4f, Timestamps = ChatDeckTimestamps.HourMinuteSecond, ChannelPrefix = false,
                ChannelColours = new Dictionary<ChatDeckChannel, uint> { [ChatDeckChannel.Say] = 0xFFE0E0E0 },
                FadeWhenIdle = true, BeepOnAlert = true,
            },
            Layout = new ChatDeckLayoutState { Pinned = false, Edge = ChatDeckEdge.Right, LastTabId = "group" },
        };
    }

    [Fact]
    public void RoundTrip()
    {
        ChatDeckSettings s = Sample();
        var (back, error) = ChatDeckSettingsJson.Parse(ChatDeckSettingsJson.Serialize(s, previousJson: null));
        Assert.Null(error);
        Assert.Equal(s.Tabs.Count, back.Tabs.Count);
        for (int i = 0; i < s.Tabs.Count; i++)
        {
            Assert.Equal(s.Tabs[i] with { Channels = ChatDeckRule.AnyChannel }, back.Tabs[i] with { Channels = ChatDeckRule.AnyChannel });
            Assert.True(s.Tabs[i].Channels.SetEquals(back.Tabs[i].Channels));
        }
        Assert.Equal(s.Rules.Count, back.Rules.Count);
        for (int i = 0; i < s.Rules.Count; i++)
        {
            Assert.Equal(s.Rules[i] with { Channels = ChatDeckRule.AnyChannel }, back.Rules[i] with { Channels = ChatDeckRule.AnyChannel });
            Assert.True(s.Rules[i].Channels.SetEquals(back.Rules[i].Channels));
        }
        var noColours = new Dictionary<ChatDeckChannel, uint>();   // one instance: records compare dictionaries by reference
        Assert.Equal(s.Appearance with { ChannelColours = noColours }, back.Appearance with { ChannelColours = noColours });
        Assert.Equal(s.Appearance.ChannelColours, back.Appearance.ChannelColours);
        Assert.Equal(s.Layout, back.Layout);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Missing_GivesDefaultsWithoutError(string? json)
    {
        var (s, error) = ChatDeckSettingsJson.Parse(json);
        Assert.Null(error);
        Assert.Equal(ChatDeckDefaults.Create().Tabs.Select(t => t.Id), s.Tabs.Select(t => t.Id));
    }

    [Theory]
    [InlineData("{ not json")]
    [InlineData("[1,2]")]
    public void Bad_GivesDefaultsWithError(string json)
    {
        var (s, error) = ChatDeckSettingsJson.Parse(json);
        Assert.NotNull(error);
        Assert.Equal(6, s.Tabs.Count);
    }

    [Fact]
    public void TooLarge_GivesDefaultsWithError()
    {
        string json = "{\"pad\":\"" + new string('x', ChatDeckLimits.MaxSettingsJsonLength) + "\"}";
        Assert.NotNull(ChatDeckSettingsJson.Parse(json).Error);
    }

    [Fact]
    public void UnknownKeys_Survive()
    {
        string previous = ChatDeckSettingsJson.Serialize(Sample(), null);
        var node = JsonNode.Parse(previous)!.AsObject();
        node["future"] = 1;
        node["tabs"]![1]!["future"] = "t";
        node["rules"]![1]!["future"] = "r";
        node["appearance"]!["future"] = true;
        node["layout"]!["future"] = 2;

        var (s, _) = ChatDeckSettingsJson.Parse(node.ToJsonString());
        var again = JsonNode.Parse(ChatDeckSettingsJson.Serialize(s, node.ToJsonString()))!.AsObject();

        Assert.Equal(1, (int)again["future"]!);
        Assert.Equal("t", (string)again["tabs"]![1]!["future"]!);
        Assert.Equal("r", (string)again["rules"]![1]!["future"]!);
        Assert.True((bool)again["appearance"]!["future"]!);
        Assert.Equal(2, (int)again["layout"]!["future"]!);
    }

    [Fact]
    public void UnknownEnums_DroppedOrDefaulted_VersionKept()
    {
        string json = """
        { "version": 3,
          "tabs": [ { "id": "all", "name": "All" },
                    { "id": "x", "name": "X", "icon": "Dragon", "channels": ["Say", "Telepathy"], "send": "Shout" } ],
          "rules": [], "layout": { "edge": "Top" } }
        """;
        var (s, error) = ChatDeckSettingsJson.Parse(json);
        Assert.Null(error);
        Assert.Equal(3, s.Version);
        ChatDeckTab x = s.Tabs.Single(t => t.Id == "x");
        Assert.Equal(ChatDeckIcon.All, x.Icon);
        Assert.True(x.Channels.SetEquals([ChatDeckChannel.Say]));
        Assert.Equal(ChatDeckSendChannel.Say, x.SendChannel);
        Assert.Equal(ChatDeckEdge.Left, s.Layout.Edge);
    }

    [Theory]
    [InlineData("{\"version\":1,\"version\":2}")]
    [InlineData("{\"tabs\":[{\"id\":\"a\",\"id\":\"b\"}]}")]
    public void DuplicateKeys_GiveDefaultsWithError_AndSerializeDoesNotThrow(string json)
    {
        var (s, error) = ChatDeckSettingsJson.Parse(json);
        Assert.NotNull(error);
        Assert.Equal(6, s.Tabs.Count);
        Assert.NotNull(ChatDeckSettingsJson.Serialize(s, json));
    }

    private const string FutureRule =
        """{"version":1,"rules":[{"id":"n1","enabled":true,"kind":"Contains","pattern":"x","field":"Text","action":"Notify","target":"","colour":"#FF112233"}]}""";

    [Fact]
    public void RuleWithUnknownAction_LoadsDisabledAndKeepsItsValueOnSave()
    {
        var (settings, error) = ChatDeckSettingsJson.Parse(FutureRule);
        Assert.Null(error);
        ChatDeckRule rule = settings.Rules.Single(r => r.Id == "n1");
        Assert.False(rule.Enabled);

        var saved = (JsonObject)JsonNode.Parse(ChatDeckSettingsJson.Serialize(settings, FutureRule))!;
        JsonObject written = saved["rules"]!.AsArray().OfType<JsonObject>().Single(o => (string?)o["id"] == "n1");
        Assert.Equal("Notify", (string?)written["action"]);
        Assert.Equal("Contains", (string?)written["kind"]);
        Assert.True((bool?)written["enabled"]);
    }

    [Fact]
    public void RepairedRuleWithUnknownAction_SavesHideAndReloadsEnabled()
    {
        var (settings, _) = ChatDeckSettingsJson.Parse(FutureRule);
        Assert.False(settings.Rules.Single(r => r.Id == "n1").Enabled);

        ChatDeckSettings repaired = settings with
        {
            Rules = settings.Rules.Select(r => r.Id == "n1" ? r with { Enabled = true } : r).ToList(),
        };
        string json = ChatDeckSettingsJson.Serialize(repaired, FutureRule);

        var saved = (JsonObject)JsonNode.Parse(json)!;
        JsonObject written = saved["rules"]!.AsArray().OfType<JsonObject>().Single(o => (string?)o["id"] == "n1");
        Assert.Equal("Hide", (string?)written["action"]);
        Assert.True((bool?)written["enabled"]);
        var (reloaded, error) = ChatDeckSettingsJson.Parse(json);
        Assert.Null(error);
        ChatDeckRule rule = reloaded.Rules.Single(r => r.Id == "n1");
        Assert.True(rule.Enabled);
        Assert.Equal(ChatDeckRuleAction.Hide, rule.Action);
    }

    [Fact]
    public void RepairedRuleWithUnknownKind_SavesContainsAndReloadsEnabled()
    {
        const string source = """{"rules":[{"id":"n1","enabled":true,"kind":"Fuzzy","action":"Hide","pattern":"x"}]}""";
        var (settings, _) = ChatDeckSettingsJson.Parse(source);
        Assert.False(settings.Rules.Single(r => r.Id == "n1").Enabled);

        ChatDeckSettings repaired = settings with
        {
            Rules = settings.Rules.Select(r => r.Id == "n1" ? r with { Enabled = true } : r).ToList(),
        };
        string json = ChatDeckSettingsJson.Serialize(repaired, source);

        var saved = (JsonObject)JsonNode.Parse(json)!;
        JsonObject written = saved["rules"]!.AsArray().OfType<JsonObject>().Single(o => (string?)o["id"] == "n1");
        Assert.Equal("Contains", (string?)written["kind"]);
        Assert.True((bool?)written["enabled"]);
        ChatDeckRule rule = ChatDeckSettingsJson.Parse(json).Settings.Rules.Single(r => r.Id == "n1");
        Assert.True(rule.Enabled);
        Assert.Equal(ChatDeckMatchKind.Contains, rule.Kind);
    }

    [Fact]
    public void RuleWithUnknownKindOrField_LoadsDisabledAndKeepsThem()
    {
        const string json = """{"rules":[{"id":"n1","kind":"Fuzzy","field":"Nick","action":"Hide","pattern":"x"}]}""";
        var (settings, _) = ChatDeckSettingsJson.Parse(json);
        Assert.False(settings.Rules.Single(r => r.Id == "n1").Enabled);
        var saved = (JsonObject)JsonNode.Parse(ChatDeckSettingsJson.Serialize(settings, json))!;
        JsonObject written = saved["rules"]!.AsArray().OfType<JsonObject>().Single(o => (string?)o["id"] == "n1");
        Assert.Equal("Fuzzy", (string?)written["kind"]);
        Assert.Equal("Nick", (string?)written["field"]);
    }

    [Fact]
    public void RuleWithoutAction_LoadsDisabled()
    {
        var (settings, _) = ChatDeckSettingsJson.Parse("""{"rules":[{"id":"n1","kind":"Contains","field":"Text","pattern":"x"}]}""");
        Assert.False(settings.Rules.Single(r => r.Id == "n1").Enabled);
    }

    [Fact]
    public void RuleWithoutColour_GetsVisibleDefault()
    {
        var (settings, _) = ChatDeckSettingsJson.Parse(
            """{"rules":[{"id":"c1","kind":"Contains","field":"Text","action":"ColourLine","pattern":"x","colour":"nope"}]}""");
        Assert.Equal(ChatDeckDefaults.MyNameColour, settings.Rules.Single(r => r.Id == "c1").Colour);
    }

    [Fact]
    public void Layout_RoundTrips()
    {
        ChatDeckSettings settings = ChatDeckDefaults.Create() with
        {
            Layout = new ChatDeckLayoutState { Pinned = false, Edge = ChatDeckEdge.Right, LastTabId = "group" },
        };
        ChatDeckSettings back = ChatDeckSettingsJson.Parse(ChatDeckSettingsJson.Serialize(settings, null)).Settings;
        Assert.Equal(settings.Layout, back.Layout);
    }
}
