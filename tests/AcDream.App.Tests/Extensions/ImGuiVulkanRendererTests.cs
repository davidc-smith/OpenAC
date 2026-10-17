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
    public void OnlyTheAtlasTextureIsDrawable()
    {
        var atlas = new GpuTextureSlot(7);
        Assert.True(ImGuiVulkanRenderer.IsDrawableTexture((IntPtr)7, atlas));
        Assert.False(ImGuiVulkanRenderer.IsDrawableTexture(IntPtr.Zero, atlas));
        Assert.False(ImGuiVulkanRenderer.IsDrawableTexture(unchecked((IntPtr)0x7FF6_1234_5678L), atlas));
        Assert.False(ImGuiVulkanRenderer.IsDrawableTexture((IntPtr)8, atlas));
    }

    [Fact]
    public void NothingIsDrawableWithoutAnAtlas()
    {
        GpuTextureSlot none = GpuTextureSlot.Unassigned;
        Assert.False(ImGuiVulkanRenderer.IsDrawableTexture(unchecked((IntPtr)uint.MaxValue), none));
        Assert.False(ImGuiVulkanRenderer.IsDrawableTexture(IntPtr.Zero, none));
        Assert.False(ImGuiVulkanRenderer.IsDrawableTexture((IntPtr)7, none));
    }
}
