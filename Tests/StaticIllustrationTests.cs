using DmitryAndDemid.Data.Archive;
using DmitryAndDemid.Utils;
using NEngineFormat.StaticIllustration;
using Xunit;

namespace DmitryAndDemid.Tests;

/// <summary>
/// The AKOB static illustration (<c>.asi</c>) as the game sees it: decoded to the same pixels it was encoded
/// from, and scanned into the texture registry alongside PNG without either format standing on the other.
///
/// <para>All headless. The format library (<c>Rendering/NEngineFormat/</c>) is pure managed code and
/// <see cref="CpuImage"/> stops before the GPU, so the only thing these cannot check is the upload itself.</para>
/// </summary>
public class StaticIllustrationTests
{
    public StaticIllustrationTests() => TestEnvironment.UseRepoAssets();

    /// <summary>
    /// A throwaway <c>Assets/Textures</c> to scan, with the asset seam pointed at it for the life of the
    /// fixture. The suite runs single-threaded (see <c>Tests/Parallelism.cs</c>), which is what makes moving a
    /// global like <see cref="Assets.Source"/> safe here.
    /// </summary>
    private sealed class TempAssets : IDisposable
    {
        public readonly string Root = Path.Combine(Path.GetTempPath(), "dnd-asi-" + Guid.NewGuid().ToString("N"));

        public TempAssets()
        {
            Directory.CreateDirectory(Path.Combine(Root, "Assets", "Textures"));
            Assets.Source = new FileSystemAssetSource(Root);
        }

        public string Textures => Path.Combine(Root, "Assets", "Textures");

        public void Dispose()
        {
            TestEnvironment.UseRepoAssets();
            try { Directory.Delete(Root, true); } catch (IOException) { /* a locked temp file is not a failure */ }
        }
    }

    /// <summary>A small picture with enough going on that the encoder has real work to do: a colour gradient,
    /// a flat region it can palette, and a varying alpha channel.</summary>
    private static (byte[] Rgba, int Width, int Height) SamplePicture(int width = 70, int height = 50)
    {
        var rgba = new byte[width * height * 4];
        for (int y = 0; y < height; y++)
        for (int x = 0; x < width; x++)
        {
            int i = (y * width + x) * 4;
            bool flat = x > width / 2 && y > height / 2;
            rgba[i + 0] = flat ? (byte)0x20 : (byte)(x * 255 / width);
            rgba[i + 1] = flat ? (byte)0x80 : (byte)(y * 255 / height);
            rgba[i + 2] = flat ? (byte)0xC0 : (byte)((x ^ y) & 0xFF);
            rgba[i + 3] = (byte)(128 + ((x + y) % 128));
        }
        return (rgba, width, height);
    }

    private static string WriteIllustration(string path, byte[] rgba, int width, int height, bool compress)
    {
        StaticIllustrationAsset asset = IllustrationEncoder.Encode(rgba, (uint)width, (uint)height);
        asset.CompressBlocks = compress;
        asset.SaveFile(path);
        return path;
    }

    /// <summary>
    /// Whether this host can do LZMA2 at all. The block region is optional compression over a native liblzma,
    /// and the test project pins <c>RuntimeIdentifier=linux-x64</c> — so on a Windows test host the Windows
    /// native is simply not in the output, while the GAME's build carries every platform's copy. Probing beats
    /// guessing: the answer differs between the test host and the thing being tested.
    /// </summary>
    private static bool NativeCompressorAvailable()
    {
        try
        {
            NEngineFormat.Core.Utils.Lzma2.Compress([0, 1, 2, 3]);
            return true;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    /// <summary>
    /// The whole point of the integration: what the encoder was given is what the game's loader hands back.
    /// Uncompressed, which every host can read — and which is what an <c>.asi</c> destined for a platform
    /// without the native library has to be written as.
    /// </summary>
    [Fact]
    public void Illustration_decodes_to_the_pixels_it_was_encoded_from()
    {
        using var temp = new TempAssets();
        (byte[] rgba, int width, int height) = SamplePicture();
        WriteIllustration(Path.Combine(temp.Textures, "sample.asi"), rgba, width, height, compress: false);

        CpuImage image = CpuImage.LoadAnyFormat("Assets/Textures/sample.asi");

        Assert.Equal(width, image.Width);
        Assert.Equal(height, image.Height);
        Assert.Equal(rgba, image.Pixels);
    }

    /// <summary>
    /// The same round trip through the LZMA2 block region, which is the form the encoder actually produces
    /// (compression is kept only when it wins, and on real art it does). Passes vacuously where the host has
    /// no liblzma — it cannot write such a file either, so there is nothing to decode; see
    /// <see cref="NativeCompressorAvailable"/> for why that is the test host and not the game.
    /// </summary>
    [Fact]
    public void Compressed_illustration_decodes_to_the_pixels_it_was_encoded_from()
    {
        if (!NativeCompressorAvailable())
            return;

        using var temp = new TempAssets();
        (byte[] rgba, int width, int height) = SamplePicture();
        WriteIllustration(Path.Combine(temp.Textures, "packed.asi"), rgba, width, height, compress: true);

        CpuImage image = CpuImage.LoadAnyFormat("Assets/Textures/packed.asi");

        Assert.Equal(rgba, image.Pixels);
    }

    /// <summary>
    /// <see cref="CpuImage.LoadAnyFormat"/> picks the decoder off the extension, and an illustration must not
    /// reach the PNG decoder (which would reject it) or vice versa. PNG is checked against the repo's own art
    /// so the case that matters — the format the game already ships — is the one under test.
    /// </summary>
    [Fact]
    public void Png_still_loads_through_the_same_entry_point()
    {
        CpuImage png = CpuImage.LoadAnyFormat("Assets/Textures/241fps.png");

        Assert.True(png.Width > 0 && png.Height > 0);
        Assert.Equal(png.Width * png.Height * 4, png.Pixels.Length);
    }

    /// <summary>Both extensions the format has carried are scanned; art does not have to be renamed.</summary>
    [Theory]
    [InlineData(".asi")]
    [InlineData(".akob")]
    public void Illustration_claims_its_own_name_and_the_png_alias(string extension)
    {
        using var temp = new TempAssets();
        (byte[] rgba, int width, int height) = SamplePicture();
        WriteIllustration(Path.Combine(temp.Textures, "boss_portrait" + extension), rgba, width, height,
            compress: false);

        var keys = TextureManifest.ScannedKeys();

        Assert.Contains("boss_portrait" + extension, keys);
        // The alias is the compatibility promise: Textures["boss_portrait.png"] keeps resolving after the art
        // moves to the illustration format, so no call site has to change.
        Assert.Contains("boss_portrait.png", keys);
    }

    /// <summary>
    /// A PNG that is really there keeps its own key. An illustration of the same name is still scanned — under
    /// its own name — so neither file is dropped and the registry has no duplicate key to collapse. Replacing
    /// the art means deleting the PNG, at which point the alias falls to the illustration.
    /// </summary>
    [Fact]
    public void A_real_png_is_never_shadowed_by_an_illustration_of_the_same_name()
    {
        using var temp = new TempAssets();
        (byte[] rgba, int width, int height) = SamplePicture();
        File.Copy(Path.Combine(TestEnvironment.RepoRoot, "Assets", "Textures", "241fps.png"),
            Path.Combine(temp.Textures, "shared.png"));
        WriteIllustration(Path.Combine(temp.Textures, "shared.asi"), rgba, width, height, compress: false);

        var scanned = TextureManifest.ScannedTextures();
        var keys = TextureManifest.ScannedKeys();

        Assert.Equal(keys.Count, keys.Distinct().Count());
        Assert.Equal(["shared.png"], scanned.Single(t => t.Path.EndsWith(".png")).Keys);
        Assert.Equal(["shared.asi"], scanned.Single(t => t.Path.EndsWith(".asi")).Keys);
    }

    /// <summary>
    /// Nothing in the repo's own art scan changes: the game ships PNGs today, and every one of them still
    /// claims exactly its own filename. Guards the alias rule against ever reaching back over plain art.
    /// </summary>
    [Fact]
    public void Shipped_png_art_claims_exactly_its_own_filename()
    {
        foreach (TextureManifest.ScannedTexture art in TextureManifest.ScannedTextures())
        {
            Assert.Single(art.Keys);
            Assert.Equal(Path.GetFileName(art.Path), art.Keys[0]);
        }
    }
}
