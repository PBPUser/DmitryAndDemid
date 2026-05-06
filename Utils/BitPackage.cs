using System.Text;

namespace DmitryAndDemid.Utils;

public class BitPackage
{
    public BitPackage()
    {
        BytesWrite = new();
    }

    public BitPackage(byte[] bytes)
    {
        Bytes = bytes;
    }

    private const byte ContinueByte = 0x80;
    private byte[] Bytes;
    private int Position = 0;
    private List<byte> BytesWrite;
    
    public byte[] Export() => BytesWrite.ToArray();
    
    #region Reader
    public byte[] Read(int count)
    {
        if (Bytes.Length < count + Position)
            throw new IndexOutOfRangeException();
        return Bytes.Skip(Position).Take(count).ToArray();
    }
    
    public byte ReadByte() => Bytes[Position++];

    public string ReadString()
    {
        byte[] length = Read(4);
        return "";
    }
    
    public string ReadFixedString(int length)
    {
        byte[] bytes = Read(length);
        return Encoding.ASCII.GetString(bytes);
    }

    public long ReadVarLong()
    {
        long value = 0;
        byte b;
        do
        {
            b = ReadByte();
            value |= b;
            value >>= 7;
        } while ((b & 0x80) == 0x80);

        return 0;
    }
    #endregion
    #region Writer
    public void Write(byte[] bytes)
    {
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
        byte[] stringSize=BitConverter.GetBytes(bytes.Length);
        Write(stringSize);
        Write(bytes);
    }

    public void WriteVarLong(long value)
    {
        List<byte> bytes = new();
        byte c = 0;
        for (int i = 0; i < 8; i++)
        {
            c = (byte)(value % ContinueByte);
            if(value != c)
                c |= ContinueByte;
            bytes.Add(c);
            value <<= 7;
        }
        Write(bytes.ToArray());
    }
    #endregion
}