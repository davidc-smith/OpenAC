namespace AcDream.Core.Items;

/// <summary>
/// What <see cref="ServerReplyLog"/> has seen: how many UseDone events and
/// server refusals arrived, and the latest of each. All zero before the first.
/// </summary>
public readonly record struct ServerReplySnapshot(
    int UseDoneSequence,
    uint LastUseDoneError,
    int WeenieErrorSequence,
    uint LastWeenieError,
    uint LastWeenieErrorEventType,
    uint LastWeenieErrorObjectId);

/// <summary>
/// Counts the server's UseDone events and its refusals (WeenieError,
/// WeenieErrorWithString, InventoryServerSaveFailed), silent codes included,
/// so a caller can tell "the server answered" from "nothing yet" by comparing
/// sequence numbers. Retail-hosted RynthCore keeps the same counters.
/// </summary>
public sealed class ServerReplyLog
{
    private readonly object _gate = new();
    private ServerReplySnapshot _snapshot;

    public ServerReplySnapshot Snapshot
    {
        get { lock (_gate) return _snapshot; }
    }

    public void RecordUseDone(uint weenieError)
    {
        lock (_gate)
        {
            _snapshot = _snapshot with
            {
                UseDoneSequence = _snapshot.UseDoneSequence + 1,
                LastUseDoneError = weenieError,
            };
        }
    }

    /// <summary>Raised with the refusal code after <see cref="RecordWeenieError"/> records it, outside the lock.</summary>
    public event Action<uint>? WeenieErrorRecorded;

    /// <param name="eventType">The GameEvent that carried it (0x28A, 0x28B or 0xA0).</param>
    /// <param name="weenieError">The refusal code.</param>
    /// <param name="objectId">The item for 0xA0, else 0.</param>
    public void RecordWeenieError(uint eventType, uint weenieError, uint objectId)
    {
        lock (_gate)
        {
            _snapshot = _snapshot with
            {
                WeenieErrorSequence = _snapshot.WeenieErrorSequence + 1,
                LastWeenieError = weenieError,
                LastWeenieErrorEventType = eventType,
                LastWeenieErrorObjectId = objectId,
            };
        }
        WeenieErrorRecorded?.Invoke(weenieError);
    }
}
