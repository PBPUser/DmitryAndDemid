using DmitryAndDemid.Utils;

namespace DmitryAndDemid.Data.Archive;

public class FileChapterInfo
{
    public FileChapterInfo()
    {
        
    }

    public FileChapterInfo(FileChapterInfo other)
    {
        Header = other.Header;
        Dialogs = other.Dialogs;
        Id = other.Id;
        SpellcardTitle = other.SpellcardTitle;
        BossName = other.BossName;
        CreateScript = other.CreateScript;
        UpdateScript = other.UpdateScript;
        TimeoutCard = other.TimeoutCard;
        BossInvincible = other.BossInvincible;
        HasDialogs = other.HasDialogs;
        UseUpdateScript = other.UseUpdateScript;
        UseCreateScript = other.UseCreateScript;
        SpellcardTexture = other.SpellcardTexture;
    }
    
    public int[] Header = new int[8];
    public FileDialogInfo[] Dialogs = [];
    public string Id = "";
    public string SpellcardTitle = "";
    public string BossName = "";
    public string CreateScript = "";
    public string UpdateScript = "";
    public string SpellcardTexture = "";
    public string SpellcardShader = "";

    public bool TimeoutCard = false;
    public bool BossInvincible = false;
    public bool HasDialogs = false;
    public bool UseUpdateScript = true;
    public bool UseCreateScript = false;
    public bool ApplyShader = false;
    
    public void Save(ref BitPackage package)
    {
        package.WriteString(Id??"");
        Header[1] = TimeoutCard ? 1 : 0;
        Header[1] |= BossInvincible ? 0x2 : 0;
        Header[1] |= HasDialogs ? 0x4 : 0;
        Header[1] |= UseUpdateScript ? 0x8 : 0;
        Header[1] |= UseCreateScript ? 0x10 : 0;
        Header[1] |= ApplyShader ? 0x20 : 0;
        if(HasDialogs && Header[0] != 3)
            Header[4] = Dialogs.Length;
        for(int i =0;i<Header.Length;i++)
            package.WriteVarLong(Header[i]);
        package.WriteString(SpellcardTitle);
        if(Header[0]>1)
            package.WriteString(BossName);
        if(UseUpdateScript)
            package.WriteString(UpdateScript);
        if(UseCreateScript)
            package.WriteString(CreateScript);
        if (Header[0] != 3 && HasDialogs)
            for(int i = 0; i < Dialogs.Length; i++)
                Dialogs[i].Save(ref package);
        if(Header[0] == 3)
            package.WriteString(SpellcardTexture);
        if(ApplyShader)
            package.WriteString(SpellcardShader);
    }

    public static FileChapterInfo Load(ref BitPackage package)
    {
        FileChapterInfo chapterInfo = new();
        chapterInfo.Id = package.ReadString();
        for (int i = 0; i < chapterInfo.Header.Length; i++)
            chapterInfo.Header[i] = (int)package.ReadVarLong();
        chapterInfo.TimeoutCard = (chapterInfo.Header[1] & 1) == 1;
        chapterInfo.BossInvincible = (chapterInfo.Header[1] & 2) == 2;
        chapterInfo.HasDialogs = (chapterInfo.Header[1] & 4) == 4;
        chapterInfo.UseUpdateScript = (chapterInfo.Header[1] & 8) == 8;
        chapterInfo.UseCreateScript = (chapterInfo.Header[1] & 16) == 16;
        chapterInfo.ApplyShader = (chapterInfo.Header[1] & 32) == 32;
        chapterInfo.SpellcardTitle = package.ReadString();
        if(chapterInfo.Header[0]  > 1)
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
        if(chapterInfo.Header[0] == 3)
            chapterInfo.SpellcardTexture = package.ReadString();
        if(chapterInfo.ApplyShader)
            chapterInfo.SpellcardShader = package.ReadString();
        return chapterInfo;
    }
}