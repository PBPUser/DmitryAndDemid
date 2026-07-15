namespace DmitryAndDemid.Utils;

/// <summary>
/// Where the game's content comes from. On desktop that is plainly the working directory, which is why the
/// code has always called File/Directory on "Assets/..." straight. Inside an APK there is no such directory —
/// the files live behind Android's AssetManager — so every read goes through here instead, and the platform
/// decides what "Assets/Shaders/base.vs" actually means.
///
/// <see cref="Resolve"/> is the escape hatch for the native loaders: Raylib/GL take a path, not a stream, so
/// the source has to be able to hand back a real filesystem path. An Android source satisfies that by
/// unpacking the APK's assets once, on first run.
/// </summary>
public interface IAssetSource
{
    /// <summary>The path a native loader (LoadTexture, LoadShader) can actually open.</summary>
    string Resolve(string path);

    bool Exists(string path);
    bool DirectoryExists(string path);

    /// <summary>Files in a directory, as paths that <see cref="Resolve"/> would accept. Sorted.</summary>
    string[] EnumerateFiles(string directory, string pattern);

    Stream OpenRead(string path);
    string ReadAllText(string path);
    byte[] ReadAllBytes(string path);
}

/// <summary>The desktop source: content sits next to the executable and paths mean what they say.</summary>
public class FileSystemAssetSource : IAssetSource
{
    private readonly string Root;

    public FileSystemAssetSource(string? root = null) => Root = root ?? "";

    public string Resolve(string path) => string.IsNullOrEmpty(Root) ? path : Path.Combine(Root, path);

    public bool Exists(string path) => File.Exists(Resolve(path));
    public bool DirectoryExists(string path) => Directory.Exists(Resolve(path));

    public string[] EnumerateFiles(string directory, string pattern)
    {
        string resolved = Resolve(directory);
        if (!Directory.Exists(resolved))
            return [];
        string[] files = Directory.GetFiles(resolved, pattern);
        Array.Sort(files, StringComparer.Ordinal);
        return files;
    }

    public Stream OpenRead(string path) => File.OpenRead(Resolve(path));
    public string ReadAllText(string path) => File.ReadAllText(Resolve(path));
    public byte[] ReadAllBytes(string path) => File.ReadAllBytes(Resolve(path));
}

/// <summary>
/// The one asset source the game reads through. The host sets <see cref="Source"/> before anything loads;
/// left alone it is the plain filesystem, which is what desktop wants.
/// </summary>
public static class Assets
{
    public static IAssetSource Source { get; set; } = new FileSystemAssetSource();

    public static string Resolve(string path) => Source.Resolve(path);
    public static bool Exists(string path) => Source.Exists(path);
    public static bool DirectoryExists(string path) => Source.DirectoryExists(path);
    public static string[] Files(string directory, string pattern = "*") =>
        Source.EnumerateFiles(directory, pattern);
    public static Stream OpenRead(string path) => Source.OpenRead(path);
    public static string ReadAllText(string path) => Source.ReadAllText(path);
    public static byte[] ReadAllBytes(string path) => Source.ReadAllBytes(path);
}
