using System.Numerics;
using System.Runtime.InteropServices;
using AcDream.App.Rendering.Gpu;
using AcDream.App.Rendering.Gpu.Vk;
using AcDream.Core.Lighting;
using Silk.NET.Vulkan;
using Buffer = Silk.NET.Vulkan.Buffer;

namespace AcDream.App.Tests.Rendering.Gpu.Vk;

public sealed unsafe partial class MeshModernSharedIndexOffscreenTests
{
    private const int Extent = 64;
    private const uint Prefix = 2;
    private static readonly object VulkanLock = new();

    [Trait("Lane", "Vulkan")]
    [Fact]
    public void CommittedProductionOrdinaryShader_RendersLocalSidecarsAtNonzeroTransformPrefix()
    {
        lock (VulkanLock)
        {
            string shaderDirectory = Path.Combine(
                HeadlessVulkanTestHost.RepositoryRoot(), "src", "AcDream.App", "Rendering", "Shaders", "spv");
            Assert.Equal(
                Path.GetFullPath(Path.Combine(
                    HeadlessVulkanTestHost.RepositoryRoot(), "src", "AcDream.App", "Rendering", "Shaders", "spv")),
                Path.GetFullPath(shaderDirectory));
            Assert.True(File.Exists(Path.Combine(shaderDirectory, "mesh_modern.vert.spv")));

            using var host = HeadlessVulkanTestHost.Create(shaderDirectory);
            byte[] pixels = Render(host.Device, host.Vk, host.PhysicalDevice, host.LogicalDevice, host.Queue, host.QueueFamily);

            int dark = CountPixels(pixels, 51);
            int bright = CountPixels(pixels, 179);
            Assert.True(dark > 64, $"Expected a dark local-sidecar instance, found {dark} matching pixels.");
            Assert.True(bright > 64, $"Expected a bright local-sidecar instance, found {bright} matching pixels.");
        }
    }

    [Fact]
    public void ReadbackDependency_PublishesTheExactCopiedRangeBeforeEndAndSubmit()
    {
        const ulong byteCount = 16_384;
        var readback = new Buffer(0x470u);

        BufferMemoryBarrier2 barrier = VulkanImageReadback.CreateHostReadBarrier(readback, byteCount);
        Assert.Equal(StructureType.BufferMemoryBarrier2, barrier.SType);
        Assert.Equal(PipelineStageFlags2.CopyBit, barrier.SrcStageMask);
        Assert.Equal(AccessFlags2.TransferWriteBit, barrier.SrcAccessMask);
        Assert.Equal(PipelineStageFlags2.HostBit, barrier.DstStageMask);
        Assert.Equal(AccessFlags2.HostReadBit, barrier.DstAccessMask);
        Assert.Equal(Silk.NET.Vulkan.Vk.QueueFamilyIgnored, barrier.SrcQueueFamilyIndex);
        Assert.Equal(Silk.NET.Vulkan.Vk.QueueFamilyIgnored, barrier.DstQueueFamilyIndex);
        Assert.Equal(readback.Handle, barrier.Buffer.Handle);
        Assert.Equal(0ul, barrier.Offset);
        Assert.Equal(byteCount, barrier.Size);

        string source = File.ReadAllText(Path.Combine(
            HeadlessVulkanTestHost.RepositoryRoot(), "tests", "AcDream.App.Tests", "Rendering", "Gpu", "Vk",
            "VulkanImageReadback.cs"));
        int readbackStart = source.LastIndexOf("internal static byte[] ReadBack(", StringComparison.Ordinal);
        int readbackEnd = source.LastIndexOf("internal static BufferMemoryBarrier2 CreateHostReadBarrier(", StringComparison.Ordinal);
        Assert.True(readbackStart >= 0 && readbackEnd > readbackStart);
        string livePath = source[readbackStart..readbackEnd];

        int copy = livePath.IndexOf("vk.CmdCopyImageToBuffer(commands, image, ImageLayout.TransferSrcOptimal, readback, 1, &copy);", StringComparison.Ordinal);
        int descriptor = livePath.IndexOf("BufferMemoryBarrier2 hostReadBarrier = CreateHostReadBarrier(readback, byteCount);", StringComparison.Ordinal);
        int dependency = livePath.IndexOf("PBufferMemoryBarriers = &hostReadBarrier", StringComparison.Ordinal);
        int publish = livePath.IndexOf("vk.CmdPipelineBarrier2(commands, &hostDependency);", StringComparison.Ordinal);
        int end = livePath.IndexOf("vk.EndCommandBuffer(commands)", StringComparison.Ordinal);
        int submit = livePath.IndexOf("vk.QueueSubmit2(queue, 1, &submit, default)", StringComparison.Ordinal);
        Assert.True(
            copy >= 0 && copy < descriptor && descriptor < dependency && dependency < publish
                && publish < end && end < submit,
            $"Expected copy -> descriptor -> barrier -> end -> submit, got {copy}, {descriptor}, {dependency}, {publish}, {end}, {submit}.");
    }

    private static byte[] Render(
        VulkanGpuDevice device,
        Silk.NET.Vulkan.Vk vk,
        PhysicalDevice physicalDevice,
        Device logicalDevice,
        Queue queue,
        uint queueFamily)
    {
        using IGpuBuffer vertices = device.CreateBuffer(new GpuBufferDescription(
            "s5-470-vertices",
            4 * Marshal.SizeOf<Vertex>(),
            GpuBufferUsage.Vertex | GpuBufferUsage.TransferDestination,
            GpuMemoryResidency.DeviceLocal));
        using IGpuBuffer indices = device.CreateBuffer(new GpuBufferDescription(
            "s5-470-indices",
            6 * sizeof(ushort),
            GpuBufferUsage.Index | GpuBufferUsage.TransferDestination,
            GpuMemoryResidency.DeviceLocal));
        Vertex[] vertexData =
        [
            new(new Vector3(-0.45f, -0.45f, 0f), Vector3.UnitZ, Vector2.Zero),
            new(new Vector3( 0.45f, -0.45f, 0f), Vector3.UnitZ, Vector2.UnitX),
            new(new Vector3( 0.45f,  0.45f, 0f), Vector3.UnitZ, Vector2.One),
            new(new Vector3(-0.45f,  0.45f, 0f), Vector3.UnitZ, Vector2.UnitY),
        ];
        vertices.Upload(0, MemoryMarshal.AsBytes<Vertex>(vertexData));
        indices.Upload(0, MemoryMarshal.AsBytes<ushort>([0, 1, 2, 2, 3, 0]));

        using IGpuRenderTarget target = device.CreateRenderTarget(new GpuRenderTargetDescription(
            "s5-470-offscreen",
            Extent,
            Extent,
            GpuTextureFormat.Rgba8UnormRenderTarget,
            DepthFormat: null,
            SampleCount: 1));
        using IGpuPipeline pipeline = device.CreatePipeline(new GpuPipelineDescription
        {
            Name = "s5-470-mesh-modern",
            Shaders = new GpuShaderSet("mesh_modern"),
            VertexLayout = GpuVertexLayout.WorldMesh,
            Topology = GpuPrimitiveTopology.TriangleList,
            Blend = GpuBlendMode.None,
            Depth = GpuDepthState.Disabled,
            Cull = GpuCullMode.None,
            SampleCount = 1,
        });
        Assert.False(pipeline.Description.Shaders.HasEmbeddedSpirv);
        Assert.Equal("mesh_modern", pipeline.Description.Shaders.Name);

        using (IGpuFrame frame = device.BeginFrame())
        {
            using IGpuPassEncoder encoder = frame.BeginPass(new GpuPassDescription
            {
                Name = "s5-470-shared-index-witness",
                Color = new GpuColorAttachment(
                    target,
                    GpuLoadOp.Clear,
                    GpuStoreOp.Store,
                    new Vector4(0f, 0f, 0f, 1f)),
                Depth = null,
                SampleCount = 1,
            });
            encoder.BindPipeline(pipeline);

            GpuPushConstants constants = GpuPushConstants.Default;
            constants.LightDebug = 3;
            constants.TextureIndexB = Prefix;
            encoder.SetPushConstants(constants);

            Matrix4x4[] transforms =
            [
                Matrix4x4.CreateTranslation(20f, 20f, 0f),
                Matrix4x4.CreateTranslation(-20f, -20f, 0f),
                Matrix4x4.CreateTranslation(-0.5f, 0f, 0f),
                Matrix4x4.CreateTranslation( 0.5f, 0f, 0f),
            ];
            BindStorage(frame, encoder, GpuBindingModel.StorageInstances, transforms);
            BindStorage(frame, encoder, GpuBindingModel.StorageBatches,
                [new BatchData(device.DefaultTextureSlot.Index, 1f, 0u, 1u)]);
            BindStorage(frame, encoder, GpuBindingModel.StorageClipSlots, [17u, 29u]);
            BindStorage(frame, encoder, GpuBindingModel.StorageGlobalLights, [GlobalLight.Zero]);
            BindStorage(frame, encoder, GpuBindingModel.StorageInstanceLightSets,
                [-1, -1, -1, -1, -1, -1, -1, -1, 0, -1, -1, -1, -1, -1, -1, -1]);
            BindStorage(frame, encoder, GpuBindingModel.StorageInstanceIndoor, [0u, 1u]);
            BindStorage(frame, encoder, GpuBindingModel.StorageInstanceAlpha, [0.25f, 0.75f]);
            BindStorage(frame, encoder, GpuBindingModel.StorageInstanceSelectionLighting,
                [new Vector2(0.2f, 0f), new Vector2(0.7f, 0f)]);
            BindStorage(frame, encoder, GpuBindingModel.StorageInstanceDetailCategory, [3u, 9u]);

            SceneLightingUbo lighting = default;
            BindUniform(frame, encoder, GpuBindingModel.UniformSceneLighting, lighting);
            encoder.BindVertexBuffer(0, vertices, 0);
            encoder.BindIndexBuffer(indices, 0, GpuIndexType.UInt16);
            encoder.DrawIndexed(6, 2, 0, 0, Prefix);
        }

        device.WaitIdle();
        VulkanGpuRenderTarget vkTarget = Assert.IsType<VulkanGpuRenderTarget>(target);
        return VulkanImageReadback.ReadBack(
            vk,
            physicalDevice,
            logicalDevice,
            queue,
            queueFamily,
            vkTarget.ColorResult.Image,
            Extent,
            Extent);
    }

    private static void BindStorage<T>(
        IGpuFrame frame,
        IGpuPassEncoder encoder,
        uint binding,
        T[] values)
        where T : unmanaged
    {
        GpuRingAllocation allocation = frame.AllocateRing(
            checked(values.Length * Marshal.SizeOf<T>()),
            GpuRingUsage.Storage);
        values.AsSpan().CopyTo(allocation.AsSpan<T>());
        encoder.BindStorageBuffer(binding, allocation.Buffer, allocation.OffsetBytes, (uint)allocation.Data.Length);
    }

    private static void BindUniform<T>(
        IGpuFrame frame,
        IGpuPassEncoder encoder,
        uint binding,
        T value)
        where T : unmanaged
    {
        GpuRingAllocation allocation = frame.AllocateRing(Marshal.SizeOf<T>(), GpuRingUsage.Uniform);
        allocation.AsSpan<T>()[0] = value;
        encoder.BindUniformBuffer(binding, allocation.Buffer, allocation.OffsetBytes, (uint)allocation.Data.Length);
    }

    private static int CountPixels(ReadOnlySpan<byte> pixels, byte expected)
    {
        int count = 0;
        for (int offset = 0; offset < pixels.Length; offset += 4)
        {
            if (Math.Abs(pixels[offset + 0] - expected) <= 2
                && Math.Abs(pixels[offset + 1] - expected) <= 2
                && Math.Abs(pixels[offset + 2] - expected) <= 2
                && pixels[offset + 3] >= 253)
            {
                count++;
            }
        }
        return count;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    private readonly record struct Vertex(Vector3 Position, Vector3 Normal, Vector2 TexCoord);

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    private readonly record struct BatchData(
        uint TextureIndex,
        float SurfaceOpacity,
        uint TextureLayer,
        uint Flags);

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    private readonly record struct GlobalLight(
        Vector4 PositionAndKind,
        Vector4 DirectionAndRange,
        Vector4 ColorAndIntensity,
        Vector4 ConeAngleEtc)
    {
        internal static GlobalLight Zero { get; } = default;
    }
}
