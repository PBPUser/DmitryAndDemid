using DmitryAndDemid.Gameplay;

namespace DmitryAndDemid.Data;

public class CompiledChapterInformation
{
    public ChapterType Type = ChapterType.Default;
    public int ChapterLength = 25;
    public GameObject[] GameObjects;
    public int[] Difficulty = [0,1,2,3,4];
    
    public static CompiledChapterInformation Load(byte[] bytes)
    {
        return null;
    }

    public byte[] Serialize()
    {
        Utils.BitPackage bitPackage = new();
        bitPackage.WriteVarLong((int)Type);
        bitPackage.WriteVarLong((int)GameObjects.Length);
        return bitPackage.Export();
    }
}