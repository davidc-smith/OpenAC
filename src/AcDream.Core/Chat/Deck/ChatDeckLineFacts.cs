namespace AcDream.Core.Chat.Deck;

/// <summary>A run of matched characters in one field of a line.</summary>
public readonly record struct ChatDeckSpan(ChatDeckMatchField Field, int Start, int Length);

/// <summary>Everything a rule may read from one line, worked out once.</summary>
public sealed record ChatDeckLineFacts(
    ChatEntry Entry,
    ChatDeckChannel Channel,
    string Text,
    string Sender,
    string Line,
    bool IsOwn)
{
    public static ChatDeckLineFacts From(in ChatEntry entry) => new(
        entry,
        ChatDeckChannels.Classify(entry),
        entry.Text,
        entry.Sender,
        ChatLineWording.Format(entry),
        ChatDeckChannels.IsOwnLine(entry));

    public string Read(ChatDeckMatchField field) => field switch
    {
        ChatDeckMatchField.Sender => Sender,
        ChatDeckMatchField.Line => Line,
        _ => Text,
    };
}
