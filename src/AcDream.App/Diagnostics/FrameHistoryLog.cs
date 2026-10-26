namespace AcDream.App.Diagnostics;

/// <summary>
/// The frame-history rows a profiled session keeps for its CSV, held in
/// fixed-size blocks that are added as rows arrive. A row is never copied
/// once written, and the log takes memory only as the session needs it:
/// reserving a whole long session's rows up front made the profiler itself
/// one of the largest allocations of a profiled login.
/// </summary>
internal sealed class FrameHistoryLog
{
    internal const int BlockRows = 8192;

    private readonly List<FrameHistoryRecord[]> _blocks = [];

    public int Count { get; private set; }

    public void Add(in FrameHistoryRecord record)
    {
        int block = Count / BlockRows;
        if (block == _blocks.Count)
            _blocks.Add(new FrameHistoryRecord[BlockRows]);
        _blocks[block][Count % BlockRows] = record;
        Count++;
    }

    public ref FrameHistoryRecord this[int index]
    {
        get
        {
            if ((uint)index >= (uint)Count)
                throw new ArgumentOutOfRangeException(nameof(index));
            return ref _blocks[index / BlockRows][index % BlockRows];
        }
    }

    public IEnumerable<FrameHistoryRecord> Rows()
    {
        for (int i = 0; i < Count; i++)
            yield return _blocks[i / BlockRows][i % BlockRows];
    }
}
