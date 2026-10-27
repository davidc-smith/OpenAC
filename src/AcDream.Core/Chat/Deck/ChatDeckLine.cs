namespace AcDream.Core.Chat.Deck;

/// <summary>One line as the Chat Deck shows it, evaluated once and shared by every tab it is in.</summary>
public sealed class ChatDeckLine(ChatDeckLineFacts facts, ChatDeckVerdict verdict)
{
    public ChatDeckLineFacts Facts { get; } = facts;
    public ChatEntry Entry => Facts.Entry;
    public ChatDeckChannel Channel => Facts.Channel;
    public long Sequence => Facts.Entry.Sequence;
    public uint? LineColour { get; } = verdict.LineColour;
    public uint? MatchColour { get; } = verdict.MatchColour;
    public IReadOnlyList<ChatDeckSpan> MatchSpans { get; } = verdict.MatchSpans;
    public bool Alert { get; } = verdict.Alert;
}
