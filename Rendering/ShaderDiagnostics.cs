using System.Runtime.InteropServices;
using System.Text;

namespace DmitryAndDemid.Rendering;

/// <summary>
/// Where a failed shader load leaves its reason. A driver rejecting a shader is the one failure the player
/// can actually act on ("0:23: error: ..." on their GPU but not on ours), and the loader used to report it as
/// a bare "Failed to load shader: &lt;path&gt;" — the compiler log went to a console nobody sees.
///
/// Silk and Vulkan report here directly, since they hold the GL/glslang log themselves. Raylib compiles
/// shaders inside the native library and only surfaces the log through its trace callback, so
/// <see cref="CaptureRaylibLog"/> installs one.
/// </summary>
public static class ShaderDiagnostics
{
    private static readonly List<string> Messages = new();

    /// <summary>Drops everything collected so far. Call before a load you are about to check.</summary>
    public static void Clear()
    {
        lock (Messages)
            Messages.Clear();
    }

    public static void Report(string? message)
    {
        if (string.IsNullOrWhiteSpace(message))
            return;
        lock (Messages)
            Messages.Add(message.Trim());
    }

    public static bool HasError
    {
        get
        {
            lock (Messages)
                return Messages.Count > 0;
        }
    }

    /// <summary>Everything reported since the last <see cref="Clear"/>, newest last, one per line.</summary>
    public static string LastError
    {
        get
        {
            lock (Messages)
                return string.Join("\n", Messages);
        }
    }

    // ---- Raylib trace-log capture ---------------------------------------------------------------

    /// <summary>
    /// Routes raylib's trace log through us so shader compile/link errors end up in <see cref="Messages"/>.
    /// Raylib logs printf-style (format string + va_list), so the arguments have to be formatted by the C
    /// runtime itself: the va_list a variadic callee receives is forwardable to vsnprintf as-is.
    /// </summary>
    public static unsafe void CaptureRaylibLog()
    {
#if !ANDROID
        Raylib_cs.Raylib.SetTraceLogCallback(&OnRaylibLog);
#endif
    }

#if !ANDROID
    [UnmanagedCallersOnly(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvCdecl)])]
    private static unsafe void OnRaylibLog(int level, sbyte* format, sbyte* args)
    {
        string line;
        try
        {
            line = Format(format, args);
        }
        catch
        {
            // A log line is never worth taking the game down for.
            return;
        }

        Console.WriteLine(line);

        // LOG_WARNING is 4, LOG_ERROR 5, LOG_FATAL 6. Raylib reports a rejected shader as
        // "SHADER: [ID 3] Failed to compile ..." followed by the driver's log, both at warning level.
        if (level >= 4 && line.Contains("SHADER", StringComparison.OrdinalIgnoreCase))
            Report(line);
    }

    private const int FormatBufferSize = 4096;

    private static unsafe string Format(sbyte* format, sbyte* args)
    {
        if (format == null)
            return "";

        // Exactly one vsnprintf call: it consumes the va_list, and C# cannot va_copy, so the usual
        // "measure then fill" two-pass would walk a spent argument list and take the process down. One
        // generous fixed buffer instead; a log line longer than this is simply truncated.
        byte* buffer = stackalloc byte[FormatBufferSize];
        int length = VsnPrintf(buffer, FormatBufferSize, format, args);
        if (length < 0)
            return Marshal.PtrToStringUTF8((IntPtr)format) ?? "";

        return Encoding.UTF8.GetString(buffer, Math.Min(length, FormatBufferSize - 1));
    }

    private static unsafe int VsnPrintf(byte* buffer, nuint size, sbyte* format, sbyte* args) =>
        OperatingSystem.IsWindows()
            ? WindowsVsnPrintf(buffer, size, format, args)
            : LibcVsnPrintf(buffer, size, format, args);

    [DllImport("libc", EntryPoint = "vsnprintf")]
    private static extern unsafe int LibcVsnPrintf(byte* buffer, nuint size, sbyte* format, sbyte* args);

    [DllImport("msvcrt", EntryPoint = "vsnprintf")]
    private static extern unsafe int WindowsVsnPrintf(byte* buffer, nuint size, sbyte* format, sbyte* args);
#endif
}
