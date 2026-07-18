using System.Runtime.InteropServices;
using DmitryAndDemid.Utils;
using Xunit;
using Xunit.Abstractions;

namespace DmitryAndDemid.Tests;

/// <summary>
/// Exercises the platform-specific system-info probes headlessly (no renderer → no GPU line, everything else
/// from /proc, /sys and the portable APIs). Asserts only the invariants that must hold on any host, and dumps
/// the full snapshot to the test output so a human can eyeball the CPU/RAM/topology this machine reports.
/// </summary>
public class SystemInfoTests
{
    private readonly ITestOutputHelper output;
    public SystemInfoTests(ITestOutputHelper output) => this.output = output;

    [Fact]
    public void Collect_returns_sane_values_without_a_renderer()
    {
        SystemInfo s = SystemInfo.Collect(null);

        // Dump it — this is the "what does my machine report" view.
        output.WriteLine($"OS      : {s.Os}");
        output.WriteLine($"ARCH    : {s.ProcessArchitecture} / {s.OsArchitecture}");
        output.WriteLine($"CPU     : {s.CpuName}");
        output.WriteLine($"CORES   : {s.PhysicalCores}C / {s.LogicalCores}T  {s.CoreTopology}");
        output.WriteLine($"CLOCK   : {s.MaxClockMHz} MHz");
        output.WriteLine($"RAM     : {(s.TotalRamBytes > 0 ? Benchmark.FormatBytes(s.TotalRamBytes) : "unknown")}");
        output.WriteLine($"GPU     : {s.Gpu ?? "(none — no renderer)"}");
        output.WriteLine($"VRAM    : {(s.VramBytes > 0 ? Benchmark.FormatBytes(s.VramBytes) : "unknown")}");
        output.WriteLine($"NPU     : {s.Npu ?? "(none)"}");

        // Portable invariants, true everywhere:
        Assert.False(string.IsNullOrWhiteSpace(s.Os));
        Assert.True(s.LogicalCores > 0);
        Assert.False(string.IsNullOrWhiteSpace(s.CpuName));
        Assert.NotNull(s.GpuExtensions);
        Assert.Null(s.Gpu);   // no renderer was passed

        // On the Linux dev/CI host the /proc probes must actually resolve.
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            Assert.True(s.TotalRamBytes > 0, "Linux /proc/meminfo probe returned no RAM.");
            Assert.NotEqual("Unknown CPU", s.CpuName);
            Assert.True(s.PhysicalCores > 0, "Linux /proc/cpuinfo probe returned no physical cores.");
            Assert.True(s.PhysicalCores <= s.LogicalCores);
        }
    }
}
