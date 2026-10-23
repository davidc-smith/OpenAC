using System.Collections.Generic;
using AcDream.Core.Net.Messages;

namespace AcDream.Core.Net;

/// <summary>
/// The chat lines retail prints, in green, for a 0x0257 guest list reply: a
/// "Guests:" heading, then one guest per line indented by a space, with " *"
/// after anyone who may also use storage, or "None" when the list is empty.
/// Both "@house guest list" and "@house storage list" ask for the same reply.
/// </summary>
public static class HouseGuestListText
{
    public const string Heading = "Guests:";
    public const string Indent = " ";
    public const string Empty = Indent + "None";
    public const string StorageMarker = " *";

    public static IReadOnlyList<string> Lines(GameEvents.HouseAccess access)
    {
        var lines = new List<string>(access.Guests.Count + 1) { Heading };
        if (access.Guests.Count == 0)
        {
            lines.Add(Empty);
            return lines;
        }

        foreach (GameEvents.HouseGuest guest in access.Guests)
            lines.Add(Indent + (guest.Storage ? guest.Name + StorageMarker : guest.Name));
        return lines;
    }
}
