namespace AcDream.Rynth.Host;

/// <summary>
/// RynthCore's vendor trade requests (VendorTrade.cs): checks a buy or sell against
/// the open vendor, sends it through the view, and keeps the status of the most recent
/// request: Sent, then Done with Accepted / AnsweredNoRefresh / TimedOut, or Refused
/// for one that never went out. One request is in flight at a time.
/// </summary>
internal sealed class RynthVendorTrades : IDisposable
{
    internal const int MaxLines = 100;
    internal const int MaxAmount = 0x7FFFFF;
    internal const long TimeoutMs = 10_000;

    internal const int StateSent = 2, StateDone = 3, StateRefused = 4;
    internal const int ResultAccepted = 1, ResultAnsweredNoRefresh = 2, ResultTimedOut = 3;

    private readonly IRynthGameView _view;
    private uint _sequence;
    private long _sentAt;
    private bool _inFlight;

    public RynthVendorTrades(IRynthGameView view)
    {
        _view = view;
        _view.VendorTransactionCompleted += OnCompleted;
    }

    /// <summary>Milliseconds; tests replace it.</summary>
    internal Func<long> Clock { get; set; } = () => Environment.TickCount64;

    /// <summary>+1 for every vendor list the server sent (open or refresh).</summary>
    public uint Generation { get; private set; }

    public bool InFlight => _inFlight;

    /// <summary>The latest request's status; RequestId 0 before the first.</summary>
    public (uint RequestId, int State, int Result, bool IsBuy, uint VendorId, int EntryCount, string Message) Status { get; private set; }

    public void NoteOpened() => Generation = unchecked(Generation + 1u);

    /// <summary>
    /// Session teardown: nothing is in flight any more. The server will never answer a
    /// request still waiting, so it ends now as a timeout rather than staying Sent.
    /// </summary>
    public void Reset()
    {
        if (_inFlight)
            Finish(ResultTimedOut, "the session ended before the server answered");
    }

    public uint Buy(uint vendorId, IReadOnlyList<(uint ObjectId, int Amount)> lines)
    {
        // One at a time, as RynthCore's single in-flight slot: the waiting request keeps its status.
        if (_inFlight)
            return 0;
        uint seq = NextSequence();
        RynthVendor? vendor = _view.Vendor;
        if (Check(vendor, vendorId, lines.Count) is { } error)
            return Refuse(seq, true, vendorId, lines.Count, error);
        RynthVendor open = vendor!;
        for (int i = 0; i < lines.Count; i++)
        {
            (uint id, int amount) = lines[i];
            for (int j = 0; j < i; j++)
                if (lines[j].ObjectId == id)
                    return Refuse(seq, true, open.VendorId, lines.Count, $"0x{id:X8} listed twice");
            RynthVendorItem? item = Find(open, id);
            if (item is null)
                return Refuse(seq, true, open.VendorId, lines.Count, $"0x{id:X8} is not on this vendor's list");
            if (amount < 1 || amount > MaxAmount)
                return Refuse(seq, true, open.VendorId, lines.Count, $"bad amount {amount} for 0x{id:X8}");
            if (!item.Value.Unlimited && amount > item.Value.Amount)
                return Refuse(seq, true, open.VendorId, lines.Count,
                    $"vendor only has {item.Value.Amount} of 0x{id:X8}, asked for {amount}");
        }
        return _view.VendorBuy(lines, out string? notice)
            ? Sent(seq, true, open.VendorId, lines.Count)
            : Refuse(seq, true, open.VendorId, lines.Count, notice ?? "the client would not send it");
    }

    public uint Sell(uint vendorId, IReadOnlyList<uint> ids)
    {
        if (_inFlight)
            return 0;
        uint seq = NextSequence();
        RynthVendor? vendor = _view.Vendor;
        if (Check(vendor, vendorId, ids.Count) is { } error)
            return Refuse(seq, false, vendorId, ids.Count, error);
        RynthVendor open = vendor!;
        for (int i = 0; i < ids.Count; i++)
        {
            uint id = ids[i];
            if (id == 0)
                return Refuse(seq, false, open.VendorId, ids.Count, "item id 0");
            if (id == open.VendorId || Find(open, id) is not null)
                return Refuse(seq, false, open.VendorId, ids.Count, $"0x{id:X8} belongs to the vendor");
            for (int j = 0; j < i; j++)
                if (ids[j] == id)
                    return Refuse(seq, false, open.VendorId, ids.Count, $"0x{id:X8} listed twice");
        }
        return _view.VendorSell(ids, out string? notice)
            ? Sent(seq, false, open.VendorId, ids.Count)
            : Refuse(seq, false, open.VendorId, ids.Count, notice ?? "the client would not send it");
    }

    /// <summary>Times out a request the server never answered. Called once per host tick.</summary>
    public void Tick()
    {
        if (_inFlight && Clock() - _sentAt >= TimeoutMs)
            Finish(ResultTimedOut, "no answer from the server within 10 s");
    }

    public void Dispose() => _view.VendorTransactionCompleted -= OnCompleted;

    private void OnCompleted(RynthVendorOutcome outcome)
    {
        if (!_inFlight)
            return;
        if (outcome.Success)
            Finish(ResultAccepted, "server accepted");
        else
            Finish(ResultAnsweredNoRefresh, outcome.Notice ?? "server refused");
    }

    private static string? Check(RynthVendor? vendor, uint vendorId, int count)
    {
        if (vendor is null)
            return "no vendor open";
        if (vendorId != 0 && vendorId != vendor.VendorId)
            return $"vendor 0x{vendorId:X8} is not the open vendor (0x{vendor.VendorId:X8})";
        if (count < 1)
            return "nothing to trade";
        if (count > MaxLines)
            return $"too many lines ({count} > {MaxLines}); split into batches";
        return null;
    }

    private static RynthVendorItem? Find(RynthVendor vendor, uint id)
    {
        foreach (RynthVendorItem item in vendor.Items)
            if (item.ObjectId == id)
                return item;
        return null;
    }

    private uint NextSequence()
    {
        _sequence = unchecked(_sequence + 1u);
        if (_sequence == 0)
            _sequence = 1;
        return _sequence;
    }

    private uint Sent(uint seq, bool isBuy, uint vendorId, int count)
    {
        _inFlight = true;
        _sentAt = Clock();
        Status = (seq, StateSent, 0, isBuy, vendorId, count, "sent, waiting for the server");
        return seq;
    }

    private uint Refuse(uint seq, bool isBuy, uint vendorId, int count, string reason)
    {
        Status = (seq, StateRefused, 0, isBuy, vendorId, count, reason);
        return 0;
    }

    private void Finish(int result, string message)
    {
        _inFlight = false;
        Status = Status with { State = StateDone, Result = result, Message = message };
    }
}
