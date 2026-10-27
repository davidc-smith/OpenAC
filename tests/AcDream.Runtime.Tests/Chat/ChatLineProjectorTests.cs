using AcDream.Core.Chat;
using AcDream.Runtime.Chat;

namespace AcDream.Runtime.Tests.Chat;

public sealed class ChatLineProjectorTests
{
    private const uint PlayerGuid = 0x5000000Au;

    public static TheoryData<string> Cases() => ["plain", "tell", "plugin-coord", "combat-coord", "channel"];

    private static ChatEntry Entry(string name) => name switch
    {
        "plain" => new ChatEntry(ChatKind.System, "", "The server says hi.", 0u, 0u),
        "tell" => new ChatEntry(ChatKind.Tell, "Bob", "meet me", PlayerGuid, 0u) { LogTextType = 0x03u },
        "plugin-coord" => new ChatEntry(ChatKind.System, "", "Go to 12.3N, 45.6E now", 0u, 0u) { AllowsMarkup = true },
        "combat-coord" => new ChatEntry(ChatKind.Combat, "", "You hit 12.3N, 45.6E for 5", 0u, 0u),
        "channel" => new ChatEntry(ChatKind.Channel, "Bob", "group up", PlayerGuid, 0x800u) { ChannelName = "Fellowship" },
        _ => throw new ArgumentOutOfRangeException(nameof(name)),
    };

    private static string Shape(IReadOnlyList<ChatTextSpan>? spans) =>
        spans is null ? "<none>" : string.Join("|", spans.Select(s => $"{s.Text}#{s.Tag?.ToString() ?? "-"}#{s.Role}"));

    [Theory]
    [MemberData(nameof(Cases))]
    public void Project_MatchesTheFeedWithoutTimestamps(string name)
    {
        ChatEntry entry = Entry(name);
        var feed = new RuntimeChatFeed(new ChatLog());   // no DisplayTimestampsSource: timestamps off

        RuntimeChatLine fed = feed.BuildLine(entry);
        ChatProjectedLine projected = ChatLineProjector.Project(entry);

        Assert.Equal(fed.Text, projected.Text);
        Assert.Equal(Shape(fed.Spans), Shape(projected.Spans));
    }

    [Fact]
    public void Tell_HasATellTagAroundTheSender()
    {
        ChatProjectedLine projected = ChatLineProjector.Project(Entry("tell"));
        Assert.Contains(projected.Spans!, s => s.Tag is { IsTell: true } && s.Text == "Bob");
        Assert.Equal("Bob tells you, \"meet me\"", projected.Text);
    }

    [Fact]
    public void CombatLine_DoesNotLinkItsCoordinates()
    {
        ChatProjectedLine projected = ChatLineProjector.Project(Entry("combat-coord"));
        Assert.True(projected.Spans is null || projected.Spans.All(s => s.Tag is null));
    }
}
