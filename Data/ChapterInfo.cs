using System.Text.Json.Serialization;

namespace DmitryAndDemid.Data;

public class ChapterInfo : StageElement
{
    [JsonInclude] public ChapterType Type = ChapterType.NonBoss;
    [JsonInclude] public string ChapterLabel = "";
    [JsonInclude] public string ChapterBossArt = "";
    [JsonInclude] public int ChapterLength = 25;
    [JsonInclude] public BulletSpawnInfo[] BulletSpawnInfos = new BulletSpawnInfo[0];
    [JsonInclude] public EnemySpawnInfo[] EnemySpawnInfos = new EnemySpawnInfo[0];
    [JsonInclude] public int[] Difficulty = [0,1,2,3];
    [JsonInclude] public BossActionInfo[] BossActionInfos = new BossActionInfo[0];
}