using DmitryAndDemid.Data.Archive;
using DmitryAndDemid.Rendering;
using DmitryAndDemid.Utils;
using Xunit;

namespace DmitryAndDemid.Tests;

/// <summary>
/// The <c>.negr</c> block format — <see cref="CpuImage.Save"/> / <see cref="CpuImage.Load"/>, specified in
/// <c>Data/Archive/CpuImage.sp</c>. Pure byte work, no GPU and no repo assets: images are built in memory and
/// written to temp files.
///
/// Three things here are worth more than the round-trips. <see cref="Spec_ExampleFile_MatchesByteForByte"/>
/// pins the exact bytes of the example in the spec, which is what stops the spec and the encoder from drifting
/// apart silently. The unknown-block pair — skipping an optional block by its declared length and refusing a
/// required one — is the entire reason the block header carries a type byte and a length. And the ordering
/// tests (tile before RESOLUTION, tile before MANIFEST) pin the one real constraint the tile grid imposes:
/// neither the number of tiles nor the size of one can be known without both header blocks first.
/// </summary>
public class CpuImageFormatTests
{
    /// <summary>A temp path that does not exist yet — <see cref="CpuImage.Save"/> refuses to overwrite, so this
    /// cannot use Path.GetTempFileName (which creates the file).</summary>
    private sealed class TempPath : IDisposable
    {
        public readonly string Path;

        public TempPath() =>
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"negr-{Guid.NewGuid():N}.negr");

        public void Dispose()
        {
            if (File.Exists(Path))
                File.Delete(Path);
        }
    }

    /// <summary>Deterministic noise: every pixel different from its neighbours, and opaque, so it exercises the
    /// no-alpha path on content that no future tile encoding will ever compress.</summary>
    private static CpuImage Noise(int width, int height)
    {
        byte[] pixels = new byte[width * height * 4];
        for (int i = 0; i < pixels.Length; i++)
            pixels[i] = i % 4 == 3 ? (byte)0xFF : (byte)(i * 31 + i / 7);
        return CpuImage.FromPixels(width, height, pixels);
    }

    /// <summary>One flat colour everywhere.</summary>
    private static CpuImage Flat(int width, int height, Rgba color)
    {
        byte[] pixels = new byte[width * height * 4];
        for (int i = 0; i < pixels.Length; i += 4)
        {
            pixels[i] = color.R;
            pixels[i + 1] = color.G;
            pixels[i + 2] = color.B;
            pixels[i + 3] = color.A;
        }
        return CpuImage.FromPixels(width, height, pixels);
    }

    private static void AssertSameImage(CpuImage expected, CpuImage actual)
    {
        Assert.Equal(expected.Width, actual.Width);
        Assert.Equal(expected.Height, actual.Height);
        Assert.Equal(expected.Pixels, actual.Pixels);
    }

    private static CpuImage SaveAndLoad(CpuImage image)
    {
        using TempPath temp = new();
        image.Save(temp.Path);
        return CpuImage.Load(temp.Path);
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(1, 37)]
    [InlineData(37, 1)]
    [InlineData(0, 0)]
    [InlineData(16, 16)]    // exactly one tile
    [InlineData(17, 17)]    // one tile plus a sliver, i.e. 2x2 tiles mostly outside the image
    [InlineData(64, 64)]
    [InlineData(384, 8)]    // the playfield's width; 24 tiles across, half a tile tall
    public void RoundTrip_Noise_IsExact(int width, int height)
    {
        CpuImage source = Noise(width, height);
        AssertSameImage(source, SaveAndLoad(source));
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(64, 64)]
    [InlineData(384, 448)]   // the playfield: 24x28 tiles, no overhang at all
    public void RoundTrip_FlatColour_IsExact(int width, int height)
    {
        CpuImage source = Flat(width, height, new Rgba(12, 34, 56, 200));
        AssertSameImage(source, SaveAndLoad(source));
    }

    /// <summary>A size that is not a multiple of 16 in either direction, so the right and bottom tiles hang off
    /// the image and the reader has to clip them. 37x21 is 3x2 tiles covering 48x32 — over a third of that area
    /// is padding, and none of it may come back as image.</summary>
    [Fact]
    public void RoundTrip_OverhangingEdgeTiles_IsExact()
    {
        CpuImage source = Noise(37, 21);
        AssertSameImage(source, SaveAndLoad(source));
    }

    /// <summary>
    /// Every pixel distinct across a multi-tile image, so a tile written or placed in the wrong order shows up
    /// as scrambled output rather than as an exception. Row-major tile order is not otherwise observable — the
    /// tiles carry no coordinates, so getting it wrong is silent.
    /// </summary>
    [Fact]
    public void TilesArePlacedInRowMajorOrder()
    {
        const int width = 48, height = 32;   // 3x2 tiles
        byte[] pixels = new byte[width * height * 4];
        for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                int i = (y * width + x) * 4;
                pixels[i] = (byte)x;
                pixels[i + 1] = (byte)y;
                pixels[i + 2] = (byte)(x * 7 + y * 13);
                pixels[i + 3] = 0xFF;
            }
        CpuImage source = CpuImage.FromPixels(width, height, pixels);
        CpuImage loaded = SaveAndLoad(source);
        AssertSameImage(source, loaded);
        // Spot-check a pixel from the last tile specifically: if tiles were placed column-major this would be
        // the one that moved.
        Assert.Equal(new Rgba(47, 31, 47 * 7 + 31 * 13 & 0xFF, 0xFF), loaded.GetPixel(47, 31));
    }

    [Fact]
    public void RoundTrip_Metadata_IsPreserved()
    {
        CpuImage source = Noise(4, 4);
        source.Metadata["tool"] = "StageEditorScreen";
        source.Metadata["note"] = "юникод and spaces";
        CpuImage loaded = SaveAndLoad(source);
        AssertSameImage(source, loaded);
        Assert.Equal("StageEditorScreen", loaded.Metadata["tool"]);
        Assert.Equal("юникод and spaces", loaded.Metadata["note"]);
    }

    /// <summary>
    /// The manifest's alpha bit is decided from the pixels, not declared by the caller: an image that is opaque
    /// everywhere is written 3 bytes per pixel, one that is not is written 4. Both round-trip, and the opaque
    /// one is measurably smaller — which is the entire point of the bit.
    /// </summary>
    [Fact]
    public void AlphaIsWrittenOnlyWhenTheImageUsesIt()
    {
        CpuImage opaque = Flat(64, 64, Rgba.White);
        CpuImage translucent = Flat(64, 64, Rgba.White with { A = 128 });
        Assert.False(opaque.UsesAlpha());
        Assert.True(translucent.UsesAlpha());

        using TempPath opaquePath = new();
        using TempPath translucentPath = new();
        opaque.Save(opaquePath.Path);
        translucent.Save(translucentPath.Path);

        long withoutAlpha = new FileInfo(opaquePath.Path).Length;
        long withAlpha = new FileInfo(translucentPath.Path).Length;
        // 16 tiles either way: 16*768 payload bytes against 16*1024, so exactly 4096 apart.
        Assert.Equal(4096, withAlpha - withoutAlpha);

        AssertSameImage(opaque, CpuImage.Load(opaquePath.Path));
        AssertSameImage(translucent, CpuImage.Load(translucentPath.Path));
    }

    /// <summary>One not-quite-opaque pixel anywhere turns the whole image's alpha on — there is one bit for the
    /// image and no way to say "alpha in this corner only".</summary>
    [Fact]
    public void OneTranslucentPixelEnablesAlphaForTheWholeImage()
    {
        CpuImage image = Flat(40, 40, Rgba.White);
        image.Pixels[(39 * 40 + 39) * 4 + 3] = 0xFE;   // the very last pixel, one step off opaque
        Assert.True(image.UsesAlpha());
        AssertSameImage(image, SaveAndLoad(image));
    }

    /// <summary>An image written without alpha decodes fully opaque, never with a zero alpha byte.</summary>
    [Fact]
    public void PixelsDecodeOpaqueWhenAlphaIsDisabled()
    {
        CpuImage loaded = SaveAndLoad(Flat(20, 20, new Rgba(1, 2, 3)));
        for (int i = 3; i < loaded.Pixels.Length; i += 4)
            Assert.Equal(0xFF, loaded.Pixels[i]);
    }

    /// <summary>The worked example at the bottom of <c>Data/Archive/CpuImage.sp</c>, byte for byte. A 1x1 image
    /// still costs a whole tile — the smallest thing the format can say is 16x16.</summary>
    [Fact]
    public void Spec_ExampleFile_MatchesByteForByte()
    {
        using TempPath temp = new();
        CpuImage.FromPixels(1, 1, [0xFF, 0x00, 0x00, 0xFF]).Save(temp.Path);
        byte[] actual = File.ReadAllBytes(temp.Path);

        byte[] expected = new byte[785];
        int at = 0;
        foreach (byte b in "NEGR1"u8) expected[at++] = b;
        expected[at++] = 0x81; expected[at++] = 0x02; expected[at++] = 0x01; expected[at++] = 0x01; // RESOLUTION 1x1
        expected[at++] = 0x82; expected[at++] = 0x01; expected[at++] = 0x00;                        // MANIFEST, no alpha
        expected[at++] = 0x90; expected[at++] = 0x86; expected[at++] = 0x00;                        // TILE_RAW8, 768 bytes
        expected[at++] = 0xFF; expected[at++] = 0x00; expected[at++] = 0x00;                        // the one red pixel
        at += 765;                                                                                  // 255 zero pixels
        expected[at++] = 0x80; expected[at++] = 0x00;                                               // END
        Assert.Equal(expected.Length, at);
        Assert.Equal(expected, actual);
    }

    /// <summary>The registry is built by reflection over the assembly, so a block class that is added wrong —
    /// no attribute, or an id another class already claims — fails when the first image is touched, wherever
    /// that happens to be. This turns that into a named failure right here.</summary>
    [Fact]
    public void EveryBlockClass_IsRegisteredWithADistinctTypeByte()
    {
        Type[] blockClasses = typeof(ImageBlock).Assembly.GetTypes()
            .Where(t => !t.IsAbstract && t.IsSubclassOf(typeof(ImageBlock)))
            .ToArray();
        Assert.NotEmpty(blockClasses);

        List<byte> typeBytes = new();
        foreach (Type blockClass in blockClasses)
        {
            // Parameterless ctor: ImageBlock.Read constructs blocks through Activator.
            ImageBlock block = Assert.IsAssignableFrom<ImageBlock>(Activator.CreateInstance(blockClass));
            typeBytes.Add(block.Descriptor.TypeByte);
        }
        Assert.Equal(typeBytes.Count, typeBytes.Distinct().Count());
    }

    /// <summary>Every tile encoding takes an id of 0x10 or above, which is what keeps the file's own structural
    /// blocks and the interchangeable ways of spelling a patch in separate ranges.</summary>
    [Fact]
    public void TileEncodingsTakeIdsFrom0x10Up()
    {
        Type[] tileClasses = typeof(ImageBlock).Assembly.GetTypes()
            .Where(t => !t.IsAbstract && t.IsSubclassOf(typeof(TileBlock)))
            .ToArray();
        Assert.NotEmpty(tileClasses);
        foreach (Type tileClass in tileClasses)
        {
            ImageBlockAttribute descriptor = ((ImageBlock)Activator.CreateInstance(tileClass)!).Descriptor;
            Assert.True(descriptor.Id >= 0x10, $"{tileClass.Name} is a tile encoding but claims id 0x{descriptor.Id:X2}");
        }
    }

    /// <summary>The base class's framing on its own, without a file around it: what one block writes is what the
    /// next read gives back, as the right subclass, positioned on the block after it.</summary>
    [Fact]
    public void ImageBlock_WriteThenRead_RoundTripsThroughTheBaseClass()
    {
        BitPackage written = new();
        new MetadataBlock("author", "нет").Write(written);
        new ResolutionBlock(3, 5).Write(written);
        new ManifestBlock(alphaEnabled: true).Write(written);
        new RawColorTileBlock { Data = new byte[RawColorTileBlock.PayloadLength(true)] }.Write(written);
        new EndBlock().Write(written);

        using BitPackage read = BitPackage.OpenReadMemoryPackage(written.Export());
        MetadataBlock metadata = Assert.IsType<MetadataBlock>(ImageBlock.Read(read));
        Assert.Equal("author", metadata.Key);
        Assert.Equal("нет", metadata.Value);
        ResolutionBlock resolution = Assert.IsType<ResolutionBlock>(ImageBlock.Read(read));
        Assert.Equal(3, resolution.Width);
        Assert.Equal(5, resolution.Height);
        Assert.True(Assert.IsType<ManifestBlock>(ImageBlock.Read(read)).AlphaEnabled);
        Assert.Equal(1024, Assert.IsType<RawColorTileBlock>(ImageBlock.Read(read)).Data.Length);
        Assert.IsType<EndBlock>(ImageBlock.Read(read));
    }

    /// <summary>Builds a .negr by hand, block header and all, so a test can put a block in a file that no
    /// <see cref="ImageBlock"/> subclass would ever write.</summary>
    private static byte[] BuildFile(params (byte Type, byte[] Payload)[] blocks)
    {
        BitPackage package = new();
        package.WriteFixedString(CpuImage.Signature);
        foreach ((byte type, byte[] payload) in blocks)
        {
            package.WriteByte(type);
            package.WriteVarULong((ulong)payload.Length);
            package.Write(payload);
        }
        return package.Export();
    }

    private static byte TypeByteOf<T>() where T : ImageBlock, new() => new T().Descriptor.TypeByte;

    private static readonly byte ResolutionType = TypeByteOf<ResolutionBlock>();
    private static readonly byte ManifestType = TypeByteOf<ManifestBlock>();
    private static readonly byte TileType = TypeByteOf<RawColorTileBlock>();
    private static readonly byte EndType = TypeByteOf<EndBlock>();

    /// <summary>Pins the id table in the <see cref="ImageBlockAttribute"/>s against the one in
    /// <c>Data/Archive/CpuImage.sp</c>, including the required bit each type byte carries. Every other test here
    /// asks the attributes what the type bytes are, so this is the one that says what they must be.</summary>
    [Theory]
    [InlineData(typeof(EndBlock), 0x80, true)]
    [InlineData(typeof(ResolutionBlock), 0x81, true)]
    [InlineData(typeof(ManifestBlock), 0x82, true)]
    [InlineData(typeof(MetadataBlock), 0x04, false)]
    [InlineData(typeof(RawColorTileBlock), 0x90, true)]   // id 0x10 — tile encodings start there
    public void BlockTypeBytes_MatchTheSpec(Type blockClass, byte typeByte, bool required)
    {
        ImageBlockAttribute descriptor = ((ImageBlock)Activator.CreateInstance(blockClass)!).Descriptor;
        Assert.Equal(typeByte, descriptor.TypeByte);
        Assert.Equal(required, descriptor.Required);
        Assert.Equal(typeByte & 0x7F, descriptor.Id);
    }

    private static byte[] ResolutionPayload(int width, int height)
    {
        BitPackage resolution = new();
        resolution.WriteVarULong((ulong)width);
        resolution.WriteVarULong((ulong)height);
        return resolution.Export();
    }

    private static byte[] ManifestPayload(bool alphaEnabled)
    {
        BitPackage manifest = new();
        manifest.WriteVarULong(alphaEnabled ? ManifestBlock.FlagAlphaEnabled : 0u);
        return manifest.Export();
    }

    /// <summary>A whole TILE_RAW8 payload whose first pixel is the given colour and whose other 255 are zero —
    /// the shape of every tile in a small hand-built image.</summary>
    private static byte[] TilePayload(bool alphaEnabled, byte r, byte g, byte b, byte a = 0xFF)
    {
        byte[] data = new byte[RawColorTileBlock.PayloadLength(alphaEnabled)];
        data[0] = r;
        data[1] = g;
        data[2] = b;
        if (alphaEnabled)
            data[3] = a;
        return data;
    }

    private static CpuImage LoadBytes(byte[] bytes)
    {
        using TempPath temp = new();
        File.WriteAllBytes(temp.Path, bytes);
        return CpuImage.Load(temp.Path);
    }

    /// <summary>The smallest legal file: both header blocks, one tile, END.</summary>
    private static byte[] OnePixelFile() => BuildFile(
        (ResolutionType, ResolutionPayload(1, 1)),
        (ManifestType, ManifestPayload(false)),
        (TileType, TilePayload(false, 0x10, 0x20, 0x30)),
        (EndType, []));

    /// <summary>An optional block this build has never heard of is stepped over using its declared length, and
    /// the image behind it still loads. This is the forward-compatibility promise the format makes.</summary>
    [Fact]
    public void UnknownOptionalBlock_IsSkipped()
    {
        CpuImage image = LoadBytes(BuildFile(
            (ResolutionType, ResolutionPayload(1, 1)),
            (0x2A, [1, 2, 3, 4, 5, 6, 7]),   // id 42, required bit clear
            (ManifestType, ManifestPayload(false)),
            (TileType, TilePayload(false, 0x10, 0x20, 0x30)),
            (0x7F, []),                      // id 127, required bit clear, empty
            (EndType, [])));
        Assert.Equal(1, image.Width);
        Assert.Equal<byte[]>([0x10, 0x20, 0x30, 0xFF], image.Pixels);
    }

    /// <summary>The other half of that promise: a block the file says is load-bearing and this build cannot
    /// read must stop the load, not be skipped into a half-decoded image.</summary>
    [Fact]
    public void UnknownRequiredBlock_Throws()
    {
        byte[] file = BuildFile(
            (ResolutionType, ResolutionPayload(1, 1)),
            (ManifestType, ManifestPayload(false)),
            (0xAA, [1, 2, 3]),               // id 42, required bit SET
            (TileType, TilePayload(false, 0x10, 0x20, 0x30)),
            (EndType, []));
        InvalidDataException ex = Assert.Throws<InvalidDataException>(() => LoadBytes(file));
        Assert.Contains("0xAA", ex.Message);
    }

    /// <summary>An unknown MANIFEST flag is deliberately NOT an error — a flag cannot change how anything
    /// already understood is read, so a reader that ignores one still decodes the file exactly right.</summary>
    [Fact]
    public void UnknownManifestFlag_IsIgnored()
    {
        BitPackage manifest = new();
        manifest.WriteVarULong(0x4000 | ManifestBlock.FlagAlphaEnabled);
        CpuImage image = LoadBytes(BuildFile(
            (ResolutionType, ResolutionPayload(1, 1)),
            (ManifestType, manifest.Export()),
            (TileType, TilePayload(true, 0x10, 0x20, 0x30, 0x44)),
            (EndType, [])));
        Assert.Equal<byte[]>([0x10, 0x20, 0x30, 0x44], image.Pixels);
    }

    [Fact]
    public void WrongSignature_Throws()
    {
        byte[] file = OnePixelFile();
        file[3] = (byte)'X';
        Assert.Throws<InvalidDataException>(() => LoadBytes(file));
    }

    [Fact]
    public void MissingEndBlock_Throws()
    {
        byte[] file = BuildFile(
            (ResolutionType, ResolutionPayload(1, 1)),
            (ManifestType, ManifestPayload(false)),
            (TileType, TilePayload(false, 0x10, 0x20, 0x30)));
        Assert.Throws<InvalidDataException>(() => LoadBytes(file));
    }

    [Fact]
    public void TruncatedPayload_Throws() =>
        Assert.Throws<InvalidDataException>(() => LoadBytes(OnePixelFile()[..^20]));

    /// <summary>
    /// A tile payload of the wrong length for the manifest's alpha bit. Both directions are corrupt: a tile is a
    /// fixed 768 or 1024 bytes and there is no third option, which is exactly what lets the length be checked
    /// against the manifest alone rather than against the tile's position in the grid.
    /// </summary>
    [Theory]
    [InlineData(false, 1024)]   // alpha off, but 4 bytes per pixel written
    [InlineData(true, 768)]     // alpha on, but only 3
    [InlineData(false, 767)]
    [InlineData(true, 0)]
    public void TileOfTheWrongLength_Throws(bool alphaEnabled, int payloadLength)
    {
        byte[] file = BuildFile(
            (ResolutionType, ResolutionPayload(1, 1)),
            (ManifestType, ManifestPayload(alphaEnabled)),
            (TileType, new byte[payloadLength]),
            (EndType, []));
        Assert.Throws<InvalidDataException>(() => LoadBytes(file));
    }

    /// <summary>A tile grid that does not cover the image, and one that overflows it. 40x40 is 3x3 = 9 tiles.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(8)]
    [InlineData(10)]
    public void WrongNumberOfTiles_Throws(int tileCount)
    {
        var blocks = new List<(byte, byte[])>
        {
            (ResolutionType, ResolutionPayload(40, 40)),
            (ManifestType, ManifestPayload(false)),
        };
        for (int i = 0; i < tileCount; i++)
            blocks.Add((TileType, TilePayload(false, 0x10, 0x20, 0x30)));
        blocks.Add((EndType, []));
        Assert.Throws<InvalidDataException>(() => LoadBytes(BuildFile(blocks.ToArray())));
    }

    /// <summary>RESOLUTION is what says how many tiles there should be, so a tile ahead of it cannot be placed
    /// and the file has to be refused rather than guessed at.</summary>
    [Fact]
    public void TileBeforeResolution_Throws()
    {
        byte[] file = BuildFile(
            (ManifestType, ManifestPayload(false)),
            (TileType, TilePayload(false, 0x10, 0x20, 0x30)),
            (ResolutionType, ResolutionPayload(1, 1)),
            (EndType, []));
        Assert.Throws<InvalidDataException>(() => LoadBytes(file));
    }

    /// <summary>And MANIFEST is what says how long a tile is, so a tile ahead of THAT cannot even be measured.</summary>
    [Fact]
    public void TileBeforeManifest_Throws()
    {
        byte[] file = BuildFile(
            (ResolutionType, ResolutionPayload(1, 1)),
            (TileType, TilePayload(false, 0x10, 0x20, 0x30)),
            (ManifestType, ManifestPayload(false)),
            (EndType, []));
        Assert.Throws<InvalidDataException>(() => LoadBytes(file));
    }

    /// <summary>A zero-sized image has no tiles at all, so this file is complete except for the manifest —
    /// which is still required, because "no flags" and "no manifest" must not be the same thing.</summary>
    [Fact]
    public void NoManifestBlock_Throws()
    {
        byte[] file = BuildFile(
            (ResolutionType, ResolutionPayload(0, 0)),
            (EndType, []));
        Assert.Throws<InvalidDataException>(() => LoadBytes(file));
    }

    [Theory]
    [InlineData(true)]    // two RESOLUTION blocks
    [InlineData(false)]   // two MANIFEST blocks
    public void DuplicateHeaderBlocks_Throw(bool duplicateResolution)
    {
        byte[] file = duplicateResolution
            ? BuildFile(
                (ResolutionType, ResolutionPayload(1, 1)),
                (ResolutionType, ResolutionPayload(1, 1)),
                (ManifestType, ManifestPayload(false)),
                (TileType, TilePayload(false, 0x10, 0x20, 0x30)),
                (EndType, []))
            : BuildFile(
                (ResolutionType, ResolutionPayload(1, 1)),
                (ManifestType, ManifestPayload(false)),
                (ManifestType, ManifestPayload(false)),
                (TileType, TilePayload(false, 0x10, 0x20, 0x30)),
                (EndType, []));
        Assert.Throws<InvalidDataException>(() => LoadBytes(file));
    }

    [Fact]
    public void NoResolutionBlock_Throws() =>
        Assert.Throws<InvalidDataException>(() => LoadBytes(BuildFile((EndType, []))));

    /// <summary>A RESOLUTION payload holding one varint instead of two. The block is intact as far as the
    /// framing is concerned — the length matches the bytes there — so only the block itself can catch it.</summary>
    [Fact]
    public void ResolutionWithOneDimension_Throws()
    {
        BitPackage half = new();
        half.WriteVarULong(4);
        byte[] file = BuildFile((ResolutionType, half.Export()), (EndType, []));
        Assert.Throws<InvalidDataException>(() => LoadBytes(file));
    }

    /// <summary>A dimension is a varint, which has no upper bound; an image dimension is an int, which does.</summary>
    [Fact]
    public void ResolutionBeyondInt32_Throws()
    {
        BitPackage huge = new();
        huge.WriteVarULong((ulong)int.MaxValue + 1);
        huge.WriteVarULong(1);
        byte[] file = BuildFile((ResolutionType, huge.Export()), (EndType, []));
        Assert.Throws<InvalidDataException>(() => LoadBytes(file));
    }

    /// <summary>Width and Height each fit an int, but the pixel buffer they ask for does not.</summary>
    [Fact]
    public void ResolutionWhosePixelsCannotFitInMemory_Throws()
    {
        byte[] file = BuildFile(
            (ResolutionType, ResolutionPayload(0x10000, 0x10000)),   // 65536² × 4 bytes = 16 TiB
            (EndType, []));
        Assert.Throws<InvalidDataException>(() => LoadBytes(file));
    }

    /// <summary>Resolutions large enough to need multi-byte varints, and the lopsided ones, survive the trip
    /// through the block on their own — no pixels involved.</summary>
    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 1)]
    [InlineData(127, 128)]
    [InlineData(384, 448)]
    [InlineData(16384, 3)]
    [InlineData(int.MaxValue, 0)]
    public void ResolutionBlock_RoundTripsAnyDimensions(int width, int height)
    {
        BitPackage written = new();
        new ResolutionBlock(width, height).Write(written);

        using BitPackage read = BitPackage.OpenReadMemoryPackage(written.Export());
        ResolutionBlock block = Assert.IsType<ResolutionBlock>(ImageBlock.Read(read));
        Assert.Equal(width, block.Width);
        Assert.Equal(height, block.Height);
    }

    /// <summary>A block header that claims an absurd payload must be rejected on the claim, before anything
    /// tries to allocate it.</summary>
    [Fact]
    public void AbsurdBlockLength_ThrowsWithoutAllocating()
    {
        BitPackage package = new();
        package.WriteFixedString(CpuImage.Signature);
        package.WriteByte(TileType);
        package.WriteVarULong(int.MaxValue);
        Assert.Throws<InvalidDataException>(() => LoadBytes(package.Export()));
    }

    [Fact]
    public void FromPixels_RejectsAMismatchedBufferLength()
    {
        Assert.Throws<ArgumentException>(() => CpuImage.FromPixels(4, 4, new byte[60]));
        Assert.Throws<ArgumentOutOfRangeException>(() => CpuImage.FromPixels(-1, 4, []));
    }

    [Fact]
    public void Save_RefusesToOverwrite()
    {
        using TempPath temp = new();
        CpuImage image = Noise(2, 2);
        image.Save(temp.Path);
        Assert.Throws<IOException>(() => image.Save(temp.Path));
    }
}
