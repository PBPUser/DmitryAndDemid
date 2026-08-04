using DmitryAndDemid.Utils;
using StbImageSharp;

namespace DmitryAndDemid.Rendering;

/// <summary>
/// A decoded image's raw pixel data, kept on the CPU instead of uploaded to the GPU — for code that needs to
/// read pixels (sampling a color, inspecting a sprite sheet, etc.) rather than draw them. This is deliberately
/// separate from <see cref="TextureHandle"/>: a texture is a backend-owned GPU resource that must be freed
/// through <see cref="Gfx.UnloadTexture"/>, while a <see cref="CpuImage"/> is plain managed memory (a
/// <c>byte[]</c>) the GC already handles — there is no Unload here and none is needed.
///
/// Decoding goes through StbImageSharp, the same decoder the Silk/Vulkan/Metal backends already use for their
/// file-to-GPU upload path (see e.g. <c>SilkGLBackend.LoadTexture</c>) — this just stops one step earlier,
/// before the pixels leave the CPU.
/// </summary>
public class CpuImage
{
    public readonly int Width;
    public readonly int Height;

    /// <summary>Tightly packed RGBA, top row first — 4 bytes per pixel, <c>Width * Height * 4</c> long.</summary>
    public readonly byte[] Pixels;

    private CpuImage(int width, int height, byte[] pixels)
    {
        Width = width;
        Height = height;
        Pixels = pixels;
    }

    /// <summary>Reads and decodes an image file (PNG, etc.) through the <see cref="Assets"/> seam, so this works
    /// the same whether the path resolves to disk or a packaged/embedded asset source.
    ///
    /// Not used on Switch today: <c>Rendering/Switch/SdlGlBackend.cs</c> deliberately avoids StbImageSharp there
    /// (a managed decode is enough to OOM that platform's constrained interpreter) in favor of native SDL2_image.
    /// Calling this on Switch would reintroduce that same risk — fine for tooling/desktop use, but if this ever
    /// needs to run there too it should get a native-decode fallback rather than reuse StbImageSharp as-is.</summary>
    public static CpuImage Load(string path)
    {
        using Stream stream = Assets.OpenRead(path);
        ImageResult image = ImageResult.FromStream(stream, ColorComponents.RedGreenBlueAlpha);
        return new CpuImage(image.Width, image.Height, image.Data);
    }

    public Rgba GetPixel(int x, int y)
    {
        int i = (y * Width + x) * 4;
        return new Rgba(Pixels[i], Pixels[i + 1], Pixels[i + 2], Pixels[i + 3]);
    }

    /// <summary>Placeholder: encode <see cref="Pixels"/> back out to a PNG on disk, the write-side counterpart
    /// to <see cref="Load"/>. Not implemented yet — the project has no PNG encoder referenced (StbImageSharp only
    /// decodes); this needs something like StbImageWriteSharp added before it can do real work.</summary>
    public void Save(string path)
    {
        if(File.Exists(path))
            throw new IOException($"File {path} already exists");
        var bitPackage = BitPackage.OpenStreamWritePackage(path);
        
        throw new NotImplementedException();
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
