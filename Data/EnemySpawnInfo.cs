using System.Text.Json.Serialization;

namespace DmitryAndDemid.Data;

public class EnemySpawnInfo : ChapterElement
{
    [JsonInclude] public string Visual = "";
    [JsonInclude] public EnemyActionInfo[] Actions = [];
    [JsonInclude] public float Speed = 1f;
    [JsonInclude] public float BulletSpeed = 1f;
    [JsonInclude] public float Health = 20f;
    [JsonInclude] public float BulletSpawnRate = 20f;
    [JsonInclude] public int ShootStreamCount = 0;
    [JsonInclude] public float AngleBetweenStreams = 0f;
    [JsonInclude] public int DropPowerPointsCount = 0;
    [JsonInclude] public int DropLargePowerPointsCount = 0;
    [JsonInclude] public int DropScorePointsCount = 0;
    [JsonInclude] public int DropHealthCount = 0;
    [JsonInclude] public int DropHealthPeacesCount = 0;
    [JsonInclude] public int DropBombCount = 0;
    [JsonInclude] public int DropBombPeacesCount = 0;
}

public class EnemyActionInfo
{
    [JsonInclude] public string Class = "";
    [JsonInclude] public string[] Args = [];
}