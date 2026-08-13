using DmitryAndDemid.Data.Archive;
using DmitryAndDemid.Rendering;
using DmitryAndDemid.Utils;
using Xunit;

namespace DmitryAndDemid.Tests;

/// <summary>
/// The <c>.negr</c> block format — <see cref="CpuImage.Save"/> / <see cref="CpuImage.Load"/>, specified in
/// <c>Rendering/CpuImage.sp</c>. Pure byte work, no GPU and no repo assets: images are built in memory and
/// written to temp files.
///
/// Two things here are worth more than the round-trips. One is <see cref="Spec_ExampleFile_MatchesByteForByte"/>,
/// which pins the exact bytes of the example in the spec — that is what stops the spec and the encoder from
/// drifting apart silently. The other is the pair of unknown-block tests: skipping an optional block by its
/// declared length and refusing a required one is the entire reason the block header carries a type byte and a
/// length, so if those two stop holding the format has lost its point even though every round-trip still passes.
/// </summary>
public class CpuImageFormatTests
{
    /// <summary>A temp path that does not exist yet — <see cref="CpuImage.Save"/> refuses to overwrite, so this
    /// cannot use Path.GetTempFileName (which creates the file).</summary>
    private sealed class TempPath : IDisposable
    {
        public readonly string Path;

        public TempPath() =>
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"cim-{Guid.NewGuid():N}.negr");

        public void Dispose()
        {
            if (File.Exists(Path))
                File.Delete(Path);
        }
    }

    /// <summary>Deterministic noise — pixel-wise incompressible, so every strip goes out as PIXELS_RAW.</summary>
    private static CpuImage Noise(int width, int height)
    {
        byte[] pixels = new byte[width * height * 4];
        for (int i = 0; i < pixels.Length; i++)
            pixels[i] = (byte)(i * 31 + i / 7);
        return CpuImage.FromPixels(width, height, pixels);
    }

    /// <summary>One flat colour — every strip goes out as PIXELS_RLE.</summary>
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
    [InlineData(64, 64)]
    // 384x8: the playfield's width, and wide enough that a strip boundary lands inside the image.
    [InlineData(384, 8)]
    public void RoundTrip_Noise_IsExact(int width, int height)
    {
        CpuImage source = Noise(width, height);
        AssertSameImage(source, SaveAndLoad(source));
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(64, 64)]
    [InlineData(384, 448)]
    public void RoundTrip_FlatColour_IsExact(int width, int height)
    {
        CpuImage source = Flat(width, height, new Rgba(12, 34, 56, 200));
        AssertSameImage(source, SaveAndLoad(source));
    }

    /// <summary>Half flat, half noise, so one file contains both PIXELS_RLE and PIXELS_RAW blocks and the
    /// reader has to concatenate across the two types.</summary>
    [Fact]
    public void RoundTrip_MixedStrips_IsExact()
    {
        // 32 rows of 512px = 64 KiB, i.e. exactly one strip's worth per half.
        const int width = 512, height = 64;
        byte[] pixels = new byte[width * height * 4];
        int half = pixels.Length / 2;
        for (int i = half; i < pixels.Length; i++)
            pixels[i] = (byte)(i * 31 + i / 7);
        CpuImage source = CpuImage.FromPixels(width, height, pixels);
        AssertSameImage(source, SaveAndLoad(source));
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

    [Fact]
    public void FlatColour_CompressesWellBelowRaw()
    {
        using TempPath temp = new();
        CpuImage source = Flat(256, 256, Rgba.White);
        source.Save(temp.Path);
        // 256 KiB of one repeated pixel: 5 bytes per 129-pixel run, so well under a tenth of raw.
        Assert.True(new FileInfo(temp.Path).Length < source.Pixels.Length / 10,
            $"flat image took {new FileInfo(temp.Path).Length} bytes, raw is {source.Pixels.Length}");
    }

    /// <summary>The worked example at the bottom of <c>Rendering/CpuImage.sp</c>, byte for byte.</summary>
    [Fact]
    public void Spec_ExampleFile_MatchesByteForByte()
    {
        using TempPath temp = new();
        CpuImage.FromPixels(1, 1, [0xFF, 0x00, 0x00, 0xFF]).Save(temp.Path);
        Assert.Equal(new byte[]
        {
            0x43, 0x49, 0x4D, 0x31,             // "CIM1"
            0x81, 0x02, 0x01, 0x01,             // RESOLUTION: 1x1
            0x82, 0x04, 0xFF, 0x00, 0x00, 0xFF, // PIXELS_RAW: one red pixel
            0x80, 0x00,                         // END
        }, File.ReadAllBytes(temp.Path));
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

    /// <summary>The base class's framing on its own, without a file around it: what one block writes is what the
    /// next read gives back, as the right subclass, positioned on the block after it.</summary>
    [Fact]
    public void ImageBlock_WriteThenRead_RoundTripsThroughTheBaseClass()
    {
        BitPackage written = new();
        new MetadataBlock("author", "нет").Write(written);
        new ResolutionBlock(3, 5).Write(written);
        new PixelsRawBlock { Data = [1, 2, 3, 4] }.Write(written);
        new EndBlock().Write(written);

        using BitPackage read = BitPackage.OpenReadMemoryPackage(written.Export());
        MetadataBlock metadata = Assert.IsType<MetadataBlock>(ImageBlock.Read(read));
        Assert.Equal("author", metadata.Key);
        Assert.Equal("нет", metadata.Value);
        ResolutionBlock resolution = Assert.IsType<ResolutionBlock>(ImageBlock.Read(read));
        Assert.Equal(3, resolution.Width);
        Assert.Equal(5, resolution.Height);
        Assert.Equal<byte[]>([1, 2, 3, 4], Assert.IsType<PixelsRawBlock>(ImageBlock.Read(read)).Data);
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
    private static readonly byte PixelsRawType = TypeByteOf<PixelsRawBlock>();
    private static readonly byte EndType = TypeByteOf<EndBlock>();

    /// <summary>Pins the id table in the <see cref="ImageBlockAttribute"/>s against the one in
    /// <c>Rendering/CpuImage.sp</c>, including the required bit each type byte carries. Every other test here
    /// asks the attributes what the type bytes are, so this is the one that says what they must be.</summary>
    [Theory]
    [InlineData(typeof(EndBlock), 0x80, true)]
    [InlineData(typeof(ResolutionBlock), 0x81, true)]
    [InlineData(typeof(PixelsRawBlock), 0x82, true)]
    [InlineData(typeof(PixelsRleBlock), 0x83, true)]
    [InlineData(typeof(MetadataBlock), 0x04, false)]
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

    private static CpuImage LoadBytes(byte[] bytes)
    {
        using TempPath temp = new();
        File.WriteAllBytes(temp.Path, bytes);
        return CpuImage.Load(temp.Path);
    }

    /// <summary>An optional block this build has never heard of is stepped over using its declared length, and
    /// the image behind it still loads. This is the forward-compatibility promise the format makes.</summary>
    [Fact]
    public void UnknownOptionalBlock_IsSkipped()
    {
        byte[] pixel = [0x10, 0x20, 0x30, 0x40];
        CpuImage image = LoadBytes(BuildFile(
            (ResolutionType, ResolutionPayload(1, 1)),
            (0x2A, [1, 2, 3, 4, 5, 6, 7]),   // id 42, required bit clear
            (PixelsRawType, pixel),
            (0x7F, []),                      // id 127, required bit clear, empty
            (EndType, [])));
        Assert.Equal(1, image.Width);
        Assert.Equal(pixel, image.Pixels);
    }

    /// <summary>The other half of that promise: a block the file says is load-bearing and this build cannot
    /// read must stop the load, not be skipped into a half-decoded image.</summary>
    [Fact]
    public void UnknownRequiredBlock_Throws()
    {
        byte[] file = BuildFile(
            (ResolutionType, ResolutionPayload(1, 1)),
            (0xAA, [1, 2, 3]),               // id 42, required bit SET
            (PixelsRawType, [0x10, 0x20, 0x30, 0x40]),
            (EndType, []));
        InvalidDataException ex = Assert.Throws<InvalidDataException>(() => LoadBytes(file));
        Assert.Contains("0xAA", ex.Message);
    }

    [Fact]
    public void WrongSignature_Throws()
    {
        byte[] file = BuildFile(
            (ResolutionType, ResolutionPayload(1, 1)),
            (PixelsRawType, [0x10, 0x20, 0x30, 0x40]),
            (EndType, []));
        file[3] = (byte)'2';
        Assert.Throws<InvalidDataException>(() => LoadBytes(file));
    }

    [Fact]
    public void MissingEndBlock_Throws()
    {
        byte[] file = BuildFile(
            (ResolutionType, ResolutionPayload(1, 1)),
            (PixelsRawType, [0x10, 0x20, 0x30, 0x40]));
        Assert.Throws<InvalidDataException>(() => LoadBytes(file));
    }

    [Fact]
    public void TruncatedPayload_Throws()
    {
        byte[] file = BuildFile(
            (ResolutionType, ResolutionPayload(4, 4)),
            (PixelsRawType, new byte[64]),
            (EndType, []));
        Assert.Throws<InvalidDataException>(() => LoadBytes(file[..^20]));
    }

    [Fact]
    public void TooFewPixelBytes_Throws()
    {
        byte[] file = BuildFile(
            (ResolutionType, ResolutionPayload(4, 4)),
            (PixelsRawType, new byte[32]),   // half a 4x4 image
            (EndType, []));
        Assert.Throws<InvalidDataException>(() => LoadBytes(file));
    }

    [Fact]
    public void TooManyPixelBytes_Throws()
    {
        byte[] file = BuildFile(
            (ResolutionType, ResolutionPayload(4, 4)),
            (PixelsRawType, new byte[128]),  // twice a 4x4 image
            (EndType, []));
        Assert.Throws<InvalidDataException>(() => LoadBytes(file));
    }

    /// <summary>RESOLUTION is what sizes the buffer pixels go into, so pixels ahead of it cannot be placed and
    /// the file has to be refused rather than guessed at.</summary>
    [Fact]
    public void PixelsBeforeResolution_Throws()
    {
        byte[] file = BuildFile(
            (PixelsRawType, [0x10, 0x20, 0x30, 0x40]),
            (ResolutionType, ResolutionPayload(1, 1)),
            (EndType, []));
        Assert.Throws<InvalidDataException>(() => LoadBytes(file));
    }

    [Fact]
    public void TwoResolutionBlocks_Throw()
    {
        byte[] file = BuildFile(
            (ResolutionType, ResolutionPayload(1, 1)),
            (ResolutionType, ResolutionPayload(1, 1)),
            (PixelsRawType, [0x10, 0x20, 0x30, 0x40]),
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
        package.WriteByte(PixelsRawType);
        package.WriteVarULong(int.MaxValue);
        Assert.Throws<InvalidDataException>(() => LoadBytes(package.Export()));
    }

    /// <summary>Run lengths around the control byte's limits: 128/129 pixels are the largest literal and repeat
    /// runs, so 129/130 are where a run has to split into two.</summary>
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(127)]
    [InlineData(128)]
    [InlineData(129)]
    [InlineData(130)]
    [InlineData(400)]
    public void Rle_RunLengthBoundaries_RoundTrip(int pixelCount)
    {
        // A repeat run of `pixelCount`, then a literal run of `pixelCount`, in one buffer.
        byte[] pixels = new byte[pixelCount * 2 * 4];
        for (int p = 0; p < pixelCount; p++)
            pixels[p * 4] = 0x77;
        for (int p = pixelCount; p < pixelCount * 2; p++)
            for (int c = 0; c < 4; c++)
                pixels[p * 4 + c] = (byte)(p * 4 + c);

        byte[] coded = CpuImageRle.Encode(pixels);
        byte[] decoded = new byte[pixels.Length];
        Assert.Equal(pixels.Length, CpuImageRle.Decode(coded, decoded));
        Assert.Equal(pixels, decoded);
    }

    [Fact]
    public void Rle_DecodeIntoTooSmallBuffer_Throws()
    {
        byte[] coded = CpuImageRle.Encode(new byte[64]);
        Assert.Throws<InvalidDataException>(() => CpuImageRle.Decode(coded, new byte[32]));
    }

    [Fact]
    public void Rle_TruncatedRun_Throws()
    {
        byte[] coded = CpuImageRle.Encode(new byte[64]);
        Assert.Throws<InvalidDataException>(() => CpuImageRle.Decode(coded[..^2], new byte[64]));
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
