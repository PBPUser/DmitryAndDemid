using DmitryAndDemid.Data.Archive;
using DmitryAndDemid.Utils;

namespace DmitryAndDemid.Data.Archive;

public class FileStageInfo
{
    public FileStageInfo()
    {
        Scripts = new string[0];
        Chapters = new FileChapterInfo[0];
        Entities = new FileEntityInfo[0];
        Backgrounds = new string[0];
    }
    
    public int[] Header = new int[16];
    public string[] Scripts;
    public FileChapterInfo[] Chapters;
    public FileEntityInfo[] Entities;
    public string[] Backgrounds;

    public static FileStageInfo Load(ref BitPackage bitPackage)
    {
        FileStageInfo info = new FileStageInfo();
        for (int i = 0; i < 16; i++)
            info.Header[i] = (int)bitPackage.ReadVarLong();
        info.Scripts = new string[info.Header[0x3]];
        for(int i = 0; i < info.Header[0x3]; i++)
            info.Scripts[i] = bitPackage.ReadString();
        info.Backgrounds = new string[info.Header[0x4]];
        for(int i = 0; i < info.Header[0x4]; i++)
            info.Backgrounds[i] = bitPackage.ReadString();
        info.Entities = new FileEntityInfo[info.Header[0x5]];
        for (int i = 0; i < info.Header[0x5]; i++)
            info.Entities[i] = FileEntityInfo.Load(ref bitPackage);
        info.Chapters = new FileChapterInfo[info.Header[0x6]];
        for(int i = 0; i < info.Header[0x6]; i++)
            info.Chapters[i] = FileChapterInfo.Load(ref bitPackage);
        return info;
    }

    public void Save(ref BitPackage bitPackage)
    {
        Header[3] = Scripts.Length;
        Header[4] = Backgrounds.Length;
        Header[5] = Entities.Length;
        Header[6] = Chapters.Length;
        for(int i = 0; i < 16; i++)
            bitPackage.WriteVarLong(Header[i]);
        for(int i = 0; i < Scripts.Length; i++)
            bitPackage.WriteString(Scripts[i]);
        for(int i = 0; i < Backgrounds.Length; i++)
            bitPackage.WriteString(Backgrounds[i]);
        for(int i = 0; i < Entities.Length; i++)
            Entities[i].Save(ref bitPackage);
        for(int i = 0; i < Chapters.Length; i++)
            Chapters[i].Save(ref bitPackage);
    }
}