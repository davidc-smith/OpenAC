namespace AcDream.Launcher.Core.Orchestration;

/// <summary>Serializes user stop requests until the requested process and startup work finish.</summary>
public sealed class LauncherStopGate
{
    private string? _sessionId;
    public bool IsPending => _sessionId is not null;

    public bool TryBegin(string sessionId, LauncherStateSnapshot snapshot)
    {
        Observe(snapshot);
        if (IsPending || !snapshot.Sessions.Any(session => session.SessionId == sessionId
                && session.IsProcessOrStartActive)) return false;
        _sessionId = sessionId;
        return true;
    }

    public void Observe(LauncherStateSnapshot snapshot)
    {
        if (_sessionId is not null && !snapshot.Sessions.Any(session =>
                session.SessionId == _sessionId && session.IsProcessOrStartActive))
            _sessionId = null;
    }
}
