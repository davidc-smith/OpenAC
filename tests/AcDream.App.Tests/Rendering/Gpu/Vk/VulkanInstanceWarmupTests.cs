using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using AcDream.App.Rendering.Gpu.Vk;
using Silk.NET.Vulkan;

namespace AcDream.App.Tests.Rendering.Gpu.Vk;

public sealed class VulkanInstanceWarmupTests
{
    private static readonly string[] Surface = ["VK_KHR_surface", "VK_KHR_win32_surface"];

    private static VulkanInstanceWarmup.Warm Made(bool optional, params string[] extensions) =>
        new(
            null!,
            new VulkanInstanceFactory.Created(
                new Instance(42),
                extensions,
                [],
                0u),
            optional);

    [Fact]
    public void TheWindowTakesAnInstanceThatHasEverySurfaceExtension()
    {
        var destroyed = new List<VulkanInstanceWarmup.Warm>();
        VulkanInstanceWarmup.Warm made = Made(false, [.. Surface, "VK_EXT_debug_utils"]);
        using var warmup = new VulkanInstanceWarmup(Task.FromResult(made), destroyed.Add);

        Assert.Same(made, warmup.TryTake(Surface, enableOptionalExtensions: false));
        Assert.Null(warmup.TryTake(Surface, enableOptionalExtensions: false));
        warmup.Dispose();
        Assert.Empty(destroyed);
    }

    [Fact]
    public void AnInstanceMissingASurfaceExtensionIsDestroyedAndNotHandedOver()
    {
        var destroyed = new List<VulkanInstanceWarmup.Warm>();
        VulkanInstanceWarmup.Warm made = Made(false, "VK_KHR_surface");
        var warmup = new VulkanInstanceWarmup(Task.FromResult(made), destroyed.Add);

        Assert.Null(warmup.TryTake(Surface, enableOptionalExtensions: false));
        Assert.Equal([made], destroyed);
        warmup.Dispose();
        Assert.Single(destroyed);
    }

    [Fact]
    public void AnInstanceMadeWithOtherOptionalExtensionsIsDestroyedAndNotHandedOver()
    {
        var destroyed = new List<VulkanInstanceWarmup.Warm>();
        VulkanInstanceWarmup.Warm made = Made(false, Surface);
        var warmup = new VulkanInstanceWarmup(Task.FromResult(made), destroyed.Add);

        Assert.Null(warmup.TryTake(Surface, enableOptionalExtensions: true));
        Assert.Equal([made], destroyed);
    }

    [Fact]
    public void AFailedCreationLeavesTheWindowToCreateItsOwn()
    {
        var destroyed = new List<VulkanInstanceWarmup.Warm>();
        var warmup = new VulkanInstanceWarmup(
            Task.FromException<VulkanInstanceWarmup.Warm>(new NotSupportedException("no driver")),
            destroyed.Add);

        Assert.Null(warmup.TryTake(Surface, enableOptionalExtensions: false));
        warmup.Dispose();
        Assert.Empty(destroyed);
    }

    [Fact]
    public void AnInstanceNoWindowTookIsDestroyedOnDispose()
    {
        var destroyed = new List<VulkanInstanceWarmup.Warm>();
        VulkanInstanceWarmup.Warm made = Made(false, Surface);
        var warmup = new VulkanInstanceWarmup(Task.FromResult(made), destroyed.Add);

        warmup.Dispose();
        warmup.Dispose();

        Assert.Equal([made], destroyed);
        Assert.Null(warmup.TryTake(Surface, enableOptionalExtensions: false));
    }
}
