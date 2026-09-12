namespace NEngineFormat.Core.Utils;

/// <summary>
/// Builds a continuous bit stream, most significant bit first.
/// </summary>
/// <remarks>
/// <para>
/// Values are written back to back with no padding between them, filling each byte before
/// moving on to the next; only the final byte is padded, and with zeros. That is the order
/// <see cref="BitStreamReader"/> reads in, and the order the illustration format already packs
/// its colour entries in, so a stream written here reads the same way everywhere in the
/// library.
/// </para>
/// <para>
/// Signed values go out in two's complement at a declared width rather than zigzagged, so a
/// sample of the full width the format allows still fits in exactly that many bits. Zigzag is
/// for values that sit near zero and only sometimes stray; a raw sample is not one of those.
/// </para>
/// </remarks>
public sealed class BitStreamWriter
{
    /// <summary>Widest single value the stream can carry, in bits.</summary>
    public const int MaxWidth = 32;

    private readonly List<byte> _bytes = [];
    private long _bits;

    /// <summary>How many bits have been written.</summary>
    public long BitLength => _bits;

    /// <summary>How many bytes those bits come to, counting a part-filled last one.</summary>
    public int ByteLength => _bytes.Count;

    /// <summary>Appends one bit.</summary>
    public void WriteBit(bool set)
    {
        if ((_bits & 7) == 0)
        {
            _bytes.Add(0);
        }

        if (set)
        {
            _bytes[^1] |= (byte)(0x80 >> (int)(_bits & 7));
        }

        _bits++;
    }

    /// <summary>Appends the low <paramref name="width"/> bits of an unsigned value, high bit first.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The width is not between 0 and 32.</exception>
    /// <exception cref="InvalidOperationException">The value does not fit the width.</exception>
    public void Write(uint value, int width)
    {
        ThrowIfBadWidth(width);

        if (width < MaxWidth && value > (1u << width) - 1)
        {
            throw new InvalidOperationException($"{value} does not fit in {width} bit(s).");
        }

        for (int bit = width - 1; bit >= 0; bit--)
        {
            WriteBit(((value >> bit) & 1) != 0);
        }
    }

    /// <summary>Appends a signed value in two's complement at <paramref name="width"/> bits.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The width is not between 1 and 32.</exception>
    /// <exception cref="InvalidOperationException">The value does not fit the width.</exception>
    public void WriteSigned(int value, int width)
    {
        if (width is < 1 or > MaxWidth)
        {
            throw new ArgumentOutOfRangeException(
                nameof(width), width, $"A signed value needs 1 to {MaxWidth} bits.");
        }

        if (!FitsSigned(value, width))
        {
            throw new InvalidOperationException($"{value} does not fit in {width} signed bit(s).");
        }

        // Masked rather than shifted, so the sign bit lands in the top bit of the declared
        // width and everything above it is dropped instead of smeared across the stream.
        uint mask = width == MaxWidth ? uint.MaxValue : (1u << width) - 1;
        Write((uint)value & mask, width);
    }

    /// <summary>Appends <paramref name="count"/> zero bits followed by a one.</summary>
    /// <remarks>This is the quotient half of a Rice code, counted in zeros and closed by a one.</remarks>
    public void WriteUnary(uint count)
    {
        for (uint i = 0; i < count; i++)
        {
            WriteBit(false);
        }

        WriteBit(true);
    }

    /// <summary>The bits written so far, the last byte padded with zeros.</summary>
    public byte[] Export() => [.. _bytes];

    /// <summary>Whether a signed value fits in a given number of two's-complement bits.</summary>
    public static bool FitsSigned(int value, int width)
    {
        if (width >= MaxWidth)
        {
            return true;
        }

        int limit = 1 << (width - 1);
        return value >= -limit && value <= limit - 1;
    }

    /// <summary>The narrowest two's-complement width that holds every one of these values.</summary>
    /// <returns>1 to 32; an empty run gives 1, which is the narrowest a width may be.</returns>
    public static int SignedWidth(ReadOnlySpan<int> values)
    {
        int width = 1;
        foreach (int value in values)
        {
            while (width < MaxWidth && !FitsSigned(value, width))
            {
                width++;
            }
        }

        return width;
    }

    private static void ThrowIfBadWidth(int width)
    {
        if (width is < 0 or > MaxWidth)
        {
            throw new ArgumentOutOfRangeException(
                nameof(width), width, $"A value occupies 0 to {MaxWidth} bits.");
        }
    }
}
