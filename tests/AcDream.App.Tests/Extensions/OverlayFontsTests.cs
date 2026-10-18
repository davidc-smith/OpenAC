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

    [Fact]
    public void AddingBumpsTheVersionAndBadInputIsIgnored()
    {
        int start = _fonts.Version;
        _fonts.Add(Array.Empty<byte>(), "x", 1f);
        _fonts.Add(new byte[] { 1 }, "", 1f);
        _fonts.Add(new byte[] { 1 }, "x", 0f);
        Assert.Equal(start, _fonts.Version);
        Assert.Equal(3, _log.Count(line => line.Contains("ignored", StringComparison.Ordinal)));

        _fonts.Add(PhosphorFont.Load()!, PhosphorIcons.Baked, 1.2f);
        Assert.Equal(start + 1, _fonts.Version);
    }

    [Fact]
    public void RangesJoinConsecutiveCodePoints()
    {
        Assert.Equal(new ushort[] { 0x41, 0x43, 0x45, 0x45, 0 }, OverlayFonts.Ranges("ECBAA"));
    }

    [Fact]
    public void AfterDisposeNothingIsMerged()
    {
        _fonts.Add(PhosphorFont.Load()!, PhosphorIcons.Baked, 1.2f);
        _fonts.Dispose();
        Build();
        Assert.False(HasGlyph(PhosphorIcons.Sparkle));
    }
}
