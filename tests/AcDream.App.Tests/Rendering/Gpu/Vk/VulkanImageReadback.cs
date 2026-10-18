using AcDream.App.Rendering.Gpu.Vk;
using Silk.NET.Vulkan;
using Buffer = Silk.NET.Vulkan.Buffer;

namespace AcDream.App.Tests.Rendering.Gpu.Vk;

/// <summary>
/// Copies a colour image the device has finished with into host memory, for
/// the offscreen proofs in the Vulkan lane. The image is expected in
/// shader-read layout, which is where a render target is left after its pass.
/// </summary>
internal static unsafe class VulkanImageReadback
{
    internal static byte[] ReadBack(
        Silk.NET.Vulkan.Vk vk,
        PhysicalDevice physicalDevice,
        Device device,
        Queue queue,
        uint queueFamily,
        Image image,
        int width,
        int height)
    {
        uint byteCount = (uint)width * (uint)height * 4u;
        Buffer readback = default;
        DeviceMemory memory = default;
        CommandPool pool = default;
        try
        {
            var bufferCreate = new BufferCreateInfo
            {
                SType = StructureType.BufferCreateInfo,
                Size = byteCount,
                Usage = BufferUsageFlags.TransferDstBit,
                SharingMode = SharingMode.Exclusive,
            };
            VulkanInterop.Check(vk.CreateBuffer(device, &bufferCreate, null, out readback), "vkCreateBuffer (S5-470 readback)");
            vk.GetBufferMemoryRequirements(device, readback, out MemoryRequirements requirements);
            uint memoryType = VulkanActiveDeviceProbe.FindMemoryType(
                vk,
                physicalDevice,
                requirements.MemoryTypeBits,
                MemoryPropertyFlags.HostVisibleBit | MemoryPropertyFlags.HostCoherentBit)
                ?? throw new NotSupportedException("S5-470 requires coherent host-visible readback memory.");
            var memoryAllocate = new MemoryAllocateInfo
            {
                SType = StructureType.MemoryAllocateInfo,
                AllocationSize = requirements.Size,
                MemoryTypeIndex = memoryType,
            };
            VulkanInterop.Check(vk.AllocateMemory(device, &memoryAllocate, null, out memory), "vkAllocateMemory (S5-470 readback)");
            VulkanInterop.Check(vk.BindBufferMemory(device, readback, memory, 0), "vkBindBufferMemory (S5-470 readback)");

            var poolCreate = new CommandPoolCreateInfo
            {
                SType = StructureType.CommandPoolCreateInfo,
                QueueFamilyIndex = queueFamily,
                Flags = CommandPoolCreateFlags.TransientBit,
            };
            VulkanInterop.Check(vk.CreateCommandPool(device, &poolCreate, null, out pool), "vkCreateCommandPool (S5-470 readback)");
            var commandAllocate = new CommandBufferAllocateInfo
            {
                SType = StructureType.CommandBufferAllocateInfo,
                CommandPool = pool,
                Level = CommandBufferLevel.Primary,
                CommandBufferCount = 1,
            };
            VulkanInterop.Check(vk.AllocateCommandBuffers(device, &commandAllocate, out CommandBuffer commands), "vkAllocateCommandBuffers (S5-470 readback)");
            var begin = new CommandBufferBeginInfo
            {
                SType = StructureType.CommandBufferBeginInfo,
                Flags = CommandBufferUsageFlags.OneTimeSubmitBit,
            };
            VulkanInterop.Check(vk.BeginCommandBuffer(commands, &begin), "vkBeginCommandBuffer (S5-470 readback)");

            var barrier = new ImageMemoryBarrier2
            {
                SType = StructureType.ImageMemoryBarrier2,
                SrcStageMask = PipelineStageFlags2.FragmentShaderBit,
                SrcAccessMask = AccessFlags2.ShaderReadBit,
                DstStageMask = PipelineStageFlags2.CopyBit,
                DstAccessMask = AccessFlags2.TransferReadBit,
                OldLayout = ImageLayout.ShaderReadOnlyOptimal,
                NewLayout = ImageLayout.TransferSrcOptimal,
                SrcQueueFamilyIndex = Silk.NET.Vulkan.Vk.QueueFamilyIgnored,
                DstQueueFamilyIndex = Silk.NET.Vulkan.Vk.QueueFamilyIgnored,
                Image = image,
                SubresourceRange = new ImageSubresourceRange(
                    ImageAspectFlags.ColorBit, 0, 1, 0, 1),
            };
            var dependency = new DependencyInfo
            {
                SType = StructureType.DependencyInfo,
                ImageMemoryBarrierCount = 1,
                PImageMemoryBarriers = &barrier,
            };
            vk.CmdPipelineBarrier2(commands, &dependency);
            var copy = new BufferImageCopy
            {
                ImageSubresource = new ImageSubresourceLayers(ImageAspectFlags.ColorBit, 0, 0, 1),
                ImageExtent = new Extent3D((uint)width, (uint)height, 1),
            };
            vk.CmdCopyImageToBuffer(commands, image, ImageLayout.TransferSrcOptimal, readback, 1, &copy);
            BufferMemoryBarrier2 hostReadBarrier = CreateHostReadBarrier(readback, byteCount);
            var hostDependency = new DependencyInfo
            {
                SType = StructureType.DependencyInfo,
                BufferMemoryBarrierCount = 1,
                PBufferMemoryBarriers = &hostReadBarrier,
            };
            vk.CmdPipelineBarrier2(commands, &hostDependency);
            VulkanInterop.Check(vk.EndCommandBuffer(commands), "vkEndCommandBuffer (S5-470 readback)");

            var commandInfo = new CommandBufferSubmitInfo
            {
                SType = StructureType.CommandBufferSubmitInfo,
                CommandBuffer = commands,
            };
            var submit = new SubmitInfo2
            {
                SType = StructureType.SubmitInfo2,
                CommandBufferInfoCount = 1,
                PCommandBufferInfos = &commandInfo,
            };
            VulkanInterop.Check(vk.QueueSubmit2(queue, 1, &submit, default), "vkQueueSubmit2 (S5-470 readback)");
            VulkanInterop.Check(vk.QueueWaitIdle(queue), "vkQueueWaitIdle (S5-470 readback)");

            void* mapped = null;
            VulkanInterop.Check(vk.MapMemory(device, memory, 0, byteCount, 0, &mapped), "vkMapMemory (S5-470 readback)");
            try
            {
                var pixels = new byte[byteCount];
                new ReadOnlySpan<byte>(mapped, pixels.Length).CopyTo(pixels);
                return pixels;
            }
            finally
            {
                vk.UnmapMemory(device, memory);
            }
        }
        finally
        {
            if (pool.Handle != 0)
                vk.DestroyCommandPool(device, pool, null);
            if (readback.Handle != 0)
                vk.DestroyBuffer(device, readback, null);
            if (memory.Handle != 0)
                vk.FreeMemory(device, memory, null);
        }
    }

    internal static BufferMemoryBarrier2 CreateHostReadBarrier(Buffer readback, ulong byteCount)
    {
        ArgumentOutOfRangeException.ThrowIfZero(byteCount);
        return new BufferMemoryBarrier2
        {
            SType = StructureType.BufferMemoryBarrier2,
            SrcStageMask = PipelineStageFlags2.CopyBit,
            SrcAccessMask = AccessFlags2.TransferWriteBit,
            DstStageMask = PipelineStageFlags2.HostBit,
            DstAccessMask = AccessFlags2.HostReadBit,
            SrcQueueFamilyIndex = Silk.NET.Vulkan.Vk.QueueFamilyIgnored,
            DstQueueFamilyIndex = Silk.NET.Vulkan.Vk.QueueFamilyIgnored,
            Buffer = readback,
            Offset = 0,
            Size = byteCount,
        };
    }
}
