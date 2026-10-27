namespace AcDream.Core.Items;

public static class PublicWeenieFlagsExtensions
{
    /// <summary>The object's bitfield as flags; None when it has none.</summary>
    public static PublicWeenieFlags FlagsOf(ClientObject obj) =>
        (PublicWeenieFlags)(obj.PublicWeenieBitfield ?? 0u);
}
