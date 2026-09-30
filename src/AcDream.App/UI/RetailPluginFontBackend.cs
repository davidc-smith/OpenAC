using AcDream.App.Plugins;
using AcDream.App.Rendering;

namespace AcDream.App.UI;

/// <summary>
/// The interface's texture services as a font table sees them: glyph atlases
/// are uploaded as releasable single-channel textures and given back one at
/// a time, like a plugin's own images.
/// </summary>
internal sealed class RetailPluginFontBackend(TextureCache textures) : IPluginFontBackend
{
    private readonly TextureCache _textures = textures ?? throw new ArgumentNullException(nameof(textures));

    public uint UploadCoverage(byte[] coverage, int width, int height, string debugName) =>
        _textures.UploadReleasableCoverage8(coverage, width, height, debugName);

    public bool ReleaseCoverage(uint texture) => _textures.ReleaseUiTexture(texture);
}
