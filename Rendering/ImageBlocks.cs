using DmitryAndDemid.Data.Archive;
using DmitryAndDemid.Utils;

namespace DmitryAndDemid.Rendering;

// Every block type the .negr format defines, one class each, specified in Data/Archive/CpuImage.sp. The ids in the
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

/// <summary>
/// The manifest: what is true of the image as a whole rather than of any one tile. One varint of flags, and
/// today exactly one flag in it — <see cref="FlagAlphaEnabled"/>.
///
/// It has to be read before any <see cref="TileBlock"/> because it decides how long a tile's payload is: with
/// alpha every pixel is 4 bytes, without it 3. That is the whole reason the alpha bit lives here and not in
/// each tile — 8 bits once per file instead of a redundant per-tile repeat of a decision that cannot vary
/// within an image anyway.
/// </summary>
[ImageBlock(0x02)]
public sealed class ManifestBlock : ImageBlock
{
    /// <summary>The image carries an alpha channel. Clear means every pixel is opaque and no tile stores an
    /// alpha byte — a quarter smaller, and the normal case for a background or a sheet with no cutouts.</summary>
    public const int FlagAlphaEnabled = 0x01;

    public int Flags;

    public bool AlphaEnabled
    {
        get => (Flags & FlagAlphaEnabled) != 0;
        set => Flags = value ? Flags | FlagAlphaEnabled : Flags & ~FlagAlphaEnabled;
    }

    public ManifestBlock() { }

    public ManifestBlock(bool alphaEnabled) => AlphaEnabled = alphaEnabled;

    protected override void ReadPayload(BitPackage payload, int length)
    {
        ulong flags = payload.ReadVarULong();
        if (flags > int.MaxValue)
            throw new InvalidDataException($"A MANIFEST block declares flags of {flags}");
        // Unknown flags are NOT an error. A flag bit cannot change how anything already understood is read —
        // that is what a new required block type is for — so an old reader ignoring one still decodes the file
        // exactly right. Same forward-compatibility bargain the optional-block rule makes, one level down.
        Flags = (int)flags;
    }

    protected override void WritePayload(BitPackage payload) => payload.WriteVarULong((ulong)Flags);

    public override void Apply(CpuImageBuilder image) => image.SetManifest(Flags);
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
/// A tile: one 16x16 patch of the image, and the instructions for painting it. Every pixel-carrying block in
/// the format is one of these — the image is a grid of tiles in row-major order (left to right, then top to
/// bottom) and the Nth tile block in the file is the Nth cell of that grid, so a tile carries no coordinates
/// of its own. Abstract, so it claims no id; the concrete encodings take ids from 0x10 up.
///
/// Why tiles and not scanlines: a tile is a square of the picture, so whatever an encoding is good at — one
/// flat colour, four colours, a gradient, a repeat of the tile above — it can decide per patch and say so in
/// one byte, with no run ever having to survive the wrap from the end of a row to the start of the next.
/// Every encoding decodes to the same thing, a 16x16 block of RGBA8888, so a reader mixes them freely and a
/// writer picks whichever is smallest for each patch independently.
///
/// The grid covers the image and then some: a 20x20 image is 2x2 tiles, and the tiles hanging off the right
/// and bottom edges are still whole 16x16 blocks. Their out-of-image pixels are written as zero and DISCARDED
/// on read — see <see cref="CpuImageBuilder.AppendTile"/>. That keeps every tile the same size, which is what
/// lets a payload length be checked against the manifest instead of against the tile's position.
/// </summary>
public abstract class TileBlock : ImageBlock
{
    /// <summary>A tile is 16x16. Not a parameter — it is the format.</summary>
    public const int Size = 16;

    /// <summary>Pixels in one tile: 256.</summary>
    public const int PixelCount = Size * Size;

    /// <summary>A decoded tile, always RGBA8888 whatever the encoding: 1024 bytes.</summary>
    public const int DecodedLength = PixelCount * 4;

    /// <summary>The payload exactly as it sits on disk. What it means is the subclass's business.</summary>
    public byte[] Data = [];

    protected override void ReadPayload(BitPackage payload, int length) =>
        Data = length == 0 ? [] : payload.Read(length);

    protected override void WritePayload(BitPackage payload) => payload.Write(Data);

    public override void Apply(CpuImageBuilder image)
    {
        Span<byte> tile = stackalloc byte[DecodedLength];
        Decode(Data, image.AlphaEnabled, tile);
        image.AppendTile(tile);
    }

    /// <summary>
    /// Paints this tile into <paramref name="rgba"/> — <see cref="DecodedLength"/> bytes, row-major, RGBA8888.
    /// <paramref name="alphaEnabled"/> comes from the file's manifest, not from the tile: it is a property of
    /// the image, and a tile that disagrees with it is a corrupt tile.
    /// </summary>
    protected abstract void Decode(ReadOnlySpan<byte> payload, bool alphaEnabled, Span<byte> rgba);
}

/// <summary>
/// The first and simplest tile encoding: the colours themselves, one pixel after another, uncompressed. 256
/// pixels in row-major order, each 8 bits per channel — R G B A when the manifest enables alpha, R G B when it
/// does not, in which case every pixel decodes fully opaque. So a payload is exactly 1024 or 768 bytes and
/// nothing else, which is the check <see cref="Decode"/> makes before it reads a thing.
///
/// This is the floor every other encoding is measured against: any tile can be written this way, so a writer
/// that cannot do better always has this, and a reader that supports only this can still read any file whose
/// writer had nothing better to offer.
/// </summary>
[ImageBlock(0x10)]
public sealed class RawColorTileBlock : TileBlock
{
    /// <summary>How many bytes one pixel takes on disk: 4 with alpha, 3 without.</summary>
    public static int BytesPerPixel(bool alphaEnabled) => alphaEnabled ? 4 : 3;

    /// <summary>The only payload length this block may have, given the manifest.</summary>
    public static int PayloadLength(bool alphaEnabled) => PixelCount * BytesPerPixel(alphaEnabled);

    /// <summary>
    /// Cuts the tile at (<paramref name="tileX"/>, <paramref name="tileY"/>) — tile coordinates, so pixel
    /// (tileX*16, tileY*16) — out of <paramref name="image"/>. Pixels past the right or bottom edge are written
    /// as zero: they are outside the image, a reader throws them away, and zero keeps the bytes deterministic
    /// so the same image always encodes to the same file.
    /// </summary>
    public static RawColorTileBlock ForTile(CpuImage image, int tileX, int tileY, bool alphaEnabled)
    {
        int bytesPerPixel = BytesPerPixel(alphaEnabled);
        byte[] data = new byte[PayloadLength(alphaEnabled)];
        for (int row = 0; row < Size; row++)
        {
            int y = tileY * Size + row;
            if (y >= image.Height)
                break;
            for (int column = 0; column < Size; column++)
            {
                int x = tileX * Size + column;
                if (x >= image.Width)
                    break;
                int source = (y * image.Width + x) * 4;
                int destination = (row * Size + column) * bytesPerPixel;
                data[destination] = image.Pixels[source];
                data[destination + 1] = image.Pixels[source + 1];
                data[destination + 2] = image.Pixels[source + 2];
                if (alphaEnabled)
                    data[destination + 3] = image.Pixels[source + 3];
            }
        }
        return new RawColorTileBlock { Data = data };
    }

    protected override void Decode(ReadOnlySpan<byte> payload, bool alphaEnabled, Span<byte> rgba)
    {
        int bytesPerPixel = BytesPerPixel(alphaEnabled);
        if (payload.Length != PixelCount * bytesPerPixel)
            throw new InvalidDataException(
                $"A raw-colour tile is {PixelCount * bytesPerPixel} bytes with alpha " +
                $"{(alphaEnabled ? "enabled" : "disabled")}, this one is {payload.Length}");
        for (int pixel = 0; pixel < PixelCount; pixel++)
        {
            int source = pixel * bytesPerPixel, destination = pixel * 4;
            rgba[destination] = payload[source];
            rgba[destination + 1] = payload[source + 1];
            rgba[destination + 2] = payload[source + 2];
            rgba[destination + 3] = alphaEnabled ? payload[source + 3] : (byte)0xFF;
        }
    }
}
