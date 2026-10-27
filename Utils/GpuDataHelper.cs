using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

namespace DmitryAndDemid.Utils;

public class GpuDataHelper
{
    #region UNIVERSAL

    /// <summary>
    /// Asks all methods for getting GPU VRAM Count, such as NVGetTotalVRAMBytes, and combines all together into one total value.
    /// </summary>
    /// <returns></returns>
    public static long GetTotalVRAMBytes()
    {
        long nvBytes = NVGetTotalVRAMBytes();
        // TODO: Add AMD and Intel methods here in the future
        return nvBytes;
    }

    /// <summary>
    /// Asks all methods for getting GPU clocks, such as NVGetGpuClocks, and combines all together into one array.
    /// </summary>
    /// <returns>Array with GPU clocks</returns>
    public static long[] GetGPUClocks()
    {
        long[] nvClocks = NVGetGPUClock();
        // TODO: Add AMD and Intel methods here in the future
        long[] combined = new long[nvClocks.Length];
        Array.Copy(nvClocks, combined, nvClocks.Length);
        return combined;
    }

    /// <summary>
    /// Asks all methods for getting VRAM clock, such as NVGetVRAMClock, and combines all together into one array.
    /// </summary>
    public static long[] GetVRAMClocks()
    {
        long[] nvClocks = NVGetVRAMClock();
                // TODO: Add AMD and Intel methods here in the future
        long[] combined = new long[nvClocks.Length];
        Array.Copy(nvClocks, combined, nvClocks.Length);
        return combined;
    }

    #endregion

    #region NVIDIA
    const string NvidiaMLLibPath = "nvml.dll";

    [DllImport(NvidiaMLLibPath)]
    public static extern int nvmlInit_v2();

    [DllImport(NvidiaMLLibPath)]
    public static extern int nvmlShutdown();

    [DllImport(NvidiaMLLibPath)]
    public static extern int nvmlDeviceGetCount(ref int count);

    [DllImport(NvidiaMLLibPath)]
    public static extern int nvmlDeviceGetHandleByIndex(int index, ref IntPtr device);

    [DllImport(NvidiaMLLibPath)]
    public static extern int nvmlDeviceGetMemoryInfo(IntPtr device, ref NvmlMemory memory);

    [DllImport(NvidiaMLLibPath)]
    public static extern int nvmlDeviceGetName(IntPtr device, StringBuilder name, uint length);
    /// <summary>
    /// Gets the vram clock of specific gpu. Type defined in nvml.h as nvmlClockType_t. Use 0 for graphics clock, 1 for SM clock, 2 for memory clock, 3 for video clock.
    /// </summary>
    [DllImport(NvidiaMLLibPath)]
    public static extern int nvmlDeviceGetClockInfo(IntPtr device, int type, ref uint clock);
    /// <summary>
    /// Gets all NVIDIA GPUs, than asks every for VRAM count and combines the total. Returns 0 if NVML is not available or no NVIDIA GPUs are present.
    /// </summary>
    /// <returns></returns>
    static long NVGetTotalVRAMBytes()
    {
        int result = nvmlInit_v2();
        if (result != 0) // NVML_SUCCESS is 0
            return 0;
        int count = 0;
        result = nvmlDeviceGetCount(ref count);
        if (result != 0 || count <= 0)
        {
            nvmlShutdown();
            return 0;
        }
        long totalVramBytes = 0;
        for (int i = 0; i < count; i++)
        {
            IntPtr device = IntPtr.Zero;
            result = nvmlDeviceGetHandleByIndex(i, ref device);
            if (result != 0)
                continue;
            NvmlMemory memory = new NvmlMemory();
            result = nvmlDeviceGetMemoryInfo(device, ref memory);
            if (result == 0)
                totalVramBytes += (long)memory.Total;
        }
        nvmlShutdown();
        return totalVramBytes;
    }

    /// <summary>
    /// Gets all gpus, than asks every about GPU clock, and returns array of it
    /// </summary>
    static long[] NVGetGPUClock()
    {
        int result = nvmlInit_v2();
        if (result != 0) // NVML_SUCCESS is 0
            return new long[0];
        int count = 0;
        result = nvmlDeviceGetCount(ref count);
        if (result != 0 || count <= 0)
        {
            nvmlShutdown();
            return new long[0];
        }
        long[] gpuClocks = new long[count];
        for (int i = 0; i < count; i++)
        {
            IntPtr device = IntPtr.Zero;
            result = nvmlDeviceGetHandleByIndex(i, ref device);
            if (result != 0)
                continue;
            uint clock = 0;
            result = nvmlDeviceGetClockInfo(device, 0, ref clock); // 0 for graphics clock
            if (result == 0)
                gpuClocks[i] = clock;
        }
        nvmlShutdown();
        return gpuClocks;
    }

    /// <summary>
    /// Gets all nvidia gpus, than asks every for a VRAM Clock and returns array of it
    /// </summary>
    static long[] NVGetVRAMClock()
    {
        int result = nvmlInit_v2();
        if (result != 0) // NVML_SUCCESS is 0
            return new long[0];
        int count = 0;
        result = nvmlDeviceGetCount(ref count);
        if (result != 0 || count <= 0)
        {
            nvmlShutdown();
            return new long[0];
        }
        long[] vramClocks = new long[count];
        for (int i = 0; i < count; i++)
        {
            IntPtr device = IntPtr.Zero;
            result = nvmlDeviceGetHandleByIndex(i, ref device);
            if (result != 0)
                continue;
            uint clock = 0;
            result = nvmlDeviceGetClockInfo(device, 2, ref clock); // 2 for memory clock
            if (result == 0)
                vramClocks[i] = clock;
        }
        nvmlShutdown();
        return vramClocks;
    }

    /// <summary>
    /// NVML memory information.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct NvmlMemory
    {
        public ulong Total;
        public ulong Free;
        public ulong Used;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct NvmlDevice
    {
        IntPtr Handle;
    }
    #endregion
}
