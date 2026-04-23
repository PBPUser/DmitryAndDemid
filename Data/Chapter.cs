using System.Linq;

using System.Text.Json.Serialization;
using System.Xml;

namespace DmitryAndDemid.Data;

public class ChapterInfo : StageElement
{
    [JsonInclude]
    public ChapterType Type = ChapterType.NonBoss;

    [JsonInclude]
    public string ChapterLabel = "";

    [JsonInclude]
    public string ChapterBossArt = "";

    [JsonInclude]
    public int ChapterLength = 25;

    [JsonInclude]
    public BulletSpawnInfo[] BulletSpawnInfos = new BulletSpawnInfo[0];

    [JsonInclude] public int[] Difficulty = [0,1,2,3];
}

public class ChapterElement
{
    [JsonInclude]
    public int SpawnTick = 0;
}

public class BulletSpawnInfo : ChapterElement
{
    [JsonInclude] public int X = 0;
    [JsonInclude] public int Y = 0;
    [JsonInclude] public string BulletUpdateMethod = "Action1";
    [JsonInclude] public string BulletVisual = "Default";
    [JsonInclude] public string BulletCreateMethod = "WritePlayerPosition";
    [JsonInclude] public float Speed = 0;
}

public class Chapter : StageElement
{
    public Chapter(Game g, ChapterInfo info)
    {
        ChapterLength = info.ChapterLength;
        Index = info.Index;
        Bullets = info.BulletSpawnInfos.Select(x => new Bullet(g, x)).OrderBy(x => x.SpawnTick).ToArray();
    }

    public int ChapterLength;
    public double ChapterStartedAt = 0;
    public Bullet[] Bullets;
}
