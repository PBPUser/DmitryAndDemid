using DmitryAndDemid.Rendering;
using Microsoft.Win32;
using System.Runtime.InteropServices;

namespace DmitryAndDemid.Utils;

/// <summary>
/// A snapshot of the host machine for the benchmark / statistics panel: OS, CPU, RAM, and — from the live
/// graphics backend — the GPU. Gathering it is inherently platform-specific: Linux answers from <c>/proc</c>
/// and <c>/sys</c>, Windows from a couple of Win32 calls, and everything falls back to the portable
/// <see cref="RuntimeInformation"/> / <see cref="Environment"/> values. Anything that genuinely cannot be read
/// without root or vendor tools (RAM/VRAM clock, and often the NPU) is left null and shown as "—" rather than
/// guessed at.
///
/// <para>Every probe is defensive: a missing file, a permission error, or an odd format degrades that one field
/// to its default and never throws, so a diagnostics panel can't crash the game.</para>
/// </summary>
public readonly struct SystemInfo
{
    public string Os { get; init; }
    public string OsArchitecture { get; init; }
    public string ProcessArchitecture { get; init; }

    public string CpuName { get; init; }
    public int PhysicalCores { get; init; }   // 0 = unknown
    public int LogicalCores { get; init; }
    public int MaxClockMHz { get; init; }     // 0 = unknown
    /// <summary>Set only on a heterogeneous CPU (Intel P/E, ARM big.LITTLE); null on a uniform one.</summary>
    public string? CoreTopology { get; init; }

    public long TotalRamBytes { get; init; }  // 0 = unknown
    /// <summary>Needs SMBIOS/DMI (root) — null unless a platform can cheaply supply it.</summary>
    public string? RamClock { get; init; }

    public string? Gpu { get; init; }
    public string? GpuApi { get; init; }
    public long VramBytes { get; init; }      // 0 = unknown
    /// <summary>Needs vendor tooling (nvidia-smi / rocm) — null otherwise.</summary>
    public string? VramClock { get; init; }
    /// <summary>A few notable GPU extensions, already trimmed; empty when none were reported.</summary>
    public IReadOnlyList<string> GpuExtensions { get; init; }

    /// <summary>Best-effort neural accelerator presence; null when none detected / not probeable.</summary>
    public string? Npu { get; init; }

    /// <summary>
    /// Collect everything. <paramref name="renderer"/> is queried for the GPU when it can answer; pass the live
    /// <see cref="Engine.Renderer"/>. Safe to call off the render thread — it only reads static device info.
    /// </summary>
    public static SystemInfo Collect(IRenderer? renderer = null)
    {
        bool linux = RuntimeInformation.IsOSPlatform(OSPlatform.Linux);
        bool windows = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);

        // ---- CPU ---------------------------------------------------------------------------
        string cpuName = "";
        int physical = 0, maxMhz = 0;
        string? topology = null;
        if (linux)
        {
            cpuName = LinuxCpuName();
            physical = LinuxPhysicalCores();
            maxMhz = LinuxMaxClockMHz();
            topology = LinuxCoreTopology();
        }
        else if (windows)
        {
            cpuName = WindowsGetCPUName();
        }
        if (string.IsNullOrWhiteSpace(cpuName))
            cpuName = Helper.Translate("benchmark.cpu.undefined");
        cpuName = Helper.TranslateEachWord(cpuName);

        // ---- RAM ---------------------------------------------------------------------------
        long ram = linux ? LinuxTotalRam() : windows ? WindowsTotalRam() : 0;

        // ---- GPU (from the backend, with an OS fallback for VRAM) --------------------------
        GpuInfo? gpu = null;
        try { gpu = renderer?.QueryGpuInfo(); } catch { gpu = null; }


        string gpuName = gpu?.Name ?? Helper.Translate("benchmark.gpu.undefined");
        gpuName = Helper.TranslateEachWord(gpuName);
        


        long vram = gpu?.VramBytes ?? 0;
        if (vram == 0 && linux)
            vram = LinuxVramBytes();
        else if (vram == 0 && windows)
            vram = WindowsVramBytes();


        IReadOnlyList<string> exts = gpu?.Extensions ?? Array.Empty<string>();

        return new SystemInfo
        {
            Os = Helper.TranslateEachWord(RuntimeInformation.OSDescription.Trim()),
            OsArchitecture = RuntimeInformation.OSArchitecture.ToString().ToLowerInvariant(),
            ProcessArchitecture = RuntimeInformation.ProcessArchitecture.ToString().ToLowerInvariant(),

            CpuName = cpuName,
            PhysicalCores = physical,
            LogicalCores = Environment.ProcessorCount,
            MaxClockMHz = maxMhz,
            CoreTopology = topology,

            TotalRamBytes = ram,
            RamClock = null,   // SMBIOS/DMI — needs root; not read here

            Gpu = gpuName,
            GpuApi = gpu?.Api,
            VramBytes = vram,
            VramClock = null,  // nvidia-smi / rocm — not shelled out to here
            GpuExtensions = exts,

            Npu = linux ? LinuxNpu() : null,
        };
    }

    /// <summary>
    /// Returns value from "HKLM/HARDWARE/DESCRIPTION/System/CentralProcessor/0/ProcessorNameString"
    /// </summary>
    /// <returns></returns>
    private static string WindowsGetCPUName()
    {
        const string keyPath = @"HARDWARE\DESCRIPTION\System\CentralProcessor\0";
        const string valueName = "ProcessorNameString";
        using (RegistryKey key = Registry.LocalMachine.OpenSubKey(keyPath, writable: false))
            return key?.GetValue(valueName)?.ToString() ?? string.Empty;
    }

    // ====================================================================================================
    // Linux probes — /proc and /sys, all defensive.
    // ====================================================================================================

    private static string LinuxCpuName()
    {
        foreach (string line in SafeReadLines("/proc/cpuinfo"))
            if (line.StartsWith("model name", StringComparison.Ordinal))
            {
                int c = line.IndexOf(':');
                if (c >= 0) return line[(c + 1)..].Trim();
            }
        // ARM boards often have no "model name"; fall back to the Hardware/Model fields.
        foreach (string line in SafeReadLines("/proc/cpuinfo"))
            if (line.StartsWith("Hardware", StringComparison.Ordinal) || line.StartsWith("Model", StringComparison.Ordinal))
            {
                int c = line.IndexOf(':');
                if (c >= 0) return line[(c + 1)..].Trim();
            }
        return "";
    }

    /// <summary>Physical cores = distinct (physical id, core id) pairs; falls back to "cpu cores" or 0.</summary>
    private static int LinuxPhysicalCores()
    {
        var pairs = new HashSet<(string, string)>();
        string pkg = "", core = "";
        int coresField = 0;
        foreach (string line in SafeReadLines("/proc/cpuinfo"))
        {
            if (line.StartsWith("physical id", StringComparison.Ordinal)) pkg = After(line);
            else if (line.StartsWith("core id", StringComparison.Ordinal)) core = After(line);
            else if (line.StartsWith("cpu cores", StringComparison.Ordinal)) int.TryParse(After(line), out coresField);
            else if (line.Length == 0) { if (core.Length > 0) pairs.Add((pkg, core)); pkg = core = ""; }
        }
        if (core.Length > 0) pairs.Add((pkg, core));
        if (pairs.Count > 0) return pairs.Count;
        return coresField;

        static string After(string l) { int c = l.IndexOf(':'); return c >= 0 ? l[(c + 1)..].Trim() : ""; }
    }

    private static int LinuxMaxClockMHz()
    {
        // cpufreq reports the policy max in kHz; prefer it (the base clock in cpuinfo is the current, throttled one).
        string s = SafeReadText("/sys/devices/system/cpu/cpu0/cpufreq/cpuinfo_max_freq").Trim();
        if (long.TryParse(s, out long khz) && khz > 0)
            return (int)(khz / 1000);
        // No cpufreq (VM, some ARM): take the highest "cpu MHz" seen in cpuinfo.
        double best = 0;
        foreach (string line in SafeReadLines("/proc/cpuinfo"))
            if (line.StartsWith("cpu MHz", StringComparison.Ordinal))
            {
                int c = line.IndexOf(':');
                if (c >= 0 && double.TryParse(line[(c + 1)..].Trim(),
                        System.Globalization.CultureInfo.InvariantCulture, out double mhz))
                    best = Math.Max(best, mhz);
            }
        return (int)Math.Round(best);
    }

    /// <summary>
    /// Heterogeneous-core detection. Intel hybrid exposes separate PMUs at
    /// <c>/sys/devices/cpu_core/cpus</c> (P) and <c>/sys/devices/cpu_atom/cpus</c> (E); ARM big.LITTLE shows
    /// up as distinct <c>cpu_capacity</c> values. Returns e.g. "6 P-cores + 8 E-cores" or "big.LITTLE (4 + 4)",
    /// or null when the CPU is uniform.
    /// </summary>
    private static string? LinuxCoreTopology()
    {
        // These sysfs files list the LOGICAL cpus each PMU owns, so cpu_core/cpus counts P-core *threads*
        // (6 cores × 2 HT = 12), not physical P-cores. E-cores are single-thread on Intel hybrid, so the atom
        // count IS the physical E-core count; recover physical P-cores as (total physical − E).
        int pThreads = CountCpuList(SafeReadText("/sys/devices/cpu_core/cpus"));
        int eCores = CountCpuList(SafeReadText("/sys/devices/cpu_atom/cpus"));
        if (pThreads > 0 && eCores > 0)
        {
            int totalPhysical = LinuxPhysicalCores();
            int pCores = totalPhysical > eCores ? totalPhysical - eCores : pThreads;
            return $"{pCores} P-core{(pCores == 1 ? "" : "s")} + {eCores} E-core{(eCores == 1 ? "" : "s")}";
        }

        // ARM big.LITTLE: group logical CPUs by cpu_capacity.
        var byCapacity = new Dictionary<string, int>();
        for (int i = 0; i < 128; i++)
        {
            string cap = SafeReadText($"/sys/devices/system/cpu/cpu{i}/cpu_capacity").Trim();
            if (cap.Length == 0) { if (i > Environment.ProcessorCount) break; else continue; }
            byCapacity[cap] = byCapacity.GetValueOrDefault(cap) + 1;
        }
        if (byCapacity.Count >= 2)
        {
            // Bigger capacity first.
            var groups = byCapacity.OrderByDescending(kv => long.TryParse(kv.Key, out long c) ? c : 0)
                .Select(kv => kv.Value.ToString());
            return $"big.LITTLE ({string.Join(" + ", groups)})";
        }
        return null;
    }

    private static long LinuxTotalRam()
    {
        foreach (string line in SafeReadLines("/proc/meminfo"))
            if (line.StartsWith("MemTotal:", StringComparison.Ordinal))
            {
                string[] parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 2 && long.TryParse(parts[1], out long kb))
                    return kb * 1024;   // MemTotal is in kB
            }
        return 0;
    }

    static long WindowsVramBytes()
    {
        return GpuDataHelper.GetTotalVRAMBytes();
    }

    /// <summary>AMD exposes total VRAM in sysfs; NVIDIA/others do not, so this is a best-effort AMD-only path.</summary>
    private static long LinuxVramBytes()
    {
        for (int card = 0; card < 8; card++)
        {
            string s = SafeReadText($"/sys/class/drm/card{card}/device/mem_info_vram_total").Trim();
            if (long.TryParse(s, out long bytes) && bytes > 0)
                return bytes;
        }
        return 0;
    }

    /// <summary>
    /// NPU presence, best-effort. Linux 6.x groups compute accelerators under the accel subsystem
    /// (<c>/sys/class/accel/accel*</c>, /dev/accel*) — Intel Meteor Lake NPU, Habana, etc. Report the driver
    /// name when we can read it.
    /// </summary>
    private static string? LinuxNpu()
    {
        try
        {
            if (!Directory.Exists("/sys/class/accel"))
                return null;
            foreach (string dir in Directory.EnumerateDirectories("/sys/class/accel"))
            {
                string drv = SafeReadText(Path.Combine(dir, "device/driver/module/drivers")).Trim();
                string name = SafeReadText(Path.Combine(dir, "device/uevent"))
                    .Split('\n').FirstOrDefault(l => l.StartsWith("DRIVER=", StringComparison.Ordinal)) ?? "";
                if (name.Length > 0) return name["DRIVER=".Length..].Trim();
                if (drv.Length > 0) return drv;
                return "present";
            }
        }
        catch { /* ignore */ }
        return null;
    }

    /// <summary>Count CPUs in a sysfs cpulist like "0-5,12-17" → 12.</summary>
    private static int CountCpuList(string list)
    {
        list = list.Trim();
        if (list.Length == 0) return 0;
        int total = 0;
        foreach (string part in list.Split(',', StringSplitOptions.RemoveEmptyEntries))
        {
            int dash = part.IndexOf('-');
            if (dash < 0)
            {
                if (int.TryParse(part, out _)) total += 1;
            }
            else if (int.TryParse(part[..dash], out int lo) && int.TryParse(part[(dash + 1)..], out int hi) && hi >= lo)
            {
                total += hi - lo + 1;
            }
        }
        return total;
    }

    private static string[] SafeReadLines(string path)
    {
        try { return File.Exists(path) ? File.ReadAllLines(path) : Array.Empty<string>(); }
        catch { return Array.Empty<string>(); }
    }

    private static string SafeReadText(string path)
    {
        try { return File.Exists(path) ? File.ReadAllText(path) : ""; }
        catch { return ""; }
    }

    // ====================================================================================================
    // Windows probes.
    // ====================================================================================================


    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryStatusEx
    {
        public uint dwLength;
        public uint dwMemoryLoad;
        public ulong ullTotalPhys;
        public ulong ullAvailPhys;
        public ulong ullTotalPageFile;
        public ulong ullAvailPageFile;
        public ulong ullTotalVirtual;
        public ulong ullAvailVirtual;
        public ulong ullAvailExtendedVirtual;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx(ref MemoryStatusEx buffer);

    private static long WindowsTotalRam()
    {
        try
        {
            var status = new MemoryStatusEx { dwLength = (uint)Marshal.SizeOf<MemoryStatusEx>() };
            return GlobalMemoryStatusEx(ref status) ? (long)status.ullTotalPhys : 0;
        }
        catch { return 0; }
    }
}
