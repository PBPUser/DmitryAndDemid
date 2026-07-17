using Android.Content.Res;
using DmitryAndDemid.Utils;

namespace DmitryAndDemid.Android;

/// <summary>
/// The game's content, on Android.
///
/// The loaders below the game (Silk's texture/font readers, BitPackage, the shader reader) all want a path
/// they can open — and an APK's assets are not files: they live behind <see cref="AssetManager"/>, compressed,
/// with no directory listing that <c>Directory.GetFiles</c> could ever see. Rather than teach every loader to
/// take a Stream, this unpacks the assets once into the app's private storage on first run and then behaves
/// exactly like the desktop source, rooted there.
///
/// On an unchanged install the per-file copy is skipped (each file already exists), so a relaunch costs one
/// directory walk rather than a re-copy. When a NEW build is installed the unpacked tree is wiped and laid down
/// fresh — keyed on the APK's modification time — so updated assets actually reach the device.
/// </summary>
public class AndroidAssetSource : IAssetSource
{
    private readonly FileSystemAssetSource Inner;

    public AndroidAssetSource(AssetManager assets, string targetRoot, long apkStamp = 0)
    {
        // The game reads paths that all begin "Assets/…", but inside the APK the trees sit directly under the
        // asset root ("Shaders/…", "Data/…") — the build strips the leading folder. So the APK's asset root is
        // unpacked into <targetRoot>/Assets, and a plain filesystem source rooted at <targetRoot> then answers
        // "Assets/Shaders/base.vs" exactly as it does on desktop.
        string assetsDir = Path.Combine(targetRoot, "Assets");
        string stampFile = Path.Combine(targetRoot, ".asset_stamp");

        // Re-extract from scratch whenever the installed APK has changed. Extract() only copies files that are not
        // already on disk, so without this the very FIRST unpack's assets stuck forever: every later build's
        // updated shaders, compiled stages (.sid), textures and data were silently ignored on the device, which
        // made asset-side fixes look like they "didn't take". Stamp the unpack with the APK's modification time
        // (apkStamp); a different stamp means a new build was installed, so wipe the tree and lay it down fresh.
        string current = apkStamp.ToString();
        string previous = "";
        try { if (File.Exists(stampFile)) previous = File.ReadAllText(stampFile); } catch { }
        if (previous != current && Directory.Exists(assetsDir))
        {
            global::Android.Util.Log.Info("aag2", $"APK changed (asset stamp {previous} -> {current}); re-extracting assets");
            try { Directory.Delete(assetsDir, recursive: true); }
            catch (System.Exception e) { global::Android.Util.Log.Warn("aag2", "asset wipe failed: " + e); }
        }

        string[] roots = assets.List("") ?? [];
        global::Android.Util.Log.Info("aag2", $"asset roots: [{string.Join(", ", roots)}]");
        Extract(assets, "", assetsDir);
        try { File.WriteAllText(stampFile, current); } catch { }
        Inner = new FileSystemAssetSource(targetRoot);
        global::Android.Util.Log.Info("aag2",
            $"base.vs present: {Inner.Exists("Assets/Shaders/base.vs")} at {Inner.Resolve("Assets/Shaders/base.vs")}");
    }

    /// <summary>Copies an APK asset subtree to disk, skipping files that are already there.</summary>
    private static void Extract(AssetManager assets, string apkDirectory, string targetDirectory)
    {
        string[] entries = assets.List(apkDirectory) ?? [];
        Directory.CreateDirectory(targetDirectory);

        foreach (string entry in entries)
        {
            string apkPath = string.IsNullOrEmpty(apkDirectory) ? entry : $"{apkDirectory}/{entry}";
            string targetPath = Path.Combine(targetDirectory, entry);

            // AssetManager gives no way to ask "is this a directory?" — a directory is simply an entry whose
            // own List() is non-empty, and a file is one that can be opened. Try the listing first.
            string[] children = assets.List(apkPath) ?? [];
            if (children.Length > 0)
            {
                Extract(assets, apkPath, targetPath);
                continue;
            }

            if (File.Exists(targetPath))
                continue;

            try
            {
                using Stream source = assets.Open(apkPath);
                using FileStream target = File.Create(targetPath);
                source.CopyTo(target);
            }
            catch (Java.IO.IOException)
            {
                // A non-game entry the framework tucked into the asset root (a directory with no children, a
                // pseudo-file) — not ours to copy, and not fatal.
            }
        }
    }

    public string Resolve(string path) => Inner.Resolve(path);
    public bool Exists(string path) => Inner.Exists(path);
    public bool DirectoryExists(string path) => Inner.DirectoryExists(path);
    public string[] EnumerateFiles(string directory, string pattern) => Inner.EnumerateFiles(directory, pattern);
    public Stream OpenRead(string path) => Inner.OpenRead(path);
    public string ReadAllText(string path) => Inner.ReadAllText(path);
    public byte[] ReadAllBytes(string path) => Inner.ReadAllBytes(path);
}
