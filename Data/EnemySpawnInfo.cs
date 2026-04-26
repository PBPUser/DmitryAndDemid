using System.Text.Json.Serialization;

namespace DmitryAndDemid.Data;

public class EnemySpawnInfo : ChapterElement
{
    [JsonInclude] public string Visual = "";
    [JsonInclude] public string Script = "";
    [JsonInclude] public string CreateScript = "";
    [JsonInclude] public string AttackScript = "";
    [JsonInclude] public float Speed = 1f;
    [JsonInclude] public float BulletSpeed = 1f;
    [JsonInclude] public float Health = 20f;
    [JsonInclude] public float BulletSpawnRate = 20f;
}