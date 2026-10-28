namespace AcDream.Core.Targeting;

/// <param name="At">Clock seconds when the ID arrived.</param>
/// <param name="HealthPercent">Live health (0..1) when it arrived, to detect later changes.</param>
public readonly record struct TargetAppraisalEntry(double At, bool Success, float HealthPercent);

/// <summary>When each object was last identified, and whether the assess succeeded.</summary>
public sealed class TargetAppraisalLog
{
    private readonly Dictionary<uint, TargetAppraisalEntry> _entries = new();

    public void Record(uint objectId, bool success, float healthPercent, double now) =>
        _entries[objectId] = new(now, success, healthPercent);

    public bool TryGet(uint objectId, out TargetAppraisalEntry entry) => _entries.TryGetValue(objectId, out entry);

    public void Forget(uint objectId) => _entries.Remove(objectId);

    public void Clear() => _entries.Clear();
}
