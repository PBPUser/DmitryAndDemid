using DmitryAndDemid.Utils;
using System;
using Xunit;
using Xunit.Abstractions;

namespace DmitryAndDemid.Tests;

public class NVMLTests
{
    private readonly ITestOutputHelper _output;

    public NVMLTests(ITestOutputHelper output) => _output = output;

    public bool NVMLCheckBeforeStart()
    {
        int result = GpuDataHelper.nvmlInit_v2();
        if(result != 0) // NVML_SUCCESS is 0
            return false;
        result = GpuDataHelper.nvmlShutdown();
        return result == 0; // NVML_SUCCESS is 0
    }

    /// <summary>
    /// Gets gpus and checks total ram of each gpu, then prints to the test output
    /// (visible with: dotnet test --logger "console;verbosity=detailed")
    /// </summary>
    [Fact]
    public void NVMLGetGpusAndCheckTotalRam()
    {
        int result = GpuDataHelper.nvmlInit_v2();
        Assert.Equal(0, result); // NVML_SUCCESS is 0
        int count = 0;
        result = GpuDataHelper.nvmlDeviceGetCount(ref count);
        Assert.Equal(0, result); // NVML_SUCCESS is 0
        for (int i = 0; i < count; i++)
        {
            IntPtr device = IntPtr.Zero;
            result = GpuDataHelper.nvmlDeviceGetHandleByIndex(i, ref device);
            Assert.Equal(0, result); // NVML_SUCCESS is 0
            GpuDataHelper.NvmlMemory memory = new GpuDataHelper.NvmlMemory();
            result = GpuDataHelper.nvmlDeviceGetMemoryInfo(device, ref memory);
            Assert.Equal(0, result); // NVML_SUCCESS is 0
            _output.WriteLine($"GPU {i}: Total Memory: {memory.Total / (1024 * 1024):F2} MB");
        }
        result = GpuDataHelper.nvmlShutdown();
        Assert.Equal(0, result); // NVML_SUCCESS is 0
    }
}
