using DmitryAndDemid.Utils;
using NEngineFormat.StaticIllustration;

namespace DmitryAndDemid.Data.Archive;

/// <summary>
/// The AKOB static illustration (<c>.asi</c>) as something the game can draw: read the file, decode it, hand
/// back a <see cref="CpuImage"/>. Everything below this is <c>Rendering/NEngineFormat/</c>, the format library
/// vendored from the NEngineFormats repo — this file is the whole of the seam between it and the engine, and
/// it is deliberately one function wide.
///
/// It is the THIRD way into a <see cref="CpuImage"/>, alongside StbImageSharp for PNG and the project's own
/// <c>.negr</c>. Which of the three a path takes is <see cref="CpuImage.LoadAnyFormat"/>'s business; PNG is
/// untouched by any of this and stays on the backend's own loader wherever it already was.
///
/// An illustration is a layered, tiled, palette-and-mask format with an LZMA2 block region; none of that
/// reaches the game, which only ever wants the flattened picture. If a file turns out to need layers here, the
/// library has <c>DecodeLayers</c> and this is where it would be exposed.
/// </summary>
public static class StaticIllustrationImage
{
    /// <summary>The extension the format uses now.</summary>
    public const string Extension = ".asi";

    /// <summary>What it was called before <see cref="Extension"/>. Files in the wild still carry it, and the
    /// format library reads both, so the game does too rather than making anyone rename art.</summary>
    public const string FormerExtension = ".akob";

    /// <summary>Every extension that is a static illustration, in scan order.</summary>
    public static readonly string[] Extensions = [Extension, FormerExtension];

    /// <summary>Is this path one of <see cref="Extensions"/>? Case-insensitive, like the rest of asset lookup.
    /// </summary>
    public static bool IsIllustrationPath(string path) =>
        Extensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Decodes an illustration to pixels. Read through the <see cref="Assets"/> seam, so this works the same
    /// against the filesystem, an Android APK or a Switch romfs.
    /// </summary>
    /// <exception cref="InvalidDataException">
    /// The file is not an illustration, is malformed, or is compressed on a host with no liblzma — the last of
    /// which is the one failure a caller can do something about (write the file uncompressed), so it says so.
    /// </exception>
    public static CpuImage Load(string path)
    {
        StaticIllustrationAsset asset;
        try
        {
            using Stream stream = Assets.OpenRead(path);
            asset = StaticIllustrationAsset.Load(stream);
        }
        catch (InvalidOperationException e) when (IsMissingNativeCompressor(e))
        {
            throw new InvalidDataException(
                $"{path} is an LZMA2-compressed illustration and liblzma is not available on this host. " +
                "Write it with CompressBlocks off, or ship the native library beside the game.", e);
        }
        catch (InvalidOperationException e)
        {
            // The format library reports a structurally inconsistent file this way; to a caller loading an
            // asset that is the same kind of problem as a corrupt one, so it arrives as the same exception.
            throw new InvalidDataException($"{path} is not a usable static illustration: {e.Message}", e);
        }

        int width = (int)asset.Header.ImageWidth;
        int height = (int)asset.Header.ImageHeight;
        return CpuImage.FromPixels(width, height, asset.DecodeRgba());
    }

    /// <summary>
    /// Tells "this host has no liblzma" apart from every other way the block region can fail. The native
    /// loader's own message is all there is to go on — it throws the same exception type as a malformed file —
    /// so this matches on it and errs towards the generic message when it does not.
    /// </summary>
    private static bool IsMissingNativeCompressor(Exception e)
    {
        for (Exception? current = e; current != null; current = current.InnerException)
            if (current.Message.Contains("liblzma", StringComparison.OrdinalIgnoreCase)
                || current is DllNotFoundException)
                return true;
        return false;
    }
}
