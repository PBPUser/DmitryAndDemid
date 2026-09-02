using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace DmitryAndDemid.Rendering.Upscaling;

/// <summary>
/// The hidden DirectX 12 side of the DLSS 5 Neural Rendering option: a D3D12 device with no window of its own,
/// created only when the player picks that mode on Windows, and the NVIDIA Streamline runtime loaded from
/// <see cref="Upscalers.NeuralRenderingDirectory"/> (sl.interposer.dll and its plugins, the nvngx_dlssnr
/// module) into this process next to it.
///
/// What it does: proves the runtime is loadable and exports the Streamline entry points, and stands up the
/// device the feature would run on. What it does NOT do, and says so in <see cref="Status"/>: evaluate the
/// feature. Streamline is initialised through versioned SDK structs (sl::Preferences and friends) and fed
/// per-frame D3D12 resources — colour, depth, motion vectors, a swapchain it interposes — none of which this
/// engine produces (it is a 2D game drawn through Raylib / Silk GL / Vulkan, with no depth or motion vectors
/// to give). So with the bridge up, the pixels on screen still come from the FSR pass; the bridge reports
/// exactly how far the neural path got, and the settings screen shows that report.
/// </summary>
[SupportedOSPlatform("windows")]
public static class NeuralRenderingBridge
{
    public static bool Active { get; private set; }
    public static string Status { get; private set; } = "";
    private static IntPtr Device;
    private static IntPtr Interposer;

    private static readonly Guid IID_ID3D12Device = new("189819f1-1db6-4b57-be54-1821339b85f7");
    private const int D3D_FEATURE_LEVEL_11_0 = 0xb000;

    [DllImport("d3d12.dll", ExactSpelling = true)]
    private static extern int D3D12CreateDevice(IntPtr adapter, int minimumFeatureLevel, ref Guid riid, out IntPtr device);

    /// <summary>The Streamline entry points the runtime has to export for a DLSS feature to be reachable at all.</summary>
    private static readonly string[] Exports = ["slInit", "slShutdown", "slIsFeatureSupported", "slSetD3DDevice", "slEvaluateFeature", "slSetTag"];

    /// <summary>Starts the bridge; false (with <see cref="Status"/> saying why) when anything is missing.</summary>
    public static bool Start(string directory)
    {
        if (Active)
            return true;
        if (!Upscalers.HasFiles(directory, Upscalers.StreamlineFiles) || !Upscalers.HasFiles(directory, Upscalers.NeuralRenderingFiles))
        {
            Status = $"runtime files missing in {directory}";
            return false;
        }
        try
        {
            // The plugins are found relative to the interposer, so it is loaded by full path from the folder.
            Interposer = NativeLibrary.Load(Path.Combine(directory, "sl.interposer.dll"));
            var missing = new List<string>();
            foreach (string export in Exports)
                if (!NativeLibrary.TryGetExport(Interposer, export, out _))
                    missing.Add(export);
            if (missing.Count > 0)
            {
                Status = "sl.interposer.dll lacks " + string.Join(", ", missing);
                Stop();
                return false;
            }
            Guid iid = IID_ID3D12Device;
            int hr = D3D12CreateDevice(IntPtr.Zero, D3D_FEATURE_LEVEL_11_0, ref iid, out Device);
            if (hr < 0 || Device == IntPtr.Zero)
            {
                Status = $"D3D12CreateDevice failed (0x{hr:X8})";
                Stop();
                return false;
            }
            Active = true;
            Status = "Streamline runtime loaded, D3D12 device up; DLSS NR needs depth/motion-vector inputs this engine has none of, pixels stay on FSR";
            Console.WriteLine($"[dlss-nr] {Status}");
            return true;
        }
        catch (Exception e)
        {
            Status = e.GetType().Name + ": " + e.Message;
            Stop();
            return false;
        }
    }

    public static void Stop()
    {
        if (Device != IntPtr.Zero)
        {
            try { Marshal.Release(Device); } catch { /* nothing to do with a dead device */ }
            Device = IntPtr.Zero;
        }
        if (Interposer != IntPtr.Zero)
        {
            try { NativeLibrary.Free(Interposer); } catch { /* likewise */ }
            Interposer = IntPtr.Zero;
        }
        Active = false;
    }
}
