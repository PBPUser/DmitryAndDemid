using DmitryAndDemid.Data.Archive;
using DmitryAndDemid.Utils;

namespace DmitryAndDemid.Rendering;

// Every block type the .negr format defines, one class each, specified in Rendering/CpuImage.sp. The ids in the
// attributes below and the ids in that file are the same list, and it is the file to change first. Adding a
// block here is all it takes to make it readable — see ImageBlockAttribute.

/// <summary>Ends the block stream. Carries nothing; bytes after it are not read.</summary>
[ImageBlock(0x00)]
public sealed class EndBlock : ImageBlock
{
    protected override void ReadPayload(BitPackage payload, int length)
    {
        if (length != 0)
            throw new InvalidDataException($"An END block must be empty, this one declares {length} bytes");
    }

    protected override void WritePayload(BitPackage payload) { }

    public override void Apply(CpuImageBuilder image) { }
}

/// <summary>
/// The image's resolution, and nothing else: two varints, width then height. Exactly one per file, and it must
/// come before any <see cref="PixelsBlock"/> — it is what sizes the buffer those append into, so a file that
/// puts pixels first cannot be read at all.
///
/// Deliberately only the resolution. Pixels are RGBA8888 by definition of the format (see
/// <see cref="CpuImage.Pixels"/>) rather than by a field here, so there is no format byte to get out of step
/// with what the encoder actually writes. A second pixel layout, if one is ever wanted, arrives as its own new
/// required block — which is precisely what <see cref="ImageBlockAttribute.Required"/> exists to express, and it
/// costs a file nothing until it is used.
/// </summary>
[ImageBlock(0x01)]
public sealed class ResolutionBlock : ImageBlock
{
    public int Width;
    public int Height;

    public ResolutionBlock() { }

    public ResolutionBlock(int width, int height)
    {
        Width = width;
        Height = height;
    }

    protected override void ReadPayload(BitPackage payload, int length)
    {
        Width = ReadDimension(payload, "width");
        Height = ReadDimension(payload, "height");
    }

    protected override void WritePayload(BitPackage payload)
    {
        payload.WriteVarULong((ulong)Width);
        payload.WriteVarULong((ulong)Height);
    }

    public override void Apply(CpuImageBuilder image) => image.Allocate(Width, Height);

    /// <summary>A varint is unbounded; a dimension is an int. A file claiming more is corrupt, and has to be
    /// caught here rather than by the cast, which would quietly wrap it into something plausible.</summary>
    private static int ReadDimension(BitPackage payload, string name)
    {
        ulong value = payload.ReadVarULong();
        if (value > int.MaxValue)
            throw new InvalidDataException($"A RESOLUTION block declares an image {name} of {value}");
        return (int)value;
    }
}

/// <summary>One key/value pair of free-form text. Optional — a reader that does not care about metadata skips
/// these — and there may be any number of them, anywhere in the file.</summary>
[ImageBlock(0x04, Required = false)]
public sealed class MetadataBlock : ImageBlock
{
    public string Key = "";
    public string Value = "";

    public MetadataBlock() { }

    public MetadataBlock(string key, string value)
    {
        Key = key;
        Value = value;
    }

    protected override void ReadPayload(BitPackage payload, int length)
    {
        Key = payload.ReadString();
        Value = payload.ReadString();
    }

    protected override void WritePayload(BitPackage payload)
    {
        payload.WriteString(Key);
        payload.WriteString(Value);
    }

    public override void Apply(CpuImageBuilder image) => image.Metadata[Key] = Value;
}

/// <summary>
/// What the two pixel-carrying blocks share: a payload that is nothing but bytes, and a decoded form that is
/// appended to the image in file order. Abstract, so it claims no id of its own. Both decode to RGBA8888 — the
/// format's one pixel layout, see <see cref="ResolutionBlock"/>.
/// </summary>
public abstract class PixelsBlock : ImageBlock
{
    /// <summary>The payload as it sits on disk — pixels for <see cref="PixelsRawBlock"/>, coded runs for
    /// <see cref="PixelsRleBlock"/>.</summary>
    public byte[] Data = [];

    /// <summary>The cheaper of the two encodings for one horizontal strip of an image. Compression is decided
    /// per strip and per file, never per format: a reader takes whichever it is given.</summary>
    public static PixelsBlock ForStrip(ReadOnlySpan<byte> strip)
    {
        byte[] coded = CpuImageRle.Encode(strip);
        return coded.Length < strip.Length
            ? new PixelsRleBlock { Data = coded }
            : new PixelsRawBlock { Data = strip.ToArray() };
    }

    protected override void ReadPayload(BitPackage payload, int length) =>
        Data = length == 0 ? [] : payload.Read(length);

    protected override void WritePayload(BitPackage payload) => payload.Write(Data);
}

/// <summary>Pixel bytes, verbatim.</summary>
[ImageBlock(0x02)]
public sealed class PixelsRawBlock : PixelsBlock
{
    public override void Apply(CpuImageBuilder image) => image.AppendPixels(Data);
}

/// <summary>Pixel bytes, run-length coded — see <see cref="CpuImageRle"/>.</summary>
[ImageBlock(0x03)]
public sealed class PixelsRleBlock : PixelsBlock
{
    public override void Apply(CpuImageBuilder image) => image.AppendCodedPixels(Data);
}

/// <summary>
/// The run-length coding a <see cref="PixelsRleBlock"/> carries. Runs are counted in whole 4-byte pixels, never
/// in bytes, so a run can never split a pixel — which is what makes a flat-colour bullet sprite (long stretches
/// of one RGBA value, and long stretches of transparent black) cheap without any per-channel bookkeeping.
///
/// Control byte: high bit clear is a literal run of <c>(c &amp; 0x7F) + 1</c> pixels that follow uncoded, high
/// bit set is <c>(c &amp; 0x7F) + 2</c> copies of the single pixel that follows. See <c>Rendering/CpuImage.sp</c>.
/// </summary>
public static class CpuImageRle
{
    public const int MaxLiteral = 128;
    public const int MaxRepeat = 129;

    /// <summary>Codes a whole number of pixels. <see cref="PixelsBlock.ForStrip"/> compares the result's length
    /// against the raw input's, so this is free to come out longer than what it was given — it does, on noisy
    /// data, by one control byte per 128 pixels.</summary>
    public static byte[] Encode(ReadOnlySpan<byte> pixels)
    {
        int count = pixels.Length / 4;
        List<byte> output = new(pixels.Length / 2 + 8);
        int i = 0;
        while (i < count)
        {
            int repeat = 1;
            while (repeat < MaxRepeat && i + repeat < count && SamePixel(pixels, i, i + repeat))
                repeat++;
            if (repeat >= 2)
            {
                output.Add((byte)(0x80 | (repeat - 2)));
                AppendPixels(output, pixels, i, 1);
                i += repeat;
                continue;
            }
            // Not a run, so gather literals until one starts. The first pixel always goes in (we only got here
            // because it differs from its neighbour), so a literal run is never empty and the count never
            // underflows its control byte.
            int start = i;
            int literal = 0;
            while (i < count && literal < MaxLiteral && !(i + 1 < count && SamePixel(pixels, i, i + 1)))
            {
                i++;
                literal++;
            }
            output.Add((byte)(literal - 1));
            AppendPixels(output, pixels, start, literal);
        }
        return output.ToArray();
    }

    /// <summary>Decodes into <paramref name="destination"/> and returns how many bytes it wrote. Every read and
    /// every write is bounds-checked against the payload and the destination respectively: this runs on file
    /// bytes, which may be corrupt, and a truncated run must throw rather than walk off either end.</summary>
    public static int Decode(ReadOnlySpan<byte> payload, Span<byte> destination)
    {
        int read = 0, written = 0;
        while (read < payload.Length)
        {
            byte control = payload[read++];
            if ((control & 0x80) == 0)
            {
                int bytes = ((control & 0x7F) + 1) * 4;
                if (read + bytes > payload.Length)
                    throw new InvalidDataException("Truncated RLE literal run");
                if (written + bytes > destination.Length)
                    throw new InvalidDataException("An RLE block decodes to more pixels than the image holds");
                payload.Slice(read, bytes).CopyTo(destination.Slice(written));
                read += bytes;
                written += bytes;
            }
            else
            {
                int repeat = (control & 0x7F) + 2;
                if (read + 4 > payload.Length)
                    throw new InvalidDataException("Truncated RLE repeat run");
                if (written + repeat * 4 > destination.Length)
                    throw new InvalidDataException("An RLE block decodes to more pixels than the image holds");
                ReadOnlySpan<byte> pixel = payload.Slice(read, 4);
                for (int k = 0; k < repeat; k++)
                    pixel.CopyTo(destination.Slice(written + k * 4));
                read += 4;
                written += repeat * 4;
            }
        }
        return written;
    }

    private static bool SamePixel(ReadOnlySpan<byte> pixels, int a, int b) =>
        pixels.Slice(a * 4, 4).SequenceEqual(pixels.Slice(b * 4, 4));

    private static void AppendPixels(List<byte> output, ReadOnlySpan<byte> pixels, int pixelIndex, int count)
    {
        int from = pixelIndex * 4, to = from + count * 4;
        for (int i = from; i < to; i++)
            output.Add(pixels[i]);
    }
}
