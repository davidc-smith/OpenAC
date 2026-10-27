namespace AcDream.Core.Chat.Deck;

/// <summary>One tab's lines (newest <see cref="Capacity"/>, oldest first) and whether it has unseen ones.</summary>
public sealed class ChatDeckTabState
{
    private readonly ChatDeckLine?[] _ring;
    private int _start;

    public ChatDeckTabState(string tabId, int capacity = ChatDeckLimits.HistoryPerTab)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(capacity, 1);
        TabId = tabId;
        _ring = new ChatDeckLine?[capacity];
    }

    public string TabId { get; }
    public int Capacity => _ring.Length;
    public int Count { get; private set; }
    public int Unread { get; private set; }
    public bool Alert { get; private set; }
    /// <summary>Changes on every add and clear; a reader that laid the tab out keeps its copy while this is unchanged.</summary>
    public long Version { get; private set; }
    /// <summary>Lines added since the last clear, including those the ring has since dropped.</summary>
    public long Added { get; private set; }

    public ChatDeckLine this[int index]
    {
        get
        {
            ArgumentOutOfRangeException.ThrowIfNegative(index);
            ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, Count);
            return _ring[(_start + index) % _ring.Length]!;
        }
    }

    public IReadOnlyList<ChatDeckLine> Snapshot()
    {
        var copy = new ChatDeckLine[Count];
        for (int i = 0; i < Count; i++) copy[i] = this[i];
        return copy;
    }

    public void Add(ChatDeckLine line, bool seen)
    {
        if (Count < _ring.Length)
        {
            _ring[(_start + Count) % _ring.Length] = line;
            Count++;
        }
        else
        {
            _ring[_start] = line;
            _start = (_start + 1) % _ring.Length;
        }
        Version++;
        Added++;
        if (seen) return;
        Unread++;
        if (line.Alert) Alert = true;
    }

    public void MarkRead()
    {
        Unread = 0;
        Alert = false;
    }

    public void Clear()
    {
        Array.Clear(_ring);
        _start = 0;
        Count = 0;
        MarkRead();
        Added = 0;
        Version++;
    }
}
