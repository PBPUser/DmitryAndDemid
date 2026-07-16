#if SWITCH
using System.Runtime.InteropServices;

namespace DmitryAndDemid.Rendering.Switch;

/// <summary>
/// P/Invoke surface for deko3d + libnx on the Nintendo Switch (Horizon OS), reached through the mono-nx
/// runtime. See <c>docs/switch-port.md</c>.
///
/// IMPORTANT — two things make this file different from a normal binding:
///
///  * <b>Static linking only.</b> mono-nx has no dynamic loader; a P/Invoke resolves against symbols compiled
///    into the runtime itself. The Mono convention for that is the library name <c>"__Internal"</c>. Every
///    entrypoint named here must also be listed in the mono-nx fork's <c>dl_shim.c</c> or the call faults at
///    startup. The canonical list lives in <c>docs/switch-port.md#dl_shim-symbols</c>.
///
///  * <b>Opaque handles, unverified maker layouts.</b> deko3d objects are created from "Maker" structs whose
///    exact field layout lives in <c>deko3d.h</c>. Rather than fabricate those layouts (they cannot be verified
///    without the installed headers / a device), the create calls here take the maker as a raw
///    <see cref="System.IntPtr"/> pointing at a caller-filled buffer. Filling those buffers correctly is Phase 3
///    work; the signatures below are the scaffold, and MUST be checked against the real headers before use.
///
/// Handles (DkDevice, DkQueue, …) are pointers, represented as <see cref="System.IntPtr"/>.
/// </summary>
internal static unsafe class Dk
{
    /// <summary>Mono's name for symbols statically linked into the runtime (mono-nx's dl_shim).</summary>
    private const string Lib = "__Internal";

    // ---- device / memory ------------------------------------------------------------------------------

    [DllImport(Lib)] public static extern IntPtr dkDeviceCreate(IntPtr maker);
    [DllImport(Lib)] public static extern void   dkDeviceDestroy(IntPtr device);

    [DllImport(Lib)] public static extern IntPtr dkMemBlockCreate(IntPtr maker);
    [DllImport(Lib)] public static extern void   dkMemBlockDestroy(IntPtr memblock);
    [DllImport(Lib)] public static extern IntPtr dkMemBlockGetCpuAddr(IntPtr memblock);
    [DllImport(Lib)] public static extern ulong  dkMemBlockGetGpuAddr(IntPtr memblock);

    // ---- queue ----------------------------------------------------------------------------------------

    [DllImport(Lib)] public static extern IntPtr dkQueueCreate(IntPtr maker);
    [DllImport(Lib)] public static extern void   dkQueueDestroy(IntPtr queue);
    [DllImport(Lib)] public static extern void   dkQueueWaitIdle(IntPtr queue);
    [DllImport(Lib)] public static extern void   dkQueueFlush(IntPtr queue);
    /// <summary>Submit a command list (a DkCmdList is a ulong token returned by dkCmdBufFinishList).</summary>
    [DllImport(Lib)] public static extern void   dkQueueSubmitCommands(IntPtr queue, ulong cmdList);
    [DllImport(Lib)] public static extern int    dkQueueAcquireImage(IntPtr queue, IntPtr swapchain);
    [DllImport(Lib)] public static extern void   dkQueuePresentImage(IntPtr queue, IntPtr swapchain, int imageSlot);

    // ---- command buffer -------------------------------------------------------------------------------

    [DllImport(Lib)] public static extern IntPtr dkCmdBufCreate(IntPtr maker);
    [DllImport(Lib)] public static extern void   dkCmdBufDestroy(IntPtr cmdbuf);
    [DllImport(Lib)] public static extern void   dkCmdBufAddMemory(IntPtr cmdbuf, IntPtr memblock, ulong offset, ulong size);
    /// <summary>Seals the recorded commands and returns a replayable DkCmdList token.</summary>
    [DllImport(Lib)] public static extern ulong  dkCmdBufFinishList(IntPtr cmdbuf);
    [DllImport(Lib)] public static extern void   dkCmdBufClear(IntPtr cmdbuf);

    [DllImport(Lib)] public static extern void   dkCmdBufBindRenderTargets(IntPtr cmdbuf, IntPtr colorTargets, int numColorTargets, IntPtr depthTarget);
    [DllImport(Lib)] public static extern void   dkCmdBufClearColorFloat(IntPtr cmdbuf, uint targetId, uint clearMask, float r, float g, float b, float a);
    [DllImport(Lib)] public static extern void   dkCmdBufBindShaders(IntPtr cmdbuf, uint stageMask, IntPtr shaders, int numShaders);
    [DllImport(Lib)] public static extern void   dkCmdBufBindUniformBuffer(IntPtr cmdbuf, uint stage, uint id, ulong bufGpuAddr, uint bufSize);
    [DllImport(Lib)] public static extern void   dkCmdBufBindTextures(IntPtr cmdbuf, uint stage, uint firstId, IntPtr handles, int numHandles);
    [DllImport(Lib)] public static extern void   dkCmdBufBindVtxBuffer(IntPtr cmdbuf, uint id, ulong bufGpuAddr, ulong bufSize);
    [DllImport(Lib)] public static extern void   dkCmdBufBindVtxAttribState(IntPtr cmdbuf, IntPtr attribs, int numAttribs);
    [DllImport(Lib)] public static extern void   dkCmdBufBindVtxBufferState(IntPtr cmdbuf, IntPtr buffers, int numBuffers);
    [DllImport(Lib)] public static extern void   dkCmdBufSetViewports(IntPtr cmdbuf, uint firstId, IntPtr viewports, int numViewports);
    [DllImport(Lib)] public static extern void   dkCmdBufSetScissors(IntPtr cmdbuf, uint firstId, IntPtr scissors, int numScissors);
    [DllImport(Lib)] public static extern void   dkCmdBufBindColorState(IntPtr cmdbuf, IntPtr state);
    [DllImport(Lib)] public static extern void   dkCmdBufBindBlendStates(IntPtr cmdbuf, uint firstId, IntPtr states, int numStates);
    [DllImport(Lib)] public static extern void   dkCmdBufBindImageDescriptorSet(IntPtr cmdbuf, ulong setGpuAddr, int numDescriptors);
    [DllImport(Lib)] public static extern void   dkCmdBufBindSamplerDescriptorSet(IntPtr cmdbuf, ulong setGpuAddr, int numDescriptors);
    [DllImport(Lib)] public static extern void   dkCmdBufDraw(IntPtr cmdbuf, uint primitive, uint vertexCount, uint instanceCount, uint firstVertex, uint firstInstance);

    // ---- images / layouts / descriptors ---------------------------------------------------------------

    [DllImport(Lib)] public static extern void   dkImageLayoutInitialize(IntPtr layout, IntPtr maker);
    [DllImport(Lib)] public static extern ulong  dkImageLayoutGetSize(IntPtr layout);
    [DllImport(Lib)] public static extern uint   dkImageLayoutGetAlignment(IntPtr layout);
    [DllImport(Lib)] public static extern void   dkImageInitialize(IntPtr image, IntPtr layout, IntPtr memblock, ulong offset);
    [DllImport(Lib)] public static extern void   dkImageDescriptorInitialize(IntPtr descriptor, IntPtr image, bool usesLoadOrStore, bool decayMS);
    [DllImport(Lib)] public static extern void   dkSamplerDescriptorInitialize(IntPtr descriptor, IntPtr sampler);

    // ---- swapchain ------------------------------------------------------------------------------------

    [DllImport(Lib)] public static extern IntPtr dkSwapchainCreate(IntPtr maker);
    [DllImport(Lib)] public static extern void   dkSwapchainDestroy(IntPtr swapchain);
    [DllImport(Lib)] public static extern void   dkSwapchainSetCrop(IntPtr swapchain, int left, int top, int right, int bottom);

    // ---- shaders (loaded from an offline UAM-compiled .dksh blob) --------------------------------------

    [DllImport(Lib)] public static extern void   dkShaderInitialize(IntPtr shader, IntPtr maker);

    // ---- fences ---------------------------------------------------------------------------------------

    [DllImport(Lib)] public static extern int    dkFenceWait(IntPtr fence, long timeoutNs);
}

/// <summary>
/// The slice of libnx the backend needs: native window (for the swapchain), gamepad + touch input, romfs,
/// the applet main-loop gate, audio, and the system tick clock. All static-linked (see <see cref="Dk"/>).
/// </summary>
internal static unsafe class Nx
{
    private const string Lib = "__Internal";

    // native window the swapchain presents to
    [DllImport(Lib)] public static extern IntPtr nwindowGetDefault();

    // applet loop / exit gating
    [DllImport(Lib)] public static extern bool   appletMainLoop();
    [DllImport(Lib)] public static extern void   appletLockExit();
    [DllImport(Lib)] public static extern void   appletUnlockExit();

    // gamepad (the modern pad API)
    [DllImport(Lib)] public static extern void   padConfigureInput(uint maxPlayers, uint style);
    [DllImport(Lib)] public static extern void   padInitializeDefault(IntPtr pad);
    [DllImport(Lib)] public static extern void   padUpdate(IntPtr pad);
    [DllImport(Lib)] public static extern ulong  padGetButtons(IntPtr pad);
    [DllImport(Lib)] public static extern ulong  padGetButtonsDown(IntPtr pad);
    /// <summary>Analog stick position, stick 0 = left, 1 = right. Components are in ±0x7FFF (see JOYSTICK_MAX).</summary>
    [DllImport(Lib)] public static extern HidAnalogStickState padGetStickPos(IntPtr pad, uint stick);

    // touch
    [DllImport(Lib)] public static extern void   hidInitializeTouchScreen();
    [DllImport(Lib)] public static extern int    hidGetTouchScreenStates(IntPtr states, int count);

    // romfs (asset backing)
    [DllImport(Lib)] public static extern int    romfsInit();
    [DllImport(Lib)] public static extern int    romfsExit();

    // clock — the whole 60 TPS tick derives from IPlatform.Time
    [DllImport(Lib)] public static extern ulong  armGetSystemTick();
    [DllImport(Lib)] public static extern ulong  armGetSystemTickFreq();
}

/// <summary>
/// One analog stick's position, as libnx reports it (<c>HidAnalogStickState</c>). Both axes are already
/// normalised by libnx to ±<see cref="JoystickMax"/>; positive Y is UP (the engine's screen Y is down, so the
/// backend negates it). 8 bytes, blittable — returned by value from <see cref="Nx.padGetStickPos"/>.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
internal struct HidAnalogStickState
{
    public int X;
    public int Y;

    /// <summary>Full-deflection magnitude libnx normalises a stick axis to (JOYSTICK_MAX).</summary>
    public const float JoystickMax = 0x7FFF;
}

/// <summary>
/// libnx <c>HidNpadButton</c> bit positions (switch/services/hid.h) — a stable ABI, unlike the deko3d maker
/// layouts. These are the bits returned by <see cref="Nx.padGetButtons"/> / <see cref="Nx.padGetButtonsDown"/>.
/// The engine's <see cref="PadButton"/> is mapped onto these by physical slot in <c>Deko3dBackend.NpadBit</c>.
/// The stick-as-dpad bits (16..23) are intentionally omitted: the sticks are read through the axis API instead.
/// </summary>
internal static class HidNpadButton
{
    public const ulong A      = 1UL << 0;
    public const ulong B      = 1UL << 1;
    public const ulong X      = 1UL << 2;
    public const ulong Y      = 1UL << 3;
    public const ulong StickL = 1UL << 4;
    public const ulong StickR = 1UL << 5;
    public const ulong L      = 1UL << 6;
    public const ulong R      = 1UL << 7;
    public const ulong ZL     = 1UL << 8;
    public const ulong ZR     = 1UL << 9;
    public const ulong Plus   = 1UL << 10;
    public const ulong Minus  = 1UL << 11;
    public const ulong Left   = 1UL << 12;
    public const ulong Up     = 1UL << 13;
    public const ulong Right  = 1UL << 14;
    public const ulong Down   = 1UL << 15;
}
#endif
