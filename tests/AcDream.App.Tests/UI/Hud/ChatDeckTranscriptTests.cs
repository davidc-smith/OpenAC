using System.Globalization;
using System.Numerics;
using AcDream.App.UI;
using AcDream.App.UI.Hud;
using AcDream.Core.Chat;
using AcDream.Core.Chat.Deck;

namespace AcDream.App.Tests.UI.Hud;

public sealed class ChatDeckTranscriptTests
{
    private static readonly Vector4 Channel = new(0.8f, 0.8f, 0.8f, 1f);
    private static readonly Vector4 Tag = new(0.4f, 0.8f, 1f, 1f);
    private static readonly Vector4 Stamp = new(0.5f, 0.5f, 0.5f, 1f);
    private const uint PlayerGuid = 0x5000000Au;

    private static ChatDeckTranscriptStyle Style(
        float width = 1000f, ChatDeckTimestamps stamps = ChatDeckTimestamps.Off, string search = "",
        object? colourKey = null, Func<ChatDeckLine, Vector4>? channel = null) =>
        new(width, s => s.Length * 7f, FontKey: null, stamps, channel ?? (_ => Channel), Tag, Stamp, search, colourKey);

    private static ChatDeckLine Line(ChatEntry entry, uint? lineColour = null, uint? matchColour = null,
        IReadOnlyList<ChatDeckSpan>? spans = null)
    {
        var facts = ChatDeckLineFacts.From(entry, "Davian");
        var verdict = new ChatDeckVerdict(
            Hidden: false, Destinations: ["all"], LineColour: lineColour, MatchColour: matchColour,
            MatchSpans: spans ?? [], Alert: false, Hits: [], TimedOutRuleIds: []);
        return new ChatDeckLine(facts, verdict);
    }

    private static ChatEntry Sys(string text, long seq = 1) =>
        new(ChatKind.System, "", text, 0u, 0u) { Sequence = seq };

    [Fact]
    public void Wraps_AndKeysByEntry()
    {
        var t = new ChatDeckTranscript();
        ChatDeckTranscriptLayout layout = t.Build([Line(Sys("aaaa bbbb cccc", seq: 42))], Style(width: 35f));

        Assert.True(layout.Lines.Count >= 2);
        Assert.All(layout.Keys, k => Assert.Equal(42, k.Source));
        Assert.Equal(0, layout.Keys[0].Start);
        Assert.Equal("aaaa bbbb cccc", string.Join(" ", layout.Lines.Select(l => l.Text.Trim())));
    }

    [Fact]
    public void Timestamp_PrefixInTimestampColour()
    {
        var received = new DateTime(2026, 10, 27, 9, 5, 7, DateTimeKind.Utc);
        var entry = Sys("hello") with { Received = received };
        var t = new ChatDeckTranscript();
        string expected = received.ToLocalTime().ToString(@"H\:mm\:ss ", CultureInfo.InvariantCulture);

        ChatDeckTranscriptLayout layout = t.Build([Line(entry)], Style(stamps: ChatDeckTimestamps.HourMinuteSecond));

        Assert.StartsWith(expected, layout.Lines[0].Text);
        IReadOnlyList<UiText.TextRun> runs = layout.Runs[0]!;
        Assert.Equal(expected, runs[0].Text);
        Assert.Equal(Stamp, runs[0].Color);
    }

    [Fact]
    public void LineColour_OverridesChannel_AndMarksEveryVisualLine()
    {
        uint red = 0xFFFF0000u;
        var t = new ChatDeckTranscript();
        ChatDeckTranscriptLayout layout = t.Build([Line(Sys("aaaa bbbb cccc"), lineColour: red)], Style(width: 35f));

        var expected = new Vector4(1f, 0f, 0f, 1f);
        Assert.All(layout.Lines, l => Assert.Equal(expected, l.Color));
        Assert.All(layout.Markers, m => Assert.Equal(expected, m));
    }

    [Fact]
    public void PlainLine_HasNoMarker_AndChannelColour()
    {
        ChatDeckTranscriptLayout layout = new ChatDeckTranscript().Build([Line(Sys("plain"))], Style());
        Assert.Null(layout.Markers[0]);
        Assert.Equal(Channel, layout.Lines[0].Color);
    }

    [Fact]
    public void TellTag_IsColouredAndListed()
    {
        var tell = new ChatEntry(ChatKind.Tell, "Bob", "meet me", PlayerGuid, 0u) { LogTextType = 0x03u };
        ChatDeckTranscriptLayout layout = new ChatDeckTranscript().Build([Line(tell)], Style());

        Assert.Contains(layout.Runs[0]!, r => r.Text == "Bob" && r.Color == Tag);
        var (start, length, tag) = Assert.Single(layout.Tags[0]!);
        Assert.Equal((0, 3), (start, length));
        Assert.True(tag.IsTell);
    }

    [Fact]
    public void MatchColour_ColoursTheMatchedText()
    {
        uint gold = 0xFFFFD700u;
        var entry = new ChatEntry(ChatKind.LocalSpeech, "Bob", "I found a key", PlayerGuid, 0u) { LogTextType = 0x02u };
        // "key" in the Text field: offset 10, length 3
        ChatDeckLine line = Line(entry, matchColour: gold, spans: [new ChatDeckSpan(ChatDeckMatchField.Text, 10, 3)]);

        ChatDeckTranscriptLayout layout = new ChatDeckTranscript().Build([line], Style());

        var expected = new Vector4(1f, 215f / 255f, 0f, 1f);
        Assert.Contains(layout.Runs[0]!, r => r.Text == "key" && r.Color == expected);
    }

    [Fact]
    public void Search_KeepsMatchingLinesOnly()
    {
        ChatDeckTranscriptLayout layout = new ChatDeckTranscript().Build(
            [Line(Sys("the drudge", 1)), Line(Sys("a banderling", 2))], Style(search: "DRUDGE"));
        Assert.Single(layout.Lines);
        Assert.Equal(1, layout.Keys[0].Source);
    }

    [Fact]
    public void ProjectsEachLineOnce()
    {
        var t = new ChatDeckTranscript();
        ChatDeckLine line = Line(Sys("once"));
        t.Build([line], Style());
        t.Build([line], Style(width: 500f));
        Assert.Equal(1, t.ProjectionsForTest);
    }

    [Fact]
    public void AppendingALine_ColoursOnlyTheNewLine()
    {
        var t = new ChatDeckTranscript();
        var key = new object();
        ChatDeckLine a = Line(Sys("first", 1)), b = Line(Sys("second", 2)), c = Line(Sys("third", 3));
        t.Build([a, b], Style(colourKey: key));
        Assert.Equal(2, t.ColourPassesForTest);

        ChatDeckTranscriptLayout layout = t.Build([a, b, c], Style(colourKey: key));

        Assert.Equal(3, t.ColourPassesForTest);
        Assert.Equal(["first", "second", "third"], layout.Lines.Select(l => l.Text));
        Assert.Equal([1L, 2L, 3L], layout.Keys.Select(k => k.Source));
    }

    [Fact]
    public void NewColourKey_RecoloursEveryLine()
    {
        var t = new ChatDeckTranscript();
        ChatDeckLine a = Line(Sys("first", 1)), b = Line(Sys("second", 2));
        t.Build([a, b], Style(colourKey: new object()));
        var green = new Vector4(0f, 1f, 0f, 1f);

        ChatDeckTranscriptLayout layout = t.Build([a, b], Style(colourKey: new object(), channel: _ => green));

        Assert.Equal(4, t.ColourPassesForTest);
        Assert.All(layout.Lines, l => Assert.Equal(green, l.Color));
    }

    [Fact]
    public void NewWrapWidth_RecoloursEveryLine()
    {
        var t = new ChatDeckTranscript();
        var key = new object();
        ChatDeckLine a = Line(Sys("aaaa bbbb cccc", 1));
        t.Build([a], Style(colourKey: key));
        ChatDeckTranscriptLayout layout = t.Build([a], Style(width: 35f, colourKey: key));

        Assert.Equal(2, t.ColourPassesForTest);
        Assert.True(layout.Lines.Count >= 2);
    }

    [Fact]
    public void Runs_PastTheColours_TakeTheLineColour()
    {
        var gold = new Vector4(1f, 0.8f, 0f, 1f);
        Vector4[] colours = [Channel, Channel, gold, gold];

        IReadOnlyList<UiText.TextRun>? runs = ChatDeckTranscript.Runs("cdef", 2, colours, Channel);

        Assert.NotNull(runs);
        Assert.Equal([("cd", gold), ("ef", Channel)], runs!.Select(r => (r.Text, r.Color)));
    }

    [Fact]
    public void Keys_CountTheTimestamp_SoSelectionsResolveToTheirOwnLine()
    {
        var received = new DateTime(2026, 10, 27, 9, 5, 7, DateTimeKind.Utc);
        var entry = Sys("aaaa bbbb cccc dddd", seq: 7) with { Received = received };
        string stamp = received.ToLocalTime().ToString(@"H\:mm\:ss ", CultureInfo.InvariantCulture);
        string shown = stamp + "aaaa bbbb cccc dddd";

        ChatDeckTranscriptLayout layout = new ChatDeckTranscript().Build(
            [Line(entry)], Style(width: 7f * (stamp.Length + 9), stamps: ChatDeckTimestamps.HourMinuteSecond));

        Assert.True(layout.Lines.Count >= 2);
        Assert.Equal(0, layout.Keys[0].Start);
        int searchFrom = 0;
        for (int i = 0; i < layout.Lines.Count; i++)
        {
            int at = shown.IndexOf(layout.Lines[i].Text, searchFrom, StringComparison.Ordinal);
            Assert.Equal(at, layout.Keys[i].Start);
            searchFrom = at + layout.Lines[i].Text.Length;
            if (i > 0) Assert.True(layout.Keys[i].Start > layout.Keys[i - 1].Start);
        }
        // The separator WrapText dropped between line 0 and line 1 is counted.
        Assert.Equal(layout.Keys[0].Start + layout.Lines[0].Text.Length + 1, layout.Keys[1].Start);

        Assert.True(UiText.TryResolveAnchor(layout.Keys, layout.Lines, 7, layout.Keys[1].Start + 2, out UiText.Pos pos));
        Assert.Equal(new UiText.Pos(1, 2), pos);
    }
}
