using DmitryAndDemid.Utils;
using Xunit;

namespace DmitryAndDemid.Tests;

/// <summary>
/// Round-trip and wire-format tests for <see cref="BitPackage"/>, the hand-rolled varint reader/writer behind
/// every binary file the game ships (.sid stages, .rpy replays, scoreaag2.gsy, .negr images). The format is
/// undocumented except by its code, so the layout tests here pin the byte-level contract: a writer change that
/// breaks the layout breaks every shipped file, and these are what say so.
///
/// Pure byte work — no assets, no GPU.
/// </summary>
public class BitPackageTests
{
    /// <summary>Write with <paramref name="write"/>, then hand the bytes back as a stream-backed reader — the
    /// same path <c>OpenStreamReadPackage</c> takes when the game loads a file.</summary>
    private static BitPackage Written(Action<BitPackage> write)
    {
        var package = new BitPackage();
        write(package);
        return BitPackage.OpenReadMemoryPackage(package.Export());
    }

    private static byte[] BytesOf(Action<BitPackage> write)
    {
        var package = new BitPackage();
        write(package);
        return package.Export();
    }

    [Theory]
    [InlineData(0L)]
    [InlineData(1L)]
    [InlineData(127L)]
    [InlineData(128L)]
    [InlineData(255L)]
    [InlineData(300L)]
    [InlineData(16383L)]
    [InlineData(16384L)]
    [InlineData(100000L)]
    [InlineData(2147483647L)]   // int.MaxValue
    [InlineData(-2147483648L)]  // int.MinValue
    [InlineData(1099511627776L)] // 1 << 40
    [InlineData(-1L)]
    [InlineData(-127L)]
    [InlineData(-128L)]
    [InlineData(-300L)]
    [InlineData(-100000L)]
    public void VarLong_round_trips(long value)
    {
        using BitPackage reader = Written(p => p.WriteVarLong(value));
        Assert.Equal(value, reader.ReadVarLong());
    }

    /// <summary>
    /// The signed varint is a custom three-tier layout (1 byte below 128, 2 below 16384, then a length-prefixed
    /// form), NOT the LEB128 a reader might assume from <see cref="BitPackage.ReadVarULong"/> sitting next to it.
    /// Pin the small end of it byte for byte.
    /// </summary>
    [Fact]
    public void VarLong_has_the_expected_wire_layout()
    {
        Assert.Equal(new byte[] { 0x00 }, BytesOf(p => p.WriteVarLong(0)));
        Assert.Equal(new byte[] { 0x7F }, BytesOf(p => p.WriteVarLong(127)));
        Assert.Equal(new byte[] { 0x80, 0x01 }, BytesOf(p => p.WriteVarLong(128)));
        // Sign lives in 0x40 of the second byte; -1 is 1 with that flag set.
        Assert.Equal(new byte[] { 0x81, 0x40 }, BytesOf(p => p.WriteVarLong(-1)));
    }

    [Theory]
    [InlineData(0UL)]
    [InlineData(1UL)]
    [InlineData(127UL)]
    [InlineData(128UL)]
    [InlineData(300UL)]
    [InlineData(ulong.MaxValue)]
    public void VarULong_round_trips(ulong value)
    {
        using BitPackage reader = Written(p => p.WriteVarULong(value));
        Assert.Equal(value, reader.ReadVarULong());
    }

    [Fact]
    public void VarULong_is_big_endian_base128()
    {
        Assert.Equal(new byte[] { 0x00 }, BytesOf(p => p.WriteVarULong(0)));
        Assert.Equal(new byte[] { 0x81, 0x00 }, BytesOf(p => p.WriteVarULong(128)));
    }

    [Theory]
    [InlineData("")]
    [InlineData("ab")]
    [InlineData("HeBo3MoJKHo uHutsuAJlu3upoBaTb")]   // the codebase's Latin-lookalike Cyrillic
    [InlineData("Невозможно инициализировать")]       // real Cyrillic (UTF-8, two bytes per char)
    public void String_round_trips(string value)
    {
        using BitPackage reader = Written(p => p.WriteString(value));
        Assert.Equal(value, reader.ReadString());
    }

    [Fact]
    public void Long_string_round_trips_past_the_one_byte_length_limit()
    {
        string value = new('x', 300);   // 300 > 127, so the length prefix itself goes multi-byte
        using BitPackage reader = Written(p => p.WriteString(value));
        Assert.Equal(value, reader.ReadString());
    }

    [Fact]
    public void String_is_length_prefixed_utf8()
    {
        Assert.Equal(new byte[] { 0x02, (byte)'a', (byte)'b' }, BytesOf(p => p.WriteString("ab")));
    }

    [Theory]
    [InlineData(0f)]
    [InlineData(-1.5f)]
    [InlineData(3.14f)]
    [InlineData(float.MaxValue)]
    [InlineData(float.Epsilon)]
    public void Float_round_trips(float value)
    {
        using BitPackage reader = Written(p => p.WriteFloat(value));
        Assert.Equal(value, reader.ReadFloat());
    }

    /// <summary>
    /// <c>WritePlayAreaPosition</c> shifts the point by (+32,+32) into unsigned byte range and stores coord
    /// bit 8 in a mask byte; <c>ReadPosition</c> returns the stored (shifted) value — the -32 is the caller's
    /// business. So the round trip is identity only once the shift is accounted for, and coordinates past 223
    /// (i.e. shifted past 255) exercise the mask.
    /// </summary>
    [Theory]
    [InlineData(0, 0)]
    [InlineData(-32, -32)]     // the shifted origin, (0,0) stored
    [InlineData(100, 200)]
    [InlineData(223, 415)]     // the last coordinates that fit a plain byte once shifted
    [InlineData(300, 400)]     // both mask bits set
    public void Play_area_position_round_trips_with_its_32_offset(int x, int y)
    {
        using BitPackage reader = Written(p => p.WritePlayAreaPosition((x, y)));
        Assert.Equal((x + 32, y + 32), reader.ReadPosition());
    }

    [Fact]
    public void Mixed_fields_read_back_in_write_order()
    {
        using BitPackage reader = Written(p =>
        {
            p.WriteByte(0x2A);
            p.WriteString("stage1");
            p.WriteVarLong(-300);
            p.WriteFloat(2.5f);
            p.WriteFixedString("SID");
            p.WriteVarULong(128);
        });

        Assert.Equal((byte)0x2A, reader.ReadByte());
        Assert.Equal("stage1", reader.ReadString());
        Assert.Equal(-300L, reader.ReadVarLong());
        Assert.Equal(2.5f, reader.ReadFloat());
        Assert.Equal("SID", reader.ReadFixedString(3));
        Assert.Equal(128UL, reader.ReadVarULong());
    }

    /// <summary>A stream that never fills the caller's buffer — Android's asset streams behave this way, and a
    /// single-call <c>Stream.Read</c> there used to truncate every stage load (see the comment in
    /// <see cref="BitPackage.Read(int)"/>). This keeps the read loop honest.</summary>
    private sealed class ShortReadStream(byte[] bytes, int maxChunk) : MemoryStream(bytes)
    {
        public override int Read(byte[] buffer, int offset, int count) =>
            base.Read(buffer, offset, Math.Min(count, maxChunk));
    }

    [Fact]
    public void Reads_survive_a_stream_that_only_returns_one_byte_at_a_time()
    {
        byte[] bytes = BytesOf(p =>
        {
            p.WriteString("nikitos");
            p.WriteVarLong(100000);
            p.WriteFloat(1.25f);
        });
        using BitPackage reader = BitPackage.GetStreamReadPackage(new ShortReadStream(bytes, 1));

        Assert.Equal("nikitos", reader.ReadString());
        Assert.Equal(100000L, reader.ReadVarLong());
        Assert.Equal(1.25f, reader.ReadFloat());
    }

    [Fact]
    public void Reading_past_the_end_throws_EndOfStream()
    {
        using BitPackage reader = BitPackage.OpenReadMemoryPackage([0x01]);
        Assert.Throws<EndOfStreamException>(() => reader.Read(2));
    }
}
