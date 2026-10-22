namespace AcDream.App.UI.Markup;

/// <summary>
/// Textures a markup control draws for a while and then gives back, such as
/// the colour picker's gradients. UI thread only. A build without one gets
/// controls that draw without textures.
/// </summary>
public interface IMarkupTextureService
{
    /// <summary>Uploads straight-alpha RGBA8 pixels, rows top to bottom; 0 when it can't.</summary>
    uint Upload(byte[] rgba, int width, int height, string debugName);

    /// <summary>Gives back a texture from <see cref="Upload"/>.</summary>
    void Release(uint texture);
}
