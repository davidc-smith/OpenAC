using AcDream.App.Rendering.Gpu;
using AcDream.App.Rendering.Gpu.Vk;
using Silk.NET.Vulkan;

namespace AcDream.App.Tests.Rendering.Gpu.Vk;

public sealed class VulkanTextureSwizzleTests
{
    [Fact]
    public void ACoverageMaskShowsWhiteWithTheStoredValueAsAlpha()
    {
        ComponentMapping mapping = VulkanTextureFormatMapping.ComponentsOf(GpuTextureSwizzle.CoverageAsAlpha);

        Assert.Equal(ComponentSwizzle.One, mapping.R);
        Assert.Equal(ComponentSwizzle.One, mapping.G);
        Assert.Equal(ComponentSwizzle.One, mapping.B);
        Assert.Equal(ComponentSwizzle.R, mapping.A);
    }

    [Fact]
    public void AReplicatedValueIsInEveryChannel()
    {
        ComponentMapping mapping = VulkanTextureFormatMapping.ComponentsOf(GpuTextureSwizzle.Replicate);

        Assert.Equal(
            (ComponentSwizzle.R, ComponentSwizzle.R, ComponentSwizzle.R, ComponentSwizzle.R),
            (mapping.R, mapping.G, mapping.B, mapping.A));
    }

    [Fact]
    public void TheIdentityIsVulkansDefaultMapping()
    {
        ComponentMapping mapping = VulkanTextureFormatMapping.ComponentsOf(GpuTextureSwizzle.Identity);

        Assert.Equal(
            (ComponentSwizzle.Identity, ComponentSwizzle.Identity, ComponentSwizzle.Identity, ComponentSwizzle.Identity),
            (mapping.R, mapping.G, mapping.B, mapping.A));
    }

    [Fact]
    public void StoredBgraIsTheVulkanBgraFormat()
    {
        Assert.Equal(Format.B8G8R8A8Unorm, VulkanTextureFormatMapping.FormatOf(GpuTextureFormat.Bgra8Unorm));
        Assert.Equal(4, VulkanTextureFormatMapping.BytesPerTexel(GpuTextureFormat.Bgra8Unorm));
        Assert.Equal(16, VulkanTextureFormatMapping.LevelSizeBytes(GpuTextureFormat.Bgra8Unorm, 2, 2));
    }

    [Fact]
    public void ADescriptionIsUnswizzledUnlessItAsks()
    {
        var description = new GpuTextureDescription("t", GpuTextureKind.Texture2D, GpuTextureFormat.R8Unorm, 1, 1, 1, 1);

        Assert.Equal(GpuTextureSwizzle.Identity, description.Swizzle);
    }
}
