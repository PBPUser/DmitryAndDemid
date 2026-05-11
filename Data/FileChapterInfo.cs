using DmitryAndDemid.Utils;

namespace DmitryAndDemid.Data;

public class FileChapterInfo
{
    public int[] Header = new int[16];
    public FileDialogInfo[] DialogInfo = [];
    public string Name = "";
    public string BossName = "";

    public bool TimeoutCard = false;
    public bool BossInvincible = false;
    public bool HasDialogs = false;

    public void Save(ref BitPackage bitPackage)
    {
        Header[4] = TimeoutCard ? 1 : 0;
        Header[4] |= BossInvincible ? 0x2 : 0;
        Header[4] |= HasDialogs ? 0x2 : 0;
        if(HasDialogs && Header[0] != 3)
            Header[5] = DialogInfo.Length;
        for(int i = 0; i < 16; i++)
            bitPackage.WriteVarLong(Header[i]);
        bitPackage.WriteString(Name);
        bitPackage.WriteString(BossName);
        if (HasDialogs && Header[0] != 3)
        {
            for(int i = 0; i < DialogInfo.Length; i++)
                DialogInfo[i].Save(ref bitPackage);
        }
    }

    public static FileChapterInfo Load(ref BitPackage bitPackage)
    {
        FileChapterInfo chapterInfo = new();
        for (int i = 0; i < 16; i++)
            chapterInfo.Header[i] = (int)bitPackage.ReadVarLong();
        chapterInfo.TimeoutCard = (chapterInfo.Header[4] & 1) == 1;
        chapterInfo.BossInvincible = (chapterInfo.Header[4] & 2) == 2;
        chapterInfo.HasDialogs = (chapterInfo.Header[4] & 4) == 4;
        chapterInfo.Name = bitPackage.ReadString();
        chapterInfo.BossName = bitPackage.ReadString();
        if (chapterInfo.Header[0] != 3 && chapterInfo.HasDialogs)
        {
            chapterInfo.DialogInfo = new FileDialogInfo[chapterInfo.Header[5]];
            for(int i = 0; i < chapterInfo.Header[5]; i++)
                chapterInfo.DialogInfo[i] = FileDialogInfo.Load(ref bitPackage);
        }
        return chapterInfo;
    }
}