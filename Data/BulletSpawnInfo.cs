using System.Numerics;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace DmitryAndDemid.Data;

public class BulletSpawnInfo : ChapterElement
{
    public static Dictionary<string, BulletSpawnInfo> Prefabs = new();

    static BulletSpawnInfo()
    {
        foreach (var file in Directory.GetFiles("Assets/Data/BulletPresets", "*.json"))
        {
            Prefabs[Path.GetFileNameWithoutExtension(file)] = JsonSerializer
                .Deserialize<BulletSpawnInfo>(File.ReadAllText(file));
        }
    }
    
    [JsonInclude] public string BulletVisual = "Default";
    [JsonInclude] public string BulletActionClass = "";
    [JsonInclude] public string[] Args = [];
    
    [JsonInclude] public float Speed = 0;
    [JsonInclude] public float Damage = 0;
}