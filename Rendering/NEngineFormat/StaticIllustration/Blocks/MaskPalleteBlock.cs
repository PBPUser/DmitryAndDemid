using NEngineFormat.Core;
using NEngineFormat.Core.Data;
using NEngineFormat.Core.Utils;
using NEngineFormat.StaticIllustration.Data;

namespace NEngineFormat.StaticIllustration.Blocks;

/// <summary>
/// A short list of colours a tile's mask can name directly, instead of storing those pixels
/// as colour entries.
/// </summary>
/// <remarks>
/// <para>Payload layout:</para>
/// <code>
/// PaletteId   VarULong  identifies this palette within the file
/// Flags       VarULong  bit 0: some channels copy others; bit 1: colours are packed;
///                       bit 2: some channels ramp; bit 3: some ramps carry corrections
/// [Mirrors]   VarULong  only when the mirror bit is set; see ChannelMirrors
/// Count       VarULong  how many colours follow, 1 to 15
/// [Ramps]      VarULong only when the gradient bit is set: which channels ramp
/// [Corrected]  VarULong only when the correction bit is set: which of those need
///                       correcting, and one width byte per corrected channel
/// Ramps       one start byte and one zigzagged step VarULong per channel that ramps
/// Corrections per corrected channel, one zigzagged correction per colour at that
///                       channel's width, most significant bit first
/// Colors      Count x either one packed VarULong, or one byte per channel that neither
///                       copies another nor ramps, in R,G,B,A order
/// </code>
/// <para>
/// A channel whose values step evenly is not written per colour at all: the palette says where
/// that channel starts and how far each entry moves it, so fifteen shades of it cost what two
/// do. Each channel decides for itself, because a real palette rarely ramps in all of them at
/// once - an antialiased edge ramps through grey while its alpha does as it pleases, and the
/// grey should not have to be written out fifteen times to say so.
/// </para>
/// <para>
/// Few real channels step perfectly. One that nearly does keeps its ramp and carries a small
/// correction per colour - a couple of bits each rather than a whole byte - which is what turns
/// the ramp from a lucky case into the usual one. The corrections are exact: a palette always
/// gives back the colours it was given.
/// </para>
/// <para>
/// Neither of the other two ways of writing a colour wins outright. Channel by channel a colour is four bytes,
/// and fewer when channels agree across the whole palette - grey, or one opacity throughout -
/// so a grey entry costs two rather than four. Packed into one number a colour is a VarULong,
/// which is five bytes when the alpha is high but one when the colour is dark and clear. The
/// palette measures both and says which it used.
/// </para>
/// <para>
/// Under <see cref="Data.MaskMode.Palette"/> a tile's mask stops being one bit per pixel and
/// becomes an index per pixel, wide enough to name any palette entry plus one escape value.
/// A pixel naming an entry costs only those few bits; one taking the escape falls through to
/// the colour entries as usual.
/// </para>
/// <para>
/// The palette lives in its own block so tiles can share it, the same way they share a
/// <see cref="ColorProfileBlock"/>.
/// </para>
/// </remarks>
[BlockId(Id)]
public class MaskPalleteBlock : BlockBase
{
    /// <summary>The id this block is stored under.</summary>
    public const uint Id = 6;

    /// <summary>Most colours a palette may hold.</summary>
    public const int MaxColors = 15;

    /// <summary>Bit in <see cref="Flags"/> marking that some channels copy others.</summary>
    public const uint MirrorFlag = 1u << 0;

    /// <summary>
    /// Bit in <see cref="Flags"/> marking that each colour is one packed number rather than a
    /// run of channel bytes.
    /// </summary>
    public const uint PackedColorsFlag = 1u << 1;

    /// <summary>
    /// Bit in <see cref="Flags"/> marking that the colours step evenly and are given by where
    /// they start and how far each one moves.
    /// </summary>
    public const uint GradientFlag = 1u << 2;

    /// <summary>
    /// Bit in <see cref="Flags"/> marking that some ramping channels carry a correction per
    /// colour, because they nearly step evenly rather than exactly.
    /// </summary>
    public const uint CorrectedFlag = 1u << 3;

    public MaskPalleteBlock() => Type = Id;

    /// <summary>Identifies this palette, so an illustration can point at it.</summary>
    public uint PaletteId { get; set; }

    /// <summary>The colours, packed RGBA with red in the lowest byte.</summary>
    public uint[] Colors { get; set; } = [];

    /// <summary>Bitmask flags.</summary>
    public uint Flags { get; set; }

    /// <summary>
    /// Which channels copy another rather than being written; see <see cref="ChannelMirrors"/>.
    /// </summary>
    public uint Mirrors { get; set; }

    /// <summary>Whether any channel copies another.</summary>
    public bool HasMirrors => (Flags & MirrorFlag) != 0;

    /// <summary>Whether each colour is written as one packed number.</summary>
    public bool HasPackedColors => (Flags & PackedColorsFlag) != 0;

    /// <summary>Whether the colours are written as a start and a step.</summary>
    public bool IsGradient => (Flags & GradientFlag) != 0;

    /// <summary>Which channels are written as a start and a step rather than per colour.</summary>
    /// <remarks>
    /// Four bits, one per channel in R,G,B,A order, meaningful only while
    /// <see cref="GradientFlag"/> is set.
    /// </remarks>
    public uint Ramps { get; set; }

    /// <summary>Whether one channel is written as a start and a step.</summary>
    public bool RampsChannel(int channel) => IsGradient && (Ramps & (1u << channel)) != 0;

    /// <summary>Whether any ramp carries corrections.</summary>
    public bool HasCorrections => (Flags & CorrectedFlag) != 0;

    /// <summary>Which ramping channels carry a correction per colour.</summary>
    public uint Corrected { get; set; }

    /// <summary>How many bits one correction takes, per channel.</summary>
    public uint[] CorrectionBits { get; set; } = new uint[ChannelMirrors.ChannelCount];

    /// <summary>Whether one channel's ramp is corrected per colour.</summary>
    public bool CorrectsChannel(int channel) =>
        HasCorrections && RampsChannel(channel) && (Corrected & (1u << channel)) != 0;

    /// <summary>
    /// The step that leaves the smallest corrections for a channel, and how wide those
    /// corrections have to be.
    /// </summary>
    /// <remarks>
    /// The line is pinned to the first colour and aimed at the last, which is what makes it
    /// reproducible from the two numbers the palette stores. Every colour is then written as
    /// its distance from that line.
    /// </remarks>
    public (int Step, int Bits) NearestRamp(int channel)
    {
        if (Colors.Length < 2)
        {
            return (0, 0);
        }

        int first = ChannelMirrors.ChannelOf(Colors[0], channel);
        int last = ChannelMirrors.ChannelOf(Colors[^1], channel);
        int span = Colors.Length - 1;

        // Rounded to nearest, so the line sits through the colours rather than under them.
        int step = (int)Math.Round((double)(last - first) / span, MidpointRounding.AwayFromZero);

        uint widest = 0;
        for (int i = 0; i < Colors.Length; i++)
        {
            int expected = first + (step * i);
            uint correction = Zigzag.Encode(ChannelMirrors.ChannelOf(Colors[i], channel) - expected);
            widest = Math.Max(widest, correction);
        }

        int bits = 0;
        while (widest >= (1u << bits))
        {
            bits++;
        }

        return (step, bits);
    }

    /// <summary>
    /// How far one channel moves from each colour to the next, or <see langword="null"/> when
    /// it does not move by the same amount every time.
    /// </summary>
    /// <remarks>
    /// Exact, not near enough: every colour has to land on the step, because a channel that
    /// almost steps evenly describes different colours from the ones it holds.
    /// </remarks>
    public int? ChannelStep(int channel)
    {
        if (Colors.Length == 0)
        {
            return null;
        }

        int first = ChannelMirrors.ChannelOf(Colors[0], channel);
        int step = Colors.Length > 1 ? ChannelMirrors.ChannelOf(Colors[1], channel) - first : 0;

        for (int i = 0; i < Colors.Length; i++)
        {
            if (ChannelMirrors.ChannelOf(Colors[i], channel) != first + (step * i))
            {
                return null;
            }
        }

        return step;
    }

    /// <summary>
    /// Settles how the colours are written: channel by channel, leaving out any channel that
    /// always matches another, or packed one number per colour - whichever is smaller.
    /// </summary>
    /// <remarks>
    /// Changes nothing about <see cref="Colors"/>, only how they go to disk. Which wins depends
    /// on the palette: greys and opaque colours are cheaper by channel, dark or clear ones
    /// cheaper packed, because a VarULong of a small number is a single byte.
    /// </remarks>
    public void ChooseStorage()
    {
        uint mirrors = ChannelMirrors.Detect(Colors);
        long descriptor = ChannelMirrors.Any(mirrors) ? VarSize(mirrors) : 0;

        // Each channel that is left after the copies chooses for itself: a start and a step if
        // it steps evenly and that is shorter, otherwise a byte in every colour.
        uint ramps = 0;
        uint corrected = 0;
        var widths = new uint[ChannelMirrors.ChannelCount];
        long byChannel = descriptor;

        for (int c = 0; c < ChannelMirrors.ChannelCount; c++)
        {
            if (ChannelMirrors.IsMirrored(mirrors, c))
            {
                continue;
            }

            long listed = Colors.Length;

            if (ChannelStep(c) is { } step && 1 + VarSize(Zigzag.Encode(step)) <= listed)
            {
                ramps |= 1u << c;
                byChannel += 1 + VarSize(Zigzag.Encode(step));
                continue;
            }

            // Not a ramp, but perhaps nearly one: a line through the colours plus a couple of
            // bits of correction each still beats a byte a colour.
            (int nearStep, int bits) = NearestRamp(c);
            long withCorrections = 1 + VarSize(Zigzag.Encode(nearStep)) + 1
                + (((long)Colors.Length * bits) + 7) / 8;

            if (bits > 0 && withCorrections * 2 < listed)
            {
                ramps |= 1u << c;
                corrected |= 1u << c;
                widths[c] = (uint)bits;
                byChannel += withCorrections;
                continue;
            }

            byChannel += listed;
        }

        // The descriptors are only worth writing if they have something to say.
        if (ramps != 0)
        {
            byChannel += VarSize(ramps);
        }

        if (corrected != 0)
        {
            byChannel += VarSize(corrected);
        }

        long packed = 0;
        foreach (uint color in Colors)
        {
            packed += VarSize(color);
        }

        if (packed < byChannel)
        {
            Mirrors = 0;
            Ramps = 0;
            Corrected = 0;
            Flags = (Flags & ~MirrorFlag & ~GradientFlag & ~CorrectedFlag) | PackedColorsFlag;
            return;
        }

        Mirrors = mirrors;
        Ramps = ramps;
        Corrected = corrected;
        CorrectionBits = widths;
        Flags &= ~PackedColorsFlag;
        Flags = ChannelMirrors.Any(mirrors) ? Flags | MirrorFlag : Flags & ~MirrorFlag;
        Flags = ramps != 0 ? Flags | GradientFlag : Flags & ~GradientFlag;
        Flags = corrected != 0 ? Flags | CorrectedFlag : Flags & ~CorrectedFlag;
    }

    /// <summary>Reads the ramps, then the channels that are written per colour.</summary>
    private void ReadColors(BitPackage package)
    {
        Span<byte> starts = stackalloc byte[ChannelMirrors.ChannelCount];
        Span<int> steps = stackalloc int[ChannelMirrors.ChannelCount];

        for (int c = 0; c < ChannelMirrors.ChannelCount; c++)
        {
            if (RampsChannel(c))
            {
                starts[c] = package.ReadByte();
                steps[c] = Zigzag.Decode((uint)package.ReadVarULong());
            }
        }

        // The corrections follow the ramps they belong to, packed one channel after another.
        var corrections = new int[ChannelMirrors.ChannelCount][];
        for (int c = 0; c < ChannelMirrors.ChannelCount; c++)
        {
            if (!CorrectsChannel(c))
            {
                continue;
            }

            int bits = (int)CorrectionBits[c];
            var packedBits = new byte[(((long)Colors.Length * bits) + 7) / 8];
            for (int i = 0; i < packedBits.Length; i++)
            {
                packedBits[i] = package.ReadByte();
            }

            corrections[c] = new int[Colors.Length];
            long bit = 0;
            for (int i = 0; i < Colors.Length; i++)
            {
                uint value = 0;
                for (int b = 0; b < bits; b++)
                {
                    value <<= 1;
                    if ((packedBits[bit >> 3] & (0x80 >> (int)(bit & 7))) != 0)
                    {
                        value |= 1;
                    }

                    bit++;
                }

                corrections[c][i] = Zigzag.Decode(value);
            }
        }

        Span<byte> channels = stackalloc byte[ChannelMirrors.ChannelCount];

        for (int i = 0; i < Colors.Length; i++)
        {
            for (int c = 0; c < ChannelMirrors.ChannelCount; c++)
            {
                if (ChannelMirrors.IsMirrored(Mirrors, c))
                {
                    continue;
                }

                if (!RampsChannel(c))
                {
                    channels[c] = package.ReadByte();
                    continue;
                }

                int value = starts[c] + (steps[c] * i) + (corrections[c]?[i] ?? 0);
                if (value is < 0 or > 255)
                {
                    throw new InvalidOperationException(
                        $"A gradient channel steps to {value} at colour {i}, which is not a channel value.");
                }

                channels[c] = (byte)value;
            }

            // The copies come last, so a source written later in the same colour is still the
            // one that gets copied.
            for (int c = 0; c < ChannelMirrors.ChannelCount; c++)
            {
                if (ChannelMirrors.IsMirrored(Mirrors, c))
                {
                    channels[c] = channels[ChannelMirrors.Source(Mirrors, c)];
                }
            }

            Colors[i] = ChannelMirrors.Pack(channels);
        }
    }

    /// <summary>Writes the ramps, then the channels that are written per colour.</summary>
    private void WriteColors(BitPackage package)
    {
        for (int c = 0; c < ChannelMirrors.ChannelCount; c++)
        {
            if (!RampsChannel(c))
            {
                continue;
            }

            // An exact ramp has one step; a corrected one takes the line through its colours
            // and writes down how far each of them strays from it.
            int step = ChannelStep(c) ?? (CorrectsChannel(c)
                ? NearestRamp(c).Step
                : throw new InvalidOperationException(
                    $"Channel {c} is marked as a gradient but its colours do not step evenly."));

            package.WriteByte(ChannelMirrors.ChannelOf(Colors[0], c));
            package.WriteVarULong(Zigzag.Encode(step));
        }

        for (int c = 0; c < ChannelMirrors.ChannelCount; c++)
        {
            if (!CorrectsChannel(c))
            {
                continue;
            }

            int bits = (int)CorrectionBits[c];
            int step = ChannelStep(c) ?? NearestRamp(c).Step;
            int start = ChannelMirrors.ChannelOf(Colors[0], c);
            var packedBits = new byte[(((long)Colors.Length * bits) + 7) / 8];
            long bit = 0;

            for (int i = 0; i < Colors.Length; i++)
            {
                uint value = Zigzag.Encode(ChannelMirrors.ChannelOf(Colors[i], c) - (start + (step * i)));
                if (value >= (1u << bits))
                {
                    throw new InvalidOperationException(
                        $"Channel {c} needs a wider correction at colour {i} than the {bits} bits it declares.");
                }

                for (int b = bits - 1; b >= 0; b--)
                {
                    if (((value >> b) & 1) != 0)
                    {
                        packedBits[bit >> 3] |= (byte)(0x80 >> (int)(bit & 7));
                    }

                    bit++;
                }
            }

            foreach (byte value in packedBits)
            {
                package.WriteByte(value);
            }
        }

        foreach (uint color in Colors)
        {
            for (int c = 0; c < ChannelMirrors.ChannelCount; c++)
            {
                if (!ChannelMirrors.IsMirrored(Mirrors, c) && !RampsChannel(c))
                {
                    package.WriteByte(ChannelMirrors.ChannelOf(color, c));
                }
            }
        }
    }

    /// <summary>Bytes a value occupies as a variable-length integer.</summary>
    private static long VarSize(ulong value)
    {
        long size = 1;
        while ((value >>= 7) != 0)
        {
            size++;
        }

        return size;
    }

    /// <summary>
    /// Bits one mask index occupies: enough for every colour plus the escape value.
    /// </summary>
    public int IndexBits => BitsFor(Colors.Length);

    /// <summary>The index meaning "this pixel is not in the palette".</summary>
    public uint EscapeIndex => (uint)Colors.Length;

    /// <summary>Bits an index needs for a palette of the given size.</summary>
    public static int BitsFor(int colorCount)
    {
        int values = colorCount + 1;
        int bits = 1;
        while ((1 << bits) < values)
        {
            bits++;
        }

        return bits;
    }

    /// <summary>Reads one packed index out of a mask, most significant bit first.</summary>
    /// <returns>The index, or <see langword="null"/> when the mask is too short to hold it.</returns>
    public static uint? ReadIndex(byte[] mask, long position, int bits)
    {
        ArgumentNullException.ThrowIfNull(mask);

        long start = position * bits;
        if (start < 0 || start + bits > (long)mask.Length * 8)
        {
            return null;
        }

        uint value = 0;
        for (int i = 0; i < bits; i++)
        {
            long bit = start + i;
            value <<= 1;
            if ((mask[bit >> 3] & (0x80 >> (int)(bit & 7))) != 0)
            {
                value |= 1;
            }
        }

        return value;
    }

    /// <summary>Writes one packed index into a mask, most significant bit first.</summary>
    public static void WriteIndex(byte[] mask, long position, int bits, uint value)
    {
        ArgumentNullException.ThrowIfNull(mask);

        long start = position * bits;
        for (int i = 0; i < bits; i++)
        {
            if (((value >> (bits - 1 - i)) & 1) != 0)
            {
                long bit = start + i;
                mask[bit >> 3] |= (byte)(0x80 >> (int)(bit & 7));
            }
        }
    }

    /// <summary>Bytes a mask needs to hold this many indices.</summary>
    public static int MaskBytes(long positions, int bits) => (int)(((positions * bits) + 7) / 8);

    /// <summary>Checks the palette can be written.</summary>
    /// <exception cref="InvalidOperationException">It is empty or too large.</exception>
    public void Validate()
    {
        if (HasCorrections && (Corrected & ~Ramps & 0b1111) != 0)
        {
            throw new InvalidOperationException(
                "A channel is marked as corrected without ramping; there is nothing to correct.");
        }

        if (IsGradient)
        {
            if ((Ramps & 0b1111) == 0)
            {
                throw new InvalidOperationException(
                    "The gradient flag is set but no channel ramps; the descriptor would say nothing.");
            }

            for (int c = 0; c < ChannelMirrors.ChannelCount; c++)
            {
                if (!RampsChannel(c))
                {
                    continue;
                }

                // A channel that copies another holds nothing of its own to ramp.
                if (ChannelMirrors.IsMirrored(Mirrors, c))
                {
                    throw new InvalidOperationException(
                        $"Channel {c} both copies another channel and ramps; it can only do one.");
                }

                if (ChannelStep(c) is null && !CorrectsChannel(c))
                {
                    throw new InvalidOperationException(
                        $"Channel {c} is marked as a gradient but its colours do not step evenly.");
                }
            }
        }

        if (Colors.Length is 0 or > MaxColors)
        {
            throw new InvalidOperationException(
                $"A palette must hold 1..{MaxColors} colours, got {Colors.Length}.");
        }
    }

    public override void Read(BitPackage package)
    {
        ArgumentNullException.ThrowIfNull(package);

        PaletteId = (uint)package.ReadVarULong();
        Flags = (uint)package.ReadVarULong();
        Mirrors = HasMirrors ? (uint)package.ReadVarULong() : 0;
        Ramps = IsGradient ? (uint)package.ReadVarULong() : 0;
        Corrected = HasCorrections ? (uint)package.ReadVarULong() : 0;
        CorrectionBits = new uint[ChannelMirrors.ChannelCount];

        for (int c = 0; c < ChannelMirrors.ChannelCount; c++)
        {
            if (!CorrectsChannel(c))
            {
                continue;
            }

            CorrectionBits[c] = package.ReadByte();
            if (CorrectionBits[c] is 0 or > 9)
            {
                throw new InvalidOperationException(
                    $"Channel {c} corrects by {CorrectionBits[c]} bits; a correction takes 1 to 9.");
            }
        }

        ulong count = package.ReadVarULong();
        if (count is 0 or > (ulong)MaxColors)
        {
            throw new InvalidOperationException(
                $"A palette must hold 1..{MaxColors} colours, got {count}.");
        }

        Colors = new uint[count];

        if (HasPackedColors)
        {
            for (int i = 0; i < Colors.Length; i++)
            {
                Colors[i] = (uint)package.ReadVarULong();
            }

            return;
        }

        ReadColors(package);
    }

    public override void Write(BitPackage package)
    {
        ArgumentNullException.ThrowIfNull(package);
        Validate();

        package.WriteVarULong(PaletteId);
        package.WriteVarULong(Flags);

        if (HasMirrors)
        {
            package.WriteVarULong(Mirrors);
        }

        if (IsGradient)
        {
            package.WriteVarULong(Ramps);
        }

        if (HasCorrections)
        {
            package.WriteVarULong(Corrected);

            for (int c = 0; c < ChannelMirrors.ChannelCount; c++)
            {
                if (CorrectsChannel(c))
                {
                    package.WriteByte((byte)CorrectionBits[c]);
                }
            }
        }

        package.WriteVarULong((ulong)Colors.Length);

        if (HasPackedColors)
        {
            foreach (uint color in Colors)
            {
                package.WriteVarULong(color);
            }

            return;
        }

        WriteColors(package);
    }

    public override string ToString() =>
        $"MaskPallete #{PaletteId}: {Colors.Length} colour(s), {IndexBits} bit(s) per pixel"
        + (IsGradient ? $", ramps 0x{Ramps:X}" : string.Empty)
        + (HasCorrections ? $", corrected 0x{Corrected:X}" : string.Empty)
        + (HasMirrors ? $", mirrors 0x{Mirrors:X}" : string.Empty);
}
