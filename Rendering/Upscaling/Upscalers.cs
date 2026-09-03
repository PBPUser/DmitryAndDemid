namespace DmitryAndDemid.Rendering.Upscaling;

/// <summary>The window upscaler the player picked in the settings (Configuration.Upscaler, by key).</summary>
public enum UpscalerKind
{
    /// <summary>The plain blit the game always had: point or bilinear depending on the scale.</summary>
    Off,
    /// <summary>AMD FidelityFX Super Resolution 1.0: the EASU upscale followed by RCAS sharpening. Runs in
    /// Likhanov32D's own shaders on every backend, and is what actually shades the pixels for every mode
    /// below that upscales.</summary>
    Fsr,
    /// <summary>DLAA-style: no upscale — the frame is rendered at the window's size and only RCAS-sharpened.
    /// The engine has no temporal history to feed a real DLAA, so this is the native-resolution shape of it.</summary>
    Dlaa,
    /// <summary>Intel XeSS. Needs the XeSS runtime (libxess.dll) next to the game; the engine does not ship it
    /// and produces no motion vectors, so the entry is listed but stays unavailable until both exist.</summary>
    Xess,
    /// <summary>NVIDIA DLSS Super Resolution through the Streamline runtime. Same story as XeSS: listed,
    /// selectable only with the runtime present, pixels shaded by the FSR path meanwhile.</summary>
    Dlss,
    /// <summary>DLSS 5 Neural Rendering: Windows only, from the runtime files in
    /// <see cref="Upscalers.NeuralRenderingFolder"/>. Picking it starts the hidden DirectX 12 pipeline in
    /// <see cref="NeuralRenderingBridge"/>; see that class for how far it gets.</summary>
    DlssNeural,
}

/// <summary>
/// The catalogue of window upscalers: keys for the config, display names, which quality preset renders at
/// which internal scale, and — the part that matters in the settings list — whether a mode can be picked on
/// this platform with these files, and the reason when not. The rules are pure so they are testable; the
/// file probes are the only I/O and are passed in.
/// </summary>
public static class Upscalers
{
    /// <summary>Where the DLSS 5 Neural Rendering runtime is looked for (the Streamline DLLs and the
    /// nvngx_dlssnr module). Overridable with the AAG2_DLSS_NR_DIR environment variable.</summary>
    public const string NeuralRenderingFolder = @"T:\Trash\Temporary\DLSS Neural Rendering";

    /// <summary>The Streamline runtime files a DLSS mode needs; the neural mode needs the last two as well.</summary>
    public static readonly string[] StreamlineFiles = ["sl.interposer.dll", "sl.common.dll", "sl.dlss.dll", "nvngx_dlss.dll"];
    public static readonly string[] NeuralRenderingFiles = ["sl.dlss_nr.dll", "nvngx_dlssnr.dll"];
    public static readonly string[] XessFiles = ["libxess.dll"];

    public static readonly (UpscalerKind Kind, string Key, string Display)[] All =
    [
        (UpscalerKind.Off, "off", "settings.upscaler.off"),
        (UpscalerKind.Fsr, "fsr", "settings.upscaler.fsr"),
        (UpscalerKind.Dlaa, "dlaa", "settings.upscaler.dlaa"),
        (UpscalerKind.Xess, "xess", "settings.upscaler.xess"),
        (UpscalerKind.Dlss, "dlss", "settings.upscaler.dlss"),
        (UpscalerKind.DlssNeural, "dlssnr", "settings.upscaler.dlssnr"),
    ];

    public static UpscalerKind Parse(string? key)
    {
        foreach (var e in All)
            if (string.Equals(e.Key, key, StringComparison.OrdinalIgnoreCase))
                return e.Kind;
        return UpscalerKind.Off;
    }

    public static string KeyOf(UpscalerKind kind) => Array.Find(All, e => e.Kind == kind).Key ?? "off";
    public static string DisplayOf(UpscalerKind kind) => Array.Find(All, e => e.Kind == kind).Display ?? "settings.upscaler.off";

    /// <summary>Quality presets, as every vendor names them: the internal render scale per axis. Native
    /// renders at the window's size and only sharpens; Ultra Performance is a third of it per axis.</summary>
    public static readonly (string Display, float Scale)[] Qualities =
    [
        ("settings.upscaler.quality.native", 1.00f),
        ("settings.upscaler.quality.ultra", 0.77f),
        ("settings.upscaler.quality.quality", 0.67f),
        ("settings.upscaler.quality.balanced", 0.59f),
        ("settings.upscaler.quality.performance", 0.50f),
        ("settings.upscaler.quality.ultraperf", 0.33f),
    ];

    /// <summary>The preset index of Quality (0.67x), the default for a fresh config.</summary>
    public const int DefaultQuality = 2;

    /// <summary>DLSS 5 Neural Rendering model presets (Configuration.DLSSNRPreset): the runtime's latest, then
    /// the lettered ones. The first is a translation key, the letters are shown as they are.</summary>
    public static readonly string[] NeuralRenderingPresets =
        ["settings.dlssnr.preset.latest", "A", "B", "C", "D", "E", "F"];

    public static int ClampNeuralPreset(int preset) => Math.Clamp(preset, 0, NeuralRenderingPresets.Length - 1);

    public static int ClampQuality(int quality) => Math.Clamp(quality, 0, Qualities.Length - 1);

    /// <summary>The internal render scale a mode renders at: 1 for Off and the DLAA-style mode (native), the
    /// preset's scale for everything that upscales.</summary>
    public static float RenderScale(UpscalerKind kind, int quality) =>
        kind is UpscalerKind.Off or UpscalerKind.Dlaa ? 1f : Qualities[ClampQuality(quality)].Scale;

    /// <summary>True when the mode shades its pixels through the FSR pass (everything but Off).</summary>
    public static bool UsesFsrPass(UpscalerKind kind) => kind != UpscalerKind.Off;

    /// <summary>
    /// Why <paramref name="kind"/> cannot be picked here, as a translation key — or null when it can.
    /// <paramref name="isWindows"/>, <paramref name="streamline"/> (the Streamline files exist),
    /// <paramref name="neural"/> (the neural-rendering files exist too) and <paramref name="xess"/> are the
    /// facts about the machine; the rules are the same on every one.
    /// </summary>
    public static string? Unavailable(UpscalerKind kind, bool isWindows, bool streamline, bool neural, bool xess)
    {
        return kind switch
        {
            UpscalerKind.Off or UpscalerKind.Fsr or UpscalerKind.Dlaa => null,
            UpscalerKind.Xess => xess ? null : "settings.upscaler.needs_xess",
            UpscalerKind.Dlss => !isWindows ? "settings.upscaler.windows_only"
                : streamline ? null : "settings.upscaler.needs_streamline",
            UpscalerKind.DlssNeural => !isWindows ? "settings.upscaler.windows_only"
                : streamline && neural ? null : "settings.upscaler.needs_nr_files",
            _ => "settings.upscaler.off",
        };
    }

    /// <summary>The neural-rendering runtime folder in force: the environment override, else the default.</summary>
    public static string NeuralRenderingDirectory =>
        Environment.GetEnvironmentVariable("AAG2_DLSS_NR_DIR") is { Length: > 0 } dir ? dir : NeuralRenderingFolder;

    /// <summary>Whether every file in <paramref name="names"/> exists under <paramref name="directory"/>.</summary>
    public static bool HasFiles(string directory, IEnumerable<string> names)
    {
        try
        {
            if (!Directory.Exists(directory))
                return false;
            foreach (string name in names)
                if (!File.Exists(Path.Combine(directory, name)))
                    return false;
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>The machine's facts, probed once: what is installed where the rules look.</summary>
    public static (bool IsWindows, bool Streamline, bool Neural, bool Xess) Probe()
    {
        bool windows = OperatingSystem.IsWindows();
        string nr = NeuralRenderingDirectory;
        bool streamline = windows && HasFiles(nr, StreamlineFiles);
        bool neural = streamline && HasFiles(nr, NeuralRenderingFiles);
        bool xess = HasFiles(AppContext.BaseDirectory, XessFiles) || HasFiles(Environment.CurrentDirectory, XessFiles);
        return (windows, streamline, neural, xess);
    }

    /// <summary>The reason a mode is unavailable on this machine, or null. The probed form of <see cref="Unavailable(UpscalerKind,bool,bool,bool,bool)"/>.</summary>
    public static string? Unavailable(UpscalerKind kind)
    {
        var p = Probe();
        return Unavailable(kind, p.IsWindows, p.Streamline, p.Neural, p.Xess);
    }
}
