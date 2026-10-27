using AcDream.Core.Chat;
using AcDream.Core.Chat.Deck;

namespace AcDream.Core.Tests.Chat.Deck;

public sealed class ChatDeckPresetsTests
{
    public static TheoryData<string> PresetIds()
    {
        var data = new TheoryData<string>();
        foreach (var p in ChatDeckPresets.All) data.Add(p.Id);
        return data;
    }

    private static ChatDeckMatchOutcome Run(ChatDeckPreset preset, ChatDeckPresetSample sample)
    {
        var rule = new ChatDeckRule { Id = "x", Kind = ChatDeckMatchKind.Preset, PresetId = preset.Id };
        var entry = new ChatEntry(ChatKind.System, "", sample.Line, 0u, 0u);
        var facts = new ChatDeckLineFacts(entry, sample.Channel, sample.Line, "", sample.Line, IsOwn: false);
        return ChatDeckMatcher.Match(ChatDeckMatcher.Compile(rule, "Davian"), facts, wantSpans: false).Outcome;
    }

    [Fact]
    public void Count_AndUniqueIds()
    {
        Assert.InRange(ChatDeckPresets.All.Count, 65, 75);
        Assert.Equal(ChatDeckPresets.All.Count,
            ChatDeckPresets.All.Select(p => p.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    [Theory]
    [MemberData(nameof(PresetIds))]
    public void Preset_CatchesItsSamplesAndSparesItsNearMisses(string id)
    {
        ChatDeckPreset preset = ChatDeckPresets.Get(id)!;
        Assert.NotEmpty(preset.Catches);
        Assert.NotEmpty(preset.Spares);
        Assert.All(preset.Catches, s => Assert.Equal(ChatDeckMatchOutcome.Match, Run(preset, s)));
        Assert.All(preset.Spares, s => Assert.Equal(ChatDeckMatchOutcome.NoMatch, Run(preset, s)));
    }

    [Fact]
    public void Scopes_MapRynthChatTypes()
    {
        Assert.True(ChatDeckPresetScopes.ForType(7).SetEquals(ChatDeckChannels.Group("Combat")));
        Assert.True(ChatDeckPresetScopes.ForType(21).SetEquals(ChatDeckChannels.Group("Combat")));
        Assert.True(ChatDeckPresetScopes.ForType(12).SetEquals(ChatDeckChannels.Group("Local")));
        Assert.True(ChatDeckPresetScopes.ForType(26).SetEquals(ChatDeckChannels.Group("System")));
        Assert.Equal(ChatDeckChannel.CombatSelf, ChatDeckPresetScopes.SampleChannel(22));
        Assert.Equal(ChatDeckChannel.Say, ChatDeckPresetScopes.SampleChannel(2));
        Assert.Empty(ChatDeckPresetScopes.Any);
    }
}
