using System.Collections.Frozen;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using DmitryAndDemid.Data.Archive;
using DmitryAndDemid.Utils;

namespace DmitryAndDemid.Rendering;

/// <summary>
/// Declares a concrete <see cref="ImageBlock"/>'s identity on disk. Every non-abstract block class must carry
/// exactly one of these, and no two may claim the same type byte — <see cref="ImageBlock"/>'s registry checks
/// both when it builds, so a block that is added wrong fails loudly at first use rather than writing a file
/// nothing can read.
///
/// This is the whole reason a block class needs no registration anywhere else: dropping a new
/// <c>[ImageBlock(0x05)] sealed class WhateverBlock : ImageBlock</c> into the assembly is enough for the reader
/// to start dispatching to it.
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
public sealed class ImageBlockAttribute(byte id) : Attribute
{
    /// <summary>The low 7 bits of the block's type byte — its identity within the format. 0x00..0x7F.</summary>
    public byte Id { get; } = id;

    /// <summary>Whether bit <see cref="ImageBlock.RequiredFlag"/> is set on the type byte, i.e. whether a reader
    /// that has never heard of this block must refuse the file (true, the default — the block carries something
    /// the image cannot be decoded without) or step over it and carry on (false).</summary>
    public bool Required { get; init; } = true;

    /// <summary>The byte actually written: <see cref="Required"/> in bit 7, <see cref="Id"/> in bits 0-6.</summary>
    public byte TypeByte => (byte)(Required ? Id | ImageBlock.RequiredFlag : Id);
}

/// <summary>
/// One block of a <c>.negr</c> image file — <c>[Type:1][Length:varint][Payload:Length]</c> — and all the framing
/// logic <see cref="CpuImage.Load"/> and <see cref="CpuImage.Save"/> need. <c>Data/Archive/CpuImage.sp</c> is the
/// format's specification; the concrete blocks live in <c>Rendering/ImageBlocks.cs</c>.
///
/// Everything a block has to do is one of four things, and the base class owns the parts that are the same for
/// all of them — reading and writing the type byte and the length, buffering a payload so its length can be
/// written before it, resolving a type byte to a class, and deciding what to do with a type byte it does not
/// know. A subclass supplies only <see cref="ReadPayload"/>, <see cref="WritePayload"/> and <see cref="Apply"/>,
/// and declares its type byte with an <see cref="ImageBlockAttribute"/>.
///
/// The unknown-type rule is the point of the whole shape, and it lives in <see cref="Read"/>: because every
/// block declares its length, a reader can step over a block it does not understand, and because bit 7 of the
/// type byte says whether that is allowed, the format can gain optional blocks without breaking old readers
/// while still refusing files that need something an old reader genuinely cannot do.
/// </summary>
public abstract class ImageBlock
{
    /// <summary>Bit 7 of the type byte — see <see cref="ImageBlockAttribute.Required"/>.</summary>
    public const byte RequiredFlag = 0x80;

    /// <summary>A ceiling on one block's payload, checked before the read allocates it. A corrupt or hostile file
    /// can claim any length it likes in a varint; without this, one bad byte becomes a multi-gigabyte
    /// allocation. Far above the fixed 768/1024 bytes a tile costs.</summary>
    public const int MaxPayloadLength = 256 * 1024 * 1024;

    private static readonly FrozenDictionary<byte, Type> ByTypeByte;
    private static readonly FrozenDictionary<Type, ImageBlockAttribute> Descriptors;

    static ImageBlock()
    {
        Dictionary<byte, Type> byTypeByte = new();
        Dictionary<Type, ImageBlockAttribute> descriptors = new();
        foreach (Type type in BlockTypes())
        {
            ImageBlockAttribute attribute = type.GetCustomAttribute<ImageBlockAttribute>() ??
                throw new InvalidOperationException(
                    $"{type.Name} derives from {nameof(ImageBlock)} but carries no [{nameof(ImageBlock)}] " +
                    "attribute, so it has no type byte and could never be read back");
            if (attribute.Id > 0x7F)
                throw new InvalidOperationException(
                    $"{type.Name} claims id 0x{attribute.Id:X2}; an id is 7 bits, 0x00..0x7F (bit 7 is the " +
                    $"required flag, set it with Required = ... instead)");
            if (byTypeByte.TryGetValue(attribute.TypeByte, out Type? clash))
                throw new InvalidOperationException(
                    $"Block type 0x{attribute.TypeByte:X2} is claimed by both {clash.Name} and {type.Name}");
            byTypeByte[attribute.TypeByte] = type;
            descriptors[type] = attribute;
        }
        ByTypeByte = byTypeByte.ToFrozenDictionary();
        Descriptors = descriptors.ToFrozenDictionary();
    }

    private static IEnumerable<Type> BlockTypes()
    {
        Type[] types;
        try
        {
            types = typeof(ImageBlock).Assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            // One unloadable type elsewhere in the assembly must not take image loading down with it — the
            // blocks are plain managed classes and are always among the types that did load. (The game's
            // assembly holds Gtk/Raylib/Silk-bound types that a constrained host may not be able to load.)
            types = ex.Types.Where(t => t != null).ToArray()!;
        }
        return types.Where(t => !t.IsAbstract && t.IsSubclassOf(typeof(ImageBlock)));
    }

    /// <summary>This block's declaration — its id, whether it is required, and the type byte those make.</summary>
    public ImageBlockAttribute Descriptor => Descriptors[GetType()];

    /// <summary>Decodes this block's payload. <paramref name="payload"/> reads only this block's bytes, so a
    /// block cannot run off into the next one; <paramref name="length"/> is how many there are, which the blocks
    /// with a bulk tail (the tiles) need and the rest ignore.</summary>
    protected abstract void ReadPayload(BitPackage payload, int length);

    /// <summary>Encodes this block's payload. The length is the caller's problem, not the block's.</summary>
    protected abstract void WritePayload(BitPackage payload);

    /// <summary>Folds this block into the image being decoded. This is where a block's meaning lives, as opposed
    /// to its encoding: RESOLUTION sizes the pixel buffer, MANIFEST says how tiles are laid out, a tile block
    /// paints its 16x16 patch of it, METADATA adds a pair.</summary>
    public abstract void Apply(CpuImageBuilder image);

    /// <summary>
    /// Reads the next whole block. Returns <c>null</c> for a block whose type byte this build does not know and
    /// whose type byte says that is allowed — its payload has already been stepped over, and the caller carries
    /// on with the next block. Throws for one that says it is not.
    /// </summary>
    public static ImageBlock? Read(BitPackage package)
    {
        byte typeByte;
        ulong declared;
        try
        {
            typeByte = package.ReadByte();
            declared = package.ReadVarULong();
        }
        catch (EndOfStreamException)
        {
            // Truncation, not a clean end: a stream may only run out AFTER an END block.
            throw new InvalidDataException("Truncated .negr: the file ends mid-block, with no END block");
        }
        if (declared > MaxPayloadLength)
            throw new InvalidDataException(
                $"Block 0x{typeByte:X2} claims a {declared}-byte payload, over the {MaxPayloadLength}-byte limit");

        int length = (int)declared;
        byte[] payload;
        try
        {
            payload = length == 0 ? [] : package.Read(length);
        }
        catch (EndOfStreamException)
        {
            throw new InvalidDataException(
                $"Truncated .negr: block 0x{typeByte:X2} promised {length} payload bytes, the file ended first");
        }

        if (!ByTypeByte.TryGetValue(typeByte, out Type? type))
        {
            if ((typeByte & RequiredFlag) != 0)
                throw new InvalidDataException(
                    $"Required block type 0x{typeByte:X2} is unknown to this build, and the file cannot be " +
                    "decoded without it");
            return null;
        }

        ImageBlock block = (ImageBlock)Activator.CreateInstance(type)!;
        using BitPackage reader = BitPackage.OpenReadMemoryPackage(payload);
        try
        {
            block.ReadPayload(reader, length);
        }
        catch (EndOfStreamException)
        {
            // The payload was whole (it was that many bytes) but held less than the block's contents need — a
            // RESOLUTION block of one varint, say. Corruption like any other, so it reads like any other.
            throw new InvalidDataException(
                $"Block 0x{typeByte:X2} has a {length}-byte payload, too short for its contents");
        }
        return block;
    }

    /// <summary>Writes this block whole: type byte, payload length, payload.</summary>
    public void Write(BitPackage package)
    {
        // The payload is built into memory first because its length has to go on the wire ahead of it and the
        // package is forward-only. Bounded by what the callers write — a pixel strip, a metadata pair.
        BitPackage buffer = new();
        WritePayload(buffer);
        byte[] payload = buffer.Export();
        package.WriteByte(Descriptor.TypeByte);
        package.WriteVarULong((ulong)payload.Length);
        package.Write(payload);
    }
}

/// <summary>
/// The half-decoded image that <see cref="ImageBlock.Apply"/> writes into: a <see cref="CpuImage"/> assembled
/// out of blocks as they are read, plus the checks that make sure the blocks actually added up to one.
/// </summary>
public sealed class CpuImageBuilder(string path)
{
    public int Width { get; private set; }
    public int Height { get; private set; }

    /// <summary>Null until the RESOLUTION block sizes it; complete once <see cref="TilesSeen"/> reaches
    /// <see cref="TileCount"/>.</summary>
    public byte[]? Pixels { get; private set; }

    /// <summary>From the MANIFEST block: whether tiles carry an alpha byte per pixel. Decides how long a tile
    /// payload is, which is why a tile before the manifest cannot be read at all.</summary>
    public bool AlphaEnabled { get; private set; }

    private bool ManifestSeen;

    /// <summary>How many <see cref="TileBlock"/>s have been placed. Also *which* tile comes next: tiles carry no
    /// coordinates, their position in the file is their position in the grid.</summary>
    public int TilesSeen { get; private set; }

    public Dictionary<string, string> Metadata { get; } = new();

    /// <summary>Tiles across and down. The grid covers the image and overhangs it: a 20-pixel width is 2 tiles,
    /// the second of which is half outside.</summary>
    public int TileColumns => (Width + TileBlock.Size - 1) / TileBlock.Size;
    public int TileRows => (Height + TileBlock.Size - 1) / TileBlock.Size;
    public int TileCount => TileColumns * TileRows;

    /// <summary>Sizes the image. Called by <see cref="ResolutionBlock"/>, which the format requires to come
    /// before any tile.</summary>
    public void Allocate(int width, int height)
    {
        if (Pixels != null)
            throw new InvalidDataException($"{path} has more than one RESOLUTION block");
        long size = (long)width * height * 4;
        if (size > int.MaxValue)
            throw new InvalidDataException($"{path} declares a {width}x{height} image, which does not fit in memory");
        Width = width;
        Height = height;
        Pixels = new byte[size];
    }

    /// <summary>Takes the image-wide flags. Called by <see cref="ManifestBlock"/>.</summary>
    public void SetManifest(int flags)
    {
        if (ManifestSeen)
            throw new InvalidDataException($"{path} has more than one MANIFEST block");
        ManifestSeen = true;
        AlphaEnabled = (flags & ManifestBlock.FlagAlphaEnabled) != 0;
    }

    /// <summary>
    /// Places one decoded 16x16 tile — <see cref="TileBlock.DecodedLength"/> bytes of RGBA8888 — at the next
    /// cell of the grid, clipped to the image. The clipping is the whole reason a tile may overhang: an edge
    /// tile brings 16 columns and the image takes however many of them are actually inside it.
    /// </summary>
    public void AppendTile(ReadOnlySpan<byte> rgba)
    {
        RequireResolutionAndManifest();
        if (rgba.Length != TileBlock.DecodedLength)
            throw new InvalidDataException(
                $"{path}: a decoded tile is {TileBlock.DecodedLength} bytes, got {rgba.Length}");
        if (TilesSeen >= TileCount)
            throw new InvalidDataException(
                $"{path} holds more than the {TileCount} tiles its {Width}x{Height} image is made of");

        int left = TilesSeen % TileColumns * TileBlock.Size;
        int top = TilesSeen / TileColumns * TileBlock.Size;
        int columns = Math.Min(TileBlock.Size, Width - left);
        for (int row = 0; row < TileBlock.Size; row++)
        {
            int y = top + row;
            if (y >= Height)
                break;
            rgba.Slice(row * TileBlock.Size * 4, columns * 4)
                .CopyTo(Pixels.AsSpan((y * Width + left) * 4));
        }
        TilesSeen++;
    }

    [MemberNotNull(nameof(Pixels))]
    private void RequireResolutionAndManifest()
    {
        if (Pixels == null)
            throw new InvalidDataException($"{path} has a tile block before its RESOLUTION block");
        if (!ManifestSeen)
            throw new InvalidDataException($"{path} has a tile block before its MANIFEST block");
    }

    /// <summary>The finished image, or the reason the blocks did not make one.</summary>
    public CpuImage Build()
    {
        if (Pixels == null)
            throw new InvalidDataException($"{path} has no RESOLUTION block");
        if (!ManifestSeen)
            throw new InvalidDataException($"{path} has no MANIFEST block");
        if (TilesSeen != TileCount)
            throw new InvalidDataException(
                $"{path} holds {TilesSeen} tiles, a {Width}x{Height} image is {TileColumns}x{TileRows} = " +
                $"{TileCount} of them");
        CpuImage image = CpuImage.FromPixels(Width, Height, Pixels);
        foreach ((string key, string value) in Metadata)
            image.Metadata[key] = value;
        return image;
    }
}
