using AcDream.App.Rendering.Gpu;
using AcDream.App.Rendering.Gpu.Vk;
using Silk.NET.Vulkan;

namespace AcDream.App.Tests.Rendering.Gpu.Vk;

/// <summary>
/// A headless Vulkan device, with no window or swapchain, for the Lane=Vulkan offscreen proofs.
/// </summary>
internal sealed unsafe class HeadlessVulkanTestHost : IDisposable
{
    private bool _disposed;

    private HeadlessVulkanTestHost(
        Silk.NET.Vulkan.Vk vk,
        Instance instance,
        PhysicalDevice physicalDevice,
        Device logicalDevice,
        Queue queue,
        uint queueFamily,
        VulkanGpuDevice device)
    {
        Vk = vk;
        Instance = instance;
        PhysicalDevice = physicalDevice;
        LogicalDevice = logicalDevice;
        Queue = queue;
        QueueFamily = queueFamily;
        Device = device;
    }

    internal Silk.NET.Vulkan.Vk Vk { get; }
    internal Instance Instance { get; }
    internal PhysicalDevice PhysicalDevice { get; }
    internal Device LogicalDevice { get; }
    internal Queue Queue { get; }
    internal uint QueueFamily { get; }
    internal VulkanGpuDevice Device { get; }

    internal static HeadlessVulkanTestHost Create(string shaderDirectory)
    {
        Silk.NET.Vulkan.Vk vk = Silk.NET.Vulkan.Vk.GetApi();
        Instance instance = default;
        Device logicalDevice = default;
        VulkanGpuDevice? gpuDevice = null;
        try
        {
            instance = VulkanInstanceFactory.Create(vk, [], enableOptionalExtensions: false).Instance;
            IReadOnlyList<VulkanPhysicalDeviceCandidate> candidates =
                VulkanPhysicalDeviceInspector.Enumerate(vk, instance, out PhysicalDevice[] handles);
            VulkanPhysicalDeviceChoice selected = VulkanPhysicalDeviceSelection.Choose(candidates, null)
                ?? throw new NotSupportedException("S5-470 offscreen proof found no Vulkan physical device.");
            PhysicalDevice physicalDevice = handles[selected.Device.Index];
            VulkanDeviceFeatureSupport features = VulkanPhysicalDeviceInspector.ReadFeatures(vk, physicalDevice);
            uint queueFamily = VulkanQueueFamilySelection.ChooseGraphicsOnly(
                VulkanPhysicalDeviceInspector.ReadQueueFamilies(vk, physicalDevice, surfaceApi: null, default))
                ?? throw new NotSupportedException("S5-470 offscreen proof found no graphics queue.");
            VulkanLogicalDeviceFactory.Created created = VulkanLogicalDeviceFactory.Create(
                vk,
                physicalDevice,
                new VulkanQueueFamilyChoice(queueFamily, queueFamily),
                requireSwapchain: false,
                features);
            logicalDevice = created.Device;
            VulkanDeviceLimitSupport limits = VulkanPhysicalDeviceInspector.ReadLimits(vk, physicalDevice);
            VulkanFormatSupport formats = VulkanPhysicalDeviceInspector.ReadFormats(
                vk, physicalDevice, surfaceOffersUnorm: true);
            gpuDevice = new VulkanGpuDevice(
                vk,
                physicalDevice,
                logicalDevice,
                created.GraphicsQueue,
                created.GraphicsQueue,
                queueFamily,
                features,
                limits,
                formats,
                selected.Device.DeviceName,
                VulkanPhysicalDeviceInspector.DescribeDriver(selected.Device),
                VulkanApiVersion.Describe(selected.Device.ApiVersion),
                VulkanDebugNames.Disabled,
                backbuffer: null,
                shaderSpirvDirectory: shaderDirectory,
                pipelineCacheDirectory: null,
                memoryProfile: GpuMemoryProfile.Default with
                {
                    RingCapacityBytesPerSlot = 2 * 1024 * 1024,
                },
                framesInFlight: 1);
            return new HeadlessVulkanTestHost(
                vk,
                instance,
                physicalDevice,
                logicalDevice,
                created.GraphicsQueue,
                queueFamily,
                gpuDevice);
        }
        catch
        {
            gpuDevice?.Dispose();
            if (logicalDevice.Handle != 0)
                vk.DestroyDevice(logicalDevice, null);
            if (instance.Handle != 0)
                vk.DestroyInstance(instance, null);
            vk.Dispose();
            throw;
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        Device.Dispose();
        if (LogicalDevice.Handle != 0)
            Vk.DestroyDevice(LogicalDevice, null);
        if (Instance.Handle != 0)
            Vk.DestroyInstance(Instance, null);
        Vk.Dispose();
    }

    /// <summary>The committed SPIR-V directory the production pipelines load from.</summary>
    internal static string CommittedShaderDirectory() =>
        Path.Combine(RepositoryRoot(), "src", "AcDream.App", "Rendering", "Shaders", "spv");

    /// <summary>The repository root, found by walking up to <c>AcDream.slnx</c>.</summary>
    internal static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "AcDream.slnx")))
            directory = directory.Parent;
        return directory?.FullName
            ?? throw new InvalidOperationException("Could not locate the repository root.");
    }

    /// <summary>Reads a render target's colour back once the device is idle.</summary>
    internal byte[] ReadBack(IGpuRenderTarget target, int width, int height) =>
        VulkanImageReadback.ReadBack(
            Vk,
            PhysicalDevice,
            LogicalDevice,
            Queue,
            QueueFamily,
            ((VulkanGpuRenderTarget)target).ColorResult.Image,
            width,
            height);
}
