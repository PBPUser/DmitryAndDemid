using static DmitryAndDemid.Rendering.Gfx;
using System.Text;
using DmitryAndDemid.Data.Archive;

namespace DmitryAndDemid.Utils;

public class BitPackage : IDisposable, IAsyncDisposable
{
    public BitPackage()
    {
        BytesWrite = new();
    }

    public BitPackage(byte[] bytes)
    {
        Bytes = bytes;
    }

    public static BitPackage GetStreamReadPackage(Stream stream)
    {
        BitPackage package = new();
        package.Stream = stream;
        return package;
    }
    
    public static BitPackage OpenReadMemoryPackage(byte[] bytes) => GetStreamReadPackage(new MemoryStream(bytes));
    public static BitPackage OpenWriteMemoryPackage(byte[] bytes) => new BitPackage();
    public static BitPackage OpenStreamReadPackage(string file) => GetStreamReadPackage(File.OpenRead(file));
    public static BitPackage OpenStreamWritePackage(string file) => GetStreamReadPackage(File.OpenWrite(file));

    private Stream? Stream =null;
    private const byte ContinueByte = 0x80;
    private const byte NegativeFlagByte = 0x40;
    private byte[] Bytes;
    private int Position = 0;
    private List<byte> BytesWrite;
    
    public byte[] Export() => BytesWrite.ToArray();
    #if DEBUG
    public void TransferDataToRead() => Bytes = BytesWrite.ToArray();
    #endif
    
    #region Reader
    public byte[] Read(int count)
    {
        if (Stream != null)
        {
            byte[] b = new byte[count];
            // Stream.Read may return FEWER than `count` bytes on a single call even when the stream is not at
            // its end (it is only obliged to return at least one). A short read is NOT end-of-stream, so loop
            // until we have all `count` bytes; only a return of 0 means true EOF. Reading the whole chunk in one
            // call happened to work on desktop FileStreams but broke on Android's asset streams (partial reads),
            // where every stage load then threw EndOfStreamException — worse the larger the file.
            int total = 0;
            while (total < count)
            {
                int c = Stream.Read(b, total, count - total);
                if (c <= 0)
                    throw new EndOfStreamException();
                total += c;
            }
            try
            {
                return b;
            }
            finally
            {
                if(!Stream.CanRead)
                    Dispose();
            }
        }
        if (Bytes.Length < count + Position)
            throw new IndexOutOfRangeException();
        try
        {
            return Bytes.Skip(Position).Take(count).ToArray();
        }
        finally
        {
            Position += count;
        }
    }

    public byte ReadByte() => Read(1)[0];

    public string ReadString()
    {
        ulong bytesLength = ReadVarULong();
        return Encoding.UTF8.GetString(Read((int)bytesLength));
    }
    
    public string ReadFixedString(int length)
    {
        byte[] bytes = Read(length);
        return Encoding.ASCII.GetString(bytes);
    }

    public float ReadFloat()
    {
        byte[] bytes = Read(4);
        return BitConverter.ToSingle(bytes, 0);
    }

    public long ReadVarLong()
    {
        byte b = ReadByte();
        if ((b & ContinueByte) != ContinueByte)
            return b;
        long value = 0;
        List<byte> bytes = new();
        bytes.Add((byte)(b & ~ContinueByte));
        b = ReadByte();
        bool isNegative = (b & NegativeFlagByte) == NegativeFlagByte;
        value = (byte)(b & ~0b1100_0000);
        value <<= 7;
        value |= bytes[0];
        if ((b & 0x80) != 0x80)
        {
            if (isNegative)
                value *= -1;
            return value;
        }
        bytes.Add((byte)(b & ~0b1100_0000));
        b = ReadByte();
        int length = b >> 5;
        bytes.Add((byte)(b & ~0b1110_0000));
        long value2 = 0;
        for (int i = 0; i < length; i++)
        {
            bytes.Add(ReadByte());
        }
        foreach (var bx in bytes.TakeLast(bytes.Count - 3).Reverse())
        {
            value2 |= bx;
            value2 <<= 8;
        }
        if (bytes.Count > 3)
            value2 >>= 8;
        value2 <<= 5;
        value2 |= bytes[2];
        value2 <<= 13;
        value2 |= value;
        if(isNegative)
            value2 *= -1;
        return value2;
    }
    
    public ulong ReadVarULong()
    {
        ulong value = 0;
        byte b;
        do
        {
            b = ReadByte();
            value |= (ulong)(b % ContinueByte);
            if((b & 0x80) == 0x80)
                value <<= 7;
        } while ((b & 0x80) == 0x80);
        return value;
    }

    public (int x, int y) ReadPosition()
    {
        byte[] bytes = Read(3);
        int x = bytes[1];
        int y = bytes[2];
        if (0x80 == (bytes[0] & 0x80))
            x += 0x100;
        if(0x40==(bytes[0] & 0x40))
            y += 0x100;
        return (x, y);
    }
    #endregion
    #region Writer
    public void Write(byte[] bytes)
    {
        if (Stream != null)
            if (Stream.CanWrite)
            {
                Stream.Write(bytes, 0, bytes.Length);
                return;
            }
        BytesWrite.AddRange(bytes);
    }

    public void WriteFixedString(string s)
    {
        byte[] bytes = Encoding.ASCII.GetBytes(s);
        Write(bytes);
    }

    public void WriteString(string s)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(s);
        WriteVarULong((ulong)bytes.Length);
        Write(bytes);
    }

    public void WriteVarLong(long value)
    {
        List<byte> bytes = new();
        bool isNegative = value < 0;
        if (isNegative)
            value *= -1;
        bytes.Add((byte)(value & ~ContinueByte));
        value >>= 7;
        if (value == 0 && !isNegative)
        {
            Write(bytes.ToArray());
            return;
        }
        bytes[0] |= ContinueByte;
        bytes.Add((byte)(value & ~0b1100_0000));
        if (isNegative)
            bytes[1] |= NegativeFlagByte;
        value >>= 6;
        if (value == 0)
        {
            Write(bytes.ToArray());
            return;
        }
        bytes[1]|=ContinueByte;
        bytes.Add((byte)(value & ~0b1110_0000));
        value >>= 5;
        while (value != 0)
        {
            bytes.Add((byte)value);
            value >>= 8;
        }
        int length = bytes.Count - 3;
        bytes[2] |= (byte)(length << 5);
        Write(bytes.ToArray());
    }   
    
    /// <summary>Goes through <see cref="Write(byte[])"/> like every other writer. It used to append straight to
    /// <see cref="BytesWrite"/>, which silently dropped the byte on a stream-backed package (the write path
    /// <c>StageEditorScreen.Save</c> and <see cref="CpuImage.Save"/> both use) — the in-memory list is never
    /// flushed to a stream, only handed back by <see cref="Export"/>.</summary>
    public void WriteByte(byte value) => Write([value]);
    public void WriteVarULong(ulong value)
    {
        List<byte> bytes = new();
        byte c = 0;
        for (int i = 0;; i++)
        {
            c = (byte)((value % ContinueByte) | ContinueByte);
            bytes.Add(c);
            value >>= 7;
            if (value == 0)
                break;
        }
        bytes.Reverse();
        bytes[^1] ^= ContinueByte;
        Write(bytes.ToArray());
    }

    public void WriteFloat(float value)
    {
        byte[] bytes = BitConverter.GetBytes(value);
        Write(bytes);
    }
    
    public void WritePlayAreaPosition((int x, int y) position)
    {
        position.x += 32;
        position.y += 32;
        byte mask = 0;
        if (position.x > 0xff)
            mask |= 0x80;
        if (position.y > 0xff)
            mask |= 0x40;
        byte[] bytesx = BitConverter.GetBytes(position.x%0x100);
        byte[] bytesy = BitConverter.GetBytes(position.y%0x100);
        WriteByte(mask);
        WriteByte(bytesx[0]);
        WriteByte(bytesy[0]);
    }
    
    #endregion

    public void Dispose()
    {
        Stream?.Dispose();
    }

    public async ValueTask DisposeAsync()
    {
        if (Stream != null) await Stream.DisposeAsync();
    }
}