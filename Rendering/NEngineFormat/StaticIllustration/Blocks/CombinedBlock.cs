using NEngineFormat.Core;
using NEngineFormat.Core.Data;
using NEngineFormat.Core.Utils;
using NEngineFormat.StaticIllustration.Data;

namespace NEngineFormat.StaticIllustration.Blocks;

/// <summary>
/// Several neighbouring tiles written as one block, sharing a single set of settings between
/// them.
/// </summary>
/// <remarks>
/// <para>Payload layout:</para>
/// <code>
/// Flags            VarULong             shared by every tile
/// ColorProfileId   VarULong
/// [MaskPaletteId]  VarULong             only when the mask mode is Palette
/// [OutOfMaskColor] VarULong             only when the out-of-mask colour flag is set
/// TileCount        VarULong
/// [MaskLengths]    VarULong x TileCount only when the mask mode is not Disabled
/// ColorCounts      VarULong x TileCount
/// Mask             bytes                every tile's mask, back to back
/// PackedLength     VarULong
/// PackedColors     bytes                every tile's entries as one bit stream
/// </code>
/// <para>
/// A tile costs more than its pixels: its own block framing, its flags, the profile it names
/// and the two lengths that follow. Tiles side by side usually agree on all of that, and a
/// picture made of thousands of them pays for the agreement thousands of times. Written
/// together they pay once, and their entries share one bit stream rather than each rounding up
/// to a whole byte.
/// </para>
/// <para>
/// The tiles fill consecutive slots, exactly as the same blocks would have separately, so
/// combining them changes nothing about which tile is where. <see cref="Tiles"/> is the view a
/// decoder works from: one <see cref="IllustrationBlock"/> per tile, each holding its own slice
/// of the mask and the entries.
/// </para>
/// </remarks>
[BlockId(Id)]
public class CombinedBlock : BlockBase
{
    /// <summary>The id this block is stored under.</summary>
    public const uint Id = 10;

    /// <summary>Most tiles one combined block may hold.</summary>
    public const int MaxTiles = 4096;

    public CombinedBlock() => Type = Id;

    /// <summary>The <see cref="IllustrationBlock.Flags"/> every tile here shares.</summary>
    public uint Flags { get; set; }

    /// <summary>The colour profile every tile here shares.</summary>
    public uint ColorProfileId { get; set; }

    /// <summary>The mask palette every tile here shares, under <see cref="MaskMode.Palette"/>.</summary>
    public uint MaskPaletteId { get; set; }

    /// <summary>The out-of-mask colour every tile here shares.</summary>
    public ulong OutOfMaskColor { get; set; }

    /// <summary>How many mask bytes each tile owns, in order.</summary>
    public uint[] MaskLengths { get; set; } = [];

    /// <summary>How many colour entries each tile owns, in order.</summary>
    public uint[] ColorCounts { get; set; } = [];

    /// <summary>Every tile's mask, one after another.</summary>
    public byte[] Mask { get; set; } = [];

    /// <summary>Every tile's entries as stored: one bit stream across all of them.</summary>
    public byte[] PackedColors { get; set; } = [];

    /// <summary>
    /// Every tile's entries unpacked, in order. Filled in by
    /// <see cref="StaticIllustrationAsset"/>, which is the only thing that can resolve the
    /// profile the widths come from.
    /// </summary>
    public uint[] Colors { get; set; } = [];

    /// <summary>How many tiles this block covers.</summary>
    public int TileCount => ColorCounts.Length;

    /// <summary>The mask mode held in bits 0-1 of <see cref="Flags"/>.</summary>
    public MaskMode MaskMode
    {
        get => (MaskMode)(Flags & IllustrationBlock.MaskModeMask);
        set => Flags = (Flags & ~IllustrationBlock.MaskModeMask)
            | ((uint)value & IllustrationBlock.MaskModeMask);
    }

    /// <summary>Whether <see cref="OutOfMaskColor"/> is in use.</summary>
    public bool UseOutOfMaskColor
    {
        get => (Flags & IllustrationBlock.UseOutOfMaskColorFlag) != 0;
        set => Flags = value
            ? Flags | IllustrationBlock.UseOutOfMaskColorFlag
            : Flags & ~IllustrationBlock.UseOutOfMaskColorFlag;
    }

    /// <summary>Whether a mask is stored at all.</summary>
    public bool HasMask => MaskMode != MaskMode.Disabled;

    /// <summary>Entries across every tile here.</summary>
    public long TotalColorCount
    {
        get
        {
            long total = 0;
            foreach (uint count in ColorCounts)
            {
                total += count;
            }

            return total;
        }
    }

    /// <summary>
    /// The tiles this block stands for, one block each, in the order they fill their slots.
    /// </summary>
    /// <remarks>
    /// Built fresh on each call rather than cached, because the entries arrive later than the
    /// block does: a file is read, then its colours are unpacked against a profile that only
    /// the whole file can resolve, and a view taken before that would hold nothing.
    /// </remarks>
    public IReadOnlyList<IllustrationBlock> Tiles
    {
        get
        {
            var tiles = new IllustrationBlock[TileCount];
            int maskAt = 0;
            int colorAt = 0;

            for (int i = 0; i < tiles.Length; i++)
            {
                int maskLength = HasMask && i < MaskLengths.Length ? (int)MaskLengths[i] : 0;
                int colorCount = (int)ColorCounts[i];

                tiles[i] = new IllustrationBlock
                {
                    Flags = Flags,
                    ColorProfileId = ColorProfileId,
                    MaskPaletteId = MaskPaletteId,
                    OutOfMaskColor = OutOfMaskColor,
                    Mask = Slice(Mask, maskAt, maskLength),
                    Colors = Slice(Colors, colorAt, colorCount),
                    ColorCount = (uint)colorCount,
                };

                maskAt += maskLength;
                colorAt += colorCount;
            }

            return tiles;
        }
    }

    /// <summary>Whether the flags claim shapes, which a combined block cannot carry.</summary>
    /// <remarks>
    /// Tiles are combined because they agree on everything but their mask and entries, and the
    /// shapes a tile paints are neither: two tiles painting different boxes agree on nothing
    /// worth sharing, so they are left as blocks of their own.
    /// </remarks>
    public bool ClaimsBrushes => (Flags & IllustrationBlock.BrushFlag) != 0;

    /// <summary>Checks the block can be written.</summary>
    /// <exception cref="InvalidOperationException">The tiles do not add up.</exception>
    public void Validate()
    {
        if (ClaimsBrushes)
        {
            throw new InvalidOperationException(
                "A combined block claims shapes, but shapes belong to one tile and it holds several.");
        }

        // One tile is not a combination, and writing it as one would cost more than writing the
        // block it came from.
        if (TileCount < 2)
        {
            throw new InvalidOperationException(
                $"A combined block holds at least two tiles, not {TileCount}.");
        }

        if (TileCount > MaxTiles)
        {
            throw new InvalidOperationException(
                $"A combined block holds {TileCount} tiles, over the {MaxTiles} limit.");
        }

        if (HasMask && MaskLengths.Length != ColorCounts.Length)
        {
            throw new InvalidOperationException(
                $"A combined block holds {MaskLengths.Length} mask length(s) against {ColorCounts.Length} tile(s).");
        }

        if (!HasMask && MaskLengths.Length > 0)
        {
            throw new InvalidOperationException(
                $"Mask mode is {MaskMode} but {MaskLengths.Length} mask length(s) are set; they would not be written.");
        }

        long masked = 0;
        foreach (uint length in MaskLengths)
        {
            masked += length;
        }

        if (HasMask && masked != Mask.Length)
        {
            throw new InvalidOperationException(
                $"The tiles claim {masked} mask byte(s) between them, but {Mask.Length} are stored.");
        }

        if (Colors.Length > 0 && Colors.Length != TotalColorCount)
        {
            throw new InvalidOperationException(
                $"The tiles claim {TotalColorCount} entr(ies) between them, but {Colors.Length} are held.");
        }

        if (!UseOutOfMaskColor && OutOfMaskColor != 0)
        {
            throw new InvalidOperationException(
                "OutOfMaskColor is set but the out-of-mask colour flag is not; it would not be written.");
        }
    }

    public override void Read(BitPackage package)
    {
        ArgumentNullException.ThrowIfNull(package);

        Flags = (uint)package.ReadVarULong();
        ColorProfileId = (uint)package.ReadVarULong();
        MaskPaletteId = MaskMode == MaskMode.Palette ? (uint)package.ReadVarULong() : 0;
        OutOfMaskColor = UseOutOfMaskColor ? package.ReadVarULong() : 0;

        ulong tiles = package.ReadVarULong();
        if (tiles is < 2 or > MaxTiles)
        {
            throw new InvalidOperationException(
                $"A combined block claims {tiles} tiles; it must hold 2 to {MaxTiles}.");
        }

        MaskLengths = new uint[HasMask ? tiles : 0];
        long maskBytes = 0;
        for (int i = 0; i < MaskLengths.Length; i++)
        {
            MaskLengths[i] = ReadCount(package, IllustrationBlock.MaxMaskLength, "mask bytes");
            maskBytes += MaskLengths[i];
        }

        if (maskBytes > IllustrationBlock.MaxMaskLength)
        {
            throw new InvalidOperationException(
                $"The tiles claim {maskBytes} mask bytes between them, over the {IllustrationBlock.MaxMaskLength}-byte limit.");
        }

        ColorCounts = new uint[tiles];
        long entries = 0;
        for (int i = 0; i < ColorCounts.Length; i++)
        {
            ColorCounts[i] = ReadCount(package, IllustrationBlock.MaxColorCount, "colours");
            entries += ColorCounts[i];
        }

        if (entries > IllustrationBlock.MaxColorCount)
        {
            throw new InvalidOperationException(
                $"The tiles claim {entries} colours between them, over the {IllustrationBlock.MaxColorCount} limit.");
        }

        Mask = maskBytes == 0 ? [] : package.Read((int)maskBytes);

        ulong packedLength = package.ReadVarULong();
        if (packedLength > IllustrationBlock.MaxColorCount)
        {
            throw new InvalidOperationException(
                $"Packed colour data claims {packedLength} bytes, over the {IllustrationBlock.MaxColorCount} limit.");
        }

        PackedColors = new byte[packedLength];
        for (int i = 0; i < PackedColors.Length; i++)
        {
            PackedColors[i] = package.ReadByte();
        }

        // Unpacking needs the channel widths, which live in a profile this block cannot see.
        Colors = [];
    }

    public override void Write(BitPackage package)
    {
        ArgumentNullException.ThrowIfNull(package);
        Validate();

        package.WriteVarULong(Flags);
        package.WriteVarULong(ColorProfileId);

        if (MaskMode == MaskMode.Palette)
        {
            package.WriteVarULong(MaskPaletteId);
        }

        if (UseOutOfMaskColor)
        {
            package.WriteVarULong(OutOfMaskColor);
        }

        package.WriteVarULong((ulong)TileCount);

        if (HasMask)
        {
            foreach (uint length in MaskLengths)
            {
                package.WriteVarULong(length);
            }
        }

        foreach (uint count in ColorCounts)
        {
            package.WriteVarULong(count);
        }

        if (Mask.Length > 0)
        {
            package.Write(Mask);
        }

        package.WriteVarULong((ulong)PackedColors.Length);
        foreach (byte value in PackedColors)
        {
            package.WriteByte(value);
        }
    }

    public override string ToString() =>
        $"Combined {TileCount} tiles: profile={ColorProfileId}, {MaskMode} mask {Mask.Length} bytes, {TotalColorCount} entr(ies)";

    /// <summary>Reads one per-tile count, rejecting a value too large to be real.</summary>
    private static uint ReadCount(BitPackage package, int limit, string what)
    {
        ulong value = package.ReadVarULong();
        if (value > (ulong)limit)
        {
            throw new InvalidOperationException(
                $"A tile in a combined block claims {value} {what}, over the {limit} limit.");
        }

        return (uint)value;
    }

    /// <summary>One tile's slice, clamped to what is actually there.</summary>
    /// <remarks>
    /// A short stream leaves the tiles after it empty rather than throwing, matching how a
    /// block handles a mask that runs out: the picture comes out wrong where the data stops,
    /// which is easier to see than an exception with no image at all.
    /// </remarks>
    private static T[] Slice<T>(T[] source, int start, int length)
    {
        if (start >= source.Length || length <= 0)
        {
            return [];
        }

        return source.AsSpan(start, Math.Min(length, source.Length - start)).ToArray();
    }
}
