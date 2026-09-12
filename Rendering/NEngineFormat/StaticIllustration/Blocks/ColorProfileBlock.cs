using NEngineFormat.Core;
using NEngineFormat.Core.Data;
using NEngineFormat.Core.Utils;
using NEngineFormat.StaticIllustration.Data;

namespace NEngineFormat.StaticIllustration.Blocks;

/// <summary>
/// Block that sets color profile for the blocks.
/// </summary>
/// <remarks>
/// <para>Payload layout:</para>
/// <code>
/// ProfileId            VarULong
/// Flags                VarULong
/// [Mirrors]            VarULong  only when the mirror flag is set: bits 0-3 say which
///                                channels copy another, then two bits per channel naming
///                                which one it copies, in R,G,B,A order
/// [NonStaticColorSize] byte      only when at least one channel is non-static;
///                                stored as size-1, so 0 means 1 bit and 15 means 16 bits
/// [skips]              only when the skips flag is set, per varying channel in R,G,B,A order:
///                        count VarULong, then count pairs of (usable, skipped) VarULong
/// StaticColorR         byte or VarULong   omitted for a channel that copies another
/// StaticColorG         byte or VarULong
/// StaticColorB         byte or VarULong
/// StaticColorA         byte or VarULong
/// </code>
/// <para>
/// A channel that copies another stores nothing at all: no width, no minimum, and no entry per
/// pixel. Grey is three channels holding one value, and a picture of it costs a third as much
/// said once as said three times.
/// </para>
/// <para>
/// A channel value is written as a single byte while the channel is 8 bits or narrower, and
/// as a VarULong above that.
/// </para>
/// <para>
/// The Offset properties are <b>derived, not stored</b> — the maximum offset is recalculated
/// from <see cref="NonStaticColorSize"/> and the skips on read. See
/// <see cref="RecomputeOffsets"/>.
/// </para>
/// </remarks>
[BlockId(Id)]
public class ColorProfileBlock : BlockBase
{
    public const int Id = 3;

    /// <summary>Bit in <see cref="Flags"/> marking the red channel non-static.</summary>
    public const uint RedNonStaticFlag = 1u << 0;

    /// <summary>Bit in <see cref="Flags"/> marking the green channel non-static.</summary>
    public const uint GreenNonStaticFlag = 1u << 1;

    /// <summary>Bit in <see cref="Flags"/> marking the blue channel non-static.</summary>
    public const uint BlueNonStaticFlag = 1u << 2;

    /// <summary>Bit in <see cref="Flags"/> marking the alpha channel non-static.</summary>
    public const uint AlphaNonStaticFlag = 1u << 3;

    /// <summary>Bit in <see cref="Flags"/> marking that colour skips follow.</summary>
    public const uint ColorSkipsFlag = 1u << 4;

    /// <summary>Bit in <see cref="Flags"/> marking that some channels copy others.</summary>
    public const uint MirrorFlag = 1u << 5;

    /// <summary>
    /// Bit in <see cref="Flags"/> marking that this profile counts in the numbering left by the
    /// picture's <see cref="ColorSkipsBlock"/> rather than in plain channel values.
    /// </summary>
    /// <remarks>
    /// The minimum, this profile's own runs and every entry are all counted in values the
    /// picture actually uses; the skips block turns the result back into a colour. A profile
    /// whose entries are not colours at all - a difference from another tile, say - never sets
    /// this, because there is no colour there to renumber.
    /// </remarks>
    public const uint GlobalSkipsFlag = 1u << 6;

    /// <summary>Every channel bit, for testing whether any channel is non-static at all.</summary>
    public const uint AllChannelFlags =
        RedNonStaticFlag | GreenNonStaticFlag | BlueNonStaticFlag | AlphaNonStaticFlag;

    /// <summary>Largest channel width the format can express, in bits.</summary>
    public const uint MaxNonStaticColorSize = 16;

    /// <summary>Most skip runs one channel may declare.</summary>
    public const uint MaxSkipRuns = 64;

    /// <summary>Bits each channel's size occupies inside <see cref="NonStaticColorSize"/>.</summary>
    public const int ChannelSizeBits = 4;

    /// <summary>Mask of one channel's size nibble.</summary>
    public const uint ChannelSizeMask = (1u << ChannelSizeBits) - 1;

    /// <summary>Width used for channel values when a channel is static.</summary>
    private const uint DefaultChannelBits = 8;

    /// <summary>
    /// Flags:
    /// Bits 0-3 RGBA Non-Static Color Enabled (0=Disabled, 1=Enabled), Disabled means the color is static,
    /// and set to the static value which is stored in the variable StaticColorR, StaticColorG, StaticColorB, StaticColorA,
    /// for 8 bit and lower we're using byte reader/writer, for higher values - varuint.
    /// If Bits set to enabled, StaticColorR/G/B/A will be used as minimum value for the color channel. Maximum offset not stored, but can be calculated as: 2^NonStaticColorSize - 1
    /// If ColorSkips Enabled, this offset will be extended to total size per channel
    /// </summary>
    /// <summary>
    /// Identifies this profile within the file, so an
    /// <see cref="IllustrationBlock.ColorProfileId"/> can point at it. This is the profile's
    /// own id, not the block type id in <see cref="BlockBase.Type"/>, which is the same for
    /// every profile block.
    /// </summary>
    public uint ProfileId { get; set; }

    public uint Flags { get; set; }
    /// <summary>
    /// Size of the non-static color, if flag which used to store information
    /// about how large non static color size in bits is, up to 16 bits. (0000=1 bit, 1111=16 bits),
    /// Read and Write only if Flag is set to 1 for the color channel.
    /// </summary>
    /// <remarks>
    /// One four-bit size per channel, red in the lowest nibble then green, blue and alpha, so
    /// each channel gets its own width rather than sharing one. A nibble holds the width minus
    /// one, so 0 means a single bit and 15 means sixteen. A static channel's nibble is unused.
    /// Prefer <see cref="GetChannelSize"/> and <see cref="SetChannelSize"/> over unpacking it
    /// by hand.
    /// </remarks>
    public uint NonStaticColorSize { get; set; }
    /// <summary>
    /// Count of color skips, if flag is set to 1 for the color channel.
    /// </summary>
    /// <remarks>
    /// One count per <b>varying</b> channel, in R, G, B, A order, so a channel can have its own
    /// gaps. A channel with no gaps carries a count of zero.
    /// </remarks>
    public uint[] ColorSkipCounts { get; set; } = [];
    /// <summary>
    /// every color skip stored as: length of the non-skip, length of skip
    /// </summary>
    /// <remarks>
    /// <para>
    /// The runs of every channel laid end to end, in the same order as
    /// <see cref="ColorSkipCounts"/>. A run is a pair: how many values are usable, then how
    /// many are skipped over. The last usable stretch is implied and not stored.
    /// </para>
    /// <para>
    /// This is what lets a channel that only ever holds 0-16 and 240-255 cost six bits instead
    /// of eight: one run of (17 usable, 223 skipped) folds the two islands into a contiguous
    /// 0-32, which <see cref="CompactOffset"/> maps into and <see cref="ExpandOffset"/> maps
    /// back out of.
    /// </para>
    /// </remarks>
    public uint[] ColorSkips { get; set; } = [];
    public uint StaticColorR { get; set; }
    public uint StaticColorG { get; set; }
    public uint StaticColorB { get; set; }
    public uint StaticColorA { get; set; }
    public uint OffsetColorR { get; set; }
    public uint OffsetColorG { get; set; }
    public uint OffsetColorB { get; set; }
    public uint OffsetColorA { get; set; }


    public ColorProfileBlock()
    {
        Type = Id;
    }

    /// <summary>Whether any colour channel varies rather than being a single static value.</summary>
    public bool HasNonStaticColor => (Flags & AllChannelFlags) != 0;

    /// <summary>Whether colour skips are present.</summary>
    public bool HasColorSkips => (Flags & ColorSkipsFlag) != 0;

    /// <summary>
    /// Which channels copy another, and which one each of them copies: bits 0-3 mark the
    /// copying channels in R,G,B,A order, and two bits from bit 4 on name each one's source.
    /// </summary>
    public uint Mirrors { get; set; }

    /// <summary>Whether any channel copies another.</summary>
    public bool HasMirrors => (Flags & MirrorFlag) != 0;

    /// <summary>Whether this profile counts in the picture's own numbering.</summary>
    public bool UsesGlobalSkips
    {
        get => (Flags & GlobalSkipsFlag) != 0;
        set => Flags = value ? Flags | GlobalSkipsFlag : Flags & ~GlobalSkipsFlag;
    }

    /// <summary>Whether this channel takes its value from another one.</summary>
    public bool IsMirrored(uint channelFlag) =>
        HasMirrors && ChannelMirrors.IsMirrored(Mirrors, IndexOf(channelFlag));

    /// <summary>The channel this one copies, or itself when it copies nothing.</summary>
    public uint MirrorSource(uint channelFlag) =>
        IsMirrored(channelFlag) ? Channels[ChannelMirrors.Source(Mirrors, IndexOf(channelFlag))] : channelFlag;

    /// <summary>Makes one channel copy another, or stop copying.</summary>
    public void SetMirror(uint channelFlag, uint sourceFlag)
    {
        if (channelFlag != sourceFlag)
        {
            // A copied channel holds no value of its own, so it cannot also vary per pixel.
            SetNonStatic(channelFlag, false);
        }

        Mirrors = ChannelMirrors.With(Mirrors, IndexOf(channelFlag), IndexOf(sourceFlag));
        Flags = ChannelMirrors.Any(Mirrors) ? Flags | MirrorFlag : Flags & ~MirrorFlag;
    }

    /// <summary>Whether a channel varies, as opposed to holding a single static value.</summary>
    public bool IsNonStatic(uint channelFlag) => (Flags & channelFlag) != 0;

    /// <summary>Turns a channel's non-static bit on or off.</summary>
    public void SetNonStatic(uint channelFlag, bool enabled) =>
        Flags = enabled ? Flags | channelFlag : Flags & ~channelFlag;

    /// <summary>
    /// How many bits one channel's offset occupies, 1 to 16. A static channel returns 0,
    /// because it stores no offset at all.
    /// </summary>
    public uint GetChannelSize(uint channelFlag) =>
        IsNonStatic(channelFlag)
            ? ((NonStaticColorSize >> ShiftFor(channelFlag)) & ChannelSizeMask) + 1
            : 0;

    /// <summary>Sets one channel's offset width, 1 to 16 bits.</summary>
    public void SetChannelSize(uint channelFlag, uint bits)
    {
        if (bits is 0 or > MaxNonStaticColorSize)
        {
            throw new ArgumentOutOfRangeException(
                nameof(bits), bits, $"A channel size must be 1..{MaxNonStaticColorSize} bits.");
        }

        int shift = ShiftFor(channelFlag);
        NonStaticColorSize = (NonStaticColorSize & ~(ChannelSizeMask << shift)) | ((bits - 1) << shift);
    }

    /// <summary>
    /// How a channel's value is stored: its own width when it varies, otherwise a plain byte.
    /// </summary>
    public uint GetChannelBits(uint channelFlag) =>
        IsNonStatic(channelFlag) ? GetChannelSize(channelFlag) : DefaultChannelBits;

    /// <summary>
    /// How many <see cref="IllustrationBlock.Colors"/> entries one covered pixel consumes:
    /// one per varying channel.
    /// </summary>
    public int ChannelsPerPixel =>
        (IsNonStatic(RedNonStaticFlag) ? 1 : 0)
        + (IsNonStatic(GreenNonStaticFlag) ? 1 : 0)
        + (IsNonStatic(BlueNonStaticFlag) ? 1 : 0)
        + (IsNonStatic(AlphaNonStaticFlag) ? 1 : 0);

    /// <summary>Declared bits per pixel across every varying channel, for reporting.</summary>
    public uint PackedColorBits =>
        GetChannelSize(RedNonStaticFlag)
        + GetChannelSize(GreenNonStaticFlag)
        + GetChannelSize(BlueNonStaticFlag)
        + GetChannelSize(AlphaNonStaticFlag);

    /// <summary>Where a channel's size nibble sits inside <see cref="NonStaticColorSize"/>.</summary>
    private static int ShiftFor(uint channelFlag) => channelFlag switch
    {
        RedNonStaticFlag => 0 * ChannelSizeBits,
        GreenNonStaticFlag => 1 * ChannelSizeBits,
        BlueNonStaticFlag => 2 * ChannelSizeBits,
        AlphaNonStaticFlag => 3 * ChannelSizeBits,
        _ => throw new ArgumentOutOfRangeException(nameof(channelFlag), channelFlag, "Not a channel flag."),
    };

    /// <summary>This channel's skip runs, or empty when it has no gaps.</summary>
    public ReadOnlySpan<uint> SkipsFor(uint channelFlag)
    {
        int index = VaryingIndex(channelFlag);
        if (index < 0 || !HasColorSkips || index >= ColorSkipCounts.Length)
        {
            return default;
        }

        int start = 0;
        for (int i = 0; i < index; i++)
        {
            start += (int)ColorSkipCounts[i] * 2;
        }

        int length = (int)ColorSkipCounts[index] * 2;
        return start + length > ColorSkips.Length
            ? default
            : ColorSkips.AsSpan(start, length);
    }

    /// <summary>
    /// Turns a stored offset into the real distance above the channel's minimum, jumping the
    /// gaps its skip runs describe.
    /// </summary>
    public uint ExpandOffset(uint channelFlag, uint compact) =>
        SkipRuns.Expand(SkipsFor(channelFlag), compact);

    /// <summary>
    /// Turns a real distance above the channel'''s minimum into the offset actually stored.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// The value falls inside a range the profile says is skipped, so it cannot be stored.
    /// </exception>
    public uint CompactOffset(uint channelFlag, uint actual) =>
        SkipRuns.Compact(SkipsFor(channelFlag), actual);

    /// <summary>Position of a channel among the varying ones, or -1 when it is static.</summary>
    private int VaryingIndex(uint channelFlag)
    {
        int index = 0;
        foreach (uint flag in ChannelOrder)
        {
            if (flag == channelFlag)
            {
                return IsNonStatic(flag) ? index : -1;
            }

            if (IsNonStatic(flag))
            {
                index++;
            }
        }

        return -1;
    }

    private static uint[] ChannelOrder =>
        [RedNonStaticFlag, GreenNonStaticFlag, BlueNonStaticFlag, AlphaNonStaticFlag];

    /// <summary>
    /// Recalculates the per-channel maximum offsets, which are not stored in the payload.
    /// </summary>
    /// <remarks>
    /// A non-static channel spans <c>2^size - 1</c> above its static minimum, where the size
    /// is that channel's own nibble of <see cref="NonStaticColorSize"/>. When skips are present
    /// that span widens to the total covered by the skip runs, since each pair contributes both
    /// its non-skipped and its skipped length. A static channel has no span at all.
    /// </remarks>
    public void RecomputeOffsets()
    {
        OffsetColorR = SpanOf(RedNonStaticFlag);
        OffsetColorG = SpanOf(GreenNonStaticFlag);
        OffsetColorB = SpanOf(BlueNonStaticFlag);
        OffsetColorA = SpanOf(AlphaNonStaticFlag);
    }

    /// <summary>
    /// The largest offset one channel can carry above its minimum. With skips this reaches
    /// past <c>2^size - 1</c>, because the stored offsets jump over the gaps.
    /// </summary>
    private uint SpanOf(uint channelFlag)
    {
        uint size = GetChannelSize(channelFlag);
        if (size == 0)
        {
            return 0;
        }

        uint widest = size >= 32 ? uint.MaxValue : (1u << (int)size) - 1;
        return ExpandOffset(channelFlag, widest);
    }

    /// <summary>Checks the profile can actually be written.</summary>
    /// <exception cref="InvalidOperationException">The properties do not describe a writable profile.</exception>
    public void Validate()
    {
        // Four nibbles, one per channel, so anything above 16 bits is not a size at all.
        if (NonStaticColorSize > 0xFFFF)
        {
            throw new InvalidOperationException(
                $"NonStaticColorSize packs four 4-bit sizes, so it cannot exceed 0xFFFF; got 0x{NonStaticColorSize:X}.");
        }

        if (HasColorSkips)
        {
            if (ColorSkipCounts.Length != ChannelsPerPixel)
            {
                throw new InvalidOperationException(
                    $"ColorSkipCounts holds {ColorSkipCounts.Length} entries; one is needed per varying channel, so {ChannelsPerPixel}.");
            }

            long expected = 0;
            foreach (uint count in ColorSkipCounts)
            {
                if (count > MaxSkipRuns)
                {
                    throw new InvalidOperationException(
                        $"A channel declares {count} skip runs, over the {MaxSkipRuns} limit.");
                }

                expected += count * 2;
            }

            // Each run is a pair, so the array is always twice the counts. Checked here because
            // a mismatch would otherwise write a payload no reader could parse.
            if (ColorSkips.Length != expected)
            {
                throw new InvalidOperationException(
                    $"ColorSkips holds {ColorSkips.Length} values; the declared runs need {expected}.");
            }
        }
        else if (ColorSkipCounts.Length != 0 || ColorSkips.Length != 0)
        {
            throw new InvalidOperationException(
                "Skip runs are set but the colour skips flag is not.");
        }

        // StaticColor is the channel's minimum value, not an offset into it, so it is bounded
        // by how it is stored rather than by that channel's size. At 8 bits or fewer a channel
        // is written as a single byte; above that it is a varuint, which holds anything.
        foreach (uint channel in Channels)
        {
            if (!IsMirrored(channel))
            {
                continue;
            }

            if (IsNonStatic(channel))
            {
                throw new InvalidOperationException(
                    $"Channel {Name(channel)} copies another channel, so it cannot vary per pixel as well.");
            }

            uint source = MirrorSource(channel);
            if (source == channel)
            {
                throw new InvalidOperationException($"Channel {Name(channel)} cannot copy itself.");
            }

            // One step only. A chain would have to be resolved in an order the file does not
            // state, and a cycle could not be resolved at all.
            if (IsMirrored(source))
            {
                throw new InvalidOperationException(
                    $"Channel {Name(channel)} copies {Name(source)}, which copies another channel itself.");
            }
        }

        if (HasMirrors && !ChannelMirrors.Any(Mirrors))
        {
            throw new InvalidOperationException(
                "The mirror flag is set but no channel copies another; the descriptor would say nothing.");
        }

        ThrowIfChannelTooWide(nameof(StaticColorR), StaticColorR, RedNonStaticFlag);
        ThrowIfChannelTooWide(nameof(StaticColorG), StaticColorG, GreenNonStaticFlag);
        ThrowIfChannelTooWide(nameof(StaticColorB), StaticColorB, BlueNonStaticFlag);
        ThrowIfChannelTooWide(nameof(StaticColorA), StaticColorA, AlphaNonStaticFlag);
    }

    public override void Read(BitPackage package)
    {
        ArgumentNullException.ThrowIfNull(package);

        ProfileId = (uint)package.ReadVarULong();
        Flags = (uint)package.ReadVarULong();

        Mirrors = HasMirrors ? (uint)package.ReadVarULong() : 0;

        // Four packed nibbles, so up to 16 bits: a varuint rather than a single byte.
        NonStaticColorSize = HasNonStaticColor ? (uint)package.ReadVarULong() : 0;
        if (NonStaticColorSize > 0xFFFF)
        {
            throw new InvalidOperationException(
                $"NonStaticColorSize 0x{NonStaticColorSize:X} exceeds the four 4-bit sizes it packs.");
        }

        if (HasColorSkips)
        {
            int channels = ChannelsPerPixel;
            ColorSkipCounts = new uint[channels];
            var runs = new List<uint>();

            for (int c = 0; c < channels; c++)
            {
                uint count = (uint)package.ReadVarULong();

                // Bounded before allocating, so a corrupt count cannot ask for a huge array.
                if (count > MaxSkipRuns)
                {
                    throw new InvalidOperationException(
                        $"Channel {c} declares {count} skip runs, over the {MaxSkipRuns} limit.");
                }

                ColorSkipCounts[c] = count;
                for (int i = 0; i < count * 2; i++)
                {
                    runs.Add((uint)package.ReadVarULong());
                }
            }

            ColorSkips = [.. runs];
        }
        else
        {
            ColorSkipCounts = [];
            ColorSkips = [];
        }

        // A channel that copies another has no value of its own to read.
        StaticColorR = IsMirrored(RedNonStaticFlag) ? 0 : ReadChannel(package, RedNonStaticFlag);
        StaticColorG = IsMirrored(GreenNonStaticFlag) ? 0 : ReadChannel(package, GreenNonStaticFlag);
        StaticColorB = IsMirrored(BlueNonStaticFlag) ? 0 : ReadChannel(package, BlueNonStaticFlag);
        StaticColorA = IsMirrored(AlphaNonStaticFlag) ? 0 : ReadChannel(package, AlphaNonStaticFlag);

        RecomputeOffsets();
    }

    public override void Write(BitPackage package)
    {
        ArgumentNullException.ThrowIfNull(package);
        Validate();

        package.WriteVarULong(ProfileId);
        package.WriteVarULong(Flags);

        if (HasMirrors)
        {
            package.WriteVarULong(Mirrors);
        }

        if (HasNonStaticColor)
        {
            package.WriteVarULong(NonStaticColorSize);
        }

        if (HasColorSkips)
        {
            int at = 0;
            foreach (uint count in ColorSkipCounts)
            {
                package.WriteVarULong(count);
                for (int i = 0; i < count * 2; i++)
                {
                    package.WriteVarULong(ColorSkips[at++]);
                }
            }
        }

        if (!IsMirrored(RedNonStaticFlag))
        {
            WriteChannel(package, StaticColorR, RedNonStaticFlag);
        }

        if (!IsMirrored(GreenNonStaticFlag))
        {
            WriteChannel(package, StaticColorG, GreenNonStaticFlag);
        }

        if (!IsMirrored(BlueNonStaticFlag))
        {
            WriteChannel(package, StaticColorB, BlueNonStaticFlag);
        }

        if (!IsMirrored(AlphaNonStaticFlag))
        {
            WriteChannel(package, StaticColorA, AlphaNonStaticFlag);
        }
    }

    public override string ToString()
    {
        string channels = $"{Letter(RedNonStaticFlag, 'R')}{Letter(GreenNonStaticFlag, 'G')}{Letter(BlueNonStaticFlag, 'B')}{Letter(AlphaNonStaticFlag, 'A')}";
        string skips = HasColorSkips ? $", {ColorSkips.Length / 2} skip run(s)" : string.Empty;
        string sizes = HasNonStaticColor ? $" {PackedColorBits}b/px" : " flat";
        return $"ColorProfile {channels}{sizes}{skips}";

        char Letter(uint flag, char letter) => IsMirrored(flag)
            ? Name(MirrorSource(flag))
            : IsNonStatic(flag) ? letter : char.ToLowerInvariant(letter);
    }

    /// <summary>The four channel flags, in the order everything reads them.</summary>
    public static readonly uint[] Channels =
        [RedNonStaticFlag, GreenNonStaticFlag, BlueNonStaticFlag, AlphaNonStaticFlag];

    /// <summary>Where a channel sits in R,G,B,A order.</summary>
    public static int IndexOf(uint channelFlag) => channelFlag switch
    {
        RedNonStaticFlag => 0,
        GreenNonStaticFlag => 1,
        BlueNonStaticFlag => 2,
        AlphaNonStaticFlag => 3,
        _ => throw new ArgumentOutOfRangeException(nameof(channelFlag), channelFlag, "Not a channel."),
    };

    /// <summary>The letter a channel goes by.</summary>
    private static char Name(uint channelFlag) => channelFlag switch
    {
        RedNonStaticFlag => 'R',
        GreenNonStaticFlag => 'G',
        BlueNonStaticFlag => 'B',
        _ => 'A',
    };

    /// <summary>Reads one channel value, byte-wide up to 8 bits and VarULong above that.</summary>
    private uint ReadChannel(BitPackage package, uint channelFlag) =>
        GetChannelBits(channelFlag) <= 8 ? package.ReadByte() : (uint)package.ReadVarULong();

    /// <summary>Writes one channel value, byte-wide up to 8 bits and VarULong above that.</summary>
    private void WriteChannel(BitPackage package, uint value, uint channelFlag)
    {
        if (GetChannelBits(channelFlag) <= 8)
        {
            package.WriteByte((byte)value);
        }
        else
        {
            package.WriteVarULong(value);
        }
    }

    private void ThrowIfChannelTooWide(string name, uint value, uint channelFlag)
    {
        if (GetChannelBits(channelFlag) <= 8 && value > byte.MaxValue)
        {
            throw new InvalidOperationException(
                $"{name} is {value}, which does not fit in the single byte an 8-bit channel is stored in.");
        }
    }
}
