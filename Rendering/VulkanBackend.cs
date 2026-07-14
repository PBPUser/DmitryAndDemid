using System.Numerics;
using System.Runtime.InteropServices;
using System.Text.Json;
using Silk.NET.Core;
using Silk.NET.Core.Native;
using Silk.NET.Input;
using Silk.NET.Maths;
using Silk.NET.Vulkan;
using Silk.NET.Vulkan.Extensions.KHR;
using Silk.NET.Windowing;
using StbImageSharp;
using StbTrueTypeSharp;
using Buffer = Silk.NET.Vulkan.Buffer;
using Image = Silk.NET.Vulkan.Image;
using Semaphore = Silk.NET.Vulkan.Semaphore;

namespace DmitryAndDemid.Rendering;

/// <summary>
/// Third renderer: Silk.NET + Vulkan.
///
/// SHADERS: Vulkan cannot eat the game's GLSL (47 of 48 shaders use gl_FragColor, all uniforms are
/// free-standing). Tools/compile_shaders.py transforms and compiles them to SPIR-V ahead of time, into
/// Assets/Shaders/vulkan/, together with a reflection sidecar giving each uniform's byte offset inside the
/// generated gl_DefaultUniformBlock. That sidecar is what makes GetUniformLocation/SetShaderValue work:
/// a "location" here is an index into the reflected uniform list, and setting one writes into a CPU shadow
/// copy of the block which is uploaded per draw.
///
/// DESIGN: deliberately the simplest thing that is correct, not the fastest.
///   - every colour image (swapchain and render targets) stays in GENERAL layout, so there is no
///     layout-transition bookkeeping; the cost is irrelevant at this game's draw counts.
///   - one render pass per attachment format, loadOp = LOAD, so a pass can be left and re-entered when the
///     game switches render targets mid-frame (which it does constantly) without losing what was drawn.
///   - one draw call per quad, with vertices appended to a host-visible ring buffer.
/// </summary>
public sealed unsafe class VulkanBackend : IBackend
{
    public string Name => "Vulkan";

    private const int MaxDrawsPerFrame = 8192;
    private const uint VertexFloats = 12;              // pos3 + uv2 + normal3 + colour4
    private const uint VerticesPerQuad = 6;

    private Vk Vk = null!;
    private IWindow Window = null!;
    private IInputContext InputContext = null!;
    private IKeyboard? Keyboard;
    private IMouse? Mouse;

    private Instance Instance;
    private KhrSurface KhrSurface = null!;
    private SurfaceKHR Surface;
    private PhysicalDevice PhysicalDevice;
    private Device Device;
    private Queue GraphicsQueue;
    private uint QueueFamily;

    private KhrSwapchain KhrSwapchain = null!;
    private SwapchainKHR Swapchain;
    private Image[] SwapchainImages = [];
    private ImageView[] SwapchainViews = [];
    private Framebuffer[] SwapchainFramebuffers = [];
    private Format SwapchainFormat;
    private Extent2D SwapchainExtent;
    private uint CurrentImage;

    private RenderPass SwapchainPass;                  // format = swapchain's
    private RenderPass OffscreenPass;                  // format = R8G8B8A8_UNORM
    private const Format OffscreenFormat = Format.R8G8B8A8Unorm;

    private CommandPool CommandPool;
    private CommandBuffer FrameCmd;
    private CommandBuffer ScratchCmd;
    private bool InFrame;
    private bool ScratchRecording;

    /// <summary>
    /// The game draws OUTSIDE a frame: at load time it renders text and UI into targets (Helper.DrawText*,
    /// RenderSelectionBackground, ...) long before the first BeginDrawing. Raylib and GL allow that — their
    /// calls are immediate. Vulkan needs an open command buffer, so out-of-frame work is recorded into a
    /// scratch buffer that is submitted and waited on when the target stack drains.
    /// </summary>
    private CommandBuffer Cmd => InFrame ? FrameCmd : ScratchCmd;
    private Semaphore ImageAvailable;
    /// <summary>
    /// One render-finished semaphore PER SWAPCHAIN IMAGE. A single shared one gets signalled again while the
    /// previous present is still waiting on it (VUID-vkQueueSubmit-pSignalSemaphores-00067) — the in-flight
    /// fence does not cover the present's wait.
    /// </summary>
    private Semaphore[] RenderFinished = [];
    private Fence InFlight;
    private DescriptorPool DescriptorPool;

    private RingBuffer Vertices;
    private RingBuffer Uniforms;
    private Sampler LinearSampler, NearestSampler;

    private readonly Dictionary<int, VkTexture> Textures = new();
    private readonly Dictionary<int, VkTarget> Targets = new();
    private readonly Dictionary<int, VkShader> Shaders = new();
    private readonly Dictionary<int, VkFont> Fonts = new();
    private readonly Dictionary<int, TextureHandle> TargetTextures = new();
    private readonly Dictionary<(int Shader, int Pass, BlendMode Blend), Pipeline> Pipelines = new();

    private readonly Stack<TargetHandle> TargetStack = new();
    private ShaderHandle ActiveShader;
    private ShaderHandle DefaultShader;
    private TextureHandle WhitePixel;
    private FontHandle DefaultFontHandle;
    private int NextId = 1;

    private int FrameWidth, FrameHeight;
    private bool InPass;
    private double StartTime;
    private int FrameCounter, FpsValue;
    private double FpsAccumulator;
    private bool Ready;                                 // false if Vulkan init failed

    private sealed class VkTexture
    {
        public Image Image;
        public DeviceMemory Memory;
        public ImageView View;
        public int Width, Height;
        public bool Linear;
        public bool OwnedByTarget;
    }

    private sealed class VkTarget
    {
        public TextureHandle Colour;
        public Framebuffer Framebuffer;
        public int Width, Height;
    }

    private sealed class Reflection
    {
        public int blockSize { get; set; }
        public int blockBinding { get; set; }
        public List<ReflectedUniform> uniforms { get; set; } = [];
        public List<ReflectedSampler> samplers { get; set; } = [];
    }

    private sealed class ReflectedUniform
    {
        public string name { get; set; } = "";
        public int offset { get; set; }
        public string type { get; set; } = "";
    }

    private sealed class ReflectedSampler
    {
        public string name { get; set; } = "";
        public int binding { get; set; }
    }

    private sealed class VkShader
    {
        public ShaderModule Vertex, Fragment;
        public DescriptorSetLayout SetLayout;
        public PipelineLayout Layout;
        public Reflection VertexReflection = new();
        public Reflection FragmentReflection = new();
        public byte[] VertexBlock = [];
        public byte[] FragmentBlock = [];
        /// <summary>Extra samplers the game binds by hand (SetShaderValueTexture), by binding number.</summary>
        public readonly Dictionary<int, TextureHandle> BoundTextures = new();
        public string[] UniformNames = [];
        public int ColDiffuseOffset = -1;
    }

    private sealed class VkFont
    {
        public TextureHandle Atlas;
        public float BaseSize;
        public readonly Dictionary<char, Glyph> Glyphs = new();
    }

    private struct Glyph
    {
        public float U0, V0, U1, V1;
        public float OffsetX, OffsetY, AdvanceX, Width, Height;
    }

    /// <summary>A host-visible buffer used as a per-frame bump allocator.</summary>
    private struct RingBuffer
    {
        public Buffer Buffer;
        public DeviceMemory Memory;
        public byte* Mapped;
        public ulong Size;
        public ulong Offset;
    }

    // ======================================================================================
    //  window + device
    // ======================================================================================

    public void OpenWindow(int width, int height, string title)
    {
        WindowOptions options = WindowOptions.DefaultVulkan with
        {
            Size = new Vector2D<int>(width, height),
            Title = title,
        };

        Window = Silk.NET.Windowing.Window.Create(options);
        Window.Initialize();

        InputContext = Window.CreateInput();
        Keyboard = InputContext.Keyboards.FirstOrDefault();
        Mouse = InputContext.Mice.FirstOrDefault();

        if (Window.VkSurface == null)
        {
            Console.WriteLine("Vulkan: windowing reports no Vulkan surface support.");
            return;
        }

        Vk = Vk.GetApi();

        CreateInstance(title);
        CreateSurface();
        PickPhysicalDevice();
        CreateLogicalDevice();
        CreateSwapchain(width, height);
        CreateRenderPasses();
        CreateFramebuffers();
        CreateCommandsAndSync();
        CreateDescriptorPool();
        CreateSamplers();

        Vertices = CreateRing(MaxDrawsPerFrame * VerticesPerQuad * VertexFloats * sizeof(float),
            BufferUsageFlags.VertexBufferBit);
        Uniforms = CreateRing(MaxDrawsPerFrame * 512, BufferUsageFlags.UniformBufferBit);

        DefaultShader = LoadSpirv("base", "_default");
        WhitePixel = CreateTexture([255, 255, 255, 255], 1, 1);

        StartTime = Environment.TickCount64 / 1000.0;
        FrameWidth = width;
        FrameHeight = height;
        Ready = true;
    }

    private void CreateInstance(string title)
    {
        byte** windowExtensions = Window.VkSurface!.GetRequiredExtensions(out uint extensionCount);

        ApplicationInfo appInfo = new()
        {
            SType = StructureType.ApplicationInfo,
            PApplicationName = (byte*)SilkMarshal.StringToPtr(title),
            ApiVersion = Vk.Version12,
        };

        InstanceCreateInfo info = new()
        {
            SType = StructureType.InstanceCreateInfo,
            PApplicationInfo = &appInfo,
            EnabledExtensionCount = extensionCount,
            PpEnabledExtensionNames = windowExtensions,
        };

        if (Vk.CreateInstance(in info, null, out Instance) != Result.Success)
            throw new Exception("Vulkan: failed to create instance");

        SilkMarshal.Free((nint)appInfo.PApplicationName);

        if (!Vk.TryGetInstanceExtension(Instance, out KhrSurface))
            throw new Exception("Vulkan: VK_KHR_surface missing");
    }

    private void CreateSurface() =>
        Surface = Window.VkSurface!.Create<AllocationCallbacks>(Instance.ToHandle(), null).ToSurface();

    private void PickPhysicalDevice()
    {
        uint count = 0;
        Vk.EnumeratePhysicalDevices(Instance, ref count, null);
        PhysicalDevice[] devices = new PhysicalDevice[count];
        fixed (PhysicalDevice* p = devices)
            Vk.EnumeratePhysicalDevices(Instance, ref count, p);

        foreach (PhysicalDevice device in devices)
        {
            uint families = 0;
            Vk.GetPhysicalDeviceQueueFamilyProperties(device, ref families, null);
            QueueFamilyProperties[] properties = new QueueFamilyProperties[families];
            fixed (QueueFamilyProperties* p = properties)
                Vk.GetPhysicalDeviceQueueFamilyProperties(device, ref families, p);

            for (uint i = 0; i < families; i++)
            {
                KhrSurface.GetPhysicalDeviceSurfaceSupport(device, i, Surface, out Bool32 present);
                if (properties[i].QueueFlags.HasFlag(QueueFlags.GraphicsBit) && present)
                {
                    PhysicalDevice = device;
                    QueueFamily = i;
                    return;
                }
            }
        }
        throw new Exception("Vulkan: no device with a graphics+present queue");
    }

    private void CreateLogicalDevice()
    {
        float priority = 1f;
        DeviceQueueCreateInfo queueInfo = new()
        {
            SType = StructureType.DeviceQueueCreateInfo,
            QueueFamilyIndex = QueueFamily,
            QueueCount = 1,
            PQueuePriorities = &priority,
        };

        byte** extensions = (byte**)SilkMarshal.StringArrayToPtr([KhrSwapchain.ExtensionName]);
        PhysicalDeviceFeatures features = new();

        DeviceCreateInfo info = new()
        {
            SType = StructureType.DeviceCreateInfo,
            QueueCreateInfoCount = 1,
            PQueueCreateInfos = &queueInfo,
            EnabledExtensionCount = 1,
            PpEnabledExtensionNames = extensions,
            PEnabledFeatures = &features,
        };

        if (Vk.CreateDevice(PhysicalDevice, in info, null, out Device) != Result.Success)
            throw new Exception("Vulkan: failed to create device");

        SilkMarshal.Free((nint)extensions);
        Vk.GetDeviceQueue(Device, QueueFamily, 0, out GraphicsQueue);

        if (!Vk.TryGetDeviceExtension(Instance, Device, out KhrSwapchain))
            throw new Exception("Vulkan: VK_KHR_swapchain missing");
    }

    private void CreateSwapchain(int width, int height)
    {
        KhrSurface.GetPhysicalDeviceSurfaceCapabilities(PhysicalDevice, Surface, out SurfaceCapabilitiesKHR caps);

        uint formatCount = 0;
        KhrSurface.GetPhysicalDeviceSurfaceFormats(PhysicalDevice, Surface, ref formatCount, null);
        SurfaceFormatKHR[] formats = new SurfaceFormatKHR[formatCount];
        fixed (SurfaceFormatKHR* p = formats)
            KhrSurface.GetPhysicalDeviceSurfaceFormats(PhysicalDevice, Surface, ref formatCount, p);

        SurfaceFormatKHR chosen = formats.FirstOrDefault(f => f.Format == Format.B8G8R8A8Unorm, formats[0]);
        SwapchainFormat = chosen.Format;
        SwapchainExtent = caps.CurrentExtent.Width != uint.MaxValue
            ? caps.CurrentExtent
            : new Extent2D((uint)width, (uint)height);

        uint imageCount = Math.Max(caps.MinImageCount + 1, 2);
        if (caps.MaxImageCount > 0 && imageCount > caps.MaxImageCount)
            imageCount = caps.MaxImageCount;

        SwapchainCreateInfoKHR info = new()
        {
            SType = StructureType.SwapchainCreateInfoKhr,
            Surface = Surface,
            MinImageCount = imageCount,
            ImageFormat = SwapchainFormat,
            ImageColorSpace = chosen.ColorSpace,
            ImageExtent = SwapchainExtent,
            ImageArrayLayers = 1,
            ImageUsage = ImageUsageFlags.ColorAttachmentBit | ImageUsageFlags.TransferSrcBit,
            ImageSharingMode = SharingMode.Exclusive,
            PreTransform = caps.CurrentTransform,
            CompositeAlpha = CompositeAlphaFlagsKHR.OpaqueBitKhr,
            PresentMode = PresentModeKHR.FifoKhr,
            Clipped = true,
        };

        if (KhrSwapchain.CreateSwapchain(Device, in info, null, out Swapchain) != Result.Success)
            throw new Exception("Vulkan: failed to create swapchain");

        uint count = 0;
        KhrSwapchain.GetSwapchainImages(Device, Swapchain, ref count, null);
        SwapchainImages = new Image[count];
        fixed (Image* p = SwapchainImages)
            KhrSwapchain.GetSwapchainImages(Device, Swapchain, ref count, p);

        SwapchainViews = SwapchainImages.Select(i => CreateView(i, SwapchainFormat)).ToArray();
    }

    private ImageView CreateView(Image image, Format format)
    {
        ImageViewCreateInfo info = new()
        {
            SType = StructureType.ImageViewCreateInfo,
            Image = image,
            ViewType = ImageViewType.Type2D,
            Format = format,
            SubresourceRange = new ImageSubresourceRange(ImageAspectFlags.ColorBit, 0, 1, 0, 1),
        };
        Vk.CreateImageView(Device, in info, null, out ImageView view);
        return view;
    }

    /// <summary>
    /// loadOp = LOAD and GENERAL layouts throughout: the game leaves and re-enters the window pass every
    /// time it renders into a target, so a pass must never discard what is already there.
    /// </summary>
    private RenderPass CreatePass(Format format)
    {
        AttachmentDescription colour = new()
        {
            Format = format,
            Samples = SampleCountFlags.Count1Bit,
            LoadOp = AttachmentLoadOp.Load,
            StoreOp = AttachmentStoreOp.Store,
            StencilLoadOp = AttachmentLoadOp.DontCare,
            StencilStoreOp = AttachmentStoreOp.DontCare,
            InitialLayout = ImageLayout.General,
            FinalLayout = ImageLayout.General,
        };

        AttachmentReference reference = new(0, ImageLayout.General);
        SubpassDescription subpass = new()
        {
            PipelineBindPoint = PipelineBindPoint.Graphics,
            ColorAttachmentCount = 1,
            PColorAttachments = &reference,
        };

        RenderPassCreateInfo info = new()
        {
            SType = StructureType.RenderPassCreateInfo,
            AttachmentCount = 1,
            PAttachments = &colour,
            SubpassCount = 1,
            PSubpasses = &subpass,
        };

        Vk.CreateRenderPass(Device, in info, null, out RenderPass pass);
        return pass;
    }

    private void CreateRenderPasses()
    {
        SwapchainPass = CreatePass(SwapchainFormat);
        OffscreenPass = CreatePass(OffscreenFormat);
    }

    private void CreateFramebuffers()
    {
        SwapchainFramebuffers = new Framebuffer[SwapchainViews.Length];
        for (int i = 0; i < SwapchainViews.Length; i++)
            SwapchainFramebuffers[i] = CreateFramebuffer(SwapchainPass, SwapchainViews[i],
                (int)SwapchainExtent.Width, (int)SwapchainExtent.Height);
    }

    private Framebuffer CreateFramebuffer(RenderPass pass, ImageView view, int width, int height)
    {
        FramebufferCreateInfo info = new()
        {
            SType = StructureType.FramebufferCreateInfo,
            RenderPass = pass,
            AttachmentCount = 1,
            PAttachments = &view,
            Width = (uint)width,
            Height = (uint)height,
            Layers = 1,
        };
        Vk.CreateFramebuffer(Device, in info, null, out Framebuffer framebuffer);
        return framebuffer;
    }

    private void CreateCommandsAndSync()
    {
        CommandPoolCreateInfo poolInfo = new()
        {
            SType = StructureType.CommandPoolCreateInfo,
            QueueFamilyIndex = QueueFamily,
            Flags = CommandPoolCreateFlags.ResetCommandBufferBit,
        };
        Vk.CreateCommandPool(Device, in poolInfo, null, out CommandPool);

        CommandBufferAllocateInfo allocInfo = new()
        {
            SType = StructureType.CommandBufferAllocateInfo,
            CommandPool = CommandPool,
            Level = CommandBufferLevel.Primary,
            CommandBufferCount = 1,
        };
        Vk.AllocateCommandBuffers(Device, in allocInfo, out FrameCmd);
        Vk.AllocateCommandBuffers(Device, in allocInfo, out ScratchCmd);

        SemaphoreCreateInfo semaphoreInfo = new() { SType = StructureType.SemaphoreCreateInfo };
        Vk.CreateSemaphore(Device, in semaphoreInfo, null, out ImageAvailable);

        RenderFinished = new Semaphore[SwapchainImages.Length];
        for (int i = 0; i < RenderFinished.Length; i++)
            Vk.CreateSemaphore(Device, in semaphoreInfo, null, out RenderFinished[i]);

        FenceCreateInfo fenceInfo = new()
        {
            SType = StructureType.FenceCreateInfo,
            Flags = FenceCreateFlags.SignaledBit,
        };
        Vk.CreateFence(Device, in fenceInfo, null, out InFlight);
    }

    private void CreateDescriptorPool()
    {
        DescriptorPoolSize[] sizes =
        [
            new(DescriptorType.CombinedImageSampler, MaxDrawsPerFrame * 2),
            new(DescriptorType.UniformBuffer, MaxDrawsPerFrame * 2),
        ];

        fixed (DescriptorPoolSize* p = sizes)
        {
            DescriptorPoolCreateInfo info = new()
            {
                SType = StructureType.DescriptorPoolCreateInfo,
                PoolSizeCount = (uint)sizes.Length,
                PPoolSizes = p,
                MaxSets = MaxDrawsPerFrame,
                Flags = DescriptorPoolCreateFlags.None,
            };
            Vk.CreateDescriptorPool(Device, in info, null, out DescriptorPool);
        }
    }

    private void CreateSamplers()
    {
        LinearSampler = CreateSampler(Filter.Linear);
        NearestSampler = CreateSampler(Filter.Nearest);
    }

    private Sampler CreateSampler(Filter filter)
    {
        SamplerCreateInfo info = new()
        {
            SType = StructureType.SamplerCreateInfo,
            MagFilter = filter,
            MinFilter = filter,
            // REPEAT, matching Raylib/GL — the game's flip trick relies on wrapping (see SilkGLBackend).
            AddressModeU = SamplerAddressMode.Repeat,
            AddressModeV = SamplerAddressMode.Repeat,
            AddressModeW = SamplerAddressMode.Repeat,
            MipmapMode = SamplerMipmapMode.Nearest,
        };
        Vk.CreateSampler(Device, in info, null, out Sampler sampler);
        return sampler;
    }

    private RingBuffer CreateRing(ulong size, BufferUsageFlags usage)
    {
        BufferCreateInfo info = new()
        {
            SType = StructureType.BufferCreateInfo,
            Size = size,
            Usage = usage,
            SharingMode = SharingMode.Exclusive,
        };
        Vk.CreateBuffer(Device, in info, null, out Buffer buffer);

        Vk.GetBufferMemoryRequirements(Device, buffer, out MemoryRequirements requirements);
        MemoryAllocateInfo allocInfo = new()
        {
            SType = StructureType.MemoryAllocateInfo,
            AllocationSize = requirements.Size,
            MemoryTypeIndex = FindMemoryType(requirements.MemoryTypeBits,
                MemoryPropertyFlags.HostVisibleBit | MemoryPropertyFlags.HostCoherentBit),
        };
        Vk.AllocateMemory(Device, in allocInfo, null, out DeviceMemory memory);
        Vk.BindBufferMemory(Device, buffer, memory, 0);

        void* mapped;
        Vk.MapMemory(Device, memory, 0, size, 0, &mapped);
        return new RingBuffer { Buffer = buffer, Memory = memory, Mapped = (byte*)mapped, Size = size };
    }

    private uint FindMemoryType(uint filter, MemoryPropertyFlags flags)
    {
        Vk.GetPhysicalDeviceMemoryProperties(PhysicalDevice, out PhysicalDeviceMemoryProperties properties);
        for (uint i = 0; i < properties.MemoryTypeCount; i++)
            if ((filter & (1u << (int)i)) != 0 &&
                properties.MemoryTypes[(int)i].PropertyFlags.HasFlag(flags))
                return i;
        throw new Exception("Vulkan: no suitable memory type");
    }

    // ======================================================================================
    //  frame
    // ======================================================================================

    private int FrameNo;


    private void EnsureScratch()
    {
        if (ScratchRecording)
            return;
        Vk.ResetCommandBuffer(ScratchCmd, 0);
        CommandBufferBeginInfo begin = new()
        {
            SType = StructureType.CommandBufferBeginInfo,
            Flags = CommandBufferUsageFlags.OneTimeSubmitBit,
        };
        Vk.BeginCommandBuffer(ScratchCmd, in begin);
        ScratchRecording = true;
    }

    /// <summary>Submits any out-of-frame work and waits for it, so its results can be sampled immediately.</summary>
    private void FlushScratch()
    {
        if (!ScratchRecording)
            return;
        if (InPass)
        {
            Vk.CmdEndRenderPass(ScratchCmd);
            InPass = false;
        }
        Vk.EndCommandBuffer(ScratchCmd);

        CommandBuffer cmd = ScratchCmd;
        SubmitInfo submit = new()
        {
            SType = StructureType.SubmitInfo,
            CommandBufferCount = 1,
            PCommandBuffers = &cmd,
        };
        Vk.QueueSubmit(GraphicsQueue, 1, in submit, default);
        Vk.QueueWaitIdle(GraphicsQueue);

        ScratchRecording = false;
        Vertices.Offset = 0;
        Uniforms.Offset = 0;
        Vk.ResetDescriptorPool(Device, DescriptorPool, 0);
        CollectGarbage();   // the wait above proves the GPU is done with this batch
    }

    public void BeginFrame()
    {
        if (!Ready)
            return;
        FrameNo++;

        FlushScratch();
        Window.DoEvents();

        Vk.WaitForFences(Device, 1, in InFlight, true, ulong.MaxValue);
        Vk.ResetFences(Device, 1, in InFlight);
        CollectGarbage();   // the fence proves last frame's resources are free

        KhrSwapchain.AcquireNextImage(Device, Swapchain, ulong.MaxValue, ImageAvailable, default, ref CurrentImage);

        InFrame = true;
        Vk.ResetCommandBuffer(FrameCmd, 0);
        CommandBufferBeginInfo begin = new() { SType = StructureType.CommandBufferBeginInfo };
        Vk.BeginCommandBuffer(FrameCmd, in begin);

        // The swapchain image comes back UNDEFINED; the passes expect GENERAL.
        Barrier(SwapchainImages[CurrentImage], ImageLayout.Undefined, ImageLayout.General);

        Vk.ResetDescriptorPool(Device, DescriptorPool, 0);
        Vertices.Offset = 0;
        Uniforms.Offset = 0;

        FrameWidth = (int)SwapchainExtent.Width;
        FrameHeight = (int)SwapchainExtent.Height;
        BeginPass(SwapchainPass, SwapchainFramebuffers[CurrentImage], FrameWidth, FrameHeight);
    }

    public void EndFrame()
    {
        if (!Ready)
            return;

        EndPass();
        Barrier(SwapchainImages[CurrentImage], ImageLayout.General, ImageLayout.PresentSrcKhr);
        Vk.EndCommandBuffer(Cmd);

        Semaphore waitSemaphore = ImageAvailable, signalSemaphore = RenderFinished[CurrentImage];
        PipelineStageFlags waitStage = PipelineStageFlags.ColorAttachmentOutputBit;
        CommandBuffer cmd = Cmd;

        SubmitInfo submit = new()
        {
            SType = StructureType.SubmitInfo,
            WaitSemaphoreCount = 1,
            PWaitSemaphores = &waitSemaphore,
            PWaitDstStageMask = &waitStage,
            CommandBufferCount = 1,
            PCommandBuffers = &cmd,
            SignalSemaphoreCount = 1,
            PSignalSemaphores = &signalSemaphore,
        };
        Vk.QueueSubmit(GraphicsQueue, 1, in submit, InFlight);

        SwapchainKHR swapchain = Swapchain;
        uint imageIndex = CurrentImage;
        PresentInfoKHR present = new()
        {
            SType = StructureType.PresentInfoKhr,
            WaitSemaphoreCount = 1,
            PWaitSemaphores = &signalSemaphore,
            SwapchainCount = 1,
            PSwapchains = &swapchain,
            PImageIndices = &imageIndex,
        };
        KhrSwapchain.QueuePresent(GraphicsQueue, in present);
        InFrame = false;

        FrameCounter++;
        double now = Time;
        if (now - FpsAccumulator >= 1.0)
        {
            FpsValue = FrameCounter;
            FrameCounter = 0;
            FpsAccumulator = now;
        }
    }

    private void BeginPass(RenderPass pass, Framebuffer framebuffer, int width, int height)
    {
        RenderPassBeginInfo info = new()
        {
            SType = StructureType.RenderPassBeginInfo,
            RenderPass = pass,
            Framebuffer = framebuffer,
            RenderArea = new Rect2D(new Offset2D(0, 0), new Extent2D((uint)width, (uint)height)),
            ClearValueCount = 0,
        };
        Vk.CmdBeginRenderPass(Cmd, in info, SubpassContents.Inline);

        Viewport viewport = new(0, 0, width, height, 0, 1);
        Rect2D scissor = new(new Offset2D(0, 0), new Extent2D((uint)width, (uint)height));
        Vk.CmdSetViewport(Cmd, 0, 1, in viewport);
        Vk.CmdSetScissor(Cmd, 0, 1, in scissor);

        FrameWidth = width;
        FrameHeight = height;
        ActivePass = pass;
        ActiveFramebuffer = framebuffer;
        ActiveWidth = width;
        ActiveHeight = height;
        CurrentPass = pass.Handle == SwapchainPass.Handle ? 0 : 1;
        InPass = true;
    }

    private int CurrentPass;
    private RenderPass ActivePass;
    private Framebuffer ActiveFramebuffer;
    private int ActiveWidth, ActiveHeight;

    private readonly List<(Image Image, DeviceMemory Memory, ImageView View)> DeferredImages = [];
    private readonly List<Framebuffer> DeferredFramebuffers = [];
    private readonly List<(Buffer Buffer, DeviceMemory Memory)> DeferredBuffers = [];

    /// <summary>
    /// Barriers and buffer->image copies are illegal inside a render pass. Leaving and re-entering the pass
    /// costs nothing; the alternative (a throwaway command buffer with QueueWaitIdle, which is what this
    /// used to do) stalls the GPU and dragged the whole game down to ~2 fps, because the menu recreates its
    /// text render targets every single frame.
    /// </summary>
    private void OutsidePass(Action<CommandBuffer> record)
    {
        if (!InFrame)
            EnsureScratch();

        bool wasInPass = InPass;
        RenderPass pass = ActivePass;
        Framebuffer framebuffer = ActiveFramebuffer;
        int width = ActiveWidth, height = ActiveHeight;

        if (wasInPass)
        {
            Vk.CmdEndRenderPass(Cmd);
            InPass = false;
        }

        record(Cmd);

        if (wasInPass)
            BeginPass(pass, framebuffer, width, height);
    }

    private void EndPass()
    {
        if (!InPass)
            return;
        Vk.CmdEndRenderPass(Cmd);
        InPass = false;

        // Anything just rendered into a target may be sampled by the next draw.
        MemoryBarrier barrier = new()
        {
            SType = StructureType.MemoryBarrier,
            SrcAccessMask = AccessFlags.ColorAttachmentWriteBit,
            DstAccessMask = AccessFlags.ShaderReadBit,
        };
        Vk.CmdPipelineBarrier(Cmd, PipelineStageFlags.ColorAttachmentOutputBit,
            PipelineStageFlags.FragmentShaderBit, 0, 1, in barrier, 0, null, 0, null);
    }

    private void Barrier(Image image, ImageLayout from, ImageLayout to)
    {
        ImageMemoryBarrier barrier = new()
        {
            SType = StructureType.ImageMemoryBarrier,
            OldLayout = from,
            NewLayout = to,
            SrcQueueFamilyIndex = Vk.QueueFamilyIgnored,
            DstQueueFamilyIndex = Vk.QueueFamilyIgnored,
            Image = image,
            SubresourceRange = new ImageSubresourceRange(ImageAspectFlags.ColorBit, 0, 1, 0, 1),
            SrcAccessMask = AccessFlags.MemoryWriteBit,
            DstAccessMask = AccessFlags.MemoryReadBit | AccessFlags.MemoryWriteBit,
        };
        Vk.CmdPipelineBarrier(Cmd, PipelineStageFlags.AllCommandsBit, PipelineStageFlags.AllCommandsBit,
            0, 0, null, 0, null, 1, in barrier);
    }

    // ======================================================================================
    //  render targets
    // ======================================================================================

    public TargetHandle CreateTarget(int width, int height)
    {
        if (!Ready)
            return TargetHandle.None;

        width = Math.Max(1, width);
        height = Math.Max(1, height);

        TextureHandle colour = CreateImage(width, height, OffscreenFormat,
            ImageUsageFlags.ColorAttachmentBit | ImageUsageFlags.SampledBit | ImageUsageFlags.TransferDstBit);
        Textures[colour.Id].OwnedByTarget = true;

        Framebuffer framebuffer = CreateFramebuffer(OffscreenPass, Textures[colour.Id].View, width, height);

        int id = NextId++;
        Targets[id] = new VkTarget
        {
            Colour = colour,
            Framebuffer = framebuffer,
            Width = width,
            Height = height,
        };
        TargetTextures[id] = colour;
        return new TargetHandle(id);
    }

    public void DestroyTarget(TargetHandle target)
    {
        if (!Targets.Remove(target.Id, out VkTarget? t))
            return;
        TargetTextures.Remove(target.Id);
        // Deferred: the GPU may still be reading this. It is freed once the next fence wait proves it isn't.
        DeferredFramebuffers.Add(t.Framebuffer);
        DestroyTextureInternal(t.Colour);
    }

    public bool IsValid(TargetHandle target) => Targets.ContainsKey(target.Id);

    public TextureHandle GetTargetTexture(TargetHandle target) =>
        TargetTextures.GetValueOrDefault(target.Id, TextureHandle.None);

    public int TargetFloor { get; set; }

    public void BeginTarget(TargetHandle target)
    {
        if (!Ready)
            return;

        // Push even an unknown handle so the matching EndTarget cannot pop the parent.
        TargetStack.Push(target);
        if (!Targets.TryGetValue(target.Id, out VkTarget? t))
            return;

        if (!InFrame)
            EnsureScratch();   // load-time rendering into targets happens outside any frame
        EndPass();
        BeginPass(OffscreenPass, t.Framebuffer, t.Width, t.Height);
    }

    public void EndTarget()
    {
        if (!Ready)
            return;

        if (TargetStack.Count <= TargetFloor)
        {
#if DEBUG
            Console.WriteLine("Renderer: ignoring unbalanced EndTarget().");
#endif
            return;
        }

        TargetStack.Pop();
        EndPass();

        if (TargetStack.TryPeek(out TargetHandle parent) && Targets.TryGetValue(parent.Id, out VkTarget? p))
        {
            BeginPass(OffscreenPass, p.Framebuffer, p.Width, p.Height);
        }
        else if (InFrame)
        {
            BeginPass(SwapchainPass, SwapchainFramebuffers[CurrentImage],
                (int)SwapchainExtent.Width, (int)SwapchainExtent.Height);
        }
        else
        {
            // Out-of-frame: there is no swapchain pass to return to. Submit what was recorded so the target
            // can be sampled right away (the game does exactly that when building its text textures).
            FlushScratch();
        }
    }

    public void ResetTargets()
    {
        if (!Ready || TargetStack.Count == 0)
            return;
        TargetStack.Clear();
        EndPass();
        BeginPass(SwapchainPass, SwapchainFramebuffers[CurrentImage],
            (int)SwapchainExtent.Width, (int)SwapchainExtent.Height);
    }

    // ======================================================================================
    //  textures
    // ======================================================================================

    private TextureHandle CreateImage(int width, int height, Format format, ImageUsageFlags usage)
    {
        ImageCreateInfo info = new()
        {
            SType = StructureType.ImageCreateInfo,
            ImageType = ImageType.Type2D,
            Extent = new Extent3D((uint)width, (uint)height, 1),
            MipLevels = 1,
            ArrayLayers = 1,
            Format = format,
            Tiling = ImageTiling.Optimal,
            InitialLayout = ImageLayout.Undefined,
            Usage = usage,
            Samples = SampleCountFlags.Count1Bit,
            SharingMode = SharingMode.Exclusive,
        };
        Vk.CreateImage(Device, in info, null, out Image image);

        Vk.GetImageMemoryRequirements(Device, image, out MemoryRequirements requirements);
        MemoryAllocateInfo allocInfo = new()
        {
            SType = StructureType.MemoryAllocateInfo,
            AllocationSize = requirements.Size,
            MemoryTypeIndex = FindMemoryType(requirements.MemoryTypeBits, MemoryPropertyFlags.DeviceLocalBit),
        };
        Vk.AllocateMemory(Device, in allocInfo, null, out DeviceMemory memory);
        Vk.BindImageMemory(Device, image, memory, 0);

        // Everything lives in GENERAL: valid both as a colour attachment and as a sampled image.
        //
        // The clear is NOT cosmetic. A fresh Vulkan image contains garbage, whereas GL/Raylib hand back a
        // zeroed FBO texture — and the game relies on that: the spell card's effect chain composites through
        // targets it never fully covers, so on Vulkan the uninitialised memory showed through as per-pixel
        // speckle noise all over the playfield.
        OutsidePass(cmd =>
        {
            LayoutBarrier(cmd, image, ImageLayout.Undefined, ImageLayout.General);

            ClearColorValue clear = new(0, 0, 0, 0);
            ImageSubresourceRange range = new(ImageAspectFlags.ColorBit, 0, 1, 0, 1);
            Vk.CmdClearColorImage(cmd, image, ImageLayout.General, in clear, 1, in range);
        });

        int id = NextId++;
        Textures[id] = new VkTexture
        {
            Image = image,
            Memory = memory,
            View = CreateView(image, format),
            Width = width,
            Height = height,
        };
        return new TextureHandle(id);
    }

    private TextureHandle CreateTexture(byte[] rgba, int width, int height)
    {
        TextureHandle handle = CreateImage(width, height, Format.R8G8B8A8Unorm,
            ImageUsageFlags.SampledBit | ImageUsageFlags.TransferDstBit);
        Upload(handle, rgba, width, height);
        return handle;
    }

    private void Upload(TextureHandle handle, byte[] rgba, int width, int height)
    {
        VkTexture texture = Textures[handle.Id];
        ulong size = (ulong)(width * height * 4);

        RingBuffer staging = CreateRing(size, BufferUsageFlags.TransferSrcBit);
        Marshal.Copy(rgba, 0, (nint)staging.Mapped, rgba.Length);

        // Record the copy into the SAME command buffer that carries this image's UNDEFINED->GENERAL barrier.
        // Submitting it separately (as a one-shot) ran the copy before that barrier had been submitted, so the
        // image was still UNDEFINED when sampled — VUID-vkCmdDraw-None-09600.
        OutsidePass(cmd =>
        {
            BufferImageCopy copy = new()
            {
                ImageSubresource = new ImageSubresourceLayers(ImageAspectFlags.ColorBit, 0, 0, 1),
                ImageExtent = new Extent3D((uint)width, (uint)height, 1),
            };
            Vk.CmdCopyBufferToImage(cmd, staging.Buffer, texture.Image, ImageLayout.General, 1, in copy);
        });

        // The GPU has not consumed it yet; free once the work it belongs to has completed.
        DeferredBuffers.Add((staging.Buffer, staging.Memory));
    }

    private void LayoutBarrier(CommandBuffer cmd, Image image, ImageLayout from, ImageLayout to)
    {
        ImageMemoryBarrier barrier = new()
        {
            SType = StructureType.ImageMemoryBarrier,
            OldLayout = from,
            NewLayout = to,
            SrcQueueFamilyIndex = Vk.QueueFamilyIgnored,
            DstQueueFamilyIndex = Vk.QueueFamilyIgnored,
            Image = image,
            SubresourceRange = new ImageSubresourceRange(ImageAspectFlags.ColorBit, 0, 1, 0, 1),
            SrcAccessMask = AccessFlags.None,
            DstAccessMask = AccessFlags.MemoryReadBit | AccessFlags.MemoryWriteBit,
        };
        Vk.CmdPipelineBarrier(cmd, PipelineStageFlags.TopOfPipeBit, PipelineStageFlags.AllCommandsBit,
            0, 0, null, 0, null, 1, in barrier);
    }

    /// <summary>Records and submits a throwaway command buffer, waiting for it. Used at load time only.</summary>
    private void OneShot(Action<CommandBuffer> record)
    {
        CommandBufferAllocateInfo allocInfo = new()
        {
            SType = StructureType.CommandBufferAllocateInfo,
            CommandPool = CommandPool,
            Level = CommandBufferLevel.Primary,
            CommandBufferCount = 1,
        };
        Vk.AllocateCommandBuffers(Device, in allocInfo, out CommandBuffer cmd);

        CommandBufferBeginInfo begin = new()
        {
            SType = StructureType.CommandBufferBeginInfo,
            Flags = CommandBufferUsageFlags.OneTimeSubmitBit,
        };
        Vk.BeginCommandBuffer(cmd, in begin);
        record(cmd);
        Vk.EndCommandBuffer(cmd);

        SubmitInfo submit = new()
        {
            SType = StructureType.SubmitInfo,
            CommandBufferCount = 1,
            PCommandBuffers = &cmd,
        };
        Vk.QueueSubmit(GraphicsQueue, 1, in submit, default);
        Vk.QueueWaitIdle(GraphicsQueue);
        Vk.FreeCommandBuffers(Device, CommandPool, 1, in cmd);
    }

    public TextureHandle LoadTexture(string path)
    {
        if (!Ready || !File.Exists(path))
            return TextureHandle.None;
        using FileStream stream = File.OpenRead(path);
        ImageResult image = ImageResult.FromStream(stream, ColorComponents.RedGreenBlueAlpha);
        return CreateTexture(image.Data, image.Width, image.Height);
    }

    public void UnloadTexture(TextureHandle texture) => DestroyTextureInternal(texture);

    private void DestroyTextureInternal(TextureHandle texture)
    {
        if (!Textures.Remove(texture.Id, out VkTexture? t))
            return;
        DeferredImages.Add((t.Image, t.Memory, t.View));
    }

    /// <summary>Frees what the GPU has finished with. Called right after the in-flight fence is signalled.</summary>
    private void CollectGarbage()
    {
        foreach (Framebuffer framebuffer in DeferredFramebuffers)
            Vk.DestroyFramebuffer(Device, framebuffer, null);
        DeferredFramebuffers.Clear();

        foreach ((Image image, DeviceMemory memory, ImageView view) in DeferredImages)
        {
            Vk.DestroyImageView(Device, view, null);
            Vk.DestroyImage(Device, image, null);
            Vk.FreeMemory(Device, memory, null);
        }
        DeferredImages.Clear();

        foreach ((Buffer buffer, DeviceMemory memory) in DeferredBuffers)
        {
            Vk.DestroyBuffer(Device, buffer, null);
            Vk.FreeMemory(Device, memory, null);
        }
        DeferredBuffers.Clear();
    }

    public bool IsValid(TextureHandle texture) => Textures.ContainsKey(texture.Id);

    public Vector2 GetTextureSize(TextureHandle texture) =>
        Textures.TryGetValue(texture.Id, out VkTexture? t) ? new Vector2(t.Width, t.Height) : Vector2.Zero;

    public void SetTextureFilter(TextureHandle texture, FilterMode filter)
    {
        if (Textures.TryGetValue(texture.Id, out VkTexture? t))
            t.Linear = filter != FilterMode.Point;
    }

    // ======================================================================================
    //  shaders
    // ======================================================================================

    public ShaderHandle LoadShader(string? vertexPath, string fragmentPath)
    {
        if (!Ready)
            return ShaderHandle.None;

        // The game hands us Assets/Shaders/<name>.fs; we consume the precompiled SPIR-V instead.
        string name = Path.GetFileNameWithoutExtension(fragmentPath);
        string vertexName = vertexPath != null ? Path.GetFileNameWithoutExtension(vertexPath) : "base";
        return LoadSpirv(vertexName, name);
    }

    public ShaderHandle LoadShaderFromSource(string? vertexSource, string fragmentSource)
    {
        // Runtime GLSL compilation would need shaderc; the game only uses this for the text shaders, which
        // also exist on disk. Fall back to the default shader rather than failing the draw.
        return DefaultShader;
    }

    private ShaderHandle LoadSpirv(string vertexName, string fragmentName)
    {
        string dir = "Assets/Shaders/vulkan";
        string vertexSpv = $"{dir}/{vertexName}.vert.spv";
        string fragmentSpv = $"{dir}/{fragmentName}.frag.spv";

        if (!File.Exists(vertexSpv) || !File.Exists(fragmentSpv))
        {
            Console.WriteLine($"Vulkan: no SPIR-V for {vertexName}/{fragmentName} — run Tools/compile_shaders.py");
            return ShaderHandle.None;
        }

        VkShader shader = new()
        {
            Vertex = CreateModule(File.ReadAllBytes(vertexSpv)),
            Fragment = CreateModule(File.ReadAllBytes(fragmentSpv)),
            VertexReflection = ReadReflection($"{dir}/{vertexName}.vert.json"),
            FragmentReflection = ReadReflection($"{dir}/{fragmentName}.frag.json"),
        };
        shader.VertexBlock = new byte[Math.Max(16, shader.VertexReflection.blockSize)];
        shader.FragmentBlock = new byte[Math.Max(16, shader.FragmentReflection.blockSize)];

        // colDiffuse is the tint every one of the game's shaders multiplies by. Raylib and GL supply it
        // implicitly (white); in Vulkan it is just another member of the generated uniform block, and a
        // zeroed block means colDiffuse = (0,0,0,0) — i.e. every pixel comes out black.
        shader.ColDiffuseOffset = shader.FragmentReflection.uniforms
            .FirstOrDefault(u => u.name == "colDiffuse")?.offset ?? -1;
        shader.UniformNames = shader.FragmentReflection.uniforms.Select(u => u.name).ToArray();

        BuildLayout(shader);

        int id = NextId++;
        Shaders[id] = shader;
        return new ShaderHandle(id);
    }

    private static Reflection ReadReflection(string path) =>
        File.Exists(path)
            ? JsonSerializer.Deserialize<Reflection>(File.ReadAllText(path)) ?? new Reflection()
            : new Reflection();

    private ShaderModule CreateModule(byte[] spirv)
    {
        fixed (byte* code = spirv)
        {
            ShaderModuleCreateInfo info = new()
            {
                SType = StructureType.ShaderModuleCreateInfo,
                CodeSize = (nuint)spirv.Length,
                PCode = (uint*)code,
            };
            Vk.CreateShaderModule(Device, in info, null, out ShaderModule module);
            return module;
        }
    }

    private void BuildLayout(VkShader shader)
    {
        List<DescriptorSetLayoutBinding> bindings = [];

        foreach (ReflectedSampler sampler in shader.FragmentReflection.samplers)
            bindings.Add(new DescriptorSetLayoutBinding
            {
                Binding = (uint)sampler.binding,
                DescriptorType = DescriptorType.CombinedImageSampler,
                DescriptorCount = 1,
                StageFlags = ShaderStageFlags.FragmentBit,
            });

        if (shader.FragmentReflection.blockBinding >= 0 && shader.FragmentReflection.blockSize > 0)
            bindings.Add(new DescriptorSetLayoutBinding
            {
                Binding = (uint)shader.FragmentReflection.blockBinding,
                DescriptorType = DescriptorType.UniformBuffer,
                DescriptorCount = 1,
                StageFlags = ShaderStageFlags.FragmentBit,
            });

        if (shader.VertexReflection.blockBinding >= 0 && shader.VertexReflection.blockSize > 0)
            bindings.Add(new DescriptorSetLayoutBinding
            {
                Binding = (uint)shader.VertexReflection.blockBinding,
                DescriptorType = DescriptorType.UniformBuffer,
                DescriptorCount = 1,
                StageFlags = ShaderStageFlags.VertexBit,
            });

        DescriptorSetLayoutBinding[] array = bindings.ToArray();
        fixed (DescriptorSetLayoutBinding* p = array)
        {
            DescriptorSetLayoutCreateInfo info = new()
            {
                SType = StructureType.DescriptorSetLayoutCreateInfo,
                BindingCount = (uint)array.Length,
                PBindings = p,
            };
            Vk.CreateDescriptorSetLayout(Device, in info, null, out DescriptorSetLayout layout);
            shader.SetLayout = layout;
        }

        DescriptorSetLayout setLayout = shader.SetLayout;
        PipelineLayoutCreateInfo layoutInfo = new()
        {
            SType = StructureType.PipelineLayoutCreateInfo,
            SetLayoutCount = 1,
            PSetLayouts = &setLayout,
        };
        Vk.CreatePipelineLayout(Device, in layoutInfo, null, out PipelineLayout pipelineLayout);
        shader.Layout = pipelineLayout;
    }

    public void UnloadShader(ShaderHandle shader)
    {
        if (!Shaders.Remove(shader.Id, out VkShader? s))
            return;
        Vk.DeviceWaitIdle(Device);
        Vk.DestroyShaderModule(Device, s.Vertex, null);
        Vk.DestroyShaderModule(Device, s.Fragment, null);
        Vk.DestroyPipelineLayout(Device, s.Layout, null);
        Vk.DestroyDescriptorSetLayout(Device, s.SetLayout, null);
    }

    public bool IsValid(ShaderHandle shader) => Shaders.ContainsKey(shader.Id);

    public void BeginShader(ShaderHandle shader) => ActiveShader = shader;

    public void EndShader() => ActiveShader = ShaderHandle.None;

    public IReadOnlyList<string> GetUniformNames(ShaderHandle shader) =>
        Shaders.TryGetValue(shader.Id, out VkShader? s) ? s.UniformNames : Array.Empty<string>();

    /// <summary>
    /// A "location" is an index into the reflected uniform list (fragment first, then vertex), which is how
    /// a name maps onto a byte offset in the shader's default uniform block.
    /// </summary>
    public int GetUniformLocation(ShaderHandle shader, string name)
    {
        if (!Shaders.TryGetValue(shader.Id, out VkShader? s))
            return -1;

        int index = s.FragmentReflection.uniforms.FindIndex(u => u.name == name);
        if (index >= 0)
            return index;

        int vertexIndex = s.VertexReflection.uniforms.FindIndex(u => u.name == name);
        if (vertexIndex >= 0)
            return 1000 + vertexIndex;   // vertex-stage uniforms live above the fragment range

        // Sampler uniforms have no block offset; encode the binding so SetUniformTexture can find it.
        ReflectedSampler? sampler = s.FragmentReflection.samplers.FirstOrDefault(x => x.name == name);
        return sampler != null ? 2000 + sampler.binding : -1;
    }

    private (byte[] Block, int Offset)? Resolve(VkShader shader, int location)
    {
        if (location >= 2000 || location < 0)
            return null;
        if (location >= 1000)
        {
            int index = location - 1000;
            return index < shader.VertexReflection.uniforms.Count
                ? (shader.VertexBlock, shader.VertexReflection.uniforms[index].offset)
                : null;
        }
        return location < shader.FragmentReflection.uniforms.Count
            ? (shader.FragmentBlock, shader.FragmentReflection.uniforms[location].offset)
            : null;
    }

    public void SetUniform<T>(ShaderHandle shader, int location, T value, UniformType type) where T : unmanaged
    {
        if (!Shaders.TryGetValue(shader.Id, out VkShader? s))
            return;
        if (Resolve(s, location) is not { } slot)
            return;

        int size = sizeof(T);
        if (slot.Offset + size > slot.Block.Length)
            return;
        fixed (byte* destination = &slot.Block[slot.Offset])
            System.Buffer.MemoryCopy(&value, destination, size, size);
    }

    public void SetUniform<T>(ShaderHandle shader, string name, T value, UniformType type) where T : unmanaged =>
        SetUniform(shader, GetUniformLocation(shader, name), value, type);

    public void SetUniformArray(ShaderHandle shader, int location, float[] values, UniformType type)
    {
        if (!Shaders.TryGetValue(shader.Id, out VkShader? s))
            return;
        if (Resolve(s, location) is not { } slot)
            return;

        int size = Math.Min(values.Length * sizeof(float), slot.Block.Length - slot.Offset);
        if (size <= 0)
            return;
        fixed (float* source = values)
        fixed (byte* destination = &slot.Block[slot.Offset])
            System.Buffer.MemoryCopy(source, destination, size, size);
    }

    public void SetUniformTexture(ShaderHandle shader, int location, TextureHandle texture)
    {
        if (!Shaders.TryGetValue(shader.Id, out VkShader? s) || location < 2000)
            return;
        s.BoundTextures[location - 2000] = texture;
    }

    // ======================================================================================
    //  pipelines + drawing
    // ======================================================================================

    private Pipeline GetPipeline(ShaderHandle handle, VkShader shader, BlendMode blend)
    {
        (int, int, BlendMode) key = (handle.Id, CurrentPass, blend);
        if (Pipelines.TryGetValue(key, out Pipeline cached))
            return cached;

        byte* entry = (byte*)SilkMarshal.StringToPtr("main");
        PipelineShaderStageCreateInfo[] stages =
        [
            new()
            {
                SType = StructureType.PipelineShaderStageCreateInfo,
                Stage = ShaderStageFlags.VertexBit,
                Module = shader.Vertex,
                PName = entry,
            },
            new()
            {
                SType = StructureType.PipelineShaderStageCreateInfo,
                Stage = ShaderStageFlags.FragmentBit,
                Module = shader.Fragment,
                PName = entry,
            },
        ];

        VertexInputBindingDescription binding = new(0, VertexFloats * sizeof(float), VertexInputRate.Vertex);
        VertexInputAttributeDescription[] attributes =
        [
            new(0, 0, Format.R32G32B32Sfloat, 0),                    // vertexPosition
            new(1, 0, Format.R32G32Sfloat, 3 * sizeof(float)),       // vertexTexCoord
            new(2, 0, Format.R32G32B32Sfloat, 5 * sizeof(float)),    // vertexNormal
            new(3, 0, Format.R32G32B32A32Sfloat, 8 * sizeof(float)), // vertexColor
        ];

        PipelineColorBlendAttachmentState blendState = BlendState(blend);
        DynamicState[] dynamicStates = [DynamicState.Viewport, DynamicState.Scissor];

        fixed (PipelineShaderStageCreateInfo* stagePtr = stages)
        fixed (VertexInputAttributeDescription* attributePtr = attributes)
        fixed (DynamicState* dynamicPtr = dynamicStates)
        {
            PipelineVertexInputStateCreateInfo vertexInput = new()
            {
                SType = StructureType.PipelineVertexInputStateCreateInfo,
                VertexBindingDescriptionCount = 1,
                PVertexBindingDescriptions = &binding,
                VertexAttributeDescriptionCount = (uint)attributes.Length,
                PVertexAttributeDescriptions = attributePtr,
            };

            PipelineInputAssemblyStateCreateInfo assembly = new()
            {
                SType = StructureType.PipelineInputAssemblyStateCreateInfo,
                Topology = PrimitiveTopology.TriangleList,
            };

            PipelineViewportStateCreateInfo viewport = new()
            {
                SType = StructureType.PipelineViewportStateCreateInfo,
                ViewportCount = 1,
                ScissorCount = 1,
            };

            PipelineRasterizationStateCreateInfo raster = new()
            {
                SType = StructureType.PipelineRasterizationStateCreateInfo,
                PolygonMode = PolygonMode.Fill,
                CullMode = CullModeFlags.None,
                FrontFace = FrontFace.CounterClockwise,
                LineWidth = 1,
            };

            PipelineMultisampleStateCreateInfo multisample = new()
            {
                SType = StructureType.PipelineMultisampleStateCreateInfo,
                RasterizationSamples = SampleCountFlags.Count1Bit,
            };

            PipelineColorBlendStateCreateInfo colourBlend = new()
            {
                SType = StructureType.PipelineColorBlendStateCreateInfo,
                AttachmentCount = 1,
                PAttachments = &blendState,
            };

            PipelineDynamicStateCreateInfo dynamic = new()
            {
                SType = StructureType.PipelineDynamicStateCreateInfo,
                DynamicStateCount = (uint)dynamicStates.Length,
                PDynamicStates = dynamicPtr,
            };

            GraphicsPipelineCreateInfo info = new()
            {
                SType = StructureType.GraphicsPipelineCreateInfo,
                StageCount = 2,
                PStages = stagePtr,
                PVertexInputState = &vertexInput,
                PInputAssemblyState = &assembly,
                PViewportState = &viewport,
                PRasterizationState = &raster,
                PMultisampleState = &multisample,
                PColorBlendState = &colourBlend,
                PDynamicState = &dynamic,
                Layout = shader.Layout,
                RenderPass = CurrentPass == 0 ? SwapchainPass : OffscreenPass,
                Subpass = 0,
            };

            Vk.CreateGraphicsPipelines(Device, default, 1, in info, null, out Pipeline pipeline);
            SilkMarshal.Free((nint)entry);
            Pipelines[key] = pipeline;
            return pipeline;
        }
    }

    private static PipelineColorBlendAttachmentState BlendState(BlendMode blend)
    {
        ColorComponentFlags all = ColorComponentFlags.RBit | ColorComponentFlags.GBit |
                                  ColorComponentFlags.BBit | ColorComponentFlags.ABit;

        return blend switch
        {
            BlendMode.CopyRgb => new PipelineColorBlendAttachmentState
            {
                BlendEnable = true,
                ColorWriteMask = all,
                SrcColorBlendFactor = BlendFactor.One,
                DstColorBlendFactor = BlendFactor.Zero,
                ColorBlendOp = BlendOp.Add,
                SrcAlphaBlendFactor = BlendFactor.One,
                DstAlphaBlendFactor = BlendFactor.Zero,
                AlphaBlendOp = BlendOp.Add,
            },
            BlendMode.Additive => new PipelineColorBlendAttachmentState
            {
                BlendEnable = true,
                ColorWriteMask = all,
                SrcColorBlendFactor = BlendFactor.SrcAlpha,
                DstColorBlendFactor = BlendFactor.One,
                ColorBlendOp = BlendOp.Add,
                SrcAlphaBlendFactor = BlendFactor.One,
                DstAlphaBlendFactor = BlendFactor.One,
                AlphaBlendOp = BlendOp.Add,
            },
            _ => new PipelineColorBlendAttachmentState
            {
                BlendEnable = true,
                ColorWriteMask = all,
                SrcColorBlendFactor = BlendFactor.SrcAlpha,
                DstColorBlendFactor = BlendFactor.OneMinusSrcAlpha,
                ColorBlendOp = BlendOp.Add,
                SrcAlphaBlendFactor = BlendFactor.SrcAlpha,
                DstAlphaBlendFactor = BlendFactor.OneMinusSrcAlpha,
                AlphaBlendOp = BlendOp.Add,
            },
        };
    }

    private BlendMode CurrentBlend = BlendMode.Alpha;

    public void BeginBlend(BlendMode mode) => CurrentBlend = mode;

    public void EndBlend() => CurrentBlend = BlendMode.Alpha;

    public void Clear(Rgba color)
    {
        if (!Ready || !InPass)
            return;

        ClearAttachment attachment = new()
        {
            AspectMask = ImageAspectFlags.ColorBit,
            ColorAttachment = 0,
            ClearValue = new ClearValue(new ClearColorValue(
                color.R / 255f, color.G / 255f, color.B / 255f, color.A / 255f)),
        };
        ClearRect rect = new()
        {
            Rect = new Rect2D(new Offset2D(0, 0), new Extent2D((uint)FrameWidth, (uint)FrameHeight)),
            BaseArrayLayer = 0,
            LayerCount = 1,
        };
        Vk.CmdClearAttachments(Cmd, 1, in attachment, 1, in rect);
    }

    public void DrawTexture(TextureHandle texture, Vector2 position, Rgba tint) =>
        DrawTexture(texture, position, 0, 1, tint);

    public void DrawTexture(TextureHandle texture, Vector2 position, float rotation, float scale, Rgba tint)
    {
        Vector2 size = GetTextureSize(texture);
        DrawTexture(texture, new Rect(0, 0, size.X, size.Y),
            new Rect(position.X, position.Y, size.X * scale, size.Y * scale), Vector2.Zero, rotation, tint);
    }

    public void DrawTexture(TextureHandle texture, Rect source, Rect destination, Vector2 origin, float rotation,
        Rgba tint)
    {
        if (!Ready || !InPass || !Textures.TryGetValue(texture.Id, out VkTexture? t))
            return;   // no pass bound (e.g. drawing to the window outside a frame) -> nothing to record into
        DrawQuad(texture, t, source, destination, origin, rotation, tint);
    }

    public void DrawNinePatch(TextureHandle texture, NinePatch patch, Rect destination, Vector2 origin,
        float rotation, Rgba tint) =>
        DrawTexture(texture, patch.Source, destination, origin, rotation, tint);

    public void DrawRect(Rect rect, Rgba color) => DrawRect(rect, Vector2.Zero, 0, color);

    public void DrawRect(Rect rect, Vector2 origin, float rotation, Rgba color)
    {
        if (!Ready || !InPass || !Textures.TryGetValue(WhitePixel.Id, out VkTexture? white))
            return;
        DrawQuad(WhitePixel, white, new Rect(0, 0, 1, 1), rect, origin, rotation, color);
    }

    public void DrawLine(Vector2 from, Vector2 to, Rgba color)
    {
        Vector2 delta = to - from;
        float length = delta.Length();
        if (length < 0.0001f)
            return;
        DrawRect(new Rect(from.X, from.Y, length, 1), Vector2.Zero,
            MathF.Atan2(delta.Y, delta.X) * 180f / MathF.PI, color);
    }

    private void DrawQuad(TextureHandle handle, VkTexture texture, Rect source, Rect dest, Vector2 origin,
        float rotation, Rgba tint)
    {
        ShaderHandle shaderHandle = ActiveShader.IsValid ? ActiveShader : DefaultShader;
        if (!Shaders.TryGetValue(shaderHandle.Id, out VkShader? shader))
            return;

        // A NEGATIVE DESTINATION extent means "same rectangle", not "flip": the game builds LeftDest from
        // UILeftSource.Size, and that source is the flipped (0, H, W, -H) form, so the HUD's dest height
        // arrives as -1200. Raylib draws that as if it were positive (the flip comes from the negative
        // SOURCE); building the quad literally puts it at y in [-1200, 0], off the top of the screen — which
        // is exactly why the whole right-hand HUD strip was missing on this backend.
        if (dest.Width < 0)
            dest.Width = -dest.Width;
        if (dest.Height < 0)
            dest.Height = -dest.Height;

        // Raylib's flip semantics, relied on by every render-target draw (see SilkGLBackend for the detail).
        bool flipX = false;
        if (source.Width < 0)
        {
            flipX = true;
            source.Width *= -1;
        }
        if (source.Height < 0)
            source.Y -= source.Height;

        float left = (flipX ? source.X + source.Width : source.X) / texture.Width;
        float right = (flipX ? source.X : source.X + source.Width) / texture.Width;
        float top = source.Y / texture.Height;
        float bottom = (source.Y + source.Height) / texture.Height;

        float radians = rotation * MathF.PI / 180f;
        float cos = MathF.Cos(radians), sin = MathF.Sin(radians);

        Span<Vector2> corners =
        [
            new(-origin.X, -origin.Y),
            new(-origin.X, -origin.Y + dest.Height),
            new(-origin.X + dest.Width, -origin.Y + dest.Height),
            new(-origin.X + dest.Width, -origin.Y),
        ];
        Span<Vector2> uv = [new(left, top), new(left, bottom), new(right, bottom), new(right, top)];

        // Two triangles: TL BL BR, TL BR TR
        Span<int> order = [0, 1, 2, 0, 2, 3];

        ulong vertexOffset = Vertices.Offset;
        float* vertex = (float*)(Vertices.Mapped + vertexOffset);
        ulong bytes = VerticesPerQuad * VertexFloats * sizeof(float);
        if (vertexOffset + bytes > Vertices.Size)
            return; // ring exhausted this frame

        for (int i = 0; i < 6; i++)
        {
            Vector2 c = corners[order[i]];
            Vector2 t = uv[order[i]];
            int o = i * (int)VertexFloats;
            vertex[o + 0] = dest.X + (c.X * cos - c.Y * sin);
            vertex[o + 1] = dest.Y + (c.X * sin + c.Y * cos);
            vertex[o + 2] = 0;
            vertex[o + 3] = t.X;
            vertex[o + 4] = t.Y;
            vertex[o + 5] = 0;
            vertex[o + 6] = 0;
            vertex[o + 7] = 1;
            vertex[o + 8] = tint.R / 255f;
            vertex[o + 9] = tint.G / 255f;
            vertex[o + 10] = tint.B / 255f;
            vertex[o + 11] = tint.A / 255f;
        }
        Vertices.Offset += bytes;

        // colDiffuse: white, unless the shader/game set it. Without this every draw is multiplied by zero.
        if (shader.ColDiffuseOffset >= 0 && shader.ColDiffuseOffset + 16 <= shader.FragmentBlock.Length)
        {
            Vector4 white = Vector4.One;
            fixed (byte* diffuse = &shader.FragmentBlock[shader.ColDiffuseOffset])
                System.Buffer.MemoryCopy(&white, diffuse, 16, 16);
        }

        // Vulkan's clip space has Y pointing DOWN; OpenGL's points up. The two cases need DIFFERENT
        // projections, because the game was written against GL's conventions:
        //   * the swapchain must come out upright  -> flip Y (top = 0 maps to the top of the window);
        //   * a render target must end up stored the way a GL FBO is (upside down in memory), because the
        //     game samples every target with a negative-height source rect to flip it back. Give targets the
        //     GL-convention ortho and that cancellation works exactly as it does under GL.
        Matrix4x4 mvp = CurrentPass == 0
            ? Matrix4x4.CreateOrthographicOffCenter(0, FrameWidth, 0, FrameHeight, -1, 1)   // window
            : Matrix4x4.CreateOrthographicOffCenter(0, FrameWidth, FrameHeight, 0, -1, 1);  // render target
        fixed (byte* vertexBlock = shader.VertexBlock)
            System.Buffer.MemoryCopy(&mvp, vertexBlock, shader.VertexBlock.Length,
                Math.Min(sizeof(Matrix4x4), shader.VertexBlock.Length));

        DescriptorSet set = AllocateSet(shader);
        WriteDescriptors(shader, set, handle, texture);

        Pipeline pipeline = GetPipeline(shaderHandle, shader, CurrentBlend);
        Vk.CmdBindPipeline(Cmd, PipelineBindPoint.Graphics, pipeline);
        Vk.CmdBindDescriptorSets(Cmd, PipelineBindPoint.Graphics, shader.Layout, 0, 1, in set, 0, null);

        Buffer vertexBuffer = Vertices.Buffer;
        Vk.CmdBindVertexBuffers(Cmd, 0, 1, in vertexBuffer, in vertexOffset);
        Vk.CmdDraw(Cmd, VerticesPerQuad, 1, 0, 0);
    }

    private DescriptorSet AllocateSet(VkShader shader)
    {
        DescriptorSetLayout layout = shader.SetLayout;
        DescriptorSetAllocateInfo info = new()
        {
            SType = StructureType.DescriptorSetAllocateInfo,
            DescriptorPool = DescriptorPool,
            DescriptorSetCount = 1,
            PSetLayouts = &layout,
        };
        Vk.AllocateDescriptorSets(Device, in info, out DescriptorSet set);
        return set;
    }

    private void WriteDescriptors(VkShader shader, DescriptorSet set, TextureHandle primary, VkTexture texture)
    {
        List<WriteDescriptorSet> writes = [];
        List<nint> pinned = [];

        foreach (ReflectedSampler sampler in shader.FragmentReflection.samplers)
        {
            // binding 0 is texture0 (the quad's own texture); anything else was bound by the game.
            VkTexture bound = texture;
            if (sampler.binding != 0 &&
                shader.BoundTextures.TryGetValue(sampler.binding, out TextureHandle extra) &&
                Textures.TryGetValue(extra.Id, out VkTexture? extraTexture))
                bound = extraTexture;

            DescriptorImageInfo* imageInfo = (DescriptorImageInfo*)Marshal.AllocHGlobal(sizeof(DescriptorImageInfo));
            *imageInfo = new DescriptorImageInfo
            {
                ImageLayout = ImageLayout.General,
                ImageView = bound.View,
                Sampler = bound.Linear ? LinearSampler : NearestSampler,
            };
            pinned.Add((nint)imageInfo);

            writes.Add(new WriteDescriptorSet
            {
                SType = StructureType.WriteDescriptorSet,
                DstSet = set,
                DstBinding = (uint)sampler.binding,
                DescriptorCount = 1,
                DescriptorType = DescriptorType.CombinedImageSampler,
                PImageInfo = imageInfo,
            });
        }

        AddBlock(shader.FragmentReflection, shader.FragmentBlock);
        AddBlock(shader.VertexReflection, shader.VertexBlock);

        void AddBlock(Reflection reflection, byte[] data)
        {
            if (reflection.blockBinding < 0 || reflection.blockSize <= 0)
                return;

            ulong offset = Uniforms.Offset;
            ulong size = (ulong)Math.Max(reflection.blockSize, 16);
            if (offset + size > Uniforms.Size)
                return;

            fixed (byte* source = data)
                System.Buffer.MemoryCopy(source, Uniforms.Mapped + offset, size,
                    Math.Min((ulong)data.Length, size));
            Uniforms.Offset += (size + 255) & ~255UL; // keep alignment comfortable

            DescriptorBufferInfo* bufferInfo = (DescriptorBufferInfo*)Marshal.AllocHGlobal(sizeof(DescriptorBufferInfo));
            *bufferInfo = new DescriptorBufferInfo
            {
                Buffer = Uniforms.Buffer,
                Offset = offset,
                Range = size,
            };
            pinned.Add((nint)bufferInfo);

            writes.Add(new WriteDescriptorSet
            {
                SType = StructureType.WriteDescriptorSet,
                DstSet = set,
                DstBinding = (uint)reflection.blockBinding,
                DescriptorCount = 1,
                DescriptorType = DescriptorType.UniformBuffer,
                PBufferInfo = bufferInfo,
            });
        }

        WriteDescriptorSet[] array = writes.ToArray();
        fixed (WriteDescriptorSet* p = array)
            Vk.UpdateDescriptorSets(Device, (uint)array.Length, p, 0, null);

        foreach (nint ptr in pinned)
            Marshal.FreeHGlobal(ptr);
    }

    // ======================================================================================
    //  text (same stb_truetype atlas as the GL backend)
    // ======================================================================================

    public FontHandle LoadFont(string path, int size)
    {
        if (!Ready)
            return FontHandle.None;

        byte[] ttf = File.ReadAllBytes(path);
        int cellSize = size + 2;
        int columns = (int)MathF.Ceiling(MathF.Sqrt(95f));
        int atlas = 256;
        while (atlas < columns * cellSize)
            atlas *= 2;

        byte[] coverage = new byte[atlas * atlas];
        VkFont font = new() { BaseSize = size };
        StbTrueType.stbtt_fontinfo info = new();

        fixed (byte* ttfPtr = ttf)
        {
            StbTrueType.stbtt_InitFont(info, ttfPtr, 0);
            float scale = StbTrueType.stbtt_ScaleForPixelHeight(info, size);
            int ascent, descent, lineGap;
            StbTrueType.stbtt_GetFontVMetrics(info, &ascent, &descent, &lineGap);

            int penX = 1, penY = 1, rowHeight = 0;
            for (char c = ' '; c < (char)127; c++)
            {
                int advance, bearing;
                StbTrueType.stbtt_GetCodepointHMetrics(info, c, &advance, &bearing);

                int x0, y0, x1, y1;
                StbTrueType.stbtt_GetCodepointBitmapBox(info, c, scale, scale, &x0, &y0, &x1, &y1);
                int w = x1 - x0, h = y1 - y0;

                if (penX + w + 1 >= atlas)
                {
                    penX = 1;
                    penY += rowHeight + 1;
                    rowHeight = 0;
                }

                bool fits = w > 0 && h > 0 && penX + w <= atlas && penY + h <= atlas;
                if (fits)
                    fixed (byte* dst = coverage)
                        StbTrueType.stbtt_MakeCodepointBitmap(info, dst + penY * atlas + penX, w, h, atlas,
                            scale, scale, c);
                else if (w > 0 && h > 0)
                    w = h = 0;

                font.Glyphs[c] = new Glyph
                {
                    U0 = penX / (float)atlas,
                    V0 = penY / (float)atlas,
                    U1 = (penX + w) / (float)atlas,
                    V1 = (penY + h) / (float)atlas,
                    OffsetX = x0,
                    OffsetY = y0 + ascent * scale,
                    AdvanceX = advance * scale,
                    Width = w,
                    Height = h,
                };

                penX += w + 1;
                rowHeight = Math.Max(rowHeight, h);
            }
        }

        byte[] rgba = new byte[atlas * atlas * 4];
        for (int i = 0; i < coverage.Length; i++)
        {
            rgba[i * 4 + 0] = 255;
            rgba[i * 4 + 1] = 255;
            rgba[i * 4 + 2] = 255;
            rgba[i * 4 + 3] = coverage[i];
        }
        font.Atlas = CreateTexture(rgba, atlas, atlas);
        SetTextureFilter(font.Atlas, FilterMode.Bilinear);

        int id = NextId++;
        Fonts[id] = font;
        return new FontHandle(id);
    }

    public void UnloadFont(FontHandle font)
    {
        if (Fonts.Remove(font.Id, out VkFont? f))
            UnloadTexture(f.Atlas);
    }

    public FontHandle GetDefaultFont()
    {
        if (DefaultFontHandle.IsValid || !Ready)
            return DefaultFontHandle;
        string? any = Directory.Exists("Assets/Fonts") ? Directory.GetFiles("Assets/Fonts").FirstOrDefault() : null;
        DefaultFontHandle = any != null ? LoadFont(any, 32) : FontHandle.None;
        return DefaultFontHandle;
    }

    public Vector2 MeasureText(FontHandle font, string text, float fontSize, float spacing)
    {
        if (!Fonts.TryGetValue(font.Id, out VkFont? f) || string.IsNullOrEmpty(text))
            return Vector2.Zero;

        float scale = fontSize / f.BaseSize;
        float width = 0;
        foreach (char c in text)
            if (f.Glyphs.TryGetValue(c, out Glyph g))
                width += g.AdvanceX * scale + spacing;
        return new Vector2(Math.Max(0, width - spacing), fontSize);
    }

    public void DrawText(FontHandle font, string text, Vector2 position, float fontSize, float spacing, Rgba tint)
    {
        if (!Ready || !Fonts.TryGetValue(font.Id, out VkFont? f) || string.IsNullOrEmpty(text))
            return;
        if (!Textures.TryGetValue(f.Atlas.Id, out VkTexture? atlas))
            return;

        float scale = fontSize / f.BaseSize;
        float x = position.X;

        foreach (char c in text)
        {
            if (!f.Glyphs.TryGetValue(c, out Glyph g))
                continue;

            if (g.Width > 0 && g.Height > 0)
            {
                Rect source = new(g.U0 * atlas.Width, g.V0 * atlas.Height,
                    (g.U1 - g.U0) * atlas.Width, (g.V1 - g.V0) * atlas.Height);
                Rect dest = new(x + g.OffsetX * scale, position.Y + g.OffsetY * scale,
                    g.Width * scale, g.Height * scale);
                DrawQuad(f.Atlas, atlas, source, dest, Vector2.Zero, 0, tint);
            }

            x += g.AdvanceX * scale + spacing;
        }
    }

    public void DrawTextPro(FontHandle font, string text, Vector2 position, Vector2 origin, float rotation,
        float fontSize, float spacing, Rgba tint) =>
        DrawText(font, text, position - origin, fontSize, spacing, tint);

    // ======================================================================================
    //  window / input / audio / debug ui
    // ======================================================================================

    public void CloseWindow() => Window?.Close();

    public bool ShouldClose
    {
        get
        {
            Window.DoEvents();
            return Window.IsClosing;
        }
    }

    public void SetWindowSize(int width, int height) => Window.Size = new Vector2D<int>(width, height);

    public WindowMode CurrentWindowMode { get; private set; } = WindowMode.Windowed;

    public int WindowWidth => Window.Size.X;

    public int WindowHeight => Window.Size.Y;

    public int MonitorWidth => Window.Monitor?.VideoMode.Resolution?.X ?? Window.Size.X;

    public int MonitorHeight => Window.Monitor?.VideoMode.Resolution?.Y ?? Window.Size.Y;

    public void ApplyWindowMode(WindowMode mode, int windowedWidth, int windowedHeight)
    {
        // Changing the window size means rebuilding the swapchain; keep it simple and only support the
        // startup size for now.
        switch (mode)
        {
            case WindowMode.Borderless:
            case WindowMode.BorderlessDotByDot:
                Window.WindowBorder = WindowBorder.Hidden;
                break;
            case WindowMode.Exclusive:
                Window.WindowState = WindowState.Fullscreen;
                break;
            default:
                Window.WindowBorder = WindowBorder.Resizable;
                Window.Center();
                break;
        }
        CurrentWindowMode = mode;
    }

    public void SetVSync(bool enabled) => Window.VSync = enabled;

    public void SetTargetFps(int fps)
    {
    }

    public void DisableExitKey()
    {
    }

    public double Time => Environment.TickCount64 / 1000.0 - StartTime;

    public int Fps => FpsValue;

    public void DrawFpsCounter(int x, int y) =>
        DrawText(GetDefaultFont(), $"{FpsValue} FPS", new Vector2(x, y), 20, 2, Rgba.Lime);

    public bool IsKeyDown(KeyCode key) =>
        Keyboard != null && SilkKey(key) is { } k && Keyboard.IsKeyPressed(k);

    private static Key? SilkKey(KeyCode key) => key switch
    {
        KeyCode.Left => Key.Left,
        KeyCode.Right => Key.Right,
        KeyCode.Up => Key.Up,
        KeyCode.Down => Key.Down,
        KeyCode.Escape => Key.Escape,
        KeyCode.Enter => Key.Enter,
        KeyCode.Space => Key.Space,
        KeyCode.Tab => Key.Tab,
        KeyCode.LeftShift => Key.ShiftLeft,
        KeyCode.RightShift => Key.ShiftRight,
        KeyCode.LeftControl => Key.ControlLeft,
        >= KeyCode.A and <= KeyCode.Z => Key.A + (key - KeyCode.A),
        >= KeyCode.Zero and <= KeyCode.Nine => Key.Number0 + (key - KeyCode.Zero),
        _ => null,
    };

    public bool IsMouseDown(MouseBtn button) =>
        Mouse != null && Mouse.IsButtonPressed((Silk.NET.Input.MouseButton)button);

    public Vector2 MousePosition => Mouse?.Position ?? Vector2.Zero;

    private Vector2 LastMouse;

    public Vector2 MouseDelta
    {
        get
        {
            Vector2 current = MousePosition;
            Vector2 delta = current - LastMouse;
            LastMouse = current;
            return delta;
        }
    }

    public float MouseWheel => Mouse?.ScrollWheels.FirstOrDefault().Y ?? 0;

    public int GamepadCount => InputContext?.Gamepads.Count(g => g.IsConnected) ?? 0;

    public void RefreshGamepads()
    {
    }

    public bool IsPadDown(PadButton button)
    {
        if (InputContext == null)
            return false;
        foreach (IGamepad pad in InputContext.Gamepads.Where(g => g.IsConnected))
            foreach (Silk.NET.Input.Button b in pad.Buttons)
                if (b.Pressed && (int)b.Name == (int)button)
                    return true;
        return false;
    }

    public float GetPadAxis(PadAxis axis)
    {
        if (InputContext == null)
            return 0;
        float value = 0;
        foreach (IGamepad pad in InputContext.Gamepads.Where(g => g.IsConnected))
        {
            float current = axis switch
            {
                PadAxis.LeftX => pad.Thumbsticks.Count > 0 ? pad.Thumbsticks[0].X : 0,
                PadAxis.LeftY => pad.Thumbsticks.Count > 0 ? pad.Thumbsticks[0].Y : 0,
                PadAxis.RightX => pad.Thumbsticks.Count > 1 ? pad.Thumbsticks[1].X : 0,
                PadAxis.RightY => pad.Thumbsticks.Count > 1 ? pad.Thumbsticks[1].Y : 0,
                PadAxis.LeftTrigger => pad.Triggers.Count > 0 ? pad.Triggers[0].Position : 0,
                PadAxis.RightTrigger => pad.Triggers.Count > 1 ? pad.Triggers[1].Position : 0,
                _ => 0,
            };
            if (MathF.Abs(current) > MathF.Abs(value))
                value = current;
        }
        return value;
    }

    /// <summary>
    /// No touch API in Silk.NET's windowing layer, so the mouse stands in for one finger while the left
    /// button is held. That is enough to exercise the touch controls on a desktop.
    /// </summary>
    public int TouchCount => Mouse != null && Mouse.IsButtonPressed(Silk.NET.Input.MouseButton.Left) ? 1 : 0;

    public Vector2 GetTouchPosition(int index) => index == 0 ? MousePosition : Vector2.Zero;

    public PadButton? GetPressedPadButton()
    {
        if (InputContext == null)
            return null;
        foreach (IGamepad pad in InputContext.Gamepads.Where(g => g.IsConnected))
            foreach (Silk.NET.Input.Button b in pad.Buttons)
                if (b.Pressed)
                    return (PadButton)(int)b.Name;
        return null;
    }

    private readonly RaylibAudio AudioDevice = new();

    public bool IsAvailable => AudioDevice.IsAvailable;

    public float SfxVolume
    {
        get => AudioDevice.SfxVolume;
        set => AudioDevice.SfxVolume = value;
    }

    public bool Initialize() => AudioDevice.Initialize();

    public SoundHandle LoadSound(string path) => AudioDevice.LoadSound(path);

    public void UnloadSound(SoundHandle sound) => AudioDevice.UnloadSound(sound);

    public void Play(SoundHandle sound) => AudioDevice.Play(sound);

    /// <summary>rlImGui is Raylib-bound, so the ImGui editors are absent here (as with the GL backend).</summary>
    public bool SupportsDebugUi => false;

    public void SetupDebugUi()
    {
    }

    public void BeginDebugUi()
    {
    }

    public void EndDebugUi()
    {
    }

    public void DebugUiImage(TextureHandle texture)
    {
    }

    public void DebugUiImage(TargetHandle target)
    {
    }

    public void Dispose()
    {
        AudioDevice.Dispose();
        if (!Ready)
            return;

        Vk.DeviceWaitIdle(Device);

        foreach (Pipeline pipeline in Pipelines.Values)
            Vk.DestroyPipeline(Device, pipeline, null);
        foreach (VkShader shader in Shaders.Values)
        {
            Vk.DestroyShaderModule(Device, shader.Vertex, null);
            Vk.DestroyShaderModule(Device, shader.Fragment, null);
            Vk.DestroyPipelineLayout(Device, shader.Layout, null);
            Vk.DestroyDescriptorSetLayout(Device, shader.SetLayout, null);
        }
        foreach (VkTarget target in Targets.Values)
            Vk.DestroyFramebuffer(Device, target.Framebuffer, null);
        foreach (VkTexture texture in Textures.Values)
        {
            Vk.DestroyImageView(Device, texture.View, null);
            Vk.DestroyImage(Device, texture.Image, null);
            Vk.FreeMemory(Device, texture.Memory, null);
        }

        Vk.DestroySampler(Device, LinearSampler, null);
        Vk.DestroySampler(Device, NearestSampler, null);
        Vk.DestroyDescriptorPool(Device, DescriptorPool, null);
        Vk.DestroyBuffer(Device, Vertices.Buffer, null);
        Vk.FreeMemory(Device, Vertices.Memory, null);
        Vk.DestroyBuffer(Device, Uniforms.Buffer, null);
        Vk.FreeMemory(Device, Uniforms.Memory, null);

        Vk.DestroyFence(Device, InFlight, null);
        Vk.DestroySemaphore(Device, ImageAvailable, null);
        foreach (Semaphore semaphore in RenderFinished)
            Vk.DestroySemaphore(Device, semaphore, null);
        Vk.DestroyCommandPool(Device, CommandPool, null);

        foreach (Framebuffer framebuffer in SwapchainFramebuffers)
            Vk.DestroyFramebuffer(Device, framebuffer, null);
        foreach (ImageView view in SwapchainViews)
            Vk.DestroyImageView(Device, view, null);

        Vk.DestroyRenderPass(Device, SwapchainPass, null);
        Vk.DestroyRenderPass(Device, OffscreenPass, null);
        KhrSwapchain.DestroySwapchain(Device, Swapchain, null);
        Vk.DestroyDevice(Device, null);
        KhrSurface.DestroySurface(Instance, Surface, null);
        Vk.DestroyInstance(Instance, null);

        Window?.Dispose();
    }
}
