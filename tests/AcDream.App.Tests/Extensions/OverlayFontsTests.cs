using AcDream.App.Extensions.Overlay;
using ImGuiNET;
using RynthCore.Engine.ImGuiBackend;

namespace AcDream.App.Tests.Extensions;

[Collection("Rynth")]
public sealed unsafe class OverlayFontsTests : IDisposable
{
    private readonly List<string> _log = new();
    private readonly OverlayFonts _fonts;
    private readonly IntPtr _context;

    public OverlayFontsTests()
    {
        _fonts = new OverlayFonts(_log.Add);
        _context = ImGui.CreateContext();
        ImGui.SetCurrentContext(_context);
    }

    public void Dispose()
    {
        _fonts.Dispose();
        ImGui.DestroyContext(_context);
    }

    private static bool HasGlyph(string glyph)
    {
        ImFontAtlasPtr atlas = ImGui.GetIO().Fonts;
        atlas.GetTexDataAsRGBA32(out IntPtr _, out int _, out int _, out int _);
        ImFontPtr font = atlas.Fonts[0];
        return ImGuiNative.ImFont_FindGlyphNoFallback(font.NativePtr, glyph[0]) != null;
    }

    private void Build()
    {
        ImFontAtlasPtr atlas = ImGui.GetIO().Fonts;
        atlas.Clear();
        atlas.AddFontDefault();
        _fonts.MergeInto(atlas, 13f);
    }

    [Fact]
    public void WithoutAMergeThereAreNoIcons()
    {
        Build();
        Assert.False(HasGlyph(PhosphorIcons.Sparkle));
    }

    [Fact]
    public void AMergedFontAddsItsGlyphs()
    {
        _fonts.Add(PhosphorFont.Load()!, PhosphorIcons.Baked, 1.2f);
        Build();
        Assert.True(HasGlyph(PhosphorIcons.Sparkle));
        Assert.True(HasGlyph(PhosphorIcons.DotsSixVertical));
        Assert.True(HasGlyph("A"));
    }

    private static float MiddleY(string glyph)
    {
        ImFontGlyph* found = ImGuiNative.ImFont_FindGlyphNoFallback(ImGui.GetIO().Fonts.Fonts[0].NativePtr, glyph[0]);
        Assert.True(found != null);
        // Native layout: one bitfield uint, AdvanceX, X0, Y0, X1, Y1 (ImGui.NET's struct has three uints, so its Y0/Y1 are off).
        float* native = (float*)found;
        return (native[3] + native[5]) / 2f;
    }

    [Theory]
    [InlineData(1f)]
    [InlineData(2f)]
    public void IconsSitLevelWithCapitals(float scale)
    {
        _fonts.Add(PhosphorFont.Load()!, PhosphorIcons.Baked, 1.2f);
        ImFontAtlasPtr atlas = ImGui.GetIO().Fonts;
        ImGuiVulkanRenderer.AddFonts(atlas, _fonts, scale);
        atlas.GetTexDataAsRGBA32(out IntPtr _, out int _, out int _, out int _);

        float capital = MiddleY("O");
        Assert.InRange(MiddleY(PhosphorIcons.Sparkle), capital - 1f, capital + 1f);
        Assert.InRange(MiddleY(PhosphorIcons.DotsSixVertical), capital - 1f, capital + 1f);
    }

    // RynthCore's faces draw with font.FontSize and AddText(font, font.FontSize, ...),
    // which a global scale doesn't reach: the font itself has to be in points.
    [Fact]
    public void OnRetinaTheFontKeepsItsPointSizeAndGetsSharper()
    {
        _fonts.Add(PhosphorFont.Load()!, PhosphorIcons.Baked, 1.2f);
        ImFontAtlasPtr atlas = ImGui.GetIO().Fonts;

        ImGuiVulkanRenderer.AddFonts(atlas, _fonts, 1f);
        atlas.GetTexDataAsRGBA32(out IntPtr _, out int width1, out int height1, out int _);
        float advance1 = Advance("W");
        float icon1 = Advance(PhosphorIcons.Sparkle);

        ImGuiVulkanRenderer.AddFonts(atlas, _fonts, 2f);
        atlas.GetTexDataAsRGBA32(out IntPtr _, out int width2, out int height2, out int _);

        Assert.Equal(ImGuiVulkanRenderer.FontSizePoints, atlas.Fonts[0].FontSize);
        Assert.Equal(advance1, Advance("W"));
        Assert.Equal(icon1, Advance(PhosphorIcons.Sparkle));
        Assert.True(width2 * height2 > width1 * height1);
    }

    [Fact]
    public void ASizeIsBakedFromTheNextBuildWithTheMergedIcons()
    {
        _fonts.Add(PhosphorFont.Load()!, PhosphorIcons.Baked, 1.2f);
        ImFontAtlasPtr atlas = ImGui.GetIO().Fonts;
        ImGuiVulkanRenderer.AddFonts(atlas, _fonts, 2f);
        int version = _fonts.Version;

        Assert.Equal((nint)atlas.Fonts[0].NativePtr, _fonts.Sized(ImGuiVulkanRenderer.FontSizePoints));
        Assert.Equal(IntPtr.Zero, _fonts.Sized(10f));
        Assert.Equal(version + 1, _fonts.Version);
        Assert.Equal(IntPtr.Zero, _fonts.Sized(10f));
        Assert.Equal(version + 1, _fonts.Version);

        ImGuiVulkanRenderer.AddFonts(atlas, _fonts, 2f);
        atlas.GetTexDataAsRGBA32(out IntPtr _, out int _, out int _, out int _);
        IntPtr ten = _fonts.Sized(10f);
        Assert.NotEqual(IntPtr.Zero, ten);
        var font = new ImFontPtr((ImFont*)ten);
        Assert.Equal(10f, font.FontSize);
        Assert.True(ImGuiNative.ImFont_FindGlyphNoFallback(font.NativePtr, PhosphorIcons.Sparkle[0]) != null);
        Assert.Equal(version + 1, _fonts.Version);
    }

    [Fact]
    public void ASizeOutsideTheRangeIsRefused()
    {
        int version = _fonts.Version;
        Assert.Equal(IntPtr.Zero, _fonts.Sized(0f));
        Assert.Equal(IntPtr.Zero, _fonts.Sized(500f));
        Assert.Equal(IntPtr.Zero, _fonts.Sized(float.NaN));
        Assert.Equal(version, _fonts.Version);
    }

    private static float Advance(string glyph)
    {
        ImFontGlyph* found = ImGuiNative.ImFont_FindGlyphNoFallback(ImGui.GetIO().Fonts.Fonts[0].NativePtr, glyph[0]);
        Assert.True(found != null);
        return ((float*)found)[1];
    }

    [Fact]
    public void AddingBumpsTheVersionAndBadInputIsIgnored()
    {
        int start = _fonts.Version;
        _fonts.Add(Array.Empty<byte>(), "x", 1f);
        _fonts.Add(new byte[] { 1 }, "", 1f);
        _fonts.Add(new byte[] { 1 }, "x", 0f);
        _fonts.Add(new byte[] { 1 }, "x", float.PositiveInfinity);
        _fonts.Add(new byte[] { 1 }, "\0\0", 1f);
        Assert.Equal(start, _fonts.Version);
        Assert.Equal(0, _fonts.Count);
        Assert.Equal(5, _log.Count(line => line.Contains("ignored", StringComparison.Ordinal)));

        _fonts.Add(PhosphorFont.Load()!, PhosphorIcons.Baked, 1.2f);
        Assert.Equal(start + 1, _fonts.Version);
    }

    [Fact]
    public void RangesJoinConsecutiveCodePoints()
    {
        Assert.Equal(new ushort[] { 0x41, 0x43, 0x45, 0x45, 0 }, OverlayFonts.Ranges("ECBAA"));
    }

    [Fact]
    public void AfterDisposeNothingIsKeptOrMerged()
    {
        _fonts.Add(PhosphorFont.Load()!, PhosphorIcons.Baked, 1.2f);
        Assert.Equal(1, _fonts.Count);
        _fonts.Dispose();
        Assert.Equal(0, _fonts.Count);
        Build();
        Assert.False(HasGlyph(PhosphorIcons.Sparkle));
        _fonts.Dispose();
    }

    [Fact]
    public void AFontAddedAfterDisposeIsIgnored()
    {
        _fonts.Dispose();
        int version = _fonts.Version;
        _fonts.Add(PhosphorFont.Load()!, PhosphorIcons.Baked, 1.2f);
        Assert.Equal(0, _fonts.Count);
        Assert.Equal(version, _fonts.Version);
        Assert.Contains(_log, line => line.Contains("disposed", StringComparison.Ordinal));
    }

    [Fact]
    public void TheSameFontTwiceIsMergedOnce()
    {
        _fonts.Add(PhosphorFont.Load()!, PhosphorIcons.Baked, 1.2f);
        int version = _fonts.Version;
        _fonts.Add(PhosphorFont.Load()!, PhosphorIcons.Baked, 1.2f);
        Assert.Equal(1, _fonts.Count);
        Assert.Equal(version, _fonts.Version);

        _fonts.Add(PhosphorFont.Load()!, PhosphorIcons.Sparkle, 1.2f);
        Assert.Equal(2, _fonts.Count);
    }
}
