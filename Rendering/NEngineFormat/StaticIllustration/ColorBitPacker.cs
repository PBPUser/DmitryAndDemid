using NEngineFormat.StaticIllustration.Blocks;

namespace NEngineFormat.StaticIllustration;

/// <summary>
/// Packs colour entries into a continuous bit stream at the widths their profile declares.
/// </summary>
/// <remarks>
/// <para>
/// Entries are written back to back with no padding between them, most significant bit first,
/// filling each byte before moving to the next. Only the final byte is padded, with zeros.
/// Channels cycle in R, G, B, A order, skipping the static ones.
/// </para>
/// <para>
/// With red at 2 bits, green at 4, blue at 1 and alpha static, a pixel costs 7 bits and five
/// pixels land like this:
/// </para>
/// <code>
/// RRGGGGBR RGGGGBRR GGGGBRRG GGGBRRGG GGB
/// </code>
/// <para>
/// This is what makes a channel's declared size matter: a 2-bit channel really does cost two
/// bits per pixel, where a fixed-width byte would have cost eight.
/// </para>
/// </remarks>
public static class ColorBitPacker
{
    /// <summary>The widths of a profile's varying channels, in R, G, B, A order.</summary>
    public static uint[] ChannelWidths(ColorProfileBlock profile)
    {
        ArgumentNullException.ThrowIfNull(profile);

        var widths = new List<uint>(4);
        foreach (uint flag in Flags)
        {
            uint size = profile.GetChannelSize(flag);
            if (size > 0)
            {
                widths.Add(size);
            }
        }

        return [.. widths];
    }

    /// <summary>Bits one covered pixel occupies across every varying channel.</summary>
    public static uint BitsPerPixel(ColorProfileBlock profile)
    {
        uint total = 0;
        foreach (uint width in ChannelWidths(profile))
        {
            total += width;
        }

        return total;
    }

    /// <summary>Packs entries into a bit stream at the profile's channel widths.</summary>
    /// <exception cref="InvalidOperationException">
    /// An entry does not fit the width its channel declares, or the profile has no varying
    /// channel to spend entries on.
    /// </exception>
    public static byte[] Pack(ColorProfileBlock profile, uint[] colors)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(colors);

        if (colors.Length == 0)
        {
            return [];
        }

        uint[] widths = ChannelWidths(profile);
        if (widths.Length == 0)
        {
            throw new InvalidOperationException(
                $"Profile {profile.ProfileId} has no varying channel, so it cannot carry {colors.Length} colour entries.");
        }

        long totalBits = 0;
        for (int i = 0; i < colors.Length; i++)
        {
            totalBits += widths[i % widths.Length];
        }

        var packed = new byte[(totalBits + 7) / 8];
        long bit = 0;

        for (int i = 0; i < colors.Length; i++)
        {
            uint width = widths[i % widths.Length];
            uint value = colors[i];

            uint limit = width >= 32 ? uint.MaxValue : (1u << (int)width) - 1;
            if (value > limit)
            {
                throw new InvalidOperationException(
                    $"Colour entry {i} is {value}, which does not fit the {width} bit(s) its channel declares.");
            }

            for (int b = (int)width - 1; b >= 0; b--)
            {
                if (((value >> b) & 1) != 0)
                {
                    packed[bit >> 3] |= (byte)(0x80 >> (int)(bit & 7));
                }

                bit++;
            }
        }

        return packed;
    }

    /// <summary>Reads <paramref name="count"/> entries back out of a bit stream.</summary>
    /// <exception cref="InvalidOperationException">
    /// The stream is too short for the entries claimed, or the profile has no varying channel.
    /// </exception>
    public static uint[] Unpack(ColorProfileBlock profile, byte[] packed, int count)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(packed);

        if (count == 0)
        {
            return [];
        }

        uint[] widths = ChannelWidths(profile);
        if (widths.Length == 0)
        {
            throw new InvalidOperationException(
                $"Profile {profile.ProfileId} has no varying channel, so {count} colour entries cannot be read.");
        }

        long totalBits = 0;
        for (int i = 0; i < count; i++)
        {
            totalBits += widths[i % widths.Length];
        }

        if ((long)packed.Length * 8 < totalBits)
        {
            throw new InvalidOperationException(
                $"{count} colour entries need {totalBits} bits but only {packed.Length * 8} were stored.");
        }

        var colors = new uint[count];
        long bit = 0;

        for (int i = 0; i < count; i++)
        {
            uint width = widths[i % widths.Length];
            uint value = 0;

            for (int b = 0; b < width; b++)
            {
                value <<= 1;
                if ((packed[bit >> 3] & (0x80 >> (int)(bit & 7))) != 0)
                {
                    value |= 1;
                }

                bit++;
            }

            colors[i] = value;
        }

        return colors;
    }

    private static uint[] Flags =>
    [
        ColorProfileBlock.RedNonStaticFlag,
        ColorProfileBlock.GreenNonStaticFlag,
        ColorProfileBlock.BlueNonStaticFlag,
        ColorProfileBlock.AlphaNonStaticFlag,
    ];
}
