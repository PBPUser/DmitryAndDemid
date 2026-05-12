using DmitryAndDemid.Data;
using DmitryAndDemid.Data.Archive;
using Raylib_cs;

namespace DmitryAndDemid.Gameplay;

public class RuntimeChapterInfo
{
    public Texture2D? SpellcardTitleTexture;
    public Texture2D? BossTitleTexture;
    private int[] Header;
    public bool TimeoutCard => (Header[4] & 0x1) == 0x1;
    public bool BossInvincible => (Header[4] & 0x2) == 0x2;
    public int MaxScore => Header[6];
    public Drop GoodDrop => new Drop(Header[7]);
    public Drop BadDrop => new Drop(Header[8]);
    

    public static RuntimeChapterInfo LoadFromFile(FileChapterInfo fileChapterInfo)
    {
        RuntimeChapterInfo runtimeChapterInfo = new();
        runtimeChapterInfo.Header = fileChapterInfo.Header;
        // TODO: Load title textures
        
        return runtimeChapterInfo;
    }
}