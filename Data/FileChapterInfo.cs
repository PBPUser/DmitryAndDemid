using DmitryAndDemid.Utils;

namespace DmitryAndDemid.Data;

public class FileChapterInfo
{
    public int[] Header = new int[16];
    public string Name = "";
    public string BossName = "";

    public bool TimeoutCard = false;
    public bool BossInvincible = false;

    public void Save(ref BitPackage bitPackage)
    {
        Header[4] = TimeoutCard ? 1 : 0;
        Header[4] |= BossInvincible ? 0x2 : 0;
        for(int i = 0; i < 16; i++)
            bitPackage.WriteVarLong(Header[i]);
        bitPackage.WriteString(Name);
        bitPackage.WriteString(BossName);
    }

    public static FileChapterInfo Load(ref BitPackage bitPackage)
    {
        FileChapterInfo chapterInfo = new();
        for (int i = 0; i < 16; i++)
            chapterInfo.Header[i] = (int)bitPackage.ReadVarLong();
        chapterInfo.TimeoutCard = (chapterInfo.Header[4] & 1) == 1;
        chapterInfo.BossInvincible = (chapterInfo.Header[4] & 2) == 2;
        chapterInfo.Name = bitPackage.ReadString();
        chapterInfo.BossName = bitPackage.ReadString();
        return chapterInfo;
    }
}