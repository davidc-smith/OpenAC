namespace AcDream.ClientExtensions;

/// <summary>
/// The client's icons as ImGui texture ids, for <c>ImGui.Image</c> on the
/// overlay. Zero means no icon (unknown id, or the client's interface isn't
/// up yet). Call from the overlay's draw thread.
/// </summary>
public interface IClientOverlayIcons
{
    /// <summary>An icon image by id; a bare index below 0x01000000 gets 0x06000000 added.</summary>
    IntPtr Icon(uint iconId);

    /// <summary>A spell's icon, as the spell bar draws it.</summary>
    IntPtr SpellIcon(uint spellId);

    /// <summary>A known object's icon with its underlay, overlay and effects, as the inventory draws it.</summary>
    IntPtr ObjectIcon(uint objectId);
}
