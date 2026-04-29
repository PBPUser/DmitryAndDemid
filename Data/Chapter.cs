using System.Linq;
using System.Xml;
using DmitryAndDemid.Gameplay;
using DmitryAndDemid.Utils;
using Raylib_cs;

namespace DmitryAndDemid.Data;

public class Chapter : StageElement
{
    public Chapter(Game g, ChapterInfo info)
    {
        Type = info.Type;
        ChapterLength = info.ChapterLength;
        Index = info.Index;
        Bullets = info.BulletSpawnInfos.SelectMany(x => GetBulletsFromInfo(g, x)).OrderBy(x => x.SpawnTick).ToArray();
        Enemies = info.EnemySpawnInfos.SelectMany(x => GetEnemiesFromInfo(g, x)).OrderBy(x => x.SpawnTick).ToArray();
        BossActions = info.BossActionInfos;
        if (Type == ChapterType.Spell)
        {
            ChapterAttackTexture = Runtime.CurrentRuntime.Textures[info.ChapterBossArt];
            ChapterTitleTexture = Helper.DrawText(info.ChapterLabel, 4, 0, 0, 2, Raylib.GetFontDefault(), Color.Red, "shadow");
        }
    }

    public Texture2D ChapterAttackTexture;
    public RenderTexture2D ChapterTitleTexture;
    public int ChapterLength;
    public double ChapterStartedAt = 0;  
    public Bullet[] Bullets;
    public Enemy[] Enemies;
    public ChapterType Type = ChapterType.NonBoss;
    public BossActionInfo[] BossActions;

    static Enemy[] GetEnemiesFromInfo(Game g, EnemySpawnInfo info)
    {
        if (!info.Stacked)
            return [new Enemy(g, info, 0)];
        if (info.StackLength <= 0)
        {
            Console.WriteLine("Invalid Stack Length");
            throw new Exception();
        }
        Enemy[] enemies=new Enemy[info.StackLength];
        for(int i = 0; i < info.StackLength; i++)
            enemies[i] = new Enemy(g, info,i);
        return enemies;
    }
    
    static Bullet[] GetBulletsFromInfo(Game g,BulletSpawnInfo info)
    {
        if(!info.Stacked)
            return [new Bullet(g, info, 0, true)];
        if (info.StackLength <= 0)
        {
            Console.WriteLine("Invalid Stack Length");
            throw new Exception();
        }
        Bullet[] bullets = new Bullet[info.StackLength];
        for(int i = 0; i < info.StackLength; i++)
            bullets[i] = new Bullet(g, info,i, true);
        return bullets;
    }
}
