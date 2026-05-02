using System.Numerics;
using System.Text.Json;
using System.Text.Json.Serialization;
using Raylib_cs;

namespace DmitryAndDemid.Data;

public class BulletVisual
{
    public static Dictionary<string, BulletVisual> Constants = new Dictionary<string, BulletVisual>();

    static BulletVisual()
    {
        foreach (var file in Directory.GetFiles("Assets/Data/BulletVisuals", "*.json"))
            Constants[Path.GetFileNameWithoutExtension(file)] = JsonSerializer.Deserialize<BulletVisual>(File.ReadAllText(file), new JsonSerializerOptions {IncludeFields= true});
    }
    
    [JsonInclude] public string Texture;
    [JsonInclude] public Vector2 Collision;
    [JsonInclude] public Vector2 RenderSize;
    [JsonInclude] public Vector2 SourcePosition = new Vector2(0, 0);
    [JsonInclude] public Vector2? SourceSize = null;
    [JsonInclude] public string Effect = "";
}