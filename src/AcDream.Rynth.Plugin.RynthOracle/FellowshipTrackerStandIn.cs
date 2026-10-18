using System;
using System.Collections.Generic;

namespace RynthCore.Plugin.RynthAi;

/// <summary>
/// Stand-in for RynthAi's FellowshipTracker, which reads the retail client's
/// memory. OpenAC has no such memory, so this reports "not in a fellowship".
/// </summary>
public class FellowshipTracker : IDisposable
{
    public FellowshipTracker() { }

    public void Dispose() { }

    public bool IsInFellowship => false;

    public int MemberCount => 0;

    public string FellowshipName => string.Empty;

    public int LeaderId => 0;

    public bool IsLeader => false;

    public bool IsOpen => false;

    public bool IsLocked => false;

    public bool ShareXP => false;

    public bool IsMember(string name) => false;

    public bool IsMember(int characterId) => false;

    public IEnumerable<string> GetMemberNames() => Array.Empty<string>();

    public string GetMemberName(int index) => string.Empty;

    public int GetMemberId(int index) => 0;
}
