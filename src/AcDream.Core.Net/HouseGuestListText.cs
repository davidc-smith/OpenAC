using System.Text;
using AcDream.Core.Net.Messages;

namespace AcDream.Core.Net;

/// <summary>
/// The text the client prints, in green and as one block, for a 0x0257 guest
/// list reply: a "Guests:" heading, then one guest per line indented by two
/// spaces with " *" after anyone who may also use storage (or "  None" when
/// the list is empty), then the allegiance permissions and the open-house
/// line when their flags are set. Roommates are not printed from this reply.
/// Both "@house guest list" and "@house storage list" ask for it.
/// </summary>
public static class HouseGuestListText
{
    public const string Heading = "Guests:\n";
    public const string Indent = "  ";
    public const string Empty = Indent + "None\n";
    public const string StorageMarker = " *";
    public const string AllegiancePermissions = "Allegiance Permissions: ";
    public const string AllegianceGuestAccess = "guest access";
    public const string AllegianceStorageAccess = "storage access";
    public const string OpenHouse = "[ open house ]\n";

    public static string Compose(GameEvents.HouseAccess access)
    {
        var text = new StringBuilder(Heading);
        if (access.Guests.Count == 0)
        {
            text.Append(Empty);
        }
        else
        {
            foreach (GameEvents.HouseGuest guest in access.Guests)
            {
                text.Append(Indent).Append(guest.Name);
                if (guest.Storage) text.Append(StorageMarker);
                text.Append('\n');
            }
        }

        // The two permission words are written back to back, with nothing
        // between them, when both are set.
        if (access.AllegianceGuests || access.AllegianceStorage)
        {
            text.Append(AllegiancePermissions);
            if (access.AllegianceGuests) text.Append(AllegianceGuestAccess);
            if (access.AllegianceStorage) text.Append(AllegianceStorageAccess);
            text.Append('\n');
        }

        if (access.OpenToEveryone) text.Append(OpenHouse);
        return text.ToString();
    }
}
