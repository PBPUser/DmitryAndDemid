using NEngineFormat.Core.Utils;

namespace NEngineFormat.StaticIllustration;

/// <summary>
/// The file header that opens a static illustration: what the file is, how large the picture
/// is, and how its illustration blocks tile across it.
/// </summary>
/// <remarks>
/// <para>Layout:</para>
/// <code>
/// Magic        4 bytes ASCII "AKOB"
/// Flags        VarULong   bitmask
/// BlockCount   VarULong   how many blocks follow
/// ImageWidth   VarULong   pixels
/// ImageHeight  VarULong   pixels
/// BlockWidth   VarULong   illustration tile width, pixels
/// BlockHeight  VarULong   illustration tile height, pixels
/// </code>
/// <para>
/// The image is divided into tiles of <see cref="BlockWidth"/> x <see cref="BlockHeight"/>,
/// each stored as one <see cref="Blocks.IllustrationBlock"/>. A 1920x1080 image in 64x64
/// tiles with three blocks is twelve bytes:
/// </para>
/// <code>
/// 41 4B 4F 42  "AKOB"
/// 00           Flags       = 0
/// 03           BlockCount  = 3
/// 8F 00        ImageWidth  = 1920
/// 88 38        ImageHeight = 1080
/// 40           BlockWidth  = 64
/// 40           BlockHeight = 64
/// </code>
/// </remarks>
public sealed class Header
{
    /// <summary>The ASCII signature every file starts with.</summary>
    public const string Magic = "AKOB";

    /// <summary>Length of <see cref="Magic"/> on disk, in bytes.</summary>
    public const int MagicLength = 4;

    /// <summary>Most blocks a file may declare.</summary>
    public const uint MaxBlockCount = 1_000_000;

    /// <summary>Largest image edge, in pixels.</summary>
    public const uint MaxImageDimension = 65_535;

    /// <summary>
    /// Set when everything after the header is one LZMA2 stream rather than plain blocks.
    /// </summary>
    public const uint CompressedFlag = 1u << 0;

    /// <summary>Bitmask flags.</summary>
    public uint Flags { get; set; }

    /// <summary>Whether the blocks after this header are compressed.</summary>
    public bool IsCompressed
    {
        get => HasFlag(CompressedFlag);
        set => SetFlag(CompressedFlag, value);
    }

    /// <summary>How many blocks follow the header.</summary>
    public uint BlockCount { get; set; }

    /// <summary>Picture width in pixels.</summary>
    public uint ImageWidth { get; set; }

    /// <summary>Picture height in pixels.</summary>
    public uint ImageHeight { get; set; }

    /// <summary>Width of one illustration tile, in pixels.</summary>
    public uint BlockWidth { get; set; }

    /// <summary>Height of one illustration tile, in pixels.</summary>
    public uint BlockHeight { get; set; }

    /// <summary>Whether the picture has any pixels at all.</summary>
    public bool IsEmpty => ImageWidth == 0 || ImageHeight == 0;

    /// <summary>Tiles across, counting a partial one at the right edge.</summary>
    public uint TilesX => BlockWidth == 0 ? 0 : DivideRoundingUp(ImageWidth, BlockWidth);

    /// <summary>Tiles down, counting a partial one at the bottom edge.</summary>
    public uint TilesY => BlockHeight == 0 ? 0 : DivideRoundingUp(ImageHeight, BlockHeight);

    /// <summary>How many illustration tiles cover the picture.</summary>
    public ulong TileCount => (ulong)TilesX * TilesY;

    /// <summary>Whether a flag bit is set.</summary>
    public bool HasFlag(uint flag) => (Flags & flag) != 0;

    /// <summary>Turns a flag bit on or off.</summary>
    public void SetFlag(uint flag, bool enabled) =>
        Flags = enabled ? Flags | flag : Flags & ~flag;

    /// <summary>Checks the header describes a file that can actually be laid out.</summary>
    /// <exception cref="InvalidOperationException">A field is out of range or inconsistent.</exception>
    public void Validate()
    {
        if (BlockCount > MaxBlockCount)
        {
            throw new InvalidOperationException(
                $"BlockCount is {BlockCount}, over the {MaxBlockCount} limit.");
        }

        ThrowIfOversized(nameof(ImageWidth), ImageWidth);
        ThrowIfOversized(nameof(ImageHeight), ImageHeight);
        ThrowIfOversized(nameof(BlockWidth), BlockWidth);
        ThrowIfOversized(nameof(BlockHeight), BlockHeight);

        // A zero tile edge would divide by zero in TilesX/TilesY. Harmless on an empty
        // picture, which has no tiles to lay out either way.
        if (!IsEmpty && (BlockWidth == 0 || BlockHeight == 0))
        {
            throw new InvalidOperationException(
                $"A {ImageWidth}x{ImageHeight} image needs a non-zero tile size, got {BlockWidth}x{BlockHeight}.");
        }
    }

    /// <summary>Reads a header, failing if the data does not start with <see cref="Magic"/>.</summary>
    /// <exception cref="InvalidDataException">The signature is absent or wrong.</exception>
    /// <exception cref="InvalidOperationException">A field is out of range or inconsistent.</exception>
    public void Read(BitPackage package)
    {
        ArgumentNullException.ThrowIfNull(package);

        string magic = package.ReadFixedString(MagicLength);
        if (magic != Magic)
        {
            throw new InvalidDataException($"Not an {Magic} file: found signature '{magic}'.");
        }

        Flags = ReadUInt32(package, nameof(Flags));
        BlockCount = ReadUInt32(package, nameof(BlockCount));
        ImageWidth = ReadUInt32(package, nameof(ImageWidth));
        ImageHeight = ReadUInt32(package, nameof(ImageHeight));
        BlockWidth = ReadUInt32(package, nameof(BlockWidth));
        BlockHeight = ReadUInt32(package, nameof(BlockHeight));

        Validate();
    }

    /// <summary>Writes the header.</summary>
    /// <exception cref="InvalidOperationException">A field is out of range or inconsistent.</exception>
    public void Write(BitPackage package)
    {
        ArgumentNullException.ThrowIfNull(package);
        Validate();

        package.WriteFixedString(Magic);
        package.WriteVarULong(Flags);
        package.WriteVarULong(BlockCount);
        package.WriteVarULong(ImageWidth);
        package.WriteVarULong(ImageHeight);
        package.WriteVarULong(BlockWidth);
        package.WriteVarULong(BlockHeight);
    }

    public override string ToString() =>
        $"{Magic} {ImageWidth}x{ImageHeight}, {BlockWidth}x{BlockHeight} tiles, {BlockCount} block(s), flags 0x{Flags:X}";

    /// <summary>
    /// Reads one field, rejecting a value too large for the 32 bits the property holds.
    /// </summary>
    /// <remarks>
    /// VarULong decodes to 64 bits, so without this a corrupt file would silently truncate
    /// into a plausible-looking small number.
    /// </remarks>
    private static uint ReadUInt32(BitPackage package, string field)
    {
        ulong value = package.ReadVarULong();
        if (value > uint.MaxValue)
        {
            throw new InvalidOperationException($"{field} is {value}, which does not fit in 32 bits.");
        }

        return (uint)value;
    }

    private static void ThrowIfOversized(string field, uint value)
    {
        if (value > MaxImageDimension)
        {
            throw new InvalidOperationException(
                $"{field} is {value}, over the {MaxImageDimension}-pixel limit.");
        }
    }

    private static uint DivideRoundingUp(uint value, uint divisor) => (value + divisor - 1) / divisor;
}
