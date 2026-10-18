using AcDream.Rynth.Host;
using ImGuiNET;
using RynthCore.Engine.ImGuiBackend;

namespace AcDream.Rynth.Host.Tests;

public sealed class RynthBarIconsTests
{
    [Fact]
    public void KnownTitlesHaveRynthCoresIcons()
    {
        Assert.Equal(PhosphorIcons.Sparkle, RynthBarIcons.Icon("Oracle"));
        Assert.Equal(PhosphorIcons.ChartLineUp, RynthBarIcons.Icon("tracker"));
        Assert.Null(RynthBarIcons.Icon("Mystery"));
    }

    [Fact]
    public void WithoutTheIconFontTheLabelIsTheFirstLetter()
    {
        using var imgui = new HeadlessImGui();
        Assert.Equal("O", RynthBarIcons.Label("Oracle"));
        Assert.Equal("M", RynthBarIcons.Label("  mystery"));
        Assert.Equal("?", RynthBarIcons.Label("--"));
    }

    [Fact]
    public unsafe void WithTheIconFontTheLabelIsTheIcon()
    {
        IntPtr context = ImGui.CreateContext();
        try
        {
            ImGui.SetCurrentContext(context);
            byte[] ttf = PhosphorFont.Load()!;
            ushort sparkle = PhosphorIcons.Sparkle[0];
            fixed (byte* data = ttf)
            {
                ImFontAtlasPtr atlas = ImGui.GetIO().Fonts;
                atlas.AddFontDefault();
                ImFontConfigPtr config = ImGuiNative.ImFontConfig_ImFontConfig();
                config.MergeMode = true;
                config.FontDataOwnedByAtlas = false;
                ushort* ranges = stackalloc ushort[] { sparkle, sparkle, 0 };
                atlas.AddFontFromMemoryTTF((IntPtr)data, ttf.Length, 13f, config, (IntPtr)ranges);
                atlas.GetTexDataAsRGBA32(out IntPtr _, out int _, out int _, out int _);
                config.Destroy();
            }

            ImGui.GetIO().DisplaySize = new System.Numerics.Vector2(800, 600);
            ImGui.NewFrame();
            try
            {
                Assert.Equal(PhosphorIcons.Sparkle, RynthBarIcons.Label("Oracle"));
                Assert.Equal("T", RynthBarIcons.Label("Tracker"));
            }
            finally
            {
                ImGui.EndFrame();
            }
        }
        finally
        {
            ImGui.DestroyContext(context);
        }
    }
}
