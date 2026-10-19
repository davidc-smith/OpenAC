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

    /// <summary>
    /// Uploads a picture of the caller's own (RGBA, 4 bytes a pixel) as a texture id the
    /// overlay draws; zero when it can't. Give it back with <see cref="Release"/>.
    /// </summary>
    IntPtr Upload(byte[] rgba, int width, int height, string name) => IntPtr.Zero;

    /// <summary>Gives back a texture from <see cref="Upload"/>; false for any other id.</summary>
    bool Release(IntPtr texture) => false;
}
