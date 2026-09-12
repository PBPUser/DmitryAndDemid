using Joveler.Compression.XZ;

namespace NEngineFormat.Core.Utils;

/// <summary>
/// LZMA2 compression, as carried by the xz container.
/// </summary>
/// <remarks>
/// <para>
/// The native library is loaded once, on first use. Joveler wants an explicit path rather than
/// leaving it to the P/Invoke resolver, so the usual output layouts are probed in turn.
/// </para>
/// <para>
/// Compression here is a wrapper around the format, not part of it: a file says in its header
/// whether its blocks are compressed, and everything below that layer works in plain bytes.
/// </para>
/// </remarks>
public static class Lzma2
{
    /// <summary>Compression effort, 0 to 9. Six is liblzma's own default.</summary>
    public const uint DefaultLevel = 6;

#if NET9_0_OR_GREATER
    private static readonly Lock InitGate = new();
#else
    // System.Threading.Lock arrived in .NET 9, and this library is also compiled into a plugin
    // for a host that carries .NET 7.
    private static readonly object InitGate = new();
#endif
    private static bool _initialised;

    /// <summary>Compresses a buffer.</summary>
    public static byte[] Compress(byte[] data, uint level = DefaultLevel)
    {
        ArgumentNullException.ThrowIfNull(data);
        Initialise();

        var output = new MemoryStream();
        var options = new XZCompressOptions { Level = (LzmaCompLevel)level, LeaveOpen = true };

        using (var xz = new XZStream(output, options))
        {
            xz.Write(data, 0, data.Length);
        }

        return output.ToArray();
    }

    /// <summary>Decompresses a buffer produced by <see cref="Compress"/>.</summary>
    /// <param name="expectedLength">
    /// What the data should come to, used to size the buffer and to catch a stream that does
    /// not expand to what its file claimed.
    /// </param>
    public static byte[] Decompress(byte[] data, int expectedLength)
    {
        ArgumentNullException.ThrowIfNull(data);
        Initialise();

        using var input = new MemoryStream(data);
        using var output = new MemoryStream(expectedLength);

        try
        {
            using var xz = new XZStream(input, new XZDecompressOptions());
            xz.CopyTo(output);
        }
        catch (Exception ex) when (ex is not InvalidOperationException)
        {
            // Corrupt input is malformed input, and callers should not have to know which
            // compressor the format happens to use to catch it.
            throw new InvalidOperationException($"Compressed data could not be read: {ex.Message}", ex);
        }

        if (output.Length != expectedLength)
        {
            throw new InvalidOperationException(
                $"Compressed data expanded to {output.Length} bytes, but the file said {expectedLength}.");
        }

        return output.ToArray();
    }

    /// <summary>Loads the native library, once.</summary>
    /// <exception cref="InvalidOperationException">The library could not be found.</exception>
    private static void Initialise()
    {
        if (_initialised)
        {
            return;
        }

        lock (InitGate)
        {
            if (_initialised)
            {
                return;
            }

            XZInit.GlobalInit(FindNativeLibrary());
            _initialised = true;
        }
    }

    /// <summary>
    /// Where the native library ends up depends on how the app was published, so the layouts
    /// are tried in order rather than assumed.
    /// </summary>
    private static string FindNativeLibrary()
    {
        string name = OperatingSystem.IsWindows() ? "liblzma.dll"
            : OperatingSystem.IsMacOS() ? "liblzma.dylib"
            : "liblzma.so";

        string architecture = System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture
            .ToString()
            .ToLowerInvariant();

        string platform = OperatingSystem.IsWindows() ? "win"
            : OperatingSystem.IsMacOS() ? "osx"
            : "linux";

        // Beside this assembly first, then beside the program. A plugin is loaded from a folder
        // of its own, and what it shipped with is there rather than where the host lives.
        string beside = Path.GetDirectoryName(typeof(Lzma2).Assembly.Location) ?? AppContext.BaseDirectory;

        string[] candidates =
        [
            Path.Combine(beside, "runtimes", $"{platform}-{architecture}", "native", name),
            Path.Combine(beside, name),
            Path.Combine(AppContext.BaseDirectory, "runtimes", $"{platform}-{architecture}", "native", name),
            Path.Combine(AppContext.BaseDirectory, name),
            name,
        ];

        foreach (string candidate in candidates)
        {
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        throw new InvalidOperationException(
            $"Could not find {name}; looked in {string.Join(", ", candidates)}.");
    }
}
