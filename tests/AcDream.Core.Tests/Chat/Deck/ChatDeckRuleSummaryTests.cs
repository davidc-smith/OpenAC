using AcDream.Core.Chat.Deck;

namespace AcDream.Core.Tests.Chat.Deck;

public sealed class ChatDeckRuleSummaryTests
{
    private static readonly IReadOnlyList<ChatDeckTab> Tabs = ChatDeckDefaults.Create().Tabs;

    private static ChatDeckRule R(ChatDeckMatchKind kind = ChatDeckMatchKind.Contains, string pattern = "loot",
        ChatDeckMatchField field = ChatDeckMatchField.Text) =>
        new() { Id = "r", Kind = kind, Pattern = pattern, Field = field };

    [Fact]
    public void When_DescribesEachKind()
    {
        Assert.Equal("Text contains \"loot\"", ChatDeckRuleSummary.When(R()));
        Assert.Equal("Line matches /x+/", ChatDeckRuleSummary.When(R(ChatDeckMatchKind.Regex, "x+", ChatDeckMatchField.Line)));
        Assert.Equal("Sender contains \"Bob\"", ChatDeckRuleSummary.When(R(pattern: "Bob", field: ChatDeckMatchField.Sender)));
        Assert.Equal("My name", ChatDeckRuleSummary.When(R(ChatDeckMatchKind.MyName)));
        ChatDeckPreset p = ChatDeckPresets.All[0];
        Assert.Equal($"Standard: {p.Label}", ChatDeckRuleSummary.When(R(ChatDeckMatchKind.Preset) with { PresetId = p.Id }));
        Assert.Equal("Standard: gone (unknown)", ChatDeckRuleSummary.When(R(ChatDeckMatchKind.Preset) with { PresetId = "gone" }));
    }

    [Fact]
    public void From_GroupsAndCounts()
    {
        Assert.Equal("Any channel", ChatDeckRuleSummary.From(R()));
        Assert.Equal("Combat", ChatDeckRuleSummary.From(R() with { Channels = ChatDeckChannels.Group("Combat").ToHashSet() }));
        Assert.Equal("Say, Trade", ChatDeckRuleSummary.From(R() with { Channels = new HashSet<ChatDeckChannel> { ChatDeckChannel.Trade, ChatDeckChannel.Say } }));
        var many = new HashSet<ChatDeckChannel> { ChatDeckChannel.Say, ChatDeckChannel.Trade, ChatDeckChannel.Help, ChatDeckChannel.Recall, ChatDeckChannel.Craft };
        Assert.Equal("Say, Trade, Help +2", ChatDeckRuleSummary.From(R() with { Channels = many }));
    }

    [Fact]
    public void Do_NamesTargetsAndAlert()
    {
        Assert.Equal("Hide", ChatDeckRuleSummary.Do(R() with { Action = ChatDeckRuleAction.Hide }, Tabs));
        Assert.Equal("Move to Combat", ChatDeckRuleSummary.Do(R() with { Action = ChatDeckRuleAction.MoveTo, TargetTabId = "combat" }, Tabs));
        Assert.Equal("Also show in Group + alert", ChatDeckRuleSummary.Do(R() with { Action = ChatDeckRuleAction.CopyTo, TargetTabId = "group", Alert = true }, Tabs));
        Assert.Equal("Move to (tab deleted)", ChatDeckRuleSummary.Do(R() with { Action = ChatDeckRuleAction.MoveTo, TargetTabId = "gone" }, Tabs));
        Assert.Equal("Colour line", ChatDeckRuleSummary.Do(R() with { Action = ChatDeckRuleAction.ColourLine }, Tabs));
    }

    [Fact]
    public void Status_ReadsTheCompiledRule()
    {
        Assert.Equal("", ChatDeckRuleSummary.Status(ChatDeckMatcher.Compile(R(), "Davian")));
        Assert.Equal("Off", ChatDeckRuleSummary.Status(ChatDeckMatcher.Compile(R() with { Enabled = false }, "Davian")));
        Assert.StartsWith("Invalid: ", ChatDeckRuleSummary.Status(ChatDeckMatcher.Compile(R(ChatDeckMatchKind.Regex, "("), "Davian")));
        Assert.StartsWith("Timed out", ChatDeckRuleSummary.Status(ChatDeckMatcher.Compile(R() with { Faulted = true }, "Davian")));
    }

    [Fact]
    public void TestLine_GeneralIsWorded_AndPresetsSeeIt()
    {
        ChatDeckLineFacts facts = ChatDeckTestLine.Facts(ChatDeckChannel.General, "Bob", "hi", "Davian");
        Assert.Equal(ChatDeckChannel.General, facts.Channel);
        Assert.Equal("[General] Bob says, \"hi\"", facts.Line);
        ChatDeckRule preset = new() { Id = "p", Kind = ChatDeckMatchKind.Preset, PresetId = "social.general" };
        Assert.Equal(ChatDeckMatchOutcome.Match,
            ChatDeckMatcher.Match(ChatDeckMatcher.Compile(preset, "Davian"), facts, false).Outcome);
        Assert.Equal("You hit the drudge for 5 points of slashing damage!",
            ChatDeckTestLine.Facts(ChatDeckChannel.CombatSelf, "", "You hit the drudge for 5 points of slashing damage!", "Davian").Line);
        foreach (ChatDeckChannel c in Enum.GetValues<ChatDeckChannel>())
            Assert.Equal(c, ChatDeckTestLine.Facts(c, "Bob", "hi", "Davian").Channel);
    }

    [Fact]
    public void TestLine_TellsAreWordedByDirection_AndBlankSenderStaysBlank()
    {
        Assert.Equal("Bob tells you, \"hi\"", ChatDeckTestLine.Facts(ChatDeckChannel.TellIn, "Bob", "hi", "Davian").Line);
        Assert.Equal("You tell Bob, \"hi\"", ChatDeckTestLine.Facts(ChatDeckChannel.TellOut, "Bob", "hi", "Davian").Line);
        Assert.Equal("", ChatDeckTestLine.Facts(ChatDeckChannel.Say, "", "hi", "Davian").Sender);
    }
}
