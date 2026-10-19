using ImGuiNET;
using RynthCore.Engine.ImGuiBackend;

namespace AcDream.Rynth.Host.Tests;

public sealed unsafe class ImGuiFontsTests : IDisposable
{
    private readonly IntPtr _context;

    public ImGuiFontsTests()
    {
        ImGuiFonts.Source = null;
        _context = ImGui.CreateContext();
        ImGui.SetCurrentContext(_context);
        ImGuiIOPtr io = ImGui.GetIO();
        io.Fonts.AddFontDefault();
        io.Fonts.GetTexDataAsRGBA32(out IntPtr _, out int _, out int _, out int _);
        io.Fonts.SetTexID(1);
        io.DisplaySize = new System.Numerics.Vector2(800, 600);
        io.DeltaTime = 1f / 60f;
        ImGui.NewFrame();   // the current font is set by a frame
    }

    public void Dispose()
    {
        ImGuiFonts.Source = null;
        ImGui.EndFrame();
        ImGui.DestroyContext(_context);
    }

    [Fact]
    public void WithoutASourceChatIsTheCurrentFont()
    {
        Assert.Equal((nint)ImGui.GetIO().Fonts.Fonts[0].NativePtr, (nint)ImGuiFonts.Chat(14f).NativePtr);
    }

    // RynthCore bakes chat fonts at 8, 10, ... 18 points and picks the nearest.
    [Theory]
    [InlineData(10f, 10f)]
    [InlineData(8f, 8f)]
    [InlineData(18f, 18f)]
    [InlineData(13f, 12f)]
    [InlineData(4f, 8f)]
    [InlineData(30f, 18f)]
    public void ChatAsksTheSourceForRynthCoresNearestChatSize(float points, float asked)
    {
        var sizes = new List<float>();
        ImFontPtr font = ImGui.GetIO().Fonts.Fonts[0];
        ImGuiFonts.Source = p => { sizes.Add(p); return (IntPtr)font.NativePtr; };

        Assert.Equal((nint)font.NativePtr, (nint)ImGuiFonts.Chat(points).NativePtr);
        Assert.Equal(new[] { asked }, sizes);
    }

    [Fact]
    public void ASizeNotBakedYetIsTheCurrentFont()
    {
        ImGuiFonts.Source = _ => IntPtr.Zero;
        Assert.Equal((nint)ImGui.GetIO().Fonts.Fonts[0].NativePtr, (nint)ImGuiFonts.Chat(10f).NativePtr);
    }
}
