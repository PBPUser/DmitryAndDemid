using System.Text.Json.Serialization;

namespace DmitryAndDemid.Data;

public class EnemySpawnInfo : StageElement
{
    [JsonInclude] public string EntityAppearance = "";
    [JsonInclude] public string EntityScript = "";
    [JsonInclude] public float EntitySpeed = 1f;
    [JsonInclude] public int X = 0;
    [JsonInclude] public int Y = 0;
}