using DmitryAndDemid.Data.Archive;

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

    /// <summary>One art file the scan found, and the dictionary keys it claims.</summary>
    /// <param name="Path">Asset path, as <see cref="Assets"/> would resolve it.</param>
    /// <param name="Keys">Every key this file registers under, its own name first.</param>
    public readonly record struct ScannedTexture(string Path, IReadOnlyList<string> Keys);

    /// <summary>
    /// The art files in <c>Assets/Textures</c> and the keys each claims — the ONE definition of the scan rule,
    /// which <see cref="Runtime.LoadTextures"/> walks to build the live dictionary and the tests walk to count
    /// it. Both halves reading the same function is what stops them drifting.
    ///
    /// <para>PNGs come first, in the asset source's ordinal order, each under its filename with extension
    /// (<c>"241fps.png"</c>). Then the AKOB static illustrations (<c>.asi</c>, and the older <c>.akob</c>),
    /// each under its OWN filename and, additionally, under the <c>.png</c> name it corresponds to — so
    /// <c>dmitry_top.asi</c> answers to <c>Textures["dmitry_top.png"]</c> and art can move to the illustration
    /// format one file at a time without touching a single call site.</para>
    ///
    /// <para>The alias is only claimed when it is free. A real <c>dmitry_top.png</c> on disk keeps its own key
    /// and the illustration is then reachable only as <c>"dmitry_top.asi"</c> — two files that both exist are
    /// two textures, and neither is silently dropped. (Replacing art means deleting the PNG, which is the
    /// point: the key then falls to the illustration.) Keys are unique by construction either way, which is
    /// what <c>TextureRegistryTests</c> insists on.</para>
    /// </summary>
    public static IReadOnlyList<ScannedTexture> ScannedTextures()
    {
        var scanned = new List<ScannedTexture>();
        var claimed = new HashSet<string>(StringComparer.Ordinal);

        foreach (string path in Assets.Files("Assets/Textures", "*.png"))
        {
            string key = Path.GetFileName(path)!;
            claimed.Add(key);
            scanned.Add(new ScannedTexture(path, [key]));
        }

        foreach (string extension in StaticIllustrationImage.Extensions)
            foreach (string path in Assets.Files("Assets/Textures", "*" + extension))
            {
                string own = Path.GetFileName(path)!;
                var keys = new List<string>();
                if (claimed.Add(own))
                    keys.Add(own);
                string alias = Path.ChangeExtension(own, ".png");
                if (claimed.Add(alias))
                    keys.Add(alias);
                scanned.Add(new ScannedTexture(path, keys));
            }

        return scanned;
    }

    /// <summary>
    /// Every key the scan claims, flattened, in registration order. The dictionary keys the game uses are the
    /// filename <b>with</b> extension (e.g. <c>"241fps.png"</c>), plus the <c>.png</c> aliases an illustration
    /// stands in under — see <see cref="ScannedTextures"/> for the rule.
    /// </summary>
    public static IReadOnlyList<string> ScannedKeys() =>
        ScannedTextures().SelectMany(t => t.Keys).ToArray();

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
