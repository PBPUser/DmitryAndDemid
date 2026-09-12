using NEngineFormat.Core.Data;
using NEngineFormat.Core.Utils;
using NEngineFormat.StaticIllustration.Data;

namespace NEngineFormat.StaticIllustration.Blocks;

/// <summary>
/// A tile stored as the difference from a nearby one: the pixels it shares are taken from that
/// tile, and only the pixels that differ are written here.
/// </summary>
/// <remarks>
/// <para>Payload layout:</para>
/// <code>
/// TargetTile       VarULong  the tile whose pixels this one starts from
/// ...                        then an illustration block's payload, unchanged
/// </code>
/// <para>
/// This is an <see cref="IllustrationBlock"/> in every respect but one: the mask says which
/// pixels this tile supplies rather than which ones it covers, and a pixel the mask leaves
/// alone keeps whatever the target tile drew there. Two neighbouring tiles alike but for a few
/// pixels therefore cost a bit per pixel plus entries for the handful that differ, rather than
/// a second copy of the tile.
/// </para>
/// <para>
/// An <see cref="IllustrationLinkBlock"/> answers the neighbouring case where whole fields
/// match; this answers the case where nothing matches exactly but almost every pixel does.
/// </para>
/// <para>
/// With <see cref="DeltaFlag"/> set the entries are not colours but zigzagged signed
/// differences from the target's pixels, which is what to reach for when a neighbour is close
/// without being equal: the profile then covers the spread of the differences rather than of
/// the colours, and a channel that took eight bits often takes three.
/// </para>
/// <para>
/// With a <see cref="Transform"/> the target is read turned or mirrored rather than straight
/// through, which finds the repeats a drawing makes of itself that a position-for-position
/// comparison cannot see - the two halves of a symmetrical shape, the four corners of a frame,
/// a texture laid down in alternating directions.
/// </para>
/// <para>
/// <see cref="TargetTile"/> counts tiles, exactly as
/// <see cref="IllustrationRepeatBlock.TargetTile"/> does, and must name an earlier one: the
/// decoder copies pixels that have already been drawn, so a tile cannot start from one that
/// comes after it.
/// </para>
/// </remarks>
[BlockId(Id)]
public class MaskedBlock : IllustrationBlock
{
    /// <summary>The id this block is stored under.</summary>
    public new const uint Id = 9;

    /// <summary>
    /// Bit 3 of <see cref="IllustrationBlock.Flags"/>: the entries are signed differences from
    /// the target tile's pixels rather than colours in their own right.
    /// </summary>
    /// <remarks>
    /// A neighbouring tile is often close without being equal - the same shape a shade darker,
    /// the same gradient a step along. Storing what changed rather than what is there turns a
    /// wide channel into a narrow one, because the differences span a fraction of the range the
    /// colours do.
    /// </remarks>
    public const uint DeltaFlag = 1u << 3;

    /// <summary>
    /// Bit 4 of <see cref="IllustrationBlock.Flags"/>: the target's columns are read right to
    /// left.
    /// </summary>
    public const uint MirrorXFlag = 1u << 4;

    /// <summary>
    /// Bit 5 of <see cref="IllustrationBlock.Flags"/>: the target's rows are read bottom to top.
    /// </summary>
    public const uint MirrorYFlag = 1u << 5;

    /// <summary>
    /// Bit 6 of <see cref="IllustrationBlock.Flags"/>: the target's rows and columns are
    /// swapped, which needs a square tile.
    /// </summary>
    public const uint TransposeFlag = 1u << 6;

    /// <summary>The three bits that together say how the target is read.</summary>
    /// <remarks>
    /// They sit inside the low seven bits of the flags, so a turned difference is written in the
    /// same single byte a straight one is: the orientation is free where it is not used.
    /// </remarks>
    public const uint TransformMask = MirrorXFlag | MirrorYFlag | TransposeFlag;

    /// <summary>How a signed difference is turned into the unsigned value that is stored.</summary>
    /// <remarks>
    /// Zigzag, as signed varints use: 0, -1, 1, -2, 2 become 0, 1, 2, 3, 4. Small differences
    /// stay small whichever way they go, which is what keeps the channel narrow; a plain bias
    /// would push every difference up into the middle of the range instead.
    /// </remarks>
    public static uint ToZigzag(int difference) => Zigzag.Encode(difference);

    /// <summary>Turns a stored value back into the signed difference it came from.</summary>
    public static int FromZigzag(uint stored) => Zigzag.Decode(stored);

    public MaskedBlock() => Type = Id;

    /// <summary>The tile whose pixels fill in everywhere this block's mask is clear.</summary>
    public uint TargetTile { get; set; }

    /// <summary>
    /// Whether the entries are differences from the target's pixels rather than colours.
    /// </summary>
    public bool IsDelta
    {
        get => (Flags & DeltaFlag) != 0;
        set => Flags = value ? Flags | DeltaFlag : Flags & ~DeltaFlag;
    }

    /// <summary>How the target tile is read: straight through, turned, or mirrored.</summary>
    public TileTransform Transform
    {
        get
        {
            var transform = TileTransform.None;

            if ((Flags & MirrorXFlag) != 0)
            {
                transform |= TileTransform.MirrorX;
            }

            if ((Flags & MirrorYFlag) != 0)
            {
                transform |= TileTransform.MirrorY;
            }

            if ((Flags & TransposeFlag) != 0)
            {
                transform |= TileTransform.Transpose;
            }

            return transform;
        }

        set
        {
            Flags &= ~TransformMask;

            if ((value & TileTransform.MirrorX) != 0)
            {
                Flags |= MirrorXFlag;
            }

            if ((value & TileTransform.MirrorY) != 0)
            {
                Flags |= MirrorYFlag;
            }

            if ((value & TileTransform.Transpose) != 0)
            {
                Flags |= TransposeFlag;
            }
        }
    }

    /// <summary>Checks the block can be written.</summary>
    /// <exception cref="InvalidOperationException">
    /// The properties do not describe a writable block, or describe one whose mask cannot be
    /// read as a per-pixel difference.
    /// </exception>
    public override void Validate()
    {
        base.Validate();

        // A palette or a linear run answers a question this block has already answered
        // differently: replaced pixels come from the entries, and the rest from the target
        // tile. Only two modes say that - one pixel at a time, or all of them, which is what a
        // difference where nothing was left alone amounts to and saves it a mask of solid ones.
        if (MaskMode is not (MaskMode.PerPixel or MaskMode.Disabled))
        {
            throw new InvalidOperationException(
                $"A masked block marks the pixels it replaces one by one, so its mask mode must be "
                + $"{MaskMode.PerPixel} or {MaskMode.Disabled}, not {MaskMode}.");
        }

        // Pixels outside the mask come from the target tile, so an out-of-mask colour would be
        // a second answer for pixels that already have one.
        if (UseOutOfMaskColor)
        {
            throw new InvalidOperationException(
                "A masked block takes its uncovered pixels from the tile it points at, so it cannot also set an out-of-mask colour.");
        }
    }

    public override void Read(BitPackage package)
    {
        ArgumentNullException.ThrowIfNull(package);

        TargetTile = (uint)package.ReadVarULong();
        base.Read(package);
    }

    public override void Write(BitPackage package)
    {
        ArgumentNullException.ThrowIfNull(package);
        Validate();

        package.WriteVarULong(TargetTile);
        base.Write(package);
    }

    public override string ToString() =>
        $"Masked from tile {TargetTile} {Transform.Describe()}: {(IsDelta ? "deltas" : "colours")}, "
        + $"profile={ColorProfileId}, {Mask.Length} mask byte(s), {ColorCount} entr(ies)";
}
