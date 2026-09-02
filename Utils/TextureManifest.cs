namespace DmitryAndDemid.Utils;

/// <summary>
/// The keys the texture dictionary is populated with at startup, computed <b>without a GPU</b>: the on-disk
/// scan of <c>Assets/Textures/*.png</c> plus the fixed, procedurally-generated entries.
///
/// <para><see cref="Runtime.LoadTextures"/> is the GPU side — it uploads each texture and fills the live
/// dictionary. This is the accounting half, deliberately split out so it can run headless (through the
/// <see cref="Assets"/> seam, no window, no GL context) in a unit test. The test verifies the registry is
/// deterministic and collision-free: a duplicate key silently overwrites in the dictionary, so the loaded
/// <c>Textures.Count</c> would come out lower than the number of files scanned — exactly the "the texture
/// count isn't what it should be" class of bug this was asked to guard.</para>
///
/// <para>The two halves are tied together at runtime: in DEBUG, <see cref="Runtime.LoadTextures"/> asserts the
/// live dictionary's keys equal <see cref="RegisteredKeys"/>, so if a procedural texture is added on one side
/// and not the other the game throws loudly on boot instead of the unit test quietly checking a stale list.</para>
/// </summary>
public static class TextureManifest
{
    /// <summary>
    /// Keys added by code rather than scanned from disk. This MUST mirror the <c>M(...)</c> additions in
    /// <see cref="Runtime.LoadTextures"/> (the DEBUG self-check there enforces it). Add a procedural texture in
    /// one place and you have to add it here too.
    /// </summary>
    public static readonly string[] ProceduralKeys =
    {
        "MenuItemSelectionGradient1",
        "MenuBackground",
        "Copyright",
        "Version",
        "384x448",
        "GrievanceBox",
        "ScoreDigitsPrerender",
    };

    /// <summary>
    /// The filenames scanned from <c>Assets/Textures</c>. The dictionary keys the game uses are the filename
    /// <b>with</b> extension (e.g. <c>"241fps.png"</c>), so that is what this returns, in the same
    /// ordinal-sorted order the asset source enumerates them.
    /// </summary>
    public static IReadOnlyList<string> ScannedKeys() =>
        Assets.Files("Assets/Textures", "*.png").Select(p => Path.GetFileName(p)!).ToArray();

    /// <summary>
    /// Every key the texture dictionary will hold after startup, in registration order (scanned files first,
    /// then the procedural entries) — the GPU-free mirror of what <see cref="Runtime.LoadTextures"/> builds.
    /// </summary>
    public static IReadOnlyList<string> RegisteredKeys()
    {
        var keys = new List<string>(ScannedKeys());
        keys.AddRange(ProceduralKeys);
        return keys;
    }
}
