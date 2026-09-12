namespace NEngineFormat.Core.Utils;

/// <summary>
/// Reads back a bit stream written by <see cref="BitStreamWriter"/>, most significant bit first.
/// </summary>
/// <remarks>
/// The stream carries no length of its own: what is in it is whatever the thing that wrote it
/// says is in it. Running off the end therefore means the file disagreed with itself, and is
/// reported as malformed input rather than as an index fault.
/// </remarks>
public sealed class BitStreamReader
{
    private readonly byte[] _bytes;
    private long _position;

    public BitStreamReader(byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        _bytes = bytes;
    }

    /// <summary>How many bits have been consumed.</summary>
    public long Position => _position;

    /// <summary>How many bits the stream holds, counting the padding of its last byte.</summary>
    public long BitLength => (long)_bytes.Length * 8;

    /// <summary>How many bits are left.</summary>
    public long Remaining => BitLength - _position;

    /// <summary>Reads one bit.</summary>
    /// <exception cref="InvalidOperationException">The stream is spent.</exception>
    public bool ReadBit()
    {
        if (_position >= BitLength)
        {
            throw new InvalidOperationException(
                $"The bit stream holds {BitLength} bit(s) and something asked for one more.");
        }

        bool set = (_bytes[_position >> 3] & (0x80 >> (int)(_position & 7))) != 0;
        _position++;
        return set;
    }

    /// <summary>Reads an unsigned value of <paramref name="width"/> bits.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The width is not between 0 and 32.</exception>
    /// <exception cref="InvalidOperationException">The stream is spent.</exception>
    public uint Read(int width)
    {
        if (width is < 0 or > BitStreamWriter.MaxWidth)
        {
            throw new ArgumentOutOfRangeException(
                nameof(width), width, $"A value occupies 0 to {BitStreamWriter.MaxWidth} bits.");
        }

        uint value = 0;
        for (int i = 0; i < width; i++)
        {
            value = (value << 1) | (ReadBit() ? 1u : 0u);
        }

        return value;
    }

    /// <summary>Reads a two's-complement signed value of <paramref name="width"/> bits.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The width is not between 1 and 32.</exception>
    /// <exception cref="InvalidOperationException">The stream is spent.</exception>
    public int ReadSigned(int width)
    {
        if (width is < 1 or > BitStreamWriter.MaxWidth)
        {
            throw new ArgumentOutOfRangeException(
                nameof(width), width, $"A signed value needs 1 to {BitStreamWriter.MaxWidth} bits.");
        }

        uint raw = Read(width);
        if (width == BitStreamWriter.MaxWidth)
        {
            return (int)raw;
        }

        // The sign bit is the top bit of the declared width, so anything at or above the
        // halfway mark came from a negative number and the bits above it have to come back.
        uint signBit = 1u << (width - 1);
        return (raw & signBit) != 0 ? (int)(raw | ~(signBit - 1)) : (int)raw;
    }

    /// <summary>Reads zero bits up to a one and returns how many there were.</summary>
    /// <param name="limit">
    /// Most zeros to tolerate. Without it a corrupt stream is a run of zeros that only the end
    /// of the buffer stops, by which point it has already built an absurd quotient.
    /// </param>
    /// <exception cref="InvalidOperationException">The run passes the limit, or the stream is spent.</exception>
    public uint ReadUnary(uint limit)
    {
        uint count = 0;
        while (!ReadBit())
        {
            count++;
            if (count > limit)
            {
                throw new InvalidOperationException(
                    $"A unary value ran past {limit} zero bit(s), so the stream is not what it claimed to be.");
            }
        }

        return count;
    }
}
