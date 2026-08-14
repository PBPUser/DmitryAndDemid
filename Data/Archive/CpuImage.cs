using DmitryAndDemid.Rendering;
using DmitryAndDemid.Utils;
using StbImageSharp;

namespace DmitryAndDemid.Data.Archive;

/// <summary>

/// 
/// A decoded image's raw pixel data, kept on the CPU instead of uploaded to the GPU — for code that needs to
/// read pixels (sampling a color, inspecting a sprite sheet, etc.) rather than draw them. This is deliberately
/// separate from <see cref="TextureHandle"/>: a texture is a backend-owned GPU resource that must be freed
/// through <see cref="Gfx.UnloadTexture"/>, while a <see cref="CpuImage"/> is plain managed memory (a
/// <c>byte[]</c>) the GC already handles — there is no Unload here and none is needed.
///
/// There are two ways in. <see cref="LoadFromGenericFormat"/> reads the usual formats (PNG, …) through
/// StbImageSharp, the same decoder the Silk/Vulkan/Metal backends already use for their file-to-GPU upload path
/// (see e.g. <c>SilkGLBackend.LoadTexture</c>) — it just stops one step earlier, before the pixels leave the CPU.
/// <see cref="Load"/> and <see cref="Save"/> are the project's own <c>.negr</c> block format, which exists because
/// StbImageSharp only decodes: it is the write side, and it is built on the same <see cref="BitPackage"/> varints
/// as the game's other binary files rather than on a new encoder dependency. Its specification is
/// <c>Data/Archive/CpuImage.sp</c>, and the container itself is <see cref="ImageBlock"/>.
/// </summary>
public class CpuImage
{
    public readonly int Width;
    public readonly int Height;

    /// <summary>Tightly packed RGBA, top row first — 4 bytes per pixel, <c>Width * Height * 4</c> long.</summary>
    public readonly byte[] Pixels;

    /// <summary>Free-form key/value strings carried by the <c>.negr</c> format's METADATA blocks (see
    /// <see cref="Save"/>). Empty for an image decoded from PNG. Nothing in the game reads these — they exist so
    /// a tool can stamp provenance on a generated image without needing a format change to do it.</summary>
    public readonly Dictionary<string, string> Metadata = new();

    private CpuImage(int width, int height, byte[] pixels)
    {
        Width = width;
        Height = height;
        Pixels = pixels;
    }

    /// <summary>Wraps pixels the caller already has — a generated or edited image — so it can be
    /// <see cref="Save"/>d or <see cref="ToTexture"/>'d like a loaded one. <paramref name="pixels"/> is taken by
    /// reference, not copied.</summary>
    public static CpuImage FromPixels(int width, int height, byte[] pixels)
    {
        if (width < 0 || height < 0)
            throw new ArgumentOutOfRangeException(nameof(width), $"Negative image size {width}x{height}");
        if (pixels.Length != (long)width * height * 4)
            throw new ArgumentException(
                $"A {width}x{height} RGBA image is {(long)width * height * 4} bytes, got {pixels.Length}",
                nameof(pixels));
        return new CpuImage(width, height, pixels);
    }

    /// <summary>Reads and decodes an image file (PNG, etc.) through the <see cref="Assets"/> seam, so this works
    /// the same whether the path resolves to disk or a packaged/embedded asset source.
    ///
    /// Not used on Switch today: <c>Rendering/Switch/SdlGlBackend.cs</c> deliberately avoids StbImageSharp there
    /// (a managed decode is enough to OOM that platform's constrained interpreter) in favor of native SDL2_image.
    /// Calling this on Switch would reintroduce that same risk — fine for tooling/desktop use, but if this ever
    /// needs to run there too it should get a native-decode fallback rather than reuse StbImageSharp as-is.</summary>
    public static CpuImage LoadFromGenericFormat(string path)
    {
        using Stream stream = Assets.OpenRead(path);
        ImageResult image = ImageResult.FromStream(stream, ColorComponents.RedGreenBlueAlpha);
        return new CpuImage(image.Width, image.Height, image.Data);
    }

    /// <summary>ASCII, 5 bytes, at offset 0 — the only bytes in a <c>.negr</c> file outside a block. Present so a
    /// wrong file type is rejected on its first bytes instead of being decoded as a nonsense block.</summary>
    public const string Signature = "NEGR1";

    public const string Extension = ".negr";

    /// <summary>Reads the project's own <c>.negr</c> block format, the counterpart to <see cref="Save"/> and the
    /// one image format here that does not go through StbImageSharp. <c>Data/Archive/CpuImage.sp</c> is the
    /// specification.
    /// 
    /// The loop is the whole decoder: past the signature a file is nothing but blocks, so this reads one, hands
    /// it the image being built, and repeats. Which class each block turns into, what it does with the image,
    /// and what happens to a block this build has never heard of are all <see cref="ImageBlock"/>'s business —
    /// a null back from <see cref="ImageBlock.Read"/> is an unknown optional block that has already been stepped
    /// over, which is why a file written by a later version of the format still loads here.</summary>
    public static CpuImage Load(string path)
    {
        using BitPackage package = BitPackage.GetStreamReadPackage(Assets.OpenRead(path));

        string signature;
        try { signature = package.ReadFixedString(Signature.Length); }
        catch (EndOfStreamException) { throw new InvalidDataException($"{path} is too short to be a .negr file"); }
        if (signature != Signature)
            throw new InvalidDataException(
                $"{path} is not a .negr file: expected signature \"{Signature}\", got \"{signature}\"");

        CpuImageBuilder image = new(path);
        while (true)
        {
            ImageBlock? block = ImageBlock.Read(package);
            if (block is EndBlock)
                break;
            block?.Apply(image);
        }
        return image.Build();
    }

    public Rgba GetPixel(int x, int y)
    {
        int i = (y * Width + x) * 4;
        return new Rgba(Pixels[i], Pixels[i + 1], Pixels[i + 2], Pixels[i + 3]);
    }

    /// <summary>Encodes <see cref="Pixels"/> out to the project's own <c>.negr</c> block format, the write-side
    /// counterpart to <see cref="Load"/>. This deliberately does not write a PNG: StbImageSharp only decodes, so
    /// PNG output would mean taking on an encoder dependency, whereas the block format is a few dozen lines over
    /// the <see cref="BitPackage"/> varints the rest of the game's binary files already use.
    ///
    /// The pixels go out as 16x16 tiles, row-major, one block each — see <see cref="TileBlock"/>. Every tile
    /// uses the one encoding that exists so far, <see cref="RawColorTileBlock"/>: the colours themselves,
    /// uncompressed. Alpha is written only if the image actually uses it (<see cref="UsesAlpha"/>) — that is the
    /// manifest bit a reader needs before it can size a single tile payload, and an opaque image comes out a
    /// quarter smaller for it.</summary>
    public void Save(string path)
    {
        if(File.Exists(path))
            throw new IOException($"File {path} already exists");
        using BitPackage bitPackage = BitPackage.OpenStreamWritePackage(path);

        bool alphaEnabled = UsesAlpha();

        bitPackage.WriteFixedString(Signature);

        new ResolutionBlock(Width, Height).Write(bitPackage);
        new ManifestBlock(alphaEnabled).Write(bitPackage);

        foreach ((string key, string value) in Metadata)
            new MetadataBlock(key, value).Write(bitPackage);

        int columns = (Width + TileBlock.Size - 1) / TileBlock.Size;
        int rows = (Height + TileBlock.Size - 1) / TileBlock.Size;
        for (int tileY = 0; tileY < rows; tileY++)
            for (int tileX = 0; tileX < columns; tileX++)
                RawColorTileBlock.ForTile(this, tileX, tileY, alphaEnabled).Write(bitPackage);

        new EndBlock().Write(bitPackage);
    }

    /// <summary>
    /// Whether any pixel is less than fully opaque — i.e. whether the alpha channel carries information, and so
    /// whether tiles need to spend a fourth byte on it. Asks the pixels rather than trusting where they came
    /// from: StbImageSharp decodes to RGBA whatever the source had, so a 24-bit PNG arrives with an all-255
    /// alpha channel and is written without one.
    /// </summary>
    public bool UsesAlpha()
    {
        for (int i = 3; i < Pixels.Length; i += 4)
            if (Pixels[i] != 0xFF)
                return true;
        return false;
    }

    /// <summary>Uploads <see cref="Pixels"/> to the GPU and hands back a <see cref="TextureHandle"/>, so a
    /// CPU-side edited/generated image can be drawn like any other texture.
    ///
    /// The handle is backend-owned exactly like a <see cref="Gfx.LoadTexture"/> one — free it with
    /// <see cref="Gfx.UnloadTexture"/>. It is a snapshot, not a view: later writes to <see cref="Pixels"/> do
    /// not reach the GPU, call this again for those. Returns <see cref="TextureHandle.None"/> on a backend with
    /// no upload path (the Switch deko3d one, which cannot load textures at all yet).</summary>
    public TextureHandle ToTexture() => Gfx.LoadTextureFromPixels(Pixels, Width, Height);
}
