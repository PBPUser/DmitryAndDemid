using System.Text.Json.Serialization;

namespace DmitryAndDemid.Data;

public class BulletSpawnInfo : ChapterElement
{
    [JsonInclude] public int X = 0;
    [JsonInclude] public int Y = 0;
    [JsonInclude] public string BulletUpdateMethod = "Action1";
    [JsonInclude] public string BulletVisual = "Default";
    [JsonInclude] public string BulletCreateMethod = "WritePlayerPosition";
    [JsonInclude] public float Speed = 0;
}