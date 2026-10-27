using AcDream.App.Rendering;
using AcDream.App.UI.Markup;

namespace AcDream.App.UI;

/// <summary>The client's markup texture service: releasable interface textures from the texture cache.</summary>
internal sealed class RetailMarkupTextureService(TextureCache textures) : IMarkupTextureService
{
    public uint Upload(byte[] rgba, int width, int height, string debugName) =>
        textures.UploadReleasableRgba8(rgba, width, height, debugName);

    public void Release(uint texture) => textures.ReleaseUiTexture(texture);
}
