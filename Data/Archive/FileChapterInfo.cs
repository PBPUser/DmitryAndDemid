using DmitryAndDemid.Utils;

namespace DmitryAndDemid.Data.Archive;

public class FileChapterInfo
{
    public int[] Header = new int[8];
    public FileDialogInfo[] Dialogs = [];
    public string Name = "";
    public string BossName = "";
    public string CreateScript = "";
    public string UpdateScript = "";

    public bool TimeoutCard = false;
    public bool BossInvincible = false;
    public bool HasDialogs = false;
    public bool UseUpdateScript = true;
    public bool UseCreateScript = false;

    public void Save(ref BitPackage package)
    {
        Header[1] = TimeoutCard ? 1 : 0;
        Header[1] |= BossInvincible ? 0x2 : 0;
        Header[1] |= HasDialogs ? 0x4 : 0;
        Header[1] |= UseUpdateScript ? 0x8 : 0;
        Header[1] |= UseCreateScript ? 0x10 : 0;
        if(HasDialogs && Header[0] != 3)
            Header[4] = Dialogs.Length;
        for(int i =0;i<Header.Length;i++)
            package.WriteVarLong(Header[i]);
        package.WriteString(Name);
        if(Header[0]==3)
            package.WriteString(BossName);
        if(UseUpdateScript)
            package.WriteString(UpdateScript);
        if(UseCreateScript)
            package.WriteString(CreateScript);
        if (Header[0] != 3 && HasDialogs)
            for(int i = 0; i < Dialogs.Length; i++)
                Dialogs[i].Save(ref package);
    }

    public static FileChapterInfo Load(ref BitPackage package)
    {
        FileChapterInfo chapterInfo = new();
        for (int i = 0; i < chapterInfo.Header.Length; i++)
            chapterInfo.Header[i] = (int)package.ReadVarLong();
        chapterInfo.TimeoutCard = (chapterInfo.Header[1] & 1) == 1;
        chapterInfo.BossInvincible = (chapterInfo.Header[1] & 2) == 2;
        chapterInfo.HasDialogs = (chapterInfo.Header[1] & 4) == 4;
        chapterInfo.UseUpdateScript = (chapterInfo.Header[1] & 8) == 8;
        chapterInfo.UseCreateScript = (chapterInfo.Header[1] & 16) == 16;
        chapterInfo.Name = package.ReadString();
        if(chapterInfo.Header[0] == 3)
            chapterInfo.BossName = package.ReadString();
        if (chapterInfo.UseUpdateScript)
            chapterInfo.UpdateScript = package.ReadString();
        if(chapterInfo.UseCreateScript)
            chapterInfo.CreateScript = package.ReadString();
        if (chapterInfo.Header[0] != 3 && chapterInfo.HasDialogs)
        {
            chapterInfo.Dialogs = new FileDialogInfo[chapterInfo.Header[4]];
            for(int i = 0; i < chapterInfo.Header[4]; i++)
                chapterInfo.Dialogs[i] = FileDialogInfo.Load(ref package);
        }
        return chapterInfo;
    }
}