using AcDream.App.UI.Hud;
using AcDream.Core.Chat;
using AcDream.Runtime.Chat;

namespace AcDream.App.Tests.UI.Hud;

public sealed class ChatDeckPrefixTests
{
    private static ChatEntry ChannelEntry(string channel, string sender, string text) =>
        new(ChatKind.Channel, sender, text, 0u, 7u) { ChannelName = channel };

    private static ChatEntry SayEntry(string sender, string text) =>
        new(ChatKind.LocalSpeech, sender, text, 0x50000002u, 0u);

    [Fact]
    public void Strip_DropsTheChannelName_AndKeepsSpansWhole()
    {
        ChatEntry entry = ChannelEntry("General", "Bob", "hi");
        ChatProjectedLine projected = ChatLineProjector.Project(entry);
        Assert.StartsWith("[General] ", projected.Text);

        ChatProjectedLine shown = ChatDeckPrefix.Strip(projected, entry);

        Assert.Equal(projected.Text["[General] ".Length..], shown.Text);
        if (shown.Spans is { } spans) Assert.Equal(shown.Text, string.Concat(spans.Select(s => s.Text)));
    }

    [Fact]
    public void Strip_LeavesOtherLinesAlone()
    {
        ChatEntry entry = SayEntry("Bob", "[General] is not a prefix here");
        ChatProjectedLine projected = ChatLineProjector.Project(entry);
        Assert.Equal(projected, ChatDeckPrefix.Strip(projected, entry));
    }
}
