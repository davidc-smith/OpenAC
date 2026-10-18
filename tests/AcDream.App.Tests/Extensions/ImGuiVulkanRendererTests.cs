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
