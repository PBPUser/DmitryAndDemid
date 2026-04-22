

using System.Text.Json.Serialization;

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
    public BulletSpawnInfo[] BulletSpawnInfos;
}

public class ChapterElement
{
    [JsonInclude]
    public int SpawnTick = 0;
}

public class BulletSpawnInfo : ChapterElement
{
    [JsonInclude]
    public int X = 0;
    [JsonInclude]
    public int Y = 0;
    [JsonInclude]
    public string BulletUpdateMethod = "";
}

public class Chapter : StageElement
{
    public Chapter(ChapterInfo info)
    {
        ChapterLength = info.ChapterLength;
        Index = info.Index;
    }

    public int ChapterLength;
    public double ChapterStartedAt = 0;
}
