using AcDream.App.Extensions.Overlay;
using AcDream.App.Rendering.Gpu;

namespace AcDream.App.Tests.Extensions;

public sealed class ImGuiVulkanRendererTests
{
    [Theory]
    [InlineData("AcDream.App.Overlay.imgui_overlay.vert.spv")]
    [InlineData("AcDream.App.Overlay.imgui_overlay.frag.spv")]
    public void EmbeddedShadersAreSpirv(string resource)
    {
        byte[] spirv = ImGuiVulkanRenderer.LoadSpirv(resource);
        Assert.True(spirv.Length > 16);
        Assert.Equal(0, spirv.Length % 4);
        Assert.Equal(0x07230203u, BitConverter.ToUInt32(spirv, 0));
    }

    [Fact]
    public void VertexLayoutMatchesImDrawVert()
    {
        GpuVertexLayout layout = ImGuiVulkanRenderer.VertexLayout;
        Assert.Equal(20u, layout.StrideBytes);
        Assert.Equal(GpuVertexFormat.Float2, layout.Attributes[0].Format);
        Assert.Equal(8u, layout.Attributes[1].OffsetBytes);
        Assert.Equal(GpuVertexFormat.UByte4Normalized, layout.Attributes[2].Format);
        Assert.Equal(16u, layout.Attributes[2].OffsetBytes);
        unsafe { Assert.Equal(20, sizeof(ImGuiNET.ImDrawVert)); }
    }

    [Fact]
    public void TheAtlasIsDrawnByItsHandle()
    {
        var atlas = new GpuTextureSlot(7);
        Assert.True(ImGuiVulkanRenderer.TryGetDrawSlot((IntPtr)8, atlas, null, out uint slot));
        Assert.Equal(7u, slot);
        Assert.False(ImGuiVulkanRenderer.TryGetDrawSlot((IntPtr)7, atlas, null, out _));
        Assert.False(ImGuiVulkanRenderer.TryGetDrawSlot(IntPtr.Zero, atlas, null, out _));
        Assert.False(ImGuiVulkanRenderer.TryGetDrawSlot(unchecked((IntPtr)0x7FF6_1234_5678L), atlas, null, out _));
    }

    [Fact]
    public void IssuedIconHandlesAreDrawnAtTheirSlot()
    {
        var atlas = new GpuTextureSlot(7);
        Func<IntPtr, bool> issued = id => id == (IntPtr)40;
        Assert.True(ImGuiVulkanRenderer.TryGetDrawSlot((IntPtr)40, atlas, issued, out uint slot));
        Assert.Equal(39u, slot);
        Assert.False(ImGuiVulkanRenderer.TryGetDrawSlot((IntPtr)41, atlas, issued, out _));
    }

    [Fact]
    public void NothingIsDrawableWithoutAnAtlasOrIssuedIcons()
    {
        GpuTextureSlot none = GpuTextureSlot.Unassigned;
        Assert.False(ImGuiVulkanRenderer.TryGetDrawSlot(unchecked((IntPtr)uint.MaxValue), none, null, out _));
        Assert.False(ImGuiVulkanRenderer.TryGetDrawSlot(IntPtr.Zero, none, _ => true, out _));
        Assert.False(ImGuiVulkanRenderer.TryGetDrawSlot((IntPtr)8, none, null, out _));
    }
}

[Collection("Rynth")]
public sealed class ImGuiVulkanRendererAtlasTests : IDisposable
{
    private readonly IntPtr _context;
    private readonly AcDream.App.Tests.Rendering.Gpu.RecordingGpuDevice _device = new();
    private readonly OverlayFonts _fonts = new(_ => { });

    public ImGuiVulkanRendererAtlasTests()
    {
        _context = ImGuiNET.ImGui.CreateContext();
        ImGuiNET.ImGui.SetCurrentContext(_context);
    }

    public void Dispose()
    {
        ImGuiNET.ImGui.DestroyContext(_context);
        _fonts.Dispose();
    }

    private uint[] AtlasSlots() => _device.Calls
        .OfType<AcDream.App.Tests.Rendering.Gpu.GpuRecordedTextureRegistration>()
        .Where(call => call.TextureName == "imgui-font-atlas")
        .Select(call => call.Slot)
        .ToArray();

    [Fact]
    public void TheAtlasIdIsItsHandleAndANewFontRebuildsIt()
    {
        using var renderer = new ImGuiVulkanRenderer(_device, new NoFrames(), null, _fonts);
        ImGuiNET.ImGuiIOPtr io = ImGuiNET.ImGui.GetIO();

        renderer.PrepareFrame(io, System.Numerics.Vector2.One);
        uint slot = Assert.Single(AtlasSlots());
        Assert.Equal((IntPtr)(slot + 1), io.Fonts.TexID);
        Assert.True(ImGuiVulkanRenderer.TryGetDrawSlot(io.Fonts.TexID, new GpuTextureSlot(slot), null, out uint drawn));
        Assert.Equal(slot, drawn);

        renderer.PrepareFrame(io, System.Numerics.Vector2.One);
        Assert.Single(AtlasSlots());

        _fonts.Add(RynthCore.Engine.ImGuiBackend.PhosphorFont.Load()!, RynthCore.Engine.ImGuiBackend.PhosphorIcons.Baked, 1.2f);
        renderer.PrepareFrame(io, System.Numerics.Vector2.One);
        uint[] slots = AtlasSlots();
        Assert.Equal(2, slots.Length);
        Assert.Equal((IntPtr)(slots[1] + 1), io.Fonts.TexID);
    }

    private sealed class NoFrames : AcDream.App.Rendering.ICurrentGpuFrameSource
    {
        public IGpuFrame? CurrentFrame => null;
    }
}
