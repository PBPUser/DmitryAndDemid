using Lua.Internal;

namespace DmitryAndDemid.Data;

public struct Drop
{
    public bool DropHeart = false;
    public bool DropHeartPiece = false;
    public bool DropStar = false;
    public bool DropStarPiece = false;
    public bool DropFullPower = false;
    public byte DropLargePower = 0;
    public byte DropPower = 0;
    public byte DropScore = 0;

    public Drop()
    {
        
    }

    public Drop(int from)
    {
        DropScore = (byte)(from >> 24);
        DropPower = (byte)(from >> 16);
        DropLargePower = (byte)(from >> 8);
        DropFullPower = (from & 0x10) == 0x10;
        DropStarPiece = (from & 0x8) == 0x8;
        DropStar = (from & 0x4) == 0x4;
        DropHeartPiece = (from & 0x2) == 0x2;
        DropHeart = (from & 0x1) == 0x1;
    }

    public int ToInt32()
    {
        int val = DropScore << 8;
        val |= DropPower;
        val <<= 8;
        val |= DropLargePower;
        val <<= 8;
        val |= DropFullPower ? 0x10 : 0;
        val |= DropStarPiece ? 0x8 : 0;
        val |= DropStar ? 0x4 : 0;
        val |= DropHeartPiece ? 0x2 : 0; 
        val |= DropHeart ? 0x1 : 0; 
        return val;
    }
}