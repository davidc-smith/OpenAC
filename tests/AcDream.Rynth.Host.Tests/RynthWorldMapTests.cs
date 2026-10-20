using RynthCore.Engine.ImGuiBackend;

namespace AcDream.Rynth.Host.Tests;

public sealed class RynthWorldMapTests : IDisposable
{
    private readonly Func<byte[], int, int, IntPtr>? _upload = DX9Backend.Upload;
    private readonly Func<IntPtr, bool>? _release = DX9Backend.Release;

    public void Dispose()
    {
        DX9Backend.Upload = _upload;
        DX9Backend.Release = _release;
    }

    // One DXT1 tile (256 x 256 = 64 x 64 blocks): every block c0 red, c1 blue, all indices 0 (c0).
    private static byte[] RedDxt1Tile()
    {
        var blocks = new byte[64 * 64 * 8];
        for (int b = 0; b < blocks.Length; b += 8)
        {
            blocks[b] = 0x00; blocks[b + 1] = 0xF8;      // c0 = 0xF800 (red)
            blocks[b + 2] = 0x1F; blocks[b + 3] = 0x00;  // c1 = 0x001F (blue); c0 > c1: four-colour form
        }
        return blocks;
    }

    [Fact]
    public void ADxt1TileUploadsAsRgba()
    {
        var uploads = new List<(byte[] Rgba, int W, int H)>();
        DX9Backend.Upload = (rgba, w, h) => { uploads.Add((rgba, w, h)); return (IntPtr)7; };

        IntPtr tex = DX9Backend.CreateTextureDxt1(IntPtr.Zero, 256, 256, RedDxt1Tile());

        Assert.Equal((IntPtr)7, tex);
        var (data, width, height) = Assert.Single(uploads);
        Assert.Equal((256, 256, 256 * 256 * 4), (width, height, data.Length));
        Assert.Equal(new byte[] { 255, 0, 0, 255 }, data[..4]);
    }

    [Fact]
    public void WithNoUploadADxt1TileIsNoTexture()
    {
        DX9Backend.Upload = null;
        Assert.Equal(IntPtr.Zero, DX9Backend.CreateTextureDxt1(IntPtr.Zero, 256, 256, RedDxt1Tile()));
    }
}
