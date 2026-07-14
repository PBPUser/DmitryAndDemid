using DmitryAndDemid.Rendering;
namespace DmitryAndDemid.Data;

public struct DropScenario
{
    public DropScenario()
    {
        
    }
    
    public byte LargePowerPoints = 0;
    public byte PowerPoints = 0;
    public byte ScorePoints = 0;
    public bool DropHeart = false;
    public bool DropHeartPiece = false;
    public bool DropStar = false;
    public bool DropStarPiece = false;

    public int ImportExport
    {
        get
        {
            int i = LargePowerPoints;
            i <<= 8;
            i |= PowerPoints;
            i <<= 8;
            i |= ScorePoints;
            i <<= 8;
            byte b = 0;
            if (DropHeart)
                b |= 1;
            b <<= 1;
            if(DropHeartPiece)
                b |= 1;
            b <<= 1;
            if (DropStar)
                b |= 1;
            b <<= 1;
            if(DropStarPiece)
                b |= 1;
            i |= b;
            return i;
        }
        set
        {
            int i = value;
            byte b = (byte)i;
            DropStarPiece = (b & 0x1) == 0x1; 
            DropStar = (b & 0x2) == 0x2; 
            DropHeartPiece = (b & 0x4) == 0x4; 
            DropHeart = (b & 0x8) == 0x8; 
            i >>= 8;
            ScorePoints = (byte)i;
            i >>= 8;
            PowerPoints = (byte)i;
            i >>= 8;
            LargePowerPoints = (byte)i;
        }
    }
}